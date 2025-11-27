namespace FSharp.Azure.Quantum.Core

open FSharp.Azure.Quantum.Core.QaoaCircuit

/// Unified Quantum Backend API
/// 
/// Provides a consistent interface for executing quantum circuits on
/// local simulation (v1.0) and Azure Quantum backends (planned for v2.0).
module QuantumBackend =
    
    /// Measurement counts from quantum execution (bitstring -> frequency)
    type MeasurementCounts = Map<string, int>
    
    /// Quantum execution result (backend-agnostic)
    type ExecutionResult =
        {
            /// Measurement counts (bitstring -> frequency)
            Counts: MeasurementCounts
            
            /// Number of shots executed
            Shots: int
            
            /// Backend used for execution
            Backend: string
            
            /// Execution time in milliseconds
            ExecutionTimeMs: float
            
            /// Job ID (Azure only, None for local)
            JobId: string option
        }
    
    /// Backend type identifier (v1.0: Local only, Azure coming in v2.0)
    [<Struct>]
    type BackendType =
        | Local
    
    /// Local simulation backend operations
    module Local =
        
        /// Execute QAOA circuit on local simulator
        /// 
        /// Takes a QaoaCircuit and executes it locally with state vector simulation.
        /// Limited to circuits with ≤10 qubits due to exponential memory growth.
        /// 
        /// Parameters:
        /// - circuit: QaoaCircuit to execute
        /// - shots: Number of measurement samples
        /// 
        /// Returns: ExecutionResult with measurement counts or error message
        val simulate: circuit: QaoaCircuit -> shots: int -> Result<ExecutionResult, string>
    
    /// Unified backend interface
    type IBackend =
        /// Execute quantum circuit
        abstract member Execute: QaoaCircuit -> int -> Result<ExecutionResult, string>
    
    /// Local backend implementation
    type LocalBackend =
        /// Create local backend instance
        new: unit -> LocalBackend
        
        interface IBackend
    
    /// Execute circuit on specified backend type (v1.0: Local only)
    val execute: backendType: BackendType -> circuit: QaoaCircuit -> shots: int -> Result<ExecutionResult, string>
    
    /// Auto-select backend based on circuit size
    /// 
    /// - ≤10 qubits: Use local simulator (fast, free)
    /// - >10 qubits: Returns error (Azure not available in v1.0)
    val autoExecute: circuit: QaoaCircuit -> shots: int -> Result<ExecutionResult, string>
    
    /// Execute multiple circuits in batch with automatic batching (v1.0: Local only)
    /// 
    /// Submits multiple circuits using the batching strategy to amortize overhead.
    /// If batching is disabled, circuits are submitted individually.
    /// 
    /// Parameters:
    /// - backendType: The backend to use (v1.0: Local only)
    /// - circuits: List of circuits to execute
    /// - shots: Number of shots per circuit
    /// - config: Batch configuration (size, timeout, enabled)
    /// 
    /// Returns: Result with list of execution results or error message
    val executeBatch:
        backendType: BackendType ->
        circuits: QaoaCircuit list ->
        shots: int ->
        config: Batching.BatchConfig ->
        Async<Result<ExecutionResult list, string>>
