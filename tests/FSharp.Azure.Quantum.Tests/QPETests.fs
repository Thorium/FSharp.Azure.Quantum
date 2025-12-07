namespace FSharp.Azure.Quantum.Tests

open Xunit
open FSharp.Azure.Quantum.Algorithms.QuantumPhaseEstimation
open FSharp.Azure.Quantum.Algorithms.QPEBackendAdapter
open FSharp.Azure.Quantum.LocalSimulator.StateVector
open FSharp.Azure.Quantum.Core.BackendAbstraction
open FSharp.Azure.Quantum.Core.CircuitAbstraction
open FSharp.Azure.Quantum.CircuitBuilder
open System

/// Tests for Quantum Phase Estimation (QPE) and Backend Adapter
module QPETests =
    
    // ========================================================================
    // LOCAL SIMULATION TESTS (using QuantumPhaseEstimation module)
    // ========================================================================
    
    [<Fact>]
    let ``QPE estimates T gate phase correctly (3 qubits)`` () =
        // T gate: e^(iπ/4) = e^(2πi·1/8)
        // Expected phase: φ = 1/8 = 0.125
        match estimateTGatePhase 3 with
        | Error msg -> Assert.Fail($"QPE execution failed: {msg}")
        | Ok result ->
            // With 3 qubits, we get 3 bits of precision
            // φ = 1/8 = 0.001 in binary → measurement should be 1
            let expectedPhase = 1.0 / 8.0
            
            // Allow small error due to quantum measurement
            let error = abs (result.EstimatedPhase - expectedPhase)
            Assert.True(error < 0.2, $"Expected phase ~{expectedPhase}, got {result.EstimatedPhase}")
            Assert.Equal(3, result.Precision)
    
    [<Fact>]
    let ``QPE estimates S gate phase correctly (3 qubits)`` () =
        // S gate: e^(iπ/2) = e^(2πi·1/4)
        // Expected phase: φ = 1/4 = 0.25
        match estimateSGatePhase 3 with
        | Error msg -> Assert.Fail($"QPE execution failed: {msg}")
        | Ok result ->
            // With 3 qubits: φ = 1/4 = 0.010 in binary → measurement should be 2
            let expectedPhase = 1.0 / 4.0
            
            let error = abs (result.EstimatedPhase - expectedPhase)
            Assert.True(error < 0.2, $"Expected phase ~{expectedPhase}, got {result.EstimatedPhase}")
            Assert.Equal(3, result.Precision)
    
    [<Fact>]
    let ``QPE with higher precision gives more accurate results`` () =
        // Test with increasing precision (4, 5, 6 qubits)
        // T gate phase = 1/8 = 0.125
        
        let results = [4; 5; 6] |> List.map estimateTGatePhase
        
        for result in results do
            match result with
            | Error msg -> Assert.Fail($"QPE failed: {msg}")
            | Ok r ->
                let expectedPhase = 1.0 / 8.0
                let error = abs (r.EstimatedPhase - expectedPhase)
                
                // Higher precision should give smaller error
                let maxError = 1.0 / float (1 <<< (r.Precision - 1))
                Assert.True(error <= maxError, 
                    $"With {r.Precision} qubits, expected error < {maxError}, got {error}")
    
    [<Fact>]
    let ``QPE validates qubit count`` () =
        match estimateTGatePhase 0 with
        | Ok _ -> Assert.Fail("Should reject 0 counting qubits")
        | Error msg -> Assert.Contains("positive", msg.Message.ToLower())
        
        match estimateTGatePhase 17 with
        | Ok _ -> Assert.Fail("Should reject > 16 counting qubits for local sim")
        | Error msg -> Assert.Contains("16", msg.Message)
    
    [<Fact>]
    let ``QPE estimates phase gate correctly`` () =
        // Test with custom phase gate: θ = π/3
        // U = e^(iπ/3) = e^(2πi·1/6)
        // Expected phase: φ = 1/6 ≈ 0.1667
        let theta = Math.PI / 3.0
        
        match estimatePhaseGate theta 4 with
        | Error msg -> Assert.Fail($"QPE execution failed: {msg}")
        | Ok result ->
            let expectedPhase = 1.0 / 6.0
            
            // With 4 qubits of precision, allow reasonable error
            let error = abs (result.EstimatedPhase - expectedPhase)
            Assert.True(error < 0.15, $"Expected phase ~{expectedPhase}, got {result.EstimatedPhase}")
    
    [<Fact>]
    let ``QPE configuration validation`` () =
        // Test invalid configurations
        
        let invalidConfig1 = {
            CountingQubits = 0
            TargetQubits = 1
            UnitaryOperator = TGate
            EigenVector = None
        }
        
        match execute invalidConfig1 with
        | Ok _ -> Assert.Fail("Should reject 0 counting qubits")
        | Error msg -> Assert.Contains("positive", msg.Message.ToLower())
        
        let invalidConfig2 = {
            CountingQubits = 3
            TargetQubits = 0
            UnitaryOperator = TGate
            EigenVector = None
        }
        
        match execute invalidConfig2 with
        | Ok _ -> Assert.Fail("Should reject 0 target qubits")
        | Error msg -> Assert.Contains("positive", msg.Message.ToLower())
    
    [<Fact>]
    let ``QPE returns correct gate count`` () =
        match estimateTGatePhase 3 with
        | Error msg -> Assert.Fail($"QPE execution failed: {msg}")
        | Ok result ->
            // QPE gate count = H gates + controlled-U gates + inverse QFT gates
            // For 3 counting qubits:
            // - 3 H gates
            // - Controlled-U: 1 + 2 + 4 = 7 gates
            // - Inverse QFT: ~10 gates
            // Total: ~20 gates
            
            Assert.True(result.GateCount >= 3, $"Expected at least 3 gates, got {result.GateCount}")
            Assert.True(result.GateCount < 100, $"Gate count seems too high: {result.GateCount}")
    
    // ========================================================================
    // BACKEND ADAPTER TESTS
    // ========================================================================
    
    [<Fact>]
    let ``QPE backend adapter validates configuration`` () =
        let backend = createLocalBackend()
        
        let invalidConfig = {
            CountingQubits = 0
            TargetQubits = 1
            UnitaryOperator = TGate
            EigenVector = None
        }
        
        match executeWithBackend invalidConfig backend 100 with
        | Ok _ -> Assert.Fail("Should reject invalid config")
        | Error msg -> Assert.Contains("positive", msg.ToLower())
    
    [<Fact>]
    let ``QPE backend adapter creates valid circuit`` () =
        let config = {
            CountingQubits = 3
            TargetQubits = 1
            UnitaryOperator = TGate
            EigenVector = None
        }
        
        match qpeToCircuit config with
        | Error msg -> Assert.Fail($"Circuit creation failed: {msg}")
        | Ok circuit ->
            // Verify circuit structure
            let gates = FSharp.Azure.Quantum.CircuitBuilder.getGates circuit
            
            // For 3 counting qubits + 1 target qubit:
            // - 3 H gates (counting qubits)
            // - 1 X gate (prepare target in |1⟩)
            // - Controlled-U operations: 3 CP gates (powers 1,2,4)
            //   Note: CP with angle 2π is mathematically identity but still included
            // - Inverse QFT (3 qubits): 3 H + 3 controlled-phase gates + 1 SWAP
            // Total: 3 H + 1 X + 3 CP + 3 H + 3 CP + 1 SWAP = 14 gates
            Assert.True(gates.Length >= 14, 
                $"Circuit should have at least 14 gates for 3-qubit QPE, got {gates.Length}")
            
            // Verify total qubits
            let totalQubits = qubitCount circuit
            Assert.Equal(4, totalQubits)
            
            // Verify first gates are Hadamards on counting qubits (0, 1, 2)
            let firstThreeGates = gates |> List.take 3
            Assert.True(
                firstThreeGates |> List.forall (function | H _ -> true | _ -> false),
                "First 3 gates should be Hadamards on counting qubits"
            )
    
    [<Fact>]
    let ``QPE backend T gate estimation with local backend`` () =
        let backend = createLocalBackend()
        
        match estimateTGatePhaseBackend 3 backend 1000 with
        | Error msg -> Assert.Fail($"Backend execution failed: {msg}")
        | Ok phase ->
            let expectedPhase = 1.0 / 8.0
            
            // Backend execution with measurements may have more variance
            let error = abs (phase - expectedPhase)
            Assert.True(error < 0.25, $"Expected phase ~{expectedPhase}, got {phase}")
    
    [<Fact>]
    let ``QPE backend S gate estimation with local backend`` () =
        let backend = createLocalBackend()
        
        match estimateSGatePhaseBackend 3 backend 1000 with
        | Error msg -> Assert.Fail($"Backend execution failed: {msg}")
        | Ok phase ->
            let expectedPhase = 1.0 / 4.0
            
            let error = abs (phase - expectedPhase)
            Assert.True(error < 0.25, $"Expected phase ~{expectedPhase}, got {phase}")
    
    [<Fact>]
    let ``QPE backend phase gate estimation`` () =
        let backend = createLocalBackend()
        let theta = Math.PI / 3.0  // Phase gate with θ = π/3
        
        match estimatePhaseGateBackend theta 4 backend 1000 with
        | Error msg -> Assert.Fail($"Backend execution failed: {msg}")
        | Ok phase ->
            let expectedPhase = 1.0 / 6.0
            
            let error = abs (phase - expectedPhase)
            Assert.True(error < 0.3, $"Expected phase ~{expectedPhase}, got {phase}")
    
    [<Fact>]
    let ``QPE histogram extraction works correctly`` () =
        // Simulate a histogram with dominant measurement
        let histogram = 
            Map.ofList [
                (1, 700)   // Most likely: φ = 1/8 for 3 qubits
                (0, 150)
                (2, 100)
                (3, 50)
            ]
        
        let phase = extractPhaseFromHistogram histogram 3
        let expectedPhase = 1.0 / 8.0  // 1 / 2^3
        
        Assert.Equal(expectedPhase, phase)
    
    [<Fact>]
    let ``QPE backend adapter rejects too many qubits`` () =
        let config = {
            CountingQubits = 25  // Too many
            TargetQubits = 1
            UnitaryOperator = TGate
            EigenVector = None
        }
        
        match qpeToCircuit config with
        | Ok _ -> Assert.Fail("Should reject > 20 counting qubits")
        | Error msg -> Assert.Contains("20", msg.Message)
    
    [<Fact>]
    let ``QPE preserves quantum state structure`` () =
        // QPE should maintain valid quantum state throughout
        match estimateTGatePhase 3 with
        | Error msg -> Assert.Fail($"QPE execution failed: {msg}")
        | Ok result ->
            // Check state is normalized
            let stateNorm = norm result.FinalState
            Assert.True(abs (stateNorm - 1.0) < 1e-6, $"State should be normalized, got norm {stateNorm}")
            
            // Check state has correct number of qubits
            let numQubits = numQubits result.FinalState
            let expectedQubits = result.Config.CountingQubits + result.Config.TargetQubits
            Assert.Equal(expectedQubits, numQubits)
