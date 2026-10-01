namespace FSharp.Azure.Quantum.MachineLearning

/// Variational Quantum Classifier (VQC).
///
/// Binary classification using quantum circuits with parameterized gates.
/// Implements training loop with parameter shift rule for gradient computation.
/// Supports both SGD and Adam optimizers.

open System
open System.Threading
open System.Threading.Tasks
open FSharp.Azure.Quantum.Backends
open FSharp.Azure.Quantum.CircuitBuilder
open FSharp.Azure.Quantum.Core.BackendAbstraction
open FSharp.Azure.Quantum.Core.CircuitAbstraction
open FSharp.Azure.Quantum.Core
open Microsoft.Extensions.Logging

module VQC =

    // ========================================================================
    // TYPES
    // ========================================================================

    /// Optimizer choice for training
    type Optimizer =
        /// Stochastic Gradient Descent
        | SGD
        /// Adam optimizer with configuration
        | Adam of AdamOptimizer.AdamConfig

    /// Training configuration
    type TrainingConfig =
        {
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

            /// Structured logger (replaces Verbose printfn output)
            Logger: ILogger option
        }

    /// Training result
    type TrainingResult =
        {
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
    [<Struct>]
    type Prediction =
        {
            /// Predicted label (0 or 1)
            Label: int

            /// Prediction probability [0, 1]
            Probability: float
        }

    // ========================================================================
    // FORWARD PASS
    // ========================================================================

    /// Execute forward pass asynchronously using backend.ExecuteToStateAsync.
    /// Non-blocking I/O for cloud backends.
    let private forwardPassAsync
        (backend: IQuantumBackend)
        (circuit: Circuit)
        (shots: int)
        (cancellationToken: CancellationToken)
        : Task<QuantumResult<float>> =
        task {
            let wrappedCircuit = CircuitWrapper(circuit) :> ICircuit
            let! stateResult = backend.ExecuteToStateAsync wrappedCircuit cancellationToken

            return
                match stateResult with
                | Error e -> Error e
                | Ok state ->
                    let measurements = QuantumState.measure state shots

                    let onesCount =
                        measurements |> Array.filter (fun shot -> shot.[0] = 1) |> Array.length |> float

                    let totalShots = float (max 1 measurements.Length)
                    Ok(onesCount / totalShots)
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
                |> Result.mapError (fun e -> QuantumError.ValidationError("Input", $"Feature map error: {e}"))

            // Build variational form circuit
            let! vfCircuit =
                VariationalForms.buildVariationalForm variationalForm parameters numQubits
                |> Result.mapError (fun e -> QuantumError.ValidationError("Input", $"Variational form error: {e}"))

            // Compose circuits
            let! composedCircuit =
                VariationalForms.composeWithFeatureMap fmCircuit vfCircuit
                |> Result.mapError (fun e -> QuantumError.ValidationError("Input", $"Composition error: {e}"))

            // Backend will automatically measure all qubits
            return composedCircuit
        }

    // ========================================================================
    // LOSS FUNCTIONS
    // ========================================================================

    /// Binary cross-entropy loss
    let private binaryCrossEntropy (predicted: float) (actual: int) : float =
        let epsilon = 1e-7 // Avoid log(0)
        let p = max epsilon (min (1.0 - epsilon) predicted)

        if actual = 1 then -log p else -log(1.0 - p)

    /// Analytic derivative of binary cross-entropy w.r.t. the predicted probability p.
    ///
    /// L(p, y) = -y ln p - (1 - y) ln (1 - p)
    /// dL/dp   = -y/p + (1 - y)/(1 - p) = (p - y) / (p (1 - p))
    ///
    /// Uses the same epsilon clamping as binaryCrossEntropy to avoid division by zero.
    let private binaryCrossEntropyDerivative (predicted: float) (actual: int) : float =
        let epsilon = 1e-7
        let p = max epsilon (min (1.0 - epsilon) predicted)
        (p - float actual) / (p * (1.0 - p))

    /// Circuits one loss or gradient evaluation keeps in flight: at most
    /// QuantumKernels.MaxConcurrentSampledJobs on a shot-sampling backend (every circuit is a
    /// separately queued and billed job), unlimited on simulators.
    type private JobGate(backend: IQuantumBackend) =
        let slots =
            match backend with
            | :? IShotSamplingBackend ->
                Some(
                    new SemaphoreSlim(QuantumKernels.MaxConcurrentSampledJobs, QuantumKernels.MaxConcurrentSampledJobs)
                )
            | _ -> None

        /// Runs `job` once a slot is free.
        member _.Run<'T>(cancellationToken: CancellationToken, job: unit -> Task<'T>) : Task<'T> =
            match slots with
            | None -> job ()
            | Some gate ->
                task {
                    do! gate.WaitAsync cancellationToken

                    try
                        return! job ()
                    finally
                        gate.Release() |> ignore
                }

    /// Compute average loss over dataset using Task.WhenAll for genuine concurrent I/O.
    /// Each sample's forward pass runs via backend.ExecuteToStateAsync.
    let private computeLossAsync
        (backend: IQuantumBackend)
        (featureMap: FeatureMapType)
        (variationalForm: VariationalForm)
        (parameters: float array)
        (features: float array array)
        (labels: int array)
        (shots: int)
        (cancellationToken: CancellationToken)
        : Task<QuantumResult<float>> =
        task {
            let gate = JobGate backend

            let computeSampleLossAsync i =
                task {
                    match buildVQCCircuit featureMap variationalForm features.[i] parameters with
                    | Error e -> return Error e
                    | Ok circuit ->
                        let! forwardResult =
                            gate.Run(
                                cancellationToken,
                                fun () -> forwardPassAsync backend circuit shots cancellationToken
                            )

                        return
                            forwardResult
                            |> Result.map (fun prediction -> binaryCrossEntropy prediction labels.[i])
                }

            // Launch all sample loss computations concurrently
            let! results =
                features |> Array.mapi (fun i _ -> computeSampleLossAsync i) |> Task.WhenAll

            // Check if any failed
            return
                match results |> Array.tryFind Result.isError with
                | Some(Error e) -> Error e
                | _ ->
                    let losses =
                        results
                        |> Array.choose (function
                            | Ok v -> Some v
                            | Error _ -> None)

                    Ok(Array.average losses)
        }

    // ========================================================================
    // GRADIENT COMPUTATION (Parameter Shift Rule)
    // ========================================================================

    /// Compute per-sample circuit expectations p_j(θ) concurrently via Task.WhenAll,
    /// submitting each circuit through `gate`.
    let private computePredictionsAsync
        (gate: JobGate)
        (backend: IQuantumBackend)
        (featureMap: FeatureMapType)
        (variationalForm: VariationalForm)
        (parameters: float array)
        (features: float array array)
        (shots: int)
        (cancellationToken: CancellationToken)
        : Task<QuantumResult<float array>> =
        task {
            let! results =
                features
                |> Array.map (fun sample ->
                    task {
                        match buildVQCCircuit featureMap variationalForm sample parameters with
                        | Error e -> return Error e
                        | Ok circuit ->
                            return!
                                gate.Run(
                                    cancellationToken,
                                    fun () -> forwardPassAsync backend circuit shots cancellationToken
                                )
                    })
                |> Task.WhenAll

            return
                match results |> Array.tryFind Result.isError with
                | Some(Error e) -> Error e
                | _ ->
                    Ok(
                        results
                        |> Array.choose (function
                            | Ok v -> Some v
                            | Error _ -> None)
                    )
        }

    /// Compute gradient using the parameter shift rule + chain rule.
    ///
    /// The parameter shift rule is exact only for the circuit EXPECTATION p(θ):
    ///   ∂p/∂θ_i = (p(θ + π/2 e_i) - p(θ - π/2 e_i)) / 2
    /// The cross-entropy loss L(p, y) is a nonlinear function of p, so the loss
    /// gradient requires the chain rule (per sample j):
    ///   ∂L_j/∂θ_i = dL/dp|_{p_j(θ)} · (p_j(θ + π/2 e_i) - p_j(θ - π/2 e_i)) / 2
    /// with dL/dp = (p - y) / (p (1 - p)) for binary cross-entropy.
    /// The dataset gradient is the average of the per-sample gradients.
    ///
    /// Both the per-parameter gradient and the +/- shift pair within each parameter
    /// are computed concurrently; one JobGate bounds the circuits in flight.
    let private computeGradientAsync
        (backend: IQuantumBackend)
        (featureMap: FeatureMapType)
        (variationalForm: VariationalForm)
        (parameters: float array)
        (features: float array array)
        (labels: int array)
        (shots: int)
        (cancellationToken: CancellationToken)
        : Task<QuantumResult<float array>> =
        task {
            let shift = Math.PI / 2.0
            let gate = JobGate backend

            // Unshifted per-sample predictions p_j(θ): loss-derivative factor of the chain rule
            let! basePredictionsResult =
                computePredictionsAsync
                    gate
                    backend
                    featureMap
                    variationalForm
                    parameters
                    features
                    shots
                    cancellationToken

            match basePredictionsResult with
            | Error e -> return Error(QuantumError.ValidationError("Input", $"Gradient computation failed: {e}"))
            | Ok basePredictions ->
                let lossDerivatives = Array.map2 binaryCrossEntropyDerivative basePredictions labels

                let computeParamGradientAsync i =
                    task {
                        // Shift parameter forward
                        let paramsPlus = Array.copy parameters
                        paramsPlus.[i] <- paramsPlus.[i] + shift

                        // Shift parameter backward
                        let paramsMinus = Array.copy parameters
                        paramsMinus.[i] <- paramsMinus.[i] - shift

                        // Compute forward and backward shifted predictions in parallel
                        let! results =
                            Task.WhenAll
                                [|
                                    computePredictionsAsync
                                        gate
                                        backend
                                        featureMap
                                        variationalForm
                                        paramsPlus
                                        features
                                        shots
                                        cancellationToken
                                    computePredictionsAsync
                                        gate
                                        backend
                                        featureMap
                                        variationalForm
                                        paramsMinus
                                        features
                                        shots
                                        cancellationToken
                                |]

                        // Combine results via the chain rule, averaged over samples
                        return
                            match results.[0], results.[1] with
                            | Ok predsPlus, Ok predsMinus ->
                                Array.init features.Length (fun j ->
                                    lossDerivatives.[j] * (predsPlus.[j] - predsMinus.[j]) / 2.0)
                                |> Array.average
                                |> Ok
                            | Error e, _ -> Error e
                            | _, Error e -> Error e
                    }

                // Compute gradient for all parameters in parallel
                let! results =
                    parameters
                    |> Array.mapi (fun i _ -> computeParamGradientAsync i)
                    |> Task.WhenAll

                // Check if any failed
                return
                    match results |> Array.tryFind Result.isError with
                    | Some(Error e) -> Error(QuantumError.ValidationError("Input", $"Gradient computation failed: {e}"))
                    | _ ->
                        let gradients =
                            results
                            |> Array.choose (function
                                | Ok v -> Some v
                                | Error _ -> None)

                        Ok gradients
        }

    // ========================================================================
    // TRAINING LOOP
    // ========================================================================

    /// Training state for recursive loop
    type private TrainingState =
        {
            Parameters: float array
            LossHistory: float list
            Epoch: int
            Converged: bool
            AdamState: AdamOptimizer.AdamState option
        }

    /// Predict label for a single sample, asynchronously
    let predictAsync
        (backend: IQuantumBackend)
        (featureMap: FeatureMapType)
        (variationalForm: VariationalForm)
        (parameters: float array)
        (features: float array)
        (shots: int)
        (cancellationToken: CancellationToken)
        : Task<QuantumResult<Prediction>> =

        quantumResultTask {
            let! circuit = buildVQCCircuit featureMap variationalForm features parameters

            let! probability = forwardPassAsync backend circuit shots cancellationToken

            let label = if probability >= 0.5 then 1 else 0

            return
                {
                    Label = label
                    Probability = probability
                }
        }

    /// Predicts every sample in order; the first failing sample's error is the result
    let private predictEachAsync
        (backend: IQuantumBackend)
        (featureMap: FeatureMapType)
        (variationalForm: VariationalForm)
        (parameters: float array)
        (features: float array array)
        (shots: int)
        (cancellationToken: CancellationToken)
        : Task<QuantumResult<Prediction array>> =

        quantumResultTask {
            let predictions = ResizeArray<Prediction>(features.Length)

            for sample in features do
                let! prediction =
                    predictAsync backend featureMap variationalForm parameters sample shots cancellationToken

                predictions.Add prediction

            return predictions.ToArray()
        }

    /// Evaluate model accuracy on dataset, asynchronously
    let evaluateAsync
        (backend: IQuantumBackend)
        (featureMap: FeatureMapType)
        (variationalForm: VariationalForm)
        (parameters: float array)
        (features: float array array)
        (labels: int array)
        (shots: int)
        (cancellationToken: CancellationToken)
        : Task<QuantumResult<float>> =

        if features.Length <> labels.Length then
            Task.FromResult(Error(QuantumError.ValidationError("Input", "Features and labels must have same length")))
        elif features.Length = 0 then
            Task.FromResult(Error(QuantumError.Other "Dataset cannot be empty"))
        else
            quantumResultTask {
                let! predictions =
                    predictEachAsync backend featureMap variationalForm parameters features shots cancellationToken

                let correctCount =
                    predictions
                    |> Array.mapi (fun i pred -> if pred.Label = labels.[i] then 1 else 0)
                    |> Array.sum

                return float correctCount / float features.Length
            }

    /// Train VQC model using gradient descent, asynchronously.
    /// Epochs run one after another; each epoch awaits its loss and gradient evaluations.
    let trainAsync
        (backend: IQuantumBackend)
        (featureMap: FeatureMapType)
        (variationalForm: VariationalForm)
        (initialParameters: float array)
        (trainFeatures: float array array)
        (trainLabels: int array)
        (config: TrainingConfig)
        (cancellationToken: CancellationToken)
        : Task<QuantumResult<TrainingResult>> =

        // Validate inputs
        if trainFeatures.Length <> trainLabels.Length then
            Task.FromResult(Error(QuantumError.ValidationError("Input", "Features and labels must have same length")))
        elif trainFeatures.Length = 0 then
            Task.FromResult(Error(QuantumError.Other "Training set cannot be empty"))
        else
            // Initialize optimizer state for Adam
            let initialAdamState =
                match config.Optimizer with
                | Adam _ -> Some(AdamOptimizer.createState initialParameters.Length)
                | SGD -> None

            if config.Verbose then
                let optimizerName =
                    match config.Optimizer with
                    | SGD -> "SGD"
                    | Adam _ -> "Adam"

                let log = logInfo config.Logger
                log "Starting VQC training..."
                log $"  Features: {trainFeatures.Length} samples"

                if trainFeatures.Length > 0 then
                    log $"            {trainFeatures.[0].Length} dimensions"

                log $"  Parameters: {initialParameters.Length}"
                log $"  Optimizer: {optimizerName}"
                log $"  Learning rate: {config.LearningRate:F4}"
                log $"  Max epochs: {config.MaxEpochs}"
                log ""

            // Report progress: Training started
            config.ProgressReporter
            |> Option.iter (fun reporter ->
                reporter.Report(
                    Progress.PhaseChanged("VQC Training", Some $"Starting with {config.MaxEpochs} max epochs...")
                ))

            // One training epoch: the loss, then a parameter update unless the loss converged
            let epochAsync (state: TrainingState) : Task<QuantumResult<TrainingState>> =
                task {
                    // Compute current loss
                    match!
                        computeLossAsync
                            backend
                            featureMap
                            variationalForm
                            state.Parameters
                            trainFeatures
                            trainLabels
                            config.Shots
                            cancellationToken
                    with
                    | Error e ->
                        return
                            Error(
                                QuantumError.ValidationError(
                                    "Input",
                                    $"Loss computation failed at epoch {state.Epoch}: {e}"
                                )
                            )
                    | Ok loss ->
                        let newLossHistory = loss :: state.LossHistory

                        // Report progress
                        config.ProgressReporter
                        |> Option.iter (fun reporter ->
                            reporter.Report(Progress.IterationUpdate(state.Epoch, config.MaxEpochs, Some loss)))

                        if config.Verbose then
                            logInfo config.Logger $"Epoch %3d{state.Epoch}: Loss = {loss:F6}"

                        // Check convergence
                        let converged =
                            if newLossHistory.Length >= 2 then
                                let prevLoss = newLossHistory.[1]
                                let lossChange = abs (prevLoss - loss)

                                if lossChange < config.ConvergenceThreshold then
                                    if config.Verbose then
                                        logInfo
                                            config.Logger
                                            $"  Converged! (loss change: {lossChange:F6} < {config.ConvergenceThreshold:F6})"

                                    true
                                else
                                    false
                            else
                                false

                        if converged then
                            return
                                Ok
                                    { state with
                                        LossHistory = newLossHistory
                                        Converged = true
                                    }
                        else
                            // Compute gradients
                            match!
                                computeGradientAsync
                                    backend
                                    featureMap
                                    variationalForm
                                    state.Parameters
                                    trainFeatures
                                    trainLabels
                                    config.Shots
                                    cancellationToken
                            with
                            | Error e ->
                                return
                                    Error(
                                        QuantumError.ValidationError(
                                            "Input",
                                            $"Gradient computation failed at epoch {state.Epoch}: {e}"
                                        )
                                    )
                            | Ok gradient ->
                                // Update parameters using selected optimizer
                                match config.Optimizer, state.AdamState with
                                | SGD, _ ->
                                    // Simple gradient descent
                                    let newParams =
                                        Array.map2 (fun p g -> p - config.LearningRate * g) state.Parameters gradient

                                    return
                                        Ok
                                            { state with
                                                Parameters = newParams
                                                LossHistory = newLossHistory
                                                Epoch = state.Epoch + 1
                                            }

                                | Adam adamConfig, Some adamState ->
                                    // Adam optimizer
                                    return
                                        AdamOptimizer.update adamConfig adamState state.Parameters gradient
                                        |> Result.mapError (fun e ->
                                            QuantumError.ValidationError(
                                                "Input",
                                                $"Adam optimizer failed at epoch {state.Epoch}: {e}"
                                            ))
                                        |> Result.map (fun (newParams, newAdamState) ->
                                            { state with
                                                Parameters = newParams
                                                LossHistory = newLossHistory
                                                Epoch = state.Epoch + 1
                                                AdamState = Some newAdamState
                                            })

                                | Adam _, None ->
                                    return Error(QuantumError.Other "Adam optimizer state not initialized")
                }

            // Start training
            let initialState =
                {
                    Parameters = Array.copy initialParameters
                    LossHistory = []
                    Epoch = 0
                    Converged = false
                    AdamState = initialAdamState
                }

            task {
                // Epochs run in order until convergence, MaxEpochs or the first error
                let mutable state = initialState
                let mutable failure = None

                while failure.IsNone && state.Epoch < config.MaxEpochs && not state.Converged do
                    match! epochAsync state with
                    | Ok next -> state <- next
                    | Error e -> failure <- Some e

                match failure with
                | Some e -> return Error e
                | None ->
                    let finalState = state

                    // Compute final training accuracy
                    match!
                        evaluateAsync
                            backend
                            featureMap
                            variationalForm
                            finalState.Parameters
                            trainFeatures
                            trainLabels
                            config.Shots
                            cancellationToken
                    with
                    | Error e ->
                        return Error(QuantumError.ValidationError("Input", $"Final evaluation failed: {e.Message}"))
                    | Ok accuracy ->
                        if config.Verbose then
                            let log = logInfo config.Logger
                            log ""
                            log "Training complete!"
                            log $"  Epochs: {finalState.Epoch}"
                            // LossHistory should never be empty here (training loop adds losses), but safe access
                            log $"  Final loss: {(List.tryHead finalState.LossHistory |> Option.defaultValue 0.0):F6}"
                            log $"  Train accuracy: {(accuracy * 100.0):F2}%%"
                            log $"  Converged: {finalState.Converged}"

                        return
                            Ok
                                {
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



    // ========================================================================
    // HELPER FUNCTIONS
    // ========================================================================

    /// Create default training configuration (uses SGD optimizer)
    let defaultConfig =
        {
            LearningRate = 0.1
            MaxEpochs = 50
            ConvergenceThreshold = 1e-4
            Shots = 1024
            Verbose = true
            Optimizer = SGD
            ProgressReporter = None
            Logger = None
        }

    /// Create training configuration with Adam optimizer
    let defaultConfigWithAdam =
        {
            LearningRate = 0.001 // Adam typically uses smaller learning rate
            MaxEpochs = 50
            ConvergenceThreshold = 1e-4
            Shots = 1024
            Verbose = true
            ProgressReporter = None
            Logger = None
            Optimizer = Adam AdamOptimizer.defaultConfig
        }

    /// Create custom Adam configuration
    let createAdamConfig learningRate beta1 beta2 =
        match AdamOptimizer.createConfig learningRate beta1 beta2 1e-8 with
        | Ok adamCfg ->
            Ok
                { defaultConfig with
                    LearningRate = learningRate
                    Optimizer = Adam adamCfg
                }
        | Error e -> Error e

    /// Confusion matrix for binary classification
    [<Struct>]
    type ConfusionMatrix =
        {
            TruePositives: int
            TrueNegatives: int
            FalsePositives: int
            FalseNegatives: int
        }

    /// Compute confusion matrix, asynchronously
    let confusionMatrixAsync
        (backend: IQuantumBackend)
        (featureMap: FeatureMapType)
        (variationalForm: VariationalForm)
        (parameters: float array)
        (features: float array array)
        (labels: int array)
        (shots: int)
        (cancellationToken: CancellationToken)
        : Task<QuantumResult<ConfusionMatrix>> =

        if features.Length <> labels.Length then
            Task.FromResult(Error(QuantumError.ValidationError("Input", "Features and labels must have same length")))
        else
            quantumResultTask {
                let! predictions =
                    predictEachAsync backend featureMap variationalForm parameters features shots cancellationToken

                let categorize i (pred: Prediction) =
                    match (pred.Label, labels.[i]) with
                    | (1, 1) -> (1, 0, 0, 0) // TP
                    | (0, 0) -> (0, 1, 0, 0) // TN
                    | (1, 0) -> (0, 0, 1, 0) // FP
                    | (0, 1) -> (0, 0, 0, 1) // FN
                    | _ -> (0, 0, 0, 0)

                let (tp, tn, fp, fn) =
                    predictions
                    |> Array.mapi categorize
                    |> Array.fold
                        (fun (tp, tn, fp, fn) (dtp, dtn, dfp, dfn) -> (tp + dtp, tn + dtn, fp + dfp, fn + dfn))
                        (0, 0, 0, 0)

                return
                    {
                        TruePositives = tp
                        TrueNegatives = tn
                        FalsePositives = fp
                        FalseNegatives = fn
                    }
            }

    /// Compute precision from confusion matrix
    let precision (cm: ConfusionMatrix) : float =
        let denominator = cm.TruePositives + cm.FalsePositives

        if denominator = 0 then
            0.0
        else
            float cm.TruePositives / float denominator

    /// Compute recall from confusion matrix
    let recall (cm: ConfusionMatrix) : float =
        let denominator = cm.TruePositives + cm.FalseNegatives

        if denominator = 0 then
            0.0
        else
            float cm.TruePositives / float denominator

    /// Compute F1 score from confusion matrix
    let f1Score (cm: ConfusionMatrix) : float =
        let p = precision cm
        let r = recall cm
        let denominator = p + r
        if denominator = 0.0 then 0.0 else 2.0 * p * r / denominator

    // ========================================================================
    // REGRESSION SUPPORT
    // ========================================================================

    /// Regression training result
    type RegressionTrainingResult =
        {
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
    [<Struct>]
    type RegressionPrediction =
        {
            /// Predicted continuous value
            Value: float
        }

    /// Predict continuous value for a single sample (regression) asynchronously.
    let predictRegressionAsync
        (backend: IQuantumBackend)
        (featureMap: FeatureMapType)
        (variationalForm: VariationalForm)
        (parameters: float array)
        (features: float array)
        (shots: int)
        (valueRange: float * float)
        (cancellationToken: CancellationToken)
        : Task<QuantumResult<RegressionPrediction>> =
        task {
            match buildVQCCircuit featureMap variationalForm features parameters with
            | Error e -> return Error e
            | Ok circuit ->
                let! forwardResult = forwardPassAsync backend circuit shots cancellationToken

                return
                    match forwardResult with
                    | Error e -> Error e
                    | Ok expectation ->
                        let (minVal, maxVal) = valueRange
                        let value = minVal + expectation * (maxVal - minVal)
                        Ok { Value = value }
        }

    /// Compute Mean Squared Error loss for regression using Task.WhenAll; one JobGate
    /// bounds the circuits in flight.
    let private computeRegressionLossAsync
        (backend: IQuantumBackend)
        (featureMap: FeatureMapType)
        (variationalForm: VariationalForm)
        (parameters: float array)
        (trainFeatures: float array array)
        (trainTargets: float array)
        (shots: int)
        (valueRange: float * float)
        (cancellationToken: CancellationToken)
        : Task<QuantumResult<float>> =
        task {
            let gate = JobGate backend

            // Compute squared errors for each sample concurrently
            let! results =
                Array.zip trainFeatures trainTargets
                |> Array.map (fun (features, target) ->
                    task {
                        let! predResult =
                            gate.Run(
                                cancellationToken,
                                fun () ->
                                    predictRegressionAsync
                                        backend
                                        featureMap
                                        variationalForm
                                        parameters
                                        features
                                        shots
                                        valueRange
                                        cancellationToken
                            )

                        return
                            match predResult with
                            | Error e -> Error e
                            | Ok prediction ->
                                let error = prediction.Value - target
                                Ok(error * error)
                    })
                |> Task.WhenAll

            return
                match results |> Array.tryFind Result.isError with
                | Some(Error e) -> Error(QuantumError.ValidationError("Input", $"Loss computation failed: {e}"))
                | _ ->
                    let squaredErrors =
                        results
                        |> Array.choose (function
                            | Ok v -> Some v
                            | Error _ -> None)

                    Ok(Array.average squaredErrors)
        }

    /// Compute per-sample regression predictions v_j(θ) concurrently via Task.WhenAll,
    /// submitting each circuit through `gate`.
    let private computeRegressionPredictionsAsync
        (gate: JobGate)
        (backend: IQuantumBackend)
        (featureMap: FeatureMapType)
        (variationalForm: VariationalForm)
        (parameters: float array)
        (trainFeatures: float array array)
        (shots: int)
        (valueRange: float * float)
        (cancellationToken: CancellationToken)
        : Task<QuantumResult<float array>> =
        task {
            let! results =
                trainFeatures
                |> Array.map (fun features ->
                    task {
                        let! predResult =
                            gate.Run(
                                cancellationToken,
                                fun () ->
                                    predictRegressionAsync
                                        backend
                                        featureMap
                                        variationalForm
                                        parameters
                                        features
                                        shots
                                        valueRange
                                        cancellationToken
                            )

                        return predResult |> Result.map (fun prediction -> prediction.Value)
                    })
                |> Task.WhenAll

            return
                match results |> Array.tryFind Result.isError with
                | Some(Error e) -> Error e
                | _ ->
                    Ok(
                        results
                        |> Array.choose (function
                            | Ok v -> Some v
                            | Error _ -> None)
                    )
        }

    /// Compute gradient for regression using the parameter shift rule + chain rule.
    ///
    /// The parameter shift rule is exact only for the circuit EXPECTATION p(θ).
    /// The predicted value v = min + p·(max - min) is affine in p, so the shift rule
    /// applied to v is still exact:
    ///   ∂v/∂θ_i = (v(θ + π/2 e_i) - v(θ - π/2 e_i)) / 2
    /// The MSE loss is nonlinear in v, so the chain rule is required (per sample j):
    ///   L_j = (v_j - t_j)²  =>  dL_j/dv = 2 (v_j - t_j)
    ///   ∂L_j/∂θ_i = 2 (v_j(θ) - t_j) · (v_j(θ + π/2 e_i) - v_j(θ - π/2 e_i)) / 2
    /// The dataset gradient is the average of the per-sample gradients.
    ///
    /// Both per-parameter parallelism and +/- shift pairs run concurrently; one JobGate
    /// bounds the circuits in flight.
    let private computeRegressionGradientAsync
        (backend: IQuantumBackend)
        (featureMap: FeatureMapType)
        (variationalForm: VariationalForm)
        (parameters: float array)
        (trainFeatures: float array array)
        (trainTargets: float array)
        (shots: int)
        (valueRange: float * float)
        (cancellationToken: CancellationToken)
        : Task<QuantumResult<float array>> =
        task {
            let shift = Math.PI / 2.0
            let gate = JobGate backend

            // Unshifted per-sample predictions v_j(θ): loss-derivative factor of the chain rule
            let! basePredictionsResult =
                computeRegressionPredictionsAsync
                    gate
                    backend
                    featureMap
                    variationalForm
                    parameters
                    trainFeatures
                    shots
                    valueRange
                    cancellationToken

            match basePredictionsResult with
            | Error e -> return Error(QuantumError.ValidationError("Input", $"Gradient computation failed: {e}"))
            | Ok basePredictions ->
                let lossDerivatives =
                    Array.map2 (fun v t -> 2.0 * (v - t)) basePredictions trainTargets

                let computeParamGradientAsync i =
                    task {
                        let paramsPlus = Array.copy parameters
                        paramsPlus.[i] <- paramsPlus.[i] + shift

                        let paramsMinus = Array.copy parameters
                        paramsMinus.[i] <- paramsMinus.[i] - shift

                        // Compute +/- shift predictions in parallel
                        let! results =
                            Task.WhenAll
                                [|
                                    computeRegressionPredictionsAsync
                                        gate
                                        backend
                                        featureMap
                                        variationalForm
                                        paramsPlus
                                        trainFeatures
                                        shots
                                        valueRange
                                        cancellationToken
                                    computeRegressionPredictionsAsync
                                        gate
                                        backend
                                        featureMap
                                        variationalForm
                                        paramsMinus
                                        trainFeatures
                                        shots
                                        valueRange
                                        cancellationToken
                                |]

                        return
                            match results.[0], results.[1] with
                            | Ok predsPlus, Ok predsMinus ->
                                // Chain rule, averaged over samples
                                Array.init trainFeatures.Length (fun j ->
                                    lossDerivatives.[j] * (predsPlus.[j] - predsMinus.[j]) / 2.0)
                                |> Array.average
                                |> Ok
                            | Error e, _
                            | _, Error e ->
                                Error(
                                    QuantumError.ValidationError(
                                        "Input",
                                        $"Gradient computation failed for parameter {i}: {e}"
                                    )
                                )
                    }

                // Compute gradient for all parameters in parallel
                let! results =
                    parameters
                    |> Array.mapi (fun i _ -> computeParamGradientAsync i)
                    |> Task.WhenAll

                return
                    match results |> Array.tryFind Result.isError with
                    | Some(Error e) -> Error e
                    | _ ->
                        let gradients =
                            results
                            |> Array.choose (function
                                | Ok v -> Some v
                                | Error _ -> None)

                        Ok gradients
        }

    /// Calculate R² score for regression
    let private calculateRSquared (yTrue: float array) (yPred: float array) : float =
        let mean = yTrue |> Array.average
        let ssTot = yTrue |> Array.sumBy (fun y -> (y - mean) ** 2.0)
        let ssRes = Array.zip yTrue yPred |> Array.sumBy (fun (yt, yp) -> (yt - yp) ** 2.0)

        if ssTot = 0.0 then 1.0 else 1.0 - (ssRes / ssTot)

    /// Train VQC model for regression using gradient descent, asynchronously.
    /// Epochs run one after another; each epoch awaits its loss and gradient evaluations.
    let trainRegressionAsync
        (backend: IQuantumBackend)
        (featureMap: FeatureMapType)
        (variationalForm: VariationalForm)
        (initialParameters: float array)
        (trainFeatures: float array array)
        (trainTargets: float array)
        (config: TrainingConfig)
        (cancellationToken: CancellationToken)
        : Task<QuantumResult<RegressionTrainingResult>> =

        // Validate inputs
        if trainFeatures.Length <> trainTargets.Length then
            Task.FromResult(Error(QuantumError.ValidationError("Input", "Features and targets must have same length")))
        elif trainFeatures.Length = 0 then
            Task.FromResult(Error(QuantumError.Other "Training set cannot be empty"))
        else
            // Determine value range from training targets
            let minTarget = trainTargets |> Array.min
            let maxTarget = trainTargets |> Array.max
            let valueRange = (minTarget, maxTarget)

            // Initialize optimizer state for Adam
            let initialAdamState =
                match config.Optimizer with
                | Adam _ -> Some(AdamOptimizer.createState initialParameters.Length)
                | SGD -> None

            if config.Verbose then
                let optimizerName =
                    match config.Optimizer with
                    | SGD -> "SGD"
                    | Adam _ -> "Adam"

                let log = logInfo config.Logger
                log "Starting VQC Regression training..."
                log $"  Features: {trainFeatures.Length} samples"

                if trainFeatures.Length > 0 then
                    log $"            {trainFeatures.[0].Length} dimensions"

                log $"  Target range: [{minTarget:F2}, {maxTarget:F2}]"
                log $"  Parameters: {initialParameters.Length}"
                log $"  Optimizer: {optimizerName}"
                log $"  Learning rate: {config.LearningRate:F4}"
                log $"  Max epochs: {config.MaxEpochs}"
                log ""

            // One training epoch: the loss, then a parameter update unless the loss converged
            let epochAsync (state: TrainingState) : Task<QuantumResult<TrainingState>> =
                task {
                    // Compute current loss
                    match!
                        computeRegressionLossAsync
                            backend
                            featureMap
                            variationalForm
                            state.Parameters
                            trainFeatures
                            trainTargets
                            config.Shots
                            valueRange
                            cancellationToken
                    with
                    | Error e ->
                        return
                            Error(
                                QuantumError.ValidationError(
                                    "Input",
                                    $"Loss computation failed at epoch {state.Epoch}: {e}"
                                )
                            )
                    | Ok loss ->
                        let newLossHistory = loss :: state.LossHistory

                        if config.Verbose then
                            logInfo config.Logger $"Epoch %3d{state.Epoch}: MSE = {loss:F6}"

                        // Check convergence
                        let converged =
                            if newLossHistory.Length >= 2 then
                                let prevLoss = newLossHistory.[1]
                                let lossChange = abs (prevLoss - loss)

                                if lossChange < config.ConvergenceThreshold then
                                    if config.Verbose then
                                        logInfo
                                            config.Logger
                                            $"  Converged! (loss change: {lossChange:F6} < {config.ConvergenceThreshold:F6})"

                                    true
                                else
                                    false
                            else
                                false

                        if converged then
                            return
                                Ok
                                    { state with
                                        LossHistory = newLossHistory
                                        Converged = true
                                    }
                        else
                            // Compute gradients
                            match!
                                computeRegressionGradientAsync
                                    backend
                                    featureMap
                                    variationalForm
                                    state.Parameters
                                    trainFeatures
                                    trainTargets
                                    config.Shots
                                    valueRange
                                    cancellationToken
                            with
                            | Error e ->
                                return
                                    Error(
                                        QuantumError.ValidationError(
                                            "Input",
                                            $"Gradient computation failed at epoch {state.Epoch}: {e}"
                                        )
                                    )
                            | Ok gradient ->

                                // Update parameters using selected optimizer
                                match config.Optimizer, state.AdamState with
                                | SGD, _ ->
                                    // Simple gradient descent
                                    let newParams =
                                        Array.map2 (fun p g -> p - config.LearningRate * g) state.Parameters gradient

                                    return
                                        Ok
                                            { state with
                                                Parameters = newParams
                                                LossHistory = newLossHistory
                                                Epoch = state.Epoch + 1
                                            }

                                | Adam adamConfig, Some adamState ->
                                    // Adam optimizer
                                    match AdamOptimizer.update adamConfig adamState state.Parameters gradient with
                                    | Ok(newParams, newAdamState) ->
                                        return
                                            Ok
                                                { state with
                                                    Parameters = newParams
                                                    LossHistory = newLossHistory
                                                    Epoch = state.Epoch + 1
                                                    AdamState = Some newAdamState
                                                }
                                    | Error e ->
                                        return
                                            Error(
                                                QuantumError.ValidationError(
                                                    "Input",
                                                    $"Adam optimizer failed at epoch {state.Epoch}: {e}"
                                                )
                                            )

                                | Adam _, None ->
                                    return Error(QuantumError.Other "Adam optimizer state not initialized")
                }

            // Start training
            let initialState =
                {
                    Parameters = Array.copy initialParameters
                    LossHistory = []
                    Epoch = 0
                    Converged = false
                    AdamState = initialAdamState
                }

            task {
                // Epochs run in order until convergence, MaxEpochs or the first error
                let mutable state = initialState
                let mutable failure = None

                while failure.IsNone && state.Epoch < config.MaxEpochs && not state.Converged do
                    match! epochAsync state with
                    | Ok next -> state <- next
                    | Error e -> failure <- Some e

                match failure with
                | Some e -> return Error e
                | None ->
                    let finalState = state

                    // Compute final training metrics, one sample after another
                    let predictions = Array.zeroCreate<float> trainFeatures.Length

                    for i in 0 .. trainFeatures.Length - 1 do
                        let! prediction =
                            predictRegressionAsync
                                backend
                                featureMap
                                variationalForm
                                finalState.Parameters
                                trainFeatures.[i]
                                config.Shots
                                valueRange
                                cancellationToken

                        predictions.[i] <- prediction |> Result.map (fun pred -> pred.Value) |> Result.defaultValue nan // NaN signals prediction failure in metrics

                    let finalMSE =
                        Array.zip trainTargets predictions
                        |> Array.averageBy (fun (y, yp) -> (y - yp) ** 2.0)

                    let finalRSquared = calculateRSquared trainTargets predictions

                    if config.Verbose then
                        let log = logInfo config.Logger
                        log ""
                        log "Training complete!"
                        log $"  Epochs: {finalState.Epoch}"
                        log $"  Final MSE: {finalMSE:F6}"
                        log $"  R^2 score: {finalRSquared:F4}"
                        log $"  Converged: {finalState.Converged}"

                    return
                        Ok
                            {
                                Parameters = finalState.Parameters
                                LossHistory = List.rev finalState.LossHistory
                                Epochs = finalState.Epoch
                                TrainMSE = finalMSE
                                TrainRSquared = finalRSquared
                                Converged = finalState.Converged
                                ValueRange = valueRange
                            }
            }

    // ========================================================================
    // MULTI-CLASS CLASSIFICATION (One-vs-Rest)
    // ========================================================================

    /// Multi-class training result (one-vs-rest)
    type MultiClassTrainingResult =
        {
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
    type MultiClassPrediction =
        {
            /// Predicted class label
            Label: int

            /// Confidence score [0, 1]
            Confidence: float

            /// Probability distribution over all classes
            Probabilities: float array
        }

    /// Train multi-class VQC using one-vs-rest strategy, asynchronously.
    /// The binary classifiers are trained one after another.
    let trainMultiClassAsync
        (backend: IQuantumBackend)
        (featureMap: FeatureMapType)
        (variationalForm: VariationalForm)
        (initialParameters: float array)
        (trainFeatures: float array array)
        (trainLabels: int array)
        (config: TrainingConfig)
        (cancellationToken: CancellationToken)
        : Task<QuantumResult<MultiClassTrainingResult>> =

        // Validate inputs
        if trainFeatures.Length <> trainLabels.Length then
            Task.FromResult(Error(QuantumError.ValidationError("Input", "Features and labels must have same length")))
        elif trainFeatures.Length = 0 then
            Task.FromResult(Error(QuantumError.Other "Training set cannot be empty"))
        else
            // Get unique class labels
            let classLabels = trainLabels |> Array.distinct |> Array.sort
            let numClasses = classLabels.Length

            if numClasses < 2 then
                Task.FromResult(Error(QuantumError.Other "Need at least 2 classes for multi-class classification"))
            elif numClasses = 2 then
                // Binary classification - just train one classifier.
                // The binary trainer (cross-entropy loss and accuracy) assumes labels ∈ {0, 1},
                // so map the two class labels to 0/1 following ClassLabels ordering:
                // the classifier's output probability is P(class = classLabels.[1]).
                let binaryLabels =
                    trainLabels |> Array.map (fun label -> if label = classLabels.[1] then 1 else 0)

                quantumResultTask {
                    let! result =
                        trainAsync
                            backend
                            featureMap
                            variationalForm
                            initialParameters
                            trainFeatures
                            binaryLabels
                            config
                            cancellationToken

                    return
                        {
                            Classifiers = [| result |]
                            ClassLabels = classLabels
                            TrainAccuracy = result.TrainAccuracy
                            NumClasses = numClasses
                        }
                }
            else
                quantumResultTask {
                    if config.Verbose then
                        let log = logInfo config.Logger
                        log "Starting VQC multi-class training (one-vs-rest)..."
                        log $"  Classes: {numClasses}"
                        log $"  Samples: {trainFeatures.Length}"
                        log ""

                    // Train one binary classifier per class, one after another; the first
                    // failing classifier's error is the result
                    let classifierList = ResizeArray<TrainingResult>(numClasses)

                    for i in 0 .. numClasses - 1 do
                        let classLabel = classLabels.[i]

                        if config.Verbose then
                            logInfo config.Logger $"Training classifier {i + 1}/{numClasses} (class {classLabel})..."

                        // Create binary labels: 1 for current class, 0 for others
                        let binaryLabels =
                            trainLabels |> Array.map (fun label -> if label = classLabel then 1 else 0)

                        // Train binary classifier
                        let! result =
                            trainAsync
                                backend
                                featureMap
                                variationalForm
                                initialParameters
                                trainFeatures
                                binaryLabels
                                config
                                cancellationToken
                            |> mapErrorAsync (fun e ->
                                QuantumError.ValidationError("Input", $"Classifier for class {classLabel} failed: {e}"))

                        if config.Verbose then
                            logInfo config.Logger $"  Class {classLabel} accuracy: {result.TrainAccuracy:F4}"
                            logInfo config.Logger ""

                        classifierList.Add result

                    let classifiers = classifierList.ToArray()

                    // Compute overall training accuracy using one-vs-rest prediction, one
                    // sample and one classifier after another; the first failing
                    // prediction's error is the result
                    let! correctCount =
                        quantumResultTask {
                            let mutable correctCount = 0

                            for sampleIndex in 0 .. trainFeatures.Length - 1 do
                                // Get scores from all classifiers
                                let scores = ResizeArray<float>(classifiers.Length)

                                for classifier in classifiers do
                                    let! pred =
                                        predictAsync
                                            backend
                                            featureMap
                                            variationalForm
                                            classifier.Parameters
                                            trainFeatures.[sampleIndex]
                                            config.Shots
                                            cancellationToken

                                    scores.Add pred.Probability

                                // Predicted class is the one with highest score
                                let predictedClassIdx =
                                    scores |> Seq.mapi (fun idx s -> (idx, s)) |> Seq.maxBy snd |> fst

                                if classLabels.[predictedClassIdx] = trainLabels.[sampleIndex] then
                                    correctCount <- correctCount + 1

                            return correctCount
                        }
                        |> mapErrorAsync (fun err ->
                            QuantumError.ValidationError(
                                "Training",
                                $"Prediction failed during multi-class accuracy computation: {err}"
                            ))

                    let accuracy = float correctCount / float trainFeatures.Length

                    if config.Verbose then
                        logInfo config.Logger "Multi-class training complete!"
                        logInfo config.Logger $"  Overall accuracy: {accuracy:F4}"

                    return
                        {
                            Classifiers = classifiers
                            ClassLabels = classLabels
                            TrainAccuracy = accuracy
                            NumClasses = numClasses
                        }
                }

    /// Turns the score of every one-vs-rest classifier into a multi-class prediction
    let private multiClassPredictionFromScores
        (result: MultiClassTrainingResult)
        (scores: float array)
        : QuantumResult<MultiClassPrediction> =

        if result.NumClasses = 2 && scores.Length = 1 then
            // Two-class models store a single binary classifier whose score is
            // p = P(class = ClassLabels.[1]); derive both class probabilities from it.
            let p = scores.[0]
            let probabilities = [| 1.0 - p; p |]
            let predictedClassIdx = if p >= 0.5 then 1 else 0

            Ok
                {
                    Label = result.ClassLabels.[predictedClassIdx]
                    Confidence = probabilities.[predictedClassIdx]
                    Probabilities = probabilities
                }
        else
            // Normalize measurement probabilities directly
            // Scores are already probabilities from quantum measurements — softmax is inappropriate here
            let sumScores = scores |> Array.sum

            let probabilities =
                if sumScores > 0.0 then
                    scores |> Array.map (fun s -> s / sumScores)
                else
                    Array.create result.NumClasses (1.0 / float result.NumClasses)

            // Predicted class is the one with highest probability
            // Use Array.mapi and maxBy to avoid floating-point comparison issues
            let predictedClassIdx =
                probabilities |> Array.mapi (fun i p -> (i, p)) |> Array.maxBy snd |> fst

            let predictedLabel = result.ClassLabels.[predictedClassIdx]

            Ok
                {
                    Label = predictedLabel
                    Confidence = probabilities.[predictedClassIdx]
                    Probabilities = probabilities
                }

    /// Predict class for multi-class VQC (one-vs-rest), asynchronously
    let predictMultiClassAsync
        (backend: IQuantumBackend)
        (featureMap: FeatureMapType)
        (variationalForm: VariationalForm)
        (result: MultiClassTrainingResult)
        (features: float array)
        (shots: int)
        (cancellationToken: CancellationToken)
        : Task<QuantumResult<MultiClassPrediction>> =
        task {
            // Get scores from all classifiers; the first failing classifier's error is the result
            let! scoreResult =
                quantumResultTask {
                    let scores = ResizeArray<float>(result.Classifiers.Length)

                    for classifier in result.Classifiers do
                        let! pred =
                            predictAsync
                                backend
                                featureMap
                                variationalForm
                                classifier.Parameters
                                features
                                shots
                                cancellationToken

                        scores.Add pred.Probability

                    return scores.ToArray()
                }

            return
                match scoreResult with
                | Error e -> Error(QuantumError.ValidationError("Input", $"Multi-class prediction failed: {e}"))
                | Ok scores -> multiClassPredictionFromScores result scores
        }
