namespace FSharp.Azure.Quantum

open System
open System.IO
open System.Text.RegularExpressions

/// TKT-97: OpenQASM 2.0 Import Module
/// 
/// Parse OpenQASM 2.0 circuits into F# Circuit types.
/// Enables loading circuits created in IBM Qiskit and other OpenQASM-compatible tools.
///
/// ⚠️ CRITICAL: ALL OpenQASM import code consolidated in this SINGLE FILE
/// for AI context optimization.
///
/// ## Features
/// - Parse OpenQASM 2.0 text format
/// - Support all standard gates (X, Y, Z, H, S, SDG, T, TDG, RX, RY, RZ, CNOT, CZ, SWAP, CCX)
/// - Angle parsing with high precision
/// - Comment and whitespace handling
/// - Comprehensive error messages
/// - File I/O support
/// - Round-trip compatibility with OpenQasmExport
///
/// ## Usage Example
/// ```fsharp
/// let qasm = File.ReadAllText "bell_state.qasm"
/// match OpenQasmImport.parse qasm with
/// | Ok circuit -> printfn "Loaded %d-qubit circuit" circuit.QubitCount
/// | Error msg -> printfn "Parse error: %s" msg
/// ```

/// <summary>
/// OpenQASM 2.0 import module for parsing quantum circuits.
/// </summary>
///
/// <remarks>
/// Parses OpenQASM 2.0 text format into F# Circuit types. Compatible with
/// circuits exported from IBM Qiskit, Cirq, and other OpenQASM-compatible tools.
///
/// <para><b>Supported Features:</b></para>
/// <list type="bullet">
///   <item>All standard gates from qelib1.inc</item>
///   <item>Single and multi-qubit gates</item>
///   <item>Parameterized rotation gates</item>
///   <item>Comments (// and /* */)</item>
///   <item>Flexible whitespace</item>
/// </list>
/// </remarks>
module OpenQasmImport =
    
    open CircuitBuilder
    
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
    }
    
    // ========================================================================
    // REGEX PATTERNS
    // ========================================================================
    
    /// Match OPENQASM version declaration
    let private versionPattern = Regex(@"^\s*OPENQASM\s+(\d+\.\d+)\s*;", RegexOptions.Compiled)
    
    /// Match include statement
    let private includePattern = Regex(@"^\s*include\s+""([^""]+)""\s*;", RegexOptions.Compiled)
    
    /// Match qreg declaration
    let private qregPattern = Regex(@"^\s*qreg\s+(\w+)\s*\[\s*(\d+)\s*\]\s*;", RegexOptions.Compiled)
    
    /// Match single-qubit gate without parameters
    let private singleQubitPattern = Regex(@"^\s*(\w+)\s+(\w+)\s*\[\s*(\d+)\s*\]\s*;", RegexOptions.Compiled)
    
    /// Match single-qubit gate with one parameter (rotation gates)
    let private rotationPattern = Regex(@"^\s*(\w+)\s*\(\s*([0-9.eE+-]+)\s*\)\s+(\w+)\s*\[\s*(\d+)\s*\]\s*;", RegexOptions.Compiled)
    
    /// Match single-qubit gate with three parameters (u3 gate)
    let private u3Pattern = Regex(@"^\s*(\w+)\s*\(\s*([0-9.eE+-]+)\s*,\s*([0-9.eE+-]+)\s*,\s*([0-9.eE+-]+)\s*\)\s+(\w+)\s*\[\s*(\d+)\s*\]\s*;", RegexOptions.Compiled)
    
    /// Match single-qubit gate with two parameters (u2 gate)
    let private u2Pattern = Regex(@"^\s*(\w+)\s*\(\s*([0-9.eE+-]+)\s*,\s*([0-9.eE+-]+)\s*\)\s+(\w+)\s*\[\s*(\d+)\s*\]\s*;", RegexOptions.Compiled)
    
    /// Match two-qubit gate with one parameter (e.g., CP gate)
    let private twoQubitRotationPattern = Regex(@"^\s*(\w+)\s*\(\s*([0-9.eE+-]+)\s*\)\s+(\w+)\s*\[\s*(\d+)\s*\]\s*,\s*(\w+)\s*\[\s*(\d+)\s*\]\s*;", RegexOptions.Compiled)
    
    /// Match two-qubit gate
    let private twoQubitPattern = Regex(@"^\s*(\w+)\s+(\w+)\s*\[\s*(\d+)\s*\]\s*,\s*(\w+)\s*\[\s*(\d+)\s*\]\s*;", RegexOptions.Compiled)
    
    /// Match three-qubit gate (CCX/Toffoli)
    let private threeQubitPattern = Regex(@"^\s*(\w+)\s+(\w+)\s*\[\s*(\d+)\s*\]\s*,\s*(\w+)\s*\[\s*(\d+)\s*\]\s*,\s*(\w+)\s*\[\s*(\d+)\s*\]\s*;", RegexOptions.Compiled)
    
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
    
    /// Parse angle string to float
    let private parseAngle (angleStr: string) : ParseResult<float> =
        match Double.TryParse(angleStr) with
        | true, value -> Ok value
        | false, _ -> Error $"Invalid angle format: {angleStr}"
    
    /// Validate qubit index against circuit size
    let private validateQubitIndex (qubit: int) (maxQubits: int) : ParseResult<unit> =
        if qubit < 0 then
            Error $"Qubit index {qubit} is negative"
        elif qubit >= maxQubits then
            Error $"Qubit index {qubit} out of bounds (circuit has {maxQubits} qubits)"
        else
            Ok ()
    
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
                // TODO: Add native SX gate support (√X gate)
                // Decomposition: SX = RX(π/2)
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
    /// U2(φ, λ) = U3(π/2, φ, λ)
    let private parseU2Gate (gateName: string) (phi: float) (lambda: float) (qubit: int) (qubitCount: int) : ParseResult<Gate> =
        match validateQubitIndex qubit qubitCount with
        | Error msg -> Error msg
        | Ok () ->
            match gateName.ToLowerInvariant() with
            | "u2" -> 
                // TODO: Add native U2 gate support (optional - this decomposition is efficient)
                // Decomposition: U2(φ, λ) = U3(π/2, φ, λ)
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
    
    /// Parse a single line of OpenQASM
    let private parseLine (line: string) (state: ParserState) : ParseResult<ParserState> =
        let cleanLine = removeComments line |> fun s -> s.Trim()
        
        // Skip empty lines
        if String.IsNullOrWhiteSpace(cleanLine) then
            Ok state
        else
            // Try matching various patterns
            
            // Version declaration
            let versionMatch = versionPattern.Match(cleanLine)
            if versionMatch.Success then
                let version = versionMatch.Groups.[1].Value
                if version = "2.0" then
                    Ok state
                else
                    Error $"Line {state.LineNumber}: Unsupported OpenQASM version {version} (only 2.0 supported)"
            
            // Include statement
            elif includePattern.IsMatch(cleanLine) then
                Ok state  // Ignore include statements
            
            // Qreg declaration
            elif qregPattern.IsMatch(cleanLine) then
                let m = qregPattern.Match(cleanLine)
                let count = Int32.Parse(m.Groups.[2].Value)
                
                match state.QubitCount with
                | Some existing ->
                    Error $"Line {state.LineNumber}: Multiple qreg declarations not supported (already have {existing} qubits)"
                | None ->
                    Ok { state with QubitCount = Some count }
            
            // Three-qubit gate (must check before two-qubit)
            elif threeQubitPattern.IsMatch(cleanLine) then
                let m = threeQubitPattern.Match(cleanLine)
                let gateName = m.Groups.[1].Value
                let qubit1 = Int32.Parse(m.Groups.[3].Value)
                let qubit2 = Int32.Parse(m.Groups.[5].Value)
                let qubit3 = Int32.Parse(m.Groups.[7].Value)
                
                match state.QubitCount with
                | None -> Error $"Line {state.LineNumber}: Gate used before qreg declaration"
                | Some qCount ->
                    match parseThreeQubitGate gateName qubit1 qubit2 qubit3 qCount with
                    | Ok gate -> Ok { state with Gates = gate :: state.Gates }
                    | Error msg -> Error $"Line {state.LineNumber}: {msg}"
            
            // Two-qubit rotation gate (with parameter) - must check before two-qubit gate
            elif twoQubitRotationPattern.IsMatch(cleanLine) then
                let m = twoQubitRotationPattern.Match(cleanLine)
                let gateName = m.Groups.[1].Value
                let angleStr = m.Groups.[2].Value
                let qubit1 = Int32.Parse(m.Groups.[4].Value)
                let qubit2 = Int32.Parse(m.Groups.[6].Value)
                
                match state.QubitCount with
                | None -> Error $"Line {state.LineNumber}: Gate used before qreg declaration"
                | Some qCount ->
                    match parseAngle angleStr with
                    | Ok angle ->
                        match parseTwoQubitRotationGate gateName angle qubit1 qubit2 qCount with
                        | Ok gate -> Ok { state with Gates = gate :: state.Gates }
                        | Error msg -> Error $"Line {state.LineNumber}: {msg}"
                    | Error msg -> Error $"Line {state.LineNumber}: {msg}"
            
            // Two-qubit gate
            elif twoQubitPattern.IsMatch(cleanLine) then
                let m = twoQubitPattern.Match(cleanLine)
                let gateName = m.Groups.[1].Value
                let qubit1 = Int32.Parse(m.Groups.[3].Value)
                let qubit2 = Int32.Parse(m.Groups.[5].Value)
                
                match state.QubitCount with
                | None -> Error $"Line {state.LineNumber}: Gate used before qreg declaration"
                | Some qCount ->
                    match parseTwoQubitGate gateName qubit1 qubit2 qCount with
                    | Ok gate -> Ok { state with Gates = gate :: state.Gates }
                    | Error msg -> Error $"Line {state.LineNumber}: {msg}"
            
            // U3 gate (three parameters) - must check before rotation gate (one parameter)
            elif u3Pattern.IsMatch(cleanLine) then
                let m = u3Pattern.Match(cleanLine)
                let gateName = m.Groups.[1].Value
                let thetaStr = m.Groups.[2].Value
                let phiStr = m.Groups.[3].Value
                let lambdaStr = m.Groups.[4].Value
                let qubit = Int32.Parse(m.Groups.[6].Value)
                
                match state.QubitCount with
                | None -> Error $"Line {state.LineNumber}: Gate used before qreg declaration"
                | Some qCount ->
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
                let qubit = Int32.Parse(m.Groups.[4].Value)
                
                match state.QubitCount with
                | None -> Error $"Line {state.LineNumber}: Gate used before qreg declaration"
                | Some qCount ->
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
                let qubit = Int32.Parse(m.Groups.[3].Value)
                
                // Special case: ID gate (identity/no-op) - just ignore it
                if gateName.ToLowerInvariant() = "id" then
                    Ok state  // Skip identity gates (they do nothing)
                else
                    match state.QubitCount with
                    | None -> Error $"Line {state.LineNumber}: Gate used before qreg declaration"
                    | Some qCount ->
                        match parseSingleQubitGate gateName qubit qCount with
                        | Ok gate -> Ok { state with Gates = gate :: state.Gates }
                        | Error msg -> Error $"Line {state.LineNumber}: {msg}"
            
            else
                // Unknown line format - provide helpful error
                if cleanLine.StartsWith("OPENQASM") then
                    Error $"Line {state.LineNumber}: Malformed OPENQASM version declaration"
                elif cleanLine.StartsWith("qreg") then
                    Error $"Line {state.LineNumber}: Malformed qreg declaration"
                elif cleanLine.StartsWith("creg") then
                    Ok state  // Ignore classical register declarations
                elif cleanLine.StartsWith("measure") then
                    Ok state  // Ignore measurement instructions
                elif cleanLine.StartsWith("barrier") then
                    Ok state  // Ignore barrier instructions
                else
                    Error $"Line {state.LineNumber}: Unrecognized instruction: {cleanLine}"
    
    // ========================================================================
    // PUBLIC API
    // ========================================================================
    
    /// <summary>
    /// Parse OpenQASM 2.0 text into a Circuit.
    /// </summary>
    ///
    /// <param name="qasm">OpenQASM 2.0 source code</param>
    /// <returns>Parsed circuit or error message</returns>
    ///
    /// <example>
    /// <code>
    /// let qasm = """
    /// OPENQASM 2.0;
    /// include "qelib1.inc";
    /// qreg q[2];
    /// h q[0];
    /// cx q[0],q[1];
    /// """
    /// 
    /// match OpenQasmImport.parse qasm with
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
            Error "Missing OPENQASM version declaration (expected: OPENQASM 2.0;)"
        else
            // Parse line by line
            let initialState = { QubitCount = None; Gates = []; LineNumber = 1 }
            
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
                | None -> Error "No qreg declaration found"
                | Some qCount ->
                    Ok { QubitCount = qCount; Gates = finalState.Gates |> List.rev }
            | Error msg -> Error msg
    
    /// <summary>
    /// Parse OpenQASM 2.0 file into a Circuit.
    /// </summary>
    ///
    /// <param name="filePath">Path to .qasm file</param>
    /// <returns>Parsed circuit or error message</returns>
    ///
    /// <example>
    /// <code>
    /// match OpenQasmImport.parseFromFile "bell_state.qasm" with
    /// | Ok circuit -> printfn "Loaded circuit from file"
    /// | Error msg -> printfn "Error: %s" msg
    /// </code>
    /// </example>
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
    /// Validate that a string contains valid OpenQASM 2.0.
    /// </summary>
    ///
    /// <param name="qasm">OpenQASM 2.0 source code</param>
    /// <returns>Unit on success, error message on failure</returns>
    let validate (qasm: string) : ParseResult<unit> =
        match parse qasm with
        | Ok _ -> Ok ()
        | Error msg -> Error msg
