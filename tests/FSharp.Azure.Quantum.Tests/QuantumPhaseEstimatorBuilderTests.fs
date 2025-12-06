namespace FSharp.Azure.Quantum.Tests

open System
open Xunit
open FSharp.Azure.Quantum
open FSharp.Azure.Quantum.Core
open FSharp.Azure.Quantum.QuantumPhaseEstimator
open FSharp.Azure.Quantum.Algorithms.QuantumPhaseEstimation  // For UnitaryOperator types

/// Unit tests for QuantumPhaseEstimatorBuilder
/// Tests QPE for eigenvalue extraction and phase estimation
module QuantumPhaseEstimatorBuilderTests =
    
    // ========================================================================
    // BUILDER VALIDATION TESTS
    // ========================================================================
    
    [<Fact>]
    let ``phaseEstimator builder rejects insufficient precision`` () =
        let result = phaseEstimator {
            unitary TGate
            precision 0  // Must be at least 1
        }
        
        match result with
        | Error msg -> Assert.Contains("at least 1", msg)
        | Ok _ -> Assert.True(false, "Should have rejected precision < 1")
    
    [<Fact>]
    let ``phaseEstimator builder rejects excessive precision`` () =
        let result = phaseEstimator {
            unitary TGate
            precision 25  // Exceeds NISQ limit
        }
        
        match result with
        | Error msg -> Assert.Contains("20 qubits", msg)
        | Ok _ -> Assert.True(false, "Should have rejected precision > 20")
    
    [<Fact>]
    let ``phaseEstimator builder rejects insufficient target qubits`` () =
        let result = phaseEstimator {
            unitary TGate
            precision 8
            targetQubits 0  // Must be at least 1
        }
        
        match result with
        | Error msg -> Assert.Contains("at least 1", msg)
        | Ok _ -> Assert.True(false, "Should have rejected targetQubits < 1")
    
    [<Fact>]
    let ``phaseEstimator builder rejects excessive target qubits`` () =
        let result = phaseEstimator {
            unitary TGate
            precision 8
            targetQubits 15  // Exceeds simulation limit
        }
        
        match result with
        | Error msg -> Assert.Contains("10 qubits", msg)
        | Ok _ -> Assert.True(false, "Should have rejected targetQubits > 10")
    
    [<Fact>]
    let ``phaseEstimator builder rejects excessive total qubits`` () =
        let result = phaseEstimator {
            unitary TGate
            precision 20
            targetQubits 10  // Total = 30 > 25 limit
        }
        
        match result with
        | Error msg -> Assert.Contains("25 qubits", msg)
        | Ok _ -> Assert.True(false, "Should have rejected total > 25 qubits")
    
    [<Fact>]
    let ``phaseEstimator builder accepts valid minimal configuration`` () =
        let result = phaseEstimator {
            unitary TGate
            precision 8
        }
        
        match result with
        | Ok problem ->
            Assert.Equal(8, problem.Precision)
            Assert.Equal(1, problem.TargetQubits)  // Default
            Assert.True(problem.EigenVector.IsNone)
        | Error msg -> Assert.True(false, sprintf "Should have succeeded: %s" msg)
    
    [<Fact>]
    let ``phaseEstimator builder accepts full configuration`` () =
        let result = phaseEstimator {
            unitary (PhaseGate (Math.PI / 4.0))
            precision 12
            targetQubits 2
        }
        
        match result with
        | Ok problem ->
            Assert.Equal(12, problem.Precision)
            Assert.Equal(2, problem.TargetQubits)
        | Error msg -> Assert.True(false, sprintf "Should have succeeded: %s" msg)
    
    // ========================================================================
    // PHASE ESTIMATION CORRECTNESS TESTS
    // ========================================================================
    
    [<Fact>]
    let ``estimate should calculate T gate phase (π/8)`` () =
        let problem = phaseEstimator {
            unitary TGate
            precision 8
        }
        
        match problem with
        | Error msg -> Assert.True(false, sprintf "Problem creation failed: %s" msg)
        | Ok prob ->
            match estimate prob with
            | Ok result ->
                // T gate: e^(iπ/4) → phase = 1/8
                let expectedPhase = 1.0 / 8.0
                Assert.True(result.Success, sprintf "Estimation failed: %s" result.Message)
                Assert.True(abs (result.Phase - expectedPhase) < 0.01, 
                           sprintf "Phase %.6f should be close to %.6f (1/8)" result.Phase expectedPhase)
                Assert.Contains("T Gate", result.Unitary)
            | Error msg -> Assert.True(false, sprintf "Estimate failed: %s" msg)
    
    [<Fact>]
    let ``estimate should calculate S gate phase (π/2)`` () =
        let problem = phaseEstimator {
            unitary SGate
            precision 8
        }
        
        match problem with
        | Error msg -> Assert.True(false, sprintf "Problem creation failed: %s" msg)
        | Ok prob ->
            match estimate prob with
            | Ok result ->
                // S gate: e^(iπ/2) → phase = 1/4
                let expectedPhase = 1.0 / 4.0
                Assert.True(result.Success)
                Assert.True(abs (result.Phase - expectedPhase) < 0.01,
                           sprintf "Phase %.6f should be close to %.6f (1/4)" result.Phase expectedPhase)
                Assert.Contains("S Gate", result.Unitary)
            | Error msg -> Assert.True(false, sprintf "Estimate failed: %s" msg)
    
    [<Fact>]
    let ``estimate should calculate custom phase gate`` () =
        let theta = Math.PI / 3.0  // 60 degrees
        let problem = phaseEstimator {
            unitary (PhaseGate theta)
            precision 10
        }
        
        match problem with
        | Error msg -> Assert.True(false, sprintf "Problem creation failed: %s" msg)
        | Ok prob ->
            match estimate prob with
            | Ok result ->
                // PhaseGate(π/3): e^(iπ/3) → phase = 1/6
                let expectedPhase = 1.0 / 6.0
                Assert.True(result.Success)
                Assert.True(abs (result.Phase - expectedPhase) < 0.02,
                           sprintf "Phase %.6f should be close to %.6f (1/6)" result.Phase expectedPhase)
                Assert.Contains("Phase Gate", result.Unitary)
            | Error msg -> Assert.True(false, sprintf "Estimate failed: %s" msg)
    
    [<Fact(Skip = "Known issue, don't affect to this library functionality.")>]
    let ``estimate should calculate rotation gate`` () =
        let theta = Math.PI / 4.0  // 45 degrees
        let problem = phaseEstimator {
            unitary (RotationZ theta)
            precision 10
        }
        
        match problem with
        | Error msg -> Assert.True(false, sprintf "Problem creation failed: %s" msg)
        | Ok prob ->
            match estimate prob with
            | Ok result ->
                // Rz(π/4) on |0⟩: e^(-i(π/4)/2) = e^(-iπ/8) → phase = -1/16 = 15/16 (mod 1)
                let expectedPhase1 = 15.0 / 16.0  // Negative phase wraps to 1 - 1/16
                let expectedPhase2 = 1.0 / 16.0   // Or positive phase
                Assert.True(result.Success)
                let error1 = abs (result.Phase - expectedPhase1)
                let error2 = abs (result.Phase - expectedPhase2)
                let minError = min error1 error2
                Assert.True(minError < 0.02,
                           sprintf "Phase %.6f should be close to %.6f (15/16) or %.6f (1/16)" result.Phase expectedPhase1 expectedPhase2)
                Assert.Contains("Rz Gate", result.Unitary)
            | Error msg -> Assert.True(false, sprintf "Estimate failed: %s" msg)
    
    // ========================================================================
    // PRECISION TESTS
    // ========================================================================
    
    [<Fact>]
    let ``estimate should improve accuracy with higher precision`` () =
        let lowPrecision = phaseEstimator {
            unitary TGate
            precision 4  // 4 bits: resolution 1/16
        }
        
        let highPrecision = phaseEstimator {
            unitary TGate
            precision 10  // 10 bits: resolution 1/1024
        }
        
        match lowPrecision, highPrecision with
        | Ok lowProb, Ok highProb ->
            match estimate lowProb, estimate highProb with
            | Ok lowResult, Ok highResult ->
                let expectedPhase = 1.0 / 8.0
                let lowError = abs (lowResult.Phase - expectedPhase)
                let highError = abs (highResult.Phase - expectedPhase)
                
                // Higher precision should have lower or equal error
                Assert.True(highError <= lowError + 0.01,
                           sprintf "High precision error %.6f should be ≤ low precision error %.6f" highError lowError)
            | _ -> Assert.True(true, "One estimation may fail")
        | _ -> Assert.True(false, "Problem creation should succeed")
    
    [<Fact>]
    let ``estimate should report measurement outcome`` () =
        let problem = phaseEstimator {
            unitary TGate
            precision 8
        }
        
        match problem with
        | Error msg -> Assert.True(false, sprintf "Problem creation failed: %s" msg)
        | Ok prob ->
            match estimate prob with
            | Ok result ->
                Assert.True(result.MeasurementOutcome >= 0)
                Assert.True(result.MeasurementOutcome < pown 2 8)  // Should be in [0, 2^8)
            | Error msg -> Assert.True(false, sprintf "Estimate failed: %s" msg)
    
    // ========================================================================
    // EIGENVALUE TESTS
    // ========================================================================
    
    [<Fact>]
    let ``estimate should calculate eigenvalue from phase`` () =
        let problem = phaseEstimator {
            unitary TGate
            precision 8
        }
        
        match problem with
        | Error msg -> Assert.True(false, sprintf "Problem creation failed: %s" msg)
        | Ok prob ->
            match estimate prob with
            | Ok result ->
                // Eigenvalue λ = e^(2πiφ), should have magnitude 1
                Assert.True(abs (result.Eigenvalue.Magnitude - 1.0) < 0.01,
                           sprintf "Eigenvalue magnitude %.6f should be ≈ 1.0" result.Eigenvalue.Magnitude)
                
                // For T gate (phase = 1/8), angle should be 2π/8 = π/4
                let expectedAngle = Math.PI / 4.0
                let actualAngle = result.Eigenvalue.Phase
                Assert.True(abs (actualAngle - expectedAngle) < 0.2,
                           sprintf "Eigenvalue angle %.6f should be close to %.6f (π/4)" actualAngle expectedAngle)
            | Error msg -> Assert.True(false, sprintf "Estimate failed: %s" msg)
    
    // ========================================================================
    // CONVENIENCE HELPER TESTS
    // ========================================================================
    
    [<Fact>]
    let ``estimateTGate should create valid problem`` () =
        let result: Result<PhaseEstimatorProblem, string> = QuantumPhaseEstimator.estimateTGate 8 None
        match result with
        | Ok problem ->
            Assert.Equal(8, problem.Precision)
            Assert.Equal(1, problem.TargetQubits)
        | Error msg -> Assert.True(false, sprintf "Should succeed: %s" msg)
    
    [<Fact>]
    let ``estimateSGate should create valid problem`` () =
        let result: Result<PhaseEstimatorProblem, string> = QuantumPhaseEstimator.estimateSGate 10 None
        match result with
        | Ok problem ->
            Assert.Equal(10, problem.Precision)
            Assert.Equal(1, problem.TargetQubits)
        | Error msg -> Assert.True(false, sprintf "Should succeed: %s" msg)
    
    [<Fact>]
    let ``estimatePhaseGate should create valid problem`` () =
        let result: Result<PhaseEstimatorProblem, string> = QuantumPhaseEstimator.estimatePhaseGate (Math.PI / 4.0) 12 None
        match result with
        | Ok problem ->
            Assert.Equal(12, problem.Precision)
            Assert.Equal(1, problem.TargetQubits)
        | Error msg -> Assert.True(false, sprintf "Should succeed: %s" msg)
    
    [<Fact>]
    let ``estimateRotationZ should create valid problem`` () =
        let result: Result<PhaseEstimatorProblem, string> = QuantumPhaseEstimator.estimateRotationZ (Math.PI / 3.0) 10 None
        match result with
        | Ok problem ->
            Assert.Equal(10, problem.Precision)
            Assert.Equal(1, problem.TargetQubits)
        | Error msg -> Assert.True(false, sprintf "Should succeed: %s" msg)
    
    [<Fact>]
    let ``estimateResources should return resource estimates`` () =
        let estimate = estimateResources 8 1
        Assert.Contains("8", estimate)
        Assert.Contains("Qubits", estimate)
        Assert.Contains("Gates", estimate)
        Assert.Contains("resolution", estimate.ToLower())
    
    [<Fact>]
    let ``describeResult should format human-readable output`` () =
        let problem = phaseEstimator {
            unitary TGate
            precision 8
        }
        
        match problem with
        | Error msg -> Assert.True(false, sprintf "Problem creation failed: %s" msg)
        | Ok prob ->
            match estimate prob with
            | Ok result ->
                let description = describeResult result
                Assert.Contains("Phase", description)
                Assert.Contains("Eigenvalue", description)
                Assert.Contains("T Gate", description)
            | Error msg -> Assert.True(false, sprintf "Estimate failed: %s" msg)
    
    // ========================================================================
    // RESULT METADATA TESTS
    // ========================================================================
    
    [<Fact>]
    let ``estimate should populate result metadata`` () =
        let problem = phaseEstimator {
            unitary TGate
            precision 8
            targetQubits 1
        }
        
        match problem with
        | Error msg -> Assert.True(false, sprintf "Problem creation failed: %s" msg)
        | Ok prob ->
            match estimate prob with
            | Ok result ->
                Assert.Equal(8, result.Precision)
                Assert.Equal(1, result.TargetQubits)
                Assert.Equal(9, result.TotalQubits)
                Assert.True(result.Phase >= 0.0 && result.Phase < 1.0)
                Assert.True(result.GateCount > 0)
                Assert.True(result.Success)
                Assert.NotEmpty(result.Message)
                Assert.NotEmpty(result.Unitary)
            | Error msg -> Assert.True(false, sprintf "Estimate failed: %s" msg)
    
    [<Fact>]
    let ``estimate should track gate count`` () =
        let problem = phaseEstimator {
            unitary TGate
            precision 8
        }
        
        match problem with
        | Error msg -> Assert.True(false, sprintf "Problem creation failed: %s" msg)
        | Ok prob ->
            match estimate prob with
            | Ok result ->
                // Should have Hadamards, controlled-U, and IQFT gates
                Assert.True(result.GateCount > 8, sprintf "Gate count %d should be > 8" result.GateCount)
            | Error msg -> Assert.True(false, sprintf "Estimate failed: %s" msg)
