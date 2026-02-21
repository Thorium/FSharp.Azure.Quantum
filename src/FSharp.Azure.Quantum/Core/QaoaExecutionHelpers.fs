namespace FSharp.Azure.Quantum.Core

open System
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
