namespace FSharp.Azure.Quantum.Algorithms

open System
open FSharp.Azure.Quantum
open FSharp.Azure.Quantum.CircuitBuilder
open FSharp.Azure.Quantum.Core
open FSharp.Azure.Quantum.Core.BackendAbstraction

/// Bell State Preparation and Measurement
/// 
/// Bell states (EPR pairs) are maximally entangled two-qubit quantum states.
/// They are fundamental building blocks in quantum computing and quantum information:
/// 
/// **Production Use Cases**:
/// - Quantum Error Correction (surface codes, toric codes)
/// - Quantum Key Distribution (BB84, E91 protocols)
/// - Quantum Teleportation (requires pre-shared Bell pair)
/// - Quantum Networking (entanglement swapping, quantum repeaters)
/// - Quantum Algorithms (Grover, Shor, VQE - all use entanglement)
/// 
/// **Real-World Deployments**:
/// - ID Quantique commercial QKD systems
/// - Micius satellite quantum communication
/// - TU Delft quantum internet experiments
/// - IBM Quantum, IonQ, Rigetti platforms (all create Bell states)
/// 
/// **Textbook References**:
/// - "Learn Quantum Computing with Python and Q#" (Kaiser, 2021) - Chapter 6
/// - "Quantum Programming in Depth" (Manning, 2024) - Chapter 2
/// - Nielsen & Chuang "Quantum Computation and Quantum Information" - Chapter 1
/// 
/// **Performance**:
/// - Creation: 2 gates (Hadamard + CNOT) - O(1)
/// - Fidelity on NISQ hardware: 95-99%
/// - Used in production quantum systems
module BellStates =
    
    // ========================================================================
    // TYPES
    // ========================================================================

    /// Four Bell states (EPR pairs) - maximally entangled basis
    /// 
    /// Bell basis forms an orthonormal basis for 2-qubit Hilbert space:
    /// - Any 2-qubit state can be expressed as superposition of Bell states
    /// - Measurement in Bell basis distinguishes all 4 states
    /// - Used for quantum error correction and quantum teleportation
    type BellState =
        /// |Φ⁺⟩ = (|00⟩ + |11⟩) / √2
        /// Most commonly used Bell state
        /// Created by: H(0), CNOT(0,1)
        | PhiPlus
        
        /// |Φ⁻⟩ = (|00⟩ - |11⟩) / √2
        /// Created by: H(0), CNOT(0,1), Z(0)
        | PhiMinus
        
        /// |Ψ⁺⟩ = (|01⟩ + |10⟩) / √2
        /// Created by: H(0), CNOT(0,1), X(1)
        | PsiPlus
        
        /// |Ψ⁻⟩ = (|01⟩ - |10⟩) / √2
        /// Created by: H(0), CNOT(0,1), X(1), Z(0)
        | PsiMinus

    type private BellIntent =
        | Prepare of BellState
        | MeasureBellBasis

    [<RequireQualifiedAccess>]
    type private BellPlan =
        | ExecuteViaOps of ops: QuantumOperation list

    // ========================================================================
    // INTENT → PLAN → EXECUTION (ADR: intent-first algorithms)
    // ========================================================================

    let private preparationOps (bellState: BellState) : QuantumOperation list =
        let baseOps = [ QuantumOperation.Gate (H 0); QuantumOperation.Gate (CNOT (0, 1)) ]
        match bellState with
        | PhiPlus -> baseOps
        | PhiMinus -> baseOps @ [ QuantumOperation.Gate (Z 0) ]
        | PsiPlus -> baseOps @ [ QuantumOperation.Gate (X 1) ]
        | PsiMinus -> baseOps @ [ QuantumOperation.Gate (X 1); QuantumOperation.Gate (Z 0) ]

    let private bellMeasurementOps : QuantumOperation list =
        [ QuantumOperation.Gate (CNOT (0, 1)); QuantumOperation.Gate (H 0) ]

    let private plan (backend: IQuantumBackend) (intent: BellIntent) : Result<BellPlan, QuantumError> =
        // Bell states require standard gate-based operations (H, CNOT, X, Z).
        // Some backends (e.g., annealing) cannot support this, so fail explicitly.
        match backend.NativeStateType with
        | QuantumStateType.Annealing ->
            Error (QuantumError.OperationError ("BellStates", $"Backend '{backend.Name}' does not support Bell state algorithms (native state type: {backend.NativeStateType})"))
        | _ ->
            let ops =
                match intent with
                | Prepare bellState -> preparationOps bellState
                | MeasureBellBasis -> bellMeasurementOps

            if ops |> List.forall backend.SupportsOperation then
                Ok (BellPlan.ExecuteViaOps ops)
            else
                Error (QuantumError.OperationError ("BellStates", $"Backend '{backend.Name}' does not support required operations for Bell states"))

    let private executePlan
        (backend: IQuantumBackend)
        (state: QuantumState)
        (plan: BellPlan)
        : Result<QuantumState, QuantumError> =

        match plan with
        | BellPlan.ExecuteViaOps ops -> UnifiedBackend.applySequence backend ops state
    
    /// Result of Bell state measurement
    type BellMeasurement = {
        /// Measured Bell state
        State: BellState
        
        /// Measurement outcomes (2 bits)
        Bits: int * int
        
        /// Probability of this outcome
        Probability: float
    }
    
    /// Bell state preparation result
    type BellStateResult = {
        /// Created Bell state
        State: BellState
        
        /// Quantum state after preparation
        QuantumState: QuantumState
        
        /// Number of qubits used (always 2)
        NumQubits: int
        
        /// Backend used
        BackendName: string
    }
    
    // ========================================================================
    // BELL STATE CREATION
    // ========================================================================
    
    /// Create Bell state |Φ⁺⟩ = (|00⟩ + |11⟩) / √2
    /// 
    /// Circuit:
    ///   q0: ─H─●─
    ///          │
    ///   q1: ───X─
    /// 
    /// This is the most common Bell state, used in:
    /// - Quantum teleportation
    /// - Superdense coding
    /// - Quantum error correction
    /// 
    /// Parameters:
    ///   backend - Quantum backend to execute on
    /// 
    /// Returns:
    ///   BellStateResult with quantum state
    let createPhiPlus (backend: IQuantumBackend) : Result<BellStateResult, QuantumError> =
        result {
            let! initialState = backend.InitializeState 2

            let intent = Prepare PhiPlus
            let! preparedPlan = plan backend intent
            let! finalState = executePlan backend initialState preparedPlan

            return {
                State = PhiPlus
                QuantumState = finalState
                NumQubits = 2
                BackendName = backend.Name
            }
        }
    
    /// Create Bell state |Φ⁻⟩ = (|00⟩ - |11⟩) / √2
    /// 
    /// Circuit:
    ///   q0: ─H─●─Z─
    ///          │
    ///   q1: ───X───
    /// 
    /// Parameters:
    ///   backend - Quantum backend to execute on
    /// 
    /// Returns:
    ///   BellStateResult with quantum state
    let createPhiMinus (backend: IQuantumBackend) : Result<BellStateResult, QuantumError> =
        result {
            let! initialState = backend.InitializeState 2

            let intent = Prepare PhiMinus
            let! preparedPlan = plan backend intent
            let! finalState = executePlan backend initialState preparedPlan

            return {
                State = PhiMinus
                QuantumState = finalState
                NumQubits = 2
                BackendName = backend.Name
            }
        }
    
    /// Create Bell state |Ψ⁺⟩ = (|01⟩ + |10⟩) / √2
    /// 
    /// Circuit:
    ///   q0: ─H─●───
    ///          │
    ///   q1: ───X─X─
    /// 
    /// Parameters:
    ///   backend - Quantum backend to execute on
    /// 
    /// Returns:
    ///   BellStateResult with quantum state
    let createPsiPlus (backend: IQuantumBackend) : Result<BellStateResult, QuantumError> =
        result {
            let! initialState = backend.InitializeState 2

            let intent = Prepare PsiPlus
            let! preparedPlan = plan backend intent
            let! finalState = executePlan backend initialState preparedPlan

            return {
                State = PsiPlus
                QuantumState = finalState
                NumQubits = 2
                BackendName = backend.Name
            }
        }
    
    /// Create Bell state |Ψ⁻⟩ = (|01⟩ - |10⟩) / √2
    /// 
    /// Circuit:
    ///   q0: ─H─●─Z─
    ///          │
    ///   q1: ───X─X─
    /// 
    /// Parameters:
    ///   backend - Quantum backend to execute on
    /// 
    /// Returns:
    ///   BellStateResult with quantum state
    let createPsiMinus (backend: IQuantumBackend) : Result<BellStateResult, QuantumError> =
        result {
            let! initialState = backend.InitializeState 2

            let intent = Prepare PsiMinus
            let! preparedPlan = plan backend intent
            let! finalState = executePlan backend initialState preparedPlan

            return {
                State = PsiMinus
                QuantumState = finalState
                NumQubits = 2
                BackendName = backend.Name
            }
        }
    
    /// Create specified Bell state
    /// 
    /// Convenience function to create any Bell state by type.
    /// 
    /// Parameters:
    ///   bellState - Which Bell state to create
    ///   backend - Quantum backend to execute on
    /// 
    /// Returns:
    ///   BellStateResult with quantum state
    let create (bellState: BellState) (backend: IQuantumBackend) : Result<BellStateResult, QuantumError> =
        match bellState with
        | PhiPlus -> createPhiPlus backend
        | PhiMinus -> createPhiMinus backend
        | PsiPlus -> createPsiPlus backend
        | PsiMinus -> createPsiMinus backend
    
    // ========================================================================
    // BELL STATE MEASUREMENT
    // ========================================================================
    
    /// Measure in Bell basis
    /// 
    /// Performs Bell measurement by:
    /// 1. Apply CNOT(0,1) - unentangle
    /// 2. Apply H(0) - rotate to computational basis
    /// 3. Measure both qubits
    /// 
    /// Measurement outcomes map to Bell states:
    /// - 00 → |Φ⁺⟩
    /// - 01 → |Ψ⁺⟩
    /// - 10 → |Φ⁻⟩
    /// - 11 → |Ψ⁻⟩
    /// 
    /// This is critical for quantum teleportation!
    /// 
    /// Parameters:
    ///   state - Quantum state to measure
    ///   backend - Quantum backend to execute on
    /// 
    /// Returns:
    ///   Measured Bell state
    let measureBellBasis (state: QuantumState) (backend: IQuantumBackend)
        : Result<BellMeasurement, QuantumError> =

        result {
            // Reverse Bell preparation circuit to map Bell basis -> computational basis.
            let! measurementPlan = plan backend MeasureBellBasis
            let! rotatedState = executePlan backend state measurementPlan

            // Sample a single measurement outcome from the rotated state.
            let bits = UnifiedBackend.measureState rotatedState 1 |> Array.head
            let bit0, bit1 = bits.[0], bits.[1]

            let bellState =
                match (bit0, bit1) with
                | (0, 0) -> PhiPlus
                | (0, 1) -> PsiPlus
                | (1, 0) -> PhiMinus
                | (1, 1) -> PsiMinus
                | _ -> PhiPlus

            let probability = QuantumState.probability bits rotatedState

            return {
                State = bellState
                Bits = (bit0, bit1)
                Probability = probability
            }
        }
    
    // ========================================================================
    // ENTANGLEMENT VERIFICATION
    // ========================================================================
    
    /// Verify Bell state by measuring correlation
    /// 
    /// Measures both qubits multiple times and checks correlation:
    /// - |Φ⁺⟩ and |Φ⁻⟩: Always measure same bit values (00 or 11)
    /// - |Ψ⁺⟩ and |Ψ⁻⟩: Always measure opposite bit values (01 or 10)
    /// 
    /// This verifies entanglement!
    /// 
    /// Parameters:
    ///   bellState - Bell state to verify
    ///   backend - Quantum backend to execute on
    ///   shots - Number of measurements
    /// 
    /// Returns:
    ///   Correlation coefficient (-1 to 1, where ±1 indicates perfect entanglement)
    let verifyEntanglement (bellState: BellStateResult) (_backend: IQuantumBackend) (shots: int)
        : Result<float, QuantumError> =
        if shots < 1 then
            Error (QuantumError.ValidationError ("shots", "At least 1 shot required"))
        else
            // We can sample from the state distribution without mutating it.
            let measurements = UnifiedBackend.measureState bellState.QuantumState shots

            let correlationValues =
                measurements
                |> Array.map (fun bits ->
                    // Same bits => +1, different => -1
                    if bits.[0] = bits.[1] then 1.0 else -1.0)

            Ok (Array.average correlationValues)
    
    // ========================================================================
    // PRETTY PRINTING
    // ========================================================================
    
    /// Get mathematical notation for Bell state
    let toNotation (bellState: BellState) : string =
        match bellState with
        | PhiPlus -> "|Φ⁺⟩ = (|00⟩ + |11⟩) / √2"
        | PhiMinus -> "|Φ⁻⟩ = (|00⟩ - |11⟩) / √2"
        | PsiPlus -> "|Ψ⁺⟩ = (|01⟩ + |10⟩) / √2"
        | PsiMinus -> "|Ψ⁻⟩ = (|01⟩ - |10⟩) / √2"
    
    /// Format Bell state result for display
    let formatResult (result: BellStateResult) : string =
        sprintf "Bell State Created:\n  State: %s\n  Notation: %s\n  Backend: %s"
            (match result.State with
             | PhiPlus -> "Φ⁺"
             | PhiMinus -> "Φ⁻"
             | PsiPlus -> "Ψ⁺"
             | PsiMinus -> "Ψ⁻")
            (toNotation result.State)
            result.BackendName
