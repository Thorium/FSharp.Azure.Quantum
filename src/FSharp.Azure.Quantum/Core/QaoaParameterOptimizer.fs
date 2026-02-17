namespace FSharp.Azure.Quantum.Core

/// Advanced QAOA Parameter Optimization
///
/// This module provides sophisticated parameter optimization strategies for QAOA:
/// - Multi-start optimization with random initialization
/// - Layer-by-layer optimization for deep QAOA circuits
/// - Adaptive parameter bounds based on problem structure
/// - Integration with quantum backends for objective function evaluation
/// - Convergence detection and early stopping
///
/// Key Algorithms:
/// - Nelder-Mead Simplex (derivative-free, robust to noise)
/// - Multi-restart strategy (escape local minima)
/// - Layer-by-layer optimization (scale to deep circuits)
module QaoaParameterOptimizer =
    
    open System
    open FSharp.Azure.Quantum.Core.QaoaCircuit
    open FSharp.Azure.Quantum.Core.CircuitAbstraction
    open FSharp.Azure.Quantum.Core.BackendAbstraction
    open FSharp.Azure.Quantum.Core.QaoaOptimizer
    open Microsoft.Extensions.Logging
    
    
    // ============================================================================
    // TYPES
    // ============================================================================
    
    /// Strategy for parameter initialization
    type InitializationStrategy =
        | RandomUniform      // Random values in [0, π]
        | StandardQAOA       // Standard heuristic: γ ∈ [0, π/2], β ∈ [0, π/4]
        | TwoLocalPattern    // Pattern for 2-local Hamiltonians
        | PreviousOptimal    // Use previously optimized parameters
    
    /// Optimization strategy
    type OptimizationStrategy =
        | SingleRun          // Single optimization run
        | MultiStart of int  // Multiple random starts, keep best
        | LayerByLayer       // Optimize layer by layer (for p > 1)
        | Adaptive           // Adaptive strategy based on convergence
    
    /// Configuration for QAOA parameter optimization
    type QaoaOptimizationConfig = {
        /// Maximum number of iterations per optimization run
        MaxIterations: int
        
        /// Convergence tolerance for objective function
        Tolerance: float
        
        /// Initialization strategy
        InitStrategy: InitializationStrategy
        
        /// Optimization strategy
        OptStrategy: OptimizationStrategy
        
        /// Number of shots for expectation value estimation
        NumShots: int
        
        /// Random seed for reproducibility (None for random)
        RandomSeed: int option
        
        /// Parameter bounds: (γ_min, γ_max, β_min, β_max)
        ParameterBounds: float * float * float * float
        
        /// Progress reporter for long-running optimization
        ProgressReporter: Progress.IProgressReporter option
        
        /// Optional logger for structured logging (replaces console output)
        Logger: ILogger option
    }
    
    /// Result of QAOA parameter optimization
    type QaoaOptimizationResult = {
        /// Optimized parameters (γ, β) for each layer
        OptimizedParameters: (float * float)[]
        
        /// Final expectation value (energy)
        FinalEnergy: float
        
        /// Whether optimization converged
        Converged: bool
        
        /// Total number of circuit evaluations
        TotalEvaluations: int
        
        /// Optimization history (for analysis)
        History: (float[] * float) list
    }
    
    // ============================================================================
    // DEFAULT CONFIGURATION
    // ============================================================================
    
    /// Default QAOA optimization configuration
    let defaultConfig : QaoaOptimizationConfig = {
        MaxIterations = 200
        Tolerance = 1e-6
        InitStrategy = StandardQAOA
        OptStrategy = MultiStart 5
        NumShots = 1000
        RandomSeed = None
        ParameterBounds = (0.0, Math.PI, 0.0, Math.PI / 2.0)
        ProgressReporter = None
        Logger = None
    }
    
    // ============================================================================
    // PARAMETER INITIALIZATION
    // ============================================================================
    
    /// Initialize QAOA parameters based on strategy
    ///
    /// Parameters:
    /// - p: Number of QAOA layers
    /// - strategy: Initialization strategy
    /// - seed: Random seed (optional)
    ///
    /// Returns: Array of (γ, β) parameters for each layer
    let initializeParameters (p: int) (strategy: InitializationStrategy) (seed: int option) : (float * float)[] =
        let rng = 
            match seed with
            | Some s -> Random(s)
            | None -> Random()
        
        match strategy with
        | RandomUniform ->
            Array.init p (fun _ -> 
                let gamma = rng.NextDouble() * Math.PI
                let beta = rng.NextDouble() * Math.PI
                (gamma, beta)
            )
        
        | StandardQAOA ->
            // Standard heuristic from QAOA literature
            // γ ∈ [0, π/2], β ∈ [0, π/4]
            Array.init p (fun _ ->
                let gamma = rng.NextDouble() * (Math.PI / 2.0)
                let beta = rng.NextDouble() * (Math.PI / 4.0)
                (gamma, beta)
            )
        
        | TwoLocalPattern ->
            // Pattern optimized for 2-local Hamiltonians
            // Start with smaller angles and increase
            Array.init p (fun i ->
                let layer_factor = float (i + 1) / float p
                let gamma = 0.5 * layer_factor * Math.PI
                let beta = 0.3 * layer_factor * Math.PI / 2.0
                (gamma, beta)
            )
        
        | PreviousOptimal ->
            // Use standard as fallback (should be provided externally)
            Array.init p (fun _ -> (0.5, 0.3))
    
    // ============================================================================
    // OBJECTIVE FUNCTION CONSTRUCTION
    // ============================================================================
    
    /// Create objective function for QAOA parameter optimization
    ///
    /// The objective function:
    /// 1. Takes parameters (γ₁, β₁, γ₂, β₂, ..., γₚ, βₚ)
    /// 2. Builds QAOA circuit with these parameters
    /// 3. Executes on backend
    /// 4. Calculates expectation value of problem Hamiltonian
    ///
    /// Parameters:
    /// - problemHam: Problem Hamiltonian to optimize
    /// - mixerHam: Mixer Hamiltonian (usually X on all qubits)
    /// - backend: Quantum backend for circuit execution
    /// - numShots: Number of measurement shots
    ///
    /// Returns: Objective function (parameters → energy)
    let createObjectiveFunction 
        (problemHam: ProblemHamiltonian)
        (mixerHam: MixerHamiltonian)
        (backend: IQuantumBackend)
        (numShots: int)
        (logger: ILogger option) : (float[] -> float) =
        
        fun (parameters: float[]) ->
            // Convert flat array to (γ, β) pairs
            let p = parameters.Length / 2
            let paramPairs = 
                Array.init p (fun i ->
                    let gamma = parameters.[2 * i]
                    let beta = parameters.[2 * i + 1]
                    (gamma, beta)
                )
            
            // Build QAOA circuit
            let circuit = QaoaCircuit.build problemHam mixerHam paramPairs
            let circuitWrapper = QaoaCircuitWrapper(circuit) :> ICircuit
            
            // Execute on backend and measure state
            match backend.ExecuteToState circuitWrapper with
            | Error e -> 
                // On error, return large penalty
                logWarning logger $"Circuit execution failed: {e}"
                1e10
            | Ok state ->
                // Measure state to get bitstrings
                let measurements = QuantumState.measure state numShots
                
                // Calculate expectation value from measurements
                let expectationValue =
                    measurements
                    |> Array.map (fun bitstring ->
                        // Calculate energy for this bitstring
                        let energy =
                            problemHam.Terms
                            |> Array.sumBy (fun term ->
                                match term.QubitsIndices.Length with
                                | 1 -> 
                                    // Single-qubit term: c * Z_i
                                    let i = term.QubitsIndices.[0]
                                    let z_i = if bitstring.[i] = 0 then 1.0 else -1.0
                                    term.Coefficient * z_i
                                | 2 ->
                                    // Two-qubit term: c * Z_i * Z_j
                                    let i = term.QubitsIndices.[0]
                                    let j = term.QubitsIndices.[1]
                                    let z_i = if bitstring.[i] = 0 then 1.0 else -1.0
                                    let z_j = if bitstring.[j] = 0 then 1.0 else -1.0
                                    term.Coefficient * z_i * z_j
                                | _ -> 
                                    // Higher-order terms (shouldn't occur in QAOA)
                                    0.0
                            )
                        energy
                    )
                    |> Array.average
                
                expectationValue
    
    // ============================================================================
    // OPTIMIZATION STRATEGIES
    // ============================================================================
    
    /// Single-run optimization
    let private optimizeSingleRun
        (objectiveFunc: float[] -> float)
        (initialParams: (float * float)[])
        (bounds: float * float * float * float)
        (maxIter: int)
        (tol: float)
        (progressReporter: Progress.IProgressReporter option) : OptimizationResult * (float[] * float) list =
        
        // Flatten parameters
        let flatParams = 
            initialParams 
            |> Array.collect (fun (gamma, beta) -> [| gamma; beta |])
        
        // Create bounds arrays
        let (gamma_min, gamma_max, beta_min, beta_max) = bounds
        let lowerBounds = 
            Array.init (flatParams.Length) (fun i ->
                if i % 2 = 0 then gamma_min else beta_min
            )
        let upperBounds =
            Array.init (flatParams.Length) (fun i ->
                if i % 2 = 0 then gamma_max else beta_max
            )
        
        // Track history
        // Use ref cell for history tracking (more explicit than ResizeArray)
        let historyRef = ref []
        let iterationCount = ref 0
        let trackedObjective (parameters: float[]) =
            let value = objectiveFunc parameters
            historyRef.Value <- (Array.copy parameters, value) :: historyRef.Value
            
            // Report progress
            iterationCount.Value <- iterationCount.Value + 1
            progressReporter
            |> Option.iter (fun r -> 
                r.Report(Progress.IterationUpdate(iterationCount.Value, maxIter, Some value)))
            
            value
        
        // Run optimization
        let result = Optimizer.minimizeWithBounds trackedObjective flatParams lowerBounds upperBounds
        
        (result, List.rev historyRef.Value)
    
    /// Multi-start optimization
    let private optimizeMultiStart
        (objectiveFunc: float[] -> float)
        (p: int)
        (strategy: InitializationStrategy)
        (seed: int option)
        (bounds: float * float * float * float)
        (numStarts: int)
        (maxIter: int)
        (tol: float)
        (progressReporter: Progress.IProgressReporter option)
        (logger: ILogger option) : OptimizationResult * (float[] * float) list =
        
        logInfo logger $"Multi-start optimization: {numStarts} starts"
        
        progressReporter
        |> Option.iter (fun r -> 
            r.Report(Progress.PhaseChanged("QAOA Multi-Start", Some $"Running {numStarts} optimization starts...")))
        
        let results =
            [1 .. numStarts]
            |> List.map (fun i ->
                let seedForRun = seed |> Option.map (fun s -> s + i)
                let initialParams = initializeParameters p strategy seedForRun
                logInfo logger $"  Start {i}/{numStarts}: Initial energy = {objectiveFunc (initialParams |> Array.collect (fun (g, b) -> [| g; b |])):F4}|{objectiveFunc (initialParams |> Array.collect (fun (g, b) -> [| g; b |])):F4}"
                
                progressReporter
                |> Option.iter (fun r -> 
                    r.Report(Progress.PhaseChanged("QAOA Multi-Start", Some $"Start {i}/{numStarts}")))
                
                let (result, history) = optimizeSingleRun objectiveFunc initialParams bounds maxIter tol progressReporter
                logInfo logger $"  Start {i}/{numStarts}: Final energy = {result.FinalObjectiveValue:F4}, Converged = {result.Converged}"
                (result, history)
            )
        
        // Select best result
        let (bestResult, bestHistory) =
            results
            |> List.minBy (fun (res, _) -> res.FinalObjectiveValue)
        
        logInfo logger $"Best result: Energy = {bestResult.FinalObjectiveValue:F4}"
        (bestResult, bestHistory)
    
    /// Layer-by-layer optimization: Optimize one layer at a time
    let private optimizeLayerByLayer
        (objectiveFunc: float[] -> float)
        (p: int)
        (strategy: InitializationStrategy)
        (seed: int option)
        (bounds: float * float * float * float)
        (maxIter: int)
        (tol: float)
        (progressReporter: Progress.IProgressReporter option)
        (logger: ILogger option) : OptimizationResult * (float[] * float) list =
        
        logInfo logger $"Layer-by-layer optimization: {p} layers"
        
        // Use fold to accumulate optimized layers
        let (finalParams, allHistory) =
            [0 .. p - 1]
            |> List.fold (fun (accParams, accHistory) layerIdx ->
                logInfo logger $"  Optimizing layer {layerIdx + 1}/{p}..."
                
                // Initialize this layer
                let layerParams = initializeParameters 1 strategy (seed |> Option.map (fun s -> s + layerIdx + 1))
                let updatedParams = Array.copy accParams
                updatedParams.[layerIdx] <- layerParams.[0]
                
                // Create objective that only varies this layer
                let layerObjective (paramArray: float[]) =
                    match paramArray with
                    | [| gamma; beta |] ->
                        let paramsWithLayer = Array.copy updatedParams
                        paramsWithLayer.[layerIdx] <- (gamma, beta)
                        let flatParams = paramsWithLayer |> Array.collect (fun (g, b) -> [| g; b |])
                        objectiveFunc flatParams
                    | _ -> failwith "Layer objective expects exactly 2 parameters (gamma, beta)"
                
                // Report progress
                progressReporter
                |> Option.iter (fun r -> 
                    r.Report(Progress.PhaseChanged("QAOA Layer-by-Layer", Some $"Optimizing layer {layerIdx + 1}/{p}...")))
                
                // Optimize this layer
                let initialLayerParams = [| updatedParams.[layerIdx] |]
                let (result, history) = optimizeSingleRun layerObjective initialLayerParams bounds (maxIter / p) tol progressReporter
                
                // Update parameters with optimized layer
                match result.OptimizedParameters with
                | [| gamma; beta |] ->
                    updatedParams.[layerIdx] <- (gamma, beta)
                    let energy = result.FinalObjectiveValue
                    logInfo logger $"    Layer {layerIdx + 1} optimized: gamma = {gamma:F4}, beta = {beta:F4}, Energy = {energy:F4}"
                    (updatedParams, accHistory @ history)
                | _ -> failwith "Optimization result should have exactly 2 parameters"
            ) (Array.init p (fun _ -> (0.0, 0.0)), [])
        
        // Create final result
        let flatFinalParams = finalParams |> Array.collect (fun (g, b) -> [| g; b |])
        let finalEnergy = objectiveFunc flatFinalParams
        
        let finalResult = {
            OptimizedParameters = flatFinalParams
            FinalObjectiveValue = finalEnergy
            Converged = true
            Iterations = List.length allHistory
        }
        
        logInfo logger $"Layer-by-layer complete: Final energy = {finalEnergy:F4}"
        (finalResult, allHistory)
    
    /// Adaptive optimization: Adjust strategy based on convergence
    let private optimizeAdaptive
        (objectiveFunc: float[] -> float)
        (p: int)
        (strategy: InitializationStrategy)
        (seed: int option)
        (bounds: float * float * float * float)
        (maxIter: int)
        (tol: float)
        (progressReporter: Progress.IProgressReporter option)
        (logger: ILogger option) : OptimizationResult * (float[] * float) list =
        
        logInfo logger "Adaptive optimization: adjusting based on convergence"
        
        progressReporter
        |> Option.iter (fun r -> 
            r.Report(Progress.PhaseChanged("QAOA Adaptive", Some "Starting initial optimization run...")))
        
        // Start with single run
        let initialParams = initializeParameters p strategy seed
        let (result1, history1) = optimizeSingleRun objectiveFunc initialParams bounds (maxIter / 2) tol progressReporter
        
        logInfo logger $"  Initial run: Energy = {result1.FinalObjectiveValue:F4}, Converged = {result1.Converged}"
        
        if result1.Converged then
            // Converged quickly, return result
            logInfo logger "  Converged on first attempt"
            (result1, history1)
        else
            // Not converged, try multi-start with 3 starts
            logInfo logger "  Not converged, trying multi-start..."
            progressReporter
            |> Option.iter (fun r -> 
                r.Report(Progress.PhaseChanged("QAOA Adaptive", Some "Switching to multi-start strategy...")))
            
            let (result2, history2) = optimizeMultiStart objectiveFunc p strategy seed bounds 3 (maxIter / 2) tol progressReporter logger
            
            // Combine histories
            let combinedHistory = history1 @ history2
            (result2, combinedHistory)
    
    // ============================================================================
    // MAIN OPTIMIZATION INTERFACE
    // ============================================================================
    
    /// Optimize QAOA parameters for a given problem
    ///
    /// Parameters:
    /// - problemHam: Problem Hamiltonian to optimize
    /// - p: Number of QAOA layers
    /// - backend: Quantum backend for circuit execution
    /// - config: Optimization configuration
    ///
    /// Returns: QaoaOptimizationResult with optimized parameters
    ///
    /// Example:
    ///   let result = optimizeQaoaParameters problemHam 2 backend defaultConfig
    ///   let optimalParams = result.OptimizedParameters
    let optimizeQaoaParameters
        (problemHam: ProblemHamiltonian)
        (p: int)
        (backend: IQuantumBackend)
        (config: QaoaOptimizationConfig) : QaoaOptimizationResult =
        
        logInfo config.Logger "=== QAOA Parameter Optimization ==="
        logInfo config.Logger $"Problem: {problemHam.NumQubits} qubits, {problemHam.Terms.Length} terms"
        logInfo config.Logger $"Layers: p={p}"
        logInfo config.Logger $"Strategy: {config.OptStrategy}"
        
        // Report optimization start
        config.ProgressReporter
        |> Option.iter (fun r -> 
            r.Report(Progress.PhaseChanged("QAOA Optimization", Some $"Starting {config.OptStrategy} with p={p} layers...")))
        
        // Create mixer Hamiltonian
        let mixerHam = MixerHamiltonian.create problemHam.NumQubits
        
        // Create objective function
        let objectiveFunc = createObjectiveFunction problemHam mixerHam backend config.NumShots config.Logger
        
        // Run optimization based on strategy
        let (result, history) =
            match config.OptStrategy with
            | SingleRun ->
                let initialParams = initializeParameters p config.InitStrategy config.RandomSeed
                optimizeSingleRun objectiveFunc initialParams config.ParameterBounds config.MaxIterations config.Tolerance config.ProgressReporter
            
            | MultiStart numStarts ->
                optimizeMultiStart objectiveFunc p config.InitStrategy config.RandomSeed config.ParameterBounds numStarts config.MaxIterations config.Tolerance config.ProgressReporter config.Logger
            
            | LayerByLayer ->
                optimizeLayerByLayer objectiveFunc p config.InitStrategy config.RandomSeed config.ParameterBounds config.MaxIterations config.Tolerance config.ProgressReporter config.Logger
            
            | Adaptive ->
                optimizeAdaptive objectiveFunc p config.InitStrategy config.RandomSeed config.ParameterBounds config.MaxIterations config.Tolerance config.ProgressReporter config.Logger
        
        // Convert flat parameters back to (γ, β) pairs
        let optimizedParams =
            Array.init p (fun i ->
                let gamma = result.OptimizedParameters.[2 * i]
                let beta = result.OptimizedParameters.[2 * i + 1]
                (gamma, beta)
            )
        
        logInfo config.Logger "Optimization Complete!"
        logInfo config.Logger $"Final Energy: {result.FinalObjectiveValue:F6}"
        logInfo config.Logger $"Converged: {result.Converged}"
        logInfo config.Logger $"Iterations: {result.Iterations}"
        logInfo config.Logger "Optimized Parameters:"
        for i in 0 .. p - 1 do
            let (gamma, beta) = optimizedParams.[i]
            logInfo config.Logger $"  Layer {i + 1}: gamma = {gamma:F4}, beta = {beta:F4}"
        
        // Report completion
        let statusMsg = if result.Converged then "converged" else "max iterations"
        config.ProgressReporter
        |> Option.iter (fun r -> 
            r.Report(Progress.PhaseChanged("QAOA Complete", Some $"Optimized to energy {result.FinalObjectiveValue:F6} ({statusMsg})")))
        
        {
            OptimizedParameters = optimizedParams
            FinalEnergy = result.FinalObjectiveValue
            Converged = result.Converged
            TotalEvaluations = history.Length
            History = history
        }
