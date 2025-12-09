namespace FSharp.Azure.Quantum.Core

open System.Threading
open FSharp.Azure.Quantum
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
    
    /// Quantum operation types (gates or braiding operations)
    /// 
    /// Represents operations that can be applied to quantum states.
    /// Backend implementation determines how operation is executed.
    [<RequireQualifiedAccess>]
    type QuantumOperation =
        /// Gate-based operation (standard quantum gates)
        /// 
        /// Applied by gate-based backends directly.
        /// Topological backends compile to braiding operations.
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
        ///   direction - Forward or backward F-move (as obj to avoid circular dependency)
        ///   depth - Depth in fusion tree
        /// 
        /// Only applicable to topological backends.
        /// Direction type is FSharp.Azure.Quantum.Topological.TopologicalOperations.FMoveDirection
        | FMove of direction: obj * depth: int
        
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
        /// Caches converted state if multiple operations will be applied.
        /// 
        /// Parameters:
        ///   backend - Unified quantum backend
        ///   operation - Operation to apply
        ///   state - Current quantum state
        ///   willReuse - Whether state will be reused (enables caching)
        /// 
        /// Returns:
        ///   Evolved state (possibly converted to backend's native type)
        let applyWithConversion 
            (backend: IQuantumBackend) 
            (operation: QuantumOperation) 
            (state: QuantumState)
            (willReuse: bool)
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
