module BernsteinVaziraniTests

open Xunit
open FSharp.Azure.Quantum.Algorithms
open FSharp.Azure.Quantum.Core.BackendAbstraction
open FSharp.Azure.Quantum.Backends

let private createLocalBackend () : IQuantumBackend =
    LocalBackend.LocalBackend() :> IQuantumBackend

[<Fact>]
let ``Bernstein-Vazirani recovers all-zero secret`` () =
    let backend = createLocalBackend ()

    match BernsteinVazirani.runWithSecret [| 0; 0; 0 |] backend 200 with
    | Ok result ->
        Assert.Equal<int[]>([| 0; 0; 0 |], result.RecoveredSecret)
        Assert.Equal(1.0, result.Confidence)
    | Error err ->
        Assert.True(false, $"Expected Ok, got Error: {err}")

[<Fact>]
let ``Bernstein-Vazirani recovers mixed secret in one query`` () =
    let backend = createLocalBackend ()

    match BernsteinVazirani.runWithSecret [| 1; 0; 1; 1 |] backend 200 with
    | Ok result ->
        Assert.Equal<int[]>([| 1; 0; 1; 1 |], result.RecoveredSecret)
        Assert.Equal(1.0, result.Confidence)
    | Error err ->
        Assert.True(false, $"Expected Ok, got Error: {err}")

[<Fact>]
let ``Bernstein-Vazirani recovers all-one secret`` () =
    let backend = createLocalBackend ()

    match BernsteinVazirani.runWithSecret [| 1; 1; 1 |] backend 200 with
    | Ok result ->
        Assert.Equal<int[]>([| 1; 1; 1 |], result.RecoveredSecret)
        Assert.Equal(1.0, result.Confidence)
    | Error err ->
        Assert.True(false, $"Expected Ok, got Error: {err}")

[<Fact>]
let ``Bernstein-Vazirani rejects non-bit secret`` () =
    let backend = createLocalBackend ()

    match BernsteinVazirani.runWithSecret [| 1; 2; 0 |] backend 10 with
    | Ok _ -> Assert.True(false, "Expected validation error for non-bit secret")
    | Error _ -> ()

[<Fact>]
let ``Bernstein-Vazirani rejects zero shots`` () =
    let backend = createLocalBackend ()

    match BernsteinVazirani.runWithSecret [| 1; 0 |] backend 0 with
    | Ok _ -> Assert.True(false, "Expected validation error for zero shots")
    | Error _ -> ()
