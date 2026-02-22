namespace FSharp.Azure.Quantum.Core
open FSharp.Azure.Quantum.Core

open System
open System.IO
open System.Text.Json
open System.Threading.Tasks
open FSharp.Azure.Quantum.Core.Types

/// Quantinuum Backend Integration
/// 
/// Low-level module for Quantinuum H-Series integration with Azure Quantum.
/// Handles job submission, result parsing, and error mapping.
/// 
/// Quantinuum Hardware Specs:
/// - 32 qubits (H1-1SC simulator, H1-1 hardware)
/// - All-to-all connectivity (no SWAP routing needed!)
/// - 99.9%+ gate fidelity
/// - Native gates: H, X, Y, Z, S, T, RX, RY, RZ, CZ (two-qubit)
/// 
/// Key Design Decisions:
/// - Accepts OpenQASM 2.0 strings (conversion happens in BackendAbstraction)
/// - Follows IonQBackend pattern for Azure Quantum integration
/// - Result format: {"results": {"00": 48, "11": 52}}
/// 
/// Note: This module does NOT depend on CircuitBuilder - it only works with
/// OpenQASM strings and Azure Quantum types. Circuit conversion happens in
/// BackendAbstraction.fs (compiled later).
module QuantinuumBackend =
    
    // ============================================================================
    // JOB SUBMISSION
    // ============================================================================
    
    /// Create a JobSubmission for a Quantinuum circuit (from OpenQASM string)
    /// 
    /// Parameters:
    /// - qasmCode: OpenQASM 2.0 string (already transpiled and validated)
    /// - shots: Number of measurement shots
    /// - target: Quantinuum backend target (e.g., "quantinuum.sim.h1-1sc", "quantinuum.qpu.h1-1")
    /// 
    /// Returns: JobSubmission ready for submitJobAsync
    /// 
    /// Example qasmCode:
    /// ```
    /// OPENQASM 2.0;
    /// include "qelib1.inc";
    /// qreg q[2];
    /// h q[0];
    /// cx q[0],q[1];
    /// ```
    let createJobSubmission (qasmCode: string) (shots: int) (target: string) : JobSubmission =
        let jobId = Guid.NewGuid().ToString()
        
        {
            JobId = jobId
            Target = target
            Name = Some (sprintf "Quantinuum-%s" target)
            InputData = qasmCode :> obj
            InputDataFormat = CircuitFormat.Custom "qasm.v2"  // OpenQASM 2.0
            InputParams = Map [ ("shots", shots :> obj) ]
            Tags = Map.empty
        }
    
    // ============================================================================
    // RESULT PARSING
    // ============================================================================
    
    /// Parse Quantinuum result JSON into measurement counts histogram
    /// 
    /// Quantinuum returns results as:
    /// {
    ///   "results": {
    ///     "00": 48,
    ///     "11": 52
    ///   }
    /// }
    /// 
    /// Returns: Map<bitstring, count>
    let parseQuantinuumResult (jsonResult: string) : Map<string, int> =
        use jsonDoc = JsonDocument.Parse(jsonResult)
        let root = jsonDoc.RootElement
        let results = root.GetProperty("results")
        
        results.EnumerateObject()
        |> Seq.map (fun prop -> (prop.Name, prop.Value.GetInt32()))
        |> Map.ofSeq
    
    // ============================================================================
    // ERROR MAPPING
    // ============================================================================
    
    /// Map Quantinuum-specific error codes to QuantumError types
    /// 
    /// Quantinuum Error Codes:
    /// - InvalidCircuit: Unsupported gate or malformed circuit
    /// - TooManyQubits: Circuit exceeds qubit limit (32 for H1-1SC)
    /// - QuotaExceeded: Insufficient HQC (Honeywell Quantum Credits)
    /// - BackendUnavailable: Hardware offline or in maintenance
    /// 
    /// Parameters:
    /// - errorCode: Quantinuum error code string
    /// - errorMessage: Quantinuum error message
    /// 
    /// Returns: Mapped QuantumError
    let mapQuantinuumError (errorCode: string) (errorMessage: string) : QuantumError =
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
        
        | _ ->
            // Unknown Quantinuum error
            QuantumError.AzureError (AzureQuantumError.UnknownError(0, sprintf "Quantinuum error: %s - %s" errorCode errorMessage))
    
    // ============================================================================
    // CONVENIENCE FUNCTIONS
    // ============================================================================
    
    /// Submit a Quantinuum circuit (from OpenQASM string) and wait for results
    /// 
    /// This is a high-level convenience function that combines:
    /// 1. Job submission via JobLifecycle.submitJobAsync
    /// 2. Status polling via JobLifecycle.pollJobUntilCompleteAsync
    /// 3. Result retrieval via JobLifecycle.getJobResultAsync
    /// 4. Result parsing from Quantinuum histogram format
    /// 
    /// Parameters:
    /// - httpClient: HTTP client for API requests
    /// - workspaceUrl: Azure Quantum workspace URL
    /// - qasmCode: OpenQASM 2.0 string (already transpiled)
    /// - shots: Number of measurement shots
    /// - target: Quantinuum backend (e.g., "quantinuum.sim.h1-1sc", "quantinuum.qpu.h1-1")
    /// 
    /// Returns: Task<Result<Map<string, int>, QuantumError>>
    ///   - Ok: Measurement histogram (bitstring -> count)
    ///   - Error: QuantumError with details
    let submitAndWaitForResultsAsync
        (httpClient: System.Net.Http.HttpClient)
        (workspaceUrl: string)
        (qasmCode: string)
        (shots: int)
        (target: string)
        : Task<Result<Map<string, int>, QuantumError>> =
        task {
            // Step 1: Create job submission
            let submission = createJobSubmission qasmCode shots target
            
            // Step 2: Submit job
            let! submitResult = JobLifecycle.submitJobAsync httpClient workspaceUrl submission
            match submitResult with
            | Error err -> return Error err
            | Ok jobId ->
                // Step 3: Poll until complete (5 minute timeout, no cancellation)
                let timeout = TimeSpan.FromMinutes(5.0)
                let cancellationToken = System.Threading.CancellationToken.None
                let! pollResult = JobLifecycle.pollJobUntilCompleteAsync httpClient workspaceUrl jobId timeout cancellationToken
                match pollResult with
                | Error err -> return Error err
                | Ok (job: QuantumJob) ->
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
                                    let histogram = parseQuantinuumResult resultJson
                                    return Ok histogram
                                with
                                | ex -> return Error (QuantumError.AzureError (AzureQuantumError.UnknownError(0, sprintf "Failed to parse Quantinuum results: %s" ex.Message)))
                    
                    | JobStatus.Failed (errorCode, errorMessage) ->
                        // Map Quantinuum error to QuantumError
                        return Error (mapQuantinuumError errorCode errorMessage)
                    
                    | JobStatus.Cancelled ->
                        return Error (QuantumError.OperationError("Job execution", "Operation cancelled"))
                    
                    | _ ->
                        return Error (QuantumError.AzureError (AzureQuantumError.UnknownError(0, sprintf "Unexpected job status: %A" job.Status)))
        }
