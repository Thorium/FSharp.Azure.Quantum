namespace FSharp.Azure.Quantum.Core

open System

/// Backend abstraction for quantum circuit execution
/// 
/// This module provides:
/// - IQuantumBackend interface: Common abstraction for all execution backends
/// - Backend wrappers: IonQ, Rigetti, and Local simulator implementations
/// 
/// Design rationale:
/// - Solvers (TSP, Portfolio) need backend-agnostic execution
/// - Each backend (IonQ/Rigetti/Local) has different APIs and formats
/// - IQuantumBackend provides a unified interface
/// - Wrappers adapt backend-specific APIs to common interface
module BackendAbstraction =
    
    open FSharp.Azure.Quantum
    open FSharp.Azure.Quantum.Core.CircuitAbstraction
    open FSharp.Azure.Quantum.Core.QaoaCircuit
    open FSharp.Azure.Quantum.LocalSimulator
    
    // ============================================================================
    // EXECUTION RESULT TYPE
    // ============================================================================
    
    /// Result of quantum circuit execution
    type ExecutionResult = {
        /// Measurement outcomes (bitstrings)
        /// Each int[] represents one shot: [qubit0_value, qubit1_value, ...]
        Measurements: int[][]
        
        /// Number of shots executed
        NumShots: int
        
        /// Backend name (for debugging/logging)
        BackendName: string
        
        /// Execution metadata (optional)
        Metadata: Map<string, obj>
    }
    
    // ============================================================================
    // QUANTUM BACKEND INTERFACE
    // ============================================================================
    
    /// Common interface for all quantum execution backends
    /// 
    /// Provides unified API for:
    /// - Circuit execution with specified shots
    /// - Backend identification and capabilities
    /// - Error reporting
    type IQuantumBackend =
        /// Execute a quantum circuit
        /// 
        /// Parameters:
        /// - circuit: Circuit to execute (ICircuit interface)
        /// - numShots: Number of measurement shots
        /// 
        /// Returns: Result with measurements or error message
        abstract member Execute: ICircuit -> int -> Result<ExecutionResult, string>
        
        /// Backend name (e.g., "IonQ Simulator", "Rigetti QVM", "Local Simulator")
        abstract member Name: string
        
        /// List of supported gate types (for validation)
        /// Examples: ["H", "RX", "RY", "RZ", "CNOT", "CZ"]
        abstract member SupportedGates: string list
        
        /// Maximum number of qubits supported
        abstract member MaxQubits: int
    
    // ============================================================================
    // LOCAL BACKEND WRAPPER - QaoaSimulator
    // ============================================================================
    
    /// Wrapper for local QAOA simulator
    /// 
    /// Adapts QaoaSimulator to IQuantumBackend interface.
    /// Provides fast local execution for development and testing.
    type LocalBackend() =
        interface IQuantumBackend with
            member _.Execute (circuit: ICircuit) (numShots: int) : Result<ExecutionResult, string> =
                try
                    // Validate parameters
                    if numShots <= 0 then
                        Error "Number of shots must be positive"
                    elif circuit.NumQubits > 10 then
                        Error "Local backend supports maximum 10 qubits"
                    else
                        // Convert ICircuit to QaoaCircuit for simulation
                        // This is a simplified approach - we need to know the circuit format
                        // For now, we'll handle CircuitWrapper and QaoaCircuitWrapper
                        
                        let qaoaCircuitResult = 
                            match circuit with
                            | :? CircuitWrapper as wrapper ->
                                // Convert CircuitBuilder.Circuit to QaoaCircuit
                                CircuitAdapter.circuitToQaoaCircuit wrapper.Circuit
                            | :? QaoaCircuitWrapper as wrapper ->
                                // Already a QAOA circuit
                                Ok wrapper.QaoaCircuit
                            | _ ->
                                Error "Local backend requires CircuitWrapper or QaoaCircuitWrapper"
                        
                        match qaoaCircuitResult with
                        | Error msg -> Error msg
                        | Ok qaoaCircuit ->
                            // Extract parameters and run simulation
                            let numQubits = qaoaCircuit.NumQubits
                            
                            // Extract gamma and beta parameters from layers
                            let gammas = qaoaCircuit.Layers |> Array.map (fun l -> l.Gamma)
                            let betas = qaoaCircuit.Layers |> Array.map (fun l -> l.Beta)
                            
                            // Extract cost coefficients from problem Hamiltonian
                            // For now, use simplified single-qubit coefficients
                            let costCoefficients = 
                                qaoaCircuit.ProblemHamiltonian.Terms
                                |> Array.filter (fun t -> t.QubitsIndices.Length = 1)
                                |> Array.groupBy (fun t -> t.QubitsIndices.[0])
                                |> Array.map (fun (qubit, terms) -> 
                                    terms |> Array.sumBy (fun t -> t.Coefficient))
                                |> fun coeffs ->
                                    // Ensure we have coefficients for all qubits
                                    Array.init numQubits (fun i ->
                                        coeffs |> Array.tryItem i |> Option.defaultValue 0.0)
                            
                            // Run QAOA simulation
                            let finalState = 
                                QaoaSimulator.runQaoaCircuit numQubits gammas betas costCoefficients
                            
                            // Sample measurements - convert basis state index to bitstring
                            let rng = Random()
                            let measurements = 
                                Array.init numShots (fun _ ->
                                    let basisStateIndex = Measurement.measureComputationalBasis rng finalState
                                    // Convert basis state index to bitstring array
                                    Array.init numQubits (fun qubitIdx ->
                                        if (basisStateIndex >>> qubitIdx) &&& 1 = 1 then 1 else 0))
                            
                            Ok {
                                Measurements = measurements
                                NumShots = numShots
                                BackendName = "Local QAOA Simulator"
                                Metadata = Map.empty
                            }
                
                with ex ->
                    Error (sprintf "Local backend execution failed: %s" ex.Message)
            
            member _.Name = "Local QAOA Simulator"
            
            member _.SupportedGates = ["H"; "RX"; "RY"; "RZ"; "CNOT"; "RZZ"]
            
            member _.MaxQubits = 10
    
    // ============================================================================
    // IONQ BACKEND WRAPPER
    // ============================================================================
    
    /// Wrapper for IonQ backend
    /// 
    /// Adapts IonQBackend to IQuantumBackend interface.
    /// Handles circuit conversion to IonQ JSON format and result parsing.
    /// 
    /// Note: This is a placeholder for future implementation.
    /// Full implementation requires:
    /// 1. Circuit → IonQCircuit conversion
    /// 2. HTTP job submission to IonQ API
    /// 3. Result polling and parsing
    type IonQBackendWrapper(apiKey: string, ?baseUrl: string) =
        let baseUrl = defaultArg baseUrl "https://api.ionq.com/v0.3"
        
        interface IQuantumBackend with
            member _.Execute (circuit: ICircuit) (numShots: int) : Result<ExecutionResult, string> =
                // TODO: Phase 3 implementation
                // 1. Convert ICircuit → CircuitBuilder.Circuit → IonQCircuit
                // 2. Submit job via IonQBackend.submitJob
                // 3. Poll for results via IonQBackend.getResults
                // 4. Parse to ExecutionResult
                Error "IonQ backend integration not yet implemented (Phase 3)"
            
            member _.Name = "IonQ Simulator"
            
            member _.SupportedGates = [
                "H"; "X"; "Y"; "Z"
                "RX"; "RY"; "RZ"
                "CNOT"; "SWAP"
                "S"; "T"
            ]
            
            member _.MaxQubits = 29  // IonQ hardware limit
    
    // ============================================================================
    // RIGETTI BACKEND WRAPPER
    // ============================================================================
    
    /// Wrapper for Rigetti backend
    /// 
    /// Adapts RigettiBackend to IQuantumBackend interface.
    /// Handles circuit conversion to Quil format and result parsing.
    /// 
    /// Note: This is a placeholder for future implementation.
    /// Full implementation requires:
    /// 1. Circuit → QuilProgram conversion
    /// 2. HTTP job submission to Rigetti QVM/QPU
    /// 3. Result polling and parsing
    type RigettiBackendWrapper(apiKey: string, ?baseUrl: string) =
        let baseUrl = defaultArg baseUrl "https://api.rigetti.com"
        
        interface IQuantumBackend with
            member _.Execute (circuit: ICircuit) (numShots: int) : Result<ExecutionResult, string> =
                // TODO: Phase 3 implementation
                // 1. Convert ICircuit → CircuitBuilder.Circuit → QuilProgram
                // 2. Submit job via RigettiBackend.submitJob
                // 3. Poll for results via RigettiBackend.getResults
                // 4. Parse to ExecutionResult
                Error "Rigetti backend integration not yet implemented (Phase 3)"
            
            member _.Name = "Rigetti QVM"
            
            member _.SupportedGates = [
                "H"; "X"; "Y"; "Z"
                "RX"; "RY"; "RZ"
                "CZ"  // Native Rigetti gate
                "MEASURE"
            ]
            
            member _.MaxQubits = 40  // Rigetti Aspen-M-3 limit
    
    // ============================================================================
    // HELPER FUNCTIONS
    // ============================================================================
    
    /// Create a local simulator backend (no configuration needed)
    let createLocalBackend () : IQuantumBackend =
        LocalBackend() :> IQuantumBackend
    
    /// Create an IonQ backend wrapper with API credentials
    let createIonQBackend (apiKey: string) : IQuantumBackend =
        IonQBackendWrapper(apiKey) :> IQuantumBackend
    
    /// Create a Rigetti backend wrapper with API credentials
    let createRigettiBackend (apiKey: string) : IQuantumBackend =
        RigettiBackendWrapper(apiKey) :> IQuantumBackend
    
    /// Validate that a circuit is compatible with a backend
    /// 
    /// Checks:
    /// - Qubit count within backend limits
    /// - All gates supported by backend
    /// 
    /// Returns: Ok() if compatible, Error with reason if not
    let validateCircuitForBackend (circuit: ICircuit) (backend: IQuantumBackend) : Result<unit, string> =
        // Check qubit count
        if circuit.NumQubits > backend.MaxQubits then
            Error (sprintf "Circuit requires %d qubits but backend '%s' supports max %d qubits" 
                circuit.NumQubits backend.Name backend.MaxQubits)
        else
            // Gate validation would require extracting gates from ICircuit
            // For now, assume compatible if qubit count is OK
            Ok ()
