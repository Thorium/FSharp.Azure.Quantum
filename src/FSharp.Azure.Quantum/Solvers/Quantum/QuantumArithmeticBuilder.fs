namespace FSharp.Azure.Quantum

open FSharp.Azure.Quantum.Core
open FSharp.Azure.Quantum.Core.BackendAbstraction
open FSharp.Azure.Quantum.Algorithms

/// High-level Quantum Arithmetic Builder - QFT-Based Arithmetic Operations
/// 
/// Executes real QFT-based quantum arithmetic circuits via the Arithmetic module
/// (Draper adder). All operations run on IQuantumBackend (defaults to LocalBackend
/// for simulation). For algorithm-level control, use the Arithmetic module directly.
/// 
/// DESIGN PHILOSOPHY:
/// This is a BUSINESS DOMAIN API for quantum arithmetic operations
/// without understanding QFT internals (phase rotations, controlled gates, inverse QFT).
/// 
/// QFT-BASED:
/// - Uses Quantum Fourier Transform for arithmetic circuits (Draper adder)
/// - Backend parameter accepted for cloud quantum hardware support (IonQ, Rigetti)
/// - For algorithm-level control, use QuantumArithmetic module directly
/// 
/// WHAT IS QUANTUM ARITHMETIC:
/// Perform arithmetic operations (addition, multiplication, exponentiation) using quantum circuits.
/// Uses QFT-based algorithms for efficiency and educational demonstrations.
/// 
/// USE CASES:
/// - Educational demonstrations of quantum arithmetic concepts
/// - Testing and validation of arithmetic logic
/// - Prototyping quantum algorithms requiring arithmetic
/// - Modular arithmetic for cryptographic operations on quantum hardware
/// 
/// EXAMPLE USAGE:
///   // Simple addition
///   let operation = quantumArithmetic {
///       operands 42 17
///       operation Add
///       qubits 8
///   }
///   
///   // Modular arithmetic for cryptography
///   let modOp = quantumArithmetic {
///       operands 7 5
///       operation ModularMultiply
///       modulus 15
///       qubits 8
///       backend ionqBackend
///   }
///   
///   // Execute the operation
///   match QuantumArithmeticOps.execute operation with
///   | Ok result -> printfn "Result: %d" result.Value
///   | Error msg -> printfn "Error: %s" msg
module QuantumArithmeticOps =
    
    // ============================================================================
    // CORE TYPES - Quantum Arithmetic Domain Model
    // ============================================================================
    
    /// Arithmetic operation type
    type OperationType =
        | Add                    // a + b
        | Multiply               // a × b
        | ModularAdd            // (a + b) mod N
        | ModularMultiply       // (a × b) mod N
        | ModularExponentiate   // a^x mod N
    
    /// <summary>
    /// Complete quantum arithmetic operation specification.
    /// </summary>
    type ArithmeticOperation = {
        /// First operand
        OperandA: int
        /// Second operand (or exponent for exponentiation)
        OperandB: int
        /// Type of operation
        Operation: OperationType
        /// Modulus for modular arithmetic (required for modular operations)
        Modulus: int option
        /// Number of qubits to use for computation
        Qubits: int
        /// Quantum backend to use (None = LocalBackend)
        Backend: BackendAbstraction.IQuantumBackend option
        /// Number of measurement shots (None = auto-scale: 100 for Local, 500 for Cloud)
        /// Used for statistical verification and noise characterization
        Shots: int option
    }
    
    /// <summary>
    /// Result of quantum arithmetic operation.
    /// </summary>
    type ArithmeticResult = {
        /// Result value of the computation
        Value: int
        /// Total qubits used
        QubitsUsed: int
        /// Total gates applied (circuit size)
        GateCount: int
        /// Circuit depth
        CircuitDepth: int
        /// Operation performed
        OperationType: OperationType
        /// Backend used for execution
        BackendName: string
        /// Whether modular arithmetic was used
        IsModular: bool
    }
    
    // ============================================================================
    // VALIDATION HELPERS
    // ============================================================================
    
    /// Compute the actual total qubits required for a given operation and register size.
    /// Different operations expand the user-specified register size by different amounts
    /// for ancilla, temp registers, overflow/flag qubits, etc.
    let private computeTotalQubits (n: int) (opType: OperationType) : int =
        match opType with
        | Add -> n
        | Multiply -> n
        | ModularAdd -> n + 2  // overflow + flag for Beauregard
        | ModularMultiply -> 2 * n + 3  // input + output + overflow + flag + AND-ancilla
        | ModularExponentiate -> 2 * n + 5  // result + temp + control + AND + overflow + flag + AND

    /// Maximum total qubits supported by LocalBackend simulation
    let private maxSimulationQubits = 20

    /// <summary>
    /// Validates an arithmetic operation specification.
    /// </summary>
    let private validate (operation: ArithmeticOperation) : Result<unit, QuantumError> =
        // Check operands are non-negative
        if operation.OperandA < 0 then
            Error (QuantumError.ValidationError ("OperandA", "must be non-negative"))
        elif operation.OperandB < 0 then
            Error (QuantumError.ValidationError ("OperandB", "must be non-negative"))
        
        // Check qubits
        elif operation.Qubits < 2 then
            Error (QuantumError.ValidationError ("Qubits", "at least 2 qubits are required"))
        else
            // Validate actual total qubits required for the specific operation
            let totalQubits = computeTotalQubits operation.Qubits operation.Operation
            if totalQubits > maxSimulationQubits then
                Error (QuantumError.ValidationError ("Qubits",
                    $"register size {operation.Qubits} requires {totalQubits} total qubits for {operation.Operation} (max {maxSimulationQubits} for simulation)"))
            
            // Check modulus for modular operations
            elif match operation.Operation with
                 | ModularAdd | ModularMultiply | ModularExponentiate -> operation.Modulus.IsNone
                 | _ -> false
            then
                Error (QuantumError.ValidationError ("Modulus", "modulus is required for modular arithmetic operations"))
            
            // Check modulus is positive
            elif operation.Modulus.IsSome && operation.Modulus.Value <= 0 then
                Error (QuantumError.ValidationError ("Modulus", "modulus must be positive"))
            
            // Check modulus is larger than operands
            elif match operation.Operation with
                 | ModularAdd | ModularMultiply when operation.Modulus.IsSome ->
                     operation.OperandA >= operation.Modulus.Value || operation.OperandB >= operation.Modulus.Value
                 | _ -> false
            then
                Error (QuantumError.ValidationError ("Operands", "operands must be smaller than modulus for modular arithmetic"))
            
            // Check operand A fits in qubit count
            elif operation.OperandA >= (1 <<< operation.Qubits) then
                Error (QuantumError.ValidationError ("OperandA", $"operandA ({operation.OperandA}) requires more than {operation.Qubits} qubits"))
            
            // Check operand B fits in qubit count (except for exponentiation where B is exponent)
            elif operation.Operation <> ModularExponentiate && operation.OperandB >= (1 <<< operation.Qubits) then
                Error (QuantumError.ValidationError ("OperandB", $"operandB ({operation.OperandB}) requires more than {operation.Qubits} qubits"))
            
            else
                Ok ()
    
    // ============================================================================
    // COMPUTATION EXPRESSION BUILDER
    // ============================================================================
    
    /// <summary>
    /// Computation expression builder for quantum arithmetic operations.
    /// Provides F#-idiomatic DSL for constructing arithmetic operations.
    /// </summary>
    type QuantumArithmeticBuilder() =
        
        /// Default arithmetic operation (all fields required)
        let defaultOperation = {
            OperandA = 0
            OperandB = 0
            Operation = Add
            Modulus = None
            Qubits = 8
            Backend = None
            Shots = None  // Auto-scale based on backend
        }
        
        /// Initialize builder with default operation
        member _.Yield(_) = defaultOperation
        
        /// Set both operands
        [<CustomOperation("operands")>]
        member _.Operands(operation: ArithmeticOperation, a: int, b: int) : ArithmeticOperation =
            { operation with OperandA = a; OperandB = b }
        
        /// Set first operand
        [<CustomOperation("operandA")>]
        member _.OperandA(operation: ArithmeticOperation, a: int) : ArithmeticOperation =
            { operation with OperandA = a }
        
        /// Set second operand
        [<CustomOperation("operandB")>]
        member _.OperandB(operation: ArithmeticOperation, b: int) : ArithmeticOperation =
            { operation with OperandB = b }
        
        /// Set operation type
        [<CustomOperation("operation")>]
        member _.Operation(operation: ArithmeticOperation, opType: OperationType) : ArithmeticOperation =
            { operation with Operation = opType }
        
        /// Set modulus for modular arithmetic
        [<CustomOperation("modulus")>]
        member _.Modulus(operation: ArithmeticOperation, m: int) : ArithmeticOperation =
            { operation with Modulus = Some m }
        
        /// Set qubit count
        [<CustomOperation("qubits")>]
        member _.Qubits(operation: ArithmeticOperation, n: int) : ArithmeticOperation =
            { operation with Qubits = n }
        
        /// Set exponent (alias for operandB for exponentiation)
        [<CustomOperation("exponent")>]
        member _.Exponent(operation: ArithmeticOperation, exp: int) : ArithmeticOperation =
            { operation with OperandB = exp }
        
        /// Set quantum backend
        [<CustomOperation("backend")>]
        member _.Backend(operation: ArithmeticOperation, backend: BackendAbstraction.IQuantumBackend) : ArithmeticOperation =
            { operation with Backend = Some backend }
        
        /// <summary>
        /// Set the number of measurement shots for statistical verification.
        /// Used for noise characterization and error mitigation in noisy quantum environments.
        /// </summary>
        /// <param name="shots">Number of measurements (typical: 100-500)</param>
        /// <remarks>
        /// If not specified, auto-scales based on backend:
        /// - LocalBackend: 100 shots (deterministic simulation)
        /// - Cloud backends: 500 shots (statistical verification on noisy hardware)
        /// Note: Arithmetic operations are deterministic, but shots help verify correctness on real hardware.
        /// </remarks>
        [<CustomOperation("shots")>]
        member _.Shots(operation: ArithmeticOperation, shots: int) : ArithmeticOperation =
            { operation with Shots = Some shots }
        
        /// Finalize and validate the operation
        member _.Run(operation: ArithmeticOperation) : Result<ArithmeticOperation, QuantumError> =
            validate operation |> Result.map (fun _ -> operation)
    
    /// Global computation expression instance for quantum arithmetic
    let quantumArithmetic = QuantumArithmeticBuilder()
    
    // ============================================================================
    // QUANTUM STATE HELPERS
    // ============================================================================
    
    /// Encode a classical integer into a quantum register by applying X gates
    /// to qubits corresponding to set bits (LSB-first convention).
    let private encodeInteger
        (registerQubits: int list)
        (value: int)
        (state: QuantumState)
        (backend: BackendAbstraction.IQuantumBackend) : Result<QuantumState, QuantumError> =
        
        let rec applyBits currentState bitIndex =
            if bitIndex >= List.length registerQubits then
                Ok currentState
            else
                let bit = (value >>> bitIndex) &&& 1
                if bit = 1 then
                    match backend.ApplyOperation (QuantumOperation.Gate (CircuitBuilder.X registerQubits.[bitIndex])) currentState with
                    | Error err -> Error err
                    | Ok nextState -> applyBits nextState (bitIndex + 1)
                else
                    applyBits currentState (bitIndex + 1)
        applyBits state 0
    
    /// Extract the integer value from a quantum register by measuring and interpreting
    /// the most common bitstring (LSB-first convention).
    let private extractRegisterValue
        (registerQubits: int list)
        (state: QuantumState)
        (shots: int) : int =
        
        let measurements = QuantumState.measure state shots
        // For deterministic circuits, all measurements should agree.
        // Take majority vote: count occurrences of each extracted value.
        measurements
        |> Array.map (fun bitstring ->
            registerQubits
            |> List.mapi (fun pos q -> bitstring.[q] <<< pos)
            |> List.sum)
        |> Array.countBy id
        |> Array.maxBy snd
        |> fst
    
    // ============================================================================
    // EXECUTION FUNCTION
    // ============================================================================
    
    /// Execute quantum arithmetic operation using real QFT-based circuits
    /// 
    /// All operations are executed as genuine quantum circuits using the
    /// Arithmetic module (Draper QFT-based adder). If no backend is specified,
    /// a LocalBackend simulator is used.
    /// 
    /// Example:
    ///   let operation = quantumArithmetic {
    ///       operands 42 17
    ///       operation Add
    ///       qubits 8
    ///   }
    ///   let result = QuantumArithmeticOps.execute operation
    let execute (operation: ArithmeticOperation) : Result<ArithmeticResult, QuantumError> =
        
        try
            // Validate operation first
            match validate operation with
            | Error err -> Error err
            | Ok () ->
                
                // Use provided backend or create LocalBackend for simulation
                let actualBackend = 
                    operation.Backend 
                    |> Option.defaultValue (Backends.LocalBackend.LocalBackend() :> Core.BackendAbstraction.IQuantumBackend)
                
                let n = operation.Qubits
                let shots = operation.Shots |> Option.defaultValue 100
                
                let isModular =
                    match operation.Operation with
                    | ModularAdd | ModularMultiply | ModularExponentiate -> true
                    | _ -> false
                
                let backendName = actualBackend.GetType().Name
                
                match operation.Operation with
                
                // ================================================================
                // ADD: |0⟩ → encode OperandA → addConstant OperandB → measure
                // ================================================================
                | Add ->
                    result {
                        let registerQubits = [ 0 .. n - 1 ]
                        let! state0 = actualBackend.InitializeState n
                        let! prepared = encodeInteger registerQubits operation.OperandA state0 actualBackend
                        let! addResult = Arithmetic.addConstant registerQubits operation.OperandB prepared actualBackend
                        let value = extractRegisterValue registerQubits addResult.State shots
                        return {
                            Value = value
                            QubitsUsed = n
                            GateCount = addResult.OperationCount
                            CircuitDepth = n * 2
                            OperationType = Add
                            BackendName = backendName
                            IsModular = false
                        }
                    }
                
                // ================================================================
                // MULTIPLY: Shift-and-add using QFT adder
                // Encode OperandA in output register, add shifted copies for
                // each set bit of OperandB (classical decomposition, quantum additions)
                // ================================================================
                | Multiply ->
                    result {
                        // Output register holds the accumulated result
                        let outputQubits = [ 0 .. n - 1 ]
                        let! state0 = actualBackend.InitializeState n
                        
                        // Shift-and-add: for each set bit k of OperandB,
                        // add (OperandA << k) mod 2^n to the output register
                        let rec shiftAndAdd currentState bitIndex totalOps =
                            if bitIndex >= n then
                                Ok (currentState, totalOps)
                            else
                                let bit = (operation.OperandB >>> bitIndex) &&& 1
                                if bit = 1 then
                                    let addend = (operation.OperandA <<< bitIndex) &&& ((1 <<< n) - 1)
                                    if addend > 0 then
                                        result {
                                            let! addResult = Arithmetic.addConstant outputQubits addend currentState actualBackend
                                            return! shiftAndAdd addResult.State (bitIndex + 1) (totalOps + addResult.OperationCount)
                                        }
                                    else
                                        shiftAndAdd currentState (bitIndex + 1) totalOps
                                else
                                    shiftAndAdd currentState (bitIndex + 1) totalOps
                        
                        let! (finalState, opCount) = shiftAndAdd state0 0 0
                        let value = extractRegisterValue outputQubits finalState shots
                        return {
                            Value = value
                            QubitsUsed = n
                            GateCount = opCount
                            CircuitDepth = n * 3
                            OperationType = Multiply
                            BackendName = backendName
                            IsModular = false
                        }
                    }
                
                // ================================================================
                // MODULAR ADD: |0⟩ → encode OperandA → addConstantModN OperandB → measure
                // Uses quantum Beauregard modular addition (proper mod reduction in Fourier basis)
                // ================================================================
                | ModularAdd ->
                    let modulus = operation.Modulus.Value
                    result {
                        let registerQubits = [ 0 .. n - 1 ]
                        // Beauregard modular addition needs 2 ancilla qubits (overflow + flag)
                        let totalQubits = n + 2
                        let! state0 = actualBackend.InitializeState totalQubits
                        let! prepared = encodeInteger registerQubits operation.OperandA state0 actualBackend
                        let! addResult = Arithmetic.addConstantModN registerQubits operation.OperandB modulus prepared actualBackend
                        let value = extractRegisterValue registerQubits addResult.State shots
                        return {
                            Value = value
                            QubitsUsed = totalQubits
                            GateCount = addResult.OperationCount
                            CircuitDepth = n * 2
                            OperationType = ModularAdd
                            BackendName = backendName
                            IsModular = true
                        }
                    }
                
                // ================================================================
                // MODULAR MULTIPLY: |x⟩|0⟩ → |x⟩|ax mod N⟩
                // Uses quantum modular multiplication (Beauregard algorithm with
                // controlled modular additions that keep accumulator in [0,N))
                // ================================================================
                | ModularMultiply ->
                    let modulus = operation.Modulus.Value
                    result {
                        let inputQubits = [ 0 .. n - 1 ]
                        let outputQubits = [ n .. 2 * n - 1 ]
                        // controlledAddConstantModN needs ancilla chain:
                        //   control=inputQubit (max n-1), register=outputQubits (max 2n-1)
                        //   overflow = 2n, flag = 2n+1
                        //   doublyControlledAddConstant AND-ancilla = 2n+2
                        let totalQubits = 2 * n + 3
                        let! state0 = actualBackend.InitializeState totalQubits
                        let! prepared = encodeInteger inputQubits operation.OperandB state0 actualBackend
                        let! mulResult = Arithmetic.multiplyConstantModN inputQubits outputQubits operation.OperandA modulus prepared actualBackend
                        let value = extractRegisterValue outputQubits mulResult.State shots
                        return {
                            Value = value
                            QubitsUsed = totalQubits
                            GateCount = mulResult.OperationCount
                            CircuitDepth = n * 3
                            OperationType = ModularMultiply
                            BackendName = backendName
                            IsModular = true
                        }
                    }
                
                // ================================================================
                // MODULAR EXPONENTIATE: a^e mod N
                // Uses controlledMultiplyConstantModNInPlace in square-and-multiply
                // ================================================================
                | ModularExponentiate ->
                    let modulus = operation.Modulus.Value
                    let baseVal = operation.OperandA
                    let exponent = operation.OperandB
                    result {
                        // Register layout:
                        // [0..n-1]     = result register (initialized to 1)
                        // [n..2n-1]    = temp register for in-place multiply
                        // [2n]         = control qubit (used for conditional multiply)
                        // Ancilla chain (dynamically allocated by nested functions):
                        //   [2n+1]     = AND-ancilla for doublyControlledAddConstantModN
                        //   [2n+2]     = overflow qubit for controlledAddConstantModN (Beauregard)
                        //   [2n+3]     = flag qubit for controlledAddConstantModN (Beauregard)
                        //   [2n+4]     = AND-ancilla for doublyControlledAddConstant (inside Beauregard step 4)
                        let registerQubits = [ 0 .. n - 1 ]
                        let tempQubits = [ n .. 2 * n - 1 ]
                        let controlQubit = 2 * n
                        let totalQubits = 2 * n + 5
                        
                        let! state0 = actualBackend.InitializeState totalQubits
                        
                        // Initialize result register to 1 (set qubit 0)
                        let! stateWith1 = encodeInteger registerQubits 1 state0 actualBackend
                        
                        // Square-and-multiply: for each bit of exponent,
                        // if bit is set, multiply result by base^(2^k) mod N
                        // Precompute powers: base^(2^0), base^(2^1), ...
                        let rec squareAndMultiply currentState bitIndex currentPower totalOps =
                            if bitIndex >= 32 || (1 <<< bitIndex) > exponent then
                                Ok (currentState, totalOps)
                            else
                                let bit = (exponent >>> bitIndex) &&& 1
                                if bit = 1 then
                                    result {
                                        // Set control qubit to |1⟩ to enable the multiply
                                        let! withControl =
                                            actualBackend.ApplyOperation
                                                (QuantumOperation.Gate (CircuitBuilder.X controlQubit))
                                                currentState
                                        
                                        // In-place multiply: result = result * currentPower mod N
                                        let! mulResult =
                                            Arithmetic.controlledMultiplyConstantModNInPlace
                                                controlQubit registerQubits tempQubits
                                                currentPower modulus
                                                withControl actualBackend
                                        
                                        // Reset control qubit back to |0⟩
                                        let! withoutControl =
                                            actualBackend.ApplyOperation
                                                (QuantumOperation.Gate (CircuitBuilder.X controlQubit))
                                                mulResult.State
                                        
                                        let nextPower = (currentPower * currentPower) % modulus
                                        return! squareAndMultiply withoutControl (bitIndex + 1) nextPower (totalOps + mulResult.OperationCount + 2)
                                    }
                                else
                                    let nextPower = (currentPower * currentPower) % modulus
                                    squareAndMultiply currentState (bitIndex + 1) nextPower totalOps
                        
                        let! (finalState, opCount) = squareAndMultiply stateWith1 0 (baseVal % modulus) 0
                        let value = extractRegisterValue registerQubits finalState shots
                        return {
                            Value = value
                            QubitsUsed = totalQubits
                            GateCount = opCount
                            CircuitDepth = n * exponent
                            OperationType = ModularExponentiate
                            BackendName = backendName
                            IsModular = true
                        }
                    }
        with
        | ex -> Error (QuantumError.OperationError ("quantum arithmetic execution", ex.Message))
    
    // ============================================================================
    // CONVENIENCE FUNCTIONS
    // ============================================================================
    
    /// Quick helper for simple addition
    let add (a: int) (b: int) (qubits: int) : ArithmeticOperation =
        {
            OperandA = a
            OperandB = b
            Operation = Add
            Modulus = None
            Qubits = qubits
            Backend = None
            Shots = None  // Auto-scale
        }
    
    /// Quick helper for modular addition
    let modularAdd (a: int) (b: int) (modulus: int) (qubits: int) : ArithmeticOperation =
        {
            OperandA = a
            OperandB = b
            Operation = ModularAdd
            Modulus = Some modulus
            Qubits = qubits
            Backend = None
            Shots = None  // Auto-scale
        }
    
    /// Quick helper for modular multiplication
    let modularMultiply (a: int) (b: int) (modulus: int) (qubits: int) : ArithmeticOperation =
        {
            OperandA = a
            OperandB = b
            Operation = ModularMultiply
            Modulus = Some modulus
            Qubits = qubits
            Backend = None
            Shots = None  // Auto-scale
        }
    
    /// Quick helper for modular exponentiation
    let modularExponentiate (baseNum: int) (exponent: int) (modulus: int) (qubits: int) : ArithmeticOperation =
        {
            OperandA = baseNum
            OperandB = exponent
            Operation = ModularExponentiate
            Modulus = Some modulus
            Qubits = qubits
            Backend = None
            Shots = None  // Auto-scale
        }
    
    /// Estimate resource requirements without executing
    let estimateResources (qubits: int) (operationType: OperationType) : string =
        let gateEstimate =
            match operationType with
            | Add | ModularAdd -> qubits * qubits
            | Multiply | ModularMultiply -> qubits * qubits * 2
            | ModularExponentiate -> qubits * qubits * 10  // Assume exp=10 average
        
        let depthEstimate =
            match operationType with
            | Add | ModularAdd -> qubits * 2
            | Multiply | ModularMultiply -> qubits * 3
            | ModularExponentiate -> qubits * 10
        
        sprintf """Quantum Arithmetic Resource Estimate:
  Operation: %A
  Qubits Required: %d
  Gate Count Estimate: %d gates
  Circuit Depth Estimate: %d
  Feasibility: %s"""
            operationType
            qubits
            gateEstimate
            depthEstimate
            (if qubits <= 16 then "✓ Feasible on NISQ devices" else "✗ Requires fault-tolerant quantum computer")
    
    /// Export result to human-readable string
    let describeResult (result: ArithmeticResult) : string =
        let modularText = if result.IsModular then "Yes (modular arithmetic)" else "No"
        
        sprintf """=== Quantum Arithmetic Result ===
Result Value: %d
Operation: %A
Modular Arithmetic: %s
Qubits Used: %d
Gate Count: %d gates
Circuit Depth: %d
Backend: %s"""
            result.Value
            result.OperationType
            modularText
            result.QubitsUsed
            result.GateCount
            result.CircuitDepth
            result.BackendName
