namespace FSharp.Azure.Quantum.MachineLearning

open FSharp.Azure.Quantum.Core

/// Variational Form (Ansatz) Implementation for QML.
///
/// Parameterized quantum circuits for variational algorithms:
/// - RealAmplitudes: Hardware-efficient with Ry rotations
/// - TwoLocal: Flexible single + two-qubit layers
/// - EfficientSU2: Full SU(2) coverage with Ry + Rz

open System
open FSharp.Azure.Quantum.CircuitBuilder

module VariationalForms =
    
    open FSharp.Azure.Quantum.MachineLearning.AnsatzHelpers
    
    // ========================================================================
    // REAL AMPLITUDES ANSATZ
    // ========================================================================
    
    /// Real Amplitudes ansatz: Hardware-efficient with Ry rotations + CZ entanglement
    ///
    /// Structure per layer:
    /// 1. Ry(θ_i) rotation on each qubit
    /// 2. CZ entanglement between neighbors
    ///
    /// Parameters: numQubits * depth rotation angles
    let realAmplitudes (depth: int) (parameters: float array) (numQubits: int) : QuantumResult<Circuit> =
        // Validate parameters
        let expectedParams = numQubits * depth
        if parameters.Length <> expectedParams then
            Error (QuantumError.ValidationError ("Input", $"RealAmplitudes requires {expectedParams} parameters for {numQubits} qubits and depth {depth}, got {parameters.Length}"))
        else
            let (finalCircuit, _) =
                [0 .. depth - 1]
                |> List.fold (fun (circ, paramIdx) layer ->
                    // Layer 1: Ry rotations on all qubits
                    let (circWithRy, newParamIdx) =
                        [0 .. numQubits - 1]
                        |> List.fold (fun (c, pIdx) qubit ->
                            let angle = parameters.[pIdx]
                            (addGate (RY(qubit, angle)) c, pIdx + 1)
                        ) (circ, paramIdx)
                    
                    // Layer 2: CZ entanglement (linear connectivity)
                    let circWithCz =
                        if layer < depth - 1 || depth = 1 then
                            [0 .. numQubits - 2]
                            |> List.fold (fun c qubit ->
                                addGate (CZ(qubit, qubit + 1)) c
                            ) circWithRy
                        else
                            circWithRy
                    
                    (circWithCz, newParamIdx)
                ) (empty numQubits, 0)
            
            Ok finalCircuit
    
    // ========================================================================
    // TWO-LOCAL ANSATZ
    // ========================================================================
    
    /// Two-Local ansatz: Customizable rotation + entanglement layers
    ///
    /// Structure per layer:
    /// 1. Single-qubit rotations (configurable: Rx, Ry, Rz)
    /// 2. Two-qubit entanglement (configurable: CX, CZ, etc.)
    ///
    /// Parameters: numQubits * depth rotation angles
    let twoLocal 
        (rotation: string) 
        (entanglement: string) 
        (depth: int) 
        (parameters: float array) 
        (numQubits: int) 
        : QuantumResult<Circuit> =
        
        // Validate parameters
        let expectedParams = numQubits * depth
        if parameters.Length <> expectedParams then
            Error (QuantumError.ValidationError ("Input", $"TwoLocal requires {expectedParams} parameters for {numQubits} qubits and depth {depth}, got {parameters.Length}"))
        else
            // Parse rotation type
            let rotationGate = 
                match rotation.ToUpper() with
                | "RX" -> fun q angle -> RX(q, angle)
                | "RY" -> fun q angle -> RY(q, angle)
                | "RZ" -> fun q angle -> RZ(q, angle)
                | _ -> fun q angle -> RY(q, angle)  // Default to Ry
            
            // Parse entanglement type
            let entanglementGate =
                match entanglement.ToUpper() with
                | "CX" | "CNOT" -> fun q1 q2 -> CNOT(q1, q2)
                | "CZ" -> fun q1 q2 -> CZ(q1, q2)
                | _ -> fun q1 q2 -> CZ(q1, q2)  // Default to CZ
            
            let (finalCircuit, _) =
                [0 .. depth - 1]
                |> List.fold (fun (circ, paramIdx) layer ->
                    // Layer 1: Single-qubit rotations
                    let (circWithRot, newParamIdx) =
                        [0 .. numQubits - 1]
                        |> List.fold (fun (c, pIdx) qubit ->
                            let angle = parameters.[pIdx]
                            (addGate (rotationGate qubit angle) c, pIdx + 1)
                        ) (circ, paramIdx)
                    
                    // Layer 2: Two-qubit entanglement
                    let circWithEnt =
                        if layer < depth - 1 || depth = 1 then
                            [0 .. numQubits - 2]
                            |> List.fold (fun c qubit ->
                                addGate (entanglementGate qubit (qubit + 1)) c
                            ) circWithRot
                        else
                            circWithRot
                    
                    (circWithEnt, newParamIdx)
                ) (empty numQubits, 0)
            
            Ok finalCircuit
    
    // ========================================================================
    // EFFICIENT SU(2) ANSATZ
    // ========================================================================
    
    /// Efficient SU(2) ansatz: Full SU(2) coverage with Ry + Rz rotations
    ///
    /// Structure per layer:
    /// 1. Ry(θ_i) + Rz(φ_i) on each qubit (full SU(2))
    /// 2. CX entanglement between neighbors
    ///
    /// Parameters: 2 * numQubits * depth rotation angles
    let efficientSU2 (depth: int) (parameters: float array) (numQubits: int) : QuantumResult<Circuit> =
        // Validate parameters
        let expectedParams = 2 * numQubits * depth
        if parameters.Length <> expectedParams then
            Error (QuantumError.ValidationError ("Input", $"EfficientSU2 requires {expectedParams} parameters (2 per qubit) for {numQubits} qubits and depth {depth}, got {parameters.Length}"))
        else
            let mutable circuit = empty numQubits
            let mutable paramIdx = 0
            
            for layer in 0 .. depth - 1 do
                // Layer 1: Ry + Rz rotations on all qubits
                for qubit in 0 .. numQubits - 1 do
                    let angleRy = parameters.[paramIdx]
                    let angleRz = parameters.[paramIdx + 1]
                    
                    circuit <- addGate (RY(qubit, angleRy)) circuit
                    circuit <- addGate (RZ(qubit, angleRz)) circuit
                    
                    paramIdx <- paramIdx + 2
                
                // Layer 2: CX entanglement
                if layer < depth - 1 || depth = 1 then
                    for qubit in 0 .. numQubits - 2 do
                        circuit <- addGate (CNOT(qubit, qubit + 1)) circuit
            
            Ok circuit
    
    // ========================================================================
    // MAIN VARIATIONAL FORM BUILDER
    // ========================================================================
    
    /// Build variational form circuit for given configuration
    ///
    /// Returns Result with circuit or error message
    let buildVariationalForm 
        (ansatz: VariationalForm) 
        (parameters: float array) 
        (numQubits: int) 
        : QuantumResult<Circuit> =
        
        if numQubits < 1 then
            Error (QuantumError.ValidationError ("Input", "Number of qubits must be at least 1"))
        else
            match ansatz with
            | RealAmplitudes depth ->
                if depth < 1 then
                    Error (QuantumError.ValidationError ("Input", "Depth must be at least 1"))
                else
                    realAmplitudes depth parameters numQubits
            
            | TwoLocal(rotation, entanglement, depth) ->
                if depth < 1 then
                    Error (QuantumError.ValidationError ("Input", "Depth must be at least 1"))
                else
                    twoLocal rotation entanglement depth parameters numQubits
            
            | EfficientSU2 depth ->
                if depth < 1 then
                    Error (QuantumError.ValidationError ("Input", "Depth must be at least 1"))
                else
                    efficientSU2 depth parameters numQubits
    
    // ========================================================================
    // PARAMETER INITIALIZATION
    // ========================================================================
    
    /// Initialize parameters with random values in [0, 2π]
    let randomParameters 
        (ansatz: VariationalForm) 
        (numQubits: int) 
        (seed: int option) 
        : float array =
        
        randomInitialParameters ansatz numQubits seed
    
    /// Initialize parameters with zeros
    let zeroParameters (ansatz: VariationalForm) (numQubits: int) : float array =
        let count = parameterCount ansatz numQubits
        Array.zeroCreate count
    
    /// Initialize parameters with specific value
    let constantParameters (ansatz: VariationalForm) (numQubits: int) (value: float) : float array =
        let count = parameterCount ansatz numQubits
        Array.create count value
    
    // ========================================================================
    // CIRCUIT COMPOSITION
    // ========================================================================
    
    /// Compose feature map + variational form into single circuit
    ///
    /// Creates complete QML circuit: |0⟩ -> FeatureMap(x) -> Ansatz(θ)
    let composeWithFeatureMap 
        (featureMapCircuit: Circuit)
        (variationalCircuit: Circuit)
        : QuantumResult<Circuit> =
        
        if featureMapCircuit.QubitCount <> variationalCircuit.QubitCount then
            Error (QuantumError.ValidationError ("Input", $"Qubit count mismatch: feature map has {featureMapCircuit.QubitCount} qubits, variational form has {variationalCircuit.QubitCount} qubits"))
        else
            try
                let composed = compose featureMapCircuit variationalCircuit
                Ok composed
            with ex ->
                Error (QuantumError.ValidationError ("Input", $"Failed to compose circuits: {ex.Message}"))
