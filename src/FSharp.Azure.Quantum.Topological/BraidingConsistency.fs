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
        | AnyonSpecies.AnyonType.SU2Level k ->
            // General SU(2)_k: particles are spins j=0, 1/2, ..., k/2
            // represented as SpinJ(j_doubled, k) with j_doubled from 0 to k
            [0 .. k] |> List.map (fun j_doubled -> AnyonSpecies.Particle.SpinJ(j_doubled, k))
    
    /// Get fusion channels a×b, returning empty list when fusion is undefined.
    ///
    /// Returning [] on Error is intentional: FusionRules.channels returns Error for
    /// particle/type combinations where fusion is not defined (e.g., unsupported anyon
    /// types). In the verification loops, this correctly causes the combination to
    /// contribute zero terms — matching the mathematical convention that undefined
    /// fusion paths are absent from the sum.
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
                            // F-symbol lookup returned None — the index combination violates
                            // fusion constraints and does not correspond to a valid fusion tree.
                            // Skipping is correct: absent paths contribute nothing to the equation.
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
    
    /// Verify hexagon equation H1 for specific particles (a,b,c,d).
    ///
    /// Correct hexagon H1 (derived from nCat Lab braided monoidal category axiom):
    ///
    ///   Σ_f F^{bca}_{d;fg} · R^{af}_d · F^{abc}_{d;ef} = R^{ac}_g · F^{bac}_{d;eg} · R^{ab}_e
    ///
    /// where:
    ///   - a,b,c are the three fusing particles, d is the total charge
    ///   - e ∈ channels(a×b) with e×c→d valid (left intermediate of F^{abc})
    ///   - g ∈ channels(c×a) with b×g→d valid (right intermediate of F^{bca})
    ///   - f ∈ channels(b×c) with a×f→d valid (summed over on LHS)
    ///   - The THREE F-matrices have DIFFERENT first-three arguments: F^{bca}, F^{abc}, F^{bac}
    ///   - R^{af}_d is the R-symbol for braiding a past f with fusion channel d
    ///   - R^{ac}_g and R^{ab}_e appear unsummed on the RHS
    ///
    /// References:
    ///   - nCat Lab: https://ncatlab.org/nlab/show/braided+monoidal+category
    ///   - Kitaev, "Anyons in an exactly solved model" (2006), Appendix E
    ///   - Simon, "Topological Quantum" (2023), Section 13.3
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
        let channelsCA = fusionChannels c a anyonType
        
        // Valid e: e ∈ channels(a×b) with e×c→d
        let validE = channelsAB |> List.filter (fun e ->
            match FusionRules.isPossible e c d anyonType with
            | Ok true -> true
            | _ -> false
        )
        
        // Valid g: g ∈ channels(c×a) with b×g→d
        let validG = channelsCA |> List.filter (fun g ->
            match FusionRules.isPossible b g d anyonType with
            | Ok true -> true
            | _ -> false
        )
        
        // Valid f (for sum): f ∈ channels(b×c) with a×f→d
        let validF = channelsBC |> List.filter (fun f ->
            match FusionRules.isPossible a f d anyonType with
            | Ok true -> true
            | _ -> false
        )
        
        if validE.IsEmpty || validG.IsEmpty then
            // No valid fusion paths - equation trivially satisfied
            {
                Equation = $"Hexagon: {a},{b},{c}→{d}"
                IsSatisfied = true
                MaxDeviation = 0.0
                Details = "No valid fusion paths"
            }
        else
            let deviations =
                [
                    for e in validE do
                    for g in validG do
                        // LHS: Σ_f F^{bca}_{d;fg} · R^{af}_d · F^{abc}_{d;ef}
                        let lhsTerms =
                            validF
                            |> List.choose (fun f ->
                                match tryGetF fData (b,c,a,d,f,g),
                                      tryGetF fData (a,b,c,d,e,f) with
                                | Some fBCA, Some fABC ->
                                    let rAF_D = RMatrix.getRSymbol rData { RMatrix.A = a; RMatrix.B = f; RMatrix.C = d }
                                    match rAF_D with
                                    | Ok r -> Some (fBCA * r * fABC)
                                    | Error _ -> None
                                | _ -> None)
                        
                        let lhs = lhsTerms |> List.fold (+) Complex.Zero
                        
                        // RHS: R^{ac}_g · F^{bac}_{d;eg} · R^{ab}_e
                        let rhsOpt =
                            match tryGetF fData (b,a,c,d,e,g) with
                            | Some fBAC ->
                                let rAC_G = RMatrix.getRSymbol rData { RMatrix.A = a; RMatrix.B = c; RMatrix.C = g }
                                let rAB_E = RMatrix.getRSymbol rData { RMatrix.A = a; RMatrix.B = b; RMatrix.C = e }
                                match rAC_G, rAB_E with
                                | Ok r1, Ok r2 -> Some (r1 * fBAC * r2)
                                | _ -> None
                            | None -> None
                        
                        match rhsOpt with
                        | Some rhs ->
                            let deviation = (lhs - rhs).Magnitude
                            yield deviation
                        | None ->
                            // F-symbol or R-symbol lookup failed — the index combination
                            // violates fusion constraints and is not a valid braiding path.
                            // Skipping is correct: absent paths contribute nothing.
                            ()
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
