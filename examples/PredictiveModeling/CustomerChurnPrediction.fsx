/// Predictive Modeling Example: Customer Churn Prediction
/// 
/// This example demonstrates how to use the PredictiveModelBuilder
/// to predict when customers will leave (churn) without understanding quantum mechanics.
///
/// BUSINESS PROBLEM:
/// Predict which customers are likely to churn and WHEN they will churn
/// so you can take proactive retention actions.
/// 
/// APPROACH:
/// Multi-class classification with 4 categories:
/// - Class 0: Will stay (no churn risk)
/// - Class 1: Will churn within 30 days (urgent!)
/// - Class 2: Will churn within 60 days (warning)
/// - Class 3: Will churn within 90 days (monitor)
///
/// Usage:
///   dotnet fsi CustomerChurnPrediction.fsx                                  (defaults)
///   dotnet fsi CustomerChurnPrediction.fsx -- --help                        (show options)
///   dotnet fsi CustomerChurnPrediction.fsx -- --example 3 --epochs 100
///   dotnet fsi CustomerChurnPrediction.fsx -- --quiet --output results.json
///   dotnet fsi CustomerChurnPrediction.fsx -- --example all --csv results.csv

//#r "nuget: FSharp.Azure.Quantum"
#r "../../src/FSharp.Azure.Quantum/bin/Debug/net10.0/FSharp.Azure.Quantum.dll"

#load "../_common/Cli.fs"
#load "../_common/Data.fs"
#load "../_common/Reporting.fs"

open System
open FSharp.Azure.Quantum.Business
open FSharp.Azure.Quantum.Business.PredictiveModel
open FSharp.Azure.Quantum.Core.BackendAbstraction
open FSharp.Azure.Quantum.Backends.LocalBackend
open FSharp.Azure.Quantum.Examples.Common

// ==============================================================================
// CLI ARGUMENT PARSING
// ==============================================================================

let argv = fsi.CommandLineArgs |> Array.skip 1
let args = Cli.parse argv

Cli.exitIfHelp
    "CustomerChurnPrediction.fsx"
    "Predictive modeling for customer churn using quantum multi-class classification."
    [ { Cli.OptionSpec.Name = "example";  Description = "Example to run (1-4 or all)";       Default = Some "all" }
      { Cli.OptionSpec.Name = "epochs";   Description = "Max training epochs";                Default = Some "60" }
      { Cli.OptionSpec.Name = "lr";       Description = "Learning rate";                      Default = Some "0.01" }
      { Cli.OptionSpec.Name = "output";   Description = "Write results to JSON file";         Default = None }
      { Cli.OptionSpec.Name = "csv";      Description = "Write results to CSV file";          Default = None }
      { Cli.OptionSpec.Name = "quiet";    Description = "Suppress informational output";      Default = None } ]
    args

let quiet = Cli.hasFlag "quiet" args
let outputPath = Cli.tryGet "output" args
let csvPath = Cli.tryGet "csv" args
let exampleFilter = Cli.getOr "example" "all" args
let cliEpochs = Cli.getIntOr "epochs" 60 args
let cliLearningRate = Cli.getFloatOr "lr" 0.01 args

let shouldRun ex =
    exampleFilter = "all" || exampleFilter = string ex

/// Explicit quantum backend (Rule 1: all code depends on IQuantumBackend)
let quantumBackend = LocalBackend() :> IQuantumBackend

// Accumulate results for JSON/CSV output
let results = ResizeArray<{| Example: string; Status: string; Details: Map<string, obj> |}>()

// ============================================================================
// SAMPLE DATA - Customer Features
// ============================================================================

/// Generate synthetic customer data for demonstration
/// In production, load from database or data warehouse
let generateCustomerData () =
    let random = Random(42)  // Fixed seed for reproducibility
    
    // Feature engineering: Extract meaningful customer behavior features
    // Features: [tenure_months, monthly_spend, support_calls, usage_frequency, satisfaction_score]
    
    // Customers who will stay (Class 0) - engaged, satisfied
    let stableCustomers =
        [| for i in 1..30 ->
            [| 
                12.0 + random.NextDouble() * 24.0       // Long tenure (1-3 years)
                100.0 + random.NextDouble() * 100.0     // Good spend
                float (random.Next(0, 2))               // Few support calls
                20.0 + random.NextDouble() * 10.0       // High usage
                7.0 + random.NextDouble() * 3.0         // High satisfaction (7-10)
            |]
        |]
    
    // Customers who will churn in 30 days (Class 1) - urgent warning signs
    let churn30DayCustomers =
        [| for i in 1..15 ->
            [| 
                1.0 + random.NextDouble() * 6.0         // Short tenure (1-6 months)
                20.0 + random.NextDouble() * 30.0       // Low spend
                float (random.Next(5, 10))              // Many support calls
                2.0 + random.NextDouble() * 5.0         // Low usage
                1.0 + random.NextDouble() * 3.0         // Low satisfaction (1-4)
            |]
        |]
    
    // Customers who will churn in 60 days (Class 2) - declining engagement
    let churn60DayCustomers =
        [| for i in 1..15 ->
            [| 
                6.0 + random.NextDouble() * 12.0        // Medium tenure (6-18 months)
                40.0 + random.NextDouble() * 40.0       // Declining spend
                float (random.Next(3, 6))               // Moderate support calls
                8.0 + random.NextDouble() * 7.0         // Declining usage
                4.0 + random.NextDouble() * 2.0         // Medium satisfaction (4-6)
            |]
        |]
    
    // Customers who will churn in 90 days (Class 3) - early warning
    let churn90DayCustomers =
        [| for i in 1..10 ->
            [| 
                12.0 + random.NextDouble() * 12.0       // Established (1-2 years)
                60.0 + random.NextDouble() * 40.0       // Medium-low spend
                float (random.Next(2, 5))               // Some support calls
                12.0 + random.NextDouble() * 8.0        // Medium usage
                5.0 + random.NextDouble() * 2.0         // Medium-low satisfaction (5-7)
            |]
        |]
    
    // Combine datasets
    let allCustomers = 
        Array.concat [stableCustomers; churn30DayCustomers; churn60DayCustomers; churn90DayCustomers]
    
    let allLabels = 
        Array.concat [
            Array.create 30 0.0  // Stable
            Array.create 15 1.0  // Churn 30 days
            Array.create 15 2.0  // Churn 60 days
            Array.create 10 3.0  // Churn 90 days
        ]
    
    // Shuffle data
    let indices = [| 0 .. allCustomers.Length - 1 |]
    let shuffled = 
        indices 
        |> Array.sortBy (fun _ -> random.Next())
        |> Array.map (fun i -> allCustomers.[i], allLabels.[i])
    
    let trainX = shuffled |> Array.map fst
    let trainY = shuffled |> Array.map snd
    
    (trainX, trainY)

let (trainX, trainY) = generateCustomerData()

// ============================================================================
// EXAMPLE 1: Multi-Class Churn Prediction (Minimal Configuration)
// ============================================================================

// Store result1 model at module level for Example 4
let mutable example1Model : PredictiveModel.Model option = None

if shouldRun 1 then
    if not quiet then
        printfn "=== Example 1: Customer Churn Prediction (Multi-Class) ===\n"
        printfn "Training on %d customers..." trainX.Length
        printfn "  - Stable customers: %d" (trainY |> Array.filter ((=) 0.0) |> Array.length)
        printfn "  - Churn in 30 days: %d" (trainY |> Array.filter ((=) 1.0) |> Array.length)
        printfn "  - Churn in 60 days: %d" (trainY |> Array.filter ((=) 2.0) |> Array.length)
        printfn "  - Churn in 90 days: %d\n" (trainY |> Array.filter ((=) 3.0) |> Array.length)

    // Train multi-class churn predictor
    let result1 = predictiveModel {
        trainWith trainX trainY
        problemType (MultiClass 4)  // 4 categories
        backend quantumBackend
    }

    match result1 with
    | Error err ->
        if not quiet then printfn "Training failed: %A" err
        results.Add({| Example = "1-multiclass"; Status = "error"; Details = Map.ofList ["error", box (sprintf "%A" err)] |})

    | Ok model ->
        example1Model <- Some model
        if not quiet then
            printfn "Training complete!"
            printfn "  - Problem type: %A" model.Metadata.ProblemType
            printfn "  - Architecture: %A" model.Metadata.Architecture
            printfn "  - Training accuracy: %.2f%%" (model.Metadata.TrainingScore * 100.0)
            printfn "  - Training time: %A\n" model.Metadata.TrainingTime
        
        // Test on new customers
        if not quiet then printfn "=== Predicting Churn Risk for New Customers ===\n"
        
        let testCustomers = [|
            ("Customer 1 (high churn risk)", [| 2.0; 25.0; 8.0; 3.0; 2.0 |])
            ("Customer 2 (stable)", [| 24.0; 150.0; 1.0; 25.0; 9.0 |])
            ("Customer 3 (medium risk)", [| 10.0; 50.0; 4.0; 10.0; 5.0 |])
        |]
        
        let predictions = ResizeArray<{| Name: string; Category: int; Confidence: float |}>()
        
        for (name, features) in testCustomers do
            match PredictiveModel.predictCategory features model None None with
            | Error err ->
                if not quiet then printfn "%s: Prediction failed: %A" name err
            | Ok pred ->
                predictions.Add({| Name = name; Category = pred.Category; Confidence = pred.Confidence |})
                if not quiet then
                    printfn "%s:" name
                    printfn "  Predicted churn category: %d" pred.Category
                    printfn "  Confidence: %.2f%%" (pred.Confidence * 100.0)
                    match pred.Category with
                    | 0 -> printfn "  Status: Customer will stay - no action needed"
                    | 1 -> printfn "  Status: HIGH RISK - Will churn in 30 days!"
                           printfn "  Action: Immediate retention offer (discount, personal call)"
                    | 2 -> printfn "  Status: MEDIUM RISK - Will churn in 60 days"
                           printfn "  Action: Send satisfaction survey, address pain points"
                    | 3 -> printfn "  Status: LOW RISK - Will churn in 90 days"
                           printfn "  Action: Monitor engagement, proactive check-in"
                    | _ -> ()
                    printfn ""
        
        results.Add({|
            Example = "1-multiclass"
            Status = "ok"
            Details = Map.ofList [
                "training_accuracy", box (model.Metadata.TrainingScore * 100.0)
                "predictions", box (predictions |> Seq.toArray)
            ]
        |})

// ============================================================================
// EXAMPLE 2: Advanced Configuration with Evaluation
// ============================================================================

if shouldRun 2 then
    if not quiet then printfn "\n=== Example 2: Advanced Churn Prediction with Evaluation ===\n"

    // Split data into train/test
    let splitIndex = int (float trainX.Length * 0.8)
    let trainXFull = trainX.[..splitIndex-1]
    let trainYFull = trainY.[..splitIndex-1]
    let testX = trainX.[splitIndex..]
    let testY = trainY.[splitIndex..]

    if not quiet then
        printfn "Training set: %d customers" trainXFull.Length
        printfn "Test set: %d customers\n" testX.Length

    let result2 = predictiveModel {
        trainWith trainXFull trainYFull
        problemType (MultiClass 4)
        backend quantumBackend
        
        // Advanced configuration
        architecture Quantum
        learningRate cliLearningRate
        maxEpochs cliEpochs
        convergenceThreshold 0.005
        
        verbose false
        
        saveModelTo "churn_predictor.model"
        note "Customer churn prediction model - Q2 2024"
    }

    match result2 with
    | Error err ->
        if not quiet then printfn "Training failed: %A" err
        results.Add({| Example = "2-advanced"; Status = "error"; Details = Map.ofList ["error", box (sprintf "%A" err)] |})

    | Ok model ->
        if not quiet then printfn "Advanced model trained!\n"
        
        // Evaluate on test set
        let testYInt = testY |> Array.map int
        match PredictiveModel.evaluateMultiClass testX testYInt model with
        | Error err ->
            if not quiet then printfn "Evaluation failed: %A" err
        | Ok metrics ->
            if not quiet then
                printfn "=== Model Performance ===\n"
                printfn "Overall Accuracy: %.2f%%\n" (metrics.Accuracy * 100.0)
                
                for c in 0..3 do
                    let label = match c with 0 -> "Will Stay" | 1 -> "Churn 30d" | 2 -> "Churn 60d" | _ -> "Churn 90d"
                    printfn "Class %d (%s):" c label
                    printfn "  Precision: %.2f%%  Recall: %.2f%%  F1: %.2f%%"
                        (metrics.Precision.[c] * 100.0)
                        (metrics.Recall.[c] * 100.0)
                        (metrics.F1Score.[c] * 100.0)
                
                printfn "\nConfusion Matrix:"
                printfn "              Predicted"
                printfn "           0    1    2    3"
                for r in 0..3 do
                    printfn "Actual %d: %3d  %3d  %3d  %3d" r
                        metrics.ConfusionMatrix.[r].[0]
                        metrics.ConfusionMatrix.[r].[1]
                        metrics.ConfusionMatrix.[r].[2]
                        metrics.ConfusionMatrix.[r].[3]
                printfn ""
            
            results.Add({|
                Example = "2-advanced"
                Status = "ok"
                Details = Map.ofList [
                    "accuracy", box (metrics.Accuracy * 100.0)
                    "precision_class0", box (metrics.Precision.[0] * 100.0)
                    "recall_class0", box (metrics.Recall.[0] * 100.0)
                    "f1_class0", box (metrics.F1Score.[0] * 100.0)
                ]
            |})

// ============================================================================
// EXAMPLE 3: Revenue Prediction (Regression)
// ============================================================================

if shouldRun 3 then
    if not quiet then printfn "\n=== Example 3: Customer Lifetime Value Prediction (Regression) ===\n"

    // Generate revenue data
    let generateRevenueData () =
        let random = Random(42)
        
        // Features: [tenure_months, monthly_spend, usage_frequency, satisfaction_score]
        // Target: Predicted 12-month revenue
        let customers = 
            [| for i in 1..60 ->
                let tenure = 1.0 + random.NextDouble() * 36.0
                let spend = 50.0 + random.NextDouble() * 200.0
                let usage = 5.0 + random.NextDouble() * 25.0
                let satisfaction = 3.0 + random.NextDouble() * 7.0
                
                // Revenue model: tenure effect + spend baseline + usage multiplier + satisfaction bonus
                let baseRevenue = spend * 12.0
                let tenureBonus = tenure * 10.0
                let usageMultiplier = usage / 30.0 * spend * 12.0
                let satisfactionBonus = satisfaction * 100.0
                
                let ltv = baseRevenue + tenureBonus + usageMultiplier + satisfactionBonus
                
                ([| tenure; spend; usage; satisfaction |], ltv)
            |]
        
        let features = customers |> Array.map fst
        let targets = customers |> Array.map snd
        (features, targets)

    let (revenueX, revenueY) = generateRevenueData()

    if not quiet then printfn "Training revenue prediction model on %d customers...\n" revenueX.Length

    let result3 = predictiveModel {
        trainWith revenueX revenueY
        problemType Regression
        backend quantumBackend
        
        learningRate cliLearningRate
        maxEpochs 100
        
        verbose false
    }

    match result3 with
    | Error err ->
        if not quiet then printfn "Training failed: %A" err
        results.Add({| Example = "3-regression"; Status = "error"; Details = Map.ofList ["error", box (sprintf "%A" err)] |})

    | Ok model ->
        if not quiet then
            printfn "Revenue model trained!"
            printfn "  - R^2 Score: %.4f" model.Metadata.TrainingScore
            printfn "  - Training time: %A\n" model.Metadata.TrainingTime
        
        // Predict revenue for sample customers
        if not quiet then printfn "=== Revenue Predictions ===\n"
        
        let testCases = [|
            ("High-Value Customer", [| 24.0; 150.0; 20.0; 9.0 |], "VIP treatment, loyalty rewards")
            ("Medium-Value Customer", [| 6.0; 60.0; 10.0; 5.0 |], "Upsell opportunities, engagement campaigns")
            ("Low-Value At-Risk", [| 2.0; 30.0; 5.0; 3.0 |], "Onboarding improvement, satisfaction survey")
        |]
        
        let revPredictions = ResizeArray<{| Name: string; PredictedLTV: float |}>()
        
        for (name, features, action) in testCases do
            match PredictiveModel.predict features model None None with
            | Error err ->
                if not quiet then printfn "%s: Prediction failed: %A" name err
            | Ok pred ->
                revPredictions.Add({| Name = name; PredictedLTV = pred.Value |})
                if not quiet then
                    printfn "%s:" name
                    printfn "  Predicted 12-month LTV: $%.2f" pred.Value
                    printfn "  Action: %s\n" action
        
        results.Add({|
            Example = "3-regression"
            Status = "ok"
            Details = Map.ofList [
                "r2_score", box model.Metadata.TrainingScore
                "predictions", box (revPredictions |> Seq.toArray)
            ]
        |})

// ============================================================================
// EXAMPLE 4: Production Integration Pattern
// ============================================================================

if shouldRun 4 then
    if not quiet then printfn "\n=== Example 4: Production Integration Pattern ===\n"

    // Use model from Example 1 if available, otherwise train fresh
    let modelForProduction =
        match example1Model with
        | Some m -> Ok m
        | None ->
            predictiveModel {
                trainWith trainX trainY
                problemType (MultiClass 4)
                backend quantumBackend
                verbose false
            }

    /// Production-ready churn assessment function
    let assessCustomerChurn (customerFeatures: float array) (model: PredictiveModel.Model) =
        match PredictiveModel.predictCategory customerFeatures model None None with
        | Error _ -> None
        | Ok prediction ->
            let riskLevel, actionPriority, recommendedAction =
                match prediction.Category with
                | 0 -> ("No Risk", "None", "Maintain relationship, monitor satisfaction")
                | 1 -> ("Critical", "Immediate", "Personal outreach, retention offer, escalate to manager")
                | 2 -> ("High", "This Week", "Satisfaction survey, address issues, re-engagement campaign")
                | 3 -> ("Medium", "This Month", "Proactive check-in, usage tips, value reminder")
                | _ -> ("Unknown", "Review", "Manual review required")
            
            Some {|
                ChurnRisk = riskLevel
                ChurnCategory = prediction.Category
                Confidence = prediction.Confidence
                ActionPriority = actionPriority
                RecommendedAction = recommendedAction
            |}

    match modelForProduction with
    | Error err ->
        if not quiet then printfn "Model not available: %A" err
        results.Add({| Example = "4-production"; Status = "error"; Details = Map.ofList ["error", box (sprintf "%A" err)] |})
    | Ok model ->
        if not quiet then printfn "Processing batch of customers for churn assessment...\n"
        
        let batchCustomers = [|
            [| 2.0; 25.0; 8.0; 3.0; 2.0 |]    // High risk
            [| 24.0; 150.0; 1.0; 25.0; 9.0 |]  // Stable
            [| 10.0; 50.0; 4.0; 10.0; 5.0 |]   // Medium risk
        |]
        
        let assessments = 
            batchCustomers 
            |> Array.choose (fun features -> assessCustomerChurn features model)
        
        if not quiet then
            printfn "=== Churn Risk Assessment Report ===\n"
            assessments
            |> Array.sortBy (fun a -> 
                match a.ActionPriority with
                | "Immediate" -> 1 | "This Week" -> 2 | "This Month" -> 3 | _ -> 4)
            |> Array.iter (fun assessment ->
                printfn "  Risk Level: %s (Category %d)" assessment.ChurnRisk assessment.ChurnCategory
                printfn "  Confidence: %.1f%%" (assessment.Confidence * 100.0)
                printfn "  Action Priority: %s" assessment.ActionPriority
                printfn "  Recommended Action: %s\n" assessment.RecommendedAction
            )
        
        results.Add({|
            Example = "4-production"
            Status = "ok"
            Details = Map.ofList [
                "assessments_count", box assessments.Length
                "assessments", box assessments
            ]
        |})

// ============================================================================
// OUTPUT
// ============================================================================

if outputPath.IsSome then
    let payload = {| script = "CustomerChurnPrediction.fsx"; timestamp = DateTime.UtcNow; results = results |> Seq.toArray |}
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
    printfn "\n=== Tutorial Complete ===\n"
    printfn "You've learned how to:"
    printfn "  - Build multi-class churn prediction models"
    printfn "  - Build regression models for revenue forecasting"
    printfn "  - Evaluate model performance with metrics"
    printfn "  - Make predictions on new customers"
    printfn "  - Integrate models into production workflows"
    printfn ""
    printfn "Try these options:"
    printfn "  dotnet fsi CustomerChurnPrediction.fsx -- --help"
    printfn "  dotnet fsi CustomerChurnPrediction.fsx -- --example 3 --epochs 100"
    printfn "  dotnet fsi CustomerChurnPrediction.fsx -- --quiet --output results.json"
