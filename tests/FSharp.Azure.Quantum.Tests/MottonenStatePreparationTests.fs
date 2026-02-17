namespace FSharp.Azure.Quantum.Tests

open System
open System.Numerics
open Xunit
open FSharp.Azure.Quantum.Algorithms
open FSharp.Azure.Quantum

module MottonenStatePreparationTests =

    // ========================================================================
    // NORMALIZE STATE
    // ========================================================================

    [<Fact>]
    let ``normalizeState with valid 2-element vector produces normalized result`` () =
        let amplitudes = [| Complex(1.0, 0.0); Complex(1.0, 0.0) |]
        let result = MottonenStatePreparation.normalizeState amplitudes
        Assert.Equal(2, result.Amplitudes.Length)
        Assert.Equal(1, result.NumQubits)
        let norm = result.Amplitudes |> Array.sumBy (fun a -> a.Magnitude * a.Magnitude)
        Assert.True(abs(norm - 1.0) < 1e-10, $"Expected normalized state, got norm = {norm}")

    [<Fact>]
    let ``normalizeState with valid 4-element vector produces 2-qubit state`` () =
        let amplitudes = [| Complex(1.0, 0.0); Complex(0.0, 0.0); Complex(0.0, 0.0); Complex(1.0, 0.0) |]
        let result = MottonenStatePreparation.normalizeState amplitudes
        Assert.Equal(4, result.Amplitudes.Length)
        Assert.Equal(2, result.NumQubits)

    [<Fact>]
    let ``normalizeState with zero vector throws`` () =
        let amplitudes = [| Complex.Zero; Complex.Zero |]
        Assert.Throws<Exception>(fun () ->
            MottonenStatePreparation.normalizeState amplitudes |> ignore
        ) |> ignore

    [<Fact>]
    let ``normalizeState with non-power-of-2 length throws`` () =
        let amplitudes = [| Complex(1.0, 0.0); Complex(1.0, 0.0); Complex(1.0, 0.0) |]
        Assert.Throws<Exception>(fun () ->
            MottonenStatePreparation.normalizeState amplitudes |> ignore
        ) |> ignore

    [<Fact>]
    let ``normalizeState with single element produces 0-qubit state`` () =
        let amplitudes = [| Complex(3.0, 0.0) |]
        let result = MottonenStatePreparation.normalizeState amplitudes
        Assert.Equal(1, result.Amplitudes.Length)
        Assert.Equal(0, result.NumQubits)
        Assert.True(abs(result.Amplitudes.[0].Magnitude - 1.0) < 1e-10)

    [<Fact>]
    let ``normalizeState preserves relative phases`` () =
        let amplitudes = [| Complex(1.0, 0.0); Complex(0.0, 1.0) |]
        let result = MottonenStatePreparation.normalizeState amplitudes
        // Phase difference should be preserved
        let phase0 = result.Amplitudes.[0].Phase
        let phase1 = result.Amplitudes.[1].Phase
        let phaseDiff = phase1 - phase0
        Assert.True(abs(phaseDiff - Math.PI / 2.0) < 1e-10, $"Expected phase difference pi/2, got {phaseDiff}")

    // ========================================================================
    // PREPARE STATE FROM AMPLITUDES
    // ========================================================================

    [<Fact>]
    let ``prepareStateFromAmplitudes creates circuit for 1-qubit state`` () =
        let amplitudes = [| Complex(1.0, 0.0); Complex(1.0, 0.0) |]
        let emptyCircuit = CircuitBuilder.empty 1
        let circuit = MottonenStatePreparation.prepareStateFromAmplitudes amplitudes [|0|] emptyCircuit
        Assert.True(circuit.QubitCount >= 1, "Circuit should have at least 1 qubit")

    [<Fact>]
    let ``prepareStateFromAmplitudes creates circuit for 2-qubit state`` () =
        let amplitudes = [| Complex(1.0, 0.0); Complex(0.0, 0.0); Complex(0.0, 0.0); Complex(1.0, 0.0) |]
        let emptyCircuit = CircuitBuilder.empty 2
        let circuit = MottonenStatePreparation.prepareStateFromAmplitudes amplitudes [|0; 1|] emptyCircuit
        Assert.True(circuit.QubitCount >= 2, "Circuit should have at least 2 qubits")

    // ========================================================================
    // PREPARE STATE FROM REAL AMPLITUDES
    // ========================================================================

    [<Fact>]
    let ``prepareStateFromRealAmplitudes creates valid circuit`` () =
        let amplitudes = [| 1.0; 1.0; 1.0; 1.0 |]
        let emptyCircuit = CircuitBuilder.empty 2
        let circuit = MottonenStatePreparation.prepareStateFromRealAmplitudes amplitudes [|0; 1|] emptyCircuit
        Assert.True(circuit.QubitCount >= 2)
        // Should have gates for uniform superposition
        Assert.True(circuit.Gates.Length > 0, "Circuit should have gates for non-trivial state")

    [<Fact>]
    let ``prepareStateFromRealAmplitudes with basis state`` () =
        // |0> state should produce minimal circuit
        let amplitudes = [| 1.0; 0.0 |]
        let emptyCircuit = CircuitBuilder.empty 1
        let circuit = MottonenStatePreparation.prepareStateFromRealAmplitudes amplitudes [|0|] emptyCircuit
        Assert.True(circuit.QubitCount >= 1)

    [<Fact>]
    let ``prepareState with qubit count mismatch throws`` () =
        let amplitudes = [| Complex(1.0, 0.0); Complex(1.0, 0.0) |]
        let state = MottonenStatePreparation.normalizeState amplitudes
        let emptyCircuit = CircuitBuilder.empty 3
        // state has 1 qubit but we pass 3-qubit indices
        Assert.Throws<Exception>(fun () ->
            MottonenStatePreparation.prepareState state [|0; 1; 2|] emptyCircuit |> ignore
        ) |> ignore
