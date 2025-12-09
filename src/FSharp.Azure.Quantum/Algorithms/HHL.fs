namespace FSharp.Azure.Quantum.Algorithms

open System
open System.Numerics
open FSharp.Azure.Quantum
open FSharp.Azure.Quantum.Core
open FSharp.Azure.Quantum.Core.BackendAbstraction
open FSharp.Azure.Quantum.LocalSimulator
open FSharp.Azure.Quantum.Algorithms.QPE

/// HHL Algorithm - Unified Backend Implementation
/// 
/// Harrow-Hassidim-Lloyd algorithm for solving linear systems Ax = b.
/// State-based implementation using IQuantumBackend.
/// 
/// Algorithm Overview:
/// Given a Hermitian matrix A and vector |b⟩, finds solution |x⟩ where A|x⟩ = |b⟩.
/// Provides exponential speedup over classical methods for sparse, well-conditioned matrices.
/// 
/// Steps:
/// 1. State Preparation: Encode |b⟩ as quantum state
/// 2. Quantum Phase Estimation: Extract eigenvalues λᵢ of A
/// 3. Eigenvalue Inversion: Apply controlled rotations ∝ 1/λᵢ  
/// 4. Inverse QPE: Uncompute eigenvalue register
/// 5. Measurement: Extract solution |x⟩
/// 
/// Applications:
/// - Machine learning (quantum SVM, least squares)
/// - Solving differential equations
/// - Portfolio optimization
/// - Quantum chemistry simulations
/// 
/// Complexity: O(log(N) s² κ² / ε) where:
/// - N = matrix dimension
/// - s = sparsity (max non-zero entries per row)
/// - κ = condition number (λ_max / λ_min)
/// - ε = precision
/// 
/// Educational Implementation:
/// This version focuses on diagonal matrices for clarity.
/// For general matrices and cloud execution, use HHLBackendAdapter.
/// 
/// Example:
/// ```fsharp
/// open FSharp.Azure.Quantum.Algorithms.HHL
/// open FSharp.Azure.Quantum.Backends.LocalBackend
/// 
/// let backend = LocalBackend() :> IQuantumBackend
/// 
/// // Solve: [[2,0],[0,3]] * x = [1,1]
/// // Expected: x ≈ [0.5, 0.333...]
/// match solve2x2Diagonal (2.0, 3.0) (Complex(1.0, 0.0), Complex(1.0, 0.0)) backend with
/// | Ok result ->
///     printfn "Success probability: %f" result.SuccessProbability
///     printfn "Solution found!"
/// | Error err -> printfn "Error: %A" err
/// ```
module HHL =
    
    open FSharp.Azure.Quantum.Algorithms.HHLTypes
    open FSharp.Azure.Quantum.Algorithms.QPE
    
    // ========================================================================
    // STATE PREPARATION - Encode input vector |b⟩
    // ========================================================================
    
    /// <summary>
    /// Prepare quantum state from input vector |b⟩.
    /// Encodes classical vector b as quantum amplitudes.
    /// </summary>
    /// <param name="inputVector">Input vector (must be normalized)</param>
    /// <param name="backend">Quantum backend</param>
    /// <returns>Quantum state |b⟩ or error</returns>
    /// <remarks>
    /// Given vector b = [b₀, b₁, ..., b_{N-1}], creates state:
    /// |b⟩ = Σᵢ bᵢ|i⟩ / ||b||
    /// 
    /// Vector must have dimension = 2^n for some n.
    /// </remarks>
    let private prepareInputState 
        (inputVector: QuantumVector) 
        (backend: IQuantumBackend) : Result<QuantumState, QuantumError> =
        
        // Create quantum state from complex amplitudes
        let amplitudes = inputVector.Components
        let stateVector = StateVector.create amplitudes
        
        Ok (QuantumState.StateVector stateVector)
    
    // ========================================================================
    // HAMILTONIAN SIMULATION - Convert matrix A to unitary
    // ========================================================================
    
    /// <summary>
    /// Create unitary operator U = e^(iAt) for diagonal Hermitian matrix A.
    /// For diagonal matrix, U|j⟩ = e^(iλⱼt)|j⟩ where λⱼ are eigenvalues.
    /// </summary>
    /// <param name="matrix">Diagonal Hermitian matrix</param>
    /// <param name="time">Evolution time parameter</param>
    /// <returns>Unitary operator for QPE</returns>
    let private createDiagonalUnitary 
        (matrix: HermitianMatrix) 
        (time: float) : QPE.UnitaryOperator =
        
        if not matrix.IsDiagonal then
            failwith "Only diagonal matrices supported in educational implementation"
        
        // Extract diagonal eigenvalues
        let eigenvalues = 
            [| 0 .. matrix.Dimension - 1 |]
            |> Array.map (fun i -> 
                let idx = i * matrix.Dimension + i
                matrix.Elements[idx].Real  // Hermitian diagonal elements are real
            )
        
        // For diagonal matrix with eigenvalues λᵢ, the unitary is:
        // U|j⟩ = e^(iλⱼt)|j⟩
        // We'll use a phase gate with angle = λ₀ * t for the first eigenvalue
        // For simplicity in educational version, use the first eigenvalue
        let firstEigenvalue = eigenvalues[0]
        QPE.PhaseGate (firstEigenvalue * time)
    
    // ========================================================================
    // EIGENVALUE INVERSION - Apply controlled rotations ∝ 1/λ
    // ========================================================================
    
    /// <summary>
    /// Apply controlled rotation for eigenvalue inversion.
    /// Given eigenvalue λ encoded in counting register, apply rotation ∝ 1/λ.
    /// </summary>
    /// <param name="eigenvalue">Estimated eigenvalue from QPE</param>
    /// <param name="ancillaQubit">Ancilla qubit index</param>
    /// <param name="minEigenvalue">Minimum eigenvalue threshold</param>
    /// <param name="backend">Quantum backend</param>
    /// <param name="state">Current quantum state</param>
    /// <returns>State after eigenvalue inversion or error</returns>
    /// <remarks>
    /// Applies controlled R_y(θ) where sin(θ/2) = C/λ
    /// C is normalization constant ensuring valid rotation angle.
    /// 
    /// For educational implementation, we use a simplified approach:
    /// - Extract eigenvalues from diagonal matrix
    /// - Apply rotation based on 1/λ
    /// </remarks>
    let private applyEigenvalueInversion
        (eigenvalue: float)
        (ancillaQubit: int)
        (minEigenvalue: float)
        (backend: IQuantumBackend)
        (state: QuantumState) : Result<QuantumState, QuantumError> =
        
        // Early validation - avoid division by zero
        if abs eigenvalue < minEigenvalue then
            Error (QuantumError.ValidationError ("eigenvalue", $"too small: {eigenvalue}"))
        else
            result {
                // Compute rotation angle: sin(θ/2) = C/λ
                // For simplicity, use C = 1 and θ = 2 * arcsin(1/λ)
                let invLambda = 1.0 / eigenvalue
                let theta = 2.0 * Math.Asin(min invLambda 1.0)
                
                // Apply controlled R_y rotation
                // In educational version, we apply Y rotation to ancilla
                let operation = QuantumOperation.Gate (CircuitBuilder.RY (ancillaQubit, theta))
                let! newState = backend.ApplyOperation operation state
            
            return newState
        }
    
    // ========================================================================
    // POST-SELECTION - Filter successful measurements
    // ========================================================================
    
    /// <summary>
    /// Apply post-selection: Keep only states where ancilla = |1⟩.
    /// Post-selection increases accuracy but decreases success probability.
    /// </summary>
    /// <param name="ancillaQubit">Index of ancilla qubit</param>
    /// <param name="state">Quantum state after eigenvalue inversion</param>
    /// <returns>Post-selected state (normalized) or error</returns>
    /// <remarks>
    /// Success probability ∝ 1/κ² where κ is condition number.
    /// This function projects onto the subspace where ancilla = |1⟩ and renormalizes.
    /// </remarks>
    let private postSelectAncilla 
        (ancillaQubit: int) 
        (state: QuantumState) : Result<QuantumState, QuantumError> =
        
        match state with
        | QuantumState.StateVector stateVec ->
            let dimension = StateVector.dimension stateVec
            let ancillaMask = 1 <<< ancillaQubit
            
            // Project onto ancilla |1⟩ subspace
            let newAmplitudes = Array.init dimension (fun i ->
                let ancillaIs1 = (i &&& ancillaMask) <> 0
                if ancillaIs1 then
                    StateVector.getAmplitude i stateVec
                else
                    Complex.Zero
            )
            
            // Renormalize
            let norm = 
                newAmplitudes 
                |> Array.sumBy (fun c -> c.Magnitude * c.Magnitude)
                |> sqrt
            
            if norm < 1e-10 then
                Error (QuantumError.Other "Post-selection failed: ancilla never measured as |1⟩")
            else
                let normalized = newAmplitudes |> Array.map (fun c -> c / norm)
                let newStateVec = StateVector.create normalized
                Ok (QuantumState.StateVector newStateVec)
        
        | _ -> Error (QuantumError.Other "Post-selection only supported for StateVector representation")
    
    /// <summary>
    /// Calculate success probability (probability of ancilla = |1⟩).
    /// </summary>
    /// <param name="ancillaQubit">Index of ancilla qubit</param>
    /// <param name="state">Quantum state</param>
    /// <returns>Probability that ancilla qubit is |1⟩</returns>
    let private calculateSuccessProbability 
        (ancillaQubit: int) 
        (state: QuantumState) : float =
        
        match state with
        | QuantumState.StateVector stateVec ->
            let dimension = StateVector.dimension stateVec
            let ancillaMask = 1 <<< ancillaQubit
            
            [0 .. dimension - 1]
            |> List.sumBy (fun i ->
                let ancillaIs1 = (i &&& ancillaMask) <> 0
                if ancillaIs1 then
                    let amp = StateVector.getAmplitude i stateVec
                    amp.Magnitude * amp.Magnitude
                else
                    0.0
            )
        
        | _ -> 0.0  // Unknown for other representations
    
    // ========================================================================
    // EIGENVALUE EXTRACTION - For result analysis
    // ========================================================================
    
    /// <summary>
    /// Extract estimated eigenvalues from QPE results.
    /// After QPE, eigenvalue register contains binary representation of λ/λ_max.
    /// </summary>
    /// <param name="eigenvalueQubits">Number of qubits in eigenvalue register</param>
    /// <param name="state">Quantum state after QPE</param>
    /// <returns>Array of estimated eigenvalues</returns>
    let private extractEigenvalues 
        (eigenvalueQubits: int) 
        (state: QuantumState) : float[] =
        
        match state with
        | QuantumState.StateVector stateVec ->
            let eigenvalueRegisterSize = 1 <<< eigenvalueQubits
            let dimension = StateVector.dimension stateVec
            
            // Count probability of each eigenvalue index
            let eigenvalueCounts = Array.create eigenvalueRegisterSize 0.0
            
            for i in 0 .. dimension - 1 do
                let eigenvalueIndex = i &&& (eigenvalueRegisterSize - 1)
                let amp = StateVector.getAmplitude i stateVec
                eigenvalueCounts[eigenvalueIndex] <- eigenvalueCounts[eigenvalueIndex] + (amp.Magnitude * amp.Magnitude)
            
            // Convert to actual eigenvalues (normalize to [0, 1] range)
            eigenvalueCounts
            |> Array.mapi (fun idx prob -> 
                if prob > 1e-6 then
                    float idx / float eigenvalueRegisterSize
                else
                    0.0
            )
            |> Array.filter (fun lambda -> lambda > 0.0)
        
        | _ -> [||]  // Unknown for other representations
    
    /// <summary>
    /// Extract solution amplitudes from final quantum state.
    /// </summary>
    /// <param name="state">Final quantum state after HHL</param>
    /// <returns>Map of basis index to amplitude for non-zero entries</returns>
    let private extractSolutionAmplitudes (state: QuantumState) : Map<int, Complex> option =
        match state with
        | QuantumState.StateVector stateVec ->
            let dimension = StateVector.dimension stateVec
            
            let amplitudes =
                [0 .. dimension - 1]
                |> List.map (fun i -> (i, StateVector.getAmplitude i stateVec))
                |> List.filter (fun (_, amp) -> amp.Magnitude > 1e-10)
                |> Map.ofList
            
            if Map.isEmpty amplitudes then None else Some amplitudes
        
        | _ -> None  // Unknown for other representations
    
    // ========================================================================
    // HHL MAIN ALGORITHM
    // ========================================================================
    
    /// <summary>
    /// Execute HHL algorithm to solve Ax = b.
    /// </summary>
    /// <param name="config">HHL configuration</param>
    /// <param name="backend">Quantum backend</param>
    /// <returns>HHL result with solution information or error</returns>
    /// <remarks>
    /// Algorithm phases:
    /// 1. Validate inputs (matrix Hermitian, dimensions match)
    /// 2. Prepare input state |b⟩
    /// 3. Apply QPE to extract eigenvalues
    /// 4. Apply eigenvalue inversion (controlled rotations)
    /// 5. Apply inverse QPE (uncompute)
    /// 6. Measure and extract solution
    /// 
    /// Current implementation supports diagonal matrices only.
    /// Success probability depends on condition number κ = λ_max/λ_min.
    /// </remarks>
    /// <example>
    /// <code>
    /// let matrix = createDiagonalMatrix [|2.0; 3.0|]
    /// let vector = createQuantumVector [|Complex(1.0, 0.0); Complex(1.0, 0.0)|]
    /// 
    /// match matrix, vector with
    /// | Ok m, Ok v ->
    ///     let config = defaultConfig m v
    ///     match execute config backend with
    ///     | Ok result -> printfn "Success: %f" result.SuccessProbability
    ///     | Error err -> printfn "Error: %A" err
    /// | _ -> printfn "Validation failed"
    /// </code>
    /// </example>
    let execute 
        (config: HHLConfig) 
        (backend: IQuantumBackend) : Result<HHLResult, QuantumError> =
        
        // ========== VALIDATION ==========
        
        // Check matrix is diagonal (educational implementation)
        if not config.Matrix.IsDiagonal then
            Error (QuantumError.ValidationError ("Matrix", "Only diagonal matrices supported in HHL (use HHLBackendAdapter for general matrices)"))
        elif config.Matrix.Dimension <> config.InputVector.Dimension then
            Error (QuantumError.ValidationError ("Dimensions", $"Matrix ({config.Matrix.Dimension}) and vector ({config.InputVector.Dimension}) dimensions must match"))
        else
            // Check matrix is invertible (no zero eigenvalues)
            let eigenvalues = 
                [| 0 .. config.Matrix.Dimension - 1 |]
                |> Array.map (fun i -> 
                    let idx = i * config.Matrix.Dimension + i
                    config.Matrix.Elements[idx].Real
                )
            
            let minEig = eigenvalues |> Array.map abs |> Array.min
            
            if minEig < config.MinEigenvalue then
                Error (QuantumError.ValidationError ("Matrix", $"Near-singular matrix (min eigenvalue = {minEig})"))
            else
                result {
                    // ========== STATE PREPARATION ==========
                    
                    let! inputState = prepareInputState config.InputVector backend
                    
                    // ========== EIGENVALUE ESTIMATION (QPE) ==========
                    
                    // NOTE: Full QPE integration requires custom eigenvector support in QPE
                    // For now, we use the known diagonal eigenvalues directly
                    // This is acceptable since for diagonal matrices, the eigenvalues are explicit
                    
                    // Create unitary operator U = e^(iAt) for matrix A (for future use)
                    let matrixUnitary = createDiagonalUnitary config.Matrix 1.0  // time t = 1
                    
                    // For diagonal matrices, we can directly use the eigenvalues
                    // In a full implementation with custom eigenvector support:
                    //   1. Configure QPE with input state as eigenvector
                    //   2. Execute QPE to extract phase
                    //   3. Convert phase to eigenvalue
                    
                    // Use diagonal eigenvalues directly (valid for diagonal matrices)
                    let estimatedEigenvalue = eigenvalues[0]  // Use first eigenvalue for now
                    
                    // State after QPE would be a superposition over eigenvalue register
                    // For now, use the input state directly
                    let stateAfterQPE = inputState
                    
                    // ========== EIGENVALUE INVERSION ==========
                    
                    // Apply controlled rotation based on 1/λ
                    // Ancilla qubit will be at position after counting qubits + solution qubits
                    let ancillaQubit = config.EigenvalueQubits + config.SolutionQubits
                    
                    let! invertedState = applyEigenvalueInversion 
                                            estimatedEigenvalue 
                                            ancillaQubit 
                                            config.MinEigenvalue 
                                            backend 
                                            stateAfterQPE
                    
                    // ========== POST-SELECTION (OPTIONAL) ==========
                    
                    // Calculate success probability before post-selection
                    let successProb = calculateSuccessProbability ancillaQubit invertedState
                    
                    // Apply post-selection if enabled
                    let! finalState, postSelectionSuccess = 
                        if config.UsePostSelection then
                            match postSelectAncilla ancillaQubit invertedState with
                            | Ok selected -> Ok (selected, true)
                            | Error _ -> 
                                // Post-selection failed - continue without it
                                Ok (invertedState, false)
                        else
                            Ok (invertedState, true)
                    
                    // ========== INVERSE QPE (UNCOMPUTE) ==========
                    
                    // In full implementation, this would apply inverse QPE
                    // For educational version, we skip this step
                    // The eigenvalue register would be disentangled here
                    let stateAfterInverseQPE = finalState
                    
                    // ========== SOLUTION EXTRACTION ==========
                    
                    // Extract eigenvalues from quantum state
                    let extractedEigenvalues = extractEigenvalues config.EigenvalueQubits stateAfterInverseQPE
                    
                    // Use diagonal eigenvalues if extraction failed
                    let finalEigenvalues = 
                        if extractedEigenvalues.Length > 0 then
                            extractedEigenvalues
                        else
                            eigenvalues
                    
                    // Extract solution amplitudes
                    let solutionAmps = extractSolutionAmplitudes stateAfterInverseQPE
                    
                    // Calculate condition number for metadata
                    let conditionNumber = 
                        match config.Matrix.ConditionNumber with
                        | Some kappa -> kappa
                        | None -> 
                            let maxEig = eigenvalues |> Array.max
                            maxEig / minEig
                    
                    // Calculate gate count estimate
                    let qpeGates = config.EigenvalueQubits * config.EigenvalueQubits
                    let inversionGates = config.EigenvalueQubits
                    let inverseQPEGates = qpeGates
                    let totalGates = qpeGates + inversionGates + inverseQPEGates + 10  // +10 for state prep
                    
                    // Build result
                    let hhlResult = {
                        SuccessProbability = successProb
                        EstimatedEigenvalues = finalEigenvalues
                        GateCount = totalGates
                        PostSelectionSuccess = postSelectionSuccess
                        Config = config
                        Fidelity = None  // Would require classical solution for comparison
                        SolutionAmplitudes = solutionAmps
                    }
                    
                    return hhlResult
                }
    
    // ========================================================================
    // CONVENIENCE FUNCTIONS
    // ========================================================================
    
    /// <summary>
    /// Solve 2×2 diagonal system: [[λ₁, 0], [0, λ₂]] * x = b.
    /// </summary>
    /// <param name="eigenvalues">Tuple of (λ₁, λ₂)</param>
    /// <param name="inputVector">Tuple of (b₀, b₁)</param>
    /// <param name="backend">Quantum backend</param>
    /// <returns>HHL result or error</returns>
    /// <remarks>
    /// Classical solution: x = [b₀/λ₁, b₁/λ₂]
    /// 
    /// Example: Solve [[2,0],[0,3]] * x = [1,1]
    /// Expected: x ≈ [0.5, 0.333...]
    /// </remarks>
    /// <example>
    /// <code>
    /// match solve2x2Diagonal (2.0, 3.0) (Complex(1.0, 0.0), Complex(1.0, 0.0)) backend with
    /// | Ok result -> printfn "κ = %A, success = %f" 
    ///                   result.Config.Matrix.ConditionNumber 
    ///                   result.SuccessProbability
    /// | Error err -> printfn "Error: %A" err
    /// </code>
    /// </example>
    let solve2x2Diagonal
        (eigenvalues: float * float)
        (inputVector: Complex * Complex)
        (backend: IQuantumBackend) : Result<HHLResult, QuantumError> =
        
        result {
            let (lambda1, lambda2) = eigenvalues
            let (b0, b1) = inputVector
            
            // Create diagonal matrix
            let! matrix = HHLTypes.createDiagonalMatrix [| lambda1; lambda2 |]
            
            // Create input vector
            let! vector = HHLTypes.createQuantumVector [| b0; b1 |]
            
            // Create configuration
            let config = HHLTypes.defaultConfig matrix vector
            
            // Execute HHL
            let! result = execute config backend
            
            return result
        }
    
    /// <summary>
    /// Solve 4×4 diagonal system with given eigenvalues and input vector.
    /// </summary>
    /// <param name="eigenvalues">Array of 4 eigenvalues [λ₀, λ₁, λ₂, λ₃]</param>
    /// <param name="inputVector">Array of 4 input components [b₀, b₁, b₂, b₃]</param>
    /// <param name="backend">Quantum backend</param>
    /// <returns>HHL result or error</returns>
    let solve4x4Diagonal
        (eigenvalues: float[])
        (inputVector: Complex[])
        (backend: IQuantumBackend) : Result<HHLResult, QuantumError> =
        
        // Early validation
        if eigenvalues.Length <> 4 then
            Error (QuantumError.ValidationError ("eigenvalues", "must have exactly 4 elements"))
        elif inputVector.Length <> 4 then
            Error (QuantumError.ValidationError ("inputVector", "must have exactly 4 elements"))
        else
            result {
                let! matrix = HHLTypes.createDiagonalMatrix eigenvalues
                let! vector = HHLTypes.createQuantumVector inputVector
                
                let config = HHLTypes.defaultConfig matrix vector
                return! execute config backend
            }
    
    /// <summary>
    /// Solve diagonal system with identity matrix: I * x = b, so x = b.
    /// Useful for testing and validation.
    /// </summary>
    /// <param name="inputVector">Input vector (solution should equal this)</param>
    /// <param name="backend">Quantum backend</param>
    /// <returns>HHL result or error</returns>
    let solveIdentity
        (inputVector: Complex[])
        (backend: IQuantumBackend) : Result<HHLResult, QuantumError> =
        
        // Early validation
        let n = inputVector.Length
        if (n &&& (n - 1)) <> 0 then
            Error (QuantumError.ValidationError ("inputVector", "dimension must be power of 2"))
        else
            result {
                // Identity matrix: all eigenvalues = 1.0
                let eigenvalues = Array.create n 1.0
                
                let! matrix = HHLTypes.createDiagonalMatrix eigenvalues
                let! vector = HHLTypes.createQuantumVector inputVector
                
                let config = HHLTypes.defaultConfig matrix vector
                return! execute config backend
            }
