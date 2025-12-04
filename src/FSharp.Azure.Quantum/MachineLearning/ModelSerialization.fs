namespace FSharp.Azure.Quantum.MachineLearning

/// Simple Model Serialization for VQC Models
///
/// Provides basic save/load functionality for trained VQC models.
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
        : Result<unit, string> =
        
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
            File.WriteAllText(filePath, json)
            
            Ok ()
        with ex ->
            Error $"Failed to save model: {ex.Message}"
    
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
        : Result<unit, string> =
        
        let finalLoss =
            match result.LossHistory with
            | [] -> 0.0
            | losses -> List.last losses
        
        saveVQCModel
            filePath
            result.Parameters
            finalLoss
            numQubits
            featureMapType
            featureMapDepth
            variationalFormType
            variationalFormDepth
            note
    
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
        : Result<unit, string> =
        
        // For regression, use TrainMSE as the "loss"
        let finalLoss = result.TrainMSE
        
        saveVQCModel
            filePath
            result.Parameters
            finalLoss
            numQubits
            featureMapType
            featureMapDepth
            variationalFormType
            variationalFormDepth
            note
    
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
        : Result<unit, string> =
        
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
            Error $"Failed to save multi-class model: {ex.Message}"
    
    /// Load VQC model from JSON file
    ///
    /// Returns: Serializable model with all metadata
    let loadVQCModel
        (filePath: string)
        : Result<SerializableVQCModel, string> =
        
        try
            if not (File.Exists filePath) then
                Error $"File not found: {filePath}"
            else
                let json = File.ReadAllText(filePath)
                let model = JsonSerializer.Deserialize<SerializableVQCModel>(json)
                Ok model
        with ex ->
            Error $"Failed to load model: {ex.Message}"
    
    /// Load VQC multi-class model from JSON file
    ///
    /// Returns: Serializable multi-class model with all classifiers
    let loadVQCMultiClassModel
        (filePath: string)
        : Result<SerializableMultiClassVQCModel, string> =
        
        try
            if not (File.Exists filePath) then
                Error $"File not found: {filePath}"
            else
                let json = File.ReadAllText(filePath)
                let model = JsonSerializer.Deserialize<SerializableMultiClassVQCModel>(json)
                Ok model
        with ex ->
            Error $"Failed to load multi-class model: {ex.Message}"
    
    /// Load only the parameters from a saved model
    ///
    /// Convenience function when you only need the weights
    let loadVQCParameters
        (filePath: string)
        : Result<float array, string> =
        
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
        : Result<unit, string> =
        
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
            Error $"Failed to save SVM model: {ex.Message}"
    
    /// Load SVM model from JSON file
    ///
    /// Returns: Serializable SVM model with all metadata
    let loadSVMModel
        (filePath: string)
        : Result<SerializableSVMModel, string> =
        
        try
            if not (File.Exists filePath) then
                Error $"File not found: {filePath}"
            else
                let json = File.ReadAllText(filePath)
                let model = JsonSerializer.Deserialize<SerializableSVMModel>(json)
                Ok model
        with ex ->
            Error $"Failed to load SVM model: {ex.Message}"
    
    // ========================================================================
    // MODEL INFORMATION
    // ========================================================================
    
    /// Get model information without loading full model
    ///
    /// Returns: (num_qubits, num_parameters, final_loss, saved_at)
    let getVQCModelInfo
        (filePath: string)
        : Result<int * int * float * string, string> =
        
        loadVQCModel filePath
        |> Result.map (fun model ->
            (model.NumQubits,
             model.Parameters.Length,
             model.FinalLoss,
             model.SavedAt))
    
    /// Print model information to console
    let printVQCModelInfo
        (filePath: string)
        : Result<unit, string> =
        
        match loadVQCModel filePath with
        | Error e -> Error e
        | Ok model ->
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
            printfn "============================"
            Ok ()
    
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
        : Result<string array, string> =
        
        let results =
            models
            |> Array.mapi (fun i (parameters, finalLoss, note) ->
                let fileName = $"{baseFileName}_{i + 1}.json"
                match saveVQCModel fileName parameters finalLoss numQubits featureMapType featureMapDepth variationalFormType variationalFormDepth note with
                | Ok () -> Ok fileName
                | Error e -> Error e)
        
        // Check for any errors
        let firstError =
            results
            |> Array.tryPick (fun result ->
                match result with
                | Error e -> Some e
                | Ok _ -> None)
        
        match firstError with
        | Some error -> Error error
        | None ->
            let fileNames =
                results
                |> Array.map (fun result ->
                    match result with
                    | Ok fileName -> fileName
                    | Error _ -> failwith "Unreachable")
            Ok fileNames
    
    /// Load multiple models from directory
    ///
    /// Loads all .json files matching pattern in directory
    let loadVQCModelBatch
        (directory: string)
        (pattern: string)
        : Result<SerializableVQCModel array, string> =
        
        try
            if not (Directory.Exists directory) then
                Error $"Directory not found: {directory}"
            else
                let files = Directory.GetFiles(directory, pattern)
                
                if files.Length = 0 then
                    Error $"No files matching pattern '{pattern}' found in {directory}"
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
            Error $"Failed to load batch: {ex.Message}"
    
    // ========================================================================
    // TRANSFER LEARNING SUPPORT
    // ========================================================================
    
    /// Load pre-trained model for transfer learning
    ///
    /// Returns: (parameters, architecture_info) for initializing new VQC
    let loadForTransferLearning
        (filePath: string)
        : Result<float array * (int * string * int * string * int), string> =
        
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
        : Result<float array * int array, string> =
        
        if freezeLayers < 0 then
            Error "freezeLayers must be non-negative"
        elif freezeLayers > numLayers then
            Error $"freezeLayers ({freezeLayers}) cannot exceed numLayers ({numLayers})"
        else
            let paramsPerLayer = pretrainedParams.Length / numLayers
            
            if pretrainedParams.Length % numLayers <> 0 then
                Error $"Parameters ({pretrainedParams.Length}) not evenly divisible by layers ({numLayers})"
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
    let parseFeatureMapType (fmType: string) (fmDepth: int) : Result<FeatureMapType, string> =
        match fmType with
        | "ZZFeatureMap" -> Ok (FeatureMapType.ZZFeatureMap fmDepth)
        | "PauliFeatureMap" -> 
            // Default Pauli string for saved models
            Ok (FeatureMapType.PauliFeatureMap (["Z"; "ZZ"], fmDepth))
        | "AngleEncoding" -> Ok FeatureMapType.AngleEncoding
        | "AmplitudeEncoding" -> Ok FeatureMapType.AmplitudeEncoding
        | _ -> Error $"Unknown feature map type: {fmType}"
    
    /// Parse VariationalForm from saved string representation
    ///
    /// Helper for reconstructing variational form from serialized model
    let parseVariationalForm (vfType: string) (vfDepth: int) : Result<VariationalForm, string> =
        match vfType with
        | "RealAmplitudes" -> Ok (VariationalForm.RealAmplitudes vfDepth)
        | "EfficientSU2" -> Ok (VariationalForm.EfficientSU2 vfDepth)
        | "TwoLocal" ->
            // Default two-local configuration
            Ok (VariationalForm.TwoLocal ("RY", "CX", vfDepth))
        | _ -> Error $"Unknown variational form type: {vfType}"
    
    /// Reconstruct QuantumKernelSVM.SVMModel from serialized data
    ///
    /// Returns: Full SVM model ready for prediction
    let reconstructSVMModel
        (serialized: SerializableSVMModel)
        : Result<QuantumKernelSVM.SVMModel, string> =
        
        // Parse feature map
        match parseFeatureMapType serialized.FeatureMapType serialized.FeatureMapDepth with
        | Error e -> Error e
        | Ok featureMap ->
            let svmModel : QuantumKernelSVM.SVMModel = {
                SupportVectorIndices = serialized.SupportVectorIndices
                Alphas = serialized.Alphas
                Bias = serialized.Bias
                TrainData = serialized.TrainData
                TrainLabels = serialized.TrainLabels
                FeatureMap = featureMap
            }
            Ok svmModel
    
    // ========================================================================
    // TRANSFER LEARNING UTILITIES
    // ========================================================================
    
    /// Check if two models have compatible architectures for transfer learning
    ///
    /// Returns: true if models can share parameters (same architecture)
    let areModelsCompatible
        (model1Path: string)
        (model2Path: string)
        : Result<bool, string> =
        
        match loadVQCModel model1Path, loadVQCModel model2Path with
        | Error e, _ -> Error e
        | _, Error e -> Error e
        | Ok m1, Ok m2 ->
            let compatible =
                m1.NumQubits = m2.NumQubits &&
                m1.FeatureMapType = m2.FeatureMapType &&
                m1.FeatureMapDepth = m2.FeatureMapDepth &&
                m1.VariationalFormType = m2.VariationalFormType &&
                m1.VariationalFormDepth = m2.VariationalFormDepth
            Ok compatible
    
    /// Extract feature extractor (frozen layers) from pre-trained model
    ///
    /// Returns subset of parameters representing the feature extraction layers
    let extractFeatureExtractor
        (modelPath: string)
        (numLayers: int)
        (extractLayers: int)
        : Result<float array, string> =
        
        if extractLayers > numLayers then
            Error $"extractLayers ({extractLayers}) cannot exceed numLayers ({numLayers})"
        else
            match loadVQCParameters modelPath with
            | Error e -> Error e
            | Ok parameters ->
                let paramsPerLayer = parameters.Length / numLayers
                if parameters.Length % numLayers <> 0 then
                    Error $"Parameters ({parameters.Length}) not evenly divisible by layers ({numLayers})"
                else
                    let extractedParams = parameters.[0 .. (extractLayers * paramsPerLayer - 1)]
                    Ok extractedParams
