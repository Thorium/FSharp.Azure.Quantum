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
///
/// Usage:
///   dotnet fsi FraudDetection.fsx                                       (defaults)
///   dotnet fsi FraudDetection.fsx -- --help                             (show options)
///   dotnet fsi FraudDetection.fsx -- --example 2 --epochs 100
///   dotnet fsi FraudDetection.fsx -- --quiet --output results.json
///   dotnet fsi FraudDetection.fsx -- --example all --csv results.csv

(*
===============================================================================
 Background Theory
===============================================================================

Fraud detection is a binary classification problem: given transaction features
(amount, time, location, merchant category, etc.), predict whether a transaction
is legitimate (0) or fraudulent (1). Classical approaches use logistic regression,
random forests, or neural networks. Quantum machine learning offers potential
advantages through high-dimensional feature spaces and quantum kernel methods
that may capture complex fraud patterns classical models miss.

Variational Quantum Classifiers (VQCs) encode transaction features into quantum
states via feature maps, then apply parameterized quantum circuits trained to
separate fraud from non-fraud. The quantum feature space is exponentially large
(2^n-dimensional for n qubits), potentially capturing non-linear decision boundaries
more efficiently than classical kernels. For imbalanced datasets (fraud is rare),
quantum approaches can be combined with classical techniques like SMOTE oversampling.

Key Equations:
  - Feature encoding: |psi(x)> = U(x)|0>^n where x = transaction features
  - Classification: P(fraud|x) = |<1|V(theta)|psi(x)>|^2 for trained parameters theta
  - Loss function: L = -Sum_i [y_i log(p_i) + (1-y_i) log(1-p_i)]  (cross-entropy)
  - Precision: TP / (TP + FP)  (fraud predictions that are correct)
  - Recall: TP / (TP + FN)  (actual frauds that are detected)
  - F1 Score: 2 * (Precision * Recall) / (Precision + Recall)

Quantum Advantage:
  Fraud detection benefits from quantum ML in several ways: (1) High-dimensional
  feature spaces may reveal fraud patterns invisible to classical methods.
  (2) Quantum kernels provide rigorous similarity measures between transactions.
  (3) For real-time scoring, quantum circuits execute in microseconds once trained.
  Current applications use hybrid classical-quantum pipelines: classical preprocessing
  (feature engineering, resampling) followed by quantum classification. Financial
  institutions exploring quantum fraud detection include JPMorgan, Goldman Sachs,
  and BBVA.

References:
  [1] Havlicek et al., "Supervised learning with quantum-enhanced feature spaces",
      Nature 567, 209-212 (2019). https://doi.org/10.1038/s41586-019-0980-2
  [2] Kyriienko & Magnusson, "Unsupervised Machine Learning on a Hybrid Quantum
      Computer", arXiv:2001.03622 (2020). https://arxiv.org/abs/2001.03622
  [3] Egger et al., "Credit Risk Analysis using Quantum Computers", IEEE ICQC (2021).
      https://doi.org/10.1109/TQE.2021.3030319
  [4] Wikipedia: Quantum_machine_learning
      https://en.wikipedia.org/wiki/Quantum_machine_learning
*)

//#r "nuget: FSharp.Azure.Quantum"
#r "../../src/FSharp.Azure.Quantum/bin/Debug/net10.0/FSharp.Azure.Quantum.dll"
#r "nuget: Microsoft.Extensions.Logging.Abstractions, 10.0.0"

#load "../_common/Cli.fs"
#load "../_common/Data.fs"
#load "../_common/Reporting.fs"

open System
open FSharp.Azure.Quantum.Business
open FSharp.Azure.Quantum.Business.BinaryClassifier
open FSharp.Azure.Quantum.Core.BackendAbstraction
open FSharp.Azure.Quantum.Backends.LocalBackend
open FSharp.Azure.Quantum.Examples.Common

// ==============================================================================
// CLI ARGUMENT PARSING
// ==============================================================================

let argv = fsi.CommandLineArgs |> Array.skip 1
let args = Cli.parse argv

Cli.exitIfHelp
    "FraudDetection.fsx"
    "Binary classification for credit card fraud detection using quantum ML."
    [ { Cli.OptionSpec.Name = "example";  Description = "Example to run (1-4 or all)";       Default = Some "all" }
      { Cli.OptionSpec.Name = "epochs";   Description = "Max training epochs";                Default = Some "50" }
      { Cli.OptionSpec.Name = "lr";       Description = "Learning rate";                      Default = Some "0.01" }
      { Cli.OptionSpec.Name = "output";   Description = "Write results to JSON file";         Default = None }
      { Cli.OptionSpec.Name = "csv";      Description = "Write results to CSV file";          Default = None }
      { Cli.OptionSpec.Name = "quiet";    Description = "Suppress informational output";      Default = None } ]
    args

let quiet = Cli.hasFlag "quiet" args
let outputPath = Cli.tryGet "output" args
let csvPath = Cli.tryGet "csv" args
let exampleFilter = Cli.getOr "example" "all" args
let cliEpochs = Cli.getIntOr "epochs" 50 args
let cliLearningRate = Cli.getFloatOr "lr" 0.01 args

let shouldRun ex =
    exampleFilter = "all" || exampleFilter = string ex

/// Explicit quantum backend (Rule 1: all code depends on IQuantumBackend)
let quantumBackend = LocalBackend() :> IQuantumBackend

// Accumulate results for JSON/CSV output
let results = ResizeArray<{| Example: string; Status: string; Details: Map<string, obj> |}>()

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

let (trainX, trainY) = generateSampleData()

// ============================================================================
// EXAMPLE 1: Minimal Configuration
// ============================================================================

if shouldRun 1 then
    if not quiet then
        printfn "=== Example 1: Fraud Detection (Minimal Configuration) ===\n"
        printfn "Training on %d transactions..." trainX.Length
        printfn "  - Normal transactions: %d" (trainY |> Array.filter ((=) 0) |> Array.length)
        printfn "  - Fraudulent transactions: %d\n" (trainY |> Array.filter ((=) 1) |> Array.length)

    // Train with minimal configuration - system picks smart defaults
    let result1 = binaryClassification {
        trainWith trainX trainY
        backend quantumBackend
    }

    match result1 with
    | Error err ->
        if not quiet then printfn "Training failed: %s" err.Message
        results.Add({| Example = "1-minimal"; Status = "error"; Details = Map.ofList ["error", box err.Message] |})

    | Ok classifier ->
        if not quiet then
            printfn "Training complete!"
            printfn "  - Architecture: %A" classifier.Metadata.Architecture
            printfn "  - Training accuracy: %.2f%%" (classifier.Metadata.TrainingAccuracy * 100.0)
            printfn "  - Training time: %A\n" classifier.Metadata.TrainingTime
        
        // Test on new transaction
        let newTransaction = [| 600.0; 14.5; 7.0; 80.0; 12.0 |]  // Suspicious!
        
        match BinaryClassifier.predict newTransaction classifier with
        | Error err ->
            if not quiet then printfn "Prediction failed: %s" err.Message
        | Ok prediction ->
            if not quiet then
                printfn "New Transaction Analysis:"
                printfn "  Amount: $600, Late night, Unusual category, Far from home"
                printfn "  Prediction: %s" (if prediction.IsPositive then "FRAUD" else "LEGITIMATE")
                printfn "  Confidence: %.1f%%" (prediction.Confidence * 100.0)
                if prediction.IsPositive && prediction.Confidence > 0.7 then
                    printfn "  RECOMMENDED ACTION: Block transaction and contact customer"
                printfn ""
            
            results.Add({|
                Example = "1-minimal"
                Status = "ok"
                Details = Map.ofList [
                    "architecture", box (sprintf "%A" classifier.Metadata.Architecture)
                    "training_accuracy", box (classifier.Metadata.TrainingAccuracy * 100.0)
                    "prediction", box (if prediction.IsPositive then "FRAUD" else "LEGITIMATE")
                    "confidence", box (prediction.Confidence * 100.0)
                ]
            |})

// ============================================================================
// EXAMPLE 2: Production Configuration
// ============================================================================

if shouldRun 2 then
    if not quiet then printfn "=== Example 2: Production Fraud Detector ===\n"

    let result2 = binaryClassification {
        trainWith trainX trainY
        backend quantumBackend
        
        // Architecture choice
        architecture Quantum  // or Hybrid for quantum-classical approach
        
        // Training parameters
        learningRate cliLearningRate
        maxEpochs cliEpochs
        convergenceThreshold 0.001
        
        // Enable logging
        verbose (not quiet)
        
        // Save for deployment
        saveModelTo "fraud_detector.model"
        note "Credit card fraud detector - trained on 2024 data"
    }

    match result2 with
    | Error err ->
        if not quiet then printfn "Training failed: %s" err.Message
        results.Add({| Example = "2-production"; Status = "error"; Details = Map.ofList ["error", box err.Message] |})

    | Ok classifier ->
        if not quiet then printfn "Model trained and saved to 'fraud_detector.model'\n"
        
        // Batch processing simulation
        let testTransactions = [|
            [| 50.0; 10.0; 2.0; 5.0; 2.0 |]       // Normal
            [| 800.0; 23.0; 8.0; 120.0; 15.0 |]   // Suspicious
            [| 25.0; 14.0; 1.0; 3.0; 1.0 |]       // Normal
            [| 1500.0; 3.0; 9.0; 200.0; 20.0 |]   // Very suspicious
        |]
        
        if not quiet then printfn "Processing %d transactions:\n" testTransactions.Length
        
        let batchResults = ResizeArray<{| Index: int; Status: string; Confidence: float |}>()
        
        testTransactions
        |> Array.iteri (fun i tx ->
            match BinaryClassifier.predict tx classifier with
            | Ok pred ->
                let status = if pred.IsPositive then "FRAUD" else "OK"
                if not quiet then
                    printfn "Transaction #%d: %s (%.0f%% confidence)" (i+1) status (pred.Confidence * 100.0)
                batchResults.Add({| Index = i + 1; Status = status; Confidence = pred.Confidence * 100.0 |})
            | Error err ->
                if not quiet then
                    printfn "Transaction #%d: Error: %s" (i+1) err.Message
        )
        
        results.Add({|
            Example = "2-production"
            Status = "ok"
            Details = Map.ofList [
                "epochs", box cliEpochs
                "learning_rate", box cliLearningRate
                "batch_size", box testTransactions.Length
                "batch_results", box (batchResults |> Seq.toArray)
            ]
        |})

// ============================================================================
// EXAMPLE 3: Model Evaluation
// ============================================================================

if shouldRun 3 then
    if not quiet then printfn "\n=== Example 3: Model Evaluation ===\n"

    // Re-train for evaluation (reuse production config)
    let evalModel = binaryClassification {
        trainWith trainX trainY
        backend quantumBackend
        architecture Quantum
        learningRate cliLearningRate
        maxEpochs cliEpochs
        convergenceThreshold 0.001
        verbose false
    }

    match evalModel with
    | Ok classifier ->
        // Simulate test set (in production, use separate held-out data)
        let (testX, testY) = generateSampleData()
        
        match BinaryClassifier.evaluate testX testY classifier with
        | Error err ->
            if not quiet then printfn "Evaluation failed: %s" err.Message
        | Ok metrics ->
            if not quiet then
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
                printfn ""
            
            results.Add({|
                Example = "3-evaluation"
                Status = "ok"
                Details = Map.ofList [
                    "accuracy", box (metrics.Accuracy * 100.0)
                    "precision", box (metrics.Precision * 100.0)
                    "recall", box (metrics.Recall * 100.0)
                    "f1_score", box (metrics.F1Score * 100.0)
                    "true_positives", box metrics.TruePositives
                    "true_negatives", box metrics.TrueNegatives
                    "false_positives", box metrics.FalsePositives
                    "false_negatives", box metrics.FalseNegatives
                ]
            |})

    | Error err ->
        if not quiet then printfn "Training failed: %s" err.Message
        results.Add({| Example = "3-evaluation"; Status = "error"; Details = Map.ofList ["error", box err.Message] |})

// ============================================================================
// EXAMPLE 4: Load Saved Model
// ============================================================================

if shouldRun 4 then
    if not quiet then printfn "=== Example 4: Load and Use Saved Model ===\n"

    // In a production service, load the pre-trained model
    match BinaryClassifier.load "fraud_detector.model" with
    | Error err ->
        if not quiet then printfn "Failed to load model: %s" err.Message
        results.Add({| Example = "4-load-model"; Status = "skipped"; Details = Map.ofList ["reason", box "Model file not found (run example 2 first)"] |})

    | Ok loadedClassifier ->
        if not quiet then printfn "Model loaded successfully\n"
        
        // Use in production API
        let incomingTransaction = [| 700.0; 2.0; 8.0; 100.0; 14.0 |]
        
        match BinaryClassifier.predict incomingTransaction loadedClassifier with
        | Ok prediction ->
            let recommendation =
                if prediction.IsPositive && prediction.Confidence > 0.8 then "BLOCK"
                elif prediction.IsPositive && prediction.Confidence > 0.5 then "REVIEW"
                else "ALLOW"
            
            if not quiet then
                printfn "API Response:"
                printfn "  transaction_id: TXN-12345"
                printfn "  is_fraud: %b" prediction.IsPositive
                printfn "  confidence: %.2f" prediction.Confidence
                printfn "  recommendation: %s" recommendation
                printfn ""
            
            results.Add({|
                Example = "4-load-model"
                Status = "ok"
                Details = Map.ofList [
                    "is_fraud", box prediction.IsPositive
                    "confidence", box prediction.Confidence
                    "recommendation", box recommendation
                ]
            |})
        
        | Error err ->
            if not quiet then printfn "Prediction failed: %s" err.Message

// ============================================================================
// INTEGRATION PATTERNS
// ============================================================================

if not quiet && exampleFilter = "all" then
    printfn "=== Integration Patterns ===\n"

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

    printfn "Batch Processing:"
    printfn """
let processNightlyBatch() =
    db.GetPendingTransactions()
    |> Array.Parallel.map (fun tx ->
        let prediction = classifier.Predict(tx)
        { tx with RiskScore = prediction.Confidence })
    |> Array.filter (fun tx -> tx.RiskScore > 0.7)
    |> db.SaveHighRiskTransactions
"""

// ============================================================================
// OUTPUT
// ============================================================================

if outputPath.IsSome then
    let payload = {| script = "FraudDetection.fsx"; timestamp = DateTime.UtcNow; results = results |> Seq.toArray |}
    Reporting.writeJson outputPath.Value payload
    if not quiet then printfn "Results written to %s" outputPath.Value

if csvPath.IsSome then
    let header = ["example"; "status"; "detail"]
    let rows =
        results
        |> Seq.map (fun r ->
            let detail =
                r.Details
                |> Map.toList
                |> List.map (fun (k, v) -> sprintf "%s=%O" k v)
                |> String.concat "; "
            [r.Example; r.Status; detail])
        |> Seq.toList
    Reporting.writeCsv csvPath.Value header rows
    if not quiet then printfn "CSV written to %s" csvPath.Value

// ============================================================================
// USAGE HINTS
// ============================================================================

if not quiet && argv.Length = 0 && outputPath.IsNone && csvPath.IsNone then
    printfn "Example complete! See code for integration patterns."
    printfn ""
    printfn "Try these options:"
    printfn "  dotnet fsi FraudDetection.fsx -- --help"
    printfn "  dotnet fsi FraudDetection.fsx -- --example 2 --epochs 100"
    printfn "  dotnet fsi FraudDetection.fsx -- --quiet --output results.json"
    printfn "  dotnet fsi FraudDetection.fsx -- --example all --csv results.csv"
