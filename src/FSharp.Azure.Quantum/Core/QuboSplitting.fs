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

            /// Most variables to fix, from 0 to MostFixedVariables; a piece coupled to b of the
            /// fixed variables runs 2^b times
            MaxFixedVariables: int
        }

    /// Upper end of MaxFixedVariables: the assignments of the fixed variables are counted in a
    /// 32-bit integer and compared one by one.
    [<Literal>]
    let MostFixedVariables = 20

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
    /// `maxPieceQubits` variables, or None when that takes more than `maxFixed` of them.
    /// Greedy: while a component is too wide, fix the variable of it whose removal leaves the
    /// smallest largest piece (the one with more neighbours, then the lower index, among
    /// equals), so a chain is cut in the middle and a hub is taken out of a star.
    let private fixedVariablesWithin (maxFixed: int) (maxPieceQubits: int) (qubo: float[,]) : int list option =
        let adjacent = adjacency qubo
        let alive = Array.create (Array2D.length1 qubo) true
        let widestPiece = max 1 maxPieceQubits

        // A free variable keeps every neighbour in its own piece or among the fixed ones, so
        // one with more neighbours than that has to be fixed itself. This refuses a dense
        // QUBO before the search below, which costs a traversal per candidate.
        let mustBeFixed =
            adjacent
            |> Array.sumBy (fun neighbours ->
                if int64 neighbours.Length > int64 widestPiece - 1L + int64 maxFixed then
                    1
                else
                    0)

        let rec fix (chosen: int list) =
            let widest =
                components adjacent alive |> List.sortByDescending Array.length |> List.tryHead

            match widest with
            | Some part when part.Length > widestPiece ->
                if chosen.Length >= maxFixed then
                    None
                else
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
            | _ -> Some(List.sort chosen)

        if mustBeFixed > maxFixed then None else fix []

    /// Variables to fix so that every connected component of the rest has at most
    /// `maxPieceQubits` variables: sorted, and empty when the QUBO already fits or falls apart
    /// by itself. It is a heuristic (see fixedVariablesWithin): a smaller set may exist.
    let chooseFixedVariables (maxPieceQubits: int) (qubo: float[,]) : int list =
        fixedVariablesWithin System.Int32.MaxValue maxPieceQubits qubo
        |> Option.defaultValue []

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

    /// Solve a QUBO that `fixedVariables` cut into pieces.
    ///
    /// A piece is a connected component of the free variables. Its QUBO depends only on the
    /// fixed variables it is coupled to, so it is solved once per assignment of those, not
    /// once per assignment of all of them: a chain cut in seven places needs four runs per
    /// piece, not 128. The energy of the whole QUBO is the energy of the fixed variables
    /// among themselves plus the pieces' energies, and the assignment with the lowest wins.
    let private solveConditionedAsync
        (qubo: float[,])
        (fixedVariables: int list)
        (solvePiece: float[,] -> Task<Result<int[], QuantumError>>)
        (cancellationToken: CancellationToken)
        : Task<Result<PiecewiseSolution, QuantumError>> =
        let n = Array2D.length1 qubo
        let adjacent = adjacency qubo
        let fixedArray = List.toArray fixedVariables
        let alive = Array.create n true
        fixedArray |> Array.iter (fun variable -> alive.[variable] <- false)
        let pieces = components adjacent alive |> List.toArray

        // For each piece, the positions in fixedArray of the fixed variables coupled to it.
        let boundaries =
            pieces
            |> Array.map (fun piece ->
                let inPiece = HashSet<int>(piece)

                [| 0 .. fixedArray.Length - 1 |]
                |> Array.filter (fun position -> adjacent.[fixedArray.[position]] |> Array.exists inPiece.Contains))

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

        // The mask over a piece's boundary that an assignment of all fixed variables gives it.
        let localOf (boundary: int[]) (assignment: int) : int =
            boundary
            |> Array.indexed
            |> Array.sumBy (fun (bit, position) -> ((assignment >>> position) &&& 1) <<< bit)

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

            let energyOf (assignment: int) =
                let ones = selected assignment fixedArray

                let amongFixed =
                    ones |> Array.sumBy (fun i -> ones |> Array.sumBy (fun j -> qubo.[i, j]))

                amongFixed
                + (Array.zip boundaries solved
                   |> Array.sumBy (fun (boundary, answers) -> snd answers.[localOf boundary assignment]))

            let best = seq { 0 .. (1 <<< fixedArray.Length) - 1 } |> Seq.minBy energyOf
            let bits = Array.zeroCreate n
            selected best fixedArray |> Array.iter (fun variable -> bits.[variable] <- 1)

            pieces
            |> Array.iteri (fun index piece ->
                let pieceBits, _ = solved.[index].[localOf boundaries.[index] best]
                piece |> Array.iteri (fun row variable -> bits.[variable] <- pieceBits.[row]))

            return
                {
                    Bits = bits
                    Energy = QaoaExecutionHelpers.evaluateQubo qubo bits
                    FixedVariables = fixedVariables
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
    /// limits.MaxFixedVariables fixed variables is a ValidationError, returned before any
    /// piece runs.
    let solvePiecewiseAsync
        (limits: ConditioningLimits)
        (qubo: float[,])
        (solvePiece: float[,] -> Task<Result<int[], QuantumError>>)
        (cancellationToken: CancellationToken)
        : Task<Result<PiecewiseSolution, QuantumError>> =
        let n = Array2D.length1 qubo

        if limits.MaxPieceQubits < 1 then
            invalid "MaxPieceQubits" "must be at least 1"
        elif limits.MaxFixedVariables < 0 || limits.MaxFixedVariables > MostFixedVariables then
            invalid "MaxFixedVariables" $"must be between 0 and {MostFixedVariables}, got {limits.MaxFixedVariables}"
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
            match fixedVariablesWithin limits.MaxFixedVariables limits.MaxPieceQubits qubo with
            | Some fixedVariables -> solveConditionedAsync qubo fixedVariables solvePiece cancellationToken
            | None ->
                invalid
                    "qubits"
                    ($"the QUBO has {n} variables; cutting it into circuits of {limits.MaxPieceQubits} qubits "
                     + $"needs more than {limits.MaxFixedVariables} fixed variables")

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
    /// settings.MaxFixedVariables fixed variables, the QUBO is left to run as one circuit if
    /// the backend can hold it (RunAsOneCircuit) and is a ValidationError naming the limit if
    /// it cannot.
    let trySolveByConditioningAsync
        (settings: QaoaExecutionHelpers.SplitSettings)
        (backend: BackendAbstraction.IQuantumBackend)
        (qubo: float[,])
        (solvePiece: float[,] -> Task<Result<int[], QuantumError>>)
        (cancellationToken: CancellationToken)
        : Task<Result<SplitAttempt, QuantumError>> =
        let n = Array2D.length1 qubo
        let maxFixed = max 0 (min settings.MaxFixedVariables MostFixedVariables)

        match QaoaExecutionHelpers.splitPieceQubits settings backend n with
        | ValueNone -> Task.FromResult(Ok SplitAttempt.RunAsOneCircuit)
        | ValueSome pieceQubits ->
            match fixedVariablesWithin maxFixed pieceQubits qubo with
            | None ->
                match BackendAbstraction.UnifiedBackend.getMaxQubits backend with
                | Some capacity when n > capacity ->
                    invalid
                        "qubits"
                        ($"the problem needs {n} qubits and the backend holds {capacity}; it was not split because "
                         + $"cutting it into circuits of {pieceQubits} qubits needs more fixed variables than "
                         + $"SplitSettings.MaxFixedVariables = {settings.MaxFixedVariables}")
                | _ -> Task.FromResult(Ok SplitAttempt.RunAsOneCircuit)
            | Some fixedVariables ->
                let widest = ref 0

                let trackedPiece (piece: float[,]) =
                    widest.Value <- max widest.Value (Array2D.length1 piece)
                    solvePiece piece

                quantumResultTask {
                    let! solved =
                        solveConditionedAsync qubo fixedVariables trackedPiece cancellationToken

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
