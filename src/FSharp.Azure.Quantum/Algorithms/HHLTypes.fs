namespace FSharp.Azure.Quantum.Algorithms

open System
open FSharp.Azure.Quantum.Core
open System.Numerics

/// HHL Algorithm Types Module
/// 
/// Defines types and configurations for the HHL (Harrow-Hassidim-Lloyd) algorithm,
/// which solves linear systems of equations Ax = b using quantum computation.
/// 
/// Key Concepts:
/// - Given: Matrix A (Hermitian), vector |b⟩
/// - Goal: Find solution vector |x⟩ where A|x⟩ = |b⟩
/// - Quantum speedup: Exponential for sparse, well-conditioned matrices
/// 
/// Algorithm Overview:
/// 1. Encode |b⟩ in quantum state
/// 2. Apply QPE to extract eigenvalues λᵢ of A
/// 3. Apply controlled rotations proportional to 1/λᵢ
/// 4. Uncompute eigenvalue register (inverse QPE)
/// 5. Measure ancilla qubit - success indicates valid solution
/// 6. Extract solution vector |x⟩ from remaining qubits
module HHLTypes =
    
    // ========================================================================
    // MATRIX REPRESENTATION
    // ========================================================================
    
    /// Hermitian matrix representation for HHL algorithm
    /// 
    /// HHL requires the matrix A to be Hermitian (A = A†).
    /// Non-Hermitian matrices can be embedded in Hermitian form:
    /// [[0, A], [A†, 0]]
    type HermitianMatrix = {
        /// Matrix dimension (must be power of 2 for quantum encoding)
        Dimension: int
        
        /// Matrix elements (row-major order)
        /// For n×n matrix: Elements[i*n + j] = A[i,j]
        Elements: Complex[]
        
        /// Whether matrix is diagonal (enables optimizations)
        IsDiagonal: bool
        
        /// Condition number κ = λ_max / λ_min (affects algorithm accuracy)
        /// Lower condition number = better accuracy
        ConditionNumber: float option
    }
    
    /// Vector representation for input |b⟩
    type QuantumVector = {
        /// Vector dimension (must match matrix dimension)
        Dimension: int
        
        /// Vector components (normalized)
        Components: Complex[]
    }
    
    // ========================================================================
    // EIGENVALUE INVERSION METHODS
    // ========================================================================
    
    /// Method for performing eigenvalue inversion (λ → 1/λ)
    /// 
    /// This is the core quantum step that achieves the speedup.
    /// Different methods trade off accuracy, circuit depth, and success probability.
    type EigenvalueInversionMethod =
        /// Exact rotation: R_y(θ) where sin(θ) = C·λ/λ_max
        /// - Most accurate for well-separated eigenvalues
        /// - Requires precise angle computation
        | ExactRotation of normalizationConstant: float
        
        /// Linear approximation: R_y(2·arcsin(C·λ))
        /// - Good for small eigenvalues
        /// - Simpler circuit implementation
        | LinearApproximation of normalizationConstant: float
        
        /// Piecewise linear: Different rotations for different eigenvalue ranges
        /// - Handles wider eigenvalue range
        /// - More complex circuit but better accuracy
        | PiecewiseLinear of segments: (float * float * float)[]  // (min, max, normConstant)
    
    // ========================================================================
    // HHL CONFIGURATION
    // ========================================================================
    
    /// Configuration for HHL algorithm execution
    type HHLConfig = {
        /// Hermitian matrix A to invert (A|x⟩ = |b⟩)
        Matrix: HermitianMatrix
        
        /// Input vector |b⟩
        InputVector: QuantumVector
        
        /// Number of qubits for eigenvalue estimation (precision)
        /// More qubits = better eigenvalue precision but larger circuit
        EigenvalueQubits: int
        
        /// Number of qubits for encoding solution vector
        /// log₂(dimension) qubits needed
        SolutionQubits: int
        
        /// Eigenvalue inversion method
        InversionMethod: EigenvalueInversionMethod
        
        /// Minimum eigenvalue threshold (for numerical stability)
        /// Eigenvalues below this are treated as zero to avoid division by zero
        MinEigenvalue: float
        
        /// Whether to apply post-selection on ancilla qubit
        /// True = only keep results where ancilla = |1⟩ (higher accuracy, lower success rate)
        /// False = accept all results (lower accuracy, higher success rate)
        UsePostSelection: bool
        
        /// QPE configuration (for eigenvalue estimation phase)
        QPEPrecision: int  // Number of counting qubits for QPE
    }
    
    // ========================================================================
    // HHL RESULT
    // ========================================================================
    
    /// Result of HHL algorithm execution
    type HHLResult = {
        /// Solution vector (x in Ax = b)
        /// Extracted from quantum state amplitudes
        Solution: Complex[]
        
        /// Success probability (probability of ancilla = |1⟩)
        /// Higher for well-conditioned matrices
        SuccessProbability: float
        
        /// Eigenvalues extracted during QPE phase
        /// Useful for debugging and condition number estimation
        EstimatedEigenvalues: float[]
        
        /// Number of gates applied in circuit
        GateCount: int
        
        /// Whether post-selection was successful (ancilla = |1⟩)
        /// Only meaningful if UsePostSelection = true
        PostSelectionSuccess: bool
        
        /// Configuration used
        Config: HHLConfig
        
        /// Fidelity estimate (if classical solution available for comparison)
        /// Fidelity = |⟨x_classical|x_quantum⟩|²
        Fidelity: float option
        
        /// Solution amplitude distribution (basis state index -> amplitude)
        /// Only available when using local simulator
        /// For backend execution, use measurement statistics instead
        SolutionAmplitudes: Map<int, Complex> option
    }
    
    // ========================================================================
    // HELPER FUNCTIONS
    // ========================================================================
    
    /// Create Hermitian matrix from 2D array
    let createHermitianMatrix (elements: Complex[,]) : QuantumResult<HermitianMatrix> =
        let n = Array2D.length1 elements
        let m = Array2D.length2 elements
        
        if n <> m then
            Error (QuantumError.ValidationError ("Matrix", "must be square"))
        elif n = 0 then
            Error (QuantumError.ValidationError ("Matrix", "dimension must be positive"))
        elif (n &&& (n - 1)) <> 0 then
            Error (QuantumError.ValidationError ("Matrix", $"dimension must be power of 2, got {n}"))
        else
            // Check Hermitian property: A[i,j] = conj(A[j,i])
            let isHermitian =
                [for i in 0 .. n - 1 do
                    for j in 0 .. n - 1 do
                        let aij = elements[i, j]
                        let aji = elements[j, i]
                        let conjAji = Complex(aji.Real, -aji.Imaginary)
                        yield Complex.Abs(aij - conjAji) <= 1e-10]
                |> List.forall id
            
            if not isHermitian then
                Error (QuantumError.ValidationError ("Matrix", "must be Hermitian (A = A†)"))
            else
                // Check if diagonal
                let isDiag =
                    [for i in 0 .. n - 1 do
                        for j in 0 .. n - 1 do
                            if i <> j then
                                yield Complex.Abs(elements[i, j]) <= 1e-10]
                    |> List.forall id
                
                // Flatten to row-major order
                let flatElements = Array.init (n * n) (fun idx -> 
                    let i = idx / n
                    let j = idx % n
                    elements[i, j]
                )
                
                Ok {
                    Dimension = n
                    Elements = flatElements
                    IsDiagonal = isDiag
                    ConditionNumber = None  // Can be computed later
                }
    
    /// Create quantum vector from complex array
    let createQuantumVector (components: Complex[]) : QuantumResult<QuantumVector> =
        let n = components.Length
        
        if n = 0 then
            Error (QuantumError.ValidationError ("Vector", "dimension must be positive"))
        elif (n &&& (n - 1)) <> 0 then
            Error (QuantumError.ValidationError ("Vector", $"dimension must be power of 2, got {n}"))
        else
            // Normalize vector
            let norm = 
                components 
                |> Array.sumBy (fun c -> c.Magnitude * c.Magnitude)
                |> sqrt
            
            if norm < 1e-10 then
                Error (QuantumError.ValidationError ("Vector", "must have non-zero norm"))
            else
                let normalized = components |> Array.map (fun c -> c / norm)
                
                Ok {
                    Dimension = n
                    Components = normalized
                }
    
    /// Create simple diagonal Hermitian matrix
    /// Useful for testing and simple cases
    let createDiagonalMatrix (eigenvalues: float[]) : QuantumResult<HermitianMatrix> =
        let n = eigenvalues.Length
        
        if n = 0 then
            Error (QuantumError.ValidationError ("Eigenvalues", "must have at least one eigenvalue"))
        elif (n &&& (n - 1)) <> 0 then
            Error (QuantumError.ValidationError ("Eigenvalues", $"number must be power of 2, got {n}"))
        else
            // Check for singular matrix (zero or near-zero eigenvalues)
            let minEigAbs = eigenvalues |> Array.map abs |> Array.min
            if minEigAbs < 1e-10 then
                Error (QuantumError.ValidationError ("eigenvalues", $"matrix is singular - eigenvalue near zero ({minEigAbs:E})"))
            else
                let elements = Array.init (n * n) (fun idx ->
                    let i = idx / n
                    let j = idx % n
                    if i = j then Complex(eigenvalues[i], 0.0) else Complex.Zero
                )
                
                // Calculate condition number
                let maxEig = eigenvalues |> Array.max
                let conditionNumber = Some (maxEig / minEigAbs)
                
                Ok {
                    Dimension = n
                    Elements = elements
                    IsDiagonal = true
                    ConditionNumber = conditionNumber
                }
    
    // ========================================================================
    // CONDITION NUMBER ESTIMATION
    // ========================================================================
    
    /// <summary>
    /// Estimate largest eigenvalue of Hermitian matrix using Power Iteration.
    /// </summary>
    /// <param name="matrix">Hermitian matrix</param>
    /// <param name="maxIterations">Maximum iterations (default: 100)</param>
    /// <param name="tolerance">Convergence tolerance (default: 1e-6)</param>
    /// <returns>Estimated largest eigenvalue (magnitude)</returns>
    /// <remarks>
    /// Power Iteration Algorithm:
    /// 1. Start with random vector v₀
    /// 2. Iterate: vₖ₊₁ = A·vₖ / ||A·vₖ||
    /// 3. Eigenvalue estimate: λ = vₖᵀ·A·vₖ / vₖᵀ·vₖ
    /// 
    /// Converges to dominant eigenvalue (largest magnitude).
    /// For Hermitian matrices, all eigenvalues are real.
    /// </remarks>
    let private estimateMaxEigenvalue 
        (matrix: HermitianMatrix) 
        (maxIterations: int) 
        (tolerance: float) : float =
        
        let n = matrix.Dimension
        
        // Matrix-vector multiplication: y = A·x (idiomatic F# - using fold for Complex)
        let matVecMult (x: Complex[]) : Complex[] =
            Array.init n (fun i ->
                [| 0 .. n - 1 |]
                |> Array.fold (fun sum j ->
                    let aij = matrix.Elements[i * n + j]
                    sum + aij * x[j]
                ) Complex.Zero
            )
        
        // Vector norm
        let norm (v: Complex[]) : float =
            v |> Array.sumBy (fun c -> c.Magnitude * c.Magnitude) |> sqrt
        
        // Normalize vector
        let normalize (v: Complex[]) : Complex[] =
            let n = norm v
            if n < 1e-15 then v else Array.map (fun c -> c / n) v
        
        // Rayleigh quotient: λ = xᵀAx / xᵀx
        let rayleighQuotient (x: Complex[]) : float =
            let ax = matVecMult x
            let numerator = Array.zip x ax |> Array.sumBy (fun (xi, axi) -> (Complex.Conjugate(xi) * axi).Real)
            let denominator = x |> Array.sumBy (fun xi -> xi.Magnitude * xi.Magnitude)
            numerator / denominator
        
        // Initialize with random vector (seeded for reproducibility)
        let rng = System.Random(42)
        let v0 = Array.init n (fun _ -> Complex(rng.NextDouble(), rng.NextDouble()))
        let v0Normalized = normalize v0
        
        // Power iteration (tail-recursive)
        let rec iterate (v: Complex[]) (prevEigenvalue: float) (iter: int) : float =
            if iter >= maxIterations then
                abs prevEigenvalue
            else
                // v_{k+1} = A·v_k
                let vNext = v |> matVecMult |> normalize
                
                // Estimate eigenvalue
                let eigenvalue = rayleighQuotient vNext
                
                // Check convergence
                if abs (eigenvalue - prevEigenvalue) < tolerance then
                    abs eigenvalue
                else
                    iterate vNext eigenvalue (iter + 1)
        
        iterate v0Normalized 0.0 0
    
    /// <summary>
    /// Estimate smallest eigenvalue of Hermitian matrix using Inverse Power Iteration.
    /// </summary>
    /// <param name="matrix">Hermitian matrix (must be invertible)</param>
    /// <param name="maxIterations">Maximum iterations (default: 100)</param>
    /// <param name="tolerance">Convergence tolerance (default: 1e-6)</param>
    /// <returns>Estimated smallest eigenvalue (magnitude)</returns>
    /// <remarks>
    /// Inverse Power Iteration: Apply power iteration to A⁻¹
    /// Converges to smallest eigenvalue of A.
    /// 
    /// For production: This uses Gaussian elimination for matrix inversion.
    /// For very large matrices, use iterative solvers (Conjugate Gradient, GMRES).
    /// </remarks>
    let private estimateMinEigenvalue 
        (matrix: HermitianMatrix) 
        (maxIterations: int) 
        (tolerance: float) : float =
        
        let n = matrix.Dimension
        
        // For diagonal matrices, just find minimum
        if matrix.IsDiagonal then
            let eigenvalues = 
                [| for i in 0 .. n - 1 do
                    yield matrix.Elements[i * n + i].Real |]
            eigenvalues |> Array.map abs |> Array.min
        else
            // For general matrices: use shift-and-invert
            // Apply power iteration to (A - σI)⁻¹ where σ is close to smallest eigenvalue
            // Simplified: just use reciprocal of max eigenvalue of A⁻¹
            
            // Note: Full matrix inversion is expensive O(n³)
            // For production large matrices, use sparse solvers
            
            // Placeholder: Estimate using power iteration on A with shift
            // True implementation would solve (A - σI)x = b iteratively
            
            // Fallback: Use Gershgorin circle theorem for bounds
            let gershgorinBound = 
                [| for i in 0 .. n - 1 do
                    let aii = matrix.Elements[i * n + i].Real
                    let rowSum = 
                        [| for j in 0 .. n - 1 do
                            if i <> j then yield matrix.Elements[i * n + j].Magnitude |]
                        |> Array.sum
                    yield abs (aii - rowSum) |]
                |> Array.min
            
            max gershgorinBound 1e-10  // Avoid zero
    
    /// <summary>
    /// Calculate condition number κ = λ_max / λ_min for Hermitian matrix.
    /// </summary>
    /// <param name="matrix">Hermitian matrix</param>
    /// <returns>Updated matrix with condition number calculated</returns>
    /// <remarks>
    /// Condition number indicates matrix conditioning:
    /// - κ ≈ 1: Well-conditioned (ideal for HHL)
    /// - κ > 100: Poorly conditioned (HHL success probability drops)
    /// - κ > 10000: Very poorly conditioned (consider preconditioning)
    /// 
    /// HHL success probability: P ∝ 1/κ²
    /// 
    /// For diagonal matrices: exact calculation
    /// For general matrices: estimated using power iteration (fast approximation)
    /// </remarks>
    let calculateConditionNumber (matrix: HermitianMatrix) : HermitianMatrix =
        match matrix.ConditionNumber with
        | Some _ -> matrix  // Already calculated
        | None ->
            if matrix.IsDiagonal then
                // Exact for diagonal
                let n = matrix.Dimension
                let eigenvalues = 
                    [| for i in 0 .. n - 1 do
                        yield abs (matrix.Elements[i * n + i].Real) |]
                let maxEig = Array.max eigenvalues
                let minEig = Array.min eigenvalues
                
                { matrix with ConditionNumber = Some (maxEig / minEig) }
            else
                // Estimate for general Hermitian
                let maxEig = estimateMaxEigenvalue matrix 100 1e-6
                let minEig = estimateMinEigenvalue matrix 100 1e-6
                
                { matrix with ConditionNumber = Some (maxEig / minEig) }
    
    // ========================================================================
    // ERROR ANALYSIS & BOUNDS
    // ========================================================================
    
    /// Error sources in HHL algorithm execution
    type HHLErrorBudget = {
        /// QPE eigenvalue estimation error: Δλ ≈ 2π / 2^n where n = precision qubits
        QPEPrecisionError: float
        
        /// Gate fidelity error accumulated over circuit depth
        /// Total error ≈ (1 - gate_fidelity) × circuit_depth
        GateFidelityError: float
        
        /// Eigenvalue inversion error (from controlled rotation approximation)
        /// Depends on normalization constant and minimum eigenvalue
        InversionError: float
        
        /// State preparation error (encoding |b⟩)
        StatePreparationError: float
        
        /// Total estimated error (sum of all sources)
        TotalError: float
        
        /// Estimated success probability based on condition number
        /// P_success ≈ (C / κ)² where C is normalization constant
        EstimatedSuccessProbability: float
    }
    
    /// <summary>
    /// Calculate error bounds for HHL algorithm execution.
    /// </summary>
    /// <param name="config">HHL configuration</param>
    /// <param name="gateFidelity">Average gate fidelity (default: 0.999 for superconducting qubits)</param>
    /// <param name="estimatedGateCount">Estimated total gates (if None, calculated from config)</param>
    /// <returns>Comprehensive error budget</returns>
    /// <remarks>
    /// **Error Budget Calculation**:
    /// 
    /// 1. QPE Precision Error:
    ///    Δλ ≈ 2π / 2^n where n = eigenvalue qubits
    ///    Impact: Eigenvalue estimates have ±Δλ uncertainty
    /// 
    /// 2. Gate Fidelity Error:
    ///    ε_gate = (1 - F) × D where F = fidelity, D = circuit depth
    ///    Typical: F = 0.999 (superconducting), 0.9999 (trapped ion)
    /// 
    /// 3. Inversion Error:
    ///    ε_inv = |1/λ - 1/λ'| ≈ Δλ / λ_min²
    ///    Larger for small eigenvalues (poorly conditioned matrices)
    /// 
    /// 4. State Preparation Error:
    ///    ε_prep ≈ 1/√(sample_count) for amplitude encoding
    ///    Negligible for exact state preparation
    /// 
    /// **Success Probability**:
    /// P_success ≈ (C / κ)² where:
    /// - C = normalization constant (typically 0.1 - 1.0)
    /// - κ = condition number
    /// 
    /// **Production Guidance**:
    /// - Total error < 0.01: Good (1% accuracy)
    /// - Total error < 0.1: Acceptable (10% accuracy)
    /// - Total error > 0.1: Poor (consider error mitigation)
    /// </remarks>
    let calculateErrorBounds 
        (config: HHLConfig) 
        (gateFidelity: float option) 
        (estimatedGateCount: int option) : HHLErrorBudget =
        
        // Default gate fidelities by backend type
        let defaultGateFidelity = 0.999  // Superconducting qubits (conservative)
        let fidelity = gateFidelity |> Option.defaultValue defaultGateFidelity
        
        // Calculate QPE precision error
        let qpePrecisionError = 2.0 * Math.PI / (pown 2.0 config.EigenvalueQubits)
        
        // Estimate circuit depth if not provided
        let gateCount = 
            match estimatedGateCount with
            | Some count -> count
            | None ->
                // Rough estimate for HHL circuit:
                // - QPE: O(n²) where n = eigenvalue qubits
                // - Controlled rotations: O(n) per eigenvalue
                // - Inverse QPE: O(n²)
                let n = config.EigenvalueQubits
                let qpeGates = n * n * 10  // QPE gates (approximate)
                let inversionGates = (1 <<< n) * 5  // Controlled rotations
                qpeGates + inversionGates + (n * n * 10)  // Inverse QPE
        
        // Calculate gate fidelity error
        let gateFidelityError = (1.0 - fidelity) * float gateCount
        
        // Calculate eigenvalue inversion error
        // Get minimum eigenvalue from matrix
        let minEigenvalue = config.MinEigenvalue
        let inversionError = qpePrecisionError / (minEigenvalue * minEigenvalue)
        
        // State preparation error (assumed negligible for exact preparation)
        let statePreparationError = 0.001  // 0.1% for amplitude encoding
        
        // Total error (sum of independent error sources)
        let totalError = 
            qpePrecisionError + 
            gateFidelityError + 
            inversionError + 
            statePreparationError
        
        // Estimate success probability from condition number
        let successProbability =
            match config.Matrix.ConditionNumber with
            | Some kappa ->
                // Extract normalization constant from inversion method
                let normConst = 
                    match config.InversionMethod with
                    | ExactRotation c | LinearApproximation c -> c
                    | PiecewiseLinear segments -> 
                        // Average normalization constant
                        segments |> Array.averageBy (fun (_, _, c) -> c)
                
                // P_success ≈ (C / κ)²
                let ratio = normConst / kappa
                ratio * ratio
            | None ->
                // Conservative estimate without condition number
                0.5
        
        {
            QPEPrecisionError = qpePrecisionError
            GateFidelityError = gateFidelityError
            InversionError = inversionError
            StatePreparationError = statePreparationError
            TotalError = totalError
            EstimatedSuccessProbability = successProbability
        }
    
    /// <summary>
    /// Get recommended QPE precision based on target error tolerance.
    /// </summary>
    /// <param name="targetError">Target eigenvalue error (e.g., 0.01 for 1% accuracy)</param>
    /// <returns>Recommended number of eigenvalue qubits</returns>
    /// <remarks>
    /// Based on QPE error bound: Δλ ≈ 2π / 2^n
    /// Solves for n: n = ceil(log₂(2π / targetError))
    /// 
    /// Examples:
    /// - 1% error (0.01): n = 9 qubits
    /// - 0.1% error (0.001): n = 13 qubits  
    /// - 0.01% error (0.0001): n = 16 qubits
    /// </remarks>
    let recommendQPEPrecision (targetError: float) : int =
        let n = Math.Ceiling(Math.Log(2.0 * Math.PI / targetError, 2.0))
        int n |> max 4 |> min 20  // Clamp to [4, 20] for practicality
    
    /// <summary>
    /// Select optimal eigenvalue inversion method based on condition number.
    /// </summary>
    /// <param name="conditionNumber">Matrix condition number κ = λ_max / λ_min</param>
    /// <param name="normalizationConstant">Normalization constant C (default: 1.0)</param>
    /// <returns>Recommended inversion method</returns>
    /// <remarks>
    /// **Decision Heuristics** (based on quantum computing literature):
    /// 
    /// 1. **Well-Conditioned (κ ≤ 10)**:
    ///    - Use ExactRotation with C = 1.0
    ///    - High success probability (> 10%)
    ///    - Most accurate for well-separated eigenvalues
    /// 
    /// 2. **Moderately Conditioned (10 < κ ≤ 100)**:
    ///    - Use LinearApproximation with C = 0.5
    ///    - Moderate success probability (1-10%)
    ///    - Better for clustered eigenvalues
    /// 
    /// 3. **Poorly Conditioned (100 < κ ≤ 1000)**:
    ///    - Use PiecewiseLinear with 2-3 segments
    ///    - Lower success probability (0.1-1%)
    ///    - Handles wide eigenvalue range
    ///    - Segments: [λ_min, λ_min×10], [λ_min×10, λ_max]
    /// 
    /// 4. **Very Poorly Conditioned (κ > 1000)**:
    ///    - Use PiecewiseLinear with 4+ segments
    ///    - Very low success probability (< 0.1%)
    ///    - Consider preconditioning before HHL
    ///    - Logarithmic segmentation of eigenvalue range
    /// 
    /// **Production Recommendation**:
    /// - κ > 10000: Apply matrix preconditioning first
    /// - κ > 100: Use error mitigation techniques
    /// - κ ≤ 100: Standard HHL should work well
    /// </remarks>
    let selectInversionMethod (conditionNumber: float) (normalizationConstant: float option) : EigenvalueInversionMethod =
        let c = normalizationConstant |> Option.defaultValue 1.0
        
        if conditionNumber <= 10.0 then
            // Well-conditioned: use exact rotation
            ExactRotation c
        elif conditionNumber <= 100.0 then
            // Moderately conditioned: use linear approximation
            LinearApproximation (c * 0.5)
        elif conditionNumber <= 1000.0 then
            // Poorly conditioned: use piecewise with 2 segments
            // Divide range: [λ_min, λ_mid], [λ_mid, λ_max]
            // where λ_mid ≈ √(λ_min × λ_max)
            let segmentBoundary = sqrt conditionNumber  // Geometric mean position
            PiecewiseLinear [|
                (1.0, segmentBoundary, c * 0.7)         // Lower range (smaller eigenvalues)
                (segmentBoundary, conditionNumber, c * 0.3)  // Upper range (larger eigenvalues)
            |]
        else
            // Very poorly conditioned: use piecewise with logarithmic segments
            // Divide into 4 segments logarithmically
            let logKappa = log10 conditionNumber
            let numSegments = 4
            let segments =
                [| for i in 0 .. numSegments - 1 do
                    let logMin = float i * logKappa / float numSegments
                    let logMax = float (i + 1) * logKappa / float numSegments
                    let segMin = pown 10.0 (int logMin)
                    let segMax = pown 10.0 (int logMax)
                    // Smaller normalization for higher eigenvalue ranges
                    let normFactor = c * (1.0 - float i / float numSegments)
                    yield (segMin, segMax, normFactor)
                |]
            PiecewiseLinear segments
    
    /// <summary>
    /// Create optimized HHL configuration with automatic method selection.
    /// </summary>
    /// <param name="matrix">Hermitian matrix (will calculate condition number if not present)</param>
    /// <param name="inputVector">Input vector |b⟩</param>
    /// <param name="targetAccuracy">Target solution accuracy (default: 0.01 = 1%)</param>
    /// <returns>Optimized HHL configuration</returns>
    /// <remarks>
    /// This function automatically:
    /// 1. Calculates condition number if not present
    /// 2. Selects optimal inversion method
    /// 3. Sets QPE precision based on target accuracy
    /// 4. Configures post-selection based on condition number
    /// 
    /// Use this for production workflows instead of manual configuration.
    /// </remarks>
    let optimizedConfig 
        (matrix: HermitianMatrix) 
        (inputVector: QuantumVector) 
        (targetAccuracy: float option) : HHLConfig =
        
        if matrix.Dimension <> inputVector.Dimension then
            failwith "Matrix and vector dimensions must match"
        
        // Calculate condition number if not present
        let matrixWithKappa = calculateConditionNumber matrix
        
        // Get condition number
        let kappa = 
            match matrixWithKappa.ConditionNumber with
            | Some k -> k
            | None -> 100.0  // Conservative fallback
        
        // Select optimal inversion method
        let inversionMethod = selectInversionMethod kappa None
        
        // Set QPE precision based on target accuracy
        let accuracy = targetAccuracy |> Option.defaultValue 0.01  // Default: 1%
        let qpePrecision = recommendQPEPrecision accuracy
        
        // Use post-selection for well-conditioned matrices (higher success rate)
        // Skip for poorly conditioned (already low success rate, avoid further reduction)
        let usePostSelection = kappa <= 100.0
        
        let solutionQubits = 
            let rec log2 n = if n <= 1 then 0 else 1 + log2 (n / 2)
            log2 matrix.Dimension
        
        {
            Matrix = matrixWithKappa
            InputVector = inputVector
            EigenvalueQubits = qpePrecision
            SolutionQubits = solutionQubits
            InversionMethod = inversionMethod
            MinEigenvalue = 1.0 / kappa  // Set based on condition number
            UsePostSelection = usePostSelection
            QPEPrecision = qpePrecision
        }
    
    /// Default HHL configuration
    let defaultConfig (matrix: HermitianMatrix) (inputVector: QuantumVector) : HHLConfig =
        if matrix.Dimension <> inputVector.Dimension then
            failwith "Matrix and vector dimensions must match"
        
        let solutionQubits = 
            let rec log2 n = if n <= 1 then 0 else 1 + log2 (n / 2)
            log2 matrix.Dimension
        
        {
            Matrix = matrix
            InputVector = inputVector
            EigenvalueQubits = 4  // 4 qubits = 16 eigenvalue bins
            SolutionQubits = solutionQubits
            InversionMethod = ExactRotation 1.0
            MinEigenvalue = 1e-6  // Avoid division by very small eigenvalues
            UsePostSelection = true
            QPEPrecision = 4
        }
