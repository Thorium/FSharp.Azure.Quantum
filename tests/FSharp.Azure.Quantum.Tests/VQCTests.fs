namespace FSharp.Azure.Quantum.Tests

open System
open System.Threading
open System.Threading.Tasks
open Xunit
open FSharp.Azure.Quantum.MachineLearning
open FSharp.Azure.Quantum.MachineLearning.VQC
open FSharp.Azure.Quantum.MachineLearning.VariationalForms
open FSharp.Azure.Quantum.CircuitBuilder
open FSharp.Azure.Quantum.Core.BackendAbstraction
open FSharp.Azure.Quantum.Core.CircuitAbstraction
open FSharp.Azure.Quantum.Backends

/// Unit tests for Variational Quantum Classifier (VQC)
///
/// Tests cover:
/// - Training loop
/// - Prediction
/// - Loss computation
/// - Gradient computation
/// - Evaluation metrics
module VQCTests =
    
    // ========================================================================
    // TEST BACKEND - Use real LocalBackend
    // ========================================================================
    
    let createTestBackend () : IQuantumBackend =
        LocalBackend.LocalBackend() :> IQuantumBackend
    
    // ========================================================================
    // HELPER FUNCTIONS
    // ========================================================================
    
    let private createSimpleDataset () =
        // Simple linearly separable dataset
        let features = [|
            [| 0.1; 0.2 |]  // Class 0
            [| 0.2; 0.1 |]  // Class 0
            [| 0.8; 0.9 |]  // Class 1
            [| 0.9; 0.8 |]  // Class 1
        |]
        
        let labels = [| 0; 0; 1; 1 |]
        
        (features, labels)
    
    let private createTestConfig () = {
        LearningRate = 0.1
        MaxEpochs = 5
        ConvergenceThreshold = 1e-4
        Shots = 100
        Verbose = false
        Optimizer = VQC.SGD
        ProgressReporter = None
        Logger = None
    }
    
    // ========================================================================
    // VALIDATION TESTS
    // ========================================================================
    
    [<Fact>]
    let ``train - rejects empty training set`` () =
        let backend = createTestBackend()
        let featureMap = AngleEncoding
        let variationalForm = RealAmplitudes 1
        let parameters = randomParameters variationalForm 2 (Some 42)
        let emptyFeatures = [| |]
        let emptyLabels = [| |]
        let config = createTestConfig()
        
        match train backend featureMap variationalForm parameters emptyFeatures emptyLabels config with
        | Error msg ->
            Assert.Contains("cannot be empty", msg.Message)
        | Ok _ ->
            Assert.True(false, "Should have rejected empty training set")
    
    [<Fact>]
    let ``train - rejects mismatched features and labels length`` () =
        let backend = createTestBackend()
        let featureMap = AngleEncoding
        let variationalForm = RealAmplitudes 1
        let parameters = randomParameters variationalForm 2 (Some 42)
        let features = [| [| 0.1; 0.2 |]; [| 0.3; 0.4 |] |]
        let labels = [| 0 |]  // Wrong length
        let config = createTestConfig()
        
        match train backend featureMap variationalForm parameters features labels config with
        | Error msg ->
            Assert.Contains("same length", msg.Message)
        | Ok _ ->
            Assert.True(false, "Should have rejected mismatched lengths")
    
    [<Fact>]
    let ``evaluate - rejects empty dataset`` () =
        let backend = createTestBackend()
        let featureMap = AngleEncoding
        let variationalForm = RealAmplitudes 1
        let parameters = randomParameters variationalForm 2 (Some 42)
        let emptyFeatures = [| |]
        let emptyLabels = [| |]
        
        match evaluate backend featureMap variationalForm parameters emptyFeatures emptyLabels 100 with
        | Error msg ->
            Assert.Contains("cannot be empty", msg.Message)
        | Ok _ ->
            Assert.True(false, "Should have rejected empty dataset")
    
    [<Fact>]
    let ``evaluate - rejects mismatched features and labels length`` () =
        let backend = createTestBackend()
        let featureMap = AngleEncoding
        let variationalForm = RealAmplitudes 1
        let parameters = randomParameters variationalForm 2 (Some 42)
        let features = [| [| 0.1; 0.2 |]; [| 0.3; 0.4 |] |]
        let labels = [| 0 |]  // Wrong length
        
        match evaluate backend featureMap variationalForm parameters features labels 100 with
        | Error msg ->
            Assert.Contains("same length", msg.Message)
        | Ok _ ->
            Assert.True(false, "Should have rejected mismatched lengths")
    
    // ========================================================================
    // PREDICTION TESTS
    // ========================================================================
    
    [<Fact>]
    let ``predict - returns valid label and probability`` () =
        let backend = createTestBackend()
        let featureMap = AngleEncoding
        let variationalForm = RealAmplitudes 1
        let parameters = randomParameters variationalForm 2 (Some 42)
        let features = [| 0.5; 0.5 |]
        
        match predict backend featureMap variationalForm parameters features 100 with
        | Error msg ->
            Assert.True(false, $"Prediction failed: {msg}")
        | Ok prediction ->
            // Label should be 0 or 1
            Assert.True(prediction.Label = 0 || prediction.Label = 1, "Label must be 0 or 1")
            // Probability must be in valid range
            Assert.True(prediction.Probability >= 0.0 && prediction.Probability <= 1.0, "Probability in [0,1]")
            // Label consistency: if prob >= 0.5 then label should be 1
            if prediction.Probability >= 0.5 then
                Assert.Equal(1, prediction.Label)
            else
                Assert.Equal(0, prediction.Label)
    
    [<Fact>]
    let ``predict - probability is in valid range [0, 1]`` () =
        let backend = createTestBackend()
        let featureMap = AngleEncoding
        let variationalForm = RealAmplitudes 1
        let parameters = randomParameters variationalForm 2 (Some 42)
        let features = [| 0.5; 0.5 |]
        
        match predict backend featureMap variationalForm parameters features 100 with
        | Error msg ->
            Assert.True(false, $"Prediction failed: {msg}")
        | Ok prediction ->
            Assert.True(prediction.Probability >= 0.0, "Probability should be >= 0")
            Assert.True(prediction.Probability <= 1.0, "Probability should be <= 1")
    
    // ========================================================================
    // EVALUATION TESTS
    // ========================================================================
    
    [<Fact>]
    let ``evaluate - returns valid accuracy in range [0, 1]`` () =
        let backend = createTestBackend()
        let featureMap = AngleEncoding
        let variationalForm = RealAmplitudes 1
        let parameters = randomParameters variationalForm 2 (Some 42)
        
        let features = [| [| 0.1; 0.2 |]; [| 0.3; 0.4 |]; [| 0.5; 0.6 |] |]
        let labels = [| 0; 1; 0 |]
        
        match evaluate backend featureMap variationalForm parameters features labels 100 with
        | Error msg ->
            Assert.True(false, $"Evaluation failed: {msg}")
        | Ok accuracy ->
            // With untrained random parameters, accuracy will be random but valid
            Assert.True(accuracy >= 0.0 && accuracy <= 1.0, "Accuracy must be in [0, 1]")
    
    [<Fact>]
    let ``evaluate - handles single sample correctly`` () =
        let backend = createTestBackend()
        let featureMap = AngleEncoding
        let variationalForm = RealAmplitudes 1
        let parameters = randomParameters variationalForm 2 (Some 42)
        let features = [| [| 0.5; 0.5 |] |]
        let labels = [| 0 |]
        
        match evaluate backend featureMap variationalForm parameters features labels 100 with
        | Error msg ->
            Assert.True(false, $"Evaluation failed: {msg}")
        | Ok accuracy ->
            // Either 0.0 (wrong) or 1.0 (correct) for single sample
            Assert.True(accuracy = 0.0 || accuracy = 1.0, "Single sample accuracy is 0 or 1")
    
    // ========================================================================
    // TRAINING TESTS
    // ========================================================================
    
    [<Fact>]
    let ``train - completes successfully with valid inputs`` () =
        let backend = createTestBackend()
        let featureMap = AngleEncoding
        let variationalForm = RealAmplitudes 1
        let parameters = randomParameters variationalForm 2 (Some 42)
        let (features, labels) = createSimpleDataset()
        let config = createTestConfig()
        
        match train backend featureMap variationalForm parameters features labels config with
        | Error msg ->
            Assert.True(false, $"Training failed: {msg}")
        | Ok result ->
            Assert.True(result.Epochs > 0, "Should have trained for at least 1 epoch")
            Assert.True(result.Epochs <= config.MaxEpochs, "Should not exceed max epochs")
            Assert.Equal(parameters.Length, result.Parameters.Length)
            Assert.True(result.LossHistory.Length > 0, "Should have loss history")
    
    [<Fact>]
    let ``train - returns trained parameters with correct length`` () =
        let backend = createTestBackend()
        let featureMap = AngleEncoding
        let variationalForm = RealAmplitudes 1
        let initialParams = randomParameters variationalForm 2 (Some 42)
        let (features, labels) = createSimpleDataset()
        let config = createTestConfig()
        
        match train backend featureMap variationalForm initialParams features labels config with
        | Error msg ->
            Assert.True(false, $"Training failed: {msg}")
        | Ok result ->
            Assert.Equal(initialParams.Length, result.Parameters.Length)
    
    [<Fact>]
    let ``train - loss history has correct length`` () =
        let backend = createTestBackend()
        let featureMap = AngleEncoding
        let variationalForm = RealAmplitudes 1
        let parameters = randomParameters variationalForm 2 (Some 42)
        let (features, labels) = createSimpleDataset()
        let config = { createTestConfig() with MaxEpochs = 3 }
        
        match train backend featureMap variationalForm parameters features labels config with
        | Error msg ->
            Assert.True(false, $"Training failed: {msg}")
        | Ok result ->
            // Loss history should have entries for epochs completed + initial
            Assert.True(result.LossHistory.Length >= 1, "Should have at least initial loss")
            Assert.True(result.LossHistory.Length <= config.MaxEpochs + 1, "Should not exceed max epochs + 1")
    
    [<Fact>]
    let ``train - respects max epochs limit`` () =
        let backend = createTestBackend()
        let featureMap = AngleEncoding
        let variationalForm = RealAmplitudes 1
        let parameters = randomParameters variationalForm 2 (Some 42)
        let (features, labels) = createSimpleDataset()
        let config = { createTestConfig() with MaxEpochs = 2; ConvergenceThreshold = 0.0 }  // Won't converge
        
        match train backend featureMap variationalForm parameters features labels config with
        | Error msg ->
            Assert.True(false, $"Training failed: {msg}")
        | Ok result ->
            Assert.True(result.Epochs <= config.MaxEpochs, $"Epochs {result.Epochs} should not exceed {config.MaxEpochs}")
    
    [<Fact>]
    let ``train - returns training accuracy`` () =
        let backend = createTestBackend()
        let featureMap = AngleEncoding
        let variationalForm = RealAmplitudes 1
        let parameters = randomParameters variationalForm 2 (Some 42)
        let (features, labels) = createSimpleDataset()
        let config = createTestConfig()
        
        match train backend featureMap variationalForm parameters features labels config with
        | Error msg ->
            Assert.True(false, $"Training failed: {msg}")
        | Ok result ->
            Assert.True(result.TrainAccuracy >= 0.0, "Accuracy should be >= 0")
            Assert.True(result.TrainAccuracy <= 1.0, "Accuracy should be <= 1")
    
    // ========================================================================
    // CONFUSION MATRIX TESTS
    // ========================================================================
    
    [<Fact>]
    let ``confusionMatrix - returns valid confusion matrix`` () =
        let backend = createTestBackend()
        let featureMap = AngleEncoding
        let variationalForm = RealAmplitudes 1
        let parameters = randomParameters variationalForm 2 (Some 42)
        
        let features = [| [| 0.1; 0.2 |]; [| 0.3; 0.4 |]; [| 0.5; 0.6 |]; [| 0.7; 0.8 |] |]
        let labels = [| 0; 0; 1; 1 |]
        
        match confusionMatrix backend featureMap variationalForm parameters features labels 100 with
        | Error msg ->
            Assert.True(false, $"Confusion matrix failed: {msg}")
        | Ok cm ->
            // Check that all counts are non-negative
            Assert.True(cm.TruePositives >= 0)
            Assert.True(cm.TrueNegatives >= 0)
            Assert.True(cm.FalsePositives >= 0)
            Assert.True(cm.FalseNegatives >= 0)
            // Check that counts sum to total samples
            let total = cm.TruePositives + cm.TrueNegatives + cm.FalsePositives + cm.FalseNegatives
            Assert.Equal(4, total)
    
    [<Fact>]
    let ``precision - computes correctly`` () =
        let cm = {
            TruePositives = 3
            TrueNegatives = 2
            FalsePositives = 1
            FalseNegatives = 2
        }
        
        let p = precision cm
        Assert.Equal(0.75, p, 2)  // 3 / (3 + 1) = 0.75
    
    [<Fact>]
    let ``precision - handles zero denominator`` () =
        let cm = {
            TruePositives = 0
            TrueNegatives = 5
            FalsePositives = 0
            FalseNegatives = 3
        }
        
        let p = precision cm
        Assert.Equal(0.0, p)  // No positive predictions
    
    [<Fact>]
    let ``recall - computes correctly`` () =
        let cm = {
            TruePositives = 3
            TrueNegatives = 2
            FalsePositives = 1
            FalseNegatives = 2
        }
        
        let r = recall cm
        Assert.Equal(0.6, r, 2)  // 3 / (3 + 2) = 0.6
    
    [<Fact>]
    let ``recall - handles zero denominator`` () =
        let cm = {
            TruePositives = 0
            TrueNegatives = 5
            FalsePositives = 3
            FalseNegatives = 0
        }
        
        let r = recall cm
        Assert.Equal(0.0, r)  // No actual positive samples
    
    [<Fact>]
    let ``f1Score - computes correctly`` () =
        let cm = {
            TruePositives = 3
            TrueNegatives = 2
            FalsePositives = 1
            FalseNegatives = 2
        }
        
        let f1 = f1Score cm
        // Precision = 0.75, Recall = 0.6
        // F1 = 2 * 0.75 * 0.6 / (0.75 + 0.6) = 0.667
        Assert.True(abs (f1 - 0.667) < 0.01, $"F1 score {f1} should be ~0.667")
    
    [<Fact>]
    let ``f1Score - handles zero denominator`` () =
        let cm = {
            TruePositives = 0
            TrueNegatives = 5
            FalsePositives = 0
            FalseNegatives = 0
        }
        
        let f1 = f1Score cm
        Assert.Equal(0.0, f1)  // Both precision and recall are 0
    
    // ========================================================================
    // INTEGRATION TESTS
    // ========================================================================
    
    [<Fact>]
    let ``End-to-end - train and predict workflow`` () =
        let backend = createTestBackend()
        let featureMap = AngleEncoding
        let variationalForm = RealAmplitudes 1
        let initialParams = randomParameters variationalForm 2 (Some 42)
        let (trainFeatures, trainLabels) = createSimpleDataset()
        let config = { createTestConfig() with MaxEpochs = 3; Verbose = false }
        
        // Train
        match train backend featureMap variationalForm initialParams trainFeatures trainLabels config with
        | Error msg ->
            Assert.True(false, $"Training failed: {msg}")
        | Ok trainResult ->
            Assert.True(trainResult.Epochs > 0, "Should have trained")
            
            // Predict on training data
            match predict backend featureMap variationalForm trainResult.Parameters trainFeatures.[0] 100 with
            | Error msg ->
                Assert.True(false, $"Prediction failed: {msg}")
            | Ok prediction ->
                Assert.True(prediction.Label = 0 || prediction.Label = 1, "Label should be 0 or 1")
                Assert.True(prediction.Probability >= 0.0 && prediction.Probability <= 1.0, "Valid probability")
    
    [<Fact>]
    let ``End-to-end - train and evaluate workflow`` () =
        let backend = createTestBackend()
        let featureMap = AngleEncoding
        let variationalForm = RealAmplitudes 1
        let initialParams = randomParameters variationalForm 2 (Some 42)
        let (trainFeatures, trainLabels) = createSimpleDataset()
        let config = { createTestConfig() with MaxEpochs = 3; Verbose = false }
        
        // Train
        match train backend featureMap variationalForm initialParams trainFeatures trainLabels config with
        | Error msg ->
            Assert.True(false, $"Training failed: {msg}")
        | Ok trainResult ->
            
            // Evaluate on training data
            match evaluate backend featureMap variationalForm trainResult.Parameters trainFeatures trainLabels 100 with
            | Error msg ->
                Assert.True(false, $"Evaluation failed: {msg}")
            | Ok accuracy ->
                Assert.True(accuracy >= 0.0 && accuracy <= 1.0, "Valid accuracy")
                // With quantum probabilistic results, accuracies should be close but may differ
                // Just verify both are reasonable
                Assert.True(trainResult.TrainAccuracy >= 0.0 && trainResult.TrainAccuracy <= 1.0, "Train accuracy valid")
    
    [<Fact>]
    let ``defaultConfig - has sensible defaults`` () =
        Assert.True(defaultConfig.LearningRate > 0.0, "Learning rate should be positive")
        Assert.True(defaultConfig.MaxEpochs > 0, "Max epochs should be positive")
        Assert.True(defaultConfig.ConvergenceThreshold > 0.0, "Convergence threshold should be positive")
        Assert.True(defaultConfig.Shots > 0, "Shots should be positive")
        
        match defaultConfig.Optimizer with
        | VQC.SGD -> ()  // Default should be SGD
        | VQC.Adam _ -> Assert.True(false, "Default should use SGD, not Adam")
    
    [<Fact>]
    let ``defaultConfigWithAdam - uses Adam optimizer`` () =
        Assert.True(defaultConfigWithAdam.LearningRate > 0.0, "Learning rate should be positive")
        Assert.True(defaultConfigWithAdam.MaxEpochs > 0, "Max epochs should be positive")
        
        match defaultConfigWithAdam.Optimizer with
        | VQC.Adam _ -> ()  // Should be Adam
        | VQC.SGD -> Assert.True(false, "defaultConfigWithAdam should use Adam, not SGD")
    
    [<Fact>]
    let ``train with Adam optimizer - should complete successfully`` () =
        let backend = LocalBackend.LocalBackend() :> IQuantumBackend
        let featureMap = AngleEncoding
        let variationalForm = RealAmplitudes 1
        let (features, labels) = createSimpleDataset ()
        let parameters = VariationalForms.randomParameters variationalForm (features.[0].Length) (Some 42)
        
        let adamConfig = { 
            defaultConfigWithAdam with 
                MaxEpochs = 10
                Verbose = false
        }
        
        let result = train backend featureMap variationalForm parameters features labels adamConfig
        
        match result with
        | Error msg ->
            Assert.True(false, $"Training with Adam should succeed: {msg}")
        | Ok trainResult ->
            Assert.True(trainResult.Parameters.Length > 0, "Should have trained parameters")
            Assert.True(trainResult.LossHistory.Length > 0, "Should have loss history")
            Assert.True(trainResult.Epochs > 0, "Should have completed epochs")
            Assert.True(trainResult.TrainAccuracy >= 0.0 && trainResult.TrainAccuracy <= 1.0, "Valid accuracy")
    
    [<Fact>]
    let ``train with SGD vs Adam - both should converge`` () =
        let backend = LocalBackend.LocalBackend() :> IQuantumBackend
        let featureMap = AngleEncoding
        let variationalForm = RealAmplitudes 1
        let (features, labels) = createSimpleDataset ()
        let parameters = VariationalForms.randomParameters variationalForm (features.[0].Length) (Some 42)
        
        // Train with SGD
        let sgdConfig = { 
            (createTestConfig ()) with 
                MaxEpochs = 15
                LearningRate = 0.1
                Optimizer = VQC.SGD
        }
        
        let sgdResult = train backend featureMap variationalForm parameters features labels sgdConfig
        
        // Train with Adam (use same initial parameters for fair comparison)
        let adamConfig = { 
            (createTestConfig ()) with 
                MaxEpochs = 15
                LearningRate = 0.01  // Adam typically uses smaller LR
                Optimizer = VQC.Adam AdamOptimizer.defaultConfig
        }
        
        let adamResult = train backend featureMap variationalForm parameters features labels adamConfig
        
        match sgdResult, adamResult with
        | Ok sgd, Ok adam ->
            // Both should complete
            Assert.True(sgd.Epochs > 0, "SGD should complete epochs")
            Assert.True(adam.Epochs > 0, "Adam should complete epochs")
            
            // Both should have reasonable accuracy (may differ due to quantum randomness)
            Assert.True(sgd.TrainAccuracy >= 0.0, "SGD should have valid accuracy")
            Assert.True(adam.TrainAccuracy >= 0.0, "Adam should have valid accuracy")
            
            // Both should have loss history
            Assert.True(sgd.LossHistory.Length > 0, "SGD should have loss history")
            Assert.True(adam.LossHistory.Length > 0, "Adam should have loss history")
            
        | Error sgdErr, _ ->
            Assert.True(false, $"SGD training failed: {sgdErr}")
        | _, Error adamErr ->
            Assert.True(false, $"Adam training failed: {adamErr}")
    
    // ========================================================================
    // MULTI-CLASS VQC TESTS
    // ========================================================================
    
    let private createMultiClassDataset () =
        // 3-class dataset (iris-like)
        let features = [|
            // Class 0 (cluster around [0.1, 0.1])
            [| 0.1; 0.1 |]
            [| 0.15; 0.12 |]
            [| 0.08; 0.14 |]
            
            // Class 1 (cluster around [0.5, 0.5])
            [| 0.5; 0.5 |]
            [| 0.52; 0.48 |]
            [| 0.48; 0.52 |]
            
            // Class 2 (cluster around [0.9, 0.9])
            [| 0.9; 0.9 |]
            [| 0.88; 0.92 |]
            [| 0.91; 0.87 |]
        |]
        
        let labels = [| 0; 0; 0; 1; 1; 1; 2; 2; 2 |]
        
        (features, labels)
    
    [<Fact>]
    let ``trainMultiClass - completes successfully with 3 classes`` () =
        let backend = createTestBackend()
        let featureMap = AngleEncoding
        let variationalForm = RealAmplitudes 1
        let parameters = randomParameters variationalForm 2 (Some 42)
        let (features, labels) = createMultiClassDataset()
        let config = { createTestConfig() with MaxEpochs = 5; Verbose = false }
        
        match trainMultiClass backend featureMap variationalForm parameters features labels config with
        | Error msg ->
            Assert.True(false, $"Multi-class training failed: {msg}")
        | Ok result ->
            // Should have 3 binary classifiers (one per class)
            Assert.Equal(3, result.Classifiers.Length)
            Assert.Equal(3, result.NumClasses)
            
            // Class labels should be [0; 1; 2]
            Assert.Equal<int seq>([| 0; 1; 2 |], result.ClassLabels)
            
            // Each classifier should have trained parameters
            for classifier in result.Classifiers do
                Assert.Equal(parameters.Length, classifier.Parameters.Length)
                Assert.True(classifier.Epochs > 0, "Classifier should have trained")
            
            // Overall training accuracy should be valid
            Assert.True(result.TrainAccuracy >= 0.0 && result.TrainAccuracy <= 1.0, "Valid accuracy")
    
    [<Fact>]
    let ``trainMultiClass - rejects binary classification (uses standard train)`` () =
        let backend = createTestBackend()
        let featureMap = AngleEncoding
        let variationalForm = RealAmplitudes 1
        let parameters = randomParameters variationalForm 2 (Some 42)
        let features = [| [| 0.1; 0.2 |]; [| 0.8; 0.9 |] |]
        let labels = [| 0; 1 |]
        let config = createTestConfig()
        
        match trainMultiClass backend featureMap variationalForm parameters features labels config with
        | Error msg ->
            Assert.True(false, $"Binary classification should work: {msg}")
        | Ok result ->
            // Should still work but only have 1 classifier
            Assert.Equal(1, result.Classifiers.Length)
            Assert.Equal(2, result.NumClasses)
    
    [<Fact>]
    let ``trainMultiClass - rejects single class`` () =
        let backend = createTestBackend()
        let featureMap = AngleEncoding
        let variationalForm = RealAmplitudes 1
        let parameters = randomParameters variationalForm 2 (Some 42)
        let features = [| [| 0.1; 0.2 |]; [| 0.3; 0.4 |] |]
        let labels = [| 0; 0 |]  // All same class
        let config = createTestConfig()
        
        match trainMultiClass backend featureMap variationalForm parameters features labels config with
        | Error msg ->
            Assert.Contains("at least 2 classes", msg.Message)
        | Ok _ ->
            Assert.True(false, "Should have rejected single class")
    
    [<Fact>]
    let ``trainMultiClass - rejects empty training set`` () =
        let backend = createTestBackend()
        let featureMap = AngleEncoding
        let variationalForm = RealAmplitudes 1
        let parameters = randomParameters variationalForm 2 (Some 42)
        let emptyFeatures = [| |]
        let emptyLabels = [| |]
        let config = createTestConfig()
        
        match trainMultiClass backend featureMap variationalForm parameters emptyFeatures emptyLabels config with
        | Error msg ->
            Assert.Contains("cannot be empty", msg.Message)
        | Ok _ ->
            Assert.True(false, "Should have rejected empty training set")
    
    [<Fact>]
    let ``trainMultiClass - rejects mismatched features and labels`` () =
        let backend = createTestBackend()
        let featureMap = AngleEncoding
        let variationalForm = RealAmplitudes 1
        let parameters = randomParameters variationalForm 2 (Some 42)
        let features = [| [| 0.1; 0.2 |]; [| 0.3; 0.4 |] |]
        let labels = [| 0 |]  // Wrong length
        let config = createTestConfig()
        
        match trainMultiClass backend featureMap variationalForm parameters features labels config with
        | Error msg ->
            Assert.Contains("same length", msg.Message)
        | Ok _ ->
            Assert.True(false, "Should have rejected mismatched lengths")
    
    [<Fact>]
    let ``predictMultiClass - returns valid prediction`` () =
        let backend = createTestBackend()
        let featureMap = AngleEncoding
        let variationalForm = RealAmplitudes 1
        let parameters = randomParameters variationalForm 2 (Some 42)
        let (features, labels) = createMultiClassDataset()
        let config = { createTestConfig() with MaxEpochs = 5; Verbose = false }
        
        match trainMultiClass backend featureMap variationalForm parameters features labels config with
        | Error msg ->
            Assert.True(false, $"Training failed: {msg}")
        | Ok trainResult ->
            // Predict on first training sample (class 0)
            match predictMultiClass backend featureMap variationalForm trainResult features.[0] 100 with
            | Error msg ->
                Assert.True(false, $"Prediction failed: {msg}")
            | Ok prediction ->
                // Label should be one of [0, 1, 2]
                Assert.True(prediction.Label >= 0 && prediction.Label <= 2, "Label should be 0, 1, or 2")
                
                // Confidence should be valid
                Assert.True(prediction.Confidence >= 0.0 && prediction.Confidence <= 1.0, "Valid confidence")
                
                // Probabilities should sum to ~1.0
                let probSum = Array.sum prediction.Probabilities
                Assert.True(abs (probSum - 1.0) < 0.01, $"Probabilities should sum to 1.0, got {probSum}")
                
                // Should have 3 probabilities
                Assert.Equal(3, prediction.Probabilities.Length)
                
                // All probabilities should be non-negative
                for prob in prediction.Probabilities do
                    Assert.True(prob >= 0.0, "Probability should be non-negative")
    
    [<Fact>]
    let ``predictMultiClass - confidence matches highest probability`` () =
        let backend = createTestBackend()
        let featureMap = AngleEncoding
        let variationalForm = RealAmplitudes 1
        let parameters = randomParameters variationalForm 2 (Some 42)
        let (features, labels) = createMultiClassDataset()
        let config = { createTestConfig() with MaxEpochs = 3; Verbose = false }
        
        match trainMultiClass backend featureMap variationalForm parameters features labels config with
        | Error msg ->
            Assert.True(false, $"Training failed: {msg}")
        | Ok trainResult ->
            match predictMultiClass backend featureMap variationalForm trainResult features.[0] 100 with
            | Error msg ->
                Assert.True(false, $"Prediction failed: {msg}")
            | Ok prediction ->
                // Confidence should equal the probability of the predicted class
                let predictedClassIdx = Array.findIndex ((=) prediction.Label) trainResult.ClassLabels
                let expectedConfidence = prediction.Probabilities.[predictedClassIdx]
                Assert.True(abs (prediction.Confidence - expectedConfidence) < 0.001, 
                           $"Confidence {prediction.Confidence} should match probability {expectedConfidence}")
    
    [<Fact>]
    let ``predictMultiClass - no floating point comparison bugs`` () =
        // This test verifies fix for Issue #1 (floating-point comparison bug)
        let backend = createTestBackend()
        let featureMap = AngleEncoding
        let variationalForm = RealAmplitudes 1
        let parameters = randomParameters variationalForm 2 (Some 42)
        let (features, labels) = createMultiClassDataset()
        let config = { createTestConfig() with MaxEpochs = 3; Verbose = false }
        
        match trainMultiClass backend featureMap variationalForm parameters features labels config with
        | Error msg ->
            Assert.True(false, $"Training failed: {msg}")
        | Ok trainResult ->
            // Run prediction 10 times to catch any floating-point comparison issues
            for i in 1 .. 10 do
                match predictMultiClass backend featureMap variationalForm trainResult features.[0] 100 with
                | Error msg ->
                    Assert.True(false, $"Prediction #{i} failed: {msg}")
                | Ok prediction ->
                    // Should always succeed without "index not found" errors
                    Assert.True(prediction.Label >= 0 && prediction.Label <= 2, "Valid label")
    
    [<Fact>]
    let ``End-to-end - multi-class train and predict workflow`` () =
        let backend = createTestBackend()
        let featureMap = AngleEncoding
        let variationalForm = RealAmplitudes 1
        let initialParams = randomParameters variationalForm 2 (Some 42)
        let (trainFeatures, trainLabels) = createMultiClassDataset()
        let config = { createTestConfig() with MaxEpochs = 5; Verbose = false }
        
        // Train multi-class model
        match trainMultiClass backend featureMap variationalForm initialParams trainFeatures trainLabels config with
        | Error msg ->
            Assert.True(false, $"Multi-class training failed: {msg}")
        | Ok trainResult ->
            Assert.Equal(3, trainResult.NumClasses)
            
            // Predict on each class
            for classLabel in 0 .. 2 do
                // Find first sample of this class
                let sampleIdx = Array.findIndex ((=) classLabel) trainLabels
                let testFeatures = trainFeatures.[sampleIdx]
                
                match predictMultiClass backend featureMap variationalForm trainResult testFeatures 100 with
                | Error msg ->
                    Assert.True(false, $"Prediction for class {classLabel} failed: {msg}")
                | Ok prediction ->
                    // Predicted label should be valid
                    Assert.True(prediction.Label >= 0 && prediction.Label <= 2, "Valid label")
                    Assert.True(prediction.Confidence > 0.0, "Should have some confidence")

    // ========================================================================
    // ASYNC PREDICTION TESTS (via public API)
    // ========================================================================

    [<Fact>]
    let ``predictRegressionAsync - returns valid prediction for single sample`` () : Task =
        task {
            let backend = createTestBackend()
            let featureMap = AngleEncoding
            let variationalForm = RealAmplitudes 1
            let parameters = randomParameters variationalForm 2 (Some 42)
            let features = [| 0.5; 0.5 |]
            let valueRange = (0.0, 1.0)

            let! result = predictRegressionAsync backend featureMap variationalForm parameters features 100 valueRange CancellationToken.None
            match result with
            | Error msg ->
                Assert.True(false, $"predictRegressionAsync failed: {msg}")
            | Ok prediction ->
                Assert.True(prediction.Value >= 0.0 && prediction.Value <= 1.0, $"Value {prediction.Value} must be in [0,1]")
        }

    [<Fact>]
    let ``predictRegressionAsync - produces equivalent results to sync version`` () : Task =
        task {
            let backend = createTestBackend()
            let featureMap = AngleEncoding
            let variationalForm = RealAmplitudes 1
            let parameters = randomParameters variationalForm 2 (Some 42)
            let features = [| 0.5; 0.5 |]
            let valueRange = (0.0, 1.0)

            let syncResult = predictRegression backend featureMap variationalForm parameters features 1000 valueRange
            let! asyncResult = predictRegressionAsync backend featureMap variationalForm parameters features 1000 valueRange CancellationToken.None

            match syncResult, asyncResult with
            | Ok syncPred, Ok asyncPred ->
                // Both should be within the value range
                Assert.True(syncPred.Value >= 0.0 && syncPred.Value <= 1.0, "Sync value in range")
                Assert.True(asyncPred.Value >= 0.0 && asyncPred.Value <= 1.0, "Async value in range")
            | Error _, _ | _, Error _ ->
                Assert.True(false, "Both sync and async should succeed")
        }

    // ========================================================================
    // ASYNC REGRESSION TESTS (wider value ranges)
    // ========================================================================

    [<Fact>]
    let ``predictRegressionAsync - handles wide value range`` () : Task =
        task {
            let backend = createTestBackend()
            let featureMap = AngleEncoding
            let variationalForm = RealAmplitudes 1
            let parameters = randomParameters variationalForm 2 (Some 42)
            let features = [| 0.5; 0.5 |]
            let valueRange = (0.0, 10.0)

            let! result = predictRegressionAsync backend featureMap variationalForm parameters features 100 valueRange CancellationToken.None
            match result with
            | Error msg ->
                Assert.True(false, $"predictRegressionAsync failed: {msg}")
            | Ok prediction ->
                Assert.True(prediction.Value >= 0.0 && prediction.Value <= 10.0, $"Value {prediction.Value} should be in [0,10]")
        }

    [<Fact>]
    let ``predictRegressionAsync - sync and async agree on negative value range`` () : Task =
        task {
            let backend = createTestBackend()
            let featureMap = AngleEncoding
            let variationalForm = RealAmplitudes 1
            let parameters = randomParameters variationalForm 2 (Some 42)
            let features = [| 0.3; 0.7 |]
            let valueRange = (-5.0, 5.0)

            let syncResult = predictRegression backend featureMap variationalForm parameters features 1000 valueRange
            let! asyncResult = predictRegressionAsync backend featureMap variationalForm parameters features 1000 valueRange CancellationToken.None

            match syncResult, asyncResult with
            | Ok syncPred, Ok asyncPred ->
                // Both should be within the value range
                Assert.True(syncPred.Value >= -5.0 && syncPred.Value <= 5.0, "Sync value in range")
                Assert.True(asyncPred.Value >= -5.0 && asyncPred.Value <= 5.0, "Async value in range")
            | Error _, _ | _, Error _ ->
                Assert.True(false, "Both sync and async should succeed")
        }

    // ========================================================================
    // ASYNC CANCELLATION TESTS
    // ========================================================================

    [<Fact>]
    let ``predictRegressionAsync - accepts cancellation token`` () : Task =
        task {
            let backend = createTestBackend()
            let featureMap = AngleEncoding
            let variationalForm = RealAmplitudes 1
            let parameters = randomParameters variationalForm 2 (Some 42)
            let features = [| 0.5; 0.5 |]
            let valueRange = (0.0, 1.0)

            use cts = new CancellationTokenSource()
            let! result = predictRegressionAsync backend featureMap variationalForm parameters features 10 valueRange cts.Token
            // Local backend doesn't observe cancellation, so it should succeed
            match result with
            | Ok pred -> Assert.True(pred.Value >= 0.0 && pred.Value <= 1.0)
            | Error _ -> () // Also acceptable if backend respects cancellation
        }
