namespace FSharp.Azure.Quantum.Tests

open System
open Xunit
open FSharp.Azure.Quantum

module QuantumAdvisorTests =

    // Helper function to create a simple NxN distance matrix
    let createDistanceMatrix (n: int) : float[,] =
        Array2D.init n n (fun i j -> if i = j then 0.0 else float (abs (i - j)))

    [<Fact>]
    let ``Small TSP problem (5 cities) should recommend classical solver`` () =
        // Arrange: Small 5x5 TSP distance matrix
        let distances = createDistanceMatrix 5

        // Act: Get recommendation
        let result = QuantumAdvisor.getRecommendation distances

        // Assert: Should recommend classical for small problem
        match result with
        | Ok recommendation ->
            Assert.Equal(
                QuantumAdvisor.RecommendationType.StronglyRecommendClassical,
                recommendation.RecommendationType
            )

            Assert.Contains("small", recommendation.Reasoning.ToLower())
            Assert.Equal(5, recommendation.ProblemSize)
            Assert.True(recommendation.Confidence > 0.9)
        | Error msg -> Assert.Fail($"Expected Ok but got Error: {msg}")

    [<Fact>]
    let ``Medium TSP problem (15 cities) should recommend classical solver`` () =
        // Arrange: Medium 15x15 TSP distance matrix
        let distances = createDistanceMatrix 15

        // Act: Get recommendation
        let result = QuantumAdvisor.getRecommendation distances

        // Assert: Should still recommend classical for medium problems
        match result with
        | Ok recommendation ->
            Assert.Equal(
                QuantumAdvisor.RecommendationType.StronglyRecommendClassical,
                recommendation.RecommendationType
            )

            Assert.Contains("medium", recommendation.Reasoning.ToLower())
            Assert.Equal(15, recommendation.ProblemSize)
        | Error msg -> Assert.Fail($"Expected Ok but got Error: {msg}")

    [<Fact>]
    let ``Large TSP problem (30 cities) should consider quantum solver`` () =
        // Arrange: Large 30x30 TSP distance matrix
        let distances = createDistanceMatrix 30

        // Act: Get recommendation
        let result = QuantumAdvisor.getRecommendation distances

        // Assert: Should consider quantum for large problems
        match result with
        | Ok recommendation ->
            Assert.Equal(QuantumAdvisor.RecommendationType.ConsiderQuantum, recommendation.RecommendationType)
            Assert.Contains("large", recommendation.Reasoning.ToLower())
            Assert.Contains("quantum", recommendation.Reasoning.ToLower())
            Assert.Equal(30, recommendation.ProblemSize)
        | Error msg -> Assert.Fail($"Expected Ok but got Error: {msg}")

    [<Fact>]
    let ``Very large TSP problem (100 cities) should strongly recommend quantum solver`` () =
        // Arrange: Very large 100x100 TSP distance matrix
        let distances = createDistanceMatrix 100

        // Act: Get recommendation
        let result = QuantumAdvisor.getRecommendation distances

        // Assert: Should strongly recommend quantum for very large problems
        match result with
        | Ok recommendation ->
            Assert.Equal(QuantumAdvisor.RecommendationType.StronglyRecommendQuantum, recommendation.RecommendationType)
            Assert.Contains("very large", recommendation.Reasoning.ToLower())
            Assert.Contains("quantum", recommendation.Reasoning.ToLower())
            Assert.Equal(100, recommendation.ProblemSize)
            Assert.True(recommendation.Confidence > 0.8)
        | Error msg -> Assert.Fail($"Expected Ok but got Error: {msg}")

    [<Fact>]
    let ``Invalid input (null matrix) should return error`` () =
        // Arrange: Null matrix
        let distances: float[,] = null

        // Act: Get recommendation
        let result = QuantumAdvisor.getRecommendation distances

        // Assert: Should return error
        match result with
        | Ok _ -> Assert.Fail("Expected Error but got Ok")
        | Error msg -> Assert.Contains("null", msg.Message.ToLower())

    [<Fact>]
    let ``Invalid input (empty matrix) should return error`` () =
        // Arrange: Empty matrix
        let distances = Array2D.create 0 0 0.0

        // Act: Get recommendation
        let result = QuantumAdvisor.getRecommendation distances

        // Assert: Should return error
        match result with
        | Ok _ -> Assert.Fail("Expected Error but got Ok")
        | Error msg -> Assert.Contains("empty", msg.Message.ToLower())

    [<Fact>]
    let ``Recommendation should include confidence level`` () =
        // Arrange: Small problem
        let distances = createDistanceMatrix 5

        // Act: Get recommendation
        let result = QuantumAdvisor.getRecommendation distances

        // Assert: Should have confidence level between 0.0 and 1.0
        match result with
        | Ok recommendation ->
            Assert.True(
                recommendation.Confidence >= 0.0 && recommendation.Confidence <= 1.0,
                $"Confidence {recommendation.Confidence} should be between 0.0 and 1.0"
            )
        | Error msg -> Assert.Fail($"Expected Ok but got Error: {msg}")

    [<Fact>]
    let ``Recommendation should include human-readable reasoning`` () =
        // Arrange: Medium problem
        let distances = createDistanceMatrix 15

        // Act: Get recommendation
        let result = QuantumAdvisor.getRecommendation distances

        // Assert: Reasoning should be non-empty and meaningful
        match result with
        | Ok recommendation ->
            Assert.False(String.IsNullOrWhiteSpace(recommendation.Reasoning))
            Assert.True(recommendation.Reasoning.Length > 20, "Reasoning should be descriptive")
        | Error msg -> Assert.Fail($"Expected Ok but got Error: {msg}")
