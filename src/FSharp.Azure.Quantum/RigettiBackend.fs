namespace FSharp.Azure.Quantum.Core

open System
open System.Text
open System.Text.Json
open FSharp.Azure.Quantum.Core.Types

/// Rigetti Backend Integration
/// 
/// Implements circuit serialization to Quil assembly language, job submission to rigetti.sim.qvm,
/// connectivity graph validation, result parsing, and error mapping.
module RigettiBackend =
    
    // ============================================================================
    // TYPE DEFINITIONS
    // ============================================================================
    
    /// Quil gate representation using discriminated unions
    /// 
    /// Design rationale: Quil is a text-based assembly language. Each gate is represented
    /// as a line of assembly code. Using discriminated unions ensures type-safety and
    /// makes serialization to Quil text straightforward.
    /// 
    /// Quil Examples:
    /// - H 0           (Hadamard on qubit 0)
    /// - RX(1.57) 0    (RX rotation by π/2 on qubit 0)
    /// - CZ 0 1        (Controlled-Z between qubits 0 and 1)
    /// - MEASURE 0 ro[0]  (Measure qubit 0, store in classical register ro[0])
    type QuilGate =
        /// Single-qubit gate (H, X, Y, Z, S, T)
        /// Serializes to: "GATE qubit"
        | SingleQubit of gate: string * qubit: int
        
        /// Single-qubit rotation gate (RX, RY, RZ)
        /// Serializes to: "GATE(angle) qubit"
        | SingleQubitRotation of gate: string * angle: float * qubit: int
        
        /// Two-qubit gate (CZ is native for Rigetti)
        /// Serializes to: "GATE control target"
        | TwoQubit of gate: string * control: int * target: int
        
        /// Measurement operation
        /// Serializes to: "MEASURE qubit memoryRef"
        /// Example: "MEASURE 0 ro[0]"
        | Measure of qubit: int * memoryRef: string
        
        /// Classical memory declaration (required for measurements)
        /// Serializes to: "DECLARE name type[size]"
        /// Example: "DECLARE ro BIT[2]"
        | DeclareMemory of name: string * typ: string * size: int
    
    /// Quil program format (assembly text)
    /// 
    /// Example Quil program:
    /// ```
    /// DECLARE ro BIT[2]
    /// H 0
    /// CZ 0 1
    /// MEASURE 0 ro[0]
    /// MEASURE 1 ro[1]
    /// ```
    type QuilProgram = {
        /// Memory declarations (must come first)
        Declarations: QuilGate list
        
        /// Sequence of gate instructions
        Instructions: QuilGate list
    }
    
    /// Connectivity graph representing allowed two-qubit interactions
    /// 
    /// Rigetti systems have limited connectivity. Only certain qubit pairs
    /// can directly interact. The connectivity graph defines the topology.
    /// 
    /// Example (5-qubit linear chain):
    /// ```
    /// 0 -- 1 -- 2 -- 3 -- 4
    /// ```
    /// Edges: {(0,1), (1,2), (2,3), (3,4)}
    /// 
    /// A CZ(0, 3) gate would be INVALID because qubits 0 and 3 are not connected.
    type ConnectivityGraph = {
        /// Set of bidirectional edges (qubit pairs)
        /// Both (a, b) and (b, a) represent the same edge
        Edges: Set<int * int>
    }
    
    // ============================================================================
    // GATE SERIALIZATION
    // ============================================================================
    
    /// Serialize a single Quil gate to assembly text
    /// 
    /// Converts QuilGate discriminated unions to Quil assembly language strings.
    /// Uses simple string formatting - no JSON or complex serialization needed.
    /// 
    /// Examples:
    /// - SingleQubit("H", 0) → "H 0"
    /// - SingleQubitRotation("RX", π/2, 0) → "RX(1.5707963267948966) 0"
    /// - TwoQubit("CZ", 0, 1) → "CZ 0 1"
    /// - Measure(0, "ro[0]") → "MEASURE 0 ro[0]"
    /// - DeclareMemory("ro", "BIT", 2) → "DECLARE ro BIT[2]"
    let serializeGate (gate: QuilGate) : string =
        match gate with
        | SingleQubit(gateName, qubit) ->
            sprintf "%s %d" gateName qubit
        
        | SingleQubitRotation(gateName, angle, qubit) ->
            sprintf "%s(%s) %d" gateName (angle.ToString("R")) qubit
        
        | TwoQubit(gateName, control, target) ->
            sprintf "%s %d %d" gateName control target
        
        | Measure(qubit, memoryRef) ->
            sprintf "MEASURE %d %s" qubit memoryRef
        
        | DeclareMemory(name, typ, size) ->
            sprintf "DECLARE %s %s[%d]" name typ size
    
    // ============================================================================
    // PROGRAM SERIALIZATION
    // ============================================================================
    
    /// Serialize a complete Quil program to assembly text
    /// 
    /// Converts a QuilProgram (declarations + instructions) to multi-line Quil assembly.
    /// Uses StringBuilder for efficient string concatenation of multiple lines.
    /// 
    /// Format:
    /// 1. Declarations first (DECLARE statements)
    /// 2. Instructions second (gates, measurements)
    /// 3. Newline-separated
    /// 
    /// Example output:
    /// ```
    /// DECLARE ro BIT[2]
    /// H 0
    /// CZ 0 1
    /// MEASURE 0 ro[0]
    /// MEASURE 1 ro[1]
    /// ```
    let serializeProgram (program: QuilProgram) : string =
        let sb = StringBuilder()
        
        // Serialize declarations first
        for decl in program.Declarations do
            if sb.Length > 0 then sb.Append('\n') |> ignore
            sb.Append(serializeGate decl) |> ignore
        
        // Serialize instructions
        for instr in program.Instructions do
            if sb.Length > 0 then sb.Append('\n') |> ignore
            sb.Append(serializeGate instr) |> ignore
        
        sb.ToString()
    
    // ============================================================================
    // CONNECTIVITY VALIDATION
    // ============================================================================
    
    /// Check if a gate is valid according to the connectivity graph
    /// 
    /// Single-qubit gates, measurements, and declarations are always valid.
    /// Two-qubit gates must have an edge in the connectivity graph.
    /// Edges are bidirectional: (a, b) and (b, a) represent the same connection.
    /// 
    /// Examples:
    /// - H 0: Always valid (single-qubit)
    /// - CZ 0 1 with graph {(0,1)}: Valid
    /// - CZ 1 0 with graph {(0,1)}: Valid (bidirectional)
    /// - CZ 0 3 with graph {(0,1), (1,2)}: Invalid (no direct connection)
    let isValidGate (graph: ConnectivityGraph) (gate: QuilGate) : bool =
        match gate with
        | SingleQubit _ -> true
        | SingleQubitRotation _ -> true
        | Measure _ -> true
        | DeclareMemory _ -> true
        | TwoQubit(_, control, target) ->
            // Check if edge exists in either direction (bidirectional)
            graph.Edges.Contains (control, target) || 
            graph.Edges.Contains (target, control)
    
    /// Validate an entire program against a connectivity graph
    /// 
    /// Returns Ok() if all two-qubit gates respect the topology.
    /// Returns Error with details of the first invalid gate found.
    /// 
    /// This prevents submission of circuits that would fail on hardware
    /// due to limited qubit connectivity.
    let validateProgram (graph: ConnectivityGraph) (program: QuilProgram) : Result<unit, string> =
        // Check all gates (declarations + instructions)
        let allGates = List.append program.Declarations program.Instructions
        
        // Find first invalid gate
        let invalidGate = 
            allGates 
            |> List.tryFind (fun gate -> not (isValidGate graph gate))
        
        match invalidGate with
        | None -> Ok ()
        | Some gate ->
            let gateText = serializeGate gate
            Error (sprintf "Gate '%s' violates connectivity constraints. Qubits are not directly connected in the hardware topology." gateText)
    
    // ============================================================================
    // CIRCUIT VALIDATION (Pre-Flight Checks)
    // ============================================================================
    
    /// Extract circuit information for validation
    /// Converts QuilProgram to CircuitValidator.Circuit format
    let private extractCircuitInfo (program: QuilProgram) : CircuitValidator.Circuit =
        let allGates = List.append program.Declarations program.Instructions
        
        let usedGates = 
            allGates
            |> List.choose (fun gate ->
                match gate with
                | SingleQubit(gateName, _) -> Some (gateName.ToUpperInvariant())
                | SingleQubitRotation(gateName, _, _) -> Some (gateName.ToUpperInvariant())
                | TwoQubit(gateName, _, _) -> Some (gateName.ToUpperInvariant())
                | Measure _ -> Some "MEASURE"
                | DeclareMemory _ -> None)
            |> Set.ofList
        
        let twoQubitGates =
            allGates
            |> List.choose (fun gate ->
                match gate with
                | TwoQubit(_, control, target) -> Some (control, target)
                | _ -> None)
        
        // Count max qubit index used
        let maxQubit =
            allGates
            |> List.choose (fun gate ->
                match gate with
                | SingleQubit(_, q) -> Some q
                | SingleQubitRotation(_, _, q) -> Some q
                | TwoQubit(_, c, t) -> Some (max c t)
                | Measure(q, _) -> Some q
                | DeclareMemory _ -> None)
            |> function
                | [] -> 0
                | qubits -> List.max qubits
        
        {
            CircuitValidator.NumQubits = maxQubit + 1  // Convert 0-indexed to count
            CircuitValidator.GateCount = program.Instructions.Length
            CircuitValidator.UsedGates = usedGates
            CircuitValidator.TwoQubitGates = twoQubitGates
        }
    
    /// Validate Quil program with backend constraints before submission
    /// 
    /// This is a high-level validation function that checks circuit against
    /// backend constraints (qubit count, gates, connectivity, depth).
    /// Complements the existing validateProgram function which checks connectivity only.
    /// 
    /// Parameters:
    /// - program: Quil program to validate
    /// - target: Target backend string (e.g., "rigetti.sim.qvm")
    /// - constraints: Optional constraints (auto-detected from target if None)
    /// 
    /// Returns: Result<unit, QuantumError> with validation errors
    let validateProgramWithConstraints 
        (program: QuilProgram)
        (target: string)
        (constraints: CircuitValidator.BackendConstraints option)
        : Result<unit, QuantumError> =
        
        // Determine constraints (auto-detect or use provided)
        let backendConstraints =
            match constraints with
            | Some c -> Some c
            | None -> CircuitValidator.KnownTargets.getConstraints target
        
        // Validate circuit if constraints available
        match backendConstraints with
        | Some c ->
            let circuitInfo = extractCircuitInfo program
            match CircuitValidator.validateCircuit c circuitInfo with
            | Error validationErrors ->
                // Convert validation errors to QuantumError
                let errorMessages = validationErrors |> List.map CircuitValidator.formatValidationError
                Error (QuantumError.InvalidCircuit errorMessages)
            | Ok () -> Ok ()
        | None -> Ok ()  // No constraints available, skip validation
    
    // ============================================================================
    // JOB SUBMISSION
    // ============================================================================
    
    /// Create a job submission for Rigetti backend
    /// 
    /// Converts a QuilProgram to a JobSubmission that can be sent to rigetti.sim.qvm
    /// or rigetti.qpu hardware backends.
    /// 
    /// Parameters:
    /// - program: Quil program to execute
    /// - shots: Number of measurement shots
    /// - target: Target backend (e.g., "rigetti.sim.qvm", "rigetti.qpu.ankaa-3")
    /// - name: Optional job name
    /// 
    /// Returns: JobSubmission ready for submitJobAsync
    let createJobSubmission 
        (program: QuilProgram) 
        (shots: int) 
        (target: string) 
        (name: string option)
        : JobSubmission =
        
        // Serialize program to Quil assembly text
        let quilText = serializeProgram program
        
        // Create job submission
        {
            JobId = Guid.NewGuid().ToString()
            Target = target
            Name = name
            InputData = quilText  // Quil assembly as string
            InputDataFormat = CircuitFormat.Custom "application/x-quil"  // Rigetti Quil format
            InputParams = Map.ofList [ ("count", box shots) ]  // "count" is Rigetti parameter name for shots
            Tags = Map.empty
        }
    
    // ============================================================================
    // RESULT PARSING
    // ============================================================================
    
    /// Parse Rigetti results from JSON response
    /// 
    /// Rigetti returns results in a histogram format:
    /// ```json
    /// {
    ///   "histogram": {
    ///     "00": 480,
    ///     "01": 12,
    ///     "10": 8,
    ///     "11": 500
    ///   }
    /// }
    /// ```
    /// 
    /// Returns: Map<string, int> of measurement outcomes to counts
    let parseRigettiResults (json: string) : Result<Map<string, int>, QuantumError> =
        try
            // Parse JSON response
            use doc = JsonDocument.Parse(json)
            let root = doc.RootElement
            
            // Extract histogram
            let mutable histogramElement = Unchecked.defaultof<JsonElement>
            if root.TryGetProperty("histogram", &histogramElement) then
                // Convert to F# Map
                let histogram = 
                    histogramElement.EnumerateObject()
                    |> Seq.map (fun prop -> (prop.Name, prop.Value.GetInt32()))
                    |> Map.ofSeq
                
                Ok histogram
            else
                Error (QuantumError.UnknownError(0, "Missing 'histogram' field in Rigetti response"))
        with
        | :? JsonException as ex ->
            Error (QuantumError.UnknownError(0, sprintf "JSON parsing error: %s" ex.Message))
        | ex ->
            Error (QuantumError.UnknownError(0, sprintf "Unexpected error: %s" ex.Message))
    
    /// Map Rigetti-specific error codes to QuantumError
    /// 
    /// Rigetti error codes:
    /// - InvalidProgram: Malformed Quil syntax
    /// - TopologyError: Two-qubit gate violates connectivity
    /// - TooManyQubits: Circuit exceeds qubit limit
    /// - QuotaExceeded: Insufficient quantum credits
    /// 
    /// Returns: Appropriate QuantumError variant
    let mapRigettiError (errorCode: string) (errorMessage: string) : QuantumError =
        match errorCode with
        | "InvalidProgram" -> 
            QuantumError.InvalidCircuit [sprintf "Malformed Quil program: %s" errorMessage]
        | "TopologyError" -> 
            QuantumError.InvalidCircuit [sprintf "Connectivity violation: %s" errorMessage]
        | "TooManyQubits" -> 
            QuantumError.InvalidCircuit [sprintf "Qubit limit exceeded: %s" errorMessage]
        | "QuotaExceeded" -> 
            QuantumError.QuotaExceeded "quantum-credits"
        | _ -> 
            QuantumError.UnknownError(0, sprintf "Rigetti error %s: %s" errorCode errorMessage)
