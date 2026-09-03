namespace FSharp.Azure.Quantum.Algorithms

open System
open FSharp.Azure.Quantum
open FSharp.Azure.Quantum.CircuitBuilder
open FSharp.Azure.Quantum.Core
open FSharp.Azure.Quantum.Core.BackendAbstraction

/// Simon's Algorithm
///
/// Simon's algorithm finds the hidden period s of a two-to-one function
/// f: {0,1}^n → {0,1}^n satisfying f(x) = f(y) ⟺ y = x ⊕ s.
///
/// Part of the textbook algorithm canon:
/// - "Quantum Computing: An Applied Approach" (Hidary, 2021) - The Canon chapter
/// - Wikipedia: Simon's problem
///
/// Historically important: Simon's exponential separation inspired Shor's
/// period-finding algorithm.
///
/// Classical approach: Requires Ω(2^(n/2)) queries (birthday bound)
/// Quantum approach: Requires O(n) queries
///
/// Each quantum iteration samples a vector y with y·s = 0 (mod 2).
/// Classical post-processing (Gaussian elimination over GF(2)) recovers s
/// from n-1 linearly independent samples.
///
/// Register layout (2n qubits total):
///   qubits 0..n-1   - input register |x⟩
///   qubits n..2n-1  - output register |f(x)⟩
///
/// **Production Value**: ⭐☆☆☆☆ (Educational only)
/// - No real-world problem hides an XOR mask behind a black box
/// - Included for: Textbook completeness, the bridge from oracles to Shor
module Simon =

    // ========================================================================
    // TYPES
    // ========================================================================

    /// Simon's algorithm result
    type SimonResult = {
        /// Recovered secret, indexed by qubit (bit i = s_i).
        /// All zeros means f is one-to-one (s = 0).
        RecoveredSecret: int[]

        /// True when the function was determined to be one-to-one (s = 0)
        IsOneToOne: bool

        /// Distinct nonzero measurement vectors used as GF(2) equations y·s = 0
        Equations: int[][]

        /// Number of input qubits (total circuit uses 2n)
        NumInputQubits: int

        /// Number of shots performed
        Shots: int

        /// Backend used
        BackendName: string
    }

    /// Oracle function type - XOR oracle over 2n qubits: |x⟩|y⟩ → |x⟩|y ⊕ f(x)⟩
    type Oracle = QuantumState -> Result<QuantumState, QuantumError>

    type private SimonIntent = {
        NumInputQubits: int
        Oracle: Oracle
    }

    [<RequireQualifiedAccess>]
    type private SimonPlan =
        | ExecuteViaOpsAndOracle of preOps: QuantumOperation list * oracle: Oracle * postOps: QuantumOperation list

    // ========================================================================
    // HELPERS
    // ========================================================================

    let private hadamardsOnInputRegister (numInputQubits: int) : QuantumOperation list =
        [ 0 .. numInputQubits - 1 ]
        |> List.map (H >> QuantumOperation.Gate)

    // ========================================================================
    // GF(2) LINEAR ALGEBRA (classical post-processing)
    // ========================================================================

    /// Reduced row echelon form over GF(2). Rows are bitmasks over n variables.
    /// Returns list of (pivotBit, row) pairs; rank = list length.
    let private rowReduce (rows: int list) : (int * int) list =
        let highestSetBit (v: int) =
            let mutable bit = 0
            let mutable x = v
            while x > 1 do
                x <- x >>> 1
                bit <- bit + 1
            bit

        let mutable pivots : (int * int) list = []
        for row in rows do
            // Reduce the incoming row against existing pivots
            let mutable r = row
            for (pb, pr) in pivots do
                if (r >>> pb) &&& 1 = 1 then r <- r ^^^ pr
            if r <> 0 then
                let pb = highestSetBit r
                // Eliminate the new pivot bit from existing rows (full RREF)
                pivots <-
                    pivots
                    |> List.map (fun (opb, opr) ->
                        if (opr >>> pb) &&& 1 = 1 then (opb, opr ^^^ r) else (opb, opr))
                pivots <- (pb, r) :: pivots
        pivots

    /// Solve for the secret from equation masks (each satisfying mask·s = 0).
    /// rank = n   → s = 0 (one-to-one function)
    /// rank = n-1 → unique nonzero s spanning the null space
    /// rank < n-1 → underdetermined (not enough samples)
    let private solveSecret (numInputQubits: int) (equationMasks: int list) : Result<int, QuantumError> =
        let pivots = rowReduce equationMasks
        let rank = List.length pivots

        if rank = numInputQubits then
            Ok 0
        elif rank = numInputQubits - 1 then
            let pivotBits = pivots |> List.map fst |> Set.ofList
            let freeBit =
                [ 0 .. numInputQubits - 1 ]
                |> List.find (fun b -> not (Set.contains b pivotBits))
            // Null space vector: s_free = 1; for each pivot row, s_pivot = row's
            // coefficient at the free bit (so that row·s = 0).
            let secret =
                pivots
                |> List.fold (fun acc (pb, pr) ->
                    if (pr >>> freeBit) &&& 1 = 1 then acc ||| (1 <<< pb) else acc)
                    (1 <<< freeBit)
            Ok secret
        else
            Error (QuantumError.OperationError (
                "Simon",
                $"Only {rank} independent equations for {numInputQubits} unknowns - increase shots to determine the secret"))

    // ========================================================================
    // ORACLE CONSTRUCTORS
    // ========================================================================

    /// Create the XOR oracle for hidden period s: |x⟩|y⟩ → |x⟩|y ⊕ f(x)⟩ where
    /// f(x) = x                when x_j = 0
    /// f(x) = x ⊕ s            when x_j = 1   (j = lowest set bit of s)
    ///
    /// This is the standard two-to-one construction with f(x) = f(x ⊕ s).
    /// For s = 0 the oracle degenerates to f(x) = x (one-to-one).
    /// The secret is indexed by qubit: secret.[i] ∈ {0, 1} corresponds to qubit i.
    let xorOracleForSecret (secret: int[]) (backend: IQuantumBackend) : Oracle =
        let n = secret.Length
        let copyOps =
            [ 0 .. n - 1 ]
            |> List.map (fun i -> QuantumOperation.Gate (CNOT (i, n + i)))
        let maskOps =
            match secret |> Array.tryFindIndex ((=) 1) with
            | Some j ->
                [ 0 .. n - 1 ]
                |> List.filter (fun i -> secret.[i] = 1)
                |> List.map (fun i -> QuantumOperation.Gate (CNOT (j, n + i)))
            | None -> []
        fun state -> UnifiedBackend.applySequence backend (copyOps @ maskOps) state

    // ========================================================================
    // INTENT → PLAN → EXECUTION (ADR: intent-first algorithms)
    // ========================================================================

    let private plan (backend: IQuantumBackend) (intent: SimonIntent) : Result<SimonPlan, QuantumError> =
        match backend.NativeStateType with
        | QuantumStateType.Annealing ->
            Error (QuantumError.OperationError ("Simon", $"Backend '{backend.Name}' does not support Simon's algorithm (native state type: {backend.NativeStateType})"))
        | _ ->
            let hadamards = hadamardsOnInputRegister intent.NumInputQubits
            if hadamards |> List.forall backend.SupportsOperation then
                Ok (SimonPlan.ExecuteViaOpsAndOracle (hadamards, intent.Oracle, hadamards))
            else
                Error (QuantumError.OperationError ("Simon", $"Backend '{backend.Name}' does not support required operations for Simon's algorithm"))

    let private executePlan
        (backend: IQuantumBackend)
        (state: QuantumState)
        (plan: SimonPlan)
        : Result<QuantumState, QuantumError> =

        match plan with
        | SimonPlan.ExecuteViaOpsAndOracle (preOps, oracle, postOps) ->
            result {
                let! afterPre = UnifiedBackend.applySequence backend preOps state
                let! afterOracle = oracle afterPre
                return! UnifiedBackend.applySequence backend postOps afterOracle
            }

    // ========================================================================
    // ALGORITHM IMPLEMENTATION
    // ========================================================================

    /// Run Simon's algorithm with custom XOR oracle
    ///
    /// Algorithm steps:
    /// 1. Initialize |0⟩^⊗2n state (input register + output register)
    /// 2. Apply Hadamard to input register → equal superposition
    /// 3. Apply XOR oracle: |x⟩|0⟩ → |x⟩|f(x)⟩
    /// 4. Apply Hadamard to input register
    /// 5. Measure input register → vector y with y·s = 0 (mod 2)
    /// 6. Gaussian elimination over GF(2) on collected vectors recovers s
    ///
    /// Parameters:
    ///   oracle - XOR oracle over 2n qubits implementing |x⟩|y⟩ → |x⟩|y ⊕ f(x)⟩
    ///   numInputQubits - Size n of the input register (circuit uses 2n qubits)
    ///   backend - Quantum backend to execute on
    ///   shots - Number of measurement shots (use ≳ 10·n to determine s reliably)
    ///
    /// Returns:
    ///   SimonResult with the recovered secret (all zeros ⟺ one-to-one)
    let run
        (oracle: Oracle)
        (numInputQubits: int)
        (backend: IQuantumBackend)
        (shots: int)
        : Result<SimonResult, QuantumError> =

        if numInputQubits < 1 then
            Error (QuantumError.ValidationError ("numInputQubits", "Simon's algorithm requires at least 1 input qubit"))
        elif numInputQubits > 10 then
            Error (QuantumError.ValidationError ("numInputQubits", "Simon's algorithm uses 2n qubits; >10 input qubits not practical on NISQ hardware"))
        elif shots < 1 then
            Error (QuantumError.ValidationError ("shots", "Simon's algorithm requires at least 1 shot"))
        else
            result {
                let intent = { NumInputQubits = numInputQubits; Oracle = oracle }

                // Step 1: Initialize |0⟩^⊗2n state
                let! initialState = backend.InitializeState (2 * numInputQubits)

                // Step 2: Plan and execute
                let! simonPlan = plan backend intent
                let! finalState = executePlan backend initialState simonPlan

                // Step 3: Measure input register (qubits 0..n-1) per shot
                let measurements = UnifiedBackend.measureState finalState shots

                let inputBits =
                    measurements
                    |> Array.map (fun bits -> Array.sub bits 0 numInputQubits)

                let equationMasks =
                    inputBits
                    |> Array.map (Array.mapi (fun i b -> b <<< i) >> Array.sum)
                    |> Array.filter ((<>) 0)
                    |> Array.distinct
                    |> Array.toList

                // Step 4: Classical post-processing over GF(2)
                let! secretMask = solveSecret numInputQubits equationMasks

                let secretBits =
                    Array.init numInputQubits (fun i -> (secretMask >>> i) &&& 1)

                let equations =
                    equationMasks
                    |> List.map (fun m -> Array.init numInputQubits (fun i -> (m >>> i) &&& 1))
                    |> List.toArray

                return {
                    RecoveredSecret = secretBits
                    IsOneToOne = (secretMask = 0)
                    Equations = equations
                    NumInputQubits = numInputQubits
                    Shots = shots
                    BackendName = backend.Name
                }
            }

    // ========================================================================
    // CONVENIENCE FUNCTIONS
    // ========================================================================

    /// Run Simon's algorithm for a known secret (builds the XOR oracle internally).
    /// Useful for testing and demonstrations: the result should recover `secret`.
    let runWithSecret (secret: int[]) (backend: IQuantumBackend) (shots: int)
        : Result<SimonResult, QuantumError> =
        if secret |> Array.exists (fun b -> b <> 0 && b <> 1) then
            Error (QuantumError.ValidationError ("secret", "Secret must contain only bits (0 or 1)"))
        else
            run (xorOracleForSecret secret backend) secret.Length backend shots

    // ========================================================================
    // PRETTY PRINTING
    // ========================================================================

    /// Format Simon result for display
    let formatResult (result: SimonResult) : string =
        let secretStr = result.RecoveredSecret |> Array.map string |> String.concat ""
        let kindStr = if result.IsOneToOne then "One-to-one (s = 0)" else "Two-to-one"
        sprintf "Simon Result:\n  Recovered Secret: %s\n  Function: %s\n  Distinct Equations: %d\n  Input Qubits: %d (circuit: %d)\n  Shots: %d\n  Backend: %s"
            secretStr
            kindStr
            result.Equations.Length
            result.NumInputQubits
            (2 * result.NumInputQubits)
            result.Shots
            result.BackendName
