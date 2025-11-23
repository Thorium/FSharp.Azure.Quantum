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

