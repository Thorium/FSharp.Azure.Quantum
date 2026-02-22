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
    open System.Numerics
    
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
    /// Implementation:
    /// 1. Reduced density matrix calculation (partial trace) 
    /// 2. Eigenvalue decomposition (Jacobi iteration)
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
    // DENSITY MATRIX AND PARTIAL TRACE
    // ========================================================================
    
    /// Construct density matrix ρ = |ψ⟩⟨ψ| from a state vector.
    /// 
    /// Input: amplitude vector [α₀, α₁, ..., αₙ] representing |ψ⟩
    /// Output: n×n matrix where ρᵢⱼ = αᵢ αⱼ*
    /// 
    /// The density matrix is Hermitian, positive semidefinite, and has Tr(ρ) = 1
    /// when the input state is normalized.
    let densityMatrix (amplitudes: Complex list) : Complex[,] =
        let n = amplitudes.Length
        let rho = Array2D.create n n Complex.Zero
        for i in 0 .. n - 1 do
            for j in 0 .. n - 1 do
                rho.[i, j] <- amplitudes.[i] * Complex.Conjugate(amplitudes.[j])
        rho
    
    /// Partial trace over subsystem B.
    /// 
    /// Given a density matrix ρ_AB of a bipartite system with dimensions
    /// (dimA × dimB), compute ρ_A = Tr_B(ρ_AB).
    /// 
    /// The total Hilbert space dimension must equal dimA × dimB.
    /// 
    /// Mathematical operation:
    ///   (ρ_A)_{i,i'} = Σⱼ ρ_{i⊗j, i'⊗j}
    /// where i,i' index subsystem A and j indexes subsystem B.
    let partialTraceB 
        (rho: Complex[,]) 
        (dimA: int) 
        (dimB: int) 
        : TopologicalResult<Complex[,]> =
        
        let totalDim = Array2D.length1 rho
        
        if totalDim <> dimA * dimB then
            TopologicalResult.validationError "dimensions" 
                $"Total dimension {totalDim} ≠ dimA ({dimA}) × dimB ({dimB})"
        elif dimA <= 0 || dimB <= 0 then
            TopologicalResult.validationError "dimensions"
                "Subsystem dimensions must be positive"
        else
            let rhoA = Array2D.create dimA dimA Complex.Zero
            
            for iA in 0 .. dimA - 1 do
                for iA' in 0 .. dimA - 1 do
                    let mutable sum = Complex.Zero
                    for jB in 0 .. dimB - 1 do
                        let row = iA * dimB + jB
                        let col = iA' * dimB + jB
                        sum <- sum + rho.[row, col]
                    rhoA.[iA, iA'] <- sum
            
            Ok rhoA
    
    /// Partial trace over subsystem A.
    /// 
    /// Given a density matrix ρ_AB, compute ρ_B = Tr_A(ρ_AB).
    /// 
    /// Mathematical operation:
    ///   (ρ_B)_{j,j'} = Σᵢ ρ_{i⊗j, i⊗j'}
    let partialTraceA 
        (rho: Complex[,]) 
        (dimA: int) 
        (dimB: int) 
        : TopologicalResult<Complex[,]> =
        
        let totalDim = Array2D.length1 rho
        
        if totalDim <> dimA * dimB then
            TopologicalResult.validationError "dimensions"
                $"Total dimension {totalDim} ≠ dimA ({dimA}) × dimB ({dimB})"
        elif dimA <= 0 || dimB <= 0 then
            TopologicalResult.validationError "dimensions"
                "Subsystem dimensions must be positive"
        else
            let rhoB = Array2D.create dimB dimB Complex.Zero
            
            for jB in 0 .. dimB - 1 do
                for jB' in 0 .. dimB - 1 do
                    let mutable sum = Complex.Zero
                    for iA in 0 .. dimA - 1 do
                        let row = iA * dimB + jB
                        let col = iA * dimB + jB'
                        sum <- sum + rho.[row, col]
                    rhoB.[jB, jB'] <- sum
            
            Ok rhoB
    
    // ========================================================================
    // EIGENVALUE DECOMPOSITION (Jacobi iteration for Hermitian matrices)
    // ========================================================================
    
    /// Extract real eigenvalues of a Hermitian density matrix using Jacobi iteration.
    /// 
    /// The Jacobi eigenvalue algorithm iteratively applies plane rotations to
    /// reduce off-diagonal elements to zero. For a Hermitian matrix, all
    /// eigenvalues are real.
    /// 
    /// Parameters:
    ///   matrix - Hermitian density matrix (must be square)
    ///   maxIterations - Maximum sweeps (default: 100)
    ///   tolerance - Convergence threshold for off-diagonal norm
    /// 
    /// Returns eigenvalues sorted in descending order.
    let eigenvaluesHermitian 
        (matrix: Complex[,]) 
        (maxIterations: int) 
        (tolerance: float) 
        : TopologicalResult<float list> =
        
        let n = Array2D.length1 matrix
        if n <> Array2D.length2 matrix then
            TopologicalResult.validationError "matrix" "Matrix must be square"
        elif n = 0 then
            Ok []
        elif n = 1 then
            Ok [matrix.[0, 0].Real]
        else
            // Work with real part (Hermitian matrix has real eigenvalues)
            // For a density matrix ρ, this is exact since ρ = ρ†
            let a = Array2D.init n n (fun i j -> matrix.[i, j].Real)
            
            let offDiagNorm () =
                let mutable s = 0.0
                for i in 0 .. n - 2 do
                    for j in i + 1 .. n - 1 do
                        s <- s + a.[i, j] * a.[i, j]
                sqrt s
            
            let mutable iteration = 0
            while iteration < maxIterations && offDiagNorm () > tolerance do
                // Sweep over all off-diagonal elements
                for p in 0 .. n - 2 do
                    for q in p + 1 .. n - 1 do
                        if abs a.[p, q] > tolerance / (float (n * n)) then
                            // Compute Jacobi rotation angle
                            let tau =
                                if abs (a.[p, p] - a.[q, q]) < 1e-15 then
                                    1.0  // θ = π/4
                                else
                                    2.0 * a.[p, q] / (a.[p, p] - a.[q, q])
                            
                            let t =
                                let sgn = if tau >= 0.0 then 1.0 else -1.0
                                sgn / (abs tau + sqrt (tau * tau + 1.0))
                            
                            let c = 1.0 / sqrt (1.0 + t * t)
                            let s = t * c
                            
                            // Apply rotation
                            let app = a.[p, p]
                            let aqq = a.[q, q]
                            let apq = a.[p, q]
                            
                            a.[p, p] <- app - t * apq
                            a.[q, q] <- aqq + t * apq
                            a.[p, q] <- 0.0
                            a.[q, p] <- 0.0
                            
                            for r in 0 .. n - 1 do
                                if r <> p && r <> q then
                                    let arp = a.[r, p]
                                    let arq = a.[r, q]
                                    a.[r, p] <- c * arp - s * arq
                                    a.[p, r] <- a.[r, p]
                                    a.[r, q] <- s * arp + c * arq
                                    a.[q, r] <- a.[r, q]
                
                iteration <- iteration + 1
            
            // Extract diagonal eigenvalues, sorted descending
            let eigenvals = 
                [ for i in 0 .. n - 1 -> a.[i, i] ]
                |> List.sortDescending
            
            Ok eigenvals
    
    /// Compute von Neumann entropy of a density matrix.
    /// 
    /// S(ρ) = -Tr(ρ log ρ) = -Σᵢ λᵢ log(λᵢ)
    /// 
    /// Performs eigenvalue decomposition and then entropy calculation.
    let vonNeumannEntropyFromDensityMatrix 
        (rho: Complex[,]) 
        : TopologicalResult<VonNeumannEntropyCalculation> =
        
        eigenvaluesHermitian rho 100 1e-12
        |> Result.bind (fun eigenvalues ->
            // Clamp small negative eigenvalues to zero (numerical noise)
            let clamped = eigenvalues |> List.map (fun v -> max 0.0 v)
            
            // Re-normalize to ensure sum = 1 (compensate for numerical error)
            let total = List.sum clamped
            let normalized = 
                if total > 1e-12 then
                    clamped |> List.map (fun v -> v / total)
                else
                    clamped
            
            vonNeumannEntropyFromEigenvalues normalized
        )
    
    /// Compute entanglement entropy of a pure bipartite state.
    /// 
    /// Given a state vector |ψ⟩_AB and subsystem dimensions dimA, dimB:
    /// 1. Construct density matrix ρ_AB = |ψ⟩⟨ψ|
    /// 2. Partial trace: ρ_A = Tr_B(ρ_AB)
    /// 3. Compute S(ρ_A) = -Tr(ρ_A log ρ_A)
    /// 
    /// This is the standard measure of bipartite entanglement for pure states.
    let entanglementEntropy 
        (amplitudes: Complex list) 
        (dimA: int) 
        (dimB: int) 
        : TopologicalResult<VonNeumannEntropyCalculation> =
        
        if amplitudes.Length <> dimA * dimB then
            TopologicalResult.validationError "amplitudes"
                $"State vector length {amplitudes.Length} ≠ dimA ({dimA}) × dimB ({dimB})"
        else
            let rhoAB = densityMatrix amplitudes
            partialTraceB rhoAB dimA dimB
            |> Result.bind vonNeumannEntropyFromDensityMatrix
    
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
            |> Result.bind (fun data ->
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
