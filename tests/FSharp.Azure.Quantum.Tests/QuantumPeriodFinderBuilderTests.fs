namespace FSharp.Azure.Quantum.Tests

open System
open Xunit
open FSharp.Azure.Quantum
open FSharp.Azure.Quantum.Algorithms
open FSharp.Azure.Quantum.Core
open FSharp.Azure.Quantum.QuantumPeriodFinder

/// Unit tests for QuantumPeriodFinderBuilder
/// Tests Shor's algorithm period finding and integer factorization
module QuantumPeriodFinderBuilderTests =

    // ========================================================================
    // BUILDER VALIDATION TESTS
    // ========================================================================

    [<Fact>]
    let ``periodFinder builder rejects numbers too small`` () =
        let result =
            periodFinder {
                number 3 // Must be at least 4
                precision 8
            }

        result
        |> Result.map (fun _ -> Assert.True(false, "Should have rejected number < 4"))
        |> Result.defaultWith (fun err -> Assert.Contains("at least 4", err.Message))

    [<Fact>]
    let ``periodFinder builder rejects numbers too large`` () =
        let result =
            periodFinder {
                number 15000 // Exceeds simulation limit
                precision 8
            }

        result
        |> Result.map (fun _ -> Assert.True(false, "Should have rejected number > 10000"))
        |> Result.defaultWith (fun err -> Assert.Contains("10000", err.Message))

    [<Fact>]
    let ``periodFinder builder rejects insufficient precision`` () =
        let result =
            periodFinder {
                number 15
                precision 0 // Must be at least 1
            }

        result
        |> Result.map (fun _ -> Assert.True(false, "Should have rejected precision < 1"))
        |> Result.defaultWith (fun err -> Assert.Contains("at least 1", err.Message))

    [<Fact>]
    let ``periodFinder builder rejects excessive precision`` () =
        let result =
            periodFinder {
                number 15
                precision 25 // Exceeds NISQ limit
            }

        result
        |> Result.map (fun _ -> Assert.True(false, "Should have rejected precision > 20"))
        |> Result.defaultWith (fun err -> Assert.Contains("20 qubits", err.Message))

    [<Fact>]
    let ``periodFinder builder rejects base less than 2`` () =
        let result =
            periodFinder {
                number 15
                chosenBase 1 // Base must be >= 2
                precision 8
            }

        result
        |> Result.map (fun _ -> Assert.True(false, "Should have rejected base < 2"))
        |> Result.defaultWith (fun err -> Assert.Contains("at least 2", err.Message))

    [<Fact>]
    let ``periodFinder builder rejects base >= number`` () =
        let result =
            periodFinder {
                number 15
                chosenBase 15 // Base must be < Number
                precision 8
            }

        result
        |> Result.map (fun _ -> Assert.True(false, "Should have rejected base >= number"))
        |> Result.defaultWith (fun err -> Assert.Contains("less than Number", err.Message))

    [<Fact>]
    let ``periodFinder builder rejects too many attempts`` () =
        let result =
            periodFinder {
                number 15
                precision 8
                maxAttempts 150 // Exceeds reasonable limit
            }

        result
        |> Result.map (fun _ -> Assert.True(false, "Should have rejected maxAttempts > 100"))
        |> Result.defaultWith (fun err -> Assert.Contains("100", err.Message))

    [<Fact>]
    let ``periodFinder builder accepts valid minimal configuration`` () =
        let result =
            periodFinder {
                number 15
                precision 8
            }

        match result with
        | Ok problem ->
            Assert.Equal(15, problem.Number)
            Assert.Equal(8, problem.Precision)
            Assert.Equal(QPE.Exactness.Exact, problem.Exactness)
            Assert.Equal(10, problem.MaxAttempts) // Default
            Assert.True(problem.Base.IsNone) // Auto-select
        | Error err -> Assert.True(false, $"Should have succeeded: {err.Message}")

    [<Fact>]
    let ``periodFinder builder accepts full configuration`` () =
        let result =
            periodFinder {
                number 21
                chosenBase 5
                precision 12
                maxAttempts 20
            }

        match result with
        | Ok problem ->
            Assert.Equal(21, problem.Number)
            Assert.Equal(Some 5, problem.Base)
            Assert.Equal(12, problem.Precision)
            Assert.Equal(QPE.Exactness.Exact, problem.Exactness)
            Assert.Equal(20, problem.MaxAttempts)
        | Error err -> Assert.True(false, $"Should have succeeded: {err.Message}")

    [<Fact>]
    let ``periodFinder builder supports exactness operation`` () =
        let result =
            periodFinder {
                number 15
                precision 8
                exactness (QPE.Exactness.Approximate 0.001)
            }

        match result with
        | Ok problem ->
            match problem.Exactness with
            | QPE.Exactness.Approximate epsilon -> Assert.Equal(0.001, epsilon, 3)
            | QPE.Exactness.Exact -> Assert.True(false, "Should have preserved Approximate exactness")
        | Error err -> Assert.True(false, $"Should have succeeded: {err.Message}")

    // ========================================================================
    // FACTORIZATION CORRECTNESS TESTS
    // ========================================================================
    //
    // These tests go through `solve`, which uses genuine quantum period finding
    // (QPE over the modular-exponentiation circuit) whenever the circuit fits the
    // simulator's 20-qubit budget, and the classically-assisted path otherwise.
    //
    // The modular arithmetic is the Beauregard (2003) construction in `Arithmetic`:
    // the workspace is fully uncomputed to |0…0⟩, which ShorArithmeticIntegrationTests
    // asserts directly (including on a superposition of inputs). An earlier note here
    // described the arithmetic as using "dirty ancillas" and listed φ-ADD as future
    // work, with the factor assertions commented out for that reason; that is no
    // longer the case, and the assertions below name exact factors.
    //
    // What *is* still probabilistic is which base a is drawn and how many QPE shots
    // the continued-fraction step needs — so these tests assert the outcome (the
    // factors of N), not the number of attempts or the particular period found.
    //
    // Reference: Beauregard, "Circuit for Shor's algorithm using 2n+3 qubits" (2003)
    // ========================================================================

    /// Assert a successful factorization of `n` into exactly `expected`.
    let private assertFactors (n: int) (expected: int list) (result: PeriodFinderResult) =
        Assert.Equal(n, result.Number)
        Assert.True(result.Success, $"Should factor {n}: {result.Message}")

        match result.Factors with
        | None -> Assert.Fail($"Should find factors of {n}: {result.Message}")
        | Some(p, q) ->
            Assert.Equal(n, p * q)
            Assert.Equal<int Set>(Set.ofList expected, Set.ofList [ p; q ])

    [<Fact; Trait("Category", "ExtraSlow")>]
    let ``solve should factor N=15 (classic example)`` () =
        let problem =
            periodFinder {
                number 15 // 15 = 3 × 5
                precision 6 // Reduced from 8 for faster execution
                maxAttempts 3 // Reduced from 10 for faster execution
            }

        match problem with
        | Error err -> Assert.Fail($"Problem creation failed: {err.Message}")
        | Ok prob ->
            match solve prob with
            | Ok result -> assertFactors 15 [ 3; 5 ] result
            | Error err -> Assert.Fail($"Should factor 15: {err.Message}")

    [<Fact; Trait("Category", "ExtraSlow")>] // ~78 min genuine factoring of 21
    let ``solve should factor N=21 (3 × 7)`` () =
        let problem =
            periodFinder {
                number 21
                // Inert as a speed knob, despite appearances. findPeriodQuantum clamps
                // counting qubits to practicalCircuitQubits - 2*registerBits - 4, here
                // 20 - 2*5 - 4 = 6, so 10, 8 and 6 all simulate the same 20-qubit circuit
                // and only the reported QubitsUsed changes. 5 is the last value that does
                // anything (a 19-qubit circuit); at 4 the phase grid 2^4 = 16 drops below
                // N = 21 and the continued fraction can no longer resolve a period of 6.
                precision 8
                // 4 of the 10 coprime bases below 21 yield no factors from their period
                // (4 and 16 have odd order 3; 5 and 17 give a^(r/2) ≡ -1 mod 21), so each
                // attempt fails with probability 4/18. Three attempts would leave a ~1%
                // flake on a test that asserts the factors exactly; ten makes it ~1e-6.
                // Failing attempts are rare, so this barely affects the typical run time.
                maxAttempts 10
            }

        match problem with
        | Error err -> Assert.Fail($"Problem creation failed: {err.Message}")
        | Ok prob ->
            match solve prob with
            | Ok result -> assertFactors 21 [ 3; 7 ] result
            | Error err -> Assert.Fail($"Should factor 21: {err.Message}")

    [<Fact>]
    let ``solve should refuse N=35, whose circuit does not fit the budget`` () =
        let problem =
            periodFinder {
                number 35
                // Coprime to 35, so period finding is actually reached. With a random base
                // an unlucky draw makes gcd(a, 35) a factor outright — a real step of Shor's
                // algorithm that returns before the qubit budget is ever consulted.
                chosenBase 2
                precision 10
                maxAttempts 3
            }

        match problem with
        | Error err -> Assert.Fail($"Problem creation failed: {err.Message}")
        | Ok prob ->
            match solve prob with
            // N=35 needs 6 register bits, so the modular-exponentiation circuit
            // (counting + 2·6 + 4) leaves 4 counting qubits where 6 are required.
            // This test asserted [5; 7] until the classical fallback was removed —
            // factors that trial division found, reported as a quantum result.
            | Ok result -> Assert.Fail($"Should refuse N=35 rather than factor it classically: {result.Message}")
            | Error err -> Assert.Contains("counting qubits", err.Message)

    // ========================================================================
    // EDGE CASE TESTS
    // ========================================================================

    [<Fact>]
    let ``solve should handle prime numbers gracefully`` () =
        let problem =
            periodFinder {
                number 17 // Prime number
                precision 6 // Reduced from 8 for faster execution
                maxAttempts 2 // Reduced from 10 for faster execution (primes won't factor anyway)
            }

        match problem with
        | Error err -> Assert.Fail($"Problem creation failed: {err.Message}")
        | Ok prob ->
            match solve prob with
            | Ok result ->
                // A prime is rejected by the classical pre-check, before any quantum
                // work: no factors, no success, and the reason says so.
                Assert.Equal(17, result.Number)
                Assert.False(result.Success, "Prime should not report success")
                Assert.True(result.Factors.IsNone, $"Prime should have no factors, got {result.Factors}")
                Assert.Contains("prime", result.Message.ToLowerInvariant())
            | Error err -> Assert.Fail($"A prime should return a clean no-factors result, not an error: {err.Message}")

    [<Fact>]
    let ``solve should handle small composite N=4`` () =
        let problem =
            periodFinder {
                number 4 // Smallest composite
                precision 4 // Reduced from 6 for faster execution
                maxAttempts 2 // Reduced from 10 for faster execution
            }

        match problem with
        | Error err -> Assert.Fail($"Problem creation failed: {err.Message}")
        | Ok prob ->
            match solve prob with
            // 4 is even, so the classical pre-check returns 2 × 2 without any
            // period finding — this is deterministic, not probabilistic.
            | Ok result -> assertFactors 4 [ 2 ] result
            | Error err -> Assert.Fail($"Solve failed: {err.Message}")

    [<Fact; Trait("Category", "ExtraSlow")>]
    let ``solve should use higher precision for better success`` () =
        let lowPrecision =
            periodFinder {
                number 15
                precision 4 // Low precision
                maxAttempts 2 // Reduced for faster execution
            }

        let highPrecision =
            periodFinder {
                number 15
                // Also clamped: 20 - 2*4 - 4 = 8 counting qubits for N = 15, so 16 and 12
                // cost the same. What this test actually contrasts is 8 counting qubits
                // against the 4 above, which is a real difference in circuit width.
                precision 12
                maxAttempts 2 // Reduced for faster execution
            }

        match lowPrecision, highPrecision with
        | Error err, _
        | _, Error err -> Assert.Fail($"Problem creation should succeed: {err.Message}")
        | Ok lowProb, Ok highProb ->
            match solve lowProb, solve highProb with
            | Error err, _ -> Assert.Fail($"Low-precision solve failed: {err.Message}")
            | _, Error err -> Assert.Fail($"High-precision solve failed: {err.Message}")
            | Ok lowResult, Ok highResult ->
                // QubitsUsed = precision + ceil(log₂ N), so it tracks the request
                // exactly even though the counting register is clamped to the
                // simulator's budget during execution.
                Assert.True(
                    highResult.QubitsUsed > lowResult.QubitsUsed,
                    $"12-qubit precision should report more qubits than 4: {highResult.QubitsUsed} vs {lowResult.QubitsUsed}"
                )

                // Both precisions factor 15; more counting qubits never costs success.
                Assert.True(lowResult.Success, $"Low precision should still factor 15: {lowResult.Message}")
                Assert.True(highResult.Success, $"High precision should factor 15: {highResult.Message}")

    // ========================================================================
    // CONVENIENCE HELPER TESTS
    // ========================================================================

    [<Fact>]
    let ``factorInteger should create valid problem`` () =
        match factorInteger 15 8 with
        | Ok problem ->
            Assert.Equal(15, problem.Number)
            Assert.Equal(8, problem.Precision)
            Assert.True(problem.Base.IsNone) // Auto-select
        | Error err -> Assert.True(false, $"Should succeed: {err.Message}")

    [<Fact>]
    let ``factorIntegerWithBase should use custom base`` () =
        match factorIntegerWithBase 21 5 10 with
        | Ok problem ->
            Assert.Equal(21, problem.Number)
            Assert.Equal(Some 5, problem.Base)
            Assert.Equal(10, problem.Precision)
        | Error err -> Assert.True(false, $"Should succeed: {err.Message}")

    [<Fact>]
    let ``breakRSA should use recommended precision`` () =
        match breakRSA 15 with
        | Ok problem ->
            Assert.Equal(15, problem.Number)
            // Recommended: 2*log₂(15) + 3 ≈ 2*4 + 3 = 11
            Assert.True(problem.Precision >= 10, $"Precision {problem.Precision} should be >= 10")
        | Error err -> Assert.True(false, $"Should succeed: {err.Message}")

    [<Fact>]
    let ``estimateResources should return qubit counts`` () =
        let estimate = estimateResources 15 8
        Assert.Contains("15", estimate)
        Assert.Contains("Qubits", estimate)

    [<Fact; Trait("Category", "ExtraSlow")>]
    let ``describeResult should format human-readable output`` () =
        let problem =
            periodFinder {
                number 15
                precision 6 // Reduced from 8 for faster execution
                maxAttempts 2 // Reduced from 10 for faster execution
            }

        match problem with
        | Error err -> Assert.Fail($"Problem creation failed: {err.Message}")
        | Ok prob ->
            match solve prob with
            | Ok result ->
                let description = describeResult result

                Assert.Contains("15", description)
                Assert.Contains("Period", description)
                Assert.Contains("Factorization", description)
                Assert.Contains("Qubits Used", description)
                Assert.Contains(result.BackendName, description)

                // Factoring 15 succeeds, so the report states the factorization it found.
                Assert.Contains("Factorization succeeded", description)
                Assert.Contains("15 = ", description)
            | Error err -> Assert.Fail($"Should factor 15: {err.Message}")

    // ========================================================================
    // RESULT METADATA TESTS
    // ========================================================================

    [<Fact; Trait("Category", "Slow")>]
    let ``solve should populate result metadata`` () =
        let problem =
            periodFinder {
                number 15
                precision 6 // Reduced from 8 for faster execution
                maxAttempts 3 // Reduced from 10 for faster execution
            }

        match problem with
        | Error err -> Assert.Fail($"Problem creation failed: {err.Message}")
        | Ok prob ->
            match solve prob with
            | Ok result ->
                Assert.Equal(15, result.Number)
                Assert.True(result.Success, $"Should factor 15: {result.Message}")
                Assert.True(result.Base >= 2 && result.Base < 15, $"Base {result.Base} should be in range [2, 15)")

                Assert.True(
                    result.PhaseEstimate >= 0.0 && result.PhaseEstimate <= 1.0,
                    $"Phase estimate {result.PhaseEstimate} should be in [0, 1]"
                )

                Assert.True(result.QubitsUsed > 0, "Should use at least one qubit")

                // Attempts counts QPE shots inside period finding, not the MaxAttempts
                // base-retry budget, so it is NOT bounded by 3 — a base whose phase
                // estimates keep landing on 0 or 1/2 legitimately needs several shots.
                Assert.True(result.Attempts >= 0, $"Attempts {result.Attempts} should be non-negative")

                Assert.Contains("Simulator", result.BackendName)
                Assert.NotEmpty(result.Message)

                // With an auto-selected random base, a lucky gcd(a, N) > 1 factors N
                // classically without any period finding, in which case Period is 0.
                let luckyGcdHit = result.Message.StartsWith "Lucky!"

                if luckyGcdHit then
                    Assert.Equal(0, result.Period)
                else
                    // Otherwise the reported period is a genuine period of the base:
                    // a^r ≡ 1 (mod 15). QPE may return a multiple of the order
                    // (e.g. 8 for a base of order 4), so assert the defining property
                    // rather than the order itself.
                    Assert.True(result.Period > 0, $"Period should be positive: {result.Message}")

                    let powMod =
                        Seq.replicate result.Period result.Base
                        |> Seq.fold (fun acc b -> acc * b % 15) 1

                    Assert.Equal(1, powMod)
            | Error err -> Assert.Fail($"Should factor 15: {err.Message}")

    [<Fact; Trait("Category", "Slow")>]
    let ``solve should report the QPE shots period finding consumed`` () =
        let problem =
            periodFinder {
                number 15
                precision 6 // Reduced from 8 for faster execution
                maxAttempts 3 // Reduced from 5 to 3 (still tests attempt tracking)
            }

        match problem with
        | Error err -> Assert.Fail($"Problem creation failed: {err.Message}")
        | Ok prob ->
            match solve prob with
            | Ok result ->
                // Attempts is the QPE shot count of the period-finding run that
                // succeeded — deliberately NOT the MaxAttempts base-retry budget,
                // which it may exceed. It is 0 exactly when no period was found,
                // e.g. a lucky gcd(a, 15) > 1 factored N with no quantum work.
                Assert.True(result.Attempts >= 0, $"Attempts should be non-negative, got {result.Attempts}")

                match result.Period with
                | 0 -> Assert.Equal(0, result.Attempts)
                | _ -> Assert.True(result.Attempts >= 1, "A reported period must have cost at least one QPE shot")
            | Error err -> Assert.Fail($"Should factor 15: {err.Message}")
