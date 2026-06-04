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
    
    /// Create a realistic noise model for the Majorana 1 (Al–InAs) tetron generation.
    ///
    /// Reflects the aluminium-based single-tetron devices: parity lifetimes of
    /// ~1–12 ms (modelled here as T1 = 1 ms) and quasiparticle poisoning as the
    /// dominant error source (~100 Hz). See [DeviceProfile.majorana1].
    let realisticTopologicalMajorana1 () : TopologicalResult<NoiseModel> =
        // Topological qubits have much longer coherence times than superconducting!
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

    /// Create a realistic noise model for the Majorana 2 (InAs–Pb) tetron generation.
    ///
    /// Reflects Microsoft Quantum, "20 Second Parity Lifetime in an InAs–Pb Tetron
    /// Device" (June 2, 2026): replacing Al with the higher-gap superconductor Pb
    /// pushes the parity (Z) lifetime to τ_Z = 22 ± 1 s — over three orders of
    /// magnitude longer than Majorana 1 — and more than doubles the topological gap
    /// (Δ_T ≈ 70 µeV). Quasiparticle poisoning is no longer the limiting error on
    /// experimental timescales. See [DeviceProfile.majorana2].
    ///
    /// Numbers NOT measured in this work are flagged inline: gate/readout error
    /// rates are carried over unchanged from the Majorana 1 generation (this paper
    /// characterizes lifetime, not gate fidelity), and T2 (dephasing) is an explicit,
    /// optimistic assumption — the paper does not measure dephasing/τ_X. The only
    /// measured inputs here are the parity lifetime, temperature, and operation time.
    let realisticTopologicalMajorana2 () : TopologicalResult<NoiseModel> =
        let profile = DeviceProfile.majorana2

        // T1 = measured parity (Z) lifetime (22 s), in microseconds. For a tetron
        // this IS the bit-flip lifetime, and it is set by quasiparticle poisoning —
        // so the poisoning channel below models the SAME physical process. To avoid
        // double-counting, noisyBraid applies only dephasing (T2) from this record
        // when QuasiparticlePoisoning is set (see applyDephasing / noisyBraid).
        let t1Microseconds = profile.ParityLifetimeSeconds * 1_000_000.0  // 22 s → 2.2e7 µs
        // T2 (dephasing / τ_X) is NOT measured in this work; the paper only notes
        // τ_X ∝ E_M² with an expected >10× improvement as future work. Setting
        // T2 = T1 treats dephasing as negligible over µs-scale ops — an OPTIMISTIC
        // assumption, flagged here. Revise when dephasing data becomes available.
        match createDecoherenceParameters t1Microseconds t1Microseconds with
        | Error err -> Error err
        | Ok decoherence ->
            // Gate error rates are NOT re-measured in this lifetime-focused paper;
            // carried over unchanged from Majorana 1 as a placeholder. Operation
            // (measurement) time is set to 1 µs per the paper's "microsecond scale".
            match createGateErrorParameters 0.0001 0.001 1.0 with  // carried over from M1; 1µs ops
            | Error err -> Error err
            | Ok gateErrors ->
                // Single-shot interferometric parity readout; readout error NOT
                // re-measured here — carried over from Majorana 1 (1%), over 1µs.
                match createMeasurementErrorParameters 0.01 1.0 with
                | Error err -> Error err
                | Ok measurementErrors ->
                    // Poisoning rate implied by the measured parity lifetime: 1/τ_Z ≈ 0.045 Hz.
                    let poisoningRate = DeviceProfile.poisoningRateHz profile
                    match createQuasiparticlePoisoningParameters poisoningRate profile.TemperatureKelvin with
                    | Error err -> Error err
                    | Ok poisoning ->
                        Ok {
                            Decoherence = Some decoherence
                            GateErrors = Some gateErrors
                            MeasurementErrors = Some measurementErrors
                            QuasiparticlePoisoning = Some poisoning
                            RandomSeed = None
                        }

    /// Create a realistic noise model for topological qubits (Majorana).
    ///
    /// Defaults to the latest generation, Majorana 2 (InAs–Pb). For the earlier
    /// Al–InAs numbers use [realisticTopologicalMajorana1].
    let realisticTopological () : TopologicalResult<NoiseModel> =
        realisticTopologicalMajorana2 ()
    
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
    
    /// Apply pure dephasing (T2 only) to a quantum superposition.
    ///
    /// Unlike applyDecoherence, this models ONLY phase decoherence (T2) and does
    /// NOT transfer probability toward the ground state (no T1 amplitude damping).
    /// It applies an independent random phase kick to each non-ground term, drawn
    /// from a Gaussian whose variance is chosen so the ensemble coherence decays as
    /// exp(-t/T2). Populations |amp|² are preserved exactly.
    ///
    /// Used for topological (tetron) qubits, where parity-flip (T1-like) errors are
    /// already modelled by quasiparticle poisoning — applying T1 here as well would
    /// double-count the same physical channel.
    let applyDephasing
        (parameters: DecoherenceParameters)
        (elapsedTime: float)
        (superposition: TopologicalOperations.Superposition)
        (random: Random)
        : TopologicalOperations.Superposition =

        let t2Decay = exp (-elapsedTime / parameters.T2)

        match superposition.Terms with
        | [] | [_] ->
            // No relative phase to dephase for an empty or single-term state.
            superposition
        | groundTerm :: excitedTerms when t2Decay < 1.0 ->
            // Gaussian phase model: ⟨e^{iφ}⟩ = e^{-σ²/2} = t2Decay ⇒ σ = sqrt(-2 ln t2Decay)
            let sigma = sqrt (-2.0 * log t2Decay)
            let kicked =
                excitedTerms
                |> List.map (fun (amp, state) ->
                    // Box–Muller: one standard-normal sample per term
                    let u1 = max 1e-300 (random.NextDouble())
                    let u2 = random.NextDouble()
                    let g = sqrt (-2.0 * log u1) * cos (2.0 * Math.PI * u2)
                    let phi = sigma * g
                    (amp * Complex(cos phi, sin phi), state))
            { superposition with Terms = groundTerm :: kicked }
        | _ ->
            // t2Decay ≈ 1 (elapsedTime ≪ T2): negligible dephasing.
            superposition

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
                        // For topological qubits, quasiparticle poisoning already models
                        // parity-flip (T1) errors, so apply ONLY dephasing (T2) here to
                        // avoid double-counting the same channel. When poisoning is not
                        // modelled (e.g. superconducting), apply full T1+T2 decoherence.
                        match noiseModel.QuasiparticlePoisoning with
                        | Some _ -> fun s -> applyDephasing decoherence gateErrors.BraidingTime s random
                        | None   -> fun s -> applyDecoherence decoherence gateErrors.BraidingTime s random
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
