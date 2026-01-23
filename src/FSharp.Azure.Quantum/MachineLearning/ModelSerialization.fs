namespace FSharp.Azure.Quantum.MachineLearning

open FSharp.Azure.Quantum.Core

/// Simple Model Serialization for VQC and Portfolio Models.
///
/// Provides basic save/load functionality for trained VQC models
/// and quantum portfolio optimization solutions.
/// Stores parameters as JSON for easy inspection and portability.

open System
open System.IO
open System.Text.Json

module ModelSerialization =
    
    // ========================================================================
    // TYPES
    // ========================================================================
    
    /// Simple serializable VQC model (JSON-friendly)
    type SerializableVQCModel = {
        /// Model parameters (weights)
        Parameters: float array
        
        /// Final training loss
        FinalLoss: float
        
        /// Number of qubits
        NumQubits: int
        
        /// Feature map type name
        FeatureMapType: string
        
        /// Feature map depth
        FeatureMapDepth: int
        
        /// Variational form type name
        VariationalFormType: string
        
        /// Variational form depth
        VariationalFormDepth: int
        
        /// Optional metadata
        SavedAt: string
        Note: string option
    }
    
    /// Serializable binary classifier (for multi-class OVR)
    type SerializableBinaryClassifier = {
        /// Classifier parameters
        Parameters: float array
        
        /// Training accuracy for this classifier
        TrainAccuracy: float
        
        /// Number of training iterations
        NumIterations: int
    }
    
    /// Serializable multi-class VQC model (one-vs-rest)
    type SerializableMultiClassVQCModel = {
        /// Binary classifiers (one per class)
        Classifiers: SerializableBinaryClassifier array
        
        /// Class labels
        ClassLabels: int array
        
        /// Overall training accuracy
        TrainAccuracy: float
        
        /// Number of classes
        NumClasses: int
        
        /// Number of qubits
        NumQubits: int
        
        /// Feature map type name
        FeatureMapType: string
        
        /// Feature map depth
        FeatureMapDepth: int
        
        /// Variational form type name
        VariationalFormType: string
        
        /// Variational form depth
        VariationalFormDepth: int
        
        /// Optional metadata
        SavedAt: string
        Note: string option
    }
    
    /// Serializable SVM model (JSON-friendly)
    type SerializableSVMModel = {
        /// Support vector indices
        SupportVectorIndices: int array
        
        /// Lagrange multipliers (alphas)
        Alphas: float array
        
        /// Bias term
        Bias: float
        
        /// Training data (support vectors)
        TrainData: float array array
        
        /// Training labels
        TrainLabels: int array
        
        /// Feature map type name
        FeatureMapType: string
        
        /// Feature map depth (if applicable)
        FeatureMapDepth: int
        
        /// Number of qubits
        NumQubits: int
        
        /// Optional metadata
        SavedAt: string
        Note: string option
    }
    
    // ========================================================================
    // VQC SERIALIZATION
    // ========================================================================
    
    /// Save VQC model to JSON file
    ///
    /// Parameters:
    ///   filePath - Path to save JSON file
    ///   parameters - Trained model parameters
    ///   finalLoss - Final training loss
    ///   numQubits - Number of qubits used
    ///   featureMapType - Feature map type name (e.g., "ZZFeatureMap")
    ///   featureMapDepth - Feature map depth
    ///   variationalFormType - Variational form type name (e.g., "RealAmplitudes")  
    ///   variationalFormDepth - Variational form depth
    ///   note - Optional note about the model
    let saveVQCModelAsync
        (filePath: string)
        (parameters: float array)
        (finalLoss: float)
        (numQubits: int)
        (featureMapType: string)
        (featureMapDepth: int)
        (variationalFormType: string)
        (variationalFormDepth: int)
        (note: string option)
        : Async<QuantumResult<unit>> =
        async {
            try
                let model = {
                    Parameters = parameters
                    FinalLoss = finalLoss
                    NumQubits = numQubits
                    FeatureMapType = featureMapType
                    FeatureMapDepth = featureMapDepth
                    VariationalFormType = variationalFormType
                    VariationalFormDepth = variationalFormDepth
                    SavedAt = DateTime.UtcNow.ToString("o")
                    Note = note
                }
                
                let options = JsonSerializerOptions()
                options.WriteIndented <- true
                
                let json = JsonSerializer.Serialize(model, options)
                do! File.WriteAllTextAsync(filePath, json) |> Async.AwaitTask
                
                return Ok ()
            with ex ->
                return Error (QuantumError.ValidationError ("Input", $"Failed to save model: {ex.Message}"))
        }
    
    [<System.Obsolete("Use saveVQCModelAsync for better performance and to avoid blocking threads")>]
    let saveVQCModel
        (filePath: string)
        (parameters: float array)
        (finalLoss: float)
        (numQubits: int)
        (featureMapType: string)
        (featureMapDepth: int)
        (variationalFormType: string)
        (variationalFormDepth: int)
        (note: string option)
        : QuantumResult<unit> =
        saveVQCModelAsync filePath parameters finalLoss numQubits featureMapType featureMapDepth variationalFormType variationalFormDepth note
        |> Async.RunSynchronously
    
    /// Save VQC training result with metadata (classification)
    ///
    /// Convenience function that takes VQC.TrainingResult directly
    let saveVQCTrainingResult
        (filePath: string)
        (result: VQC.TrainingResult)
        (numQubits: int)
        (featureMapType: string)
        (featureMapDepth: int)
        (variationalFormType: string)
        (variationalFormDepth: int)
        (note: string option)
        : QuantumResult<unit> =
        
        let finalLoss =
            match result.LossHistory with
            | [] -> 0.0
            | losses -> List.last losses
        
        saveVQCModelAsync
            filePath
            result.Parameters
            finalLoss
            numQubits
            featureMapType
            featureMapDepth
            variationalFormType
            variationalFormDepth
            note
        |> Async.RunSynchronously
    
    /// Save VQC regression training result with metadata
    ///
    /// Convenience function that takes VQC.RegressionTrainingResult directly
    let saveVQCRegressionTrainingResult
        (filePath: string)
        (result: VQC.RegressionTrainingResult)
        (numQubits: int)
        (featureMapType: string)
        (featureMapDepth: int)
        (variationalFormType: string)
        (variationalFormDepth: int)
        (note: string option)
        : QuantumResult<unit> =
        
        // For regression, use TrainMSE as the "loss"
        let finalLoss = result.TrainMSE
        
        saveVQCModelAsync
            filePath
            result.Parameters
            finalLoss
            numQubits
            featureMapType
            featureMapDepth
            variationalFormType
            variationalFormDepth
            note
        |> Async.RunSynchronously
    
    /// Save VQC multi-class training result (one-vs-rest)
    ///
    /// Saves all binary classifiers with full architecture metadata
    let saveVQCMultiClassTrainingResult
        (filePath: string)
        (result: VQC.MultiClassTrainingResult)
        (numQubits: int)
        (featureMapType: string)
        (featureMapDepth: int)
        (variationalFormType: string)
        (variationalFormDepth: int)
        (note: string option)
        : QuantumResult<unit> =
        
        try
            // Convert all binary classifiers to serializable format
            let classifiers =
                result.Classifiers
                |> Array.map (fun classifier -> {
                    Parameters = classifier.Parameters
                    TrainAccuracy = classifier.TrainAccuracy
                    NumIterations = classifier.LossHistory.Length
                })
            
            let model = {
                Classifiers = classifiers
                ClassLabels = result.ClassLabels
                TrainAccuracy = result.TrainAccuracy
                NumClasses = result.NumClasses
                NumQubits = numQubits
                FeatureMapType = featureMapType
                FeatureMapDepth = featureMapDepth
                VariationalFormType = variationalFormType
                VariationalFormDepth = variationalFormDepth
                SavedAt = DateTime.UtcNow.ToString("o")
                Note = note
            }
            
            let options = JsonSerializerOptions()
            options.WriteIndented <- true
            
            let json = JsonSerializer.Serialize(model, options)
            File.WriteAllText(filePath, json)
            
            Ok ()
        with ex ->
            Error (QuantumError.ValidationError ("Input", $"Failed to save multi-class model: {ex.Message}"))
    
    /// Load VQC model from JSON file
    ///
    /// Returns: Serializable model with all metadata
    let loadVQCModel
        (filePath: string)
        : QuantumResult<SerializableVQCModel> =
        
        try
            if not (File.Exists filePath) then
                Error (QuantumError.ValidationError ("Input", $"File not found: {filePath}"))
            else
                let json = File.ReadAllText(filePath)
                let model = JsonSerializer.Deserialize<SerializableVQCModel>(json)
                Ok model
        with ex ->
            Error (QuantumError.ValidationError ("Input", $"Failed to load model: {ex.Message}"))
    
    /// Load VQC multi-class model from JSON file
    ///
    /// Returns: Serializable multi-class model with all classifiers
    let loadVQCMultiClassModel
        (filePath: string)
        : QuantumResult<SerializableMultiClassVQCModel> =
        
        try
            if not (File.Exists filePath) then
                Error (QuantumError.ValidationError ("Input", $"File not found: {filePath}"))
            else
                let json = File.ReadAllText(filePath)
                let model = JsonSerializer.Deserialize<SerializableMultiClassVQCModel>(json)
                Ok model
        with ex ->
            Error (QuantumError.ValidationError ("Input", $"Failed to load multi-class model: {ex.Message}"))
    
    /// Load only the parameters from a saved model
    ///
    /// Convenience function when you only need the weights
    let loadVQCParameters
        (filePath: string)
        : QuantumResult<float array> =
        
        loadVQCModel filePath
        |> Result.map (fun model -> model.Parameters)
    
    // ========================================================================
    // SVM SERIALIZATION
    // ========================================================================
    
    /// Save SVM model to JSON file
    ///
    /// Parameters:
    ///   filePath - Path to save JSON file
    ///   svmModel - Trained SVM model
    ///   numQubits - Number of qubits used
    ///   note - Optional note about the model
    let saveSVMModel
        (filePath: string)
        (svmModel: QuantumKernelSVM.SVMModel)
        (numQubits: int)
        (note: string option)
        : QuantumResult<unit> =
        
        try
            // Extract feature map info
            let fmType, fmDepth =
                match svmModel.FeatureMap with
                | FeatureMapType.ZZFeatureMap d -> ("ZZFeatureMap", d)
                | FeatureMapType.PauliFeatureMap (_, d) -> ("PauliFeatureMap", d)
                | FeatureMapType.AngleEncoding -> ("AngleEncoding", 0)
                | FeatureMapType.AmplitudeEncoding -> ("AmplitudeEncoding", 0)
            
            let model = {
                SupportVectorIndices = svmModel.SupportVectorIndices
                Alphas = svmModel.Alphas
                Bias = svmModel.Bias
                TrainData = svmModel.TrainData
                TrainLabels = svmModel.TrainLabels
                FeatureMapType = fmType
                FeatureMapDepth = fmDepth
                NumQubits = numQubits
                SavedAt = DateTime.UtcNow.ToString("o")
                Note = note
            }
            
            let options = JsonSerializerOptions()
            options.WriteIndented <- true
            
            let json = JsonSerializer.Serialize(model, options)
            File.WriteAllText(filePath, json)
            
            Ok ()
        with ex ->
            Error (QuantumError.ValidationError ("Input", $"Failed to save SVM model: {ex.Message}"))
    
    /// Load SVM model from JSON file
    ///
    /// Returns: Serializable SVM model with all metadata
    let loadSVMModel
        (filePath: string)
        : QuantumResult<SerializableSVMModel> =
        
        try
            if not (File.Exists filePath) then
                Error (QuantumError.ValidationError ("Input", $"File not found: {filePath}"))
            else
                let json = File.ReadAllText(filePath)
                let model = JsonSerializer.Deserialize<SerializableSVMModel>(json)
                Ok model
        with ex ->
            Error (QuantumError.ValidationError ("Input", $"Failed to load SVM model: {ex.Message}"))
    
    // ========================================================================
    // MODEL INFORMATION
    // ========================================================================
    
    /// Get model information without loading full model
    ///
    /// Returns: (num_qubits, num_parameters, final_loss, saved_at)
    let getVQCModelInfo
        (filePath: string)
        : QuantumResult<int * int * float * string> =
        
        loadVQCModel filePath
        |> Result.map (fun model ->
            (model.NumQubits,
             model.Parameters.Length,
             model.FinalLoss,
             model.SavedAt))
    
    /// Print model information to console
    let printVQCModelInfo
        (filePath: string)
        : QuantumResult<unit> =
        
        loadVQCModel filePath
        |> Result.map (fun model ->
            printfn "=== VQC Model Information ==="
            printfn "File: %s" filePath
            printfn "Saved at: %s" model.SavedAt
            printfn "Qubits: %d" model.NumQubits
            printfn "Parameters: %d" model.Parameters.Length
            printfn "Final Loss: %.6f" model.FinalLoss
            printfn "Feature Map: %s (depth=%d)" model.FeatureMapType model.FeatureMapDepth
            printfn "Variational Form: %s (depth=%d)" model.VariationalFormType model.VariationalFormDepth
            match model.Note with
            | Some note -> printfn "Note: %s" note
            | None -> ()
            printfn "============================")
    
    // ========================================================================
    // BATCH OPERATIONS
    // ========================================================================
    
    /// Save multiple models with automatic naming
    ///
    /// Files will be named: {baseFileName}_1.json, {baseFileName}_2.json, etc.
    let saveVQCModelBatch
        (baseFileName: string)
        (models: (float array * float * string option) array)
        (numQubits: int)
        (featureMapType: string)
        (featureMapDepth: int)
        (variationalFormType: string)
        (variationalFormDepth: int)
        : QuantumResult<string array> =
        
        let results =
            models
            |> Array.mapi (fun i (parameters, finalLoss, note) ->
                let fileName = $"{baseFileName}_{i + 1}.json"
                saveVQCModelAsync fileName parameters finalLoss numQubits featureMapType featureMapDepth variationalFormType variationalFormDepth note 
                |> Async.RunSynchronously
                |> Result.map (fun () -> fileName))
        
        // Check for any errors and collect all successful file names
        let firstError = results |> Array.tryPick (function Error e -> Some e | Ok _ -> None)
        
        match firstError with
        | Some error -> Error error
        | None -> Ok (results |> Array.choose (function Ok fileName -> Some fileName | Error _ -> None))
    
    /// Load multiple models from directory
    ///
    /// Loads all .json files matching pattern in directory
    let loadVQCModelBatch
        (directory: string)
        (pattern: string)
        : QuantumResult<SerializableVQCModel array> =
        
        try
            if not (Directory.Exists directory) then
                Error (QuantumError.ValidationError ("Input", $"Directory not found: {directory}"))
            else
                let files = Directory.GetFiles(directory, pattern)
                
                if files.Length = 0 then
                    Error (QuantumError.ValidationError ("Input", $"No files matching pattern '{pattern}' found in {directory}"))
                else
                    let results =
                        files
                        |> Array.map loadVQCModel
                    
                    // Check for errors
                    let firstError =
                        results
                        |> Array.tryPick (fun result ->
                            match result with
                            | Error e -> Some e
                            | Ok _ -> None)
                    
                    match firstError with
                    | Some error -> Error error
                    | None ->
                        let models =
                            results
                            |> Array.map (fun result ->
                                match result with
                                | Ok model -> model
                                | Error _ -> failwith "Unreachable")
                        Ok models
        with ex ->
            Error (QuantumError.ValidationError ("Input", $"Failed to load batch: {ex.Message}"))
    
    // ========================================================================
    // TRANSFER LEARNING SUPPORT
    // ========================================================================
    
    /// Load pre-trained model for transfer learning
    ///
    /// Returns: (parameters, architecture_info) for initializing new VQC
    let loadForTransferLearning
        (filePath: string)
        : QuantumResult<float array * (int * string * int * string * int)> =
        
        loadVQCModel filePath
        |> Result.map (fun model ->
            (model.Parameters,
             (model.NumQubits,
              model.FeatureMapType,
              model.FeatureMapDepth,
              model.VariationalFormType,
              model.VariationalFormDepth)))
    
    /// Initialize parameters for fine-tuning with optional layer freezing
    ///
    /// Parameters:
    ///   pretrainedParams - Pre-trained parameters from base model
    ///   numLayers - Number of parameter layers in variational form
    ///   freezeLayers - Number of initial layers to freeze (keep unchanged)
    ///
    /// Returns: Initialized parameters where frozen layers use pre-trained values
    ///          and unfrozen layers can be randomized or reused
    let initializeForFineTuning
        (pretrainedParams: float array)
        (numLayers: int)
        (freezeLayers: int)
        : QuantumResult<float array * int array> =
        
        if freezeLayers < 0 then
            Error (QuantumError.ValidationError ("Input", "freezeLayers must be non-negative"))
        elif freezeLayers > numLayers then
            Error (QuantumError.ValidationError ("Input", $"freezeLayers ({freezeLayers}) cannot exceed numLayers ({numLayers})"))
        else
            let paramsPerLayer = pretrainedParams.Length / numLayers
            
            if pretrainedParams.Length % numLayers <> 0 then
                Error (QuantumError.ValidationError ("Input", $"Parameters ({pretrainedParams.Length}) not evenly divisible by layers ({numLayers})"))
            else
                // Indices of frozen parameters (first freezeLayers * paramsPerLayer)
                let frozenIndices =
                    [| 0 .. (freezeLayers * paramsPerLayer - 1) |]
                
                Ok (pretrainedParams, frozenIndices)
    
    /// Apply parameter update respecting frozen layers
    ///
    /// Only updates parameters not in frozenIndices
    let updateParametersWithFrozenLayers
        (currentParams: float array)
        (gradients: float array)
        (learningRate: float)
        (frozenIndices: int array)
        : float array =
        
        let frozenSet = Set.ofArray frozenIndices
        
        currentParams
        |> Array.mapi (fun i param ->
            if frozenSet.Contains i then
                param  // Keep frozen parameter unchanged
            else
                param - learningRate * gradients.[i]  // Apply gradient update
        )
    
    /// Parse FeatureMapType from saved string representation
    ///
    /// Helper for reconstructing feature map from serialized model
    let parseFeatureMapType (fmType: string) (fmDepth: int) : QuantumResult<FeatureMapType> =
        match fmType with
        | "ZZFeatureMap" -> Ok (FeatureMapType.ZZFeatureMap fmDepth)
        | "PauliFeatureMap" -> 
            // Default Pauli string for saved models
            Ok (FeatureMapType.PauliFeatureMap (["Z"; "ZZ"], fmDepth))
        | "AngleEncoding" -> Ok FeatureMapType.AngleEncoding
        | "AmplitudeEncoding" -> Ok FeatureMapType.AmplitudeEncoding
        | _ -> Error (QuantumError.ValidationError ("Input", $"Unknown feature map type: {fmType}"))
    
    /// Parse VariationalForm from saved string representation
    ///
    /// Helper for reconstructing variational form from serialized model
    let parseVariationalForm (vfType: string) (vfDepth: int) : QuantumResult<VariationalForm> =
        match vfType with
        | "RealAmplitudes" -> Ok (VariationalForm.RealAmplitudes vfDepth)
        | "EfficientSU2" -> Ok (VariationalForm.EfficientSU2 vfDepth)
        | "TwoLocal" ->
            // Default two-local configuration
            Ok (VariationalForm.TwoLocal ("RY", "CX", vfDepth))
        | _ -> Error (QuantumError.ValidationError ("Input", $"Unknown variational form type: {vfType}"))
    
    /// Reconstruct QuantumKernelSVM.SVMModel from serialized data
    ///
    /// Returns: Full SVM model ready for prediction
    let reconstructSVMModel
        (serialized: SerializableSVMModel)
        : QuantumResult<QuantumKernelSVM.SVMModel> =
        
        // Parse feature map
        parseFeatureMapType serialized.FeatureMapType serialized.FeatureMapDepth
        |> Result.map (fun featureMap ->
            {
                SupportVectorIndices = serialized.SupportVectorIndices
                Alphas = serialized.Alphas
                Bias = serialized.Bias
                TrainData = serialized.TrainData
                TrainLabels = serialized.TrainLabels
                FeatureMap = featureMap
            })
    
    // ========================================================================
    // TRANSFER LEARNING UTILITIES
    // ========================================================================
    
    /// Check if two models have compatible architectures for transfer learning
    ///
    /// Returns: true if models can share parameters (same architecture)
    let areModelsCompatible
        (model1Path: string)
        (model2Path: string)
        : QuantumResult<bool> =
        
        loadVQCModel model1Path
        |> Result.bind (fun m1 ->
            loadVQCModel model2Path
            |> Result.map (fun m2 ->
                m1.NumQubits = m2.NumQubits &&
                m1.FeatureMapType = m2.FeatureMapType &&
                m1.FeatureMapDepth = m2.FeatureMapDepth &&
                m1.VariationalFormType = m2.VariationalFormType &&
                m1.VariationalFormDepth = m2.VariationalFormDepth))
    
    /// Extract feature extractor (frozen layers) from pre-trained model
    ///
    /// Returns subset of parameters representing the feature extraction layers
    let extractFeatureExtractor
        (modelPath: string)
        (numLayers: int)
        (extractLayers: int)
        : QuantumResult<float array> =
        
        if extractLayers > numLayers then
            Error (QuantumError.ValidationError ("Input", $"extractLayers ({extractLayers}) cannot exceed numLayers ({numLayers})"))
        else
            loadVQCParameters modelPath
            |> Result.bind (fun parameters ->
                let paramsPerLayer = parameters.Length / numLayers
                if parameters.Length % numLayers <> 0 then
                    Error (QuantumError.ValidationError ("Input", $"Parameters ({parameters.Length}) not evenly divisible by layers ({numLayers})"))
                else
                    let extractedParams = parameters.[0 .. (extractLayers * paramsPerLayer - 1)]
                    Ok extractedParams)
    
    // ========================================================================
    // PORTFOLIO SOLUTION SERIALIZATION
    // ========================================================================
    
    /// Serializable asset allocation for portfolio solutions
    type SerializableAllocation = {
        /// Asset symbol (e.g., "AAPL")
        Symbol: string
        
        /// Number of shares allocated
        Shares: float
        
        /// Dollar value of allocation
        Value: float
        
        /// Percentage of total portfolio (0.0 to 1.0)
        Percentage: float
        
        /// Original asset data
        ExpectedReturn: float
        Risk: float
        Price: float
    }
    
    /// Serializable QUBO matrix for portfolio optimization
    type SerializableQuboMatrix = {
        /// Number of variables (assets)
        NumVariables: int
        
        /// QUBO coefficients as list of (row, col, value) tuples
        /// Stored as list for JSON compatibility
        Coefficients: (int * int * float) list
    }
    
    /// Serializable quantum portfolio solution (JSON-friendly)
    type SerializablePortfolioSolution = {
        /// Asset allocations
        Allocations: SerializableAllocation list
        
        /// Total portfolio value
        TotalValue: float
        
        /// Expected portfolio return (weighted average)
        ExpectedReturn: float
        
        /// Portfolio risk (standard deviation)
        Risk: float
        
        /// Sharpe ratio (return / risk)
        SharpeRatio: float
        
        /// Backend used for quantum execution
        BackendName: string
        
        /// Number of measurement shots
        NumShots: int
        
        /// Execution time in milliseconds
        ElapsedMs: float
        
        /// QAOA parameters used (gamma, beta)
        QaoaGamma: float
        QaoaBeta: float
        
        /// QUBO objective value (energy)
        BestEnergy: float
        
        /// Selected assets mapping (symbol -> selected)
        SelectedAssets: (string * bool) list
        
        /// Risk aversion parameter used
        RiskAversion: float
        
        /// Budget constraint
        Budget: float
        
        /// QUBO matrix (optional, for reproducibility)
        QuboMatrix: SerializableQuboMatrix option
        
        /// Timestamp when saved
        SavedAt: string
        
        /// Optional note
        Note: string option
    }
    
    /// Convert QUBO matrix Map to serializable format
    let private quboToSerializable (quboMap: Map<(int * int), float>) (numVars: int) : SerializableQuboMatrix =
        let coefficients =
            quboMap
            |> Map.toList
            |> List.map (fun ((i, j), v) -> (i, j, v))
        {
            NumVariables = numVars
            Coefficients = coefficients
        }
    
    /// Convert serializable QUBO back to Map format
    let private serializableToQubo (serialized: SerializableQuboMatrix) : Map<(int * int), float> =
        serialized.Coefficients
        |> List.map (fun (i, j, v) -> ((i, j), v))
        |> Map.ofList
    
    /// Save quantum portfolio solution to JSON file
    ///
    /// This is a data-centric serialization that takes all values directly,
    /// avoiding coupling to specific solver types. Use helper functions in
    /// QuantumPortfolioSolver to convert from QuantumPortfolioSolution if needed.
    ///
    /// Parameters:
    ///   filePath - Path to save JSON file
    ///   allocations - List of asset allocations
    ///   totalValue - Total portfolio value
    ///   expectedReturn - Expected portfolio return
    ///   risk - Portfolio risk (standard deviation)
    ///   sharpeRatio - Sharpe ratio
    ///   backendName - Backend used for execution
    ///   numShots - Number of measurement shots
    ///   elapsedMs - Execution time in milliseconds
    ///   qaoaParams - QAOA parameters (gamma, beta)
    ///   bestEnergy - QUBO objective value
    ///   selectedAssets - Map of asset symbol -> selected
    ///   riskAversion - Risk aversion parameter used
    ///   budget - Budget constraint used
    ///   quboMatrix - Optional QUBO matrix for reproducibility
    ///   note - Optional note about the solution
    let savePortfolioSolutionAsync
        (filePath: string)
        (allocations: SerializableAllocation list)
        (totalValue: float)
        (expectedReturn: float)
        (risk: float)
        (sharpeRatio: float)
        (backendName: string)
        (numShots: int)
        (elapsedMs: float)
        (qaoaParams: float * float)
        (bestEnergy: float)
        (selectedAssets: Map<string, bool>)
        (riskAversion: float)
        (budget: float)
        (quboMatrix: Map<(int * int), float> option)
        (numVariables: int)
        (note: string option)
        : Async<QuantumResult<unit>> =
        async {
            try
                let (gamma, beta) = qaoaParams
                
                // Convert selected assets to list for JSON
                let selectedAssetsList =
                    selectedAssets
                    |> Map.toList
                
                // Convert QUBO matrix if provided
                let serializableQubo =
                    quboMatrix
                    |> Option.map (fun q -> quboToSerializable q numVariables)
                
                let model = {
                    Allocations = allocations
                    TotalValue = totalValue
                    ExpectedReturn = expectedReturn
                    Risk = risk
                    SharpeRatio = sharpeRatio
                    BackendName = backendName
                    NumShots = numShots
                    ElapsedMs = elapsedMs
                    QaoaGamma = gamma
                    QaoaBeta = beta
                    BestEnergy = bestEnergy
                    SelectedAssets = selectedAssetsList
                    RiskAversion = riskAversion
                    Budget = budget
                    QuboMatrix = serializableQubo
                    SavedAt = DateTime.UtcNow.ToString("o")
                    Note = note
                }
                
                let options = JsonSerializerOptions()
                options.WriteIndented <- true
                
                let json = JsonSerializer.Serialize(model, options)
                do! File.WriteAllTextAsync(filePath, json) |> Async.AwaitTask
                
                return Ok ()
            with ex ->
                return Error (QuantumError.ValidationError ("Input", $"Failed to save portfolio solution: {ex.Message}"))
        }
    
    /// Save portfolio solution synchronously
    [<System.Obsolete("Use savePortfolioSolutionAsync for better performance")>]
    let savePortfolioSolution
        (filePath: string)
        (allocations: SerializableAllocation list)
        (totalValue: float)
        (expectedReturn: float)
        (risk: float)
        (sharpeRatio: float)
        (backendName: string)
        (numShots: int)
        (elapsedMs: float)
        (qaoaParams: float * float)
        (bestEnergy: float)
        (selectedAssets: Map<string, bool>)
        (riskAversion: float)
        (budget: float)
        (quboMatrix: Map<(int * int), float> option)
        (numVariables: int)
        (note: string option)
        : QuantumResult<unit> =
        savePortfolioSolutionAsync filePath allocations totalValue expectedReturn risk sharpeRatio 
            backendName numShots elapsedMs qaoaParams bestEnergy selectedAssets riskAversion budget quboMatrix numVariables note
        |> Async.RunSynchronously
    
    /// Load quantum portfolio solution from JSON file
    ///
    /// Returns: Serializable portfolio solution with all metadata
    let loadPortfolioSolution
        (filePath: string)
        : QuantumResult<SerializablePortfolioSolution> =
        
        try
            if not (File.Exists filePath) then
                Error (QuantumError.ValidationError ("Input", $"File not found: {filePath}"))
            else
                let json = File.ReadAllText(filePath)
                let model = JsonSerializer.Deserialize<SerializablePortfolioSolution>(json)
                Ok model
        with ex ->
            Error (QuantumError.ValidationError ("Input", $"Failed to load portfolio solution: {ex.Message}"))
    
    /// Load QUBO matrix from saved portfolio solution
    ///
    /// Returns: QUBO matrix as Map if present in saved solution
    let loadPortfolioQubo
        (filePath: string)
        : QuantumResult<Map<(int * int), float> option> =
        
        loadPortfolioSolution filePath
        |> Result.map (fun solution ->
            solution.QuboMatrix
            |> Option.map serializableToQubo)
    
    /// Load QAOA parameters from saved portfolio solution
    ///
    /// Returns: (gamma, beta) tuple
    let loadPortfolioQaoaParams
        (filePath: string)
        : QuantumResult<float * float> =
        
        loadPortfolioSolution filePath
        |> Result.map (fun solution ->
            (solution.QaoaGamma, solution.QaoaBeta))
    
    /// Get portfolio solution summary without loading full data
    ///
    /// Returns: (total_value, expected_return, risk, sharpe_ratio, backend_name, saved_at)
    let getPortfolioSolutionInfo
        (filePath: string)
        : QuantumResult<float * float * float * float * string * string> =
        
        loadPortfolioSolution filePath
        |> Result.map (fun solution ->
            (solution.TotalValue,
             solution.ExpectedReturn,
             solution.Risk,
             solution.SharpeRatio,
             solution.BackendName,
             solution.SavedAt))
    
    /// Print portfolio solution information to console
    let printPortfolioSolutionInfo
        (filePath: string)
        : QuantumResult<unit> =
        
        loadPortfolioSolution filePath
        |> Result.map (fun solution ->
            printfn "=== Portfolio Solution Information ==="
            printfn "File: %s" filePath
            printfn "Saved at: %s" solution.SavedAt
            printfn "Backend: %s" solution.BackendName
            printfn "Total Value: $%.2f" solution.TotalValue
            printfn "Expected Return: %.2f%%" (solution.ExpectedReturn * 100.0)
            printfn "Risk: %.2f%%" (solution.Risk * 100.0)
            printfn "Sharpe Ratio: %.4f" solution.SharpeRatio
            printfn "QAOA Parameters: gamma=%.4f, beta=%.4f" solution.QaoaGamma solution.QaoaBeta
            printfn "Risk Aversion: %.2f" solution.RiskAversion
            printfn "Budget: $%.2f" solution.Budget
            printfn "Best Energy: %.4f" solution.BestEnergy
            printfn "Num Shots: %d" solution.NumShots
            printfn "Elapsed: %.2fms" solution.ElapsedMs
            printfn ""
            printfn "Allocations (%d assets):" solution.Allocations.Length
            solution.Allocations
            |> List.iter (fun alloc ->
                printfn "  %s: %.2f shares @ $%.2f = $%.2f (%.1f%%)" 
                    alloc.Symbol alloc.Shares alloc.Price alloc.Value (alloc.Percentage * 100.0))
            match solution.Note with
            | Some note -> printfn "Note: %s" note
            | None -> ()
            printfn "======================================")
