namespace FSharp.Azure.Quantum.Core

/// Circuit Validation Module
/// 
/// Validates quantum circuits against backend constraints before submission
/// to prevent costly failed submissions to Azure Quantum.
/// 
/// ⚠️ CRITICAL: ALL validation code in this SINGLE FILE for AI context optimization
module CircuitValidator =
    
    // ============================================================================
    // 1. TYPES AND RECORDS (Primitives, no dependencies)
    // ============================================================================
    
    /// Backend types
    type Backend =
        | IonQSimulator
        | IonQHardware
        | RigettiAspenM3
    
    /// Backend constraints
    type BackendConstraints = {
        /// Backend name for display
        Name: string
        
        /// Maximum number of qubits supported
        MaxQubits: int
        
        /// Supported gate set
        SupportedGates: Set<string>
        
        /// Maximum circuit depth (practical limit)
        MaxCircuitDepth: int option
        
        /// Whether backend has all-to-all qubit connectivity
        HasAllToAllConnectivity: bool
        
        /// Connected qubit pairs (for limited connectivity backends)
        ConnectedPairs: Set<int * int>
    }
    
    /// Simple circuit representation for validation
    type Circuit = {
        /// Number of qubits used in circuit
        NumQubits: int
        
        /// Total number of gates in circuit
        GateCount: int
        
        /// Set of unique gate types used
        UsedGates: Set<string>
        
        /// Two-qubit gate connections (qubit pairs)
        TwoQubitGates: (int * int) list
    }
    
    /// Validation error types
    type ValidationError =
        | QubitCountExceeded of requested: int * limit: int * backend: string
        | UnsupportedGate of gate: string * backend: string * supportedGates: Set<string>
        | CircuitDepthExceeded of depth: int * limit: int * backend: string
        | ConnectivityViolation of qubit1: int * qubit2: int * backend: string
        | InvalidParameter of message: string
    
    // ============================================================================
    // 2. CONSTRAINT DEFINITIONS
    // ============================================================================
    
    /// Get backend constraints for a given backend
    let getConstraints (backend: Backend) : BackendConstraints =
        match backend with
        | IonQSimulator ->
            {
                Name = "IonQ Simulator"
                MaxQubits = 29
                SupportedGates = Set.ofList ["X"; "Y"; "Z"; "H"; "Rx"; "Ry"; "Rz"; "CNOT"; "SWAP"]
                MaxCircuitDepth = Some 100
                HasAllToAllConnectivity = true
                ConnectedPairs = Set.empty
            }
        | IonQHardware ->
            {
                Name = "IonQ Hardware"
                MaxQubits = 11
                SupportedGates = Set.ofList ["X"; "Y"; "Z"; "H"; "Rx"; "Ry"; "Rz"; "CNOT"; "SWAP"]
                MaxCircuitDepth = Some 100
                HasAllToAllConnectivity = true
                ConnectedPairs = Set.empty
            }
        | RigettiAspenM3 ->
            // Rigetti Aspen-M-3 connectivity graph (simplified for validation)
            // Real topology has linear and ring connections
            let rigettiConnectivity = 
                [
                    // Linear connectivity chain for qubits 0-4
                    (0, 1); (1, 2); (2, 3); (3, 4)
                ] |> Set.ofList
            
            {
                Name = "Rigetti Aspen-M-3"
                MaxQubits = 79
                SupportedGates = Set.ofList ["X"; "Y"; "Z"; "H"; "Rx"; "Ry"; "Rz"; "CZ"; "CNOT"; "SWAP"]
                MaxCircuitDepth = Some 50
                HasAllToAllConnectivity = false
                ConnectedPairs = rigettiConnectivity
            }
    
    // ============================================================================
    // 3. VALIDATION FUNCTIONS
    // ============================================================================
    
    /// Validate circuit qubit count against backend limits
    let validateQubitCount (backend: Backend) (circuit: Circuit) : Result<unit, ValidationError> =
        let constraints = getConstraints backend
        if circuit.NumQubits <= constraints.MaxQubits then
            Ok ()
        else
            Error (QubitCountExceeded(circuit.NumQubits, constraints.MaxQubits, constraints.Name))
    
    /// Validate circuit gate set against backend supported gates
    let validateGateSet (backend: Backend) (circuit: Circuit) : Result<unit, ValidationError list> =
        let constraints = getConstraints backend
        let unsupportedGates = 
            circuit.UsedGates
            |> Set.filter (fun gate -> not (constraints.SupportedGates.Contains gate))
            |> Set.toList
        
        match unsupportedGates with
        | [] -> Ok ()
        | gates -> 
            gates
            |> List.map (fun gate -> 
                UnsupportedGate(gate, constraints.Name, constraints.SupportedGates))
            |> Error
    
    /// Validate circuit depth against backend limits
    let validateCircuitDepth (backend: Backend) (circuit: Circuit) : Result<unit, ValidationError> =
        let constraints = getConstraints backend
        match constraints.MaxCircuitDepth with
        | None -> Ok ()  // No depth limit
        | Some maxDepth ->
            if circuit.GateCount <= maxDepth then
                Ok ()
            else
                Error (CircuitDepthExceeded(circuit.GateCount, maxDepth, constraints.Name))
    
    /// Validate two-qubit gate connectivity against backend constraints
    let validateConnectivity (backend: Backend) (circuit: Circuit) : Result<unit, ValidationError list> =
        let constraints = getConstraints backend
        
        // If backend has all-to-all connectivity, all connections are valid
        if constraints.HasAllToAllConnectivity then
            Ok ()
        else
            // Check each two-qubit gate against connected pairs
            let invalidConnections =
                circuit.TwoQubitGates
                |> List.filter (fun (q1, q2) ->
                    // Check both directions since connectivity is bidirectional
                    not (constraints.ConnectedPairs.Contains (q1, q2) || 
                         constraints.ConnectedPairs.Contains (q2, q1)))
            
            match invalidConnections with
            | [] -> Ok ()
            | connections ->
                connections
                |> List.map (fun (q1, q2) -> 
                    ConnectivityViolation(q1, q2, constraints.Name))
                |> Error
    
    /// Validate entire circuit against backend constraints
    /// Runs all validation checks and collects all errors
    let validateCircuit (backend: Backend) (circuit: Circuit) : Result<unit, ValidationError list> =
        let results = [
            // Run qubit count validation (single error)
            validateQubitCount backend circuit
            |> Result.mapError (fun err -> [err])
            
            // Run gate set validation (multiple errors possible)
            validateGateSet backend circuit
            
            // Run depth validation (single error)
            validateCircuitDepth backend circuit
            |> Result.mapError (fun err -> [err])
            
            // Run connectivity validation (multiple errors possible)
            validateConnectivity backend circuit
        ]
        
        // Collect all errors from all validations
        let allErrors = 
            results
            |> List.choose (fun result ->
                match result with
                | Error errors -> Some errors
                | Ok () -> None)
            |> List.concat
        
        match allErrors with
        | [] -> Ok ()
        | errors -> Error errors
    
    // ============================================================================
    // 4. ERROR MESSAGE FORMATTING
    // ============================================================================
    
    /// Format a single validation error into a user-friendly message
    let formatValidationError (error: ValidationError) : string =
        match error with
        | QubitCountExceeded(requested, limit, backend) ->
            sprintf "Circuit requires %d qubits but %s supports maximum %d qubits. Please reduce circuit size or choose a different backend." 
                requested backend limit
        
        | UnsupportedGate(gate, backend, supportedGates) ->
            let supportedList = 
                supportedGates 
                |> Set.toList 
                |> String.concat ", "
            sprintf "Gate '%s' is not supported by %s. Supported gates: %s" 
                gate backend supportedList
        
        | CircuitDepthExceeded(depth, limit, backend) ->
            sprintf "Circuit depth of %d gates exceeds %s recommended limit of %d. Consider circuit optimization or decomposition." 
                depth backend limit
        
        | ConnectivityViolation(q1, q2, backend) ->
            sprintf "Two-qubit gate between qubits %d and %d violates %s connectivity constraints. These qubits are not directly connected." 
                q1 q2 backend
        
        | InvalidParameter(message) ->
            sprintf "Invalid parameter: %s" message
    
    /// Format multiple validation errors into a summary message
    let formatValidationErrors (errors: ValidationError list) : string =
        let header = sprintf "Circuit validation failed with %d validation error(s):\n" errors.Length
        let messages = 
            errors 
            |> List.mapi (fun i err -> 
                sprintf "%d. %s" (i + 1) (formatValidationError err))
            |> String.concat "\n"
        header + messages

