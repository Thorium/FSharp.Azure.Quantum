namespace FSharp.Azure.Quantum.Business

open System
open FSharp.Azure.Quantum.Core.BackendAbstraction
open FSharp.Azure.Quantum.MachineLearning

/// Automated Machine Learning Builder - Zero-Config ML
/// 
/// DESIGN PHILOSOPHY:
/// This is the SIMPLEST possible API for machine learning.
/// Just provide your data - AutoML figures out everything else.
/// 
/// WHAT IS AutoML:
/// Automatically tries different model types, architectures, and hyperparameters
/// to find the best model for your data. No ML expertise required.
/// 
/// HOW IT WORKS:
/// 1. Analyzes your data to understand the problem type
/// 2. Tries multiple model architectures (Quantum, Hybrid, Classical)
/// 3. Tests different hyperparameters (learning rate, epochs, etc.)
/// 4. Evaluates all models and picks the best one
/// 5. Returns the winner with a detailed report
/// 
/// USE CASES:
/// - Quick prototyping: "Just give me a working model"
/// - Non-expert users: Don't know which algorithm to use
/// - Baseline comparison: See what's possible with your data
/// - Rapid experimentation: Try everything at once
/// - Model selection: Which approach works best?
/// 
/// EXAMPLE USAGE:
///   // Minimal: Just data
///   let result = autoML {
///       trainWith features labels
///   }
///   
///   match result with
///   | Ok model ->
///       printfn "Best model: %s (%.2f%% accuracy)" 
///           model.BestModelType (model.Score * 100.0)
///       
///       let prediction = model.Predict(newSample)
///   
///   // Advanced: Custom search space
///   let result = autoML {
///       trainWith features labels
///       
///       // What to try
///       tryBinaryClassification true
///       tryMultiClass 3
///       tryAnomalyDetection true
///       
///       // Architectures to test
///       tryArchitectures [Quantum; Hybrid; Classical]
///       
///       // Search budget
///       maxTrials 20
///       maxTimeMinutes 30
///       
///       verbose true
///   }
module AutoML =
    
    // ========================================================================
    // CORE TYPES - AutoML Domain Model
    // ========================================================================
    
    /// Model type that was tried
    type ModelType =
        | BinaryClassification
        | MultiClassClassification of int  // num classes
        | AnomalyDetection
        | Regression
        | SimilaritySearch
    
    /// Architecture for model
    type Architecture =
        | Quantum
        | Hybrid
        | Classical
    
    /// Hyperparameter configuration
    type HyperparameterConfig = {
        LearningRate: float
        MaxEpochs: int
        ConvergenceThreshold: float
        Shots: int
    }
    
    /// Single trial result
    type TrialResult = {
        /// Trial ID
        Id: int
        
        /// Model type tested
        ModelType: ModelType
        
        /// Architecture used
        Architecture: Architecture
        
        /// Hyperparameters used
        Hyperparameters: HyperparameterConfig
        
        /// Validation score (accuracy for classification, R² for regression)
        Score: float
        
        /// Training time
        TrainingTime: TimeSpan
        
        /// Success or failure
        Success: bool
        
        /// Error message (if failed)
        ErrorMessage: string option
    }
    
    /// AutoML search configuration
    type AutoMLProblem = {
        /// Training features (samples × features)
        TrainFeatures: float array array
        
        /// Training labels or targets
        TrainLabels: float array
        
        /// Try binary classification
        TryBinaryClassification: bool
        
        /// Try multi-class classification (None = auto-detect from labels)
        TryMultiClass: int option
        
        /// Try anomaly detection
        TryAnomalyDetection: bool
        
        /// Try regression
        TryRegression: bool
        
        /// Try similarity search
        TrySimilaritySearch: bool
        
        /// Architectures to test
        TryArchitectures: Architecture list
        
        /// Maximum number of trials
        MaxTrials: int
        
        /// Maximum time budget (minutes)
        MaxTimeMinutes: int option
        
        /// Train/validation split ratio
        ValidationSplit: float
        
        /// Quantum backend (None = LocalBackend)
        Backend: IQuantumBackend option
        
        /// Verbose logging
        Verbose: bool
        
        /// Path to save best model
        SavePath: string option
        
        /// Random seed for reproducibility
        RandomSeed: int option
    }
    
    /// AutoML result - best model found
    type AutoMLResult = {
        /// Best model type
        BestModelType: string
        
        /// Best architecture
        BestArchitecture: Architecture
        
        /// Best hyperparameters
        BestHyperparameters: HyperparameterConfig
        
        /// Validation score
        Score: float
        
        /// All trial results
        AllTrials: TrialResult array
        
        /// Total search time
        TotalSearchTime: TimeSpan
        
        /// Number of successful trials
        SuccessfulTrials: int
        
        /// Number of failed trials
        FailedTrials: int
        
        /// Trained model (can be used for predictions)
        Model: obj  // Type-erased for flexibility
        
        /// Model metadata
        Metadata: AutoMLMetadata
    }
    
    and AutoMLMetadata = {
        NumFeatures: int
        NumSamples: int
        CreatedAt: DateTime
        SearchCompleted: DateTime
        Note: string option
    }
    
    /// Prediction result (wrapper for any model type)
    type Prediction =
        | BinaryPrediction of BinaryClassifier.Prediction
        | CategoryPrediction of PredictiveModel.CategoryPrediction
        | RegressionPrediction of PredictiveModel.RegressionPrediction
        | AnomalyPrediction of AnomalyDetector.AnomalyResult
        | SimilarityPrediction of SimilaritySearch.SearchResults<obj>
    
    // ========================================================================
    // VALIDATION
    // ========================================================================
    
    let private validateProblem (problem: AutoMLProblem) : Result<unit, string> =
        if problem.TrainFeatures.Length = 0 then
            Error "Training features cannot be empty"
        elif problem.TrainLabels.Length = 0 then
            Error "Training labels cannot be empty"
        elif problem.TrainFeatures.Length <> problem.TrainLabels.Length then
            Error $"Feature count ({problem.TrainFeatures.Length}) must match label count ({problem.TrainLabels.Length})"
        elif problem.TrainFeatures |> Array.exists (fun f -> f.Length = 0) then
            Error "All feature arrays must have at least one element"
        elif problem.TrainFeatures |> Array.map Array.length |> Array.distinct |> Array.length > 1 then
            Error "All feature arrays must have the same length"
        elif problem.MaxTrials <= 0 then
            Error "MaxTrials must be positive"
        elif problem.ValidationSplit <= 0.0 || problem.ValidationSplit >= 1.0 then
            Error "ValidationSplit must be between 0 and 1"
        elif not (problem.TryBinaryClassification || problem.TryMultiClass.IsSome || 
                  problem.TryAnomalyDetection || problem.TryRegression || problem.TrySimilaritySearch) then
            Error "At least one model type must be enabled"
        elif problem.TryArchitectures.IsEmpty then
            Error "At least one architecture must be enabled"
        else
            Ok ()
    
    // ========================================================================
    // HYPERPARAMETER SEARCH
    // ========================================================================
    
    let private generateHyperparameterConfigs (randomSeed: int option) : HyperparameterConfig array =
        let random = 
            match randomSeed with
            | Some seed -> Random(seed)
            | None -> Random()
        
        // Grid + random search combination
        let gridConfigs = [|
            // Conservative
            { LearningRate = 0.01; MaxEpochs = 50; ConvergenceThreshold = 0.001; Shots = 1000 }
            { LearningRate = 0.01; MaxEpochs = 100; ConvergenceThreshold = 0.001; Shots = 1000 }
            
            // Aggressive
            { LearningRate = 0.05; MaxEpochs = 100; ConvergenceThreshold = 0.0005; Shots = 1000 }
            
            // Fine-tuned
            { LearningRate = 0.005; MaxEpochs = 150; ConvergenceThreshold = 0.0001; Shots = 1000 }
        |]
        
        // Add random configurations
        let randomConfigs = [|
            for i in 1..6 ->
                {
                    LearningRate = 0.001 + random.NextDouble() * 0.049  // [0.001, 0.05]
                    MaxEpochs = 50 + random.Next(150)  // [50, 200]
                    ConvergenceThreshold = 0.0001 + random.NextDouble() * 0.0099  // [0.0001, 0.01]
                    Shots = 1000
                }
        |]
        
        Array.append gridConfigs randomConfigs
    
    // ========================================================================
    // MODEL TRAINING - Try different approaches
    // ========================================================================
    
    let private tryBinaryClassificationModel 
        (trainX: float array array) (trainY: int array)
        (arch: Architecture) (hyperparams: HyperparameterConfig)
        (backend: IQuantumBackend option) : Result<BinaryClassifier.Classifier, string> =
        
        let problem : BinaryClassifier.ClassificationProblem = {
            TrainFeatures = trainX
            TrainLabels = trainY
            Architecture = 
                match arch with
                | Quantum -> BinaryClassifier.Architecture.Quantum
                | Hybrid -> BinaryClassifier.Architecture.Hybrid
                | Classical -> BinaryClassifier.Architecture.Classical
            LearningRate = hyperparams.LearningRate
            MaxEpochs = hyperparams.MaxEpochs
            ConvergenceThreshold = hyperparams.ConvergenceThreshold
            Backend = backend
            Shots = hyperparams.Shots
            Verbose = false
            SavePath = None
            Note = None
        }
        
        BinaryClassifier.train problem
    
    let private tryMultiClassModel
        (trainX: float array array) (trainY: int array) (numClasses: int)
        (arch: Architecture) (hyperparams: HyperparameterConfig)
        (backend: IQuantumBackend option) : Result<PredictiveModel.Model, string> =
        
        let problem : PredictiveModel.PredictionProblem = {
            TrainFeatures = trainX
            TrainTargets = trainY |> Array.map float
            ProblemType = PredictiveModel.ProblemType.MultiClass numClasses
            Architecture = 
                match arch with
                | Quantum -> PredictiveModel.Architecture.Quantum
                | Hybrid -> PredictiveModel.Architecture.Hybrid
                | Classical -> PredictiveModel.Architecture.Classical
            LearningRate = hyperparams.LearningRate
            MaxEpochs = hyperparams.MaxEpochs
            ConvergenceThreshold = hyperparams.ConvergenceThreshold
            Backend = backend
            Shots = hyperparams.Shots
            Verbose = false
            SavePath = None
            Note = None
        }
        
        PredictiveModel.train problem
    
    let private tryRegressionModel
        (trainX: float array array) (trainY: float array)
        (arch: Architecture) (hyperparams: HyperparameterConfig)
        (backend: IQuantumBackend option) : Result<PredictiveModel.Model, string> =
        
        let problem : PredictiveModel.PredictionProblem = {
            TrainFeatures = trainX
            TrainTargets = trainY
            ProblemType = PredictiveModel.ProblemType.Regression
            Architecture = 
                match arch with
                | Quantum -> PredictiveModel.Architecture.Quantum
                | Hybrid -> PredictiveModel.Architecture.Hybrid
                | Classical -> PredictiveModel.Architecture.Classical
            LearningRate = hyperparams.LearningRate
            MaxEpochs = hyperparams.MaxEpochs
            ConvergenceThreshold = hyperparams.ConvergenceThreshold
            Backend = backend
            Shots = hyperparams.Shots
            Verbose = false
            SavePath = None
            Note = None
        }
        
        PredictiveModel.train problem
    
    let private tryAnomalyDetectionModel
        (trainX: float array array)
        (arch: Architecture) (hyperparams: HyperparameterConfig)
        (backend: IQuantumBackend option) : Result<AnomalyDetector.Detector, string> =
        
        let problem : AnomalyDetector.DetectionProblem = {
            NormalData = trainX
            Sensitivity = AnomalyDetector.Sensitivity.Medium
            ContaminationRate = 0.1  // Assume 10% anomalies
            Backend = backend
            Shots = hyperparams.Shots
            Verbose = false
            SavePath = None
            Note = None
        }
        
        AnomalyDetector.train problem
    
    // ========================================================================
    // AUTO ML SEARCH
    // ========================================================================
    
    /// Run AutoML search to find best model
    let search (problem: AutoMLProblem) : Result<AutoMLResult, string> =
        match validateProblem problem with
        | Error e -> Error e
        | Ok () ->
            
            let startTime = DateTime.UtcNow
            let backend = problem.Backend |> Option.defaultValue (LocalBackend() :> IQuantumBackend)
            
            if problem.Verbose then
                printfn "🚀 Starting AutoML Search..."
                printfn $"   Samples: {problem.TrainFeatures.Length}"
                printfn $"   Features: {problem.TrainFeatures.[0].Length}"
                printfn $"   Max Trials: {problem.MaxTrials}"
                printfn $"   Architectures: {problem.TryArchitectures.Length}"
                printfn ""
            
            // Split data into train/validation
            let splitIndex = int (float problem.TrainFeatures.Length * (1.0 - problem.ValidationSplit))
            let trainX = problem.TrainFeatures.[..splitIndex-1]
            let trainY = problem.TrainLabels.[..splitIndex-1]
            let valX = problem.TrainFeatures.[splitIndex..]
            let valY = problem.TrainLabels.[splitIndex..]
            
            if problem.Verbose then
                printfn $"Train/Val Split: {trainX.Length}/{valX.Length} samples\n"
            
            // Generate hyperparameter configurations
            let hyperparamConfigs = generateHyperparameterConfigs problem.RandomSeed
            
            // Build trial list
            let mutable trials = []
            let mutable trialId = 0
            
            // Detect problem type from labels if needed
            let uniqueLabels = problem.TrainLabels |> Array.distinct
            let isLikelyBinary = uniqueLabels.Length = 2
            let isLikelyMultiClass = uniqueLabels.Length > 2 && uniqueLabels.Length <= 10
            let isLikelyRegression = uniqueLabels.Length > 10 || (uniqueLabels |> Array.exists (fun x -> x <> floor x))
            
            // Add trials based on configuration
            for arch in problem.TryArchitectures do
                for hp in hyperparamConfigs |> Array.take (min 3 hyperparamConfigs.Length) do
                    
                    // Binary classification
                    if problem.TryBinaryClassification && isLikelyBinary then
                        trials <- (trialId, BinaryClassification, arch, hp) :: trials
                        trialId <- trialId + 1
                    
                    // Multi-class classification
                    match problem.TryMultiClass with
                    | Some numClasses ->
                        trials <- (trialId, MultiClassClassification numClasses, arch, hp) :: trials
                        trialId <- trialId + 1
                    | None when isLikelyMultiClass ->
                        let numClasses = uniqueLabels.Length
                        trials <- (trialId, MultiClassClassification numClasses, arch, hp) :: trials
                        trialId <- trialId + 1
                    | _ -> ()
                    
                    // Regression
                    if problem.TryRegression && isLikelyRegression then
                        trials <- (trialId, Regression, arch, hp) :: trials
                        trialId <- trialId + 1
                    
                    // Anomaly detection (only normal data needed)
                    if problem.TryAnomalyDetection then
                        trials <- (trialId, AnomalyDetection, arch, hp) :: trials
                        trialId <- trialId + 1
            
            // Limit total trials
            let trials = trials |> List.take (min problem.MaxTrials trials.Length) |> List.toArray
            
            if problem.Verbose then
                printfn $"Generated {trials.Length} trials to execute\n"
            
            // Execute trials
            let mutable results = []
            let mutable bestScore = -1.0
            let mutable bestTrial = None
            let mutable bestModel : obj option = None
            
            for (id, modelType, arch, hp) in trials do
                
                // Check time budget
                let elapsed = DateTime.UtcNow - startTime
                match problem.MaxTimeMinutes with
                | Some maxMinutes when elapsed.TotalMinutes > float maxMinutes ->
                    if problem.Verbose then
                        printfn "⏱️  Time budget exceeded (%.1f > %d minutes)" elapsed.TotalMinutes maxMinutes
                    ()
                | _ ->
                    
                    let trialStart = DateTime.UtcNow
                    
                    if problem.Verbose then
                        printfn $"Trial {id + 1}/{trials.Length}: {modelType} with {arch}..."
                    
                    try
                        match modelType with
                        
                        // Binary Classification
                        | BinaryClassification ->
                            let trainYInt = trainY |> Array.map int
                            let valYInt = valY |> Array.map int
                            
                            match tryBinaryClassificationModel trainX trainYInt arch hp (Some backend) with
                            | Ok model ->
                                // Evaluate on validation set
                                match BinaryClassifier.evaluate valX valYInt model with
                                | Ok metrics ->
                                    let score = metrics.Accuracy
                                    let trialTime = DateTime.UtcNow - trialStart
                                    
                                    if problem.Verbose then
                                        printfn $"  ✅ Score: {score * 100.0:F2}%% (time: {trialTime.TotalSeconds:F1}s)"
                                    
                                    let trialResult = {
                                        Id = id
                                        ModelType = modelType
                                        Architecture = arch
                                        Hyperparameters = hp
                                        Score = score
                                        TrainingTime = trialTime
                                        Success = true
                                        ErrorMessage = None
                                    }
                                    results <- trialResult :: results
                                    
                                    if score > bestScore then
                                        bestScore <- score
                                        bestTrial <- Some trialResult
                                        bestModel <- Some (box model)
                                
                                | Error e ->
                                    if problem.Verbose then
                                        printfn $"  ⚠️  Evaluation failed: {e}"
                                    
                                    results <- {
                                        Id = id; ModelType = modelType; Architecture = arch
                                        Hyperparameters = hp; Score = 0.0
                                        TrainingTime = DateTime.UtcNow - trialStart
                                        Success = false; ErrorMessage = Some e
                                    } :: results
                            
                            | Error e ->
                                if problem.Verbose then
                                    printfn $"  ❌ Training failed: {e}"
                                
                                results <- {
                                    Id = id; ModelType = modelType; Architecture = arch
                                    Hyperparameters = hp; Score = 0.0
                                    TrainingTime = DateTime.UtcNow - trialStart
                                    Success = false; ErrorMessage = Some e
                                } :: results
                        
                        // Multi-Class Classification
                        | MultiClassClassification numClasses ->
                            let trainYInt = trainY |> Array.map int
                            let valYInt = valY |> Array.map int
                            
                            match tryMultiClassModel trainX trainYInt numClasses arch hp (Some backend) with
                            | Ok model ->
                                match PredictiveModel.evaluateMultiClass valX valYInt model with
                                | Ok metrics ->
                                    let score = metrics.Accuracy
                                    let trialTime = DateTime.UtcNow - trialStart
                                    
                                    if problem.Verbose then
                                        printfn $"  ✅ Score: {score * 100.0:F2}%% (time: {trialTime.TotalSeconds:F1}s)"
                                    
                                    let trialResult = {
                                        Id = id; ModelType = modelType; Architecture = arch
                                        Hyperparameters = hp; Score = score
                                        TrainingTime = trialTime; Success = true; ErrorMessage = None
                                    }
                                    results <- trialResult :: results
                                    
                                    if score > bestScore then
                                        bestScore <- score
                                        bestTrial <- Some trialResult
                                        bestModel <- Some (box model)
                                
                                | Error e ->
                                    results <- {
                                        Id = id; ModelType = modelType; Architecture = arch
                                        Hyperparameters = hp; Score = 0.0
                                        TrainingTime = DateTime.UtcNow - trialStart
                                        Success = false; ErrorMessage = Some e
                                    } :: results
                            
                            | Error e ->
                                results <- {
                                    Id = id; ModelType = modelType; Architecture = arch
                                    Hyperparameters = hp; Score = 0.0
                                    TrainingTime = DateTime.UtcNow - trialStart
                                    Success = false; ErrorMessage = Some e
                                } :: results
                        
                        // Regression
                        | Regression ->
                            match tryRegressionModel trainX trainY arch hp (Some backend) with
                            | Ok model ->
                                match PredictiveModel.evaluateRegression valX valY model with
                                | Ok metrics ->
                                    let score = max 0.0 metrics.RSquared  // R² can be negative
                                    let trialTime = DateTime.UtcNow - trialStart
                                    
                                    if problem.Verbose then
                                        printfn $"  ✅ R² Score: {score:F4} (time: {trialTime.TotalSeconds:F1}s)"
                                    
                                    let trialResult = {
                                        Id = id; ModelType = modelType; Architecture = arch
                                        Hyperparameters = hp; Score = score
                                        TrainingTime = trialTime; Success = true; ErrorMessage = None
                                    }
                                    results <- trialResult :: results
                                    
                                    if score > bestScore then
                                        bestScore <- score
                                        bestTrial <- Some trialResult
                                        bestModel <- Some (box model)
                                
                                | Error e ->
                                    results <- {
                                        Id = id; ModelType = modelType; Architecture = arch
                                        Hyperparameters = hp; Score = 0.0
                                        TrainingTime = DateTime.UtcNow - trialStart
                                        Success = false; ErrorMessage = Some e
                                    } :: results
                            
                            | Error e ->
                                results <- {
                                    Id = id; ModelType = modelType; Architecture = arch
                                    Hyperparameters = hp; Score = 0.0
                                    TrainingTime = DateTime.UtcNow - trialStart
                                    Success = false; ErrorMessage = Some e
                                } :: results
                        
                        // Anomaly Detection
                        | AnomalyDetection ->
                            // For anomaly detection, use all normal data for training
                            let normalData = trainX  // Assume training data is mostly normal
                            
                            match tryAnomalyDetectionModel normalData arch hp (Some backend) with
                            | Ok detector ->
                                // Simple evaluation: predict on validation set
                                // (In production, would need labeled anomalies for proper eval)
                                let predictions = 
                                    valX |> Array.choose (fun x ->
                                        match AnomalyDetector.check x detector with
                                        | Ok pred -> Some (if pred.IsAnomaly then 1.0 else 0.0)
                                        | Error _ -> None
                                    )
                                
                                let score = 
                                    if predictions.Length > 0 then
                                        // Heuristic: good model finds 5-15% anomalies
                                        let anomalyRate = predictions |> Array.average
                                        if anomalyRate >= 0.05 && anomalyRate <= 0.15 then 0.8
                                        else 0.5
                                    else 0.0
                                
                                let trialTime = DateTime.UtcNow - trialStart
                                
                                if problem.Verbose then
                                    printfn $"  ✅ Heuristic Score: {score:F2} (time: {trialTime.TotalSeconds:F1}s)"
                                
                                let trialResult = {
                                    Id = id; ModelType = modelType; Architecture = arch
                                    Hyperparameters = hp; Score = score
                                    TrainingTime = trialTime; Success = true; ErrorMessage = None
                                }
                                results <- trialResult :: results
                                
                                if score > bestScore then
                                    bestScore <- score
                                    bestTrial <- Some trialResult
                                    bestModel <- Some (box detector)
                            
                            | Error e ->
                                results <- {
                                    Id = id; ModelType = modelType; Architecture = arch
                                    Hyperparameters = hp; Score = 0.0
                                    TrainingTime = DateTime.UtcNow - trialStart
                                    Success = false; ErrorMessage = Some e
                                } :: results
                        
                        | _ ->
                            ()  // Similarity search not yet implemented in AutoML
                    
                    with ex ->
                        if problem.Verbose then
                            printfn $"  ❌ Exception: {ex.Message}"
                        
                        results <- {
                            Id = id; ModelType = modelType; Architecture = arch
                            Hyperparameters = hp; Score = 0.0
                            TrainingTime = DateTime.UtcNow - trialStart
                            Success = false; ErrorMessage = Some ex.Message
                        } :: results
            
            let totalTime = DateTime.UtcNow - startTime
            
            // Return result
            match bestTrial, bestModel with
            | Some trial, Some model ->
                let modelTypeStr = 
                    match trial.ModelType with
                    | BinaryClassification -> "Binary Classification"
                    | MultiClassClassification n -> $"Multi-Class Classification ({n} classes)"
                    | Regression -> "Regression"
                    | AnomalyDetection -> "Anomaly Detection"
                    | SimilaritySearch -> "Similarity Search"
                
                let result = {
                    BestModelType = modelTypeStr
                    BestArchitecture = trial.Architecture
                    BestHyperparameters = trial.Hyperparameters
                    Score = trial.Score
                    AllTrials = results |> List.rev |> List.toArray
                    TotalSearchTime = totalTime
                    SuccessfulTrials = results |> List.filter (fun r -> r.Success) |> List.length
                    FailedTrials = results |> List.filter (fun r -> not r.Success) |> List.length
                    Model = model
                    Metadata = {
                        NumFeatures = problem.TrainFeatures.[0].Length
                        NumSamples = problem.TrainFeatures.Length
                        CreatedAt = startTime
                        SearchCompleted = DateTime.UtcNow
                        Note = None
                    }
                }
                
                if problem.Verbose then
                    printfn ""
                    printfn "✅ AutoML Search Complete!"
                    printfn $"   Best Model: {result.BestModelType}"
                    printfn $"   Best Architecture: {result.BestArchitecture}"
                    printfn $"   Best Score: {result.Score * 100.0:F2}%%"
                    printfn $"   Successful Trials: {result.SuccessfulTrials}/{results.Length}"
                    printfn $"   Total Time: {result.TotalSearchTime.TotalSeconds:F1}s"
                
                Ok result
            
            | _ ->
                Error "All trials failed - no model could be trained successfully"
    
    // ========================================================================
    // PREDICTION - Use best model
    // ========================================================================
    
    /// Predict with AutoML result (wrapper for underlying model)
    let predict (features: float array) (result: AutoMLResult) : Result<Prediction, string> =
        try
            match result.BestModelType with
            | name when name.StartsWith("Binary") ->
                let model = unbox<BinaryClassifier.Classifier> result.Model
                match BinaryClassifier.predict features model with
                | Ok pred -> Ok (BinaryPrediction pred)
                | Error e -> Error e
            
            | name when name.StartsWith("Multi-Class") ->
                let model = unbox<PredictiveModel.Model> result.Model
                match PredictiveModel.predictCategory features model with
                | Ok pred -> Ok (CategoryPrediction pred)
                | Error e -> Error e
            
            | "Regression" ->
                let model = unbox<PredictiveModel.Model> result.Model
                match PredictiveModel.predict features model with
                | Ok pred -> Ok (RegressionPrediction pred)
                | Error e -> Error e
            
            | "Anomaly Detection" ->
                let detector = unbox<AnomalyDetector.Detector> result.Model
                match AnomalyDetector.check features detector with
                | Ok pred -> Ok (AnomalyPrediction pred)
                | Error e -> Error e
            
            | _ ->
                Error $"Unsupported model type: {result.BestModelType}"
        with ex ->
            Error $"Prediction failed: {ex.Message}"
    
    // ========================================================================
    // COMPUTATION EXPRESSION BUILDER
    // ========================================================================
    
    /// Computation expression builder for AutoML
    type AutoMLBuilder() =
        
        member _.Yield(_) : AutoMLProblem =
            {
                TrainFeatures = [||]
                TrainLabels = [||]
                TryBinaryClassification = true
                TryMultiClass = None  // Auto-detect
                TryAnomalyDetection = true
                TryRegression = true
                TrySimilaritySearch = false  // Expensive
                TryArchitectures = [Quantum; Hybrid]  // Classical kept internal but not default
                MaxTrials = 20
                MaxTimeMinutes = None
                ValidationSplit = 0.2
                Backend = None
                Verbose = false
                SavePath = None
                RandomSeed = None
            }
        
        member _.Delay(f: unit -> AutoMLProblem) = f
        
        member _.Run(f: unit -> AutoMLProblem) : Result<AutoMLResult, string> =
            let problem = f()
            search problem
        
        member _.Combine(p1: AutoMLProblem, p2: AutoMLProblem) =
            { p2 with 
                TrainFeatures = if p2.TrainFeatures.Length = 0 then p1.TrainFeatures else p2.TrainFeatures
                TrainLabels = if p2.TrainLabels.Length = 0 then p1.TrainLabels else p2.TrainLabels
            }
        
        member _.Zero() : AutoMLProblem =
            {
                TrainFeatures = [||]
                TrainLabels = [||]
                TryBinaryClassification = true
                TryMultiClass = None
                TryAnomalyDetection = true
                TryRegression = true
                TrySimilaritySearch = false
                TryArchitectures = [Quantum; Hybrid]  // Classical kept internal but not default
                MaxTrials = 20
                MaxTimeMinutes = None
                ValidationSplit = 0.2
                Backend = None
                Verbose = false
                SavePath = None
                RandomSeed = None
            }
        
        [<CustomOperation("trainWith")>]
        member _.TrainWith(problem: AutoMLProblem, features: float array array, labels: float array) =
            { problem with TrainFeatures = features; TrainLabels = labels }
        
        [<CustomOperation("tryBinaryClassification")>]
        member _.TryBinaryClassification(problem: AutoMLProblem, enable: bool) =
            { problem with TryBinaryClassification = enable }
        
        [<CustomOperation("tryMultiClass")>]
        member _.TryMultiClass(problem: AutoMLProblem, numClasses: int) =
            { problem with TryMultiClass = Some numClasses }
        
        [<CustomOperation("tryAnomalyDetection")>]
        member _.TryAnomalyDetection(problem: AutoMLProblem, enable: bool) =
            { problem with TryAnomalyDetection = enable }
        
        [<CustomOperation("tryRegression")>]
        member _.TryRegression(problem: AutoMLProblem, enable: bool) =
            { problem with TryRegression = enable }
        
        [<CustomOperation("trySimilaritySearch")>]
        member _.TrySimilaritySearch(problem: AutoMLProblem, enable: bool) =
            { problem with TrySimilaritySearch = enable }
        
        [<CustomOperation("tryArchitectures")>]
        member _.TryArchitectures(problem: AutoMLProblem, architectures: Architecture list) =
            { problem with TryArchitectures = architectures }
        
        [<CustomOperation("maxTrials")>]
        member _.MaxTrials(problem: AutoMLProblem, trials: int) =
            { problem with MaxTrials = trials }
        
        [<CustomOperation("maxTimeMinutes")>]
        member _.MaxTimeMinutes(problem: AutoMLProblem, minutes: int) =
            { problem with MaxTimeMinutes = Some minutes }
        
        [<CustomOperation("validationSplit")>]
        member _.ValidationSplit(problem: AutoMLProblem, split: float) =
            { problem with ValidationSplit = split }
        
        [<CustomOperation("backend")>]
        member _.Backend(problem: AutoMLProblem, backend: IQuantumBackend) =
            { problem with Backend = Some backend }
        
        [<CustomOperation("verbose")>]
        member _.Verbose(problem: AutoMLProblem, verbose: bool) =
            { problem with Verbose = verbose }
        
        [<CustomOperation("saveModelTo")>]
        member _.SaveModelTo(problem: AutoMLProblem, path: string) =
            { problem with SavePath = Some path }
        
        [<CustomOperation("randomSeed")>]
        member _.RandomSeed(problem: AutoMLProblem, seed: int) =
            { problem with RandomSeed = Some seed }
    
    /// Create AutoML computation expression
    let autoML = AutoMLBuilder()
