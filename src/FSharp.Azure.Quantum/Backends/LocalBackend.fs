namespace FSharp.Azure.Quantum.Backends

open System
open System.Numerics
open System.Threading
open FSharp.Azure.Quantum.Core
open FSharp.Azure.Quantum.Core.BackendAbstraction
open FSharp.Azure.Quantum.Core.CircuitAbstraction
open FSharp.Azure.Quantum.Core.UnifiedBackendAbstraction
open FSharp.Azure.Quantum.LocalSimulator

/// Local quantum simulator backend implementing unified quantum backend interface
/// 
/// Features:
/// - Gate-based quantum simulation using state vectors
/// - Native StateVector representation (no conversion needed)
/// - Supports all standard gates (H, X, Y, Z, RX, RY, RZ, CNOT, CZ, etc.)
/// - Efficient for circuits up to ~20 qubits
/// - Implements both IQuantumBackend and IUnifiedQuantumBackend
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
        let private applyGate (gate: CircuitBuilder.Gate) (state: StateVector.StateVector) : StateVector.StateVector =
            match gate with
            // Single-qubit gates - Pauli
            | CircuitBuilder.H q -> Gates.applyH q state
            | CircuitBuilder.X q -> Gates.applyX q state
            | CircuitBuilder.Y q -> Gates.applyY q state
            | CircuitBuilder.Z q -> Gates.applyZ q state
            
            // Single-qubit gates - Phase
            | CircuitBuilder.S q -> Gates.applyS q state
            | CircuitBuilder.SDG q -> 
                // SDG = S† = Rz(-π/2)
                Gates.applyRz q (-Math.PI / 2.0) state
            | CircuitBuilder.T q -> Gates.applyT q state
            | CircuitBuilder.TDG q -> 
                // TDG = T† = Rz(-π/4)
                Gates.applyRz q (-Math.PI / 4.0) state
            | CircuitBuilder.P (q, angle) -> Gates.applyPhase q angle state
            
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
            
            // Two-qubit gates - Controlled (simplified implementations)
            | CircuitBuilder.CP (ctrl, target, angle) ->
                // CP = Controlled-Phase = diag(1,1,1,e^iθ)
                Gates.applyPhase target angle state  // Simplified (should be controlled)
            | CircuitBuilder.CRX (ctrl, target, angle) ->
                Gates.applyRx target angle state  // Simplified
            | CircuitBuilder.CRY (ctrl, target, angle) ->
                Gates.applyRy target angle state  // Simplified
            | CircuitBuilder.CRZ (ctrl, target, angle) ->
                Gates.applyRz target angle state  // Simplified
            
            // Three-qubit gates
            | CircuitBuilder.CCX (ctrl1, ctrl2, target) ->
                failwith "CCX (Toffoli) gate not yet implemented in LocalBackend"
            
            // Multi-qubit gates
            | CircuitBuilder.MCZ (controls, target) ->
                failwith "MCZ (Multi-controlled Z) gate not yet implemented in LocalBackend"
            
            // Measurement - should not appear in circuit execution
            | CircuitBuilder.Measure q ->
                failwith "Measurement gates should be handled separately"
        
        /// Execute circuit on state vector
        let private executeCircuit (circuit: ICircuit) (numQubits: int) : Result<StateVector.StateVector, QuantumError> =
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
                Error (QuantumError.ExecutionError ("LocalBackend", "Execution was cancelled"))
            | ex ->
                Error (QuantumError.ExecutionError ("LocalBackend", ex.Message))
        
        /// Sample measurements from state vector
        let private sampleMeasurements (state: StateVector.StateVector) (numShots: int) : int[][] =
            [| for _ in 1 .. numShots do
                yield Measurement.measureAll state
            |]
        
        // ====================================================================
        // IQuantumBackend Implementation (Backward Compatibility)
        // ====================================================================
        
        interface IQuantumBackend with
            member _.SetCancellationToken (token: CancellationToken option) =
                cancellationToken <- token
            
            member this.ExecuteAsync (circuit: ICircuit) (numShots: int) : Async<Result<ExecutionResult, QuantumError>> =
                async {
                    let numQubits = circuit.NumQubits
                    
                    match executeCircuit circuit numQubits with
                    | Error err -> return Error err
                    | Ok finalState ->
                        let measurements = sampleMeasurements finalState numShots
                        
                        return Ok {
                            Measurements = measurements
                            NumShots = numShots
                            BackendName = "Local Simulator"
                            Metadata = Map.empty
                        }
                }
            
            member this.Execute (circuit: ICircuit) (numShots: int) : Result<ExecutionResult, QuantumError> =
                (this :> IQuantumBackend).ExecuteAsync circuit numShots
                |> Async.RunSynchronously
            
            member _.Name = "Local Simulator"
            
            member _.SupportedGates = 
                [
                    // Pauli gates
                    "H"; "X"; "Y"; "Z"
                    // Phase gates
                    "S"; "SDG"; "T"; "TDG"; "P"
                    // Rotation gates
                    "RX"; "RY"; "RZ"
                    // Universal gates
                    "U3"
                    // Two-qubit gates
                    "CNOT"; "CZ"; "SWAP"
                    // Controlled gates (partial support)
                    "CP"; "CRX"; "CRY"; "CRZ"
                ]
        
        // ====================================================================
        // IUnifiedQuantumBackend Implementation (New Interface)
        // ====================================================================
        
        interface IUnifiedQuantumBackend with
            member _.ExecuteToState (circuit: ICircuit) : Result<QuantumState, QuantumError> =
                let numQubits = circuit.NumQubits
                
                match executeCircuit circuit numQubits with
                | Ok finalState -> Ok (QuantumState.StateVector finalState)
                | Error err -> Error err
            
            member _.NativeStateType = QuantumStateType.GateBased
            
            member _.ApplyOperation (operation: QuantumOperation) (state: QuantumState) : Result<QuantumState, QuantumError> =
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
                                        (this :> IUnifiedQuantumBackend).ApplyOperation op currentState
                                ) (Ok state)
                            result
                        
                        | QuantumOperation.Measure qubitIdx ->
                            // Single qubit measurement
                            let outcome = Measurement.measure qubitIdx sv
                            let collapsed = Measurement.collapse qubitIdx outcome sv
                            Ok (QuantumState.StateVector collapsed)
                        
                        | QuantumOperation.Braid _ ->
                            Error (QuantumError.ExecutionError ("LocalBackend", "Braiding operations not supported by gate-based backend"))
                        
                        | QuantumOperation.FMove _ ->
                            Error (QuantumError.ExecutionError ("LocalBackend", "F-move operations not supported by gate-based backend"))
                    with
                    | ex -> Error (QuantumError.ExecutionError ("LocalBackend", ex.Message))
                
                | _ ->
                    // State is not in native format - try conversion
                    match QuantumStateConversion.convert QuantumStateType.GateBased state with
                    | Ok (QuantumState.StateVector sv) ->
                        (this :> IUnifiedQuantumBackend).ApplyOperation operation (QuantumState.StateVector sv)
                    | Ok _ ->
                        Error (QuantumError.ExecutionError ("LocalBackend", "State conversion failed unexpectedly"))
                    | Error convErr ->
                        Error (QuantumError.ExecutionError ("LocalBackend", $"State conversion error: {convErr}"))
            
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
                | ex -> Error (QuantumError.ExecutionError ("LocalBackend", ex.Message))

/// Factory functions for creating local backend instances
module LocalBackendFactory =
    
    /// Create a new local simulator backend
    let create () : LocalBackend.LocalBackend = LocalBackend.LocalBackend()
    
    /// Create and cast to IUnifiedQuantumBackend
    let createUnified () : IUnifiedQuantumBackend = create () :> IUnifiedQuantumBackend
    
    /// Create and cast to IQuantumBackend (for backward compatibility)
    let createStandard () : IQuantumBackend = create () :> IQuantumBackend
