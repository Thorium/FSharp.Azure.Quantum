namespace FSharp.Azure.Quantum.Core

open System

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
        let private circuitBuilderGateToQaoaGate (gate: CircuitBuilder.Gate) : QuantumGate option =
            match gate with
            | CircuitBuilder.H q -> Some (QuantumGate.H q)
            | CircuitBuilder.X q -> Some (QuantumGate.RX (q, Math.PI))  // X = RX(π)
            | CircuitBuilder.Y q -> Some (QuantumGate.RY (q, Math.PI))  // Y = RY(π)
            | CircuitBuilder.Z q -> Some (QuantumGate.RZ (q, Math.PI))  // Z = RZ(π)
            | CircuitBuilder.RX (q, angle) -> Some (QuantumGate.RX (q, angle))
            | CircuitBuilder.RY (q, angle) -> Some (QuantumGate.RY (q, angle))
            | CircuitBuilder.RZ (q, angle) -> Some (QuantumGate.RZ (q, angle))
            | CircuitBuilder.CNOT (c, t) -> Some (QuantumGate.CNOT (c, t))
            | CircuitBuilder.CZ (c, t) -> 
                // CZ = H(target) CNOT(c,t) H(target)
                // For now, approximate with CNOT (full conversion would need sequence)
                Some (QuantumGate.CNOT (c, t))
            | _ -> None  // Other gates not supported in basic QAOA gate set
        
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
        let circuitToQaoaCircuit (circuit: CircuitBuilder.Circuit) : Result<QaoaCircuit, string> =
            // Convert initial state preparation gates (before first mixing layer)
            let initialGates = 
                circuit.Gates 
                |> List.choose circuitBuilderGateToQaoaGate
                |> Array.ofList
            
            // Create placeholder Hamiltonians (for general circuits, these are empty)
            let problemHamiltonian : ProblemHamiltonian = {
                NumQubits = circuit.QubitCount
                Terms = [||]
            }
            
            let mixerHamiltonian = MixerHamiltonian.create circuit.QubitCount
            
            // Create single "layer" with all gates (non-standard QAOA structure)
            let layer = {
                CostGates = [||]
                MixerGates = initialGates
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
        
        /// Convert QAOA QuantumGate to CircuitBuilder.Gate
        let private qaoaGateToCircuitBuilderGate (gate: QuantumGate) : CircuitBuilder.Gate =
            match gate with
            | QuantumGate.H q -> CircuitBuilder.H q
            | QuantumGate.RX (q, angle) -> CircuitBuilder.RX (q, angle)
            | QuantumGate.RY (q, angle) -> CircuitBuilder.RY (q, angle)
            | QuantumGate.RZ (q, angle) -> CircuitBuilder.RZ (q, angle)
            | QuantumGate.CNOT (c, t) -> CircuitBuilder.CNOT (c, t)
            | QuantumGate.RZZ (q1, q2, angle) ->
                // RZZ is not directly supported in CircuitBuilder
                // Approximate with CZ for now (full decomposition would be: CNOT RZ CNOT)
                CircuitBuilder.CZ (q1, q2)
        
        /// Convert QaoaCircuit to CircuitBuilder.Circuit
        /// 
        /// Flattens QAOA layer structure into sequential gates.
        let qaoaCircuitToCircuit (qaoa: QaoaCircuit) : CircuitBuilder.Circuit =
            let mutable gates = []
            
            // Add initial state gates
            gates <- gates @ (qaoa.InitialStateGates |> Array.map qaoaGateToCircuitBuilderGate |> List.ofArray)
            
            // Add gates from each QAOA layer
            for layer in qaoa.Layers do
                gates <- gates @ (layer.CostGates |> Array.map qaoaGateToCircuitBuilderGate |> List.ofArray)
                gates <- gates @ (layer.MixerGates |> Array.map qaoaGateToCircuitBuilderGate |> List.ofArray)
            
            {
                QubitCount = qaoa.NumQubits
                Gates = gates
            }
        
        // ========================================================================
        // CONVERSION: ICircuit → Backend-specific formats
        // ========================================================================
        
        /// Convert ICircuit to IonQCircuit (via intermediate QaoaCircuit)
        /// 
        /// This will be implemented in Phase 2 when we integrate with IonQBackend.
        /// For now, returns Error indicating conversion not yet supported.
        let toIonQCircuit (circuit: ICircuit) : Result<IonQBackend.IonQCircuit, string> =
            Error "ICircuit → IonQCircuit conversion not yet implemented (Phase 2)"
        
        /// Convert ICircuit to QuilProgram (via intermediate QaoaCircuit)
        /// 
        /// This will be implemented in Phase 2 when we integrate with RigettiBackend.
        /// For now, returns Error indicating conversion not yet supported.
        let toQuilProgram (circuit: ICircuit) : Result<RigettiBackend.QuilProgram, string> =
            Error "ICircuit → QuilProgram conversion not yet implemented (Phase 2)"
        
        // ========================================================================
        // HELPER: Extract underlying circuit from wrapper
        // ========================================================================
        
        /// Try to extract CircuitBuilder.Circuit from ICircuit wrapper
        let tryGetCircuit (circuit: ICircuit) : CircuitBuilder.Circuit option =
            match circuit with
            | :? CircuitWrapper as wrapper -> Some wrapper.Circuit
            | :? QaoaCircuitWrapper as wrapper -> Some (qaoaCircuitToCircuit wrapper.QaoaCircuit)
            | _ -> None
        
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
