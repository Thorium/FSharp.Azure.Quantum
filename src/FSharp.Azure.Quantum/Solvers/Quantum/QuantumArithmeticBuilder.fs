namespace FSharp.Azure.Quantum

open FSharp.Azure.Quantum.Core
open FSharp.Azure.Quantum.Algorithms

/// High-level Quantum Arithmetic Builder - QFT-Based Arithmetic Operations
/// 
/// ⚠️ CURRENT IMPLEMENTATION STATUS (MVP):
/// This builder currently uses CLASSICAL SIMULATION to compute arithmetic results
/// for educational and testing purposes. Full QFT circuit execution on backends
/// is planned for future releases. The builder DOES accept IQuantumBackend parameters
/// (Rule 1 compliant) but does not yet execute actual quantum circuits.
/// 
/// For production quantum arithmetic circuits, use the QuantumArithmetic module directly.
/// 
/// DESIGN PHILOSOPHY:
/// This is a BUSINESS DOMAIN API for quantum arithmetic operations
/// without understanding QFT internals (phase rotations, controlled gates, inverse QFT).
/// 
/// QFT-BASED (PLANNED):
/// - Will use Quantum Fourier Transform for arithmetic circuits (Draper adder)
/// - Backend parameter accepted for future cloud quantum hardware support (IonQ, Rigetti)
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
/// - (Future) Modular arithmetic for cryptographic operations on quantum hardware
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
        elif operation.Qubits > 16 then
            Error (QuantumError.ValidationError ("Qubits", $"qubit count {operation.Qubits} exceeds maximum (16 qubits for NISQ devices)"))
        
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
    // EXECUTION FUNCTION
    // ============================================================================
    
    /// Execute quantum arithmetic operation
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
                
                // ========================================================================
                // ⚠️ MVP IMPLEMENTATION - CLASSICAL SIMULATION
                // ========================================================================
                // This builder currently computes results classically for educational
                // and testing purposes. Full QFT circuit execution on backends is planned.
                //
                // FUTURE WORK:
                // 1. Create QuantumArithmeticBackendAdapter module
                // 2. Build QFT-based arithmetic circuits (Draper adder, modular ops)
                // 3. Execute circuits via actualBackend.Execute(circuit, shots)
                // 4. Extract results from measurement histograms
                //
                // For production quantum arithmetic, use QuantumArithmetic module directly.
                // ========================================================================
                
                let result =
                    match operation.Operation with
                    | Add -> 
                        operation.OperandA + operation.OperandB
                    
                    | Multiply ->
                        operation.OperandA * operation.OperandB
                    
                    | ModularAdd ->
                        let modulus = operation.Modulus.Value
                        (operation.OperandA + operation.OperandB) % modulus
                    
                    | ModularMultiply ->
                        let modulus = operation.Modulus.Value
                        (operation.OperandA * operation.OperandB) % modulus
                    
                    | ModularExponentiate ->
                        let modulus = operation.Modulus.Value
                        let rec modPow b e m acc =
                            if e = 0 then acc
                            elif e % 2 = 0 then modPow ((b * b) % m) (e / 2) m acc
                            else modPow b (e - 1) m ((acc * b) % m)
                        modPow operation.OperandA operation.OperandB modulus 1
                
                // Estimate circuit metrics
                let gateCount = 
                    match operation.Operation with
                    | Add | ModularAdd -> operation.Qubits * operation.Qubits  // QFT gates
                    | Multiply | ModularMultiply -> operation.Qubits * operation.Qubits * 2
                    | ModularExponentiate -> operation.Qubits * operation.Qubits * operation.OperandB
                
                let circuitDepth =
                    match operation.Operation with
                    | Add | ModularAdd -> operation.Qubits * 2
                    | Multiply | ModularMultiply -> operation.Qubits * 3
                    | ModularExponentiate -> operation.Qubits * operation.OperandB
                
                let backendName = 
                    match operation.Backend with
                    | Some backend -> backend.GetType().Name
                    | None -> "LocalBackend (Simulation)"
                
                let isModular =
                    match operation.Operation with
                    | ModularAdd | ModularMultiply | ModularExponentiate -> true
                    | _ -> false
                
                Ok {
                    Value = result
                    QubitsUsed = operation.Qubits
                    GateCount = gateCount
                    CircuitDepth = circuitDepth
                    OperationType = operation.Operation
                    BackendName = backendName
                    IsModular = isModular
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
