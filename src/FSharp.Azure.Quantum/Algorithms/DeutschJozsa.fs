namespace FSharp.Azure.Quantum.Algorithms

open System
open FSharp.Azure.Quantum
open FSharp.Azure.Quantum.CircuitBuilder
open FSharp.Azure.Quantum.Core
open FSharp.Azure.Quantum.Core.BackendAbstraction

/// Deutsch-Jozsa Algorithm
/// 
/// The Deutsch-Jozsa algorithm determines whether a boolean function f: {0,1}^n → {0,1}
/// is constant (returns 0 for all inputs or 1 for all inputs) or balanced 
/// (returns 0 for exactly half the inputs and 1 for the other half).
/// 
/// This is the canonical first quantum algorithm demonstrating quantum advantage,
/// appearing in every quantum computing textbook:
/// - "Learn Quantum Computing with Python and Q#" (Kaiser, 2021) - Chapter 8, Appendix D
/// - "Quantum Programming in Depth" (Manning, 2024) - Chapter 6
/// - Wikipedia: Deutsch-Jozsa algorithm
/// 
/// Classical approach: Requires up to 2^(n-1) + 1 function evaluations (deterministic)
/// Quantum approach: Requires exactly 1 function evaluation (deterministic)
/// 
/// Key quantum concepts demonstrated:
/// - Quantum parallelism (evaluating function on all inputs simultaneously)
/// - Phase kickback
/// - Interference (constructive/destructive)
/// - Oracle-based algorithms
/// 
/// Performance:
/// - Deterministic success (100% accuracy)
/// - Single query to oracle (vs exponential classical queries)
/// - NISQ-era executable (works on current quantum hardware)
/// 
/// **Production Value**: ⭐☆☆☆☆ (Educational only - "bubble-sort tier")
/// - No real-world problem requires determining if a black-box function is constant/balanced
/// - Classical randomized algorithm solves with 2 queries
/// - Included for: Textbook completeness, teaching quantum concepts
module DeutschJozsa =
    
    // ========================================================================
    // TYPES
    // ========================================================================
    
    /// Oracle function type: Constant or Balanced
    type OracleType =
        | Constant  // f(x) = 0 for all x OR f(x) = 1 for all x
        | Balanced  // f(x) = 0 for half inputs, f(x) = 1 for other half
    
    /// Deutsch-Jozsa algorithm result
    type DeutschJozsaResult = {
        /// Determined oracle type
        OracleType: OracleType
        
        /// Probability of measuring |00...0⟩ state
        ZeroProbability: float
        
        /// Number of qubits used
        NumQubits: int
        
        /// Number of shots performed
        Shots: int
        
        /// Backend used
        BackendName: string
    }
    
    /// Oracle implementation - applies phase flip for marked states
    /// Oracle: |x⟩ → (-1)^f(x) |x⟩ (phase oracle form)
    type Oracle = QuantumState -> Result<QuantumState, QuantumError>

    type private DeutschJozsaIntent = {
        NumQubits: int
        Oracle: Oracle
    }

    [<RequireQualifiedAccess>]
    type private DeutschJozsaPlan =
        | ExecuteViaOpsAndOracle of preOps: QuantumOperation list * oracle: Oracle * postOps: QuantumOperation list

    // ========================================================================
    // HELPERS
    // ========================================================================

    let private gatesOnAllQubits (gate: int -> Gate) (numQubits: int) : QuantumOperation list =
        [ 0 .. numQubits - 1 ]
        |> List.map (fun i -> QuantumOperation.Gate (gate i))

    let private oracleFromOps (backend: IQuantumBackend) (ops: QuantumOperation list) : Oracle =
        fun state -> UnifiedBackend.applySequence backend ops state

    // ========================================================================
    // ORACLE CONSTRUCTORS
    // ========================================================================

    /// Create constant-zero oracle: f(x) = 0 for all x.
    ///
    /// In phase-oracle form this is the identity: |x⟩ → |x⟩.
    let constantZeroOracle (state: QuantumState) : Result<QuantumState, QuantumError> =
        Ok state

    /// Create constant-one oracle: f(x) = 1 for all x.
    ///
    /// In phase-oracle form this is a global phase flip: |x⟩ → -|x⟩.
    /// Global phase is not observable, so we model it as identity.
    let constantOneOracle : Oracle =
        fun state -> Ok state

    /// Create balanced oracle that flips phase for states where first qubit is |1⟩.
    /// Implements f(x) = x₀.
    let balancedFirstBitOracle (backend: IQuantumBackend) : Oracle =
        oracleFromOps backend [ QuantumOperation.Gate (Z 0) ]

    /// Create balanced oracle that flips phase for states with odd parity.
    /// Implements f(x) = x₀ ⊕ x₁ ⊕ ... ⊕ xₙ₋₁.
    let balancedParityOracle (numQubits: int) (backend: IQuantumBackend) : Oracle =
        oracleFromOps backend (gatesOnAllQubits Z numQubits)

    // ========================================================================
    // INTENT → PLAN → EXECUTION (ADR: intent-first algorithms)
    // ========================================================================

    let private plan (backend: IQuantumBackend) (intent: DeutschJozsaIntent) : Result<DeutschJozsaPlan, QuantumError> =
        // Deutsch-Jozsa requires standard gate-based operations (H, and whatever the oracle requires).
        // Some backends (e.g., annealing) cannot support this, and we should fail explicitly.
        match backend.NativeStateType with
        | QuantumStateType.Annealing ->
            Error (QuantumError.OperationError ("DeutschJozsa", $"Backend '{backend.Name}' does not support Deutsch-Jozsa (native state type: {backend.NativeStateType})"))
        | _ ->
            // Today we always lower to ops + explicit oracle.
            // Future: add `QuantumOperation.Algorithm (AlgorithmOperation.DeutschJozsa ...)` if/when supported.
            let hadamards = gatesOnAllQubits H intent.NumQubits
            if hadamards |> List.forall backend.SupportsOperation then
                Ok (DeutschJozsaPlan.ExecuteViaOpsAndOracle (hadamards, intent.Oracle, hadamards))
            else
                Error (QuantumError.OperationError ("DeutschJozsa", $"Backend '{backend.Name}' does not support required operations for Deutsch-Jozsa"))

    let private executePlan
        (backend: IQuantumBackend)
        (state: QuantumState)
        (plan: DeutschJozsaPlan)
        : Result<QuantumState, QuantumError> =

        match plan with
        | DeutschJozsaPlan.ExecuteViaOpsAndOracle (preOps, oracle, postOps) ->
            result {
                let! afterPre = UnifiedBackend.applySequence backend preOps state
                let! afterOracle = oracle afterPre
                return! UnifiedBackend.applySequence backend postOps afterOracle
            }

    // ========================================================================
    // ALGORITHM IMPLEMENTATION
    // ========================================================================

    /// Run Deutsch-Jozsa algorithm with custom oracle
    /// 
    /// Algorithm steps:
    /// 1. Initialize |0⟩^⊗n state
    /// 2. Apply Hadamard to all qubits → equal superposition
    /// 3. Apply oracle (phase oracle form)
    /// 4. Apply Hadamard to all qubits → interference
    /// 5. Measure all qubits
    /// 
    /// Result interpretation:
    /// - Measure |00...0⟩ → Constant function
    /// - Measure anything else → Balanced function
    /// 
    /// Parameters:
    ///   oracle - Phase oracle implementing f(x)
    ///   numQubits - Number of qubits (search space = 2^n)
    ///   backend - Quantum backend to execute on
    ///   shots - Number of measurement shots
    /// 
    /// Returns:
    ///   DeutschJozsaResult with oracle type determination
    let run
        (oracle: Oracle)
        (numQubits: int)
        (backend: IQuantumBackend)
        (shots: int)
        : Result<DeutschJozsaResult, QuantumError> =

        // Validate inputs
        if numQubits < 1 then
            Error (QuantumError.ValidationError ("numQubits", "Deutsch-Jozsa requires at least 1 qubit"))
        elif numQubits > 20 then
            Error (QuantumError.ValidationError ("numQubits", "Deutsch-Jozsa with >20 qubits not practical on NISQ hardware"))
        elif shots < 1 then
            Error (QuantumError.ValidationError ("shots", "Deutsch-Jozsa requires at least 1 shot"))
        else
            result {
                let intent = { NumQubits = numQubits; Oracle = oracle }

                // Step 1: Initialize |0⟩^⊗n state
                let! initialState = backend.InitializeState numQubits

                // Step 2: Plan and execute
                let! djPlan = plan backend intent
                let! finalState = executePlan backend initialState djPlan

                // Step 3: Measure and interpret
                let measurements = UnifiedBackend.measureState finalState shots

                let isAllZero (bits: int[]) = bits |> Array.forall ((=) 0)

                let zeroCount =
                    measurements
                    |> Array.filter isAllZero
                    |> Array.length

                let zeroProbability = float zeroCount / float shots

                // Ideal DJ: constant → always zero, balanced → never zero.
                // Use threshold to handle noise from real backends.
                let oracleType = if zeroProbability > 0.99 then Constant else Balanced

                return {
                    OracleType = oracleType
                    ZeroProbability = zeroProbability
                    NumQubits = numQubits
                    Shots = shots
                    BackendName = backend.Name
                }
            }
    
    // ========================================================================
    // CONVENIENCE FUNCTIONS
    // ========================================================================
    
    /// Run Deutsch-Jozsa with constant-zero oracle (identity)
    let runConstantZero (numQubits: int) (backend: IQuantumBackend) (shots: int)
        : Result<DeutschJozsaResult, QuantumError> =
        run constantZeroOracle numQubits backend shots
    
    /// Run Deutsch-Jozsa with constant-one oracle (global phase flip)
    let runConstantOne (numQubits: int) (backend: IQuantumBackend) (shots: int)
        : Result<DeutschJozsaResult, QuantumError> =
        run constantOneOracle numQubits backend shots
    
    /// Run Deutsch-Jozsa with balanced first-bit oracle (f(x) = x_0)
    let runBalancedFirstBit (numQubits: int) (backend: IQuantumBackend) (shots: int)
        : Result<DeutschJozsaResult, QuantumError> =
        run (balancedFirstBitOracle backend) numQubits backend shots
    
    /// Run Deutsch-Jozsa with balanced parity oracle (f(x) = x_0 ⊕ x_1 ⊕ ... ⊕ x_n)
    let runBalancedParity (numQubits: int) (backend: IQuantumBackend) (shots: int)
        : Result<DeutschJozsaResult, QuantumError> =
        run (balancedParityOracle numQubits backend) numQubits backend shots
    
    // ========================================================================
    // PRETTY PRINTING
    // ========================================================================
    
    /// Format Deutsch-Jozsa result for display
    let formatResult (result: DeutschJozsaResult) : string =
        let oracleTypeStr = match result.OracleType with | Constant -> "Constant" | Balanced -> "Balanced"
        
        sprintf "Deutsch-Jozsa Result:\n  Oracle Type: %s\n  Zero Probability: %.2f%%\n  Qubits: %d\n  Shots: %d\n  Backend: %s"
            oracleTypeStr
            (result.ZeroProbability * 100.0)
            result.NumQubits
            result.Shots
            result.BackendName
