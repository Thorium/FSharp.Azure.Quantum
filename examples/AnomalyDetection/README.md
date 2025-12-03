# Anomaly Detection Examples

**One-class quantum machine learning for detecting unusual patterns.**

## 🎯 What is Anomaly Detection?

Find items that don't fit normal patterns. You only need examples of "normal" behavior - the system automatically identifies anything unusual.

**Key Advantage:** No need for labeled attack/fraud/defect data - just normal examples!

**Common Use Cases:**
- **Security:** Detect intrusions, unauthorized access, suspicious network traffic
- **Fraud Detection:** Spot unusual transaction patterns
- **Quality Control:** Find defective products in manufacturing
- **System Monitoring:** Detect performance issues, failures
- **Network Security:** Identify DDoS attacks, port scanning, data exfiltration
- **IoT/Sensors:** Detect equipment failures, sensor malfunctions

## 🚀 Quick Start

### F# Example

```fsharp
#r "nuget: FSharp.Azure.Quantum"
open FSharp.Azure.Quantum.Business

// Train on normal data only
let detector = anomalyDetection {
    trainOnNormalData normalSamples
    sensitivity Medium
}

// Check new items
match detector with
| Ok det ->
    let result = AnomalyDetector.check suspiciousSample det
    if result.IsAnomaly && result.AnomalyScore > 0.8 then
        blockImmediately()
| Error msg ->
    printfn "Error: %s" msg
```

### C# Example

```csharp
using FSharp.Azure.Quantum.Business.CSharp;

// Train on normal behavior
var detector = new AnomalyDetectionBuilder()
    .TrainOnNormalData(normalData)
    .WithSensitivity(Sensitivity.Medium)
    .Build();

// Real-time detection
var result = detector.Check(newSample);
if (result.IsAnomaly && result.AnomalyScore > 0.8)
{
    await BlockAndAlert(newSample);
}
```

## 📚 Examples

### 1. [SecurityThreatDetection.fsx](SecurityThreatDetection.fsx)

Complete security monitoring example covering:
- Network traffic anomaly detection
- Sensitivity level tuning
- Real-time threat monitoring
- Explainability (why is it anomalous?)
- Daily security reports
- Production integration patterns

**Run it:**
```bash
dotnet fsi SecurityThreatDetection.fsx
```

## 🔧 Configuration Options

### Sensitivity Levels

```fsharp
sensitivity Low       // Fewer false alarms, may miss some anomalies
sensitivity Medium    // Balanced (default)
sensitivity High      // More detections, more false alarms
sensitivity VeryHigh  // Maximum sensitivity
```

**Choosing Sensitivity:**
- **Security/Fraud:** Use `High` or `VeryHigh` (don't miss threats)
- **Quality Control:** Use `Medium` (balance precision/recall)
- **System Monitoring:** Use `Low` to `Medium` (avoid alert fatigue)

### Contamination Rate

```fsharp
contaminationRate 0.05  // Assume 5% of training data may be anomalous
```

If your "normal" training data might contain some anomalies, set this value.
Default: 0.05 (5%)

### Other Options

```fsharp
backend azureBackend  // Use Azure Quantum (default: LocalBackend)
shots 1000            // Quantum measurements (default: 1000)
verbose true          // Enable logging (default: false)
```

## 📊 Understanding Results

### Anomaly Score

- **0.0 - 0.3:** Definitely normal
- **0.3 - 0.5:** Slightly unusual but probably OK
- **0.5 - 0.7:** Suspicious - flag for review
- **0.7 - 0.9:** Very unusual - likely anomaly
- **0.9 - 1.0:** Extremely anomalous - immediate action

### Recommended Actions

```csharp
var result = detector.Check(sample);

if (result.IsAnomaly && result.AnomalyScore > 0.8)
{
    // High threat - block immediately
    await BlockImmediately(sample);
    await AlertSecurityTeam();
}
else if (result.IsAnomaly && result.AnomalyScore > 0.5)
{
    // Medium threat - investigate
    await FlagForReview(sample);
    await IncreaseMonitoring();
}
else if (result.IsAnomaly)
{
    // Low threat - log only
    await LogSuspicious(sample);
}
```

## 🔍 Explainability

Understand WHY something is anomalous:

```fsharp
match AnomalyDetector.explain sample detector normalData with
| Ok contributions ->
    // Shows which features are most unusual
    contributions
    |> Array.take 5
    |> Array.iter (fun (feature, score) ->
        printfn "%s: %.2f std devs from normal" feature score)
| Error msg ->
    printfn "Error: %s" msg
```

## 🏢 Production Integration

### Real-Time Monitoring (SIEM Integration)

```csharp
public class SecurityMonitor : BackgroundService
{
    private readonly IAnomalyDetector _detector;
    
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var traffic = await _firewall.GetLatestSession();
            var features = ExtractFeatures(traffic);
            
            var result = _detector.Check(features);
            
            if (result.IsAnomaly && result.AnomalyScore > 0.8)
            {
                await _firewall.BlockIP(traffic.SourceIP);
                await _siem.RaiseAlert(AlertLevel.Critical, traffic);
            }
            else if (result.IsAnomaly)
            {
                await _siem.LogSuspicious(traffic);
            }
            
            await Task.Delay(1000, stoppingToken);
        }
    }
}
```

### Batch Processing (Daily Reports)

```csharp
[Function("DailySecurityReport")]
public async Task GenerateReport(
    [TimerTrigger("0 0 6 * * *")] TimerInfo timer)
{
    var yesterday = DateTime.UtcNow.AddDays(-1);
    var traffic = await _database.GetTrafficSince(yesterday);
    
    var features = traffic.Select(ExtractFeatures).ToArray();
    var batch = _detector.CheckBatch(features);
    
    var report = new SecurityReport
    {
        Date = yesterday,
        TotalSessions = batch.TotalItems,
        AnomaliesDetected = batch.AnomaliesDetected,
        AnomalyRate = batch.AnomalyRate,
        TopThreats = batch.TopAnomalies
    };
    
    await _email.SendReport(report, "security@company.com");
}
```

### API Endpoint

```csharp
[ApiController]
[Route("api/security")]
public class SecurityController : ControllerBase
{
    private readonly IAnomalyDetector _detector;
    
    [HttpPost("check")]
    public IActionResult CheckTraffic([FromBody] NetworkTraffic traffic)
    {
        var features = ExtractFeatures(traffic);
        var result = _detector.Check(features);
        
        return Ok(new {
            SessionId = traffic.Id,
            IsAnomaly = result.IsAnomaly,
            AnomalyScore = result.AnomalyScore,
            Recommendation = result.Recommendation
        });
    }
}
```

## 📖 Data Format

### Training Data (Normal Examples Only)

```fsharp
let normalData = [|
    [| 800.0; 1500.0; 3.0; 0.0; 2.0 |]   // Normal sample 1
    [| 750.0; 1600.0; 4.0; 0.0; 1.0 |]   // Normal sample 2
    [| 900.0; 1400.0; 2.0; 0.0; 3.0 |]   // Normal sample 3
    // ... more normal examples
|]
```

**Important:** Only include normal/legitimate examples. The detector learns the boundary of normal behavior.

### Feature Engineering Examples

#### Network Security
```fsharp
let extractNetworkFeatures (session: NetworkSession) = [|
    session.BytesSent
    session.BytesReceived
    session.ConnectionsPerMinute
    session.FailedLoginAttempts
    session.PortsScanned
    session.GeographicDistance
    session.TimeOfDay
    session.ProtocolType
|]
```

#### Transaction Fraud
```fsharp
let extractTransactionFeatures (tx: Transaction) = [|
    tx.Amount
    tx.TimeOfDay
    tx.DaysSinceLastTransaction
    tx.DistanceFromUsualLocation
    tx.MerchantRiskScore
    tx.CardNotPresentFlag
|]
```

#### Manufacturing Quality
```fsharp
let extractQualityFeatures (product: Product) = [|
    product.Weight
    product.Dimension1
    product.Dimension2
    product.SurfaceQuality
    product.TemperatureDuringProduction
    product.ProductionSpeed
|]
```

## 🎓 Best Practices

### 1. Training Data Quality
- **Clean data:** Remove known anomalies from training set
- **Sufficient samples:** Minimum 50-100 normal examples
- **Representative:** Cover full range of normal variation
- **Fresh data:** Retrain regularly (weekly/monthly)

### 2. Sensitivity Tuning
- **Start conservative:** Begin with `Low` or `Medium`
- **Monitor false positives:** Adjust based on operational needs
- **Different use cases:** Security = High, Quality = Medium, Monitoring = Low

### 3. Feature Engineering
- **Normalize:** Scale features to similar ranges [0, 1]
- **Domain knowledge:** Include features that experts use
- **Remove noise:** Filter out irrelevant features
- **Temporal features:** Include time-of-day, day-of-week if relevant

### 4. Production Deployment
- **A/B testing:** Compare new detector with baseline
- **Gradual rollout:** Start with logging, then alerting, then blocking
- **Human feedback:** Let analysts mark false positives
- **Regular retraining:** Adapt to changing normal behavior

### 5. Monitoring
- **Track metrics:** Anomaly rate over time
- **Alert on changes:** Sudden spike in anomalies = potential attack or detector issue
- **False positive rate:** Aim for < 5% in production
- **Response time:** Aim for < 100ms per check

## 🔧 Troubleshooting

**Too Many False Positives?**
- Lower sensitivity level
- Increase contamination rate
- Add more diverse normal examples to training
- Check if normal behavior has changed (retrain)

**Missing Known Anomalies?**
- Increase sensitivity level
- Ensure training data is truly normal
- Check if anomaly is actually within normal variation
- Improve feature engineering

**Slow Performance?**
- Reduce number of features
- Use batch processing where possible
- Consider hybrid architecture
- Cache detector in memory

## 📚 Learn More

- [One-Class SVM](https://en.wikipedia.org/wiki/One-class_classification)
- [Anomaly Detection](https://en.wikipedia.org/wiki/Anomaly_detection)
- [Quantum Kernel Methods](https://arxiv.org/abs/1803.07128)
- [Feature Engineering](https://en.wikipedia.org/wiki/Feature_engineering)

## 🤝 Contributing

Have a real-world anomaly detection use case? We'd love to add more examples!

## 📄 License

This example is part of FSharp.Azure.Quantum library.
