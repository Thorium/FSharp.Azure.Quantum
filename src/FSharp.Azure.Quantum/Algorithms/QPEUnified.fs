namespace FSharp.Azure.Quantum.Algorithms

open System
open FSharp.Azure.Quantum
open FSharp.Azure.Quantum.Core
open FSharp.Azure.Quantum.Core.UnifiedBackendAbstraction
open System.Numerics

/// Quantum Phase Estimation (QPE) - Unified Backend Implementation
/// 
/// State-based implementation using IUnifiedQuantumBackend.
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
/// 3. Apply inverse QFT to counting register (uses QFTUnified)
/// 4. Measure to extract phase estimate
/// 
/// Precision: n counting qubits → φ estimated to n bits of accuracy
/// 
/// Example:
/// ```fsharp
/// open FSharp.Azure.Quantum.Algorithms.QPEUnified
/// open FSharp.Azure.Quantum.Backends.LocalBackend
/// 
/// let backend = LocalBackend() :> IUnifiedQuantumBackend
/// 
/// // Estimate T gate phase: e^(iπ/4) = e^(2πi·1/8) → φ = 1/8
/// match estimateTGatePhase 4 backend with
/// | Ok result -> printfn "Estimated phase: %f (expected ~0.125)" result.EstimatedPhase
/// | Error err -> printfn "Error: %A" err
/// ```
module QPEUnified =
    
    open FSharp.Azure.Quantum.Algorithms.QFTUnified
    
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
    // CONTROLLED UNITARY OPERATIONS
    // ========================================================================
    
    /// Apply controlled-U^(2^power) gate via backend
    /// 
    /// For phase gates (T, S, PhaseGate), we apply the gate multiple times:
    /// - U^(2^0) = U (apply once)
    /// - U^(2^1) = U² (apply twice)
    /// - U^(2^k) = U^(2^k) (apply 2^k times)
    /// 
    /// This works because phase gates commute with themselves and compose additively:
    /// e^(iθ₁) · e^(iθ₂) = e^(i(θ₁+θ₂))
    let private applyControlledUnitaryPower
        (controlQubit: int)
        (targetQubit: int)
        (unitary: UnitaryOperator)
        (power: int)
        (backend: IUnifiedQuantumBackend)
        (state: QuantumState) : Result<QuantumState, QuantumError> =
        
        let totalApplications = 1 <<< power  // 2^power
        
        // Get the gate to apply based on unitary type
        result {
            let gateOperation = 
                match unitary with
                | PhaseGate theta ->
                    // Phase gate: U|1⟩ = e^(iθ)|1⟩
                    // After 2^k applications: e^(i·2^k·θ)
                    // CP(θ) applies phase e^(iθ) to |11⟩ state - matches old QPE exactly
                    let totalTheta = float totalApplications * theta
                    QuantumOperation.Gate (CircuitBuilder.CP (controlQubit, targetQubit, totalTheta))
                
                | TGate ->
                    // T gate: T|1⟩ = e^(iπ/4)|1⟩
                    // After 2^k applications: e^(i·2^k·π/4)
                    // CP(θ) applies phase e^(iθ) to |11⟩ state
                    let totalTheta = float totalApplications * Math.PI / 4.0
                    QuantumOperation.Gate (CircuitBuilder.CP (controlQubit, targetQubit, totalTheta))
                
                | SGate ->
                    // S gate: S|1⟩ = e^(iπ/2)|1⟩
                    // After 2^k applications: e^(i·2^k·π/2)
                    // CP(θ) applies phase e^(iθ) to |11⟩ state
                    let totalTheta = float totalApplications * Math.PI / 2.0
                    QuantumOperation.Gate (CircuitBuilder.CP (controlQubit, targetQubit, totalTheta))
                
                | RotationZ theta ->
                    // Rz(θ) = [[e^(-iθ/2), 0], [0, e^(iθ/2)]]
                    // After 2^k applications: Rz(2^k · θ)
                    let totalTheta = float totalApplications * theta
                    QuantumOperation.Gate (CircuitBuilder.CRZ (controlQubit, targetQubit, totalTheta))
            
            // Apply controlled gate operation
            let! newState = backend.ApplyOperation gateOperation state
            return newState
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
    /// 4. Apply inverse QFT to counting register (uses QFTUnified)
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
    /// let backend = LocalBackend() :> IUnifiedQuantumBackend
    /// match execute config backend with
    /// | Ok result -> printfn "Phase: %f" result.EstimatedPhase  // ~0.125 (1/8)
    /// | Error err -> printfn "Error: %A" err
    /// ```
    let execute (config: QPEConfig) (backend: IUnifiedQuantumBackend) : Result<QPEResult, QuantumError> =
        result {
            // Validation
            if config.CountingQubits <= 0 then
                return! Error (QuantumError.ValidationError ("CountingQubits", "must be positive"))
            elif config.TargetQubits <= 0 then
                return! Error (QuantumError.ValidationError ("TargetQubits", "must be positive"))
            elif config.CountingQubits > 16 then
                return! Error (QuantumError.ValidationError ("CountingQubits", "more than 16 is not practical for local simulation"))
            else
                let totalQubits = config.CountingQubits + config.TargetQubits
                
                // Step 1: Initialize state |0⟩^⊗(n+m)
                let! initialState = backend.InitializeState totalQubits
                
                // Step 2: Apply Hadamard to counting qubits → |+⟩^⊗n
                let hadamardOps = 
                    [0 .. config.CountingQubits - 1]
                    |> List.map (fun i -> QuantumOperation.Gate (CircuitBuilder.H i))
                
                let! stateAfterHadamards = UnifiedBackend.applySequence backend hadamardOps initialState
                let mutable gateCount = config.CountingQubits  // H gates applied
                
                // Step 3: Prepare target qubits in eigenvector
                // For phase gates, eigenvector is |1⟩
                let! (stateAfterEigenPrep, eigenPrepGates) = 
                    match config.EigenVector with
                    | Some eigenVec ->
                        // Use provided eigenvector (NOT IMPLEMENTED - requires state preparation)
                        Error (QuantumError.ValidationError ("EigenVector", "custom eigenvector not yet supported"))
                    | None ->
                        // For phase gates (T, S, PhaseGate), eigenvector is |1⟩
                        // Apply X gate to target qubit to prepare |1⟩
                        match config.UnitaryOperator with
                        | TGate | SGate | PhaseGate _ | RotationZ _ when config.TargetQubits = 1 ->
                            let targetQubit = config.CountingQubits  // First target qubit after counting qubits
                            backend.ApplyOperation 
                                (QuantumOperation.Gate (CircuitBuilder.X targetQubit)) 
                                stateAfterHadamards
                            |> Result.map (fun newState -> (newState, 1))  // 1 X gate
                        | _ -> 
                            Ok (stateAfterHadamards, 0)  // No additional gates
                
                gateCount <- gateCount + eigenPrepGates
                
                // Step 4: Apply controlled-U^(2^j) for each counting qubit j
                let! (stateAfterControlled, controlledGates) = 
                    [0 .. config.CountingQubits - 1]
                    |> List.fold (fun stateResult j ->
                        result {
                            let! (currentState, accGates) = stateResult
                            let targetQubit = config.CountingQubits  // First target qubit
                            let! newState = applyControlledUnitaryPower j targetQubit config.UnitaryOperator j backend currentState
                            return (newState, accGates + (1 <<< j))  // Accumulate 2^j gate applications
                        }
                    ) (Ok (stateAfterEigenPrep, 0))
                
                gateCount <- gateCount + controlledGates
                
                // Step 5: Apply inverse QFT to counting register manually
                // CRITICAL: Inverse QFT processes qubits in REVERSE order (n-1 down to 0)
                // For each qubit: controlled phases FIRST, then Hadamard LAST
                let qftOperations = 
                    [(config.CountingQubits - 1) .. -1 .. 0]
                    |> List.collect (fun targetQubit ->
                        // Controlled phase rotations FIRST (with negated angles for inverse)
                        let controlledPhaseOps =
                            [targetQubit + 1 .. config.CountingQubits - 1]
                            |> List.map (fun k ->
                                let power = k - targetQubit + 1
                                let angle = -2.0 * Math.PI / float (1 <<< power)  // Negative for inverse
                                QuantumOperation.Gate (CircuitBuilder.CP (k, targetQubit, angle))
                            )
                        
                        // Hadamard LAST
                        let hadamardOp = QuantumOperation.Gate (CircuitBuilder.H targetQubit)
                        
                        controlledPhaseOps @ [hadamardOp]  // Phases first, then H
                    )
                
                let! stateAfterQFT = UnifiedBackend.applySequence backend qftOperations stateAfterControlled
                let qftGateCount = List.length qftOperations
                
                // Apply bit-reversal swaps to counting qubits
                let swapOperations =
                    [0 .. config.CountingQubits / 2 - 1]
                    |> List.map (fun i ->
                        let j = config.CountingQubits - 1 - i
                        QuantumOperation.Gate (CircuitBuilder.SWAP (i, j))
                    )
                
                let! stateAfterSwaps = UnifiedBackend.applySequence backend swapOperations stateAfterQFT
                let swapGateCount = List.length swapOperations
                
                gateCount <- gateCount + qftGateCount + swapGateCount
                
                // Step 6: Measure final state (all qubits)
                let measurements = UnifiedBackend.measureState stateAfterSwaps 1000
                
                // Extract phase from measurement outcome of counting qubits
                let measurementOutcome = 
                    measurements.[0]  // Take first measurement
                    |> Array.take config.CountingQubits  // Extract counting register bits
                    |> Array.indexed
                    |> Array.fold (fun acc (i, bit) -> acc + (bit <<< i)) 0
                
                let estimatedPhase = float measurementOutcome / float (1 <<< config.CountingQubits)
                
                return {
                    EstimatedPhase = estimatedPhase
                    MeasurementOutcome = measurementOutcome
                    Precision = config.CountingQubits
                    FinalState = stateAfterSwaps
                    GateCount = gateCount
                    Config = config
                }
        }
    
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
    /// let backend = LocalBackend() :> IUnifiedQuantumBackend
    /// match estimateTGatePhase 4 backend with
    /// | Ok result -> 
    ///     printfn "Estimated phase: %f" result.EstimatedPhase  // ~0.125
    ///     printfn "Binary: %B" result.MeasurementOutcome        // ~2 (0010 in 4 bits)
    /// | Error err -> printfn "Error: %A" err
    /// ```
    let estimateTGatePhase (countingQubits: int) (backend: IUnifiedQuantumBackend) : Result<QPEResult, QuantumError> =
        let config = {
            CountingQubits = countingQubits
            TargetQubits = 1
            UnitaryOperator = TGate
            EigenVector = None
        }
        execute config backend
    
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
    /// let backend = LocalBackend() :> IUnifiedQuantumBackend
    /// match estimateSGatePhase 4 backend with
    /// | Ok result -> 
    ///     printfn "Estimated phase: %f" result.EstimatedPhase  // ~0.25
    ///     printfn "Binary: %B" result.MeasurementOutcome        // ~4 (0100 in 4 bits)
    /// | Error err -> printfn "Error: %A" err
    /// ```
    let estimateSGatePhase (countingQubits: int) (backend: IUnifiedQuantumBackend) : Result<QPEResult, QuantumError> =
        let config = {
            CountingQubits = countingQubits
            TargetQubits = 1
            UnitaryOperator = SGate
            EigenVector = None
        }
        execute config backend
    
    /// Estimate phase of general phase gate U = e^(iθ)
    /// 
    /// Phase gate: U = e^(iθ) = e^(2πiφ) → φ = θ/(2π)
    /// 
    /// This is a generalization of the T and S gates to arbitrary angles.
    /// 
    /// Example:
    /// ```fsharp
    /// let backend = LocalBackend() :> IUnifiedQuantumBackend
    /// // For θ = π, we get φ = 1/2
    /// match estimatePhaseGate Math.PI 4 backend with
    /// | Ok result -> 
    ///     printfn "Estimated phase: %f" result.EstimatedPhase  // ~0.5
    ///     printfn "Binary: %B" result.MeasurementOutcome        // ~8 (1000 in 4 bits)
    /// | Error err -> printfn "Error: %A" err
    /// ```
    let estimatePhaseGate (theta: float) (countingQubits: int) (backend: IUnifiedQuantumBackend) : Result<QPEResult, QuantumError> =
        let config = {
            CountingQubits = countingQubits
            TargetQubits = 1
            UnitaryOperator = PhaseGate theta
            EigenVector = None
        }
        execute config backend
