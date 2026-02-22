namespace FSharp.Azure.Quantum

open System
open System.IO
open System.Text.RegularExpressions
open System.Threading
open System.Threading.Tasks

/// OpenQASM Import Module (Versioned)
/// 
/// Parse OpenQASM 1.0, 2.0, and 3.0 circuits into F# Circuit types.
/// Auto-detects version from the OPENQASM header declaration.
/// Enables loading circuits created in IBM Qiskit and other OpenQASM-compatible tools.
///
/// ## Backward Compatibility
/// The `parse` function now auto-detects version (1.0, 2.0, or 3.0).
/// All existing code calling `parse` with OpenQASM 2.0 input continues to work unchanged.
///
/// ## Features
/// - Parse OpenQASM 1.0, 2.0, and 3.0 text formats
/// - Version auto-detection from header
/// - Support all standard gates from qelib1.inc and stdgates.inc
/// - OpenQASM 3.0: qubit[n]/bit[n] declarations, assignment measurement
/// - Angle parsing with high precision
/// - Comment and whitespace handling
/// - File I/O support
/// - Round-trip compatibility with OpenQasmExport
///
/// ## Usage Example
/// ```fsharp
/// // Auto-detect version (works for 1.0, 2.0, 3.0)
/// match OpenQasmImport.parse qasm with
/// | Ok circuit -> printfn "Loaded %d-qubit circuit" circuit.QubitCount
/// | Error msg -> printfn "Parse error: %s" msg
/// ```

/// <summary>
/// OpenQASM import module with version-aware parsing.
/// </summary>
module OpenQasmImport =
    
    open CircuitBuilder
    open OpenQasmVersion
    
    // ========================================================================
    // TYPES
    // ========================================================================
    
    /// Parse result type
    type ParseResult<'T> = Result<'T, string>
    
    /// Internal parser state
    type private ParserState = {
        QubitCount: int option
        Gates: Gate list
        LineNumber: int
        DetectedVersion: QasmVersion option
        /// Register name -> (offset, size) mapping for named register support
        RegisterMap: Map<string, int * int>
    }
    
    // ========================================================================
    // REGEX PATTERNS
    // ========================================================================
    
    /// Match OPENQASM version declaration
    let private versionPattern = Regex(@"^\s*OPENQASM\s+(\d+\.\d+)\s*;", RegexOptions.Compiled)
    
    /// Match include statement
    let private includePattern = Regex(@"^\s*include\s+""([^""]+)""\s*;", RegexOptions.Compiled)
    
    /// Match qreg declaration (OpenQASM 1.0/2.0 style: qreg q[n];)
    let private qregPattern = Regex(@"^\s*qreg\s+(\w+)\s*\[\s*(\d+)\s*\]\s*;", RegexOptions.Compiled)
    
    /// Match creg declaration (OpenQASM 1.0/2.0 style: creg c[n];)
    let private cregPattern = Regex(@"^\s*creg\s+(\w+)\s*\[\s*(\d+)\s*\]\s*;", RegexOptions.Compiled)
    
    /// Match qubit declaration (OpenQASM 3.0 style: qubit[n] q;)
    let private qubitDeclPattern = Regex(@"^\s*qubit\s*\[\s*(\d+)\s*\]\s+(\w+)\s*;", RegexOptions.Compiled)
    
    /// Match bit declaration (OpenQASM 3.0 style: bit[n] c;)
    let private bitDeclPattern = Regex(@"^\s*bit\s*\[\s*(\d+)\s*\]\s+(\w+)\s*;", RegexOptions.Compiled)
    
    /// Match assignment-style measurement (OpenQASM 3.0: c[n] = measure q[n];)
    let private assignMeasurePattern = Regex(@"^\s*(\w+)\s*\[\s*(\d+)\s*\]\s*=\s*measure\s+(\w+)\s*\[\s*(\d+)\s*\]\s*;", RegexOptions.Compiled)
    
    /// Match arrow-style measurement (OpenQASM 2.0: measure q[n] -> c[n];)
    let private arrowMeasurePattern = Regex(@"^\s*measure\s+(\w+)\s*\[\s*(\d+)\s*\]\s*->\s*(\w+)\s*\[\s*(\d+)\s*\]\s*;", RegexOptions.Compiled)
    
    /// Match single-qubit gate without parameters
    let private singleQubitPattern = Regex(@"^\s*(\w+)\s+(\w+)\s*\[\s*(\d+)\s*\]\s*;", RegexOptions.Compiled)
    
    /// Match single-qubit gate with one parameter (rotation gates)
    let private rotationPattern = Regex(@"^\s*(\w+)\s*\(\s*(-?[0-9.eE+\-*/pi]+)\s*\)\s+(\w+)\s*\[\s*(\d+)\s*\]\s*;", RegexOptions.Compiled)
    
    /// Match single-qubit gate with three parameters (u3 gate)
    let private u3Pattern = Regex(@"^\s*(\w+)\s*\(\s*(-?[0-9.eE+\-*/pi]+)\s*,\s*(-?[0-9.eE+\-*/pi]+)\s*,\s*(-?[0-9.eE+\-*/pi]+)\s*\)\s+(\w+)\s*\[\s*(\d+)\s*\]\s*;", RegexOptions.Compiled)
    
    /// Match single-qubit gate with two parameters (u2 gate)
    let private u2Pattern = Regex(@"^\s*(\w+)\s*\(\s*(-?[0-9.eE+\-*/pi]+)\s*,\s*(-?[0-9.eE+\-*/pi]+)\s*\)\s+(\w+)\s*\[\s*(\d+)\s*\]\s*;", RegexOptions.Compiled)
    
    /// Match two-qubit gate with one parameter (e.g., CP gate)
    let private twoQubitRotationPattern = Regex(@"^\s*(\w+)\s*\(\s*(-?[0-9.eE+\-*/pi]+)\s*\)\s+(\w+)\s*\[\s*(\d+)\s*\]\s*,\s*(\w+)\s*\[\s*(\d+)\s*\]\s*;", RegexOptions.Compiled)
    
    /// Match two-qubit gate
    let private twoQubitPattern = Regex(@"^\s*(\w+)\s+(\w+)\s*\[\s*(\d+)\s*\]\s*,\s*(\w+)\s*\[\s*(\d+)\s*\]\s*;", RegexOptions.Compiled)
    
    /// Match three-qubit gate (CCX/Toffoli)
    let private threeQubitPattern = Regex(@"^\s*(\w+)\s+(\w+)\s*\[\s*(\d+)\s*\]\s*,\s*(\w+)\s*\[\s*(\d+)\s*\]\s*,\s*(\w+)\s*\[\s*(\d+)\s*\]\s*;", RegexOptions.Compiled)
    
    /// Match reset instruction: reset q[n];
    let private resetPattern = Regex(@"^\s*reset\s+(\w+)\s*\[\s*(\d+)\s*\]\s*;", RegexOptions.Compiled)
    
    /// Match barrier instruction: barrier q[0],q[1],...;
    let private barrierPattern = Regex(@"^\s*barrier\s+(.*?)\s*;", RegexOptions.Compiled)
    
    /// Match individual qubit reference in barrier argument list: q[n]
    let private barrierQubitPattern = Regex(@"(\w+)\s*\[\s*(\d+)\s*\]", RegexOptions.Compiled)
    
    /// Match bare register name(s) in barrier argument list: q or q,r
    let private bareRegisterPattern = Regex(@"^\s*(\w+(?:\s*,\s*\w+)*)\s*$", RegexOptions.Compiled)
    
    /// Match single-line comment
    let private commentPattern = Regex(@"//.*$", RegexOptions.Compiled)
    
    /// Match multi-line comment (non-greedy, with Singleline option to allow . to match newlines)
    let private multiLineCommentPattern = Regex(@"/\*.*?\*/", RegexOptions.Compiled ||| RegexOptions.Singleline)
    
    // ========================================================================
    // HELPER FUNCTIONS
    // ========================================================================
    
    /// Remove comments from a line (single-line comments only)
    /// Multi-line comments are removed during preprocessing in parse()
    let private removeComments (line: string) : string =
        commentPattern.Replace(line, "")
    
    /// Parse angle string to float, supporting:
    /// - Numeric literals: "1.5707", "-1.5707", "3.14e-2"
    /// - Pi expressions: "pi", "pi/2", "pi/4", "2*pi", "3*pi/4", "-pi", "-pi/4"
    let private parseAngle (angleStr: string) : ParseResult<float> =
        let s = angleStr.Trim()
        // Try plain numeric first
        match Double.TryParse(s, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture) with
        | true, value -> Ok value
        | false, _ ->
            // Try pi expression parsing
            let piPattern = Regex(@"^(-?)(\d+(?:\.\d+)?)?\s*\*?\s*pi(?:\s*/\s*(\d+(?:\.\d+)?))?$", RegexOptions.Compiled)
            let m = piPattern.Match(s)
            if m.Success then
                let sign = if m.Groups.[1].Value = "-" then -1.0 else 1.0
                let multiplier =
                    if m.Groups.[2].Success && m.Groups.[2].Value <> "" then
                        match Double.TryParse(m.Groups.[2].Value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture) with
                        | true, v -> v
                        | false, _ -> 1.0
                    else 1.0
                let divisor =
                    if m.Groups.[3].Success && m.Groups.[3].Value <> "" then
                        match Double.TryParse(m.Groups.[3].Value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture) with
                        | true, v -> v
                        | false, _ -> 1.0
                    else 1.0
                Ok (sign * multiplier * Math.PI / divisor)
            else
                Error $"Invalid angle format: {angleStr}"
    
    /// Validate qubit index against circuit size
    let private validateQubitIndex (qubit: int) (maxQubits: int) : ParseResult<unit> =
        if qubit < 0 then
            Error $"Qubit index {qubit} is negative"
        elif qubit >= maxQubits then
            Error $"Qubit index {qubit} out of bounds (circuit has {maxQubits} qubits)"
        else
            Ok ()

    /// Resolve a register name and local index to a linear qubit index.
    /// If the register map is empty (legacy mode), just return the raw index.
    /// Otherwise, look up the register name and compute offset + local index.
    let private resolveQubit (regName: string) (localIndex: int) (state: ParserState) : ParseResult<int> =
        if Map.isEmpty state.RegisterMap then
            // Legacy mode: no named registers tracked, use raw index
            Ok localIndex
        else
            match Map.tryFind regName state.RegisterMap with
            | Some (offset, size) ->
                if localIndex < 0 || localIndex >= size then
                    Error $"Qubit index {localIndex} out of bounds for register '{regName}' (size {size})"
                else
                    Ok (offset + localIndex)
            | None ->
                Error $"Unknown register name '{regName}'"
    
    /// Expand a bare register name to all its qubit indices.
    /// Returns the linear qubit indices [offset .. offset + size - 1].
    /// In legacy mode (empty RegisterMap), expands to [0 .. QubitCount - 1].
    let private expandRegister (regName: string) (state: ParserState) : ParseResult<int list> =
        if Map.isEmpty state.RegisterMap then
            // Legacy mode: single unnamed register, expand to all qubits
            match state.QubitCount with
            | Some qCount -> Ok [ 0 .. qCount - 1 ]
            | None -> Error "Register used before qubit declaration"
        else
            match Map.tryFind regName state.RegisterMap with
            | Some (offset, size) -> Ok [ offset .. offset + size - 1 ]
            | None -> Error $"Unknown register name '{regName}'"
    
    // ========================================================================
    // GATE PARSING
    // ========================================================================
    
    /// Parse single-qubit gate (no parameters)
    let private parseSingleQubitGate (gateName: string) (qubit: int) (qubitCount: int) : ParseResult<Gate> =
        match validateQubitIndex qubit qubitCount with
        | Error msg -> Error msg
        | Ok () ->
            match gateName.ToLowerInvariant() with
            | "x" -> Ok (X qubit)
            | "y" -> Ok (Y qubit)
            | "z" -> Ok (Z qubit)
            | "h" -> Ok (H qubit)
            | "s" -> Ok (S qubit)
            | "sdg" -> Ok (SDG qubit)
            | "t" -> Ok (T qubit)
            | "tdg" -> Ok (TDG qubit)
            | "sx" ->
                // SX = √X gate, decomposition: SX = RX(π/2)
                Ok (RX (qubit, Math.PI / 2.0))
            | _ -> Error $"Unknown single-qubit gate: {gateName}"
    
    /// Parse rotation gate (with angle parameter)
    let private parseRotationGate (gateName: string) (angle: float) (qubit: int) (qubitCount: int) : ParseResult<Gate> =
        match validateQubitIndex qubit qubitCount with
        | Error msg -> Error msg
        | Ok () ->
            match gateName.ToLowerInvariant() with
            | "rx" -> Ok (RX (qubit, angle))
            | "ry" -> Ok (RY (qubit, angle))
            | "rz" -> Ok (RZ (qubit, angle))
            | "p" | "u1" -> Ok (P (qubit, angle))  // p or u1 (legacy) both map to P gate
            | _ -> Error $"Unknown rotation gate: {gateName}"
    
    /// Parse U3 gate (universal single-qubit gate with three angle parameters)
    let private parseU3Gate (gateName: string) (theta: float) (phi: float) (lambda: float) (qubit: int) (qubitCount: int) : ParseResult<Gate> =
        match validateQubitIndex qubit qubitCount with
        | Error msg -> Error msg
        | Ok () ->
            match gateName.ToLowerInvariant() with
            | "u3" | "u" -> Ok (U3 (qubit, theta, phi, lambda))  // u3 or u (OpenQASM 2.0) both map to U3 gate
            | _ -> Error $"Unknown three-parameter gate: {gateName}"
    
    /// Parse U2 gate (two-parameter gate) - special case of U3
    /// U2(phi, lambda) = U3(pi/2, phi, lambda)
    let private parseU2Gate (gateName: string) (phi: float) (lambda: float) (qubit: int) (qubitCount: int) : ParseResult<Gate> =
        match validateQubitIndex qubit qubitCount with
        | Error msg -> Error msg
        | Ok () ->
            match gateName.ToLowerInvariant() with
            | "u2" -> 
                // Decomposition: U2(phi, lambda) = U3(pi/2, phi, lambda)
                Ok (U3 (qubit, Math.PI / 2.0, phi, lambda))
            | _ -> Error $"Unknown two-parameter gate: {gateName}"
    
    /// Parse two-qubit rotation gate (with angle parameter)
    let private parseTwoQubitRotationGate (gateName: string) (angle: float) (qubit1: int) (qubit2: int) (qubitCount: int) : ParseResult<Gate> =
        match validateQubitIndex qubit1 qubitCount with
        | Error msg -> Error msg
        | Ok () ->
            match validateQubitIndex qubit2 qubitCount with
            | Error msg -> Error msg
            | Ok () ->
                if qubit1 = qubit2 then
                    Error $"Two-qubit gate cannot have same control and target: {qubit1}"
                else
                    match gateName.ToLowerInvariant() with
                    | "cp" | "cu1" -> Ok (CP (qubit1, qubit2, angle))  // cp or cu1 (legacy) both map to CP gate
                    | "crx" -> Ok (CRX (qubit1, qubit2, angle))  // Controlled-RX gate
                    | "cry" -> Ok (CRY (qubit1, qubit2, angle))  // Controlled-RY gate
                    | "crz" -> Ok (CRZ (qubit1, qubit2, angle))  // Controlled-RZ gate
                    | _ -> Error $"Unknown two-qubit rotation gate: {gateName}"
    
    /// Parse two-qubit gate
    let private parseTwoQubitGate (gateName: string) (qubit1: int) (qubit2: int) (qubitCount: int) : ParseResult<Gate> =
        match validateQubitIndex qubit1 qubitCount with
        | Error msg -> Error msg
        | Ok () ->
            match validateQubitIndex qubit2 qubitCount with
            | Error msg -> Error msg
            | Ok () ->
                if qubit1 = qubit2 then
                    Error $"Two-qubit gate cannot have same control and target: {qubit1}"
                else
                    match gateName.ToLowerInvariant() with
                    | "cx" | "cnot" -> Ok (CNOT (qubit1, qubit2))
                    | "cz" -> Ok (CZ (qubit1, qubit2))
                    | "swap" -> Ok (SWAP (qubit1, qubit2))
                    | _ -> Error $"Unknown two-qubit gate: {gateName}"
    
    /// Parse three-qubit gate
    let private parseThreeQubitGate (gateName: string) (qubit1: int) (qubit2: int) (qubit3: int) (qubitCount: int) : ParseResult<Gate> =
        match validateQubitIndex qubit1 qubitCount with
        | Error msg -> Error msg
        | Ok () ->
            match validateQubitIndex qubit2 qubitCount with
            | Error msg -> Error msg
            | Ok () ->
                match validateQubitIndex qubit3 qubitCount with
                | Error msg -> Error msg
                | Ok () ->
                    if qubit1 = qubit2 || qubit1 = qubit3 || qubit2 = qubit3 then
                        Error $"Three-qubit gate cannot have duplicate qubits: {qubit1}, {qubit2}, {qubit3}"
                    else
                        match gateName.ToLowerInvariant() with
                        | "ccx" | "toffoli" -> Ok (CCX (qubit1, qubit2, qubit3))
                        | _ -> Error $"Unknown three-qubit gate: {gateName}"
    
    // ========================================================================
    // LINE PARSING
    // ========================================================================
    
    /// Parse a single line of OpenQASM (version-aware)
    let private parseLine (line: string) (state: ParserState) : ParseResult<ParserState> =
        let cleanLine = removeComments line |> fun s -> s.Trim()
        
        // Skip empty lines
        if String.IsNullOrWhiteSpace(cleanLine) then
            Ok state
        else
            // Version declaration — accept 1.0, 2.0, and 3.0
            let versionMatch = versionPattern.Match(cleanLine)
            if versionMatch.Success then
                let versionStr = versionMatch.Groups.[1].Value
                match parseVersionString versionStr with
                | Ok detectedVersion ->
                    Ok { state with DetectedVersion = Some detectedVersion }
                | Error msg ->
                    Error $"Line {state.LineNumber}: {msg}"
            
            // Include statement
            elif includePattern.IsMatch(cleanLine) then
                Ok state  // Ignore include statements
            
            // OpenQASM 3.0 qubit declaration: qubit[n] q;
            elif qubitDeclPattern.IsMatch(cleanLine) then
                let m = qubitDeclPattern.Match(cleanLine)
                let count = Int32.Parse(m.Groups.[1].Value)
                let regName = m.Groups.[2].Value
                
                if Map.containsKey regName state.RegisterMap then
                    Error $"Line {state.LineNumber}: Duplicate register name '{regName}'"
                else
                    let currentTotal = state.QubitCount |> Option.defaultValue 0
                    let newMap = Map.add regName (currentTotal, count) state.RegisterMap
                    Ok { state with QubitCount = Some (currentTotal + count); RegisterMap = newMap }
            
            // OpenQASM 1.0/2.0 qreg declaration: qreg q[n];
            elif qregPattern.IsMatch(cleanLine) then
                let m = qregPattern.Match(cleanLine)
                let regName = m.Groups.[1].Value
                let count = Int32.Parse(m.Groups.[2].Value)
                
                if Map.containsKey regName state.RegisterMap then
                    Error $"Line {state.LineNumber}: Duplicate register name '{regName}'"
                else
                    let currentTotal = state.QubitCount |> Option.defaultValue 0
                    let newMap = Map.add regName (currentTotal, count) state.RegisterMap
                    Ok { state with QubitCount = Some (currentTotal + count); RegisterMap = newMap }
            
            // OpenQASM 3.0 bit declaration: bit[n] c; — ignore (classical register)
            elif bitDeclPattern.IsMatch(cleanLine) then
                Ok state
            
            // OpenQASM 1.0/2.0 creg declaration: creg c[n]; — ignore (classical register)
            elif cregPattern.IsMatch(cleanLine) then
                Ok state
            
            // OpenQASM 3.0 assignment measurement: c[n] = measure q[n];
            elif assignMeasurePattern.IsMatch(cleanLine) then
                let m = assignMeasurePattern.Match(cleanLine)
                let qRegName = m.Groups.[3].Value
                let localQubit = Int32.Parse(m.Groups.[4].Value)
                
                match state.QubitCount with
                | None -> Error $"Line {state.LineNumber}: Measurement used before qubit declaration"
                | Some qCount ->
                    match resolveQubit qRegName localQubit state with
                    | Error msg -> Error $"Line {state.LineNumber}: {msg}"
                    | Ok qubit ->
                        match validateQubitIndex qubit qCount with
                        | Error msg -> Error $"Line {state.LineNumber}: {msg}"
                        | Ok () ->
                            Ok { state with Gates = (Measure qubit) :: state.Gates }
            
            // OpenQASM 2.0 arrow measurement: measure q[n] -> c[n];
            elif arrowMeasurePattern.IsMatch(cleanLine) then
                let m = arrowMeasurePattern.Match(cleanLine)
                let qRegName = m.Groups.[1].Value
                let localQubit = Int32.Parse(m.Groups.[2].Value)
                
                match state.QubitCount with
                | None -> Error $"Line {state.LineNumber}: Measurement used before qubit declaration"
                | Some qCount ->
                    match resolveQubit qRegName localQubit state with
                    | Error msg -> Error $"Line {state.LineNumber}: {msg}"
                    | Ok qubit ->
                        match validateQubitIndex qubit qCount with
                        | Error msg -> Error $"Line {state.LineNumber}: {msg}"
                        | Ok () ->
                            Ok { state with Gates = (Measure qubit) :: state.Gates }
            
            // Reset instruction: reset q[n];
            elif resetPattern.IsMatch(cleanLine) then
                let m = resetPattern.Match(cleanLine)
                let regName = m.Groups.[1].Value
                let localIdx = Int32.Parse(m.Groups.[2].Value)
                
                match state.QubitCount with
                | None -> Error $"Line {state.LineNumber}: Reset used before qubit declaration"
                | Some qCount ->
                    match resolveQubit regName localIdx state with
                    | Error msg -> Error $"Line {state.LineNumber}: {msg}"
                    | Ok qubit ->
                        match validateQubitIndex qubit qCount with
                        | Error msg -> Error $"Line {state.LineNumber}: {msg}"
                        | Ok () ->
                            Ok { state with Gates = (Reset qubit) :: state.Gates }
            
            // Barrier instruction: barrier q[0],q[1],...; or barrier q;
            elif barrierPattern.IsMatch(cleanLine) then
                let m = barrierPattern.Match(cleanLine)
                let argsStr = m.Groups.[1].Value
                let qubitMatches = barrierQubitPattern.Matches(argsStr)
                
                match state.QubitCount with
                | None -> Error $"Line {state.LineNumber}: Barrier used before qubit declaration"
                | Some qCount ->
                    if qubitMatches.Count > 0 then
                        // Explicit qubit indices: barrier q[0],q[1],...;
                        let resolvedQubits =
                            [ for qm in qubitMatches do
                                let regName = qm.Groups.[1].Value
                                let localIdx = Int32.Parse(qm.Groups.[2].Value)
                                yield resolveQubit regName localIdx state
                                    |> Result.bind (fun q -> validateQubitIndex q qCount |> Result.map (fun () -> q)) ]
                        
                        match resolvedQubits |> List.tryFind Result.isError with
                        | Some (Error msg) -> Error $"Line {state.LineNumber}: {msg}"
                        | _ ->
                            let qubits = resolvedQubits |> List.map (fun r -> match r with Ok q -> q | _ -> 0)
                            Ok { state with Gates = (Barrier qubits) :: state.Gates }
                    else
                        // Bare register names: barrier q; or barrier q,r;
                        let bareMatch = bareRegisterPattern.Match(argsStr)
                        if bareMatch.Success then
                            let regNames =
                                bareMatch.Groups.[1].Value.Split(',')
                                |> Array.map (fun s -> s.Trim())
                                |> Array.toList
                            let expandedResults = regNames |> List.map (fun name -> expandRegister name state)
                            match expandedResults |> List.tryFind Result.isError with
                            | Some (Error msg) -> Error $"Line {state.LineNumber}: {msg}"
                            | _ ->
                                let qubits = expandedResults |> List.collect (fun r -> match r with Ok qs -> qs | _ -> [])
                                if List.isEmpty qubits then
                                    Error $"Line {state.LineNumber}: Barrier must specify at least one qubit"
                                else
                                    Ok { state with Gates = (Barrier qubits) :: state.Gates }
                        else
                            Error $"Line {state.LineNumber}: Barrier must specify at least one qubit"
            
            // Three-qubit gate (must check before two-qubit)
            elif threeQubitPattern.IsMatch(cleanLine) then
                let m = threeQubitPattern.Match(cleanLine)
                let gateName = m.Groups.[1].Value
                let reg1 = m.Groups.[2].Value
                let idx1 = Int32.Parse(m.Groups.[3].Value)
                let reg2 = m.Groups.[4].Value
                let idx2 = Int32.Parse(m.Groups.[5].Value)
                let reg3 = m.Groups.[6].Value
                let idx3 = Int32.Parse(m.Groups.[7].Value)
                
                match state.QubitCount with
                | None -> Error $"Line {state.LineNumber}: Gate used before qreg declaration"
                | Some qCount ->
                    match resolveQubit reg1 idx1 state, resolveQubit reg2 idx2 state, resolveQubit reg3 idx3 state with
                    | Ok qubit1, Ok qubit2, Ok qubit3 ->
                        match parseThreeQubitGate gateName qubit1 qubit2 qubit3 qCount with
                        | Ok gate -> Ok { state with Gates = gate :: state.Gates }
                        | Error msg -> Error $"Line {state.LineNumber}: {msg}"
                    | Error msg, _, _ -> Error $"Line {state.LineNumber}: {msg}"
                    | _, Error msg, _ -> Error $"Line {state.LineNumber}: {msg}"
                    | _, _, Error msg -> Error $"Line {state.LineNumber}: {msg}"
            
            // Two-qubit rotation gate (with parameter) - must check before two-qubit gate
            elif twoQubitRotationPattern.IsMatch(cleanLine) then
                let m = twoQubitRotationPattern.Match(cleanLine)
                let gateName = m.Groups.[1].Value
                let angleStr = m.Groups.[2].Value
                let reg1 = m.Groups.[3].Value
                let idx1 = Int32.Parse(m.Groups.[4].Value)
                let reg2 = m.Groups.[5].Value
                let idx2 = Int32.Parse(m.Groups.[6].Value)
                
                match state.QubitCount with
                | None -> Error $"Line {state.LineNumber}: Gate used before qreg declaration"
                | Some qCount ->
                    match parseAngle angleStr with
                    | Ok angle ->
                        match resolveQubit reg1 idx1 state, resolveQubit reg2 idx2 state with
                        | Ok qubit1, Ok qubit2 ->
                            match parseTwoQubitRotationGate gateName angle qubit1 qubit2 qCount with
                            | Ok gate -> Ok { state with Gates = gate :: state.Gates }
                            | Error msg -> Error $"Line {state.LineNumber}: {msg}"
                        | Error msg, _ -> Error $"Line {state.LineNumber}: {msg}"
                        | _, Error msg -> Error $"Line {state.LineNumber}: {msg}"
                    | Error msg -> Error $"Line {state.LineNumber}: {msg}"
            
            // Two-qubit gate
            elif twoQubitPattern.IsMatch(cleanLine) then
                let m = twoQubitPattern.Match(cleanLine)
                let gateName = m.Groups.[1].Value
                let reg1 = m.Groups.[2].Value
                let idx1 = Int32.Parse(m.Groups.[3].Value)
                let reg2 = m.Groups.[4].Value
                let idx2 = Int32.Parse(m.Groups.[5].Value)
                
                match state.QubitCount with
                | None -> Error $"Line {state.LineNumber}: Gate used before qreg declaration"
                | Some qCount ->
                    match resolveQubit reg1 idx1 state, resolveQubit reg2 idx2 state with
                    | Ok qubit1, Ok qubit2 ->
                        match parseTwoQubitGate gateName qubit1 qubit2 qCount with
                        | Ok gate -> Ok { state with Gates = gate :: state.Gates }
                        | Error msg -> Error $"Line {state.LineNumber}: {msg}"
                    | Error msg, _ -> Error $"Line {state.LineNumber}: {msg}"
                    | _, Error msg -> Error $"Line {state.LineNumber}: {msg}"
            
            // U3 gate (three parameters) - must check before rotation gate (one parameter)
            elif u3Pattern.IsMatch(cleanLine) then
                let m = u3Pattern.Match(cleanLine)
                let gateName = m.Groups.[1].Value
                let thetaStr = m.Groups.[2].Value
                let phiStr = m.Groups.[3].Value
                let lambdaStr = m.Groups.[4].Value
                let regName = m.Groups.[5].Value
                let localIdx = Int32.Parse(m.Groups.[6].Value)
                
                match state.QubitCount with
                | None -> Error $"Line {state.LineNumber}: Gate used before qreg declaration"
                | Some qCount ->
                    match resolveQubit regName localIdx state with
                    | Error msg -> Error $"Line {state.LineNumber}: {msg}"
                    | Ok qubit ->
                        match parseAngle thetaStr, parseAngle phiStr, parseAngle lambdaStr with
                        | Ok theta, Ok phi, Ok lambda ->
                            match parseU3Gate gateName theta phi lambda qubit qCount with
                            | Ok gate -> Ok { state with Gates = gate :: state.Gates }
                            | Error msg -> Error $"Line {state.LineNumber}: {msg}"
                        | Error msg, _, _ -> Error $"Line {state.LineNumber}: {msg}"
                        | _, Error msg, _ -> Error $"Line {state.LineNumber}: {msg}"
                        | _, _, Error msg -> Error $"Line {state.LineNumber}: {msg}"
            
            // Rotation gate (with parameter)
            elif rotationPattern.IsMatch(cleanLine) then
                let m = rotationPattern.Match(cleanLine)
                let gateName = m.Groups.[1].Value
                let angleStr = m.Groups.[2].Value
                let regName = m.Groups.[3].Value
                let localIdx = Int32.Parse(m.Groups.[4].Value)
                
                match state.QubitCount with
                | None -> Error $"Line {state.LineNumber}: Gate used before qreg declaration"
                | Some qCount ->
                    match resolveQubit regName localIdx state with
                    | Error msg -> Error $"Line {state.LineNumber}: {msg}"
                    | Ok qubit ->
                        match parseAngle angleStr with
                        | Ok angle ->
                            match parseRotationGate gateName angle qubit qCount with
                            | Ok gate -> Ok { state with Gates = gate :: state.Gates }
                            | Error msg -> Error $"Line {state.LineNumber}: {msg}"
                        | Error msg -> Error $"Line {state.LineNumber}: {msg}"
            
            // Single-qubit gate (no parameters)
            elif singleQubitPattern.IsMatch(cleanLine) then
                let m = singleQubitPattern.Match(cleanLine)
                let gateName = m.Groups.[1].Value
                let regName = m.Groups.[2].Value
                let localIdx = Int32.Parse(m.Groups.[3].Value)
                
                // Special case: ID gate (identity/no-op) - just ignore it
                if gateName.ToLowerInvariant() = "id" then
                    Ok state  // Skip identity gates (they do nothing)
                else
                    match state.QubitCount with
                    | None -> Error $"Line {state.LineNumber}: Gate used before qreg declaration"
                    | Some qCount ->
                        match resolveQubit regName localIdx state with
                        | Error msg -> Error $"Line {state.LineNumber}: {msg}"
                        | Ok qubit ->
                            match parseSingleQubitGate gateName qubit qCount with
                            | Ok gate -> Ok { state with Gates = gate :: state.Gates }
                            | Error msg -> Error $"Line {state.LineNumber}: {msg}"
            
            else
                // Unknown line format - provide helpful error
                if cleanLine.StartsWith("OPENQASM") then
                    Error $"Line {state.LineNumber}: Malformed OPENQASM version declaration"
                elif cleanLine.StartsWith("qreg") then
                    Error $"Line {state.LineNumber}: Malformed qreg declaration"
                elif cleanLine.StartsWith("qubit") then
                    Error $"Line {state.LineNumber}: Malformed qubit declaration"
                elif cleanLine.StartsWith("creg") then
                    Ok state  // Ignore classical register declarations
                elif cleanLine.StartsWith("measure") then
                    Ok state  // Ignore standalone measure instructions (no arrow/assignment)
                elif cleanLine.StartsWith("barrier") then
                    Error $"Line {state.LineNumber}: Malformed barrier instruction"
                elif cleanLine.StartsWith("reset") then
                    Error $"Line {state.LineNumber}: Malformed reset instruction"
                else
                    Error $"Line {state.LineNumber}: Unrecognized instruction: {cleanLine}"
    
    // ========================================================================
    // PUBLIC API
    // ========================================================================
    
    /// <summary>
    /// Parse OpenQASM text into a Circuit with version auto-detection.
    /// Supports OpenQASM 1.0, 2.0, and 3.0.
    /// </summary>
    ///
    /// <param name="qasm">OpenQASM source code (any supported version)</param>
    /// <returns>Parsed circuit or error message</returns>
    ///
    /// <example>
    /// <code>
    /// // OpenQASM 2.0
    /// let qasm2 = "OPENQASM 2.0;\ninclude \"qelib1.inc\";\nqreg q[2];\nh q[0];\ncx q[0],q[1];"
    /// 
    /// // OpenQASM 3.0
    /// let qasm3 = "OPENQASM 3.0;\ninclude \"stdgates.inc\";\nqubit[2] q;\nh q[0];\ncx q[0],q[1];"
    ///
    /// match OpenQasmImport.parse qasm2 with
    /// | Ok circuit -> printfn "Loaded %d-qubit circuit" circuit.QubitCount
    /// | Error msg -> printfn "Parse error: %s" msg
    /// </code>
    /// </example>
    let parse (qasm: string) : ParseResult<Circuit> =
        // Remove multi-line comments that span multiple lines (/* ... */)
        // Must be done before splitting into lines
        let withoutMultiLineComments = multiLineCommentPattern.Replace(qasm, "")
        
        // Check for OPENQASM version first
        let lines = withoutMultiLineComments.Split([|'\n'; '\r'|], StringSplitOptions.RemoveEmptyEntries)
        
        if not (lines |> Array.exists (fun line -> versionPattern.IsMatch(line))) then
            Error "Missing OPENQASM version declaration (expected: OPENQASM 2.0; or OPENQASM 3.0;)"
        else
            // Parse line by line
            let initialState = { QubitCount = None; Gates = []; LineNumber = 1; DetectedVersion = None; RegisterMap = Map.empty }
            
            let rec parseLines (lineList: string list) (state: ParserState) : ParseResult<ParserState> =
                match lineList with
                | [] -> Ok state
                | line :: rest ->
                    match parseLine line state with
                    | Ok newState -> parseLines rest { newState with LineNumber = state.LineNumber + 1 }
                    | Error msg -> Error msg
            
            match parseLines (List.ofArray lines) initialState with
            | Ok finalState ->
                match finalState.QubitCount with
                | None -> Error "No qubit register declaration found (expected: qreg q[n]; or qubit[n] q;)"
                | Some qCount ->
                    Ok { QubitCount = qCount; Gates = finalState.Gates |> List.rev }
            | Error msg -> Error msg
    
    /// <summary>
    /// Parse OpenQASM text with explicit version enforcement.
    /// Rejects input if the declared version does not match the expected version.
    /// </summary>
    ///
    /// <param name="expectedVersion">Expected OpenQASM version</param>
    /// <param name="qasm">OpenQASM source code</param>
    /// <returns>Parsed circuit or error message</returns>
    let parseWithVersion (expectedVersion: QasmVersion) (qasm: string) : ParseResult<Circuit> =
        match detectVersion qasm with
        | Error msg -> Error msg
        | Ok detectedVersion ->
            if detectedVersion <> expectedVersion then
                let expected = versionToString expectedVersion
                let actual = versionToString detectedVersion
                Error $"Version mismatch: expected OpenQASM {expected} but found {actual}"
            else
                parse qasm
    
    /// <summary>
    /// Parse OpenQASM file into a Circuit with version auto-detection.
    /// </summary>
    let parseFromFile (filePath: string) : ParseResult<Circuit> =
        try
            let qasm = File.ReadAllText(filePath)
            parse qasm
        with
        | :? FileNotFoundException ->
            Error $"File not found: {filePath}"
        | :? IOException as ex ->
            Error $"I/O error reading file: {ex.Message}"
        | ex ->
            Error $"Unexpected error reading file: {ex.Message}"

    /// <summary>
    /// Parse OpenQASM file into a Circuit asynchronously with version auto-detection.
    /// </summary>
    let parseFromFileAsync (filePath: string) (ct: CancellationToken) : Task<ParseResult<Circuit>> = task {
        try
            let! qasm = File.ReadAllTextAsync(filePath, ct)
            return parse qasm
        with
        | :? FileNotFoundException ->
            return Error $"File not found: {filePath}"
        | :? IOException as ex ->
            return Error $"I/O error reading file: {ex.Message}"
        | ex ->
            return Error $"Unexpected error reading file: {ex.Message}"
    }
    
    /// <summary>
    /// Validate that a string contains valid OpenQASM (any supported version).
    /// </summary>
    let validate (qasm: string) : ParseResult<unit> =
        match parse qasm with
        | Ok _ -> Ok ()
        | Error msg -> Error msg
