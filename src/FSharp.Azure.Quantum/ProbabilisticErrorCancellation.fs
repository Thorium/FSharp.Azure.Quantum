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
        /// List of (clean_gate, quasi_probability) pairs
        /// Note: Some probabilities can be NEGATIVE!
        Terms: (CircuitBuilder.Gate * float) list
        
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
