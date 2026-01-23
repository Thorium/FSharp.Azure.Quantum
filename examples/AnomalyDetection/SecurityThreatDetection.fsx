(**
# Anomaly Detection: Security Threat Detection

One-class quantum machine learning for detecting unusual patterns.

(*
===============================================================================
 Background Theory
===============================================================================

Anomaly detection identifies data points that deviate significantly from "normal"
behavior—critical for security (intrusion detection), fraud prevention, and
quality control. Unlike binary classification, anomaly detection trains only on
normal examples, learning a boundary that separates normal from everything else.
This "one-class classification" approach is essential when anomalies are rare,
diverse, or unknown (zero-day attacks, novel fraud schemes).

Quantum approaches to anomaly detection leverage high-dimensional feature spaces
and quantum kernel methods. The quantum one-class SVM encodes normal data into
quantum states and learns a decision boundary in Hilbert space. For n qubits,
the feature space is 2ⁿ-dimensional, potentially capturing complex patterns
invisible to classical methods. The quantum kernel K(x,x') = |⟨ψ(x)|ψ(x')⟩|²
measures similarity between data points, with anomalies having low similarity
to the normal training set.

Key Equations:
  - One-class SVM objective: min ½||w||² - ρ + (1/νm)Σᵢξᵢ  s.t. w·φ(xᵢ) ≥ ρ - ξᵢ
  - Quantum kernel: K(x,x') = |⟨ψ(x)|ψ(x')⟩|² (overlap of quantum states)
  - Anomaly score: s(x) = -⟨ψ(x)|ρ_normal|ψ(x)⟩ (distance from normal density)
  - Decision boundary: f(x) = sign(Σᵢ αᵢK(xᵢ,x) - ρ) where αᵢ are SVM coefficients
  - Reconstruction error: ||x - D(E(x))||² for quantum autoencoder approach

Quantum Advantage:
  Quantum anomaly detection can identify subtle patterns in high-dimensional data
  where classical methods struggle. For cybersecurity, network traffic has many
  features (packet sizes, timing, protocols, ports); quantum kernels can find
  correlations classical SVMs miss. The approach is particularly powerful for:
  (1) High-dimensional sparse data, (2) Non-linear decision boundaries, (3) Small
  training sets (few normal examples). Financial institutions and cybersecurity
  firms are exploring quantum anomaly detection for fraud and intrusion detection.

References:
  [1] Liu & Rebentrost, "Quantum machine learning for quantum anomaly detection",
      Phys. Rev. A 97, 042315 (2018). https://doi.org/10.1103/PhysRevA.97.042315
  [2] Sakhnenko et al., "Hybrid classical-quantum autoencoder for anomaly detection",
      Quantum Mach. Intell. 4, 27 (2022). https://doi.org/10.1007/s42484-022-00078-0
  [3] Heredge et al., "Quantum Support Vector Machine for Big Data Classification",
      Phys. Rev. Lett. 127, 130501 (2021). https://doi.org/10.1103/PhysRevLett.127.130501
  [4] Wikipedia: Anomaly_detection
      https://en.wikipedia.org/wiki/Anomaly_detection
*)

## Overview

This example demonstrates using quantum anomaly detection to identify
suspicious network traffic patterns. Train on normal behavior only - 
the system automatically identifies anything unusual.

### Business Problem

Identify security threats from network traffic:
- Unauthorized access attempts
- Data exfiltration
- DDoS attacks
- Malware activity

### Approach

Train on normal network traffic only. The quantum detector learns what "normal" 
looks like, then flags anything unusual. No need for labeled attack data!

### Key Features

- One-class classification (normal examples only)
- Sensitivity level tuning
- Real-time threat monitoring
- Explainability (why is it anomalous?)
- Production integration patterns

### Common Use Cases

- Security: Detect intrusions, unauthorized access, suspicious network traffic
- Fraud Detection: Spot unusual transaction patterns
- Quality Control: Find defective products in manufacturing
- System Monitoring: Detect performance issues, failures
- Network Security: Identify DDoS attacks, port scanning, data exfiltration
- IoT/Sensors: Detect equipment failures, sensor malfunctions
*)

/// Anomaly Detection Example: Security Threat Detection
/// Implementation using quantum one-class classification

//#r "nuget: FSharp.Azure.Quantum"
#r "../../src/FSharp.Azure.Quantum/bin/Debug/net10.0/FSharp.Azure.Quantum.dll"

open System
open FSharp.Azure.Quantum.Business
open FSharp.Azure.Quantum.Business.AnomalyDetector

// ============================================================================
// SAMPLE DATA - Network Traffic Patterns
// ============================================================================

/// Generate synthetic network traffic data for demonstration
/// In production, collect from firewalls, IDS, network monitoring tools
let generateNormalTraffic () =
    let random = Random(42)
    
    // Features extracted from network traffic:
    // [bytes_sent, bytes_received, connections_per_min, failed_logins, ports_scanned, 
    //  geographic_distance, time_of_day, protocol_type]
    
    // Normal traffic patterns during business hours
    [| for i in 1..30 ->
        [| 
            random.NextDouble() * 1000.0 + 500.0    // Normal data transfer
            random.NextDouble() * 2000.0 + 1000.0   // Normal responses
            random.NextDouble() * 10.0 + 2.0        // Few connections
            0.0                                      // No failed logins
            random.NextDouble() * 3.0               // Few ports
            random.NextDouble() * 100.0             // Local/regional
            float (8 + random.Next(10))             // Business hours
            random.NextDouble() * 3.0               // Common protocols
        |]
    |]

let generateAnomalousTraffic () =
    let random = Random(123)
    
    // Suspicious patterns
    [|
        // Pattern 1: Port scanning (potential reconnaissance)
        [| 1000.0; 500.0; 50.0; 0.0; 100.0; 50.0; 14.0; 2.0 |]
        
        // Pattern 2: Brute force attack (many failed logins)
        [| 500.0; 300.0; 30.0; 50.0; 5.0; 20.0; 3.0; 1.0 |]
        
        // Pattern 3: Data exfiltration (huge outbound transfer)
        [| 50000.0; 1000.0; 5.0; 0.0; 1.0; 5000.0; 2.0; 4.0 |]
        
        // Pattern 4: DDoS (massive connection attempts)
        [| 2000.0; 1500.0; 500.0; 10.0; 20.0; 1000.0; 12.0; 5.0 |]
        
        // Pattern 5: Unusual time + location (off-hours foreign access)
        [| 800.0; 600.0; 8.0; 5.0; 3.0; 8000.0; 3.0; 1.0 |]
    |]

// ============================================================================
// EXAMPLE 1: Basic Security Monitoring
// ============================================================================

printfn "=== Example 1: Security Threat Detection (Basic) ===\n"

let normalTraffic = generateNormalTraffic()

printfn "Training on %d normal network sessions..." normalTraffic.Length
printfn "Learning patterns of legitimate traffic...\n"

// Train detector on normal traffic only
let result1 = anomalyDetection {
    trainOnNormalData normalTraffic
    sensitivity Medium
}

match result1 with
| Error err ->
    printfn "❌ Training failed: %s" err.Message

| Ok detector ->
    printfn "✅ Detector trained!"
    printfn "  Training time: %A\n" detector.Metadata.TrainingTime
    
    // Test on known anomalies
    let threats = generateAnomalousTraffic()
    let threatNames = [|
        "Port Scanning"
        "Brute Force Attack"
        "Data Exfiltration"
        "DDoS Attack"
        "Suspicious Access"
    |]
    
    printfn "Checking suspicious activities:\n"
    
    threats
    |> Array.iteri (fun i traffic ->
        match AnomalyDetector.check traffic detector with
        | Ok result ->
            let status = if result.IsAnomaly then "🚨 THREAT" else "✅ OK"
            printfn "%s: %s" threatNames.[i] status
            printfn "  Anomaly Score: %.2f (%.0f%% confidence)" 
                result.AnomalyScore (result.Confidence * 100.0)
            
            if result.IsAnomaly then
                printfn "  ⚠️  Action: %s" 
                    (if result.AnomalyScore > 0.8 then "BLOCK IMMEDIATELY"
                     else "FLAG FOR INVESTIGATION")
            printfn ""
        
        | Error err ->
            printfn "❌ %s: Check failed - %s\n" threatNames.[i] err.Message
    )

// ============================================================================
// EXAMPLE 2: Sensitivity Levels
// ============================================================================

printfn "\n=== Example 2: Adjusting Sensitivity ===\n"

let testSensitivity sens sensName =
    printfn "Testing with %s sensitivity..." sensName
    
    match anomalyDetection { trainOnNormalData normalTraffic; sensitivity sens } with
    | Ok detector ->
        let testTraffic = Array.append (normalTraffic |> Array.take 10) (generateAnomalousTraffic())
        
        match AnomalyDetector.checkBatch testTraffic detector with
        | Ok batch ->
            printfn "  Checked %d samples" batch.TotalItems
            printfn "  Detected %d anomalies (%.1f%%)" 
                batch.AnomaliesDetected (batch.AnomalyRate * 100.0)
            printfn ""
        | Error err ->
            printfn "  ❌ Batch check failed: %s\n" err.Message
    | Error err ->
        printfn "  ❌ Training failed: %s\n" err.Message

testSensitivity Low "LOW"
testSensitivity Medium "MEDIUM"
testSensitivity High "HIGH"

// ============================================================================
// EXAMPLE 3: Production Deployment
// ============================================================================

printfn "\n=== Example 3: Production Security Monitoring ===\n"

let productionDetector = anomalyDetection {
    trainOnNormalData normalTraffic
    
    // Production settings
    sensitivity High              // Don't miss threats
    contaminationRate 0.02        // Assume 2% training data may be bad
    
    // Enable logging
    verbose true
    
    // Save for deployment
    note "Network security threat detector - trained on Q4 2024 traffic"
}

match productionDetector with
| Error err ->
    printfn "❌ Production detector failed: %s" err.Message

| Ok detector ->
    printfn "\n✅ Production detector ready\n"
    
    // Simulate real-time monitoring
    printfn "=== Real-Time Monitoring Simulation ===\n"
    
    let monitoredSessions = [|
        ("Normal User Login", [| 800.0; 1500.0; 3.0; 0.0; 2.0; 50.0; 9.0; 1.0 |])
        ("Port Scan Attempt", [| 1000.0; 500.0; 50.0; 0.0; 100.0; 50.0; 14.0; 2.0 |])
        ("Regular File Download", [| 2000.0; 5000.0; 2.0; 0.0; 1.0; 20.0; 10.0; 1.0 |])
        ("Brute Force Attack", [| 500.0; 300.0; 30.0; 50.0; 5.0; 20.0; 3.0; 1.0 |])
        ("Normal Email Send", [| 1200.0; 800.0; 5.0; 0.0; 1.0; 30.0; 11.0; 1.0 |])
    |]
    
    monitoredSessions
    |> Array.iter (fun (name, traffic) ->
        match AnomalyDetector.check traffic detector with
        | Ok result ->
            printfn "[%s] %s" 
                (DateTime.Now.ToString("HH:mm:ss")) 
                name
            
            if result.IsAnomaly then
                printfn "  🚨 SECURITY ALERT"
                printfn "  Threat Level: %.0f%%" (result.AnomalyScore * 100.0)
                printfn "  Recommended Action: %s"
                    (if result.AnomalyScore > 0.8 then 
                        "BLOCK IP + ALERT SECURITY TEAM"
                     elif result.AnomalyScore > 0.5 then
                        "FLAG + INCREASE MONITORING"
                     else
                        "LOG FOR REVIEW")
            else
                printfn "  ✅ Normal traffic"
            
            printfn ""
        
        | Error err ->
            printfn "  ⚠️  Monitoring error: %s\n" err.Message
    )

// ============================================================================
// EXAMPLE 4: Explainability - Why is it anomalous?
// ============================================================================

printfn "\n=== Example 4: Explaining Anomalies ===\n"

match productionDetector with
| Ok detector ->
    // Investigate the port scanning attempt
    let portScan = [| 1000.0; 500.0; 50.0; 0.0; 100.0; 50.0; 14.0; 2.0 |]
    
    printfn "Analyzing suspicious port scanning activity...\n"
    
    match AnomalyDetector.explain portScan detector normalTraffic with
    | Ok contributions ->
        printfn "Top factors contributing to anomaly score:\n"
        
        contributions
        |> Array.take (min 5 contributions.Length)
        |> Array.iteri (fun i (feature, score) ->
            printfn "%d. %s: %.2f standard deviations from normal" 
                (i+1) feature score
        )
        
        printfn "\nInterpretation:"
        printfn "  - This traffic is scanning many ports (Feature_5)"
        printfn "  - Much higher connection rate than normal (Feature_3)"
        printfn "  - Pattern consistent with network reconnaissance"
    
    | Error err ->
        printfn "❌ Explanation failed: %s" err.Message

| Error _ -> ()

// ============================================================================
// EXAMPLE 5: Batch Analysis for Daily Reports
// ============================================================================

printfn "\n\n=== Example 5: Daily Security Report ===\n"

match productionDetector with
| Ok detector ->
    
    // Simulate 24 hours of traffic
    let dailyTraffic = 
        Array.append 
            (generateNormalTraffic())  // 100 normal sessions
            (Array.replicate 10 (generateAnomalousTraffic()) |> Array.concat)  // 50 attacks
    
    printfn "Analyzing %d network sessions from past 24 hours...\n" dailyTraffic.Length
    
    match AnomalyDetector.checkBatch dailyTraffic detector with
    | Ok batch ->
        printfn "=== Daily Security Report ==="
        printfn "Date: %s\n" (DateTime.Now.ToString("yyyy-MM-dd"))
        
        printfn "Summary:"
        printfn "  Total Sessions: %d" batch.TotalItems
        printfn "  Anomalies Detected: %d" batch.AnomaliesDetected
        printfn "  Anomaly Rate: %.2f%%\n" (batch.AnomalyRate * 100.0)
        
        printfn "Risk Assessment: %s"
            (if batch.AnomalyRate > 0.2 then "🔴 HIGH - Possible ongoing attack"
             elif batch.AnomalyRate > 0.1 then "🟡 MEDIUM - Increased suspicious activity"
             else "🟢 LOW - Normal levels")
        
        printfn "\nTop 5 Most Suspicious Sessions:"
        batch.TopAnomalies
        |> Array.take (min 5 batch.TopAnomalies.Length)
        |> Array.iteri (fun i (idx, score) ->
            printfn "  %d. Session #%d - Score: %.2f" (i+1) idx score
        )
        
        printfn "\n✅ Report complete - %d sessions flagged for investigation" 
            batch.AnomaliesDetected
    
    | Error err ->
        printfn "❌ Batch analysis failed: %s" err.Message

| Error _ -> ()

// ============================================================================
// INTEGRATION PATTERNS
// ============================================================================

printfn "\n\n=== Integration Patterns ===\n"

printfn "Real-time Network Monitoring:"
printfn """
// SIEM Integration
let monitorNetworkTraffic() =
    async {
        while true do
            let! traffic = firewall.GetLatestSession()
            let features = extractFeatures(traffic)
            
            match detector.Check(features) with
            | result when result.IsAnomaly && result.AnomalyScore > 0.8 ->
                // High threat - block immediately
                await firewall.BlockIP(traffic.SourceIP)
                await siem.RaiseAlert(AlertLevel.Critical, traffic)
                await securityTeam.NotifyImmediate(traffic)
            
            | result when result.IsAnomaly && result.AnomalyScore > 0.5 ->
                // Medium threat - increase monitoring
                await siem.RaiseAlert(AlertLevel.Warning, traffic)
                await firewall.IncreasedMonitoring(traffic.SourceIP)
            
            | result when result.IsAnomaly ->
                // Low threat - log for review
                await siem.LogSuspicious(traffic)
            
            | _ ->
                // Normal traffic
                ()
            
            do! Async.Sleep(1000)  // Check every second
    }
"""

printfn "\nDaily Batch Analysis:"
printfn """
// Scheduled job for daily security reports
[<Function("DailySecurityReport")>]
let generateDailyReport([<TimerTrigger("0 0 6 * * *")>] timer) =
    async {
        let yesterday = DateTime.UtcNow.AddDays(-1.0)
        let! traffic = database.GetTrafficSince(yesterday)
        
        let features = traffic |> Array.map extractFeatures
        let batch = detector.CheckBatch(features)
        
        let report = {
            Date = yesterday
            TotalSessions = batch.TotalItems
            AnomaliesDetected = batch.AnomaliesDetected
            AnomalyRate = batch.AnomalyRate
            TopThreats = batch.TopAnomalies
        }
        
        // Email to security team
        await email.SendReport(report, "security@company.com")
        
        // Store in database
        await database.SaveDailyReport(report)
    }
"""

printfn "\nAdaptive Learning:"
printfn """
// Retrain weekly with latest normal traffic
[<Function("RetrainDetector")>]
let retrainWeekly([<TimerTrigger("0 0 0 * * 0")>] timer) =
    async {
        // Get last week's verified normal traffic
        let! normalTraffic = database.GetVerifiedNormalTraffic(days = 7)
        
        // Retrain detector
        let! newDetector = 
            AnomalyDetectionBuilder()
                .TrainOnNormalData(normalTraffic)
                .WithSensitivity(Sensitivity.High)
                .Build()
        
        // A/B test before deploying
        let! testResults = validator.Compare(currentDetector, newDetector)
        
        if testResults.NewDetectorBetter then
            newDetector.SaveTo("production_detector_v{version}.model")
            await deploymentService.Deploy(newDetector)
    }
"""

printfn "\n✅ Example complete! See code for integration patterns."
