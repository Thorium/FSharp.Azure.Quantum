namespace FSharp.Azure.Quantum.MachineLearning

/// Multi-class Classification using One-vs-Rest (OvR) strategy with Quantum Kernel SVM.
///
/// Extends binary quantum kernel SVM to handle multi-class problems
/// by training K binary classifiers (one per class).
///
/// Strategy: For each class k, train binary classifier (class k vs. all others)

open System
open FSharp.Azure.Quantum.Core
open FSharp.Azure.Quantum.Core.BackendAbstraction
open Microsoft.Extensions.Logging

module MultiClassSVM =
    
    // ========================================================================
    // TYPES
    // ========================================================================
    
    /// Multi-class SVM model (One-vs-Rest)
    type MultiClassModel = {
        /// Binary SVM models (one per class)
        BinaryModels: QuantumKernelSVM.SVMModel array
        
        /// Class labels in order
        ClassLabels: int array
        
        /// Number of classes
        NumClasses: int
    }
    
    /// Multi-class prediction result
    type MultiClassPrediction = {
        /// Predicted class label
        Label: int
        
        /// Decision values for all classes
        DecisionValues: float array
        
        /// Confidence (max decision value)
        Confidence: float
    }
    
    // ========================================================================
    // HELPER FUNCTIONS
    // ========================================================================
    
    /// Helper to convert Result array to array Result
    let private traverseResult (results: Result<'a, 'e> array) : Result<'a array, 'e> =
        let folder state item =
            match state, item with
            | Ok acc, Ok value -> Ok (Array.append acc [| value |])
            | Error e, _ -> Error e
            | _, Error e -> Error e
        
        Array.fold folder (Ok [||]) results
    
    // ========================================================================
    // TRAINING (One-vs-Rest Strategy)
    // ========================================================================
    
    /// Convert multi-class labels to binary (class k vs. rest)
    let private createBinaryLabels (labels: int array) (targetClass: int) : int array =
        labels
        |> Array.map (fun label -> if label = targetClass then 1 else 0)
    
    /// Train multi-class SVM using One-vs-Rest strategy
    ///
    /// Parameters:
    ///   backend - Quantum backend
    ///   featureMap - Quantum feature map
    ///   trainData - Training feature vectors
    ///   trainLabels - Training labels (0, 1, 2, ..., K-1)
    ///   config - SVM configuration
    ///   shots - Number of shots for quantum kernel evaluation
    ///
    /// Returns:
    ///   Multi-class model or error message
    let train
        (backend: IQuantumBackend)
        (featureMap: FeatureMapType)
        (trainData: float array array)
        (trainLabels: int array)
        (config: QuantumKernelSVM.SVMConfig)
        (shots: int)
        : QuantumResult<MultiClassModel> =
        
        // Validate inputs
        if trainData.Length = 0 then
            Error (QuantumError.Other "Training data cannot be empty")
        elif trainLabels.Length = 0 then
            Error (QuantumError.Other "Training labels cannot be empty")
        elif trainData.Length <> trainLabels.Length then
            Error (QuantumError.ValidationError ("Input", $"Data and labels must have same length: {trainData.Length} vs {trainLabels.Length}"))
        else
            // Extract unique class labels
            let uniqueClasses =
                trainLabels
                |> Array.distinct
                |> Array.sort
            
            let numClasses = uniqueClasses.Length
            
            if numClasses < 2 then
                Error (QuantumError.ValidationError ("Input", $"Need at least 2 classes, found {numClasses}"))
            elif numClasses = 2 then
                Error (QuantumError.Other "For binary classification, use QuantumKernelSVM.train directly")
            else
                if config.Verbose then
                    logInfo config.Logger "Training One-vs-Rest multi-class SVM..."
                    logInfo config.Logger (sprintf "  Classes: %d (%A)" numClasses uniqueClasses)
                
                // Train one binary classifier per class (functional)
                uniqueClasses
                |> Array.map (fun classLabel ->
                    if config.Verbose then
                        logInfo config.Logger (sprintf "  Training classifier for class %d vs rest..." classLabel)
                    
                    // Create binary labels (class vs. rest)
                    let binaryLabels = createBinaryLabels trainLabels classLabel
                    
                    // Train binary SVM
                    QuantumKernelSVM.train backend featureMap trainData binaryLabels config shots
                    |> Result.mapError (fun e -> QuantumError.OperationError ("MultiClassSVM training", $"Failed to train classifier for class {classLabel}: {e.Message}")))
                |> traverseResult
                |> Result.map (fun binaryModels ->
                    if config.Verbose then
                        logInfo config.Logger "Multi-class training complete!"
                    
                    {
                        BinaryModels = binaryModels
                        ClassLabels = uniqueClasses
                        NumClasses = numClasses
                    })
    
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
    ///
    /// Returns:
    ///   Multi-class prediction with label and confidence
    let predict
        (backend: IQuantumBackend)
        (model: MultiClassModel)
        (sample: float array)
        (shots: int)
        : QuantumResult<MultiClassPrediction> =
        
        if shots <= 0 then
            Error (QuantumError.ValidationError ("Input", "Number of shots must be positive"))
        else
            // Get predictions from all binary classifiers (functional)
            model.BinaryModels
            |> Array.map (fun binaryModel ->
                QuantumKernelSVM.predict backend binaryModel sample shots)
            |> traverseResult
            |> Result.map (fun predictions ->
                // Extract decision values
                let decisionValues =
                    predictions
                    |> Array.map (fun pred -> pred.DecisionValue)
                
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
    
    // ========================================================================
    // EVALUATION
    // ========================================================================
    
    /// Evaluate multi-class model on a dataset
    ///
    /// Returns accuracy (fraction of correct predictions)
    let evaluate
        (backend: IQuantumBackend)
        (model: MultiClassModel)
        (testData: float array array)
        (testLabels: int array)
        (shots: int)
        : QuantumResult<float> =
        
        if testData.Length = 0 then
            Error (QuantumError.Other "Test data cannot be empty")
        elif testData.Length <> testLabels.Length then
            Error (QuantumError.ValidationError ("Input", "Test data and labels must have same length"))
        else
            // Get predictions for all test samples (functional)
            testData
            |> Array.map (fun sample -> predict backend model sample shots)
            |> traverseResult
            |> Result.map (fun predictions ->
                // Extract predicted labels and compute accuracy
                let correctCount =
                    predictions
                    |> Array.map (fun pred -> pred.Label)
                    |> Array.zip testLabels
                    |> Array.filter (fun (actual, pred) -> pred = actual)
                    |> Array.length
                
                float correctCount / float testData.Length)
    
    // ========================================================================
    // CONFUSION MATRIX & METRICS
    // ========================================================================
    
    /// Compute confusion matrix for multi-class classification
    ///
    /// Returns K×K matrix where entry (i,j) = # samples of class i predicted as class j
    let confusionMatrix
        (model: MultiClassModel)
        (predictions: int array)
        (trueLabels: int array)
        : int[,] =
        
        let K = model.NumClasses
        let matrix = Array2D.zeroCreate K K
        
        // Map class labels to indices
        let labelToIndex =
            model.ClassLabels
            |> Array.mapi (fun i label -> (label, i))
            |> Map.ofArray
        
        // Populate confusion matrix
        Array.zip predictions trueLabels
        |> Array.iter (fun (pred, actual) ->
            let predIdx = labelToIndex.[pred]
            let actualIdx = labelToIndex.[actual]
            matrix.[actualIdx, predIdx] <- matrix.[actualIdx, predIdx] + 1)
        
        matrix
    
    /// Compute per-class precision, recall, and F1-score
    ///
    /// Returns array of (precision, recall, f1) tuples (one per class)
    let perClassMetrics
        (confMatrix: int[,])
        : (float * float * float) array =
        
        let K = Array2D.length1 confMatrix
        
        [| 0 .. K - 1 |]
        |> Array.map (fun i ->
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
            let precision =
                if tp + fp = 0.0 then 0.0
                else tp / (tp + fp)
            
            // Recall: TP / (TP + FN)
            let recall =
                if tp + fn = 0.0 then 0.0
                else tp / (tp + fn)
            
            // F1-score: harmonic mean of precision and recall
            let f1 =
                if precision + recall = 0.0 then 0.0
                else 2.0 * precision * recall / (precision + recall)
            
            (precision, recall, f1))
    
    /// Compute macro-averaged metrics (average across classes)
    let macroAverageMetrics
        (perClassMetrics: (float * float * float) array)
        : float * float * float =
        
        let precisions = perClassMetrics |> Array.map (fun (p, _, _) -> p)
        let recalls = perClassMetrics |> Array.map (fun (_, r, _) -> r)
        let f1Scores = perClassMetrics |> Array.map (fun (_, _, f1) -> f1)
        
        let macroPrecision = Array.average precisions
        let macroRecall = Array.average recalls
        let macroF1 = Array.average f1Scores
        
        (macroPrecision, macroRecall, macroF1)
