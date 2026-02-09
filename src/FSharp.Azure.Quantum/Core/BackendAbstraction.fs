namespace FSharp.Azure.Quantum.Core

open System.Threading
open FSharp.Azure.Quantum
open FSharp.Azure.Quantum.LocalSimulator
open FSharp.Azure.Quantum.Core.CircuitAbstraction

/// Unified quantum backend interface supporting both gate-based and state-based execution
/// 
/// Design rationale:
/// - Extends IQuantumBackend with state access (backward compatible)
/// - Enables algorithms to work with intermediate quantum states
/// - Supports pure topological execution (no gate compilation)
/// - Type-safe operation dispatch based on backend capabilities
/// 
/// Architecture:
/// - Level 1: IQuantumBackend (measurements only) - existing 85% of users
/// - Level 2: IQuantumBackend (state access) - new advanced users
/// 
/// Migration path:
/// - Existing backends: Optionally implement new methods
/// - Existing algorithms: Continue using IQuantumBackend
/// - New algorithms: Can use IQuantumBackend for more control
module BackendAbstraction =
    
    /// Algorithm-level intent for backends that are not gate-native.
    ///
    /// This is a DU-first way to express *meaning* without forcing a gate circuit
    /// as the canonical representation.
    type QftIntent = {
        /// Number of logical qubits the QFT applies to.
        NumQubits: int

        /// Whether to compute inverse QFT (QFT†).
        Inverse: bool

        /// Whether to apply bit-reversal swaps.
        ///
        /// `true` corresponds to the standard mathematical QFT.
        /// `false` returns the bit-reversed output ordering (useful for some algorithms).
        ApplySwaps: bool
    }

    type GroverIntent = {
        /// Number of logical qubits in the search space.
        NumQubits: int

        /// A function that decides whether a basis index is marked.
        ///
        /// This keeps the intent backend-agnostic while allowing backends to apply
        /// the oracle phase flip using their native state model.
        IsMarked: int -> bool
    }

    /// Minimal unitary family supported by QPE intent.
    ///
    /// This intentionally covers the common "single-qubit phase" cases used throughout
    /// the codebase (T, S, and phase/rotation gates). More general unitaries can be
    /// added later without forcing the algorithm implementation to be gate-native.
    [<RequireQualifiedAccess>]
    type QpeUnitary =
        /// U|1⟩ = e^(iθ)|1⟩ (implemented via CP in the controlled case).
        | PhaseGate of theta: float

        /// T gate (π/8 gate): phase θ = π/4.
        | TGate

        /// S gate (√Z): phase θ = π/2.
        | SGate

        /// Rz(θ) = [[e^(-iθ/2), 0], [0, e^(iθ/2)]].
        ///
        /// Controlled variant uses CRZ.
        | RotationZ of theta: float

    type QpeIntent = {
        /// Number of counting (precision) qubits.
        CountingQubits: int
 
        /// Number of target qubits.
        ///
        /// Current intent implementation supports only `TargetQubits = 1`.
        TargetQubits: int
 
        /// Unitary whose phase is estimated.
        Unitary: QpeUnitary
 
        /// If true, prepares the target in |1⟩ via X on the first target qubit.
        PrepareTargetOne: bool
 
        /// Whether to apply bit-reversal swaps after inverse QFT.
        ///
        /// QPE does not fundamentally require these swaps: omitting them yields a bit-reversed
        /// counting register that can be classically un-reversed during post-processing.
        ApplySwaps: bool
    }

    /// Inversion method family for HHL intent.
    ///
    /// Mirrors `Algorithms.HHLTypes.EigenvalueInversionMethod`, but lives in Core to avoid
    /// algorithm-layer dependencies in the backend abstraction.
    type HhlEigenvalueInversionMethod =
        | ExactRotation of normalizationConstant: float
        | LinearApproximation of normalizationConstant: float
        | PiecewiseLinear of segments: (float * float * float)[]

    /// HHL intent (educational diagonal-matrix variant).
    ///
    /// Important: this intent currently assumes the matrix is diagonal and provided via its
    /// diagonal eigenvalues. The input |b⟩ is expected to already be encoded in the *solution*
    /// register of the provided quantum state.
    type HhlIntent = {
        /// Number of qubits used for eigenvalue estimation (reserved for future full QPE).
        EigenvalueQubits: int

        /// Number of logical qubits for the solution register.
        SolutionQubits: int

        /// Diagonal eigenvalues of A (length must match the solution space dimension).
        DiagonalEigenvalues: float[]

        /// Eigenvalue inversion method.
        InversionMethod: HhlEigenvalueInversionMethod

        /// Minimum eigenvalue threshold for numerical stability.
        MinEigenvalue: float
    }
 
    [<RequireQualifiedAccess>]
    type AlgorithmOperation =
        /// Quantum Fourier Transform intent.
        | QFT of QftIntent
 
        /// Quantum Phase Estimation intent.
        ///
        /// Transforms `|0⟩^(CountingQubits+TargetQubits)` to the standard QPE state,
        /// ready for measurement on the counting register.
        | QPE of QpeIntent

        /// HHL (Harrow-Hassidim-Lloyd) intent.
        | HHL of HhlIntent
 
        /// Prepare the uniform superposition |s⟩.
        | GroverPrepare of numQubits: int
 
        /// Apply the oracle phase flip to marked states.
        | GroverOraclePhaseFlip of GroverIntent
 
        /// Apply Grover diffusion (inversion about mean).
        | GroverDiffusion of numQubits: int


    /// Extension point for operations not represented in the core DU.
    ///
    /// Why this exists:
    /// - The `QuantumOperation` DU is intentionally *closed* for core primitives.
    /// - Some apps/libraries need to add new operation kinds without editing this repo.
    ///
    /// Backends may choose to support extensions by pattern-matching `QuantumOperation.Extension`.
    type IQuantumOperationExtension =
        /// Stable identifier for capability checks and diagnostics.
        abstract member Id: string

    /// Optional contract: extension can lower itself to core operations.
    ///
    /// This is the most portable mechanism: any backend that supports the resulting
    /// operations can execute the extension.
    type ILowerToOperationsExtension =
        inherit IQuantumOperationExtension
        abstract member LowerToGates: unit -> CircuitBuilder.Gate list

    /// Optional contract: extension can apply directly to a gate-based StateVector.
    ///
    /// This targets the local simulator fast-path without requiring the backend
    /// to know about the extension type.
    type IApplyToStateVectorExtension =
        inherit IQuantumOperationExtension
        abstract member ApplyToStateVector: LocalSimulator.StateVector.StateVector -> LocalSimulator.StateVector.StateVector

    /// Direction of an F-move (basis change in fusion tree)
    type FMoveDirection =
        | Forward
        | Backward

    /// Quantum operation types (gates, braids, algorithm intent, and extensions)
    ///
    /// Represents operations that can be applied to quantum states.
    /// Backend implementation determines how operation is executed.
    [<RequireQualifiedAccess>]
    type QuantumOperation =
        /// Algorithm-level intent (preferred when available).
        | Algorithm of AlgorithmOperation

        /// Open-world extension operation.
        | Extension of IQuantumOperationExtension

        /// Gate-based operation (standard quantum gates)
        ///
        /// Applied by gate-based backends directly.
        /// Some non-gate backends may choose to compile gates to their native model.
        | Gate of CircuitBuilder.Gate
        
        /// Braiding operation (topological quantum computing)
        ///
        /// Parameters:
        ///   anyonIndex - Index of left anyon in pair to braid
        ///
        /// Applied by topological backends directly.
        /// Gate-based backends cannot execute (return error).
        | Braid of anyonIndex: int
        
        /// Measurement operation
        ///
        /// Parameters:
        ///   qubitIndex - Index of qubit to measure
        ///
        /// Returns measurement outcome and collapsed state.
        | Measure of qubitIndex: int
        
        /// F-move operation (basis change in fusion tree)
        ///
        /// Parameters:
        ///   direction - Forward or backward F-move
        ///   depth - Depth in fusion tree
        ///
        /// Only applicable to topological backends.
        | FMove of direction: FMoveDirection * depth: int
        
        /// Sequence of operations (batch execution)
        ///
        /// More efficient than individual operations (reduces overhead).
        | Sequence of QuantumOperation list
    
    /// Unified quantum backend interface
    /// 
    /// Extends IQuantumBackend with state-based execution capabilities.
    /// All existing IQuantumBackend methods remain (backward compatible).
    /// 
    /// New methods:
    /// - ExecuteToState: Get quantum state instead of just measurements
    /// - ApplyOperation: Apply operation to existing state
    /// - NativeStateType: Query backend's preferred representation
    /// 
    /// Usage:
    ///   let backend = LocalBackend() :> IQuantumBackend
    ///   let! state = backend.ExecuteToState circuit
    ///   let! evolved = backend.ApplyOperation (Gate H(0)) state
    type IQuantumBackend =
        /// Execute circuit and return quantum state (not just measurements)
        /// 
        /// This is the key method enabling algorithm implementations that work
        /// with quantum states directly.
        /// 
        /// Parameters:
        ///   circuit - Quantum circuit to execute
        /// 
        /// Returns:
        ///   QuantumState - Final quantum state after circuit execution
        ///   Backend returns state in its native representation (no conversion)
        /// 
        /// Use cases:
        /// - Quantum state tomography (inspect amplitudes)
        /// - Intermediate state inspection for debugging
        /// - Multi-stage algorithms (QFT → QPE → Shor)
        /// - Backend switching mid-computation
        /// - Variational algorithms (VQE, QAOA) with classical feedback
        /// 
        /// Performance:
        /// - Same cost as Execute, but returns state instead of measuring
        /// - No additional overhead for state extraction
        /// 
        /// Example:
        ///   let circuit = CircuitBuilder.create 3 |> addGate (H 0) |> addGate (CNOT (0,1))
        ///   let! state = backend.ExecuteToState circuit
        ///   match state with
        ///   | QuantumState.StateVector sv ->
        ///       let amp = StateVector.getAmplitude 0 sv
        ///       printfn "Amplitude of |000⟩: %A" amp
        ///   | _ -> ()
        abstract member ExecuteToState: ICircuit -> Result<QuantumState, QuantumError>
        
        /// Get backend's native state representation type
        /// 
        /// Indicates which QuantumState variant the backend uses natively.
        /// Helps algorithms make intelligent decisions about conversions.
        /// 
        /// Returns:
        ///   QuantumStateType - GateBased, TopologicalBraiding, Sparse, or Mixed
        /// 
        /// Examples:
        ///   LocalBackend().NativeStateType = GateBased
        ///   TopologicalBackend().NativeStateType = TopologicalBraiding
        ///   StabilizerBackend().NativeStateType = Sparse
        /// 
        /// Usage:
        ///   // Optimize: Avoid conversion if state already matches backend
        ///   if QuantumState.stateType state = backend.NativeStateType then
        ///       backend.ApplyOperation op state  // No conversion!
        ///   else
        ///       (* convert first *)
        abstract member NativeStateType: QuantumStateType
        
        /// Apply quantum operation to existing state
        /// 
        /// More efficient than building full circuit for single operations.
        /// Enables iterative algorithms (VQE, QAOA) and hybrid classical-quantum loops.
        /// 
        /// Parameters:
        ///   operation - Gate, braid, or other quantum operation
        ///   state - Current quantum state
        /// 
        /// Returns:
        ///   Evolved quantum state after applying operation
        /// 
        /// Behavior:
        /// - If operation matches backend type: Apply natively (fast)
        /// - If operation doesn't match: Convert or error
        ///   * LocalBackend + Gate → apply directly ✓
        ///   * LocalBackend + Braid → error ✗
        ///   * TopologicalBackend + Braid → apply directly ✓
        ///   * TopologicalBackend + Gate → compile to braid first, then apply ✓
        /// 
        /// Example:
        ///   let mutable state = QuantumState.StateVector (StateVector.init 3)
        ///   
        ///   // Iterative algorithm (VQE optimization loop)
        ///   for iteration in 0 .. 100 do
        ///       let! evolved = backend.ApplyOperation (Gate (RY (0, params.[0]))) state
        ///       let! final = backend.ApplyOperation (Gate (CNOT (0,1))) evolved
        ///       
        ///       let energy = measureEnergy final hamiltonian
        ///       params <- optimizeParams params energy  // Classical optimization
        ///       
        ///       state <- final
        abstract member ApplyOperation: QuantumOperation -> QuantumState -> Result<QuantumState, QuantumError>
        
        /// Check if backend supports a specific operation type
        /// 
        /// Parameters:
        ///   operation - Operation to check
        /// 
        /// Returns:
        ///   true if backend can execute this operation natively or through compilation
        ///   false if operation is unsupported
        /// 
        /// Example:
        ///   let canBraid = backend.SupportsOperation (Braid 0)
        ///   if canBraid then
        ///       (* use braiding operations *)
        ///   else
        ///       (* fall back to gates *)
        abstract member SupportsOperation: QuantumOperation -> bool
        
        /// Backend name (for logging and diagnostics)
        /// 
        /// Returns:
        ///   Human-readable name of the backend (e.g. "Local Simulator", "IonQ", "Rigetti")
        /// 
        /// Example:
        ///   let backend = LocalBackend()
        ///   printfn "Using backend: %s" backend.Name
        abstract member Name: string
        
        /// Initialize quantum state without running a circuit
        /// 
        /// Creates initial state |0⟩^⊗n in backend's native representation.
        /// Faster than ExecuteToState with empty circuit.
        /// 
        /// Parameters:
        ///   numQubits - Number of qubits
        /// 
        /// Returns:
        ///   QuantumState initialized to |000...0⟩
        /// 
        /// Example:
        ///   let! state = backend.InitializeState 5
        ///   // state = |00000⟩ in backend's native representation
        abstract member InitializeState: int -> Result<QuantumState, QuantumError>
    
    /// Backend capabilities descriptor
    /// 
    /// Describes what features a backend supports.
    /// Helps algorithms make intelligent decisions.
    type BackendCapabilities = {
        /// Maximum number of qubits supported
        MaxQubits: int option
        
        /// Native state representation
        NativeStateType: QuantumStateType
        
        /// Supported gate types (if gate-based backend)
        SupportedGates: Set<string> option
        
        /// Supports braiding operations (if topological backend)
        SupportsBraiding: bool
        
        /// Supports arbitrary unitaries (vs restricted gate set)
        SupportsArbitraryUnitaries: bool
        
        /// Supports mid-circuit measurement
        SupportsMidCircuitMeasurement: bool
        
        /// Supports reset operations
        SupportsReset: bool
        
        /// Estimated noise level (0.0 = noiseless, 1.0 = maximum noise)
        NoiseLevel: float option
        
        /// Backend is simulator (true) or hardware (false)
        IsSimulator: bool
    }
    
    /// Helper functions for working with unified backends
    module UnifiedBackend =
        
        /// Create backend capabilities from IQuantumBackend
        let getCapabilities (backend: IQuantumBackend) : BackendCapabilities =
            {
                MaxQubits = None  // Query from backend if available
                NativeStateType = backend.NativeStateType
                SupportedGates = None  // Query from IQuantumBackend.SupportedGates
                SupportsBraiding = backend.SupportsOperation (QuantumOperation.Braid 0)
                SupportsArbitraryUnitaries = true  // Default assumption
                SupportsMidCircuitMeasurement = false  // Conservative default
                SupportsReset = false  // Conservative default
                NoiseLevel = Some 0.0  // Assume noiseless unless specified
                IsSimulator = true  // Conservative default - assume simulator
            }
        
        /// Execute operation with automatic state conversion if needed
        /// 
        /// Handles conversion between state types transparently.
        /// For batch operations, prefer applySequence which converts once.
        /// 
        /// Parameters:
        ///   backend - Unified quantum backend
        ///   operation - Operation to apply
        ///   state - Current quantum state
        /// 
        /// Returns:
        ///   Evolved state (possibly converted to backend's native type)
        let applyWithConversion 
            (backend: IQuantumBackend) 
            (operation: QuantumOperation) 
            (state: QuantumState)
            : Result<QuantumState, QuantumError> =
            
            let stateType = QuantumState.stateType state
            let nativeType = backend.NativeStateType
            
            if stateType = nativeType then
                // Optimal: No conversion needed
                backend.ApplyOperation operation state
            else
                // Convert to backend's native type
                let converted = QuantumStateConversion.convert nativeType state
                backend.ApplyOperation operation converted
        
        /// Apply sequence of operations efficiently
        /// 
        /// Batches operations to minimize overhead.
        /// Converts state once if needed (not per operation).
        /// 
        /// Parameters:
        ///   backend - Unified quantum backend
        ///   operations - List of operations to apply
        ///   initialState - Starting quantum state
        /// 
        /// Returns:
        ///   Final quantum state after all operations
        let applySequence
            (backend: IQuantumBackend)
            (operations: QuantumOperation list)
            (initialState: QuantumState)
            : Result<QuantumState, QuantumError> =
            
            if List.isEmpty operations then
                Ok initialState
            else
                // Convert to native type once (not per operation)
                let nativeType = backend.NativeStateType
                let stateType = QuantumState.stateType initialState
                
                let convertedState =
                    if stateType <> nativeType then
                        QuantumStateConversion.convert nativeType initialState
                    else
                        initialState
                
                // Apply operations sequentially (fold with short-circuit on error)
                operations
                |> List.fold (fun stateResult op ->
                    stateResult |> Result.bind (fun s -> backend.ApplyOperation op s)
                ) (Ok convertedState)
        
        /// Measure state and return classical outcomes
        /// 
        /// Convenience wrapper around QuantumState.measure.
        /// 
        /// Parameters:
        ///   state - Quantum state to measure
        ///   shots - Number of measurement samples
        /// 
        /// Returns:
        ///   Array of bitstrings (measurement outcomes)
        let measureState (state: QuantumState) (shots: int) : int[][] =
            QuantumState.measure state shots
