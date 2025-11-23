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
