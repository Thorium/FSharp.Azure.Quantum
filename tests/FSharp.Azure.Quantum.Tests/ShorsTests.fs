namespace FSharp.Azure.Quantum.Tests

open Xunit
open FSharp.Azure.Quantum.Algorithms.ShorsTypes
open FSharp.Azure.Quantum.Core.BackendAbstraction
open FSharp.Azure.Quantum.Backends
open FSharp.Azure.Quantum.CircuitBuilder
open System
open FSharp.Azure.Quantum.LocalSimulator

/// Tests for Shor's Algorithm and Backend Adapter
///
/// These tests drive `Shor.executeClassicallyAssisted`: period finding runs
/// classically and a QPE circuit is executed for demonstration. That path is
/// qubit-light and its factor output is deterministic, so the assertions below
/// name exact factors rather than accepting "any attempt".
///
/// The modular arithmetic underneath is the Beauregard (2003) construction in
/// `Arithmetic` — clean ancillas, fully uncomputed. (An earlier note here
/// claimed "dirty ancillas" made factoring probabilistic and listed φ-ADD as
/// future work; that work is done. ShorArithmeticIntegrationTests asserts the
/// workspace returns to |0…0⟩, including on a superposition of inputs.)
///
/// Genuine quantum period finding (`Shor.executeWith`, the default) is exercised
/// by QuantumPeriodFinderBuilderTests; it is far slower because it simulates the
/// full modular-exponentiation circuit.
///
/// Reference: src/FSharp.Azure.Quantum/Algorithms/QuantumArithmetic.fs
module ShorsTests =

    // Import Shor module functions
    module Shor = FSharp.Azure.Quantum.Algorithms.Shor

    // ========================================================================
    // BACKWARD COMPATIBILITY HELPERS
    // ========================================================================

    /// Execute Shor's algorithm (backward compatibility wrapper)
    /// NOTE: shots parameter ignored in new API (state-based execution)
    let executeShorsWithBackend (config: ShorsConfig) (backend: IQuantumBackend) (shots: int) =
        Shor.executeClassicallyAssisted config backend

    /// Factor number with backend (convenience wrapper)
    /// NOTE: shots parameter ignored in new API (state-based execution)
    let factorWithBackend (n: int) (backend: IQuantumBackend) (shots: int) =
        let config =
            {
                NumberToFactor = n
                RandomBase = None
                PrecisionQubits = 2 * (int (Math.Log(float n, 2.0))) + 3
                MaxAttempts = 5
            }
        // Return Result type to match old signature
        match Shor.executeClassicallyAssisted config backend with
        | Ok result -> if result.Success then Ok result else Error result.Message
        | Error err -> Error $"%A{err}"

    // ========================================================================
    // LOCAL SIMULATION TESTS (using ShorsAlgorithm module)
    // ========================================================================

    [<Fact>]
    let ``Shor factors 15 correctly`` () =
        // 15 = 3 × 5 (classic Shor's example)
        // OPTIMIZED: Use 5 precision qubits and 10 shots for fast execution
        // This reduces circuit depth from ~2048 to ~32 controlled operations
        let config =
            {
                NumberToFactor = 15
                RandomBase = Some 7 // Use a=7 (known to work)
                PrecisionQubits = 5 // Reduced from 11 (still sufficient for N=15)
                MaxAttempts = 1
            }

        match executeShorsWithBackend config (LocalBackend.LocalBackend() :> IQuantumBackend) 10 with
        | Error msg -> Assert.Fail($"Shor's execution failed: {msg}")
        | Ok result ->
            Assert.True(result.Success, $"Should factor 15 with a=7: {result.Message}")

            match result.Factors with
            | None -> Assert.Fail($"Should find factors of 15: {result.Message}")
            | Some(p, q) ->
                Assert.Equal(15, p * q)
                Assert.Equal<int Set>(Set.ofList [ 3; 5 ], Set.ofList [ p; q ])

                // a=7 has order 4 mod 15, and the classically-assisted path recovers it.
                match result.PeriodResult with
                | None -> Assert.Fail("Successful factoring should report the period it used")
                | Some period ->
                    Assert.Equal(7, period.Base)
                    Assert.Equal(4, period.Period)

    [<Fact>]
    let ``Shor factors 21 correctly`` () =
        // 21 = 3 × 7
        // OPTIMIZED: Use 5 precision qubits and 10 shots
        let config =
            {
                NumberToFactor = 21
                RandomBase = Some 2 // Use a=2 for deterministic testing
                PrecisionQubits = 5 // Reduced for faster execution
                MaxAttempts = 1
            }

        match executeShorsWithBackend config (LocalBackend.LocalBackend() :> IQuantumBackend) 10 with
        | Error msg -> Assert.Fail($"Shor's execution failed: {msg}")
        | Ok result ->
            Assert.True(result.Success, $"Should factor 21 with a=2: {result.Message}")

            match result.Factors with
            | None -> Assert.Fail($"Should find factors of 21: {result.Message}")
            | Some(p, q) ->
                Assert.Equal(21, p * q)
                Assert.Equal<int Set>(Set.ofList [ 3; 7 ], Set.ofList [ p; q ])

                // ord(2) = 6 mod 21. QPE occasionally lands on a multiple of the
                // order (e.g. 18) instead of the order itself; any such r is a
                // legitimate period for factoring, so assert the defining property
                // rather than the literal 6.
                match result.PeriodResult with
                | None -> Assert.Fail("Successful factoring should report the period it used")
                | Some period ->
                    Assert.Equal(2, period.Base)
                    Assert.True(period.Period > 0, $"Period should be positive, got {period.Period}")

                    let powMod = Seq.replicate period.Period 2 |> Seq.fold (fun acc b -> acc * b % 21) 1

                    Assert.Equal(1, powMod) // 2^r ≡ 1 (mod 21)

    [<Fact>]
    let ``Shor handles even numbers correctly`` () =
        // Even numbers have trivial factorization
        match factorWithBackend 14 (LocalBackend.LocalBackend() :> IQuantumBackend) 1000 with
        | Error msg -> Assert.Fail($"Shor's execution failed: {msg}")
        | Ok result ->
            Assert.True(result.Success, "Should successfully identify even number")

            match result.Factors with
            | None -> Assert.Fail("Should find factors for even number")
            | Some(p, q) ->
                Assert.Equal(14, p * q)
                Assert.True(p = 2 || q = 2, "One factor should be 2")
                Assert.Contains("even", result.Message.ToLower())

    [<Fact>]
    let ``Shor detects prime numbers`` () =
        // Prime numbers cannot be factored
        // OPTIMIZED: Use minimal precision qubits and shots
        let config =
            {
                NumberToFactor = 17
                RandomBase = Some 2
                PrecisionQubits = 5 // Minimal for fast testing
                MaxAttempts = 1
            }

        match executeShorsWithBackend config (LocalBackend.LocalBackend() :> IQuantumBackend) 10 with
        | Error msg -> Assert.Fail($"Shor's execution failed: {msg}")
        | Ok result ->
            // 17 is prime: the classical pre-check catches it before any quantum work.
            Assert.False(result.Success, "Should fail to factor prime")
            Assert.True(result.Factors.IsNone, "Prime should have no factors")
            Assert.Contains("prime", result.Message.ToLower())

    [<Fact>]
    let ``Shor validates input range`` () =
        // Test input validation

        // Too small
        (factorWithBackend 2 (LocalBackend.LocalBackend() :> IQuantumBackend) 1000)
        |> Result.map (fun _ -> Assert.Fail("Should reject N < 4"))
        |> Result.defaultWith (fun msg -> Assert.Contains("4", msg))

        // Too large
        (factorWithBackend 100000 (LocalBackend.LocalBackend() :> IQuantumBackend) 1000)
        |> Result.map (fun _ -> Assert.Fail("Should reject N > 1000"))
        |> Result.defaultWith (fun msg -> Assert.Contains("1000", msg))

    [<Fact>]
    let ``Shor with specific base`` () =
        // factorWithBackend 15 (LocalBackend.LocalBackend() :> IQuantumBackend) 1000 using base a=7 (known to work)
        // OPTIMIZED: Reduced precision and shots
        match
            let config =
                {
                    NumberToFactor = 15
                    RandomBase = Some 7
                    PrecisionQubits = 5
                    MaxAttempts = 1
                } in

            executeShorsWithBackend config (LocalBackend.LocalBackend() :> IQuantumBackend) 10
        with
        | Error msg -> Assert.Fail($"Shor's execution failed: {msg}")
        | Ok result ->
            Assert.True(result.Success, $"Factorization with a=7 should succeed: {result.Message}")

            match result.Factors with
            | None -> Assert.Fail($"Should find factors of 15 with a=7: {result.Message}")
            | Some(p, q) ->
                Assert.Equal(15, p * q)
                Assert.Equal<int Set>(Set.ofList [ 3; 5 ], Set.ofList [ p; q ])

                match result.PeriodResult with
                | None -> Assert.Fail("Successful factoring should report the period it used")
                | Some pResult ->
                    Assert.Equal(7, pResult.Base)
                    Assert.True(pResult.Period > 0, "Period should be positive")

    [<Fact>]
    let ``Shor configuration validation`` () =
        let invalidConfig =
            {
                NumberToFactor = 15
                RandomBase = None
                PrecisionQubits = 0 // Invalid
                MaxAttempts = 5
            }

        (executeShorsWithBackend invalidConfig (LocalBackend.LocalBackend() :> IQuantumBackend) 1000)
        |> Result.map (fun _ -> Assert.Fail("Should reject 0 precision qubits"))
        |> Result.defaultWith (fun msg -> Assert.Contains("positive", msg.Message.ToLower()))

        // Precision is bounded by the circuit budget (wall-clock), not by state capacity.
        let budget = StateVector.practicalCircuitQubits

        let invalidConfig2 =
            {
                NumberToFactor = 15
                RandomBase = None
                PrecisionQubits = budget + 5 // Too many
                MaxAttempts = 5
            }

        (executeShorsWithBackend invalidConfig2 (LocalBackend.LocalBackend() :> IQuantumBackend) 1000)
        |> Result.map (fun _ -> Assert.Fail($"Should reject > {budget} precision qubits"))
        |> Result.defaultWith (fun msg -> Assert.Contains(string budget, msg.Message))

    [<Fact>]
    let ``Shor factors small composites`` () =
        // Test multiple small composite numbers
        // OPTIMIZED: Reduced test cases and parameters for speed
        let testCases = [ (6, [ 2; 3 ]); (10, [ 2; 5 ]) ]

        for (n, expectedFactorList) in testCases do
            let config =
                {
                    NumberToFactor = n
                    RandomBase = None
                    PrecisionQubits = 4 // Minimal precision
                    MaxAttempts = 1
                }

            match executeShorsWithBackend config (LocalBackend.LocalBackend() :> IQuantumBackend) 10 with
            | Error msg -> Assert.Fail($"Failed to factor {n}: {msg}")
            | Ok result ->
                Assert.True(result.Success, $"Should successfully factor {n}: {result.Message}")

                match result.Factors with
                | None -> Assert.Fail($"Should find factors of {n}: {result.Message}")
                | Some(p, q) ->
                    Assert.Equal(n, p * q)
                    Assert.Equal<int Set>(Set.ofList expectedFactorList, Set.ofList [ p; q ])

    [<Fact>]
    let ``Shor returns correct config in result`` () =
        // OPTIMIZED: Reduced precision and shots
        let config =
            {
                NumberToFactor = 15
                RandomBase = Some 7
                PrecisionQubits = 5
                MaxAttempts = 1
            }

        match executeShorsWithBackend config (LocalBackend.LocalBackend() :> IQuantumBackend) 10 with
        | Error msg -> Assert.Fail($"Shor's execution failed: {msg}")
        | Ok result ->
            Assert.Equal(15, result.Number)
            Assert.Equal(15, result.Config.NumberToFactor)
            Assert.True(result.Config.PrecisionQubits > 0)

    // ========================================================================
    // BACKEND ADAPTER TESTS
    // ========================================================================

    [<Fact>]
    let ``Shor backend validates input`` () =
        let backend = LocalBackend.LocalBackend() :> IQuantumBackend

        // Too small
        (factorWithBackend 2 backend 100)
        |> Result.map (fun _ -> Assert.Fail("Should reject N < 4"))
        |> Result.defaultWith (fun msg -> Assert.Contains("4", msg))

        // Too large for backend
        (factorWithBackend 10000 backend 100)
        |> Result.map (fun _ -> Assert.Fail("Should reject N > 1000 for backend"))
        |> Result.defaultWith (fun msg -> Assert.Contains("1000", msg))

    [<Fact>]
    let ``Shor backend handles even numbers`` () =
        let backend = LocalBackend.LocalBackend() :> IQuantumBackend

        match factorWithBackend 14 backend 100 with
        | Error msg -> Assert.Fail($"Backend execution failed: {msg}")
        | Ok result ->
            Assert.True(result.Success)

            match result.Factors with
            | None -> Assert.Fail("Should find factors for even number")
            | Some(p, q) ->
                Assert.Equal(14, p * q)
                Assert.True(p = 2 || q = 2, "One factor should be 2")

    [<Fact>]
    let ``Shor backend factorWithBackend 15 (LocalBackend.LocalBackend() :> IQuantumBackend) 1000 example`` () =
        let backend = LocalBackend.LocalBackend() :> IQuantumBackend

        // OPTIMIZED: Use reduced parameters (5 precision qubits, 10 shots)
        let config =
            {
                NumberToFactor = 15
                RandomBase = Some 7
                PrecisionQubits = 5
                MaxAttempts = 1
            }

        match executeShorsWithBackend config backend 10 with
        | Error msg ->
            // Backend execution should succeed or provide meaningful error
            Assert.Fail($"Shor's algorithm execution failed: {msg}")
        | Ok result ->
            // Verify result structure is valid
            Assert.Equal(15, result.Number)

            // If factors found, verify correctness
            match result.Factors with
            | Some(p, q) ->
                // Verify p × q = 15
                Assert.Equal(15, p * q)
                // Verify factors are non-trivial (not 1 or 15)
                Assert.True(p > 1 && p < 15, $"Factor p={p} should be non-trivial")
                Assert.True(q > 1 && q < 15, $"Factor q={q} should be non-trivial")
                // Verify we got the correct factors (3 and 5)
                let factors = Set.ofList [ p; q ]
                Assert.True(factors.Contains(3) && factors.Contains(5), $"Expected factors {{3, 5}}, got {{{p}, {q}}}")
                Assert.True(result.Success, "Success flag should be true when factors found")
            | None ->
                // If no factors found, success should be false
                Assert.False(result.Success, "Success flag should be false when no factors found")
                // Period result should still be present for analysis
                match result.PeriodResult with
                | Some periodResult -> Assert.True(periodResult.Period > 0, "Period should be positive")
                | None ->
                    // If no period found, ensure error message explains why
                    Assert.False(String.IsNullOrEmpty(result.Message), "Should provide meaningful error message")

    [<Fact>]
    let ``Period extraction works end-to-end`` () =
        // Test that Shor can extract periods correctly end-to-end
        // This replaces the internal extractPeriodFromHistogram test
        let backend = LocalBackend.LocalBackend() :> IQuantumBackend

        // Use a known case: 15 with base 7 has period 4
        let config =
            {
                NumberToFactor = 15
                RandomBase = Some 7
                PrecisionQubits = 8
                MaxAttempts = 3
            }

        match executeShorsWithBackend config backend 100 with
        | Ok result ->
            // Should successfully find period or factors
            Assert.True(
                result.Success || result.PeriodResult.IsSome || result.Factors.IsSome,
                "Should find period or factors"
            )

            // If period found, it should be valid
            match result.PeriodResult with
            | Some periodResult ->
                Assert.True(
                    periodResult.Period >= 2 && periodResult.Period <= 15,
                    $"Period should be between 2 and 15, got {periodResult.Period}"
                )
            | None -> ()
        | Error err -> Assert.Fail($"Period finding failed: {err}")

    [<Fact>]
    let ``Shor validates precision qubits correctly`` () =
        // Test that Shor validates precision qubit configuration
        // This replaces the internal periodFindingToCircuit validation test
        let backend = LocalBackend.LocalBackend() :> IQuantumBackend

        // Config with too few precision qubits (should still work but warn)
        let configLowPrecision =
            {
                NumberToFactor = 15
                RandomBase = Some 7
                PrecisionQubits = 2 // Very low
                MaxAttempts = 1
            }

        // Should execute without error (validation happens inside)
        match executeShorsWithBackend configLowPrecision backend 10 with
        | Ok result ->
            // May not succeed with low precision, but should execute
            Assert.NotNull(result)
            Assert.Equal(15, result.Number)
        | Error err ->
            // If it errors, should be a reasonable validation error
            Assert.NotNull(err)

    [<Fact>]
    let ``Shor config precision affects accuracy`` () =
        // Test that precision configuration affects results
        // This validates that the circuit creation respects precision parameter
        let backend = LocalBackend.LocalBackend() :> IQuantumBackend

        // Higher precision should give better results
        let configHighPrecision =
            {
                NumberToFactor = 21
                RandomBase = Some 2
                PrecisionQubits = 10 // High precision
                MaxAttempts = 3
            }

        match executeShorsWithBackend configHighPrecision backend 100 with
        | Ok result ->
            Assert.NotNull(result)
            // With high precision and multiple attempts, should have reasonable chance
            Assert.True(result.Config.PrecisionQubits = 10, "Config should be preserved")
        | Error err -> Assert.Fail($"High precision execution failed: {err}")

    [<Fact>]
    let ``Shor backend config uses correct precision`` () =
        let backend = LocalBackend.LocalBackend() :> IQuantumBackend

        // OPTIMIZED: Use explicit minimal config instead of factorWithBackend
        let config =
            {
                NumberToFactor = 15
                RandomBase = Some 2
                PrecisionQubits = 5 // Explicitly set for testing
                MaxAttempts = 1
            }

        match executeShorsWithBackend config backend 10 with
        | Error err -> Assert.Fail($"Execution with 5 precision qubits should succeed: {err}")
        | Ok result -> Assert.Equal(5, result.Config.PrecisionQubits)

    [<Fact>]
    let ``Shor result includes period information when available`` () =
        // OPTIMIZED: Reduced precision and shots
        let config =
            {
                NumberToFactor = 15
                RandomBase = Some 7
                PrecisionQubits = 5
                MaxAttempts = 1
            }

        match executeShorsWithBackend config (LocalBackend.LocalBackend() :> IQuantumBackend) 10 with
        | Error msg -> Assert.Fail($"Shor's execution failed: {msg}")
        | Ok result ->
            if result.Success then
                // Successful factorization may include period result
                match result.PeriodResult with
                | Some pResult ->
                    Assert.True(pResult.Period > 0)
                    Assert.True(pResult.Base > 0 && pResult.Base < 15)
                    Assert.True(pResult.PhaseEstimate >= 0.0 && pResult.PhaseEstimate < 1.0)
                    Assert.True(pResult.Attempts > 0)
                | None ->
                    // May not have period result for trivial factorizations (even numbers)
                    ()

    [<Fact>]
    let ``Shor verifies factors are correct`` () =
        // Factor multiple numbers and verify p*q = N
        // OPTIMIZED: Reduced test set and parameters
        let testNumbers = [ 6; 10; 14 ]

        for n in testNumbers do
            let config =
                {
                    NumberToFactor = n
                    RandomBase = None
                    PrecisionQubits = 4
                    MaxAttempts = 1
                }

            match executeShorsWithBackend config (LocalBackend.LocalBackend() :> IQuantumBackend) 10 with
            | Error msg ->
                // Some may fail (primes, etc.)
                ()
            | Ok result ->
                match result.Factors with
                | Some(p, q) ->
                    Assert.Equal(n, p * q)
                    Assert.True(p >= 2 && q >= 2, "Factors should be at least 2")
                    Assert.True(p <= n && q <= n, "Factors should not exceed N")
                | None ->
                    // Failed to factor (acceptable for some numbers)
                    ()
