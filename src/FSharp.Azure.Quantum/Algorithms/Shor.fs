namespace FSharp.Azure.Quantum.Algorithms

open System
open System.Numerics
open FSharp.Azure.Quantum
open FSharp.Azure.Quantum.Core
open FSharp.Azure.Quantum.Core.BackendAbstraction

/// Shor's Algorithm - Unified Backend Implementation
///
/// State-based implementation using IQuantumBackend.
/// Optimized for educational purposes and local simulation.
///
/// For cloud hardware execution, use ShorsBackendAdapter instead.
///
/// Algorithm Overview:
/// Shor's algorithm factors a composite number N by finding the period r of modular exponentiation:
///   a^r ≡ 1 (mod N)
///
/// Once the period r is found (using Quantum Phase Estimation), factors can be extracted classically:
///   p = gcd(a^(r/2) + 1, N)
///   q = gcd(a^(r/2) - 1, N)
///
/// Steps:
/// 1. Classical pre-checks (even number, prime test, etc.)
/// 2. Choose random base a coprime to N
/// 3. Find period r using QPE (quantum subroutine)
/// 4. Extract factors from period (classical post-processing)
///
/// Limitations:
/// - This implementation focuses on educational value (N ≤ 100)
/// - For larger numbers, use ShorsBackendAdapter with cloud backends
/// - Genuine quantum path bounded by StateVector.practicalCircuitQubits (wall-clock, not capacity)
///
/// Example:
/// ```fsharp
/// open FSharp.Azure.Quantum.Algorithms.Shor
/// open FSharp.Azure.Quantum.Backends.LocalBackend
///
/// let backend = LocalBackend() :> IQuantumBackend
///
/// // Factor 15 (classic example)
/// match factor15 backend with
/// | Ok result ->
///     match result.Factors with
///     | Some (p, q) -> printfn "15 = %d × %d" p q
///     | None -> printfn "Could not find factors"
/// | Error err -> printfn "Error: %A" err
/// ```
module Shor =

    open FSharp.Azure.Quantum.Algorithms.ShorsTypes
    open FSharp.Azure.Quantum.Algorithms.QPE

    /// Qubits the genuine quantum path may spend on one circuit.
    ///
    /// Shor's modular-exponentiation circuit is counting + 2·registerBits + 4 wide,
    /// so this decides which N are factored quantumly rather than through the
    /// classically-assisted fallback.
    ///
    /// Budgeted against how wide a circuit FINISHES, not how wide a state FITS. The
    /// distinction matters here more than anywhere: the circuit applies hundreds of
    /// gates, and each qubit doubles the cost of every one of them. Budgeting against
    /// memory capacity instead pulls N=33, 35, 51 off the fast classical fallback and
    /// onto 26-qubit circuits that take hours — the state fits comfortably; the run
    /// does not finish. Raise FSAQ_MAX_CIRCUIT_QUBITS to opt into the wider circuits.
    let private simulatorQubitBudget =
        FSharp.Azure.Quantum.LocalSimulator.StateVector.practicalCircuitQubits

    /// Bits needed to hold a residue mod N — the width of each of the two work registers.
    ///
    /// Delegated rather than recomputed: the circuit lowering derives its register width the
    /// same way, and if the two ever disagreed this module would budget for a circuit of one
    /// size and then build one of another.
    let private registerBitsFor (n: int) : int =
        ModularExponentiationCircuit.registerBitsFor n

    /// Counting qubits left for QPE once modular exponentiation has taken its workspace.
    /// The circuit is counting + 2·registerBits + 4, so this is what the budget leaves over.
    /// It goes negative for large N, which is why callers compare it against registerBits
    /// rather than against zero.
    let private maxCountingQubitsFor (n: int) : int =
        simulatorQubitBudget - 2 * registerBitsFor n - 4

    /// Reported when the modular-exponentiation circuit for N does not fit the budget.
    ///
    /// This is the error that used to be a classical fallback. It names the shortfall and
    /// the one knob that moves it, and it deliberately does not offer a classical route,
    /// because there no longer is one.
    let private unfittableCircuitError (n: int) : QuantumError =
        let registerBits = registerBitsFor n

        QuantumError.ValidationError(
            "n",
            $"N={n} needs {registerBits} counting qubits to resolve the period, but only "
            + $"{max 0 (maxCountingQubitsFor n)} fit the {simulatorQubitBudget}-qubit circuit budget "
            + $"(counting + 2·{registerBits} + 4). Raise FSAQ_MAX_CIRCUIT_QUBITS if the machine "
            + "has the memory and the time, or factor a smaller N."
        )

    // ========================================================================
    // CLASSICAL NUMBER THEORY HELPERS
    // ========================================================================

    /// <summary>
    /// Compute greatest common divisor using Euclidean algorithm.
    /// </summary>
    /// <param name="a">First number</param>
    /// <param name="b">Second number</param>
    /// <returns>Greatest common divisor of a and b</returns>
    /// <example>
    /// <code>
    /// gcd 15 10 = 5
    /// gcd 21 14 = 7
    /// </code>
    /// </example>
    [<TailCall>]
    let rec private gcd a b = if b = 0 then a else gcd b (a % b)

    /// <summary>
    /// Modular exponentiation: (base^exp) mod m.
    /// Uses BigInteger for handling large numbers without overflow.
    /// </summary>
    /// <param name="baseNum">Base number</param>
    /// <param name="exp">Exponent</param>
    /// <param name="modulus">Modulus</param>
    /// <returns>Result of (base^exp) mod m</returns>
    /// <example>
    /// <code>
    /// modPow 2 10 1000 = 24  // 2^10 = 1024, 1024 mod 1000 = 24
    /// modPow 7 4 15 = 1       // 7^4 = 2401, 2401 mod 15 = 1
    /// </code>
    /// </example>
    let private modPow (baseNum: int) (exp: int) (modulus: int) : int =
        int (bigint.ModPow(bigint baseNum, bigint exp, bigint modulus))

    /// <summary>
    /// Check if number is prime using trial division.
    /// </summary>
    /// <param name="n">Number to test</param>
    /// <returns>True if n is prime, false otherwise</returns>
    /// <example>
    /// <code>
    /// isPrime 2 = true
    /// isPrime 15 = false
    /// isPrime 17 = true
    /// </code>
    /// </example>
    let private isPrime n =
        if n < 2 then
            false
        elif n = 2 then
            true
        elif n % 2 = 0 then
            false
        else
            let limit = int (sqrt (float n))
            [ 2..limit ] |> List.forall (fun i -> n % i <> 0)

    /// <summary>
    /// Check if number is even.
    /// </summary>
    let private isEven n = n % 2 = 0

    /// <summary>
    /// Convert phase estimate to period using continued fraction approximation.
    /// Given phase φ = s/r (reduced fraction), extracts period r.
    /// </summary>
    /// <param name="phi">Phase estimate from QPE (in range [0, 1))</param>
    /// <param name="maxDenom">Maximum denominator to search (typically N)</param>
    /// <returns>Best rational approximation (numerator, denominator) or None</returns>
    /// <example>
    /// <code>
    /// continuedFractionConvergent 0.125 15 = Some (1, 8)  // φ = 1/8
    /// continuedFractionConvergent 0.25 15 = Some (1, 4)   // φ = 1/4
    /// continuedFractionConvergent 0.5 15 = Some (1, 2)    // φ = 1/2
    /// </code>
    /// </example>
    let private continuedFractionConvergent (phi: float) (maxDenom: int) : (int * int) option =
        // Simple continued fraction approximation
        // Find s/r such that |phi - s/r| is minimized
        [ 1..maxDenom ]
        |> List.map (fun denom ->
            let num = int (round (phi * float denom))
            let error = abs (phi - float num / float denom)
            (num, denom, error))
        |> List.minBy (fun (_, _, error) -> error)
        |> fun (num, denom, _) -> if denom > 0 then Some(num, denom) else None

    // ========================================================================
    // FACTOR EXTRACTION FROM PERIOD (CLASSICAL)
    // ========================================================================

    /// <summary>
    /// Extract factors from period r.
    /// Given a^r ≡ 1 (mod N), compute gcd(a^(r/2) ± 1, N).
    /// </summary>
    /// <param name="a">Base number (coprime to N)</param>
    /// <param name="r">Period (must be even)</param>
    /// <param name="n">Number to factor</param>
    /// <returns>Factors (p, q) such that N = p × q, or None if extraction fails</returns>
    /// <remarks>
    /// This is the classical post-processing step of Shor's algorithm.
    /// Requirements:
    /// - r must be even
    /// - a^(r/2) must not be ≡ -1 (mod N)
    /// </remarks>
    /// <example>
    /// <code>
    /// // For N=15, a=7, r=4:
    /// // 7^(4/2) = 49 ≡ 4 (mod 15)
    /// // gcd(4+1, 15) = gcd(5, 15) = 5
    /// // gcd(4-1, 15) = gcd(3, 15) = 3
    /// extractFactorsFromPeriod 7 4 15 = Some (3, 5)
    /// </code>
    /// </example>
    let private extractFactorsFromPeriod (a: int) (r: int) (n: int) : (int * int) option =
        // Check if r is even
        if not (isEven r) then
            None
        else
            // Compute a^(r/2) mod N
            let halfR = r / 2
            let aToHalfR = modPow a halfR n

            // Check if a^(r/2) ≢ -1 (mod N)
            if aToHalfR = n - 1 then
                None
            else
                // Compute gcd(a^(r/2) + 1, N) and gcd(a^(r/2) - 1, N)
                let factor1 = gcd (aToHalfR + 1) n
                let factor2 = gcd (abs (aToHalfR - 1)) n

                // Check if we found non-trivial factors
                if factor1 > 1 && factor1 < n then
                    Some(factor1, n / factor1)
                elif factor2 > 1 && factor2 < n then
                    Some(factor2, n / factor2)
                else
                    None

    // ========================================================================
    // QUANTUM MODULAR ARITHMETIC (STATE-BASED)
    // ========================================================================

    /// <summary>
    /// Controlled modular multiplication: C-U|y⟩ = |ay mod N⟩ if control=1, else |y⟩.
    /// Implements modular multiplication as a quantum operation on state.
    /// </summary>
    /// <param name="controlQubit">Control qubit index</param>
    /// <param name="targetQubits">Target qubit indices (encoding y in binary)</param>
    /// <param name="a">Multiplication factor</param>
    /// <param name="n">Modulus</param>
    /// <param name="backend">Quantum backend</param>
    /// <param name="state">Current quantum state</param>
    /// <returns>New quantum state after controlled modular multiplication, or error</returns>
    /// <remarks>
    /// This is a SIMPLIFIED implementation for educational purposes.
    /// Full implementation requires quantum adder circuits and modular reduction.
    /// Currently supports only small N (N ≤ 100) using lookup tables.
    /// </remarks>
    /// Controlled modular multiplication using Beauregard (2003) quantum arithmetic.
    ///
    /// Performs: C|y⟩ → C|ay mod N⟩ (in-place, when control=|1⟩)
    ///
    /// Allocates temp qubits above the highest existing qubit index.
    /// The caller must provide a state with enough qubits (2n + 5 where n = |targetQubits|).
    ///
    /// Internal visibility for testing; not part of the public API.
    let internal controlledModularMultiplication
        (controlQubit: int)
        (targetQubits: int list)
        (a: int)
        (n: int)
        (backend: IQuantumBackend)
        (state: QuantumState)
        : Result<QuantumState, QuantumError> =

        // Wire to the Beauregard (2003) modular multiplication circuit
        // from QuantumArithmetic. This performs:
        //   C|y⟩ → C|ay mod N⟩  (in-place, when control=|1⟩)
        //
        // Qubit layout (for n = |targetQubits|):
        //   controlQubit:  1 qubit
        //   targetQubits:  n qubits (register to multiply in-place)
        //   tempQubits:    n qubits (workspace, must start as |0⟩)
        //   Internal ancilla chain (allocated by Arithmetic module):
        //     - andAncilla (doublyControlledAddConstantModN): 1 qubit
        //     - overflow (controlledAddConstantModN → Beauregard): 1 qubit
        //     - flag (controlledAddConstantModN → Beauregard): 1 qubit
        //     - dcAdd ancilla (doublyControlledAddConstant inside Beauregard): 1 qubit
        //   Total: 2n + 5 qubits
        //
        // For educational Shor's (N ≤ 100): n ≤ 7 bits, so total ≤ 19 qubits.
        //
        // The caller must provide a state with enough qubits to accommodate
        // targetQubits + tempQubits + internal ancilla chain.

        let numBits = List.length targetQubits

        if numBits = 0 then
            Error(
                QuantumError.ValidationError(
                    "targetQubits",
                    "controlledModularMultiplication requires at least one target qubit"
                )
            )
        else

            // Allocate temp qubits above all existing qubits in use
            let maxExistingQubit = max controlQubit (List.max targetQubits)
            let tempQubits = [ maxExistingQubit + 1 .. maxExistingQubit + numBits ]

            // The full ancilla chain inside the Arithmetic module:
            //   doublyControlledAddConstantModN allocates andAncilla = max + 1
            //   controlledAddConstantModN allocates overflow = andAncilla + 1, flag = andAncilla + 2
            //   doublyControlledAddConstant (inside Beauregard step 4) allocates dcAncilla = flag + 1
            // Total required: max(tempQubits) + 4 + 1 = maxExistingQubit + numBits + 5
            let totalQubitsRequired = maxExistingQubit + numBits + 5

            let currentQubits =
                match state with
                | QuantumState.StateVector sv -> FSharp.Azure.Quantum.LocalSimulator.StateVector.numQubits sv
                | _ -> totalQubitsRequired // Non-local backends manage their own qubits

            if currentQubits < totalQubitsRequired then
                Error(
                    QuantumError.ValidationError(
                        "state",
                        $"State has {currentQubits} qubits but controlledModularMultiplication requires at least {totalQubitsRequired} (register={numBits}, temp={numBits}, ancilla=3, control=1). Initialize state with enough qubits."
                    )
                )
            else
                result {
                    let! arithmeticResult =
                        Arithmetic.controlledMultiplyConstantModNInPlace
                            controlQubit
                            targetQubits
                            tempQubits
                            a
                            n
                            state
                            backend

                    return arithmeticResult.State
                }

    /// <summary>
    /// Controlled modular exponentiation: C-U^k|x⟩ = |a^k x mod N⟩ if control=1, else |x⟩.
    /// This is the core quantum operation for Shor's period-finding.
    /// </summary>
    /// <param name="controlQubit">Control qubit index</param>
    /// <param name="targetQubits">Target qubit indices (encoding x in binary)</param>
    /// <param name="a">Base number</param>
    /// <param name="k">Exponent (power of U)</param>
    /// <param name="n">Modulus</param>
    /// <param name="backend">Quantum backend</param>
    /// <param name="state">Current quantum state</param>
    /// <returns>New quantum state after controlled modular exponentiation, or error</returns>
    /// Controlled modular exponentiation: C-U^k|x⟩ = |a^k x mod N⟩ if control=1.
    /// Internal visibility for testing; not part of the public API.
    let internal controlledModularExponentiation
        (controlQubit: int)
        (targetQubits: int list)
        (a: int)
        (k: int)
        (n: int)
        (backend: IQuantumBackend)
        (state: QuantumState)
        : Result<QuantumState, QuantumError> =

        // Compute a^k mod n classically
        let aToK = modPow a k n

        // Apply controlled modular multiplication by a^k
        controlledModularMultiplication controlQubit targetQubits aToK n backend state

    // ========================================================================
    // FULL QUANTUM MODULAR-EXPONENTIATION QPE
    // ========================================================================

    /// Result of modular-exponentiation phase estimation.
    type ModExpPhaseResult =
        {
            /// Estimated phase φ = s/r where a^r ≡ 1 (mod N).
            EstimatedPhase: float
            /// Raw measurement outcome from counting register.
            MeasurementOutcome: int
            /// Number of counting (precision) qubits used.
            CountingQubits: int
            /// Total qubits allocated (counting + 2n + 4).
            TotalQubits: int
            /// Number of controlled modular multiplications applied.
            ModularMultiplications: int
        }

    /// Estimate the phase of modular exponentiation U_a: |x⟩ → |ax mod N⟩
    /// using full Beauregard (2003) quantum arithmetic circuits.
    ///
    /// This is the core quantum subroutine of Shor's algorithm, performing QPE
    /// with the actual multi-qubit controlled modular multiplication circuit
    /// (not the classical-assisted demonstration used by `findPeriod`).
    ///
    /// Algorithm:
    /// 1. Allocate counting register + target register + workspace qubits
    /// 2. Apply H to all counting qubits (superposition)
    /// 3. Prepare target register in |1⟩ (eigenvector of modular multiplication)
    /// 4. For each counting qubit j: apply controlled U_a^(2^j) where U_a|x⟩ = |a^(2^j) · x mod N⟩
    /// 5. Apply inverse QFT to counting register
    /// 6. Measure counting register and extract phase
    ///
    /// Qubit layout (n = ceil(log₂(N))):
    ///   [0..c-1]         = counting qubits (c = countingQubits)
    ///   [c..c+n-1]       = target register (n-bit number register)
    ///   [c+n..c+2n+3]    = workspace (temp + ancilla, allocated by controlledModularMultiplication)
    ///   Total: c + 2n + 4 qubits
    ///
    /// Constraints:
    /// - N must be > 1 and composite (not checked here; caller's responsibility)
    /// - a must be coprime to N and 1 < a < N
    /// - Total qubits must not exceed the local simulator's memory-derived capacity
    let estimateModExpPhase
        (baseNum: int)
        (modulus: int)
        (countingQubits: int)
        (backend: IQuantumBackend)
        : Result<ModExpPhaseResult, QuantumError> =

        // Validation
        let n = int (Math.Ceiling(Math.Log(float modulus, 2.0)))

        if modulus < 2 then
            Error(QuantumError.ValidationError("modulus", "must be ≥ 2"))
        elif baseNum < 2 || baseNum >= modulus then
            Error(QuantumError.ValidationError("baseNum", $"must be in range [2, {modulus - 1}]"))
        elif gcd baseNum modulus <> 1 then
            Error(QuantumError.ValidationError("baseNum", $"{baseNum} is not coprime to {modulus}"))
        elif countingQubits <= 0 then
            Error(QuantumError.ValidationError("countingQubits", "must be positive"))
        elif countingQubits > 16 then
            Error(QuantumError.ValidationError("countingQubits", "must be ≤ 16 for local simulation"))
        else
            // Qubit allocation:
            //   counting:  [0 .. countingQubits-1]
            //   target:    [countingQubits .. countingQubits+n-1]
            //   workspace: allocated dynamically by controlledModularMultiplication
            // Total: countingQubits + 2n + 4
            let totalQubits = countingQubits + 2 * n + 4

            if totalQubits > simulatorQubitBudget then
                Error(
                    QuantumError.ValidationError(
                        "totalQubits",
                        $"Requires {totalQubits} qubits (counting={countingQubits}, register={n}, workspace={n + 4}) "
                        + $"but the local simulator holds at most {simulatorQubitBudget}. "
                        + $"Reduce countingQubits to ≤ {simulatorQubitBudget - 2 * n - 4}."
                    )
                )
            else
                result {
                    // Steps 1-5 — Hadamards on the counting register, target prepared in |1⟩,
                    // the controlled modular multiplications, and the inverse QFT — come from
                    // the shared lowering rather than being assembled here.
                    //
                    // They used to be built inline, which meant this circuit had two
                    // constructions: this one and ModularExponentiationCircuit's. They agreed,
                    // but agreeing is not the same as being one thing, and every other
                    // duplicate in this area has eventually drifted. `applySwaps = false`
                    // leaves the counting register bit-reversed, undone classically below.
                    let! ops =
                        ModularExponentiationCircuit.buildModExpQpe baseNum modulus countingQubits false

                    let! initialState = backend.InitializeState totalQubits
                    let! stateAfterQft = UnifiedBackend.applySequence backend ops initialState

                    // Step 6: Measure counting register and extract phase.
                    // We consume exactly one shot (measurements.[0]); QPE is inherently probabilistic
                    // and findPeriodQuantum already retries on failure, so sampling 1000 full states
                    // and discarding 999 was pure waste.
                    let measurements = UnifiedBackend.measureState stateAfterQft 1

                    // Extract counting register bits (qubits 0..c-1)
                    // Inverse QFT without bit-reversal swaps produces bit-reversed output;
                    // reverse classically to get canonical order.
                    let measuredCountingBits =
                        measurements.[0] |> Array.take countingQubits |> Array.rev // undo bit-reversal (no swaps applied)

                    let measurementOutcome =
                        measuredCountingBits
                        |> Array.indexed
                        |> Array.fold (fun acc (i, bit) -> acc + (bit <<< i)) 0

                    let estimatedPhase = float measurementOutcome / float (1 <<< countingQubits)

                    return
                        {
                            EstimatedPhase = estimatedPhase
                            MeasurementOutcome = measurementOutcome
                            CountingQubits = countingQubits
                            TotalQubits = totalQubits
                            ModularMultiplications = countingQubits
                        }
                }
    // ========================================================================
    // INTENT -> PLAN -> EXECUTION (ADR: intent-first algorithms)
    // ========================================================================
    //
    // Period finding states WHAT it wants — the phase of U_a on a counting register of
    // a given width — and the backend decides HOW. That indirection is what lets a
    // non-simulator backend (the topological one, say) run Shor: it can claim the whole
    // modular-exponentiation QPE as one semantic intent instead of receiving a gate list.
    //
    // Both plan cases are quantum. An earlier version of this pipeline had exactly one
    // case, ExecuteClassicalWithQpeDemo, which found the period by trial division and
    // then ran a QPE circuit whose phase was built from that answer. The pattern was
    // sound; what it was planning was not.

    /// Canonical intent for Shor period finding: estimate the phase of U_a |x⟩ = |ax mod N⟩.
    type ShorPeriodFindingIntent =
        {
            Base: int
            Modulus: int
            CountingQubits: int
        }

    [<RequireQualifiedAccess>]
    type ShorPeriodFindingPlan =
        /// The backend executes modular-exponentiation QPE as one semantic operation.
        /// Offered to any backend whose SupportsOperation accepts the intent.
        | ExecuteNatively of intent: QpeIntent

        /// The backend receives the Beauregard (2003) modular-arithmetic circuit gate by
        /// gate, via estimateModExpPhase. Every gate-based backend can run this.
        | ExecuteViaModExpCircuit of baseNum: int * modulus: int * countingQubits: int

    /// Choose how period finding will run on this backend.
    ///
    /// Backends that advertise native modular-exponentiation QPE get the intent whole;
    /// everyone else gets the explicit circuit. Note that a backend must answer
    /// SupportsOperation honestly for this to work — LocalBackend and TopologicalBackend
    /// both used to return true for every QPE intent while their ApplyOperation rejected
    /// this one, which is why the choice is made against the fully-built intent.
    let planPeriodFinding
        (backend: IQuantumBackend)
        (intent: ShorPeriodFindingIntent)
        : Result<ShorPeriodFindingPlan, QuantumError> =

        let a = intent.Base
        let n = intent.Modulus
        let countingQubits = intent.CountingQubits

        if a <= 1 || a >= n then
            Error(QuantumError.ValidationError("Base", $"must be in range (1, {n})"))
        elif gcd a n <> 1 then
            Error(QuantumError.ValidationError("Base", $"{a} is not coprime to {n} (gcd={gcd a n})"))
        elif countingQubits <= 0 then
            Error(QuantumError.ValidationError("CountingQubits", "must be positive"))
        else
            let qpeIntent: QpeIntent =
                {
                    CountingQubits = countingQubits
                    TargetQubits = registerBitsFor n
                    Unitary = QpeUnitary.ModularExponentiation(a, n)
                    PrepareTargetOne = true
                    ApplySwaps = false
                }

            if backend.SupportsOperation(QuantumOperation.Algorithm(AlgorithmOperation.QPE qpeIntent)) then
                Ok(ShorPeriodFindingPlan.ExecuteNatively qpeIntent)
            else
                Ok(ShorPeriodFindingPlan.ExecuteViaModExpCircuit(a, n, countingQubits))

    /// Run a period-finding plan and return the estimated phase.
    let private executePeriodFindingPlan
        (backend: IQuantumBackend)
        (plan: ShorPeriodFindingPlan)
        : Result<ModExpPhaseResult, QuantumError> =

        match plan with
        | ShorPeriodFindingPlan.ExecuteViaModExpCircuit(a, n, countingQubits) ->
            estimateModExpPhase a n countingQubits backend

        | ShorPeriodFindingPlan.ExecuteNatively qpeIntent ->
            result {
                let totalQubits = qpeIntent.CountingQubits + qpeIntent.TargetQubits
                let! initialState = backend.InitializeState totalQubits

                let! preparedState =
                    backend.ApplyOperation (QuantumOperation.Algorithm(AlgorithmOperation.QPE qpeIntent)) initialState

                let measurements = UnifiedBackend.measureState preparedState 1

                let countingBits = measurements.[0] |> Array.take qpeIntent.CountingQubits

                // ApplySwaps is false above, so the inverse QFT leaves the counting
                // register bit-reversed and it is undone here rather than in gates.
                let canonicalBits = Array.rev countingBits

                let measurementOutcome =
                    canonicalBits
                    |> Array.indexed
                    |> Array.fold (fun acc (i, bit) -> acc + (bit <<< i)) 0

                return
                    {
                        EstimatedPhase = float measurementOutcome / float (1 <<< qpeIntent.CountingQubits)
                        MeasurementOutcome = measurementOutcome
                        CountingQubits = qpeIntent.CountingQubits
                        TotalQubits = totalQubits
                        ModularMultiplications = qpeIntent.CountingQubits
                    }
            }

    // ========================================================================
    // PERIOD FINDING USING QPE
    // ========================================================================

    /// <summary>
    /// Find period r such that a^r ≡ 1 (mod N) using Quantum Phase Estimation.
    /// This is the quantum subroutine of Shor's algorithm.
    /// </summary>
    /// <param name="a">Base number (must be coprime to N)</param>
    /// <param name="n">Modulus (number to factor)</param>
    /// <param name="precisionQubits">Number of counting qubits for QPE precision</param>
    /// <param name="backend">Quantum backend</param>
    /// <returns>Period-finding result or error</returns>
    /// <remarks>
    /// Uses QPE to estimate phase φ of eigenvalue e^(2πiφ) where U^r = I.
    /// The period r is extracted from φ = s/r using continued fraction approximation.
    ///
    /// The period is found by the quantum routine or not at all. There is no classical
    /// period-finding path: when the modular-exponentiation circuit does not fit the
    /// simulator's qubit budget this errors rather than computing the answer classically
    /// and presenting it as a quantum result.
    /// </remarks>
    /// Genuine quantum period finding: plan modular-exponentiation QPE for this backend,
    /// then recover the period from the measured phase φ ≈ s/r via its continued-fraction
    /// convergent. Each QPE shot is probabilistic, so this retries until it obtains a convergent
    /// r satisfying a^r ≡ 1 (mod N). Counting qubits are clamped so the circuit fits the
    /// simulator's qubit budget.
    ///
    /// The plan is made once and reused across attempts: which backend path runs cannot
    /// change between shots, only the measurement outcome does.
    let findPeriodQuantum
        (a: int)
        (n: int)
        (precisionQubits: int)
        (backend: IQuantumBackend)
        : Result<PeriodFindingResult, QuantumError> =

        let maxCounting = maxCountingQubitsFor n

        if maxCounting < registerBitsFor n then
            // With fewer counting qubits than register bits the phase grid 2^c < N, so the
            // continued-fraction step can only ever return the dyadic denominator 2^c — the
            // retries below would burn maxAttempts full-width simulations and then fail
            // anyway (for any period that is not a power of two ≤ 2^c). Fail fast instead.
            Error(unfittableCircuitError n)
        else

            let countingQubits = max 1 (min precisionQubits maxCounting)
            let maxAttempts = 16

            let periodFindingPlan =
                planPeriodFinding
                    backend
                    {
                        Base = a
                        Modulus = n
                        CountingQubits = countingQubits
                    }

            let rec attempt tries =
                match periodFindingPlan |> Result.bind (executePeriodFindingPlan backend) with
                | Error e -> Error e
                | Ok phaseResult ->
                    match continuedFractionConvergent phaseResult.EstimatedPhase n with
                    | Some(_, r) when r > 0 && r < n && modPow a r n = 1 ->
                        Ok
                            {
                                Period = r
                                Base = a
                                PhaseEstimate = phaseResult.EstimatedPhase
                                Attempts = tries
                            }
                    | _ when tries < maxAttempts -> attempt (tries + 1)
                    | _ ->
                        Error(
                            QuantumError.OperationError(
                                "Period finding",
                                $"QPE did not yield a valid period for a={a}, N={n} within {maxAttempts} attempts"
                            )
                        )

            attempt 1

    /// Find the period of a^x mod N by QPE on modular exponentiation.
    ///
    /// Identical to `findPeriodQuantum` — it exists as the conventional name. Where this
    /// once degraded to a classical period search for N too large for the simulator, it
    /// now reports that qubit-budget error unchanged. A period this library returns was
    /// measured, not computed.
    let findPeriod
        (a: int)
        (n: int)
        (precisionQubits: int)
        (backend: IQuantumBackend)
        : Result<PeriodFindingResult, QuantumError> =

        findPeriodQuantum a n precisionQubits backend

    // ========================================================================
    // INTENT -> PLAN -> EXECUTION (ADR: Shor factoring)
    // ========================================================================

    /// Canonical, algorithm-level intent for Shor factorization.
    type ShorExecutionIntent =
        {
            Config: ShorsConfig
            Exactness: QPE.Exactness
        }

    [<RequireQualifiedAccess>]
    type ShorPlan =
        /// The result is settled without period finding: N is even, prime or a perfect
        /// power, or the drawn base shares a factor with N. These are steps of Shor's
        /// algorithm, not substitutes for its quantum subroutine.
        | ReturnResult of ShorsResult

        /// Execute the period-finding + factor-extraction path. Period finding is QPE on
        /// modular exponentiation, per attempt; there is no other strategy.
        | ExecuteQuantum of
            baseNum: int *
            modulus: int *
            precisionQubits: int *
            exactness: QPE.Exactness *
            maxAttempts: int *
            config: ShorsConfig

    let private mkResult
        (n: int)
        (factors: (int * int) option)
        (period: PeriodFindingResult option)
        (success: bool)
        (message: string)
        (config: ShorsConfig)
        : ShorsResult =
        {
            Number = n
            Factors = factors
            PeriodResult = period
            Success = success
            Message = message
            Config = config
        }

    /// Choose the base a for Shor's algorithm.
    ///
    /// A caller-provided base is honored when it lies in (1, N) and is coprime to N.
    /// Otherwise a ∈ [2, N-2] is drawn uniformly at random (Random.Shared is fine here —
    /// this is algorithmic randomness, not cryptography). The draw is deliberately NOT
    /// filtered for coprimality: gcd(a, N) > 1 already reveals a non-trivial factor,
    /// and callers turn that case into an immediate success.
    let private chooseRandomBase (n: int) (provided: int option) : int =
        match provided with
        | Some a when a > 1 && a < n && gcd a n = 1 -> a
        | _ ->
            // Random draw from [2, N-2] (upper bound of Next is exclusive).
            // Only reached for odd composite N ≥ 9, so the range is never empty.
            Random.Shared.Next(2, n - 1)

    /// Plan Shor execution strategy.
    ///
    /// Notes:
    /// - Validation failures return Error.
    /// - Trivial outcomes return ReturnResult.
    /// - Otherwise, returns a plan that delegates period finding through the ADR pipeline.
    let plan (backend: IQuantumBackend) (intent: ShorExecutionIntent) : Result<ShorPlan, QuantumError> =
        let config = intent.Config
        let n = config.NumberToFactor

        let approximateEpsilon =
            match intent.Exactness with
            | QPE.Exactness.Approximate epsilon -> Some epsilon
            | QPE.Exactness.Exact -> None

        // ========== CONFIGURATION VALIDATION ==========
        // Exactness was only ever read by the classically-assisted QPE demonstration. The
        // quantum path builds its inverse QFT with every controlled-phase rotation present,
        // so there is nothing for Approximate to loosen. Refuse it rather than accept a
        // parameter and ignore it — an approximate QFT is real work, not a default.
        if approximateEpsilon.IsSome then
            Error(
                QuantumError.ValidationError(
                    "Exactness",
                    $"Shor period finding builds an exact inverse QFT; Approximate {approximateEpsilon.Value} is not implemented. Pass QPE.Exactness.Exact."
                )
            )
        // Validate number range FIRST (before precision qubits check)
        // This ensures N > 1000 error takes precedence over derived precision errors.
        elif n > 1000 then
            Error(QuantumError.ValidationError("NumberToFactor", "must be ≤ 1000 for local simulation"))
        elif config.PrecisionQubits <= 0 then
            Error(QuantumError.ValidationError("PrecisionQubits", "must be positive"))
        elif config.PrecisionQubits > simulatorQubitBudget then
            Error(
                QuantumError.ValidationError(
                    "PrecisionQubits",
                    $"must be ≤ {simulatorQubitBudget} for local simulation"
                )
            )

        // ========== CLASSICAL PRE-CHECKS ==========
        // Check if N < 4 (too small) - MUST CHECK FIRST before even/prime checks.
        elif n < 4 then
            Ok(ShorPlan.ReturnResult(mkResult n None None false "Number too small (must be ≥ 4)" config))
        // Check if N is even (trivial case).
        elif isEven n then
            Ok(ShorPlan.ReturnResult(mkResult n (Some(2, n / 2)) None true "Number is even (trivial factor 2)" config))
        // Check if N is prime (no factors).
        elif isPrime n then
            Ok(ShorPlan.ReturnResult(mkResult n None None false "Number is prime (no non-trivial factors)" config))
        else
            // ========== QUANTUM PERIOD-FINDING ==========
            let a = chooseRandomBase n config.RandomBase
            let gcdResult = gcd a n

            if gcdResult <> 1 then
                Ok(
                    ShorPlan.ReturnResult(
                        mkResult
                            n
                            (Some(gcdResult, n / gcdResult))
                            None
                            true
                            $"Lucky! gcd({a}, {n}) = {gcdResult} (non-trivial factor)"
                            config
                    )
                )
            elif maxCountingQubitsFor n < registerBitsFor n then
                // Period finding needs enough counting qubits to resolve the period within
                // the simulator budget. When it does not fit, say so. Computing the period
                // classically and returning it as a quantum result is the one thing this
                // library must not do, so infeasibility is reported at plan time rather
                // than papered over at execution time.
                Error(unfittableCircuitError n)
            else
                Ok(ShorPlan.ExecuteQuantum(a, n, config.PrecisionQubits, intent.Exactness, config.MaxAttempts, config))

    let private executePlan (backend: IQuantumBackend) (plan: ShorPlan) : Result<ShorsResult, QuantumError> =
        match plan with
        | ShorPlan.ReturnResult result -> Ok result
        | ShorPlan.ExecuteQuantum(baseNum, modulus, precisionQubits, _exactness, maxAttempts, config) ->
            let findPeriodOnce a =
                findPeriodQuantum a modulus precisionQubits backend

            // Whether a period-finding failure is worth another base.
            //
            // QPE is probabilistic: `findPeriodQuantum` gives up after its own retry budget
            // and reports OperationError("Period finding", …). That is a failure OF THIS
            // BASE, not of the configuration, and retrying with a fresh base is exactly what
            // the outer attempt budget is for — so it must not abort the whole run while
            // attempts remain. Everything else (a validation error, a qubit-budget error,
            // a backend failure) would fail identically for every base, so it is returned
            // straight away rather than burned through maxAttempts times.
            let isRetryablePeriodFailure (err: QuantumError) =
                match err with
                | QuantumError.OperationError("Period finding", _) -> true
                | _ -> false

            let rec tryFindFactors attempt a (lastError: QuantumError option) : Result<ShorsResult, QuantumError> =
                if attempt > maxAttempts then
                    // Out of attempts. If the last thing that happened was a probabilistic
                    // period-finding failure, surface it — it explains the outcome better
                    // than a bare count.
                    let reason =
                        match lastError with
                        | Some err -> $"Failed to find factors after {maxAttempts} attempts ({err.Message})"
                        | None -> $"Failed to find factors after {maxAttempts} attempts"

                    Ok(mkResult modulus None None false reason config)
                else
                    let g = gcd a modulus

                    if g <> 1 then
                        // A freshly drawn retry base sharing a factor with N is a lucky
                        // classical hit — no period finding needed.
                        Ok(
                            mkResult
                                modulus
                                (Some(g, modulus / g))
                                None
                                true
                                $"Lucky! gcd({a}, {modulus}) = {g} (non-trivial factor)"
                                config
                        )
                    else
                        match findPeriodOnce a with
                        | Error err when isRetryablePeriodFailure err ->
                            tryFindFactors (attempt + 1) (chooseRandomBase modulus None) (Some err)
                        | Error err -> Error err
                        | Ok periodResult ->
                            match extractFactorsFromPeriod a periodResult.Period modulus with
                            | Some(p, q) ->
                                Ok(
                                    mkResult
                                        modulus
                                        (Some(p, q))
                                        (Some periodResult)
                                        true
                                        $"Factors found using period r={periodResult.Period}"
                                        config
                                )
                            | None ->
                                // Period finding is deterministic for a given base on the
                                // classically-assisted path, so retrying the SAME base would fail
                                // forever (e.g. N=33 with a=2: r=10, 2^5 ≡ -1 mod 33). Draw a fresh
                                // random base for each retry.
                                tryFindFactors (attempt + 1) (chooseRandomBase modulus None) None

            tryFindFactors 1 baseNum None

    // ========================================================================
    // MAIN SHOR'S ALGORITHM EXECUTION
    // ========================================================================

    /// <summary>
    /// Execute Shor's factoring algorithm with an explicit QPE exactness.
    /// Period finding is QPE on modular exponentiation; there is no strategy to choose.
    /// </summary>
    /// <param name="config">Shor's algorithm configuration</param>
    /// <param name="exactness">
    /// QPE exactness. Only <c>Exact</c> is accepted: period finding builds an exact inverse
    /// QFT, so <c>Approximate</c> is not implemented and is refused with a validation error.
    /// </param>
    /// <param name="backend">Quantum backend</param>
    /// <returns>Factorization result or error</returns>
    let executeWith
        (config: ShorsConfig)
        (exactness: QPE.Exactness)
        (backend: IQuantumBackend)
        : Result<ShorsResult, QuantumError> =

        let intent: ShorExecutionIntent =
            {
                Config = config
                Exactness = exactness
            }

        plan backend intent |> Result.bind (executePlan backend)

    /// <summary>
    /// Execute Shor's factoring algorithm.
    /// Given composite number N, find non-trivial factors p and q such that N = p × q.
    /// </summary>
    /// <param name="config">Shor's algorithm configuration</param>
    /// <param name="backend">Quantum backend</param>
    /// <returns>Factorization result or error</returns>
    /// <remarks>
    /// Algorithm steps:
    /// 1. Classical pre-checks (even, prime, perfect power)
    /// 2. Choose random base a coprime to N
    /// 3. Find period r using quantum phase estimation
    /// 4. Extract factors from period using gcd
    /// 5. Verify factors
    /// </remarks>
    /// <example>
    /// <code>
    /// let config = {
    ///     NumberToFactor = 15
    ///     RandomBase = Some 7
    ///     PrecisionQubits = 8
    ///     MaxAttempts = 3
    /// }
    ///
    /// match execute config backend with
    /// | Ok result ->
    ///     match result.Factors with
    ///     | Some (p, q) -> printfn "%d = %d × %d" result.Number p q
    ///     | None -> printfn "Factorization failed: %s" result.Message
    /// | Error err -> printfn "Error: %A" err
    /// </code>
    /// </example>
    let execute (config: ShorsConfig) (backend: IQuantumBackend) : Result<ShorsResult, QuantumError> =

        executeWith config QPE.Exactness.Exact backend

    // ========================================================================
    // CONVENIENCE FUNCTIONS
    // ========================================================================

    /// <summary>
    /// Factor a number using default configuration.
    /// </summary>
    /// <param name="n">Number to factor</param>
    /// <param name="backend">Quantum backend</param>
    /// <returns>Factorization result or error</returns>
    /// <example>
    /// <code>
    /// match factor 21 backend with
    /// | Ok result -> printfn "%A" result
    /// | Error err -> printfn "Error: %A" err
    /// </code>
    /// </example>
    let factor (n: int) (backend: IQuantumBackend) : Result<ShorsResult, QuantumError> =

        // Calculate recommended precision: 2 * log₂(N) + 3, clamped to the simulator
        // validation bound (the formula exceeds it from N = 512; for such N the plan degrades
        // to the classically-assisted path anyway, which clamps further to its own 16 cap).
        let precisionQubits =
            min simulatorQubitBudget (2 * int (Math.Log(float n, 2.0)) + 3)

        let config =
            {
                NumberToFactor = n
                RandomBase = None // Let algorithm choose random base
                PrecisionQubits = precisionQubits
                MaxAttempts = 3
            }

        executeWith config QPE.Exactness.Exact backend

    /// <summary>
    /// Factor 15 using Shor's algorithm.
    /// This is the classic educational example of Shor's algorithm.
    /// Expected result: 15 = 3 × 5
    /// </summary>
    /// <param name="backend">Quantum backend</param>
    /// <returns>Factorization result or error</returns>
    /// <example>
    /// <code>
    /// match factor15 backend with
    /// | Ok result ->
    ///     match result.Factors with
    ///     | Some (p, q) -> printfn "15 = %d × %d" p q
    ///     | None -> printfn "Could not find factors"
    /// | Error err -> printfn "Error: %A" err
    /// </code>
    /// </example>
    let factor15 (backend: IQuantumBackend) : Result<ShorsResult, QuantumError> =

        // Genuine quantum period finding. N=15 has period r=4, which 3 counting qubits
        // resolve exactly (total 15 qubits), keeping the full quantum circuit fast.
        let config =
            {
                NumberToFactor = 15
                RandomBase = Some 7 // Known to work well for N=15
                PrecisionQubits = 3
                MaxAttempts = 5
            }

        executeWith config QPE.Exactness.Exact backend

    /// <summary>
    /// Factor 21 using Shor's algorithm.
    /// Another common educational example.
    /// Expected result: 21 = 3 × 7
    /// </summary>
    /// <param name="backend">Quantum backend</param>
    /// <returns>Factorization result or error</returns>
    let factor21 (backend: IQuantumBackend) : Result<ShorsResult, QuantumError> =

        // N=21 (period r=6) needs the full 2n+4 workspace, so the genuine circuit is 20
        // qubits and this is slow — tens of minutes on a local simulator. It is slow because
        // it is real; the previous version returned in milliseconds by finding the period
        // classically, which is not factoring 21 on a quantum computer.
        //
        // MaxAttempts is 10 rather than 3: four of the ten coprime bases below 21 yield no
        // factors from their period, so a small budget turns an honest run into a coin flip.
        let config =
            {
                NumberToFactor = 21
                RandomBase = Some 2 // Known to work well for N=21
                PrecisionQubits = 8
                MaxAttempts = 10
            }

        executeWith config QPE.Exactness.Exact backend
