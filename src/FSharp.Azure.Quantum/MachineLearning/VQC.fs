namespace FSharp.Azure.Quantum.MachineLearning

/// Variational Quantum Classifier (VQC).
///
/// Binary classification using quantum circuits with parameterized gates.
/// Implements training loop with parameter shift rule for gradient computation.
/// Supports both SGD and Adam optimizers.

open System
open FSharp.Azure.Quantum.Backends
open FSharp.Azure.Quantum.CircuitBuilder
open FSharp.Azure.Quantum.Core.BackendAbstraction
open FSharp.Azure.Quantum.Core.CircuitAbstraction
open FSharp.Azure.Quantum.Core

module VQC =
    
    // ========================================================================
    // TYPES
    // ========================================================================
    
    /// Optimizer choice for training
    type Optimizer =
        | SGD  // Stochastic Gradient Descent
        | Adam of AdamOptimizer.AdamConfig  // Adam optimizer with configuration
    
    /// Training configuration
    type TrainingConfig = {
        /// Learning rate for gradient descent (used by SGD and Adam)
        LearningRate: float
        
        /// Maximum number of training epochs
        MaxEpochs: int
        
        /// Convergence threshold (stop if loss change < threshold)
        ConvergenceThreshold: float
        
        /// Number of measurement shots per circuit evaluation
        Shots: int
        
        /// Verbose logging
        Verbose: bool
        
        /// Optimizer to use (SGD or Adam)
        Optimizer: Optimizer
        
        /// Progress reporter for long-running training
        ProgressReporter: Progress.IProgressReporter option
    }
    
    /// Training result
    type TrainingResult = {
        /// Trained parameters
        Parameters: float array
        
        /// Training loss history
        LossHistory: float list
        
        /// Number of epochs completed
        Epochs: int
        
        /// Final training accuracy
        TrainAccuracy: float
        
        /// Whether training converged
        Converged: bool
    }
    
    /// Prediction result
    type Prediction = {
        /// Predicted label (0 or 1)
        Label: int
        
        /// Prediction probability [0, 1]
        Probability: float
    }
    
    // ========================================================================
    // FORWARD PASS
    // ========================================================================
    
    /// Execute forward pass: measure output qubit
    let private forwardPass 
        (backend: IQuantumBackend) 
        (circuit: Circuit) 
        (shots: int) 
        : QuantumResult<float> =
        
        quantumResult {
            // Wrap circuit for backend execution
            let wrappedCircuit = CircuitWrapper(circuit) :> ICircuit
            
            // Execute circuit to get state
            let! state = backend.ExecuteToState wrappedCircuit
            
            // Perform measurements on quantum state
            let measurements = QuantumState.measure state shots
            
            // Count |1⟩ measurements on first qubit
            let onesCount = 
                measurements
                |> Array.filter (fun shot -> shot.[0] = 1)
                |> Array.length
                |> float
            
            let totalShots = float shots
            let probability = onesCount / totalShots
            
            return probability
        }
    
    /// Build VQC circuit for a single sample
    let private buildVQCCircuit
        (featureMap: FeatureMapType)
        (variationalForm: VariationalForm)
        (features: float array)
        (parameters: float array)
        : QuantumResult<Circuit> =
        
        let numQubits = features.Length
        
        quantumResult {
            // Build feature map circuit
            let! fmCircuit = 
                FeatureMap.buildFeatureMap featureMap features
                |> Result.mapError (fun e -> QuantumError.ValidationError ("Input", $"Feature map error: {e}"))
            
            // Build variational form circuit
            let! vfCircuit = 
                VariationalForms.buildVariationalForm variationalForm parameters numQubits
                |> Result.mapError (fun e -> QuantumError.ValidationError ("Input", $"Variational form error: {e}"))
            
            // Compose circuits
            let! composedCircuit = 
                VariationalForms.composeWithFeatureMap fmCircuit vfCircuit
                |> Result.mapError (fun e -> QuantumError.ValidationError ("Input", $"Composition error: {e}"))
            
            // Backend will automatically measure all qubits
            return composedCircuit
        }
    
    // ========================================================================
    // LOSS FUNCTIONS
    // ========================================================================
    
    /// Binary cross-entropy loss
    let private binaryCrossEntropy (predicted: float) (actual: int) : float =
        let epsilon = 1e-7  // Avoid log(0)
        let p = max epsilon (min (1.0 - epsilon) predicted)
        
        if actual = 1 then
            -log p
        else
            -log (1.0 - p)
    
    /// Compute average loss over dataset (parallelized for performance)
    let private computeLoss
        (backend: IQuantumBackend)
        (featureMap: FeatureMapType)
        (variationalForm: VariationalForm)
        (parameters: float array)
        (features: float array array)
        (labels: int array)
        (shots: int)
        : QuantumResult<float> =
        
        let computeSampleLoss i =
            buildVQCCircuit featureMap variationalForm features.[i] parameters
            |> Result.bind (fun circuit -> forwardPass backend circuit shots)
            |> Result.map (fun prediction -> binaryCrossEntropy prediction labels.[i])
        
        // 🚀 PARALLELIZED: Compute loss for all samples in parallel
        // This can provide N× speedup where N = number of samples
        let results = 
            features 
            |> Array.mapi (fun i _ -> async { return computeSampleLoss i })
            |> Async.Parallel
            |> Async.RunSynchronously
        
        // Check if any failed
        match results |> Array.tryFind Result.isError with
        | Some (Error e) -> Error e
        | _ ->
            let losses = results |> Array.choose (function Ok v -> Some v | _ -> None)
            Ok (Array.average losses)
    
    // ========================================================================
    // GRADIENT COMPUTATION (Parameter Shift Rule)
    // ========================================================================
    
    /// Compute gradient using parameter shift rule (parallelized for massive speedup)
    /// 
    /// For a parameter θ_i, the gradient is:
    /// ∂L/∂θ_i = (L(θ + π/2 e_i) - L(θ - π/2 e_i)) / 2
    /// 
    /// 🚀 PERFORMANCE: Gradients for different parameters are computed in parallel
    /// This can provide 10-100× speedup depending on parameter count!
    let private computeGradient
        (backend: IQuantumBackend)
        (featureMap: FeatureMapType)
        (variationalForm: VariationalForm)
        (parameters: float array)
        (features: float array array)
        (labels: int array)
        (shots: int)
        : QuantumResult<float array> =
        
        let shift = Math.PI / 2.0
        
        let computeParamGradient i =
            async {
                // Shift parameter forward
                let paramsPlus = Array.copy parameters
                paramsPlus.[i] <- paramsPlus.[i] + shift
                
                // Shift parameter backward
                let paramsMinus = Array.copy parameters
                paramsMinus.[i] <- paramsMinus.[i] - shift
                
                // 🚀 PARALLELIZED: Compute forward and backward shifts in parallel too!
                let! results = 
                    Async.Parallel [|
                        async { return computeLoss backend featureMap variationalForm paramsPlus features labels shots }
                        async { return computeLoss backend featureMap variationalForm paramsMinus features labels shots }
                    |]
                
                let lossPlus = results.[0]
                let lossMinus = results.[1]
                
                // Combine results
                return 
                    match lossPlus, lossMinus with
                    | Ok lp, Ok lm -> Ok ((lp - lm) / 2.0)
                    | Error e, _ -> Error e
                    | _, Error e -> Error e
            }
        
        // 🚀 PARALLELIZED: Compute gradient for all parameters in parallel
        // This is a HUGE win - can be 10-100× faster depending on parameter count!
        let results = 
            parameters 
            |> Array.mapi (fun i _ -> computeParamGradient i)
            |> Async.Parallel
            |> Async.RunSynchronously
        
        // Check if any failed
        match results |> Array.tryFind Result.isError with
        | Some (Error e) -> Error (QuantumError.ValidationError ("Input", $"Gradient computation failed: {e}"))
        | _ ->
            let gradients = results |> Array.choose (function Ok v -> Some v | _ -> None)
            Ok gradients
    
    // ========================================================================
    // TRAINING LOOP
    // ========================================================================
    
    /// Training state for recursive loop
    type private TrainingState = {
        Parameters: float array
        LossHistory: float list
        Epoch: int
        Converged: bool
        AdamState: AdamOptimizer.AdamState option
    }
    
    /// Train VQC model using gradient descent
    let rec train
        (backend: IQuantumBackend)
        (featureMap: FeatureMapType)
        (variationalForm: VariationalForm)
        (initialParameters: float array)
        (trainFeatures: float array array)
        (trainLabels: int array)
        (config: TrainingConfig)
        : QuantumResult<TrainingResult> =
        
        // Validate inputs
        if trainFeatures.Length <> trainLabels.Length then
            Error (QuantumError.ValidationError ("Input", "Features and labels must have same length"))
        elif trainFeatures.Length = 0 then
            Error (QuantumError.Other "Training set cannot be empty")
        else
            // Initialize optimizer state for Adam
            let initialAdamState = 
                match config.Optimizer with
                | Adam _ -> Some (AdamOptimizer.createState initialParameters.Length)
                | SGD -> None
            
            if config.Verbose then
                let optimizerName = match config.Optimizer with | SGD -> "SGD" | Adam _ -> "Adam"
                printfn "Starting VQC training..."
                printfn "  Features: %d samples" trainFeatures.Length
                if trainFeatures.Length > 0 then
                    printfn "            %d dimensions" trainFeatures.[0].Length
                printfn "  Parameters: %d" initialParameters.Length
                printfn "  Optimizer: %s" optimizerName
                printfn "  Learning rate: %.4f" config.LearningRate
                printfn "  Max epochs: %d" config.MaxEpochs
                printfn ""
            
            // Report progress: Training started
            config.ProgressReporter
            |> Option.iter (fun reporter ->
                reporter.Report(Progress.PhaseChanged("VQC Training", Some $"Starting with {config.MaxEpochs} max epochs...")))
            
            // Recursive training loop
            let rec trainLoop (state: TrainingState) : QuantumResult<TrainingState> =
                if state.Epoch >= config.MaxEpochs || state.Converged then
                    Ok state
                else
                    quantumResult {
                        // Compute current loss
                        let! loss = 
                            computeLoss backend featureMap variationalForm state.Parameters trainFeatures trainLabels config.Shots
                            |> Result.mapError (fun e -> QuantumError.ValidationError ("Input", $"Loss computation failed at epoch {state.Epoch}: {e}"))
                        
                        let newLossHistory = loss :: state.LossHistory
                        
                        // Report progress
                        config.ProgressReporter
                        |> Option.iter (fun reporter ->
                            reporter.Report(Progress.IterationUpdate(state.Epoch, config.MaxEpochs, Some loss)))
                        
                        if config.Verbose then
                            printfn "Epoch %3d: Loss = %.6f" state.Epoch loss
                        
                        // Check convergence
                        let converged =
                            if newLossHistory.Length >= 2 then
                                let prevLoss = newLossHistory.[1]
                                let lossChange = abs (prevLoss - loss)
                                
                                if lossChange < config.ConvergenceThreshold then
                                    if config.Verbose then
                                        printfn "  Converged! (loss change: %.6f < %.6f)" lossChange config.ConvergenceThreshold
                                    true
                                else
                                    false
                            else
                                false
                        
                        if converged then
                            return! trainLoop { state with LossHistory = newLossHistory; Converged = true }
                        else
                            // Compute gradients
                            let! gradient = 
                                computeGradient backend featureMap variationalForm state.Parameters trainFeatures trainLabels config.Shots
                                |> Result.mapError (fun e -> QuantumError.ValidationError ("Input", $"Gradient computation failed at epoch {state.Epoch}: {e}"))
                            
                            // Update parameters using selected optimizer
                            match config.Optimizer, state.AdamState with
                            | SGD, _ ->
                                // Simple gradient descent
                                let newParams = 
                                    Array.map2 (fun p g -> p - config.LearningRate * g) state.Parameters gradient
                                
                                return! trainLoop { 
                                    state with 
                                        Parameters = newParams
                                        LossHistory = newLossHistory
                                        Epoch = state.Epoch + 1 
                                }
                            
                            | Adam adamConfig, Some adamState ->
                                // Adam optimizer
                                let! (newParams, newAdamState) = 
                                    AdamOptimizer.update adamConfig adamState state.Parameters gradient
                                    |> Result.mapError (fun e -> QuantumError.ValidationError ("Input", $"Adam optimizer failed at epoch {state.Epoch}: {e}"))
                                
                                return! trainLoop {
                                    state with
                                        Parameters = newParams
                                        LossHistory = newLossHistory
                                        Epoch = state.Epoch + 1
                                        AdamState = Some newAdamState
                                }
                            
                            | Adam _, None ->
                                return! Error (QuantumError.Other "Adam optimizer state not initialized")
                    }
            
            // Start training
            let initialState = {
                Parameters = Array.copy initialParameters
                LossHistory = []
                Epoch = 0
                Converged = false
                AdamState = initialAdamState
            }
            
            quantumResult {
                let! finalState = trainLoop initialState
                
                // Compute final training accuracy
                let! accuracy = 
                    evaluate backend featureMap variationalForm finalState.Parameters trainFeatures trainLabels config.Shots
                    |> Result.mapError (fun e -> QuantumError.ValidationError ("Input", $"Final evaluation failed: {e.Message}"))
                
                if config.Verbose then
                    printfn ""
                    printfn "Training complete!"
                    printfn "  Epochs: %d" finalState.Epoch
                    // LossHistory should never be empty here (training loop adds losses), but safe access
                    printfn "  Final loss: %.6f" (List.tryHead finalState.LossHistory |> Option.defaultValue 0.0)
                    printfn "  Train accuracy: %.2f%%" (accuracy * 100.0)
                    printfn "  Converged: %b" finalState.Converged
                
                return {
                    Parameters = finalState.Parameters
                    LossHistory = List.rev finalState.LossHistory
                    Epochs = finalState.Epoch
                    TrainAccuracy = accuracy
                    Converged = finalState.Converged
                }
            }
    
    // ========================================================================
    // PREDICTION & EVALUATION
    // ========================================================================
    
    /// Predict label for a single sample
    and predict
        (backend: IQuantumBackend)
        (featureMap: FeatureMapType)
        (variationalForm: VariationalForm)
        (parameters: float array)
        (features: float array)
        (shots: int)
        : QuantumResult<Prediction> =
        
        quantumResult {
            let! circuit = buildVQCCircuit featureMap variationalForm features parameters
            let! probability = forwardPass backend circuit shots
            
            let label = if probability >= 0.5 then 1 else 0
            
            return {
                Label = label
                Probability = probability
            }
        }
    
    /// Evaluate model accuracy on dataset
    and evaluate
        (backend: IQuantumBackend)
        (featureMap: FeatureMapType)
        (variationalForm: VariationalForm)
        (parameters: float array)
        (features: float array array)
        (labels: int array)
        (shots: int)
        : QuantumResult<float> =
        
        if features.Length <> labels.Length then
            Error (QuantumError.ValidationError ("Input", "Features and labels must have same length"))
        elif features.Length = 0 then
            Error (QuantumError.Other "Dataset cannot be empty")
        else
            let predictSample i =
                predict backend featureMap variationalForm parameters features.[i] shots
                |> Result.map (fun pred -> if pred.Label = labels.[i] then 1 else 0)
            
            let results = features |> Array.mapi (fun i _ -> predictSample i)
            
            match results |> Array.tryFind Result.isError with
            | Some (Error e) -> Error e
            | _ ->
                let correctCounts = results |> Array.choose (function Ok v -> Some v | _ -> None)
                let accuracy = float (Array.sum correctCounts) / float features.Length
                Ok accuracy
    
    // ========================================================================
    // HELPER FUNCTIONS
    // ========================================================================
    
    /// Create default training configuration (uses SGD optimizer)
    let defaultConfig = {
        LearningRate = 0.1
        MaxEpochs = 50
        ConvergenceThreshold = 1e-4
        Shots = 1024
        Verbose = true
        Optimizer = SGD
        ProgressReporter = None
    }
    
    /// Create training configuration with Adam optimizer
    let defaultConfigWithAdam = {
        LearningRate = 0.001  // Adam typically uses smaller learning rate
        MaxEpochs = 50
        ConvergenceThreshold = 1e-4
        Shots = 1024
        Verbose = true
        ProgressReporter = None
        Optimizer = Adam AdamOptimizer.defaultConfig
    }
    
    /// Create custom Adam configuration
    let createAdamConfig learningRate beta1 beta2 =
        match AdamOptimizer.createConfig learningRate beta1 beta2 1e-8 with
        | Ok adamCfg -> 
            Ok { defaultConfig with 
                    LearningRate = learningRate
                    Optimizer = Adam adamCfg }
        | Error e -> Error e
    
    /// Confusion matrix for binary classification
    type ConfusionMatrix = {
        TruePositives: int
        TrueNegatives: int
        FalsePositives: int
        FalseNegatives: int
    }
    
    /// Compute confusion matrix
    let confusionMatrix
        (backend: IQuantumBackend)
        (featureMap: FeatureMapType)
        (variationalForm: VariationalForm)
        (parameters: float array)
        (features: float array array)
        (labels: int array)
        (shots: int)
        : QuantumResult<ConfusionMatrix> =
        
        if features.Length <> labels.Length then
            Error (QuantumError.ValidationError ("Input", "Features and labels must have same length"))
        else
            let categorizePrediction i =
                predict backend featureMap variationalForm parameters features.[i] shots
                |> Result.map (fun pred ->
                    match (pred.Label, labels.[i]) with
                    | (1, 1) -> (1, 0, 0, 0)  // TP
                    | (0, 0) -> (0, 1, 0, 0)  // TN
                    | (1, 0) -> (0, 0, 1, 0)  // FP
                    | (0, 1) -> (0, 0, 0, 1)  // FN
                    | _ -> (0, 0, 0, 0))
            
            let results = features |> Array.mapi (fun i _ -> categorizePrediction i)
            
            match results |> Array.tryFind Result.isError with
            | Some (Error e) -> Error e
            | _ ->
                let categories = results |> Array.choose (function Ok v -> Some v | _ -> None)
                let (tp, tn, fp, fn) = 
                    categories
                    |> Array.fold (fun (tp, tn, fp, fn) (dtp, dtn, dfp, dfn) ->
                        (tp + dtp, tn + dtn, fp + dfp, fn + dfn)) (0, 0, 0, 0)
                
                Ok {
                    TruePositives = tp
                    TrueNegatives = tn
                    FalsePositives = fp
                    FalseNegatives = fn
                }
    
    /// Compute precision from confusion matrix
    let precision (cm: ConfusionMatrix) : float =
        let denominator = cm.TruePositives + cm.FalsePositives
        if denominator = 0 then 0.0
        else float cm.TruePositives / float denominator
    
    /// Compute recall from confusion matrix
    let recall (cm: ConfusionMatrix) : float =
        let denominator = cm.TruePositives + cm.FalseNegatives
        if denominator = 0 then 0.0
        else float cm.TruePositives / float denominator
    
    /// Compute F1 score from confusion matrix
    let f1Score (cm: ConfusionMatrix) : float =
        let p = precision cm
        let r = recall cm
        let denominator = p + r
        if denominator = 0.0 then 0.0
        else 2.0 * p * r / denominator
    
    // ========================================================================
    // REGRESSION SUPPORT
    // ========================================================================
    
    /// Regression training result
    type RegressionTrainingResult = {
        /// Trained parameters
        Parameters: float array
        
        /// Training loss (MSE) history
        LossHistory: float list
        
        /// Number of epochs completed
        Epochs: int
        
        /// Final Mean Squared Error on training data
        TrainMSE: float
        
        /// Final R² score on training data
        TrainRSquared: float
        
        /// Whether training converged
        Converged: bool
        
        /// Value range used for scaling [min, max]
        ValueRange: float * float
    }
    
    /// Regression prediction result
    type RegressionPrediction = {
        /// Predicted continuous value
        Value: float
    }
    
    /// Predict continuous value for a single sample (regression)
    let predictRegression
        (backend: IQuantumBackend)
        (featureMap: FeatureMapType)
        (variationalForm: VariationalForm)
        (parameters: float array)
        (features: float array)
        (shots: int)
        (valueRange: float * float)
        : QuantumResult<RegressionPrediction> =
        
        match buildVQCCircuit featureMap variationalForm features parameters with
        | Error e -> Error e
        | Ok circuit ->
            match forwardPass backend circuit shots with
            | Error e -> Error e
            | Ok expectation ->
                // Scale expectation [0, 1] to target range [min, max]
                let (minVal, maxVal) = valueRange
                let value = minVal + expectation * (maxVal - minVal)
                
                Ok { Value = value }
    
    /// Compute Mean Squared Error loss for regression
    let private computeRegressionLoss
        (backend: IQuantumBackend)
        (featureMap: FeatureMapType)
        (variationalForm: VariationalForm)
        (parameters: float array)
        (trainFeatures: float array array)
        (trainTargets: float array)
        (shots: int)
        (valueRange: float * float)
        : QuantumResult<float> =
        
        // Compute squared errors for each sample
        let results =
            Array.zip trainFeatures trainTargets
            |> Array.map (fun (features, target) ->
                match predictRegression backend featureMap variationalForm parameters features shots valueRange with
                | Error e -> Error e
                | Ok prediction ->
                    let error = prediction.Value - target
                    Ok (error * error))
        
        // Check if any predictions failed
        match results |> Array.tryFind Result.isError with
        | Some (Error e) -> Error (QuantumError.ValidationError ("Input", $"Loss computation failed: {e}"))
        | _ ->
            let squaredErrors = 
                results 
                |> Array.choose (function Ok v -> Some v | _ -> None)
            
            Ok (Array.average squaredErrors)
    
    /// Compute gradient for regression using parameter shift rule
    let private computeRegressionGradient
        (backend: IQuantumBackend)
        (featureMap: FeatureMapType)
        (variationalForm: VariationalForm)
        (parameters: float array)
        (trainFeatures: float array array)
        (trainTargets: float array)
        (shots: int)
        (valueRange: float * float)
        : QuantumResult<float array> =
        
        let shift = Math.PI / 2.0
        
        // Compute gradient for each parameter using parameter shift rule
        let computeParamGradient i =
            // Shift parameter forward
            let paramsPlus = Array.copy parameters
            paramsPlus.[i] <- paramsPlus.[i] + shift
            
            // Shift parameter backward
            let paramsMinus = Array.copy parameters
            paramsMinus.[i] <- paramsMinus.[i] - shift
            
            // Compute losses with shifted parameters
            match computeRegressionLoss backend featureMap variationalForm paramsPlus trainFeatures trainTargets shots valueRange,
                  computeRegressionLoss backend featureMap variationalForm paramsMinus trainFeatures trainTargets shots valueRange with
            | Ok lossPlus, Ok lossMinus ->
                Ok ((lossPlus - lossMinus) / 2.0)
            | Error e, _ | _, Error e ->
                Error (QuantumError.ValidationError ("Input", $"Gradient computation failed for parameter {i}: {e}"))
        
        // Compute gradient for all parameters
        let results = 
            parameters 
            |> Array.mapi (fun i _ -> computeParamGradient i)
        
        // Check if any failed
        match results |> Array.tryFind Result.isError with
        | Some (Error e) -> Error e
        | _ ->
            let gradients = 
                results 
                |> Array.choose (function Ok v -> Some v | _ -> None)
            Ok gradients
    
    /// Calculate R² score for regression
    let private calculateRSquared (yTrue: float array) (yPred: float array) : float =
        let mean = yTrue |> Array.average
        let ssTot = yTrue |> Array.sumBy (fun y -> (y - mean) ** 2.0)
        let ssRes = Array.zip yTrue yPred |> Array.sumBy (fun (yt, yp) -> (yt - yp) ** 2.0)
        
        if ssTot = 0.0 then 1.0
        else 1.0 - (ssRes / ssTot)
    
    /// Train VQC model for regression using gradient descent
    let trainRegression
        (backend: IQuantumBackend)
        (featureMap: FeatureMapType)
        (variationalForm: VariationalForm)
        (initialParameters: float array)
        (trainFeatures: float array array)
        (trainTargets: float array)
        (config: TrainingConfig)
        : QuantumResult<RegressionTrainingResult> =
        
        // Validate inputs
        if trainFeatures.Length <> trainTargets.Length then
            Error (QuantumError.ValidationError ("Input", "Features and targets must have same length"))
        elif trainFeatures.Length = 0 then
            Error (QuantumError.Other "Training set cannot be empty")
        else
            // Determine value range from training targets
            let minTarget = trainTargets |> Array.min
            let maxTarget = trainTargets |> Array.max
            let valueRange = (minTarget, maxTarget)
            
            // Initialize optimizer state for Adam
            let initialAdamState = 
                match config.Optimizer with
                | Adam _ -> Some (AdamOptimizer.createState initialParameters.Length)
                | SGD -> None
            
            if config.Verbose then
                let optimizerName = match config.Optimizer with | SGD -> "SGD" | Adam _ -> "Adam"
                printfn "Starting VQC Regression training..."
                printfn "  Features: %d samples" trainFeatures.Length
                if trainFeatures.Length > 0 then
                    printfn "            %d dimensions" trainFeatures.[0].Length
                printfn "  Target range: [%.2f, %.2f]" minTarget maxTarget
                printfn "  Parameters: %d" initialParameters.Length
                printfn "  Optimizer: %s" optimizerName
                printfn "  Learning rate: %.4f" config.LearningRate
                printfn "  Max epochs: %d" config.MaxEpochs
                printfn ""
            
            // Recursive training loop
            let rec trainLoop (state: TrainingState) : QuantumResult<TrainingState> =
                if state.Epoch >= config.MaxEpochs || state.Converged then
                    Ok state
                else
                    // Compute current loss
                    match computeRegressionLoss backend featureMap variationalForm state.Parameters trainFeatures trainTargets config.Shots valueRange with
                    | Error e -> Error (QuantumError.ValidationError ("Input", $"Loss computation failed at epoch {state.Epoch}: {e}"))
                    | Ok loss ->
                        let newLossHistory = loss :: state.LossHistory
                        
                        if config.Verbose then
                            printfn "Epoch %3d: MSE = %.6f" state.Epoch loss
                        
                        // Check convergence
                        let converged =
                            if newLossHistory.Length >= 2 then
                                let prevLoss = newLossHistory.[1]
                                let lossChange = abs (prevLoss - loss)
                                
                                if lossChange < config.ConvergenceThreshold then
                                    if config.Verbose then
                                        printfn "  Converged! (loss change: %.6f < %.6f)" lossChange config.ConvergenceThreshold
                                    true
                                else
                                    false
                            else
                                false
                        
                        if converged then
                            trainLoop { state with LossHistory = newLossHistory; Converged = true }
                        else
                            // Compute gradients
                            match computeRegressionGradient backend featureMap variationalForm state.Parameters trainFeatures trainTargets config.Shots valueRange with
                            | Error e -> Error (QuantumError.ValidationError ("Input", $"Gradient computation failed at epoch {state.Epoch}: {e}"))
                            | Ok gradient ->
                                
                                // Update parameters using selected optimizer
                                match config.Optimizer, state.AdamState with
                                | SGD, _ ->
                                    // Simple gradient descent
                                    let newParams = 
                                        Array.map2 (fun p g -> p - config.LearningRate * g) state.Parameters gradient
                                    
                                    trainLoop { 
                                        state with 
                                            Parameters = newParams
                                            LossHistory = newLossHistory
                                            Epoch = state.Epoch + 1 
                                    }
                                
                                | Adam adamConfig, Some adamState ->
                                    // Adam optimizer
                                    match AdamOptimizer.update adamConfig adamState state.Parameters gradient with
                                    | Ok (newParams, newAdamState) ->
                                        trainLoop {
                                            state with
                                                Parameters = newParams
                                                LossHistory = newLossHistory
                                                Epoch = state.Epoch + 1
                                                AdamState = Some newAdamState
                                        }
                                    | Error e ->
                                        Error (QuantumError.ValidationError ("Input", $"Adam optimizer failed at epoch {state.Epoch}: {e}"))
                                
                                | Adam _, None ->
                                    Error (QuantumError.Other "Adam optimizer state not initialized")
            
            // Start training
            let initialState = {
                Parameters = Array.copy initialParameters
                LossHistory = []
                Epoch = 0
                Converged = false
                AdamState = initialAdamState
            }
            
            match trainLoop initialState with
            | Error e -> Error e
            | Ok finalState ->
                // Compute final training metrics
                let predictions = 
                    trainFeatures 
                    |> Array.map (fun features ->
                        match predictRegression backend featureMap variationalForm finalState.Parameters features config.Shots valueRange with
                        | Ok pred -> pred.Value
                        | Error _ -> 0.0)  // Fallback
                
                let finalMSE = 
                    Array.zip trainTargets predictions
                    |> Array.averageBy (fun (y, yp) -> (y - yp) ** 2.0)
                
                let finalRSquared = calculateRSquared trainTargets predictions
                
                if config.Verbose then
                    printfn ""
                    printfn "Training complete!"
                    printfn "  Epochs: %d" finalState.Epoch
                    printfn "  Final MSE: %.6f" finalMSE
                    printfn "  R² score: %.4f" finalRSquared
                    printfn "  Converged: %b" finalState.Converged
                
                Ok {
                    Parameters = finalState.Parameters
                    LossHistory = List.rev finalState.LossHistory
                    Epochs = finalState.Epoch
                    TrainMSE = finalMSE
                    TrainRSquared = finalRSquared
                    Converged = finalState.Converged
                    ValueRange = valueRange
                }

    // ========================================================================
    // MULTI-CLASS CLASSIFICATION (One-vs-Rest)
    // ========================================================================
    
    /// Multi-class training result (one-vs-rest)
    type MultiClassTrainingResult = {
        /// Binary classifiers (one per class)
        Classifiers: TrainingResult array
        
        /// Class labels
        ClassLabels: int array
        
        /// Overall training accuracy
        TrainAccuracy: float
        
        /// Number of classes
        NumClasses: int
    }
    
    /// Multi-class prediction result
    type MultiClassPrediction = {
        /// Predicted class label
        Label: int
        
        /// Confidence score [0, 1]
        Confidence: float
        
        /// Probability distribution over all classes
        Probabilities: float array
    }
    
    /// Train multi-class VQC using one-vs-rest strategy
    let trainMultiClass
        (backend: IQuantumBackend)
        (featureMap: FeatureMapType)
        (variationalForm: VariationalForm)
        (initialParameters: float array)
        (trainFeatures: float array array)
        (trainLabels: int array)
        (config: TrainingConfig)
        : QuantumResult<MultiClassTrainingResult> =
        
        // Validate inputs
        if trainFeatures.Length <> trainLabels.Length then
            Error (QuantumError.ValidationError ("Input", "Features and labels must have same length"))
        elif trainFeatures.Length = 0 then
            Error (QuantumError.Other "Training set cannot be empty")
        else
            // Get unique class labels
            let classLabels = trainLabels |> Array.distinct |> Array.sort
            let numClasses = classLabels.Length
            
            if numClasses < 2 then
                Error (QuantumError.Other "Need at least 2 classes for multi-class classification")
            elif numClasses = 2 then
                // Binary classification - just train one classifier
                match train backend featureMap variationalForm initialParameters trainFeatures trainLabels config with
                | Error e -> Error e
                | Ok result ->
                    Ok {
                        Classifiers = [| result |]
                        ClassLabels = classLabels
                        TrainAccuracy = result.TrainAccuracy
                        NumClasses = numClasses
                    }
            else
                if config.Verbose then
                    printfn "Starting VQC multi-class training (one-vs-rest)..."
                    printfn "  Classes: %d" numClasses
                    printfn "  Samples: %d" trainFeatures.Length
                    printfn ""
                
                // Train one binary classifier per class
                let classifierResults = 
                    classLabels 
                    |> Array.mapi (fun i classLabel ->
                        if config.Verbose then
                            printfn "Training classifier %d/%d (class %d)..." (i + 1) numClasses classLabel
                        
                        // Create binary labels: 1 for current class, 0 for others
                        let binaryLabels = 
                            trainLabels 
                            |> Array.map (fun label -> if label = classLabel then 1 else 0)
                        
                        // Train binary classifier
                        match train backend featureMap variationalForm initialParameters trainFeatures binaryLabels config with
                        | Error e -> Error (QuantumError.ValidationError ("Input", $"Classifier for class {classLabel} failed: {e}"))
                        | Ok result ->
                            if config.Verbose then
                                printfn "  Class %d accuracy: %.4f" classLabel result.TrainAccuracy
                                printfn ""
                            Ok result
                    )
                
                // Check if any classifier failed
                match classifierResults |> Array.tryFind Result.isError with
                | Some (Error e) -> Error e
                | _ ->
                    let classifiers = classifierResults |> Array.choose (function Ok r -> Some r | _ -> None)
                    
                    // Compute overall training accuracy using one-vs-rest prediction
                    let mutable correctCount = 0
                    for i in 0 .. trainFeatures.Length - 1 do
                        // Get scores from all classifiers
                        let scores = 
                            classifiers 
                            |> Array.map (fun classifier ->
                                match predict backend featureMap variationalForm classifier.Parameters trainFeatures.[i] config.Shots with
                                | Ok pred -> pred.Probability
                                | Error _ -> 0.0
                            )
                        
                        // Predicted class is the one with highest score
                        // Use Array.mapi and maxBy to avoid floating-point comparison issues
                        let predictedClassIdx = scores |> Array.mapi (fun i s -> (i, s)) |> Array.maxBy snd |> fst
                        let predictedClass = classLabels.[predictedClassIdx]
                        
                        if predictedClass = trainLabels.[i] then
                            correctCount <- correctCount + 1
                    
                    let accuracy = float correctCount / float trainFeatures.Length
                    
                    if config.Verbose then
                        printfn "Multi-class training complete!"
                        printfn "  Overall accuracy: %.4f" accuracy
                    
                    Ok {
                        Classifiers = classifiers
                        ClassLabels = classLabels
                        TrainAccuracy = accuracy
                        NumClasses = numClasses
                    }
    
    /// Predict class for multi-class VQC (one-vs-rest)
    let predictMultiClass
        (backend: IQuantumBackend)
        (featureMap: FeatureMapType)
        (variationalForm: VariationalForm)
        (result: MultiClassTrainingResult)
        (features: float array)
        (shots: int)
        : QuantumResult<MultiClassPrediction> =
        
        // Get scores from all classifiers
        let scoreResults = 
            result.Classifiers 
            |> Array.map (fun classifier ->
                match predict backend featureMap variationalForm classifier.Parameters features shots with
                | Ok pred -> Ok pred.Probability
                | Error e -> Error e
            )
        
        // Check if any prediction failed
        match scoreResults |> Array.tryFind Result.isError with
        | Some (Error e) -> Error (QuantumError.ValidationError ("Input", $"Multi-class prediction failed: {e}"))
        | _ ->
            let scores = scoreResults |> Array.choose (function Ok s -> Some s | _ -> None)
            
            // Normalize scores to probabilities (softmax-like)
            let expScores = scores |> Array.map exp
            let sumExp = expScores |> Array.sum
            let probabilities = 
                if sumExp > 0.0 then
                    expScores |> Array.map (fun e -> e / sumExp)
                else
                    Array.create result.NumClasses (1.0 / float result.NumClasses)
            
            // Predicted class is the one with highest probability
            // Use Array.mapi and maxBy to avoid floating-point comparison issues
            let predictedClassIdx = probabilities |> Array.mapi (fun i p -> (i, p)) |> Array.maxBy snd |> fst
            let predictedLabel = result.ClassLabels.[predictedClassIdx]
            
            Ok {
                Label = predictedLabel
                Confidence = probabilities.[predictedClassIdx]
                Probabilities = probabilities
            }
