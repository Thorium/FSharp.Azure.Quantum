namespace FSharp.Azure.Quantum.Tests

(*
    ✅ UNCOMMENTED: QuantumArithmetic is now compiled!
    
    The QuantumArithmetic module is critical for Shor's algorithm and quantum arithmetic operations.
    It was temporarily commented out due to dependency on removed adapters.
    
    Fixed by:
    - Removing QFTBackendAdapter dependency
    - Adding inline qftToCircuit helper function
    - Keeping all original tests to ensure arithmetic operations work correctly
*)

open Xunit
open FSharp.Azure.Quantum.Algorithms
open FSharp.Azure.Quantum.Algorithms.QuantumArithmetic
open FSharp.Azure.Quantum.LocalSimulator
open FSharp.Azure.Quantum.CircuitBuilder
open System.Numerics

module QuantumArithmeticTests =
    
    // ========================================================================
    // HELPER FUNCTIONS
    // ========================================================================
    
    /// Measure probability that a specific qubit is in |0⟩ state
    let private measureQubitProbability (qubitIndex: int) (state: StateVector.StateVector) : float =
        let dim = StateVector.dimension state
        
        // Sum probabilities of all basis states where qubit is 0 (functional style)
        seq { 0 .. dim - 1 }
        |> Seq.filter (fun i -> (i >>> qubitIndex) &&& 1 = 0)
        |> Seq.sumBy (fun i ->
            let amp = StateVector.getAmplitude i state
            amp.Magnitude * amp.Magnitude)
    
    /// Execute circuit by applying gates to state vector (with realistic measurement)
    let private executeCircuit (circuit: Circuit) : StateVector.StateVector =
        let numQubits = qubitCount circuit
        let initialState = StateVector.init numQubits
        let gates = getGates circuit
        let rng = System.Random(42)  // Fixed seed for reproducibility
        
        /// Apply a single gate to a state vector
        let applyGate (state: StateVector.StateVector) (gate: Gate) : StateVector.StateVector =
            match gate with
            | X q -> Gates.applyX q state
            | Y q -> Gates.applyY q state
            | Z q -> Gates.applyZ q state
            | H q -> Gates.applyH q state
            | S q -> Gates.applyS q state
            | SDG q -> Gates.applySDG q state
            | T q -> Gates.applyT q state
            | TDG q -> Gates.applyTDG q state
            | P (q, angle) -> Gates.applyP q angle state
            | CP (c, t, angle) -> Gates.applyCPhase c t angle state
            | CRX (c, t, angle) -> Gates.applyCRX c t angle state
            | CRY (c, t, angle) -> Gates.applyCRY c t angle state
            | CRZ (c, t, angle) -> Gates.applyCRZ c t angle state
            | RX (q, angle) -> Gates.applyRx q angle state
            | RY (q, angle) -> Gates.applyRy q angle state
            | RZ (q, angle) -> Gates.applyRz q angle state
            | U3 (q, theta, phi, lambda) ->
                // U3(θ,φ,λ) = RZ(φ) RY(θ) RZ(λ)
                state
                |> Gates.applyRz q lambda
                |> Gates.applyRy q theta
                |> Gates.applyRz q phi
            | CNOT (c, t) -> Gates.applyCNOT c t state
            | CZ (c, t) -> Gates.applyCZ c t state
            | MCZ (controls, t) -> Gates.applyMultiControlledZ controls t state
            | SWAP (q1, q2) -> Gates.applySWAP q1 q2 state
            | CCX (c1, c2, t) -> Gates.applyCCX c1 c2 t state
            | Measure q -> 
                // Perform realistic measurement with state collapse
                let outcome = Measurement.measureSingleQubit rng q state
                Measurement.collapseAfterMeasurement q outcome state
            | Reset q ->
                // Reset to |0⟩: measure, if 1 apply X to flip back
                let outcome = Measurement.measureSingleQubit rng q state
                let collapsed = Measurement.collapseAfterMeasurement q outcome state
                if outcome = 1 then Gates.applyX q collapsed else collapsed
            | Barrier _ -> state
        
        // Apply all gates using functional fold
        gates |> List.fold applyGate initialState
    
    // ========================================================================
    // DOUBLY-CONTROLLED OPERATION TESTS (Ancilla Restoration)
    // ========================================================================
    
    [<Fact>]
    let ``Doubly controlled addition restores ancilla to |0⟩`` () =
        // *** CRITICAL TEST: Verify ancilla is properly uncomputed ***
        // This test ensures quantum reversibility - fundamental requirement
        
        let control1 = 0
        let control2 = 1
        let registerQubits = [2; 3; 4; 5]
        let ancillaQubit = 6
        let constant = 3
        let numQubits = 7  // 2 controls + 4 register + 1 ancilla
        
        let circuit =
            empty numQubits
            |> addGate (X control1)               // Set both controls to |1⟩
            |> addGate (X control2)
            |> addGate (X registerQubits.[0])     // Set register to |5⟩ = |0101⟩
            |> addGate (X registerQubits.[2])
            |> doublyControlledAddConstant control1 control2 registerQubits constant ancillaQubit
        
        let finalState = executeCircuit circuit
        
        // ✅ CRITICAL CHECK: Ancilla should be |0⟩ (properly restored)
        let ancillaProb0 = measureQubitProbability ancillaQubit finalState
        Assert.True(ancillaProb0 > 0.999, 
            $"Ancilla should be restored to |0⟩, but P(0) = {ancillaProb0}")
    
    [<Fact>]
    let ``Doubly controlled subtraction restores ancilla to |0⟩`` () =
        // *** CRITICAL TEST: Verify ancilla restoration for subtraction ***
        
        let control1 = 0
        let control2 = 1
        let registerQubits = [2; 3; 4; 5]
        let ancillaQubit = 6
        let constant = 3
        let numQubits = 7
        
        let circuit =
            empty numQubits
            |> addGate (X control1)               // Set both controls to |1⟩
            |> addGate (X control2)
            |> addGate (X registerQubits.[3])     // Set register to |8⟩ = |1000⟩
            |> doublyControlledSubtractConstant control1 control2 registerQubits constant ancillaQubit
        
        let finalState = executeCircuit circuit
        
        // ✅ CRITICAL CHECK: Ancilla should be |0⟩
        let ancillaProb0 = measureQubitProbability ancillaQubit finalState
        Assert.True(ancillaProb0 > 0.999, 
            $"Ancilla should be restored to |0⟩ after subtraction, P(0) = {ancillaProb0}")
    
    [<Fact>]
    let ``Doubly controlled operations only apply when both controls are |1⟩`` () =
        // Test that operation doesn't apply if only one control is |1⟩
        
        let control1 = 0
        let control2 = 1
        let registerQubits = [2; 3; 4; 5]
        let ancillaQubit = 6
        let constant = 3
        let numQubits = 7
        
        let circuit =
            empty numQubits
            |> addGate (X control1)               // Only control1 is |1⟩, control2 is |0⟩
            // Don't set control2 to |1⟩
            |> addGate (X registerQubits.[0])     // Set register to |5⟩
            |> addGate (X registerQubits.[2])
            |> doublyControlledAddConstant control1 control2 registerQubits constant ancillaQubit
        
        let finalState = executeCircuit circuit
        
        // Register should be unchanged (still |5⟩) since control2 is |0⟩
        // Check that we have significant amplitude with register=5 (functional style)
        let dim = StateVector.dimension finalState
        let hasUnchangedRegister =
            seq { 0 .. dim - 1 }
            |> Seq.exists (fun i ->
                let amp = StateVector.getAmplitude i finalState
                let registerValue = (i >>> 2) &&& 0xF  // Extract register bits
                amp.Magnitude > 0.999 && registerValue = 5)
        
        Assert.True(hasUnchangedRegister, 
            "Register should be unchanged when only one control is |1⟩")
    
    // ========================================================================
    // IN-PLACE MODULAR MULTIPLICATION TESTS (Uncomputation Correctness)
    // ========================================================================
    
    [<Fact>]
    let ``In-place modular multiplication restores temp qubits to |0⟩`` () =
        // Uncomputation is exact because the multiply uses true modular adders
        // (Beauregard 2003): the forward pass accumulates mod N, so after the
        // controlled SWAP, subtracting (a⁻¹ · 2^k mod N) mod N for each set bit
        // of the register (= ay mod N) returns the temp register exactly to |0⟩.
        //
        // The historical failure here was caused by accumulating mod-N-reduced
        // addends with a plain mod-2^n adder: mod-N and mod-2^n arithmetic don't
        // commute, so the uncomputation didn't cancel and temps ended up dirty.

        let controlQubit = 0
        let registerQubits = [1; 2; 3; 4]     // Input/output register
        let tempQubits = [5; 6; 7; 8]         // Temporary register (restored to |0⟩)
        let ancillaQubit = 9
        let constant = 7                       // Multiply by 7
        let modulus = 15                       // mod 15
        let numQubits = 12                     // + overflow (10) and flag (11) ancillas

        let circuit =
            empty numQubits
            |> addGate (X controlQubit)        // Set control to |1⟩
            |> addGate (X registerQubits.[0])  // Set register to |3⟩ = |0011⟩
            |> addGate (X registerQubits.[1])
            |> controlledMultiplyConstantModNInPlace
                controlQubit registerQubits constant modulus tempQubits ancillaQubit

        let finalState = executeCircuit circuit

        // Temp qubits must be fully restored to |0⟩
        for i, tempQubit in List.indexed tempQubits do
            let prob0 = measureQubitProbability tempQubit finalState
            Assert.True(prob0 > 0.95, 
                $"Temp qubit {i} (qubit #{tempQubit}) should be |0⟩, but P(0) = {prob0}")
        
        // ✅ Ancilla IS properly restored
        let ancillaProb0 = measureQubitProbability ancillaQubit finalState
        Assert.True(ancillaProb0 > 0.95, 
            $"Ancilla should be |0⟩, but P(0) = {ancillaProb0}")
    
    [<Fact>]
    let ``In-place modular multiplication with control=|0⟩ leaves state unchanged`` () =
        // Test that operation doesn't apply when control is |0⟩
        
        let controlQubit = 0
        let registerQubits = [1; 2; 3; 4]
        let tempQubits = [5; 6; 7; 8]
        let ancillaQubit = 9
        let constant = 7
        let modulus = 15
        let numQubits = 12                     // + overflow (10) and flag (11) ancillas

        let circuit =
            empty numQubits
            // Control stays |0⟩
            |> addGate (X registerQubits.[0])  // Set register to |5⟩ = |0101⟩
            |> addGate (X registerQubits.[2])
            |> controlledMultiplyConstantModNInPlace 
                controlQubit registerQubits constant modulus tempQubits ancillaQubit
        
        let finalState = executeCircuit circuit
        
        // Register should still be |5⟩, temps should be |0⟩ (functional style)
        let dim = StateVector.dimension finalState
        let hasOriginalState =
            seq { 0 .. dim - 1 }
            |> Seq.exists (fun i ->
                let amp = StateVector.getAmplitude i finalState
                let registerValue = (i >>> 1) &&& 0xF
                amp.Magnitude > 0.99 && registerValue = 5)
        
        Assert.True(hasOriginalState, "Register should be unchanged when control=|0⟩")
    
    [<Fact>]
    let ``In-place modular multiplication works for basic example`` () =
        // Checks both the multiplication result (2 × 7 mod 15 = 14) and that the
        // temp qubits are restored to |0⟩ (exact uncomputation via modular adders).

        let controlQubit = 0
        let registerQubits = [1; 2; 3; 4]
        let tempQubits = [5; 6; 7; 8]
        let ancillaQubit = 9
        let constant = 7
        let modulus = 15
        let numQubits = 12                     // + overflow (10) and flag (11) ancillas
        let inputValue = 2
        let expectedResult = 14  // 2 × 7 mod 15 = 14
        
        // Build circuit to set register to input value (functional style)
        let circuit =
            // Start with control |1⟩
            empty numQubits
            |> addGate (X controlQubit)
            // Set register bits based on input value
            |> fun c ->
                [0..3]
                |> List.fold (fun circuit i ->
                    if (inputValue >>> i) &&& 1 = 1 then
                        circuit |> addGate (X registerQubits.[i])
                    else
                        circuit
                ) c
            // Apply multiplication
            |> controlledMultiplyConstantModNInPlace 
                controlQubit registerQubits constant modulus tempQubits ancillaQubit
        
        let finalState = executeCircuit circuit
        
        // Temp qubits must be fully restored to |0⟩
        tempQubits
        |> List.iter (fun tempQubit ->
            let prob0 = measureQubitProbability tempQubit finalState
            Assert.True(prob0 > 0.90,
                $"Test {inputValue} × {constant} mod {modulus}: temp qubits not restored, P(0)={prob0}"))

        // Register must hold the product (functional style)
        let dim = StateVector.dimension finalState
        let hasCorrectResult =
            seq { 0 .. dim - 1 }
            |> Seq.exists (fun i ->
                let amp = StateVector.getAmplitude i finalState
                let registerValue = (i >>> 1) &&& 0xF
                amp.Magnitude > 0.1 && registerValue = expectedResult)
        
        Assert.True(hasCorrectResult, 
            $"Expected {inputValue} × {constant} mod {modulus} = {expectedResult}")
    
    // ========================================================================
    // REVERSIBILITY TESTS
    // ========================================================================
    
    [<Fact>]
    let ``Controlled add then subtract is identity`` () =
        // Test reversibility: add then subtract should return to original state
        
        let controlQubit = 0
        let registerQubits = [1; 2; 3; 4]
        let constant = 7
        let numQubits = 5
        
        let circuit =
            empty numQubits
            |> addGate (X controlQubit)           // Set control to |1⟩
            |> addGate (X registerQubits.[0])     // Set register to |5⟩ = |0101⟩
            |> addGate (X registerQubits.[2])
            |> controlledAddConstant controlQubit registerQubits constant      // Add 7
            |> controlledSubtractConstant controlQubit registerQubits constant  // Subtract 7
        
        let finalState = executeCircuit circuit
        
        // State should be back to |1⟩|5⟩ (functional style)
        let dim = StateVector.dimension finalState
        let hasOriginalState =
            seq { 0 .. dim - 1 }
            |> Seq.exists (fun i ->
                let amp = StateVector.getAmplitude i finalState
                let registerValue = (i >>> 1) &&& 0xF
                amp.Magnitude > 0.999 && registerValue = 5)
        
        Assert.True(hasOriginalState, "Add then subtract should be identity")
    
    // ========================================================================
    // U3 GATE TESTS (Universal Single-Qubit Rotation)
    // ========================================================================
    
    [<Fact>]
    let ``U3 gate applies correct universal rotation`` () =
        // Test U3(θ,φ,λ) gate implementation
        // U3(π, 0, π) should convert |0⟩ to |1⟩ (equivalent to X gate)
        
        let qubit = 0
        let numQubits = 1
        let theta = System.Math.PI
        let phi = 0.0
        let lambda = System.Math.PI
        
        let circuit =
            empty numQubits
            |> addGate (U3 (qubit, theta, phi, lambda))
        
        let finalState = executeCircuit circuit
        
        // Should be in |1⟩ state with high probability
        let prob0 = measureQubitProbability qubit finalState
        Assert.True(prob0 < 0.01, 
            $"U3(π, 0, π) should flip |0⟩ to |1⟩, but P(0) = {prob0}")
    
    [<Fact>]
    let ``U3 gate decomposes correctly via RZ-RY-RZ`` () =
        // Test that U3(θ,φ,λ) = RZ(φ) RY(θ) RZ(λ)
        // Compare U3 gate with explicit decomposition
        
        let qubit = 0
        let numQubits = 1
        let theta = System.Math.PI / 3.0   // 60 degrees
        let phi = System.Math.PI / 4.0     // 45 degrees
        let lambda = System.Math.PI / 6.0  // 30 degrees
        
        // Circuit 1: Using U3 gate
        let circuit1 =
            empty numQubits
            |> addGate (U3 (qubit, theta, phi, lambda))
        
        // Circuit 2: Using explicit RZ-RY-RZ decomposition
        let circuit2 =
            empty numQubits
            |> addGate (RZ (qubit, lambda))
            |> addGate (RY (qubit, theta))
            |> addGate (RZ (qubit, phi))
        
        let state1 = executeCircuit circuit1
        let state2 = executeCircuit circuit2
        
        // Both states should have same |0⟩ probability (up to numerical precision)
        let prob0_u3 = measureQubitProbability qubit state1
        let prob0_decomposed = measureQubitProbability qubit state2
        let difference = abs (prob0_u3 - prob0_decomposed)
        
        Assert.True(difference < 0.001, 
            $"U3 and RZ-RY-RZ decomposition should match. Difference = {difference}")
    
    [<Fact>]
    let ``U3 gate with zero parameters is identity`` () =
        // Test U3(0,0,0) = I (identity)
        
        let qubit = 0
        let numQubits = 1
        
        let circuit =
            empty numQubits
            |> addGate (U3 (qubit, 0.0, 0.0, 0.0))
        
        let finalState = executeCircuit circuit
        
        // Should remain in |0⟩ state
        let prob0 = measureQubitProbability qubit finalState
        Assert.True(prob0 > 0.999, 
            $"U3(0,0,0) should be identity, but P(0) = {prob0}")
    
    [<Fact>]
    let ``U3 gate can create superposition`` () =
        // Test U3(π/2, 0, 0) creates equal superposition (Hadamard-like)
        // U3(π/2, 0, 0) = RY(π/2) which creates (|0⟩ + |1⟩)/√2
        
        let qubit = 0
        let numQubits = 1
        let theta = System.Math.PI / 2.0
        
        let circuit =
            empty numQubits
            |> addGate (U3 (qubit, theta, 0.0, 0.0))
        
        let finalState = executeCircuit circuit
        
        // Should be 50/50 superposition
        let prob0 = measureQubitProbability qubit finalState
        let difference = abs (prob0 - 0.5)
        
        Assert.True(difference < 0.01, 
            $"U3(π/2, 0, 0) should create 50/50 superposition, but P(0) = {prob0}")
