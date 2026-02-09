namespace FSharp.Azure.Quantum.Topological

/// Fusion rules for anyon combination
/// 
/// Fusion is the process of combining two anyons to produce a definite outcome.
/// Unlike classical particles, anyon fusion can be:
/// - Probabilistic: Multiple possible outcomes (non-abelian anyons)
/// - Deterministic: Single outcome (abelian anyons like Vacuum, Psi)
/// 
/// Mathematical structure:
/// - Forms a fusion algebra: a × b = Σ_c N^c_ab · c
/// - N^c_ab ∈ {0, 1, 2, ...} = fusion multiplicity (usually 0 or 1)
/// - Associative: (a × b) × c ≅ a × (b × c) via F-matrices
/// - Commutative: a × b = b × a (for all theories we implement)
[<RequireQualifiedAccess>]
module FusionRules =
    
    /// Fusion outcome represents one possible result of fusing two anyons
    /// 
    /// For non-abelian anyons (e.g., σ × σ), multiple outcomes are possible.
    /// The actual outcome is determined by measurement (projective measurement
    /// onto the fusion channel basis).
    type Outcome = {
        /// Resulting particle after fusion
        Result: AnyonSpecies.Particle
        
        /// Multiplicity (usually 1, but can be >1 for some theories)
        /// 
        /// N^c_ab = number of ways to fuse a × b → c
        /// 
        /// For Ising/Fibonacci: always 0 or 1 (multiplicity-free)
        /// For SU(3) or larger theories: can be 2, 3, etc.
        Multiplicity: int
    }
    
    /// Fuse two particles according to anyon type
    /// 
    /// Returns all possible fusion outcomes with their multiplicities.
    /// 
    /// Examples:
    /// - Ising: σ × σ = 1 + ψ (two outcomes with multiplicity 1 each)
    /// - Fibonacci: τ × τ = 1 + τ (two outcomes)
    /// - Ising: ψ × ψ = 1 (deterministic, single outcome)
    /// 
    /// Returns Error if particles are invalid for the given anyon type.
    let fuse 
        (a: AnyonSpecies.Particle) 
        (b: AnyonSpecies.Particle) 
        (anyonType: AnyonSpecies.AnyonType) 
        : TopologicalResult<Outcome list> =
        
        // Validate particles
        match AnyonSpecies.isValid anyonType a, AnyonSpecies.isValid anyonType b with
        | Error err, _ | _, Error err -> Error err
        | Ok false, _ | _, Ok false -> 
            TopologicalResult.validationError "particles" $"Invalid particles {a} or {b} for anyon type {anyonType}"
        | Ok true, Ok true ->
        
        match anyonType, a, b with
        // ========================================================================
        // ISING FUSION RULES (SU(2)₂)
        // ========================================================================
        
        // Vacuum is identity: 1 × a = a
        | AnyonSpecies.AnyonType.Ising, AnyonSpecies.Particle.Vacuum, x 
        | AnyonSpecies.AnyonType.Ising, x, AnyonSpecies.Particle.Vacuum ->
            Ok [{ Result = x; Multiplicity = 1 }]
        
        // Key non-abelian fusion: σ × σ = 1 + ψ
        // This creates a 2-dimensional Hilbert space → qubit!
        | AnyonSpecies.AnyonType.Ising, AnyonSpecies.Particle.Sigma, AnyonSpecies.Particle.Sigma ->
            Ok [
                { Result = AnyonSpecies.Particle.Vacuum; Multiplicity = 1 }
                { Result = AnyonSpecies.Particle.Psi; Multiplicity = 1 }
            ]
        
        // σ × ψ = σ (deterministic)
        | AnyonSpecies.AnyonType.Ising, AnyonSpecies.Particle.Sigma, AnyonSpecies.Particle.Psi 
        | AnyonSpecies.AnyonType.Ising, AnyonSpecies.Particle.Psi, AnyonSpecies.Particle.Sigma ->
            Ok [{ Result = AnyonSpecies.Particle.Sigma; Multiplicity = 1 }]
        
        // ψ × ψ = 1 (fermion pair annihilates)
        | AnyonSpecies.AnyonType.Ising, AnyonSpecies.Particle.Psi, AnyonSpecies.Particle.Psi ->
            Ok [{ Result = AnyonSpecies.Particle.Vacuum; Multiplicity = 1 }]
        
        // ========================================================================
        // FIBONACCI FUSION RULES
        // ========================================================================
        
        // Vacuum is identity: 1 × τ = τ
        | AnyonSpecies.AnyonType.Fibonacci, AnyonSpecies.Particle.Vacuum, x 
        | AnyonSpecies.AnyonType.Fibonacci, x, AnyonSpecies.Particle.Vacuum ->
            Ok [{ Result = x; Multiplicity = 1 }]
        
        // Key fusion rule: τ × τ = 1 + τ (Fibonacci recurrence!)
        | AnyonSpecies.AnyonType.Fibonacci, AnyonSpecies.Particle.Tau, AnyonSpecies.Particle.Tau ->
            Ok [
                { Result = AnyonSpecies.Particle.Vacuum; Multiplicity = 1 }
                { Result = AnyonSpecies.Particle.Tau; Multiplicity = 1 }
            ]
        
        // ========================================================================
        // INVALID COMBINATIONS
        // ========================================================================
        
        // Mixing Ising and Fibonacci particles is invalid
        | AnyonSpecies.AnyonType.Ising, AnyonSpecies.Particle.Tau, _ 
        | AnyonSpecies.AnyonType.Ising, _, AnyonSpecies.Particle.Tau ->
            TopologicalResult.validationError "particle" "Tau particle not valid for Ising anyon type"
        
        | AnyonSpecies.AnyonType.Fibonacci, AnyonSpecies.Particle.Sigma, _ 
        | AnyonSpecies.AnyonType.Fibonacci, _, AnyonSpecies.Particle.Sigma
        | AnyonSpecies.AnyonType.Fibonacci, AnyonSpecies.Particle.Psi, _ 
        | AnyonSpecies.AnyonType.Fibonacci, _, AnyonSpecies.Particle.Psi ->
            TopologicalResult.validationError "particle" "Sigma or Psi particles not valid for Fibonacci anyon type"
        
        // Mixing Ising/Fibonacci with SU(2)_k SpinJ particles is invalid
        | AnyonSpecies.AnyonType.Ising, AnyonSpecies.Particle.SpinJ _, _
        | AnyonSpecies.AnyonType.Ising, _, AnyonSpecies.Particle.SpinJ _ ->
            TopologicalResult.validationError "particle" "SpinJ particles not valid for Ising anyon type (use SU2Level instead)"
        
        | AnyonSpecies.AnyonType.Fibonacci, AnyonSpecies.Particle.SpinJ _, _
        | AnyonSpecies.AnyonType.Fibonacci, _, AnyonSpecies.Particle.SpinJ _ ->
            TopologicalResult.validationError "particle" "SpinJ particles not valid for Fibonacci anyon type (use SU2Level instead)"
        
        // ========================================================================
        // SU(2)_k FUSION RULES (GENERAL CASE)
        // ========================================================================
        
        // SU(2)_k fusion rule: j1 × j2 → j3
        // where |j1 - j2| ≤ j3 ≤ min(j1 + j2, k - j1 - j2) in steps of 1
        // 
        // Truncation condition: j1 + j2 + j3 ≤ k (in true spin units where
        // spins range from 0 to k/2). Since j values are already in spin units
        // (j = j_doubled / 2), the upper bound is k - j1 - j2 (NOT k/2 - j1 - j2).
        // 
        // Reference: "Topological Quantum" by Steven H. Simon, Chapter 17
        | AnyonSpecies.AnyonType.SU2Level k, AnyonSpecies.Particle.SpinJ (j1_doubled, _), AnyonSpecies.Particle.SpinJ (j2_doubled, _) ->
            let j1 = float j1_doubled / 2.0
            let j2 = float j2_doubled / 2.0
            let k_float = float k
            
            // Compute fusion bounds
            // j_min from triangle inequality, j_max from SU(2)_k truncation
            let j_min = abs (j1 - j2)
            let j_max = min (j1 + j2) (k_float - j1 - j2)
            
            // Check if fusion is allowed
            if j_max < j_min then
                Ok []  // No valid fusion channels
            else
                // SU(2)_k fusion outputs always step by 1 in spin units.
                // The parity of j1_doubled + j2_doubled determines whether
                // j3 starts at an integer or half-integer, but the step is always 1.
                let fusion_results = 
                    [j_min .. 1.0 .. j_max]
                    |> List.filter (fun j3 -> j3 <= k_float / 2.0)  // Ensure j3 ≤ k/2 (max spin)
                    |> List.map (fun j3 -> 
                        { Result = AnyonSpecies.Particle.SpinJ (int (j3 * 2.0), k); Multiplicity = 1 }
                    )
                Ok fusion_results
        
        // Handle vacuum specially for SU(2)_k
        | AnyonSpecies.AnyonType.SU2Level k, AnyonSpecies.Particle.Vacuum, AnyonSpecies.Particle.SpinJ (j, _)
        | AnyonSpecies.AnyonType.SU2Level k, AnyonSpecies.Particle.SpinJ (j, _), AnyonSpecies.Particle.Vacuum ->
            Ok [{ Result = AnyonSpecies.Particle.SpinJ (j, k); Multiplicity = 1 }]
        
        | AnyonSpecies.AnyonType.SU2Level k, AnyonSpecies.Particle.Vacuum, AnyonSpecies.Particle.Vacuum ->
            Ok [{ Result = AnyonSpecies.Particle.Vacuum; Multiplicity = 1 }]
        
        // Mixing particle types is invalid
        | AnyonSpecies.AnyonType.SU2Level _, _, _ ->
            TopologicalResult.validationError "particle" "Mixing Ising/Fibonacci particles with SU(2)_k SpinJ particles is invalid"
    
    /// Get fusion multiplicity N^c_ab
    /// 
    /// Returns the number of times particle 'c' appears in fusion a × b.
    /// 
    /// For multiplicity-free theories (Ising, Fibonacci): always 0 or 1
    /// Returns Error if particles are invalid for the given anyon type.
    let multiplicity 
        (a: AnyonSpecies.Particle) 
        (b: AnyonSpecies.Particle) 
        (c: AnyonSpecies.Particle) 
        (anyonType: AnyonSpecies.AnyonType) 
        : TopologicalResult<int> =
        
        fuse a b anyonType
        |> Result.map (fun outcomes ->
            outcomes
            |> List.filter (fun outcome -> outcome.Result = c)
            |> List.sumBy (fun outcome -> outcome.Multiplicity)
        )
    
    /// Check if fusion outcome is possible
    /// 
    /// Returns true if N^c_ab > 0
    /// Returns Error if particles are invalid for the given anyon type.
    let isPossible 
        (a: AnyonSpecies.Particle) 
        (b: AnyonSpecies.Particle) 
        (c: AnyonSpecies.Particle) 
        (anyonType: AnyonSpecies.AnyonType) 
        : TopologicalResult<bool> =
        
        multiplicity a b c anyonType
        |> Result.map (fun m -> m > 0)
    
    /// Get all possible fusion results (just the particles, not multiplicities)
    /// 
    /// Convenience function for when you don't care about multiplicities.
    /// Returns Error if particles are invalid for the given anyon type.
    let channels 
        (a: AnyonSpecies.Particle) 
        (b: AnyonSpecies.Particle) 
        (anyonType: AnyonSpecies.AnyonType) 
        : TopologicalResult<AnyonSpecies.Particle list> =
        
        fuse a b anyonType
        |> Result.map (fun outcomes ->
            outcomes
            |> List.map (fun outcome -> outcome.Result)
            |> List.distinct
        )
    
    /// Verify fusion algebra axioms (for testing)
    /// 
    /// Checks:
    /// 1. Identity: 1 × a = a
    /// 2. Commutativity: a × b = b × a
    /// 3. Existence of anti-particle: a × ā contains 1
    let verifyAlgebra (anyonType: AnyonSpecies.AnyonType) : TopologicalResult<unit> =
        match AnyonSpecies.particles anyonType with
        | Error err -> Error err
        | Ok allParticles ->
        
        // Check identity: 1 × a = a
        // For each particle, check if fusionwith vacuum gives itself
        let identityResults =
            allParticles
            |> List.map (fun a ->
                channels AnyonSpecies.Particle.Vacuum a anyonType
                |> Result.map (fun outcomes -> outcomes = [a])
            )
        
        // Find first Error or check if all are Ok true
        match identityResults |> List.tryFind Result.isError with
        | Some (Error err) -> Error err
        | _ ->
            let identityCheck = identityResults |> List.forall (fun r -> r = Ok true)
            if not identityCheck then
                Error (TopologicalError.Other "Identity axiom violated: 1 × a ≠ a for some a")
            else
                // Check commutativity: a × b = b × a
                let commutativityResults =
                    allParticles
                    |> List.allPairs allParticles
                    |> List.map (fun (a, b) ->
                        match channels a b anyonType, channels b a anyonType with
                        | Ok ab, Ok ba -> Ok (Set.ofList ab = Set.ofList ba)
                        | Error err, _ | _, Error err -> Error err
                    )
                
                match commutativityResults |> List.tryFind Result.isError with
                | Some (Error err) -> Error err
                | _ ->
                    let commutativityCheck = commutativityResults |> List.forall (fun r -> r = Ok true)
                    if not commutativityCheck then
                        Error (TopologicalError.Other "Commutativity violated: a × b ≠ b × a for some (a,b)")
                    else
                        // Check anti-particle exists: a × ā contains 1
                        let antiParticleResults =
                            allParticles
                            |> List.map (fun a ->
                                let anti = AnyonSpecies.antiParticle a
                                isPossible a anti AnyonSpecies.Particle.Vacuum anyonType
                            )
                        
                        match antiParticleResults |> List.tryFind Result.isError with
                        | Some (Error err) -> Error err
                        | _ ->
                            let antiParticleCheck = antiParticleResults |> List.forall (fun r -> r = Ok true)
                            if not antiParticleCheck then
                                Error (TopologicalError.Other "Anti-particle axiom violated: a × ā does not contain 1 for some a")
                            else
                                Ok ()
    
    /// Create fusion tensor N^c_ab as 3D array
    /// 
    /// Useful for:
    /// - Verifying associativity via Pentagon equation
    /// - Computing quantum dimensions
    /// - Solving for F-matrices
    /// 
    /// Returns: N[i,j,k] = N^k_ij where i,j,k index into particles list
    /// Returns Error if the anyon type is not yet implemented.
    let tensor (anyonType: AnyonSpecies.AnyonType) : TopologicalResult<int[,,]> =
        match AnyonSpecies.particles anyonType with
        | Error err -> Error err
        | Ok particleList ->
        let allParticles = particleList |> List.toArray
        let n = allParticles.Length
        
        // Build fusion tensor (fail fast on programming bugs with validated particles)
        // Compute all multiplicities first to handle potential errors
        let multiplicities =
            [| for i in 0 .. n-1 do
                for j in 0 .. n-1 do
                    for k in 0 .. n-1 do
                        let a = allParticles.[i]
                        let b = allParticles.[j]
                        let c = allParticles.[k]
                        match multiplicity a b c anyonType with
                        | Ok m -> Ok (i, j, k, m)
                        | Error err -> Error err
            |]
        
        // Check if any errors occurred
        match multiplicities |> Array.tryPick (function Error e -> Some e | Ok _ -> None) with
        | Some err -> Error err
        | None ->
            // All succeeded - build the 3D array
            let tensor = Array3D.create n n n 0
            multiplicities |> Array.iter (function 
                | Ok (i, j, k, m) -> tensor.[i, j, k] <- m
                | Error _ -> ()  // Already handled above
            )
            Ok tensor
