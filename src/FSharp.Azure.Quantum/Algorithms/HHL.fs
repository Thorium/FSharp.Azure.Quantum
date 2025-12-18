namespace FSharp.Azure.Quantum.Algorithms

open System
open System.Numerics
open FSharp.Azure.Quantum
open FSharp.Azure.Quantum.Core
open FSharp.Azure.Quantum.Core.BackendAbstraction
open FSharp.Azure.Quantum.LocalSimulator

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
    
    // ========================================================================
    // INTENT → PLAN → EXECUTION (ADR: intent-first algorithms)
    // ========================================================================

    [<RequireQualifiedAccess>]
    type Exactness =
        | Exact
        | Approximate of epsilon: float

    type HhlExecutionIntent = {
        Matrix: HermitianMatrix
        InputVector: QuantumVector
        EigenvalueQubits: int
        SolutionQubits: int
        InversionMethod: EigenvalueInversionMethod
        MinEigenvalue: float
        UsePostSelection: bool
        QpePrecision: int
        Exactness: Exactness
    }

    [<RequireQualifiedAccess>]
    type HhlPlan =
        /// Execute the semantic HHL intent natively, if supported by backend.
        | ExecuteNatively of intent: BackendAbstraction.HhlIntent * exactness: Exactness

        /// Execute via explicit lowering to operations, if supported.
        | ExecuteViaOps of ops: QuantumOperation list * exactness: Exactness

    let private toExecutionIntent (config: HHLConfig) : HhlExecutionIntent =
        {
            Matrix = config.Matrix
            InputVector = config.InputVector
            EigenvalueQubits = config.EigenvalueQubits
            SolutionQubits = config.SolutionQubits
            InversionMethod = config.InversionMethod
            MinEigenvalue = config.MinEigenvalue
            UsePostSelection = config.UsePostSelection
            QpePrecision = config.QPEPrecision
            Exactness = Exactness.Exact
        }

    let private toCoreIntent (intent: HhlExecutionIntent) : BackendAbstraction.HhlIntent =
        // Current educational implementation supports diagonal matrices only.
        let diagonalEigenvalues =
            if intent.Matrix.IsDiagonal then
                let dim = intent.Matrix.Dimension
                [| for i in 0 .. dim - 1 -> intent.Matrix.Elements[i * dim + i].Real |]
            else
                [||]

        let inversionMethod =
            match intent.InversionMethod with
            | EigenvalueInversionMethod.ExactRotation c -> HhlEigenvalueInversionMethod.ExactRotation c
            | EigenvalueInversionMethod.LinearApproximation c -> HhlEigenvalueInversionMethod.LinearApproximation c
            | EigenvalueInversionMethod.PiecewiseLinear segments -> HhlEigenvalueInversionMethod.PiecewiseLinear segments

        {
            EigenvalueQubits = intent.EigenvalueQubits
            SolutionQubits = intent.SolutionQubits
            DiagonalEigenvalues = diagonalEigenvalues
            InversionMethod = inversionMethod
            MinEigenvalue = intent.MinEigenvalue
        }

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
        (totalQubits: int)
        (backend: IQuantumBackend) : Result<QuantumState, QuantumError> =
        
        // Calculate number of qubits for input vector
        let vectorDim = inputVector.Components.Length
        let solutionQubits = int (ceil (log (float vectorDim) / log 2.0))
        
        // Create full state with all qubits (eigenvalue register + solution register + ancilla)
        let fullDimension = 1 <<< totalQubits
        let amplitudes = Array.create fullDimension Complex.Zero
        
        // Place input vector amplitudes in the solution register part of the state
        // Solution register is at the end (rightmost qubits)
        for i in 0 .. vectorDim - 1 do
            amplitudes[i] <- inputVector.Components[i]
        
        // Normalize
        let norm = sqrt (amplitudes |> Array.sumBy (fun a -> (a * Complex.Conjugate(a)).Real))
        let normalizedAmplitudes = 
            if norm > 1e-10 then
                amplitudes |> Array.map (fun a -> a / norm)
            else
                amplitudes
        
        let stateVector = StateVector.create normalizedAmplitudes
        
        Ok (QuantumState.StateVector stateVector)
    
    // ========================================================================
    // POST-SELECTION - Filter successful measurements
    // ========================================================================
    
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
    /// <param name="conditionNumber">Condition number κ = λ_max / λ_min (optional)</param>
    /// <returns>Probability that ancilla qubit is |1⟩</returns>
    /// <remarks>
    /// For HHL, the success probability depends on the condition number κ.
    /// Theoretical bound: P_success ≥ 1/κ²
    /// Actual probability depends on input state overlap with eigenvectors.
    /// 
    /// If conditionNumber is provided and > 1, we apply a correction factor
    /// to give a more realistic estimate that accounts for the worst-case
    /// eigenvalue scaling.
    /// </remarks>
    let private calculateSuccessProbability 
        (ancillaQubit: int) 
        (state: QuantumState) 
        (conditionNumber: float option) : float =
        
        match state with
        | QuantumState.StateVector stateVec ->
            let dimension = StateVector.dimension stateVec
            let ancillaMask = 1 <<< ancillaQubit
            
            let measuredProb =
                [0 .. dimension - 1]
                |> List.sumBy (fun i ->
                    let ancillaIs1 = (i &&& ancillaMask) <> 0
                    if ancillaIs1 then
                        let amp = StateVector.getAmplitude i stateVec
                        amp.Magnitude * amp.Magnitude
                    else
                        0.0
                )
            
            // Apply condition number correction if provided
            // This gives a more realistic estimate for poorly-conditioned matrices
            match conditionNumber with
            | Some kappa when kappa > 1.0 ->
                // Theoretical worst-case: P_success ∝ 1/κ²
                // Use geometric mean between measured and theoretical bound
                let theoreticalBound = 1.0 / (kappa * kappa)
                sqrt (measuredProb * theoreticalBound)
            | _ ->
                measuredProb
        
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

    let private validateIntent (intent: HhlExecutionIntent) : Result<float[] * float * float, QuantumError> =
        if not intent.Matrix.IsDiagonal then
            Error (QuantumError.ValidationError ("Matrix", "Only diagonal matrices supported in HHL (use HHLBackendAdapter for general matrices)"))
        elif intent.Matrix.Dimension <> intent.InputVector.Dimension then
            Error (QuantumError.ValidationError ("Dimensions", $"Matrix ({intent.Matrix.Dimension}) and vector ({intent.InputVector.Dimension}) dimensions must match"))
        else
            let eigenvalues =
                [| 0 .. intent.Matrix.Dimension - 1 |]
                |> Array.map (fun i ->
                    let idx = i * intent.Matrix.Dimension + i
                    intent.Matrix.Elements[idx].Real
                )

            let absEigenvalues = eigenvalues |> Array.map abs
            let minEig = absEigenvalues |> Array.min
            let maxEig = absEigenvalues |> Array.max

            if minEig < intent.MinEigenvalue then
                Error (QuantumError.ValidationError ("Matrix", $"Near-singular matrix (min eigenvalue = {minEig})"))
            else
                let conditionNumber = if minEig > 0.0 then maxEig / minEig else Double.PositiveInfinity
                Ok (eigenvalues, minEig, conditionNumber)

    let private clampToUnit (x: float) : float =
        if x > 1.0 then 1.0
        elif x < -1.0 then -1.0
        else x

    let private inversionRotationAngle (method: EigenvalueInversionMethod) (eigenvalue: float) (minEigenvalue: float) : Result<float, QuantumError> =
        if abs eigenvalue < minEigenvalue then
            Error (QuantumError.ValidationError ("eigenvalue", $"too small: {eigenvalue}"))
        else
            match method with
            | EigenvalueInversionMethod.ExactRotation normalizationConstant
            | EigenvalueInversionMethod.LinearApproximation normalizationConstant ->
                let invLambda = normalizationConstant / eigenvalue
                Ok (2.0 * Math.Asin(clampToUnit invLambda))
            | EigenvalueInversionMethod.PiecewiseLinear segments ->
                let absLambda = abs eigenvalue
                let constant =
                    segments
                    |> Array.tryFind (fun (minL, maxL, _) -> absLambda >= minL && absLambda < maxL)
                    |> Option.map (fun (_, _, c) -> c)
                    |> Option.defaultValue 1.0
                let invLambda = constant / eigenvalue
                Ok (2.0 * Math.Asin(clampToUnit invLambda))

    /// Build an intent-first operation for HHL.
    let private hhlIntentOp (intent: HhlExecutionIntent) (diagonalEigenvalues: float[]) : QuantumOperation =
        let inversionMethod =
            match intent.InversionMethod with
            | EigenvalueInversionMethod.ExactRotation c -> HhlEigenvalueInversionMethod.ExactRotation c
            | EigenvalueInversionMethod.LinearApproximation c -> HhlEigenvalueInversionMethod.LinearApproximation c
            | EigenvalueInversionMethod.PiecewiseLinear segments -> HhlEigenvalueInversionMethod.PiecewiseLinear segments

        QuantumOperation.Algorithm (AlgorithmOperation.HHL {
            EigenvalueQubits = intent.EigenvalueQubits
            SolutionQubits = intent.SolutionQubits
            DiagonalEigenvalues = diagonalEigenvalues
            InversionMethod = inversionMethod
            MinEigenvalue = intent.MinEigenvalue
        })

    let private buildLoweringOps
        (intent: HhlExecutionIntent)
        (diagonalEigenvalues: float[])
        : Result<QuantumOperation list * int * int, QuantumError> =

        // NOTE: This educational implementation does not yet perform full QPE.
        // We keep the intent/planning separation, and model the current implementation as:
        // - amplitude-encoded input state prepared directly (not via operations)
        // - a single ancilla rotation based on one representative eigenvalue
        // - optional post-selection in classical post-processing

        let ancillaQubit = intent.EigenvalueQubits + intent.SolutionQubits
        let estimatedEigenvalue = diagonalEigenvalues[0]

        result {
            let! theta = inversionRotationAngle intent.InversionMethod estimatedEigenvalue intent.MinEigenvalue
            let ops = [ QuantumOperation.Gate (CircuitBuilder.RY (ancillaQubit, theta)) ]
            let gateCountEstimate =
                // Keep the previous estimate structure for stability.
                let qpeGates = intent.EigenvalueQubits * intent.EigenvalueQubits
                let inversionGates = intent.EigenvalueQubits
                let inverseQpeGates = qpeGates
                qpeGates + inversionGates + inverseQpeGates + 10

            return (ops, gateCountEstimate, ancillaQubit)
        }

    let plan (backend: IQuantumBackend) (intent: HhlExecutionIntent) : Result<HhlPlan * float[] * int * float, QuantumError> =
        result {
            match backend.NativeStateType with
            | QuantumStateType.Annealing ->
                return! Error (QuantumError.OperationError ("HHL", $"Backend '{backend.Name}' does not support HHL (native state type: {backend.NativeStateType})"))
            | _ ->
                match intent.Exactness with
                | Exactness.Approximate epsilon when epsilon <= 0.0 ->
                    return! Error (QuantumError.ValidationError ("Exactness", "epsilon must be positive"))
                | _ ->
                    let! (eigenvalues, _minEig, conditionNumber) = validateIntent intent
                    let ancillaQubit = intent.EigenvalueQubits + intent.SolutionQubits

                    let op = hhlIntentOp intent eigenvalues
                    // Current educational strategy is not full HHL, but the *rotation* we apply is effectively exact.
                    // Keep exactness in the plan so call sites can reason about it.
                    let exactness = intent.Exactness

                    if backend.SupportsOperation op then
                        return (HhlPlan.ExecuteNatively (toCoreIntent intent, exactness), eigenvalues, ancillaQubit, conditionNumber)
                    else
                        let! (ops, _gateCount, _ancillaQubit) = buildLoweringOps intent eigenvalues
                        if ops |> List.forall backend.SupportsOperation then
                            return (HhlPlan.ExecuteViaOps (ops, exactness), eigenvalues, ancillaQubit, conditionNumber)
                        else
                            return! Error (QuantumError.OperationError ("HHL", $"Backend '{backend.Name}' does not support required operations for HHL"))
        }

    let private executePlan
        (backend: IQuantumBackend)
        (state: QuantumState)
        (plan: HhlPlan)
        : Result<QuantumState, QuantumError> =

        match plan with
        | HhlPlan.ExecuteNatively (intent, _) ->
            backend.ApplyOperation (QuantumOperation.Algorithm (AlgorithmOperation.HHL intent)) state
        | HhlPlan.ExecuteViaOps (ops, _) ->
            UnifiedBackend.applySequence backend ops state

    /// <summary>
    /// Execute HHL algorithm to solve Ax = b.
    /// </summary>
    /// <param name="config">HHL configuration</param>
    /// <param name="backend">Quantum backend</param>
    /// <returns>HHL result with solution information or error</returns>
    /// <remarks>
    /// Current implementation supports diagonal matrices only.
    /// </remarks>
    let execute
        (config: HHLConfig)
        (backend: IQuantumBackend)
        : Result<HHLResult, QuantumError> =

        result {
            let intent = toExecutionIntent config
            let totalQubits = intent.EigenvalueQubits + intent.SolutionQubits + 1

            let! (plan, diagonalEigenvalues, ancillaQubit, conditionNumber) = plan backend intent
            let! inputState = prepareInputState intent.InputVector totalQubits backend
            let! invertedState = executePlan backend inputState plan

            let successProb = calculateSuccessProbability ancillaQubit invertedState (Some conditionNumber)

            let! finalState, postSelectionSuccess =
                if intent.UsePostSelection then
                    match postSelectAncilla ancillaQubit invertedState with
                    | Ok selected -> Ok (selected, true)
                    | Error _ -> Ok (invertedState, false)
                else
                    Ok (invertedState, true)

            let extractedEigenvalues = extractEigenvalues intent.EigenvalueQubits finalState
            let finalEigenvalues = if extractedEigenvalues.Length > 0 then extractedEigenvalues else diagonalEigenvalues

            let solutionAmps = extractSolutionAmplitudes finalState
            let solution =
                match solutionAmps with
                | Some amplitudes ->
                    let dimension = intent.Matrix.Dimension
                    Array.init dimension (fun i -> amplitudes.TryFind i |> Option.defaultValue Complex.Zero)
                | None ->
                    Array.create intent.Matrix.Dimension Complex.Zero

            let gateCount =
                // Mirror the original estimate (independent of chosen plan).
                let qpeGates = intent.EigenvalueQubits * intent.EigenvalueQubits
                let inversionGates = intent.EigenvalueQubits
                let inverseQpeGates = qpeGates
                qpeGates + inversionGates + inverseQpeGates + 10

            return {
                Solution = solution
                SuccessProbability = successProb
                EstimatedEigenvalues = finalEigenvalues
                GateCount = gateCount
                PostSelectionSuccess = postSelectionSuccess
                Config = config
                Fidelity = None
                SolutionAmplitudes = solutionAmps
            }
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
