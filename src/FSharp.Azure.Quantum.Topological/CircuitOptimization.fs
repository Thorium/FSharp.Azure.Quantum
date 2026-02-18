namespace FSharp.Azure.Quantum.Topological

open System

/// Circuit optimization techniques for topological quantum computing
/// 
/// **Goals:**
/// 1. Minimize T-count (T gates are expensive in fault-tolerant QC)
/// 2. Reduce circuit depth (minimize execution time)
/// 3. Apply commutation rules to enable gate cancellations
/// 4. Template matching for common patterns
/// 
/// **Techniques Implemented:**
/// - T-count minimization using Clifford+T identities
/// - Commutation-based reordering
/// - Gate cancellation and merging
/// - Template pattern recognition
module CircuitOptimization =
    
    open SolovayKitaev
    
    // ========================================================================
    // TYPES
    // ========================================================================
    
    /// Optimization statistics
    type OptimizationStats = {
        OriginalGateCount: int
        OptimizedGateCount: int
        OriginalTCount: int
        OptimizedTCount: int
        OriginalDepth: int
        OptimizedDepth: int
        OptimizationsApplied: string list
    }
    
    // ========================================================================
    // GATE COMMUTATION RULES
    // ========================================================================
    
    /// Check if two gates commute
    let commutes (g1: BasicGate) (g2: BasicGate) : bool =
        match g1, g2 with
        // Z-axis gates all commute with each other
        | T, T | T, TDagger | T, S | T, SDagger | T, Z -> true
        | TDagger, T | TDagger, TDagger | TDagger, S | TDagger, SDagger | TDagger, Z -> true
        | S, T | S, TDagger | S, S | S, SDagger | S, Z -> true
        | SDagger, T | SDagger, TDagger | SDagger, S | SDagger, SDagger | SDagger, Z -> true
        | Z, T | Z, TDagger | Z, S | Z, SDagger | Z, Z -> true
        
        // Identity commutes with everything
        | I, _ | _, I -> true
        
        // X and Y don't commute with Z-axis gates
        | X, X | Y, Y -> true
        
        // Default: don't commute
        | _ -> false
    
    /// Check if gate is a Clifford gate (cheap)
    let isClifford (gate: BasicGate) : bool =
        match gate with
        | H | S | SDagger | X | Y | Z | I -> true
        | T | TDagger -> false
    
    /// Check if gate is a T gate (expensive)
    let isTGate (gate: BasicGate) : bool =
        match gate with
        | T | TDagger -> true
        | _ -> false
    
    // ========================================================================
    // GATE CANCELLATION
    // ========================================================================
    
    /// Cancel adjacent inverse gates
    /// Example: T followed by T† → cancel both
    let rec cancelInverses (gates: GateSequence) : GateSequence =
        match gates with
        | [] -> []
        | [g] -> [g]
        | T :: TDagger :: rest | TDagger :: T :: rest -> cancelInverses rest
        | S :: SDagger :: rest | SDagger :: S :: rest -> cancelInverses rest
        | H :: H :: rest -> cancelInverses rest
        | X :: X :: rest -> cancelInverses rest
        | Y :: Y :: rest -> cancelInverses rest
        | Z :: Z :: rest -> cancelInverses rest
        | I :: rest -> cancelInverses rest  // Identity can always be removed
        | g :: rest -> g :: cancelInverses rest
    
    /// Merge adjacent gates on same axis
    /// Example: T·T = S, Z·Z = I
    /// 
    /// **Gate phase arithmetic** (T = diag(1, exp(iπ/4))):
    ///   T² = S, T⁴ = S² = Z, T⁸ = Z² = I
    ///   S² = Z, S⁴ = I
    ///   T·S = T³ (no single-gate equivalent)
    let rec mergeAdjacentGates (gates: GateSequence) : GateSequence =
        match gates with
        | [] -> []
        | [g] -> [g]
        
        // T·T = S (correct: exp(iπ/4)² = exp(iπ/2))
        | T :: T :: rest -> mergeAdjacentGates (S :: rest)
        | TDagger :: TDagger :: rest -> mergeAdjacentGates (SDagger :: rest)
        
        // Z·Z = I (correct: (-1)² = 1)
        | Z :: Z :: rest -> mergeAdjacentGates rest
        
        | g :: rest -> g :: mergeAdjacentGates rest
    
    // ========================================================================
    // COMMUTATION-BASED OPTIMIZATION
    // ========================================================================
    
    /// Move Clifford gates to the left (they're cheap)
    /// Move T gates to the right (they're expensive, easier to optimize together)
    let rec commuteCliffordsLeft (gates: GateSequence) : GateSequence * bool =
        match gates with
        | [] -> ([], false)
        | [g] -> ([g], false)
        | g1 :: g2 :: rest when (not (isClifford g1)) && (isClifford g2) && commutes g1 g2 ->
            // Swap: expensive gate on right, cheap gate on left
            let (optimized, _) = commuteCliffordsLeft (g2 :: g1 :: rest)
            (optimized, true)  // Made a change
        | g :: rest ->
            let (optimized, changed) = commuteCliffordsLeft rest
            (g :: optimized, changed)
    
    /// Repeatedly apply commutation until no more changes
    let rec commuteCliffordsUntilStable (gates: GateSequence) : GateSequence =
        let (optimized, changed) = commuteCliffordsLeft gates
        if changed then
            commuteCliffordsUntilStable optimized
        else
            optimized
    
    // ========================================================================
    // TEMPLATE MATCHING
    // ========================================================================
    
    /// Recognize common patterns and replace with optimized equivalents
    /// 
    /// **Verified identities** (T = diag(1, exp(iπ/4)), S = T²):
    /// - T⁷ = T⁸·T⁻¹ = T† (7 T gates → 1 T† gate) since T⁸ = I
    /// - T⁵ = T⁴·T = Z·T (5 T gates → 1 T gate + 1 Clifford) since T⁴ = Z
    /// - T³ = T²·T = S·T (3 T gates → 1 T gate + 1 Clifford)
    /// - S³ = S⁴·S⁻¹ = S† (3 Cliffords → 1 Clifford) since S⁴ = I
    let rec templateMatch (gates: GateSequence) : GateSequence * bool =
        match gates with
        | [] -> ([], false)
        | [g] -> ([g], false)
        
        // Pattern: T⁷ = T† (T⁸ = I, so T⁷ = T⁻¹)
        // Reduces T-count from 7 to 1
        | T :: T :: T :: T :: T :: T :: T :: rest ->
            let (optimized, _) = templateMatch (TDagger :: rest)
            (optimized, true)
        
        // Pattern: T⁵ = Z·T (T⁴ = Z, so T⁵ = Z·T)
        // Reduces T-count from 5 to 1
        | T :: T :: T :: T :: T :: rest ->
            let (optimized, _) = templateMatch (Z :: T :: rest)
            (optimized, true)
        
        // Pattern: T³ = S·T (T² = S, so T³ = S·T)
        // Reduces T-count from 3 to 1
        | T :: T :: T :: rest ->
            let (optimized, _) = templateMatch (S :: T :: rest)
            (optimized, true)
        
        // Pattern: S³ = S† (S⁴ = I, so S³ = S⁻¹)
        // Reduces gate count from 3 to 1
        | S :: S :: S :: rest ->
            let (optimized, _) = templateMatch (SDagger :: rest)
            (optimized, true)
        
        | g :: rest ->
            let (optimized, changed) = templateMatch rest
            (g :: optimized, changed)
    
    /// Apply template matching repeatedly until stable
    let rec templateMatchUntilStable (gates: GateSequence) : GateSequence =
        let (optimized, changed) = templateMatch gates
        if changed then
            templateMatchUntilStable optimized
        else
            optimized
    
    // ========================================================================
    // FULL OPTIMIZATION PIPELINE
    // ========================================================================
    
    /// Count T gates in sequence
    let countTGates (gates: GateSequence) : int =
        gates |> List.filter isTGate |> List.length
    
    /// Calculate circuit depth (simplified - assumes all gates on same qubit)
    let calculateDepth (gates: GateSequence) : int =
        gates.Length
    
    /// Apply basic optimizations
    let optimizeBasic (gates: GateSequence) : GateSequence =
        gates
        |> cancelInverses
        |> mergeAdjacentGates
        |> cancelInverses  // Run again after merging
    
    /// Apply aggressive optimizations
    let optimizeAggressive (gates: GateSequence) : GateSequence =
        gates
        |> cancelInverses
        |> mergeAdjacentGates
        |> commuteCliffordsUntilStable
        |> templateMatchUntilStable
        |> cancelInverses
        |> mergeAdjacentGates
    
    /// Optimize with full pipeline and statistics
    let optimize (gates: GateSequence) (level: int) : GateSequence * OptimizationStats =
        let original = gates
        let originalTCount = countTGates original
        let originalDepth = calculateDepth original
        
        let (optimized, techniques) =
            match level with
            | 0 -> (gates, [])
            | 1 -> (optimizeBasic gates, ["Gate cancellation"; "Gate merging"])
            | _ -> (optimizeAggressive gates, 
                   ["Gate cancellation"; "Gate merging"; "Commutation-based reordering"; "Template matching"])
        
        let stats = {
            OriginalGateCount = original.Length
            OptimizedGateCount = optimized.Length
            OriginalTCount = originalTCount
            OptimizedTCount = countTGates optimized
            OriginalDepth = originalDepth
            OptimizedDepth = calculateDepth optimized
            OptimizationsApplied = techniques
        }
        
        (optimized, stats)
    
    /// Display optimization statistics
    let displayStats (stats: OptimizationStats) : string =
        let gateReduction = 
            if stats.OriginalGateCount > 0 then
                100.0 * float (stats.OriginalGateCount - stats.OptimizedGateCount) / float stats.OriginalGateCount
            else 0.0
        
        let tReduction =
            if stats.OriginalTCount > 0 then
                100.0 * float (stats.OriginalTCount - stats.OptimizedTCount) / float stats.OriginalTCount
            else 0.0
        
        let depthReduction =
            if stats.OriginalDepth > 0 then
                100.0 * float (stats.OriginalDepth - stats.OptimizedDepth) / float stats.OriginalDepth
            else 0.0
        
        $"""Circuit Optimization Results
━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
Original:  {stats.OriginalGateCount} gates, {stats.OriginalTCount} T gates, depth {stats.OriginalDepth}
Optimized: {stats.OptimizedGateCount} gates, {stats.OptimizedTCount} T gates, depth {stats.OptimizedDepth}

Reductions:
  Total gates: {gateReduction:F1}%%
  T-count:     {tReduction:F1}%%
  Depth:       {depthReduction:F1}%%

Optimizations applied:
{stats.OptimizationsApplied |> List.map (fun t -> $"  • {t}") |> String.concat "\n"}"""
