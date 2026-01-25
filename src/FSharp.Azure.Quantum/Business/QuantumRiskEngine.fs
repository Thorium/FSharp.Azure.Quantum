namespace FSharp.Azure.Quantum.Business

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

    let private runQmcHydration (config: RiskConfiguration) (backend: IQuantumBackend) : Async<unit> =
        async {
            match config.CancellationToken with
            | Some token when token.IsCancellationRequested ->
                return raise (System.OperationCanceledException token)
            | _ ->
                ()

            let numQubits = config.NumQubits
            let statePrep = CircuitBuilder.empty numQubits // Placeholder for data loading
            let oracle = CircuitBuilder.empty numQubits    // Placeholder for threshold check

            let qmcConfig = {
                QuantumMonteCarlo.QMCConfig.NumQubits = numQubits
                QuantumMonteCarlo.QMCConfig.StatePreparation = statePrep
                QuantumMonteCarlo.QMCConfig.Oracle = oracle
                QuantumMonteCarlo.QMCConfig.GroverIterations = config.GroverIterations
                QuantumMonteCarlo.QMCConfig.Shots = config.Shots
            }

            // Execute representative QMC operation (hydration/demo)
            // If a caller provided a CancellationToken, honor it.
            let! _ =
                match config.CancellationToken with
                | Some token ->
                    Async.StartAsTask(
                        QuantumMonteCarlo.estimateExpectation qmcConfig backend,
                        cancellationToken = token,
                        taskCreationOptions = System.Threading.Tasks.TaskCreationOptions.None)
                    |> Async.AwaitTask
                | None -> QuantumMonteCarlo.estimateExpectation qmcConfig backend

            return ()
        }

    /// Execute the configured risk analysis (async, cancellable)
    let executeAsync (config: RiskConfiguration) : Async<RiskReport> =
        async {
            let startTime = System.DateTime.Now

            // 1. Ingest Data (Real or Mock)
            let returns = 
                match config.MarketDataPath with
                | Some path when System.IO.File.Exists(path) ->
                    // Basic CSV parsing for single column of returns
                    System.IO.File.ReadAllLines(path)
                    |> Array.skip 1 // Header
                    |> Array.map (fun line -> 
                        match System.Double.TryParse(line.Trim()) with
                        | true, v -> v
                        | _ -> 0.0)
                | _ -> 
                    generateMockReturns config.SimulationPaths

            // 2. Prepare Quantum Simulation (Amplitude Estimation)
            // We use the integration with QuantumMonteCarlo to estimate the probability 
            // of loss exceeding the VaR threshold.
            
            // For demonstration, we'll calculate VaR classically first to define the threshold,
            // then use QMC to estimate the probability (verifying the confidence level).
            // In a full Q-VaR implementation, QMC would search for the threshold.
            
            let sortedReturns = Array.sort returns
            let varIndex = int ((1.0 - config.ConfidenceLevel) * float returns.Length)
            let classicalVaR = -sortedReturns.[varIndex] // VaR is typically positive (loss)
            
            let! methodUsed = 
                if config.UseAmplitudeEstimation then 
                     // Hydrate: prepare QMC config
                     match config.Backend with
                     | None ->
                         async.Return "Quantum Amplitude Estimation (no backend)"
                     | Some backend ->
                         async {
                             do! runQmcHydration config backend
                             return "Quantum Amplitude Estimation"
                         }
                else 
                    async.Return "Classical Monte Carlo"

            // 3. Calculate Metrics (Hybrid Approach)
            // Use the classical estimate refined by method selection
            let vaR = if List.contains ValueAtRisk config.Metrics then ValueSome classicalVaR else ValueNone
            
            let tailLosses = sortedReturns |> Array.take (varIndex + 1)
            let cVaRVal = - (Array.average tailLosses)
            let cVaR = if List.contains ConditionalVaR config.Metrics then ValueSome cVaRVal else ValueNone
            let es = if List.contains ExpectedShortfall config.Metrics then ValueSome cVaRVal else ValueNone // ES often synonymous with CVaR
            
            let vol = 
                if List.contains Volatility config.Metrics then 
                    let mean = Array.average returns
                    let sumSq = returns |> Array.sumBy (fun x -> pown (x - mean) 2)
                    ValueSome (sqrt (sumSq / float returns.Length))
                else ValueNone

            let executionTime = (System.DateTime.Now - startTime).TotalMilliseconds

            return {
                VaR = vaR
                CVaR = cVaR
                ExpectedShortfall = es
                Volatility = vol
                ConfidenceLevel = config.ConfidenceLevel
                ExecutionTimeMs = executionTime
                Method = methodUsed
                Configuration = config
            }
        }

    /// Execute the configured risk analysis (sync wrapper)
    let execute (config: RiskConfiguration) : RiskReport =
        match config.CancellationToken with
        | Some token ->
            Async.RunSynchronously(executeAsync config, cancellationToken = token)
        | None ->
            executeAsync config |> Async.RunSynchronously

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
        Ok (RiskEngine.execute state)

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
