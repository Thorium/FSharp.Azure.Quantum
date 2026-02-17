namespace FSharp.Azure.Quantum

open System.Text.RegularExpressions

/// OpenQASM Version Configuration Module
///
/// Provides version-parameterized types and configuration for OpenQASM 1.0, 2.0, and 3.0.
/// All version-dependent behavior is driven by the QasmVersion discriminated union and
/// the QasmConfig record, enabling a single codepath to handle multiple OpenQASM versions.
///
/// ## Architecture
/// - QasmVersion DU: V1_0 | V2_0 | V3_0
/// - QasmConfig record: captures all version-specific settings
/// - configFor: QasmVersion -> QasmConfig constructor
/// - Version detection from parsed header strings
///
/// ## Usage
/// ```fsharp
/// let config = OpenQasmVersion.configFor V2_0
/// let qasm = OpenQasmExport.exportWithConfig config circuit
/// ```
module OpenQasmVersion =

    // ========================================================================
    // VERSION TYPES
    // ========================================================================

    /// OpenQASM language version
    type QasmVersion =
        | V1_0
        | V2_0
        | V3_0

    /// Measurement syntax style
    type MeasureStyle =
        /// OpenQASM 2.0 style: measure q[0] -> c[0];
        | ArrowSyntax
        /// OpenQASM 3.0 style: c[0] = measure q[0];
        | AssignmentSyntax

    /// Register declaration style
    type RegisterStyle =
        /// OpenQASM 1.0/2.0 style: qreg q[n]; creg c[n];
        | QregCreg
        /// OpenQASM 3.0 style: qubit[n] q; bit[n] c;
        | QubitBit

    /// Version-specific configuration record.
    /// All version-dependent export/import behavior is driven by this record.
    type QasmConfig = {
        /// The OpenQASM version
        Version: QasmVersion
        /// Version string for the header (e.g., "2.0", "3.0")
        VersionString: string
        /// Include file name (e.g., "qelib1.inc", "stdgates.inc")
        IncludeFile: string
        /// Register declaration style
        RegisterStyle: RegisterStyle
        /// Measurement syntax style
        MeasureStyle: MeasureStyle
    }

    // ========================================================================
    // VERSION CONFIGURATION CONSTRUCTORS
    // ========================================================================

    /// Create configuration for a specific OpenQASM version.
    ///
    /// V1_0: Minimal pre-publication format (subset of 2.0)
    /// V2_0: Standard IBM Qiskit format with qelib1.inc
    /// V3_0: Modern format with stdgates.inc, qubit/bit declarations, assignment measurement
    let configFor (version: QasmVersion) : QasmConfig =
        match version with
        | V1_0 ->
            { Version = V1_0
              VersionString = "1.0"
              IncludeFile = "qelib1.inc"
              RegisterStyle = QregCreg
              MeasureStyle = ArrowSyntax }
        | V2_0 ->
            { Version = V2_0
              VersionString = "2.0"
              IncludeFile = "qelib1.inc"
              RegisterStyle = QregCreg
              MeasureStyle = ArrowSyntax }
        | V3_0 ->
            { Version = V3_0
              VersionString = "3.0"
              IncludeFile = "stdgates.inc"
              RegisterStyle = QubitBit
              MeasureStyle = AssignmentSyntax }

    // ========================================================================
    // VERSION DETECTION
    // ========================================================================

    /// Regex to extract version number from OPENQASM header
    let private versionHeaderPattern =
        Regex(@"^\s*OPENQASM\s+(\d+)\.(\d+)\s*;", RegexOptions.Compiled ||| RegexOptions.Multiline)

    /// Detect QasmVersion from an OpenQASM header string.
    /// Returns Ok with detected version, or Error with message for unsupported versions.
    let detectVersion (qasm: string) : Result<QasmVersion, string> =
        let m = versionHeaderPattern.Match(qasm)
        if not m.Success then
            Error "Missing OPENQASM version declaration"
        else
            let major = m.Groups.[1].Value
            let minor = m.Groups.[2].Value
            match major, minor with
            | "1", "0" -> Ok V1_0
            | "2", "0" -> Ok V2_0
            | "3", "0" -> Ok V3_0
            | _ -> Error $"Unsupported OpenQASM version {major}.{minor} (supported: 1.0, 2.0, 3.0)"

    /// Parse a version string (e.g., "2.0") into a QasmVersion.
    let parseVersionString (versionStr: string) : Result<QasmVersion, string> =
        match versionStr with
        | "1.0" -> Ok V1_0
        | "2.0" -> Ok V2_0
        | "3.0" | "3" -> Ok V3_0
        | _ -> Error $"Unsupported OpenQASM version {versionStr} (supported: 1.0, 2.0, 3.0)"

    // ========================================================================
    // HEADER GENERATION
    // ========================================================================

    /// Generate the OpenQASM header line for a version.
    let headerLine (config: QasmConfig) : string =
        $"OPENQASM {config.VersionString};"

    /// Generate the include line for a version.
    let includeLine (config: QasmConfig) : string =
        $"include \"{config.IncludeFile}\";"

    /// Generate the qubit register declaration for a version.
    let qubitRegisterDecl (config: QasmConfig) (qubitCount: int) : string =
        match config.RegisterStyle with
        | QregCreg -> $"qreg q[{qubitCount}];"
        | QubitBit -> $"qubit[{qubitCount}] q;"

    /// Generate a classical register declaration for a version (used with measurement).
    let classicalRegisterDecl (config: QasmConfig) (bitCount: int) : string =
        match config.RegisterStyle with
        | QregCreg -> $"creg c[{bitCount}];"
        | QubitBit -> $"bit[{bitCount}] c;"

    /// Generate a measurement instruction for a version.
    let measureInstruction (config: QasmConfig) (qubit: int) : string =
        match config.MeasureStyle with
        | ArrowSyntax -> $"measure q[{qubit}] -> c[{qubit}];"
        | AssignmentSyntax -> $"c[{qubit}] = measure q[{qubit}];"

    // ========================================================================
    // VERSION DISPLAY
    // ========================================================================

    /// Convert QasmVersion to display string.
    let versionToString (version: QasmVersion) : string =
        match version with
        | V1_0 -> "1.0"
        | V2_0 -> "2.0"
        | V3_0 -> "3.0"
