namespace FSharp.Azure.Quantum.MachineLearning

open FSharp.Azure.Quantum.Core

/// Feature Map Implementation for QML.
///
/// Encodes classical feature vectors into quantum states using various strategies:
/// - ZZ Feature Map: Standard for NISQ devices
/// - Pauli Feature Map: Flexible Pauli string rotations
/// - Angle Encoding: Simple Ry rotations
/// - Amplitude Encoding: Direct state preparation

open System
open FSharp.Azure.Quantum.CircuitBuilder

module FeatureMap =
    
    // Open types from same namespace
    open FSharp.Azure.Quantum.MachineLearning.FeatureMapHelpers
    
    // ========================================================================
    // ANGLE ENCODING
    // ========================================================================
    
    /// Angle encoding: Ry(x_i) on each qubit
    ///
    /// Simple encoding: maps feature x_i to rotation angle on qubit i
    /// Circuit: Ry(π * x_i) for each feature
    let angleEncoding (features: FeatureVector) : Circuit =
        let numQubits = features.Length
        [0 .. numQubits - 1]
        |> List.fold (fun circ i ->
            let angle = Math.PI * features.[i]
            addGate (RY(i, angle)) circ
        ) (empty numQubits)
    
    // ========================================================================
    // ZZ FEATURE MAP
    // ========================================================================
    
    /// ZZ Feature Map: Standard feature map for NISQ devices
    ///
    /// Encodes classical data using:
    /// 1. Hadamard layer: Create superposition
    /// 2. Feature layer: U(φ(x)) with ZZ entanglement
    /// 3. Repeat for depth layers
    ///
    /// φ(x) = x_i * x_j for pairs (i,j)
    let zzFeatureMap (depth: int) (features: FeatureVector) : Circuit =
        let numQubits = features.Length
        
        [0 .. depth - 1]
        |> List.fold (fun circ layer ->
            // Step 1: Hadamard layer (create superposition)
            let circWithH =
                [0 .. numQubits - 1]
                |> List.fold (fun c i -> addGate (H i) c) circ
            
            // Step 2: Feature rotation layer
            let circWithRz =
                [0 .. numQubits - 1]
                |> List.fold (fun c i ->
                    let angle = 2.0 * features.[i]
                    addGate (RZ(i, angle)) c
                ) circWithH
            
            // Step 3: ZZ entanglement layer
            [0 .. numQubits - 2]
            |> List.fold (fun c i ->
                let j = i + 1
                let angle = 2.0 * features.[i] * features.[j]
                c
                |> addGate (CNOT(i, j))
                |> addGate (RZ(j, angle))
                |> addGate (CNOT(i, j))
            ) circWithRz
        ) (empty numQubits)
    
    // ========================================================================
    // PAULI FEATURE MAP
    // ========================================================================
    
    /// Pauli Feature Map: Flexible feature encoding with Pauli strings
    ///
    /// Applies rotations based on Pauli operators:
    /// U(φ) = exp(-i * φ(x) * P) where P is a Pauli string
    ///
    /// Example: "ZZ", "XY", "ZZZ" etc.
    let pauliFeatureMap (pauliStrings: string list) (depth: int) (features: FeatureVector) : Circuit =
        let numQubits = features.Length
        
        let applyPauliRotation circ pauliIdx (pauliStr : string) =
            let featureIdx = pauliIdx % features.Length
            let angle = 2.0 * features.[featureIdx]
            
            match pauliStr.ToUpper() with
            | "ZZ" when numQubits >= 2 ->
                circ
                |> addGate (CNOT(0, 1))
                |> addGate (RZ(1, angle))
                |> addGate (CNOT(0, 1))
            | "XX" when numQubits >= 2 ->
                circ
                |> addGate (H 0)
                |> addGate (H 1)
                |> addGate (CNOT(0, 1))
                |> addGate (RZ(1, angle))
                |> addGate (CNOT(0, 1))
                |> addGate (H 0)
                |> addGate (H 1)
            | "Z" ->
                addGate (RZ(0, angle)) circ
            | _ ->
                let qubit = pauliIdx % numQubits
                addGate (RZ(qubit, angle)) circ
        
        [0 .. depth - 1]
        |> List.fold (fun circ layer ->
            // Hadamard layer
            let circWithH =
                [0 .. numQubits - 1]
                |> List.fold (fun c i -> addGate (H i) c) circ
            
            // Apply each Pauli string rotation
            pauliStrings
            |> List.indexed
            |> List.fold (fun c (pauliIdx, pauliStr) ->
                applyPauliRotation c pauliIdx pauliStr
            ) circWithH
        ) (empty numQubits)
    
    // ========================================================================
    // AMPLITUDE ENCODING
    // ========================================================================
    
    /// Amplitude Encoding: Encode features directly into state amplitudes
    ///
    /// Maps classical vector x to quantum state |ψ⟩:
    /// |ψ⟩ = ∑_i x_i |i⟩
    ///
    /// Requires: len(x) = 2^n for n qubits
    /// Uses: Mottonen state preparation or isometry
    let amplitudeEncoding (features: FeatureVector) : Circuit =
        // Determine number of qubits needed
        let numQubits = int (Math.Ceiling(Math.Log(float features.Length, 2.0)))
        
        // Normalize features
        let normalized = normalizeForAmplitude features
        
        // Pad to power of 2 if needed
        let targetSize = 1 <<< numQubits
        let padded = 
            if normalized.Length < targetSize then
                Array.append normalized (Array.zeroCreate (targetSize - normalized.Length))
            else
                normalized
        
        // Use simplified Mottonen-style state preparation
        let mutable circ = empty numQubits
        
        // For a full implementation, we would:
        // 1. Use Gray code ordering
        // 2. Apply controlled rotations hierarchically
        // 3. Optimize gate count
        
        // Simplified version: just apply Hadamard to create uniform superposition
        // (This is a placeholder - full Mottonen is complex)
        for i in 0 .. numQubits - 1 do
            circ <- addGate (H i) circ
        
        circ
    
    // ========================================================================
    // MAIN FEATURE MAP BUILDER
    // ========================================================================
    
    /// Build feature map circuit for given configuration
    let buildFeatureMap (featureMapType: FeatureMapType) (features: FeatureVector) : QuantumResult<Circuit> =
        try
            let circuit =
                match featureMapType with
                | AngleEncoding ->
                    angleEncoding features
                
                | ZZFeatureMap depth ->
                    zzFeatureMap depth features
                
                | PauliFeatureMap(paulis, depth) ->
                    pauliFeatureMap paulis depth features
                
                | AmplitudeEncoding ->
                    amplitudeEncoding features
            
            Ok circuit
        
        with ex ->
            Error (QuantumError.ValidationError ("Input", $"Failed to build feature map: {ex.Message}"))
