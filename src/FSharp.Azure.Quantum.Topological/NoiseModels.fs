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
        
        /// Temperature in Kelvin (displayed as milli-Kelvin in diagnostics)
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
    
    /// Apply decoherence to a quantum superposition over time
    ///
    /// Models:
    /// 1. Amplitude damping (T1): Excited states decay toward ground state
    /// 2. Phase damping (T2): Off-diagonal coherences decay exponentially
    ///
    /// The first term in the superposition is treated as the ground state.
    /// T1 relaxation transfers amplitude from excited states to ground.
    /// T2 dephasing multiplies each amplitude by exp(-t/T2), reducing coherence.
    let applyDecoherence 
        (parameters: DecoherenceParameters) 
        (elapsedTime: float) 
        (superposition: TopologicalOperations.Superposition) 
        (_random: Random) 
        : TopologicalOperations.Superposition =
        
        let t1Decay = exp (-elapsedTime / parameters.T1)
        let t2Decay = exp (-elapsedTime / parameters.T2)
        
        match superposition.Terms with
        | [] -> superposition
        | [(amp, state)] ->
            // Single basis state: T2 dephasing has no observable effect on a pure
            // computational basis state (global phase is unobservable).
            // T1 would only matter if this is an excited state, but with a single term
            // there is no ground state to decay into, so return unchanged.
            superposition
        | (groundAmp, groundState) :: excitedTerms ->
            // T1: Each excited amplitude is damped by sqrt(t1Decay),
            //     and the lost probability transfers to the ground state.
            // T2: Each amplitude is damped by t2Decay (coherence loss).
            let dampedExcited =
                excitedTerms
                |> List.map (fun (amp, state) ->
                    let t1Factor = sqrt t1Decay       // amplitude damping
                    let t2Factor = t2Decay             // phase damping
                    (amp * Complex(t1Factor * t2Factor, 0.0), state))
            
            // Probability transferred from excited states to ground via T1
            let excitedProbBefore =
                excitedTerms |> List.sumBy (fun (amp, _) -> (Complex.Abs amp) ** 2.0)
            let excitedProbAfter =
                dampedExcited |> List.sumBy (fun (amp, _) -> (Complex.Abs amp) ** 2.0)
            let transferredProb = excitedProbBefore - excitedProbAfter
            
            // Ground state amplitude grows from transferred probability
            let groundProbNew = (Complex.Abs groundAmp) ** 2.0 + max 0.0 transferredProb
            let groundPhase = if Complex.Abs groundAmp > 1e-15 then groundAmp / Complex(Complex.Abs groundAmp, 0.0) else Complex.One
            let newGroundAmp = groundPhase * Complex(sqrt groundProbNew, 0.0)
            
            { superposition with Terms = (newGroundAmp, groundState) :: dampedExcited }
            |> TopologicalOperations.normalize
    
    /// Apply depolarizing noise to a quantum superposition
    ///
    /// Depolarizing channel: ρ → (1-p)ρ + p(I/d)
    /// With probability p, replace state with maximally mixed superposition
    /// over the existing basis states (uniform amplitudes).
    let applyDepolarizingNoise 
        (errorRate: float) 
        (superposition: TopologicalOperations.Superposition) 
        (random: Random) 
        : TopologicalOperations.Superposition =
        
        if random.NextDouble() < errorRate then
            // Error occurred — replace with uniform superposition over the
            // same basis states (maximally mixed in this subspace)
            match superposition.Terms with
            | [] -> superposition
            | terms ->
                let d = float terms.Length
                let uniformAmp = Complex(1.0 / sqrt d, 0.0)
                { superposition with
                    Terms = terms |> List.map (fun (_, state) -> (uniformAmp, state)) }
        else
            // No error — state unchanged
            superposition
    
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
        (superposition: TopologicalOperations.Superposition) 
        (random: Random) 
        : TopologicalOperations.Superposition =
        
        // Functional pipeline (idiomatic F#) - no mutable state!
        superposition
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
                        // Poisoning event — replace with uniform superposition
                        // (quasiparticle corrupts the encoded state)
                        match s.Terms with
                        | [] -> s
                        | terms ->
                            let d = float terms.Length
                            let uniformAmp = Complex(1.0 / sqrt d, 0.0)
                            { s with Terms = terms |> List.map (fun (_, state) -> (uniformAmp, state)) }
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
    
    /// Calculate effective error rate combining all noise sources.
    /// When decoherence or gate error models are not configured (None),
    /// a 0.0 error rate is used for that component, representing the ideal case
    /// where no noise model has been specified (not a claim of zero physical error).
    let effectiveErrorRate (model: NoiseModel) (operationTime: float) : float =
        // Functional error accumulation (idiomatic F#) - no mutable!
        let decoherenceError =
            match model.Decoherence with
            | Some d ->
                let t1Error = 1.0 - exp (-operationTime / d.T1)
                let t2Error = 1.0 - exp (-operationTime / d.T2)
                max t1Error t2Error
            | None -> 0.0  // No decoherence model configured; ideal case
        
        let gateError =
            match model.GateErrors with
            | Some g -> g.TwoQubitErrorRate
            | None -> 0.0  // No gate error model configured; ideal case
        
        // Combine errors (approximate)
        min 1.0 (decoherenceError + gateError)
