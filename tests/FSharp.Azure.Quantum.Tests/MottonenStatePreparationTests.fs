namespace FSharp.Azure.Quantum.Tests

open System
open System.Numerics
open Xunit
open FSharp.Azure.Quantum.Algorithms
open FSharp.Azure.Quantum
open FSharp.Azure.Quantum.Core
open FSharp.Azure.Quantum.Core.BackendAbstraction
open FSharp.Azure.Quantum.Core.CircuitAbstraction
open FSharp.Azure.Quantum.Backends
open FSharp.Azure.Quantum.LocalSimulator

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

    // ========================================================================
    // END-TO-END: PREPARED CIRCUIT REPRODUCES THE TARGET STATE
    // ========================================================================

    /// Build the preparation circuit, run it on the local simulator and
    /// assert the resulting state vector matches the (normalized) target
    /// amplitudes up to a global phase.
    let private assertPreparesState (amplitudes: Complex[]) =
        let target = (MottonenStatePreparation.normalizeState amplitudes).Amplitudes
        let numQubits =
            let rec log2 k = if k <= 1 then 0 else 1 + log2 (k / 2)
            log2 amplitudes.Length

        let circuit =
            CircuitBuilder.empty numQubits
            |> MottonenStatePreparation.prepareStateFromAmplitudes amplitudes [| 0 .. numQubits - 1 |]

        let backend = LocalBackend.LocalBackend() :> IQuantumBackend

        match backend.ExecuteToState (CircuitWrapper(circuit) :> ICircuit) with
        | Error err ->
            failwith $"ExecuteToState failed: %A{err}"
        | Ok (QuantumState.StateVector sv) ->
            let actual = Array.init (StateVector.dimension sv) (fun i -> StateVector.getAmplitude i sv)
            Assert.Equal(target.Length, actual.Length)

            // Align on the largest target amplitude to factor out global phase
            let refIdx =
                target
                |> Array.mapi (fun i a -> i, a.Magnitude)
                |> Array.maxBy snd
                |> fst

            Assert.True(actual[refIdx].Magnitude > 1e-6, "Reference amplitude missing in prepared state")
            let globalPhase = actual[refIdx] / target[refIdx]

            (target, actual)
            ||> Array.iteri2 (fun i expected got ->
                let aligned = got / globalPhase
                Assert.True(
                    (aligned - expected).Magnitude < 1e-6,
                    $"Amplitude mismatch at index {i}: expected {expected}, got {aligned}"))
        | Ok _ ->
            failwith "Expected StateVector result"

    [<Fact>]
    let ``prepareState reproduces 3-qubit state with non-uniform real amplitudes`` () =
        assertPreparesState [|
            Complex(0.1, 0.0); Complex(0.25, 0.0); Complex(0.3, 0.0); Complex(0.05, 0.0)
            Complex(0.45, 0.0); Complex(0.2, 0.0); Complex(0.5, 0.0); Complex(0.4, 0.0)
        |]

    [<Fact>]
    let ``prepareState reproduces 3-qubit state with complex phases`` () =
        assertPreparesState [|
            Complex(0.1, 0.2); Complex(0.25, -0.15); Complex(0.0, 0.3); Complex(0.05, 0.05)
            Complex(0.45, -0.1); Complex(0.2, 0.25); Complex(-0.3, 0.1); Complex(0.35, 0.0)
        |]

    [<Fact>]
    let ``prepareState reproduces 4-qubit state with complex amplitudes`` () =
        Array.init 16 (fun i -> Complex(float (i + 1), float (15 - i) * 0.3))
        |> assertPreparesState

    [<Fact>]
    let ``prepareState reproduces 2-qubit state with pure phase differences`` () =
        assertPreparesState [|
            Complex(0.5, 0.0); Complex(0.0, 0.5); Complex(-0.5, 0.0); Complex(0.0, -0.5)
        |]
