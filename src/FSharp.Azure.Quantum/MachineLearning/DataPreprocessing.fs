namespace FSharp.Azure.Quantum.MachineLearning

/// Data Preprocessing Utilities for Quantum Machine Learning.
///
/// Provides essential data preprocessing functions for preparing datasets
/// for quantum machine learning models (VQC, Quantum Kernels, etc.)

open FSharp.Azure.Quantum.Core
open System

module DataPreprocessing =
    
    // ========================================================================
    // NORMALIZATION
    // ========================================================================
    
    /// Normalize features to [0, 1] range using min-max scaling
    ///
    /// Returns: (normalized_data, min_values, max_values)
    /// The min/max values can be used to normalize test data consistently
    let normalizeMinMax (data: float array array) : float array array * float array * float array =
        if data.Length = 0 then
            ([||], [||], [||])
        else
            let numFeatures = data.[0].Length
            
            // Compute min and max for each feature
            let minMaxPairs =
                Array.init numFeatures (fun featureIdx ->
                    let featureValues = data |> Array.map (fun sample -> sample.[featureIdx])
                    (Array.min featureValues, Array.max featureValues))
            
            let minValues = minMaxPairs |> Array.map fst
            let maxValues = minMaxPairs |> Array.map snd
            
            // Normalize each sample
            let normalizedData =
                data
                |> Array.map (fun sample ->
                    sample
                    |> Array.mapi (fun i value ->
                        let range = maxValues.[i] - minValues.[i]
                        if range = 0.0 then
                            0.5  // If all values same, normalize to 0.5
                        else
                            (value - minValues.[i]) / range))
            
            (normalizedData, minValues, maxValues)
    
    /// Apply min-max normalization to new data using existing min/max values
    let applyMinMaxNormalization
        (data: float array array)
        (minValues: float array)
        (maxValues: float array)
        : float array array =
        
        data
        |> Array.map (fun sample ->
            sample
            |> Array.mapi (fun i value ->
                let range = maxValues.[i] - minValues.[i]
                if range = 0.0 then
                    0.5
                else
                    (value - minValues.[i]) / range))
    
    /// Normalize features to specific range [minVal, maxVal]
    let normalizeToRange
        (data: float array array)
        (minVal: float)
        (maxVal: float)
        : float array array * float array * float array =
        
        let (normalized01, mins, maxs) = normalizeMinMax data
        
        // Scale from [0, 1] to [minVal, maxVal]
        let scaledData =
            normalized01
            |> Array.map (fun sample ->
                sample |> Array.map (fun v -> minVal + v * (maxVal - minVal)))
        
        (scaledData, mins, maxs)
    
    // ========================================================================
    // STANDARDIZATION
    // ========================================================================
    
    /// Standardize features to zero mean and unit variance (z-score normalization)
    ///
    /// Returns: (standardized_data, mean_values, std_values)
    let standardize (data: float array array) : float array array * float array * float array =
        if data.Length = 0 then
            ([||], [||], [||])
        else
            let numFeatures = data.[0].Length
            let numSamples = float data.Length
            
            // Compute mean for each feature
            let meanValues =
                [| 0 .. numFeatures - 1 |]
                |> Array.map (fun featureIdx ->
                    data
                    |> Array.map (fun sample -> sample.[featureIdx])
                    |> Array.average)
            
            // Compute standard deviation for each feature
            let stdValues =
                [| 0 .. numFeatures - 1 |]
                |> Array.map (fun featureIdx ->
                    let mean = meanValues.[featureIdx]
                    let variance =
                        data
                        |> Array.map (fun sample ->
                            let diff = sample.[featureIdx] - mean
                            diff * diff)
                        |> Array.average
                    sqrt variance)
            
            // Standardize each sample
            let standardizedData =
                data
                |> Array.map (fun sample ->
                    sample
                    |> Array.mapi (fun i value ->
                        if stdValues.[i] = 0.0 then
                            0.0  // If std is 0, all values are same, center at 0
                        else
                            (value - meanValues.[i]) / stdValues.[i]))
            
            (standardizedData, meanValues, stdValues)
    
    /// Apply standardization to new data using existing mean/std values
    let applyStandardization
        (data: float array array)
        (meanValues: float array)
        (stdValues: float array)
        : float array array =
        
        data
        |> Array.map (fun sample ->
            sample
            |> Array.mapi (fun i value ->
                if stdValues.[i] = 0.0 then
                    0.0
                else
                    (value - meanValues.[i]) / stdValues.[i]))
    
    // ========================================================================
    // TRAIN/TEST SPLIT
    // ========================================================================
    
    /// Split data into training and test sets
    ///
    /// Parameters:
    ///   data - Feature vectors
    ///   labels - Corresponding labels
    ///   testSize - Fraction of data for test set (0.0 to 1.0)
    ///   seed - Optional random seed for reproducibility
    ///
    /// Returns: ((train_data, train_labels), (test_data, test_labels))
    let trainTestSplit
        (data: float array array)
        (labels: int array)
        (testSize: float)
        (seed: int option)
        : (float array array * int array) * (float array array * int array) =
        
        if data.Length = 0 || labels.Length = 0 then
            ((data, labels), ([||], [||]))
        elif data.Length <> labels.Length then
            failwith "Data and labels must have same length"
        elif testSize < 0.0 || testSize > 1.0 then
            failwith "testSize must be between 0.0 and 1.0"
        else
            let n = data.Length
            let numTest = int (float n * testSize)
            let numTrain = n - numTest
            
            // Create random generator
            let rng =
                match seed with
                | Some s -> Random(s)
                | None -> Random()
            
            // Create shuffled indices using functional Fisher-Yates shuffle
            let indices =
                [| 0 .. n - 1 |]
                |> Array.fold (fun (arr : int array, remaining) _ ->
                    if remaining = 0 then (arr, 0)
                    else
                        let j = rng.Next(remaining + 1)
                        let temp = arr.[remaining]
                        arr.[remaining] <- arr.[j]
                        arr.[j] <- temp
                        (arr, remaining - 1)
                ) ([| 0 .. n - 1 |], n - 1)
                |> fst
            
            // Split indices
            let trainIndices = indices.[0 .. numTrain - 1]
            let testIndices = indices.[numTrain .. n - 1]
            
            // Extract train and test data
            let trainData = trainIndices |> Array.map (fun i -> data.[i])
            let trainLabels = trainIndices |> Array.map (fun i -> labels.[i])
            let testData = testIndices |> Array.map (fun i -> data.[i])
            let testLabels = testIndices |> Array.map (fun i -> labels.[i])
            
            ((trainData, trainLabels), (testData, testLabels))
    
    // ========================================================================
    // K-FOLD CROSS-VALIDATION
    // ========================================================================
    
    /// Generate K-fold cross-validation splits
    ///
    /// Parameters:
    ///   data - Feature vectors
    ///   labels - Corresponding labels
    ///   k - Number of folds
    ///   seed - Optional random seed for reproducibility
    ///
    /// Returns: Array of k (train, test) splits
    let kFoldSplit
        (data: float array array)
        (labels: int array)
        (k: int)
        (seed: int option)
        : ((float array array * int array) * (float array array * int array)) array =
        
        if data.Length = 0 || labels.Length = 0 then
            [||]
        elif data.Length <> labels.Length then
            failwith "Data and labels must have same length"
        elif k < 2 then
            failwith "k must be at least 2"
        elif k > data.Length then
            failwith "k cannot be larger than number of samples"
        else
            let n = data.Length
            
            // Create random generator and shuffle indices
            let rng =
                match seed with
                | Some s -> Random(s)
                | None -> Random()
            
            // Fisher-Yates shuffle using functional approach
            let indices =
                [| 0 .. n - 1 |]
                |> Array.fold (fun (arr : int array, remaining) _ ->
                    if remaining = 0 then (arr, 0)
                    else
                        let j = rng.Next(remaining + 1)
                        let temp = arr.[remaining]
                        arr.[remaining] <- arr.[j]
                        arr.[j] <- temp
                        (arr, remaining - 1)
                ) ([| 0 .. n - 1 |], n - 1)
                |> fst
            
            // Create k folds
            let foldSize = n / k
            let remainder = n % k
            
            [| 0 .. k - 1 |]
            |> Array.map (fun foldIdx ->
                // Calculate fold boundaries
                let foldStart = foldIdx * foldSize + min foldIdx remainder
                let foldEnd = foldStart + foldSize + (if foldIdx < remainder then 1 else 0)
                
                // Test indices for this fold
                let testIndices = indices.[foldStart .. foldEnd - 1]
                
                // Train indices are all others
                let trainIndices =
                    indices
                    |> Array.mapi (fun i idx -> (i, idx))
                    |> Array.filter (fun (i, _) -> i < foldStart || i >= foldEnd)
                    |> Array.map snd
                
                // Extract data
                let trainData = trainIndices |> Array.map (fun i -> data.[i])
                let trainLabels = trainIndices |> Array.map (fun i -> labels.[i])
                let testData = testIndices |> Array.map (fun i -> data.[i])
                let testLabels = testIndices |> Array.map (fun i -> labels.[i])
                
                ((trainData, trainLabels), (testData, testLabels)))
    
    // ========================================================================
    // STRATIFIED SPLIT (maintains class distribution)
    // ========================================================================
    
    /// Stratified train/test split - maintains class distribution in splits
    ///
    /// Ensures each class is proportionally represented in train and test sets
    let stratifiedTrainTestSplit
        (data: float array array)
        (labels: int array)
        (testSize: float)
        (seed: int option)
        : (float array array * int array) * (float array array * int array) =
        
        if data.Length = 0 || labels.Length = 0 then
            ((data, labels), ([||], [||]))
        elif data.Length <> labels.Length then
            failwith "Data and labels must have same length"
        elif testSize < 0.0 || testSize > 1.0 then
            failwith "testSize must be between 0.0 and 1.0"
        else
            // Group indices by class
            let classSamples =
                labels
                |> Array.mapi (fun i label -> (label, i))
                |> Array.groupBy fst
                |> Array.map (fun (classLabel, samples) ->
                    (classLabel, samples |> Array.map snd))
            
            let rng =
                match seed with
                | Some s -> Random(s)
                | None -> Random()
            
            // Split each class separately, collecting train/test indices
            let splits =
                classSamples
                |> Array.map (fun (_classLabel, classIndices) ->
                    let n = classIndices.Length
                    let numTest = max 1 (int (float n * testSize))  // At least 1 sample per class in test
                    let numTrain = n - numTest
                    
                    // Shuffle class indices using functional Fisher-Yates
                    let shuffled =
                        Array.copy classIndices
                        |> Array.fold (fun (arr : int array, remaining) _ ->
                            if remaining = 0 then (arr, 0)
                            else
                                let j = rng.Next(remaining + 1)
                                let temp = arr.[remaining]
                                arr.[remaining] <- arr.[j]
                                arr.[j] <- temp
                                (arr, remaining - 1)
                        ) (Array.copy classIndices, n - 1)
                        |> fst
                    
                    (shuffled.[0 .. numTrain - 1], shuffled.[numTrain .. n - 1]))
            
            let trainIndices = splits |> Array.collect fst
            let testIndices = splits |> Array.collect snd
            
            // Extract data
            let trainData = trainIndices |> Array.map (fun i -> data.[i])
            let trainLabels = trainIndices |> Array.map (fun i -> labels.[i])
            let testData = testIndices |> Array.map (fun i -> data.[i])
            let testLabels = testIndices |> Array.map (fun i -> labels.[i])
            
            ((trainData, trainLabels), (testData, testLabels))
