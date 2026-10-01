module FSharp.Azure.Quantum.Tests.QuantumKernelSVMTests

open System.Threading
open System.Threading.Tasks
open Xunit
open FSharp.Azure.Quantum
open FSharp.Azure.Quantum.MachineLearning
open FSharp.Azure.Quantum.MachineLearning.QuantumKernelSVM
open FSharp.Azure.Quantum.Core.BackendAbstraction
open FSharp.Azure.Quantum.Backends

// ============================================================================
// Test Setup
// ============================================================================

let private backend = LocalBackend.LocalBackend() :> IQuantumBackend

let private createSimpleDataset () =
    // Linearly separable dataset
    let trainData =
        [|
            [| 0.1; 0.2 |] // Class 0
            [| 0.2; 0.1 |] // Class 0
            [| 0.8; 0.9 |] // Class 1
            [| 0.9; 0.8 |] // Class 1
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
let ``train - should reject empty training data`` () : Task =
    task {
        let featureMap = AngleEncoding
        let trainData = [||]
        let trainLabels = [||]
        let config = defaultConfig
        let shots = 500

        let! result =
            trainAsync backend featureMap trainData trainLabels config shots CancellationToken.None

        result
        |> Result.map (fun _ -> Assert.True(false, "Should have rejected empty data"))
        |> Result.defaultWith (fun msg -> Assert.Contains("cannot be empty", msg.Message))
    }

[<Fact>]
let ``train - should reject mismatched data and labels`` () : Task =
    task {
        let featureMap = AngleEncoding
        let trainData = [| [| 0.1; 0.2 |]; [| 0.3; 0.4 |] |]
        let trainLabels = [| 0 |] // Wrong length
        let config = defaultConfig
        let shots = 500

        let! result =
            trainAsync backend featureMap trainData trainLabels config shots CancellationToken.None

        result
        |> Result.map (fun _ -> Assert.True(false, "Should have rejected mismatched lengths"))
        |> Result.defaultWith (fun msg -> Assert.Contains("same length", msg.Message))
    }

[<Fact>]
let ``train - should reject invalid labels`` () : Task =
    task {
        let featureMap = AngleEncoding
        let trainData = [| [| 0.1; 0.2 |]; [| 0.3; 0.4 |] |]
        let trainLabels = [| 0; 2 |] // Invalid label: 2
        let config = defaultConfig
        let shots = 500

        let! result =
            trainAsync backend featureMap trainData trainLabels config shots CancellationToken.None

        result
        |> Result.map (fun _ -> Assert.True(false, "Should have rejected invalid labels"))
        |> Result.defaultWith (fun msg -> Assert.Contains("must be 0 or 1", msg.Message))
    }

[<Fact>]
let ``train - should reject non-positive C`` () : Task =
    task {
        let featureMap = AngleEncoding
        let (trainData, trainLabels) = createSimpleDataset ()
        let config = { defaultConfig with C = 0.0 }
        let shots = 500

        let! result =
            trainAsync backend featureMap trainData trainLabels config shots CancellationToken.None

        result
        |> Result.map (fun _ -> Assert.True(false, "Should have rejected non-positive C"))
        |> Result.defaultWith (fun msg -> Assert.Contains("must be positive", msg.Message))
    }

[<Fact>]
let ``train - should reject non-positive shots`` () : Task =
    task {
        let featureMap = AngleEncoding
        let (trainData, trainLabels) = createSimpleDataset ()
        let config = defaultConfig
        let shots = 0

        let! result =
            trainAsync backend featureMap trainData trainLabels config shots CancellationToken.None

        result
        |> Result.map (fun _ -> Assert.True(false, "Should have rejected zero shots"))
        |> Result.defaultWith (fun msg -> Assert.Contains("must be positive", msg.Message))
    }

// ============================================================================
// Training Functional Tests
// ============================================================================

[<Fact>]
let ``train - should complete successfully on simple dataset`` () : Task =
    task {
        let featureMap = AngleEncoding
        let (trainData, trainLabels) = createSimpleDataset ()
        let config = { defaultConfig with Verbose = false }
        let shots = 500

        match! trainAsync backend featureMap trainData trainLabels config shots CancellationToken.None with
        | Ok model ->
            Assert.True(model.SupportVectorIndices.Length > 0, "Should have support vectors")
            Assert.Equal(model.SupportVectorIndices.Length, model.Alphas.Length)
            Assert.Equal(trainData.Length, model.TrainData.Length)
            Assert.Equal(trainLabels.Length, model.TrainLabels.Length)
        | Error err -> Assert.True(false, $"Training should succeed: %s{err.Message}")
    }

[<Fact>]
let ``train - support vectors should have positive alphas`` () : Task =
    task {
        let featureMap = AngleEncoding
        let (trainData, trainLabels) = createSimpleDataset ()
        let config = { defaultConfig with Verbose = false }
        let shots = 500

        match! trainAsync backend featureMap trainData trainLabels config shots CancellationToken.None with
        | Ok model ->
            // All alphas should be positive
            for alpha in model.Alphas do
                Assert.True(alpha > 0.0, $"Alpha should be positive, got %f{alpha}")
        | Error err -> Assert.True(false, $"Training should succeed: %s{err.Message}")
    }

[<Fact>]
let ``train - should handle balanced classes`` () : Task =
    task {
        let featureMap = AngleEncoding

        let trainData =
            [|
                [| 0.1; 0.2 |]
                [| 0.2; 0.3 |] // Class 0
                [| 0.7; 0.8 |]
                [| 0.8; 0.9 |] // Class 1
            |]

        let trainLabels = [| 0; 0; 1; 1 |]
        let config = { defaultConfig with Verbose = false }
        let shots = 500

        let! result =
            trainAsync backend featureMap trainData trainLabels config shots CancellationToken.None

        result
        |> Result.map (fun model -> Assert.True(model.SupportVectorIndices.Length > 0, "Should have support vectors"))
        |> Result.defaultWith (fun err -> Assert.True(false, $"Training should succeed: %s{err.Message}"))
    }

// ============================================================================
// Prediction Tests
// ============================================================================

[<Fact; Trait("Category", "Slow")>]
let ``predict - should return valid label`` () : Task =
    task {
        let featureMap = AngleEncoding
        let (trainData, trainLabels) = createSimpleDataset ()
        let config = { defaultConfig with Verbose = false }
        let shots = 500

        match! trainAsync backend featureMap trainData trainLabels config shots CancellationToken.None with
        | Error err -> Assert.True(false, $"Training failed: %s{err.Message}")
        | Ok model ->
            let testSample = [| 0.15; 0.15 |] // Should be class 0

            let! result = predictAsync backend model testSample shots CancellationToken.None

            result
            |> Result.map (fun prediction ->
                Assert.True(prediction.Label = 0 || prediction.Label = 1, "Label should be 0 or 1"))
            |> Result.defaultWith (fun err -> Assert.True(false, $"Prediction failed: %s{err.Message}"))
    }

[<Fact>]
let ``predict - should reject non-positive shots`` () : Task =
    task {
        let featureMap = AngleEncoding
        let (trainData, trainLabels) = createSimpleDataset ()
        let config = { defaultConfig with Verbose = false }
        let shots = 500

        match! trainAsync backend featureMap trainData trainLabels config shots CancellationToken.None with
        | Error err -> Assert.True(false, $"Training failed: %s{err.Message}")
        | Ok model ->
            let testSample = [| 0.15; 0.15 |]

            let! result = predictAsync backend model testSample 0 CancellationToken.None

            result
            |> Result.map (fun _ -> Assert.True(false, "Should have rejected zero shots"))
            |> Result.defaultWith (fun msg -> Assert.Contains("must be positive", msg.Message))
    }

[<Fact>]
let ``predict - should classify training samples correctly`` () : Task =
    task {
        let featureMap = AngleEncoding
        let (trainData, trainLabels) = createSimpleDataset ()

        let config =
            { defaultConfig with
                Verbose = false
                MaxIterations = 200
            }

        let shots = 1000

        match! trainAsync backend featureMap trainData trainLabels config shots CancellationToken.None with
        | Error err -> Assert.True(false, $"Training failed: %s{err.Message}")
        | Ok model ->
            // Test on training samples (should classify most correctly)
            let mutable correctCount = 0

            for i in 0 .. trainData.Length - 1 do
                match! predictAsync backend model trainData.[i] shots CancellationToken.None with
                | Ok prediction ->
                    if prediction.Label = trainLabels.[i] then
                        correctCount <- correctCount + 1
                | Error e -> Assert.Fail($"prediction of training sample {i} failed: {e.Message}")

            // Should get at least 50% correct (with quantum noise)
            Assert.True(
                correctCount >= 2,
                $"Should classify at least 2/4 training samples correctly, got %d{correctCount}"
            )
    }

[<Fact>]
let ``predict - decision value should have correct sign`` () : Task =
    task {
        let featureMap = AngleEncoding
        let (trainData, trainLabels) = createSimpleDataset ()
        let config = { defaultConfig with Verbose = false }
        let shots = 500

        match! trainAsync backend featureMap trainData trainLabels config shots CancellationToken.None with
        | Error err -> Assert.True(false, $"Training failed: %s{err.Message}")
        | Ok model ->
            let testSample = [| 0.15; 0.15 |]

            match! predictAsync backend model testSample shots CancellationToken.None with
            | Error err -> Assert.True(false, $"Prediction failed: %s{err.Message}")
            | Ok prediction ->
                // Decision value sign should match label
                if prediction.Label = 1 then
                    Assert.True(
                        prediction.DecisionValue >= 0.0,
                        $"Label 1 should have non-negative decision value, got %f{prediction.DecisionValue}"
                    )
                else
                    Assert.True(
                        prediction.DecisionValue < 0.0,
                        $"Label 0 should have negative decision value, got %f{prediction.DecisionValue}"
                    )
    }

// ============================================================================
// Evaluation Tests
// ============================================================================

[<Fact>]
let ``evaluate - should return accuracy between 0 and 1`` () : Task =
    task {
        let featureMap = AngleEncoding
        let (trainData, trainLabels) = createSimpleDataset ()
        let config = { defaultConfig with Verbose = false }
        let shots = 500

        match! trainAsync backend featureMap trainData trainLabels config shots CancellationToken.None with
        | Error err -> Assert.True(false, $"Training failed: %s{err.Message}")
        | Ok model ->
            match! evaluateAsync backend model trainData trainLabels shots CancellationToken.None with
            | Error err -> Assert.True(false, $"Evaluation failed: %s{err.Message}")
            | Ok accuracy ->
                Assert.True(accuracy >= 0.0 && accuracy <= 1.0, $"Accuracy should be in [0,1], got %f{accuracy}")
    }

[<Fact>]
let ``evaluate - should reject empty test data`` () : Task =
    task {
        let featureMap = AngleEncoding
        let (trainData, trainLabels) = createSimpleDataset ()
        let config = { defaultConfig with Verbose = false }
        let shots = 500

        match! trainAsync backend featureMap trainData trainLabels config shots CancellationToken.None with
        | Error err -> Assert.True(false, $"Training failed: %s{err.Message}")
        | Ok model ->
            let! result = evaluateAsync backend model [||] [||] shots CancellationToken.None

            result
            |> Result.map (fun _ -> Assert.True(false, "Should have rejected empty test data"))
            |> Result.defaultWith (fun msg -> Assert.Contains("cannot be empty", msg.Message))
    }

[<Fact>]
let ``evaluate - should reject mismatched test data and labels`` () : Task =
    task {
        let featureMap = AngleEncoding
        let (trainData, trainLabels) = createSimpleDataset ()
        let config = { defaultConfig with Verbose = false }
        let shots = 500

        match! trainAsync backend featureMap trainData trainLabels config shots CancellationToken.None with
        | Error err -> Assert.True(false, $"Training failed: %s{err.Message}")
        | Ok model ->
            let testData = [| [| 0.5; 0.5 |] |]
            let testLabels = [| 0; 1 |] // Wrong length

            let! result =
                evaluateAsync backend model testData testLabels shots CancellationToken.None

            result
            |> Result.map (fun _ -> Assert.True(false, "Should have rejected mismatched lengths"))
            |> Result.defaultWith (fun msg -> Assert.Contains("same length", msg.Message))
    }

[<Fact>]
let ``evaluate - should achieve reasonable accuracy on training data`` () : Task =
    task {
        let featureMap = AngleEncoding
        let (trainData, trainLabels) = createSimpleDataset ()

        let config =
            { defaultConfig with
                Verbose = false
                MaxIterations = 200
            }

        let shots = 1000

        match! trainAsync backend featureMap trainData trainLabels config shots CancellationToken.None with
        | Error err -> Assert.True(false, $"Training failed: %s{err.Message}")
        | Ok model ->
            match! evaluateAsync backend model trainData trainLabels shots CancellationToken.None with
            | Error err -> Assert.True(false, $"Evaluation failed: %s{err.Message}")
            | Ok accuracy ->
                // With quantum noise, should get at least 50% accuracy
                Assert.True(accuracy >= 0.5, $"Training accuracy should be >= 0.5, got %f{accuracy}")
    }

// ============================================================================
// Integration Tests
// ============================================================================

[<Fact>]
let ``train and predict - end-to-end workflow`` () : Task =
    task {
        let featureMap = AngleEncoding
        let (trainData, trainLabels) = createSimpleDataset ()
        let config = { defaultConfig with Verbose = false }
        let shots = 500

        // Train
        match! trainAsync backend featureMap trainData trainLabels config shots CancellationToken.None with
        | Error err -> Assert.True(false, $"Training failed: %s{err.Message}")
        | Ok model ->
            // Predict on new samples
            let testSamples =
                [|
                    [| 0.1; 0.1 |] // Should be class 0
                    [| 0.9; 0.9 |] // Should be class 1
                |]

            for testSample in testSamples do
                match! predictAsync backend model testSample shots CancellationToken.None with
                | Error err -> Assert.True(false, $"Prediction failed: %s{err.Message}")
                | Ok prediction ->
                    Assert.True(prediction.Label = 0 || prediction.Label = 1, "Should return valid label")
    }

[<Fact>]
let ``train with different feature maps`` () : Task =
    task {
        let (trainData, trainLabels) = createSimpleDataset ()
        let config = { defaultConfig with Verbose = false }
        let shots = 500

        // Test with AngleEncoding
        let! angleResult =
            trainAsync backend AngleEncoding trainData trainLabels config shots CancellationToken.None

        angleResult
        |> Result.map (fun _ -> ())
        |> Result.defaultWith (fun err -> Assert.True(false, $"AngleEncoding training failed: %s{err.Message}"))

        // Test with ZZFeatureMap
        let! zzResult =
            trainAsync backend (ZZFeatureMap 1) trainData trainLabels config shots CancellationToken.None

        zzResult
        |> Result.map (fun _ -> ())
        |> Result.defaultWith (fun err -> Assert.True(false, $"ZZFeatureMap training failed: %s{err.Message}"))
    }

[<Fact>]
let ``train with different C values`` () : Task =
    task {
        let featureMap = AngleEncoding
        let (trainData, trainLabels) = createSimpleDataset ()
        let shots = 500

        // Test with small C (more regularization)
        let configSmallC =
            { defaultConfig with
                C = 0.1
                Verbose = false
            }

        let! smallCResult =
            trainAsync backend featureMap trainData trainLabels configSmallC shots CancellationToken.None

        smallCResult
        |> Result.map (fun modelSmallC ->
            Assert.True(modelSmallC.SupportVectorIndices.Length > 0, "Should have support vectors"))
        |> Result.defaultWith (fun err -> Assert.True(false, $"Small C training failed: %s{err.Message}"))

        // Test with large C (less regularization)
        let configLargeC =
            { defaultConfig with
                C = 10.0
                Verbose = false
            }

        let! largeCResult =
            trainAsync backend featureMap trainData trainLabels configLargeC shots CancellationToken.None

        largeCResult
        |> Result.map (fun modelLargeC ->
            Assert.True(modelLargeC.SupportVectorIndices.Length > 0, "Should have support vectors"))
        |> Result.defaultWith (fun err -> Assert.True(false, $"Large C training failed: %s{err.Message}"))
    }

// ============================================================================
// Async Prediction Tests
// ============================================================================

[<Fact>]
let ``predictAsync - should return valid label`` () : Task =
    task {
        let featureMap = AngleEncoding
        let (trainData, trainLabels) = createSimpleDataset ()
        let config = { defaultConfig with Verbose = false }
        let shots = 500

        match! trainAsync backend featureMap trainData trainLabels config shots CancellationToken.None with
        | Error err -> Assert.True(false, $"Training failed: %s{err.Message}")
        | Ok model ->
            let testSample = [| 0.15; 0.15 |]

            match! predictAsync backend model testSample shots CancellationToken.None with
            | Error err -> Assert.True(false, $"predictAsync failed: %s{err.Message}")
            | Ok prediction -> Assert.True(prediction.Label = 0 || prediction.Label = 1, "Label should be 0 or 1")
    }

[<Fact>]
let ``predictAsync - should reject non-positive shots`` () : Task =
    task {
        let featureMap = AngleEncoding
        let (trainData, trainLabels) = createSimpleDataset ()
        let config = { defaultConfig with Verbose = false }
        let shots = 500

        match! trainAsync backend featureMap trainData trainLabels config shots CancellationToken.None with
        | Error err -> Assert.True(false, $"Training failed: %s{err.Message}")
        | Ok model ->
            let testSample = [| 0.15; 0.15 |]

            match! predictAsync backend model testSample 0 CancellationToken.None with
            | Error msg -> Assert.Contains("must be positive", msg.Message)
            | Ok _ -> Assert.True(false, "Should have rejected zero shots")
    }

[<Fact>]
let ``predictAsync - decision value sign matches label`` () : Task =
    task {
        let featureMap = AngleEncoding
        let (trainData, trainLabels) = createSimpleDataset ()
        let config = { defaultConfig with Verbose = false }
        let shots = 500

        match! trainAsync backend featureMap trainData trainLabels config shots CancellationToken.None with
        | Error err -> Assert.True(false, $"Training failed: %s{err.Message}")
        | Ok model ->
            let testSample = [| 0.15; 0.15 |]

            match! predictAsync backend model testSample shots CancellationToken.None with
            | Error err -> Assert.True(false, $"predictAsync failed: %s{err.Message}")
            | Ok prediction ->
                if prediction.Label = 1 then
                    Assert.True(
                        prediction.DecisionValue >= 0.0,
                        $"Label 1 should have non-negative decision value, got %f{prediction.DecisionValue}"
                    )
                else
                    Assert.True(
                        prediction.DecisionValue < 0.0,
                        $"Label 0 should have negative decision value, got %f{prediction.DecisionValue}"
                    )
    }

// ============================================================================
// Async Evaluation Tests
// ============================================================================

[<Fact>]
let ``evaluateAsync - should return accuracy between 0 and 1`` () : Task =
    task {
        let featureMap = AngleEncoding
        let (trainData, trainLabels) = createSimpleDataset ()
        let config = { defaultConfig with Verbose = false }
        let shots = 500

        match! trainAsync backend featureMap trainData trainLabels config shots CancellationToken.None with
        | Error err -> Assert.True(false, $"Training failed: %s{err.Message}")
        | Ok model ->
            match! evaluateAsync backend model trainData trainLabels shots CancellationToken.None with
            | Error err -> Assert.True(false, $"evaluateAsync failed: %s{err.Message}")
            | Ok accuracy ->
                Assert.True(accuracy >= 0.0 && accuracy <= 1.0, $"Accuracy should be in [0,1], got %f{accuracy}")
    }

[<Fact>]
let ``evaluateAsync - should reject empty test data`` () : Task =
    task {
        let featureMap = AngleEncoding
        let (trainData, trainLabels) = createSimpleDataset ()
        let config = { defaultConfig with Verbose = false }
        let shots = 500

        match! trainAsync backend featureMap trainData trainLabels config shots CancellationToken.None with
        | Error err -> Assert.True(false, $"Training failed: %s{err.Message}")
        | Ok model ->
            match! evaluateAsync backend model [||] [||] shots CancellationToken.None with
            | Error msg -> Assert.Contains("cannot be empty", msg.Message)
            | Ok _ -> Assert.True(false, "Should have rejected empty test data")
    }

[<Fact>]
let ``evaluateAsync - should reject mismatched test data and labels`` () : Task =
    task {
        let featureMap = AngleEncoding
        let (trainData, trainLabels) = createSimpleDataset ()
        let config = { defaultConfig with Verbose = false }
        let shots = 500

        match! trainAsync backend featureMap trainData trainLabels config shots CancellationToken.None with
        | Error err -> Assert.True(false, $"Training failed: %s{err.Message}")
        | Ok model ->
            let testData = [| [| 0.5; 0.5 |] |]
            let testLabels = [| 0; 1 |] // Wrong length

            match! evaluateAsync backend model testData testLabels shots CancellationToken.None with
            | Error msg -> Assert.Contains("same length", msg.Message)
            | Ok _ -> Assert.True(false, "Should have rejected mismatched lengths")
    }

/// A model without support vectors: it needs no training and runs no kernel circuit.
let private biasOnlyModel (bias: float) : SVMModel =
    {
        SupportVectorIndices = [||]
        Alphas = [||]
        Bias = bias
        TrainData = [||]
        TrainLabels = [||]
        FeatureMap = AngleEncoding
    }

[<Theory; InlineData(0); InlineData(-5)>]
let ``evaluateAsync - should reject non-positive shots`` (shots: int) : Task =
    task {
        match! evaluateAsync backend (biasOnlyModel 0.25) [| [| 0.5; 0.5 |] |] [| 1 |] shots CancellationToken.None with
        | Error err -> Assert.Contains("shots must be positive", err.Message)
        | Ok _ -> Assert.True(false, "Should have rejected non-positive shots")
    }

[<Theory; InlineData(0.25, 2); InlineData(-0.25, 1)>]
let ``evaluateAsync - without support vectors every prediction is the sign of the bias``
    (bias: float)
    (expectedCorrect: int)
    : Task =
    task {
        let testData = [| [| 0.1; 0.2 |]; [| 0.3; 0.4 |]; [| 0.5; 0.6 |] |]
        let testLabels = [| 1; 0; 1 |]

        match! evaluateAsync backend (biasOnlyModel bias) testData testLabels 100 CancellationToken.None with
        | Error err -> Assert.True(false, $"evaluateAsync failed: %s{err.Message}")
        | Ok accuracy -> Assert.Equal(float expectedCorrect / 3.0, accuracy, 10)
    }

// ============================================================================
// Async Cancellation Tests
// ============================================================================

[<Fact>]
let ``predictAsync - accepts cancellation token`` () : Task =
    task {
        let featureMap = AngleEncoding
        let (trainData, trainLabels) = createSimpleDataset ()
        let config = { defaultConfig with Verbose = false }
        let shots = 500

        match! trainAsync backend featureMap trainData trainLabels config shots CancellationToken.None with
        | Error err -> Assert.True(false, $"Training failed: %s{err.Message}")
        | Ok model ->
            let testSample = [| 0.15; 0.15 |]
            // The token is never cancelled: passing it must not change the outcome.
            use cts = new CancellationTokenSource()

            match! predictAsync backend model testSample shots cts.Token with
            | Ok prediction -> Assert.Contains(prediction.Label, [ 0; 1 ])
            | Error e -> Assert.Fail($"an uncancelled token must not fail the prediction: {e.Message}")
    }

// ============================================================================
// Async Integration Tests
// ============================================================================

[<Fact>]
let ``train and predictAsync - end-to-end async workflow`` () : Task =
    task {
        let featureMap = AngleEncoding
        let (trainData, trainLabels) = createSimpleDataset ()
        let config = { defaultConfig with Verbose = false }
        let shots = 500

        // Train
        match! trainAsync backend featureMap trainData trainLabels config shots CancellationToken.None with
        | Error err -> Assert.True(false, $"Training failed: %s{err.Message}")
        | Ok model ->
            // Predict on new samples asynchronously
            let testSamples =
                [|
                    [| 0.1; 0.1 |] // Should be class 0
                    [| 0.9; 0.9 |] // Should be class 1
                |]

            for testSample in testSamples do
                match! predictAsync backend model testSample shots CancellationToken.None with
                | Error err -> Assert.True(false, $"predictAsync failed: %s{err.Message}")
                | Ok prediction ->
                    Assert.True(prediction.Label = 0 || prediction.Label = 1, "Should return valid label")

            // Evaluate asynchronously
            match! evaluateAsync backend model trainData trainLabels shots CancellationToken.None with
            | Error err -> Assert.True(false, $"evaluateAsync failed: %s{err.Message}")
            | Ok accuracy -> Assert.True(accuracy >= 0.0 && accuracy <= 1.0, "Valid accuracy")
    }
