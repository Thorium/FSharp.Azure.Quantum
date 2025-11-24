namespace FSharp.Azure.Quantum.Tests

open Xunit
open FSharp.Azure.Quantum
open FSharp.Azure.Quantum.Classical

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

    [<Fact>]
    let ``Portfolio.solve should return valid allocation for 3 assets`` () =
        // Arrange
        let assets = [
            ("AAPL", 0.12, 0.15, 150.0)
            ("GOOGL", 0.10, 0.12, 2800.0)
            ("MSFT", 0.11, 0.14, 350.0)
        ]
        let problem = Portfolio.createProblem assets 10000.0
        
        // Act
        let result = Portfolio.solve problem None
        
        // Assert
        match result with
        | Ok allocation ->
            Assert.True(allocation.TotalValue > 0.0)
            Assert.True(allocation.TotalValue <= 10000.0)
            Assert.True(allocation.IsValid)
            Assert.True(allocation.Allocations.Length > 0)
        | Error msg ->
            Assert.Fail($"solve failed: {msg}")

    [<Fact>]
    let ``Portfolio.solve should respect budget constraint`` () =
        // Arrange
        let assets = [
            ("AAPL", 0.12, 0.15, 150.0)
            ("GOOGL", 0.10, 0.12, 2800.0)
            ("MSFT", 0.11, 0.14, 350.0)
        ]
        let budget = 5000.0
        let problem = Portfolio.createProblem assets budget
        
        // Act
        let result = Portfolio.solve problem None
        
        // Assert
        match result with
        | Ok allocation ->
            Assert.True(allocation.TotalValue <= budget)
        | Error msg ->
            Assert.Fail($"solve failed: {msg}")

    [<Fact>]
    let ``Portfolio.solveDirectly should solve without creating problem explicitly`` () =
        // Arrange
        let assets = [
            ("AAPL", 0.12, 0.15, 150.0)
            ("GOOGL", 0.10, 0.12, 2800.0)
        ]
        
        // Act
        let result = Portfolio.solveDirectly assets 10000.0 None
        
        // Assert
        match result with
        | Ok allocation ->
            Assert.True(allocation.IsValid)
            Assert.True(allocation.TotalValue > 0.0)
        | Error msg ->
            Assert.Fail($"solveDirectly failed: {msg}")
