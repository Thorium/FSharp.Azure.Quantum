namespace FSharp.Azure.Quantum.Topological

/// Anyon species definitions for topological quantum computing
/// 
/// This module defines the fundamental particle types used in topological
/// quantum computation. Unlike traditional qubits, anyons are quasiparticles
/// with exotic exchange statistics that exist in 2D systems.
/// 
/// Key concepts:
/// - Anyons: Particles in 2D with neither bosonic nor fermionic statistics
/// - Fusion: How anyons combine (analogous to measurement)
/// - Non-abelian: Fusion outcomes form a Hilbert space (enables computation)
[<RequireQualifiedAccess>]
module AnyonSpecies =
    
    /// Anyon type specifies the topological quantum field theory (TQFT)
    /// 
    /// Each type has different:
    /// - Particle species
    /// - Fusion rules (how particles combine)
    /// - Braiding statistics (how particles exchange)
    /// - Computational power (universal vs Clifford-only)
    type AnyonType =
        /// Ising anyons (SU(2)₂ TQFT)
        /// - Particles: {1, σ, ψ}
        /// - Clifford gates only (needs magic states for universality)
        /// - Physically realizable via Majorana zero modes
        /// - Microsoft's Majorana quantum computing approach
        | Ising
        
        /// Fibonacci anyons
        /// - Particles: {1, τ}
        /// - Universal for quantum computation via braiding alone
        /// - No known physical realization yet
        /// - Golden ratio appears in fusion tree dimensions
        | Fibonacci
        
        /// General SU(2)_k theory (k = level)
        /// - Ising = SU(2)₂
        /// - Fibonacci-like = SU(2)₃
        /// - Higher k → richer structure
        | SU2Level of level: int
    
    /// Particle types for different anyon theories
    /// 
    /// Each anyon theory has its own set of particles with specific
    /// fusion rules and quantum dimensions.
    type Particle =
        /// Vacuum (identity element)
        /// - Appears in all theories
        /// - Fusion: 1 × a = a (identity)
        /// - Quantum dimension: d₁ = 1
        | Vacuum
        
        /// Sigma anyon (Ising theory)
        /// - Non-abelian Majorana fermion
        /// - Fusion: σ × σ = 1 + ψ (TWO outcomes!)
        /// - Quantum dimension: d_σ = √2
        /// - Physically: Majorana zero mode
        | Sigma
        
        /// Psi anyon (Ising theory)
        /// - Abelian fermion
        /// - Fusion: ψ × ψ = 1 (deterministic)
        /// - Quantum dimension: d_ψ = 1
        /// - Physically: Ordinary fermion
        | Psi
        
        /// Tau anyon (Fibonacci theory)
        /// - Non-abelian "golden" anyon
        /// - Fusion: τ × τ = 1 + τ (Fibonacci recurrence)
        /// - Quantum dimension: d_τ = φ = (1+√5)/2 (golden ratio!)
        /// - Universal braiding
        | Tau
        
        /// SU(2)_k spin-j anyon (general SU(2) level k)
        /// - j ranges from 0 to k/2 in steps of 1/2
        /// - j is stored as integer j_doubled = 2*j (to avoid floats)
        /// - Example SU(2)₃: j ∈ {0, 1/2, 1, 3/2} → j_doubled ∈ {0, 1, 2, 3}
        /// - Quantum dimension: d_j = sin(π(j+1)/(k+2)) / sin(π/(k+2))
        | SpinJ of j_doubled: int * level: int
    
    /// Get quantum dimension of a particle
    /// 
    /// Quantum dimension is a topological invariant that:
    /// - d₁ = 1 (vacuum)
    /// - Satisfies fusion rules: N^c_ab = Σ_d (d_a × d_b × d_d / d_c)
    /// - Total dimension D = √(Σ_a d_a²) (important for normalization)
    /// 
    /// For Ising: d₁=1, d_σ=√2, d_ψ=1 → D=2
    /// For Fibonacci: d₁=1, d_τ=φ=(1+√5)/2 → D=√(1+φ²)
    /// For SU(2)_k: d_j = sin(π(j+1)/(k+2)) / sin(π/(k+2))
    let quantumDimension (particle: Particle) : float =
        match particle with
        | Vacuum -> 1.0
        | Sigma -> sqrt 2.0  // √2 ≈ 1.414
        | Psi -> 1.0
        | Tau -> (1.0 + sqrt 5.0) / 2.0  // φ ≈ 1.618 (golden ratio)
        | SpinJ (j_doubled, k) ->
            // j = j_doubled / 2, formula: d_j = sin(π(j+1)/(k+2)) / sin(π/(k+2))
            let j = float j_doubled / 2.0
            let numerator = sin (System.Math.PI * (j + 1.0) / float (k + 2))
            let denominator = sin (System.Math.PI / float (k + 2))
            numerator / denominator
    
    /// Get all particles for a given anyon type
    /// 
    /// Returns the complete set of simple objects (particle types)
    /// in the topological quantum field theory.
    /// 
    /// Returns Error if the anyon type is not yet implemented.
    let particles (anyonType: AnyonType) : TopologicalResult<Particle list> =
        match anyonType with
        | Ising -> Ok [Vacuum; Sigma; Psi]
        | Fibonacci -> Ok [Vacuum; Tau]
        | SU2Level k when k = 2 -> Ok [Vacuum; Sigma; Psi]  // SU(2)₂ = Ising
        | SU2Level k when k = 3 ->
            // SU(2)₃ has 4 particles: j ∈ {0, 1/2, 1, 3/2}
            // j_doubled ∈ {0, 1, 2, 3}
            Ok [
                SpinJ(0, 3)  // j=0 (vacuum)
                SpinJ(1, 3)  // j=1/2
                SpinJ(2, 3)  // j=1
                SpinJ(3, 3)  // j=3/2
            ]
        | SU2Level k -> 
            // General SU(2)_k has k+1 particles (spin 0, 1/2, ..., k/2)
            // Generate all j values: j_doubled from 0 to k
            let particleList = 
                [0 .. k]
                |> List.map (fun j_doubled -> SpinJ(j_doubled, k))
            Ok particleList
    
    /// Total quantum dimension D for normalization
    /// 
    /// D = √(Σ_a d_a²) where sum is over all particle types
    /// 
    /// Used for:
    /// - Normalizing fusion tree states
    /// - Calculating topological entanglement entropy: S_topo = log(D)
    /// - Computing partition functions
    /// 
    /// Returns Error if the anyon type is not yet implemented.
    let totalQuantumDimension (anyonType: AnyonType) : TopologicalResult<float> =
        particles anyonType
        |> Result.map (fun particleList ->
            particleList
            |> List.map quantumDimension
            |> List.sumBy (fun d -> d * d)
            |> sqrt
        )
    
    /// Check if particle is valid for given anyon type
    /// 
    /// Returns Error if the anyon type is not yet implemented.
    let isValid (anyonType: AnyonType) (particle: Particle) : TopologicalResult<bool> =
        particles anyonType
        |> Result.map (fun particleList -> List.contains particle particleList)
    
    /// Get anti-particle (charge conjugate)
    /// 
    /// For fusion: a × ā = 1 (with some multiplicity)
    /// 
    /// In Ising/Fibonacci theories, all particles are self-conjugate:
    /// - 1̄ = 1 (vacuum is self-conjugate)
    /// - σ̄ = σ (Majorana is its own antiparticle)
    /// - ψ̄ = ψ (fermion is self-conjugate in this theory)
    /// - τ̄ = τ (Fibonacci anyon is self-conjugate)
    /// 
    /// In SU(2)_k: all spin-j anyons are self-conjugate
    let antiParticle (particle: Particle) : Particle =
        match particle with
        | Vacuum -> Vacuum  // 1̄ = 1
        | Sigma -> Sigma    // σ̄ = σ (Majorana is self-conjugate)
        | Psi -> Psi        // ψ̄ = ψ
        | Tau -> Tau        // τ̄ = τ
        | SpinJ (j, k) -> SpinJ (j, k)  // All SU(2)_k particles are self-conjugate
    
    /// Frobenius-Schur indicator κ_a ∈ {+1, -1, 0}
    /// 
    /// Determines symmetry of fusion channels:
    /// - κ = +1: "Real" (symmetric under conjugation)
    /// - κ = -1: "Pseudoreal" (antisymmetric)
    /// - κ = 0: "Complex" (not self-conjugate)
    /// 
    /// For self-conjugate anyons in Ising/Fibonacci: κ ∈ {+1, -1}
    /// For SU(2)_k: κ = (-1)^j (where j is the spin)
    let frobenius_schur_indicator (particle: Particle) : int =
        match particle with
        | Vacuum -> 1   // Always +1 for vacuum
        | Sigma -> 1    // Ising: σ is "real"
        | Psi -> -1     // Ising: ψ is "pseudoreal" (fermion)
        | Tau -> 1      // Fibonacci: τ is "real"
        | SpinJ (j_doubled, _) -> 
            // κ_j = (-1)^j = (-1)^(j_doubled/2)
            // For integer spins (j_doubled even): κ = +1
            // For half-integer spins (j_doubled odd): κ = -1
            if j_doubled % 2 = 0 then 1 else -1
