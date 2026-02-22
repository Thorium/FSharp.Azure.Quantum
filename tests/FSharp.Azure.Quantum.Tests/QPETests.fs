namespace FSharp.Azure.Quantum.Tests

open Xunit
open FSharp.Azure.Quantum.Core
open FSharp.Azure.Quantum.Core.BackendAbstraction
open FSharp.Azure.Quantum.Backends
open System
open System.Threading
open System.Threading.Tasks

/// Tests for Quantum Phase Estimation (QPE) using unified backend
module QPE = FSharp.Azure.Quantum.Algorithms.QPE

[<Collection("NonParallel")>]
module QPETests =

    // ========================================================================
    // QPE PLANNER TESTS
    // ========================================================================

    type private NoQpeIntentBackend(inner: IQuantumBackend) =
        interface IQuantumBackend with
            member _.ExecuteToState circuit = inner.ExecuteToState circuit
            member _.NativeStateType = inner.NativeStateType
            member _.ApplyOperation operation state = inner.ApplyOperation operation state

            member _.SupportsOperation operation =
                match operation with
                | QuantumOperation.Algorithm (AlgorithmOperation.QPE _) -> false
                | _ -> inner.SupportsOperation operation

            member _.Name = inner.Name + " (no-qpe-intent)"
            member _.InitializeState numQubits = inner.InitializeState numQubits
            member this.ExecuteToStateAsync circuit ct =
                task { return (this :> IQuantumBackend).ExecuteToState circuit }
            member this.ApplyOperationAsync operation state ct =
                task { return (this :> IQuantumBackend).ApplyOperation operation state }

    [<Fact>]
    let ``QPE planner prefers algorithm intent when supported`` () =
        let backend = LocalBackend.LocalBackend() :> IQuantumBackend

        let config: QPE.QPEConfig =
            {
                CountingQubits = 3
                TargetQubits = 1
                UnitaryOperator = QPE.UnitaryOperator.TGate
                EigenVector = None
            }

        let intent: QPE.QpeExecutionIntent =
            {
                ApplyBitReversalSwaps = false
                Config = config
                Exactness = QPE.Exact
            }

        match QPE.plan backend intent with
        | Ok (QPE.QpePlan.ExecuteNatively _) -> Assert.True(true)
        | Ok _ -> Assert.Fail("Expected ExecuteNatively plan")
        | Error err -> Assert.Fail($"Planning failed: {err}")

    [<Fact>]
    let ``QPE planner produces explicit lowered ops when intent op unsupported`` () =
        let backend = LocalBackend.LocalBackend() |> NoQpeIntentBackend :> IQuantumBackend

        let config: QPE.QPEConfig =
            {
                CountingQubits = 3
                TargetQubits = 1
                UnitaryOperator = QPE.UnitaryOperator.TGate
                EigenVector = None
            }

        let intent: QPE.QpeExecutionIntent =
            {
                ApplyBitReversalSwaps = false
                Config = config
                Exactness = QPE.Exact
            }

        match QPE.plan backend intent with
        | Ok (QPE.QpePlan.ExecuteViaOps (ops, exactness)) ->
            Assert.Equal(QPE.Exact, exactness)
            Assert.NotEmpty ops
            Assert.True(ops |> List.forall backend.SupportsOperation)
        | Ok _ -> Assert.Fail("Expected ExecuteViaOps plan")
        | Error err -> Assert.Fail($"Planning failed: {err}")

    [<Fact>]
    let ``QPE planner uses Exactness to trim small controlled phases when lowering`` () =
        let backend = LocalBackend.LocalBackend() |> NoQpeIntentBackend :> IQuantumBackend

        // Use more counting qubits so inverse-QFT includes very small rotations.
        let config: QPE.QPEConfig =
            {
                CountingQubits = 8
                TargetQubits = 1
                UnitaryOperator = QPE.UnitaryOperator.TGate
                EigenVector = None
            }

        let exactIntent: QPE.QpeExecutionIntent =
            {
                ApplyBitReversalSwaps = false
                Config = config
                Exactness = QPE.Exact
            }

        let approxIntent: QPE.QpeExecutionIntent =
            {
                ApplyBitReversalSwaps = false
                Config = config
                Exactness = QPE.Approximate 0.2
            }

        match QPE.plan backend exactIntent, QPE.plan backend approxIntent with
        | Error err, _ -> Assert.Fail($"Exact planning failed: {err}")
        | _, Error err -> Assert.Fail($"Approx planning failed: {err}")
        | Ok (QPE.QpePlan.ExecuteViaOps (exactOps, _)), Ok (QPE.QpePlan.ExecuteViaOps (approxOps, _)) ->
            Assert.True(approxOps.Length < exactOps.Length, "Approximate exactness should produce fewer lowered ops")
        | Ok _, Ok _ -> Assert.Fail("Expected ExecuteViaOps plans")
    
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
