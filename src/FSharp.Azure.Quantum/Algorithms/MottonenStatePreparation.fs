namespace FSharp.Azure.Quantum.Algorithms

open System
open System.Numerics
open FSharp.Azure.Quantum.Core.CircuitAbstraction
open FSharp.Azure.Quantum.CircuitBuilder

/// Möttönen's State Preparation Algorithm
/// 
/// Prepares arbitrary quantum state |ψ⟩ = Σᵢ αᵢ|i⟩ from |0...0⟩
/// where αᵢ are complex amplitudes satisfying Σᵢ|αᵢ|² = 1
/// 
/// **Algorithm Overview (Möttönen & Vartiainen, 2004):**
/// 1. Decompose state into amplitude (|αᵢ|) and phase (arg(αᵢ))
/// 2. Prepare amplitude distribution using controlled-RY gates
/// 3. Apply phase corrections using controlled-RZ gates
/// 4. Use Gray code traversal for efficient gate decomposition
/// 
/// **Circuit Depth:** O(2ⁿ) gates for n qubits, O(n·2ⁿ) total
/// 
/// **Applications:**
/// - HHL algorithm: Prepare input vector |b⟩
/// - Quantum machine learning: Load training data
/// - Quantum simulation: Initialize quantum states
/// - Amplitude estimation: Prepare target states
/// 
/// **Key Features:**
/// - Prepares ANY normalized quantum state
/// - Deterministic (no measurement/re-preparation)
/// - Optimal gate count using Gray code
/// - Numerically stable implementation
module MottonenStatePreparation =
    
    // ========================================================================
    // TYPES & HELPERS
    // ========================================================================
    
    /// Normalized quantum state vector
    type StateVector = {
        /// Complex amplitudes (must satisfy Σᵢ|αᵢ|² = 1)
        Amplitudes: Complex[]
        
        /// Number of qubits (2^n = Amplitudes.Length)
        NumQubits: int
    }
    
    /// Gray code: binary encoding where adjacent values differ by 1 bit
    /// Used for efficient multi-controlled gate decomposition
    let private grayCode (n: int) : int =
        n ^^^ (n >>> 1)
    
    /// Find bit position where two Gray codes differ
    let private grayCodeDiffBit (g1: int) (g2: int) : int =
        let diff = g1 ^^^ g2
        // Find position of set bit (only one bit differs in Gray code)
        let rec findBit pos =
            if (diff >>> pos) &&& 1 = 1 then pos
            elif pos >= 30 then 0  // Safety check
            else findBit (pos + 1)
        findBit 0

    /// Parity of the number of set bits
    let private bitParity (x: int) : int =
        let rec go acc v = if v = 0 then acc else go (acc ^^^ (v &&& 1)) (v >>> 1)
        go 0 x

    /// Apply a uniformly-controlled (multiplexed) rotation on `target`:
    /// for control pattern c (over `controls`, bit t of c ↔ controls[t])
    /// the target is rotated by angles[c].
    ///
    /// Uses the Gray-code construction of Möttönen et al. (2004):
    /// 2^m single-qubit rotations interleaved with 2^m CNOTs, where the
    /// rotation angles are the Walsh-Hadamard-like transform of the input
    /// angles, θ̂ⱼ = 2⁻ᵐ Σ_c (-1)^(c·g(j)) θ_c with g(j) the Gray code of j.
    let private applyMultiplexedRotation
        (makeRotation: int * float -> Gate)
        (angles: float[])
        (target: int)
        (controls: int[])
        (circuit: Circuit) : Circuit =

        let m = controls.Length
        if angles.Length <> (1 <<< m) then
            failwith $"Multiplexed rotation needs {1 <<< m} angles for {m} controls, got {angles.Length}"

        if m = 0 then
            if abs angles[0] > 1e-10 then
                circuit |> addGate (makeRotation (target, angles[0]))
            else
                circuit
        elif angles |> Array.forall (fun a -> abs a <= 1e-10) then
            // All angles negligible - identity, skip entirely
            circuit
        else
            let count = 1 <<< m
            let transformed =
                Array.init count (fun j ->
                    let gj = grayCode j
                    let signedSum =
                        angles
                        |> Array.mapi (fun c angle ->
                            if bitParity (c &&& gj) = 0 then angle else -angle)
                        |> Array.sum
                    signedSum / float count)

            (circuit, [0 .. count - 1])
            ||> List.fold (fun circ j ->
                let circ =
                    if abs transformed[j] > 1e-10 then
                        circ |> addGate (makeRotation (target, transformed[j]))
                    else
                        circ
                // CNOT control sits at the bit where consecutive Gray codes
                // differ; the final CNOT (wrapping to g(0)=0) is at the MSB,
                // returning the accumulated XOR mask to identity.
                let ctrlBit =
                    if j = count - 1 then m - 1
                    else grayCodeDiffBit (grayCode j) (grayCode (j + 1))
                circ |> addGate (CNOT(controls[ctrlBit], target)))
    
    /// Convert state amplitudes to normalized form
    let normalizeState (amplitudes: Complex[]) : StateVector =
        let norm = 
            amplitudes 
            |> Array.sumBy (fun a -> a.Magnitude * a.Magnitude) 
            |> sqrt
        
        if norm < 1e-10 then
            failwith "Cannot normalize zero vector"
        
        let normalized = 
            amplitudes 
            |> Array.map (fun a -> a / Complex(norm, 0.0))
        
        let numQubits =
            let n = amplitudes.Length
            let rec log2 k = if k <= 1 then 0 else 1 + log2 (k / 2)
            log2 n
        
        if (1 <<< numQubits) <> amplitudes.Length then
            failwith $"State dimension must be power of 2, got {amplitudes.Length}"
        
        { Amplitudes = normalized; NumQubits = numQubits }
    
    // ========================================================================
    // AMPLITUDE PREPARATION (Step 1)
    // ========================================================================
    
    /// Calculate rotation angles for amplitude preparation
    /// 
    /// For each qubit k and control pattern c, compute angle θ(k,c) such that:
    /// RY(θ) transforms |0⟩ → cos(θ/2)|0⟩ + sin(θ/2)|1⟩
    /// 
    /// This ensures correct amplitude distribution in computational basis
    let private calculateAmplitudeAngles (state: StateVector) : float[,] =
        let n = state.NumQubits
        let dim = 1 <<< n
        
        // Extract magnitude of each amplitude
        let magnitudes = state.Amplitudes |> Array.map (fun a -> a.Magnitude)
        
        // angles[qubit, control_pattern]
        let angles = Array2D.zeroCreate n (1 <<< (n - 1))
        
        // Process each qubit level (from most significant to least significant)
        for k in 0 .. n - 1 do
            let numControls = n - k - 1
            let numPatterns = 1 <<< numControls
            
            // For each control bit pattern, calculate rotation angle
            for c in 0 .. numPatterns - 1 do
                // Calculate angle for this qubit and control pattern
                // Group amplitudes into pairs based on qubit k value
                let sum0, sum1 = 
                    [0 .. (1 <<< k) - 1]
                    |> List.fold (fun (s0, s1) i ->
                        let baseIdx = (c <<< (k + 1)) ||| i
                        let idx0 = baseIdx
                        let idx1 = baseIdx ||| (1 <<< k)
                        
                        let contrib0 = if idx0 < dim then magnitudes[idx0] * magnitudes[idx0] else 0.0
                        let contrib1 = if idx1 < dim then magnitudes[idx1] * magnitudes[idx1] else 0.0
                        
                        (s0 + contrib0, s1 + contrib1)
                    ) (0.0, 0.0)
                
                // Calculate rotation angle: tan(θ/2) = sqrt(sum1/sum0)
                let angle = 
                    if sum0 + sum1 < 1e-10 then 0.0
                    else 2.0 * atan2 (sqrt sum1) (sqrt sum0)
                
                angles[k, c] <- angle
        
        angles
    
    /// Apply amplitude preparation gates using Gray code traversal
    /// 
    /// **Gray Code Optimization:**
    /// - Adjacent control patterns differ by 1 bit
    /// - Only 1 CNOT needed to switch between patterns (vs. O(n) naive)
    /// - Total CNOTs: O(2ⁿ) vs. O(n·2ⁿ) naive
    let private applyAmplitudeGates
        (angles: float[,])
        (qubits: int[])
        (circuit: Circuit) : Circuit =
        
        let n = qubits.Length
        let numQubits = Array2D.length1 angles
        
        if n <> numQubits then
            failwith $"Qubit count mismatch: expected {numQubits}, got {n}"
        
        // Process each qubit level (reverse order): the most significant qubit
        // gets an unconditional rotation, each lower qubit a rotation
        // multiplexed over all higher qubits.
        [ numQubits - 1 .. -1 .. 0 ]
        |> List.fold (fun currentCirc targetQubitIdx ->
            let numControls = numQubits - targetQubitIdx - 1
            let numPatterns = 1 <<< numControls
            let levelAngles = Array.init numPatterns (fun c -> angles[targetQubitIdx, c])
            let controlQubits = qubits[targetQubitIdx + 1 .. numQubits - 1]

            currentCirc
            |> applyMultiplexedRotation RY levelAngles qubits[targetQubitIdx] controlQubits
        ) circuit
    
    // ========================================================================
    // PHASE PREPARATION (Step 2)
    // ========================================================================
    
    /// Calculate phase angles for each computational basis state
    let private calculatePhaseAngles (state: StateVector) : float[] =
        state.Amplitudes
        |> Array.map (fun amp ->
            if amp.Magnitude < 1e-10 then 0.0
            else amp.Phase  // arg(αᵢ)
        )
    
    /// Apply phase correction gates
    ///
    /// For each basis state |i⟩, apply phase e^(iφᵢ) (up to an unobservable
    /// global phase). The diagonal operator diag(e^(iφᵢ)) is decomposed into
    /// one multiplexed RZ per qubit: for target qubit k controlled on qubits
    /// k+1..n-1, the angle for control pattern c is the phase difference
    /// between the qubit-k=1 and qubit-k=0 halves, averaged over lower bits.
    let private applyPhaseGates
        (phases: float[])
        (qubits: int[])
        (circuit: Circuit) : Circuit =

        let n = qubits.Length
        let dim = phases.Length

        if (1 <<< n) <> dim then
            failwith "Dimension mismatch"

        [0 .. n - 1]
        |> List.fold (fun circ k ->
            let numControls = n - k - 1
            let numPatterns = 1 <<< numControls
            let lowCount = 1 <<< k

            let levelAngles =
                Array.init numPatterns (fun c ->
                    let totalDiff =
                        [0 .. lowCount - 1]
                        |> List.sumBy (fun low ->
                            let idx0 = (c <<< (k + 1)) ||| low
                            let idx1 = idx0 ||| (1 <<< k)
                            phases[idx1] - phases[idx0])
                    totalDiff / float lowCount)

            let controlQubits = qubits[k + 1 .. n - 1]

            circ
            |> applyMultiplexedRotation RZ levelAngles qubits[k] controlQubits
        ) circuit
    
    // ========================================================================
    // HIGH-LEVEL API
    // ========================================================================
    
    /// Prepare arbitrary quantum state |ψ⟩ from |0...0⟩ using Möttönen's method
    /// 
    /// **Input:** Normalized state vector with 2ⁿ complex amplitudes
    /// **Output:** Circuit that prepares |ψ⟩ from |0...0⟩
    /// 
    /// **Circuit Structure:**
    /// 1. Amplitude preparation: controlled-RY gates (O(2ⁿ) gates)
    /// 2. Phase preparation: controlled-RZ gates (O(2ⁿ) gates)
    /// 3. Total gates: O(2ⁿ) using Gray code optimization
    let prepareState
        (state: StateVector)
        (qubits: int[])
        (circuit: Circuit) : Circuit =
        
        if qubits.Length <> state.NumQubits then
            failwith $"Qubit count mismatch: expected {state.NumQubits}, got {qubits.Length}"
        
        circuit
        |> applyAmplitudeGates (calculateAmplitudeAngles state) qubits
        |> applyPhaseGates (calculatePhaseAngles state) qubits
    
    /// Prepare state from complex amplitude array (convenience function)
    let prepareStateFromAmplitudes
        (amplitudes: Complex[])
        (qubits: int[])
        (circuit: Circuit) : Circuit =
        
        amplitudes
        |> normalizeState
        |> fun state -> prepareState state qubits circuit
    
    /// Prepare state from real amplitude array (convenience function)
    let prepareStateFromRealAmplitudes
        (amplitudes: float[])
        (qubits: int[])
        (circuit: Circuit) : Circuit =
        
        amplitudes 
        |> Array.map (fun a -> Complex(a, 0.0))
        |> fun amps -> prepareStateFromAmplitudes amps qubits circuit
