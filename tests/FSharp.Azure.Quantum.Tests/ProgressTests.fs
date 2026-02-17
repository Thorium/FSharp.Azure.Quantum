namespace FSharp.Azure.Quantum.Tests

open System.Threading
open Xunit
open FSharp.Azure.Quantum.Core
open FSharp.Azure.Quantum.Core.Progress

module ProgressTests =

    // ========================================================================
    // PROGRESS EVENT CONSTRUCTION
    // ========================================================================

    [<Fact>]
    let ``ProgressEvent TrialStarted stores values`` () =
        let event = TrialStarted(1, 10, "VQC")
        match event with
        | TrialStarted (id, total, model) ->
            Assert.Equal(1, id)
            Assert.Equal(10, total)
            Assert.Equal("VQC", model)
        | _ -> failwith "Unexpected event type"

    [<Fact>]
    let ``ProgressEvent TrialCompleted stores values`` () =
        let event = TrialCompleted(3, 0.95, 2.5)
        match event with
        | TrialCompleted (id, score, elapsed) ->
            Assert.Equal(3, id)
            Assert.True(abs(score - 0.95) < 1e-10)
            Assert.True(abs(elapsed - 2.5) < 1e-10)
        | _ -> failwith "Unexpected event type"

    [<Fact>]
    let ``ProgressEvent TrialFailed stores values`` () =
        let event = TrialFailed(2, "convergence failure")
        match event with
        | TrialFailed (id, error) ->
            Assert.Equal(2, id)
            Assert.Equal("convergence failure", error)
        | _ -> failwith "Unexpected event type"

    [<Fact>]
    let ``ProgressEvent PhaseChanged with message`` () =
        let event = PhaseChanged("Period Finding", Some "step 3")
        match event with
        | PhaseChanged (name, msg) ->
            Assert.Equal("Period Finding", name)
            Assert.Equal(Some "step 3", msg)
        | _ -> failwith "Unexpected event type"

    [<Fact>]
    let ``ProgressEvent PhaseChanged without message`` () =
        let event = PhaseChanged("Factor Extraction", None)
        match event with
        | PhaseChanged (name, msg) ->
            Assert.Equal("Factor Extraction", name)
            Assert.Equal(None, msg)
        | _ -> failwith "Unexpected event type"

    [<Fact>]
    let ``ProgressEvent IterationUpdate with best value`` () =
        let event = IterationUpdate(5, 100, Some 0.42)
        match event with
        | IterationUpdate (current, total, best) ->
            Assert.Equal(5, current)
            Assert.Equal(100, total)
            Assert.Equal(Some 0.42, best)
        | _ -> failwith "Unexpected event type"

    [<Fact>]
    let ``ProgressEvent IterationUpdate without best value`` () =
        let event = IterationUpdate(1, 50, None)
        match event with
        | IterationUpdate (current, total, best) ->
            Assert.Equal(1, current)
            Assert.Equal(50, total)
            Assert.Equal(None, best)
        | _ -> failwith "Unexpected event type"

    [<Fact>]
    let ``ProgressEvent BackendExecutionStarted stores values`` () =
        let event = BackendExecutionStarted("ionq.simulator", 1000)
        match event with
        | BackendExecutionStarted (name, shots) ->
            Assert.Equal("ionq.simulator", name)
            Assert.Equal(1000, shots)
        | _ -> failwith "Unexpected event type"

    [<Fact>]
    let ``ProgressEvent BackendExecutionCompleted stores values`` () =
        let event = BackendExecutionCompleted("rigetti.qvm", 3.7)
        match event with
        | BackendExecutionCompleted (name, elapsed) ->
            Assert.Equal("rigetti.qvm", name)
            Assert.True(abs(elapsed - 3.7) < 1e-10)
        | _ -> failwith "Unexpected event type"

    // ========================================================================
    // NULL PROGRESS REPORTER
    // ========================================================================

    [<Fact>]
    let ``NullProgressReporter Report does nothing`` () =
        let reporter = NullProgressReporter() :> IProgressReporter
        // Should not throw
        reporter.Report(TrialStarted(1, 1, "test"))
        reporter.Report(ProgressUpdate(50.0, "halfway"))
        reporter.Report(BackendExecutionCompleted("local", 1.0))

    [<Fact>]
    let ``NullProgressReporter IsCancellationRequested is always false`` () =
        let reporter = NullProgressReporter() :> IProgressReporter
        Assert.False(reporter.IsCancellationRequested)

    [<Fact>]
    let ``createNullReporter returns IProgressReporter`` () =
        let reporter = createNullReporter()
        Assert.False(reporter.IsCancellationRequested)
        reporter.Report(ProgressUpdate(100.0, "done"))

    // ========================================================================
    // EVENT PROGRESS REPORTER
    // ========================================================================

    [<Fact>]
    let ``EventProgressReporter fires ProgressChanged event`` () =
        let reporter = createEventReporter()
        let mutable received: ProgressEvent list = []
        reporter.ProgressChanged.Add(fun e -> received <- e :: received)

        let iface = reporter :> IProgressReporter
        iface.Report(TrialStarted(1, 5, "SVM"))
        iface.Report(TrialCompleted(1, 0.88, 1.2))

        Assert.Equal(2, received.Length)
        // Events are prepended, so last fired is first in list
        match received.[0] with
        | TrialCompleted (1, _, _) -> ()
        | _ -> failwith "Expected TrialCompleted"
        match received.[1] with
        | TrialStarted (1, 5, "SVM") -> ()
        | _ -> failwith "Expected TrialStarted"

    [<Fact>]
    let ``EventProgressReporter IsCancellationRequested defaults to false`` () =
        let reporter = createEventReporter()
        let iface = reporter :> IProgressReporter
        Assert.False(iface.IsCancellationRequested)

    [<Fact>]
    let ``EventProgressReporter IsCancellationRequested reflects token`` () =
        let reporter = createEventReporter()
        let cts = new CancellationTokenSource()
        reporter.SetCancellationToken(cts.Token)

        let iface = reporter :> IProgressReporter
        Assert.False(iface.IsCancellationRequested)

        cts.Cancel()
        Assert.True(iface.IsCancellationRequested)

    [<Fact>]
    let ``EventProgressReporter multiple subscribers all receive events`` () =
        let reporter = createEventReporter()
        let mutable count1 = 0
        let mutable count2 = 0
        reporter.ProgressChanged.Add(fun _ -> count1 <- count1 + 1)
        reporter.ProgressChanged.Add(fun _ -> count2 <- count2 + 1)

        let iface = reporter :> IProgressReporter
        iface.Report(ProgressUpdate(50.0, "halfway"))

        Assert.Equal(1, count1)
        Assert.Equal(1, count2)

    // ========================================================================
    // CONSOLE PROGRESS REPORTER
    // ========================================================================

    [<Fact>]
    let ``ConsoleProgressReporter IsCancellationRequested without token is false`` () =
        let reporter = ConsoleProgressReporter(verbose = false) :> IProgressReporter
        Assert.False(reporter.IsCancellationRequested)

    [<Fact>]
    let ``ConsoleProgressReporter IsCancellationRequested reflects token`` () =
        let cts = new CancellationTokenSource()
        let reporter = ConsoleProgressReporter(verbose = false, cancellationToken = cts.Token) :> IProgressReporter
        Assert.False(reporter.IsCancellationRequested)
        cts.Cancel()
        Assert.True(reporter.IsCancellationRequested)

    [<Fact>]
    let ``ConsoleProgressReporter Report does not throw in non-verbose mode`` () =
        let reporter = ConsoleProgressReporter(verbose = false) :> IProgressReporter
        // In non-verbose mode, Report should be a no-op
        reporter.Report(TrialStarted(1, 1, "test"))
        reporter.Report(TrialCompleted(1, 0.5, 1.0))
        reporter.Report(TrialFailed(1, "err"))
        reporter.Report(ProgressUpdate(50.0, "msg"))
        reporter.Report(PhaseChanged("phase", None))
        reporter.Report(PhaseChanged("phase", Some "detail"))
        reporter.Report(IterationUpdate(1, 10, None))
        reporter.Report(IterationUpdate(1, 10, Some 0.5))
        reporter.Report(BackendExecutionStarted("local", 100))
        reporter.Report(BackendExecutionCompleted("local", 1.0))

    [<Fact>]
    let ``createConsoleReporter with no args works`` () =
        let reporter = createConsoleReporter None None
        Assert.False(reporter.IsCancellationRequested)

    [<Fact>]
    let ``createConsoleReporter with cancellation token`` () =
        let cts = new CancellationTokenSource()
        let reporter = createConsoleReporter (Some false) (Some cts.Token)
        Assert.False(reporter.IsCancellationRequested)
        cts.Cancel()
        Assert.True(reporter.IsCancellationRequested)

    // ========================================================================
    // AGGREGATING PROGRESS REPORTER
    // ========================================================================

    [<Fact>]
    let ``AggregatingProgressReporter forwards to all reporters`` () =
        let mutable count1 = 0
        let mutable count2 = 0

        let reporter1 =
            { new IProgressReporter with
                member _.Report(_) = count1 <- count1 + 1
                member _.IsCancellationRequested = false }

        let reporter2 =
            { new IProgressReporter with
                member _.Report(_) = count2 <- count2 + 1
                member _.IsCancellationRequested = false }

        let agg = createAggregatingReporter [reporter1; reporter2]
        agg.Report(TrialStarted(1, 1, "test"))
        agg.Report(ProgressUpdate(100.0, "done"))

        Assert.Equal(2, count1)
        Assert.Equal(2, count2)

    [<Fact>]
    let ``AggregatingProgressReporter IsCancellationRequested if any reporter cancelled`` () =
        let reporter1 =
            { new IProgressReporter with
                member _.Report(_) = ()
                member _.IsCancellationRequested = false }

        let reporter2 =
            { new IProgressReporter with
                member _.Report(_) = ()
                member _.IsCancellationRequested = true }

        let agg = createAggregatingReporter [reporter1; reporter2]
        Assert.True(agg.IsCancellationRequested)

    [<Fact>]
    let ``AggregatingProgressReporter IsCancellationRequested false when none cancelled`` () =
        let reporter1 =
            { new IProgressReporter with
                member _.Report(_) = ()
                member _.IsCancellationRequested = false }

        let reporter2 =
            { new IProgressReporter with
                member _.Report(_) = ()
                member _.IsCancellationRequested = false }

        let agg = createAggregatingReporter [reporter1; reporter2]
        Assert.False(agg.IsCancellationRequested)

    [<Fact>]
    let ``AggregatingProgressReporter with empty list does not throw`` () =
        let agg = createAggregatingReporter []
        agg.Report(ProgressUpdate(50.0, "test"))
        Assert.False(agg.IsCancellationRequested)

    // ========================================================================
    // CHECK CANCELLATION HELPER
    // ========================================================================

    [<Fact>]
    let ``checkCancellation returns Ok when nothing cancelled`` () =
        let result = checkCancellation None None
        Assert.Equal(Ok (), result)

    [<Fact>]
    let ``checkCancellation returns Ok with non-cancelled token`` () =
        let cts = new CancellationTokenSource()
        let result = checkCancellation None (Some cts.Token)
        Assert.Equal(Ok (), result)

    [<Fact>]
    let ``checkCancellation returns Error when token is cancelled`` () =
        let cts = new CancellationTokenSource()
        cts.Cancel()
        let result = checkCancellation None (Some cts.Token)
        match result with
        | Error (QuantumError.OperationError ("Cancellation", msg)) ->
            Assert.Contains("cancelled", msg.ToLower())
        | _ -> failwith "Expected OperationError with Cancellation"

    [<Fact>]
    let ``checkCancellation returns Error when reporter is cancelled`` () =
        let reporter =
            { new IProgressReporter with
                member _.Report(_) = ()
                member _.IsCancellationRequested = true }
        let result = checkCancellation (Some reporter) None
        match result with
        | Error (QuantumError.OperationError ("Cancellation", msg)) ->
            Assert.Contains("cancelled", msg.ToLower())
        | _ -> failwith "Expected OperationError with Cancellation"

    [<Fact>]
    let ``checkCancellation returns Ok with non-cancelled reporter`` () =
        let reporter = createNullReporter()
        let result = checkCancellation (Some reporter) None
        Assert.Equal(Ok (), result)

    [<Fact>]
    let ``checkCancellation returns Error when both token and reporter cancelled`` () =
        let cts = new CancellationTokenSource()
        cts.Cancel()
        let reporter =
            { new IProgressReporter with
                member _.Report(_) = ()
                member _.IsCancellationRequested = true }
        let result = checkCancellation (Some reporter) (Some cts.Token)
        match result with
        | Error (QuantumError.OperationError ("Cancellation", _)) -> ()
        | _ -> failwith "Expected OperationError with Cancellation"
