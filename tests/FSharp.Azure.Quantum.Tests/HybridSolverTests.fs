namespace FSharp.Azure.Quantum.Tests

open System
open Xunit
open FSharp.Azure.Quantum.Classical

module HybridSolverTests =

    // Helper function to create a simple TSP problem (distance matrix)
    let createTspDistanceMatrix (n: int) : float[,] =
        Array2D.init n n (fun i j -> if i = j then 0.0 else float (abs (i - j)))

    [<Fact>]
    let ``Small TSP problem should route to classical solver`` () =
        // Arrange: Small 5-city TSP problem
        let distances = createTspDistanceMatrix 5
        
        // Act: Solve using HybridSolver (should automatically choose classical)
        let result = HybridSolver.solve distances
        
        // Assert: Should use classical method
        match result with
        | Ok solution ->
            Assert.Equal(HybridSolver.SolverMethod.Classical, solution.Method)
            Assert.NotNull(solution.Result)
            Assert.NotEmpty(solution.Reasoning)
            Assert.Contains("classical", solution.Reasoning.ToLower())
            Assert.True(solution.ElapsedMs > 0.0)
        | Error msg -> Assert.Fail($"Expected Ok but got Error: {msg}")
