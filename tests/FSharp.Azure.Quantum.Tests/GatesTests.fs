namespace FSharp.Azure.Quantum.Tests

open System
open System.Numerics
open Xunit
open FSharp.Azure.Quantum.LocalSimulator

module GatesTests =

    [<Fact>]
    let ``Pauli-X gate - should flip qubit basis states correctly`` () =
        // Test X gate on |0⟩ state → |1⟩
        let state0 = StateVector.init 1
        let state1 = Gates.applyX 0 state0

        Assert.Equal(0.0, (StateVector.getAmplitude 0 state1).Real, 10)
        Assert.Equal(1.0, (StateVector.getAmplitude 1 state1).Real, 10)

        // Test X gate on |1⟩ state → |0⟩
        let state1Input = StateVector.create [| Complex.Zero; Complex.One |]
        let state0Output = Gates.applyX 0 state1Input

        Assert.Equal(1.0, (StateVector.getAmplitude 0 state0Output).Real, 10)
        Assert.Equal(0.0, (StateVector.getAmplitude 1 state0Output).Real, 10)

        // Test X twice returns to original (X² = I)
        let stateOriginal = StateVector.init 1
        let stateAfterXX = Gates.applyX 0 (Gates.applyX 0 stateOriginal)
        Assert.True(StateVector.equals stateOriginal stateAfterXX)

        // Test on multi-qubit state: X on qubit 1 of |00⟩ → |10⟩
        let state2q = StateVector.init 2 // |00⟩
        let stateFlipped = Gates.applyX 1 state2q // Flip qubit 1 (middle bit)
        Assert.Equal(0.0, (StateVector.getAmplitude 0 stateFlipped).Real, 10) // |00⟩
        Assert.Equal(0.0, (StateVector.getAmplitude 1 stateFlipped).Real, 10) // |01⟩
        Assert.Equal(1.0, (StateVector.getAmplitude 2 stateFlipped).Real, 10) // |10⟩
        Assert.Equal(0.0, (StateVector.getAmplitude 3 stateFlipped).Real, 10) // |11⟩

    [<Fact>]
    let ``Pauli-Y gate - should apply Y rotation correctly`` () =
        // Test Y gate on |0⟩ → i|1⟩
        let state0 = StateVector.init 1
        let state1 = Gates.applyY 0 state0

        Assert.Equal(0.0, (StateVector.getAmplitude 0 state1).Real, 10)
        Assert.Equal(0.0, (StateVector.getAmplitude 0 state1).Imaginary, 10)
        Assert.Equal(0.0, (StateVector.getAmplitude 1 state1).Real, 10)
        Assert.Equal(1.0, (StateVector.getAmplitude 1 state1).Imaginary, 10) // i

        // Test Y gate on |1⟩ → -i|0⟩
        let state1Input = StateVector.create [| Complex.Zero; Complex.One |]
        let state0Output = Gates.applyY 0 state1Input

        Assert.Equal(0.0, (StateVector.getAmplitude 0 state0Output).Real, 10)
        Assert.Equal(-1.0, (StateVector.getAmplitude 0 state0Output).Imaginary, 10) // -i
        Assert.Equal(0.0, (StateVector.getAmplitude 1 state0Output).Real, 10)
        Assert.Equal(0.0, (StateVector.getAmplitude 1 state0Output).Imaginary, 10)

    [<Fact>]
    let ``Pauli-Z gate - should apply phase flip correctly`` () =
        // Test Z gate on |0⟩ → |0⟩ (no change)
        let state0 = StateVector.init 1
        let state0After = Gates.applyZ 0 state0
        Assert.True(StateVector.equals state0 state0After)

        // Test Z gate on |1⟩ → -|1⟩
        let state1 = StateVector.create [| Complex.Zero; Complex.One |]
        let state1After = Gates.applyZ 0 state1

        Assert.Equal(0.0, (StateVector.getAmplitude 0 state1After).Real, 10)
        Assert.Equal(-1.0, (StateVector.getAmplitude 1 state1After).Real, 10)

        // Test Z on superposition: (|0⟩+|1⟩)/√2 → (|0⟩-|1⟩)/√2
        let sqrtHalf = 1.0 / sqrt 2.0

        let superposition =
            StateVector.create [| Complex(sqrtHalf, 0.0); Complex(sqrtHalf, 0.0) |]

        let afterZ = Gates.applyZ 0 superposition

        Assert.Equal(sqrtHalf, (StateVector.getAmplitude 0 afterZ).Real, 10)
        Assert.Equal(-sqrtHalf, (StateVector.getAmplitude 1 afterZ).Real, 10)

    [<Fact>]
    let ``Hadamard gate - should create superposition correctly`` () =
        // Test H on |0⟩ → (|0⟩+|1⟩)/√2
        let state0 = StateVector.init 1
        let superposition = Gates.applyH 0 state0

        let sqrtHalf = 1.0 / sqrt 2.0
        Assert.Equal(sqrtHalf, (StateVector.getAmplitude 0 superposition).Real, 10)
        Assert.Equal(sqrtHalf, (StateVector.getAmplitude 1 superposition).Real, 10)

        // Test H on |1⟩ → (|0⟩-|1⟩)/√2
        let state1 = StateVector.create [| Complex.Zero; Complex.One |]
        let superposition1 = Gates.applyH 0 state1

        Assert.Equal(sqrtHalf, (StateVector.getAmplitude 0 superposition1).Real, 10)
        Assert.Equal(-sqrtHalf, (StateVector.getAmplitude 1 superposition1).Real, 10)

        // Test H² = I (Hadamard is self-inverse)
        let stateOriginal = StateVector.init 1
        let stateAfterHH = Gates.applyH 0 (Gates.applyH 0 stateOriginal)
        Assert.True(StateVector.equals stateOriginal stateAfterHH)

    [<Fact>]
    let ``Rx gate - should rotate around X axis correctly`` () =
        // Rx(π) on |0⟩ → -i|1⟩
        let state0 = StateVector.init 1
        let stateAfterRx = Gates.applyRx 0 Math.PI state0

        Assert.Equal(0.0, (StateVector.getAmplitude 0 stateAfterRx).Real, 10)
        Assert.Equal(0.0, (StateVector.getAmplitude 0 stateAfterRx).Imaginary, 10)
        Assert.Equal(0.0, (StateVector.getAmplitude 1 stateAfterRx).Real, 10)
        Assert.Equal(-1.0, (StateVector.getAmplitude 1 stateAfterRx).Imaginary, 10) // -i

        // Rx(0) = I (identity - no change)
        let state0Original = StateVector.init 1
        let stateAfterRx0 = Gates.applyRx 0 0.0 state0Original
        Assert.True(StateVector.equals state0Original stateAfterRx0)

        // Rx(2π) ≈ I (full rotation returns to original, up to global phase)
        let stateAfterRx2Pi = Gates.applyRx 0 (2.0 * Math.PI) state0Original
        // Global phase difference allowed, check norm preserved
        Assert.Equal(1.0, StateVector.norm stateAfterRx2Pi, 10)

    [<Fact>]
    let ``Ry gate - should rotate around Y axis correctly`` () =
        // Ry(π/2) on |0⟩ → (|0⟩+|1⟩)/√2
        let state0 = StateVector.init 1
        let stateAfterRy = Gates.applyRy 0 (Math.PI / 2.0) state0

        let sqrtHalf = 1.0 / sqrt 2.0
        Assert.Equal(sqrtHalf, (StateVector.getAmplitude 0 stateAfterRy).Real, 10)
        Assert.Equal(sqrtHalf, (StateVector.getAmplitude 1 stateAfterRy).Real, 10)

        // Ry(π) on |0⟩ → |1⟩
        let stateAfterRyPi = Gates.applyRy 0 Math.PI state0
        Assert.Equal(0.0, (StateVector.getAmplitude 0 stateAfterRyPi).Real, 10)
        Assert.Equal(1.0, (StateVector.getAmplitude 1 stateAfterRyPi).Real, 10)

        // Ry(0) = I
        let stateAfterRy0 = Gates.applyRy 0 0.0 state0
        Assert.True(StateVector.equals state0 stateAfterRy0)

    [<Fact>]
    let ``Rz gate - should rotate around Z axis correctly`` () =
        // Rz only affects |1⟩ component (adds phase)
        // Rz(θ) |0⟩ = e^(-iθ/2) |0⟩ (global phase)
        // Rz(θ) |1⟩ = e^(iθ/2) |1⟩

        let state0 = StateVector.init 1
        let stateAfterRz = Gates.applyRz 0 Math.PI state0
        // |0⟩ gets global phase e^(-iπ/2) = -i, but we check norm preservation
        Assert.Equal(1.0, StateVector.norm stateAfterRz, 10)

        // Test on superposition to see relative phase
        let sqrtHalf = 1.0 / sqrt 2.0

        let superposition =
            StateVector.create [| Complex(sqrtHalf, 0.0); Complex(sqrtHalf, 0.0) |]

        let afterRz = Gates.applyRz 0 Math.PI superposition

        // After Rz(π): (e^(-iπ/2)|0⟩ + e^(iπ/2)|1⟩)/√2 = (-i|0⟩ + i|1⟩)/√2
        Assert.Equal(1.0, StateVector.norm afterRz, 10)

        // Rz(0) = I
        let stateAfterRz0 = Gates.applyRz 0 0.0 state0
        Assert.True(StateVector.equals state0 stateAfterRz0)

    [<Fact>]
    let ``Gate application - should preserve state vector norm`` () =
        // Test that all gates preserve unitarity (norm = 1)
        let state0 = StateVector.init 1
        let sqrtHalf = 1.0 / sqrt 2.0

        let superposition =
            StateVector.create [| Complex(sqrtHalf, 0.0); Complex(sqrtHalf, 0.0) |]

        // Test each gate preserves norm
        Assert.Equal(1.0, StateVector.norm (Gates.applyX 0 state0), 10)
        Assert.Equal(1.0, StateVector.norm (Gates.applyY 0 state0), 10)
        Assert.Equal(1.0, StateVector.norm (Gates.applyZ 0 state0), 10)
        Assert.Equal(1.0, StateVector.norm (Gates.applyH 0 state0), 10)
        Assert.Equal(1.0, StateVector.norm (Gates.applyRx 0 1.234 superposition), 10)
        Assert.Equal(1.0, StateVector.norm (Gates.applyRy 0 2.345 superposition), 10)
        Assert.Equal(1.0, StateVector.norm (Gates.applyRz 0 3.456 superposition), 10)

    [<Fact>]
    let ``Multi-qubit gate application - should apply to correct qubit`` () =
        // Test applying gates to specific qubits in multi-qubit systems
        let state3q = StateVector.init 3 // |000⟩

        // Apply X to qubit 0 (rightmost): |000⟩ → |001⟩
        let stateXq0 = Gates.applyX 0 state3q
        Assert.Equal(0.0, (StateVector.getAmplitude 0 stateXq0).Real, 10) // |000⟩
        Assert.Equal(1.0, (StateVector.getAmplitude 1 stateXq0).Real, 10) // |001⟩

        // Apply X to qubit 1 (middle): |000⟩ → |010⟩
        let stateXq1 = Gates.applyX 1 state3q
        Assert.Equal(0.0, (StateVector.getAmplitude 0 stateXq1).Real, 10) // |000⟩
        Assert.Equal(0.0, (StateVector.getAmplitude 1 stateXq1).Real, 10) // |001⟩
        Assert.Equal(1.0, (StateVector.getAmplitude 2 stateXq1).Real, 10) // |010⟩

        // Apply X to qubit 2 (leftmost): |000⟩ → |100⟩
        let stateXq2 = Gates.applyX 2 state3q
        Assert.Equal(0.0, (StateVector.getAmplitude 0 stateXq2).Real, 10) // |000⟩
        Assert.Equal(1.0, (StateVector.getAmplitude 4 stateXq2).Real, 10) // |100⟩

    [<Fact>]
    let ``Gate composition - should apply gates in correct order`` () =
        // Test H then X: H(X|0⟩) = H|1⟩ = (|0⟩-|1⟩)/√2
        let state0 = StateVector.init 1
        let stateXH = state0 |> Gates.applyX 0 |> Gates.applyH 0

        let sqrtHalf = 1.0 / sqrt 2.0
        Assert.Equal(sqrtHalf, (StateVector.getAmplitude 0 stateXH).Real, 10)
        Assert.Equal(-sqrtHalf, (StateVector.getAmplitude 1 stateXH).Real, 10)

        // Test X then H: X(H|0⟩) = X((|0⟩+|1⟩)/√2) = (|1⟩+|0⟩)/√2
        let stateHX = state0 |> Gates.applyH 0 |> Gates.applyX 0
        Assert.Equal(sqrtHalf, (StateVector.getAmplitude 0 stateHX).Real, 10)
        Assert.Equal(sqrtHalf, (StateVector.getAmplitude 1 stateHX).Real, 10)

    [<Fact>]
    let ``Pauli matrices - should satisfy algebraic properties`` () =
        // Test X² = Y² = Z² = I
        let state = StateVector.init 1

        let stateXX = state |> Gates.applyX 0 |> Gates.applyX 0
        Assert.True(StateVector.equals state stateXX)

        let stateYY = state |> Gates.applyY 0 |> Gates.applyY 0
        Assert.True(StateVector.equals state stateYY)

        let stateZZ = state |> Gates.applyZ 0 |> Gates.applyZ 0
        Assert.True(StateVector.equals state stateZZ)

        // Test XYZ = iI (up to global phase)
        let stateXYZ = state |> Gates.applyX 0 |> Gates.applyY 0 |> Gates.applyZ 0
        // Result should have same norm as original
        Assert.Equal(StateVector.norm state, StateVector.norm stateXYZ, 10)

    [<Fact>]
    let ``Rotation gates - should handle special angles correctly`` () =
        let state0 = StateVector.init 1

        // Rx(π/2) on |0⟩
        let stateRxHalfPi = Gates.applyRx 0 (Math.PI / 2.0) state0
        Assert.Equal(1.0, StateVector.norm stateRxHalfPi, 10)

        // Ry(π/2) on |0⟩ → (|0⟩+|1⟩)/√2
        let stateRyHalfPi = Gates.applyRy 0 (Math.PI / 2.0) state0
        let sqrtHalf = 1.0 / sqrt 2.0
        Assert.Equal(sqrtHalf, (StateVector.getAmplitude 0 stateRyHalfPi).Real, 2)
        Assert.Equal(sqrtHalf, (StateVector.getAmplitude 1 stateRyHalfPi).Real, 2)

        // Rz(π/4) preserves |0⟩ (adds global phase only)
        let stateRzQuarterPi = Gates.applyRz 0 (Math.PI / 4.0) state0
        Assert.Equal(1.0, StateVector.norm stateRzQuarterPi, 10)

    [<Fact>]
    let ``Gate validation - should reject invalid qubit indices`` () =
        let state2q = StateVector.init 2

        // Valid indices: 0, 1
        Gates.applyX 0 state2q |> ignore
        Gates.applyX 1 state2q |> ignore

        // Invalid indices: -1, 2
        Assert.Throws<Exception>(fun () -> Gates.applyX -1 state2q |> ignore) |> ignore
        Assert.Throws<Exception>(fun () -> Gates.applyX 2 state2q |> ignore) |> ignore

    [<Fact>]
    let ``Complex rotation composition - should produce correct final state`` () =
        // Test realistic QAOA-like sequence: H → Rz(θ) → Rx(φ)
        let state0 = StateVector.init 1
        let theta = 0.5
        let phi = 1.2

        let finalState =
            state0 |> Gates.applyH 0 |> Gates.applyRz 0 theta |> Gates.applyRx 0 phi

        // Verify norm preserved through sequence
        Assert.Equal(1.0, StateVector.norm finalState, 10)

        // Verify final state is non-trivial (not |0⟩ or |1⟩)
        let amp0Mag = (StateVector.getAmplitude 0 finalState).Magnitude
        let amp1Mag = (StateVector.getAmplitude 1 finalState).Magnitude
        Assert.True(amp0Mag > 0.01 && amp0Mag < 0.99)
        Assert.True(amp1Mag > 0.01 && amp1Mag < 0.99)

    // =============================================================================
    // TWO-QUBIT GATE TESTS
    // =============================================================================

    [<Fact>]
    let ``CNOT gate - should flip target when control is 1`` () =
        // Test CNOT on |00⟩ → |00⟩ (control=qubit_0=0, no flip)
        let state00 = StateVector.init 2
        let result00 = Gates.applyCNOT 0 1 state00
        Assert.Equal(1.0, (StateVector.getAmplitude 0 result00).Real, 10) // |00⟩
        Assert.Equal(0.0, (StateVector.getAmplitude 1 result00).Real, 10) // |01⟩
        Assert.Equal(0.0, (StateVector.getAmplitude 2 result00).Real, 10) // |10⟩
        Assert.Equal(0.0, (StateVector.getAmplitude 3 result00).Real, 10) // |11⟩

        // Test CNOT on |01⟩ → |11⟩ (control=qubit_0=1, flip target)
        let state01 =
            StateVector.create [| Complex.Zero; Complex.One; Complex.Zero; Complex.Zero |]

        let result01 = Gates.applyCNOT 0 1 state01
        Assert.Equal(0.0, (StateVector.getAmplitude 0 result01).Real, 10) // |00⟩
        Assert.Equal(0.0, (StateVector.getAmplitude 1 result01).Real, 10) // |01⟩
        Assert.Equal(0.0, (StateVector.getAmplitude 2 result01).Real, 10) // |10⟩
        Assert.Equal(1.0, (StateVector.getAmplitude 3 result01).Real, 10) // |11⟩

        // Test CNOT on |10⟩ → |10⟩ (control=qubit_0=0, no flip)
        let state10 =
            StateVector.create [| Complex.Zero; Complex.Zero; Complex.One; Complex.Zero |]

        let result10 = Gates.applyCNOT 0 1 state10
        Assert.Equal(0.0, (StateVector.getAmplitude 0 result10).Real, 10) // |00⟩
        Assert.Equal(0.0, (StateVector.getAmplitude 1 result10).Real, 10) // |01⟩
        Assert.Equal(1.0, (StateVector.getAmplitude 2 result10).Real, 10) // |10⟩ (unchanged)
        Assert.Equal(0.0, (StateVector.getAmplitude 3 result10).Real, 10) // |11⟩

        // Test CNOT on |11⟩ → |01⟩ (control=qubit_0=1, flip target)
        let state11 =
            StateVector.create [| Complex.Zero; Complex.Zero; Complex.Zero; Complex.One |]

        let result11 = Gates.applyCNOT 0 1 state11
        Assert.Equal(0.0, (StateVector.getAmplitude 0 result11).Real, 10) // |00⟩
        Assert.Equal(1.0, (StateVector.getAmplitude 1 result11).Real, 10) // |01⟩
        Assert.Equal(0.0, (StateVector.getAmplitude 2 result11).Real, 10) // |10⟩
        Assert.Equal(0.0, (StateVector.getAmplitude 3 result11).Real, 10) // |11⟩

    [<Fact>]
    let ``CNOT gate - should work with different qubit orderings`` () =
        // Test CNOT with control=1, target=0
        // State |01⟩: qubit_0=1, qubit_1=0
        // CNOT(1,0): control=qubit_1=0, target=qubit_0=1
        // Since control=0, no flip → |01⟩
        let state01a =
            StateVector.create [| Complex.Zero; Complex.One; Complex.Zero; Complex.Zero |]

        let result01a = Gates.applyCNOT 1 0 state01a
        Assert.Equal(0.0, (StateVector.getAmplitude 0 result01a).Real, 10) // |00⟩
        Assert.Equal(1.0, (StateVector.getAmplitude 1 result01a).Real, 10) // |01⟩ (unchanged)
        Assert.Equal(0.0, (StateVector.getAmplitude 2 result01a).Real, 10) // |10⟩
        Assert.Equal(0.0, (StateVector.getAmplitude 3 result01a).Real, 10) // |11⟩

        // State |10⟩: qubit_0=0, qubit_1=1
        // CNOT(1,0): control=qubit_1=1, target=qubit_0=0
        // Since control=1, flip target → |11⟩
        let state10 =
            StateVector.create [| Complex.Zero; Complex.Zero; Complex.One; Complex.Zero |]

        let result10 = Gates.applyCNOT 1 0 state10
        Assert.Equal(0.0, (StateVector.getAmplitude 0 result10).Real, 10) // |00⟩
        Assert.Equal(0.0, (StateVector.getAmplitude 1 result10).Real, 10) // |01⟩
        Assert.Equal(0.0, (StateVector.getAmplitude 2 result10).Real, 10) // |10⟩
        Assert.Equal(1.0, (StateVector.getAmplitude 3 result10).Real, 10) // |11⟩

    [<Fact>]
    let ``CNOT gate - should create entanglement from superposition`` () =
        // Create Bell state: H on qubit 0, then CNOT(0,1) creates (|00⟩+|11⟩)/√2
        let state00 = StateVector.init 2
        let stateSuperpos = Gates.applyH 0 state00 // (|00⟩+|10⟩)/√2
        let bellState = Gates.applyCNOT 0 1 stateSuperpos

        let sqrtHalf = 1.0 / sqrt 2.0
        Assert.Equal(sqrtHalf, (StateVector.getAmplitude 0 bellState).Real, 10) // |00⟩
        Assert.Equal(0.0, (StateVector.getAmplitude 1 bellState).Real, 10) // |01⟩
        Assert.Equal(0.0, (StateVector.getAmplitude 2 bellState).Real, 10) // |10⟩
        Assert.Equal(sqrtHalf, (StateVector.getAmplitude 3 bellState).Real, 10) // |11⟩

    [<Fact>]
    let ``CNOT gate - should be self-inverse`` () =
        // CNOT applied twice returns to original state
        let state =
            StateVector.create [| Complex(0.6, 0.0); Complex(0.0, 0.0); Complex(0.8, 0.0); Complex(0.0, 0.0) |]

        let afterCNOT = Gates.applyCNOT 0 1 state
        let afterTwoCNOT = Gates.applyCNOT 0 1 afterCNOT

        Assert.True(StateVector.equals state afterTwoCNOT)

    [<Fact>]
    let ``CNOT gate - should preserve norm`` () =
        // Test norm preservation on various states
        let state1 = StateVector.init 2
        Assert.Equal(1.0, StateVector.norm (Gates.applyCNOT 0 1 state1), 10)

        let sqrtHalf = 1.0 / sqrt 2.0

        let state2 =
            StateVector.create [| Complex(sqrtHalf, 0.0); Complex(sqrtHalf, 0.0); Complex.Zero; Complex.Zero |]

        Assert.Equal(1.0, StateVector.norm (Gates.applyCNOT 0 1 state2), 10)

        let state3 =
            StateVector.create [| Complex(0.6, 0.0); Complex(0.0, 0.0); Complex(0.8, 0.0); Complex(0.0, 0.0) |]

        Assert.Equal(1.0, StateVector.norm (Gates.applyCNOT 0 1 state3), 10)

    [<Fact>]
    let ``CZ gate - should add phase when both qubits are 1`` () =
        // Test CZ on |00⟩ → |00⟩ (no change)
        let state00 = StateVector.init 2
        let result00 = Gates.applyCZ 0 1 state00
        Assert.True(StateVector.equals state00 result00)

        // Test CZ on |01⟩ → |01⟩ (no change)
        let state01 =
            StateVector.create [| Complex.Zero; Complex.One; Complex.Zero; Complex.Zero |]

        let result01 = Gates.applyCZ 0 1 state01
        Assert.True(StateVector.equals state01 result01)

        // Test CZ on |10⟩ → |10⟩ (no change)
        let state10 =
            StateVector.create [| Complex.Zero; Complex.Zero; Complex.One; Complex.Zero |]

        let result10 = Gates.applyCZ 0 1 state10
        Assert.True(StateVector.equals state10 result10)

        // Test CZ on |11⟩ → -|11⟩ (phase flip)
        let state11 =
            StateVector.create [| Complex.Zero; Complex.Zero; Complex.Zero; Complex.One |]

        let result11 = Gates.applyCZ 0 1 state11
        Assert.Equal(0.0, (StateVector.getAmplitude 0 result11).Real, 10)
        Assert.Equal(0.0, (StateVector.getAmplitude 1 result11).Real, 10)
        Assert.Equal(0.0, (StateVector.getAmplitude 2 result11).Real, 10)
        Assert.Equal(-1.0, (StateVector.getAmplitude 3 result11).Real, 10)

    [<Fact>]
    let ``CZ gate - should be symmetric`` () =
        // CZ(0,1) should equal CZ(1,0) - control and target are interchangeable
        let state =
            StateVector.create [| Complex(0.5, 0.0); Complex(0.5, 0.0); Complex(0.5, 0.0); Complex(0.5, 0.0) |]

        let resultCZ01 = Gates.applyCZ 0 1 state
        let resultCZ10 = Gates.applyCZ 1 0 state

        Assert.True(StateVector.equals resultCZ01 resultCZ10)

    [<Fact>]
    let ``CZ gate - should be self-inverse`` () =
        // CZ applied twice returns to original (Z² = I)
        let state =
            StateVector.create [| Complex(0.5, 0.0); Complex(0.5, 0.0); Complex(0.5, 0.0); Complex(0.5, 0.0) |]

        let afterCZ = Gates.applyCZ 0 1 state
        let afterTwoCZ = Gates.applyCZ 0 1 afterCZ

        Assert.True(StateVector.equals state afterTwoCZ)

    [<Fact>]
    let ``CZ gate - should preserve norm`` () =
        let state1 = StateVector.init 2
        Assert.Equal(1.0, StateVector.norm (Gates.applyCZ 0 1 state1), 10)

        let state2 =
            StateVector.create [| Complex(0.5, 0.0); Complex(0.5, 0.0); Complex(0.5, 0.0); Complex(0.5, 0.0) |]

        Assert.Equal(1.0, StateVector.norm (Gates.applyCZ 0 1 state2), 10)

    [<Fact>]
    let ``Two-qubit gates - should reject invalid indices`` () =
        let state2q = StateVector.init 2

        // Valid indices: 0, 1
        Gates.applyCNOT 0 1 state2q |> ignore
        Gates.applyCZ 0 1 state2q |> ignore

        // Invalid: same control and target
        Assert.Throws<Exception>(fun () -> Gates.applyCNOT 0 0 state2q |> ignore)
        |> ignore

        Assert.Throws<Exception>(fun () -> Gates.applyCZ 1 1 state2q |> ignore)
        |> ignore

        // Invalid: out of range
        Assert.Throws<Exception>(fun () -> Gates.applyCNOT 2 0 state2q |> ignore)
        |> ignore

        Assert.Throws<Exception>(fun () -> Gates.applyCZ 0 3 state2q |> ignore)
        |> ignore

    // ========================================================================
    // ISING INTERACTION GATES (RXX / RYY / RZZ)
    // ========================================================================

    /// Random-ish 2-qubit test state (normalized via create)
    let private testState2q () =
        StateVector.create
            [|
                Complex(0.5, 0.1)
                Complex(-0.3, 0.4)
                Complex(0.2, -0.6)
                Complex(0.1, 0.25)
            |]

    let private assertStatesEqual (expected: StateVector.StateVector) (actual: StateVector.StateVector) =
        for i in 0 .. StateVector.dimension expected - 1 do
            let e = StateVector.getAmplitude i expected
            let a = StateVector.getAmplitude i actual
            Assert.True((e - a).Magnitude < 1e-10, $"Amplitude {i}: expected {e}, got {a}")

    [<Fact>]
    let ``RZZ applies parity-dependent phases`` () =
        // RZZ(θ)|00⟩ = e^(-iθ/2)|00⟩, RZZ(θ)|01⟩ = e^(+iθ/2)|01⟩
        let theta = 0.7
        let state00 = StateVector.init 2
        let after00 = Gates.applyRzz 0 1 theta state00

        let expectedPhase = Complex(cos (theta / 2.0), -sin(theta / 2.0))
        Assert.True((StateVector.getAmplitude 0 after00 - expectedPhase).Magnitude < 1e-10)

        // |01⟩ (qubit 0 = 1, qubit 1 = 0 → bits differ → e^(+iθ/2))
        let state01 =
            StateVector.create [| Complex.Zero; Complex.One; Complex.Zero; Complex.Zero |]

        let after01 = Gates.applyRzz 0 1 theta state01
        let expectedDiff = Complex(cos (theta / 2.0), sin (theta / 2.0))
        Assert.True((StateVector.getAmplitude 1 after01 - expectedDiff).Magnitude < 1e-10)

    [<Fact>]
    let ``RXX matches its CNOT-RZ-CNOT decomposition`` () =
        // RXX(θ) = (H⊗H)·CNOT·RZ(θ)·CNOT·(H⊗H)
        let theta = 1.234
        let state = testState2q ()

        let native = Gates.applyRxx 0 1 theta state

        let decomposed =
            state
            |> Gates.applyH 0
            |> Gates.applyH 1
            |> Gates.applyCNOT 0 1
            |> Gates.applyRz 1 theta
            |> Gates.applyCNOT 0 1
            |> Gates.applyH 0
            |> Gates.applyH 1

        assertStatesEqual decomposed native

    [<Fact>]
    let ``RYY matches its RX-conjugated decomposition`` () =
        // RYY(θ) = (RX(π/2)⊗RX(π/2))·RZZ(θ)·(RX(-π/2)⊗RX(-π/2))
        let theta = -0.81
        let state = testState2q ()

        let native = Gates.applyRyy 0 1 theta state

        let decomposed =
            state
            |> Gates.applyRx 0 (-Math.PI / 2.0)
            |> Gates.applyRx 1 (-Math.PI / 2.0)
            |> Gates.applyRzz 0 1 theta
            |> Gates.applyRx 0 (Math.PI / 2.0)
            |> Gates.applyRx 1 (Math.PI / 2.0)

        assertStatesEqual decomposed native

    [<Fact>]
    let ``Ising gates with negated angle are inverses`` () =
        let theta = 0.456
        let state = testState2q ()

        let roundtripXX = state |> Gates.applyRxx 0 1 theta |> Gates.applyRxx 0 1 -theta
        let roundtripYY = state |> Gates.applyRyy 0 1 theta |> Gates.applyRyy 0 1 -theta
        let roundtripZZ = state |> Gates.applyRzz 0 1 theta |> Gates.applyRzz 0 1 -theta

        assertStatesEqual state roundtripXX
        assertStatesEqual state roundtripYY
        assertStatesEqual state roundtripZZ

    [<Fact>]
    let ``Ising gates reject same-qubit application`` () =
        let state = StateVector.init 2

        Assert.Throws<Exception>(fun () -> Gates.applyRxx 0 0 1.0 state |> ignore)
        |> ignore

        Assert.Throws<Exception>(fun () -> Gates.applyRyy 1 1 1.0 state |> ignore)
        |> ignore

        Assert.Throws<Exception>(fun () -> Gates.applyRzz 0 0 1.0 state |> ignore)
        |> ignore

    // ========================================================================
    // KERNEL DIFFERENTIAL TESTS
    // ========================================================================
    //
    // The single-qubit kernel walks index PAIRS and reads the amplitude array
    // directly, rather than walking every index and re-reading its partner through
    // the bounds-checked accessor. That is a performance rewrite of arithmetic that
    // must not change, so it is pinned against the obvious formulation below rather
    // than against hand-written expected values.

    /// The straightforward formulation: for every index, look up its partner.
    /// Deliberately naive — it is the oracle, not the implementation.
    let private referenceSingleQubit
        (q: int)
        (a: Complex, b: Complex, c: Complex, d: Complex)
        (src: Complex[])
        : Complex[] =
        Array.init src.Length (fun i ->
            let bit = 1 <<< q

            if (i &&& bit) <> 0 then
                c * src.[i ^^^ bit] + d * src.[i]
            else
                a * src.[i] + b * src.[i ||| bit])

    let private randomAmplitudes (rng: Random) (numQubits: int) : Complex[] =
        let amps =
            Array.init (1 <<< numQubits) (fun _ -> Complex(rng.NextDouble() * 2.0 - 1.0, rng.NextDouble() * 2.0 - 1.0))

        let norm = sqrt (amps |> Array.sumBy (fun z -> z.Magnitude * z.Magnitude))
        amps |> Array.map (fun z -> z / Complex(norm, 0.0))

    [<Fact>]
    let ``single-qubit kernel matches the reference formulation exactly`` () =
        let rng = Random(20260922) // fixed seed: a failure must be reproducible
        let sqrtHalf = 1.0 / sqrt 2.0

        let gates
            : (string *
              (int -> StateVector.StateVector -> StateVector.StateVector) *
              (Complex * Complex * Complex * Complex)) list =
            [
                "H",
                Gates.applyH,
                (Complex(sqrtHalf, 0.0), Complex(sqrtHalf, 0.0), Complex(sqrtHalf, 0.0), Complex(-sqrtHalf, 0.0))
                "X", Gates.applyX, (Complex.Zero, Complex.One, Complex.One, Complex.Zero)
                "Y", Gates.applyY, (Complex.Zero, Complex(0.0, -1.0), Complex(0.0, 1.0), Complex.Zero)
                "Z", Gates.applyZ, (Complex.One, Complex.Zero, Complex.Zero, -Complex.One)
                "S", Gates.applyS, (Complex.One, Complex.Zero, Complex.Zero, Complex(0.0, 1.0))
                "SDG", Gates.applySDG, (Complex.One, Complex.Zero, Complex.Zero, Complex(0.0, -1.0))
                "T",
                Gates.applyT,
                (Complex.One, Complex.Zero, Complex.Zero, Complex(cos (Math.PI / 4.0), sin (Math.PI / 4.0)))
                "TDG",
                Gates.applyTDG,
                (Complex.One, Complex.Zero, Complex.Zero, Complex(cos (-Math.PI / 4.0), sin (-Math.PI / 4.0)))
            ]

        for numQubits in [ 1; 2; 3; 5; 8 ] do
            for q in 0 .. numQubits - 1 do
                let amplitudes = randomAmplitudes rng numQubits
                let state = StateVector.create amplitudes

                for (name, gate, matrix) in gates do
                    let actual = gate q state
                    let expected = referenceSingleQubit q matrix amplitudes

                    for i in 0 .. amplitudes.Length - 1 do
                        let difference = Complex.Abs(StateVector.getAmplitude i actual - expected.[i])

                        Assert.True(
                            difference < 1e-12,
                            $"{name} on qubit {q} of {numQubits}: amplitude {i} differs by {difference}"
                        )

    [<Fact>]
    let ``rotation kernels match the reference formulation across angles`` () =
        let rng = Random(20260923)

        for numQubits in [ 1; 3; 6 ] do
            for q in 0 .. numQubits - 1 do
                let amplitudes = randomAmplitudes rng numQubits
                let state = StateVector.create amplitudes

                for theta in [ 0.0; 0.3; Math.PI / 2.0; Math.PI; 2.3; -1.1 ] do
                    let half = theta / 2.0

                    let cases =
                        [
                            "RX",
                            Gates.applyRx q theta state,
                            (Complex(cos half, 0.0),
                             Complex(0.0, -sin half),
                             Complex(0.0, -sin half),
                             Complex(cos half, 0.0))
                            "RY",
                            Gates.applyRy q theta state,
                            (Complex(cos half, 0.0),
                             Complex(-sin half, 0.0),
                             Complex(sin half, 0.0),
                             Complex(cos half, 0.0))
                            "P",
                            Gates.applyP q theta state,
                            (Complex.One, Complex.Zero, Complex.Zero, Complex(cos theta, sin theta))
                        ]

                    for (name, actual, matrix) in cases do
                        let expected = referenceSingleQubit q matrix amplitudes

                        for i in 0 .. amplitudes.Length - 1 do
                            let difference = Complex.Abs(StateVector.getAmplitude i actual - expected.[i])

                            Assert.True(
                                difference < 1e-12,
                                $"{name}({theta}) on qubit {q}: amplitude {i} differs by {difference}"
                            )

    [<Fact>]
    let ``applying a gate never mutates the state it was given`` () =
        // The kernel reads the amplitude array directly, so this is the property that
        // stops it from aliasing. Primitives.expectation depends on it: it reads the
        // source state again after deriving another state from it.
        let rng = Random(20260924)
        let amplitudes = randomAmplitudes rng 6
        let state = StateVector.create amplitudes

        let derived =
            state
            |> Gates.applyH 0
            |> Gates.applyRx 3 1.1
            |> Gates.applyT 5
            |> Gates.applyX 2

        for i in 0 .. amplitudes.Length - 1 do
            Assert.Equal(amplitudes.[i].Real, (StateVector.getAmplitude i state).Real, 12)
            Assert.Equal(amplitudes.[i].Imaginary, (StateVector.getAmplitude i state).Imaginary, 12)

        // ...and the derived state really is different, so the check above is not vacuous.
        let anyDifference =
            [ 0 .. amplitudes.Length - 1 ]
            |> List.exists (fun i -> Complex.Abs(StateVector.getAmplitude i derived - amplitudes.[i]) > 1e-9)

        Assert.True(anyDifference, "derived state should differ from the source")

    // ========================================================================
    // TWO- AND THREE-QUBIT KERNEL DIFFERENTIAL TESTS
    // ========================================================================
    //
    // Same discipline as the single-qubit oracle above: the controlled kernels are
    // pinned against the obvious formulation rather than against hand-computed
    // amplitudes, so a performance rewrite of them cannot quietly change the maths.

    /// Naive controlled gate: for every index, decide from its own bits and look up
    /// the partner. Deliberately the slow, transparent version — it is the oracle.
    let private referenceControlled
        (controlMask: int)
        (targetIndex: int)
        (a: Complex, b: Complex, c: Complex, d: Complex)
        (src: Complex[])
        : Complex[] =
        let bit = 1 <<< targetIndex

        Array.init src.Length (fun i ->
            if i &&& controlMask <> controlMask then src.[i] // a control is |0⟩: untouched
            elif i &&& bit <> 0 then c * src.[i ^^^ bit] + d * src.[i]
            else a * src.[i] + b * src.[i ||| bit])

    /// Naive SWAP: exchange amplitudes whose two qubits disagree.
    let private referenceSwap (q1: int) (q2: int) (src: Complex[]) : Complex[] =
        let mask1 = 1 <<< q1
        let mask2 = 1 <<< q2

        Array.init src.Length (fun i ->
            let bit1 = i &&& mask1 <> 0
            let bit2 = i &&& mask2 <> 0
            if bit1 = bit2 then src.[i] else src.[i ^^^ mask1 ^^^ mask2])

    let private assertMatches (label: string) (expected: Complex[]) (actual: StateVector.StateVector) =
        for i in 0 .. expected.Length - 1 do
            let difference = Complex.Abs(StateVector.getAmplitude i actual - expected.[i])
            Assert.True(difference < 1e-12, $"{label}: amplitude {i} differs by {difference}")

    [<Fact>]
    let ``controlled kernels match the reference formulation`` () =
        let rng = Random(20260926)
        let numQubits = 5

        for control in 0 .. numQubits - 1 do
            for target in 0 .. numQubits - 1 do
                if control <> target then
                    let amplitudes = randomAmplitudes rng numQubits
                    let state = StateVector.create amplitudes
                    let mask = 1 <<< control
                    let angle = rng.NextDouble() * 4.0 - 2.0

                    let cases =
                        [
                            "CNOT", Gates.applyCNOT control target state, Gates.Matrices.x
                            "CZ", Gates.applyCZ control target state, Gates.Matrices.z
                            "CP", Gates.applyCP control target angle state, Gates.Matrices.p angle
                            "CRX", Gates.applyCRX control target angle state, Gates.Matrices.rx angle
                            "CRY", Gates.applyCRY control target angle state, Gates.Matrices.ry angle
                            "CRZ", Gates.applyCRZ control target angle state, Gates.Matrices.rz angle
                        ]

                    for (name, actual, matrix) in cases do
                        assertMatches
                            $"{name}(c={control}, t={target})"
                            (referenceControlled mask target matrix amplitudes)
                            actual

    [<Fact>]
    let ``Toffoli and multi-controlled Z match the reference formulation`` () =
        let rng = Random(20260927)
        let numQubits = 5

        for control1 in 0 .. numQubits - 1 do
            for control2 in 0 .. numQubits - 1 do
                for target in 0 .. numQubits - 1 do
                    if control1 <> control2 && control1 <> target && control2 <> target then
                        let amplitudes = randomAmplitudes rng numQubits
                        let state = StateVector.create amplitudes
                        let mask = (1 <<< control1) ||| (1 <<< control2)

                        assertMatches
                            $"CCX({control1},{control2}->{target})"
                            (referenceControlled mask target Gates.Matrices.x amplitudes)
                            (Gates.applyCCX control1 control2 target state)

                        assertMatches
                            $"MCZ([{control1};{control2}]->{target})"
                            (referenceControlled mask target Gates.Matrices.z amplitudes)
                            (Gates.applyMultiControlledZ [ control1; control2 ] target state)

    [<Fact>]
    let ``SWAP matches the reference formulation`` () =
        let rng = Random(20260928)
        let numQubits = 5

        for q1 in 0 .. numQubits - 1 do
            for q2 in 0 .. numQubits - 1 do
                if q1 <> q2 then
                    let amplitudes = randomAmplitudes rng numQubits
                    let state = StateVector.create amplitudes

                    assertMatches $"SWAP({q1},{q2})" (referenceSwap q1 q2 amplitudes) (Gates.applySWAP q1 q2 state)

    [<Fact>]
    let ``controlled gates leave the state they were given untouched`` () =
        // These kernels read the amplitude array directly, so this is the property
        // that stops them aliasing their source.
        let rng = Random(20260929)
        let amplitudes = randomAmplitudes rng 5
        let state = StateVector.create amplitudes

        let derived =
            state
            |> Gates.applyCNOT 0 1
            |> Gates.applyCZ 1 2
            |> Gates.applyCRY 2 3 0.7
            |> Gates.applyCCX 0 1 4
            |> Gates.applySWAP 3 4
            |> Gates.applyMultiControlledZ [ 0; 1; 2 ] 3

        for i in 0 .. amplitudes.Length - 1 do
            Assert.Equal(amplitudes.[i].Real, (StateVector.getAmplitude i state).Real, 12)
            Assert.Equal(amplitudes.[i].Imaginary, (StateVector.getAmplitude i state).Imaginary, 12)

        let anyDifference =
            [ 0 .. amplitudes.Length - 1 ]
            |> List.exists (fun i -> Complex.Abs(StateVector.getAmplitude i derived - amplitudes.[i]) > 1e-9)

        Assert.True(anyDifference, "derived state should differ from the source")
