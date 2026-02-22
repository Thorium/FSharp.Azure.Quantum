namespace FSharp.Azure.Quantum.MachineLearning

open FSharp.Azure.Quantum.Core

/// SVM Model Serialization.
///
/// Provides save/load functionality for trained SVM models (binary and multi-class).
/// Stores model parameters and data as JSON for easy inspection and portability.

open System
open System.IO
open System.Text.Json
open System.Threading
open System.Threading.Tasks
open Microsoft.Extensions.Logging

module SVMModelSerialization =
    
    // ========================================================================
    // TYPES
    // ========================================================================
    
    /// Serializable feature map representation
    type SerializableFeatureMap = {
        Type: string
        Depth: int option
        Paulis: string list option
        Rotation: string option
        Entanglement: string option
    }
    
    /// Serializable binary SVM model (JSON-friendly)
    type SerializableSVMModel = {
        /// Support vector indices
        SupportVectorIndices: int array
        
        /// Lagrange multipliers (alphas)
        Alphas: float array
        
        /// Bias term
        Bias: float
        
        /// Training data
        TrainData: float array array
        
        /// Training labels
        TrainLabels: int array
        
        /// Feature map configuration
        FeatureMap: SerializableFeatureMap
        
        /// Metadata
        SavedAt: string
        Note: string option
    }
    
    /// Serializable multi-class SVM model (JSON-friendly)
    type SerializableMultiClassSVMModel = {
        /// Binary SVM models (one per class)
        BinaryModels: SerializableSVMModel array
        
        /// Class labels
        ClassLabels: int array
        
        /// Number of classes
        NumClasses: int
        
        /// Metadata
        SavedAt: string
        Note: string option
    }
    
    // ========================================================================
    // FEATURE MAP CONVERSION
    // ========================================================================
    
    /// Convert FeatureMapType to serializable format
    let private featureMapToSerializable (fm: FeatureMapType) : SerializableFeatureMap =
        match fm with
        | FeatureMapType.ZZFeatureMap depth ->
            { Type = "ZZFeatureMap"; Depth = Some depth; Paulis = None; Rotation = None; Entanglement = None }
        | FeatureMapType.PauliFeatureMap (paulis, depth) ->
            { Type = "PauliFeatureMap"; Depth = Some depth; Paulis = Some paulis; Rotation = None; Entanglement = None }
        | FeatureMapType.AngleEncoding ->
            { Type = "AngleEncoding"; Depth = None; Paulis = None; Rotation = None; Entanglement = None }
        | FeatureMapType.AmplitudeEncoding ->
            { Type = "AmplitudeEncoding"; Depth = None; Paulis = None; Rotation = None; Entanglement = None }
    
    /// Convert serializable format back to FeatureMapType
    let private serializableToFeatureMap (fm: SerializableFeatureMap) : QuantumResult<FeatureMapType> =
        match fm.Type with
        | "ZZFeatureMap" ->
            match fm.Depth with
            | Some depth -> Ok (FeatureMapType.ZZFeatureMap depth)
            | None -> Error (QuantumError.ValidationError ("FeatureMap", "ZZFeatureMap requires depth"))
        | "PauliFeatureMap" ->
            match fm.Depth, fm.Paulis with
            | Some depth, Some paulis -> Ok (FeatureMapType.PauliFeatureMap (paulis, depth))
            | _ -> Error (QuantumError.ValidationError ("FeatureMap", "PauliFeatureMap requires depth and paulis"))
        | "AngleEncoding" ->
            Ok FeatureMapType.AngleEncoding
        | "AmplitudeEncoding" ->
            Ok FeatureMapType.AmplitudeEncoding
        | _ -> Error (QuantumError.ValidationError ("Input", $"Unknown feature map type: {fm.Type}"))
    
    // ========================================================================
    // BINARY SVM SERIALIZATION
    // ========================================================================
    
    /// Save binary SVM model to JSON file (async, task-based)
    let saveSVMModelAsync
        (filePath: string)
        (model: QuantumKernelSVM.SVMModel)
        (note: string option)
        (cancellationToken: CancellationToken)
        : Task<QuantumResult<unit>> =
        task {
            try
                let serializable = {
                    SupportVectorIndices = model.SupportVectorIndices
                    Alphas = model.Alphas
                    Bias = model.Bias
                    TrainData = model.TrainData
                    TrainLabels = model.TrainLabels
                    FeatureMap = featureMapToSerializable model.FeatureMap
                    SavedAt = DateTime.UtcNow.ToString("o")
                    Note = note
                }
                
                let options = JsonSerializerOptions()
                options.WriteIndented <- true
                
                let json = JsonSerializer.Serialize(serializable, options)
                do! File.WriteAllTextAsync(filePath, json, cancellationToken)
                
                return Ok ()
            with ex ->
                return Error (QuantumError.ValidationError ("Input", $"Failed to save SVM model: {ex.Message}"))
        }
    
    [<System.Obsolete("Use saveSVMModelAsync for better performance and to avoid blocking threads")>]
    let saveSVMModel
        (filePath: string)
        (model: QuantumKernelSVM.SVMModel)
        (note: string option)
        : QuantumResult<unit> =
        saveSVMModelAsync filePath model note CancellationToken.None
        |> Async.AwaitTask
        |> Async.RunSynchronously
    
    /// Load binary SVM model from JSON file (async, task-based)
    let loadSVMModelAsync
        (filePath: string)
        (cancellationToken: CancellationToken)
        : Task<QuantumResult<QuantumKernelSVM.SVMModel>> =
        task {
            try
                if not (File.Exists filePath) then
                    return Error (QuantumError.ValidationError ("Input", $"File not found: {filePath}"))
                else
                    let! json = File.ReadAllTextAsync(filePath, cancellationToken)
                    let serializable = JsonSerializer.Deserialize<SerializableSVMModel>(json)
                    
                    return
                        serializableToFeatureMap serializable.FeatureMap
                        |> Result.map (fun featureMap ->
                            let model : QuantumKernelSVM.SVMModel = {
                                SupportVectorIndices = serializable.SupportVectorIndices
                                Alphas = serializable.Alphas
                                Bias = serializable.Bias
                                TrainData = serializable.TrainData
                                TrainLabels = serializable.TrainLabels
                                FeatureMap = featureMap
                            }
                            model)
            with ex ->
                return Error (QuantumError.ValidationError ("Input", $"Failed to load SVM model: {ex.Message}"))
        }
    
    [<System.Obsolete("Use loadSVMModelAsync for better performance and to avoid blocking threads")>]
    let loadSVMModel
        (filePath: string)
        : QuantumResult<QuantumKernelSVM.SVMModel> =
        loadSVMModelAsync filePath CancellationToken.None
        |> Async.AwaitTask
        |> Async.RunSynchronously
    
    /// Print binary SVM model information via ILogger
    let printSVMModelInfo
        (filePath: string)
        (logger: ILogger option)
        : QuantumResult<unit> =
        
        try
            if not (File.Exists filePath) then
                Error (QuantumError.ValidationError ("Input", $"File not found: {filePath}"))
            else
                let json = File.ReadAllText(filePath)
                let model = JsonSerializer.Deserialize<SerializableSVMModel>(json)
                
                logInfo logger "=== Binary SVM Model Information ==="
                logInfo logger (sprintf "File: %s" filePath)
                logInfo logger (sprintf "Saved at: %s" model.SavedAt)
                logInfo logger (sprintf "Support Vectors: %d" model.SupportVectorIndices.Length)
                logInfo logger (sprintf "Training Samples: %d" model.TrainData.Length)
                logInfo logger (sprintf "Features: %d" (if model.TrainData.Length > 0 then model.TrainData.[0].Length else 0))
                logInfo logger (sprintf "Bias: %.6f" model.Bias)
                logInfo logger (sprintf "Feature Map: %s" model.FeatureMap.Type)
                match model.FeatureMap.Depth with
                | Some d -> logInfo logger (sprintf "  Depth: %d" d)
                | None -> ()
                match model.Note with
                | Some note -> logInfo logger (sprintf "Note: %s" note)
                | None -> ()
                logInfo logger "===================================="
                Ok ()
        with ex ->
            Error (QuantumError.ValidationError ("Input", $"Failed to print SVM model info: {ex.Message}"))
    
    // ========================================================================
    // MULTI-CLASS SVM SERIALIZATION
    // ========================================================================
    
    /// Save multi-class SVM model to JSON file (async, task-based)
    let saveMultiClassSVMModelAsync
        (filePath: string)
        (model: MultiClassSVM.MultiClassModel)
        (note: string option)
        (cancellationToken: CancellationToken)
        : Task<QuantumResult<unit>> =
        task {
            try
                let binaryModels =
                    model.BinaryModels
                    |> Array.map (fun bm ->
                        {
                            SupportVectorIndices = bm.SupportVectorIndices
                            Alphas = bm.Alphas
                            Bias = bm.Bias
                            TrainData = bm.TrainData
                            TrainLabels = bm.TrainLabels
                            FeatureMap = featureMapToSerializable bm.FeatureMap
                            SavedAt = DateTime.UtcNow.ToString("o")
                            Note = None
                        })
                
                let serializable = {
                    BinaryModels = binaryModels
                    ClassLabels = model.ClassLabels
                    NumClasses = model.NumClasses
                    SavedAt = DateTime.UtcNow.ToString("o")
                    Note = note
                }
                
                let options = JsonSerializerOptions()
                options.WriteIndented <- true
                
                let json = JsonSerializer.Serialize(serializable, options)
                do! File.WriteAllTextAsync(filePath, json, cancellationToken)
                
                return Ok ()
            with ex ->
                return Error (QuantumError.ValidationError ("Input", $"Failed to save multi-class SVM model: {ex.Message}"))
        }
    
    [<System.Obsolete("Use saveMultiClassSVMModelAsync for better performance and to avoid blocking threads")>]
    let saveMultiClassSVMModel
        (filePath: string)
        (model: MultiClassSVM.MultiClassModel)
        (note: string option)
        : QuantumResult<unit> =
        saveMultiClassSVMModelAsync filePath model note CancellationToken.None
        |> Async.AwaitTask
        |> Async.RunSynchronously
    
    /// Load multi-class SVM model from JSON file (async, task-based)
    let loadMultiClassSVMModelAsync
        (filePath: string)
        (cancellationToken: CancellationToken)
        : Task<QuantumResult<MultiClassSVM.MultiClassModel>> =
        task {
            try
                if not (File.Exists filePath) then
                    return Error (QuantumError.ValidationError ("Input", $"File not found: {filePath}"))
                else
                    let! json = File.ReadAllTextAsync(filePath, cancellationToken)
                    let serializable = JsonSerializer.Deserialize<SerializableMultiClassSVMModel>(json)
                    
                    // Convert all binary models
                    let binaryModelsResult =
                        serializable.BinaryModels
                        |> Array.map (fun bm ->
                            serializableToFeatureMap bm.FeatureMap
                            |> Result.map (fun featureMap ->
                                let svmModel : QuantumKernelSVM.SVMModel = {
                                    SupportVectorIndices = bm.SupportVectorIndices
                                    Alphas = bm.Alphas
                                    Bias = bm.Bias
                                    TrainData = bm.TrainData
                                    TrainLabels = bm.TrainLabels
                                    FeatureMap = featureMap
                                }
                                svmModel))
                        |> Array.fold (fun state item ->
                            Result.bind (fun models ->
                                Result.map (fun model -> Array.append models [| model |]) item
                            ) state
                        ) (Ok [||])
                
                    return
                        binaryModelsResult
                        |> Result.map (fun binaryModels ->
                            let model : MultiClassSVM.MultiClassModel = {
                                BinaryModels = binaryModels
                                ClassLabels = serializable.ClassLabels
                                NumClasses = serializable.NumClasses
                            }
                            model)
            with ex ->
                return Error (QuantumError.ValidationError ("Input", $"Failed to load multi-class SVM model: {ex.Message}"))
        }
    
    [<System.Obsolete("Use loadMultiClassSVMModelAsync for better performance and to avoid blocking threads")>]
    let loadMultiClassSVMModel
        (filePath: string)
        : QuantumResult<MultiClassSVM.MultiClassModel> =
        loadMultiClassSVMModelAsync filePath CancellationToken.None
        |> Async.AwaitTask
        |> Async.RunSynchronously
    
    /// Print multi-class SVM model information via ILogger
    let printMultiClassSVMModelInfo
        (filePath: string)
        (logger: ILogger option)
        : QuantumResult<unit> =
        
        try
            if not (File.Exists filePath) then
                Error (QuantumError.ValidationError ("Input", $"File not found: {filePath}"))
            else
                let json = File.ReadAllText(filePath)
                let model = JsonSerializer.Deserialize<SerializableMultiClassSVMModel>(json)
                
                logInfo logger "=== Multi-Class SVM Model Information ==="
                logInfo logger (sprintf "File: %s" filePath)
                logInfo logger (sprintf "Saved at: %s" model.SavedAt)
                logInfo logger (sprintf "Number of Classes: %d" model.NumClasses)
                logInfo logger (sprintf "Class Labels: %A" model.ClassLabels)
                logInfo logger (sprintf "Binary Models: %d" model.BinaryModels.Length)
                
                for i in 0 .. model.BinaryModels.Length - 1 do
                    let bm = model.BinaryModels.[i]
                    logInfo logger (sprintf "  Class %d vs Rest:" model.ClassLabels.[i])
                    logInfo logger (sprintf "    Support Vectors: %d" bm.SupportVectorIndices.Length)
                    logInfo logger (sprintf "    Bias: %.6f" bm.Bias)
                    logInfo logger (sprintf "    Feature Map: %s" bm.FeatureMap.Type)
                
                match model.Note with
                | Some note -> logInfo logger (sprintf "Note: %s" note)
                | None -> ()
                logInfo logger "=========================================="
                Ok ()
        with ex ->
            Error (QuantumError.ValidationError ("Input", $"Failed to print multi-class SVM model info: {ex.Message}"))
