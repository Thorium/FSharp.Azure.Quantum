namespace FSharp.Azure.Quantum.Topological

/// Braiding Consistency Module - Verifies fundamental equations relating F and R matrices
///
/// This module verifies the mathematical consistency conditions that must hold for
/// any valid topological quantum field theory:
///
/// 1. **Pentagon Equation** (F-matrices only):
///    F[a,b,c;e] · F[a,e,d;f] = Σ_g F[b,c,d;g] · F[a,b,g;f] · F[g,c,d;f]
///
/// 2. **Hexagon Equation** (F and R matrices):
///    R[b,c;f] · F[a,b,c;d;e,f] · R[a,c;d] = 
///        Σ_g F[b,a,c;d;g,f] · R[a,b;g] · F[a,b,c;d;e,g]
///
/// These equations ensure that different ways of computing the same physical
/// quantity (e.g., braiding then fusing vs fusing then braiding) give the same result.
///
/// Physical Interpretation:
/// - Pentagon: Associativity of fusion is consistent
/// - Hexagon: Braiding and fusion operations commute properly
///
/// Mathematical Reference:
/// - Turaev "Quantum Invariants of Knots and 3-Manifolds" (1994)
/// - Bakalov & Kirillov "Lectures on Tensor Categories" (2001)
module BraidingConsistency =
    
    open System.Numerics
    
    // ========================================================================
    // TYPES
    // ========================================================================
    
    /// Result of consistency check
    type ConsistencyCheckResult = {
        Equation: string
        IsSatisfied: bool
        MaxDeviation: float
        Details: string
    }
    
    /// Summary of all consistency checks for an anyon type
    type ConsistencySummary = {
        AnyonType: AnyonSpecies.AnyonType
        PentagonChecks: ConsistencyCheckResult list
        HexagonChecks: ConsistencyCheckResult list
        AllSatisfied: bool
    }
    
    // ========================================================================
    // HELPERS
    // ========================================================================
    
    /// Get all particles in a theory, returning empty list for unsupported types
    let rec private getParticles (anyonType: AnyonSpecies.AnyonType) : AnyonSpecies.Particle list =
        match anyonType with
        | AnyonSpecies.AnyonType.Ising ->
            [ AnyonSpecies.Particle.Vacuum
              AnyonSpecies.Particle.Sigma
              AnyonSpecies.Particle.Psi ]
        | AnyonSpecies.AnyonType.Fibonacci ->
            [ AnyonSpecies.Particle.Vacuum
              AnyonSpecies.Particle.Tau ]
        | AnyonSpecies.AnyonType.SU2Level 2 ->
            getParticles AnyonSpecies.AnyonType.Ising
        | _ -> []
    
    /// Get fusion channels a×b, returning empty list on error
    let private fusionChannels
        (a: AnyonSpecies.Particle)
        (b: AnyonSpecies.Particle)
        (anyonType: AnyonSpecies.AnyonType)
        : AnyonSpecies.Particle list =
        match FusionRules.channels a b anyonType with
        | Ok channels -> channels
        | Error _ -> []
    
    /// Look up F-symbol value, returning None if fusion constraints are violated
    let private tryGetF
        (fData: FMatrix.FMatrixData)
        (a, b, c, d, e, f)
        : Complex option =
        match FMatrix.getFSymbol fData { FMatrix.A = a; FMatrix.B = b; FMatrix.C = c; FMatrix.D = d; FMatrix.E = e; FMatrix.F = f } with
        | Ok value -> Some value
        | Error _ -> None
    
    // ========================================================================
    // PENTAGON EQUATION VERIFICATION
    // ========================================================================
    
    /// Verify the pentagon equation for four fusing anyons (a,b,c,d) with total charge e.
    ///
    /// The pentagon identity (Kitaev 2006, Appendix C) states that two distinct
    /// sequences of F-moves relating fusion trees of four anyons must agree:
    ///
    ///   F^{fcd}_{e;gl} · F^{abl}_{e;fh} = Σ_k F^{abc}_{g;fk} · F^{akd}_{e;gh} · F^{bcd}_{h;kl}
    ///
    /// In our FSymbolIndex convention F[A,B,C,D;E,F] = F^{ABC}_{D;EF}, where
    /// (A×B)→E is the left intermediate channel and (B×C)→F the right intermediate.
    ///
    /// Free indices: f ∈ a×b, g ∈ f×c, l ∈ c×d, h ∈ b×l.
    /// Summed index: k ∈ b×c.
    let verifyPentagonForParticles
        (fData: FMatrix.FMatrixData)
        (a: AnyonSpecies.Particle)
        (b: AnyonSpecies.Particle)
        (c: AnyonSpecies.Particle)
        (d: AnyonSpecies.Particle)
        (e: AnyonSpecies.Particle)
        : ConsistencyCheckResult =
        
        let anyonType = fData.AnyonType
        let tolerance = 1e-10
        
        let channelsAB = fusionChannels a b anyonType
        let channelsBC = fusionChannels b c anyonType
        let channelsCD = fusionChannels c d anyonType
        
        // For each valid combination of free indices (f,g,l,h), compare LHS and RHS
        let deviations =
            [ for f in channelsAB do
                for g in fusionChannels f c anyonType do
                    for l in channelsCD do
                        for h in fusionChannels b l anyonType do
                            // LHS: F[f,c,d,e; g,l] · F[a,b,l,e; f,h]
                            match tryGetF fData (f,c,d,e,g,l), tryGetF fData (a,b,l,e,f,h) with
                            | Some lhs1, Some lhs2 ->
                                let lhs = lhs1 * lhs2
                                
                                // RHS: Σ_k F[a,b,c,g; f,k] · F[a,k,d,e; g,h] · F[b,c,d,h; k,l]
                                let rhs =
                                    channelsBC
                                    |> List.choose (fun k ->
                                        match tryGetF fData (a,b,c,g,f,k),
                                              tryGetF fData (a,k,d,e,g,h),
                                              tryGetF fData (b,c,d,h,k,l) with
                                        | Some v1, Some v2, Some v3 -> Some (v1 * v2 * v3)
                                        | _ -> None)
                                    |> List.fold (+) Complex.Zero
                                
                                (lhs - rhs).Magnitude
                            | _ -> () ]
        
        let label = $"Pentagon({a},{b},{c},{d};{e})"
        
        match deviations with
        | [] ->
            { Equation = label
              IsSatisfied = true
              MaxDeviation = 0.0
              Details = "No valid fusion paths" }
        | _ ->
            let maxDev = List.max deviations
            let satisfied = deviations |> List.forall (fun d -> d < tolerance)
            { Equation = label
              IsSatisfied = satisfied
              MaxDeviation = maxDev
              Details =
                  if satisfied then
                      $"Verified {deviations.Length} index combinations (max deviation: {maxDev:E3})"
                  else
                      $"FAILED: max deviation {maxDev:E3} exceeds tolerance {tolerance:E3}" }
    
    /// Verify pentagon equation for all particle 5-tuples (a,b,c,d,e) in the theory.
    /// Returns one ConsistencyCheckResult per combination.
    let verifyAllPentagons (fData: FMatrix.FMatrixData) : ConsistencyCheckResult list =
        let particles = getParticles fData.AnyonType
        [ for a in particles do
          for b in particles do
          for c in particles do
          for d in particles do
          for e in particles ->
              verifyPentagonForParticles fData a b c d e ]
    
    // ========================================================================
    // HEXAGON EQUATION VERIFICATION
    // ========================================================================
    
    /// Verify hexagon equation for specific particles (a,b,c,d)
    ///
    /// Hexagon equation:
    /// R[b,c;f] · F[a,b,c;d;e,f] · R[a,c;d] = 
    ///     Σ_g F[b,a,c;d;g,f] · R[a,b;g] · F[a,b,c;d;e,g]
    ///
    /// This ensures braiding and fusion commute properly.
    let verifyHexagonForParticles
        (fData: FMatrix.FMatrixData)
        (rData: RMatrix.RMatrixData)
        (a: AnyonSpecies.Particle)
        (b: AnyonSpecies.Particle)
        (c: AnyonSpecies.Particle)
        (d: AnyonSpecies.Particle)
        : ConsistencyCheckResult =
        
        let anyonType = fData.AnyonType
        let tolerance = 1e-10
        
        // Get possible intermediate fusion channels
        let channelsAB = fusionChannels a b anyonType
        let channelsBC = fusionChannels b c anyonType
        
        // For multiplicity-free theories (Ising, Fibonacci), hexagon simplifies
        // We need to check: R[b,c] · F[a,b,c;d] · R[a,c] = F'[b,a,c;d] · R[a,b] · F[a,b,c;d]
        
        // Get fusion outcomes for a×b→e and b×c→f
        let validE = channelsAB |> List.filter (fun e ->
            match FusionRules.isPossible e c d anyonType with
            | Ok true -> true
            | _ -> false
        )
        
        let validF = channelsBC |> List.filter (fun f ->
            match FusionRules.isPossible a f d anyonType with
            | Ok true -> true
            | _ -> false
        )
        
        if validE.IsEmpty || validF.IsEmpty then
            // No valid fusion paths - equation trivially satisfied
            {
                Equation = $"Hexagon: {a},{b},{c}→{d}"
                IsSatisfied = true
                MaxDeviation = 0.0
                Details = "No valid fusion paths"
            }
        else
            // For multiplicity-free case, compute both sides
            let deviations =
                [
                    for e in validE do
                    for f in validF do
                        // Left side: R[b,c;f] · F[a,b,c;d;e,f] · R[a,c;d]
                        let rbcf = RMatrix.getRSymbol rData { RMatrix.A = b; RMatrix.B = c; RMatrix.C = f }
                        let fabc = FMatrix.getFSymbol fData { FMatrix.A = a; FMatrix.B = b; FMatrix.C = c; FMatrix.D = d; FMatrix.E = e; FMatrix.F = f }
                        let racd = RMatrix.getRSymbol rData { RMatrix.A = a; RMatrix.B = c; RMatrix.C = d }
                        
                        match rbcf, fabc, racd with
                        | Ok r1, Ok fVal, Ok r2 ->
                            let leftSide = r1 * fVal * r2
                            
                            // Right side: Σ_g F[b,a,c;d;g,f] · R[a,b;g] · F[a,b,c;d;e,g]
                            // For multiplicity-free, sum over possible g (intermediate channels from a×b)
                            let rightSide =
                                channelsAB
                                |> List.map (fun g ->
                                    let fbac = FMatrix.getFSymbol fData { FMatrix.A = b; FMatrix.B = a; FMatrix.C = c; FMatrix.D = d; FMatrix.E = g; FMatrix.F = f }
                                    let rabg = RMatrix.getRSymbol rData { RMatrix.A = a; RMatrix.B = b; RMatrix.C = g }
                                    let fabc2 = FMatrix.getFSymbol fData { FMatrix.A = a; FMatrix.B = b; FMatrix.C = c; FMatrix.D = d; FMatrix.E = e; FMatrix.F = g }
                                    
                                    match fbac, rabg, fabc2 with
                                    | Ok f1, Ok r, Ok f2 -> Some (f1 * r * f2)
                                    | _ -> None
                                )
                                |> List.choose id
                                |> List.fold (+) Complex.Zero
                            
                            let deviation = (leftSide - rightSide).Magnitude
                            yield deviation
                        | _ -> ()
                ]
            
            let maxDeviation = if deviations.IsEmpty then 0.0 else List.max deviations
            let allSatisfied = deviations |> List.forall (fun d -> d < tolerance)
            
            {
                Equation = $"Hexagon: {a},{b},{c}→{d}"
                IsSatisfied = allSatisfied
                MaxDeviation = maxDeviation
                Details = if allSatisfied then "Satisfied within tolerance" else $"Deviation: {maxDeviation}"
            }
    
    /// Verify hexagon equation for all valid particle combinations
    let verifyAllHexagons 
        (fData: FMatrix.FMatrixData) 
        (rData: RMatrix.RMatrixData) 
        : ConsistencyCheckResult list =
        
        if fData.AnyonType <> rData.AnyonType then
            [
                {
                    Equation = "Hexagon equation (type mismatch)"
                    IsSatisfied = false
                    MaxDeviation = infinity
                    Details = $"F-matrix type {fData.AnyonType} ≠ R-matrix type {rData.AnyonType}"
                }
            ]
        else
            let particles = getParticles fData.AnyonType
            
            // Check all valid 4-tuples (a,b,c,d)
            [
                for a in particles do
                for b in particles do
                for c in particles do
                for d in particles ->
                    verifyHexagonForParticles fData rData a b c d
            ]
    
    // ========================================================================
    // FULL CONSISTENCY CHECK
    // ========================================================================
    
    /// Perform complete consistency check for an anyon type
    let verifyConsistency (anyonType: AnyonSpecies.AnyonType) : TopologicalResult<ConsistencySummary> =
        // Compute F and R matrices
        match FMatrix.computeFMatrix anyonType, RMatrix.computeRMatrix anyonType with
        | Error err, _ | _, Error err -> Error err
        | Ok fData, Ok rData ->
        
        // Verify pentagon equations
        let pentagonChecks = verifyAllPentagons fData
        
        // Verify hexagon equations
        let hexagonChecks = verifyAllHexagons fData rData
        
        // Check if all tests passed
        let allPentagonsSatisfied = pentagonChecks |> List.forall (fun c -> c.IsSatisfied)
        let allHexagonsSatisfied = hexagonChecks |> List.forall (fun c -> c.IsSatisfied)
        
        Ok {
            AnyonType = anyonType
            PentagonChecks = pentagonChecks
            HexagonChecks = hexagonChecks
            AllSatisfied = allPentagonsSatisfied && allHexagonsSatisfied
        }
    
    // ========================================================================
    // DISPLAY UTILITIES
    // ========================================================================
    
    /// Display consistency check summary
    let displayConsistencySummary (summary: ConsistencySummary) : string =
        let header = $"Consistency Verification for {summary.AnyonType} Anyons\n"
        let overall = if summary.AllSatisfied then "✓ ALL CHECKS PASSED" else "✗ SOME CHECKS FAILED"
        
        let pentagons =
            summary.PentagonChecks
            |> List.map (fun check ->
                let status = if check.IsSatisfied then "✓" else "✗"
                $"  {status} {check.Equation} (dev: {check.MaxDeviation:E3})"
            )
            |> String.concat "\n"
        
        let hexagons =
            summary.HexagonChecks
            |> List.filter (fun c -> not (c.Details.Contains("No valid")))  // Filter trivial cases
            |> List.map (fun check ->
                let status = if check.IsSatisfied then "✓" else "✗"
                $"  {status} {check.Equation} (dev: {check.MaxDeviation:E3})"
            )
            |> String.concat "\n"
        
        let pentagonSection = if pentagons = "" then "" else $"\nPentagon Equations:\n{pentagons}"
        let hexagonSection = if hexagons = "" then "" else $"\nHexagon Equations:\n{hexagons}"
        
        header + overall + pentagonSection + hexagonSection
