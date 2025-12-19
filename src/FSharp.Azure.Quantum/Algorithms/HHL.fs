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
/// - Diagonal matrices: simple path and (optionally) native intent on some backends.
/// - General Hermitian matrices: explicit gate lowering + backend gate transpilation.
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
        // NOTE:
        // The native `AlgorithmOperation.HHL` intent payload uses diagonal eigenvalues.
        // For general Hermitian matrices, this module executes via explicit gate lowering instead.
        
        let diagonalEigenvalues =
            let dim = intent.Matrix.Dimension
            [| for i in 0 .. dim - 1 -> intent.Matrix.Elements[i * dim + i].Real |]

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
         let solutionQubits = int (Math.Log(float vectorDim, 2.0))
         if (1 <<< solutionQubits) <> vectorDim then
             Error (QuantumError.ValidationError ("InputVector", $"dimension must be power of 2, got {vectorDim}"))
         else
             let eigenvalueQubits = totalQubits - solutionQubits - 1
             if eigenvalueQubits < 0 then
                 Error (QuantumError.ValidationError ("totalQubits", $"totalQubits too small for vector dimension: {totalQubits}"))
             else
                 // Create full state with all qubits (eigenvalue register + solution register + ancilla)
                 let fullDimension = 1 <<< totalQubits
                 let amplitudes = Array.create fullDimension Complex.Zero

                 // Expected qubit layout (little-endian basis index):
                 //   [0 .. eigenvalueQubits-1] = eigenvalue register (LSBs)
                 //   [eigenvalueQubits .. eigenvalueQubits+solutionQubits-1] = solution register
                 //   [eigenvalueQubits+solutionQubits] = ancilla
                 //
                 // Place |b⟩ into the solution register while keeping eigenvalue=0 and ancilla=0.
                 for i in 0 .. vectorDim - 1 do
                     let basisIndex = i <<< eigenvalueQubits
                     amplitudes[basisIndex] <- inputVector.Components[i]

                 // Normalize (inputVector is already normalized, but keep this robust)
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
    /// 
    /// Interprets the solution as amplitudes on the solution register when:
    /// - eigenvalue register is |0...0⟩ (after uncomputation)
    /// - ancilla is |1⟩ (successful HHL branch)
    /// </summary>
    let private extractSolutionAmplitudes
         (eigenvalueQubits: int)
         (solutionQubits: int)
         (ancillaQubit: int)
         (state: QuantumState)
         : Map<int, Complex> option =
         match state with
         | QuantumState.StateVector stateVec ->
             let solutionDim = 1 <<< solutionQubits
             let ancillaMask = 1 <<< ancillaQubit
             let amplitudes =
                 [0 .. solutionDim - 1]
                 |> List.choose (fun i ->
                     let basisIndex = (i <<< eigenvalueQubits) ||| ancillaMask
                     let amp = StateVector.getAmplitude basisIndex stateVec
                     if amp.Magnitude > 1e-10 then Some (i, amp) else None)
                 |> Map.ofList

             if Map.isEmpty amplitudes then None else Some amplitudes
         | _ -> None
    
    // ========================================================================
    // HHL MAIN ALGORITHM
    // ========================================================================

    let private validateIntent (intent: HhlExecutionIntent) : Result<float[] * float * float * float, QuantumError> =
        if intent.Matrix.Dimension <> intent.InputVector.Dimension then
            Error (QuantumError.ValidationError ("Dimensions", $"Matrix ({intent.Matrix.Dimension}) and vector ({intent.InputVector.Dimension}) dimensions must match"))
        else
            // If we have a diagonal matrix, prefer exact eigenvalues for normalization checks.
            let diagonalEigenvalues =
                if intent.Matrix.IsDiagonal then
                    let dim = intent.Matrix.Dimension
                    Array.init dim (fun i -> intent.Matrix.Elements[i * dim + i].Real)
                else
                    [||]

            // Avoid classical eigen-decomposition on the execution/planning path.
            // For validation we only need bounds for min/max eigenvalues.
            let (minEig, maxEig) =
                if intent.Matrix.IsDiagonal then
                    let absEigenvalues = diagonalEigenvalues |> Array.map abs
                    (Array.min absEigenvalues, Array.max absEigenvalues)
                else
                    let maxEig = abs (HHLTypes.estimateMaxEigenvalue intent.Matrix 100 1e-6)
                    let minEig = abs (HHLTypes.estimateMinEigenvalue intent.Matrix 100 1e-6)
                    (minEig, maxEig)

            if minEig < intent.MinEigenvalue then
                Error (QuantumError.ValidationError ("Matrix", $"Near-singular matrix (min eigenvalue = {minEig})"))
            else
                let conditionNumber =
                    if minEig > 0.0 then maxEig / minEig else Double.PositiveInfinity

                Ok (diagonalEigenvalues, minEig, conditionNumber, maxEig)

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
        (spectrumEigenvalues: float[])
        (maxEig: float)
        : Result<QuantumOperation list * int * int, QuantumError> =

         let ancillaQubit = intent.EigenvalueQubits + intent.SolutionQubits

         if intent.Matrix.IsDiagonal then
             // Diagonal path: single ancilla rotation (simplified baseline).
             let estimatedEigenvalue = spectrumEigenvalues[0]

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
         else
             result {
                 // General-matrix path: perform QPE using controlled exp(-iAt), then apply an
                 // ancilla rotation multiplexed by the eigenvalue register.
                 //
                 // Constraint: use gate-only Hamiltonian simulation (Trotter-Suzuki), not local-only
                 // unitary synthesis of exp(-iAt).

                 // Choose a phase scaling constant C so that phi = lambda / C lies in [0, 1).
                 // Using 2*maxEig keeps phases away from the wrap-around at 1.0.
                 let phaseScale = 2.0 * maxEig

                 // We want U = exp(2π i * (A / phaseScale)) so that positive eigenvalues encode positive phases.
                 // Our evolution primitive synthesizes exp(-i A t), so choose negative time: t0 = -2π / phaseScale.
                 let baseTime = -2.0 * Math.PI / phaseScale

                 let eigenQubits = [ 0 .. intent.EigenvalueQubits - 1 ]
                 let eigenQubitArray = eigenQubits |> List.toArray
                 let solutionQubits = [ intent.EigenvalueQubits .. intent.EigenvalueQubits + intent.SolutionQubits - 1 ]

                 let hadamards = eigenQubits |> List.map (fun q -> QuantumOperation.Gate (CircuitBuilder.H q))

                 let totalQubits = intent.EigenvalueQubits + intent.SolutionQubits + 1

                 let toMatrix2D (matrix: HermitianMatrix) : Complex[,] =
                     let dim = matrix.Dimension
                     Array2D.init dim dim (fun i j -> matrix.Elements[i * dim + j])

                 let matrix2D = toMatrix2D intent.Matrix

                 let! pauliHamiltonian = TrotterSuzuki.decomposeMatrixToPauli matrix2D

                 let trotterSteps = max 10 (2 * intent.QpePrecision)
                 let trotterOrder = 2

                 let buildControlledEvolutionOps (controlQubit: int) (time: float) : QuantumOperation list =
                     let config : TrotterSuzuki.TrotterConfig = { NumSteps = trotterSteps; Time = time; Order = trotterOrder }
                     let evolved =
                         TrotterSuzuki.synthesizeControlledHamiltonianEvolution
                             controlQubit
                             pauliHamiltonian
                             config
                             (solutionQubits |> List.toArray)
                             (CircuitBuilder.empty totalQubits)

                     evolved.Gates |> List.map QuantumOperation.Gate

                 let controlledEvolutions =
                     eigenQubits
                     |> List.collect (fun j ->
                         let time = baseTime * float (1 <<< j)
                         buildControlledEvolutionOps j time)

                 let calculatePhaseAngle (k: int) (inverse: bool) : float =
                     let angle = 2.0 * Math.PI / float (1 <<< k)
                     if inverse then -angle else angle

                 let buildQftLoweringOps (numQubits: int) (inverse: bool) : QuantumOperation list =
                     if inverse then
                         [numQubits - 1 .. -1 .. 0]
                         |> List.collect (fun targetQubit ->
                             let phases =
                                 [numQubits - 1 .. -1 .. targetQubit + 1]
                                 |> List.map (fun k ->
                                     let power = k - targetQubit + 1
                                     let angle = calculatePhaseAngle power true
                                     QuantumOperation.Gate (CircuitBuilder.CP (k, targetQubit, angle)))

                             let hOp = QuantumOperation.Gate (CircuitBuilder.H targetQubit)
                             phases @ [hOp])
                     else
                         [0 .. numQubits - 1]
                         |> List.collect (fun targetQubit ->
                             let hOp = QuantumOperation.Gate (CircuitBuilder.H targetQubit)
                             let phases =
                                 [targetQubit + 1 .. numQubits - 1]
                                 |> List.map (fun k ->
                                     let power = k - targetQubit + 1
                                     let angle = calculatePhaseAngle power false
                                     QuantumOperation.Gate (CircuitBuilder.CP (k, targetQubit, angle)))

                             hOp :: phases)

                 let inverseQft = buildQftLoweringOps intent.EigenvalueQubits true

                 // Gate-only multiplexed/uniformly controlled RY.
                 // For each eigen-register basis state |k⟩, apply RY(angles[k]) to ancilla.
                 //
                 // When ApplySwaps=false, the inverse QFT leaves the eigen register in bit-reversed order.
                 // Compensate by bit-reversing k before mapping it to an estimated eigenvalue.
                 let eigenRegisterSize = 1 <<< intent.EigenvalueQubits

                 let bitReverse (value: int) : int =
                     let rec loop bitsRemaining v rev =
                         if bitsRemaining = 0 then
                             rev
                         else
                             loop (bitsRemaining - 1) (v >>> 1) ((rev <<< 1) ||| (v &&& 1))

                     loop intent.EigenvalueQubits value 0

                 let angles =
                     Array.init eigenRegisterSize (fun k ->
                         if k = 0 then
                             0.0
                         else
                             let canonicalK = bitReverse k
                             let lambdaEst = (float canonicalK / float eigenRegisterSize) * phaseScale

                             // Avoid saturation: ensure the normalization constant is <= estimated eigenvalue.
                             let safeMethod =
                                 match intent.InversionMethod with
                                 | EigenvalueInversionMethod.ExactRotation c -> EigenvalueInversionMethod.ExactRotation (min c lambdaEst)
                                 | EigenvalueInversionMethod.LinearApproximation c -> EigenvalueInversionMethod.LinearApproximation (min c lambdaEst)
                                 | EigenvalueInversionMethod.PiecewiseLinear _ -> intent.InversionMethod

                             match inversionRotationAngle safeMethod lambdaEst intent.MinEigenvalue with
                             | Ok theta -> theta
                             | Error _ -> 0.0)

                 let multiControlledXGates (controls: int list) (target: int) : CircuitBuilder.Gate list =
                     match controls with
                     | [] -> [ CircuitBuilder.X target ]
                     | _ ->
                         // Multi-controlled X can be expressed as H·MCZ·H.
                         [ CircuitBuilder.H target
                           CircuitBuilder.MCZ (controls, target)
                           CircuitBuilder.H target ]

                 let multiControlledRyGates (controls: int list) (target: int) (theta: float) : CircuitBuilder.Gate list =
                     if abs theta < 1e-12 then
                         []
                     elif controls.IsEmpty then
                         [ CircuitBuilder.RY (target, theta) ]
                     else
                         // Generalize the CRY decomposition by replacing CNOT with MCX.
                         let mcx = multiControlledXGates controls target
                         [ CircuitBuilder.RY (target, theta / 2.0) ]
                         @ mcx
                         @ [ CircuitBuilder.RY (target, -theta / 2.0) ]
                         @ mcx

                 let multiplexedRyGates =
                     [ 0 .. eigenRegisterSize - 1 ]
                     |> List.collect (fun k ->
                         let theta = angles[k]
                         if abs theta < 1e-12 then
                             []
                         else
                             // Flip controls where k has 0 bits so that k maps to all-ones.
                             let xFlips =
                                 [ 0 .. intent.EigenvalueQubits - 1 ]
                                 |> List.choose (fun bitIdx ->
                                     if ((k >>> bitIdx) &&& 1) = 0 then
                                         Some (CircuitBuilder.X eigenQubitArray[bitIdx])
                                     else
                                         None)

                             xFlips
                             @ multiControlledRyGates eigenQubits ancillaQubit theta
                             @ xFlips)

                 let multiplexedRy = multiplexedRyGates |> List.map QuantumOperation.Gate

                 let forwardQft = buildQftLoweringOps intent.EigenvalueQubits false

                 let controlledUncompute =
                     eigenQubits
                     |> List.collect (fun j ->
                         let time = -baseTime * float (1 <<< j)
                         buildControlledEvolutionOps j time)

                 let hadamardsUncompute = eigenQubits |> List.map (fun q -> QuantumOperation.Gate (CircuitBuilder.H q))

                 // Gate count estimate: rough but monotonic.
                 let gateCountEstimate =
                     let qpeOps = hadamards.Length + controlledEvolutions.Length + inverseQft.Length
                     let invOps = multiplexedRy.Length
                     let uncomputeOps = forwardQft.Length + controlledUncompute.Length + hadamardsUncompute.Length
                     qpeOps + invOps + uncomputeOps + 10

                 let ops =
                     hadamards
                     @ controlledEvolutions
                     @ inverseQft
                     @ multiplexedRy
                     @ forwardQft
                     @ controlledUncompute
                     @ hadamardsUncompute

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
                     let! (spectrumEigenvalues, _minEig, conditionNumber, maxEig) = validateIntent intent
                     let ancillaQubit = intent.EigenvalueQubits + intent.SolutionQubits

                     let op = hhlIntentOp intent spectrumEigenvalues
                     let exactness = intent.Exactness

                     // Only use the native intent implementation for diagonal matrices.
                     if intent.Matrix.IsDiagonal && backend.SupportsOperation op then
                         return (HhlPlan.ExecuteNatively (toCoreIntent intent, exactness), spectrumEigenvalues, ancillaQubit, conditionNumber)
                      else
                          let! (ops, _gateCount, _ancillaQubit) = buildLoweringOps intent spectrumEigenvalues maxEig

                          // Backends (Rigetti/IonQ/etc.) require transpilation into their supported gate sets.
                          // `UnifiedBackend.applySequence` does not transpile, so we must do it during planning.
                          let transpiledOps =
                              let gateOps =
                                  ops
                                  |> List.choose (function
                                      | QuantumOperation.Gate gate -> Some gate
                                      | _ -> None)

                              if gateOps.Length = ops.Length then
                                   let totalQubits = intent.EigenvalueQubits + intent.SolutionQubits + 1
                                   let circuit : CircuitBuilder.Circuit = { QubitCount = totalQubits; Gates = gateOps }

                                   // Some decompositions (e.g., MCZ -> CCX -> {RZ,CNOT,...}) require multiple passes.
                                   // Iterate to a fixpoint (bounded) to ensure we emit only backend-native gates.
                                   let rec transpileToFixpoint remaining (current: CircuitBuilder.Circuit) =
                                       if remaining <= 0 then
                                           current
                                       else
                                           let next = GateTranspiler.transpileForBackend backend.Name current
                                           if next.Gates = current.Gates then
                                               next
                                           else
                                               transpileToFixpoint (remaining - 1) next

                                   let transpiled = transpileToFixpoint 5 circuit
                                   transpiled.Gates |> List.map QuantumOperation.Gate
                              else
                                  // Non-gate operations should never appear in lowering, but keep this safe.
                                  ops

                          if transpiledOps |> List.forall backend.SupportsOperation then
                              return (HhlPlan.ExecuteViaOps (transpiledOps, exactness), spectrumEigenvalues, ancillaQubit, conditionNumber)
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
    /// Supports two execution strategies:
    /// - Diagonal matrices: may execute via native `AlgorithmOperation.HHL` intent on backends that support it.
    /// - General Hermitian matrices: executes via explicit gate lowering (QPE + Trotter-Suzuki simulation + multiplexed rotation),
    ///   then backend-specific gate transpilation during planning.
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

            let solutionAmps = extractSolutionAmplitudes intent.EigenvalueQubits intent.SolutionQubits ancillaQubit finalState
            let solution =
                 match finalState with
                 | QuantumState.StateVector stateVec ->
                     let solutionDim = 1 <<< intent.SolutionQubits
                     let ancillaMask = 1 <<< ancillaQubit
                     Array.init solutionDim (fun i ->
                         let basisIndex = (i <<< intent.EigenvalueQubits) ||| ancillaMask
                         StateVector.getAmplitude basisIndex stateVec)
                 | _ ->
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
