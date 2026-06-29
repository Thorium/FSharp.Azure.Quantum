namespace FSharp.Azure.Quantum

open System
open FSharp.Azure.Quantum.Core

/// Error Mitigation Strategy Selection module.
/// 
/// Implements automatic strategy selection based on problem type, backend characteristics,
/// and cost-benefit analysis. Supports fallback strategies if primary mitigation fails.
module ErrorMitigationStrategy =
    
    // ============================================================================
    // Types - Strategy Selection Domain
    // ============================================================================
    
    /// Available mitigation techniques that can be applied individually or combined.
    type MitigationTechnique =
        /// Zero-Noise Extrapolation: 30-50% error reduction, 3x overhead
        | ZeroNoiseExtrapolation of ZeroNoiseExtrapolation.ZNEConfig
        
        /// Probabilistic Error Cancellation: 2-3x accuracy improvement, 10-100x overhead
        | ProbabilisticErrorCancellation of ProbabilisticErrorCancellation.PECConfig
        
        /// Readout Error Mitigation: 50-90% readout correction, ~0x overhead (after calibration)
        | ReadoutErrorMitigation of ReadoutErrorMitigation.CalibrationMatrix
        
        /// Combined techniques applied in sequence
        | Combined of techniques: MitigationTechnique list
    
    /// Strategy selection criteria for automatic decision-making.
    type SelectionCriteria = {
        /// Circuit depth (number of gates)
        CircuitDepth: int
        
        /// Number of qubits in circuit
        QubitCount: int
        
        /// Target quantum backend
        Backend: Types.Backend
        
        /// Maximum acceptable cost in USD (None = no limit)
        MaxCostUSD: float option
        
        /// Required accuracy target 0.0-1.0 (None = best effort)
        RequiredAccuracy: float option
        
        /// User-supplied calibration matrix from actual hardware characterization.
        /// When None, a default placeholder (98% readout fidelity) is used for strategy
        /// selection heuristics. For production use, supply real calibration data measured
        /// via ReadoutErrorMitigation.measureCalibrationMatrix.
        Calibration: ReadoutErrorMitigation.CalibrationMatrix option
    }
    
    /// Recommended mitigation strategy with reasoning and estimates.
    type RecommendedStrategy = {
        /// Primary mitigation technique to apply
        Primary: MitigationTechnique
        
        /// Fallback strategy if primary fails (None = no fallback)
        Fallback: MitigationTechnique option
        
        /// Human-readable explanation for this choice
        Reasoning: string
        
        /// Estimated cost multiplier (e.g., 3.0 = 3x base cost)
        EstimatedCostMultiplier: float
        
        /// Estimated accuracy improvement (0.0-1.0)
        EstimatedAccuracy: float
    }
    
    /// Result of applying mitigation strategy.
    type MitigatedResult = {
        /// Corrected measurement histogram
        Histogram: Map<string, float>
        
        /// Technique that was successfully applied
        AppliedTechnique: MitigationTechnique
        
        /// Whether fallback was used
        UsedFallback: bool
        
        /// Actual cost multiplier achieved
        ActualCostMultiplier: float
    }
    
    // ============================================================================
    // Cost Estimation Functions
    // ============================================================================
    
    /// Estimate cost multiplier for ZNE technique.
    /// ZNE requires 3-5 noise scaling levels, each with full circuit execution.
    let estimateZNECost (criteria: SelectionCriteria) : float =
        // ZNE typically uses 3 noise levels (1.0, 1.5, 2.0)
        // Each level requires full measurement shots
        3.0
    
    /// Estimate cost multiplier for PEC technique.
    /// PEC requires Monte Carlo sampling with importance weighting (high overhead).
    let estimatePECCost (criteria: SelectionCriteria) : float =
        // PEC overhead depends on noise model normalization factor
        // Typical range: 10x to 100x
        // Use conservative estimate based on circuit depth
        if criteria.CircuitDepth < 20 then
            10.0  // Shallow circuits: lower overhead
        elif criteria.CircuitDepth < 50 then
            30.0  // Medium circuits
        else
            50.0  // Deep circuits: higher overhead
    
    /// Estimate cost multiplier for Readout Error Mitigation.
    /// REM has zero runtime overhead after calibration (calibration cost amortized).
    let estimateReadoutCost (criteria: SelectionCriteria) : float =
        0.0  // No additional cost after calibration
    
    /// Estimate cost multiplier for combined techniques.
    /// Combined cost is approximately sum of individual costs.
    let rec estimateCombinedCost (techniques: MitigationTechnique list) (criteria: SelectionCriteria) : float =
        techniques
        |> List.sumBy (fun tech ->
            match tech with
            | ZeroNoiseExtrapolation _ -> estimateZNECost criteria
            | ProbabilisticErrorCancellation _ -> estimatePECCost criteria
            | ReadoutErrorMitigation _ -> estimateReadoutCost criteria
            | Combined nested -> estimateCombinedCost nested criteria
        )
    
    // ============================================================================
    // Strategy Selection Logic
    // ============================================================================
    
    /// Create default ZNE configuration for a given backend.
    let private createDefaultZNEConfig (backend: Types.Backend) : ZeroNoiseExtrapolation.ZNEConfig =
        {
            NoiseScalings = [
                ZeroNoiseExtrapolation.IdentityInsertion 0.0    // baseline
                ZeroNoiseExtrapolation.IdentityInsertion 0.5    // 1.5x noise
                ZeroNoiseExtrapolation.IdentityInsertion 1.0    // 2.0x noise
            ]
            PolynomialDegree = 2
            MinSamples = 1000
        }
    
    /// Create default PEC configuration for a given backend.
    let private createDefaultPECConfig (backend: Types.Backend) : ProbabilisticErrorCancellation.PECConfig =
        {
            NoiseModel = {
                SingleQubitDepolarizing = 0.001
                TwoQubitDepolarizing = 0.01
                ReadoutError = 0.02
            }
            Samples = 1000
            Seed = None
        }
    
    /// Create default calibration matrix for readout error mitigation.
    /// WARNING: This uses a fabricated 98%/2% readout fidelity model for strategy
    /// selection heuristics ONLY. For actual error correction, supply real calibration
    /// data measured via ReadoutErrorMitigation.measureCalibrationMatrix.
    let private createDefaultCalibration (backend: Types.Backend) (qubits: int) : ReadoutErrorMitigation.CalibrationMatrix =
        // For strategy selection, use placeholder calibration
        // Real calibration would be measured via ReadoutErrorMitigation.measureCalibrationMatrix
        let dim = 1 <<< qubits  // 2^qubits
        let matrix = Array2D.init dim dim (fun i j ->
            if i = j then 0.98 else 0.02 / float (dim - 1)
        )
        {
            Matrix = matrix
            Qubits = qubits
            Timestamp = DateTime.UtcNow
            Backend = backend.Id
            CalibrationShots = 10000
        }
    
    /// Select optimal mitigation strategy based on problem characteristics.
    /// 
    /// Decision tree implements cost-benefit heuristics:
    /// - Shallow circuits (<10 gates): Readout only
    /// - Medium circuits (10-50 gates): ZNE + Readout
    /// - Deep circuits (>50 gates) with high accuracy: PEC + ZNE + Readout
    /// - Budget constrained: Downgrade to cheaper techniques
    let selectStrategy (criteria: SelectionCriteria) : RecommendedStrategy =
        
        let zneConfig = createDefaultZNEConfig criteria.Backend
        let pecConfig = createDefaultPECConfig criteria.Backend
        let calibration = 
            criteria.Calibration
            |> Option.defaultWith (fun () -> createDefaultCalibration criteria.Backend criteria.QubitCount)
        
        // Decision tree based on cost-benefit analysis
        match criteria with
        
        // Budget extremely constrained (<$1): Readout only
        | { MaxCostUSD = Some budget } when budget < 1.0 ->
            {
                Primary = ReadoutErrorMitigation calibration
                Fallback = None
                Reasoning = "Minimal budget - readout mitigation only (zero overhead)"
                EstimatedCostMultiplier = 0.0
                EstimatedAccuracy = 0.80
            }
        
        // Shallow circuits (<10 gates): Readout errors dominate
        | { CircuitDepth = depth } when depth < 10 ->
            {
                Primary = ReadoutErrorMitigation calibration
                Fallback = None
                Reasoning = "Shallow circuit - readout errors dominate over gate errors"
                EstimatedCostMultiplier = 0.0
                EstimatedAccuracy = 0.85
            }
        
        // High accuracy required (>90%) with sufficient budget: Full stack
        | { RequiredAccuracy = Some acc; MaxCostUSD = Some budget } 
            when acc > 0.90 && budget > 100.0 ->
            {
                Primary = Combined [
                    ProbabilisticErrorCancellation pecConfig
                    ZeroNoiseExtrapolation zneConfig
                    ReadoutErrorMitigation calibration
                ]
                Fallback = Some (Combined [
                    ZeroNoiseExtrapolation zneConfig
                    ReadoutErrorMitigation calibration
                ])
                Reasoning = "High accuracy required (>90%) - full mitigation stack with PEC"
                EstimatedCostMultiplier = 50.0 + 3.0  // PEC + ZNE
                EstimatedAccuracy = 0.92
            }
        
        // Medium circuits (10-50 gates) with budget: ZNE + Readout
        | { CircuitDepth = depth; MaxCostUSD = Some budget } 
            when depth >= 10 && depth < 50 && budget > 10.0 ->
            {
                Primary = Combined [
                    ZeroNoiseExtrapolation zneConfig
                    ReadoutErrorMitigation calibration
                ]
                Fallback = Some (ReadoutErrorMitigation calibration)
                Reasoning = "Medium circuit with budget - ZNE provides good cost/benefit balance"
                EstimatedCostMultiplier = 3.0
                EstimatedAccuracy = 0.75
            }
        
        // Deep circuits (>50 gates) without strict budget: ZNE + Readout
        | { CircuitDepth = depth } when depth >= 50 ->
            {
                Primary = Combined [
                    ZeroNoiseExtrapolation zneConfig
                    ReadoutErrorMitigation calibration
                ]
                Fallback = Some (ReadoutErrorMitigation calibration)
                Reasoning = "Deep circuit - ZNE + Readout for balanced accuracy/cost"
                EstimatedCostMultiplier = 3.0
                EstimatedAccuracy = 0.70
            }
        
        // Budget constrained (<$10): Readout only
        | { MaxCostUSD = Some budget } when budget < 10.0 ->
            {
                Primary = ReadoutErrorMitigation calibration
                Fallback = None
                Reasoning = "Budget constrained (<$10) - readout mitigation only"
                EstimatedCostMultiplier = 0.0
                EstimatedAccuracy = 0.80
            }
        
        // Default: ZNE + Readout (good balance for most cases)
        | _ ->
            {
                Primary = Combined [
                    ZeroNoiseExtrapolation zneConfig
                    ReadoutErrorMitigation calibration
                ]
                Fallback = Some (ReadoutErrorMitigation calibration)
                Reasoning = "Balanced approach - ZNE + Readout for general use"
                EstimatedCostMultiplier = 3.0
                EstimatedAccuracy = 0.75
            }
    
    // ============================================================================
    // Strategy Application (Placeholder for async execution)
    // ============================================================================
    
    /// The readout calibration carried by a technique, if any. Readout error mitigation is
    /// the only component that can be applied to a finished measurement histogram.
    let rec private readoutCalibrationOf (technique: MitigationTechnique) : ReadoutErrorMitigation.CalibrationMatrix option =
        match technique with
        | ReadoutErrorMitigation calibration -> Some calibration
        | Combined techniques -> techniques |> List.tryPick readoutCalibrationOf
        | ZeroNoiseExtrapolation _ | ProbabilisticErrorCancellation _ -> None

    /// Apply a mitigation strategy to a measurement histogram.
    ///
    /// Only readout error mitigation can be applied post-hoc to a finished histogram — it
    /// corrects the measured counts with the inverse confusion matrix. Zero-Noise Extrapolation
    /// and Probabilistic Error Cancellation are circuit-level techniques (they re-execute the
    /// circuit at multiple noise levels / sample quasi-probability circuits), so they cannot be
    /// applied here; for those use ZeroNoiseExtrapolation.mitigate / ProbabilisticErrorCancellation.mitigate
    /// with a circuit executor.
    ///
    /// This genuinely applies the readout-correction component of the chosen technique (whether
    /// a bare ReadoutErrorMitigation or a Combined strategy that contains one). If the primary
    /// carries no applicable readout calibration it falls back to the secondary strategy.
    let applyStrategy
        (histogram: Map<string, int>)
        (strategy: RecommendedStrategy)
        : QuantumResult<MitigatedResult> =

        let applyTechnique (technique: MitigationTechnique) : QuantumResult<Map<string, float>> =
            match readoutCalibrationOf technique with
            | Some calibration ->
                ReadoutErrorMitigation.correctReadoutErrors histogram calibration ReadoutErrorMitigation.defaultConfig
                |> Result.map (fun corrected -> corrected.Histogram)
                |> Result.mapError (fun msg -> QuantumError.OperationError ("Readout error mitigation", msg))
            | None ->
                Error (QuantumError.NotImplemented (
                    "post-hoc circuit-level mitigation (ZNE/PEC)",
                    Some "Zero-Noise Extrapolation and Probabilistic Error Cancellation must execute the circuit at multiple noise levels; they cannot be applied to a finished histogram. Use their mitigate functions with a circuit executor, or include a ReadoutErrorMitigation calibration in the strategy."))

        match applyTechnique strategy.Primary with
        | Ok corrected ->
            Ok {
                Histogram = corrected
                AppliedTechnique = strategy.Primary
                UsedFallback = false
                ActualCostMultiplier = strategy.EstimatedCostMultiplier
            }
        | Error primaryErr ->
            match strategy.Fallback with
            | Some fallback ->
                match applyTechnique fallback with
                | Ok corrected ->
                    Ok {
                        Histogram = corrected
                        AppliedTechnique = fallback
                        UsedFallback = true
                        ActualCostMultiplier = strategy.EstimatedCostMultiplier / 2.0
                    }
                | Error fallbackErr ->
                    Error (QuantumError.OperationError (
                        "Error mitigation",
                        sprintf "Primary failed (%s) and fallback failed (%s)" primaryErr.Message fallbackErr.Message))
            | None -> Error primaryErr
