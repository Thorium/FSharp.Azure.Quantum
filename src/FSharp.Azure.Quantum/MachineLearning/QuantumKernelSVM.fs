namespace FSharp.Azure.Quantum.MachineLearning

/// Quantum Kernel Support Vector Machine (SVM).
///
/// Implements binary classification using quantum kernels.
/// Uses simplified dual formulation suitable for small datasets.
///
/// Reference: Havlíček et al., "Supervised learning with quantum-enhanced 
/// feature spaces" Nature (2019)

open System
open FSharp.Azure.Quantum.Core
open FSharp.Azure.Quantum.Core.BackendAbstraction
open Microsoft.Extensions.Logging

module QuantumKernelSVM =
    
    // ========================================================================
    // TYPES
    // ========================================================================
    
    /// SVM training configuration
    type SVMConfig = {
        /// Regularization parameter (C)
        /// Higher values = less regularization, fit training data more closely
        C: float
        
        /// Convergence tolerance for optimization
        Tolerance: float
        
        /// Maximum number of optimization iterations
        MaxIterations: int
        
        /// Verbose output
        Verbose: bool
        
        /// Optional logger for structured logging
        Logger: ILogger option
    }
    
    /// Trained SVM model
    type SVMModel = {
        /// Support vector indices (indices into training data)
        SupportVectorIndices: int array
        
        /// Lagrange multipliers (alphas) for support vectors
        Alphas: float array
        
        /// Bias term (b)
        Bias: float
        
        /// Training data (kept for prediction)
        TrainData: float array array
        
        /// Training labels (kept for prediction)
        TrainLabels: int array
        
        /// Feature map used for training
        FeatureMap: FeatureMapType
    }
    
    /// Prediction result
    type Prediction = {
        /// Predicted label (0 or 1)
        Label: int
        
        /// Decision function value (distance from hyperplane)
        DecisionValue: float
    }
    
    // ========================================================================
    // DEFAULT CONFIGURATION
    // ========================================================================
    
    /// Default SVM configuration
    let defaultConfig = {
        C = 1.0
        Tolerance = 1e-3
        MaxIterations = 100
        Verbose = false
        Logger = None
    }
    
    // ========================================================================
    // HELPER FUNCTIONS (Pure)
    // ========================================================================
    
    /// Compute decision function value for sample i
    let private computeDecision (alphas: float array) (y: float array) (kernelMatrix: float[,]) (bias: float) (i: int) : float =
        alphas
        |> Array.mapi (fun j alpha -> alpha * y.[j] * kernelMatrix.[j, i])
        |> Array.sum
        |> (+) bias
    
    /// Find index j that maximizes |E_i - E_j|
    let private findBestPair (alphas: float array) (y: float array) (kernelMatrix: float[,]) (bias: float) (i: int) (E_i: float) : int =
        [| 0 .. alphas.Length - 1 |]
        |> Array.filter ((<>) i)
        |> Array.map (fun k ->
            let E_k = computeDecision alphas y kernelMatrix bias k - y.[k]
            (k, abs (E_i - E_k)))
        |> Array.maxBy snd
        |> fst
    
    /// Clamp value between L and H
    let private clamp L H value =
        if value > H then H
        elif value < L then L
        else value
    
    // ========================================================================
    // SMO STATE AND ITERATION
    // ========================================================================
    
    /// Immutable state for SMO algorithm
    type private SMOState = {
        Alphas: float array
        Bias: float
        Iteration: int
        NumChanged: int
        ExamineAll: bool
    }
    
    /// Try to update alpha pair (i, j) - returns updated state if successful
    let private tryUpdatePair 
        (kernelMatrix: float[,]) 
        (y: float array) 
        (config: SVMConfig) 
        (state: SMOState) 
        (i: int) 
        : SMOState =
        
        let E_i = computeDecision state.Alphas y kernelMatrix state.Bias i - y.[i]
        let r_i = E_i * y.[i]
        
        // Check KKT conditions
        if (r_i < -config.Tolerance && state.Alphas.[i] < config.C) ||
           (r_i > config.Tolerance && state.Alphas.[i] > 0.0) then
            
            // Find best j
            let j = findBestPair state.Alphas y kernelMatrix state.Bias i E_i
            let E_j = computeDecision state.Alphas y kernelMatrix state.Bias j - y.[j]
            
            let alpha_i_old = state.Alphas.[i]
            let alpha_j_old = state.Alphas.[j]
            
            // Compute bounds
            let L, H =
                if y.[i] <> y.[j] then
                    max 0.0 (alpha_j_old - alpha_i_old), 
                    min config.C (config.C + alpha_j_old - alpha_i_old)
                else
                    max 0.0 (alpha_i_old + alpha_j_old - config.C),
                    min config.C (alpha_i_old + alpha_j_old)
            
            if abs (L - H) > 1e-10 then
                // Compute eta (second derivative)
                let eta = 2.0 * kernelMatrix.[i, j] - kernelMatrix.[i, i] - kernelMatrix.[j, j]
                
                if eta < 0.0 then
                    // Update alpha_j
                    let alpha_j_new_unc = alpha_j_old - y.[j] * (E_i - E_j) / eta
                    let alpha_j_new = clamp L H alpha_j_new_unc
                    
                    if abs (alpha_j_new - alpha_j_old) > config.Tolerance then
                        // Create new alphas array with updates
                        let newAlphas = Array.copy state.Alphas
                        newAlphas.[j] <- alpha_j_new
                        newAlphas.[i] <- alpha_i_old + y.[i] * y.[j] * (alpha_j_old - alpha_j_new)
                        
                        // Update bias
                        let b1 = state.Bias - E_i - y.[i] * (newAlphas.[i] - alpha_i_old) * kernelMatrix.[i, i] 
                                  - y.[j] * (newAlphas.[j] - alpha_j_old) * kernelMatrix.[i, j]
                        let b2 = state.Bias - E_j - y.[i] * (newAlphas.[i] - alpha_i_old) * kernelMatrix.[i, j]
                                  - y.[j] * (newAlphas.[j] - alpha_j_old) * kernelMatrix.[j, j]
                        
                        let newBias =
                            if newAlphas.[i] > 0.0 && newAlphas.[i] < config.C then b1
                            elif newAlphas.[j] > 0.0 && newAlphas.[j] < config.C then b2
                            else (b1 + b2) / 2.0
                        
                        { state with 
                            Alphas = newAlphas
                            Bias = newBias
                            NumChanged = state.NumChanged + 1 }
                    else
                        state
                else
                    state
            else
                state
        else
            state
    
    /// Single SMO iteration - examines all indices and updates state
    let private smoIteration
        (kernelMatrix: float[,])
        (y: float array)
        (config: SVMConfig)
        (state: SMOState)
        : SMOState =
        
        let indices = 
            if state.ExamineAll then
                [| 0 .. y.Length - 1 |]
            else
                // Focus on non-bound examples (0 < alpha < C)
                [| for i in 0 .. y.Length - 1 do
                    if state.Alphas.[i] > 0.0 && state.Alphas.[i] < config.C then
                        yield i |]
        
        // Process all indices, accumulating state changes
        let newState =
            indices
            |> Array.fold (fun s i -> tryUpdatePair kernelMatrix y config s i) 
                { state with NumChanged = 0 }
        
        // Update iteration counter and examineAll flag
        let nextExamineAll =
            if state.ExamineAll then false
            elif newState.NumChanged = 0 then true
            else state.ExamineAll
        
        if config.Verbose && (newState.Iteration + 1) % 10 = 0 then
            logInfo config.Logger (sprintf "SMO iteration %d: %d alphas changed" (newState.Iteration + 1) newState.NumChanged)
        
        { newState with 
            Iteration = newState.Iteration + 1
            ExamineAll = nextExamineAll }
    
    /// Tail-recursive SMO training loop
    let rec private smoLoop
        (kernelMatrix: float[,])
        (y: float array)
        (config: SVMConfig)
        (state: SMOState)
        : SMOState =
        
        if (state.NumChanged = 0 && not state.ExamineAll) || 
           state.Iteration >= config.MaxIterations then
            state  // Converged or max iterations reached
        else
            let newState = smoIteration kernelMatrix y config state
            smoLoop kernelMatrix y config newState
    
    // ========================================================================
    // SIMPLIFIED SMO ALGORITHM
    // ========================================================================
    
    /// Simplified Sequential Minimal Optimization (SMO) for SVM training
    ///
    /// This is a simplified version suitable for small datasets.
    /// For large datasets, use a full SMO implementation.
    let private trainSMO
        (kernelMatrix: float[,])
        (labels: int array)
        (config: SVMConfig)
        : QuantumResult<float array * float> =
        
        let n = labels.Length
        
        // Convert labels to {-1, +1}
        let y = labels |> Array.map (fun l -> if l = 1 then 1.0 else -1.0)
        
        // Initialize state
        let initialState = {
            Alphas = Array.zeroCreate n
            Bias = 0.0
            Iteration = 0
            NumChanged = 1  // Start with 1 to enter loop
            ExamineAll = true
        }
        
        // Run SMO optimization
        let finalState = smoLoop kernelMatrix y config initialState
        
        if config.Verbose then
            let numSupportVectors = 
                finalState.Alphas 
                |> Array.filter (fun a -> a > 1e-6) 
                |> Array.length
            logInfo config.Logger (sprintf "Training complete after %d iterations" finalState.Iteration)
            logInfo config.Logger (sprintf "Support vectors: %d / %d" numSupportVectors n)
        
        Ok (finalState.Alphas, finalState.Bias)
    
    // ========================================================================
    // TRAINING
    // ========================================================================
    
    /// Train quantum kernel SVM
    ///
    /// Parameters:
    ///   backend - Quantum backend for kernel computation
    ///   featureMap - Quantum feature map
    ///   trainData - Training feature vectors
    ///   trainLabels - Training labels (0 or 1)
    ///   config - SVM configuration
    ///   shots - Number of shots for quantum kernel evaluation
    ///
    /// Returns:
    ///   Trained SVM model or error message
    let train
        (backend: IQuantumBackend)
        (featureMap: FeatureMapType)
        (trainData: float array array)
        (trainLabels: int array)
        (config: SVMConfig)
        (shots: int)
        : QuantumResult<SVMModel> =
        
        // Validate inputs
        if trainData.Length = 0 then
            Error (QuantumError.Other "Training data cannot be empty")
        elif trainLabels.Length = 0 then
            Error (QuantumError.Other "Training labels cannot be empty")
        elif trainData.Length <> trainLabels.Length then
            Error (QuantumError.ValidationError ("Input", $"Data and labels must have same length: {trainData.Length} vs {trainLabels.Length}"))
        elif config.C <= 0.0 then
            Error (QuantumError.ValidationError ("Input", "Regularization parameter C must be positive"))
        elif shots <= 0 then
            Error (QuantumError.ValidationError ("Input", "Number of shots must be positive"))
        else
            // Check labels are binary (0 or 1)
            let validLabels = trainLabels |> Array.forall (fun l -> l = 0 || l = 1)
            if not validLabels then
                Error (QuantumError.ValidationError ("Input", "Labels must be 0 or 1"))
            else
                if config.Verbose then
                    logInfo config.Logger "Computing quantum kernel matrix..."
                
                // Compute kernel matrix and train SVM
                QuantumKernels.computeKernelMatrix backend featureMap trainData shots
                |> Result.mapError (fun e -> QuantumError.ValidationError ("Input", $"Kernel matrix computation failed: {e}"))
                |> Result.bind (fun kernelMatrix ->
                    if config.Verbose then
                        logInfo config.Logger "Training SVM with SMO algorithm..."
                    
                    // Train SVM using SMO
                    trainSMO kernelMatrix trainLabels config
                    |> Result.map (fun (alphas, bias) ->
                        // Extract support vectors (alpha > threshold)
                        let supportVectorIndices =
                            alphas
                            |> Array.mapi (fun i alpha -> (i, alpha))
                            |> Array.filter (fun (_, alpha) -> alpha > 1e-6)
                            |> Array.map fst
                        
                        let supportVectorAlphas =
                            supportVectorIndices
                            |> Array.map (fun i -> alphas.[i])
                        
                        {
                            SupportVectorIndices = supportVectorIndices
                            Alphas = supportVectorAlphas
                            Bias = bias
                            TrainData = trainData
                            TrainLabels = trainLabels
                            FeatureMap = featureMap
                        }))
    
    // ========================================================================
    // PREDICTION
    // ========================================================================
    
    /// Helper to convert Result array to array Result
    let private traverseResult (results: Result<'a, 'e> array) : Result<'a array, 'e> =
        match results |> Array.tryFind Result.isError with
        | Some (Error e) -> Error e
        | _ ->
            results
            |> Array.map (fun r ->
                match r with
                | Ok v -> v
                | Error _ -> failwith "unreachable")
            |> Ok
    
    /// Predict label for a single sample
    ///
    /// Parameters:
    ///   backend - Quantum backend
    ///   model - Trained SVM model
    ///   sample - Feature vector to classify
    ///   shots - Number of shots for kernel evaluation
    ///
    /// Returns:
    ///   Prediction with label and decision value
    let predict
        (backend: IQuantumBackend)
        (model: SVMModel)
        (sample: float array)
        (shots: int)
        : QuantumResult<Prediction> =
        
        if shots <= 0 then
            Error (QuantumError.ValidationError ("Input", "Number of shots must be positive"))
        else
            // Compute kernels between sample and support vectors (functional style)
            let kernelResults =
                model.SupportVectorIndices
                |> Array.map (fun svIdx ->
                    QuantumKernels.computeKernel backend model.FeatureMap sample model.TrainData.[svIdx] shots
                    |> Result.mapError (fun e -> QuantumError.OperationError ("Kernel computation", $"Kernel computation failed: {e.Message}")))
            
            // Traverse Result array to get array Result
            kernelResults
            |> traverseResult
            |> Result.map (fun kernelValues ->
                // Compute decision function: f(x) = Σ(α_i * y_i * K(x, x_i)) + b
                let decisionValue =
                    model.SupportVectorIndices
                    |> Array.mapi (fun i svIdx ->
                        let y_i = if model.TrainLabels.[svIdx] = 1 then 1.0 else -1.0
                        model.Alphas.[i] * y_i * kernelValues.[i])
                    |> Array.sum
                    |> (+) model.Bias
                
                let label = if decisionValue >= 0.0 then 1 else 0
                
                {
                    Label = label
                    DecisionValue = decisionValue
                })
    
    // ========================================================================
    // EVALUATION
    // ========================================================================
    
    /// Evaluate model on a dataset
    ///
    /// Returns accuracy (fraction of correct predictions)
    let evaluate
        (backend: IQuantumBackend)
        (model: SVMModel)
        (testData: float array array)
        (testLabels: int array)
        (shots: int)
        : QuantumResult<float> =
        
        if testData.Length = 0 then
            Error (QuantumError.Other "Test data cannot be empty")
        elif testData.Length <> testLabels.Length then
            Error (QuantumError.ValidationError ("Input", "Test data and labels must have same length"))
        else
            // Compute predictions for all test samples
            let predictionResults =
                testData
                |> Array.map (fun sample -> predict backend model sample shots)
            
            // Traverse results and compute accuracy
            predictionResults
            |> traverseResult
            |> Result.map (fun predictions ->
                let correctCount =
                    Array.zip predictions testLabels
                    |> Array.filter (fun (pred, label) -> pred.Label = label)
                    |> Array.length
                
                float correctCount / float testData.Length)
