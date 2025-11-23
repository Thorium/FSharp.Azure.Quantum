namespace FSharp.Azure.Quantum.Tests

open System
open Xunit
open FSharp.Azure.Quantum.Classical

module QuantumAdvisorTests =
    
    [<Fact>]
    let ``Small TSP problem (5 cities) should recommend classical solver`` () =
        // Arrange: Small 5x5 TSP distance matrix
        let distances = array2D [
            [0.0; 2.0; 9.0; 10.0; 7.0]
            [2.0; 0.0; 6.0; 4.0; 3.0]
            [9.0; 6.0; 0.0; 8.0; 7.0]
            [10.0; 4.0; 8.0; 0.0; 5.0]
            [7.0; 3.0; 7.0; 5.0; 0.0]
        ]
        
        // Act: Get recommendation
        let result = QuantumAdvisor.getRecommendation distances
        
        // Assert: Should recommend classical for small problem
        match result with
        | Ok recommendation ->
            Assert.Equal(QuantumAdvisor.RecommendationType.StronglyRecommendClassical, recommendation.RecommendationType)
            Assert.Contains("small", recommendation.Reasoning.ToLower())
            Assert.Contains("classical", recommendation.Reasoning.ToLower())
        | Error msg ->
            Assert.Fail($"Expected Ok but got Error: {msg}")
