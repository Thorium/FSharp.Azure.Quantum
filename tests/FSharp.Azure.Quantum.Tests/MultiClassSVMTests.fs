module FSharp.Azure.Quantum.Tests.MultiClassSVMTests

open Xunit
open FSharp.Azure.Quantum.LocalSimulator
open FSharp.Azure.Quantum.MachineLearning
open FSharp.Azure.Quantum.Core.BackendAbstraction

/// Helper: Create local backend
let private backend = LocalBackend() :> IQuantumBackend

/// Helper: Create simple 3-class dataset
let createThreeClassDataset () =
    let trainData = [|
        [| 0.0; 0.0 |]; [| 0.1; 0.1 |]  // Class 0
        [| 1.0; 0.0 |]; [| 0.9; 0.1 |]  // Class 1
        [| 0.0; 1.0 |]; [| 0.1; 0.9 |]  // Class 2
    |]
    let trainLabels = [| 0; 0; 1; 1; 2; 2 |]
    (trainData, trainLabels)

/// Helper: Create 4-class dataset
let createFourClassDataset () =
    let trainData = [|
        [| 0.0; 0.0 |]; [| 0.1; 0.0 |]  // Class 0
        [| 1.0; 0.0 |]; [| 0.9; 0.0 |]  // Class 1
        [| 0.0; 1.0 |]; [| 0.0; 0.9 |]  // Class 2
        [| 1.0; 1.0 |]; [| 0.9; 0.9 |]  // Class 3
    |]
    let trainLabels = [| 0; 0; 1; 1; 2; 2; 3; 3 |]
    (trainData, trainLabels)

[<Fact>]
let ``MultiClassSVM train should succeed with 3 classes`` () =
    let featureMap = FeatureMapType.ZZFeatureMap 1
    let (trainData, trainLabels) = createThreeClassDataset ()
    let config = QuantumKernelSVM.defaultConfig
    let shots = 1000
    
    match MultiClassSVM.train backend featureMap trainData trainLabels config shots with
    | Error e -> Assert.Fail($"Training failed: {e}")
    | Ok model ->
        Assert.Equal(3, model.NumClasses)
        Assert.Equal(3, model.BinaryModels.Length)
        Assert.Equal<seq<int>>([| 0; 1; 2 |], model.ClassLabels)

[<Fact>]
let ``MultiClassSVM train should succeed with 4 classes`` () =
    let featureMap = FeatureMapType.ZZFeatureMap 1
    let (trainData, trainLabels) = createFourClassDataset ()
    let config = QuantumKernelSVM.defaultConfig
    let shots = 1000
    
    match MultiClassSVM.train backend featureMap trainData trainLabels config shots with
    | Error e -> Assert.Fail($"Training failed: {e}")
    | Ok model ->
        Assert.Equal(4, model.NumClasses)
        Assert.Equal(4, model.BinaryModels.Length)
        Assert.Equal<seq<int>>([| 0; 1; 2; 3 |], model.ClassLabels)

[<Fact>]
let ``MultiClassSVM predict should classify training samples`` () =
    let featureMap = FeatureMapType.ZZFeatureMap 1
    let (trainData, trainLabels) = createThreeClassDataset ()
    let config = QuantumKernelSVM.defaultConfig
    let shots = 1000
    
    match MultiClassSVM.train backend featureMap trainData trainLabels config shots with
    | Error e -> Assert.Fail($"Training failed: {e}")
    | Ok model ->
        match MultiClassSVM.predict backend model trainData.[0] shots with
        | Error e -> Assert.Fail($"Prediction failed: {e}")
        | Ok prediction ->
            Assert.Equal(3, prediction.DecisionValues.Length)
            Assert.True(prediction.Label >= 0 && prediction.Label <= 2)

[<Fact>]
let ``MultiClassSVM evaluate should compute accuracy`` () =
    let featureMap = FeatureMapType.ZZFeatureMap 1
    let (trainData, trainLabels) = createThreeClassDataset ()
    let config = QuantumKernelSVM.defaultConfig
    let shots = 1000
    
    match MultiClassSVM.train backend featureMap trainData trainLabels config shots with
    | Error e -> Assert.Fail($"Training failed: {e}")
    | Ok model ->
        match MultiClassSVM.evaluate backend model trainData trainLabels shots with
        | Error e -> Assert.Fail($"Evaluation failed: {e}")
        | Ok accuracy ->
            Assert.True(accuracy >= 0.0 && accuracy <= 1.0)
            Assert.True(accuracy >= 0.4)  // Reasonable threshold

[<Fact>]
let ``MultiClassSVM confusionMatrix should have correct dimensions`` () =
    let featureMap = FeatureMapType.ZZFeatureMap 1
    let (trainData, trainLabels) = createThreeClassDataset ()
    let config = QuantumKernelSVM.defaultConfig
    let shots = 1000
    
    match MultiClassSVM.train backend featureMap trainData trainLabels config shots with
    | Error e -> Assert.Fail($"Training failed: {e}")
    | Ok model ->
        let predictions = [| 0; 1; 2; 0; 1; 2 |]
        let trueLabels = [| 0; 1; 2; 1; 2; 0 |]
        let confMatrix = MultiClassSVM.confusionMatrix model predictions trueLabels
        Assert.Equal(3, Array2D.length1 confMatrix)
        Assert.Equal(3, Array2D.length2 confMatrix)

[<Fact>]
let ``MultiClassSVM perClassMetrics should return valid metrics`` () =
    let confMatrix = array2D [[ 10; 0; 0 ]; [ 0; 20; 0 ]; [ 0; 0; 15 ]]
    let metrics = MultiClassSVM.perClassMetrics confMatrix
    Assert.Equal(3, metrics.Length)
    for (precision, recall, f1) in metrics do
        Assert.Equal(1.0, precision)
        Assert.Equal(1.0, recall)
        Assert.Equal(1.0, f1)

[<Fact>]
let ``MultiClassSVM macroAverageMetrics should average correctly`` () =
    let perClassMetrics = [| (1.0, 0.8, 0.9); (0.9, 0.9, 0.9); (0.8, 1.0, 0.9) |]
    let (macroPrecision, macroRecall, macroF1) = MultiClassSVM.macroAverageMetrics perClassMetrics
    Assert.Equal(0.9, macroPrecision, 3)
    Assert.Equal(0.9, macroRecall, 3)
    Assert.Equal(0.9, macroF1, 3)
