namespace FSharp.Azure.Quantum.MachineLearning

/// HHL Model Serialization
///
/// Provides save/load functionality for trained HHL regression models.
/// Stores weights and metadata as JSON for easy inspection and portability.

open System
open System.IO
open System.Text.Json

module HHLModelSerialization =
    
    // ========================================================================
    // TYPES
    // ========================================================================
    
    /// Simple serializable HHL regression model (JSON-friendly)
    type SerializableHHLModel = {
        /// Learned weights (including intercept if fitted)
        Weights: float array
        
        /// R² score on training data
        RSquared: float
        
        /// Mean Squared Error on training data
        MSE: float
        
        /// HHL success probability
        SuccessProbability: float
        
        /// Number of features (excluding intercept)
        NumFeatures: int
        
        /// Number of samples used for training
        NumSamples: int
        
        /// Whether intercept was included
        HasIntercept: bool
        
        /// Condition number of Gram matrix (if available)
        ConditionNumber: float option
        
        /// Optional metadata
        SavedAt: string
        Note: string option
    }
    
    // ========================================================================
    // HHL SERIALIZATION
    // ========================================================================
    
    /// Save HHL regression model to JSON file
    let saveHHLModel
        (filePath: string)
        (weights: float array)
        (rSquared: float)
        (mse: float)
        (successProbability: float)
        (numFeatures: int)
        (numSamples: int)
        (hasIntercept: bool)
        (conditionNumber: float option)
        (note: string option)
        : Result<unit, string> =
        
        try
            let model = {
                Weights = weights
                RSquared = rSquared
                MSE = mse
                SuccessProbability = successProbability
                NumFeatures = numFeatures
                NumSamples = numSamples
                HasIntercept = hasIntercept
                ConditionNumber = conditionNumber
                SavedAt = DateTime.UtcNow.ToString("o")
                Note = note
            }
            
            let options = JsonSerializerOptions()
            options.WriteIndented <- true
            
            let json = JsonSerializer.Serialize(model, options)
            File.WriteAllText(filePath, json)
            
            Ok ()
        with ex ->
            Error $"Failed to save HHL model: {ex.Message}"
    
    /// Save HHL regression result with metadata
    ///
    /// Convenience function that takes QuantumRegressionHHL.RegressionResult directly
    let saveHHLRegressionResult
        (filePath: string)
        (result: QuantumRegressionHHL.RegressionResult)
        (note: string option)
        : Result<unit, string> =
        
        saveHHLModel
            filePath
            result.Weights
            result.RSquared
            result.MSE
            result.SuccessProbability
            result.NumFeatures
            result.NumSamples
            result.HasIntercept
            result.ConditionNumber
            note
    
    /// Load HHL regression model from JSON file
    ///
    /// Returns: Serializable HHL model with all metadata
    let loadHHLModel
        (filePath: string)
        : Result<SerializableHHLModel, string> =
        
        try
            if not (File.Exists filePath) then
                Error $"File not found: {filePath}"
            else
                let json = File.ReadAllText(filePath)
                let model = JsonSerializer.Deserialize<SerializableHHLModel>(json)
                Ok model
        with ex ->
            Error $"Failed to load HHL model: {ex.Message}"
    
    /// Load HHL model and reconstruct RegressionResult
    ///
    /// Convenience function for full deserialization
    let loadHHLRegressionResult
        (filePath: string)
        : Result<QuantumRegressionHHL.RegressionResult, string> =
        
        loadHHLModel filePath
        |> Result.map (fun model ->
            {
                Weights = model.Weights
                RSquared = model.RSquared
                MSE = model.MSE
                SuccessProbability = model.SuccessProbability
                NumFeatures = model.NumFeatures
                NumSamples = model.NumSamples
                HasIntercept = model.HasIntercept
                ConditionNumber = model.ConditionNumber
            })
    
    /// Print HHL model information to console
    let printHHLModelInfo
        (filePath: string)
        : Result<unit, string> =
        
        match loadHHLModel filePath with
        | Error e -> Error e
        | Ok model ->
            printfn "=== HHL Regression Model Information ==="
            printfn "File: %s" filePath
            printfn "Saved at: %s" model.SavedAt
            printfn "Features: %d" model.NumFeatures
            printfn "Samples: %d" model.NumSamples
            printfn "Weights: %d (intercept=%b)" model.Weights.Length model.HasIntercept
            printfn "R² Score: %.6f" model.RSquared
            printfn "MSE: %.6f" model.MSE
            printfn "Success Probability: %.6f" model.SuccessProbability
            match model.ConditionNumber with
            | Some cn -> printfn "Condition Number: %.6f" cn
            | None -> printfn "Condition Number: N/A"
            match model.Note with
            | Some note -> printfn "Note: %s" note
            | None -> ()
            printfn "========================================"
            Ok ()
