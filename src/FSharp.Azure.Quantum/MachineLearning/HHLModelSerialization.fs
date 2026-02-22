namespace FSharp.Azure.Quantum.MachineLearning

open FSharp.Azure.Quantum.Core

/// HHL Model Serialization.
///
/// Provides save/load functionality for trained HHL regression models.
/// Stores weights and metadata as JSON for easy inspection and portability.

open System
open System.IO
open System.Text.Json
open System.Threading
open System.Threading.Tasks
open Microsoft.Extensions.Logging

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
        : QuantumResult<unit> =
        
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
            Error (QuantumError.ValidationError ("Input", $"Failed to save HHL model: {ex.Message}"))
    
    /// Save HHL regression model to JSON file asynchronously
    let saveHHLModelAsync
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
        (cancellationToken: CancellationToken)
        : Task<QuantumResult<unit>> =
        task {
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
                do! File.WriteAllTextAsync(filePath, json, cancellationToken)
                
                return Ok ()
            with ex ->
                return Error (QuantumError.ValidationError ("Input", $"Failed to save HHL model: {ex.Message}"))
        }
    
    /// Save HHL regression result with metadata (async, task-based)
    ///
    /// Convenience function that takes QuantumRegressionHHL.RegressionResult directly
    let saveHHLRegressionResultAsync
        (filePath: string)
        (result: QuantumRegressionHHL.RegressionResult)
        (note: string option)
        (cancellationToken: CancellationToken)
        : Task<QuantumResult<unit>> =
        
        saveHHLModelAsync
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
            cancellationToken
    
    /// Save HHL regression result with metadata
    ///
    /// Convenience function that takes QuantumRegressionHHL.RegressionResult directly
    [<System.Obsolete("Use saveHHLRegressionResultAsync for better performance and to avoid blocking threads")>]
    let saveHHLRegressionResult
        (filePath: string)
        (result: QuantumRegressionHHL.RegressionResult)
        (note: string option)
        : QuantumResult<unit> =
        
        saveHHLModelAsync
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
            CancellationToken.None
        |> Async.AwaitTask
        |> Async.RunSynchronously
    
    /// Load HHL regression model from JSON file
    ///
    /// Returns: Serializable HHL model with all metadata
    let loadHHLModel
        (filePath: string)
        : QuantumResult<SerializableHHLModel> =
        
        try
            if not (File.Exists filePath) then
                Error (QuantumError.ValidationError ("Input", $"File not found: {filePath}"))
            else
                let json = File.ReadAllText(filePath)
                let model = JsonSerializer.Deserialize<SerializableHHLModel>(json)
                Ok model
        with ex ->
            Error (QuantumError.ValidationError ("Input", $"Failed to load HHL model: {ex.Message}"))
    
    /// Load HHL regression model from JSON file asynchronously
    let loadHHLModelAsync
        (filePath: string)
        (cancellationToken: CancellationToken)
        : Task<QuantumResult<SerializableHHLModel>> =
        task {
            try
                if not (File.Exists filePath) then
                    return Error (QuantumError.ValidationError ("Input", $"File not found: {filePath}"))
                else
                    let! json = File.ReadAllTextAsync(filePath, cancellationToken)
                    let model = JsonSerializer.Deserialize<SerializableHHLModel>(json)
                    return Ok model
            with ex ->
                return Error (QuantumError.ValidationError ("Input", $"Failed to load HHL model: {ex.Message}"))
        }
    
    /// Load HHL model and reconstruct RegressionResult
    ///
    /// Convenience function for full deserialization
    let loadHHLRegressionResult
        (filePath: string)
        : QuantumResult<QuantumRegressionHHL.RegressionResult> =
        
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
    
    /// Load HHL model and reconstruct RegressionResult (async, task-based)
    ///
    /// Convenience function for full deserialization
    let loadHHLRegressionResultAsync
        (filePath: string)
        (cancellationToken: CancellationToken)
        : Task<QuantumResult<QuantumRegressionHHL.RegressionResult>> =
        task {
            let! result = loadHHLModelAsync filePath cancellationToken
            return
                result
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
                    } : QuantumRegressionHHL.RegressionResult)
        }
    
    /// Print HHL model information via ILogger
    let printHHLModelInfo
        (filePath: string)
        (logger: ILogger option)
        : QuantumResult<unit> =
        
        loadHHLModel filePath
        |> Result.map (fun model ->
            logInfo logger "=== HHL Regression Model Information ==="
            logInfo logger (sprintf "File: %s" filePath)
            logInfo logger (sprintf "Saved at: %s" model.SavedAt)
            logInfo logger (sprintf "Features: %d" model.NumFeatures)
            logInfo logger (sprintf "Samples: %d" model.NumSamples)
            logInfo logger (sprintf "Weights: %d (intercept=%b)" model.Weights.Length model.HasIntercept)
            logInfo logger (sprintf "R2 Score: %.6f" model.RSquared)
            logInfo logger (sprintf "MSE: %.6f" model.MSE)
            logInfo logger (sprintf "Success Probability: %.6f" model.SuccessProbability)
            match model.ConditionNumber with
            | Some cn -> logInfo logger (sprintf "Condition Number: %.6f" cn)
            | None -> logInfo logger "Condition Number: N/A"
            match model.Note with
            | Some note -> logInfo logger (sprintf "Note: %s" note)
            | None -> ()
            logInfo logger "========================================")
