namespace FSharp.Azure.Quantum.Topological.Tests

open Xunit
open FSharp.Azure.Quantum
open FSharp.Azure.Quantum.Topological
open FSharp.Azure.Quantum.Core
open FSharp.Azure.Quantum.Core.BackendAbstraction
open FSharp.Azure.Quantum.Core.CircuitAbstraction
open System
open System.Numerics

/// End-to-end amplitude verification tests for the topological braiding pipeline.
///
/// These tests create a TopologicalBackend, apply gates through the FULL braiding
/// pipeline (GateToBraid → braidSuperpositionDirected → R-matrix phases), and then
/// verify that the resulting quantum state amplitudes match the expected gate behavior.
///
/// MOTIVATION: Prior to these tests, no test in the codebase verified that the
/// braiding pipeline produced correct quantum state amplitudes. All existing tests
/// checked only structural properties (braid count, gate labels, normalization).
///
/// REFERENCE: Simon, "Topological Quantum" (epub/topological.txt)
///   - Ising R-matrices: R^I_{σσ} = e^{-iπ/8}, R^ψ_{σσ} = e^{3iπ/8} (Eq. 10.9-10.10)
///   - Relative phase per exchange: e^{3iπ/8} / e^{-iπ/8} = e^{iπ/2} = i
///   - This means one clockwise braid = S gate (not T gate!)
///   - Ising anyons can only produce Clifford gates by braiding (Section 11.2.4)
module BraidingAmplitudeTests =

    // ========================================================================
    // HELPERS
    // ========================================================================

    /// Create a 1-qubit Ising backend, initialize to |0⟩, and return (backend, state)
    let private initSingleQubit () =
        let backend = TopologicalUnifiedBackend.TopologicalUnifiedBackend(AnyonSpecies.AnyonType.Ising, 20) :> IQuantumBackend
        match backend.InitializeState 1 with
        | Ok state -> (backend, state)
        | Error err -> failwith $"InitializeState failed: {err}"

    /// Apply a gate operation and return the resulting state
    let private applyGate (backend: IQuantumBackend) (gate: CircuitBuilder.Gate) (state: QuantumState) =
        match backend.ApplyOperation (QuantumOperation.Gate gate) state with
        | Ok newState -> newState
        | Error err -> failwith $"ApplyOperation failed for gate {gate}: {err}"

    /// Extract the amplitude vector from a FusionSuperposition state
    let private getAmplitudes (state: QuantumState) : Complex[] =
        match state with
        | QuantumState.FusionSuperposition fs -> fs.GetAmplitudeVector()
        | _ -> failwith "Expected FusionSuperposition state"

    /// Check that two complex numbers are approximately equal (up to global phase)
    let private complexApproxEqual (tolerance: float) (expected: Complex) (actual: Complex) =
        let diff = expected - actual
        Complex.Abs diff < tolerance

    /// Check that a state vector matches expected amplitudes up to global phase.
    /// Global phase is irrelevant in quantum mechanics, so we find the phase factor
    /// that best aligns the vectors and then compare.
    let private assertAmplitudesMatchUpToGlobalPhase (tolerance: float) (expected: Complex[]) (actual: Complex[]) (description: string) =
        Assert.Equal(expected.Length, actual.Length)

        // Find a non-zero amplitude to determine the global phase
        let mutable phaseFound = false
        let mutable globalPhase = Complex.One
        for i in 0 .. expected.Length - 1 do
            if not phaseFound && Complex.Abs expected.[i] > 1e-10 && Complex.Abs actual.[i] > 1e-10 then
                globalPhase <- actual.[i] / expected.[i]
                // Normalize to unit magnitude
                globalPhase <- globalPhase / Complex(Complex.Abs globalPhase, 0.0)
                phaseFound <- true

        // Compare each amplitude (actual should equal globalPhase * expected)
        for i in 0 .. expected.Length - 1 do
            let adjustedExpected = globalPhase * expected.[i]
            let diff = Complex.Abs (actual.[i] - adjustedExpected)
            Assert.True(diff < tolerance,
                $"{description}: Amplitude mismatch at index {i}. " +
                $"Expected {adjustedExpected} (with global phase {globalPhase}), got {actual.[i]}. " +
                $"Diff = {diff}")

    /// Check that the RELATIVE phase between |0⟩ and |1⟩ components matches.
    /// This is the physically meaningful quantity (global phase cancels).
    let private assertRelativePhase (tolerance: float) (expectedRelPhase: Complex) (amplitudes: Complex[]) (description: string) =
        Assert.True(amplitudes.Length >= 2, $"{description}: Need at least 2 amplitudes")

        // Both amplitudes must be non-zero for a relative phase to be defined
        let mag0 = Complex.Abs amplitudes.[0]
        let mag1 = Complex.Abs amplitudes.[1]

        if mag0 < 1e-10 || mag1 < 1e-10 then
            Assert.Fail($"{description}: Cannot compute relative phase when amplitude is zero. " +
                        $"|0⟩ = {amplitudes.[0]} (mag={mag0}), |1⟩ = {amplitudes.[1]} (mag={mag1})")
        else
            // Relative phase = amp[1] / amp[0] (normalized to unit magnitude)
            let actualRel = amplitudes.[1] / amplitudes.[0]
            let actualRelNorm = actualRel / Complex(Complex.Abs actualRel, 0.0)
            let diff = Complex.Abs (actualRelNorm - expectedRelPhase)
            Assert.True(diff < tolerance,
                $"{description}: Relative phase mismatch. " +
                $"Expected {expectedRelPhase}, got {actualRelNorm}. Diff = {diff}")

    // ========================================================================
    // T GATE AMPLITUDE TESTS
    // ========================================================================
    //
    // The T gate should apply: |0⟩ → |0⟩, |1⟩ → e^{iπ/4} |1⟩
    //
    // Starting from |0⟩: T|0⟩ = |0⟩ (no change, diagonal gate on computational basis)
    // Starting from |+⟩ = (|0⟩ + |1⟩)/√2: T|+⟩ = (|0⟩ + e^{iπ/4}|1⟩)/√2
    //
    // T gate is handled via amplitude-level intercept in TopologicalBackend.ApplyGate
    // because Ising anyon braiding can only produce S-gate phases (π/2), not T-gate (π/4).
    // Reference: Simon "Topological Quantum" §11.2.4

    [<Fact>]
    let ``T gate on |0⟩ should leave state unchanged`` () =
        // T|0⟩ = |0⟩ — diagonal gate doesn't change |0⟩ amplitude
        // This test should PASS even with the phase bug (no |1⟩ component to shift)
        let (backend, state) = initSingleQubit ()
        let afterT = applyGate backend (CircuitBuilder.Gate.T 0) state
        let amps = getAmplitudes afterT

        // |0⟩ amplitude should be ~1 (up to global phase), |1⟩ should be ~0
        Assert.True(Complex.Abs amps.[0] > 0.99, $"Expected |0⟩ ≈ 1, got {amps.[0]}")
        Assert.True(Complex.Abs amps.[1] < 0.01, $"Expected |1⟩ ≈ 0, got {amps.[1]}")

    [<Fact>]
    let ``T gate on |+⟩ should produce relative phase e^(iπ/4)`` () =
        // Prepare |+⟩ = H|0⟩, then apply T.
        // Expected: T|+⟩ = (|0⟩ + e^{iπ/4}|1⟩)/√2
        // Relative phase between |1⟩ and |0⟩ should be e^{iπ/4}
        //
        // T is applied via amplitude intercept (not braiding) because Ising anyons
        // cannot produce π/4 phase by braiding alone.
        let (backend, state) = initSingleQubit ()
        let afterH = applyGate backend (CircuitBuilder.Gate.H 0) state
        let afterT = applyGate backend (CircuitBuilder.Gate.T 0) afterH
        let amps = getAmplitudes afterT

        let expectedRelPhase = Complex(cos (Math.PI / 4.0), sin (Math.PI / 4.0))  // e^{iπ/4}
        assertRelativePhase 0.05 expectedRelPhase amps "T gate on |+⟩"

    [<Fact>]
    let ``Two T gates on |+⟩ should produce S gate relative phase e^(iπ/2)`` () =
        // T²|+⟩ = S|+⟩ = (|0⟩ + i|1⟩)/√2
        // Relative phase should be e^{iπ/2} = i
        let (backend, state) = initSingleQubit ()
        let afterH = applyGate backend (CircuitBuilder.Gate.H 0) state
        let afterT1 = applyGate backend (CircuitBuilder.Gate.T 0) afterH
        let afterT2 = applyGate backend (CircuitBuilder.Gate.T 0) afterT1
        let amps = getAmplitudes afterT2

        let expectedRelPhase = Complex(0.0, 1.0)  // e^{iπ/2} = i
        assertRelativePhase 0.05 expectedRelPhase amps "T² = S gate on |+⟩"

    // ========================================================================
    // S GATE AMPLITUDE TESTS
    // ========================================================================
    //
    // The S gate should apply: |0⟩ → |0⟩, |1⟩ → i|1⟩
    // Relative phase = e^{iπ/2} = i
    //
    // FIXED: S gate = 1 clockwise braid (exact for Ising anyons).
    // One braid gives relative phase e^{iπ/2} = i = S gate (correct!).

    [<Fact>]
    let ``S gate on |+⟩ should produce relative phase i`` () =
        // S|+⟩ = (|0⟩ + i|1⟩)/√2
        //
        // FIXED: S = 1 braid → relative phase e^{iπ/2} = i (correct)
        let (backend, state) = initSingleQubit ()
        let afterH = applyGate backend (CircuitBuilder.Gate.H 0) state
        let afterS = applyGate backend (CircuitBuilder.Gate.S 0) afterH
        let amps = getAmplitudes afterS

        let expectedRelPhase = Complex(0.0, 1.0)  // i
        assertRelativePhase 0.05 expectedRelPhase amps "S gate on |+⟩"

    [<Fact>]
    let ``S gate on |0⟩ should leave state unchanged`` () =
        // S|0⟩ = |0⟩ — diagonal gate doesn't change |0⟩
        let (backend, state) = initSingleQubit ()
        let afterS = applyGate backend (CircuitBuilder.Gate.S 0) state
        let amps = getAmplitudes afterS

        Assert.True(Complex.Abs amps.[0] > 0.99, $"Expected |0⟩ ≈ 1, got {amps.[0]}")
        Assert.True(Complex.Abs amps.[1] < 0.01, $"Expected |1⟩ ≈ 0, got {amps.[1]}")

    [<Fact>]
    let ``SDG gate on |+⟩ should produce relative phase -i`` () =
        // S†|+⟩ = (|0⟩ - i|1⟩)/√2
        // Relative phase should be e^{-iπ/2} = -i
        //
        // FIXED: S† = 1 CCW braid → relative phase e^{-iπ/2} = -i (correct)
        let (backend, state) = initSingleQubit ()
        let afterH = applyGate backend (CircuitBuilder.Gate.H 0) state
        let afterSDG = applyGate backend (CircuitBuilder.Gate.SDG 0) afterH
        let amps = getAmplitudes afterSDG

        let expectedRelPhase = Complex(0.0, -1.0)  // -i
        assertRelativePhase 0.05 expectedRelPhase amps "S† gate on |+⟩"

    // ========================================================================
    // Z GATE AMPLITUDE TESTS (via gate intercept)
    // ========================================================================
    //
    // Z gate should apply: |0⟩ → |0⟩, |1⟩ → -|1⟩
    // Relative phase = e^{iπ} = -1
    //
    // NOTE: Z is currently intercepted in ApplyGate (bypasses braiding),
    // so this test checks the intercept path, not the braiding path.
    // After intercept removal, this will go through braiding.

    [<Fact>]
    let ``Z gate on |+⟩ should produce relative phase -1`` () =
        // Z|+⟩ = (|0⟩ - |1⟩)/√2 = |−⟩
        let (backend, state) = initSingleQubit ()
        let afterH = applyGate backend (CircuitBuilder.Gate.H 0) state
        let afterZ = applyGate backend (CircuitBuilder.Gate.Z 0) afterH
        let amps = getAmplitudes afterZ

        let expectedRelPhase = Complex(-1.0, 0.0)  // -1
        assertRelativePhase 0.05 expectedRelPhase amps "Z gate on |+⟩"

    // ========================================================================
    // COMBINED GATE TESTS (expose accumulation errors)
    // ========================================================================

    [<Fact>]
    let ``Four T gates on |+⟩ should equal Z gate (relative phase -1)`` () =
        // T⁴ = Z: relative phase = e^{4×iπ/4} = e^{iπ} = -1
        // T gates are applied via amplitude intercept (exact), so T⁴ gives correct Z.
        let (backend, state) = initSingleQubit ()
        let afterH = applyGate backend (CircuitBuilder.Gate.H 0) state
        let mutable current = afterH
        for _ in 1..4 do
            current <- applyGate backend (CircuitBuilder.Gate.T 0) current
        let amps = getAmplitudes current

        let expectedRelPhase = Complex(-1.0, 0.0)  // -1 (Z gate)
        assertRelativePhase 0.05 expectedRelPhase amps "T⁴ = Z gate on |+⟩"

    [<Fact>]
    let ``Eight T gates on |+⟩ should return to identity (relative phase 1)`` () =
        // T⁸ = I: relative phase = e^{8×iπ/4} = e^{2iπ} = 1
        // T gates are applied via amplitude intercept (exact).
        let (backend, state) = initSingleQubit ()
        let afterH = applyGate backend (CircuitBuilder.Gate.H 0) state
        let mutable current = afterH
        for _ in 1..8 do
            current <- applyGate backend (CircuitBuilder.Gate.T 0) current
        let amps = getAmplitudes current

        let expectedRelPhase = Complex(1.0, 0.0)  // 1 (identity)
        assertRelativePhase 0.05 expectedRelPhase amps "T⁸ = I on |+⟩"

    [<Fact>]
    let ``S followed by S should equal Z (relative phase -1)`` () =
        // S² = Z: relative phase = e^{2×iπ/2} = e^{iπ} = -1
        //
        // FIXED: S = 1 braid, so S² = 2 braids → relative phase (e^{iπ/2})² = -1 (correct)
        let (backend, state) = initSingleQubit ()
        let afterH = applyGate backend (CircuitBuilder.Gate.H 0) state
        let afterS1 = applyGate backend (CircuitBuilder.Gate.S 0) afterH
        let afterS2 = applyGate backend (CircuitBuilder.Gate.S 0) afterS1
        let amps = getAmplitudes afterS2

        let expectedRelPhase = Complex(-1.0, 0.0)  // -1 (Z gate)
        assertRelativePhase 0.05 expectedRelPhase amps "S² = Z gate on |+⟩"

    // ========================================================================
    // PHYSICAL CORRECTNESS: Ising anyon braiding phase verification
    // ========================================================================
    //
    // These tests verify the ACTUAL physical behavior of Ising anyon braiding
    // (what the R-matrix phases produce), independent of what gate label the
    // code assigns to it. This is the ground truth from physics.

    [<Fact>]
    let ``One clockwise braid physically produces relative phase e^(iπ/2) = i`` () =
        // PHYSICS: One clockwise σ-exchange gives:
        //   |0⟩ → e^{-iπ/8} |0⟩  (R^I_{σσ} = e^{-iπ/8})
        //   |1⟩ → e^{3iπ/8} |1⟩  (R^ψ_{σσ} = e^{3iπ/8})
        // Relative phase = e^{3iπ/8} / e^{-iπ/8} = e^{iπ/2} = i
        //
        // This IS the S gate. One braid = S (correct after fix).
        // Reference: Simon "Topological Quantum" Eq. 10.9-10.10
        let (backend, state) = initSingleQubit ()
        let afterH = applyGate backend (CircuitBuilder.Gate.H 0) state

        // Apply one S gate (which internally does 1 clockwise braid)
        let afterBraid = applyGate backend (CircuitBuilder.Gate.S 0) afterH
        let amps = getAmplitudes afterBraid

        // The ACTUAL physical relative phase from one braid is i (S gate)
        // After the fix, the code correctly labels this as S gate.
        let actualRel = amps.[1] / amps.[0]
        let actualRelNorm = actualRel / Complex(Complex.Abs actualRel, 0.0)

        // Physical prediction: relative phase = i (±tolerance for braiding numerics)
        let physicalPhase = Complex(0.0, 1.0)  // i = e^{iπ/2}
        let diff = Complex.Abs (actualRelNorm - physicalPhase)

        // This assertion tests the PHYSICS, not the gate label.
        // It should always pass (it just confirms the R-matrix is correct).
        Assert.True(diff < 0.05,
            $"Physical braid phase mismatch. Expected i, got {actualRelNorm}. Diff = {diff}. " +
            $"If this fails, the R-matrix implementation is wrong.")

    [<Fact>]
    let ``Two clockwise braids physically produce relative phase -1`` () =
        // Two clockwise braids: relative phase = (e^{iπ/2})² = e^{iπ} = -1 = Z gate
        let (backend, state) = initSingleQubit ()
        let afterH = applyGate backend (CircuitBuilder.Gate.H 0) state

        // Apply 2 S gates = 2 clockwise braids (S gate = 1 braid after fix)
        let afterBraid1 = applyGate backend (CircuitBuilder.Gate.S 0) afterH
        let afterBraid2 = applyGate backend (CircuitBuilder.Gate.S 0) afterBraid1
        let amps = getAmplitudes afterBraid2

        let actualRel = amps.[1] / amps.[0]
        let actualRelNorm = actualRel / Complex(Complex.Abs actualRel, 0.0)

        let physicalPhase = Complex(-1.0, 0.0)  // -1 = e^{iπ}
        let diff = Complex.Abs (actualRelNorm - physicalPhase)

        Assert.True(diff < 0.05,
            $"Physical 2-braid phase mismatch. Expected -1, got {actualRelNorm}. Diff = {diff}")

    [<Fact>]
    let ``Four clockwise braids physically produce relative phase +1 (identity)`` () =
        // Four clockwise braids: relative phase = (e^{iπ/2})⁴ = e^{2iπ} = 1
        // Period of Ising braiding is 4 (not 8)
        let (backend, state) = initSingleQubit ()
        let afterH = applyGate backend (CircuitBuilder.Gate.H 0) state

        // Apply 4 individual S gates = 4 clockwise braids
        // S gate = 1 CW braid in Ising model, so 4 S gates = 4 braids
        let mutable current = afterH
        for _ in 1..4 do
            current <- applyGate backend (CircuitBuilder.Gate.S 0) current
        let amps = getAmplitudes current

        let actualRel = amps.[1] / amps.[0]
        let actualRelNorm = actualRel / Complex(Complex.Abs actualRel, 0.0)

        let physicalPhase = Complex(1.0, 0.0)  // +1 = identity
        let diff = Complex.Abs (actualRelNorm - physicalPhase)

        Assert.True(diff < 0.05,
            $"Physical 4-braid phase mismatch. Expected +1, got {actualRelNorm}. Diff = {diff}")
