namespace FSharp.Azure.Quantum.Core
open FSharp.Azure.Quantum.Core

open System
open System.IO
open System.Text.Json
open FSharp.Azure.Quantum.Core.Types

/// Atom Computing Backend Integration
/// 
/// Low-level module for Atom Computing Phoenix integration with Azure Quantum.
/// Handles job submission, result parsing, and error mapping.
/// 
/// Atom Computing Hardware Specs:
/// - 100+ qubits (Phoenix system)
/// - All-to-all connectivity (movable neutral atoms!)
/// - Gate-based quantum computing (vs. analog simulation)
/// - Native gates: RX, RY, RZ, CZ (Rydberg blockade)
/// 
/// Key Design Decisions:
/// - Accepts OpenQASM 2.0 strings (conversion happens in BackendAbstraction)
/// - Follows Quantinuum/IonQ pattern for Azure Quantum integration
/// - Result format: {"results": {"00": 48, "11": 52}} (assumed)
/// 
/// Note: This module does NOT depend on CircuitBuilder - it only works with
/// OpenQASM strings and Azure Quantum types. Circuit conversion happens in
/// BackendAbstraction.fs (compiled later).
module AtomComputingBackend =
    
    // ============================================================================
    // JOB SUBMISSION
    // ============================================================================
    
    /// Create a JobSubmission for an Atom Computing circuit (from OpenQASM string)
    /// 
    /// Parameters:
    /// - qasmCode: OpenQASM 2.0 string (already transpiled and validated)
    /// - shots: Number of measurement shots
    /// - target: Atom Computing backend target (e.g., "atom-computing.sim", "atom-computing.qpu.phoenix")
    /// 
    /// Returns: JobSubmission ready for submitJobAsync
    /// 
    /// Example qasmCode:
    /// ```
    /// OPENQASM 2.0;
    /// include "qelib1.inc";
    /// qreg q[2];
    /// h q[0];
    /// cz q[0],q[1];
    /// ```
    let createJobSubmission (qasmCode: string) (shots: int) (target: string) : JobSubmission =
        let jobId = Guid.NewGuid().ToString()
        
        {
            JobId = jobId
            Target = target
            Name = Some (sprintf "AtomComputing-%s" target)
            InputData = qasmCode :> obj
            InputDataFormat = CircuitFormat.Custom "qasm.v2"  // OpenQASM 2.0
            InputParams = Map [ ("shots", shots :> obj) ]
            Tags = Map.empty
        }
    
    // ============================================================================
    // RESULT PARSING
    // ============================================================================
    
    /// Parse Atom Computing result JSON into measurement counts histogram
    /// 
    /// Atom Computing returns results as (assumed format, similar to Quantinuum):
    /// {
    ///   "results": {
    ///     "00": 48,
    ///     "11": 52
    ///   }
    /// }
    /// 
    /// Alternative format (if different):
    /// {
    ///   "measurements": {
    ///     "00": 48,
    ///     "11": 52
    ///   }
    /// }
    /// 
    /// Returns: Map<bitstring, count>
    let parseAtomComputingResult (jsonResult: string) : Map<string, int> =
        use jsonDoc = JsonDocument.Parse(jsonResult)
        let root = jsonDoc.RootElement
        
        // Try both possible property names
        let results = 
            match root.TryGetProperty("results") with
            | (true, element) -> element
            | (false, _) ->
                match root.TryGetProperty("measurements") with
                | (true, element) -> element
                | (false, _) -> 
                    // Fallback: assume root is the histogram itself
                    root
        
        results.EnumerateObject()
        |> Seq.map (fun prop -> (prop.Name, prop.Value.GetInt32()))
        |> Map.ofSeq
    
    // ============================================================================
    // ERROR MAPPING
    // ============================================================================
    
    /// Map Atom Computing-specific error codes to QuantumError types
    /// 
    /// Atom Computing Error Codes (assumed, based on standard patterns):
    /// - InvalidCircuit: Unsupported gate or malformed circuit
    /// - TooManyQubits: Circuit exceeds qubit limit (100+ for Phoenix)
    /// - QuotaExceeded: Insufficient credits
    /// - BackendUnavailable: Hardware offline or in maintenance
    /// - InvalidTopology: Attempted invalid qubit connectivity (unlikely with all-to-all)
    /// 
    /// Parameters:
    /// - errorCode: Atom Computing error code string
    /// - errorMessage: Atom Computing error message
    /// 
    /// Returns: Mapped QuantumError
    let mapAtomComputingError (errorCode: string) (errorMessage: string) : QuantumError =
        match errorCode with
        | "InvalidCircuit" ->
            QuantumError.ValidationError("circuit", errorMessage)
        
        | "TooManyQubits" ->
            // TooManyQubits is a circuit validation error
            QuantumError.ValidationError("circuit", sprintf "Circuit too large: %s" errorMessage)
        
        | "QuotaExceeded" ->
            QuantumError.AzureError (AzureQuantumError.QuotaExceeded errorMessage)
        
        | "BackendUnavailable" ->
            // Suggest retry after 5 minutes for maintenance
            QuantumError.AzureError (AzureQuantumError.ServiceUnavailable (Some (TimeSpan.FromMinutes(5.0))))
        
        | "InvalidTopology" ->
            // Shouldn't happen with all-to-all connectivity, but handle gracefully
            QuantumError.ValidationError("circuit", sprintf "Connectivity error: %s" errorMessage)
        
        | _ ->
            // Unknown Atom Computing error
            QuantumError.AzureError (AzureQuantumError.UnknownError(0, sprintf "Atom Computing error: %s - %s" errorCode errorMessage))
    
    // ============================================================================
    // CONVENIENCE FUNCTIONS
    // ============================================================================
    
    /// Submit an Atom Computing circuit (from OpenQASM string) and wait for results
    /// 
    /// This is a high-level convenience function that combines:
    /// 1. Job submission via JobLifecycle.submitJobAsync
    /// 2. Status polling via JobLifecycle.pollJobUntilCompleteAsync
    /// 3. Result retrieval via JobLifecycle.getJobResultAsync
    /// 4. Result parsing from Atom Computing histogram format
    /// 
    /// Parameters:
    /// - httpClient: HTTP client for API requests
    /// - workspaceUrl: Azure Quantum workspace URL
    /// - qasmCode: OpenQASM 2.0 string (already transpiled)
    /// - shots: Number of measurement shots
    /// - target: Atom Computing backend (e.g., "atom-computing.sim", "atom-computing.qpu.phoenix")
    /// 
    /// Returns: Async<Result<Map<string, int>, QuantumError>>
    ///   - Ok: Measurement histogram (bitstring -> count)
    ///   - Error: QuantumError with details
    let submitAndWaitForResultsAsync
        (httpClient: System.Net.Http.HttpClient)
        (workspaceUrl: string)
        (qasmCode: string)
        (shots: int)
        (target: string)
        : Async<Result<Map<string, int>, QuantumError>> =
        async {
            // Step 1: Create job submission
            let submission = createJobSubmission qasmCode shots target
            
            // Step 2: Submit job
            let! submitResult = JobLifecycle.submitJobAsync httpClient workspaceUrl submission
            match submitResult with
            | Error err -> return Error err
            | Ok jobId ->
                // Step 3: Poll until complete (10 minute timeout for 100+ qubit circuits)
                let timeout = TimeSpan.FromMinutes(10.0)
                let cancellationToken = System.Threading.CancellationToken.None
                let! pollResult = JobLifecycle.pollJobUntilCompleteAsync httpClient workspaceUrl jobId timeout cancellationToken
                match pollResult with
                | Error err -> return Error err
                | Ok job ->
                    // Check job status
                    match job.Status with
                    | JobStatus.Succeeded ->
                        // Step 4: Get results from blob storage
                        match job.OutputDataUri with
                        | None ->
                            return Error (QuantumError.AzureError (AzureQuantumError.UnknownError(500, "Job completed but no output URI available")))
                        | Some uri ->
                            let! resultData = JobLifecycle.getJobResultAsync httpClient uri
                            match resultData with
                            | Error err -> return Error err
                            | Ok jobResult ->
                                // Step 5: Parse histogram from OutputData
                                try
                                    let resultJson = jobResult.OutputData :?> string
                                    let histogram = parseAtomComputingResult resultJson
                                    return Ok histogram
                                with
                                | ex -> return Error (QuantumError.AzureError (AzureQuantumError.UnknownError(0, sprintf "Failed to parse Atom Computing results: %s" ex.Message)))
                    
                    | JobStatus.Failed (errorCode, errorMessage) ->
                        // Map Atom Computing error to QuantumError
                        return Error (mapAtomComputingError errorCode errorMessage)
                    
                    | JobStatus.Cancelled ->
                        return Error (QuantumError.OperationError("Job execution", "Operation cancelled"))
                    
                    | _ ->
                        return Error (QuantumError.AzureError (AzureQuantumError.UnknownError(0, sprintf "Unexpected job status: %A" job.Status)))
        }
