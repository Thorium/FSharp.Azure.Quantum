module FSharp.Azure.Quantum.Tests.PortfolioSolverTests

open Xunit
open FSharp.Azure.Quantum.Classical.PortfolioSolver

[<Fact>]
let ``Portfolio solver should validate budget constraint`` () =
    // Arrange
    let assets = [
        { Symbol = "AAPL"; ExpectedReturn = 0.12; Risk = 0.20; Price = 150.0 }
        { Symbol = "GOOGL"; ExpectedReturn = 0.15; Risk = 0.25; Price = 2800.0 }
        { Symbol = "MSFT"; ExpectedReturn = 0.10; Risk = 0.18; Price = 350.0 }
    ]
    
    let constraints = {
        Budget = 10000.0
        MinHolding = 0.0
        MaxHolding = 5000.0
    }
    
    // Act
    let result = validateBudgetConstraint assets constraints
    
    // Assert
    Assert.True(result.IsValid)
    Assert.Empty(result.Messages)

[<Fact>]
let ``Greedy-by-ratio should allocate assets by return-risk ratio`` () =
    // Arrange: Assets with different return/risk ratios
    let assets = [
        { Symbol = "HIGH"; ExpectedReturn = 0.20; Risk = 0.10; Price = 100.0 }  // Ratio: 2.0
        { Symbol = "MED"; ExpectedReturn = 0.15; Risk = 0.15; Price = 100.0 }   // Ratio: 1.0
        { Symbol = "LOW"; ExpectedReturn = 0.10; Risk = 0.20; Price = 100.0 }   // Ratio: 0.5
    ]
    
    let constraints = {
        Budget = 250.0  // Can afford 2.5 shares total
        MinHolding = 0.0
        MaxHolding = 150.0  // Max 1.5 shares per asset
    }
    
    let config = defaultConfig
    
    // Act
    let solution = solveGreedyByRatio assets constraints config
    
    // Assert: Should allocate to HIGH first (best ratio), then MED
    Assert.NotEmpty(solution.Allocations)
    
    // HIGH should get max allocation (1.5 shares = 150.0)
    let highAlloc = solution.Allocations |> List.find (fun a -> a.Asset.Symbol = "HIGH")
    Assert.Equal(1.5, highAlloc.Shares, 2)
    Assert.Equal(150.0, highAlloc.Value, 2)
    
    // MED should get remaining budget (1.0 share = 100.0)
    let medAlloc = solution.Allocations |> List.find (fun a -> a.Asset.Symbol = "MED")
    Assert.Equal(1.0, medAlloc.Shares, 2)
    Assert.Equal(100.0, medAlloc.Value, 2)
    
    // LOW should not be allocated (no budget left)
    let lowAllocExists = solution.Allocations |> List.exists (fun a -> a.Asset.Symbol = "LOW")
    Assert.False(lowAllocExists)
    
    // Total value should equal budget used (250.0)
    Assert.Equal(250.0, solution.TotalValue, 2)

[<Fact>]
let ``Mean-variance optimization should balance return and risk`` () =
    // Arrange: Three assets with different risk-return profiles
    let assets = [
        { Symbol = "SAFE"; ExpectedReturn = 0.08; Risk = 0.05; Price = 100.0 }   // Low risk, low return
        { Symbol = "BALANCED"; ExpectedReturn = 0.12; Risk = 0.10; Price = 100.0 } // Medium risk, medium return
        { Symbol = "RISKY"; ExpectedReturn = 0.20; Risk = 0.25; Price = 100.0 }  // High risk, high return
    ]
    
    let constraints = {
        Budget = 300.0  // Can afford 3 shares total
        MinHolding = 0.0
        MaxHolding = 150.0  // Max 1.5 shares per asset
    }
    
    // Test with moderate risk tolerance (0.5)
    let config = { defaultConfig with RiskTolerance = 0.5 }
    
    // Act
    let solution = solveMeanVariance assets constraints config
    
    // Assert: Solution should exist and use budget
    Assert.NotEmpty(solution.Allocations)
    Assert.True(solution.TotalValue > 0.0)
    Assert.True(solution.TotalValue <= constraints.Budget)
    
    // Portfolio metrics should be calculated
    Assert.True(solution.ExpectedReturn > 0.0, "Expected return should be positive")
    Assert.True(solution.Risk >= 0.0, "Risk should be non-negative")
    
    // Mean-variance should diversify across multiple assets (not just pick highest return)
    // Should have at least 2 assets in solution (diversification)
    Assert.True(solution.Allocations.Length >= 2, "Mean-variance should diversify across multiple assets")
    
    // BALANCED asset should be included (good risk-return tradeoff)
    let hasBalanced = solution.Allocations |> List.exists (fun a -> a.Asset.Symbol = "BALANCED")
    Assert.True(hasBalanced, "Mean-variance should include balanced asset")

[<Fact>]
let ``Mean-variance optimization with high risk tolerance should favor high returns`` () =
    // Arrange: Same assets as previous test
    let assets = [
        { Symbol = "SAFE"; ExpectedReturn = 0.08; Risk = 0.05; Price = 100.0 }
        { Symbol = "BALANCED"; ExpectedReturn = 0.12; Risk = 0.10; Price = 100.0 }
        { Symbol = "RISKY"; ExpectedReturn = 0.20; Risk = 0.25; Price = 100.0 }
    ]
    
    let constraints = {
        Budget = 300.0
        MinHolding = 0.0
        MaxHolding = 200.0
    }
    
    // High risk tolerance (0.9) - should favor returns over risk reduction
    let config = { defaultConfig with RiskTolerance = 0.9 }
    
    // Act
    let solution = solveMeanVariance assets constraints config
    
    // Assert: Should allocate more to RISKY asset
    let riskyAlloc = solution.Allocations |> List.tryFind (fun a -> a.Asset.Symbol = "RISKY")
    Assert.True(riskyAlloc.IsSome, "High risk tolerance should include risky asset")
    
    // Expected return should be relatively high
    Assert.True(solution.ExpectedReturn > 0.12, "High risk tolerance should result in higher expected return")

[<Fact>]
let ``Greedy-by-ratio should handle 50 assets efficiently`` () =
    // Arrange: Generate 50 random assets
    let random = System.Random(42)  // Fixed seed for reproducibility
    let assets = 
        [1..50]
        |> List.map (fun i ->
            {
                Symbol = sprintf "ASSET%02d" i
                ExpectedReturn = 0.05 + (random.NextDouble() * 0.20)  // 5% to 25%
                Risk = 0.05 + (random.NextDouble() * 0.25)            // 5% to 30%
                Price = 10.0 + (random.NextDouble() * 190.0)          // $10 to $200
            })
    
    let constraints = {
        Budget = 100000.0   // $100k budget
        MinHolding = 0.0
        MaxHolding = 10000.0  // Max $10k per asset
    }
    
    let config = defaultConfig
    
    // Act
    let solution = solveGreedyByRatio assets constraints config
    
    // Assert: Performance constraint (< 1 second)
    Assert.True(solution.ElapsedMs < 1000.0, 
        sprintf "Greedy algorithm should complete in < 1 second for 50 assets, took %.2fms" solution.ElapsedMs)
    
    // Assert: Solution quality
    Assert.NotEmpty(solution.Allocations)
    Assert.True(solution.TotalValue > 0.0)
    Assert.True(solution.TotalValue <= constraints.Budget)
    Assert.True(solution.ExpectedReturn > 0.0, "Portfolio should have positive expected return")
    
    // Assert: Each allocation respects MaxHolding
    solution.Allocations
    |> List.iter (fun alloc ->
        Assert.True(alloc.Value <= constraints.MaxHolding, 
            sprintf "Asset %s exceeds MaxHolding: %.2f > %.2f" alloc.Asset.Symbol alloc.Value constraints.MaxHolding))

[<Fact>]
let ``Mean-variance should handle 50 assets efficiently`` () =
    // Arrange: Generate 50 random assets (same seed for consistency)
    let random = System.Random(42)
    let assets = 
        [1..50]
        |> List.map (fun i ->
            {
                Symbol = sprintf "ASSET%02d" i
                ExpectedReturn = 0.05 + (random.NextDouble() * 0.20)
                Risk = 0.05 + (random.NextDouble() * 0.25)
                Price = 10.0 + (random.NextDouble() * 190.0)
            })
    
    let constraints = {
        Budget = 100000.0
        MinHolding = 0.0
        MaxHolding = 10000.0
    }
    
    let config = { defaultConfig with RiskTolerance = 0.5 }
    
    // Act
    let solution = solveMeanVariance assets constraints config
    
    // Assert: Performance constraint (< 5 seconds per requirements)
    Assert.True(solution.ElapsedMs < 5000.0, 
        sprintf "Mean-variance should complete in < 5 seconds for 50 assets, took %.2fms" solution.ElapsedMs)
    
    // Assert: Solution quality
    Assert.NotEmpty(solution.Allocations)
    Assert.True(solution.TotalValue > 0.0)
    Assert.True(solution.TotalValue <= constraints.Budget)
    Assert.True(solution.ExpectedReturn > 0.0, "Portfolio should have positive expected return")
    Assert.True(solution.Risk >= 0.0, "Portfolio risk should be non-negative")
    
    // Assert: Diversification (mean-variance should spread across multiple assets)
    Assert.True(solution.Allocations.Length >= 3, 
        sprintf "Mean-variance should diversify across multiple assets, got %d" solution.Allocations.Length)

[<Fact>]
let ``Portfolio solver should handle edge case with single asset`` () =
    // Arrange: Portfolio with only one asset
    let assets = [
        { Symbol = "ONLY"; ExpectedReturn = 0.10; Risk = 0.15; Price = 100.0 }
    ]
    
    let constraints = {
        Budget = 500.0
        MinHolding = 0.0
        MaxHolding = 500.0
    }
    
    let config = defaultConfig
    
    // Act
    let greedySolution = solveGreedyByRatio assets constraints config
    let mvSolution = solveMeanVariance assets constraints config
    
    // Assert: Both algorithms should handle single asset
    let greedyAlloc = Assert.Single(greedySolution.Allocations)
    Assert.Equal("ONLY", greedyAlloc.Asset.Symbol)
    Assert.Equal(500.0, greedySolution.TotalValue, 2)
    
    let mvAlloc = Assert.Single(mvSolution.Allocations)
    Assert.Equal("ONLY", mvAlloc.Asset.Symbol)
    Assert.True(mvSolution.TotalValue > 0.0)

[<Fact>]
let ``Portfolio solver should handle assets with zero risk`` () =
    // Arrange: Include a risk-free asset (e.g., treasury bonds)
    let assets = [
        { Symbol = "RISKFREE"; ExpectedReturn = 0.03; Risk = 0.0; Price = 100.0 }  // Risk-free
        { Symbol = "MODERATE"; ExpectedReturn = 0.10; Risk = 0.12; Price = 100.0 }
        { Symbol = "RISKY"; ExpectedReturn = 0.18; Risk = 0.25; Price = 100.0 }
    ]
    
    let constraints = {
        Budget = 300.0
        MinHolding = 0.0
        MaxHolding = 150.0
    }
    
    let config = defaultConfig
    
    // Act
    let greedySolution = solveGreedyByRatio assets constraints config
    let mvSolution = solveMeanVariance assets constraints config
    
    // Assert: Should handle zero risk without errors
    Assert.NotEmpty(greedySolution.Allocations)
    Assert.NotEmpty(mvSolution.Allocations)
    
    // Risk-free asset should have infinite ratio, so greedy should prioritize it
    let riskFreeGreedy = greedySolution.Allocations |> List.tryFind (fun a -> a.Asset.Symbol = "RISKFREE")
    Assert.True(riskFreeGreedy.IsSome, "Greedy should allocate to risk-free asset (infinite ratio)")
