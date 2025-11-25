namespace FSharp.Azure.Quantum.Core

open System
open System.IO
open System.Text.Json

/// IonQ Backend Integration
/// 
/// Implements circuit serialization to IonQ format, job submission to ionq.simulator,
/// result parsing, and error mapping.
module IonQBackend =
    
    // ============================================================================
    // TYPE DEFINITIONS (TDD Cycle 1)
    // ============================================================================
    
    /// IonQ gate representation using discriminated unions to avoid JSON null issues
    /// 
    /// Design rationale: Using DUs instead of records with optional fields prevents
    /// JSON serialization null complications. Each gate variant contains exactly
    /// the fields it needs, with no nulls.
    type IonQGate =
        /// Single-qubit gate (H, X, Y, Z, S, T)
        | SingleQubit of gate: string * target: int
        
        /// Single-qubit rotation gate (RX, RY, RZ)
        | SingleQubitRotation of gate: string * target: int * rotation: float
        
        /// Two-qubit gate (CNOT, MS)
        | TwoQubit of gate: string * control: int * target: int
        
        /// Measurement operation
        | Measure of targets: int[]
    
    /// IonQ circuit format (JSON schema)
    /// 
    /// Example JSON:
    /// {
    ///   "qubits": 3,
    ///   "circuit": [
    ///     { "gate": "h", "target": 0 },
    ///     { "gate": "cnot", "control": 0, "target": 1 }
    ///   ]
    /// }
    type IonQCircuit = {
        /// Number of qubits
        Qubits: int
        
        /// Sequence of gates
        Circuit: IonQGate list
    }
    
    // ============================================================================
    // GATE SERIALIZATION (TDD Cycle 2)
    // ============================================================================
    
    /// Serialize a single IonQ gate to JSON string
    /// 
    /// Uses manual JSON construction to avoid null serialization issues.
    /// Each gate variant produces exactly the JSON fields it needs.
    /// 
    /// Examples:
    /// - SingleQubit("h", 0) → {"gate": "h", "target": 0}
    /// - TwoQubit("cnot", 0, 1) → {"gate": "cnot", "control": 0, "target": 1}
    /// - SingleQubitRotation("rx", 2, 1.57) → {"gate": "rx", "target": 2, "rotation": 1.57}
    /// - Measure([|0;1;2|]) → {"gate": "measure", "target": [0, 1, 2]}
    let serializeGate (gate: IonQGate) : string =
        use stream = new System.IO.MemoryStream()
        use writer = new Utf8JsonWriter(stream)
        
        writer.WriteStartObject()
        
        match gate with
        | SingleQubit(gateName, target) ->
            writer.WriteString("gate", gateName)
            writer.WriteNumber("target", target)
        
        | SingleQubitRotation(gateName, target, rotation) ->
            writer.WriteString("gate", gateName)
            writer.WriteNumber("target", target)
            writer.WriteNumber("rotation", rotation)
        
        | TwoQubit(gateName, control, target) ->
            writer.WriteString("gate", gateName)
            writer.WriteNumber("control", control)
            writer.WriteNumber("target", target)
        
        | Measure(targets) ->
            writer.WriteString("gate", "measure")
            writer.WriteStartArray("target")
            for t in targets do
                writer.WriteNumberValue(t)
            writer.WriteEndArray()
        
        writer.WriteEndObject()
        writer.Flush()
        
        System.Text.Encoding.UTF8.GetString(stream.ToArray())
