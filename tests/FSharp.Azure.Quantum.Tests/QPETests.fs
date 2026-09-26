namespace FSharp.Azure.Quantum.Tests

open Xunit
open FSharp.Azure.Quantum.Core
open FSharp.Azure.Quantum.Core.BackendAbstraction
open FSharp.Azure.Quantum.Backends
open System
open System.Numerics
open System.Threading
open System.Threading.Tasks

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
                | QuantumOperation.Algorithm(AlgorithmOperation.QPE _) -> false
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
        | Ok(QPE.QpePlan.ExecuteNatively _) -> Assert.True(true)
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
        | Ok(QPE.QpePlan.ExecuteViaOps(ops, exactness)) ->
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
        | Ok(QPE.QpePlan.ExecuteViaOps(exactOps, _)), Ok(QPE.QpePlan.ExecuteViaOps(approxOps, _)) ->
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

        let results = [ 4; 5; 6 ] |> List.map (fun n -> QPE.estimateTGatePhase n backend)

        for result in results do
            match result with
            | Error err -> Assert.Fail($"QPE failed: {err}")
            | Ok r ->
                let expectedPhase = 1.0 / 8.0
                let error = abs (r.EstimatedPhase - expectedPhase)

                // Higher precision should give smaller error
                let maxError = 1.0 / float (1 <<< (r.Precision - 1))
                Assert.True(error <= maxError, $"With {r.Precision} qubits, expected error < {maxError}, got {error}")

    [<Fact>]
    let ``QPE rejects invalid qubit counts`` () =
        let backend = LocalBackend.LocalBackend() :> IQuantumBackend

        // Test with 0 qubits - should be rejected
        (QPE.estimateTGatePhase 0 backend)
        |> Result.iter (fun _ -> Assert.Fail("Should reject 0 counting qubits")) // Expected error

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

    // ========================================================================
    // CUSTOM EIGENVECTOR SUPPORT
    // ========================================================================

    let private stateVectorOf (amplitudes: Complex[]) : QuantumState =
        QuantumState.StateVector(FSharp.Azure.Quantum.LocalSimulator.StateVector.create amplitudes)

    [<Fact>]
    let ``QPE with explicit |1> eigenvector matches default behaviour`` () =
        // PhaseGate(π/2) with eigenvector |1⟩: phase φ = 1/4
        let backend = LocalBackend.LocalBackend() :> IQuantumBackend

        let config: QPE.QPEConfig =
            {
                CountingQubits = 3
                TargetQubits = 1
                UnitaryOperator = QPE.UnitaryOperator.PhaseGate(Math.PI / 2.0)
                EigenVector = Some(stateVectorOf [| Complex.Zero; Complex.One |])
            }

        match QPE.execute config backend with
        | Error err -> Assert.Fail($"QPE with custom eigenvector failed: {err}")
        | Ok result ->
            let error = abs (result.EstimatedPhase - 0.25)
            Assert.True(error < 0.15, $"Expected phase ~0.25, got {result.EstimatedPhase}")

    [<Fact>]
    let ``QPE with |0> eigenvector estimates zero phase`` () =
        // PhaseGate leaves |0⟩ unchanged (eigenvalue 1): phase φ = 0
        let backend = LocalBackend.LocalBackend() :> IQuantumBackend

        let config: QPE.QPEConfig =
            {
                CountingQubits = 3
                TargetQubits = 1
                UnitaryOperator = QPE.UnitaryOperator.PhaseGate(Math.PI / 2.0)
                EigenVector = Some(stateVectorOf [| Complex.One; Complex.Zero |])
            }

        (QPE.execute config backend)
        |> Result.map (fun result ->
            Assert.True(result.EstimatedPhase < 0.1, $"Expected phase ~0, got {result.EstimatedPhase}"))
        |> Result.defaultWith (fun err -> Assert.Fail($"QPE with |0> eigenvector failed: {err}"))

    [<Fact>]
    let ``QPE rejects eigenvector with wrong dimension`` () =
        let backend = LocalBackend.LocalBackend() :> IQuantumBackend

        let config: QPE.QPEConfig =
            {
                CountingQubits = 3
                TargetQubits = 1
                UnitaryOperator = QPE.UnitaryOperator.TGate
                // 4 amplitudes = 2 qubits, but TargetQubits = 1
                EigenVector = Some(stateVectorOf (Array.create 4 (Complex(0.5, 0.0))))
            }

        match QPE.execute config backend with
        | Error(QuantumError.ValidationError("EigenVector", _)) -> ()
        | Error err -> Assert.Fail($"Expected EigenVector validation error, got: {err}")
        | Ok _ -> Assert.Fail("Expected validation error for mismatched eigenvector dimension")

    // Cloud-style backend: rejects incremental ApplyOperation (like real hardware) and only runs
    // COMPLETE circuits via ExecuteToState (delegated to a local simulator to stand in for a job).
    type private CloudStyleBackend(inner: IQuantumBackend) =
        let mutable executeCalls = 0

        let incremental: Result<QuantumState, QuantumError> =
            Error(
                QuantumError.OperationError(
                    "ApplyOperation",
                    "CloudStyle does not support incremental ApplyOperation. Use ExecuteToState with a complete circuit instead."
                )
            )

        member _.ExecuteToStateCalls = executeCalls

        interface IQuantumBackend with
            member _.ExecuteToState circuit =
                executeCalls <- executeCalls + 1
                inner.ExecuteToState circuit

            member _.NativeStateType = inner.NativeStateType
            member _.ApplyOperation _operation _state = incremental
            member _.SupportsOperation _operation = true
            member _.Name = inner.Name + " (cloud-style)"
            member _.InitializeState numQubits = inner.InitializeState numQubits

            member this.ExecuteToStateAsync circuit _ct =
                task { return (this :> IQuantumBackend).ExecuteToState circuit }

            member _.ApplyOperationAsync _operation _state _ct = Task.FromResult(incremental)

    [<Fact>]
    let ``QPE runs on a cloud-style backend via whole-circuit submission`` () =
        let cloud = CloudStyleBackend(LocalBackend.LocalBackend() :> IQuantumBackend)

        let config: QPE.QPEConfig =
            {
                CountingQubits = 3
                TargetQubits = 1
                UnitaryOperator = QPE.UnitaryOperator.TGate
                EigenVector = None
            }

        match QPE.execute config (cloud :> IQuantumBackend) with
        | Ok _ ->
            Assert.True(
                cloud.ExecuteToStateCalls > 0,
                "QPE should submit a complete circuit via ExecuteToState on a cloud-style backend"
            )
        | Error err -> Assert.Fail($"Cloud-style QPE failed: {err}")

    [<Fact>]
    let ``QPE on modular exponentiation runs on a cloud-style backend via whole-circuit submission`` () =
        // Regression. A cloud-style backend claims every operation, so plan() hands it the
        // modular-exponentiation intent natively; ApplyOperation then refuses incremental
        // execution and QPE falls back to whole-circuit submission. That fallback called
        // the single-qubit lowering, whose ModularExponentiation arm was a `failwith`
        // justified as "plan() rejects it first" — an invariant that stopped holding the
        // day plan() learned to lower modular exponentiation. Every cloud backend and the
        // noisy local one threw here instead of returning a Result.
        let cloud = CloudStyleBackend(LocalBackend.LocalBackend() :> IQuantumBackend)

        let config: QPE.QPEConfig =
            {
                CountingQubits = 3
                TargetQubits = 4 // ceil(log2 15)
                UnitaryOperator = QPE.UnitaryOperator.ModularExponentiation(7, 15)
                EigenVector = None
            }

        match QPE.execute config (cloud :> IQuantumBackend) with
        | Ok result ->
            Assert.True(
                cloud.ExecuteToStateCalls > 0,
                "modular-exponentiation QPE should submit a complete circuit via ExecuteToState on a cloud-style backend"
            )

            // ord(7 mod 15) = 4, so the measured phase sits on a multiple of 1/4.
            let scaled = result.EstimatedPhase * 4.0

            Assert.True(
                abs (scaled - System.Math.Round scaled) < 1e-9,
                $"phase {result.EstimatedPhase} is not a multiple of 1/4; the whole-circuit lowering is wrong"
            )
        | Error err -> Assert.Fail($"Cloud-style modular-exponentiation QPE failed: {err}")

    [<Fact>]
    let ``QPE refuses Approximate exactness for modular exponentiation`` () =
        // The lowering builds an exact inverse QFT and the native handlers transform
        // exactly, so Approximate had nothing to act on and was silently ignored — while
        // Shor refuses the identical input. Same input, same answer.
        let backend = LocalBackend.LocalBackend() :> IQuantumBackend

        let config: QPE.QPEConfig =
            {
                CountingQubits = 3
                TargetQubits = 4
                UnitaryOperator = QPE.UnitaryOperator.ModularExponentiation(7, 15)
                EigenVector = None
            }

        match QPE.executeWithExactness config backend false (QPE.Approximate 0.01) with
        | Error(QuantumError.ValidationError("Exactness", message)) -> Assert.Contains("Approximate", message)
        | Error err -> Assert.Fail($"Expected an Exactness ValidationError, got: {err}")
        | Ok _ -> Assert.Fail("Should refuse Approximate for modular exponentiation, as Shor does")

    [<Fact>]
    let ``single-qubit QPE runs on the noisy local backend via whole-circuit submission`` () =
        // Regression for a pre-existing defect the modular-exponentiation crash exposed.
        // NoisyLocalBackend handed out a StateVector while declaring NativeStateType =
        // Mixed, so applySequence failed converting the initial state before the
        // incremental-unsupported fallback could route the algorithm to ExecuteToState —
        // no applySequence-based algorithm had ever run here. It now initialises as
        // |0..0><0..0| in its own representation, and isZeroState admits that state.
        //
        // T on |1> has eigenphase pi/4, i.e. phi = 1/8, which a 3-qubit counting register
        // resolves exactly. Noiseless, so the assertion is deterministic.
        let backend =
            DensityMatrixSimulator.NoisyLocalBackend(DensityMatrixSimulator.noiseless) :> IQuantumBackend

        let config: QPE.QPEConfig =
            {
                CountingQubits = 3
                TargetQubits = 1
                UnitaryOperator = QPE.UnitaryOperator.TGate
                EigenVector = None
            }

        match QPE.execute config backend with
        | Ok result ->
            Assert.True(
                abs (result.EstimatedPhase - 0.125) < 1e-9,
                $"expected phase 1/8 for T, got {result.EstimatedPhase} on the noisy backend"
            )
        | Error err -> Assert.Fail($"Single-qubit QPE failed on the noisy backend: {err}")

    [<Fact>]
    let ``QPE on modular exponentiation is refused by the noisy backend's qubit cap, not crashed`` () =
        // The whole fallback path now runs on this backend — plan, lowered ops, incremental
        // refusal, gates form, ExecuteToState — and stops only at the density-matrix
        // simulator's own 8-qubit cap. No modular-exponentiation instance can ever fit: the
        // Beauregard workspace alone is 2n + 4 >= 8, before any counting qubit. So on this
        // backend the honest answer is that ValidationError. It used to be a thrown
        // exception from the lowering, and before that a conversion failure.
        let backend =
            DensityMatrixSimulator.NoisyLocalBackend(DensityMatrixSimulator.noiseless) :> IQuantumBackend

        let config: QPE.QPEConfig =
            {
                CountingQubits = 2
                TargetQubits = 2 // ceil(log2 3); the smallest instance is still 10 qubits
                UnitaryOperator = QPE.UnitaryOperator.ModularExponentiation(2, 3)
                EigenVector = None
            }

        match QPE.execute config backend with
        | Error(QuantumError.ValidationError("numQubits", message)) -> Assert.Contains("8 qubits", message)
        | Error err -> Assert.Fail($"Expected the density-matrix qubit-cap ValidationError, got: {err}")
        | Ok _ -> Assert.Fail("A 10-qubit density-matrix simulation cannot succeed under an 8-qubit cap")
