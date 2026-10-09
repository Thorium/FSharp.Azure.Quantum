module DeutschJozsaTests

open Xunit
open FSharp.Azure.Quantum.Algorithms
open FSharp.Azure.Quantum.Core.BackendAbstraction
open FSharp.Azure.Quantum.Backends
open FSharp.Azure.Quantum.Tests

let private createLocalBackend () : IQuantumBackend =
    LocalBackend.LocalBackend() :> IQuantumBackend

[<Fact>]
let ``Deutsch-Jozsa reports Constant for constant-zero oracle`` () =
    let backend = createLocalBackend ()

    match DeutschJozsa.runConstantZero 3 backend 200 with
    | Ok result ->
        Assert.Equal(DeutschJozsa.Constant, result.OracleType)
        Assert.Equal(1.0, result.ZeroProbability)
    | Error err -> Assert.True(false, $"Expected Ok, got Error: {err}")

[<Fact>]
let ``Deutsch-Jozsa reports Constant for constant-one oracle`` () =
    let backend = createLocalBackend ()

    match DeutschJozsa.runConstantOne 3 backend 200 with
    | Ok result ->
        Assert.Equal(DeutschJozsa.Constant, result.OracleType)
        Assert.Equal(1.0, result.ZeroProbability)
    | Error err -> Assert.True(false, $"Expected Ok, got Error: {err}")

[<Fact>]
let ``Deutsch-Jozsa reports Balanced for first-bit balanced oracle`` () =
    let backend = createLocalBackend ()

    match DeutschJozsa.runBalancedFirstBit 3 backend 200 with
    | Ok result ->
        Assert.Equal(DeutschJozsa.Balanced, result.OracleType)
        Assert.Equal(0.0, result.ZeroProbability)
    | Error err -> Assert.True(false, $"Expected Ok, got Error: {err}")

[<Fact>]
let ``Deutsch-Jozsa reports Balanced for parity balanced oracle`` () =
    let backend = createLocalBackend ()

    match DeutschJozsa.runBalancedParity 3 backend 200 with
    | Ok result ->
        Assert.Equal(DeutschJozsa.Balanced, result.OracleType)
        Assert.Equal(0.0, result.ZeroProbability)
    | Error err -> Assert.True(false, $"Expected Ok, got Error: {err}")

// Width limits come from the backend (UnifiedBackend.getRunnableQubits). No test here
// simulates more than 16 qubits: wide inputs go to a backend that simulates nothing.

[<Fact>]
let ``Deutsch-Jozsa admits 24 input bits when the backend declares no limit`` () =
    let backend = CapacityStubBackend("non-simulating", None) :> IQuantumBackend

    let result = DeutschJozsa.run Ok 24 backend 10

    Assert.True(QubitLimitBackends.reachedBackend result, $"Expected the backend's own error, got {result}")

[<Fact>]
let ``Deutsch-Jozsa refuses more input bits than the backend runs, naming the backend`` () =
    let limited = LimitedLocalBackend 4

    let result = DeutschJozsa.run Ok 6 (limited :> IQuantumBackend) 10

    let msg = QubitLimitBackends.validationMessage result
    Assert.Contains("limited-local-4", msg)
    Assert.Contains("at most 4 qubits", msg)
    Assert.Equal(0, limited.WidestRequested)
