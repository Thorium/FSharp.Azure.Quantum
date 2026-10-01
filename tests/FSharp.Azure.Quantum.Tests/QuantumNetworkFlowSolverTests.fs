namespace FSharp.Azure.Quantum.Tests

open Xunit
open FSharp.Azure.Quantum.Core.BackendAbstraction
open FSharp.Azure.Quantum.Backends
open FSharp.Azure.Quantum.GraphOptimization
open FSharp.Azure.Quantum.Quantum
open System.Threading
open System.Threading.Tasks

module QuantumNetworkFlowSolverTests =

    let private route source target cost : Edge<float> =
        {
            Source = source
            Target = target
            Weight = cost
            Directed = true
            Value = None
            Properties = Map.empty
        }

    // One supplier, two customers. Serving either customer alone is a valid
    // flow and costs less than serving both.
    let private twoCustomers: QuantumNetworkFlowSolver.NetworkFlowProblem =
        {
            Sources = [ "S" ]
            Sinks = [ "C1"; "C2" ]
            IntermediateNodes = []
            Edges = [ route "S" "C1" 1.0; route "S" "C2" 2.0 ]
            Capacities = Map.empty
            Demands = Map.ofList [ "C1", 1; "C2", 1 ]
            Supplies = Map.ofList [ "S", 2 ]
        }

    [<Fact>]
    let ``solve returns the flow meeting the most demand, not the cheapest partial one`` () : Task =
        task {
            let backend = LocalBackend.LocalBackend() :> IQuantumBackend

            match! QuantumNetworkFlowSolver.solveWithShotsAsync backend twoCustomers 1000 CancellationToken.None with
            | Ok solution ->
                Assert.Equal(2.0, solution.DemandSatisfied)
                Assert.Equal(1.0, solution.FillRate)
                Assert.Equal(3.0, solution.TotalCost)
            | Error err -> Assert.Fail($"Network flow failed: {err}")
        }
        :> Task

    // Two suppliers, two warehouses, two customers; eight routes. The cheapest last hop
    // into C2 goes through W2, whose feeds are the expensive ones.
    let private eightRoutes: QuantumNetworkFlowSolver.NetworkFlowProblem =
        {
            Sources = [ "S1"; "S2" ]
            Sinks = [ "C1"; "C2" ]
            IntermediateNodes = [ "W1"; "W2" ]
            Edges =
                [
                    route "S1" "W1" 4.0
                    route "S1" "W2" 16.0
                    route "S2" "W1" 5.0
                    route "S2" "W2" 14.0
                    route "W1" "C1" 6.0
                    route "W1" "C2" 8.0
                    route "W2" "C1" 10.0
                    route "W2" "C2" 7.0
                ]
            Capacities = Map.ofList [ "W1", 200; "W2", 200 ]
            Demands = Map.ofList [ "C1", 1; "C2", 1 ]
            Supplies = Map.ofList [ "S1", 100; "S2", 100 ]
        }

    // S1→W1, S2→W1, W1→C1, W1→C2: both customers through W1, cost 23
    let private optimalRoutes = [| 1; 0; 1; 0; 1; 1; 0; 0 |]

    let private quboOf problem =
        match QuantumNetworkFlowSolver.toQubo problem with
        | Ok matrix -> FSharp.Azure.Quantum.Core.QaoaExecutionHelpers.quboMapToArray matrix
        | Error err -> failwith $"toQubo failed: {err}"

    [<Fact>]
    let ``toQubo ground state is the cheapest flow that serves every customer`` () =
        let qubo = quboOf eightRoutes
        let energy = FSharp.Azure.Quantum.Core.QaoaExecutionHelpers.evaluateQubo qubo

        let ground =
            List.init 256 (fun v -> Array.init 8 (fun i -> (v >>> i) &&& 1))
            |> List.minBy energy

        Assert.Equal<int[]>(optimalRoutes, ground)

    [<Fact>]
    let ``toQubo scores a customer served twice above one served once`` () =
        let energy =
            FSharp.Azure.Quantum.Core.QaoaExecutionHelpers.evaluateQubo (quboOf eightRoutes)

        // Every route open: flow is conserved at both warehouses, each customer gets two units
        let everyRoute = Array.create 8 1
        Assert.True(energy everyRoute > energy optimalRoutes, "over-delivery must cost more than the optimum")

    [<Fact>]
    let ``solveWithShotsAsync finds the optimum of an eight-route problem in 300 shots`` () : Task =
        task {
            let backend = LocalBackend.LocalBackend() :> IQuantumBackend

            match! QuantumNetworkFlowSolver.solveWithShotsAsync backend eightRoutes 300 CancellationToken.None with
            | Ok solution ->
                Assert.Equal(2.0, solution.DemandSatisfied)
                Assert.Equal(23.0, solution.TotalCost)
                Assert.Equal(300, solution.NumShots)
            | Error err -> Assert.Fail($"Network flow failed: {err}")
        }
        :> Task

    [<Fact>]
    let ``solveAsync runs one circuit at the configured angles`` () : Task =
        task {
            let backend = LocalBackend.LocalBackend() :> IQuantumBackend

            let config =
                { QuantumNetworkFlowSolver.defaultConfig with
                    NumShots = 2000
                }

            match! QuantumNetworkFlowSolver.solveAsync backend eightRoutes config CancellationToken.None with
            | Ok solution ->
                Assert.Equal(2.0, solution.DemandSatisfied)
                Assert.True(solution.TotalCost >= 23.0, $"cost {solution.TotalCost} is below the optimum")
            | Error err -> Assert.Fail($"Network flow failed: {err}")
        }
        :> Task

    [<Fact>]
    let ``solveWithQaoaConfigAsync accepts a grid-search configuration`` () : Task =
        task {
            let backend = LocalBackend.LocalBackend() :> IQuantumBackend

            let config =
                { FSharp.Azure.Quantum.Core.QaoaExecutionHelpers.fastConfig with
                    NumLayers = 1
                    EnableOptimization = false
                    FinalShots = 1000
                }

            match!
                QuantumNetworkFlowSolver.solveWithQaoaConfigAsync backend eightRoutes config CancellationToken.None
            with
            | Ok solution ->
                Assert.Equal(2.0, solution.DemandSatisfied)
                Assert.Equal(1000, solution.NumShots)
            | Error err -> Assert.Fail($"Network flow failed: {err}")
        }
        :> Task
