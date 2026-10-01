namespace FSharp.Azure.Quantum.Tests

open Xunit
open FSharp.Azure.Quantum
open FSharp.Azure.Quantum.Classical
open FSharp.Azure.Quantum.Core
open FSharp.Azure.Quantum.Core.BackendAbstraction
open FSharp.Azure.Quantum.Backends
open System.Threading
open System.Threading.Tasks

module TspBuilderTests =

    [<Fact>]
    let ``TSP.createProblem should create problem from 3 cities with coordinates`` () =
        // Arrange
        let cities = [ ("A", 0.0, 0.0); ("B", 3.0, 0.0); ("C", 0.0, 4.0) ]

        // Act
        let problem = TSP.createProblem cities

        // Assert
        Assert.Equal(3, problem.CityCount)
        Assert.Equal(3, problem.Cities.Length)

    [<Fact>]
    let ``TSP.createProblem should calculate correct distance matrix`` () =
        // Arrange - Right triangle with sides 3, 4, 5
        let cities = [ ("A", 0.0, 0.0); ("B", 3.0, 0.0); ("C", 0.0, 4.0) ]

        // Act
        let problem = TSP.createProblem cities

        // Assert
        Assert.Equal(0.0, problem.DistanceMatrix.[0, 0], 5) // A to A
        Assert.Equal(3.0, problem.DistanceMatrix.[0, 1], 5) // A to B
        Assert.Equal(4.0, problem.DistanceMatrix.[0, 2], 5) // A to C
        Assert.Equal(3.0, problem.DistanceMatrix.[1, 0], 5) // B to A
        Assert.Equal(5.0, problem.DistanceMatrix.[1, 2], 5) // B to C (hypotenuse)
        Assert.Equal(5.0, problem.DistanceMatrix.[2, 1], 5) // C to B

    /// TSP.solveAsync returns the shortest tour among the measurements that are valid tours,
    /// or the solver's error when no measurement is one; which of the two happens depends on
    /// the sampling. Three cities with symmetric distances have a single cycle, so a returned
    /// tour is the cities in input order with the triangle's perimeter as its length.
    let private assertTriangleOrNoValidTour (problem: TSP.TspProblem) (result: QuantumResult<TSP.Tour>) =
        match result with
        | Ok tour ->
            let names =
                problem.Cities |> Array.map (fun city -> Option.get city.Name) |> Array.toList

            let d = problem.DistanceMatrix
            Assert.Equal<string list>(names, tour.Cities)
            Assert.Equal(d.[0, 1] + d.[1, 2] + d.[2, 0], tour.TotalDistance, 9)
            Assert.True(tour.IsValid)
        | Error err -> Assert.Contains("No valid tour in 1000 shots", err.Message)

    [<Fact>]
    let ``TSP.solve returns the triangle or reports that no valid tour was measured`` () : Task =
        task {
            let problem =
                TSP.createProblem [ ("A", 0.0, 0.0); ("B", 1.0, 0.0); ("C", 0.0, 1.0) ]

            let! result = TSP.solveAsync problem None CancellationToken.None
            assertTriangleOrNoValidTour problem result
        }
        :> Task

    [<Fact>]
    let ``TSP.solveAsync propagates cancellation instead of returning Error`` () : Task =
        task {
            let problem =
                TSP.createProblem [ ("A", 0.0, 0.0); ("B", 1.0, 0.0); ("C", 0.0, 1.0) ]

            let! _ =
                Assert.ThrowsAnyAsync<System.OperationCanceledException>(fun () ->
                    TSP.solveAsync problem None (CancellationToken true) :> Task)

            ()
        }
        :> Task

    [<Fact>]
    let ``TSP.solve should handle 3 cities triangle`` () : Task =
        task {
            // Arrange - Triangle shape (within LocalBackend 16-qubit limit)
            let problem =
                TSP.createProblem [ ("A", 0.0, 1.0); ("B", 0.87, -0.5); ("C", -0.87, -0.5) ]

            let! result = TSP.solveAsync problem None CancellationToken.None
            assertTriangleOrNoValidTour problem result
        }
        :> Task

    [<Fact>]
    let ``TSP.solveDirectly should solve without creating problem explicitly`` () : Task =
        task {
            let cities = [ ("A", 0.0, 0.0); ("B", 1.0, 0.0); ("C", 0.5, 1.0) ]

            let! result = TSP.solveDirectlyAsync cities None CancellationToken.None
            assertTriangleOrNoValidTour (TSP.createProblem cities) result
        }
        :> Task

    [<Fact>]
    let ``TSP.solve should accept custom backend`` () : Task =
        task {
            // Arrange - 3 cities (within LocalBackend 16-qubit limit)
            let problem =
                TSP.createProblem [ ("A", 0.0, 0.0); ("B", 1.0, 0.0); ("C", 0.0, 1.0) ]
            // Use LocalBackend explicitly (though None would also work)
            let backend =
                Some(LocalBackend.LocalBackend() :> BackendAbstraction.IQuantumBackend)

            let! result = TSP.solveAsync problem backend CancellationToken.None
            assertTriangleOrNoValidTour problem result
        }
        :> Task
