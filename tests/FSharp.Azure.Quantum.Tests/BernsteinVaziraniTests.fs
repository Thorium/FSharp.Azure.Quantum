module BernsteinVaziraniTests

open Xunit
open FSharp.Azure.Quantum.Algorithms
open FSharp.Azure.Quantum.Core.BackendAbstraction
open FSharp.Azure.Quantum.Backends
open FSharp.Azure.Quantum.Tests

let private createLocalBackend () : IQuantumBackend =
    LocalBackend.LocalBackend() :> IQuantumBackend

[<Fact>]
let ``Bernstein-Vazirani recovers all-zero secret`` () =
    let backend = createLocalBackend ()

    match BernsteinVazirani.runWithSecret [| 0; 0; 0 |] backend 200 with
    | Ok result ->
        Assert.Equal<int[]>([| 0; 0; 0 |], result.RecoveredSecret)
        Assert.Equal(1.0, result.Confidence)
    | Error err -> Assert.True(false, $"Expected Ok, got Error: {err}")

[<Fact>]
let ``Bernstein-Vazirani recovers mixed secret in one query`` () =
    let backend = createLocalBackend ()

    match BernsteinVazirani.runWithSecret [| 1; 0; 1; 1 |] backend 200 with
    | Ok result ->
        Assert.Equal<int[]>([| 1; 0; 1; 1 |], result.RecoveredSecret)
        Assert.Equal(1.0, result.Confidence)
    | Error err -> Assert.True(false, $"Expected Ok, got Error: {err}")

[<Fact>]
let ``Bernstein-Vazirani recovers all-one secret`` () =
    let backend = createLocalBackend ()

    match BernsteinVazirani.runWithSecret [| 1; 1; 1 |] backend 200 with
    | Ok result ->
        Assert.Equal<int[]>([| 1; 1; 1 |], result.RecoveredSecret)
        Assert.Equal(1.0, result.Confidence)
    | Error err -> Assert.True(false, $"Expected Ok, got Error: {err}")

[<Fact>]
let ``Bernstein-Vazirani rejects non-bit secret`` () =
    let backend = createLocalBackend ()

    (BernsteinVazirani.runWithSecret [| 1; 2; 0 |] backend 10)
    |> Result.iter (fun _ -> Assert.True(false, "Expected validation error for non-bit secret"))

[<Fact>]
let ``Bernstein-Vazirani rejects zero shots`` () =
    let backend = createLocalBackend ()

    (BernsteinVazirani.runWithSecret [| 1; 0 |] backend 0)
    |> Result.iter (fun _ -> Assert.True(false, "Expected validation error for zero shots"))

// Width limits come from the backend (UnifiedBackend.getRunnableQubits). No test here
// simulates more than 16 qubits: wide secrets go to a backend that simulates nothing.

[<Fact>]
let ``Bernstein-Vazirani admits a 24-bit secret when the backend declares no limit`` () =
    let backend = CapacityStubBackend("non-simulating", None) :> IQuantumBackend

    let result = BernsteinVazirani.runWithSecret (Array.create 24 1) backend 10

    Assert.True(QubitLimitBackends.reachedBackend result, $"Expected the backend's own error, got {result}")

[<Fact>]
let ``Bernstein-Vazirani refuses a secret wider than the backend runs, naming the backend`` () =
    let limited = LimitedLocalBackend 4

    let result = BernsteinVazirani.runWithSecret [| 1; 0; 1; 1; 0; 1 |] (limited :> IQuantumBackend) 10

    let msg = QubitLimitBackends.validationMessage result
    Assert.Contains("limited-local-4", msg)
    Assert.Contains("at most 4 qubits", msg)
    Assert.Equal(0, limited.WidestRequested)
