namespace FSharp.Azure.Quantum.Business

open FSharp.Azure.Quantum.Backends
open FSharp.Azure.Quantum.Core
open System
open System.Threading
open System.Threading.Tasks
open FSharp.Azure.Quantum.Core.BackendAbstraction
open FSharp.Azure.Quantum.MachineLearning
open FSharp.Azure.Quantum
open Microsoft.Extensions.Logging

/// High-Level Binary Classification Builder - Business-First API
///
/// DESIGN PHILOSOPHY:
/// This is a BUSINESS DOMAIN API for classifying items into two categories
/// without understanding quantum circuits, feature maps, or optimization algorithms.
///
/// WHAT IS BINARY CLASSIFICATION:
/// Automatically categorize items into one of two groups based on their characteristics.
/// Examples: spam/not-spam, fraud/legitimate, churn/retain, approve/reject.
///
/// USE CASES:
/// - Fraud detection: Identify suspicious transactions
/// - Spam filtering: Classify emails as spam or legitimate
/// - Churn prediction: Identify customers likely to leave
/// - Credit risk: Approve or reject loan applications
/// - Quality control: Detect defective products
/// - Medical diagnosis: Detect disease presence/absence
///
/// EXAMPLE USAGE (the builder yields a Task, so bind it inside task { }):
///   // Simple: Train from data arrays
///   let! classifier = binaryClassification {
///       trainWith trainX trainY
///   }
///
///   // Predict
///   let! result = BinaryClassifier.predictAsync newSample classifier cancellationToken
///   if result.IsPositive then
///       blockTransaction()
///
///   // Advanced: Full configuration
///   let! classifier = binaryClassification {
///       trainWith trainX trainY
///
///       // Architecture (optional - has smart defaults)
///       architecture Quantum  // or Hybrid, or Classical
///
///       // Training (optional)
///       learningRate 0.01
///       maxEpochs 100
///
///       // Infrastructure (optional)
///       backend azureBackend
///
///       // Persistence (optional)
///       saveModelTo "fraud_detector.model"
///   }
module BinaryClassifier =

    // ========================================================================
    // CORE TYPES - Binary Classification Domain Model
    // ========================================================================

    /// Architecture choice for classification
    type Architecture =
        /// Pure quantum classifier (VQC)
        | Quantum
        /// Quantum feature extraction + classical SVM
        | Hybrid
        /// Classical baseline for comparison
        | Classical

    /// Binary classification problem specification
    type ClassificationProblem =
        {
            /// Training features (samples × features)
            TrainFeatures: float array array

            /// Training labels (0 or 1)
            TrainLabels: int array

            /// Architecture to use
            Architecture: Architecture

            /// Learning rate for training
            LearningRate: float

            /// Maximum training epochs
            MaxEpochs: int

            /// Convergence threshold
            ConvergenceThreshold: float

            /// Quantum backend (None = LocalBackend)
            Backend: IQuantumBackend option

            /// Number of measurement shots
            Shots: int

            /// Verbose logging
            Verbose: bool

            /// Path to save trained model
            SavePath: string option

            /// Optional note about the model
            Note: string option

            /// Optional progress reporter for real-time updates
            ProgressReporter: Core.Progress.IProgressReporter option

            /// Optional cancellation token for early termination
            CancellationToken: CancellationToken option

            /// Optional structured logger
            Logger: ILogger option
        }

    /// Trained binary classifier
    type Classifier =
        {
            /// Underlying model
            Model: ClassifierModel

            /// Training metadata
            Metadata: ClassifierMetadata

            /// Backend used for training/prediction
            Backend: IQuantumBackend
        }

    and ClassifierModel =
        | VQCModel of
            result: VQC.TrainingResult *
            featureMap: FeatureMapType *
            varForm: VariationalForm *
            numQubits: int
        /// Model * NumQubits
        | SVMModel of QuantumKernelSVM.SVMModel * int
        /// Simple weights
        | ClassicalModel of float array

    and ClassifierMetadata =
        {
            Architecture: Architecture
            TrainingAccuracy: float
            TrainingTime: TimeSpan
            NumFeatures: int
            NumSamples: int
            CreatedAt: DateTime
            Note: string option
        }

    /// Prediction result
    [<Struct>]
    type Prediction =
        {
            /// Predicted class (0 or 1)
            Label: int

            /// Confidence score [0, 1]
            Confidence: float

            /// Is positive class (label = 1)
            IsPositive: bool

            /// Is negative class (label = 0)
            IsNegative: bool
        }

    /// Evaluation metrics
    type EvaluationMetrics =
        {
            Accuracy: float
            Precision: float
            Recall: float
            F1Score: float
            TruePositives: int
            TrueNegatives: int
            FalsePositives: int
            FalseNegatives: int
        }

    // ========================================================================
    // VALIDATION
    // ========================================================================

    /// Validate classification problem
    let private validate (problem: ClassificationProblem) : QuantumResult<unit> =
        if problem.TrainFeatures.Length = 0 then
            Error(QuantumError.ValidationError("Input", "Training features cannot be empty"))
        elif problem.TrainLabels.Length = 0 then
            Error(QuantumError.ValidationError("Input", "Training labels cannot be empty"))
        elif problem.TrainFeatures.Length <> problem.TrainLabels.Length then
            Error(
                QuantumError.ValidationError(
                    "Input",
                    $"Features ({problem.TrainFeatures.Length}) and labels ({problem.TrainLabels.Length}) must have same length"
                )
            )
        elif problem.TrainLabels |> Array.exists (fun l -> l <> 0 && l <> 1) then
            Error(QuantumError.ValidationError("Input", "Labels must be 0 or 1 for binary classification"))
        elif problem.LearningRate <= 0.0 then
            Error(QuantumError.ValidationError("Input", "Learning rate must be positive"))
        elif problem.MaxEpochs < 1 then
            Error(QuantumError.ValidationError("Input", "MaxEpochs must be at least 1"))
        elif problem.Shots < 1 then
            Error(QuantumError.ValidationError("Input", "Shots must be at least 1"))
        else
            let numFeatures = problem.TrainFeatures.[0].Length

            let allSameLength =
                problem.TrainFeatures |> Array.forall (fun x -> x.Length = numFeatures)

            if not allSameLength then
                Error(QuantumError.ValidationError("Input", "All feature vectors must have the same length"))
            else
                Ok()

    // ========================================================================
    // TRAINING
    // ========================================================================

    /// VQC builds circuits with one qubit per feature dimension (see
    /// VQC.buildVQCCircuit). When the feature count exceeds the qubit cap, samples
    /// are truncated to the first numQubits dimensions — consistently at training
    /// and prediction time — so the circuit matches the sized parameter vector.
    let private truncateFeatures (numQubits: int) (sample: float array) : float array =
        if sample.Length > numQubits then
            Array.sub sample 0 numQubits
        else
            sample

    /// Train quantum VQC classifier
    let private trainQuantumAsync
        (backend: IQuantumBackend)
        (features: float array array)
        (labels: int array)
        (config: ClassificationProblem)
        (cancellationToken: CancellationToken)
        : Task<QuantumResult<Classifier>> =

        let startTime = DateTime.UtcNow
        let numFeatures = features.[0].Length

        // Smart defaults for quantum architecture
        let maxQubits = 8 // Cap for reasonable local simulation time
        let numQubits = min numFeatures maxQubits
        let featureMap = FeatureMapType.ZZFeatureMap 2
        let variationalForm = VariationalForm.RealAmplitudes 2

        // VQC uses one qubit per feature dimension. Refuse feature counts above
        // the qubit cap rather than silently truncating: a classifier that
        // ignores most of its input features while reporting success is worse
        // than a clear error the user can act on.
        if numFeatures > maxQubits then
            Task.FromResult(
                Error(
                    QuantumError.ValidationError(
                        "features",
                        $"Quantum classification supports at most {maxQubits} features (one qubit per feature; {numFeatures} supplied). Reduce dimensionality first (e.g. feature selection or PCA), or use the Classical architecture."
                    )
                )
            )
        else

            let trainFeatures = features

            // Training configuration
            let trainConfig =
                {
                    VQC.LearningRate = config.LearningRate
                    VQC.MaxEpochs = config.MaxEpochs
                    VQC.ConvergenceThreshold = config.ConvergenceThreshold
                    VQC.Shots = config.Shots
                    VQC.Verbose = config.Verbose
                    VQC.Optimizer =
                        VQC.Adam
                            {
                                AdamOptimizer.LearningRate = config.LearningRate
                                Beta1 = 0.9
                                Beta2 = 0.999
                                Epsilon = 1e-8
                            }
                    VQC.ProgressReporter = config.ProgressReporter
                    VQC.Logger = None
                }

            // Train VQC (initialize parameters randomly)
            let numParams = AnsatzHelpers.parameterCount variationalForm numQubits
            let rng = Random()
            let initialParams = Array.init numParams (fun _ -> rng.NextDouble() * 2.0 * Math.PI)

            quantumResultTask {
                let! result =
                    VQC.train backend featureMap variationalForm initialParams trainFeatures labels trainConfig
                    |> Result.mapError (fun e -> QuantumError.ValidationError("Input", $"VQC training failed: {e}"))

                let endTime = DateTime.UtcNow

                let classifier =
                    {
                        Model = VQCModel(result, featureMap, variationalForm, numQubits)
                        Metadata =
                            {
                                Architecture = Quantum
                                TrainingAccuracy = result.TrainAccuracy
                                TrainingTime = endTime - startTime
                                NumFeatures = numFeatures
                                NumSamples = features.Length
                                CreatedAt = startTime
                                Note = config.Note
                            }
                        Backend = backend
                    }

                // Save if requested
                match config.SavePath with
                | None -> ()
                | Some path ->
                    let note =
                        match config.Note with
                        | Some n -> Some n
                        | None ->
                            Some(sprintf "Binary classifier trained %s" (startTime.ToString "yyyy-MM-dd HH:mm:ss"))

                    // Model save failure is non-fatal: the trained classifier is still valid.
                    // Callers who need durable persistence should use BinaryClassifier.saveAsync
                    // explicitly and handle the Result. We do not use printfn in library code.
                    let! (_saved: QuantumResult<unit> option) =
                        task {
                            let! saved =
                                ModelSerialization.saveVQCTrainingResultAsync
                                    path
                                    result
                                    numQubits
                                    "ZZFeatureMap"
                                    2
                                    "RealAmplitudes"
                                    2
                                    note
                                    cancellationToken

                            return Some saved
                        }

                    ()

                return classifier
            }

    /// Train hybrid quantum-classical classifier
    let private trainHybridAsync
        (backend: IQuantumBackend)
        (features: float array array)
        (labels: int array)
        (config: ClassificationProblem)
        (cancellationToken: CancellationToken)
        : Task<QuantumResult<Classifier>> =

        let startTime = DateTime.UtcNow
        let numFeatures = features.[0].Length

        // Use quantum kernel SVM
        let numQubits = min numFeatures 8
        let featureMap = FeatureMapType.ZZFeatureMap 2

        let svmConfig: QuantumKernelSVM.SVMConfig =
            {
                C = 1.0
                Tolerance = 0.001
                MaxIterations = 1000
                Verbose = config.Verbose
                Logger = config.Logger
            }

        quantumResultTask {
            let! model =
                QuantumKernelSVM.train backend featureMap features labels svmConfig config.Shots
                |> Result.mapError (fun e -> QuantumError.ValidationError("Input", $"Hybrid training failed: {e}"))

            let endTime = DateTime.UtcNow

            // Compute training accuracy - propagate prediction errors
            let predictions = ResizeArray<int>(features.Length)

            for x in features do
                let! label =
                    task {
                        let! prediction =
                            QuantumKernelSVM.predictAsync backend model x config.Shots cancellationToken

                        return
                            prediction
                            |> Result.map (fun pred -> pred.Label)
                            |> Result.mapError (fun err ->
                                QuantumError.ValidationError(
                                    "Training",
                                    $"Prediction failed during accuracy computation: {err}"
                                ))
                    }

                predictions.Add label

            let correct =
                Seq.zip predictions labels |> Seq.filter (fun (p, l) -> p = l) |> Seq.length

            let accuracy = float correct / float labels.Length

            return
                {
                    Model = SVMModel(model, numQubits)
                    Metadata =
                        {
                            Architecture = Hybrid
                            TrainingAccuracy = accuracy
                            TrainingTime = endTime - startTime
                            NumFeatures = numFeatures
                            NumSamples = features.Length
                            CreatedAt = startTime
                            Note = config.Note
                        }
                    Backend = backend
                }
        }

    /// Train classifier based on architecture choice
    let trainAsync
        (problem: ClassificationProblem)
        (cancellationToken: CancellationToken)
        : Task<QuantumResult<Classifier>> =
        quantumResultTask {
            do! validate problem

            let backend =
                match problem.Backend with
                | Some b -> b
                | None -> LocalBackend.LocalBackend() :> IQuantumBackend // Default to local simulation

            match problem.Architecture with
            | Quantum ->
                return! trainQuantumAsync backend problem.TrainFeatures problem.TrainLabels problem cancellationToken
            | Hybrid ->
                return! trainHybridAsync backend problem.TrainFeatures problem.TrainLabels problem cancellationToken
            | Classical ->
                return!
                    Error(
                        QuantumError.NotImplemented(
                            "Classical architecture",
                            Some
                                "Use PredictiveModelBuilder for classical baselines, or use Hybrid architecture for quantum-classical classification"
                        )
                    )
        }

    /// Train classifier based on architecture choice
    [<Obsolete("Use trainAsync for non-blocking execution against cloud backends")>]
    let train (problem: ClassificationProblem) : QuantumResult<Classifier> =
        trainAsync problem CancellationToken.None
        |> Async.AwaitTask
        |> Async.RunSynchronously

    // ========================================================================
    // PREDICTION
    // ========================================================================

    /// Make prediction on new sample
    let predictAsync
        (sample: float array)
        (classifier: Classifier)
        (cancellationToken: CancellationToken)
        : Task<QuantumResult<Prediction>> =
        let backend = classifier.Backend

        match classifier.Model with
        | VQCModel(result, featureMap, varForm, numQubits) ->
            // Apply the same feature truncation used at training time (qubit cap)
            task {
                let! prediction =
                    VQC.predictAsync
                        backend
                        featureMap
                        varForm
                        result.Parameters
                        (truncateFeatures numQubits sample)
                        1000
                        cancellationToken

                return
                    prediction
                    |> Result.map (fun vqcPred ->
                        {
                            Label = vqcPred.Label
                            Confidence = vqcPred.Probability
                            IsPositive = vqcPred.Label = 1
                            IsNegative = vqcPred.Label = 0
                        })
            }

        | SVMModel(model, _storedNumQubits) ->
            task {
                let! prediction =
                    QuantumKernelSVM.predictAsync backend model sample 1000 cancellationToken

                return
                    prediction
                    |> Result.map (fun prediction ->
                        // Convert decision value to confidence (sigmoid-like transformation)
                        let confidence = 1.0 / (1.0 + exp (-abs prediction.DecisionValue))

                        {
                            Label = prediction.Label
                            Confidence = confidence
                            IsPositive = prediction.Label = 1
                            IsNegative = prediction.Label = 0
                        })
            }

        | ClassicalModel _ ->
            Task.FromResult(
                Error(
                    QuantumError.NotImplemented(
                        "Classical model prediction",
                        Some "Use PredictiveModelBuilder for classical baselines"
                    )
                )
            )

    /// Make prediction on new sample
    [<Obsolete("Use predictAsync for non-blocking execution against cloud backends")>]
    let predict (sample: float array) (classifier: Classifier) : QuantumResult<Prediction> =
        predictAsync sample classifier CancellationToken.None
        |> Async.AwaitTask
        |> Async.RunSynchronously

    /// Evaluate classifier on test set
    let evaluateAsync
        (testFeatures: float array array)
        (testLabels: int array)
        (classifier: Classifier)
        (cancellationToken: CancellationToken)
        : Task<QuantumResult<EvaluationMetrics>> =
        quantumResultTask {
            if testFeatures.Length <> testLabels.Length then
                return! Error(QuantumError.ValidationError("Input", "Test features and labels must have same length"))
            else
                // Make predictions - propagate errors instead of silently defaulting to 0
                let predictionLabels = ResizeArray<int>(testFeatures.Length)

                for x in testFeatures do
                    let! pred = predictAsync x classifier cancellationToken
                    predictionLabels.Add pred.Label

                let predictions = predictionLabels.ToArray()

                // Compute confusion matrix
                let tp =
                    Array.zip predictions testLabels
                    |> Array.filter (fun (p, l) -> p = 1 && l = 1)
                    |> Array.length

                let tn =
                    Array.zip predictions testLabels
                    |> Array.filter (fun (p, l) -> p = 0 && l = 0)
                    |> Array.length

                let fp =
                    Array.zip predictions testLabels
                    |> Array.filter (fun (p, l) -> p = 1 && l = 0)
                    |> Array.length

                let fn =
                    Array.zip predictions testLabels
                    |> Array.filter (fun (p, l) -> p = 0 && l = 1)
                    |> Array.length

                // Compute metrics
                let accuracy = float (tp + tn) / float testLabels.Length
                let precision = if (tp + fp) = 0 then 0.0 else float tp / float (tp + fp)
                let recall = if (tp + fn) = 0 then 0.0 else float tp / float (tp + fn)

                let f1 =
                    if (precision + recall) = 0.0 then
                        0.0
                    else
                        2.0 * precision * recall / (precision + recall)

                return
                    {
                        Accuracy = accuracy
                        Precision = precision
                        Recall = recall
                        F1Score = f1
                        TruePositives = tp
                        TrueNegatives = tn
                        FalsePositives = fp
                        FalseNegatives = fn
                    }
        }

    /// Evaluate classifier on test set
    [<Obsolete("Use evaluateAsync for non-blocking execution against cloud backends")>]
    let evaluate
        (testFeatures: float array array)
        (testLabels: int array)
        (classifier: Classifier)
        : QuantumResult<EvaluationMetrics> =
        evaluateAsync testFeatures testLabels classifier CancellationToken.None
        |> Async.AwaitTask
        |> Async.RunSynchronously

    // ========================================================================
    // PERSISTENCE
    // ========================================================================

    /// Save classifier to file
    let saveAsync
        (path: string)
        (classifier: Classifier)
        (cancellationToken: CancellationToken)
        : Task<QuantumResult<unit>> =
        match classifier.Model with
        | VQCModel(result, featureMap, varForm, numQubits) ->
            let fmType =
                match featureMap with
                | ZZFeatureMap _ -> "ZZFeatureMap"
                | _ -> "Unknown"

            let fmDepth =
                match featureMap with
                | ZZFeatureMap d -> d
                | _ -> 0

            let vfType =
                match varForm with
                | RealAmplitudes _ -> "RealAmplitudes"
                | TwoLocal _
                | EfficientSU2 _ -> "Unknown"

            let vfDepth =
                match varForm with
                | RealAmplitudes d -> d
                | TwoLocal _
                | EfficientSU2 _ -> 0

            ModelSerialization.saveVQCTrainingResultAsync
                path
                result
                numQubits
                fmType
                fmDepth
                vfType
                vfDepth
                classifier.Metadata.Note
                cancellationToken

        | SVMModel(svmModel, _numQubits) ->
            // numQubits is recoverable from the feature dimension on load, so it isn't
            // separately persisted; use the canonical SVM schema (SVMModelSerialization).
            SVMModelSerialization.saveSVMModelAsync path svmModel classifier.Metadata.Note cancellationToken

        | ClassicalModel _ ->
            Task.FromResult(
                Error(
                    QuantumError.NotImplemented(
                        "Classical model persistence",
                        Some "Use PredictiveModelBuilder for classical baselines"
                    )
                )
            )

    /// Save classifier to file
    [<Obsolete("Use saveAsync for non-blocking file I/O")>]
    let save (path: string) (classifier: Classifier) : QuantumResult<unit> =
        saveAsync path classifier CancellationToken.None
        |> Async.AwaitTask
        |> Async.RunSynchronously

    /// Load classifier from file
    let load (path: string) : QuantumResult<Classifier> =
        // Detect model type by checking JSON structure
        try
            let json = System.IO.File.ReadAllText(path)

            // Check if it's an SVM model (has SupportVectorIndices field)
            if json.Contains "\"SupportVectorIndices\"" then
                // Load as SVM (canonical SVMModelSerialization schema). The qubit/feature count
                // is derived from the training-data dimension rather than a stored field.
                let serialized =
                    System.Text.Json.JsonSerializer.Deserialize<SVMModelSerialization.SerializableSVMModel>(json)

                SVMModelSerialization.fromSerializable serialized
                |> Result.mapError (fun e -> QuantumError.ValidationError("Input", $"Failed to load SVM model: {e}"))
                |> Result.map (fun svmModel ->
                    let numFeatures =
                        if svmModel.TrainData.Length > 0 then
                            svmModel.TrainData.[0].Length
                        else
                            0

                    {
                        Model = SVMModel(svmModel, numFeatures)
                        Metadata =
                            {
                                Architecture = Hybrid
                                TrainingAccuracy = 0.0
                                TrainingTime = TimeSpan.Zero
                                NumFeatures = numFeatures
                                NumSamples = svmModel.TrainData.Length
                                CreatedAt = DateTime.UtcNow
                                Note = serialized.Note
                            }
                        Backend = LocalBackend.LocalBackend() :> IQuantumBackend
                    })
            else
                // Load as VQC model
                ModelSerialization.loadForTransferLearning path
                |> Result.map (fun (parameters, (numQubits, fmType, fmDepth, vfType, vfDepth)) ->
                    // Reconstruct VQC model
                    let featureMap =
                        match fmType with
                        | "ZZFeatureMap" -> FeatureMapType.ZZFeatureMap fmDepth
                        | _ -> FeatureMapType.ZZFeatureMap 2

                    let varForm =
                        match vfType with
                        | "RealAmplitudes" -> VariationalForm.RealAmplitudes vfDepth
                        | _ -> VariationalForm.RealAmplitudes 2

                    let result: VQC.TrainingResult =
                        {
                            Parameters = parameters
                            LossHistory = []
                            Epochs = 0
                            TrainAccuracy = 0.0
                            Converged = true
                        }

                    {
                        Model = VQCModel(result, featureMap, varForm, numQubits)
                        Metadata =
                            {
                                Architecture = Quantum
                                TrainingAccuracy = 0.0
                                TrainingTime = TimeSpan.Zero
                                NumFeatures = numQubits
                                NumSamples = 0
                                CreatedAt = DateTime.UtcNow
                                Note = None
                            }
                        Backend = LocalBackend.LocalBackend() :> IQuantumBackend
                    })
        with ex ->
            Error(QuantumError.ValidationError("Input", $"Failed to load model: {ex.Message}"))

    // ========================================================================
    // COMPUTATION EXPRESSION BUILDER
    // ========================================================================

    /// Computation expression builder for binary classification
    type BinaryClassificationBuilder() =

        member _.Yield(_) : ClassificationProblem =
            {
                TrainFeatures = [||]
                TrainLabels = [||]
                Architecture = Quantum
                LearningRate = 0.01
                MaxEpochs = 100
                ConvergenceThreshold = 0.001
                Backend = None
                Shots = 1000
                Verbose = false
                SavePath = None
                Note = None
                ProgressReporter = None
                CancellationToken = None
                Logger = None
            }

        member _.Delay(f: unit -> ClassificationProblem) = f

        /// The `binaryClassification { ... }` expression yields a task: write
        /// `let! classifier = binaryClassification { ... }` inside `task { }`. The
        /// `cancellationToken` operation, when given, cancels training.
        member _.Run(f: unit -> ClassificationProblem) : Task<QuantumResult<Classifier>> =
            let problem = f ()
            trainAsync problem (problem.CancellationToken |> Option.defaultValue CancellationToken.None)

        member _.Combine(p1: ClassificationProblem, p2: ClassificationProblem) =
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

        member _.Zero() : ClassificationProblem =
            {
                TrainFeatures = [||]
                TrainLabels = [||]
                Architecture = Quantum
                LearningRate = 0.01
                MaxEpochs = 100
                ConvergenceThreshold = 0.001
                Backend = None
                Shots = 1000
                Verbose = false
                SavePath = None
                Note = None
                ProgressReporter = None
                CancellationToken = None
                Logger = None
            }

        /// <summary>Set the training data with features and binary labels.</summary>
        /// <param name="features">Training feature vectors</param>
        /// <param name="labels">Binary labels (0 or 1) for each sample</param>
        [<CustomOperation("trainWith")>]
        member _.TrainWith(problem: ClassificationProblem, features: float array array, labels: int array) =
            { problem with
                TrainFeatures = features
                TrainLabels = labels
            }

        /// <summary>Set the neural network architecture.</summary>
        /// <param name="arch">Architecture specification</param>
        [<CustomOperation("architecture")>]
        member _.Architecture(problem: ClassificationProblem, arch: Architecture) = { problem with Architecture = arch }

        /// <summary>Set the learning rate for optimization.</summary>
        /// <param name="lr">Learning rate (typically 0.001 to 0.1)</param>
        [<CustomOperation("learningRate")>]
        member _.LearningRate(problem: ClassificationProblem, lr: float) = { problem with LearningRate = lr }

        /// <summary>Set the maximum number of training epochs.</summary>
        /// <param name="epochs">Maximum epochs</param>
        [<CustomOperation("maxEpochs")>]
        member _.MaxEpochs(problem: ClassificationProblem, epochs: int) = { problem with MaxEpochs = epochs }

        /// <summary>Set the convergence threshold for early stopping.</summary>
        /// <param name="threshold">Convergence threshold for loss improvement</param>
        [<CustomOperation("convergenceThreshold")>]
        member _.ConvergenceThreshold(problem: ClassificationProblem, threshold: float) =
            { problem with
                ConvergenceThreshold = threshold
            }

        /// <summary>Set the quantum backend for execution.</summary>
        /// <param name="backend">Quantum backend instance</param>
        [<CustomOperation("backend")>]
        member _.Backend(problem: ClassificationProblem, backend: IQuantumBackend) =
            { problem with Backend = Some backend }

        /// <summary>Set the number of measurement shots.</summary>
        /// <param name="shots">Number of circuit measurements</param>
        [<CustomOperation("shots")>]
        member _.Shots(problem: ClassificationProblem, shots: int) = { problem with Shots = shots }

        /// <summary>Enable or disable verbose output.</summary>
        /// <param name="verbose">True to enable detailed logging</param>
        [<CustomOperation("verbose")>]
        member _.Verbose(problem: ClassificationProblem, verbose: bool) = { problem with Verbose = verbose }

        /// <summary>Set the path to save the trained model.</summary>
        /// <param name="path">File path for saving the model</param>
        [<CustomOperation("saveModelTo")>]
        member _.SaveModelTo(problem: ClassificationProblem, path: string) = { problem with SavePath = Some path }

        /// <summary>Add a note or description to the classification problem.</summary>
        /// <param name="note">Descriptive note</param>
        [<CustomOperation("note")>]
        member _.Note(problem: ClassificationProblem, note: string) = { problem with Note = Some note }

        /// <summary>Set a progress reporter for real-time training updates.</summary>
        /// <param name="reporter">Progress reporter instance</param>
        [<CustomOperation("progressReporter")>]
        member _.ProgressReporter(problem: ClassificationProblem, reporter: Core.Progress.IProgressReporter) =
            { problem with
                ProgressReporter = Some reporter
            }

        /// <summary>Set a cancellation token for early termination of training.</summary>
        /// <param name="token">Cancellation token</param>
        [<CustomOperation("cancellationToken")>]
        member _.CancellationToken(problem: ClassificationProblem, token: CancellationToken) =
            { problem with
                CancellationToken = Some token
            }

    /// Create binary classification computation expression
    let binaryClassification = BinaryClassificationBuilder()
