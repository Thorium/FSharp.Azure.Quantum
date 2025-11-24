namespace FSharp.Azure.Quantum.Tests

open Xunit
open FSharp.Azure.Quantum

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
        let result = TSP.solve problem
        
        // Assert
        match result with
        | Ok tour ->
            Assert.Equal(3, tour.Cities.Length)
            Assert.True(tour.TotalDistance > 0.0)
            Assert.True(tour.IsValid)
        | Error msg ->
            Assert.Fail($"solve failed: {msg}")
