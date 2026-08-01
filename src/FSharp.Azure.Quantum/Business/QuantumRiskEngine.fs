namespace FSharp.Azure.Quantum.Business

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
type RiskConfiguration = {
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
type RiskReport = {
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
        let rng = System.Random(42)
        Array.init n (fun _ ->
            // Log-normal returns: mu=0.0005, sigma=0.02
            let u1 = rng.NextDouble()
            let u2 = rng.NextDouble()
            let z = sqrt(-2.0 * log u1) * cos(2.0 * System.Math.PI * u2)
            0.0005 + 0.02 * z
        )

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
        returns |> Array.iter (fun r ->
            let idx = int ((r - lo) / binWidth)
            let clampedIdx = max 0 (min (numBins - 1) idx)
            counts.[clampedIdx] <- counts.[clampedIdx] + 1
        )

        // Normalize to probabilities
        let total = float (Array.sum counts)
        let probs =
            counts |> Array.map (fun c ->
                let p = float c / total
                // Ensure non-zero probabilities for numerical stability
                // (Mottonen requires sqrt of probability as amplitude)
                if p < 1e-12 then 1e-12 else p
            )

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
        let amplitudes =
            probabilities
            |> Array.map (fun p -> Complex(sqrt p, 0.0))

        let qubits = [| 0 .. numQubits - 1 |]
        let circuit = CircuitBuilder.empty numQubits

        MottonenStatePreparation.prepareStateFromAmplitudes amplitudes qubits circuit

    /// Build a threshold comparator oracle circuit for VaR estimation.
    ///
    /// Marks (applies phase flip to) all computational basis states |i> where
    /// i < thresholdIndex, i.e., the return bins below the VaR loss threshold.
    ///
    /// Implementation: For each basis state i < thresholdIndex, apply a diagonal
    /// phase (-1) using a multi-controlled Z decomposition. This is equivalent to
    /// Z * diag(1,...,1,-1,...,-1) on the marked subspace.
    ///
    /// For small threshold indices this is efficient; for larger thresholds we
    /// flip the complement (mark i >= threshold) and apply a global phase.
    let private buildThresholdOracle (numQubits: int) (thresholdIndex: int) : CircuitBuilder.Circuit =
        let numStates = 1 <<< numQubits
        let clampedThreshold = max 0 (min numStates thresholdIndex)

        if clampedThreshold = 0 then
            // No states to mark - identity oracle
            CircuitBuilder.empty numQubits
        elif clampedThreshold = numStates then
            // All states marked - global phase flip (Z on qubit 0 with all others as control
            // effectively -I). We apply X to all, MCZ, X to all (marks |11...1> = marks all).
            // Simpler: phase flip on every state = -I, which is just Z on any qubit preceded
            // by X gates to flip it. But for Grover oracle the global phase is irrelevant,
            // so we can just apply Z to qubit 0.
            CircuitBuilder.empty numQubits
            |> CircuitBuilder.addGate (CircuitBuilder.Z 0)
        else
            // Mark each basis state i < thresholdIndex with a phase flip.
            // For each target state, we apply X gates to qubits that are 0 in the binary
            // representation, then MCZ (or CZ/Z for small cases), then undo the X gates.
            //
            // This is O(thresholdIndex * numQubits) gates. For VaR at typical confidence
            // levels (95%, 99%), thresholdIndex is small (5% or 1% of 2^n states), so this
            // is efficient.
            let markState (circuit: CircuitBuilder.Circuit) (stateIdx: int) : CircuitBuilder.Circuit =
                // Apply X to qubits where bit is 0 (so |stateIdx> maps to |11...1>)
                let flipQubits =
                    [0 .. numQubits - 1]
                    |> List.filter (fun q -> (stateIdx >>> q) &&& 1 = 0)

                let withFlips =
                    flipQubits
                    |> List.fold (fun c q -> c |> CircuitBuilder.addGate (CircuitBuilder.X q)) circuit

                // Apply multi-controlled Z to flip phase of |11...1>
                let withPhaseFlip =
                    if numQubits = 1 then
                        withFlips |> CircuitBuilder.addGate (CircuitBuilder.Z 0)
                    elif numQubits = 2 then
                        withFlips |> CircuitBuilder.addGate (CircuitBuilder.CZ(0, 1))
                    else
                        let controls = [0 .. numQubits - 2]
                        withFlips |> CircuitBuilder.addGate (CircuitBuilder.MCZ(controls, numQubits - 1))

                // Undo X flips
                flipQubits
                |> List.fold (fun c q -> c |> CircuitBuilder.addGate (CircuitBuilder.X q)) withPhaseFlip

            // If marking more than half the states, mark the complement and apply global phase
            if clampedThreshold > numStates / 2 then
                // Mark states i >= threshold (the complement) and apply global phase
                let complementCircuit =
                    [clampedThreshold .. numStates - 1]
                    |> List.fold markState (CircuitBuilder.empty numQubits)

                // Global phase flip = mark all states, which together with complement marking
                // gives: (-1)^(all) * (-1)^(complement) = (-1)^(target)
                // Global Z on qubit 0 suffices for global phase
                complementCircuit
                |> CircuitBuilder.addGate (CircuitBuilder.Z 0)
            else
                [0 .. clampedThreshold - 1]
                |> List.fold markState (CircuitBuilder.empty numQubits)

    /// Find the bin index corresponding to the VaR threshold.
    ///
    /// At confidence level alpha, VaR is the threshold where
    /// P(return < -VaR) = 1 - alpha. We find the bin index such that
    /// the cumulative probability of bins [0..index-1] is approximately 1-alpha.
    let private findVarThresholdIndex (probabilities: float[]) (confidenceLevel: float) : int =
        let targetCumProb = 1.0 - confidenceLevel
        let rec findIndex idx cumProb =
            if idx >= probabilities.Length then
                probabilities.Length
            else
                let newCum = cumProb + probabilities.[idx]
                if newCum >= targetCumProb then
                    idx + 1
                else
                    findIndex (idx + 1) newCum
        findIndex 0 0.0

    /// Read the genuine quantum bin probabilities q_i = |⟨i|ψ⟩|² from the prepared state |ψ⟩ = A|0⟩.
    /// Bin index i maps directly to basis-state index i (the state-preparation encoding), so VaR/CVaR
    /// are derived from the measured quantum distribution rather than from a classical array.
    let private quantumBinProbabilities
        (backend: IQuantumBackend)
        (statePrep: CircuitBuilder.Circuit)
        : Result<float[], QuantumError> =
        QuantumMonteCarlo.measureBinProbabilities backend statePrep

    /// Execute quantum amplitude estimation for VaR using the QMC infrastructure.
    ///
    /// Algorithm:
    /// 1. Ingest returns data and discretize into 2^n bins
    /// 2. Build state prep circuit (Mottonen encoding of distribution)
    /// 3. Build threshold oracle (marks bins below VaR threshold)
    /// 4. Run QMC amplitude estimation via backend
    /// 5. Extract estimated tail probability from quantum result
    /// 6. Map back to VaR value using bin edges
    ///
    /// Returns: (quantumVaR, quantumCVaR, estimatedTailProb)
    let private executeQuantumVaR
        (config: RiskConfiguration)
        (qBackend: IQuantumBackend)
        (returns: float[])
        : Async<Result<float * float * float, QuantumError>> =
        async {
            let numQubits = config.NumQubits

            // 1. Discretize the empirical return distribution and encode it as |ψ⟩ = A|0⟩.
            let (empiricalProbs, binEdges, _binWidth) = discretizeDistribution returns numQubits
            let statePrep = buildStatePrepCircuit empiricalProbs numQubits

            // 2. Read the GENUINE quantum bin probabilities from the prepared state.
            //    Everything below is derived from these, not from the classical array.
            match quantumBinProbabilities qBackend statePrep with
            | Error err -> return Error err
            | Ok quantumProbs ->
                // 3. VaR threshold from the quantum cumulative distribution.
                let thresholdIndex = findVarThresholdIndex quantumProbs config.ConfidenceLevel
                let oracle = buildThresholdOracle numQubits thresholdIndex

                // 4. Quantum amplitude estimation of the tail probability P(return < threshold).
                let qmcConfig = {
                    QuantumMonteCarlo.QMCConfig.NumQubits = numQubits
                    QuantumMonteCarlo.QMCConfig.StatePreparation = statePrep
                    QuantumMonteCarlo.QMCConfig.Oracle = oracle
                    QuantumMonteCarlo.QMCConfig.GroverIterations = config.GroverIterations
                    QuantumMonteCarlo.QMCConfig.Shots = config.Shots
                }

                let! qmcResultWrapped =
                    match config.CancellationToken with
                    | Some token ->
                        Async.StartAsTask(
                            QuantumMonteCarlo.estimateExpectation qmcConfig qBackend,
                            cancellationToken = token,
                            taskCreationOptions = System.Threading.Tasks.TaskCreationOptions.None)
                        |> Async.AwaitTask
                    | None ->
                        QuantumMonteCarlo.estimateExpectation qmcConfig qBackend

                match qmcResultWrapped with
                | Error err -> return Error err
                | Ok qmcResult ->
                    // 5. Tail probability is the genuine amplitude-estimation result.
                    let estimatedTailProb = qmcResult.ExpectationValue

                    // VaR = loss at the threshold bin edge (the quantile boundary).
                    let quantumVaR =
                        if thresholdIndex > 0 && thresholdIndex <= binEdges.Length - 1 then
                            -binEdges.[thresholdIndex]
                        else
                            0.0

                    // CVaR/ES: quantum-probability-weighted mean of the tail bin midpoints.
                    let quantumCVaR =
                        if thresholdIndex > 0 then
                            let tailBins = [| 0 .. thresholdIndex - 1 |]
                            let tailMass = tailBins |> Array.sumBy (fun i -> quantumProbs.[i])
                            if tailMass > 1e-12 then
                                let weightedSum =
                                    tailBins
                                    |> Array.sumBy (fun i ->
                                        let binMid = (binEdges.[i] + binEdges.[i + 1]) / 2.0
                                        quantumProbs.[i] * binMid)
                                -(weightedSum / tailMass)
                            else
                                quantumVaR
                        else
                            quantumVaR

                    return Ok (quantumVaR, quantumCVaR, estimatedTailProb)
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
            let startTime = System.DateTime.Now

            // 0. Validate configuration
            if config.ConfidenceLevel <= 0.0 || config.ConfidenceLevel >= 1.0 then
                return Error (QuantumError.ValidationError ("ConfidenceLevel", $"Confidence level must be strictly between 0 and 1, got {config.ConfidenceLevel}"))
            elif config.MarketDataPath.IsNone && config.SimulationPaths < MinValidMarketDataRows then
                // Mock returns are generated from SimulationPaths; fewer than 2 paths
                // crashes the VaR percentile index arithmetic (and the quantum path's
                // distribution discretization) with an index-out-of-range instead of
                // a proper validation error.
                return Error (QuantumError.ValidationError ("SimulationPaths", $"At least {MinValidMarketDataRows} simulation paths are required for the VaR percentile math, got {config.SimulationPaths}"))
            else
                // 1. Ingest data. Real market data is required when a path is configured;
                //    mock returns are used only when no MarketDataPath is given.
                let! returnsResult =
                    match config.MarketDataPath with
                    | Some path ->
                        async {
                            let! exists = System.Threading.Tasks.Task.Run(fun () -> System.IO.File.Exists(path)) |> Async.AwaitTask
                            if not exists then
                                // A configured-but-missing file is an explicit error,
                                // not a silent fallback to mock data.
                                return Error (QuantumError.ValidationError ("MarketDataPath", $"Market data file not found: {path}"))
                            else
                                let! lines = System.IO.File.ReadAllLinesAsync(path) |> Async.AwaitTask
                                if lines.Length <= 1 then
                                    return Error (QuantumError.ValidationError ("MarketDataPath", $"Market data file contains no data rows (empty or header only): {path}"))
                                else
                                    // Skip unparseable rows rather than silently treating them as 0.0 returns.
                                    let parsed =
                                        lines
                                        |> Array.skip 1
                                        |> Array.choose (fun line ->
                                            match System.Double.TryParse(line.Trim(),
                                                                         System.Globalization.NumberStyles.Float,
                                                                         System.Globalization.CultureInfo.InvariantCulture) with
                                            | true, v -> Some v
                                            | _ -> None)
                                    let dataRows = lines.Length - 1
                                    if parsed.Length < MinValidMarketDataRows then
                                        // Too few rows for the percentile math (index arithmetic
                                        // needs at least 2 sorted returns).
                                        return Error (QuantumError.ValidationError ("MarketDataPath", $"Market data file has only {parsed.Length} valid numeric rows (minimum {MinValidMarketDataRows} required): {path}"))
                                    elif parsed.Length * 2 < dataRows then
                                        // Most rows failed to parse — almost certainly a format
                                        // mismatch (wrong column layout, non-invariant decimal
                                        // separator), not occasional bad lines. Erroring loudly
                                        // beats computing VaR from a fraction of the data.
                                        return Error (QuantumError.ValidationError ("MarketDataPath", $"Only {parsed.Length} of {dataRows} data rows are valid numbers — the file format is probably wrong (expected one invariant-culture numeric return per line): {path}"))
                                    else
                                        return Ok parsed
                        }
                    | None ->
                        async { return Ok (generateMockReturns config.SimulationPaths) }

                match returnsResult with
                | Error err -> return Error err
                | Ok returns ->
                    // 2. Choose quantum or classical path
                    if config.UseAmplitudeEstimation && config.Backend.IsSome then
                        // Quantum path: amplitude estimation for VaR/CVaR
                        let qBackend = config.Backend.Value
                        let! quantumResult = executeQuantumVaR config qBackend returns

                        match quantumResult with
                        | Error err ->
                            // Business outcome: propagate the quantum failure as Error (no classical fallback).
                            return Error err
                        | Ok (quantumVaR, quantumCVaR, _tailProb) ->
                            // Volatility still computed classically (not a tail-risk metric)
                            let vol =
                                if List.contains Volatility config.Metrics then
                                    let mean = Array.average returns
                                    let sumSq = returns |> Array.sumBy (fun x -> pown (x - mean) 2)
                                    ValueSome (sqrt (sumSq / float returns.Length))
                                else ValueNone

                            let executionTime = (System.DateTime.Now - startTime).TotalMilliseconds

                            return Ok {
                                VaR = if List.contains ValueAtRisk config.Metrics then ValueSome quantumVaR else ValueNone
                                CVaR = if List.contains ConditionalVaR config.Metrics then ValueSome quantumCVaR else ValueNone
                                ExpectedShortfall = if List.contains ExpectedShortfall config.Metrics then ValueSome quantumCVaR else ValueNone
                                Volatility = vol
                                ConfidenceLevel = config.ConfidenceLevel
                                ExecutionTimeMs = executionTime
                                Method = "Quantum Amplitude Estimation"
                                Configuration = config
                            }
                    else
                        // Classical path: Monte Carlo
                        let sortedReturns = Array.sort returns
                        let varIndex = int ((1.0 - config.ConfidenceLevel) * float returns.Length)
                        let classicalVaR = -sortedReturns.[varIndex]

                        let vaR = if List.contains ValueAtRisk config.Metrics then ValueSome classicalVaR else ValueNone

                        let tailLosses = sortedReturns |> Array.take (varIndex + 1)
                        let cVaRVal = - (Array.average tailLosses)
                        let cVaR = if List.contains ConditionalVaR config.Metrics then ValueSome cVaRVal else ValueNone
                        let es = if List.contains ExpectedShortfall config.Metrics then ValueSome cVaRVal else ValueNone

                        let vol =
                            if List.contains Volatility config.Metrics then
                                let mean = Array.average returns
                                let sumSq = returns |> Array.sumBy (fun x -> pown (x - mean) 2)
                                ValueSome (sqrt (sumSq / float returns.Length))
                            else ValueNone

                        let executionTime = (System.DateTime.Now - startTime).TotalMilliseconds

                        return Ok {
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
            | Some token ->
                Async.RunSynchronously(executeAsync config, cancellationToken = token)
            | None ->
                executeAsync config |> Async.RunSynchronously
        match result with
        | Ok report -> report
        | Error err ->
            raise (System.InvalidOperationException(
                $"Risk analysis failed: {err.Message}. Use executeAsync to handle this as a Result."))

/// Builder for the Quantum Risk Engine DSL
type QuantumRiskEngineBuilder() =
    member _.Yield(_) = {
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

    member _.Zero() = {
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

    member _.For(state: RiskConfiguration, body: unit -> RiskConfiguration) =
        body()

    member _.Run(state: RiskConfiguration) : QuantumResult<RiskReport> =
        // Propagate a quantum failure as Error rather than raising (executeAsync is Result-typed).
        match state.CancellationToken with
        | Some token -> Async.RunSynchronously(RiskEngine.executeAsync state, cancellationToken = token)
        | None -> RiskEngine.executeAsync state |> Async.RunSynchronously

    member this.Run(f: unit -> RiskConfiguration) : QuantumResult<RiskReport> =
        this.Run(f())

    /// Load market data from a file path
    [<CustomOperation("load_market_data")>]
    member _.LoadMarketData(state: RiskConfiguration, path: string) =
        { state with MarketDataPath = Some path }

    /// Set the confidence level for risk calculations (e.g., 0.99 for 99%)
    [<CustomOperation("set_confidence_level")>]
    member _.SetConfidenceLevel(state: RiskConfiguration, level: float) =
        { state with ConfidenceLevel = level }

    /// Set the number of simulation paths (classical equivalent)
    [<CustomOperation("set_simulation_paths")>]
    member _.SetSimulationPaths(state: RiskConfiguration, paths: int) =
        { state with SimulationPaths = paths }

    /// Enable or disable Quantum Amplitude Estimation for quadratic speedup
    [<CustomOperation("use_amplitude_estimation")>]
    member _.UseAmplitudeEstimation(state: RiskConfiguration, enable: bool) =
        { state with UseAmplitudeEstimation = enable }

    /// Enable or disable error mitigation techniques
    [<CustomOperation("use_error_mitigation")>]
    member _.UseErrorMitigation(state: RiskConfiguration, enable: bool) =
        { state with UseErrorMitigation = enable }

    /// Add a metric to be calculated
    [<CustomOperation("calculate_metric")>]
    member _.CalculateMetric(state: RiskConfiguration, metric: RiskMetric) =
        { state with Metrics = state.Metrics @ [metric] }

    /// Provide a cancellation token for long-running operations
    [<CustomOperation("cancellation_token")>]
    member _.CancellationToken(state: RiskConfiguration, token: System.Threading.CancellationToken) =
        { state with CancellationToken = Some token }

    /// Set number of qubits for QMC operations
    [<CustomOperation("qubits")>]
    member _.Qubits(state: RiskConfiguration, numQubits: int) =
        { state with NumQubits = numQubits }

    /// Set Grover iterations for QMC operations
    [<CustomOperation("iterations")>]
    member _.Iterations(state: RiskConfiguration, groverIterations: int) =
        { state with GroverIterations = groverIterations }

    /// Set number of measurement shots
    [<CustomOperation("shots")>]
    member _.Shots(state: RiskConfiguration, shots: int) =
        { state with Shots = shots }

    /// Set the quantum backend
    [<CustomOperation("backend")>]
    member _.Backend(state: RiskConfiguration, backend: IQuantumBackend) =
        { state with Backend = Some backend }


[<AutoOpen>]
module QuantumRiskEngineDSL =
    let quantumRiskEngine = QuantumRiskEngineBuilder()
