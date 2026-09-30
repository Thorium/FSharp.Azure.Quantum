---
layout: default
title: Business Problem Builders
---

# Business Problem Builders

**High-level APIs for common business applications**, built on the library's quantum algorithms and quantum machine learning.

## Overview

Business Problem Builders provide domain-specific computation expressions that hide the quantum details behind business terms. They use:

- **Quantum Machine Learning** - variational quantum classifiers (VQC) and quantum kernels for classification, regression, anomaly detection and similarity
- **Grover's Search** - oracle-based search for exact constraint and clique problems
- **QAOA** - approximate optimization over QUBO formulations (scheduling, covering, matching, packing)

Every builder runs its quantum path on the backend you give it, or on the local simulator (`LocalBackend`) when you don't (Quantum Drug Discovery is the exception: it requires a backend). Local simulation keeps problems small; each section notes the relevant limits.

**Target Audience:** Business analysts, data scientists and application developers who want to try quantum methods without quantum expertise.

All builders live under `FSharp.Azure.Quantum.Business`. Most return a `QuantumResult<'T>` (`Result<'T, QuantumError>`) from the computation expression itself: the work (training, search, optimization) runs when the expression is evaluated.

## Available Builders

### 1. AutoML - Automated Machine Learning
### 2. Binary Classification - Fraud Detection, Spam Filtering
### 3. Anomaly Detection - Security Threats, Quality Control
### 4. Predictive Modeling - Churn Prediction, Demand Forecasting
### 5. Similarity Search - Recommendations, Semantic Search
### 6. Quantum Drug Discovery - Virtual Screening, Compound Selection
### 7. Social Network Analyzer - Communities, Monitoring, Pairings
### 8. Constraint Scheduler - Constraint-Based Task-to-Resource Assignment
### 9. Coverage Optimizer - Set Coverage Optimization
### 10. Resource Pairing - Resource Pairing/Matching Optimization
### 11. Packing Optimizer - Bin Packing Optimization

---

## AutoML - Automated Machine Learning

### What is AutoML?

AutoML tries several model types, architectures and hyperparameter settings on your data and returns the best one:

**What AutoML Does:**
1. Looks at your labels to decide which problem types apply
2. Trains candidate models (binary classification, multi-class, regression, anomaly detection, optionally similarity search)
3. Tries the architectures you allow (`Quantum` and `Hybrid` by default)
4. Samples hyperparameters (learning rate, epochs, shots)
5. Scores every trial on a validation split and returns the best model with all trial results

**When to Use:**
- Quick prototyping ("just give me a working model")
- You don't know which model type to use
- Comparing approaches on the same data
- Establishing baselines

### API Reference

```fsharp
open FSharp.Azure.Quantum.Business
open FSharp.Azure.Quantum.Business.AutoML

// Training data: feature vectors and labels (float labels; 0.0/1.0 for binary problems)
let features = [|
    [| 0.1; 0.2 |]; [| 0.2; 0.1 |]; [| 0.15; 0.25 |]; [| 0.3; 0.2 |]; [| 0.2; 0.3 |]
    [| 0.9; 0.8 |]; [| 0.8; 0.9 |]; [| 0.85; 0.75 |]; [| 0.7; 0.8 |]; [| 0.8; 0.7 |]
|]
let labels = [| 0.0; 0.0; 0.0; 0.0; 0.0; 1.0; 1.0; 1.0; 1.0; 1.0 |]

// Minimal usage - "zero config ML"
let result = autoML {
    trainWith features labels
}

match result with
| Ok automlResult ->
    printfn "Best Model: %s" automlResult.BestModelType
    printfn "Architecture: %A" automlResult.BestArchitecture
    printfn "Validation Score: %.2f%%" (automlResult.Score * 100.0)
    printfn "Search Time: %.1fs" automlResult.TotalSearchTime.TotalSeconds

    // Use the best model for predictions
    let newData = [| 0.75; 0.85 |]
    match AutoML.predict newData automlResult with
    | Ok (BinaryPrediction p) -> printfn "Class %d (confidence %.2f)" p.Label p.Confidence
    | Ok other -> printfn "Prediction: %A" other
    | Error err -> eprintfn "Prediction failed: %s" err.Message

| Error err -> eprintfn "AutoML failed: %s" err.Message
```

`AutoML.predict` returns a `Prediction` union with one case per model type: `BinaryPrediction`, `CategoryPrediction`, `RegressionPrediction`, `AnomalyPrediction` and `SimilarityPrediction`. The trained model itself is in `automlResult.Model`.

### Configuration Options

```fsharp
// Advanced configuration
let tunedResult = autoML {
    trainWith features labels

    // Search space
    tryBinaryClassification true
    tryMultiClass 3              // Include 3-class classification
    tryRegression true
    tryAnomalyDetection false
    trySimilaritySearch false    // Off by default (expensive)
    tryArchitectures [Quantum; Hybrid]

    // Resource limits
    maxTrials 20
    maxTimeMinutes 10

    // Validation and reproducibility
    validationSplit 0.2
    randomSeed 42

    verbose true
}
```

| Operation | Description | Default |
|-----------|-------------|---------|
| `trainWith features labels` | Training data (`float[][]`, `float[]`) | required |
| `tryBinaryClassification`, `tryRegression`, `tryAnomalyDetection` | Include or exclude a model type | `true` |
| `trySimilaritySearch` | Include similarity search | `false` |
| `tryMultiClass n` | Include `n`-class classification | auto-detected from labels |
| `tryArchitectures` | Architectures to try: `Quantum`, `Hybrid`, `Classical` | `[Quantum; Hybrid]` |
| `maxTrials`, `maxTimeMinutes` | Search budget | 20 trials, no time limit |
| `validationSplit` | Fraction held out for scoring | 0.2 |
| `backend`, `randomSeed`, `verbose`, `saveModelTo`, `progressReporter`, `cancellationToken` | Execution options | `LocalBackend`, none |

Binary classification has no classical implementation, so `Classical` binary trials fail and are reported in `AllTrials`.

### AutoML Search Process

**1. Problem detection** - distinct label values decide whether binary or multi-class classification applies; regression and anomaly detection are tried when enabled.

**2. Architecture search**
- **Quantum**: VQC-based models
- **Hybrid**: quantum kernel + classical SVM

**3. Hyperparameters** - a small grid plus random samples of learning rate, max epochs, convergence threshold and shots.

**4. Validation** - a shuffled train/validation split; the score depends on the model type (accuracy for classifiers, R² for regression, balanced accuracy for anomaly detection). The best-scoring successful trial wins.

### Working Example

See complete example: [examples/AutoML/QuickPrototyping.fsx](https://github.com/Thorium/FSharp.Azure.Quantum/tree/main/examples/AutoML/QuickPrototyping.fsx)

---

## Binary Classification

### What is Binary Classification?

Classify data into two categories (e.g., fraud/legitimate, spam/ham, churn/retain).

**Business Applications:**
- Fraud detection (credit card transactions)
- Spam filtering (email, SMS)
- Customer churn prediction
- Quality control (defect detection)

### API Reference

```fsharp
open FSharp.Azure.Quantum.Business.BinaryClassifier

// Features: [amount, time_of_day, merchant_category, distance_from_home, frequency]
let trainFeatures = [|
    [| 50.0; 12.0; 2.0; 5.0; 3.0 |]; [| 75.0; 18.0; 3.0; 8.0; 2.0 |]; [| 30.0; 10.0; 1.0; 2.0; 4.0 |]
    [| 800.0; 3.0; 7.0; 150.0; 15.0 |]; [| 600.0; 2.0; 9.0; 200.0; 20.0 |]; [| 700.0; 4.0; 8.0; 120.0; 18.0 |]
|]
let trainLabels = [| 0; 0; 0; 1; 1; 1 |]  // int labels: 0 or 1

// Minimal configuration - smart defaults
let classifierResult = binaryClassification {
    trainWith trainFeatures trainLabels
}

match classifierResult with
| Ok classifier ->
    printfn "Training accuracy: %.2f%%" (classifier.Metadata.TrainingAccuracy * 100.0)

    // Classify new data
    let newTransaction = [| 600.0; 14.5; 7.0; 80.0; 12.0 |]

    match BinaryClassifier.predict newTransaction classifier with
    | Ok prediction ->
        printfn "Class: %d (confidence: %.2f%%)"
            prediction.Label
            (prediction.Confidence * 100.0)
    | Error err -> eprintfn "Error: %s" err.Message

| Error err -> eprintfn "Training failed: %s" err.Message
```

Note the argument order: `BinaryClassifier.predict sample classifier`. `BinaryClassifier.evaluate testFeatures testLabels classifier` returns accuracy, precision, recall and the confusion counts.

### Configuration Options

```fsharp
// Advanced configuration
let configuredResult = binaryClassification {
    trainWith trainFeatures trainLabels

    // Architecture selection
    architecture Quantum   // Quantum (VQC, default) or Hybrid (quantum kernel SVM)

    // Training configuration
    learningRate 0.1
    maxEpochs 50
    convergenceThreshold 0.001
    shots 1000

    // Bookkeeping
    note "Fraud model v1"
    verbose false
}
```

| Operation | Description | Default |
|-----------|-------------|---------|
| `trainWith features labels` | Training data (`float[][]`, `int[]` of 0/1) | required |
| `architecture` | `Quantum` (VQC), `Hybrid` (quantum kernel + SVM); `Classical` returns a `NotImplemented` error | `Quantum` |
| `learningRate`, `maxEpochs`, `convergenceThreshold` | VQC training | 0.01, 100, 0.001 |
| `shots` | Measurement shots | 1000 |
| `backend` | Quantum backend | `LocalBackend` |
| `saveModelTo`, `note`, `verbose`, `progressReporter`, `cancellationToken` | Bookkeeping | none |

The `Quantum` architecture uses one qubit per feature and accepts at most 8 features; reduce dimensionality first for wider data. Feature map (ZZ, depth 2) and ansatz (RealAmplitudes, depth 2) are fixed by the builder.

### Example: Fraud Detection

```fsharp
// Feature engineering for credit card transactions
// Features: [amount, time_of_day, merchant_category, distance_from_home, frequency]

let normalTransactions = [|
    [| 50.0; 12.0; 2.0; 5.0; 3.0 |]   // Small, daytime, grocery, nearby
    [| 75.0; 18.0; 3.0; 8.0; 2.0 |]   // Medium, evening, gas, nearby
    [| 20.0; 9.0; 1.0; 1.0; 5.0 |]
    [| 60.0; 13.0; 2.0; 4.0; 3.0 |]
|]

let fraudulentTransactions = [|
    [| 800.0; 3.0; 7.0; 150.0; 15.0 |]  // Large, late night, unusual, far away
    [| 600.0; 2.0; 9.0; 200.0; 20.0 |]  // Large, very late, unusual, very far
|]

let trainX = Array.append normalTransactions fraudulentTransactions
let trainY = Array.append (Array.create normalTransactions.Length 0) (Array.create fraudulentTransactions.Length 1)

// Train fraud detector
let fraudResult = binaryClassification {
    trainWith trainX trainY
    architecture Quantum
}

match fraudResult with
| Ok model ->
    // Score transactions with the trained model
    let scoreTransaction transaction =
        match BinaryClassifier.predict transaction model with
        | Ok pred when pred.IsPositive && pred.Confidence > 0.8 ->
            "BLOCK - High fraud risk"
        | Ok pred when pred.IsPositive && pred.Confidence > 0.5 ->
            "REVIEW - Medium fraud risk"
        | Ok _ ->
            "APPROVE - Low fraud risk"
        | Error _ ->
            "ERROR - Manual review required"

    // Score new transaction
    let newTx = [| 650.0; 2.5; 8.0; 180.0; 18.0 |]
    printfn "%s" (scoreTransaction newTx)

| Error err -> eprintfn "Training failed: %s" err.Message
```

There is no class-weight option; with imbalanced data, rebalance the training set (e.g. oversample the minority class) and tune the confidence cut-offs as above.

### Working Example

See complete example: [examples/BinaryClassification/FraudDetection.fsx](https://github.com/Thorium/FSharp.Azure.Quantum/tree/main/examples/BinaryClassification/FraudDetection.fsx)

---

## Anomaly Detection

### What is Anomaly Detection?

Identify unusual patterns that don't conform to expected behavior. The detector is trained on normal data only (a one-class quantum kernel model).

**Business Applications:**
- Security threat detection (network intrusion, unusual access)
- Equipment failure prediction (sensor anomalies)
- Quality control (manufacturing defects)
- Healthcare monitoring (abnormal vital signs)

### API Reference

```fsharp
open FSharp.Azure.Quantum.Business.AnomalyDetector

// Normal operation samples: [cpu_percent, memory_mb, network_kb_s, disk_io_ops]
// At least 10 samples are required.
let normalData = [|
    [| 45.0; 4000.0; 5000.0; 100.0 |]; [| 50.0; 4200.0; 5500.0; 110.0 |]
    [| 42.0; 3900.0; 4800.0; 95.0 |];  [| 48.0; 4100.0; 5200.0; 105.0 |]
    [| 46.0; 4050.0; 5100.0; 98.0 |];  [| 44.0; 3950.0; 4900.0; 102.0 |]
    [| 49.0; 4150.0; 5300.0; 108.0 |]; [| 47.0; 4000.0; 5050.0; 99.0 |]
    [| 43.0; 3980.0; 4950.0; 97.0 |];  [| 51.0; 4250.0; 5600.0; 112.0 |]
|]

// Train on normal data only
let detectorResult = anomalyDetection {
    trainOnNormalData normalData  // Only normal samples, no labels

    // How strict is "anomaly"?
    sensitivity Medium            // Low, Medium, High, VeryHigh
    contaminationRate 0.05        // Expected fraction of anomalies in the training data (0.0-0.5)
}

match detectorResult with
| Ok detector ->
    // Check new data point
    let newSample = [| 95.0; 8000.0; 15000.0; 200.0 |]  // High CPU usage

    match AnomalyDetector.check newSample detector with
    | Ok result ->
        if result.IsAnomaly then
            printfn "ANOMALY DETECTED"
            printfn "  Anomaly score: %.4f" result.AnomalyScore
            printfn "  Confidence: %.2f" result.Confidence
        else
            printfn "Normal behavior (score: %.4f)" result.AnomalyScore
    | Error err -> eprintfn "Detection error: %s" err.Message

| Error err -> eprintfn "Training failed: %s" err.Message
```

### Configuration Options

| Operation | Description | Default |
|-----------|-------------|---------|
| `trainOnNormalData data` | Normal samples (`float[][]`, at least 10) | required |
| `sensitivity` | `Low`, `Medium`, `High` or `VeryHigh` | `Medium` |
| `contaminationRate` | Expected anomaly fraction in the training data, 0.0-0.5 | 0.05 |
| `shots` | Measurement shots | 1000 |
| `backend` | Quantum backend | `LocalBackend` |
| `saveModelTo`, `note`, `verbose`, `progressReporter`, `cancellationToken` | Bookkeeping | none |

Other functions: `AnomalyDetector.checkBatch samples detector` (counts, rate and the most anomalous indices), `AnomalyDetector.explain sample detector trainingData` (per-feature contributions), `save`/`load`. The detector uses up to 8 qubits.

### Example: Network Intrusion Detection

```fsharp
let sendAlert (message: string) = eprintfn "ALERT: %s" message
let logWarning (message: string) = eprintfn "WARN: %s" message

let intrusionResult = anomalyDetection {
    trainOnNormalData normalData
    sensitivity High
}

match intrusionResult with
| Ok detector ->
    // Real-time monitoring
    let monitorMetrics currentMetrics =
        match AnomalyDetector.check currentMetrics detector with
        | Ok result when result.IsAnomaly && result.AnomalyScore > 0.9 ->
            sendAlert "CRITICAL: Possible intrusion detected"
        | Ok result when result.IsAnomaly ->
            logWarning $"Unusual activity (score: {result.AnomalyScore})"
        | Ok _ ->
            ()
        | Error err ->
            logWarning err.Message

    // Check current server state
    let current = [| 98.0; 7800.0; 25000.0; 500.0 |]  // Suspicious!
    monitorMetrics current

| Error err -> eprintfn "Setup failed: %s" err.Message
```

### Working Example

See complete example: [examples/AnomalyDetection/](https://github.com/Thorium/FSharp.Azure.Quantum/tree/main/examples/AnomalyDetection/)

---

## Predictive Modeling

### What is Predictive Modeling?

Forecast future outcomes based on historical patterns: continuous values (regression) or categories (multi-class classification).

**Business Applications:**
- Customer churn prediction
- Demand forecasting
- Sales predictions
- Resource capacity planning

### API Reference

```fsharp
open FSharp.Azure.Quantum.Business.PredictiveModel

// Features: [tenure_months, monthly_spend, support_calls, satisfaction]
let historicalData = [|
    [| 24.0; 75.0; 1.0; 8.5 |]; [| 6.0; 45.0; 5.0; 3.0 |]; [| 36.0; 120.0; 0.0; 9.0 |]
    [| 12.0; 60.0; 3.0; 5.5 |]; [| 3.0; 30.0; 6.0; 2.5 |]; [| 48.0; 150.0; 1.0; 8.0 |]
|]
let historicalRevenue = [| 1800.0; 270.0; 4320.0; 720.0; 90.0; 7200.0 |]

// Train a regression model
let revenueModel = predictiveModel {
    trainWith historicalData historicalRevenue
    problemType Regression
}

match revenueModel with
| Ok model ->
    printfn "Training R²: %.2f" model.Metadata.TrainingScore

    // Predict for a new customer (backend and shots default when None)
    let newCustomer = [| 6.0; 45.0; 3.0; 6.5 |]

    match PredictiveModel.predict newCustomer model None None with
    | Ok prediction ->
        printfn "Predicted revenue: %.2f" prediction.Value
        match prediction.ConfidenceInterval with
        | Some (lower, upper) -> printfn "Confidence interval: [%.2f, %.2f]" lower upper
        | None -> ()
    | Error err -> eprintfn "Error: %s" err.Message

| Error err -> eprintfn "Training failed: %s" err.Message
```

For a held-out set, `PredictiveModel.evaluateRegression testX testY model` returns R², MAE, MSE and RMSE; `evaluateMultiClass` returns accuracy, per-class precision/recall/F1 and a confusion matrix.

### Configuration Options

| Operation | Description | Default |
|-----------|-------------|---------|
| `trainWith features targets` | Training data (`float[][]`, `float[]`; class indices as floats for multi-class) | required |
| `problemType` | `Regression` or `MultiClass n` | `Regression` |
| `architecture` | `Quantum`, `Hybrid` or `Classical` | `Quantum` |
| `learningRate`, `maxEpochs`, `convergenceThreshold` | Training | 0.01, 100, 0.001 |
| `shots` | Measurement shots | 1000 |
| `backend` | Quantum backend | `LocalBackend` |
| `saveModelTo`, `note`, `verbose`, `progressReporter`, `cancellationToken` | Bookkeeping | none |

Regression always runs on the quantum backend (an HHL linear fit, falling back to VQC regression), whatever the architecture. For multi-class, `Hybrid` uses quantum kernel SVMs and `Quantum`/`Classical` use VQC. VQC models accept at most 5 features.

### Example: Customer Churn Prediction

Churn risk as three categories (0 = stays, 1 = at risk, 2 = leaves):

```fsharp
// Features: [tenure_months, monthly_spend, support_calls, satisfaction]
let customerHistory = [|
    [| 24.0; 75.0; 1.0; 8.5 |], 0.0
    [| 6.0; 45.0; 5.0; 3.0 |], 2.0
    [| 36.0; 120.0; 0.0; 9.0 |], 0.0
    [| 12.0; 60.0; 3.0; 5.5 |], 1.0
    [| 3.0; 30.0; 6.0; 2.5 |], 2.0
    [| 18.0; 70.0; 2.0; 6.0 |], 1.0
|]

let churnX = customerHistory |> Array.map fst
let churnY = customerHistory |> Array.map snd

let churnResult = predictiveModel {
    trainWith churnX churnY
    problemType (MultiClass 3)
    architecture Quantum
}

match churnResult with
| Ok model ->
    // Churn risk scoring for current customers
    let scoreChurnRisk customer =
        match PredictiveModel.predictCategory customer model None None with
        | Ok pred when pred.Category = 2 -> "HIGH RISK - Immediate retention campaign"
        | Ok pred when pred.Category = 1 -> "MEDIUM RISK - Monitor and engage"
        | Ok _ -> "LOW RISK - Routine engagement"
        | Error _ -> "ERROR - Manual review"

    // Score at-risk customer
    let atRiskCustomer = [| 8.0; 40.0; 4.0; 4.5 |]
    printfn "%s" (scoreChurnRisk atRiskCustomer)

| Error err -> eprintfn "Model training failed: %s" err.Message
```

### Working Example

See complete example: [examples/PredictiveModeling/CustomerChurnPrediction.fsx](https://github.com/Thorium/FSharp.Azure.Quantum/tree/main/examples/PredictiveModeling/CustomerChurnPrediction.fsx)

---

## Similarity Search

### What is Similarity Search?

Find items similar to a query item based on features.

**Business Applications:**
- Product recommendations
- Document similarity (semantic search)
- Customer segmentation
- Duplicate detection

### API Reference

The index is generic in the item type: you index `(item, featureVector)` pairs and get items back.

```fsharp
open FSharp.Azure.Quantum.Business.SimilaritySearch

// Product catalog: (product name, [category_id, price, rating, popularity])
let catalogItems = [|
    "Headphones", [| 1.0; 29.99; 4.5; 850.0 |]
    "Earbuds", [| 1.0; 49.99; 4.8; 1200.0 |]
    "Speaker", [| 1.0; 39.99; 4.4; 900.0 |]
    "Novel", [| 2.0; 15.99; 4.2; 300.0 |]
|]

// Build similarity index
let indexResult = similaritySearch {
    indexItems catalogItems
    similarityMetric Cosine   // Cosine (default), Euclidean or QuantumKernel
    threshold 0.5             // Minimum similarity to report (default 0.7)
}

match indexResult with
| Ok searchIndex ->
    // Find items similar to a query item
    let queryName, queryFeatures = catalogItems.[0]

    match SimilaritySearch.findSimilar queryName queryFeatures 3 searchIndex with  // Top 3
    | Ok results ->
        printfn "Similar items:"
        results.Matches |> Array.iter (fun m ->
            printfn "  %d. %s (similarity: %.2f%%)" m.Rank m.Item (m.Similarity * 100.0))
    | Error err -> eprintfn "Search error: %s" err.Message

| Error err -> eprintfn "Index build failed: %s" err.Message
```

Results exclude the query item itself and anything below the threshold.

### Configuration Options

| Operation | Description | Default |
|-----------|-------------|---------|
| `indexItems items` | `('T * float[])[]` to index | required |
| `similarityMetric` | `Cosine`, `Euclidean` or `QuantumKernel` | `Cosine` |
| `threshold` | Minimum similarity, 0.0-1.0 | 0.7 |
| `shots` | Shots per kernel evaluation (`QuantumKernel` only); on a cloud backend it must equal the backend's own shots per job, else an `Error` | 1000 |
| `backend` | Quantum backend (`QuantumKernel` only) | `LocalBackend` |
| `saveIndexTo`, `note`, `verbose`, `progressReporter`, `cancellationToken` | Bookkeeping | none |

`QuantumKernel` precomputes a quantum kernel matrix over all items, which costs a circuit run per item pair; keep such indexes small. Other functions: `findAllSimilar`, `findDuplicates threshold index`, `cluster numClusters maxIterations index`, `save`/`load`/`loadWithItems`.

### Example: Product Recommendations

```fsharp
let quantumIndex = similaritySearch {
    indexItems catalogItems
    similarityMetric QuantumKernel
    threshold 0.3
    shots 500
}

match quantumIndex with
| Ok index ->
    // User viewed a product - find similar items
    let viewedName, viewedFeatures = catalogItems.[1]

    match SimilaritySearch.findSimilar viewedName viewedFeatures 2 index with
    | Ok recommendations ->
        printfn "Customers who viewed %s also liked:" viewedName
        recommendations.Matches |> Array.iter (fun m ->
            printfn "  - %s (%.0f%% match)" m.Item (m.Similarity * 100.0))
    | Error err -> eprintfn "Recommendation error: %s" err.Message

| Error err -> eprintfn "Index failed: %s" err.Message
```

### Working Example

See complete example: [examples/SimilaritySearch/](https://github.com/Thorium/FSharp.Azure.Quantum/tree/main/examples/SimilaritySearch/)

---

## Quantum Drug Discovery

### What is Quantum Drug Discovery?

Virtual screening of molecular candidates using quantum machine learning and optimization algorithms.

**Business Applications:**
- Virtual screening for drug candidates
- Diverse compound library selection
- Compound prioritization

### Available Screening Methods

| Method | Description | Use Case |
|--------|-------------|----------|
| **QuantumKernelSVM** | Quantum kernel-based SVM classification (default) | Binary activity classification; needs activity labels |
| **VQCClassifier** | Variational Quantum Classifier | Trainable classification; needs activity labels |
| **QAOADiverseSelection** | QAOA-based diverse subset selection | Select diverse, high-value compounds within budget; labels optional |

### API Reference

```fsharp
open FSharp.Azure.Quantum.Business
open FSharp.Azure.Quantum.Backends
open FSharp.Azure.Quantum.Core.BackendAbstraction

// A backend is required for every screening method
let localBackend = LocalBackend.LocalBackend() :> IQuantumBackend

// Method 1: Quantum Kernel SVM (default)
let screeningResult = drugDiscovery {
    load_candidates_from_file "candidates.csv"  // CSV with SMILES and Label columns
    use_method QuantumKernelSVM
    use_feature_map ZZFeatureMap
    set_batch_size 20
    shots 1000
    backend localBackend
}

match screeningResult with
| Ok screening ->
    printfn "Method: %A" screening.Method
    printfn "Molecules Processed: %d" screening.MoleculesProcessed
    printfn "Result: %s" screening.Message
    for candidate in screening.RankedCandidates |> Array.truncate 5 do
        printfn "  %s (score %.3f)" candidate.Identifier candidate.Score
| Error err -> eprintfn "Screening failed: %s" err.Message
```

`ScreeningResult` holds `Message`, `Method`, `MoleculesProcessed`, `RankedCandidates` (each with `Index`, `Identifier`, `Score` and `PredictedActive`, best first) and the `Configuration` used.

### Configuration Options

```fsharp
// Full configuration example
let fullConfigResult = drugDiscovery {
    // Data source (choose one)
    load_candidates_from_file "molecules.sdf"
    // OR: load_candidates_from_provider sdfProvider
    // OR: load_candidates_from_provider_async asyncProvider

    // Optional target structure
    target_protein_from_pdb "target.pdb"

    // Screening method
    use_method VQCClassifier  // or QuantumKernelSVM, QAOADiverseSelection

    // Feature encoding
    use_feature_map ZZFeatureMap  // or PauliFeatureMap, ZFeatureMap

    // General settings
    set_batch_size 20         // Molecules per batch (default: 10)
    shots 1000                // Measurement shots (default: 100)
    backend localBackend      // Quantum backend (required)

    // VQC-specific settings (for VQCClassifier)
    vqc_layers 3              // Number of ansatz layers (default: 2)
    vqc_max_epochs 100        // Max training epochs (default: 50)

    // QAOA-specific settings (for QAOADiverseSelection)
    selection_budget 5.0      // Budget constraint (default: 10.0)
    diversity_weight 0.7      // Diversity bonus weight (default: 0.5)
}
```

(`sdfProvider` and `asyncProvider` stand for dataset providers such as `SdfFileDatasetProvider`.)

### Method 1: Quantum Kernel SVM

Classify molecules using quantum feature maps and support vector machines.

```fsharp
// Train a quantum kernel SVM for activity prediction
let svmResult = drugDiscovery {
    load_candidates_from_file "labeled_compounds.csv"  // Requires activity labels
    use_method QuantumKernelSVM
    use_feature_map ZZFeatureMap
    set_batch_size 50
    shots 1000
    backend localBackend
}

match svmResult with
| Ok r ->
    printfn "%s" r.Message
| Error e -> eprintfn "Error: %s" e.Message
```

**When to use:**
- Binary classification (active/inactive)
- Well-labeled training data available
- Small datasets (tens of molecules on a simulator)

### Method 2: VQC Classifier

Train a Variational Quantum Classifier for molecular activity prediction.

```fsharp
let vqcResult = drugDiscovery {
    load_candidates_from_file "compounds.sdf"
    use_method VQCClassifier
    use_feature_map ZZFeatureMap

    // VQC-specific configuration
    vqc_layers 3              // More layers = more expressivity
    vqc_max_epochs 100        // Training iterations

    set_batch_size 30
    shots 500
    backend localBackend
}

match vqcResult with
| Ok r ->
    printfn "Training complete!"
    printfn "%s" r.Message  // Shows accuracy, convergence
| Error e -> eprintfn "Training failed: %s" e.Message
```

**When to use:**
- Need a trainable quantum model
- Want to tune circuit depth

### Method 3: QAOA Diverse Selection

Select a diverse subset of high-value compounds within a budget using QAOA optimization.

```fsharp
let selectionResult = drugDiscovery {
    load_candidates_from_file "compound_library.sdf"
    use_method QAOADiverseSelection

    // QAOA-specific configuration
    selection_budget 10.0     // Max total cost of selected compounds
    diversity_weight 0.6      // Balance value vs diversity (0-1)

    set_batch_size 50         // Evaluate top 50 candidates
    shots 2000                // More shots for better optimization
    backend localBackend
}

match selectionResult with
| Ok r ->
    printfn "Selection complete!"
    printfn "%s" r.Message  // Shows selected compounds, total value, diversity
| Error e -> eprintfn "Selection failed: %s" e.Message
```

**When to use:**
- Building diverse screening libraries
- Budget-constrained compound selection
- Unlabeled candidate pools

### Example: Two-Stage Screening Pipeline

```fsharp
// Step 1: Initial classification with VQC
let classificationResult = drugDiscovery {
    load_candidates_from_file "hit_compounds.sdf"
    use_method VQCClassifier
    vqc_layers 2
    vqc_max_epochs 50
    set_batch_size 100
    backend localBackend
}

// Step 2: Select diverse subset from classified hits
let diverseResult = drugDiscovery {
    load_candidates_from_file "classified_hits.sdf"
    use_method QAOADiverseSelection
    selection_budget 20.0       // Select compounds worth total "cost" of 20
    diversity_weight 0.5        // Equal weight to value and diversity
    set_batch_size 50
    backend localBackend
}

match classificationResult, diverseResult with
| Ok cls, Ok sel ->
    printfn "Classification: %d molecules processed" cls.MoleculesProcessed
    printfn "Selection: %s" sel.Message
| Error e, _ -> eprintfn "Classification failed: %s" e.Message
| _, Error e -> eprintfn "Selection failed: %s" e.Message
```

### Supported File Formats

`load_candidates_from_file` picks the loader from the file extension:

| Format | Extension | Loader |
|--------|-----------|--------|
| SDF/MOL | .sdf, .mol | `SdfFileDatasetProvider` |
| PDB | .pdb | `PdbLigandDatasetProvider` |
| FCIDump | .fcidump | `FciDumpFileDatasetProvider` |
| CSV | .csv | `MolecularData.loadFromCsv` (columns `SMILES` and `Label`) |
| SMILES | any other extension | `MolecularData.loadFromSmilesList` (one SMILES per line) |

### Working Example

See complete example: [examples/DrugDiscovery/](https://github.com/Thorium/FSharp.Azure.Quantum/tree/main/examples/DrugDiscovery/)

---

## Social Network Analyzer

### What is Social Network Analysis?

Analyze the structure of a network of people and their connections.

**Business Applications:**
- Community detection (tight-knit groups in customer networks)
- Fraud ring detection (fully connected groups of suspicious actors)
- Monitoring (smallest set of people that sees every connection)
- Mentoring and partner matching (1:1 pairings)

### Analysis Modes

| Operation | Finds | Algorithm |
|-----------|-------|-----------|
| `findCommunities n` | Groups of exactly `n` people who all know each other (cliques) | Grover search only |
| `findLargestCommunity` | The largest fully connected group (maximum clique) | QAOA by default; Grover with `useGrover` |
| `findMonitorSet` | Smallest set of people covering every connection (vertex cover) | QAOA only |
| `findPairings` | 1:1 pairings between connected people (matching) | QAOA only |

Choosing an unsupported algorithm for a mode (`useQaoa` with `findCommunities`, `useGrover` with `findMonitorSet` or `findPairings`) returns a validation error. Grover is exact search; QAOA is approximate optimization.

### API Reference

```fsharp
open FSharp.Azure.Quantum.Business.SocialNetworkAnalyzer

let result = socialNetwork {
    people ["Alice"; "Bob"; "Carol"; "Dave"]

    connection "Alice" "Bob"
    connection "Bob" "Carol"
    connection "Carol" "Alice"
    connection "Carol" "Dave"

    findLargestCommunity
}

match result with
| Ok analysis ->
    printfn "%s" analysis.Message
    for community in analysis.Communities do
        printfn "Community: %A (strength %.2f)" community.Members community.Strength
| Error err -> eprintfn "Analysis failed: %s" err.Message
```

### Configuration Options

| Operation | Description | Default |
|-----------|-------------|---------|
| `person id` / `people ids` | Add people (at most 100) | required |
| `connection p1 p2` / `connections pairs` | Add connections | none |
| `findCommunities n`, `findLargestCommunity`, `findMonitorSet`, `findPairings` | Analysis mode | see below |
| `useGrover` / `useQaoa` | Force an algorithm | automatic per mode |
| `shots` | Measurement shots | 1000 |
| `backend` | Quantum backend | `LocalBackend` |

**Result — `SocialNetworkResult`:** `Communities` (each with `Members`, `Strength` - 1.0 for a clique - and `InternalConnections`), `MonitorSet`, `Pairings` (each with `Person1`, `Person2`, `Weight`), `TotalPeople`, `TotalConnections` and `Message`. Only the fields for the chosen mode are filled.

### Example: Monitoring and Pairing

```fsharp
let team = ["Ann"; "Ben"; "Cat"; "Dan"; "Eve"]
let links = [("Ann", "Ben"); ("Ben", "Cat"); ("Cat", "Dan"); ("Dan", "Eve"); ("Eve", "Ann")]

// Who must we observe to see every communication channel?
let monitorResult = socialNetwork {
    people team
    connections links
    findMonitorSet
}

// Best 1:1 mentoring pairs among connected people
let pairingResult = socialNetwork {
    people team
    connections links
    findPairings
    shots 2000
}

match monitorResult, pairingResult with
| Ok monitor, Ok pairs ->
    printfn "Monitor set: %A" monitor.MonitorSet
    for p in pairs.Pairings do
        printfn "Pair: %s - %s" p.Person1 p.Person2
| Error e, _
| _, Error e -> eprintfn "Analysis failed: %s" e.Message
```

### Working Example

See complete example: [examples/GraphAnalytics/SocialNetworkAnalyzer_Example.fsx](https://github.com/Thorium/FSharp.Azure.Quantum/tree/main/examples/GraphAnalytics/SocialNetworkAnalyzer_Example.fsx)

---

## Constraint Scheduler

### What is Constraint Scheduling?

Assign tasks to resources (servers, people, machines, rooms) subject to hard constraints (which tasks conflict, which resource a task needs) and soft preferences, at minimum cost.

This builder assigns tasks to resources; it has **no time dimension**. Temporal ordering (`precedence`) is rejected with a `NotImplemented` error. For start times and dependencies, use the [Task Scheduling builder](TaskScheduling-API.md).

**Business Applications:**
- Workforce assignment (who covers which duty)
- Resource allocation (tasks to servers with capacity limits)
- Container or VM placement with resource costs
- Room assignment

### API Reference

```fsharp
open FSharp.Azure.Quantum.Business.ConstraintScheduler

let result = constraintScheduler {
    // Tasks
    task "Deploy API"
    task "Run Tests"
    task "Build Docs"

    // Resources with a cost
    resource "FastServer" 10.0
    resource "TestServer" 2.0

    // Hard constraints (must satisfy)
    conflict "Deploy API" "Run Tests"      // Not on the same resource
    require "Run Tests" "TestServer"       // Must run on this resource

    // Soft constraints (preferences, weighted)
    prefer "Build Docs" "TestServer" 0.8

    // Optimization goal
    optimizeFor MinimizeCost               // or MaximizeSatisfaction, Balanced (default)
    maxBudget 50.0
}

match result with
| Ok schedulingResult ->
    printfn "%s" schedulingResult.Message
    match schedulingResult.BestSchedule with
    | Some schedule ->
        printfn "Feasible: %b, total cost: %.2f" schedule.IsFeasible schedule.TotalCost
        for a in schedule.Assignments do
            printfn "  %s -> %s (cost %.2f)" a.Task a.Resource a.Cost
    | None -> printfn "No schedule found"
| Error err -> eprintfn "Scheduling failed: %s" err.Message
```

### Configuration Options

| Operation | Description |
|-----------|-------------|
| `task id` / `tasks ids` | Tasks to assign (at most 50) |
| `resource id cost` | Resource with a cost and unlimited capacity |
| `resourceWithCapacity id cost capacity` | Resource that can take at most `capacity` tasks |
| `conflict t1 t2` | Hard: the two tasks may not share a resource |
| `require task resource` | Hard: the task must use that resource |
| `precedence before after` | Not supported (returns an error; see above) |
| `prefer task resource weight` | Soft: prefer this assignment |
| `optimizeFor goal` | `MinimizeCost`, `MaximizeSatisfaction` or `Balanced` (default) |
| `maxBudget amount` | Budget limit |
| `useGrover` / `useQaoa` | Force an algorithm; by default QAOA is used when any resource has a capacity, Grover otherwise |
| `shots` | Measurement shots (default 1000) |
| `backend` | Quantum backend (default `LocalBackend`) |

**Result — `SchedulingResult`:** `BestSchedule: Schedule option` and `Message`. A `Schedule` has `Assignments` (each `Task`, `Resource`, `Cost`), `TotalCost`, hard/soft constraint counts (`HardConstraintsSatisfied`, `TotalHardConstraints`, `SoftConstraintsSatisfied`, `TotalSoftConstraints`) and `IsFeasible`.

The search space is tasks × resources binary variables, so keep problems small on a simulator.

### Example: Shift Duty Assignment

```fsharp
let rosterResult = constraintScheduler {
    tasks ["MorningTill"; "AfternoonTill"; "NightSecurity"]

    resourceWithCapacity "Alice" 20.0 1   // Cost per assignment, at most 1 duty
    resourceWithCapacity "Bob" 18.0 1
    resourceWithCapacity "Carol" 25.0 2

    conflict "MorningTill" "AfternoonTill"
    require "NightSecurity" "Bob"
    prefer "MorningTill" "Alice" 0.8

    optimizeFor MinimizeCost
}

match rosterResult with
| Ok r ->
    match r.BestSchedule with
    | Some schedule ->
        schedule.Assignments
        |> List.groupBy (fun a -> a.Resource)
        |> List.iter (fun (person, duties) ->
            printfn "  %s: %s" person (duties |> List.map (fun d -> d.Task) |> String.concat ", "))
    | None -> printfn "%s" r.Message
| Error err -> eprintfn "Scheduling failed: %s" err.Message
```

### Working Example

See complete example: [examples/JobScheduling/ConstraintScheduler_Example.fsx](https://github.com/Thorium/FSharp.Azure.Quantum/tree/main/examples/JobScheduling/ConstraintScheduler_Example.fsx)

---

## Coverage Optimizer

### What is Set Coverage Optimization?

Find the minimum-cost collection of options that covers all required elements. This is a fundamental combinatorial optimization problem with broad applications in facility placement, service deployment, and resource allocation.

**Business Applications:**
- Shift coverage (select minimum shifts to cover all required time slots)
- Facility location (minimum facilities to serve all demand zones)
- Service coverage (select service packages covering all customer needs)
- Sensor placement (minimum sensors for full network monitoring)
- Test suite minimization (minimum tests to cover all code paths)

### API Reference

The `coverageOptimizer` computation expression builder is in `FSharp.Azure.Quantum.Business.CoverageOptimizer`.

**CE Operations:**

| Operation | Parameters | Description |
|---|---|---|
| `element` | `elementIndex: int` | Add an element to the universe (expands universe size if needed) |
| `universeSize` | `size: int` | Set the universe size directly |
| `option` | `id: string, coveredElements: int list, cost: float` | Add a coverage option with its cost |
| `backend` | `backend: IQuantumBackend` | Set the quantum backend (optional; defaults to `LocalBackend`) |
| `shots` | `shots: int` | Number of measurement shots (default: 1000) |

**Result type — `CoverageResult`:**

| Field | Type | Description |
|---|---|---|
| `SelectedOptions` | `CoverageOption list` | Coverage options selected by the optimizer |
| `TotalCost` | `float` | Total cost of selected options |
| `ElementsCovered` | `int` | Number of distinct elements covered |
| `TotalElements` | `int` | Total elements that need coverage |
| `IsComplete` | `bool` | Whether all elements are covered |
| `Message` | `string` | Human-readable execution summary |

**Minimal example:**

```fsharp
open FSharp.Azure.Quantum.Business.CoverageOptimizer

let coverage = coverageOptimizer {
    element 0   // Time slot 0
    element 1   // Time slot 1
    element 2   // Time slot 2

    option "MorningShift" [0; 1] 25.0   // Covers slots 0,1 at cost $25
    option "AfternoonShift" [1; 2] 20.0 // Covers slots 1,2 at cost $20
    option "FullDay" [0; 1; 2] 40.0     // Covers all at cost $40

    backend localBackend
}
```

### Working Example

See the complete runnable script with CLI options, JSON/CSV output, and detailed reporting:
[examples/CoverageOptimizer/CoverageOptimizer_Example.fsx](https://github.com/Thorium/FSharp.Azure.Quantum/tree/main/examples/CoverageOptimizer/CoverageOptimizer_Example.fsx)

---

## Resource Pairing

### What is Resource Pairing?

Match participants into pairs based on compatibility scores to maximize total matching quality. This solves maximum weight matching problems common in workforce management, mentoring programs, and logistics.

**Business Applications:**
- Recruiting (match candidates to positions by skill fit)
- Mentor-mentee pairing (optimize mentorship compatibility)
- Donor-recipient matching (organ transplant, blood donation)
- Trading (match buyers with sellers for optimal deals)
- Ride-sharing matching (drivers to passengers)

### API Reference

The `resourcePairing` computation expression builder is in `FSharp.Azure.Quantum.Business.ResourcePairing`.

**CE Operations:**

| Operation | Parameters | Description |
|---|---|---|
| `participant` | `id: string` | Add a single participant |
| `participants` | `ids: string list` | Add multiple participants at once |
| `compatibility` | `p1: string, p2: string, weight: float` | Set compatibility score between two participants (higher = better) |
| `backend` | `backend: IQuantumBackend` | Set the quantum backend (optional; defaults to `LocalBackend`) |
| `shots` | `shots: int` | Number of measurement shots (default: 1000) |

**Result type — `PairingResult`:**

| Field | Type | Description |
|---|---|---|
| `Pairings` | `Pairing list` | Pairings found (each has `Participant1`, `Participant2`, `Weight`) |
| `TotalScore` | `float` | Total compatibility score across all pairings |
| `ParticipantsPaired` | `int` | Number of participants that were paired |
| `TotalParticipants` | `int` | Total number of participants |
| `IsValid` | `bool` | Whether matching is valid (no participant in multiple pairs) |
| `Message` | `string` | Human-readable execution summary |

**Minimal example:**

```fsharp
open FSharp.Azure.Quantum.Business.ResourcePairing

let pairing = resourcePairing {
    participant "Alice"
    participant "Bob"
    participant "Carol"

    compatibility "Alice" "Bob" 0.9    // High compatibility
    compatibility "Alice" "Carol" 0.5  // Medium compatibility
    compatibility "Bob" "Carol" 0.7    // Good compatibility

    backend localBackend
}
```

### Working Example

See the complete runnable script with CLI options, JSON/CSV output, and detailed reporting:
[examples/ResourcePairing/ResourcePairing_Example.fsx](https://github.com/Thorium/FSharp.Azure.Quantum/tree/main/examples/ResourcePairing/ResourcePairing_Example.fsx)

---

## Packing Optimizer

### What is Bin Packing Optimization?

Pack items of varying sizes into containers (bins) to minimize the number of containers used. This is a classic combinatorial optimization problem with significant cost implications in logistics and infrastructure.

**Business Applications:**
- Container loading (shipping/logistics)
- Cloud VM placement (virtual machine bin packing)
- Warehouse storage allocation (optimize shelf/pallet usage)
- Memory allocation (data partitioning across storage nodes)
- Cutting stock problems (minimize material waste)

### API Reference

The `packingOptimizer` computation expression builder is in `FSharp.Azure.Quantum.Business.PackingOptimizer`.

**CE Operations:**

| Operation | Parameters | Description |
|---|---|---|
| `item` | `id: string, size: float` | Add an item to pack with its size/weight (must be positive and fit in a bin) |
| `containerCapacity` | `capacity: float` | Set the bin/container capacity (all bins have the same capacity) |
| `backend` | `backend: IQuantumBackend` | Set the quantum backend (optional; defaults to `LocalBackend`) |
| `shots` | `shots: int` | Number of measurement shots (default: 1000) |

**Result type — `PackingResult`:**

| Field | Type | Description |
|---|---|---|
| `Assignments` | `BinAssignment list` | Item-to-bin assignments (each has `Item: PackingItem`, `BinIndex: int`) |
| `BinsUsed` | `int` | Number of bins used |
| `IsValid` | `bool` | Whether all items are assigned and no bin exceeds capacity |
| `TotalItems` | `int` | Total items in the problem |
| `ItemsAssigned` | `int` | Items successfully assigned |
| `Message` | `string` | Human-readable execution summary |

**Minimal example:**

```fsharp
open FSharp.Azure.Quantum.Business.PackingOptimizer

let packing = packingOptimizer {
    containerCapacity 100.0

    item "Crate-A" 45.0
    item "Crate-B" 35.0
    item "Crate-C" 25.0
    item "Crate-D" 50.0

    backend localBackend
}
```

### Working Example

See the complete runnable script with CLI options, JSON/CSV output, and detailed reporting:
[examples/PackingOptimizer/PackingOptimizer_Example.fsx](https://github.com/Thorium/FSharp.Azure.Quantum/tree/main/examples/PackingOptimizer/PackingOptimizer_Example.fsx)

---

## Architecture Selection Guide

The machine-learning builders take an `architecture` (AutoML takes a list via `tryArchitectures`):

| Builder | `Quantum` | `Hybrid` | `Classical` |
|---------|-----------|----------|-------------|
| Binary Classification | VQC (default), at most 8 features | Quantum kernel + SVM | Not implemented (returns an error) |
| Predictive Modeling | Regression: HHL/VQC. Multi-class: VQC, at most 5 features | Regression: same as Quantum. Multi-class: quantum kernel SVMs | Accepted; runs the same quantum paths |
| AutoML | Tried by default | Tried by default | Opt-in; binary trials fail |

Anomaly detection and similarity search don't take an architecture: anomaly detection always uses a quantum kernel, and similarity search uses whichever `similarityMetric` you choose.

**Practical guidance:**
- Everything runs on the local simulator unless you pass a cloud backend, so training time grows quickly with features and samples; start with a few features and tens of samples.
- `Hybrid` (quantum kernel + SVM) evaluates a kernel circuit for every pair of training samples; `Quantum` (VQC) runs circuits for every sample in every epoch. Which is faster depends on the data size and epoch count.
- For a classical baseline, train a conventional model with your usual ML library and compare on the same held-out data; these builders do not provide classical models.
- No quantum advantage is claimed for these models; treat them as experiments and measure them against a baseline.

## Troubleshooting

### Common Issues

#### 1. "supports at most N features"

**Cause:** VQC models use one qubit per feature (8 for binary classification, 5 for predictive VQC models).

**Solutions:**
- Reduce dimensionality (feature selection, PCA) before training

#### 2. Poor Accuracy

**Symptoms:** Model performs poorly on both training and validation data

**Solutions:**
- Scale features to similar ranges
- Add informative features (better feature engineering)
- Collect more training data; check label quality
- Try AutoML to compare model types and architectures

#### 3. Overfitting (High Training, Low Validation Accuracy)

**Solutions:**
- Increase training data
- Reduce `maxEpochs` or loosen `convergenceThreshold`
- Hold out more data with `validationSplit` (AutoML) or `evaluate` on a test set

#### 4. Slow Training

**Symptoms:** Training takes a long time

**Solutions:**
- Use fewer shots (e.g. 100 instead of 1000) while experimenting
- Reduce `maxEpochs`
- Use `Hybrid` instead of `Quantum`
- Reduce features and samples
- For AutoML, set `maxTrials` and `maxTimeMinutes`

#### 5. Class Imbalance (Fraud Detection)

**Symptoms:** Model always predicts the majority class

**Solutions:**
- Oversample the minority class (or undersample the majority) before training
- Adjust the decision rule using `Confidence` (lower the cut-off for rare events)
- Judge models by precision and recall (`BinaryClassifier.evaluate`), not accuracy alone

## See Also

- [Quantum Machine Learning](quantum-machine-learning) - VQC, Quantum Kernels, Feature Maps
- [Task Scheduling](TaskScheduling-API.md) - Time-based scheduling with dependencies
- [Getting Started Guide](getting-started) - Installation and setup
- [API Reference](api-reference) - Complete API documentation
- [Working Examples](https://github.com/Thorium/FSharp.Azure.Quantum/tree/main/examples/) - Complete code examples
