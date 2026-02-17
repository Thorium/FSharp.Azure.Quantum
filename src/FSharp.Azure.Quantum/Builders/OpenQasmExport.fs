namespace FSharp.Azure.Quantum

open System
open System.IO
open System.Text

/// TKT-97: OpenQASM Export Module (Versioned)
/// 
/// Export quantum circuits to OpenQASM 1.0, 2.0, or 3.0 format.
/// Supports IBM Qiskit compatibility (2.0) and modern OpenQASM 3.0 syntax.
///
/// ## Backward Compatibility
/// All existing functions (export, validate, exportToFile) default to OpenQASM 2.0.
/// New versioned functions (exportWithConfig, validateWithConfig) accept QasmConfig.
///
/// ## Features
/// - Gate translation (X, Y, Z, H, S, SDG, T, TDG, RX, RY, RZ, CNOT, CZ, SWAP, CCX, etc.)
/// - Version-parameterized headers, register declarations, and measurement syntax
/// - Angle formatting with 10 decimal precision
/// - Circuit validation (qubit bounds checking)
/// - File export functionality
///
/// ## Usage Example
/// ```fsharp
/// // Default (2.0)
/// let qasmCode = OpenQasm.export circuit
///
/// // Versioned (3.0)
/// let config = OpenQasmVersion.configFor V3_0
/// let qasmV3 = OpenQasm.exportWithConfig config circuit
/// ```

/// <summary>
/// OpenQASM export module with version-parameterized output.
/// </summary>
module OpenQasmExport =
    
    open CircuitBuilder
    open OpenQasmVersion
    
    // ========================================================================
    // CONSTANTS
    // ========================================================================
    
    /// OpenQASM version number (default)
    let version = "2.0"
    
    /// Number of decimal places for angle formatting
    let private angleDecimalPlaces = 10
    
    // ========================================================================
    // ANGLE FORMATTING
    // ========================================================================
    
    /// <summary>
    /// Format angle with specified decimal precision for OpenQASM.
    /// </summary>
    let private formatAngle (angle: float) : string =
        angle.ToString($"F{angleDecimalPlaces}")
    
    // ========================================================================
    // GATE TRANSLATION
    // ========================================================================
    
    /// <summary>
    /// Translate a single gate to an OpenQASM instruction.
    /// Gate names are the same across all supported versions (qelib1.inc / stdgates.inc).
    /// The only version-dependent part is measurement syntax, handled separately.
    /// </summary>
    let private gateToQasm (config: QasmConfig) (gate: Gate) : string =
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
            $"p({formatAngle theta}) q[{q}];"
        | CP (control, target, theta) -> 
            $"cp({formatAngle theta}) q[{control}],q[{target}];"
        | CRX (control, target, theta) ->
            $"crx({formatAngle theta}) q[{control}],q[{target}];"
        | CRY (control, target, theta) ->
            $"cry({formatAngle theta}) q[{control}],q[{target}];"
        | CRZ (control, target, theta) ->
            $"crz({formatAngle theta}) q[{control}],q[{target}];"
        | U3 (q, theta, phi, lambda) ->
            $"u3({formatAngle theta},{formatAngle phi},{formatAngle lambda}) q[{q}];"
        | MCZ (controls, target) ->
            failwith "MCZ gate found in OpenQASM export. Call GateTranspiler.transpile() first to decompose multi-controlled gates."
        | Measure q ->
            measureInstruction config q
        | Reset q ->
            $"reset q[{q}];"
        | Barrier qubits ->
            let qubitList = qubits |> List.map (fun q -> $"q[{q}]") |> String.concat ","
            $"barrier {qubitList};"
    
    // ========================================================================
    // VALIDATION
    // ========================================================================
    
    /// <summary>
    /// Validate that a circuit is compatible with OpenQASM export.
    /// Validation is version-independent (qubit bounds checking applies to all versions).
    /// </summary>
    let validate (circuit: Circuit) : Result<unit, string> =
        if circuit.QubitCount < 0 then
            Error $"Invalid circuit: QubitCount must be >= 0, got {circuit.QubitCount}"
        else
            let validateGate (gate: Gate) : Result<unit, string> =
                let checkQubit q gateName =
                    if q < 0 || q >= circuit.QubitCount then
                        Error $"Invalid {gateName}: qubit {q} out of range [0, {circuit.QubitCount - 1}]"
                    else
                        Ok ()
                
                match gate with
                | X q | Y q | Z q | H q | S q | SDG q | T q | TDG q | Measure q | Reset q -> 
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
                    let controlResults = controls |> List.map (fun c -> checkQubit c "MCZ control")
                    let targetResult = checkQubit target "MCZ target"
                    
                    match List.tryFind Result.isError controlResults with
                    | Some (Error msg) -> Error msg
                    | _ ->
                        match targetResult with
                        | Error msg -> Error msg
                        | Ok () ->
                            let allQubits = target :: controls
                            if List.distinct allQubits |> List.length <> List.length allQubits then
                                Error "MCZ control and target qubits must be distinct"
                            else
                                Ok ()
                | Barrier qubits ->
                    if List.isEmpty qubits then
                        Error "Barrier must specify at least one qubit"
                    else
                        let qubitResults = qubits |> List.map (fun q -> checkQubit q "Barrier qubit")
                        match List.tryFind Result.isError qubitResults with
                        | Some (Error msg) -> Error msg
                        | _ ->
                            if List.distinct qubits |> List.length <> List.length qubits then
                                Error "Barrier qubits must be distinct"
                            else
                                Ok ()
            
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
    // EXPORT (VERSIONED)
    // ========================================================================
    
    /// <summary>
    /// Export circuit to OpenQASM text format using the specified version configuration.
    /// </summary>
    ///
    /// <param name="config">Version configuration (use OpenQasmVersion.configFor)</param>
    /// <param name="circuit">Circuit to export</param>
    /// <returns>OpenQASM code as string in the specified version format</returns>
    let exportWithConfig (config: QasmConfig) (circuit: Circuit) : string =
        let sb = StringBuilder()
        
        // Header
        sb.AppendLine(headerLine config) |> ignore
        sb.AppendLine(includeLine config) |> ignore
        
        // Register declarations
        sb.AppendLine(qubitRegisterDecl config circuit.QubitCount) |> ignore
        
        // Classical register declaration (only if circuit has measurements)
        let measureCount =
            circuit.Gates
            |> List.choose (fun g -> match g with | Measure _ -> Some () | _ -> None)
            |> List.length
        if measureCount > 0 then
            sb.AppendLine(classicalRegisterDecl config measureCount) |> ignore
        
        // Gate instructions
        for gate in circuit.Gates do
            sb.AppendLine(gateToQasm config gate) |> ignore
        
        sb.ToString().TrimEnd()
    
    /// <summary>
    /// Export circuit to OpenQASM 2.0 text format (backward compatible).
    /// </summary>
    let export (circuit: Circuit) : string =
        exportWithConfig (configFor V2_0) circuit
    
    /// <summary>
    /// Export circuit to a .qasm file using the specified version configuration.
    /// </summary>
    let exportToFileWithConfig (config: QasmConfig) (circuit: Circuit) (filePath: string) : unit =
        let directory = Path.GetDirectoryName(filePath)
        if not (String.IsNullOrEmpty(directory)) && not (Directory.Exists(directory)) then
            Directory.CreateDirectory(directory) |> ignore
        
        let qasmCode = exportWithConfig config circuit
        File.WriteAllText(filePath, qasmCode)
    
    /// <summary>
    /// Export circuit to a .qasm file (OpenQASM 2.0, backward compatible).
    /// </summary>
    let exportToFile (circuit: Circuit) (filePath: string) : unit =
        exportToFileWithConfig (configFor V2_0) circuit filePath

/// <summary>
/// Public API module for OpenQASM export.
/// Provides both backward-compatible 2.0 functions and new versioned functions.
/// </summary>
module OpenQasm =
    
    open OpenQasmVersion
    
    /// <summary>OpenQASM default version number</summary>
    let version = OpenQasmExport.version
    
    /// <summary>Export circuit to OpenQASM 2.0 text format (default)</summary>
    let export = OpenQasmExport.export
    
    /// <summary>Export circuit to OpenQASM text format using specified version config</summary>
    let exportWithConfig = OpenQasmExport.exportWithConfig
    
    /// <summary>Export circuit to OpenQASM 3.0 text format</summary>
    let exportV3 = OpenQasmExport.exportWithConfig (configFor V3_0)
    
    /// <summary>Export circuit to file (OpenQASM 2.0, default)</summary>
    let exportToFile = OpenQasmExport.exportToFile
    
    /// <summary>Export circuit to file using specified version config</summary>
    let exportToFileWithConfig = OpenQasmExport.exportToFileWithConfig
    
    /// <summary>Validate circuit is OpenQASM compatible</summary>
    let validate = OpenQasmExport.validate
