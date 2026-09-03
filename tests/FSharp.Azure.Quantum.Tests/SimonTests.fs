module SimonTests

open Xunit
open FSharp.Azure.Quantum.Algorithms
open FSharp.Azure.Quantum.Core.BackendAbstraction
open FSharp.Azure.Quantum.Backends

let private createLocalBackend () : IQuantumBackend =
    LocalBackend.LocalBackend() :> IQuantumBackend

[<Fact>]
let ``Simon recovers two-to-one secret`` () =
    let backend = createLocalBackend ()

    match Simon.runWithSecret [| 1; 1; 0 |] backend 200 with
    | Ok result ->
        Assert.Equal<int[]>([| 1; 1; 0 |], result.RecoveredSecret)
        Assert.False(result.IsOneToOne)
    | Error err ->
        Assert.True(false, $"Expected Ok, got Error: {err}")

[<Fact>]
let ``Simon recovers secret with single set bit`` () =
    let backend = createLocalBackend ()

    match Simon.runWithSecret [| 0; 1; 0 |] backend 200 with
    | Ok result ->
        Assert.Equal<int[]>([| 0; 1; 0 |], result.RecoveredSecret)
        Assert.False(result.IsOneToOne)
    | Error err ->
        Assert.True(false, $"Expected Ok, got Error: {err}")

[<Fact>]
let ``Simon recovers all-one secret`` () =
    let backend = createLocalBackend ()

    match Simon.runWithSecret [| 1; 1; 1 |] backend 200 with
    | Ok result ->
        Assert.Equal<int[]>([| 1; 1; 1 |], result.RecoveredSecret)
        Assert.False(result.IsOneToOne)
    | Error err ->
        Assert.True(false, $"Expected Ok, got Error: {err}")

[<Fact>]
let ``Simon reports one-to-one for zero secret`` () =
    let backend = createLocalBackend ()

    match Simon.runWithSecret [| 0; 0; 0 |] backend 200 with
    | Ok result ->
        Assert.True(result.IsOneToOne)
        Assert.Equal<int[]>([| 0; 0; 0 |], result.RecoveredSecret)
    | Error err ->
        Assert.True(false, $"Expected Ok, got Error: {err}")

[<Fact>]
let ``Simon measurement vectors are orthogonal to secret`` () =
    let backend = createLocalBackend ()
    let secret = [| 1; 0; 1 |]

    match Simon.runWithSecret secret backend 200 with
    | Ok result ->
        for equation in result.Equations do
            let parity =
                Array.map2 (*) equation secret
                |> Array.sum
                |> fun s -> s % 2
            Assert.Equal(0, parity)
    | Error err ->
        Assert.True(false, $"Expected Ok, got Error: {err}")

[<Fact>]
let ``Simon rejects too many input qubits`` () =
    let backend = createLocalBackend ()

    (Simon.runWithSecret (Array.create 11 1) backend 10) |> Result.iter (fun _ -> Assert.True(false, "Expected validation error for >10 input qubits"))

[<Fact>]
let ``Simon rejects non-bit secret`` () =
    let backend = createLocalBackend ()

    (Simon.runWithSecret [| 1; 3 |] backend 10) |> Result.iter (fun _ -> Assert.True(false, "Expected validation error for non-bit secret"))
