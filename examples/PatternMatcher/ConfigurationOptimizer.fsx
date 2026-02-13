// ==============================================================================
// Quantum Pattern Matcher Examples - FSharp.Azure.Quantum
// ==============================================================================
// Demonstrates the Quantum Pattern Matcher API using Grover's algorithm to
// find items matching a pattern in large search spaces:
//
// 1. System Configuration Optimization (database tuning)
// 2. Machine Learning Hyperparameter Tuning
// 3. Feature Selection for ML Models
//
// Usage:
//   dotnet fsi ConfigurationOptimizer.fsx
//   dotnet fsi ConfigurationOptimizer.fsx -- --example db
//   dotnet fsi ConfigurationOptimizer.fsx -- --quiet --output results.json --csv results.csv
//   dotnet fsi ConfigurationOptimizer.fsx -- --help
// ==============================================================================

#r "../../src/FSharp.Azure.Quantum/bin/Debug/net10.0/FSharp.Azure.Quantum.dll"
#load "../_common/Cli.fs"
#load "../_common/Data.fs"
#load "../_common/Reporting.fs"
open FSharp.Azure.Quantum.Examples.Common

open System
open FSharp.Azure.Quantum
open FSharp.Azure.Quantum.QuantumPatternMatcher
open FSharp.Azure.Quantum.Core.BackendAbstraction
open FSharp.Azure.Quantum.Backends.LocalBackend

// --- CLI ---
let argv = fsi.CommandLineArgs |> Array.skip 1
let args = Cli.parse argv
Cli.exitIfHelp "ConfigurationOptimizer.fsx" "Quantum pattern matching for configuration search" [
    { Name = "example"; Description = "Which example: all, db, ml, feature"; Default = Some "all" }
    { Name = "shots"; Description = "Measurement shots per search"; Default = Some "1000" }
    { Name = "output"; Description = "Write results to JSON file"; Default = None }
    { Name = "csv"; Description = "Write results to CSV file"; Default = None }
    { Name = "quiet"; Description = "Suppress printed output"; Default = None }
] args

let exampleChoice = Cli.getOr "example" "all" args
let cliShots = Cli.getIntOr "shots" 1000 args
let quiet = Cli.hasFlag "quiet" args
let outputPath = Cli.tryGet "output" args
let csvPath = Cli.tryGet "csv" args

let pr fmt = Printf.ksprintf (fun s -> if not quiet then printfn "%s" s) fmt

// ==============================================================================
// Backend (Rule 1: explicit IQuantumBackend)
// ==============================================================================

let quantumBackend = LocalBackend() :> IQuantumBackend

pr "=== Quantum Pattern Matcher: Configuration Optimizer ==="
pr "Backend: %s" quantumBackend.Name
pr ""

// Accumulators for output
let mutable jsonResults: (string * obj) list = []
let mutable csvRows: string list list = []

// ==============================================================================
// EXAMPLE 1: Database Configuration Optimization
// ==============================================================================

type DbConfig = {
    CacheSize: int
    PoolSize: int
    QueryTimeout: int
    LogLevel: string
}

let decodeDbConfig (index: int) : DbConfig =
    let cacheSizes = [| 64; 128; 256; 512 |]
    let poolSizes = [| 10; 50; 100; 200 |]
    let timeouts = [| 5; 10; 30; 60 |]
    let logLevels = [| "Debug"; "Info"; "Warn"; "Error" |]
    { CacheSize = cacheSizes.[(index >>> 6) &&& 0b11]
      PoolSize = poolSizes.[(index >>> 4) &&& 0b11]
      QueryTimeout = timeouts.[(index >>> 2) &&& 0b11]
      LogLevel = logLevels.[index &&& 0b11] }

let benchmarkDatabase (config: DbConfig) =
    let cacheScore = match config.CacheSize with | 512 -> 100.0 | 256 -> 80.0 | 128 -> 55.0 | _ -> 30.0
    let poolScore = match config.PoolSize with | 100 -> 95.0 | 200 -> 85.0 | 50 -> 60.0 | _ -> 25.0
    let timeoutScore = match config.QueryTimeout with | 30 -> 100.0 | 60 -> 80.0 | 10 -> 50.0 | _ -> 20.0
    // Throughput: queries/sec (weighted combination of cache + pool + timeout)
    let throughput = cacheScore * 50.0 + poolScore * 40.0 + timeoutScore * 10.0
    // Latency: ms response time (lower is better, 20-100 range)
    let latency = 100.0 - cacheScore * 0.4 - poolScore * 0.3 - timeoutScore * 0.1
    // CPU: % utilization (lower is better)
    let cpuUsage = 40.0 + (if config.LogLevel = "Debug" then 25.0 else 0.0)
                        + (if config.PoolSize >= 200 then 10.0 else 0.0)
                        + (if config.CacheSize <= 64 then 15.0 else 0.0)
    (throughput, latency, cpuUsage)

let isGoodConfig (index: int) : bool =
    let config = decodeDbConfig index
    let (throughput, latency, cpuUsage) = benchmarkDatabase config
    throughput >= 9500.0 && latency < 25.0 && cpuUsage <= 40.0 && config.LogLevel <> "Debug"

if exampleChoice = "all" || exampleChoice = "db" then
    pr "--- Example 1: Database Configuration ---"
    pr "Search Space: 256 configs (4 params x 4 values)"
    pr "  Cache Size:    64/128/256/512 MB"
    pr "  Pool Size:     10/50/100/200 connections"
    pr "  Query Timeout: 5/10/30/60 seconds"
    pr "  Log Level:     Debug/Info/Warn/Error"
    pr ""

    let dbProblem = patternMatcher {
        searchSpaceSize 256
        matchPattern isGoodConfig
        findTop 5
        backend quantumBackend
        shots cliShots
    }

    match solve dbProblem with
    | Ok result ->
        pr "Found %d matching configurations" result.Matches.Length
        pr ""
        result.Matches
        |> List.iteri (fun i index ->
            let config = decodeDbConfig index
            let (throughput, latency, cpuUsage) = benchmarkDatabase config
            pr "  #%d - Config %d: Cache=%dMB Pool=%d Timeout=%ds Log=%s => TP=%.0f q/s Lat=%.1fms CPU=%.0f%%"
                (i + 1) index config.CacheSize config.PoolSize config.QueryTimeout config.LogLevel throughput latency cpuUsage
            csvRows <- [
                "db"; string index; sprintf "%d" config.CacheSize; sprintf "%d" config.PoolSize
                config.LogLevel; sprintf "%.0f" throughput; sprintf "%.1f" latency; sprintf "%.1f" cpuUsage
            ] :: csvRows
        )
        pr "  Quantum advantage: sqrt(256) = 16x fewer evaluations"
        pr ""
        jsonResults <- ("db", box {| matches = result.Matches.Length; searchSpace = 256; qubits = result.QubitsRequired; iterations = result.IterationsUsed |}) :: jsonResults
    | Error err ->
        pr "Error: %s" err.Message

if exampleChoice = "all" || exampleChoice = "ml" then
    // ==============================================================================
    // EXAMPLE 2: ML Hyperparameter Tuning
    // ==============================================================================

    pr "--- Example 2: ML Hyperparameter Tuning ---"
    pr "Search Space: 128 combinations (LR x Batch x Layers x Dropout)"
    pr ""

    let decodeMLConfig (index: int) =
        let learningRates = [| 0.001; 0.01; 0.1; 1.0 |]
        let batchSizes = [| 16; 32; 64; 128 |]
        let layerCounts = [| 1; 2; 3; 4 |]
        {| LearningRate = learningRates.[(index >>> 5) &&& 0b11]
           BatchSize = batchSizes.[(index >>> 3) &&& 0b11]
           Layers = layerCounts.[(index >>> 1) &&& 0b11]
           DropoutRate = if (index &&& 0b1) = 1 then 0.5 else 0.0 |}

    let trainModel (index: int) : float =
        let config = decodeMLConfig index
        let lrScore =
            match config.LearningRate with
            | 0.01 | 0.1 -> 95.0
            | 0.001 -> 85.0
            | _ -> 60.0
        let batchScore =
            match config.BatchSize with
            | 32 | 64 -> 95.0
            | 16 -> 88.0
            | _ -> 75.0
        let layerScore =
            match config.Layers with
            | 2 | 3 -> 95.0
            | 1 -> 82.0
            | _ -> 70.0
        let dropoutBonus = if config.DropoutRate > 0.0 then 5.0 else 0.0
        (lrScore + batchScore + layerScore) / 3.0 + dropoutBonus

    let isGoodModel (index: int) : bool = trainModel index > 95.0

    let mlProblem = patternMatcher {
        searchSpaceSize 128
        matchPattern isGoodModel
        findTop 3
        backend quantumBackend
        shots cliShots
    }

    match solve mlProblem with
    | Ok result ->
        pr "Found %d high-accuracy configurations" result.Matches.Length
        pr ""
        result.Matches
        |> List.iteri (fun i index ->
            let config = decodeMLConfig index
            let accuracy = trainModel index
            pr "  #%d - Config %d: LR=%.3f Batch=%d Layers=%d Dropout=%.1f => Acc=%.2f%%"
                (i + 1) index config.LearningRate config.BatchSize config.Layers config.DropoutRate accuracy
            csvRows <- [
                "ml"; string index; sprintf "%.3f" config.LearningRate; sprintf "%d" config.BatchSize
                sprintf "%d" config.Layers; sprintf "%.1f" config.DropoutRate; sprintf "%.2f" accuracy; ""
            ] :: csvRows
        )
        pr "  Quantum advantage: sqrt(128) ~ 11x fewer training runs"
        pr ""
        jsonResults <- ("ml", box {| matches = result.Matches.Length; searchSpace = 128; qubits = result.QubitsRequired; iterations = result.IterationsUsed |}) :: jsonResults
    | Error err ->
        pr "Error: %s" err.Message

if exampleChoice = "all" || exampleChoice = "feature" then
    // ==============================================================================
    // EXAMPLE 3: Feature Selection for ML
    // ==============================================================================

    pr "--- Example 3: Feature Selection ---"

    let features = [| "Age"; "Income"; "CreditScore"; "Education"; "Employment"; "LoanHistory"; "Assets"; "Debt" |]
    pr "Feature Pool: %d features (%s)" features.Length (String.concat ", " features)
    pr "Goal: Select features achieving >93%% accuracy with 3-5 features"
    pr ""

    let decodeFeatureSet (index: int) : string list =
        features
        |> Array.mapi (fun i f -> if (index &&& (1 <<< i)) <> 0 then Some f else None)
        |> Array.choose id
        |> Array.toList

    let evaluateFeatureSet (featureSet: string list) : (float * int) =
        let hasIncome = featureSet |> List.contains "Income"
        let hasCreditScore = featureSet |> List.contains "CreditScore"
        let hasLoanHistory = featureSet |> List.contains "LoanHistory"
        let hasAssets = featureSet |> List.contains "Assets"
        let baseAccuracy =
            match (hasIncome, hasCreditScore, hasLoanHistory) with
            | (true, true, true) -> 94.0
            | (true, true, false) -> 89.0
            | (true, false, true) -> 85.0
            | _ -> 72.0
        let bonus = if hasAssets && hasCreditScore then 2.0 else 0.0
        let featureCount = featureSet.Length
        let penalty = if featureCount > 4 then float (featureCount - 4) * 3.0 else 0.0
        (baseAccuracy + bonus - penalty, featureCount)

    let isGoodFeatureSet (index: int) : bool =
        let featureSet = decodeFeatureSet index
        let (accuracy, count) = evaluateFeatureSet featureSet
        accuracy > 93.0 && count >= 3 && count <= 5

    let featureProblem = patternMatcher {
        searchSpaceSize 256
        matchPattern isGoodFeatureSet
        findTop 5
        backend quantumBackend
        shots cliShots
    }

    match solve featureProblem with
    | Ok result ->
        pr "Found %d optimal feature subsets" result.Matches.Length
        pr ""
        result.Matches
        |> List.iteri (fun i index ->
            let featureSet = decodeFeatureSet index
            let (accuracy, count) = evaluateFeatureSet featureSet
            pr "  #%d - Subset %d: [%s] (%d features, %.2f%% acc)"
                (i + 1) index (String.concat ", " featureSet) count accuracy
            csvRows <- [
                "feature"; string index; String.concat "|" featureSet; sprintf "%d" count
                sprintf "%.2f" accuracy; ""; ""; ""
            ] :: csvRows
        )
        pr "  Quantum advantage: sqrt(256) = 16x fewer model trainings"
        pr ""
        jsonResults <- ("feature", box {| matches = result.Matches.Length; searchSpace = 256; qubits = result.QubitsRequired; iterations = result.IterationsUsed |}) :: jsonResults
    | Error err ->
        pr "Error: %s" err.Message

// ==============================================================================
// Output
// ==============================================================================

outputPath |> Option.iter (fun path ->
    let payload =
        {| backend = quantumBackend.Name
           shotsPerSearch = cliShots
           examples = jsonResults |> List.rev |> List.map (fun (name, data) -> {| example = name; results = data |}) |}
    Reporting.writeJson path payload
    pr "JSON written to %s" path
)

csvPath |> Option.iter (fun path ->
    let header = ["Example"; "Index"; "Param1"; "Param2"; "Param3"; "Metric1"; "Metric2"; "Metric3"]
    Reporting.writeCsv path header (csvRows |> List.rev)
    pr "CSV written to %s" path
)

if not quiet && outputPath.IsNone && csvPath.IsNone then
    pr "---"
    pr "Tip: Use --example db|ml|feature to run a single example."
    pr "     Use --shots N to change measurement count (default 1000)."
    pr "     Use --output results.json or --csv results.csv to export."
    pr "     Use --help for all options."
