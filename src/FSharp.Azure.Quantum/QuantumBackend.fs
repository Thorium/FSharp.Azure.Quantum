namespace FSharp.Azure.Quantum.Core

open System

/// Unified Quantum Backend API
/// 
/// Provides a consistent interface for executing quantum circuits on both
/// local simulation and Azure Quantum backends.
/// 
/// The same QaoaCircuit type is used for both backends, making it trivial
/// to switch between local and remote execution.
module QuantumBackend =
    
    open FSharp.Azure.Quantum.Core.QaoaCircuit
    
    // ============================================================================
    // COMMON RESULT TYPES
    // ============================================================================
    
    /// Measurement counts from quantum execution
    /// Maps bitstring -> frequency
    type MeasurementCounts = Map<string, int>
    
    /// Quantum execution result (backend-agnostic)
    type ExecutionResult = {
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
    
    /// Backend type identifier
    [<Struct>]
    type BackendType =
        | Local
        | Azure of workspace: unit  // TODO: Replace 'unit' with actual WorkspaceConfig
    
    // ============================================================================
    // LOCAL SIMULATION BACKEND
    // ============================================================================
    
    module Local =
        open FSharp.Azure.Quantum.LocalSimulator
        
        /// Execute QAOA circuit on local simulator
        /// 
        /// Takes a QaoaCircuit (same type used for Azure) and executes it locally.
        /// Limited to circuits with ≤10 qubits.
        /// 
        /// Returns ExecutionResult with same format as Azure backend.
        let simulate (circuit: QaoaCircuit) (shots: int) : Result<ExecutionResult, string> =
            try
                if circuit.NumQubits > 10 then
                    Error $"Local simulator limited to 10 qubits, got {circuit.NumQubits}"
                elif shots < 1 then
                    Error $"Shots must be positive, got {shots}"
                else
                    let startTime = DateTime.UtcNow
                    
                    // Initialize state to uniform superposition
                    let mutable state = QaoaSimulator.initializeUniformSuperposition circuit.NumQubits
                    
                    // Extract cost terms from problem Hamiltonian
                    let extractCostInteractions (hamiltonian: ProblemHamiltonian) =
                        hamiltonian.Terms
                        |> Array.choose (fun term ->
                            // Look for two-qubit ZZ terms (interactions)
                            if term.QubitsIndices.Length = 2 && 
                               term.PauliOperators.Length = 2 &&
                               term.PauliOperators.[0] = PauliZ &&
                               term.PauliOperators.[1] = PauliZ then
                                Some (term.QubitsIndices.[0], term.QubitsIndices.[1], term.Coefficient)
                            else
                                None
                        )
                    
                    let costInteractions = extractCostInteractions circuit.ProblemHamiltonian
                    
                    // Apply each QAOA layer
                    for layer in circuit.Layers do
                        let gamma = layer.Gamma
                        let beta = layer.Beta
                        
                        // Cost layer: Apply interactions
                        for (q1, q2, coeff) in costInteractions do
                            state <- QaoaSimulator.applyCostInteraction gamma q1 q2 coeff state
                        
                        // Mixer layer
                        state <- QaoaSimulator.applyMixerLayer beta state
                    
                    // Measure
                    let rng = Random()
                    let rawCounts = Measurement.sampleAndCount rng shots state
                    
                    // Convert basis indices to bitstrings
                    let counts = 
                        rawCounts
                        |> Map.toSeq
                        |> Seq.map (fun (basisIndex, count) ->
                            let bitstring = 
                                Convert.ToString(basisIndex, 2)
                                       .PadLeft(circuit.NumQubits, '0')
                            (bitstring, count))
                        |> Map.ofSeq
                    
                    let elapsedMs = (DateTime.UtcNow - startTime).TotalMilliseconds
                    
                    Ok {
                        Counts = counts
                        Shots = shots
                        Backend = "Local"
                        ExecutionTimeMs = elapsedMs
                        JobId = None
                    }
            with
            | ex -> Error $"Local simulation failed: {ex.Message}"
    
    // ============================================================================
    // AZURE QUANTUM BACKEND (PLACEHOLDER)
    // ============================================================================
    
    module Azure =
        
        /// Execute QAOA circuit on Azure Quantum (not yet implemented)
        /// 
        /// Takes the same QaoaCircuit type as local simulator.
        /// Will submit to Azure Quantum service and wait for results.
        let execute (circuit: QaoaCircuit) (shots: int) (workspace: unit) : Result<ExecutionResult, string> =
            Error "Azure Quantum backend not yet implemented. Use QuantumBackend.Local.simulate for now."
            
            // TODO: Future implementation
            // 1. Convert QaoaCircuit to Azure Quantum job format
            // 2. Submit job to workspace
            // 3. Poll for completion
            // 4. Parse results into ExecutionResult format
            // 
            // Example structure:
            // try
            //     let jobSubmission = {
            //         JobId = Guid.NewGuid().ToString()
            //         Target = "ionq.simulator"  // or other backend
            //         InputData = serializeCircuit circuit
            //         InputParams = Map [ ("shots", shots :> obj) ]
            //         // ... other fields
            //     }
            //     
            //     let jobId = AzureClient.submitJob jobSubmission workspace
            //     let result = AzureClient.waitForCompletion jobId workspace
            //     
            //     Ok {
            //         Counts = result.Counts
            //         Shots = shots
            //         Backend = "Azure"
            //         ExecutionTimeMs = result.ExecutionTime.TotalMilliseconds
            //         JobId = Some jobId
            //     }
            // with
            // | ex -> Error $"Azure execution failed: {ex.Message}"
    
    // ============================================================================
    // UNIFIED BACKEND INTERFACE
    // ============================================================================
    
    /// Unified backend interface
    type IBackend =
        abstract member Execute: QaoaCircuit -> int -> Result<ExecutionResult, string>
    
    /// Local backend implementation
    type LocalBackend() =
        interface IBackend with
            member _.Execute circuit shots = Local.simulate circuit shots
    
    /// Azure backend implementation (placeholder)
    type AzureBackend(workspace: unit) =
        interface IBackend with
            member _.Execute circuit shots = Azure.execute circuit shots workspace
    
    // ============================================================================
    // CONVENIENCE FUNCTIONS
    // ============================================================================
    
    /// Execute circuit on specified backend type
    let execute (backendType: BackendType) (circuit: QaoaCircuit) (shots: int) : Result<ExecutionResult, string> =
        match backendType with
        | Local -> Local.simulate circuit shots
        | Azure workspace -> Azure.execute circuit shots workspace
    
    /// Auto-select backend based on circuit size
    /// 
    /// - ≤10 qubits: Use local simulator (fast, free)
    /// - >10 qubits: Require Azure (not yet available)
    let autoExecute (circuit: QaoaCircuit) (shots: int) : Result<ExecutionResult, string> =
        if circuit.NumQubits <= 10 then
            printfn "Auto-selecting local simulator (circuit size: %d qubits)" circuit.NumQubits
            Local.simulate circuit shots
        else
            Error $"Circuit requires Azure Quantum ({circuit.NumQubits} qubits > 10 qubit local limit). Azure backend not yet implemented."
