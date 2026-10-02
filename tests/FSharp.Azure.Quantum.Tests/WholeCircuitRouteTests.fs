namespace FSharp.Azure.Quantum.Tests

open System
open System.Numerics
open System.Net.Http
open Xunit
open FSharp.Azure.Quantum
open FSharp.Azure.Quantum.Core
open FSharp.Azure.Quantum.Core.BackendAbstraction
open FSharp.Azure.Quantum.Backends
open FSharp.Azure.Quantum.Algorithms
open FSharp.Azure.Quantum.GroverSearch
open FSharp.Azure.Quantum.LocalSimulator

/// Route selection for backends that run complete circuits only.
///
/// Route order for every algorithm: (1) the native intent where the backend claims it,
/// (2) gate by gate, (3) the whole circuit as one job, taken only when (1) or (2) is refused
/// as incremental application. These tests pin each route: a cloud-style backend that refuses
/// incremental application (and the real cloud backend classes) takes (3) and gets the right
/// answer; a backend claiming the intent receives the intent and never a gate sequence; the
/// local simulator keeps (2). Every simulated circuit here is at most 16 qubits.
[<Collection("NonParallel")>]
module WholeCircuitRouteTests =

    // ========================================================================
    // TEST BACKENDS
    // ========================================================================

    let private incrementalRefusal name : Result<QuantumState, QuantumError> =
        Error(
            QuantumError.OperationError(
                "ApplyOperation",
                $"{name} does not support incremental ApplyOperation. Use ExecuteToState with a complete circuit instead."
            )
        )

    /// Cloud-style backend: refuses incremental ApplyOperation and runs complete circuits on the
    /// local simulator. With `shots` it returns the frequencies of that many seeded samples as a
    /// phase-less state (CloudBackendHelpers.histogramToQuantumState), like cloud hardware; with
    /// None it returns the exact state. `claims` decides SupportsOperation.
    type private WholeCircuitBackend(shots: int option, seed: int, claims: QuantumOperation -> bool) =
        let inner = LocalBackend.LocalBackend() :> IQuantumBackend
        let rng = Random seed
        let widths = ResizeArray<int>()

        member _.Submitted = widths.Count
        member _.Widths = List.ofSeq widths

        interface IQuantumBackend with
            member _.ExecuteToState circuit =
                widths.Add circuit.NumQubits

                match inner.ExecuteToState circuit, shots with
                | Ok(QuantumState.StateVector sv), Some shots ->
                    let p = Measurement.getProbabilityDistribution sv
                    let n = StateVector.numQubits sv
                    let cumulative = Array.scan (+) 0.0 p |> Array.tail
                    let counts = Collections.Generic.Dictionary<int, int>()

                    for _ in 1..shots do
                        let k = Array.BinarySearch(cumulative, rng.NextDouble())
                        let k = min (p.Length - 1) (if k >= 0 then k + 1 else ~~~k)

                        counts.[k] <-
                            (match counts.TryGetValue k with
                             | true, c -> c
                             | _ -> 0)
                            + 1

                    let histogram =
                        counts
                        |> Seq.map (fun kv -> Convert.ToString(kv.Key, 2).PadLeft(n, '0'), kv.Value)
                        |> Map.ofSeq

                    Ok(CloudBackendHelpers.histogramToQuantumState histogram n)
                | other, _ -> other

            member _.NativeStateType = inner.NativeStateType

            member _.ApplyOperation _ _ =
                incrementalRefusal "WholeCircuitBackend"

            member _.SupportsOperation operation = claims operation
            member _.Name = "whole-circuit test backend"
            member _.InitializeState n = inner.InitializeState n

            member this.ExecuteToStateAsync circuit _ =
                Threading.Tasks.Task.FromResult((this :> IQuantumBackend).ExecuteToState circuit)

            member _.ApplyOperationAsync _ _ _ =
                Threading.Tasks.Task.FromResult(incrementalRefusal "WholeCircuitBackend")

    /// Claims what gate-based cloud hardware claims.
    let private cloudClaims = CloudBackendHelpers.isCloudSupportedOperation

    /// Claims every operation, native intents included, as cloud backends once did.
    let private claimsEverything (_: QuantumOperation) = true

    let private sampling shots seed =
        WholeCircuitBackend(Some shots, seed, cloudClaims)

    let private exact () =
        WholeCircuitBackend(None, 0, cloudClaims)

    let private local () =
        LocalBackend.LocalBackend() :> IQuantumBackend

    /// Records every operation applied to `inner` and every submitted circuit; `claims`
    /// overrides SupportsOperation when given.
    type private RecordingBackend(inner: IQuantumBackend, claims: (QuantumOperation -> bool) option) =
        let applied = ResizeArray<QuantumOperation>()
        let mutable submitted = 0

        member _.Applied = List.ofSeq applied
        member _.Submitted = submitted

        interface IQuantumBackend with
            member _.ExecuteToState circuit =
                submitted <- submitted + 1
                inner.ExecuteToState circuit

            member _.NativeStateType = inner.NativeStateType

            member _.ApplyOperation operation state =
                applied.Add operation
                inner.ApplyOperation operation state

            member _.SupportsOperation operation =
                match claims with
                | Some claim -> claim operation
                | None -> inner.SupportsOperation operation

            member _.Name = inner.Name
            member _.InitializeState n = inner.InitializeState n

            member this.ExecuteToStateAsync circuit _ =
                Threading.Tasks.Task.FromResult((this :> IQuantumBackend).ExecuteToState circuit)

            member this.ApplyOperationAsync operation state _ =
                Threading.Tasks.Task.FromResult((this :> IQuantumBackend).ApplyOperation operation state)

    let private isGate op =
        match op with
        | QuantumOperation.Gate _ -> true
        | _ -> false

    let private isAlgorithm op =
        match op with
        | QuantumOperation.Algorithm _ -> true
        | _ -> false

    /// Gates of a (possibly nested) operation list.
    let rec private gateCount (ops: QuantumOperation list) : int =
        ops
        |> List.sumBy (function
            | QuantumOperation.Gate _ -> 1
            | QuantumOperation.Sequence inner -> gateCount inner
            | _ -> 0)

    /// The real cloud backend classes. ApplyOperation and SupportsOperation never touch the
    /// network; ExecuteToState tries to submit a job to an address that cannot resolve.
    let private realCloudBackends () : (string * IQuantumBackend) list =
        let http = new HttpClient(Timeout = TimeSpan.FromSeconds 30.0)

        let url =
            "https://example.invalid/subscriptions/x/resourceGroups/y/providers/Microsoft.Quantum/workspaces/z"

        [
            "Rigetti", CloudBackends.CloudBackendFactory.createRigetti http url "rigetti.sim.qvm" 100
            "IonQ", CloudBackends.CloudBackendFactory.createIonQ http url "ionq.simulator" 100
        ]

    /// A real cloud backend got as far as submitting a job: the run failed on the unreachable
    /// workspace, not on the incremental refusal a gate-by-gate route meets.
    let private assertReachedSubmission (name: string) (result: Result<'T, QuantumError>) =
        match result with
        | Ok _ -> Assert.Fail($"{name}: a job to an unreachable workspace cannot succeed")
        | Error e ->
            Assert.False(
                UnifiedBackend.isIncrementalUnsupported e,
                $"{name}: the run stopped at incremental application instead of submitting a job: {e.Message}"
            )

    // ========================================================================
    // SHOR
    // ========================================================================

    [<Fact>]
    let ``Shor Route - sampling cloud backend finds the period with one whole-circuit job`` () =
        let backend = sampling 256 11

        match Shor.findPeriodQuantum 7 15 3 (backend :> IQuantumBackend) with
        | Ok result ->
            Assert.Equal(4, result.Period)
            // Every attempt is a further shot of the one job, not a job of its own.
            Assert.Equal(1, backend.Submitted)
            Assert.Equal<int list>([ ModularExponentiationCircuit.totalQubitsFor 15 3 ], backend.Widths)
        | Error e -> Assert.Fail($"Shor on a whole-circuit backend failed: {e.Message}")

    [<Fact>]
    let ``Shor Route - retries read distinct recorded shots and a spent job is followed by a new one`` () =
        // a = 7, N = 15, 3 counting qubits: half of all shots give the period 4. One recorded
        // shot per job means every retry needs a job of its own — re-reading the first job's
        // single outcome 16 times would fail whenever that outcome is a bad one.
        for seed in 1..12 do
            let backend = sampling 1 seed

            match Shor.findPeriodQuantum 7 15 3 (backend :> IQuantumBackend) with
            | Ok result ->
                Assert.Equal(4, result.Period)
                Assert.Equal(result.Attempts, backend.Submitted)
            | Error e -> Assert.Fail($"seed {seed}: {e.Message}")

        // Four recorded shots per job: attempts walk them before a second job is submitted.
        for seed in 1..12 do
            let backend = sampling 4 (100 + seed)

            match Shor.findPeriodQuantum 7 15 3 (backend :> IQuantumBackend) with
            | Ok result -> Assert.Equal((result.Attempts + 3) / 4, backend.Submitted)
            | Error e -> Assert.Fail($"seed {seed}: {e.Message}")

    [<Fact>]
    let ``Shor Route - native intent refused as incremental falls back to the whole circuit`` () =
        // A backend that claims the modular-exponentiation QPE intent but cannot apply it
        // incrementally: the intent is tried first and the refusal routes to one job.
        let backend = WholeCircuitBackend(Some 256, 5, claimsEverything)

        match
            Shor.planPeriodFinding
                (backend :> IQuantumBackend)
                {
                    Base = 7
                    Modulus = 15
                    CountingQubits = 3
                }
        with
        | Ok(Shor.ShorPeriodFindingPlan.ExecuteNatively _) -> ()
        | other -> Assert.Fail($"A backend claiming the intent should be planned natively: %A{other}")

        match Shor.findPeriodQuantum 7 15 3 (backend :> IQuantumBackend) with
        | Ok result ->
            Assert.Equal(4, result.Period)
            Assert.Equal(1, backend.Submitted)
        | Error e -> Assert.Fail($"Fallback from a refused native intent failed: {e.Message}")

    [<Fact>]
    let ``Shor Route - real cloud backend classes submit the period-finding circuit as a job`` () =
        for name, backend in realCloudBackends () do
            assertReachedSubmission name (Shor.findPeriodQuantum 7 15 3 backend)

    [<Fact>]
    let ``Shor Route - a backend claiming the native intent receives it and never a gate`` () =
        // Stands in for a backend that realises modular-exponentiation QPE natively (the
        // topological backend does; its own test pins that): the intent must arrive whole.
        let local = LocalBackend.LocalBackend() :> IQuantumBackend

        let native =
            { new IQuantumBackend with
                member _.ExecuteToState circuit = local.ExecuteToState circuit
                member _.NativeStateType = local.NativeStateType

                member _.ApplyOperation operation _ =
                    match operation with
                    | QuantumOperation.Algorithm(AlgorithmOperation.QPE intent) ->
                        match intent.Unitary with
                        | QpeUnitary.ModularExponentiation(a, n) ->
                            let c = intent.CountingQubits
                            let swaps = intent.ApplySwaps

                            ModularExponentiationCircuit.buildModExpQpeAsGates a n c swaps
                            |> Result.bind (fun ops ->
                                local.InitializeState(ModularExponentiationCircuit.totalQubitsFor n c)
                                |> Result.bind (UnifiedBackend.applySequence local ops))
                        | other -> Error(QuantumError.OperationError("native test backend", $"unexpected %A{other}"))
                    | _ -> Error(QuantumError.OperationError("native test backend", "gate received"))

                member _.SupportsOperation operation = isAlgorithm operation
                member _.Name = "native modexp test backend"
                member _.InitializeState n = local.InitializeState n

                member this.ExecuteToStateAsync circuit _ =
                    Threading.Tasks.Task.FromResult(this.ExecuteToState circuit)

                member this.ApplyOperationAsync operation state _ =
                    Threading.Tasks.Task.FromResult(this.ApplyOperation operation state)
            }

        let recording = RecordingBackend(native, None)

        match Shor.findPeriodQuantum 7 15 3 (recording :> IQuantumBackend) with
        | Ok result ->
            Assert.Equal(4, result.Period)
            Assert.Equal(1, recording.Applied.Length)
            Assert.True(recording.Applied |> List.forall isAlgorithm, "the native route must receive the intent")
            Assert.Equal(0, gateCount recording.Applied)
            Assert.Equal(0, recording.Submitted)
        | Error e -> Assert.Fail($"Native route failed: {e.Message}")

    [<Fact>]
    let ``Shor Route - local simulator runs the circuit gate by gate`` () =
        let recording = RecordingBackend(LocalBackend.LocalBackend(), None)

        match Shor.findPeriodQuantum 7 15 3 (recording :> IQuantumBackend) with
        | Ok result ->
            Assert.Equal(4, result.Period)
            Assert.Equal(0, recording.Submitted)
            Assert.True(gateCount recording.Applied > 0, "the local route applies gates")

            let isQpeIntent op =
                match op with
                | QuantumOperation.Algorithm(AlgorithmOperation.QPE _) -> true
                | _ -> false

            Assert.False(recording.Applied |> List.exists isQpeIntent, "LocalBackend declines the modexp intent")
        | Error e -> Assert.Fail($"Local route failed: {e.Message}")

    [<Fact>]
    let ``Shor Route - period finder builder factors 15 on a sampling cloud backend`` () =
        let backend = sampling 256 3

        let problem: QuantumPeriodFinder.PeriodFinderProblem =
            {
                Number = 15
                Base = Some 7
                Precision = 3
                MaxAttempts = 3
                Backend = Some(backend :> IQuantumBackend)
                Exactness = QPE.Exactness.Exact
                Shots = None
            }

        match QuantumPeriodFinder.solve problem with
        | Ok result ->
            Assert.True(result.Success, result.Message)
            Assert.Equal(ShorsTypes.FactorSource.QuantumPeriodFinding, result.FactorSource)
            Assert.Equal(4, result.Period)
            Assert.Equal<int list>([ 3; 5 ], (let (p, q) = result.Factors.Value in List.sort [ p; q ]))
        | Error e -> Assert.Fail($"Period finder on a whole-circuit backend failed: {e.Message}")

    /// Refuses everything: a result obtained with it ran no quantum circuit.
    type private UntouchableBackend() =
        let mutable touched = 0
        member _.Touched = touched

        interface IQuantumBackend with
            member _.ExecuteToState _ =
                touched <- touched + 1
                Error(QuantumError.OperationError("untouchable", "no circuit may run"))

            member _.NativeStateType = QuantumStateType.GateBased

            member _.ApplyOperation _ _ =
                touched <- touched + 1
                Error(QuantumError.OperationError("untouchable", "no circuit may run"))

            member _.SupportsOperation _ = true
            member _.Name = "untouchable"

            member _.InitializeState _ =
                touched <- touched + 1
                Error(QuantumError.OperationError("untouchable", "no circuit may run"))

            member this.ExecuteToStateAsync circuit _ =
                Threading.Tasks.Task.FromResult((this :> IQuantumBackend).ExecuteToState circuit)

            member this.ApplyOperationAsync operation state _ =
                Threading.Tasks.Task.FromResult((this :> IQuantumBackend).ApplyOperation operation state)

    [<Fact>]
    let ``Shor - a drawn base sharing a factor with N is reported as classical preprocessing`` () =
        // Half the bases in [2, 13] share a factor with 15. The backend refuses everything, so
        // any draw that reaches period finding errors, and any factors reported ran no circuit.
        let config: ShorsTypes.ShorsConfig =
            {
                NumberToFactor = 15
                RandomBase = None
                PrecisionQubits = 3
                MaxAttempts = 1
            }

        let classical =
            Seq.init 60 (fun _ ->
                let backend = UntouchableBackend()
                Shor.execute config (backend :> IQuantumBackend), backend.Touched)
            |> Seq.tryPick (function
                | Ok result, touched -> Some(result, touched)
                | Error _, _ -> None)

        match classical with
        | Some(result, touched) ->
            Assert.Equal(0, touched)
            Assert.Equal(ShorsTypes.FactorSource.ClassicalPreprocessing, result.FactorSource)
            Assert.True(result.PeriodResult.IsNone, "no period was measured")
            Assert.Contains("classically", result.Message)
            Assert.Contains("no quantum period finding ran", result.Message)
        | None -> Assert.Fail("60 draws produced no base sharing a factor with 15")

    [<Fact>]
    let ``Shor - an even N is reported as classical preprocessing`` () =
        let config: ShorsTypes.ShorsConfig =
            {
                NumberToFactor = 14
                RandomBase = None
                PrecisionQubits = 3
                MaxAttempts = 1
            }

        match Shor.execute config (UntouchableBackend() :> IQuantumBackend) with
        | Ok result ->
            Assert.Equal(ShorsTypes.FactorSource.ClassicalPreprocessing, result.FactorSource)
            Assert.Equal(Some(2, 7), result.Factors)
        | Error e -> Assert.Fail(e.Message)

    // ========================================================================
    // HHL
    // ========================================================================

    let private diagonalConfig (eigenvalues: float[]) (b: Complex[]) =
        match HHLTypes.createDiagonalMatrix eigenvalues, HHLTypes.createQuantumVector b with
        | Ok m, Ok v -> (HHLTypes.defaultConfig m v) |> Result.defaultWith (fun e -> failwith e.Message)
        | _ -> failwith "invalid HHL input"

    [<Fact>]
    let ``HHL Route - sampling cloud backend solves with one job and returns measured magnitudes`` () =
        let backend = sampling 20000 2

        let config =
            diagonalConfig [| 2.0; 4.0 |] [| Complex(0.6, 0.0); Complex(-0.8, 0.0) |]

        match HHL.execute config (backend :> IQuantumBackend) with
        | Ok result ->
            Assert.Equal(HHLTypes.HhlReadout.MeasuredMagnitudes, result.Readout)
            Assert.Equal(1, backend.Submitted)
            // x ∝ (0.3, -0.2): magnitudes (0.832, 0.555); the sign is not in the counts.
            Assert.InRange(result.Solution.[0].Real, 0.80, 0.86)
            Assert.InRange(result.Solution.[1].Real, 0.52, 0.59)
            Assert.True(result.Solution |> Array.forall (fun c -> c.Real >= 0.0 && c.Imaginary = 0.0))
        | Error e -> Assert.Fail($"HHL on a whole-circuit backend failed: {e.Message}")

    [<Fact>]
    let ``HHL Route - whole circuit matches the local amplitudes in magnitude`` () =
        // Exact whole-circuit backend: the Möttönen preparation plus the inversion lowering
        // must reproduce the simulator's post-selected state up to signs.
        let config =
            diagonalConfig
                [| 1.0; 2.0; 4.0; 8.0 |]
                [|
                    Complex(0.5, 0.0)
                    Complex(-0.5, 0.0)
                    Complex(0.5, 0.0)
                    Complex(0.5, 0.0)
                |]

        match HHL.execute config (local ()), HHL.execute config (exact () :> IQuantumBackend) with
        | Ok local, Ok whole ->
            Assert.Equal(HHLTypes.HhlReadout.Amplitudes, local.Readout)
            Assert.Equal(HHLTypes.HhlReadout.MeasuredMagnitudes, whole.Readout)
            Assert.Equal(local.SuccessProbability, whole.SuccessProbability, 9)

            for i in 0..3 do
                Assert.Equal(local.Solution.[i].Magnitude, whole.Solution.[i].Real, 9)
        | Error e, _
        | _, Error e -> Assert.Fail(e.Message)

    [<Fact>]
    let ``HHL Route - general matrix whole circuit matches the local route in magnitude`` () =
        let hermitian =
            array2D
                [
                    [ Complex(1.0, 0.0); Complex(0.5, 0.0) ]
                    [ Complex(0.5, 0.0); Complex(1.0, 0.0) ]
                ]

        let config =
            match
                HHLTypes.createHermitianMatrix hermitian,
                HHLTypes.createQuantumVector [| Complex(1.0, 0.0); Complex.Zero |]
            with
            | Ok m, Ok v ->
                match HHLTypes.defaultConfig m v with
                | Ok c ->
                    { c with
                        EigenvalueQubits = 2
                        QPEPrecision = 2
                    }
                | Error e -> failwith e.Message
            | _ -> failwith "invalid HHL input"

        match HHL.execute config (local ()), HHL.execute config (exact () :> IQuantumBackend) with
        | Ok local, Ok whole ->
            Assert.Equal(local.SuccessProbability, whole.SuccessProbability, 6)

            for i in 0..1 do
                Assert.Equal(local.Solution.[i].Magnitude, whole.Solution.[i].Real, 6)
        | Error e, _
        | _, Error e -> Assert.Fail(e.Message)

    /// a and b equal up to one global phase: |⟨a|b⟩| = ‖a‖·‖b‖.
    let private assertSameUpToGlobalPhase (tolerance: float) (a: Complex[]) (b: Complex[]) =
        let overlap =
            Array.fold2 (fun acc (x: Complex) y -> acc + Complex.Conjugate x * y) Complex.Zero a b

        let phase = Complex.FromPolarCoordinates(1.0, overlap.Phase)

        for i in 0 .. a.Length - 1 do
            Assert.True((a.[i] * phase - b.[i]).Magnitude < tolerance, $"component {i}: {a.[i] * phase} vs {b.[i]}")

    [<Fact>]
    let ``HHL Route - executeWithRelativePhases measures the signs on a sampling cloud backend`` () =
        let backend = sampling 20000 5

        let config =
            diagonalConfig [| 2.0; 4.0 |] [| Complex(0.6, 0.0); Complex(-0.8, 0.0) |]

        match HHL.executeWithRelativePhases config (backend :> IQuantumBackend), HHL.execute config (local ()) with
        | Ok phased, Ok exact ->
            Assert.Equal(HHLTypes.HhlReadout.MeasuredRelativePhases, phased.Result.Readout)
            // Magnitude circuit + one Hadamard circuit (one solution qubit, real system).
            Assert.Equal(2, phased.Circuits)
            Assert.Equal(2, backend.Submitted)
            // x ∝ (0.3, -0.2): opposite signs, measured.
            Assert.True(phased.Result.Solution.[0].Real * phased.Result.Solution.[1].Real < 0.0)
            assertSameUpToGlobalPhase 0.02 exact.Solution phased.Result.Solution
        | Error e, _
        | _, Error e -> Assert.Fail(e.Message)

    [<Fact>]
    let ``HHL Route - executeWithRelativePhases recovers complex relative phases (exact whole circuit)`` () =
        // A complex Hermitian system needs the Y-basis circuits too: 1 + 2·2 = 5 jobs.
        let config =
            diagonalConfig
                [| 1.0; 2.0; 4.0; 8.0 |]
                [|
                    Complex(0.5, 0.0)
                    Complex(0.0, -0.5)
                    Complex(-0.5, 0.0)
                    Complex(0.3, 0.4)
                |]

        let backend = exact ()

        match HHL.executeWithRelativePhases config (backend :> IQuantumBackend), HHL.execute config (local ()) with
        | Ok phased, Ok exactResult ->
            Assert.Equal(HHLTypes.HhlReadout.MeasuredRelativePhases, phased.Result.Readout)
            Assert.Equal(5, phased.Circuits)
            Assert.Equal(5, backend.Submitted)
            assertSameUpToGlobalPhase 1e-6 exactResult.Solution phased.Result.Solution
        | Error e, _
        | _, Error e -> Assert.Fail(e.Message)

    [<Fact>]
    let ``HHL Route - executeWithRelativePhases on a simulator is execute and submits nothing`` () =
        let config =
            diagonalConfig [| 2.0; 4.0 |] [| Complex(0.6, 0.0); Complex(-0.8, 0.0) |]

        match HHL.executeWithRelativePhases config (local ()), HHL.execute config (local ()) with
        | Ok phased, Ok plain ->
            Assert.Equal(HHLTypes.HhlReadout.Amplitudes, phased.Result.Readout)
            Assert.Equal(0, phased.Circuits)
            Assert.Equal<Complex[]>(plain.Solution, phased.Result.Solution)
        | Error e, _
        | _, Error e -> Assert.Fail(e.Message)

    [<Fact>]
    let ``HHL Route - real cloud backend classes submit the circuit as a job`` () =
        for name, backend in realCloudBackends () do
            assertReachedSubmission
                name
                (HHL.solve2x2Diagonal (2.0, 4.0) (Complex(0.6, 0.0), Complex(0.8, 0.0)) backend)

    [<Fact>]
    let ``HHL Route - a backend claiming the HHL intent receives it and never a gate`` () =
        // LocalBackend realises the diagonal HHL intent; the topological backend's own test
        // pins the same route there.
        let recording = RecordingBackend(LocalBackend.LocalBackend(), None)

        let config =
            diagonalConfig [| 2.0; 4.0 |] [| Complex(0.6, 0.0); Complex(0.8, 0.0) |]

        match HHL.execute config (recording :> IQuantumBackend) with
        | Ok result ->
            Assert.Equal(HHLTypes.HhlReadout.Amplitudes, result.Readout)
            Assert.True(recording.Applied |> List.forall isAlgorithm, "the native route must receive the intent only")
            Assert.Equal(1, recording.Applied.Length)
            Assert.Equal(0, recording.Submitted)
        | Error e -> Assert.Fail(e.Message)

    [<Fact>]
    let ``HHL Route - linear system solver passes the measured readout through`` () =
        let config =
            diagonalConfig [| 2.0; 4.0 |] [| Complex(0.6, 0.0); Complex(0.8, 0.0) |]

        let problem: QuantumLinearSystemSolver.LinearSystemProblem =
            {
                Matrix = config.Matrix
                InputVector = config.InputVector
                EigenvalueQubits = config.EigenvalueQubits
                InversionMethod = config.InversionMethod
                MinEigenvalue = config.MinEigenvalue
                UsePostSelection = true
                Backend = Some(sampling 4000 9 :> IQuantumBackend)
                Shots = None
            }

        match QuantumLinearSystemSolver.solve problem with
        | Ok solution ->
            Assert.Equal(HHLTypes.HhlReadout.MeasuredMagnitudes, solution.Readout)
            Assert.True(solution.Success, solution.Message)
        | Error e -> Assert.Fail(e.Message)

    [<Fact>]
    let ``HHL Route - regression measures the signs by interference circuits rather than guess them`` () =
        // y = 2·x1 − x2: a negative weight, which magnitudes alone cannot give.
        let x =
            [|
                [| 1.0; 0.2 |]
                [| 0.1; 1.0 |]
                [| 0.9; -0.1 |]
                [| -0.2; 0.8 |]
                [| 0.5; 0.4 |]
                [| 0.3; -0.6 |]
            |]

        let config: FSharp.Azure.Quantum.MachineLearning.QuantumRegressionHHL.RegressionConfig =
            {
                TrainX = x
                TrainY = x |> Array.map (fun row -> 2.0 * row.[0] - row.[1])
                EigenvalueQubits = 6
                MinEigenvalue = 1e-6
                Backend = sampling 20000 1 :> IQuantumBackend
                Shots = 1000
                FitIntercept = false
                Verbose = false
                Logger = None
            }

        match FSharp.Azure.Quantum.MachineLearning.QuantumRegressionHHL.train config with
        | Ok result ->
            // Magnitude circuit + one Hadamard circuit for the single solution qubit.
            Assert.Equal(2, result.Circuits)
            Assert.InRange(result.Weights.[0], 1.9, 2.1)
            Assert.InRange(result.Weights.[1], -1.1, -0.9)
        | Error e -> Assert.Fail(e.Message)

    // ========================================================================
    // QUANTUM ARITHMETIC
    // ========================================================================

    [<Fact>]
    let ``Arithmetic Route - builder operations run as one whole-circuit job each`` () =
        let cases =
            [
                QuantumArithmeticOps.add 3 4 4, 7
                QuantumArithmeticOps.modularAdd 3 4 5 4, 2
                QuantumArithmeticOps.modularMultiply 2 3 5 4, 1
                QuantumArithmeticOps.modularExponentiate 2 2 5 3, 4
            ]

        for operation, expected in cases do
            let backend = sampling 64 4

            match
                QuantumArithmeticOps.execute
                    { operation with
                        Backend = Some(backend :> IQuantumBackend)
                    }
            with
            | Ok result ->
                Assert.Equal(expected, result.Value)
                Assert.Equal(1, backend.Submitted)
            | Error e -> Assert.Fail($"{operation.Operation} on a whole-circuit backend failed: {e.Message}")

    [<Fact>]
    let ``Arithmetic Route - operations on |0> submit one circuit, other inputs are refused`` () =
        let backend = sampling 64 6
        let b = backend :> IQuantumBackend
        let zero = b.InitializeState 3 |> Result.defaultWith (fun e -> failwith e.Message)

        match Arithmetic.addConstant [ 0; 1; 2 ] 5 zero b with
        | Ok result ->
            Assert.Equal(1, backend.Submitted)
            Assert.Equal(1.0, QuantumState.probability [| 1; 0; 1 |] result.State, 9)
        | Error e -> Assert.Fail(e.Message)

        // A job starts from |0…0⟩, so a prepared state cannot be continued.
        let simulator = local ()

        let prepared =
            simulator.InitializeState 3
            |> Result.bind (simulator.ApplyOperation(QuantumOperation.Gate(CircuitBuilder.X 0)))
            |> Result.defaultWith (fun e -> failwith e.Message)

        match Arithmetic.addConstant [ 0; 1; 2 ] 5 prepared b with
        | Ok _ -> Assert.Fail("A non-|0> input cannot be submitted as a job")
        | Error e -> Assert.Contains("|0…0⟩", e.Message)

    [<Fact>]
    let ``Arithmetic Route - local simulator keeps gate-by-gate application`` () =
        let recording = RecordingBackend(LocalBackend.LocalBackend(), None)

        match
            QuantumArithmeticOps.execute
                { QuantumArithmeticOps.modularAdd 3 4 5 4 with
                    Backend = Some(recording :> IQuantumBackend)
                }
        with
        | Ok result ->
            Assert.Equal(2, result.Value)
            Assert.Equal(0, recording.Submitted)
            Assert.True(gateCount recording.Applied > 0)
        | Error e -> Assert.Fail(e.Message)

    [<Fact>]
    let ``Arithmetic Route - recorded modular multiplication holds gates only`` () =
        match ModularExponentiationCircuit.buildControlledModularMultiplication 0 [ 1; 2; 3 ] 2 5 with
        | Ok ops ->
            Assert.NotEmpty ops
            Assert.True(ops |> List.forall isGate, "the recorder keeps probes and intents out of the circuit")
        | Error e -> Assert.Fail(e.Message)

    // ========================================================================
    // AMPLITUDE AMPLIFICATION
    // ========================================================================

    let private amplificationIntent
        (prep: CircuitBuilder.Circuit)
        : AmplitudeAmplification.Unified.AmplitudeAmplificationIntent =
        {
            NumQubits = 3
            StatePreparation = prep
            Oracle = Oracle.forValue 5 3 |> Result.defaultWith (fun e -> failwith e.Message)
            Iterations = 1
            Exactness = AmplitudeAmplification.Unified.Exact
        }

    let private prepOf (gates: CircuitBuilder.Gate list) =
        gates
        |> List.fold (fun c g -> CircuitBuilder.addGate g c) (CircuitBuilder.empty 3)

    [<Fact>]
    let ``AmplitudeAmplification Route - whole circuit matches the local state`` () =
        let preparations =
            [
                prepOf [ CircuitBuilder.H 0; CircuitBuilder.H 1; CircuitBuilder.H 2 ]
                prepOf [ CircuitBuilder.RY(0, 1.1); CircuitBuilder.H 1; CircuitBuilder.RY(2, 0.7) ]
            ]

        for prep in preparations do
            let intent = amplificationIntent prep

            match
                AmplitudeAmplification.Unified.execute (local ()) intent,
                AmplitudeAmplification.Unified.execute (exact () :> IQuantumBackend) intent
            with
            | Ok local, Ok whole ->
                for index in 0..7 do
                    let bits = Array.init 3 (fun q -> (index >>> q) &&& 1)
                    Assert.Equal(QuantumState.probability bits local, QuantumState.probability bits whole, 9)
            | Error e, _
            | _, Error e -> Assert.Fail(e.Message)

    [<Fact>]
    let ``AmplitudeAmplification Route - refused Grover intents fall back to the whole circuit`` () =
        let backend = WholeCircuitBackend(None, 0, claimsEverything)

        let intent =
            amplificationIntent (prepOf [ CircuitBuilder.H 0; CircuitBuilder.H 1; CircuitBuilder.H 2 ])

        match AmplitudeAmplification.Unified.execute (backend :> IQuantumBackend) intent with
        | Ok state ->
            Assert.Equal(1, backend.Submitted)
            // One iteration on 8 states with one marked: sin²(3·asin(1/√8)) = 0.78125.
            Assert.Equal(0.78125, QuantumState.probability [| 1; 0; 1 |] state, 9)
        | Error e -> Assert.Fail(e.Message)

    // ========================================================================
    // TEXTBOOK PROTOCOLS
    // ========================================================================

    [<Fact>]
    let ``Protocols Route - oracle and Bell protocols run as one job on a sampling backend`` () =
        let run (f: IQuantumBackend -> Result<'T, QuantumError>) (check: 'T -> unit) =
            let backend = sampling 400 8

            match f (backend :> IQuantumBackend) with
            | Ok value ->
                check value
                Assert.Equal(1, backend.Submitted)
            | Error e -> Assert.Fail(e.Message)

        run BellStates.createPsiMinus (fun r ->
            Assert.Equal(0.0, QuantumState.probability [| 0; 0 |] r.QuantumState)
            Assert.InRange(QuantumState.probability [| 1; 0 |] r.QuantumState, 0.4, 0.6))

        run (fun b -> BernsteinVazirani.runWithSecret [| 1; 0; 1; 1 |] b 50) (fun r ->
            Assert.Equal<int[]>([| 1; 0; 1; 1 |], r.RecoveredSecret))

        run (fun b -> DeutschJozsa.runBalancedParity 3 b 50) (fun r ->
            Assert.Equal(DeutschJozsa.Balanced, r.OracleType))

        run (fun b -> DeutschJozsa.runConstantZero 3 b 50) (fun r -> Assert.Equal(DeutschJozsa.Constant, r.OracleType))

        run (fun b -> Simon.runWithSecret [| 1; 1; 0 |] b 60) (fun r ->
            Assert.Equal<int[]>([| 1; 1; 0 |], r.RecoveredSecret))

        run SuperdenseCoding.send11 (fun r -> Assert.True(r.Success, $"sent 11, received %A{r.ReceivedMessage}"))

    [<Fact>]
    let ``Protocols Route - an opaque oracle cannot be submitted and is an Error`` () =
        let local = LocalBackend.LocalBackend() :> IQuantumBackend
        // A function the whole-circuit route cannot see into.
        let opaque: DeutschJozsa.Oracle =
            fun state -> local.ApplyOperation (QuantumOperation.Gate(CircuitBuilder.Z 0)) state

        match DeutschJozsa.run opaque 3 (sampling 50 1 :> IQuantumBackend) 50 with
        | Ok _ -> Assert.Fail("An opaque oracle has no gates to submit")
        | Error e -> Assert.Contains("opaque", e.Message)

    [<Fact>]
    let ``Protocols Route - Bell measurement of a prepared state is refused on whole-circuit backends`` () =
        let backend = sampling 100 2 :> IQuantumBackend

        match BellStates.createPhiPlus backend with
        | Ok bell ->
            match BellStates.measureBellBasis bell.QuantumState backend with
            | Ok _ -> Assert.Fail("A measured state cannot be continued in another job")
            | Error e -> Assert.Contains("|0…0⟩", e.Message)
        | Error e -> Assert.Fail(e.Message)

    [<Fact>]
    let ``Protocols Route - teleportation runs whole circuits with tomographic fidelity`` () =
        for name, teleport in
            [
                "one", QuantumTeleportation.teleportOne
                "plus", QuantumTeleportation.teleportPlus
                "minus", QuantumTeleportation.teleportMinus
                "zero", QuantumTeleportation.teleportZero
            ] do
            let backend = sampling 2000 13

            match teleport (backend :> IQuantumBackend) with
            | Ok result ->
                Assert.True(result.Fidelity > 0.95, $"{name}: fidelity {result.Fidelity}")
                // Bob's qubit measured in the Z, X and Y bases by three copies side by side in
                // one 9-qubit circuit: one job per call.
                Assert.Equal(1, backend.Submitted)
                Assert.Equal<int list>([ 9 ], backend.Widths)
                Assert.Equal(3, QuantumState.numQubits result.BobState)
            | Error e -> Assert.Fail($"{name}: {e.Message}")

        let local = LocalBackend.LocalBackend() :> IQuantumBackend

        let prepared =
            local.InitializeState 3
            |> Result.bind (local.ApplyOperation(QuantumOperation.Gate(CircuitBuilder.H 0)))
            |> Result.defaultWith (fun e -> failwith e.Message)

        match QuantumTeleportation.teleportArbitrary prepared (sampling 100 1 :> IQuantumBackend) with
        | Ok _ -> Assert.Fail("A prepared state cannot be loaded into a job")
        | Error e -> Assert.Contains("|0…0⟩", e.Message)

    [<Fact>]
    let ``Protocols Route - BB84 runs every transmission on the backend and detects Eve`` () =
        let honest = sampling 1 21

        match QuantumKeyDistribution.runBB84 40 (honest :> IQuantumBackend) 0.3 0.11 (Some 4) with
        | Ok result ->
            Assert.Equal(0.0, result.EavesdropCheck.ErrorRate)
            Assert.False(result.EavesdropCheck.EavesdropDetected)
            Assert.Equal(result.InitialKeyLength, honest.Widths |> List.sum)
        | Error e -> Assert.Fail(e.Message)

        match QuantumKeyDistribution.runBB84WithEve 150 (sampling 1 22 :> IQuantumBackend) 0.3 0.11 (Some 4) with
        | Ok result -> Assert.True(result.EavesdropCheck.EavesdropDetected, $"QBER {result.EavesdropCheck.ErrorRate}")
        | Error e -> Assert.Fail(e.Message)

    [<Fact>]
    let ``Protocols Route - E91 violates CHSH without Eve and not with her`` () =
        match EkertQKD.run (sampling 1 31 :> IQuantumBackend) 600 (Some 7) with
        | Ok result -> Assert.True(result.CHSHTest.S > 2.2, $"S = {result.CHSHTest.S}")
        | Error e -> Assert.Fail(e.Message)

        match EkertQKD.runWithEve (sampling 1 32 :> IQuantumBackend) 600 (Some 7) with
        | Ok result ->
            Assert.True(abs result.CHSHTest.S < 2.0, $"S = {result.CHSHTest.S}")
            Assert.False(result.IsSecure)
        | Error e -> Assert.Fail(e.Message)

    [<Fact>]
    let ``Protocols Route - BB84 and E91 use every shot of a job`` () =
        // A 200-bit BB84 key sends 480 transmissions of at most 8 kinds (16 with Eve); at 100
        // shots a job, one slot per kind covers each kind's demand.
        let honest = sampling 100 41

        match QuantumKeyDistribution.runBB84 200 (honest :> IQuantumBackend) 0.15 0.11 (Some 5) with
        | Ok result ->
            Assert.Equal(0.0, result.EavesdropCheck.ErrorRate)
            Assert.Equal(1, honest.Submitted)
        | Error e -> Assert.Fail(e.Message)

        let attacked = sampling 100 42

        match QuantumKeyDistribution.runBB84WithEve 200 (attacked :> IQuantumBackend) 0.15 0.11 (Some 5) with
        | Ok result ->
            Assert.True(result.EavesdropCheck.EavesdropDetected, $"QBER {result.EavesdropCheck.ErrorRate}")
            Assert.True(attacked.Submitted <= 2, $"{attacked.Submitted} jobs")
            Assert.True(attacked.Widths |> List.forall (fun w -> w <= WholeCircuit.MaxTrialWidth))
        | Error e -> Assert.Fail(e.Message)

        // 600 E91 pairs: 9 kinds of two qubits (36 of four with Eve).
        let pairs = sampling 100 43

        match EkertQKD.run (pairs :> IQuantumBackend) 600 (Some 7) with
        | Ok result ->
            Assert.True(result.CHSHTest.S > 2.2, $"S = {result.CHSHTest.S}")
            Assert.Equal(2, pairs.Submitted)
        | Error e -> Assert.Fail(e.Message)

        let intercepted = sampling 100 44

        match EkertQKD.runWithEve (intercepted :> IQuantumBackend) 600 (Some 7) with
        | Ok result ->
            Assert.True(abs result.CHSHTest.S < 2.0, $"S = {result.CHSHTest.S}")
            Assert.True(intercepted.Submitted <= 9, $"{intercepted.Submitted} jobs")
        | Error e -> Assert.Fail(e.Message)

    [<Fact>]
    let ``Protocols Route - BB84 and E91 run on the 8-qubit density-matrix simulator`` () =
        let noisy () =
            Backends.DensityMatrixSimulator.NoisyLocalBackend(Backends.DensityMatrixSimulator.noiseless)
            :> IQuantumBackend

        match QuantumKeyDistribution.runBB84 60 (noisy ()) 0.2 0.11 (Some 3) with
        | Ok result -> Assert.Equal(0.0, result.EavesdropCheck.ErrorRate)
        | Error e -> Assert.Fail(e.Message)

        // Outcomes are drawn afresh each run: 800 key bits give a check sample large enough
        // that a missed eavesdropper is more than six standard deviations out.
        match QuantumKeyDistribution.runBB84WithEve 800 (noisy ()) 0.2 0.11 (Some 3) with
        | Ok result -> Assert.True(result.EavesdropCheck.EavesdropDetected, $"QBER {result.EavesdropCheck.ErrorRate}")
        | Error e -> Assert.Fail(e.Message)

        match EkertQKD.run (noisy ()) 400 (Some 9) with
        | Ok result -> Assert.True(result.CHSHTest.S > 2.2, $"S = {result.CHSHTest.S}")
        | Error e -> Assert.Fail(e.Message)

        match EkertQKD.runWithEve (noisy ()) 400 (Some 9) with
        | Ok result -> Assert.True(abs result.CHSHTest.S < 2.0, $"S = {result.CHSHTest.S}")
        | Error e -> Assert.Fail(e.Message)

    [<Fact>]
    let ``Protocols Route - Eve's two E91 measurements are one joint measurement on every route`` () =
        // Eve measures |Φ+⟩ in one basis for both qubits, so her two outcomes agree and she
        // resends |ee⟩: Alice and Bob measuring both in Z always agree. Drawing her outcomes
        // as two independent samples made them disagree about half the time.
        for backend in [ local (); sampling 100 51 :> IQuantumBackend ] do
            match EkertQKD.runWithEve backend 300 (Some 11) with
            | Ok result ->
                let zz =
                    result.Pairs
                    |> List.filter (fun p -> p.AliceBasis = EkertQKD.AliceDeg0 && p.BobBasis = EkertQKD.BobDeg0)

                Assert.NotEmpty zz
                Assert.All(zz, fun p -> Assert.Equal(p.AliceResult, p.BobResult))
            | Error e -> Assert.Fail($"{backend.Name}: {e.Message}")

    [<Fact>]
    let ``Protocols Route - a closure that returns an empty probe unchanged is still opaque`` () =
        // Identity on the state it is probed with, a phase flip on anything else: judged by a
        // probe it looked like the identity and was submitted as no gates, so a balanced
        // oracle was reported constant.
        let local = LocalBackend.LocalBackend() :> IQuantumBackend

        let contrived: DeutschJozsa.Oracle =
            fun state ->
                match state with
                | QuantumState.SparseState(amplitudes, _) when Map.isEmpty amplitudes -> Ok state
                | _ -> local.ApplyOperation (QuantumOperation.Gate(CircuitBuilder.Z 0)) state

        match DeutschJozsa.run contrived 3 (sampling 50 1 :> IQuantumBackend) 50 with
        | Ok r -> Assert.Fail($"An opaque oracle has no gates to submit; got {r.OracleType}")
        | Error e -> Assert.Contains("opaque", e.Message)

        // The module's identity oracles are registered, and still run as no gates.
        match DeutschJozsa.run DeutschJozsa.constantOneOracle 3 (sampling 50 2 :> IQuantumBackend) 50 with
        | Ok r -> Assert.Equal(DeutschJozsa.Constant, r.OracleType)
        | Error e -> Assert.Fail(e.Message)

    [<Fact>]
    let ``Protocols Route - repetition codes correct every single error with deferred measurement`` () =
        for logicalBit in [ 0; 1 ] do
            for errorQubit in [ None; Some 0; Some 1; Some 2 ] do
                let expectedSyndrome =
                    match errorQubit with
                    | None -> [ 0; 0 ]
                    | Some 0 -> [ 1; 0 ]
                    | Some 1 -> [ 1; 1 ]
                    | _ -> [ 0; 1 ]

                for name, roundTrip in
                    [
                        "bit-flip", QuantumErrorCorrection.BitFlip.roundTrip
                        "phase-flip", QuantumErrorCorrection.PhaseFlip.roundTrip
                    ] do
                    let backend = sampling 16 (logicalBit * 10 + defaultArg errorQubit 5)

                    match roundTrip (backend :> IQuantumBackend) logicalBit errorQubit with
                    | Ok result ->
                        Assert.True(result.Success, $"%s{name} bit %d{logicalBit} error %A{errorQubit}")
                        Assert.Equal<int list>(expectedSyndrome, result.Syndrome.SyndromeBits)
                        Assert.Equal(1, backend.Submitted)
                    | Error e -> Assert.Fail($"{name}: {e.Message}")

    [<Fact>]
    let ``Protocols Route - the Steane round trip is an explicit Error on whole-circuit backends`` () =
        let backend = sampling 16 1

        match
            QuantumErrorCorrection.Steane.roundTrip (backend :> IQuantumBackend) 1 QuantumErrorCorrection.BitFlipError 2
        with
        | Ok _ -> Assert.Fail("Steane has no whole-circuit round trip")
        | Error e ->
            Assert.Contains("not implemented", e.Message)
            Assert.Equal(0, backend.Submitted)

    // ========================================================================
    // QFT
    // ========================================================================

    [<Fact>]
    let ``QFT Route - basis-state transform submits its preparation inside the circuit`` () =
        let backend = exact ()

        match
            QFT.transformBasisState 3 5 (local ()) QFT.defaultConfig,
            QFT.transformBasisState 3 5 (backend :> IQuantumBackend) QFT.defaultConfig
        with
        | Ok local, Ok whole ->
            Assert.Equal(1, backend.Submitted)

            match local.FinalState, whole.FinalState with
            | QuantumState.StateVector a, QuantumState.StateVector b ->
                for i in 0..7 do
                    let d = StateVector.getAmplitude i a - StateVector.getAmplitude i b
                    Assert.True(d.Magnitude < 1e-9, $"amplitude {i} differs")
            | _ -> Assert.Fail("expected state vectors")
        | Error e, _
        | _, Error e -> Assert.Fail(e.Message)

    // ========================================================================
    // GROVER AND TREE SEARCH
    // ========================================================================

    [<Fact>]
    let ``Grover - unknown solution count always runs at least one iteration`` () =
        let backend = LocalBackend.LocalBackend() :> IQuantumBackend

        for predicate in [ (fun x -> x % 2 = 1); (fun x -> x % 4 <> 0); (fun x -> x = 11) ] do
            let oracle =
                Oracle.fromPredicate predicate 4
                |> Result.defaultWith (fun e -> failwith e.Message)

            match Grover.search oracle backend Grover.defaultConfig with
            | Ok result ->
                Assert.True(result.Iterations >= 1, $"ran {result.Iterations} iterations")
                Assert.NotEmpty result.Solutions
                Assert.True(result.Solutions |> List.forall predicate)
            | Error e -> Assert.Fail(e.Message)

    [<Fact>]
    let ``Grover - a cloud job reports the shots it measured, not the shots requested`` () =
        let oracle = Oracle.forValue 5 3 |> Result.defaultWith (fun e -> failwith e.Message)

        let backend = sampling 500 17

        match
            Grover.search
                oracle
                (backend :> IQuantumBackend)
                { Grover.defaultConfig with
                    Shots = 1000
                }
        with
        | Ok result ->
            Assert.Equal(500, result.Measurements |> Map.toSeq |> Seq.sumBy snd)
            Assert.Equal<int list>([ 5 ], result.Solutions)
        | Error e -> Assert.Fail(e.Message)

    [<Fact>]
    let ``Measurement - a job's state yields its recorded shots, never resampled ones`` () =
        let state = CloudBackendHelpers.histogramToQuantumState (Map [ "01", 3; "10", 1 ]) 2

        // Recorded counts in the QuantumState convention (character q = qubit q).
        Assert.Equal(Some(Map [ "10", 3; "01", 1 ]), QuantumState.tryRecordedCounts state)
        Assert.Equal(Some 4, QuantumState.recordedShotCount state)

        // More shots than recorded: every recorded shot once, and no more.
        let all = UnifiedBackend.measureState state 1000
        Assert.Equal(4, all.Length)
        Assert.Equal(3, all |> Array.filter (fun b -> b = [| 1; 0 |]) |> Array.length)
        Assert.Equal(1, all |> Array.filter (fun b -> b = [| 0; 1 |]) |> Array.length)

        // The order is a function of the counts: the same job result (or another with the
        // same counts) yields the same shots in the same order, run after run.
        Assert.Equal<int[][]>(all, UnifiedBackend.measureState state 1000)

        let again = CloudBackendHelpers.histogramToQuantumState (Map [ "01", 3; "10", 1 ]) 2
        Assert.Equal<int[][]>(all, UnifiedBackend.measureState again 1000)

        // Fewer: a subset drawn without replacement, so "01" appears at most once.
        for _ in 1..50 do
            let two = UnifiedBackend.measureState state 2
            Assert.Equal(2, two.Length)
            Assert.True((two |> Array.filter (fun b -> b = [| 0; 1 |]) |> Array.length) <= 1)

        // A computed state is sampled afresh, as many times as asked.
        let local = LocalBackend.LocalBackend() :> IQuantumBackend

        let plus =
            local.InitializeState 1
            |> Result.bind (local.ApplyOperation(QuantumOperation.Gate(CircuitBuilder.H 0)))

        match plus with
        | Ok s ->
            Assert.Equal(None, QuantumState.tryRecordedCounts s)
            Assert.Equal(1000, (UnifiedBackend.measureState s 1000).Length)
        | Error e -> Assert.Fail(e.Message)

    [<Fact>]
    let ``TreeSearch - marks the sampled top percentile, not a lowered bar`` () =
        // Root 1; moves +1, +2, ×2; depth 2 → leaf scores {3,4,4,4,5,6,3,4,4} on 9 legal paths
        // of 16. The top-20% bar is at least 4, so a leaf scoring 3 is never a solution. The
        // lowered bar marked those too — 9 of 16 paths — and one Grover iteration then put the
        // marked probability below uniform sampling.
        let config: TreeSearch.TreeSearchConfig<int> =
            {
                MaxDepth = 2
                BranchingFactor = 4
                EvaluationFunction = float
                MoveGenerator = fun s -> [ s + 1; s + 2; s * 2 ]
            }

        let leafScore encoded =
            TreeSearch.decodeTreePosition encoded 4 2
            |> List.fold (fun s move -> (config.MoveGenerator s).[move]) 1

        for _ in 1..5 do
            match TreeSearch.searchGameTree 1 config (local ()) 0.2 None None None None with
            | Ok result ->
                Assert.True(result.GroverIterations >= 1)
                Assert.NotEmpty result.AllSolutions
                Assert.True(result.AllSolutions |> List.forall (fun s -> leafScore s >= 4), $"%A{result.AllSolutions}")
            | Error e -> Assert.Fail(e.Message)
