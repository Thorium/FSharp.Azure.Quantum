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
        
        // Universal single-qubit gate
        | U3 of int * float * float * float  // U3(θ, φ, λ) = RZ(φ) RY(θ) RZ(λ) on qubit
        
        // Two-qubit gates
        | CNOT of int * int           // Controlled-NOT (control, target)
        | CZ of int * int             // Controlled-Z (control, target)
        | CP of int * int * float     // Controlled-P gate: CP(θ) applies P(θ) when control is |1⟩
        | CRX of int * int * float    // Controlled-RX: CRX(θ) applies RX(θ) when control is |1⟩
        | CRY of int * int * float    // Controlled-RY: CRY(θ) applies RY(θ) when control is |1⟩
        | CRZ of int * int * float    // Controlled-RZ: CRZ(θ) applies RZ(θ) when control is |1⟩
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
    /// Internally prepends for O(1) performance; gates are reversed at output boundaries
    let addGate (gate: Gate) (circuit: Circuit) : Circuit =
        { circuit with Gates = gate :: circuit.Gates }

    /// Adds multiple gates to the end of a circuit
    /// Internally prepends reversed for O(n) where n = new gates; gates are reversed at output boundaries
    let addGates (gates: Gate list) (circuit: Circuit) : Circuit =
        { circuit with Gates = (List.rev gates) @ circuit.Gates }

    /// Adds a measurement operation to the end of a circuit
    let addMeasurement (qubit: int) (circuit: Circuit) : Circuit =
        addGate (Measure qubit) circuit

    /// Composes two circuits by appending the gates of the second to the first
    let compose (circuit1: Circuit) (circuit2: Circuit) : Circuit =
        if circuit1.QubitCount <> circuit2.QubitCount then
            failwith "Cannot compose circuits with different qubit counts"
        { circuit1 with Gates = circuit2.Gates @ circuit1.Gates }

    /// Gets all gates from a circuit in order
    let getGates (circuit: Circuit) : Gate list =
        List.rev circuit.Gates

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
        
        // Gates are stored in reverse order internally; reverse for optimization, then re-reverse
        let forwardGates = List.rev circuit.Gates
        let optimized = optimizeGates forwardGates
        { circuit with Gates = List.rev optimized }

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
        | CRX (c, t, theta) -> $"crx({theta}) q[{c}],q[{t}];"
        | CRY (c, t, theta) -> $"cry({theta}) q[{c}],q[{t}];"
        | CRZ (c, t, theta) -> $"crz({theta}) q[{c}],q[{t}];"
        | SWAP (q1, q2) -> $"swap q[{q1}],q[{q2}];"
        | CCX (c1, c2, t) -> $"ccx q[{c1}],q[{c2}],q[{t}];"
        | RX (q, angle) -> $"rx({angle}) q[{q}];"
        | RY (q, angle) -> $"ry({angle}) q[{q}];"
        | RZ (q, angle) -> $"rz({angle}) q[{q}];"
        | U3 (q, theta, phi, lambda) -> $"u3({theta},{phi},{lambda}) q[{q}];"
        | Measure q -> $"measure q[{q}];"

    /// Converts a circuit to OpenQASM 2.0 format for Azure Quantum submission
    let toOpenQASM (circuit: Circuit) : string =
        let header = "OPENQASM 2.0;\ninclude \"qelib1.inc\";"
        let qregDecl = $"qreg q[{circuit.QubitCount}];"
        let gateLines = circuit.Gates |> List.rev |> List.map gateToQASM
        
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
        | U3 _ -> "U3"
        | CNOT _ -> "CNOT"
        | CZ _ -> "CZ"
        | CP _ -> "CP"
        | CRX _ -> "CRX"
        | CRY _ -> "CRY"
        | CRZ _ -> "CRZ"
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
            | RX (q, _) | RY (q, _) | RZ (q, _) | P (q, _) | U3 (q, _, _, _) ->
                validateQubit q |> Option.toList
            | CNOT (control, target) | CZ (control, target) | CP (control, target, _) 
            | CRX (control, target, _) | CRY (control, target, _) | CRZ (control, target, _) ->
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
            |> List.rev
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
            // Gates are stored in reverse order, so circuit2's gates (which come after)
            // should be prepended to circuit1's gates
            let qubitCount = max circuit1.QubitCount circuit2.QubitCount
            {
                QubitCount = qubitCount
                Gates = circuit2.Gates @ circuit1.Gates
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
            { circuit with Gates = (H qubit) :: circuit.Gates }
        
        /// Apply Pauli-X (NOT) gate to qubit
        [<CustomOperation("X")>]
        member _.X(circuit: Circuit, qubit: int) : Circuit =
            { circuit with Gates = (X qubit) :: circuit.Gates }
        
        /// Apply Pauli-Y gate to qubit
        [<CustomOperation("Y")>]
        member _.Y(circuit: Circuit, qubit: int) : Circuit =
            { circuit with Gates = (Y qubit) :: circuit.Gates }
        
        /// Apply Pauli-Z gate to qubit
        [<CustomOperation("Z")>]
        member _.Z(circuit: Circuit, qubit: int) : Circuit =
            { circuit with Gates = (Z qubit) :: circuit.Gates }
        
        /// Apply S gate (phase gate, √Z) to qubit
        [<CustomOperation("S")>]
        member _.S(circuit: Circuit, qubit: int) : Circuit =
            { circuit with Gates = (S qubit) :: circuit.Gates }
        
        /// Apply S-dagger (S†, inverse phase) to qubit
        [<CustomOperation("SDG")>]
        member _.SDG(circuit: Circuit, qubit: int) : Circuit =
            { circuit with Gates = (SDG qubit) :: circuit.Gates }
        
        /// Apply T gate (π/8 gate, √S) to qubit
        [<CustomOperation("T")>]
        member _.T(circuit: Circuit, qubit: int) : Circuit =
            { circuit with Gates = (T qubit) :: circuit.Gates }
        
        /// Apply T-dagger (T†, inverse π/8) to qubit
        [<CustomOperation("TDG")>]
        member _.TDG(circuit: Circuit, qubit: int) : Circuit =
            { circuit with Gates = (TDG qubit) :: circuit.Gates }
        
        /// Apply phase gate P(θ) to qubit
        [<CustomOperation("P")>]
        member _.P(circuit: Circuit, qubit: int, angle: float) : Circuit =
            { circuit with Gates = (P (qubit, angle)) :: circuit.Gates }

        /// Apply phase gate P(θ) to qubit, tuple
        [<CustomOperation("P")>]
        member _.P(circuit: Circuit, qubitAndAngle: int*float) : Circuit =
            { circuit with Gates = (P qubitAndAngle) :: circuit.Gates }
        
        
        // ========================================================================
        // ROTATION GATES
        // ========================================================================
        
        /// Apply RX rotation (around X-axis) to qubit
        [<CustomOperation("RX")>]
        member _.RX(circuit: Circuit, qubit: int, angle: float) : Circuit =
            { circuit with Gates = (RX (qubit, angle)) :: circuit.Gates }

        /// Apply RX rotation (around X-axis) to qubit
        [<CustomOperation("RX")>]
        member _.RX(circuit: Circuit, qubitAndangle: int*float) : Circuit =
            { circuit with Gates = (RX qubitAndangle) :: circuit.Gates }
        
        /// Apply RY rotation (around Y-axis) to qubit
        [<CustomOperation("RY")>]
        member _.RY(circuit: Circuit, qubit: int, angle: float) : Circuit =
            { circuit with Gates = (RY (qubit, angle)) :: circuit.Gates }

        /// Apply RY rotation (around Y-axis) to qubit
        [<CustomOperation("RY")>]
        member _.RY(circuit: Circuit, qubitAndAngle: int*float) : Circuit =
            { circuit with Gates = (RY qubitAndAngle) :: circuit.Gates }
        
        /// Apply RZ rotation (around Z-axis) to qubit
        [<CustomOperation("RZ")>]
        member _.RZ(circuit: Circuit, qubit: int, angle: float) : Circuit =
            { circuit with Gates = (RZ (qubit, angle)) :: circuit.Gates }

        /// Apply RZ rotation (around Z-axis) to qubit
        [<CustomOperation("RZ")>]
        member _.RZ(circuit: Circuit, qubitAndAngle: int*float) : Circuit =
            { circuit with Gates = (RZ qubitAndAngle) :: circuit.Gates }
        
        // ========================================================================
        // TWO-QUBIT GATES
        // ========================================================================
        
        /// Apply CNOT (controlled-NOT) gate, control and target (separately)
        [<CustomOperation("CNOT")>]
        member _.CNOT(circuit: Circuit, control: int, target: int) : Circuit =
            { circuit with Gates = (CNOT (control, target)) :: circuit.Gates }

        /// Apply CNOT (controlled-NOT) gate (control and target, tuple)
        [<CustomOperation("CNOT")>]
        member _.CNOT(circuit: Circuit, controlAndTarget: int*int) : Circuit =
            { circuit with Gates = (CNOT controlAndTarget) :: circuit.Gates }
        
        
        /// Apply CZ (controlled-Z) gate
        [<CustomOperation("CZ")>]
        member _.CZ(circuit: Circuit, control: int, target: int) : Circuit =
            { circuit with Gates = (CZ (control, target)) :: circuit.Gates }

        /// Apply CZ (controlled-Z) gate
        [<CustomOperation("CZ")>]
        member _.CZ(circuit: Circuit, controlAndTarget: int*int) : Circuit =
            { circuit with Gates = (CZ controlAndTarget) :: circuit.Gates }
        
        
        /// Apply controlled-phase gate CP(θ)
        [<CustomOperation("CP")>]
        member _.CP(circuit: Circuit, control: int, target: int, angle: float) : Circuit =
            { circuit with Gates = (CP (control, target, angle)) :: circuit.Gates }

        /// Apply controlled-phase gate CP(θ)
        [<CustomOperation("CP")>]
        member _.CP(circuit: Circuit, controlTargetAngle: int*int*float) : Circuit =
            { circuit with Gates = (CP controlTargetAngle) :: circuit.Gates }
        
        /// Apply SWAP gate to exchange two qubits
        [<CustomOperation("SWAP")>]
        member _.SWAP(circuit: Circuit, qubit1: int, qubit2: int) : Circuit =
            { circuit with Gates = (SWAP (qubit1, qubit2)) :: circuit.Gates }

        /// Apply SWAP gate to exchange two qubits
        [<CustomOperation("SWAP")>]
        member _.SWAP(circuit: Circuit, qubit1and2: int*int) : Circuit =
            { circuit with Gates = (SWAP qubit1and2) :: circuit.Gates }
        
        
        // ========================================================================
        // THREE-QUBIT GATES
        // ========================================================================
        
        /// Apply CCX (Toffoli, CCNOT) gate
        [<CustomOperation("CCX")>]
        member _.CCX(circuit: Circuit, control1: int, control2: int, target: int) : Circuit =
            { circuit with Gates = (CCX (control1, control2, target)) :: circuit.Gates }
        
        /// Apply CCX (Toffoli, CCNOT) gate
        [<CustomOperation("CCX")>]
        member _.CCX(circuit: Circuit, control1control2target: int*int*int) : Circuit =
            { circuit with Gates = (CCX control1control2target) :: circuit.Gates }
        
        // ========================================================================
        // HELPER FOR FOR-LOOPS
        // ========================================================================
        
        /// Apply a gate (useful in for loops where custom operations don't work directly)
        [<CustomOperation("gate")>]
        member _.Gate(circuit: Circuit, g: Gate) : Circuit =
            { circuit with Gates = g :: circuit.Gates }
    
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
    
    // ========================================================================
    // COMPOSITION OPERATORS (Functional Extensions)
    // ========================================================================
    
    /// Sequential composition operator (circuit1 followed by circuit2)
    /// Alias for existing `compose` function
    /// Example: let combined = bellState ++ measurement
    let (++) = compose
    
    /// Right-pipe composition for fluent gate addition
    /// Example: circuit |+ H 0 |+ CNOT(0, 1)
    let (|+) (circuit: Circuit) (gate: Gate) : Circuit =
        addGate gate circuit
    
    /// Apply circuit transformation
    /// Example: circuit |>> optimize |>> validate
    let (|>>) (circuit: Circuit) (transform: Circuit -> Circuit) : Circuit =
        transform circuit
    
    // ========================================================================
    // HIGHER-ORDER FUNCTIONS (Functional Combinators)
    // ========================================================================
    
    /// Map function over all gates
    /// Example: mapGates (fun g -> shiftQubit g 5) circuit
    let mapGates (f: Gate -> Gate) (circuit: Circuit) : Circuit =
        { circuit with Gates = List.map f circuit.Gates }
    
    /// Filter gates by predicate
    /// Example: filterGates (fun g -> not (isMeasurement g)) circuit
    let filterGates (predicate: Gate -> bool) (circuit: Circuit) : Circuit =
        { circuit with Gates = List.filter predicate circuit.Gates }
    
    /// Fold over gates (left fold)
    /// Example: foldGates (fun count gate -> count + 1) 0 circuit
    let foldGates (folder: 'State -> Gate -> 'State) (state: 'State) (circuit: Circuit) : 'State =
        circuit.Gates |> List.rev |> List.fold folder state
    
    /// Count gates matching predicate
    /// Example: countGatesWhere (fun g -> match g with T _ -> true | _ -> false) circuit
    let countGatesWhere (predicate: Gate -> bool) (circuit: Circuit) : int =
        circuit.Gates |> List.filter predicate |> List.length
    
    /// Reverse circuit (creates inverse/adjoint circuit)
    /// Useful for uncomputation in quantum algorithms
    let reverse (circuit: Circuit) : Circuit =
        let inverseGate (gate: Gate) : Gate =
            match gate with
            // Self-inverse gates
            | H q -> H q
            | X q -> X q
            | Y q -> Y q
            | Z q -> Z q
            | CNOT (c, t) -> CNOT (c, t)
            | CZ (c, t) -> CZ (c, t)
            | SWAP (q1, q2) -> SWAP (q1, q2)
            | CCX (c1, c2, t) -> CCX (c1, c2, t)
            | MCZ (controls, target) -> MCZ (controls, target)
            
            // Inverse pairs
            | S q -> SDG q
            | SDG q -> S q
            | T q -> TDG q
            | TDG q -> T q
            
            // Rotations (negate angle)
            | P (q, theta) -> P (q, -theta)
            | RX (q, theta) -> RX (q, -theta)
            | RY (q, theta) -> RY (q, -theta)
            | RZ (q, theta) -> RZ (q, -theta)
            | CP (c, t, theta) -> CP (c, t, -theta)
            | CRX (c, t, theta) -> CRX (c, t, -theta)
            | CRY (c, t, theta) -> CRY (c, t, -theta)
            | CRZ (c, t, theta) -> CRZ (c, t, -theta)
            
            // U3 gate (negate all angles for inverse)
            | U3 (q, theta, phi, lambda) -> U3 (q, -theta, -lambda, -phi)
            
            // Measurement not reversible (approximate as identity)
            | Measure q -> Measure q
        
        // Gates are stored in reverse chronological order internally.
        // Adjoint of [A, B, C] (forward) is [C†, B†, A†] (forward).
        // Stored reversed: original is [C, B, A], adjoint stored reversed is [A†, B†, C†].
        // Mapping inverseGate over [C, B, A] gives [C†, B†, A†] which is already the
        // correct reversed storage for the adjoint — no List.rev needed.
        { circuit with Gates = circuit.Gates |> List.map inverseGate }
    
    // ========================================================================
    // COMMON CIRCUIT PATTERNS (Quantum Subroutines)
    // ========================================================================
    
    /// Create Bell state circuit: |Φ+⟩ = (|00⟩ + |11⟩)/√2
    /// The fundamental two-qubit entangled state
    let bellState (q1: int) (q2: int) : Circuit =
        empty (max q1 q2 + 1)
        |> addGate (H q1)
        |> addGate (CNOT(q1, q2))
    
    /// Create GHZ state: |GHZ_n⟩ = (|000...0⟩ + |111...1⟩)/√2
    /// Maximally entangled state for n qubits
    let ghzState (qubits: int list) : Circuit =
        match qubits with
        | [] -> empty 0
        | first :: rest ->
            let n = List.length qubits
            let circuit = empty n |> addGate (H first)
            rest |> List.fold (fun c q -> c |> addGate (CNOT(first, q))) circuit
    
    /// Create W state: |W_n⟩ = (|100...0⟩ + |010...0⟩ + ... + |000...1⟩)/√n
    /// Another type of multi-qubit entangled state (not equivalent to GHZ)
    /// 
    /// Algorithm: Start with |100...0⟩, then use controlled rotations to
    /// distribute the single excitation uniformly across all qubits.
    /// For each step i (0..n-2): apply RY(2·arccos(√(1/(n-i)))) controlled
    /// on qubit i targeting qubit i+1, then CNOT(i+1, i) to transfer the excitation.
    let wState (n: int) : Circuit =
        if n < 2 then
            empty (max n 1) |> addGate (X 0)
        else
            let mutable c = empty n |> addGate (X 0)
            for i in 0 .. n - 2 do
                // Rotation angle: 2·arccos(√(1/(n-i)))
                // This gives the correct amplitude split at each step
                let angle = 2.0 * System.Math.Acos(System.Math.Sqrt(1.0 / float (n - i)))
                c <- c |> addGate (CRY(i, i + 1, angle))
                c <- c |> addGate (CNOT(i + 1, i))
            c
    
    /// Quantum Fourier Transform (QFT) on specified qubits
    /// The quantum analogue of the discrete Fourier transform
    let qft (qubits: int list) : Circuit =
        let n = List.length qubits
        if n = 0 then
            empty 0
        else
            let maxQubit = List.max qubits
            let circuit = empty (maxQubit + 1)
            
            // QFT algorithm: for each qubit, apply H then controlled rotations
            List.indexed qubits
            |> List.fold (fun c (i, q) ->
                // Apply Hadamard
                let c1 = c |> addGate (H q)
                
                // Apply controlled phase rotations
                qubits
                |> List.skip (i + 1)
                |> List.indexed
                |> List.fold (fun c2 (j, target) ->
                    let k = j + 1
                    let angle = System.Math.PI / (2.0 ** float k)
                    c2 |> addGate (CP(q, target, angle))
                ) c1
            ) circuit
    
    /// Inverse Quantum Fourier Transform
    /// Used in quantum phase estimation and Shor's algorithm
    let inverseQFT (qubits: int list) : Circuit =
        qft qubits |> reverse
    
    /// SWAP gate using 3 CNOTs (for demonstration/education)
    /// Note: SWAP is a primitive gate, but this shows decomposition
    let swapViaCNOT (q1: int) (q2: int) : Circuit =
        empty (max q1 q2 + 1)
        |> addGate (CNOT(q1, q2))
        |> addGate (CNOT(q2, q1))
        |> addGate (CNOT(q1, q2))
    
    /// Toffoli (CCX) gate using Clifford+T decomposition
    /// Standard fault-tolerant decomposition: 7 T gates, 8 CNOTs, 2 H
    /// Critical for understanding fault-tolerant quantum computation cost
    let toffoliViaCliffordT (c1: int) (c2: int) (target: int) : Circuit =
        let maxQubit = max c1 (max c2 target)
        empty (maxQubit + 1)
        |> addGate (H target)
        |> addGate (CNOT(c2, target))
        |> addGate (TDG target)
        |> addGate (CNOT(c1, target))
        |> addGate (T target)
        |> addGate (CNOT(c2, target))
        |> addGate (TDG target)
        |> addGate (CNOT(c1, target))
        |> addGate (T c2)
        |> addGate (T target)
        |> addGate (CNOT(c1, c2))
        |> addGate (H target)
        |> addGate (T c1)
        |> addGate (TDG c2)
        |> addGate (CNOT(c1, c2))
    
    // ========================================================================
    // GATE COMMUTATION ANALYSIS (For Optimization)
    // ========================================================================
    
    /// Get qubits affected by a gate
    let private getAffectedQubits (gate: Gate) : int list =
        match gate with
        | X q | Y q | Z q | H q -> [q]
        | S q | SDG q | T q | TDG q -> [q]
        | P (q, _) | RX (q, _) | RY (q, _) | RZ (q, _) -> [q]
        | U3 (q, _, _, _) -> [q]
        | CNOT (c, t) | CZ (c, t) -> [c; t]
        | CP (c, t, _) -> [c; t]
        | CRX (c, t, _) -> [c; t]
        | CRY (c, t, _) -> [c; t]
        | CRZ (c, t, _) -> [c; t]
        | SWAP (c, t) -> [c; t]
        | CCX (c1, c2, t) -> [c1; c2; t]
        | MCZ (controls, target) -> controls @ [target]
        | Measure q -> [q]
    
    /// Check if two gates act on disjoint qubits (always commute)
    let private areDisjoint (gate1: Gate) (gate2: Gate) : bool =
        let qubits1 = getAffectedQubits gate1 |> Set.ofList
        let qubits2 = getAffectedQubits gate2 |> Set.ofList
        Set.intersect qubits1 qubits2 |> Set.isEmpty
    
    /// Check if two gates commute (can be safely reordered)
    /// Used for optimization: reordering gates to cancel inverses
    let commute (gate1: Gate) (gate2: Gate) : bool =
        // Disjoint gates always commute
        if areDisjoint gate1 gate2 then
            true
        else
            match gate1, gate2 with
            // Z gates commute with each other on same qubit
            | Z q1, Z q2 when q1 = q2 -> true
            
            // Z commutes with CNOT when Z is on control qubit
            | Z c, CNOT (c', _) when c = c' -> true
            | CNOT (c', _), Z c when c = c' -> true
            
            // X commutes with CNOT when X is on target qubit  
            | X t, CNOT (_, t') when t = t' -> true
            | CNOT (_, t'), X t when t = t' -> true
            
            // CZ is symmetric - commutes with Z on either qubit
            | Z q, CZ (c, t) when q = c || q = t -> true
            | CZ (c, t), Z q when q = c || q = t -> true
            
            // CZ gates commute with each other on same qubits (order doesn't matter)
            | CZ (c1, t1), CZ (c2, t2) when (c1 = c2 && t1 = t2) || (c1 = t2 && t1 = c2) -> true
            
            // RZ rotations commute with Z gates (both are Z-axis operations)
            | RZ (q1, _), Z q2 when q1 = q2 -> true
            | Z q1, RZ (q2, _) when q1 = q2 -> true
            
            // RZ rotations commute with each other (already merged by optimize)
            | RZ (q1, _), RZ (q2, _) when q1 = q2 -> true
            
            // Phase gates (S, T) commute with Z on same qubit
            | S q1, Z q2 when q1 = q2 -> true
            | Z q1, S q2 when q1 = q2 -> true
            | T q1, Z q2 when q1 = q2 -> true
            | Z q1, T q2 when q1 = q2 -> true
            
            // Default: gates don't commute (conservative approach)
            | _ -> false
    
    // ========================================================================
    // CIRCUIT STATISTICS (Useful for Rigetti workflows)
    // ========================================================================
    
    /// Circuit statistics for analysis and optimization
    type CircuitStatistics = {
        TotalGates: int
        SingleQubitGates: int
        TwoQubitGates: int
        MultiQubitGates: int
        MeasurementCount: int
        MaxQubitIndex: int
        GateTypeCounts: Map<string, int>
    }
    
    /// Compute comprehensive statistics for a circuit
    /// Useful for analyzing circuit complexity before Rigetti execution
    let statistics (circuit: Circuit) : CircuitStatistics =
        let gates = circuit.Gates
        
        let singleQubitCount = gates |> List.filter (fun g -> (getAffectedQubits g).Length = 1) |> List.length
        let twoQubitCount = gates |> List.filter (fun g -> (getAffectedQubits g).Length = 2) |> List.length
        let multiQubitCount = gates |> List.filter (fun g -> (getAffectedQubits g).Length > 2) |> List.length
        let measurementCount = gates |> List.filter (fun g -> match g with Measure _ -> true | _ -> false) |> List.length
        
        let maxQubit = 
            gates 
            |> List.collect getAffectedQubits
            |> function
                | [] -> 0
                | qubits -> List.max qubits
        
        let gateTypeCounts =
            gates
            |> List.groupBy getGateName
            |> List.map (fun (name, gateList) -> (name, List.length gateList))
            |> Map.ofList
        
        {
            TotalGates = gates.Length
            SingleQubitGates = singleQubitCount
            TwoQubitGates = twoQubitCount
            MultiQubitGates = multiQubitCount
            MeasurementCount = measurementCount
            MaxQubitIndex = maxQubit
            GateTypeCounts = gateTypeCounts
        }
    
    /// Count two-qubit gates (CNOT count is critical for NISQ devices like Rigetti)
    /// Two-qubit gates are 10-100x noisier than single-qubit gates
    let twoQubitGateCount (circuit: Circuit) : int =
        circuit.Gates
        |> List.filter (fun g -> (getAffectedQubits g).Length = 2)
        |> List.length
    
    /// Calculate circuit depth (critical path length when gates are parallelized)
    /// Lower depth = faster execution and less decoherence on real hardware
    let depth (circuit: Circuit) : int =
        // Gates stored in reverse order; reverse to get chronological for layer building
        let gates = circuit.Gates |> List.rev
        
        // Build layers by tracking which qubits are occupied
        let rec buildLayers (remainingGates: Gate list) (currentLayer: Gate list) (occupiedQubits: Set<int>) (currentDepth: int) : int =
            match remainingGates with
            | [] -> 
                // Finished all gates - return final depth
                if List.isEmpty currentLayer then currentDepth else currentDepth + 1
            
            | gate :: rest ->
                let qubits = getAffectedQubits gate |> Set.ofList
                
                // Check if gate can be added to current layer
                if Set.intersect qubits occupiedQubits |> Set.isEmpty then
                    // No conflict - add to current layer
                    buildLayers rest (gate :: currentLayer) (Set.union occupiedQubits qubits) currentDepth
                else
                    // Conflict - start new layer
                    buildLayers rest [gate] qubits (currentDepth + 1)
        
        buildLayers gates [] Set.empty 0
    
    // ========================================================================
    // ENHANCED OPTIMIZATION (Using Commutation)
    // ========================================================================
    
    /// Optimize circuit using commutation to bring inverse pairs together
    /// Example: H-X-H becomes X (X gates commute through, H-H cancel)
    let optimizeWithCommutation (circuit: Circuit) : Circuit =
        let rec trySwapGates (gates: Gate list) : Gate list option =
            match gates with
            | [] | [_] -> None
            | g1 :: g2 :: rest when commute g1 g2 ->
                // Found commuting pair - try swapping
                Some (g2 :: g1 :: rest)
            | firstGate :: rest ->
                // Try next position
                match trySwapGates rest with
                | Some optimized -> Some (firstGate :: optimized)
                | None -> None
        
        let rec optimizeLoop (gates: Gate list) (maxIterations: int) : Gate list =
            if maxIterations <= 0 then
                gates
            else
                // First try standard optimizations (inverse cancellation, rotation merging)
                let optimized1 = (optimize { circuit with Gates = gates }).Gates
                
                // Then try commutation-based swaps
                match trySwapGates optimized1 with
                | Some swapped ->
                    // Made progress - continue optimizing
                    optimizeLoop swapped (maxIterations - 1)
                | None ->
                    // No more commuting pairs to swap
                    optimized1
        
        { circuit with Gates = optimizeLoop circuit.Gates 10 }
    
    /// Remove identity gates (gates that do nothing)
    /// Example: RZ(0) is identity, can be removed
    let removeIdentities (circuit: Circuit) : Circuit =
        let isIdentity (gate: Gate) : bool =
            match gate with
            | RX (_, angle) | RY (_, angle) | RZ (_, angle) when abs angle < 1e-10 -> true
            | P (_, angle) when abs angle < 1e-10 -> true
            | CP (_, _, angle) when abs angle < 1e-10 -> true
            | _ -> false
        
        { circuit with Gates = circuit.Gates |> List.filter (not << isIdentity) }
    
    /// Full optimization pipeline combining all techniques
    /// Recommended before sending circuits to Rigetti hardware
    let optimizeFully (circuit: Circuit) : Circuit =
        circuit
        |> optimize                    // Inverse cancellation + rotation merging
        |> removeIdentities           // Remove RZ(0), etc.
        |> optimizeWithCommutation    // Use commutation to find more cancellations
        |> removeIdentities           // Clean up any new identities
