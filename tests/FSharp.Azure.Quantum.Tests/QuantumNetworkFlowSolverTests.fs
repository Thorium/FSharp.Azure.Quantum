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
