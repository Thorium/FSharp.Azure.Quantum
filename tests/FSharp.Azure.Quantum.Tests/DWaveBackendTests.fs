namespace FSharp.Azure.Quantum.Tests

open Xunit
open FSharp.Azure.Quantum.Core
open FSharp.Azure.Quantum.Core.QaoaCircuit
open FSharp.Azure.Quantum.Core.CircuitAbstraction
open FSharp.Azure.Quantum.Backends
open FSharp.Azure.Quantum.Backends.DWaveBackend
open FSharp.Azure.Quantum.Backends.DWaveTypes
open FSharp.Azure.Quantum.Core.BackendAbstraction
open System.Threading
open System.Threading.Tasks

/// Unit tests for D-Wave Backend (MockDWaveBackend)
///
/// Tests cover:
/// - QAOA circuit execution via Execute
/// - Error handling (non-QAOA circuits, qubit limits)
/// - Deterministic results with seed
///
/// Note: MockDWaveBackend does NOT implement IQuantumBackend interface.
/// D-Wave annealing is fundamentally different from gate-based quantum computing.
/// Use Execute method directly.
module DWaveBackendTests =

    // ============================================================================
    // HELPER FUNCTIONS FOR TEST DATA
    // ============================================================================

    /// Create a simple QAOA circuit for testing
    let createSimpleQaoaCircuit () : ICircuit =
        let hamiltonian: ProblemHamiltonian =
            {
                NumQubits = 2
                Terms =
                    [|
                        {
                            Coefficient = -1.0
                            QubitsIndices = [| 0 |]
                            PauliOperators = [| PauliZ |]
                        }
                        {
                            Coefficient = 0.5
                            QubitsIndices = [| 0; 1 |]
                            PauliOperators = [| PauliZ; PauliZ |]
                        }
                    |]
            }

        let mixerHam = MixerHamiltonian.create 2
        let parameters = [| (0.5, 0.3) |]
        let qaoaCircuit = QaoaCircuit.build hamiltonian mixerHam parameters
        QaoaCircuitWrapper(qaoaCircuit) :> ICircuit

    /// Create a QAOA circuit whose QUBO has DEGENERATE minima ({01, 10}), so a
    /// simulated annealer produces multiple distinct solutions across shots.
    /// (A pure ZZ coupling: Q_01 = 2, Q_00 = Q_11 = -1 → E(01) = E(10) = -1.)
    /// Used by the stochasticity tests — a problem with a unique ground state
    /// legitimately converges to the same solution on every shot.
    let createDegenerateQaoaCircuit () : ICircuit =
        let hamiltonian: ProblemHamiltonian =
            {
                NumQubits = 2
                Terms =
                    [|
                        {
                            Coefficient = 0.5
                            QubitsIndices = [| 0; 1 |]
                            PauliOperators = [| PauliZ; PauliZ |]
                        }
                    |]
            }

        let mixerHam = MixerHamiltonian.create 2
        let parameters = [| (0.5, 0.3) |]
        let qaoaCircuit = QaoaCircuit.build hamiltonian mixerHam parameters
        QaoaCircuitWrapper(qaoaCircuit) :> ICircuit

    /// Create MaxCut QAOA circuit
    let createMaxCutCircuit () : ICircuit =
        let hamiltonian: ProblemHamiltonian =
            {
                NumQubits = 3
                Terms =
                    [|
                        {
                            Coefficient = -2.5
                            QubitsIndices = [| 0 |]
                            PauliOperators = [| PauliZ |]
                        }
                        {
                            Coefficient = -4.0
                            QubitsIndices = [| 1 |]
                            PauliOperators = [| PauliZ |]
                        }
                        {
                            Coefficient = -1.5
                            QubitsIndices = [| 2 |]
                            PauliOperators = [| PauliZ |]
                        }
                        {
                            Coefficient = 1.25
                            QubitsIndices = [| 0; 1 |]
                            PauliOperators = [| PauliZ; PauliZ |]
                        }
                        {
                            Coefficient = 0.75
                            QubitsIndices = [| 1; 2 |]
                            PauliOperators = [| PauliZ; PauliZ |]
                        }
                    |]
            }

        let mixerHam = MixerHamiltonian.create 3
        let qaoaCircuit = QaoaCircuit.build hamiltonian mixerHam [| (0.5, 0.3) |]
        QaoaCircuitWrapper(qaoaCircuit) :> ICircuit

    // ============================================================================
    // NOTE: Interface compliance tests removed
    // ============================================================================
    //
    // MockDWaveBackend does NOT implement IQuantumBackend interface.
    // D-Wave annealing backends work with QUBO/Ising problems, not quantum states.
    // Tests now use Execute method directly instead of IQuantumBackend interface.

    //==============================================================================
    // QAOA CIRCUIT EXECUTION TESTS
    // ============================================================================

    [<Fact>]
    let ``Execute succeeds for simple QAOA circuit`` () =
        let backend = createDefaultMockBackend ()
        let circuit = createSimpleQaoaCircuit ()

        let result = backend.Execute circuit 100

        match result with
        | Ok execResult ->
            Assert.Equal(100, execResult.NumShots)
            Assert.Equal(100, execResult.Measurements.Length)
            // All measurements should be 2-qubit bitstrings
            for bitstring in execResult.Measurements do
                Assert.Equal(2, bitstring.Length)
                Assert.All(bitstring, fun bit -> Assert.True(bit = 0 || bit = 1))
        | Error e -> Assert.True(false, $"Should not fail: {e}")

    [<Fact>]
    let ``Execute with seed produces deterministic results`` () =
        let backend1 = createMockDWaveBackend Advantage_System6_1 (Some 42)
        let backend2 = createMockDWaveBackend Advantage_System6_1 (Some 42)
        let circuit = createSimpleQaoaCircuit ()

        let result1 = backend1.Execute circuit 100
        let result2 = backend2.Execute circuit 100

        match result1, result2 with
        | Ok exec1, Ok exec2 ->
            // Results should be identical with same seed
            Assert.Equal(exec1.NumShots, exec2.NumShots)
            Assert.Equal(exec1.Measurements.Length, exec2.Measurements.Length)
            // Compare measurements element by element
            for i in 0 .. exec1.Measurements.Length - 1 do
                Assert.Equal<int[]>(exec1.Measurements.[i], exec2.Measurements.[i])
        | _ -> Assert.True(false, "Both executions should succeed")

    [<Fact>]
    let ``Execute with different seeds produces different results`` () =
        // Deterministic version of the "randomness plumbing" check: two DIFFERENT
        // seeds must give different sample sequences. (The previous "no seed ⇒
        // different results" assertion was inherently flaky: on the degenerate
        // two-minimum problem, two unseeded 100-shot runs produce identical
        // occurrence counts with ~5% probability — the annealer converging to the
        // same physics is correct behavior, not a seeding bug.)
        let backend1 = createMockDWaveBackend Advantage_System6_1 (Some 1)
        let backend2 = createMockDWaveBackend Advantage_System6_1 (Some 2)
        // Degenerate ground states ({01, 10}) make seed-dependent sampling visible;
        // the simple circuit's unique minimum would legitimately give identical runs.
        let circuit = createDegenerateQaoaCircuit ()

        let result1 = backend1.Execute circuit 100
        let result2 = backend2.Execute circuit 100

        match result1, result2 with
        | Ok exec1, Ok exec2 ->
            let allSame =
                exec1.Measurements
                |> Array.zip exec2.Measurements
                |> Array.forall (fun (m1, m2) -> m1 = m2)

            Assert.False(allSame, "Different seeds should produce different sample sequences")
        | _ -> Assert.True(false, "Both executions should succeed")

    [<Fact>]
    let ``Execute handles MaxCut problem`` () =
        let backend = createDefaultMockBackend ()
        let circuit = createMaxCutCircuit ()

        match backend.Execute circuit 200 with
        | Ok execResult ->
            Assert.Equal(200, execResult.NumShots)
            // All measurements should be 3-qubit bitstrings
            for bitstring in execResult.Measurements do
                Assert.Equal(3, bitstring.Length)
        | Error e -> Assert.True(false, $"MaxCut execution failed: {e}")

    [<Fact>]
    let ``Execute produces multiple distinct solutions`` () =
        let backend = createDefaultMockBackend ()
        // Degenerate ground states ({01, 10}) guarantee multiple distinct
        // solutions; the simple circuit's unique minimum would not.
        let circuit = createDegenerateQaoaCircuit ()

        match backend.Execute circuit 1000 with
        | Ok execResult ->
            // Count distinct bitstrings
            let distinctSolutions = execResult.Measurements |> Array.distinct |> Array.length
            // Should have multiple distinct bitstrings (not just one)
            Assert.True(distinctSolutions > 1, $"Should produce multiple solutions, got {distinctSolutions}")
        | Error e -> Assert.True(false, $"Execution failed: {e}")

    // ============================================================================
    // ERROR HANDLING TESTS
    // ============================================================================

    [<Fact>]
    let ``Execute rejects non-QAOA circuit`` () =
        let backend = createDefaultMockBackend ()
        // Create a raw ICircuit that's not a QaoaCircuitWrapper
        let nonQaoaCircuit =
            { new ICircuit with
                member _.NumQubits = 2
                member _.Description = "NonQAOA"
            }

        let result = backend.Execute nonQaoaCircuit 100

        match result with
        | Error err ->
            // Error should be a QuantumError.ValidationError
            Assert.True(true) // Successfully rejected
        | Ok _ -> Assert.True(false, "Should have failed")

    [<Fact>]
    let ``Execute rejects circuit exceeding qubit limit`` () =
        // Create small solver with limited qubits
        let smallBackend = createMockDWaveBackend Advantage_System1_1 None // 5000 qubits

        // Create huge Hamiltonian (6000 qubits)
        let terms =
            [|
                for i in 0..5999 do
                    yield
                        {
                            Coefficient = -1.0
                            QubitsIndices = [| i |]
                            PauliOperators = [| PauliZ |]
                        }
            |]

        let hugeHamiltonian: ProblemHamiltonian = { NumQubits = 6000; Terms = terms }
        let mixerHam = MixerHamiltonian.create 6000
        let circuit = QaoaCircuit.build hugeHamiltonian mixerHam [| (0.5, 0.3) |]
        let wrapper = QaoaCircuitWrapper(circuit) :> ICircuit

        let result = smallBackend.Execute wrapper 10

        match result with
        | Error _ ->
            // Successfully rejected - error is expected
            Assert.True(true)
        | Ok _ -> Assert.True(false, "Should have failed")

    [<Fact>]
    let ``Execute rejects zero shots`` () =
        let backend = createDefaultMockBackend ()
        let circuit = createSimpleQaoaCircuit ()

        let result = backend.Execute circuit 0

        match result with
        | Error _ -> Assert.True(true) // Error expected for zero shots
        | Ok _ -> Assert.True(false, "Should reject zero shots")

    // ============================================================================
    // NOTE: Async execution tests removed
    // ============================================================================
    //
    // MockDWaveBackend only provides Execute (synchronous).
    // No ExecuteAsync available - backend doesn't implement IQuantumBackend interface.

    // ============================================================================
    // SOLVER CONFIGURATION TESTS
    // ============================================================================

    // NOTE: Removed test "Different solver types have different qubit counts"
    // MockDWaveBackend no longer exposes MaxQubits property (not part of interface)
    // Qubit limits are validated internally during Execute()

    [<Fact>]
    let ``All solver types can execute QAOA circuits`` () =
        let solvers =
            [
                Advantage_System1_1
                Advantage_System4_1
                Advantage_System6_1
                Advantage2_Prototype
            ]

        let circuit = createSimpleQaoaCircuit ()

        for solver in solvers do
            let backend = createMockDWaveBackend solver (Some 42)
            let result = backend.Execute circuit 10

            result
            |> Result.map (fun _ -> Assert.True(true))
            |> Result.defaultWith (fun e -> Assert.True(false, $"Solver {solver} failed: {e}"))

    [<Fact>]
    let ``QUBO business builder (MaxCut) solves via D-Wave annealing through the unified backend`` () : Task =
        task {
            // A D-Wave backend is an IQuantumBackend: the QAOA circuit the solver builds is
            // reverse-extracted to a QUBO, annealed, and decoded — so the *same* MaxCut.solveAsync
            // targets annealing hardware just by passing a D-Wave backend. Triangle MaxCut = 2.
            let dwave =
                MockDWaveBackend(Advantage_System6_1, seed = 42) :> BackendAbstraction.IQuantumBackend

            let triangle =
                FSharp.Azure.Quantum.MaxCut.createProblem
                    [ "A"; "B"; "C" ]
                    [ ("A", "B", 1.0); ("B", "C", 1.0); ("A", "C", 1.0) ]

            let! result =
                FSharp.Azure.Quantum.MaxCut.solveAsync triangle (Some dwave) CancellationToken.None

            result
            |> Result.map (fun solution -> Assert.Equal(2.0, solution.CutValue, 3))
            |> Result.defaultWith (fun e -> failwith $"D-Wave MaxCut failed: {e.Message}")
        }
        :> Task

    // ============================================================================
    // READS: the samples of a state are the anneals the backend took
    // ============================================================================

    let private expectOk (result: Result<'T, QuantumError>) : 'T =
        result |> Result.defaultWith (fun e -> failwith $"expected Ok, got {e.Message}")

    /// Reads a state holds: the occurrences of its solutions.
    let private readsOf (state: QuantumState) : int =
        match state with
        | QuantumState.IsingSamples(_, solutions) ->
            (solutions :?> DWaveSolution list) |> List.sumBy (fun solution -> solution.NumOccurrences)
        | other -> failwith $"expected IsingSamples, got {QuantumState.stateType other}"

    [<Fact>]
    let ``ExecuteToState takes one read by default and measuring it returns one sample`` () =
        let backend = MockDWaveBackend(Advantage_System6_1, seed = 7) :> IQuantumBackend
        let state = backend.ExecuteToState(createDegenerateQaoaCircuit ()) |> expectOk

        Assert.Equal(1, readsOf state)
        // One anneal is one sample, however many are asked for.
        Assert.Equal(1, (QuantumState.measure state 1000).Length)

    [<Fact>]
    let ``ExecuteToState takes the configured default reads`` () =
        let backend =
            MockDWaveBackend(Advantage_System6_1, seed = 7, defaultReads = 64) :> IQuantumBackend

        let state = backend.ExecuteToState(createDegenerateQaoaCircuit ()) |> expectOk

        Assert.Equal(64, readsOf state)
        Assert.Equal(64, (QuantumState.measure state 1000).Length)
        Assert.Equal(10, (QuantumState.measure state 10).Length)

    [<Fact>]
    let ``a default read count below one is rejected`` () =
        Assert.Throws<System.ArgumentException>(fun () ->
            MockDWaveBackend(Advantage_System6_1, defaultReads = 0) |> ignore)
        |> ignore

    [<Theory; InlineData(1); InlineData(37); InlineData(500)>]
    let ``ExecuteWithReadsAsync takes exactly the reads asked for`` (reads: int) : Task =
        task {
            let backend = MockDWaveBackend(Advantage_System6_1, seed = 7) :> IAnnealingBackend

            let! result = backend.ExecuteWithReadsAsync (createDegenerateQaoaCircuit ()) reads CancellationToken.None
            let state = expectOk result

            Assert.Equal(reads, readsOf state)
            Assert.Equal(reads, (QuantumState.measure state reads).Length)
            Assert.Equal(reads, (QuantumState.measure state (10 * reads)).Length)
        }

    [<Fact>]
    let ``ExecuteWithReadsAsync cuts a request above the solver limit down to it`` () : Task =
        task {
            let backend = MockDWaveBackend(Advantage_System6_1, seed = 7) :> IAnnealingBackend

            let! result =
                backend.ExecuteWithReadsAsync
                    (createDegenerateQaoaCircuit ())
                    (maxReadsPerExecution + 5)
                    CancellationToken.None

            let state = expectOk result

            // The state reports the reads taken, not the reads asked for.
            Assert.Equal(maxReadsPerExecution, readsOf state)
            Assert.Equal(maxReadsPerExecution, (QuantumState.measure state (maxReadsPerExecution + 5)).Length)
        }

    [<Fact>]
    let ``ExecuteWithReadsAsync rejects a read count below one`` () : Task =
        task {
            let backend = MockDWaveBackend(Advantage_System6_1, seed = 7) :> IAnnealingBackend
            match! backend.ExecuteWithReadsAsync (createSimpleQaoaCircuit ()) 0 CancellationToken.None with
            | Error(QuantumError.ValidationError("reads", _)) -> ()
            | other -> Assert.Fail($"expected a ValidationError for reads, got %A{other}")
        }

    [<Fact>]
    let ``the mock honours a cancelled token`` () : Task =
        task {
            let backend = MockDWaveBackend(Advantage_System6_1, seed = 7)
            use cancelled = new CancellationTokenSource()
            do! cancelled.CancelAsync()

            let! _ =
                Assert.ThrowsAnyAsync<System.OperationCanceledException>(fun () ->
                    (backend :> IAnnealingBackend).ExecuteWithReadsAsync (createSimpleQaoaCircuit ()) 10 cancelled.Token
                    :> Task)

            let! _ =
                Assert.ThrowsAnyAsync<System.OperationCanceledException>(fun () ->
                    (backend :> IQuantumBackend).ExecuteToStateAsync (createSimpleQaoaCircuit ()) cancelled.Token
                    :> Task)

            ()
        }

    // ============================================================================
    // QAOA SOLVERS: one submission per solve, for the shots the caller asked for
    // ============================================================================

    /// The mock annealer with every submission recorded: (reads asked for) per call.
    type private CountingAnnealer(seed: int) =
        let inner = MockDWaveBackend(Advantage_System6_1, seed = seed)
        let submissions = ResizeArray<int>()

        /// Reads asked for by each submission, in order; -1 for one that named no count.
        member _.Submissions = List.ofSeq submissions

        interface IQuantumBackend with
            member _.Name = (inner :> IQuantumBackend).Name
            member _.NativeStateType = (inner :> IQuantumBackend).NativeStateType

            member _.ExecuteToState circuit =
                submissions.Add -1
                (inner :> IQuantumBackend).ExecuteToState circuit

            member _.ExecuteToStateAsync circuit ct =
                submissions.Add -1
                (inner :> IQuantumBackend).ExecuteToStateAsync circuit ct

            member _.InitializeState n =
                (inner :> IQuantumBackend).InitializeState n

            member _.ApplyOperation op state =
                (inner :> IQuantumBackend).ApplyOperation op state

            member _.ApplyOperationAsync op state ct =
                (inner :> IQuantumBackend).ApplyOperationAsync op state ct

            member _.SupportsOperation op =
                (inner :> IQuantumBackend).SupportsOperation op

        interface IAnnealingBackend with
            member _.ExecuteWithReadsAsync circuit reads ct =
                submissions.Add reads
                (inner :> IAnnealingBackend).ExecuteWithReadsAsync circuit reads ct

    /// Triangle MaxCut as a QUBO: minimum -2 at every cut of two edges.
    let private triangleQubo =
        array2D [ [ -2.0; 2.0; 2.0 ]; [ 0.0; -2.0; 2.0 ]; [ 0.0; 0.0; -2.0 ] ]

    [<Theory; InlineData(true); InlineData(false)>]
    let ``a QAOA run submits the problem to the annealer once, for FinalShots reads`` (optimize: bool) : Task =
        task {
            let annealer = CountingAnnealer 11

            let config =
                { QaoaExecutionHelpers.defaultConfig with
                    EnableOptimization = optimize
                    OptimizationShots = 25
                    FinalShots = 300
                    MaxOptimizationIterations = 200
                }

            let! result = QaoaExecutionHelpers.runQaoaSampledAsync annealer triangleQubo config CancellationToken.None
            let run = expectOk result

            // Not one submission per optimizer evaluation or grid point: one, with the final shots.
            Assert.Equal<int list>([ 300 ], annealer.Submissions)
            Assert.Equal(300, run.Samples.Length)
            Assert.Equal(-2.0, QaoaExecutionHelpers.evaluateQubo triangleQubo run.Best, 9)
            // Nothing was optimised.
            Assert.Empty run.Parameters
            Assert.Equal(None, run.Converged)
            Assert.Equal(ValueNone, run.Iterations)

            // The plan made before the run says the same.
            let plan = QaoaExecutionHelpers.jobPlan annealer config
            Assert.Equal(1, plan.Jobs)
            Assert.Equal(300, plan.FinalShots)
        }

    [<Fact>]
    let ``the tuple-returning QAOA entry points submit to the annealer once too`` () : Task =
        task {
            let config =
                { QaoaExecutionHelpers.defaultConfig with
                    FinalShots = 120
                    MaxOptimizationIterations = 200
                }

            let optimizing = CountingAnnealer 3

            let! optimized =
                QaoaExecutionHelpers.executeQaoaWithOptimizationAsync optimizing triangleQubo config CancellationToken.None

            let best, parameters, _ = expectOk optimized
            Assert.Equal<int list>([ 120 ], optimizing.Submissions)
            Assert.Equal(-2.0, QaoaExecutionHelpers.evaluateQubo triangleQubo best, 9)
            Assert.Empty parameters

            let searching = CountingAnnealer 3

            let! searched =
                QaoaExecutionHelpers.executeQaoaWithGridSearchAsync searching triangleQubo config 4 CancellationToken.None

            expectOk searched |> ignore
            Assert.Equal<int list>([ 120 ], searching.Submissions)
        }

    [<Fact>]
    let ``a single QAOA circuit asks the annealer for the shots of the call`` () : Task =
        task {
            let annealer = CountingAnnealer 5

            let! result =
                QaoaExecutionHelpers.executeFromQuboAsync annealer triangleQubo [| (0.5, 0.5) |] 77 CancellationToken.None

            Assert.Equal(77, (expectOk result).Length)
            Assert.Equal<int list>([ 77 ], annealer.Submissions)
        }

    [<Fact>]
    let ``MaxCut on an annealer is one submission of NumShots reads`` () : Task =
        task {
            let annealer = CountingAnnealer 42

            let triangle =
                FSharp.Azure.Quantum.MaxCut.createProblem
                    [ "A"; "B"; "C" ]
                    [ ("A", "B", 1.0); ("B", "C", 1.0); ("A", "C", 1.0) ]

            let! result =
                FSharp.Azure.Quantum.MaxCut.solveAsync triangle (Some(annealer :> IQuantumBackend)) CancellationToken.None

            let solution = expectOk result
            Assert.Equal(2.0, solution.CutValue, 3)

            Assert.Equal<int list>(
                [ FSharp.Azure.Quantum.Quantum.QuantumMaxCutSolver.defaultConfig.NumShots ],
                annealer.Submissions
            )
        }

    [<Fact>]
    let ``the portfolio angle grid is one submission on an annealer`` () : Task =
        task {
            let annealer = CountingAnnealer 9

            let! result =
                FSharp.Azure.Quantum.Quantum.QuantumPortfolioSolver.sampleWithAngleGridAsync
                    annealer
                    triangleQubo
                    (0.5, 0.5)
                    50
                    400
                    CancellationToken.None

            let samples, angles = expectOk result
            Assert.Equal(400, samples.Length)
            Assert.Equal((0.5, 0.5), angles)
            Assert.Equal<int list>([ 400 ], annealer.Submissions)
        }

/// RealDWaveBackend against a local stand-in for SAPI: what num_reads it submits, and what
/// the returned state holds.
module RealDWaveBackendTests =

    open System
    open System.IO
    open System.Net
    open System.Net.Sockets
    open System.Text
    open System.Text.Json

    let private base64Of (values: 'T[]) (toBytes: 'T -> byte[]) : string =
        values |> Array.collect toBytes |> Convert.ToBase64String

    /// A SAPI stand-in on a loopback port. It serves a two-qubit solver (qubits 0 and 1, one
    /// coupler) with `num_reads_range` [1, maxReads], accepts a problem, and answers its
    /// status with two solutions whose occurrences add up to the num_reads it was sent.
    type private FakeSapi(maxReads: int) =
        let listener = new TcpListener(IPAddress.Loopback, 0)
        let submittedReads = ResizeArray<int>()
        let stop = new CancellationTokenSource()
        do listener.Start()

        let answerFor (reads: int) : string =
            // Solutions "10" (qubit 0 up, qubit 1 down) and "01", MSB-first, one byte each.
            let occurrences = if reads > 1 then [| reads - 1; 1 |] else [| reads |]
            let solutions = [| 0b10000000uy; 0b01000000uy |] |> Array.take occurrences.Length
            let energies = [| -1.0; -1.0 |] |> Array.take occurrences.Length

            JsonSerializer.Serialize(
                {|
                    status = "COMPLETED"
                    answer =
                        {|
                            format = "qp"
                            num_variables = 2
                            active_variables = base64Of [| 0; 1 |] (fun (v: int) -> BitConverter.GetBytes v)
                            energies = base64Of energies (fun (v: float) -> BitConverter.GetBytes v)
                            num_occurrences = base64Of occurrences (fun (v: int) -> BitConverter.GetBytes v)
                            solutions = Convert.ToBase64String solutions
                        |}
                |}
            )

        let respond (requestLine: string) (body: string) : string =
            if requestLine.StartsWith "GET" && requestLine.Contains "/solvers/remote/" then
                JsonSerializer.Serialize(
                    {|
                        properties =
                            {|
                                qubits = [| 0; 1 |]
                                couplers = [| [| 0; 1 |] |]
                                num_reads_range = [| 1; maxReads |]
                            |}
                    |}
                )
            elif requestLine.StartsWith "POST" && requestLine.Contains "/problems/" then
                use doc = JsonDocument.Parse body
                submittedReads.Add(doc.RootElement.[0].GetProperty("params").GetProperty("num_reads").GetInt32())
                """[{"id":"problem-1","status":"PENDING"}]"""
            else
                answerFor (Seq.last submittedReads)

        let serve (client: TcpClient) : Task =
            task {
                use client = client
                use stream = client.GetStream()
                let received = new MemoryStream()
                let buffer = Array.zeroCreate<byte> 8192

                let text () =
                    Encoding.ASCII.GetString(received.GetBuffer(), 0, int received.Length)

                let headerEnd () = text().IndexOf "\r\n\r\n"

                let mutable complete = false

                while not complete do
                    let! count = stream.ReadAsync(buffer, 0, buffer.Length)
                    received.Write(buffer, 0, count)
                    let split = headerEnd ()

                    if count = 0 then
                        complete <- true
                    elif split >= 0 then
                        let headers = text().Substring(0, split)

                        let contentLength =
                            headers.Split "\r\n"
                            |> Array.tryPick (fun line ->
                                if line.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase) then
                                    Some(int (line.Substring(15).Trim()))
                                else
                                    None)
                            |> Option.defaultValue 0

                        complete <- int received.Length >= split + 4 + contentLength

                let request = text ()
                let split = max 0 (headerEnd ())
                let requestLine = request.Substring(0, max 0 (request.IndexOf "\r\n"))
                let body = respond requestLine (request.Substring(min request.Length (split + 4)))
                let payload = Encoding.UTF8.GetBytes body

                let head =
                    $"HTTP/1.1 200 OK\r\nContent-Type: application/json\r\nContent-Length: {payload.Length}\r\nConnection: close\r\n\r\n"
                    |> Encoding.ASCII.GetBytes

                do! stream.WriteAsync(head, 0, head.Length)
                do! stream.WriteAsync(payload, 0, payload.Length)
            }

        let loop: Task =
            task {
                try
                    while not stop.IsCancellationRequested do
                        let! client = listener.AcceptTcpClientAsync stop.Token
                        do! serve client
                with
                | :? OperationCanceledException
                | :? SocketException
                | :? ObjectDisposedException -> ()
            }

        member _.Endpoint =
            $"http://127.0.0.1:{(listener.LocalEndpoint :?> IPEndPoint).Port}/sapi/v2/"

        /// num_reads of every problem submitted, in order.
        member _.SubmittedReads = List.ofSeq submittedReads

        interface IDisposable with
            member _.Dispose() =
                stop.Cancel()
                listener.Stop()
                loop.Wait(TimeSpan.FromSeconds 5.0) |> ignore

    let private backendFor (sapi: FakeSapi) : RealDWaveBackend.RealDWaveBackend =
        RealDWaveBackend.create
            {
                RealDWaveBackend.ApiToken = "DEV-test-token"
                Endpoint = sapi.Endpoint
                Solver = "Advantage_system6.1"
                TimeoutMs = Some 20000
            }

    /// A problem on the stand-in solver's working graph: qubits 0 and 1, coupled.
    let private circuit () =
        DWaveBackendTests.createDegenerateQaoaCircuit ()

    let private expectOk (result: Result<'T, QuantumError>) : 'T =
        result |> Result.defaultWith (fun e -> failwith $"expected Ok, got {e.Message}")

    [<Fact>]
    let ``ExecuteWithReadsAsync submits the reads asked for and returns that many samples`` () : Task =
        task {
            use sapi = new FakeSapi(10000)
            use backend = backendFor sapi

            let! result = (backend :> IAnnealingBackend).ExecuteWithReadsAsync (circuit ()) 40 CancellationToken.None
            let state = expectOk result

            Assert.Equal<int list>([ 40 ], sapi.SubmittedReads)

            let samples = QuantumState.measure state 1000
            Assert.Equal(40, samples.Length)
            Assert.Equal(39, samples |> Array.filter (fun s -> s = [| 1; 0 |]) |> Array.length)
            Assert.Equal(1, samples |> Array.filter (fun s -> s = [| 0; 1 |]) |> Array.length)
        }

    [<Fact>]
    let ``a request above the solver's num_reads range is cut down and the state holds the reads taken`` () : Task =
        task {
            use sapi = new FakeSapi(100)
            use backend = backendFor sapi

            let! result = (backend :> IAnnealingBackend).ExecuteWithReadsAsync (circuit ()) 500 CancellationToken.None
            let state = expectOk result

            Assert.Equal<int list>([ 100 ], sapi.SubmittedReads)
            Assert.Equal(100, (QuantumState.measure state 500).Length)
        }

    [<Fact>]
    let ``ExecuteToStateAsync submits the default single read`` () : Task =
        task {
            use sapi = new FakeSapi(10000)
            use backend = backendFor sapi

            let! result = (backend :> IQuantumBackend).ExecuteToStateAsync (circuit ()) CancellationToken.None
            let state = expectOk result

            Assert.Equal<int list>([ 1 ], sapi.SubmittedReads)
            Assert.Equal(1, (QuantumState.measure state 1000).Length)
        }

    [<Fact>]
    let ``a QAOA solve on the real backend is one problem with FinalShots reads`` () : Task =
        task {
            use sapi = new FakeSapi(10000)
            use backend = backendFor sapi

            // Q = [[-1, 2], [0, -1]]: the coupled pair the stand-in solver has.
            let qubo = array2D [ [ -1.0; 2.0 ]; [ 0.0; -1.0 ] ]

            let config =
                { QaoaExecutionHelpers.defaultConfig with
                    FinalShots = 60
                    MaxOptimizationIterations = 50
                }

            let! result = QaoaExecutionHelpers.runQaoaSampledAsync backend qubo config CancellationToken.None
            let run = expectOk result

            Assert.Equal<int list>([ 60 ], sapi.SubmittedReads)
            Assert.Equal(60, run.Samples.Length)
        }

    [<Fact>]
    let ``a cancelled token stops the submission`` () : Task =
        task {
            use sapi = new FakeSapi(10000)
            use backend = backendFor sapi
            use cancelled = new CancellationTokenSource()
            do! cancelled.CancelAsync()

            let! _ =
                Assert.ThrowsAnyAsync<OperationCanceledException>(fun () ->
                    (backend :> IAnnealingBackend).ExecuteWithReadsAsync (circuit ()) 10 cancelled.Token :> Task)

            Assert.Empty sapi.SubmittedReads
        }

    [<Fact>]
    let ``the real backend counts as billed and the mock does not`` () =
        use sapi = new FakeSapi(10000)
        use backend = backendFor sapi
        Assert.True(QaoaExecutionHelpers.isBilledBackend backend)

        Assert.False(
            QaoaExecutionHelpers.isBilledBackend (
                DWaveBackend.createMockDWaveBackend DWaveTypes.Advantage_System6_1 None
            )
        )

module RealDWaveRedirectTests =

    open System
    open System.Net
    open System.Net.Sockets
    open System.Text

    /// A local host that answers one request.
    type private Host(answer: string) =
        let listener = new TcpListener(IPAddress.Loopback, 0)
        let mutable called = false
        do listener.Start()

        let served =
            task {
                try
                    use! socket = listener.AcceptTcpClientAsync()
                    called <- true
                    use stream = socket.GetStream()
                    let buffer = Array.zeroCreate 16384
                    let! _ = stream.ReadAsync(buffer, 0, buffer.Length)
                    let bytes = Encoding.ASCII.GetBytes answer
                    do! stream.WriteAsync(bytes, 0, bytes.Length)
                with
                | :? SocketException
                | :? ObjectDisposedException -> ()
            }

        member _.Port = (listener.LocalEndpoint :?> IPEndPoint).Port
        member _.Called = called
        member _.Served: Task = served

        interface IDisposable with
            member _.Dispose() = listener.Stop()

    [<Literal>]
    let private okAnswer = "HTTP/1.1 200 OK\r\nContent-Length: 2\r\nConnection: close\r\n\r\n{}"

    let private redirectTo (port: int) =
        $"HTTP/1.1 307 Moved\r\nLocation: http://127.0.0.1:{port}/stolen\r\nContent-Length: 0\r\nConnection: close\r\n\r\n"

    [<Fact>]
    let ``the SAPI token doesn't follow a redirect to another host`` () : Task =
        task {
            use other = new Host(okAnswer)
            use first = new Host(redirectTo other.Port)
            use http = RealDWaveBackend.createSapiHttpClient "DEV-the-token"
            use! response = http.GetAsync $"http://127.0.0.1:{first.Port}/sapi/v2/solvers/remote/"
            Assert.Equal(HttpStatusCode.TemporaryRedirect, response.StatusCode)
            do! first.Served
            Assert.False other.Called
        }
