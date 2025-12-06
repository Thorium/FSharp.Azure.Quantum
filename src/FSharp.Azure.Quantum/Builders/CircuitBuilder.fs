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
        | P of int * float            // Phase gate P(θ) = diag(1, e^(iθ)) on qubit
        
        // Rotation gates
        | RX of int * float           // Rotation around X-axis (qubit, angle)
        | RY of int * float           // Rotation around Y-axis (qubit, angle)
        | RZ of int * float           // Rotation around Z-axis (qubit, angle)
        
        // Two-qubit gates
        | CNOT of int * int           // Controlled-NOT (control, target)
        | CZ of int * int             // Controlled-Z (control, target)
        | CP of int * int * float     // Controlled-P gate: CP(θ) applies P(θ) when control is |1⟩
        | SWAP of int * int           // SWAP two qubits (qubit1, qubit2)
        
        // Three-qubit gates
        | CCX of int * int * int      // Toffoli (CCNOT) (control1, control2, target)
        
        // Multi-qubit gates
        | MCZ of int list * int       // Multi-controlled Z (controls, target)
        
        // Measurement
        | Measure of int              // Measurement of qubit in computational basis

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

    /// Adds a measurement operation to the end of a circuit
    let addMeasurement (qubit: int) (circuit: Circuit) : Circuit =
        addGate (Measure qubit) circuit

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
        | P (q, theta) -> $"p({theta}) q[{q}];"
        | CNOT (control, target) -> $"cx q[{control}],q[{target}];"
        | CZ (control, target) -> $"cz q[{control}],q[{target}];"
        | MCZ (controls, target) -> 
            // OpenQASM 2.0 doesn't support MCZ natively (OpenQASM 3.1 will)
            // For now, emit as comment - use GateTranspiler.transpile() to decompose
            let controlsStr = controls |> List.map string |> String.concat ","
            $"// MCZ([{controlsStr}], {target}) - transpile to OpenQASM 2.0 compatible gates"
        | CP (c, t, theta) -> $"cp({theta}) q[{c}],q[{t}];"
        | SWAP (q1, q2) -> $"swap q[{q1}],q[{q2}];"
        | CCX (c1, c2, t) -> $"ccx q[{c1}],q[{c2}],q[{t}];"
        | RX (q, angle) -> $"rx({angle}) q[{q}];"
        | RY (q, angle) -> $"ry({angle}) q[{q}];"
        | RZ (q, angle) -> $"rz({angle}) q[{q}];"
        | Measure q -> $"measure q[{q}];"

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
        | P _ -> "P"
        | RX _ -> "Rx"
        | RY _ -> "Ry"
        | RZ _ -> "Rz"
        | CNOT _ -> "CNOT"
        | CZ _ -> "CZ"
        | CP _ -> "CP"
        | SWAP _ -> "SWAP"
        | CCX _ -> "CCX"
        | MCZ _ -> "MCZ"
        | Measure _ -> "Measure"
    
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
            | X q | Y q | Z q | H q | S q | SDG q | T q | TDG q | Measure q ->
                validateQubit q |> Option.toList
            | RX (q, _) | RY (q, _) | RZ (q, _) | P (q, _) ->
                validateQubit q |> Option.toList
            | CNOT (control, target) | CZ (control, target) | CP (control, target, _) ->
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
            | MCZ (controls, target) ->
                let errors = []
                // Validate all control qubits
                let errors = 
                    controls
                    |> List.fold (fun acc c ->
                        match validateQubit c with
                        | Some err -> err :: acc
                        | None -> acc
                    ) errors
                // Validate target qubit
                let errors = 
                    match validateQubit target with
                    | Some err -> err :: errors
                    | None -> errors
                // Check all qubits are distinct
                let allQubits = target :: controls
                let errors = 
                    if List.length allQubits <> (Set.ofList allQubits |> Set.count) then
                        "MCZ control and target qubits must all be distinct" :: errors
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

    // ============================================================================
    // COMPUTATION EXPRESSION BUILDER - Quantum Circuit Builder
    // ============================================================================
    
    /// <summary>
    /// Computation expression builder for constructing quantum circuits declaratively.
    /// Enables natural gate composition with custom operations.
    /// </summary>
    /// 
    /// <example>
    /// <code>
    /// // Bell state (2-qubit entanglement)
    /// let bell = circuit {
    ///     qubits 2
    ///     H 0
    ///     CNOT 0 1
    /// }
    /// 
    /// // GHZ state (use programmatic API for loops)
    /// let ghz = 
    ///     let mutable c = empty 5
    ///     c &lt;- addGate (H 0) c
    ///     for i in [0..3] do
    ///         c &lt;- addGate (CNOT (i, i+1)) c
    ///     c
    /// 
    /// // Or use the For method directly via helper function
    /// let ghzWithFor qubitCount =
    ///     circuit {
    ///         qubits qubitCount
    ///         H 0
    ///     }
    ///     |> fun baseCircuit ->
    ///         let cnots = [0..qubitCount-2] |> List.map (fun i -> CNOT (i, i+1))
    ///         addGates cnots baseCircuit
    /// </code>
    /// </example>
    type CircuitBuilderCE() =
        
        /// <summary>Initialize an empty circuit</summary>
        member _.Yield(_) : Circuit =
            { QubitCount = 0; Gates = [] }
        
        /// <summary>Yield an existing circuit (enables yield! syntax)</summary>
        member _.YieldFrom(circuit: Circuit) : Circuit =
            circuit
        
        member _.Combine(circuit1: Circuit, circuit2: Circuit) : Circuit =
            // Compose circuits: merge gates and preserve qubit count
            let qubitCount = max circuit1.QubitCount circuit2.QubitCount
            {
                QubitCount = qubitCount
                Gates = circuit1.Gates @ circuit2.Gates
            }
        
        member _.Zero() : Circuit =
            { QubitCount = 0; Gates = [] }
        
        // Delay executes immediately (like FsCdk pattern)
        member inline _.Delay([<InlineIfLambda>] f: unit -> Circuit) : Circuit = f()
        
        // For method for delayed execution patterns (Delay/Run interaction)
        member inline this.For(circuit: Circuit, [<InlineIfLambda>] f: unit -> Circuit) : Circuit =
            this.Combine(circuit, f())
        
        member this.For(sequence: seq<'T>, body: 'T -> Circuit) : Circuit =
            let mutable state = this.Zero()
            for item in sequence do
                let itemCircuit = body item
                state <- this.Combine(state, itemCircuit)
            state
        
        member _.Run(circuit: Circuit) : Circuit =
            // Validate circuit before returning
            match validate circuit with
            | result when result.IsValid -> circuit
            | result -> failwithf "Invalid circuit: %s" (System.String.Join("; ", result.Messages))
        
        // ========================================================================
        // CUSTOM OPERATIONS - Circuit Configuration and Gates
        // ========================================================================
        
        /// <summary>
        /// Set the number of qubits in the circuit.
        /// This is typically the first operation in a circuit definition.
        /// </summary>
        /// <param name="count">Total number of qubits (indexed 0 to count-1)</param>
        [<CustomOperation("qubits")>]
        member _.Qubits(circuit: Circuit, count: int) : Circuit =
            { circuit with QubitCount = count }
        
        // ========================================================================
        // SINGLE-QUBIT GATES - Custom operations for common gates
        // ========================================================================
        
        /// Apply Hadamard gate to qubit
        [<CustomOperation("H")>]
        member _.H(circuit: Circuit, qubit: int) : Circuit =
            { circuit with Gates = circuit.Gates @ [H qubit] }
        
        /// Apply Pauli-X (NOT) gate to qubit
        [<CustomOperation("X")>]
        member _.X(circuit: Circuit, qubit: int) : Circuit =
            { circuit with Gates = circuit.Gates @ [X qubit] }
        
        /// Apply Pauli-Y gate to qubit
        [<CustomOperation("Y")>]
        member _.Y(circuit: Circuit, qubit: int) : Circuit =
            { circuit with Gates = circuit.Gates @ [Y qubit] }
        
        /// Apply Pauli-Z gate to qubit
        [<CustomOperation("Z")>]
        member _.Z(circuit: Circuit, qubit: int) : Circuit =
            { circuit with Gates = circuit.Gates @ [Z qubit] }
        
        /// Apply S gate (phase gate, √Z) to qubit
        [<CustomOperation("S")>]
        member _.S(circuit: Circuit, qubit: int) : Circuit =
            { circuit with Gates = circuit.Gates @ [S qubit] }
        
        /// Apply S-dagger (S†, inverse phase) to qubit
        [<CustomOperation("SDG")>]
        member _.SDG(circuit: Circuit, qubit: int) : Circuit =
            { circuit with Gates = circuit.Gates @ [SDG qubit] }
        
        /// Apply T gate (π/8 gate, √S) to qubit
        [<CustomOperation("T")>]
        member _.T(circuit: Circuit, qubit: int) : Circuit =
            { circuit with Gates = circuit.Gates @ [T qubit] }
        
        /// Apply T-dagger (T†, inverse π/8) to qubit
        [<CustomOperation("TDG")>]
        member _.TDG(circuit: Circuit, qubit: int) : Circuit =
            { circuit with Gates = circuit.Gates @ [TDG qubit] }
        
        /// Apply phase gate P(θ) to qubit
        [<CustomOperation("P")>]
        member _.P(circuit: Circuit, qubit: int, angle: float) : Circuit =
            { circuit with Gates = circuit.Gates @ [P (qubit, angle)] }

        /// Apply phase gate P(θ) to qubit, tuple
        [<CustomOperation("P")>]
        member _.P(circuit: Circuit, qubitAndAngle: int*float) : Circuit =
            { circuit with Gates = circuit.Gates @ [P qubitAndAngle] }
        
        
        // ========================================================================
        // ROTATION GATES
        // ========================================================================
        
        /// Apply RX rotation (around X-axis) to qubit
        [<CustomOperation("RX")>]
        member _.RX(circuit: Circuit, qubit: int, angle: float) : Circuit =
            { circuit with Gates = circuit.Gates @ [RX (qubit, angle)] }

        /// Apply RX rotation (around X-axis) to qubit
        [<CustomOperation("RX")>]
        member _.RX(circuit: Circuit, qubitAndangle: int*float) : Circuit =
            { circuit with Gates = circuit.Gates @ [RX qubitAndangle] }
        
        /// Apply RY rotation (around Y-axis) to qubit
        [<CustomOperation("RY")>]
        member _.RY(circuit: Circuit, qubit: int, angle: float) : Circuit =
            { circuit with Gates = circuit.Gates @ [RY (qubit, angle)] }

        /// Apply RY rotation (around Y-axis) to qubit
        [<CustomOperation("RY")>]
        member _.RY(circuit: Circuit, qubitAndAngle: int*float) : Circuit =
            { circuit with Gates = circuit.Gates @ [RY qubitAndAngle] }
        
        /// Apply RZ rotation (around Z-axis) to qubit
        [<CustomOperation("RZ")>]
        member _.RZ(circuit: Circuit, qubit: int, angle: float) : Circuit =
            { circuit with Gates = circuit.Gates @ [RZ (qubit, angle)] }

        /// Apply RZ rotation (around Z-axis) to qubit
        [<CustomOperation("RZ")>]
        member _.RZ(circuit: Circuit, qubitAndAngle: int*float) : Circuit =
            { circuit with Gates = circuit.Gates @ [RZ qubitAndAngle] }
        
        // ========================================================================
        // TWO-QUBIT GATES
        // ========================================================================
        
        /// Apply CNOT (controlled-NOT) gate, control and target (separately)
        [<CustomOperation("CNOT")>]
        member _.CNOT(circuit: Circuit, control: int, target: int) : Circuit =
            { circuit with Gates = circuit.Gates @ [CNOT (control, target)] }

        /// Apply CNOT (controlled-NOT) gate (control and target, tuple)
        [<CustomOperation("CNOT")>]
        member _.CNOT(circuit: Circuit, controlAndTarget: int*int) : Circuit =
            { circuit with Gates = circuit.Gates @ [CNOT controlAndTarget] }
        
        
        /// Apply CZ (controlled-Z) gate
        [<CustomOperation("CZ")>]
        member _.CZ(circuit: Circuit, control: int, target: int) : Circuit =
            { circuit with Gates = circuit.Gates @ [CZ (control, target)] }

        /// Apply CZ (controlled-Z) gate
        [<CustomOperation("CZ")>]
        member _.CZ(circuit: Circuit, controlAndTarget: int*int) : Circuit =
            { circuit with Gates = circuit.Gates @ [CZ controlAndTarget] }
        
        
        /// Apply controlled-phase gate CP(θ)
        [<CustomOperation("CP")>]
        member _.CP(circuit: Circuit, control: int, target: int, angle: float) : Circuit =
            { circuit with Gates = circuit.Gates @ [CP (control, target, angle)] }

        /// Apply controlled-phase gate CP(θ)
        [<CustomOperation("CP")>]
        member _.CP(circuit: Circuit, controlTargetAngle: int*int*float) : Circuit =
            { circuit with Gates = circuit.Gates @ [CP controlTargetAngle] }
        
        /// Apply SWAP gate to exchange two qubits
        [<CustomOperation("SWAP")>]
        member _.SWAP(circuit: Circuit, qubit1: int, qubit2: int) : Circuit =
            { circuit with Gates = circuit.Gates @ [SWAP (qubit1, qubit2)] }

        /// Apply SWAP gate to exchange two qubits
        [<CustomOperation("SWAP")>]
        member _.SWAP(circuit: Circuit, qubit1and2: int*int) : Circuit =
            { circuit with Gates = circuit.Gates @ [SWAP qubit1and2] }
        
        
        // ========================================================================
        // THREE-QUBIT GATES
        // ========================================================================
        
        /// Apply CCX (Toffoli, CCNOT) gate
        [<CustomOperation("CCX")>]
        member _.CCX(circuit: Circuit, control1: int, control2: int, target: int) : Circuit =
            { circuit with Gates = circuit.Gates @ [CCX (control1, control2, target)] }
        
        /// Apply CCX (Toffoli, CCNOT) gate
        [<CustomOperation("CCX")>]
        member _.CCX(circuit: Circuit, control1control2target: int*int*int) : Circuit =
            { circuit with Gates = circuit.Gates @ [CCX control1control2target] }
        
        // ========================================================================
        // HELPER FOR FOR-LOOPS
        // ========================================================================
        
        /// Apply a gate (useful in for loops where custom operations don't work directly)
        [<CustomOperation("gate")>]
        member _.Gate(circuit: Circuit, g: Gate) : Circuit =
            { circuit with Gates = circuit.Gates @ [g] }
    
    /// Global computation expression instance for circuit construction
    let circuit = CircuitBuilderCE()
    
    // ============================================================================
    // HELPER FUNCTIONS FOR USE INSIDE FOR LOOPS
    // ============================================================================
    // These functions return single-gate circuits that can be used in for loop bodies
    // Example: for i in [0..3] do yield! singleGate (CNOT (i, i+1))
    
    /// Creates a circuit with a single gate (useful for for loops)
    /// Use with yield! inside for loops: for i in [0..3] do yield! singleGate (CNOT (i, i+1))
    let singleGate (gate: Gate) : Circuit =
        { QubitCount = 0; Gates = [gate] }
    
    /// Creates a circuit with multiple gates (useful for for loops)
    /// Use with yield! inside for loops: for gates in gateList do yield! multiGate gates
    let multiGate (gates: Gate list) : Circuit =
        { QubitCount = 0; Gates = gates }
    
    // ============================================================================
    // GATE CONSTRUCTOR HELPERS - For use in for loops
    // ============================================================================
    // These helpers create gate values that can be wrapped in singleGate()
    // They mirror the Gate union cases but are functions, making them easier to use
    
    /// Creates an H (Hadamard) gate - for use in for loops
    let h q = H q
    
    /// Creates an X (NOT) gate - for use in for loops
    let x q = X q
    
    /// Creates a Y gate - for use in for loops
    let y q = Y q
    
    /// Creates a Z gate - for use in for loops
    let z q = Z q
    
    /// Creates an S (phase) gate - for use in for loops
    let s q = S q
    
    /// Creates an SDG (inverse phase) gate - for use in for loops
    let sdg q = SDG q
    
    /// Creates a T gate - for use in for loops
    let t q = T q
    
    /// Creates a TDG (inverse T) gate - for use in for loops
    let tdg q = TDG q
    
    /// Creates a P (phase) gate with angle - for use in for loops
    let p q angle = P (q, angle)
    
    /// Creates an RX (X-rotation) gate - for use in for loops
    let rx q angle = RX (q, angle)
    
    /// Creates an RY (Y-rotation) gate - for use in for loops
    let ry q angle = RY (q, angle)
    
    /// Creates an RZ (Z-rotation) gate - for use in for loops
    let rz q angle = RZ (q, angle)
    
    /// Creates a CNOT gate - for use in for loops
    let cnot control target = CNOT (control, target)
    
    /// Creates a CZ gate - for use in for loops
    let cz control target = CZ (control, target)
    
    /// Creates a CP (controlled phase) gate - for use in for loops
    let cp control target angle = CP (control, target, angle)
    
    /// Creates a SWAP gate - for use in for loops
    let swap q1 q2 = SWAP (q1, q2)
    
    /// Creates a CCX (Toffoli) gate - for use in for loops
    let ccx c1 c2 target = CCX (c1, c2, target)
    
    /// Creates an MCZ (multi-controlled Z) gate - for use in for loops
    let mcz controls target = MCZ (controls, target)
