namespace FSharp.Azure.Quantum.Core
open FSharp.Azure.Quantum.Core

open System
open System.IO
open System.Text.Json
open System.Threading.Tasks
open FSharp.Azure.Quantum.Core.Types

/// IonQ Backend Integration
/// 
/// Implements circuit serialization to IonQ format, job submission to ionq.simulator,
/// result parsing, and error mapping.
module IonQBackend =
    
    // ============================================================================
    // TYPE DEFINITIONS
    // ============================================================================
    
    /// IonQ gate representation using discriminated unions to avoid JSON null issues
    /// 
    /// Design rationale: Using DUs instead of records with optional fields prevents
    /// JSON serialization null complications. Each gate variant contains exactly
    /// the fields it needs, with no nulls.
    type IonQGate =
        /// Single-qubit gate (H, X, Y, Z, S, T)
        | SingleQubit of gate: string * target: int
        
        /// Single-qubit rotation gate (RX, RY, RZ)
        | SingleQubitRotation of gate: string * target: int * rotation: float
        
        /// Two-qubit gate (CNOT, MS)
        | TwoQubit of gate: string * control: int * target: int
        
        /// Measurement operation
        | Measure of targets: int[]
    
    /// IonQ circuit format (JSON schema)
    /// 
    /// Example JSON:
    /// {
    ///   "qubits": 3,
    ///   "circuit": [
    ///     { "gate": "h", "target": 0 },
    ///     { "gate": "cnot", "control": 0, "target": 1 }
    ///   ]
    /// }
    type IonQCircuit = {
        /// Number of qubits
        Qubits: int
        
        /// Sequence of gates
        Circuit: IonQGate list
    }
    
    // ============================================================================
    // GATE SERIALIZATION
    // ============================================================================
    
    /// Serialize a single IonQ gate to JSON string
    /// 
    /// Uses manual JSON construction to avoid null serialization issues.
    /// Each gate variant produces exactly the JSON fields it needs.
    /// 
    /// Examples:
    /// - SingleQubit("h", 0) → {"gate": "h", "target": 0}
    /// - TwoQubit("cnot", 0, 1) → {"gate": "cnot", "control": 0, "target": 1}
    /// - SingleQubitRotation("rx", 2, 1.57) → {"gate": "rx", "target": 2, "rotation": 1.57}
    /// - Measure([|0;1;2|]) → {"gate": "measure", "target": [0, 1, 2]}
    let serializeGate (gate: IonQGate) : string =
        use stream = new System.IO.MemoryStream()
        use writer = new Utf8JsonWriter(stream)
        
        writer.WriteStartObject()
        
        match gate with
        | SingleQubit(gateName, target) ->
            writer.WriteString("gate", gateName)
            writer.WriteNumber("target", target)
        
        | SingleQubitRotation(gateName, target, rotation) ->
            writer.WriteString("gate", gateName)
            writer.WriteNumber("target", target)
            writer.WriteNumber("rotation", rotation)
        
        | TwoQubit(gateName, control, target) ->
            writer.WriteString("gate", gateName)
            writer.WriteNumber("control", control)
            writer.WriteNumber("target", target)
        
        | Measure(targets) ->
            writer.WriteString("gate", "measure")
            writer.WriteStartArray("target")
            for t in targets do
                writer.WriteNumberValue(t)
            writer.WriteEndArray()
        
        writer.WriteEndObject()
        writer.Flush()
        
        System.Text.Encoding.UTF8.GetString(stream.ToArray())
    
    // ============================================================================
    // CIRCUIT SERIALIZATION
    // ============================================================================
    
    /// Serialize an IonQ circuit to JSON string
    /// 
    /// Produces JSON in IonQ format:
    /// {
    ///   "qubits": 2,
    ///   "circuit": [
    ///     {"gate": "h", "target": 0},
    ///     {"gate": "cnot", "control": 0, "target": 1},
    ///     {"gate": "measure", "target": [0, 1]}
    ///   ]
    /// }
    let serializeCircuit (circuit: IonQCircuit) : string =
        use stream = new MemoryStream()
        use writer = new Utf8JsonWriter(stream, JsonWriterOptions(Indented = false))
        
        writer.WriteStartObject()
        writer.WriteNumber("qubits", circuit.Qubits)
        
        // Serialize circuit array
        writer.WriteStartArray("circuit")
        for gate in circuit.Circuit do
            // Parse each gate's JSON and write to array
            let gateJson = serializeGate gate
            use gateDoc = JsonDocument.Parse(gateJson)
            gateDoc.RootElement.WriteTo(writer)
        writer.WriteEndArray()
        
        writer.WriteEndObject()
        writer.Flush()
        
        System.Text.Encoding.UTF8.GetString(stream.ToArray())
    
    // ============================================================================
    // CIRCUIT VALIDATION (Pre-Flight Checks)
    // ============================================================================
    
    /// Extract circuit information for validation
    /// Converts IonQCircuit to CircuitValidator.Circuit format
    let private extractCircuitInfo (circuit: IonQCircuit) : CircuitValidator.CircuitStats =
        let usedGates = 
            circuit.Circuit
            |> List.map (fun gate ->
                match gate with
                | SingleQubit(gateName, _) -> gateName.ToUpperInvariant()
                | SingleQubitRotation(gateName, _, _) -> gateName.ToUpperInvariant()
                | TwoQubit(gateName, _, _) -> gateName.ToUpperInvariant()
                | Measure _ -> "MEASURE")
            |> Set.ofList
        
        let twoQubitGates =
            circuit.Circuit
            |> List.choose (fun gate ->
                match gate with
                | TwoQubit(_, control, target) -> Some (control, target)
                | _ -> None)
        
        {
            CircuitValidator.NumQubits = circuit.Qubits
            CircuitValidator.GateCount = circuit.Circuit.Length
            CircuitValidator.Depth = None
            CircuitValidator.UsedGates = usedGates
            CircuitValidator.TwoQubitGates = twoQubitGates
        }
    
    // ============================================================================
    // JOB SUBMISSION
    // ============================================================================
    
    /// Create a JobSubmission for an IonQ circuit
    /// 
    /// Parameters:
    /// - circuit: IonQ circuit to submit
    /// - shots: Number of measurement shots
    /// - target: IonQ backend target (e.g., "ionq.simulator", "ionq.qpu.aria-1")
    /// 
    /// Returns: JobSubmission ready for submitJobAsync
    let createJobSubmission (circuit: IonQCircuit) (shots: int) (target: string) : JobSubmission =
        let jobId = Guid.NewGuid().ToString()
        let circuitJson = serializeCircuit circuit
        
        {
            JobId = jobId
            Target = target
            Name = Some (sprintf "IonQ-%s" target)
            InputData = circuitJson :> obj
            InputDataFormat = CircuitFormat.IonQ_V1
            InputParams = Map [ ("shots", shots :> obj) ]
            Tags = Map.empty
        }
    
    // ============================================================================
    // RESULT PARSING
    // ============================================================================
    
    /// Parse IonQ result JSON into measurement counts histogram
    /// 
    /// IonQ returns results as:
    /// {
    ///   "histogram": {
    ///     "00": 492,
    ///     "01": 12,
    ///     "10": 8,
    ///     "11": 488
    ///   }
    /// }
    /// 
    /// Returns: Map<bitstring, count>
    let parseIonQResult (jsonResult: string) : Map<string, int> =
        use jsonDoc = JsonDocument.Parse(jsonResult)
        let root = jsonDoc.RootElement
        let histogram = root.GetProperty("histogram")
        
        histogram.EnumerateObject()
        |> Seq.map (fun prop -> (prop.Name, prop.Value.GetInt32()))
        |> Map.ofSeq
    
    // ============================================================================
    // ERROR MAPPING
    // ============================================================================
    
    /// Map IonQ-specific error codes to QuantumError types
    /// 
    /// IonQ Error Codes:
    /// - InvalidCircuit: Unsupported gate or malformed circuit
    /// - TooManyQubits: Circuit exceeds qubit limit (29 for simulator, varies for hardware)
    /// - QuotaExceeded: Insufficient credits
    /// - BackendUnavailable: Hardware offline
    /// 
    /// Parameters:
    /// - errorCode: IonQ error code string
    /// - errorMessage: IonQ error message
    /// 
    /// Returns: Mapped QuantumError
    let mapIonQError (errorCode: string) (errorMessage: string) : QuantumError =
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
            // Unknown IonQ error
            QuantumError.AzureError (AzureQuantumError.UnknownError(0, sprintf "IonQ error: %s - %s" errorCode errorMessage))
    
    // ============================================================================
    // CONVENIENCE FUNCTIONS
    // ============================================================================
    
    /// Submit an IonQ circuit and wait for results
    /// 
    /// This is a high-level convenience function that combines:
    /// 1. Circuit serialization
    /// 2. Job submission via JobLifecycle.submitJobAsync
    /// 3. Status polling via JobLifecycle.pollJobUntilCompleteAsync
    /// 4. Result retrieval via JobLifecycle.getJobResultAsync
    /// 5. Result parsing from IonQ histogram format
    /// 
    /// Parameters:
    /// - httpClient: HTTP client for API requests
    /// - workspaceUrl: Azure Quantum workspace URL
    /// - circuit: IonQ circuit to execute
    /// - shots: Number of measurement shots
    /// - target: IonQ backend (e.g., "ionq.simulator", "ionq.qpu.aria-1")
    /// 
    /// Returns: Task<Result<Map<string, int>, QuantumError>>
    ///   - Ok: Measurement histogram (bitstring -> count)
    ///   - Error: QuantumError with details
    let submitAndWaitForResultsAsync
        (httpClient: System.Net.Http.HttpClient)
        (workspaceUrl: string)
        (circuit: IonQCircuit)
        (shots: int)
        (target: string)
        : Task<Result<Map<string, int>, QuantumError>> =
        task {
            // Step 1: Create job submission
            let submission = createJobSubmission circuit shots target
            
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
                                    let histogram = parseIonQResult resultJson
                                    return Ok histogram
                                with
                                | ex -> return Error (QuantumError.AzureError (AzureQuantumError.UnknownError(0, sprintf "Failed to parse IonQ results: %s" ex.Message)))
                    
                    | JobStatus.Failed (errorCode, errorMessage) ->
                        // Map IonQ error to QuantumError
                        return Error (mapIonQError errorCode errorMessage)
                    
                    | JobStatus.Cancelled ->
                        return Error (QuantumError.OperationError("Job execution", "Operation cancelled"))
                    
                    | _ ->
                        return Error (QuantumError.AzureError (AzureQuantumError.UnknownError(0, sprintf "Unexpected job status: %A" job.Status)))
        }
    
    /// Submit IonQ circuit with pre-flight validation
    /// 
    /// This function validates the circuit against backend constraints BEFORE submission,
    /// preventing costly failed Azure API calls.
    /// 
    /// Parameters:
    /// - httpClient: Authenticated HttpClient
    /// - workspaceUrl: Azure Quantum workspace URL
    /// - circuit: IonQ circuit to submit
    /// - shots: Number of measurement shots
    /// - target: IonQ backend target (e.g., "ionq.simulator", "ionq.qpu.aria-1")
    /// - constraints: Optional backend constraints (auto-detected from target if None)
    /// 
    /// Returns: Task<Result<Map<string, int>, QuantumError>>
    ///   - Ok: Measurement histogram (bitstring -> count)
    ///   - Error: QuantumError with validation or execution details
    let submitAndWaitForResultsWithValidationAsync
        (httpClient: System.Net.Http.HttpClient)
        (workspaceUrl: string)
        (circuit: IonQCircuit)
        (shots: int)
        (target: string)
        (constraints: CircuitValidator.BackendConstraints option)
        : Task<Result<Map<string, int>, QuantumError>> =
        task {
            // Step 1: Determine constraints (auto-detect or use provided)
            let backendConstraints =
                match constraints with
                | Some c -> Some c
                | None -> CircuitValidator.KnownTargets.getConstraints target
            
            // Step 2: Validate circuit if constraints available
            let validationResult =
                match backendConstraints with
                | Some c ->
                    let circuitInfo = extractCircuitInfo circuit
                    CircuitValidator.validateCircuit c circuitInfo
                | None -> Ok ()  // No constraints available, skip validation
            
            // Step 3: Check validation result
            match validationResult with
            | Error validationErrors ->
                // Convert validation errors to QuantumError
                let errorMessages = validationErrors |> List.map CircuitValidator.formatValidationError
                return Error (QuantumError.ValidationError("circuit", String.concat "; " errorMessages))
            | Ok () ->
                // Step 4: Submit circuit (validation passed)
                return! submitAndWaitForResultsAsync httpClient workspaceUrl circuit shots target |> Async.AwaitTask
        }
