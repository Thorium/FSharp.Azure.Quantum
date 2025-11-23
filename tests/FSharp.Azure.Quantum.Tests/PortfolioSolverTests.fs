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

