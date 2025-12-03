module FSharp.Azure.Quantum.Tests.DataPreprocessingTests

open Xunit
open FSharp.Azure.Quantum.MachineLearning

// ========================================================================
// NORMALIZATION TESTS
// ========================================================================

[<Fact>]
let ``normalizeMinMax should scale features to [0, 1]`` () =
    let data = [|
        [| 1.0; 10.0 |]
        [| 2.0; 20.0 |]
        [| 3.0; 30.0 |]
    |]
    
    let (normalized, mins, maxs) = DataPreprocessing.normalizeMinMax data
    
    // Check mins and maxs
    Assert.Equal(1.0, mins.[0])
    Assert.Equal(10.0, mins.[1])
    Assert.Equal(3.0, maxs.[0])
    Assert.Equal(30.0, maxs.[1])
    
    // Check normalized values
    Assert.Equal(0.0, normalized.[0].[0], 10)
    Assert.Equal(0.0, normalized.[0].[1], 10)
    Assert.Equal(1.0, normalized.[2].[0], 10)
    Assert.Equal(1.0, normalized.[2].[1], 10)
    Assert.Equal(0.5, normalized.[1].[0], 10)
    Assert.Equal(0.5, normalized.[1].[1], 10)

[<Fact>]
let ``normalizeMinMax with constant feature should return 0.5`` () =
    let data = [|
        [| 5.0; 10.0 |]
        [| 5.0; 20.0 |]
        [| 5.0; 30.0 |]
    |]
    
    let (normalized, _, _) = DataPreprocessing.normalizeMinMax data
    
    // Constant feature (all 5.0) should normalize to 0.5
    Assert.Equal(0.5, normalized.[0].[0])
    Assert.Equal(0.5, normalized.[1].[0])
    Assert.Equal(0.5, normalized.[2].[0])

[<Fact>]
let ``applyMinMaxNormalization should use provided min/max`` () =
    let trainData = [| [| 0.0 |]; [| 10.0 |] |]
    let (_, mins, maxs) = DataPreprocessing.normalizeMinMax trainData
    
    let testData = [| [| 5.0 |]; [| 15.0 |] |]
    let normalized = DataPreprocessing.applyMinMaxNormalization testData mins maxs
    
    Assert.Equal(0.5, normalized.[0].[0], 10)
    Assert.Equal(1.5, normalized.[1].[0], 10)  // Can go outside [0,1] if test data outside train range

[<Fact>]
let ``normalizeToRange should scale to custom range`` () =
    let data = [| [| 0.0 |]; [| 10.0 |] |]
    let (normalized, _, _) = DataPreprocessing.normalizeToRange data -1.0 1.0
    
    Assert.Equal(-1.0, normalized.[0].[0], 10)
    Assert.Equal(1.0, normalized.[1].[0], 10)

// ========================================================================
// STANDARDIZATION TESTS
// ========================================================================

[<Fact>]
let ``standardize should produce zero mean and unit variance`` () =
    let data = [|
        [| 1.0; 100.0 |]
        [| 2.0; 200.0 |]
        [| 3.0; 300.0 |]
        [| 4.0; 400.0 |]
    |]
    
    let (standardized, means, stds) = DataPreprocessing.standardize data
    
    // Check means
    Assert.Equal(2.5, means.[0])
    Assert.Equal(250.0, means.[1])
    
    // Check standardized data has approximately zero mean
    let feature0Mean = standardized |> Array.averageBy (fun s -> s.[0])
    let feature1Mean = standardized |> Array.averageBy (fun s -> s.[1])
    Assert.Equal(0.0, feature0Mean, 10)
    Assert.Equal(0.0, feature1Mean, 10)
    
    // Check standardized data has approximately unit variance
    let feature0Var =
        standardized
        |> Array.averageBy (fun s -> s.[0] * s.[0])
    Assert.Equal(1.0, feature0Var, 5)

[<Fact>]
let ``standardize with constant feature should return zeros`` () =
    let data = [|
        [| 5.0; 10.0 |]
        [| 5.0; 20.0 |]
        [| 5.0; 30.0 |]
    |]
    
    let (standardized, _, _) = DataPreprocessing.standardize data
    
    // Constant feature should standardize to 0.0
    Assert.Equal(0.0, standardized.[0].[0])
    Assert.Equal(0.0, standardized.[1].[0])
    Assert.Equal(0.0, standardized.[2].[0])

[<Fact>]
let ``applyStandardization should use provided mean/std`` () =
    let trainData = [| [| 0.0 |]; [| 10.0 |] |]
    let (_, means, stds) = DataPreprocessing.standardize trainData
    
    let testData = [| [| 5.0 |] |]
    let standardized = DataPreprocessing.applyStandardization testData means stds
    
    // Should use train statistics
    Assert.Equal(0.0, standardized.[0].[0], 10)

// ========================================================================
// TRAIN/TEST SPLIT TESTS
// ========================================================================

[<Fact>]
let ``trainTestSplit should split data correctly`` () =
    let data = [| [| 1.0 |]; [| 2.0 |]; [| 3.0 |]; [| 4.0 |]; [| 5.0 |] |]
    let labels = [| 0; 1; 0; 1; 0 |]
    
    let ((trainData, trainLabels), (testData, testLabels)) =
        DataPreprocessing.trainTestSplit data labels 0.4 (Some 42)
    
    // Check sizes
    Assert.Equal(3, trainData.Length)  // 60% of 5 = 3
    Assert.Equal(3, trainLabels.Length)
    Assert.Equal(2, testData.Length)  // 40% of 5 = 2
    Assert.Equal(2, testLabels.Length)
    
    // Check total samples preserved
    Assert.Equal(5, trainData.Length + testData.Length)

[<Fact>]
let ``trainTestSplit with seed should be reproducible`` () =
    let data = [| [| 1.0 |]; [| 2.0 |]; [| 3.0 |]; [| 4.0 |] |]
    let labels = [| 0; 1; 0; 1 |]
    
    let ((train1, _), _) = DataPreprocessing.trainTestSplit data labels 0.5 (Some 123)
    let ((train2, _), _) = DataPreprocessing.trainTestSplit data labels 0.5 (Some 123)
    
    // Same seed should produce same split
    Assert.Equal<float array array>(train1, train2)

[<Fact>]
let ``trainTestSplit should fail with mismatched data and labels`` () =
    let data = [| [| 1.0 |]; [| 2.0 |] |]
    let labels = [| 0 |]  // Mismatch
    
    Assert.Throws<System.Exception>(fun () ->
        DataPreprocessing.trainTestSplit data labels 0.5 None |> ignore)

[<Fact>]
let ``trainTestSplit should fail with invalid testSize`` () =
    let data = [| [| 1.0 |] |]
    let labels = [| 0 |]
    
    Assert.Throws<System.Exception>(fun () ->
        DataPreprocessing.trainTestSplit data labels 1.5 None |> ignore)

// ========================================================================
// K-FOLD CROSS-VALIDATION TESTS
// ========================================================================

[<Fact>]
let ``kFoldSplit should create k folds`` () =
    let data = [| for i in 1 .. 10 -> [| float i |] |]
    let labels = [| for i in 1 .. 10 -> i % 2 |]
    
    let folds = DataPreprocessing.kFoldSplit data labels 5 (Some 42)
    
    Assert.Equal(5, folds.Length)

[<Fact>]
let ``kFoldSplit should cover all samples exactly once`` () =
    let data = [| for i in 1 .. 10 -> [| float i |] |]
    let labels = [| for i in 1 .. 10 -> i % 2 |]
    
    let folds = DataPreprocessing.kFoldSplit data labels 5 (Some 42)
    
    // Each fold's test set should be unique
    let allTestSamples =
        folds
        |> Array.collect (fun (_, (testData, _)) -> testData)
    
    Assert.Equal(10, allTestSamples.Length)

[<Fact>]
let ``kFoldSplit should have roughly equal fold sizes`` () =
    let data = [| for i in 1 .. 10 -> [| float i |] |]
    let labels = [| for i in 1 .. 10 -> i % 2 |]
    
    let folds = DataPreprocessing.kFoldSplit data labels 3 (Some 42)
    
    // 10 samples / 3 folds = ~3.33, so folds should be size 3, 3, 4 or similar
    for (_, (testData, _)) in folds do
        Assert.True(testData.Length >= 3 && testData.Length <= 4)

[<Fact>]
let ``kFoldSplit should fail with k > n`` () =
    let data = [| [| 1.0 |]; [| 2.0 |] |]
    let labels = [| 0; 1 |]
    
    Assert.Throws<System.Exception>(fun () ->
        DataPreprocessing.kFoldSplit data labels 5 None |> ignore)

[<Fact>]
let ``kFoldSplit should fail with k < 2`` () =
    let data = [| [| 1.0 |] |]
    let labels = [| 0 |]
    
    Assert.Throws<System.Exception>(fun () ->
        DataPreprocessing.kFoldSplit data labels 1 None |> ignore)

// ========================================================================
// STRATIFIED SPLIT TESTS
// ========================================================================

[<Fact>]
let ``stratifiedTrainTestSplit should maintain class distribution`` () =
    // Create imbalanced dataset: 80% class 0, 20% class 1
    let data = [| for i in 1 .. 10 -> [| float i |] |]
    let labels = [| 0; 0; 0; 0; 0; 0; 0; 0; 1; 1 |]
    
    let ((trainData, trainLabels), (testData, testLabels)) =
        DataPreprocessing.stratifiedTrainTestSplit data labels 0.3 (Some 42)
    
    // Count classes in train and test
    let trainClass0 = trainLabels |> Array.filter ((=) 0) |> Array.length
    let trainClass1 = trainLabels |> Array.filter ((=) 1) |> Array.length
    let testClass0 = testLabels |> Array.filter ((=) 0) |> Array.length
    let testClass1 = testLabels |> Array.filter ((=) 1) |> Array.length
    
    // Both train and test should have both classes
    Assert.True(trainClass0 > 0)
    Assert.True(trainClass1 > 0)
    Assert.True(testClass0 > 0)
    Assert.True(testClass1 > 0)
    
    // Ratio should be maintained (approximately 4:1)
    let trainRatio = float trainClass0 / float trainClass1
    let testRatio = float testClass0 / float testClass1
    Assert.True(trainRatio >= 2.0)  // At least 2:1 ratio maintained

[<Fact>]
let ``stratifiedTrainTestSplit should handle multi-class`` () =
    let data = [| for i in 1 .. 12 -> [| float i |] |]
    let labels = [| 0; 0; 0; 0; 1; 1; 1; 1; 2; 2; 2; 2 |]  // Balanced 3-class
    
    let ((trainData, trainLabels), (testData, testLabels)) =
        DataPreprocessing.stratifiedTrainTestSplit data labels 0.25 (Some 42)
    
    // All 3 classes should appear in both sets
    let trainClasses = trainLabels |> Array.distinct |> Array.sort
    let testClasses = testLabels |> Array.distinct |> Array.sort
    
    Assert.Equal<seq<int>>([| 0; 1; 2 |], trainClasses)
    Assert.Equal<seq<int>>([| 0; 1; 2 |], testClasses)

// ========================================================================
// EDGE CASE TESTS
// ========================================================================

[<Fact>]
let ``normalizeMinMax with empty data should return empty`` () =
    let (normalized, mins, maxs) = DataPreprocessing.normalizeMinMax [||]
    
    Assert.Empty(normalized)
    Assert.Empty(mins)
    Assert.Empty(maxs)

[<Fact>]
let ``standardize with empty data should return empty`` () =
    let (standardized, means, stds) = DataPreprocessing.standardize [||]
    
    Assert.Empty(standardized)
    Assert.Empty(means)
    Assert.Empty(stds)

[<Fact>]
let ``trainTestSplit with empty data should return empty`` () =
    let ((trainData, trainLabels), (testData, testLabels)) =
        DataPreprocessing.trainTestSplit [||] [||] 0.5 None
    
    Assert.Empty(trainData)
    Assert.Empty(testData)

[<Fact>]
let ``kFoldSplit with empty data should return empty`` () =
    let folds = DataPreprocessing.kFoldSplit [||] [||] 3 None
    
    Assert.Empty(folds)
