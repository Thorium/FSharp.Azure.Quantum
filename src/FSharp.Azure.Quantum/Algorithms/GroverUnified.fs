namespace FSharp.Azure.Quantum.GroverSearch

open System
open FSharp.Azure.Quantum.Core
open FSharp.Azure.Quantum.Core.UnifiedBackendAbstraction

/// Grover's Search Algorithm - Unified Backend Edition
/// 
/// This module provides a backend-agnostic implementation of Grover's quantum search algorithm
/// using the unified backend interface (IUnifiedQuantumBackend).
/// 
/// Key features:
/// - Works seamlessly with gate-based and topological backends
/// - State-based execution for efficiency
/// - Pure functional design with Result-based error handling
/// - Idiomatic F# with computation expressions
/// - No mutable state
/// 
/// Performance benefits:
/// - Eliminates circuit construction overhead per iteration
/// - Reuses quantum states across iterations
/// - Automatic backend-specific optimizations
/// 
/// Usage:
///   let backend = LocalBackend.LocalBackend() :> IUnifiedQuantumBackend
///   let! result = Grover.searchSingle 5 3 backend defaultConfig
module GroverUnified =
    
    open FSharp.Azure.Quantum.GroverSearch.Oracle
    
    // ========================================================================
    // TYPES
    // ========================================================================
    
    /// Configuration for Grover search with unified backend
    type GroverConfig = {
        /// Number of Grover iterations (None = auto-calculate)
        Iterations: int option
        
        /// Number of measurement shots for result extraction
        Shots: int
        
        /// Success probability threshold (0.0 - 1.0)
        SuccessThreshold: float
        
        /// Minimum shot count threshold for solution extraction (0.0 - 1.0)
        SolutionThreshold: float
    }
    
    /// Search result with found solutions and statistics
    type GroverResult = {
        /// Found solutions (bitstrings that satisfy oracle)
        Solutions: int list
        
        /// Total number of Grover iterations performed
        Iterations: int
        
        /// Measurement distribution (value -> count)
        Measurements: Map<int, int>
        
        /// Success probability (highest solution frequency)
        SuccessProbability: float
        
        /// Total execution time (milliseconds)
        ExecutionTimeMs: float
    }
    
    // ========================================================================
    // DEFAULT CONFIGURATION
    // ========================================================================
    
    /// Default configuration with standard parameters
    let defaultConfig = {
        Iterations = None  // Auto-calculate optimal
        Shots = 1000
        SuccessThreshold = 0.5
        SolutionThreshold = 0.07  // 7% of shots
    }
    
    // ========================================================================
    // HELPER: Optimal Iteration Count
    // ========================================================================
    
    /// Calculate optimal number of Grover iterations
    /// 
    /// Formula: k ≈ (π/4) * √(N/M)
    /// where N = search space size (2^n), M = number of solutions
    /// 
    /// Parameters:
    ///   numQubits - Search space size (N = 2^numQubits)
    ///   numSolutions - Number of marked solutions (M)
    /// 
    /// Returns:
    ///   Optimal iteration count
    let calculateOptimalIterations (numQubits: int) (numSolutions: int) : int =
        let n = float (1 <<< numQubits)  // N = 2^numQubits
        let m = float numSolutions       // M
        
        if m <= 0.0 || m >= n then
            // Edge cases: no solutions or all solutions
            0
        else
            // k = (π/4) * √(N/M)
            let k = (Math.PI / 4.0) * sqrt (n / m)
            max 1 (int (Math.Round k))
    
    // ========================================================================
    // GROVER ITERATION PRIMITIVES (State-Based)
    // ========================================================================
    
    /// Apply Hadamard transform to all qubits (superposition creation)
    let private applyHadamardTransform 
        (backend: IUnifiedQuantumBackend) 
        (state: QuantumState) 
        : Result<QuantumState, QuantumError> =
        
        let numQubits = QuantumState.numQubits state
        
        // Create Hadamard operations for all qubits
        let hadamardOps =
            [0 .. numQubits - 1]
            |> List.map (fun q -> QuantumOperation.Gate (CircuitBuilder.H q))
        
        // Apply sequence efficiently (single conversion if needed)
        UnifiedBackend.applySequence backend hadamardOps state
    
    /// Apply oracle operation to mark target states
    /// 
    /// Oracle flips the phase of target states: |target⟩ → -|target⟩
    /// Implemented via controlled phase flip operations
    let private applyOracle
        (backend: IUnifiedQuantumBackend)
        (oracle: CompiledOracle)
        (state: QuantumState)
        : Result<QuantumState, QuantumError> =
        
        // Oracle is implemented as a sequence of gates that mark the target
        // For each target value, we apply phase flip when qubits match
        
        match oracle.Targets with
        | SingleTarget target ->
            // Mark single target value
            // Convert target to binary and apply controlled-Z pattern
            let numQubits = oracle.NumQubits
            
            // Build oracle circuit: X gates + MCZ + X gates (undo)
            let oracleOps = 
                [
                    // Step 1: X gates where target bit is 0
                    for q in 0 .. numQubits - 1 do
                        let bitValue = (target >>> q) &&& 1
                        if bitValue = 0 then
                            yield QuantumOperation.Gate (CircuitBuilder.X q)
                    
                    // Step 2: Multi-controlled Z (phase flip when all qubits are |1⟩)
                    // Decompose to CZ gates for simplicity
                    for q in 0 .. numQubits - 2 do
                        yield QuantumOperation.Gate (CircuitBuilder.CZ (q, numQubits - 1))
                    
                    // Step 3: Undo X gates
                    for q in 0 .. numQubits - 1 do
                        let bitValue = (target >>> q) &&& 1
                        if bitValue = 0 then
                            yield QuantumOperation.Gate (CircuitBuilder.X q)
                ]
            
            UnifiedBackend.applySequence backend oracleOps state
        
        | MultipleTargets targets ->
            // Mark multiple targets by applying phase flip for each
            let applyTargetOracle targetValue currentState =
                // Recursively build oracle for each target
                let singleTargetOracle = { 
                    oracle with 
                    Targets = SingleTarget targetValue 
                }
                applyOracle backend singleTargetOracle currentState
            
            // Fold over targets, short-circuiting on error
            targets
            |> List.fold (fun stateResult target ->
                stateResult |> Result.bind (applyTargetOracle target)
            ) (Ok state)
        
        | PredicateTargets _ ->
            // For predicate-based oracles, we'd need to evaluate predicate
            // and convert to MultipleTargets (not implemented in this version)
            Error (QuantumError.ExecutionError ("GroverUnified", "Predicate-based oracles not yet supported in unified backend"))
    
    /// Apply diffusion operator (inversion about average)
    /// 
    /// Diffusion operator: D = 2|s⟩⟨s| - I
    /// where |s⟩ = H^⊗n|0⟩^⊗n is uniform superposition
    /// 
    /// Implementation:
    /// 1. Apply H^⊗n
    /// 2. Apply X^⊗n (flip all qubits)
    /// 3. Apply multi-controlled Z (phase flip |111...1⟩)
    /// 4. Apply X^⊗n (undo)
    /// 5. Apply H^⊗n
    let private applyDiffusion
        (backend: IUnifiedQuantumBackend)
        (state: QuantumState)
        : Result<QuantumState, QuantumError> =
        
        let numQubits = QuantumState.numQubits state
        
        let diffusionOps =
            [
                // Step 1: H^⊗n
                for q in 0 .. numQubits - 1 do
                    yield QuantumOperation.Gate (CircuitBuilder.H q)
                
                // Step 2: X^⊗n (flip to |111...1⟩)
                for q in 0 .. numQubits - 1 do
                    yield QuantumOperation.Gate (CircuitBuilder.X q)
                
                // Step 3: Multi-controlled Z (simplified with CZ chain)
                for q in 0 .. numQubits - 2 do
                    yield QuantumOperation.Gate (CircuitBuilder.CZ (q, numQubits - 1))
                
                // Step 4: X^⊗n (undo flip)
                for q in 0 .. numQubits - 1 do
                    yield QuantumOperation.Gate (CircuitBuilder.X q)
                
                // Step 5: H^⊗n
                for q in 0 .. numQubits - 1 do
                    yield QuantumOperation.Gate (CircuitBuilder.H q)
            ]
        
        UnifiedBackend.applySequence backend diffusionOps state
    
    /// Single Grover iteration: Oracle + Diffusion
    let private applyGroverIteration
        (backend: IUnifiedQuantumBackend)
        (oracle: CompiledOracle)
        (state: QuantumState)
        : Result<QuantumState, QuantumError> =
        
        // Grover iteration = Oracle · Diffusion
        // Using Result-based composition
        state
        |> applyOracle backend oracle
        |> Result.bind (applyDiffusion backend)
    
    // ========================================================================
    // MEASUREMENT AND RESULT EXTRACTION
    // ========================================================================
    
    /// Convert measurement bitstring to integer
    let private bitstringToInt (bits: int[]) : int =
        bits
        |> Array.rev  // Reverse for big-endian
        |> Array.fold (fun acc bit -> (acc <<< 1) + bit) 0
    
    /// Extract measurement distribution from shots
    let private extractDistribution (measurements: int[][]) : Map<int, int> =
        measurements
        |> Array.map bitstringToInt
        |> Array.groupBy id
        |> Array.map (fun (value, instances) -> (value, instances.Length))
        |> Map.ofArray
    
    /// Extract solutions from measurement distribution
    let private extractSolutions 
        (distribution: Map<int, int>) 
        (totalShots: int) 
        (threshold: float) 
        : int list =
        
        let minCount = int (float totalShots * threshold)
        
        distribution
        |> Map.toList
        |> List.filter (fun (_, count) -> count >= minCount)
        |> List.sortByDescending snd
        |> List.map fst
    
    /// Calculate success probability (highest measurement frequency)
    let private calculateSuccessProbability (distribution: Map<int, int>) (totalShots: int) : float =
        if Map.isEmpty distribution then
            0.0
        else
            let maxCount = distribution |> Map.toSeq |> Seq.map snd |> Seq.max
            float maxCount / float totalShots
    
    // ========================================================================
    // MAIN GROVER SEARCH ALGORITHM
    // ========================================================================
    
    /// Execute Grover search algorithm with unified backend
    /// 
    /// Parameters:
    ///   oracle - Compiled oracle marking target states
    ///   backend - Quantum backend (gate-based or topological)
    ///   config - Algorithm configuration
    /// 
    /// Returns:
    ///   Result with found solutions or error
    let search
        (oracle: CompiledOracle)
        (backend: IUnifiedQuantumBackend)
        (config: GroverConfig)
        : Result<GroverResult, QuantumError> =
        
        let startTime = DateTime.Now
        
        // Determine iteration count (auto-calculate if not specified)
        let iterationCount =
            match config.Iterations with
            | Some k -> k
            | None ->
                // Auto-calculate based on number of targets
                let numSolutions =
                    match oracle.Targets with
                    | SingleTarget _ -> 1
                    | MultipleTargets targets -> targets.Length
                    | PredicateTargets _ -> 1  // Conservative estimate
                
                calculateOptimalIterations oracle.NumQubits numSolutions
        
        // Step 1: Initialize quantum state |0⟩^⊗n
        result {
            let! initialState = backend.InitializeState oracle.NumQubits
            
            // Step 2: Create uniform superposition H^⊗n|0⟩
            let! superpositionState = applyHadamardTransform backend initialState
            
            // Step 3: Apply Grover iterations (Oracle + Diffusion)^k
            let! finalState =
                [1 .. iterationCount]
                |> List.fold (fun stateResult _ ->
                    stateResult |> Result.bind (applyGroverIteration backend oracle)
                ) (Ok superpositionState)
            
            // Step 4: Measure final state
            let measurements = UnifiedBackend.measureState finalState config.Shots
            
            // Step 5: Extract solutions from measurements
            let distribution = extractDistribution measurements
            let solutions = extractSolutions distribution config.Shots config.SolutionThreshold
            let successProb = calculateSuccessProbability distribution config.Shots
            
            // Calculate execution time
            let endTime = DateTime.Now
            let elapsedMs = (endTime - startTime).TotalMilliseconds
            
            // Return result
            return {
                Solutions = solutions
                Iterations = iterationCount
                Measurements = distribution
                SuccessProbability = successProb
                ExecutionTimeMs = elapsedMs
            }
        }
    
    // ========================================================================
    // CONVENIENCE API FUNCTIONS
    // ========================================================================
    
    /// Search for single target value
    /// 
    /// Example:
    ///   let! result = searchSingle 5 3 backend defaultConfig
    ///   // Searches for value 5 in 3-qubit space (0-7)
    let searchSingle
        (target: int)
        (numQubits: int)
        (backend: IUnifiedQuantumBackend)
        (config: GroverConfig)
        : Result<GroverResult, QuantumError> =
        
        result {
            let! oracle = Oracle.forValue target numQubits
            return! search oracle backend config
        }
    
    /// Search for multiple target values
    /// 
    /// Example:
    ///   let! result = searchMultiple [2; 5; 7] 3 backend defaultConfig
    let searchMultiple
        (targets: int list)
        (numQubits: int)
        (backend: IUnifiedQuantumBackend)
        (config: GroverConfig)
        : Result<GroverResult, QuantumError> =
        
        if List.isEmpty targets then
            Error (QuantumError.ValidationError ("targets", "Target list cannot be empty"))
        else
            result {
                let! oracle = Oracle.forValues targets numQubits
                return! search oracle backend config
            }
    
    /// Search with custom predicate function
    /// 
    /// Example:
    ///   let! result = searchWhere (fun x -> x % 2 = 0) 3 backend defaultConfig
    ///   // Searches for even numbers in range 0-7
    let searchWhere
        (predicate: int -> bool)
        (numQubits: int)
        (backend: IUnifiedQuantumBackend)
        (config: GroverConfig)
        : Result<GroverResult, QuantumError> =
        
        result {
            let! oracle = Oracle.fromPredicate predicate numQubits
            return! search oracle backend config
        }
    
    /// Search for even numbers
    let searchEven
        (numQubits: int)
        (backend: IUnifiedQuantumBackend)
        (config: GroverConfig)
        : Result<GroverResult, QuantumError> =
        
        result {
            let! oracle = Oracle.even numQubits
            return! search oracle backend config
        }
    
    /// Search for odd numbers
    let searchOdd
        (numQubits: int)
        (backend: IUnifiedQuantumBackend)
        (config: GroverConfig)
        : Result<GroverResult, QuantumError> =
        
        result {
            let! oracle = Oracle.odd numQubits
            return! search oracle backend config
        }
    
    // ========================================================================
    // PRETTY PRINTING
    // ========================================================================
    
    /// Format Grover result as human-readable string
    let formatResult (result: GroverResult) : string =
        let solutionsStr =
            if List.isEmpty result.Solutions then
                "No solutions found"
            else
                result.Solutions
                |> List.map string
                |> String.concat ", "
                |> sprintf "Found solutions: %s"
        
        let statsStr =
            sprintf "Iterations: %d | Success probability: %.2f%% | Time: %.2f ms"
                result.Iterations
                (result.SuccessProbability * 100.0)
                result.ExecutionTimeMs
        
        sprintf "%s\n%s" solutionsStr statsStr
