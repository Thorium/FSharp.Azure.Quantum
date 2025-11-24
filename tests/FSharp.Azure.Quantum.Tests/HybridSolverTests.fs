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
        
        // Assert: Should use classical method
        match result with
        | Ok solution ->
            Assert.Equal(HybridSolver.SolverMethod.Classical, solution.Method)
            Assert.NotNull(solution.Result)
            Assert.NotEmpty(solution.Reasoning)
            Assert.Contains("classical", solution.Reasoning.ToLower())
            Assert.True(solution.ElapsedMs > 0.0)
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
        
        // Assert: Should use classical method
        match result with
        | Ok solution ->
            Assert.Equal(HybridSolver.SolverMethod.Classical, solution.Method)
            Assert.NotNull(solution.Result)
            Assert.NotEmpty(solution.Reasoning)
            Assert.Contains("classical", solution.Reasoning.ToLower())
            Assert.True(solution.ElapsedMs > 0.0)
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
        | Error msg -> Assert.Fail($"Expected Ok but got Error: {msg}")
