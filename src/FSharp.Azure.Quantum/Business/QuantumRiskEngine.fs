namespace FSharp.Azure.Quantum.Business

open System
open System.Numerics
open FSharp.Azure.Quantum
open FSharp.Azure.Quantum.Core
open FSharp.Azure.Quantum.Core.BackendAbstraction
open FSharp.Azure.Quantum.Algorithms


/// Metrics available for risk calculation
[<Struct>]
type RiskMetric =
    | ValueAtRisk
    | ConditionalVaR
    | ExpectedShortfall
    | Volatility

/// Configuration for the Quantum Risk Engine
type RiskConfiguration =
    {
        MarketDataPath: string option
        ConfidenceLevel: float
        SimulationPaths: int
        UseAmplitudeEstimation: bool
        UseErrorMitigation: bool
        Metrics: RiskMetric list
        NumQubits: int
        GroverIterations: int
        Shots: int
        Backend: IQuantumBackend option
        CancellationToken: System.Threading.CancellationToken option
    }

/// Result of the risk engine execution
type RiskReport =
    {
        VaR: float voption
        CVaR: float voption
        ExpectedShortfall: float voption
        Volatility: float voption
        ConfidenceLevel: float
        ExecutionTimeMs: float
        Method: string
        Configuration: RiskConfiguration
    }

module RiskEngine =

    // Mock data generator for fallback
    let private generateMockReturns n =
        let rng = Random(42)

        Array.init n (fun _ ->
            // Log-normal returns: mu=0.0005, sigma=0.02
            let u1 = rng.NextDouble()
            let u2 = rng.NextDouble()
            let z = sqrt (-2.0 * log u1) * cos (2.0 * Math.PI * u2)
            0.0005 + 0.02 * z)

    // ========================================================================
    // QUANTUM PATH - State Preparation & Oracle for VaR Estimation
    // ========================================================================

    /// Discretize a return distribution into 2^n bins and compute bin probabilities.
    ///
    /// Returns: (binProbabilities, binEdges, binWidth)
    ///   binProbabilities: array of length 2^n with normalized probabilities
    ///   binEdges: array of length 2^n + 1 with bin boundaries
    ///   binWidth: width of each bin
    let private discretizeDistribution (returns: float[]) (numQubits: int) : float[] * float[] * float =
        let numBins = 1 <<< numQubits
        let minReturn = Array.min returns
        let maxReturn = Array.max returns
        // Add small margin to avoid edge effects
        let margin = (maxReturn - minReturn) * 0.01
        let lo = minReturn - margin
        let hi = maxReturn + margin
        let binWidth = (hi - lo) / float numBins

        let binEdges = Array.init (numBins + 1) (fun i -> lo + float i * binWidth)

        // Count returns per bin
        let counts = Array.zeroCreate numBins

        returns
        |> Array.iter (fun r ->
            let idx = int ((r - lo) / binWidth)
            let clampedIdx = max 0 (min (numBins - 1) idx)
            counts.[clampedIdx] <- counts.[clampedIdx] + 1)

        // Normalize to probabilities
        let total = float (Array.sum counts)

        let probs =
            counts
            |> Array.map (fun c ->
                let p = float c / total
                // Ensure non-zero probabilities for numerical stability
                // (Mottonen requires sqrt of probability as amplitude)
                if p < 1e-12 then 1e-12 else p)

        // Re-normalize after floor adjustment
        let probSum = Array.sum probs
        let normalizedProbs = probs |> Array.map (fun p -> p / probSum)

        (normalizedProbs, binEdges, binWidth)

    /// Build a state preparation circuit that encodes the return distribution
    /// into quantum amplitudes using Mottonen's algorithm.
    ///
    /// The resulting state is |psi> = sum_i sqrt(p_i) |i> where p_i is the
    /// probability of returns falling in bin i.
    let private buildStatePrepCircuit (probabilities: float[]) (numQubits: int) : CircuitBuilder.Circuit =
        // Convert probabilities to amplitudes: a_i = sqrt(p_i)
        let amplitudes = probabilities |> Array.map (fun p -> Complex(sqrt p, 0.0))

        let qubits = [| 0 .. numQubits - 1 |]
        let circuit = CircuitBuilder.empty numQubits

        MottonenStatePreparation.prepareStateFromAmplitudes amplitudes qubits circuit

    /// Risk metrics estimated on the quantum path, each by amplitude estimation.
    type private QuantumRiskEstimates =
        {
            VaR: float voption
            CVaR: float voption
            Volatility: float voption
            /// How the circuits ran: "" gate by gate, else a note on the whole-circuit route
            Route: string
        }

    /// Execute the quantum risk metrics, each from amplitude estimation of an expectation over
    /// the loaded distribution |ψ⟩ = Σ_i √p_i |i⟩ of the 2^n return bins
    /// (QuantumMonteCarlo.estimateBoundedExpectation):
    /// - VaR: bisection over the bin index t for the smallest t whose estimated CDF
    ///   F(t) = P(bin < t) reaches 1 - ConfidenceLevel; VaR is minus the upper edge of bin t - 1.
    /// - CVaR / ES: the mean loss over the tail bins, E[loss · 1(bin < t)] / F(t), with both
    ///   expectations estimated (the loss scaled into [0, 1] over the tail's range).
    /// - Volatility: the standard deviation of the bin midpoints, E[(r - c)²] - E[r - c]² for
    ///   the grid centre c, both estimated.
    /// Only the metrics requested are estimated. A negative estimated variance (possible from
    /// sampled whole circuits) is an Error.
    let private executeQuantumRisk
        (config: RiskConfiguration)
        (qBackend: IQuantumBackend)
        (returns: float[])
        : Async<Result<QuantumRiskEstimates, QuantumError>> =
        async {
            let numQubits = config.NumQubits
            let numBins = 1 <<< numQubits
            let wants metric = List.contains metric config.Metrics
            let needTail = wants ConditionalVaR || wants ExpectedShortfall
            let needVaR = wants ValueAtRisk || needTail

            let (probabilities, binEdges, _binWidth) = discretizeDistribution returns numQubits
            let statePrep = buildStatePrepCircuit probabilities numQubits

            let midpoints =
                Array.init numBins (fun i -> (binEdges.[i] + binEdges.[i + 1]) / 2.0)

            let route = ref ""

            /// Amplitude estimate of Σ_i p_i values_i, values in [0, 1].
            let estimate (values: float[]) : Async<Result<float, QuantumError>> =
                let run =
                    QuantumMonteCarlo.estimateBoundedExpectation
                        statePrep
                        values
                        config.GroverIterations
                        config.Shots
                        qBackend

                async {
                    let! result =
                        match config.CancellationToken with
                        | Some token ->
                            Async.StartAsTask(
                                run,
                                cancellationToken = token,
                                taskCreationOptions = System.Threading.Tasks.TaskCreationOptions.None
                            )
                            |> Async.AwaitTask
                        | None -> run

                    return
                        result
                        |> Result.map (fun r ->
                            route.Value <-
                                match r.ShotsPerCircuit with
                                | Some s -> $" (whole circuits sampled at {s} shots)"
                                | None when r.WholeCircuit -> " (whole circuits)"
                                | None -> ""

                            r.Expectation)
                }

            /// Amplitude estimate of Σ_i p_i values_i for real values: scaled into [0, 1] over
            /// their range for the estimate, and back.
            let estimateReal (values: float[]) : Async<Result<float, QuantumError>> =
                let lo = Array.min values
                let span = Array.max values - lo

                if span <= 0.0 then
                    async { return Ok lo }
                else
                    async {
                        let! scaled = estimate (values |> Array.map (fun v -> (v - lo) / span))
                        return scaled |> Result.map (fun e -> lo + span * e)
                    }

            let target = 1.0 - config.ConfidenceLevel

            /// Smallest t in [lo, hi] with estimated F(t) >= target, and F(t). F(numBins) = 1.
            let rec bisect (lo: int) (hi: int) (known: Map<int, float>) =
                async {
                    if lo >= hi then
                        return Ok(lo, known.[lo])
                    else
                        let mid = (lo + hi) / 2

                        match! estimate (Array.init numBins (fun i -> if i < mid then 1.0 else 0.0)) with
                        | Error err -> return Error err
                        | Ok f when f >= target -> return! bisect lo mid (known.Add(mid, f))
                        | Ok f -> return! bisect (mid + 1) hi (known.Add(mid, f))
                }

            let! tail =
                async {
                    if needVaR then
                        let! found = bisect 1 numBins (Map.ofList [ numBins, 1.0 ])
                        return found |> Result.map Some
                    else
                        return Ok None
                }

            let! cvar =
                match tail with
                | Ok(Some(t, tailProbability)) when needTail ->
                    let losses = Array.init t (fun i -> -midpoints.[i])
                    let lo = Array.min losses
                    let span = Array.max losses - lo

                    if span <= 0.0 then
                        async { return Ok(ValueSome lo) }
                    else
                        async {
                            let! scaled =
                                estimate (Array.init numBins (fun i -> if i < t then (losses.[i] - lo) / span else 0.0))

                            // E[scaled loss · 1(tail)] <= P(tail); the ratio is clamped to [0, 1]
                            // against sampling noise.
                            return
                                scaled
                                |> Result.map (fun g -> ValueSome(lo + span * min 1.0 (max 0.0 (g / tailProbability))))
                        }
                | Ok _ -> async { return Ok ValueNone }
                | Error err -> async { return Error err }

            let! volatility =
                if wants Volatility && Result.isOk tail && Result.isOk cvar then
                    let centre = (binEdges.[0] + binEdges.[numBins]) / 2.0

                    async {
                        let! mean = estimateReal (midpoints |> Array.map (fun r -> r - centre))

                        let! second =
                            estimateReal (midpoints |> Array.map (fun r -> (r - centre) * (r - centre)))

                        return
                            match mean, second with
                            | Error err, _
                            | _, Error err -> Error err
                            | Ok m1, Ok m2 when m2 - m1 * m1 < 0.0 ->
                                Error(
                                    QuantumError.OperationError(
                                        "RiskEngine",
                                        $"estimated variance is negative ({m2 - m1 * m1:E3}); raise the backend's shots"
                                    )
                                )
                            | Ok m1, Ok m2 -> Ok(ValueSome(sqrt (m2 - m1 * m1)))
                    }
                else
                    async { return Ok ValueNone }

            return
                match tail, cvar, volatility with
                | Error err, _, _
                | _, Error err, _
                | _, _, Error err -> Error err
                | Ok tail, Ok cvar, Ok volatility ->
                    Ok
                        {
                            VaR =
                                match tail with
                                | Some(t, _) when wants ValueAtRisk -> ValueSome(-binEdges.[t])
                                | _ -> ValueNone
                            CVaR = cvar
                            Volatility = volatility
                            Route = route.Value
                        }
        }

    // ========================================================================
    // CLASSICAL PATH
    // ========================================================================

    /// Minimum number of valid numeric rows required in a market data file.
    /// Two sorted returns are the least the VaR percentile index arithmetic can
    /// handle; small-but-valid files remain accepted (a majority-unparseable
    /// file is rejected separately as a format mismatch).
    [<Literal>]
    let private MinValidMarketDataRows = 2

    /// Execute the configured risk analysis (async, cancellable).
    ///
    /// Returns a `Result`: the quantum amplitude-estimation path can fail as a business
    /// outcome (e.g. backend rejects the circuit), surfaced as `Error`; the classical
    /// Monte Carlo path always yields `Ok`.
    let executeAsync (config: RiskConfiguration) : Async<QuantumResult<RiskReport>> =
        async {
            let startTime = DateTime.Now

            // 0. Validate configuration
            if config.ConfidenceLevel <= 0.0 || config.ConfidenceLevel >= 1.0 then
                return
                    Error(
                        QuantumError.ValidationError(
                            "ConfidenceLevel",
                            $"Confidence level must be strictly between 0 and 1, got {config.ConfidenceLevel}"
                        )
                    )
            elif config.MarketDataPath.IsNone && config.SimulationPaths < MinValidMarketDataRows then
                // Mock returns are generated from SimulationPaths; fewer than 2 paths
                // crashes the VaR percentile index arithmetic (and the quantum path's
                // distribution discretization) with an index-out-of-range instead of
                // a proper validation error.
                return
                    Error(
                        QuantumError.ValidationError(
                            "SimulationPaths",
                            $"At least {MinValidMarketDataRows} simulation paths are required for the VaR percentile math, got {config.SimulationPaths}"
                        )
                    )
            else
                // 1. Ingest data. Real market data is required when a path is configured;
                //    mock returns are used only when no MarketDataPath is given.
                let! returnsResult =
                    match config.MarketDataPath with
                    | Some path ->
                        async {
                            let! exists =
                                System.Threading.Tasks.Task.Run(fun () -> System.IO.File.Exists(path))
                                |> Async.AwaitTask

                            if not exists then
                                // A configured-but-missing file is an explicit error,
                                // not a silent fallback to mock data.
                                return
                                    Error(
                                        QuantumError.ValidationError(
                                            "MarketDataPath",
                                            $"Market data file not found: {path}"
                                        )
                                    )
                            else
                                let! lines = System.IO.File.ReadAllLinesAsync(path) |> Async.AwaitTask

                                if lines.Length <= 1 then
                                    return
                                        Error(
                                            QuantumError.ValidationError(
                                                "MarketDataPath",
                                                $"Market data file contains no data rows (empty or header only): {path}"
                                            )
                                        )
                                else
                                    // Skip unparseable rows rather than silently treating them as 0.0 returns.
                                    let parsed =
                                        lines
                                        |> Array.skip 1
                                        |> Array.choose (fun line ->
                                            match
                                                Double.TryParse(
                                                    line.Trim(),
                                                    System.Globalization.NumberStyles.Float,
                                                    System.Globalization.CultureInfo.InvariantCulture
                                                )
                                            with
                                            | true, v -> Some v
                                            | _ -> None)

                                    let dataRows = lines.Length - 1

                                    if parsed.Length < MinValidMarketDataRows then
                                        // Too few rows for the percentile math (index arithmetic
                                        // needs at least 2 sorted returns).
                                        return
                                            Error(
                                                QuantumError.ValidationError(
                                                    "MarketDataPath",
                                                    $"Market data file has only {parsed.Length} valid numeric rows (minimum {MinValidMarketDataRows} required): {path}"
                                                )
                                            )
                                    elif parsed.Length * 2 < dataRows then
                                        // Most rows failed to parse — almost certainly a format
                                        // mismatch (wrong column layout, non-invariant decimal
                                        // separator), not occasional bad lines. Erroring loudly
                                        // beats computing VaR from a fraction of the data.
                                        return
                                            Error(
                                                QuantumError.ValidationError(
                                                    "MarketDataPath",
                                                    $"Only {parsed.Length} of {dataRows} data rows are valid numbers — the file format is probably wrong (expected one invariant-culture numeric return per line): {path}"
                                                )
                                            )
                                    else
                                        return Ok parsed
                        }
                    | None -> async { return Ok(generateMockReturns config.SimulationPaths) }

                match returnsResult with
                | Error err -> return Error err
                | Ok returns ->
                    // 2. Choose quantum or classical path
                    if config.UseAmplitudeEstimation && config.Backend.IsSome then
                        // Quantum path: every reported metric from amplitude estimation
                        let qBackend = config.Backend.Value

                        match! executeQuantumRisk config qBackend returns with
                        | Error err ->
                            // Business outcome: propagate the quantum failure as Error (no classical fallback).
                            return Error err
                        | Ok estimates ->
                            let executionTime = (DateTime.Now - startTime).TotalMilliseconds

                            return
                                Ok
                                    {
                                        VaR = estimates.VaR
                                        CVaR =
                                            if List.contains ConditionalVaR config.Metrics then
                                                estimates.CVaR
                                            else
                                                ValueNone
                                        ExpectedShortfall =
                                            if List.contains ExpectedShortfall config.Metrics then
                                                estimates.CVaR
                                            else
                                                ValueNone
                                        Volatility = estimates.Volatility
                                        ConfidenceLevel = config.ConfidenceLevel
                                        ExecutionTimeMs = executionTime
                                        Method = "Quantum Amplitude Estimation" + estimates.Route
                                        Configuration = config
                                    }
                    else
                        // Classical path: Monte Carlo
                        let sortedReturns = Array.sort returns
                        let varIndex = int ((1.0 - config.ConfidenceLevel) * float returns.Length)
                        let classicalVaR = -sortedReturns.[varIndex]

                        let vaR =
                            if List.contains ValueAtRisk config.Metrics then
                                ValueSome classicalVaR
                            else
                                ValueNone

                        let tailLosses = sortedReturns |> Array.take (varIndex + 1)
                        let cVaRVal = -(Array.average tailLosses)

                        let cVaR =
                            if List.contains ConditionalVaR config.Metrics then
                                ValueSome cVaRVal
                            else
                                ValueNone

                        let es =
                            if List.contains ExpectedShortfall config.Metrics then
                                ValueSome cVaRVal
                            else
                                ValueNone

                        let vol =
                            if List.contains Volatility config.Metrics then
                                let mean = Array.average returns
                                let sumSq = returns |> Array.sumBy (fun x -> pown (x - mean) 2)
                                ValueSome(sqrt (sumSq / float returns.Length))
                            else
                                ValueNone

                        let executionTime = (DateTime.Now - startTime).TotalMilliseconds

                        return
                            Ok
                                {
                                    VaR = vaR
                                    CVaR = cVaR
                                    ExpectedShortfall = es
                                    Volatility = vol
                                    ConfidenceLevel = config.ConfidenceLevel
                                    ExecutionTimeMs = executionTime
                                    Method = "Classical Monte Carlo"
                                    Configuration = config
                                }
        }

    /// Execute the configured risk analysis (sync wrapper).
    ///
    /// Convenience adapter over `executeAsync`. Prefer `executeAsync`, which returns the
    /// quantum failure as a `Result`; this wrapper unwraps it and raises on `Error`.
    [<System.Obsolete("Use executeAsync instead. This synchronous wrapper blocks the calling thread and raises on quantum failure; prefer the Result-returning executeAsync.")>]
    let execute (config: RiskConfiguration) : RiskReport =
        let result =
            match config.CancellationToken with
            | Some token -> Async.RunSynchronously(executeAsync config, cancellationToken = token)
            | None -> executeAsync config |> Async.RunSynchronously

        match result with
        | Ok report -> report
        | Error err ->
            raise (
                InvalidOperationException(
                    $"Risk analysis failed: {err.Message}. Use executeAsync to handle this as a Result."
                )
            )

/// Builder for the Quantum Risk Engine DSL
type QuantumRiskEngineBuilder() =
    member _.Yield(_) =
        {
            MarketDataPath = None
            ConfidenceLevel = 0.95
            SimulationPaths = 10000
            UseAmplitudeEstimation = false
            UseErrorMitigation = false
            Metrics = []
            NumQubits = 5
            GroverIterations = 2
            Shots = 100
            Backend = None
            CancellationToken = None
        }

    member _.Zero() =
        {
            MarketDataPath = None
            ConfidenceLevel = 0.95
            SimulationPaths = 10000
            UseAmplitudeEstimation = false
            UseErrorMitigation = false
            Metrics = []
            NumQubits = 5
            GroverIterations = 2
            Shots = 100
            Backend = None
            CancellationToken = None
        }

    member _.Delay(f: unit -> RiskConfiguration) = f

    member _.For(state: RiskConfiguration, body: unit -> RiskConfiguration) = body ()

    member _.Run(state: RiskConfiguration) : QuantumResult<RiskReport> =
        // Propagate a quantum failure as Error rather than raising (executeAsync is Result-typed).
        match state.CancellationToken with
        | Some token -> Async.RunSynchronously(RiskEngine.executeAsync state, cancellationToken = token)
        | None -> RiskEngine.executeAsync state |> Async.RunSynchronously

    member this.Run(f: unit -> RiskConfiguration) : QuantumResult<RiskReport> = this.Run(f ())

    /// Load market data from a file path
    [<CustomOperation("load_market_data")>]
    member _.LoadMarketData(state: RiskConfiguration, path: string) =
        { state with
            MarketDataPath = Some path
        }

    /// Set the confidence level for risk calculations (e.g., 0.99 for 99%)
    [<CustomOperation("set_confidence_level")>]
    member _.SetConfidenceLevel(state: RiskConfiguration, level: float) = { state with ConfidenceLevel = level }

    /// Set the number of simulation paths (classical equivalent)
    [<CustomOperation("set_simulation_paths")>]
    member _.SetSimulationPaths(state: RiskConfiguration, paths: int) = { state with SimulationPaths = paths }

    /// Enable or disable Quantum Amplitude Estimation for quadratic speedup
    [<CustomOperation("use_amplitude_estimation")>]
    member _.UseAmplitudeEstimation(state: RiskConfiguration, enable: bool) =
        { state with
            UseAmplitudeEstimation = enable
        }

    /// Enable or disable error mitigation techniques
    [<CustomOperation("use_error_mitigation")>]
    member _.UseErrorMitigation(state: RiskConfiguration, enable: bool) =
        { state with
            UseErrorMitigation = enable
        }

    /// Add a metric to be calculated
    [<CustomOperation("calculate_metric")>]
    member _.CalculateMetric(state: RiskConfiguration, metric: RiskMetric) =
        { state with
            Metrics = state.Metrics @ [ metric ]
        }

    /// Provide a cancellation token for long-running operations
    [<CustomOperation("cancellation_token")>]
    member _.CancellationToken(state: RiskConfiguration, token: System.Threading.CancellationToken) =
        { state with
            CancellationToken = Some token
        }

    /// Set number of qubits for QMC operations
    [<CustomOperation("qubits")>]
    member _.Qubits(state: RiskConfiguration, numQubits: int) = { state with NumQubits = numQubits }

    /// Set Grover iterations for QMC operations
    [<CustomOperation("iterations")>]
    member _.Iterations(state: RiskConfiguration, groverIterations: int) =
        { state with
            GroverIterations = groverIterations
        }

    /// Set number of measurement shots
    [<CustomOperation("shots")>]
    member _.Shots(state: RiskConfiguration, shots: int) = { state with Shots = shots }

    /// Set the quantum backend
    [<CustomOperation("backend")>]
    member _.Backend(state: RiskConfiguration, backend: IQuantumBackend) = { state with Backend = Some backend }


[<AutoOpen>]
module QuantumRiskEngineDSL =
    let quantumRiskEngine = QuantumRiskEngineBuilder()
