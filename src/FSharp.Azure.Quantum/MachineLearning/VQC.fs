namespace FSharp.Azure.Quantum.MachineLearning

/// Variational Quantum Classifier (VQC)
///
/// Binary classification using quantum circuits with parameterized gates.
/// Implements training loop with parameter shift rule for gradient computation.
/// Supports both SGD and Adam optimizers.

open System
open FSharp.Azure.Quantum.CircuitBuilder
open FSharp.Azure.Quantum.Core.BackendAbstraction
open FSharp.Azure.Quantum.Core.CircuitAbstraction

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
        : Result<float, string> =
        
        // Wrap circuit for backend execution
        let wrappedCircuit = CircuitWrapper(circuit) :> ICircuit
        
        // Execute circuit
        match backend.Execute wrappedCircuit shots with
        | Ok result ->
            // Get measurement results (int[][])
            // Each int[] is one shot: [qubit0, qubit1, ...]
            let measurements = result.Measurements
            
            // Count |1⟩ measurements on first qubit
            let onesCount = 
                measurements
                |> Array.filter (fun shot -> shot.[0] = 1)
                |> Array.length
                |> float
            
            let totalShots = float shots
            let probability = onesCount / totalShots
            
            Ok probability
        | Error e -> Error e
    
    /// Build VQC circuit for a single sample
    let private buildVQCCircuit
        (featureMap: FeatureMapType)
        (variationalForm: VariationalForm)
        (features: float array)
        (parameters: float array)
        : Result<Circuit, string> =
        
        let numQubits = features.Length
        
        // Build feature map circuit
        match FeatureMap.buildFeatureMap featureMap features with
        | Error e -> Error $"Feature map error: {e}"
        | Ok fmCircuit ->
            
            // Build variational form circuit
            match VariationalForms.buildVariationalForm variationalForm parameters numQubits with
            | Error e -> Error $"Variational form error: {e}"
            | Ok vfCircuit ->
                
                // Compose circuits
                match VariationalForms.composeWithFeatureMap fmCircuit vfCircuit with
                | Error e -> Error $"Composition error: {e}"
                | Ok composedCircuit ->
                    // Backend will automatically measure all qubits
                    Ok composedCircuit
    
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
    
    /// Compute average loss over dataset
    let private computeLoss
        (backend: IQuantumBackend)
        (featureMap: FeatureMapType)
        (variationalForm: VariationalForm)
        (parameters: float array)
        (features: float array array)
        (labels: int array)
        (shots: int)
        : Result<float, string> =
        
        let computeSampleLoss i =
            buildVQCCircuit featureMap variationalForm features.[i] parameters
            |> Result.bind (fun circuit -> forwardPass backend circuit shots)
            |> Result.map (fun prediction -> binaryCrossEntropy prediction labels.[i])
        
        // Compute loss for each sample
        let results = Array.mapi (fun i _ -> computeSampleLoss i) features
        
        // Check if any failed
        match results |> Array.tryFind Result.isError with
        | Some (Error e) -> Error e
        | _ ->
            let losses = results |> Array.choose (function Ok v -> Some v | _ -> None)
            Ok (Array.average losses)
    
    // ========================================================================
    // GRADIENT COMPUTATION (Parameter Shift Rule)
    // ========================================================================
    
    /// Compute gradient using parameter shift rule
    /// 
    /// For a parameter θ_i, the gradient is:
    /// ∂L/∂θ_i = (L(θ + π/2 e_i) - L(θ - π/2 e_i)) / 2
    let private computeGradient
        (backend: IQuantumBackend)
        (featureMap: FeatureMapType)
        (variationalForm: VariationalForm)
        (parameters: float array)
        (features: float array array)
        (labels: int array)
        (shots: int)
        : Result<float array, string> =
        
        let shift = Math.PI / 2.0
        
        let computeParamGradient i =
            // Shift parameter forward
            let paramsPlus = Array.copy parameters
            paramsPlus.[i] <- paramsPlus.[i] + shift
            
            // Shift parameter backward
            let paramsMinus = Array.copy parameters
            paramsMinus.[i] <- paramsMinus.[i] - shift
            
            // Compute gradient using parameter shift rule
            computeLoss backend featureMap variationalForm paramsPlus features labels shots
            |> Result.bind (fun lossPlus ->
                computeLoss backend featureMap variationalForm paramsMinus features labels shots
                |> Result.map (fun lossMinus -> (lossPlus - lossMinus) / 2.0))
        
        // Compute gradient for each parameter
        let results = parameters |> Array.mapi (fun i _ -> computeParamGradient i)
        
        // Check if any failed
        match results |> Array.tryFind Result.isError with
        | Some (Error e) -> Error $"Gradient computation failed: {e}"
        | _ ->
            let gradients = results |> Array.choose (function Ok v -> Some v | _ -> None)
            Ok gradients
    
    // ========================================================================
    // TRAINING LOOP
    // ========================================================================
    
    /// Train VQC model using gradient descent
    let rec train
        (backend: IQuantumBackend)
        (featureMap: FeatureMapType)
        (variationalForm: VariationalForm)
        (initialParameters: float array)
        (trainFeatures: float array array)
        (trainLabels: int array)
        (config: TrainingConfig)
        : Result<TrainingResult, string> =
        
        // Validate inputs
        if trainFeatures.Length <> trainLabels.Length then
            Error "Features and labels must have same length"
        elif trainFeatures.Length = 0 then
            Error "Training set cannot be empty"
        else
            // Initialize training state
            let mutable parameters = Array.copy initialParameters
            let mutable lossHistory = []
            let mutable converged = false
            let mutable epoch = 0
            let mutable trainingError = None
            
            // Initialize optimizer state for Adam
            let mutable adamState = 
                match config.Optimizer with
                | Adam _ -> Some (AdamOptimizer.createState parameters.Length)
                | SGD -> None
            
            if config.Verbose then
                let optimizerName = match config.Optimizer with | SGD -> "SGD" | Adam _ -> "Adam"
                printfn "Starting VQC training..."
                printfn "  Features: %d samples" trainFeatures.Length
                if trainFeatures.Length > 0 then
                    printfn "            %d dimensions" trainFeatures.[0].Length
                printfn "  Parameters: %d" parameters.Length
                printfn "  Optimizer: %s" optimizerName
                printfn "  Learning rate: %.4f" config.LearningRate
                printfn "  Max epochs: %d" config.MaxEpochs
                printfn ""
            
            // Training loop
            while epoch < config.MaxEpochs && not converged && trainingError.IsNone do
                // Compute current loss
                match computeLoss backend featureMap variationalForm parameters trainFeatures trainLabels config.Shots with
                | Error e -> trainingError <- Some $"Loss computation failed at epoch {epoch}: {e}"
                | Ok loss ->
                    
                    lossHistory <- loss :: lossHistory
                    
                    if config.Verbose then
                        printfn "Epoch %3d: Loss = %.6f" epoch loss
                    
                    // Check convergence
                    if lossHistory.Length >= 2 then
                        let prevLoss = lossHistory.[1]
                        let lossChange = abs (prevLoss - loss)
                        
                        if lossChange < config.ConvergenceThreshold then
                            converged <- true
                            if config.Verbose then
                                printfn "  Converged! (loss change: %.6f < %.6f)" lossChange config.ConvergenceThreshold
                    
                    if not converged then
                        // Compute gradients
                        match computeGradient backend featureMap variationalForm parameters trainFeatures trainLabels config.Shots with
                        | Error e -> trainingError <- Some $"Gradient computation failed at epoch {epoch}: {e}"
                        | Ok gradient ->
                            
                            // Update parameters using selected optimizer
                            match config.Optimizer, adamState with
                            | SGD, _ ->
                                // Simple gradient descent
                                for i in 0 .. parameters.Length - 1 do
                                    parameters.[i] <- parameters.[i] - config.LearningRate * gradient.[i]
                            
                            | Adam adamConfig, Some state ->
                                // Adam optimizer
                                match AdamOptimizer.update adamConfig state parameters gradient with
                                | Ok (newParams, newState) ->
                                    parameters <- newParams
                                    adamState <- Some newState
                                | Error e ->
                                    trainingError <- Some $"Adam optimizer failed at epoch {epoch}: {e}"
                            
                            | Adam _, None ->
                                trainingError <- Some "Adam optimizer state not initialized"
                            
                            epoch <- epoch + 1
            
            // Check for training errors
            match trainingError with
            | Some e -> Error e
            | None ->
                // Compute final training accuracy
                match evaluate backend featureMap variationalForm parameters trainFeatures trainLabels config.Shots with
                | Error e -> Error $"Final evaluation failed: {e}"
                | Ok accuracy ->
                    
                    if config.Verbose then
                        printfn ""
                        printfn "Training complete!"
                        printfn "  Epochs: %d" epoch
                        printfn "  Final loss: %.6f" (List.head lossHistory)
                        printfn "  Train accuracy: %.2f%%" (accuracy * 100.0)
                        printfn "  Converged: %b" converged
                    
                    Ok {
                        Parameters = parameters
                        LossHistory = List.rev lossHistory
                        Epochs = epoch
                        TrainAccuracy = accuracy
                        Converged = converged
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
        : Result<Prediction, string> =
        
        match buildVQCCircuit featureMap variationalForm features parameters with
        | Error e -> Error e
        | Ok circuit ->
            
            match forwardPass backend circuit shots with
            | Error e -> Error e
            | Ok probability ->
                
                let label = if probability >= 0.5 then 1 else 0
                
                Ok {
                    Label = label
                    Probability = probability
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
        : Result<float, string> =
        
        if features.Length <> labels.Length then
            Error "Features and labels must have same length"
        elif features.Length = 0 then
            Error "Dataset cannot be empty"
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
    }
    
    /// Create training configuration with Adam optimizer
    let defaultConfigWithAdam = {
        LearningRate = 0.001  // Adam typically uses smaller learning rate
        MaxEpochs = 50
        ConvergenceThreshold = 1e-4
        Shots = 1024
        Verbose = true
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
        : Result<ConfusionMatrix, string> =
        
        if features.Length <> labels.Length then
            Error "Features and labels must have same length"
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
