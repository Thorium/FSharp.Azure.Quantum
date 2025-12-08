namespace FSharp.Azure.Quantum.Tests

open Xunit
open FSharp.Azure.Quantum.Core
open FSharp.Azure.Quantum.Core.UnifiedBackendAbstraction
open FSharp.Azure.Quantum.GroverSearch
open FSharp.Azure.Quantum.Backends
open FSharp.Azure.Quantum.Topological

/// Tests for Unified Backend Grover's Algorithm
/// 
/// Verifies:
/// - Backend-agnostic execution (same code works on any backend)
/// - Correct search results across backends
/// - Performance characteristics
/// - Error handling
module GroverUnifiedTests =
    
    // ========================================================================
    // Test Helpers
    // ========================================================================
    
    /// Create local backend for testing
    let createLocalBackend () : IUnifiedQuantumBackend =
        LocalBackend.LocalBackend() :> IUnifiedQuantumBackend
    
    /// Create topological backend for testing
    let createTopologicalBackend () : IUnifiedQuantumBackend =
        TopologicalUnifiedBackendFactory.createIsing 20
    
    /// Test configuration with fewer shots for faster tests
    let testConfig = {
        GroverUnified.defaultConfig with
            Shots = 100
            SolutionThreshold = 0.15  // 15% threshold for test stability
    }
    
    /// Verify solution is found in results
    let assertSolutionFound (expected: int) (result: GroverUnified.GroverResult) =
        Assert.Contains(expected, result.Solutions)
    
    /// Verify multiple solutions are found
    let assertSolutionsFound (expected: int list) (result: GroverUnified.GroverResult) =
        for expectedValue in expected do
            Assert.Contains(expectedValue, result.Solutions)
    
    // ========================================================================
    // Basic Grover Search Tests (LocalBackend)
    // ========================================================================
    
    [<Fact>]
    let ``Grover finds single target value on LocalBackend`` () =
        let backend = createLocalBackend ()
        
        // Search for value 5 in 3-qubit space (0-7)
        match GroverUnified.searchSingle 5 3 backend testConfig with
        | Ok result ->
            assertSolutionFound 5 result
            Assert.True(result.Iterations > 0, "Should perform at least one iteration")
            Assert.True(result.SuccessProbability > 0.0, "Success probability should be > 0")
        | Error err ->
            Assert.True(false, $"Search failed: {err}")
    
    [<Fact>]
    let ``Grover finds value 0 (edge case)`` () =
        let backend = createLocalBackend ()
        
        match GroverUnified.searchSingle 0 2 backend testConfig with
        | Ok result ->
            assertSolutionFound 0 result
        | Error err ->
            Assert.True(false, $"Search failed: {err}")
    
    [<Fact>]
    let ``Grover finds maximum value (edge case)`` () =
        let backend = createLocalBackend ()
        
        // Search for 7 in 3-qubit space (max value)
        match GroverUnified.searchSingle 7 3 backend testConfig with
        | Ok result ->
            assertSolutionFound 7 result
        | Error err ->
            Assert.True(false, $"Search failed: {err}")
    
    [<Fact>]
    let ``Grover finds multiple targets`` () =
        let backend = createLocalBackend ()
        
        // Search for multiple values
        let targets = [2; 5; 7]
        match GroverUnified.searchMultiple targets 3 backend testConfig with
        | Ok result ->
            // At least one target should be found
            let foundAny = targets |> List.exists (fun t -> List.contains t result.Solutions)
            Assert.True(foundAny, "At least one target should be found")
        | Error err ->
            Assert.True(false, $"Search failed: {err}")
    
    [<Fact>]
    let ``Grover searchEven finds even numbers`` () =
        let backend = createLocalBackend ()
        
        match GroverUnified.searchEven 3 backend testConfig with
        | Ok result ->
            // Should find at least one even number (0, 2, 4, 6)
            let isEven x = x % 2 = 0
            let foundEven = result.Solutions |> List.exists isEven
            Assert.True(foundEven, "Should find at least one even number")
        | Error err ->
            Assert.True(false, $"Search failed: {err}")
    
    [<Fact>]
    let ``Grover searchOdd finds odd numbers`` () =
        let backend = createLocalBackend ()
        
        match GroverUnified.searchOdd 3 backend testConfig with
        | Ok result ->
            // Should find at least one odd number (1, 3, 5, 7)
            let isOdd x = x % 2 = 1
            let foundOdd = result.Solutions |> List.exists isOdd
            Assert.True(foundOdd, "Should find at least one odd number")
        | Error err ->
            Assert.True(false, $"Search failed: {err}")
    
    [<Fact>]
    let ``Grover with custom predicate works`` () =
        let backend = createLocalBackend ()
        
        // Search for numbers divisible by 3
        let isDivisibleBy3 x = x % 3 = 0
        
        match GroverUnified.searchWhere isDivisibleBy3 3 backend testConfig with
        | Ok result ->
            // Should find at least one number divisible by 3 (0, 3, 6)
            let foundMatch = result.Solutions |> List.exists isDivisibleBy3
            Assert.True(foundMatch, "Should find at least one number divisible by 3")
        | Error err ->
            Assert.True(false, $"Search failed: {err}")
    
    // ========================================================================
    // Backend Agnostic Tests (Same Code, Different Backends)
    // ========================================================================
    
    [<Fact>]
    let ``Grover works identically on LocalBackend and TopologicalBackend`` () =
        let localBackend = createLocalBackend ()
        let topBackend = createTopologicalBackend ()
        
        let target = 3
        let numQubits = 3
        
        // Run on local backend
        let localResult = GroverUnified.searchSingle target numQubits localBackend testConfig
        
        // Run on topological backend
        let topResult = GroverUnified.searchSingle target numQubits topBackend testConfig
        
        match localResult, topResult with
        | Ok lr, Ok tr ->
            // Both should find the target
            assertSolutionFound target lr
            assertSolutionFound target tr
            
            // Both should use same number of iterations
            Assert.Equal(lr.Iterations, tr.Iterations)
        | Error err, _ | _, Error err ->
            Assert.True(false, $"Search failed: {err}")
    
    [<Theory>]
    [<InlineData(0)>]
    [<InlineData(1)>]
    [<InlineData(2)>]
    [<InlineData(3)>]
    let ``Backend-agnostic search finds target on both backends`` (target: int) =
        let backends = [
            ("LocalBackend", createLocalBackend ())
            ("TopologicalBackend", createTopologicalBackend ())
        ]
        
        for (name, backend) in backends do
            match GroverUnified.searchSingle target 2 backend testConfig with
            | Ok result ->
                assertSolutionFound target result
            | Error err ->
                Assert.True(false, $"{name} search failed: {err}")
    
    // ========================================================================
    // Iteration Count Tests
    // ========================================================================
    
    [<Fact>]
    let ``Calculate optimal iterations for single target`` () =
        // For single target in 3-qubit space (N=8, M=1)
        // Optimal k ≈ (π/4) * √(8/1) ≈ 2.22 → 2 iterations
        let iterations = GroverUnified.calculateOptimalIterations 3 1
        Assert.InRange(iterations, 1, 3)
    
    [<Fact>]
    let ``Calculate optimal iterations for multiple targets`` () =
        // For 2 targets in 3-qubit space (N=8, M=2)
        // Optimal k ≈ (π/4) * √(8/2) = π/4 * 2 ≈ 1.57 → 2 iterations
        let iterations = GroverUnified.calculateOptimalIterations 3 2
        Assert.InRange(iterations, 1, 2)
    
    [<Fact>]
    let ``Auto-calculate iterations when config.Iterations is None`` () =
        let backend = createLocalBackend ()
        let configAutoIterations = { testConfig with Iterations = None }
        
        match GroverUnified.searchSingle 5 3 backend configAutoIterations with
        | Ok result ->
            // Should auto-calculate iterations (for single target in 8-element space)
            Assert.True(result.Iterations > 0, "Should perform at least one iteration")
            Assert.InRange(result.Iterations, 1, 3)
        | Error err ->
            Assert.True(false, $"Search failed: {err}")
    
    [<Fact>]
    let ``Use explicit iteration count when config.Iterations is Some`` () =
        let backend = createLocalBackend ()
        let configExplicit = { testConfig with Iterations = Some 5 }
        
        match GroverUnified.searchSingle 5 3 backend configExplicit with
        | Ok result ->
            Assert.Equal(5, result.Iterations)
        | Error err ->
            Assert.True(false, $"Search failed: {err}")
    
    // ========================================================================
    // Result Quality Tests
    // ========================================================================
    
    [<Fact>]
    let ``Grover result includes measurement distribution`` () =
        let backend = createLocalBackend ()
        
        match GroverUnified.searchSingle 5 3 backend testConfig with
        | Ok result ->
            Assert.NotEmpty(result.Measurements)
            
            // Distribution should sum to total shots
            let totalMeasured = result.Measurements |> Map.toSeq |> Seq.sumBy snd
            Assert.Equal(testConfig.Shots, totalMeasured)
        | Error err ->
            Assert.True(false, $"Search failed: {err}")
    
    [<Fact>]
    let ``Success probability reflects solution quality`` () =
        let backend = createLocalBackend ()
        
        match GroverUnified.searchSingle 5 3 backend testConfig with
        | Ok result ->
            // Success probability should be reasonable (> 0% for correct algorithm)
            Assert.True(result.SuccessProbability > 0.0, "Success probability should be > 0")
            Assert.True(result.SuccessProbability <= 1.0, "Success probability should be ≤ 1")
        | Error err ->
            Assert.True(false, $"Search failed: {err}")
    
    [<Fact>]
    let ``Execution time is measured`` () =
        let backend = createLocalBackend ()
        
        match GroverUnified.searchSingle 5 3 backend testConfig with
        | Ok result ->
            Assert.True(result.ExecutionTimeMs > 0.0, "Execution time should be > 0")
        | Error err ->
            Assert.True(false, $"Search failed: {err}")
    
    // ========================================================================
    // Error Handling Tests
    // ========================================================================
    
    [<Fact>]
    let ``Search rejects target out of range`` () =
        let backend = createLocalBackend ()
        
        // Target 10 is out of range for 3 qubits (valid range: 0-7)
        match GroverUnified.searchSingle 10 3 backend testConfig with
        | Ok _ ->
            Assert.True(false, "Should reject out-of-range target")
        | Error (QuantumError.ValidationError _) ->
            Assert.True(true)  // Expected error
        | Error err ->
            Assert.True(false, $"Wrong error type: {err}")
    
    [<Fact>]
    let ``Search rejects negative target`` () =
        let backend = createLocalBackend ()
        
        match GroverUnified.searchSingle -1 3 backend testConfig with
        | Ok _ ->
            Assert.True(false, "Should reject negative target")
        | Error (QuantumError.ValidationError _) ->
            Assert.True(true)  // Expected error
        | Error err ->
            Assert.True(false, $"Wrong error type: {err}")
    
    [<Fact>]
    let ``Search rejects empty target list`` () =
        let backend = createLocalBackend ()
        
        match GroverUnified.searchMultiple [] 3 backend testConfig with
        | Ok _ ->
            Assert.True(false, "Should reject empty target list")
        | Error (QuantumError.ValidationError _) ->
            Assert.True(true)  // Expected error
        | Error err ->
            Assert.True(false, $"Wrong error type: {err}")
    
    // ========================================================================
    // Format Tests
    // ========================================================================
    
    [<Fact>]
    let ``Format result produces readable output`` () =
        let result = {
            GroverUnified.GroverResult.Solutions = [5]
            Iterations = 2
            Measurements = Map.ofList [(5, 80); (3, 20)]
            SuccessProbability = 0.8
            ExecutionTimeMs = 15.5
        }
        
        let formatted = GroverUnified.formatResult result
        
        Assert.Contains("Found solutions: 5", formatted)
        Assert.Contains("Iterations: 2", formatted)
        Assert.Contains("80.00%", formatted)
        Assert.Contains("15.50 ms", formatted)
    
    [<Fact>]
    let ``Format result handles no solutions gracefully`` () =
        let result = {
            GroverUnified.GroverResult.Solutions = []
            Iterations = 1
            Measurements = Map.ofList [(0, 50); (1, 50)]
            SuccessProbability = 0.5
            ExecutionTimeMs = 10.0
        }
        
        let formatted = GroverUnified.formatResult result
        
        Assert.Contains("No solutions found", formatted)
    
    // ========================================================================
    // Integration Tests
    // ========================================================================
    
    [<Fact>]
    let ``Complete Grover workflow from initialization to measurement`` () =
        let backend = createLocalBackend ()
        
        // Complete workflow test
        let target = 5
        let numQubits = 3
        let config = {
            Iterations = Some 2  // Explicit iterations
            Shots = 200
            SuccessThreshold = 0.5
            SolutionThreshold = 0.10
        }
        
        match GroverUnified.searchSingle target numQubits backend config with
        | Ok result ->
            // Verify all components
            assertSolutionFound target result
            Assert.Equal(2, result.Iterations)
            Assert.Equal(200, result.Measurements |> Map.toSeq |> Seq.sumBy snd)
            Assert.True(result.ExecutionTimeMs > 0.0)
            Assert.NotEmpty(result.Solutions)
            
            // Verify solution quality
            Assert.True(result.SuccessProbability >= 0.1, "Should have reasonable success probability")
        | Error err ->
            Assert.True(false, $"Workflow failed: {err}")
    
    [<Fact>]
    let ``Grover scales to larger search spaces`` () =
        let backend = createLocalBackend ()
        
        // Test with 4 qubits (16-element search space)
        match GroverUnified.searchSingle 10 4 backend testConfig with
        | Ok result ->
            assertSolutionFound 10 result
            // Should use more iterations for larger space
            Assert.True(result.Iterations >= 2, "Larger space should need more iterations")
        | Error err ->
            Assert.True(false, $"Search failed: {err}")
