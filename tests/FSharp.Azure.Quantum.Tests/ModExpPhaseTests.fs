namespace FSharp.Azure.Quantum.Tests

open Xunit
open FSharp.Azure.Quantum.Core
open FSharp.Azure.Quantum.Core.BackendAbstraction
open FSharp.Azure.Quantum.Backends
open FSharp.Azure.Quantum.Algorithms

/// Tests for Shor.estimateModExpPhase (full quantum modular-exponentiation QPE)
/// and QPE rejection of ModularExponentiation unitary.
module ModExpPhaseTests =

    module Shor = FSharp.Azure.Quantum.Algorithms.Shor
    module QPE = FSharp.Azure.Quantum.Algorithms.QPE

    // ========================================================================
    // estimateModExpPhase VALIDATION TESTS
    // ========================================================================

    [<Fact>]
    let ``estimateModExpPhase rejects modulus < 2`` () =
        let bknd = LocalBackend.LocalBackend() :> IQuantumBackend
        match Shor.estimateModExpPhase 2 1 4 bknd with
        | Error (QuantumError.ValidationError ("modulus", _)) -> ()
        | Error err -> Assert.Fail($"Expected ValidationError for modulus, got: {err}")
        | Ok _ -> Assert.Fail("Expected error for modulus < 2")

    [<Fact>]
    let ``estimateModExpPhase rejects baseNum < 2`` () =
        let bknd = LocalBackend.LocalBackend() :> IQuantumBackend
        match Shor.estimateModExpPhase 1 7 4 bknd with
        | Error (QuantumError.ValidationError ("baseNum", _)) -> ()
        | Error err -> Assert.Fail($"Expected ValidationError for baseNum, got: {err}")
        | Ok _ -> Assert.Fail("Expected error for baseNum < 2")

    [<Fact>]
    let ``estimateModExpPhase rejects baseNum >= modulus`` () =
        let bknd = LocalBackend.LocalBackend() :> IQuantumBackend
        match Shor.estimateModExpPhase 7 7 4 bknd with
        | Error (QuantumError.ValidationError ("baseNum", _)) -> ()
        | Error err -> Assert.Fail($"Expected ValidationError for baseNum, got: {err}")
        | Ok _ -> Assert.Fail("Expected error for baseNum >= modulus")

    [<Fact>]
    let ``estimateModExpPhase rejects non-coprime baseNum`` () =
        let bknd = LocalBackend.LocalBackend() :> IQuantumBackend
        // 6 and 15 share factor 3 → gcd(6,15) = 3
        match Shor.estimateModExpPhase 6 15 4 bknd with
        | Error (QuantumError.ValidationError ("baseNum", msg)) ->
            Assert.Contains("coprime", msg)
        | Error err -> Assert.Fail($"Expected ValidationError for non-coprime, got: {err}")
        | Ok _ -> Assert.Fail("Expected error for non-coprime baseNum and modulus")

    [<Fact>]
    let ``estimateModExpPhase rejects zero countingQubits`` () =
        let bknd = LocalBackend.LocalBackend() :> IQuantumBackend
        match Shor.estimateModExpPhase 2 7 0 bknd with
        | Error (QuantumError.ValidationError ("countingQubits", _)) -> ()
        | Error err -> Assert.Fail($"Expected ValidationError for countingQubits, got: {err}")
        | Ok _ -> Assert.Fail("Expected error for zero countingQubits")

    [<Fact>]
    let ``estimateModExpPhase rejects negative countingQubits`` () =
        let bknd = LocalBackend.LocalBackend() :> IQuantumBackend
        match Shor.estimateModExpPhase 2 7 -1 bknd with
        | Error (QuantumError.ValidationError ("countingQubits", _)) -> ()
        | Error err -> Assert.Fail($"Expected ValidationError for countingQubits, got: {err}")
        | Ok _ -> Assert.Fail("Expected error for negative countingQubits")

    [<Fact>]
    let ``estimateModExpPhase rejects countingQubits > 16`` () =
        let bknd = LocalBackend.LocalBackend() :> IQuantumBackend
        match Shor.estimateModExpPhase 2 7 17 bknd with
        | Error (QuantumError.ValidationError ("countingQubits", msg)) ->
            Assert.Contains("16", msg)
        | Error err -> Assert.Fail($"Expected ValidationError for countingQubits > 16, got: {err}")
        | Ok _ -> Assert.Fail("Expected error for countingQubits > 16")

    [<Fact>]
    let ``estimateModExpPhase rejects exceeding 20-qubit limit`` () =
        let bknd = LocalBackend.LocalBackend() :> IQuantumBackend
        // N=15, n=4 bits, workspace = 2*4+4 = 12, so c > 8 would exceed 20
        match Shor.estimateModExpPhase 2 15 9 bknd with
        | Error (QuantumError.ValidationError ("totalQubits", msg)) ->
            Assert.Contains("20", msg)
        | Error err -> Assert.Fail($"Expected ValidationError for totalQubits, got: {err}")
        | Ok _ -> Assert.Fail("Expected error when total qubits exceed 20")

    // ========================================================================
    // estimateModExpPhase EXECUTION TESTS
    // ========================================================================

    // a=2 mod 5: period r=4 (2^1=2, 2^2=4, 2^3=3, 2^4=1 mod 5)
    // N=5, n=3, workspace = 2*3+4 = 10. For c=4: total = 14 qubits.
    // Possible phases: 0/4, 1/4, 2/4, 3/4 = 0.0, 0.25, 0.5, 0.75

    [<Fact>]
    let ``estimateModExpPhase a=2 mod 5 returns valid phase (c=4)`` () =
        let bknd = LocalBackend.LocalBackend() :> IQuantumBackend
        match Shor.estimateModExpPhase 2 5 4 bknd with
        | Error err -> Assert.Fail($"estimateModExpPhase failed: {err}")
        | Ok result ->
            // Phase must be s/r where r=4, so s/4 for some s in {0,1,2,3}
            // With 4 counting qubits (2^4=16 slots), the valid measurement
            // outcomes that yield phases s/4 are: 0, 4, 8, 12 → phases 0.0, 0.25, 0.5, 0.75
            let validPhases = [| 0.0; 0.25; 0.5; 0.75 |]
            let isValidPhase =
                validPhases |> Array.exists (fun expected ->
                    abs (result.EstimatedPhase - expected) < 0.001)
            Assert.True(isValidPhase,
                $"Phase {result.EstimatedPhase} not close to any valid s/4 phase: {validPhases}")
            Assert.Equal(4, result.CountingQubits)
            Assert.Equal(14, result.TotalQubits)
            Assert.Equal(4, result.ModularMultiplications)

    // a=3 mod 5: period r=4 (3^1=3, 3^2=4, 3^3=2, 3^4=1 mod 5)
    // Same qubit count as a=2 mod 5.

    [<Fact>]
    let ``estimateModExpPhase a=3 mod 5 returns valid phase (c=3)`` () =
        let bknd = LocalBackend.LocalBackend() :> IQuantumBackend
        // c=3: total = 3 + 2*3 + 4 = 13 qubits
        match Shor.estimateModExpPhase 3 5 3 bknd with
        | Error err -> Assert.Fail($"estimateModExpPhase failed: {err}")
        | Ok result ->
            // r=4, with 3 counting qubits (8 slots), valid phases s/4:
            // measurement outcomes 0, 2, 4, 6 → phases 0.0, 0.25, 0.5, 0.75
            let validPhases = [| 0.0; 0.25; 0.5; 0.75 |]
            let isValidPhase =
                validPhases |> Array.exists (fun expected ->
                    abs (result.EstimatedPhase - expected) < 0.001)
            Assert.True(isValidPhase,
                $"Phase {result.EstimatedPhase} not close to any valid s/4 phase: {validPhases}")
            Assert.Equal(3, result.CountingQubits)
            Assert.Equal(13, result.TotalQubits)

    // a=2 mod 7: period r=3 (2^1=2, 2^2=4, 2^3=1 mod 7)
    // N=7, n=3, workspace = 10. For c=4: total = 14 qubits.
    // Possible phases: s/3 for s in {0,1,2} = 0.0, 0.333..., 0.666...

    [<Fact>]
    let ``estimateModExpPhase a=2 mod 7 returns valid phase (c=4)`` () =
        let bknd = LocalBackend.LocalBackend() :> IQuantumBackend
        match Shor.estimateModExpPhase 2 7 4 bknd with
        | Error err -> Assert.Fail($"estimateModExpPhase failed: {err}")
        | Ok result ->
            // r=3, valid phases: 0/3, 1/3, 2/3
            // With c=4 counting qubits the exact phases 1/3, 2/3 are irrational fractions
            // of 2^4=16, so measurement will be approximate (closest integer/16).
            // 1/3 ≈ 5.33/16 → outcome 5 (phase=0.3125) or 6 (0.375)
            // 2/3 ≈ 10.67/16 → outcome 11 (0.6875) or 10 (0.625)
            // Allow tolerance for these approximations
            let validPhases = [| 0.0; 1.0/3.0; 2.0/3.0 |]
            let isNearValidPhase =
                validPhases |> Array.exists (fun expected ->
                    abs (result.EstimatedPhase - expected) < 0.1)
            Assert.True(isNearValidPhase,
                $"Phase {result.EstimatedPhase} not near any valid s/3 phase")
            Assert.Equal(4, result.CountingQubits)
            Assert.Equal(14, result.TotalQubits)

    [<Fact>]
    let ``estimateModExpPhase result fields are consistent`` () =
        let bknd = LocalBackend.LocalBackend() :> IQuantumBackend
        // a=2, N=5, c=2: total = 2 + 6 + 4 = 12 qubits
        match Shor.estimateModExpPhase 2 5 2 bknd with
        | Error err -> Assert.Fail($"estimateModExpPhase failed: {err}")
        | Ok result ->
            // Verify phase = measurementOutcome / 2^countingQubits
            let expectedPhase = float result.MeasurementOutcome / float (1 <<< result.CountingQubits)
            Assert.Equal(expectedPhase, result.EstimatedPhase, 10)
            // Verify measurement outcome is in valid range [0, 2^c)
            Assert.True(result.MeasurementOutcome >= 0)
            Assert.True(result.MeasurementOutcome < (1 <<< result.CountingQubits))
            Assert.Equal(2, result.CountingQubits)
            Assert.Equal(12, result.TotalQubits)
            Assert.Equal(2, result.ModularMultiplications)

    // ========================================================================
    // QPE REJECTS ModularExponentiation (plan and execute)
    // ========================================================================

    [<Fact>]
    let ``QPE plan rejects ModularExponentiation with descriptive error`` () =
        let bknd = LocalBackend.LocalBackend() :> IQuantumBackend
        let config: QPE.QPEConfig =
            {
                CountingQubits = 4
                TargetQubits = 3
                UnitaryOperator = QPE.UnitaryOperator.ModularExponentiation (2, 7)
                EigenVector = None
            }
        let intent: QPE.QpeExecutionIntent =
            {
                ApplyBitReversalSwaps = false
                Config = config
                Exactness = QPE.Exact
            }
        match QPE.plan bknd intent with
        | Error (QuantumError.OperationError ("QPE", msg)) ->
            Assert.Contains("ModularExponentiation", msg)
            Assert.Contains("Shor.estimateModExpPhase", msg)
        | Error err -> Assert.Fail($"Expected OperationError for QPE, got: {err}")
        | Ok _ -> Assert.Fail("Expected QPE plan to reject ModularExponentiation")

    [<Fact>]
    let ``QPE executeWithExactness rejects ModularExponentiation`` () =
        let bknd = LocalBackend.LocalBackend() :> IQuantumBackend
        let config: QPE.QPEConfig =
            {
                CountingQubits = 4
                TargetQubits = 3
                UnitaryOperator = QPE.UnitaryOperator.ModularExponentiation (2, 7)
                EigenVector = None
            }
        match QPE.executeWithExactness config bknd false QPE.Exact with
        | Error (QuantumError.OperationError ("QPE", msg)) ->
            Assert.Contains("ModularExponentiation", msg)
            Assert.Contains("Shor.estimateModExpPhase", msg)
        | Error err -> Assert.Fail($"Expected OperationError for QPE, got: {err}")
        | Ok _ -> Assert.Fail("Expected QPE execute to reject ModularExponentiation")

    [<Fact>]
    let ``QPE execute rejects ModularExponentiation`` () =
        let bknd = LocalBackend.LocalBackend() :> IQuantumBackend
        let config: QPE.QPEConfig =
            {
                CountingQubits = 4
                TargetQubits = 3
                UnitaryOperator = QPE.UnitaryOperator.ModularExponentiation (2, 7)
                EigenVector = None
            }
        match QPE.execute config bknd with
        | Error (QuantumError.OperationError ("QPE", msg)) ->
            Assert.Contains("ModularExponentiation", msg)
        | Error err -> Assert.Fail($"Expected OperationError for QPE, got: {err}")
        | Ok _ -> Assert.Fail("Expected QPE execute to reject ModularExponentiation")
