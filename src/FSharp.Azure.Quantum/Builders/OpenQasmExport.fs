/// TKT-97: OpenQASM 2.0 Export Module
/// 
/// Export quantum circuits to OpenQASM 2.0 format for IBM Qiskit compatibility.
/// Enables F# type-safe circuit construction with export to Qiskit ecosystem (6.7k stars).
///
/// ⚠️ CRITICAL: ALL OpenQASM export code consolidated in this SINGLE FILE
/// for AI context optimization.
///
/// ## Features
/// - Gate translation (X, Y, Z, H, S, SDG, T, TDG, RX, RY, RZ, CNOT, CZ, SWAP, CCX)
/// - Angle formatting with 10 decimal precision
/// - Circuit validation (qubit bounds checking)
/// - File export functionality
/// - Clear error messages
///
/// ## Usage Example
/// ```fsharp
/// let circuit = { QubitCount = 2; Gates = [H 0; CNOT (0, 1)] }
/// let qasmCode = OpenQasm.export circuit
/// OpenQasm.exportToFile circuit "bell_state.qasm"
/// ```
namespace FSharp.Azure.Quantum

open System
open System.IO
open System.Text

/// <summary>
/// OpenQASM 2.0 export module for IBM Qiskit compatibility.
/// </summary>
///
/// <remarks>
/// OpenQASM (Open Quantum Assembly Language) is the industry standard text-based format
/// for quantum circuits, primarily used by IBM Qiskit. This module enables users to build
/// circuits in F# with type safety and export them to the Qiskit ecosystem.
///
/// <para><b>Supported Gates:</b></para>
/// <list type="bullet">
///   <item>Pauli gates: X, Y, Z, H</item>
///   <item>Phase gates: S, SDG, T, TDG</item>
///   <item>Rotation gates: RX(θ), RY(θ), RZ(θ)</item>
///   <item>Two-qubit gates: CNOT (CX), CZ, SWAP</item>
///   <item>Three-qubit gates: CCX (Toffoli)</item>
/// </list>
///
/// <para><b>OpenQASM 2.0 Format:</b></para>
/// <code>
/// OPENQASM 2.0;
/// include "qelib1.inc";
/// qreg q[n];
/// h q[0];
/// cx q[0],q[1];
/// </code>
/// </remarks>
module OpenQasmExport =
    
    open CircuitBuilder
    
    // ========================================================================
    // CONSTANTS
    // ========================================================================
    
    /// OpenQASM version number
    let version = "2.0"
    
    /// Number of decimal places for angle formatting
    let private angleDecimalPlaces = 10
    
    // ========================================================================
    // ANGLE FORMATTING
    // ========================================================================
    
    /// <summary>
    /// Format angle with specified decimal precision for OpenQASM.
    /// </summary>
    ///
    /// <param name="angle">Angle in radians</param>
    /// <returns>Formatted string with 10 decimal places</returns>
    ///
    /// <example>
    /// <code>
    /// formatAngle 3.14159265358979 // Returns "3.1415926536"
    /// formatAngle 0.5              // Returns "0.5000000000"
    /// </code>
    /// </example>
    let private formatAngle (angle: float) : string =
        angle.ToString($"F{angleDecimalPlaces}")
    
    // ========================================================================
    // GATE TRANSLATION
    // ========================================================================
    
    /// <summary>
    /// Translate a single gate to OpenQASM 2.0 instruction.
    /// </summary>
    ///
    /// <param name="gate">Quantum gate to translate</param>
    /// <returns>OpenQASM 2.0 instruction string</returns>
    ///
    /// <remarks>
    /// Gate mappings:
    /// - X(q) → x q[q];
    /// - Y(q) → y q[q];
    /// - Z(q) → z q[q];
    /// - H(q) → h q[q];
    /// - S(q) → s q[q];
    /// - SDG(q) → sdg q[q];
    /// - T(q) → t q[q];
    /// - TDG(q) → tdg q[q];
    /// - CNOT(c,t) → cx q[c],q[t];
    /// - CZ(c,t) → cz q[c],q[t];
    /// - SWAP(q1,q2) → swap q[q1],q[q2];
    /// - CCX(c1,c2,t) → ccx q[c1],q[c2],q[t];
    /// - RX(q,θ) → rx(θ) q[q];
    /// - RY(q,θ) → ry(θ) q[q];
    /// - RZ(q,θ) → rz(θ) q[q];
    /// </remarks>
    let private gateToQasm (gate: Gate) : string =
        match gate with
        | X q -> 
            $"x q[{q}];"
        | Y q -> 
            $"y q[{q}];"
        | Z q -> 
            $"z q[{q}];"
        | H q -> 
            $"h q[{q}];"
        | S q -> 
            $"s q[{q}];"
        | SDG q -> 
            $"sdg q[{q}];"
        | T q -> 
            $"t q[{q}];"
        | TDG q -> 
            $"tdg q[{q}];"
        | CNOT (control, target) -> 
            $"cx q[{control}],q[{target}];"
        | CZ (control, target) -> 
            $"cz q[{control}],q[{target}];"
        | SWAP (q1, q2) -> 
            $"swap q[{q1}],q[{q2}];"
        | CCX (control1, control2, target) -> 
            $"ccx q[{control1}],q[{control2}],q[{target}];"
        | RX (q, angle) -> 
            $"rx({formatAngle angle}) q[{q}];"
        | RY (q, angle) -> 
            $"ry({formatAngle angle}) q[{q}];"
        | RZ (q, angle) -> 
            $"rz({formatAngle angle}) q[{q}];"
        | P (q, theta) -> 
            // P(θ) = phase gate = diag(1, e^(iθ))
            // In OpenQASM 2.0, this is the 'p' gate (also known as 'u1' in older versions)
            $"p({formatAngle theta}) q[{q}];"
        | CP (control, target, theta) -> 
            // CP(θ) = controlled-phase gate
            // In OpenQASM 2.0, this is 'cp' (controlled-p)
            $"cp({formatAngle theta}) q[{control}],q[{target}];"
        | CRX (control, target, theta) ->
            // CRX(θ) = controlled-RX gate
            // In OpenQASM 3.0, this is 'crx'
            $"crx({formatAngle theta}) q[{control}],q[{target}];"
        | CRY (control, target, theta) ->
            // CRY(θ) = controlled-RY gate
            // In OpenQASM 3.0, this is 'cry'
            $"cry({formatAngle theta}) q[{control}],q[{target}];"
        | CRZ (control, target, theta) ->
            // CRZ(θ) = controlled-RZ gate
            // In OpenQASM 3.0, this is 'crz'
            $"crz({formatAngle theta}) q[{control}],q[{target}];"
        | U3 (q, theta, phi, lambda) ->
            // U3(θ,φ,λ) = universal single-qubit gate
            // In OpenQASM 2.0, this is the 'u3' gate (also known as 'u' gate)
            $"u3({formatAngle theta},{formatAngle phi},{formatAngle lambda}) q[{q}];"
        | MCZ (controls, target) ->
            // MCZ gate should be decomposed before export
            // This case should not be reached if transpilation is done properly
            failwith "MCZ gate found in OpenQASM export. Call GateTranspiler.transpile() first to decompose multi-controlled gates."
        | Measure q ->
            $"measure q[{q}];"
    
    // ========================================================================
    // VALIDATION
    // ========================================================================
    
    /// <summary>
    /// Validate that a circuit is compatible with OpenQASM 2.0.
    /// </summary>
    ///
    /// <param name="circuit">Circuit to validate</param>
    /// <returns>Ok if valid, Error with message if invalid</returns>
    ///
    /// <remarks>
    /// Validation checks:
    /// - QubitCount >= 0
    /// - All gate qubit indices in range [0, QubitCount-1]
    /// - CNOT control and target in valid range
    /// </remarks>
    let validate (circuit: Circuit) : Result<unit, string> =
        // Check qubit count is non-negative
        if circuit.QubitCount < 0 then
            Error $"Invalid circuit: QubitCount must be >= 0, got {circuit.QubitCount}"
        else
            // Validate each gate's qubit indices
            let validateGate (gate: Gate) : Result<unit, string> =
                let checkQubit q gateName =
                    if q < 0 || q >= circuit.QubitCount then
                        Error $"Invalid {gateName}: qubit {q} out of range [0, {circuit.QubitCount - 1}]"
                    else
                        Ok ()
                
                match gate with
                | X q | Y q | Z q | H q | S q | SDG q | T q | TDG q | Measure q -> 
                    checkQubit q "single-qubit gate"
                | RX (q, _) | RY (q, _) | RZ (q, _) | P (q, _) -> 
                    checkQubit q "rotation gate"
                | U3 (q, _, _, _) ->
                    checkQubit q "U3 gate"
                | CNOT (control, target) | CZ (control, target) | CP (control, target, _) 
                | CRX (control, target, _) | CRY (control, target, _) | CRZ (control, target, _) ->
                    match checkQubit control "two-qubit control", checkQubit target "two-qubit target" with
                    | Ok (), Ok () -> 
                        if control = target then
                            Error "Two-qubit gate control and target must be different qubits"
                        else
                            Ok ()
                    | Error msg, _ -> Error msg
                    | _, Error msg -> Error msg
                | SWAP (q1, q2) ->
                    match checkQubit q1 "SWAP qubit1", checkQubit q2 "SWAP qubit2" with
                    | Ok (), Ok () -> 
                        if q1 = q2 then
                            Error "SWAP qubits must be different"
                        else
                            Ok ()
                    | Error msg, _ -> Error msg
                    | _, Error msg -> Error msg
                | CCX (control1, control2, target) ->
                    match checkQubit control1 "CCX control1", checkQubit control2 "CCX control2", checkQubit target "CCX target" with
                    | Ok (), Ok (), Ok () -> 
                        if control1 = control2 || control1 = target || control2 = target then
                            Error "CCX (Toffoli) control and target qubits must be distinct"
                        else
                            Ok ()
                    | Error msg, _, _ -> Error msg
                    | _, Error msg, _ -> Error msg
                    | _, _, Error msg -> Error msg
                | MCZ (controls, target) ->
                    // Validate all control qubits
                    let controlResults = controls |> List.map (fun c -> checkQubit c "MCZ control")
                    let targetResult = checkQubit target "MCZ target"
                    
                    match List.tryFind Result.isError controlResults with
                    | Some (Error msg) -> Error msg
                    | _ ->
                        match targetResult with
                        | Error msg -> Error msg
                        | Ok () ->
                            // Check all qubits are distinct
                            let allQubits = target :: controls
                            if List.distinct allQubits |> List.length <> List.length allQubits then
                                Error "MCZ control and target qubits must be distinct"
                            else
                                Ok ()
            
            // Validate all gates
            circuit.Gates
            |> List.tryPick (fun gate ->
                match validateGate gate with
                | Error msg -> Some msg
                | Ok () -> None
            )
            |> function
                | Some errorMsg -> Error errorMsg
                | None -> Ok ()
    
    // ========================================================================
    // EXPORT
    // ========================================================================
    
    /// <summary>
    /// Export circuit to OpenQASM 2.0 text format.
    /// </summary>
    ///
    /// <param name="circuit">Circuit to export</param>
    /// <returns>OpenQASM 2.0 code as string</returns>
    ///
    /// <remarks>
    /// <para><b>Output Format:</b></para>
    /// <code>
    /// OPENQASM 2.0;
    /// include "qelib1.inc";
    /// qreg q[n];
    /// [gate instructions...]
    /// </code>
    ///
    /// <para><b>Validation:</b></para>
    /// The circuit is NOT automatically validated. Use `validate` first if needed.
    /// Invalid circuits may produce invalid OpenQASM output.
    /// </remarks>
    ///
    /// <example>
    /// <code>
    /// let circuit = { QubitCount = 2; Gates = [H 0; CNOT (0, 1)] }
    /// let qasm = OpenQasm.export circuit
    /// // Returns:
    /// // OPENQASM 2.0;
    /// // include "qelib1.inc";
    /// // qreg q[2];
    /// // h q[0];
    /// // cx q[0],q[1];
    /// </code>
    /// </example>
    let export (circuit: Circuit) : string =
        let sb = StringBuilder()
        
        // Header
        sb.AppendLine("OPENQASM 2.0;") |> ignore
        sb.AppendLine("include \"qelib1.inc\";") |> ignore
        
        // Quantum register declaration
        sb.AppendLine($"qreg q[{circuit.QubitCount}];") |> ignore
        
        // Gate instructions
        for gate in circuit.Gates do
            sb.AppendLine(gateToQasm gate) |> ignore
        
        // Remove trailing newline
        sb.ToString().TrimEnd()
    
    /// <summary>
    /// Export circuit to a .qasm file.
    /// </summary>
    ///
    /// <param name="circuit">Circuit to export</param>
    /// <param name="filePath">Path to output file</param>
    ///
    /// <remarks>
    /// Overwrites the file if it already exists.
    /// Creates parent directories if they don't exist.
    /// </remarks>
    ///
    /// <example>
    /// <code>
    /// let circuit = { QubitCount = 1; Gates = [H 0] }
    /// OpenQasm.exportToFile circuit "hadamard.qasm"
    /// </code>
    /// </example>
    ///
    /// <exception cref="System.IO.IOException">
    /// Thrown if the file cannot be written
    /// </exception>
    let exportToFile (circuit: Circuit) (filePath: string) : unit =
        // Ensure parent directory exists
        let directory = Path.GetDirectoryName(filePath)
        if not (String.IsNullOrEmpty(directory)) && not (Directory.Exists(directory)) then
            Directory.CreateDirectory(directory) |> ignore
        
        // Export and write to file
        let qasmCode = export circuit
        File.WriteAllText(filePath, qasmCode)

/// <summary>
/// Public API module for OpenQASM 2.0 export.
/// </summary>
module OpenQasm =
    
    /// <summary>OpenQASM version number</summary>
    let version = OpenQasmExport.version
    
    /// <summary>Export circuit to OpenQASM 2.0 text format</summary>
    let export = OpenQasmExport.export
    
    /// <summary>Export circuit to file</summary>
    let exportToFile = OpenQasmExport.exportToFile
    
    /// <summary>Validate circuit is OpenQASM 2.0 compatible</summary>
    let validate = OpenQasmExport.validate
