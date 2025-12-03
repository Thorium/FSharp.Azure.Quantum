module FSharp.Azure.Quantum.Tests.QuantumKernelTests

open Xunit
open FSharp.Azure.Quantum.Core.BackendAbstraction
open FSharp.Azure.Quantum
open FSharp.Azure.Quantum.MachineLearning
open FSharp.Azure.Quantum.MachineLearning.QuantumKernels

// ============================================================================
// Test Setup
// ============================================================================

let private backend = LocalBackend() :> IQuantumBackend

let private epsilon = 1e-6

// ============================================================================
// Kernel Computation Tests
// ============================================================================

[<Fact>]
let ``computeKernel - should return value between 0 and 1`` () =
    let featureMap = AngleEncoding
    let x = [| 0.5; 0.3 |]
    let y = [| 0.7; 0.4 |]
    let shots = 1000
    
    let result = computeKernel backend featureMap x y shots
    
    match result with
    | Ok kernelValue ->
        Assert.True(kernelValue >= 0.0 && kernelValue <= 1.0, 
            sprintf "Kernel value should be in [0,1], got %f" kernelValue)
    | Error err ->
        Assert.True(false, sprintf "Should not fail: %s" err)

[<Fact>]
let ``computeKernel - identical vectors should give high kernel value`` () =
    let featureMap = AngleEncoding
    let x = [| 0.5; 0.3 |]
    let shots = 1000
    
    // K(x, x) should be close to 1.0 (identical states)
    let result = computeKernel backend featureMap x x shots
    
    match result with
    | Ok kernelValue ->
        // Due to quantum noise, might not be exactly 1.0 but should be high
        Assert.True(kernelValue > 0.8, 
            sprintf "K(x,x) should be high (>0.8), got %f" kernelValue)
    | Error err ->
        Assert.True(false, sprintf "Should not fail: %s" err)

[<Fact>]
let ``computeKernel - should reject empty feature vectors`` () =
    let featureMap = AngleEncoding
    let x = [||]
    let y = [||]
    let shots = 1000
    
    let result = computeKernel backend featureMap x y shots
    
    match result with
    | Error msg ->
        Assert.Contains("cannot be empty", msg)
    | Ok _ ->
        Assert.True(false, "Should have rejected empty vectors")

[<Fact>]
let ``computeKernel - should reject mismatched vector lengths`` () =
    let featureMap = AngleEncoding
    let x = [| 0.5; 0.3 |]
    let y = [| 0.7 |]  // Different length
    let shots = 1000
    
    let result = computeKernel backend featureMap x y shots
    
    match result with
    | Error msg ->
        Assert.Contains("same length", msg)
    | Ok _ ->
        Assert.True(false, "Should have rejected mismatched lengths")

[<Fact>]
let ``computeKernel - should reject non-positive shots`` () =
    let featureMap = AngleEncoding
    let x = [| 0.5; 0.3 |]
    let y = [| 0.7; 0.4 |]
    let shots = 0
    
    let result = computeKernel backend featureMap x y shots
    
    match result with
    | Error msg ->
        Assert.Contains("must be positive", msg)
    | Ok _ ->
        Assert.True(false, "Should have rejected zero shots")

[<Fact>]
let ``computeKernel - orthogonal states should give low kernel value`` () =
    let featureMap = AngleEncoding
    // These should produce nearly orthogonal quantum states
    let x = [| 0.0; 0.0 |]  // |00⟩
    let y = [| 1.0; 1.0 |]  // After rotation, should be far from |00⟩
    let shots = 1000
    
    let result = computeKernel backend featureMap x y shots
    
    match result with
    | Ok kernelValue ->
        // Orthogonal states should have low overlap
        Assert.True(kernelValue < 0.8, 
            sprintf "K(x,y) for distant states should be lower, got %f" kernelValue)
    | Error err ->
        Assert.True(false, sprintf "Should not fail: %s" err)

// ============================================================================
// Kernel Matrix Tests
// ============================================================================

[<Fact>]
let ``computeKernelMatrix - should be square and symmetric`` () =
    let featureMap = AngleEncoding
    let data = [|
        [| 0.1; 0.2 |]
        [| 0.3; 0.4 |]
        [| 0.5; 0.6 |]
    |]
    let shots = 500
    
    let result = computeKernelMatrix backend featureMap data shots
    
    match result with
    | Ok matrix ->
        // Should be 3x3
        Assert.Equal(3, Array2D.length1 matrix)
        Assert.Equal(3, Array2D.length2 matrix)
        
        // Should be symmetric: K[i,j] ≈ K[j,i]
        for i in 0 .. 2 do
            for j in i + 1 .. 2 do
                Assert.True(abs (matrix.[i, j] - matrix.[j, i]) < 0.1,
                    sprintf "Matrix should be symmetric at (%d,%d): %f vs %f" 
                        i j matrix.[i,j] matrix.[j,i])
    | Error err ->
        Assert.True(false, sprintf "Should not fail: %s" err)

[<Fact>]
let ``computeKernelMatrix - diagonal should be close to 1`` () =
    let featureMap = AngleEncoding
    let data = [|
        [| 0.1; 0.2 |]
        [| 0.3; 0.4 |]
    |]
    let shots = 1000
    
    let result = computeKernelMatrix backend featureMap data shots
    
    match result with
    | Ok matrix ->
        // Diagonal elements K(x,x) should be close to 1
        for i in 0 .. 1 do
            Assert.True(matrix.[i, i] > 0.8,
                sprintf "K(%d,%d) should be high, got %f" i i matrix.[i,i])
    | Error err ->
        Assert.True(false, sprintf "Should not fail: %s" err)

[<Fact>]
let ``computeKernelMatrix - should reject empty dataset`` () =
    let featureMap = AngleEncoding
    let data = [||]
    let shots = 1000
    
    let result = computeKernelMatrix backend featureMap data shots
    
    match result with
    | Error msg ->
        Assert.Contains("cannot be empty", msg)
    | Ok _ ->
        Assert.True(false, "Should have rejected empty dataset")

[<Fact>]
let ``computeKernelMatrix - all values should be in range 0 to 1`` () =
    let featureMap = AngleEncoding
    let data = [|
        [| 0.1; 0.2 |]
        [| 0.8; 0.9 |]
        [| 0.4; 0.5 |]
    |]
    let shots = 500
    
    let result = computeKernelMatrix backend featureMap data shots
    
    match result with
    | Ok matrix ->
        for i in 0 .. 2 do
            for j in 0 .. 2 do
                Assert.True(matrix.[i, j] >= 0.0 && matrix.[i, j] <= 1.0,
                    sprintf "K[%d,%d]=%f should be in [0,1]" i j matrix.[i,j])
    | Error err ->
        Assert.True(false, sprintf "Should not fail: %s" err)

// ============================================================================
// Train/Test Kernel Matrix Tests
// ============================================================================

[<Fact>]
let ``computeKernelMatrixTrainTest - should have correct dimensions`` () =
    let featureMap = AngleEncoding
    let trainData = [|
        [| 0.1; 0.2 |]
        [| 0.3; 0.4 |]
        [| 0.5; 0.6 |]
    |]
    let testData = [|
        [| 0.7; 0.8 |]
        [| 0.9; 1.0 |]
    |]
    let shots = 500
    
    let result = computeKernelMatrixTrainTest backend featureMap trainData testData shots
    
    match result with
    | Ok matrix ->
        // Should be 2 (test) × 3 (train)
        Assert.Equal(2, Array2D.length1 matrix)
        Assert.Equal(3, Array2D.length2 matrix)
    | Error err ->
        Assert.True(false, sprintf "Should not fail: %s" err)

[<Fact>]
let ``computeKernelMatrixTrainTest - all values should be in range`` () =
    let featureMap = AngleEncoding
    let trainData = [| [| 0.1; 0.2 |]; [| 0.3; 0.4 |] |]
    let testData = [| [| 0.5; 0.6 |] |]
    let shots = 500
    
    let result = computeKernelMatrixTrainTest backend featureMap trainData testData shots
    
    match result with
    | Ok matrix ->
        for i in 0 .. 0 do
            for j in 0 .. 1 do
                Assert.True(matrix.[i, j] >= 0.0 && matrix.[i, j] <= 1.0,
                    sprintf "K[%d,%d]=%f should be in [0,1]" i j matrix.[i,j])
    | Error err ->
        Assert.True(false, sprintf "Should not fail: %s" err)

[<Fact>]
let ``computeKernelMatrixTrainTest - should reject empty train data`` () =
    let featureMap = AngleEncoding
    let trainData = [||]
    let testData = [| [| 0.5; 0.6 |] |]
    let shots = 500
    
    let result = computeKernelMatrixTrainTest backend featureMap trainData testData shots
    
    match result with
    | Error msg ->
        Assert.Contains("Training dataset cannot be empty", msg)
    | Ok _ ->
        Assert.True(false, "Should have rejected empty train data")

[<Fact>]
let ``computeKernelMatrixTrainTest - should reject empty test data`` () =
    let featureMap = AngleEncoding
    let trainData = [| [| 0.1; 0.2 |] |]
    let testData = [||]
    let shots = 500
    
    let result = computeKernelMatrixTrainTest backend featureMap trainData testData shots
    
    match result with
    | Error msg ->
        Assert.Contains("Test dataset cannot be empty", msg)
    | Ok _ ->
        Assert.True(false, "Should have rejected empty test data")

// ============================================================================
// Kernel Properties Tests
// ============================================================================

[<Fact>]
let ``isSymmetric - should detect symmetric matrix`` () =
    let matrix = array2D [[1.0; 0.5]; [0.5; 1.0]]
    let tolerance = 1e-6
    
    let result = isSymmetric matrix tolerance
    
    Assert.True(result, "Matrix should be detected as symmetric")

[<Fact>]
let ``isSymmetric - should detect non-symmetric matrix`` () =
    let matrix = array2D [[1.0; 0.5]; [0.3; 1.0]]  // Not symmetric
    let tolerance = 1e-6
    
    let result = isSymmetric matrix tolerance
    
    Assert.False(result, "Matrix should be detected as non-symmetric")

[<Fact>]
let ``isSymmetric - should reject non-square matrix`` () =
    let matrix = array2D [[1.0; 0.5; 0.3]]  // 1x3 matrix
    let tolerance = 1e-6
    
    let result = isSymmetric matrix tolerance
    
    Assert.False(result, "Non-square matrix cannot be symmetric")

[<Fact>]
let ``isPositiveSemiDefinite - should accept matrix with non-negative diagonal`` () =
    let matrix = array2D [[1.0; 0.5]; [0.5; 0.8]]
    
    let result = isPositiveSemiDefinite matrix
    
    Assert.True(result, "Matrix with positive diagonal should pass")

[<Fact>]
let ``isPositiveSemiDefinite - should reject matrix with negative diagonal`` () =
    let matrix = array2D [[1.0; 0.5]; [0.5; -0.1]]  // Negative on diagonal
    
    let result = isPositiveSemiDefinite matrix
    
    Assert.False(result, "Matrix with negative diagonal should fail")

// ============================================================================
// Normalization Tests
// ============================================================================

[<Fact>]
let ``normalizeKernelMatrix - should normalize diagonal to 1`` () =
    let matrix = array2D [[2.0; 1.0]; [1.0; 3.0]]
    
    let result = normalizeKernelMatrix matrix
    
    match result with
    | Ok normalized ->
        // Diagonal should be 1.0
        Assert.True(abs (normalized.[0, 0] - 1.0) < epsilon,
            sprintf "K_norm[0,0] should be 1.0, got %f" normalized.[0,0])
        Assert.True(abs (normalized.[1, 1] - 1.0) < epsilon,
            sprintf "K_norm[1,1] should be 1.0, got %f" normalized.[1,1])
        
        // Off-diagonal: K_norm[0,1] = 1.0 / sqrt(2.0 * 3.0) ≈ 0.408
        let expected = 1.0 / sqrt (2.0 * 3.0)
        Assert.True(abs (normalized.[0, 1] - expected) < 0.01,
            sprintf "K_norm[0,1] should be %f, got %f" expected normalized.[0,1])
    | Error err ->
        Assert.True(false, sprintf "Should not fail: %s" err)

[<Fact>]
let ``normalizeKernelMatrix - should reject non-square matrix`` () =
    let matrix = array2D [[1.0; 0.5; 0.3]]  // 1x3
    
    let result = normalizeKernelMatrix matrix
    
    match result with
    | Error msg ->
        Assert.Contains("must be square", msg)
    | Ok _ ->
        Assert.True(false, "Should have rejected non-square matrix")

[<Fact>]
let ``normalizeKernelMatrix - should reject matrix with zero diagonal`` () =
    let matrix = array2D [[0.0; 0.5]; [0.5; 1.0]]  // Zero on diagonal
    
    let result = normalizeKernelMatrix matrix
    
    match result with
    | Error msg ->
        Assert.Contains("must be positive", msg)
    | Ok _ ->
        Assert.True(false, "Should have rejected zero diagonal")

// ============================================================================
// Helper Functions Tests
// ============================================================================

[<Fact>]
let ``getDiagonal - should extract diagonal elements`` () =
    let matrix = array2D [[1.0; 2.0]; [3.0; 4.0]]
    
    let diagonal = getDiagonal matrix
    
    Assert.Equal(2, diagonal.Length)
    Assert.Equal(1.0, diagonal.[0])
    Assert.Equal(4.0, diagonal.[1])

[<Fact>]
let ``getDiagonal - should work with non-square matrix`` () =
    let matrix = array2D [[1.0; 2.0; 3.0]; [4.0; 5.0; 6.0]]  // 2x3
    
    let diagonal = getDiagonal matrix
    
    // Should get min(2,3) = 2 diagonal elements
    Assert.Equal(2, diagonal.Length)
    Assert.Equal(1.0, diagonal.[0])
    Assert.Equal(5.0, diagonal.[1])

[<Fact>]
let ``computeStats - should compute correct statistics`` () =
    let matrix = array2D [[1.0; 0.5]; [0.5; 1.0]]
    
    let stats = computeStats matrix
    
    // Mean = (1.0 + 0.5 + 0.5 + 1.0) / 4 = 0.75
    Assert.True(abs (stats.Mean - 0.75) < epsilon,
        sprintf "Mean should be 0.75, got %f" stats.Mean)
    
    // Min = 0.5, Max = 1.0
    Assert.Equal(0.5, stats.Min)
    Assert.Equal(1.0, stats.Max)
    
    // Diagonal mean = (1.0 + 1.0) / 2 = 1.0
    Assert.Equal(1.0, stats.DiagonalMean)
    
    // Should detect symmetry
    Assert.True(stats.IsSymmetric, "Should detect symmetric matrix")
    
    // Should detect positive semi-definite
    Assert.True(stats.IsPositiveSemiDefinite, "Should detect PSD matrix")

// ============================================================================
// Integration Tests with Different Feature Maps
// ============================================================================

[<Fact>]
let ``computeKernel - should work with ZZFeatureMap`` () =
    let featureMap = ZZFeatureMap 1
    let x = [| 0.5; 0.3 |]
    let y = [| 0.7; 0.4 |]
    let shots = 1000
    
    let result = computeKernel backend featureMap x y shots
    
    match result with
    | Ok kernelValue ->
        Assert.True(kernelValue >= 0.0 && kernelValue <= 1.0,
            sprintf "Kernel value should be in [0,1], got %f" kernelValue)
    | Error err ->
        Assert.True(false, sprintf "Should not fail: %s" err)

[<Fact>]
let ``computeKernelMatrix - should work with ZZFeatureMap`` () =
    let featureMap = ZZFeatureMap 1
    let data = [|
        [| 0.1; 0.2 |]
        [| 0.3; 0.4 |]
    |]
    let shots = 500
    
    let result = computeKernelMatrix backend featureMap data shots
    
    match result with
    | Ok matrix ->
        Assert.Equal(2, Array2D.length1 matrix)
        Assert.Equal(2, Array2D.length2 matrix)
        
        // Diagonal should be high
        Assert.True(matrix.[0, 0] > 0.7, sprintf "K[0,0] should be high, got %f" matrix.[0,0])
        Assert.True(matrix.[1, 1] > 0.7, sprintf "K[1,1] should be high, got %f" matrix.[1,1])
    | Error err ->
        Assert.True(false, sprintf "Should not fail: %s" err)

[<Fact>]
let ``computeKernelMatrix - properties should hold for real quantum kernel`` () =
    let featureMap = AngleEncoding
    let data = [|
        [| 0.1; 0.2 |]
        [| 0.5; 0.6 |]
        [| 0.9; 1.0 |]
    |]
    let shots = 1000
    
    let result = computeKernelMatrix backend featureMap data shots
    
    match result with
    | Ok matrix ->
        let stats = computeStats matrix
        
        // All values in valid range
        Assert.True(stats.Min >= 0.0 && stats.Max <= 1.0,
            sprintf "All kernel values should be in [0,1]: min=%f, max=%f" stats.Min stats.Max)
        
        // Should be symmetric (within quantum noise)
        Assert.True(isSymmetric matrix 0.2,
            "Kernel matrix should be approximately symmetric")
        
        // Diagonal mean should be high (self-similarity)
        Assert.True(stats.DiagonalMean > 0.8,
            sprintf "Diagonal mean should be high, got %f" stats.DiagonalMean)
    | Error err ->
        Assert.True(false, sprintf "Should not fail: %s" err)
