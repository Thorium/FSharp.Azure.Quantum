namespace FSharp.Azure.Quantum.Topological

/// Topological Entanglement Entropy
/// 
/// The topological entanglement entropy γ is a key diagnostic for topological order.
/// It appears in the area law for entanglement entropy:
/// 
///   S(A) = α|∂A| - γ
/// 
/// where:
/// - S(A) is the von Neumann entropy of region A
/// - |∂A| is the perimeter of region A
/// - α is a non-universal constant
/// - γ is the topological entropy = log(D)
/// - D is the total quantum dimension
/// 
/// Key properties:
/// - γ is independent of geometry (topological invariant)
/// - γ = 0 for trivial phases
/// - γ > 0 indicates long-range entanglement
/// - γ quantifies "topological order"
/// 
/// References:
/// - Kitaev & Preskill (2006): "Topological entanglement entropy"
/// - Levin & Wen (2006): "Detecting topological order in a ground state wave function"
/// - Simon, "Topological Quantum" (2023), Chapter 34
[<RequireQualifiedAccess>]
module EntanglementEntropy =
    
    open System
    
    // ========================================================================
    // TOPOLOGICAL ENTROPY
    // ========================================================================
    
    /// Compute topological entanglement entropy γ = log(D)
    /// 
    /// D is the total quantum dimension: D = √(Σ_a d_a²)
    /// 
    /// For common theories:
    /// - Ising (Z₂ × Z₂): D = 2 → γ = log(2) ≈ 0.693
    /// - Fibonacci: D = φ² ≈ 2.618 → γ = log(φ²) ≈ 0.962 where φ = (1+√5)/2
    /// - SU(2)₃: D = 2 → γ = log(2) ≈ 0.693
    /// 
    /// Returns Error if the anyon type is not yet implemented.
    let topologicalEntropy (anyonType: AnyonSpecies.AnyonType) : TopologicalResult<float> =
        AnyonSpecies.totalQuantumDimension anyonType
        |> Result.map log
    
    /// Get topological entropy in natural log (base e)
    let topologicalEntropyNat (anyonType: AnyonSpecies.AnyonType) : TopologicalResult<float> =
        topologicalEntropy anyonType
    
    /// Get topological entropy in log base 2 (bits)
    /// 
    /// Useful for information-theoretic interpretations:
    /// γ_bits = number of "topological qubits" hidden in the ground state
    let topologicalEntropyLog2 (anyonType: AnyonSpecies.AnyonType) : TopologicalResult<float> =
        topologicalEntropy anyonType
        |> Result.map (fun gamma -> gamma / log 2.0)
    
    // ========================================================================
    // KITAEV-PRESKILL CONSTRUCTION
    // ========================================================================
    
    /// Kitaev-Preskill topological entropy extraction
    /// 
    /// For regions A, B, C on a 2D surface:
    ///   γ = S(A) + S(B) + S(C) - S(AB) - S(BC) - S(CA) + S(ABC)
    /// 
    /// This combination cancels the area law term, leaving only γ.
    /// 
    /// For the toric code on a torus:
    ///   S(region) = volume law for small regions
    ///   γ can be extracted by carefully choosing regions
    type KitaevPreskillRegions = {
        /// Entropy of region A
        S_A: float
        
        /// Entropy of region B
        S_B: float
        
        /// Entropy of region C
        S_C: float
        
        /// Entropy of union AB
        S_AB: float
        
        /// Entropy of union BC
        S_BC: float
        
        /// Entropy of union CA
        S_CA: float
        
        /// Entropy of union ABC
        S_ABC: float
    }
    
    /// Extract topological entropy using Kitaev-Preskill formula
    /// 
    /// Result should equal -γ (note the minus sign convention).
    /// 
    /// In practice:
    /// - Positive result → topological order present
    /// - Zero result → trivial phase
    /// - Should match theoretical γ = log(D)
    let kitaevPreskill (regions: KitaevPreskillRegions) : float =
        regions.S_A + regions.S_B + regions.S_C 
        - regions.S_AB - regions.S_BC - regions.S_CA 
        + regions.S_ABC
    
    // ========================================================================
    // LEVIN-WEN CONSTRUCTION
    // ========================================================================
    
    /// Levin-Wen topological entropy extraction (alternative method)
    /// 
    /// For a disk-like region A with smooth boundary:
    ///   S(A) = α|∂A| - γ + ...
    /// 
    /// By comparing regions of different sizes, can extract γ.
    /// 
    /// Simpler than Kitaev-Preskill but requires smooth boundary.
    type LevinWenRegions = {
        /// Entropy of smaller region
        S_small: float
        
        /// Perimeter of smaller region
        Perimeter_small: float
        
        /// Entropy of larger region (similar shape)
        S_large: float
        
        /// Perimeter of larger region
        Perimeter_large: float
    }
    
    /// Extract topological entropy using Levin-Wen method
    /// 
    /// Assumes linear scaling: S = α|∂A| - γ
    /// 
    /// Returns: (α, γ) where α is the area law coefficient and γ is topological entropy
    let levinWen (regions: LevinWenRegions) : float * float =
        // Solve linear system:
        // S_small = α * P_small - γ
        // S_large = α * P_large - γ
        // Subtracting: S_large - S_small = α * (P_large - P_small)
        
        let deltaS = regions.S_large - regions.S_small
        let deltaP = regions.Perimeter_large - regions.Perimeter_small
        
        let alpha = 
            if abs deltaP > 1e-10 then
                deltaS / deltaP
            else
                0.0  // Degenerate case
        
        // From S = α * P - γ, we rearrange to get: γ = α * P - S
        let gamma = alpha * regions.Perimeter_small - regions.S_small
        
        (alpha, gamma)
    
    // ========================================================================
    // VON NEUMANN ENTROPY (for fusion tree states)
    // ========================================================================
    
    /// Von Neumann entropy S = -Tr(ρ log ρ)
    /// 
    /// For a pure state |ψ⟩, the reduced density matrix ρ_A for subsystem A is:
    ///   ρ_A = Tr_B(|ψ⟩⟨ψ|)
    /// 
    /// The entropy measures entanglement between A and B.
    /// 
    /// Note: This is a placeholder for future implementation.
    /// Full implementation requires:
    /// 1. Reduced density matrix calculation (partial trace)
    /// 2. Eigenvalue decomposition
    /// 3. Entropy formula: S = -Σ_i λ_i log(λ_i)
    type VonNeumannEntropyCalculation = {
        /// Eigenvalues of reduced density matrix
        Eigenvalues: float list
        
        /// Total entropy
        Entropy: float
        
        /// Entropy in bits (log₂)
        EntropyBits: float
    }
    
    /// Calculate von Neumann entropy from eigenvalues
    /// 
    /// S = -Σ_i λ_i log(λ_i)
    /// 
    /// Handles edge cases:
    /// - λ_i = 0: contributes 0 (using limit λ log λ → 0)
    /// - λ_i < 0: returns Error (unphysical)
    /// - Σ λ_i ≠ 1: returns Error (not normalized)
    let vonNeumannEntropyFromEigenvalues 
        (eigenvalues: float list) 
        : TopologicalResult<VonNeumannEntropyCalculation> =
        
        // Validate eigenvalues
        if eigenvalues |> List.exists (fun lambda -> lambda < -1e-10) then
            TopologicalResult.validationError "eigenvalues" "Eigenvalues must be non-negative (density matrix must be positive semidefinite)"
        elif abs (List.sum eigenvalues - 1.0) > 1e-6 then
            TopologicalResult.validationError "eigenvalues" $"Eigenvalues must sum to 1 (got {List.sum eigenvalues:F6})"
        else
            // Compute entropy: S = -Σ λ log(λ)
            // Handle λ = 0 case: 0 * log(0) = 0 by convention
            let entropyNat = 
                eigenvalues
                |> List.sumBy (fun lambda ->
                    if lambda < 1e-12 then
                        0.0  // Limit: λ log λ → 0 as λ → 0
                    else
                        -lambda * log lambda
                )
            
            let entropyBits = entropyNat / log 2.0
            
            Ok {
                Eigenvalues = eigenvalues
                Entropy = entropyNat
                EntropyBits = entropyBits
            }
    
    // ========================================================================
    // GROUND STATE DEGENERACY AND ENTROPY
    // ========================================================================
    
    /// Ground state degeneracy on a genus-g surface
    /// 
    /// From modular data:
    ///   GSD(g) = Σ_a (d_a / D)^(2-2g)
    /// 
    /// Special cases:
    /// - g=0 (sphere): GSD = 1
    /// - g=1 (torus): GSD = number of anyon types
    /// - Higher genus: Exponential growth in number of independent states
    /// 
    /// The logarithm of GSD is related to topological entropy.
    let groundStateDegeneracy 
        (anyonType: AnyonSpecies.AnyonType) 
        (genus: int) 
        : TopologicalResult<int> =
        
        if genus < 0 then
            TopologicalResult.validationError "field" "Genus must be non-negative"
        else
            // Use ModularData to compute GSD
            ModularData.computeModularData anyonType
            |> Result.map (fun data ->
                ModularData.groundStateDegeneracy data genus
            )
    
    /// Entropy of ground state degeneracy
    /// 
    /// S_GSD = log(GSD)
    /// 
    /// Related to topological entropy but depends on topology (genus).
    let groundStateDegeneracyEntropy 
        (anyonType: AnyonSpecies.AnyonType) 
        (genus: int) 
        : TopologicalResult<float> =
        
        groundStateDegeneracy anyonType genus
        |> Result.map (float >> log)
    
    // ========================================================================
    // UTILITY FUNCTIONS
    // ========================================================================
    
    /// Display topological entropy with interpretation
    let display (anyonType: AnyonSpecies.AnyonType) : TopologicalResult<string> =
        topologicalEntropy anyonType
        |> Result.bind (fun gamma ->
            topologicalEntropyLog2 anyonType
            |> Result.map (fun gammaBits ->
                let D_result = AnyonSpecies.totalQuantumDimension anyonType
                match D_result with
                | Ok d ->
                    let entanglementStatus = if gamma > 0.0 then "YES" else "NO"
                    let orderStatus = if gamma > 0.0 then "Present" else "Absent"
                    $"Topological Entropy for {anyonType}:\n" +
                    $"  γ = log(D) = {gamma:F6} (natural log)\n" +
                    $"  γ = {gammaBits:F6} bits (log₂)\n" +
                    $"  D = {d:F6} (total quantum dimension)\n" +
                    $"\nInterpretation:\n" +
                    $"  - Total quantum dimension: D = {d:F6}\n" +
                    $"  - Long-range entanglement: {entanglementStatus}\n" +
                    $"  - Topological order: {orderStatus}"
                | Error _ ->
                    $"γ = {gamma:F6}"
            )
        )
    
    /// Compare topological entropies of different theories
    let compare 
        (anyonType1: AnyonSpecies.AnyonType) 
        (anyonType2: AnyonSpecies.AnyonType) 
        : TopologicalResult<string> =
        
        topologicalEntropy anyonType1
        |> Result.bind (fun gamma1 ->
            topologicalEntropy anyonType2
            |> Result.map (fun gamma2 ->
                $"Topological Entropy Comparison:\n" +
                $"  {anyonType1}: γ = {gamma1:F6}\n" +
                $"  {anyonType2}: γ = {gamma2:F6}\n" +
                $"  Ratio: {gamma1 / gamma2:F4}"
            )
        )
    
    /// Verify Kitaev-Preskill formula against theoretical value
    let verifyKitaevPreskill 
        (anyonType: AnyonSpecies.AnyonType) 
        (regions: KitaevPreskillRegions) 
        : TopologicalResult<bool> =
        
        topologicalEntropy anyonType
        |> Result.map (fun gammaTheoretical ->
            let gammaExtracted = -kitaevPreskill regions  // Note minus sign
            let difference = abs (gammaExtracted - gammaTheoretical)
            difference < 0.1  // Tolerance for numerical/finite-size effects
        )
