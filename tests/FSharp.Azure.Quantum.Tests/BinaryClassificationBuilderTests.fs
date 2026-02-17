namespace FSharp.Azure.Quantum.Tests

open System
open Xunit
open FSharp.Azure.Quantum
open FSharp.Azure.Quantum.Core
open FSharp.Azure.Quantum.Business
open FSharp.Azure.Quantum.Backends
open FSharp.Azure.Quantum.Core.BackendAbstraction

module BinaryClassificationBuilderTests =

    open BinaryClassifier

    // ========================================================================
    // HELPERS
    // ========================================================================

    /// Simple linearly separable 2D dataset (XOR-like clusters)
    let private makeTrainData () =
        let features = [|
            [| 0.1; 0.1 |]  // class 0
            [| 0.2; 0.15 |] // class 0
            [| 0.15; 0.2 |] // class 0
            [| 0.05; 0.1 |] // class 0
            [| 0.1; 0.05 |] // class 0
            [| 0.9; 0.9 |]  // class 1
            [| 0.8; 0.85 |] // class 1
            [| 0.85; 0.8 |] // class 1
            [| 0.95; 0.9 |] // class 1
            [| 0.9; 0.95 |] // class 1
        |]
        let labels = [| 0; 0; 0; 0; 0; 1; 1; 1; 1; 1 |]
        features, labels

    // ========================================================================
    // VALIDATION TESTS
    // ========================================================================

    [<Fact>]
    let ``train with empty features should return ValidationError`` () =
        let problem = {
            TrainFeatures = [||]
            TrainLabels = [| 0; 1 |]
            Architecture = Quantum
            LearningRate = 0.01
            MaxEpochs = 10
            ConvergenceThreshold = 0.001
            Backend = None
            Shots = 100
            Verbose = false
            SavePath = None
            Note = None
            ProgressReporter = None
            CancellationToken = None
            Logger = None
        }
        match train problem with
        | Error (QuantumError.ValidationError (param, _)) ->
            Assert.Equal("Input", param)
        | _ -> failwith "Should return ValidationError for empty features"

    [<Fact>]
    let ``train with empty labels should return ValidationError`` () =
        let features, _ = makeTrainData()
        let problem = {
            TrainFeatures = features
            TrainLabels = [||]
            Architecture = Quantum
            LearningRate = 0.01
            MaxEpochs = 10
            ConvergenceThreshold = 0.001
            Backend = None
            Shots = 100
            Verbose = false
            SavePath = None
            Note = None
            ProgressReporter = None
            CancellationToken = None
            Logger = None
        }
        match train problem with
        | Error (QuantumError.ValidationError (param, _)) ->
            Assert.Equal("Input", param)
        | _ -> failwith "Should return ValidationError for empty labels"

    [<Fact>]
    let ``train with mismatched features and labels should return ValidationError`` () =
        let features, _ = makeTrainData()
        let problem = {
            TrainFeatures = features
            TrainLabels = [| 0; 1 |] // only 2 labels for 10 features
            Architecture = Quantum
            LearningRate = 0.01
            MaxEpochs = 10
            ConvergenceThreshold = 0.001
            Backend = None
            Shots = 100
            Verbose = false
            SavePath = None
            Note = None
            ProgressReporter = None
            CancellationToken = None
            Logger = None
        }
        match train problem with
        | Error (QuantumError.ValidationError (param, msg)) ->
            Assert.Equal("Input", param)
            Assert.Contains("same length", msg)
        | _ -> failwith "Should return ValidationError for mismatched lengths"

    [<Fact>]
    let ``train with invalid labels should return ValidationError`` () =
        let features, _ = makeTrainData()
        let problem = {
            TrainFeatures = features
            TrainLabels = [| 0; 1; 2; 0; 1; 0; 1; 0; 1; 0 |] // 2 is invalid
            Architecture = Quantum
            LearningRate = 0.01
            MaxEpochs = 10
            ConvergenceThreshold = 0.001
            Backend = None
            Shots = 100
            Verbose = false
            SavePath = None
            Note = None
            ProgressReporter = None
            CancellationToken = None
            Logger = None
        }
        match train problem with
        | Error (QuantumError.ValidationError (param, msg)) ->
            Assert.Equal("Input", param)
            Assert.Contains("0 or 1", msg)
        | _ -> failwith "Should return ValidationError for invalid labels"

    [<Fact>]
    let ``train with non-positive learning rate should return ValidationError`` () =
        let features, labels = makeTrainData()
        let problem = {
            TrainFeatures = features
            TrainLabels = labels
            Architecture = Quantum
            LearningRate = 0.0
            MaxEpochs = 10
            ConvergenceThreshold = 0.001
            Backend = None
            Shots = 100
            Verbose = false
            SavePath = None
            Note = None
            ProgressReporter = None
            CancellationToken = None
            Logger = None
        }
        match train problem with
        | Error (QuantumError.ValidationError (param, _)) ->
            Assert.Equal("Input", param)
        | _ -> failwith "Should return ValidationError for zero learning rate"

    [<Fact>]
    let ``train with zero epochs should return ValidationError`` () =
        let features, labels = makeTrainData()
        let problem = {
            TrainFeatures = features
            TrainLabels = labels
            Architecture = Quantum
            LearningRate = 0.01
            MaxEpochs = 0
            ConvergenceThreshold = 0.001
            Backend = None
            Shots = 100
            Verbose = false
            SavePath = None
            Note = None
            ProgressReporter = None
            CancellationToken = None
            Logger = None
        }
        match train problem with
        | Error (QuantumError.ValidationError (param, _)) ->
            Assert.Equal("Input", param)
        | _ -> failwith "Should return ValidationError for zero epochs"

    [<Fact>]
    let ``train with Classical architecture should return NotImplemented`` () =
        let features, labels = makeTrainData()
        let problem = {
            TrainFeatures = features
            TrainLabels = labels
            Architecture = Classical
            LearningRate = 0.01
            MaxEpochs = 10
            ConvergenceThreshold = 0.001
            Backend = None
            Shots = 100
            Verbose = false
            SavePath = None
            Note = None
            ProgressReporter = None
            CancellationToken = None
            Logger = None
        }
        match train problem with
        | Error (QuantumError.NotImplemented _) -> ()
        | _ -> failwith "Should return NotImplemented for Classical architecture"

    // ========================================================================
    // SUCCESSFUL TRAINING TESTS
    // ========================================================================

    [<Fact>]
    let ``train with Quantum architecture should succeed`` () =
        let features, labels = makeTrainData()
        let problem = {
            TrainFeatures = features
            TrainLabels = labels
            Architecture = Quantum
            LearningRate = 0.01
            MaxEpochs = 5
            ConvergenceThreshold = 0.001
            Backend = None
            Shots = 100
            Verbose = false
            SavePath = None
            Note = None
            ProgressReporter = None
            CancellationToken = None
            Logger = None
        }
        match train problem with
        | Ok classifier ->
            Assert.Equal(Quantum, classifier.Metadata.Architecture)
            Assert.Equal(2, classifier.Metadata.NumFeatures)
            Assert.Equal(10, classifier.Metadata.NumSamples)
            Assert.True(classifier.Metadata.TrainingAccuracy >= 0.0 && classifier.Metadata.TrainingAccuracy <= 1.0)
        | Error e -> failwith $"Should succeed, got error: {e}"

    [<Fact>]
    let ``train with Hybrid architecture should succeed`` () =
        let features, labels = makeTrainData()
        let problem = {
            TrainFeatures = features
            TrainLabels = labels
            Architecture = Hybrid
            LearningRate = 0.01
            MaxEpochs = 10
            ConvergenceThreshold = 0.001
            Backend = None
            Shots = 100
            Verbose = false
            SavePath = None
            Note = None
            ProgressReporter = None
            CancellationToken = None
            Logger = None
        }
        match train problem with
        | Ok classifier ->
            Assert.Equal(Hybrid, classifier.Metadata.Architecture)
            Assert.Equal(10, classifier.Metadata.NumSamples)
        | Error e -> failwith $"Should succeed, got error: {e}"

    // ========================================================================
    // PREDICTION TESTS
    // ========================================================================

    [<Fact>]
    let ``predict after training should return valid prediction`` () =
        let features, labels = makeTrainData()
        let problem = {
            TrainFeatures = features
            TrainLabels = labels
            Architecture = Hybrid
            LearningRate = 0.01
            MaxEpochs = 10
            ConvergenceThreshold = 0.001
            Backend = None
            Shots = 100
            Verbose = false
            SavePath = None
            Note = None
            ProgressReporter = None
            CancellationToken = None
            Logger = None
        }
        match train problem with
        | Ok classifier ->
            let sample = [| 0.1; 0.1 |]
            match predict sample classifier with
            | Ok pred ->
                Assert.True(pred.Label = 0 || pred.Label = 1, $"Label should be 0 or 1, got {pred.Label}")
                Assert.True(pred.Confidence >= 0.0 && pred.Confidence <= 1.0,
                    $"Confidence should be in [0,1], got {pred.Confidence}")
                Assert.Equal(pred.Label = 1, pred.IsPositive)
                Assert.Equal(pred.Label = 0, pred.IsNegative)
            | Error e -> failwith $"predict should succeed, got error: {e}"
        | Error e -> failwith $"train should succeed, got error: {e}"

    // ========================================================================
    // EVALUATION TESTS
    // ========================================================================

    [<Fact>]
    let ``evaluate should return metrics with valid ranges`` () =
        let features, labels = makeTrainData()
        let problem = {
            TrainFeatures = features
            TrainLabels = labels
            Architecture = Hybrid
            LearningRate = 0.01
            MaxEpochs = 10
            ConvergenceThreshold = 0.001
            Backend = None
            Shots = 100
            Verbose = false
            SavePath = None
            Note = None
            ProgressReporter = None
            CancellationToken = None
            Logger = None
        }
        match train problem with
        | Ok classifier ->
            match evaluate features labels classifier with
            | Ok metrics ->
                Assert.True(metrics.Accuracy >= 0.0 && metrics.Accuracy <= 1.0)
                Assert.True(metrics.Precision >= 0.0 && metrics.Precision <= 1.0)
                Assert.True(metrics.Recall >= 0.0 && metrics.Recall <= 1.0)
                Assert.True(metrics.F1Score >= 0.0 && metrics.F1Score <= 1.0)
                Assert.Equal(labels.Length, metrics.TruePositives + metrics.TrueNegatives + metrics.FalsePositives + metrics.FalseNegatives)
            | Error e -> failwith $"evaluate should succeed, got error: {e}"
        | Error e -> failwith $"train should succeed, got error: {e}"

    [<Fact>]
    let ``evaluate with mismatched test data should return ValidationError`` () =
        let features, labels = makeTrainData()
        let problem = {
            TrainFeatures = features
            TrainLabels = labels
            Architecture = Hybrid
            LearningRate = 0.01
            MaxEpochs = 10
            ConvergenceThreshold = 0.001
            Backend = None
            Shots = 100
            Verbose = false
            SavePath = None
            Note = None
            ProgressReporter = None
            CancellationToken = None
            Logger = None
        }
        match train problem with
        | Ok classifier ->
            match evaluate features [| 0; 1 |] classifier with // wrong label count
            | Error (QuantumError.ValidationError _) -> ()
            | _ -> failwith "Should return ValidationError for mismatched test data"
        | Error e -> failwith $"train should succeed, got error: {e}"

    // ========================================================================
    // CE BUILDER TESTS
    // ========================================================================

    [<Fact>]
    let ``binaryClassification CE should train classifier`` () =
        let features, labels = makeTrainData()
        let result = binaryClassification {
            trainWith features labels
            maxEpochs 5
            shots 100
        }
        match result with
        | Ok classifier ->
            Assert.Equal(Quantum, classifier.Metadata.Architecture) // default
            Assert.Equal(10, classifier.Metadata.NumSamples)
        | Error e -> failwith $"CE should succeed, got error: {e}"

    [<Fact>]
    let ``binaryClassification CE with architecture should work`` () =
        let features, labels = makeTrainData()
        let result = binaryClassification {
            trainWith features labels
            architecture Hybrid
            maxEpochs 5
            shots 100
        }
        match result with
        | Ok classifier ->
            Assert.Equal(Hybrid, classifier.Metadata.Architecture)
        | Error e -> failwith $"CE should succeed, got error: {e}"

    [<Fact>]
    let ``binaryClassification CE with backend should work`` () =
        let features, labels = makeTrainData()
        let quantumBackend = LocalBackend.LocalBackend() :> IQuantumBackend
        let result = binaryClassification {
            trainWith features labels
            architecture Hybrid
            backend quantumBackend
            maxEpochs 5
            shots 100
        }
        match result with
        | Ok classifier ->
            Assert.Equal(Hybrid, classifier.Metadata.Architecture)
        | Error e -> failwith $"CE should succeed, got error: {e}"

    [<Fact>]
    let ``binaryClassification CE with Classical should return NotImplemented`` () =
        let features, labels = makeTrainData()
        let result = binaryClassification {
            trainWith features labels
            architecture Classical
        }
        match result with
        | Error (QuantumError.NotImplemented _) -> ()
        | _ -> failwith "Should return NotImplemented for Classical"

    [<Fact>]
    let ``binaryClassification CE with empty data should return ValidationError`` () =
        let result = binaryClassification {
            trainWith [||] [||]
        }
        match result with
        | Error (QuantumError.ValidationError _) -> ()
        | Ok _ -> failwith "Should return error for empty data"
        | Error e -> failwith $"Expected ValidationError, got: {e}"

    [<Fact>]
    let ``binaryClassification CE with learning rate and convergence should work`` () =
        let features, labels = makeTrainData()
        let result = binaryClassification {
            trainWith features labels
            architecture Hybrid
            learningRate 0.05
            convergenceThreshold 0.01
            maxEpochs 5
            shots 100
        }
        match result with
        | Ok _ -> ()
        | Error e -> failwith $"CE should succeed, got error: {e}"
