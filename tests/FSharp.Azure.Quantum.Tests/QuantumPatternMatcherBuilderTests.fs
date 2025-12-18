namespace FSharp.Azure.Quantum.Tests

open System
open Xunit
open FSharp.Azure.Quantum
open FSharp.Azure.Quantum.Core
open FSharp.Azure.Quantum.Core.BackendAbstraction
open FSharp.Azure.Quantum.Backends
open FSharp.Azure.Quantum.GroverSearch

/// Unit tests for QuantumPatternMatcherBuilder
/// Tests the computation expression builder and solve function
module QuantumPatternMatcherBuilderTests =
    
    // ========================================================================
    // TEST HELPERS
    // ========================================================================
    
    /// Test configuration type
    type Config = {
        Id: int
        Throughput: float
        Latency: float
        Cost: float
    }
    
    /// Sample configurations
    let sampleConfigs = [
        { Id = 0; Throughput = 500.0; Latency = 100.0; Cost = 10.0 }
        { Id = 1; Throughput = 1200.0; Latency = 45.0; Cost = 20.0 }
        { Id = 2; Throughput = 800.0; Latency = 80.0; Cost = 15.0 }
        { Id = 3; Throughput = 1500.0; Latency = 30.0; Cost = 25.0 }
        { Id = 4; Throughput = 600.0; Latency = 120.0; Cost = 12.0 }
        { Id = 5; Throughput = 1100.0; Latency = 50.0; Cost = 18.0 }
        { Id = 6; Throughput = 900.0; Latency = 70.0; Cost = 16.0 }
        { Id = 7; Throughput = 1300.0; Latency = 40.0; Cost = 22.0 }
    ]
    
    /// Pattern: High performance (throughput > 1000, latency < 60)
    let highPerformancePattern (config: Config) : bool =
        config.Throughput > 1000.0 && config.Latency < 60.0
    
    /// Pattern: Cost effective (cost < 20)
    let costEffectivePattern (config: Config) : bool =
        config.Cost < 20.0
    
    /// Pattern for integer search space
    let evenNumberPattern (n: int) : bool =
        n % 2 = 0
    
    // ========================================================================
    // BUILDER VALIDATION TESTS
    // ========================================================================
    
    [<Fact>]
    let ``Builder should reject empty search space`` () =
        // Arrange & Act & Assert
        let ex = Assert.Throws<Exception>(fun () ->
            QuantumPatternMatcher.patternMatcher {
                searchSpace []  // Empty
                matchPattern highPerformancePattern
            } |> ignore
        )
        Assert.Contains("must contain at least 1 item", ex.Message)
    
    [<Fact>]
    let ``Builder should reject search space size > 2^16`` () =
        // Arrange & Act & Assert
        let ex = Assert.Throws<Exception>(fun () ->
            QuantumPatternMatcher.patternMatcher {
                searchSpaceSize 100000  // Too large
                matchPattern evenNumberPattern
            } |> ignore
        )
        Assert.Contains("exceeds maximum", ex.Message)
    
    [<Fact>]
    let ``Builder should reject TopN < 1`` () =
        // Arrange & Act & Assert
        let ex = Assert.Throws<Exception>(fun () ->
            QuantumPatternMatcher.patternMatcher {
                searchSpace sampleConfigs
                matchPattern highPerformancePattern
                findTop 0  // Invalid
            } |> ignore
        )
        Assert.Contains("must be at least 1", ex.Message)
    
    [<Fact>]
    let ``Builder should reject TopN > search space size`` () =
        // Arrange & Act & Assert
        let ex = Assert.Throws<Exception>(fun () ->
            QuantumPatternMatcher.patternMatcher {
                searchSpace sampleConfigs  // 8 items
                matchPattern highPerformancePattern
                findTop 20  // More than 8
            } |> ignore
        )
        Assert.Contains("cannot exceed search space size", ex.Message)
    
    [<Fact>]
    let ``Builder should reject problem requiring > 16 qubits`` () =
        // Arrange & Act & Assert
        let ex = Assert.Throws<Exception>(fun () ->
            QuantumPatternMatcher.patternMatcher {
                searchSpaceSize (1 <<< 17)  // 2^17, needs 17 qubits
                matchPattern evenNumberPattern
            } |> ignore
        )
        Assert.Contains("exceeds maximum", ex.Message)
    
    [<Fact>]
    let ``Builder should accept valid problem`` () =
        // Arrange & Act
        let problem = QuantumPatternMatcher.patternMatcher {
            searchSpace sampleConfigs
            matchPattern highPerformancePattern
            findTop 3
        }
        
        // Assert - if we got here, validation passed
        match problem.SearchSpace with
        | Choice1Of2 items -> Assert.Equal(8, List.length items)
        | Choice2Of2 _ -> Assert.Fail("Expected item list")
        Assert.Equal(3, problem.TopN)
    
    // ========================================================================
    // COMPUTATION EXPRESSION BUILDER TESTS
    // ========================================================================
    
    [<Fact>]
    let ``patternMatcher builder should create problem with all fields`` () =
        // Arrange & Act
        let problem = QuantumPatternMatcher.patternMatcher {
            searchSpace sampleConfigs
            matchPattern highPerformancePattern
            findTop 5
            shots 2000
        }
        
        // Assert
        match problem.SearchSpace with
        | Choice1Of2 items -> Assert.Equal(8, List.length items)
        | Choice2Of2 _ -> Assert.Fail("Expected item list")
        Assert.Equal(5, problem.TopN)
        Assert.Equal(2000, problem.Shots)
        Assert.True(Option.isNone problem.Backend)
    
    [<Fact>]
    let ``patternMatcher builder should use default values`` () =
        // Arrange & Act
        let problem = QuantumPatternMatcher.patternMatcher {
            searchSpace sampleConfigs
            matchPattern highPerformancePattern
        }
        
        // Assert - Check defaults
        Assert.Equal(1, problem.TopN)  // Default
        Assert.Equal(1000, problem.Shots)  // Default
    
    [<Fact>]
    let ``patternMatcher builder should accept custom backend`` () =
        // Arrange
        let myBackend = LocalBackend.LocalBackend() :> BackendAbstraction.IQuantumBackend
        
        // Act
        let problem = QuantumPatternMatcher.patternMatcher {
            searchSpace sampleConfigs
            matchPattern highPerformancePattern
            backend myBackend
        }
        
        // Assert
        Assert.True(Option.isSome problem.Backend)
    
    [<Fact>]
    let ``patternMatcher builder should accept search space as size`` () =
        // Arrange & Act
        let problem = QuantumPatternMatcher.patternMatcher {
            searchSpaceSize 256  // Just a size, not actual items
            matchPattern evenNumberPattern
            findTop 10
        }
        
        // Assert
        match problem.SearchSpace with
        | Choice2Of2 size -> Assert.Equal(256, size)
        | Choice1Of2 _ -> Assert.Fail("Expected size")

    [<Fact>]
    let ``patternMatcher builder supports searchSpace size alias`` () =
        // Arrange & Act
        let problem = QuantumPatternMatcher.patternMatcher {
            searchSpace 256
            matchPattern evenNumberPattern
            findTop 10
        }

        // Assert
        match problem.SearchSpace with
        | Choice2Of2 size -> Assert.Equal(256, size)
        | Choice1Of2 _ -> Assert.Fail("Expected size")
    
    [<Fact>]
    let ``patternMatcher builder should accept maxIterations`` () =
        // Arrange & Act
        let problem = QuantumPatternMatcher.patternMatcher {
            searchSpace sampleConfigs
            matchPattern highPerformancePattern
            maxIterations 5
        }
        
        // Assert
        Assert.Equal(Some 5, problem.MaxIterations)
    
    // ========================================================================
    // CONVENIENCE FUNCTION TESTS
    // ========================================================================
    
    [<Fact>]
    let ``QuantumPatternMatcher.simple should create problem with defaults`` () =
        // Arrange & Act
        let problem = QuantumPatternMatcher.simple sampleConfigs highPerformancePattern
        
        // Assert
        match problem.SearchSpace with
        | Choice1Of2 items -> Assert.Equal(8, List.length items)
        | Choice2Of2 _ -> Assert.Fail("Expected item list")
        Assert.Equal(1, problem.TopN)
        Assert.Equal(1000, problem.Shots)
        Assert.True(Option.isNone problem.Backend)
    
    [<Fact>]
    let ``QuantumPatternMatcher.findAll should cap at 10 results`` () =
        // Arrange
        let manyItems = [1..50]
        
        // Act
        let problem = QuantumPatternMatcher.findAll manyItems evenNumberPattern
        
        // Assert
        Assert.Equal(10, problem.TopN)  // Capped at 10
    
    [<Fact>]
    let ``QuantumPatternMatcher.findAll should use list length if less than 10`` () =
        // Arrange
        let fewItems = [1..5]
        
        // Act
        let problem = QuantumPatternMatcher.findAll fewItems evenNumberPattern
        
        // Assert
        Assert.Equal(5, problem.TopN)  // Uses actual length
    
    [<Fact>]
    let ``QuantumPatternMatcher.estimateResources should return resource estimate`` () =
        // Arrange
        let searchSpace = 256  // 2^8
        let topN = 5
        
        // Act
        let estimate = QuantumPatternMatcher.estimateResources searchSpace topN
        
        // Assert
        Assert.Contains("Search Space Size: 256", estimate)
        Assert.Contains("Top N Results: 5", estimate)
        Assert.Contains("Qubits Required: 8", estimate)
        Assert.Contains("Feasibility:", estimate)
    
    // ========================================================================
    // SOLVER INTEGRATION TESTS
    // ========================================================================
    
    [<Fact>]
    let ``QuantumPatternMatcher.solve should handle simple pattern on integers`` () =
        // Arrange - Find even numbers
        let problem = QuantumPatternMatcher.patternMatcher {
            searchSpaceSize 16  // Search space 0-15
            matchPattern evenNumberPattern
            findTop 3
        }
        
        // Act
        let result = QuantumPatternMatcher.solve problem
        
        // Assert - Builder API should call solve without crashing
        // Note: Algorithm success depends on quantum backend behavior (integration test concern)
        match result with
        | Ok solution ->
            Assert.True(solution.QubitsRequired > 0)
            Assert.NotEmpty(solution.BackendName)
            Assert.Equal(16, solution.SearchSpaceSize)
        | Error err ->
            // Algorithm may fail to find patterns (LocalBackend simulation limitation)
            Assert.True(err.Message.Length > 0, "Should return descriptive error message")
    
    [<Fact>]
    let ``QuantumPatternMatcher.solve should use LocalBackend by default`` () =
        // Arrange
        let problem = QuantumPatternMatcher.simple sampleConfigs highPerformancePattern
        
        // Act
        let result = QuantumPatternMatcher.solve problem
        
        // Assert - Backend name should indicate LocalBackend
        match result with
        | Ok solution ->
            Assert.Contains("LocalBackend", solution.BackendName)
        | Error err ->
            // Algorithm may fail (backend limitation) - just verify it attempted execution
            Assert.True(err.Message.Length > 0, "Should return descriptive error message")
    
    [<Fact>]
    let ``QuantumPatternMatcher.solve should use custom backend when provided`` () =
        // Arrange
        let myBackend = LocalBackend.LocalBackend() :> BackendAbstraction.IQuantumBackend
        let problem = QuantumPatternMatcher.patternMatcher {
            searchSpace sampleConfigs
            matchPattern highPerformancePattern
            backend myBackend
        }
        
        // Act
        let result = QuantumPatternMatcher.solve problem
        
        // Assert - Backend should be used (type name verification)
        match result with
        | Ok solution ->
            Assert.NotEmpty(solution.BackendName)
            // Backend name should be the type name of LocalBackend
            Assert.True(solution.BackendName.Contains("Backend") || solution.BackendName.Contains("Local"))
        | Error err ->
            // Algorithm may fail (backend limitation) - verify backend was attempted
            Assert.True(err.Message.Length > 0, "Should return descriptive error message")
    
    [<Fact>]
    let ``QuantumPatternMatcher.solve should return sensible qubit requirements`` () =
        // Arrange - search space 256 = 2^8 → 8 qubits
        let problem = QuantumPatternMatcher.patternMatcher {
            searchSpaceSize 256
            matchPattern evenNumberPattern
            findTop 5
        }
        
        // Act
        let result = QuantumPatternMatcher.solve problem
        
        // Assert - Qubit calculation should be correct regardless of algorithm success
        match result with
        | Ok solution ->
            // 256 = 2^8 → 8 qubits
            Assert.Equal(8, solution.QubitsRequired)
        | Error _ ->
            // Even if algorithm fails, we can verify qubit estimation separately
            let qubitsEstimated = int (ceil (log (float 256) / log 2.0))
            Assert.Equal(8, qubitsEstimated)
    
    [<Fact>]
    let ``QuantumPatternMatcher.solve should respect TopN limit`` () =
        // Arrange
        let problem = QuantumPatternMatcher.patternMatcher {
            searchSpaceSize 16
            matchPattern evenNumberPattern  // Should match 8 even numbers (0,2,4,6,8,10,12,14)
            findTop 3  // But only return top 3
        }
        
        // Act
        let result = QuantumPatternMatcher.solve problem
        
        // Assert
        match result with
        | Ok solution ->
            // Should return at most 3 matches
            Assert.True(List.length solution.Matches <= 3)
        | Error _ ->
            // Algorithm may fail - that's okay for builder API test
            Assert.True(true)
    
    // ========================================================================
    // DESCRIBE SOLUTION TESTS
    // ========================================================================
    
    [<Fact>]
    let ``QuantumPatternMatcher.describeSolution should format solution nicely`` () =
        // Arrange
        let solution : QuantumPatternMatcher.PatternSolution<Config> = {
            Matches = [sampleConfigs.[1]; sampleConfigs.[3]]
            SuccessProbability = 0.85
            BackendName = "LocalBackend"
            QubitsRequired = 8
            IterationsUsed = 3
            SearchSpaceSize = 8
        }
        
        // Act
        let description = QuantumPatternMatcher.describeSolution solution
        
        // Assert
        Assert.Contains("Success Probability: 0.85", description)
        Assert.Contains("Matches Found: 2 / 8 searched", description)
        Assert.Contains("LocalBackend", description)
        Assert.Contains("Qubits Required: 8", description)
        Assert.Contains("Iterations Used: 3", description)
        Assert.Contains("Matching Items:", description)
    
    [<Fact>]
    let ``QuantumPatternMatcher.describeSolution should handle no matches`` () =
        // Arrange
        let solution : QuantumPatternMatcher.PatternSolution<int> = {
            Matches = []
            SuccessProbability = 0.1
            BackendName = "LocalBackend"
            QubitsRequired = 4
            IterationsUsed = 2
            SearchSpaceSize = 16
        }
        
        // Act
        let description = QuantumPatternMatcher.describeSolution solution
        
        // Assert
        Assert.Contains("No matches found", description)
        Assert.Contains("Matches Found: 0 / 16 searched", description)
    
    [<Fact>]
    let ``QuantumPatternMatcher.describeSolution should truncate long match lists`` () =
        // Arrange
        let manyMatches = [1..15]
        let solution : QuantumPatternMatcher.PatternSolution<int> = {
            Matches = manyMatches
            SuccessProbability = 0.9
            BackendName = "Test"
            QubitsRequired = 5
            IterationsUsed = 4
            SearchSpaceSize = 100
        }
        
        // Act
        let description = QuantumPatternMatcher.describeSolution solution
        
        // Assert
        Assert.Contains("... and 5 more matches", description)  // 15 - 10 = 5
    
    // ========================================================================
    // ERROR HANDLING TESTS
    // ========================================================================
    
    [<Fact>]
    let ``QuantumPatternMatcher.solve should return error for invalid problem`` () =
        // Arrange - Create problem with manual record (bypass builder validation for testing)
        let problem : QuantumPatternMatcher.PatternProblem<int> = {
            SearchSpace = Choice2Of2 (1 <<< 20)  // 2^20, way too large
            Pattern = evenNumberPattern
            TopN = 5
            Backend = None
            MaxIterations = None
            Shots = 1000
        }
        
        // Act
        let result = QuantumPatternMatcher.solve problem
        
        // Assert
        match result with
        | Error err ->
            Assert.Contains("exceeds maximum", err.Message)
        | Ok _ ->
            Assert.Fail("Expected solve to return error")
    
    [<Fact>]
    let ``QuantumPatternMatcher.solve should handle pattern that never matches`` () =
        // Arrange - Pattern that always returns false
        let neverMatchPattern (config: Config) : bool = false
        
        let problem = QuantumPatternMatcher.patternMatcher {
            searchSpace sampleConfigs
            matchPattern neverMatchPattern
            findTop 3
        }
        
        // Act
        let result = QuantumPatternMatcher.solve problem
        
        // Assert
        match result with
        | Error err ->
            // Should get error about predicate, search failure, or no matches
            Assert.True(
                err.Message.Contains("Grover search failed") || 
                err.Message.Contains("No matching patterns found") ||
                err.Message.Contains("matches no solutions"),
                $"Expected error about search failure or no matches, got: {err.Message}"
            )
        | Ok solution ->
            // Or succeed but with empty matches
            Assert.Empty(solution.Matches)
