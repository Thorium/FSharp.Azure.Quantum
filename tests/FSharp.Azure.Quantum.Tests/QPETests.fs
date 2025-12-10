namespace FSharp.Azure.Quantum.Tests

open Xunit
open FSharp.Azure.Quantum.Core
open FSharp.Azure.Quantum.Core.BackendAbstraction
open FSharp.Azure.Quantum.Backends
open System

/// Tests for Quantum Phase Estimation (QPE) using unified backend
module QPE = FSharp.Azure.Quantum.Algorithms.QPE

module QPETests =
    
    // ========================================================================
    // QPE UNIFIED BACKEND TESTS
    // ========================================================================
    
    [<Fact>]
    let ``QPE estimates T gate phase correctly (3 qubits)`` () =
        // T gate: e^(iπ/4) = e^(2πi·1/8)
        // Expected phase: φ = 1/8 = 0.125
        let backend = LocalBackend.LocalBackend() :> IQuantumBackend
        
        match QPE.estimateTGatePhase 3 backend with
        | Error err -> Assert.Fail($"QPE execution failed: {err}")
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
        let backend = LocalBackend.LocalBackend() :> IQuantumBackend
        
        match QPE.estimateSGatePhase 3 backend with
        | Error err -> Assert.Fail($"QPE execution failed: {err}")
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
        let backend = LocalBackend.LocalBackend() :> IQuantumBackend
        
        let results = [4; 5; 6] |> List.map (fun n -> QPE.estimateTGatePhase n backend)
        
        for result in results do
            match result with
            | Error err -> Assert.Fail($"QPE failed: {err}")
            | Ok r ->
                let expectedPhase = 1.0 / 8.0
                let error = abs (r.EstimatedPhase - expectedPhase)
                
                // Higher precision should give smaller error
                let maxError = 1.0 / float (1 <<< (r.Precision - 1))
                Assert.True(error <= maxError, 
                    $"With {r.Precision} qubits, expected error < {maxError}, got {error}")
    
    [<Fact>]
    let ``QPE rejects invalid qubit counts`` () =
        let backend = LocalBackend.LocalBackend() :> IQuantumBackend
        
        // Test with 0 qubits - should be rejected
        match QPE.estimateTGatePhase 0 backend with
        | Ok _ -> Assert.Fail("Should reject 0 counting qubits")
        | Error _ -> () // Expected error
    
    [<Fact>]
    let ``QPE estimates phase gate correctly`` () =
        // Test with custom phase gate: θ = π/3
        // U = e^(iπ/3) = e^(2πi·1/6)
        // Expected phase: φ = 1/6 ≈ 0.1667
        let backend = LocalBackend.LocalBackend() :> IQuantumBackend
        let theta = Math.PI / 3.0
        
        match QPE.estimatePhaseGate theta 4 backend with
        | Error err -> Assert.Fail($"QPE execution failed: {err}")
        | Ok result ->
            let expectedPhase = 1.0 / 6.0
            
            // With 4 qubits of precision, allow reasonable error
            let error = abs (result.EstimatedPhase - expectedPhase)
            Assert.True(error < 0.15, $"Expected phase ~{expectedPhase}, got {result.EstimatedPhase}")
    
    [<Fact>]
    let ``QPE returns valid gate count`` () =
        let backend = LocalBackend.LocalBackend() :> IQuantumBackend
        
        match QPE.estimateTGatePhase 3 backend with
        | Error err -> Assert.Fail($"QPE execution failed: {err}")
        | Ok result ->
            // QPE gate count = H gates + controlled-U gates + inverse QFT gates
            // Should have applied multiple gates
            Assert.True(result.GateCount >= 3, $"Expected at least 3 gates, got {result.GateCount}")
            Assert.True(result.GateCount < 100, $"Gate count seems too high: {result.GateCount}")
            // Total: 3 H + 1 X + 3 CP + 3 H + 3 CP + 1 SWAP = 14 gates
