module QuantumTeleportationTests

open Xunit
open FSharp.Azure.Quantum.Algorithms
open FSharp.Azure.Quantum.Core.BackendAbstraction
open FSharp.Azure.Quantum.Backends

let private createLocalBackend () : IQuantumBackend =
    LocalBackend.LocalBackend() :> IQuantumBackend

let private assertHighFidelity (result: QuantumTeleportation.TeleportationResult) (minFidelity: float) =
    Assert.True(
        result.Fidelity >= minFidelity,
        $"Expected fidelity >= {minFidelity}, got {result.Fidelity} (bits={result.AliceMeasurement.Bit0}{result.AliceMeasurement.Bit1})"
    )

[<Fact>]
let ``QuantumTeleportation.teleportZero returns near-perfect fidelity`` () =
    let backend = createLocalBackend ()

    match QuantumTeleportation.teleportZero backend with
    | Error err -> Assert.Fail($"Expected Ok, got Error: {err}")
    | Ok result -> assertHighFidelity result 0.999999

[<Fact>]
let ``QuantumTeleportation.teleportOne returns near-perfect fidelity`` () =
    let backend = createLocalBackend ()

    match QuantumTeleportation.teleportOne backend with
    | Error err -> Assert.Fail($"Expected Ok, got Error: {err}")
    | Ok result -> assertHighFidelity result 0.999999

[<Fact>]
let ``QuantumTeleportation.teleportPlus returns near-perfect fidelity`` () =
    let backend = createLocalBackend ()

    match QuantumTeleportation.teleportPlus backend with
    | Error err -> Assert.Fail($"Expected Ok, got Error: {err}")
    | Ok result -> assertHighFidelity result 0.999999

[<Fact>]
let ``QuantumTeleportation.teleportMinus returns near-perfect fidelity`` () =
    let backend = createLocalBackend ()

    match QuantumTeleportation.teleportMinus backend with
    | Error err -> Assert.Fail($"Expected Ok, got Error: {err}")
    | Ok result -> assertHighFidelity result 0.999999

[<Fact>]
let ``QuantumTeleportation.runStatistics returns all successful results`` () =
    let backend = createLocalBackend ()

    let prepareInput (b: IQuantumBackend) =
        b.InitializeState 3
        |> Result.bind (fun s -> b.ApplyOperation (QuantumOperation.Gate (FSharp.Azure.Quantum.CircuitBuilder.H 0)) s)

    match QuantumTeleportation.runStatistics prepareInput backend 25 with
    | Error err -> Assert.Fail($"Expected Ok, got Error: {err}")
    | Ok results ->
        Assert.Equal(25, results.Length)
        results |> List.iter (fun r -> assertHighFidelity r 0.999999)
