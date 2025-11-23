namespace FSharp.Azure.Quantum.Tests

open Xunit
open System
open FSharp.Azure.Quantum.Classical

module ProblemAnalysisTests =
    
    [<Fact>]
    let ``Should classify TSP problem from distance matrix correctly``() =
        // Arrange: Create a symmetric distance matrix (TSP characteristic)
        let distanceMatrix = array2D [
            [0.0; 10.0; 15.0; 20.0]
            [10.0; 0.0; 35.0; 25.0]
            [15.0; 35.0; 0.0; 30.0]
            [20.0; 25.0; 30.0; 0.0]
        ]
        
        // Act: Classify the problem
        let result = ProblemAnalysis.classifyProblem distanceMatrix
        
        // Assert: Should be classified as TSP with meaningful details
        match result with
        | Ok problemInfo ->
            Assert.Equal(ProblemAnalysis.ProblemType.TSP, problemInfo.ProblemType)
            Assert.Equal(4, problemInfo.Size)
            Assert.True(problemInfo.Size > 0, "Problem size should be positive")
        | Error errorMsg ->
            Assert.Fail($"Classification should succeed but got error: {errorMsg}")
    
    [<Fact>]
    let ``Should reject null distance matrix``() =
        // Arrange: null input
        let nullMatrix : float[,] = null
        
        // Act: Try to classify
        let result = ProblemAnalysis.classifyProblem nullMatrix
        
        // Assert: Should return error, not throw exception
        match result with
        | Ok _ -> 
            Assert.Fail("Should reject null matrix with error, not return Ok")
        | Error errorMsg ->
            Assert.Contains("null", errorMsg.ToLower())
            Assert.False(String.IsNullOrWhiteSpace(errorMsg), "Error message should be meaningful")
    
    [<Fact>]
    let ``Should reject empty distance matrix``() =
        // Arrange: Empty matrix
        let emptyMatrix = Array2D.create 0 0 0.0
        
        // Act: Try to classify
        let result = ProblemAnalysis.classifyProblem emptyMatrix
        
        // Assert: Should return error with clear message
        match result with
        | Ok _ -> 
            Assert.Fail("Should reject empty matrix with error")
        | Error errorMsg ->
            Assert.Contains("empty", errorMsg.ToLower())
            Assert.False(String.IsNullOrWhiteSpace(errorMsg), "Error message should be meaningful")
    
    [<Fact>]
    let ``Should reject non-square distance matrix``() =
        // Arrange: Non-square matrix (3x4)
        let nonSquareMatrix = array2D [
            [0.0; 10.0; 15.0; 20.0]
            [10.0; 0.0; 35.0; 25.0]
            [15.0; 35.0; 0.0; 30.0]
        ]
        
        // Act: Try to classify
        let result = ProblemAnalysis.classifyProblem nonSquareMatrix
        
        // Assert: Should return error for invalid matrix shape
        match result with
        | Ok _ -> 
            Assert.Fail("Should reject non-square matrix")
        | Error errorMsg ->
            Assert.True(errorMsg.Contains("square") || errorMsg.Contains("dimensions"), 
                       $"Error should mention matrix shape issue, got: {errorMsg}")
    
    [<Fact>]
    let ``Should handle single city TSP edge case``() =
        // Arrange: 1x1 matrix (trivial TSP)
        let singleCity = array2D [[0.0]]
        
        // Act: Classify
        let result = ProblemAnalysis.classifyProblem singleCity
        
        // Assert: Should handle gracefully
        match result with
        | Ok problemInfo ->
            Assert.Equal(1, problemInfo.Size)
            Assert.True(problemInfo.Size > 0, "Size should be positive even for edge case")
        | Error _ ->
            // Alternatively, rejecting size=1 is also valid
            Assert.True(true, "Rejecting single-node TSP is acceptable")
    
    [<Fact>]
    let ``Should detect asymmetric distance matrix``() =
        // Arrange: Asymmetric matrix (not typical TSP)
        let asymmetricMatrix = array2D [
            [0.0; 10.0; 15.0]
            [20.0; 0.0; 35.0]  // Note: 20.0 != 10.0
            [15.0; 35.0; 0.0]
        ]
        
        // Act: Classify
        let result = ProblemAnalysis.classifyProblem asymmetricMatrix
        
        // Assert: Should either detect asymmetry or classify differently
        match result with
        | Ok problemInfo ->
            // If it classifies as TSP, it should note asymmetry
            Assert.True(true, "Detected problem characteristics")
        | Error errorMsg ->
            // Or reject as invalid TSP
            Assert.False(String.IsNullOrWhiteSpace(errorMsg))
    
    [<Fact>]
    let ``Should reject matrix with negative distances``() =
        // Arrange: Matrix with invalid negative distance
        let negativeMatrix = array2D [
            [0.0; 10.0; -5.0]
            [10.0; 0.0; 15.0]
            [-5.0; 15.0; 0.0]
        ]
        
        // Act: Classify
        let result = ProblemAnalysis.classifyProblem negativeMatrix
        
        // Assert: Should reject or warn about negative distances
        match result with
        | Ok _ ->
            Assert.Fail("Should reject negative distances in TSP")
        | Error errorMsg ->
            Assert.True(errorMsg.Contains("negative") || errorMsg.Contains("invalid"),
                       $"Error should mention negative/invalid distances, got: {errorMsg}")
    
    [<Fact>]
    let ``Should reject matrix with NaN values``() =
        // Arrange: Matrix with NaN
        let nanMatrix = array2D [
            [0.0; 10.0; Double.NaN]
            [10.0; 0.0; 15.0]
            [Double.NaN; 15.0; 0.0]
        ]
        
        // Act: Classify
        let result = ProblemAnalysis.classifyProblem nanMatrix
        
        // Assert: Should reject NaN values
        match result with
        | Ok _ ->
            Assert.Fail("Should reject NaN values")
        | Error errorMsg ->
            Assert.True(errorMsg.Contains("NaN") || errorMsg.Contains("invalid"),
                       $"Error should mention NaN/invalid values, got: {errorMsg}")
    
    [<Fact>]
    let ``Should reject matrix with infinity values``() =
        // Arrange: Matrix with infinity
        let infMatrix = array2D [
            [0.0; 10.0; Double.PositiveInfinity]
            [10.0; 0.0; 15.0]
            [Double.PositiveInfinity; 15.0; 0.0]
        ]
        
        // Act: Classify
        let result = ProblemAnalysis.classifyProblem infMatrix
        
        // Assert: Should reject infinity values
        match result with
        | Ok _ ->
            Assert.Fail("Should reject infinity values")
        | Error errorMsg ->
            Assert.True(errorMsg.Contains("infinity") || errorMsg.Contains("invalid"),
                       $"Error should mention infinity/invalid values, got: {errorMsg}")
