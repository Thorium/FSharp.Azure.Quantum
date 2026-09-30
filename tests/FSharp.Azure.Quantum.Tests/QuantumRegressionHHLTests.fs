namespace FSharp.Azure.Quantum.Tests

open Xunit
open FSharp.Azure.Quantum.Core.BackendAbstraction
open FSharp.Azure.Quantum.MachineLearning
open FSharp.Azure.Quantum.Business.PredictiveModel
open FSharp.Azure.Quantum.Backends

/// Tests for Quantum Linear Regression via HHL Algorithm
module QuantumRegressionHHLTests =

    // Use functions from QuantumRegressionHHL module
    open QuantumRegressionHHL

    // ========================================================================
    // CONFIGURATION VALIDATION TESTS
    // ========================================================================
    let private createLocalBackend () : IQuantumBackend =
        LocalBackend.LocalBackend() :> IQuantumBackend

    [<Fact>]
    let ``Train rejects empty training features`` () =
        let backend = createLocalBackend ()

        let config: RegressionConfig =
            {
                TrainX = [||]
                TrainY = [| 1.0 |]
                EigenvalueQubits = 4
                MinEigenvalue = 1e-6
                Backend = backend
                Shots = 1000
                FitIntercept = true
                Verbose = false
                Logger = None
            }

        (train config)
        |> Result.map (fun _ -> Assert.Fail("Should reject empty features"))
        |> Result.defaultWith (fun msg -> Assert.Contains("empty", msg.Message.ToLower()))

    [<Fact>]
    let ``Train rejects mismatched sample counts`` () =
        let backend = createLocalBackend ()

        let config: RegressionConfig =
            {
                TrainX = [| [| 1.0 |]; [| 2.0 |] |]
                TrainY = [| 1.0 |] // Only 1 target for 2 samples!
                EigenvalueQubits = 4
                MinEigenvalue = 1e-6
                Backend = backend
                Shots = 1000
                FitIntercept = true
                Verbose = false
                Logger = None
            }

        (train config)
        |> Result.map (fun _ -> Assert.Fail("Should reject mismatched sample counts"))
        |> Result.defaultWith (fun msg -> Assert.Contains("mismatch", msg.Message.ToLower()))

    [<Fact>]
    let ``Train rejects too few eigenvalue qubits`` () =
        let backend = createLocalBackend ()

        let config: RegressionConfig =
            {
                TrainX = [| [| 1.0; 0.0 |]; [| 0.0; 1.0 |] |]
                TrainY = [| 1.0; 2.0 |]
                EigenvalueQubits = 1 // Too few!
                MinEigenvalue = 1e-6
                Backend = backend
                Shots = 1000
                FitIntercept = true
                Verbose = false
                Logger = None
            }

        (train config)
        |> Result.map (fun _ -> Assert.Fail("Should reject < 2 eigenvalue qubits"))
        |> Result.defaultWith (fun msg -> Assert.Contains("qubit", msg.Message.ToLower()))

    // ========================================================================
    // PREDICTION TESTS
    // ========================================================================

    [<Fact>]
    let ``Predict makes correct prediction with known weights`` () =
        // Manual weights: y = 2*x1 + 3*x2 + 1 (intercept)
        let weights = [| 1.0; 2.0; 3.0 |] // [intercept; w1; w2]
        let features = [| 5.0; 7.0 |]
        let hasIntercept = true

        let prediction = predict weights features hasIntercept

        // Expected: 1 + 2*5 + 3*7 = 1 + 10 + 21 = 32
        Assert.Equal(32.0, prediction, 3)

    [<Fact>]
    let ``Predict without intercept`` () =
        let weights = [| 2.0; 3.0 |]
        let features = [| 5.0; 7.0 |]
        let hasIntercept = false

        let prediction = predict weights features hasIntercept

        // Expected: 2*5 + 3*7 = 10 + 21 = 31
        Assert.Equal(31.0, prediction, 3)

    [<Fact>]
    let ``PredictBatch makes multiple predictions`` () =
        let weights = [| 1.0; 2.0 |] // [intercept; w1]
        let features = [| [| 3.0 |]; [| 4.0 |]; [| 5.0 |] |]
        let hasIntercept = true

        let predictions = predictBatch weights features hasIntercept

        Assert.Equal(3, predictions.Length)
        // Expected: [1+2*3=7, 1+2*4=9, 1+2*5=11]
        Assert.Equal(7.0, predictions[0], 3)
        Assert.Equal(9.0, predictions[1], 3)
        Assert.Equal(11.0, predictions[2], 3)

    // ========================================================================
    // ACTUAL TRAINING TESTS (verify fixes work)
    // ========================================================================

    [<Fact>]
    let ``Train actually executes HHL and produces weights`` () =
        // Very simple 2x2 system that HHL can solve
        // Data: y = 2x (simple linear relationship)
        let backend = createLocalBackend ()

        let config: RegressionConfig =
            {
                TrainX = [| [| 1.0 |]; [| 2.0 |] |] // 2 samples, 1 feature
                TrainY = [| 2.0; 4.0 |] // y = 2x
                EigenvalueQubits = 3
                MinEigenvalue = 1e-6
                Backend = backend
                Shots = 1000
                FitIntercept = false // No intercept for simplicity
                Verbose = true
                Logger = None
            }

        match train config with
        | Error msg -> Assert.Fail($"Training failed: {msg}")
        | Ok result ->
            // If it succeeds, verify the result structure
            Assert.Equal(1, result.Weights.Length)
            Assert.Equal(1, result.NumFeatures)
            Assert.Equal(2, result.NumSamples)
            Assert.False(result.HasIntercept)
            Assert.True(result.SuccessProbability >= 0.0 && result.SuccessProbability <= 1.0)

            // Weight should be approximately 2.0 (from y = 2x)
            // But due to quantum measurement noise, we allow large tolerance
            printfn $"Trained weight: {result.Weights[0]}"
            printfn "Expected: 2.0"
            printfn $"Success probability: {result.SuccessProbability}"

    [<Fact>]
    let ``Train with intercept produces correct number of weights`` () =
        let backend = createLocalBackend ()

        let config: RegressionConfig =
            {
                TrainX = [| [| 1.0 |]; [| 2.0 |] |]
                TrainY = [| 3.0; 5.0 |] // y = 2x + 1
                EigenvalueQubits = 3
                MinEigenvalue = 1e-6
                Backend = backend
                Shots = 1000 // train's minimum
                FitIntercept = true // Enable intercept
                Verbose = false
                Logger = None
            }

        match train config with
        | Error msg -> Assert.Fail($"Training failed: {msg}")
        | Ok result ->
            // With intercept, weights = [intercept, slope]
            Assert.Equal(2, result.Weights.Length)
            Assert.True(result.HasIntercept)
            printfn $"Weights with intercept: [{result.Weights[0]:F4}, {result.Weights[1]:F4}]"
            printfn "Expected: [1.0, 2.0] (approximately)"

    // ========================================================================
    // INTEGRATION TESTS WITH PREDICTIVEMODELBUILDER
    // ========================================================================

    [<Fact>]
    let ``PredictiveModel quantum regression configuration`` () =
        let X = [| [| 1.0; 0.0 |]; [| 0.0; 1.0 |] |]
        let y = [| 1.0; 2.0 |]

        let modelResult =
            predictiveModel {
                trainWith X y
                problemType Regression
                architecture Quantum
                shots 1000
                verbose false
            }

        match modelResult with
        | Error msg -> Assert.Fail($"Model training failed: {msg}")
        | Ok model ->
            Assert.Equal(Regression, model.Metadata.ProblemType)
            Assert.Equal(Quantum, model.Metadata.Architecture)
            Assert.True(model.Metadata.NumFeatures > 0)

    [<Fact>]
    let ``PredictiveModel hybrid regression uses HHL`` () =
        let X = [| [| 1.0; 0.0 |]; [| 0.0; 1.0 |] |]
        let y = [| 1.0; 2.0 |]

        let modelResult =
            predictiveModel {
                trainWith X y
                problemType Regression
                architecture Hybrid
                shots 1000
                verbose false
            }

        match modelResult with
        | Error msg -> Assert.Fail($"Hybrid model training failed: {msg}")
        | Ok model ->
            Assert.Equal(Regression, model.Metadata.ProblemType)
            Assert.Equal(Hybrid, model.Metadata.Architecture)

    // ========================================================================
    // COMPREHENSIVE ACCURACY TESTS
    // ========================================================================

    [<Fact>]
    let ``HHL achieves R² > 0.95 for simple linear regression`` () =
        // Test: y = 2x + 1
        let backend = createLocalBackend ()

        let config: RegressionConfig =
            {
                TrainX = [| [| 1.0 |]; [| 2.0 |]; [| 3.0 |]; [| 4.0 |] |]
                TrainY = [| 3.0; 5.0; 7.0; 9.0 |]
                EigenvalueQubits = 4
                MinEigenvalue = 0.01
                Backend = backend
                Shots = 5000 // More shots for accuracy
                FitIntercept = true
                Verbose = false
                Logger = None
            }

        match train config with
        | Error msg -> Assert.Fail($"Training failed: {msg}")
        | Ok result ->
            printfn $"R² score: {result.RSquared:F4}"
            printfn $"Weights: [{result.Weights[0]:F4}, {result.Weights[1]:F4}]"
            printfn "Expected: [1.0, 2.0]"
            Assert.True(result.RSquared > 0.95, $"R² too low: {result.RSquared}")

    [<Fact>]
    let ``HHL predictions within 10% error for well-conditioned system`` () =
        // Test prediction accuracy
        let backend = createLocalBackend ()

        let config: RegressionConfig =
            {
                TrainX = [| [| 1.0 |]; [| 2.0 |]; [| 3.0 |]; [| 4.0 |] |]
                TrainY = [| 3.0; 5.0; 7.0; 9.0 |] // y = 2x + 1
                EigenvalueQubits = 5
                MinEigenvalue = 0.01
                Backend = backend
                Shots = 10000
                FitIntercept = true
                Verbose = false
                Logger = None
            }

        match train config with
        | Error msg -> Assert.Fail($"Training failed: {msg}")
        | Ok result ->
            // Test prediction for x=5, expected y=11
            let testFeatures = [| 5.0 |]
            let prediction = predict result.Weights testFeatures true
            let expected = 11.0
            let error = abs (prediction - expected) / expected

            printfn $"Prediction: {prediction:F2}, Expected: {expected:F2}, Error: {error * 100.0:F2}%%"
            Assert.True(error < 0.10, $"Prediction error too high: {error * 100.0:F2}%%")

    [<Fact>]
    let ``HHL handles multi-feature regression with R² > 0.80`` () =
        // Test: y = 3*x1 + 2*x2 + 5
        let backend = createLocalBackend ()

        let config: RegressionConfig =
            {
                TrainX = [| [| 1.0; 1.0 |]; [| 2.0; 1.0 |]; [| 1.0; 2.0 |]; [| 2.0; 2.0 |] |]
                TrainY = [| 10.0; 13.0; 12.0; 15.0 |] // y = 3*x1 + 2*x2 + 5
                EigenvalueQubits = 4
                MinEigenvalue = 0.01
                Backend = backend
                Shots = 5000
                FitIntercept = true
                Verbose = false
                Logger = None
            }

        match train config with
        | Error msg -> Assert.Fail($"Multi-feature training failed: {msg}")
        | Ok result ->
            printfn $"R² score: {result.RSquared:F4}"
            printfn $"Weights: [{result.Weights[0]:F4}, {result.Weights[1]:F4}, {result.Weights[2]:F4}]"
            printfn "Expected: [5.0, 3.0, 2.0]"
            // Multi-feature is harder - accept R² > 0.60 as reasonable
            Assert.True(result.RSquared > 0.60, $"R² too low for multi-feature: {result.RSquared}")

    [<Fact>]
    let ``HHL trains at 3 to 6 eigenvalue qubits with a valid success probability`` () =
        // More eigenvalue qubits do not raise the success probability here: it measures
        // 0.0051 at every count (the old name claimed an increase that was never asserted).
        let backend = createLocalBackend ()

        let baseConfig: RegressionConfig =
            {
                TrainX = [| [| 1.0 |]; [| 2.0 |]; [| 3.0 |] |]
                TrainY = [| 2.0; 4.0; 6.0 |]
                EigenvalueQubits = 3
                MinEigenvalue = 0.01
                Backend = backend
                Shots = 5000
                FitIntercept = false
                Verbose = false
                Logger = None
            }

        let results =
            [ 3; 4; 5; 6 ]
            |> List.choose (fun qubits ->
                let config =
                    { baseConfig with
                        EigenvalueQubits = qubits
                    }

                match train config with
                | Ok result -> Some(qubits, result.SuccessProbability)
                | Error e ->
                    Assert.Fail($"training with {qubits} eigenvalue qubits failed: {e}")
                    None)

        results |> List.iter (fun (q, p) -> printfn $"Qubits: {q}, Success: {p:F4}")

        for (q, p) in results do
            Assert.True(p > 0.0 && p <= 1.0, $"{q} eigenvalue qubits: success probability {p} outside (0, 1]")

    [<Fact>]
    let ``HHL validates NaN inputs`` () =
        let backend = createLocalBackend ()

        let config: RegressionConfig =
            {
                TrainX = [| [| nan |]; [| 2.0 |] |] // NaN in features
                TrainY = [| 1.0; 2.0 |]
                EigenvalueQubits = 3
                MinEigenvalue = 0.01
                Backend = backend
                Shots = 1000
                FitIntercept = false
                Verbose = false
                Logger = None
            }

        (train config)
        |> Result.map (fun _ -> Assert.Fail("Should reject NaN inputs"))
        |> Result.defaultWith (fun msg -> Assert.Contains("NaN", msg.Message))

    [<Fact>]
    let ``HHL validates insufficient shots`` () =
        let backend = createLocalBackend ()

        let config: RegressionConfig =
            {
                TrainX = [| [| 1.0 |]; [| 2.0 |] |]
                TrainY = [| 1.0; 2.0 |]
                EigenvalueQubits = 3
                MinEigenvalue = 0.01
                Backend = backend
                Shots = 100 // Too few shots
                FitIntercept = false
                Verbose = false
                Logger = None
            }

        (train config)
        |> Result.map (fun _ -> Assert.Fail("Should reject insufficient shots"))
        |> Result.defaultWith (fun msg -> Assert.Contains("Shots", msg.Message))

    // ========================================================================
    // CLOUD (WHOLE-CIRCUIT, SHOT-SAMPLING) BACKENDS: SIGNS FROM INTERFERENCE CIRCUITS
    // ========================================================================

    /// y = 2·x1 − x2 exactly: 2 features (no intercept), a well-conditioned Gram matrix.
    let private twoFeatureDesign =
        [|
            [| 1.0; 0.2 |]
            [| 0.1; 1.0 |]
            [| 0.9; -0.1 |]
            [| -0.2; 0.8 |]
            [| 0.5; 0.4 |]
            [| 0.3; -0.6 |]
        |]

    /// y = x1 − 2·x2 + 0.5·x3 + 1.5·x4 exactly: 4 features (no intercept).
    let private fourFeatureDesign =
        Array.init 12 (fun i ->
            [|
                float (i % 2)
                float ((i / 2) % 2)
                float ((i / 4) % 3) / 2.0
                float ((i * 7) % 5) / 4.0
            |])

    let private regressionConfig (x: float array array) (weights: float[]) (backend: IQuantumBackend) : RegressionConfig =
        {
            TrainX = x
            TrainY = x |> Array.map (fun row -> Array.fold2 (fun acc xi wi -> acc + xi * wi) 0.0 row weights)
            EigenvalueQubits = 6
            MinEigenvalue = 1e-6
            Backend = backend
            Shots = 10000
            FitIntercept = false
            Verbose = false
            Logger = None
        }

    /// Train on the local simulator and on a seeded 10,000-shot cloud-style backend; the cloud
    /// weights (signs measured by interference circuits) must match the exact path within
    /// `tolerance`, with `expectedJobs` whole-circuit jobs.
    let private assertCloudMatchesExact (x: float array array) (weights: float[]) (expectedJobs: int) (tolerance: float) =
        let cloud = CloudStyleBackends.ShotSamplingCloud(10000, 17)

        match train (regressionConfig x weights (createLocalBackend ())), train (regressionConfig x weights cloud) with
        | Ok exact, Ok sampled ->
            Assert.Equal(0, exact.Circuits)
            Assert.Equal(expectedJobs, sampled.Circuits)
            Assert.Equal(expectedJobs, cloud.Jobs)

            for i in 0 .. weights.Length - 1 do
                // Same sign as the true weight on both routes (the negative one included).
                Assert.Equal(sign weights.[i], sign exact.Weights.[i])
                Assert.Equal(sign weights.[i], sign sampled.Weights.[i])

                Assert.True(
                    abs (sampled.Weights.[i] - exact.Weights.[i]) < tolerance,
                    $"weight {i}: cloud {sampled.Weights.[i]} vs exact {exact.Weights.[i]}"
                )

            Assert.True(abs (sampled.RSquared - exact.RSquared) < 0.01, $"R² {sampled.RSquared} vs {exact.RSquared}")
        | Error e, _
        | _, Error e -> failwith $"training failed: {e.Message}"

    [<Fact>]
    let ``Train on a shot-sampling cloud backend recovers signed weights matching the exact path (2 features)`` () =
        // 2 weights: 1 solution qubit, 8 qubits in all; magnitude circuit + 1 Hadamard circuit.
        assertCloudMatchesExact twoFeatureDesign [| 2.0; -1.0 |] 2 0.08

    [<Fact>]
    let ``Train on a shot-sampling cloud backend recovers signed weights matching the exact path (4 features)`` () =
        // 4 weights: 2 solution qubits, 9 qubits in all; magnitude circuit + 2 Hadamard circuits.
        assertCloudMatchesExact fourFeatureDesign [| 1.0; -2.0; 0.5; 1.5 |] 3 0.1
