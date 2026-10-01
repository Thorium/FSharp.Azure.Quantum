namespace FSharp.Azure.Quantum.Tests

open Xunit
open FSharp.Azure.Quantum.Core
open FSharp.Azure.Quantum.Core.BackendAbstraction
open FSharp.Azure.Quantum.Algorithms.QuantumMonteCarlo
open FSharp.Azure.Quantum
open FSharp.Azure.Quantum.Backends
open System.Threading
open System.Threading.Tasks
open FSharp.Azure.Quantum.LocalSimulator

/// A backend shaped like cloud hardware, for pinning whole-circuit routes: ApplyOperation is
/// refused with the incremental error, ExecuteToState runs the circuit on the local simulator,
/// draws `shots` seeded samples and returns CloudBackendHelpers.histogramToQuantumState, a
/// phase-less state of outcome frequencies, and the backend reports Shots.
module SampledWholeCircuit =

    type Backend(shots: int, seed: int) =
        let inner = LocalBackend.LocalBackend() :> IQuantumBackend
        let rng = System.Random seed

        let incremental: Result<QuantumState, QuantumError> =
            Error(
                QuantumError.OperationError(
                    "ApplyOperation",
                    "Sampled test backend does not support incremental ApplyOperation. Use ExecuteToState with a complete circuit instead."
                )
            )

        member val Executed = 0 with get, set

        interface IShotSamplingBackend with
            member _.Shots = shots

        interface IQuantumBackend with
            member this.ExecuteToState circuit =
                this.Executed <- this.Executed + 1

                match inner.ExecuteToState circuit with
                | Ok(QuantumState.StateVector sv) ->
                    let p = Measurement.getProbabilityDistribution sv
                    let n = StateVector.numQubits sv
                    let cumulative = Array.scan (+) 0.0 p |> Array.tail
                    let counts = Array.zeroCreate p.Length

                    for _ in 1..shots do
                        let u = rng.NextDouble()
                        let k = defaultArg (Array.tryFindIndex (fun c -> u < c) cumulative) (p.Length - 1)
                        counts.[k] <- counts.[k] + 1

                    // Azure histogram keys: rightmost character = qubit 0.
                    let histogram =
                        counts
                        |> Array.mapi (fun i c -> System.Convert.ToString(i, 2).PadLeft(n, '0'), c)
                        |> Array.filter (fun (_, c) -> c > 0)
                        |> Map.ofArray

                    Ok(CloudBackendHelpers.histogramToQuantumState histogram n)
                | other -> other

            member _.NativeStateType = inner.NativeStateType
            member _.ApplyOperation _ _ = incremental

            member _.SupportsOperation op =
                CloudBackendHelpers.isCloudSupportedOperation op

            member _.Name = "sampled whole-circuit test backend"
            member _.InitializeState n = inner.InitializeState n

            member this.ExecuteToStateAsync circuit _ =
                Task.FromResult((this :> IQuantumBackend).ExecuteToState circuit)

            member _.ApplyOperationAsync _ _ _ = Task.FromResult incremental

module QuantumMonteCarloTests =

    let private createBackend () =
        LocalBackend.LocalBackend() :> IQuantumBackend

    let private createSimpleConfig numQubits iterations shots =
        let statePrep =
            [ 0 .. numQubits - 1 ]
            |> List.fold (fun c q -> c |> CircuitBuilder.addGate (CircuitBuilder.H q)) (CircuitBuilder.empty numQubits)

        let oracle =
            CircuitBuilder.empty numQubits |> CircuitBuilder.addGate (CircuitBuilder.Z 0)

        {
            NumQubits = numQubits
            StatePreparation = statePrep
            Oracle = oracle
            GroverIterations = iterations
            Shots = shots
        }

    // ========================================================================
    // CORRECTNESS: amplitude estimation recovers a known marked probability
    // ========================================================================

    [<Fact>]
    let ``QAE recovers a known non-uniform marked amplitude`` () =
        // 2 qubits, 4 bins, non-uniform distribution p = [0.1; 0.2; 0.3; 0.4].
        // The oracle marks bins 0 and 1, so the true marked amplitude is a = p0 + p1 = 0.3.
        // This exercises the state-dependent diffusion (A S0 A†): the textbook uniform
        // diffusion would give the wrong answer for a non-uniform state preparation.
        task {
            let numQubits = 2
            let probs = [| 0.1; 0.2; 0.3; 0.4 |]
            let amps = probs |> Array.map (fun p -> System.Numerics.Complex(sqrt p, 0.0))
            let qubits = [| 0 .. numQubits - 1 |]

            let statePrep =
                FSharp.Azure.Quantum.Algorithms.MottonenStatePreparation.prepareStateFromAmplitudes
                    amps
                    qubits
                    (CircuitBuilder.empty numQubits)
            // Phase oracle marking basis states 0 (|00>) and 1: flip the qubits that are 0 in
            // the target index, apply CZ to flip |11>, then undo the flips.
            let markState (c: CircuitBuilder.Circuit) (idx: int) =
                let flips = [ 0 .. numQubits - 1 ] |> List.filter (fun q -> (idx >>> q) &&& 1 = 0)

                let withFlips =
                    flips
                    |> List.fold (fun cc q -> cc |> CircuitBuilder.addGate (CircuitBuilder.X q)) c

                let withCZ = withFlips |> CircuitBuilder.addGate (CircuitBuilder.CZ(0, 1))

                flips
                |> List.fold (fun cc q -> cc |> CircuitBuilder.addGate (CircuitBuilder.X q)) withCZ

            let oracle = [ 0; 1 ] |> List.fold markState (CircuitBuilder.empty numQubits)

            let config =
                {
                    NumQubits = numQubits
                    StatePreparation = statePrep
                    Oracle = oracle
                    GroverIterations = 4
                    Shots = 1000
                }

            match! estimateExpectationAsync config (createBackend ()) CancellationToken.None with
            | Ok qmc ->
                Assert.True(
                    abs (qmc.ExpectationValue - 0.3) < 0.02,
                    $"Expected marked amplitude a ≈ 0.3, got {qmc.ExpectationValue}"
                )
            | Error e -> failwith $"QAE failed: {e}"
        }
        :> Task

    // ========================================================================
    // VALIDATION
    // ========================================================================

    [<Fact>]
    let ``estimateExpectation rejects NumQubits < 1`` () =
        task {
            let config =
                { createSimpleConfig 1 1 100 with
                    NumQubits = 0
                }

            let qb = createBackend ()

            match! estimateExpectationAsync config qb CancellationToken.None with
            | Error(QuantumError.ValidationError("NumQubits", _)) -> ()
            | r -> failwith $"Expected ValidationError for NumQubits, got {r}"
        }
        :> Task

    [<Fact>]
    let ``estimateExpectation rejects NumQubits beyond the circuit budget`` () =
        task {
            // Bounded by how wide a circuit finishes, not by how wide a state fits —
            // Grover amplification applies many gates, and each qubit doubles all of
            // them. Derived rather than hardcoded so it holds on any machine.
            let config =
                { createSimpleConfig 1 1 100 with
                    NumQubits = StateVector.practicalCircuitQubits + 1
                }

            let qb = createBackend ()

            match! estimateExpectationAsync config qb CancellationToken.None with
            | Error(QuantumError.ValidationError("NumQubits", _)) -> ()
            | r -> failwith $"Expected ValidationError for NumQubits, got {r}"
        }
        :> Task

    [<Fact>]
    let ``estimateExpectation rejects negative GroverIterations`` () =
        task {
            let config =
                { createSimpleConfig 2 1 100 with
                    GroverIterations = -1
                }

            let qb = createBackend ()

            match! estimateExpectationAsync config qb CancellationToken.None with
            | Error(QuantumError.ValidationError("GroverIterations", _)) -> ()
            | r -> failwith $"Expected ValidationError for GroverIterations, got {r}"
        }
        :> Task

    [<Fact>]
    let ``estimateExpectation rejects Shots < 100`` () =
        task {
            let config =
                { createSimpleConfig 2 1 50 with
                    Shots = 50
                }

            let qb = createBackend ()

            match! estimateExpectationAsync config qb CancellationToken.None with
            | Error(QuantumError.ValidationError("Shots", _)) -> ()
            | r -> failwith $"Expected ValidationError for Shots, got {r}"
        }
        :> Task

    [<Fact>]
    let ``estimateExpectation rejects state prep qubit count mismatch`` () =
        task {
            let config =
                { createSimpleConfig 3 1 100 with
                    NumQubits = 2
                }

            let qb = createBackend ()

            match! estimateExpectationAsync config qb CancellationToken.None with
            | Error(QuantumError.ValidationError _) -> ()
            | r -> failwith $"Expected ValidationError for qubit count mismatch, got {r}"
        }
        :> Task

    // ========================================================================
    // SUCCESSFUL EXECUTION
    // ========================================================================

    [<Fact>]
    let ``estimateExpectation returns QMCResult on success`` () =
        task {
            let config = createSimpleConfig 3 1 100
            let qb = createBackend ()

            match! estimateExpectationAsync config qb CancellationToken.None with
            | Ok qmc ->
                Assert.True(
                    qmc.ExpectationValue >= 0.0 && qmc.ExpectationValue <= 1.0,
                    $"ExpectationValue {qmc.ExpectationValue} should be in [0,1]"
                )

                Assert.True(qmc.StandardError > 0.0, "StandardError should be positive")

                Assert.True(
                    qmc.SuccessProbability >= 0.0 && qmc.SuccessProbability <= 1.0,
                    $"SuccessProbability {qmc.SuccessProbability} should be in [0,1]"
                )

                Assert.True(qmc.QuantumQueries > 0, "QuantumQueries should be positive")
            | Error e -> failwith $"Expected Ok, got Error: {e}"
        }
        :> Task

    [<Fact>]
    let ``estimateExpectation with zero Grover iterations still works`` () =
        task {
            let config = createSimpleConfig 2 0 100
            let qb = createBackend ()
            let! result = estimateExpectationAsync config qb CancellationToken.None

            result
            |> Result.map (fun qmc -> Assert.True(qmc.ExpectationValue >= 0.0 && qmc.ExpectationValue <= 1.0))
            |> Result.defaultWith (fun e -> failwith $"Expected Ok, got Error: {e}")
        }
        :> Task

    // ========================================================================
    // CONVENIENCE FUNCTIONS
    // ========================================================================

    [<Fact>]
    let ``estimateProbability returns probability in valid range`` () =
        task {
            let numQubits = 3

            let statePrep =
                [ 0 .. numQubits - 1 ]
                |> List.fold
                    (fun c q -> c |> CircuitBuilder.addGate (CircuitBuilder.H q))
                    (CircuitBuilder.empty numQubits)

            let oracle =
                CircuitBuilder.empty numQubits |> CircuitBuilder.addGate (CircuitBuilder.Z 0)

            let qb = createBackend ()
            let! result = estimateProbabilityAsync statePrep oracle 1 qb CancellationToken.None

            result
            |> Result.map (fun p -> Assert.True(p >= 0.0 && p <= 1.0, $"Probability {p} should be in [0,1]"))
            |> Result.defaultWith (fun e -> failwith $"Expected Ok, got Error: {e}")
        }
        :> Task

    [<Fact>]
    let ``integrate returns a finite value`` () =
        task {
            let numQubits = 3

            let functionOracle =
                CircuitBuilder.empty numQubits |> CircuitBuilder.addGate (CircuitBuilder.Z 0)

            let qb = createBackend ()
            let! result = integrateAsync functionOracle (0.0, 1.0) 1 qb CancellationToken.None

            result
            |> Result.map (fun value ->
                Assert.True(System.Double.IsFinite(value), $"Integration result {value} should be finite"))
            |> Result.defaultWith (fun e -> failwith $"Expected Ok, got Error: {e}")
        }
        :> Task

    // ========================================================================
    // QMC RESULT FIELDS
    // ========================================================================

    [<Fact>]
    let ``QMCResult has correct QuantumQueries calculation`` () =
        task {
            let config = createSimpleConfig 3 2 200
            let qb = createBackend ()

            match! estimateExpectationAsync config qb CancellationToken.None with
            | Ok qmc ->
                // QuantumQueries = GroverIterations * Shots
                Assert.Equal(2 * 200, qmc.QuantumQueries)
            | Error e -> failwith $"Expected Ok, got Error: {e}"
        }
        :> Task

    [<Fact>]
    let ``QMCResult has correct ClassicalEquivalent calculation`` () =
        task {
            let config = createSimpleConfig 3 3 100
            let qb = createBackend ()

            match! estimateExpectationAsync config qb CancellationToken.None with
            | Ok qmc ->
                // ClassicalEquivalent = GroverIterations^2
                Assert.Equal(9, qmc.ClassicalEquivalent)
            | Error e -> failwith $"Expected Ok, got Error: {e}"
        }
        :> Task

    // ========================================================================
    // ROUTES: exact gate by gate locally, whole circuits on a sampling backend
    // ========================================================================

    /// P(qubit 0 = 1) = 0.3 on two qubits, marked by a Z on qubit 0.
    let private knownConfig () =
        let theta = 2.0 * asin (sqrt 0.3)

        {
            NumQubits = 2
            StatePreparation =
                CircuitBuilder.empty 2
                |> CircuitBuilder.addGate (CircuitBuilder.RY(0, theta))
                |> CircuitBuilder.addGate (CircuitBuilder.H 1)
            Oracle = CircuitBuilder.empty 2 |> CircuitBuilder.addGate (CircuitBuilder.Z 0)
            GroverIterations = 4
            Shots = 1000
        }

    [<Fact>]
    let ``estimateExpectation on a whole-circuit sampling backend estimates from measured frequencies`` () =
        task {
            let backend = SampledWholeCircuit.Backend(4000, 11)

            match! estimateExpectationAsync (knownConfig ()) backend CancellationToken.None with
            | Ok qmc ->
                // Sampled states carry no phases: the marked set must come from the oracle's
                // definition, or the estimate collapses to 0.
                Assert.True(abs (qmc.ExpectationValue - 0.3) < 0.03, $"expected ≈ 0.3, got {qmc.ExpectationValue}")
                Assert.True(backend.Executed >= 4, $"expected one job per Grover power, got {backend.Executed}")
            | Error e -> failwith $"Expected Ok, got Error: {e}"
        }
        :> Task

    [<Fact>]
    let ``estimateExpectation on the local simulator stays exact`` () =
        task {
            match! estimateExpectationAsync (knownConfig ()) (createBackend ()) CancellationToken.None with
            | Ok qmc ->
                Assert.True(abs (qmc.ExpectationValue - 0.3) < 1e-4, $"expected 0.3, got {qmc.ExpectationValue}")
            | Error e -> failwith $"Expected Ok, got Error: {e}"
        }
        :> Task

    [<Fact>]
    let ``estimateExpectation refuses an oracle that is not a phase oracle`` () =
        task {
            let config =
                { knownConfig () with
                    Oracle = CircuitBuilder.empty 2 |> CircuitBuilder.addGate (CircuitBuilder.H 0)
                }

            match! estimateExpectationAsync config (createBackend ()) CancellationToken.None with
            | Error(QuantumError.ValidationError("Oracle", _)) -> ()
            | other -> failwith $"Expected an Oracle validation error, got {other}"
        }
        :> Task

    /// p = [0.1; 0.2; 0.3; 0.4] on two qubits.
    let private fourBinPreparation () =
        let amplitudes =
            [| 0.1; 0.2; 0.3; 0.4 |]
            |> Array.map (fun p -> System.Numerics.Complex(sqrt p, 0.0))

        FSharp.Azure.Quantum.Algorithms.MottonenStatePreparation.prepareStateFromAmplitudes
            amplitudes
            [| 0; 1 |]
            (CircuitBuilder.empty 2)

    [<Fact>]
    let ``estimateBoundedExpectation is exact gate by gate on the local simulator`` () =
        // E[f] = 0.1·0 + 0.2·0.5 + 0.3·0.25 + 0.4·1 = 0.575
        task {
            match!
                estimateBoundedExpectationAsync
                    (fourBinPreparation ())
                    [| 0.0; 0.5; 0.25; 1.0 |]
                    4
                    1000
                    (createBackend ())
                    CancellationToken.None
            with
            | Ok r ->
                Assert.True(abs (r.Expectation - 0.575) < 1e-4, $"expected 0.575, got {r.Expectation}")
                Assert.False(r.WholeCircuit)
                Assert.Equal(None, r.ShotsPerCircuit)
                Assert.Equal<int list>([ 0; 1; 2; 4 ], r.GroverPowers)
                Assert.True(r.StandardError > 0.0)
            | Error e -> failwith $"Expected Ok, got Error: {e}"
        }
        :> Task

    [<Fact>]
    let ``estimateBoundedExpectation submits whole circuits to a sampling backend`` () =
        task {
            let backend = SampledWholeCircuit.Backend(4000, 5)

            match!
                estimateBoundedExpectationAsync
                    (fourBinPreparation ())
                    [| 0.0; 0.5; 0.25; 1.0 |]
                    4
                    1000
                    backend
                    CancellationToken.None
            with
            | Ok r ->
                Assert.True(r.WholeCircuit)
                Assert.Equal(Some 4000, r.ShotsPerCircuit)
                Assert.Equal(4, backend.Executed)

                Assert.True(
                    abs (r.Expectation - 0.575) < 4.0 * r.StandardError + 0.005,
                    $"expected ≈ 0.575, got {r.Expectation} ± {r.StandardError}"
                )
            | Error e -> failwith $"Expected Ok, got Error: {e}"
        }
        :> Task

    [<Fact>]
    let ``estimateBoundedExpectation refuses values outside the unit interval`` () =
        task {
            match!
                estimateBoundedExpectationAsync
                    (fourBinPreparation ())
                    [| 0.0; 1.5; 0.25; 1.0 |]
                    2
                    1000
                    (createBackend ())
                    CancellationToken.None
            with
            | Error(QuantumError.ValidationError("values", _)) -> ()
            | other -> failwith $"Expected a values validation error, got {other}"
        }
        :> Task

    // ========================================================================
    // STANDARD ERROR (the fit's Cramér–Rao error, not the O(1/M) query bound)
    // ========================================================================

    /// Uniform superposition on 2 qubits with |11⟩ marked: a = 0.25.
    let private quarterConfig (iterations: int) (shots: int) : QMCConfig =
        {
            NumQubits = 2
            StatePreparation =
                CircuitBuilder.empty 2
                |> CircuitBuilder.addGate (CircuitBuilder.H 0)
                |> CircuitBuilder.addGate (CircuitBuilder.H 1)
            Oracle = CircuitBuilder.empty 2 |> CircuitBuilder.addGate (CircuitBuilder.CZ(0, 1))
            GroverIterations = iterations
            Shots = shots
        }

    /// Cramér–Rao error of MLAE at a = sin²θ over the powers 0, 1, 2, 4 with N shots each.
    let private cramerRao (a: float) (shots: int) =
        let theta = asin (sqrt a)
        let sigmaTheta = 1.0 / (2.0 * sqrt (float shots * (1.0 + 9.0 + 25.0 + 81.0)))
        abs (sin (2.0 * theta)) * sigmaTheta + sigmaTheta * sigmaTheta

    [<Fact>]
    let ``estimateExpectation reports the fit's standard error, not 1 over the iterations`` () =
        task {
            match! estimateExpectationAsync (quarterConfig 4 1000) (createBackend ()) CancellationToken.None with
            | Ok r ->
                // Exact probabilities: the maximum-likelihood fit recovers a (to ~1e-8: a power
                // whose probability is 1 - 1e-16 pins θ only to √1e-16).
                Assert.Equal(0.25, r.ExpectationValue, 7)
                // Previously 1/GroverIterations = 0.25; the Cramér–Rao error at 1,000 shots is ≈0.0013.
                Assert.Equal(cramerRao 0.25 1000, r.StandardError, 9)
                Assert.True(r.StandardError < 0.002, $"StandardError {r.StandardError}")
            | Error e -> failwith $"Expected Ok, got Error: {e}"
        }
        :> Task

    [<Fact>]
    let ``estimateExpectation standard error matches the spread of sampled estimates`` () =
        task {
            // 30 independent runs on a 4,000-shot sampling backend: the reported error must
            // describe the actual scatter of the estimates.
            let estimates = ResizeArray<float>()
            let errors = ResizeArray<float>()

            for seed in 1..30 do
                let backend = SampledWholeCircuit.Backend(4000, seed)

                match! estimateExpectationAsync (quarterConfig 4 1000) backend CancellationToken.None with
                | Ok r ->
                    estimates.Add r.ExpectationValue
                    errors.Add r.StandardError
                | Error e -> failwith $"Expected Ok, got Error: {e}"

            let mean = Seq.average estimates
            let spread = sqrt (estimates |> Seq.averageBy (fun e -> (e - mean) ** 2.0))
            let reported = Seq.average errors

            // The backend's 4,000 shots, not config.Shots, are behind each probability.
            Assert.True(abs (reported - cramerRao 0.25 4000) < 2e-4, $"reported {reported}")
            Assert.True(abs (mean - 0.25) < 4.0 * reported / sqrt 30.0 + 1e-4, $"mean {mean}")
            Assert.InRange(spread / reported, 0.4, 2.5)
        }
        :> Task
