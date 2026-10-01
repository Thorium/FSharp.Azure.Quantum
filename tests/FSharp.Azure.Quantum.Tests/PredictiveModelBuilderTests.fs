namespace FSharp.Azure.Quantum.Tests

open System
open System.Threading
open System.Threading.Tasks
open Xunit
open FSharp.Azure.Quantum
open FSharp.Azure.Quantum.Core
open FSharp.Azure.Quantum.Business
open FSharp.Azure.Quantum.Backends
open FSharp.Azure.Quantum.Core.BackendAbstraction

module PredictiveModelBuilderTests =
    open PredictiveModel

    // ========================================================================
    // HELPERS
    // ========================================================================

    /// Simple linear regression data: y = 2*x1 + 3*x2 + 1
    let private makeRegressionData () =
        let features =
            [|
                [| 1.0; 0.0 |]
                [| 0.0; 1.0 |]
                [| 1.0; 1.0 |]
                [| 2.0; 0.0 |]
                [| 0.0; 2.0 |]
                [| 2.0; 1.0 |]
                [| 1.0; 2.0 |]
                [| 0.5; 0.5 |]
                [| 1.5; 0.5 |]
                [| 0.5; 1.5 |]
            |]

        let targets = [| 3.0; 4.0; 6.0; 5.0; 7.0; 8.0; 9.0; 3.5; 5.5; 6.5 |]
        (features, targets)

    /// Multi-class data: 3 clusters in 2D
    let private makeMultiClassData () =
        let features =
            [|
                // Class 0: cluster near (0.1, 0.1)
                [| 0.1; 0.1 |]
                [| 0.15; 0.05 |]
                [| 0.05; 0.15 |]
                [| 0.2; 0.1 |]
                // Class 1: cluster near (0.9, 0.1)
                [| 0.9; 0.1 |]
                [| 0.85; 0.15 |]
                [| 0.95; 0.05 |]
                [| 0.8; 0.1 |]
                // Class 2: cluster near (0.5, 0.9)
                [| 0.5; 0.9 |]
                [| 0.45; 0.85 |]
                [| 0.55; 0.95 |]
                [| 0.5; 0.8 |]
            |]

        let targets = [| 0.0; 0.0; 0.0; 0.0; 1.0; 1.0; 1.0; 1.0; 2.0; 2.0; 2.0; 2.0 |]
        (features, targets)

    let private defaultRegressionProblem =
        let features, targets = makeRegressionData ()

        {
            TrainFeatures = features
            TrainTargets = targets
            ProblemType = Regression
            Architecture = Quantum
            LearningRate = 0.01
            MaxEpochs = 5
            ConvergenceThreshold = 0.001
            Backend = None
            Shots = 100
            Verbose = false
            Logger = None
            SavePath = None
            Note = None
            ProgressReporter = None
            CancellationToken = None
        }

    let private defaultMultiClassProblem =
        let features, targets = makeMultiClassData ()

        {
            TrainFeatures = features
            TrainTargets = targets
            ProblemType = MultiClass 3
            Architecture = Quantum
            LearningRate = 0.01
            MaxEpochs = 5
            ConvergenceThreshold = 0.001
            Backend = None
            Shots = 100
            Verbose = false
            Logger = None
            SavePath = None
            Note = None
            ProgressReporter = None
            CancellationToken = None
        }

    // ========================================================================
    // VALIDATION TESTS
    // ========================================================================

    [<Fact>]
    let ``train with empty features should return ValidationError`` () =
        task {
            let problem =
                { defaultRegressionProblem with
                    TrainFeatures = [||]
                }

            match! trainAsync problem CancellationToken.None with
            | Error(QuantumError.ValidationError("Input", msg)) -> Assert.Contains("empty", msg.ToLower())
            | other -> failwith $"Expected ValidationError for empty features, got {other}"
        }
        :> Task

    [<Fact>]
    let ``train with empty targets should return ValidationError`` () =
        task {
            let problem =
                { defaultRegressionProblem with
                    TrainTargets = [||]
                }

            match! trainAsync problem CancellationToken.None with
            | Error(QuantumError.ValidationError("Input", msg)) -> Assert.Contains("empty", msg.ToLower())
            | other -> failwith $"Expected ValidationError for empty targets, got {other}"
        }
        :> Task

    [<Fact>]
    let ``train with mismatched features and targets should return ValidationError`` () =
        task {
            let problem =
                { defaultRegressionProblem with
                    TrainTargets = [| 1.0; 2.0 |]
                }

            match! trainAsync problem CancellationToken.None with
            | Error(QuantumError.ValidationError("Input", msg)) -> Assert.Contains("match", msg.ToLower())
            | other -> failwith $"Expected ValidationError for mismatched lengths, got {other}"
        }
        :> Task

    [<Fact>]
    let ``train with zero-length feature vectors should return ValidationError`` () =
        task {
            let problem =
                { defaultRegressionProblem with
                    TrainFeatures = [| [||]; [||] |]
                    TrainTargets = [| 1.0; 2.0 |]
                }

            match! trainAsync problem CancellationToken.None with
            | Error(QuantumError.ValidationError("Input", msg)) -> Assert.Contains("at least one", msg.ToLower())
            | other -> failwith $"Expected ValidationError for zero-length features, got {other}"
        }
        :> Task

    [<Fact>]
    let ``train with inconsistent feature dimensions should return ValidationError`` () =
        task {
            let problem =
                { defaultRegressionProblem with
                    TrainFeatures = [| [| 1.0; 2.0 |]; [| 1.0 |] |]
                    TrainTargets = [| 1.0; 2.0 |]
                }

            match! trainAsync problem CancellationToken.None with
            | Error(QuantumError.ValidationError("Input", msg)) -> Assert.Contains("same length", msg.ToLower())
            | other -> failwith $"Expected ValidationError for inconsistent features, got {other}"
        }
        :> Task

    [<Fact>]
    let ``train with non-positive learning rate should return ValidationError`` () =
        task {
            let problem =
                { defaultRegressionProblem with
                    LearningRate = 0.0
                }

            match! trainAsync problem CancellationToken.None with
            | Error(QuantumError.ValidationError("Input", msg)) -> Assert.Contains("positive", msg.ToLower())
            | other -> failwith $"Expected ValidationError for zero learning rate, got {other}"
        }
        :> Task

    [<Fact>]
    let ``train with zero epochs should return ValidationError`` () =
        task {
            let problem =
                { defaultRegressionProblem with
                    MaxEpochs = 0
                }

            match! trainAsync problem CancellationToken.None with
            | Error(QuantumError.ValidationError("Input", msg)) -> Assert.Contains("positive", msg.ToLower())
            | other -> failwith $"Expected ValidationError for zero epochs, got {other}"
        }
        :> Task

    [<Fact>]
    let ``train with non-positive convergence threshold should return ValidationError`` () =
        task {
            let problem =
                { defaultRegressionProblem with
                    ConvergenceThreshold = 0.0
                }

            match! trainAsync problem CancellationToken.None with
            | Error(QuantumError.ValidationError("Input", msg)) -> Assert.Contains("positive", msg.ToLower())
            | other -> failwith $"Expected ValidationError for zero convergence threshold, got {other}"
        }
        :> Task

    [<Fact>]
    let ``train with non-positive shots should return ValidationError`` () =
        task {
            let problem =
                { defaultRegressionProblem with
                    Shots = 0
                }

            match! trainAsync problem CancellationToken.None with
            | Error(QuantumError.ValidationError("Input", msg)) -> Assert.Contains("positive", msg.ToLower())
            | other -> failwith $"Expected ValidationError for zero shots, got {other}"
        }
        :> Task

    [<Fact>]
    let ``train with MultiClass less than 2 classes should return error`` () =
        task {
            let problem =
                { defaultMultiClassProblem with
                    ProblemType = MultiClass 1
                }

            let! trained = trainAsync problem CancellationToken.None

            trained
            |> Result.iter (fun _ -> failwith "Should return error for MultiClass with 1 class")
        }
        :> Task

    [<Fact>]
    let ``train with MultiClass labels out of range should return ValidationError`` () =
        task {
            let features, _ = makeMultiClassData ()

            let problem =
                { defaultMultiClassProblem with
                    TrainTargets = [| 0.0; 1.0; 2.0; 3.0; 0.0; 1.0; 2.0; 3.0; 0.0; 1.0; 2.0; 3.0 |]
                    ProblemType = MultiClass 3
                } // labels 0-3 but only 3 classes (0-2 valid)

            match! trainAsync problem CancellationToken.None with
            | Error(QuantumError.ValidationError("Input", msg)) -> Assert.Contains("range", msg.ToLower())
            | other -> failwith $"Expected ValidationError for out-of-range labels, got {other}"
        }
        :> Task

    // ========================================================================
    // REGRESSION TRAINING TESTS
    // ========================================================================

    [<Fact>]
    let ``train Quantum regression should succeed`` () =
        task {
            match! trainAsync defaultRegressionProblem CancellationToken.None with
            | Ok model ->
                Assert.Equal(Regression, model.Metadata.ProblemType)
                Assert.Equal(Quantum, model.Metadata.Architecture)
                Assert.Equal(2, model.Metadata.NumFeatures)
                Assert.Equal(10, model.Metadata.NumSamples)
                Assert.True(model.Metadata.TrainingTime >= TimeSpan.Zero)
            | Error e -> failwith $"Should succeed, got error: {e}"
        }
        :> Task

    [<Fact>]
    let ``train Hybrid regression should succeed`` () =
        task {
            let problem =
                { defaultRegressionProblem with
                    Architecture = Hybrid
                }

            match! trainAsync problem CancellationToken.None with
            | Ok model ->
                // Hybrid uses HHL first, falls back to classical if needed
                Assert.Equal(Regression, model.Metadata.ProblemType)
                Assert.Equal(Hybrid, model.Metadata.Architecture)
            | Error e -> failwith $"Should succeed, got error: {e}"
        }
        :> Task

    [<Fact>]
    let ``train Classical regression should succeed`` () =
        task {
            let problem =
                { defaultRegressionProblem with
                    Architecture = Classical
                }

            match! trainAsync problem CancellationToken.None with
            | Ok model ->
                Assert.Equal(Regression, model.Metadata.ProblemType)
                Assert.Equal(Classical, model.Metadata.Architecture)
            | Error e -> failwith $"Should succeed, got error: {e}"
        }
        :> Task

    [<Fact>]
    let ``train Hybrid regression runs on the quantum backend and yields a valid R2`` () =
        task {
            // Linear data y = 2*x1 + 3*x2 + 1. Regression now runs entirely on the quantum backend:
            // HHL (QuantumRegressionHHL) for the linear fit, with a VQC fallback for non-linear data
            // — no classical solver (RULE1). Quantum regression on a simulator is approximate, so we
            // assert a valid finite R² rather than classical-level accuracy.
            let problem =
                { defaultRegressionProblem with
                    Architecture = Hybrid
                }

            match! trainAsync problem CancellationToken.None with
            | Ok model ->
                Assert.Equal(Regression, model.Metadata.ProblemType)
                Assert.False(Double.IsNaN model.Metadata.TrainingScore, "R² should be a finite number")

                Assert.True(
                    model.Metadata.TrainingScore <= 1.0,
                    $"R² should be ≤ 1, got {model.Metadata.TrainingScore}"
                )
            | Error e -> failwith $"Should succeed, got error: {e}"
        }
        :> Task

    // ========================================================================
    // MULTI-CLASS TRAINING TESTS
    // ========================================================================

    [<Fact>]
    let ``train Quantum multi-class should succeed`` () =
        task {
            match! trainAsync defaultMultiClassProblem CancellationToken.None with
            | Ok model ->
                Assert.Equal(MultiClass 3, model.Metadata.ProblemType)
                Assert.Equal(Quantum, model.Metadata.Architecture)
                Assert.Equal(2, model.Metadata.NumFeatures)
                Assert.Equal(12, model.Metadata.NumSamples)
            | Error e -> failwith $"Should succeed, got error: {e}"
        }
        :> Task

    [<Fact>]
    let ``train Hybrid multi-class should succeed`` () =
        task {
            let problem =
                { defaultMultiClassProblem with
                    Architecture = Hybrid
                }

            match! trainAsync problem CancellationToken.None with
            | Ok model ->
                Assert.Equal(MultiClass 3, model.Metadata.ProblemType)
                Assert.Equal(Hybrid, model.Metadata.Architecture)
            | Error e -> failwith $"Should succeed, got error: {e}"
        }
        :> Task

    [<Fact>]
    let ``train Classical multi-class should succeed`` () =
        task {
            let problem =
                { defaultMultiClassProblem with
                    Architecture = Classical
                }

            match! trainAsync problem CancellationToken.None with
            | Ok model ->
                Assert.Equal(MultiClass 3, model.Metadata.ProblemType)
                Assert.Equal(Classical, model.Metadata.Architecture)

                Assert.True(
                    model.Metadata.TrainingScore >= 0.0 && model.Metadata.TrainingScore <= 1.0,
                    $"Accuracy should be in [0,1], got {model.Metadata.TrainingScore}"
                )
            | Error e -> failwith $"Should succeed, got error: {e}"
        }
        :> Task

    // ========================================================================
    // PREDICTION TESTS - REGRESSION
    // ========================================================================

    [<Fact>]
    let ``predict regression after training should return value`` () =
        task {
            let problem =
                { defaultRegressionProblem with
                    Architecture = Hybrid
                }

            match! trainAsync problem CancellationToken.None with
            | Ok model ->
                match! predictAsync [| 1.0; 1.0 |] model None None CancellationToken.None with
                | Ok pred ->
                    Assert.True(Double.IsFinite pred.Value, $"Prediction value should be finite, got {pred.Value}")
                    Assert.True(pred.ModelType.Length > 0, "ModelType should not be empty")
                | Error e -> failwith $"predict should succeed, got error: {e}"
            | Error e -> failwith $"train should succeed, got error: {e}"
        }
        :> Task

    [<Fact>]
    let ``predict regression on multi-class model should return error`` () =
        task {
            match! trainAsync defaultMultiClassProblem CancellationToken.None with
            | Ok model ->
                match! predictAsync [| 0.1; 0.1 |] model None None CancellationToken.None with
                | Error(QuantumError.Other msg) -> Assert.Contains("predictCategory", msg)
                | other -> failwith $"Expected error directing to predictCategory, got {other}"
            | Error e -> failwith $"train should succeed, got error: {e}"
        }
        :> Task

    // ========================================================================
    // PREDICTION TESTS - MULTI-CLASS
    // ========================================================================

    [<Fact>]
    let ``predictCategory after training should return valid category`` () =
        task {
            match! trainAsync defaultMultiClassProblem CancellationToken.None with
            | Ok model ->
                match! predictCategoryAsync [| 0.1; 0.1 |] model None None CancellationToken.None with
                | Ok pred ->
                    Assert.True(
                        pred.Category >= 0 && pred.Category < 3,
                        $"Category should be 0, 1, or 2, got {pred.Category}"
                    )

                    Assert.True(
                        pred.Confidence >= 0.0 && pred.Confidence <= 1.0,
                        $"Confidence should be in [0,1], got {pred.Confidence}"
                    )

                    Assert.Equal(3, pred.Probabilities.Length)
                    Assert.True(pred.ModelType.Length > 0)
                | Error e -> failwith $"predictCategory should succeed, got error: {e}"
            | Error e -> failwith $"train should succeed, got error: {e}"
        }
        :> Task

    [<Fact>]
    let ``predictCategory on regression model should return error`` () =
        task {
            let problem =
                { defaultRegressionProblem with
                    Architecture = Hybrid
                }

            match! trainAsync problem CancellationToken.None with
            | Ok model ->
                match! predictCategoryAsync [| 1.0; 1.0 |] model None None CancellationToken.None with
                | Error(QuantumError.Other msg) -> Assert.Contains("predict", msg.ToLower())
                | other -> failwith $"Expected error directing to predict, got {other}"
            | Error e -> failwith $"train should succeed, got error: {e}"
        }
        :> Task

    [<Fact>]
    let ``predictCategory with Classical multi-class should return valid category`` () =
        task {
            let problem =
                { defaultMultiClassProblem with
                    Architecture = Classical
                }

            match! trainAsync problem CancellationToken.None with
            | Ok model ->
                match! predictCategoryAsync [| 0.5; 0.9 |] model None None CancellationToken.None with
                | Ok pred ->
                    Assert.True(pred.Category >= 0 && pred.Category < 3)
                    Assert.Equal(3, pred.Probabilities.Length)
                    // Probabilities should sum to ~1.0 (softmax)
                    let sumProb = pred.Probabilities |> Array.sum
                    Assert.True(abs (sumProb - 1.0) < 0.01, $"Probabilities should sum to ~1.0, got {sumProb}")
                | Error e -> failwith $"predictCategory should succeed, got error: {e}"
            | Error e -> failwith $"train should succeed, got error: {e}"
        }
        :> Task

    // ========================================================================
    // EVALUATION TESTS - REGRESSION
    // ========================================================================

    [<Fact>]
    let ``evaluateRegression should return valid metrics`` () =
        task {
            let features, targets = makeRegressionData ()

            let problem =
                { defaultRegressionProblem with
                    Architecture = Hybrid
                }

            match! trainAsync problem CancellationToken.None with
            | Ok model ->
                let! evaluation =
                    evaluateRegressionAsync features targets model CancellationToken.None

                match evaluation with
                | Ok metrics ->
                    Assert.True(Double.IsFinite metrics.RSquared, $"R^2 should be finite, got {metrics.RSquared}")
                    Assert.True(metrics.MAE >= 0.0, $"MAE should be non-negative, got {metrics.MAE}")
                    Assert.True(metrics.MSE >= 0.0, $"MSE should be non-negative, got {metrics.MSE}")
                    Assert.True(metrics.RMSE >= 0.0, $"RMSE should be non-negative, got {metrics.RMSE}")
                    // RMSE should be sqrt of MSE
                    Assert.True(
                        abs (metrics.RMSE - sqrt metrics.MSE) < 1e-10,
                        $"RMSE ({metrics.RMSE}) should equal sqrt(MSE) ({sqrt metrics.MSE})"
                    )
                | Error e -> failwith $"evaluateRegression should succeed, got error: {e}"
            | Error e -> failwith $"train should succeed, got error: {e}"
        }
        :> Task

    [<Fact>]
    let ``evaluateRegression on multi-class model should return error`` () =
        task {
            match! trainAsync defaultMultiClassProblem CancellationToken.None with
            | Ok model ->
                let features, targets = makeRegressionData ()

                let! evaluation =
                    evaluateRegressionAsync features targets model CancellationToken.None

                match evaluation with
                | Error(QuantumError.Other msg) -> Assert.Contains("evaluateMultiClass", msg)
                | other -> failwith $"Expected error directing to evaluateMultiClass, got {other}"
            | Error e -> failwith $"train should succeed, got error: {e}"
        }
        :> Task

    // ========================================================================
    // EVALUATION TESTS - MULTI-CLASS
    // ========================================================================

    [<Fact; Trait("Category", "Slow")>]
    let ``evaluateMultiClass should return valid metrics`` () =
        task {
            let features, targets = makeMultiClassData ()
            let intLabels = targets |> Array.map int

            match! trainAsync defaultMultiClassProblem CancellationToken.None with
            | Ok model ->
                match! evaluateMultiClassAsync features intLabels model CancellationToken.None with
                | Ok metrics ->
                    Assert.True(
                        metrics.Accuracy >= 0.0 && metrics.Accuracy <= 1.0,
                        $"Accuracy should be in [0,1], got {metrics.Accuracy}"
                    )

                    Assert.Equal(3, metrics.Precision.Length)
                    Assert.Equal(3, metrics.Recall.Length)
                    Assert.Equal(3, metrics.F1Score.Length)
                    Assert.Equal(3, metrics.ConfusionMatrix.Length)
                    // Each row of confusion matrix should have 3 columns
                    metrics.ConfusionMatrix |> Array.iter (fun row -> Assert.Equal(3, row.Length))
                | Error e -> failwith $"evaluateMultiClass should succeed, got error: {e}"
            | Error e -> failwith $"train should succeed, got error: {e}"
        }
        :> Task

    [<Fact>]
    let ``evaluateMultiClass on regression model should return error`` () =
        task {
            let problem =
                { defaultRegressionProblem with
                    Architecture = Hybrid
                }

            match! trainAsync problem CancellationToken.None with
            | Ok model ->
                let! evaluation =
                    evaluateMultiClassAsync [| [| 1.0; 1.0 |] |] [| 0 |] model CancellationToken.None

                match evaluation with
                | Error(QuantumError.Other msg) -> Assert.Contains("evaluateRegression", msg)
                | other -> failwith $"Expected error directing to evaluateRegression, got {other}"
            | Error e -> failwith $"train should succeed, got error: {e}"
        }
        :> Task

    // ========================================================================
    // METADATA TESTS
    // ========================================================================

    [<Fact>]
    let ``trained model metadata should have correct values`` () =
        task {
            match! trainAsync defaultRegressionProblem CancellationToken.None with
            | Ok model ->
                Assert.Equal(Regression, model.Metadata.ProblemType)
                Assert.Equal(2, model.Metadata.NumFeatures)
                Assert.Equal(10, model.Metadata.NumSamples)
                Assert.True(model.Metadata.CreatedAt <= DateTime.UtcNow)
                Assert.True(model.Metadata.TrainingTime >= TimeSpan.Zero)
            | Error e -> failwith $"Should succeed, got error: {e}"
        }
        :> Task

    [<Fact>]
    let ``trained model with note should preserve note`` () =
        task {
            let problem =
                { defaultRegressionProblem with
                    Architecture = Hybrid
                    Note = Some "Revenue prediction model v1"
                }

            let! trained = trainAsync problem CancellationToken.None

            trained
            |> Result.map (fun model -> Assert.Equal(Some "Revenue prediction model v1", model.Metadata.Note))
            |> Result.defaultWith (fun e -> failwith $"Should succeed, got error: {e}")
        }
        :> Task

    // ========================================================================
    // COMPUTATION EXPRESSION TESTS
    // ========================================================================

    [<Fact>]
    let ``CE predictiveModel with regression should succeed`` () =
        task {
            let features, targets = makeRegressionData ()

            let! result =
                predictiveModel {
                    trainWith features targets
                    problemType Regression
                    architecture Hybrid
                    maxEpochs 5
                    shots 100
                }

            match result with
            | Ok model ->
                Assert.Equal(Regression, model.Metadata.ProblemType)
                Assert.Equal(Hybrid, model.Metadata.Architecture)
            | Error e -> failwith $"CE should succeed, got error: {e}"
        }
        :> Task

    [<Fact>]
    let ``CE predictiveModel with multi-class should succeed`` () =
        task {
            let features, targets = makeMultiClassData ()

            let! result =
                predictiveModel {
                    trainWith features targets
                    problemType (MultiClass 3)
                    maxEpochs 5
                    shots 100
                }

            result
            |> Result.map (fun model -> Assert.Equal(MultiClass 3, model.Metadata.ProblemType))
            |> Result.defaultWith (fun e -> failwith $"CE should succeed, got error: {e}")
        }
        :> Task

    [<Fact>]
    let ``CE predictiveModel with explicit backend should succeed`` () =
        task {
            let features, targets = makeRegressionData ()
            let quantumBackend = LocalBackend.LocalBackend() :> IQuantumBackend

            let! result =
                predictiveModel {
                    trainWith features targets
                    problemType Regression
                    architecture Hybrid
                    backend quantumBackend
                    maxEpochs 5
                    shots 100
                }

            result
            |> Result.map (fun model -> Assert.Equal(Regression, model.Metadata.ProblemType))
            |> Result.defaultWith (fun e -> failwith $"CE should succeed, got error: {e}")
        }
        :> Task

    [<Fact>]
    let ``CE predictiveModel with empty data should return ValidationError`` () =
        task {
            match! predictiveModel { trainWith [||] [||] } with
            | Error(QuantumError.ValidationError _) -> ()
            | Ok _ -> failwith "Should return error for empty data"
            | Error e -> failwith $"Expected ValidationError, got: {e}"
        }
        :> Task

    [<Fact>]
    let ``CE predictiveModel with all options should succeed`` () =
        task {
            let features, targets = makeRegressionData ()

            let! result =
                predictiveModel {
                    trainWith features targets
                    problemType Regression
                    architecture Hybrid
                    learningRate 0.05
                    maxEpochs 10
                    convergenceThreshold 0.01
                    shots 200
                    note "Full options test"
                }

            result
            |> Result.map (fun model -> Assert.Equal(Some "Full options test", model.Metadata.Note))
            |> Result.defaultWith (fun e -> failwith $"CE should succeed, got error: {e}")
        }
        :> Task

    [<Fact>]
    let ``CE predictiveModel defaults to Regression and Quantum`` () =
        task {
            let features, targets = makeRegressionData ()

            let! result =
                predictiveModel {
                    trainWith features targets
                    maxEpochs 5
                    shots 100
                }

            match result with
            | Ok model ->
                Assert.Equal(Regression, model.Metadata.ProblemType)
                Assert.Equal(Quantum, model.Metadata.Architecture)
            | Error e -> failwith $"CE should succeed, got error: {e}"
        }
        :> Task
