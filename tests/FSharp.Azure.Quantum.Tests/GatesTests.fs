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
        let state2q = StateVector.init 2  // |00⟩
        let stateFlipped = Gates.applyX 1 state2q  // Flip qubit 1 (middle bit)
        Assert.Equal(0.0, (StateVector.getAmplitude 0 stateFlipped).Real, 10)  // |00⟩
        Assert.Equal(0.0, (StateVector.getAmplitude 1 stateFlipped).Real, 10)  // |01⟩
        Assert.Equal(1.0, (StateVector.getAmplitude 2 stateFlipped).Real, 10)  // |10⟩
        Assert.Equal(0.0, (StateVector.getAmplitude 3 stateFlipped).Real, 10)  // |11⟩
    
    [<Fact>]
    let ``Pauli-Y gate - should apply Y rotation correctly`` () =
        // Test Y gate on |0⟩ → i|1⟩
        let state0 = StateVector.init 1
        let state1 = Gates.applyY 0 state0
        
        Assert.Equal(0.0, (StateVector.getAmplitude 0 state1).Real, 10)
        Assert.Equal(0.0, (StateVector.getAmplitude 0 state1).Imaginary, 10)
        Assert.Equal(0.0, (StateVector.getAmplitude 1 state1).Real, 10)
        Assert.Equal(1.0, (StateVector.getAmplitude 1 state1).Imaginary, 10)  // i
        
        // Test Y gate on |1⟩ → -i|0⟩
        let state1Input = StateVector.create [| Complex.Zero; Complex.One |]
        let state0Output = Gates.applyY 0 state1Input
        
        Assert.Equal(0.0, (StateVector.getAmplitude 0 state0Output).Real, 10)
        Assert.Equal(-1.0, (StateVector.getAmplitude 0 state0Output).Imaginary, 10)  // -i
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
        let superposition = StateVector.create [| Complex(sqrtHalf, 0.0); Complex(sqrtHalf, 0.0) |]
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
        Assert.Equal(-1.0, (StateVector.getAmplitude 1 stateAfterRx).Imaginary, 10)  // -i
        
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
        let superposition = StateVector.create [| Complex(sqrtHalf, 0.0); Complex(sqrtHalf, 0.0) |]
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
        let superposition = StateVector.create [| Complex(sqrtHalf, 0.0); Complex(sqrtHalf, 0.0) |]
        
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
        let state3q = StateVector.init 3  // |000⟩
        
        // Apply X to qubit 0 (rightmost): |000⟩ → |001⟩
        let stateXq0 = Gates.applyX 0 state3q
        Assert.Equal(0.0, (StateVector.getAmplitude 0 stateXq0).Real, 10)  // |000⟩
        Assert.Equal(1.0, (StateVector.getAmplitude 1 stateXq0).Real, 10)  // |001⟩
        
        // Apply X to qubit 1 (middle): |000⟩ → |010⟩
        let stateXq1 = Gates.applyX 1 state3q
        Assert.Equal(0.0, (StateVector.getAmplitude 0 stateXq1).Real, 10)  // |000⟩
        Assert.Equal(0.0, (StateVector.getAmplitude 1 stateXq1).Real, 10)  // |001⟩
        Assert.Equal(1.0, (StateVector.getAmplitude 2 stateXq1).Real, 10)  // |010⟩
        
        // Apply X to qubit 2 (leftmost): |000⟩ → |100⟩
        let stateXq2 = Gates.applyX 2 state3q
        Assert.Equal(0.0, (StateVector.getAmplitude 0 stateXq2).Real, 10)  // |000⟩
        Assert.Equal(1.0, (StateVector.getAmplitude 4 stateXq2).Real, 10)  // |100⟩
    
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
        let _ = Gates.applyX 0 state2q
        let _ = Gates.applyX 1 state2q
        
        // Invalid indices: -1, 2
        Assert.Throws<System.Exception>(fun () -> 
            Gates.applyX -1 state2q |> ignore
        )
        Assert.Throws<System.Exception>(fun () -> 
            Gates.applyX 2 state2q |> ignore
        )
    
    [<Fact>]
    let ``Complex rotation composition - should produce correct final state`` () =
        // Test realistic QAOA-like sequence: H → Rz(θ) → Rx(φ)
        let state0 = StateVector.init 1
        let theta = 0.5
        let phi = 1.2
        
        let finalState = 
            state0
            |> Gates.applyH 0
            |> Gates.applyRz 0 theta
            |> Gates.applyRx 0 phi
        
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
        Assert.Equal(1.0, (StateVector.getAmplitude 0 result00).Real, 10)  // |00⟩
        Assert.Equal(0.0, (StateVector.getAmplitude 1 result00).Real, 10)  // |01⟩
        Assert.Equal(0.0, (StateVector.getAmplitude 2 result00).Real, 10)  // |10⟩
        Assert.Equal(0.0, (StateVector.getAmplitude 3 result00).Real, 10)  // |11⟩
        
        // Test CNOT on |01⟩ → |11⟩ (control=qubit_0=1, flip target)
        let state01 = StateVector.create [| Complex.Zero; Complex.One; Complex.Zero; Complex.Zero |]
        let result01 = Gates.applyCNOT 0 1 state01
        Assert.Equal(0.0, (StateVector.getAmplitude 0 result01).Real, 10)  // |00⟩
        Assert.Equal(0.0, (StateVector.getAmplitude 1 result01).Real, 10)  // |01⟩
        Assert.Equal(0.0, (StateVector.getAmplitude 2 result01).Real, 10)  // |10⟩
        Assert.Equal(1.0, (StateVector.getAmplitude 3 result01).Real, 10)  // |11⟩
        
        // Test CNOT on |10⟩ → |10⟩ (control=qubit_0=0, no flip)
        let state10 = StateVector.create [| Complex.Zero; Complex.Zero; Complex.One; Complex.Zero |]
        let result10 = Gates.applyCNOT 0 1 state10
        Assert.Equal(0.0, (StateVector.getAmplitude 0 result10).Real, 10)  // |00⟩
        Assert.Equal(0.0, (StateVector.getAmplitude 1 result10).Real, 10)  // |01⟩
        Assert.Equal(1.0, (StateVector.getAmplitude 2 result10).Real, 10)  // |10⟩ (unchanged)
        Assert.Equal(0.0, (StateVector.getAmplitude 3 result10).Real, 10)  // |11⟩
        
        // Test CNOT on |11⟩ → |01⟩ (control=qubit_0=1, flip target)
        let state11 = StateVector.create [| Complex.Zero; Complex.Zero; Complex.Zero; Complex.One |]
        let result11 = Gates.applyCNOT 0 1 state11
        Assert.Equal(0.0, (StateVector.getAmplitude 0 result11).Real, 10)  // |00⟩
        Assert.Equal(1.0, (StateVector.getAmplitude 1 result11).Real, 10)  // |01⟩
        Assert.Equal(0.0, (StateVector.getAmplitude 2 result11).Real, 10)  // |10⟩
        Assert.Equal(0.0, (StateVector.getAmplitude 3 result11).Real, 10)  // |11⟩
    
    [<Fact>]
    let ``CNOT gate - should work with different qubit orderings`` () =
        // Test CNOT with control=1, target=0
        // State |01⟩: qubit_0=1, qubit_1=0
        // CNOT(1,0): control=qubit_1=0, target=qubit_0=1
        // Since control=0, no flip → |01⟩
        let state01a = StateVector.create [| Complex.Zero; Complex.One; Complex.Zero; Complex.Zero |]
        let result01a = Gates.applyCNOT 1 0 state01a
        Assert.Equal(0.0, (StateVector.getAmplitude 0 result01a).Real, 10)  // |00⟩
        Assert.Equal(1.0, (StateVector.getAmplitude 1 result01a).Real, 10)  // |01⟩ (unchanged)
        Assert.Equal(0.0, (StateVector.getAmplitude 2 result01a).Real, 10)  // |10⟩
        Assert.Equal(0.0, (StateVector.getAmplitude 3 result01a).Real, 10)  // |11⟩
        
        // State |10⟩: qubit_0=0, qubit_1=1
        // CNOT(1,0): control=qubit_1=1, target=qubit_0=0
        // Since control=1, flip target → |11⟩
        let state10 = StateVector.create [| Complex.Zero; Complex.Zero; Complex.One; Complex.Zero |]
        let result10 = Gates.applyCNOT 1 0 state10
        Assert.Equal(0.0, (StateVector.getAmplitude 0 result10).Real, 10)  // |00⟩
        Assert.Equal(0.0, (StateVector.getAmplitude 1 result10).Real, 10)  // |01⟩
        Assert.Equal(0.0, (StateVector.getAmplitude 2 result10).Real, 10)  // |10⟩
        Assert.Equal(1.0, (StateVector.getAmplitude 3 result10).Real, 10)  // |11⟩
    
    [<Fact>]
    let ``CNOT gate - should create entanglement from superposition`` () =
        // Create Bell state: H on qubit 0, then CNOT(0,1) creates (|00⟩+|11⟩)/√2
        let state00 = StateVector.init 2
        let stateSuperpos = Gates.applyH 0 state00  // (|00⟩+|10⟩)/√2
        let bellState = Gates.applyCNOT 0 1 stateSuperpos
        
        let sqrtHalf = 1.0 / sqrt 2.0
        Assert.Equal(sqrtHalf, (StateVector.getAmplitude 0 bellState).Real, 10)  // |00⟩
        Assert.Equal(0.0, (StateVector.getAmplitude 1 bellState).Real, 10)       // |01⟩
        Assert.Equal(0.0, (StateVector.getAmplitude 2 bellState).Real, 10)       // |10⟩
        Assert.Equal(sqrtHalf, (StateVector.getAmplitude 3 bellState).Real, 10)  // |11⟩
    
    [<Fact>]
    let ``CNOT gate - should be self-inverse`` () =
        // CNOT applied twice returns to original state
        let state = StateVector.create [| Complex(0.6, 0.0); Complex(0.0, 0.0); Complex(0.8, 0.0); Complex(0.0, 0.0) |]
        let afterCNOT = Gates.applyCNOT 0 1 state
        let afterTwoCNOT = Gates.applyCNOT 0 1 afterCNOT
        
        Assert.True(StateVector.equals state afterTwoCNOT)
    
    [<Fact>]
    let ``CNOT gate - should preserve norm`` () =
        // Test norm preservation on various states
        let state1 = StateVector.init 2
        Assert.Equal(1.0, StateVector.norm (Gates.applyCNOT 0 1 state1), 10)
        
        let sqrtHalf = 1.0 / sqrt 2.0
        let state2 = StateVector.create [| Complex(sqrtHalf, 0.0); Complex(sqrtHalf, 0.0); Complex.Zero; Complex.Zero |]
        Assert.Equal(1.0, StateVector.norm (Gates.applyCNOT 0 1 state2), 10)
        
        let state3 = StateVector.create [| Complex(0.6, 0.0); Complex(0.0, 0.0); Complex(0.8, 0.0); Complex(0.0, 0.0) |]
        Assert.Equal(1.0, StateVector.norm (Gates.applyCNOT 0 1 state3), 10)
    
    [<Fact>]
    let ``CZ gate - should add phase when both qubits are 1`` () =
        // Test CZ on |00⟩ → |00⟩ (no change)
        let state00 = StateVector.init 2
        let result00 = Gates.applyCZ 0 1 state00
        Assert.True(StateVector.equals state00 result00)
        
        // Test CZ on |01⟩ → |01⟩ (no change)
        let state01 = StateVector.create [| Complex.Zero; Complex.One; Complex.Zero; Complex.Zero |]
        let result01 = Gates.applyCZ 0 1 state01
        Assert.True(StateVector.equals state01 result01)
        
        // Test CZ on |10⟩ → |10⟩ (no change)
        let state10 = StateVector.create [| Complex.Zero; Complex.Zero; Complex.One; Complex.Zero |]
        let result10 = Gates.applyCZ 0 1 state10
        Assert.True(StateVector.equals state10 result10)
        
        // Test CZ on |11⟩ → -|11⟩ (phase flip)
        let state11 = StateVector.create [| Complex.Zero; Complex.Zero; Complex.Zero; Complex.One |]
        let result11 = Gates.applyCZ 0 1 state11
        Assert.Equal(0.0, (StateVector.getAmplitude 0 result11).Real, 10)
        Assert.Equal(0.0, (StateVector.getAmplitude 1 result11).Real, 10)
        Assert.Equal(0.0, (StateVector.getAmplitude 2 result11).Real, 10)
        Assert.Equal(-1.0, (StateVector.getAmplitude 3 result11).Real, 10)
    
    [<Fact>]
    let ``CZ gate - should be symmetric`` () =
        // CZ(0,1) should equal CZ(1,0) - control and target are interchangeable
        let state = StateVector.create [| Complex(0.5, 0.0); Complex(0.5, 0.0); Complex(0.5, 0.0); Complex(0.5, 0.0) |]
        let resultCZ01 = Gates.applyCZ 0 1 state
        let resultCZ10 = Gates.applyCZ 1 0 state
        
        Assert.True(StateVector.equals resultCZ01 resultCZ10)
    
    [<Fact>]
    let ``CZ gate - should be self-inverse`` () =
        // CZ applied twice returns to original (Z² = I)
        let state = StateVector.create [| Complex(0.5, 0.0); Complex(0.5, 0.0); Complex(0.5, 0.0); Complex(0.5, 0.0) |]
        let afterCZ = Gates.applyCZ 0 1 state
        let afterTwoCZ = Gates.applyCZ 0 1 afterCZ
        
        Assert.True(StateVector.equals state afterTwoCZ)
    
    [<Fact>]
    let ``CZ gate - should preserve norm`` () =
        let state1 = StateVector.init 2
        Assert.Equal(1.0, StateVector.norm (Gates.applyCZ 0 1 state1), 10)
        
        let state2 = StateVector.create [| Complex(0.5, 0.0); Complex(0.5, 0.0); Complex(0.5, 0.0); Complex(0.5, 0.0) |]
        Assert.Equal(1.0, StateVector.norm (Gates.applyCZ 0 1 state2), 10)
    
    [<Fact>]
    let ``Two-qubit gates - should reject invalid indices`` () =
        let state2q = StateVector.init 2
        
        // Valid indices: 0, 1
        let _ = Gates.applyCNOT 0 1 state2q
        let _ = Gates.applyCZ 0 1 state2q
        
        // Invalid: same control and target
        Assert.Throws<System.Exception>(fun () -> 
            Gates.applyCNOT 0 0 state2q |> ignore
        )
        Assert.Throws<System.Exception>(fun () -> 
            Gates.applyCZ 1 1 state2q |> ignore
        )
        
        // Invalid: out of range
        Assert.Throws<System.Exception>(fun () -> 
            Gates.applyCNOT 2 0 state2q |> ignore
        )
        Assert.Throws<System.Exception>(fun () -> 
            Gates.applyCZ 0 3 state2q |> ignore
        )
