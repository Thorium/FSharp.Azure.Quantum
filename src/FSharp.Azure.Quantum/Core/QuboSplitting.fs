namespace FSharp.Azure.Quantum.Core

open System.Collections.Generic
open System.Threading
open System.Threading.Tasks
open FSharp.Azure.Quantum

/// Solving a problem that is wider than one circuit by splitting it into circuits that fit.
///
/// Two splits, both trading qubits for circuit runs:
///
/// - Conditioning (any QUBO): fix a few variables whose removal breaks the QUBO's interaction
///   graph into pieces that fit, solve every piece for each assignment of the fixed variables
///   it is coupled to, and keep the lowest-energy combination. A piece coupled to b fixed
///   variables costs 2^b runs, so it suits sparse QUBOs; a QUBO whose penalty couples every
///   variable to every other has no small set to fix.
///
/// - Shares (a linear objective under one additive integer constraint, such as a knapsack):
///   cut the items into blocks, find for each block the best value at every exact share of
///   the resource, and join the blocks' tables. A block asked for an exact share is an
///   equality, so it needs no slack bits. The runs grow with the capacity, not with 2^k.
///
/// The caller passes the function that solves or samples a piece, so every circuit runs on
/// the caller's backend; only the bookkeeping that joins the pieces is classical. The joined
/// answer is exact when every piece is solved exactly, and inherits the misses of the pieces
/// otherwise.
module QuboSplitting =

    // ========================================================================
    // CONDITIONING
    // ========================================================================

    /// Limits of a conditioning split.
    [<Struct>]
    type ConditioningLimits =
        {
            /// Widest piece, in qubits
            MaxPieceQubits: int

            /// Most circuit runs: a piece coupled to b fixed variables runs 2^b times
            MaxRuns: int
        }

    /// Most fixed variables one step of the join minimizes over: the step builds a table of
    /// 2^MostJoinSpan energies (8 MB) and keeps one choice bit per entry (128 KB). QUBOs of bounded width (chains, trees, ladders, strips)
    /// stay at a span of 6 or less however many variables are fixed. Square grids pass 20
    /// just below 16 × 16 and random graphs of degree 3 between 64 and 128 variables, where
    /// the runs already number in the hundreds or more.
    [<Literal>]
    let MostJoinSpan = 20

    /// A QUBO solved through pieces.
    type PiecewiseSolution =
        {
            /// One bit per variable of the QUBO
            Bits: int[]

            /// QUBO energy of Bits
            Energy: float

            /// Variables that were fixed to split the QUBO; empty when it ran as one piece
            FixedVariables: int list

            /// Circuits run: each piece once per assignment of the fixed variables it is coupled to
            PiecesSolved: int
        }

    /// Coupling of two variables: both triangles of the matrix count.
    let private coupling (qubo: float[,]) (i: int) (j: int) : float = qubo.[i, j] + qubo.[j, i]

    /// Neighbours of every variable in the interaction graph of the QUBO.
    let private adjacency (qubo: float[,]) : int[][] =
        let n = Array2D.length1 qubo

        Array.init n (fun i ->
            [|
                for j in 0 .. n - 1 do
                    if j <> i && coupling qubo i j <> 0.0 then
                        yield j
            |])

    /// Connected components of the variables marked alive, each in ascending order, in order
    /// of their lowest variable. Breadth-first over arrays: the search for variables to fix
    /// runs this once per candidate, so it has to be linear in the size of the graph.
    let private components (adjacent: int[][]) (alive: bool[]) : int[] list =
        let seen = Array.zeroCreate<bool> alive.Length
        let queue = Queue<int>()

        [
            for start in 0 .. alive.Length - 1 do
                if alive.[start] && not seen.[start] then
                    let part = ResizeArray<int>()
                    seen.[start] <- true
                    queue.Enqueue start

                    while queue.Count > 0 do
                        let v = queue.Dequeue()
                        part.Add v

                        for next in adjacent.[v] do
                            if alive.[next] && not seen.[next] then
                                seen.[next] <- true
                                queue.Enqueue next

                    yield part |> Seq.sort |> Seq.toArray
        ]

    /// Variables to fix so that every connected component of the rest has at most
    /// `maxPieceQubits` variables, sorted. Greedy: while a component is too wide, fix the
    /// variable of it whose removal leaves the smallest largest piece (the one with more
    /// neighbours, then the lower index, among equals), so a chain is cut in the middle and a
    /// hub is taken out of a star.
    let private cutInto (adjacent: int[][]) (maxPieceQubits: int) : int list =
        let alive = Array.create adjacent.Length true
        let widestPiece = max 1 maxPieceQubits

        let rec fix (chosen: int list) =
            let widest =
                components adjacent alive |> List.sortByDescending Array.length |> List.tryHead

            match widest with
            | Some part when part.Length > widestPiece ->
                let inPart = Array.zeroCreate<bool> alive.Length
                part |> Array.iter (fun v -> inPart.[v] <- true)

                let largestWithout (v: int) =
                    inPart.[v] <- false

                    let largest =
                        components adjacent inPart
                        |> List.fold (fun widestLeft piece -> max widestLeft piece.Length) 0

                    inPart.[v] <- true
                    largest

                let degreeInPart (v: int) =
                    adjacent.[v] |> Array.sumBy (fun u -> if inPart.[u] then 1 else 0)

                let cut = part |> Array.minBy (fun v -> largestWithout v, -(degreeInPart v), v)
                alive.[cut] <- false
                fix (cut :: chosen)
            | _ -> List.sort chosen

        fix []

    /// Variables to fix so that every connected component of the rest has at most
    /// `maxPieceQubits` variables: sorted, and empty when the QUBO already fits or falls apart
    /// by itself. It is a heuristic (see cutInto): a smaller set may exist.
    let chooseFixedVariables (maxPieceQubits: int) (qubo: float[,]) : int list =
        cutInto (adjacency qubo) maxPieceQubits

    /// Variables that every cut within the run limit fixes, and a lower bound on the span of
    /// the join over them. A free variable keeps every neighbour in its own piece or among the
    /// fixed variables its piece is coupled to, at most `boundary` of them, so one with more
    /// neighbours than that is fixed. Their couplings stay terms of the join, and no
    /// elimination order spans fewer variables than the degeneracy of those couplings (the
    /// largest least degree met while peeling off a variable of least degree at a time). It
    /// costs a pass per forced variable, so a dense QUBO is refused before the search for a
    /// cut, which costs a traversal per candidate.
    let private forcedJoin (adjacent: int[][]) (maxPieceQubits: int) (boundary: int) : int * int =
        let n = adjacent.Length
        let most = int64 (max 1 maxPieceQubits) - 1L + int64 boundary
        let left = adjacent |> Array.map (fun neighbours -> int64 neighbours.Length > most)
        let forced = left |> Array.sumBy (fun isForced -> if isForced then 1 else 0)

        let degree =
            Array.init n (fun v -> adjacent.[v] |> Array.sumBy (fun u -> if left.[u] then 1 else 0))

        let mutable widest = 0

        for _ in 1..forced do
            let mutable least = -1

            for v in 0 .. n - 1 do
                if left.[v] && (least < 0 || degree.[v] < degree.[least]) then
                    least <- v

            widest <- max widest degree.[least]
            left.[least] <- false

            for u in adjacent.[least] do
                degree.[u] <- degree.[u] - 1

        forced, widest

    /// The QUBO over the free variables when `fixedValues` (variable → 0 or 1) are set: a
    /// fixed variable at 1 turns its couplings into linear terms of its neighbours. Returns
    /// the reduced matrix and, for each of its rows, the variable of the full QUBO.
    let condition (qubo: float[,]) (fixedValues: Map<int, int>) : float[,] * int[] =
        let n = Array2D.length1 qubo

        let free =
            [|
                for i in 0 .. n - 1 do
                    if not (fixedValues.ContainsKey i) then
                        i
            |]

        let ones =
            fixedValues
            |> Map.toList
            |> List.choose (fun (v, value) -> if value = 1 then Some v else None)

        let reduced =
            Array2D.init free.Length free.Length (fun a b ->
                let direct = qubo.[free.[a], free.[b]]

                if a = b then
                    direct + (ones |> List.sumBy (coupling qubo free.[a]))
                else
                    direct)

        reduced, free

    /// The members of `items` whose bit is set in `mask`, bit 0 for the first.
    let private selected (mask: int) (items: int[]) : int[] =
        items
        |> Array.indexed
        |> Array.filter (fun (bit, _) -> (mask >>> bit) &&& 1 = 1)
        |> Array.map snd

    /// An energy term over a few of the fixed variables: Table.[mask] is its value when the
    /// variables Scope.[b] are 1 for the bits b set in mask and 0 for the rest.
    type private EnergyTerm = { Scope: int[]; Table: float[] }

    /// Order in which the join eliminates `variables` variables coupled by terms over
    /// `scopes`, each step with the variables it minimizes over (ascending): those that share
    /// a term with it once the variables before it are gone. Each step takes the variable
    /// that shares terms with the fewest others, the lower index among equals; eliminating it
    /// couples all of those others to each other.
    let private eliminationOrder (variables: int) (scopes: int[] seq) : (int * int[])[] =
        let neighbours = Array.init variables (fun _ -> HashSet<int>())

        for scope in scopes do
            for a in scope do
                for b in scope do
                    if a <> b then
                        neighbours.[a].Add b |> ignore

        let eliminated = Array.zeroCreate<bool> variables

        [|
            for _ in 1..variables do
                let mutable variable = -1

                for v in 0 .. variables - 1 do
                    if
                        not eliminated.[v]
                        && (variable < 0 || neighbours.[v].Count < neighbours.[variable].Count)
                    then
                        variable <- v

                let others = neighbours.[variable] |> Seq.toArray |> Array.sort
                eliminated.[variable] <- true

                for a in others do
                    neighbours.[a].Remove variable |> ignore

                    for b in others do
                        if a <> b then
                            neighbours.[a].Add b |> ignore

                yield variable, others
        |]

    /// Lowest-energy assignment (0 or 1 for each variable of `order`) of a sum of terms, by
    /// min-sum variable elimination in that order: each step replaces the terms of its
    /// variable by their minimum over it, a term over the others of the step, and remembers
    /// the minimizing value for every assignment of those others. The cost is exponential in
    /// the most others of a step, not in the number of variables. Ties keep a variable at 0.
    let private minimizeTerms (order: (int * int[])[]) (terms: EnergyTerm seq) : int[] =
        let variables = order.Length
        let stepOf = Array.zeroCreate<int> variables
        order |> Array.iteri (fun step (variable, _) -> stepOf.[variable] <- step)

        // A term waits for the first step that eliminates one of its variables; by then the
        // rest of its variables are among the others of that step.
        let waiting = Array.init variables (fun _ -> ResizeArray<EnergyTerm>())

        let place (term: EnergyTerm) =
            if term.Scope.Length > 0 then
                waiting.[term.Scope |> Array.map (fun v -> stepOf.[v]) |> Array.min].Add term

        terms |> Seq.iter place
        // Per step, the value its variable takes at each mask of its others, one bit per mask.
        let choices = Array.zeroCreate<uint64[]> variables

        for step in 0 .. variables - 1 do
            let variable, others = order.[step]

            // For each term of the step, the bit of each of its variables in a mask over
            // `others` with `variable` as the extra top bit.
            let top = others.Length

            let placed =
                waiting.[step]
                |> Seq.map (fun term ->
                    term.Table,
                    term.Scope
                    |> Array.map (fun v -> if v = variable then top else System.Array.BinarySearch(others, v)))
                |> Seq.toArray

            waiting.[step].Clear()

            let valueAt (mask: int) =
                placed
                |> Array.sumBy (fun (table, bits) ->
                    let mutable index = 0

                    for b in 0 .. bits.Length - 1 do
                        index <- index ||| (((mask >>> bits.[b]) &&& 1) <<< b)

                    table.[index])

            let size = 1 <<< others.Length
            let minimum = Array.zeroCreate<float> size
            let choice = Array.zeroCreate<uint64> ((size + 63) / 64)

            for mask in 0 .. size - 1 do
                let atZero = valueAt mask
                let atOne = valueAt (mask ||| (1 <<< top))

                if atOne < atZero then
                    minimum.[mask] <- atOne
                    choice.[mask >>> 6] <- choice.[mask >>> 6] ||| (1UL <<< (mask &&& 63))
                else
                    minimum.[mask] <- atZero

            choices.[step] <- choice
            place { Scope = others; Table = minimum }

        // Walking back, each step depends only on variables eliminated after it.
        let values = Array.zeroCreate<int> variables

        for step in variables - 1 .. -1 .. 0 do
            let variable, others = order.[step]

            let mask =
                others
                |> Array.indexed
                |> Array.sumBy (fun (bit, v) -> values.[v] <<< bit)

            values.[variable] <- int ((choices.[step].[mask >>> 6] >>> (mask &&& 63)) &&& 1UL)

        values

    /// How a QUBO is cut, worked out before any piece runs.
    type private ConditioningPlan =
        {
            /// Fixed variables, ascending
            Fixed: int[]

            /// Connected components of the free variables
            Pieces: int[][]

            /// For each piece, the positions in Fixed of the fixed variables coupled to it
            Boundaries: int[][]

            /// Steps of the join, over positions in Fixed (see eliminationOrder)
            Order: (int * int[])[]

            /// Circuit runs: 2^b for a piece with b fixed variables on its boundary
            Runs: int64
        }

    /// The cut of a QUBO into pieces of at most `maxPieceQubits` variables within `maxRuns`
    /// circuit runs and a join of at most MostJoinSpan, or why there is none: a phrase that
    /// follows "cutting it into circuits of k qubits" and names the run limit as `runLimit`.
    let private planWithin
        (runLimit: string)
        (maxRuns: int)
        (maxPieceQubits: int)
        (qubo: float[,])
        : Result<ConditioningPlan, string> =
        let adjacent = adjacency qubo

        // Most fixed variables a piece can touch: 2^boundary runs fit in maxRuns.
        let boundary = System.Numerics.BitOperations.Log2(uint32 (max 1 maxRuns))
        let forced, forcedSpan = forcedJoin adjacent maxPieceQubits boundary

        if maxRuns < 1 then
            Error $"has no circuit runs to use: {runLimit} = {maxRuns}"
        elif forcedSpan > MostJoinSpan then
            Error(
                $"fixes {forced} variables coupled to more than {max 1 maxPieceQubits - 1 + boundary} others "
                + $"(left free, one would run its piece more than {runLimit} = {maxRuns} times), and joining "
                + $"the pieces around them takes steps over {forcedSpan} or more fixed variables, more than "
                + $"QuboSplitting.MostJoinSpan = {MostJoinSpan}"
            )
        else
            let fixedArray = cutInto adjacent maxPieceQubits |> List.toArray
            let alive = Array.create adjacent.Length true
            fixedArray |> Array.iter (fun variable -> alive.[variable] <- false)
            let pieces = components adjacent alive |> List.toArray

            let boundaries =
                pieces
                |> Array.map (fun piece ->
                    let inPiece = HashSet<int>(piece)

                    [| 0 .. fixedArray.Length - 1 |]
                    |> Array.filter (fun position ->
                        adjacent.[fixedArray.[position]] |> Array.exists inPiece.Contains))

            let runs =
                boundaries
                |> Array.fold
                    (fun total b ->
                        if b.Length >= 62 || total > System.Int64.MaxValue - (1L <<< b.Length) then
                            System.Int64.MaxValue
                        else
                            total + (1L <<< b.Length))
                    0L

            if runs > int64 maxRuns then
                Error $"needs {runs} circuit runs, more than {runLimit} = {maxRuns}"
            else
                let couplings =
                    [|
                        for p in 0 .. fixedArray.Length - 1 do
                            for q in p + 1 .. fixedArray.Length - 1 do
                                if coupling qubo fixedArray.[p] fixedArray.[q] <> 0.0 then
                                    yield [| p; q |]
                    |]

                let order = eliminationOrder fixedArray.Length (Seq.append couplings boundaries)
                let span = order |> Array.fold (fun widest (_, others) -> max widest others.Length) 0

                if span > MostJoinSpan then
                    Error(
                        $"needs a join with steps over {span} fixed variables, more than "
                        + $"QuboSplitting.MostJoinSpan = {MostJoinSpan}"
                    )
                else
                    Ok
                        {
                            Fixed = fixedArray
                            Pieces = pieces
                            Boundaries = boundaries
                            Order = order
                            Runs = runs
                        }

    /// Solve a QUBO cut by `plan`.
    ///
    /// A piece is a connected component of the free variables. Its QUBO depends only on the
    /// fixed variables it is coupled to, so it is solved once per assignment of those, not
    /// once per assignment of all of them: a chain cut in seven places needs four runs per
    /// piece, not 128. The energy of the whole QUBO is the energy of the fixed variables
    /// among themselves plus the pieces' energies; the join finds the assignment of the
    /// fixed variables with the lowest (see minimizeTerms).
    let private solveConditionedAsync
        (qubo: float[,])
        (plan: ConditioningPlan)
        (solvePiece: float[,] -> Task<Result<int[], QuantumError>>)
        (cancellationToken: CancellationToken)
        : Task<Result<PiecewiseSolution, QuantumError>> =
        let n = Array2D.length1 qubo
        let fixedArray = plan.Fixed
        let pieces = plan.Pieces
        let boundaries = plan.Boundaries

        // The piece's QUBO when the fixed variables at `local` (a mask over its boundary) are 1.
        let pieceQubo (piece: int[]) (boundary: int[]) (local: int) : float[,] =
            let ones =
                selected local boundary |> Array.map (fun position -> fixedArray.[position])

            Array2D.init piece.Length piece.Length (fun a b ->
                let direct = qubo.[piece.[a], piece.[b]]

                if a = b then
                    direct + (ones |> Array.sumBy (coupling qubo piece.[a]))
                else
                    direct)

        // The mask over a piece's boundary that values of all fixed variables (by position in
        // fixedArray) give it.
        let localOf (boundary: int[]) (values: int[]) : int =
            boundary
            |> Array.indexed
            |> Array.sumBy (fun (bit, position) -> values.[position] <<< bit)

        quantumResultTask {
            // solved.[piece].[local]: the piece's bits and energy under that boundary assignment
            let solved =
                boundaries
                |> Array.map (fun boundary -> Array.zeroCreate<int[] * float>(1 <<< boundary.Length))

            for index in 0 .. pieces.Length - 1 do
                for local in 0 .. solved.[index].Length - 1 do
                    cancellationToken.ThrowIfCancellationRequested()
                    let reduced = pieceQubo pieces.[index] boundaries.[index] local
                    let! bits = solvePiece reduced

                    if bits.Length <> pieces.[index].Length then
                        do!
                            Error(
                                QuantumError.OperationError(
                                    "QuboSplitting",
                                    $"a piece of {pieces.[index].Length} variables came back with {bits.Length} bits"
                                )
                            )

                    solved.[index].[local] <- bits, QaoaExecutionHelpers.evaluateQubo reduced bits

            // The energy of an assignment of the fixed variables, as terms over their positions
            // in fixedArray: each one's linear term, each coupled pair's coupling, and each
            // piece's energy at its boundary.
            let terms =
                [
                    for p in 0 .. fixedArray.Length - 1 do
                        let f = fixedArray.[p]
                        yield { Scope = [| p |]; Table = [| 0.0; qubo.[f, f] |] }

                        for q in p + 1 .. fixedArray.Length - 1 do
                            let c = coupling qubo f fixedArray.[q]

                            if c <> 0.0 then
                                yield { Scope = [| p; q |]; Table = [| 0.0; 0.0; 0.0; c |] }

                    for index in 0 .. pieces.Length - 1 do
                        yield
                            {
                                Scope = boundaries.[index]
                                Table = solved.[index] |> Array.map snd
                            }
                ]

            let values = minimizeTerms plan.Order terms
            let bits = Array.zeroCreate n
            fixedArray |> Array.iteri (fun position variable -> bits.[variable] <- values.[position])

            pieces
            |> Array.iteri (fun index piece ->
                let pieceBits, _ = solved.[index].[localOf boundaries.[index] values]
                piece |> Array.iteri (fun row variable -> bits.[variable] <- pieceBits.[row]))

            return
                {
                    Bits = bits
                    Energy = QaoaExecutionHelpers.evaluateQubo qubo bits
                    FixedVariables = List.ofArray fixedArray
                    PiecesSolved = solved |> Array.sumBy Array.length
                }
        }

    let private invalid (field: string) (message: string) : Task<Result<'T, QuantumError>> =
        Task.FromResult(Error(QuantumError.ValidationError(field, message)))

    /// Solve a QUBO through pieces of at most limits.MaxPieceQubits variables.
    ///
    /// `solvePiece` returns the best assignment it finds for a QUBO that fits. A QUBO that
    /// fits is passed to it whole. A wider one is conditioned on fixed variables (see
    /// chooseFixedVariables): the rest falls into pieces, every piece is solved once for each
    /// assignment of the fixed variables it is coupled to, and the assignment of all fixed
    /// variables with the lowest energy of the whole QUBO wins. A QUBO that needs more than
    /// limits.MaxRuns circuit runs, or a join wider than MostJoinSpan, is a ValidationError,
    /// returned before any piece runs.
    let solvePiecewiseAsync
        (limits: ConditioningLimits)
        (qubo: float[,])
        (solvePiece: float[,] -> Task<Result<int[], QuantumError>>)
        (cancellationToken: CancellationToken)
        : Task<Result<PiecewiseSolution, QuantumError>> =
        let n = Array2D.length1 qubo

        if limits.MaxPieceQubits < 1 then
            invalid "MaxPieceQubits" "must be at least 1"
        elif limits.MaxRuns < 1 then
            invalid "MaxRuns" $"must be at least 1, got {limits.MaxRuns}"
        elif n <= limits.MaxPieceQubits then
            quantumResultTask {
                let! bits = solvePiece qubo

                return
                    {
                        Bits = bits
                        Energy = QaoaExecutionHelpers.evaluateQubo qubo bits
                        FixedVariables = []
                        PiecesSolved = 1
                    }
            }
        else
            match planWithin "MaxRuns" limits.MaxRuns limits.MaxPieceQubits qubo with
            | Ok plan -> solveConditionedAsync qubo plan solvePiece cancellationToken
            | Error reason ->
                invalid
                    "qubits"
                    $"the QUBO has {n} variables; cutting it into circuits of {limits.MaxPieceQubits} qubits {reason}"

    /// What became of a problem offered for splitting.
    [<RequireQualifiedAccess>]
    type SplitAttempt =
        /// The problem is not split: the settings forbid it on this backend, the problem
        /// fits, or it does not cut within the limits while the backend can still hold it.
        /// The caller runs it as one circuit.
        | RunAsOneCircuit

        /// The problem was solved through pieces.
        | Solved of bits: int[] * report: QaoaExecutionHelpers.SplitReport

    /// Solve a QUBO by conditioning when the settings and the backend call for a split.
    ///
    /// The QUBO is split when it is wider than the backend runs and settings.Policy allows a
    /// split on this backend (QaoaExecutionHelpers.splitPieceQubits). `solvePiece` then runs
    /// every piece and the report says what was done. When it would take more than
    /// settings.MaxShareRuns circuit runs or a join wider than MostJoinSpan, the QUBO is left
    /// to run as one circuit if the backend can hold it (RunAsOneCircuit) and is a
    /// ValidationError naming the limit if it cannot.
    let trySolveByConditioningAsync
        (settings: QaoaExecutionHelpers.SplitSettings)
        (backend: BackendAbstraction.IQuantumBackend)
        (qubo: float[,])
        (solvePiece: float[,] -> Task<Result<int[], QuantumError>>)
        (cancellationToken: CancellationToken)
        : Task<Result<SplitAttempt, QuantumError>> =
        let n = Array2D.length1 qubo

        match QaoaExecutionHelpers.splitPieceQubits settings backend n with
        | ValueNone -> Task.FromResult(Ok SplitAttempt.RunAsOneCircuit)
        | ValueSome pieceQubits ->
            match planWithin "SplitSettings.MaxShareRuns" settings.MaxShareRuns pieceQubits qubo with
            | Error reason ->
                match BackendAbstraction.UnifiedBackend.getMaxQubits backend with
                | Some capacity when n > capacity ->
                    invalid
                        "qubits"
                        ($"the problem needs {n} qubits and the backend holds {capacity}; it was not split because "
                         + $"cutting it into circuits of {pieceQubits} qubits {reason}")
                | _ -> Task.FromResult(Ok SplitAttempt.RunAsOneCircuit)
            | Ok plan ->
                let widest = ref 0

                let trackedPiece (piece: float[,]) =
                    widest.Value <- max widest.Value (Array2D.length1 piece)
                    solvePiece piece

                quantumResultTask {
                    let! solved = solveConditionedAsync qubo plan trackedPiece cancellationToken

                    let report: QaoaExecutionHelpers.SplitReport =
                        {
                            Runs = solved.PiecesSolved
                            FixedVariables = solved.FixedVariables.Length
                            Blocks = 0
                            WidestPieceQubits = widest.Value
                        }

                    return SplitAttempt.Solved(solved.Bits, report)
                }

    /// How a QUBO was run through QAOA.
    type QaoaSplitRun =
        {
            /// Lowest-energy assignment found, one bit per variable of the QUBO
            Best: int[]

            /// The run when the QUBO ran as one circuit; None when it was split
            Direct: QaoaExecutionHelpers.QaoaRun option

            /// What the split did; None when the QUBO ran as one circuit
            Split: QaoaExecutionHelpers.SplitReport option
        }

    /// QAOA on a QUBO, split by conditioning when config.Splitting and the backend call for
    /// it (trySolveByConditioningAsync); otherwise one QaoaExecutionHelpers.runQaoaSampledAsync.
    /// Every piece of a split runs with the same configuration.
    let runQaoaAsync
        (backend: BackendAbstraction.IQuantumBackend)
        (qubo: float[,])
        (config: QaoaExecutionHelpers.QaoaSolverConfig)
        (cancellationToken: CancellationToken)
        : Task<Result<QaoaSplitRun, QuantumError>> =
        let solvePiece (piece: float[,]) =
            quantumResultTask {
                let! run =
                    QaoaExecutionHelpers.runQaoaSampledAsync backend piece config cancellationToken

                return run.Best
            }

        quantumResultTask {
            match! trySolveByConditioningAsync config.Splitting backend qubo solvePiece cancellationToken with
            | SplitAttempt.Solved(bits, report) ->
                return
                    {
                        Best = bits
                        Direct = None
                        Split = Some report
                    }
            | SplitAttempt.RunAsOneCircuit ->
                let! run =
                    QaoaExecutionHelpers.runQaoaSampledAsync backend qubo config cancellationToken

                return
                    {
                        Best = run.Best
                        Direct = Some run
                        Split = None
                    }
        }

    // ========================================================================
    // SHARES
    // ========================================================================

    /// Best value found for each exact use of the resource by a set of items:
    /// resource used → (value, chosen items).
    type ShareTable = Map<int, float * int list>

    /// Limits of a shares split.
    [<Struct>]
    type ShareLimits =
        {
            /// Most items in a block (one qubit each)
            MaxBlockItems: int

            /// Most circuit runs: one per block and share asked for
            MaxRuns: int
        }

    /// A selection found through blocks.
    type SharesSolution =
        {
            /// Chosen items, ascending
            Items: int list

            /// Sum of the chosen items' values
            Value: float

            /// Sum of the chosen items' weights
            ResourceUsed: int

            /// Blocks the items were cut into
            Blocks: int

            /// Circuit runs made
            Runs: int
        }

    /// QUBO of "the most valuable subset of these items that uses exactly `share` of the
    /// resource": minimise -Σ vᵢxᵢ + λ(Σ wᵢxᵢ - share)² with λ = Σ|vᵢ| + 1. The weights are
    /// integers, so missing the share costs at least λ, more than any difference in value: a
    /// minimum uses exactly the share whenever some subset does. One qubit per item.
    let exactShareQubo (values: float[]) (weights: int[]) (share: int) : float[,] =
        let lambda = (values |> Array.sumBy abs) + 1.0
        let terms = weights |> Array.mapi (fun i w -> i, float w) |> Array.toList

        let dense =
            Qubo.toDenseArray values.Length (Qubo.squaredLinearPenalty lambda terms (-(float share)))

        values |> Array.iteri (fun i v -> dense.[i, i] <- dense.[i, i] - v)
        dense

    /// The table with the samples of a block added: a sample is a subset of the block, and
    /// it enters at its own resource use when that fits the capacity and no subset of at
    /// least its value is known there. `items` are the block's item numbers, in sample bit
    /// order.
    let harvest
        (items: int[])
        (values: float[])
        (weights: int[])
        (capacity: int)
        (samples: int[] seq)
        (table: ShareTable)
        : ShareTable =
        samples
        |> Seq.filter (fun sample -> sample.Length >= items.Length)
        |> Seq.fold
            (fun (known: ShareTable) sample ->
                let chosen =
                    items
                    |> Array.indexed
                    |> Array.filter (fun (bit, _) -> sample.[bit] = 1)
                    |> Array.map snd

                // 64-bit: weights near the integer limit must not wrap into the capacity
                let used = chosen |> Array.sumBy (fun item -> int64 weights.[item])
                let value = chosen |> Array.sumBy (fun item -> values.[item])

                if used > int64 capacity then
                    known
                else
                    match Map.tryFind (int used) known with
                    | Some(best, _) when best >= value -> known
                    | _ -> Map.add (int used) (value, List.ofArray chosen) known)
            table

    /// The table of two sets of items taken together: for every total use within the
    /// capacity, the most valuable pair of one entry from each.
    let joinTables (capacity: int) (first: ShareTable) (second: ShareTable) : ShareTable =
        Seq.allPairs (Map.toSeq first) (Map.toSeq second)
        |> Seq.filter (fun ((usedA, _), (usedB, _)) -> int64 usedA + int64 usedB <= int64 capacity)
        |> Seq.fold
            (fun (joined: ShareTable) ((usedA, (valueA, itemsA)), (usedB, (valueB, itemsB))) ->
                let used = usedA + usedB
                let value = valueA + valueB

                match Map.tryFind used joined with
                | Some(best, _) when best >= value -> joined
                | _ -> Map.add used (value, itemsA @ itemsB) joined)
            Map.empty

    /// Consecutive blocks of at most `maxBlockItems` items (at least 1), as equal in size as
    /// they come.
    let blocksOf (maxBlockItems: int) (itemCount: int) : int[] list =
        if itemCount <= 0 then
            []
        else
            let widest = max 1 maxBlockItems
            let blockCount = (itemCount + widest - 1) / widest
            let size = (itemCount + blockCount - 1) / blockCount
            [| 0 .. itemCount - 1 |] |> Array.chunkBySize size |> Array.toList

    /// First and last exact share a block is asked for: from 1 (from 0 when the block holds
    /// weightless items) to the smaller of the capacity and the block's weight.
    let private shareRange (weights: int[]) (capacity: int) (block: int[]) : int * int =
        let blockWeight = block |> Array.sumBy (fun item -> int64 weights.[item])
        let hasWeightless = block |> Array.exists (fun item -> weights.[item] = 0)
        (if hasWeightless then 0 else 1), int (min (int64 capacity) blockWeight)

    /// Circuit runs solveSharesAsync makes for these weights and capacity with blocks of at
    /// most `maxBlockItems` items: one per block and share. Counted, not enumerated, so a
    /// capacity of millions of units costs nothing to refuse.
    let shareRuns (maxBlockItems: int) (weights: int[]) (capacity: int) : int64 =
        blocksOf maxBlockItems weights.Length
        |> List.sumBy (fun block ->
            let first, last = shareRange weights capacity block
            max 0L (int64 last - int64 first + 1L))

    /// The most valuable selection of items whose integer weights add up to at most
    /// `capacity`, found block by block.
    ///
    /// `sampleBlock` runs a QUBO and returns its samples. Each block is asked for every exact
    /// share from 1 to the smaller of the capacity and its total weight (and for share 0 when
    /// it holds weightless items), one run each; every sample of every run feeds the block's
    /// table, and the tables are joined. Weights and capacity must not be negative. More
    /// runs than limits.MaxRuns is a ValidationError, returned before any run: use a coarser
    /// weight unit or larger blocks.
    let solveSharesAsync
        (limits: ShareLimits)
        (values: float[])
        (weights: int[])
        (capacity: int)
        (sampleBlock: float[,] -> Task<Result<int[][], QuantumError>>)
        (cancellationToken: CancellationToken)
        : Task<Result<SharesSolution, QuantumError>> =
        if values.Length <> weights.Length then
            invalid "weights" "one weight per value is required"
        elif limits.MaxBlockItems < 1 then
            invalid "MaxBlockItems" "must be at least 1"
        elif capacity < 0 || weights |> Array.exists (fun w -> w < 0) then
            invalid "weights" "weights and capacity must not be negative"
        else
            let blocks = blocksOf limits.MaxBlockItems values.Length
            let runs = shareRuns limits.MaxBlockItems weights capacity

            if runs > int64 limits.MaxRuns then
                invalid
                    "capacity"
                    ($"splitting into {blocks.Length} blocks needs {runs} circuit runs, above the limit of {limits.MaxRuns}; "
                     + "use a coarser weight unit or larger blocks")
            else
                let empty: ShareTable = Map [ 0, (0.0, []) ]

                quantumResultTask {
                    let joined = ref empty

                    for block in blocks do
                        let blockValues = block |> Array.map (fun item -> values.[item])
                        let blockWeights = block |> Array.map (fun item -> weights.[item])
                        let firstShare, lastShare = shareRange weights capacity block
                        let table = ref empty

                        for share in firstShare..lastShare do
                            cancellationToken.ThrowIfCancellationRequested()
                            let! samples = sampleBlock (exactShareQubo blockValues blockWeights share)
                            table.Value <- harvest block values weights capacity samples table.Value

                        joined.Value <- joinTables capacity joined.Value table.Value

                    let used, (value, items) =
                        joined.Value |> Map.toSeq |> Seq.maxBy (fun (used, (value, _)) -> value, -used)

                    return
                        {
                            Items = List.sort items
                            Value = value
                            ResourceUsed = used
                            Blocks = blocks.Length
                            Runs = int runs
                        }
                }
