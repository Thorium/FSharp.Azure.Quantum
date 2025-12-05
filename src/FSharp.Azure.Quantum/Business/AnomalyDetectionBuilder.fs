namespace FSharp.Azure.Quantum.Business

open System
open System.IO
open System.Text.Json
open FSharp.Azure.Quantum.Core.BackendAbstraction
open FSharp.Azure.Quantum.MachineLearning

/// High-Level Anomaly Detection Builder - Business-First API
/// 
/// DESIGN PHILOSOPHY:
/// This is a BUSINESS DOMAIN API for detecting unusual patterns or outliers
/// without understanding one-class classification or quantum kernels.
/// 
/// WHAT IS ANOMALY DETECTION:
/// Find items that don't fit normal patterns. Unlike classification, you only
/// need examples of "normal" behavior - the system learns what's unusual.
/// 
/// USE CASES:
/// - Fraud detection: Spot unusual transaction patterns
/// - Security: Detect intrusions, unauthorized access
/// - Quality control: Find defective products
/// - System monitoring: Detect performance issues
/// - Network security: Identify suspicious traffic
/// - Manufacturing: Detect equipment failures
/// 
/// EXAMPLE USAGE:
///   // Simple: Train on normal data only
///   let detector = anomalyDetection {
///       trainOnNormalData historicalTransactions
///       sensitivity Medium
///   }
///   
///   // Check new items
///   let result = detector |> AnomalyDetector.check suspiciousTransaction
///   if result.IsAnomaly && result.Score > 0.8 then
///       blockImmediately()
///   
///   // Advanced: Full configuration
///   let detector = anomalyDetection {
///       trainOnNormalData normalData
///       
///       // Detection parameters
///       sensitivity High  // Low, Medium, High, VeryHigh
///       contaminationRate 0.05  // Expected % of anomalies in training
///       
///       // Infrastructure
///       backend azureBackend
///       
///       // Persistence
///       saveModelTo "anomaly_detector.model"
///   }
module AnomalyDetector =
    
    // ========================================================================
    // CORE TYPES - Anomaly Detection Domain Model
    // ========================================================================
    
    /// Sensitivity level for anomaly detection
    type Sensitivity =
        | Low        // Fewer false alarms, may miss some anomalies
        | Medium     // Balanced (default)
        | High       // More sensitive, more false alarms
        | VeryHigh   // Maximum sensitivity
    
    /// Anomaly detection problem specification
    type DetectionProblem = {
        /// Training data (normal examples only)
        NormalData: float array array
        
        /// Sensitivity level
        Sensitivity: Sensitivity
        
        /// Expected contamination rate in training data (0.0 to 0.5)
        ContaminationRate: float
        
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
    
    /// Trained anomaly detector
    type Detector = {
        /// Underlying model (one-class SVM)
        Model: QuantumKernelSVM.SVMModel
        
        /// Detection metadata
        Metadata: DetectorMetadata
        
        /// Quantum feature map
        FeatureMap: FeatureMapType
        
        /// Number of qubits
        NumQubits: int
        
        /// Decision threshold
        Threshold: float
    }
    
    and DetectorMetadata = {
        Sensitivity: Sensitivity
        TrainingTime: TimeSpan
        NumFeatures: int
        NumNormalSamples: int
        CreatedAt: DateTime
        Note: string option
    }
    
    /// Anomaly detection result
    type AnomalyResult = {
        /// Is this sample anomalous?
        IsAnomaly: bool
        
        /// Is this sample normal?
        IsNormal: bool
        
        /// Anomaly score [0, 1] - higher = more anomalous
        AnomalyScore: float
        
        /// Confidence in the detection [0, 1]
        Confidence: float
    }
    
    /// Batch detection results
    type BatchResults = {
        /// Total items checked
        TotalItems: int
        
        /// Number of anomalies detected
        AnomaliesDetected: int
        
        /// Percentage of anomalies
        AnomalyRate: float
        
        /// Individual results
        Results: AnomalyResult array
        
        /// Top N most anomalous items (indices)
        TopAnomalies: (int * float) array
    }
    
    // ========================================================================
    // VALIDATION
    // ========================================================================
    
    /// Validate anomaly detection problem
    let private validate (problem: DetectionProblem) : Result<unit, string> =
        if problem.NormalData.Length = 0 then
            Error "Normal data cannot be empty"
        elif problem.ContaminationRate < 0.0 || problem.ContaminationRate > 0.5 then
            Error "Contamination rate must be between 0.0 and 0.5"
        elif problem.Shots < 1 then
            Error "Shots must be at least 1"
        elif problem.NormalData.Length < 10 then
            Error "Need at least 10 normal samples for reliable detection"
        else
            let numFeatures = problem.NormalData.[0].Length
            let allSameLength = problem.NormalData |> Array.forall (fun x -> x.Length = numFeatures)
            if not allSameLength then
                Error "All feature vectors must have the same length"
            else
                Ok ()
    
    // ========================================================================
    // SENSITIVITY MAPPING
    // ========================================================================
    
    /// Map sensitivity level to nu parameter for one-class SVM
    let private sensitivityToNu (sensitivity: Sensitivity) (contaminationRate: float) : float =
        let baseNu = 
            match sensitivity with
            | Low -> 0.01      // Very conservative
            | Medium -> 0.05   // Balanced
            | High -> 0.1      // More sensitive
            | VeryHigh -> 0.2  // Maximum sensitivity
        
        // Adjust for expected contamination
        min 0.5 (baseNu + contaminationRate)
    
    /// Map sensitivity to decision threshold adjustment
    let private sensitivityToThreshold (sensitivity: Sensitivity) : float =
        match sensitivity with
        | Low -> 0.5        // Higher threshold = fewer alarms
        | Medium -> 0.3
        | High -> 0.1
        | VeryHigh -> 0.0   // Lower threshold = more alarms
    
    // ========================================================================
    // MODEL PERSISTENCE
    // ========================================================================
    
    /// Serializable detector format (for JSON)
    [<CLIMutable>]
    type private SerializableDetector = {
        /// SVM model data
        SVMModel: ModelSerialization.SerializableSVMModel
        
        /// Detector-specific threshold
        Threshold: float
        
        /// Sensitivity level
        Sensitivity: string
        
        /// Training time in milliseconds
        TrainingTime: float
        
        /// Created timestamp
        CreatedAt: string
        
        /// Optional note
        Note: string option
    }
    
    /// Save detector to file
    let saveDetector (detector: Detector) (path: string) : Result<unit, string> =
        try
            // First save the SVM model to get serializable format
            match ModelSerialization.saveSVMModel path detector.Model detector.NumQubits detector.Metadata.Note with
            | Error e -> Error e
            | Ok () ->
                // Load it back to get SerializableSVMModel
                match ModelSerialization.loadSVMModel path with
                | Error e -> Error e
                | Ok svmSerialized ->
                    // Wrap with detector-specific metadata
                    let detectorData = {
                        SVMModel = svmSerialized
                        Threshold = detector.Threshold
                        Sensitivity = 
                            match detector.Metadata.Sensitivity with
                            | Low -> "Low"
                            | Medium -> "Medium"
                            | High -> "High"
                            | VeryHigh -> "VeryHigh"
                        TrainingTime = detector.Metadata.TrainingTime.TotalMilliseconds
                        CreatedAt = detector.Metadata.CreatedAt.ToString("o")
                        Note = detector.Metadata.Note
                    }
                    
                    let options = JsonSerializerOptions(WriteIndented = true)
                    let json = JsonSerializer.Serialize(detectorData, options)
                    File.WriteAllText(path, json)
                    Ok ()
        with ex ->
            Error $"Failed to save detector: {ex.Message}"
    
    /// Load detector from file
    let loadDetector (path: string) : Result<Detector, string> =
        try
            if not (File.Exists path) then
                Error $"File not found: {path}"
            else
                let json = File.ReadAllText(path)
                let detectorData = JsonSerializer.Deserialize<SerializableDetector>(json)
                
                // Reconstruct SVM model
                match ModelSerialization.reconstructSVMModel detectorData.SVMModel with
                | Error e -> Error e
                | Ok svmModel ->
                    let sensitivity =
                        match detectorData.Sensitivity with
                        | "Low" -> Low
                        | "Medium" -> Medium
                        | "High" -> High
                        | "VeryHigh" -> VeryHigh
                        | _ -> Medium
                    
                    Ok {
                        Model = svmModel
                        Metadata = {
                            Sensitivity = sensitivity
                            TrainingTime = TimeSpan.FromMilliseconds(detectorData.TrainingTime)
                            NumFeatures = detectorData.SVMModel.NumQubits
                            NumNormalSamples = detectorData.SVMModel.TrainData.Length
                            CreatedAt = DateTime.Parse(detectorData.CreatedAt)
                            Note = detectorData.Note
                        }
                        FeatureMap = svmModel.FeatureMap
                        NumQubits = detectorData.SVMModel.NumQubits
                        Threshold = detectorData.Threshold
                    }
        with ex ->
            Error $"Failed to load detector: {ex.Message}"
    
    // ========================================================================
    // TRAINING
    // ========================================================================
    
    /// Train anomaly detector using one-class quantum kernel SVM
    let train (problem: DetectionProblem) : Result<Detector, string> =
        match validate problem with
        | Error e -> Error e
        | Ok () ->
            
            let startTime = DateTime.UtcNow
            let numFeatures = problem.NormalData.[0].Length
            
            let backend = 
                match problem.Backend with
                | Some b -> b
                | None -> LocalBackend() :> IQuantumBackend
            
            // Smart defaults for quantum architecture
            let numQubits = min numFeatures 8
            let featureMap = FeatureMapType.ZZFeatureMap 2
            
            // One-class SVM configuration
            let nu = sensitivityToNu problem.Sensitivity problem.ContaminationRate
            let threshold = sensitivityToThreshold problem.Sensitivity
            
            // For one-class classification, all training labels are +1
            // (we're learning the boundary of normal behavior)
            let labels = Array.create problem.NormalData.Length 1
            
            let svmConfig : QuantumKernelSVM.SVMConfig = {
                C = 1.0 / (float problem.NormalData.Length * nu)
                Tolerance = 0.001
                MaxIterations = 1000
                Verbose = problem.Verbose
            }
            
            if problem.Verbose then
                printfn "Training anomaly detector..."
                printfn "  Normal samples: %d" problem.NormalData.Length
                printfn "  Features: %d" numFeatures
                printfn "  Sensitivity: %A (nu=%.3f)" problem.Sensitivity nu
            
            match QuantumKernelSVM.train backend featureMap problem.NormalData labels svmConfig problem.Shots with
            | Error e -> Error $"Training failed: {e}"
            | Ok model ->
                
                let endTime = DateTime.UtcNow
                
                let detector = {
                    Model = model
                    Metadata = {
                        Sensitivity = problem.Sensitivity
                        TrainingTime = endTime - startTime
                        NumFeatures = numFeatures
                        NumNormalSamples = problem.NormalData.Length
                        CreatedAt = startTime
                        Note = problem.Note
                    }
                    FeatureMap = featureMap
                    NumQubits = numQubits
                    Threshold = threshold
                }
                
                if problem.Verbose then
                    printfn "✅ Training complete in %A" (endTime - startTime)
                
                // Save if requested
                match problem.SavePath with
                | None -> 
                    Ok detector
                | Some path ->
                    match saveDetector detector path with
                    | Ok () ->
                        if problem.Verbose then
                            printfn "✅ Detector saved to: %s" path
                        Ok detector
                    | Error msg ->
                        if problem.Verbose then
                            printfn "⚠️  Warning: Failed to save detector: %s" msg
                        // Don't fail the entire training just because save failed
                        Ok detector
    
    // ========================================================================
    // DETECTION
    // ========================================================================
    
    /// Compute anomaly score for a sample
    let private computeAnomalyScore 
        (backend: IQuantumBackend)
        (detector: Detector)
        (sample: float array)
        (shots: int)
        : Result<float, string> =
        
        // Use SVM decision value as anomaly score
        // Positive = normal, Negative = anomaly
        match QuantumKernelSVM.predict backend detector.Model sample shots with
        | Error e -> Error e
        | Ok prediction ->
            
            // For one-class SVM:
            // prediction.Label = 1 means "normal" (inside boundary)
            // prediction.Label = -1 or 0 means "anomaly" (outside boundary)
            
            // Map to [0, 1] score where higher = more anomalous
            // Use decision value distance from hyperplane
            let score = 
                if prediction.Label = 1 then
                    // Normal - low anomaly score
                    1.0 / (1.0 + exp(abs(prediction.DecisionValue)))
                else
                    // Anomaly - high anomaly score
                    1.0 / (1.0 + exp(-abs(prediction.DecisionValue)))
            
            Ok score
    
    /// Check if sample is anomalous
    let check (sample: float array) (detector: Detector) : Result<AnomalyResult, string> =
        let backend = LocalBackend() :> IQuantumBackend
        
        match computeAnomalyScore backend detector sample 1000 with
        | Error e -> Error e
        | Ok score ->
            
            let isAnomaly = score > detector.Threshold
            
            Ok {
                IsAnomaly = isAnomaly
                IsNormal = not isAnomaly
                AnomalyScore = score
                Confidence = abs (score - detector.Threshold) / (1.0 - detector.Threshold)
            }
    
    /// Check multiple samples
    let checkBatch 
        (samples: float array array) 
        (detector: Detector) 
        : Result<BatchResults, string> =
        
        let results = samples |> Array.map (fun s -> check s detector)
        
        // Check for errors
        let firstError = results |> Array.tryPick (fun r -> match r with Error e -> Some e | _ -> None)
        
        match firstError with
        | Some e -> Error e
        | None ->
            
            let anomalyResults = 
                results 
                |> Array.map (fun r -> match r with Ok ar -> ar | Error _ -> failwith "Unreachable")
            
            let anomalyCount = anomalyResults |> Array.filter (fun r -> r.IsAnomaly) |> Array.length
            let anomalyRate = float anomalyCount / float samples.Length
            
            // Get top anomalies by score
            let topAnomalies =
                anomalyResults
                |> Array.mapi (fun i r -> (i, r.AnomalyScore))
                |> Array.sortByDescending snd
                |> Array.take (min 10 samples.Length)
            
            Ok {
                TotalItems = samples.Length
                AnomaliesDetected = anomalyCount
                AnomalyRate = anomalyRate
                Results = anomalyResults
                TopAnomalies = topAnomalies
            }
    
    // ========================================================================
    // EXPLANATION
    // ========================================================================
    
    /// Explain why sample is anomalous (feature contribution)
    let explain 
        (sample: float array) 
        (detector: Detector) 
        (trainingData: float array array)
        : Result<(string * float) array, string> =
        
        // Compute distance from normal examples in each feature
        let featureContributions =
            [| 0 .. sample.Length - 1 |]
            |> Array.map (fun i ->
                let featureValue = sample.[i]
                let normalValues = trainingData |> Array.map (fun x -> x.[i])
                let mean = Array.average normalValues
                let stddev = 
                    let variance = normalValues |> Array.averageBy (fun x -> (x - mean) ** 2.0)
                    sqrt variance
                
                let deviation = abs (featureValue - mean) / (stddev + 1e-6)
                (sprintf "Feature_%d" (i+1), deviation)
            )
            |> Array.sortByDescending snd
        
        Ok featureContributions
    
    // ========================================================================
    // PERSISTENCE
    // ========================================================================
    
    /// Save detector to file
    let save (path: string) (detector: Detector) : Result<unit, string> =
        Error "Detector persistence not yet implemented"
    
    /// Load detector from file
    let load (path: string) : Result<Detector, string> =
        Error "Detector loading not yet implemented"
    
    // ========================================================================
    // COMPUTATION EXPRESSION BUILDER
    // ========================================================================
    
    /// Computation expression builder for anomaly detection
    type AnomalyDetectionBuilder() =
        
        member _.Yield(_) : DetectionProblem =
            {
                NormalData = [||]
                Sensitivity = Medium
                ContaminationRate = 0.05
                Backend = None
                Shots = 1000
                Verbose = false
                SavePath = None
                Note = None
                ProgressReporter = None
                CancellationToken = None
            }
        
        member _.Delay(f: unit -> DetectionProblem) = f
        
        member _.Run(f: unit -> DetectionProblem) : Result<Detector, string> =
            let problem = f()
            train problem
        
        member _.Combine(p1: DetectionProblem, p2: DetectionProblem) =
            { p2 with 
                NormalData = if p2.NormalData.Length = 0 then p1.NormalData else p2.NormalData
            }
        
        member _.Zero() : DetectionProblem =
            {
                NormalData = [||]
                Sensitivity = Medium
                ContaminationRate = 0.05
                Backend = None
                Shots = 1000
                Verbose = false
                SavePath = None
                Note = None
                ProgressReporter = None
                CancellationToken = None
            }
        
        /// <summary>Set the training data containing normal (non-anomalous) samples.</summary>
        /// <param name="data">Array of normal data samples as feature vectors</param>
        [<CustomOperation("trainOnNormalData")>]
        member _.TrainOnNormalData(problem: DetectionProblem, data: float array array) =
            { problem with NormalData = data }
        
        /// <summary>Set the sensitivity level for anomaly detection.</summary>
        /// <param name="sensitivity">Sensitivity level (Low, Medium, or High)</param>
        [<CustomOperation("sensitivity")>]
        member _.Sensitivity(problem: DetectionProblem, sensitivity: Sensitivity) =
            { problem with Sensitivity = sensitivity }
        
        /// <summary>Set the expected contamination rate in the training data.</summary>
        /// <param name="rate">Contamination rate (0.0 to 1.0) indicating fraction of anomalies expected</param>
        [<CustomOperation("contaminationRate")>]
        member _.ContaminationRate(problem: DetectionProblem, rate: float) =
            { problem with ContaminationRate = rate }
        
        /// <summary>Set the quantum backend for execution.</summary>
        /// <param name="backend">Quantum backend instance</param>
        [<CustomOperation("backend")>]
        member _.Backend(problem: DetectionProblem, backend: IQuantumBackend) =
            { problem with Backend = Some backend }
        
        /// <summary>Set the number of measurement shots.</summary>
        /// <param name="shots">Number of circuit measurements</param>
        [<CustomOperation("shots")>]
        member _.Shots(problem: DetectionProblem, shots: int) =
            { problem with Shots = shots }
        
        /// <summary>Enable or disable verbose output.</summary>
        /// <param name="verbose">True to enable detailed logging</param>
        [<CustomOperation("verbose")>]
        member _.Verbose(problem: DetectionProblem, verbose: bool) =
            { problem with Verbose = verbose }
        
        /// <summary>Set the path to save the trained model.</summary>
        /// <param name="path">File path for saving the model</param>
        [<CustomOperation("saveModelTo")>]
        member _.SaveModelTo(problem: DetectionProblem, path: string) =
            { problem with SavePath = Some path }
        
        /// <summary>Add a note or description to the detection problem.</summary>
        /// <param name="note">Descriptive note</param>
        [<CustomOperation("note")>]
        member _.Note(problem: DetectionProblem, note: string) =
            { problem with Note = Some note }
        
        /// <summary>Set a progress reporter for real-time training updates.</summary>
        /// <param name="reporter">Progress reporter instance</param>
        [<CustomOperation("progressReporter")>]
        member _.ProgressReporter(problem: DetectionProblem, reporter: Core.Progress.IProgressReporter) =
            { problem with ProgressReporter = Some reporter }
        
        /// <summary>Set a cancellation token for early termination of training.</summary>
        /// <param name="token">Cancellation token</param>
        [<CustomOperation("cancellationToken")>]
        member _.CancellationToken(problem: DetectionProblem, token: System.Threading.CancellationToken) =
            { problem with CancellationToken = Some token }
    
    /// Create anomaly detection computation expression
    let anomalyDetection = AnomalyDetectionBuilder()
