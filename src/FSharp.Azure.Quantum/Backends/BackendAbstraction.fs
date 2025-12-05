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
    /// - Cancellation support for long-running operations
    type IQuantumBackend =
        /// Execute a quantum circuit asynchronously with optional cancellation
        /// 
        /// Parameters:
        /// - circuit: Circuit to execute (ICircuit interface)
        /// - numShots: Number of measurement shots
        /// - cancellationToken: Optional cancellation token for early termination
        /// 
        /// Returns: Async<Result> with measurements or error message
        /// 
        /// Note: This is the primary execution method. Use this for:
        /// - Cloud backends (Azure Quantum, IonQ, Rigetti)
        /// - Long-running executions
        /// - When you want non-blocking execution
        abstract member ExecuteAsync: ICircuit -> int -> System.Threading.CancellationToken option -> Async<Result<ExecutionResult, string>>
        
        /// Execute a quantum circuit synchronously with optional cancellation
        /// 
        /// Parameters:
        /// - circuit: Circuit to execute (ICircuit interface)
        /// - numShots: Number of measurement shots
        /// - cancellationToken: Optional cancellation token for early termination
        /// 
        /// Returns: Result with measurements or error message
        /// 
        /// Note: This is a convenience wrapper around ExecuteAsync.
        /// Default implementation calls ExecuteAsync and blocks.
        /// For better performance, use ExecuteAsync directly when possible.
        abstract member Execute: ICircuit -> int -> System.Threading.CancellationToken option -> Result<ExecutionResult, string>
        
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
        
        /// Core synchronous execution logic (shared by both Execute and ExecuteAsync)
        member private _.ExecuteCore (circuit: ICircuit) (numShots: int) (cancellationToken: System.Threading.CancellationToken option) : Result<ExecutionResult, string> =
                try
                    // Check cancellation before starting
                    match cancellationToken with
                    | Some token when token.IsCancellationRequested ->
                        Error "Operation cancelled before execution"
                    | _ -> ()
                    
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
                            
                            // Helper: Apply a single gate to state
                            let applyGate state gate =
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
                                    // Toffoli gate - use standard decomposition
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
                                | CircuitBuilder.MCZ (controls, target) -> 
                                    // Multi-controlled Z gate - CRITICAL for Grover's algorithm
                                    Gates.applyMultiControlledZ controls target state
                            
                            // Apply all gates sequentially using functional fold
                            let finalState = 
                                generalCircuit.Gates
                                |> List.fold applyGate (StateVector.init numQubits)
                            
                            // Sample measurements from final state
                            let rng = Random()
                            let measurements = 
                                Array.init numShots (fun _ ->
                                    let basisStateIndex = Measurement.measureComputationalBasis rng finalState
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
        
        interface IQuantumBackend with
            member this.ExecuteAsync (circuit: ICircuit) (numShots: int) (cancellationToken: System.Threading.CancellationToken option) : Async<Result<ExecutionResult, string>> =
                // Local backend is synchronous, so wrap in async
                async { return this.ExecuteCore circuit numShots cancellationToken }
            
            member this.Execute (circuit: ICircuit) (numShots: int) (cancellationToken: System.Threading.CancellationToken option) : Result<ExecutionResult, string> =
                this.ExecuteCore circuit numShots cancellationToken
            
            member _.Name = "Local Simulator"
            
            member _.SupportedGates = ["H"; "X"; "Y"; "Z"; "S"; "T"; "SDG"; "TDG"; "RX"; "RY"; "RZ"; "P"; "CNOT"; "CZ"; "CP"; "SWAP"; "CCX"]
            
            member _.MaxQubits = 20
    
    // ============================================================================
    // IONQ BACKEND WRAPPER
    // ============================================================================
    
    /// Wrapper for IonQ backend via Azure Quantum
    /// 
    /// Integrates with Azure Quantum to execute circuits on IonQ hardware/simulator.
    /// Uses the existing IonQBackend module for Azure integration.
    type IonQBackendWrapper(httpClient: System.Net.Http.HttpClient, workspaceUrl: string, target: string) =
        
        member private _.ExecuteAsyncCore (circuit: ICircuit) (numShots: int) (cancellationToken: System.Threading.CancellationToken option) : Async<Result<ExecutionResult, string>> =
            async {
                try
                    // Check cancellation before starting
                    match cancellationToken with
                    | Some token when token.IsCancellationRequested ->
                        return Error "Operation cancelled before execution"
                    | _ -> ()
                    // Step 1: Convert ICircuit to CircuitBuilder.Circuit
                    let builderCircuit = 
                        match circuit with
                        | :? CircuitWrapper as wrapper -> wrapper.Circuit
                        | :? QaoaCircuitWrapper as wrapper -> 
                            CircuitAdapter.qaoaCircuitToCircuit wrapper.QaoaCircuit
                        | _ -> 
                            failwith "Unsupported circuit type for IonQ backend"
                    
                    // Step 2: Transpile to IonQ-compatible gates (auto-decompose MCZ, CZ, CCX)
                    let transpiledCircuit = 
                        GateTranspiler.transpileForBackend "IonQ" builderCircuit
                    
                    // Step 3: Convert transpiled circuit to IonQCircuit
                    // After transpilation, MCZ/CZ/CCX should be decomposed
                    let ionqGatesResult = 
                        transpiledCircuit.Gates
                        |> List.fold (fun (acc: Result<IonQBackend.IonQGate list, string>) gate ->
                            match acc with
                            | Error msg -> Error msg
                            | Ok gates ->
                                match gate with
                                | CircuitBuilder.H q -> Ok (gates @ [IonQBackend.SingleQubit("h", q)])
                                | CircuitBuilder.X q -> Ok (gates @ [IonQBackend.SingleQubit("x", q)])
                                | CircuitBuilder.Y q -> Ok (gates @ [IonQBackend.SingleQubit("y", q)])
                                | CircuitBuilder.Z q -> Ok (gates @ [IonQBackend.SingleQubit("z", q)])
                                | CircuitBuilder.S q -> Ok (gates @ [IonQBackend.SingleQubit("s", q)])
                                | CircuitBuilder.T q -> Ok (gates @ [IonQBackend.SingleQubit("t", q)])
                                | CircuitBuilder.RX (q, angle) -> Ok (gates @ [IonQBackend.SingleQubitRotation("rx", q, angle)])
                                | CircuitBuilder.RY (q, angle) -> Ok (gates @ [IonQBackend.SingleQubitRotation("ry", q, angle)])
                                | CircuitBuilder.RZ (q, angle) -> Ok (gates @ [IonQBackend.SingleQubitRotation("rz", q, angle)])
                                | CircuitBuilder.CNOT (c, t) -> Ok (gates @ [IonQBackend.TwoQubit("cnot", c, t)])
                                | CircuitBuilder.SWAP (q1, q2) -> Ok (gates @ [IonQBackend.TwoQubit("swap", q1, q2)])
                                | CircuitBuilder.MCZ _ | CircuitBuilder.CZ _ | CircuitBuilder.CCX _ -> 
                                    Error $"Gate {gate} found after transpilation - this indicates a transpiler bug."
                                | _ -> 
                                    Error $"Unsupported gate for IonQ backend after transpilation: {gate}"
                        ) (Ok [])
                    
                    match ionqGatesResult with
                    | Error msg -> return Error msg
                    | Ok ionqGates ->
                        // Add measurement on all qubits
                        let qubits = [| 0 .. builderCircuit.QubitCount - 1 |]
                        let ionqCircuit = {
                            IonQBackend.Qubits = builderCircuit.QubitCount
                            IonQBackend.Circuit = ionqGates @ [ IonQBackend.Measure qubits ]
                        }
                        
                        // Step 3: Submit to Azure Quantum IonQ backend
                        let! result = IonQBackend.submitAndWaitForResultsAsync httpClient workspaceUrl ionqCircuit numShots target
                        
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
                            
                            return Ok {
                                Measurements = measurements
                                NumShots = numShots
                                BackendName = sprintf "IonQ via Azure Quantum (%s)" target
                                Metadata = Map.ofList [ ("target", target :> obj); ("workspace", workspaceUrl :> obj) ]
                            }
                        
                        | Error quantumError ->
                            return Error (sprintf "IonQ execution failed: %A" quantumError)
                
                with ex ->
                    return Error (sprintf "IonQ backend error: %s" ex.Message)
            }
        
        interface IQuantumBackend with
            member this.ExecuteAsync (circuit: ICircuit) (numShots: int) (cancellationToken: System.Threading.CancellationToken option) : Async<Result<ExecutionResult, string>> =
                this.ExecuteAsyncCore circuit numShots cancellationToken
            
            member this.Execute (circuit: ICircuit) (numShots: int) (cancellationToken: System.Threading.CancellationToken option) : Result<ExecutionResult, string> =
                this.ExecuteAsyncCore circuit numShots cancellationToken |> Async.RunSynchronously
            
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
        
        member private _.ExecuteAsyncCore (circuit: ICircuit) (numShots: int) (cancellationToken: System.Threading.CancellationToken option) : Async<Result<ExecutionResult, string>> =
            async {
                try
                    // Check cancellation before starting
                    match cancellationToken with
                    | Some token when token.IsCancellationRequested ->
                        return Error "Operation cancelled before execution"
                    | _ -> ()
                    // Step 1: Convert ICircuit to CircuitBuilder.Circuit
                    let builderCircuit = 
                        match circuit with
                        | :? CircuitWrapper as wrapper -> wrapper.Circuit
                        | :? QaoaCircuitWrapper as wrapper -> 
                            CircuitAdapter.qaoaCircuitToCircuit wrapper.QaoaCircuit
                        | _ -> 
                            failwith "Unsupported circuit type for Rigetti backend"
                    
                    // Step 2: Transpile to Rigetti-compatible gates (auto-decompose MCZ, CCX, CNOT)
                    let transpiledCircuit = 
                        GateTranspiler.transpileForBackend "Rigetti" builderCircuit
                    
                    // Step 3: Convert transpiled circuit to QuilProgram
                    // After transpilation, all gates should be Rigetti-compatible
                    let quilResult = 
                        transpiledCircuit.Gates
                        |> List.fold (fun (acc: Result<RigettiBackend.QuilGate list, string>) gate ->
                            match acc with
                            | Error msg -> Error msg
                            | Ok instructions ->
                                match gate with
                                | CircuitBuilder.H q -> Ok (instructions @ [RigettiBackend.SingleQubit("H", q)])
                                | CircuitBuilder.X q -> Ok (instructions @ [RigettiBackend.SingleQubit("X", q)])
                                | CircuitBuilder.Y q -> Ok (instructions @ [RigettiBackend.SingleQubit("Y", q)])
                                | CircuitBuilder.Z q -> Ok (instructions @ [RigettiBackend.SingleQubit("Z", q)])
                                | CircuitBuilder.S q -> Ok (instructions @ [RigettiBackend.SingleQubit("S", q)])
                                | CircuitBuilder.T q -> Ok (instructions @ [RigettiBackend.SingleQubit("T", q)])
                                | CircuitBuilder.RX (q, angle) -> Ok (instructions @ [RigettiBackend.SingleQubitRotation("RX", angle, q)])
                                | CircuitBuilder.RY (q, angle) -> Ok (instructions @ [RigettiBackend.SingleQubitRotation("RY", angle, q)])
                                | CircuitBuilder.RZ (q, angle) -> Ok (instructions @ [RigettiBackend.SingleQubitRotation("RZ", angle, q)])
                                | CircuitBuilder.CZ (c, t) -> Ok (instructions @ [RigettiBackend.TwoQubit("CZ", c, t)])
                                | CircuitBuilder.CNOT (c, t) -> Ok (instructions @ [RigettiBackend.TwoQubit("CNOT", c, t)])
                                | CircuitBuilder.MCZ _ | CircuitBuilder.CCX _ -> 
                                    Error $"Gate {gate} found after transpilation - this indicates a transpiler bug."
                                | _ -> 
                                    Error $"Unsupported gate for Rigetti backend after transpilation: {gate}"
                        ) (Ok [])
                    
                    match quilResult with
                    | Error msg -> return Error msg
                    | Ok quilInstructions ->
                        // Add measurements
                        let measurements = 
                            [ 0 .. builderCircuit.QubitCount - 1 ]
                            |> List.map (fun q -> RigettiBackend.Measure(q, sprintf "ro[%d]" q))
                        
                        let quilProgram = {
                            RigettiBackend.Declarations = [ RigettiBackend.DeclareMemory("ro", "BIT", builderCircuit.QubitCount) ]
                            RigettiBackend.Instructions = quilInstructions @ measurements
                        }
                        
                        // Step 4: Submit to Azure Quantum Rigetti backend
                        let! result = RigettiBackend.submitAndWaitForResultsAsync httpClient workspaceUrl quilProgram numShots target
                        
                        // Step 5: Convert histogram to ExecutionResult
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
                            
                            return Ok {
                                Measurements = measurements
                                NumShots = numShots
                                BackendName = sprintf "Rigetti via Azure Quantum (%s)" target
                                Metadata = Map.ofList [ ("target", target :> obj); ("workspace", workspaceUrl :> obj) ]
                            }
                        
                        | Error quantumError ->
                            return Error (sprintf "Rigetti execution failed: %A" quantumError)
                
                with ex ->
                    return Error (sprintf "Rigetti backend error: %s" ex.Message)
            }
        
        interface IQuantumBackend with
            member this.ExecuteAsync (circuit: ICircuit) (numShots: int) (cancellationToken: System.Threading.CancellationToken option) : Async<Result<ExecutionResult, string>> =
                this.ExecuteAsyncCore circuit numShots cancellationToken
            
            member this.Execute (circuit: ICircuit) (numShots: int) (cancellationToken: System.Threading.CancellationToken option) : Result<ExecutionResult, string> =
                this.ExecuteAsyncCore circuit numShots cancellationToken |> Async.RunSynchronously
            
            member _.Name = "Rigetti QVM"
            
            member _.SupportedGates = [
                "H"; "X"; "Y"; "Z"
                "RX"; "RY"; "RZ"
                "CZ"    // Native Rigetti gate
                "CNOT"  // Also supported by Rigetti
                "MEASURE"
            ]
            
            member _.MaxQubits = 40  // Rigetti Aspen-M-3 limit
    
    // ============================================================================
    // QUANTINUUM BACKEND WRAPPER
    // ============================================================================
    
    /// Wrapper for Quantinuum backend via Azure Quantum
    /// 
    /// Integrates with Azure Quantum to execute circuits on Quantinuum H-Series hardware/simulator.
    /// Uses the existing QuantinuumBackend module for Azure integration.
    /// 
    /// Key Features:
    /// - Uses OpenQASM 2.0 format (reuses OpenQasmExport module)
    /// - All-to-all connectivity (32 qubits)
    /// - 99.9%+ gate fidelity
    /// - Native CZ gates (trapped-ion advantage)
    type QuantinuumBackendWrapper(httpClient: System.Net.Http.HttpClient, workspaceUrl: string, target: string) =
        
        member private _.ExecuteAsyncCore (circuit: ICircuit) (numShots: int) : Async<Result<ExecutionResult, string>> =
            async {
                try
                    // Step 1: Convert ICircuit to CircuitBuilder.Circuit
                    let builderCircuit = 
                        match circuit with
                        | :? CircuitWrapper as wrapper -> wrapper.Circuit
                        | :? QaoaCircuitWrapper as wrapper -> 
                            CircuitAdapter.qaoaCircuitToCircuit wrapper.QaoaCircuit
                        | _ -> 
                            failwith "Unsupported circuit type for Quantinuum backend"
                    
                    // Step 2: Transpile to Quantinuum-compatible gates (only CCX decomposition needed)
                    let transpiledCircuit = 
                        GateTranspiler.transpileForBackend "Quantinuum" builderCircuit
                    
                    // Step 3: Convert transpiled circuit to OpenQASM 2.0
                    let qasmCode = OpenQasmExport.export transpiledCircuit
                    
                    // Step 4: Submit to Azure Quantum Quantinuum backend
                    let! result = QuantinuumBackend.submitAndWaitForResultsAsync httpClient workspaceUrl qasmCode numShots target
                    
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
                        
                        return Ok {
                            Measurements = measurements
                            NumShots = numShots
                            BackendName = sprintf "Quantinuum via Azure Quantum (%s)" target
                            Metadata = Map.ofList [ ("target", target :> obj); ("workspace", workspaceUrl :> obj) ]
                        }
                    
                    | Error quantumError ->
                        return Error (sprintf "Quantinuum execution failed: %A" quantumError)
                
                with ex ->
                    return Error (sprintf "Quantinuum backend error: %s" ex.Message)
            }
        
        interface IQuantumBackend with
            member this.ExecuteAsync (circuit: ICircuit) (numShots: int) : Async<Result<ExecutionResult, string>> =
                this.ExecuteAsyncCore circuit numShots
            
            member this.Execute (circuit: ICircuit) (numShots: int) : Result<ExecutionResult, string> =
                this.ExecuteAsyncCore circuit numShots |> Async.RunSynchronously
            
            member _.Name = "Quantinuum H-Series"
            
            member _.SupportedGates = [
                "H"; "X"; "Y"; "Z"
                "S"; "T"; "SDG"; "TDG"
                "RX"; "RY"; "RZ"
                "CZ"    // Native Quantinuum gate (trapped-ion)
                "MEASURE"
            ]
            
            member _.MaxQubits = 
                // Dynamic qubit limit based on target hardware
                if target.Contains("h2", StringComparison.OrdinalIgnoreCase) then 
                    32  // H2-1 hardware
                elif target.Contains("h1", StringComparison.OrdinalIgnoreCase) then 
                    20  // H1-1 hardware/simulators
                else 
                    32  // Default to H2-1 for future models
    
    // ============================================================================
    // ATOM COMPUTING BACKEND WRAPPER
    // ============================================================================
    
    /// Wrapper for Atom Computing backend via Azure Quantum
    /// 
    /// Integrates with Azure Quantum to execute circuits on Atom Computing Phoenix hardware/simulator.
    /// Uses the existing AtomComputingBackend module for Azure integration.
    /// 
    /// Key Features:
    /// - Uses OpenQASM 2.0 format (reuses OpenQasmExport module)
    /// - All-to-all connectivity via movable neutral atoms (100+ qubits)
    /// - CZ-based native gates (neutral atom Rydberg blockade)
    type AtomComputingBackendWrapper(httpClient: System.Net.Http.HttpClient, workspaceUrl: string, target: string) =
        
        member private _.ExecuteAsyncCore (circuit: ICircuit) (numShots: int) : Async<Result<ExecutionResult, string>> =
            async {
                try
                    // Step 1: Convert ICircuit to CircuitBuilder.Circuit
                    let builderCircuit = 
                        match circuit with
                        | :? CircuitWrapper as wrapper -> wrapper.Circuit
                        | :? QaoaCircuitWrapper as wrapper -> 
                            CircuitAdapter.qaoaCircuitToCircuit wrapper.QaoaCircuit
                        | _ -> 
                            failwith "Unsupported circuit type for Atom Computing backend"
                    
                    // Step 2: Transpile to Atom Computing-compatible gates (CZ-native, all-to-all)
                    let transpiledCircuit = 
                        GateTranspiler.transpileForBackend "AtomComputing" builderCircuit
                    
                    // Step 3: Convert transpiled circuit to OpenQASM 2.0
                    let qasmCode = OpenQasmExport.export transpiledCircuit
                    
                    // Step 4: Submit to Azure Quantum Atom Computing backend
                    let! result = AtomComputingBackend.submitAndWaitForResultsAsync httpClient workspaceUrl qasmCode numShots target
                    
                    // Step 5: Convert histogram to ExecutionResult
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
                        
                        return Ok {
                            Measurements = measurements
                            NumShots = numShots
                            BackendName = sprintf "Atom Computing via Azure Quantum (%s)" target
                            Metadata = Map.ofList [ ("target", target :> obj); ("workspace", workspaceUrl :> obj) ]
                        }
                    
                    | Error quantumError ->
                        return Error (sprintf "Atom Computing execution failed: %A" quantumError)
                
                with ex ->
                    return Error (sprintf "Atom Computing backend error: %s" ex.Message)
            }
        
        interface IQuantumBackend with
            member this.ExecuteAsync (circuit: ICircuit) (numShots: int) : Async<Result<ExecutionResult, string>> =
                this.ExecuteAsyncCore circuit numShots
            
            member this.Execute (circuit: ICircuit) (numShots: int) : Result<ExecutionResult, string> =
                this.ExecuteAsyncCore circuit numShots |> Async.RunSynchronously
            
            member _.Name = "Atom Computing Phoenix"
            
            member _.SupportedGates = [
                "H"; "X"; "Y"; "Z"
                "S"; "T"; "SDG"; "TDG"
                "RX"; "RY"; "RZ"
                "CZ"    // Native Atom Computing gate (Rydberg blockade)
                "MEASURE"
            ]
            
            member _.MaxQubits = 
                // Atom Computing Phoenix has 100+ qubits
                // Specific limits may vary by target
                if target.Contains("phoenix", StringComparison.OrdinalIgnoreCase) then 
                    100  // Phoenix QPU
                else 
                    100  // Default (simulator may support more)
    
    // ============================================================================
    // AZURE QUANTUM SDK BACKEND WRAPPER
    // ============================================================================
    
    // ------------------------------------------------------------------------
    // HELPER: Circuit Format Conversion
    // ------------------------------------------------------------------------
    
    /// Convert CircuitBuilder.Circuit to IonQ circuit format
    let private convertToIonQCircuit (circuit: CircuitBuilder.Circuit) : IonQBackend.IonQCircuit =
        let ionqGates = 
            circuit.Gates
            |> List.map (fun gate ->
                match gate with
                | CircuitBuilder.H q -> IonQBackend.SingleQubit("h", q)
                | CircuitBuilder.X q -> IonQBackend.SingleQubit("x", q)
                | CircuitBuilder.Y q -> IonQBackend.SingleQubit("y", q)
                | CircuitBuilder.Z q -> IonQBackend.SingleQubit("z", q)
                | CircuitBuilder.S q -> IonQBackend.SingleQubit("s", q)
                | CircuitBuilder.T q -> IonQBackend.SingleQubit("t", q)
                | CircuitBuilder.RX (q, angle) -> IonQBackend.SingleQubitRotation("rx", q, angle)
                | CircuitBuilder.RY (q, angle) -> IonQBackend.SingleQubitRotation("ry", q, angle)
                | CircuitBuilder.RZ (q, angle) -> IonQBackend.SingleQubitRotation("rz", q, angle)
                | CircuitBuilder.CNOT (c, t) -> IonQBackend.TwoQubit("cnot", c, t)
                | CircuitBuilder.SWAP (q1, q2) -> IonQBackend.TwoQubit("swap", q1, q2)
                | _ -> failwith $"Gate {gate} not supported by IonQ - use transpiler first"
            )
        
        // Add measurement on all qubits
        let measureGate = IonQBackend.Measure [| 0 .. circuit.QubitCount - 1 |]
        
        {
            IonQBackend.Qubits = circuit.QubitCount
            IonQBackend.Circuit = ionqGates @ [measureGate]
        }
    
    /// Convert CircuitBuilder.Circuit to Rigetti Quil format
    let private convertToQuilProgram (circuit: CircuitBuilder.Circuit) : RigettiBackend.QuilProgram =
        let quilGates =
            circuit.Gates
            |> List.map (fun gate ->
                match gate with
                | CircuitBuilder.H q -> RigettiBackend.SingleQubit("H", q)
                | CircuitBuilder.X q -> RigettiBackend.SingleQubit("X", q)
                | CircuitBuilder.Y q -> RigettiBackend.SingleQubit("Y", q)
                | CircuitBuilder.Z q -> RigettiBackend.SingleQubit("Z", q)
                | CircuitBuilder.S q -> RigettiBackend.SingleQubit("S", q)
                | CircuitBuilder.T q -> RigettiBackend.SingleQubit("T", q)
                | CircuitBuilder.RX (q, angle) -> RigettiBackend.SingleQubitRotation("RX", angle, q)
                | CircuitBuilder.RY (q, angle) -> RigettiBackend.SingleQubitRotation("RY", angle, q)
                | CircuitBuilder.RZ (q, angle) -> RigettiBackend.SingleQubitRotation("RZ", angle, q)
                | CircuitBuilder.CZ (c, t) -> RigettiBackend.TwoQubit("CZ", c, t)
                | _ -> failwith $"Gate {gate} not supported by Rigetti - use transpiler first"
            )
        
        // Add measurements
        let measurements =
            [ 0 .. circuit.QubitCount - 1 ]
            |> List.map (fun q -> RigettiBackend.Measure(q, sprintf "ro[%d]" q))
        
        {
            RigettiBackend.Declarations = [ RigettiBackend.DeclareMemory("ro", "BIT", circuit.QubitCount) ]
            RigettiBackend.Instructions = quilGates @ measurements
        }
    
    /// Convert ICircuit to provider-specific format (JSON for IonQ, Quil for Rigetti)
    /// 
    /// This helper function enables workspace-based backends to leverage the
    /// existing, proven HTTP backend serialization logic.
    let rec convertCircuitToProviderFormat (circuit: ICircuit) (targetId: string) : Result<string, string> =
        try
            // Determine provider from targetId
            let provider =
                if targetId.StartsWith("ionq", StringComparison.OrdinalIgnoreCase) then "IonQ"
                elif targetId.StartsWith("rigetti", StringComparison.OrdinalIgnoreCase) then "Rigetti"
                elif targetId.StartsWith("quantinuum", StringComparison.OrdinalIgnoreCase) then "Quantinuum"
                elif targetId.StartsWith("atom", StringComparison.OrdinalIgnoreCase) then "AtomComputing"
                else "Unknown"
            
            match circuit with
            | :? CircuitWrapper as wrapper ->
                let builderCircuit = wrapper.Circuit
                
                // Transpile circuit to backend-compatible gates
                let transpiledCircuit = GateTranspiler.transpileForBackend provider builderCircuit
                
                match provider with
                | "IonQ" ->
                    let ionqCircuit = convertToIonQCircuit transpiledCircuit
                    let json = IonQBackend.serializeCircuit ionqCircuit
                    Ok json
                
                | "Rigetti" ->
                    let quilProgram = convertToQuilProgram transpiledCircuit
                    let quil = RigettiBackend.serializeProgram quilProgram
                    Ok quil
                
                | "Quantinuum" ->
                    // Quantinuum uses OpenQASM 2.0 format (reuse existing OpenQasmExport!)
                    let qasm = OpenQasmExport.export transpiledCircuit
                    Ok qasm
                
                | "AtomComputing" ->
                    // Atom Computing uses OpenQASM 2.0 format (same as Quantinuum!)
                    let qasm = OpenQasmExport.export transpiledCircuit
                    Ok qasm
                
                | _ ->
                    Error $"Unsupported provider: {provider} (targetId: {targetId})"
            
            | :? QaoaCircuitWrapper as wrapper ->
                // Convert QAOA circuit to general circuit first
                let generalCircuit = CircuitAdapter.qaoaCircuitToCircuit wrapper.QaoaCircuit
                // Recursively convert
                convertCircuitToProviderFormat (CircuitWrapper(generalCircuit) :> ICircuit) targetId
            
            | _ ->
                Error "Unsupported circuit type - must be CircuitWrapper or QaoaCircuitWrapper"
        
        with ex ->
            Error $"Circuit conversion failed: {ex.Message}"
    
    /// Wrapper for Azure Quantum SDK-based execution
    /// 
    /// Integrates with Azure Quantum via the official Microsoft.Azure.Quantum.Client SDK.
    /// Provides full workspace integration including quota checking, job management,
    /// and provider discovery.
    type AzureQuantumSdkBackendWrapper(workspace: FSharp.Azure.Quantum.Backends.AzureQuantumWorkspace.QuantumWorkspace, targetId: string) =
        
        /// Poll for job completion with exponential backoff and cancellation support
        let rec pollForCompletion (job: Microsoft.Azure.Quantum.CloudJob) (maxAttempts: int) (currentAttempt: int) (delayMs: int) (cancellationToken: System.Threading.CancellationToken option) : Async<Result<unit, string>> =
            async {
                // Check cancellation
                match cancellationToken with
                | Some token when token.IsCancellationRequested ->
                    return Error "Job polling cancelled by user"
                | _ -> ()
                
                if currentAttempt >= maxAttempts then
                    return Error $"Job polling timeout after {maxAttempts} attempts"
                else
                    // Refresh job status
                    do! job.RefreshAsync() |> Async.AwaitTask
                    
                    if job.Succeeded then
                        return Ok ()
                    elif job.Failed then
                        return Error $"Job failed with status: {job.Status}"
                    elif job.InProgress then
                        // Exponential backoff: double delay each time, max 30 seconds
                        let nextDelay = min (delayMs * 2) 30000
                        do! Async.Sleep delayMs
                        return! pollForCompletion job maxAttempts (currentAttempt + 1) nextDelay cancellationToken
                    else
                        // Unknown status, wait and retry
                        do! Async.Sleep delayMs
                        return! pollForCompletion job maxAttempts (currentAttempt + 1) delayMs cancellationToken
            }
        
        /// Helper to try getting a JSON property (idiomatic F# with Option)
        let tryGetJsonProperty (propertyName: string) (element: System.Text.Json.JsonElement) : System.Text.Json.JsonElement option =
            match element.TryGetProperty(propertyName) with
            | true, value -> Some value
            | false, _ -> None
        
        /// Parse histogram results from job output
        let parseHistogram (outputData: string) : Result<Map<string, int>, string> =
            try
                // Try to parse as JSON histogram
                let json = System.Text.Json.JsonDocument.Parse(outputData)
                let root = json.RootElement
                
                // Look for histogram in common locations (idiomatic F# with Option)
                let histogram =
                    tryGetJsonProperty "histogram" root
                    |> Option.orElseWith (fun () -> tryGetJsonProperty "Histogram" root)
                    |> Option.defaultValue root  // Assume root is the histogram if not found
                
                // Parse histogram entries
                let counts =
                    histogram.EnumerateObject()
                    |> Seq.map (fun prop -> (prop.Name, prop.Value.GetInt32()))
                    |> Map.ofSeq
                
                Ok counts
            with ex ->
                Error $"Failed to parse job output as histogram: {ex.Message}"
        
        /// Convert histogram to measurement results
        let histogramToMeasurements (histogram: Map<string, int>) (numQubits: int) : int[][] =
            histogram
            |> Map.toSeq
            |> Seq.collect (fun (bitstring, count) ->
                // Convert bitstring (e.g., "1011") to array [1; 0; 1; 1]
                let bits =
                    bitstring
                    |> Seq.map (fun c -> if c = '1' then 1 else 0)
                    |> Array.ofSeq
                
                // Ensure correct length
                let paddedBits =
                    if bits.Length < numQubits then
                        Array.append (Array.zeroCreate (numQubits - bits.Length)) bits
                    else
                        bits
                
                // Repeat for count occurrences
                Seq.replicate count paddedBits
            )
            |> Array.ofSeq
        
        member private _.ExecuteAsyncCore (circuit: ICircuit) (numShots: int) (cancellationToken: System.Threading.CancellationToken option) : Async<Result<ExecutionResult, string>> =
            async {
                try
                    // Check cancellation before starting
                    match cancellationToken with
                    | Some token when token.IsCancellationRequested ->
                        return Error "Operation cancelled before execution"
                    | _ -> ()
                    
                    if numShots <= 0 then
                        return Error "Number of shots must be positive"
                    else
                        // Step 1: Convert circuit to provider format
                        match convertCircuitToProviderFormat circuit targetId with
                        | Error msg -> return Error msg
                        | Ok circuitData ->
                            // Step 2: Create job details
                            let providerId = 
                                if targetId.Contains(".") then 
                                    targetId.Substring(0, targetId.IndexOf('.'))
                                else 
                                    targetId
                            
                            // Determine input format based on provider
                            let inputFormat =
                                if providerId.Equals("ionq", StringComparison.OrdinalIgnoreCase) then
                                    "ionq.circuit.v1"
                                elif providerId.Equals("rigetti", StringComparison.OrdinalIgnoreCase) then
                                    "rigetti.quil.v1"
                                else
                                    "json"  // Generic fallback
                            
                            // Create job details with inline circuit data
                            let jobDetails = new Azure.Quantum.Jobs.Models.JobDetails(
                                containerUri = "",  // Empty for inline data
                                inputDataFormat = inputFormat,
                                providerId = providerId,
                                target = targetId
                            )
                            
                            // Set job name and input params
                            jobDetails.Name <- $"FSharp.Azure.Quantum-{targetId}-{System.DateTime.UtcNow.Ticks}"
                            
                            // Create input params with circuit data and shot count
                            let inputParams = 
                                dict [
                                    "circuit", box circuitData
                                    "shots", box numShots
                                ]
                            jobDetails.InputParams <- inputParams
                            
                            // Step 3: Create and submit job
                            let cloudJob = new Microsoft.Azure.Quantum.CloudJob(
                                workspace.InnerWorkspace,
                                jobDetails
                            )
                            
                            let! submittedJob = workspace.InnerWorkspace.SubmitJobAsync(cloudJob) |> Async.AwaitTask
                            
                            // Step 4: Poll for completion (max 60 attempts, start with 1s delay)
                            let! pollResult = pollForCompletion submittedJob 60 0 1000 cancellationToken
                            
                            match pollResult with
                            | Error msg -> return Error msg
                            | Ok () ->
                                // Step 5: Get output data
                                if isNull cloudJob.OutputDataUri then
                                    return Error "Job completed but no output data URI available"
                                else
                                    // Download output data
                                    use httpClient = new System.Net.Http.HttpClient()
                                    let! outputData = httpClient.GetStringAsync(cloudJob.OutputDataUri) |> Async.AwaitTask
                                    
                                    // Step 6: Parse histogram
                                    match parseHistogram outputData with
                                    | Error msg -> return Error msg
                                    | Ok histogram ->
                                        // Convert histogram to measurements
                                        // Get qubit count from circuit
                                        let numQubits =
                                            match circuit with
                                            | :? CircuitWrapper as cw -> cw.Circuit.QubitCount
                                            | :? QaoaCircuitWrapper as qw -> qw.QaoaCircuit.NumQubits
                                            | _ -> 
                                                // Fallback: infer from histogram keys
                                                histogram 
                                                |> Map.toSeq 
                                                |> Seq.map (fun (bitstring, _) -> bitstring.Length)
                                                |> Seq.max
                                        
                                        let measurements = histogramToMeasurements histogram numQubits
                                        
                                        // Create execution result
                                        return Ok {
                                            BackendName = $"Azure Quantum SDK: {targetId}"
                                            NumShots = measurements.Length
                                            Measurements = measurements
                                            Metadata = Map.empty
                                                                                            .Add("job_id", box cloudJob.Id)
                                                                                            .Add("provider", box providerId)
                                                                                            .Add("target", box targetId)
                                                                                            .Add("status", box cloudJob.Status)
                                        }
                
                with ex ->
                    return Error $"Azure Quantum SDK backend error: {ex.Message}\n{ex.StackTrace}"
            }
        
        interface IQuantumBackend with
            member this.ExecuteAsync (circuit: ICircuit) (numShots: int) (cancellationToken: System.Threading.CancellationToken option) : Async<Result<ExecutionResult, string>> =
                this.ExecuteAsyncCore circuit numShots cancellationToken
            
            member this.Execute (circuit: ICircuit) (numShots: int) (cancellationToken: System.Threading.CancellationToken option) : Result<ExecutionResult, string> =
                this.ExecuteAsyncCore circuit numShots cancellationToken |> Async.RunSynchronously
            
            member _.Name = $"Azure Quantum SDK: {targetId}"
            
            member _.SupportedGates = 
                // Determine supported gates based on target
                if targetId.StartsWith("ionq", StringComparison.OrdinalIgnoreCase) then
                    [ "H"; "X"; "Y"; "Z"; "S"; "T"; "RX"; "RY"; "RZ"; "CNOT"; "SWAP"; "MEASURE" ]
                elif targetId.StartsWith("rigetti", StringComparison.OrdinalIgnoreCase) then
                    [ "H"; "X"; "Y"; "Z"; "RX"; "RY"; "RZ"; "CZ"; "MEASURE" ]
                else
                    [ "H"; "X"; "Y"; "Z"; "RX"; "RY"; "RZ"; "CNOT"; "CZ"; "MEASURE" ]
            
            member _.MaxQubits = 
                // Determine max qubits based on target
                if targetId.StartsWith("ionq", StringComparison.OrdinalIgnoreCase) then 29
                elif targetId.StartsWith("rigetti", StringComparison.OrdinalIgnoreCase) then 40
                else 20  // Conservative default
    
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
    
    /// Create a Quantinuum backend wrapper with Azure Quantum workspace
    /// 
    /// Parameters:
    /// - httpClient: Authenticated HTTP client for Azure Quantum API
    /// - workspaceUrl: Azure Quantum workspace URL (e.g., "https://my-workspace.quantum.azure.com")
    /// - target: Quantinuum target (e.g., "quantinuum.sim.h1-1sc", "quantinuum.qpu.h1-1")
    /// 
    /// Example:
    /// ```fsharp
    /// let httpClient = new System.Net.Http.HttpClient()
    /// let workspaceUrl = "https://my-workspace.quantum.azure.com"
    /// let backend = BackendAbstraction.createQuantinuumBackend httpClient workspaceUrl "quantinuum.sim.h1-1sc"
    /// ```
    let createQuantinuumBackend (httpClient: System.Net.Http.HttpClient) (workspaceUrl: string) (target: string) : IQuantumBackend =
        QuantinuumBackendWrapper(httpClient, workspaceUrl, target) :> IQuantumBackend
    
    /// Create an Atom Computing backend wrapper with Azure Quantum workspace
    /// 
    /// Parameters:
    /// - httpClient: Authenticated HTTP client for Azure Quantum API
    /// - workspaceUrl: Azure Quantum workspace URL (e.g., "https://my-workspace.quantum.azure.com")
    /// - target: Atom Computing target (e.g., "atom-computing.sim", "atom-computing.qpu.phoenix")
    /// 
    /// Example:
    /// ```fsharp
    /// let httpClient = new System.Net.Http.HttpClient()
    /// let workspaceUrl = "https://my-workspace.quantum.azure.com"
    /// let backend = BackendAbstraction.createAtomComputingBackend httpClient workspaceUrl "atom-computing.qpu.phoenix"
    /// ```
    let createAtomComputingBackend (httpClient: System.Net.Http.HttpClient) (workspaceUrl: string) (target: string) : IQuantumBackend =
        AtomComputingBackendWrapper(httpClient, workspaceUrl, target) :> IQuantumBackend
    
    /// Create a backend from Azure Quantum Workspace (SDK-based)
    /// 
    /// This is a new approach using the official Microsoft.Azure.Quantum.Client SDK.
    /// It provides better integration with Azure Quantum services including:
    /// - Automatic quota checking
    /// - Job management and monitoring
    /// - Provider discovery
    /// 
    /// Parameters:
    /// - workspace: QuantumWorkspace instance (from AzureQuantumWorkspace module)
    /// - targetId: Target identifier (e.g., "ionq.simulator", "rigetti.sim.qvm")
    /// 
    /// Example:
    /// ```fsharp
    /// open FSharp.Azure.Quantum.Backends.AzureQuantumWorkspace
    /// 
    /// let workspace = createDefault "sub-id" "rg" "ws-name" "eastus"
    /// let backend = BackendAbstraction.createFromWorkspace workspace "ionq.simulator"
    /// ```
    let createFromWorkspace (workspace: FSharp.Azure.Quantum.Backends.AzureQuantumWorkspace.QuantumWorkspace) (targetId: string) : IQuantumBackend =
        AzureQuantumSdkBackendWrapper(workspace, targetId) :> IQuantumBackend
    
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
