#!/usr/bin/env dotnet fsi
// ============================================================================
// AutoML: Quick Prototyping & Model Selection
// ============================================================================
//
// Demonstrates AutoML for rapid ML experimentation with quantum/hybrid
// architectures. AutoML automatically tries multiple approaches (binary
// classification, multi-class, regression, anomaly detection) and returns
// the best model.
//
// Examples: zero-config, custom search, regression, full comparison, production.
// Extensible starting point for automated quantum ML workflows.
//
// ============================================================================

#r "../../src/FSharp.Azure.Quantum/bin/Debug/net10.0/FSharp.Azure.Quantum.dll"
#load "../_common/Cli.fs"
#load "../_common/Data.fs"
#load "../_common/Reporting.fs"

open System
open FSharp.Azure.Quantum.Business
open FSharp.Azure.Quantum.Business.AutoML
open FSharp.Azure.Quantum.Core.BackendAbstraction
open FSharp.Azure.Quantum.Backends.LocalBackend
open FSharp.Azure.Quantum.Examples.Common

// --- Quantum Backend (Rule 1) ---
let quantumBackend = LocalBackend() :> IQuantumBackend

// --- CLI ---
let argv = fsi.CommandLineArgs |> Array.skip 1
let args = Cli.parse argv

Cli.exitIfHelp
    "QuickPrototyping.fsx"
    "AutoML for quick prototyping: zero-config, custom search, regression, comparison"
    [ { Name = "example"; Description = "Which example (all|zeroconfig|custom|regression|compare|production)"; Default = Some "all" }
      { Name = "max-trials"; Description = "Max trials per search"; Default = Some "1" }
      { Name = "seed"; Description = "Random seed"; Default = Some "42" }
      { Name = "output"; Description = "Write results to JSON file"; Default = None }
      { Name = "csv"; Description = "Write results to CSV file"; Default = None }
      { Name = "quiet"; Description = "Suppress console output"; Default = None } ]
    args

let quiet = Cli.hasFlag "quiet" args
let outputPath = Cli.tryGet "output" args
let csvPath = Cli.tryGet "csv" args
let exampleArg = Cli.getOr "example" "all" args
let cliMaxTrials = Cli.getIntOr "max-trials" 1 args
let seed = Cli.getIntOr "seed" 42 args

let pr fmt = Printf.ksprintf (fun s -> if not quiet then printfn "%s" s) fmt
let shouldRun key = exampleArg = "all" || exampleArg = key

// --- Result Tracking ---

type ExampleResult =
    { Name: string
      Label: string
      BestModel: string
      Architecture: string
      Score: float
      Trials: int
      SearchTimeSec: float }

let mutable jsonResults : ExampleResult list = []
let mutable csvRows : string list list = []

let record (r: ExampleResult) =
    jsonResults <- jsonResults @ [ r ]
    csvRows <- csvRows @ [
        [ r.Name; r.Label; r.BestModel; r.Architecture
          sprintf "%.4f" r.Score; string r.Trials; sprintf "%.1f" r.SearchTimeSec ] ]

// --- Data Generators ---

let generateChurnData (rng: Random) =
    let features = [|
        for _ in 1..30 ->
            [| rng.NextDouble() * 36.0; 50.0 + rng.NextDouble() * 150.0
               float (rng.Next(0, 10)); rng.NextDouble() * 30.0; rng.NextDouble() * 10.0 |]
    |]
    let labels = [|
        for i in 0..29 ->
            if features.[i].[1] < 100.0 && features.[i].[4] < 5.0 && features.[i].[2] > 5.0 then 1.0
            else 0.0
    |]
    (features, labels)

let generateMultiClassData (rng: Random) =
    let features = [|
        for _ in 1..24 ->
            [| 50.0 + rng.NextDouble() * 450.0; rng.NextDouble() * 48.0; rng.NextDouble() * 40.0 |]
    |]
    let labels = [|
        for i in 0..23 ->
            let spend = features.[i].[0]
            let tenure = features.[i].[1]
            if spend > 400.0 && tenure > 36.0 then 3.0
            elif spend > 250.0 && tenure > 24.0 then 2.0
            elif spend > 150.0 && tenure > 12.0 then 1.0
            else 0.0
    |]
    (features, labels)

let generateRegressionData (rng: Random) =
    let features = [|
        for _ in 1..21 ->
            [| 50.0 + rng.NextDouble() * 200.0; rng.NextDouble() * 30.0; rng.NextDouble() * 36.0 |]
    |]
    let targets = [|
        for i in 0..20 ->
            features.[i].[0] * 12.0 + features.[i].[1] * 20.0 + features.[i].[2] * 15.0
            + rng.NextDouble() * 200.0 - 100.0
    |]
    (features, targets)

let rng = Random(seed)

// ============================================================================
// EXAMPLE 1: Zero-Config AutoML
// ============================================================================

if shouldRun "zeroconfig" then
    pr "=== Example 1: Zero-Config AutoML ==="
    pr ""

    let (features, labels) = generateChurnData rng
    pr "  Dataset: %d samples, %d features" features.Length features.[0].Length
    pr "  Churn: %d, Stay: %d"
        (labels |> Array.filter ((=) 1.0) |> Array.length)
        (labels |> Array.filter ((=) 0.0) |> Array.length)

    let result = autoML {
        trainWith features labels
        backend quantumBackend
        maxTrials cliMaxTrials
        randomSeed seed
    }

    match result with
    | Error err -> pr "  [ERROR] %A" err
    | Ok r ->
        pr "  [OK] Best: %s (%A), Score: %.2f%%, Time: %.1fs"
            r.BestModelType r.BestArchitecture (r.Score * 100.0) r.TotalSearchTime.TotalSeconds
        pr "  Trials: %d successful, %d failed" r.SuccessfulTrials r.FailedTrials

        let testGood = [| 24.0; 150.0; 1.0; 25.0; 9.0 |]
        let testRisk = [| 2.0; 60.0; 8.0; 5.0; 3.0 |]

        match AutoML.predict testGood r with
        | Ok (AutoML.BinaryPrediction p) ->
            pr "  Test (good customer): %s (conf %.1f%%)" (if p.IsPositive then "CHURN" else "STAY") (p.Confidence * 100.0)
        | _ -> ()

        match AutoML.predict testRisk r with
        | Ok (AutoML.BinaryPrediction p) ->
            pr "  Test (at-risk):       %s (conf %.1f%%)" (if p.IsPositive then "CHURN" else "STAY") (p.Confidence * 100.0)
        | _ -> ()

        record
            { Name = "zeroconfig"; Label = "Zero-Config Binary"
              BestModel = r.BestModelType; Architecture = sprintf "%A" r.BestArchitecture
              Score = r.Score; Trials = r.SuccessfulTrials
              SearchTimeSec = r.TotalSearchTime.TotalSeconds }
    pr ""

// ============================================================================
// EXAMPLE 2: Custom Search (Multi-Class)
// ============================================================================

if shouldRun "custom" then
    pr "=== Example 2: Custom Search (Multi-Class) ==="
    pr ""

    let (features, labels) = generateMultiClassData rng
    pr "  Dataset: %d samples, classes: Bronze/Silver/Gold/Platinum" features.Length

    let result = autoML {
        trainWith features labels
        backend quantumBackend
        tryBinaryClassification false
        tryMultiClass 4
        tryAnomalyDetection false
        tryRegression false
        tryArchitectures [Quantum; Hybrid]
        maxTrials cliMaxTrials
        maxTimeMinutes 10
        validationSplit 0.25
        randomSeed seed
        verbose (not quiet)
    }

    match result with
    | Error err -> pr "  [ERROR] %A" err
    | Ok r ->
        pr "  [OK] Best: %s, Score: %.2f%%" r.BestModelType (r.Score * 100.0)

        r.AllTrials
        |> Array.filter (fun t -> t.Success)
        |> Array.sortByDescending (fun t -> t.Score)
        |> Array.truncate 3
        |> Array.iteri (fun i trial ->
            let modelStr =
                match trial.ModelType with
                | AutoML.BinaryClassification -> "Binary"
                | AutoML.MultiClassClassification n -> sprintf "Multi-%d" n
                | AutoML.Regression -> "Regression"
                | AutoML.AnomalyDetection -> "Anomaly"
                | AutoML.SimilaritySearch -> "Similarity"
            pr "  #%d: %s %A  score=%.2f%%" (i+1) modelStr trial.Architecture (trial.Score * 100.0))

        let testHigh = [| 450.0; 40.0; 35.0 |]
        match AutoML.predict testHigh r with
        | Ok (AutoML.CategoryPrediction p) ->
            let seg = match p.Category with 0 -> "Bronze" | 1 -> "Silver" | 2 -> "Gold" | 3 -> "Platinum" | _ -> "?"
            pr "  Test (high-value): %s (conf %.1f%%)" seg (p.Confidence * 100.0)
        | _ -> ()

        record
            { Name = "custom"; Label = "Custom Multi-Class"
              BestModel = r.BestModelType; Architecture = sprintf "%A" r.BestArchitecture
              Score = r.Score; Trials = r.SuccessfulTrials
              SearchTimeSec = r.TotalSearchTime.TotalSeconds }
    pr ""

// ============================================================================
// EXAMPLE 3: Regression (Revenue Prediction)
// ============================================================================

if shouldRun "regression" then
    pr "=== Example 3: Regression (Revenue Prediction) ==="
    pr ""

    let (features, targets) = generateRegressionData rng
    pr "  Dataset: %d samples, target: annual revenue" features.Length

    let result = autoML {
        trainWith features targets
        backend quantumBackend
        tryBinaryClassification false
        tryAnomalyDetection false
        tryRegression true
        tryArchitectures [Hybrid; Quantum]
        maxTrials cliMaxTrials
        verbose (not quiet)
    }

    match result with
    | Error err -> pr "  [ERROR] %A" err
    | Ok r ->
        pr "  [OK] Best: %s (%A), R2: %.4f" r.BestModelType r.BestArchitecture r.Score

        let testHigh = [| 180.0; 25.0; 30.0 |]
        let testLow = [| 70.0; 10.0; 6.0 |]

        match AutoML.predict testHigh r with
        | Ok (AutoML.RegressionPrediction p) -> pr "  Test (high-value): $%.2f" p.Value
        | _ -> ()

        match AutoML.predict testLow r with
        | Ok (AutoML.RegressionPrediction p) -> pr "  Test (low-value):  $%.2f" p.Value
        | _ -> ()

        record
            { Name = "regression"; Label = "Revenue Regression"
              BestModel = r.BestModelType; Architecture = sprintf "%A" r.BestArchitecture
              Score = r.Score; Trials = r.SuccessfulTrials
              SearchTimeSec = r.TotalSearchTime.TotalSeconds }
    pr ""

// ============================================================================
// EXAMPLE 4: Compare Everything
// ============================================================================

if shouldRun "compare" then
    pr "=== Example 4: Full Comparison ==="
    pr ""

    let (features, labels) = generateChurnData (Random(seed))
    pr "  Trying all model types on churn data..."

    let result = autoML {
        trainWith features labels
        backend quantumBackend
        tryBinaryClassification true
        tryAnomalyDetection true
        tryRegression true
        tryArchitectures [Quantum; Hybrid]
        maxTrials cliMaxTrials
        verbose false
        randomSeed seed
    }

    match result with
    | Error err -> pr "  [ERROR] %A" err
    | Ok r ->
        pr "  [OK] Winner: %s (%A), Score: %.2f%%" r.BestModelType r.BestArchitecture (r.Score * 100.0)

        let successTrials = r.AllTrials |> Array.filter (fun t -> t.Success)
        let byType =
            successTrials
            |> Array.groupBy (fun t ->
                match t.ModelType with
                | AutoML.BinaryClassification -> "Binary"
                | AutoML.MultiClassClassification n -> sprintf "Multi-%d" n
                | AutoML.Regression -> "Regression"
                | AutoML.AnomalyDetection -> "Anomaly"
                | AutoML.SimilaritySearch -> "Similarity")

        byType
        |> Array.iter (fun (mtype, trials) ->
            let best = trials |> Array.map (fun t -> t.Score) |> Array.max
            pr "  %s: %d trials, best=%.2f%%" mtype trials.Length (best * 100.0))

        record
            { Name = "compare"; Label = "Full Comparison"
              BestModel = r.BestModelType; Architecture = sprintf "%A" r.BestArchitecture
              Score = r.Score; Trials = r.SuccessfulTrials
              SearchTimeSec = r.TotalSearchTime.TotalSeconds }
    pr ""

// ============================================================================
// EXAMPLE 5: Production Workflow
// ============================================================================

if shouldRun "production" then
    pr "=== Example 5: Production Workflow ==="
    pr ""

    let (features, labels) = generateChurnData (Random(seed))

    pr "  Step 1: Running AutoML search..."
    let result = autoML {
        trainWith features labels
        backend quantumBackend
        tryArchitectures [Hybrid; Quantum]
        maxTrials cliMaxTrials
        maxTimeMinutes 5
        validationSplit 0.2
        verbose false
        randomSeed seed
    }

    match result with
    | Error err -> pr "  [ERROR] %A" err
    | Ok r ->
        pr "  Step 2: Quality gate (>= 70%%)..."

        if r.Score < 0.70 then
            pr "  [WARN] Model quality insufficient (%.2f%% < 70%%)" (r.Score * 100.0)
            pr "  Recommendation: Collect more data or improve features"
        else
            pr "  [OK] Model quality: %.2f%% >= 70%%" (r.Score * 100.0)
            pr "  Step 3: Model ready for deployment"
            pr "    Model: %s (%A)" r.BestModelType r.BestArchitecture
            pr "    Trials: %d/%d successful" r.SuccessfulTrials (r.SuccessfulTrials + r.FailedTrials)

        record
            { Name = "production"; Label = "Production Workflow"
              BestModel = r.BestModelType; Architecture = sprintf "%A" r.BestArchitecture
              Score = r.Score; Trials = r.SuccessfulTrials
              SearchTimeSec = r.TotalSearchTime.TotalSeconds }
    pr ""

// --- JSON output ---

outputPath
|> Option.iter (fun path ->
    let payload =
        jsonResults
        |> List.map (fun r ->
            dict [
                "name", box r.Name
                "label", box r.Label
                "bestModel", box r.BestModel
                "architecture", box r.Architecture
                "score", box r.Score
                "trials", box r.Trials
                "searchTimeSec", box r.SearchTimeSec ])
    Reporting.writeJson path payload)

// --- CSV output ---

csvPath
|> Option.iter (fun path ->
    let header = [ "name"; "label"; "bestModel"; "architecture"; "score"; "trials"; "searchTimeSec" ]
    Reporting.writeCsv path header csvRows)

// --- Summary ---

if not quiet then
    pr ""
    pr "=== Summary ==="
    jsonResults
    |> List.iter (fun r ->
        pr "  [OK] %-25s %s (%s) score=%.2f%%" r.Label r.BestModel r.Architecture (r.Score * 100.0))
    pr ""

if not quiet && outputPath.IsNone && csvPath.IsNone && (argv |> Array.isEmpty) then
    pr "Tip: Use --output results.json or --csv results.csv to export data."
    pr "     Use --example zeroconfig to run a single example."
    pr "     Use --max-trials 5 for more thorough search."
    pr "     Run with --help for all options."
