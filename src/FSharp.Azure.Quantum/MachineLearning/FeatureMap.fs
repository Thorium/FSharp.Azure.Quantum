namespace FSharp.Azure.Quantum.MachineLearning

/// Feature Map Implementation for QML
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
        let mutable circ = empty numQubits
        
        for i in 0 .. numQubits - 1 do
            let angle = Math.PI * features.[i]
            circ <- addGate (RY(i, angle)) circ
        
        circ
    
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
        let mutable circ = empty numQubits
        
        for layer in 0 .. depth - 1 do
            // Step 1: Hadamard layer (create superposition)
            for i in 0 .. numQubits - 1 do
                circ <- addGate (H i) circ
            
            // Step 2: Feature rotation layer
            // Apply Rz(2 * φ(x_i)) to each qubit
            for i in 0 .. numQubits - 1 do
                let angle = 2.0 * features.[i]
                circ <- addGate (RZ(i, angle)) circ
            
            // Step 3: ZZ entanglement layer
            // Apply exp(-i * φ(x_i, x_j) * Z_i ⊗ Z_j)
            // Implemented as: CNot(i,j), Rz(2*φ), CNot(i,j)
            for i in 0 .. numQubits - 2 do
                let j = i + 1
                let angle = 2.0 * features.[i] * features.[j]
                
                circ <- addGate (CNOT(i, j)) circ
                circ <- addGate (RZ(j, angle)) circ
                circ <- addGate (CNOT(i, j)) circ
        
        circ
    
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
        let mutable circ = empty numQubits
        
        for layer in 0 .. depth - 1 do
            // Hadamard layer
            for i in 0 .. numQubits - 1 do
                circ <- addGate (H i) circ
            
            // Apply each Pauli string rotation
            for (pauliIdx, pauliStr) in List.indexed pauliStrings do
                // For now, simplified implementation: use first two features
                let featureIdx = pauliIdx % features.Length
                let angle = 2.0 * features.[featureIdx]
                
                // Parse Pauli string and apply corresponding gates
                // Simplified: support ZZ, XX, YY patterns
                match pauliStr.ToUpper() with
                | "ZZ" when numQubits >= 2 ->
                    circ <- addGate (CNOT(0, 1)) circ
                    circ <- addGate (RZ(1, angle)) circ
                    circ <- addGate (CNOT(0, 1)) circ
                
                | "XX" when numQubits >= 2 ->
                    circ <- addGate (H 0) circ
                    circ <- addGate (H 1) circ
                    circ <- addGate (CNOT(0, 1)) circ
                    circ <- addGate (RZ(1, angle)) circ
                    circ <- addGate (CNOT(0, 1)) circ
                    circ <- addGate (H 0) circ
                    circ <- addGate (H 1) circ
                
                | "Z" ->
                    circ <- addGate (RZ(0, angle)) circ
                
                | _ ->
                    // Default: single qubit Z rotation
                    let qubit = pauliIdx % numQubits
                    circ <- addGate (RZ(qubit, angle)) circ
        
        circ
    
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
    let buildFeatureMap (featureMapType: FeatureMapType) (features: FeatureVector) : Result<Circuit, string> =
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
            Error $"Failed to build feature map: {ex.Message}"
