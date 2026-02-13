#!/usr/bin/env dotnet fsi
// ============================================================================
// AutoML: Cancellation & Progress Reporting
// ============================================================================
//
// Demonstrates cancellation tokens and progress reporters for long-running
// AutoML searches. Shows how to stop searches gracefully, monitor progress
// in real-time, integrate with UI frameworks, and combine multiple reporters.
//
// Examples: console, events, timeout, custom-ui, production.
// Extensible starting point for production AutoML monitoring workflows.
//
// ============================================================================

#r "../../src/FSharp.Azure.Quantum/bin/Debug/net10.0/FSharp.Azure.Quantum.dll"
#load "../_common/Cli.fs"
#load "../_common/Data.fs"
#load "../_common/Reporting.fs"

open System
open System.Threading
open FSharp.Azure.Quantum.Business
open FSharp.Azure.Quantum.Business.AutoML
open FSharp.Azure.Quantum.Core.Progress
open FSharp.Azure.Quantum.Core.BackendAbstraction
open FSharp.Azure.Quantum.Backends.LocalBackend
open FSharp.Azure.Quantum.Examples.Common

// --- Quantum Backend (Rule 1) ---
let quantumBackend = LocalBackend() :> IQuantumBackend

// --- CLI ---
let argv = fsi.CommandLineArgs |> Array.skip 1
let args = Cli.parse argv

Cli.exitIfHelp
    "CancellationAndProgressExample.fsx"
    "AutoML cancellation and progress: console, events, timeout, custom UI, production"
    [ { Name = "example"; Description = "Which example (all|console|events|timeout|custom-ui|production)"; Default = Some "all" }
      { Name = "max-trials"; Description = "Max trials per search"; Default = Some "1" }
      { Name = "timeout-sec"; Description = "Timeout in seconds for timeout example"; Default = Some "30" }
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
let cliTimeoutSec = Cli.getIntOr "timeout-sec" 30 args
let seed = Cli.getIntOr "seed" 42 args

let pr fmt = Printf.ksprintf (fun s -> if not quiet then printfn "%s" s) fmt
let shouldRun key = exampleArg = "all" || exampleArg = key

// --- Result Tracking ---

type ExampleResult =
    { Name: string
      Label: string
      BestModel: string
      Score: float
      Trials: int
      Cancelled: bool
      SearchTimeSec: float }

let mutable jsonResults : ExampleResult list = []
let mutable csvRows : string list list = []

let record (r: ExampleResult) =
    jsonResults <- jsonResults @ [ r ]
    csvRows <- csvRows @ [
        [ r.Name; r.Label; r.BestModel
          sprintf "%.4f" r.Score; string r.Trials; string r.Cancelled
          sprintf "%.1f" r.SearchTimeSec ] ]

// --- Sample Data ---

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

let (sampleFeatures, sampleLabels) = generateChurnData (Random(seed))

pr "Dataset: %d samples, %d features" sampleFeatures.Length sampleFeatures.[0].Length
pr ""

// --- UIProgressTracker type (must be at module scope) ---

type UIProgressTracker() =
    let mutable currentProgress = 0.0
    let mutable currentMessage = ""

    member _.UpdateProgress(percent: float, message: string) =
        currentProgress <- percent
        currentMessage <- message

    member _.GetProgress() = (currentProgress, currentMessage)

// ============================================================================
// EXAMPLE 1: Console Progress Reporter
// ============================================================================

if shouldRun "console" then
    pr "=== Example 1: Built-in Console Progress Reporter ==="
    pr ""

    let consoleReporter = createConsoleReporter (Some true) None

    let result = autoML {
        trainWith sampleFeatures sampleLabels
        backend quantumBackend
        maxTrials cliMaxTrials
        tryArchitectures [Quantum; Hybrid]
        progressReporter consoleReporter
        verbose false
        randomSeed seed
    }

    match result with
    | Error err -> pr "  [ERROR] %A" err
    | Ok r ->
        pr "  [OK] Best: %s, Score: %.2f%%, Time: %.1fs"
            r.BestModelType (r.Score * 100.0) r.TotalSearchTime.TotalSeconds
        pr "  Trials: %d successful, %d failed" r.SuccessfulTrials r.FailedTrials

        record
            { Name = "console"; Label = "Console Reporter"
              BestModel = r.BestModelType; Score = r.Score
              Trials = r.SuccessfulTrials; Cancelled = false
              SearchTimeSec = r.TotalSearchTime.TotalSeconds }
    pr ""

// ============================================================================
// EXAMPLE 2: Event-Based Progress with Cancellation
// ============================================================================

if shouldRun "events" then
    pr "=== Example 2: Event-Based Progress with Cancellation ==="
    pr ""

    let cts = new CancellationTokenSource()
    let eventReporter = createEventReporter()
    eventReporter.SetCancellationToken(cts.Token)

    let mutable bestScoreSeen = 0.0

    eventReporter.ProgressChanged.Add(fun event ->
        match event with
        | TrialStarted (id, total, modelType) ->
            pr "  [%d/%d] Starting: %s" id total modelType

        | TrialCompleted (id, score, elapsed) ->
            pr "  [%d] OK Score: %.2f%% (%.1fs)" id (score * 100.0) elapsed
            if score > bestScoreSeen then
                bestScoreSeen <- score
                if score > 0.90 then
                    pr "  Excellent score (%.1f%%)! Cancelling remaining trials..." (score * 100.0)
                    cts.Cancel()

        | TrialFailed (id, error) ->
            pr "  [%d] FAILED: %s" id error

        | PhaseChanged (phase, msgOpt) ->
            match msgOpt with
            | Some msg -> pr "  ==> %s: %s" phase msg
            | None -> pr "  ==> %s" phase

        | _ -> ())

    let result = autoML {
        trainWith sampleFeatures sampleLabels
        backend quantumBackend
        maxTrials cliMaxTrials
        tryArchitectures [Quantum; Hybrid]
        progressReporter (eventReporter :> IProgressReporter)
        cancellationToken cts.Token
        verbose false
        randomSeed seed
    }

    let wasCancelled = cts.IsCancellationRequested

    match result with
    | Error err -> pr "  [ERROR] %A" err
    | Ok r ->
        pr "  [OK] Best: %s, Score: %.2f%%" r.BestModelType (r.Score * 100.0)
        pr "  Trials: %d/%d completed%s"
            r.SuccessfulTrials (r.SuccessfulTrials + r.FailedTrials)
            (if wasCancelled then " (early exit)" else "")

        record
            { Name = "events"; Label = "Event-Based + Cancel"
              BestModel = r.BestModelType; Score = r.Score
              Trials = r.SuccessfulTrials; Cancelled = wasCancelled
              SearchTimeSec = r.TotalSearchTime.TotalSeconds }

    cts.Dispose()
    pr ""

// ============================================================================
// EXAMPLE 3: Timeout-Based Cancellation
// ============================================================================

if shouldRun "timeout" then
    pr "=== Example 3: Timeout Cancellation (%d seconds) ===" cliTimeoutSec
    pr ""

    let ctsTimeout = new CancellationTokenSource()
    ctsTimeout.CancelAfter(TimeSpan.FromSeconds(float cliTimeoutSec))

    let timeoutReporter = createConsoleReporter (Some (not quiet)) (Some ctsTimeout.Token)

    pr "  Starting search with %d-second timeout..." cliTimeoutSec

    let result = autoML {
        trainWith sampleFeatures sampleLabels
        backend quantumBackend
        maxTrials cliMaxTrials
        tryArchitectures [Quantum; Hybrid]
        progressReporter timeoutReporter
        cancellationToken ctsTimeout.Token
        verbose false
        randomSeed seed
    }

    let timedOut = ctsTimeout.IsCancellationRequested

    match result with
    | Error err ->
        let errMsg = sprintf "%A" err
        if errMsg.Contains("cancelled") || errMsg.Contains("Cancellation") then
            pr "  [TIMEOUT] Search timed out - returning best result found"
        else
            pr "  [ERROR] %A" err
    | Ok r ->
        pr "  [OK] Best: %s, Score: %.2f%%" r.BestModelType (r.Score * 100.0)
        pr "  Completed: %d trials%s"
            (r.SuccessfulTrials + r.FailedTrials)
            (if timedOut then " (timed out)" else "")

        record
            { Name = "timeout"; Label = "Timeout Cancellation"
              BestModel = r.BestModelType; Score = r.Score
              Trials = r.SuccessfulTrials; Cancelled = timedOut
              SearchTimeSec = r.TotalSearchTime.TotalSeconds }

    ctsTimeout.Dispose()
    pr ""

// ============================================================================
// EXAMPLE 4: Custom Progress Handler (UI Simulation)
// ============================================================================

if shouldRun "custom-ui" then
    pr "=== Example 4: Custom Progress Handler (UI Simulation) ==="
    pr ""

    let uiTracker = UIProgressTracker()

    let customReporter = {
        new IProgressReporter with
            member _.Report(event) =
                match event with
                | TrialStarted (id, total, modelType) ->
                    let percent = float id / float (max total 1) * 100.0
                    uiTracker.UpdateProgress(percent, sprintf "Trial %d/%d: %s" id total modelType)
                    pr "  [UI] Progress: %.0f%% - Trial %d/%d: %s" percent id total modelType

                | TrialCompleted (id, score, _) ->
                    let percent = float id / float cliMaxTrials * 100.0
                    uiTracker.UpdateProgress(percent, sprintf "Completed with %.1f%% accuracy" (score * 100.0))
                    pr "  [UI] Progress: %.0f%% - Score: %.1f%%" percent (score * 100.0)

                | PhaseChanged (phase, _) ->
                    uiTracker.UpdateProgress(0.0, sprintf "Phase: %s" phase)
                    pr "  [UI] Phase: %s" phase

                | ProgressUpdate (percent, msg) ->
                    uiTracker.UpdateProgress(percent, msg)
                    pr "  [UI] Progress: %.0f%% - %s" percent msg

                | _ -> ()

            member _.IsCancellationRequested = false
    }

    let result = autoML {
        trainWith sampleFeatures sampleLabels
        backend quantumBackend
        maxTrials cliMaxTrials
        tryArchitectures [Hybrid]
        progressReporter customReporter
        verbose false
        randomSeed seed
    }

    match result with
    | Error err -> pr "  [ERROR] %A" err
    | Ok r ->
        let (finalPct, finalMsg) = uiTracker.GetProgress()
        pr "  [OK] Final UI state: %.0f%% - %s" finalPct finalMsg
        pr "  Best: %s, Score: %.2f%%" r.BestModelType (r.Score * 100.0)

        record
            { Name = "custom-ui"; Label = "Custom UI Reporter"
              BestModel = r.BestModelType; Score = r.Score
              Trials = r.SuccessfulTrials; Cancelled = false
              SearchTimeSec = r.TotalSearchTime.TotalSeconds }
    pr ""

// ============================================================================
// EXAMPLE 5: Production Pattern with Multiple Reporters
// ============================================================================

if shouldRun "production" then
    pr "=== Example 5: Production (Console + Logging) ==="
    pr ""

    let consoleLog = createConsoleReporter (Some (not quiet)) None

    let mutable logEntries : string list = []

    let loggingReporter = {
        new IProgressReporter with
            member _.Report(event) =
                match event with
                | TrialCompleted (id, score, elapsed) ->
                    let entry = sprintf "[LOG] Trial %d: score=%.4f, elapsed=%.2fs, ts=%s" id score elapsed (DateTime.UtcNow.ToString("o"))
                    logEntries <- logEntries @ [ entry ]
                    pr "  %s" entry

                | TrialFailed (id, error) ->
                    let entry = sprintf "[LOG] ERROR Trial %d: %s" id error
                    logEntries <- logEntries @ [ entry ]
                    pr "  %s" entry

                | _ -> ()

            member _.IsCancellationRequested = false
    }

    let multiReporter = createAggregatingReporter [consoleLog; loggingReporter]

    let result = autoML {
        trainWith sampleFeatures sampleLabels
        backend quantumBackend
        maxTrials cliMaxTrials
        tryArchitectures [Quantum; Hybrid]
        progressReporter multiReporter
        verbose false
        randomSeed seed
    }

    match result with
    | Error err -> pr "  [ERROR] %A" err
    | Ok r ->
        pr "  [OK] Best: %s, Score: %.2f%%" r.BestModelType (r.Score * 100.0)
        pr "  Log entries: %d" logEntries.Length

        record
            { Name = "production"; Label = "Production Multi-Reporter"
              BestModel = r.BestModelType; Score = r.Score
              Trials = r.SuccessfulTrials; Cancelled = false
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
                "score", box r.Score
                "trials", box r.Trials
                "cancelled", box r.Cancelled
                "searchTimeSec", box r.SearchTimeSec ])
    Reporting.writeJson path payload)

// --- CSV output ---

csvPath
|> Option.iter (fun path ->
    let header = [ "name"; "label"; "bestModel"; "score"; "trials"; "cancelled"; "searchTimeSec" ]
    Reporting.writeCsv path header csvRows)

// --- Summary ---

if not quiet then
    pr ""
    pr "=== Summary ==="
    jsonResults
    |> List.iter (fun r ->
        pr "  [OK] %-25s %s score=%.2f%%%s"
            r.Label r.BestModel (r.Score * 100.0)
            (if r.Cancelled then " (cancelled)" else ""))
    pr ""
    pr "Features demonstrated:"
    pr "  - Console progress reporter (built-in CLI feedback)"
    pr "  - Event-based progress (subscribe to ProgressChanged events)"
    pr "  - Cancellation tokens (graceful search termination)"
    pr "  - Timeout cancellation (resource-constrained environments)"
    pr "  - Custom reporters (UI/logging integration)"
    pr "  - Aggregating reporters (combine multiple reporters)"
    pr ""

if not quiet && outputPath.IsNone && csvPath.IsNone && (argv |> Array.isEmpty) then
    pr "Tip: Use --output results.json or --csv results.csv to export data."
    pr "     Use --example console to run a single example."
    pr "     Use --timeout-sec 10 for shorter timeout."
    pr "     Run with --help for all options."
