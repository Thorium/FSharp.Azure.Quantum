namespace FSharp.Azure.Quantum.Backends

open System
open System.Numerics
open System.Threading
open FSharp.Azure.Quantum
open FSharp.Azure.Quantum.Core
open FSharp.Azure.Quantum.Core.BackendAbstraction
open FSharp.Azure.Quantum.Core.CircuitAbstraction
open FSharp.Azure.Quantum.Core.BackendAbstraction
open FSharp.Azure.Quantum.LocalSimulator

/// Local quantum simulator backend implementing unified quantum backend interface
/// 
/// Features:
/// - Gate-based quantum simulation using state vectors
/// - Native StateVector representation (no conversion needed)
/// - Supports all standard gates (H, X, Y, Z, RX, RY, RZ, CNOT, CZ, etc.)
/// - Efficient for circuits up to 30 qubits (practical limit ~20 qubits depending on available memory)
/// - Implements both IQuantumBackend and IQuantumBackend
/// 
/// Usage:
///   let backend = LocalBackend()
///   let! state = backend.ExecuteToState circuit  // Get quantum state
///   let! result = backend.Execute circuit 1000   // Get measurements
///   
///   // State-based execution
///   let! initialState = backend.InitializeState 3
///   let! evolved = backend.ApplyOperation (QuantumOperation.Gate (CircuitBuilder.H 0)) initialState
module LocalBackend =
    
    /// Local simulator backend
    type LocalBackend() =
        let mutable cancellationToken: CancellationToken option = None
        
        // ====================================================================
        // HELPER: Gate Execution on StateVector
        // ====================================================================
        
        /// Apply single gate to state vector
        let applyGate (gate: CircuitBuilder.Gate) (state: StateVector.StateVector) : StateVector.StateVector =
            match gate with
            // Single-qubit gates - Pauli
            | CircuitBuilder.H q -> Gates.applyH q state
            | CircuitBuilder.X q -> Gates.applyX q state
            | CircuitBuilder.Y q -> Gates.applyY q state
            | CircuitBuilder.Z q -> Gates.applyZ q state
            
            // Single-qubit gates - Phase
            | CircuitBuilder.S q -> Gates.applyS q state
            | CircuitBuilder.SDG q -> Gates.applySDG q state
            | CircuitBuilder.T q -> Gates.applyT q state
            | CircuitBuilder.TDG q -> Gates.applyTDG q state
            | CircuitBuilder.P (q, angle) -> Gates.applyP q angle state
            
            // Single-qubit gates - Rotation
            | CircuitBuilder.RX (q, angle) -> Gates.applyRx q angle state
            | CircuitBuilder.RY (q, angle) -> Gates.applyRy q angle state
            | CircuitBuilder.RZ (q, angle) -> Gates.applyRz q angle state
            
            // Single-qubit gates - Universal
            | CircuitBuilder.U3 (q, theta, phi, lambda) ->
                // U3(θ,φ,λ) = Rz(φ)·Ry(θ)·Rz(λ)
                state
                |> Gates.applyRz q lambda
                |> Gates.applyRy q theta
                |> Gates.applyRz q phi
            
            // Two-qubit gates - Standard
            | CircuitBuilder.CNOT (ctrl, target) -> Gates.applyCNOT ctrl target state
            | CircuitBuilder.CZ (ctrl, target) -> Gates.applyCZ ctrl target state
            | CircuitBuilder.SWAP (q1, q2) -> Gates.applySWAP q1 q2 state
            
            // Two-qubit gates - Controlled (proper implementations)
            | CircuitBuilder.CP (ctrl, target, angle) ->
                Gates.applyCP ctrl target angle state
            | CircuitBuilder.CRX (ctrl, target, angle) ->
                Gates.applyCRX ctrl target angle state
            | CircuitBuilder.CRY (ctrl, target, angle) ->
                Gates.applyCRY ctrl target angle state
            | CircuitBuilder.CRZ (ctrl, target, angle) ->
                Gates.applyCRZ ctrl target angle state
            
            // Three-qubit gates
            | CircuitBuilder.CCX (ctrl1, ctrl2, target) ->
                Gates.applyCCX ctrl1 ctrl2 target state
            
            // Multi-qubit gates
            | CircuitBuilder.MCZ (controls, target) ->
                Gates.applyMultiControlledZ controls target state
            
            // Measurement - should not appear in circuit execution
            | CircuitBuilder.Measure q ->
                failwith "Measurement gates should be handled separately"
        
        /// Execute circuit on state vector
        let executeCircuit (circuit: CircuitBuilder.Circuit) (numQubits: int) : Result<StateVector.StateVector, QuantumError> =
            try
                // Initialize to |0⟩^⊗n
                let initialState = StateVector.init numQubits
                
                // Apply gates sequentially
                let finalState =
                    circuit.Gates
                    |> List.fold (fun state gate ->
                        applyGate gate state
                    ) initialState
                
                Ok finalState
            with
            | :? System.OperationCanceledException ->
                Error (QuantumError.OperationError ("LocalBackend", "Execution was cancelled"))
            | ex ->
                Error (QuantumError.OperationError ("LocalBackend", ex.Message))
        
        /// Sample measurements from state vector
        let sampleMeasurements (state: StateVector.StateVector) (numShots: int) : int[][] =
            [| for _ in 1 .. numShots do
                yield Measurement.measureAll state
            |]
        
        // ====================================================================
        // IQuantumBackend Implementation
        // ====================================================================
        
        interface IQuantumBackend with
            member this.ExecuteToState (circuit: CircuitAbstraction.ICircuit) : Result<QuantumState, QuantumError> =
                let numQubits = circuit.NumQubits
                
                // Pattern match on specific circuit wrapper types to extract gates
                match box circuit with
                | :? CircuitAbstraction.CircuitWrapper as wrapper ->
                    // Extract CircuitBuilder.Circuit and apply gates
                    let cbCircuit = wrapper.Circuit
                    try
                        let initialState = StateVector.init numQubits
                        let finalState =
                            cbCircuit.Gates
                            |> List.fold (fun state gate ->
                                applyGate gate state
                            ) initialState
                        Ok (QuantumState.StateVector finalState)
                    with
                    | :? System.OperationCanceledException ->
                        Error (QuantumError.OperationError ("LocalBackend", "Execution was cancelled"))
                    | ex ->
                        Error (QuantumError.OperationError ("LocalBackend", ex.Message))
                
                | _ ->
                    // For unknown circuit types, cannot execute directly
                    Error (QuantumError.OperationError ("LocalBackend", $"Circuit type {circuit.GetType().Name} not supported by LocalBackend.ExecuteToState - wrap with CircuitWrapper"))
            
            member _.NativeStateType = QuantumStateType.GateBased
            
            member self.ApplyOperation (operation: QuantumOperation) (state: QuantumState) : Result<QuantumState, QuantumError> =
                match state with
                | QuantumState.StateVector sv ->
                    try
                        match operation with
                        | QuantumOperation.Gate gate ->
                            let evolved = applyGate gate sv
                            Ok (QuantumState.StateVector evolved)
                        
                        | QuantumOperation.Sequence ops ->
                            // Apply operations sequentially
                            let result =
                                ops
                                |> List.fold (fun stateResult op ->
                                    match stateResult with
                                    | Error err -> Error err
                                    | Ok currentState ->
                                        (self :> IQuantumBackend).ApplyOperation op currentState
                                ) (Ok state)
                            result
                        
                        | QuantumOperation.Measure qubitIdx ->
                            // Single qubit measurement
                            let outcome = Measurement.measure qubitIdx sv
                            let collapsed = Measurement.collapse qubitIdx outcome sv
                            Ok (QuantumState.StateVector collapsed)
                        
                        | QuantumOperation.Braid _ ->
                            Error (QuantumError.OperationError ("LocalBackend", "Braiding operations not supported by gate-based backend"))
                        
                        | QuantumOperation.FMove _ ->
                            Error (QuantumError.OperationError ("LocalBackend", "F-move operations not supported by gate-based backend"))
                    with
                    | ex -> Error (QuantumError.OperationError ("LocalBackend", ex.Message))
                
                | _ ->
                    // State is not in native format - try conversion
                    let converted = QuantumStateConversion.convert QuantumStateType.GateBased state
                    match converted with
                    | QuantumState.StateVector sv ->
                        (self :> IQuantumBackend).ApplyOperation operation (QuantumState.StateVector sv)
                    | _ ->
                        Error (QuantumError.OperationError ("LocalBackend", "State conversion failed unexpectedly"))
            
            member _.SupportsOperation (operation: QuantumOperation) : bool =
                match operation with
                | QuantumOperation.Gate _ -> true
                | QuantumOperation.Sequence _ -> true
                | QuantumOperation.Measure _ -> true
                | QuantumOperation.Braid _ -> false  // No braiding in gate-based backend
                | QuantumOperation.FMove _ -> false  // No F-moves in gate-based backend
            
            member _.InitializeState (numQubits: int) : Result<QuantumState, QuantumError> =
                try
                    let initialState = StateVector.init numQubits
                    Ok (QuantumState.StateVector initialState)
                with
                | ex -> Error (QuantumError.OperationError ("LocalBackend", ex.Message))

/// Factory functions for creating local backend instances
module LocalBackendFactory =
    
    /// Create a new local simulator backend
    let create () : LocalBackend.LocalBackend = LocalBackend.LocalBackend()
    
    /// Create and cast to IQuantumBackend
    let createUnified () : IQuantumBackend = create () :> IQuantumBackend
    
    /// Create and cast to IQuantumBackend (for backward compatibility)
    let createStandard () : IQuantumBackend = create () :> IQuantumBackend
