module BellStatesTests

open Xunit
open FSharp.Azure.Quantum.Algorithms
open FSharp.Azure.Quantum.Core.BackendAbstraction
open FSharp.Azure.Quantum.Backends

let private createLocalBackend () : IQuantumBackend =
    LocalBackend.LocalBackend() :> IQuantumBackend

let private measureMany (state: FSharp.Azure.Quantum.Core.QuantumState) (backend: IQuantumBackend) shots =
    [| 1 .. shots |]
    |> Array.choose (fun _ ->
        match BellStates.measureBellBasis state backend with
        | Ok measurement -> Some measurement
        | Error _ -> None)

[<Fact>]
let ``BellStates.create PhiPlus measures to PhiPlus in Bell basis`` () =
    let backend = createLocalBackend ()

    match BellStates.create BellStates.PhiPlus backend with
    | Error err -> Assert.Fail($"Expected Ok, got Error: {err}")
    | Ok result ->
        let measurements = measureMany result.QuantumState backend 200
        Assert.True(measurements.Length = 200, "All measurements should succeed")
        Assert.All(measurements, fun m -> Assert.Equal(BellStates.PhiPlus, m.State))

[<Fact>]
let ``BellStates.create PhiMinus measures to PhiMinus in Bell basis`` () =
    let backend = createLocalBackend ()

    match BellStates.create BellStates.PhiMinus backend with
    | Error err -> Assert.Fail($"Expected Ok, got Error: {err}")
    | Ok result ->
        let measurements = measureMany result.QuantumState backend 200
        Assert.True(measurements.Length = 200, "All measurements should succeed")
        Assert.All(measurements, fun m -> Assert.Equal(BellStates.PhiMinus, m.State))

[<Fact>]
let ``BellStates.create PsiPlus measures to PsiPlus in Bell basis`` () =
    let backend = createLocalBackend ()

    match BellStates.create BellStates.PsiPlus backend with
    | Error err -> Assert.Fail($"Expected Ok, got Error: {err}")
    | Ok result ->
        let measurements = measureMany result.QuantumState backend 200
        Assert.True(measurements.Length = 200, "All measurements should succeed")
        Assert.All(measurements, fun m -> Assert.Equal(BellStates.PsiPlus, m.State))

[<Fact>]
let ``BellStates.create PsiMinus measures to PsiMinus in Bell basis`` () =
    let backend = createLocalBackend ()

    match BellStates.create BellStates.PsiMinus backend with
    | Error err -> Assert.Fail($"Expected Ok, got Error: {err}")
    | Ok result ->
        let measurements = measureMany result.QuantumState backend 200
        Assert.True(measurements.Length = 200, "All measurements should succeed")
        Assert.All(measurements, fun m -> Assert.Equal(BellStates.PsiMinus, m.State))

[<Fact>]
let ``BellStates.verifyEntanglement returns near ±1 correlation`` () =
    let backend = createLocalBackend ()

    let correlationFor bellState expectedSign =
        match BellStates.create bellState backend with
        | Error err -> Assert.Fail($"Expected Ok, got Error: {err}")
        | Ok result ->
            match BellStates.verifyEntanglement result backend 500 with
            | Error err -> Assert.Fail($"Expected Ok, got Error: {err}")
            | Ok corr ->
                // Ideal correlations:
                // Phi states -> +1 (same bits)
                // Psi states -> -1 (opposite bits)
                Assert.True(abs corr > 0.9, $"Expected strong correlation, got {corr}")
                Assert.True(sign corr = expectedSign, $"Expected sign {expectedSign}, got {corr}")

    correlationFor BellStates.PhiPlus 1
    correlationFor BellStates.PhiMinus 1
    correlationFor BellStates.PsiPlus -1
    correlationFor BellStates.PsiMinus -1
