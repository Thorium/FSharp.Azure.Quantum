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

//#r "nuget: FSharp.Azure.Quantum"
#r "../../src/FSharp.Azure.Quantum/bin/Debug/net10.0/FSharp.Azure.Quantum.dll"

open System
open FSharp.Azure.Quantum.Business
open FSharp.Azure.Quantum.Business.PredictiveModel

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

// ============================================================================
// EXAMPLE 1: Multi-Class Churn Prediction (Minimal Configuration)
// ============================================================================

printfn "=== Example 1: Customer Churn Prediction (Multi-Class) ===\n"

let (trainX, trainY) = generateCustomerData()

printfn "Training on %d customers..." trainX.Length
printfn "  - Stable customers: %d" (trainY |> Array.filter ((=) 0.0) |> Array.length)
printfn "  - Churn in 30 days: %d" (trainY |> Array.filter ((=) 1.0) |> Array.length)
printfn "  - Churn in 60 days: %d" (trainY |> Array.filter ((=) 2.0) |> Array.length)
printfn "  - Churn in 90 days: %d\n" (trainY |> Array.filter ((=) 3.0) |> Array.length)

// Train multi-class churn predictor
let result1 = predictiveModel {
    trainWith trainX trainY
    problemType (MultiClass 4)  // 4 categories
}

match result1 with
| Error err ->
    printfn "❌ Training failed: %A" err

| Ok model ->
    printfn "✅ Training complete!"
    printfn "  - Problem type: %A" model.Metadata.ProblemType
    printfn "  - Architecture: %A" model.Metadata.Architecture
    printfn "  - Training accuracy: %.2f%%" (model.Metadata.TrainingScore * 100.0)
    printfn "  - Training time: %A\n" model.Metadata.TrainingTime
    
    // Test on new customers
    printfn "=== Predicting Churn Risk for New Customers ===\n"
    
    // Customer 1: High churn risk (30 days)
    let customer1 = [| 2.0; 25.0; 8.0; 3.0; 2.0 |]  // Short tenure, low spend, many calls, low usage, low satisfaction
    
    match PredictiveModel.predictCategory customer1 model None None with
    | Error err -> printfn "❌ Prediction failed: %A" err
    | Ok pred ->
        printfn "Customer 1 Analysis:"
        printfn "  Predicted churn category: %d" pred.Category
        printfn "  Confidence: %.2f%%" (pred.Confidence * 100.0)
        printfn "  Probabilities: %A" pred.Probabilities
        match pred.Category with
        | 0 -> printfn "  ✅ Status: Customer will stay - no action needed"
        | 1 -> printfn "  🚨 Status: HIGH RISK - Will churn in 30 days!"
               printfn "  💡 Action: Immediate retention offer (discount, personal call)"
        | 2 -> printfn "  ⚠️  Status: MEDIUM RISK - Will churn in 60 days"
               printfn "  💡 Action: Send satisfaction survey, address pain points"
        | 3 -> printfn "  ⚡ Status: LOW RISK - Will churn in 90 days"
               printfn "  💡 Action: Monitor engagement, proactive check-in"
        | _ -> ()
        printfn ""
    
    // Customer 2: Stable customer
    let customer2 = [| 24.0; 150.0; 1.0; 25.0; 9.0 |]  // Long tenure, high spend, few calls, high usage, high satisfaction
    
    match PredictiveModel.predictCategory customer2 model None None with
    | Error err -> printfn "❌ Prediction failed: %A" err
    | Ok pred ->
        printfn "Customer 2 Analysis:"
        printfn "  Predicted churn category: %d" pred.Category
        printfn "  Confidence: %.2f%%" (pred.Confidence * 100.0)
        match pred.Category with
        | 0 -> printfn "  ✅ Status: Happy customer - maintain relationship"
        | 1 -> printfn "  🚨 Status: Unexpected risk detected!"
        | 2 -> printfn "  ⚠️  Status: Watch for declining engagement"
        | 3 -> printfn "  ⚡ Status: Monitor satisfaction trends"
        | _ -> ()
        printfn ""
    
    // Customer 3: Medium-term risk
    let customer3 = [| 10.0; 50.0; 4.0; 10.0; 5.0 |]  // Medium tenure, declining spend, some calls, medium usage, medium satisfaction
    
    match PredictiveModel.predictCategory customer3 model None None with
    | Error err -> printfn "❌ Prediction failed: %A" err
    | Ok pred ->
        printfn "Customer 3 Analysis:"
        printfn "  Predicted churn category: %d" pred.Category
        printfn "  Confidence: %.2f%%" (pred.Confidence * 100.0)
        match pred.Category with
        | 0 -> printfn "  ✅ Status: Stable but monitor trends"
        | 1 -> printfn "  🚨 Status: Urgent action required"
        | 2 -> printfn "  ⚠️  Status: Declining - re-engagement campaign"
               printfn "  💡 Action: Feature education, usage tips, value reminder"
        | 3 -> printfn "  ⚡ Status: Early warning - proactive outreach"
        | _ -> ()
        printfn ""

// ============================================================================
// EXAMPLE 2: Advanced Configuration with Evaluation
// ============================================================================

printfn "\n=== Example 2: Advanced Churn Prediction with Evaluation ===\n"

// Split data into train/test
let splitIndex = int (float trainX.Length * 0.8)
let trainXFull = trainX.[..splitIndex-1]
let trainYFull = trainY.[..splitIndex-1]
let testX = trainX.[splitIndex..]
let testY = trainY.[splitIndex..]

printfn "Training set: %d customers" trainXFull.Length
printfn "Test set: %d customers\n" testX.Length

let result2 = predictiveModel {
    trainWith trainXFull trainYFull
    problemType (MultiClass 4)
    
    // Advanced configuration
    architecture Quantum
    learningRate 0.01
    maxEpochs 150
    convergenceThreshold 0.0005
    
    verbose true
    
    saveModelTo "churn_predictor.model"
    note "Customer churn prediction model - Q2 2024"
}

match result2 with
| Error err ->
    printfn "❌ Training failed: %A" err

| Ok model ->
    printfn "\n✅ Advanced model trained!\n"
    
    // Evaluate on test set
    let testYInt = testY |> Array.map int
    match PredictiveModel.evaluateMultiClass testX testYInt model with
    | Error err ->
        printfn "❌ Evaluation failed: %A" err
    
    | Ok metrics ->
        printfn "=== Model Performance ===\n"
        printfn "Overall Accuracy: %.2f%%\n" (metrics.Accuracy * 100.0)
        
        printfn "Per-Class Metrics:"
        printfn "Class 0 (Will Stay):"
        printfn "  Precision: %.2f%%" (metrics.Precision.[0] * 100.0)
        printfn "  Recall: %.2f%%" (metrics.Recall.[0] * 100.0)
        printfn "  F1 Score: %.2f%%\n" (metrics.F1Score.[0] * 100.0)
        
        printfn "Class 1 (Churn 30 days):"
        printfn "  Precision: %.2f%%" (metrics.Precision.[1] * 100.0)
        printfn "  Recall: %.2f%%" (metrics.Recall.[1] * 100.0)
        printfn "  F1 Score: %.2f%%\n" (metrics.F1Score.[1] * 100.0)
        
        printfn "Class 2 (Churn 60 days):"
        printfn "  Precision: %.2f%%" (metrics.Precision.[2] * 100.0)
        printfn "  Recall: %.2f%%" (metrics.Recall.[2] * 100.0)
        printfn "  F1 Score: %.2f%%\n" (metrics.F1Score.[2] * 100.0)
        
        printfn "Class 3 (Churn 90 days):"
        printfn "  Precision: %.2f%%" (metrics.Precision.[3] * 100.0)
        printfn "  Recall: %.2f%%" (metrics.Recall.[3] * 100.0)
        printfn "  F1 Score: %.2f%%\n" (metrics.F1Score.[3] * 100.0)
        
        printfn "Confusion Matrix:"
        printfn "              Predicted"
        printfn "           0    1    2    3"
        printfn "Actual 0: %3d  %3d  %3d  %3d" 
            metrics.ConfusionMatrix.[0].[0]
            metrics.ConfusionMatrix.[0].[1]
            metrics.ConfusionMatrix.[0].[2]
            metrics.ConfusionMatrix.[0].[3]
        printfn "       1: %3d  %3d  %3d  %3d" 
            metrics.ConfusionMatrix.[1].[0]
            metrics.ConfusionMatrix.[1].[1]
            metrics.ConfusionMatrix.[1].[2]
            metrics.ConfusionMatrix.[1].[3]
        printfn "       2: %3d  %3d  %3d  %3d" 
            metrics.ConfusionMatrix.[2].[0]
            metrics.ConfusionMatrix.[2].[1]
            metrics.ConfusionMatrix.[2].[2]
            metrics.ConfusionMatrix.[2].[3]
        printfn "       3: %3d  %3d  %3d  %3d\n" 
            metrics.ConfusionMatrix.[3].[0]
            metrics.ConfusionMatrix.[3].[1]
            metrics.ConfusionMatrix.[3].[2]
            metrics.ConfusionMatrix.[3].[3]

// ============================================================================
// EXAMPLE 3: Revenue Prediction (Regression)
// ============================================================================

printfn "\n=== Example 3: Customer Lifetime Value Prediction (Regression) ===\n"

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

printfn "Training revenue prediction model on %d customers...\n" revenueX.Length

let result3 = predictiveModel {
    trainWith revenueX revenueY
    problemType Regression
    
    learningRate 0.01
    maxEpochs 100
    
    verbose false
}

match result3 with
| Error err ->
    printfn "❌ Training failed: %A" err

| Ok model ->
    printfn "✅ Revenue model trained!"
    printfn "  - R² Score: %.4f" model.Metadata.TrainingScore
    printfn "  - Training time: %A\n" model.Metadata.TrainingTime
    
    // Predict revenue for sample customers
    printfn "=== Revenue Predictions ===\n"
    
    let testCustomer1 = [| 24.0; 150.0; 20.0; 9.0 |]  // High-value customer
    match PredictiveModel.predict testCustomer1 model None None with
    | Error err -> printfn "❌ Prediction failed: %A" err
    | Ok pred ->
        printfn "High-Value Customer:"
        printfn "  Features: tenure=24mo, spend=$150/mo, usage=20, satisfaction=9"
        printfn "  Predicted 12-month LTV: $%.2f" pred.Value
        printfn "  💡 Action: VIP treatment, loyalty rewards\n"
    
    let testCustomer2 = [| 6.0; 60.0; 10.0; 5.0 |]  // Medium-value customer
    match PredictiveModel.predict testCustomer2 model None None with
    | Error err -> printfn "❌ Prediction failed: %A" err
    | Ok pred ->
        printfn "Medium-Value Customer:"
        printfn "  Features: tenure=6mo, spend=$60/mo, usage=10, satisfaction=5"
        printfn "  Predicted 12-month LTV: $%.2f" pred.Value
        printfn "  💡 Action: Upsell opportunities, engagement campaigns\n"
    
    let testCustomer3 = [| 2.0; 30.0; 5.0; 3.0 |]  // Low-value at-risk customer
    match PredictiveModel.predict testCustomer3 model None None with
    | Error err -> printfn "❌ Prediction failed: %A" err
    | Ok pred ->
        printfn "Low-Value At-Risk Customer:"
        printfn "  Features: tenure=2mo, spend=$30/mo, usage=5, satisfaction=3"
        printfn "  Predicted 12-month LTV: $%.2f" pred.Value
        printfn "  💡 Action: Onboarding improvement, satisfaction survey\n"

// ============================================================================
// EXAMPLE 4: Production Integration Pattern
// ============================================================================

printfn "\n=== Example 4: Production Integration Pattern ===\n"

// This pattern shows how to integrate churn prediction into production systems

/// Production-ready churn assessment function
let assessCustomerChurn (customerFeatures: float array) (model: PredictiveModel.Model) =
    match PredictiveModel.predictCategory customerFeatures model None None with
    | Error err ->
        printfn "⚠️  Prediction error: %A" err
        None
    
    | Ok prediction ->
        let riskLevel, actionPriority, recommendedAction =
            match prediction.Category with
            | 0 -> ("No Risk", "None", "Maintain relationship, monitor satisfaction")
            | 1 -> ("Critical", "Immediate", "Personal outreach, retention offer, escalate to manager")
            | 2 -> ("High", "This Week", "Satisfaction survey, address issues, re-engagement campaign")
            | 3 -> ("Medium", "This Month", "Proactive check-in, usage tips, value reminder")
            | _ -> ("Unknown", "Review", "Manual review required")
        
        Some {|
            CustomerId = "CUST-" + System.Guid.NewGuid().ToString().Substring(0, 8)
            ChurnRisk = riskLevel
            ChurnCategory = prediction.Category
            Confidence = prediction.Confidence
            ActionPriority = actionPriority
            RecommendedAction = recommendedAction
            PredictedAt = DateTime.UtcNow
        |}

// Simulate production batch processing
match result1 with
| Error _ -> printfn "Model not available"
| Ok model ->
    printfn "Processing batch of customers for churn assessment...\n"
    
    let batchCustomers = [|
        [| 2.0; 25.0; 8.0; 3.0; 2.0 |]    // High risk
        [| 24.0; 150.0; 1.0; 25.0; 9.0 |]  // Stable
        [| 10.0; 50.0; 4.0; 10.0; 5.0 |]   // Medium risk
    |]
    
    let assessments = 
        batchCustomers 
        |> Array.choose (fun features -> assessCustomerChurn features model)
    
    printfn "=== Churn Risk Assessment Report ===\n"
    assessments
    |> Array.sortBy (fun a -> 
        match a.ActionPriority with
        | "Immediate" -> 1
        | "This Week" -> 2
        | "This Month" -> 3
        | _ -> 4
    )
    |> Array.iter (fun assessment ->
        printfn "Customer: %s" assessment.CustomerId
        printfn "  Risk Level: %s (Category %d)" assessment.ChurnRisk assessment.ChurnCategory
        printfn "  Confidence: %.1f%%" (assessment.Confidence * 100.0)
        printfn "  Action Priority: %s" assessment.ActionPriority
        printfn "  Recommended Action: %s" assessment.RecommendedAction
        printfn "  Assessed At: %s\n" (assessment.PredictedAt.ToString("yyyy-MM-dd HH:mm:ss"))
    )

printfn "\n=== Tutorial Complete ===\n"
printfn "You've learned how to:"
printfn "  ✅ Build multi-class churn prediction models"
printfn "  ✅ Build regression models for revenue forecasting"
printfn "  ✅ Evaluate model performance with metrics"
printfn "  ✅ Make predictions on new customers"
printfn "  ✅ Integrate models into production workflows"
printfn "\nNext steps:"
printfn "  - Connect to your real customer database"
printfn "  - Engineer domain-specific features"
printfn "  - Deploy model via REST API or Azure Functions"
printfn "  - Set up automated retraining pipeline"
printfn "  - Monitor model performance over time"
