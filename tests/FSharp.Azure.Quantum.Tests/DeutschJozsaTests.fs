module DeutschJozsaTests

open Xunit
open FSharp.Azure.Quantum.Algorithms
open FSharp.Azure.Quantum.Core.BackendAbstraction
open FSharp.Azure.Quantum.Backends

let private createLocalBackend () : IQuantumBackend =
    LocalBackend.LocalBackend() :> IQuantumBackend

[<Fact>]
let ``Deutsch-Jozsa reports Constant for constant-zero oracle`` () =
    let backend = createLocalBackend ()

    match DeutschJozsa.runConstantZero 3 backend 200 with
    | Ok result ->
        Assert.Equal(DeutschJozsa.Constant, result.OracleType)
        Assert.Equal(1.0, result.ZeroProbability)
    | Error err ->
        Assert.True(false, $"Expected Ok, got Error: {err}")

[<Fact>]
let ``Deutsch-Jozsa reports Constant for constant-one oracle`` () =
    let backend = createLocalBackend ()

    match DeutschJozsa.runConstantOne 3 backend 200 with
    | Ok result ->
        Assert.Equal(DeutschJozsa.Constant, result.OracleType)
        Assert.Equal(1.0, result.ZeroProbability)
    | Error err ->
        Assert.True(false, $"Expected Ok, got Error: {err}")

[<Fact>]
let ``Deutsch-Jozsa reports Balanced for first-bit balanced oracle`` () =
    let backend = createLocalBackend ()

    match DeutschJozsa.runBalancedFirstBit 3 backend 200 with
    | Ok result ->
        Assert.Equal(DeutschJozsa.Balanced, result.OracleType)
        Assert.Equal(0.0, result.ZeroProbability)
    | Error err ->
        Assert.True(false, $"Expected Ok, got Error: {err}")

[<Fact>]
let ``Deutsch-Jozsa reports Balanced for parity balanced oracle`` () =
    let backend = createLocalBackend ()

    match DeutschJozsa.runBalancedParity 3 backend 200 with
    | Ok result ->
        Assert.Equal(DeutschJozsa.Balanced, result.OracleType)
        Assert.Equal(0.0, result.ZeroProbability)
    | Error err ->
        Assert.True(false, $"Expected Ok, got Error: {err}")
