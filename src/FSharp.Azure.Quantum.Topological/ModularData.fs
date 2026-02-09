namespace FSharp.Azure.Quantum.Topological

/// Modular S and T matrices for topological quantum field theories
/// 
/// The modular data (S and T matrices) are fundamental invariants of a
/// topological quantum field theory (TQFT). They encode:
/// - S-matrix: Unlinking/threading operations on a torus
/// - T-matrix: Self-rotation (twist) eigenvalues
/// - Together: Define the modular tensor category (MTC)
/// 
/// Key properties (from Simon's Topological Quantum, Chapter 17):
/// - S is symmetric and unitary
/// - T is diagonal and unitary  
/// - S² = C (charge conjugation)
/// - (ST)³ = S²
/// - Ground state degeneracy on genus g: Dim = Tr(S^g)
/// 
/// References:
/// - Steven H. Simon, "Topological Quantum" (2023), Chapter 17
/// - Rowell & Wang, "Mathematics of Topological Quantum Computing"
[<RequireQualifiedAccess>]
module ModularData =
    
    open System
    open System.Numerics
    
    /// Modular data for a topological quantum field theory
    /// 
    /// Contains S and T matrices which are the fundamental topological
    /// invariants of an anyon theory. These matrices:
    /// - Determine ground state degeneracies
    /// - Encode fusion and braiding data
    /// - Satisfy consistency relations
    type ModularData = {
        /// Modular S-matrix (unlinking matrix)
        /// - Unitary: S S† = I
        /// - Symmetric: S = S^T
        /// - Relates basis on torus
        /// - S₀ₐ = dₐ/D (quantum dimensions)
        SMatrix: Complex[,]
        
        /// Modular T-matrix (twist matrix)
        /// - Diagonal: Tₐᵦ = δₐᵦ θₐ
        /// - Unitary: |θₐ| = 1
        /// - θₐ = exp(2πi hₐ) where hₐ is topological spin
        TMatrix: Complex[,]
        
        /// Central charge c (mod 8)
        /// - Related to chiral anomaly
        /// - Ising: c = 1/2
        /// - Fibonacci: c = 14/5
        CentralCharge: float
        
        /// Particle labels (ordering for matrix indices)
        Particles: AnyonSpecies.Particle list
    }
    
    /// Compute modular S-matrix for a given anyon type
    /// 
    /// The S-matrix describes the effect of unlinking/threading anyons
    /// on a torus. It is computed from the fusion rules.
    /// 
    /// For well-behaved (modular) theories, S diagonalizes the fusion
    /// matrices: S Nₐ S† = Λₐ (diagonal).
    /// 
    /// Returns Error if anyon type not supported.
    let rec computeSMatrix (anyonType: AnyonSpecies.AnyonType) 
        : TopologicalResult<Complex[,]> =
        
        match anyonType with
        | AnyonSpecies.Ising ->
            // Ising S-matrix from Simon Table 17.1
            // Particles: [Vacuum=1, Sigma=σ, Psi=ψ]
            // S = (1/2) * | 1    √2    1  |
            //             | √2    0   -√2 |
            //             | 1   -√2    1  |
            let sqrt2 = sqrt 2.0
            let s = Array2D.init 3 3 (fun i j ->
                // Array2D.init guarantees i,j are in bounds [0..2] for 3x3 matrix
                let value = 
                    match i, j with
                    | 0, 0 -> 1.0      | 0, 1 -> sqrt2  | 0, 2 -> 1.0
                    | 1, 0 -> sqrt2    | 1, 1 -> 0.0    | 1, 2 -> -sqrt2
                    | 2, 0 -> 1.0      | 2, 1 -> -sqrt2 | 2, 2 -> 1.0
                    | _ -> invalidOp $"Array2D.init contract violated: index ({i},{j}) out of bounds for 3x3 matrix"
                Complex(value / 2.0, 0.0))
            Ok s
        
        | AnyonSpecies.Fibonacci ->
            // Fibonacci S-matrix from Simon Table 17.1
            // Particles: [Vacuum=1, Tau=τ]
            // φ = golden ratio = (1+√5)/2
            // S = (1/√(2+φ)) * | 1   φ   |
            //                  | φ  -1   |
            let phi = (1.0 + sqrt 5.0) / 2.0  // Golden ratio
            let norm = sqrt (2.0 + phi)
            let s = Array2D.init 2 2 (fun i j ->
                // Array2D.init guarantees i,j are in bounds [0..1] for 2x2 matrix
                let value = 
                    match i, j with
                    | 0, 0 -> 1.0  | 0, 1 -> phi
                    | 1, 0 -> phi  | 1, 1 -> -1.0
                    | _ -> invalidOp $"Array2D.init contract violated: index ({i},{j}) out of bounds for 2x2 matrix"
                Complex(value / norm, 0.0))
            Ok s
        
        | AnyonSpecies.SU2Level k when k = 2 ->
            // SU(2)₂ is the same as Ising
            computeSMatrix AnyonSpecies.Ising
        
        | AnyonSpecies.SU2Level k ->
            // General SU(2)_k S-matrix using Verlinde/Kac-Peterson formula
            // For SU(2)_k, the S-matrix element is:
            //   S_{j1,j2} = sqrt(2/(k+2)) * sin(π(2j1+1)(2j2+1)/(k+2))
            // 
            // where j1, j2 ∈ {0, 1/2, 1, ..., k/2}
            //
            // Reference: Simon "Topological Quantum", Chapter 17.3 (Verlinde formula)
            
            // Get particle list
            match AnyonSpecies.particles anyonType with
            | Error err -> Error err
            | Ok particleList ->
                let dimension = particleList.Length
                
                // Extract spin value from particle
                let spinValue = function
                    | AnyonSpecies.Particle.SpinJ (j_doubled, _) -> float j_doubled / 2.0
                    | AnyonSpecies.Particle.Vacuum -> 0.0
                    | other -> invalidOp $"SU(2)_k S-matrix: particle {other} is not valid for SU(2) theory (expected Vacuum or SpinJ)"
                
                // Get list of j values
                let jValues =
                    particleList
                    |> List.map spinValue
                    |> List.toArray
                
                // Normalization factor
                let norm = sqrt (2.0 / float (k + 2))
                
                // Compute S-matrix using Kac-Peterson formula
                // S_{j1,j2} = sqrt(2/(k+2)) * sin(π(2j1+1)(2j2+1)/(k+2))
                let s = Array2D.init dimension dimension (fun i j ->
                    let j1 = jValues.[i]
                    let j2 = jValues.[j]
                    let argument = Math.PI * (2.0 * j1 + 1.0) * (2.0 * j2 + 1.0) / float (k + 2)
                    let value = norm * sin argument
                    Complex(value, 0.0))
                
                Ok s
    
    /// Compute modular T-matrix for a given anyon type
    /// 
    /// The T-matrix is diagonal with entries θₐ = exp(2πi hₐ)
    /// where hₐ is the topological spin (conformal dimension mod 1).
    /// 
    /// The topological spin determines the phase acquired when
    /// an anyon rotates by 2π (self-rotation).
    /// 
    /// Returns Error if anyon type not supported.
    let rec computeTMatrix (anyonType: AnyonSpecies.AnyonType) 
        : TopologicalResult<Complex[,]> =
        
        match anyonType with
        | AnyonSpecies.Ising ->
            // Ising T-matrix from Simon Table 17.1
            // Particles: [Vacuum=1, Sigma=σ, Psi=ψ]
            // Topological spins: h₁=0, h_σ=1/16, h_ψ=1/2
            // θ = exp(2πi h)
            let h1 = 0.0
            let hSigma = 1.0 / 16.0
            let hPsi = 1.0 / 2.0
            
            let theta h = 
                let phase = 2.0 * Math.PI * h
                Complex(cos phase, sin phase)
            
            let t = Array2D.init 3 3 (fun i j ->
                if i = j then
                    // Array2D.init guarantees i is in bounds [0..2] for 3x3 matrix
                    match i with
                    | 0 -> theta h1
                    | 1 -> theta hSigma
                    | 2 -> theta hPsi
                    | _ -> invalidOp $"Array2D.init contract violated: index {i} out of bounds for 3x3 matrix"
                else Complex.Zero)
            Ok t
        
        | AnyonSpecies.Fibonacci ->
            // Fibonacci T-matrix from Simon Table 17.1
            // Particles: [Vacuum=1, Tau=τ]
            // Topological spins: h₁=0, h_τ=2/5
            let h1 = 0.0
            let hTau = 2.0 / 5.0
            
            let theta h = 
                let phase = 2.0 * Math.PI * h
                Complex(cos phase, sin phase)
            
            let t = Array2D.init 2 2 (fun i j ->
                if i = j then
                    // Array2D.init guarantees i is in bounds [0..1] for 2x2 matrix
                    match i with
                    | 0 -> theta h1
                    | 1 -> theta hTau
                    | _ -> invalidOp $"Array2D.init contract violated: index {i} out of bounds for 2x2 matrix"
                else Complex.Zero)
            Ok t
        
        | AnyonSpecies.SU2Level k when k = 2 ->
            // SU(2)₂ is the same as Ising
            computeTMatrix AnyonSpecies.Ising
        
        | AnyonSpecies.SU2Level k ->
            // General SU(2)_k T-matrix using conformal field theory
            // For SU(2)_k, particles have j ∈ {0, 1/2, 1, ..., k/2}
            // Topological spin: h_j = j(j+1)/(k+2)  (conformal weight)
            // But we need h mod 1 for the phase θ_j = exp(2πi h_j)
            
            // Get particle list
            match AnyonSpecies.particles anyonType with
            | Error err -> Error err
            | Ok particleList ->
                let dimension = particleList.Length
                
                // Extract spin value from particle
                let spinValue = function
                    | AnyonSpecies.Particle.SpinJ (j_doubled, _) -> float j_doubled / 2.0
                    | AnyonSpecies.Particle.Vacuum -> 0.0
                    | other -> invalidOp $"SU(2)_k T-matrix: particle {other} is not valid for SU(2) theory (expected Vacuum or SpinJ)"
                
                // Compute topological spin (conformal weight) for each particle
                let topologicalSpins =
                    particleList
                    |> List.map (fun p ->
                        let j = spinValue p
                        j * (j + 1.0) / float (k + 2))
                    |> List.toArray
                
                // Create phase function
                let theta h = 
                    let phase = 2.0 * Math.PI * h
                    Complex(cos phase, sin phase)
                
                // T-matrix is diagonal with phases θ_j = exp(2πi h_j)
                let t = Array2D.init dimension dimension (fun i j ->
                    if i = j then theta topologicalSpins.[i] else Complex.Zero)
                
                Ok t
    
    /// Get central charge for anyon type
    /// 
    /// Central charge c determines the thermal Hall conductance
    /// and appears in modular transformations.
    /// 
    /// Returns Error if not implemented.
    let centralCharge (anyonType: AnyonSpecies.AnyonType) 
        : TopologicalResult<float> =
        
        match anyonType with
        | AnyonSpecies.Ising -> Ok 0.5           // c = 1/2
        | AnyonSpecies.Fibonacci -> Ok 2.8       // c = 14/5
        | AnyonSpecies.SU2Level 2 -> Ok 0.5      // Same as Ising
        | AnyonSpecies.SU2Level k -> 
            // General formula: c = 3k/(k+2)
            Ok (3.0 * float k / float (k + 2))
    
    /// Compute complete modular data for an anyon type
    /// 
    /// Returns S-matrix, T-matrix, central charge, and particle ordering.
    /// This is the complete set of modular invariants.
    let computeModularData (anyonType: AnyonSpecies.AnyonType) 
        : TopologicalResult<ModularData> =
        
        // Use Result.bind for railway-oriented programming
        computeSMatrix anyonType
        |> Result.bind (fun s ->
            computeTMatrix anyonType
            |> Result.bind (fun t ->
                centralCharge anyonType
                |> Result.bind (fun c ->
                    AnyonSpecies.particles anyonType
                    |> Result.map (fun particles ->
                        {
                            SMatrix = s
                            TMatrix = t
                            CentralCharge = c
                            Particles = particles
                        }))))
    
    /// Matrix multiplication helper (validates dimensions)
    let private matrixMultiply (a: Complex[,]) (b: Complex[,]) : Complex[,] =
        let aRows = Array2D.length1 a
        let aCols = Array2D.length2 a
        let bRows = Array2D.length1 b
        let bCols = Array2D.length2 b
        if aCols <> bRows then
            invalidArg "b" $"Matrix dimension mismatch: A is {aRows}×{aCols} but B is {bRows}×{bCols}"
        Array2D.init aRows bCols (fun i j ->
            [0 .. aCols - 1]
            |> List.fold (fun acc k -> acc + a.[i, k] * b.[k, j]) Complex.Zero)
    
    /// Verify S-matrix is unitary: S S† = I
    let verifySMatrixUnitary (s: Complex[,]) : bool =
        let n = Array2D.length1 s
        let sDagger = Array2D.init n n (fun i j -> Complex.Conjugate s.[j, i])
        
        // Compute S S†
        let product = matrixMultiply s sDagger
        
        // Check if it's identity (within tolerance)
        [0 .. n - 1]
        |> List.allPairs [0 .. n - 1]
        |> List.forall (fun (i, j) ->
            let expected = if i = j then Complex.One else Complex.Zero
            let diff = Complex.Abs(product.[i, j] - expected)
            diff <= 1e-10)
    
    /// Verify T-matrix is diagonal and unitary
    let verifyTMatrixDiagonal (t: Complex[,]) : bool =
        let n = Array2D.length1 t
        
        [0 .. n - 1]
        |> List.allPairs [0 .. n - 1]
        |> List.forall (fun (i, j) ->
            if i <> j then
                // Off-diagonal must be zero
                Complex.Abs(t.[i, j]) <= 1e-10
            else
                // Diagonal must have unit magnitude
                abs(Complex.Abs(t.[i, i]) - 1.0) <= 1e-10)
    
    /// Verify (ST)³ = S² (fundamental modular relation)
    /// 
    /// This is one of the key consistency conditions for modular data.
    /// From Simon Section 17.3.2.
    /// 
    /// Note: The relation holds up to a global phase: (ST)³ = e^(iφ) S²
    /// We verify by checking if all entries have the same phase ratio.
    let verifyModularSTRelation (s: Complex[,]) (t: Complex[,]) : bool =
        let n = Array2D.length1 s
        
        // Compute powers using helper function
        let st = matrixMultiply s t
        let st2 = matrixMultiply st st
        let st3 = matrixMultiply st2 st
        let s2 = matrixMultiply s s
        
        // Find global phase by looking at first non-zero entry
        let globalPhase =
            [0 .. n - 1]
            |> List.allPairs [0 .. n - 1]
            |> List.tryPick (fun (i, j) ->
                if Complex.Abs(s2.[i, j]) > 1e-10 then
                    Some (st3.[i, j] / s2.[i, j])
                else
                    None)
            |> Option.defaultValue Complex.One
        
        // Check if (ST)³ = globalPhase * S² for all entries
        [0 .. n - 1]
        |> List.allPairs [0 .. n - 1]
        |> List.forall (fun (i, j) ->
            let expected = globalPhase * s2.[i, j]
            let diff = Complex.Abs(st3.[i, j] - expected)
            diff <= 1e-10)
    
    /// Verify all modular data consistency conditions
    let verifyModularData (data: ModularData) : bool =
        verifySMatrixUnitary data.SMatrix &&
        verifyTMatrixDiagonal data.TMatrix &&
        verifyModularSTRelation data.SMatrix data.TMatrix
    
    /// Compute total quantum dimension D = √(Σ dₐ²)
    /// 
    /// This appears in the normalization of the S-matrix:
    /// S₀ₐ = dₐ/D
    let totalQuantumDimension (anyonType: AnyonSpecies.AnyonType) 
        : TopologicalResult<float> =
        
        topologicalResult {
            let! particles = AnyonSpecies.particles anyonType
            let sumSquares = 
                particles
                |> List.map AnyonSpecies.quantumDimension
                |> List.map (fun d -> d * d)
                |> List.sum
            return sqrt sumSquares
        }
    
    /// Compute ground state degeneracy on genus-g surface
    /// 
    /// From Simon Section 8.4 and 17.3:
    /// For genus g: Dim V(Σ_g) = Σₐ S₀ₐ^(2-2g)
    /// 
    /// Special cases:
    /// - g=0 (sphere): Dim = 1
    /// - g=1 (torus): Dim = number of particles
    /// - g=2: Dim = Σₐ (dₐ/D)⁻²
    let groundStateDegeneracy (data: ModularData) (genus: int) : TopologicalResult<int> =
        if genus < 0 then
            TopologicalResult.validationError "genus" "Genus must be non-negative"
        else
        
        let n = data.Particles.Length
        let s = data.SMatrix
        let power = 2 - 2 * genus
        
        // Sum over all particle types using functional approach
        [0 .. n - 1]
        |> List.sumBy (fun a ->
            let s0a = s.[0, a]  // S₀ₐ (first row)
            let contribution = Complex.Pow(s0a, float power)
            contribution.Real)
        |> round
        |> int
        |> Ok
