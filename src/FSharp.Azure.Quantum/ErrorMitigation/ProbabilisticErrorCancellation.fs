namespace FSharp.Azure.Quantum

/// Probabilistic Error Cancellation (PEC) error mitigation module.
/// 
/// Implements quasi-probability decomposition to achieve 2-3x accuracy improvement.
/// Uses importance sampling with negative probabilities to invert noise channels.
module ProbabilisticErrorCancellation =
    
    // ============================================================================
    // Types - Error Mitigation Domain (Quasi-Probability)
    // ============================================================================
    
    /// Noise model for depolarizing channels.
    /// 
    /// Characterizes error rates for different gate types.
    /// Typical values: single-qubit ~0.001, two-qubit ~0.01, readout ~0.02
    type NoiseModel = {
        /// Error rate per single-qubit gate (depolarizing probability p)
        SingleQubitDepolarizing: float
        
        /// Error rate per two-qubit gate (depolarizing probability p)
        TwoQubitDepolarizing: float
        
        /// Measurement error rate (readout fidelity)
        ReadoutError: float
    }
    
    /// Quasi-probability decomposition of a noisy gate.
    /// 
    /// Key insight: Noisy_Gate = Σᵢ pᵢ × Clean_Gate_i
    /// where some pᵢ < 0 (quasi-probability, not true probability!)
    type QuasiProbDecomposition = {
        /// List of (gate-sequence, quasi_probability) pairs. Each term is the sequence of
        /// gates run IN PLACE OF the ideal gate: the original (noisy) gate followed by a
        /// Pauli correction. A list (rather than a single gate) is required so a term can
        /// express a Pauli tensor product on a two-qubit gate — e.g. X⊗X = [X control; X target]
        /// — and so the identity correction is simply the original gate on its own.
        /// Note: Some probabilities can be NEGATIVE!
        Terms: (CircuitBuilder.Gate list * float) list

        /// Normalization factor = Σ|pᵢ| (sum of absolute values)
        /// Used for importance sampling from quasi-probability distribution
        Normalization: float
    }
    
    /// Configuration for Probabilistic Error Cancellation.
    type PECConfig = {
        /// Noise model for the quantum backend
        NoiseModel: NoiseModel
        
        /// Number of Monte Carlo samples (10-100x overhead)
        /// More samples = lower variance but higher cost
        Samples: int
        
        /// Random seed for reproducibility
        Seed: int option
    }
    
    /// Result of PEC error mitigation.
    type PECResult = {
        /// Corrected expectation value (after PEC)
        CorrectedExpectation: float
        
        /// Uncorrected expectation value (before PEC, noisy)
        UncorrectedExpectation: float
        
        /// Error reduction percentage (0-1 scale)
        ErrorReduction: float
        
        /// Number of samples used in Monte Carlo
        SamplesUsed: int
        
        /// Actual overhead ratio (circuit executions / baseline)
        Overhead: float
    }
    
    // ============================================================================
    // Quasi-Probability Decomposition - Inverting Noise Channels
    // ============================================================================
    
    /// Decompose noisy single-qubit gate into quasi-probability distribution.
    ///
    /// Mathematical foundation:
    /// Depolarizing channel: ρ → (1-p)UρU† + (p/4)(IρI† + XρX† + YρY† + ZρZ†)
    ///
    /// Exact inverse (derived on qI/qP below):
    ///   D⁻¹(ρ) = q_I·ρ + q·(XρX + YρY + ZρZ),  q = −p/(4(1−p)),  q_I = 1 + 3p/(4(1−p)).
    ///
    /// Returns a 4-term decomposition — each term runs the noisy gate, then a Pauli correction:
    /// - First term: the gate itself (≡ gate then I), quasi-probability q_I > 0
    /// - Three correction terms: gate then X / Y / Z, each with quasi-probability q < 0
    ///
    /// Properties:
    /// - Quasi-probabilities sum to 1: Σpᵢ = 1
    /// - Normalization factor: Σ|pᵢ| = 1 + 3p/(2(1−p)) (for importance sampling)
    let decomposeSingleQubitGate (gate: CircuitBuilder.Gate) (noiseModel: NoiseModel) : QuasiProbDecomposition =
        // Clamp into [0, 1): the inverse channel is singular at p = 1 (fully depolarizing is
        // non-invertible), where denom below would be 0 and produce NaN quasi-probabilities.
        let p = noiseModel.SingleQubitDepolarizing |> max 0.0 |> min (1.0 - 1e-12)
        
        // Helper: Extract qubit index from single-qubit gate
        let getQubit gate =
            match gate with
            | CircuitBuilder.Gate.H q -> q
            | CircuitBuilder.Gate.X q -> q
            | CircuitBuilder.Gate.Y q -> q
            | CircuitBuilder.Gate.Z q -> q
            | CircuitBuilder.Gate.S q -> q
            | CircuitBuilder.Gate.SDG q -> q
            | CircuitBuilder.Gate.T q -> q
            | CircuitBuilder.Gate.TDG q -> q
            | CircuitBuilder.Gate.RX (q, _) -> q
            | CircuitBuilder.Gate.RY (q, _) -> q
            | CircuitBuilder.Gate.RZ (q, _) -> q
            | _ -> 0  // Default for multi-qubit gates
        
        let qubit = getQubit gate

        // Exact inverse single-qubit depolarizing quasi-probabilities.
        // D⁻¹(ρ) = q_I·ρ + q·(XρX + YρY + ZρZ) with
        //   q_I + 3q = 1            (trace preserving)
        //   q_I − q  = 1/(1−p)      (Pauli eigenvalue inverted)
        // ⇒ q = −p/(4(1−p)),  q_I = 1 + 3p/(4(1−p)).
        let denom = 4.0 * (1.0 - p)
        let qI = 1.0 + 3.0 * p / denom
        let qP = -p / denom

        // 4-term decomposition. Each term runs the original (noisy) gate, then a Pauli
        // correction; the identity correction is just the gate on its own.
        let terms = [
            ([gate], qI)                                   // U   (≡ U then I)
            ([gate; CircuitBuilder.Gate.X qubit], qP)      // U then X
            ([gate; CircuitBuilder.Gate.Y qubit], qP)      // U then Y
            ([gate; CircuitBuilder.Gate.Z qubit], qP)      // U then Z
        ]

        // Normalization = Σ|pᵢ| = |q_I| + 3|q| = 1 + 3p/(2(1−p))
        let normalization = abs qI + 3.0 * abs qP

        {
            Terms = terms
            Normalization = normalization
        }
    
    /// Decompose noisy two-qubit gate into quasi-probability distribution.
    ///
    /// Mathematical foundation:
    /// Two-qubit depolarizing channel over 16 Pauli basis operators:
    /// {I⊗I, I⊗X, I⊗Y, I⊗Z, X⊗I, X⊗X, ..., Z⊗Z}
    ///
    /// Exact inverse (derived on qI/qP below):
    ///   D⁻¹ = q_I·(I⊗I) + q·Σ(15 non-identity Paulis),  q = −p/(16(1−p)),  q_I = 1 + 15p/(16(1−p)).
    ///
    /// Returns a 16-term decomposition — each term runs the noisy gate, then a Pauli correction:
    /// - First term: the gate itself (≡ gate then I⊗I), quasi-probability q_I > 0
    /// - 15 correction terms: gate then a P_control⊗P_target tensor product, each with q < 0
    ///
    /// Properties:
    /// - Quasi-probabilities sum to 1: Σpᵢ = 1
    /// - Normalization factor: Σ|pᵢ| = 1 + 15p/(8(1−p)) (for importance sampling)
    ///
    /// Note: the 15 non-identity Pauli pairs are the corrections; I⊗I folds into the gate term.
    let decomposeTwoQubitGate (gate: CircuitBuilder.Gate) (noiseModel: NoiseModel) : QuasiProbDecomposition =
        // Clamp into [0, 1): the inverse channel is singular at p = 1 (denom below would be 0).
        let p = noiseModel.TwoQubitDepolarizing |> max 0.0 |> min (1.0 - 1e-12)
        
        // Helper: Extract qubits from two-qubit gate
        let (control, target) =
            match gate with
            | CircuitBuilder.Gate.CNOT (c, t) -> (c, t)
            | CircuitBuilder.Gate.CZ (c, t) -> (c, t)
            | CircuitBuilder.Gate.SWAP (q1, q2) -> (q1, q2)
            | CircuitBuilder.Gate.CCX (c1, _, t) -> (c1, t)  // two-qubit approximation of Toffoli
            | _ -> (0, 1)  // Default for other gate types

        // Exact inverse two-qubit depolarizing quasi-probabilities over the 16-Pauli basis
        // {I,X,Y,Z}⊗{I,X,Y,Z}. D⁻¹ = q_I·(I⊗I) + q·Σ(15 non-identity Paulis) with
        //   q_I + 15q = 1           (trace preserving)
        //   q_I − q   = 1/(1−p)     (Pauli eigenvalue inverted)
        // ⇒ q = −p/(16(1−p)),  q_I = 1 + 15p/(16(1−p)).
        let denom = 16.0 * (1.0 - p)
        let qI = 1.0 + 15.0 * p / denom
        let qP = -p / denom

        // A Pauli factor on a given qubit: None = identity (no gate).
        let pauliOn q =
            [ None
              Some (CircuitBuilder.Gate.X q)
              Some (CircuitBuilder.Gate.Y q)
              Some (CircuitBuilder.Gate.Z q) ]

        // 15 non-identity Pauli pairs P_control ⊗ P_target (skip I⊗I). A tensor product
        // applies BOTH factors, e.g. X⊗X = [X control; X target].
        let pauliBasisCorrections =
            [ for pc in pauliOn control do
                for pt in pauliOn target do
                    match pc, pt with
                    | None, None -> ()                       // I⊗I folds into the gate term
                    | _ -> yield (gate :: List.choose id [pc; pt], qP) ]

        // 16 terms total: gate (≡ gate then I⊗I) + 15 Pauli-correction tensor products.
        let terms = ([gate], qI) :: pauliBasisCorrections

        // Normalization = Σ|pᵢ| = |q_I| + 15|q| = 1 + 15p/(8(1−p))
        let normalization = abs qI + 15.0 * abs qP

        {
            Terms = terms
            Normalization = normalization
        }
    
    // ============================================================================
    // Importance Sampling - Converting Negative Probabilities
    // ============================================================================
    
    /// Sample from categorical distribution with given probabilities.
    /// 
    /// Takes a list of probabilities [p₁, p₂, ..., pₙ] that sum to 1.0
    /// Returns index i with probability pᵢ.
    /// 
    /// Uses cumulative probability method for efficient sampling.
    let private sampleCategorical (probabilities: float list) (rng: System.Random) : int =
        let cumulative = 
            probabilities 
            |> List.scan (+) 0.0 
            |> List.skip 1  // Remove initial 0.0 (List.scan always produces at least one element)
        
        let u = rng.NextDouble()
        
        // Find first index where cumulative probability exceeds u
        cumulative 
        |> List.tryFindIndex (fun cum -> u <= cum)
        |> Option.defaultValue (probabilities.Length - 1)
    
    /// Sample from quasi-probability distribution using importance sampling.
    /// 
    /// Key insight: Cannot directly sample from quasi-probability (has negative values!)
    /// 
    /// Importance sampling algorithm:
    /// 1. Convert to proper probabilities: qᵢ = |pᵢ| / Σ|pⱼ|
    /// 2. Sample index i with probability qᵢ
    /// 3. Return (gate_i, sign(pᵢ) × Σ|pⱼ|)
    /// 
    /// The sign correction ensures expectation value is correct:
    /// E[f] = Σᵢ pᵢ·f(gateᵢ) = Σᵢ qᵢ·(sign(pᵢ)×Normalization)·f(gateᵢ)
    /// 
    /// Returns: (sampled_gate_sequence, weight) where weight = ±Normalization
    let sampleQuasiProb (decomposition: QuasiProbDecomposition) (rng: System.Random) : CircuitBuilder.Gate list * float =
        // Step 1: Convert quasi-probabilities to proper probabilities
        // qᵢ = |pᵢ| / Σ|pⱼ|
        let properProbabilities =
            decomposition.Terms
            |> List.map (fun (_, quasiProb) -> abs quasiProb / decomposition.Normalization)

        // Step 2: Sample index using categorical distribution
        let sampledIndex = sampleCategorical properProbabilities rng

        // Step 3: Extract the gate sequence and compute weight with sign correction
        let (gates, originalQuasiProb) = decomposition.Terms.[sampledIndex]
        let sign = if originalQuasiProb >= 0.0 then 1.0 else -1.0
        let weight = sign * decomposition.Normalization

        (gates, weight)
    
    // ============================================================================
    // Full PEC Pipeline - Monte Carlo Error Mitigation
    // ============================================================================
    
    /// Apply Probabilistic Error Cancellation to a quantum circuit.
    /// 
    /// Full pipeline:
    /// 1. Decompose each gate in circuit into quasi-probability distribution
    /// 2. For each Monte Carlo sample:
    ///    a. Sample clean gates from quasi-probability distributions
    ///    b. Build clean circuit from sampled gates
    ///    c. Execute clean circuit and get expectation value
    ///    d. Apply weight (sign correction)
    /// 3. Average weighted results over all samples
    /// 4. Compare with uncorrected baseline
    /// 
    /// Achieves 2-3x accuracy improvement at cost of 10-100x overhead.
    /// 
    /// Returns: PECResult with corrected expectation, error reduction, and overhead metrics.
    let mitigate 
        (circuit: CircuitBuilder.Circuit) 
        (config: PECConfig) 
        (executor: CircuitBuilder.Circuit -> Async<Result<float, string>>)
        : Async<Result<PECResult, string>> =
        async {
            try
                // Step 1: Decompose all gates in the circuit
                let gateDecompositions = 
                    circuit.Gates
                    |> List.rev
                    |> List.map (fun gate ->
                        match gate with
                        | CircuitBuilder.Gate.CNOT _ 
                        | CircuitBuilder.Gate.CZ _
                        | CircuitBuilder.Gate.SWAP _ ->
                            decomposeTwoQubitGate gate config.NoiseModel
                        | CircuitBuilder.Gate.CCX _ ->
                            // For three-qubit gates, use two-qubit approximation
                            // (More sophisticated handling could be added in future)
                            decomposeTwoQubitGate gate config.NoiseModel
                        | _ ->
                            decomposeSingleQubitGate gate config.NoiseModel)
                
                // Step 2: Monte Carlo sampling - execute samples in parallel
                let rng = System.Random(config.Seed |> Option.defaultValue 42)
                
                // Generate all samples first (for reproducibility with seed)
                let samples = 
                    [1 .. config.Samples]
                    |> List.map (fun _ ->
                        // Sample clean circuit from quasi-probability distributions.
                        // Each sampled term is a short gate sequence (original gate + Pauli
                        // correction), appended in order to rebuild the corrected circuit.
                        gateDecompositions
                        |> List.fold (fun (gates, weight) decomposition ->
                            let (sampledGates, gateWeight) = sampleQuasiProb decomposition rng
                            (gates @ sampledGates, weight * gateWeight)
                        ) ([], 1.0))
                
                // Execute all sampled circuits
                let! sampleResults =
                    samples
                    |> List.map (fun (sampledGates, totalWeight) ->
                        async {
                            // Build clean circuit with sampled gates. sampledGates is in program
                            // order (the original circuit was reversed at step 1), but
                            // CircuitBuilder.Circuit stores Gates most-recent-first, so reverse it
                            // back — otherwise a conforming executor (which List.rev's) runs the
                            // sampled circuit backwards, inconsistently with the baseline.
                            let sampledCircuit: CircuitBuilder.Circuit = {
                                QubitCount = circuit.QubitCount
                                Gates = List.rev sampledGates
                            }
                            
                            // Execute sampled circuit
                            let! executionResult = executor sampledCircuit
                            
                            return 
                                match executionResult with
                                | Ok expectation -> Ok (expectation, totalWeight)
                                | Error err -> Error err
                        })
                    |> Async.Parallel
                
                // Check for execution failures
                let failures = 
                    sampleResults 
                    |> Array.choose (function | Error e -> Some e | _ -> None)
                
                if not (Array.isEmpty failures) then
                    return Error (sprintf "Circuit execution failed: %s" (String.concat "; " failures))
                else
                    // Step 3: Aggregate weighted results
                    let sumCorrected =
                        sampleResults
                        |> Array.choose (function | Ok (exp, weight) -> Some (exp * weight) | _ -> None)
                        |> Array.sum
                
                    let correctedExpectation = sumCorrected / float config.Samples
                    
                    // Step 4: Get uncorrected baseline and compute result
                    let! uncorrectedResult = executor circuit
                    
                    return
                        uncorrectedResult
                        |> Result.map (fun uncorrectedExpectation ->
                            // Calculate error reduction
                            let errorReduction = 
                                if uncorrectedExpectation <> 0.0 then
                                    abs ((correctedExpectation - uncorrectedExpectation) / uncorrectedExpectation)
                                else
                                    0.0
                            
                            // Calculate overhead (samples + 1 baseline execution)
                            let overhead = float config.Samples
                            
                            {
                                CorrectedExpectation = correctedExpectation
                                UncorrectedExpectation = uncorrectedExpectation
                                ErrorReduction = errorReduction
                                SamplesUsed = config.Samples
                                Overhead = overhead
                            })
                        |> Result.mapError (sprintf "Baseline execution failed: %s")
            with
            | ex -> return Error (sprintf "PEC pipeline error: %s" ex.Message)
        }
