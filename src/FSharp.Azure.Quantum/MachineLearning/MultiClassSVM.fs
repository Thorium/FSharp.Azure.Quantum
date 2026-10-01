namespace FSharp.Azure.Quantum.MachineLearning

/// Multi-class Classification using One-vs-Rest (OvR) strategy with Quantum Kernel SVM.
///
/// Extends binary quantum kernel SVM to handle multi-class problems
/// by training K binary classifiers (one per class).
///
/// Strategy: For each class k, train binary classifier (class k vs. all others)

open System
open System.Threading
open System.Threading.Tasks
open FSharp.Azure.Quantum.Core
open FSharp.Azure.Quantum.Core.BackendAbstraction
open Microsoft.Extensions.Logging

module MultiClassSVM =

    // ========================================================================
    // TYPES
    // ========================================================================

    /// Multi-class SVM model (One-vs-Rest)
    type MultiClassModel =
        {
            /// Binary SVM models (one per class)
            BinaryModels: QuantumKernelSVM.SVMModel array

            /// Class labels in order
            ClassLabels: int array

            /// Number of classes
            NumClasses: int
        }

    /// Multi-class prediction result
    type MultiClassPrediction =
        {
            /// Predicted class label
            Label: int

            /// Decision values for all classes
            DecisionValues: float array

            /// Confidence (max decision value)
            Confidence: float
        }

    // ========================================================================
    // TRAINING (One-vs-Rest Strategy)
    // ========================================================================

    /// Convert multi-class labels to binary (class k vs. rest)
    let private createBinaryLabels (labels: int array) (targetClass: int) : int array =
        labels |> Array.map (fun label -> if label = targetClass then 1 else 0)

    /// Train multi-class SVM using One-vs-Rest strategy
    ///
    /// Parameters:
    ///   backend - Quantum backend
    ///   featureMap - Quantum feature map
    ///   trainData - Training feature vectors
    ///   trainLabels - Training labels (0, 1, 2, ..., K-1)
    ///   config - SVM configuration
    ///   shots - Number of shots for quantum kernel evaluation
    ///   cancellationToken - Cancels the kernel evaluations
    ///
    /// Returns:
    ///   Multi-class model or error message
    let trainAsync
        (backend: IQuantumBackend)
        (featureMap: FeatureMapType)
        (trainData: float array array)
        (trainLabels: int array)
        (config: QuantumKernelSVM.SVMConfig)
        (shots: int)
        (cancellationToken: CancellationToken)
        : Task<QuantumResult<MultiClassModel>> =

        // Validate inputs
        if trainData.Length = 0 then
            Task.FromResult(Error(QuantumError.Other "Training data cannot be empty"))
        elif trainLabels.Length = 0 then
            Task.FromResult(Error(QuantumError.Other "Training labels cannot be empty"))
        elif trainData.Length <> trainLabels.Length then
            Task.FromResult(
                Error(
                    QuantumError.ValidationError(
                        "Input",
                        $"Data and labels must have same length: {trainData.Length} vs {trainLabels.Length}"
                    )
                )
            )
        else
            // Extract unique class labels
            let uniqueClasses = trainLabels |> Array.distinct |> Array.sort

            let numClasses = uniqueClasses.Length

            if numClasses < 2 then
                Task.FromResult(
                    Error(QuantumError.ValidationError("Input", $"Need at least 2 classes, found {numClasses}"))
                )
            elif numClasses = 2 then
                Task.FromResult(Error(QuantumError.Other "For binary classification, use QuantumKernelSVM.trainAsync directly"))
            else
                quantumResultTask {
                    if config.Verbose then
                        logInfo config.Logger "Training One-vs-Rest multi-class SVM..."
                        logInfo config.Logger ($"  Classes: %d{numClasses} (%A{uniqueClasses})")

                    // Train one binary classifier per class, one after another; the first
                    // failing classifier's error is the result
                    let binaryModels = ResizeArray<QuantumKernelSVM.SVMModel>(numClasses)

                    for classLabel in uniqueClasses do
                        if config.Verbose then
                            logInfo config.Logger ($"  Training classifier for class %d{classLabel} vs rest...")

                        // Create binary labels (class vs. rest)
                        let binaryLabels = createBinaryLabels trainLabels classLabel

                        // Train binary SVM
                        let! binaryModel =
                            QuantumKernelSVM.trainAsync
                                backend
                                featureMap
                                trainData
                                binaryLabels
                                config
                                shots
                                cancellationToken
                            |> mapErrorAsync (fun e ->
                                QuantumError.OperationError(
                                    "MultiClassSVM training",
                                    $"Failed to train classifier for class {classLabel}: {e.Message}"
                                ))

                        binaryModels.Add binaryModel

                    if config.Verbose then
                        logInfo config.Logger "Multi-class training complete!"

                    return
                        {
                            BinaryModels = binaryModels.ToArray()
                            ClassLabels = uniqueClasses
                            NumClasses = numClasses
                        }
                }

    // ========================================================================
    // PREDICTION
    // ========================================================================

    /// Predict class label for a single sample
    ///
    /// Uses One-vs-Rest strategy: pick class with highest decision value
    ///
    /// Parameters:
    ///   backend - Quantum backend
    ///   model - Trained multi-class model
    ///   sample - Feature vector to classify
    ///   shots - Number of shots for kernel evaluation
    ///   cancellationToken - Cancels the kernel evaluations
    ///
    /// Returns:
    ///   Multi-class prediction with label and confidence
    let predictAsync
        (backend: IQuantumBackend)
        (model: MultiClassModel)
        (sample: float array)
        (shots: int)
        (cancellationToken: CancellationToken)
        : Task<QuantumResult<MultiClassPrediction>> =
        task {
            if shots <= 0 then
                return Error(QuantumError.ValidationError("Input", "Number of shots must be positive"))
            else
                // One binary classifier per class, in turn: each prediction already runs its
                // support-vector kernels concurrently (bounded on a sampling backend), so a
                // class-level fan-out on top would multiply the jobs in flight.
                let binaryPredictions = Array.zeroCreate model.BinaryModels.Length

                for i in 0 .. model.BinaryModels.Length - 1 do
                    let! prediction =
                        QuantumKernelSVM.predictAsync backend model.BinaryModels.[i] sample shots cancellationToken

                    binaryPredictions.[i] <- prediction

                return
                    binaryPredictions
                    |> QuantumKernelSVM.traverseResult
                    |> Result.map (fun predictions ->
                        // Extract decision values
                        let decisionValues = predictions |> Array.map (fun pred -> pred.DecisionValue)

                        // Find class with maximum decision value
                        let maxIndex =
                            decisionValues
                            |> Array.mapi (fun i value -> (i, value))
                            |> Array.maxBy snd
                            |> fst

                        let predictedClass = model.ClassLabels.[maxIndex]
                        let confidence = decisionValues.[maxIndex]

                        {
                            Label = predictedClass
                            DecisionValues = decisionValues
                            Confidence = confidence
                        })
        }

    // ========================================================================
    // EVALUATION
    // ========================================================================

    /// Evaluate multi-class model on a dataset, asynchronously
    ///
    /// Samples are predicted in order; the first failing sample's error is the result.
    ///
    /// Returns accuracy (fraction of correct predictions)
    let evaluateAsync
        (backend: IQuantumBackend)
        (model: MultiClassModel)
        (testData: float array array)
        (testLabels: int array)
        (shots: int)
        (cancellationToken: CancellationToken)
        : Task<QuantumResult<float>> =
        quantumResultTask {
            if testData.Length = 0 then
                return! Error(QuantumError.Other "Test data cannot be empty")
            elif testData.Length <> testLabels.Length then
                return! Error(QuantumError.ValidationError("Input", "Test data and labels must have same length"))
            else
                let mutable correctCount = 0

                for i in 0 .. testData.Length - 1 do
                    let! prediction = predictAsync backend model testData.[i] shots cancellationToken

                    if prediction.Label = testLabels.[i] then
                        correctCount <- correctCount + 1

                return float correctCount / float testData.Length
        }

    // ========================================================================
    // CONFUSION MATRIX & METRICS
    // ========================================================================

    /// Compute confusion matrix for multi-class classification
    ///
    /// Returns K×K matrix where entry (i,j) = # samples of class i predicted as class j
    let confusionMatrix (model: MultiClassModel) (predictions: int array) (trueLabels: int array) : int[,] =

        let K = model.NumClasses
        let matrix = Array2D.zeroCreate K K

        // Map class labels to indices
        let labelToIndex =
            model.ClassLabels |> Array.mapi (fun i label -> (label, i)) |> Map.ofArray

        // Populate confusion matrix. Pairs with a label the model never saw at
        // training time (possible when a singleton class ends up only in the test
        // split) have no row/column in the K×K matrix — skip them instead of
        // throwing KeyNotFoundException.
        Array.zip predictions trueLabels
        |> Array.iter (fun (pred, actual) ->
            match labelToIndex.TryFind pred, labelToIndex.TryFind actual with
            | Some predIdx, Some actualIdx -> matrix.[actualIdx, predIdx] <- matrix.[actualIdx, predIdx] + 1
            | _ -> ())

        matrix

    /// Compute per-class precision, recall, and F1-score
    ///
    /// Returns array of (precision, recall, f1) tuples (one per class)
    let perClassMetrics (confMatrix: int[,]) : (float * float * float) array =

        let K = Array2D.length1 confMatrix

        Array.init (max 0 K) (fun i ->
            // True positives: diagonal entry
            let tp = float confMatrix.[i, i]

            // False positives: sum of column i (excluding diagonal)
            let fp =
                [| 0 .. K - 1 |]
                |> Array.filter ((<>) i)
                |> Array.sumBy (fun j -> float confMatrix.[j, i])

            // False negatives: sum of row i (excluding diagonal)
            let fn =
                [| 0 .. K - 1 |]
                |> Array.filter ((<>) i)
                |> Array.sumBy (fun j -> float confMatrix.[i, j])

            // Precision: TP / (TP + FP)
            let precision = if tp + fp = 0.0 then 0.0 else tp / (tp + fp)

            // Recall: TP / (TP + FN)
            let recall = if tp + fn = 0.0 then 0.0 else tp / (tp + fn)

            // F1-score: harmonic mean of precision and recall
            let f1 =
                if precision + recall = 0.0 then
                    0.0
                else
                    2.0 * precision * recall / (precision + recall)

            (precision, recall, f1))

    /// Compute macro-averaged metrics (average across classes)
    let macroAverageMetrics (perClassMetrics: (float * float * float) array) : float * float * float =

        let precisions = perClassMetrics |> Array.map (fun (p, _, _) -> p)
        let recalls = perClassMetrics |> Array.map (fun (_, r, _) -> r)
        let f1Scores = perClassMetrics |> Array.map (fun (_, _, f1) -> f1)

        let macroPrecision = Array.average precisions
        let macroRecall = Array.average recalls
        let macroF1 = Array.average f1Scores

        (macroPrecision, macroRecall, macroF1)
