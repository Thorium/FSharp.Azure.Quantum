namespace FSharp.Azure.Quantum.Core

open System
open System.IO
open System.Text.Json
open FSharp.Azure.Quantum.Core.Types

/// IonQ Backend Integration
/// 
/// Implements circuit serialization to IonQ format, job submission to ionq.simulator,
/// result parsing, and error mapping.
module IonQBackend =
    
    // ============================================================================
    // TYPE DEFINITIONS (TDD Cycle 1)
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
    // GATE SERIALIZATION (TDD Cycle 2)
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
    // CIRCUIT SERIALIZATION (TDD Cycle 3)
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
    // JOB SUBMISSION (TDD Cycle 4)
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
    // RESULT PARSING (TDD Cycle 5)
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
    // ERROR MAPPING (TDD Cycle 6)
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
            QuantumError.InvalidCircuit [ errorMessage ]
        
        | "TooManyQubits" ->
            // TooManyQubits is a circuit validation error
            QuantumError.InvalidCircuit [ sprintf "Circuit too large: %s" errorMessage ]
        
        | "QuotaExceeded" ->
            QuantumError.QuotaExceeded errorMessage
        
        | "BackendUnavailable" ->
            // Suggest retry after 5 minutes for maintenance
            QuantumError.ServiceUnavailable (Some (TimeSpan.FromMinutes(5.0)))
        
        | _ ->
            // Unknown IonQ error
            QuantumError.UnknownError(0, sprintf "IonQ error: %s - %s" errorCode errorMessage)
