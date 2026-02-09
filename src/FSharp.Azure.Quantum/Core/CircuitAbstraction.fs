namespace FSharp.Azure.Quantum.Core

open System
open FSharp.Azure.Quantum

/// Unified circuit abstraction for all quantum backends
/// 
/// This module provides:
/// - ICircuit interface: Common abstraction for all circuit types
/// - CircuitAdapter: Conversion functions between circuit formats
/// 
/// Design rationale:
/// - Backends (IonQ, Rigetti, Local) require different circuit formats
/// - Rather than forcing one format, we use adapters for conversion
/// - ICircuit provides a minimal common interface
/// - Type-safe conversions preserve circuit structure
module CircuitAbstraction =
    
    open FSharp.Azure.Quantum
    open FSharp.Azure.Quantum.Core.QaoaCircuit
    
    // ============================================================================
    // COMMON CIRCUIT INTERFACE
    // ============================================================================
    
    /// Common interface for all circuit types
    /// 
    /// Provides minimal abstraction that all backends understand.
    /// Specific circuit types (QaoaCircuit, IonQCircuit, etc.) can implement this.
    type ICircuit =
        /// Number of qubits in the circuit
        abstract member NumQubits: int
        
        /// Description of the circuit (for debugging/logging)
        abstract member Description: string

    // ============================================================================
    // CIRCUIT WRAPPERS - Make existing types implement ICircuit
    // ============================================================================
    
    /// Wrapper for CircuitBuilder.Circuit to implement ICircuit
    type CircuitWrapper(circuit: CircuitBuilder.Circuit) =
        interface ICircuit with
            member _.NumQubits = circuit.QubitCount
            member _.Description = sprintf "Circuit with %d qubits and %d gates" circuit.QubitCount circuit.Gates.Length
        
        member _.Circuit = circuit
    
    /// Wrapper for QaoaCircuit to implement ICircuit
    type QaoaCircuitWrapper(circuit: QaoaCircuit) =
        interface ICircuit with
            member _.NumQubits = circuit.NumQubits
            member _.Description = sprintf "QAOA circuit with %d qubits, %d layers" circuit.NumQubits circuit.Layers.Length
        
        member _.QaoaCircuit = circuit

    // ============================================================================
    // CIRCUIT ADAPTER - Convert between formats
    // ============================================================================
    
    /// Adapter module for converting between circuit formats
    /// 
    /// Conversion functions maintain circuit semantics while adapting to
    /// backend-specific requirements (gate sets, JSON formats, etc.)
    module CircuitAdapter =
        
        // ========================================================================
        // HELPER: Gate conversion from CircuitBuilder to QAOA format
        // ========================================================================
        
        /// Convert CircuitBuilder.Gate to QaoaCircuit.QuantumGate
        /// 
        /// Note: Some gates require decomposition into multiple QAOA gates.
        /// This function only converts gates 1-to-1. For gates requiring
        /// decomposition (SWAP, CZ), use circuitToQaoaCircuit which handles
        /// gate sequences.
        let private circuitBuilderGateToQaoaGate (gate: CircuitBuilder.Gate) : QuantumGate list =
            match gate with
            | CircuitBuilder.H q -> [QuantumGate.H q]
            | CircuitBuilder.X q -> [QuantumGate.RX (q, Math.PI)]  // X = RX(π)
            | CircuitBuilder.Y q -> [QuantumGate.RY (q, Math.PI)]  // Y = RY(π)
            | CircuitBuilder.Z q -> [QuantumGate.RZ (q, Math.PI)]  // Z = RZ(π)
            | CircuitBuilder.RX (q, angle) -> [QuantumGate.RX (q, angle)]
            | CircuitBuilder.RY (q, angle) -> [QuantumGate.RY (q, angle)]
            | CircuitBuilder.RZ (q, angle) -> [QuantumGate.RZ (q, angle)]
            | CircuitBuilder.U3 (q, theta, phi, lambda) ->
                // U3(θ,φ,λ) = RZ(φ) RY(θ) RZ(λ) decomposition
                [QuantumGate.RZ (q, lambda); QuantumGate.RY (q, theta); QuantumGate.RZ (q, phi)]
            | CircuitBuilder.CNOT (c, t) -> [QuantumGate.CNOT (c, t)]
            | CircuitBuilder.CZ (c, t) -> 
                // CZ = H(target) CNOT(c,t) H(target)
                [QuantumGate.H t; QuantumGate.CNOT (c, t); QuantumGate.H t]
            | CircuitBuilder.SWAP (q1, q2) ->
                // SWAP = CNOT(q1,q2) CNOT(q2,q1) CNOT(q1,q2)
                [QuantumGate.CNOT (q1, q2); QuantumGate.CNOT (q2, q1); QuantumGate.CNOT (q1, q2)]
            | CircuitBuilder.CCX (c1, c2, t) ->
                // Toffoli (CCX) decomposition using 6 CNOTs + T gates (Barenco decomposition)
                // Reference: Nielsen & Chuang, p. 182
                // Simplified version using H + CNOT + RZ for QAOA compatibility
                [
                    QuantumGate.H t
                    QuantumGate.CNOT (c2, t)
                    QuantumGate.RZ (t, -Math.PI / 4.0)  // T† gate
                    QuantumGate.CNOT (c1, t)
                    QuantumGate.RZ (t, Math.PI / 4.0)   // T gate
                    QuantumGate.CNOT (c2, t)
                    QuantumGate.RZ (t, -Math.PI / 4.0)  // T† gate
                    QuantumGate.CNOT (c1, t)
                    QuantumGate.RZ (c2, Math.PI / 4.0)  // T gate on c2
                    QuantumGate.RZ (t, Math.PI / 4.0)   // T gate on target
                    QuantumGate.H t
                    QuantumGate.CNOT (c1, c2)
                    QuantumGate.RZ (c2, -Math.PI / 4.0)  // T† gate on c2
                    QuantumGate.CNOT (c1, c2)
                    QuantumGate.RZ (c1, Math.PI / 4.0)  // T gate on c1
                ]
            | CircuitBuilder.S q -> 
                // S = P(π/2) = diag(1, i)
                // Note: S ≠ RZ(π/2)! They differ by global phase e^(-iπ/4)
                // For QAOA simulation: Use RZ but add phase correction if needed
                [QuantumGate.RZ (q, Math.PI / 2.0)]
            | CircuitBuilder.T q -> 
                // T = P(π/4) = diag(1, e^(iπ/4))
                // Note: T ≠ RZ(π/4)! They differ by global phase e^(-iπ/8)
                [QuantumGate.RZ (q, Math.PI / 4.0)]
            | CircuitBuilder.SDG q -> [QuantumGate.RZ (q, -Math.PI / 2.0)]
            | CircuitBuilder.TDG q -> [QuantumGate.RZ (q, -Math.PI / 4.0)]
            | CircuitBuilder.P (q, theta) -> 
                // P(θ) = diag(1, e^(iθ)) - phase gate
                // Note: P(θ) and RZ(θ) differ by global phase e^(-iθ/2)
                // RZ(θ) = e^(-iθ/2) * P(θ), so P(θ) = e^(iθ/2) * RZ(θ)
                // 
                // For QAOA simulation: Use RZ(θ) approximation
                // Global phase doesn't affect measurement outcomes in QAOA,
                // so this approximation is correct for optimization purposes
                // (but DOES affect controlled operations and interference patterns).
                [QuantumGate.RZ (q, theta)]
            | CircuitBuilder.CP (c, t, theta) ->
                // CP(θ) = Controlled-P(θ) = diag(1, 1, 1, e^(iθ))
                // Standard decomposition using CNOT and RZ gates
                // Reference: Nielsen & Chuang, Section 4.3
                let halfTheta = theta / 2.0
                [
                    QuantumGate.RZ (c, halfTheta)
                    QuantumGate.RZ (t, halfTheta)
                    QuantumGate.CNOT (c, t)
                    QuantumGate.RZ (t, -halfTheta)
                    QuantumGate.CNOT (c, t)
                ]
            | CircuitBuilder.CRX (c, t, angle) ->
                // CRX(θ) = Controlled-RX(θ) decomposition
                // Reference: Nielsen & Chuang
                let halfAngle = angle / 2.0
                [
                    QuantumGate.RX (t, halfAngle)
                    QuantumGate.CNOT (c, t)
                    QuantumGate.RX (t, -halfAngle)
                    QuantumGate.CNOT (c, t)
                ]
            | CircuitBuilder.CRY (c, t, angle) ->
                // CRY(θ) = Controlled-RY(θ) decomposition
                let halfAngle = angle / 2.0
                [
                    QuantumGate.RY (t, halfAngle)
                    QuantumGate.CNOT (c, t)
                    QuantumGate.RY (t, -halfAngle)
                    QuantumGate.CNOT (c, t)
                ]
            | CircuitBuilder.CRZ (c, t, angle) ->
                // CRZ(θ) = Controlled-RZ(θ) decomposition
                let halfAngle = angle / 2.0
                [
                    QuantumGate.RZ (t, halfAngle)
                    QuantumGate.CNOT (c, t)
                    QuantumGate.RZ (t, -halfAngle)
                    QuantumGate.CNOT (c, t)
                ]
            | CircuitBuilder.MCZ (controls, target) ->
                // Multi-controlled Z gate - not directly supported in QAOA
                // MCZ gates are primarily used in Grover's algorithm via LocalBackend
                // For QAOA contexts, this gate should not appear
                failwith "MCZ (multi-controlled Z) gate cannot be converted to QAOA gates. Use LocalBackend for Grover's algorithm."
            | CircuitBuilder.Measure _ ->
                // Measurements are handled separately by the backend
                // Don't include in gate sequence
                []
        
        // ========================================================================
        // CONVERSION: CircuitBuilder.Circuit → QaoaCircuit
        // ========================================================================
        
        /// Convert CircuitBuilder.Circuit to QaoaCircuit
        /// 
        /// This is a best-effort conversion for general circuits.
        /// QAOA circuits have a specific structure (Hamiltonian layers),
        /// so not all circuits can be represented as valid QAOA.
        /// 
        /// Returns Error if circuit cannot be converted.
        let circuitToQaoaCircuit (circuit: CircuitBuilder.Circuit) : Result<QaoaCircuit, QuantumError> =
            // Convert gates, collecting decomposed sequences
            let convertedGates = 
                circuit.Gates
                |> List.rev
                |> List.collect circuitBuilderGateToQaoaGate
            
            // Check if any non-measurement gates failed to convert
            // Measure gates intentionally return [] (handled separately by the backend)
            let unconvertedCount = 
                circuit.Gates 
                |> List.filter (fun gate -> match gate with CircuitBuilder.Measure _ -> false | _ -> true)
                |> List.map circuitBuilderGateToQaoaGate 
                |> List.filter (fun gates -> gates.IsEmpty)
                |> List.length
            
            if unconvertedCount > 0 then
                Error (QuantumError.OperationError(
                    "Circuit conversion", 
                    $"Failed to convert {unconvertedCount} gates from CircuitBuilder to QAOA format - some gate types are not supported in QAOA"))
            else
                // Create placeholder Hamiltonians (for general circuits, these are empty)
                let problemHamiltonian : ProblemHamiltonian = {
                    NumQubits = circuit.QubitCount
                    Terms = [||]
                }
                
                let mixerHamiltonian = MixerHamiltonian.create circuit.QubitCount
                
                // Create single "layer" with all gates (non-standard QAOA structure)
                let layer = {
                    CostGates = [||]
                    MixerGates = convertedGates |> Array.ofList
                    Gamma = 0.0
                    Beta = 0.0
                }
                
                Ok {
                    NumQubits = circuit.QubitCount
                    InitialStateGates = [||]  // Gates are in layer instead
                    Layers = [| layer |]
                    ProblemHamiltonian = problemHamiltonian
                    MixerHamiltonian = mixerHamiltonian
                }
        
        // ========================================================================
        // CONVERSION: QaoaCircuit → CircuitBuilder.Circuit
        // ========================================================================
        
        /// Convert QAOA QuantumGate to CircuitBuilder.Gate list
        /// Returns a list because some gates (e.g., RZZ) decompose into multiple gates
        let private qaoaGateToCircuitBuilderGates (gate: QuantumGate) : CircuitBuilder.Gate list =
            match gate with
            | QuantumGate.H q -> [CircuitBuilder.H q]
            | QuantumGate.RX (q, angle) -> [CircuitBuilder.RX (q, angle)]
            | QuantumGate.RY (q, angle) -> [CircuitBuilder.RY (q, angle)]
            | QuantumGate.RZ (q, angle) -> [CircuitBuilder.RZ (q, angle)]
            | QuantumGate.CNOT (c, t) -> [CircuitBuilder.CNOT (c, t)]
            | QuantumGate.RZZ (q1, q2, angle) ->
                // RZZ(θ) = CNOT(q1,q2) · RZ(q2, θ) · CNOT(q1,q2)
                [
                    CircuitBuilder.CNOT (q1, q2)
                    CircuitBuilder.RZ (q2, angle)
                    CircuitBuilder.CNOT (q1, q2)
                ]
        
        /// Convert QaoaCircuit to CircuitBuilder.Circuit
        /// 
        /// Flattens QAOA layer structure into sequential gates.
        let qaoaCircuitToCircuit (qaoa: QaoaCircuit) : CircuitBuilder.Circuit =
            // Convert initial state gates
            let initialGates = 
                qaoa.InitialStateGates 
                |> Array.toList
                |> List.collect qaoaGateToCircuitBuilderGates
            
            // Convert all layer gates (cost + mixer) in sequence
            let layerGates =
                qaoa.Layers
                |> Array.collect (fun layer ->
                    Array.append layer.CostGates layer.MixerGates
                )
                |> Array.toList
                |> List.collect qaoaGateToCircuitBuilderGates
            
            // Combine all gates
            let allGates = initialGates @ layerGates
            
            {
                QubitCount = qaoa.NumQubits
                Gates = allGates
            }
        
        // ========================================================================
        // CONVERSION: CircuitBuilder.Gate → Backend-specific formats
        // ========================================================================
        
        /// Convert CircuitBuilder.Gate to IonQ gate format
        let private circuitBuilderGateToIonQGate (gate: CircuitBuilder.Gate) : IonQBackend.IonQGate option =
            match gate with
            // Single-qubit gates
            | CircuitBuilder.H q -> Some (IonQBackend.SingleQubit ("h", q))
            | CircuitBuilder.X q -> Some (IonQBackend.SingleQubit ("x", q))
            | CircuitBuilder.Y q -> Some (IonQBackend.SingleQubit ("y", q))
            | CircuitBuilder.Z q -> Some (IonQBackend.SingleQubit ("z", q))
            | CircuitBuilder.S q -> Some (IonQBackend.SingleQubit ("s", q))
            | CircuitBuilder.T q -> Some (IonQBackend.SingleQubit ("t", q))
            
            // Rotation gates
            | CircuitBuilder.RX (q, angle) -> Some (IonQBackend.SingleQubitRotation ("rx", q, angle))
            | CircuitBuilder.RY (q, angle) -> Some (IonQBackend.SingleQubitRotation ("ry", q, angle))
            | CircuitBuilder.RZ (q, angle) -> Some (IonQBackend.SingleQubitRotation ("rz", q, angle))
            
            // Two-qubit gates
            | CircuitBuilder.CNOT (c, t) -> Some (IonQBackend.TwoQubit ("cnot", c, t))
            | CircuitBuilder.CZ (c, t) -> Some (IonQBackend.TwoQubit ("cz", c, t))
            | CircuitBuilder.SWAP (q1, q2) -> Some (IonQBackend.TwoQubit ("swap", q1, q2))
            
            | _ -> None  // Unsupported gate
        
        /// Convert CircuitBuilder.Gate to Quil instruction
        let private circuitBuilderGateToQuilGate (gate: CircuitBuilder.Gate) : RigettiBackend.QuilGate option =
            match gate with
            // Single-qubit gates
            | CircuitBuilder.H q -> Some (RigettiBackend.SingleQubit ("H", q))
            | CircuitBuilder.X q -> Some (RigettiBackend.SingleQubit ("X", q))
            | CircuitBuilder.Y q -> Some (RigettiBackend.SingleQubit ("Y", q))
            | CircuitBuilder.Z q -> Some (RigettiBackend.SingleQubit ("Z", q))
            | CircuitBuilder.S q -> Some (RigettiBackend.SingleQubit ("S", q))
            | CircuitBuilder.T q -> Some (RigettiBackend.SingleQubit ("T", q))
            
            // Rotation gates
            | CircuitBuilder.RX (q, angle) -> Some (RigettiBackend.SingleQubitRotation ("RX", angle, q))
            | CircuitBuilder.RY (q, angle) -> Some (RigettiBackend.SingleQubitRotation ("RY", angle, q))
            | CircuitBuilder.RZ (q, angle) -> Some (RigettiBackend.SingleQubitRotation ("RZ", angle, q))
            
            // Two-qubit gates (CZ is native for Rigetti)
            | CircuitBuilder.CZ (c, t) -> Some (RigettiBackend.TwoQubit ("CZ", c, t))
            | CircuitBuilder.CNOT (c, t) -> 
                // CNOT can be decomposed to H-CZ-H on target, but for simplicity
                // Rigetti also supports CNOT directly
                Some (RigettiBackend.TwoQubit ("CNOT", c, t))
            | CircuitBuilder.SWAP (q1, q2) -> Some (RigettiBackend.TwoQubit ("SWAP", q1, q2))
            
            | _ -> None  // Unsupported gate
        
        // ========================================================================
        // HELPER: Extract underlying circuit from wrapper
        // ========================================================================
        
        /// Try to extract CircuitBuilder.Circuit from ICircuit wrapper
        let tryGetCircuit (circuit: ICircuit) : CircuitBuilder.Circuit option =
            match circuit with
            | :? CircuitWrapper as wrapper -> Some wrapper.Circuit
            | :? QaoaCircuitWrapper as wrapper -> Some (qaoaCircuitToCircuit wrapper.QaoaCircuit)
            | _ -> None
        
        // ========================================================================
        // CONVERSION: ICircuit → Backend-specific formats
        // ========================================================================
        
        /// Convert ICircuit to IonQCircuit
        /// 
        /// Extracts the underlying CircuitBuilder.Circuit and converts gates to IonQ format.
        let toIonQCircuit (circuit: ICircuit) : Result<IonQBackend.IonQCircuit, QuantumError> =
            match tryGetCircuit circuit with
            | None -> Error (QuantumError.OperationError("Circuit extraction", "Cannot extract CircuitBuilder.Circuit from ICircuit wrapper"))
            | Some builderCircuit ->
                // Convert all gates
                let convertedGates =
                    builderCircuit.Gates
                    |> List.rev
                    |> List.choose circuitBuilderGateToIonQGate
                
                // Check if any gates failed to convert
                if convertedGates.Length < builderCircuit.Gates.Length then
                    let unsupportedCount = builderCircuit.Gates.Length - convertedGates.Length
                    Error (QuantumError.OperationError(
                        "Circuit conversion", 
                        $"Failed to convert {unsupportedCount} gates to IonQ format - some gate types are not supported by IonQ backend"))
                else
                    Ok {
                        IonQBackend.Qubits = builderCircuit.QubitCount
                        IonQBackend.Circuit = convertedGates
                    }
        
        /// Convert ICircuit to QuilProgram
        /// 
        /// Extracts the underlying CircuitBuilder.Circuit and converts to Quil instructions.
        /// Automatically adds memory declarations for measurements.
        let toQuilProgram (circuit: ICircuit) : Result<RigettiBackend.QuilProgram, QuantumError> =
            match tryGetCircuit circuit with
            | None -> Error (QuantumError.OperationError("Circuit extraction", "Cannot extract CircuitBuilder.Circuit from ICircuit wrapper"))
            | Some builderCircuit ->
                // Convert all gates to Quil instructions
                let instructions =
                    builderCircuit.Gates
                    |> List.rev
                    |> List.choose circuitBuilderGateToQuilGate
                
                // Check if any gates failed to convert
                if instructions.Length < builderCircuit.Gates.Length then
                    let unsupportedCount = builderCircuit.Gates.Length - instructions.Length
                    Error (QuantumError.OperationError(
                        "Circuit conversion", 
                        $"Failed to convert {unsupportedCount} gates to Quil format - some gate types are not supported by Rigetti backend"))
                else
                    // Add memory declaration for measurement results
                    // Rigetti requires: DECLARE ro BIT[numQubits]
                    let memoryDeclaration = 
                        RigettiBackend.DeclareMemory ("ro", "BIT", builderCircuit.QubitCount)
                    
                    Ok {
                        RigettiBackend.Declarations = [memoryDeclaration]
                        RigettiBackend.Instructions = instructions
                    }
        
        /// Try to extract QaoaCircuit from ICircuit wrapper
        let tryGetQaoaCircuit (circuit: ICircuit) : QaoaCircuit option =
            match circuit with
            | :? QaoaCircuitWrapper as wrapper -> Some wrapper.QaoaCircuit
            | :? CircuitWrapper as wrapper -> 
                match circuitToQaoaCircuit wrapper.Circuit with
                | Ok qaoa -> Some qaoa
                | Error _ -> None
            | _ -> None

    // ============================================================================
    // CONVENIENCE FUNCTIONS - Wrap circuits in ICircuit interface
    // ============================================================================
    
    /// Wrap a CircuitBuilder.Circuit to implement ICircuit
    let wrapCircuit (circuit: CircuitBuilder.Circuit) : ICircuit =
        CircuitWrapper(circuit) :> ICircuit
    
    /// Wrap a QaoaCircuit to implement ICircuit
    let wrapQaoaCircuit (circuit: QaoaCircuit) : ICircuit =
        QaoaCircuitWrapper(circuit) :> ICircuit
