namespace FSharp.Azure.Quantum.Core

open System
open System.Threading
open System.Threading.Tasks
open FSharp.Azure.Quantum

/// Shared QAOA execution infrastructure for all quantum solvers.
///
/// Consolidates the QAOA execution pattern from DrugDiscoverySolvers (the most mature pattern)
/// into reusable functions. Addresses technical debt:
/// - Debt 2: Eliminates 6 private copies of quboMapToArray across solver files
/// - Debt 3: Defines unified QaoaSolverConfig replacing 7 incompatible config types
/// - Debt 6: Extracts private QAOA helpers from DrugDiscoverySolvers into shared module
///
/// RULE 1 COMPLIANCE:
/// All execution functions require IQuantumBackend parameter (explicit quantum execution).
module QaoaExecutionHelpers =

    // ================================================================================
    // UNIFIED QAOA CONFIGURATION (Decision 7)
    // ================================================================================

    /// Unified QAOA execution configuration.
    /// Captures all core QAOA execution fields shared across solvers.
    /// Solvers with domain-specific config fields (NumColors, RiskAversion, etc.)
    /// should compose this type with their own domain-specific config type.
    type QaoaSolverConfig = {
        /// Number of QAOA layers (p parameter). Higher p = better solutions but slower.
        NumLayers: int
        
        /// Number of shots for optimization phase (lower = faster)
        OptimizationShots: int
        
        /// Number of shots for final execution (higher = better sampling)
        FinalShots: int
        
        /// Enable Nelder-Mead parameter optimization.
        /// When false, uses grid search (faster but lower quality).
        EnableOptimization: bool
        
        /// Enable constraint repair post-processing
        EnableConstraintRepair: bool
        
        /// Maximum optimization iterations for Nelder-Mead
        MaxOptimizationIterations: int
    }

    /// Default QAOA configuration (balanced speed/quality)
    let defaultConfig : QaoaSolverConfig = {
        NumLayers = 2
        OptimizationShots = 100
        FinalShots = 1000
        EnableOptimization = true
        EnableConstraintRepair = true
        MaxOptimizationIterations = 200
    }

    /// Fast configuration (for quick prototyping / grid search only)
    let fastConfig : QaoaSolverConfig = {
        NumLayers = 1
        OptimizationShots = 50
        FinalShots = 500
        EnableOptimization = false
        EnableConstraintRepair = true
        MaxOptimizationIterations = 100
    }

    /// High-quality configuration (for production workloads)
    let highQualityConfig : QaoaSolverConfig = {
        NumLayers = 3
        OptimizationShots = 200
        FinalShots = 2000
        EnableOptimization = true
        EnableConstraintRepair = true
        MaxOptimizationIterations = 500
    }

    // ================================================================================
    // CONFIGURATION VALIDATION
    // ================================================================================

    /// Validate QAOA solver configuration, returning Error if invalid.
    let private validateConfig (config: QaoaSolverConfig) : Result<unit, QuantumError> =
        if config.NumLayers <= 0 then
            Error (QuantumError.ValidationError ("NumLayers", $"must be > 0, got {config.NumLayers}"))
        elif config.OptimizationShots <= 0 then
            Error (QuantumError.ValidationError ("OptimizationShots", $"must be > 0, got {config.OptimizationShots}"))
        elif config.FinalShots <= 0 then
            Error (QuantumError.ValidationError ("FinalShots", $"must be > 0, got {config.FinalShots}"))
        elif config.MaxOptimizationIterations <= 0 then
            Error (QuantumError.ValidationError ("MaxOptimizationIterations", $"must be > 0, got {config.MaxOptimizationIterations}"))
        else
            Ok ()

    // ================================================================================
    // QUBO CONVERSION UTILITIES (Debt 2 consolidation)
    // ================================================================================

    /// Convert GraphOptimization.QuboMatrix (sparse Map) to dense float[,].
    /// Delegates to Qubo.toDenseArray — kept as convenience wrapper for QuboMatrix input.
    /// For new solvers that build QUBO as Map<int*int, float>, use Qubo.toDenseArray directly.
    let quboMapToArray (quboMatrix: GraphOptimization.QuboMatrix) : float[,] =
        Qubo.toDenseArray quboMatrix.NumVariables quboMatrix.Q

    // ================================================================================
    // SHARED QAOA EXECUTION FUNCTIONS (Debt 6 extraction)
    // ================================================================================

    /// Evaluate QUBO objective for a bitstring.
    /// Returns the energy: sum of Q[i,j] * bits[i] * bits[j] for all i,j.
    let evaluateQubo (qubo: float[,]) (bits: int[]) : float =
        let n = Array2D.length1 qubo
        seq {
            for i in 0 .. n - 1 do
                for j in 0 .. n - 1 do
                    yield qubo.[i, j] * float bits.[i] * float bits.[j]
        }
        |> Seq.sum

    /// Execute a single QAOA circuit with given parameters and return measurements.
    /// Pipeline: QUBO -> ProblemHamiltonian -> MixerHamiltonian -> QaoaCircuit -> ICircuit -> backend
    [<Obsolete("Use executeQaoaCircuitAsync for non-blocking execution against cloud backends")>]
    let executeQaoaCircuit
        (backend: BackendAbstraction.IQuantumBackend)
        (problemHam: QaoaCircuit.ProblemHamiltonian)
        (mixerHam: QaoaCircuit.MixerHamiltonian)
        (parameters: (float * float)[])
        (shots: int)
        : Result<int[][], QuantumError> =
        
        let qaoaCircuit = QaoaCircuit.QaoaCircuit.build problemHam mixerHam parameters
        let circuit = CircuitAbstraction.QaoaCircuitWrapper(qaoaCircuit) :> CircuitAbstraction.ICircuit
        
        match backend.ExecuteToState circuit with
        | Error err -> Error err
        | Ok state -> Ok (QuantumState.measure state shots)

    /// Execute a single QAOA circuit asynchronously with given parameters and return measurements.
    /// Uses backend.ExecuteToStateAsync for non-blocking I/O against cloud backends.
    /// Pipeline: QUBO -> ProblemHamiltonian -> MixerHamiltonian -> QaoaCircuit -> ICircuit -> backend
    let executeQaoaCircuitAsync
        (backend: BackendAbstraction.IQuantumBackend)
        (problemHam: QaoaCircuit.ProblemHamiltonian)
        (mixerHam: QaoaCircuit.MixerHamiltonian)
        (parameters: (float * float)[])
        (shots: int)
        (cancellationToken: CancellationToken)
        : Task<Result<int[][], QuantumError>> =
        task {
            let qaoaCircuit = QaoaCircuit.QaoaCircuit.build problemHam mixerHam parameters
            let circuit = CircuitAbstraction.QaoaCircuitWrapper(qaoaCircuit) :> CircuitAbstraction.ICircuit

            let! result = backend.ExecuteToStateAsync circuit cancellationToken
            return
                match result with
                | Error err -> Error err
                | Ok state -> Ok (QuantumState.measure state shots)
        }

    /// Execute QAOA from a dense QUBO matrix asynchronously.
    /// Builds Hamiltonians, circuit, executes via backend.ExecuteToStateAsync,
    /// and returns measurements.
    let executeFromQuboAsync
        (backend: BackendAbstraction.IQuantumBackend)
        (qubo: float[,])
        (parameters: (float * float)[])
        (shots: int)
        (cancellationToken: CancellationToken)
        : Task<Result<int[][], QuantumError>> =

        let n = Array2D.length1 qubo
        let problemHam = QaoaCircuit.ProblemHamiltonian.fromQubo qubo
        let mixerHam = QaoaCircuit.MixerHamiltonian.create n
        executeQaoaCircuitAsync backend problemHam mixerHam parameters shots cancellationToken

    /// Execute a single QAOA circuit from sparse QUBO representation asynchronously.
    /// Avoids allocating dense float[,] array — calls ProblemHamiltonian.fromQuboSparse.
    let executeQaoaCircuitSparseAsync
        (backend: BackendAbstraction.IQuantumBackend)
        (numQubits: int)
        (quboMap: Map<int * int, float>)
        (parameters: (float * float)[])
        (shots: int)
        (cancellationToken: CancellationToken)
        : Task<Result<int[][], QuantumError>> =

        let problemHam = QaoaCircuit.ProblemHamiltonian.fromQuboSparse numQubits quboMap
        let mixerHam = QaoaCircuit.MixerHamiltonian.create numQubits
        executeQaoaCircuitAsync backend problemHam mixerHam parameters shots cancellationToken

    /// Create objective function closure for Nelder-Mead optimization.
    /// Returns expectation value of QUBO Hamiltonian (lower = better).
    /// The returned function converts flat parameter array to (gamma, beta) pairs,
    /// executes the QAOA circuit, and returns average QUBO energy.
    let createObjectiveFunction
        (backend: BackendAbstraction.IQuantumBackend)
        (qubo: float[,])
        (problemHam: QaoaCircuit.ProblemHamiltonian)
        (mixerHam: QaoaCircuit.MixerHamiltonian)
        (numLayers: int)
        (shots: int)
        : float[] -> float =
        
        fun (flatParams: float[]) ->
            // Convert flat array to (gamma, beta) pairs
            let parameters = 
                Array.init numLayers (fun i ->
                    let gamma = flatParams.[2 * i]
                    let beta = flatParams.[2 * i + 1]
                    (gamma, beta))
            
            match executeQaoaCircuit backend problemHam mixerHam parameters shots with
            | Error _ -> System.Double.MaxValue  // Penalty for failed execution
            | Ok measurements ->
                // Calculate average QUBO energy across all measurements
                measurements
                |> Array.map (fun bits -> evaluateQubo qubo bits)
                |> Array.average

    /// Execute QAOA with Nelder-Mead parameter optimization.
    /// Returns: (bestBitstring, optimizedParameters, converged)
    /// Uses QaoaOptimizer.minimizeWithBounds for bounded Nelder-Mead.
    let executeQaoaWithOptimization
        (backend: BackendAbstraction.IQuantumBackend)
        (qubo: float[,])
        (config: QaoaSolverConfig)
        : Result<int[] * (float * float)[] * bool, QuantumError> =
        
        match validateConfig config with
        | Error err -> Error err
        | Ok () ->
        
        let n = Array2D.length1 qubo
        let problemHam = QaoaCircuit.ProblemHamiltonian.fromQubo qubo
        let mixerHam = QaoaCircuit.MixerHamiltonian.create n
        
        // Create objective function for optimization
        let objectiveFunc = createObjectiveFunction backend qubo problemHam mixerHam config.NumLayers config.OptimizationShots
        
        // Initial parameters: use standard QAOA heuristic
        // gamma in [0, pi/2], beta in [0, pi/4] works well for many problems
        let rng = Random(42)  // Fixed seed for reproducibility
        let initialParams = 
            Array.init (2 * config.NumLayers) (fun i ->
                if i % 2 = 0 then 
                    rng.NextDouble() * (Math.PI / 2.0)  // gamma
                else 
                    rng.NextDouble() * (Math.PI / 4.0)) // beta
        
        // Parameter bounds
        let lowerBounds = Array.init (2 * config.NumLayers) (fun _ -> 0.0)
        let upperBounds = Array.init (2 * config.NumLayers) (fun i ->
            if i % 2 = 0 then Math.PI else Math.PI / 2.0)
        
        // Run Nelder-Mead optimization (may throw MaximumIterationsException)
        let optimResult, converged = 
            try
                let result = QaoaOptimizer.Optimizer.minimizeWithBounds 
                                objectiveFunc initialParams lowerBounds upperBounds
                (result, result.Converged)
            with
            | :? MathNet.Numerics.Optimization.MaximumIterationsException ->
                // Optimizer didn't converge - use initial parameters as fallback
                ({ QaoaOptimizer.OptimizationResult.OptimizedParameters = initialParams
                   QaoaOptimizer.OptimizationResult.FinalObjectiveValue = System.Double.MaxValue
                   QaoaOptimizer.OptimizationResult.Converged = false
                   QaoaOptimizer.OptimizationResult.Iterations = config.MaxOptimizationIterations }, false)
        
        // Extract optimized parameters (or initial if optimization failed)
        let optimizedParams =
            Array.init config.NumLayers (fun i ->
                (optimResult.OptimizedParameters.[2 * i], 
                 optimResult.OptimizedParameters.[2 * i + 1]))
        
        // Execute final circuit with optimized parameters and more shots
        match executeQaoaCircuit backend problemHam mixerHam optimizedParams config.FinalShots with
        | Error err -> Error err
        | Ok measurements ->
            // Find best solution (lowest QUBO energy)
            let bestSolution =
                measurements
                |> Array.minBy (fun bits -> evaluateQubo qubo bits)
            
            Ok (bestSolution, optimizedParams, converged)

    /// Execute QAOA with grid search (fallback when optimization disabled).
    /// Returns: (bestBitstring, bestParameters)
    /// Searches over a grid of (gamma, beta) values using config.NumLayers layers.
    [<Obsolete("Use executeQaoaWithGridSearchAsync for non-blocking execution with optional parallelism")>]
    let executeQaoaWithGridSearch
        (backend: BackendAbstraction.IQuantumBackend)
        (qubo: float[,])
        (config: QaoaSolverConfig)
        : Result<int[] * (float * float)[], QuantumError> =
        
        match validateConfig config with
        | Error err -> Error err
        | Ok () ->
        
        let n = Array2D.length1 qubo
        let problemHam = QaoaCircuit.ProblemHamiltonian.fromQubo qubo
        let mixerHam = QaoaCircuit.MixerHamiltonian.create n
        
        // Grid search parameter sets for multi-layer QAOA
        let gammaValues = [| 0.1; 0.3; 0.5; 0.7; 1.0; 1.5; Math.PI / 4.0 |]
        let betaValues = [| 0.1; 0.3; 0.5; 0.7; 1.0 |]
        
        let initialState = {| BestSolution = None; BestEnergy = System.Double.MaxValue; BestParams = Array.empty<float * float>; LastError = None |}
        
        // Try different parameter combinations
        let result =
            (initialState, seq {
                for gamma in gammaValues do
                    for beta in betaValues do
                        yield (gamma, beta)
            })
            ||> Seq.fold (fun state (gamma, beta) ->
                // Create multi-layer parameters (same gamma/beta for each layer)
                let parameters = Array.init config.NumLayers (fun _ -> (gamma, beta))
                
                match executeQaoaCircuit backend problemHam mixerHam parameters config.OptimizationShots with
                | Error err -> 
                    {| state with LastError = Some err |}
                | Ok measurements ->
                    // Find best measurement in this batch
                    let candidate = 
                        measurements
                        |> Array.minBy (fun bits -> evaluateQubo qubo bits)
                    
                    let energy = evaluateQubo qubo candidate
                    if energy < state.BestEnergy then
                        {| state with BestSolution = Some candidate; BestEnergy = energy; BestParams = parameters |}
                    else
                        state)
        
        match result.BestSolution with
        | Some _ ->
            // Re-execute with FinalShots using the best parameters found
            match executeQaoaCircuit backend problemHam mixerHam result.BestParams config.FinalShots with
            | Error err -> Error err
            | Ok measurements ->
                let bestSolution =
                    measurements
                    |> Array.minBy (fun bits -> evaluateQubo qubo bits)
                Ok (bestSolution, result.BestParams)
        | None -> 
            match result.LastError with
            | Some err -> Error err
            | None -> Error (QuantumError.OperationError ("QAOA", "No valid solution found"))

    /// Execute QAOA with grid search asynchronously.
    /// Returns: (bestBitstring, bestParameters)
    ///
    /// The maxConcurrency parameter controls how many grid search evaluations
    /// run concurrently. Default is 1 (sequential) to limit memory usage on
    /// local simulators. Set higher (e.g. 10-35) for cloud backends where
    /// submissions are I/O-bound and memory is remote.
    let executeQaoaWithGridSearchAsync
        (backend: BackendAbstraction.IQuantumBackend)
        (qubo: float[,])
        (config: QaoaSolverConfig)
        (maxConcurrency: int)
        (cancellationToken: CancellationToken)
        : Task<Result<int[] * (float * float)[], QuantumError>> =
        task {
            match validateConfig config with
            | Error err -> return Error err
            | Ok () ->

            let n = Array2D.length1 qubo
            let problemHam = QaoaCircuit.ProblemHamiltonian.fromQubo qubo
            let mixerHam = QaoaCircuit.MixerHamiltonian.create n

            let gammaValues = [| 0.1; 0.3; 0.5; 0.7; 1.0; 1.5; Math.PI / 4.0 |]
            let betaValues = [| 0.1; 0.3; 0.5; 0.7; 1.0 |]

            let parameterSets =
                [| for gamma in gammaValues do
                       for beta in betaValues do
                           Array.init config.NumLayers (fun _ -> (gamma, beta)) |]

            let concurrency = max 1 (min maxConcurrency parameterSets.Length)
            use semaphore = new SemaphoreSlim(concurrency, concurrency)

            let executeOne (parameters: (float * float)[]) =
                task {
                    do! semaphore.WaitAsync(cancellationToken)
                    try
                        let! result = executeQaoaCircuitAsync backend problemHam mixerHam parameters config.OptimizationShots cancellationToken
                        return
                            match result with
                            | Error err -> Error err
                            | Ok measurements ->
                                let candidate = measurements |> Array.minBy (fun bits -> evaluateQubo qubo bits)
                                let energy = evaluateQubo qubo candidate
                                Ok (candidate, energy, parameters)
                    finally
                        semaphore.Release() |> ignore
                }

            let! results = parameterSets |> Array.map executeOne |> Task.WhenAll

            // Find best across all results
            let mutable bestSolution = None
            let mutable bestEnergy = Double.MaxValue
            let mutable bestParams = Array.empty<float * float>
            let mutable lastError = None

            for r in results do
                match r with
                | Error err -> lastError <- Some err
                | Ok (candidate, energy, parameters) ->
                    if energy < bestEnergy then
                        bestSolution <- Some candidate
                        bestEnergy <- energy
                        bestParams <- parameters

            match bestSolution with
            | Some _ ->
                // Re-execute with FinalShots using the best parameters found
                let! finalResult = executeQaoaCircuitAsync backend problemHam mixerHam bestParams config.FinalShots cancellationToken
                return
                    match finalResult with
                    | Error err -> Error err
                    | Ok measurements ->
                        let best = measurements |> Array.minBy (fun bits -> evaluateQubo qubo bits)
                        Ok (best, bestParams)
            | None ->
                return
                    match lastError with
                    | Some err -> Error err
                    | None -> Error (QuantumError.OperationError ("QAOA", "No valid solution found"))
        }

    // ================================================================================
    // DENSE QUBO CONVENIENCE (for old solver migration — Task 2)
    // ================================================================================

    /// Execute QAOA from a dense QUBO matrix: builds Hamiltonians, circuit, executes,
    /// and returns measurements. This is the convenience entry-point for old solvers
    /// that have their own config types and just need the circuit execution pipeline.
    ///
    /// Parameters:
    ///   backend    - quantum backend to execute on
    ///   qubo       - dense QUBO matrix (float[,])
    ///   parameters - array of (gamma, beta) tuples, one per QAOA layer
    ///   shots      - number of measurement shots
    ///
    /// Returns: Ok measurements or Error
    [<Obsolete("Use executeFromQuboAsync for non-blocking execution against cloud backends")>]
    let executeFromQubo
        (backend: BackendAbstraction.IQuantumBackend)
        (qubo: float[,])
        (parameters: (float * float)[])
        (shots: int)
        : Result<int[][], QuantumError> =

        let n = Array2D.length1 qubo
        let problemHam = QaoaCircuit.ProblemHamiltonian.fromQubo qubo
        let mixerHam = QaoaCircuit.MixerHamiltonian.create n
        executeQaoaCircuit backend problemHam mixerHam parameters shots

    // ================================================================================
    // SPARSE QUBO EXECUTION (Task 1 — memory-efficient path)
    // ================================================================================

    /// Evaluate sparse QUBO objective for a bitstring.
    /// Returns the energy: sum of Q[(i,j)] * bits[i] * bits[j] for all entries.
    let evaluateQuboSparse (quboMap: Map<int * int, float>) (bits: int[]) : float =
        quboMap
        |> Map.fold (fun acc (i, j) qij ->
            acc + qij * float bits.[i] * float bits.[j]) 0.0

    /// Execute a single QAOA circuit from sparse QUBO representation.
    /// Avoids allocating dense float[,] array — calls ProblemHamiltonian.fromQuboSparse.
    [<Obsolete("Use executeQaoaCircuitSparseAsync for non-blocking execution against cloud backends")>]
    let executeQaoaCircuitSparse
        (backend: BackendAbstraction.IQuantumBackend)
        (numQubits: int)
        (quboMap: Map<int * int, float>)
        (parameters: (float * float)[])
        (shots: int)
        : Result<int[][], QuantumError> =

        let problemHam = QaoaCircuit.ProblemHamiltonian.fromQuboSparse numQubits quboMap
        let mixerHam = QaoaCircuit.MixerHamiltonian.create numQubits
        executeQaoaCircuit backend problemHam mixerHam parameters shots

    /// Execute QAOA with Nelder-Mead optimization from sparse QUBO.
    /// Returns: (bestBitstring, optimizedParameters, converged)
    let executeQaoaWithOptimizationSparse
        (backend: BackendAbstraction.IQuantumBackend)
        (numQubits: int)
        (quboMap: Map<int * int, float>)
        (config: QaoaSolverConfig)
        : Result<int[] * (float * float)[] * bool, QuantumError> =

        match validateConfig config with
        | Error err -> Error err
        | Ok () ->

        let problemHam = QaoaCircuit.ProblemHamiltonian.fromQuboSparse numQubits quboMap
        let mixerHam = QaoaCircuit.MixerHamiltonian.create numQubits

        // Build a dense array on the fly for the objective function (evaluateQubo needs it)
        // For very large problems, callers should use evaluateQuboSparse directly.
        let objectiveFunc =
            fun (flatParams: float[]) ->
                let parameters =
                    Array.init config.NumLayers (fun i ->
                        (flatParams.[2 * i], flatParams.[2 * i + 1]))

                match executeQaoaCircuit backend problemHam mixerHam parameters config.OptimizationShots with
                | Error _ -> System.Double.MaxValue
                | Ok measurements ->
                    measurements
                    |> Array.map (fun bits -> evaluateQuboSparse quboMap bits)
                    |> Array.average

        let rng = Random(42)
        let initialParams =
            Array.init (2 * config.NumLayers) (fun i ->
                if i % 2 = 0 then
                    rng.NextDouble() * (Math.PI / 2.0)
                else
                    rng.NextDouble() * (Math.PI / 4.0))

        let lowerBounds = Array.init (2 * config.NumLayers) (fun _ -> 0.0)
        let upperBounds = Array.init (2 * config.NumLayers) (fun i ->
            if i % 2 = 0 then Math.PI else Math.PI / 2.0)

        let optimResult, converged =
            try
                let result = QaoaOptimizer.Optimizer.minimizeWithBounds
                                objectiveFunc initialParams lowerBounds upperBounds
                (result, result.Converged)
            with
            | :? MathNet.Numerics.Optimization.MaximumIterationsException ->
                ({ QaoaOptimizer.OptimizationResult.OptimizedParameters = initialParams
                   QaoaOptimizer.OptimizationResult.FinalObjectiveValue = System.Double.MaxValue
                   QaoaOptimizer.OptimizationResult.Converged = false
                   QaoaOptimizer.OptimizationResult.Iterations = config.MaxOptimizationIterations }, false)

        let optimizedParams =
            Array.init config.NumLayers (fun i ->
                (optimResult.OptimizedParameters.[2 * i],
                 optimResult.OptimizedParameters.[2 * i + 1]))

        match executeQaoaCircuit backend problemHam mixerHam optimizedParams config.FinalShots with
        | Error err -> Error err
        | Ok measurements ->
            let bestSolution =
                measurements
                |> Array.minBy (fun bits -> evaluateQuboSparse quboMap bits)
            Ok (bestSolution, optimizedParams, converged)

    /// Execute QAOA with grid search from sparse QUBO.
    /// Returns: (bestBitstring, bestParameters)
    [<Obsolete("Use executeQaoaWithGridSearchSparseAsync for non-blocking execution with optional parallelism")>]
    let executeQaoaWithGridSearchSparse
        (backend: BackendAbstraction.IQuantumBackend)
        (numQubits: int)
        (quboMap: Map<int * int, float>)
        (config: QaoaSolverConfig)
        : Result<int[] * (float * float)[], QuantumError> =

        match validateConfig config with
        | Error err -> Error err
        | Ok () ->

        let problemHam = QaoaCircuit.ProblemHamiltonian.fromQuboSparse numQubits quboMap
        let mixerHam = QaoaCircuit.MixerHamiltonian.create numQubits

        let gammaValues = [| 0.1; 0.3; 0.5; 0.7; 1.0; 1.5; Math.PI / 4.0 |]
        let betaValues = [| 0.1; 0.3; 0.5; 0.7; 1.0 |]

        let initialState = {| BestSolution = None; BestEnergy = System.Double.MaxValue; BestParams = Array.empty<float * float>; LastError = None |}

        let result =
            (initialState, seq {
                for gamma in gammaValues do
                    for beta in betaValues do
                        yield (gamma, beta)
            })
            ||> Seq.fold (fun state (gamma, beta) ->
                let parameters = Array.init config.NumLayers (fun _ -> (gamma, beta))

                match executeQaoaCircuit backend problemHam mixerHam parameters config.OptimizationShots with
                | Error err ->
                    {| state with LastError = Some err |}
                | Ok measurements ->
                    let candidate =
                        measurements
                        |> Array.minBy (fun bits -> evaluateQuboSparse quboMap bits)

                    let energy = evaluateQuboSparse quboMap candidate
                    if energy < state.BestEnergy then
                        {| state with BestSolution = Some candidate; BestEnergy = energy; BestParams = parameters |}
                    else
                        state)

        match result.BestSolution with
        | Some _ ->
            match executeQaoaCircuit backend problemHam mixerHam result.BestParams config.FinalShots with
            | Error err -> Error err
            | Ok measurements ->
                let bestSolution =
                    measurements
                    |> Array.minBy (fun bits -> evaluateQuboSparse quboMap bits)
                Ok (bestSolution, result.BestParams)
        | None ->
            match result.LastError with
            | Some err -> Error err
            | None -> Error (QuantumError.OperationError ("QAOA", "No valid solution found"))

    /// Execute QAOA with grid search from sparse QUBO asynchronously.
    /// Returns: (bestBitstring, bestParameters)
    ///
    /// The maxConcurrency parameter controls how many grid search evaluations
    /// run concurrently. Default is 1 (sequential) to limit memory usage on
    /// local simulators. Set higher for cloud backends.
    let executeQaoaWithGridSearchSparseAsync
        (backend: BackendAbstraction.IQuantumBackend)
        (numQubits: int)
        (quboMap: Map<int * int, float>)
        (config: QaoaSolverConfig)
        (maxConcurrency: int)
        (cancellationToken: CancellationToken)
        : Task<Result<int[] * (float * float)[], QuantumError>> =
        task {
            match validateConfig config with
            | Error err -> return Error err
            | Ok () ->

            let problemHam = QaoaCircuit.ProblemHamiltonian.fromQuboSparse numQubits quboMap
            let mixerHam = QaoaCircuit.MixerHamiltonian.create numQubits

            let gammaValues = [| 0.1; 0.3; 0.5; 0.7; 1.0; 1.5; Math.PI / 4.0 |]
            let betaValues = [| 0.1; 0.3; 0.5; 0.7; 1.0 |]

            let parameterSets =
                [| for gamma in gammaValues do
                       for beta in betaValues do
                           Array.init config.NumLayers (fun _ -> (gamma, beta)) |]

            let concurrency = max 1 (min maxConcurrency parameterSets.Length)
            use semaphore = new SemaphoreSlim(concurrency, concurrency)

            let executeOne (parameters: (float * float)[]) =
                task {
                    do! semaphore.WaitAsync(cancellationToken)
                    try
                        let! result = executeQaoaCircuitAsync backend problemHam mixerHam parameters config.OptimizationShots cancellationToken
                        return
                            match result with
                            | Error err -> Error err
                            | Ok measurements ->
                                let candidate = measurements |> Array.minBy (fun bits -> evaluateQuboSparse quboMap bits)
                                let energy = evaluateQuboSparse quboMap candidate
                                Ok (candidate, energy, parameters)
                    finally
                        semaphore.Release() |> ignore
                }

            let! results = parameterSets |> Array.map executeOne |> Task.WhenAll

            let mutable bestSolution = None
            let mutable bestEnergy = Double.MaxValue
            let mutable bestParams = Array.empty<float * float>
            let mutable lastError = None

            for r in results do
                match r with
                | Error err -> lastError <- Some err
                | Ok (candidate, energy, parameters) ->
                    if energy < bestEnergy then
                        bestSolution <- Some candidate
                        bestEnergy <- energy
                        bestParams <- parameters

            match bestSolution with
            | Some _ ->
                let! finalResult = executeQaoaCircuitAsync backend problemHam mixerHam bestParams config.FinalShots cancellationToken
                return
                    match finalResult with
                    | Error err -> Error err
                    | Ok measurements ->
                        let best = measurements |> Array.minBy (fun bits -> evaluateQuboSparse quboMap bits)
                        Ok (best, bestParams)
            | None ->
                return
                    match lastError with
                    | Some err -> Error err
                    | None -> Error (QuantumError.OperationError ("QAOA", "No valid solution found"))
        }

    // ================================================================================
    // BUDGET-CONSTRAINED EXECUTION (Task 5)
    // ================================================================================

    /// Capacity-check strategy for budget-constrained execution.
    ///
    /// Mirrors ProblemDecomposition.DecompositionStrategy but lives here to avoid
    /// a compile-order dependency (QaoaExecutionHelpers compiles before ProblemDecomposition).
    type BudgetDecompositionStrategy =
        /// Run as-is (no capacity check).
        | NoBudgetDecomposition
        /// Error if problem exceeds this fixed qubit limit.
        | FixedQubitLimit of maxQubits: int
        /// Error if problem exceeds backend's MaxQubits (IQubitLimitedBackend).
        | AdaptiveToBudgetBackend

    /// Budget constraints for QAOA execution.
    ///
    /// Controls total resource usage and provides a safety check against
    /// exceeding backend qubit capacity.
    type ExecutionBudget = {
        /// Maximum total measurement shots across all sub-problems.
        /// Shots are divided equally among decomposed sub-problems.
        MaxTotalShots: int

        /// Optional wall-clock time limit in milliseconds.
        /// Execution stops early if time is exceeded (best-effort).
        MaxTimeMs: int option

        /// Capacity-check strategy for large problems.
        Decomposition: BudgetDecompositionStrategy
    }

    /// Default execution budget: 1000 shots, no time limit, adaptive capacity check.
    let defaultBudget : ExecutionBudget = {
        MaxTotalShots = 1000
        MaxTimeMs = None
        Decomposition = AdaptiveToBudgetBackend
    }

    /// Execute QAOA with budget constraints and capacity checking.
    ///
    /// This is the highest-level QAOA execution entry point. It:
    /// 1. Validates configuration and budget
    /// 2. Checks backend capacity (MaxQubits via IQubitLimitedBackend)
    /// 3. Returns clear error if problem exceeds capacity
    /// 4. Applies shot budget limit to config
    /// 5. Respects optional time limit
    ///
    /// For automatic problem decomposition, use solver-level
    /// ProblemDecomposition.solveWithDecomposition instead.
    ///
    /// Parameters:
    ///   backend  - quantum backend (checked for IQubitLimitedBackend)
    ///   qubo     - dense QUBO matrix
    ///   config   - QAOA solver configuration
    ///   budget   - execution budget constraints
    ///
    /// Returns: Ok (bestBitstring, parameters, converged) or Error
    [<Obsolete("Use executeWithBudgetAsync for non-blocking execution against cloud backends")>]
    let executeWithBudget
        (backend: BackendAbstraction.IQuantumBackend)
        (qubo: float[,])
        (config: QaoaSolverConfig)
        (budget: ExecutionBudget)
        : Result<int[] * (float * float)[] * bool, QuantumError> =

        match validateConfig config with
        | Error err -> Error err
        | Ok () ->

        if budget.MaxTotalShots <= 0 then
            Error (QuantumError.ValidationError ("MaxTotalShots", $"must be > 0, got {budget.MaxTotalShots}"))
        else

        let n = Array2D.length1 qubo
        let stopwatch = System.Diagnostics.Stopwatch.StartNew()

        let isTimeExceeded () =
            match budget.MaxTimeMs with
            | Some maxMs -> stopwatch.ElapsedMilliseconds > int64 maxMs
            | None -> false

        // Check if problem exceeds capacity
        let maxQubits = BackendAbstraction.UnifiedBackend.getMaxQubits backend
        let exceedsCapacity =
            match budget.Decomposition with
            | NoBudgetDecomposition -> false
            | FixedQubitLimit limit -> n > limit
            | AdaptiveToBudgetBackend ->
                match maxQubits with
                | Some limit -> n > limit
                | None -> false

        if isTimeExceeded () then
            Error (QuantumError.OperationError ("QAOA", "Time budget exceeded before execution started"))
        elif exceedsCapacity then
            // Problem exceeds capacity — return clear error.
            // QUBO-level decomposition requires solver-level knowledge.
            // Use ProblemDecomposition.solveWithDecomposition for auto-splitting.
            let limitStr =
                match maxQubits with
                | Some limit -> $"{limit}"
                | None -> "unknown"
            Error (QuantumError.OperationError (
                "QAOA",
                $"Problem requires {n} qubits but backend supports {limitStr}. " +
                "Use solver-level decomposition (solveWithConfig) for automatic splitting, " +
                "or reduce problem size."))
        else
            // Single execution with budget-limited shots
            let adjustedConfig = { config with FinalShots = min config.FinalShots budget.MaxTotalShots }
            if config.EnableOptimization then
                executeQaoaWithOptimization backend qubo adjustedConfig
            else
                executeQaoaWithGridSearch backend qubo adjustedConfig
                |> Result.map (fun (bits, ps) -> (bits, ps, false))

    /// Execute QAOA with budget constraints and capacity checking asynchronously.
    ///
    /// This is the highest-level async QAOA execution entry point. It:
    /// 1. Validates configuration and budget
    /// 2. Checks backend capacity (MaxQubits via IQubitLimitedBackend)
    /// 3. Returns clear error if problem exceeds capacity
    /// 4. Applies shot budget limit to config
    /// 5. Respects optional time limit
    ///
    /// The maxConcurrency parameter controls grid search parallelism.
    /// Default 1 = sequential. Set higher for cloud backends.
    ///
    /// Note: Nelder-Mead optimization path remains synchronous internally
    /// (each step depends on the previous evaluation), but the final execution
    /// uses the async path.
    let executeWithBudgetAsync
        (backend: BackendAbstraction.IQuantumBackend)
        (qubo: float[,])
        (config: QaoaSolverConfig)
        (budget: ExecutionBudget)
        (maxConcurrency: int)
        (cancellationToken: CancellationToken)
        : Task<Result<int[] * (float * float)[] * bool, QuantumError>> =
        task {
            match validateConfig config with
            | Error err -> return Error err
            | Ok () ->

            if budget.MaxTotalShots <= 0 then
                return Error (QuantumError.ValidationError ("MaxTotalShots", $"must be > 0, got {budget.MaxTotalShots}"))
            else

            let n = Array2D.length1 qubo
            let stopwatch = System.Diagnostics.Stopwatch.StartNew()

            let isTimeExceeded () =
                match budget.MaxTimeMs with
                | Some maxMs -> stopwatch.ElapsedMilliseconds > int64 maxMs
                | None -> false

            let maxQubits = BackendAbstraction.UnifiedBackend.getMaxQubits backend
            let exceedsCapacity =
                match budget.Decomposition with
                | NoBudgetDecomposition -> false
                | FixedQubitLimit limit -> n > limit
                | AdaptiveToBudgetBackend ->
                    match maxQubits with
                    | Some limit -> n > limit
                    | None -> false

            if isTimeExceeded () then
                return Error (QuantumError.OperationError ("QAOA", "Time budget exceeded before execution started"))
            elif exceedsCapacity then
                let limitStr =
                    match maxQubits with
                    | Some limit -> $"{limit}"
                    | None -> "unknown"
                return Error (QuantumError.OperationError (
                    "QAOA",
                    $"Problem requires {n} qubits but backend supports {limitStr}. " +
                    "Use solver-level decomposition (solveWithConfig) for automatic splitting, " +
                    "or reduce problem size."))
            else
                let adjustedConfig = { config with FinalShots = min config.FinalShots budget.MaxTotalShots }
                if config.EnableOptimization then
                    // Nelder-Mead is inherently sequential — run sync optimization,
                    // but wrap in task to keep the async contract.
                    return executeQaoaWithOptimization backend qubo adjustedConfig
                else
                    let! result = executeQaoaWithGridSearchAsync backend qubo adjustedConfig maxConcurrency cancellationToken
                    return result |> Result.map (fun (bits, ps) -> (bits, ps, false))
        }