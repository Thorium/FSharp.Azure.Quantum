namespace FSharp.Azure.Quantum.Algorithms

open System
open FSharp.Azure.Quantum
open FSharp.Azure.Quantum.Core
open FSharp.Azure.Quantum.Core.BackendAbstraction
open System.Numerics

/// Quantum Phase Estimation (QPE) - Unified Backend Implementation
/// 
/// State-based implementation using IQuantumBackend.
/// Estimates the phase (eigenvalue) of a unitary operator U with respect to an eigenvector |ψ⟩.
/// Given U|ψ⟩ = e^(2πiφ)|ψ⟩, QPE estimates φ to n bits of precision.
/// 
/// Key Applications:
/// - Shor's factoring algorithm (period finding)
/// - Quantum chemistry (ground state energy estimation)
/// - Solving linear systems of equations (HHL algorithm)
/// - Quantum simulation
/// 
/// Algorithm Overview:
/// 1. Prepare counting qubits in superposition (Hadamard gates)
/// 2. Apply controlled-U gates with increasing powers (U^(2^0), U^(2^1), ...)
/// 3. Apply inverse QFT to counting register (uses QFT)
/// 4. Measure to extract phase estimate
/// 
/// Precision: n counting qubits → φ estimated to n bits of accuracy
/// 
/// Example:
/// ```fsharp
/// open FSharp.Azure.Quantum.Algorithms.QPE
/// open FSharp.Azure.Quantum.Backends.LocalBackend
/// 
/// let backend = LocalBackend() :> IQuantumBackend
/// 
/// // Estimate T gate phase: e^(iπ/4) = e^(2πi·1/8) → φ = 1/8
/// match estimateTGatePhase 4 backend with
/// | Ok result -> printfn "Estimated phase: %f (expected ~0.125)" result.EstimatedPhase
/// | Error err -> printfn "Error: %A" err
/// ```
module QPE =
    
    open FSharp.Azure.Quantum.Algorithms.QFT
    
    // ========================================================================
    // TYPES - QPE configuration and results
    // ========================================================================
    
    /// Unitary operator for phase estimation
    /// Represents U such that U|ψ⟩ = e^(2πiφ)|ψ⟩
    type UnitaryOperator =
        /// Phase gate: U = e^(iθ) (simple example)
        /// Eigenvalue: e^(iθ) → phase φ = θ/(2π)
        | PhaseGate of theta: float
        
        /// T gate: U = e^(iπ/4) (π/8 gate)
        /// Eigenvalue: e^(iπ/4) → phase φ = 1/8
        | TGate
        
        /// S gate: U = e^(iπ/2) (phase gate)
        /// Eigenvalue: e^(iπ/2) → phase φ = 1/4
        | SGate
        
        /// General rotation gate: U = Rz(θ) = [[e^(-iθ/2), 0], [0, e^(iθ/2)]]
        /// Eigenvalue (for |1⟩): e^(iθ/2) → phase φ = θ/(4π)
        | RotationZ of theta: float
    
    /// Configuration for Quantum Phase Estimation
    type QPEConfig = {
        /// Number of counting qubits (precision = n bits)
        /// More counting qubits → higher precision
        /// Typical: 2 * log₂(desired accuracy) + 3
        CountingQubits: int
        
        /// Number of target qubits (for eigenvector |ψ⟩)
        /// For single-qubit gates (T, S, PhaseGate): use 1
        TargetQubits: int
        
        /// Unitary operator U to estimate phase of
        UnitaryOperator: UnitaryOperator
        
        /// Initial eigenvector |ψ⟩ (must be eigenstate of U)
        /// If None, assumes |1⟩ for phase gates (standard eigenvector)
        EigenVector: QuantumState option
    }
    
    /// Result of QPE execution
    type QPEResult = {
        /// Estimated phase φ (in range [0, 1))
        /// For U|ψ⟩ = e^(2πiφ)|ψ⟩, this is φ
        EstimatedPhase: float
        
        /// Measurement outcome (binary representation of φ)
        /// EstimatedPhase = MeasurementOutcome / 2^CountingQubits
        MeasurementOutcome: int
        
        /// Number of counting qubits used (precision)
        Precision: int
        
        /// Final quantum state after QPE
        FinalState: QuantumState
        
        /// Number of gates applied
        GateCount: int
        
        /// Configuration used
        Config: QPEConfig
    }
    
    // ========================================================================
    // INTENT  PLAN  EXECUTION (ADR: intent-first algorithms)
    // ========================================================================

    [<RequireQualifiedAccess>]
    type QpePlan =
        | ExecuteViaIntent of QuantumOperation
        | ExecuteViaOps of QuantumOperation list

    // ========================================================================
    // CONTROLLED UNITARY OPERATIONS
    // ========================================================================
    
    /// Convert algorithm-level unitary to intent unitary.
    let private toIntentUnitary (u: UnitaryOperator) : QpeUnitary =
        match u with
        | UnitaryOperator.PhaseGate theta -> QpeUnitary.PhaseGate theta
        | UnitaryOperator.TGate -> QpeUnitary.TGate
        | UnitaryOperator.SGate -> QpeUnitary.SGate
        | UnitaryOperator.RotationZ theta -> QpeUnitary.RotationZ theta

    /// QPE does not require bit-reversal SWAPs; we can post-process classically.
    let private defaultApplyBitReversalSwaps = false

    /// Build a QPE intent operation.
    let private qpeIntentOp (applyBitReversalSwaps: bool) (config: QPEConfig) : QuantumOperation =
        QuantumOperation.Algorithm (AlgorithmOperation.QPE {
            CountingQubits = config.CountingQubits
            TargetQubits = config.TargetQubits
            Unitary = toIntentUnitary config.UnitaryOperator
            PrepareTargetOne =
                match config.EigenVector with
                | Some _ -> false
                | None ->
                    match config.UnitaryOperator with
                    | TGate | SGate | PhaseGate _ | RotationZ _ when config.TargetQubits = 1 -> true
                    | _ -> false
            ApplySwaps = applyBitReversalSwaps
        })

    let private estimateGateCount (applyBitReversalSwaps: bool) (config: QPEConfig) : int =
        // Mirrors the lowered strategy (H prep + eigen prep + controlled unitaries + inverse QFT + swaps).
        //
        // Note: we model each controlled-U^(2^j) as a single controlled phase/rotation gate
        // with a scaled angle (not as 2^j repeated applications), so it counts as 1 per j.
        let eigenPrep = if config.EigenVector.IsSome then 0 else (if config.TargetQubits = 1 then 1 else 0)
        let controlled = config.CountingQubits
        let inverseQft =
            // Same as QFT: n Hadamards + n(n-1)/2 controlled phases.
            config.CountingQubits + (config.CountingQubits * (config.CountingQubits - 1) / 2)
        let swaps = if applyBitReversalSwaps then config.CountingQubits / 2 else 0
        config.CountingQubits + eigenPrep + controlled + inverseQft + swaps

    let private buildLoweringOps (applyBitReversalSwaps: bool) (config: QPEConfig) : QuantumOperation list =
        // Step 2: Apply Hadamard to counting qubits: |+⟩^⊗n
        let hadamardOps =
            [0 .. config.CountingQubits - 1]
            |> List.map (fun i -> QuantumOperation.Gate (CircuitBuilder.H i))

        // Step 3: Prepare target qubits in eigenvector
        // For phase gates, eigenvector is |1⟩
        let eigenPrepOps =
            match config.EigenVector with
            | Some _ -> []
            | None ->
                match config.UnitaryOperator with
                | TGate | SGate | PhaseGate _ | RotationZ _ when config.TargetQubits = 1 ->
                    let targetQubit = config.CountingQubits
                    [ QuantumOperation.Gate (CircuitBuilder.X targetQubit) ]
                | _ -> []

        // Step 4: Apply controlled-U^(2^j) for each counting qubit j
        let controlledOps =
            [0 .. config.CountingQubits - 1]
            |> List.map (fun j ->
                let applications = 1 <<< j

                match config.UnitaryOperator with
                | PhaseGate theta ->
                    let totalTheta = float applications * theta
                    QuantumOperation.Gate (CircuitBuilder.CP (j, config.CountingQubits, totalTheta))
                | TGate ->
                    let totalTheta = float applications * Math.PI / 4.0
                    QuantumOperation.Gate (CircuitBuilder.CP (j, config.CountingQubits, totalTheta))
                | SGate ->
                    let totalTheta = float applications * Math.PI / 2.0
                    QuantumOperation.Gate (CircuitBuilder.CP (j, config.CountingQubits, totalTheta))
                | RotationZ theta ->
                    let totalTheta = float applications * theta
                    QuantumOperation.Gate (CircuitBuilder.CRZ (j, config.CountingQubits, totalTheta)))

        // Step 5: Apply inverse QFT to counting register manually
        // CRITICAL: Inverse QFT processes qubits in REVERSE order (n-1 down to 0)
        // For each qubit: controlled phases FIRST, then Hadamard LAST
        let inverseQftOps =
            [(config.CountingQubits - 1) .. -1 .. 0]
            |> List.collect (fun targetQubit ->
                let controlledPhaseOps =
                    [targetQubit + 1 .. config.CountingQubits - 1]
                    |> List.map (fun k ->
                        let power = k - targetQubit + 1
                        let angle = -2.0 * Math.PI / float (1 <<< power)
                        QuantumOperation.Gate (CircuitBuilder.CP (k, targetQubit, angle)))

                let hadamardOp = QuantumOperation.Gate (CircuitBuilder.H targetQubit)
                controlledPhaseOps @ [ hadamardOp ])

        // Apply bit-reversal swaps to counting qubits (optional)
        let swapOps =
            if applyBitReversalSwaps then
                [0 .. config.CountingQubits / 2 - 1]
                |> List.map (fun i ->
                    let j = config.CountingQubits - 1 - i
                    QuantumOperation.Gate (CircuitBuilder.SWAP (i, j)))
            else
                []

        hadamardOps @ eigenPrepOps @ controlledOps @ inverseQftOps @ swapOps

    let private plan (backend: IQuantumBackend) (applyBitReversalSwaps: bool) (config: QPEConfig) : Result<QpePlan, QuantumError> =
        // QPE requires gate-based operations (or explicit native intent support).
        // Some backends (e.g., annealing) cannot support this, and we should fail explicitly.
        match backend.NativeStateType with
        | QuantumStateType.Annealing ->
            Error (QuantumError.OperationError ("QPE", $"Backend '{backend.Name}' does not support QPE (native state type: {backend.NativeStateType})"))
        | _ ->
            let op = qpeIntentOp applyBitReversalSwaps config

            if backend.SupportsOperation op then
                Ok (QpePlan.ExecuteViaIntent op)
            else
                let lowerOps = buildLoweringOps applyBitReversalSwaps config
                if lowerOps |> List.forall backend.SupportsOperation then
                    Ok (QpePlan.ExecuteViaOps lowerOps)
                else
                    Error (QuantumError.OperationError ("QPE", $"Backend '{backend.Name}' does not support required operations for QPE"))

    let private executePlan
        (backend: IQuantumBackend)
        (state: QuantumState)
        (plan: QpePlan)
        : Result<QuantumState, QuantumError> =

        match plan with
        | QpePlan.ExecuteViaIntent op ->
            backend.ApplyOperation op state
        | QpePlan.ExecuteViaOps ops ->
            UnifiedBackend.applySequence backend ops state

    let private executePlanned
        (backend: IQuantumBackend)
        (applyBitReversalSwaps: bool)
        (config: QPEConfig)
        (initialState: QuantumState)
        : Result<QuantumState * int, QuantumError> =

        result {
            let! qpePlan = plan backend applyBitReversalSwaps config
            let! preparedState = executePlan backend initialState qpePlan
            return (preparedState, estimateGateCount applyBitReversalSwaps config)
        }



    
    // ========================================================================
    // QUANTUM PHASE ESTIMATION ALGORITHM
    // ========================================================================
    
    /// Execute Quantum Phase Estimation
    /// 
    /// Algorithm Steps:
    /// 1. Initialize counting qubits to |+⟩^⊗n (Hadamard on all counting qubits)
    /// 2. Initialize target qubits to eigenvector |ψ⟩
    /// 3. For each counting qubit j (from 0 to n-1):
    ///    - Apply controlled-U^(2^j) with control=counting[j], target=|ψ⟩
    /// 4. Apply inverse QFT to counting register (uses QFT)
    /// 5. Measure counting register to extract phase
    /// 
    /// Phase Encoding:
    /// After controlled unitaries, counting register contains:
    /// |φ⟩ = (1/√N) Σₖ e^(2πiφk) |k⟩
    /// where φ is the phase we want to estimate.
    /// 
    /// The inverse QFT converts this to:
    /// |φ_binary⟩ where measurement gives binary representation of φ
    /// 
    /// Example:
    /// ```fsharp
    /// let config = {
    ///     CountingQubits = 4
    ///     TargetQubits = 1
    ///     UnitaryOperator = TGate
    ///     EigenVector = None
    /// }
    /// let backend = LocalBackend() :> IQuantumBackend
    /// match execute config backend with
    /// | Ok result -> printfn "Phase: %f" result.EstimatedPhase  // ~0.125 (1/8)
    /// | Error err -> printfn "Error: %A" err
    /// ```
    let executeWith (config: QPEConfig) (backend: IQuantumBackend) (applyBitReversalSwaps: bool) : Result<QPEResult, QuantumError> =
        result {
            // Validation
            if config.CountingQubits <= 0 then
                return! Error (QuantumError.ValidationError ("CountingQubits", "must be positive"))
            elif config.TargetQubits <= 0 then
                return! Error (QuantumError.ValidationError ("TargetQubits", "must be positive"))
            elif config.CountingQubits > 16 then
                return! Error (QuantumError.ValidationError ("CountingQubits", "more than 16 is not practical for local simulation"))
            elif config.EigenVector.IsSome then
                return! Error (QuantumError.ValidationError ("EigenVector", "custom eigenvector not yet supported"))
            else
                let totalQubits = config.CountingQubits + config.TargetQubits

                // Step 1: Initialize state |0⟩^(⊗(n+m))
                let! initialState = backend.InitializeState totalQubits

                // Step 2: Build intent, plan execution strategy, execute plan.
                let! (preparedState, gateCount) =
                    executePlanned backend applyBitReversalSwaps config initialState

                // Step 3: Measure final state (all qubits)
                let measurements = UnifiedBackend.measureState preparedState 1000

                // Extract phase from measurement outcome of counting qubits.
                //
                // When we omit the final bit-reversal SWAPs, the measured register is in bit-reversed
                // order; compensate classically before converting to an integer.
                let measuredCountingBits =
                    measurements.[0]
                    |> Array.take config.CountingQubits

                let canonicalCountingBits =
                    if applyBitReversalSwaps then
                        measuredCountingBits
                    else
                        Array.rev measuredCountingBits

                let measurementOutcome =
                    canonicalCountingBits
                    |> Array.indexed
                    |> Array.fold (fun acc (i, bit) -> acc + (bit <<< i)) 0

                let estimatedPhase = float measurementOutcome / float (1 <<< config.CountingQubits)

                return {
                    EstimatedPhase = estimatedPhase
                    MeasurementOutcome = measurementOutcome
                    Precision = config.CountingQubits
                    FinalState = preparedState
                    GateCount = gateCount
                    Config = config
                }
        }

    let execute (config: QPEConfig) (backend: IQuantumBackend) : Result<QPEResult, QuantumError> =
        executeWith config backend defaultApplyBitReversalSwaps
    
    // ========================================================================
    // CONVENIENCE FUNCTIONS
    // ========================================================================
    
    /// Estimate phase of T gate
    /// 
    /// T gate: U = e^(iπ/4) = e^(2πi·1/8)
    /// Expected phase: φ = 1/8 = 0.125
    /// 
    /// The T gate is the π/8 gate and is a fundamental gate in quantum computing.
    /// It's used extensively in fault-tolerant quantum computation.
    /// 
    /// Example:
    /// ```fsharp
    /// let backend = LocalBackend() :> IQuantumBackend
    /// match estimateTGatePhase 4 backend with
    /// | Ok result ->
    ///     printfn "Estimated phase: %f" result.EstimatedPhase  // ~0.125
    ///     printfn "Binary: %B" result.MeasurementOutcome        // ~2 (0010 in 4 bits)
    /// | Error err -> printfn "Error: %A" err
    /// ```
    let estimateTGatePhaseWith (countingQubits: int) (backend: IQuantumBackend) (applyBitReversalSwaps: bool) : Result<QPEResult, QuantumError> =
        let config = {
            CountingQubits = countingQubits
            TargetQubits = 1
            UnitaryOperator = TGate
            EigenVector = None
        }
        executeWith config backend applyBitReversalSwaps

    let estimateTGatePhase (countingQubits: int) (backend: IQuantumBackend) : Result<QPEResult, QuantumError> =
        estimateTGatePhaseWith countingQubits backend defaultApplyBitReversalSwaps
    
    /// Estimate phase of S gate
    /// 
    /// S gate: U = e^(iπ/2) = e^(2πi·1/4)
    /// Expected phase: φ = 1/4 = 0.25
    /// 
    /// The S gate (phase gate) is also called the √Z gate because S² = Z.
    /// It's used in many quantum algorithms including Grover's search.
    /// 
    /// Example:
    /// ```fsharp
    /// let backend = LocalBackend() :> IQuantumBackend
    /// match estimateSGatePhase 4 backend with
    /// | Ok result ->
    ///     printfn "Estimated phase: %f" result.EstimatedPhase  // ~0.25
    ///     printfn "Binary: %B" result.MeasurementOutcome        // ~4 (0100 in 4 bits)
    /// | Error err -> printfn "Error: %A" err
    /// ```
    let estimateSGatePhaseWith (countingQubits: int) (backend: IQuantumBackend) (applyBitReversalSwaps: bool) : Result<QPEResult, QuantumError> =
        let config = {
            CountingQubits = countingQubits
            TargetQubits = 1
            UnitaryOperator = SGate
            EigenVector = None
        }
        executeWith config backend applyBitReversalSwaps

    let estimateSGatePhase (countingQubits: int) (backend: IQuantumBackend) : Result<QPEResult, QuantumError> =
        estimateSGatePhaseWith countingQubits backend defaultApplyBitReversalSwaps
    
    /// Estimate phase of general phase gate U = e^(iθ)
    /// 
    /// Phase gate: U = e^(iθ) = e^(2πiφ) → φ = θ/(2π)
    /// 
    /// This is a generalization of the T and S gates to arbitrary angles.
    /// 
    /// Example:
    /// ```fsharp
    /// let backend = LocalBackend() :> IQuantumBackend
    /// // For θ = π, we get φ = 1/2
    /// match estimatePhaseGate Math.PI 4 backend with
    /// | Ok result ->
    ///     printfn "Estimated phase: %f" result.EstimatedPhase  // ~0.5
    ///     printfn "Binary: %B" result.MeasurementOutcome        // ~8 (1000 in 4 bits)
    /// | Error err -> printfn "Error: %A" err
    /// ```
    let estimatePhaseGateWith (theta: float) (countingQubits: int) (backend: IQuantumBackend) (applyBitReversalSwaps: bool) : Result<QPEResult, QuantumError> =
        let config = {
            CountingQubits = countingQubits
            TargetQubits = 1
            UnitaryOperator = PhaseGate theta
            EigenVector = None
        }
        executeWith config backend applyBitReversalSwaps

    let estimatePhaseGate (theta: float) (countingQubits: int) (backend: IQuantumBackend) : Result<QPEResult, QuantumError> =
        estimatePhaseGateWith theta countingQubits backend defaultApplyBitReversalSwaps

