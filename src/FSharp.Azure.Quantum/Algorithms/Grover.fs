namespace FSharp.Azure.Quantum.GroverSearch

open System
open System.Diagnostics
open FSharp.Azure.Quantum
open FSharp.Azure.Quantum.Core
open FSharp.Azure.Quantum.Core.BackendAbstraction

/// Grover's Search Algorithm - Unified Backend Edition
/// 
/// This module provides a backend-agnostic implementation of Grover's quantum search algorithm
/// using the unified backend interface (IQuantumBackend).
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
///   let backend = LocalBackend.LocalBackend() :> IQuantumBackend
///   let! result = Grover.searchSingle 5 3 backend defaultConfig
module Grover =
    
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
        
        /// Random seed for reproducible results (None = non-deterministic)
        RandomSeed: int option
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
        RandomSeed = None  // Non-deterministic by default
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
    
    /// Decompose MCZ gate into basic gates for topological backend compatibility
    /// 
    /// MCZ gates need decomposition before braiding compilation.
    /// Uses GateTranspiler for standard Toffoli decomposition.
    /// Recursively transpiles until only basic gates remain (no MCZ, CCX).
    let private decomposeMCZGate (controls: int list) (target: int) (numQubits: int) : CircuitBuilder.Gate list =
        // Use GateTranspiler to get standard decomposition
        // IMPORTANT: Use numQubits from the actual state, not just max(target,controls)+1
        // This ensures decomposed gates don't reference non-existent auxiliary qubits
        let circuit = CircuitBuilder.empty numQubits
        let mczGate = CircuitBuilder.MCZ (controls, target)
        let circuitWithMCZ = CircuitBuilder.addGate mczGate circuit
        
        // Transpile MCZ to basic gates - may produce CCX gates
        let transpiled1 = GateTranspiler.transpileForBackend "topological" circuitWithMCZ
        
        // Transpile again to decompose any CCX gates produced by first transpilation
        // (MCZ decomposes to CCX, which then decomposes to CNOTs + T gates)
        let transpiled2 = GateTranspiler.transpileForBackend "topological" transpiled1
        
        transpiled2.Gates
    
    /// Apply Hadamard transform to all qubits (superposition creation)
    let private applyHadamardTransform 
        (backend: IQuantumBackend) 
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
    let rec private applyOracle
        (backend: IQuantumBackend)
        (oracle: Oracle.CompiledOracle)
        (state: QuantumState)
        : Result<QuantumState, QuantumError> =
        
        // Oracle is implemented as a sequence of gates that mark the target
        // For each target value, we apply phase flip when qubits match
        
        match oracle.Spec with
        | Oracle.OracleSpec.SingleTarget target ->
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
                    // Decompose MCZ to basic gates for topological backend compatibility
                    let controls = [0 .. numQubits - 2]
                    let targetQubit = numQubits - 1
                    let decomposedMCZ = decomposeMCZGate controls targetQubit numQubits
                    
                    yield! decomposedMCZ |> List.map QuantumOperation.Gate
                    
                    // Step 3: Undo X gates
                    for q in 0 .. numQubits - 1 do
                        let bitValue = (target >>> q) &&& 1
                        if bitValue = 0 then
                            yield QuantumOperation.Gate (CircuitBuilder.X q)
                ]
            
            UnifiedBackend.applySequence backend oracleOps state
        
        | Oracle.OracleSpec.Solutions solutionList ->
            // Mark multiple solution indices by applying phase flip for each
            let applyTargetOracle targetValue currentState =
                // Recursively build oracle for each target
                let singleTargetOracle = { oracle with Spec = Oracle.OracleSpec.SingleTarget targetValue }
                applyOracle backend singleTargetOracle currentState
            
            // Fold over targets, short-circuiting on error
            solutionList
            |> List.fold (fun stateResult target ->
                stateResult |> Result.bind (applyTargetOracle target)
            ) (Ok state)
        
        | Oracle.OracleSpec.Predicate pred ->
            // Enumerate solutions classically (same strategy as old GroverSearch)
            // Pragmatic limit: Up to 42 qubits (as in BackendAdapter.fs)
            let numQubits = oracle.NumQubits
            let searchSpaceSize = 1 <<< numQubits
            
            // Check pragmatic limit (prevents excessive enumeration)
            if numQubits > 42 then
                Error (QuantumError.ValidationError ("NumQubits", 
                    $"predicate oracle with {numQubits} qubits requires enumerating {searchSpaceSize} states classically (prohibitive). " +
                    "For large search spaces, use explicit Solutions oracle instead. Pragmatic limit: ≤42 qubits."))
            else
                // Enumerate all values and filter by predicate
                let solutions = 
                    [0 .. searchSpaceSize - 1]
                    |> List.filter pred
                
                if List.isEmpty solutions then
                    Error (QuantumError.ValidationError ("Predicate", "matches no solutions"))
                else
                    // Convert to Solutions oracle and apply recursively
                    let solutionsOracle = { oracle with Spec = Oracle.OracleSpec.Solutions solutions }
                    applyOracle backend solutionsOracle state
        
        | Oracle.OracleSpec.And (spec1, spec2) ->
            // Apply both oracles sequentially (marks states satisfying both conditions)
            let oracle1 = { oracle with Spec = spec1 }
            let oracle2 = { oracle with Spec = spec2 }
            
            state
            |> applyOracle backend oracle1
            |> Result.bind (applyOracle backend oracle2)
        
        | Oracle.OracleSpec.Or (spec1, spec2) ->
            // Enumerate solutions for both specs and combine
            let numQubits = oracle.NumQubits
            let searchSpaceSize = 1 <<< numQubits
            
            if numQubits > 42 then
                Error (QuantumError.ValidationError ("NumQubits",
                    $"OR oracle with {numQubits} qubits requires enumerating {searchSpaceSize} states classically (prohibitive). " +
                    "Pragmatic limit: ≤42 qubits."))
            else
                let solutions =
                    [0 .. searchSpaceSize - 1]
                    |> List.filter (fun i -> 
                        Oracle.isSolution spec1 i || Oracle.isSolution spec2 i
                    )
                
                if List.isEmpty solutions then
                    Error (QuantumError.ValidationError ("OR oracle", "has no solutions"))
                else
                    let solutionsOracle = { oracle with Spec = Oracle.OracleSpec.Solutions solutions }
                    applyOracle backend solutionsOracle state
        
        | Oracle.OracleSpec.Not spec ->
            // Negate oracle: mark all states NOT marked by inner oracle
            let numQubits = oracle.NumQubits
            let searchSpaceSize = 1 <<< numQubits
            
            if numQubits > 42 then
                Error (QuantumError.ValidationError ("NumQubits",
                    $"NOT oracle with {numQubits} qubits requires enumerating {searchSpaceSize} states classically (prohibitive). " +
                    "Pragmatic limit: ≤42 qubits."))
            else
                let solutions =
                    [0 .. searchSpaceSize - 1]
                    |> List.filter (fun i -> not (Oracle.isSolution spec i))
                
                if List.isEmpty solutions then
                    Error (QuantumError.ValidationError ("NOT oracle", "has no solutions"))
                else
                    let solutionsOracle = { oracle with Spec = Oracle.OracleSpec.Solutions solutions }
                    applyOracle backend solutionsOracle state
    
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
        (backend: IQuantumBackend)
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
                
                // Step 3: Multi-controlled Z (phase flip when all qubits are |1⟩)
                // Decompose MCZ to basic gates for topological backend compatibility
                let controls = [0 .. numQubits - 2]
                let targetQubit = numQubits - 1
                let decomposedMCZ = decomposeMCZGate controls targetQubit numQubits
                
                yield! decomposedMCZ |> List.map QuantumOperation.Gate
                
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
        (backend: IQuantumBackend)
        (oracle: Oracle.CompiledOracle)
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
    // MAIN GROVER SEARCH ALGORITHM (ADR: intent → plan → executePlan)
    // ========================================================================

    /// Execution exactness contract for a planned Grover run.
    type Exactness =
        | Exact
        | Approximate of epsilon: float

    /// Canonical, algorithm-level intent for Grover search.
    type GroverSearchIntent = {
        Oracle: Oracle.CompiledOracle
        Iterations: int
        Exactness: Exactness
    }

    type GroverAlgorithmOps = {
        Prepare: QuantumOperation
        Oracle: QuantumOperation
        Diffusion: QuantumOperation
    }

    /// Planned strategy for executing Grover on a specific backend.
    type GroverPlan =
        /// Execute Grover using semantic algorithm intents, if supported by the backend.
        | ExecuteNatively of ops: GroverAlgorithmOps * iterations: int * exactness: Exactness

        /// Execute Grover by explicitly lowering to a supported `QuantumOperation` set.
        ///
        /// This path is explicit and does not silently fall back: if required operations are not
        /// supported by the backend, planning fails.
        | ExecuteViaOps of prepareOps: QuantumOperation list * iterationOps: QuantumOperation list * iterations: int * exactness: Exactness

    let private createAlgorithmOps (oracle: Oracle.CompiledOracle) : GroverAlgorithmOps =
        {
            Prepare = QuantumOperation.Algorithm (AlgorithmOperation.GroverPrepare oracle.NumQubits)
            Oracle =
                QuantumOperation.Algorithm (
                    AlgorithmOperation.GroverOraclePhaseFlip {
                        NumQubits = oracle.NumQubits
                        IsMarked = Oracle.isSolution oracle.Spec
                    })
            Diffusion = QuantumOperation.Algorithm (AlgorithmOperation.GroverDiffusion oracle.NumQubits)
        }

    let private lowerPrepareOps (numQubits: int) : QuantumOperation list =
        [ 0 .. numQubits - 1 ]
        |> List.map (fun q -> QuantumOperation.Gate (CircuitBuilder.H q))

    let private lowerSingleTargetOracleOps (target: int) (numQubits: int) : QuantumOperation list =
        [
            // Step 1: X gates where target bit is 0
            for q in 0 .. numQubits - 1 do
                let bitValue = (target >>> q) &&& 1
                if bitValue = 0 then
                    yield QuantumOperation.Gate (CircuitBuilder.X q)

            // Step 2: Multi-controlled Z (phase flip when all qubits are |1⟩)
            let controls = [ 0 .. numQubits - 2 ]
            let targetQubit = numQubits - 1
            let decomposedMCZ = decomposeMCZGate controls targetQubit numQubits
            yield! decomposedMCZ |> List.map QuantumOperation.Gate

            // Step 3: Undo X gates
            for q in 0 .. numQubits - 1 do
                let bitValue = (target >>> q) &&& 1
                if bitValue = 0 then
                    yield QuantumOperation.Gate (CircuitBuilder.X q)
        ]

    let private enumerateSolutions (oracle: Oracle.CompiledOracle) : Result<int list, QuantumError> =
        let numQubits = oracle.NumQubits
        let searchSpaceSize = 1 <<< numQubits

        let solutions =
            [ 0 .. searchSpaceSize - 1 ]
            |> List.filter (Oracle.isSolution oracle.Spec)

        if List.isEmpty solutions then
            Error (QuantumError.ValidationError ("Oracle", "matches no solutions"))
        else
            Ok solutions

    let private lowerOracleOps (oracle: Oracle.CompiledOracle) : Result<QuantumOperation list, QuantumError> =
        result {
            let! solutions =
                match oracle.Spec with
                | Oracle.OracleSpec.SingleTarget t -> Ok [ t ]
                | Oracle.OracleSpec.Solutions targets ->
                    if List.isEmpty targets then
                        Error (QuantumError.ValidationError ("Solutions", "list cannot be empty"))
                    else
                        Ok targets
                | _ -> enumerateSolutions oracle

            // Lower to an explicit sequence that phase-flips each marked basis index.
            return solutions |> List.collect (fun t -> lowerSingleTargetOracleOps t oracle.NumQubits)
        }

    let private lowerDiffusionOps (numQubits: int) : QuantumOperation list =
        [
            // Step 1: H^⊗n
            for q in 0 .. numQubits - 1 do
                yield QuantumOperation.Gate (CircuitBuilder.H q)

            // Step 2: X^⊗n (flip to |111...1⟩)
            for q in 0 .. numQubits - 1 do
                yield QuantumOperation.Gate (CircuitBuilder.X q)

            // Step 3: Multi-controlled Z (phase flip when all qubits are |1⟩)
            let controls = [ 0 .. numQubits - 2 ]
            let targetQubit = numQubits - 1
            let decomposedMCZ = decomposeMCZGate controls targetQubit numQubits
            yield! decomposedMCZ |> List.map QuantumOperation.Gate

            // Step 4: X^⊗n (undo flip)
            for q in 0 .. numQubits - 1 do
                yield QuantumOperation.Gate (CircuitBuilder.X q)

            // Step 5: H^⊗n
            for q in 0 .. numQubits - 1 do
                yield QuantumOperation.Gate (CircuitBuilder.H q)
        ]

    /// Plan Grover execution on the given backend.
    let plan
        (backend: IQuantumBackend)
        (intent: GroverSearchIntent)
        : Result<GroverPlan, QuantumError> =

        // Prefer semantic algorithm intents when available; otherwise lower explicitly.
        match backend.NativeStateType with
        | QuantumStateType.Annealing ->
            Error (QuantumError.OperationError ("Grover", $"Backend '{backend.Name}' does not support Grover search (native state type: {backend.NativeStateType})"))
        | _ ->
            let ops = createAlgorithmOps intent.Oracle

            if backend.SupportsOperation ops.Prepare
               && backend.SupportsOperation ops.Oracle
               && backend.SupportsOperation ops.Diffusion then
                Ok (ExecuteNatively (ops, intent.Iterations, intent.Exactness))
            else
                result {
                    let prepareOps = lowerPrepareOps intent.Oracle.NumQubits
                    let! oracleOps = lowerOracleOps intent.Oracle
                    let diffusionOps = lowerDiffusionOps intent.Oracle.NumQubits

                    let iterationOps = oracleOps @ diffusionOps

                    let required = prepareOps @ iterationOps

                    if required |> List.forall backend.SupportsOperation then
                        return ExecuteViaOps (prepareOps, iterationOps, intent.Iterations, intent.Exactness)
                    else
                        return!
                            Error (QuantumError.OperationError (
                                "Grover",
                                $"Backend '{backend.Name}' does not support required operations for Grover search"))
                }

    let private repeatM (count: int) (step: 'state -> Result<'state, QuantumError>) (initial: 'state) =
        let rec loop remaining state =
            if remaining <= 0 then
                Ok state
            else
                state |> step |> Result.bind (loop (remaining - 1))

        loop count initial

    let private executePlan
        (backend: IQuantumBackend)
        (oracle: Oracle.CompiledOracle)
        (plan: GroverPlan)
        : Result<QuantumState, QuantumError> =
        result {
            let! state0 = backend.InitializeState oracle.NumQubits

            match plan with
            | ExecuteNatively (ops, iterations, _exactness) ->
                let! prepared = backend.ApplyOperation ops.Prepare state0

                let step s =
                    s
                    |> backend.ApplyOperation ops.Oracle
                    |> Result.bind (backend.ApplyOperation ops.Diffusion)

                return! repeatM iterations step prepared

            | ExecuteViaOps (prepareOps, iterationOps, iterations, _exactness) ->
                let! prepared = backend.ApplyOperation (QuantumOperation.Sequence prepareOps) state0

                let step s = backend.ApplyOperation (QuantumOperation.Sequence iterationOps) s

                return! repeatM iterations step prepared
        }

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
        (oracle: Oracle.CompiledOracle)
        (backend: IQuantumBackend)
        (config: GroverConfig)
        : Result<GroverResult, QuantumError> =

        let stopwatch = Stopwatch.StartNew()

        // Determine iteration count (auto-calculate if not specified)
        let iterationCount =
            match config.Iterations with
            | Some k -> k
            | None ->
                // Auto-calculate based on number of solutions
                let numSolutions =
                    match oracle.Spec with
                    | Oracle.OracleSpec.SingleTarget _ -> 1
                    | Oracle.OracleSpec.Solutions solutionList -> solutionList.Length
                    | Oracle.OracleSpec.Predicate _ -> 1 // Conservative estimate
                    | _ -> 1 // Conservative estimate for combinators

                calculateOptimalIterations oracle.NumQubits numSolutions

        // Build intent → plan → execute (ADR).
        let preparedAndIteratedState =
            let intent =
                {
                    Oracle = oracle
                    Iterations = iterationCount
                    Exactness = Exact
                }

            plan backend intent
            |> Result.bind (executePlan backend oracle)

        result {
            let! finalState = preparedAndIteratedState

            // Step 4: Measure final state
            let measurements = UnifiedBackend.measureState finalState config.Shots

            // Step 5: Extract solutions from measurements
            //
            // Important: Grover measurements are probabilistic; finite shots may yield "false positives"
            // above the solution threshold. Filter candidates classically via the oracle predicate.
            let distribution = extractDistribution measurements
            let candidateSolutions = extractSolutions distribution config.Shots config.SolutionThreshold
            let solutions = candidateSolutions |> List.filter (Oracle.isSolution oracle.Spec)

            let successProb =
                solutions
                |> List.choose (fun s -> distribution |> Map.tryFind s)
                |> function
                    | [] -> 0.0
                    | counts -> float (List.max counts) / float config.Shots

            // Calculate execution time
            stopwatch.Stop()
            let elapsedMs = stopwatch.Elapsed.TotalMilliseconds

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
        (backend: IQuantumBackend)
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
        (backend: IQuantumBackend)
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
        (backend: IQuantumBackend)
        (config: GroverConfig)
        : Result<GroverResult, QuantumError> =
        
        result {
            let! oracle = Oracle.fromPredicate predicate numQubits
            return! search oracle backend config
        }
    
    /// Search for even numbers
    let searchEven
        (numQubits: int)
        (backend: IQuantumBackend)
        (config: GroverConfig)
        : Result<GroverResult, QuantumError> =
        
        result {
            let! oracle = Oracle.even numQubits
            return! search oracle backend config
        }
    
    /// Search for odd numbers
    let searchOdd
        (numQubits: int)
        (backend: IQuantumBackend)
        (config: GroverConfig)
        : Result<GroverResult, QuantumError> =
        
        result {
            let! oracle = Oracle.odd numQubits
            return! search oracle backend config
        }
    
    /// Search for values in a range [min, max]
    /// 
    /// Example:
    ///   let! result = searchInRange 10 20 5 backend defaultConfig
    ///   // Searches for values between 10 and 20 in 5-qubit space (0-31)
    let searchInRange
        (minValue: int)
        (maxValue: int)
        (numQubits: int)
        (backend: IQuantumBackend)
        (config: GroverConfig)
        : Result<GroverResult, QuantumError> =
        
        result {
            let predicate x = x >= minValue && x <= maxValue
            let! oracle = Oracle.fromPredicate predicate numQubits
            return! search oracle backend config
        }
    
    /// Search for values divisible by a given number
    /// 
    /// Example:
    ///   let! result = searchDivisibleBy 3 4 backend defaultConfig
    ///   // Searches for numbers divisible by 3 in 4-qubit space (0-15)
    ///   // Would find: 0, 3, 6, 9, 12, 15
    let searchDivisibleBy
        (divisor: int)
        (numQubits: int)
        (backend: IQuantumBackend)
        (config: GroverConfig)
        : Result<GroverResult, QuantumError> =
        
        if divisor <= 0 then
            Error (QuantumError.ValidationError ("Divisor", "must be positive"))
        else
            result {
                let predicate x = x % divisor = 0
                let! oracle = Oracle.fromPredicate predicate numQubits
                return! search oracle backend config
            }
    
    // ========================================================================
    // ADVANCED SEARCH - Multi-round search
    // ========================================================================
    
    /// <summary>
    /// Multi-round search with retry logic and result aggregation.
    /// </summary>
    /// <remarks>
    /// Executes Grover search multiple times independently and aggregates results.
    /// This is useful when:
    /// <list type="bullet">
    /// <item>Dealing with noisy backends (averaging improves reliability)</item>
    /// <item>Number of solutions is unknown (multiple rounds increase coverage)</item>
    /// <item>Need higher confidence in results (statistical aggregation)</item>
    /// <item>Building production systems requiring fault tolerance</item>
    /// </list>
    /// 
    /// The function gracefully handles partial failures - if some rounds fail but at least
    /// one succeeds, it returns aggregated results from successful rounds only. It only
    /// returns an error if ALL rounds fail.
    /// 
    /// Result aggregation:
    /// <list type="bullet">
    /// <item><c>Solutions</c>: Distinct union of all found solutions (sorted)</item>
    /// <item><c>SuccessProbability</c>: Average across all successful rounds</item>
    /// <item><c>Iterations</c>: Average iterations per round</item>
    /// <item><c>Measurements</c>: Sum of all measurement distributions</item>
    /// <item><c>ExecutionTimeMs</c>: Total time across all rounds</item>
    /// </list>
    /// </remarks>
    /// <param name="oracle">Compiled oracle marking target states</param>
    /// <param name="backend">Quantum backend (gate-based or topological)</param>
    /// <param name="config">Algorithm configuration (shots, thresholds, iterations, seed)</param>
    /// <param name="rounds">Number of independent search rounds to execute (must be positive)</param>
    /// <returns>
    /// <c>Ok GroverResult</c> with aggregated results from successful rounds, or
    /// <c>Error QuantumError</c> if validation fails or all rounds fail.
    /// </returns>
    /// <example>
    /// <code>
    /// // Search for values 5 and 7 with 5 independent rounds
    /// let! oracle = Oracle.forValues [5; 7] 4
    /// let! result = searchMultiRound oracle backend defaultConfig 5
    /// 
    /// // Result contains:
    /// // - Solutions: [5; 7] (distinct union from all rounds)
    /// // - SuccessProbability: averaged across 5 rounds
    /// // - Measurements: sum of 5000 total measurements (1000 shots × 5 rounds)
    /// </code>
    /// </example>
    let searchMultiRound
        (oracle: Oracle.CompiledOracle)
        (backend: IQuantumBackend)
        (config: GroverConfig)
        (rounds: int)
        : Result<GroverResult, QuantumError> =
        
        if rounds < 1 then
            Error (QuantumError.ValidationError ("Rounds", "number of rounds must be positive"))
        else
            try
                // Execute search multiple times
                let results =
                    [1 .. rounds]
                    |> List.choose (fun _ ->
                        match search oracle backend config with
                        | Ok res -> Some res
                        | Error _ -> None
                    )
                
                if List.isEmpty results then
                    Error (QuantumError.OperationError ("Grover search rounds", "all search rounds failed"))
                else
                    // Aggregate results
                    let allSolutions =
                        results
                        |> List.collect (fun r -> r.Solutions)
                        |> List.distinct
                        |> List.sort
                    
                    let avgSuccessProb =
                        results
                        |> List.averageBy (fun r -> r.SuccessProbability)
                    
                    let avgIterations =
                        results
                        |> List.averageBy (fun r -> float r.Iterations)
                        |> int
                    
                    let totalTime =
                        results
                        |> List.sumBy (fun r -> r.ExecutionTimeMs)
                    
                    // Aggregate measurement counts
                    let aggregatedMeasurements =
                        results
                        |> List.collect (fun r -> r.Measurements |> Map.toList)
                        |> List.groupBy fst
                        |> List.map (fun (key, values) -> (key, values |> List.sumBy snd))
                        |> Map.ofList
                    
                    Ok {
                        Solutions = allSolutions
                        SuccessProbability = avgSuccessProb
                        Iterations = avgIterations
                        Measurements = aggregatedMeasurements
                        ExecutionTimeMs = totalTime
                    }
            with
            | ex -> Error (QuantumError.OperationError ("Multi-round search", $"failed: {ex.Message}"))
    
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
