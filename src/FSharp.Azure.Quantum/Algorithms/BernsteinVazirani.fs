namespace FSharp.Azure.Quantum.Algorithms

open System
open FSharp.Azure.Quantum
open FSharp.Azure.Quantum.CircuitBuilder
open FSharp.Azure.Quantum.Core
open FSharp.Azure.Quantum.Core.BackendAbstraction

/// Bernstein-Vazirani Algorithm
///
/// The Bernstein-Vazirani algorithm recovers a hidden bitstring s from a
/// black-box function f(x) = s·x (mod 2) using a single oracle query.
///
/// Part of the textbook algorithm canon:
/// - "Quantum Computing: An Applied Approach" (Hidary, 2021) - The Canon chapter
/// - "Introduction to Quantum Computing with Q# and QDK" - Chapter 7.2
/// - Wikipedia: Bernstein-Vazirani algorithm
///
/// Classical approach: Requires n queries (one per bit of s)
/// Quantum approach: Requires exactly 1 query (deterministic)
///
/// Key quantum concepts demonstrated:
/// - Phase kickback (same circuit shape as Deutsch-Jozsa)
/// - Reading out a full bitstring from a single interference pattern
///
/// Circuit (phase oracle form):
///   H^⊗n → oracle |x⟩ → (-1)^(s·x) |x⟩ → H^⊗n → measure = s
///
/// **Production Value**: ⭐☆☆☆☆ (Educational only)
/// - No real-world problem hides a linear function behind a black box
/// - Included for: Textbook completeness, teaching phase kickback
module BernsteinVazirani =

    // ========================================================================
    // TYPES
    // ========================================================================

    /// Bernstein-Vazirani algorithm result
    type BernsteinVaziraniResult = {
        /// Recovered secret bitstring, indexed by qubit (bit i = s_i)
        RecoveredSecret: int[]

        /// Fraction of shots that produced the recovered bitstring
        /// (1.0 on an ideal simulator; lower on noisy backends)
        Confidence: float

        /// Number of qubits used
        NumQubits: int

        /// Number of shots performed
        Shots: int

        /// Backend used
        BackendName: string
    }

    /// Oracle function type - applies |x⟩ → (-1)^(s·x) |x⟩ (phase oracle form)
    type Oracle = QuantumState -> Result<QuantumState, QuantumError>

    type private BernsteinVaziraniIntent = {
        NumQubits: int
        Oracle: Oracle
    }

    [<RequireQualifiedAccess>]
    type private BernsteinVaziraniPlan =
        | ExecuteViaOpsAndOracle of preOps: QuantumOperation list * oracle: Oracle * postOps: QuantumOperation list

    // ========================================================================
    // HELPERS
    // ========================================================================

    let private gatesOnAllQubits (gate: int -> Gate) (numQubits: int) : QuantumOperation list =
        [ 0 .. numQubits - 1 ]
        |> List.map (fun i -> QuantumOperation.Gate (gate i))

    // ========================================================================
    // ORACLE CONSTRUCTORS
    // ========================================================================

    /// Create the phase oracle for secret s: |x⟩ → (-1)^(s·x) |x⟩.
    ///
    /// In phase-oracle form this is a Z gate on every qubit i where s_i = 1.
    /// The secret is indexed by qubit: secret.[i] ∈ {0, 1} corresponds to qubit i.
    let oracleForSecret (secret: int[]) (backend: IQuantumBackend) : Oracle =
        let ops =
            secret
            |> Array.toList
            |> List.mapi (fun i bit -> i, bit)
            |> List.filter (fun (_, bit) -> bit = 1)
            |> List.map (fun (i, _) -> QuantumOperation.Gate (Z i))
        fun state -> UnifiedBackend.applySequence backend ops state

    // ========================================================================
    // INTENT → PLAN → EXECUTION (ADR: intent-first algorithms)
    // ========================================================================

    let private plan (backend: IQuantumBackend) (intent: BernsteinVaziraniIntent) : Result<BernsteinVaziraniPlan, QuantumError> =
        match backend.NativeStateType with
        | QuantumStateType.Annealing ->
            Error (QuantumError.OperationError ("BernsteinVazirani", $"Backend '{backend.Name}' does not support Bernstein-Vazirani (native state type: {backend.NativeStateType})"))
        | _ ->
            let hadamards = gatesOnAllQubits H intent.NumQubits
            if hadamards |> List.forall backend.SupportsOperation then
                Ok (BernsteinVaziraniPlan.ExecuteViaOpsAndOracle (hadamards, intent.Oracle, hadamards))
            else
                Error (QuantumError.OperationError ("BernsteinVazirani", $"Backend '{backend.Name}' does not support required operations for Bernstein-Vazirani"))

    let private executePlan
        (backend: IQuantumBackend)
        (state: QuantumState)
        (plan: BernsteinVaziraniPlan)
        : Result<QuantumState, QuantumError> =

        match plan with
        | BernsteinVaziraniPlan.ExecuteViaOpsAndOracle (preOps, oracle, postOps) ->
            result {
                let! afterPre = UnifiedBackend.applySequence backend preOps state
                let! afterOracle = oracle afterPre
                return! UnifiedBackend.applySequence backend postOps afterOracle
            }

    // ========================================================================
    // ALGORITHM IMPLEMENTATION
    // ========================================================================

    /// Run Bernstein-Vazirani algorithm with custom oracle
    ///
    /// Algorithm steps:
    /// 1. Initialize |0⟩^⊗n state
    /// 2. Apply Hadamard to all qubits → equal superposition
    /// 3. Apply oracle (phase oracle form)
    /// 4. Apply Hadamard to all qubits → interference collapses to |s⟩
    /// 5. Measure all qubits → recovered secret
    ///
    /// Parameters:
    ///   oracle - Phase oracle implementing f(x) = s·x
    ///   numQubits - Number of qubits (secret length)
    ///   backend - Quantum backend to execute on
    ///   shots - Number of measurement shots
    ///
    /// Returns:
    ///   BernsteinVaziraniResult with the recovered secret
    let run
        (oracle: Oracle)
        (numQubits: int)
        (backend: IQuantumBackend)
        (shots: int)
        : Result<BernsteinVaziraniResult, QuantumError> =

        if numQubits < 1 then
            Error (QuantumError.ValidationError ("numQubits", "Bernstein-Vazirani requires at least 1 qubit"))
        elif numQubits > 20 then
            Error (QuantumError.ValidationError ("numQubits", "Bernstein-Vazirani with >20 qubits not practical on NISQ hardware"))
        elif shots < 1 then
            Error (QuantumError.ValidationError ("shots", "Bernstein-Vazirani requires at least 1 shot"))
        else
            result {
                let intent = { NumQubits = numQubits; Oracle = oracle }

                // Step 1: Initialize |0⟩^⊗n state
                let! initialState = backend.InitializeState numQubits

                // Step 2: Plan and execute
                let! bvPlan = plan backend intent
                let! finalState = executePlan backend initialState bvPlan

                // Step 3: Measure; the most frequent bitstring is the secret
                let measurements = UnifiedBackend.measureState finalState shots

                let secret, count =
                    measurements
                    |> Array.countBy id
                    |> Array.maxBy snd

                return {
                    RecoveredSecret = secret
                    Confidence = float count / float shots
                    NumQubits = numQubits
                    Shots = shots
                    BackendName = backend.Name
                }
            }

    // ========================================================================
    // CONVENIENCE FUNCTIONS
    // ========================================================================

    /// Run Bernstein-Vazirani for a known secret (builds the oracle internally).
    /// Useful for testing and demonstrations: the result should recover `secret`.
    let runWithSecret (secret: int[]) (backend: IQuantumBackend) (shots: int)
        : Result<BernsteinVaziraniResult, QuantumError> =
        if secret |> Array.exists (fun b -> b <> 0 && b <> 1) then
            Error (QuantumError.ValidationError ("secret", "Secret must contain only bits (0 or 1)"))
        else
            run (oracleForSecret secret backend) secret.Length backend shots

    // ========================================================================
    // PRETTY PRINTING
    // ========================================================================

    /// Format Bernstein-Vazirani result for display
    let formatResult (result: BernsteinVaziraniResult) : string =
        let secretStr = result.RecoveredSecret |> Array.map string |> String.concat ""
        sprintf "Bernstein-Vazirani Result:\n  Recovered Secret: %s\n  Confidence: %.2f%%\n  Qubits: %d\n  Shots: %d\n  Backend: %s"
            secretStr
            (result.Confidence * 100.0)
            result.NumQubits
            result.Shots
            result.BackendName
