namespace FSharp.Azure.Quantum

/// Quantum circuit builder module for constructing and manipulating quantum circuits
module CircuitBuilder =

    /// Represents a quantum gate operation
    type Gate =
        | X of int                    // Pauli-X gate (NOT) on qubit
        | Y of int                    // Pauli-Y gate on qubit
        | Z of int                    // Pauli-Z gate on qubit
        | H of int                    // Hadamard gate on qubit
        | CNOT of int * int           // Controlled-NOT (control, target)
        | RX of int * float           // Rotation around X-axis (qubit, angle)
        | RY of int * float           // Rotation around Y-axis (qubit, angle)
        | RZ of int * float           // Rotation around Z-axis (qubit, angle)

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
        | CNOT (control, target) -> $"cx q[{control}],q[{target}];"
        | RX (q, angle) -> $"rx({angle}) q[{q}];"
        | RY (q, angle) -> $"ry({angle}) q[{q}];"
        | RZ (q, angle) -> $"rz({angle}) q[{q}];"

    /// Converts a circuit to OpenQASM 2.0 format for Azure Quantum submission
    let toOpenQASM (circuit: Circuit) : string =
        let header = "OPENQASM 2.0;\ninclude \"qelib1.inc\";"
        let qregDecl = $"qreg q[{circuit.QubitCount}];"
        let gateLines = circuit.Gates |> List.map gateToQASM
        
        let allLines = [header; qregDecl] @ gateLines
        System.String.Join("\n", allLines)

    /// Validation result containing validity status and error messages
    type ValidationResult = {
        IsValid: bool
        Errors: string list
    }

    /// Validates a circuit for correctness (qubit bounds, gate compatibility)
    let validate (circuit: Circuit) : ValidationResult =
        let validateQubit (q: int) : string option =
            if q < 0 then
                Some $"Qubit index {q} is negative (must be >= 0)"
            elif q >= circuit.QubitCount then
                Some $"Qubit index {q} out of bounds (circuit has {circuit.QubitCount} qubits)"
            else
                None

        let validateGate (gate: Gate) : string list =
            match gate with
            | X q | Y q | Z q | H q ->
                validateQubit q |> Option.toList
            | RX (q, _) | RY (q, _) | RZ (q, _) ->
                validateQubit q |> Option.toList
            | CNOT (control, target) ->
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
                        "CNOT control and target cannot be the same qubit" :: errors
                    else
                        errors
                List.rev errors

        let allErrors = 
            circuit.Gates 
            |> List.collect validateGate

        {
            IsValid = List.isEmpty allErrors
            Errors = allErrors
        }
