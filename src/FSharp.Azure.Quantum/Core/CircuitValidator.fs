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
    
    /// Backend constraints - configurable for any quantum provider
    /// Users can create custom constraints or use built-in factory functions
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
    
    /// Circuit statistics for validation
    /// Note: This is a statistical summary, not a full circuit representation.
    /// For full circuit representation, see CircuitBuilder.Circuit
    type CircuitStats = {
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
    // 2. CONSTRAINT FACTORY FUNCTIONS (Provider-Agnostic Design)
    // ============================================================================
    
    /// Factory functions for creating backend constraints
    /// Users can create custom constraints or use built-in providers
    module BackendConstraints =
        
        /// Create custom backend constraints
        /// Allows users to define constraints for any quantum provider
        let create 
            (name: string)
            (maxQubits: int)
            (supportedGates: string list)
            (maxCircuitDepth: int option)
            (hasAllToAllConnectivity: bool)
            (connectedPairs: (int * int) list) : BackendConstraints =
            {
                Name = name
                MaxQubits = maxQubits
                SupportedGates = Set.ofList supportedGates
                MaxCircuitDepth = maxCircuitDepth
                HasAllToAllConnectivity = hasAllToAllConnectivity
                ConnectedPairs = Set.ofList connectedPairs
            }
        
        /// IonQ Simulator constraints (29 qubits, all-to-all connectivity)
        /// Note: S, SDG, T, TDG, CZ, CCX not natively supported - use GateTranspiler for automatic decomposition
        let ionqSimulator () : BackendConstraints =
            {
                Name = "IonQ Simulator"
                MaxQubits = 29
                SupportedGates = Set.ofList ["X"; "Y"; "Z"; "H"; "Rx"; "Ry"; "Rz"; "CNOT"; "SWAP"]
                MaxCircuitDepth = Some 100
                HasAllToAllConnectivity = true
                ConnectedPairs = Set.empty
            }
        
        /// IonQ Hardware (Aria-1) constraints (11 qubits, all-to-all connectivity)
        /// Note: S, SDG, T, TDG, CZ, CCX not natively supported - use GateTranspiler for automatic decomposition
        let ionqHardware () : BackendConstraints =
            {
                Name = "IonQ Hardware"
                MaxQubits = 11
                SupportedGates = Set.ofList ["X"; "Y"; "Z"; "H"; "Rx"; "Ry"; "Rz"; "CNOT"; "SWAP"]
                MaxCircuitDepth = Some 100
                HasAllToAllConnectivity = true
                ConnectedPairs = Set.empty
            }
        
        /// Rigetti Aspen-M-3 constraints (79 qubits, limited connectivity)
        /// Note: S, SDG, T, TDG, CCX not natively supported - use GateTranspiler for automatic decomposition
        let rigettiAspenM3 () : BackendConstraints =
            // Rigetti Aspen-M-3 connectivity graph (simplified for validation)
            // Real topology has linear and ring connections
            let rigettiConnectivity = 
                [
                    // Linear connectivity chain for qubits 0-4
                    (0, 1); (1, 2); (2, 3); (3, 4)
                ]
            
            {
                Name = "Rigetti Aspen-M-3"
                MaxQubits = 79
                SupportedGates = Set.ofList ["X"; "Y"; "Z"; "H"; "Rx"; "Ry"; "Rz"; "CZ"; "CNOT"; "SWAP"]
                MaxCircuitDepth = Some 50
                HasAllToAllConnectivity = false
                ConnectedPairs = Set.ofList rigettiConnectivity
            }
        
        /// Local QAOA Simulator constraints (1-16 qubits, all gates supported)
        /// This is the local state vector simulator with exponential memory requirements
        /// Supports full OpenQASM 2.0 gate set including S, SDG, T, TDG, CZ, CCX
        let localSimulator () : BackendConstraints =
            {
                Name = "Local QAOA Simulator"
                MaxQubits = 16  // Limited by 2^n state vector memory (~1 MB for 16 qubits)
                SupportedGates = Set.ofList ["X"; "Y"; "Z"; "H"; "S"; "SDG"; "T"; "TDG"; "Rx"; "Ry"; "Rz"; "RZZ"; "CNOT"; "CZ"; "SWAP"; "CCX"]
                MaxCircuitDepth = None  // No practical depth limit for local execution
                HasAllToAllConnectivity = true  // Simulated, so all connections possible
                ConnectedPairs = Set.empty
            }
    
    // ============================================================================
    // 3. VALIDATION FUNCTIONS (Constraint-Based)
    // ============================================================================
    
    /// Validate circuit qubit count against backend limits
    let validateQubitCount (constraints: BackendConstraints) (circuit: CircuitStats) : Result<unit, ValidationError> =
        if circuit.NumQubits <= constraints.MaxQubits then
            Ok ()
        else
            Error (QubitCountExceeded(circuit.NumQubits, constraints.MaxQubits, constraints.Name))
    
    /// Validate circuit gate set against backend supported gates
    let validateGateSet (constraints: BackendConstraints) (circuit: CircuitStats) : Result<unit, ValidationError list> =
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
    let validateCircuitDepth (constraints: BackendConstraints) (circuit: CircuitStats) : Result<unit, ValidationError> =
        match constraints.MaxCircuitDepth with
        | None -> Ok ()  // No depth limit
        | Some maxDepth ->
            if circuit.GateCount <= maxDepth then
                Ok ()
            else
                Error (CircuitDepthExceeded(circuit.GateCount, maxDepth, constraints.Name))
    
    /// Validate two-qubit gate connectivity against backend constraints
    let validateConnectivity (constraints: BackendConstraints) (circuit: CircuitStats) : Result<unit, ValidationError list> =
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
    let validateCircuit (constraints: BackendConstraints) (circuit: CircuitStats) : Result<unit, ValidationError list> =
        let results = [
            // Run qubit count validation (single error)
            validateQubitCount constraints circuit
            |> Result.mapError (fun err -> [err])
            
            // Run gate set validation (multiple errors possible)
            validateGateSet constraints circuit
            
            // Run depth validation (single error)
            validateCircuitDepth constraints circuit
            |> Result.mapError (fun err -> [err])
            
            // Run connectivity validation (multiple errors possible)
            validateConnectivity constraints circuit
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
    
    // ============================================================================
    // 5. KNOWN AZURE QUANTUM TARGETS (Target String → Constraints)
    // ============================================================================
    
    /// Maps Azure Quantum target strings to backend constraints
    /// Provides automatic constraint lookup for common providers
    module KnownTargets =
        
        /// Get backend constraints for a known Azure Quantum target string
        /// Returns None if target is unknown (user must provide custom constraints)
        let getConstraints (targetString: string) : BackendConstraints option =
            match targetString.ToLowerInvariant() with
            // Local Simulator
            | "local" | "local.simulator" | "qaoa.local" -> Some (BackendConstraints.localSimulator())
            
            // IonQ Targets
            | "ionq.simulator" -> Some (BackendConstraints.ionqSimulator())
            | "ionq.qpu" | "ionq.qpu.aria-1" | "ionq.qpu.aria-2" -> Some (BackendConstraints.ionqHardware())
            
            // Rigetti Targets
            | "rigetti.sim.qvm" -> Some (BackendConstraints.rigettiAspenM3())
            | "rigetti.qpu.aspen-m-3" -> Some (BackendConstraints.rigettiAspenM3())
            
            // Unknown target - return None so user can provide custom constraints
            | _ -> None
        
        /// List of all known target strings
        let knownTargets : string list = [
            "local"
            "local.simulator"
            "qaoa.local"
            "ionq.simulator"
            "ionq.qpu"
            "ionq.qpu.aria-1"
            "ionq.qpu.aria-2"
            "rigetti.sim.qvm"
            "rigetti.qpu.aspen-m-3"
        ]
    
    // ============================================================================
    // 6. QAOA-SPECIFIC VALIDATION
    // ============================================================================
    
    /// Validate QAOA parameter arrays match circuit depth
    let validateQaoaParameters (depth: int) (gammaParams: float[]) (betaParams: float[]) : Result<unit, ValidationError> =
        // Check gamma parameters length
        if gammaParams.Length <> depth then
            Error (InvalidParameter(
                sprintf "Gamma parameter array length (%d) must match QAOA depth (%d)" 
                    gammaParams.Length depth))
        // Check beta parameters length
        else if betaParams.Length <> depth then
            Error (InvalidParameter(
                sprintf "Beta parameter array length (%d) must match QAOA depth (%d)" 
                    betaParams.Length depth))
        else
            Ok ()

