namespace FSharp.Azure.Quantum.GroverSearch

open System

/// Amplitude Amplification Backend Adapter Module
/// 
/// Bridges Amplitude Amplification algorithm to IQuantumBackend interface, enabling execution
/// on cloud quantum hardware (IonQ, Rigetti) in addition to local simulation.
/// 
/// Key responsibilities:
/// - Convert state preparation to quantum circuit
/// - Convert oracle to circuit (reuse from GroverBackendAdapter)
/// - Convert reflection operator to circuit
/// - Execute amplitude amplification via IQuantumBackend
/// 
/// Amplitude Amplification Circuit Structure:
/// 1. State preparation: A|0⟩ (custom superposition)
/// 2. Repeat k times:
///    a. Oracle: O (marks "good" states)
///    b. Reflection: S₀ = 2|ψ⟩⟨ψ| - I (reflects about prepared state)
/// 3. Measure
/// 
/// Design Note:
/// Since state preparation and reflection operators are functions (StateVector → StateVector),
/// we need to either:
/// - Require circuit representation directly (preferred for backend execution)
/// - Support common patterns (uniform, Gaussian, etc.)
/// - Analyze function behavior (computationally expensive)
/// 
/// This implementation supports common patterns with extensibility.
module AmplitudeAmplificationAdapter =
    
    open FSharp.Azure.Quantum
    open FSharp.Azure.Quantum.Core.BackendAbstraction
    open FSharp.Azure.Quantum.Core.CircuitAbstraction
    open FSharp.Azure.Quantum.CircuitBuilder
    open FSharp.Azure.Quantum.GroverSearch.Oracle
    open FSharp.Azure.Quantum.GroverSearch.GroverIteration
    open FSharp.Azure.Quantum.GroverSearch.AmplitudeAmplification
    
    // Import helper functions from GroverBackendAdapter (module is named BackendAdapter)
    open FSharp.Azure.Quantum.GroverSearch.BackendAdapter
    
    // ========================================================================
    // TYPES - Circuit representations for state prep and reflection
    // ========================================================================
    
    /// Circuit-based state preparation (for backend execution)
    /// Instead of StateVector function, user provides circuit that prepares state
    type StatePreparationCircuit = Circuit
    
    /// Circuit-based reflection operator
    /// Instead of StateVector function, user provides circuit for reflection
    type ReflectionCircuit = Circuit
    
    /// Backend-compatible amplitude amplification configuration
    type BackendAmplificationConfig = {
        /// Number of qubits in the system
        NumQubits: int
        
        /// State preparation circuit A (prepares initial superposition)
        StatePreparationCircuit: StatePreparationCircuit
        
        /// Oracle (marks "good" states) - uses existing CompiledOracle
        Oracle: CompiledOracle
        
        /// Reflection circuit S₀ (reflects about prepared state)
        /// If None, uses standard Grover diffusion (reflection about uniform superposition)
        ReflectionCircuit: ReflectionCircuit option
        
        /// Number of amplification iterations
        Iterations: int
    }
    
    // ========================================================================
    // COMMON STATE PREPARATION CIRCUITS
    // ========================================================================
    
    /// Create uniform superposition preparation circuit (H^⊗n)
    /// 
    /// This is the standard Grover initialization:
    /// |0⟩^⊗n → (1/√N) Σ|x⟩
    let uniformSuperpositionCircuit (numQubits: int) : StatePreparationCircuit =
        [0 .. numQubits - 1]
        |> List.fold (fun circuit i ->
            addGate (H i) circuit
        ) (empty numQubits)
    
    /// Create equal superposition of specific qubits
    /// 
    /// Example: [0; 2] applies H to qubits 0 and 2, leaving others in |0⟩
    let partialSuperpositionCircuit (numQubits: int) (qubitIndices: int list) : StatePreparationCircuit =
        qubitIndices
        |> List.filter (fun i -> i >= 0 && i < numQubits)
        |> List.fold (fun circuit i ->
            addGate (H i) circuit
        ) (empty numQubits)
    
    /// Create computational basis state preparation
    /// 
    /// Prepares |state⟩ by applying X gates to set appropriate bits
    let basisStateCircuit (numQubits: int) (state: int) : StatePreparationCircuit =
        [0 .. numQubits - 1]
        |> List.fold (fun circuit i ->
            if (state >>> i) &&& 1 = 1 then
                addGate (X i) circuit
            else
                circuit
        ) (empty numQubits)
    
    // ========================================================================
    // REFLECTION CIRCUIT SYNTHESIS
    // ========================================================================
    
    /// Create standard Grover diffusion circuit
    /// 
    /// Reflects about uniform superposition:
    /// D = H^⊗n · (2|0⟩⟨0| - I) · H^⊗n
    /// 
    /// Equivalent to reflection about |+⟩^⊗n state
    /// 
    /// Circuit structure:
    /// 1. H^⊗n (transform to computational basis)
    /// 2. X^⊗n (flip all qubits)
    /// 3. Multi-controlled Z (phase flip |0⟩)
    /// 4. X^⊗n (flip back)
    /// 5. H^⊗n (transform back)
    let groverDiffusionCircuit (numQubits: int) : ReflectionCircuit =
        let initialCircuit = empty numQubits
        
        // Step 1: Apply H to all qubits
        let circuitWithH1 =
            [0 .. numQubits - 1]
            |> List.fold (fun circuit i ->
                addGate (H i) circuit
            ) initialCircuit
        
        // Step 2: Apply X to all qubits (invert)
        let circuitWithX1 =
            [0 .. numQubits - 1]
            |> List.fold (fun circuit i ->
                addGate (X i) circuit
            ) circuitWithH1
        
        // Step 3: Multi-controlled Z gate (phase flip on |0⟩)
        let circuitWithZ =
            if numQubits = 1 then
                addGate (Z 0) circuitWithX1
            else
                // Multi-controlled Z: controls = [0..n-2], target = n-1
                let controls = [0 .. (numQubits - 2)]
                let target = numQubits - 1
                addMultiControlledZ controls target circuitWithX1
        
        // Step 4: Apply X to all qubits (invert back)
        let circuitWithX2 =
            [0 .. numQubits - 1]
            |> List.fold (fun circuit i ->
                addGate (X i) circuit
            ) circuitWithZ
        
        // Step 5: Apply H to all qubits
        [0 .. numQubits - 1]
        |> List.fold (fun circuit i ->
            addGate (H i) circuit
        ) circuitWithX2
    
    /// Create reflection circuit about arbitrary state preparation
    /// 
    /// For state preparation A|0⟩, creates circuit for:
    /// S_ψ = A · (2|0⟩⟨0| - I) · A†
    /// 
    /// Circuit structure:
    /// 1. A† (inverse of state preparation)
    /// 2. (2|0⟩⟨0| - I) (reflection about |0⟩)
    /// 3. A (forward state preparation)
    /// 
    /// IMPORTANT LIMITATION: This function attempts to invert the state preparation circuit.
    /// Full circuit inversion is complex and gate-dependent. This implementation uses a
    /// SIMPLIFIED inversion that works for common cases:
    /// - Hadamard gates: H† = H (self-inverse)
    /// - Pauli gates (X, Y, Z): Self-inverse
    /// - Rotation gates: Negate angles
    /// - CNOT: Self-inverse
    /// 
    /// For complex state preparations, users should provide the ReflectionCircuit directly
    /// in BackendAmplificationConfig instead of relying on this automatic inversion.
    let reflectionAboutPreparedStateCircuit 
        (statePrep: StatePreparationCircuit) 
        (numQubits: int) : ReflectionCircuit =
        
        let initialCircuit = empty numQubits
        
        // Step 1: Apply inverse of state preparation (A†)
        // Reverse gate order and invert each gate
        let invertedGates = 
            statePrep.Gates 
            |> List.rev  // Reverse order for inverse
            |> List.map (fun gate ->
                match gate with
                // Self-inverse gates
                | H q -> H q
                | X q -> X q
                | Y q -> Y q
                | Z q -> Z q
                | CNOT (c, t) -> CNOT (c, t)
                | CZ (c, t) -> CZ (c, t)
                | SWAP (q1, q2) -> SWAP (q1, q2)
                
                // Rotation gates: negate angle
                | RX (q, angle) -> RX (q, -angle)
                | RY (q, angle) -> RY (q, -angle)
                | RZ (q, angle) -> RZ (q, -angle)
                | P (q, theta) -> P (q, -theta)  // P† = P(-θ)
                | CP (c, t, theta) -> CP (c, t, -theta)  // CP† = CP(-θ)
                
                // Phase gates
                | S q -> SDG q  // S† = S-dagger
                | SDG q -> S q
                | T q -> TDG q  // T† = T-dagger
                | TDG q -> T q
                
                // Multi-qubit gates - self-inverse
                | CCX (c1, c2, t) -> CCX (c1, c2, t)
                | MCZ (controls, target) -> MCZ (controls, target))  // MCZ is self-inverse (Z† = Z)
        
        // Add inverted gates to circuit
        let circuitWithInverse =
            invertedGates
            |> List.fold (fun circuit gate ->
                addGate gate circuit
            ) initialCircuit
        
        // Step 2: Reflection about |0⟩ state
        // (2|0⟩⟨0| - I) implemented as: X^⊗n · Multi-CZ · X^⊗n
        let circuitWithX1 =
            [0 .. numQubits - 1]
            |> List.fold (fun circuit i ->
                addGate (X i) circuit
            ) circuitWithInverse
        
        let circuitWithZ =
            if numQubits = 1 then
                addGate (Z 0) circuitWithX1
            else
                let controls = [0 .. (numQubits - 2)]
                let target = numQubits - 1
                addMultiControlledZ controls target circuitWithX1
        
        let circuitWithX2 =
            [0 .. numQubits - 1]
            |> List.fold (fun circuit i ->
                addGate (X i) circuit
            ) circuitWithZ
        
        // Step 3: Apply state preparation (A)
        statePrep.Gates
        |> List.fold (fun circuit gate ->
            addGate gate circuit
        ) circuitWithX2
    
    // ========================================================================
    // AMPLITUDE AMPLIFICATION CIRCUIT SYNTHESIS
    // ========================================================================
    
    /// Compose single amplitude amplification iteration
    /// 
    /// Single iteration = Oracle · Reflection
    let amplificationIterationCircuit 
        (oracleCircuit: Circuit) 
        (reflectionCircuit: Circuit) : Circuit =
        
        // Compose: Oracle then Reflection
        compose oracleCircuit reflectionCircuit
    
    /// Create full amplitude amplification circuit
    /// 
    /// Structure:
    /// 1. State preparation A|0⟩
    /// 2. (Oracle · Reflection)^k iterations
    let fullAmplificationCircuit 
        (config: BackendAmplificationConfig) 
        (oracleCircuit: Circuit) 
        (reflectionCircuit: Circuit) : Circuit =
        
        // Start with state preparation
        let initialCircuit = config.StatePreparationCircuit
        
        // Apply iterations
        let iterationCircuit = amplificationIterationCircuit oracleCircuit reflectionCircuit
        
        [1 .. config.Iterations]
        |> List.fold (fun circuit _ ->
            compose circuit iterationCircuit
        ) initialCircuit
    
    // ========================================================================
    // BACKEND EXECUTION
    // ========================================================================
    
    /// Execute amplitude amplification using IQuantumBackend (async version)
    /// 
    /// Parameters:
    /// - config: Amplitude amplification configuration with circuits
    /// - backend: Quantum backend to execute on
    /// - numShots: Number of measurement shots
    /// 
    /// Returns: Async<Result<SearchResult, string>> - Async computation with result or error
    let executeAmplificationWithBackendAsync 
        (config: BackendAmplificationConfig) 
        (backend: IQuantumBackend) 
        (numShots: int) 
        : Async<Result<GroverIteration.SearchResult, string>> = async {
        
        try
            // Step 1: Validate inputs
            if config.Iterations < 0 then
                return Error "Number of iterations must be non-negative"
            elif numShots <= 0 then
                return Error "Number of shots must be positive"
            elif config.NumQubits > backend.MaxQubits then
                return Error $"Amplification requires {config.NumQubits} qubits but backend '{backend.Name}' supports max {backend.MaxQubits}"
            else
                // Step 2: Convert oracle to circuit (reuse from GroverBackendAdapter)
                match oracleToCircuit config.Oracle.Spec config.NumQubits with
                | Error msg -> return Error msg
                | Ok oracleCircuit ->
                    // Step 3: Get reflection circuit (use provided or default to Grover diffusion)
                    let reflectionCircuit =
                        match config.ReflectionCircuit with
                        | Some circuit -> circuit
                        | None -> groverDiffusionCircuit config.NumQubits
                    
                    // Step 4: Create full amplification circuit
                    let fullCircuit = fullAmplificationCircuit config oracleCircuit reflectionCircuit
                    
                    // Step 5: Wrap circuit in ICircuit interface
                    let circuitWrapper = CircuitWrapper(fullCircuit)
                    
                    // Step 6: Execute on backend asynchronously
                    let! execResult = backend.ExecuteAsync circuitWrapper numShots
                    
                    match execResult with
                    | Error msg -> return Error msg
                    | Ok executionResult ->
                        // Step 7: Convert measurements to basis state counts
                        let counts = 
                            executionResult.Measurements
                            |> Array.map (fun bitstring ->
                                bitstring 
                                |> Array.rev
                                |> Array.fold (fun acc bit -> acc * 2 + bit) 0)
                            |> Array.groupBy id
                            |> Array.map (fun (state, instances) -> (state, Array.length instances))
                            |> Map.ofArray
                        
                        // Step 8: Extract solutions (top 10% by count)
                        let solutions = extractTopSolutions counts 0.1
                        let successProb = calculateSuccessProb solutions counts numShots
                        
                        return Ok {
                            Solutions = solutions
                            SuccessProbability = successProb
                            IterationsApplied = config.Iterations
                            MeasurementCounts = counts
                            Shots = numShots
                            Success = successProb >= 0.3
                        }
        
        with ex ->
            return Error $"Amplitude amplification backend execution failed: {ex.Message}"
    }

    /// Execute amplitude amplification using IQuantumBackend (synchronous wrapper)
    /// 
    /// This is a synchronous wrapper around executeAmplificationWithBackendAsync for backward compatibility.
    /// For cloud backends (IonQ, Rigetti), prefer using executeAmplificationWithBackendAsync directly.
    /// 
    /// Parameters:
    /// - config: Amplitude amplification configuration with circuits
    /// - backend: Quantum backend to execute on
    /// - numShots: Number of measurement shots
    /// 
    /// Returns: AmplificationResult with solutions and success probability
    let executeAmplificationWithBackend 
        (config: BackendAmplificationConfig) 
        (backend: IQuantumBackend) 
        (numShots: int) 
        : Result<GroverIteration.SearchResult, string> =
        executeAmplificationWithBackendAsync config backend numShots
        |> Async.RunSynchronously
    
    // ========================================================================
    // CONVENIENCE FUNCTIONS
    // ========================================================================
    
    /// Execute standard Grover search using amplitude amplification
    /// (Uniform superposition + Grover diffusion)
    let executeGroverWithAmplification 
        (oracle: CompiledOracle) 
        (backend: IQuantumBackend) 
        (iterations: int) 
        (numShots: int) 
        : Result<GroverIteration.SearchResult, string> =
        
        let config = {
            NumQubits = oracle.NumQubits
            StatePreparationCircuit = uniformSuperpositionCircuit oracle.NumQubits
            Oracle = oracle
            ReflectionCircuit = None  // Use default Grover diffusion
            Iterations = iterations
        }
        
        executeAmplificationWithBackend config backend numShots
    
    /// Execute amplitude amplification with custom state preparation
    let executeWithCustomPreparation 
        (oracle: CompiledOracle) 
        (statePrep: StatePreparationCircuit) 
        (backend: IQuantumBackend) 
        (iterations: int) 
        (numShots: int) 
        : Result<GroverIteration.SearchResult, string> =
        
        let config = {
            NumQubits = oracle.NumQubits
            StatePreparationCircuit = statePrep
            Oracle = oracle
            ReflectionCircuit = Some (reflectionAboutPreparedStateCircuit statePrep oracle.NumQubits)
            Iterations = iterations
        }
        
        executeAmplificationWithBackend config backend numShots
    
    // ========================================================================
    // EXAMPLES
    // ========================================================================
    
    module Examples =
        
        /// Example: Amplitude amplification with partial superposition
        let partialAmplification () : Result<GroverIteration.SearchResult, string> =
            // Search in 4-qubit space, but only superpose first 3 qubits
            let numQubits = 4
            let targetValue = 5  // Binary: 0101
            
            // Create oracle for target
            match Oracle.forValue targetValue numQubits with
            | Error msg -> Error msg
            | Ok oracle ->
                // Prepare superposition only on qubits [0, 1, 2]
                let statePrep = partialSuperpositionCircuit numQubits [0; 1; 2]
                
                // Create backend
                let backend = FSharp.Azure.Quantum.Core.BackendAbstraction.createLocalBackend()
                
                // Calculate optimal iterations
                let searchSpaceSize = 8  // 2^3 (only 3 qubits in superposition)
                let numSolutions = 1
                
                match GroverIteration.optimalIterations searchSpaceSize numSolutions with
                | Error msg -> Error msg
                | Ok iterations ->
                    executeWithCustomPreparation oracle statePrep backend iterations 1000
