namespace FSharp.Azure.Quantum.Tests

open System
open Xunit
open FSharp.Azure.Quantum.LocalSimulator
open FSharp.Azure.Quantum.GroverSearch.Oracle
open FSharp.Azure.Quantum.GroverSearch.AmplitudeAmplification

/// Tests for Generalized Amplitude Amplification
module AmplitudeAmplificationTests =
    
    // ========================================================================
    // GROVER AS SPECIAL CASE TESTS
    // ========================================================================
    
    [<Fact>]
    let ``Grover via amplitude amplification equals standard Grover`` () =
        let target = 5
        let numQubits = 3
        let oracle = forValue target numQubits
        let iterations = 2
        
        // Verify equivalence
        let isEquivalent = verifyGroverEquivalence oracle iterations
        
        Assert.True(isEquivalent, "Grover via amplitude amplification should equal standard Grover")
    
    [<Fact>]
    let ``GroverAsAmplification creates correct config`` () =
        let target = 7
        let numQubits = 3
        let oracle = forValue target numQubits
        let iterations = 3
        
        let config = groverAsAmplification oracle iterations
        
        Assert.Equal(numQubits, config.NumQubits)
        Assert.Equal(iterations, config.Iterations)
        Assert.True(config.ReflectionOperator.IsSome, "Should have reflection operator")
    
    [<Fact>]
    let ``ExecuteGroverViaAmplification finds target`` () =
        let target = 3
        let numQubits = 3
        let oracle = forValue target numQubits
        let iterations = FSharp.Azure.Quantum.GroverSearch.GroverIteration.optimalIterations (1 <<< numQubits) 1
        
        match executeGroverViaAmplification oracle iterations with
        | Ok result ->
            Assert.Equal(iterations, result.IterationsApplied)
            
            // Should have reasonable success probability
            Assert.True(result.SuccessProbability > 0.3,
                $"Success probability {result.SuccessProbability} should be > 0.3")
            
            // Measurement should find target
            let hasTarget = result.MeasurementCounts |> Map.containsKey target
            Assert.True(hasTarget, $"Should measure target {target}")
        | Error err ->
            Assert.True(false, $"Amplitude amplification failed: {err}")
    
    // ========================================================================
    // REFLECTION OPERATOR TESTS
    // ========================================================================
    
    [<Fact>]
    let ``ReflectionAboutState is self-inverse`` () =
        let numQubits = 2
        
        // Create target state (uniform superposition)
        let mutable targetState = StateVector.init numQubits
        for i in 0 .. numQubits - 1 do
            targetState <- Gates.applyH i targetState
        
        // Create test state
        let mutable testState = StateVector.init numQubits
        testState <- Gates.applyH 0 testState
        
        // Apply reflection twice
        let reflector = reflectionAboutState targetState
        let reflected1 = reflector testState
        let reflected2 = reflector reflected1
        
        // Should return to original state
        for i in 0 .. (1 <<< numQubits) - 1 do
            let originalAmp = StateVector.getAmplitude i testState
            let finalAmp = StateVector.getAmplitude i reflected2
            
            Assert.Equal(originalAmp.Real, finalAmp.Real, 10)
            Assert.Equal(originalAmp.Imaginary, finalAmp.Imaginary, 10)
    
    [<Fact>]
    let ``GroverReflection works on uniform superposition`` () =
        let numQubits = 3
        
        // Create uniform superposition
        let mutable state = StateVector.init numQubits
        for i in 0 .. numQubits - 1 do
            state <- Gates.applyH i state
        
        // Apply Grover reflection
        let reflector = groverReflection numQubits
        let reflected = reflector state
        
        // State should be modified but still normalized
        let norm = StateVector.norm reflected
        Assert.Equal(1.0, norm, 10)
    
    // ========================================================================
    // CUSTOM STATE PREPARATION TESTS
    // ========================================================================
    
    [<Fact>]
    let ``Partial uniform superposition creates correct state`` () =
        let numStates = 4  // Superposition over |0⟩, |1⟩, |2⟩, |3⟩
        let numQubits = 3
        
        let statePrep = partialUniformPreparation numStates numQubits
        let initialState = StateVector.init numQubits
        let prepared = statePrep initialState
        
        // First 4 states should have equal amplitude 1/√4 = 0.5
        let expectedAmp = 1.0 / Math.Sqrt(float numStates)
        
        for i in 0 .. numStates - 1 do
            let amp = StateVector.getAmplitude i prepared
            let magnitude = sqrt(amp.Real * amp.Real + amp.Imaginary * amp.Imaginary)
            Assert.Equal(expectedAmp, magnitude, 10)
        
        // Remaining states should be zero
        for i in numStates .. (1 <<< numQubits) - 1 do
            let amp = StateVector.getAmplitude i prepared
            let magnitude = sqrt(amp.Real * amp.Real + amp.Imaginary * amp.Imaginary)
            Assert.Equal(0.0, magnitude, 10)
    
    [<Fact>]
    let ``W-state preparation creates correct 3-qubit W-state`` () =
        let numQubits = 3
        let statePrep = wStatePreparation numQubits
        let initialState = StateVector.init numQubits
        let wState = statePrep initialState
        
        // W-state: (|001⟩ + |010⟩ + |100⟩)/√3
        let expectedAmp = 1.0 / Math.Sqrt(3.0)
        
        // Check states |001⟩=1, |010⟩=2, |100⟩=4
        for targetIndex in [1; 2; 4] do
            let amp = StateVector.getAmplitude targetIndex wState
            let magnitude = sqrt(amp.Real * amp.Real + amp.Imaginary * amp.Imaginary)
            Assert.Equal(expectedAmp, magnitude, 10)
        
        // Other states should be zero
        for i in [0; 3; 5; 6; 7] do
            let amp = StateVector.getAmplitude i wState
            let magnitude = sqrt(amp.Real * amp.Real + amp.Imaginary * amp.Imaginary)
            Assert.Equal(0.0, magnitude, 10)
    
    // ========================================================================
    // CUSTOM PREPARATION WITH AMPLIFICATION TESTS
    // ========================================================================
    
    [<Fact>]
    let ``Amplitude amplification with partial uniform preparation`` () =
        let numQubits = 3
        let target = 2  // One of the prepared states
        let oracle = forValue target numQubits
        
        // Prepare uniform superposition over first 4 states (|0⟩-|3⟩)
        let statePrep = partialUniformPreparation 4 numQubits
        let iterations = 2
        
        match executeWithCustomPreparation oracle statePrep iterations with
        | Ok result ->
            Assert.Equal(iterations, result.IterationsApplied)
            
            // Should amplify target state
            let targetProb = StateVector.probability target result.FinalState
            Assert.True(targetProb > 0.1, $"Target probability {targetProb} should be amplified")
        | Error err ->
            Assert.True(false, $"Custom preparation failed: {err}")
    
    [<Fact>]
    let ``ExecuteWithCustomPreparation runs without error`` () =
        let numQubits = 3
        let target = 5
        let oracle = forValue target numQubits
        
        // Simple custom preparation: just apply H to first qubit
        let statePrep (state: StateVector.StateVector) =
            Gates.applyH 0 state
        
        let iterations = 3
        
        match executeWithCustomPreparation oracle statePrep iterations with
        | Ok result ->
            Assert.Equal(iterations, result.IterationsApplied)
            Assert.True(result.SuccessProbability >= 0.0 && result.SuccessProbability <= 1.0)
        | Error err ->
            Assert.True(false, $"Execution failed: {err}")
    
    // ========================================================================
    // OPTIMAL ITERATIONS CALCULATION TESTS
    // ========================================================================
    
    [<Fact>]
    let ``OptimalIterations with zero initial success returns Grover formula`` () =
        let N = 16
        let M = 1
        let p0 = 0.0  // No initial success
        
        let k = optimalIterations N M p0
        
        // Should fall back to standard Grover: π/4 * √(16/1) = π/4 * 4 ≈ 3
        Assert.Equal(3, k)
    
    [<Fact>]
    let ``OptimalIterations with high initial success returns few iterations`` () =
        let N = 16
        let M = 4
        let p0 = 0.8  // Already 80% success
        
        let k = optimalIterations N M p0
        
        // With high initial success, should need few iterations
        Assert.True(k < 5, $"With p₀=0.8, should need < 5 iterations, got {k}")
    
    [<Fact>]
    let ``OptimalIterations with perfect initial success returns zero`` () =
        let N = 16
        let M = 1
        let p0 = 1.0  // Perfect initial success
        
        let k = optimalIterations N M p0
        
        // No amplification needed
        Assert.Equal(0, k)
    
    // ========================================================================
    // AMPLITUDE AMPLIFICATION EXECUTION TESTS
    // ========================================================================
    
    [<Fact>]
    let ``Execute with zero iterations returns prepared state`` () =
        let numQubits = 3
        let target = 5
        let oracle = forValue target numQubits
        
        let statePrep (state: StateVector.StateVector) =
            Gates.applyH 0 state
        
        let config = {
            NumQubits = numQubits
            StatePreparation = statePrep
            Oracle = oracle
            ReflectionOperator = None
            Iterations = 0
        }
        
        match execute config with
        | Ok result ->
            Assert.Equal(0, result.IterationsApplied)
            
            // Final state should be the prepared state (H on qubit 0 only)
            // For 3-qubit system, qubit 0 is least significant
            // H on qubit 0: |000⟩ -> (|000⟩ + |001⟩)/√2
            // So states |0⟩ (000) and |1⟩ (001) should have non-zero amplitude
            let amp0 = StateVector.getAmplitude 0 result.FinalState
            let amp1 = StateVector.getAmplitude 1 result.FinalState
            
            Assert.True(abs amp0.Real > 0.1, "State |0⟩ should have amplitude")
            Assert.True(abs amp1.Real > 0.1, "State |1⟩ should have amplitude")
        | Error err ->
            Assert.True(false, $"Execution failed: {err}")
    
    [<Fact>]
    let ``Execute validates configuration`` () =
        let numQubits = 3
        let target = 5
        let oracle = forValue target numQubits
        
        // Invalid: negative iterations
        let invalidConfig = {
            NumQubits = numQubits
            StatePreparation = fun s -> s
            Oracle = oracle
            ReflectionOperator = None
            Iterations = -1
        }
        
        match execute invalidConfig with
        | Ok _ -> Assert.True(false, "Should reject negative iterations")
        | Error msg -> Assert.Contains("non-negative", msg)
    
    [<Fact>]
    let ``Execute detects qubit mismatch`` () =
        let oracle = forValue 5 3  // 3 qubits
        
        // Config with wrong number of qubits
        let invalidConfig = {
            NumQubits = 4  // Mismatch!
            StatePreparation = fun s -> s
            Oracle = oracle
            ReflectionOperator = None
            Iterations = 2
        }
        
        match execute invalidConfig with
        | Ok _ -> Assert.True(false, "Should detect qubit mismatch")
        | Error msg -> Assert.Contains("qubit", msg.ToLower())
    
    // ========================================================================
    // RESULT VALIDATION TESTS
    // ========================================================================
    
    [<Fact>]
    let ``Amplitude amplification result contains all fields`` () =
        let numQubits = 3
        let target = 7
        let oracle = forValue target numQubits
        let iterations = 2
        
        match executeGroverViaAmplification oracle iterations with
        | Ok result ->
            // Verify all result fields are populated
            Assert.True(StateVector.dimension result.FinalState > 0)
            Assert.Equal(iterations, result.IterationsApplied)
            Assert.True(result.SuccessProbability >= 0.0 && result.SuccessProbability <= 1.0)
            Assert.NotEmpty(result.MeasurementCounts)
            Assert.True(result.Shots > 0)
            
            // Total measurement counts should equal shots
            let totalCounts = result.MeasurementCounts |> Map.toList |> List.sumBy snd
            Assert.Equal(result.Shots, totalCounts)
        | Error err ->
            Assert.True(false, $"Execution failed: {err}")
    
    [<Fact>]
    let ``Amplitude amplification preserves state norm`` () =
        let numQubits = 3
        let target = 3
        let oracle = forValue target numQubits
        let iterations = 3
        
        match executeGroverViaAmplification oracle iterations with
        | Ok result ->
            let norm = StateVector.norm result.FinalState
            Assert.Equal(1.0, norm, 10)
        | Error err ->
            Assert.True(false, $"Execution failed: {err}")
    
    // ========================================================================
    // COMPARISON TESTS - Verify amplitude amplification generalizes Grover
    // ========================================================================
    
    [<Fact>]
    let ``Grover equivalence for multiple targets`` () =
        let targets = [2; 5; 7]
        let numQubits = 3
        let oracle = forValues targets numQubits
        let iterations = 2
        
        let isEquivalent = verifyGroverEquivalence oracle iterations
        
        Assert.True(isEquivalent, "Should be equivalent for multiple targets")
    
    [<Fact>]
    let ``Grover equivalence with different iteration counts`` () =
        let target = 6
        let numQubits = 3
        let oracle = forValue target numQubits
        
        for iterations in [1; 2; 3; 4] do
            let isEquivalent = verifyGroverEquivalence oracle iterations
            Assert.True(isEquivalent, $"Should be equivalent with {iterations} iterations")
    
    // ========================================================================
    // EDGE CASES
    // ========================================================================
    
    [<Fact>]
    let ``Amplitude amplification works with 1-qubit system`` () =
        let numQubits = 1
        let target = 1
        let oracle = forValue target numQubits
        let iterations = 1
        
        match executeGroverViaAmplification oracle iterations with
        | Ok result ->
            Assert.Equal(iterations, result.IterationsApplied)
        | Error err ->
            Assert.True(false, $"1-qubit amplification failed: {err}")
    
    [<Fact>]
    let ``Amplitude amplification works with 5-qubit system`` () =
        let numQubits = 5
        let target = 20
        let oracle = forValue target numQubits
        let iterations = 4
        
        match executeGroverViaAmplification oracle iterations with
        | Ok result ->
            Assert.Equal(iterations, result.IterationsApplied)
            Assert.Equal(32, 1 <<< numQubits)  // Verify 32-dimensional space
        | Error err ->
            Assert.True(false, $"5-qubit amplification failed: {err}")
