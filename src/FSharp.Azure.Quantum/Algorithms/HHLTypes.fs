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
