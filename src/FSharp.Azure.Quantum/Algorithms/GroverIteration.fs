namespace FSharp.Azure.Quantum.GroverSearch

open System

/// Grover Iteration Module
/// 
/// Implements the core Grover iteration: Oracle + Diffusion operator.
/// The diffusion operator (also called inversion about average) amplifies
/// the amplitude of marked states while reducing others.
/// 
/// Grover's algorithm works by:
/// 1. Initialize uniform superposition: H^⊗n|0⟩
/// 2. Repeat k times: Apply Oracle, then Apply Diffusion
/// 3. Measure
/// 
/// Optimal iterations: k = π/4 * √(N/M) where N = search space, M = solutions
/// 
/// ALL GROVER ITERATION CODE IN SINGLE FILE
module GroverIteration =
    
    open FSharp.Azure.Quantum.LocalSimulator
    open FSharp.Azure.Quantum.GroverSearch.Oracle
    
    // ============================================================================
    // TYPES - Pure data structures
    // ============================================================================
    
    /// Configuration for Grover iteration
    type IterationConfig = {
        /// Number of iterations to apply
        NumIterations: int
        
        /// Track success probability after each iteration (debug mode)
        TrackProbabilities: bool
    }
    
    /// Result of Grover iteration with diagnostics
    type IterationResult = {
        /// Final quantum state after iterations
        FinalState: StateVector.StateVector
        
        /// Number of iterations applied
        IterationsApplied: int
        
        /// Success probability history (if tracked)
        ProbabilityHistory: float list option
        
        /// Expected success probability (theoretical)
        ExpectedSuccessProbability: float
    }
    
    /// Result from Grover search (used by Search and BackendAdapter)
    type SearchResult = {
        /// Solutions found
        Solutions: int list
        
        /// Success probability achieved
        SuccessProbability: float
        
        /// Number of iterations applied
        IterationsApplied: int
        
        /// Measurement counts (bitstring -> count)
        MeasurementCounts: Map<int, int>
        
        /// Total measurement shots
        Shots: int
        
        /// Search succeeded (found solution with probability > threshold)
        Success: bool
    }
    
    // ============================================================================
    // DIFFUSION OPERATOR - Inversion about average
    // ============================================================================
    
    /// Apply diffusion operator (inversion about average)
    /// 
    /// Diffusion operator: D = 2|s⟩⟨s| - I
    /// where |s⟩ = H^⊗n|0⟩ is uniform superposition
    /// 
    /// Implemented as: H^⊗n · (2|0⟩⟨0| - I) · H^⊗n
    /// 
    /// Steps:
    /// 1. Apply H to all qubits
    /// 2. Apply conditional phase shift (flip phase of |0⟩ state)
    /// 3. Apply H to all qubits again
    /// 
    /// This is a pure function - creates new state
    let diffusionOperator (numQubits: int) (state: StateVector.StateVector) : StateVector.StateVector =
        if StateVector.numQubits state <> numQubits then
            failwith $"State has {StateVector.numQubits state} qubits, expected {numQubits}"
        
        // Step 1: Apply H to all qubits
        let afterH1 =
            [0 .. numQubits - 1]
            |> List.fold (fun s qubitIdx -> Gates.applyH qubitIdx s) state
        
        // Step 2: Conditional phase shift (2|0⟩⟨0| - I)
        // This flips the phase of all states except |0...0⟩
        let dimension = StateVector.dimension afterH1
        let afterPhaseShift =
            [| 0 .. dimension - 1 |]
            |> Array.map (fun i ->
                let amp = StateVector.getAmplitude i afterH1
                if i = 0 then
                    // State |0...0⟩: multiply by (2 - 1) = 1 (no change)
                    amp
                else
                    // All other states: multiply by (0 - 1) = -1 (flip phase)
                    -amp
            )
            |> StateVector.create
        
        // Step 3: Apply H to all qubits again
        let afterH2 =
            [0 .. numQubits - 1]
            |> List.fold (fun s qubitIdx -> Gates.applyH qubitIdx s) afterPhaseShift
        
        afterH2
    
    /// Alternative implementation: Direct inversion about average
    /// 
    /// For each amplitude: new_amp = 2*avg - old_amp
    /// where avg = average of all amplitudes
    /// 
    /// This is mathematically equivalent to H^⊗n · (2|0⟩⟨0| - I) · H^⊗n
    /// but more intuitive to understand
    let diffusionOperatorDirect (state: StateVector.StateVector) : StateVector.StateVector =
        let dimension = StateVector.dimension state
        
        // Calculate average amplitude (complex)
        let avgReal =
            [0 .. dimension - 1]
            |> List.sumBy (fun i -> (StateVector.getAmplitude i state).Real)
            |> fun sum -> sum / float dimension
        
        let avgImag =
            [0 .. dimension - 1]
            |> List.sumBy (fun i -> (StateVector.getAmplitude i state).Imaginary)
            |> fun sum -> sum / float dimension
        
        let avgAmp = System.Numerics.Complex(avgReal, avgImag)
        
        // Invert about average: new = 2*avg - old
        let newAmplitudes =
            [| 0 .. dimension - 1 |]
            |> Array.map (fun i ->
                let amp = StateVector.getAmplitude i state
                2.0 * avgAmp - amp
            )
        
        StateVector.create newAmplitudes
    
    // ============================================================================
    // GROVER ITERATION - Oracle + Diffusion
    // ============================================================================
    
    /// Apply single Grover iteration: Oracle then Diffusion
    /// 
    /// One iteration = Oracle(state) |> Diffusion(state)
    /// This amplifies marked states and suppresses unmarked ones
    let applyIteration (oracle: CompiledOracle) (state: StateVector.StateVector) : StateVector.StateVector =
        // Step 1: Apply oracle (mark solutions)
        let afterOracle = oracle.LocalSimulation state
        
        // Step 2: Apply diffusion (amplify marked states)
        let afterDiffusion = diffusionOperator oracle.NumQubits afterOracle
        
        afterDiffusion
    
    /// Apply multiple Grover iterations
    /// 
    /// Applies k iterations to amplify solution amplitudes
    let applyIterations (oracle: CompiledOracle) (k: int) (initialState: StateVector.StateVector) : StateVector.StateVector =
        if k < 0 then
            failwith $"Number of iterations must be non-negative, got {k}"
        
        // Apply k iterations using fold
        [1 .. k]
        |> List.fold (fun state _ -> applyIteration oracle state) initialState
    
    // ============================================================================
    // OPTIMAL ITERATION COUNT - Theoretical calculation
    // ============================================================================
    
    /// Calculate optimal number of Grover iterations
    /// 
    /// Formula: k = π/4 * √(N/M)
    /// where N = search space size (2^n)
    ///       M = number of solutions
    /// 
    /// Returns the integer closest to theoretical optimum
    let optimalIterations (searchSpaceSize: int) (numSolutions: int) : Result<int, string> =
        if searchSpaceSize < 1 then
            Error $"Search space size must be positive, got {searchSpaceSize}"
        elif numSolutions < 1 then
            Error $"Number of solutions must be positive, got {numSolutions}"
        elif numSolutions > searchSpaceSize then
            Error $"Number of solutions ({numSolutions}) exceeds search space size ({searchSpaceSize})"
        else
            // Special case: all states are solutions
            if numSolutions = searchSpaceSize then
                Ok 0  // No iterations needed - already in solution space
            else
                // Calculate k = π/4 * √(N/M)
                let ratio = float searchSpaceSize / float numSolutions
                let k = (Math.PI / 4.0) * Math.Sqrt(ratio)
                
                // Round to nearest integer
                Ok (int (Math.Round(k)))
    
    /// Calculate optimal iterations from oracle
    /// Uses oracle's expected solution count if available
    let optimalIterationsForOracle (oracle: CompiledOracle) : Result<int, string> option =
        match oracle.ExpectedSolutions with
        | Some numSolutions ->
            let searchSpaceSize = 1 <<< oracle.NumQubits
            Some (optimalIterations searchSpaceSize numSolutions)
        | None ->
            None  // Cannot calculate without knowing solution count
    
    // ============================================================================
    // SUCCESS PROBABILITY - Theoretical and empirical
    // ============================================================================
    
    /// Calculate theoretical success probability after k iterations
    /// 
    /// For single solution: P(success) = sin²((2k+1)θ)
    /// where θ = arcsin(1/√N)
    /// 
    /// This is a simplified formula for educational purposes
    let theoreticalSuccessProbability (numQubits: int) (numSolutions: int) (k: int) : float =
        let searchSpaceSize = 1 <<< numQubits
        
        if numSolutions = 1 then
            // Single solution case (exact formula)
            let theta = Math.Asin(1.0 / Math.Sqrt(float searchSpaceSize))
            let prob = Math.Sin((2.0 * float k + 1.0) * theta)
            prob * prob
        else
            // Multiple solutions (approximation)
            let ratio = float numSolutions / float searchSpaceSize
            let theta = Math.Asin(Math.Sqrt(ratio))
            let prob = Math.Sin((2.0 * float k + 1.0) * theta)
            prob * prob
    
    /// Calculate empirical success probability from quantum state
    /// 
    /// Sums probabilities of all solution states
    let empiricalSuccessProbability (oracle: CompiledOracle) (state: StateVector.StateVector) : float =
        let solutions = Oracle.listSolutions oracle
        
        solutions
        |> List.sumBy (fun sol -> StateVector.probability sol state)
    
    // ============================================================================
    // GROVER SEARCH EXECUTION - High-level API
    // ============================================================================
    
    /// Execute Grover's algorithm with optimal iterations
    /// 
    /// Returns final state and diagnostics
    let execute (oracle: CompiledOracle) (config: IterationConfig) : Result<IterationResult, string> =
        try
            // Initialize uniform superposition
            let initialState = StateVector.init oracle.NumQubits
            let uniformState =
                [0 .. oracle.NumQubits - 1]
                |> List.fold (fun s qubitIdx -> Gates.applyH qubitIdx s) initialState
            
            // Apply iterations with optional probability tracking
            let (finalState, probHistory) =
                [1 .. config.NumIterations]
                |> List.fold (fun (state, history) _ ->
                    let newState = applyIteration oracle state
                    
                    let newHistory =
                        if config.TrackProbabilities then
                            let prob = empiricalSuccessProbability oracle newState
                            history @ [prob]
                        else
                            history
                    
                    (newState, newHistory)
                ) (uniformState, [])
            
            // Calculate expected success probability
            let numSolutions =
                match oracle.ExpectedSolutions with
                | Some count -> count
                | None -> Oracle.countSolutions oracle
            
            let expectedProb =
                theoreticalSuccessProbability oracle.NumQubits numSolutions config.NumIterations
            
            Ok {
                FinalState = finalState
                IterationsApplied = config.NumIterations
                ProbabilityHistory = if config.TrackProbabilities then Some probHistory else None
                ExpectedSuccessProbability = expectedProb
            }
        with
        | ex -> Error $"Grover execution failed: {ex.Message}"
    
    /// Execute Grover's algorithm with automatic optimal iteration count
    let executeOptimal (oracle: CompiledOracle) (trackProbabilities: bool) : Result<IterationResult, string> =
        match optimalIterationsForOracle oracle with
        | Some kResult ->
            match kResult with
            | Ok k ->
                let config = {
                    NumIterations = k
                    TrackProbabilities = trackProbabilities
                }
                execute oracle config
            | Error msg ->
                Error msg
        | None ->
            Error "Cannot determine optimal iterations: oracle solution count unknown"
    
    // ============================================================================
    // UTILITY FUNCTIONS
    // ============================================================================
    
    /// Create default iteration config
    let defaultConfig (numIterations: int) : IterationConfig =
        {
            NumIterations = numIterations
            TrackProbabilities = false
        }
    
    /// Create debug config (with probability tracking)
    let debugConfig (numIterations: int) : IterationConfig =
        {
            NumIterations = numIterations
            TrackProbabilities = true
        }
