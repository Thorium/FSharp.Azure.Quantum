# Binary Classification Examples

**Business-oriented quantum machine learning for enterprise developers.**

## 🎯 What is Binary Classification?

Automatically categorize items into one of two groups based on their characteristics.

**Common Use Cases:**
- **Fraud Detection:** Identify suspicious transactions (fraud vs legitimate)
- **Spam Filtering:** Classify emails (spam vs ham)
- **Churn Prediction:** Identify customers likely to leave (churn vs retain)
- **Credit Risk:** Approve or reject loan applications (approve vs reject)
- **Quality Control:** Detect defective products (defect vs pass)
- **Medical Diagnosis:** Detect disease presence (positive vs negative)

## 🚀 Quick Start

### F# Example

```fsharp
#r "nuget: FSharp.Azure.Quantum"
open FSharp.Azure.Quantum.Business

// Train classifier
let classifier = binaryClassification {
    trainWith features labels
}

// Predict
match classifier with
| Ok clf ->
    let result = BinaryClassifier.predict newSample clf
    if result.IsPositive then
        blockTransaction()
| Error msg ->
    printfn "Error: %s" msg
```

### C# Example

```csharp
using FSharp.Azure.Quantum.Business.CSharp;

// Train classifier
var classifier = new BinaryClassificationBuilder()
    .WithFeatures(trainX)
    .WithLabels(trainY)
    .Build();

// Predict
var result = classifier.Classify(newTransaction);
if (result.IsFraud && result.Confidence > 0.8)
    BlockTransaction(newTransaction);
```

## 📚 Examples

### 1. [FraudDetection.fsx](FraudDetection.fsx)

Complete fraud detection example covering:
- Training on transaction data
- Real-time fraud detection
- Model evaluation
- Saving/loading models
- Production integration patterns

**Run it:**
```bash
dotnet fsi FraudDetection.fsx
```

## 🔧 Configuration Options

### Architecture Choices

```fsharp
architecture Quantum    // Pure quantum (default)
architecture Hybrid     // Quantum kernel SVM
architecture Classical  // Classical baseline
```

### Training Parameters

```fsharp
learningRate 0.01           // Learning rate (default: 0.01)
maxEpochs 100               // Max training epochs (default: 100)
convergenceThreshold 0.001  // Early stopping (default: 0.001)
verbose true                // Enable logging (default: false)
```

### Infrastructure

```fsharp
backend azureBackend  // Use Azure Quantum (default: LocalBackend)
shots 1000            // Quantum measurements (default: 1000)
```

### Persistence

```fsharp
saveModelTo "model.json"  // Save after training
note "Model description"  // Add metadata
```

## 📊 Model Evaluation

```fsharp
// Evaluate on test set
match BinaryClassifier.evaluate testX testY classifier with
| Ok metrics ->
    printfn "Accuracy: %.2f%%" (metrics.Accuracy * 100.0)
    printfn "Precision: %.2f%%" (metrics.Precision * 100.0)
    printfn "Recall: %.2f%%" (metrics.Recall * 100.0)
    printfn "F1 Score: %.2f%%" (metrics.F1Score * 100.0)
| Error msg ->
    printfn "Error: %s" msg
```

## 🏢 Production Integration

### REST API (ASP.NET Core)

```csharp
[ApiController]
[Route("api/fraud")]
public class FraudController : ControllerBase
{
    private readonly IBinaryClassifier _classifier;
    
    public FraudController()
    {
        _classifier = BinaryClassificationBuilder.LoadFrom("fraud_detector.model");
    }
    
    [HttpPost("check")]
    public IActionResult CheckFraud([FromBody] Transaction transaction)
    {
        var result = _classifier.Classify(transaction.Features);
        
        return Ok(new {
            TransactionId = transaction.Id,
            IsFraud = result.IsFraud,
            Confidence = result.Confidence,
            Recommendation = result.Confidence > 0.8 ? "BLOCK" : "ALLOW"
        });
    }
}
```

### Azure Functions

```csharp
[Function("DetectFraud")]
public async Task Run(
    [QueueTrigger("transactions")] Transaction transaction,
    [Queue("flagged")] IAsyncCollector<Transaction> flaggedQueue)
{
    var result = _classifier.Classify(transaction.Features);
    
    if (result.IsFraud && result.Confidence > 0.7)
    {
        await flaggedQueue.AddAsync(transaction);
    }
}
```

### Batch Processing

```fsharp
// Process daily batch
let processNightlyBatch() =
    db.GetPendingTransactions()
    |> Array.Parallel.map (fun tx ->
        let prediction = BinaryClassifier.predict tx.Features classifier
        { tx with RiskScore = prediction.Confidence })
    |> Array.filter (fun tx -> tx.RiskScore > 0.7)
    |> db.SaveHighRiskTransactions
```

## 📖 Data Format

### Training Data

**Features:** 2D array (samples × features)
```fsharp
let trainX = [|
    [| 50.0; 10.0; 2.0; 5.0 |]   // Sample 1
    [| 800.0; 23.0; 8.0; 120.0 |] // Sample 2
    // ...
|]
```

**Labels:** 1D array (binary: 0 or 1)
```fsharp
let trainY = [| 0; 1; 0; 1; ... |]  // 0 = legitimate, 1 = fraud
```

### Feature Engineering

Good features are crucial for classification:
- **Transaction Amount:** Normalize to [0, 1]
- **Time of Day:** Convert to hour (0-24)
- **Location:** Distance from home
- **Frequency:** Recent transaction count
- **Category:** Merchant category code

## 🎓 Best Practices

1. **Data Quality:** Clean, normalize, and balance your data
2. **Feature Engineering:** Domain knowledge → better features
3. **Train/Test Split:** Always evaluate on holdout data
4. **Cross-Validation:** Use k-fold for robust evaluation
5. **Monitor Performance:** Track accuracy in production
6. **Regular Retraining:** Update models with new data

## 🔍 Troubleshooting

**Low Accuracy?**
- Check class balance (equal positive/negative samples)
- Normalize features to [0, 1] range
- Try different architecture (Quantum vs Hybrid)
- Increase training epochs

**Slow Training?**
- Reduce number of features
- Use smaller datasets for prototyping
- Try Hybrid architecture (usually faster)

**High False Positives?**
- Adjust confidence threshold
- Collect more training data
- Improve feature engineering

## 📚 Learn More

- [F# Computation Expressions](https://docs.microsoft.com/en-us/dotnet/fsharp/language-reference/computation-expressions)
- [Binary Classification](https://en.wikipedia.org/wiki/Binary_classification)
- [Confusion Matrix](https://en.wikipedia.org/wiki/Confusion_matrix)
- [Quantum Machine Learning](https://en.wikipedia.org/wiki/Quantum_machine_learning)

## 🤝 Contributing

Have a real-world use case? We'd love to add more examples!

## 📄 License

This example is part of FSharp.Azure.Quantum library.
