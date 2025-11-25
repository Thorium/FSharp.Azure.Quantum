namespace FSharp.Azure.Quantum.Core

open System
open System.Text
open FSharp.Azure.Quantum.Core.Types

/// Rigetti Backend Integration
/// 
/// Implements circuit serialization to Quil assembly language, job submission to rigetti.sim.qvm,
/// connectivity graph validation, result parsing, and error mapping.
module RigettiBackend =
    
    // ============================================================================
    // TYPE DEFINITIONS (TDD Cycle 1)
    // ============================================================================
    
    /// Quil gate representation using discriminated unions
    /// 
    /// Design rationale: Quil is a text-based assembly language. Each gate is represented
    /// as a line of assembly code. Using discriminated unions ensures type-safety and
    /// makes serialization to Quil text straightforward.
    /// 
    /// Quil Examples:
    /// - H 0           (Hadamard on qubit 0)
    /// - RX(1.57) 0    (RX rotation by π/2 on qubit 0)
    /// - CZ 0 1        (Controlled-Z between qubits 0 and 1)
    /// - MEASURE 0 ro[0]  (Measure qubit 0, store in classical register ro[0])
    type QuilGate =
        /// Single-qubit gate (H, X, Y, Z, S, T)
        /// Serializes to: "GATE qubit"
        | SingleQubit of gate: string * qubit: int
        
        /// Single-qubit rotation gate (RX, RY, RZ)
        /// Serializes to: "GATE(angle) qubit"
        | SingleQubitRotation of gate: string * angle: float * qubit: int
        
        /// Two-qubit gate (CZ is native for Rigetti)
        /// Serializes to: "GATE control target"
        | TwoQubit of gate: string * control: int * target: int
        
        /// Measurement operation
        /// Serializes to: "MEASURE qubit memoryRef"
        /// Example: "MEASURE 0 ro[0]"
        | Measure of qubit: int * memoryRef: string
        
        /// Classical memory declaration (required for measurements)
        /// Serializes to: "DECLARE name type[size]"
        /// Example: "DECLARE ro BIT[2]"
        | DeclareMemory of name: string * typ: string * size: int
    
    /// Quil program format (assembly text)
    /// 
    /// Example Quil program:
    /// ```
    /// DECLARE ro BIT[2]
    /// H 0
    /// CZ 0 1
    /// MEASURE 0 ro[0]
    /// MEASURE 1 ro[1]
    /// ```
    type QuilProgram = {
        /// Memory declarations (must come first)
        Declarations: QuilGate list
        
        /// Sequence of gate instructions
        Instructions: QuilGate list
    }
    
    /// Connectivity graph representing allowed two-qubit interactions
    /// 
    /// Rigetti systems have limited connectivity. Only certain qubit pairs
    /// can directly interact. The connectivity graph defines the topology.
    /// 
    /// Example (5-qubit linear chain):
    /// ```
    /// 0 -- 1 -- 2 -- 3 -- 4
    /// ```
    /// Edges: {(0,1), (1,2), (2,3), (3,4)}
    /// 
    /// A CZ(0, 3) gate would be INVALID because qubits 0 and 3 are not connected.
    type ConnectivityGraph = {
        /// Set of bidirectional edges (qubit pairs)
        /// Both (a, b) and (b, a) represent the same edge
        Edges: Set<int * int>
    }
    
    // ============================================================================
    // GATE SERIALIZATION (TDD Cycle 2)
    // ============================================================================
    
    /// Serialize a single Quil gate to assembly text
    /// 
    /// Converts QuilGate discriminated unions to Quil assembly language strings.
    /// Uses simple string formatting - no JSON or complex serialization needed.
    /// 
    /// Examples:
    /// - SingleQubit("H", 0) → "H 0"
    /// - SingleQubitRotation("RX", π/2, 0) → "RX(1.5707963267948966) 0"
    /// - TwoQubit("CZ", 0, 1) → "CZ 0 1"
    /// - Measure(0, "ro[0]") → "MEASURE 0 ro[0]"
    /// - DeclareMemory("ro", "BIT", 2) → "DECLARE ro BIT[2]"
    let serializeGate (gate: QuilGate) : string =
        match gate with
        | SingleQubit(gateName, qubit) ->
            sprintf "%s %d" gateName qubit
        
        | SingleQubitRotation(gateName, angle, qubit) ->
            sprintf "%s(%s) %d" gateName (angle.ToString("R")) qubit
        
        | TwoQubit(gateName, control, target) ->
            sprintf "%s %d %d" gateName control target
        
        | Measure(qubit, memoryRef) ->
            sprintf "MEASURE %d %s" qubit memoryRef
        
        | DeclareMemory(name, typ, size) ->
            sprintf "DECLARE %s %s[%d]" name typ size
    
    // ============================================================================
    // PROGRAM SERIALIZATION (TDD Cycle 3)
    // ============================================================================
    
    /// Serialize a complete Quil program to assembly text
    /// 
    /// Converts a QuilProgram (declarations + instructions) to multi-line Quil assembly.
    /// Uses StringBuilder for efficient string concatenation of multiple lines.
    /// 
    /// Format:
    /// 1. Declarations first (DECLARE statements)
    /// 2. Instructions second (gates, measurements)
    /// 3. Newline-separated
    /// 
    /// Example output:
    /// ```
    /// DECLARE ro BIT[2]
    /// H 0
    /// CZ 0 1
    /// MEASURE 0 ro[0]
    /// MEASURE 1 ro[1]
    /// ```
    let serializeProgram (program: QuilProgram) : string =
        let sb = StringBuilder()
        
        // Serialize declarations first
        for decl in program.Declarations do
            if sb.Length > 0 then sb.Append('\n') |> ignore
            sb.Append(serializeGate decl) |> ignore
        
        // Serialize instructions
        for instr in program.Instructions do
            if sb.Length > 0 then sb.Append('\n') |> ignore
            sb.Append(serializeGate instr) |> ignore
        
        sb.ToString()
