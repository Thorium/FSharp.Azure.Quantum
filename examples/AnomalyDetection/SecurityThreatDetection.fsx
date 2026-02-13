(**
# Anomaly Detection: Security Threat Detection

One-class quantum machine learning for detecting unusual patterns.

(*
===============================================================================
 Background Theory
===============================================================================

Anomaly detection identifies data points that deviate significantly from "normal"
behavior -- critical for security (intrusion detection), fraud prevention, and
quality control. Unlike binary classification, anomaly detection trains only on
normal examples, learning a boundary that separates normal from everything else.
This "one-class classification" approach is essential when anomalies are rare,
diverse, or unknown (zero-day attacks, novel fraud schemes).

Quantum approaches to anomaly detection leverage high-dimensional feature spaces
and quantum kernel methods. The quantum one-class SVM encodes normal data into
quantum states and learns a decision boundary in Hilbert space. For n qubits,
the feature space is 2^n-dimensional, potentially capturing complex patterns
invisible to classical methods. The quantum kernel K(x,x') = |<psi(x)|psi(x')>|^2
measures similarity between data points, with anomalies having low similarity
to the normal training set.

Key Equations:
  - One-class SVM objective: min 1/2||w||^2 - rho + (1/nu*m) Sum_i xi_i  s.t. w*phi(x_i) >= rho - xi_i
  - Quantum kernel: K(x,x') = |<psi(x)|psi(x')>|^2 (overlap of quantum states)
  - Anomaly score: s(x) = -<psi(x)|rho_normal|psi(x)> (distance from normal density)
  - Decision boundary: f(x) = sign(Sum_i alpha_i K(x_i,x) - rho)
  - Reconstruction error: ||x - D(E(x))||^2 for quantum autoencoder approach

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

### Usage

    dotnet fsi SecurityThreatDetection.fsx                                    (defaults)
    dotnet fsi SecurityThreatDetection.fsx -- --help                          (show options)
    dotnet fsi SecurityThreatDetection.fsx -- --example 3 --sensitivity high
    dotnet fsi SecurityThreatDetection.fsx -- --quiet --output results.json
    dotnet fsi SecurityThreatDetection.fsx -- --example all --csv results.csv
*)

/// Anomaly Detection Example: Security Threat Detection
/// Implementation using quantum one-class classification

//#r "nuget: FSharp.Azure.Quantum"
#r "../../src/FSharp.Azure.Quantum/bin/Debug/net10.0/FSharp.Azure.Quantum.dll"

#load "../_common/Cli.fs"
#load "../_common/Data.fs"
#load "../_common/Reporting.fs"

open System
open FSharp.Azure.Quantum.Business
open FSharp.Azure.Quantum.Business.AnomalyDetector
open FSharp.Azure.Quantum.Core.BackendAbstraction
open FSharp.Azure.Quantum.Backends.LocalBackend
open FSharp.Azure.Quantum.Examples.Common

// ==============================================================================
// CLI ARGUMENT PARSING
// ==============================================================================

let argv = fsi.CommandLineArgs |> Array.skip 1
let args = Cli.parse argv

Cli.exitIfHelp
    "SecurityThreatDetection.fsx"
    "Anomaly detection for network security using quantum one-class classification."
    [ { Cli.OptionSpec.Name = "example";      Description = "Example to run (1-5 or all)";        Default = Some "all" }
      { Cli.OptionSpec.Name = "sensitivity";  Description = "Detection sensitivity (low/medium/high)"; Default = Some "medium" }
      { Cli.OptionSpec.Name = "output";       Description = "Write results to JSON file";          Default = None }
      { Cli.OptionSpec.Name = "csv";          Description = "Write results to CSV file";           Default = None }
      { Cli.OptionSpec.Name = "quiet";        Description = "Suppress informational output";       Default = None } ]
    args

let quiet = Cli.hasFlag "quiet" args
let outputPath = Cli.tryGet "output" args
let csvPath = Cli.tryGet "csv" args
let exampleFilter = Cli.getOr "example" "all" args
let cliSensitivity =
    match (Cli.getOr "sensitivity" "medium" args).ToLowerInvariant() with
    | "low" -> Low
    | "high" -> High
    | _ -> Medium

let shouldRun ex =
    exampleFilter = "all" || exampleFilter = string ex

/// Explicit quantum backend (Rule 1: all code depends on IQuantumBackend)
let quantumBackend = LocalBackend() :> IQuantumBackend

// Accumulate results for JSON/CSV output
let results = ResizeArray<{| Example: string; Status: string; Details: Map<string, obj> |}>()

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

let normalTraffic = generateNormalTraffic()

// ============================================================================
// EXAMPLE 1: Basic Security Monitoring
// ============================================================================

// Store production detector for examples 3-5
let mutable productionDetectorResult : Result<AnomalyDetector.Detector, FSharp.Azure.Quantum.Core.QuantumError> = Error (FSharp.Azure.Quantum.Core.QuantumError.Other "not trained")

if shouldRun 1 then
    if not quiet then
        printfn "=== Example 1: Security Threat Detection (Basic) ===\n"
        printfn "Training on %d normal network sessions..." normalTraffic.Length
        printfn "Learning patterns of legitimate traffic...\n"

    // Train detector on normal traffic only
    let result1 = anomalyDetection {
        trainOnNormalData normalTraffic
        sensitivity cliSensitivity
        backend quantumBackend
    }

    match result1 with
    | Error err ->
        if not quiet then printfn "Training failed: %s" err.Message
        results.Add({| Example = "1-basic"; Status = "error"; Details = Map.ofList ["error", box err.Message] |})

    | Ok detector ->
        if not quiet then
            printfn "Detector trained!"
            printfn "  Training time: %A\n" detector.Metadata.TrainingTime

        // Test on known anomalies
        let threats = generateAnomalousTraffic()
        let threatNames = [| "Port Scanning"; "Brute Force Attack"; "Data Exfiltration"; "DDoS Attack"; "Suspicious Access" |]
        
        if not quiet then printfn "Checking suspicious activities:\n"
        
        let threatResults = ResizeArray<{| Name: string; IsAnomaly: bool; Score: float; Confidence: float |}>()
        
        threats
        |> Array.iteri (fun i traffic ->
            match AnomalyDetector.check traffic detector with
            | Ok result ->
                threatResults.Add({| Name = threatNames.[i]; IsAnomaly = result.IsAnomaly; Score = result.AnomalyScore; Confidence = result.Confidence |})
                if not quiet then
                    let status = if result.IsAnomaly then "THREAT" else "OK"
                    printfn "%s: %s" threatNames.[i] status
                    printfn "  Anomaly Score: %.2f (%.0f%% confidence)" 
                        result.AnomalyScore (result.Confidence * 100.0)
                    if result.IsAnomaly then
                        printfn "  Action: %s" 
                            (if result.AnomalyScore > 0.8 then "BLOCK IMMEDIATELY"
                             else "FLAG FOR INVESTIGATION")
                    printfn ""
            | Error err ->
                if not quiet then printfn "%s: Check failed - %s\n" threatNames.[i] err.Message
        )
        
        results.Add({|
            Example = "1-basic"
            Status = "ok"
            Details = Map.ofList [
                "threats_checked", box threats.Length
                "threat_results", box (threatResults |> Seq.toArray)
            ]
        |})

// ============================================================================
// EXAMPLE 2: Sensitivity Levels
// ============================================================================

if shouldRun 2 then
    if not quiet then printfn "\n=== Example 2: Adjusting Sensitivity ===\n"

    let sensLevels = [| (Low, "LOW"); (Medium, "MEDIUM"); (High, "HIGH") |]
    let sensResults = ResizeArray<{| Level: string; Anomalies: int; Rate: float |}>()

    for (sens, sensName) in sensLevels do
        if not quiet then printfn "Testing with %s sensitivity..." sensName
        
        match anomalyDetection { trainOnNormalData normalTraffic; sensitivity sens; backend quantumBackend } with
        | Ok detector ->
            let testTraffic = Array.append (normalTraffic |> Array.take 10) (generateAnomalousTraffic())
            
            match AnomalyDetector.checkBatch testTraffic detector with
            | Ok batch ->
                sensResults.Add({| Level = sensName; Anomalies = batch.AnomaliesDetected; Rate = batch.AnomalyRate |})
                if not quiet then
                    printfn "  Checked %d samples" batch.TotalItems
                    printfn "  Detected %d anomalies (%.1f%%)\n" 
                        batch.AnomaliesDetected (batch.AnomalyRate * 100.0)
            | Error err ->
                if not quiet then printfn "  Batch check failed: %s\n" err.Message
        | Error err ->
            if not quiet then printfn "  Training failed: %s\n" err.Message

    results.Add({|
        Example = "2-sensitivity"
        Status = "ok"
        Details = Map.ofList [
            "sensitivity_results", box (sensResults |> Seq.toArray)
        ]
    |})

// ============================================================================
// EXAMPLE 3: Production Deployment
// ============================================================================

if shouldRun 3 then
    if not quiet then printfn "\n=== Example 3: Production Security Monitoring ===\n"

    let prodResult = anomalyDetection {
        trainOnNormalData normalTraffic
        backend quantumBackend
        
        // Production settings
        sensitivity High              // Don't miss threats
        contaminationRate 0.02        // Assume 2% training data may be bad
        
        // Enable logging
        verbose (not quiet)
        
        // Save for deployment
        note "Network security threat detector - trained on Q4 2024 traffic"
    }

    productionDetectorResult <- prodResult

    match prodResult with
    | Error err ->
        if not quiet then printfn "Production detector failed: %s" err.Message
        results.Add({| Example = "3-production"; Status = "error"; Details = Map.ofList ["error", box err.Message] |})

    | Ok detector ->
        if not quiet then printfn "\nProduction detector ready\n"
        
        // Simulate real-time monitoring
        if not quiet then printfn "=== Real-Time Monitoring Simulation ===\n"
        
        let monitoredSessions = [|
            ("Normal User Login", [| 800.0; 1500.0; 3.0; 0.0; 2.0; 50.0; 9.0; 1.0 |])
            ("Port Scan Attempt", [| 1000.0; 500.0; 50.0; 0.0; 100.0; 50.0; 14.0; 2.0 |])
            ("Regular File Download", [| 2000.0; 5000.0; 2.0; 0.0; 1.0; 20.0; 10.0; 1.0 |])
            ("Brute Force Attack", [| 500.0; 300.0; 30.0; 50.0; 5.0; 20.0; 3.0; 1.0 |])
            ("Normal Email Send", [| 1200.0; 800.0; 5.0; 0.0; 1.0; 30.0; 11.0; 1.0 |])
        |]
        
        let monitorResults = ResizeArray<{| Name: string; IsAnomaly: bool; Score: float |}>()
        
        monitoredSessions
        |> Array.iter (fun (name, traffic) ->
            match AnomalyDetector.check traffic detector with
            | Ok result ->
                monitorResults.Add({| Name = name; IsAnomaly = result.IsAnomaly; Score = result.AnomalyScore |})
                if not quiet then
                    printfn "[%s] %s" (DateTime.Now.ToString("HH:mm:ss")) name
                    if result.IsAnomaly then
                        printfn "  SECURITY ALERT"
                        printfn "  Threat Level: %.0f%%" (result.AnomalyScore * 100.0)
                        printfn "  Recommended Action: %s"
                            (if result.AnomalyScore > 0.8 then "BLOCK IP + ALERT SECURITY TEAM"
                             elif result.AnomalyScore > 0.5 then "FLAG + INCREASE MONITORING"
                             else "LOG FOR REVIEW")
                    else
                        printfn "  Normal traffic"
                    printfn ""
            | Error err ->
                if not quiet then printfn "  Monitoring error: %s\n" err.Message
        )
        
        results.Add({|
            Example = "3-production"
            Status = "ok"
            Details = Map.ofList [
                "monitor_results", box (monitorResults |> Seq.toArray)
            ]
        |})

// ============================================================================
// EXAMPLE 4: Explainability - Why is it anomalous?
// ============================================================================

if shouldRun 4 then
    if not quiet then printfn "\n=== Example 4: Explaining Anomalies ===\n"

    // Use production detector if available, otherwise train fresh
    let detectorForExplain =
        match productionDetectorResult with
        | Ok d -> Ok d
        | Error _ ->
            anomalyDetection {
                trainOnNormalData normalTraffic
                sensitivity High
                backend quantumBackend
                verbose false
            }

    match detectorForExplain with
    | Ok detector ->
        // Investigate the port scanning attempt
        let portScan = [| 1000.0; 500.0; 50.0; 0.0; 100.0; 50.0; 14.0; 2.0 |]
        
        if not quiet then printfn "Analyzing suspicious port scanning activity...\n"
        
        match AnomalyDetector.explain portScan detector normalTraffic with
        | Ok contributions ->
            if not quiet then
                printfn "Top factors contributing to anomaly score:\n"
                contributions
                |> Array.take (min 5 contributions.Length)
                |> Array.iteri (fun i (feature, score) ->
                    printfn "%d. %s: %.2f standard deviations from normal" (i+1) feature score
                )
                printfn "\nInterpretation:"
                printfn "  - This traffic is scanning many ports (Feature_5)"
                printfn "  - Much higher connection rate than normal (Feature_3)"
                printfn "  - Pattern consistent with network reconnaissance"
                printfn ""
            
            results.Add({|
                Example = "4-explainability"
                Status = "ok"
                Details = Map.ofList [
                    "top_factors", box (contributions |> Array.take (min 5 contributions.Length))
                ]
            |})
        
        | Error err ->
            if not quiet then printfn "Explanation failed: %s" err.Message
            results.Add({| Example = "4-explainability"; Status = "error"; Details = Map.ofList ["error", box err.Message] |})

    | Error err ->
        if not quiet then printfn "Detector not available: %s" err.Message
        results.Add({| Example = "4-explainability"; Status = "skipped"; Details = Map.ofList ["reason", box "No detector available"] |})

// ============================================================================
// EXAMPLE 5: Batch Analysis for Daily Reports
// ============================================================================

if shouldRun 5 then
    if not quiet then printfn "\n=== Example 5: Daily Security Report ===\n"

    // Use production detector if available, otherwise train fresh
    let detectorForBatch =
        match productionDetectorResult with
        | Ok d -> Ok d
        | Error _ ->
            anomalyDetection {
                trainOnNormalData normalTraffic
                sensitivity High
                backend quantumBackend
                verbose false
            }

    match detectorForBatch with
    | Ok detector ->
        // Simulate 24 hours of traffic
        let dailyTraffic = 
            Array.append 
                (generateNormalTraffic())
                (Array.replicate 10 (generateAnomalousTraffic()) |> Array.concat)
        
        if not quiet then printfn "Analyzing %d network sessions from past 24 hours...\n" dailyTraffic.Length
        
        match AnomalyDetector.checkBatch dailyTraffic detector with
        | Ok batch ->
            if not quiet then
                printfn "=== Daily Security Report ==="
                printfn "Date: %s\n" (DateTime.Now.ToString("yyyy-MM-dd"))
                printfn "Summary:"
                printfn "  Total Sessions: %d" batch.TotalItems
                printfn "  Anomalies Detected: %d" batch.AnomaliesDetected
                printfn "  Anomaly Rate: %.2f%%\n" (batch.AnomalyRate * 100.0)
                
                printfn "Risk Assessment: %s"
                    (if batch.AnomalyRate > 0.2 then "HIGH - Possible ongoing attack"
                     elif batch.AnomalyRate > 0.1 then "MEDIUM - Increased suspicious activity"
                     else "LOW - Normal levels")
                
                printfn "\nTop 5 Most Suspicious Sessions:"
                batch.TopAnomalies
                |> Array.take (min 5 batch.TopAnomalies.Length)
                |> Array.iteri (fun i (idx, score) ->
                    printfn "  %d. Session #%d - Score: %.2f" (i+1) idx score
                )
                printfn ""
            
            results.Add({|
                Example = "5-daily-report"
                Status = "ok"
                Details = Map.ofList [
                    "total_sessions", box batch.TotalItems
                    "anomalies_detected", box batch.AnomaliesDetected
                    "anomaly_rate", box (batch.AnomalyRate * 100.0)
                ]
            |})
        
        | Error err ->
            if not quiet then printfn "Batch analysis failed: %s" err.Message
            results.Add({| Example = "5-daily-report"; Status = "error"; Details = Map.ofList ["error", box err.Message] |})

    | Error err ->
        if not quiet then printfn "Detector not available: %s" err.Message
        results.Add({| Example = "5-daily-report"; Status = "skipped"; Details = Map.ofList ["reason", box "No detector available"] |})

// ============================================================================
// INTEGRATION PATTERNS
// ============================================================================

if not quiet && exampleFilter = "all" then
    printfn "\n=== Integration Patterns ===\n"

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
                await firewall.BlockIP(traffic.SourceIP)
                await siem.RaiseAlert(AlertLevel.Critical, traffic)
            | result when result.IsAnomaly ->
                await siem.RaiseAlert(AlertLevel.Warning, traffic)
            | _ -> ()
            
            do! Async.Sleep(1000)
    }
"""

    printfn "Adaptive Learning:"
    printfn """
// Retrain weekly with latest normal traffic
let retrainWeekly() =
    async {
        let! normalTraffic = database.GetVerifiedNormalTraffic(days = 7)
        let! newDetector = 
            AnomalyDetectionBuilder()
                .TrainOnNormalData(normalTraffic)
                .WithSensitivity(Sensitivity.High)
                .Build()
        
        let! testResults = validator.Compare(currentDetector, newDetector)
        if testResults.NewDetectorBetter then
            newDetector.SaveTo("production_detector.model")
    }
"""

// ============================================================================
// OUTPUT
// ============================================================================

if outputPath.IsSome then
    let payload = {| script = "SecurityThreatDetection.fsx"; timestamp = DateTime.UtcNow; results = results |> Seq.toArray |}
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
    printfn "Example complete!"
    printfn ""
    printfn "Try these options:"
    printfn "  dotnet fsi SecurityThreatDetection.fsx -- --help"
    printfn "  dotnet fsi SecurityThreatDetection.fsx -- --example 2 --sensitivity high"
    printfn "  dotnet fsi SecurityThreatDetection.fsx -- --quiet --output results.json"
    printfn "  dotnet fsi SecurityThreatDetection.fsx -- --example all --csv results.csv"
