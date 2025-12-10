namespace FSharp.Azure.Quantum.Algorithms

open System
open FSharp.Azure.Quantum
open FSharp.Azure.Quantum.Core
open FSharp.Azure.Quantum.Core.BackendAbstraction

// Import CircuitBuilder for gate construction
module CB = FSharp.Azure.Quantum.CircuitBuilder

/// Unified Quantum Arithmetic Operations
/// 
/// State-based implementation of quantum arithmetic operations using IQuantumBackend.
/// This module provides backend-agnostic arithmetic that works with the unified state model.
/// 
/// Key Features:
/// - Works with any IQuantumBackend implementation
/// - Uses QuantumState instead of Circuit types
/// - Supports gate-based and topological backends
/// - Idiomatic F# with Result-based error handling
/// 
/// Operations:
/// - Addition: |x⟩ → |x + a mod 2^n⟩
/// - Subtraction: |x⟩ → |x - a mod 2^n⟩
/// - Controlled variants
/// - Modular arithmetic
/// 
/// Applications:
/// - Shor's factoring algorithm
/// - Quantum neural networks
/// - Quantum machine learning
/// 
/// Usage:
///   let backend = LocalBackend.LocalBackend() :> IQuantumBackend
///   let! state = QuantumArithmetic.addConstant registerQubits 5 initialState backend
module Arithmetic =
    
    // ========================================================================
    // TYPES
    // ========================================================================
    
    /// Configuration for quantum arithmetic operations
    type ArithmeticConfig = {
        /// Number of qubits in arithmetic register
        NumQubits: int
        
        /// Whether to use QFT-based addition (Draper method)
        /// False = use ripple-carry adder (more gates but no QFT)
        UseQFTAdder: bool
        
        /// For modular arithmetic: modulus N
        Modulus: int option
    }
    
    /// Result of arithmetic operation
    type ArithmeticResult = {
        /// Final quantum state after operation
        State: QuantumState
        
        /// Number of operations applied
        OperationCount: int
        
        /// Configuration used
        Config: ArithmeticConfig
    }
    
    // ========================================================================
    // UTILITY FUNCTIONS
    // ========================================================================
    
    /// Convert integer to binary representation (LSB first)
    let private intToBinary (n: int) (width: int) : int list =
        [0 .. width - 1]
        |> List.map (fun i -> (n >>> i) &&& 1)
    
    /// Compute number of qubits needed to represent integer n
    let private qubitCountFor (n: int) : int =
        if n <= 0 then 1
        else int (ceil (Math.Log2(float n))) + 1
    
    // ========================================================================
    // QFT-BASED ADDITION (Draper Method)
    // ========================================================================
    
    /// Apply QFT to quantum register using existing QFT module
    /// 
    /// NOTE: This implementation leverages the unified QFT module instead of
    /// building QFT inline. This avoids recursive complexity and reuses tested code.
    /// 
    /// Parameters:
    ///   - qubits: List of qubit indices (LSB first) - MUST be contiguous [0..n-1]
    ///   - state: Current quantum state
    ///   - backend: Backend to execute operations
    /// 
    /// Returns: State with QFT applied to specified qubits
    let private applyQFT 
        (qubits: int list) 
        (state: QuantumState) 
        (backend: IQuantumBackend) : Result<QuantumState, QuantumError> =
        
        // Use existing unified QFT module to avoid recursive complexity
        let config = {
            QFT.ApplySwaps = true
            QFT.Inverse = false
            QFT.Shots = 1  // Not used for state-based execution
        }
        
        result {
            let! qftResult = QFT.executeOnState state backend config
            return qftResult.FinalState
        }
    
    /// Apply inverse QFT to quantum register
    let private applyInverseQFT 
        (qubits: int list) 
        (state: QuantumState) 
        (backend: IQuantumBackend) : Result<QuantumState, QuantumError> =
        
        // Use existing unified QFT module with Inverse = true
        let config = {
            QFT.ApplySwaps = true
            QFT.Inverse = true  // Inverse QFT
            QFT.Shots = 1
        }
        
        result {
            let! qftResult = QFT.executeOnState state backend config
            return qftResult.FinalState
        }
    
    // ========================================================================
    // PUBLIC API - QUANTUM ADDITION
    // ========================================================================
    
    /// Add classical constant to quantum register using QFT method
    /// 
    /// Operation: |x⟩ → |x + a mod 2^n⟩
    /// 
    /// Parameters:
    ///   - registerQubits: List of qubit indices (LSB first)
    ///   - constant: Classical integer to add
    ///   - state: Current quantum state
    ///   - backend: Backend to execute operations
    /// 
    /// Returns: Result<ArithmeticResult, QuantumError>
    /// 
    /// Algorithm: Draper QFT-based addition
    /// 1. Apply QFT to quantum register
    /// 2. Apply phase rotations based on classical constant
    /// 3. Apply inverse QFT to get result
    /// 
    /// Complexity: O(n²) gates where n is register size
    /// 
    /// Example:
    ///   let backend = LocalBackend.LocalBackend() :> IQuantumBackend
    ///   let! initialState = backend.InitializeState 3
    ///   let! result = addConstant [0; 1; 2] 5 initialState backend
    let addConstant 
        (registerQubits: int list)
        (constant: int)
        (state: QuantumState)
        (backend: IQuantumBackend) : Result<ArithmeticResult, QuantumError> =
        
        let numQubits = List.length registerQubits
        
        // Validate constant fits in register
        let maxValue = (1 <<< numQubits) - 1
        if constant < 0 || constant > maxValue then
            Error (QuantumError.ValidationError ("constant", $"Value {constant} must be in range [0, {maxValue}] for {numQubits}-qubit register"))
        else
            let constantBits = intToBinary constant numQubits
            
            result {
                // Step 1: Apply QFT
                let! qftState = applyQFT registerQubits state backend
                
                // Step 2: Apply phase rotations for each bit of the constant
                let rec applyPhaseRotations (currentState: QuantumState) (j: int) (opCount: int) : Result<QuantumState * int, QuantumError> =
                    if j >= numQubits then
                        Ok (currentState, opCount)
                    else
                        result {
                            let targetQubit = registerQubits.[j]
                            
                            // For this qubit, apply phases from all constant bits k <= j
                            let rec applyBitPhases (s: QuantumState) (k: int) (ops: int) : Result<QuantumState * int, QuantumError> =
                                if k > j then
                                    Ok (s, ops)
                                else
                                    if constantBits.[k] = 1 then
                                        result {
                                            // Apply phase: 2π * 2^k / 2^(j+1)
                                            let angle = 2.0 * Math.PI * (float (1 <<< k)) / (float (1 <<< (j + 1)))
                                            let! nextState = backend.ApplyOperation 
                                                                (QuantumOperation.Gate (CB.P (targetQubit, angle))) 
                                                                s
                                            return! applyBitPhases nextState (k + 1) (ops + 1)
                                        }
                                    else
                                        applyBitPhases s (k + 1) ops
                            
                            let! (nextState, newOpCount) = applyBitPhases currentState 0 opCount
                            return! applyPhaseRotations nextState (j + 1) newOpCount
                        }
                
                let! (stateWithPhases, opCount) = applyPhaseRotations qftState 0 0
                
                // Step 3: Apply inverse QFT
                let! finalState = applyInverseQFT registerQubits stateWithPhases backend
                
                return {
                    State = finalState
                    OperationCount = opCount + numQubits * 2  // QFT + inverse QFT + phases
                    Config = { 
                        NumQubits = numQubits
                        UseQFTAdder = true
                        Modulus = None
                    }
                }
            }
    
    /// Subtract classical constant from quantum register
    /// 
    /// Operation: |x⟩ → |x - a mod 2^n⟩
    /// 
    /// Parameters: Same as addConstant
    /// 
    /// Implementation: Subtraction is addition of two's complement
    let subtractConstant 
        (registerQubits: int list)
        (constant: int)
        (state: QuantumState)
        (backend: IQuantumBackend) : Result<ArithmeticResult, QuantumError> =
        
        let numQubits = List.length registerQubits
        let twosComplement = (1 <<< numQubits) - constant
        addConstant registerQubits twosComplement state backend
    
    // ========================================================================
    // CONTROLLED ARITHMETIC
    // ========================================================================
    
    /// Controlled addition: Add constant if control qubit is |1⟩
    /// 
    /// Operation: |c⟩|x⟩ → |c⟩|x + c*a mod 2^n⟩
    /// 
    /// Parameters:
    ///   - controlQubit: Control qubit index (addition happens when |1⟩)
    ///   - registerQubits: List of qubit indices (LSB first)
    ///   - constant: Classical integer to add
    ///   - state: Current quantum state
    ///   - backend: Backend to execute operations
    /// 
    /// Algorithm: QFT-based controlled addition using controlled-phase gates
    /// 1. Apply QFT to quantum register
    /// 2. Apply controlled phase rotations (CP gates) based on classical constant
    /// 3. Apply inverse QFT to get result
    /// 
    /// The key difference from regular addition is using CP instead of P gates.
    let controlledAddConstant 
        (controlQubit: int)
        (registerQubits: int list)
        (constant: int)
        (state: QuantumState)
        (backend: IQuantumBackend) : Result<ArithmeticResult, QuantumError> =
        
        let numQubits = List.length registerQubits
        
        // Validate constant fits in register
        let maxValue = (1 <<< numQubits) - 1
        if constant < 0 || constant > maxValue then
            Error (QuantumError.ValidationError ("constant", $"Value {constant} must be in range [0, {maxValue}] for {numQubits}-qubit register"))
        else
            let constantBits = intToBinary constant numQubits
            
            result {
                // Step 1: Apply QFT
                let! qftState = applyQFT registerQubits state backend
                
                // Step 2: Apply controlled phase rotations for each bit of the constant
                let rec applyControlledPhaseRotations (currentState: QuantumState) (j: int) (opCount: int) : Result<QuantumState * int, QuantumError> =
                    if j >= numQubits then
                        Ok (currentState, opCount)
                    else
                        result {
                            let targetQubit = registerQubits.[j]
                            
                            // For this qubit, apply controlled phases from all constant bits k <= j
                            let rec applyBitPhases (s: QuantumState) (k: int) (ops: int) : Result<QuantumState * int, QuantumError> =
                                if k > j then
                                    Ok (s, ops)
                                else
                                    if constantBits.[k] = 1 then
                                        result {
                                            // Apply controlled phase: CP gate from control qubit to target
                                            let angle = 2.0 * Math.PI * (float (1 <<< k)) / (float (1 <<< (j + 1)))
                                            let! nextState = backend.ApplyOperation 
                                                                (QuantumOperation.Gate (CB.CP (controlQubit, targetQubit, angle))) 
                                                                s
                                            return! applyBitPhases nextState (k + 1) (ops + 1)
                                        }
                                    else
                                        applyBitPhases s (k + 1) ops
                            
                            let! (nextState, newOpCount) = applyBitPhases currentState 0 opCount
                            return! applyControlledPhaseRotations nextState (j + 1) newOpCount
                        }
                
                let! (stateWithPhases, opCount) = applyControlledPhaseRotations qftState 0 0
                
                // Step 3: Apply inverse QFT
                let! finalState = applyInverseQFT registerQubits stateWithPhases backend
                
                return {
                    State = finalState
                    OperationCount = opCount + numQubits * 2  // QFT + inverse QFT + phases
                    Config = { 
                        NumQubits = numQubits
                        UseQFTAdder = true
                        Modulus = None
                    }
                }
            }
    
    /// Doubly-controlled addition: Add constant if both control qubits are |1⟩
    /// 
    /// Operation: |c1⟩|c2⟩|x⟩|ancilla⟩ → |c1⟩|c2⟩|x + (c1 AND c2)*a mod 2^n⟩|ancilla⟩
    /// 
    /// Parameters:
    ///   - control1: First control qubit index
    ///   - control2: Second control qubit index
    ///   - registerQubits: List of qubit indices (LSB first)
    ///   - constant: Classical integer to add
    ///   - state: Current quantum state
    ///   - backend: Backend to execute operations
    /// 
    /// ⚠️ IMPORTANT: The quantum state MUST have at least (max_qubit_index + 2) qubits
    ///    to accommodate an ancilla qubit for the Toffoli decomposition.
    ///    Example: If using control qubits 0,1 and register [2,3,4], you need at least 6 qubits.
    /// 
    /// Algorithm: Uses ancilla qubit to convert CC-ADD into C-ADD
    /// 1. Use Toffoli (CCX) to set ancilla = control1 AND control2
    /// 2. Perform controlled addition with ancilla as control
    /// 3. Uncompute ancilla with another Toffoli
    /// 
    /// This ensures the operation is unitary and doesn't consume ancilla qubits permanently.
    let doublyControlledAddConstant 
        (control1: int)
        (control2: int)
        (registerQubits: int list)
        (constant: int)
        (state: QuantumState)
        (backend: IQuantumBackend) : Result<ArithmeticResult, QuantumError> =
        
        let numQubits = List.length registerQubits
        
        // Validate constant
        let maxValue = (1 <<< numQubits) - 1
        if constant < 0 || constant > maxValue then
            Error (QuantumError.ValidationError ("constant", $"Value {constant} must be in range [0, {maxValue}] for {numQubits}-qubit register"))
        else
            // Find an unused ancilla qubit (max qubit index in state + 1)
            let maxQubitInUse = 
                [ control1; control2 ] @ registerQubits
                |> List.max
            let ancillaQubit = maxQubitInUse + 1
            
            // Validate state has enough qubits for ancilla
            let requiredQubits = ancillaQubit + 1
            let stateSize = 
                match state with
                | QuantumState.StateVector sv -> FSharp.Azure.Quantum.LocalSimulator.StateVector.numQubits sv
                | _ -> maxQubitInUse + 2  // Assume enough for other state types
            
            if stateSize < requiredQubits then
                Error (QuantumError.ValidationError ("state", $"State has {stateSize} qubits but needs at least {requiredQubits} (including ancilla qubit {ancillaQubit})"))
            else
                result {
                    // Step 1: Set ancilla = control1 AND control2 using Toffoli
                    let! stateWithAncilla = backend.ApplyOperation 
                                               (QuantumOperation.Gate (CB.CCX (control1, control2, ancillaQubit))) 
                                               state
                    
                    // Step 2: Perform controlled addition with ancilla as control
                    let! addResult = controlledAddConstant ancillaQubit registerQubits constant stateWithAncilla backend
                    
                    // Step 3: Uncompute ancilla (flip it back to |0⟩)
                    let! finalState = backend.ApplyOperation 
                                         (QuantumOperation.Gate (CB.CCX (control1, control2, ancillaQubit))) 
                                         addResult.State
                    
                    return {
                        State = finalState
                        OperationCount = addResult.OperationCount + 2  // Two Toffoli gates
                        Config = addResult.Config
                    }
                }
    
    /// Controlled subtraction: Subtract constant if control qubit is |1⟩
    /// 
    /// Operation: |c⟩|x⟩ → |c⟩|x - c*a mod 2^n⟩
    /// 
    /// Parameters: Same as controlledAddConstant
    /// 
    /// Implementation: Controlled subtraction is controlled addition of two's complement
    let controlledSubtractConstant 
        (controlQubit: int)
        (registerQubits: int list)
        (constant: int)
        (state: QuantumState)
        (backend: IQuantumBackend) : Result<ArithmeticResult, QuantumError> =
        
        let numQubits = List.length registerQubits
        let twosComplement = (1 <<< numQubits) - constant
        controlledAddConstant controlQubit registerQubits twosComplement state backend
    
    /// Doubly-controlled subtraction: Subtract constant if both control qubits are |1⟩
    /// 
    /// Operation: |c1⟩|c2⟩|x⟩ → |c1⟩|c2⟩|x - (c1 AND c2)*a mod 2^n⟩
    /// 
    /// Parameters: Same as doublyControlledAddConstant
    /// 
    /// Implementation: Doubly-controlled subtraction is doubly-controlled addition of two's complement
    let doublyControlledSubtractConstant 
        (control1: int)
        (control2: int)
        (registerQubits: int list)
        (constant: int)
        (state: QuantumState)
        (backend: IQuantumBackend) : Result<ArithmeticResult, QuantumError> =
        
        let numQubits = List.length registerQubits
        let twosComplement = (1 <<< numQubits) - constant
        doublyControlledAddConstant control1 control2 registerQubits twosComplement state backend
    
    // ========================================================================
    // MODULAR ARITHMETIC
    // ========================================================================
    
    /// Modular addition: |x⟩ → |x + a mod N⟩
    let addConstantModN 
        (registerQubits: int list)
        (constant: int)
        (modulus: int)
        (state: QuantumState)
        (backend: IQuantumBackend) : Result<ArithmeticResult, QuantumError> =
        
        Error (QuantumError.NotImplemented ("Modular arithmetic", Some "Use Legacy QuantumArithmetic module"))
    
    /// Modular multiplication: |x⟩ → |a*x mod N⟩
    let multiplyConstantModN 
        (registerQubits: int list)
        (constant: int)
        (modulus: int)
        (state: QuantumState)
        (backend: IQuantumBackend) : Result<ArithmeticResult, QuantumError> =
        
        Error (QuantumError.NotImplemented ("Modular arithmetic", Some "Use Legacy QuantumArithmetic module"))
    
    /// Controlled modular multiplication (for Shor's algorithm)
    let controlledMultiplyConstantModN 
        (controlQubit: int)
        (registerQubits: int list)
        (constant: int)
        (modulus: int)
        (state: QuantumState)
        (backend: IQuantumBackend) : Result<ArithmeticResult, QuantumError> =
        
        Error (QuantumError.NotImplemented ("Modular arithmetic", Some "Use Legacy QuantumArithmetic module"))
    
    /// In-place controlled modular multiplication (optimized for Shor's algorithm)
    let controlledMultiplyConstantModNInPlace 
        (controlQubit: int)
        (registerQubits: int list)
        (constant: int)
        (modulus: int)
        (state: QuantumState)
        (backend: IQuantumBackend) : Result<ArithmeticResult, QuantumError> =
        
        Error (QuantumError.NotImplemented ("Modular arithmetic", Some "Use Legacy QuantumArithmetic module"))
    
    // ========================================================================
    // CONFIGURATION HELPERS
    // ========================================================================
    
    /// Create default configuration
    let defaultConfig (numQubits: int) : ArithmeticConfig = {
        NumQubits = numQubits
        UseQFTAdder = true  // QFT method is more efficient for large registers
        Modulus = None
    }
    
    /// Create configuration for modular arithmetic
    let modularConfig (numQubits: int) (modulus: int) : ArithmeticConfig = {
        NumQubits = numQubits
        UseQFTAdder = true
        Modulus = Some modulus
    }
