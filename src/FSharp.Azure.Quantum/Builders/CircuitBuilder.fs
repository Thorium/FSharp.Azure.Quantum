namespace FSharp.Azure.Quantum

/// Quantum circuit builder module for constructing and manipulating quantum circuits
module CircuitBuilder =

    /// Represents a quantum gate operation
    type Gate =
        // Pauli gates
        | X of int                    // Pauli-X gate (NOT) on qubit
        | Y of int                    // Pauli-Y gate on qubit
        | Z of int                    // Pauli-Z gate on qubit
        | H of int                    // Hadamard gate on qubit
        
        // Phase gates
        | S of int                    // S gate (√Z, phase gate) on qubit
        | SDG of int                  // S-dagger (S†, inverse phase) on qubit
        | T of int                    // T gate (√S, π/8 gate) on qubit
        | TDG of int                  // T-dagger (T†, inverse π/8) on qubit
        
        // Rotation gates
        | RX of int * float           // Rotation around X-axis (qubit, angle)
        | RY of int * float           // Rotation around Y-axis (qubit, angle)
        | RZ of int * float           // Rotation around Z-axis (qubit, angle)
        
        // Two-qubit gates
        | CNOT of int * int           // Controlled-NOT (control, target)
        | CZ of int * int             // Controlled-Z (control, target)
        | SWAP of int * int           // SWAP two qubits (qubit1, qubit2)
        
        // Three-qubit gates
        | CCX of int * int * int      // Toffoli (CCNOT) (control1, control2, target)

    /// Represents a quantum circuit with gates and qubit count
    type Circuit = {
        QubitCount: int
        Gates: Gate list
    }

    /// Creates an empty circuit with specified number of qubits
    let empty qubitCount : Circuit =
        { QubitCount = qubitCount; Gates = [] }

    /// Gets the number of gates in a circuit
    let gateCount (circuit: Circuit) : int =
        circuit.Gates.Length

    /// Gets the number of qubits in a circuit
    let qubitCount (circuit: Circuit) : int =
        circuit.QubitCount

    /// Adds a single gate to the end of a circuit
    let addGate (gate: Gate) (circuit: Circuit) : Circuit =
        { circuit with Gates = circuit.Gates @ [gate] }

    /// Adds multiple gates to the end of a circuit
    let addGates (gates: Gate list) (circuit: Circuit) : Circuit =
        { circuit with Gates = circuit.Gates @ gates }

    /// Composes two circuits by appending the gates of the second to the first
    let compose (circuit1: Circuit) (circuit2: Circuit) : Circuit =
        if circuit1.QubitCount <> circuit2.QubitCount then
            failwith "Cannot compose circuits with different qubit counts"
        { circuit1 with Gates = circuit1.Gates @ circuit2.Gates }

    /// Gets all gates from a circuit in order
    let getGates (circuit: Circuit) : Gate list =
        circuit.Gates

    /// Optimizes a circuit by removing inverse gate pairs and fusing rotation gates
    let optimize (circuit: Circuit) : Circuit =
        let rec optimizeGates (gates: Gate list) : Gate list =
            match gates with
            | [] -> []
            | [gate] -> [gate]
            // Remove consecutive H gates on same qubit
            | H q1 :: H q2 :: rest when q1 = q2 ->
                optimizeGates rest
            // Remove consecutive X gates on same qubit
            | X q1 :: X q2 :: rest when q1 = q2 ->
                optimizeGates rest
            // Remove S and SDG pairs on same qubit
            | S q1 :: SDG q2 :: rest when q1 = q2 ->
                optimizeGates rest
            | SDG q1 :: S q2 :: rest when q1 = q2 ->
                optimizeGates rest
            // Remove T and TDG pairs on same qubit
            | T q1 :: TDG q2 :: rest when q1 = q2 ->
                optimizeGates rest
            | TDG q1 :: T q2 :: rest when q1 = q2 ->
                optimizeGates rest
            // Remove consecutive SWAP gates on same qubits
            | SWAP (q1a, q1b) :: SWAP (q2a, q2b) :: rest 
                when (q1a = q2a && q1b = q2b) || (q1a = q2b && q1b = q2a) ->
                optimizeGates rest
            // Fuse consecutive RX gates on same qubit
            | RX (q1, angle1) :: RX (q2, angle2) :: rest when q1 = q2 ->
                optimizeGates (RX (q1, angle1 + angle2) :: rest)
            // Fuse consecutive RY gates on same qubit
            | RY (q1, angle1) :: RY (q2, angle2) :: rest when q1 = q2 ->
                optimizeGates (RY (q1, angle1 + angle2) :: rest)
            // Fuse consecutive RZ gates on same qubit
            | RZ (q1, angle1) :: RZ (q2, angle2) :: rest when q1 = q2 ->
                optimizeGates (RZ (q1, angle1 + angle2) :: rest)
            // Keep gate and continue
            | gate :: rest ->
                gate :: optimizeGates rest
        
        { circuit with Gates = optimizeGates circuit.Gates }

    /// Converts a gate to OpenQASM 2.0 format
    let private gateToQASM (gate: Gate) : string =
        match gate with
        | X q -> $"x q[{q}];"
        | Y q -> $"y q[{q}];"
        | Z q -> $"z q[{q}];"
        | H q -> $"h q[{q}];"
        | S q -> $"s q[{q}];"
        | SDG q -> $"sdg q[{q}];"
        | T q -> $"t q[{q}];"
        | TDG q -> $"tdg q[{q}];"
        | CNOT (control, target) -> $"cx q[{control}],q[{target}];"
        | CZ (control, target) -> $"cz q[{control}],q[{target}];"
        | SWAP (q1, q2) -> $"swap q[{q1}],q[{q2}];"
        | CCX (c1, c2, t) -> $"ccx q[{c1}],q[{c2}],q[{t}];"
        | RX (q, angle) -> $"rx({angle}) q[{q}];"
        | RY (q, angle) -> $"ry({angle}) q[{q}];"
        | RZ (q, angle) -> $"rz({angle}) q[{q}];"

    /// Converts a circuit to OpenQASM 2.0 format for Azure Quantum submission
    let toOpenQASM (circuit: Circuit) : string =
        let header = "OPENQASM 2.0;\ninclude \"qelib1.inc\";"
        let qregDecl = $"qreg q[{circuit.QubitCount}];"
        let gateLines = circuit.Gates |> List.map gateToQASM
        
        let allLines = header :: qregDecl :: gateLines
        System.String.Join("\n", allLines)

    /// Get the name of a gate for validation/display purposes
    let getGateName (gate: Gate) : string =
        match gate with
        | X _ -> "X"
        | Y _ -> "Y"
        | Z _ -> "Z"
        | H _ -> "H"
        | S _ -> "S"
        | SDG _ -> "SDG"
        | T _ -> "T"
        | TDG _ -> "TDG"
        | RX _ -> "Rx"
        | RY _ -> "Ry"
        | RZ _ -> "Rz"
        | CNOT _ -> "CNOT"
        | CZ _ -> "CZ"
        | SWAP _ -> "SWAP"
        | CCX _ -> "CCX"
    
    /// Validates a circuit for correctness (qubit bounds, gate compatibility)
    let validate (circuit: Circuit) : Validation.ValidationResult =
        let validateQubit (q: int) : string option =
            if q < 0 then
                Some $"Qubit index {q} is negative (must be >= 0)"
            elif q >= circuit.QubitCount then
                Some $"Qubit index {q} out of bounds (circuit has {circuit.QubitCount} qubits)"
            else
                None

        let validateGate (gate: Gate) : string list =
            match gate with
            | X q | Y q | Z q | H q | S q | SDG q | T q | TDG q ->
                validateQubit q |> Option.toList
            | RX (q, _) | RY (q, _) | RZ (q, _) ->
                validateQubit q |> Option.toList
            | CNOT (control, target) | CZ (control, target) ->
                let errors = []
                let errors = 
                    match validateQubit control with
                    | Some err -> err :: errors
                    | None -> errors
                let errors = 
                    match validateQubit target with
                    | Some err -> err :: errors
                    | None -> errors
                let errors = 
                    if control = target then
                        $"{gate.GetType().Name} control and target cannot be the same qubit" :: errors
                    else
                        errors
                List.rev errors
            | SWAP (q1, q2) ->
                let errors = []
                let errors = 
                    match validateQubit q1 with
                    | Some err -> err :: errors
                    | None -> errors
                let errors = 
                    match validateQubit q2 with
                    | Some err -> err :: errors
                    | None -> errors
                let errors = 
                    if q1 = q2 then
                        "SWAP qubits cannot be the same" :: errors
                    else
                        errors
                List.rev errors
            | CCX (control1, control2, target) ->
                let errors = []
                let errors = 
                    match validateQubit control1 with
                    | Some err -> err :: errors
                    | None -> errors
                let errors = 
                    match validateQubit control2 with
                    | Some err -> err :: errors
                    | None -> errors
                let errors = 
                    match validateQubit target with
                    | Some err -> err :: errors
                    | None -> errors
                let errors = 
                    if control1 = control2 || control1 = target || control2 = target then
                        "CCX (Toffoli) control and target qubits must be distinct" :: errors
                    else
                        errors
                List.rev errors

        let allErrors = 
            circuit.Gates 
            |> List.collect validateGate

        if List.isEmpty allErrors then
            Validation.success
        else
            Validation.failure allErrors
