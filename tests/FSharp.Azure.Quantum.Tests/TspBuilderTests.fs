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
