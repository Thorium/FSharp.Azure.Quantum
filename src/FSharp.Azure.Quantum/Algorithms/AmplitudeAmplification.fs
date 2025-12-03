namespace FSharp.Azure.Quantum.GroverSearch

open System
open System.Numerics

/// Generalized Amplitude Amplification Module
/// 
/// Amplitude amplification is a generalization of Grover's algorithm that works with
/// arbitrary initial state preparations, not just uniform superposition.
/// 
/// Grover's algorithm is a special case where:
/// - Initial state = uniform superposition H^⊗n|0⟩
/// - Reflection operator = reflection about uniform superposition
/// 
/// General amplitude amplification allows:
/// - Custom initial state preparation A|0⟩
/// - Reflection operator = reflection about A|0⟩
/// 
/// This enables quantum speedups for problems beyond simple search.
module AmplitudeAmplification =
    
    open FSharp.Azure.Quantum.LocalSimulator
    open FSharp.Azure.Quantum.GroverSearch.Oracle
    
    // ========================================================================
    // TYPES - Amplitude amplification configuration
    // ========================================================================
    
    /// State preparation function
    /// Takes initial |0⟩ state and prepares target superposition A|0⟩
    type StatePreparation = StateVector.StateVector -> StateVector.StateVector
    
    /// Reflection operator
    /// Reflects about a specific quantum state
    type ReflectionOperator = StateVector.StateVector -> StateVector.StateVector
    
    /// Configuration for amplitude amplification
    type AmplificationConfig = {
        /// Number of qubits in the system
        NumQubits: int
        
        /// State preparation operator A (prepares initial superposition)
        StatePreparation: StatePreparation
        
        /// Oracle O (marks "good" states)
        Oracle: CompiledOracle
        
        /// Reflection operator S₀ (reflects about prepared state)
        /// If None, uses standard reflection about StatePreparation|0⟩
        ReflectionOperator: ReflectionOperator option
        
        /// Number of amplification iterations
        Iterations: int
    }
    
    /// Result of amplitude amplification
    type AmplificationResult = {
        /// Final quantum state
        FinalState: StateVector.StateVector
        
        /// Number of iterations applied
        IterationsApplied: int
        
        /// Empirical success probability (probability of measuring a solution)
        SuccessProbability: float
        
        /// Measurement counts (if measured)
        MeasurementCounts: Map<int, int>
        
        /// Number of measurement shots
        Shots: int
    }
    
    // ========================================================================
    // REFLECTION OPERATORS - Core building blocks
    // ========================================================================
    
    /// Create reflection operator about a specific state |ψ⟩
    /// 
    /// Reflection operator: R_ψ = 2|ψ⟩⟨ψ| - I
    /// This reflects quantum states about |ψ⟩
    let reflectionAboutState (targetState: StateVector.StateVector) : ReflectionOperator =
        fun (state: StateVector.StateVector) ->
            let dimension = StateVector.dimension state
            
            // Calculate inner product ⟨ψ|φ⟩
            let innerProduct = StateVector.innerProduct targetState state
            
            // R_ψ|φ⟩ = 2⟨ψ|φ⟩|ψ⟩ - |φ⟩
            // CRITICAL: Must use complex multiplication for 2⟨ψ|φ⟩ψᵢ
            // This is where most quantum simulators fail - they only use the real part!
            let reflection =
                [| 0 .. dimension - 1 |]
                |> Array.map (fun i ->
                    let psiAmp = StateVector.getAmplitude i targetState
                    let phiAmp = StateVector.getAmplitude i state
                    
                    // 2⟨ψ|φ⟩ψᵢ - φᵢ (full complex multiplication)
                    let twoInnerProduct = 2.0 * innerProduct
                    let twoPsi = twoInnerProduct * psiAmp
                    twoPsi - phiAmp
                )
                |> StateVector.create
            
            reflection
    
    /// Create standard Grover reflection operator (reflection about uniform superposition)
    /// 
    /// This is the special case: reflection about |+⟩^⊗n = H^⊗n|0⟩
    let groverReflection (numQubits: int) : ReflectionOperator =
        fun (state: StateVector.StateVector) ->
            // Create uniform superposition |+⟩^⊗n using functional pipeline
            let uniformState = 
                [0 .. numQubits - 1]
                |> List.fold (fun s i -> Gates.applyH i s) (StateVector.init numQubits)
            
            // Reflect about uniform superposition
            let reflector = reflectionAboutState uniformState
            reflector state
    
    /// Create reflection operator from state preparation
    /// 
    /// Given state preparation A, creates reflection operator R_A = 2A|0⟩⟨0|A† - I
    /// This is equivalent to: A · (2|0⟩⟨0| - I) · A†
    let reflectionFromPreparation (numQubits: int) (statePrep: StatePreparation) : ReflectionOperator =
        // Prepare target state A|0⟩
        let initialState = StateVector.init numQubits
        let preparedState = statePrep initialState
        
        // Reflect about prepared state
        reflectionAboutState preparedState
    
    // ========================================================================
    // AMPLITUDE AMPLIFICATION - Core algorithm
    // ========================================================================
    
    /// Single amplitude amplification iteration
    /// 
    /// One iteration consists of:
    /// 1. Apply oracle O (mark good states)
    /// 2. Apply reflection S₀ (amplify good states)
    /// 
    /// This is analogous to Grover iteration but with custom reflection operator
    let applyAmplificationIteration (oracle: CompiledOracle) (reflection: ReflectionOperator) (state: StateVector.StateVector) : StateVector.StateVector =
        // Step 1: Apply oracle (phase flip good states)
        let afterOracle = oracle.LocalSimulation state
        
        // Step 2: Apply reflection operator (amplify good states)
        let afterReflection = reflection afterOracle
        
        afterReflection
    
    // ========================================================================
    // NOTE: For amplitude amplification execution, use AmplitudeAmplificationAdapter module
    // which provides backend-compatible execution with circuit-based state preparation.
    // 
    // The legacy StateVector-based execute() function has been removed in favor of
    // backend abstraction. See AmplitudeAmplificationAdapter.fs for backend execution.
    // ========================================================================
    
    // ========================================================================
    // GROVER AS SPECIAL CASE - Show equivalence
    // ========================================================================
    
    /// Create amplitude amplification config for standard Grover search
    /// 
    /// This demonstrates that Grover is a special case of amplitude amplification:
    /// - State preparation = Hadamard on all qubits (uniform superposition)
    /// - Reflection = Grover diffusion operator
    let groverAsAmplification (oracle: CompiledOracle) (iterations: int) : AmplificationConfig =
        let numQubits = oracle.NumQubits
        
        // State preparation: H^⊗n (uniform superposition)
        let statePrep (state: StateVector.StateVector) : StateVector.StateVector =
            [0 .. numQubits - 1]
            |> List.fold (fun s qubitIdx -> Gates.applyH qubitIdx s) state
        
        // Reflection: Grover diffusion operator
        let reflection = groverReflection numQubits
        
        {
            NumQubits = numQubits
            StatePreparation = statePrep
            Oracle = oracle
            ReflectionOperator = Some reflection
            Iterations = iterations
        }
    
    // ========================================================================
    // NOTE: Legacy convenience functions removed
    // ========================================================================
    // 
    // executeGroverViaAmplification() and executeWithCustomPreparation() have been removed.
    // Use AmplitudeAmplificationAdapter module for backend-compatible execution.
    // 
    // See: AmplitudeAmplificationAdapter.executeWithBackend()
    //      AmplitudeAmplificationAdapter.uniformSuperpositionCircuit()
    //      AmplitudeAmplificationAdapter.partialSuperpositionCircuit()
    
    /// Calculate optimal iterations for amplitude amplification
    /// 
    /// For M solutions in N-dimensional space with initial success probability p₀:
    /// k_opt = π/(4θ) where θ = arcsin(√p₀)
    let optimalIterations (searchSpaceSize: int) (numSolutions: int) (initialSuccessProb: float) : int =
        if initialSuccessProb >= 1.0 then
            0  // Already in solution space
        elif initialSuccessProb <= 0.0 then
            // Fall back to standard Grover formula
            let ratio = float searchSpaceSize / float numSolutions
            int (Math.Round((Math.PI / 4.0) * Math.Sqrt(ratio)))
        else
            // General formula: k = π/(4θ) where θ = arcsin(√p₀)
            let theta = Math.Asin(Math.Sqrt(initialSuccessProb))
            int (Math.Round(Math.PI / (4.0 * theta)))
    
    // ========================================================================
    // ADVANCED STATE PREPARATIONS - Examples
    // ========================================================================
    
    /// W-state preparation |W⟩ = (|100⟩ + |010⟩ + |001⟩)/√3
    /// 
    /// Example of non-uniform initial state
    let wStatePreparation (numQubits: int) : StatePreparation =
        fun (state: StateVector.StateVector) ->
            if numQubits <> 3 then
                failwith "W-state preparation only implemented for 3 qubits"
            
            let dimension = StateVector.dimension state
            let invSqrt3 = 1.0 / Math.Sqrt(3.0)
            
            // Create W-state: (|100⟩ + |010⟩ + |001⟩)/√3
            let amplitudes =
                [| 0 .. dimension - 1 |]
                |> Array.map (fun i ->
                    match i with
                    | 1 -> Complex(invSqrt3, 0.0)  // |001⟩
                    | 2 -> Complex(invSqrt3, 0.0)  // |010⟩
                    | 4 -> Complex(invSqrt3, 0.0)  // |100⟩
                    | _ -> Complex.Zero
                )
            
            StateVector.create amplitudes
    
    /// Partial uniform superposition over first k basis states
    /// 
    /// |ψ⟩ = (|0⟩ + |1⟩ + ... + |k-1⟩)/√k
    let partialUniformPreparation (numStates: int) (numQubits: int) : StatePreparation =
        fun (state: StateVector.StateVector) ->
            let dimension = StateVector.dimension state
            
            if numStates > dimension then
                failwith $"Cannot prepare uniform superposition over {numStates} states in {dimension}-dimensional space"
            
            let amplitude = Complex(1.0 / Math.Sqrt(float numStates), 0.0)
            
            let amplitudes =
                [| 0 .. dimension - 1 |]
                |> Array.map (fun i ->
                    if i < numStates then amplitude else Complex.Zero
                )
            
            StateVector.create amplitudes
    
    // ========================================================================
    // VERIFICATION - Compare with standard Grover
    // ========================================================================
    
    // NOTE: verifyGroverEquivalence() has been removed because:
    // - executeGroverViaAmplification() was deleted (legacy LocalSimulator dependency)
    // - AmplitudeAmplification now requires backend-based approach via AmplitudeAmplificationAdapter
    // - To verify equivalence, use GroverBackendAdapter and AmplitudeAmplificationAdapter with same backend


