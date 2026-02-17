---
layout: default
title: Business Problem Builders
---

# Business Problem Builders

**High-level APIs for common business applications** powered by quantum algorithms and quantum machine learning.

## Overview

Business Problem Builders provide domain-specific APIs that hide quantum complexity while delivering quantum-enhanced solutions for real-world business problems. These builders leverage:

- **Quantum Machine Learning** - VQC, quantum kernels for classification and regression
- **Grover's Search** - Quantum search for pattern matching and optimization
- **Hybrid Approaches** - Automatic classical/quantum routing based on problem characteristics

**Target Audience:** Business analysts, data scientists, application developers who want quantum benefits without quantum expertise.

## Available Builders

### 1. AutoML - Automated Machine Learning
### 2. Binary Classification - Fraud Detection, Spam Filtering
### 3. Anomaly Detection - Security Threats, Quality Control
### 4. Predictive Modeling - Churn Prediction, Demand Forecasting
### 5. Similarity Search - Recommendations, Semantic Search
### 6. Quantum Drug Discovery - Virtual Screening, Compound Selection

---

## AutoML - Automated Machine Learning

### What is AutoML?

AutoML automatically selects the best machine learning approach for your data:

**What AutoML Does:**
1. Analyzes your dataset characteristics
2. Tries multiple approaches (binary classification, regression, anomaly detection)
3. Tests quantum and classical architectures
4. Tunes hyperparameters automatically
5. Returns best model with performance metrics

**When to Use:**
- Quick prototyping ("just give me a working model")
- You don't know which ML algorithm to use
- Model selection (compare all approaches)
- Establishing performance baselines

### API Reference

```fsharp
open FSharp.Azure.Quantum.Business
open FSharp.Azure.Quantum.Business.AutoML

// Minimal usage - "zero config ML"
let result = autoML {
    trainWith features labels
}

match result with
| Ok automlResult ->
    printfn "Best Model: %s" automlResult.BestModelType
    printfn "Architecture: %A" automlResult.BestArchitecture
    printfn "Validation Score: %.2f%%" (automlResult.Score * 100.0)
    printfn "Training Time: %.1fs" automlResult.TotalSearchTime.TotalSeconds
    
    // Use best model for predictions
    let prediction = AutoML.predict automlResult.BestModel newData
    
| Error err -> eprintfn "AutoML failed: %A" err
```

### Configuration Options

```fsharp
// Advanced configuration
let result = autoML {
    trainWith trainFeatures trainLabels
    
    // Search space
    tryArchitectures [Quantum; Classical; Hybrid]
    tryTaskTypes [BinaryClassification; MultiClass; Regression; AnomalyDetection]
    
    // Resource limits
    maxTrials 50
    maxTime (TimeSpan.FromMinutes 10.0)
    
    // Validation
    validationSplit 0.2
    metric Accuracy  // or Precision, Recall, F1Score, RMSE
    
    // Optimization
    enableEarlyStop true
    patience 5
}
```

### AutoML Search Process

**1. Data Analysis**
- Feature count and dimensionality
- Label distribution (classification vs regression)
- Dataset size (determines architecture candidates)

**2. Architecture Search**
- **Quantum**: VQC with various feature maps and variational forms
- **Classical**: SVM, decision trees, logistic regression
- **Hybrid**: Quantum kernels with classical SVM

**3. Hyperparameter Tuning**
- Learning rate (0.001 - 0.5)
- Depth (1-5 for quantum circuits)
- Shots (100-10000 for quantum)
- Regularization parameters

**4. Validation**
- Cross-validation or train/validation split
- Multiple metrics computed (accuracy, precision, recall, F1)
- Best model selected by primary metric

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
- Disease diagnosis (positive/negative)
- Quality control (defect detection)

### API Reference

```fsharp
open FSharp.Azure.Quantum.Business.BinaryClassifier

// Minimal configuration - smart defaults
let result = binaryClassification {
    trainWith trainFeatures trainLabels
}

match result with
| Ok classifier ->
    printfn "Training accuracy: %.2f%%" (classifier.Metadata.TrainingAccuracy * 100.0)
    
    // Classify new data
    let newTransaction = [| 600.0; 14.5; 7.0; 80.0; 12.0 |]
    
    match BinaryClassifier.predict classifier newTransaction with
    | Ok prediction ->
        printfn "Class: %d (confidence: %.2f%%)" 
            prediction.Class 
            (prediction.Confidence * 100.0)
    | Error err -> eprintfn "Error: %s" err.Message
    
| Error err -> eprintfn "Training failed: %s" err.Message
```

### Configuration Options

```fsharp
// Advanced configuration
let result = binaryClassification {
    trainWith trainFeatures trainLabels
    
    // Architecture selection
    architecture Quantum  // or Classical, Hybrid, AutoSelect
    
    // Quantum-specific (when architecture = Quantum)
    featureMap (ZZFeatureMap 2)
    variationalForm (RealAmplitudes 2)
    
    // Training configuration
    learningRate 0.1
    maxEpochs 50
    convergenceThreshold 0.001
    shots 1000
    
    // Optimizer
    optimizer Adam  // or SGD
    
    // Validation
    validationSplit 0.2
    
    // Class imbalance handling
    classWeights [| 1.0; 3.0 |]  // Penalize misclassifying class 1 more
}
```

### Example: Fraud Detection

```fsharp
// Feature engineering for credit card transactions
// Features: [amount, time_of_day, merchant_category, distance_from_home, frequency]

let normalTransactions = [|
    [| 50.0; 12.0; 2.0; 5.0; 3.0 |]   // Small, daytime, grocery, nearby
    [| 75.0; 18.0; 3.0; 8.0; 2.0 |]   // Medium, evening, gas, nearby
    // ... 40 normal transactions
|]

let fraudulentTransactions = [|
    [| 800.0; 3.0; 7.0; 150.0; 15.0 |]  // Large, late night, unusual, far away
    [| 600.0; 2.0; 9.0; 200.0; 20.0 |]  // Large, very late, unusual, very far
    // ... 10 fraudulent transactions
|]

let trainX = Array.append normalTransactions fraudulentTransactions
let trainY = Array.append (Array.create 40 0) (Array.create 10 1)

// Train fraud detector
let result = binaryClassification {
    trainWith trainX trainY
    architecture Quantum
    classWeights [| 1.0; 5.0 |]  // False negatives (missing fraud) are costly!
}

match result with
| Ok model ->
    // Deploy model for real-time fraud scoring
    let scoreTransaction transaction =
        match BinaryClassifier.predict model transaction with
        | Ok pred when pred.Class = 1 && pred.Confidence > 0.8 ->
            "BLOCK - High fraud risk"
        | Ok pred when pred.Class = 1 && pred.Confidence > 0.5 ->
            "REVIEW - Medium fraud risk"
        | Ok pred -> 
            "APPROVE - Low fraud risk"
        | Error _ -> 
            "ERROR - Manual review required"
    
    // Score new transaction
    let newTx = [| 650.0; 2.5; 8.0; 180.0; 18.0 |]
    printfn "%s" (scoreTransaction newTx)
    
| Error err -> eprintfn "Training failed: %s" err.Message
```

### Working Example

See complete example: [examples/BinaryClassification/FraudDetection.fsx](https://github.com/Thorium/FSharp.Azure.Quantum/tree/main/examples/BinaryClassification/FraudDetection.fsx)

---

## Anomaly Detection

### What is Anomaly Detection?

Identify unusual patterns that don't conform to expected behavior.

**Business Applications:**
- Security threat detection (network intrusion, unusual access)
- Equipment failure prediction (sensor anomalies)
- Quality control (manufacturing defects)
- Healthcare monitoring (abnormal vital signs)

### API Reference

```fsharp
open FSharp.Azure.Quantum.Business.AnomalyDetector

// Train on normal data only
let result = anomalyDetection {
    trainWith normalData  // Only normal samples, no labels
    
    // Sensitivity (how strict is "anomaly"?)
    threshold 0.05  // 5% most unusual are anomalies
    
    // Feature engineering
    features ["cpu_usage"; "memory"; "network_io"; "disk_io"]
    
    // Detection method
    method QuantumKernel  // or Classical, Hybrid
}

match result with
| Ok detector ->
    // Check new data point
    let newSample = [| 95.0; 8000.0; 15000.0; 200.0 |]  // High CPU usage
    
    match AnomalyDetector.detect detector newSample with
    | Ok result ->
        if result.IsAnomaly then
            printfn "ANOMALY DETECTED"
            printfn "  Anomaly score: %.4f" result.AnomalyScore
            printfn "  Distance from normal: %.2f sigma" result.DeviationSigma
        else
            printfn "Normal behavior (score: %.4f)" result.AnomalyScore
    | Error err -> eprintfn "Detection error: %s" err.Message
    
| Error err -> eprintfn "Training failed: %s" err.Message
```

### Configuration Options

```fsharp
let result = anomalyDetection {
    trainWith normalData
    
    // Detection threshold
    threshold 0.05           // Top 5% unusual = anomaly
    contamination 0.02       // Expected % of anomalies in training data
    
    // Method selection
    method QuantumKernel     // Quantum feature space for similarity
    
    // Quantum configuration
    featureMap (ZZFeatureMap 2)
    shots 1000
    
    // Sensitivity tuning
    sensitivity High         // Low, Medium, High, VeryHigh
}
```

### Example: Network Intrusion Detection

```fsharp
// Monitor server metrics for unusual activity
let normalServerMetrics = [|
    [| 45.0; 4000.0; 5000.0; 100.0 |]   // Normal: CPU, Memory, Network, Disk
    [| 50.0; 4200.0; 5500.0; 110.0 |]
    [| 42.0; 3900.0; 4800.0; 95.0 |]
    // ... 100 normal operation samples
|]

let result = anomalyDetection {
    trainWith normalServerMetrics
    features ["cpu_percent"; "memory_mb"; "network_kb_s"; "disk_io_ops"]
    threshold 0.05  // Top 5% unusual
    method QuantumKernel
}

match result with
| Ok detector ->
    // Real-time monitoring
    let monitorMetrics currentMetrics =
        match AnomalyDetector.detect detector currentMetrics with
        | Ok result when result.IsAnomaly && result.AnomalyScore > 0.9 ->
            // Critical anomaly
            sendAlert "CRITICAL: Possible intrusion detected"
        | Ok result when result.IsAnomaly ->
            // Minor anomaly
            logWarning $"Unusual activity (score: {result.AnomalyScore})"
        | Ok _ ->
            // Normal
            ()
        | Error err ->
            logError err.Message
    
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

Forecast future outcomes based on historical patterns.

**Business Applications:**
- Customer churn prediction
- Demand forecasting
- Sales predictions
- Equipment maintenance scheduling
- Resource capacity planning

### API Reference

```fsharp
open FSharp.Azure.Quantum.Business.PredictiveModel

// Train predictive model
let result = predictiveModel {
    trainWith historicalData historicalOutcomes
    
    // Model type
    modelType Regression  // or Classification
    
    // Features
    features ["tenure_months"; "monthly_spend"; "support_calls"; "satisfaction"]
    target "will_churn"
    
    // Architecture
    architecture Hybrid  // Quantum features + classical regression
}

match result with
| Ok model ->
    printfn "Model R² score: %.2f" model.Metrics.RSquared
    printfn "Mean error: %.2f" model.Metrics.MAE
    
    // Predict for new customer
    let newCustomer = [| 6.0; 45.0; 3.0; 6.5 |]
    
    match PredictiveModel.predict model newCustomer with
    | Ok prediction ->
        printfn "Churn probability: %.2f%%" (prediction.Value * 100.0)
        printfn "Confidence interval: [%.2f, %.2f]" 
            prediction.LowerBound 
            prediction.UpperBound
    | Error err -> eprintfn "Error: %s" err.Message
    
| Error err -> eprintfn "Training failed: %s" err.Message
```

### Configuration Options

```fsharp
let result = predictiveModel {
    trainWith trainX trainY
    
    // Model configuration
    modelType Regression
    architecture Quantum
    
    // Feature engineering
    features featureNames
    target targetName
    normalizeFeatures true
    
    // Training
    learningRate 0.1
    maxEpochs 100
    validationSplit 0.2
    
    // Regularization (prevent overfitting)
    l2Penalty 0.01
    dropoutRate 0.1
}
```

### Example: Customer Churn Prediction

```fsharp
// Historical customer data
// Features: [tenure_months, monthly_spend, support_calls, usage_hours, satisfaction]
let customerHistory = [|
    [| 24.0; 75.0; 1.0; 20.0; 8.5 |]; 0.0  // Stayed
    [| 6.0; 45.0; 5.0; 5.0; 3.0 |]; 1.0    // Churned
    [| 36.0; 120.0; 0.0; 30.0; 9.0 |]; 0.0 // Stayed
    // ... 1000 historical customers
|]

let trainX = customerHistory |> Array.map fst
let trainY = customerHistory |> Array.map snd

let result = predictiveModel {
    trainWith trainX trainY
    
    features [
        "tenure_months"
        "monthly_spend"
        "support_calls"
        "usage_hours"
        "satisfaction_score"
    ]
    target "will_churn"
    
    modelType BinaryClassification
    architecture Quantum
    
    // Business constraint: False negatives costly (missed churn)
    classWeights [| 1.0; 3.0 |]
}

match result with
| Ok model ->
    // Churn risk scoring for current customers
    let scoreChurnRisk customer =
        match PredictiveModel.predict model customer with
        | Ok pred when pred.Value > 0.7 ->
            "HIGH RISK - Immediate retention campaign"
        | Ok pred when pred.Value > 0.4 ->
            "MEDIUM RISK - Monitor and engage"
        | Ok pred ->
            "LOW RISK - Routine engagement"
        | Error _ ->
            "ERROR - Manual review"
    
    // Score at-risk customer
    let atRiskCustomer = [| 8.0; 40.0; 4.0; 8.0; 4.5 |]
    printfn "%s" (scoreChurnRisk atRiskCustomer)
    
| Error err -> eprintfn "Model training failed: %s" err.Message
```

### Working Example

See complete example: [examples/PredictiveModeling/CustomerChurnPrediction.fsx](https://github.com/Thorium/FSharp.Azure.Quantum/tree/main/examples/PredictiveModeling/)

---

## Similarity Search

### What is Similarity Search?

Find items similar to a query item based on features.

**Business Applications:**
- Product recommendations
- Document similarity (semantic search)
- Image matching
- Customer segmentation
- Duplicate detection

### API Reference

```fsharp
open FSharp.Azure.Quantum.Business.SimilaritySearch

// Build similarity index
let result = similaritySearch {
    indexItems catalogItems
    
    // Features to compare
    features ["category"; "price"; "rating"; "description_embedding"]
    
    // Similarity metric
    similarityMetric QuantumKernel  // or Cosine, Euclidean
    
    // Index optimization
    buildIndex true  // Pre-compute for fast queries
}

match result with
| Ok searchIndex ->
    // Find similar products
    let queryItem = [| 2.0; 49.99; 4.5; embeddingVector |]
    
    match SimilaritySearch.findSimilar searchIndex queryItem 5 with  // Top 5
    | Ok results ->
        printfn "Similar items:"
        results |> Array.iter (fun r ->
            printfn "  - Item %d (similarity: %.2f%%)" r.ItemId (r.Score * 100.0)
        )
    | Error err -> eprintfn "Search error: %s" err.Message
    
| Error err -> eprintfn "Index build failed: %s" err.Message
```

### Configuration Options

```fsharp
let result = similaritySearch {
    indexItems items
    
    // Similarity method
    similarityMetric QuantumKernel  // Quantum feature space
    
    // Quantum kernel configuration
    featureMap (ZZFeatureMap 2)
    shots 1000
    
    // Index optimization
    buildIndex true
    indexMethod ApproximateNearestNeighbor  // Fast for large catalogs
    
    // Pre-filtering
    filterByCategory true
    categoryFeatureIndex 0
}
```

### Example: Product Recommendations

```fsharp
// Product catalog features
// [category_id, price, rating, popularity, feature_vec...]
let catalog = [|
    [| 1.0; 29.99; 4.5; 850.0; 0.1; 0.8; 0.3 |]  // Electronics
    [| 1.0; 49.99; 4.8; 1200.0; 0.2; 0.9; 0.4 |] // Electronics
    [| 2.0; 15.99; 4.2; 300.0; 0.7; 0.2; 0.1 |]  // Books
    // ... 10,000 products
|]

let result = similaritySearch {
    indexItems catalog
    features ["category"; "price"; "rating"; "popularity"; "f1"; "f2"; "f3"]
    similarityMetric QuantumKernel
    buildIndex true
}

match result with
| Ok index ->
    // User viewed a product - find similar items
    let viewedProduct = catalog.[42]
    
    match SimilaritySearch.findSimilar index viewedProduct 10 with
    | Ok recommendations ->
        printfn "Customers who viewed this also liked:"
        recommendations 
        |> Array.take 5  // Top 5
        |> Array.iter (fun r ->
            printfn "  - Product %d (%.0f%% match)" r.ItemId (r.Score * 100.0)
        )
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
- Lead optimization
- Pharmacophore feature selection
- Compound prioritization

### Available Screening Methods

| Method | Description | Use Case |
|--------|-------------|----------|
| **QuantumKernelSVM** | Quantum kernel-based SVM classification | Binary activity classification |
| **VQCClassifier** | Variational Quantum Classifier | Multi-label molecular classification |
| **QAOADiverseSelection** | QAOA-based diverse subset selection | Select diverse, high-value compounds within budget |


### API Reference

```fsharp
open FSharp.Azure.Quantum.Business
open FSharp.Azure.Quantum.Business.QuantumDrugDiscoveryDSL

// Method 1: Quantum Kernel SVM (default)
let result = drugDiscovery {
    load_candidates_from_file "candidates.sdf"
    use_method QuantumKernelSVM
    use_feature_map ZZFeatureMap
    set_batch_size 20
    shots 1000
    backend localBackend
}

match result with
| Ok screening ->
    printfn "Method: %A" screening.Method
    printfn "Molecules Processed: %d" screening.MoleculesProcessed
    printfn "Result: %s" screening.Message
| Error err -> eprintfn "Screening failed: %s" err.Message
```

### Configuration Options

```fsharp
// Full configuration example
let result = drugDiscovery {
    // Data source (choose one)
    load_candidates_from_file "molecules.sdf"
    // OR: load_candidates_from_provider sdfProvider
    // OR: target_protein_from_pdb "target.pdb"
    
    // Screening method
    use_method VQCClassifier  // or QuantumKernelSVM, QAOADiverseSelection
    
    // Feature encoding
    use_feature_map ZZFeatureMap  // or PauliFeatureMap, ZFeatureMap
    
    // General settings
    set_batch_size 20         // Molecules per batch
    shots 1000                // Quantum measurement shots
    backend localBackend      // Quantum backend
    
    // VQC-specific settings (for VQCClassifier)
    vqc_layers 3              // Number of ansatz layers (default: 2)
    vqc_max_epochs 100        // Max training epochs (default: 50)
    
    // QAOA-specific settings (for QAOADiverseSelection)
    selection_budget 5.0      // Budget constraint (default: 10.0)
    diversity_weight 0.7      // Diversity bonus weight (default: 0.5)
}
```

### Method 1: Quantum Kernel SVM

Classify molecules using quantum feature maps and support vector machines.

```fsharp
// Train a quantum kernel SVM for activity prediction
let result = drugDiscovery {
    load_candidates_from_file "labeled_compounds.csv"  // Requires activity labels
    use_method QuantumKernelSVM
    use_feature_map ZZFeatureMap
    set_batch_size 50
    shots 1000
    backend (LocalBackend() :> IQuantumBackend)
}

match result with
| Ok r -> 
    printfn "Support vectors found: %s" r.Message
    // Model can classify new candidates
| Error e -> eprintfn "Error: %s" e.Message
```

**When to use:**
- Binary classification (active/inactive)
- Well-labeled training data available
- Moderate dataset size (10-100 molecules)

### Method 2: VQC Classifier

Train a Variational Quantum Classifier for molecular activity prediction.

```fsharp
// Train VQC for multi-class molecular classification
let result = drugDiscovery {
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

match result with
| Ok r ->
    printfn "Training complete!"
    printfn "%s" r.Message  // Shows accuracy, convergence
| Error e -> eprintfn "Training failed: %s" e.Message
```

**When to use:**
- Need trainable quantum model
- Want to tune circuit depth
- Classification with gradient-based optimization

### Method 3: QAOA Diverse Selection

Select a diverse subset of high-value compounds within a budget using QAOA optimization.

```fsharp
// Select diverse compounds for screening library
let result = drugDiscovery {
    load_candidates_from_file "compound_library.sdf"
    use_method QAOADiverseSelection
    
    // QAOA-specific configuration
    selection_budget 10.0     // Max total cost of selected compounds
    diversity_weight 0.6      // Balance value vs diversity (0-1)
    
    set_batch_size 50         // Evaluate top 50 candidates
    shots 2000                // More shots for better optimization
    backend localBackend
}

match result with
| Ok r ->
    printfn "Selection complete!"
    printfn "%s" r.Message  // Shows selected compounds, total value, diversity
| Error e -> eprintfn "Selection failed: %s" e.Message
```

**When to use:**
- Building diverse screening libraries
- Budget-constrained compound selection
- Maximizing chemical diversity
- Avoiding redundant compounds

### Example: Complete Virtual Screening Pipeline

```fsharp
open FSharp.Azure.Quantum.Business
open FSharp.Azure.Quantum.Core.BackendAbstraction
open FSharp.Azure.Quantum.Backends.LocalBackend

// Create backend
let backend = LocalBackend() :> IQuantumBackend

// Step 1: Initial classification with VQC
let classificationResult = drugDiscovery {
    load_candidates_from_file "hit_compounds.sdf"
    use_method VQCClassifier
    vqc_layers 2
    vqc_max_epochs 50
    set_batch_size 100
    backend backend
}

// Step 2: Select diverse subset from classified hits
let selectionResult = drugDiscovery {
    load_candidates_from_file "classified_hits.sdf"
    use_method QAOADiverseSelection
    selection_budget 20.0       // Select compounds worth total "cost" of 20
    diversity_weight 0.5        // Equal weight to value and diversity
    set_batch_size 50
    backend backend
}

match classificationResult, selectionResult with
| Ok cls, Ok sel ->
    printfn "Classification: %d molecules processed" cls.MoleculesProcessed
    printfn "Selection: %s" sel.Message
| Error e, _ -> eprintfn "Classification failed: %s" e.Message
| _, Error e -> eprintfn "Selection failed: %s" e.Message
```

### Supported File Formats

| Format | Extension | Provider |
|--------|-----------|----------|
| SDF/MOL | .sdf, .mol | SdfFileDatasetProvider |
| PDB | .pdb | PdbLigandDatasetProvider |
| SMILES | .smi, .txt | MolecularData.loadFromSmilesList |
| CSV | .csv | MolecularData.loadFromCsv |
| FCIDump | .fcidump | FciDumpFileDatasetProvider |

### Working Example

See complete example: [examples/DrugDiscovery/](https://github.com/Thorium/FSharp.Azure.Quantum/tree/main/examples/DrugDiscovery/)

---

## Architecture Selection Guide

### When to Use Each Architecture

**Quantum:**
- High-dimensional feature spaces (>10 features)
- Complex feature correlations
- Pattern recognition requires expressive models
- Willing to trade speed for potential accuracy gains

**Classical:**
- Simple, low-dimensional problems (<5 features)
- Large datasets (>10,000 samples)
- Real-time inference critical (< 100ms)
- Interpretability paramount

**Hybrid:**
- Medium complexity (5-10 features)
- Want quantum benefits without full overhead
- Quantum kernels + classical SVM
- Best of both worlds for many tasks

**AutoSelect:**
- Unsure which is best
- Let library decide based on data characteristics
- Automatic fallback if quantum resources limited

### Performance Comparison

| Architecture | Training Time | Inference Time | Accuracy (Typical) | Resource Usage |
|--------------|---------------|----------------|--------------------|--------------------|
| **Quantum** | Minutes-Hours | Seconds | 85-95% | High (quantum backend) |
| **Classical** | Seconds-Minutes | Milliseconds | 80-90% | Low (CPU only) |
| **Hybrid** | Minutes | Hundreds of ms | 83-93% | Medium |

## Troubleshooting

### Common Issues

#### 1. Poor Accuracy (<60%)

**Symptoms:** Model performs poorly on both train and test sets

**Solutions:**
- Increase model complexity (use Quantum or Hybrid)
- Add more features (better feature engineering)
- Collect more training data
- Check data quality (labels correct?)
- Try AutoML to find best approach

#### 2. Overfitting (High Train, Low Test Accuracy)

**Symptoms:** Train accuracy >90%, test accuracy <70%

**Solutions:**
- Increase training data
- Add regularization (L2 penalty, dropout)
- Reduce model complexity (use Classical)
- Increase validation split
- Use cross-validation

#### 3. Slow Training

**Symptoms:** Training takes hours

**Solutions:**
- Use smaller shots (100 instead of 1000) during training
- Reduce max epochs
- Use Classical or Hybrid architecture
- Enable early stopping
- Reduce dataset size (sample if very large)

#### 4. Class Imbalance (Fraud Detection)

**Symptoms:** Model always predicts majority class

**Solutions:**
- Set `classWeights` to penalize minority class more
- Use SMOTE or oversampling for minority class
- Adjust decision threshold (lower for rare events)
- Use precision/recall instead of accuracy as metric

## See Also

- [Quantum Machine Learning](quantum-machine-learning) - VQC, Quantum Kernels, Feature Maps
- [Getting Started Guide](getting-started) - Installation and setup
- [API Reference](api-reference) - Complete API documentation
- [Working Examples](https://github.com/Thorium/FSharp.Azure.Quantum/tree/main/examples/) - Complete code examples

---

**Last Updated**: December 2025
