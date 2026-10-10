module FSharp.Azure.Quantum.Tests.QuboSplittingTests

// QuboSplitting joins pieces exactly: with a piece solver that returns the true minimum (or
// a block sampler that returns every subset) the joined answer is the true optimum of the
// whole problem. Checked against full enumeration, so nothing depends on sampling.

open System
open System.Threading
open System.Threading.Tasks
open Xunit
open FSharp.Azure.Quantum
open FSharp.Azure.Quantum.Core
open FSharp.Azure.Quantum.Backends

let private bitsOf (n: int) (index: int) =
    Array.init n (fun q -> (index >>> q) &&& 1)

let private allBits (n: int) = Array.init (1 <<< n) (bitsOf n)

let private energy = QaoaExecutionHelpers.evaluateQubo

let private minimumOf (qubo: float[,]) : int[] * float =
    allBits (Array2D.length1 qubo)
    |> Array.map (fun bits -> bits, energy qubo bits)
    |> Array.minBy snd

/// Piece solver that returns the true minimum and records the widths it was given.
let private exactPieces (widths: ResizeArray<int>) (piece: float[,]) : Task<Result<int[], QuantumError>> =
    widths.Add(Array2D.length1 piece)
    Task.FromResult(Ok(fst (minimumOf piece)))

/// Seeded sparse QUBO: a ring with a few chords, random couplings and linear terms.
let private sparseQubo (seed: int) (n: int) (chords: int) : float[,] =
    let rng = Random(seed)
    let qubo = Array2D.zeroCreate n n
    let weight () = float (rng.Next(-9, 10)) / 2.0

    for i in 0 .. n - 1 do
        qubo.[i, i] <- weight ()
        let j = (i + 1) % n
        qubo.[min i j, max i j] <- weight () + 0.25

    for _ in 1..chords do
        let i = rng.Next n
        let j = rng.Next n

        if i <> j then
            qubo.[min i j, max i j] <- weight () + 0.25

    qubo

let private limits (piece: int) (runs: int) : QuboSplitting.ConditioningLimits =
    {
        MaxPieceQubits = piece
        MaxRuns = runs
    }

let private ok (result: Result<'T, QuantumError>) : 'T =
    result |> Result.defaultWith (fun err -> failwith $"{err}")

// ----------------------------------------------------------------------------
// Conditioning
// ----------------------------------------------------------------------------

[<Fact>]
let ``chooseFixedVariables leaves only pieces that fit`` () =
    for seed in 1..20 do
        let n = 14
        let qubo = sparseQubo seed n 2
        let maxPiece = 5
        let fixedVariables = QuboSplitting.chooseFixedVariables maxPiece qubo
        let fixedValues = fixedVariables |> List.map (fun v -> v, 0) |> Map.ofList
        let reduced, free = QuboSplitting.condition qubo fixedValues

        Assert.Equal(n - fixedVariables.Length, free.Length)

        let edges =
            [
                for a in 0 .. free.Length - 1 do
                    for b in a + 1 .. free.Length - 1 do
                        if reduced.[a, b] + reduced.[b, a] <> 0.0 then
                            yield (a, b)
            ]

        for part in ProblemDecomposition.connectedComponents free.Length edges do
            Assert.True(part.Length <= maxPiece, $"seed {seed}: a piece of {part.Length} variables is left")

[<Fact>]
let ``chooseFixedVariables fixes nothing when the QUBO fits or falls apart by itself`` () =
    Assert.Empty(QuboSplitting.chooseFixedVariables 8 (sparseQubo 3 8 2))

    // two rings of 4 with no coupling between them
    let apart = Array2D.zeroCreate 8 8

    for offset in [ 0; 4 ] do
        for i in 0..3 do
            let a, b = offset + i, offset + (i + 1) % 4
            apart.[min a b, max a b] <- 1.0

    Assert.Empty(QuboSplitting.chooseFixedVariables 4 apart)

[<Fact>]
let ``condition keeps every energy difference between assignments of the free variables`` () =
    let qubo = sparseQubo 5 8 3
    let fixedValues = Map [ 2, 1; 5, 0; 6, 1 ]
    let reduced, free = QuboSplitting.condition qubo fixedValues

    let full (freeBits: int[]) =
        let bits = Array.zeroCreate 8

        for KeyValue(variable, value) in fixedValues do
            bits.[variable] <- value

        free |> Array.iteri (fun row variable -> bits.[variable] <- freeBits.[row])
        bits

    let offsets =
        allBits free.Length
        |> Array.map (fun freeBits -> energy qubo (full freeBits) - energy reduced freeBits)

    for offset in offsets do
        Assert.Equal(offsets.[0], offset, 9)

[<Fact>]
let ``pieces solved exactly join into the minimum of the whole QUBO`` () : Task =
    task {
        for seed in 1..12 do
            let qubo = sparseQubo seed 13 2
            let widths = ResizeArray<int>()

            let! solved =
                QuboSplitting.solvePiecewiseAsync (limits 5 256) qubo (exactPieces widths) CancellationToken.None

            let solution = ok solved
            let _, lowest = minimumOf qubo

            Assert.Equal(lowest, solution.Energy, 9)
            Assert.Equal(solution.Energy, energy qubo solution.Bits, 9)
            Assert.NotEmpty solution.FixedVariables
            Assert.Equal(widths.Count, solution.PiecesSolved)
            Assert.All(widths, (fun width -> Assert.InRange(width, 1, 5)))
    }

[<Fact>]
let ``a QUBO that fits is passed to the piece solver whole`` () : Task =
    task {
        let qubo = sparseQubo 9 6 1
        let widths = ResizeArray<int>()

        let! solved =
            QuboSplitting.solvePiecewiseAsync (limits 6 1) qubo (exactPieces widths) CancellationToken.None

        let solution = ok solved

        Assert.Equal<int list>([ 6 ], List.ofSeq widths)
        Assert.Empty solution.FixedVariables
        Assert.Equal(1, solution.PiecesSolved)
        Assert.Equal(snd (minimumOf qubo), solution.Energy, 9)
    }

[<Fact>]
let ``a dense QUBO needs too many circuit runs and is refused before any piece runs`` () : Task =
    task {
        // pieces of 4 out of 12 all coupled: the piece touches 8 fixed variables, 256 runs
        let dense = Array2D.init 12 12 (fun i j -> if i < j then 1.0 else 0.0)
        let widths = ResizeArray<int>()

        match! QuboSplitting.solvePiecewiseAsync (limits 4 3) dense (exactPieces widths) CancellationToken.None with
        | Error(QuantumError.ValidationError(field, message)) ->
            Assert.Equal("qubits", field)
            Assert.Contains("needs 256 circuit runs, more than MaxRuns = 3", message)
        | other -> Assert.Fail($"Expected a qubits validation error, got %A{other}")

        Assert.Empty widths
    }

[<Fact>]
let ``a failing piece fails the whole solve with its error`` () : Task =
    task {
        let failing (_: float[,]) : Task<Result<int[], QuantumError>> =
            Task.FromResult(Error(QuantumError.OperationError("Piece", "no")))

        match! QuboSplitting.solvePiecewiseAsync (limits 5 256) (sparseQubo 2 12 1) failing CancellationToken.None with
        | Error(QuantumError.OperationError(operation, _)) -> Assert.Equal("Piece", operation)
        | other -> Assert.Fail($"Expected the piece error, got %A{other}")
    }

/// LocalBackend that reports a qubit limit of its own.
type private NarrowBackend(maxQubits: int) =
    let inner = LocalBackend.LocalBackend() :> BackendAbstraction.IQuantumBackend
    let widest = ref 0

    member _.WidestCircuit = widest.Value

    interface BackendAbstraction.IQuantumBackend with
        member _.Name = inner.Name
        member _.NativeStateType = inner.NativeStateType
        member _.SupportsOperation op = inner.SupportsOperation op
        member _.InitializeState n = inner.InitializeState n
        member _.ApplyOperation op state = inner.ApplyOperation op state

        member _.ExecuteToState circuit =
            widest.Value <- max widest.Value circuit.NumQubits
            inner.ExecuteToState circuit

        member _.ExecuteToStateAsync circuit ct =
            widest.Value <- max widest.Value circuit.NumQubits
            inner.ExecuteToStateAsync circuit ct

        member _.ApplyOperationAsync op state ct = inner.ApplyOperationAsync op state ct

    interface BackendAbstraction.IQubitLimitedBackend with
        member _.MaxQubits = Some maxQubits

let private quickConfig =
    { QaoaExecutionHelpers.fastConfig with
        NumLayers = 1
        FinalShots = 100
    }

[<Fact>]
let ``runQaoaAsync splits a QUBO wider than the backend and never runs a wider circuit`` () : Task =
    task {
        let qubo = sparseQubo 4 12 0 // a ring of 12
        let narrow = NarrowBackend 5

        let! result =
            QuboSplitting.runQaoaAsync narrow qubo quickConfig CancellationToken.None

        let run = ok result

        Assert.Equal(12, run.Best.Length)
        Assert.True(run.Direct.IsNone, "a split run has no single sample set")
        Assert.InRange(narrow.WidestCircuit, 1, 5)

        match run.Split with
        | None -> Assert.Fail("a split run reports what it did")
        | Some report ->
            Assert.True(report.FixedVariables > 0)
            Assert.True(report.Runs >= 2)
            Assert.Equal(0, report.Blocks)
            Assert.Equal(narrow.WidestCircuit, report.WidestPieceQubits)
    }

[<Fact>]
let ``runQaoaAsync runs a QUBO that fits as one circuit`` () : Task =
    task {
        let qubo = sparseQubo 4 5 0

        let! result =
            QuboSplitting.runQaoaAsync (NarrowBackend 5) qubo quickConfig CancellationToken.None

        let run = ok result

        Assert.True(run.Direct.IsSome)
        Assert.True(run.Split.IsNone)
        Assert.Equal(100, run.Direct.Value.Samples.Length)
    }

// ----------------------------------------------------------------------------
// Shares
// ----------------------------------------------------------------------------

/// Block sampler that returns every subset of the block once.
let private everySubset (runs: ResizeArray<int>) (qubo: float[,]) : Task<Result<int[][], QuantumError>> =
    runs.Add(Array2D.length1 qubo)
    Task.FromResult(Ok(allBits (Array2D.length1 qubo)))

/// Block sampler that returns only the minimum-energy assignments of the QUBO it is given.
let private onlyMinima (qubo: float[,]) : Task<Result<int[][], QuantumError>> =
    let states = allBits (Array2D.length1 qubo)
    let energies = states |> Array.map (energy qubo)
    let lowest = Array.min energies

    Task.FromResult(
        Ok(
            Array.zip states energies
            |> Array.filter (fun (_, e) -> e - lowest <= 1e-9)
            |> Array.map fst
        )
    )

let private knapsackOptimum (values: float[]) (weights: int[]) (capacity: int) : float =
    allBits values.Length
    |> Array.filter (fun bits -> Array.fold2 (fun acc bit w -> acc + bit * w) 0 bits weights <= capacity)
    |> Array.map (fun bits -> Array.fold2 (fun acc bit v -> acc + float bit * v) 0.0 bits values)
    |> Array.max

let private seededItems (seed: int) (n: int) : float[] * int[] =
    let rng = Random(seed)
    Array.init n (fun _ -> float (rng.Next(1, 20))), Array.init n (fun _ -> rng.Next(1, 7))

let private shareLimits (blockItems: int) (runs: int) : QuboSplitting.ShareLimits =
    {
        MaxBlockItems = blockItems
        MaxRuns = runs
    }

[<Fact>]
let ``exactShareQubo minimum is the most valuable subset with exactly that share`` () =
    let values, weights = seededItems 3 7

    for share in 0 .. Array.sum weights do
        let qubo = QuboSplitting.exactShareQubo values weights share
        let states = allBits 7
        let energies = states |> Array.map (energy qubo)
        let lowest = Array.min energies

        let weightOf (bits: int[]) =
            Array.fold2 (fun acc bit w -> acc + bit * w) 0 bits weights

        let valueOf (bits: int[]) =
            Array.fold2 (fun acc bit v -> acc + float bit * v) 0.0 bits values

        let exact = states |> Array.filter (fun bits -> weightOf bits = share)

        if exact.Length > 0 then
            let best = exact |> Array.map valueOf |> Array.max

            for bits, e in Array.zip states energies do
                if e - lowest <= 1e-9 then
                    Assert.Equal(share, weightOf bits)
                    Assert.Equal(best, valueOf bits, 9)

[<Fact>]
let ``blocks sampled exhaustively join into the knapsack optimum`` () : Task =
    task {
        for seed in 1..10 do
            let values, weights = seededItems seed 12
            let capacity = 14
            let runs = ResizeArray<int>()

            let! solved =
                QuboSplitting.solveSharesAsync
                    (shareLimits 4 256)
                    values
                    weights
                    capacity
                    (everySubset runs)
                    CancellationToken.None

            let solution = ok solved

            Assert.Equal(knapsackOptimum values weights capacity, solution.Value, 9)
            Assert.Equal(solution.Value, solution.Items |> List.sumBy (fun item -> values.[item]), 9)
            Assert.Equal(solution.ResourceUsed, solution.Items |> List.sumBy (fun item -> weights.[item]))
            Assert.InRange(solution.ResourceUsed, 0, capacity)
            Assert.Equal(3, solution.Blocks)
            Assert.Equal(runs.Count, solution.Runs)
            Assert.All(runs, (fun width -> Assert.Equal(4, width)))
    }

[<Fact>]
let ``blocks that return only their QUBO minima still join into the knapsack optimum`` () : Task =
    task {
        // Each run returns nothing but the best subsets at the share asked for: the shares
        // asked for must cover every use of the resource a block can contribute.
        for seed in 11..18 do
            let values, weights = seededItems seed 10
            let capacity = 11

            let! solved =
                QuboSplitting.solveSharesAsync
                    (shareLimits 5 256)
                    values
                    weights
                    capacity
                    onlyMinima
                    CancellationToken.None

            Assert.Equal(knapsackOptimum values weights capacity, (ok solved).Value, 9)
    }

[<Fact>]
let ``weightless items are chosen when they add value`` () : Task =
    task {
        let values = [| 5.0; 3.0; 4.0; 2.0 |]
        let weights = [| 0; 2; 0; 3 |]

        let! solved =
            QuboSplitting.solveSharesAsync (shareLimits 2 64) values weights 2 onlyMinima CancellationToken.None

        let solution = ok solved
        Assert.Equal<int list>([ 0; 1; 2 ], solution.Items)
        Assert.Equal(12.0, solution.Value, 9)
    }

[<Fact>]
let ``a split that needs more runs than allowed is refused before any run`` () : Task =
    task {
        let values, weights = seededItems 1 12
        let runs = ResizeArray<int>()

        match!
            QuboSplitting.solveSharesAsync
                (shareLimits 4 10)
                values
                weights
                14
                (everySubset runs)
                CancellationToken.None
        with
        | Error(QuantumError.ValidationError(field, message)) ->
            Assert.Equal("capacity", field)
            Assert.Contains("circuit runs", message)
        | other -> Assert.Fail($"Expected a capacity validation error, got %A{other}")

        Assert.Empty runs
    }

[<Fact>]
let ``blocksOf cuts items into consecutive blocks of nearly equal size`` () =
    Assert.Equal<int list>([ 4; 4; 4 ], QuboSplitting.blocksOf 5 12 |> List.map Array.length)
    Assert.Equal<int list>([ 6 ], QuboSplitting.blocksOf 6 6 |> List.map Array.length)
    Assert.Equal<int list>([ 4; 3 ], QuboSplitting.blocksOf 4 7 |> List.map Array.length)
    Assert.Empty(QuboSplitting.blocksOf 4 0)
    Assert.Equal<int[]>([| 0..6 |], QuboSplitting.blocksOf 4 7 |> Array.concat)

// ----------------------------------------------------------------------------
// Solvers on a backend narrower than the problem
// ----------------------------------------------------------------------------

open FSharp.Azure.Quantum.Quantum

let private knapsackOf (seed: int) (n: int) (capacity: float) : QuantumKnapsackSolver.KnapsackProblem =
    let values, weights = seededItems seed n

    {
        Items =
            [
                for i in 0 .. n - 1 ->
                    {
                        Id = string i
                        Weight = float weights.[i]
                        Value = values.[i]
                    }
            ]
        Capacity = capacity
    }

[<Fact>]
let ``knapsack solved in blocks fits the capacity and never beats the optimum`` () : Task =
    task {
        let problem = knapsackOf 21 12 14.0
        let values, weights = seededItems 21 12
        let narrow = NarrowBackend 16

        let! result =
            QuantumKnapsackSolver.solveInBlocksAsync narrow problem quickConfig 4 CancellationToken.None

        let solution = ok result

        Assert.True(solution.IsFeasible)
        Assert.InRange(solution.TotalWeight, 0.0, 14.0)
        Assert.True(solution.TotalValue <= knapsackOptimum values weights 14 + 1e-9)
        Assert.Equal(solution.SelectedItems.Length, (solution.SelectedItems |> List.distinct).Length)
        Assert.True(solution.Sampling.IsNone, "no single sampling run produced a joined selection")
        Assert.InRange(narrow.WidestCircuit, 1, 4)
    }

[<Fact>]
let ``knapsack solveAsync switches to blocks when the QUBO is wider than the backend`` () : Task =
    task {
        let problem = knapsackOf 22 9 12.0
        let width = (ok (QuantumKnapsackSolver.toQubo problem)).NumVariables
        let narrow = NarrowBackend 6
        Assert.True(width > 6, $"the QUBO takes {width} qubits")

        let! result =
            QuantumKnapsackSolver.solveAsync
                narrow
                problem
                { QuantumKnapsackSolver.defaultConfig with
                    NumShots = 100
                }
                CancellationToken.None

        let solution = ok result
        Assert.True(solution.IsFeasible)
        Assert.InRange(solution.TotalWeight, 0.0, 12.0)
        Assert.True(solution.Sampling.IsNone)
        Assert.InRange(narrow.WidestCircuit, 1, 6)
    }

[<Fact>]
let ``vertex cover wider than the backend is solved through circuits that fit`` () : Task =
    task {
        let cover: QuantumVertexCoverSolver.Problem =
            {
                Vertices = [ for i in 0..11 -> { Id = string i; Weight = 1.0 } ]
                Edges = [ for i in 0..10 -> (i, i + 1) ]
            }

        let narrow = NarrowBackend 5

        let! result =
            QuantumVertexCoverSolver.solveWithConfigAsync narrow cover quickConfig CancellationToken.None

        let solution = ok result

        Assert.True(solution.IsValid, "repair is on, so the cover is valid")
        Assert.True(solution.Sampling.IsNone)
        Assert.True(solution.OptimizedParameters.IsNone)
        Assert.InRange(narrow.WidestCircuit, 1, 5)
        Assert.InRange(solution.CoverSize, 6, 12)
    }

[<Fact>]
let ``MaxCut wider than the backend is solved through circuits that fit`` () : Task =
    task {
        let ring: QuantumMaxCutSolver.MaxCutProblem =
            {
                Vertices = [ for i in 0..11 -> string i ]
                Edges =
                    [
                        for i in 0..11 -> GraphOptimization.edge (string i) (string ((i + 1) % 12)) 1.0
                    ]
            }

        let narrow = NarrowBackend 5

        let! result =
            QuantumMaxCutSolver.solveAsync
                narrow
                ring
                { QuantumMaxCutSolver.defaultConfig with
                    NumShots = 100
                }
                CancellationToken.None

        let solution = ok result

        Assert.Equal(12, solution.PartitionS.Length + solution.PartitionT.Length)
        Assert.Equal(solution.CutValue, QuantumMaxCutSolver.calculateCutValue ring solution.PartitionS, 9)
        Assert.InRange(solution.CutValue, 0.0, 12.0)
        Assert.True(solution.Sampling.IsNone)
        Assert.InRange(narrow.WidestCircuit, 1, 5)
    }

// ----------------------------------------------------------------------------
// Split settings: when a split happens, how wide its pieces are, and what runs instead
// ----------------------------------------------------------------------------

/// LocalBackend that holds `holds` qubits and reports `runs` as the widest circuit worth running.
type private RoomyBackend(runs: int, holds: int) =
    let inner = LocalBackend.LocalBackend() :> BackendAbstraction.IQuantumBackend
    let widest = ref 0

    member _.WidestCircuit = widest.Value

    interface BackendAbstraction.IQuantumBackend with
        member _.Name = inner.Name
        member _.NativeStateType = inner.NativeStateType
        member _.SupportsOperation op = inner.SupportsOperation op
        member _.InitializeState n = inner.InitializeState n
        member _.ApplyOperation op state = inner.ApplyOperation op state

        member _.ExecuteToState circuit =
            widest.Value <- max widest.Value circuit.NumQubits
            inner.ExecuteToState circuit

        member _.ExecuteToStateAsync circuit ct =
            widest.Value <- max widest.Value circuit.NumQubits
            inner.ExecuteToStateAsync circuit ct

        member _.ApplyOperationAsync op state ct = inner.ApplyOperationAsync op state ct

    interface BackendAbstraction.IQubitLimitedBackend with
        member _.MaxQubits = Some holds

    interface BackendAbstraction.IWallClockLimitedBackend with
        member _.PracticalQubits = runs

/// LocalBackend that presents itself as a backend billing every circuit as a job.
type private BilledBackend(maxQubits: int) =
    let inner = LocalBackend.LocalBackend() :> BackendAbstraction.IQuantumBackend
    let widest = ref 0

    member _.WidestCircuit = widest.Value

    interface BackendAbstraction.IQuantumBackend with
        member _.Name = inner.Name
        member _.NativeStateType = inner.NativeStateType
        member _.SupportsOperation op = inner.SupportsOperation op
        member _.InitializeState n = inner.InitializeState n
        member _.ApplyOperation op state = inner.ApplyOperation op state

        member _.ExecuteToState circuit =
            widest.Value <- max widest.Value circuit.NumQubits
            inner.ExecuteToState circuit

        member _.ExecuteToStateAsync circuit ct =
            widest.Value <- max widest.Value circuit.NumQubits
            inner.ExecuteToStateAsync circuit ct

        member _.ApplyOperationAsync op state ct = inner.ApplyOperationAsync op state ct

    interface BackendAbstraction.IQubitLimitedBackend with
        member _.MaxQubits = Some maxQubits

    interface BackendAbstraction.IShotSamplingBackend with
        member _.Shots = 100

let private withSplitting (settings: QaoaExecutionHelpers.SplitSettings) =
    { quickConfig with
        Splitting = settings
    }

let private ring12 = sparseQubo 4 12 0

[<Fact>]
let ``default split settings split on simulators only`` () =
    let settings = QaoaExecutionHelpers.defaultSplitSettings
    Assert.Equal(QaoaExecutionHelpers.SplitPolicy.OnSimulators, settings.Policy)
    Assert.Equal(ValueNone, settings.MaxPieceQubits)
    Assert.Equal(256, settings.MaxShareRuns)
    Assert.Equal(12, settings.MaxBlockItems)
    Assert.Equal(0, settings.MaxSweeps)
    Assert.Equal(settings, QaoaExecutionHelpers.defaultConfig.Splitting)
    Assert.Equal(settings, QuantumMaxCutSolver.defaultConfig.Splitting)
    Assert.Equal(settings, QuantumKnapsackSolver.defaultConfig.Splitting)

[<Fact>]
let ``splitPieceQubits follows the policy, the backend and the piece cap`` () =
    let settings = QaoaExecutionHelpers.defaultSplitSettings
    let simulator = NarrowBackend 5 :> BackendAbstraction.IQuantumBackend
    let billed = BilledBackend 5 :> BackendAbstraction.IQuantumBackend

    let piece policy backend width =
        QaoaExecutionHelpers.splitPieceQubits { settings with Policy = policy } backend width

    // a problem that fits is never split
    Assert.Equal(ValueNone, piece QaoaExecutionHelpers.SplitPolicy.Always simulator 5)
    // simulators: split by default, not under Never
    Assert.Equal(ValueSome 5, piece QaoaExecutionHelpers.SplitPolicy.OnSimulators simulator 12)
    Assert.Equal(ValueNone, piece QaoaExecutionHelpers.SplitPolicy.Never simulator 12)
    // a backend that bills every circuit: only under Always
    Assert.Equal(ValueNone, piece QaoaExecutionHelpers.SplitPolicy.OnSimulators billed 12)
    Assert.Equal(ValueSome 5, piece QaoaExecutionHelpers.SplitPolicy.Always billed 12)

    // the piece cap narrows the backend's limit and never widens it
    let capped cap =
        QaoaExecutionHelpers.splitPieceQubits
            { settings with
                MaxPieceQubits = ValueSome cap
            }
            simulator
            12

    Assert.Equal(ValueSome 3, capped 3)
    Assert.Equal(ValueSome 5, capped 9)

[<Fact>]
let ``SplitPolicy Never sends a wide QUBO as one circuit`` () : Task =
    task {
        let narrow = NarrowBackend 5

        let config =
            withSplitting
                { QaoaExecutionHelpers.defaultSplitSettings with
                    Policy = QaoaExecutionHelpers.SplitPolicy.Never
                }

        let! result = QuboSplitting.runQaoaAsync narrow ring12 config CancellationToken.None
        let run = ok result

        Assert.True(run.Direct.IsSome)
        Assert.True(run.Split.IsNone)
        Assert.Equal(12, narrow.WidestCircuit)
    }

[<Fact>]
let ``a billed backend is split only when the policy is Always`` () : Task =
    task {
        let unasked = BilledBackend 5

        let! direct =
            QuboSplitting.runQaoaAsync unasked ring12 quickConfig CancellationToken.None

        Assert.True((ok direct).Split.IsNone)
        Assert.Equal(12, unasked.WidestCircuit)

        let asked = BilledBackend 5

        let config =
            withSplitting
                { QaoaExecutionHelpers.defaultSplitSettings with
                    Policy = QaoaExecutionHelpers.SplitPolicy.Always
                }

        let! split = QuboSplitting.runQaoaAsync asked ring12 config CancellationToken.None
        Assert.True((ok split).Split.IsSome)
        Assert.InRange(asked.WidestCircuit, 1, 5)
    }

[<Fact>]
let ``MaxPieceQubits makes the pieces narrower than the backend`` () : Task =
    task {
        let roomy = NarrowBackend 8

        let config =
            withSplitting
                { QaoaExecutionHelpers.defaultSplitSettings with
                    MaxPieceQubits = ValueSome 4
                }

        let! result = QuboSplitting.runQaoaAsync roomy ring12 config CancellationToken.None

        match (ok result).Split with
        | Some report -> Assert.InRange(report.WidestPieceQubits, 1, 4)
        | None -> Assert.Fail("expected a split")

        Assert.InRange(roomy.WidestCircuit, 1, 4)
    }

[<Fact>]
let ``a QUBO that does not cut within the limit runs as one circuit while the backend holds it`` () : Task =
    task {
        // 9 variables, all coupled. The backend runs 5 and holds 9.
        let dense =
            Array2D.init 9 9 (fun i j ->
                if i < j then 1.0
                elif i = j then -2.0
                else 0.0)

        let roomy = RoomyBackend(5, 9)

        // A piece of 5 touches the 4 fixed variables and runs 16 times; only 8 runs are allowed.
        let config =
            withSplitting
                { QaoaExecutionHelpers.defaultSplitSettings with
                    MaxShareRuns = 8
                }

        let! result = QuboSplitting.runQaoaAsync roomy dense config CancellationToken.None
        let run = ok result

        Assert.True(run.Split.IsNone)
        Assert.True(run.Direct.IsSome)
        Assert.Equal(9, roomy.WidestCircuit)
    }

[<Fact>]
let ``a QUBO that neither cuts nor fits is refused with the reason`` () : Task =
    task {
        let dense =
            Array2D.init 9 9 (fun i j ->
                if i < j then 1.0
                elif i = j then -2.0
                else 0.0)

        let narrow = NarrowBackend 5

        let config =
            withSplitting
                { QaoaExecutionHelpers.defaultSplitSettings with
                    MaxShareRuns = 8
                }

        match! QuboSplitting.runQaoaAsync narrow dense config CancellationToken.None with
        | Error(QuantumError.ValidationError(field, message)) ->
            Assert.Equal("qubits", field)
            Assert.Contains("needs 16 circuit runs, more than SplitSettings.MaxShareRuns = 8", message)
        | other -> Assert.Fail($"Expected a qubits validation error, got %A{other}")

        Assert.Equal(0, narrow.WidestCircuit)
    }

[<Fact>]
let ``split solutions report how they were put together`` () : Task =
    task {
        let cover: QuantumVertexCoverSolver.Problem =
            {
                Vertices = [ for i in 0..11 -> { Id = string i; Weight = 1.0 } ]
                Edges = [ for i in 0..10 -> (i, i + 1) ]
            }

        let! covered =
            QuantumVertexCoverSolver.solveWithConfigAsync (NarrowBackend 5) cover quickConfig CancellationToken.None

        match (ok covered).Split with
        | Some report ->
            Assert.True(report.FixedVariables > 0)
            Assert.InRange(report.WidestPieceQubits, 1, 5)
            Assert.True(report.Runs >= 2)
        | None -> Assert.Fail("vertex cover: expected a split report")

        let! fitting =
            QuantumVertexCoverSolver.solveWithConfigAsync (NarrowBackend 12) cover quickConfig CancellationToken.None

        Assert.True((ok fitting).Split.IsNone)
        Assert.True((ok fitting).Sampling.IsSome)

        let! packed =
            QuantumKnapsackSolver.solveInBlocksAsync
                (NarrowBackend 16)
                (knapsackOf 21 12 14.0)
                quickConfig
                4
                CancellationToken.None

        match (ok packed).Split with
        | Some report ->
            Assert.Equal(3, report.Blocks)
            Assert.Equal(0, report.FixedVariables)
            Assert.Equal(4, report.WidestPieceQubits)
            Assert.True(report.Runs >= 3)
        | None -> Assert.Fail("knapsack: expected a split report")
    }

[<Fact>]
let ``knapsack follows the split policy and the run limit`` () : Task =
    task {
        let problem = knapsackOf 22 9 12.0
        let width = (ok (QuantumKnapsackSolver.toQubo problem)).NumVariables

        let configWith (settings: QaoaExecutionHelpers.SplitSettings) : QuantumKnapsackSolver.QaoaConfig =
            { QuantumKnapsackSolver.defaultConfig with
                NumShots = 100
                Splitting = settings
            }

        // Never: one circuit, as wide as the QUBO
        let unsplit = RoomyBackend(6, 16)

        let! direct =
            QuantumKnapsackSolver.solveAsync
                unsplit
                problem
                (configWith
                    { QaoaExecutionHelpers.defaultSplitSettings with
                        Policy = QaoaExecutionHelpers.SplitPolicy.Never
                    })
                CancellationToken.None

        Assert.True((ok direct).Split.IsNone)
        Assert.Equal(width, unsplit.WidestCircuit)

        // Too few runs allowed: one circuit while the backend holds it...
        let tight =
            { QaoaExecutionHelpers.defaultSplitSettings with
                MaxShareRuns = 1
            }

        let holding = RoomyBackend(6, 16)

        let! held =
            QuantumKnapsackSolver.solveAsync holding problem (configWith tight) CancellationToken.None

        Assert.True((ok held).Split.IsNone)
        Assert.Equal(width, holding.WidestCircuit)

        // ...and an error naming the limit when it does not
        match! QuantumKnapsackSolver.solveAsync (NarrowBackend 6) problem (configWith tight) CancellationToken.None with
        | Error(QuantumError.ValidationError(field, message)) ->
            Assert.Equal("qubits", field)
            Assert.Contains("MaxShareRuns = 1", message)
        | other -> Assert.Fail($"Expected a qubits validation error, got %A{other}")
    }

[<Fact>]
let ``vertex cover wider than the anyon budget is split on the topological simulator`` () : Task =
    task {
        // 10 Ising anyons hold 4 logical qubits; the path of 8 vertices needs 8.
        let topological =
            FSharp.Azure.Quantum.Topological.TopologicalUnifiedBackendFactory.createIsing 10

        let cover: QuantumVertexCoverSolver.Problem =
            {
                Vertices = [ for i in 0..7 -> { Id = string i; Weight = 1.0 } ]
                Edges = [ for i in 0..6 -> (i, i + 1) ]
            }

        let! result =
            QuantumVertexCoverSolver.solveWithConfigAsync topological cover quickConfig CancellationToken.None

        let solution = ok result

        Assert.True(solution.IsValid, "repair is on, so the cover is valid")

        match solution.Split with
        | Some report ->
            Assert.InRange(report.WidestPieceQubits, 1, 4)
            Assert.True(report.FixedVariables > 0)
        | None -> Assert.Fail("expected a split on a 4-qubit anyon budget")
    }

[<Fact>]
let ``chooseFixedVariables cuts a chain in the middle and takes the hub out of a star`` () =
    let chain (n: int) =
        Array2D.init n n (fun i j -> if j = i + 1 then 1.0 else 0.0)

    // 100 variables in a chain, pieces of 12: 7 cuts leave 8 pieces of at most 12
    Assert.Equal(7, (QuboSplitting.chooseFixedVariables 12 (chain 100)).Length)
    Assert.Equal<int list>([ 3 ], QuboSplitting.chooseFixedVariables 3 (chain 7))

    // a star: fixing the hub leaves single leaves
    let star = Array2D.init 9 9 (fun i j -> if i = 4 && j <> 4 then 1.0 else 0.0)
    Assert.Equal<int list>([ 4 ], QuboSplitting.chooseFixedVariables 1 star)

// ----------------------------------------------------------------------------
// Subset-sum search (findAllExactCombinationsAsync) over more items than the backend runs
// ----------------------------------------------------------------------------

let private subsetItems (weights: int list) : QuantumKnapsackSolver.KnapsackItem list =
    weights
    |> List.mapi (fun i w ->
        {
            Id = string i
            Weight = float w
            Value = float w
        })

/// Every subset of the weights that sums to the target, as sorted id lists.
let private exactSubsetIds (weights: int list) (target: int) : string list list =
    let n = weights.Length
    let array = List.toArray weights

    [
        for mask in 1 .. (1 <<< n) - 1 do
            let chosen =
                [
                    for i in 0 .. n - 1 do
                        if (mask >>> i) &&& 1 = 1 then
                            i
                ]

            if (chosen |> List.sumBy (fun i -> array.[i])) = target then
                yield chosen |> List.map string |> List.sort
    ]
    |> List.sort

let private idsOf (combinations: QuantumKnapsackSolver.KnapsackItem list list) : string list list =
    combinations
    |> List.map (List.map (fun item -> item.Id) >> List.sort)
    |> List.sort

[<Fact>]
let ``subset-sum search splits items wider than the backend and finds only exact subsets`` () : Task =
    task {
        // 12 items of weight at most 7, target 7: a subset may take items from both halves
        let weights = [ 7; 5; 2; 3; 4; 1; 6; 1; 2; 5; 3; 4 ]
        let narrow = NarrowBackend 6

        let! result =
            QuantumKnapsackSolver.findAllExactCombinationsAsync
                narrow
                (subsetItems weights)
                7.0
                QuantumKnapsackSolver.defaultSubsetSumConfig
                CancellationToken.None

        let found = ok result
        let all = exactSubsetIds weights 7 |> Set.ofList
        let got = idsOf found.Combinations

        Assert.True(Set.isSubset (Set.ofList got) all, "every returned subset sums to the target")
        Assert.Equal(got.Length, (List.distinct got).Length)
        Assert.NotEmpty got
        Assert.InRange(narrow.WidestCircuit, 1, 6)

        match found.Split with
        | Some report ->
            Assert.InRange(report.WidestPieceQubits, 1, 6)
            Assert.True(report.Blocks >= 2)
            Assert.True(report.Runs >= 2)
            Assert.Equal(0, report.FixedVariables)
        | None -> Assert.Fail("expected a split report")
    }

[<Fact>]
let ``subset-sum search that fits runs as one circuit and leaves out items heavier than the target`` () : Task =
    task {
        let weights = [ 2; 5; 3; 4; 9; 11 ]
        let narrow = NarrowBackend 4

        let! result =
            QuantumKnapsackSolver.findAllExactCombinationsAsync
                narrow
                (subsetItems weights)
                7.0
                QuantumKnapsackSolver.defaultSubsetSumConfig
                CancellationToken.None

        let found = ok result

        // 9 and 11 take no qubit, so the four remaining items fit the 4-qubit backend
        Assert.True(found.Split.IsNone)
        Assert.Equal(4, narrow.WidestCircuit)
        Assert.True(Set.isSubset (Set.ofList (idsOf found.Combinations)) (Set.ofList (exactSubsetIds weights 7)))
    }

[<Fact>]
let ``subset-sum search follows the split policy and the run limit`` () : Task =
    task {
        let weights = [ 7; 5; 2; 3; 4; 1; 6; 1; 2; 5; 3; 4 ]

        let configWith (settings: QaoaExecutionHelpers.SplitSettings) =
            { QuantumKnapsackSolver.defaultSubsetSumConfig with
                NumShots = 200
                MaxIterations = 4
                Splitting = settings
            }

        // Never: one circuit over all twelve items
        let unsplit = NarrowBackend 6

        let! direct =
            QuantumKnapsackSolver.findAllExactCombinationsAsync
                unsplit
                (subsetItems weights)
                7.0
                (configWith
                    { QaoaExecutionHelpers.defaultSplitSettings with
                        Policy = QaoaExecutionHelpers.SplitPolicy.Never
                    })
                CancellationToken.None

        Assert.True((ok direct).Split.IsNone)
        Assert.Equal(12, unsplit.WidestCircuit)

        // one search allowed where the split needs several
        match!
            QuantumKnapsackSolver.findAllExactCombinationsAsync
                (NarrowBackend 6)
                (subsetItems weights)
                7.0
                (configWith
                    { QaoaExecutionHelpers.defaultSplitSettings with
                        MaxShareRuns = 1
                    })
                CancellationToken.None
        with
        | Error(QuantumError.ValidationError(field, message)) ->
            Assert.Equal("qubits", field)
            Assert.Contains("MaxShareRuns = 1", message)
        | other -> Assert.Fail($"Expected a qubits validation error, got %A{other}")
    }

[<Fact>]
let ``MaxPieceQubits splits a problem the backend could run as one circuit`` () =
    let settings =
        { QaoaExecutionHelpers.defaultSplitSettings with
            MaxPieceQubits = ValueSome 4
        }

    let roomy = NarrowBackend 16 :> BackendAbstraction.IQuantumBackend
    Assert.Equal(ValueSome 4, QaoaExecutionHelpers.splitPieceQubits settings roomy 10)
    Assert.Equal(ValueNone, QaoaExecutionHelpers.splitPieceQubits settings roomy 4)
    Assert.Equal(ValueNone, QaoaExecutionHelpers.splitPieceQubits QaoaExecutionHelpers.defaultSplitSettings roomy 10)

// ----------------------------------------------------------------------------
// Limits of the split itself: runs per piece, refusals, and what the report adds up
// ----------------------------------------------------------------------------

/// Seeded chain: linear terms and a coupling between neighbours only.
let private chainQubo (seed: int) (n: int) : float[,] =
    let rng = Random(seed)
    let weight () = float (rng.Next(-9, 10)) / 2.0

    Array2D.init n n (fun i j ->
        if i = j then weight ()
        elif j = i + 1 then weight () + 0.25
        else 0.0)

/// Minimum energy of a chain QUBO, by carrying the best energy for each value of the last variable.
let private chainMinimum (qubo: float[,]) : float =
    let n = Array2D.length1 qubo

    let off, on =
        seq { 1 .. n - 1 }
        |> Seq.fold (fun (off, on) i -> min off on, qubo.[i, i] + min off (on + qubo.[i - 1, i])) (0.0, qubo.[0, 0])

    min off on

[<Fact>]
let ``a piece runs once per assignment of the fixed variables it touches`` () : Task =
    task {
        for seed in 1..5 do
            let qubo = chainQubo seed 40
            let widths = ResizeArray<int>()

            let! solved =
                QuboSplitting.solvePiecewiseAsync (limits 6 256) qubo (exactPieces widths) CancellationToken.None

            let solution = ok solved
            let cuts = solution.FixedVariables.Length

            Assert.Equal(chainMinimum qubo, solution.Energy, 9)
            Assert.Equal(solution.Energy, energy qubo solution.Bits, 9)
            Assert.Equal(widths.Count, solution.PiecesSolved)
            // a piece of a chain touches at most two cuts: four runs, not 2^cuts
            Assert.InRange(cuts, 5, 8)
            Assert.InRange(solution.PiecesSolved, cuts + 1, 4 * (cuts + 1))
            Assert.All(widths, (fun width -> Assert.InRange(width, 1, 6)))
    }

[<Fact>]
let ``pieces of one variable join into the minimum however many variables are fixed`` () : Task =
    task {
        // most variables fixed: the join, not the pieces, finds the minimum
        for seed in 1..8 do
            let qubo = sparseQubo seed 16 4
            let widths = ResizeArray<int>()

            let! solved =
                QuboSplitting.solvePiecewiseAsync (limits 1 4096) qubo (exactPieces widths) CancellationToken.None

            let solution = ok solved
            Assert.Equal(snd (minimumOf qubo), solution.Energy, 9)
            Assert.Equal(solution.Energy, energy qubo solution.Bits, 9)
            Assert.InRange(solution.FixedVariables.Length, 8, 16)

        // a chain of 64 cut into single variables: dozens of fixed variables, a few runs each
        for seed in 1..3 do
            let qubo = chainQubo seed 64
            let widths = ResizeArray<int>()

            let! solved =
                QuboSplitting.solvePiecewiseAsync (limits 1 256) qubo (exactPieces widths) CancellationToken.None

            let solution = ok solved
            Assert.InRange(solution.FixedVariables.Length, 21, 63)
            Assert.InRange(solution.PiecesSolved, 1, 256)
            Assert.Equal(chainMinimum qubo, solution.Energy, 9)
            Assert.Equal(solution.Energy, energy qubo solution.Bits, 9)
    }

[<Fact>]
let ``a split over the run limit is refused before any piece runs`` () : Task =
    task {
        // a chain of 40 in pieces of 6 runs a few times per piece, more than 10 in all
        let widths = ResizeArray<int>()

        match!
            QuboSplitting.solvePiecewiseAsync (limits 6 10) (chainQubo 1 40) (exactPieces widths) CancellationToken.None
        with
        | Error(QuantumError.ValidationError(field, message)) ->
            Assert.Equal("qubits", field)
            Assert.Contains("circuit runs, more than MaxRuns = 10", message)
        | other -> Assert.Fail($"Expected a qubits validation error, got %A{other}")

        Assert.Empty widths
    }

/// Seeded graph of degree 3: a ring and a random perfect matching across it.
let private cubicQubo (seed: int) (n: int) : float[,] =
    let rng = Random(seed)
    let matching = Array.init n id |> Array.sortBy (fun _ -> rng.Next())
    let qubo = Array2D.zeroCreate n n

    let couple i j =
        if i <> j then
            qubo.[min i j, max i j] <- 1.0

    for i in 0 .. n - 1 do
        qubo.[i, i] <- -1.0
        couple i ((i + 1) % n)

    for k in 0..2 .. n - 2 do
        couple matching.[k] matching.[k + 1]

    qubo

[<Fact>]
let ``a split whose join is too wide is refused before any piece runs`` () : Task =
    task {
        // a sparse graph with no narrow structure: runs are allowed, the join is not
        let widths = ResizeArray<int>()

        match!
            QuboSplitting.solvePiecewiseAsync
                (limits 16 Int32.MaxValue)
                (cubicQubo 5 256)
                (exactPieces widths)
                CancellationToken.None
        with
        | Error(QuantumError.ValidationError(field, message)) ->
            Assert.Equal("qubits", field)
            Assert.Contains($"QuboSplitting.MostJoinSpan = {QuboSplitting.MostJoinSpan}", message)
        | other -> Assert.Fail($"Expected a qubits validation error, got %A{other}")

        Assert.Empty widths
    }

[<Fact>]
let ``independent pieces are not packed into one circuit`` () : Task =
    task {
        // a star of eight leaves: with the hub fixed every leaf is a circuit of its own
        let star =
            Array2D.init 9 9 (fun i j ->
                if i = j then -1.0
                elif i = 0 then 1.5
                else 0.0)

        let widths = ResizeArray<int>()

        let! solved =
            QuboSplitting.solvePiecewiseAsync (limits 4 16) star (exactPieces widths) CancellationToken.None

        let solution = ok solved

        Assert.Equal<int list>([ 0 ], solution.FixedVariables)
        Assert.Equal(16, solution.PiecesSolved)
        Assert.All(widths, (fun width -> Assert.Equal(1, width)))
        Assert.Equal(snd (minimumOf star), solution.Energy, 9)
    }

[<Fact>]
let ``a wide dense QUBO is refused without running anything`` () : Task =
    task {
        let dense = Array2D.init 300 300 (fun i j -> if i < j then 1.0 else 0.0)
        let widths = ResizeArray<int>()

        match! QuboSplitting.solvePiecewiseAsync (limits 12 8) dense (exactPieces widths) CancellationToken.None with
        | Error(QuantumError.ValidationError(field, message)) ->
            Assert.Equal("qubits", field)
            Assert.Contains("fixes 300 variables", message)
            Assert.Contains("MostJoinSpan", message)
        | other -> Assert.Fail($"Expected a qubits validation error, got %A{other}")

        Assert.Empty widths
    }

[<Theory; InlineData(-1); InlineData(0)>]
let ``MaxRuns below 1 is refused`` (runs: int) : Task =
    task {
        let widths = ResizeArray<int>()

        match!
            QuboSplitting.solvePiecewiseAsync
                (limits 5 runs)
                (sparseQubo 2 12 1)
                (exactPieces widths)
                CancellationToken.None
        with
        | Error(QuantumError.ValidationError(field, _)) -> Assert.Equal("MaxRuns", field)
        | other -> Assert.Fail($"Expected a MaxRuns validation error, got %A{other}")

        Assert.Empty widths
    }

[<Fact>]
let ``a backend that reports no qubits still gets pieces of one qubit`` () : Task =
    task {
        let none = NarrowBackend 0 :> BackendAbstraction.IQuantumBackend

        Assert.Equal(
            ValueSome 1,
            QaoaExecutionHelpers.splitPieceQubits QaoaExecutionHelpers.defaultSplitSettings none 3
        )
    }

[<Fact>]
let ``share runs are counted, not listed, so a huge capacity is refused at once`` () : Task =
    task {
        let weights = Array.init 24 (fun i -> 900_000_000 + i)
        let values = Array.create 24 1.0
        let capacity = 2_000_000_000

        // three blocks of eight, each asked for every share up to the capacity
        Assert.Equal(3L * 2_000_000_000L, QuboSplitting.shareRuns 8 weights capacity)

        let runs = ResizeArray<int>()

        match!
            QuboSplitting.solveSharesAsync
                (shareLimits 8 256)
                values
                weights
                capacity
                (everySubset runs)
                CancellationToken.None
        with
        | Error(QuantumError.ValidationError(field, _)) -> Assert.Equal("capacity", field)
        | other -> Assert.Fail($"Expected a capacity validation error, got %A{other}")

        Assert.Empty runs
    }

[<Fact>]
let ``harvest ignores samples whose weight is beyond the capacity or the integer range`` () =
    let items = [| 0; 1; 2 |]
    let values = [| 1.0; 2.0; 4.0 |]
    let weights = [| 2_000_000_000; 2_000_000_000; 3 |]
    let samples = [| [| 1; 1; 0 |]; [| 0; 0; 1 |]; [| 1; 1; 1 |] |]
    let table = QuboSplitting.harvest items values weights 10 samples Map.empty

    Assert.Equal<(int * (float * int list)) list>([ 3, (4.0, [ 2 ]) ], Map.toList table)

[<Fact>]
let ``combineSplitReports adds up the parts and keeps the widest piece`` () =
    let report runs fixedVariables blocks widest sweeps : QaoaExecutionHelpers.SplitReport =
        {
            Runs = runs
            FixedVariables = fixedVariables
            Blocks = blocks
            WidestPieceQubits = widest
            Sweeps = sweeps
        }

    Assert.Equal(None, QaoaExecutionHelpers.combineSplitReports [ None; None ])
    Assert.Equal(None, QaoaExecutionHelpers.combineSplitReports [])

    Assert.Equal(
        Some(report 13 3 2 5 3),
        QaoaExecutionHelpers.combineSplitReports [ Some(report 6 1 0 4 0); None; Some(report 7 2 2 5 3) ]
    )

[<Fact>]
let ``a graph of several components reports the splits of all of them`` () : Task =
    task {
        // two separate paths of eight vertices, each wider than the 5-qubit backend
        let cover: QuantumVertexCoverSolver.Problem =
            {
                Vertices = [ for i in 0..15 -> { Id = string i; Weight = 1.0 } ]
                Edges =
                    [
                        for i in 0..14 do
                            if i <> 7 then
                                (i, i + 1)
                    ]
            }

        let narrow = NarrowBackend 5

        let! result =
            QuantumVertexCoverSolver.solveWithConfigAsync narrow cover quickConfig CancellationToken.None

        let solution = ok result

        Assert.True(solution.IsValid, "repair is on, so the cover is valid")
        Assert.InRange(narrow.WidestCircuit, 1, 5)

        match solution.Split with
        | Some report ->
            Assert.True(report.FixedVariables >= 2, "each path needs a cut of its own")
            Assert.True(report.Runs >= 4)
            Assert.InRange(report.WidestPieceQubits, 1, 5)
        | None -> Assert.Fail("expected the splits of both components in the report")
    }

[<Fact>]
let ``subset-sum search over halves returns every subset when its pieces do`` () : Task =
    task {
        // Weights of at most 4 and a small target: every piece has few exact subsets, so the
        // search of a piece finds all of them and the join has to produce all of the whole.
        let weights = [ 1; 2; 3; 4; 1; 2; 3; 4 ]
        let narrow = NarrowBackend 4

        let! result =
            QuantumKnapsackSolver.findAllExactCombinationsAsync
                narrow
                (subsetItems weights)
                4.0
                { QuantumKnapsackSolver.defaultSubsetSumConfig with
                    NumShots = 2000
                }
                CancellationToken.None

        let found = ok result

        Assert.Equal<string list list>(exactSubsetIds weights 4, idsOf found.Combinations)
        Assert.InRange(narrow.WidestCircuit, 1, 4)
    }

// ----------------------------------------------------------------------------
// Neighbourhood search: a heuristic for QUBOs that no exact split cuts
// ----------------------------------------------------------------------------

/// Every variable coupled to every other.
let private denseQubo (n: int) : float[,] =
    Array2D.init n n (fun i j ->
        if i < j then 1.0
        elif i = j then -2.0
        else 0.0)

/// Three blocks of four with random couplings inside each block and none between them, so
/// the blocks a sweep grows by coupling are the true blocks and the optimum is theirs.
let private blockDiagonalQubo (seed: int) : float[,] =
    let rng = Random(seed)
    let qubo = Array2D.zeroCreate 12 12
    let weight () = float (rng.Next(-9, 10)) / 2.0 + 0.25

    for block in 0..2 do
        for a in 0..3 do
            let i = 4 * block + a
            qubo.[i, i] <- weight ()

            for b in a + 1 .. 3 do
                qubo.[i, 4 * block + b] <- weight ()

    qubo

let private searchLimits (piece: int) (sweeps: int) (runs: int) : QuboSplitting.NeighbourhoodLimits =
    {
        MaxPieceQubits = piece
        MaxSweeps = sweeps
        MaxRuns = runs
    }

[<Fact>]
let ``quboNeighbourhoods cuts every variable into one block within the width and shifts the blocks between sweeps`` () =
    let qubo = denseQubo 9
    let neighbourhoods = QuboSplitting.quboNeighbourhoods 4 qubo

    let incumbent: QuboSplitting.QuboIncumbent =
        {
            Bits = Array.zeroCreate 9
            Energy = 0.0
        }

    let first = neighbourhoods.OfSweep 0 incumbent
    Assert.Equal(3, first.Length)
    Assert.Equal<int list>([ 0..8 ], first |> Array.concat |> Array.sort |> Array.toList)

    for block in first do
        Assert.InRange(block.Length, 1, 4)

    // the second sweep starts its seeds two variables on, so the blocks straddle the first
    let second = neighbourhoods.OfSweep 1 incumbent
    Assert.Equal<int list>([ 0..8 ], second |> Array.concat |> Array.sort |> Array.toList)
    Assert.NotEqual<int list>(first.[0] |> Array.toList, second.[0] |> Array.toList)

    // a block of a coupled QUBO is grown by coupling: the true blocks come out whole
    let blocks = (QuboSplitting.quboNeighbourhoods 4 (blockDiagonalQubo 1)).OfSweep 0 incumbent

    Assert.Equal<int list list>(
        [ [ 0..3 ]; [ 4..7 ]; [ 8..11 ] ],
        blocks |> Array.map Array.toList |> Array.sortBy List.head |> Array.toList
    )

[<Fact>]
let ``quboNeighbourhoods keeps the energy of the bits it writes`` () =
    let qubo = sparseQubo 5 10 3
    let neighbourhoods = QuboSplitting.quboNeighbourhoods 4 qubo
    let rng = Random(7)

    for _ in 1..20 do
        let bits = Array.init 10 (fun _ -> rng.Next 2)

        let incumbent: QuboSplitting.QuboIncumbent =
            {
                Bits = bits
                Energy = energy qubo bits
            }

        let block = (neighbourhoods.OfSweep (rng.Next 5) incumbent).[rng.Next 3]
        let sampled = Array.init block.Length (fun _ -> rng.Next 2)

        match neighbourhoods.Apply incumbent block sampled with
        | ValueSome written ->
            block |> Array.iteri (fun row v -> Assert.Equal(sampled.[row], written.Bits.[v]))

            for v in 0..9 do
                if not (System.Linq.Enumerable.Contains(block, v)) then
                    Assert.Equal(bits.[v], written.Bits.[v])

            Assert.Equal(energy qubo written.Bits, written.Energy, 9)
            Assert.Equal(written.Energy, neighbourhoods.Score written)
        | ValueNone -> Assert.Fail("a sample of the block's width is a solution")

        Assert.Equal(ValueNone, neighbourhoods.Apply incumbent block (Array.sub sampled 0 (block.Length - 1)))

[<Fact>]
let ``a neighbourhood search with every block sample reaches the optimum of a QUBO whose couplings stay within its blocks``
    ()
    : Task =
    task {
        for seed in 1..10 do
            let qubo = blockDiagonalQubo seed
            let runs = ResizeArray<int>()

            let! result =
                QuboSplitting.searchQuboNeighbourhoodsAsync
                    (searchLimits 4 5 100)
                    qubo
                    (Array.zeroCreate 12)
                    (everySubset runs)
                    CancellationToken.None

            let found = ok result
            let _, lowest = minimumOf qubo

            Assert.Equal(lowest, found.Score, 9)
            Assert.Equal(lowest, energy qubo found.Solution, 9)
            // one sweep improves what it can; the next improves nothing and ends the search
            Assert.Equal(2, found.Sweeps)
            Assert.Equal(6, found.Runs)
            Assert.Equal(6, runs.Count)
            Assert.Equal(4, found.WidestPieceQubits)
            Assert.InRange(found.Improvements, 0, 3)
    }

[<Fact>]
let ``a neighbourhood search is bounded by its sweeps and its runs`` () : Task =
    task {
        let qubo = denseQubo 9

        // one sweep only: three blocks, three runs
        let runs = ResizeArray<int>()

        let! one =
            QuboSplitting.searchQuboNeighbourhoodsAsync
                (searchLimits 4 1 100)
                qubo
                (Array.zeroCreate 9)
                (everySubset runs)
                CancellationToken.None

        Assert.Equal(1, (ok one).Sweeps)
        Assert.Equal(3, (ok one).Runs)
        Assert.Equal(3, runs.Count)

        // a second sweep would pass 4 runs, so the search ends after the first
        let runs = ResizeArray<int>()

        let! bounded =
            QuboSplitting.searchQuboNeighbourhoodsAsync
                (searchLimits 4 5 4)
                qubo
                (Array.zeroCreate 9)
                (everySubset runs)
                CancellationToken.None

        Assert.Equal(1, (ok bounded).Sweeps)
        Assert.Equal(3, runs.Count)

        // a first sweep over more blocks than the limit is refused before any run
        let runs = ResizeArray<int>()

        match!
            QuboSplitting.searchQuboNeighbourhoodsAsync
                (searchLimits 4 5 2)
                qubo
                (Array.zeroCreate 9)
                (everySubset runs)
                CancellationToken.None
        with
        | Error(QuantumError.ValidationError(field, message)) ->
            Assert.Equal("MaxRuns", field)
            Assert.Contains("one sweep samples 3 neighbourhoods, more than MaxRuns = 2", message)
        | other -> Assert.Fail($"Expected a MaxRuns validation error, got %A{other}")

        Assert.Empty runs
    }

[<Fact>]
let ``a neighbourhood search takes only accepted solutions and fails with none`` () : Task =
    task {
        let qubo = denseQubo 9

        // only assignments with the first bit set are acceptable; the all-zero start is not
        let gated =
            { QuboSplitting.quboNeighbourhoods 4 qubo with
                Accept = fun incumbent -> incumbent.Bits.[0] = 1
            }

        let start: QuboSplitting.QuboIncumbent =
            {
                Bits = Array.zeroCreate 9
                Energy = 0.0
            }

        let! result =
            QuboSplitting.searchNeighbourhoodsAsync
                (searchLimits 4 5 100)
                gated
                start
                (everySubset (ResizeArray()))
                CancellationToken.None

        let found = ok result
        Assert.Equal(1, found.Solution.Bits.[0])
        Assert.True(found.Improvements >= 1)
        Assert.Equal(energy qubo found.Solution.Bits, found.Score, 9)

        // nothing accepted: the search still lowers the score (two sweeps, six runs) and
        // then fails for want of an accepted solution
        let refusing =
            { QuboSplitting.quboNeighbourhoods 4 qubo with
                Accept = fun _ -> false
            }

        match!
            QuboSplitting.searchNeighbourhoodsAsync
                (searchLimits 4 2 100)
                refusing
                start
                (everySubset (ResizeArray()))
                CancellationToken.None
        with
        | Error(QuantumError.OperationError(operation, message)) ->
            Assert.Equal("QuboSplitting", operation)
            Assert.Contains("no sample of 6 neighbourhood runs gave an acceptable solution", message)
        | other -> Assert.Fail($"Expected an operation error, got %A{other}")
    }

[<Fact>]
let ``a neighbourhood search moves into acceptance before it demands it`` () : Task =
    task {
        let qubo = denseQubo 9

        // acceptable: at least two bits set. The all-zero start is not; a block of four with
        // every sample on offer gets there in one step and the search keeps it from then on
        let gated =
            { QuboSplitting.quboNeighbourhoods 4 qubo with
                Accept = fun incumbent -> Array.sum incumbent.Bits >= 2
            }

        let start: QuboSplitting.QuboIncumbent =
            {
                Bits = Array.zeroCreate 9
                Energy = 0.0
            }

        let! result =
            QuboSplitting.searchNeighbourhoodsAsync
                (searchLimits 4 5 100)
                gated
                start
                (everySubset (ResizeArray()))
                CancellationToken.None

        let found = ok result
        Assert.True(Array.sum found.Solution.Bits >= 2)
        Assert.Equal(energy qubo found.Solution.Bits, found.Score, 9)
        // the dense QUBO's optimum sets every bit, which the search reaches block by block
        Assert.Equal(snd (minimumOf qubo), found.Score, 9)
    }

[<Fact>]
let ``runQaoaAsync searches neighbourhoods when a QUBO neither cuts nor fits and MaxSweeps allows`` () : Task =
    task {
        let narrow = NarrowBackend 5

        let config =
            withSplitting
                { QaoaExecutionHelpers.defaultSplitSettings with
                    MaxShareRuns = 8
                    MaxSweeps = 2
                }

        let! result = QuboSplitting.runQaoaAsync narrow (denseQubo 9) config CancellationToken.None
        let run = ok result

        Assert.Equal(9, run.Best.Length)
        Assert.True(run.Direct.IsNone)
        Assert.InRange(narrow.WidestCircuit, 1, 5)

        match run.Split with
        | Some report ->
            Assert.InRange(report.Sweeps, 1, 2)
            Assert.Equal(2, report.Blocks)
            Assert.Equal(0, report.FixedVariables)
            Assert.InRange(report.Runs, 2, 4)
            Assert.Equal(narrow.WidestCircuit, report.WidestPieceQubits)
        | None -> Assert.Fail("a neighbourhood search reports its sweeps")
    }

[<Fact>]
let ``a neighbourhood search that cannot sweep within MaxShareRuns is refused with both reasons`` () : Task =
    task {
        let narrow = NarrowBackend 5

        let config =
            withSplitting
                { QaoaExecutionHelpers.defaultSplitSettings with
                    MaxShareRuns = 1
                    MaxSweeps = 1
                }

        match! QuboSplitting.runQaoaAsync narrow (denseQubo 9) config CancellationToken.None with
        | Error(QuantumError.ValidationError(field, message)) ->
            Assert.Equal("qubits", field)
            Assert.Contains("it was not split because", message)

            Assert.Contains(
                "a neighbourhood search samples 2 blocks per sweep, more than SplitSettings.MaxShareRuns = 1",
                message
            )
        | other -> Assert.Fail($"Expected a qubits validation error, got %A{other}")

        Assert.Equal(0, narrow.WidestCircuit)
    }
