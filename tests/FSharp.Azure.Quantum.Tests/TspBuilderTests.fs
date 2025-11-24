namespace FSharp.Azure.Quantum.Tests

open Xunit
open FSharp.Azure.Quantum
open FSharp.Azure.Quantum.Classical

module TspBuilderTests =

    [<Fact>]
    let ``TSP.createProblem should create problem from 3 cities with coordinates`` () =
        // Arrange
        let cities = [
            ("A", 0.0, 0.0)
            ("B", 3.0, 0.0)
            ("C", 0.0, 4.0)
        ]
        
        // Act
        let problem = TSP.createProblem cities
        
        // Assert
        Assert.Equal(3, problem.CityCount)
        Assert.Equal(3, problem.Cities.Length)

    [<Fact>]
    let ``TSP.createProblem should calculate correct distance matrix`` () =
        // Arrange - Right triangle with sides 3, 4, 5
        let cities = [
            ("A", 0.0, 0.0)
            ("B", 3.0, 0.0)
            ("C", 0.0, 4.0)
        ]
        
        // Act
        let problem = TSP.createProblem cities
        
        // Assert
        Assert.Equal(0.0, problem.DistanceMatrix.[0, 0], 5) // A to A
        Assert.Equal(3.0, problem.DistanceMatrix.[0, 1], 5) // A to B
        Assert.Equal(4.0, problem.DistanceMatrix.[0, 2], 5) // A to C
        Assert.Equal(3.0, problem.DistanceMatrix.[1, 0], 5) // B to A
        Assert.Equal(5.0, problem.DistanceMatrix.[1, 2], 5) // B to C (hypotenuse)
        Assert.Equal(5.0, problem.DistanceMatrix.[2, 1], 5) // C to B

    [<Fact>]
    let ``TSP.solve should return valid tour for 3 cities`` () =
        // Arrange
        let cities = [
            ("A", 0.0, 0.0)
            ("B", 1.0, 0.0)
            ("C", 0.0, 1.0)
        ]
        let problem = TSP.createProblem cities
        
        // Act
        let result = TSP.solve problem None
        
        // Assert
        match result with
        | Ok tour ->
            Assert.Equal(3, tour.Cities.Length)
            Assert.True(tour.TotalDistance > 0.0)
            Assert.True(tour.IsValid)
        | Error msg ->
            Assert.Fail($"solve failed: {msg}")

    [<Fact>]
    let ``TSP.solve should handle 5 cities`` () =
        // Arrange - Pentagon shape
        let cities = [
            ("A", 0.0, 1.0)
            ("B", 0.95, 0.31)
            ("C", 0.59, -0.81)
            ("D", -0.59, -0.81)
            ("E", -0.95, 0.31)
        ]
        let problem = TSP.createProblem cities
        
        // Act
        let result = TSP.solve problem None
        
        // Assert
        match result with
        | Ok tour ->
            Assert.Equal(5, tour.Cities.Length)
            Assert.True(tour.TotalDistance > 0.0)
            Assert.True(tour.IsValid)
            // All cities should be in the tour
            Assert.Contains("A", tour.Cities)
            Assert.Contains("B", tour.Cities)
            Assert.Contains("C", tour.Cities)
            Assert.Contains("D", tour.Cities)
            Assert.Contains("E", tour.Cities)
        | Error msg ->
            Assert.Fail($"solve failed: {msg}")

    [<Fact>]
    let ``TSP.solve should return tour with all unique cities`` () =
        // Arrange
        let cities = [
            ("City1", 0.0, 0.0)
            ("City2", 1.0, 0.0)
            ("City3", 1.0, 1.0)
            ("City4", 0.0, 1.0)
        ]
        let problem = TSP.createProblem cities
        
        // Act
        let result = TSP.solve problem None
        
        // Assert
        match result with
        | Ok tour ->
            let uniqueCities = tour.Cities |> Set.ofList
            Assert.Equal(4, uniqueCities.Count) // All cities unique
        | Error msg ->
            Assert.Fail($"solve failed: {msg}")

    [<Fact>]
    let ``TSP.solveDirectly should solve without creating problem explicitly`` () =
        // Arrange
        let cities = [
            ("A", 0.0, 0.0)
            ("B", 1.0, 0.0)
            ("C", 0.5, 1.0)
        ]
        
        // Act
        let result = TSP.solveDirectly cities None
        
        // Assert
        match result with
        | Ok tour ->
            Assert.Equal(3, tour.Cities.Length)
            Assert.True(tour.IsValid)
            Assert.True(tour.TotalDistance > 0.0)
        | Error msg ->
            Assert.Fail($"solveDirectly failed: {msg}")

    [<Fact>]
    let ``TSP.solve should accept custom configuration`` () =
        // Arrange
        let cities = [
            ("A", 0.0, 0.0)
            ("B", 1.0, 0.0)
            ("C", 1.0, 1.0)
            ("D", 0.0, 1.0)
        ]
        let problem = TSP.createProblem cities
        let customConfig = Some { TspSolver.defaultConfig with MaxIterations = 100 }
        
        // Act
        let result = TSP.solve problem customConfig
        
        // Assert
        match result with
        | Ok tour ->
            Assert.Equal(4, tour.Cities.Length)
            Assert.True(tour.IsValid)
        | Error msg ->
            Assert.Fail($"solve with custom config failed: {msg}")
