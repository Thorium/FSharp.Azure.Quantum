namespace FSharp.Azure.Quantum.Topological

/// Braiding operators (R-matrices and F-matrices) for topological quantum computing
/// 
/// These matrices implement the algebra of anyon braiding and fusion basis transformations:
/// 
/// - **R-matrix**: Exchange (braiding) operator R^c_ab
///   - Acts when anyons a and b (fusing to c) are braided
///   - Phase accumulated depends on anyon types and fusion channel
///   - Satisfies hexagon equation with F-matrices
/// 
/// - **F-matrix**: Fusion basis transformation F^{abc}_d
///   - Changes associativity: (a × b) × c ↔ a × (b × c)
///   - Relates different fusion tree configurations
///   - Satisfies pentagon equation (consistency)
/// 
/// Mathematical reference: "Topological Quantum" by Steven H. Simon, Chapters 9-10
[<RequireQualifiedAccess>]
module BraidingOperators =
    
    open System
    open System.Numerics
    
    // ============================================================================
    // COMPLEX NUMBER HELPERS (from TopologicalHelpers)
    // ============================================================================
    
    let inline private polar r theta = TopologicalHelpers.polar r theta
    let inline private expI theta = TopologicalHelpers.expI theta
    let private π = TopologicalHelpers.π
    
    // ============================================================================
    // R-MATRIX (BRAIDING OPERATOR)
    // ============================================================================
    
    /// R-matrix element R^c_ab for braiding anyons a and b with fusion channel c
    /// 
    /// Physical interpretation:
    /// - R^c_ab = phase accumulated when anyon 'a' crosses over anyon 'b' clockwise
    /// - Counterclockwise braid: (R^c_ab)^(-1) = (R^c_ab)*
    /// 
    /// For Ising anyons (Kitaev 2006 convention):
    /// - R^1_σσ = exp(-iπ/8) (braiding two Majoranas to vacuum)
    /// - R^ψ_σσ = exp(3iπ/8) (braiding two Majoranas to fermion)
    /// 
    /// For Fibonacci anyons:
    /// - R^1_ττ = exp(4πi/5)
    /// - R^τ_ττ = exp(-3πi/5)
    let element 
        (a: AnyonSpecies.Particle) 
        (b: AnyonSpecies.Particle) 
        (c: AnyonSpecies.Particle) 
        (anyonType: AnyonSpecies.AnyonType) 
        : TopologicalResult<Complex> =
        
        // Verify fusion is possible
        match FusionRules.isPossible a b c anyonType with
        | Error err -> Error err
        | Ok false -> TopologicalResult.logicError "operation" $"Cannot fuse {a} × {b} → {c} in {anyonType} theory"
        | Ok true ->
        
        match anyonType, a, b, c with
        // ========================================================================
        // ISING R-MATRICES (SU(2)₂)
        // ========================================================================
        
        // Vacuum braiding is trivial
        | AnyonSpecies.AnyonType.Ising, AnyonSpecies.Particle.Vacuum, x, _ when c = x ->
            Ok Complex.One
        | AnyonSpecies.AnyonType.Ising, x, AnyonSpecies.Particle.Vacuum, _ when c = x ->
            Ok Complex.One
        
        // σ × σ → 1: R^1_σσ = e^(-iπ/8) (Kitaev convention)
        | AnyonSpecies.AnyonType.Ising, AnyonSpecies.Particle.Sigma, AnyonSpecies.Particle.Sigma, AnyonSpecies.Particle.Vacuum ->
            Ok (expI (-π / 8.0))
        
        // σ × σ → ψ: R^ψ_σσ = e^(3iπ/8)
        | AnyonSpecies.AnyonType.Ising, AnyonSpecies.Particle.Sigma, AnyonSpecies.Particle.Sigma, AnyonSpecies.Particle.Psi ->
            Ok (expI (3.0 * π / 8.0))
        
        // σ × ψ → σ: R^σ_σψ = e^(-iπ/2) = -i
        | AnyonSpecies.AnyonType.Ising, AnyonSpecies.Particle.Sigma, AnyonSpecies.Particle.Psi, AnyonSpecies.Particle.Sigma 
        | AnyonSpecies.AnyonType.Ising, AnyonSpecies.Particle.Psi, AnyonSpecies.Particle.Sigma, AnyonSpecies.Particle.Sigma ->
            Ok (expI (-π / 2.0))
        
        // ψ × ψ → 1: R^1_ψψ = -1 (fermion statistics!)
        | AnyonSpecies.AnyonType.Ising, AnyonSpecies.Particle.Psi, AnyonSpecies.Particle.Psi, AnyonSpecies.Particle.Vacuum ->
            Ok (Complex(-1.0, 0.0))
        
        // ========================================================================
        // FIBONACCI R-MATRICES
        // ========================================================================
        
        // Vacuum braiding
        | AnyonSpecies.AnyonType.Fibonacci, AnyonSpecies.Particle.Vacuum, x, _ when c = x ->
            Ok Complex.One
        | AnyonSpecies.AnyonType.Fibonacci, x, AnyonSpecies.Particle.Vacuum, _ when c = x ->
            Ok Complex.One
        
        // τ × τ → 1: R^1_ττ = e^(4πi/5)
        | AnyonSpecies.AnyonType.Fibonacci, AnyonSpecies.Particle.Tau, AnyonSpecies.Particle.Tau, AnyonSpecies.Particle.Vacuum ->
            Ok (expI (4.0 * π / 5.0))
        
        // τ × τ → τ: R^τ_ττ = e^(-3πi/5)
        | AnyonSpecies.AnyonType.Fibonacci, AnyonSpecies.Particle.Tau, AnyonSpecies.Particle.Tau, AnyonSpecies.Particle.Tau ->
            Ok (expI (-3.0 * π / 5.0))
        
        // ========================================================================
        // UNRECOGNIZED CASES
        // ========================================================================
        
        | _ ->
            TopologicalResult.logicError "R-matrix"
                $"No R-matrix element defined for {a} × {b} → {c} in {anyonType} theory"
    
    /// Get full R-matrix for a given pair (a,b) and anyon type
    /// 
    /// Returns a matrix R where R[i,j] corresponds to:
    /// - Initial state: fusion channel c_i
    /// - Final state: fusion channel c_j
    /// - Usually diagonal (R[i,i] = phase, R[i,j≠i] = 0)
    /// 
    /// For multiplicity-free theories (Ising, Fibonacci), this is a diagonal matrix.
    /// Returns Error if particles are invalid or fusion is not possible.
    let matrix 
        (a: AnyonSpecies.Particle) 
        (b: AnyonSpecies.Particle) 
        (anyonType: AnyonSpecies.AnyonType) 
        : TopologicalResult<Complex[,]> =
        
        match FusionRules.channels a b anyonType with
        | Error err -> Error err
        | Ok fusionChannels ->
        let n = fusionChannels.Length
        
        // Create diagonal matrix with R-matrix elements
        // Pre-compute all R-matrix elements (propagate errors properly)
        let rElementResults = 
            fusionChannels 
            |> List.map (fun channel ->
                element a b channel anyonType
            )
        
        // Check if any errors occurred
        match rElementResults |> List.tryPick (function Error e -> Some e | Ok _ -> None) with
        | Some err -> Error err
        | None ->
            let rElements = rElementResults |> List.choose (function Ok elem -> Some elem | Error _ -> None) |> List.toArray
            
            // Build diagonal matrix from pre-computed elements
            Ok (Array2D.init n n (fun i j ->
                if i = j then rElements.[i] else Complex.Zero
            ))
    
    // ============================================================================
    // F-MATRIX (FUSION BASIS TRANSFORMATION)
    // ============================================================================
    
    /// F-matrix element F^{abc}_d for changing fusion basis
    /// 
    /// Physical interpretation:
    /// - Transforms between (a × b) × c and a × (b × c) fusion orderings
    /// - Matrix element: [F^{abc}_d]_{e,f} where:
    ///   - e = intermediate fusion channel in (a × b) × c
    ///   - f = intermediate fusion channel in a × (b × c)
    ///   - d = total fusion outcome
    /// 
    /// Pentagon equation (consistency):
    /// F^{abc}_e · F^{ade}_f = Σ_g F^{bcd}_g · F^{abg}_f · F^{gce}_f
    let matrixElement 
        (a: AnyonSpecies.Particle) 
        (b: AnyonSpecies.Particle) 
        (c: AnyonSpecies.Particle) 
        (d: AnyonSpecies.Particle)
        (e: AnyonSpecies.Particle) 
        (f: AnyonSpecies.Particle)
        (anyonType: AnyonSpecies.AnyonType) 
        : TopologicalResult<Complex> =
        
        // Verify that both fusion paths are valid:
        // Path 1: a × b → e, then e × c → d
        // Path 2: b × c → f, then a × f → d
        match FusionRules.isPossible a b e anyonType, FusionRules.isPossible e c d anyonType,
              FusionRules.isPossible b c f anyonType, FusionRules.isPossible a f d anyonType with
        | Error err, _, _, _ | _, Error err, _, _ | _, _, Error err, _ | _, _, _, Error err -> Error err
        | Ok path1a, Ok path1b, Ok path2a, Ok path2b ->
        
        if not (path1a && path1b) then
            Ok Complex.Zero
        elif not (path2a && path2b) then
            Ok Complex.Zero
        else
            match anyonType with
            // ====================================================================
            // ISING F-MATRICES
            // ====================================================================
            
            | AnyonSpecies.AnyonType.Ising ->
                match a, b, c, d, e, f with
                // Vacuum in any position gives identity
                | AnyonSpecies.Particle.Vacuum, _, _, _, _, _ 
                | _, AnyonSpecies.Particle.Vacuum, _, _, _, _
                | _, _, AnyonSpecies.Particle.Vacuum, _, _, _ ->
                    if e = b && f = b && d = b then Ok Complex.One
                    elif e = b && f = c && d = c then Ok Complex.One
                    elif e = c && f = b && d = b then Ok Complex.One
                    else Ok Complex.Zero
                
                // σσσ case - the interesting one!
                //
                // In the Ising theory, σ×σ×σ always fuses to σ, and the non-trivial
                // basis change is:
                //   F^{σσσ}_σ = (1/√2) * [[1, 1], [1, -1]]
                // in the {1, ψ} intermediate-channel basis.
                | AnyonSpecies.Particle.Sigma, AnyonSpecies.Particle.Sigma, AnyonSpecies.Particle.Sigma, finalResult, intermediate1, intermediate2 ->
                    match finalResult, intermediate1, intermediate2 with
                    | AnyonSpecies.Particle.Sigma, AnyonSpecies.Particle.Vacuum, AnyonSpecies.Particle.Vacuum ->
                        Ok (Complex(0.5 * sqrt 2.0, 0.0))
                    | AnyonSpecies.Particle.Sigma, AnyonSpecies.Particle.Vacuum, AnyonSpecies.Particle.Psi ->
                        Ok (Complex(0.5 * sqrt 2.0, 0.0))
                    | AnyonSpecies.Particle.Sigma, AnyonSpecies.Particle.Psi, AnyonSpecies.Particle.Vacuum ->
                        Ok (Complex(0.5 * sqrt 2.0, 0.0))
                    | AnyonSpecies.Particle.Sigma, AnyonSpecies.Particle.Psi, AnyonSpecies.Particle.Psi ->
                        Ok (Complex(-0.5 * sqrt 2.0, 0.0))
                    | _ ->
                        Ok Complex.Zero
                
                // Other simple cases
                | _ ->
                    match FusionRules.isPossible a b e anyonType, FusionRules.isPossible e c d anyonType with
                    | Ok true, Ok true when e = f -> Ok Complex.One
                    | _ -> Ok Complex.Zero
            
            // ====================================================================
            // FIBONACCI F-MATRICES
            // ====================================================================
            
            | AnyonSpecies.AnyonType.Fibonacci ->
                let phi = (1.0 + sqrt 5.0) / 2.0  // Golden ratio
                let sqrtPhi = sqrt phi
                
                match a, b, c, d, e, f with
                // Vacuum cases
                | AnyonSpecies.Particle.Vacuum, _, _, _, _, _ 
                | _, AnyonSpecies.Particle.Vacuum, _, _, _, _
                | _, _, AnyonSpecies.Particle.Vacuum, _, _, _ ->
                    if e = b && f = b && d = b then Ok Complex.One
                    elif e = b && f = c && d = c then Ok Complex.One
                    elif e = c && f = b && d = b then Ok Complex.One
                    else Ok Complex.Zero
                
                // F^{τττ}_1 matrix (1×1: only e=τ, f=τ is fusion-valid)
                // The other 3 combinations (e=1 or f=1) are rejected by the fusion
                // validator above, so only the (τ,τ) entry is reachable.
                // F[τ,τ,τ,1;τ,τ] = φ⁻¹
                | AnyonSpecies.Particle.Tau, AnyonSpecies.Particle.Tau, AnyonSpecies.Particle.Tau, 
                  AnyonSpecies.Particle.Vacuum, AnyonSpecies.Particle.Tau, AnyonSpecies.Particle.Tau ->
                    Ok (Complex(1.0 / phi, 0.0))
                
                // F^{τττ}_τ matrix
                // From Simon "Topological Quantum", Table 9.5:
                //   F^{τττ}_τ = [[φ⁻¹, φ⁻¹/²], [φ⁻¹/², -φ⁻¹]]
                // in the {1,τ} intermediate-channel basis.
                | AnyonSpecies.Particle.Tau, AnyonSpecies.Particle.Tau, AnyonSpecies.Particle.Tau, 
                  AnyonSpecies.Particle.Tau, AnyonSpecies.Particle.Vacuum, AnyonSpecies.Particle.Vacuum ->
                    Ok (Complex(1.0 / phi, 0.0))
                
                | AnyonSpecies.Particle.Tau, AnyonSpecies.Particle.Tau, AnyonSpecies.Particle.Tau, 
                  AnyonSpecies.Particle.Tau, AnyonSpecies.Particle.Vacuum, AnyonSpecies.Particle.Tau ->
                    Ok (Complex(1.0 / sqrtPhi, 0.0))
                
                | AnyonSpecies.Particle.Tau, AnyonSpecies.Particle.Tau, AnyonSpecies.Particle.Tau, 
                  AnyonSpecies.Particle.Tau, AnyonSpecies.Particle.Tau, AnyonSpecies.Particle.Vacuum ->
                    Ok (Complex(1.0 / sqrtPhi, 0.0))
                
                | AnyonSpecies.Particle.Tau, AnyonSpecies.Particle.Tau, AnyonSpecies.Particle.Tau, 
                  AnyonSpecies.Particle.Tau, AnyonSpecies.Particle.Tau, AnyonSpecies.Particle.Tau ->
                    Ok (Complex(-1.0 / phi, 0.0))
                
                | _ -> Ok Complex.Zero
            
            | _ ->
                TopologicalResult.notImplemented
                    "F-matrix"
                    (Some $"F-matrix not implemented for anyon type {anyonType}")
    
    /// Get full F-matrix for (a,b,c) fusion to d
    /// 
    /// Returns matrix F where F[i,j] = [F^{abc}_d]_{e_i, f_j}
    /// - i indexes intermediate channels e in (a × b) × c
    /// - j indexes intermediate channels f in a × (b × c)
    let fusionBasisChange 
        (a: AnyonSpecies.Particle) 
        (b: AnyonSpecies.Particle) 
        (c: AnyonSpecies.Particle) 
        (d: AnyonSpecies.Particle) 
        (anyonType: AnyonSpecies.AnyonType) 
        : TopologicalResult<Complex[,]> =
        
        // Get possible intermediate channels
        match FusionRules.channels a b anyonType, FusionRules.channels b c anyonType with
        | Error err, _ | _, Error err -> Error err
        | Ok channelsAB, Ok channelsBC ->
        
        // Filter to valid channels (need to handle Result in filter)
        let validEResults = 
            channelsAB |> List.map (fun e -> 
                FusionRules.isPossible e c d anyonType |> Result.map (fun isPoss -> (e, isPoss))
            )
        let validFResults = 
            channelsBC |> List.map (fun f -> 
                FusionRules.isPossible a f d anyonType |> Result.map (fun isPoss -> (f, isPoss))
            )
        
        // Check for errors in validation
        match List.tryFind Result.isError validEResults, List.tryFind Result.isError validFResults with
        | Some (Error err), _ | _, Some (Error err) -> Error err
        | _ ->
        
        let validE = validEResults |> List.choose (fun r -> match r with Ok (e, true) -> Some e | _ -> None)
        let validF = validFResults |> List.choose (fun r -> match r with Ok (f, true) -> Some f | _ -> None)
        
        let nE = validE.Length
        let nF = validF.Length
        
        // Convert to arrays for efficient indexed access
        let validEArray = List.toArray validE
        let validFArray = List.toArray validF
        
        // Build F-matrix by computing each element (propagate errors properly)
        let matrixElementResults =
            [| for i in 0 .. nE-1 do
                for j in 0 .. nF-1 do
                    match matrixElement a b c d validEArray.[i] validFArray.[j] anyonType with
                    | Ok elem -> Ok (i, j, elem)
                    | Error err -> Error err
            |]
        
        // Check if any errors occurred
        match matrixElementResults |> Array.tryPick (function Error e -> Some e | Ok _ -> None) with
        | Some err -> Error err
        | None ->
            // All succeeded - build the 2D array
            let matrix = Array2D.create nE nF Complex.Zero
            matrixElementResults |> Array.iter (function 
                | Ok (i, j, elem) -> matrix.[i, j] <- elem
                | Error _ -> ()  // Already handled above
            )
            Ok matrix
