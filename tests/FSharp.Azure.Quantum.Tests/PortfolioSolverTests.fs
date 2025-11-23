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
