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
    
    /// Check if two complex numbers are approximately equal
    let private areComplexEqual (tolerance: float) (z1: Complex) (z2: Complex) : bool =
        let diff = z1 - z2
        diff.Magnitude < tolerance
    
    /// Get list of all particles for an anyon type
    let rec private getParticles (anyonType: AnyonSpecies.AnyonType) : AnyonSpecies.Particle list =
        match anyonType with
        | AnyonSpecies.AnyonType.Ising ->
            [
                AnyonSpecies.Particle.Vacuum
                AnyonSpecies.Particle.Sigma
                AnyonSpecies.Particle.Psi
            ]
        | AnyonSpecies.AnyonType.Fibonacci ->
            [
                AnyonSpecies.Particle.Vacuum
                AnyonSpecies.Particle.Tau
            ]
        | AnyonSpecies.AnyonType.SU2Level 2 ->
            getParticles AnyonSpecies.AnyonType.Ising  // Isomorphic
        | _ ->
            []  // Not implemented yet
    
    /// Extract fusion channels from Result, returning empty list on error
    let private toParticles (result: TopologicalResult<FusionRules.Outcome list>) : AnyonSpecies.Particle list =
        match result with
        | Ok outcomes -> outcomes |> List.map (fun (o: FusionRules.Outcome) -> o.Result)
        | Error _ -> []
    
    // ========================================================================
    // PENTAGON EQUATION VERIFICATION
    // ========================================================================
    
    /// Verify pentagon equation for specific particles (a,b,c,d,e)
    ///
    /// Pentagon equation:
    /// F[a,b,c;e] · F[a,e,d;f] = Σ_g F[b,c,d;g] · F[a,b,g;f] · F[g,c,d;f]
    ///
    /// This ensures associativity of fusion: ((a×b)×c)×d = a×(b×(c×d))
    let verifyPentagonForParticles
        (fData: FMatrix.FMatrixData)
        (a: AnyonSpecies.Particle)
        (b: AnyonSpecies.Particle)
        (c: AnyonSpecies.Particle)
        (d: AnyonSpecies.Particle)
        (e: AnyonSpecies.Particle)
        : ConsistencyCheckResult =
        
        let anyonType = fData.AnyonType
        
        // Get possible intermediate channels
        let channelsAB = FusionRules.fuse a b anyonType |> toParticles
        let channelsBC = FusionRules.fuse b c anyonType |> toParticles
        let channelsCD = FusionRules.fuse c d anyonType |> toParticles
        
        // For multiplicity-free theories, we can simplify the check
        // Left side: F[a,b,c;e] · F[a,e,d;f]
        // Right side: Σ_g F[b,c,d;g] · F[a,b,g;f] · F[g,c,d;f]
        
        // TODO: Full pentagon verification requires summing over all intermediate states.
        // For multiplicity-free theories this is a matrix multiplication check.
        // For now, mark as NOT verified (not implemented) rather than falsely reporting success.
        {
            Equation = $"Pentagon: F[{a},{b},{c};{e}] · F[{a},{e},{d};f]"
            IsSatisfied = false
            MaxDeviation = nan
            Details = "NOT IMPLEMENTED: Pentagon verification requires summing over intermediate fusion channels. Results should not be trusted until this is implemented."
        }
    
    /// Verify pentagon equation for all valid particle combinations
    let verifyAllPentagons (fData: FMatrix.FMatrixData) : ConsistencyCheckResult list =
        let particles = getParticles fData.AnyonType
        
        // Generate all valid 5-tuples (a,b,c,d,e)
        // Return actual per-combination results (currently all NOT IMPLEMENTED)
        [
            for a in particles do
            for b in particles do
            for c in particles do
            for d in particles do
            for e in particles ->
                verifyPentagonForParticles fData a b c d e
        ]
    
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
        let channelsAB = FusionRules.fuse a b anyonType |> toParticles
        let channelsBC = FusionRules.fuse b c anyonType |> toParticles
        
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
