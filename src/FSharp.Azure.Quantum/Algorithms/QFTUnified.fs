namespace FSharp.Azure.Quantum.Algorithms

open System
open FSharp.Azure.Quantum.Core
open FSharp.Azure.Quantum.Core.UnifiedBackendAbstraction

/// Quantum Fourier Transform (QFT) - Unified Backend Edition
/// 
/// This module provides a backend-agnostic implementation of the Quantum Fourier Transform
/// using the unified backend interface (IUnifiedQuantumBackend).
/// 
/// The QFT transforms computational basis states into frequency basis:
/// |j⟩ → (1/√N) Σₖ e^(2πijk/N) |k⟩
/// 
/// Key features:
/// - Works seamlessly with gate-based and topological backends
/// - State-based execution for efficiency
/// - Pure functional design with Result-based error handling
/// - Idiomatic F# with computation expressions
/// - O(n²) gate complexity
/// 
/// Applications:
/// - Shor's factoring algorithm
/// - Quantum phase estimation (QPE)
/// - Period finding
/// - Quantum signal processing
/// 
/// Usage:
///   let backend = LocalBackend.LocalBackend() :> IUnifiedQuantumBackend
///   let! result = QFT.execute 3 backend defaultConfig
module QFTUnified =
    
    // ========================================================================
    // TYPES
    // ========================================================================
    
    /// Configuration for Quantum Fourier Transform
    type QFTConfig = {
        /// Whether to apply bit-reversal SWAPs at the end
        /// (Standard QFT includes SWAPs, but some algorithms omit them)
        ApplySwaps: bool
        
        /// Whether to compute inverse QFT (QFT†)
        Inverse: bool
        
        /// Number of measurement shots (for verification)
        Shots: int
    }
    
    /// Result of QFT execution
    type QFTResult = {
        /// Transformed quantum state
        FinalState: QuantumState
        
        /// Number of gates applied
        GateCount: int
        
        /// Configuration used
        Config: QFTConfig
        
        /// Execution time (milliseconds)
        ExecutionTimeMs: float
    }
    
    // ========================================================================
    // DEFAULT CONFIGURATION
    // ========================================================================
    
    /// Default QFT configuration
    let defaultConfig = {
        ApplySwaps = true
        Inverse = false
        Shots = 1000
    }
    
    // ========================================================================
    // HELPER: Phase Angle Calculation
    // ========================================================================
    
    /// Calculate phase angle for controlled rotation
    /// 
    /// Formula: θ = 2π / 2^k
    /// where k is the distance between qubits
    let private calculatePhaseAngle (k: int) (inverse: bool) : float =
        let angle = 2.0 * Math.PI / float (1 <<< k)
        if inverse then -angle else angle
    
    // ========================================================================
    // QFT PRIMITIVES (State-Based Operations)
    // ========================================================================
    
    /// Apply QFT step to single qubit
    /// 
    /// QFT step consists of:
    /// 1. Hadamard gate on target qubit
    /// 2. Controlled phase rotations from all higher-indexed qubits
    /// 
    /// Phase angles: CPhase(2π/2^k) where k = distance between qubits
    let private applyQFTStep
        (backend: IUnifiedQuantumBackend)
        (targetQubit: int)
        (numQubits: int)
        (inverse: bool)
        (state: QuantumState)
        : Result<QuantumState * int, QuantumError> =
        
        result {
            // Step 1: Apply Hadamard to target qubit
            let! stateAfterH = 
                backend.ApplyOperation 
                    (QuantumOperation.Gate (CircuitBuilder.H targetQubit)) 
                    state
            
            // Step 2: Apply controlled phase rotations
            // For each qubit k > targetQubit, apply CPhase(2π/2^(k-targetQubit+1))
            let controlledPhaseOps =
                [targetQubit + 1 .. numQubits - 1]
                |> List.map (fun k ->
                    let power = k - targetQubit + 1
                    let angle = calculatePhaseAngle power inverse
                    QuantumOperation.Gate (CircuitBuilder.CPhase (k, targetQubit, angle))
                )
            
            // Apply all controlled phases
            let! finalState = 
                UnifiedBackend.applySequence backend controlledPhaseOps stateAfterH
            
            // Return state and gate count (1 H + n controlled phases)
            return (finalState, 1 + List.length controlledPhaseOps)
        }
    
    /// Apply bit-reversal SWAP gates
    /// 
    /// QFT outputs qubits in reverse order:
    /// qubit 0 ↔ qubit (n-1)
    /// qubit 1 ↔ qubit (n-2)
    /// etc.
    let private applyBitReversalSwaps
        (backend: IUnifiedQuantumBackend)
        (numQubits: int)
        (state: QuantumState)
        : Result<QuantumState * int, QuantumError> =
        
        // Create SWAP operations for bit reversal
        let swapOps =
            [0 .. numQubits / 2 - 1]
            |> List.map (fun i ->
                let j = numQubits - 1 - i
                QuantumOperation.Gate (CircuitBuilder.SWAP (i, j))
            )
        
        result {
            let! finalState = UnifiedBackend.applySequence backend swapOps state
            return (finalState, List.length swapOps)
        }
    
    // ========================================================================
    // MAIN QFT ALGORITHM
    // ========================================================================
    
    /// Execute Quantum Fourier Transform
    /// 
    /// Algorithm:
    /// 1. For each qubit j from 0 to n-1:
    ///    a. Apply Hadamard to qubit j
    ///    b. Apply controlled phase rotations from qubits k > j
    /// 2. Apply bit-reversal SWAPs (optional)
    /// 
    /// Time Complexity: O(n²) gates
    /// 
    /// Parameters:
    ///   numQubits - Number of qubits to transform
    ///   backend - Quantum backend (gate-based or topological)
    ///   config - QFT configuration
    /// 
    /// Returns:
    ///   Result with transformed state or error
    let execute
        (numQubits: int)
        (backend: IUnifiedQuantumBackend)
        (config: QFTConfig)
        : Result<QFTResult, QuantumError> =
        
        let startTime = DateTime.Now
        
        result {
            // Step 1: Initialize state to |0⟩^⊗n
            let! initialState = backend.InitializeState numQubits
            
            // Step 2: Apply QFT steps to each qubit
            // Use fold to thread state and accumulate gate count
            let! (stateAfterQFT, qftGates) =
                [0 .. numQubits - 1]
                |> List.fold (fun resultAcc targetQubit ->
                    result {
                        let! (currentState, accGates) = resultAcc
                        let! (newState, stepGates) = 
                            applyQFTStep backend targetQubit numQubits config.Inverse currentState
                        return (newState, accGates + stepGates)
                    }
                ) (Ok (initialState, 0))
            
            // Step 3: Apply bit-reversal SWAPs (if enabled)
            let! (finalState, swapGates) =
                if config.ApplySwaps then
                    applyBitReversalSwaps backend numQubits stateAfterQFT
                else
                    Ok (stateAfterQFT, 0)
            
            // Calculate execution time
            let endTime = DateTime.Now
            let elapsedMs = (endTime - startTime).TotalMilliseconds
            
            // Return result
            return {
                FinalState = finalState
                GateCount = qftGates + swapGates
                Config = config
                ExecutionTimeMs = elapsedMs
            }
        }
    
    /// Execute QFT on existing quantum state
    /// 
    /// Applies QFT transformation to given state instead of starting from |0⟩
    /// Useful for multi-stage algorithms
    let executeOnState
        (state: QuantumState)
        (backend: IUnifiedQuantumBackend)
        (config: QFTConfig)
        : Result<QFTResult, QuantumError> =
        
        let startTime = DateTime.Now
        let numQubits = QuantumState.numQubits state
        
        result {
            // Apply QFT steps to each qubit
            let! (stateAfterQFT, qftGates) =
                [0 .. numQubits - 1]
                |> List.fold (fun resultAcc targetQubit ->
                    result {
                        let! (currentState, accGates) = resultAcc
                        let! (newState, stepGates) = 
                            applyQFTStep backend targetQubit numQubits config.Inverse currentState
                        return (newState, accGates + stepGates)
                    }
                ) (Ok (state, 0))
            
            // Apply bit-reversal SWAPs (if enabled)
            let! (finalState, swapGates) =
                if config.ApplySwaps then
                    applyBitReversalSwaps backend numQubits stateAfterQFT
                else
                    Ok (stateAfterQFT, 0)
            
            // Calculate execution time
            let endTime = DateTime.Now
            let elapsedMs = (endTime - startTime).TotalMilliseconds
            
            return {
                FinalState = finalState
                GateCount = qftGates + swapGates
                Config = config
                ExecutionTimeMs = elapsedMs
            }
        }
    
    // ========================================================================
    // CONVENIENCE FUNCTIONS
    // ========================================================================
    
    /// Execute inverse QFT (QFT†)
    /// 
    /// Inverse QFT is used in many algorithms to convert from frequency basis
    /// back to computational basis
    let executeInverse
        (numQubits: int)
        (backend: IUnifiedQuantumBackend)
        (shots: int)
        : Result<QFTResult, QuantumError> =
        
        let inverseConfig = { 
            defaultConfig with 
                Inverse = true
                Shots = shots
        }
        execute numQubits backend inverseConfig
    
    /// Execute QFT without bit-reversal SWAPs
    /// 
    /// Some algorithms (like QPE) don't require bit-reversal, saving gates
    let executeNoSwaps
        (numQubits: int)
        (backend: IUnifiedQuantumBackend)
        (shots: int)
        : Result<QFTResult, QuantumError> =
        
        let noSwapConfig = { 
            defaultConfig with 
                ApplySwaps = false
                Shots = shots
        }
        execute numQubits backend noSwapConfig
    
    // ========================================================================
    // GATE COUNT ESTIMATION
    // ========================================================================
    
    /// Calculate total gate count for QFT
    /// 
    /// Formula:
    /// - QFT steps: n Hadamards + Σᵢ (n-i-1) controlled phases = n + n(n-1)/2
    /// - Bit reversal: ⌊n/2⌋ SWAPs
    /// 
    /// Total with swaps: n + n(n-1)/2 + ⌊n/2⌋ ≈ n²/2 + 3n/2
    let estimateGateCount (numQubits: int) (applySwaps: bool) : int =
        let qftGates = numQubits + (numQubits * (numQubits - 1) / 2)
        let swapGates = if applySwaps then numQubits / 2 else 0
        qftGates + swapGates
    
    // ========================================================================
    // VERIFICATION HELPERS
    // ========================================================================
    
    /// Verify QFT correctness by checking round-trip: QFT → QFT† ≈ I
    /// 
    /// Applies QFT followed by inverse QFT and checks if result ≈ original state
    let verifyRoundTrip
        (numQubits: int)
        (backend: IUnifiedQuantumBackend)
        : Result<bool, QuantumError> =
        
        result {
            // Start with |0⟩^⊗n
            let! initialState = backend.InitializeState numQubits
            
            // Apply QFT
            let! qftResult = execute numQubits backend defaultConfig
            
            // Apply inverse QFT
            let! inverseResult = 
                executeOnState qftResult.FinalState backend { defaultConfig with Inverse = true }
            
            // Check if we're back to |0⟩^⊗n
            // (Measure multiple times and verify all outcomes are 0)
            let measurements = UnifiedBackend.measureState inverseResult.FinalState 100
            let allZeros = 
                measurements 
                |> Array.forall (fun bits -> Array.forall ((=) 0) bits)
            
            return allZeros
        }
    
    // ========================================================================
    // PRETTY PRINTING
    // ========================================================================
    
    /// Format QFT result as human-readable string
    let formatResult (result: QFTResult) : string =
        let qftType = if result.Config.Inverse then "Inverse QFT" else "QFT"
        let swapStr = if result.Config.ApplySwaps then "with SWAPs" else "without SWAPs"
        
        sprintf "%s (%s)\nGates: %d | Time: %.2f ms"
            qftType
            swapStr
            result.GateCount
            result.ExecutionTimeMs
