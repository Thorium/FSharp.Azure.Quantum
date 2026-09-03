module QuantumTeleportationTests

open Xunit
open FSharp.Azure.Quantum.Algorithms
open FSharp.Azure.Quantum.Core.BackendAbstraction
open FSharp.Azure.Quantum.Backends

let private createLocalBackend () : IQuantumBackend =
    LocalBackend.LocalBackend() :> IQuantumBackend

let private assertHighFidelity (minFidelity: float) (result: QuantumTeleportation.TeleportationResult) =
    Assert.True(
        result.Fidelity >= minFidelity,
        $"Expected fidelity >= {minFidelity}, got {result.Fidelity} (bits={result.AliceMeasurement.Bit0}{result.AliceMeasurement.Bit1})"
    )

[<Fact>]
let ``QuantumTeleportation.teleportZero returns near-perfect fidelity`` () =
    let backend = createLocalBackend ()

    (QuantumTeleportation.teleportZero backend) |> Result.map (fun result -> assertHighFidelity 0.999999 result) |> Result.defaultWith (fun err -> Assert.Fail($"Expected Ok, got Error: {err}"))

[<Fact>]
let ``QuantumTeleportation.teleportOne returns near-perfect fidelity`` () =
    let backend = createLocalBackend ()

    (QuantumTeleportation.teleportOne backend) |> Result.map (fun result -> assertHighFidelity 0.999999 result) |> Result.defaultWith (fun err -> Assert.Fail($"Expected Ok, got Error: {err}"))

[<Fact>]
let ``QuantumTeleportation.teleportPlus returns near-perfect fidelity`` () =
    let backend = createLocalBackend ()

    (QuantumTeleportation.teleportPlus backend) |> Result.map (fun result -> assertHighFidelity 0.999999 result) |> Result.defaultWith (fun err -> Assert.Fail($"Expected Ok, got Error: {err}"))

[<Fact>]
let ``QuantumTeleportation.teleportMinus returns near-perfect fidelity`` () =
    let backend = createLocalBackend ()

    (QuantumTeleportation.teleportMinus backend) |> Result.map (fun result -> assertHighFidelity 0.999999 result) |> Result.defaultWith (fun err -> Assert.Fail($"Expected Ok, got Error: {err}"))

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
        results |> List.iter (assertHighFidelity 0.999999)
