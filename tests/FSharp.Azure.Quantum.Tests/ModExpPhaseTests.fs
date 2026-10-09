namespace FSharp.Azure.Quantum.Tests

open Xunit
open FSharp.Azure.Quantum.Core
open FSharp.Azure.Quantum.Core.BackendAbstraction
open FSharp.Azure.Quantum.Backends
open FSharp.Azure.Quantum.Algorithms
open FSharp.Azure.Quantum.LocalSimulator

/// Tests for Shor.estimateModExpPhase (full quantum modular-exponentiation QPE)
/// and QPE rejection of ModularExponentiation unitary.
module ModExpPhaseTests =

    module Shor = FSharp.Azure.Quantum.Algorithms.Shor
    module QPE = FSharp.Azure.Quantum.Algorithms.QPE

    [<Literal>]
    let private probeName = "width-probe"

    // ========================================================================
    // estimateModExpPhase VALIDATION TESTS
    // ========================================================================

    [<Fact>]
    let ``estimateModExpPhase rejects modulus < 2`` () =
        let bknd = LocalBackend.LocalBackend() :> IQuantumBackend

        match Shor.estimateModExpPhase 2 1 4 bknd with
        | Error(QuantumError.ValidationError("modulus", _)) -> ()
        | Error err -> Assert.Fail($"Expected ValidationError for modulus, got: {err}")
        | Ok _ -> Assert.Fail("Expected error for modulus < 2")

    [<Fact>]
    let ``estimateModExpPhase rejects baseNum < 2`` () =
        let bknd = LocalBackend.LocalBackend() :> IQuantumBackend

        match Shor.estimateModExpPhase 1 7 4 bknd with
        | Error(QuantumError.ValidationError("baseNum", _)) -> ()
        | Error err -> Assert.Fail($"Expected ValidationError for baseNum, got: {err}")
        | Ok _ -> Assert.Fail("Expected error for baseNum < 2")

    [<Fact>]
    let ``estimateModExpPhase rejects baseNum >= modulus`` () =
        let bknd = LocalBackend.LocalBackend() :> IQuantumBackend

        match Shor.estimateModExpPhase 7 7 4 bknd with
        | Error(QuantumError.ValidationError("baseNum", _)) -> ()
        | Error err -> Assert.Fail($"Expected ValidationError for baseNum, got: {err}")
        | Ok _ -> Assert.Fail("Expected error for baseNum >= modulus")

    [<Fact>]
    let ``estimateModExpPhase rejects non-coprime baseNum`` () =
        let bknd = LocalBackend.LocalBackend() :> IQuantumBackend
        // 6 and 15 share factor 3 → gcd(6,15) = 3
        match Shor.estimateModExpPhase 6 15 4 bknd with
        | Error(QuantumError.ValidationError("baseNum", msg)) -> Assert.Contains("coprime", msg)
        | Error err -> Assert.Fail($"Expected ValidationError for non-coprime, got: {err}")
        | Ok _ -> Assert.Fail("Expected error for non-coprime baseNum and modulus")

    [<Fact>]
    let ``estimateModExpPhase rejects zero countingQubits`` () =
        let bknd = LocalBackend.LocalBackend() :> IQuantumBackend

        match Shor.estimateModExpPhase 2 7 0 bknd with
        | Error(QuantumError.ValidationError("countingQubits", _)) -> ()
        | Error err -> Assert.Fail($"Expected ValidationError for countingQubits, got: {err}")
        | Ok _ -> Assert.Fail("Expected error for zero countingQubits")

    [<Fact>]
    let ``estimateModExpPhase rejects negative countingQubits`` () =
        let bknd = LocalBackend.LocalBackend() :> IQuantumBackend

        match Shor.estimateModExpPhase 2 7 -1 bknd with
        | Error(QuantumError.ValidationError("countingQubits", _)) -> ()
        | Error err -> Assert.Fail($"Expected ValidationError for countingQubits, got: {err}")
        | Ok _ -> Assert.Fail("Expected error for negative countingQubits")

    [<Fact>]
    let ``estimateModExpPhase rejects countingQubits above QPE.MaxCountingQubits`` () =
        let probe = CapacityStubBackend(probeName, None)

        match Shor.estimateModExpPhase 2 7 (QPE.MaxCountingQubits + 1) probe with
        | Error(QuantumError.ValidationError("countingQubits", msg)) ->
            Assert.Contains(string QPE.MaxCountingQubits, msg)
        | Error err -> Assert.Fail($"Expected ValidationError for countingQubits, got: {err}")
        | Ok _ -> Assert.Fail("Expected error for countingQubits above QPE.MaxCountingQubits")

        Assert.Empty(probe.Widths)

    [<Fact>]
    let ``estimateModExpPhase passes 17 counting qubits to a backend that declares no limit`` () =
        // 17 + 2·3 + 4 = 27 qubits for N=7. The probe runs nothing; reaching it shows
        // validation did not refuse the width.
        let probe = CapacityStubBackend(probeName, None)

        match Shor.estimateModExpPhase 2 7 17 probe with
        | Error(QuantumError.ValidationError(param, msg)) -> Assert.Fail($"Validation refused it: {param}: {msg}")
        | Error _ -> ()
        | Ok _ -> Assert.Fail("The probe backend runs nothing")

        Assert.Equal<int list>([ 27 ], probe.Widths)

    [<Fact>]
    let ``estimateModExpPhase refuses a circuit wider than the backend runs, naming the backend`` () =
        let probe = CapacityStubBackend(probeName, Some 26)

        match Shor.estimateModExpPhase 2 7 17 probe with
        | Error(QuantumError.ValidationError("totalQubits", msg)) ->
            Assert.Contains("27", msg)
            Assert.Contains("26", msg)
            Assert.Contains(probeName, msg)
        | Error err -> Assert.Fail($"Expected ValidationError for totalQubits, got: {err}")
        | Ok _ -> Assert.Fail("Expected error when total qubits exceed the backend limit")

        Assert.Empty(probe.Widths)

    [<Fact>]
    let ``estimateModExpPhase rejects exceeding the local simulator's runnable qubits`` () =
        let bknd = LocalBackend.LocalBackend() :> IQuantumBackend
        let budget = StateVector.practicalCircuitQubits

        // N=7 has a 3-bit register, so the circuit is counting + 10 wide: one counting qubit
        // past budget - 10 overruns the local simulator's runnable qubits. The budget is
        // wall-clock, not capacity, and tracks FSAQ_MAX_CIRCUIT_QUBITS.
        let countingQubits = budget - 10 + 1

        match Shor.estimateModExpPhase 2 7 countingQubits bknd with
        | Error(QuantumError.ValidationError("totalQubits", msg)) ->
            Assert.Contains(string budget, msg)
            Assert.Contains(bknd.Name, msg)
            Assert.Contains("FSAQ_MAX_CIRCUIT_QUBITS", msg)
        | Error err -> Assert.Fail($"Expected ValidationError for totalQubits, got: {err}")
        | Ok _ -> Assert.Fail($"Expected error when total qubits exceed {budget}")

    // ========================================================================
    // BACKEND-DECLARED LIMITS: Shor planning and QPE validation
    // ========================================================================

    // 1003 = 17 · 59, odd and composite, above the N ≤ 1000 bound Shor once had; its 10-bit
    // register makes the circuit counting + 24 wide.
    let private shorConfig1003 precision : ShorsTypes.ShorsConfig =
        {
            NumberToFactor = 1003
            RandomBase = Some 2
            PrecisionQubits = precision
            MaxAttempts = 1
        }

    let private shorIntent config : Shor.ShorExecutionIntent =
        {
            Config = config
            Exactness = QPE.Exactness.Exact
        }

    [<Fact>]
    let ``Shor plans N above 1000 on a backend that declares no limit`` () =
        let probe = CapacityStubBackend(probeName, None)

        match Shor.plan probe (shorIntent (shorConfig1003 20)) with
        | Ok(Shor.ShorPlan.ExecuteQuantum(baseNum, modulus, precision, _, _, _)) ->
            Assert.Equal(2, baseNum)
            Assert.Equal(1003, modulus)
            Assert.Equal(20, precision)
        | other -> Assert.Fail($"Expected a quantum plan, got: {other}")

        Assert.Empty(probe.Widths)

    [<Fact>]
    let ``Shor refuses N whose circuit does not fit the backend, naming the backend`` () =
        let probe = CapacityStubBackend(probeName, Some 30)

        match Shor.plan probe (shorIntent (shorConfig1003 10)) with
        | Error(QuantumError.ValidationError("n", msg)) ->
            Assert.Contains("N=1003", msg)
            Assert.Contains(probeName, msg)
            Assert.Contains("30", msg)
        | other -> Assert.Fail($"Expected the unfittable-circuit error, got: {other}")

        match Shor.findPeriodQuantum 2 1003 10 probe with
        | Error(QuantumError.ValidationError("n", msg)) -> Assert.Contains(probeName, msg)
        | other -> Assert.Fail($"Expected the unfittable-circuit error, got: {other}")

        Assert.Empty(probe.Widths)

    [<Fact>]
    let ``Shor plans PrecisionQubits above the backend limit when a register's worth of counting fits`` () =
        // N = 15: 4 register bits, so the Beauregard circuit takes 2·4 + 4 = 12 qubits beside
        // the counting register. 20 qubits leave 8 counting qubits for the 13 asked; 12 leave none.
        let config =
            { shorConfig1003 13 with
                NumberToFactor = 15
            }

        let roomy = CapacityStubBackend(probeName, Some 20)

        match Shor.plan roomy (shorIntent config) with
        | Ok(Shor.ShorPlan.ExecuteQuantum(_, modulus, precision, _, _, _)) ->
            Assert.Equal(15, modulus)
            Assert.Equal(13, precision)
        | other -> Assert.Fail($"Expected a quantum plan, got: {other}")

        match Shor.plan (CapacityStubBackend(probeName, Some 12)) (shorIntent config) with
        | Error(QuantumError.ValidationError("n", msg)) ->
            Assert.Contains("12", msg)
            Assert.Contains(probeName, msg)
        | other -> Assert.Fail($"Expected the unfittable-circuit error, got: {other}")

        Assert.Empty(roomy.Widths)

    [<Fact>]
    let ``Shor refuses N above MaxNumberToFactor whatever the backend`` () =
        let config =
            { shorConfig1003 8 with
                NumberToFactor = Shor.MaxNumberToFactor + 1
            }

        match Shor.plan (CapacityStubBackend(probeName, None)) (shorIntent config) with
        | Error(QuantumError.ValidationError("NumberToFactor", msg)) ->
            Assert.Contains(string Shor.MaxNumberToFactor, msg)
        | other -> Assert.Fail($"Expected a NumberToFactor error, got: {other}")

    let private tGateConfig countingQubits : QPE.QPEConfig =
        {
            CountingQubits = countingQubits
            TargetQubits = 1
            UnitaryOperator = QPE.UnitaryOperator.TGate
            EigenVector = None
        }

    [<Fact>]
    let ``QPE passes 21 counting qubits to a backend that declares no limit`` () =
        let probe = CapacityStubBackend(probeName, None)

        match QPE.execute (tGateConfig 21) probe with
        | Error(QuantumError.ValidationError(param, msg)) -> Assert.Fail($"Validation refused it: {param}: {msg}")
        | Error _ -> ()
        | Ok _ -> Assert.Fail("The probe backend runs nothing")

        Assert.Equal<int list>([ 22 ], probe.Widths)

    [<Fact>]
    let ``QPE refuses a state wider than the backend runs, naming the backend`` () =
        let probe = CapacityStubBackend(probeName, Some 12)

        match QPE.execute (tGateConfig 12) probe with
        | Error(QuantumError.ValidationError("CountingQubits", msg)) ->
            Assert.Contains("13", msg)
            Assert.Contains("12", msg)
            Assert.Contains(probeName, msg)
        | other -> Assert.Fail($"Expected a CountingQubits error, got: {other}")

        Assert.Empty(probe.Widths)

    [<Fact>]
    let ``QPE refuses CountingQubits above QPE.MaxCountingQubits`` () =
        match QPE.execute (tGateConfig (QPE.MaxCountingQubits + 1)) (CapacityStubBackend(probeName, None)) with
        | Error(QuantumError.ValidationError("CountingQubits", msg)) ->
            Assert.Contains(string QPE.MaxCountingQubits, msg)
        | other -> Assert.Fail($"Expected a CountingQubits error, got: {other}")

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
                validPhases
                |> Array.exists (fun expected -> abs (result.EstimatedPhase - expected) < 0.001)

            Assert.True(isValidPhase, $"Phase {result.EstimatedPhase} not close to any valid s/4 phase: {validPhases}")
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
                validPhases
                |> Array.exists (fun expected -> abs (result.EstimatedPhase - expected) < 0.001)

            Assert.True(isValidPhase, $"Phase {result.EstimatedPhase} not close to any valid s/4 phase: {validPhases}")
            Assert.Equal(3, result.CountingQubits)
            Assert.Equal(13, result.TotalQubits)

    // a=2 mod 7: period r=3 (2^1=2, 2^2=4, 2^3=1 mod 7)
    // N=7, n=3, workspace = 10. For c=4: total = 14 qubits.
    // Possible phases: s/3 for s in {0,1,2} = 0.0, 0.333..., 0.666...

    [<Fact>]
    let ``estimateModExpPhase a=2 mod 7 returns valid phase (c=4)`` () =
        let bknd = LocalBackend.LocalBackend() :> IQuantumBackend
        // QPE with non-exact binary fractions (1/3, 2/3 don't fit in 4 bits) is
        // inherently probabilistic. Retry up to 5 times — any single success suffices.
        let validPhases = [| 0.0; 1.0 / 3.0; 2.0 / 3.0 |]
        let maxAttempts = 5
        let mutable succeeded = false
        let mutable lastPhase = 0.0
        let mutable lastError = ""

        for _ in 1..maxAttempts do
            if not succeeded then
                match Shor.estimateModExpPhase 2 7 4 bknd with
                | Error err -> lastError <- $"{err}"
                | Ok result ->
                    lastPhase <- result.EstimatedPhase

                    let isNearValidPhase =
                        validPhases
                        |> Array.exists (fun expected -> abs (result.EstimatedPhase - expected) < 0.1)

                    if isNearValidPhase then
                        succeeded <- true
                        Assert.Equal(4, result.CountingQubits)
                        Assert.Equal(14, result.TotalQubits)

        if not succeeded then
            if lastError <> "" then
                Assert.Fail($"estimateModExpPhase failed after {maxAttempts} attempts: {lastError}")
            else
                Assert.Fail($"Phase {lastPhase} not near any valid s/3 phase after {maxAttempts} attempts")

    [<Fact>]
    let ``estimateModExpPhase result fields are consistent`` () =
        let bknd = LocalBackend.LocalBackend() :> IQuantumBackend
        // a=2, N=5, c=2: total = 2 + 6 + 4 = 12 qubits
        match Shor.estimateModExpPhase 2 5 2 bknd with
        | Error err -> Assert.Fail($"estimateModExpPhase failed: {err}")
        | Ok result ->
            // Verify phase = measurementOutcome / 2^countingQubits
            let expectedPhase =
                float result.MeasurementOutcome / float (1 <<< result.CountingQubits)

            Assert.Equal(expectedPhase, result.EstimatedPhase, 10)
            // Verify measurement outcome is in valid range [0, 2^c)
            Assert.True(result.MeasurementOutcome >= 0)
            Assert.True(result.MeasurementOutcome < (1 <<< result.CountingQubits))
            Assert.Equal(2, result.CountingQubits)
            Assert.Equal(12, result.TotalQubits)
            Assert.Equal(2, result.ModularMultiplications)

    // ========================================================================
    // QPE HANDLES ModularExponentiation (plan and execute)
    // ========================================================================
    //
    // These three tests asserted the opposite until the Beauregard circuit became
    // available as operations. QPE could not lower a ModularExponentiation unitary, so
    // plan and execute both refused it and pointed callers at Shor.estimateModExpPhase —
    // which meant Shor's period finding was the one algorithm that could not go through
    // the unified backend path, and a non-gate backend had to reimplement it.
    //
    // ord(2 mod 7) = 3, so the phase is s/3 and a 4-qubit counting register cannot
    // represent it exactly. These assert the mechanism, not a particular phase.

    let private modExpConfig: QPE.QPEConfig =
        {
            CountingQubits = 4
            TargetQubits = 3
            UnitaryOperator = QPE.UnitaryOperator.ModularExponentiation(2, 7)
            EigenVector = None
        }

    [<Fact>]
    let ``QPE plan lowers ModularExponentiation to the Beauregard circuit`` () =
        let bknd = LocalBackend.LocalBackend() :> IQuantumBackend

        let intent: QPE.QpeExecutionIntent =
            {
                ApplyBitReversalSwaps = false
                Config = modExpConfig
                Exactness = QPE.Exact
            }

        match QPE.plan bknd intent with
        | Ok(QPE.QpePlan.ExecuteViaOps(ops, exactness)) ->
            Assert.Equal(QPE.Exact, exactness)
            Assert.NotEmpty ops
            // Every lowered operation must be something the backend can actually run,
            // otherwise the plan is a promise it cannot keep.
            Assert.True(ops |> List.forall bknd.SupportsOperation)
        | Ok(QPE.QpePlan.ExecuteNatively _) ->
            Assert.Fail("LocalBackend has no native modular-exponentiation QPE; planning one would fail at execution")
        | Error err -> Assert.Fail($"Expected a lowered plan, got: {err}")

    [<Fact>]
    let ``QPE executeWithExactness runs ModularExponentiation`` () =
        let bknd = LocalBackend.LocalBackend() :> IQuantumBackend

        match QPE.executeWithExactness modExpConfig bknd false QPE.Exact with
        | Ok result ->
            Assert.Equal(4, result.Precision)
            Assert.True(result.MeasurementOutcome < (1 <<< 4))
            Assert.InRange(result.EstimatedPhase, 0.0, 1.0)
        | Error err -> Assert.Fail($"Expected modular-exponentiation QPE to run, got: {err}")

    [<Fact>]
    let ``QPE execute runs ModularExponentiation`` () =
        let bknd = LocalBackend.LocalBackend() :> IQuantumBackend

        match QPE.execute modExpConfig bknd with
        | Ok result ->
            // Total qubits must cover the arithmetic workspace (counting + 2n + 4), not just
            // the counting and target registers — sizing it as 4 + 3 would leave the
            // Beauregard circuit nowhere to put its temp register and ancilla chain.
            Assert.Equal(ModularExponentiationCircuit.totalQubitsFor 7 4, QuantumState.numQubits result.FinalState)
        | Error err -> Assert.Fail($"Expected modular-exponentiation QPE to run, got: {err}")

    [<Fact>]
    let ``QPE rejects a target register that does not match the modulus`` () =
        // The lowering derives the register width from the modulus, so a disagreeing
        // TargetQubits would be ignored rather than honoured — the caller would get a
        // correct answer to a different question than the one they asked.
        let bknd = LocalBackend.LocalBackend() :> IQuantumBackend

        let intent: QPE.QpeExecutionIntent =
            {
                ApplyBitReversalSwaps = false
                Config = { modExpConfig with TargetQubits = 5 } // mod 7 needs 3
                Exactness = QPE.Exact
            }

        match QPE.plan bknd intent with
        | Error(QuantumError.ValidationError("TargetQubits", message)) -> Assert.Contains("3-qubit", message)
        | Error err -> Assert.Fail($"Expected a TargetQubits ValidationError, got: {err}")
        | Ok _ -> Assert.Fail("Should reject a target register the lowering would ignore")

    [<Fact>]
    let ``QPE rejects a custom eigenvector for modular exponentiation`` () =
        // Modular exponentiation is estimated on |1>, the generator of the multiplicative
        // order. A supplied eigenvector has nowhere to go, so accepting one silently would
        // mean returning a phase that has nothing to do with the state the caller passed.
        let bknd = LocalBackend.LocalBackend() :> IQuantumBackend

        // |000⟩ over the 3-qubit target register.
        let eigenVector =
            Array.init 8 (fun i ->
                if i = 0 then
                    System.Numerics.Complex.One
                else
                    System.Numerics.Complex.Zero)
            |> StateVector.create
            |> QuantumState.StateVector

        let intent: QPE.QpeExecutionIntent =
            {
                ApplyBitReversalSwaps = false
                Config =
                    { modExpConfig with
                        EigenVector = Some eigenVector
                    }
                Exactness = QPE.Exact
            }

        match QPE.plan bknd intent with
        | Error(QuantumError.ValidationError("EigenVector", message)) -> Assert.Contains("|1⟩", message)
        | Error err -> Assert.Fail($"Expected an EigenVector ValidationError, got: {err}")
        | Ok _ -> Assert.Fail("Should reject a custom eigenvector it cannot use")
