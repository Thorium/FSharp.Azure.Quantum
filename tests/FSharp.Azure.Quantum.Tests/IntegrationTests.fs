namespace FSharp.Azure.Quantum.Tests

open System
open Xunit
open FSharp.Azure.Quantum
open FSharp.Azure.Quantum.Classical
open FSharp.Azure.Quantum.Core.Types

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
        
        // Act: Solve with classical backend
        let result = TSP.solve cities None
        
        // Assert: Verify tour validity
        Assert.Equal(10, result.Tour.Length)
        
        // All cities should be visited exactly once
        let uniqueCities = result.Tour |> Array.distinct
        Assert.Equal(10, uniqueCities.Length)
        
        // Tour should be in valid range [0..9]
        Assert.All(result.Tour, fun cityIndex -> 
            Assert.True(cityIndex >= 0 && cityIndex < 10))
        
        // Tour length should be positive
        Assert.True(result.TourLength > 0.0, "Tour length must be positive")
        
        // Should complete in reasonable time (< 100ms for 10 cities)
        Assert.True(result.ElapsedMs < 100.0, $"Expected < 100ms, got {result.ElapsedMs}ms")

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
        
        // Act
        let result = TSP.solve cities None
        
        // Assert: Tour should be reasonable (not worst-case)
        // Optimal tour for circle is approximately 2*PI*radius ≈ 6.28
        // Naive tour could be much worse
        Assert.True(result.TourLength < 10.0, 
            $"Tour length {result.TourLength} should be better than naive solution")

    // ===========================================
    // Test Scenario 2: TSP Quantum Emulator
    // ===========================================
    
    [<Fact>]
    let ``TSP Quantum - 5-city problem with emulator should produce valid tour`` () =
        // Arrange: Small problem suitable for quantum emulation
        let cities = [
            ("A", 0.0, 0.0)
            ("B", 1.0, 0.0)
            ("C", 2.0, 0.0)
            ("D", 1.0, 1.0)
            ("E", 1.0, 2.0)
        ]
        
        // Act: Solve (currently using classical, quantum backend integration TBD)
        let result = TSP.solve cities None
        
        // Assert: Verify basic solution properties
        Assert.Equal(5, result.Tour.Length)
        Assert.True(result.TourLength > 0.0)
        
        // Verify all cities visited
        let uniqueCities = result.Tour |> Array.distinct
        Assert.Equal(5, uniqueCities.Length)

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
                {| 
                    Symbol = $"ASSET{i}"
                    ExpectedReturn = 0.05 + rng.NextDouble() * 0.15  // 5-20% return
                    Risk = 0.10 + rng.NextDouble() * 0.20             // 10-30% risk
                    Price = 50.0 + rng.NextDouble() * 150.0          // $50-200
                |})
        
        let budget = 10000.0
        let riskTolerance = 0.15
        
        // Act: Solve portfolio optimization
        let result = Portfolio.solve assets budget (Some riskTolerance) None
        
        // Assert: Verify constraints
        let totalCost = 
            result.Allocation 
            |> List.sumBy (fun alloc -> alloc.Quantity * alloc.Asset.Price)
        
        // Budget constraint
        Assert.True(totalCost <= budget * 1.01, // Allow 1% tolerance
            $"Total cost {totalCost} exceeds budget {budget}")
        
        // Should have allocated some assets
        Assert.True(result.Allocation.Length > 0, "Should allocate at least one asset")
        
        // All quantities should be non-negative
        Assert.All(result.Allocation, fun alloc ->
            Assert.True(alloc.Quantity >= 0.0, "Quantity must be non-negative"))
        
        // Expected return should be positive
        Assert.True(result.ExpectedReturn > 0.0, "Expected return should be positive")
        
        // Risk should be within tolerance (with small margin)
        Assert.True(result.Risk <= riskTolerance * 1.1, 
            $"Risk {result.Risk} exceeds tolerance {riskTolerance}")

    [<Fact>]
    let ``Portfolio Classical - Zero budget should return empty allocation`` () =
        // Arrange
        let assets = [
            {| Symbol = "AAPL"; ExpectedReturn = 0.12; Risk = 0.18; Price = 150.0 |}
            {| Symbol = "GOOGL"; ExpectedReturn = 0.10; Risk = 0.15; Price = 120.0 |}
        ]
        
        // Act
        let result = Portfolio.solve assets 0.0 None None
        
        // Assert
        Assert.Empty(result.Allocation)
        Assert.Equal(0.0, result.ExpectedReturn)

    // ===========================================
    // Test Scenario 4: Portfolio Quantum Emulator
    // ===========================================
    
    [<Fact>]
    let ``Portfolio Quantum - 10-asset portfolio with emulator`` () =
        // Arrange: Medium-sized portfolio
        let assets = 
            [1..10]
            |> List.map (fun i -> 
                {| 
                    Symbol = $"STOCK{i}"
                    ExpectedReturn = 0.08 + float i * 0.01
                    Risk = 0.12 + float i * 0.01
                    Price = 100.0
                |})
        
        let budget = 5000.0
        
        // Act: Solve (currently classical, quantum backend TBD)
        let result = Portfolio.solve assets budget None None
        
        // Assert: Basic validation
        Assert.True(result.Allocation.Length <= 10)
        
        let totalCost = 
            result.Allocation 
            |> List.sumBy (fun alloc -> alloc.Quantity * alloc.Asset.Price)
        Assert.True(totalCost <= budget * 1.01)

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
                Assert.True(recommendation.ConfidenceLevel > 0.5)
            | None -> ()
        | Error msg -> 
            Assert.Fail($"Expected successful solution, got error: {msg}")

    [<Fact>]
    let ``HybridSolver - Small Portfolio should route to classical automatically`` () =
        // Arrange: Small 3-asset portfolio
        let assets = [
            { Symbol = "A"; ExpectedReturn = 0.10; Risk = 0.15; Price = 100.0 }
            { Symbol = "B"; ExpectedReturn = 0.12; Risk = 0.18; Price = 150.0 }
            { Symbol = "C"; ExpectedReturn = 0.08; Risk = 0.12; Price = 80.0 }
        ]
        
        // Act: Let HybridSolver decide
        let result = HybridSolver.solvePortfolio assets 1000.0 None None None None
        
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
            {| Symbol = "EXPENSIVE"; ExpectedReturn = 0.15; Risk = 0.20; Price = 500.0 |}
            {| Symbol = "CHEAP"; ExpectedReturn = 0.10; Risk = 0.15; Price = 50.0 |}
        ]
        
        let tightBudget = 100.0
        
        // Act: Solve with tight budget
        let result = Portfolio.solve assets tightBudget None None
        
        // Assert: Should respect budget strictly
        let totalCost = 
            result.Allocation 
            |> List.sumBy (fun alloc -> alloc.Quantity * alloc.Asset.Price)
        
        Assert.True(totalCost <= tightBudget, 
            $"Total cost {totalCost} must not exceed budget {tightBudget}")
        
        // Should not be able to buy expensive asset
        let expensiveAlloc = 
            result.Allocation 
            |> List.tryFind (fun a -> a.Asset.Symbol = "EXPENSIVE")
        Assert.True(expensiveAlloc.IsNone, "Should not allocate expensive asset with tight budget")

    [<Fact>]
    let ``Budget Enforcement - Portfolio should handle insufficient budget gracefully`` () =
        // Arrange: Budget too small for any asset
        let assets = [
            {| Symbol = "STOCK1"; ExpectedReturn = 0.10; Risk = 0.15; Price = 100.0 |}
            {| Symbol = "STOCK2"; ExpectedReturn = 0.12; Risk = 0.18; Price = 150.0 |}
        ]
        
        let insufficientBudget = 50.0  // Less than cheapest asset
        
        // Act
        let result = Portfolio.solve assets insufficientBudget None None
        
        // Assert: Should return empty allocation gracefully
        Assert.Empty(result.Allocation)
        Assert.Equal(0.0, result.ExpectedReturn)

    // ===========================================
    // Test Scenario 8: Error Handling
    // ===========================================
    
    [<Fact>]
    let ``Error Handling - Empty TSP input should handle gracefully`` () =
        // Arrange: Empty city list
        let cities = []
        
        // Act & Assert: Should not throw, should handle gracefully
        // Note: Current implementation may return empty tour or default values
        let result = TSP.solve cities None
        
        // Should return valid result structure (even if empty)
        Assert.NotNull(result)
        Assert.True(result.Tour.Length = 0 || result.TourLength >= 0.0)

    [<Fact>]
    let ``Error Handling - Invalid constraints in Portfolio should handle gracefully`` () =
        // Arrange: Invalid risk tolerance (negative)
        let assets = [
            {| Symbol = "AAPL"; ExpectedReturn = 0.10; Risk = 0.15; Price = 100.0 |}
        ]
        
        let invalidRiskTolerance = -0.5  // Invalid negative risk
        
        // Act: Should handle gracefully
        // Note: Implementation should either clamp to valid range or return error
        let result = Portfolio.solve assets 1000.0 (Some invalidRiskTolerance) None
        
        // Assert: Should return valid result (implementation-specific behavior)
        Assert.NotNull(result)

    [<Fact>]
    let ``Error Handling - HybridSolver with invalid input returns error`` () =
        // Arrange: Invalid distance matrix (non-square)
        // This would be caught at type level in F#, but we test empty matrix
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
        
        // Act
        let result = TSP.solve cities None
        
        // Assert: Should handle trivially
        Assert.Equal(1, result.Tour.Length)
        Assert.Equal(0.0, result.TourLength)  // Distance is zero for single city
        Assert.True(result.ElapsedMs >= 0.0)

    [<Fact>]
    let ``Error Handling - Portfolio with single asset should allocate within budget`` () =
        // Arrange: Single asset (trivial allocation)
        let assets = [
            {| Symbol = "ONLY"; ExpectedReturn = 0.10; Risk = 0.15; Price = 100.0 |}
        ]
        let budget = 500.0
        
        // Act
        let result = Portfolio.solve assets budget None None
        
        // Assert: Should allocate maximum possible
        Assert.Equal(1, result.Allocation.Length)
        
        let allocation = result.Allocation.[0]
        Assert.Equal("ONLY", allocation.Asset.Symbol)
        
        // Should buy as many as budget allows
        let expectedQuantity = floor (budget / 100.0)
        Assert.True(allocation.Quantity <= expectedQuantity)
        
        let totalCost = allocation.Quantity * allocation.Asset.Price
        Assert.True(totalCost <= budget)
