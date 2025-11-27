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
    
    /// Backend type identifier (v1.0: Local only, Azure coming in v2.0)
    [<Struct>]
    type BackendType =
        | Local
    
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
    // AZURE QUANTUM BACKEND (INTERNAL - Coming in v2.0)
    // ============================================================================
    
    module internal Azure =
        
        /// Internal Azure backend workspace configuration (v2.0)
        type internal AzureWorkspace = {
            SubscriptionId: string
            ResourceGroup: string
            WorkspaceName: string
            Region: string
        }
        
        /// Execute QAOA circuit on Azure Quantum (not yet implemented - v2.0 feature)
        /// 
        /// Takes the same QaoaCircuit type as local simulator.
        /// Will submit to Azure Quantum service and wait for results.
        let internal execute (circuit: QaoaCircuit) (shots: int) (workspace: AzureWorkspace) : Result<ExecutionResult, string> =
            Error "Azure Quantum backend planned for v2.0. Use QuantumBackend.Local.simulate for now."
            
            // TODO: v2.0 implementation
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
    
    /// Azure backend implementation (internal - v2.0)
    type internal AzureBackend(workspace: Azure.AzureWorkspace) =
        interface IBackend with
            member _.Execute circuit shots = Azure.execute circuit shots workspace
    
    // ============================================================================
    // CONVENIENCE FUNCTIONS
    // ============================================================================
    
    /// Execute circuit on specified backend type (v1.0: Local only)
    let execute (backendType: BackendType) (circuit: QaoaCircuit) (shots: int) : Result<ExecutionResult, string> =
        match backendType with
        | Local -> Local.simulate circuit shots
    
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
    
    // Helper function to sequence a list of Results
    // Returns Error on first error, or Ok with list of successes
    let private sequenceResults (results: Result<'a, string> list) : Result<'a list, string> =
        results
        |> List.fold (fun acc result ->
            match acc, result with
            | Ok values, Ok value -> Ok (value :: values)
            | Error msg, _ -> Error msg
            | _, Error msg -> Error msg
        ) (Ok [])
        |> Result.map List.rev
    
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
    let executeBatch 
        (backendType: BackendType) 
        (circuits: QaoaCircuit list) 
        (shots: int) 
        (config: Batching.BatchConfig)
        : Async<Result<ExecutionResult list, string>> =
        async {
            if circuits.IsEmpty then
                return Ok []
            elif not config.Enabled then
                // Batching disabled - execute circuits individually using functional approach
                let results = 
                    circuits 
                    |> List.map (fun circuit -> execute backendType circuit shots)
                
                return sequenceResults results
            else
                // Batching enabled - use batchCircuitsAsync with functional submission
                let submitBatch (batch: QaoaCircuit list) : Async<ExecutionResult list> =
                    async {
                        let results = 
                            batch 
                            |> List.map (fun circuit -> execute backendType circuit shots)
                        
                        // For batching, we need to return successful results
                        // Error handling moved to outer level
                        match sequenceResults results with
                        | Ok successResults -> return successResults
                        | Error msg -> 
                            // Re-throw as exception to be caught at outer level
                            return failwith msg
                    }
                
                try
                    // Use functional batching
                    let! allResults = Batching.batchCircuitsAsync config circuits submitBatch
                    return Ok allResults
                with
                | ex -> return Error ex.Message
        }
