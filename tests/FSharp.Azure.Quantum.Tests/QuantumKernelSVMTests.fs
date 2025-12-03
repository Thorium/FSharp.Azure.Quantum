module FSharp.Azure.Quantum.Tests.QuantumKernelSVMTests

open Xunit
open FSharp.Azure.Quantum
open FSharp.Azure.Quantum.MachineLearning
open FSharp.Azure.Quantum.MachineLearning.QuantumKernelSVM
open FSharp.Azure.Quantum.Core.BackendAbstraction

// ============================================================================
// Test Setup
// ============================================================================

let private backend = LocalBackend() :> IQuantumBackend

let private createSimpleDataset () =
    // Linearly separable dataset
    let trainData = [|
        [| 0.1; 0.2 |]  // Class 0
        [| 0.2; 0.1 |]  // Class 0
        [| 0.8; 0.9 |]  // Class 1
        [| 0.9; 0.8 |]  // Class 1
    |]
    let trainLabels = [| 0; 0; 1; 1 |]
    (trainData, trainLabels)

// ============================================================================
// Configuration Tests
// ============================================================================

[<Fact>]
let ``defaultConfig - should have valid parameters`` () =
    Assert.True(defaultConfig.C > 0.0, "C must be positive")
    Assert.True(defaultConfig.Tolerance > 0.0, "Tolerance must be positive")
    Assert.True(defaultConfig.MaxIterations > 0, "MaxIterations must be positive")

// ============================================================================
// Training Validation Tests
// ============================================================================

[<Fact>]
let ``train - should reject empty training data`` () =
    let featureMap = AngleEncoding
    let trainData = [||]
    let trainLabels = [||]
    let config = defaultConfig
    let shots = 500
    
    let result = train backend featureMap trainData trainLabels config shots
    
    match result with
    | Error msg ->
        Assert.Contains("cannot be empty", msg)
    | Ok _ ->
        Assert.True(false, "Should have rejected empty data")

[<Fact>]
let ``train - should reject mismatched data and labels`` () =
    let featureMap = AngleEncoding
    let trainData = [| [| 0.1; 0.2 |]; [| 0.3; 0.4 |] |]
    let trainLabels = [| 0 |]  // Wrong length
    let config = defaultConfig
    let shots = 500
    
    let result = train backend featureMap trainData trainLabels config shots
    
    match result with
    | Error msg ->
        Assert.Contains("same length", msg)
    | Ok _ ->
        Assert.True(false, "Should have rejected mismatched lengths")

[<Fact>]
let ``train - should reject invalid labels`` () =
    let featureMap = AngleEncoding
    let trainData = [| [| 0.1; 0.2 |]; [| 0.3; 0.4 |] |]
    let trainLabels = [| 0; 2 |]  // Invalid label: 2
    let config = defaultConfig
    let shots = 500
    
    let result = train backend featureMap trainData trainLabels config shots
    
    match result with
    | Error msg ->
        Assert.Contains("must be 0 or 1", msg)
    | Ok _ ->
        Assert.True(false, "Should have rejected invalid labels")

[<Fact>]
let ``train - should reject non-positive C`` () =
    let featureMap = AngleEncoding
    let (trainData, trainLabels) = createSimpleDataset ()
    let config = { defaultConfig with C = 0.0 }
    let shots = 500
    
    let result = train backend featureMap trainData trainLabels config shots
    
    match result with
    | Error msg ->
        Assert.Contains("must be positive", msg)
    | Ok _ ->
        Assert.True(false, "Should have rejected non-positive C")

[<Fact>]
let ``train - should reject non-positive shots`` () =
    let featureMap = AngleEncoding
    let (trainData, trainLabels) = createSimpleDataset ()
    let config = defaultConfig
    let shots = 0
    
    let result = train backend featureMap trainData trainLabels config shots
    
    match result with
    | Error msg ->
        Assert.Contains("must be positive", msg)
    | Ok _ ->
        Assert.True(false, "Should have rejected zero shots")

// ============================================================================
// Training Functional Tests
// ============================================================================

[<Fact>]
let ``train - should complete successfully on simple dataset`` () =
    let featureMap = AngleEncoding
    let (trainData, trainLabels) = createSimpleDataset ()
    let config = { defaultConfig with Verbose = false }
    let shots = 500
    
    let result = train backend featureMap trainData trainLabels config shots
    
    match result with
    | Ok model ->
        Assert.True(model.SupportVectorIndices.Length > 0, "Should have support vectors")
        Assert.Equal(model.SupportVectorIndices.Length, model.Alphas.Length)
        Assert.Equal(trainData.Length, model.TrainData.Length)
        Assert.Equal(trainLabels.Length, model.TrainLabels.Length)
    | Error err ->
        Assert.True(false, sprintf "Training should succeed: %s" err)

[<Fact>]
let ``train - support vectors should have positive alphas`` () =
    let featureMap = AngleEncoding
    let (trainData, trainLabels) = createSimpleDataset ()
    let config = { defaultConfig with Verbose = false }
    let shots = 500
    
    let result = train backend featureMap trainData trainLabels config shots
    
    match result with
    | Ok model ->
        // All alphas should be positive
        for alpha in model.Alphas do
            Assert.True(alpha > 0.0, sprintf "Alpha should be positive, got %f" alpha)
    | Error err ->
        Assert.True(false, sprintf "Training should succeed: %s" err)

[<Fact>]
let ``train - should handle balanced classes`` () =
    let featureMap = AngleEncoding
    let trainData = [|
        [| 0.1; 0.2 |]; [| 0.2; 0.3 |]  // Class 0
        [| 0.7; 0.8 |]; [| 0.8; 0.9 |]  // Class 1
    |]
    let trainLabels = [| 0; 0; 1; 1 |]
    let config = { defaultConfig with Verbose = false }
    let shots = 500
    
    let result = train backend featureMap trainData trainLabels config shots
    
    match result with
    | Ok model ->
        Assert.True(model.SupportVectorIndices.Length > 0, "Should have support vectors")
    | Error err ->
        Assert.True(false, sprintf "Training should succeed: %s" err)

// ============================================================================
// Prediction Tests
// ============================================================================

[<Fact>]
let ``predict - should return valid label`` () =
    let featureMap = AngleEncoding
    let (trainData, trainLabels) = createSimpleDataset ()
    let config = { defaultConfig with Verbose = false }
    let shots = 500
    
    match train backend featureMap trainData trainLabels config shots with
    | Error err ->
        Assert.True(false, sprintf "Training failed: %s" err)
    | Ok model ->
        let testSample = [| 0.15; 0.15 |]  // Should be class 0
        
        match predict backend model testSample shots with
        | Error err ->
            Assert.True(false, sprintf "Prediction failed: %s" err)
        | Ok prediction ->
            Assert.True(prediction.Label = 0 || prediction.Label = 1, "Label should be 0 or 1")

[<Fact>]
let ``predict - should reject non-positive shots`` () =
    let featureMap = AngleEncoding
    let (trainData, trainLabels) = createSimpleDataset ()
    let config = { defaultConfig with Verbose = false }
    let shots = 500
    
    match train backend featureMap trainData trainLabels config shots with
    | Error err ->
        Assert.True(false, sprintf "Training failed: %s" err)
    | Ok model ->
        let testSample = [| 0.15; 0.15 |]
        
        match predict backend model testSample 0 with
        | Error msg ->
            Assert.Contains("must be positive", msg)
        | Ok _ ->
            Assert.True(false, "Should have rejected zero shots")

[<Fact>]
let ``predict - should classify training samples correctly`` () =
    let featureMap = AngleEncoding
    let (trainData, trainLabels) = createSimpleDataset ()
    let config = { defaultConfig with Verbose = false; MaxIterations = 200 }
    let shots = 1000
    
    match train backend featureMap trainData trainLabels config shots with
    | Error err ->
        Assert.True(false, sprintf "Training failed: %s" err)
    | Ok model ->
        // Test on training samples (should classify most correctly)
        let mutable correctCount = 0
        
        for i in 0 .. trainData.Length - 1 do
            match predict backend model trainData.[i] shots with
            | Ok prediction ->
                if prediction.Label = trainLabels.[i] then
                    correctCount <- correctCount + 1
            | Error _ -> ()
        
        // Should get at least 50% correct (with quantum noise)
        Assert.True(correctCount >= 2, 
            sprintf "Should classify at least 2/4 training samples correctly, got %d" correctCount)

[<Fact>]
let ``predict - decision value should have correct sign`` () =
    let featureMap = AngleEncoding
    let (trainData, trainLabels) = createSimpleDataset ()
    let config = { defaultConfig with Verbose = false }
    let shots = 500
    
    match train backend featureMap trainData trainLabels config shots with
    | Error err ->
        Assert.True(false, sprintf "Training failed: %s" err)
    | Ok model ->
        let testSample = [| 0.15; 0.15 |]
        
        match predict backend model testSample shots with
        | Error err ->
            Assert.True(false, sprintf "Prediction failed: %s" err)
        | Ok prediction ->
            // Decision value sign should match label
            if prediction.Label = 1 then
                Assert.True(prediction.DecisionValue >= 0.0,
                    sprintf "Label 1 should have non-negative decision value, got %f" prediction.DecisionValue)
            else
                Assert.True(prediction.DecisionValue < 0.0,
                    sprintf "Label 0 should have negative decision value, got %f" prediction.DecisionValue)

// ============================================================================
// Evaluation Tests
// ============================================================================

[<Fact>]
let ``evaluate - should return accuracy between 0 and 1`` () =
    let featureMap = AngleEncoding
    let (trainData, trainLabels) = createSimpleDataset ()
    let config = { defaultConfig with Verbose = false }
    let shots = 500
    
    match train backend featureMap trainData trainLabels config shots with
    | Error err ->
        Assert.True(false, sprintf "Training failed: %s" err)
    | Ok model ->
        match evaluate backend model trainData trainLabels shots with
        | Error err ->
            Assert.True(false, sprintf "Evaluation failed: %s" err)
        | Ok accuracy ->
            Assert.True(accuracy >= 0.0 && accuracy <= 1.0,
                sprintf "Accuracy should be in [0,1], got %f" accuracy)

[<Fact>]
let ``evaluate - should reject empty test data`` () =
    let featureMap = AngleEncoding
    let (trainData, trainLabels) = createSimpleDataset ()
    let config = { defaultConfig with Verbose = false }
    let shots = 500
    
    match train backend featureMap trainData trainLabels config shots with
    | Error err ->
        Assert.True(false, sprintf "Training failed: %s" err)
    | Ok model ->
        match evaluate backend model [||] [||] shots with
        | Error msg ->
            Assert.Contains("cannot be empty", msg)
        | Ok _ ->
            Assert.True(false, "Should have rejected empty test data")

[<Fact>]
let ``evaluate - should reject mismatched test data and labels`` () =
    let featureMap = AngleEncoding
    let (trainData, trainLabels) = createSimpleDataset ()
    let config = { defaultConfig with Verbose = false }
    let shots = 500
    
    match train backend featureMap trainData trainLabels config shots with
    | Error err ->
        Assert.True(false, sprintf "Training failed: %s" err)
    | Ok model ->
        let testData = [| [| 0.5; 0.5 |] |]
        let testLabels = [| 0; 1 |]  // Wrong length
        
        match evaluate backend model testData testLabels shots with
        | Error msg ->
            Assert.Contains("same length", msg)
        | Ok _ ->
            Assert.True(false, "Should have rejected mismatched lengths")

[<Fact>]
let ``evaluate - should achieve reasonable accuracy on training data`` () =
    let featureMap = AngleEncoding
    let (trainData, trainLabels) = createSimpleDataset ()
    let config = { defaultConfig with Verbose = false; MaxIterations = 200 }
    let shots = 1000
    
    match train backend featureMap trainData trainLabels config shots with
    | Error err ->
        Assert.True(false, sprintf "Training failed: %s" err)
    | Ok model ->
        match evaluate backend model trainData trainLabels shots with
        | Error err ->
            Assert.True(false, sprintf "Evaluation failed: %s" err)
        | Ok accuracy ->
            // With quantum noise, should get at least 50% accuracy
            Assert.True(accuracy >= 0.5,
                sprintf "Training accuracy should be >= 0.5, got %f" accuracy)

// ============================================================================
// Integration Tests
// ============================================================================

[<Fact>]
let ``train and predict - end-to-end workflow`` () =
    let featureMap = AngleEncoding
    let (trainData, trainLabels) = createSimpleDataset ()
    let config = { defaultConfig with Verbose = false }
    let shots = 500
    
    // Train
    match train backend featureMap trainData trainLabels config shots with
    | Error err ->
        Assert.True(false, sprintf "Training failed: %s" err)
    | Ok model ->
        // Predict on new samples
        let testSamples = [|
            [| 0.1; 0.1 |]  // Should be class 0
            [| 0.9; 0.9 |]  // Should be class 1
        |]
        
        for testSample in testSamples do
            match predict backend model testSample shots with
            | Error err ->
                Assert.True(false, sprintf "Prediction failed: %s" err)
            | Ok prediction ->
                Assert.True(prediction.Label = 0 || prediction.Label = 1,
                    "Should return valid label")

[<Fact>]
let ``train with different feature maps`` () =
    let (trainData, trainLabels) = createSimpleDataset ()
    let config = { defaultConfig with Verbose = false }
    let shots = 500
    
    // Test with AngleEncoding
    match train backend AngleEncoding trainData trainLabels config shots with
    | Error err ->
        Assert.True(false, sprintf "AngleEncoding training failed: %s" err)
    | Ok _ -> ()
    
    // Test with ZZFeatureMap
    match train backend (ZZFeatureMap 1) trainData trainLabels config shots with
    | Error err ->
        Assert.True(false, sprintf "ZZFeatureMap training failed: %s" err)
    | Ok _ -> ()

[<Fact>]
let ``train with different C values`` () =
    let featureMap = AngleEncoding
    let (trainData, trainLabels) = createSimpleDataset ()
    let shots = 500
    
    // Test with small C (more regularization)
    let configSmallC = { defaultConfig with C = 0.1; Verbose = false }
    match train backend featureMap trainData trainLabels configSmallC shots with
    | Error err ->
        Assert.True(false, sprintf "Small C training failed: %s" err)
    | Ok modelSmallC ->
        Assert.True(modelSmallC.SupportVectorIndices.Length > 0, "Should have support vectors")
    
    // Test with large C (less regularization)
    let configLargeC = { defaultConfig with C = 10.0; Verbose = false }
    match train backend featureMap trainData trainLabels configLargeC shots with
    | Error err ->
        Assert.True(false, sprintf "Large C training failed: %s" err)
    | Ok modelLargeC ->
        Assert.True(modelLargeC.SupportVectorIndices.Length > 0, "Should have support vectors")
