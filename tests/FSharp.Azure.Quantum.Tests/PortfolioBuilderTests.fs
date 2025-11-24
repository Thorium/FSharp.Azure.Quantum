namespace FSharp.Azure.Quantum.Tests

open Xunit
open FSharp.Azure.Quantum

module PortfolioBuilderTests =

    [<Fact>]
    let ``Portfolio.createProblem should create problem from 3 assets`` () =
        // Arrange
        let assets = [
            ("AAPL", 0.12, 0.15, 150.0)  // symbol, expectedReturn, risk, price
            ("GOOGL", 0.10, 0.12, 2800.0)
            ("MSFT", 0.11, 0.14, 350.0)
        ]
        let budget = 10000.0
        
        // Act
        let problem = Portfolio.createProblem assets budget
        
        // Assert
        Assert.Equal(3, problem.AssetCount)
        Assert.Equal(10000.0, problem.Budget)
