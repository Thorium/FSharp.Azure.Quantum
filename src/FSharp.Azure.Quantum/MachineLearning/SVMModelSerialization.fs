namespace FSharp.Azure.Quantum.MachineLearning

/// SVM Model Serialization
///
/// Provides save/load functionality for trained SVM models (binary and multi-class).
/// Stores model parameters and data as JSON for easy inspection and portability.

open System
open System.IO
open System.Text.Json

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
    let private serializableToFeatureMap (fm: SerializableFeatureMap) : Result<FeatureMapType, string> =
        match fm.Type with
        | "ZZFeatureMap" ->
            match fm.Depth with
            | Some depth -> Ok (FeatureMapType.ZZFeatureMap depth)
            | None -> Error "ZZFeatureMap requires depth"
        | "PauliFeatureMap" ->
            match fm.Depth, fm.Paulis with
            | Some depth, Some paulis -> Ok (FeatureMapType.PauliFeatureMap (paulis, depth))
            | _ -> Error "PauliFeatureMap requires depth and paulis"
        | "AngleEncoding" ->
            Ok FeatureMapType.AngleEncoding
        | "AmplitudeEncoding" ->
            Ok FeatureMapType.AmplitudeEncoding
        | _ -> Error $"Unknown feature map type: {fm.Type}"
    
    // ========================================================================
    // BINARY SVM SERIALIZATION
    // ========================================================================
    
    /// Save binary SVM model to JSON file
    let saveSVMModel
        (filePath: string)
        (model: QuantumKernelSVM.SVMModel)
        (note: string option)
        : Result<unit, string> =
        
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
            File.WriteAllText(filePath, json)
            
            Ok ()
        with ex ->
            Error $"Failed to save SVM model: {ex.Message}"
    
    /// Load binary SVM model from JSON file
    let loadSVMModel
        (filePath: string)
        : Result<QuantumKernelSVM.SVMModel, string> =
        
        try
            if not (File.Exists filePath) then
                Error $"File not found: {filePath}"
            else
                let json = File.ReadAllText(filePath)
                let serializable = JsonSerializer.Deserialize<SerializableSVMModel>(json)
                
                match serializableToFeatureMap serializable.FeatureMap with
                | Error e -> Error e
                | Ok featureMap ->
                    Ok {
                        SupportVectorIndices = serializable.SupportVectorIndices
                        Alphas = serializable.Alphas
                        Bias = serializable.Bias
                        TrainData = serializable.TrainData
                        TrainLabels = serializable.TrainLabels
                        FeatureMap = featureMap
                    }
        with ex ->
            Error $"Failed to load SVM model: {ex.Message}"
    
    /// Print binary SVM model information
    let printSVMModelInfo
        (filePath: string)
        : Result<unit, string> =
        
        try
            if not (File.Exists filePath) then
                Error $"File not found: {filePath}"
            else
                let json = File.ReadAllText(filePath)
                let model = JsonSerializer.Deserialize<SerializableSVMModel>(json)
                
                printfn "=== Binary SVM Model Information ==="
                printfn "File: %s" filePath
                printfn "Saved at: %s" model.SavedAt
                printfn "Support Vectors: %d" model.SupportVectorIndices.Length
                printfn "Training Samples: %d" model.TrainData.Length
                printfn "Features: %d" (if model.TrainData.Length > 0 then model.TrainData.[0].Length else 0)
                printfn "Bias: %.6f" model.Bias
                printfn "Feature Map: %s" model.FeatureMap.Type
                match model.FeatureMap.Depth with
                | Some d -> printfn "  Depth: %d" d
                | None -> ()
                match model.Note with
                | Some note -> printfn "Note: %s" note
                | None -> ()
                printfn "===================================="
                Ok ()
        with ex ->
            Error $"Failed to print SVM model info: {ex.Message}"
    
    // ========================================================================
    // MULTI-CLASS SVM SERIALIZATION
    // ========================================================================
    
    /// Save multi-class SVM model to JSON file
    let saveMultiClassSVMModel
        (filePath: string)
        (model: MultiClassSVM.MultiClassModel)
        (note: string option)
        : Result<unit, string> =
        
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
            File.WriteAllText(filePath, json)
            
            Ok ()
        with ex ->
            Error $"Failed to save multi-class SVM model: {ex.Message}"
    
    /// Load multi-class SVM model from JSON file
    let loadMultiClassSVMModel
        (filePath: string)
        : Result<MultiClassSVM.MultiClassModel, string> =
        
        try
            if not (File.Exists filePath) then
                Error $"File not found: {filePath}"
            else
                let json = File.ReadAllText(filePath)
                let serializable = JsonSerializer.Deserialize<SerializableMultiClassSVMModel>(json)
                
                // Convert all binary models
                let binaryModelsResult =
                    serializable.BinaryModels
                    |> Array.map (fun bm ->
                        match serializableToFeatureMap bm.FeatureMap with
                        | Error e -> Error e
                        | Ok featureMap ->
                            let svmModel : QuantumKernelSVM.SVMModel = {
                                SupportVectorIndices = bm.SupportVectorIndices
                                Alphas = bm.Alphas
                                Bias = bm.Bias
                                TrainData = bm.TrainData
                                TrainLabels = bm.TrainLabels
                                FeatureMap = featureMap
                            }
                            Ok svmModel)
                    |> Array.fold (fun state item ->
                        match state, item with
                        | Ok models, Ok model -> Ok (Array.append models [| model |])
                        | Error e, _ -> Error e
                        | _, Error e -> Error e
                    ) (Ok [||])
                
                match binaryModelsResult with
                | Error e -> Error e
                | Ok binaryModels ->
                    let multiClassModel : MultiClassSVM.MultiClassModel = {
                        BinaryModels = binaryModels
                        ClassLabels = serializable.ClassLabels
                        NumClasses = serializable.NumClasses
                    }
                    Ok multiClassModel
        with ex ->
            Error $"Failed to load multi-class SVM model: {ex.Message}"
    
    /// Print multi-class SVM model information
    let printMultiClassSVMModelInfo
        (filePath: string)
        : Result<unit, string> =
        
        try
            if not (File.Exists filePath) then
                Error $"File not found: {filePath}"
            else
                let json = File.ReadAllText(filePath)
                let model = JsonSerializer.Deserialize<SerializableMultiClassSVMModel>(json)
                
                printfn "=== Multi-Class SVM Model Information ==="
                printfn "File: %s" filePath
                printfn "Saved at: %s" model.SavedAt
                printfn "Number of Classes: %d" model.NumClasses
                printfn "Class Labels: %A" model.ClassLabels
                printfn "Binary Models: %d" model.BinaryModels.Length
                
                for i in 0 .. model.BinaryModels.Length - 1 do
                    let bm = model.BinaryModels.[i]
                    printfn "  Class %d vs Rest:" model.ClassLabels.[i]
                    printfn "    Support Vectors: %d" bm.SupportVectorIndices.Length
                    printfn "    Bias: %.6f" bm.Bias
                    printfn "    Feature Map: %s" bm.FeatureMap.Type
                
                match model.Note with
                | Some note -> printfn "Note: %s" note
                | None -> ()
                printfn "=========================================="
                Ok ()
        with ex ->
            Error $"Failed to print multi-class SVM model info: {ex.Message}"
