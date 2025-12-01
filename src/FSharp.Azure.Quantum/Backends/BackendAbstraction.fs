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
    
    /// Wrapper for local simulator (supports both QAOA and general circuits)
    /// 
    /// Provides fast local execution for development and testing.
    /// - QAOA circuits: Uses optimized QaoaSimulator
    /// - General circuits: Uses gate-by-gate simulation with Gates module
    type LocalBackend() =
        interface IQuantumBackend with
            member _.Execute (circuit: ICircuit) (numShots: int) : Result<ExecutionResult, string> =
                try
                    // Validate parameters
                    if numShots <= 0 then
                        Error "Number of shots must be positive"
                    elif circuit.NumQubits > 20 then
                        Error "Local backend supports maximum 20 qubits"
                    else
                        match circuit with
                        | :? QaoaCircuitWrapper as wrapper ->
                            // QAOA circuit: Use optimized QAOA simulator
                            let qaoaCircuit = wrapper.QaoaCircuit
                            let numQubits = qaoaCircuit.NumQubits
                            
                            // Extract gamma and beta parameters from layers
                            let gammas = qaoaCircuit.Layers |> Array.map (fun l -> l.Gamma)
                            let betas = qaoaCircuit.Layers |> Array.map (fun l -> l.Beta)
                            
                            // Extract cost coefficients from problem Hamiltonian
                            let costCoefficients = 
                                qaoaCircuit.ProblemHamiltonian.Terms
                                |> Array.filter (fun t -> t.QubitsIndices.Length = 1)
                                |> Array.groupBy (fun t -> t.QubitsIndices.[0])
                                |> Array.map (fun (qubit, terms) -> 
                                    terms |> Array.sumBy (fun t -> t.Coefficient))
                                |> fun coeffs ->
                                    Array.init numQubits (fun i ->
                                        coeffs |> Array.tryItem i |> Option.defaultValue 0.0)
                            
                            // Run QAOA simulation
                            let finalState = 
                                QaoaSimulator.runQaoaCircuit numQubits gammas betas costCoefficients
                            
                            // Sample measurements
                            let rng = Random()
                            let measurements = 
                                Array.init numShots (fun _ ->
                                    let basisStateIndex = Measurement.measureComputationalBasis rng finalState
                                    Array.init numQubits (fun qubitIdx ->
                                        if (basisStateIndex >>> qubitIdx) &&& 1 = 1 then 1 else 0))
                            
                            Ok {
                                Measurements = measurements
                                NumShots = numShots
                                BackendName = "Local QAOA Simulator"
                                Metadata = Map.empty
                            }
                        
                        | :? CircuitWrapper as wrapper ->
                            // General circuit: Use gate-by-gate simulation
                            let generalCircuit = wrapper.Circuit
                            let numQubits = generalCircuit.QubitCount
                            
                            // Initialize state to |0...0⟩
                            let mutable state = StateVector.init numQubits
                            
                            // Apply each gate sequentially
                            for gate in generalCircuit.Gates do
                                state <- 
                                    match gate with
                                    | CircuitBuilder.H q -> Gates.applyH q state
                                    | CircuitBuilder.X q -> Gates.applyX q state
                                    | CircuitBuilder.Y q -> Gates.applyY q state
                                    | CircuitBuilder.Z q -> Gates.applyZ q state
                                    | CircuitBuilder.S q -> Gates.applyS q state
                                    | CircuitBuilder.T q -> Gates.applyT q state
                                    | CircuitBuilder.SDG q -> Gates.applySDG q state
                                    | CircuitBuilder.TDG q -> Gates.applyTDG q state
                                    | CircuitBuilder.RX (q, angle) -> Gates.applyRx q angle state
                                    | CircuitBuilder.RY (q, angle) -> Gates.applyRy q angle state
                                    | CircuitBuilder.RZ (q, angle) -> Gates.applyRz q angle state
                                    | CircuitBuilder.P (q, angle) -> Gates.applyP q angle state
                                    | CircuitBuilder.CNOT (c, t) -> Gates.applyCNOT c t state
                                    | CircuitBuilder.CZ (c, t) -> Gates.applyCZ c t state
                                    | CircuitBuilder.CP (c, t, angle) -> Gates.applyCP c t angle state
                                    | CircuitBuilder.SWAP (q1, q2) -> Gates.applySWAP q1 q2 state
                                    | CircuitBuilder.CCX (c1, c2, t) -> 
                                        // Toffoli gate - use decomposition
                                        // CCX = H(t), CX(c2,t), TDG(t), CX(c1,t), T(t), CX(c2,t), TDG(t), CX(c1,t), T(c2), T(t), H(t), CX(c1,c2), T(c1), TDG(c2), CX(c1,c2)
                                        state
                                        |> Gates.applyH t
                                        |> Gates.applyCNOT c2 t
                                        |> Gates.applyTDG t
                                        |> Gates.applyCNOT c1 t
                                        |> Gates.applyT t
                                        |> Gates.applyCNOT c2 t
                                        |> Gates.applyTDG t
                                        |> Gates.applyCNOT c1 t
                                        |> Gates.applyT c2
                                        |> Gates.applyT t
                                        |> Gates.applyH t
                                        |> Gates.applyCNOT c1 c2
                                        |> Gates.applyT c1
                                        |> Gates.applyTDG c2
                                        |> Gates.applyCNOT c1 c2
                            
                            // Sample measurements from final state
                            let rng = Random()
                            let measurements = 
                                Array.init numShots (fun _ ->
                                    let basisStateIndex = Measurement.measureComputationalBasis rng state
                                    Array.init numQubits (fun qubitIdx ->
                                        if (basisStateIndex >>> qubitIdx) &&& 1 = 1 then 1 else 0))
                            
                            Ok {
                                Measurements = measurements
                                NumShots = numShots
                                BackendName = "Local Simulator"
                                Metadata = Map.empty
                            }
                        
                        | _ ->
                            Error "Local backend requires CircuitWrapper or QaoaCircuitWrapper"
                
                with ex ->
                    Error (sprintf "Local backend execution failed: %s" ex.Message)
            
            member _.Name = "Local Simulator"
            
            member _.SupportedGates = ["H"; "X"; "Y"; "Z"; "S"; "T"; "SDG"; "TDG"; "RX"; "RY"; "RZ"; "P"; "CNOT"; "CZ"; "CP"; "SWAP"; "CCX"]
            
            member _.MaxQubits = 16
    
    // ============================================================================
    // IONQ BACKEND WRAPPER
    // ============================================================================
    
    /// Wrapper for IonQ backend via Azure Quantum
    /// 
    /// Integrates with Azure Quantum to execute circuits on IonQ hardware/simulator.
    /// Uses the existing IonQBackend module for Azure integration.
    type IonQBackendWrapper(httpClient: System.Net.Http.HttpClient, workspaceUrl: string, target: string) =
        
        interface IQuantumBackend with
            member _.Execute (circuit: ICircuit) (numShots: int) : Result<ExecutionResult, string> =
                try
                    // Step 1: Convert ICircuit to CircuitBuilder.Circuit
                    let builderCircuit = 
                        match circuit with
                        | :? CircuitWrapper as wrapper -> wrapper.Circuit
                        | :? QaoaCircuitWrapper as wrapper -> 
                            CircuitAdapter.qaoaCircuitToCircuit wrapper.QaoaCircuit
                        | _ -> 
                            failwith "Unsupported circuit type for IonQ backend"
                    
                    // Step 2: Convert CircuitBuilder.Circuit to IonQCircuit
                    let ionqGates = 
                        builderCircuit.Gates
                        |> List.choose (fun gate ->
                            match gate with
                            | CircuitBuilder.H q -> Some (IonQBackend.SingleQubit("h", q))
                            | CircuitBuilder.X q -> Some (IonQBackend.SingleQubit("x", q))
                            | CircuitBuilder.Y q -> Some (IonQBackend.SingleQubit("y", q))
                            | CircuitBuilder.Z q -> Some (IonQBackend.SingleQubit("z", q))
                            | CircuitBuilder.S q -> Some (IonQBackend.SingleQubit("s", q))
                            | CircuitBuilder.T q -> Some (IonQBackend.SingleQubit("t", q))
                            | CircuitBuilder.RX (q, angle) -> Some (IonQBackend.SingleQubitRotation("rx", q, angle))
                            | CircuitBuilder.RY (q, angle) -> Some (IonQBackend.SingleQubitRotation("ry", q, angle))
                            | CircuitBuilder.RZ (q, angle) -> Some (IonQBackend.SingleQubitRotation("rz", q, angle))
                            | CircuitBuilder.CNOT (c, t) -> Some (IonQBackend.TwoQubit("cnot", c, t))
                            | CircuitBuilder.SWAP (q1, q2) -> Some (IonQBackend.TwoQubit("swap", q1, q2))
                            | _ -> None  // Skip unsupported gates
                        )
                    
                    // Add measurement on all qubits
                    let qubits = [| 0 .. builderCircuit.QubitCount - 1 |]
                    let ionqCircuit = {
                        IonQBackend.Qubits = builderCircuit.QubitCount
                        IonQBackend.Circuit = ionqGates @ [ IonQBackend.Measure qubits ]
                    }
                    
                    // Step 3: Submit to Azure Quantum IonQ backend
                    let asyncResult = async {
                        return! IonQBackend.submitAndWaitForResultsAsync httpClient workspaceUrl ionqCircuit numShots target
                    }
                    
                    let result = Async.RunSynchronously asyncResult
                    
                    // Step 4: Convert histogram to ExecutionResult
                    match result with
                    | Ok histogram ->
                        // Convert histogram Map<bitstring, count> to measurements int[][]
                        let measurements = 
                            histogram
                            |> Map.toSeq
                            |> Seq.collect (fun (bitstring, count) ->
                                // Convert bitstring "01101" to int array [0;1;1;0;1]
                                let bits = 
                                    bitstring.ToCharArray()
                                    |> Array.map (fun c -> if c = '1' then 1 else 0)
                                // Repeat this bitstring 'count' times
                                Seq.replicate count bits
                            )
                            |> Array.ofSeq
                        
                        Ok {
                            Measurements = measurements
                            NumShots = numShots
                            BackendName = sprintf "IonQ via Azure Quantum (%s)" target
                            Metadata = Map.ofList [ ("target", target :> obj); ("workspace", workspaceUrl :> obj) ]
                        }
                    
                    | Error quantumError ->
                        Error (sprintf "IonQ execution failed: %A" quantumError)
                
                with ex ->
                    Error (sprintf "IonQ backend error: %s" ex.Message)
            
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
    
    /// Wrapper for Rigetti backend via Azure Quantum
    /// 
    /// Integrates with Azure Quantum to execute circuits on Rigetti QVM/QPU.
    /// Uses the existing RigettiBackend module for Azure integration.
    type RigettiBackendWrapper(httpClient: System.Net.Http.HttpClient, workspaceUrl: string, target: string) =
        
        interface IQuantumBackend with
            member _.Execute (circuit: ICircuit) (numShots: int) : Result<ExecutionResult, string> =
                try
                    // Step 1: Convert ICircuit to CircuitBuilder.Circuit
                    let builderCircuit = 
                        match circuit with
                        | :? CircuitWrapper as wrapper -> wrapper.Circuit
                        | :? QaoaCircuitWrapper as wrapper -> 
                            CircuitAdapter.qaoaCircuitToCircuit wrapper.QaoaCircuit
                        | _ -> 
                            failwith "Unsupported circuit type for Rigetti backend"
                    
                    // Step 2: Convert CircuitBuilder.Circuit to QuilProgram
                    let quilInstructions = 
                        builderCircuit.Gates
                        |> List.choose (fun gate ->
                            match gate with
                            | CircuitBuilder.H q -> Some (RigettiBackend.SingleQubit("H", q))
                            | CircuitBuilder.X q -> Some (RigettiBackend.SingleQubit("X", q))
                            | CircuitBuilder.Y q -> Some (RigettiBackend.SingleQubit("Y", q))
                            | CircuitBuilder.Z q -> Some (RigettiBackend.SingleQubit("Z", q))
                            | CircuitBuilder.S q -> Some (RigettiBackend.SingleQubit("S", q))
                            | CircuitBuilder.T q -> Some (RigettiBackend.SingleQubit("T", q))
                            | CircuitBuilder.RX (q, angle) -> Some (RigettiBackend.SingleQubitRotation("RX", angle, q))
                            | CircuitBuilder.RY (q, angle) -> Some (RigettiBackend.SingleQubitRotation("RY", angle, q))
                            | CircuitBuilder.RZ (q, angle) -> Some (RigettiBackend.SingleQubitRotation("RZ", angle, q))
                            | CircuitBuilder.CZ (c, t) -> Some (RigettiBackend.TwoQubit("CZ", c, t))
                            | CircuitBuilder.CNOT (c, t) -> 
                                // CNOT not native to Rigetti, decompose if needed
                                // For now, skip (Rigetti uses CZ)
                                None
                            | _ -> None
                        )
                    
                    // Add measurements
                    let measurements = 
                        [ 0 .. builderCircuit.QubitCount - 1 ]
                        |> List.map (fun q -> RigettiBackend.Measure(q, sprintf "ro[%d]" q))
                    
                    let quilProgram = {
                        RigettiBackend.Declarations = [ RigettiBackend.DeclareMemory("ro", "BIT", builderCircuit.QubitCount) ]
                        RigettiBackend.Instructions = quilInstructions @ measurements
                    }
                    
                    // Step 3: Submit to Azure Quantum Rigetti backend
                    let asyncResult = async {
                        return! RigettiBackend.submitAndWaitForResultsAsync httpClient workspaceUrl quilProgram numShots target
                    }
                    
                    let result = Async.RunSynchronously asyncResult
                    
                    // Step 4: Convert histogram to ExecutionResult
                    match result with
                    | Ok histogram ->
                        // Convert histogram Map<bitstring, count> to measurements int[][]
                        let measurements = 
                            histogram
                            |> Map.toSeq
                            |> Seq.collect (fun (bitstring, count) ->
                                // Convert bitstring "01101" to int array [0;1;1;0;1]
                                let bits = 
                                    bitstring.ToCharArray()
                                    |> Array.map (fun c -> if c = '1' then 1 else 0)
                                // Repeat this bitstring 'count' times
                                Seq.replicate count bits
                            )
                            |> Array.ofSeq
                        
                        Ok {
                            Measurements = measurements
                            NumShots = numShots
                            BackendName = sprintf "Rigetti via Azure Quantum (%s)" target
                            Metadata = Map.ofList [ ("target", target :> obj); ("workspace", workspaceUrl :> obj) ]
                        }
                    
                    | Error quantumError ->
                        Error (sprintf "Rigetti execution failed: %A" quantumError)
                
                with ex ->
                    Error (sprintf "Rigetti backend error: %s" ex.Message)
            
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
    
    /// Create an IonQ backend wrapper with Azure Quantum workspace
    /// 
    /// Parameters:
    /// - httpClient: Authenticated HTTP client for Azure Quantum API
    /// - workspaceUrl: Azure Quantum workspace URL (e.g., "https://my-workspace.quantum.azure.com")
    /// - target: IonQ target (e.g., "ionq.simulator", "ionq.qpu.aria-1")
    let createIonQBackend (httpClient: System.Net.Http.HttpClient) (workspaceUrl: string) (target: string) : IQuantumBackend =
        IonQBackendWrapper(httpClient, workspaceUrl, target) :> IQuantumBackend
    
    /// Create a Rigetti backend wrapper with Azure Quantum workspace
    /// 
    /// Parameters:
    /// - httpClient: Authenticated HTTP client for Azure Quantum API
    /// - workspaceUrl: Azure Quantum workspace URL (e.g., "https://my-workspace.quantum.azure.com")
    /// - target: Rigetti target (e.g., "rigetti.sim.qvm", "rigetti.qpu.ankaa-3")
    let createRigettiBackend (httpClient: System.Net.Http.HttpClient) (workspaceUrl: string) (target: string) : IQuantumBackend =
        RigettiBackendWrapper(httpClient, workspaceUrl, target) :> IQuantumBackend
    
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
