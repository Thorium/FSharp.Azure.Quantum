namespace FSharp.Azure.Quantum.Topological

open System

/// Error propagation tracking for topological quantum circuits
/// 
/// **Purpose**: Track how approximation errors accumulate through a quantum circuit.
/// 
/// **Key Concepts**:
/// 1. **Gate Errors**: Each gate has an approximation error (0 for exact gates)
/// 2. **Error Propagation**: Errors compound as gates are applied sequentially
/// 3. **Error Bounds**: Provide probabilistic bounds on final circuit fidelity
/// 
/// **Mathematical Foundation**:
/// - For independent errors ε₁, ε₂, ..., εₙ, total error ≈ Σεᵢ (additive model)
/// - For diamond norm: ε_total ≤ Σεᵢ (worst case)
/// - For trace distance: ε_total ≤ √(Σεᵢ²) (quadratic accumulation)
/// 
/// **Use Cases**:
/// - Validate circuit quality before execution
/// - Compare different compilation strategies
/// - Determine if circuit meets fidelity requirements
/// - Debug approximation issues
module ErrorPropagation =
    
    open SolovayKitaev
    open CircuitOptimization
    
    // ========================================================================
    // TYPES
    // ========================================================================
    
    /// Error accumulation model
    type ErrorModel =
        /// Simple additive model: ε_total = Σεᵢ
        | Additive
        /// Quadratic (RMS) model: ε_total = √(Σεᵢ²)
        | Quadratic
        /// Worst-case (diamond norm): ε_total ≤ Σεᵢ
        | DiamondNorm
    
    /// Error contribution from a single gate
    type GateError = {
        /// Gate that contributes error
        Gate: BasicGate
        /// Position in circuit (0-indexed)
        Position: int
        /// Approximation error for this gate
        Error: float
        /// Error source description
        Source: string
    }
    
    /// Accumulated error through circuit
    type ErrorAccumulation = {
        /// Individual gate errors
        GateErrors: GateError list
        /// Total accumulated error
        TotalError: float
        /// Error model used
        Model: ErrorModel
        /// Number of exact gates
        ExactGateCount: int
        /// Number of approximate gates
        ApproximateGateCount: int
        /// Maximum single-gate error
        MaxSingleError: float
    }
    
    /// Error budget for circuit design
    type ErrorBudget = {
        /// Maximum allowed total error
        MaxTotalError: float
        /// Maximum allowed single-gate error
        MaxSingleGateError: float
        /// Error model to use
        Model: ErrorModel
    }
    
    /// Circuit quality assessment
    type QualityAssessment = {
        /// Does circuit meet error budget?
        MeetsBudget: bool
        /// Current total error
        CurrentError: float
        /// Allowed total error
        AllowedError: float
        /// Error margin (positive = under budget, negative = over budget)
        ErrorMargin: float
        /// Percentage of budget used
        BudgetUtilization: float
        /// Quality grade (A+ to F)
        Grade: string
    }
    
    // ========================================================================
    // ERROR CALCULATION
    // ========================================================================
    
    /// Calculate total error using specified model
    let calculateTotalError (errors: float list) (model: ErrorModel) : float =
        match model with
        | Additive ->
            // Simple sum: ε_total = Σεᵢ
            List.sum errors
        
        | Quadratic ->
            // RMS (root mean square): ε_total = √(Σεᵢ²)
            errors
            |> List.map (fun e -> e * e)
            |> List.sum
            |> sqrt
        
        | DiamondNorm ->
            // Worst case: same as additive for conservative estimate
            List.sum errors
    
    /// Extract error from a single gate
    /// Exact gates (T, S, Z) have error = 0
    /// Approximate gates have non-zero error
    let getGateError (gate: BasicGate) : float * string =
        match gate with
        // Exact gates in topological QC (Ising anyons)
        | T | TDagger -> (0.0, "Exact (single Majorana braiding)")
        | S | SDagger -> (0.0, "Exact (double Majorana braiding)")
        | Z -> (0.0, "Exact (quadruple Majorana braiding)")
        | I -> (0.0, "Exact (identity)")
        
        // Approximate gates (these should never appear in optimized topological circuits!)
        | H -> (1e-5, "Approximated via Solovay-Kitaev")
        | X -> (1e-5, "Approximated via Solovay-Kitaev")
        | Y -> (1e-5, "Approximated via Solovay-Kitaev")
    
    /// Track error propagation through gate sequence
    let trackErrors (gates: GateSequence) (model: ErrorModel) : ErrorAccumulation =
        let gateErrors =
            gates
            |> List.mapi (fun i gate ->
                let (error, source) = getGateError gate
                { Gate = gate
                  Position = i
                  Error = error
                  Source = source })
        
        let errorValues = gateErrors |> List.map (fun ge -> ge.Error)
        let totalError = calculateTotalError errorValues model
        
        let exactCount = gateErrors |> List.filter (fun ge -> ge.Error = 0.0) |> List.length
        let approxCount = gates.Length - exactCount
        
        let maxError = 
            if List.isEmpty errorValues then 0.0
            else List.max errorValues
        
        { GateErrors = gateErrors
          TotalError = totalError
          Model = model
          ExactGateCount = exactCount
          ApproximateGateCount = approxCount
          MaxSingleError = maxError }
    
    // ========================================================================
    // ERROR BUDGET MANAGEMENT
    // ========================================================================
    
    /// Default error budget for high-fidelity quantum computing
    let defaultBudget = {
        MaxTotalError = 1e-3  // 0.1% total error
        MaxSingleGateError = 1e-5  // 0.001% per gate
        Model = Quadratic
    }
    
    /// Strict error budget for fault-tolerant QC
    let strictBudget = {
        MaxTotalError = 1e-6  // 0.0001% total error
        MaxSingleGateError = 1e-8  // Ultra-precise gates
        Model = DiamondNorm
    }
    
    /// Relaxed error budget for NISQ-era algorithms
    let relaxedBudget = {
        MaxTotalError = 1e-2  // 1% total error
        MaxSingleGateError = 1e-4  // 0.01% per gate
        Model = Additive
    }
    
    /// Assess circuit quality against budget
    let assessQuality (accumulation: ErrorAccumulation) (budget: ErrorBudget) : QualityAssessment =
        let meetsBudget = 
            accumulation.TotalError <= budget.MaxTotalError &&
            accumulation.MaxSingleError <= budget.MaxSingleGateError
        
        let errorMargin = budget.MaxTotalError - accumulation.TotalError
        
        let utilization =
            if budget.MaxTotalError > 0.0 then
                100.0 * accumulation.TotalError / budget.MaxTotalError
            else 0.0
        
        // Assign grade based on budget utilization
        let grade =
            if utilization < 10.0 then "A+"
            elif utilization < 25.0 then "A"
            elif utilization < 50.0 then "B"
            elif utilization < 75.0 then "C"
            elif utilization < 100.0 then "D"
            else "F"
        
        { MeetsBudget = meetsBudget
          CurrentError = accumulation.TotalError
          AllowedError = budget.MaxTotalError
          ErrorMargin = errorMargin
          BudgetUtilization = utilization
          Grade = grade }
    
    // ========================================================================
    // OPTIMIZATION SUGGESTIONS
    // ========================================================================
    
    /// Suggest optimizations to meet error budget
    let suggestOptimizations (accumulation: ErrorAccumulation) (budget: ErrorBudget) : string list =
        let mutable suggestions = []
        
        // Check if over budget
        if accumulation.TotalError > budget.MaxTotalError then
            suggestions <- "⚠️ Circuit exceeds error budget" :: suggestions
            
            // Suggest increasing Solovay-Kitaev precision
            if accumulation.ApproximateGateCount > 0 then
                suggestions <- "• Increase Solovay-Kitaev precision (smaller ε)" :: suggestions
                suggestions <- "• Use larger base set (n=5 instead of n=4)" :: suggestions
        
        // Check for single gates exceeding budget
        if accumulation.MaxSingleError > budget.MaxSingleGateError then
            suggestions <- "⚠️ Individual gate(s) exceed single-gate error budget" :: suggestions
            suggestions <- "• Tighten approximation tolerance for H, X, Y gates" :: suggestions
        
        // Suggest circuit optimization
        if accumulation.ApproximateGateCount > 10 then
            suggestions <- "• Apply circuit optimization to reduce gate count" :: suggestions
            suggestions <- "• Use template matching to simplify gate sequences" :: suggestions
        
        // Suggest different error model
        match budget.Model with
        | DiamondNorm ->
            suggestions <- "ℹ️ Using conservative diamond norm model" :: suggestions
            suggestions <- "• Consider quadratic model for tighter error bounds" :: suggestions
        | _ -> ()
        
        // If no issues, provide positive feedback
        if List.isEmpty suggestions then
            suggestions <- ["✅ Circuit meets error budget with room to spare"]
        
        suggestions
    
    // ========================================================================
    // DISPLAY UTILITIES
    // ========================================================================
    
    /// Display error accumulation details
    let displayAccumulation (accumulation: ErrorAccumulation) : string =
        let modelName =
            match accumulation.Model with
            | Additive -> "Additive (Σεᵢ)"
            | Quadratic -> "Quadratic (√Σεᵢ²)"
            | DiamondNorm -> "Diamond Norm (worst case)"
        
        let topErrors =
            accumulation.GateErrors
            |> List.filter (fun ge -> ge.Error > 0.0)
            |> List.sortByDescending (fun ge -> ge.Error)
            |> List.truncate 5
            |> List.map (fun ge -> 
                $"    Gate {ge.Position}: {ge.Gate} (ε = {ge.Error:E6}) - {ge.Source}")
            |> String.concat "\n"
        
        let topErrorsDisplay =
            if String.IsNullOrEmpty(topErrors) then
                "    (No approximate gates - all exact!)"
            else
                topErrors
        
        $"""Error Propagation Analysis
━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
Model: {modelName}
Total Error: {accumulation.TotalError:E6}
Max Single Error: {accumulation.MaxSingleError:E6}

Gate Breakdown:
  Exact gates: {accumulation.ExactGateCount}
  Approximate gates: {accumulation.ApproximateGateCount}
  Total gates: {accumulation.ExactGateCount + accumulation.ApproximateGateCount}

Top Error Contributors:
{topErrorsDisplay}"""
    
    /// Display quality assessment
    let displayQualityAssessment (assessment: QualityAssessment) : string =
        let status = if assessment.MeetsBudget then "✅ PASS" else "❌ FAIL"
        let marginSign = if assessment.ErrorMargin >= 0.0 then "+" else ""
        
        $"""Circuit Quality Assessment
━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
Status: {status}
Grade: {assessment.Grade}

Error Budget:
  Current: {assessment.CurrentError:E6}
  Allowed: {assessment.AllowedError:E6}
  Margin: {marginSign}{assessment.ErrorMargin:E6}
  
Budget Utilization: {assessment.BudgetUtilization:F1}%%"""
    
    /// Full error analysis report
    let generateReport
        (gates: GateSequence)
        (budget: ErrorBudget)
        : string =
        
        let accumulation = trackErrors gates budget.Model
        let assessment = assessQuality accumulation budget
        let suggestions = suggestOptimizations accumulation budget
        
        let suggestionsText =
            suggestions
            |> List.map (fun s -> $"  {s}")
            |> String.concat "\n"
        
        $"""{displayAccumulation accumulation}

{displayQualityAssessment assessment}

Recommendations:
{suggestionsText}
━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━"""
