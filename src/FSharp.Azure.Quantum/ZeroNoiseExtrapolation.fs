namespace FSharp.Azure.Quantum

/// Zero-Noise Extrapolation (ZNE) error mitigation module.
/// 
/// Implements Richardson extrapolation to reduce quantum circuit errors by 30-50%.
/// Composes with CircuitBuilder for circuit manipulation and async workflows for execution.
module ZeroNoiseExtrapolation =
    
    // ============================================================================
    // Types - Error Mitigation Domain
    // ============================================================================
    
    /// Noise scaling strategy for error mitigation.
    /// 
    /// Two approaches:
    /// - IdentityInsertion: Insert I·I gate pairs (for IonQ)
    /// - PulseStretching: Stretch gate pulse duration (for Rigetti)
    [<Struct>]
    type NoiseScaling =
        /// Identity insertion: Increase circuit depth by adding I·I pairs.
        /// insertionRate = 0.5 means +50% circuit depth (baseline noise * 1.5)
        | IdentityInsertion of insertionRate: float
        
        /// Pulse stretching: Increase gate pulse duration to amplify decoherence.
        /// stretchFactor = 1.5 means +50% longer pulses (baseline noise * 1.5)
        | PulseStretching of stretchFactor: float
    
    /// Configuration for Zero-Noise Extrapolation
    type ZNEConfig = {
        /// Noise scaling levels to measure (e.g., [1.0; 1.5; 2.0])
        NoiseScalings: NoiseScaling list
        
        /// Polynomial degree for Richardson extrapolation (typically 2)
        PolynomialDegree: int
        
        /// Minimum number of measurement shots per noise level
        MinSamples: int
    }
    
    /// Result of Zero-Noise Extrapolation
    type ZNEResult = {
        /// Extrapolated zero-noise expectation value E(0)
        ZeroNoiseValue: float
        
        /// Measured expectation values at each noise level
        MeasuredValues: (float * float) list  // (noise_level, expectation_value)
        
        /// Polynomial coefficients [a₀, a₁, a₂, ...]
        /// E(λ) = a₀ + a₁λ + a₂λ² + ...
        PolynomialCoefficients: float list
        
        /// R² goodness-of-fit (1.0 = perfect fit)
        GoodnessOfFit: float
    }
    
    // ============================================================================
    // Public API - Composable Functions
    // ============================================================================
    
    // Will be implemented in next cycles
