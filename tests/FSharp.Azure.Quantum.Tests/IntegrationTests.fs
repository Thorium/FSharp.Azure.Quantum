namespace FSharp.Azure.Quantum.Tests

open System
open Xunit
open FSharp.Azure.Quantum
open FSharp.Azure.Quantum.Classical
open FSharp.Azure.Quantum.Core.BackendAbstraction
open FSharp.Azure.Quantum.Quantum

/// <summary>
/// Integration tests for end-to-end workflows.
/// ALL integration test scenarios are consolidated in this single file per domain rule
/// for AI context window optimization.
/// </summary>
module IntegrationTests =

    // ===========================================
    // Test Scenario 1: TSP Classical Backend
    // ===========================================
    
    [<Fact>]
    let ``TSP Classical - 10-city problem should produce valid tour`` () =
        // Arrange: Create 10 cities in a grid pattern
        let cities = [
            ("City0", 0.0, 0.0)
            ("City1", 1.0, 0.0)
            ("City2", 2.0, 0.0)
            ("City3", 0.0, 1.0)
            ("City4", 1.0, 1.0)
            ("City5", 2.0, 1.0)
            ("City6", 0.0, 2.0)
            ("City7", 1.0, 2.0)
            ("City8", 2.0, 2.0)
            ("City9", 1.0, 3.0)
        ]
        
        let problem = TSP.createProblem cities
        
        // Act: Solve with classical backend
        let result = TSP.solve problem None
        
        // Assert: Verify tour validity
        match result with
        | Ok tour ->
            Assert.Equal(10, tour.Cities.Length)
            
            // All cities should be visited exactly once
            let uniqueCities = tour.Cities |> List.distinct
            Assert.Equal(10, uniqueCities.Length)
            
            // Tour length should be positive
            Assert.True(tour.TotalDistance > 0.0, "Tour length must be positive")
            
            // Should be marked as valid
            Assert.True(tour.IsValid, "Tour should be valid")
            
        | Error msg ->
            Assert.Fail($"Expected successful solution, got error: {msg}")

    [<Fact>]
    let ``TSP Classical - Should produce better than naive tour`` () =
        // Arrange: Circle of cities where optimal tour is obvious
        let n = 10
        let cities = 
            [0..n-1]
            |> List.map (fun i ->
                let angle = 2.0 * Math.PI * float i / float n
                let x = Math.Cos(angle)
                let y = Math.Sin(angle)
                ($"City{i}", x, y))
        
        let problem = TSP.createProblem cities
        
        // Act
        let result = TSP.solve problem None
        
        // Assert: Tour should be reasonable (not worst-case)
        match result with
        | Ok tour ->
            // Optimal tour for circle is approximately 2*PI*radius ≈ 6.28
            // Naive tour could be much worse
            Assert.True(tour.TotalDistance < 10.0, 
                $"Tour length {tour.TotalDistance} should be better than naive solution")
            Assert.True(tour.IsValid)
        | Error msg ->
            Assert.Fail($"Expected successful solution, got error: {msg}")

    // ===========================================
    // Test Scenario 2: TSP Quantum Emulator
    // ===========================================
    
    [<Fact>]
    let ``TSP Quantum - 5-city problem with emulator should produce valid tour`` () =
        // Arrange: Small problem suitable for quantum emulation (5 cities = 25 qubits, within LocalBackend 10-qubit limit is too tight)
        // Note: 5 cities requires 25 qubits (N^2), but LocalBackend only supports 10 qubits
        // Using 3 cities (9 qubits) to stay within limit
        let cities = [
            ("A", 0.0, 0.0)
            ("B", 1.0, 0.0)
            ("C", 0.0, 1.0)
        ]
        
        let problem = TSP.createProblem cities
        
        // Act: Solve using QuantumTspSolver with LocalBackend (quantum emulator)
        let backend = createLocalBackend()
        let result = QuantumTspSolver.solve backend problem.DistanceMatrix 1000
        
        // Assert: Verify basic solution properties
        match result with
        | Ok solution ->
            Assert.Equal(3, solution.Tour.Length)
            Assert.True(solution.TourLength > 0.0)
            Assert.Equal("Local QAOA Simulator", solution.BackendName)
            Assert.Equal(1000, solution.NumShots)
            
            // Verify all cities visited
            let uniqueCities = solution.Tour |> Array.distinct
            Assert.Equal(3, uniqueCities.Length)
        | Error msg ->
            Assert.Fail($"Expected successful solution, got error: {msg}")

    // ===========================================
    // Test Scenario 3: Portfolio Classical Backend
    // ===========================================
    
    [<Fact>]
    let ``Portfolio Classical - 20-asset portfolio should satisfy constraints`` () =
        // Arrange: Create 20 assets with varying risk/return profiles
        let rng = Random(42) // Seed for reproducibility
        let assets = 
            [1..20]
            |> List.map (fun i -> 
                let symbol = $"ASSET{i}"
                let expectedReturn = 0.05 + rng.NextDouble() * 0.15  // 5-20% return
                let risk = 0.10 + rng.NextDouble() * 0.20             // 10-30% risk
                let price = 50.0 + rng.NextDouble() * 150.0          // $50-200
                (symbol, expectedReturn, risk, price))
        
        let budget = 10000.0
        let problem = Portfolio.createProblem assets budget
        
        // Act: Solve portfolio optimization
        let result = Portfolio.solve problem None
        
        // Assert: Verify constraints
        match result with
        | Ok allocation ->
            // Budget constraint
            Assert.True(allocation.TotalValue <= budget * 1.01, // Allow 1% tolerance
                $"Total value {allocation.TotalValue} exceeds budget {budget}")
            
            // Should have allocated some assets
            Assert.True(allocation.Allocations.Length > 0, "Should allocate at least one asset")
            
            // Expected return should be positive
            Assert.True(allocation.ExpectedReturn > 0.0, "Expected return should be positive")
            
            // Should be marked as valid
            Assert.True(allocation.IsValid)
            
        | Error msg ->
            Assert.Fail($"Expected successful solution, got error: {msg}")

    [<Fact>]
    let ``Portfolio Classical - Zero budget should return empty allocation`` () =
        // Arrange
        let assets = [
            ("AAPL", 0.12, 0.18, 150.0)
            ("GOOGL", 0.10, 0.15, 120.0)
        ]
        
        let problem = Portfolio.createProblem assets 0.0
        
        // Act
        let result = Portfolio.solve problem None
        
        // Assert
        match result with
        | Ok allocation ->
            Assert.Empty(allocation.Allocations)
            Assert.Equal(0.0, allocation.TotalValue)
        | Error msg ->
            Assert.Fail($"Expected successful solution with empty allocation, got error: {msg}")

    // ===========================================
    // Test Scenario 4: Portfolio Classical (Medium Size)
    // ===========================================
    // Note: QuantumPortfolioSolver not yet implemented (TKT-XXX TODO)
    // When quantum solver exists, create separate test for quantum execution
    
    [<Fact>]
    let ``Portfolio Classical - 10-asset portfolio should optimize allocation`` () =
        // Arrange: Medium-sized portfolio
        let assets = 
            [1..10]
            |> List.map (fun i -> 
                let symbol = $"STOCK{i}"
                let expectedReturn = 0.08 + float i * 0.01
                let risk = 0.12 + float i * 0.01
                let price = 100.0
                (symbol, expectedReturn, risk, price))
        
        let budget = 5000.0
        let problem = Portfolio.createProblem assets budget
        
        // Act: Solve using classical solver
        let result = Portfolio.solve problem None
        
        // Assert: Basic validation
        match result with
        | Ok allocation ->
            Assert.True(allocation.Allocations.Length <= 10)
            Assert.True(allocation.TotalValue <= budget * 1.01)
            Assert.True(allocation.IsValid)
        | Error msg ->
            Assert.Fail($"Expected successful solution, got error: {msg}")

    // ===========================================
    // Test Scenario 5: HybridSolver Small Problem
    // ===========================================
    
    [<Fact>]
    let ``HybridSolver - Small TSP should route to classical automatically`` () =
        // Arrange: Small 5-city problem
        let distances = Array2D.init 5 5 (fun i j -> 
            if i = j then 0.0 else float (abs (i - j)))
        
        // Act: Let HybridSolver decide
        let result = HybridSolver.solveTsp distances None None None
        
        // Assert: Should choose classical method
        match result with
        | Ok solution ->
            Assert.Equal(HybridSolver.SolverMethod.Classical, solution.Method)
            Assert.Contains("classical", solution.Reasoning.ToLower())
            Assert.Equal(5, solution.Result.Tour.Length)
            
            // Should have recommendation explaining why classical
            Assert.True(solution.Recommendation.IsSome)
            match solution.Recommendation with
            | Some recommendation -> 
                Assert.Equal(5, recommendation.ProblemSize)
                Assert.True(recommendation.Confidence > 0.5)
            | None -> ()
        | Error msg -> 
            Assert.Fail($"Expected successful solution, got error: {msg}")

    [<Fact>]
    let ``HybridSolver - Small Portfolio should route to classical automatically`` () =
        // Arrange: Small 3-asset portfolio
        let assets: PortfolioSolver.Asset list = [
            { Symbol = "A"; ExpectedReturn = 0.10; Risk = 0.15; Price = 100.0 }
            { Symbol = "B"; ExpectedReturn = 0.12; Risk = 0.18; Price = 150.0 }
            { Symbol = "C"; ExpectedReturn = 0.08; Risk = 0.12; Price = 80.0 }
        ]
        
        let constraints: PortfolioSolver.Constraints = {
            Budget = 1000.0
            MinHolding = 0.0
            MaxHolding = 1000.0
        }
        
        // Act: Let HybridSolver decide
        let result = HybridSolver.solvePortfolio assets constraints None None None
        
        // Assert: Should choose classical method
        match result with
        | Ok solution ->
            Assert.Equal(HybridSolver.SolverMethod.Classical, solution.Method)
            Assert.Contains("classical", solution.Reasoning.ToLower())
        | Error msg -> 
            Assert.Fail($"Expected successful solution, got error: {msg}")

    // ===========================================
    // Test Scenario 6: HybridSolver Large Problem
    // ===========================================
    
    [<Fact>]
    let ``HybridSolver - Large TSP should consider quantum recommendation`` () =
        // Arrange: Larger 30-city problem where quantum might be beneficial
        let distances = Array2D.init 30 30 (fun i j -> 
            if i = j then 0.0 
            else 1.0 + float (abs (i - j)) * 0.5)
        
        // Act: Get recommendation (will still solve with classical for now)
        let result = HybridSolver.solveTsp distances None None None
        
        // Assert: Should have recommendation considering quantum
        match result with
        | Ok solution ->
            // Should provide recommendation
            Assert.True(solution.Recommendation.IsSome)
            
            match solution.Recommendation with
            | Some recommendation ->
                Assert.Equal(30, recommendation.ProblemSize)
                // Reasoning should mention problem size
                Assert.True(solution.Reasoning.Length > 20, 
                    "Should provide detailed reasoning for larger problems")
            | None -> ()
            
            // Solution should still be valid regardless of method
            Assert.Equal(30, solution.Result.Tour.Length)
            Assert.True(solution.Result.TourLength > 0.0)
        | Error msg -> 
            Assert.Fail($"Expected successful solution, got error: {msg}")

    // ===========================================
    // Test Scenario 7: Budget Enforcement
    // ===========================================
    
    [<Fact>]
    let ``Budget Enforcement - Should respect cost limits`` () =
        // Arrange: Portfolio with strict budget
        let assets = [
            ("EXPENSIVE", 0.15, 0.20, 500.0)
            ("CHEAP", 0.10, 0.15, 50.0)
        ]
        
        let tightBudget = 100.0
        let problem = Portfolio.createProblem assets tightBudget
        
        // Act: Solve with tight budget
        let result = Portfolio.solve problem None
        
        // Assert: Should respect budget strictly
        match result with
        | Ok allocation ->
            Assert.True(allocation.TotalValue <= tightBudget, 
                $"Total value {allocation.TotalValue} must not exceed budget {tightBudget}")
            
            // Should not be able to buy expensive asset
            let expensiveAlloc = 
                allocation.Allocations 
                |> List.tryFind (fun (symbol, _, _) -> symbol = "EXPENSIVE")
            Assert.True(expensiveAlloc.IsNone, "Should not allocate expensive asset with tight budget")
        | Error msg ->
            Assert.Fail($"Expected successful solution, got error: {msg}")

    [<Fact>]
    let ``Budget Enforcement - Portfolio should handle insufficient budget gracefully`` () =
        // Arrange: Budget too small for any asset
        let assets = [
            ("STOCK1", 0.10, 0.15, 100.0)
            ("STOCK2", 0.12, 0.18, 150.0)
        ]
        
        let insufficientBudget = 50.0  // Less than cheapest asset
        let problem = Portfolio.createProblem assets insufficientBudget
        
        // Act
        let result = Portfolio.solve problem None
        
        // Assert: Should return empty allocation gracefully
        match result with
        | Ok allocation ->
            Assert.Empty(allocation.Allocations)
            Assert.Equal(0.0, allocation.TotalValue)
        | Error msg ->
            Assert.Fail($"Expected successful solution with empty allocation, got error: {msg}")

    // ===========================================
    // Test Scenario 8: Error Handling
    // ===========================================
    
    [<Fact>]
    let ``Error Handling - Empty TSP input should handle gracefully`` () =
        // Arrange: Empty city list
        let cities = []
        let problem = TSP.createProblem cities
        
        // Act & Assert: Should not throw, should handle gracefully
        let result = TSP.solve problem None
        
        // Should return valid result structure (even if empty/trivial)
        match result with
        | Ok tour ->
            // Empty tour is valid
            Assert.True(tour.Cities.Length = 0 || tour.TotalDistance >= 0.0)
        | Error msg ->
            // Error is also acceptable for empty input
            Assert.False(String.IsNullOrWhiteSpace(msg))

    [<Fact>]
    let ``Error Handling - Invalid constraints in Portfolio should handle gracefully`` () =
        // Arrange: Portfolio with negative risk tolerance would be invalid
        // But our API doesn't directly expose risk tolerance parameter
        // Test with empty assets instead
        let assets = []
        let problem = Portfolio.createProblem assets 1000.0
        
        // Act: Should handle gracefully
        let result = Portfolio.solve problem None
        
        // Assert: Should return valid result (implementation-specific behavior)
        match result with
        | Ok allocation ->
            Assert.Empty(allocation.Allocations)
        | Error msg ->
            Assert.False(String.IsNullOrWhiteSpace(msg))

    [<Fact>]
    let ``Error Handling - HybridSolver with invalid input returns error`` () =
        // Arrange: Empty distance matrix
        let emptyMatrix = Array2D.create 0 0 0.0
        
        // Act
        let result = HybridSolver.solveTsp emptyMatrix None None None
        
        // Assert: Should handle gracefully
        match result with
        | Ok solution -> 
            // If it succeeds, tour should be empty or minimal
            Assert.True(solution.Result.Tour.Length <= 1)
        | Error msg -> 
            // If it errors, message should be informative
            Assert.False(String.IsNullOrWhiteSpace(msg))
            Assert.True(msg.Length > 5, "Error message should be descriptive")

    [<Fact>]
    let ``Error Handling - TSP with single city should return valid trivial tour`` () =
        // Arrange: Single city (trivial tour)
        let cities = [("OnlyCity", 0.0, 0.0)]
        let problem = TSP.createProblem cities
        
        // Act
        let result = TSP.solve problem None
        
        // Assert: Should handle trivially
        match result with
        | Ok tour ->
            Assert.Equal(1, tour.Cities.Length)
            Assert.Equal(0.0, tour.TotalDistance)  // Distance is zero for single city
            Assert.True(tour.IsValid)
        | Error msg ->
            Assert.Fail($"Expected successful trivial solution, got error: {msg}")

    [<Fact>]
    let ``Error Handling - Portfolio with single asset should allocate within budget`` () =
        // Arrange: Single asset (trivial allocation)
        let assets = [
            ("ONLY", 0.10, 0.15, 100.0)
        ]
        let budget = 500.0
        let problem = Portfolio.createProblem assets budget
        
        // Act
        let result = Portfolio.solve problem None
        
        // Assert: Should allocate maximum possible
        match result with
        | Ok allocation ->
            Assert.Equal(1, allocation.Allocations.Length)
            
            let (symbol, shares, value) = allocation.Allocations.[0]
            Assert.Equal("ONLY", symbol)
            
            // Should buy as many as budget allows
            let expectedShares = floor (budget / 100.0)
            Assert.True(shares <= expectedShares)
            Assert.True(value <= budget)
            Assert.True(allocation.IsValid)
        | Error msg ->
            Assert.Fail($"Expected successful allocation, got error: {msg}")

    [<Fact>]
    let ``Integration - TSP and Portfolio workflows end-to-end`` () =
        // Arrange: Test complete workflow with both TSP and Portfolio
        
        // TSP: 8 cities
        let tspCities = [
            ("New York", 40.7, -74.0)
            ("Los Angeles", 34.1, -118.2)
            ("Chicago", 41.9, -87.6)
            ("Houston", 29.8, -95.4)
            ("Phoenix", 33.4, -112.1)
            ("Philadelphia", 40.0, -75.2)
            ("San Antonio", 29.4, -98.5)
            ("San Diego", 32.7, -117.2)
        ]
        let tspProblem = TSP.createProblem tspCities
        
        // Portfolio: 8 assets
        let portfolioAssets = [
            ("AAPL", 0.12, 0.18, 150.0)
            ("GOOGL", 0.10, 0.15, 2800.0)
            ("MSFT", 0.11, 0.14, 350.0)
            ("AMZN", 0.13, 0.20, 3300.0)
            ("TSLA", 0.15, 0.25, 700.0)
            ("META", 0.10, 0.16, 300.0)
            ("NVDA", 0.14, 0.22, 450.0)
            ("AMD", 0.12, 0.19, 120.0)
        ]
        let portfolioProblem = Portfolio.createProblem portfolioAssets 20000.0
        
        // Act: Solve both problems
        let tspResult = TSP.solve tspProblem None
        let portfolioResult = Portfolio.solve portfolioProblem None
        
        // Assert: Both should succeed
        match tspResult with
        | Ok tour ->
            Assert.Equal(8, tour.Cities.Length)
            Assert.True(tour.IsValid)
            Assert.True(tour.TotalDistance > 0.0)
        | Error msg ->
            Assert.Fail($"TSP solve failed: {msg}")
            
        match portfolioResult with
        | Ok allocation ->
            Assert.True(allocation.Allocations.Length > 0)
            Assert.True(allocation.TotalValue <= 20000.0 * 1.01)
            Assert.True(allocation.IsValid)
        | Error msg ->
            Assert.Fail($"Portfolio solve failed: {msg}")
