namespace FSharp.Azure.Quantum.Tests

open System
open System.Numerics
open Xunit
open FSharp.Azure.Quantum.LocalSimulator

module QaoaSimulatorTests =
    
    [<Fact>]
    let ``Initialize uniform superposition - should create equal amplitudes`` () =
        // Test 1 qubit: |+⟩ = (|0⟩+|1⟩)/√2
        let state1q = QaoaSimulator.initializeUniformSuperposition 1
        let sqrtHalf = 1.0 / sqrt 2.0
        Assert.Equal(sqrtHalf, (StateVector.getAmplitude 0 state1q).Real, 10)
        Assert.Equal(sqrtHalf, (StateVector.getAmplitude 1 state1q).Real, 10)
        
        // Test 2 qubits: |++⟩ = (|00⟩+|01⟩+|10⟩+|11⟩)/2
        let state2q = QaoaSimulator.initializeUniformSuperposition 2
        let quarter = 0.5
        for i in 0..3 do
            Assert.Equal(quarter, (StateVector.getAmplitude i state2q).Real, 10)
        
        // Test norm preservation
        Assert.Equal(1.0, StateVector.norm state1q, 10)
        Assert.Equal(1.0, StateVector.norm state2q, 10)
    
    [<Fact>]
    let ``Initialize uniform superposition - should reject invalid qubit counts`` () =
        Assert.Throws<System.Exception>(fun () -> 
            QaoaSimulator.initializeUniformSuperposition 0 |> ignore
        ) |> ignore
        Assert.Throws<System.Exception>(fun () -> 
            QaoaSimulator.initializeUniformSuperposition 17 |> ignore
        ) |> ignore
    
    [<Fact>]
    let ``Apply cost layer - should apply Rz rotations`` () =
        // Single qubit with cost coefficient 1.0
        let state = QaoaSimulator.initializeUniformSuperposition 1
        let gamma = Math.PI / 4.0  // 45 degrees
        let costCoeffs = [| 1.0 |]
        
        let resultState = QaoaSimulator.applyCostLayer gamma costCoeffs state
        
        // After Rz(π/2) on superposition, norm should be preserved
        Assert.Equal(1.0, StateVector.norm resultState, 10)
        
        // Rz adds phase, doesn't change magnitude
        let amp0Mag = (StateVector.getAmplitude 0 resultState).Magnitude
        let amp1Mag = (StateVector.getAmplitude 1 resultState).Magnitude
        let sqrtHalf = 1.0 / sqrt 2.0
        Assert.Equal(sqrtHalf, amp0Mag, 10)
        Assert.Equal(sqrtHalf, amp1Mag, 10)
    
    [<Fact>]
    let ``Apply cost layer - should handle zero coefficients`` () =
        let state = QaoaSimulator.initializeUniformSuperposition 2
        let gamma = Math.PI / 4.0
        let costCoeffs = [| 0.0; 0.0 |]  // No cost
        
        let resultState = QaoaSimulator.applyCostLayer gamma costCoeffs state
        
        // With zero coefficients, Rz(0) = I, state unchanged
        Assert.True(StateVector.equals state resultState)
    
    [<Fact>]
    let ``Apply cost layer - should reject mismatched array length`` () =
        let state = QaoaSimulator.initializeUniformSuperposition 2
        let costCoeffs = [| 1.0 |]  // Wrong length
        
        Assert.Throws<System.Exception>(fun () ->
            QaoaSimulator.applyCostLayer 1.0 costCoeffs state |> ignore
        )
    
    [<Fact>]
    let ``Apply cost interaction - should apply ZZ interaction`` () =
        // Start with uniform superposition on 2 qubits
        let state = QaoaSimulator.initializeUniformSuperposition 2
        let gamma = Math.PI / 8.0
        let coefficient = 1.0
        
        let resultState = QaoaSimulator.applyCostInteraction gamma 0 1 coefficient state
        
        // Norm should be preserved
        Assert.Equal(1.0, StateVector.norm resultState, 10)
        
        // State should be modified (not equal to original)
        Assert.False(StateVector.equals state resultState)
    
    [<Fact>]
    let ``Apply cost interaction - should reject invalid indices`` () =
        let state = QaoaSimulator.initializeUniformSuperposition 2
        
        // Same qubit for both indices
        Assert.Throws<System.Exception>(fun () ->
            QaoaSimulator.applyCostInteraction 1.0 0 0 1.0 state |> ignore
        ) |> ignore
        
        // Out of range
        Assert.Throws<System.Exception>(fun () ->
            QaoaSimulator.applyCostInteraction 1.0 0 2 1.0 state |> ignore
        ) |> ignore
    
    [<Fact>]
    let ``Apply mixer layer - should apply Rx rotations`` () =
        // Start with |0⟩ state
        let state = StateVector.init 1
        let beta = Math.PI / 4.0  // π/2 rotation angle
        
        let resultState = QaoaSimulator.applyMixerLayer beta state
        
        // Rx(π/2) on |0⟩ creates superposition
        Assert.Equal(1.0, StateVector.norm resultState, 10)
        
        // Both amplitudes should be non-zero
        let amp0Mag = (StateVector.getAmplitude 0 resultState).Magnitude
        let amp1Mag = (StateVector.getAmplitude 1 resultState).Magnitude
        Assert.True(amp0Mag > 0.1)
        Assert.True(amp1Mag > 0.1)
    
    [<Fact>]
    let ``Apply mixer layer - should preserve norm`` () =
        let state = QaoaSimulator.initializeUniformSuperposition 3
        let beta = 0.75
        
        let resultState = QaoaSimulator.applyMixerLayer beta state
        
        Assert.Equal(1.0, StateVector.norm resultState, 10)
    
    [<Fact>]
    let ``Apply QAOA layer - should combine cost and mixer`` () =
        let state = QaoaSimulator.initializeUniformSuperposition 2
        let gamma = 0.5
        let beta = 0.3
        let costCoeffs = [| 1.0; -1.0 |]
        
        let resultState = QaoaSimulator.applyQaoaLayer gamma beta costCoeffs state
        
        // Should preserve norm
        Assert.Equal(1.0, StateVector.norm resultState, 10)
        
        // Should modify state
        Assert.False(StateVector.equals state resultState)
    
    [<Fact>]
    let ``Run QAOA circuit - should apply multiple layers`` () =
        let numQubits = 2
        let gammas = [| 0.5; 0.3 |]
        let betas = [| 0.4; 0.6 |]
        let costCoeffs = [| 1.0; 1.0 |]
        
        let finalState = QaoaSimulator.runQaoaCircuit numQubits gammas betas costCoeffs
        
        // Should preserve norm through all layers
        Assert.Equal(1.0, StateVector.norm finalState, 10)
        
        // Should be in non-trivial superposition
        let amp0Mag = (StateVector.getAmplitude 0 finalState).Magnitude
        Assert.True(amp0Mag > 0.01 && amp0Mag < 0.99)
    
    [<Fact>]
    let ``Run QAOA circuit - should reject mismatched array lengths`` () =
        let gammas = [| 0.5; 0.3 |]
        let betas = [| 0.4 |]  // Wrong length
        let costCoeffs = [| 1.0; 1.0 |]
        
        Assert.Throws<System.Exception>(fun () ->
            QaoaSimulator.runQaoaCircuit 2 gammas betas costCoeffs |> ignore
        )
    
    [<Fact>]
    let ``Compute cost expectation - should calculate correctly for basis states`` () =
        // Test on |0⟩: Z eigenvalue is +1
        let state0 = StateVector.init 1
        let costCoeffs0 = [| 1.0 |]
        let expectation0 = QaoaSimulator.computeCostExpectation costCoeffs0 state0
        Assert.Equal(1.0, expectation0, 10)  // Cost = 1.0 * (+1) = 1.0
        
        // Test on |1⟩: Z eigenvalue is -1
        let state1 = StateVector.create [| Complex.Zero; Complex.One |]
        let expectation1 = QaoaSimulator.computeCostExpectation costCoeffs0 state1
        Assert.Equal(-1.0, expectation1, 10)  // Cost = 1.0 * (-1) = -1.0
    
    [<Fact>]
    let ``Compute cost expectation - should handle superposition`` () =
        // Test on uniform superposition |+⟩ = (|0⟩+|1⟩)/√2
        let statePlus = QaoaSimulator.initializeUniformSuperposition 1
        let costCoeffs = [| 1.0 |]
        let expectation = QaoaSimulator.computeCostExpectation costCoeffs statePlus
        
        // Expectation = 0.5*(+1) + 0.5*(-1) = 0
        Assert.Equal(0.0, expectation, 10)
    
    [<Fact>]
    let ``Compute cost expectation - should handle multiple qubits`` () =
        // Test on |00⟩: both qubits in |0⟩, Z eigenvalues both +1
        let state00 = StateVector.init 2
        let costCoeffs = [| 1.0; 1.0 |]
        let expectation00 = QaoaSimulator.computeCostExpectation costCoeffs state00
        Assert.Equal(2.0, expectation00, 10)  // Cost = 1.0*(+1) + 1.0*(+1) = 2.0
        
        // Test on |11⟩: both qubits in |1⟩, Z eigenvalues both -1
        let state11 = StateVector.create [| Complex.Zero; Complex.Zero; Complex.Zero; Complex.One |]
        let expectation11 = QaoaSimulator.computeCostExpectation costCoeffs state11
        Assert.Equal(-2.0, expectation11, 10)  // Cost = 1.0*(-1) + 1.0*(-1) = -2.0
        
        // Test on |01⟩: qubit_0=1 (-1), qubit_1=0 (+1)
        let state01 = StateVector.create [| Complex.Zero; Complex.One; Complex.Zero; Complex.Zero |]
        let expectation01 = QaoaSimulator.computeCostExpectation costCoeffs state01
        Assert.Equal(0.0, expectation01, 10)  // Cost = 1.0*(-1) + 1.0*(+1) = 0.0
    
    [<Fact>]
    let ``Compute cost expectation - should handle negative coefficients`` () =
        let state0 = StateVector.init 1
        let costCoeffs = [| -2.0 |]
        let expectation = QaoaSimulator.computeCostExpectation costCoeffs state0
        Assert.Equal(-2.0, expectation, 10)  // Cost = -2.0 * (+1) = -2.0
    
    [<Fact>]
    let ``Compute cost expectation - should reject mismatched array length`` () =
        let state = QaoaSimulator.initializeUniformSuperposition 2
        let costCoeffs = [| 1.0 |]  // Wrong length
        
        Assert.Throws<System.Exception>(fun () ->
            QaoaSimulator.computeCostExpectation costCoeffs state |> ignore
        )
    
    [<Fact>]
    let ``Full QAOA workflow - should optimize simple problem`` () =
        // Simple MaxCut problem on 2 qubits
        // Goal: maximize Z₀ + Z₁ (both qubits should be |0⟩)
        let numQubits = 2
        let gammas = [| 0.5 |]  // Single layer
        let betas = [| 0.3 |]
        let costCoeffs = [| 1.0; 1.0 |]  // Favor |0⟩ states
        
        let finalState = QaoaSimulator.runQaoaCircuit numQubits gammas betas costCoeffs
        let expectation = QaoaSimulator.computeCostExpectation costCoeffs finalState
        
        // Expectation should be positive (favoring |00⟩)
        // Note: This is a very rough test - actual QAOA requires parameter optimization
        Assert.True(expectation > -2.0)  // Not the worst case
        Assert.Equal(1.0, StateVector.norm finalState, 10)
    
    // ============================================================================
    // HIGH-LEVEL SIMULATE API TESTS
    // ============================================================================
    
    [<Fact>]
    let ``simulate - should return Ok result for valid 2-qubit circuit`` () =
        let circuit: QaoaSimulator.QaoaCircuit = {
            NumQubits = 2
            Parameters = [| 0.5; 0.3 |]  // gamma=0.5, beta=0.3
            CostTerms = [| (0, 1, -1.0) |]  // Single edge interaction
            Depth = 1
        }
        
        let result = QaoaSimulator.simulate circuit 100
        
        match result with
        | Ok qaoaResult ->
            Assert.Equal(100, qaoaResult.Shots)
            Assert.True(qaoaResult.Counts.Count > 0)
            Assert.True(qaoaResult.ExecutionTimeMs > 0.0)
            
            // Verify total counts equal shots
            let totalCounts = qaoaResult.Counts |> Map.toSeq |> Seq.sumBy snd
            Assert.Equal(100, totalCounts)
            
            // Verify bitstring format (2 qubits = 2 characters)
            qaoaResult.Counts |> Map.iter (fun bitstring _ ->
                Assert.Equal(2, bitstring.Length)
                Assert.True(bitstring |> String.forall (fun c -> c = '0' || c = '1'))
            )
        | Error msg -> Assert.True(false, $"Expected Ok, got Error: {msg}")
    
    [<Fact>]
    let ``simulate - should handle single-qubit cost terms`` () =
        let circuit: QaoaSimulator.QaoaCircuit = {
            NumQubits = 2
            Parameters = [| 0.5; 0.3 |]
            CostTerms = [| (0, 0, 1.0); (1, 1, 1.0) |]  // Single-qubit terms
            Depth = 1
        }
        
        let result = QaoaSimulator.simulate circuit 50
        
        match result with
        | Ok qaoaResult ->
            Assert.Equal(50, qaoaResult.Shots)
            let totalCounts = qaoaResult.Counts |> Map.toSeq |> Seq.sumBy snd
            Assert.Equal(50, totalCounts)
        | Error msg -> Assert.True(false, $"Expected Ok, got Error: {msg}")
    
    [<Fact>]
    let ``simulate - should handle multiple QAOA layers`` () =
        let circuit: QaoaSimulator.QaoaCircuit = {
            NumQubits = 3
            Parameters = [| 0.5; 0.3; 0.4; 0.2 |]  // 2 layers: (γ1,β1), (γ2,β2)
            CostTerms = [| (0, 1, -1.0); (1, 2, -1.0) |]
            Depth = 2
        }
        
        let result = QaoaSimulator.simulate circuit 200
        
        match result with
        | Ok qaoaResult ->
            Assert.Equal(200, qaoaResult.Shots)
            qaoaResult.Counts |> Map.iter (fun bitstring _ ->
                Assert.Equal(3, bitstring.Length)
            )
        | Error msg -> Assert.True(false, $"Expected Ok, got Error: {msg}")
    
    [<Fact>]
    let ``simulate - should return Error for invalid NumQubits`` () =
        let circuit: QaoaSimulator.QaoaCircuit = {
            NumQubits = 0  // Invalid
            Parameters = [| 0.5; 0.3 |]
            CostTerms = [| |]
            Depth = 1
        }
        
        let result = QaoaSimulator.simulate circuit 100
        
        match result with
        | Ok _ -> Assert.True(false, "Expected Error, got Ok")
        | Error msg -> Assert.Contains("qubits", msg.ToLower())
    
    [<Fact>]
    let ``simulate - should return Error for invalid number of shots`` () =
        let circuit: QaoaSimulator.QaoaCircuit = {
            NumQubits = 2
            Parameters = [| 0.5; 0.3 |]
            CostTerms = [| (0, 1, -1.0) |]
            Depth = 1
        }
        
        let result = QaoaSimulator.simulate circuit 0  // Invalid
        
        match result with
        | Ok _ -> Assert.True(false, "Expected Error, got Ok")
        | Error msg -> Assert.Contains("shots", msg.ToLower())
    
    [<Fact>]
    let ``simulate - should return Error for mismatched Parameters length`` () =
        let circuit: QaoaSimulator.QaoaCircuit = {
            NumQubits = 2
            Parameters = [| 0.5 |]  // Should be 2 for Depth=1
            CostTerms = [| (0, 1, -1.0) |]
            Depth = 1
        }
        
        let result = QaoaSimulator.simulate circuit 100
        
        match result with
        | Ok _ -> Assert.True(false, "Expected Error, got Ok")
        | Error msg -> Assert.Contains("parameters", msg.ToLower())
    
    [<Fact>]
    let ``simulate - MaxCut example from documentation`` () =
        // 4-node MaxCut problem from docs
        // Graph: 0-1, 1-2, 2-3, 3-0, 0-2 (5 edges)
        let edges = [(0, 1); (1, 2); (2, 3); (3, 0); (0, 2)]
        let circuit: QaoaSimulator.QaoaCircuit = {
            NumQubits = 4
            Parameters = [| 0.5; 0.3 |]
            CostTerms = edges |> List.map (fun (i, j) -> (i, j, -1.0)) |> Array.ofList
            Depth = 1
        }
        
        let result = QaoaSimulator.simulate circuit 1000
        
        match result with
        | Ok qaoaResult ->
            Assert.Equal(1000, qaoaResult.Shots)
            
            // Verify we get multiple different measurement outcomes
            Assert.True(qaoaResult.Counts.Count > 1, "Should have multiple outcomes")
            
            // Verify bitstrings are 4 characters long
            qaoaResult.Counts |> Map.iter (fun bitstring _ ->
                Assert.Equal(4, bitstring.Length)
            )
            
            // Find most common outcome
            let mostCommon = 
                qaoaResult.Counts 
                |> Map.toSeq 
                |> Seq.maxBy snd
                |> fst
            
            // Most common should appear multiple times
            Assert.True(qaoaResult.Counts[mostCommon] > 10)
        | Error msg -> Assert.True(false, $"Expected Ok, got Error: {msg}")
