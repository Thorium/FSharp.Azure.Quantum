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
### 7. Social Network Analyzer - Community Detection, Influence Maximization
### 8. Constraint Scheduler - Constraint-Based Scheduling Optimization
### 9. Coverage Optimizer - Set Coverage Optimization
### 10. Resource Pairing - Resource Pairing/Matching Optimization
### 11. Packing Optimizer - Bin Packing Optimization

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

## Social Network Analyzer

### What is Social Network Analysis?

Analyze the structure and dynamics of social networks to detect communities, identify influential nodes, and optimize information flow.

**Business Applications:**
- Community detection (identify clusters in customer networks)
- Influence maximization (find key influencers for marketing campaigns)
- Information diffusion modeling (viral marketing reach prediction)
- Fraud ring detection (connected suspicious actors)
- Organizational network optimization (team collaboration analysis)

### API Reference

```fsharp
open FSharp.Azure.Quantum.Business.SocialNetworkAnalyzer

// Minimal configuration - community detection
let result = socialNetworkAnalysis {
    loadGraph edges  // Array of (sourceNode, targetNode, weight) tuples
    
    // Analysis type
    analysisType CommunityDetection  // or InfluenceMaximization, InformationDiffusion
    
    // Algorithm
    method QuantumWalk  // or QAOA, Classical
}

match result with
| Ok analysis ->
    printfn "Communities found: %d" analysis.CommunityCount
    printfn "Modularity score: %.4f" analysis.Modularity
    
    // Inspect detected communities
    analysis.Communities |> Array.iteri (fun i community ->
        printfn "Community %d: %d members" i community.Members.Length
    )
    
| Error err -> eprintfn "Analysis failed: %s" err.Message
```

### Configuration Options

```fsharp
let result = socialNetworkAnalysis {
    loadGraph edges
    
    // Analysis type
    analysisType InfluenceMaximization
    
    // Method selection
    method QAOA  // Quantum Approximate Optimization Algorithm
    
    // QAOA configuration
    qaoaLayers 3            // Number of QAOA layers (default: 2)
    shots 2000              // Quantum measurement shots
    backend localBackend    // Quantum backend
    
    // Influence maximization parameters
    seedSetSize 10          // Number of influencers to find
    diffusionModel IndependentCascade  // or LinearThreshold
    propagationProbability 0.1         // Edge activation probability
    
    // Graph preprocessing
    directed false          // Undirected graph
    weighted true           // Use edge weights
    normalizeWeights true   // Normalize edge weights to [0,1]
}
```

### Example: Influence Maximization for Marketing

```fsharp
// Social network edges: (from_user, to_user, interaction_strength)
let socialEdges = [|
    (0, 1, 0.8); (0, 2, 0.6); (1, 3, 0.9)
    (2, 4, 0.7); (3, 5, 0.5); (4, 5, 0.4)
    (5, 6, 0.8); (6, 7, 0.3); (7, 8, 0.6)
    // ... 10,000 edges from customer interaction data
|]

let result = socialNetworkAnalysis {
    loadGraph socialEdges
    analysisType InfluenceMaximization
    method QAOA
    qaoaLayers 2
    seedSetSize 5           // Find top 5 influencers
    diffusionModel IndependentCascade
    propagationProbability 0.15
    shots 1000
    backend localBackend
}

match result with
| Ok analysis ->
    printfn "Top influencers for campaign:"
    analysis.InfluentialNodes |> Array.iteri (fun rank node ->
        printfn "  %d. User %d (reach: %.0f%% of network)" 
            (rank + 1) node.Id (node.ExpectedReach * 100.0)
    )
    printfn "Total expected reach: %.0f%% of network" 
        (analysis.TotalExpectedReach * 100.0)
    
| Error err -> eprintfn "Analysis failed: %s" err.Message
```

### Working Example

See complete example: [examples/SocialNetworkAnalysis/](https://github.com/Thorium/FSharp.Azure.Quantum/tree/main/examples/SocialNetworkAnalysis/)

---

## Constraint Scheduler

### What is Constraint Scheduling?

Optimize scheduling of tasks, resources, and events subject to hard and soft constraints such as time windows, dependencies, capacity limits, and preferences.

**Business Applications:**
- Employee shift scheduling (nurse rostering, retail staffing)
- Job shop scheduling (manufacturing production lines)
- Meeting room allocation (calendar optimization)
- University timetabling (course scheduling)
- Vehicle routing with time windows (delivery scheduling)

### API Reference

```fsharp
open FSharp.Azure.Quantum.Business.ConstraintScheduler

// Minimal configuration - schedule tasks with constraints
let result = constraintSchedule {
    tasks taskList           // Array of tasks with durations
    resources resourceList   // Available resources (people, machines, rooms)
    
    // Constraints
    timeHorizon (TimeSpan.FromHours 8.0)  // Scheduling window
    
    // Optimization objective
    objective MinimizeMakespan  // or MinimizeLateness, MaximizeUtilization
    
    // Solver
    method QAOA  // or QuantumAnnealing, Classical
}

match result with
| Ok schedule ->
    printfn "Makespan: %A" schedule.Makespan
    printfn "Resource utilization: %.1f%%" (schedule.Utilization * 100.0)
    
    // Inspect assignments
    schedule.Assignments |> Array.iter (fun a ->
        printfn "Task '%s' -> Resource '%s' at %A" 
            a.TaskName a.ResourceName a.StartTime
    )
    
| Error err -> eprintfn "Scheduling failed: %s" err.Message
```

### Configuration Options

```fsharp
let result = constraintSchedule {
    tasks taskList
    resources resourceList
    
    // Time configuration
    timeHorizon (TimeSpan.FromHours 8.0)
    timeSlotDuration (TimeSpan.FromMinutes 30.0)  // Granularity
    
    // Hard constraints (must satisfy)
    precedenceConstraints [| (task1, task2); (task2, task3) |]  // task1 before task2
    unavailability [| (resource1, timeSlot3); (resource2, timeSlot1) |]
    maxConcurrentTasks 3           // Per-resource concurrency limit
    
    // Soft constraints (preferences, penalized if violated)
    preferredAssignments [| (task1, resource2, 0.8) |]  // weight 0.8
    minimizeGaps true              // Reduce idle time between tasks
    balanceLoad true               // Even distribution across resources
    
    // Solver configuration
    method QAOA
    qaoaLayers 4
    shots 2000
    backend localBackend
    
    // Optimization objective
    objective MinimizeMakespan
    constraintPenalty 10.0         // Penalty weight for soft constraint violations
}
```

### Example: Employee Shift Scheduling

```fsharp
// Define shifts and employees
let shifts = [|
    { Name = "Morning"; Duration = TimeSpan.FromHours 8.0; RequiredSkill = "Cashier" }
    { Name = "Afternoon"; Duration = TimeSpan.FromHours 8.0; RequiredSkill = "Cashier" }
    { Name = "Night"; Duration = TimeSpan.FromHours 8.0; RequiredSkill = "Security" }
    { Name = "Stocking"; Duration = TimeSpan.FromHours 4.0; RequiredSkill = "General" }
    // ... 20 shifts per week
|]

let employees = [|
    { Name = "Alice"; Skills = [| "Cashier"; "General" |]; MaxHoursPerWeek = 40.0 }
    { Name = "Bob"; Skills = [| "Security"; "General" |]; MaxHoursPerWeek = 32.0 }
    { Name = "Carol"; Skills = [| "Cashier"; "Security"; "General" |]; MaxHoursPerWeek = 40.0 }
    // ... 8 employees
|]

let result = constraintSchedule {
    tasks shifts
    resources employees
    
    timeHorizon (TimeSpan.FromDays 7.0)
    timeSlotDuration (TimeSpan.FromHours 4.0)
    
    // Hard constraints
    maxConcurrentTasks 1               // One shift at a time per person
    
    // Soft constraints
    balanceLoad true                   // Fair distribution of shifts
    minimizeGaps true                  // Minimize fragmented schedules
    
    objective MaximizeUtilization
    method QAOA
    qaoaLayers 3
    shots 1500
    backend localBackend
}

match result with
| Ok schedule ->
    printfn "Weekly Schedule (utilization: %.1f%%):" (schedule.Utilization * 100.0)
    schedule.Assignments 
    |> Array.groupBy (fun a -> a.ResourceName)
    |> Array.iter (fun (employee, assignments) ->
        let totalHours = assignments |> Array.sumBy (fun a -> a.Duration.TotalHours)
        printfn "  %s (%.0f hrs):" employee totalHours
        assignments |> Array.iter (fun a ->
            printfn "    %s at %A" a.TaskName a.StartTime
        )
    )
    
| Error err -> eprintfn "Scheduling failed: %s" err.Message
```

### Working Example

See complete example: [examples/ConstraintScheduling/](https://github.com/Thorium/FSharp.Azure.Quantum/tree/main/examples/ConstraintScheduling/)

---

## Coverage Optimizer

### What is Set Coverage Optimization?

Find the minimum-cost collection of sets that covers all required elements. This is a fundamental combinatorial optimization problem with broad applications in facility placement, service deployment, and resource allocation.

**Business Applications:**
- Facility location (minimum stores to cover all delivery zones)
- Service deployment (minimum servers to cover all regions)
- Sensor placement (minimum sensors for full monitoring coverage)
- Test suite minimization (minimum tests to cover all code paths)
- Advertising campaign selection (minimum campaigns to reach all demographics)

### API Reference

```fsharp
open FSharp.Azure.Quantum.Business.CoverageOptimizer

// Minimal configuration - find minimum covering sets
let result = coverageOptimization {
    universe elements          // Array of elements to cover
    sets candidateSets         // Array of (setId, elements, cost) tuples
    
    // Optimization objective
    objective MinimizeCost  // or MinimizeSets, MaximizeCoverage
    
    // Solver
    method QAOA  // or QuantumAnnealing, Classical
}

match result with
| Ok solution ->
    printfn "Sets selected: %d" solution.SelectedSets.Length
    printfn "Total cost: %.2f" solution.TotalCost
    printfn "Coverage: %.1f%%" (solution.CoveragePercent * 100.0)
    
    // Inspect selected sets
    solution.SelectedSets |> Array.iter (fun s ->
        printfn "  Set '%s' (cost: %.2f, covers: %d elements)" 
            s.Name s.Cost s.CoveredElements.Length
    )
    
| Error err -> eprintfn "Optimization failed: %s" err.Message
```

### Configuration Options

```fsharp
let result = coverageOptimization {
    universe elements
    sets candidateSets
    
    // Objective
    objective MinimizeCost
    
    // Constraints
    maxSets 10                     // Maximum number of sets to select
    budgetLimit 1000.0             // Maximum total cost
    requiredCoverage 1.0           // 100% coverage required (default)
    
    // Solver configuration
    method QAOA
    qaoaLayers 3
    shots 2000
    backend localBackend
    
    // Penalty tuning
    coveragePenalty 20.0           // Penalty weight for uncovered elements
    
    // Greedy warm-start (seed QAOA with classical greedy solution)
    warmStart true
}
```

### Example: Facility Location for Delivery Coverage

```fsharp
// Delivery zones that must be covered
let deliveryZones = [| "Zone-A"; "Zone-B"; "Zone-C"; "Zone-D"; "Zone-E"; 
                        "Zone-F"; "Zone-G"; "Zone-H" |]

// Candidate warehouse locations: (name, zones covered, monthly cost)
let warehouseCandidates = [|
    ("Warehouse-North", [| "Zone-A"; "Zone-B"; "Zone-C" |], 5000.0)
    ("Warehouse-South", [| "Zone-F"; "Zone-G"; "Zone-H" |], 4500.0)
    ("Warehouse-Central", [| "Zone-C"; "Zone-D"; "Zone-E"; "Zone-F" |], 7000.0)
    ("Warehouse-East", [| "Zone-B"; "Zone-D"; "Zone-G" |], 3500.0)
    ("Warehouse-West", [| "Zone-A"; "Zone-E"; "Zone-H" |], 4000.0)
    // ... 20 candidate locations
|]

let result = coverageOptimization {
    universe deliveryZones
    sets warehouseCandidates
    
    objective MinimizeCost
    requiredCoverage 1.0           // Must cover all zones
    maxSets 5                      // Open at most 5 warehouses
    
    method QAOA
    qaoaLayers 3
    shots 2000
    warmStart true
    backend localBackend
}

match result with
| Ok solution ->
    printfn "Optimal warehouse placement:"
    printfn "  Total monthly cost: $%.0f" solution.TotalCost
    printfn "  Warehouses needed: %d" solution.SelectedSets.Length
    solution.SelectedSets |> Array.iter (fun w ->
        printfn "  - %s ($%.0f/mo, covers: %s)" 
            w.Name w.Cost (String.concat ", " w.CoveredElements)
    )
    printfn "  Coverage: %.0f%%" (solution.CoveragePercent * 100.0)
    
| Error err -> eprintfn "Optimization failed: %s" err.Message
```

### Working Example

See complete example: [examples/CoverageOptimization/](https://github.com/Thorium/FSharp.Azure.Quantum/tree/main/examples/CoverageOptimization/)

---

## Resource Pairing

### What is Resource Pairing?

Optimally match resources to demands, workers to tasks, or entities to partners based on compatibility scores, preferences, and constraints. This solves assignment and matching problems common in workforce management and logistics.

**Business Applications:**
- Worker-task assignment (match employees to projects by skill fit)
- Mentor-mentee pairing (optimize mentorship compatibility)
- Donor-recipient matching (organ transplant, blood donation)
- Student-school assignment (school choice optimization)
- Ride-sharing matching (drivers to passengers)

### API Reference

```fsharp
open FSharp.Azure.Quantum.Business.ResourcePairing

// Minimal configuration - optimal resource matching
let result = resourcePairing {
    supply supplyItems        // Array of resources (workers, donors, drivers)
    demand demandItems        // Array of demands (tasks, recipients, passengers)
    
    // Compatibility scoring
    compatibilityMatrix scores  // 2D array of compatibility scores
    
    // Optimization objective
    objective MaximizeCompatibility  // or MinimizeCost, Balanced
    
    // Solver
    method QAOA  // or QuantumAnnealing, Classical
}

match result with
| Ok matching ->
    printfn "Pairs matched: %d" matching.Pairs.Length
    printfn "Total compatibility: %.2f" matching.TotalScore
    printfn "Average score: %.2f" matching.AverageScore
    
    // Inspect pairings
    matching.Pairs |> Array.iter (fun p ->
        printfn "  %s <-> %s (score: %.2f)" 
            p.SupplyName p.DemandName p.Score
    )
    
| Error err -> eprintfn "Matching failed: %s" err.Message
```

### Configuration Options

```fsharp
let result = resourcePairing {
    supply supplyItems
    demand demandItems
    compatibilityMatrix scores
    
    // Matching constraints
    maxPairsPerSupply 1            // One-to-one matching (default)
    maxPairsPerDemand 1            // Each demand fulfilled once
    minimumScore 0.3               // Minimum acceptable compatibility
    
    // Capacity matching (one-to-many)
    supplyCapacities capacities    // Array of max assignments per supply item
    
    // Preferences (soft constraints)
    supplyPreferences prefs        // Ranked preference lists for supply side
    demandPreferences prefs        // Ranked preference lists for demand side
    
    // Solver configuration
    objective MaximizeCompatibility
    method QAOA
    qaoaLayers 3
    shots 2000
    backend localBackend
    
    // Fairness
    balancePairings true           // Distribute evenly across supply items
    fairnessPenalty 5.0            // Penalty weight for imbalanced assignments
}
```

### Example: Worker-Project Assignment

```fsharp
// Available developers
let developers = [|
    { Name = "Alice"; Skills = [| "F#"; "Azure"; "ML" |] }
    { Name = "Bob"; Skills = [| "C#"; "Azure"; "DevOps" |] }
    { Name = "Carol"; Skills = [| "F#"; "Python"; "ML" |] }
    { Name = "Dave"; Skills = [| "C#"; "React"; "SQL" |] }
|]

// Projects needing assignment
let projects = [|
    { Name = "Quantum ML Platform"; RequiredSkills = [| "F#"; "ML"; "Azure" |] }
    { Name = "Cloud Infrastructure"; RequiredSkills = [| "Azure"; "DevOps" |] }
    { Name = "Data Pipeline"; RequiredSkills = [| "Python"; "ML"; "SQL" |] }
    { Name = "Web Dashboard"; RequiredSkills = [| "React"; "C#"; "SQL" |] }
|]

// Compute compatibility scores based on skill overlap
let scores = Array2D.init developers.Length projects.Length (fun i j ->
    let devSkills = Set.ofArray developers.[i].Skills
    let projSkills = Set.ofArray projects.[j].RequiredSkills
    let overlap = Set.intersect devSkills projSkills |> Set.count |> float
    overlap / (projSkills |> Set.count |> float)  // Fraction of required skills met
)

let result = resourcePairing {
    supply developers
    demand projects
    compatibilityMatrix scores
    
    maxPairsPerSupply 1            // Each developer on one project
    maxPairsPerDemand 1            // Each project gets one developer
    minimumScore 0.3               // Must have at least 30% skill match
    
    objective MaximizeCompatibility
    method QAOA
    qaoaLayers 2
    shots 1000
    backend localBackend
}

match result with
| Ok matching ->
    printfn "Optimal team assignments:"
    matching.Pairs |> Array.iter (fun p ->
        printfn "  %s -> %s (skill fit: %.0f%%)" 
            p.SupplyName p.DemandName (p.Score * 100.0)
    )
    printfn "Overall team fit: %.0f%%" (matching.AverageScore * 100.0)
    
    // Identify unmatched items
    if matching.UnmatchedSupply.Length > 0 then
        printfn "Unassigned developers: %s" 
            (matching.UnmatchedSupply |> Array.map (fun u -> u.Name) |> String.concat ", ")
    if matching.UnmatchedDemand.Length > 0 then
        printfn "Unstaffed projects: %s" 
            (matching.UnmatchedDemand |> Array.map (fun u -> u.Name) |> String.concat ", ")
    
| Error err -> eprintfn "Assignment failed: %s" err.Message
```

### Working Example

See complete example: [examples/ResourcePairing/](https://github.com/Thorium/FSharp.Azure.Quantum/tree/main/examples/ResourcePairing/)

---

## Packing Optimizer

### What is Bin Packing Optimization?

Optimally pack items of varying sizes into containers (bins) to minimize wasted space or the number of containers used. This is a classic combinatorial optimization problem with significant cost implications in logistics and infrastructure.

**Business Applications:**
- Container loading (shipping/logistics)
- Cloud VM placement (virtual machine bin packing)
- Warehouse storage allocation (optimize shelf/pallet usage)
- Memory allocation (data partitioning across storage nodes)
- Cutting stock problems (minimize material waste)

### API Reference

```fsharp
open FSharp.Azure.Quantum.Business.PackingOptimizer

// Minimal configuration - pack items into bins
let result = packingOptimization {
    items itemList             // Array of items with sizes/weights
    binCapacity capacity       // Capacity of each bin
    
    // Optimization objective
    objective MinimizeBins  // or MinimizeWaste, BalanceLoad
    
    // Solver
    method QAOA  // or QuantumAnnealing, Classical
}

match result with
| Ok packing ->
    printfn "Bins used: %d" packing.BinsUsed
    printfn "Total waste: %.1f%%" (packing.WastePercent * 100.0)
    printfn "Average fill: %.1f%%" (packing.AverageFillRate * 100.0)
    
    // Inspect bin assignments
    packing.Bins |> Array.iteri (fun i bin ->
        printfn "  Bin %d: %d items, %.1f%% full" 
            (i + 1) bin.Items.Length (bin.FillRate * 100.0)
    )
    
| Error err -> eprintfn "Packing failed: %s" err.Message
```

### Configuration Options

```fsharp
let result = packingOptimization {
    items itemList
    binCapacity capacity
    
    // Multi-dimensional packing
    dimensions 1               // 1D (weight), 2D (area), or 3D (volume)
    
    // Bin configuration
    binCapacity 100.0          // Single capacity (1D)
    binCapacities [| 100.0; 50.0; 80.0 |]  // Per-dimension capacities (multi-D)
    maxBins 20                 // Upper bound on bins available
    
    // Item constraints
    itemGroups groupAssignments  // Items that must be in the same bin
    incompatible conflicts       // Item pairs that cannot share a bin
    
    // Solver configuration
    objective MinimizeBins
    method QAOA
    qaoaLayers 3
    shots 2000
    backend localBackend
    
    // Heuristic warm-start
    warmStart true             // Seed with First Fit Decreasing heuristic
    
    // Penalty tuning
    overflowPenalty 50.0       // Penalty for exceeding bin capacity
}
```

### Example: Container Loading for Shipping

```fsharp
// Packages to ship: (name, weight_kg, volume_m3)
let packages = [|
    ("Electronics-A", 15.0, 0.3)
    ("Electronics-B", 8.0, 0.2)
    ("Furniture-1", 45.0, 1.2)
    ("Furniture-2", 38.0, 0.9)
    ("Books-Pallet", 25.0, 0.5)
    ("Clothing-Box", 5.0, 0.8)
    ("Appliance-1", 30.0, 0.6)
    ("Appliance-2", 22.0, 0.4)
    ("Fragile-1", 10.0, 0.3)
    ("Fragile-2", 12.0, 0.35)
    // ... 50 packages
|]

let items = packages |> Array.map (fun (name, weight, volume) ->
    { Name = name; Sizes = [| weight; volume |] }
)

let result = packingOptimization {
    items items
    dimensions 2                           // Weight and volume
    binCapacities [| 100.0; 2.5 |]        // 100kg, 2.5m³ per container
    maxBins 10
    
    // Fragile items cannot share with heavy furniture
    incompatible [| ("Fragile-1", "Furniture-1"); ("Fragile-2", "Furniture-2") |]
    
    objective MinimizeBins
    method QAOA
    qaoaLayers 3
    shots 2000
    warmStart true
    backend localBackend
}

match result with
| Ok packing ->
    printfn "Shipping plan:"
    printfn "  Containers needed: %d" packing.BinsUsed
    printfn "  Average fill rate: %.1f%%" (packing.AverageFillRate * 100.0)
    packing.Bins |> Array.iteri (fun i bin ->
        printfn "  Container %d (%.1f%% full):" (i + 1) (bin.FillRate * 100.0)
        bin.Items |> Array.iter (fun item ->
            printfn "    - %s (%.0fkg, %.1fm³)" item.Name item.Sizes.[0] item.Sizes.[1]
        )
    )
    
    let wastedCapacity = packing.WastePercent * 100.0
    printfn "  Wasted capacity: %.1f%%" wastedCapacity
    
| Error err -> eprintfn "Packing failed: %s" err.Message
```

### Working Example

See complete example: [examples/PackingOptimization/](https://github.com/Thorium/FSharp.Azure.Quantum/tree/main/examples/PackingOptimization/)

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
