namespace FSharp.Azure.Quantum.Algorithms

open System
open System.Numerics

/// Quantum Phase Estimation (QPE) Module
/// 
/// Estimates the phase (eigenvalue) of a unitary operator U with respect to an eigenvector |ψ⟩.
/// Given U|ψ⟩ = e^(2πiφ)|ψ⟩, QPE estimates φ to n bits of precision.
/// 
/// Key Applications:
/// - Shor's factoring algorithm (period finding)
/// - Quantum chemistry (ground state energy estimation)
/// - Solving linear systems of equations
/// - Quantum simulation
/// 
/// Algorithm Overview:
/// 1. Prepare counting qubits in superposition (Hadamard gates)
/// 2. Apply controlled-U gates with increasing powers
/// 3. Apply inverse QFT to counting register
/// 4. Measure to extract phase estimate
/// 
/// Precision: n counting qubits → φ estimated to n bits of accuracy
module QuantumPhaseEstimation =
    
    open FSharp.Azure.Quantum.LocalSimulator
    open FSharp.Azure.Quantum.Algorithms.QuantumFourierTransform
    
    // ========================================================================
    // TYPES - QPE configuration and results
    // ========================================================================
    
    /// Unitary operator for phase estimation
    /// Represents U such that U|ψ⟩ = e^(2πiφ)|ψ⟩
    type UnitaryOperator =
        /// Apply U to the state
        | CustomUnitary of apply: (StateVector.StateVector -> StateVector.StateVector)
        
        /// Phase gate: U = e^(iθ) (simple example)
        | PhaseGate of theta: float
        
        /// T gate: U = e^(iπ/4) (π/8 gate)
        | TGate
        
        /// S gate: U = e^(iπ/2) (phase gate)
        | SGate
        
        /// General rotation gate: U = Rz(θ)
        | RotationZ of theta: float
    
    /// Configuration for Quantum Phase Estimation
    type QPEConfig = {
        /// Number of counting qubits (precision = n bits)
        CountingQubits: int
        
        /// Number of target qubits (for eigenvector |ψ⟩)
        TargetQubits: int
        
        /// Unitary operator U to estimate phase of
        UnitaryOperator: UnitaryOperator
        
        /// Initial eigenvector |ψ⟩ (must be eigenstate of U)
        /// If None, assumes |ψ⟩ = |0⟩ (works for phase/rotation gates)
        EigenVector: StateVector.StateVector option
    }
    
    /// Result of QPE execution
    type QPEResult = {
        /// Estimated phase φ (in range [0, 1))
        EstimatedPhase: float
        
        /// Measurement outcome (binary representation of φ)
        MeasurementOutcome: int
        
        /// Number of counting qubits used (precision)
        Precision: int
        
        /// Final quantum state after QPE
        FinalState: StateVector.StateVector
        
        /// Number of gates applied
        GateCount: int
        
        /// Configuration used
        Config: QPEConfig
    }
    
    // ========================================================================
    // CONTROLLED UNITARY OPERATIONS
    // ========================================================================
    
    /// Apply controlled-U^(2^k) gate
    /// control qubit at position controlIdx controls U^(2^k) on target qubits
    let private applyControlledUnitaryPower
        (controlIdx: int)
        (targetStartIdx: int)
        (targetQubits: int)
        (unitary: UnitaryOperator)
        (power: int)
        (state: StateVector.StateVector) : StateVector.StateVector =
        
        let numQubits = StateVector.numQubits state
        if controlIdx < 0 || controlIdx >= numQubits then
            failwith $"Control qubit index {controlIdx} out of range"
        
        let dimension = StateVector.dimension state
        let controlMask = 1 <<< controlIdx
        
        // Create new amplitude array - idiomatic F# functional style
        let newAmplitudes = 
            Array.init dimension (fun i ->
                let controlIs1 = (i &&& controlMask) <> 0
                
                if not controlIs1 then
                    // Control is |0⟩: no change
                    StateVector.getAmplitude i state
                else
                    // Control is |1⟩: apply U^(2^k) to target qubit(s)
                    //
                    // For phase gates (T, S, PhaseGate), the gate only affects |1⟩ states:
                    // - T|0⟩ = |0⟩, T|1⟩ = e^(iπ/4)|1⟩
                    // - S|0⟩ = |0⟩, S|1⟩ = e^(iπ/2)|1⟩
                    //
                    // In a controlled-U operation:
                    // - Control |0⟩: no change
                    // - Control |1⟩, target |0⟩: no change  
                    // - Control |1⟩, target |1⟩: apply phase
                    
                    let targetMask = (1 <<< targetQubits) - 1
                    let targetState = (i >>> targetStartIdx) &&& targetMask
                    let totalPower = 1 <<< power  // 2^k
                    
                    let phase = 
                        match unitary with
                        | PhaseGate theta ->
                            // U = e^(iθ) gate - only affects |1⟩ state
                            if targetState = 1 then
                                let totalPhase = float totalPower * theta
                                Complex(cos totalPhase, sin totalPhase)
                            else
                                Complex.One
                        
                        | TGate ->
                            // T = e^(iπ/4) gate - only affects |1⟩ state
                            if targetState = 1 then
                                let totalPhase = float totalPower * Math.PI / 4.0
                                Complex(cos totalPhase, sin totalPhase)
                            else
                                Complex.One
                        
                        | SGate ->
                            // S = e^(iπ/2) gate - only affects |1⟩ state
                            if targetState = 1 then
                                let totalPhase = float totalPower * Math.PI / 2.0
                                Complex(cos totalPhase, sin totalPhase)
                            else
                                Complex.One
                        
                        | RotationZ theta ->
                            // Rz(θ) = [[e^(-iθ/2), 0], [0, e^(iθ/2)]]
                            let totalTheta = float totalPower * theta
                            let targetIs1 = targetState = 1
                            let phaseAngle = if targetIs1 then totalTheta / 2.0 else -totalTheta / 2.0
                            Complex(cos phaseAngle, sin phaseAngle)
                        
                        | CustomUnitary _ ->
                            failwith "Custom unitary operators not yet supported in controlled-U^k"
                    
                    (StateVector.getAmplitude i state) * phase
            )
        
        StateVector.create newAmplitudes
    
    // ========================================================================
    // QUANTUM PHASE ESTIMATION ALGORITHM
    // ========================================================================
    
    /// Execute Quantum Phase Estimation
    /// 
    /// Algorithm:
    /// 1. Initialize counting qubits to |+⟩ (Hadamard on all counting qubits)
    /// 2. Initialize target qubits to eigenvector |ψ⟩
    /// 3. For each counting qubit j (from 0 to n-1):
    ///    - Apply controlled-U^(2^j) with control=counting[j], target=|ψ⟩
    /// 4. Apply inverse QFT to counting register
    /// 5. Measure counting register to extract phase
    let execute (config: QPEConfig) : Result<QPEResult, string> =
        try
            if config.CountingQubits <= 0 then
                Error "Number of counting qubits must be positive"
            elif config.TargetQubits <= 0 then
                Error "Number of target qubits must be positive"
            elif config.CountingQubits > 16 then
                Error "More than 16 counting qubits is not practical for local simulation"
            else
                let totalQubits = config.CountingQubits + config.TargetQubits
                
                // Step 1: Initialize state
                // Counting qubits (first n qubits) in |+⟩ state
                // Target qubits (last m qubits) in eigenvector |ψ⟩
                let initialState = 
                    match config.EigenVector with
                    | Some eigenVec ->
                        // Use provided eigenvector for target qubits
                        if StateVector.numQubits eigenVec <> config.TargetQubits then
                            failwith "Eigenvector must have TargetQubits qubits"
                        StateVector.init totalQubits
                    | None ->
                        StateVector.init totalQubits
                
                // Apply Hadamard to counting qubits (creates superposition)
                let stateWithSuperposition = 
                    [0 .. config.CountingQubits - 1]
                    |> List.fold (fun state i -> Gates.applyH i state) initialState
                
                // Prepare target qubits in eigenvector state
                // For phase gates (T, S, PhaseGate), the eigenvector is |1⟩
                let stateWithEigenvector, eigenPrepGateCount =
                    match config.EigenVector with
                    | Some _ -> 
                        stateWithSuperposition, 0
                    | None ->
                        match config.UnitaryOperator with
                        | TGate | SGate | PhaseGate _ when config.TargetQubits = 1 ->
                            // Flip target qubit from |0⟩ to |1⟩
                            Gates.applyX config.CountingQubits stateWithSuperposition, 1
                        | _ -> 
                            stateWithSuperposition, 0
                
                // Step 2: Apply controlled-U^(2^j) gates
                let stateAfterControlledU, controlledUGateCount =
                    [0 .. config.CountingQubits - 1]
                    |> List.fold (fun (state, gateCount) j ->
                        let newState = applyControlledUnitaryPower
                                        j                              // control qubit index
                                        config.CountingQubits          // target qubits start after counting qubits
                                        config.TargetQubits            // number of target qubits
                                        config.UnitaryOperator         // unitary operator
                                        j                              // power: 2^j
                                        state
                        (newState, gateCount + (1 <<< j))
                    ) (stateWithEigenvector, 0)
                
                // Step 3: Apply inverse QFT to counting register only
                // Manual inverse QFT on counting qubits [0..n-1]
                // For inverse QFT, we reverse the forward QFT operations:
                // Forward QFT on qubit j: H(j), then CPhase(k→j) for all k>j
                // Inverse QFT on qubit j: CPhase(k→j)† for all k>j, then H(j)
                // Process qubits in reverse order: n-1 down to 0
                let stateAfterInverseQFT, inverseQFTGateCount =
                    [(config.CountingQubits - 1) .. -1 .. 0]
                    |> List.fold (fun (state, gateCount) j ->
                        // Apply controlled phase rotations FIRST (with negated angles)
                        let stateAfterPhases, phaseGateCount =
                            [j + 1 .. config.CountingQubits - 1]
                            |> List.fold (fun (s, gc) k ->
                                let power = k - j + 1
                                let angle = -2.0 * Math.PI / float (1 <<< power)
                                (Gates.applyCPhase k j angle s, gc + 1)
                            ) (state, 0)
                        
                        // Apply Hadamard LAST
                        let stateAfterH = Gates.applyH j stateAfterPhases
                        (stateAfterH, gateCount + phaseGateCount + 1)
                    ) (stateAfterControlledU, 0)
                
                // Apply bit-reversal SWAPs (required for standard QFT)
                let stateAfterSwaps, swapGateCount =
                    [0 .. (config.CountingQubits / 2 - 1)]
                    |> List.fold (fun (state, gateCount) i ->
                        let j = config.CountingQubits - 1 - i
                        (Gates.applySWAP i j state, gateCount + 1)
                    ) (stateAfterInverseQFT, 0)
                
                // Step 4: Measure counting register
                let rng = System.Random()
                let measurement = Measurement.measureComputationalBasis rng stateAfterSwaps
                
                // Extract phase from measurement outcome (first CountingQubits bits)
                let measurementOutcome = measurement &&& ((1 <<< config.CountingQubits) - 1)
                let estimatedPhase = float measurementOutcome / float (1 <<< config.CountingQubits)
                
                // Calculate total gate count
                let totalGateCount = 
                    config.CountingQubits + eigenPrepGateCount + controlledUGateCount + 
                    inverseQFTGateCount + swapGateCount
                
                Ok {
                    EstimatedPhase = estimatedPhase
                    MeasurementOutcome = measurementOutcome
                    Precision = config.CountingQubits
                    FinalState = stateAfterSwaps
                    GateCount = totalGateCount
                    Config = config
                }
        with
        | ex -> Error $"QPE execution failed: {ex.Message}"
    
    // ========================================================================
    // CONVENIENCE FUNCTIONS
    // ========================================================================
    
    /// Estimate phase of T gate (φ = 1/8, since T = e^(iπ/4) = e^(2πi·1/8))
    let estimateTGatePhase (countingQubits: int) : Result<QPEResult, string> =
        let config = {
            CountingQubits = countingQubits
            TargetQubits = 1
            UnitaryOperator = TGate
            EigenVector = None
        }
        execute config
    
    /// Estimate phase of S gate (φ = 1/4, since S = e^(iπ/2) = e^(2πi·1/4))
    let estimateSGatePhase (countingQubits: int) : Result<QPEResult, string> =
        let config = {
            CountingQubits = countingQubits
            TargetQubits = 1
            UnitaryOperator = SGate
            EigenVector = None
        }
        execute config
    
    /// Estimate phase of general phase gate U = e^(iθ)
    let estimatePhaseGate (theta: float) (countingQubits: int) : Result<QPEResult, string> =
        let config = {
            CountingQubits = countingQubits
            TargetQubits = 1
            UnitaryOperator = PhaseGate theta
            EigenVector = None
        }
        execute config
