namespace FSharp.Azure.Quantum.Business

open FSharp.Azure.Quantum.Backends
open FSharp.Azure.Quantum.Core
open System
open System.IO
open System.Text.Json
open FSharp.Azure.Quantum.Backends
open FSharp.Azure.Quantum.Core.BackendAbstraction
open FSharp.Azure.Quantum.Backends
open FSharp.Azure.Quantum.MachineLearning
open FSharp.Azure.Quantum.Backends
open FSharp.Azure.Quantum
open Microsoft.Extensions.Logging

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
        /// Fewer false alarms, may miss some anomalies
        | Low
        /// Balanced (default)
        | Medium
        /// More sensitive, more false alarms
        | High
        /// Maximum sensitivity
        | VeryHigh
    
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
        
        /// Optional structured logger. When provided, verbose output is sent to this
        /// ILogger instead of being discarded.
        Logger: ILogger option
        
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
        /// Underlying model container: holds the normal training data and quantum
        /// feature map used for kernel evaluation at inference time. Scoring uses
        /// kernel centroid distance rather than the SVM decision function.
        Model: QuantumKernelSVM.SVMModel

        /// Detection metadata
        Metadata: DetectorMetadata
        
        /// Quantum feature map
        FeatureMap: FeatureMapType

        /// Number of qubits
        NumQubits: int

        /// Decision threshold
        Threshold: float

        /// Backend the detector was trained on, reused for inference so predictions
        /// run on the same quantum kernel. None for detectors loaded from disk (a
        /// backend cannot be serialised); inference then falls back to a local simulator.
        Backend: IQuantumBackend option

        /// Measurement shots used for kernel evaluation at inference time.
        Shots: int

        /// Mean of the training kernel matrix, (1/n²)·ΣᵢⱼK(xᵢ,xⱼ) — the squared norm
        /// of the training-set centroid in feature space (centroid-distance scoring).
        KernelMean: float

        /// Training-set centroid-distance quantile that maps to anomaly score 0.5
        /// (the learned boundary of normal behaviour).
        ReferenceDistance: float

        /// Spread (standard deviation) of training centroid distances, used to
        /// normalize distances to a [0, 1] anomaly score.
        DistanceScale: float
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
    let private validate (problem: DetectionProblem) : QuantumResult<unit> =
        if problem.NormalData.Length = 0 then
            Error (QuantumError.ValidationError ("Input", "Normal data cannot be empty"))
        elif problem.ContaminationRate < 0.0 || problem.ContaminationRate > 0.5 then
            Error (QuantumError.ValidationError ("Input", "Contamination rate must be between 0.0 and 0.5"))
        elif problem.Shots < 1 then
            Error (QuantumError.ValidationError ("Input", "Shots must be at least 1"))
        elif problem.NormalData.Length < 10 then
            Error (QuantumError.Other "Need at least 10 normal samples for reliable detection")
        else
            let numFeatures = problem.NormalData.[0].Length
            let allSameLength = problem.NormalData |> Array.forall (fun x -> x.Length = numFeatures)
            if not allSameLength then
                Error (QuantumError.ValidationError ("Input", "All feature vectors must have the same length"))
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
    
    /// Decision threshold on the anomaly score.
    ///
    /// The centroid-distance score is sigmoid-normalized so that the learned
    /// boundary of normal behaviour (the (1 - nu) quantile of training
    /// distances) sits at exactly 0.5. Sensitivity already controls that
    /// boundary through nu (sensitivityToNu), so the threshold is the fixed
    /// boundary score — using sub-0.5 thresholds here would double-count
    /// sensitivity and flag samples the model itself scores as normal.
    let private sensitivityToThreshold (_sensitivity: Sensitivity) : float =
        0.5
    
    // ========================================================================
    // MODEL PERSISTENCE
    // ========================================================================
    
    /// Serializable detector format (for JSON)
    [<CLIMutable>]
    type private SerializableDetector = {
        /// SVM model data
        SVMModel: SVMModelSerialization.SerializableSVMModel
        
        /// Detector-specific threshold
        Threshold: float

        /// Mean of the training kernel matrix (centroid-distance scoring)
        KernelMean: float

        /// Training-distance quantile mapping to anomaly score 0.5
        ReferenceDistance: float

        /// Spread of training distances (score normalization scale)
        DistanceScale: float

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
    let saveDetector (detector: Detector) (path: string) : QuantumResult<unit> =
        try
            // Embed the SVM model (with its lossless feature-map representation) directly in the
            // detector envelope — one write, one canonical SVM schema (SVMModelSerialization).
            let detectorData = {
                SVMModel = SVMModelSerialization.toSerializable detector.Metadata.Note detector.Model
                Threshold = detector.Threshold
                KernelMean = detector.KernelMean
                ReferenceDistance = detector.ReferenceDistance
                DistanceScale = detector.DistanceScale
                Sensitivity =
                    match detector.Metadata.Sensitivity with
                    | Low -> "Low"
                    | Medium -> "Medium"
                    | High -> "High"
                    | VeryHigh -> "VeryHigh"
                TrainingTime = detector.Metadata.TrainingTime.TotalMilliseconds
                CreatedAt = detector.Metadata.CreatedAt.ToString "o"
                Note = detector.Metadata.Note
            }

            let options = JsonSerializerOptions(WriteIndented = true)
            let json = JsonSerializer.Serialize(detectorData, options)
            File.WriteAllText(path, json)
            Ok ()
        with ex ->
            Error (QuantumError.ValidationError ("Input", $"Failed to save detector: {ex.Message}"))
    
    /// Load detector from file
    let loadDetector (path: string) : QuantumResult<Detector> =
        try
            if not (File.Exists path) then
                Error (QuantumError.ValidationError ("Input", $"File not found: {path}"))
            else
                let json = File.ReadAllText(path)
                let detectorData = JsonSerializer.Deserialize<SerializableDetector>(json)

                // Files saved before the centroid-distance scoring lack
                // KernelMean/ReferenceDistance/DistanceScale; System.Text.Json fills
                // them with 0.0, which would make every score degenerate. A valid
                // save always has DistanceScale >= 1e-6, so reject instead.
                if detectorData.DistanceScale <= 0.0 then
                    Error (QuantumError.ValidationError ("Input",
                        $"Detector file '{path}' was saved by an older version and lacks the scoring statistics (KernelMean/ReferenceDistance/DistanceScale). Retrain the detector and save it again."))
                else

                // Reconstruct SVM model
                SVMModelSerialization.fromSerializable detectorData.SVMModel
                |> Result.map (fun svmModel ->
                    let sensitivity =
                        match detectorData.Sensitivity with
                        | "Low" -> Low
                        | "Medium" -> Medium
                        | "High" -> High
                        | "VeryHigh" -> VeryHigh
                        | _ -> Medium

                    // Qubit/feature count is the training-data dimension (one qubit per feature).
                    let numFeatures = if svmModel.TrainData.Length > 0 then svmModel.TrainData.[0].Length else 0

                    {
                        Model = svmModel
                        Metadata = {
                            Sensitivity = sensitivity
                            TrainingTime = TimeSpan.FromMilliseconds(detectorData.TrainingTime)
                            NumFeatures = numFeatures
                            NumNormalSamples = svmModel.TrainData.Length
                            CreatedAt = DateTime.Parse(detectorData.CreatedAt)
                            Note = detectorData.Note
                        }
                        FeatureMap = svmModel.FeatureMap
                        NumQubits = numFeatures
                        Threshold = detectorData.Threshold
                        // Backend is not serialisable; inference uses a local simulator.
                        Backend = None
                        Shots = 1000
                        KernelMean = detectorData.KernelMean
                        ReferenceDistance = detectorData.ReferenceDistance
                        DistanceScale = detectorData.DistanceScale
                    })
        with ex ->
            Error (QuantumError.ValidationError ("Input", $"Failed to load detector: {ex.Message}"))
    
    // ========================================================================
    // TRAINING
    // ========================================================================
    
    /// Train anomaly detector using one-class quantum kernel SVM
    let train (problem: DetectionProblem) : QuantumResult<Detector> =
        validate problem
        |> Result.bind (fun () ->
            
            let startTime = DateTime.UtcNow
            let numFeatures = problem.NormalData.[0].Length
            
            let backend = 
                match problem.Backend with
                | Some b -> b
                | None -> LocalBackend.LocalBackend() :> IQuantumBackend
            
            
            // Smart defaults for quantum architecture
            let numQubits = min numFeatures 8
            let featureMap = FeatureMapType.ZZFeatureMap 2
            
            // One-class configuration: nu = expected fraction of training points
            // lying outside the learned boundary of normal behaviour
            let nu = sensitivityToNu problem.Sensitivity problem.ContaminationRate
            let threshold = sensitivityToThreshold problem.Sensitivity

            // For one-class detection, all training labels are +1
            // (kept in the model container for serialization compatibility)
            let labels = Array.create problem.NormalData.Length 1

            if problem.Verbose then
                let log = logInfo problem.Logger
                log "Training anomaly detector..."
                log $"  Normal samples: {problem.NormalData.Length}"
                log $"  Features: {numFeatures}"
                log $"  Sensitivity: {problem.Sensitivity} (nu={nu:F3})"

            // One-class scoring via kernel centroid distance:
            //   d²(x) = k(x,x) − (2/n)·Σᵢ k(x,xᵢ) + (1/n²)·Σᵢⱼ K(xᵢ,xⱼ)
            // i.e. the squared distance to the mean of the training points in the
            // quantum feature space. (A binary SVM is degenerate here: with all
            // labels identical the SMO bounds collapse and no alpha can move.)
            QuantumKernels.computeKernelMatrix backend featureMap problem.NormalData problem.Shots
            |> Result.mapError (fun e -> QuantumError.ValidationError ("Input", $"Training failed: {e}"))
            |> Result.map (fun kernelMatrix ->

                let n = problem.NormalData.Length
                let nf = float n

                // Row means (1/n)·Σⱼ K(i,j) and the kernel grand mean (1/n²)·Σᵢⱼ K(i,j)
                let rowMeans =
                    Array.init n (fun i ->
                        (Array.init n (fun j -> kernelMatrix.[i, j]) |> Array.sum) / nf)
                let kernelMean = Array.sum rowMeans / nf

                // Centroid distance of every training point
                let trainDistances =
                    Array.init n (fun i ->
                        sqrt (max 0.0 (kernelMatrix.[i, i] - 2.0 * rowMeans.[i] + kernelMean)))

                // The (1 − nu) quantile of training distances is the boundary of
                // normal behaviour (maps to anomaly score 0.5); the spread of the
                // training distances sets the score normalization scale.
                let sortedDistances = Array.sort trainDistances
                let quantileIdx = min (n - 1) (int (ceil ((1.0 - nu) * float (n - 1))))
                let referenceDistance = sortedDistances.[quantileIdx]
                let distanceScale =
                    let mean = Array.average trainDistances
                    let std = sqrt (trainDistances |> Array.averageBy (fun d -> (d - mean) * (d - mean)))
                    max std 1e-6

                // Model record acts as the serializable container for the training
                // data and feature map consumed at inference time.
                let model : QuantumKernelSVM.SVMModel = {
                    SupportVectorIndices = Array.init n id
                    Alphas = Array.create n (1.0 / nf)
                    Bias = 0.0
                    TrainData = problem.NormalData
                    TrainLabels = labels
                    FeatureMap = featureMap
                }

                let endTime = DateTime.UtcNow

                let detector = {
                    Model = model
                    KernelMean = kernelMean
                    ReferenceDistance = referenceDistance
                    DistanceScale = distanceScale
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
                    Backend = Some backend
                    Shots = problem.Shots
                }

                if problem.Verbose then
                    logInfo problem.Logger $"[OK] Training complete in {endTime - startTime}"
                
                // Save if requested
                match problem.SavePath with
                | None -> 
                    ()
                | Some path ->
                    match saveDetector detector path with
                    | Ok () ->
                        if problem.Verbose then
                            logInfo problem.Logger $"[OK] Detector saved to: {path}"
                    | Error msg ->
                        if problem.Verbose then
                            logWarning problem.Logger $"[WARN] Failed to save detector: {msg.Message}"
                        // Don't fail the entire training just because save failed
                
                detector))
    
    // ========================================================================
    // DETECTION
    // ========================================================================
    
    /// Compute anomaly score for a sample using kernel centroid distance:
    ///   d²(x) = k(x,x) − (2/n)·Σᵢ k(x,xᵢ) + (1/n²)·Σᵢⱼ K(xᵢ,xⱼ)
    /// For fidelity kernels k(x,x) = 1. The distance is normalized to [0, 1]
    /// with a sigmoid centred on the training-set reference distance, so
    /// scores above 0.5 lie outside the learned boundary of normal behaviour
    /// (higher score = more anomalous).
    let private computeAnomalyScore
        (backend: IQuantumBackend)
        (detector: Detector)
        (sample: float array)
        (shots: int)
        : QuantumResult<float> =

        let trainData = detector.Model.TrainData

        // Kernel between the sample and every training point
        let kernelResults =
            trainData
            |> Array.map (fun x ->
                QuantumKernels.computeKernel backend detector.Model.FeatureMap sample x shots)

        match kernelResults |> Array.tryPick (function Error e -> Some e | Ok _ -> None) with
        | Some e -> Error e
        | None ->
            let meanCross =
                (kernelResults |> Array.sumBy (function Ok k -> k | Error _ -> 0.0))
                / float trainData.Length

            // k(x,x) = 1 for fidelity kernels: |⟨φ(x)|φ(x)⟩|² = 1
            let distance = sqrt (max 0.0 (1.0 - 2.0 * meanCross + detector.KernelMean))

            // Sigmoid normalization: reference distance maps to 0.5
            let scale = max detector.DistanceScale 1e-6
            let score = 1.0 / (1.0 + exp (-(distance - detector.ReferenceDistance) / scale))

            Ok score
    
    /// Check if sample is anomalous
    let check (sample: float array) (detector: Detector) : QuantumResult<AnomalyResult> =
        // Reuse the backend the detector was trained on so inference runs on the same
        // quantum kernel; fall back to a local simulator only for disk-loaded detectors.
        let backend =
            match detector.Backend with
            | Some b -> b
            | None -> LocalBackend.LocalBackend() :> IQuantumBackend

        computeAnomalyScore backend detector sample detector.Shots
        |> Result.map (fun score ->
            
            let isAnomaly = score > detector.Threshold
            
            {
                IsAnomaly = isAnomaly
                IsNormal = not isAnomaly
                AnomalyScore = score
                Confidence = abs (score - detector.Threshold) / (1.0 - detector.Threshold)
            })
    
    /// Check multiple samples
    let checkBatch 
        (samples: float array array) 
        (detector: Detector) 
        : QuantumResult<BatchResults> =
        
        let results = samples |> Array.map (fun s -> check s detector)
        
        // Check for errors
        let firstError = results |> Array.tryPick (Result.map (fun _ -> None) >> Result.defaultWith (fun e -> Some e))
        
        match firstError with
        | Some e -> Error e
        | None ->
            
            let anomalyResults = 
                results 
                |> Array.map (fun r -> r |> Result.defaultWith (fun _ -> failwith $"Unreachable, calling checkBatch with samples: {samples}, detector: {detector}"))
            
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
        : QuantumResult<(string * float) array> =
        
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
    let save (path: string) (detector: Detector) : QuantumResult<unit> =
        saveDetector detector path
    
    /// Load detector from file
    let load (path: string) : QuantumResult<Detector> =
        loadDetector path
    
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
                Logger = None
                SavePath = None
                Note = None
                ProgressReporter = None
                CancellationToken = None
            }
        
        member _.Delay(f: unit -> DetectionProblem) = f
        
        member _.Run(f: unit -> DetectionProblem) : QuantumResult<Detector> =
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
                Logger = None
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
