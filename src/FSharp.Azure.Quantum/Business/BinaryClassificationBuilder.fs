namespace FSharp.Azure.Quantum.Business

open FSharp.Azure.Quantum.Core
open System
open FSharp.Azure.Quantum.Core.BackendAbstraction
open FSharp.Azure.Quantum.MachineLearning
open FSharp.Azure.Quantum

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
/// EXAMPLE USAGE:
///   // Simple: Train from data arrays
///   let classifier = binaryClassification {
///       trainWith trainX trainY
///   }
///   
///   // Predict
///   let result = classifier |> BinaryClassifier.predict newSample
///   if result.IsPositive then
///       blockTransaction()
///   
///   // Advanced: Full configuration
///   let classifier = binaryClassification {
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
        | Quantum        // Pure quantum classifier (VQC)
        | Hybrid         // Quantum feature extraction + classical SVM
        | Classical      // Classical baseline for comparison
    
    /// Binary classification problem specification
    type ClassificationProblem = {
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
        CancellationToken: System.Threading.CancellationToken option
    }
    
    /// Trained binary classifier
    type Classifier = {
        /// Underlying model
        Model: ClassifierModel
        
        /// Training metadata
        Metadata: ClassifierMetadata
    }
    
    and ClassifierModel =
        | VQCModel of VQC.TrainingResult * FeatureMapType * VariationalForm * int
        | SVMModel of QuantumKernelSVM.SVMModel * int  // Model * NumQubits
        | ClassicalModel of float array  // Simple weights
    
    and ClassifierMetadata = {
        Architecture: Architecture
        TrainingAccuracy: float
        TrainingTime: TimeSpan
        NumFeatures: int
        NumSamples: int
        CreatedAt: DateTime
        Note: string option
    }
    
    /// Prediction result
    type Prediction = {
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
    type EvaluationMetrics = {
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
            Error (QuantumError.ValidationError ("Input", "Training features cannot be empty"))
        elif problem.TrainLabels.Length = 0 then
            Error (QuantumError.ValidationError ("Input", "Training labels cannot be empty"))
        elif problem.TrainFeatures.Length <> problem.TrainLabels.Length then
            Error (QuantumError.ValidationError ("Input", $"Features ({problem.TrainFeatures.Length}) and labels ({problem.TrainLabels.Length}) must have same length"))
        elif problem.TrainLabels |> Array.exists (fun l -> l <> 0 && l <> 1) then
            Error (QuantumError.ValidationError ("Input", "Labels must be 0 or 1 for binary classification"))
        elif problem.LearningRate <= 0.0 then
            Error (QuantumError.ValidationError ("Input", "Learning rate must be positive"))
        elif problem.MaxEpochs < 1 then
            Error (QuantumError.ValidationError ("Input", "MaxEpochs must be at least 1"))
        elif problem.Shots < 1 then
            Error (QuantumError.ValidationError ("Input", "Shots must be at least 1"))
        else
            let numFeatures = problem.TrainFeatures.[0].Length
            let allSameLength = problem.TrainFeatures |> Array.forall (fun x -> x.Length = numFeatures)
            if not allSameLength then
                Error (QuantumError.ValidationError ("Input", "All feature vectors must have the same length"))
            else
                Ok ()
    
    // ========================================================================
    // TRAINING
    // ========================================================================
    
    /// Train quantum VQC classifier
    let private trainQuantum 
        (backend: IQuantumBackend)
        (features: float array array)
        (labels: int array)
        (config: ClassificationProblem)
        : QuantumResult<Classifier> =
        
        let startTime = DateTime.UtcNow
        let numFeatures = features.[0].Length
        
        // Smart defaults for quantum architecture
        let numQubits = min numFeatures 8  // Cap at 8 qubits for reasonable simulation time
        let featureMap = FeatureMapType.ZZFeatureMap 2
        let variationalForm = VariationalForm.RealAmplitudes 2
        
        // Training configuration
        let trainConfig = {
            VQC.LearningRate = config.LearningRate
            VQC.MaxEpochs = config.MaxEpochs
            VQC.ConvergenceThreshold = config.ConvergenceThreshold
            VQC.Shots = config.Shots
            VQC.Verbose = config.Verbose
            VQC.Optimizer = VQC.Adam {
                AdamOptimizer.LearningRate = config.LearningRate
                Beta1 = 0.9
                Beta2 = 0.999
                Epsilon = 1e-8
            }
        }
        
        // Train VQC (initialize parameters randomly)
        let numParams = AnsatzHelpers.parameterCount variationalForm numQubits
        let initialParams = Array.init numParams (fun _ -> Random().NextDouble() * 2.0 * Math.PI)
        
        match VQC.train backend featureMap variationalForm initialParams features labels trainConfig with
        | Error e -> Error (QuantumError.ValidationError ("Input", $"VQC training failed: {e}"))
        | Ok result ->
            
            let endTime = DateTime.UtcNow
            
            let classifier = {
                Model = VQCModel (result, featureMap, variationalForm, numQubits)
                Metadata = {
                    Architecture = Quantum
                    TrainingAccuracy = result.TrainAccuracy
                    TrainingTime = endTime - startTime
                    NumFeatures = numFeatures
                    NumSamples = features.Length
                    CreatedAt = startTime
                    Note = config.Note
                }
            }
            
            // Save if requested
            match config.SavePath with
            | None -> ()
            | Some path ->
                let note = 
                    match config.Note with
                    | Some n -> Some n
                    | None -> Some (sprintf "Binary classifier trained %s" (startTime.ToString("yyyy-MM-dd HH:mm:ss")))
                
                match ModelSerialization.saveVQCTrainingResult 
                        path result numQubits "ZZFeatureMap" 2 "RealAmplitudes" 2 note with
                | Error e -> printfn "Warning: Failed to save model: %s" e.Message
                | Ok () -> ()
            
            Ok classifier
    
    /// Train hybrid quantum-classical classifier
    let private trainHybrid
        (backend: IQuantumBackend)
        (features: float array array)
        (labels: int array)
        (config: ClassificationProblem)
        : QuantumResult<Classifier> =
        
        let startTime = DateTime.UtcNow
        let numFeatures = features.[0].Length
        
        // Use quantum kernel SVM
        let numQubits = min numFeatures 8
        let featureMap = FeatureMapType.ZZFeatureMap 2
        
        let svmConfig : QuantumKernelSVM.SVMConfig = {
            C = 1.0
            Tolerance = 0.001
            MaxIterations = 1000
            Verbose = config.Verbose
        }
        
        match QuantumKernelSVM.train backend featureMap features labels svmConfig config.Shots with
        | Error e -> Error (QuantumError.ValidationError ("Input", $"Hybrid training failed: {e}"))
        | Ok model ->
            
            let endTime = DateTime.UtcNow
            
            // Compute training accuracy
            let predictions = features |> Array.map (fun x -> 
                match QuantumKernelSVM.predict backend model x config.Shots with
                | Ok pred -> pred.Label
                | Error _ -> 0)
            
            let correct = Array.zip predictions labels |> Array.filter (fun (p, l) -> p = l) |> Array.length
            let accuracy = float correct / float labels.Length
            
            let classifier = {
                Model = SVMModel (model, numQubits)
                Metadata = {
                    Architecture = Hybrid
                    TrainingAccuracy = accuracy
                    TrainingTime = endTime - startTime
                    NumFeatures = numFeatures
                    NumSamples = features.Length
                    CreatedAt = startTime
                    Note = config.Note
                }
            }
            
            Ok classifier
    
    /// Train classifier based on architecture choice
    let train (problem: ClassificationProblem) : QuantumResult<Classifier> =
        match validate problem with
        | Error e -> Error e
        | Ok () ->
            
            let backend = 
                match problem.Backend with
                | Some b -> b
                | None -> LocalBackend() :> IQuantumBackend  // Default to local simulation
            
            // Set cancellation token on backend if provided
            problem.CancellationToken |> Option.iter (fun token ->
                backend.SetCancellationToken(Some token))
            
            match problem.Architecture with
            | Quantum -> trainQuantum backend problem.TrainFeatures problem.TrainLabels problem
            | Hybrid -> trainHybrid backend problem.TrainFeatures problem.TrainLabels problem
            | Classical -> Error (QuantumError.NotImplemented ("Classical architecture", None))
    
    // ========================================================================
    // PREDICTION
    // ========================================================================
    
    /// Make prediction on new sample
    let predict (sample: float array) (classifier: Classifier) : QuantumResult<Prediction> =
        match classifier.Model with
        | VQCModel (result, featureMap, varForm, numQubits) ->
            let backend = LocalBackend() :> IQuantumBackend
            match VQC.predict backend featureMap varForm result.Parameters sample 1000 with
            | Error e -> Error e
            | Ok vqcPred ->
                Ok {
                    Label = vqcPred.Label
                    Confidence = vqcPred.Probability
                    IsPositive = vqcPred.Label = 1
                    IsNegative = vqcPred.Label = 0
                }
        
        | SVMModel (model, storedNumQubits) ->
            let backend = LocalBackend() :> IQuantumBackend
            let featureMap = FeatureMapType.ZZFeatureMap 2
            match QuantumKernelSVM.predict backend model sample 1000 with
            | Error e -> Error e
            | Ok prediction ->
                // Convert decision value to confidence (sigmoid-like transformation)
                let confidence = 1.0 / (1.0 + exp(-abs(prediction.DecisionValue)))
                Ok {
                    Label = prediction.Label
                    Confidence = confidence
                    IsPositive = prediction.Label = 1
                    IsNegative = prediction.Label = 0
                }
        
        | ClassicalModel _ ->
            Error (QuantumError.Other "Classical model prediction not implemented")
    
    /// Evaluate classifier on test set
    let evaluate 
        (testFeatures: float array array) 
        (testLabels: int array) 
        (classifier: Classifier) 
        : QuantumResult<EvaluationMetrics> =
        
        if testFeatures.Length <> testLabels.Length then
            Error (QuantumError.ValidationError ("Input", "Test features and labels must have same length"))
        else
            // Make predictions
            let predictions = 
                testFeatures 
                |> Array.map (fun x -> 
                    match predict x classifier with
                    | Ok pred -> pred.Label
                    | Error _ -> 0)
            
            // Compute confusion matrix
            let tp = Array.zip predictions testLabels |> Array.filter (fun (p, l) -> p = 1 && l = 1) |> Array.length
            let tn = Array.zip predictions testLabels |> Array.filter (fun (p, l) -> p = 0 && l = 0) |> Array.length
            let fp = Array.zip predictions testLabels |> Array.filter (fun (p, l) -> p = 1 && l = 0) |> Array.length
            let fn = Array.zip predictions testLabels |> Array.filter (fun (p, l) -> p = 0 && l = 1) |> Array.length
            
            // Compute metrics
            let accuracy = float (tp + tn) / float testLabels.Length
            let precision = if (tp + fp) = 0 then 0.0 else float tp / float (tp + fp)
            let recall = if (tp + fn) = 0 then 0.0 else float tp / float (tp + fn)
            let f1 = if (precision + recall) = 0.0 then 0.0 else 2.0 * precision * recall / (precision + recall)
            
            Ok {
                Accuracy = accuracy
                Precision = precision
                Recall = recall
                F1Score = f1
                TruePositives = tp
                TrueNegatives = tn
                FalsePositives = fp
                FalseNegatives = fn
            }
    
    // ========================================================================
    // PERSISTENCE
    // ========================================================================
    
    /// Save classifier to file
    let save (path: string) (classifier: Classifier) : QuantumResult<unit> =
        match classifier.Model with
        | VQCModel (result, featureMap, varForm, numQubits) ->
            let fmType = match featureMap with ZZFeatureMap _ -> "ZZFeatureMap" | _ -> "Unknown"
            let fmDepth = match featureMap with ZZFeatureMap d -> d | _ -> 0
            let vfType = match varForm with RealAmplitudes _ -> "RealAmplitudes" | _ -> "Unknown"
            let vfDepth = match varForm with RealAmplitudes d -> d | _ -> 0
            
            ModelSerialization.saveVQCTrainingResult path result numQubits fmType fmDepth vfType vfDepth classifier.Metadata.Note
        
        | SVMModel (svmModel, numQubits) ->
            ModelSerialization.saveSVMModel path svmModel numQubits classifier.Metadata.Note
        
        | ClassicalModel _ ->
            Error (QuantumError.Other "Classical model persistence not yet implemented")
    
    /// Load classifier from file
    let load (path: string) : QuantumResult<Classifier> =
        // Detect model type by checking JSON structure
        try
            let json = System.IO.File.ReadAllText(path)
            
            // Check if it's an SVM model (has SupportVectorIndices field)
            if json.Contains("\"SupportVectorIndices\"") then
                // Load as SVM
                match ModelSerialization.loadSVMModel path with
                | Error e -> Error (QuantumError.ValidationError ("Input", $"Failed to load SVM model: {e}"))
                | Ok serialized ->
                    match ModelSerialization.reconstructSVMModel serialized with
                    | Error e -> Error e
                    | Ok svmModel ->
                        Ok {
                            Model = SVMModel (svmModel, serialized.NumQubits)
                            Metadata = {
                                Architecture = Hybrid
                                TrainingAccuracy = 0.0
                                TrainingTime = TimeSpan.Zero
                                NumFeatures = serialized.NumQubits
                                NumSamples = serialized.TrainData.Length
                                CreatedAt = DateTime.UtcNow
                                Note = serialized.Note
                            }
                        }
            else
                // Load as VQC model
                match ModelSerialization.loadForTransferLearning path with
                | Error e -> Error e
                | Ok (parameters, (numQubits, fmType, fmDepth, vfType, vfDepth)) ->
                    // Reconstruct VQC model
                    let featureMap = 
                        match fmType with
                        | "ZZFeatureMap" -> FeatureMapType.ZZFeatureMap fmDepth
                        | _ -> FeatureMapType.ZZFeatureMap 2
                    
                    let varForm =
                        match vfType with
                        | "RealAmplitudes" -> VariationalForm.RealAmplitudes vfDepth
                        | _ -> VariationalForm.RealAmplitudes 2
                    
                    let result : VQC.TrainingResult = {
                        Parameters = parameters
                        LossHistory = []
                        Epochs = 0
                        TrainAccuracy = 0.0
                        Converged = true
                    }
                    
                    Ok {
                        Model = VQCModel (result, featureMap, varForm, numQubits)
                        Metadata = {
                            Architecture = Quantum
                            TrainingAccuracy = 0.0
                            TrainingTime = TimeSpan.Zero
                            NumFeatures = numQubits
                            NumSamples = 0
                            CreatedAt = DateTime.UtcNow
                            Note = None
                        }
                    }
        with ex ->
            Error (QuantumError.ValidationError ("Input", $"Failed to load model: {ex.Message}"))
    
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
            }
        
        member _.Delay(f: unit -> ClassificationProblem) = f
        
        member _.Run(f: unit -> ClassificationProblem) : QuantumResult<Classifier> =
            let problem = f()
            train problem
        
        member _.Combine(p1: ClassificationProblem, p2: ClassificationProblem) =
            { p2 with 
                TrainFeatures = if p2.TrainFeatures.Length = 0 then p1.TrainFeatures else p2.TrainFeatures
                TrainLabels = if p2.TrainLabels.Length = 0 then p1.TrainLabels else p2.TrainLabels
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
            }
        
        /// <summary>Set the training data with features and binary labels.</summary>
        /// <param name="features">Training feature vectors</param>
        /// <param name="labels">Binary labels (0 or 1) for each sample</param>
        [<CustomOperation("trainWith")>]
        member _.TrainWith(problem: ClassificationProblem, features: float array array, labels: int array) =
            { problem with TrainFeatures = features; TrainLabels = labels }
        
        /// <summary>Set the neural network architecture.</summary>
        /// <param name="arch">Architecture specification</param>
        [<CustomOperation("architecture")>]
        member _.Architecture(problem: ClassificationProblem, arch: Architecture) =
            { problem with Architecture = arch }
        
        /// <summary>Set the learning rate for optimization.</summary>
        /// <param name="lr">Learning rate (typically 0.001 to 0.1)</param>
        [<CustomOperation("learningRate")>]
        member _.LearningRate(problem: ClassificationProblem, lr: float) =
            { problem with LearningRate = lr }
        
        /// <summary>Set the maximum number of training epochs.</summary>
        /// <param name="epochs">Maximum epochs</param>
        [<CustomOperation("maxEpochs")>]
        member _.MaxEpochs(problem: ClassificationProblem, epochs: int) =
            { problem with MaxEpochs = epochs }
        
        /// <summary>Set the convergence threshold for early stopping.</summary>
        /// <param name="threshold">Convergence threshold for loss improvement</param>
        [<CustomOperation("convergenceThreshold")>]
        member _.ConvergenceThreshold(problem: ClassificationProblem, threshold: float) =
            { problem with ConvergenceThreshold = threshold }
        
        /// <summary>Set the quantum backend for execution.</summary>
        /// <param name="backend">Quantum backend instance</param>
        [<CustomOperation("backend")>]
        member _.Backend(problem: ClassificationProblem, backend: IQuantumBackend) =
            { problem with Backend = Some backend }
        
        /// <summary>Set the number of measurement shots.</summary>
        /// <param name="shots">Number of circuit measurements</param>
        [<CustomOperation("shots")>]
        member _.Shots(problem: ClassificationProblem, shots: int) =
            { problem with Shots = shots }
        
        /// <summary>Enable or disable verbose output.</summary>
        /// <param name="verbose">True to enable detailed logging</param>
        [<CustomOperation("verbose")>]
        member _.Verbose(problem: ClassificationProblem, verbose: bool) =
            { problem with Verbose = verbose }
        
        /// <summary>Set the path to save the trained model.</summary>
        /// <param name="path">File path for saving the model</param>
        [<CustomOperation("saveModelTo")>]
        member _.SaveModelTo(problem: ClassificationProblem, path: string) =
            { problem with SavePath = Some path }
        
        /// <summary>Add a note or description to the classification problem.</summary>
        /// <param name="note">Descriptive note</param>
        [<CustomOperation("note")>]
        member _.Note(problem: ClassificationProblem, note: string) =
            { problem with Note = Some note }
        
        /// <summary>Set a progress reporter for real-time training updates.</summary>
        /// <param name="reporter">Progress reporter instance</param>
        [<CustomOperation("progressReporter")>]
        member _.ProgressReporter(problem: ClassificationProblem, reporter: Core.Progress.IProgressReporter) =
            { problem with ProgressReporter = Some reporter }
        
        /// <summary>Set a cancellation token for early termination of training.</summary>
        /// <param name="token">Cancellation token</param>
        [<CustomOperation("cancellationToken")>]
        member _.CancellationToken(problem: ClassificationProblem, token: System.Threading.CancellationToken) =
            { problem with CancellationToken = Some token }
    
    /// Create binary classification computation expression
    let binaryClassification = BinaryClassificationBuilder()
