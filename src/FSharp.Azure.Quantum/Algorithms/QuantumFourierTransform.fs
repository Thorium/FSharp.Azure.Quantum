namespace FSharp.Azure.Quantum.Algorithms

open System
open FSharp.Azure.Quantum.Core
open System.Numerics

/// Quantum Fourier Transform (QFT) Module
/// 
/// Implements the quantum analog of the discrete Fourier transform.
/// QFT is a fundamental building block for many quantum algorithms including:
/// - Shor's factoring algorithm
/// - Quantum phase estimation
/// - Period finding algorithms
/// 
/// The QFT transforms computational basis states into frequency basis:
/// |j⟩ → (1/√N) Σₖ e^(2πijk/N) |k⟩
/// 
/// Time Complexity: O(n²) quantum gates vs O(n·2^n) for classical FFT
module QuantumFourierTransform =
    
    open FSharp.Azure.Quantum.LocalSimulator
    
    // ========================================================================
    // TYPES - QFT configuration and results
    // ========================================================================
    
    /// Configuration for Quantum Fourier Transform
    type QFTConfig = {
        /// Number of qubits to transform
        NumQubits: int
        
        /// Whether to apply bit-reversal SWAP at the end
        /// (Standard QFT includes SWAPs, but some algorithms omit them)
        ApplySwaps: bool
        
        /// Whether to compute inverse QFT (QFT†)
        Inverse: bool
    }
    
    /// Result of QFT execution
    type QFTResult = {
        /// Transformed quantum state
        FinalState: StateVector.StateVector
        
        /// Number of gates applied
        GateCount: int
        
        /// Configuration used
        Config: QFTConfig
    }
    
    // ========================================================================
    // CONTROLLED ROTATION GATES - Building blocks for QFT
    // ========================================================================
    
    /// Apply controlled Rz gate (controlled phase rotation)
    /// 
    /// CRz(θ) applies Rz(θ) to target qubit when control qubit is |1⟩
    /// 
    /// Matrix when control=1:
    /// [[e^(-iθ/2),  0        ],
    ///  [0,          e^(iθ/2) ]]
    /// 
    /// This is the key gate for QFT - implements controlled phase shifts
    let private applyControlledRz 
        (controlIndex: int) 
        (targetIndex: int) 
        (theta: float) 
        (state: StateVector.StateVector) : StateVector.StateVector =
        
        let numQubits = StateVector.numQubits state
        if controlIndex < 0 || controlIndex >= numQubits then
            failwith $"Control qubit index {controlIndex} out of range for {numQubits}-qubit state"
        if targetIndex < 0 || targetIndex >= numQubits then
            failwith $"Target qubit index {targetIndex} out of range for {numQubits}-qubit state"
        if controlIndex = targetIndex then
            failwith "Control and target qubits must be different"
        
        let dimension = StateVector.dimension state
        let controlMask = 1 <<< controlIndex
        let targetMask = 1 <<< targetIndex
        
        // Precompute phase factors
        let halfTheta = theta / 2.0
        let negPhase = Complex(cos halfTheta, -sin halfTheta)  // e^(-iθ/2)
        let posPhase = Complex(cos halfTheta, sin halfTheta)   // e^(iθ/2)
        
        // Create new amplitude array
        let newAmplitudes = Array.zeroCreate dimension
        
        // Process each basis state
        for i in 0 .. dimension - 1 do
            let controlIs1 = (i &&& controlMask) <> 0
            
            if controlIs1 then
                // Control is 1: apply Rz to target
                let targetIs1 = (i &&& targetMask) <> 0
                
                if targetIs1 then
                    // Target is |1⟩: multiply by e^(iθ/2)
                    newAmplitudes[i] <- StateVector.getAmplitude i state * posPhase
                else
                    // Target is |0⟩: multiply by e^(-iθ/2)
                    newAmplitudes[i] <- StateVector.getAmplitude i state * negPhase
            else
                // Control is 0: no operation
                newAmplitudes[i] <- StateVector.getAmplitude i state
        
        StateVector.create newAmplitudes
    
    /// Apply controlled phase gate (simplified controlled Rz)
    /// 
    /// CPhase(θ) = controlled-Rz(θ) but with simplified matrix:
    /// Adds phase e^(iθ) when both qubits are |1⟩
    /// 
    /// This is more commonly used in QFT implementations:
    /// CPhase(2π/2^k) between qubits at distance k
    let private applyControlledPhase
        (controlIndex: int)
        (targetIndex: int)
        (theta: float)
        (state: StateVector.StateVector) : StateVector.StateVector =
        
        let numQubits = StateVector.numQubits state
        if controlIndex < 0 || controlIndex >= numQubits then
            failwith $"Control qubit index {controlIndex} out of range for {numQubits}-qubit state"
        if targetIndex < 0 || targetIndex >= numQubits then
            failwith $"Target qubit index {targetIndex} out of range for {numQubits}-qubit state"
        if controlIndex = targetIndex then
            failwith "Control and target qubits must be different"
        
        let dimension = StateVector.dimension state
        let controlMask = 1 <<< controlIndex
        let targetMask = 1 <<< targetIndex
        
        // Phase to add when both qubits are |1⟩
        let phase = Complex(cos theta, sin theta)  // e^(iθ)
        
        // Create new amplitude array
        let newAmplitudes = Array.zeroCreate dimension
        
        // Process each basis state
        for i in 0 .. dimension - 1 do
            let controlIs1 = (i &&& controlMask) <> 0
            let targetIs1 = (i &&& targetMask) <> 0
            
            if controlIs1 && targetIs1 then
                // Both qubits are |1⟩: add phase
                newAmplitudes[i] <- StateVector.getAmplitude i state * phase
            else
                // Otherwise: no change
                newAmplitudes[i] <- StateVector.getAmplitude i state
        
        StateVector.create newAmplitudes
    
    // ========================================================================
    // QFT IMPLEMENTATION - Core algorithm
    // ========================================================================
    
    /// Apply QFT to a single qubit with controlled phases from other qubits
    /// 
    /// This is one iteration of the QFT algorithm:
    /// 1. Apply Hadamard to target qubit
    /// 2. Apply controlled phase rotations from all qubits to the right
    /// 
    /// The phase rotation for qubit j controlled by qubit k (where k > j) is:
    /// CPhase(2π / 2^(k-j+1))
    let private applyQFTStep
        (targetQubit: int)
        (numQubits: int)
        (inverse: bool)
        (state: StateVector.StateVector) : StateVector.StateVector * int =
        
        // Step 1: Apply Hadamard to target qubit
        let stateAfterH = Gates.applyH targetQubit state
        
        // Step 2: Apply controlled phase rotations using fold
        let (finalState, totalGates) =
            [targetQubit + 1 .. numQubits - 1]
            |> List.fold (fun (currState, gateCount) k ->
                // Phase angle: 2π / 2^(k - targetQubit + 1)
                let power = k - targetQubit + 1
                let angle = 2.0 * Math.PI / (float (1 <<< power))
                
                // For inverse QFT, negate the angle
                let finalAngle = if inverse then -angle else angle
                
                // Apply controlled phase: control=k, target=targetQubit
                let newState = applyControlledPhase k targetQubit finalAngle currState
                (newState, gateCount + 1)
            ) (stateAfterH, 1)
        
        (finalState, totalGates)
    
    /// Apply bit-reversal SWAP gates
    /// 
    /// QFT outputs qubits in reverse order, so we need to SWAP them back
    /// Example for 3 qubits: qubit 0 ↔ qubit 2, qubit 1 stays
    let private applyBitReversalSwaps
        (numQubits: int)
        (state: StateVector.StateVector) : StateVector.StateVector * int =
        
        // Generate SWAP pairs functionally
        let swapPairs = 
            [0 .. (numQubits / 2 - 1)]
            |> List.map (fun i -> (i, numQubits - 1 - i))
        
        // Apply swaps using fold
        let (finalState, gateCount) =
            swapPairs
            |> List.fold (fun (currState, count) (i, j) ->
                let newState = Gates.applySWAP i j currState
                (newState, count + 1)
            ) (state, 0)
        
        (finalState, gateCount)
    
    /// Execute Quantum Fourier Transform
    /// 
    /// Applies QFT to all qubits in the state vector
    /// 
    /// Algorithm:
    /// 1. For each qubit j from 0 to n-1:
    ///    a. Apply Hadamard to qubit j
    ///    b. For each qubit k > j: apply controlled phase rotation CPhase(2π/2^(k-j+1))
    /// 2. Apply bit-reversal SWAPs (if config.ApplySwaps = true)
    /// 
    /// Time complexity: O(n²) gates
    let execute (config: QFTConfig) (state: StateVector.StateVector) : QuantumResult<QFTResult> =
        try
            // Validate configuration
            if config.NumQubits < 1 || config.NumQubits > 20 then
                Error (QuantumError.Other $"Number of qubits must be between 1 and 20, got {config.NumQubits}")
            elif StateVector.numQubits state <> config.NumQubits then
                Error (QuantumError.Other $"State has {StateVector.numQubits state} qubits, expected {config.NumQubits}")
            else
                // Apply QFT to each qubit using functional fold
                let (stateAfterQFT, gatesAfterQFT) =
                    [0 .. (config.NumQubits - 1)]
                    |> List.fold (fun (currState, gateCount) j ->
                        let (newState, gates) = applyQFTStep j config.NumQubits config.Inverse currState
                        (newState, gateCount + gates)
                    ) (state, 0)
                
                // Apply bit-reversal SWAPs if requested
                let (finalState, totalGates) =
                    if config.ApplySwaps then
                        let (newState, swapGates) = applyBitReversalSwaps config.NumQubits stateAfterQFT
                        (newState, gatesAfterQFT + swapGates)
                    else
                        (stateAfterQFT, gatesAfterQFT)
                
                Ok {
                    FinalState = finalState
                    GateCount = totalGates
                    Config = config
                }
        with
        | ex -> Error (QuantumError.Other $"QFT execution failed: {ex.Message}")
    
    // ========================================================================
    // CONVENIENCE FUNCTIONS - Common use cases
    // ========================================================================
    
    /// Default QFT configuration
    let defaultConfig (numQubits: int) : QFTConfig = {
        NumQubits = numQubits
        ApplySwaps = true   // Standard QFT includes SWAPs
        Inverse = false
    }
    
    /// Execute standard QFT (forward transform with SWAPs)
    let executeStandard (numQubits: int) (state: StateVector.StateVector) : QuantumResult<QFTResult> =
        let config = defaultConfig numQubits
        execute config state
    
    /// Execute inverse QFT (QFT†)
    /// 
    /// Used for decoding QFT results back to computational basis
    let executeInverse (numQubits: int) (state: StateVector.StateVector) : QuantumResult<QFTResult> =
        let config = { defaultConfig numQubits with Inverse = true }
        execute config state
    
    /// Execute QFT without final SWAPs
    /// 
    /// Some algorithms (e.g., quantum phase estimation) don't require bit-reversal
    /// This saves n/2 SWAP gates
    let executeNoSwaps (numQubits: int) (state: StateVector.StateVector) : QuantumResult<QFTResult> =
        let config = { defaultConfig numQubits with ApplySwaps = false }
        execute config state
    
    // ========================================================================
    // VERIFICATION - Check QFT properties
    // ========================================================================
    
    /// Verify that QFT · QFT† = I (identity)
    /// 
    /// QFT is unitary, so applying QFT then inverse QFT should return original state
    let verifyUnitarity (numQubits: int) (state: StateVector.StateVector) : bool =
        // Apply QFT
        let qftResult = executeStandard numQubits state
        
        match qftResult with
        | Error _ -> false
        | Ok result ->
            // Apply inverse QFT
            let inverseResult = executeInverse numQubits result.FinalState
            
            match inverseResult with
            | Error _ -> false
            | Ok invResult ->
                // Compare with original state (should be very close)
                let dimension = StateVector.dimension state
                
                let maxDifference =
                    [0 .. dimension - 1]
                    |> List.map (fun i ->
                        let original = StateVector.getAmplitude i state
                        let recovered = StateVector.getAmplitude i invResult.FinalState
                        
                        let diffReal = abs (original.Real - recovered.Real)
                        let diffImag = abs (original.Imaginary - recovered.Imaginary)
                        
                        max diffReal diffImag
                    )
                    |> List.max
                
                // Allow small numerical tolerance (1e-6)
                maxDifference < 1e-6
    
    /// Calculate expected gate count for QFT
    /// 
    /// For n qubits:
    /// - Hadamard gates: n
    /// - Controlled phase gates: n(n-1)/2
    /// - SWAP gates: floor(n/2)
    /// Total: n + n(n-1)/2 + floor(n/2) = n(n+1)/2 + floor(n/2)
    let expectedGateCount (numQubits: int) (includeSwaps: bool) : int =
        let hadamards = numQubits
        let controlledPhases = numQubits * (numQubits - 1) / 2
        let swaps = if includeSwaps then numQubits / 2 else 0
        
        hadamards + controlledPhases + swaps
    
    // ========================================================================
    // APPLICATIONS - Example use cases
    // ========================================================================
    
    /// Apply QFT to computational basis state |j⟩
    /// 
    /// Result: (1/√N) Σₖ e^(2πijk/N) |k⟩
    /// This creates an equal superposition with phase relationships
    let transformBasisState (numQubits: int) (basisIndex: int) : QuantumResult<QFTResult> =
        if basisIndex < 0 || basisIndex >= (1 <<< numQubits) then
            Error (QuantumError.Other $"Basis index {basisIndex} out of range for {numQubits} qubits")
        else
            // Create computational basis state |j⟩
            let dimension = 1 <<< numQubits
            let amplitudes = Array.create dimension Complex.Zero
            amplitudes[basisIndex] <- Complex.One
            let state = StateVector.create amplitudes
            
            // Apply QFT
            executeStandard numQubits state
    
    /// Encode integer into quantum state and apply QFT
    /// 
    /// This is the first step in many quantum algorithms (e.g., Shor's algorithm)
    let encodeAndTransform (numQubits: int) (value: int) : QuantumResult<QFTResult> =
        transformBasisState numQubits value


