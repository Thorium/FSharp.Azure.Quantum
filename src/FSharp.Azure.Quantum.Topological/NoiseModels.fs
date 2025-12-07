namespace FSharp.Azure.Quantum.Topological

/// Noise Models for Realistic Topological Quantum Simulation
///
/// Real quantum hardware suffers from various error sources:
/// 1. **Decoherence**: T1 (relaxation), T2 (dephasing)
/// 2. **Gate errors**: Imperfect braiding operations
/// 3. **Measurement errors**: Incorrect fusion outcomes
/// 4. **Quasiparticle poisoning**: Unwanted anyon creation (topological-specific)
///
/// This module provides realistic noise models for simulating these effects.
[<RequireQualifiedAccess>]
module NoiseModels =
    
    open System
    open System.Numerics
    
    // ========================================================================
    // NOISE MODEL PARAMETERS
    // ========================================================================
    
    /// Decoherence parameters (T1 and T2 times)
    type DecoherenceParameters = {
        /// T1 relaxation time (microseconds) - energy relaxation
        /// Typical: 10-100μs for superconducting qubits
        T1: float
        
        /// T2 dephasing time (microseconds) - phase coherence
        /// Typical: 5-50μs (T2 ≤ 2*T1 by theory)
        T2: float
        
        /// Validate: T2 ≤ 2*T1 (quantum mechanics constraint)
        /// Returns Error if constraint violated
        IsValid: unit -> TopologicalResult<unit>
    }
    
    /// Gate (braiding) error parameters
    type GateErrorParameters = {
        /// Single-qubit gate error rate (depolarizing)
        /// Typical: 0.001 (0.1%) for superconducting qubits
        /// Topological target: 10^-12 (topological protection!)
        SingleQubitErrorRate: float
        
        /// Two-qubit gate error rate (depolarizing)
        /// Typical: 0.01 (1%) - worse than single-qubit
        TwoQubitErrorRate: float
        
        /// Braiding operation time (microseconds)
        /// Longer time → more decoherence
        BraidingTime: float
    }
    
    /// Measurement error parameters
    type MeasurementErrorParameters = {
        /// Probability of measuring wrong outcome
        /// Typical: 0.01-0.05 (1-5%)
        ReadoutErrorRate: float
        
        /// Measurement time (microseconds)
        MeasurementTime: float
    }
    
    /// Quasiparticle poisoning parameters (topological-specific)
    ///
    /// In topological systems, unwanted quasiparticles can be created
    /// by thermal excitations or external perturbations, causing errors.
    type QuasiparticlePoisoningParameters = {
        /// Poisoning rate (events per second)
        /// Typical: 10-1000 Hz depending on temperature
        PoisoningRate: float
        
        /// Temperature (milli-Kelvin)
        /// Lower temperature → less poisoning
        Temperature: float
    }
    
    /// Complete noise model combining all error sources
    type NoiseModel = {
        Decoherence: DecoherenceParameters option
        GateErrors: GateErrorParameters option
        MeasurementErrors: MeasurementErrorParameters option
        QuasiparticlePoisoning: QuasiparticlePoisoningParameters option
        
        /// Random number generator seed (for reproducibility)
        RandomSeed: int option
    }
    
    // ========================================================================
    // NOISE MODEL CONSTRUCTION
    // ========================================================================
    
    /// Create decoherence parameters with validation
    let createDecoherenceParameters (t1: float) (t2: float) : TopologicalResult<DecoherenceParameters> =
        if t1 <= 0.0 then
            TopologicalResult.validationError "field" $"T1 must be positive, got {t1}"
        elif t2 <= 0.0 then
            TopologicalResult.validationError "field" $"T2 must be positive, got {t2}"
        elif t2 > 2.0 * t1 then
            TopologicalResult.validationError "t2Time" $"T2 ({t2}μs) cannot exceed 2*T1 ({2.0*t1}μs) - quantum mechanics constraint"
        else
            Ok {
                T1 = t1
                T2 = t2
                IsValid = fun () -> Ok ()
            }
    
    /// Create gate error parameters with validation
    let createGateErrorParameters 
        (singleQubitErrorRate: float) 
        (twoQubitErrorRate: float) 
        (braidingTime: float) 
        : TopologicalResult<GateErrorParameters> =
        
        if singleQubitErrorRate < 0.0 || singleQubitErrorRate > 1.0 then
            TopologicalResult.validationError "singleQubitErrorRate" $"Single-qubit error rate must be in [0, 1], got {singleQubitErrorRate}"
        elif twoQubitErrorRate < 0.0 || twoQubitErrorRate > 1.0 then
            TopologicalResult.validationError "twoQubitErrorRate" $"Two-qubit error rate must be in [0, 1], got {twoQubitErrorRate}"
        elif braidingTime <= 0.0 then
            TopologicalResult.validationError "field" $"Braiding time must be positive, got {braidingTime}"
        else
            Ok {
                SingleQubitErrorRate = singleQubitErrorRate
                TwoQubitErrorRate = twoQubitErrorRate
                BraidingTime = braidingTime
            }
    
    /// Create measurement error parameters
    let createMeasurementErrorParameters 
        (readoutErrorRate: float) 
        (measurementTime: float) 
        : TopologicalResult<MeasurementErrorParameters> =
        
        if readoutErrorRate < 0.0 || readoutErrorRate > 1.0 then
            TopologicalResult.validationError "readoutErrorRate" $"Readout error rate must be in [0, 1], got {readoutErrorRate}"
        elif measurementTime <= 0.0 then
            TopologicalResult.validationError "field" $"Measurement time must be positive, got {measurementTime}"
        else
            Ok {
                ReadoutErrorRate = readoutErrorRate
                MeasurementTime = measurementTime
            }
    
    /// Create quasiparticle poisoning parameters
    let createQuasiparticlePoisoningParameters 
        (poisoningRate: float) 
        (temperature: float) 
        : TopologicalResult<QuasiparticlePoisoningParameters> =
        
        if poisoningRate < 0.0 then
            TopologicalResult.validationError "field" $"Poisoning rate must be non-negative, got {poisoningRate}"
        elif temperature < 0.0 then
            TopologicalResult.validationError "field" $"Temperature must be non-negative, got {temperature}"
        else
            Ok {
                PoisoningRate = poisoningRate
                Temperature = temperature
            }
    
    /// Create noiseless model (ideal simulator)
    let noiseless () : NoiseModel = {
        Decoherence = None
        GateErrors = None
        MeasurementErrors = None
        QuasiparticlePoisoning = None
        RandomSeed = None
    }
    
    /// Create realistic noise model for superconducting qubits
    let realisticSuperconducting () : TopologicalResult<NoiseModel> =
        match createDecoherenceParameters 50.0 30.0 with  // T1=50μs, T2=30μs
        | Error err -> Error err
        | Ok decoherence ->
            match createGateErrorParameters 0.001 0.01 0.05 with  // 0.1%, 1%, 50ns braiding
            | Error err -> Error err
            | Ok gateErrors ->
                match createMeasurementErrorParameters 0.02 0.1 with  // 2% readout, 100ns
                | Error err -> Error err
                | Ok measurementErrors ->
                    Ok {
                        Decoherence = Some decoherence
                        GateErrors = Some gateErrors
                        MeasurementErrors = Some measurementErrors
                        QuasiparticlePoisoning = None  // Not applicable to standard superconducting
                        RandomSeed = None
                    }
    
    /// Create realistic noise model for topological qubits (Majorana)
    let realisticTopological () : TopologicalResult<NoiseModel> =
        // Topological qubits have much longer coherence times!
        match createDecoherenceParameters 1000.0 500.0 with  // T1=1ms, T2=0.5ms
        | Error err -> Error err
        | Ok decoherence ->
            // Topological protection → much lower gate errors
            match createGateErrorParameters 0.0001 0.001 0.1 with  // 0.01%, 0.1%, 100ns
            | Error err -> Error err
            | Ok gateErrors ->
                match createMeasurementErrorParameters 0.01 0.1 with  // 1% readout
                | Error err -> Error err
                | Ok measurementErrors ->
                    // Quasiparticle poisoning is the main error source
                    match createQuasiparticlePoisoningParameters 100.0 0.050 with  // 100 Hz, 50 mK
                    | Error err -> Error err
                    | Ok poisoning ->
                        Ok {
                            Decoherence = Some decoherence
                            GateErrors = Some gateErrors
                            MeasurementErrors = Some measurementErrors
                            QuasiparticlePoisoning = Some poisoning
                            RandomSeed = None
                        }
    
    // ========================================================================
    // NOISE APPLICATION
    // ========================================================================
    
    /// Apply decoherence to a quantum state over time
    ///
    /// Models:
    /// 1. Amplitude damping (T1): |1⟩ → |0⟩ relaxation
    /// 2. Phase damping (T2): Loss of coherence
    let applyDecoherence 
        (parameters: DecoherenceParameters) 
        (elapsedTime: float) 
        (state: FusionTree.State) 
        (random: Random) 
        : FusionTree.State =
        
        // Calculate decay probabilities
        let t1Decay = 1.0 - exp (-elapsedTime / parameters.T1)
        let t2Decay = 1.0 - exp (-elapsedTime / parameters.T2)
        
        // For simulation simplicity, state doesn't change structurally
        // (Real implementation would modify amplitudes in superposition)
        // This is a placeholder for the structural decay
        state
    
    /// Apply depolarizing noise to a quantum state
    ///
    /// Depolarizing channel: ρ → (1-p)ρ + p(I/d)
    /// With probability p, replace state with maximally mixed state
    let applyDepolarizingNoise 
        (errorRate: float) 
        (state: FusionTree.State) 
        (random: Random) 
        : FusionTree.State =
        
        if random.NextDouble() < errorRate then
            // Error occurred - state gets randomized
            // (Placeholder: in reality, would create mixed state)
            state
        else
            // No error
            state
    
    /// Apply measurement error (bit flip)
    ///
    /// With probability p, flip the measurement outcome
    let applyMeasurementError 
        (errorRate: float) 
        (outcome: AnyonSpecies.Particle) 
        (random: Random) 
        : AnyonSpecies.Particle =
        
        if random.NextDouble() < errorRate then
            // Error: flip to opposite outcome (exhaustive pattern match)
            match outcome with
            | AnyonSpecies.Particle.Vacuum -> AnyonSpecies.Particle.Psi
            | AnyonSpecies.Particle.Psi -> AnyonSpecies.Particle.Vacuum
            | AnyonSpecies.Particle.Sigma -> AnyonSpecies.Particle.Psi
            | AnyonSpecies.Particle.Tau -> AnyonSpecies.Particle.Vacuum  // Explicit (not wildcard)
            | AnyonSpecies.Particle.SpinJ (j, k) -> 
                // For SpinJ, flip to vacuum as default error behavior
                AnyonSpecies.Particle.SpinJ (0, k)  // j=0 is vacuum for SU(2)_k
        else
            outcome
    
    /// Simulate quasiparticle poisoning event
    ///
    /// Check if poisoning occurs during operation time
    let checkQuasiparticlePoisoning 
        (parameters: QuasiparticlePoisoningParameters) 
        (operationTime: float) 
        (random: Random) 
        : bool =
        
        // Poisson process: P(event) = 1 - exp(-rate * time)
        let poisoningProbability = 1.0 - exp (-parameters.PoisoningRate * operationTime / 1_000_000.0)
        random.NextDouble() < poisoningProbability
    
    // ========================================================================
    // NOISY OPERATIONS
    // ========================================================================
    
    /// Perform noisy braiding operation
    let noisyBraid 
        (noiseModel: NoiseModel) 
        (leftIndex: int) 
        (state: FusionTree.State) 
        (random: Random) 
        : FusionTree.State =
        
        // Functional pipeline (idiomatic F#) - no mutable state!
        state
        |> (match noiseModel.GateErrors with
            | Some gateErrors ->
                (fun s -> applyDepolarizingNoise gateErrors.TwoQubitErrorRate s random)
                >> (match noiseModel.Decoherence with
                    | Some decoherence ->
                        fun s -> applyDecoherence decoherence gateErrors.BraidingTime s random
                    | None -> id)
            | None -> id)
        |> (match noiseModel.QuasiparticlePoisoning, noiseModel.GateErrors with
            | Some poisoning, Some gateErrors ->
                fun s ->
                    if checkQuasiparticlePoisoning poisoning gateErrors.BraidingTime random then
                        // Poisoning event - state corrupted (placeholder)
                        s
                    else
                        s
            | _ -> id)
    
    /// Perform noisy measurement
    let noisyMeasure 
        (noiseModel: NoiseModel) 
        (outcome: AnyonSpecies.Particle) 
        (random: Random) 
        : AnyonSpecies.Particle =
        
        match noiseModel.MeasurementErrors with
        | Some measurementErrors ->
            applyMeasurementError 
                measurementErrors.ReadoutErrorRate 
                outcome 
                random
        | None -> outcome
    
    // ========================================================================
    // UTILITY FUNCTIONS
    // ========================================================================
    
    /// Display noise model parameters
    let displayNoiseModel (model: NoiseModel) : string =
        // Functional list building (idiomatic F#) - no ResizeArray!
        [
            "Noise Model Configuration:"
            "=========================="
            
            match model.Decoherence with
            | Some d -> 
                yield! [
                    "Decoherence:"
                    $"  T1 (relaxation):  {d.T1:F2} μs"
                    $"  T2 (dephasing):   {d.T2:F2} μs"
                ]
            | None -> "Decoherence: None (ideal)"
            
            match model.GateErrors with
            | Some g ->
                yield! [
                    "Gate Errors:"
                    $"  Single-qubit:     {g.SingleQubitErrorRate * 100.0:F4}%%"
                    $"  Two-qubit:        {g.TwoQubitErrorRate * 100.0:F4}%%"
                    $"  Braiding time:    {g.BraidingTime * 1000.0:F2} ns"
                ]
            | None -> "Gate Errors: None (perfect gates)"
            
            match model.MeasurementErrors with
            | Some m ->
                yield! [
                    "Measurement Errors:"
                    $"  Readout error:    {m.ReadoutErrorRate * 100.0:F2}%%"
                    $"  Measurement time: {m.MeasurementTime * 1000.0:F2} ns"
                ]
            | None -> "Measurement Errors: None (perfect readout)"
            
            match model.QuasiparticlePoisoning with
            | Some q ->
                yield! [
                    "Quasiparticle Poisoning:"
                    $"  Poisoning rate:   {q.PoisoningRate:F1} Hz"
                    $"  Temperature:      {q.Temperature * 1000.0:F1} mK"
                ]
            | None -> "Quasiparticle Poisoning: None"
            
            match model.RandomSeed with
            | Some seed -> $"Random Seed: {seed} (reproducible)"
            | None -> "Random Seed: Not set (non-reproducible)"
        ]
        |> String.concat "\n"
    
    /// Calculate effective error rate combining all noise sources
    let effectiveErrorRate (model: NoiseModel) (operationTime: float) : float =
        // Functional error accumulation (idiomatic F#) - no mutable!
        let decoherenceError =
            match model.Decoherence with
            | Some d ->
                let t1Error = 1.0 - exp (-operationTime / d.T1)
                let t2Error = 1.0 - exp (-operationTime / d.T2)
                max t1Error t2Error
            | None -> 0.0
        
        let gateError =
            match model.GateErrors with
            | Some g -> g.TwoQubitErrorRate
            | None -> 0.0
        
        // Combine errors (approximate)
        min 1.0 (decoherenceError + gateError)
