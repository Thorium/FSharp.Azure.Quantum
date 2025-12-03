# Predictive Modeling with Quantum Machine Learning

**Build forecasting and prediction models without quantum expertise.**

The `PredictiveModelBuilder` provides a business-first API for predicting future outcomes using quantum machine learning. Whether you need to forecast revenue, predict customer churn, or estimate demand, this builder handles all the quantum complexity for you.

## 🎯 What Can You Predict?

### Regression (Continuous Values)
Predict numerical outcomes:
- **Revenue forecasting**: Expected revenue per customer
- **Demand prediction**: How many units will sell
- **Customer lifetime value (LTV)**: Long-term customer value
- **Risk scoring**: Credit risk, insurance risk (0-100 scale)
- **Pricing optimization**: Optimal price point
- **Inventory requirements**: Stock levels needed

### Multi-Class Classification (Categories)
Predict categorical outcomes:
- **Churn timing**: Will customer leave? When? (Stay, 30 days, 60 days, 90 days)
- **Risk levels**: Low, medium, high risk categories
- **Lead scoring tiers**: Hot, warm, cold leads
- **Customer segments**: Which segment does customer belong to
- **Product recommendations**: Which product category to recommend

## 🚀 Quick Start

### F# - Computation Expression

```fsharp
#r "nuget: FSharp.Azure.Quantum"
open FSharp.Azure.Quantum.Business

// Multi-class: Predict when customers will churn
let churnModel = predictiveModel {
    trainWith customerFeatures churnLabels  // Labels: 0=Stay, 1=Churn30, 2=Churn60, 3=Churn90
    problemType (MultiClass 4)
}

match churnModel with
| Ok model ->
    // Predict churn risk
    match PredictiveModel.predictCategory newCustomer model with
    | Ok prediction ->
        match prediction.Category with
        | 0 -> printfn "✅ Customer will stay"
        | 1 -> printfn "🚨 HIGH RISK - Will churn in 30 days!"
        | 2 -> printfn "⚠️  Will churn in 60 days"
        | 3 -> printfn "⚡ Will churn in 90 days"
    | Error e -> printfn "Error: %s" e
| Error e -> printfn "Training failed: %s" e

// Regression: Predict customer lifetime value
let ltvModel = predictiveModel {
    trainWith customerFeatures revenueTargets
    problemType Regression
}

match ltvModel with
| Ok model ->
    match PredictiveModel.predict newCustomer model with
    | Ok prediction ->
        printfn "Expected 12-month revenue: $%.2f" prediction.Value
    | Error e -> printfn "Error: %s" e
| Error e -> printfn "Training failed: %s" e
```

### C# - Fluent API

```csharp
using FSharp.Azure.Quantum.Business.CSharp;

// Multi-class: Predict customer churn timing
var churnModel = new PredictiveModelBuilder()
    .WithFeatures(customerFeatures)
    .WithTargets(churnLabels)  // 0=Stay, 1=Churn30, 2=Churn60, 3=Churn90
    .WithProblemType(ProblemType.MultiClass(4))
    .SaveModelTo("churn_predictor.model")
    .Build();

var prediction = churnModel.PredictCategory(newCustomer);
switch (prediction.Category)
{
    case 0:
        Console.WriteLine("✅ Customer will stay");
        break;
    case 1:
        Console.WriteLine("🚨 HIGH RISK - Take immediate action!");
        SendRetentionOffer(customer);
        break;
    case 2:
        Console.WriteLine("⚠️  Medium risk - Schedule check-in");
        break;
    case 3:
        Console.WriteLine("⚡ Low risk - Monitor engagement");
        break;
}

// Regression: Predict revenue
var ltvModel = new PredictiveModelBuilder()
    .WithFeatures(customerFeatures)
    .WithTargets(revenueTargets)
    .WithProblemType(ProblemType.Regression)
    .Build();

var revenuePrediction = ltvModel.Predict(newCustomer);
Console.WriteLine($"Expected LTV: ${revenuePrediction.Value:F2}");

if (revenuePrediction.Value > 10000)
    AssignToVIPTeam(customer);
```

## 📚 Complete Tutorial

See [CustomerChurnPrediction.fsx](CustomerChurnPrediction.fsx) for comprehensive examples including:
1. **Multi-class churn prediction** - Predict when customers will leave
2. **Advanced evaluation** - Confusion matrix, per-class metrics
3. **Revenue forecasting (regression)** - Predict customer lifetime value
4. **Production integration** - Batch processing, risk assessment workflows

## ⚙️ Configuration Options

### Problem Type

```fsharp
// Regression: Predict continuous values
problemType Regression

// Multi-class: Predict categories
problemType (MultiClass 4)  // 4 classes: 0, 1, 2, 3
```

```csharp
// C#
.WithProblemType(ProblemType.Regression)
.WithProblemType(ProblemType.MultiClass(4))
```

### Architecture

```fsharp
architecture Quantum      // Pure quantum model (VQC)
architecture Hybrid       // Quantum kernel + classical SVM
architecture Classical    // Classical baseline
```

```csharp
// C#
.WithArchitecture(ModelArchitecture.Quantum)
.WithArchitecture(ModelArchitecture.Hybrid)
.WithArchitecture(ModelArchitecture.Classical)
```

### Training Parameters

```fsharp
learningRate 0.01
maxEpochs 150
convergenceThreshold 0.0005
shots 1000
verbose true
```

```csharp
// C#
.WithLearningRate(0.01)
.WithMaxEpochs(150)
.WithConvergenceThreshold(0.0005)
.WithShots(1000)
.WithVerbose(true)
```

### Model Persistence

```fsharp
saveModelTo "churn_model.json"
note "Q2 2024 churn predictor"
```

```csharp
// C#
.SaveModelTo("churn_model.json")
.WithNote("Q2 2024 churn predictor")
```

## 📊 Evaluation Metrics

### Regression Metrics

```fsharp
match PredictiveModel.evaluateRegression testX testY model with
| Ok metrics ->
    printfn "R² Score: %.4f" metrics.RSquared        // 1.0 = perfect
    printfn "MAE: %.2f" metrics.MAE                  // Mean Absolute Error
    printfn "RMSE: %.2f" metrics.RMSE                // Root Mean Squared Error
| Error e -> printfn "Evaluation failed: %s" e
```

```csharp
// C#
var metrics = model.EvaluateRegression(testX, testY);
Console.WriteLine($"R² Score: {metrics.RSquared:F4}");
Console.WriteLine($"MAE: {metrics.MAE:F2}");
Console.WriteLine($"RMSE: {metrics.RMSE:F2}");
```

### Multi-Class Metrics

```fsharp
match PredictiveModel.evaluateMultiClass testX testY model with
| Ok metrics ->
    printfn "Overall Accuracy: %.2f%%" (metrics.Accuracy * 100.0)
    printfn "Per-class Precision: %A" metrics.Precision
    printfn "Per-class Recall: %A" metrics.Recall
    printfn "Per-class F1 Score: %A" metrics.F1Score
    printfn "Confusion Matrix:"
    metrics.ConfusionMatrix |> Array.iter (printfn "%A")
| Error e -> printfn "Evaluation failed: %s" e
```

```csharp
// C#
var metrics = model.EvaluateMultiClass(testX, testY);
Console.WriteLine($"Accuracy: {metrics.Accuracy * 100:F2}%");

for (int i = 0; i < metrics.Precision.Length; i++)
{
    Console.WriteLine($"Class {i}:");
    Console.WriteLine($"  Precision: {metrics.Precision[i] * 100:F2}%");
    Console.WriteLine($"  Recall: {metrics.Recall[i] * 100:F2}%");
    Console.WriteLine($"  F1 Score: {metrics.F1Score[i] * 100:F2}%");
}
```

## 🏭 Production Patterns

### Pattern 1: Batch Customer Risk Assessment

```fsharp
/// Assess churn risk for all customers
let assessCustomerBatch (customers: float array array) (model: PredictiveModel.Model) =
    customers
    |> Array.choose (fun features ->
        match PredictiveModel.predictCategory features model with
        | Ok pred ->
            let risk = 
                match pred.Category with
                | 0 -> "No Risk"
                | 1 -> "Critical"
                | 2 -> "High"
                | 3 -> "Medium"
                | _ -> "Unknown"
            Some {| Features = features; Risk = risk; Confidence = pred.Confidence |}
        | Error _ -> None
    )
    |> Array.sortByDescending (fun a -> a.Confidence)  // Prioritize by confidence

// Process batch
let riskAssessments = assessCustomerBatch allCustomers churnModel
let criticalCustomers = riskAssessments |> Array.filter (fun a -> a.Risk = "Critical")

printfn "Found %d critical churn risks" criticalCustomers.Length
criticalCustomers |> Array.iter (fun c ->
    printfn "  Risk: %s, Confidence: %.1f%%" c.Risk (c.Confidence * 100.0)
)
```

```csharp
// C# - Batch processing
var criticalCustomers = customers
    .Select(features => new 
    {
        Features = features,
        Prediction = model.PredictCategory(features)
    })
    .Where(x => x.Prediction.Category == 1)  // Critical risk
    .OrderByDescending(x => x.Prediction.Confidence)
    .ToList();

foreach (var customer in criticalCustomers)
{
    Console.WriteLine($"Critical churn risk: {customer.Prediction.Confidence * 100:F1}%");
    SendRetentionOffer(customer);
}
```

### Pattern 2: REST API Integration

```fsharp
// ASP.NET Core endpoint
[<HttpPost("predict/churn")>]
let predictChurn ([<FromBody>] request: ChurnPredictionRequest) =
    let features = [| 
        request.TenureMonths
        request.MonthlySpend
        request.SupportCalls
        request.UsageFrequency
        request.SatisfactionScore
    |]
    
    match PredictiveModel.predictCategory features loadedModel with
    | Ok prediction ->
        let response = {|
            ChurnRisk = match prediction.Category with
                        | 0 -> "No Risk"
                        | 1 -> "Critical"
                        | 2 -> "High"
                        | 3 -> "Medium"
                        | _ -> "Unknown"
            Confidence = prediction.Confidence
            RecommendedAction = getRecommendedAction prediction.Category
            PredictedAt = DateTime.UtcNow
        |}
        Ok response
    | Error e ->
        Error (BadRequest e)
```

### Pattern 3: Azure Functions Deployment

```fsharp
// Azure Function for serverless prediction
[<FunctionName("PredictCustomerChurn")>]
let run ([<HttpTrigger(AuthorizationLevel.Function, "post")>] req: HttpRequest) =
    async {
        // Load model once at startup (cache)
        let! body = req.ReadAsStringAsync() |> Async.AwaitTask
        let request = JsonSerializer.Deserialize<CustomerFeatures>(body)
        
        let features = extractFeatures request
        
        match PredictiveModel.predictCategory features cachedModel with
        | Ok prediction ->
            return OkObjectResult({|
                churnCategory = prediction.Category
                confidence = prediction.Confidence
                probabilities = prediction.Probabilities
            |})
        | Error e ->
            return BadRequestObjectResult({| error = e |})
    }
```

## 💡 Best Practices

### 1. Feature Engineering

```fsharp
// Transform raw data into meaningful features
let engineerFeatures (customer: CustomerRecord) =
    [|
        float customer.TenureMonths
        customer.MonthlySpend
        float customer.SupportCallsLast30Days
        customer.DailyActiveMinutes / 60.0  // Normalize to hours
        float customer.SatisfactionScore
        if customer.HasPremiumFeatures then 1.0 else 0.0  // Binary feature
    |]
```

### 2. Data Normalization

```fsharp
// Normalize features to [0, 1] range for better training
let normalizeFeatures (features: float array array) =
    let numFeatures = features.[0].Length
    
    let mins = Array.init numFeatures (fun i -> features |> Array.map (fun f -> f.[i]) |> Array.min)
    let maxs = Array.init numFeatures (fun i -> features |> Array.map (fun f -> f.[i]) |> Array.max)
    
    features 
    |> Array.map (fun sample ->
        sample 
        |> Array.mapi (fun i value ->
            if maxs.[i] - mins.[i] = 0.0 then 0.0
            else (value - mins.[i]) / (maxs.[i] - mins.[i])
        )
    )
```

### 3. Train/Test Split

```fsharp
// Always evaluate on held-out test data
let splitData (features: float array array) (targets: float array) (trainRatio: float) =
    let splitIndex = int (float features.Length * trainRatio)
    let trainX = features.[..splitIndex-1]
    let trainY = targets.[..splitIndex-1]
    let testX = features.[splitIndex..]
    let testY = targets.[splitIndex..]
    (trainX, trainY, testX, testY)

let (trainX, trainY, testX, testY) = splitData allFeatures allTargets 0.8
```

### 4. Model Retraining

```fsharp
// Retrain periodically with fresh data
let retrainModel (newData: (float array array * float array)) (existingModelPath: string) =
    let (features, targets) = newData
    
    // Train new model
    let result = predictiveModel {
        trainWith features targets
        problemType (MultiClass 4)
        learningRate 0.01
        maxEpochs 150
    }
    
    match result with
    | Ok newModel ->
        // Evaluate before replacing
        match PredictiveModel.evaluateMultiClass testX testY newModel with
        | Ok metrics when metrics.Accuracy > 0.75 ->
            // Good performance - save new model
            PredictiveModel.save existingModelPath newModel |> ignore
            printfn "✅ Model retrained successfully (accuracy: %.2f%%)" (metrics.Accuracy * 100.0)
        | Ok metrics ->
            printfn "⚠️  New model performance insufficient (%.2f%% < 75%%)" (metrics.Accuracy * 100.0)
        | Error e ->
            printfn "⚠️  Evaluation failed: %s" e
    | Error e ->
        printfn "❌ Retraining failed: %s" e
```

### 5. Confidence Thresholding

```fsharp
// Only act on high-confidence predictions
let predictWithConfidenceThreshold (features: float array) (model: PredictiveModel.Model) (threshold: float) =
    match PredictiveModel.predictCategory features model with
    | Ok prediction when prediction.Confidence >= threshold ->
        Some prediction
    | Ok prediction ->
        printfn "⚠️  Low confidence (%.1f%% < %.1f%%) - manual review recommended" 
            (prediction.Confidence * 100.0) (threshold * 100.0)
        None
    | Error e ->
        printfn "❌ Prediction error: %s" e
        None
```

## 🔧 Troubleshooting

### Low Accuracy

**Problem**: Model accuracy is below expectations.

**Solutions**:
1. **More training data**: Collect more labeled examples
2. **Better features**: Engineer more meaningful features
3. **Feature normalization**: Normalize features to [0, 1] range
4. **Increase epochs**: Try `maxEpochs 200` or higher
5. **Adjust learning rate**: Try `learningRate 0.005` (smaller) or `learningRate 0.02` (larger)
6. **Try hybrid architecture**: `architecture Hybrid` sometimes performs better

### Imbalanced Classes

**Problem**: One class dominates the dataset (e.g., 90% "will stay", 10% "will churn").

**Solutions**:
```fsharp
// Oversample minority class
let balanceClasses (features: float array array) (labels: float array) =
    let minorityClass = 1.0  // Churn class
    let minoritySamples = 
        Array.zip features labels 
        |> Array.filter (fun (_, lbl) -> lbl = minorityClass)
    
    // Duplicate minority samples
    let balancedFeatures = Array.append features (minoritySamples |> Array.map fst)
    let balancedLabels = Array.append labels (minoritySamples |> Array.map snd)
    
    (balancedFeatures, balancedLabels)
```

### Training Too Slow

**Problem**: Training takes too long.

**Solutions**:
1. Reduce `maxEpochs`: Start with `maxEpochs 50` for faster iteration
2. Use `architecture Classical` for quick baseline
3. Reduce dataset size during development
4. Increase `convergenceThreshold` to `0.01` for faster convergence

## 📖 Additional Resources

- [CustomerChurnPrediction.fsx](CustomerChurnPrediction.fsx) - Complete tutorial
- [BinaryClassification](../BinaryClassification/) - For simple yes/no predictions
- [AnomalyDetection](../AnomalyDetection/) - For outlier detection
- [SimilaritySearch](../SimilaritySearch/) - For finding similar items

## 🎓 Key Concepts

### Regression vs Classification

**Regression** predicts a continuous number:
- Revenue: $1,250.50
- Demand: 47 units
- LTV: $8,432.00

**Multi-Class** predicts a category:
- Churn timing: 0 (Stay), 1 (30 days), 2 (60 days), 3 (90 days)
- Risk level: 0 (Low), 1 (Medium), 2 (High)
- Lead quality: 0 (Cold), 1 (Warm), 2 (Hot)

### When to Use Which Architecture

| Architecture | Speed | Accuracy | Use Case |
|--------------|-------|----------|----------|
| **Quantum** | Slow | High | Final production model |
| **Hybrid** | Medium | Highest | Best overall performance |
| **Classical** | Fast | Baseline | Quick prototyping, baseline comparison |

**Recommendation**: Start with `Classical` for fast iteration, then switch to `Hybrid` for production.

---

**Ready to predict the future?** Start with [CustomerChurnPrediction.fsx](CustomerChurnPrediction.fsx) and adapt it to your business needs!
