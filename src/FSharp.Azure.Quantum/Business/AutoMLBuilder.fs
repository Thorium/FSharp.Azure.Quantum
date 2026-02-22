namespace FSharp.Azure.Quantum.Business

open FSharp.Azure.Quantum.Backends
open FSharp.Azure.Quantum.Core
open System
open FSharp.Azure.Quantum.Backends
open FSharp.Azure.Quantum.Core.BackendAbstraction
open FSharp.Azure.Quantum.Backends
open FSharp.Azure.Quantum.MachineLearning
open FSharp.Azure.Quantum.Backends
open FSharp.Azure.Quantum
open Microsoft.Extensions.Logging

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
        
        /// Optional structured logger. When provided, verbose output is sent to this
        /// ILogger instead of being discarded.
        Logger: ILogger option
        
        /// Path to save best model
        SavePath: string option
        
        /// Random seed for reproducibility
        RandomSeed: int option
        
        /// Optional progress reporter for real-time updates
        ProgressReporter: Core.Progress.IProgressReporter option
        
        /// Optional cancellation token for early termination
        CancellationToken: System.Threading.CancellationToken option
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
    
    let private validateProblem (problem: AutoMLProblem) : QuantumResult<unit> =
        if problem.TrainFeatures.Length = 0 then
            Error (QuantumError.ValidationError ("Input", "Training features cannot be empty"))
        elif problem.TrainLabels.Length = 0 then
            Error (QuantumError.ValidationError ("Input", "Training labels cannot be empty"))
        elif problem.TrainFeatures.Length <> problem.TrainLabels.Length then
            Error (QuantumError.ValidationError ("Input", $"Feature count ({problem.TrainFeatures.Length}) must match label count ({problem.TrainLabels.Length})"))
        elif problem.TrainFeatures |> Array.exists (fun f -> f.Length = 0) then
            Error (QuantumError.ValidationError ("Input", "All feature arrays must have at least one element"))
        elif problem.TrainFeatures |> Array.map Array.length |> Array.distinct |> Array.length > 1 then
            Error (QuantumError.ValidationError ("Input", "All feature arrays must have the same length"))
        elif problem.MaxTrials <= 0 then
            Error (QuantumError.ValidationError ("Input", "MaxTrials must be positive"))
        elif problem.ValidationSplit <= 0.0 || problem.ValidationSplit >= 1.0 then
            Error (QuantumError.ValidationError ("Input", "ValidationSplit must be between 0 and 1"))
        elif not (problem.TryBinaryClassification || problem.TryMultiClass.IsSome || 
                  problem.TryAnomalyDetection || problem.TryRegression || problem.TrySimilaritySearch) then
            Error (QuantumError.ValidationError ("Input", "At least one model type must be enabled"))
        elif problem.TryArchitectures.IsEmpty then
            Error (QuantumError.ValidationError ("Input", "At least one architecture must be enabled"))
        else
            Ok ()
    
    // ========================================================================
    // HYPERPARAMETER SEARCH
    // ========================================================================
    
    let private generateHyperparameterConfigs (randomSeed: int option) : HyperparameterConfig list =
        let random = 
            randomSeed
            |> Option.map Random
            |> Option.defaultWith (fun () -> Random())
        
        // Grid + random search combination
        let gridConfigs = [
            // Conservative
            { LearningRate = 0.01; MaxEpochs = 50; ConvergenceThreshold = 0.001; Shots = 1000 }
            { LearningRate = 0.01; MaxEpochs = 100; ConvergenceThreshold = 0.001; Shots = 1000 }
            
            // Aggressive
            { LearningRate = 0.05; MaxEpochs = 100; ConvergenceThreshold = 0.0005; Shots = 1000 }
            
            // Fine-tuned
            { LearningRate = 0.005; MaxEpochs = 150; ConvergenceThreshold = 0.0001; Shots = 1000 }
        ]
        
        // Add random configurations
        let randomConfigs =
            List.init 6 (fun _ ->
                {
                    LearningRate = 0.001 + random.NextDouble() * 0.049  // [0.001, 0.05]
                    MaxEpochs = 50 + random.Next(150)  // [50, 200]
                    ConvergenceThreshold = 0.0001 + random.NextDouble() * 0.0099  // [0.0001, 0.01]
                    Shots = 1000
                })
        
        gridConfigs @ randomConfigs
    
    // ========================================================================
    // MODEL TRAINING - Try different approaches
    // ========================================================================
    
    let private tryBinaryClassificationModel 
        (trainX: float array array) (trainY: int array)
        (arch: Architecture) (hyperparams: HyperparameterConfig)
        (backend: IQuantumBackend option) : QuantumResult<BinaryClassifier.Classifier> =
        
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
            ProgressReporter = None
            CancellationToken = None
            Logger = None
        }
        
        BinaryClassifier.train problem
    
    let private tryMultiClassModel
        (trainX: float array array) (trainY: int array) (numClasses: int)
        (arch: Architecture) (hyperparams: HyperparameterConfig)
        (backend: IQuantumBackend option) : QuantumResult<PredictiveModel.Model> =
        
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
            Logger = None
            SavePath = None
            Note = None
            ProgressReporter = None
            CancellationToken = None
        }
        
        PredictiveModel.train problem
    
    let private tryRegressionModel
        (trainX: float array array) (trainY: float array)
        (arch: Architecture) (hyperparams: HyperparameterConfig)
        (backend: IQuantumBackend option) : QuantumResult<PredictiveModel.Model> =
        
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
            Logger = None
            SavePath = None
            Note = None
            ProgressReporter = None
            CancellationToken = None
        }
        
        PredictiveModel.train problem
    
    let private tryAnomalyDetectionModel
        (trainX: float array array)
        (arch: Architecture) (hyperparams: HyperparameterConfig)
        (backend: IQuantumBackend option) : QuantumResult<AnomalyDetector.Detector> =
        
        let problem : AnomalyDetector.DetectionProblem = {
            NormalData = trainX
            Sensitivity = AnomalyDetector.Sensitivity.Medium
            ContaminationRate = 0.1  // Assume 10% anomalies
            Backend = backend
            Shots = hyperparams.Shots
            Verbose = false
            Logger = None
            SavePath = None
            Note = None
            ProgressReporter = None
            CancellationToken = None
        }
        
        AnomalyDetector.train problem
    
    let private trySimilaritySearchModel
        (trainX: float array array)
        (hyperparams: HyperparameterConfig)
        (backend: IQuantumBackend option) : QuantumResult<SimilaritySearch.SearchIndex<obj>> =
        
        // Create indexed items (boxed item index, features)
        let items = trainX |> Array.mapi (fun i features -> (box i, features))
        
        let problem : SimilaritySearch.SearchProblem<obj> = {
            Items = items
            Metric = SimilaritySearch.SimilarityMetric.Cosine  // Use Cosine for reliability
            Threshold = 0.5
            Backend = backend
            Shots = hyperparams.Shots
            Verbose = false
            SavePath = None
            Note = None
            ProgressReporter = None
            CancellationToken = None
            Logger = None
        }
        
        SimilaritySearch.build problem
    
    // ========================================================================
    // TRIAL GENERATION
    // ========================================================================
    
    type private TrialSpec = {
        Id: int
        ModelType: ModelType
        Architecture: Architecture
        Hyperparameters: HyperparameterConfig
    }
    
    let private detectProblemTypes (labels: float array) =
        let uniqueLabels = labels |> Array.distinct
        {|
            IsLikelyBinary = uniqueLabels.Length = 2
            IsLikelyMultiClass = uniqueLabels.Length > 2 && uniqueLabels.Length <= 10
            IsLikelyRegression = uniqueLabels.Length > 10 || (uniqueLabels |> Array.exists (fun x -> x <> floor x))
            NumClasses = uniqueLabels.Length
        |}
    
    let private generateTrials (problem: AutoMLProblem) (hyperparamConfigs: HyperparameterConfig list) : TrialSpec list =
        let problemTypes = detectProblemTypes problem.TrainLabels
        let hpSample = hyperparamConfigs |> List.truncate 3
        
        problem.TryArchitectures
        |> List.collect (fun arch ->
            hpSample
            |> List.collect (fun hp ->
                [
                    // Binary classification
                    if problem.TryBinaryClassification && problemTypes.IsLikelyBinary then
                        Some (BinaryClassification, arch, hp)
                    else
                        None
                    
                    // Multi-class classification
                    match problem.TryMultiClass with
                    | Some numClasses ->
                        Some (MultiClassClassification numClasses, arch, hp)
                    | None when problemTypes.IsLikelyMultiClass ->
                        Some (MultiClassClassification problemTypes.NumClasses, arch, hp)
                    | _ ->
                        None
                    
                    // Regression
                    if problem.TryRegression && problemTypes.IsLikelyRegression then
                        Some (Regression, arch, hp)
                    else
                        None
                    
                    // Anomaly detection
                    if problem.TryAnomalyDetection then
                        Some (AnomalyDetection, arch, hp)
                    else
                        None
                    
                    // Similarity search
                    if problem.TrySimilaritySearch then
                        Some (SimilaritySearch, arch, hp)
                    else
                        None
                ]
                |> List.choose id
                |> List.map (fun (modelType, arch, hp) -> (modelType, arch, hp))))
        |> List.mapi (fun i (modelType, arch, hp) ->
            { Id = i; ModelType = modelType; Architecture = arch; Hyperparameters = hp })
        |> List.truncate problem.MaxTrials
    
    // ========================================================================
    // AUTO ML SEARCH
    // ========================================================================
    
    /// Run AutoML search to find best model
    [<System.Obsolete("Uses Async.Parallel |> Async.RunSynchronously internally. Use searchAsync for non-blocking parallelization.")>]
    let search (problem: AutoMLProblem) : QuantumResult<AutoMLResult> =
        validateProblem problem
        |> Result.bind (fun () ->
            
            let startTime = DateTime.UtcNow
            let backend = problem.Backend |> Option.defaultValue (LocalBackend.LocalBackend() :> IQuantumBackend)
            let reporter = problem.ProgressReporter
            
            // Report initial phase
            reporter |> Option.iter (fun r ->
                r.Report(Core.Progress.PhaseChanged("AutoML Search", Some "Initializing search")))
            
            if problem.Verbose then
                logInfo problem.Logger "[Start] Starting AutoML Search..."
                logInfo problem.Logger $"   Samples: {problem.TrainFeatures.Length}"
                logInfo problem.Logger $"   Features: {problem.TrainFeatures.[0].Length}"
                logInfo problem.Logger $"   Max Trials: {problem.MaxTrials}"
                logInfo problem.Logger $"   Architectures: {problem.TryArchitectures.Length}"
                logInfo problem.Logger ""
            
            // Split data into train/validation
            let splitIndex = int (float problem.TrainFeatures.Length * (1.0 - problem.ValidationSplit))
            let trainX = problem.TrainFeatures.[..splitIndex-1]
            let trainY = problem.TrainLabels.[..splitIndex-1]
            let valX = problem.TrainFeatures.[splitIndex..]
            let valY = problem.TrainLabels.[splitIndex..]
            
            if problem.Verbose then
                logInfo problem.Logger $"Train/Val Split: {trainX.Length}/{valX.Length} samples\n"
            
            // Generate hyperparameter configurations and trials
            let hyperparamConfigs = generateHyperparameterConfigs problem.RandomSeed
            let trials = generateTrials problem hyperparamConfigs
            
            if problem.Verbose then
                logInfo problem.Logger $"Generated {trials.Length} trials to execute\n"
            
            // Check if time budget exceeded
            let isTimeBudgetExceeded () =
                problem.MaxTimeMinutes
                |> Option.map (fun maxMinutes -> 
                    (DateTime.UtcNow - startTime).TotalMinutes > float maxMinutes)
                |> Option.defaultValue false
            
            // Check if cancellation requested
            let isCancellationRequested () =
                match problem.CancellationToken with
                | Some token when token.IsCancellationRequested -> true
                | _ ->
                    reporter |> Option.map (fun r -> r.IsCancellationRequested) |> Option.defaultValue false
            
            // Execute a single trial and return result with trained model
            let executeTrial (trial: TrialSpec) : (TrialResult * obj option) option =
                // Check cancellation first
                if isCancellationRequested() then
                    if problem.Verbose then
                        logInfo problem.Logger "[Stop] Search cancelled by user"
                    reporter |> Option.iter (fun r ->
                        r.Report(Core.Progress.ProgressUpdate(0.0, "Search cancelled by user")))
                    None
                elif isTimeBudgetExceeded() then
                    if problem.Verbose then
                        let elapsed = (DateTime.UtcNow - startTime).TotalMinutes
                        logInfo problem.Logger $"[Timeout] Time budget exceeded ({elapsed:F1} minutes)"
                    None
                else
                    let trialStart = DateTime.UtcNow
                    
                    // Report trial start
                    let modelTypeStr = sprintf "%A" trial.ModelType
                    reporter |> Option.iter (fun r ->
                        r.Report(Core.Progress.TrialStarted(trial.Id + 1, trials.Length, modelTypeStr)))
                    
                    if problem.Verbose then
                        logInfo problem.Logger $"Trial {trial.Id + 1}/{List.length trials}: {trial.ModelType} with {trial.Architecture}..."
                    
                    let createFailureResult errorMsg =
                        ({
                            Id = trial.Id
                            ModelType = trial.ModelType
                            Architecture = trial.Architecture
                            Hyperparameters = trial.Hyperparameters
                            Score = 0.0
                            TrainingTime = DateTime.UtcNow - trialStart
                            Success = false
                            ErrorMessage = Some errorMsg
                        }, None)
                    
                    let createSuccessResult score model =
                        ({
                            Id = trial.Id
                            ModelType = trial.ModelType
                            Architecture = trial.Architecture
                            Hyperparameters = trial.Hyperparameters
                            Score = score
                            TrainingTime = DateTime.UtcNow - trialStart
                            Success = true
                            ErrorMessage = None
                        }, Some (box model))
                    
                    let result =
                        try
                            match trial.ModelType with
                            
                            // Binary Classification
                            | BinaryClassification ->
                                let trainYInt = trainY |> Array.map int
                                let valYInt = valY |> Array.map int
                                
                                tryBinaryClassificationModel trainX trainYInt trial.Architecture trial.Hyperparameters (Some backend)
                                |> Result.bind (fun model ->
                                    BinaryClassifier.evaluate valX valYInt model
                                     |> Result.map (fun metrics ->
                                         let score = metrics.Accuracy
                                         let elapsed = (DateTime.UtcNow - trialStart).TotalSeconds
                                         if problem.Verbose then
                                              logInfo problem.Logger $"  [OK] Score: {score * 100.0:F2}%% (time: {elapsed:F1}s)"
                                         
                                         // Report trial completion
                                         reporter |> Option.iter (fun r ->
                                             r.Report(Core.Progress.TrialCompleted(trial.Id + 1, score, elapsed)))
                                         
                                         (score, model)))
                                |> Result.map (fun (score, model) -> createSuccessResult score model)
                                |> Result.orElseWith (fun e ->
                                    if problem.Verbose then
                                        logWarning problem.Logger $"  [FAIL] Failed: {e}"
                                    
                                    // Report trial failure
                                    reporter |> Option.iter (fun r ->
                                        r.Report(Core.Progress.TrialFailed(trial.Id + 1, e.Message)))
                                    
                                    Ok (createFailureResult e.Message))
                            
                            // Multi-Class Classification
                            | MultiClassClassification numClasses ->
                                let trainYInt = trainY |> Array.map int
                                let valYInt = valY |> Array.map int
                                
                                tryMultiClassModel trainX trainYInt numClasses trial.Architecture trial.Hyperparameters (Some backend)
                                |> Result.bind (fun model ->
                                    PredictiveModel.evaluateMultiClass valX valYInt model
                                    |> Result.map (fun metrics ->
                                        let score = metrics.Accuracy
                                        if problem.Verbose then
                                            logInfo problem.Logger $"  [OK] Score: {score * 100.0:F2}%% (time: {(DateTime.UtcNow - trialStart).TotalSeconds:F1}s)"
                                        (score, model)))
                                |> Result.map (fun (score, model) -> createSuccessResult score model)
                                |> Result.orElseWith (fun e ->
                                    if problem.Verbose then
                                        logWarning problem.Logger $"  [FAIL] Failed: {e}"
                                    Ok (createFailureResult e.Message))
                            
                            // Regression
                            | Regression ->
                                tryRegressionModel trainX trainY trial.Architecture trial.Hyperparameters (Some backend)
                                |> Result.bind (fun model ->
                                    PredictiveModel.evaluateRegression valX valY model
                                    |> Result.map (fun metrics ->
                                        let score = max 0.0 metrics.RSquared  // R² can be negative
                                        if problem.Verbose then
                                            logInfo problem.Logger $"  [OK] R2 Score: {score:F4} (time: {(DateTime.UtcNow - trialStart).TotalSeconds:F1}s)"
                                        (score, model)))
                                |> Result.map (fun (score, model) -> createSuccessResult score model)
                                |> Result.orElseWith (fun e ->
                                    if problem.Verbose then
                                        logWarning problem.Logger $"  [FAIL] Failed: {e}"
                                    Ok (createFailureResult e.Message))
                            
                            // Anomaly Detection
                            | AnomalyDetection ->
                                // For anomaly detection, use all normal data for training
                                let normalData = trainX  // Assume training data is mostly normal
                                
                                tryAnomalyDetectionModel normalData trial.Architecture trial.Hyperparameters (Some backend)
                                |> Result.map (fun detector ->
                                    // Simple evaluation: predict on validation set
                                    let predictions = 
                                        valX 
                                        |> Array.choose (fun x ->
                                            AnomalyDetector.check x detector
                                            |> Result.toOption
                                            |> Option.map (fun pred -> if pred.IsAnomaly then 1.0 else 0.0))
                                    
                                    let score = 
                                        if predictions.Length > 0 then
                                            // Heuristic: good model finds 5-15% anomalies
                                            let anomalyRate = predictions |> Array.average
                                            if anomalyRate >= 0.05 && anomalyRate <= 0.15 then 0.8 else 0.5
                                        else 0.0
                                    
                                    if problem.Verbose then
                                        logInfo problem.Logger $"  [OK] Heuristic Score: {score:F2} (time: {(DateTime.UtcNow - trialStart).TotalSeconds:F1}s)"
                                    
                                    (score, detector))
                                |> Result.map (fun (score, detector) -> createSuccessResult score detector)
                                |> Result.orElseWith (fun e ->
                                    if problem.Verbose then
                                        logWarning problem.Logger $"  [FAIL] Failed: {e}"
                                    Ok (createFailureResult e.Message))
                            
                            // Similarity Search
                            | SimilaritySearch ->
                                trySimilaritySearchModel trainX trial.Hyperparameters (Some backend)
                                |> Result.map (fun searchIndex ->
                                    // Evaluate: test similarity search quality
                                    // Score based on successful index building
                                    let score = 
                                        if searchIndex.Items.Length >= 2 then
                                            // Successfully built index with multiple items
                                            0.7
                                        else
                                            // Index too small
                                            0.3
                                    
                                    if problem.Verbose then
                                        logInfo problem.Logger $"  [OK] Search Quality Score: {score:F2} (time: {(DateTime.UtcNow - trialStart).TotalSeconds:F1}s)"
                                    
                                    (score, searchIndex))
                                |> Result.map (fun (score, searchIndex) -> createSuccessResult score searchIndex)
                                |> Result.orElseWith (fun e ->
                                    if problem.Verbose then
                                        logWarning problem.Logger $"  [FAIL] Failed: {e}"
                                    Ok (createFailureResult e.Message))
                            
                        with ex ->
                            if problem.Verbose then
                                logError problem.Logger $"  [ERROR] Exception: {ex.Message}"
                            Ok (createFailureResult ex.Message)
                    
                    match result with
                    | Ok resultTuple -> Some resultTuple
                    | Error e -> None
            
            // 🚀 PARALLELIZED: Execute trials in parallel with controlled concurrency
            // Use maxDegreeOfParallelism to avoid overwhelming the system
            // Still respects cancellation and time budget per trial
            let maxDegreeOfParallelism = min 4 (trials.Length / 2 |> max 1)
            
            let resultsWithModels =
                trials
                |> List.chunkBySize maxDegreeOfParallelism
                |> List.collect (fun batch ->
                    // Execute each batch in parallel
                    batch
                    |> List.map (fun trial -> async { return executeTrial trial })
                    |> Async.Parallel
                    |> Async.RunSynchronously
                    |> Array.choose id
                    |> Array.toList)
            
            let results = resultsWithModels |> List.map fst
            
            let totalTime = DateTime.UtcNow - startTime
            
            // Find best result
            let bestResultWithModel =
                resultsWithModels
                |> List.filter (fun (r, _) -> r.Success)
                |> List.sortByDescending (fun (r, _) -> r.Score)
                |> List.tryHead
            
            // Build and return final result
            match bestResultWithModel with
            | Some (bestTrial, Some bestModel) ->
                let modelTypeStr = 
                    match bestTrial.ModelType with
                    | BinaryClassification -> "Binary Classification"
                    | MultiClassClassification n -> $"Multi-Class Classification ({n} classes)"
                    | Regression -> "Regression"
                    | AnomalyDetection -> "Anomaly Detection"
                    | SimilaritySearch -> "Similarity Search"
                
                let successfulTrials = results |> List.filter (fun r -> r.Success) |> List.length
                let failedTrials = results |> List.filter (fun r -> not r.Success) |> List.length
                
                let result = {
                    BestModelType = modelTypeStr
                    BestArchitecture = bestTrial.Architecture
                    BestHyperparameters = bestTrial.Hyperparameters
                    Score = bestTrial.Score
                    AllTrials = results |> List.toArray
                    TotalSearchTime = totalTime
                    SuccessfulTrials = successfulTrials
                    FailedTrials = failedTrials
                    Model = bestModel
                    Metadata = {
                        NumFeatures = problem.TrainFeatures.[0].Length
                        NumSamples = problem.TrainFeatures.Length
                        CreatedAt = startTime
                        SearchCompleted = DateTime.UtcNow
                        Note = None
                    }
                }
                
                if problem.Verbose then
                    logInfo problem.Logger ""
                    logInfo problem.Logger "[OK] AutoML Search Complete!"
                    logInfo problem.Logger $"   Best Model: {result.BestModelType}"
                    logInfo problem.Logger $"   Best Architecture: {result.BestArchitecture}"
                    logInfo problem.Logger $"   Best Score: {result.Score * 100.0:F2}%%"
                    logInfo problem.Logger $"   Successful Trials: {result.SuccessfulTrials}/{results.Length}"
                    logInfo problem.Logger $"   Total Time: {result.TotalSearchTime.TotalSeconds:F1}s"
                
                Ok result
            
            | _ ->
                Error (QuantumError.OperationError ("Operation", "All trials failed - no model could be trained successfully")))

    /// Run AutoML search to find best model (task-based, non-blocking parallelization).
    ///
    /// Uses Task.WhenAll + Task.Run for CPU-bound trial batches instead of
    /// Async.Parallel |> Async.RunSynchronously, making it safe to call from
    /// an async/task context without deadlock risk.
    let searchAsync (problem: AutoMLProblem) (cancellationToken: System.Threading.CancellationToken) : System.Threading.Tasks.Task<QuantumResult<AutoMLResult>> =
        task {
            // Merge explicit CancellationToken with any token on the problem
            let problemWithToken =
                match problem.CancellationToken with
                | Some _ -> problem
                | None -> { problem with CancellationToken = Some cancellationToken }

            return
                validateProblem problemWithToken
                |> Result.bind (fun () ->

                    let startTime = DateTime.UtcNow
                    let backend = problemWithToken.Backend |> Option.defaultValue (LocalBackend.LocalBackend() :> IQuantumBackend)
                    let reporter = problemWithToken.ProgressReporter

                    reporter |> Option.iter (fun r ->
                        r.Report(Core.Progress.PhaseChanged("AutoML Search", Some "Initializing search")))

                    if problemWithToken.Verbose then
                        logInfo problemWithToken.Logger "[Start] Starting AutoML Search (async)..."
                        logInfo problemWithToken.Logger $"   Samples: {problemWithToken.TrainFeatures.Length}"
                        logInfo problemWithToken.Logger $"   Features: {problemWithToken.TrainFeatures.[0].Length}"
                        logInfo problemWithToken.Logger $"   Max Trials: {problemWithToken.MaxTrials}"
                        logInfo problemWithToken.Logger $"   Architectures: {problemWithToken.TryArchitectures.Length}"
                        logInfo problemWithToken.Logger ""

                    let splitIndex = int (float problemWithToken.TrainFeatures.Length * (1.0 - problemWithToken.ValidationSplit))
                    let trainX = problemWithToken.TrainFeatures.[..splitIndex-1]
                    let trainY = problemWithToken.TrainLabels.[..splitIndex-1]
                    let valX = problemWithToken.TrainFeatures.[splitIndex..]
                    let valY = problemWithToken.TrainLabels.[splitIndex..]

                    if problemWithToken.Verbose then
                        logInfo problemWithToken.Logger $"Train/Val Split: {trainX.Length}/{valX.Length} samples\n"

                    let hyperparamConfigs = generateHyperparameterConfigs problemWithToken.RandomSeed
                    let trials = generateTrials problemWithToken hyperparamConfigs

                    if problemWithToken.Verbose then
                        logInfo problemWithToken.Logger $"Generated {trials.Length} trials to execute\n"

                    let isTimeBudgetExceeded () =
                        problemWithToken.MaxTimeMinutes
                        |> Option.map (fun maxMinutes ->
                            (DateTime.UtcNow - startTime).TotalMinutes > float maxMinutes)
                        |> Option.defaultValue false

                    let isCancellationRequested () =
                        cancellationToken.IsCancellationRequested ||
                        (match problemWithToken.CancellationToken with
                         | Some token when token.IsCancellationRequested -> true
                         | _ ->
                             reporter |> Option.map (fun r -> r.IsCancellationRequested) |> Option.defaultValue false)

                    // executeTrial is CPU-bound, identical logic to sync version
                    let executeTrial (trial: TrialSpec) : (TrialResult * obj option) option =
                        if isCancellationRequested() then
                            if problemWithToken.Verbose then
                                logInfo problemWithToken.Logger "[Stop] Search cancelled by user"
                            reporter |> Option.iter (fun r ->
                                r.Report(Core.Progress.ProgressUpdate(0.0, "Search cancelled by user")))
                            None
                        elif isTimeBudgetExceeded() then
                            if problemWithToken.Verbose then
                                let elapsed = (DateTime.UtcNow - startTime).TotalMinutes
                                logInfo problemWithToken.Logger $"[Timeout] Time budget exceeded ({elapsed:F1} minutes)"
                            None
                        else
                            let trialStart = DateTime.UtcNow
                            let modelTypeStr = sprintf "%A" trial.ModelType
                            reporter |> Option.iter (fun r ->
                                r.Report(Core.Progress.TrialStarted(trial.Id + 1, trials.Length, modelTypeStr)))
                            if problemWithToken.Verbose then
                                logInfo problemWithToken.Logger $"Trial {trial.Id + 1}/{List.length trials}: {trial.ModelType} with {trial.Architecture}..."

                            let createFailureResult errorMsg =
                                ({
                                    Id = trial.Id
                                    ModelType = trial.ModelType
                                    Architecture = trial.Architecture
                                    Hyperparameters = trial.Hyperparameters
                                    Score = 0.0
                                    TrainingTime = DateTime.UtcNow - trialStart
                                    Success = false
                                    ErrorMessage = Some errorMsg
                                }, None)

                            let createSuccessResult score model =
                                ({
                                    Id = trial.Id
                                    ModelType = trial.ModelType
                                    Architecture = trial.Architecture
                                    Hyperparameters = trial.Hyperparameters
                                    Score = score
                                    TrainingTime = DateTime.UtcNow - trialStart
                                    Success = true
                                    ErrorMessage = None
                                }, Some (box model))

                            let result =
                                try
                                    match trial.ModelType with
                                    | BinaryClassification ->
                                        let trainYInt = trainY |> Array.map int
                                        let valYInt = valY |> Array.map int
                                        tryBinaryClassificationModel trainX trainYInt trial.Architecture trial.Hyperparameters (Some backend)
                                        |> Result.bind (fun model ->
                                            BinaryClassifier.evaluate valX valYInt model
                                            |> Result.map (fun metrics ->
                                                let score = metrics.Accuracy
                                                let elapsed = (DateTime.UtcNow - trialStart).TotalSeconds
                                                if problemWithToken.Verbose then
                                                    logInfo problemWithToken.Logger $"  [OK] Score: {score * 100.0:F2}%% (time: {elapsed:F1}s)"
                                                reporter |> Option.iter (fun r ->
                                                    r.Report(Core.Progress.TrialCompleted(trial.Id + 1, score, elapsed)))
                                                (score, model)))
                                        |> Result.map (fun (score, model) -> createSuccessResult score model)
                                        |> Result.orElseWith (fun e ->
                                            if problemWithToken.Verbose then
                                                logWarning problemWithToken.Logger $"  [FAIL] Failed: {e}"
                                            reporter |> Option.iter (fun r ->
                                                r.Report(Core.Progress.TrialFailed(trial.Id + 1, e.Message)))
                                            Ok (createFailureResult e.Message))
                                    | MultiClassClassification numClasses ->
                                        let trainYInt = trainY |> Array.map int
                                        let valYInt = valY |> Array.map int
                                        tryMultiClassModel trainX trainYInt numClasses trial.Architecture trial.Hyperparameters (Some backend)
                                        |> Result.bind (fun model ->
                                            PredictiveModel.evaluateMultiClass valX valYInt model
                                            |> Result.map (fun metrics ->
                                                let score = metrics.Accuracy
                                                if problemWithToken.Verbose then
                                                    logInfo problemWithToken.Logger $"  [OK] Score: {score * 100.0:F2}%% (time: {(DateTime.UtcNow - trialStart).TotalSeconds:F1}s)"
                                                (score, model)))
                                        |> Result.map (fun (score, model) -> createSuccessResult score model)
                                        |> Result.orElseWith (fun e ->
                                            if problemWithToken.Verbose then
                                                logWarning problemWithToken.Logger $"  [FAIL] Failed: {e}"
                                            Ok (createFailureResult e.Message))
                                    | Regression ->
                                        tryRegressionModel trainX trainY trial.Architecture trial.Hyperparameters (Some backend)
                                        |> Result.bind (fun model ->
                                            PredictiveModel.evaluateRegression valX valY model
                                            |> Result.map (fun metrics ->
                                                let score = max 0.0 metrics.RSquared
                                                if problemWithToken.Verbose then
                                                    logInfo problemWithToken.Logger $"  [OK] R2 Score: {score:F4} (time: {(DateTime.UtcNow - trialStart).TotalSeconds:F1}s)"
                                                (score, model)))
                                        |> Result.map (fun (score, model) -> createSuccessResult score model)
                                        |> Result.orElseWith (fun e ->
                                            if problemWithToken.Verbose then
                                                logWarning problemWithToken.Logger $"  [FAIL] Failed: {e}"
                                            Ok (createFailureResult e.Message))
                                    | AnomalyDetection ->
                                        let normalData = trainX
                                        tryAnomalyDetectionModel normalData trial.Architecture trial.Hyperparameters (Some backend)
                                        |> Result.map (fun detector ->
                                            let predictions =
                                                valX
                                                |> Array.choose (fun x ->
                                                    AnomalyDetector.check x detector
                                                    |> Result.toOption
                                                    |> Option.map (fun pred -> if pred.IsAnomaly then 1.0 else 0.0))
                                            let score =
                                                if predictions.Length > 0 then
                                                    let anomalyRate = predictions |> Array.average
                                                    if anomalyRate >= 0.05 && anomalyRate <= 0.15 then 0.8 else 0.5
                                                else 0.0
                                            if problemWithToken.Verbose then
                                                logInfo problemWithToken.Logger $"  [OK] Heuristic Score: {score:F2} (time: {(DateTime.UtcNow - trialStart).TotalSeconds:F1}s)"
                                            (score, detector))
                                        |> Result.map (fun (score, detector) -> createSuccessResult score detector)
                                        |> Result.orElseWith (fun e ->
                                            if problemWithToken.Verbose then
                                                logWarning problemWithToken.Logger $"  [FAIL] Failed: {e}"
                                            Ok (createFailureResult e.Message))
                                    | SimilaritySearch ->
                                        trySimilaritySearchModel trainX trial.Hyperparameters (Some backend)
                                        |> Result.map (fun searchIndex ->
                                            let score =
                                                if searchIndex.Items.Length >= 2 then 0.7
                                                else 0.3
                                            if problemWithToken.Verbose then
                                                logInfo problemWithToken.Logger $"  [OK] Search Quality Score: {score:F2} (time: {(DateTime.UtcNow - trialStart).TotalSeconds:F1}s)"
                                            (score, searchIndex))
                                        |> Result.map (fun (score, searchIndex) -> createSuccessResult score searchIndex)
                                        |> Result.orElseWith (fun e ->
                                            if problemWithToken.Verbose then
                                                logWarning problemWithToken.Logger $"  [FAIL] Failed: {e}"
                                            Ok (createFailureResult e.Message))
                                with ex ->
                                    if problemWithToken.Verbose then
                                        logError problemWithToken.Logger $"  [ERROR] Exception: {ex.Message}"
                                    Ok (createFailureResult ex.Message)

                            match result with
                            | Ok resultTuple -> Some resultTuple
                            | Error _ -> None

                    // Task-based parallelization: Task.WhenAll + Task.Run for CPU-bound work
                    let maxDegreeOfParallelism = min 4 (trials.Length / 2 |> max 1)

                    let resultsWithModels =
                        trials
                        |> List.chunkBySize maxDegreeOfParallelism
                        |> List.collect (fun batch ->
                            let tasks =
                                batch
                                |> List.map (fun trial ->
                                    System.Threading.Tasks.Task.Run((fun () -> executeTrial trial), cancellationToken))
                                |> Array.ofList
                            System.Threading.Tasks.Task.WhenAll(tasks).GetAwaiter().GetResult()
                            |> Array.choose id
                            |> Array.toList)

                    let results = resultsWithModels |> List.map fst
                    let totalTime = DateTime.UtcNow - startTime

                    let bestResultWithModel =
                        resultsWithModels
                        |> List.filter (fun (r, _) -> r.Success)
                        |> List.sortByDescending (fun (r, _) -> r.Score)
                        |> List.tryHead

                    match bestResultWithModel with
                    | Some (bestTrial, Some bestModel) ->
                        let modelTypeStr =
                            match bestTrial.ModelType with
                            | BinaryClassification -> "Binary Classification"
                            | MultiClassClassification n -> $"Multi-Class Classification ({n} classes)"
                            | Regression -> "Regression"
                            | AnomalyDetection -> "Anomaly Detection"
                            | SimilaritySearch -> "Similarity Search"

                        let successfulTrials = results |> List.filter (fun r -> r.Success) |> List.length
                        let failedTrials = results |> List.filter (fun r -> not r.Success) |> List.length

                        let result = {
                            BestModelType = modelTypeStr
                            BestArchitecture = bestTrial.Architecture
                            BestHyperparameters = bestTrial.Hyperparameters
                            Score = bestTrial.Score
                            AllTrials = results |> List.toArray
                            TotalSearchTime = totalTime
                            SuccessfulTrials = successfulTrials
                            FailedTrials = failedTrials
                            Model = bestModel
                            Metadata = {
                                NumFeatures = problemWithToken.TrainFeatures.[0].Length
                                NumSamples = problemWithToken.TrainFeatures.Length
                                CreatedAt = startTime
                                SearchCompleted = DateTime.UtcNow
                                Note = None
                            }
                        }

                        if problemWithToken.Verbose then
                            logInfo problemWithToken.Logger ""
                            logInfo problemWithToken.Logger "[OK] AutoML Search Complete (async)!"
                            logInfo problemWithToken.Logger $"   Best Model: {result.BestModelType}"
                            logInfo problemWithToken.Logger $"   Best Architecture: {result.BestArchitecture}"
                            logInfo problemWithToken.Logger $"   Best Score: {result.Score * 100.0:F2}%%"
                            logInfo problemWithToken.Logger $"   Successful Trials: {result.SuccessfulTrials}/{results.Length}"
                            logInfo problemWithToken.Logger $"   Total Time: {result.TotalSearchTime.TotalSeconds:F1}s"

                        Ok result

                    | _ ->
                        Error (QuantumError.OperationError ("Operation", "All trials failed - no model could be trained successfully")))
        }

    
    // ========================================================================
    // PREDICTION - Use best model
    // ========================================================================
    
    /// Predict with AutoML result (wrapper for underlying model)
    let predict (features: float array) (result: AutoMLResult) : QuantumResult<Prediction> =
        try
            match result.BestModelType with
            | name when name.StartsWith("Binary") ->
                let model = unbox<BinaryClassifier.Classifier> result.Model
                BinaryClassifier.predict features model
                |> Result.map BinaryPrediction
            
            | name when name.StartsWith("Multi-Class") ->
                let model = unbox<PredictiveModel.Model> result.Model
                PredictiveModel.predictCategory features model None None
                |> Result.map CategoryPrediction
            
            | "Regression" ->
                let model = unbox<PredictiveModel.Model> result.Model
                PredictiveModel.predict features model None None
                |> Result.map RegressionPrediction
            
            | "Anomaly Detection" ->
                let detector = unbox<AnomalyDetector.Detector> result.Model
                AnomalyDetector.check features detector
                |> Result.map AnomalyPrediction
            
            | "Similarity Search" ->
                let searchIndex = unbox<SimilaritySearch.SearchIndex<obj>> result.Model
                // For similarity search, we need an item from the index
                // Use the first item in the index as a fallback
                if searchIndex.Items.Length = 0 then
                    Error (QuantumError.ValidationError ("Input", "Similarity search index is empty"))
                else
                    let firstItem, _ = searchIndex.Items.[0]
                    // Limit topN to number of items minus 1 (exclude query itself)
                    let topN = min 5 (searchIndex.Items.Length - 1) |> max 1
                    SimilaritySearch.findSimilar firstItem features topN searchIndex
                    |> Result.map SimilarityPrediction
            
            | _ ->
                Error (QuantumError.ValidationError ("Input", $"Unsupported model type: {result.BestModelType}"))
        with ex ->
            Error (QuantumError.ValidationError ("Input", $"Prediction failed: {ex.Message}"))
    
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
                Logger = None
                SavePath = None
                RandomSeed = None
                ProgressReporter = None
                CancellationToken = None
            }
        
        member _.Delay(f: unit -> AutoMLProblem) = f
        
        member _.Run(f: unit -> AutoMLProblem) : QuantumResult<AutoMLResult> =
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
                Logger = None
                SavePath = None
                RandomSeed = None
                ProgressReporter = None
                CancellationToken = None
            }
        
        /// <summary>Set the training data with features and labels.</summary>
        /// <param name="features">Training feature vectors</param>
        /// <param name="labels">Labels for each sample</param>
        [<CustomOperation("trainWith")>]
        member _.TrainWith(problem: AutoMLProblem, features: float array array, labels: float array) =
            { problem with TrainFeatures = features; TrainLabels = labels }
        
        /// <summary>Enable or disable binary classification in the search space.</summary>
        /// <param name="enable">True to include binary classification</param>
        [<CustomOperation("tryBinaryClassification")>]
        member _.TryBinaryClassification(problem: AutoMLProblem, enable: bool) =
            { problem with TryBinaryClassification = enable }
        
        /// <summary>Enable multi-class classification with specified number of classes.</summary>
        /// <param name="numClasses">Number of classes for multi-class classification</param>
        [<CustomOperation("tryMultiClass")>]
        member _.TryMultiClass(problem: AutoMLProblem, numClasses: int) =
            { problem with TryMultiClass = Some numClasses }
        
        /// <summary>Enable or disable anomaly detection in the search space.</summary>
        /// <param name="enable">True to include anomaly detection</param>
        [<CustomOperation("tryAnomalyDetection")>]
        member _.TryAnomalyDetection(problem: AutoMLProblem, enable: bool) =
            { problem with TryAnomalyDetection = enable }
        
        /// <summary>Enable or disable regression in the search space.</summary>
        /// <param name="enable">True to include regression</param>
        [<CustomOperation("tryRegression")>]
        member _.TryRegression(problem: AutoMLProblem, enable: bool) =
            { problem with TryRegression = enable }
        
        /// <summary>Enable or disable similarity search in the search space.</summary>
        /// <param name="enable">True to include similarity search</param>
        [<CustomOperation("trySimilaritySearch")>]
        member _.TrySimilaritySearch(problem: AutoMLProblem, enable: bool) =
            { problem with TrySimilaritySearch = enable }
        
        /// <summary>Specify the architectures to try during optimization.</summary>
        /// <param name="architectures">List of architectures to evaluate</param>
        [<CustomOperation("tryArchitectures")>]
        member _.TryArchitectures(problem: AutoMLProblem, architectures: Architecture list) =
            { problem with TryArchitectures = architectures }
        
        /// <summary>Set the maximum number of trials for hyperparameter search.</summary>
        /// <param name="trials">Maximum number of trials</param>
        [<CustomOperation("maxTrials")>]
        member _.MaxTrials(problem: AutoMLProblem, trials: int) =
            { problem with MaxTrials = trials }
        
        /// <summary>Set the maximum time limit for AutoML search in minutes.</summary>
        /// <param name="minutes">Maximum time in minutes</param>
        [<CustomOperation("maxTimeMinutes")>]
        member _.MaxTimeMinutes(problem: AutoMLProblem, minutes: int) =
            { problem with MaxTimeMinutes = Some minutes }
        
        /// <summary>Set the validation split ratio for model evaluation.</summary>
        /// <param name="split">Validation split ratio (0.0 to 1.0)</param>
        [<CustomOperation("validationSplit")>]
        member _.ValidationSplit(problem: AutoMLProblem, split: float) =
            { problem with ValidationSplit = split }
        
        /// <summary>Set the quantum backend for execution.</summary>
        /// <param name="backend">Quantum backend instance</param>
        [<CustomOperation("backend")>]
        member _.Backend(problem: AutoMLProblem, backend: IQuantumBackend) =
            { problem with Backend = Some backend }
        
        /// <summary>Enable or disable verbose output.</summary>
        /// <param name="verbose">True to enable detailed logging</param>
        [<CustomOperation("verbose")>]
        member _.Verbose(problem: AutoMLProblem, verbose: bool) =
            { problem with Verbose = verbose }
        
        /// <summary>Set the path to save the best model found.</summary>
        /// <param name="path">File path for saving the model</param>
        [<CustomOperation("saveModelTo")>]
        member _.SaveModelTo(problem: AutoMLProblem, path: string) =
            { problem with SavePath = Some path }
        
        /// <summary>Set the random seed for reproducibility.</summary>
        /// <param name="seed">Random seed value</param>
        [<CustomOperation("randomSeed")>]
        member _.RandomSeed(problem: AutoMLProblem, seed: int) =
            { problem with RandomSeed = Some seed }
        
        /// <summary>Set a progress reporter for real-time updates.</summary>
        /// <param name="reporter">Progress reporter instance</param>
        [<CustomOperation("progressReporter")>]
        member _.ProgressReporter(problem: AutoMLProblem, reporter: Core.Progress.IProgressReporter) =
            { problem with ProgressReporter = Some reporter }
        
        /// <summary>Set a cancellation token for early termination.</summary>
        /// <param name="token">Cancellation token</param>
        [<CustomOperation("cancellationToken")>]
        member _.CancellationToken(problem: AutoMLProblem, token: System.Threading.CancellationToken) =
            { problem with CancellationToken = Some token }
    
    /// Create AutoML computation expression
    let autoML = AutoMLBuilder()
