namespace FSharp.Azure.Quantum.Business

open FSharp.Azure.Quantum.Backends
open FSharp.Azure.Quantum.Core
open System
open System.Threading
open System.Threading.Tasks
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
/// EXAMPLE USAGE (the builder yields a Task, so bind it inside task { }):
///   // Minimal: Just data
///   let! result = autoML {
///       trainWith features labels
///   }
///
///   match result with
///   | Ok model ->
///       printfn "Best model: %s (%.2f%% accuracy)"
///           model.BestModelType (model.Score * 100.0)
///
///       let! prediction = AutoML.predictAsync newSample model cancellationToken
///       ...
///
///   // Advanced: Custom search space
///   let! result = autoML {
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
        /// num classes
        | MultiClassClassification of int
        | AnomalyDetection
        | Regression
        | SimilaritySearch

    /// Architecture for model
    type Architecture =
        | Quantum
        | Hybrid
        | Classical

    /// Hyperparameter configuration
    [<Struct>]
    type HyperparameterConfig =
        {
            LearningRate: float
            MaxEpochs: int
            ConvergenceThreshold: float
            Shots: int
        }

    /// Single trial result
    type TrialResult =
        {
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
    type AutoMLProblem =
        {
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
            CancellationToken: CancellationToken option
        }

    /// Trained model produced by an AutoML trial.
    /// Replaces the former type-erased `obj` so callers can pattern-match instead of unboxing.
    type TrainedModel =
        | BinaryModel of BinaryClassifier.Classifier
        | MultiClassModel of PredictiveModel.Model
        | RegressionModel of PredictiveModel.Model
        | AnomalyModel of AnomalyDetector.Detector
        | SimilarityModel of SimilaritySearch.SearchIndex<obj>

    /// AutoML result - best model found
    type AutoMLResult =
        {
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
            Model: TrainedModel

            /// Model metadata
            Metadata: AutoMLMetadata
        }

    and AutoMLMetadata =
        {
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
            Error(QuantumError.ValidationError("Input", "Training features cannot be empty"))
        elif problem.TrainLabels.Length = 0 then
            Error(QuantumError.ValidationError("Input", "Training labels cannot be empty"))
        elif problem.TrainFeatures.Length <> problem.TrainLabels.Length then
            Error(
                QuantumError.ValidationError(
                    "Input",
                    $"Feature count ({problem.TrainFeatures.Length}) must match label count ({problem.TrainLabels.Length})"
                )
            )
        elif problem.TrainFeatures |> Array.exists (fun f -> f.Length = 0) then
            Error(QuantumError.ValidationError("Input", "All feature arrays must have at least one element"))
        elif
            problem.TrainFeatures
            |> Array.map Array.length
            |> Array.distinct
            |> Array.length
                >
                1
        then
            Error(QuantumError.ValidationError("Input", "All feature arrays must have the same length"))
        elif problem.MaxTrials <= 0 then
            Error(QuantumError.ValidationError("Input", "MaxTrials must be positive"))
        elif problem.ValidationSplit <= 0.0 || problem.ValidationSplit >= 1.0 then
            Error(QuantumError.ValidationError("Input", "ValidationSplit must be between 0 and 1"))
        elif
            not (
                problem.TryBinaryClassification
                || problem.TryMultiClass.IsSome
                || problem.TryAnomalyDetection
                || problem.TryRegression
                || problem.TrySimilaritySearch
            )
        then
            Error(QuantumError.ValidationError("Input", "At least one model type must be enabled"))
        elif problem.TryArchitectures.IsEmpty then
            Error(QuantumError.ValidationError("Input", "At least one architecture must be enabled"))
        else
            Ok()

    // ========================================================================
    // HYPERPARAMETER SEARCH
    // ========================================================================

    let private generateHyperparameterConfigs (randomSeed: int option) : HyperparameterConfig list =
        let random =
            randomSeed |> Option.map Random |> Option.defaultWith (fun () -> Random())

        // Grid + random search combination
        let gridConfigs =
            [
                // Conservative
                {
                    LearningRate = 0.01
                    MaxEpochs = 50
                    ConvergenceThreshold = 0.001
                    Shots = 1000
                }
                {
                    LearningRate = 0.01
                    MaxEpochs = 100
                    ConvergenceThreshold = 0.001
                    Shots = 1000
                }

                // Aggressive
                {
                    LearningRate = 0.05
                    MaxEpochs = 100
                    ConvergenceThreshold = 0.0005
                    Shots = 1000
                }

                // Fine-tuned
                {
                    LearningRate = 0.005
                    MaxEpochs = 150
                    ConvergenceThreshold = 0.0001
                    Shots = 1000
                }
            ]

        // Add random configurations
        let randomConfigs =
            List.init 6 (fun _ ->
                {
                    LearningRate = 0.001 + random.NextDouble() * 0.049 // [0.001, 0.05]
                    MaxEpochs = 50 + random.Next(150) // [50, 200]
                    ConvergenceThreshold = 0.0001 + random.NextDouble() * 0.0099 // [0.0001, 0.01]
                    Shots = 1000
                })

        gridConfigs @ randomConfigs

    // ========================================================================
    // MODEL TRAINING - Try different approaches
    // ========================================================================

    let private tryBinaryClassificationModelAsync
        (trainX: float array array)
        (trainY: int array)
        (arch: Architecture)
        (hyperparams: HyperparameterConfig)
        (backend: IQuantumBackend option)
        (cancellationToken: CancellationToken)
        : Task<QuantumResult<BinaryClassifier.Classifier>> =

        let problem: BinaryClassifier.ClassificationProblem =
            {
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

        BinaryClassifier.trainAsync problem cancellationToken

    let private tryMultiClassModelAsync
        (trainX: float array array)
        (trainY: int array)
        (numClasses: int)
        (arch: Architecture)
        (hyperparams: HyperparameterConfig)
        (backend: IQuantumBackend option)
        (cancellationToken: CancellationToken)
        : Task<QuantumResult<PredictiveModel.Model>> =

        let problem: PredictiveModel.PredictionProblem =
            {
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

        PredictiveModel.trainAsync problem cancellationToken

    let private tryRegressionModelAsync
        (trainX: float array array)
        (trainY: float array)
        (arch: Architecture)
        (hyperparams: HyperparameterConfig)
        (backend: IQuantumBackend option)
        (cancellationToken: CancellationToken)
        : Task<QuantumResult<PredictiveModel.Model>> =

        let problem: PredictiveModel.PredictionProblem =
            {
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

        PredictiveModel.trainAsync problem cancellationToken

    let private tryAnomalyDetectionModelAsync
        (trainX: float array array)
        (arch: Architecture)
        (hyperparams: HyperparameterConfig)
        (backend: IQuantumBackend option)
        (cancellationToken: CancellationToken)
        : Task<QuantumResult<AnomalyDetector.Detector>> =

        let problem: AnomalyDetector.DetectionProblem =
            {
                NormalData = trainX
                Sensitivity = AnomalyDetector.Sensitivity.Medium
                ContaminationRate = 0.1 // Assume 10% anomalies
                Backend = backend
                Shots = hyperparams.Shots
                Verbose = false
                Logger = None
                SavePath = None
                Note = None
                ProgressReporter = None
                CancellationToken = None
            }

        AnomalyDetector.trainAsync problem cancellationToken

    let private trySimilaritySearchModelAsync
        (trainX: float array array)
        (hyperparams: HyperparameterConfig)
        (backend: IQuantumBackend option)
        (cancellationToken: CancellationToken)
        : Task<QuantumResult<SimilaritySearch.SearchIndex<obj>>> =

        // Create indexed items (boxed item index, features)
        let items = trainX |> Array.mapi (fun i features -> (box i, features))

        let problem: SimilaritySearch.SearchProblem<obj> =
            {
                Items = items
                Metric = SimilaritySearch.SimilarityMetric.Cosine // Use Cosine for reliability
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

        SimilaritySearch.buildAsync problem cancellationToken

    // ========================================================================
    // GENUINE EVALUATION OF UNSUPERVISED TRIALS
    //
    // AutoML is supervised (it always has labels), so anomaly and similarity trials
    // are scored against those labels with real metrics — not fabricated constants.
    // ========================================================================

    /// Balanced accuracy = mean of true-positive-rate and true-negative-rate. Robust
    /// to class imbalance and to degenerate "predict-all-one-class" detectors (both
    /// collapse to 0.5), unlike raw accuracy. Used to genuinely score anomaly detection
    /// against ground-truth anomaly labels (label >= 0.5 ⇒ anomaly).
    let private balancedAccuracy (predicted: float array) (truth: float array) : float =
        let pairs = Array.zip predicted truth
        let pos = pairs |> Array.filter (fun (_, t) -> t >= 0.5)
        let neg = pairs |> Array.filter (fun (_, t) -> t < 0.5)

        let tpr =
            if pos.Length = 0 then
                None
            else
                Some(
                    float (pos |> Array.filter (fun (p, _) -> p >= 0.5) |> Array.length)
                    / float pos.Length
                )

        let tnr =
            if neg.Length = 0 then
                None
            else
                Some(
                    float (neg |> Array.filter (fun (p, _) -> p < 0.5) |> Array.length)
                    / float neg.Length
                )

        match tpr, tnr with
        | Some a, Some b -> (a + b) / 2.0
        | Some a, None -> a // only anomalies in the validation set: report recall
        | None, Some b -> b // only normals: report specificity
        | None, None -> 0.0

    /// Genuine anomaly-detection score: run the detector over the labelled validation
    /// set and compare its anomaly flags to the ground-truth labels via balanced accuracy.
    /// Samples are checked one after another; a sample whose check fails is left out.
    let private scoreAnomalyDetectorAsync
        (detector: AnomalyDetector.Detector)
        (valX: float array array)
        (valY: float array)
        (cancellationToken: CancellationToken)
        : Task<float> =
        task {
            let paired = ResizeArray<float * float>(valX.Length)

            for (x, y) in Array.zip valX valY do
                match! AnomalyDetector.checkAsync x detector cancellationToken with
                | Ok pred -> paired.Add(((if pred.IsAnomaly then 1.0 else 0.0), y))
                | Error _ -> ()

            return
                if paired.Count = 0 then
                    0.0
                else
                    paired.ToArray() |> Array.unzip ||> balancedAccuracy
        }

    /// Genuine similarity-search score: average precision@k over the labelled validation
    /// set, where a retrieved neighbour is "relevant" if it shares the query's label.
    /// Measures whether the index actually groups same-class items, rather than scoring
    /// by index size. The index keys are the boxed training-row indices (see
    /// trySimilaritySearchModel), which map back to training labels.
    /// Queries run one after another; a query that fails or matches nothing is left out.
    let private scoreSimilarityIndexAsync
        (index: SimilaritySearch.SearchIndex<obj>)
        (trainY: float array)
        (valX: float array array)
        (valY: float array)
        (cancellationToken: CancellationToken)
        : Task<float> =
        let k = min 5 index.Items.Length

        task {
            let perQuery = ResizeArray<float>(valX.Length)

            for (qFeatures, qLabel) in Array.zip valX valY do
                // Use a sentinel key (-1) absent from the index so no item is excluded as self.
                match! SimilaritySearch.findSimilarAsync (box -1) qFeatures k index cancellationToken with
                | Ok results when results.Matches.Length > 0 ->
                    let relevant =
                        results.Matches
                        |> Array.filter (fun m ->
                            match m.Item with
                            | :? int as idx when idx >= 0 && idx < trainY.Length -> abs (trainY.[idx] - qLabel) < 0.5
                            | _ -> false)
                        |> Array.length

                    perQuery.Add(float relevant / float results.Matches.Length)
                | _ -> ()

            return if perQuery.Count = 0 then 0.0 else Seq.average perQuery
        }

    // ========================================================================
    // TRIAL GENERATION
    // ========================================================================

    type private TrialSpec =
        {
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
            IsLikelyRegression =
                uniqueLabels.Length > 10
                || (uniqueLabels |> Array.exists (fun x -> x <> floor x))
            NumClasses = uniqueLabels.Length
        |}

    let private generateTrials
        (problem: AutoMLProblem)
        (hyperparamConfigs: HyperparameterConfig list)
        : TrialSpec list =
        let problemTypes = detectProblemTypes problem.TrainLabels
        let hpSample = hyperparamConfigs |> List.truncate 3

        problem.TryArchitectures
        |> List.collect (fun arch ->
            hpSample
            |> List.collect (fun hp ->
                [
                    // Binary classification
                    if problem.TryBinaryClassification && problemTypes.IsLikelyBinary then
                        Some(BinaryClassification, arch, hp)
                    else
                        None

                    // Multi-class classification
                    match problem.TryMultiClass with
                    | Some numClasses -> Some(MultiClassClassification numClasses, arch, hp)
                    | None when problemTypes.IsLikelyMultiClass ->
                        Some(MultiClassClassification problemTypes.NumClasses, arch, hp)
                    | _ -> None

                    // Regression
                    if problem.TryRegression && problemTypes.IsLikelyRegression then
                        Some(Regression, arch, hp)
                    else
                        None

                    // Anomaly detection
                    if problem.TryAnomalyDetection then
                        Some(AnomalyDetection, arch, hp)
                    else
                        None

                    // Similarity search
                    if problem.TrySimilaritySearch then
                        Some(SimilaritySearch, arch, hp)
                    else
                        None
                ]
                |> List.choose id
                |> List.map (fun (modelType, arch, hp) -> (modelType, arch, hp))))
        |> List.mapi (fun i (modelType, arch, hp) ->
            {
                Id = i
                ModelType = modelType
                Architecture = arch
                Hyperparameters = hp
            })
        |> List.truncate problem.MaxTrials

    // ========================================================================
    // TRAIN / VALIDATION SPLIT
    // ========================================================================

    /// Shuffle the dataset with a seeded Fisher-Yates permutation, then split into
    /// train/validation sets.
    ///
    /// A plain head/tail split is degenerate on ordered datasets (e.g. label-sorted
    /// data puts one class entirely in the validation set), making every trial score
    /// meaningless. Shuffling first gives both splits the same distribution.
    /// Uses RandomSeed when provided (default 42) so searches are reproducible.
    let private shuffledTrainValSplit
        (features: float array array)
        (labels: float array)
        (validationSplit: float)
        (seed: int option)
        : (float array array * float array * float array array * float array) =

        let n = features.Length
        let rng = Random(seed |> Option.defaultValue 42)

        // Fisher-Yates shuffle of indices
        let indices = Array.init n id

        for i in n - 1 .. -1 .. 1 do
            let j = rng.Next(i + 1)
            let tmp = indices.[i]
            indices.[i] <- indices.[j]
            indices.[j] <- tmp

        let splitIndex = int (float n * (1.0 - validationSplit))
        let trainIdx = indices.[.. splitIndex - 1]
        let valIdx = indices.[splitIndex..]

        (trainIdx |> Array.map (fun i -> features.[i]),
         trainIdx |> Array.map (fun i -> labels.[i]),
         valIdx |> Array.map (fun i -> features.[i]),
         valIdx |> Array.map (fun i -> labels.[i]))

    // ========================================================================
    // AUTO ML SEARCH
    // ========================================================================

    /// Run AutoML search to find best model (task-based, non-blocking parallelization).
    ///
    /// Trials run in batches: each trial of a batch is started with Task.Run and the
    /// batch is awaited with Task.WhenAll, so no thread blocks while the trials
    /// (and the quantum jobs they submit) run. Safe to call from an async/task
    /// context without deadlock risk.
    let searchAsync
        (problem: AutoMLProblem)
        (cancellationToken: CancellationToken)
        : Task<QuantumResult<AutoMLResult>> =
        // Merge explicit CancellationToken with any token on the problem
        let problemWithToken =
            match problem.CancellationToken with
            | Some _ -> problem
            | None ->
                { problem with
                    CancellationToken = Some cancellationToken
                }

        task {
            match validateProblem problemWithToken with
            | Error e -> return Error e
            | Ok() ->

                let startTime = DateTime.UtcNow

                let backend =
                    problemWithToken.Backend
                    |> Option.defaultValue (LocalBackend.LocalBackend() :> IQuantumBackend)

                let reporter = problemWithToken.ProgressReporter

                reporter
                |> Option.iter (fun r ->
                    r.Report(Core.Progress.PhaseChanged("AutoML Search", Some "Initializing search")))

                if problemWithToken.Verbose then
                    logInfo problemWithToken.Logger "[Start] Starting AutoML Search (async)..."
                    logInfo problemWithToken.Logger $"   Samples: {problemWithToken.TrainFeatures.Length}"
                    logInfo problemWithToken.Logger $"   Features: {problemWithToken.TrainFeatures.[0].Length}"
                    logInfo problemWithToken.Logger $"   Max Trials: {problemWithToken.MaxTrials}"
                    logInfo problemWithToken.Logger $"   Architectures: {problemWithToken.TryArchitectures.Length}"
                    logInfo problemWithToken.Logger ""

                // Split data into train/validation (shuffled so ordered/label-sorted
                // datasets don't yield degenerate splits)
                let (trainX, trainY, valX, valY) =
                    shuffledTrainValSplit
                        problemWithToken.TrainFeatures
                        problemWithToken.TrainLabels
                        problemWithToken.ValidationSplit
                        problemWithToken.RandomSeed

                if problemWithToken.Verbose then
                    logInfo problemWithToken.Logger $"Train/Val Split: {trainX.Length}/{valX.Length} samples\n"

                let hyperparamConfigs = generateHyperparameterConfigs problemWithToken.RandomSeed
                let trials = generateTrials problemWithToken hyperparamConfigs

                if problemWithToken.Verbose then
                    logInfo problemWithToken.Logger $"Generated {trials.Length} trials to execute\n"

                let isTimeBudgetExceeded () =
                    problemWithToken.MaxTimeMinutes
                    |> Option.map (fun maxMinutes -> (DateTime.UtcNow - startTime).TotalMinutes > float maxMinutes)
                    |> Option.defaultValue false

                let isCancellationRequested () =
                    cancellationToken.IsCancellationRequested
                    || (match problemWithToken.CancellationToken with
                        | Some token when token.IsCancellationRequested -> true
                        | _ ->
                            reporter
                            |> Option.map (fun r -> r.IsCancellationRequested)
                            |> Option.defaultValue false)

                let executeTrialAsync (trial: TrialSpec) : Task<(TrialResult * TrainedModel option) option> =
                    task {
                        if isCancellationRequested () then
                            if problemWithToken.Verbose then
                                logInfo problemWithToken.Logger "[Stop] Search cancelled by user"

                            reporter
                            |> Option.iter (fun r ->
                                r.Report(Core.Progress.ProgressUpdate(0.0, "Search cancelled by user")))

                            return None
                        elif isTimeBudgetExceeded () then
                            if problemWithToken.Verbose then
                                let elapsed = (DateTime.UtcNow - startTime).TotalMinutes

                                logInfo problemWithToken.Logger $"[Timeout] Time budget exceeded ({elapsed:F1} minutes)"

                            return None
                        else
                            let trialStart = DateTime.UtcNow
                            let modelTypeStr = $"%A{trial.ModelType}"

                            reporter
                            |> Option.iter (fun r ->
                                r.Report(Core.Progress.TrialStarted(trial.Id + 1, trials.Length, modelTypeStr)))

                            if problemWithToken.Verbose then
                                logInfo
                                    problemWithToken.Logger
                                    $"Trial {trial.Id + 1}/{List.length trials}: {trial.ModelType} with {trial.Architecture}..."

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
                                 },
                                 None)

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
                                 },
                                 Some model)

                            let! result =
                                task {
                                    try
                                        match trial.ModelType with
                                        | BinaryClassification ->
                                            let trainYInt = trainY |> Array.map int
                                            let valYInt = valY |> Array.map int

                                            let! outcome =
                                                quantumResultTask {
                                                    let! model =
                                                        tryBinaryClassificationModelAsync
                                                            trainX
                                                            trainYInt
                                                            trial.Architecture
                                                            trial.Hyperparameters
                                                            (Some backend)
                                                            cancellationToken

                                                    let! metrics =
                                                        BinaryClassifier.evaluateAsync
                                                            valX
                                                            valYInt
                                                            model
                                                            cancellationToken

                                                    let score = metrics.Accuracy
                                                    let elapsed = (DateTime.UtcNow - trialStart).TotalSeconds

                                                    if problemWithToken.Verbose then
                                                        logInfo
                                                            problemWithToken.Logger
                                                            $"  [OK] Score: {score * 100.0:F2}%% (time: {elapsed:F1}s)"

                                                    reporter
                                                    |> Option.iter (fun r ->
                                                        r.Report(
                                                            Core.Progress.TrialCompleted(trial.Id + 1, score, elapsed)
                                                        ))

                                                    return (score, model)
                                                }

                                            return
                                                outcome
                                                |> Result.map (fun (score, model) ->
                                                    createSuccessResult score (BinaryModel model))
                                                |> Result.orElseWith (fun e ->
                                                    if problemWithToken.Verbose then
                                                        logWarning problemWithToken.Logger $"  [FAIL] Failed: {e}"

                                                    reporter
                                                    |> Option.iter (fun r ->
                                                        r.Report(Core.Progress.TrialFailed(trial.Id + 1, e.Message)))

                                                    Ok(createFailureResult e.Message))
                                        | MultiClassClassification numClasses ->
                                            let trainYInt = trainY |> Array.map int
                                            let valYInt = valY |> Array.map int

                                            let! outcome =
                                                quantumResultTask {
                                                    let! model =
                                                        tryMultiClassModelAsync
                                                            trainX
                                                            trainYInt
                                                            numClasses
                                                            trial.Architecture
                                                            trial.Hyperparameters
                                                            (Some backend)
                                                            cancellationToken

                                                    let! metrics =
                                                        PredictiveModel.evaluateMultiClassAsync
                                                            valX
                                                            valYInt
                                                            model
                                                            cancellationToken

                                                    let score = metrics.Accuracy

                                                    if problemWithToken.Verbose then
                                                        logInfo
                                                            problemWithToken.Logger
                                                            $"  [OK] Score: {score * 100.0:F2}%% (time: {(DateTime.UtcNow - trialStart).TotalSeconds:F1}s)"

                                                    return (score, model)
                                                }

                                            return
                                                outcome
                                                |> Result.map (fun (score, model) ->
                                                    createSuccessResult score (MultiClassModel model))
                                                |> Result.orElseWith (fun e ->
                                                    if problemWithToken.Verbose then
                                                        logWarning problemWithToken.Logger $"  [FAIL] Failed: {e}"

                                                    Ok(createFailureResult e.Message))
                                        | Regression ->
                                            let! outcome =
                                                quantumResultTask {
                                                    let! model =
                                                        tryRegressionModelAsync
                                                            trainX
                                                            trainY
                                                            trial.Architecture
                                                            trial.Hyperparameters
                                                            (Some backend)
                                                            cancellationToken

                                                    let! metrics =
                                                        PredictiveModel.evaluateRegressionAsync
                                                            valX
                                                            valY
                                                            model
                                                            cancellationToken

                                                    let score = max 0.0 metrics.RSquared

                                                    if problemWithToken.Verbose then
                                                        logInfo
                                                            problemWithToken.Logger
                                                            $"  [OK] R2 Score: {score:F4} (time: {(DateTime.UtcNow - trialStart).TotalSeconds:F1}s)"

                                                    return (score, model)
                                                }

                                            return
                                                outcome
                                                |> Result.map (fun (score, model) ->
                                                    createSuccessResult score (RegressionModel model))
                                                |> Result.orElseWith (fun e ->
                                                    if problemWithToken.Verbose then
                                                        logWarning problemWithToken.Logger $"  [FAIL] Failed: {e}"

                                                    Ok(createFailureResult e.Message))
                                        | AnomalyDetection ->
                                            let normalData = trainX

                                            let! outcome =
                                                quantumResultTask {
                                                    let! detector =
                                                        tryAnomalyDetectionModelAsync
                                                            normalData
                                                            trial.Architecture
                                                            trial.Hyperparameters
                                                            (Some backend)
                                                            cancellationToken

                                                    // Genuine evaluation against ground-truth labels (balanced accuracy)
                                                    let! score =
                                                        scoreAnomalyDetectorAsync detector valX valY cancellationToken

                                                    if problemWithToken.Verbose then
                                                        logInfo
                                                            problemWithToken.Logger
                                                            $"  [OK] Balanced accuracy: {score:F4} (time: {(DateTime.UtcNow - trialStart).TotalSeconds:F1}s)"

                                                    return (score, detector)
                                                }

                                            return
                                                outcome
                                                |> Result.map (fun (score, detector) ->
                                                    createSuccessResult score (AnomalyModel detector))
                                                |> Result.orElseWith (fun e ->
                                                    if problemWithToken.Verbose then
                                                        logWarning problemWithToken.Logger $"  [FAIL] Failed: {e}"

                                                    Ok(createFailureResult e.Message))
                                        | SimilaritySearch ->
                                            let! outcome =
                                                quantumResultTask {
                                                    let! searchIndex =
                                                        trySimilaritySearchModelAsync
                                                            trainX
                                                            trial.Hyperparameters
                                                            (Some backend)
                                                            cancellationToken

                                                    // Genuine retrieval quality: label-based precision@k on the validation set
                                                    let! score =
                                                        scoreSimilarityIndexAsync
                                                            searchIndex
                                                            trainY
                                                            valX
                                                            valY
                                                            cancellationToken

                                                    if problemWithToken.Verbose then
                                                        logInfo
                                                            problemWithToken.Logger
                                                            $"  [OK] Precision@k: {score:F4} (time: {(DateTime.UtcNow - trialStart).TotalSeconds:F1}s)"

                                                    return (score, searchIndex)
                                                }

                                            return
                                                outcome
                                                |> Result.map (fun (score, searchIndex) ->
                                                    createSuccessResult score (SimilarityModel searchIndex))
                                                |> Result.orElseWith (fun e ->
                                                    if problemWithToken.Verbose then
                                                        logWarning problemWithToken.Logger $"  [FAIL] Failed: {e}"

                                                    Ok(createFailureResult e.Message))
                                    with ex when not (ex :? OperationCanceledException) ->
                                        if problemWithToken.Verbose then
                                            logError problemWithToken.Logger $"  [ERROR] Exception: {ex.Message}"

                                        return Ok(createFailureResult ex.Message)
                                }

                            return
                                result
                                |> Result.map (fun resultTuple -> Some resultTuple)
                                |> Result.defaultValue None
                    }

                // Task-based parallelization: each batch of trials runs concurrently (Task.Run)
                // and is awaited with Task.WhenAll before the next batch starts
                let maxDegreeOfParallelism = min 4 (trials.Length / 2 |> max 1)

                let completedTrials = ResizeArray<TrialResult * TrainedModel option>()

                for batch in trials |> List.chunkBySize maxDegreeOfParallelism do
                    let tasks =
                        batch
                        |> List.map (fun trial ->
                            Task.Run<(TrialResult * TrainedModel option) option>(
                                Func<Task<(TrialResult * TrainedModel option) option>>(fun () ->
                                    executeTrialAsync trial),
                                cancellationToken
                            ))
                        |> Array.ofList

                    let! batchResults = Task.WhenAll(tasks)
                    completedTrials.AddRange(batchResults |> Array.choose id)

                let resultsWithModels = List.ofSeq completedTrials
                let results = resultsWithModels |> List.map fst
                let totalTime = DateTime.UtcNow - startTime

                let bestResultWithModel =
                    resultsWithModels
                    |> List.filter (fun (r, _) -> r.Success)
                    |> List.sortByDescending (fun (r, _) -> r.Score)
                    |> List.tryHead

                match bestResultWithModel with
                | Some(bestTrial, Some bestModel) ->
                    let modelTypeStr =
                        match bestTrial.ModelType with
                        | BinaryClassification -> "Binary Classification"
                        | MultiClassClassification n -> $"Multi-Class Classification ({n} classes)"
                        | Regression -> "Regression"
                        | AnomalyDetection -> "Anomaly Detection"
                        | SimilaritySearch -> "Similarity Search"

                    let successfulTrials = results |> List.filter (fun r -> r.Success) |> List.length
                    let failedTrials = results |> List.filter (fun r -> not r.Success) |> List.length

                    let result =
                        {
                            BestModelType = modelTypeStr
                            BestArchitecture = bestTrial.Architecture
                            BestHyperparameters = bestTrial.Hyperparameters
                            Score = bestTrial.Score
                            AllTrials = results |> List.toArray
                            TotalSearchTime = totalTime
                            SuccessfulTrials = successfulTrials
                            FailedTrials = failedTrials
                            Model = bestModel
                            Metadata =
                                {
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

                        logInfo
                            problemWithToken.Logger
                            $"   Successful Trials: {result.SuccessfulTrials}/{results.Length}"

                        logInfo problemWithToken.Logger $"   Total Time: {result.TotalSearchTime.TotalSeconds:F1}s"

                    return Ok result

                | _ ->
                    return
                        Error(
                            QuantumError.OperationError(
                                "Operation",
                                "All trials failed - no model could be trained successfully"
                            )
                        )
        }

    // ========================================================================
    // PREDICTION - Use best model
    // ========================================================================

    /// Predict with AutoML result (wrapper for underlying model)
    let predictAsync
        (features: float array)
        (result: AutoMLResult)
        (cancellationToken: CancellationToken)
        : Task<QuantumResult<Prediction>> =
        task {
            try
                // Pattern-match the typed model (exhaustive — no unboxing, no "unsupported" arm).
                match result.Model with
                | BinaryModel model ->
                    let! prediction = BinaryClassifier.predictAsync features model cancellationToken
                    return prediction |> Result.map BinaryPrediction

                | MultiClassModel model ->
                    let! prediction =
                        PredictiveModel.predictCategoryAsync features model None None cancellationToken

                    return prediction |> Result.map CategoryPrediction

                | RegressionModel model ->
                    let! prediction =
                        PredictiveModel.predictAsync features model None None cancellationToken

                    return prediction |> Result.map RegressionPrediction

                | AnomalyModel detector ->
                    let! prediction = AnomalyDetector.checkAsync features detector cancellationToken
                    return prediction |> Result.map AnomalyPrediction

                | SimilarityModel searchIndex ->
                    // For similarity search, use the first index item as a query fallback
                    if searchIndex.Items.Length = 0 then
                        return Error(QuantumError.ValidationError("Input", "Similarity search index is empty"))
                    else
                        let firstItem, _ = searchIndex.Items.[0]
                        // Limit topN to number of items minus 1 (exclude query itself)
                        let topN = min 5 (searchIndex.Items.Length - 1) |> max 1

                        let! matches =
                            SimilaritySearch.findSimilarAsync firstItem features topN searchIndex cancellationToken

                        return matches |> Result.map SimilarityPrediction
            with ex when not (ex :? OperationCanceledException) ->
                return Error(QuantumError.ValidationError("Input", $"Prediction failed: {ex.Message}"))
        }

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
                TryMultiClass = None // Auto-detect
                TryAnomalyDetection = true
                TryRegression = true
                TrySimilaritySearch = false // Expensive
                TryArchitectures = [ Quantum; Hybrid ] // Classical kept internal but not default
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

        /// The `autoML { ... }` expression yields a task: write
        /// `let! result = autoML { ... }` inside `task { }`. The `cancellationToken`
        /// operation, when given, cancels the search.
        member _.Run(f: unit -> AutoMLProblem) : Task<QuantumResult<AutoMLResult>> =
            let problem = f ()
            searchAsync problem (problem.CancellationToken |> Option.defaultValue CancellationToken.None)

        member _.Combine(p1: AutoMLProblem, p2: AutoMLProblem) =
            { p2 with
                TrainFeatures =
                    if p2.TrainFeatures.Length = 0 then
                        p1.TrainFeatures
                    else
                        p2.TrainFeatures
                TrainLabels =
                    if p2.TrainLabels.Length = 0 then
                        p1.TrainLabels
                    else
                        p2.TrainLabels
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
                TryArchitectures = [ Quantum; Hybrid ] // Classical kept internal but not default
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
            { problem with
                TrainFeatures = features
                TrainLabels = labels
            }

        /// <summary>Enable or disable binary classification in the search space.</summary>
        /// <param name="enable">True to include binary classification</param>
        [<CustomOperation("tryBinaryClassification")>]
        member _.TryBinaryClassification(problem: AutoMLProblem, enable: bool) =
            { problem with
                TryBinaryClassification = enable
            }

        /// <summary>Enable multi-class classification with specified number of classes.</summary>
        /// <param name="numClasses">Number of classes for multi-class classification</param>
        [<CustomOperation("tryMultiClass")>]
        member _.TryMultiClass(problem: AutoMLProblem, numClasses: int) =
            { problem with
                TryMultiClass = Some numClasses
            }

        /// <summary>Enable or disable anomaly detection in the search space.</summary>
        /// <param name="enable">True to include anomaly detection</param>
        [<CustomOperation("tryAnomalyDetection")>]
        member _.TryAnomalyDetection(problem: AutoMLProblem, enable: bool) =
            { problem with
                TryAnomalyDetection = enable
            }

        /// <summary>Enable or disable regression in the search space.</summary>
        /// <param name="enable">True to include regression</param>
        [<CustomOperation("tryRegression")>]
        member _.TryRegression(problem: AutoMLProblem, enable: bool) = { problem with TryRegression = enable }

        /// <summary>Enable or disable similarity search in the search space.</summary>
        /// <param name="enable">True to include similarity search</param>
        [<CustomOperation("trySimilaritySearch")>]
        member _.TrySimilaritySearch(problem: AutoMLProblem, enable: bool) =
            { problem with
                TrySimilaritySearch = enable
            }

        /// <summary>Specify the architectures to try during optimization.</summary>
        /// <param name="architectures">List of architectures to evaluate</param>
        [<CustomOperation("tryArchitectures")>]
        member _.TryArchitectures(problem: AutoMLProblem, architectures: Architecture list) =
            { problem with
                TryArchitectures = architectures
            }

        /// <summary>Set the maximum number of trials for hyperparameter search.</summary>
        /// <param name="trials">Maximum number of trials</param>
        [<CustomOperation("maxTrials")>]
        member _.MaxTrials(problem: AutoMLProblem, trials: int) = { problem with MaxTrials = trials }

        /// <summary>Set the maximum time limit for AutoML search in minutes.</summary>
        /// <param name="minutes">Maximum time in minutes</param>
        [<CustomOperation("maxTimeMinutes")>]
        member _.MaxTimeMinutes(problem: AutoMLProblem, minutes: int) =
            { problem with
                MaxTimeMinutes = Some minutes
            }

        /// <summary>Set the validation split ratio for model evaluation.</summary>
        /// <param name="split">Validation split ratio (0.0 to 1.0)</param>
        [<CustomOperation("validationSplit")>]
        member _.ValidationSplit(problem: AutoMLProblem, split: float) =
            { problem with ValidationSplit = split }

        /// <summary>Set the quantum backend for execution.</summary>
        /// <param name="backend">Quantum backend instance</param>
        [<CustomOperation("backend")>]
        member _.Backend(problem: AutoMLProblem, backend: IQuantumBackend) = { problem with Backend = Some backend }

        /// <summary>Enable or disable verbose output.</summary>
        /// <param name="verbose">True to enable detailed logging</param>
        [<CustomOperation("verbose")>]
        member _.Verbose(problem: AutoMLProblem, verbose: bool) = { problem with Verbose = verbose }

        /// <summary>Set the path to save the best model found.</summary>
        /// <param name="path">File path for saving the model</param>
        [<CustomOperation("saveModelTo")>]
        member _.SaveModelTo(problem: AutoMLProblem, path: string) = { problem with SavePath = Some path }

        /// <summary>Set the random seed for reproducibility.</summary>
        /// <param name="seed">Random seed value</param>
        [<CustomOperation("randomSeed")>]
        member _.RandomSeed(problem: AutoMLProblem, seed: int) = { problem with RandomSeed = Some seed }

        /// <summary>Set a progress reporter for real-time updates.</summary>
        /// <param name="reporter">Progress reporter instance</param>
        [<CustomOperation("progressReporter")>]
        member _.ProgressReporter(problem: AutoMLProblem, reporter: Core.Progress.IProgressReporter) =
            { problem with
                ProgressReporter = Some reporter
            }

        /// <summary>Set a cancellation token for early termination.</summary>
        /// <param name="token">Cancellation token</param>
        [<CustomOperation("cancellationToken")>]
        member _.CancellationToken(problem: AutoMLProblem, token: CancellationToken) =
            { problem with
                CancellationToken = Some token
            }

    /// Create AutoML computation expression
    let autoML = AutoMLBuilder()
