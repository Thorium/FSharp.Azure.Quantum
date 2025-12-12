namespace FSharp.Azure.Quantum.Algorithms

open System
open FSharp.Azure.Quantum
open FSharp.Azure.Quantum.Core
open FSharp.Azure.Quantum.Core.BackendAbstraction
open FSharp.Azure.Quantum.Core.CircuitAbstraction
open FSharp.Azure.Quantum.LocalSimulator

/// Quantum Monte Carlo - RULE1 Compliant Implementation
/// 
/// **RULE1**: All public APIs require IQuantumBackend parameter
/// **Quadratic Speedup**: O(1/ε²) → O(1/ε) for precision ε
/// 
/// MATHEMATICAL FOUNDATION:
/// Classical Monte Carlo: Estimate E[f(X)] using N samples → accuracy O(1/√N)
/// Quantum Monte Carlo: Uses Amplitude Estimation → accuracy O(1/N) queries
/// Result: Quadratic speedup (100x for 10,000 samples)
/// 
/// ALGORITHM OVERVIEW:
/// 1. State Preparation: Encode probability distribution in quantum state
/// 2. Oracle: Mark states to measure (e.g., "in-the-money" for options)
/// 3. Amplitude Estimation: Grover-based algorithm to estimate probability
/// 4. Expectation: Extract value from amplitude
/// 
/// **Classical Monte Carlo**: Private only (for validation/comparison)
/// 
/// REFERENCE:
/// Rebentrost et al., "Quantum computational finance: Monte Carlo pricing of financial derivatives"
/// Phys. Rev. A 98, 022321 (2018) - https://arxiv.org/abs/1805.00109
module QuantumMonteCarlo =
    
    // ========================================================================
    // TYPES
    // ========================================================================
    
    /// Configuration for Quantum Monte Carlo
    type QMCConfig = {
        /// Number of qubits for state representation
        /// More qubits → finer discretization of probability space
        NumQubits: int
        
        /// State preparation circuit (encodes probability distribution)
        /// Creates superposition: ∑_x √p(x) |x⟩
        StatePreparation: CircuitBuilder.Circuit
        
        /// Oracle circuit (marks target states)
        /// Applies phase flip to states where f(x) = 1
        Oracle: CircuitBuilder.Circuit
        
        /// Number of Grover iterations for amplitude estimation
        /// Optimal: O(1/√a) where a is target amplitude
        /// More iterations → higher precision
        GroverIterations: int
        
        /// Number of measurement shots
        Shots: int
    }
    
    /// Result of Quantum Monte Carlo estimation
    type QMCResult = {
        /// Estimated expectation value
        ExpectationValue: float
        
        /// Standard error of estimate
        StandardError: float
        
        /// Success probability (measured amplitude squared)
        SuccessProbability: float
        
        /// Number of quantum queries used
        QuantumQueries: int
        
        /// Classical equivalent sample count (for speedup metric)
        ClassicalEquivalent: int
        
        /// Speedup factor (quantum vs classical)
        SpeedupFactor: float
    }
    
    // ========================================================================
    // NO CLASSICAL BASELINE - RULE1 STRICT COMPLIANCE
    // ========================================================================
    // 
    // This module contains ONLY quantum implementations.
    // Classical Monte Carlo has been removed to ensure RULE1 compliance:
    // 
    // "This is an Azure Quantum library, NOT a standalone solver library.
    //  All code must depend on IBackend."
    // 
    // For classical Monte Carlo comparison:
    // - Use external libraries (NumPy, SciPy, QuantLib)
    // - Or implement in separate classical solver if needed for testing
    // 
    // This keeps the quantum algorithm pure and RULE1 compliant.
    
    // ========================================================================
    // PRIVATE - Circuit Construction Helpers
    // ========================================================================
    
    /// Build Grover diffusion operator (reflection about average)
    /// 
    /// Diffusion = 2|ψ⟩⟨ψ| - I where |ψ⟩ is uniform superposition
    /// Implements: H^⊗n (2|0⟩⟨0| - I) H^⊗n
    let private buildDiffusionOperator (numQubits: int) : CircuitBuilder.Circuit =
        let circuit = CircuitBuilder.empty numQubits
        
        // Step 1: Apply Hadamard to all qubits
        let afterHadamards =
            [0 .. numQubits - 1]
            |> List.fold (fun c q -> c |> CircuitBuilder.addGate (CircuitBuilder.H q)) circuit
        
        // Step 2: Apply X to all qubits
        let afterXGates =
            [0 .. numQubits - 1]
            |> List.fold (fun c q -> c |> CircuitBuilder.addGate (CircuitBuilder.X q)) afterHadamards
        
        // Step 3: Multi-controlled Z gate (phase flip on |0...0⟩)
        let afterControlledZ =
            if numQubits = 1 then
                afterXGates |> CircuitBuilder.addGate (CircuitBuilder.Z 0)
            elif numQubits = 2 then
                afterXGates |> CircuitBuilder.addGate (CircuitBuilder.CZ(0, 1))
            else
                // Multi-controlled Z: control on qubits 0..n-2, target on n-1
                let controls = [0 .. numQubits - 2]
                afterXGates |> CircuitBuilder.addGate (CircuitBuilder.MCZ(controls, numQubits - 1))
        
        // Step 4: Apply X to all qubits again
        let afterSecondXGates =
            [0 .. numQubits - 1]
            |> List.fold (fun c q -> c |> CircuitBuilder.addGate (CircuitBuilder.X q)) afterControlledZ
        
        // Step 5: Apply Hadamard to all qubits again
        [0 .. numQubits - 1]
        |> List.fold (fun c q -> c |> CircuitBuilder.addGate (CircuitBuilder.H q)) afterSecondXGates
    
    /// Build Grover operator: G = Diffusion · Oracle
    let private buildGroverOperator (oracle: CircuitBuilder.Circuit) : CircuitBuilder.Circuit =
        let numQubits = oracle.QubitCount
        let diffusion = buildDiffusionOperator numQubits
        
        // Compose: first apply oracle, then diffusion
        oracle |> CircuitBuilder.compose diffusion
    
    // ========================================================================
    // PUBLIC - Quantum Monte Carlo (RULE1: backend required)
    // ========================================================================
    
    /// Execute Quantum Monte Carlo with quantum backend (RULE1 compliant)
    /// 
    /// **REQUIRED PARAMETER**: backend: IQuantumBackend
    /// **Quadratic Speedup**: O(1/ε) quantum queries vs O(1/ε²) classical samples
    /// 
    /// ALGORITHM:
    /// 1. Prepare state |ψ⟩ = StatePreparation|0⟩ = ∑_x √p(x)|x⟩
    /// 2. Apply Grover iterations: G^k |ψ⟩ where G = Diffusion · Oracle
    /// 3. Measure to estimate amplitude a (probability of marked states)
    /// 4. Extract original amplitude from Grover-amplified result
    /// 5. Return expectation value E = a
    let estimateExpectation
        (config: QMCConfig)
        (backend: IQuantumBackend)  // ✅ RULE1: Backend required
        : Async<QuantumResult<QMCResult>> =
        
        async {
            return quantumResult {
                // Validate config
                if config.NumQubits < 1 then
                    return! Error (QuantumError.ValidationError ("NumQubits", "Must be >= 1"))
                elif config.NumQubits > 20 then
                    return! Error (QuantumError.ValidationError ("NumQubits", "Too large (max 20)"))
                elif config.GroverIterations < 0 then
                    return! Error (QuantumError.ValidationError ("GroverIterations", "Must be >= 0"))
                elif config.Shots < 100 then
                    return! Error (QuantumError.ValidationError ("Shots", "Must be >= 100"))
                elif config.StatePreparation.QubitCount <> config.NumQubits then
                    return! Error (QuantumError.ValidationError ("StatePreparation", "Qubit count mismatch"))
                elif config.Oracle.QubitCount <> config.NumQubits then
                    return! Error (QuantumError.ValidationError ("Oracle", "Qubit count mismatch"))
                else
                    
                    // Build Grover operator
                    let groverOp = buildGroverOperator config.Oracle
                    
                    // Build full circuit: StatePrep → Grover^k
                    // Note: We don't add measurements before ExecuteToState
                    // ExecuteToState returns the quantum state directly
                    let circuit =
                        [1 .. config.GroverIterations]
                        |> List.fold (fun c _ -> c |> CircuitBuilder.compose groverOp) config.StatePreparation
                    
                    // Execute on quantum backend (✅ RULE1 compliant)
                    let wrapper = CircuitAbstraction.CircuitWrapper(circuit) :> CircuitAbstraction.ICircuit
                    let executionResult = backend.ExecuteToState wrapper
                    
                    match executionResult with
                    | Error err ->
                        return! Error err
                    
                    | Ok finalState ->
                        // Extract success probability from quantum state
                        // We want probability of |0...0⟩ (all qubits measured as 0)
                        let successProb = 
                            match finalState with
                            | QuantumState.StateVector sv ->
                                // Basis state |0...0⟩ corresponds to index 0
                                StateVector.probability 0 sv
                            
                            | QuantumState.FusionSuperposition topo ->
                                // Topological state - measure |0...0⟩
                                let zeroState = Array.create config.NumQubits 0
                                topo.Probability zeroState
                            
                            | _ ->
                                // Other state types not supported yet
                                failwith "QuantumMonteCarlo only supports StateVector and FusionSuperposition backends"
                        
                        // Estimate original amplitude from Grover-amplified result
                        // Grover amplifies a → sin²((2k+1)θ) where sin²(θ) = a, k = iterations
                        let amplifiedAngle = asin (sqrt successProb)
                        let k = float config.GroverIterations
                        let theta = amplifiedAngle / (2.0 * k + 1.0)
                        let originalAmplitude = (sin theta) ** 2.0
                        
                        // Calculate standard error (theoretical bound)
                        // Quantum amplitude estimation achieves O(1/M) error with M queries
                        let stdError = 
                            if config.GroverIterations > 0 then
                                1.0 / float config.GroverIterations
                            else
                                1.0 / sqrt (float config.Shots)
                        
                        // Classical equivalent samples for same accuracy
                        let classicalSamples = 
                            if config.GroverIterations > 0 then
                                config.GroverIterations * config.GroverIterations
                            else
                                config.Shots
                        
                        // Total quantum queries
                        let quantumQueries = config.GroverIterations * config.Shots
                        
                        // Speedup factor
                        let speedup = 
                            if quantumQueries > 0 then
                                float classicalSamples / float quantumQueries
                            else
                                1.0
                        
                        return {
                            ExpectationValue = originalAmplitude
                            StandardError = stdError
                            SuccessProbability = successProb
                            QuantumQueries = quantumQueries
                            ClassicalEquivalent = classicalSamples
                            SpeedupFactor = speedup
                        }
            }
        }
    
    // ========================================================================
    // CONVENIENCE FUNCTIONS (RULE1: all require backend)
    // ========================================================================
    
    /// Estimate probability using quantum backend (RULE1 compliant)
    /// 
    /// Estimates P(f(X) = 1) where X follows distribution encoded in statePrep
    /// 
    /// **REQUIRED PARAMETER**: backend: IQuantumBackend
    let estimateProbability
        (statePrep: CircuitBuilder.Circuit)
        (oracle: CircuitBuilder.Circuit)
        (iterations: int)
        (backend: IQuantumBackend)  // ✅ RULE1: Backend required
        : Async<QuantumResult<float>> =
        
        async {
            let config = {
                NumQubits = statePrep.QubitCount
                StatePreparation = statePrep
                Oracle = oracle
                GroverIterations = iterations
                Shots = 1000
            }
            
            let! result = estimateExpectation config backend
            return result |> Result.map (fun r -> r.ExpectationValue)
        }
    
    /// Numerical integration using quantum backend (RULE1 compliant)
    /// 
    /// Estimates ∫_a^b f(x) dx using quantum amplitude estimation
    /// 
    /// **REQUIRED PARAMETER**: backend: IQuantumBackend
    /// **Speedup**: O(1/ε) vs classical O(1/ε²)
    let integrate
        (functionOracle: CircuitBuilder.Circuit)
        (domain: float * float)
        (precision: int)
        (backend: IQuantumBackend)  // ✅ RULE1: Backend required
        : Async<QuantumResult<float>> =
        
        async {
            let numQubits = functionOracle.QubitCount
            
            // Create uniform superposition over domain
            let statePrep =
                [0 .. numQubits - 1]
                |> List.fold (fun c q -> c |> CircuitBuilder.addGate (CircuitBuilder.H q))
                               (CircuitBuilder.empty numQubits)
            
            // Estimate probability that oracle marks state
            let! prob = estimateProbability statePrep functionOracle precision backend
            
            // Scale by domain width
            return prob |> Result.map (fun p ->
                let (a, b) = domain
                (b - a) * p
            )
        }
