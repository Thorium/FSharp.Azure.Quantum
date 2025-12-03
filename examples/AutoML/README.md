# AutoML - Automated Machine Learning with Quantum

**The simplest ML API ever - just provide data, get the best model automatically.**

The `AutoMLBuilder` automatically tries multiple approaches (classification, regression, anomaly detection), tests different architectures (Quantum, Hybrid, Classical), and tunes hyperparameters to find the best model for your data. Zero ML expertise required.

## 🎯 What Is AutoML?

AutoML (Automated Machine Learning) removes the guesswork from ML by:

1. **Analyzing your data** - Understands problem type automatically
2. **Trying multiple approaches** - Binary classification, multi-class, regression, anomaly detection
3. **Testing architectures** - Quantum, Hybrid, Classical
4. **Tuning hyperparameters** - Learning rate, epochs, convergence thresholds
5. **Returning the winner** - Best model with detailed performance report

**Perfect for:**
- 🚀 **Quick prototyping**: "Just give me a working model"
- 👥 **Non-experts**: Don't know which algorithm to use
- 📊 **Baseline comparison**: What's the best possible with this data?
- 🔬 **Model selection**: Compare all approaches at once

## 🚀 Quick Start

### F# - Zero Config

```fsharp
#r "nuget: FSharp.Azure.Quantum"
open FSharp.Azure.Quantum.Business

// MINIMAL USAGE: Just provide data
let result = autoML {
    trainWith features labels
}

match result with
| Ok model ->
    printfn "Best model: %s" model.BestModelType
    printfn "Score: %.2f%%" (model.Score * 100.0)
    
    // Use the best model
    let prediction = AutoML.predict newSample model
| Error e ->
    printfn "AutoML failed: %s" e
```

### C# - Zero Config

```csharp
using FSharp.Azure.Quantum.Business.CSharp;

// MINIMAL USAGE: Just provide data
var result = new AutoMLBuilder()
    .WithData(features, labels)
    .Build();

Console.WriteLine($"Best model: {result.BestModelType}");
Console.WriteLine($"Score: {result.Score * 100:F2}%");

// Use the best model
var prediction = result.Predict(newSample);
```

That's it! AutoML does everything else automatically.

## 📚 Complete Tutorial

See [QuickPrototyping.fsx](QuickPrototyping.fsx) for comprehensive examples including:
1. **Zero-config AutoML** - Minimal usage
2. **Custom search configuration** - Control what to try
3. **Regression problems** - Revenue prediction
4. **Full analysis** - Compare everything
5. **Production workflow** - Quality gates and deployment

## ⚙️ Configuration Options

### Basic Configuration

```fsharp
// F#
let result = autoML {
    trainWith features labels
    
    // Optional: Control what to try
    tryBinaryClassification true
    tryMultiClass 4
    tryRegression true
    tryAnomalyDetection false
    
    // Architectures to test
    tryArchitectures [Quantum; Hybrid; Classical]
    
    // Search budget
    maxTrials 20
    maxTimeMinutes 10
    
    verbose true
}
```

```csharp
// C#
var result = new AutoMLBuilder()
    .WithData(features, labels)
    
    // Optional: Control what to try
    .TryBinaryClassification(true)
    .TryMultiClass(4)
    .TryRegression(true)
    .TryAnomalyDetection(false)
    
    // Architectures to test
    .WithArchitectures(
        SearchArchitecture.Quantum,
        SearchArchitecture.Hybrid,
        SearchArchitecture.Classical)
    
    // Search budget
    .WithMaxTrials(20)
    .WithMaxTimeMinutes(10)
    
    .WithVerbose(true)
    .Build();
```

### Advanced Configuration

```fsharp
// F#
let result = autoML {
    trainWith features labels
    
    validationSplit 0.25     // Use 25% for validation
    randomSeed 42            // Reproducible results
    
    backend azureBackend     // Use specific quantum backend
    saveModelTo "best_model.json"
}
```

```csharp
// C#
var result = new AutoMLBuilder()
    .WithData(features, labels)
    .WithValidationSplit(0.25)
    .WithRandomSeed(42)
    .WithBackend(azureBackend)
    .SaveBestModelTo("best_model.json")
    .Build();
```

## 📊 Understanding Results

### Search Report

```fsharp
match result with
| Ok automlResult ->
    // Best model found
    printfn "Best Model: %s" automlResult.BestModelType
    printfn "Architecture: %A" automlResult.BestArchitecture
    printfn "Score: %.2f%%" (automlResult.Score * 100.0)
    
    // Search statistics
    printfn "Successful Trials: %d" automlResult.SuccessfulTrials
    printfn "Failed Trials: %d" automlResult.FailedTrials
    printfn "Total Time: %A" automlResult.TotalSearchTime
    
    // Best hyperparameters
    printfn "Learning Rate: %f" automlResult.BestHyperparameters.LearningRate
    printfn "Max Epochs: %d" automlResult.BestHyperparameters.MaxEpochs
| Error e -> printfn "Error: %s" e
```

### Detailed Trial Analysis

```fsharp
// Analyze all trials
automlResult.AllTrials
|> Array.filter (fun t -> t.Success)
|> Array.sortByDescending (fun t -> t.Score)
|> Array.take 5
|> Array.iter (fun trial ->
    let modelTypeStr = match trial.ModelType with
                       | AutoML.BinaryClassification -> "Binary"
                       | AutoML.MultiClassClassification n -> $"Multi-Class ({n})"
                       | AutoML.Regression -> "Regression"
                       | AutoML.AnomalyDetection -> "Anomaly"
                       | _ -> "Other"
    
    printfn "%s with %A: %.2f%%" modelTypeStr trial.Architecture (trial.Score * 100.0)
)
```

```csharp
// C#
var topTrials = result.AllTrials
    .Where(t => t.Success)
    .OrderByDescending(t => t.Score)
    .Take(5);

foreach (var trial in topTrials)
{
    Console.WriteLine($"{trial.ModelType} with {trial.Architecture}: {trial.Score * 100:F2}%");
    Console.WriteLine($"  Time: {trial.TrainingTime.TotalSeconds:F1}s");
    Console.WriteLine($"  LR: {trial.Hyperparameters.LearningRate}, Epochs: {trial.Hyperparameters.MaxEpochs}");
}
```

## 🎯 Making Predictions

AutoML returns different prediction types based on the best model:

```fsharp
// F#
match AutoML.predict newSample automlResult with
| Ok (AutoML.BinaryPrediction pred) ->
    printfn "Binary: %s (%.1f%%)" 
        (if pred.IsPositive then "YES" else "NO")
        (pred.Confidence * 100.0)

| Ok (AutoML.CategoryPrediction pred) ->
    printfn "Category: %d (%.1f%%)" pred.Category (pred.Confidence * 100.0)
    printfn "Probabilities: %A" pred.Probabilities

| Ok (AutoML.RegressionPrediction pred) ->
    printfn "Value: %.2f" pred.Value

| Ok (AutoML.AnomalyPrediction pred) ->
    printfn "Anomaly: %b (score: %.2f)" pred.IsAnomaly pred.AnomalyScore

| Error e -> printfn "Prediction failed: %s" e
```

```csharp
// C#
var prediction = result.Predict(newSample);

switch (prediction)
{
    case ClassificationResult binary:
        Console.WriteLine($"Binary: {(binary.IsPositive ? "YES" : "NO")} ({binary.Confidence * 100:F1}%)");
        break;
    
    case CategoryPrediction category:
        Console.WriteLine($"Category: {category.Category} ({category.Confidence * 100:F1}%)");
        Console.WriteLine($"Probabilities: {string.Join(", ", category.Probabilities)}");
        break;
    
    case RegressionPrediction regression:
        Console.WriteLine($"Value: {regression.Value:F2}");
        break;
    
    case AnomalyResult anomaly:
        Console.WriteLine($"Anomaly: {anomaly.IsAnomaly} (score: {anomaly.AnomalyScore:F2})");
        break;
}
```

## 🏭 Production Patterns

### Pattern 1: Quick Baseline

```fsharp
/// Get a working model in minutes
let quickBaseline (features: float array array) (labels: float array) =
    let result = autoML {
        trainWith features labels
        
        // Fast search - Classical only
        tryArchitectures [Classical]
        maxTrials 10
        maxTimeMinutes 5
        
        verbose false
    }
    
    match result with
    | Ok model -> 
        printfn "✅ Baseline: %.2f%%" (model.Score * 100.0)
        Some model
    | Error e -> 
        printfn "❌ Failed: %s" e
        None
```

### Pattern 2: Production Deployment

```fsharp
/// Production-grade AutoML with quality gates
let productionAutoML (features: float array array) (labels: float array) =
    let result = autoML {
        trainWith features labels
        
        // Production settings
        tryArchitectures [Hybrid; Classical]  // Skip Quantum for speed
        maxTrials 20
        validationSplit 0.2
        randomSeed 42  // Reproducible
        
        verbose true
    }
    
    match result with
    | Ok model when model.Score >= 0.75 ->
        // Quality gate passed
        printfn "✅ Model approved: %.2f%%" (model.Score * 100.0)
        printfn "💾 Saving to production..."
        // Save and deploy
        Some model
    
    | Ok model ->
        // Quality gate failed
        printfn "⚠️  Model quality insufficient: %.2f%% < 75%%" (model.Score * 100.0)
        printfn "💡 Recommendation: More training data needed"
        None
    
    | Error e ->
        printfn "❌ AutoML failed: %s" e
        None
```

### Pattern 3: Periodic Retraining

```fsharp
/// Retrain with fresh data and compare
let retrainAndCompare 
    (newFeatures: float array array) 
    (newLabels: float array) 
    (currentScore: float) =
    
    printfn "🔄 Retraining with %d new samples..." newFeatures.Length
    
    let result = autoML {
        trainWith newFeatures newLabels
        tryArchitectures [Hybrid]
        maxTrials 15
        randomSeed 42
    }
    
    match result with
    | Ok newModel when newModel.Score > currentScore ->
        printfn "✅ New model better: %.2f%% > %.2f%%" 
            (newModel.Score * 100.0) (currentScore * 100.0)
        printfn "📦 Deploying updated model..."
        Some newModel
    
    | Ok newModel ->
        printfn "⚠️  New model not better: %.2f%% <= %.2f%%" 
            (newModel.Score * 100.0) (currentScore * 100.0)
        printfn "💡 Keeping current model"
        None
    
    | Error e ->
        printfn "❌ Retraining failed: %s" e
        None
```

### Pattern 4: A/B Testing

```csharp
// C# - A/B test AutoML vs current model
public class ModelABTest
{
    public async Task<bool> ShouldDeployAutoML(
        double[][] features, 
        double[] labels,
        IExistingModel currentModel)
    {
        Console.WriteLine("🧪 Running A/B test: AutoML vs Current Model");
        
        // Train AutoML model
        var automl = new AutoMLBuilder()
            .WithData(features, labels)
            .WithArchitectures(SearchArchitecture.Hybrid)
            .WithMaxTrials(15)
            .WithValidationSplit(0.3)
            .Build();
        
        // Split validation data
        var validationSize = (int)(features.Length * 0.3);
        var valFeatures = features.Skip(features.Length - validationSize).ToArray();
        var valLabels = labels.Skip(labels.Length - validationSize).ToArray();
        
        // Evaluate both models
        var automlCorrect = 0;
        var currentCorrect = 0;
        
        for (int i = 0; i < valFeatures.Length; i++)
        {
            var automlPred = automl.Predict(valFeatures[i]);
            var currentPred = currentModel.Predict(valFeatures[i]);
            
            // Count correct predictions (simplified)
            if (IsCorrect(automlPred, valLabels[i])) automlCorrect++;
            if (IsCorrect(currentPred, valLabels[i])) currentCorrect++;
        }
        
        var automlAccuracy = (double)automlCorrect / valFeatures.Length;
        var currentAccuracy = (double)currentCorrect / valFeatures.Length;
        
        Console.WriteLine($"AutoML Accuracy: {automlAccuracy * 100:F2}%");
        Console.WriteLine($"Current Model: {currentAccuracy * 100:F2}%");
        
        // Deploy if >5% improvement
        if (automlAccuracy > currentAccuracy + 0.05)
        {
            Console.WriteLine("✅ AutoML wins - deploying new model");
            return true;
        }
        else
        {
            Console.WriteLine("⚠️  Current model still better - no change");
            return false;
        }
    }
}
```

## 💡 Best Practices

### 1. Start Simple, Then Optimize

```fsharp
// Step 1: Quick baseline (Classical only, fast)
let baseline = autoML {
    trainWith features labels
    tryArchitectures [Classical]
    maxTrials 5
}

// Step 2: If baseline looks good, try advanced
let advanced = autoML {
    trainWith features labels
    tryArchitectures [Quantum; Hybrid; Classical]
    maxTrials 30
}
```

### 2. Set Time Budgets

```fsharp
// Development: Quick iteration
let devModel = autoML {
    trainWith features labels
    maxTimeMinutes 5  // Stop after 5 minutes
    maxTrials 10
}

// Production: Thorough search
let prodModel = autoML {
    trainWith features labels
    maxTimeMinutes 30  // More time for better results
    maxTrials 50
}
```

### 3. Use Quality Gates

```fsharp
let deployModel (model: AutoML.AutoMLResult) =
    // Quality checks
    if model.Score < 0.70 then
        Error "Model accuracy too low (<70%)"
    elif model.SuccessfulTrials < 5 then
        Error "Not enough successful trials"
    elif model.Score > 0.99 then
        Error "Suspiciously high score - possible overfitting"
    else
        Ok "Model approved for deployment"
```

### 4. Monitor Failed Trials

```fsharp
// Analyze failures
let failures = 
    automlResult.AllTrials
    |> Array.filter (fun t -> not t.Success)

if failures.Length > automlResult.SuccessfulTrials then
    printfn "⚠️  High failure rate: %d/%d trials failed" 
        failures.Length automlResult.AllTrials.Length
    
    // Log error messages for debugging
    failures 
    |> Array.choose (fun t -> t.ErrorMessage)
    |> Array.distinct
    |> Array.iter (printfn "Error: %s")
```

### 5. Use Seed for Reproducibility

```fsharp
// Development: Random exploration
let exploratory = autoML {
    trainWith features labels
    randomSeed None  // Different results each run
}

// Production: Reproducible results
let reproducible = autoML {
    trainWith features labels
    randomSeed (Some 42)  // Same results every time
}
```

## 🔧 Troubleshooting

### All Trials Failing

**Problem**: AutoML reports all trials failed.

**Solutions**:
1. **Check data quality**: Ensure features and labels are valid
2. **Check data size**: Need at least 20-30 samples
3. **Check labels**: For classification, ensure labels are 0, 1, 2, etc.
4. **Try Classical only**: `tryArchitectures [Classical]` for debugging
5. **Enable verbose**: `verbose true` to see what's failing

### Low Scores

**Problem**: Best score is below expectations.

**Solutions**:
1. **More trials**: Increase `maxTrials` to 50 or 100
2. **More time**: Increase `maxTimeMinutes` to 20 or 30
3. **Better features**: Improve feature engineering
4. **More data**: Collect more training samples
5. **Try all architectures**: Don't restrict to just Quantum or Classical

### Search Too Slow

**Problem**: AutoML takes too long to complete.

**Solutions**:
```fsharp
// Fast search configuration
let fast = autoML {
    trainWith features labels
    
    // Speed optimizations
    tryArchitectures [Classical]  // Fastest
    maxTrials 10                  // Fewer trials
    maxTimeMinutes 5              // Time limit
    tryAnomalyDetection false     // Skip expensive trials
}
```

### Overfitting

**Problem**: Very high training score but poor real-world performance.

**Solutions**:
1. **Increase validation split**: `validationSplit 0.3` (30% validation)
2. **More diverse data**: Ensure training data covers all scenarios
3. **Feature selection**: Remove irrelevant features
4. **Regularization**: Use `Classical` or `Hybrid` (more regularized)

## 🎓 Key Concepts

### When to Use AutoML

| Scenario | Use AutoML | Use Specific Builder |
|----------|------------|---------------------|
| Quick prototype | ✅ Yes | ❌ No |
| Don't know which approach | ✅ Yes | ❌ No |
| Baseline comparison | ✅ Yes | ❌ No |
| Production deployment | ⚠️ Maybe | ✅ Yes |
| Fine-tuned control | ❌ No | ✅ Yes |
| Known problem type | ❌ No | ✅ Yes |

**Recommendation**: Use AutoML for exploration, then switch to specific builders (BinaryClassificationBuilder, PredictiveModelBuilder, etc.) for production.

### Architecture Comparison

| Architecture | Speed | Accuracy | Best For |
|--------------|-------|----------|----------|
| **Classical** | ⚡⚡⚡ Fast | Baseline | Quick prototyping, debugging |
| **Hybrid** | ⚡⚡ Medium | Highest | Production (best balance) |
| **Quantum** | ⚡ Slow | High | Research, small datasets |

### Search Budget Guide

| Budget | maxTrials | maxTimeMinutes | Best For |
|--------|-----------|----------------|----------|
| **Minimal** | 5-10 | 2-5 | Quick check |
| **Standard** | 15-20 | 10-15 | Development |
| **Thorough** | 30-50 | 20-30 | Production |
| **Exhaustive** | 100+ | 60+ | Research |

## 📖 Additional Resources

- [QuickPrototyping.fsx](QuickPrototyping.fsx) - Complete tutorial with 5 examples
- [BinaryClassification](../BinaryClassification/) - For known binary problems
- [PredictiveModeling](../PredictiveModeling/) - For known regression/multi-class
- [AnomalyDetection](../AnomalyDetection/) - For known anomaly detection

## 🚀 Next Steps

After using AutoML to find the best approach for your data:

1. **Note the winner**: Which model type performed best?
2. **Switch to specific builder**: Use BinaryClassificationBuilder, PredictiveModelBuilder, etc.
3. **Fine-tune**: Adjust hyperparameters based on AutoML findings
4. **Deploy**: Use specific builder for production (more control)
5. **Monitor**: Track performance over time
6. **Retrain**: Periodically re-run AutoML with new data

---

**Ready for zero-config ML?** Start with [QuickPrototyping.fsx](QuickPrototyping.fsx) and let AutoML find the best model for your data!
