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
    /// Uses: Möttönen state preparation algorithm
    ///
    /// Reference: M. Möttönen et al., "Transformation of quantum states using
    /// uniformly controlled rotations", Quant. Inf. Comp. 5, 467 (2005).
    let amplitudeEncoding (features: FeatureVector) : Circuit =
        // Determine number of qubits needed
        let numQubits = int (Math.Ceiling(Math.Log(float features.Length, 2.0)))
        
        // Normalize features
        let normalized = normalizeForAmplitude features
        
        // Pad to power of 2 if needed
        let targetSize = 1 <<< numQubits
        let amplitudes = 
            if normalized.Length < targetSize then
                Array.append normalized (Array.zeroCreate (targetSize - normalized.Length))
            else
                normalized
        
        // Möttönen state preparation:
        // Recursively decompose the target state into uniformly-controlled RY rotations.
        // At each level k (from n-1 down to 0), compute rotation angles from pairs
        // of amplitudes and apply controlled rotations.
        
        /// Compute rotation angles for a level of the Möttönen decomposition.
        /// Given 2^(k+1) amplitudes at level k, compute 2^k rotation angles.
        let computeAngles (amps: float array) : float array =
            let halfLen = amps.Length / 2
            Array.init halfLen (fun i ->
                let a = amps.[2 * i]
                let b = amps.[2 * i + 1]
                let norm = Math.Sqrt(a * a + b * b)
                if norm < 1e-15 then 0.0
                else 2.0 * Math.Atan2(b, a))
        
        /// Recursively compute all RY angles for each qubit level.
        /// Qubit q holds bit q of the feature index, so the level that pairs over
        /// feature bit 0 targets qubit 0, and the final single-angle level targets
        /// qubit numQubits-1 (the feature MSB, prepared first).
        let rec computeAllAngles (amps: float array) (qubitIdx: int) : (int * float array) list =
            if amps.Length <= 1 then []
            else
                let angles = computeAngles amps
                // Compute residual amplitudes for next level (norms of pairs)
                let residuals =
                    Array.init (amps.Length / 2) (fun i ->
                        let a = amps.[2 * i]
                        let b = amps.[2 * i + 1]
                        Math.Sqrt(a * a + b * b))
                (qubitIdx, angles) :: computeAllAngles residuals (qubitIdx + 1)

        let allAngles = computeAllAngles amplitudes 0
        
        // Helper: find bit position of lowest set bit (trailing zero count)
        let trailingZeros n =
            let rec go d pos =
                if d <= 1 then pos
                else go (d >>> 1) (pos + 1)
            go n 0
        
        // Walsh-Hadamard transform (in-place on a copy — inherently imperative)
        let walshHadamardTransform (angles: float[]) =
            let transformed = Array.copy angles
            let rec applyStep step =
                if step >= transformed.Length then ()
                else
                    // Standard iterative WHT butterfly: blocks of size 2*step,
                    // pairing element i with element i+step within each block.
                    for blockStart in 0 .. 2 * step .. transformed.Length - 1 do
                        for j in 0 .. step - 1 do
                            if blockStart + j + step < transformed.Length then
                                let u = transformed.[blockStart + j]
                                let v = transformed.[blockStart + j + step]
                                transformed.[blockStart + j] <- (u + v) / 2.0
                                transformed.[blockStart + j + step] <- (u - v) / 2.0
                    applyStep (step * 2)
            applyStep 1
            transformed
        
        // Apply uniformly-controlled rotations from most significant to least significant qubit
        let circ =
            (empty numQubits, allAngles |> List.rev)
            ||> List.fold (fun circ (targetQubit, angles) ->
                if angles.Length = 1 then
                    // No controls needed — single RY on the target qubit
                    if abs angles.[0] > 1e-15 then
                        addGate (RY(targetQubit, angles.[0])) circ
                    else
                        circ
                else
                    // Uniformly-controlled RY (multiplexer) over the already-prepared
                    // higher qubits: for control basis state |r⟩, apply RY(angle_r).
                    // Möttönen decomposition: Walsh-Hadamard-transformed angles read in
                    // Gray-code order, with a CNOT between consecutive rotations on the
                    // control bit where the Gray code changes. The CNOT parity chain is
                    // structural — a CNOT must be emitted even when its rotation is zero,
                    // and the final CNOT (wrapping to Gray code 0) closes the chain on
                    // the most significant control.
                    let controlQubits = [| targetQubit + 1 .. numQubits - 1 |]

                    let transformed = walshHadamardTransform angles

                    if transformed |> Array.forall (fun a -> abs a <= 1e-15) then
                        // Identity multiplexer — the CNOTs would cancel pairwise
                        circ
                    else
                        let n = transformed.Length
                        (circ, [0 .. n - 1])
                        ||> List.fold (fun c k ->
                            let grayK = k ^^^ (k >>> 1)
                            let next = (k + 1) % n
                            let grayNext = next ^^^ (next >>> 1)
                            let bitPos = trailingZeros (grayK ^^^ grayNext)
                            let angle = transformed.[grayK]
                            let c' =
                                if abs angle > 1e-15 then
                                    addGate (RY(targetQubit, angle)) c
                                else
                                    c
                            addGate (CNOT(controlQubits.[bitPos], targetQubit)) c'))

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
