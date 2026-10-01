module FSharp.Azure.Quantum.Tests.KnapsackExactCombinationsTests

// Knapsack.findAll…Async: classical enumeration only when no backend is given; with a
// backend the answer is the quantum result or its error.

open System.Threading
open System.Threading.Tasks
open Xunit
open FSharp.Azure.Quantum
open FSharp.Azure.Quantum.Core
open FSharp.Azure.Quantum.Backends
open FSharp.Azure.Quantum.Quantum

/// LocalBackend whose circuit execution always fails.
type private FailingBackend() =
    let inner = LocalBackend.LocalBackend() :> BackendAbstraction.IQuantumBackend

    let failure =
        Error(QuantumError.OperationError("FailingBackend", "no circuit runs here"))

    interface BackendAbstraction.IQuantumBackend with
        member _.Name = "Failing"
        member _.NativeStateType = inner.NativeStateType
        member _.SupportsOperation op = inner.SupportsOperation op
        member _.InitializeState n = inner.InitializeState n
        member _.ApplyOperation op state = inner.ApplyOperation op state
        member _.ExecuteToState _ = failure
        member _.ExecuteToStateAsync _ _ = Task.FromResult failure
        member _.ApplyOperationAsync op state ct = inner.ApplyOperationAsync op state ct

// weights 2, 5, 3, 4 with capacity 7: the exact combinations are {A, B} and {C, D}
let private problem =
    Knapsack.createProblem [ ("A", 2.0, 2.0); ("B", 5.0, 5.0); ("C", 3.0, 3.0); ("D", 4.0, 4.0) ] 7.0

let private ids (combinations: Knapsack.Item list list) =
    combinations
    |> List.map (List.map (fun item -> item.Id) >> List.sort)
    |> List.sort

[<Fact>]
let ``without a backend the combinations are enumerated classically`` () : Task =
    task {
        match! Knapsack.findAllExactCombinationsAsync problem None CancellationToken.None with
        | Ok combinations -> Assert.Equal<string list list>([ [ "A"; "B" ]; [ "C"; "D" ] ], ids combinations)
        | Error err -> Assert.Fail($"Unexpected error: {err}")

        match! Knapsack.findAllValidCombinationsAsync problem None CancellationToken.None with
        | Ok(combinations, union, count) ->
            Assert.Equal(2, count)
            Assert.Equal(2, combinations.Length)
            Assert.Equal(4, union.Length)
        | Error err -> Assert.Fail($"Unexpected error: {err}")
    }

[<Fact>]
let ``a backend error is returned, not replaced by classical enumeration`` () : Task =
    task {
        let backend = FailingBackend() :> BackendAbstraction.IQuantumBackend

        match!
            QuantumKnapsackSolver.findAllExactCombinationsAsync
                backend
                problem.Items
                problem.Capacity
                QuantumKnapsackSolver.defaultSubsetSumConfig
                CancellationToken.None
        with
        | Error(QuantumError.OperationError(operation, _)) -> Assert.Equal("FailingBackend", operation)
        | other -> Assert.Fail($"Expected the backend error from the solver, got %A{other}")

        match! Knapsack.findAllExactCombinationsAsync problem (Some backend) CancellationToken.None with
        | Error(QuantumError.OperationError(operation, _)) -> Assert.Equal("FailingBackend", operation)
        | other -> Assert.Fail($"Expected the backend error, got %A{other}")

        match! Knapsack.findAllCapturedItemsAsync problem (Some backend) CancellationToken.None with
        | Error _ -> ()
        | other -> Assert.Fail($"Expected the backend error, got %A{other}")

        match! Knapsack.findAllValidCombinationsAsync problem (Some backend) CancellationToken.None with
        | Error _ -> ()
        | other -> Assert.Fail($"Expected the backend error, got %A{other}")

        match! Knapsack.solveWithModeAsync problem (Some backend) true CancellationToken.None with
        | Error _ -> ()
        | other -> Assert.Fail($"Expected the backend error, got %A{other}")
    }

[<Fact>]
let ``with a backend every returned combination sums to the capacity`` () : Task =
    task {
        let backend = LocalBackend.LocalBackend() :> BackendAbstraction.IQuantumBackend

        match! Knapsack.findAllExactCombinationsAsync problem (Some backend) CancellationToken.None with
        | Ok combinations ->
            for combination in combinations do
                Assert.Equal(7.0, combination |> List.sumBy (fun item -> item.Weight))

            Assert.Equal(combinations.Length, (ids combinations |> List.distinct).Length)
        | Error err -> Assert.Fail($"Unexpected error: {err}")
    }
