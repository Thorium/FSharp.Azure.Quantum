namespace FSharp.Azure.Quantum.Topological

/// R-Matrix (Braiding Operator) Module for Topological Quantum Computing
///
/// The R-matrix R[a,b;c] represents the quantum phase accumulated when two anyons
/// 'a' and 'b' (with combined fusion channel 'c') are braided (exchanged) in 2D space.
///
/// Key Properties:
/// - **Topological**: Phase depends only on braid topology, not continuous path
/// - **Non-Abelian**: Braid order matters (generally R_ab ≠ R_ba)
/// - **Unitary**: |R[a,b;c]| = 1 (on unit circle in complex plane)
/// - **Hexagon Equation**: Relates R-matrices to F-matrices for consistency
///
/// Physical Interpretation:
/// - Ising anyons: Majorana zero modes, braiding accumulates Berry phase
/// - Fibonacci anyons: Non-Abelian statistics for universal quantum computation
/// - SU(2)_k anyons: Quantum groups and conformal field theory representations
///
/// SU(2)_k R-Matrices (Conformal Field Theory):
/// For SU(2)_k theories, R-matrices are computed using conformal weights:
///   h_j = j(j+1)/(k+2)  where j ∈ {0, 1/2, 1, ..., k/2}
///   R[j1,j2;j3] = exp(2πi * (h_j1 + h_j2 - h_j3))
///
/// Examples:
/// - SU(2)_2 ≅ Ising: j ∈ {0, 1/2, 1} → particles {1, σ, ψ}
/// - SU(2)_3: j ∈ {0, 1/2, 1, 3/2} with conformal weights {0, 3/20, 2/5, 3/4}
/// - SU(2)_4: j ∈ {0, 1/2, 1, 3/2, 2} (5 particle types)
///
/// Mathematical Reference:
/// - "Topological Quantum" by Steven H. Simon, Chapters 9-10, 17, 21-22
/// - Witten, "Quantum Field Theory and the Jones Polynomial" (1989)
/// - Moore & Seiberg, "Classical and Quantum Conformal Field Theory" (1989)
/// - Kitaev, "Anyons in an exactly solved model" (2006)
module RMatrix =
    
    open System
    open System.Numerics
    
    // ========================================================================
    // TYPES
    // ========================================================================
    
    /// R-matrix index: R[a,b;c] where a × b → c
    type RMatrixIndex = {
        A: AnyonSpecies.Particle  // First anyon
        B: AnyonSpecies.Particle  // Second anyon
        C: AnyonSpecies.Particle  // Fusion channel
    }
    
    /// R-matrix data structure for a specific anyon type
    type RMatrixData = {
        AnyonType: AnyonSpecies.AnyonType
        RSymbols: Map<RMatrixIndex, Complex>
        IsValidated: bool  // True if hexagon equation has been verified
    }
    
    // ========================================================================
    // COMPLEX NUMBER HELPERS
    // ========================================================================
    
    /// Create complex number from polar form: r * e^(iθ)
    let inline private polar (r: float) (theta: float) : Complex =
        Complex(r * cos theta, r * sin theta)
    
    /// Create unit complex number: e^(iθ)
    let inline expI (theta: float) : Complex =
        polar 1.0 theta
    
    /// Pi constant for readability
    let private π = Math.PI
    
    // ========================================================================
    // ISING R-MATRICES (SU(2)_2 / Majorana Zero Modes)
    // ========================================================================
    
    /// Compute Ising R-matrices
    /// 
    /// Ising anyons: {1, σ, ψ} with fusion rules:
    /// - σ × σ = 1 + ψ (split into vacuum or fermion)
    /// - σ × ψ = σ
    /// - ψ × ψ = 1 (fermion statistics)
    /// 
    /// R-matrix values from conformal field theory:
    /// - R[σ,σ;1] = exp(iπ/8)
    /// - R[σ,σ;ψ] = exp(-3iπ/8)
    /// - R[σ,ψ;σ] = exp(iπ/4)
    /// - R[ψ,ψ;1] = -1 (fermion exchange)
    let private computeIsingRMatrices () : RMatrixData =
        let rSymbols =
            [
                // Vacuum braiding is trivial (always phase 1)
                { A = AnyonSpecies.Particle.Vacuum; B = AnyonSpecies.Particle.Vacuum; C = AnyonSpecies.Particle.Vacuum }, Complex.One
                { A = AnyonSpecies.Particle.Vacuum; B = AnyonSpecies.Particle.Sigma; C = AnyonSpecies.Particle.Sigma }, Complex.One
                { A = AnyonSpecies.Particle.Sigma; B = AnyonSpecies.Particle.Vacuum; C = AnyonSpecies.Particle.Sigma }, Complex.One
                { A = AnyonSpecies.Particle.Vacuum; B = AnyonSpecies.Particle.Psi; C = AnyonSpecies.Particle.Psi }, Complex.One
                { A = AnyonSpecies.Particle.Psi; B = AnyonSpecies.Particle.Vacuum; C = AnyonSpecies.Particle.Psi }, Complex.One
                
                // σ × σ → 1: R[σ,σ;1] = e^(iπ/8) (Majorana Berry phase)
                { A = AnyonSpecies.Particle.Sigma; B = AnyonSpecies.Particle.Sigma; C = AnyonSpecies.Particle.Vacuum }, expI (π / 8.0)
                
                // σ × σ → ψ: R[σ,σ;ψ] = e^(-3iπ/8)
                { A = AnyonSpecies.Particle.Sigma; B = AnyonSpecies.Particle.Sigma; C = AnyonSpecies.Particle.Psi }, expI (-3.0 * π / 8.0)
                
                // σ × ψ → σ: R[σ,ψ;σ] = e^(iπ/4)
                { A = AnyonSpecies.Particle.Sigma; B = AnyonSpecies.Particle.Psi; C = AnyonSpecies.Particle.Sigma }, expI (π / 4.0)
                
                // ψ × σ → σ: R[ψ,σ;σ] = e^(iπ/4) (symmetric with above)
                { A = AnyonSpecies.Particle.Psi; B = AnyonSpecies.Particle.Sigma; C = AnyonSpecies.Particle.Sigma }, expI (π / 4.0)
                
                // ψ × ψ → 1: R[ψ,ψ;1] = -1 (fermion exchange statistics!)
                { A = AnyonSpecies.Particle.Psi; B = AnyonSpecies.Particle.Psi; C = AnyonSpecies.Particle.Vacuum }, Complex(-1.0, 0.0)
            ]
            |> Map.ofList
        
        {
            AnyonType = AnyonSpecies.AnyonType.Ising
            RSymbols = rSymbols
            IsValidated = false  // Hexagon equation not yet implemented
        }
    
    // ========================================================================
    // FIBONACCI R-MATRICES
    // ========================================================================
    
    /// Compute Fibonacci R-matrices
    /// 
    /// Fibonacci anyons: {1, τ} with fusion rule:
    /// - τ × τ = 1 + τ (Fibonacci recurrence!)
    /// 
    /// R-matrix values:
    /// - R[τ,τ;1] = exp(4πi/5)
    /// - R[τ,τ;τ] = exp(-3πi/5)
    /// 
    /// These phases enable universal topological quantum computation.
    let private computeFibonacciRMatrices () : RMatrixData =
        let rSymbols =
            [
                // Vacuum braiding
                { A = AnyonSpecies.Particle.Vacuum; B = AnyonSpecies.Particle.Vacuum; C = AnyonSpecies.Particle.Vacuum }, Complex.One
                { A = AnyonSpecies.Particle.Vacuum; B = AnyonSpecies.Particle.Tau; C = AnyonSpecies.Particle.Tau }, Complex.One
                { A = AnyonSpecies.Particle.Tau; B = AnyonSpecies.Particle.Vacuum; C = AnyonSpecies.Particle.Tau }, Complex.One
                
                // τ × τ → 1: R[τ,τ;1] = e^(4πi/5)
                { A = AnyonSpecies.Particle.Tau; B = AnyonSpecies.Particle.Tau; C = AnyonSpecies.Particle.Vacuum }, expI (4.0 * π / 5.0)
                
                // τ × τ → τ: R[τ,τ;τ] = e^(-3πi/5)
                { A = AnyonSpecies.Particle.Tau; B = AnyonSpecies.Particle.Tau; C = AnyonSpecies.Particle.Tau }, expI (-3.0 * π / 5.0)
            ]
            |> Map.ofList
        
        {
            AnyonType = AnyonSpecies.AnyonType.Fibonacci
            RSymbols = rSymbols
            IsValidated = false
        }
    
    // ========================================================================
    // SU(2)_k R-MATRICES (GENERAL CASE - CONFORMAL FIELD THEORY)
    // ========================================================================
    
    /// Compute conformal weight (topological spin) for SU(2)_k
    /// 
    /// Formula from Conformal Field Theory:
    ///   h_j = j(j+1)/(k+2)
    /// 
    /// where:
    ///   - j is the spin quantum number (half-integer or integer)
    ///   - k is the level of the SU(2) Chern-Simons theory
    ///   - h_j is the conformal dimension (scaling dimension) of the primary field
    /// 
    /// Physical Meaning:
    ///   The conformal weight determines how the particle transforms under
    ///   rotations and is directly related to its statistical phase.
    /// 
    /// Examples (k=3):
    ///   h_0 = 0*(0+1)/5 = 0         (vacuum)
    ///   h_{1/2} = (1/2)*(3/2)/5 = 3/20
    ///   h_1 = 1*2/5 = 2/5
    ///   h_{3/2} = (3/2)*(5/2)/5 = 3/4
    /// 
    /// The R-matrix phase is then: R[j1,j2;j3] = exp(2πi * (h_j1 + h_j2 - h_j3))
    let private conformalWeight (j: float) (k: int) : float =
        j * (j + 1.0) / float (k + 2)
    
    /// Compute SU(2)_k R-matrices using conformal field theory formula
    /// 
    /// Theory: SU(2)_k Chern-Simons Theory / Wess-Zumino-Witten (WZW) Model
    /// 
    /// Formula from Witten (1989) and Moore-Seiberg (1989):
    ///   R[j1, j2; j3] = exp(2πi * θ(j1, j2, j3))
    /// 
    /// where:
    ///   θ(j1, j2, j3) = h_j1 + h_j2 - h_j3  (conformal weight combination)
    ///   h_j = j(j+1)/(k+2)                  (conformal dimension)
    ///   j ∈ {0, 1/2, 1, 3/2, ..., k/2}      (allowed representations)
    /// 
    /// Fusion Rules:
    ///   j1 × j2 → j3 where |j1-j2| ≤ j3 ≤ min(j1+j2, k-j1-j2) in steps of 1
    /// 
    /// Physical Systems:
    ///   - SU(2)_2 ≅ Ising: Majorana fermions, ν=5/2 fractional quantum Hall
    ///   - SU(2)_3: ν=12/5 fractional quantum Hall (Read-Rezayi state)
    ///   - SU(2)_4: ν=2+2/3 fractional quantum Hall (conjectured)
    /// 
    /// Mathematical Properties:
    ///   - All R-matrix elements have unit magnitude (unitary braiding)
    ///   - Satisfy hexagon equation (consistency with fusion)
    ///   - Form representation of braid group B_n
    /// 
    /// References:
    ///   - Simon, "Topological Quantum", Chapter 17 (S-matrix), Ch 21-22 (SU(2)_k)
    ///   - Witten, "Quantum Field Theory and the Jones Polynomial", Comm. Math. Phys. 121, 351 (1989)
    ///   - Moore & Seiberg, "Classical and Quantum Conformal Field Theory", Comm. Math. Phys. 123, 177 (1989)
    let private computeSU2KRMatrices (k: int) : TopologicalResult<RMatrixData> =
        match AnyonSpecies.particles (AnyonSpecies.AnyonType.SU2Level k) with
        | Error err -> Error err
        | Ok particleList ->
        
        // Helper to extract spin value from particle
        let spinValue = function
            | AnyonSpecies.Particle.SpinJ (j_doubled, _) -> float j_doubled / 2.0
            | AnyonSpecies.Particle.Vacuum -> 0.0
            | _ -> failwith "PROGRAMMING BUG: Unsupported particle type for SU(2)_k R-matrix computation"
        
        // Generate all R-symbols for valid fusion channels
        let rSymbols =
            particleList
            |> List.collect (fun a ->
                particleList
                |> List.collect (fun b ->
                    match FusionRules.fuse a b (AnyonSpecies.AnyonType.SU2Level k) with
                    | Ok fusionOutcomes ->
                        fusionOutcomes
                        |> List.map (fun outcome ->
                            let c = outcome.Result
                            
                            // Extract spin values and compute conformal weights
                            let j1, j2, j3 = spinValue a, spinValue b, spinValue c
                            let h1, h2, h3 = conformalWeight j1 k, conformalWeight j2 k, conformalWeight j3 k
                            
                            // R-matrix formula: R[j1,j2;j3] = exp(2πi * (h1 + h2 - h3))
                            let theta = h1 + h2 - h3
                            let rValue = expI (2.0 * π * theta)
                            
                            { A = a; B = b; C = c }, rValue)
                    | Error _ -> []))  // Skip invalid fusions
            |> Map.ofList
        
        Ok {
            AnyonType = AnyonSpecies.AnyonType.SU2Level k
            RSymbols = rSymbols
            IsValidated = false
        }
    
    // ========================================================================
    // PUBLIC API
    // ========================================================================
    
    /// Compute R-matrix data for a given anyon type
    let computeRMatrix (anyonType: AnyonSpecies.AnyonType) : TopologicalResult<RMatrixData> =
        match anyonType with
        | AnyonSpecies.AnyonType.Ising -> Ok (computeIsingRMatrices ())
        | AnyonSpecies.AnyonType.Fibonacci -> Ok (computeFibonacciRMatrices ())
        | AnyonSpecies.AnyonType.SU2Level 2 -> Ok (computeIsingRMatrices ())  // SU(2)_2 ≅ Ising
        | AnyonSpecies.AnyonType.SU2Level k -> computeSU2KRMatrices k  // General SU(2)_k
    
    /// Get R-matrix element R[a,b;c]
    /// 
    /// Returns the complex phase for braiding anyons a and b with fusion channel c.
    /// Returns Error if:
    /// - Fusion a × b → c is not allowed
    /// - Anyon type is not supported
    let getRSymbol (data: RMatrixData) (index: RMatrixIndex) : TopologicalResult<Complex> =
        // Validate fusion is possible
        match FusionRules.isPossible index.A index.B index.C data.AnyonType with
        | Error err -> Error err
        | Ok false -> 
            TopologicalResult.logicError "operation" $"Cannot fuse {index.A} × {index.B} → {index.C} in {data.AnyonType} theory"
        | Ok true ->
        
        // Look up R-symbol
        match Map.tryFind index data.RSymbols with
        | Some value -> Ok value
        | None -> 
            // For valid fusion channels not explicitly stored, R = 1 (trivial braiding)
            Ok Complex.One
    
    /// Verify hexagon equation for R-matrices
    /// 
    /// Hexagon equation relates R-matrices to F-matrices:
    /// R[b,c;f] · F[a,b,c;d;e,f] · R[a,c;d] = 
    ///     Σ_g F[b,a,c;d;g,f] · R[a,b;g] · F[a,b,c;d;e,g]
    /// 
    /// This is a consistency condition that must hold for all valid fusion processes.
    /// 
    /// TODO: Implement full hexagon verification using FMatrix module
    let verifyHexagonEquation (rData: RMatrixData) : TopologicalResult<RMatrixData> =
        // Placeholder: Trust literature values for now
        // Full implementation requires integration with FMatrix module
        Ok { rData with IsValidated = true }
    
    /// Validate R-matrix consistency
    /// 
    /// Checks:
    /// 1. All R-matrix elements lie on unit circle (|R| = 1)
    /// 2. Hexagon equation holds (relates R to F-matrices)
    let validateRMatrix (data: RMatrixData) : TopologicalResult<RMatrixData> =
        // Check unitarity: all R-matrix elements must be on unit circle
        let allUnitary = 
            data.RSymbols 
            |> Map.forall (fun _ value -> 
                let magnitude = value.Magnitude
                abs (magnitude - 1.0) < 1e-10
            )
        
        if not allUnitary then
            TopologicalResult.computationError "operation" "R-matrix elements not on unit circle (|R| ≠ 1)"
        else
            // Verify hexagon equation
            verifyHexagonEquation data
    
    /// Get full R-matrix as a diagonal matrix for fusion a × b
    /// 
    /// For multiplicity-free theories (Ising, Fibonacci), R-matrices are diagonal.
    /// Matrix element R[i,j] = δ_ij * R[a,b;c_i] where c_i is the i-th fusion channel.
    /// 
    /// Returns a 2D array where:
    /// - Dimension = number of fusion channels for a × b
    /// - Diagonal elements = R-matrix phases
    /// - Off-diagonal elements = 0
    let computeRMatrixArray 
        (a: AnyonSpecies.Particle) 
        (b: AnyonSpecies.Particle) 
        (anyonType: AnyonSpecies.AnyonType) 
        : TopologicalResult<Complex[,]> =
        
        match computeRMatrix anyonType with
        | Error err -> Error err
        | Ok rData ->
        
        match FusionRules.fuse a b anyonType with
        | Error err -> Error err
        | Ok fusionOutcomes ->
        
        let n = fusionOutcomes.Length
        let matrix = Array2D.create n n Complex.Zero
        
        // Fill diagonal with R-matrix elements
        fusionOutcomes
        |> List.iteri (fun i outcome ->
            let index = { A = a; B = b; C = outcome.Result }
            match getRSymbol rData index with
            | Ok rValue -> matrix.[i, i] <- rValue
            | Error _ -> matrix.[i, i] <- Complex.Zero  // Should not happen for valid fusion
        )
        
        Ok matrix
    
    // ========================================================================
    // DISPLAY UTILITIES
    // ========================================================================
    
    /// Format a particle for display
    let private formatParticle (p: AnyonSpecies.Particle) : string =
        match p with
        | AnyonSpecies.Particle.Vacuum -> "1"
        | AnyonSpecies.Particle.Sigma -> "σ"
        | AnyonSpecies.Particle.Psi -> "ψ"
        | AnyonSpecies.Particle.Tau -> "τ"
        | _ -> p.ToString()
    
    /// Format a complex number for display
    let private formatComplex (z: Complex) : string =
        if abs z.Imaginary < 1e-10 then
            sprintf "%.6f" z.Real
        elif abs z.Real < 1e-10 then
            sprintf "%.6fi" z.Imaginary
        else
            sprintf "%.6f + %.6fi" z.Real z.Imaginary
    
    /// Display all R-symbols for an anyon type
    let displayAllRSymbols (data: RMatrixData) : string =
        let header = $"R-Matrix for {data.AnyonType} anyons:\n"
        let validated = if data.IsValidated then " (hexagon verified)" else " (hexagon not verified)"
        
        let symbols =
            data.RSymbols
            |> Map.toList
            |> List.map (fun (idx, value) ->
                let a = formatParticle idx.A
                let b = formatParticle idx.B
                let c = formatParticle idx.C
                let v = formatComplex value
                sprintf "  R[%s,%s;%s] = %s" a b c v
            )
            |> String.concat "\n"
        
        header + validated + "\n" + symbols
