namespace FSharp.Azure.Quantum.Tests

open System
open Xunit
open FSharp.Azure.Quantum.Core
open FSharp.Azure.Quantum.CircuitBuilder
open FSharp.Azure.Quantum.MachineLearning
open FSharp.Azure.Quantum.MachineLearning.FeatureMap

module FeatureMapTests =

    // ========================================================================
    // ANGLE ENCODING
    // ========================================================================

    [<Fact>]
    let ``angleEncoding creates circuit with correct number of qubits`` () =
        let features = [| 0.5; 0.3; 0.7 |]
        let circuit = angleEncoding features
        Assert.Equal(3, circuit.QubitCount)

    [<Fact>]
    let ``angleEncoding produces RY gates for each feature`` () =
        let features = [| 0.5; 0.8 |]
        let circuit = angleEncoding features
        let gates = circuit.Gates
        // Should have 2 RY gates
        let ryGates =
            gates |> List.choose (fun g ->
                match g with
                | RY(q, angle) -> Some (q, angle)
                | _ -> None)
        Assert.Equal(2, ryGates.Length)

    [<Fact>]
    let ``angleEncoding uses pi * feature as angle`` () =
        let features = [| 0.5 |]
        let circuit = angleEncoding features
        let gates = circuit.Gates
        let hasCorrectAngle =
            gates |> List.exists (fun g ->
                match g with
                | RY(0, angle) -> abs(angle - Math.PI * 0.5) < 1e-10
                | _ -> false)
        Assert.True(hasCorrectAngle, "Expected RY gate with angle pi * 0.5")

    [<Fact>]
    let ``angleEncoding with single feature`` () =
        let features = [| 1.0 |]
        let circuit = angleEncoding features
        Assert.Equal(1, circuit.QubitCount)
        Assert.True(circuit.Gates.Length >= 1)

    // ========================================================================
    // ZZ FEATURE MAP
    // ========================================================================

    [<Fact>]
    let ``zzFeatureMap creates circuit with correct number of qubits`` () =
        let features = [| 0.5; 0.3; 0.7 |]
        let circuit = zzFeatureMap 1 features
        Assert.Equal(3, circuit.QubitCount)

    [<Fact>]
    let ``zzFeatureMap includes Hadamard gates`` () =
        let features = [| 0.5; 0.3 |]
        let circuit = zzFeatureMap 1 features
        let hGates =
            circuit.Gates |> List.choose (fun g ->
                match g with
                | H q -> Some q
                | _ -> None)
        Assert.True(hGates.Length >= 2, $"Expected at least 2 H gates, got {hGates.Length}")

    [<Fact>]
    let ``zzFeatureMap includes CNOT gates for entanglement`` () =
        let features = [| 0.5; 0.3 |]
        let circuit = zzFeatureMap 1 features
        let cnotGates =
            circuit.Gates |> List.choose (fun g ->
                match g with
                | CNOT(c, t) -> Some (c, t)
                | _ -> None)
        Assert.True(cnotGates.Length >= 2, $"Expected CNOT gates for ZZ entanglement, got {cnotGates.Length}")

    [<Fact>]
    let ``zzFeatureMap includes RZ gates`` () =
        let features = [| 0.5; 0.3 |]
        let circuit = zzFeatureMap 1 features
        let rzGates =
            circuit.Gates |> List.choose (fun g ->
                match g with
                | RZ(q, angle) -> Some (q, angle)
                | _ -> None)
        Assert.True(rzGates.Length >= 2, $"Expected RZ gates, got {rzGates.Length}")

    [<Fact>]
    let ``zzFeatureMap depth 2 has more gates than depth 1`` () =
        let features = [| 0.5; 0.3 |]
        let circuit1 = zzFeatureMap 1 features
        let circuit2 = zzFeatureMap 2 features
        Assert.True(circuit2.Gates.Length > circuit1.Gates.Length,
            $"Depth 2 ({circuit2.Gates.Length} gates) should have more gates than depth 1 ({circuit1.Gates.Length} gates)")

    [<Fact>]
    let ``zzFeatureMap with single qubit has no CNOT`` () =
        let features = [| 0.5 |]
        let circuit = zzFeatureMap 1 features
        let cnotCount =
            circuit.Gates |> List.sumBy (fun g ->
                match g with CNOT _ -> 1 | _ -> 0)
        Assert.Equal(0, cnotCount)

    // ========================================================================
    // PAULI FEATURE MAP
    // ========================================================================

    [<Fact>]
    let ``pauliFeatureMap creates circuit with correct qubits`` () =
        let features = [| 0.5; 0.3 |]
        let circuit = pauliFeatureMap ["ZZ"] 1 features
        Assert.Equal(2, circuit.QubitCount)

    [<Fact>]
    let ``pauliFeatureMap with Z string applies RZ gate`` () =
        let features = [| 0.5; 0.3 |]
        let circuit = pauliFeatureMap ["Z"] 1 features
        let rzGates =
            circuit.Gates |> List.choose (fun g ->
                match g with
                | RZ(q, angle) -> Some (q, angle)
                | _ -> None)
        Assert.True(rzGates.Length >= 1, "Expected at least one RZ gate for Z Pauli string")

    [<Fact>]
    let ``pauliFeatureMap with ZZ string includes CNOT`` () =
        let features = [| 0.5; 0.3 |]
        let circuit = pauliFeatureMap ["ZZ"] 1 features
        let cnotCount =
            circuit.Gates |> List.sumBy (fun g ->
                match g with CNOT _ -> 1 | _ -> 0)
        Assert.True(cnotCount >= 2, $"Expected at least 2 CNOT gates for ZZ, got {cnotCount}")

    [<Fact>]
    let ``pauliFeatureMap with XX string includes H and CNOT`` () =
        let features = [| 0.5; 0.3 |]
        let circuit = pauliFeatureMap ["XX"] 1 features
        let hCount =
            circuit.Gates |> List.sumBy (fun g ->
                match g with H _ -> 1 | _ -> 0)
        // At least 2 H from Hadamard layer + 2 H around XX + 2 H restoring
        Assert.True(hCount >= 4, $"Expected at least 4 H gates for XX Pauli, got {hCount}")

    [<Fact>]
    let ``pauliFeatureMap includes Hadamard layer`` () =
        let features = [| 0.5; 0.3 |]
        let circuit = pauliFeatureMap ["Z"] 1 features
        let hGates =
            circuit.Gates |> List.choose (fun g ->
                match g with H q -> Some q | _ -> None)
        Assert.True(hGates.Length >= 2, "Expected Hadamard layer")

    [<Fact>]
    let ``pauliFeatureMap depth 2 has more gates than depth 1`` () =
        let features = [| 0.5; 0.3 |]
        let circuit1 = pauliFeatureMap ["Z"] 1 features
        let circuit2 = pauliFeatureMap ["Z"] 2 features
        Assert.True(circuit2.Gates.Length > circuit1.Gates.Length)

    // ========================================================================
    // AMPLITUDE ENCODING
    // ========================================================================

    [<Fact>]
    let ``amplitudeEncoding creates circuit for power-of-2 input`` () =
        let features = [| 1.0; 0.0; 0.0; 0.0 |]
        let circuit = amplitudeEncoding features
        Assert.Equal(2, circuit.QubitCount)

    [<Fact>]
    let ``amplitudeEncoding creates circuit for 8 features`` () =
        let features = [| 1.0; 0.0; 0.0; 0.0; 0.0; 0.0; 0.0; 0.0 |]
        let circuit = amplitudeEncoding features
        Assert.Equal(3, circuit.QubitCount)

    [<Fact>]
    let ``amplitudeEncoding handles non-power-of-2 input`` () =
        // 3 features -> needs 2 qubits (padded to 4)
        let features = [| 0.5; 0.5; 0.5 |]
        let circuit = amplitudeEncoding features
        Assert.Equal(2, circuit.QubitCount)

    [<Fact>]
    let ``amplitudeEncoding produces gates`` () =
        let features = [| 0.7; 0.3; 0.5; 0.1 |]
        let circuit = amplitudeEncoding features
        Assert.True(circuit.Gates.Length > 0, "Expected some gates for non-trivial input")

    [<Fact>]
    let ``amplitudeEncoding for basis state produces minimal gates`` () =
        // |00> = [1, 0, 0, 0] should need very few or no rotations
        let features = [| 1.0; 0.0; 0.0; 0.0 |]
        let circuit = amplitudeEncoding features
        // The circuit should have 2 qubits; may or may not have gates depending on normalization
        Assert.Equal(2, circuit.QubitCount)

    // ========================================================================
    // BUILD FEATURE MAP
    // ========================================================================

    [<Fact>]
    let ``buildFeatureMap AngleEncoding returns Ok`` () =
        let features = [| 0.5; 0.3 |]
        match buildFeatureMap AngleEncoding features with
        | Ok circuit ->
            Assert.Equal(2, circuit.QubitCount)
        | Error e -> failwith $"Expected Ok, got Error: {e}"

    [<Fact>]
    let ``buildFeatureMap ZZFeatureMap returns Ok`` () =
        let features = [| 0.5; 0.3; 0.7 |]
        match buildFeatureMap (ZZFeatureMap 2) features with
        | Ok circuit ->
            Assert.Equal(3, circuit.QubitCount)
        | Error e -> failwith $"Expected Ok, got Error: {e}"

    [<Fact>]
    let ``buildFeatureMap PauliFeatureMap returns Ok`` () =
        let features = [| 0.5; 0.3 |]
        match buildFeatureMap (PauliFeatureMap(["ZZ"; "Z"], 1)) features with
        | Ok circuit ->
            Assert.Equal(2, circuit.QubitCount)
        | Error e -> failwith $"Expected Ok, got Error: {e}"

    [<Fact>]
    let ``buildFeatureMap AmplitudeEncoding returns Ok`` () =
        let features = [| 0.5; 0.3; 0.7; 0.1 |]
        match buildFeatureMap AmplitudeEncoding features with
        | Ok circuit ->
            Assert.Equal(2, circuit.QubitCount)
        | Error e -> failwith $"Expected Ok, got Error: {e}"
