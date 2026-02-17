namespace FSharp.Azure.Quantum.Tests

open System
open Xunit
open FSharp.Azure.Quantum
open FSharp.Azure.Quantum.Core
open FSharp.Azure.Quantum.Business
open FSharp.Azure.Quantum.Backends
open FSharp.Azure.Quantum.Core.BackendAbstraction

module AnomalyDetectionBuilderTests =

    open AnomalyDetector

    // ========================================================================
    // HELPERS
    // ========================================================================

    /// Generate synthetic "normal" data: clusters around (1,1) and (2,2) with small noise
    let private generateNormalData (n: int) (seed: int) =
        let rng = Random(seed)
        [| for _ in 1..n ->
            let cluster = rng.Next(2)
            let cx, cy = if cluster = 0 then (1.0, 1.0) else (2.0, 2.0)
            [| cx + (rng.NextDouble() - 0.5) * 0.3
               cy + (rng.NextDouble() - 0.5) * 0.3 |]
        |]

    let private defaultNormalData = generateNormalData 15 42

    // ========================================================================
    // VALIDATION TESTS
    // ========================================================================

    [<Fact>]
    let ``train with empty data should return ValidationError`` () =
        let problem = {
            NormalData = [||]
            Sensitivity = Medium
            ContaminationRate = 0.05
            Backend = None
            Shots = 1000
            Verbose = false
            Logger = None
            SavePath = None
            Note = None
            ProgressReporter = None
            CancellationToken = None
        }
        match train problem with
        | Error (QuantumError.ValidationError (param, _)) ->
            Assert.Equal("Input", param)
        | _ -> failwith "Should return ValidationError for empty data"

    [<Fact>]
    let ``train with fewer than 10 samples should return error`` () =
        let problem = {
            NormalData = generateNormalData 5 42
            Sensitivity = Medium
            ContaminationRate = 0.05
            Backend = None
            Shots = 1000
            Verbose = false
            Logger = None
            SavePath = None
            Note = None
            ProgressReporter = None
            CancellationToken = None
        }
        match train problem with
        | Error _ -> () // Expected: either ValidationError or Other
        | Ok _ -> failwith "Should return error for fewer than 10 samples"

    [<Fact>]
    let ``train with contamination rate out of range should return ValidationError`` () =
        let problem = {
            NormalData = defaultNormalData
            Sensitivity = Medium
            ContaminationRate = 0.8 // > 0.5
            Backend = None
            Shots = 1000
            Verbose = false
            Logger = None
            SavePath = None
            Note = None
            ProgressReporter = None
            CancellationToken = None
        }
        match train problem with
        | Error (QuantumError.ValidationError (param, _)) ->
            Assert.Equal("Input", param)
        | _ -> failwith "Should return ValidationError for contamination rate > 0.5"

    [<Fact>]
    let ``train with negative contamination rate should return ValidationError`` () =
        let problem = {
            NormalData = defaultNormalData
            Sensitivity = Medium
            ContaminationRate = -0.1
            Backend = None
            Shots = 1000
            Verbose = false
            Logger = None
            SavePath = None
            Note = None
            ProgressReporter = None
            CancellationToken = None
        }
        match train problem with
        | Error (QuantumError.ValidationError (param, _)) ->
            Assert.Equal("Input", param)
        | _ -> failwith "Should return ValidationError for negative contamination rate"

    [<Fact>]
    let ``train with mismatched feature lengths should return ValidationError`` () =
        let badData = [|
            [| 1.0; 2.0 |]
            [| 1.0; 2.0; 3.0 |] // different length
            [| 1.0; 2.0 |]
            [| 1.0; 2.0 |]
            [| 1.0; 2.0 |]
            [| 1.0; 2.0 |]
            [| 1.0; 2.0 |]
            [| 1.0; 2.0 |]
            [| 1.0; 2.0 |]
            [| 1.0; 2.0 |]
        |]
        let problem = {
            NormalData = badData
            Sensitivity = Medium
            ContaminationRate = 0.05
            Backend = None
            Shots = 1000
            Verbose = false
            Logger = None
            SavePath = None
            Note = None
            ProgressReporter = None
            CancellationToken = None
        }
        match train problem with
        | Error (QuantumError.ValidationError (param, msg)) ->
            Assert.Equal("Input", param)
            Assert.Contains("same length", msg)
        | _ -> failwith "Should return ValidationError for mismatched feature lengths"

    [<Fact>]
    let ``train with zero shots should return ValidationError`` () =
        let problem = {
            NormalData = defaultNormalData
            Sensitivity = Medium
            ContaminationRate = 0.05
            Backend = None
            Shots = 0
            Verbose = false
            Logger = None
            SavePath = None
            Note = None
            ProgressReporter = None
            CancellationToken = None
        }
        match train problem with
        | Error (QuantumError.ValidationError (param, _)) ->
            Assert.Equal("Input", param)
        | _ -> failwith "Should return ValidationError for zero shots"

    // ========================================================================
    // SUCCESSFUL TRAINING TESTS
    // ========================================================================

    [<Fact>]
    let ``train with valid normal data should succeed`` () =
        let problem = {
            NormalData = defaultNormalData
            Sensitivity = Medium
            ContaminationRate = 0.05
            Backend = None
            Shots = 100
            Verbose = false
            Logger = None
            SavePath = None
            Note = None
            ProgressReporter = None
            CancellationToken = None
        }
        match train problem with
        | Ok detector ->
            Assert.Equal(Medium, detector.Metadata.Sensitivity)
            Assert.Equal(2, detector.Metadata.NumFeatures)
            Assert.Equal(15, detector.Metadata.NumNormalSamples)
        | Error e -> failwith $"Should succeed, got error: {e}"

    [<Fact>]
    let ``train with explicit backend should succeed`` () =
        let quantumBackend = LocalBackend.LocalBackend() :> IQuantumBackend
        let problem = {
            NormalData = defaultNormalData
            Sensitivity = High
            ContaminationRate = 0.0
            Backend = Some quantumBackend
            Shots = 100
            Verbose = false
            Logger = None
            SavePath = None
            Note = Some "test detector"
            ProgressReporter = None
            CancellationToken = None
        }
        match train problem with
        | Ok detector ->
            Assert.Equal(High, detector.Metadata.Sensitivity)
            Assert.Equal(Some "test detector", detector.Metadata.Note)
        | Error e -> failwith $"Should succeed, got error: {e}"

    [<Fact>]
    let ``train with different sensitivity levels should succeed`` () =
        let makeProblem sensitivity = {
            NormalData = defaultNormalData
            Sensitivity = sensitivity
            ContaminationRate = 0.05
            Backend = None
            Shots = 100
            Verbose = false
            Logger = None
            SavePath = None
            Note = None
            ProgressReporter = None
            CancellationToken = None
        }
        for s in [Low; Medium; High; VeryHigh] do
            match train (makeProblem s) with
            | Ok detector -> Assert.Equal(s, detector.Metadata.Sensitivity)
            | Error e -> failwith $"Should succeed for sensitivity {s}, got error: {e}"

    // ========================================================================
    // DETECTION TESTS
    // ========================================================================

    [<Fact>]
    let ``check on normal-like sample should return result`` () =
        let problem = {
            NormalData = defaultNormalData
            Sensitivity = Medium
            ContaminationRate = 0.05
            Backend = None
            Shots = 100
            Verbose = false
            Logger = None
            SavePath = None
            Note = None
            ProgressReporter = None
            CancellationToken = None
        }
        match train problem with
        | Ok detector ->
            let sample = [| 1.1; 1.1 |] // Close to cluster center
            match check sample detector with
            | Ok result ->
                Assert.True(result.AnomalyScore >= 0.0 && result.AnomalyScore <= 1.0,
                    $"AnomalyScore should be in [0,1], got {result.AnomalyScore}")
                Assert.True(result.Confidence >= 0.0,
                    $"Confidence should be non-negative, got {result.Confidence}")
                Assert.Equal(not result.IsAnomaly, result.IsNormal)
            | Error e -> failwith $"check should succeed, got error: {e}"
        | Error e -> failwith $"train should succeed, got error: {e}"

    [<Fact>]
    let ``checkBatch should return results for all samples`` () =
        let problem = {
            NormalData = defaultNormalData
            Sensitivity = Medium
            ContaminationRate = 0.05
            Backend = None
            Shots = 100
            Verbose = false
            Logger = None
            SavePath = None
            Note = None
            ProgressReporter = None
            CancellationToken = None
        }
        match train problem with
        | Ok detector ->
            let samples = [| [| 1.0; 1.0 |]; [| 2.0; 2.0 |]; [| 10.0; 10.0 |] |]
            match checkBatch samples detector with
            | Ok batch ->
                Assert.Equal(3, batch.TotalItems)
                Assert.Equal(3, batch.Results.Length)
                Assert.True(batch.AnomalyRate >= 0.0 && batch.AnomalyRate <= 1.0)
                Assert.True(batch.TopAnomalies.Length > 0)
            | Error e -> failwith $"checkBatch should succeed, got error: {e}"
        | Error e -> failwith $"train should succeed, got error: {e}"

    // ========================================================================
    // EXPLANATION TESTS
    // ========================================================================

    [<Fact>]
    let ``explain should return feature contributions`` () =
        let problem = {
            NormalData = defaultNormalData
            Sensitivity = Medium
            ContaminationRate = 0.05
            Backend = None
            Shots = 100
            Verbose = false
            Logger = None
            SavePath = None
            Note = None
            ProgressReporter = None
            CancellationToken = None
        }
        match train problem with
        | Ok detector ->
            let sample = [| 10.0; 10.0 |] // Far from normal
            match explain sample detector defaultNormalData with
            | Ok contributions ->
                Assert.Equal(2, contributions.Length)
                // Features should be named Feature_1, Feature_2
                Assert.True(contributions |> Array.exists (fun (name, _) -> name.StartsWith("Feature_")))
                // All deviations should be non-negative
                for (_, dev) in contributions do
                    Assert.True(dev >= 0.0, $"Deviation should be non-negative, got {dev}")
            | Error e -> failwith $"explain should succeed, got error: {e}"
        | Error e -> failwith $"train should succeed, got error: {e}"

    // ========================================================================
    // CE BUILDER TESTS
    // ========================================================================

    [<Fact>]
    let ``anomalyDetection CE should train detector`` () =
        let result = anomalyDetection {
            trainOnNormalData defaultNormalData
            sensitivity Medium
            shots 100
        }
        match result with
        | Ok detector ->
            Assert.Equal(Medium, detector.Metadata.Sensitivity)
            Assert.Equal(15, detector.Metadata.NumNormalSamples)
        | Error e -> failwith $"CE should succeed, got error: {e}"

    [<Fact>]
    let ``anomalyDetection CE with backend should succeed`` () =
        let quantumBackend = LocalBackend.LocalBackend() :> IQuantumBackend
        let result = anomalyDetection {
            trainOnNormalData defaultNormalData
            sensitivity High
            contaminationRate 0.1
            backend quantumBackend
            shots 100
        }
        match result with
        | Ok detector ->
            Assert.Equal(High, detector.Metadata.Sensitivity)
        | Error e -> failwith $"CE should succeed, got error: {e}"

    [<Fact>]
    let ``anomalyDetection CE with empty data should return error`` () =
        let result = anomalyDetection {
            trainOnNormalData [||]
            sensitivity Medium
        }
        match result with
        | Error (QuantumError.ValidationError _) -> ()
        | Ok _ -> failwith "Should return error for empty data"
        | Error e -> failwith $"Expected ValidationError, got: {e}"

    [<Fact>]
    let ``anomalyDetection CE with note should preserve it`` () =
        let result = anomalyDetection {
            trainOnNormalData defaultNormalData
            sensitivity Low
            note "fraud detection model"
            shots 100
        }
        match result with
        | Ok detector ->
            Assert.Equal(Some "fraud detection model", detector.Metadata.Note)
        | Error e -> failwith $"CE should succeed, got error: {e}"
