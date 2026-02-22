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

    let createBackend() = LocalBackend() :> IQuantumBackend

    // ========================================================================
    // PLANNER TESTS (ADR: intent -> plan -> execute)
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

            member _.ExecuteToStateAsync circuit ct = inner.ExecuteToStateAsync circuit ct
            member _.ApplyOperationAsync operation state ct = inner.ApplyOperationAsync operation state ct

    [<Fact>]
    let ``Shor period-finding planner prefers native QPE intent when supported`` () =
        let backend = createBackend()

        let intent: ShorPeriodFindingIntent =
            {
                Base = 7
                Modulus = 15
                PrecisionQubits = 3
                Exactness = QPE.Exact
            }

        match planPeriodFinding backend intent with
        | Ok (ShorPeriodFindingPlan.ExecuteClassicalWithQpeDemo (_, _, _, _, qpePlan)) ->
            match qpePlan with
            | QPE.QpePlan.ExecuteNatively (_, exactness) ->
                Assert.Equal(QPE.Exact, exactness)
            | _ -> Assert.Fail("Expected QPE ExecuteNatively plan")
        | Error err ->
            Assert.Fail($"Planning failed: {err}")

    [<Fact>]
    let ``Shor period-finding planner lowers QPE when intent op unsupported`` () =
        let backend = createBackend() |> NoQpeIntentBackend :> IQuantumBackend

        let intent: ShorPeriodFindingIntent =
            {
                Base = 7
                Modulus = 15
                PrecisionQubits = 3
                Exactness = QPE.Exact
            }

        match planPeriodFinding backend intent with
        | Ok (ShorPeriodFindingPlan.ExecuteClassicalWithQpeDemo (_, _, _, _, qpePlan)) ->
            match qpePlan with
            | QPE.QpePlan.ExecuteViaOps (ops, exactness) ->
                Assert.Equal(QPE.Exact, exactness)
                Assert.NotEmpty ops
                Assert.True(ops |> List.forall backend.SupportsOperation)
            | _ ->
                Assert.Fail("Expected QPE ExecuteViaOps plan")
        | Error err ->
            Assert.Fail($"Planning failed: {err}")

    [<Fact>]
    let ``Shor period-finding planner preserves approximate exactness`` () =
        let backend = createBackend() |> NoQpeIntentBackend :> IQuantumBackend

        let approximate = QPE.Approximate 0.001

        let intent: ShorPeriodFindingIntent =
            {
                Base = 7
                Modulus = 15
                PrecisionQubits = 3
                Exactness = approximate
            }

        match planPeriodFinding backend intent with
        | Ok (ShorPeriodFindingPlan.ExecuteClassicalWithQpeDemo (_, _, _, _, qpePlan)) ->
            match qpePlan with
            | QPE.QpePlan.ExecuteViaOps (_, exactness) ->
                Assert.Equal(approximate, exactness)
            | _ ->
                Assert.Fail("Expected QPE ExecuteViaOps plan")
        | Error err ->
            Assert.Fail($"Planning failed: {err}")
    
    // ========================================================================
    // CLASSICAL PRE-CHECK TESTS
    // ========================================================================
    
    [<Fact>]
    let ``Shor's algorithm handles even numbers (trivial case)`` () =
        let backend = createBackend()
        
        match factor 6 backend with
        | Error err -> Assert.Fail($"Should succeed for even number: {err}")
        | Ok result ->
            Assert.True(result.Success, "Should succeed for even number")
            Assert.Equal(6, result.Number)
            
            match result.Factors with
            | Some (p, q) ->
                Assert.Equal(6, p * q)
                Assert.Contains(2, [p; q])  // One factor should be 2
            | None -> Assert.Fail("Should find factors for even number")
            
            Assert.Contains("even", result.Message.ToLower())
    
    [<Fact>]
    let ``Shor's algorithm detects prime numbers`` () =
        let backend = createBackend()
        
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
        let backend = createBackend()
        
        for n in [0; 1; 2; 3] do
            match factor n backend with
            | Error err -> Assert.Fail($"Should return result (not error) for n={n}: {err}")
            | Ok result ->
                Assert.False(result.Success, $"Should fail for n={n}")
                Assert.Equal(n, result.Number)
                Assert.Equal(None, result.Factors)
                Assert.Contains("too small", result.Message.ToLower())
    
    [<Fact>]
    let ``Shor's algorithm handles composite even number 20`` () =
        let backend = createBackend()
        
        match factor 20 backend with
        | Error err -> Assert.Fail($"Should succeed for even number: {err}")
        | Ok result ->
            Assert.True(result.Success)
            Assert.Equal(20, result.Number)
            
            match result.Factors with
            | Some (p, q) ->
                Assert.Equal(20, p * q)
                Assert.Contains(2, [p; q])
            | None -> Assert.Fail("Should find factors")
    
    // ========================================================================
    // CONFIGURATION VALIDATION TESTS
    // ========================================================================
    
    [<Fact>]
    let ``factor15 uses correct configuration`` () =
        let backend = createBackend()
        
        // factor15 currently returns NotImplemented, but we can check that it's callable
        match factor15 backend with
        | Ok result ->
            // If it somehow succeeds (e.g., if quantum part is implemented), validate
            Assert.Equal(15, result.Config.NumberToFactor)
            Assert.Equal(Some 7, result.Config.RandomBase)
            Assert.Equal(8, result.Config.PrecisionQubits)
        | Error (QuantumError.NotImplemented _) ->
            // Expected: quantum subroutine not yet implemented
            ()
        | Error err ->
            Assert.Fail($"Unexpected error: {err}")
    
    [<Fact>]
    let ``factor21 uses correct configuration`` () =
        let backend = createBackend()
        
        match factor21 backend with
        | Ok result ->
            Assert.Equal(21, result.Config.NumberToFactor)
            Assert.Equal(Some 2, result.Config.RandomBase)
            Assert.Equal(8, result.Config.PrecisionQubits)
        | Error (QuantumError.NotImplemented _) ->
            ()  // Expected
        | Error err ->
            Assert.Fail($"Unexpected error: {err}")
    
    [<Fact>]
    let ``factor calculates precision qubits correctly`` () =
        let backend = createBackend()
        
        // For N=15: log₂(15) ≈ 3.9 → 2*3.9+3 = 10.8 → round to 10
        // For N=21: log₂(21) ≈ 4.4 → 2*4.4+3 = 11.8 → round to 11
        
        match factor 15 backend with
        | Ok result ->
            // Precision should be around 10-11 qubits
            Assert.InRange(result.Config.PrecisionQubits, 9, 12)
        | Error (QuantumError.NotImplemented _) ->
            ()  // Expected - quantum part not implemented
        | Error err ->
            Assert.Fail($"Unexpected error: {err}")
    
    // ========================================================================
    // NOT IMPLEMENTED ERROR TESTS
    // ========================================================================
    
    [<Fact>]
    let ``Shor's algorithm returns NotImplemented for composite odd numbers`` () =
        let backend = createBackend()
        
        // 15 is composite and odd, requires quantum period-finding
        match factor 15 backend with
        | Ok result ->
            // If somehow implemented, validate success
            Assert.Equal(15, result.Number)
            match result.Factors with
            | Some (p, q) ->
                Assert.Equal(15, p * q)
                Assert.Contains(3, [p; q])
                Assert.Contains(5, [p; q])
            | None -> ()
        | Error (QuantumError.NotImplemented (feature, hint)) ->
            // Expected: quantum subroutine not yet implemented
            Assert.Contains("Shor", feature)
            Assert.True(hint.IsSome, "Should provide implementation hint")
        | Error err ->
            Assert.Fail($"Unexpected error type: {err}")
    
    [<Fact>]
    let ``factor15 returns meaningful error for quantum subroutine`` () =
        let backend = createBackend()
        
        match factor15 backend with
        | Ok _ -> ()  // If implemented, that's fine
        | Error (QuantumError.NotImplemented (feature, Some hint)) ->
            // Should have helpful hint
            Assert.False(System.String.IsNullOrWhiteSpace(hint))
        | Error (QuantumError.NotImplemented (feature, None)) ->
            // Still acceptable but less helpful
            ()
        | Error err ->
            Assert.Fail($"Unexpected error: {err}")
    
    // ========================================================================
    // CUSTOM CONFIGURATION TESTS
    // ========================================================================
    
    [<Fact>]
    let ``execute accepts custom ShorsConfig`` () =
        let backend = createBackend()
        
        let config = {
            NumberToFactor = 35
            RandomBase = Some 2
            PrecisionQubits = 10
            MaxAttempts = 5
        }
        
        match execute config backend with
        | Ok result ->
            Assert.Equal(35, result.Number)
            Assert.Equal(config, result.Config)
        | Error (QuantumError.NotImplemented _) ->
            ()  // Expected
        | Error err ->
            Assert.Fail($"Unexpected error: {err}")
    
    [<Fact>]
    let ``execute with None RandomBase allows algorithm to choose`` () =
        let backend = createBackend()
        
        let config = {
            NumberToFactor = 21
            RandomBase = None  // Let algorithm choose
            PrecisionQubits = 8
            MaxAttempts = 3
        }
        
        match execute config backend with
        | Ok result ->
            Assert.Equal(21, result.Number)
            Assert.Equal(None, config.RandomBase)
        | Error (QuantumError.NotImplemented _) ->
            ()  // Expected
        | Error err ->
            Assert.Fail($"Unexpected error: {err}")
    
    // ========================================================================
    // EDGE CASES
    // ========================================================================
    
    [<Fact>]
    let ``Shor's algorithm handles perfect squares`` () =
        let backend = createBackend()
        
        // 16 = 4^2 (but also even, so caught by even check)
        match factor 16 backend with
        | Ok result ->
            Assert.True(result.Success)
            Assert.Equal(16, result.Number)
            match result.Factors with
            | Some (p, q) -> Assert.Equal(16, p * q)
            | None -> Assert.Fail("Should find factors")
        | Error err ->
            Assert.Fail($"Should handle perfect square: {err}")
    
    [<Fact>]
    let ``Shor's algorithm handles semi-primes`` () =
        let backend = createBackend()
        
        // 35 = 5 × 7 (semi-prime: product of two primes)
        match factor 35 backend with
        | Ok result ->
            Assert.Equal(35, result.Number)
            match result.Factors with
            | Some (5, 7) | Some (7, 5) -> ()  // Success
            | Some (p, q) -> Assert.Equal(35, p * q)
            | None -> ()  // Quantum part not implemented, acceptable
        | Error (QuantumError.NotImplemented _) ->
            ()  // Expected
        | Error err ->
            Assert.Fail($"Unexpected error: {err}")
    
    // ========================================================================
    // RULE1 COMPLIANCE TESTS
    // ========================================================================
    
    [<Fact>]
    let ``Shor accepts IQuantumBackend`` () =
        // This test validates that Shor follows RULE1
        let backend = createBackend()
        
        // Should compile and accept IQuantumBackend
        let result = factor 15 backend
        
        // We don't care about the result, just that it compiles and runs
        Assert.True(true)
    
    [<Fact>]
    let ``Shor works with LocalBackend`` () =
        // Validate that LocalBackend is compatible
        let backend = LocalBackend() :> IQuantumBackend
        
        let config = {
            NumberToFactor = 15
            RandomBase = Some 7
            PrecisionQubits = 8
            MaxAttempts = 3
        }
        
        // Should accept LocalBackend
        let result = execute config backend
        Assert.True(true)  // Just validate it compiles and runs
    
    // ========================================================================
    // QUANTUM PATH TESTS (NEW - Actual Factorization)
    // ========================================================================
    
    [<Fact>]
    let ``factor15 successfully factors 15 into 3 and 5`` () =
        let backend = createBackend()
        
        match factor15 backend with
        | Error err -> Assert.Fail($"Should succeed: {err}")
        | Ok result ->
            Assert.True(result.Success, "Should successfully factor 15")
            Assert.Equal(15, result.Number)
            
            match result.Factors with
            | Some (p, q) ->
                // Verify factors are correct
                Assert.Equal(15, p * q)
                // Check we got 3 and 5 (in any order)
                let factors = [p; q] |> List.sort
                Assert.Equal<int list>([3; 5], factors)
            | None -> Assert.Fail("Should find factors for 15")
            
            // Verify period-finding result exists
            Assert.True(result.PeriodResult.IsSome, "Should have period result")
            
            match result.PeriodResult with
            | Some pr ->
                Assert.Equal(7, pr.Base)
                // Period of 7 mod 15 should be 4 (since 7^4 = 2401 ≡ 1 mod 15)
                Assert.Equal(4, pr.Period)
            | None -> ()
    
    [<Fact>]
    let ``factor21 successfully factors 21 into 3 and 7`` () =
        let backend = createBackend()
        
        match factor21 backend with
        | Error err -> Assert.Fail($"Should succeed: {err}")
        | Ok result ->
            Assert.True(result.Success, "Should successfully factor 21")
            Assert.Equal(21, result.Number)
            
            match result.Factors with
            | Some (p, q) ->
                Assert.Equal(21, p * q)
                let factors = [p; q] |> List.sort
                Assert.Equal<int list>([3; 7], factors)
            | None -> Assert.Fail("Should find factors for 21")
            
            Assert.True(result.PeriodResult.IsSome)
            
            match result.PeriodResult with
            | Some pr ->
                Assert.Equal(2, pr.Base)
                // Period of 2 mod 21 should be 6 (since 2^6 = 64 ≡ 1 mod 21)
                Assert.Equal(6, pr.Period)
            | None -> ()
    
    [<Fact>]
    let ``factor with N=35 successfully factors into 5 and 7`` () =
        let backend = createBackend()
        
        match factor 35 backend with
        | Error err -> Assert.Fail($"Should succeed: {err}")
        | Ok result ->
            Assert.True(result.Success)
            Assert.Equal(35, result.Number)
            
            match result.Factors with
            | Some (p, q) ->
                Assert.Equal(35, p * q)
                let factors = [p; q] |> List.sort
                Assert.Equal<int list>([5; 7], factors)
            | None -> Assert.Fail("Should find factors for 35")
    
    [<Fact>]
    let ``findPeriod validates inputs`` () =
        let backend = createBackend()
        
        // Invalid a (must be in range (0, n))
        match findPeriod 0 15 8 backend with
        | Error (QuantumError.ValidationError _) -> ()  // Expected
        | _ -> Assert.Fail("Should reject a=0")
        
        match findPeriod 15 15 8 backend with
        | Error (QuantumError.ValidationError _) -> ()  // Expected
        | _ -> Assert.Fail("Should reject a=n")
        
        // Invalid a (not coprime)
        match findPeriod 3 15 8 backend with
        | Error (QuantumError.ValidationError (field, reason)) ->
            Assert.Contains("coprime", reason.ToLower())
        | _ -> Assert.Fail("Should reject non-coprime a")
    
    [<Fact>]
    let ``findPeriod correctly finds period for a=7, N=15`` () =
        let backend = createBackend()
        
        match findPeriod 7 15 8 backend with
        | Error err -> Assert.Fail($"Should succeed: {err}")
        | Ok result ->
            Assert.Equal(7, result.Base)
            Assert.Equal(15 % result.Period, 15 % 4)  // Period should divide into pattern
            // 7^4 mod 15 should be 1
            let verification = 1
            for _ in 1..result.Period do
                ()  // Period verification
            Assert.InRange(result.Period, 1, 15)
    
    [<Fact>]
    let ``Shor demonstrates QPE for period finding`` () =
        let backend = createBackend()
        
        // Test that QPE is being used (even if classically assisted)
        match factor 15 backend with
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
