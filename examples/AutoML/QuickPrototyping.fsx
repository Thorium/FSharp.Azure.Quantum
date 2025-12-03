/// AutoML Example: Quick Prototyping & Model Selection
/// 
/// This example demonstrates how to use AutoML for rapid experimentation
/// when you don't know which algorithm or approach will work best.
///
/// BUSINESS SCENARIO:
/// You have labeled data but don't know which ML approach to use.
/// AutoML automatically tries multiple approaches and returns the best one.
/// 
/// WHAT AutoML DOES:
/// 1. Analyzes your data automatically
/// 2. Tries Binary Classification, Multi-Class, Regression, Anomaly Detection
/// 3. Tests Quantum and Hybrid architectures
/// 4. Tunes hyperparameters automatically
/// 5. Returns best model with detailed report
/// 
/// PERFECT FOR:
/// - Quick prototyping: "Just give me a working model"
/// - Non-experts: Don't know which algorithm to use
/// - Baseline comparison: What's the best possible with this data?
/// - Model selection: Compare all approaches at once

#r "../../src/FSharp.Azure.Quantum/bin/Debug/net10.0/FSharp.Azure.Quantum.dll"

open System
open FSharp.Azure.Quantum.Business
open FSharp.Azure.Quantum.Business.AutoML

// ============================================================================
// EXAMPLE 1: Minimal Usage - "Zero Config ML"
// ============================================================================

printfn "=== Example 1: Zero-Config AutoML ===\n"

// Generate sample data (customer features and churn labels)
let generateSampleData () =
    let random = Random(42)
    
    // Features: [tenure_months, monthly_spend, support_calls, usage_frequency, satisfaction]
    let features = [|
        for i in 1..100 ->
            [| 
                random.NextDouble() * 36.0              // Tenure: 0-36 months
                50.0 + random.NextDouble() * 150.0      // Spend: $50-$200
                float (random.Next(0, 10))              // Support calls: 0-10
                random.NextDouble() * 30.0              // Usage: 0-30 hrs/week
                random.NextDouble() * 10.0              // Satisfaction: 0-10
            |]
    |]
    
    // Labels: Will customer churn? (1 = yes, 0 = no)
    let labels = [|
        for i in 0..99 ->
            // Simple rule: churn if low spend + low satisfaction + high support calls
            if features.[i].[1] < 100.0 && features.[i].[4] < 5.0 && features.[i].[2] > 5.0 then
                1.0
            else
                0.0
    |]
    
    (features, labels)

let (sampleFeatures, sampleLabels) = generateSampleData()

printfn "Dataset: %d samples, %d features" sampleFeatures.Length sampleFeatures.[0].Length
printfn "Class distribution:"
printfn "  - No churn: %d" (sampleLabels |> Array.filter ((=) 0.0) |> Array.length)
printfn "  - Churn: %d\n" (sampleLabels |> Array.filter ((=) 1.0) |> Array.length)

// MINIMAL USAGE: Just provide data - AutoML does everything else
let result1 = autoML {
    trainWith sampleFeatures sampleLabels
}

match result1 with
| Error msg ->
    printfn "❌ AutoML failed: %s" msg

| Ok automlResult ->
    printfn "✅ AutoML Search Complete!\n"
    
    printfn "=== BEST MODEL FOUND ==="
    printfn "  Model Type: %s" automlResult.BestModelType
    printfn "  Architecture: %A" automlResult.BestArchitecture
    printfn "  Validation Score: %.2f%%" (automlResult.Score * 100.0)
    printfn "  Search Time: %.1f seconds" automlResult.TotalSearchTime.TotalSeconds
    printfn ""
    
    printfn "=== HYPERPARAMETERS ==="
    printfn "  Learning Rate: %f" automlResult.BestHyperparameters.LearningRate
    printfn "  Max Epochs: %d" automlResult.BestHyperparameters.MaxEpochs
    printfn "  Convergence Threshold: %f" automlResult.BestHyperparameters.ConvergenceThreshold
    printfn "  Shots: %d" automlResult.BestHyperparameters.Shots
    printfn ""
    
    printfn "=== SEARCH STATISTICS ==="
    printfn "  Successful Trials: %d" automlResult.SuccessfulTrials
    printfn "  Failed Trials: %d" automlResult.FailedTrials
    printfn "  Total Trials: %d" automlResult.AllTrials.Length
    printfn ""
    
    // Make predictions with best model
    let testSample1 = [| 24.0; 150.0; 1.0; 25.0; 9.0 |]  // Good customer
    let testSample2 = [| 2.0; 60.0; 8.0; 5.0; 3.0 |]     // At-risk customer
    
    match AutoML.predict testSample1 automlResult with
    | Ok (AutoML.BinaryPrediction pred) ->
        printfn "Test Sample 1 (Good Customer):"
        printfn "  Prediction: %s" (if pred.IsPositive then "CHURN" else "STAY")
        printfn "  Confidence: %.1f%%" (pred.Confidence * 100.0)
    | Ok _ -> printfn "Unexpected prediction type"
    | Error e -> printfn "Prediction failed: %s" e
    
    match AutoML.predict testSample2 automlResult with
    | Ok (AutoML.BinaryPrediction pred) ->
        printfn "\nTest Sample 2 (At-Risk Customer):"
        printfn "  Prediction: %s" (if pred.IsPositive then "CHURN ⚠️" else "STAY")
        printfn "  Confidence: %.1f%%" (pred.Confidence * 100.0)
    | Ok _ -> printfn "Unexpected prediction type"
    | Error e -> printfn "Prediction failed: %s" e
    
    printfn ""

// ============================================================================
// EXAMPLE 2: Advanced Configuration - Custom Search Space
// ============================================================================

printfn "\n=== Example 2: Custom Search Configuration ===\n"

// Generate multi-class data (customer segments: 0=Bronze, 1=Silver, 2=Gold, 3=Platinum)
let generateMultiClassData () =
    let random = Random(42)
    
    let features = [|
        for i in 1..80 ->
            let spend = 50.0 + random.NextDouble() * 450.0
            let tenure = random.NextDouble() * 48.0
            let usage = random.NextDouble() * 40.0
            
            [| spend; tenure; usage |]
    |]
    
    let labels = [|
        for i in 0..79 ->
            // Segment based on spend + tenure
            let spend = features.[i].[0]
            let tenure = features.[i].[1]
            
            if spend > 400.0 && tenure > 36.0 then 3.0        // Platinum
            elif spend > 250.0 && tenure > 24.0 then 2.0      // Gold
            elif spend > 150.0 && tenure > 12.0 then 1.0      // Silver
            else 0.0                                           // Bronze
    |]
    
    (features, labels)

let (multiClassFeatures, multiClassLabels) = generateMultiClassData()

printfn "Multi-class dataset: %d samples" multiClassFeatures.Length
printfn "Classes: 0=Bronze, 1=Silver, 2=Gold, 3=Platinum\n"

// ADVANCED USAGE: Control search space
let result2 = autoML {
    trainWith multiClassFeatures multiClassLabels
    
    // Specify what to try
    tryBinaryClassification false        // Don't try binary (we have 4 classes)
    tryMultiClass 4                      // Try multi-class with 4 categories
    tryAnomalyDetection false            // Skip anomaly detection
    tryRegression false                  // Skip regression
    
    // Which architectures to test (Quantum-only per Rule 1)
    tryArchitectures [Quantum; Hybrid]
    
    // Search budget
    maxTrials 15                         // Limit to 15 trials for speed
    maxTimeMinutes 10                    // Stop after 10 minutes
    
    // Validation
    validationSplit 0.25                 // Use 25% for validation
    
    // Reproducibility
    randomSeed 42
    
    // Monitoring
    verbose true                         // Show progress
}

match result2 with
| Error msg ->
    printfn "❌ AutoML failed: %s" msg

| Ok automlResult ->
    printfn "\n✅ Multi-Class AutoML Complete!\n"
    
    printfn "Best Model: %s" automlResult.BestModelType
    printfn "Score: %.2f%%" (automlResult.Score * 100.0)
    printfn ""
    
    // Show top 5 trials
    printfn "=== TOP 5 TRIALS ==="
    automlResult.AllTrials
    |> Array.filter (fun t -> t.Success)
    |> Array.sortByDescending (fun t -> t.Score)
    |> Array.take (min 5 automlResult.SuccessfulTrials)
    |> Array.iteri (fun i trial ->
        let modelTypeStr = 
            match trial.ModelType with
            | AutoML.BinaryClassification -> "Binary"
            | AutoML.MultiClassClassification n -> $"Multi-Class ({n})"
            | AutoML.Regression -> "Regression"
            | AutoML.AnomalyDetection -> "Anomaly"
            | AutoML.SimilaritySearch -> "Similarity"
        
        printfn "%d. %s with %A" (i + 1) modelTypeStr trial.Architecture
        printfn "   Score: %.2f%%, Time: %.1fs" (trial.Score * 100.0) trial.TrainingTime.TotalSeconds
        printfn "   LR: %f, Epochs: %d" trial.Hyperparameters.LearningRate trial.Hyperparameters.MaxEpochs
        printfn ""
    )
    
    // Test predictions
    let testCustomer1 = [| 450.0; 40.0; 35.0 |]  // High spend, long tenure, high usage
    let testCustomer2 = [| 100.0; 6.0; 10.0 |]   // Low spend, short tenure
    
    match AutoML.predict testCustomer1 automlResult with
    | Ok (AutoML.CategoryPrediction pred) ->
        let segment = match pred.Category with
                      | 0 -> "Bronze"
                      | 1 -> "Silver"
                      | 2 -> "Gold"
                      | 3 -> "Platinum"
                      | _ -> "Unknown"
        printfn "Test Customer 1:"
        printfn "  Predicted Segment: %s (confidence: %.1f%%)" segment (pred.Confidence * 100.0)
        printfn "  Probabilities: %A" pred.Probabilities
    | Ok _ -> printfn "Unexpected prediction type"
    | Error e -> printfn "Prediction failed: %s" e
    
    printfn ""
    
    match AutoML.predict testCustomer2 automlResult with
    | Ok (AutoML.CategoryPrediction pred) ->
        let segment = match pred.Category with
                      | 0 -> "Bronze"
                      | 1 -> "Silver"
                      | 2 -> "Gold"
                      | 3 -> "Platinum"
                      | _ -> "Unknown"
        printfn "Test Customer 2:"
        printfn "  Predicted Segment: %s (confidence: %.1f%%)" segment (pred.Confidence * 100.0)
    | Ok _ -> printfn "Unexpected prediction type"
    | Error e -> printfn "Prediction failed: %s" e

// ============================================================================
// EXAMPLE 3: Regression Problem - Revenue Prediction
// ============================================================================

printfn "\n\n=== Example 3: AutoML for Regression ===\n"

let generateRegressionData () =
    let random = Random(42)
    
    let features = [|
        for i in 1..70 ->
            let spend = 50.0 + random.NextDouble() * 200.0
            let usage = random.NextDouble() * 30.0
            let tenure = random.NextDouble() * 36.0
            
            [| spend; usage; tenure |]
    |]
    
    // Target: Annual revenue (based on features with some noise)
    let targets = [|
        for i in 0..69 ->
            let baseRevenue = features.[i].[0] * 12.0                    // Monthly spend × 12
            let usageBonus = features.[i].[1] * 20.0                     // Usage contribution
            let tenureBonus = features.[i].[2] * 15.0                    // Loyalty value
            let noise = random.NextDouble() * 200.0 - 100.0              // Random variation
            
            baseRevenue + usageBonus + tenureBonus + noise
    |]
    
    (features, targets)

let (regressionFeatures, regressionTargets) = generateRegressionData()

printfn "Regression dataset: %d samples" regressionFeatures.Length
printfn "Target: Annual revenue prediction\n"

let result3 = autoML {
    trainWith regressionFeatures regressionTargets
    
    // Focus on regression
    tryBinaryClassification false
    // tryMultiClass false  // Skip - takes int not bool
    tryAnomalyDetection false
    tryRegression true
    
    // Test quantum architectures only (Rule 1: Quantum-only library)
    tryArchitectures [Hybrid; Quantum]
    
    maxTrials 12
    verbose true
}

match result3 with
| Error msg ->
    printfn "❌ AutoML failed: %s" msg

| Ok automlResult ->
    printfn "\n✅ Regression AutoML Complete!\n"
    
    printfn "Best Model: %s" automlResult.BestModelType
    printfn "R² Score: %.4f" automlResult.Score
    printfn "Architecture: %A" automlResult.BestArchitecture
    printfn ""
    
    // Test revenue predictions
    let testCustomer1 = [| 180.0; 25.0; 30.0 |]  // High value
    let testCustomer2 = [| 70.0; 10.0; 6.0 |]    // Low value
    
    match AutoML.predict testCustomer1 automlResult with
    | Ok (AutoML.RegressionPrediction pred) ->
        printfn "High-Value Customer:"
        printfn "  Predicted Annual Revenue: $%.2f" pred.Value
        printfn "  💡 Action: VIP treatment, account manager"
    | Ok _ -> printfn "Unexpected prediction type"
    | Error e -> printfn "Prediction failed: %s" e
    
    printfn ""
    
    match AutoML.predict testCustomer2 automlResult with
    | Ok (AutoML.RegressionPrediction pred) ->
        printfn "Low-Value Customer:"
        printfn "  Predicted Annual Revenue: $%.2f" pred.Value
        printfn "  💡 Action: Upsell opportunities, engagement campaigns"
    | Ok _ -> printfn "Unexpected prediction type"
    | Error e -> printfn "Prediction failed: %s" e

// ============================================================================
// EXAMPLE 4: Compare All Approaches - Full Analysis
// ============================================================================

printfn "\n\n=== Example 4: Full Search - Compare Everything ===\n"

// Reuse first example data
let result4 = autoML {
    trainWith sampleFeatures sampleLabels
    
    // Try EVERYTHING
    tryBinaryClassification true
    tryAnomalyDetection true
    tryRegression true
    
    // Quantum architectures only
    tryArchitectures [Quantum; Hybrid]
    
    // Large search
    maxTrials 30
    
    verbose false  // Quiet mode
}

match result4 with
| Error msg ->
    printfn "❌ AutoML failed: %s" msg

| Ok automlResult ->
    printfn "✅ Full Search Complete!\n"
    
    printfn "=== COMPREHENSIVE REPORT ===\n"
    
    // Group by model type
    let byModelType = 
        automlResult.AllTrials
        |> Array.filter (fun t -> t.Success)
        |> Array.groupBy (fun t -> 
            match t.ModelType with
            | AutoML.BinaryClassification -> "Binary Classification"
            | AutoML.MultiClassClassification n -> $"Multi-Class ({n})"
            | AutoML.Regression -> "Regression"
            | AutoML.AnomalyDetection -> "Anomaly Detection"
            | AutoML.SimilaritySearch -> "Similarity Search"
        )
    
    for (modelType, trials) in byModelType do
        printfn "%s:" modelType
        
        let bestScore = trials |> Array.map (fun t -> t.Score) |> Array.max
        let avgScore = trials |> Array.averageBy (fun t -> t.Score)
        let quantumTrials = trials |> Array.filter (fun t -> t.Architecture = AutoML.Quantum)
        let hybridTrials = trials |> Array.filter (fun t -> t.Architecture = AutoML.Hybrid)
        
        printfn "  Best Score: %.2f%%" (bestScore * 100.0)
        printfn "  Avg Score: %.2f%%" (avgScore * 100.0)
        printfn "  Quantum trials: %d (best: %.2f%%)" 
            quantumTrials.Length 
            (if quantumTrials.Length > 0 then (quantumTrials |> Array.map (fun t -> t.Score) |> Array.max) * 100.0 else 0.0)
        printfn "  Hybrid trials: %d (best: %.2f%%)" 
            hybridTrials.Length 
            (if hybridTrials.Length > 0 then (hybridTrials |> Array.map (fun t -> t.Score) |> Array.max) * 100.0 else 0.0)
        printfn ""
    
    printfn "=== WINNER ==="
    printfn "Model: %s" automlResult.BestModelType
    printfn "Architecture: %A" automlResult.BestArchitecture
    printfn "Score: %.2f%%" (automlResult.Score * 100.0)
    printfn ""
    
    printfn "=== INSIGHTS ==="
    
    // Architecture comparison
    let successfulTrials = automlResult.AllTrials |> Array.filter (fun t -> t.Success)
    let quantumAvg = 
        successfulTrials 
        |> Array.filter (fun t -> t.Architecture = AutoML.Quantum) 
        |> Array.averageBy (fun t -> t.Score)
    let hybridAvg = 
        successfulTrials 
        |> Array.filter (fun t -> t.Architecture = AutoML.Hybrid) 
        |> Array.averageBy (fun t -> t.Score)
    
    printfn "Average Performance by Architecture:"
    printfn "  Quantum: %.2f%%" (quantumAvg * 100.0)
    printfn "  Hybrid: %.2f%%" (hybridAvg * 100.0)
    printfn ""
    
    let bestArch = 
        [("Quantum", quantumAvg); ("Hybrid", hybridAvg)]
        |> List.maxBy snd
        |> fst
    
    printfn "💡 Recommendation: %s architecture performs best on this data" bestArch

// ============================================================================
// EXAMPLE 5: Production Integration
// ============================================================================

printfn "\n\n=== Example 5: Production Workflow ===\n"

/// Production-ready AutoML workflow
let productionAutoMLWorkflow (features: float array array) (labels: float array) =
    
    printfn "🔍 Step 1: Running AutoML search..."
    
    let result = autoML {
        trainWith features labels
        
        // Production settings (Quantum-only library)
        tryArchitectures [Hybrid; Quantum]
        maxTrials 15
        maxTimeMinutes 5
        validationSplit 0.2
        
        verbose false
        randomSeed 42  // Reproducible
    }
    
    match result with
    | Error msg ->
        printfn "❌ AutoML failed: %s" msg
        None
    
    | Ok automlResult ->
        printfn "✅ AutoML complete: %s (%.2f%%)" 
            automlResult.BestModelType (automlResult.Score * 100.0)
        
        printfn "\n📊 Step 2: Validating model quality..."
        
        // Quality gate: Require at least 70% score
        if automlResult.Score < 0.70 then
            printfn "⚠️  Model quality insufficient (%.2f%% < 70%%)" (automlResult.Score * 100.0)
            printfn "💡 Recommendation: Collect more training data or improve feature engineering"
            None
        else
            printfn "✅ Model quality acceptable (%.2f%% >= 70%%)" (automlResult.Score * 100.0)
            
            printfn "\n🚀 Step 3: Model ready for deployment"
            printfn "   - Model: %s" automlResult.BestModelType
            printfn "   - Architecture: %A" automlResult.BestArchitecture
            printfn "   - Validation Score: %.2f%%" (automlResult.Score * 100.0)
            printfn "   - Successful Trials: %d/%d" 
                automlResult.SuccessfulTrials 
                (automlResult.SuccessfulTrials + automlResult.FailedTrials)
            
            Some automlResult

// Run production workflow
match productionAutoMLWorkflow sampleFeatures sampleLabels with
| Some model ->
    printfn "\n✅ Production model ready for deployment!"
    printfn "\nNext steps:"
    printfn "  1. Deploy via REST API or Azure Functions"
    printfn "  2. Set up monitoring and alerting"
    printfn "  3. Schedule periodic retraining"
    printfn "  4. A/B test against current baseline"

| None ->
    printfn "\n❌ Model did not meet quality requirements"
    printfn "\nNext steps:"
    printfn "  1. Collect more training data"
    printfn "  2. Improve feature engineering"
    printfn "  3. Review data quality and labeling"

printfn "\n\n=== Tutorial Complete ===\n"
printfn "You've learned how to:"
printfn "  ✅ Use AutoML for zero-config ML (Example 1)"
printfn "  ✅ Customize search space and parameters (Example 2)"
printfn "  ✅ Handle regression problems (Example 3)"
printfn "  ✅ Compare all approaches comprehensively (Example 4)"
printfn "  ✅ Integrate AutoML into production workflows (Example 5)"
printfn ""
printfn "AutoML is perfect when:"
printfn "  - You need a quick prototype"
printfn "  - You're not sure which algorithm to use"
printfn "  - You want to establish a baseline"
printfn "  - You want to compare all approaches at once"
printfn ""
printfn "For production, consider using the specific builder for your use case"
printfn "(BinaryClassificationBuilder, PredictiveModelBuilder, etc.) for more control."
