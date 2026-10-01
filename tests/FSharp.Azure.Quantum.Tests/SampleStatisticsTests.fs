module FSharp.Azure.Quantum.Tests.SampleStatisticsTests

// SampleStatistics: the arithmetic, and that every QAOA solver reports the standing of the
// solution it returns among its final samples. Only relations that hold for every sampling
// outcome are asserted, so nothing here depends on the random draw.

open System.Threading
open System.Threading.Tasks
open Xunit
open FSharp.Azure.Quantum
open FSharp.Azure.Quantum.Core
open FSharp.Azure.Quantum.Core.QaoaExecutionHelpers
open FSharp.Azure.Quantum.Backends
open FSharp.Azure.Quantum.Classical
open FSharp.Azure.Quantum.GraphOptimization
open FSharp.Azure.Quantum.Quantum

let private backend () =
    LocalBackend.LocalBackend() :> BackendAbstraction.IQuantumBackend

let private ok (label: string) (result: Result<'T, QuantumError>) : 'T =
    result |> Result.defaultWith (fun err -> failwith $"{label}: {err}")

let private sampled (label: string) (shots: int) (qubits: int) (sampling: SampleStatistics option) =
    match sampling with
    | None -> failwith $"{label}: no sampling statistics"
    | Some stats ->
        Assert.Equal(shots, stats.Shots)
        Assert.Equal(qubits, stats.Qubits)
        Assert.InRange(stats.Valid, 0, stats.Shots)
        Assert.InRange(stats.Hits, 0, stats.Shots)
        stats

/// A solution picked from the valid samples was sampled at least once, as a valid sample.
let private pickedFromValid (label: string) (stats: SampleStatistics) =
    Assert.True(stats.Hits >= 1, $"{label}: the returned solution was never sampled")
    Assert.True(stats.Hits <= stats.Valid, $"{label}: {stats.Hits} hits among {stats.Valid} valid samples")

/// The lowest-energy sample is returned unless repair replaced it.
let private pickedUnlessRepaired (label: string) (wasRepaired: bool) (stats: SampleStatistics) =
    if not wasRepaired then
        Assert.True(stats.Hits >= 1, $"{label}: the returned solution was never sampled")

let private smallConfig =
    { fastConfig with
        NumLayers = 1
        FinalShots = 200
    }

[<Fact>]
let ``sampleStatistics counts hits and valid samples`` () =
    let samples = [| [| 1; 0 |]; [| 1; 0 |]; [| 0; 1 |]; [| 1; 1 |] |]

    let stats =
        sampleStatistics 2 (fun sample -> Array.sum sample = 1) (fun sample -> sample = [| 1; 0 |]) samples

    Assert.Equal(4, stats.Shots)
    Assert.Equal(2, stats.Qubits)
    Assert.Equal(2, stats.Hits)
    Assert.Equal(3, stats.Valid)
    Assert.Equal(0.5, stats.HitRate)
    Assert.Equal(0.75, stats.ValidRate)
    Assert.Equal(0.25, stats.UniformRate)

[<Fact>]
let ``sampleStatistics of no samples has zero rates`` () =
    let stats = sampleStatistics 3 (fun _ -> true) (fun _ -> true) [||]

    Assert.Equal(0.0, stats.HitRate)
    Assert.Equal(0.0, stats.ValidRate)
    Assert.Equal(ValueNone, stats.ShotsFor 0.95)

[<Fact>]
let ``ShotsFor solves 1 - (1 - p)^N for N`` () =
    let stats: SampleStatistics =
        {
            Shots = 100
            Qubits = 8
            Hits = 5
            Valid = 40
        }

    // ln 0.05 / ln 0.95 = 58.4 and ln 0.01 / ln 0.95 = 89.8
    Assert.Equal(ValueSome 59, stats.ShotsFor 0.95)
    Assert.Equal(ValueSome 90, stats.ShotsFor 0.99)
    Assert.Equal(ValueNone, { stats with Hits = 0 }.ShotsFor 0.95)
    Assert.Equal(ValueSome 1, { stats with Hits = 100 }.ShotsFor 0.95)
    Assert.Equal(ValueNone, stats.ShotsFor 1.0)
    Assert.Equal(ValueNone, stats.ShotsFor 0.0)

[<Fact>]
let ``runQaoaSampledAsync returns every final sample and the lowest-energy one`` () : Task =
    task {
        let qubo = array2D [| [| -1.0; 2.0 |]; [| 0.0; -1.0 |] |]

        for enableOptimization in [ true; false ] do
            let config =
                { smallConfig with
                    FinalShots = 60
                    EnableOptimization = enableOptimization
                }

            let! result = runQaoaSampledAsync (backend ()) qubo config CancellationToken.None
            let run = ok "runQaoaSampledAsync" result

            Assert.Equal(60, run.Samples.Length)
            Assert.Equal(1, run.Parameters.Length)
            Assert.Equal(enableOptimization, run.Converged.IsSome)
            Assert.Equal(enableOptimization, run.Iterations.IsSome)
            Assert.True(run.Samples |> Array.contains run.Best, "Best is one of the samples")

            let lowest = run.Samples |> Array.map (evaluateQubo qubo) |> Array.min
            Assert.Equal(lowest, evaluateQubo qubo run.Best)
    }

[<Fact>]
let ``runQaoaSampledAsync rejects an invalid configuration`` () : Task =
    task {
        let qubo = array2D [| [| -1.0 |] |]
        let config = { smallConfig with FinalShots = 0 }

        match! runQaoaSampledAsync (backend ()) qubo config CancellationToken.None with
        | Error(QuantumError.ValidationError(field, _)) -> Assert.Equal("FinalShots", field)
        | other -> Assert.Fail($"Expected a FinalShots validation error, got {other}")
    }

/// LocalBackend that replaces every circuit by X on each qubit: every sample is 1…1.
type private AllOnesBackend() =
    let inner = backend ()

    let allOnes (circuit: CircuitAbstraction.ICircuit) =
        CircuitBuilder.empty circuit.NumQubits
        |> CircuitBuilder.addGates (List.init circuit.NumQubits CircuitBuilder.X)
        |> CircuitAbstraction.wrapCircuit

    interface BackendAbstraction.IQuantumBackend with
        member _.Name = inner.Name
        member _.NativeStateType = inner.NativeStateType
        member _.SupportsOperation op = inner.SupportsOperation op
        member _.InitializeState n = inner.InitializeState n
        member _.ApplyOperation op state = inner.ApplyOperation op state
        member _.ExecuteToState circuit = inner.ExecuteToState(allOnes circuit)

        member _.ExecuteToStateAsync circuit ct =
            inner.ExecuteToStateAsync (allOnes circuit) ct

        member _.ApplyOperationAsync op state ct = inner.ApplyOperationAsync op state ct

[<Fact>]
let ``knapsack without a feasible sample returns the empty selection with no hit and no valid sample`` () : Task =
    task {
        let knapsack: QuantumKnapsackSolver.KnapsackProblem =
            {
                Items =
                    [
                        { Id = "a"; Weight = 2.0; Value = 3.0 }
                        { Id = "b"; Weight = 3.0; Value = 4.0 }
                        { Id = "c"; Weight = 4.0; Value = 5.0 }
                    ]
                Capacity = 5.0
            }

        let knapsackQubits =
            (QuantumKnapsackSolver.toQubo knapsack |> ok "Knapsack QUBO").NumVariables

        // Every sample selects all three items (weight 9 against a capacity of 5).
        let! result =
            QuantumKnapsackSolver.solveAsync
                (AllOnesBackend() :> BackendAbstraction.IQuantumBackend)
                knapsack
                { QuantumKnapsackSolver.defaultConfig with
                    NumShots = 50
                }
                CancellationToken.None

        let solution = ok "Knapsack" result
        Assert.Empty(solution.SelectedItems)
        Assert.True(solution.IsFeasible)
        Assert.Equal(0.0, solution.TotalValue)

        let stats = sampled "Knapsack" 50 knapsackQubits solution.Sampling
        Assert.Equal(0, stats.Valid)
        Assert.Equal(0, stats.Hits)
    }

[<Fact>]
let ``fixed-angle solvers report the standing of the returned solution`` () : Task =
    task {
        let maxCut: QuantumMaxCutSolver.MaxCutProblem =
            {
                Vertices = [ for i in 0..5 -> string i ]
                Edges =
                    [ for i in 0..5 -> edge (string i) (string ((i + 1) % 6)) 1.0 ]
                    @ [ edge "0" "3" 1.0 ]
            }

        let! result =
            QuantumMaxCutSolver.solveAsync
                (backend ())
                maxCut
                { QuantumMaxCutSolver.defaultConfig with
                    NumShots = 200
                }
                CancellationToken.None

        let stats = sampled "MaxCut" 200 6 (ok "MaxCut" result).Sampling
        Assert.Equal(200, stats.Valid)
        pickedFromValid "MaxCut" stats

        let knapsack: QuantumKnapsackSolver.KnapsackProblem =
            {
                Items =
                    [
                        { Id = "a"; Weight = 2.0; Value = 3.0 }
                        { Id = "b"; Weight = 3.0; Value = 4.0 }
                        { Id = "c"; Weight = 4.0; Value = 5.0 }
                    ]
                Capacity = 5.0
            }

        let knapsackQubits =
            (QuantumKnapsackSolver.toQubo knapsack |> ok "Knapsack QUBO").NumVariables

        let! result =
            QuantumKnapsackSolver.solveAsync
                (backend ())
                knapsack
                { QuantumKnapsackSolver.defaultConfig with
                    NumShots = 200
                }
                CancellationToken.None

        let solution = ok "Knapsack" result
        let stats = sampled "Knapsack" 200 knapsackQubits solution.Sampling
        // No feasible sample at all returns the empty selection without a hit.
        if stats.Valid > 0 then
            pickedFromValid "Knapsack" stats

        let tsp = array2D [ [ 0.0; 2.0; 3.0 ]; [ 2.0; 0.0; 4.0 ]; [ 3.0; 4.0; 0.0 ] ]

        let! result =
            QuantumTspSolver.solveAsync
                (backend ())
                tsp
                { QuantumTspSolver.fastConfig with
                    FinalShots = 200
                }
                CancellationToken.None

        // A TSP sample is valid only when it is a permutation matrix; a run without one
        // returns an error that names the shots instead of a solution.
        match result with
        | Error err -> Assert.Contains("No valid tour in 200 shots", err.Message)
        | Ok solution ->
            let stats = sampled "TSP" 200 9 solution.Sampling
            pickedFromValid "TSP" stats
            // A symmetric 3-city instance has one cycle: every valid sample decodes to it.
            Assert.Equal(stats.Valid, stats.Hits)

            match solution.TopSolutions with
            | [ (_, _, frequency) ] -> Assert.Equal(frequency, stats.Hits)
            | other -> Assert.Fail($"TSP: expected one top solution, got {other}")

        let coloringEdge a b : Edge<unit> =
            {
                Source = a
                Target = b
                Weight = 1.0
                Directed = false
                Value = None
                Properties = Map.empty
            }

        let coloring: QuantumGraphColoringSolver.GraphColoringProblem =
            {
                Vertices = [ "a"; "b"; "c" ]
                Edges = [ coloringEdge "a" "b"; coloringEdge "b" "c" ]
                NumColors = 2
                FixedColors = Map.empty
            }

        let! result =
            QuantumGraphColoringSolver.solveAsync
                (backend ())
                coloring
                { QuantumGraphColoringSolver.defaultConfig 2 with
                    NumShots = 200
                }
                CancellationToken.None

        let solution = ok "GraphColoring" result
        let stats = sampled "GraphColoring" 200 6 solution.Sampling
        Assert.True(stats.Hits >= 1, "GraphColoring: the returned coloring was never sampled")

        if solution.IsValid then
            Assert.True(stats.Hits <= stats.Valid, "GraphColoring: a valid coloring counts as a valid sample")

        let directed s t w : Edge<float> = { edge s t w with Directed = true }

        let flow: QuantumNetworkFlowSolver.NetworkFlowProblem =
            {
                Sources = [ "S" ]
                Sinks = [ "T" ]
                IntermediateNodes = [ "A"; "B" ]
                Edges =
                    [
                        directed "S" "A" 1.0
                        directed "S" "B" 3.0
                        directed "A" "T" 1.0
                        directed "B" "T" 1.0
                    ]
                Capacities = Map [ "A", 1; "B", 1 ]
                Demands = Map [ "T", 1 ]
                Supplies = Map [ "S", 1 ]
            }

        let! result =
            QuantumNetworkFlowSolver.solveAsync
                (backend ())
                flow
                { QuantumNetworkFlowSolver.defaultConfig with
                    NumShots = 200
                }
                CancellationToken.None

        let stats = sampled "NetworkFlow" 200 4 (ok "NetworkFlow" result).Sampling
        pickedFromValid "NetworkFlow" stats
    }

[<Fact>]
let ``portfolio statistics cover the final run, not the angle grid`` () : Task =
    task {
        let assets: PortfolioTypes.Asset list =
            [
                {
                    Symbol = "X"
                    ExpectedReturn = 0.10
                    Risk = 0.15
                    Price = 100.0
                }
                {
                    Symbol = "Y"
                    ExpectedReturn = 0.12
                    Risk = 0.18
                    Price = 60.0
                }
                {
                    Symbol = "Z"
                    ExpectedReturn = 0.07
                    Risk = 0.09
                    Price = 50.0
                }
            ]

        let constraints: PortfolioSolver.Constraints =
            {
                Budget = 300.0
                MinHolding = 0.0
                MaxHolding = 300.0
            }

        let config: QuantumPortfolioSolver.QuantumPortfolioConfig =
            {
                NumShots = 120
                RiskAversion = 0.5
                InitialParameters = (0.5, 0.5)
            }

        let! result =
            QuantumPortfolioSolver.solveAsync (backend ()) assets constraints config CancellationToken.None

        let solution = ok "Portfolio" result
        let stats = sampled "Portfolio" 120 3 solution.Sampling

        // The best selection may have been seen only on the angle grid, so no lower bound on Hits.
        Assert.True(stats.Hits <= stats.Valid, $"Portfolio: {stats.Hits} hits among {stats.Valid} valid samples")
    }

[<Fact>]
let ``shared-configuration solvers report the standing of the returned solution`` () : Task =
    task {
        let cover: QuantumVertexCoverSolver.Problem =
            {
                Vertices = [ for i in 0..4 -> { Id = string i; Weight = 1.0 } ]
                Edges = [ (0, 1); (1, 2); (2, 3); (3, 4) ]
            }

        let! result =
            QuantumVertexCoverSolver.solveWithConfigAsync (backend ()) cover smallConfig CancellationToken.None

        let solution = ok "VertexCover" result
        let stats = sampled "VertexCover" 200 5 solution.Sampling
        pickedUnlessRepaired "VertexCover" solution.WasRepaired stats

        if solution.WasRepaired then
            Assert.True(stats.Valid < stats.Shots, "VertexCover: a repair means the lowest-energy sample was invalid")

        let subsets: QuantumSetCoverSolver.Problem =
            {
                UniverseSize = 3
                Subsets =
                    [
                        {
                            Id = "S1"
                            Elements = [ 0; 1 ]
                            Cost = 1.0
                        }
                        {
                            Id = "S2"
                            Elements = [ 1; 2 ]
                            Cost = 1.0
                        }
                        {
                            Id = "S3"
                            Elements = [ 0; 1; 2 ]
                            Cost = 3.0
                        }
                    ]
            }

        let setCoverQubits =
            Array2D.length1 (QuantumSetCoverSolver.toQubo subsets |> ok "SetCover QUBO")

        let! result =
            QuantumSetCoverSolver.solveWithConfigAsync (backend ()) subsets smallConfig CancellationToken.None

        let solution = ok "SetCover" result
        let stats = sampled "SetCover" 200 setCoverQubits solution.Sampling
        pickedUnlessRepaired "SetCover" solution.WasRepaired stats

        let packing: QuantumBinPackingSolver.Problem =
            {
                Items = [ { Id = "A"; Size = 2.0 }; { Id = "B"; Size = 2.0 } ]
                BinCapacity = 4.0
            }

        let! result =
            QuantumBinPackingSolver.solveWithConfigAsync (backend ()) packing smallConfig CancellationToken.None

        let solution = ok "BinPacking" result

        match solution.Sampling with
        | None -> Assert.Fail("BinPacking: no sampling statistics")
        | Some stats ->
            Assert.Equal(200, stats.Shots)
            pickedUnlessRepaired "BinPacking" solution.WasRepaired stats
    }

[<Fact>]
let ``a solution recombined from several components has no single sampling run`` () =
    let part: QuantumVertexCoverSolver.Solution =
        {
            CoverVertices = [ { Id = "0"; Weight = 1.0 } ]
            CoverWeight = 1.0
            CoverSize = 1
            IsValid = true
            WasRepaired = false
            BackendName = "test"
            NumShots = 10
            OptimizedParameters = None
            OptimizationConverged = None
            Split = None
            Sampling =
                Some
                    {
                        Shots = 10
                        Qubits = 2
                        Hits = 4
                        Valid = 9
                    }
        }

    Assert.Equal(part.Sampling, (QuantumVertexCoverSolver.recombine [ part ]).Sampling)
    Assert.Equal(None, (QuantumVertexCoverSolver.recombine [ part; part ]).Sampling)
