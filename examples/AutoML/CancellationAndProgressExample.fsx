/// AutoML Example: Cancellation and Progress Reporting
/// 
/// This example demonstrates the new cancellation and progress features
/// for long-running AutoML searches.
///
/// NEW FEATURES DEMONSTRATED:
/// 1. Cancellation Tokens - Stop search gracefully
/// 2. Progress Reporters - Real-time feedback on search progress
/// 3. Event-Based Progress - Subscribe to progress events for UI integration
/// 4. Console Progress - Built-in console reporter
///
/// USE CASES:
/// - Cancel search when good-enough model found
/// - Monitor progress in long-running searches
/// - Integrate with UI frameworks (WPF, Blazor, Avalonia)
/// - Timeout protection for resource-constrained environments

#r "../../src/FSharp.Azure.Quantum/bin/Debug/net10.0/FSharp.Azure.Quantum.dll"

open System
open System.Threading
open FSharp.Azure.Quantum.Business
open FSharp.Azure.Quantum.Business.AutoML
open FSharp.Azure.Quantum.Core.Progress

// ============================================================================
// SAMPLE DATA GENERATION
// ============================================================================

let generateSampleData () =
    let random = Random(42)
    
    // Features: [tenure_months, monthly_spend, support_calls, usage_frequency, satisfaction]
    let features = [|
        for i in 1..30 ->
            [| 
                random.NextDouble() * 36.0              // Tenure: 0-36 months
                50.0 + random.NextDouble() * 150.0      // Spend: $50-$200
                float (random.Next(0, 10))              // Support calls: 0-10
                random.NextDouble() * 30.0              // Usage: 0-30 hrs/week
                random.NextDouble() * 10.0              // Satisfaction: 0-10
            |]
    |]
    
    // Labels: Will customer churn? (1 = yes, 0 = no)
    let labels = [|
        for i in 0..29 ->
            if features.[i].[1] < 100.0 && features.[i].[4] < 5.0 && features.[i].[2] > 5.0 then
                1.0
            else
                0.0
    |]
    
    (features, labels)

let (sampleFeatures, sampleLabels) = generateSampleData()

printfn "Dataset: %d samples, %d features\n" sampleFeatures.Length sampleFeatures.[0].Length

// ============================================================================
// EXAMPLE 1: Console Progress Reporter
// ============================================================================

printfn "=== Example 1: Built-in Console Progress Reporter ===\n"

// Use the built-in console progress reporter
let consoleReporter = createConsoleReporter (Some true) None

let result1 = autoML {
    trainWith sampleFeatures sampleLabels
    
    maxTrials 1
    tryArchitectures [Quantum; Hybrid]
    
    // Add console progress reporter
    progressReporter consoleReporter
    
    verbose false  // Disable verbose to see just progress events
}

match result1 with
| Ok automlResult ->
    printfn "\n✅ Search Complete!"
    printfn "Best Model: %s" automlResult.BestModelType
    printfn "Score: %.2f%%" (automlResult.Score * 100.0)
| Error err ->
    printfn "\n❌ Search Failed: %A" err

printfn "\n%s" (String.replicate 80 "=")

// ============================================================================
// EXAMPLE 2: Event-Based Progress with Cancellation
// ============================================================================

printfn "\n=== Example 2: Event-Based Progress with Manual Cancellation ===\n"

// Create cancellation token source
let cts = new CancellationTokenSource()

// Create event-based progress reporter
let eventReporter = createEventReporter()
eventReporter.SetCancellationToken(cts.Token)

// Track best score seen
let mutable bestScoreSeen = 0.0

// Subscribe to progress events
eventReporter.ProgressChanged.Add(fun event ->
    match event with
    | TrialStarted (id, total, modelType) ->
        printfn $"[{id}/{total}] Starting: {modelType}"
    
    | TrialCompleted (id, score, elapsed) ->
        printfn $"[{id}] ✓ Score: {score * 100.0:F2}%% ({elapsed:F1}s)"
        
        // Track best score
        if score > bestScoreSeen then
            bestScoreSeen <- score
            
            // Early exit if we find excellent model
            if score > 0.90 then
                printfn $"\n🎯 Excellent score found ({score * 100.0:F1}%%)! Cancelling remaining trials..."
                cts.Cancel()
    
    | TrialFailed (id, error) ->
        printfn $"[{id}] ✗ Failed: {error}"
    
    | PhaseChanged (phase, msgOpt) ->
        match msgOpt with
        | Some msg -> printfn $"\n==> {phase}: {msg}"
        | None -> printfn $"\n==> {phase}"
    
    | _ -> ())

let result2 = autoML {
    trainWith sampleFeatures sampleLabels
    
    maxTrials 1
    tryArchitectures [Quantum; Hybrid]
    
    // Add event-based progress reporter
    progressReporter (eventReporter :> IProgressReporter)
    
    // Add cancellation token
    cancellationToken cts.Token
    
    verbose false
}

match result2 with
| Ok automlResult ->
    printfn "\n✅ Search Complete (possibly early exit)!"
    printfn "Best Model: %s" automlResult.BestModelType
    printfn "Score: %.2f%%" (automlResult.Score * 100.0)
    printfn "Trials completed: %d/%d" automlResult.SuccessfulTrials (automlResult.SuccessfulTrials + automlResult.FailedTrials)
| Error err ->
    printfn "\n❌ Search Failed: %A" err

printfn "\n%s" (String.replicate 80 "=")

// ============================================================================
// EXAMPLE 3: Timeout-Based Cancellation
// ============================================================================

printfn "\n=== Example 3: Automatic Timeout Cancellation ===\n"

// Create cancellation token with 30-second timeout
let ctsTimeout = new CancellationTokenSource()
ctsTimeout.CancelAfter(TimeSpan.FromSeconds(30.0))

let timeoutReporter = createConsoleReporter (Some true) (Some ctsTimeout.Token)

printfn "Starting search with 30-second timeout...\n"

let result3 = autoML {
    trainWith sampleFeatures sampleLabels
    
    maxTrials 1  // Many trials, will likely timeout
    tryArchitectures [Quantum; Hybrid]
    
    progressReporter timeoutReporter
    cancellationToken ctsTimeout.Token
    
    verbose false
}

match result3 with
| Ok automlResult ->
    printfn "\n✅ Search Complete!"
    printfn "Best Model: %s" automlResult.BestModelType
    printfn "Score: %.2f%%" (automlResult.Score * 100.0)
    printfn "Completed trials: %d" (automlResult.SuccessfulTrials + automlResult.FailedTrials)
| Error err ->
    let errMsg = sprintf "%A" err
    if errMsg.Contains("cancelled") then
        printfn "\n⏱️  Search timeout - returning best result found so far"
    else
        printfn "\n❌ Search Failed: %A" err

printfn "\n%s" (String.replicate 80 "=")

// ============================================================================
// EXAMPLE 4: Custom Progress Handler for UI Integration
// ============================================================================

printfn "\n=== Example 4: Custom Progress Handler (UI Simulation) ===\n"

// Simulate UI progress tracking
type UIProgressTracker() =
    let mutable currentProgress = 0.0
    let mutable currentMessage = ""
    
    member _.UpdateProgress(percent: float, message: string) =
        currentProgress <- percent
        currentMessage <- message
        // In real UI, this would update progress bar
        printfn $"[UI] Progress: {percent:F0}%% - {message}"
    
    member _.GetProgress() = (currentProgress, currentMessage)

let uiTracker = UIProgressTracker()

// Create custom reporter
let customReporter = {
    new IProgressReporter with
        member _.Report(event) =
            match event with
            | TrialStarted (id, total, modelType) ->
                let percent = float id / float total * 100.0
                uiTracker.UpdateProgress(percent, $"Trial {id}/{total}: {modelType}")
            
            | TrialCompleted (id, score, _) ->
                let percent = float id / float 20 * 100.0  // Assuming 20 trials
                uiTracker.UpdateProgress(percent, $"Completed with {score * 100.0:F1}%% accuracy")
            
            | PhaseChanged (phase, _) ->
                uiTracker.UpdateProgress(0.0, $"Phase: {phase}")
            
            | ProgressUpdate (percent, msg) ->
                uiTracker.UpdateProgress(percent, msg)
            
            | _ -> ()
        
        member _.IsCancellationRequested = false
}

let result4 = autoML {
    trainWith sampleFeatures sampleLabels
    
    maxTrials 1
    tryArchitectures [Hybrid]
    
    progressReporter customReporter
    
    verbose false
}

match result4 with
| Ok automlResult ->
    let (finalPercent, finalMsg) = uiTracker.GetProgress()
    printfn "\n✅ Search Complete!"
    printfn "Final UI State: %.0f%% - %s" finalPercent finalMsg
    printfn "Best Model: %s (%.2f%%)" automlResult.BestModelType (automlResult.Score * 100.0)
| Error err ->
    printfn "\n❌ Search Failed: %A" err

printfn "\n%s" (String.replicate 80 "=")

// ============================================================================
// EXAMPLE 5: Production Pattern with Multiple Reporters
// ============================================================================

printfn "\n=== Example 5: Production Pattern - Console + Custom Logging ===\n"

// Production-ready pattern: log to both console and custom logger
let consoleLog = createConsoleReporter (Some true) None

let loggingReporter = {
    new IProgressReporter with
        member _.Report(event) =
            match event with
            | TrialCompleted (id, score, elapsed) ->
                // In production, write to log file or monitoring system
                printfn $"[LOG] Trial {id}: score={score:F4}, elapsed={elapsed:F2}s, timestamp={DateTime.UtcNow}"
            
            | TrialFailed (id, error) ->
                printfn $"[LOG] ERROR Trial {id}: {error}"
            
            | _ -> ()
        
        member _.IsCancellationRequested = false
}

// Combine multiple reporters
let multiReporter = createAggregatingReporter [consoleLog; loggingReporter]

let result5 = autoML {
    trainWith sampleFeatures sampleLabels
    
    maxTrials 1
    tryArchitectures [Quantum; Hybrid]
    
    progressReporter multiReporter
    
    verbose false
}

match result5 with
| Ok automlResult ->
    printfn "\n✅ Production Search Complete!"
    printfn "Best Model: %s" automlResult.BestModelType
    printfn "Score: %.2f%%" (automlResult.Score * 100.0)
    printfn "Logs written for all %d trials" automlResult.AllTrials.Length
| Error err ->
    printfn "\n❌ Search Failed: %A" err

// ============================================================================
// SUMMARY
// ============================================================================

printfn "\n\n=== Feature Summary ===\n"
printfn "✅ Demonstrated Features:"
printfn "  1. Console Progress Reporter - Built-in CLI feedback"
printfn "  2. Event-Based Progress - Subscribe to progress events"
printfn "  3. Manual Cancellation - Stop search gracefully"
printfn "  4. Automatic Timeout - Cancel after time limit"
printfn "  5. Custom Reporters - Integrate with UI or logging"
printfn "  6. Multiple Reporters - Combine console + custom logging"
printfn ""
printfn "💡 Use Cases:"
printfn "  - Long-running AutoML searches (20+ trials)"
printfn "  - UI integration (WPF, Blazor, Avalonia)"
printfn "  - Production monitoring and logging"
printfn "  - Resource-constrained environments with timeouts"
printfn "  - Early exit when good-enough model found"
printfn ""
printfn "📚 See CANCELLATION-AND-PROGRESS-PROPOSAL.md for full API documentation"
