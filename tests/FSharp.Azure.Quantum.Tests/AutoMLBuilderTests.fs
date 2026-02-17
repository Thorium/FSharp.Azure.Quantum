namespace FSharp.Azure.Quantum.Tests

open System
open Xunit
open FSharp.Azure.Quantum
open FSharp.Azure.Quantum.Core
open FSharp.Azure.Quantum.Business
open FSharp.Azure.Quantum.Backends
open FSharp.Azure.Quantum.Core.BackendAbstraction

module AutoMLBuilderTests =
    open AutoML

    // ========================================================================
    // HELPERS
    // ========================================================================

    /// Create binary classification data: 2 linearly separable clusters
    /// Need >= 13 samples so train split (0.8) has >= 10 for anomaly detection
    let private makeBinaryData () =
        let features = [|
            // Class 0: cluster around (0, 0)
            [| 0.1; 0.2 |]
            [| 0.2; 0.1 |]
            [| 0.0; 0.3 |]
            [| 0.3; 0.0 |]
            [| 0.1; 0.1 |]
            [| 0.2; 0.2 |]
            [| 0.15; 0.15 |]
            // Class 1: cluster around (1, 1)
            [| 0.9; 0.8 |]
            [| 0.8; 0.9 |]
            [| 1.0; 0.7 |]
            [| 0.7; 1.0 |]
            [| 0.85; 0.85 |]
            [| 0.9; 0.9 |]
        |]
        let labels = [| 0.0; 0.0; 0.0; 0.0; 0.0; 0.0; 0.0; 1.0; 1.0; 1.0; 1.0; 1.0; 1.0 |]
        (features, labels)

    let private defaultProblem =
        let features, labels = makeBinaryData ()
        {
            TrainFeatures = features
            TrainLabels = labels
            TryBinaryClassification = true
            TryMultiClass = None
            TryAnomalyDetection = false
            TryRegression = false
            TrySimilaritySearch = false
            TryArchitectures = [Quantum]
            MaxTrials = 1
            MaxTimeMinutes = None
            ValidationSplit = 0.2
            Backend = None
            Verbose = false
            SavePath = None
            RandomSeed = Some 42
            ProgressReporter = None
            CancellationToken = None
            Logger = None
        }

    // ========================================================================
    // VALIDATION TESTS
    // ========================================================================

    [<Fact>]
    let ``search with empty features should return ValidationError`` () =
        let problem = { defaultProblem with TrainFeatures = [||] }
        match search problem with
        | Error (QuantumError.ValidationError ("Input", msg)) ->
            Assert.Contains("empty", msg.ToLower())
        | other -> failwith $"Expected ValidationError for empty features, got {other}"

    [<Fact>]
    let ``search with empty labels should return ValidationError`` () =
        let problem = { defaultProblem with TrainLabels = [||] }
        match search problem with
        | Error (QuantumError.ValidationError ("Input", msg)) ->
            Assert.Contains("empty", msg.ToLower())
        | other -> failwith $"Expected ValidationError for empty labels, got {other}"

    [<Fact>]
    let ``search with mismatched features and labels should return ValidationError`` () =
        let problem = { defaultProblem with TrainLabels = [| 0.0; 1.0 |] }
        match search problem with
        | Error (QuantumError.ValidationError ("Input", msg)) ->
            Assert.Contains("match", msg.ToLower())
        | other -> failwith $"Expected ValidationError for mismatched lengths, got {other}"

    [<Fact>]
    let ``search with zero-length feature vectors should return ValidationError`` () =
        let problem = { defaultProblem with
                            TrainFeatures = [| [||]; [||] |]
                            TrainLabels = [| 0.0; 1.0 |] }
        match search problem with
        | Error (QuantumError.ValidationError ("Input", msg)) ->
            Assert.Contains("at least one", msg.ToLower())
        | other -> failwith $"Expected ValidationError for zero-length features, got {other}"

    [<Fact>]
    let ``search with MaxTrials 0 should return ValidationError`` () =
        let problem = { defaultProblem with MaxTrials = 0 }
        match search problem with
        | Error (QuantumError.ValidationError ("Input", msg)) ->
            Assert.Contains("MaxTrials", msg)
        | other -> failwith $"Expected ValidationError for MaxTrials 0, got {other}"

    [<Fact>]
    let ``search with ValidationSplit 0 should return ValidationError`` () =
        let problem = { defaultProblem with ValidationSplit = 0.0 }
        match search problem with
        | Error (QuantumError.ValidationError ("Input", msg)) ->
            Assert.Contains("ValidationSplit", msg)
        | other -> failwith $"Expected ValidationError for ValidationSplit 0, got {other}"

    [<Fact>]
    let ``search with ValidationSplit 1 should return ValidationError`` () =
        let problem = { defaultProblem with ValidationSplit = 1.0 }
        match search problem with
        | Error (QuantumError.ValidationError ("Input", msg)) ->
            Assert.Contains("ValidationSplit", msg)
        | other -> failwith $"Expected ValidationError for ValidationSplit 1.0, got {other}"

    [<Fact>]
    let ``search with no model types enabled should return ValidationError`` () =
        let problem = { defaultProblem with
                            TryBinaryClassification = false
                            TryAnomalyDetection = false
                            TryRegression = false
                            TrySimilaritySearch = false }
        match search problem with
        | Error (QuantumError.ValidationError ("Input", msg)) ->
            Assert.Contains("model type", msg.ToLower())
        | other -> failwith $"Expected ValidationError for no model types, got {other}"

    [<Fact>]
    let ``search with no architectures enabled should return ValidationError`` () =
        let problem = { defaultProblem with TryArchitectures = [] }
        match search problem with
        | Error (QuantumError.ValidationError ("Input", msg)) ->
            Assert.Contains("architecture", msg.ToLower())
        | other -> failwith $"Expected ValidationError for no architectures, got {other}"

    [<Fact>]
    let ``search with inconsistent feature lengths should return ValidationError`` () =
        let problem = { defaultProblem with
                            TrainFeatures = [| [| 1.0; 2.0 |]; [| 1.0 |] |]
                            TrainLabels = [| 0.0; 1.0 |] }
        match search problem with
        | Error (QuantumError.ValidationError ("Input", msg)) ->
            Assert.Contains("same length", msg.ToLower())
        | other -> failwith $"Expected ValidationError for inconsistent features, got {other}"

    // ========================================================================
    // SUCCESSFUL SEARCH TESTS
    // ========================================================================

    [<Fact>]
    let ``search with binary classification and Quantum should succeed`` () =
        match search defaultProblem with
        | Ok result ->
            Assert.True(result.BestModelType.Length > 0, "BestModelType should not be empty")
            Assert.True(result.Score >= 0.0 && result.Score <= 1.0, $"Score should be in [0,1], got {result.Score}")
            Assert.True(result.AllTrials.Length > 0, "Should have at least one trial")
            Assert.True(result.SuccessfulTrials >= 0)
            Assert.True(result.FailedTrials >= 0)
            Assert.Equal(result.AllTrials.Length, result.SuccessfulTrials + result.FailedTrials)
            Assert.True(result.TotalSearchTime > TimeSpan.Zero)
            Assert.Equal(2, result.Metadata.NumFeatures)
            Assert.Equal(13, result.Metadata.NumSamples)
        | Error e -> failwith $"Expected Ok, got Error: {e}"

    [<Fact>]
    let ``search with Hybrid architecture should succeed`` () =
        let problem = { defaultProblem with TryArchitectures = [Hybrid] }
        match search problem with
        | Ok result ->
            Assert.True(result.SuccessfulTrials > 0 || result.FailedTrials > 0,
                "Should have at least one trial result")
        | Error e -> failwith $"Expected Ok, got Error: {e}"

    [<Fact>]
    let ``search with anomaly detection enabled should include anomaly trials`` () =
        // Need enough samples for anomaly detection (>= 10 in train split)
        let problem = { defaultProblem with
                            TryBinaryClassification = false
                            TryAnomalyDetection = true
                            MaxTrials = 1 }
        match search problem with
        | Ok result ->
            Assert.True(result.AllTrials.Length >= 1)
        | Error e -> failwith $"Expected Ok, got Error: {e}"

    // ========================================================================
    // PREDICTION TESTS
    // ========================================================================

    [<Fact>]
    let ``predict with best model should return valid prediction`` () =
        match search defaultProblem with
        | Ok result ->
            match predict [| 0.1; 0.2 |] result with
            | Ok prediction ->
                // Prediction type depends on best model
                match prediction with
                | BinaryPrediction p ->
                    Assert.True(p.Label = 0 || p.Label = 1)
                    Assert.True(p.Confidence >= 0.0 && p.Confidence <= 1.0)
                | AnomalyPrediction _ -> ()
                | _ -> ()
            | Error e -> failwith $"Expected Ok from predict, got Error: {e}"
        | Error e -> failwith $"Expected Ok from search, got Error: {e}"

    // ========================================================================
    // RESULT STRUCTURE TESTS
    // ========================================================================

    [<Fact>]
    let ``search result should have correct metadata`` () =
        match search defaultProblem with
        | Ok result ->
            Assert.Equal(2, result.Metadata.NumFeatures)
            Assert.Equal(13, result.Metadata.NumSamples)
            Assert.True(result.Metadata.CreatedAt <= DateTime.UtcNow)
            Assert.True(result.Metadata.SearchCompleted >= result.Metadata.CreatedAt)
        | Error e -> failwith $"Expected Ok, got Error: {e}"

    [<Fact>]
    let ``search result trials should have valid fields`` () =
        match search defaultProblem with
        | Ok result ->
            result.AllTrials |> Array.iter (fun trial ->
                Assert.True(trial.Id >= 0)
                Assert.True(trial.TrainingTime >= TimeSpan.Zero)
                if trial.Success then
                    Assert.True(trial.Score >= 0.0)
                    Assert.True(trial.ErrorMessage.IsNone)
                else
                    Assert.True(trial.ErrorMessage.IsSome))
        | Error e -> failwith $"Expected Ok, got Error: {e}"

    // ========================================================================
    // COMPUTATION EXPRESSION TESTS
    // ========================================================================

    [<Fact>]
    let ``CE autoML with trainWith should succeed`` () =
        let features, labels = makeBinaryData ()
        let result = autoML {
            trainWith features labels
            maxTrials 1
            tryBinaryClassification true
            tryAnomalyDetection false
            tryRegression false
            tryArchitectures [Quantum]
            randomSeed 42
        }
        match result with
        | Ok r ->
            Assert.True(r.BestModelType.Length > 0)
            Assert.True(r.Score >= 0.0)
        | Error e -> failwith $"Expected Ok, got Error: {e}"

    [<Fact>]
    let ``CE autoML with empty data should return error`` () =
        let result = autoML {
            trainWith [||] [||]
            maxTrials 1
        }
        match result with
        | Error (QuantumError.ValidationError _) -> ()
        | other -> failwith $"Expected ValidationError for empty data, got {other}"

    [<Fact>]
    let ``CE autoML with explicit backend should succeed`` () =
        let quantumBackend = LocalBackend.LocalBackend() :> IQuantumBackend
        let features, labels = makeBinaryData ()
        let result = autoML {
            trainWith features labels
            maxTrials 1
            tryBinaryClassification true
            tryAnomalyDetection false
            tryRegression false
            tryArchitectures [Quantum]
            backend quantumBackend
            randomSeed 42
        }
        match result with
        | Ok r ->
            Assert.True(r.BestModelType.Length > 0)
        | Error e -> failwith $"Expected Ok, got Error: {e}"

    [<Fact>]
    let ``CE autoML with maxTrials should limit trial count`` () =
        let features, labels = makeBinaryData ()
        let result = autoML {
            trainWith features labels
            maxTrials 2
            tryBinaryClassification true
            tryAnomalyDetection false
            tryRegression false
            tryArchitectures [Quantum]
            randomSeed 42
        }
        match result with
        | Ok r ->
            Assert.True(r.AllTrials.Length <= 2, $"Expected <= 2 trials, got {r.AllTrials.Length}")
        | Error e -> failwith $"Expected Ok, got Error: {e}"
