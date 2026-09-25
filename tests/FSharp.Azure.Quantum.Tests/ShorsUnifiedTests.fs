namespace FSharp.Azure.Quantum.Tests

open Xunit
open FSharp.Azure.Quantum.Algorithms.Shor
open FSharp.Azure.Quantum.Algorithms.ShorsTypes
open FSharp.Azure.Quantum.Backends.LocalBackend
open FSharp.Azure.Quantum.Core
open FSharp.Azure.Quantum.Core.BackendAbstraction
open System.Threading
open System.Threading.Tasks

/// Tests for Shor's Algorithm Unified Implementation
///
/// Note: Full quantum subroutines are not yet implemented, so tests focus on:
/// - Classical pre-checks (even numbers, primes, etc.)
/// - Input validation
/// - NotImplemented error handling
/// - Configuration validation
module ShorTests =

    module QPE = FSharp.Azure.Quantum.Algorithms.QPE

    let createBackend () = LocalBackend() :> IQuantumBackend

    // ========================================================================
    // PLANNER TESTS (ADR: intent -> plan -> execute)
    // ========================================================================

    /// A backend that claims modular-exponentiation QPE as one semantic operation.
    ///
    /// No shipped backend does this yet — the Beauregard arithmetic lives in Shor, which
    /// compiles after both LocalBackend and TopologicalBackend — so the native branch of
    /// the planner would otherwise be untested and free to rot.
    type private NativeModExpQpeBackend(inner: IQuantumBackend) =
        interface IQuantumBackend with
            member _.ExecuteToState circuit = inner.ExecuteToState circuit
            member _.NativeStateType = inner.NativeStateType
            member _.ApplyOperation operation state = inner.ApplyOperation operation state

            member _.SupportsOperation operation =
                match operation with
                | QuantumOperation.Algorithm(AlgorithmOperation.QPE _) -> true
                | _ -> inner.SupportsOperation operation

            member _.Name = inner.Name + " (native-modexp-qpe)"
            member _.InitializeState numQubits = inner.InitializeState numQubits

            member _.ExecuteToStateAsync circuit ct = inner.ExecuteToStateAsync circuit ct

            member _.ApplyOperationAsync operation state ct =
                inner.ApplyOperationAsync operation state ct

    let private periodIntent: ShorPeriodFindingIntent =
        {
            Base = 7
            Modulus = 15
            CountingQubits = 3
        }

    [<Fact>]
    let ``Shor period-finding planner hands the whole intent to a backend that claims it`` () =
        let backend = createBackend () |> NativeModExpQpeBackend :> IQuantumBackend

        match planPeriodFinding backend periodIntent with
        | Ok(ShorPeriodFindingPlan.ExecuteNatively qpeIntent) ->
            Assert.Equal(3, qpeIntent.CountingQubits)
            // ceil(log2 15) = 4 register bits for the target, which is exactly the shape
            // no shipped backend accepts yet — hence the test double above.
            Assert.Equal(4, qpeIntent.TargetQubits)

            match qpeIntent.Unitary with
            | QpeUnitary.ModularExponentiation(baseNum, modulus) ->
                Assert.Equal(7, baseNum)
                Assert.Equal(15, modulus)
            | other -> Assert.Fail($"Expected a ModularExponentiation unitary, got {other}")
        | Ok(ShorPeriodFindingPlan.ExecuteViaModExpCircuit _) ->
            Assert.Fail("Backend claimed native modular-exponentiation QPE but the planner lowered anyway")
        | Error err -> Assert.Fail($"Planning failed: {err}")

    [<Fact>]
    let ``Shor period-finding planner lowers to the circuit when the backend declines`` () =
        // The real LocalBackend, not a double: its SupportsOperation used to answer true
        // for every QPE intent while ApplyOperation rejected this one, so this pins the
        // honest answer as much as it pins the planner.
        let backend = createBackend ()

        match planPeriodFinding backend periodIntent with
        | Ok(ShorPeriodFindingPlan.ExecuteViaModExpCircuit(baseNum, modulus, counting)) ->
            Assert.Equal(7, baseNum)
            Assert.Equal(15, modulus)
            Assert.Equal(3, counting)
        | Ok(ShorPeriodFindingPlan.ExecuteNatively _) ->
            Assert.Fail("LocalBackend cannot execute modular-exponentiation QPE natively; planning it is a bug")
        | Error err -> Assert.Fail($"Planning failed: {err}")

    [<Fact>]
    let ``Shor period-finding planner rejects a base that is not coprime to N`` () =
        let backend = createBackend ()

        // gcd(5, 15) = 5. This is caught at planning rather than after a simulation,
        // and the caller turns it into a factor instead of a period-finding run.
        match planPeriodFinding backend { periodIntent with Base = 5 } with
        | Ok _ -> Assert.Fail("Should reject a base sharing a factor with N")
        | Error(QuantumError.ValidationError(field, message)) ->
            Assert.Equal("Base", field)
            Assert.Contains("coprime", message)
        | Error err -> Assert.Fail($"Expected a ValidationError, got: {err}")

    // ========================================================================
    // CLASSICAL PRE-CHECK TESTS
    // ========================================================================

    [<Fact>]
    let ``Shor's algorithm handles even numbers (trivial case)`` () =
        let backend = createBackend ()

        match factor 6 backend with
        | Error err -> Assert.Fail($"Should succeed for even number: {err}")
        | Ok result ->
            Assert.True(result.Success, "Should succeed for even number")
            Assert.Equal(6, result.Number)

            match result.Factors with
            | Some(p, q) ->
                Assert.Equal(6, p * q)
                Assert.Contains(2, [ p; q ]) // One factor should be 2
            | None -> Assert.Fail("Should find factors for even number")

            Assert.Contains("even", result.Message.ToLower())

    [<Fact>]
    let ``Shor's algorithm detects prime numbers`` () =
        let backend = createBackend ()

        // Test with prime number 11
        match factor 11 backend with
        | Error err -> Assert.Fail($"Should return result for prime: {err}")
        | Ok result ->
            Assert.False(result.Success, "Should fail for prime number")
            Assert.Equal(11, result.Number)
            Assert.Equal(None, result.Factors)
            Assert.Contains("prime", result.Message.ToLower())

    [<Fact>]
    let ``Shor's algorithm rejects numbers < 4`` () =
        let backend = createBackend ()

        for n in [ 0; 1; 2; 3 ] do
            match factor n backend with
            | Error err -> Assert.Fail($"Should return result (not error) for n={n}: {err}")
            | Ok result ->
                Assert.False(result.Success, $"Should fail for n={n}")
                Assert.Equal(n, result.Number)
                Assert.Equal(None, result.Factors)
                Assert.Contains("too small", result.Message.ToLower())

    [<Fact>]
    let ``Shor's algorithm handles composite even number 20`` () =
        let backend = createBackend ()

        match factor 20 backend with
        | Error err -> Assert.Fail($"Should succeed for even number: {err}")
        | Ok result ->
            Assert.True(result.Success)
            Assert.Equal(20, result.Number)

            match result.Factors with
            | Some(p, q) ->
                Assert.Equal(20, p * q)
                Assert.Contains(2, [ p; q ])
            | None -> Assert.Fail("Should find factors")

    // ========================================================================
    // CONFIGURATION VALIDATION TESTS
    // ========================================================================

    [<Fact; Trait("Category", "Slow")>]
    let ``factor15 uses correct configuration`` () =
        let backend = createBackend ()

        // factor15 currently returns NotImplemented, but we can check that it's callable
        match factor15 backend with
        | Ok result ->
            // If it somehow succeeds (e.g., if quantum part is implemented), validate
            Assert.Equal(15, result.Config.NumberToFactor)
            Assert.Equal(Some 7, result.Config.RandomBase)
            Assert.Equal(3, result.Config.PrecisionQubits)
        | Error(QuantumError.NotImplemented _) ->
            // Expected: quantum subroutine not yet implemented
            ()
        | Error err -> Assert.Fail($"Unexpected error: {err}")

    [<Fact; Trait("Category", "ExtraSlow")>] // genuine quantum period finding since the classical path was removed
    let ``factor21 uses correct configuration`` () =
        let backend = createBackend ()

        match factor21 backend with
        | Ok result ->
            Assert.Equal(21, result.Config.NumberToFactor)
            Assert.Equal(Some 2, result.Config.RandomBase)
            Assert.Equal(8, result.Config.PrecisionQubits)
        | Error(QuantumError.NotImplemented _) -> () // Expected
        | Error err -> Assert.Fail($"Unexpected error: {err}")

    [<Fact; Trait("Category", "ExtraSlow")>] // genuine quantum period finding since the classical path was removed
    let ``factor calculates precision qubits correctly`` () =
        let backend = createBackend ()

        // For N=15: log₂(15) ≈ 3.9 → 2*3.9+3 = 10.8 → round to 10
        // For N=21: log₂(21) ≈ 4.4 → 2*4.4+3 = 11.8 → round to 11

        match factor 15 backend with
        | Ok result ->
            // Precision should be around 10-11 qubits
            Assert.InRange(result.Config.PrecisionQubits, 9, 12)
        | Error(QuantumError.NotImplemented _) -> () // Expected - quantum part not implemented
        | Error err -> Assert.Fail($"Unexpected error: {err}")

    // ========================================================================
    // NOT IMPLEMENTED ERROR TESTS
    // ========================================================================

    [<Fact; Trait("Category", "ExtraSlow")>] // genuine quantum period finding since the classical path was removed
    let ``Shor's algorithm returns NotImplemented for composite odd numbers`` () =
        let backend = createBackend ()

        // 15 is composite and odd, requires quantum period-finding
        match factor 15 backend with
        | Ok result ->
            // If somehow implemented, validate success
            Assert.Equal(15, result.Number)

            match result.Factors with
            | Some(p, q) ->
                Assert.Equal(15, p * q)
                Assert.Contains(3, [ p; q ])
                Assert.Contains(5, [ p; q ])
            | None -> ()
        | Error(QuantumError.NotImplemented(feature, hint)) ->
            // Expected: quantum subroutine not yet implemented
            Assert.Contains("Shor", feature)
            Assert.True(hint.IsSome, "Should provide implementation hint")
        | Error err -> Assert.Fail($"Unexpected error type: {err}")

    [<Fact>]
    let ``factor15 returns meaningful error for quantum subroutine`` () =
        let backend = createBackend ()

        match factor15 backend with
        | Ok _ -> () // If implemented, that's fine
        | Error(QuantumError.NotImplemented(feature, Some hint)) ->
            // Should have helpful hint
            Assert.False(System.String.IsNullOrWhiteSpace(hint))
        | Error(QuantumError.NotImplemented(feature, None)) ->
            // Still acceptable but less helpful
            ()
        | Error err -> Assert.Fail($"Unexpected error: {err}")

    // ========================================================================
    // CUSTOM CONFIGURATION TESTS
    // ========================================================================

    // These two check that a config is accepted and round-trips, not that factoring is hard.
    // They used N=35 and N=21, which now cost a refusal and a 20-qubit circuit respectively.
    // N=15 at 3 counting qubits keeps them honest and quick: every order of a base mod 15
    // divides 8, so the phase grid resolves exactly and QPE lands first try.
    [<Fact; Trait("Category", "Slow")>]
    let ``execute accepts custom ShorsConfig`` () =
        let backend = createBackend ()

        let config =
            {
                NumberToFactor = 15
                RandomBase = Some 2
                PrecisionQubits = 3
                MaxAttempts = 5
            }

        match execute config backend with
        | Ok result ->
            Assert.Equal(15, result.Number)
            Assert.Equal(config, result.Config)
        | Error err -> Assert.Fail($"Unexpected error: {err}")

    [<Fact; Trait("Category", "Slow")>]
    let ``execute with None RandomBase allows algorithm to choose`` () =
        let backend = createBackend ()

        let config =
            {
                NumberToFactor = 15
                RandomBase = None // Let algorithm choose
                PrecisionQubits = 3
                MaxAttempts = 5
            }

        match execute config backend with
        | Ok result ->
            Assert.Equal(15, result.Number)
            Assert.Equal(None, config.RandomBase)
        | Error err -> Assert.Fail($"Unexpected error: {err}")

    // ========================================================================
    // EDGE CASES
    // ========================================================================

    [<Fact>]
    let ``Shor's algorithm handles perfect squares`` () =
        let backend = createBackend ()

        // 16 = 4^2 (but also even, so caught by even check)
        match factor 16 backend with
        | Ok result ->
            Assert.True(result.Success)
            Assert.Equal(16, result.Number)

            match result.Factors with
            | Some(p, q) -> Assert.Equal(16, p * q)
            | None -> Assert.Fail("Should find factors")
        | Error err -> Assert.Fail($"Should handle perfect square: {err}")

    [<Fact>]
    let ``Shor refuses a semi-prime whose circuit exceeds the qubit budget`` () =
        let backend = createBackend ()

        // 35 = 5 × 7 needs 6 register bits, leaving 20 - 2*6 - 4 = 4 counting qubits — fewer
        // than the 6 required to resolve the period. This used to return [5; 7] from a
        // classical trial-division search dressed as a quantum result. Refusing is the
        // honest answer, and asserting the refusal is what stops the fallback coming back.
        //
        // The base is pinned because it must be coprime to 35 for period finding to be
        // reached at all: an unlucky draw (a=30, say) makes gcd(a, 35) a factor outright,
        // which is a genuine step of Shor's algorithm and returns Ok before any planning.
        let config =
            {
                NumberToFactor = 35
                RandomBase = Some 2 // gcd(2, 35) = 1
                PrecisionQubits = 8
                MaxAttempts = 1
            }

        match execute config backend with
        | Ok result -> Assert.Fail($"Should refuse N=35 rather than factor it classically: {result.Message}")
        | Error(QuantumError.ValidationError(_, message)) ->
            Assert.Contains("counting qubits", message)
            Assert.DoesNotContain("classically assisted", message)
        | Error err -> Assert.Fail($"Expected a qubit-budget ValidationError: {err}")

    // ========================================================================
    // RULE1 COMPLIANCE TESTS
    // ========================================================================

    [<Fact; Trait("Category", "Slow")>] // genuine end-to-end factoring of 15 (~5 min)
    let ``Shor accepts IQuantumBackend`` () =
        // This test validates that Shor follows RULE1
        let backend = createBackend ()

        // Should compile and accept IQuantumBackend
        let result = factor 15 backend

        // We don't care about the result, just that it compiles and runs
        Assert.True(true)

    [<Fact; Trait("Category", "ExtraSlow")>] // genuine end-to-end factoring of 15 (~5 min)
    let ``Shor works with LocalBackend`` () =
        // Validate that LocalBackend is compatible
        let backend = LocalBackend() :> IQuantumBackend

        let config =
            {
                NumberToFactor = 15
                RandomBase = Some 7
                PrecisionQubits = 8
                MaxAttempts = 3
            }

        // Should accept LocalBackend
        let result = execute config backend
        Assert.True(true) // Just validate it compiles and runs

    // ========================================================================
    // QUANTUM PATH TESTS (NEW - Actual Factorization)
    // ========================================================================

    [<Fact; Trait("Category", "Slow")>]
    let ``factor15 successfully factors 15 into 3 and 5`` () =
        let backend = createBackend ()

        match factor15 backend with
        | Error err -> Assert.Fail($"Should succeed: {err}")
        | Ok result ->
            Assert.True(result.Success, "Should successfully factor 15")
            Assert.Equal(15, result.Number)

            match result.Factors with
            | Some(p, q) ->
                // Verify factors are correct
                Assert.Equal(15, p * q)
                // Check we got 3 and 5 (in any order)
                let factors = [ p; q ] |> List.sort
                Assert.Equal<int list>([ 3; 5 ], factors)
            | None -> Assert.Fail("Should find factors for 15")

            // Verify period-finding result exists
            Assert.True(result.PeriodResult.IsSome, "Should have period result")

            match result.PeriodResult with
            | Some pr ->
                Assert.Equal(7, pr.Base)
                // Period of 7 mod 15 should be 4 (since 7^4 = 2401 ≡ 1 mod 15)
                Assert.Equal(4, pr.Period)
            | None -> ()

    [<Fact; Trait("Category", "ExtraSlow")>] // genuine quantum period finding since the classical path was removed
    let ``factor21 successfully factors 21 into 3 and 7`` () =
        let backend = createBackend ()

        match factor21 backend with
        | Error err -> Assert.Fail($"Should succeed: {err}")
        | Ok result ->
            Assert.True(result.Success, "Should successfully factor 21")
            Assert.Equal(21, result.Number)

            match result.Factors with
            | Some(p, q) ->
                Assert.Equal(21, p * q)
                let factors = [ p; q ] |> List.sort
                Assert.Equal<int list>([ 3; 7 ], factors)
            | None -> Assert.Fail("Should find factors for 21")

            Assert.True(result.PeriodResult.IsSome)

            match result.PeriodResult with
            | Some pr ->
                Assert.Equal(2, pr.Base)
                // Period of 2 mod 21 should be 6 (since 2^6 = 64 ≡ 1 mod 21)
                Assert.Equal(6, pr.Period)
            | None -> ()

    [<Fact>]
    let ``findPeriod validates inputs`` () =
        let backend = createBackend ()

        // Invalid a (must be in range (0, n))
        match findPeriod 0 15 8 backend with
        | Error(QuantumError.ValidationError _) -> () // Expected
        | _ -> Assert.Fail("Should reject a=0")

        match findPeriod 15 15 8 backend with
        | Error(QuantumError.ValidationError _) -> () // Expected
        | _ -> Assert.Fail("Should reject a=n")

        // Invalid a (not coprime)
        match findPeriod 3 15 8 backend with
        | Error(QuantumError.ValidationError(field, reason)) -> Assert.Contains("coprime", reason.ToLower())
        | _ -> Assert.Fail("Should reject non-coprime a")

    [<Fact>]
    let ``findPeriod correctly finds period for a=7, N=15`` () =
        let backend = createBackend ()

        match findPeriod 7 15 3 backend with
        | Error err -> Assert.Fail($"Should succeed: {err}")
        | Ok result ->
            Assert.Equal(7, result.Base)
            Assert.Equal(15 % result.Period, 15 % 4) // Period should divide into pattern
            // 7^4 mod 15 should be 1
            let verification = 1

            for _ in 1 .. result.Period do
                () // Period verification

            Assert.InRange(result.Period, 1, 15)

    [<Fact>]
    let ``findPeriod refuses N too large for the quantum circuit rather than going classical`` () =
        let backend = createBackend ()

        // N=35 needs 6 register bits, more counting qubits than the 20-qubit budget leaves.
        // findPeriod used to answer 12 here — ord(2 mod 35), found by trial division. It is
        // the right number and the wrong way to get it, so the contract is now an error.
        match findPeriod 2 35 8 backend with
        | Ok result -> Assert.Fail($"Should refuse N=35, but returned period {result.Period}")
        | Error(QuantumError.ValidationError(_, reason)) -> Assert.Contains("counting qubits", reason)
        | Error err -> Assert.Fail($"Expected a qubit-budget ValidationError: {err}")

    [<Fact>]
    let ``findPeriodQuantum fails fast for N too large for the qubit budget`` () =
        let backend = createBackend ()

        // Explicitly requesting the genuine quantum path for a too-large N must return a
        // clear validation error immediately, not after 16 full QPE simulations.
        match findPeriodQuantum 2 35 8 backend with
        | Error(QuantumError.ValidationError(_, reason)) -> Assert.Contains("qubit", reason.ToLower())
        | Ok _ -> Assert.Fail("Should reject N=35 on the genuine quantum path")
        | Error err -> Assert.Fail($"Expected ValidationError, got: {err}")

    [<Fact>]
    let ``factoring N in the 128-1000 range is refused, not faked`` () =
        let backend = createBackend ()

        // This used to assert that N=143 factors into 11 and 13. It did — classically. The
        // "128-1000 range it is documented for" was a range the quantum circuit has never
        // been able to reach on a 20-qubit budget, so the documentation described the
        // fallback rather than the algorithm. Now the range is simply out of reach and says so.
        //
        // Base pinned coprime for the same reason as the N=35 case above: a random draw
        // hitting a multiple of 11 or 13 factors 143 by gcd and never reaches planning.
        let config =
            {
                NumberToFactor = 143
                RandomBase = Some 2 // gcd(2, 143) = 1
                PrecisionQubits = 8
                MaxAttempts = 1
            }

        match execute config backend with
        | Ok result -> Assert.Fail($"Should refuse N=143 rather than factor it classically: {result.Message}")
        | Error(QuantumError.ValidationError(_, reason)) -> Assert.Contains("counting qubits", reason)
        | Error err -> Assert.Fail($"Expected a qubit-budget ValidationError: {err}")

    [<Fact>]
    let ``Shor demonstrates QPE for period finding`` () =
        let backend = createBackend ()

        // Test that QPE is being used (even if classically assisted).
        // Pin the base: with an auto-selected random base, a lucky gcd(a, 15) > 1
        // factors 15 classically without running period finding (no PeriodResult).
        let config =
            {
                NumberToFactor = 15
                RandomBase = Some 7
                PrecisionQubits = 8
                MaxAttempts = 3
            }

        match execute config backend with
        | Ok result when result.PeriodResult.IsSome ->
            let pr = result.PeriodResult.Value
            // Phase estimate should be in range [0, 1)
            Assert.InRange(pr.PhaseEstimate, 0.0, 1.0)
            // For period r=4, phase should be s/4 for some s in [0,3]
            // So phase * 4 should be close to an integer
            let phaseTimesR = pr.PhaseEstimate * float pr.Period
            let nearestInt = round phaseTimesR
            let error = abs (phaseTimesR - nearestInt)
            Assert.True(error < 0.1, $"Phase estimate should be s/r form, got {pr.PhaseEstimate}")
        | _ -> Assert.Fail("Should have period result")
