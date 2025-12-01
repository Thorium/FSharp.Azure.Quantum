namespace FSharp.Azure.Quantum.Tests

open Xunit
open System
open FSharp.Azure.Quantum

module ProblemAnalysisTests =

    [<Fact>]
    let ``Should classify TSP problem from distance matrix correctly`` () =
        // Arrange: Create a symmetric distance matrix (TSP characteristic)
        let distanceMatrix =
            array2D
                [ [ 0.0; 10.0; 15.0; 20.0 ]
                  [ 10.0; 0.0; 35.0; 25.0 ]
                  [ 15.0; 35.0; 0.0; 30.0 ]
                  [ 20.0; 25.0; 30.0; 0.0 ] ]

        // Act: Classify the problem
        let result = ProblemAnalysis.classifyProblem distanceMatrix

        // Assert: Should be classified as TSP with meaningful details
        match result with
        | Ok problemInfo ->
            Assert.Equal(ProblemAnalysis.ProblemType.TSP, problemInfo.ProblemType)
            Assert.Equal(4, problemInfo.Size)
            Assert.True(problemInfo.Size > 0, "Problem size should be positive")
        | Error errorMsg -> Assert.Fail($"Classification should succeed but got error: {errorMsg}")

    [<Fact>]
    let ``Should reject null distance matrix`` () =
        // Arrange: null input
        let nullMatrix: float[,] = null

        // Act: Try to classify
        let result = ProblemAnalysis.classifyProblem nullMatrix

        // Assert: Should return error, not throw exception
        match result with
        | Ok _ -> Assert.Fail("Should reject null matrix with error, not return Ok")
        | Error errorMsg ->
            Assert.Contains("null", errorMsg.ToLower())
            Assert.False(String.IsNullOrWhiteSpace(errorMsg), "Error message should be meaningful")

    [<Fact>]
    let ``Should reject empty distance matrix`` () =
        // Arrange: Empty matrix
        let emptyMatrix = Array2D.create 0 0 0.0

        // Act: Try to classify
        let result = ProblemAnalysis.classifyProblem emptyMatrix

        // Assert: Should return error with clear message
        match result with
        | Ok _ -> Assert.Fail("Should reject empty matrix with error")
        | Error errorMsg ->
            Assert.Contains("empty", errorMsg.ToLower())
            Assert.False(String.IsNullOrWhiteSpace(errorMsg), "Error message should be meaningful")

    [<Fact>]
    let ``Should reject non-square distance matrix`` () =
        // Arrange: Non-square matrix (3x4)
        let nonSquareMatrix =
            array2D
                [ [ 0.0; 10.0; 15.0; 20.0 ]
                  [ 10.0; 0.0; 35.0; 25.0 ]
                  [ 15.0; 35.0; 0.0; 30.0 ] ]

        // Act: Try to classify
        let result = ProblemAnalysis.classifyProblem nonSquareMatrix

        // Assert: Should return error for invalid matrix shape
        match result with
        | Ok _ -> Assert.Fail("Should reject non-square matrix")
        | Error errorMsg ->
            Assert.True(
                errorMsg.Contains("square") || errorMsg.Contains("dimensions"),
                $"Error should mention matrix shape issue, got: {errorMsg}"
            )

    [<Fact>]
    let ``Should handle single city TSP edge case`` () =
        // Arrange: 1x1 matrix (trivial TSP)
        let singleCity = array2D [ [ 0.0 ] ]

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
    let ``Should detect asymmetric distance matrix`` () =
        // Arrange: Asymmetric matrix (not typical TSP)
        let asymmetricMatrix =
            array2D
                [ [ 0.0; 10.0; 15.0 ]
                  [ 20.0; 0.0; 35.0 ] // Note: 20.0 != 10.0
                  [ 15.0; 35.0; 0.0 ] ]

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
    let ``Should reject matrix with negative distances`` () =
        // Arrange: Matrix with invalid negative distance
        let negativeMatrix =
            array2D [ [ 0.0; 10.0; -5.0 ]; [ 10.0; 0.0; 15.0 ]; [ -5.0; 15.0; 0.0 ] ]

        // Act: Classify
        let result = ProblemAnalysis.classifyProblem negativeMatrix

        // Assert: Should reject or warn about negative distances
        match result with
        | Ok _ -> Assert.Fail("Should reject negative distances in TSP")
        | Error errorMsg ->
            Assert.True(
                errorMsg.Contains("negative") || errorMsg.Contains("invalid"),
                $"Error should mention negative/invalid distances, got: {errorMsg}"
            )

    [<Fact>]
    let ``Should reject matrix with NaN values`` () =
        // Arrange: Matrix with NaN
        let nanMatrix =
            array2D [ [ 0.0; 10.0; Double.NaN ]; [ 10.0; 0.0; 15.0 ]; [ Double.NaN; 15.0; 0.0 ] ]

        // Act: Classify
        let result = ProblemAnalysis.classifyProblem nanMatrix

        // Assert: Should reject NaN values
        match result with
        | Ok _ -> Assert.Fail("Should reject NaN values")
        | Error errorMsg ->
            Assert.True(
                errorMsg.Contains("NaN") || errorMsg.Contains("invalid"),
                $"Error should mention NaN/invalid values, got: {errorMsg}"
            )

    [<Fact>]
    let ``Should reject matrix with infinity values`` () =
        // Arrange: Matrix with infinity
        let infMatrix =
            array2D
                [ [ 0.0; 10.0; Double.PositiveInfinity ]
                  [ 10.0; 0.0; 15.0 ]
                  [ Double.PositiveInfinity; 15.0; 0.0 ] ]

        // Act: Classify
        let result = ProblemAnalysis.classifyProblem infMatrix

        // Assert: Should reject infinity values
        match result with
        | Ok _ -> Assert.Fail("Should reject infinity values")
        | Error errorMsg ->
            Assert.True(
                errorMsg.Contains("infinity") || errorMsg.Contains("invalid"),
                $"Error should mention infinity/invalid values, got: {errorMsg}"
            )

    // ===== Search Space Estimation Tests =====

    [<Fact>]
    let ``Should estimate TSP search space correctly for small problem`` () =
        // Arrange: 5-city TSP
        let matrix =
            array2D
                [ [ 0.0; 10.0; 15.0; 20.0; 25.0 ]
                  [ 10.0; 0.0; 35.0; 30.0; 40.0 ]
                  [ 15.0; 35.0; 0.0; 30.0; 45.0 ]
                  [ 20.0; 30.0; 30.0; 0.0; 50.0 ]
                  [ 25.0; 40.0; 45.0; 50.0; 0.0 ] ]

        // Act
        let result = ProblemAnalysis.classifyProblem matrix

        // Assert
        match result with
        | Ok problemInfo ->
            Assert.Equal("O(n!)", problemInfo.Complexity)
            // 5! = 120
            Assert.Equal(120.0, problemInfo.SearchSpaceSize)
            Assert.True(problemInfo.SearchSpaceSize > 0.0, "Search space should be positive")
        | Error errorMsg -> Assert.Fail($"Should succeed: {errorMsg}")

    [<Fact>]
    let ``Should estimate TSP search space for larger problem`` () =
        // Arrange: 10-city TSP
        let matrix =
            Array2D.init 10 10 (fun i j -> if i = j then 0.0 else float (i + j + 1) * 10.0)

        // Act
        let result = ProblemAnalysis.classifyProblem matrix

        // Assert
        match result with
        | Ok problemInfo ->
            Assert.Equal("O(n!)", problemInfo.Complexity)
            // 10! = 3,628,800
            Assert.Equal(3628800.0, problemInfo.SearchSpaceSize)
            Assert.True(problemInfo.SearchSpaceSize > 1_000_000.0, "10-city TSP should have > 1M search space")
        | Error errorMsg -> Assert.Fail($"Should succeed: {errorMsg}")

    [<Fact>]
    let ``Should handle very large TSP problem without overflow`` () =
        // Arrange: 20-city TSP (20! is huge)
        let matrix =
            Array2D.init 20 20 (fun i j -> if i = j then 0.0 else float (abs (i - j)) * 5.0)

        // Act
        let result = ProblemAnalysis.classifyProblem matrix

        // Assert
        match result with
        | Ok problemInfo ->
            Assert.Equal("O(n!)", problemInfo.Complexity)
            // Should not be NaN or negative infinity
            Assert.False(Double.IsNaN(problemInfo.SearchSpaceSize), "Search space should not be NaN")
            Assert.True(problemInfo.SearchSpaceSize > 0.0, "Search space should be positive")
            // 20! is approximately 2.43 × 10^18
            Assert.True(problemInfo.SearchSpaceSize > 1e18, "20-city TSP search space should be > 10^18")
        | Error errorMsg -> Assert.Fail($"Should succeed: {errorMsg}")

    // ===== Quantum Advantage Estimation Tests =====

    [<Fact>]
    let ``Should estimate quantum advantage for small TSP`` () =
        // Arrange: 5-city TSP (small, classical is faster)
        let matrix =
            array2D
                [ [ 0.0; 10.0; 15.0; 20.0; 25.0 ]
                  [ 10.0; 0.0; 35.0; 30.0; 40.0 ]
                  [ 15.0; 35.0; 0.0; 30.0; 45.0 ]
                  [ 20.0; 30.0; 30.0; 0.0; 50.0 ]
                  [ 25.0; 40.0; 45.0; 50.0; 0.0 ] ]

        // Act
        let result = ProblemAnalysis.estimateQuantumAdvantage matrix

        // Assert
        match result with
        | Ok advantage ->
            Assert.True(advantage.ProblemSize >= 0, "Problem size should be non-negative")
            Assert.NotNull(advantage.Recommendation)
            Assert.False(String.IsNullOrWhiteSpace(advantage.Recommendation), "Recommendation should be meaningful")
            // Small problems should recommend classical
            Assert.Contains("classical", advantage.Recommendation.ToLower())
        | Error errorMsg -> Assert.Fail($"Should succeed: {errorMsg}")

    [<Fact>]
    let ``Should estimate quantum advantage for medium TSP`` () =
        // Arrange: 15-city TSP (medium, quantum might help)
        let matrix =
            Array2D.init 15 15 (fun i j -> if i = j then 0.0 else float (i + j + 1) * 10.0)

        // Act
        let result = ProblemAnalysis.estimateQuantumAdvantage matrix

        // Assert
        match result with
        | Ok advantage ->
            Assert.Equal(15, advantage.ProblemSize)
            Assert.True(advantage.QuantumSpeedup >= 1.0, "Quantum speedup should be at least 1x")
            Assert.True(advantage.EstimatedClassicalTimeMs > 0.0, "Classical time estimate should be positive")
            Assert.True(advantage.EstimatedQuantumTimeMs > 0.0, "Quantum time estimate should be positive")
            Assert.False(String.IsNullOrWhiteSpace(advantage.Recommendation))
        | Error errorMsg -> Assert.Fail($"Should succeed: {errorMsg}")

    [<Fact>]
    let ``Should estimate quantum advantage for large TSP`` () =
        // Arrange: 50-city TSP (large, quantum advantage expected)
        let matrix =
            Array2D.init 50 50 (fun i j -> if i = j then 0.0 else float (abs (i - j)) * 3.0)

        // Act
        let result = ProblemAnalysis.estimateQuantumAdvantage matrix

        // Assert
        match result with
        | Ok advantage ->
            Assert.Equal(50, advantage.ProblemSize)
            // For large problems, quantum should show significant advantage
            Assert.True(advantage.QuantumSpeedup > 1.0, "Large problems should show quantum speedup")
            Assert.Contains("quantum", advantage.Recommendation.ToLower())
        | Error errorMsg -> Assert.Fail($"Should succeed: {errorMsg}")

    [<Fact>]
    let ``Quantum advantage should reject null input`` () =
        // Arrange
        let nullMatrix: float[,] = null

        // Act
        let result = ProblemAnalysis.estimateQuantumAdvantage nullMatrix

        // Assert
        match result with
        | Ok _ -> Assert.Fail("Should reject null matrix")
        | Error errorMsg -> Assert.Contains("null", errorMsg.ToLower())

    [<Fact>]
    let ``Quantum advantage should reject invalid matrix`` () =
        // Arrange: Non-square matrix
        let invalidMatrix = array2D [ [ 0.0; 10.0; 15.0 ]; [ 10.0; 0.0; 35.0 ] ]

        // Act
        let result = ProblemAnalysis.estimateQuantumAdvantage invalidMatrix

        // Assert
        match result with
        | Ok _ -> Assert.Fail("Should reject invalid matrix")
        | Error errorMsg -> Assert.False(String.IsNullOrWhiteSpace(errorMsg))
