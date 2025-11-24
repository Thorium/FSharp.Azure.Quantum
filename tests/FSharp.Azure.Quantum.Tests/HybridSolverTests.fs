namespace FSharp.Azure.Quantum.Tests

open System
open Xunit
open FSharp.Azure.Quantum.Classical

module HybridSolverTests =

    // Helper function to create a simple TSP problem (distance matrix)
    let createTspDistanceMatrix (n: int) : float[,] =
        Array2D.init n n (fun i j -> if i = j then 0.0 else float (abs (i - j)))

    [<Fact>]
    let ``Small TSP problem should route to classical solver`` () =
        // Arrange: Small 5-city TSP problem
        let distances = createTspDistanceMatrix 5
        
        // Act: Solve using HybridSolver (should automatically choose classical)
        let result = HybridSolver.solveTsp distances None None None
        
        // Assert: Should use classical method with valid solution
        match result with
        | Ok solution ->
            // Verify method selection
            Assert.Equal(HybridSolver.SolverMethod.Classical, solution.Method)
            
            // Verify TSP solution is valid
            Assert.Equal(5, solution.Result.Tour.Length)
            Assert.True(solution.Result.TourLength > 0.0, "Tour length should be positive")
            Assert.True(solution.Result.Iterations >= 0, "Iterations should be non-negative")
            
            // Verify reasoning contains meaningful information
            Assert.False(String.IsNullOrWhiteSpace(solution.Reasoning), "Reasoning should not be empty")
            Assert.Contains("classical", solution.Reasoning.ToLower())
            
            // Verify timing
            Assert.True(solution.ElapsedMs > 0.0, "Elapsed time should be positive")
            
            // Verify recommendation was populated
            Assert.True(solution.Recommendation.IsSome, "Recommendation should be provided")
            match solution.Recommendation with
            | Some recommendation -> Assert.Equal(5, recommendation.ProblemSize)
            | None -> ()
        | Error msg -> Assert.Fail($"Expected Ok but got Error: {msg}")

    [<Fact>]
    let ``Small Portfolio problem should route to classical solver`` () =
        // Arrange: Small 3-asset portfolio problem
        let assets : PortfolioSolver.Asset list = [
            { Symbol = "AAPL"; ExpectedReturn = 0.12; Risk = 0.18; Price = 150.0 }
            { Symbol = "MSFT"; ExpectedReturn = 0.10; Risk = 0.15; Price = 300.0 }
            { Symbol = "GOOGL"; ExpectedReturn = 0.15; Risk = 0.20; Price = 2800.0 }
        ]
        let constraints : PortfolioSolver.Constraints = {
            Budget = 10000.0
            MinHolding = 0.0
            MaxHolding = 5000.0
        }
        
        // Act: Solve using HybridSolver
        let result = HybridSolver.solvePortfolio assets constraints None None None
        
        // Assert: Should use classical method with valid portfolio
        match result with
        | Ok solution ->
            // Verify method selection
            Assert.Equal(HybridSolver.SolverMethod.Classical, solution.Method)
            
            // Verify portfolio solution is valid
            Assert.True(solution.Result.TotalValue <= constraints.Budget, 
                        "Portfolio value should not exceed budget")
            Assert.True(solution.Result.TotalValue > 0.0, 
                        "Portfolio should have positive value")
            Assert.True(List.isEmpty solution.Result.Allocations |> not, 
                        "Portfolio should have allocations")
            Assert.True(solution.Result.ExpectedReturn >= 0.0, 
                        "Expected return should be non-negative")
            
            // Verify reasoning
            Assert.False(String.IsNullOrWhiteSpace(solution.Reasoning))
            Assert.Contains("classical", solution.Reasoning.ToLower())
            
            // Verify timing and recommendation
            Assert.True(solution.ElapsedMs > 0.0)
            Assert.True(solution.Recommendation.IsSome)
        | Error msg -> Assert.Fail($"Expected Ok but got Error: {msg}")

    [<Fact>]
    let ``Large TSP problem should consider quantum (but use classical for now)`` () =
        // Arrange: Large 60-city TSP problem (should recommend quantum)
        let distances = createTspDistanceMatrix 60
        
        // Act: Solve using HybridSolver
        let result = HybridSolver.solveTsp distances None None None
        
        // Assert: Should currently use classical (quantum not yet implemented)
        match result with
        | Ok solution ->
            Assert.Equal(HybridSolver.SolverMethod.Classical, solution.Method)
            Assert.NotNull(solution.Result)
            // Should mention that quantum was considered
            Assert.True(
                solution.Reasoning.Contains("quantum", StringComparison.OrdinalIgnoreCase),
                "Reasoning should mention quantum consideration"
            )
        | Error msg -> Assert.Fail($"Expected Ok but got Error: {msg}")

    [<Fact>]
    let ``Forcing classical method should override advisor recommendation`` () =
        // Arrange: Large problem that would normally recommend quantum
        let distances = createTspDistanceMatrix 60
        
        // Act: Force classical method
        let result = HybridSolver.solveTsp distances None None (Some HybridSolver.SolverMethod.Classical)
        
        // Assert: Should use classical despite large problem size
        match result with
        | Ok solution ->
            Assert.Equal(HybridSolver.SolverMethod.Classical, solution.Method)
            Assert.Contains("forced", solution.Reasoning.ToLower())
            
            // Recommendation should be None when method is forced
            Assert.True(solution.Recommendation.IsNone, 
                        "Recommendation should be None when method forced")
            
            // Solution should still be valid
            Assert.Equal(60, solution.Result.Tour.Length)
            Assert.True(solution.Result.TourLength > 0.0)
        | Error msg -> Assert.Fail($"Expected Ok but got Error: {msg}")

    [<Fact>]
    let ``Forcing quantum method should return error`` () =
        // Arrange: Any TSP problem
        let distances = createTspDistanceMatrix 5
        
        // Act: Force quantum method (not implemented yet)
        let result = HybridSolver.solveTsp distances None None (Some HybridSolver.SolverMethod.Quantum)
        
        // Assert: Should return error
        match result with
        | Ok _ -> Assert.Fail("Expected Error for forced quantum method")
        | Error msg -> 
            Assert.Contains("quantum", msg.ToLower())
            Assert.Contains("not yet implemented", msg.ToLower())
