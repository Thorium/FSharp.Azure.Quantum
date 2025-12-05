namespace FSharp.Azure.Quantum.Core

open System
open System.Threading

/// Progress reporting and cancellation support for long-running operations
/// 
/// This module provides:
/// - Standard progress event types for algorithms and AutoML
/// - IProgressReporter interface for custom progress handling
/// - Built-in console and event-based reporters
/// - Cancellation token integration
module Progress =
    
    // ========================================================================
    // PROGRESS EVENT TYPES
    // ========================================================================
    
    /// Progress event types for long-running operations
    type ProgressEvent =
        /// AutoML trial started
        /// Parameters: trialId, totalTrials, modelType
        | TrialStarted of trialId: int * totalTrials: int * modelType: string
        
        /// AutoML trial completed successfully
        /// Parameters: trialId, score, elapsedSeconds
        | TrialCompleted of trialId: int * score: float * elapsedSeconds: float
        
        /// AutoML trial failed
        /// Parameters: trialId, error
        | TrialFailed of trialId: int * error: string
        
        /// General progress update with percentage
        /// Parameters: percentComplete, message
        | ProgressUpdate of percentComplete: float * message: string
        
        /// Algorithm phase changed (e.g., "Period Finding", "Factor Extraction")
        /// Parameters: phaseName, optionalMessage
        | PhaseChanged of phaseName: string * message: string option
        
        /// Iteration update for iterative algorithms (Grover, QAOA optimization)
        /// Parameters: currentIteration, totalIterations, currentBest
        | IterationUpdate of currentIteration: int * totalIterations: int * currentBest: float option
        
        /// Backend execution started
        /// Parameters: backendName, numShots
        | BackendExecutionStarted of backendName: string * numShots: int
        
        /// Backend execution completed
        /// Parameters: backendName, elapsedSeconds
        | BackendExecutionCompleted of backendName: string * elapsedSeconds: float
    
    // ========================================================================
    // PROGRESS REPORTER INTERFACE
    // ========================================================================
    
    /// Interface for progress reporting
    /// 
    /// Implementations can report progress to console, UI, logs, etc.
    type IProgressReporter =
        /// Report a progress event
        abstract member Report: ProgressEvent -> unit
        
        /// Check if cancellation has been requested
        abstract member IsCancellationRequested: bool
    
    // ========================================================================
    // NULL PROGRESS REPORTER (NO-OP)
    // ========================================================================
    
    /// No-op progress reporter (does nothing)
    /// Used as default when no reporter specified
    type NullProgressReporter() =
        interface IProgressReporter with
            member _.Report(_event: ProgressEvent) = ()
            member _.IsCancellationRequested = false
    
    // ========================================================================
    // CONSOLE PROGRESS REPORTER
    // ========================================================================
    
    /// Console-based progress reporter
    /// 
    /// Prints progress events to console with formatted output.
    /// Supports verbose mode for detailed logging.
    type ConsoleProgressReporter(?verbose: bool, ?cancellationToken: CancellationToken) =
        let verbose = defaultArg verbose true
        let cancellationToken = cancellationToken
        let mutable lastPercent = 0.0
        
        interface IProgressReporter with
            member _.Report(event: ProgressEvent) =
                if verbose then
                    match event with
                    | TrialStarted (id, total, modelType) ->
                        printfn $"[Trial {id}/{total}] Starting: {modelType}"
                    
                    | TrialCompleted (id, score, elapsed) ->
                        printfn $"[Trial {id}] ✓ Score: {score * 100.0:F2}%% ({elapsed:F1}s)"
                    
                    | TrialFailed (id, error) ->
                        printfn $"[Trial {id}] ✗ Failed: {error}"
                    
                    | ProgressUpdate (percent, message) ->
                        // Only print when percent changes by at least 5%
                        if percent - lastPercent >= 5.0 || percent >= 100.0 then
                            printfn $"[{percent:F0}%%] {message}"
                            lastPercent <- percent
                    
                    | PhaseChanged (phase, messageOpt) ->
                        match messageOpt with
                        | Some msg -> printfn $"==> Phase: {phase} - {msg}"
                        | None -> printfn $"==> Phase: {phase}"
                    
                    | IterationUpdate (current, total, bestOpt) ->
                        match bestOpt with
                        | Some best -> printfn $"[Iteration {current}/{total}] Current best: {best:F6}"
                        | None -> printfn $"[Iteration {current}/{total}]"
                    
                    | BackendExecutionStarted (backend, shots) ->
                        printfn $"[Backend] Executing on {backend} ({shots} shots)..."
                    
                    | BackendExecutionCompleted (backend, elapsed) ->
                        printfn $"[Backend] Completed on {backend} ({elapsed:F1}s)"
            
            member _.IsCancellationRequested =
                match cancellationToken with
                | Some token -> token.IsCancellationRequested
                | None -> false
    
    // ========================================================================
    // EVENT-BASED PROGRESS REPORTER
    // ========================================================================
    
    /// Event-based progress reporter
    /// 
    /// Exposes a .NET event that can be subscribed to from F# or C#.
    /// Perfect for UI integration (WPF, Avalonia, Blazor, etc.)
    /// 
    /// Example:
    ///   let reporter = EventProgressReporter()
    ///   reporter.ProgressChanged.Add(fun event -> ...)
    type EventProgressReporter() =
        let progressEvent = new Event<ProgressEvent>()
        let mutable cancellationToken: CancellationToken option = None
        
        /// Subscribe to progress events
        /// 
        /// Example (F#):
        ///   reporter.ProgressChanged.Add(fun event ->
        ///       match event with
        ///       | TrialCompleted(id, score, _) -> printfn $"Trial {id}: {score}")
        /// 
        /// Example (C#):
        ///   reporter.ProgressChanged += (sender, e) => {
        ///       if (e is TrialCompleted tc)
        ///           Console.WriteLine($"Trial {tc.trialId}: {tc.score}");
        ///   };
        [<CLIEvent>]
        member _.ProgressChanged = progressEvent.Publish
        
        /// Set cancellation token for cancellation support
        member _.SetCancellationToken(token: CancellationToken) =
            cancellationToken <- Some token
        
        interface IProgressReporter with
            member _.Report(event: ProgressEvent) =
                progressEvent.Trigger(event)
            
            member _.IsCancellationRequested =
                cancellationToken 
                |> Option.map (fun t -> t.IsCancellationRequested) 
                |> Option.defaultValue false
    
    // ========================================================================
    // AGGREGATING PROGRESS REPORTER
    // ========================================================================
    
    /// Aggregating progress reporter
    /// 
    /// Forwards progress events to multiple reporters.
    /// Useful for reporting to both console and UI simultaneously.
    type AggregatingProgressReporter(reporters: IProgressReporter list) =
        
        interface IProgressReporter with
            member _.Report(event: ProgressEvent) =
                reporters |> List.iter (fun r -> r.Report(event))
            
            member _.IsCancellationRequested =
                reporters |> List.exists (fun r -> r.IsCancellationRequested)
    
    // ========================================================================
    // HELPER FUNCTIONS
    // ========================================================================
    
    /// Create a null reporter (no-op)
    let createNullReporter () : IProgressReporter =
        NullProgressReporter() :> IProgressReporter
    
    /// Create a console reporter
    let createConsoleReporter (?verbose: bool) (?cancellationToken: CancellationToken) : IProgressReporter =
        ConsoleProgressReporter(?verbose = verbose, ?cancellationToken = cancellationToken) :> IProgressReporter
    
    /// Create an event-based reporter
    let createEventReporter () : EventProgressReporter =
        EventProgressReporter()
    
    /// Create an aggregating reporter from multiple reporters
    let createAggregatingReporter (reporters: IProgressReporter list) : IProgressReporter =
        AggregatingProgressReporter(reporters) :> IProgressReporter
    
    /// Helper to check cancellation and return error if cancelled
    let checkCancellation (reporter: IProgressReporter option) (token: CancellationToken option) : Result<unit, string> =
        let tokenCancelled = token |> Option.map (fun t -> t.IsCancellationRequested) |> Option.defaultValue false
        let reporterCancelled = reporter |> Option.map (fun r -> r.IsCancellationRequested) |> Option.defaultValue false
        
        if tokenCancelled || reporterCancelled then
            Error "Operation cancelled by user"
        else
            Ok ()
