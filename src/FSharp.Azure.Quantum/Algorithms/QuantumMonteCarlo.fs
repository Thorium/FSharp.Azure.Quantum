namespace FSharp.Azure.Quantum.Algorithms

open System
open System.Numerics
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
    
    /// Phase flip on |0...0⟩ : S₀ = I − 2|0⟩⟨0| (up to a global sign).
    /// X on every qubit maps |0...0⟩ → |1...1⟩, a multi-controlled Z flips that
    /// single basis state's phase, and the X layer is undone afterwards.
    let private buildZeroReflection (numQubits: int) : CircuitBuilder.Circuit =
        let circuit = CircuitBuilder.empty numQubits

        let afterXGates =
            [0 .. numQubits - 1]
            |> List.fold (fun c q -> c |> CircuitBuilder.addGate (CircuitBuilder.X q)) circuit

        let afterControlledZ =
            if numQubits = 1 then
                afterXGates |> CircuitBuilder.addGate (CircuitBuilder.Z 0)
            elif numQubits = 2 then
                afterXGates |> CircuitBuilder.addGate (CircuitBuilder.CZ(0, 1))
            else
                let controls = [0 .. numQubits - 2]
                afterXGates |> CircuitBuilder.addGate (CircuitBuilder.MCZ(controls, numQubits - 1))

        [0 .. numQubits - 1]
        |> List.fold (fun c q -> c |> CircuitBuilder.addGate (CircuitBuilder.X q)) afterControlledZ

    /// Reflection about the prepared state |ψ⟩ = A|0⟩ :  2|ψ⟩⟨ψ| − I = A · S₀ · A†
    /// (up to a global phase).
    ///
    /// This is the CORRECT diffusion for amplitude amplification of an arbitrary,
    /// possibly non-uniform state preparation A. The textbook H-based diffusion
    /// (H^⊗n S₀ H^⊗n) only reflects about the uniform superposition and is therefore
    /// only valid when A = H^⊗n — it gives wrong amplification for a Möttönen-encoded
    /// (non-uniform) distribution, which is exactly the case in quantum finance.
    let private buildStateReflection (statePrep: CircuitBuilder.Circuit) : CircuitBuilder.Circuit =
        let numQubits = statePrep.QubitCount
        let aDagger = CircuitBuilder.reverse statePrep
        let s0 = buildZeroReflection numQubits
        // Operator product A·S₀·A† ⇒ execute A† first, then S₀, then A.
        // CircuitBuilder.compose c1 c2 runs c1 then c2.
        CircuitBuilder.compose (CircuitBuilder.compose aDagger s0) statePrep

    /// Amplitude-amplification (Grover) operator  Q = (reflection about |ψ⟩) · (oracle).
    /// Applying Q^k to |ψ⟩ rotates the good-state amplitude to sin²((2k+1)θ) where
    /// sin²θ = a is the marked-subspace probability.
    let private buildGroverOperator (statePrep: CircuitBuilder.Circuit) (oracle: CircuitBuilder.Circuit) : CircuitBuilder.Circuit =
        let diffusion = buildStateReflection statePrep
        // Run the oracle first, then the reflection about |ψ⟩ (compose c1 c2 = c1 then c2).
        CircuitBuilder.compose oracle diffusion

    // ========================================================================
    // INTENT → PLAN → EXECUTE (ADR: Intent-First)
    // ========================================================================

    type private QmcIntent = { Config: QMCConfig }

    [<RequireQualifiedAccess>]
    type private QmcPlan =
        | ExecuteViaCircuit

    let private supportsCircuit (backend: IQuantumBackend) (circuit: CircuitBuilder.Circuit) : bool =
        circuit.Gates
        |> List.forall (fun gate -> backend.SupportsOperation (QuantumOperation.Gate gate))

    let private plan (backend: IQuantumBackend) (intent: QmcIntent) : Result<QmcPlan, QuantumError> =
        // Quantum Monte Carlo relies on gate operations; explicit refusal for annealing backends.
        match backend.NativeStateType with
        | QuantumStateType.Annealing ->
            Error (QuantumError.OperationError ("QuantumMonteCarlo", $"Backend '{backend.Name}' does not support quantum Monte Carlo (native state type: {backend.NativeStateType})"))
        | _ ->
            // Ensure all gates in the user-provided circuits are supported.
            if supportsCircuit backend intent.Config.StatePreparation && supportsCircuit backend intent.Config.Oracle then
                Ok QmcPlan.ExecuteViaCircuit
            else
                Error (QuantumError.OperationError ("QuantumMonteCarlo", $"Backend '{backend.Name}' does not support all required circuit operations"))

    /// Extract the dense amplitude vector from a state-vector (gate-based) result.
    let private tryGetAmplitudes (state: QuantumState) : Result<Complex[], QuantumError> =
        match state with
        | QuantumState.StateVector sv ->
            let dim = 1 <<< StateVector.numQubits sv
            Ok (Array.init dim (fun i -> StateVector.getAmplitude i sv))
        | _ -> Error (QuantumError.OperationError ("QuantumMonteCarlo", "Amplitude estimation requires a state-vector (gate-based) backend"))

    /// Execute a circuit on the backend and read back its amplitude vector.
    let private runAndReadAmplitudes (backend: IQuantumBackend) (circuit: CircuitBuilder.Circuit) : Result<Complex[], QuantumError> =
        let wrapper = CircuitAbstraction.CircuitWrapper(circuit) :> CircuitAbstraction.ICircuit
        backend.ExecuteToState wrapper |> Result.bind tryGetAmplitudes

    /// Identify the marked (good) basis states encoded by the phase oracle.
    /// Running the oracle on the uniform superposition flips the sign of exactly the
    /// marked amplitudes, so they are the indices whose real part becomes negative.
    let private determineMarkedSet (backend: IQuantumBackend) (oracle: CircuitBuilder.Circuit) (numQubits: int) : Result<Set<int>, QuantumError> =
        let uniform =
            [0 .. numQubits - 1]
            |> List.fold (fun c q -> c |> CircuitBuilder.addGate (CircuitBuilder.H q)) (CircuitBuilder.empty numQubits)
        let circuit = CircuitBuilder.compose uniform oracle  // H^⊗n first, then the oracle
        runAndReadAmplitudes backend circuit
        |> Result.map (fun amps ->
            amps
            |> Array.indexed
            |> Array.choose (fun (i, a) -> if a.Real < -1e-9 then Some i else None)
            |> Set.ofArray)

    /// Probability mass on the marked subspace for a given amplitude vector.
    let private markedProbability (markedSet: Set<int>) (amps: Complex[]) : float =
        markedSet
        |> Set.fold (fun acc i -> if i < amps.Length then acc + amps.[i].Magnitude ** 2.0 else acc) 0.0

    /// Grover-power schedule for Maximum-Likelihood Amplitude Estimation: exponentially
    /// increasing powers (0,1,2,4,...) capped at the configured budget. Power 0 is the
    /// bare prepared state, whose marked probability is a itself.
    let private mlaeSchedule (maxIterations: int) : int list =
        let rec build acc k =
            if k > maxIterations || List.length acc >= 7 then List.rev acc
            else build (k :: acc) (if k = 0 then 1 else k * 2)
        build [] 0

    /// Maximum-Likelihood Amplitude Estimation: recover θ (hence a = sin²θ) from the
    /// marked-state probabilities measured at several Grover powers, each obeying
    /// P_k(good) = sin²((2k+1)θ). A grid search over θ ∈ [0, π/2] maximises the
    /// shot-weighted Bernoulli log-likelihood, followed by a local refinement.
    let private estimateAmplitudeMLAE (shots: int) (measurements: (int * float) list) : float =
        let logLikelihood (theta: float) : float =
            measurements
            |> List.sumBy (fun (k, pGood) ->
                let angle = float (2 * k + 1) * theta
                let s = max 1e-12 ((sin angle) ** 2.0)
                let c = max 1e-12 ((cos angle) ** 2.0)
                float shots * (pGood * log s + (1.0 - pGood) * log c))

        let gridN = 2000
        let half = Math.PI / 2.0
        let coarse =
            [0 .. gridN]
            |> List.map (fun i -> let th = half * float i / float gridN in (th, logLikelihood th))
            |> List.maxBy snd
            |> fst
        let step = half / float gridN
        [ -50 .. 50 ]
        |> List.map (fun j -> coarse + float j * step / 50.0)
        |> List.filter (fun th -> th >= 0.0 && th <= half)
        |> List.map (fun th -> (th, logLikelihood th))
        |> List.maxBy snd
        |> fst
        |> fun thetaHat -> (sin thetaHat) ** 2.0

    /// Run amplitude estimation end to end: identify the marked subspace, measure the
    /// marked-state probability P_k(good) at each Grover power on the backend, and
    /// return the maximum-likelihood estimate of the marked amplitude a.
    let private runAmplitudeEstimation (backend: IQuantumBackend) (config: QMCConfig) : Result<float, QuantumError> =
        let groverOp = buildGroverOperator config.StatePreparation config.Oracle
        let buildAmplified (k: int) : CircuitBuilder.Circuit =
            // State prep first, then k applications of the Grover operator (compose c1 c2 = c1 then c2).
            [1 .. k] |> List.fold (fun c _ -> CircuitBuilder.compose c groverOp) config.StatePreparation
        determineMarkedSet backend config.Oracle config.NumQubits
        |> Result.bind (fun markedSet ->
            mlaeSchedule config.GroverIterations
            |> List.fold (fun accR k ->
                accR |> Result.bind (fun acc ->
                    runAndReadAmplitudes backend (buildAmplified k)
                    |> Result.map (fun amps -> (k, markedProbability markedSet amps) :: acc)))
                (Ok [])
            |> Result.map (fun measurements -> estimateAmplitudeMLAE config.Shots (List.rev measurements)))
    
    /// Measure the genuine bin probabilities q_i = |⟨i|ψ⟩|² produced by a state-preparation
    /// circuit on the given backend (basis index i ↔ bin i). Exposed so business modules
    /// (option pricing, risk) can derive expectations from the actual quantum distribution
    /// rather than from a classical array.
    let measureBinProbabilities (backend: IQuantumBackend) (statePrep: CircuitBuilder.Circuit) : Result<float[], QuantumError> =
        runAndReadAmplitudes backend statePrep
        |> Result.map (Array.map (fun (a: Complex) -> a.Magnitude * a.Magnitude))

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
                    
                    let intent = { Config = config }

                    // Validate backend support, then estimate the marked-subspace amplitude
                    // via Maximum-Likelihood Amplitude Estimation over a Grover-power schedule.
                    let! estimatedAmplitude =
                        match plan backend intent with
                        | Error err -> Error err
                        | Ok _ -> runAmplitudeEstimation backend config

                    // The estimated marked amplitude a = sin²θ IS the expectation E[1_good] = P(good).
                    let originalAmplitude = estimatedAmplitude
                    let successProb = estimatedAmplitude

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
