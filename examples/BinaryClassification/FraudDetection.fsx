/// Binary Classification Example: Fraud Detection
/// 
/// This example demonstrates how to use the BinaryClassificationBuilder
/// to detect fraudulent transactions without understanding quantum mechanics.
///
/// BUSINESS PROBLEM:
/// Automatically identify suspicious credit card transactions to prevent fraud.
/// 
/// APPROACH:
/// Train a quantum classifier on historical transaction data (features + labels).
/// Use the trained model to classify new transactions in real-time.

//#r "nuget: FSharp.Azure.Quantum"
#r "../../src/FSharp.Azure.Quantum/bin/Debug/net10.0/FSharp.Azure.Quantum.dll"

open System
open FSharp.Azure.Quantum.Business
open FSharp.Azure.Quantum.Business.BinaryClassifier

// ============================================================================
// SAMPLE DATA - Credit Card Transactions
// ============================================================================

/// Generate synthetic transaction data for demonstration
/// In production, load from database or CSV
let generateSampleData () =
    let random = Random(42)  // Fixed seed for reproducibility
    
    // Feature engineering: Extract meaningful features from transactions
    // Features: [amount, time_of_day, merchant_category, distance_from_home, frequency]
    
    // Normal transactions (label = 0)
    let normalTransactions =
        [| for i in 1..40 ->
            [| 
                random.NextDouble() * 100.0              // Small amounts
                random.NextDouble() * 24.0               // Random time
                float (random.Next(1, 5))                // Common categories
                random.NextDouble() * 10.0               // Close to home
                random.NextDouble() * 5.0                // Normal frequency
            |]
        |]
    
    // Fraudulent transactions (label = 1) - different patterns
    let fraudulentTransactions =
        [| for i in 1..10 ->
            [| 
                500.0 + random.NextDouble() * 500.0     // Large amounts
                random.NextDouble() * 24.0               // Random time
                float (random.Next(5, 10))               // Unusual categories
                50.0 + random.NextDouble() * 100.0       // Far from home
                10.0 + random.NextDouble() * 10.0        // High frequency
            |]
        |]
    
    // Combine datasets
    let allTransactions = Array.append normalTransactions fraudulentTransactions
    let allLabels = Array.append (Array.create 40 0) (Array.create 10 1)
    
    // Shuffle data
    let indices = [| 0 .. allTransactions.Length - 1 |]
    let shuffled = 
        indices 
        |> Array.sortBy (fun _ -> random.Next())
        |> Array.map (fun i -> allTransactions.[i], allLabels.[i])
    
    let trainX = shuffled |> Array.map fst
    let trainY = shuffled |> Array.map snd
    
    (trainX, trainY)

// ============================================================================
// EXAMPLE 1: Minimal Configuration
// ============================================================================

printfn "=== Example 1: Fraud Detection (Minimal Configuration) ===\n"

let (trainX, trainY) = generateSampleData()

printfn "Training on %d transactions..." trainX.Length
printfn "  - Normal transactions: %d" (trainY |> Array.filter ((=) 0) |> Array.length)
printfn "  - Fraudulent transactions: %d\n" (trainY |> Array.filter ((=) 1) |> Array.length)

// Train with minimal configuration - system picks smart defaults
let result1 = binaryClassification {
    trainWith trainX trainY
}

match result1 with
| Error msg ->
    printfn "❌ Training failed: %s" msg

| Ok classifier ->
    printfn "✅ Training complete!"
    printfn "  - Architecture: %A" classifier.Metadata.Architecture
    printfn "  - Training accuracy: %.2f%%" (classifier.Metadata.TrainingAccuracy * 100.0)
    printfn "  - Training time: %A\n" classifier.Metadata.TrainingTime
    
    // Test on new transaction
    let newTransaction = [| 600.0; 14.5; 7.0; 80.0; 12.0 |]  // Suspicious!
    
    match BinaryClassifier.predict newTransaction classifier with
    | Error msg ->
        printfn "❌ Prediction failed: %s" msg
    
    | Ok prediction ->
        printfn "New Transaction Analysis:"
        printfn "  Amount: $600, Late night, Unusual category, Far from home"
        printfn ""
        printfn "  Prediction: %s" (if prediction.IsPositive then "🚨 FRAUD" else "✅ LEGITIMATE")
        printfn "  Confidence: %.1f%%" (prediction.Confidence * 100.0)
        printfn ""
        
        if prediction.IsPositive && prediction.Confidence > 0.7 then
            printfn "  ⚠️  RECOMMENDED ACTION: Block transaction and contact customer"

// ============================================================================
// EXAMPLE 2: Production Configuration
// ============================================================================

printfn "\n=== Example 2: Production Fraud Detector ===\n"

let result2 = binaryClassification {
    trainWith trainX trainY
    
    // Architecture choice
    architecture Quantum  // or Hybrid for quantum-classical approach
    
    // Training parameters
    learningRate 0.01
    maxEpochs 50
    convergenceThreshold 0.001
    
    // Enable logging
    verbose true
    
    // Save for deployment
    saveModelTo "fraud_detector.model"
    note "Credit card fraud detector - trained on 2024 data"
}

match result2 with
| Error msg ->
    printfn "❌ Training failed: %s" msg

| Ok classifier ->
    printfn "✅ Model trained and saved to 'fraud_detector.model'\n"
    
    // Batch processing simulation
    let testTransactions = [|
        [| 50.0; 10.0; 2.0; 5.0; 2.0 |]       // Normal
        [| 800.0; 23.0; 8.0; 120.0; 15.0 |]   // Suspicious
        [| 25.0; 14.0; 1.0; 3.0; 1.0 |]       // Normal
        [| 1500.0; 3.0; 9.0; 200.0; 20.0 |]   // Very suspicious
    |]
    
    printfn "Processing %d transactions:\n" testTransactions.Length
    
    testTransactions
    |> Array.iteri (fun i tx ->
        match BinaryClassifier.predict tx classifier with
        | Ok pred ->
            let status = if pred.IsPositive then "🚨 FRAUD" else "✅ OK"
            printfn "Transaction #%d: %s (%.0f%% confidence)" (i+1) status (pred.Confidence * 100.0)
        | Error msg ->
            printfn "Transaction #%d: ⚠️  Error: %s" (i+1) msg
    )

// ============================================================================
// EXAMPLE 3: Model Evaluation
// ============================================================================

printfn "\n=== Example 3: Model Evaluation ===\n"

// In production, evaluate on holdout test set
match result2 with
| Ok classifier ->
    
    // Simulate test set (in production, use separate held-out data)
    let (testX, testY) = generateSampleData()
    
    match BinaryClassifier.evaluate testX testY classifier with
    | Error msg ->
        printfn "❌ Evaluation failed: %s" msg
    
    | Ok metrics ->
        printfn "Model Performance Metrics:"
        printfn "  Accuracy:  %.2f%%" (metrics.Accuracy * 100.0)
        printfn "  Precision: %.2f%%" (metrics.Precision * 100.0)
        printfn "  Recall:    %.2f%%" (metrics.Recall * 100.0)
        printfn "  F1 Score:  %.2f%%" (metrics.F1Score * 100.0)
        printfn ""
        printfn "Confusion Matrix:"
        printfn "  True Positives:  %d (correctly identified fraud)" metrics.TruePositives
        printfn "  True Negatives:  %d (correctly identified legitimate)" metrics.TrueNegatives
        printfn "  False Positives: %d (legitimate flagged as fraud)" metrics.FalsePositives
        printfn "  False Negatives: %d (fraud missed)" metrics.FalseNegatives

| Error _ -> ()

// ============================================================================
// EXAMPLE 4: Load Saved Model
// ============================================================================

printfn "\n=== Example 4: Load and Use Saved Model ===\n"

// In a production service, load the pre-trained model
match BinaryClassifier.load "fraud_detector.model" with
| Error msg ->
    printfn "❌ Failed to load model: %s" msg

| Ok loadedClassifier ->
    printfn "✅ Model loaded successfully\n"
    
    // Use in production API
    let incomingTransaction = [| 700.0; 2.0; 8.0; 100.0; 14.0 |]
    
    match BinaryClassifier.predict incomingTransaction loadedClassifier with
    | Ok prediction ->
        printfn "API Response:"
        printfn "  transaction_id: TXN-12345"
        printfn "  is_fraud: %b" prediction.IsPositive
        printfn "  confidence: %.2f" prediction.Confidence
        printfn "  recommendation: %s" 
            (if prediction.IsPositive && prediction.Confidence > 0.8 then "BLOCK"
             elif prediction.IsPositive && prediction.Confidence > 0.5 then "REVIEW"
             else "ALLOW")
    
    | Error msg ->
        printfn "❌ Prediction failed: %s" msg

// ============================================================================
// BUSINESS INTEGRATION PATTERNS
// ============================================================================

printfn "\n=== Integration Patterns ===\n"

printfn "Real-time API Integration:"
printfn """
[<HttpPost("api/check-fraud")>]
let checkFraud (transaction: Transaction) =
    match classifier.Predict(transaction) with
    | Ok pred when pred.IsPositive && pred.Confidence > 0.8 ->
        BlockTransaction(transaction)
        StatusCode(403, "Transaction blocked - fraud detected")
    | Ok pred when pred.IsPositive && pred.Confidence > 0.5 ->
        FlagForReview(transaction)
        Ok("Transaction flagged for manual review")
    | Ok _ ->
        Ok("Transaction approved")
    | Error e ->
        StatusCode(500, e)
"""

printfn "\nBatch Processing:"
printfn """
let processNightlyBatch() =
    db.GetPendingTransactions()
    |> Array.Parallel.map (fun tx ->
        let prediction = classifier.Predict(tx)
        { tx with RiskScore = prediction.Confidence })
    |> Array.filter (fun tx -> tx.RiskScore > 0.7)
    |> db.SaveHighRiskTransactions
"""

printfn "\n✅ Example complete! See code for integration patterns."
