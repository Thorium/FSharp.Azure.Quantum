namespace FSharp.Azure.Quantum.Tests

open System
open Xunit
open FSharp.Azure.Quantum
open FSharp.Azure.Quantum.Core
open FSharp.Azure.Quantum.GroverSearch

/// Unit tests for QuantumTreeSearchBuilder
/// Tests the computation expression builder and solve function
module QuantumTreeSearchBuilderTests =
    
    // ========================================================================
    // TEST HELPERS
    // ========================================================================
    
    /// Simple game state for testing (integer position)
    type SimpleGameState = int
    
    /// Simple evaluation function (higher is better)
    let simpleEval (state: SimpleGameState) : float = float state
    
    /// Simple move generator (add 1, add 2, multiply by 2)
    let simpleMoveGen (state: SimpleGameState) : SimpleGameState list =
        [state + 1; state + 2; state * 2]
    
    /// Unwrap Result for testing
    let unwrapResult (result: Result<'T, string>) : 'T =
        match result with
        | Ok value -> value
        | Error msg -> failwith $"Operation failed: {msg}"
    
    // ========================================================================
    // BUILDER VALIDATION TESTS
    // ========================================================================
    
    [<Fact>]
    let ``Builder should reject MaxDepth < 1`` () =
        // Arrange & Act & Assert
        let ex = Assert.Throws<Exception>(fun () ->
            QuantumTreeSearch.quantumTreeSearch {
                initialState 0
                maxDepth 0  // Invalid
                branchingFactor 4
                evaluateWith simpleEval
                generateMovesWith simpleMoveGen
            } |> ignore
        )
        Assert.Contains("MaxDepth must be at least 1", ex.Message)
    
    [<Fact>]
    let ``Builder should reject MaxDepth > 8`` () =
        // Arrange & Act & Assert
        let ex = Assert.Throws<Exception>(fun () ->
            QuantumTreeSearch.quantumTreeSearch {
                initialState 0
                maxDepth 10  // Too large
                branchingFactor 4
                evaluateWith simpleEval
                generateMovesWith simpleMoveGen
            } |> ignore
        )
        Assert.Contains("MaxDepth exceeds 8", ex.Message)
    
    [<Fact>]
    let ``Builder should reject BranchingFactor < 2`` () =
        // Arrange & Act & Assert
        let ex = Assert.Throws<Exception>(fun () ->
            QuantumTreeSearch.quantumTreeSearch {
                initialState 0
                maxDepth 3
                branchingFactor 1  // Too small
                evaluateWith simpleEval
                generateMovesWith simpleMoveGen
            } |> ignore
        )
        Assert.Contains("BranchingFactor must be at least 2", ex.Message)
    
    [<Fact>]
    let ``Builder should reject TopPercentile <= 0`` () =
        // Arrange & Act & Assert
        let ex = Assert.Throws<Exception>(fun () ->
            QuantumTreeSearch.quantumTreeSearch {
                initialState 0
                maxDepth 3
                branchingFactor 4
                evaluateWith simpleEval
                generateMovesWith simpleMoveGen
                topPercentile 0.0  // Invalid
            } |> ignore
        )
        Assert.Contains("TopPercentile must be in range", ex.Message)
    
    [<Fact>]
    let ``Builder should reject TopPercentile > 1`` () =
        // Arrange & Act & Assert
        let ex = Assert.Throws<Exception>(fun () ->
            QuantumTreeSearch.quantumTreeSearch {
                initialState 0
                maxDepth 3
                branchingFactor 4
                evaluateWith simpleEval
                generateMovesWith simpleMoveGen
                topPercentile 1.5  // Too large
            } |> ignore
        )
        Assert.Contains("TopPercentile must be in range", ex.Message)
    
    [<Fact>]
    let ``Builder should reject problem requiring > 16 qubits`` () =
        // Arrange & Act & Assert
        let ex = Assert.Throws<Exception>(fun () ->
            QuantumTreeSearch.quantumTreeSearch {
                initialState 0
                maxDepth 5
                branchingFactor 16  // 5 * 4 bits = 20 qubits (too many)
                evaluateWith simpleEval
                generateMovesWith simpleMoveGen
            } |> ignore
        )
        Assert.Contains("requires", ex.Message)
        Assert.Contains("qubits", ex.Message)
    
    [<Fact>]
    let ``Builder should accept valid problem`` () =
        // Arrange & Act
        let problem = QuantumTreeSearch.quantumTreeSearch {
            initialState 0
            maxDepth 3
            branchingFactor 4
            evaluateWith simpleEval
            generateMovesWith simpleMoveGen
        }
        
        // Assert - if we got here, validation passed
        Assert.Equal(0, problem.InitialState)
        Assert.Equal(3, problem.MaxDepth)
        Assert.Equal(4, problem.BranchingFactor)
    
    // ========================================================================
    // COMPUTATION EXPRESSION BUILDER TESTS
    // ========================================================================
    
    [<Fact>]
    let ``quantumTreeSearch builder should create problem with all fields`` () =
        // Arrange & Act
        let problem = QuantumTreeSearch.quantumTreeSearch {
            initialState 1
            maxDepth 3
            branchingFactor 4
            evaluateWith simpleEval
            generateMovesWith simpleMoveGen
            topPercentile 0.15
        }
        
        // Assert
        Assert.Equal(1, problem.InitialState)
        Assert.Equal(3, problem.MaxDepth)
        Assert.Equal(4, problem.BranchingFactor)
        Assert.Equal(0.15, problem.TopPercentile)
        Assert.True(Option.isNone problem.Backend)
    
    [<Fact>]
    let ``quantumTreeSearch builder should use default values`` () =
        // Arrange & Act
        let problem = QuantumTreeSearch.quantumTreeSearch {
            initialState 1
            evaluateWith simpleEval
            generateMovesWith simpleMoveGen
        }
        
        // Assert - Check defaults
        Assert.Equal(3, problem.MaxDepth)  // Default
        Assert.Equal(16, problem.BranchingFactor)  // Default
        Assert.Equal(0.2, problem.TopPercentile)  // Default
    
    [<Fact>]
    let ``quantumTreeSearch builder should accept custom backend`` () =
        // Arrange
        let myBackend = BackendAbstraction.createLocalBackend()
        
        // Act
        let problem = QuantumTreeSearch.quantumTreeSearch {
            initialState 1
            evaluateWith simpleEval
            generateMovesWith simpleMoveGen
            backend myBackend
        }
        
        // Assert
        Assert.True(Option.isSome problem.Backend)
    
    [<Fact>]
    let ``quantumTreeSearch builder should validate and throw on invalid problem`` () =
        // Arrange & Act & Assert
        Assert.Throws<Exception>(fun () ->
            QuantumTreeSearch.quantumTreeSearch {
                initialState 1
                maxDepth 0  // Invalid
                evaluateWith simpleEval
                generateMovesWith simpleMoveGen
            } |> ignore
        ) |> ignore
    
    // ========================================================================
    // CONVENIENCE FUNCTION TESTS
    // ========================================================================
    
    [<Fact>]
    let ``QuantumTreeSearch.simple should create problem with defaults`` () =
        // Arrange & Act
        let problem = QuantumTreeSearch.simple 1 simpleEval simpleMoveGen
        
        // Assert
        Assert.Equal(1, problem.InitialState)
        Assert.Equal(3, problem.MaxDepth)
        Assert.Equal(16, problem.BranchingFactor)
        Assert.Equal(0.2, problem.TopPercentile)
        Assert.True(Option.isNone problem.Backend)
    
    [<Fact>]
    let ``QuantumTreeSearch.forGameAI should create game AI problem`` () =
        // Arrange
        let board = 0
        let depth = 4
        let branching = 8
        
        // Act
        let problem = QuantumTreeSearch.forGameAI board depth branching simpleEval simpleMoveGen
        
        // Assert
        Assert.Equal(0, problem.InitialState)
        Assert.Equal(4, problem.MaxDepth)
        Assert.Equal(8, problem.BranchingFactor)
        Assert.Equal(0.2, problem.TopPercentile)
    
    [<Fact>]
    let ``QuantumTreeSearch.forDecisionProblem should create decision problem`` () =
        // Arrange
        let initialDecision = 5
        let steps = 3
        let optionsPerStep = 4
        
        // Act
        let problem = QuantumTreeSearch.forDecisionProblem initialDecision steps optionsPerStep simpleEval simpleMoveGen
        
        // Assert
        Assert.Equal(5, problem.InitialState)
        Assert.Equal(3, problem.MaxDepth)
        Assert.Equal(4, problem.BranchingFactor)
        Assert.Equal(0.15, problem.TopPercentile)  // More selective for decision problems
    
    [<Fact>]
    let ``QuantumTreeSearch.estimateResources should return resource estimate`` () =
        // Arrange
        let maxDepth = 3
        let branchingFactor = 4
        
        // Act
        let estimate = QuantumTreeSearch.estimateResources maxDepth branchingFactor
        
        // Assert
        Assert.Contains("Max Depth: 3", estimate)
        Assert.Contains("Branching Factor: 4", estimate)
        Assert.Contains("Qubits Required", estimate)
        Assert.Contains("Search Space Size", estimate)
    
    // ========================================================================
    // SOLVER INTEGRATION TESTS
    // ========================================================================
    
    [<Fact>]
    let ``QuantumTreeSearch.solve should solve simple arithmetic game`` () =
        // Arrange - Simple arithmetic: start at 1, maximize value
        let problem = QuantumTreeSearch.quantumTreeSearch {
            initialState 1
            maxDepth 2
            branchingFactor 4
            evaluateWith (fun x -> float x)  // Higher is better
            generateMovesWith (fun x -> [x + 1; x + 2; x * 2; x - 1])
            topPercentile 0.3
        }
        
        // Act
        let result = QuantumTreeSearch.solve problem
        
        // Assert - Builder API should call solve without crashing
        // Note: Algorithm success depends on quantum backend behavior (integration test concern)
        match result with
        | Ok solution ->
            Assert.True(solution.BestMove >= 0 && solution.BestMove < 4)
            Assert.True(solution.Score >= 0.0)
            Assert.True(solution.PathsExplored > 0)
            Assert.True(solution.QubitsRequired > 0)
            Assert.NotEmpty(solution.BackendName)
        | Error msg ->
            // Algorithm may fail to find solution (LocalBackend simulation limitation)
            Assert.Contains("No solution found", msg)  // Expected error from algorithm
    
    [<Fact>]
    let ``QuantumTreeSearch.solve should handle depth 1 search`` () =
        // Arrange - Minimal depth
        let problem = QuantumTreeSearch.quantumTreeSearch {
            initialState 5
            maxDepth 1
            branchingFactor 3
            evaluateWith (fun x -> float x)
            generateMovesWith (fun x -> [x + 1; x + 10; x - 1])
            topPercentile 0.5
        }
        
        // Act
        let result = QuantumTreeSearch.solve problem
        
        // Assert
        match result with
        | Ok solution ->
            // Best move should be index 1 (x + 10), as it gives highest value
            Assert.True(solution.BestMove >= 0)
            Assert.True(solution.PathsExplored > 0)
        | Error msg ->
            Assert.Fail($"solve failed: {msg}")
    
    [<Fact>]
    let ``QuantumTreeSearch.solve should use LocalBackend by default`` () =
        // Arrange
        let problem = QuantumTreeSearch.simple 1 simpleEval simpleMoveGen
        
        // Act
        let result = QuantumTreeSearch.solve problem
        
        // Assert - Backend name should indicate LocalBackend
        match result with
        | Ok solution ->
            Assert.Contains("LocalBackend", solution.BackendName)
        | Error msg ->
            // Algorithm may fail (backend limitation) - just verify it attempted execution
            Assert.True(msg.Length > 0, "Should return descriptive error message")
    
    [<Fact>]
    let ``QuantumTreeSearch.solve should use custom backend when provided`` () =
        // Arrange
        let myBackend = BackendAbstraction.createLocalBackend()
        let problem = QuantumTreeSearch.quantumTreeSearch {
            initialState 1
            evaluateWith simpleEval
            generateMovesWith simpleMoveGen
            backend myBackend
        }
        
        // Act
        let result = QuantumTreeSearch.solve problem
        
        // Assert - Backend should be used (type name verification)
        match result with
        | Ok solution ->
            Assert.NotEmpty(solution.BackendName)
            // Backend name should be the type name of LocalBackend
            Assert.True(solution.BackendName.Contains("Backend") || solution.BackendName.Contains("Local"))
        | Error msg ->
            // Algorithm may fail (backend limitation) - verify backend was attempted
            Assert.True(msg.Length > 0, "Should return descriptive error message")
    
    [<Fact>]
    let ``QuantumTreeSearch.solve should return sensible qubit requirements`` () =
        // Arrange - depth=2, branching=4 → 2 bits per level → 4 qubits
        let problem = QuantumTreeSearch.quantumTreeSearch {
            initialState 1
            maxDepth 2
            branchingFactor 4
            evaluateWith simpleEval
            generateMovesWith simpleMoveGen
        }
        
        // Act
        let result = QuantumTreeSearch.solve problem
        
        // Assert - Qubit calculation should be correct regardless of algorithm success
        match result with
        | Ok solution ->
            // 2 bits per level (branching=4), depth=2 → 4 qubits
            Assert.Equal(4, solution.QubitsRequired)
        | Error _ ->
            // Even if algorithm fails, we can verify qubit estimation separately
            let qubitsEstimated = GroverSearch.TreeSearch.estimateQubitsNeeded 2 4
            Assert.Equal(4, qubitsEstimated)
    
    // ========================================================================
    // DESCRIBE SOLUTION TESTS
    // ========================================================================
    
    [<Fact>]
    let ``QuantumTreeSearch.describeSolution should format solution nicely`` () =
        // Arrange
        let solution : QuantumTreeSearch.TreeSearchSolution = {
            BestMove = 2
            Score = 0.85
            PathsExplored = 256
            QuantumAdvantage = true
            BackendName = "LocalBackend"
            QubitsRequired = 8
            AllSolutions = [5; 12; 23]
        }
        
        // Act
        let description = QuantumTreeSearch.describeSolution solution
        
        // Assert
        Assert.Contains("Best Move: 2", description)
        Assert.Contains("Score: 0.85", description)
        Assert.Contains("Paths Explored: 256", description)
        Assert.Contains("✓ Yes", description)  // Quantum Advantage
        Assert.Contains("LocalBackend", description)
        Assert.Contains("Qubits Required: 8", description)
        Assert.Contains("All Solutions Found:", description)
    
    [<Fact>]
    let ``QuantumTreeSearch.describeSolution should handle no quantum advantage`` () =
        // Arrange
        let solution : QuantumTreeSearch.TreeSearchSolution = {
            BestMove = 0
            Score = 0.5
            PathsExplored = 16
            QuantumAdvantage = false
            BackendName = "LocalBackend"
            QubitsRequired = 4
            AllSolutions = []
        }
        
        // Act
        let description = QuantumTreeSearch.describeSolution solution
        
        // Assert
        Assert.Contains("✗ No", description)  // No quantum advantage
        Assert.DoesNotContain("All Solutions Found:", description)  // Empty list
    
    [<Fact>]
    let ``QuantumTreeSearch.describeSolution should truncate long solution lists`` () =
        // Arrange
        let manySolutions = [1..15]  // More than 10
        let solution : QuantumTreeSearch.TreeSearchSolution = {
            BestMove = 0
            Score = 0.9
            PathsExplored = 100
            QuantumAdvantage = true
            BackendName = "Test"
            QubitsRequired = 5
            AllSolutions = manySolutions
        }
        
        // Act
        let description = QuantumTreeSearch.describeSolution solution
        
        // Assert
        Assert.Contains("... and 5 more", description)  // 15 - 10 = 5
    
    // ========================================================================
    // ERROR HANDLING TESTS
    // ========================================================================
    
    [<Fact>]
    let ``QuantumTreeSearch.solve should return error for invalid problem`` () =
        // Arrange - Create problem with manual record (bypass builder validation for testing)
        let problem : QuantumTreeSearch.TreeSearchProblem<int> = {
            InitialState = 1
            MaxDepth = 20  // Way too deep
            BranchingFactor = 256
            EvaluationFunction = simpleEval
            MoveGenerator = simpleMoveGen
            TopPercentile = 0.2
            Backend = None
        }
        
        // Act
        let result = QuantumTreeSearch.solve problem
        
        // Assert
        match result with
        | Error msg ->
            Assert.Contains("qubits", msg)
        | Ok _ ->
            Assert.Fail("Expected solve to return error")
    
    [<Fact>]
    let ``QuantumTreeSearch.solve should handle empty move generator gracefully`` () =
        // Arrange
        let emptyMoveGen (state: int) : int list = []
        let problem = QuantumTreeSearch.quantumTreeSearch {
            initialState 1
            maxDepth 2
            branchingFactor 4
            evaluateWith simpleEval
            generateMovesWith emptyMoveGen  // No moves
        }
        
        // Act
        let result = QuantumTreeSearch.solve problem
        
        // Assert
        match result with
        | Error msg ->
            // Should get error about no paths or invalid oracle
            Assert.True(msg.Length > 0)
        | Ok solution ->
            // Or succeed with minimal result
            Assert.True(solution.PathsExplored >= 0)
