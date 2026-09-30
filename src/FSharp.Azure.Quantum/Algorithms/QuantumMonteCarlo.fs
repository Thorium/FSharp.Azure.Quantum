namespace FSharp.Azure.Quantum.Algorithms

open System
open System.Numerics
open FSharp.Azure.Quantum
open FSharp.Azure.Quantum.Core
open FSharp.Azure.Quantum.Core.BackendAbstraction
open FSharp.Azure.Quantum.Core.CircuitAbstraction
open FSharp.Azure.Quantum.LocalSimulator

/// Quantum Monte Carlo - RULE1 Compliant Implementation
///
/// **RULE1**: All public APIs require IQuantumBackend parameter
/// **Quadratic Speedup**: O(1/ε²) → O(1/ε) for precision ε
///
/// MATHEMATICAL FOUNDATION:
/// Classical Monte Carlo: Estimate E[f(X)] using N samples → accuracy O(1/√N)
/// Quantum Monte Carlo: Uses Amplitude Estimation → accuracy O(1/N) queries
/// Result: Quadratic speedup (100x for 10,000 samples)
///
/// ALGORITHM OVERVIEW:
/// 1. State Preparation: Encode probability distribution in quantum state
/// 2. Oracle: Mark states to measure (e.g., "in-the-money" for options)
/// 3. Amplitude Estimation: Grover-based algorithm to estimate probability
/// 4. Expectation: Extract value from amplitude
///
/// **Classical Monte Carlo**: Private only (for validation/comparison)
///
/// REFERENCE:
/// Rebentrost et al., "Quantum computational finance: Monte Carlo pricing of financial derivatives"
/// Phys. Rev. A 98, 022321 (2018) - https://arxiv.org/abs/1805.00109
module QuantumMonteCarlo =

    // ========================================================================
    // TYPES
    // ========================================================================

    /// Configuration for Quantum Monte Carlo
    type QMCConfig =
        {
            /// Number of qubits for state representation
            /// More qubits → finer discretization of probability space
            NumQubits: int

            /// State preparation circuit (encodes probability distribution)
            /// Creates superposition: ∑_x √p(x) |x⟩
            StatePreparation: CircuitBuilder.Circuit

            /// Oracle circuit (marks target states)
            /// Applies phase flip to states where f(x) = 1
            Oracle: CircuitBuilder.Circuit

            /// Number of Grover iterations for amplitude estimation
            /// Optimal: O(1/√a) where a is target amplitude
            /// More iterations → higher precision
            GroverIterations: int

            /// Number of measurement shots
            Shots: int
        }

    /// Result of Quantum Monte Carlo estimation
    type QMCResult =
        {
            /// Estimated expectation value
            ExpectationValue: float

            /// Cramér–Rao standard error of ExpectationValue from the maximum-likelihood
            /// amplitude-estimation fit, for the shots behind each measured probability: the
            /// backend's Shots on a shot-sampling (IShotSamplingBackend) backend that ran whole
            /// circuits, else config.Shots
            StandardError: float

            /// Success probability (measured amplitude squared)
            SuccessProbability: float

            /// Number of quantum queries used
            QuantumQueries: int

            /// Classical equivalent sample count (for speedup metric)
            ClassicalEquivalent: int

            /// Speedup factor (quantum vs classical)
            SpeedupFactor: float
        }

    // ========================================================================
    // NO CLASSICAL BASELINE - RULE1 STRICT COMPLIANCE
    // ========================================================================
    //
    // This module contains ONLY quantum implementations.
    // Classical Monte Carlo has been removed to ensure RULE1 compliance:
    //
    // "This is an Azure Quantum library, NOT a standalone solver library.
    //  All code must depend on IBackend."
    //
    // For classical Monte Carlo comparison:
    // - Use external libraries (NumPy, SciPy, QuantLib)
    // - Or implement in separate classical solver if needed for testing
    //
    // This keeps the quantum algorithm pure and RULE1 compliant.

    // ========================================================================
    // PRIVATE - Circuit Construction Helpers
    // ========================================================================

    /// Phase flip on |0...0⟩ : S₀ = I − 2|0⟩⟨0| (up to a global sign).
    /// X on every qubit maps |0...0⟩ → |1...1⟩, a multi-controlled Z flips that
    /// single basis state's phase, and the X layer is undone afterwards.
    let private buildZeroReflection (numQubits: int) : CircuitBuilder.Circuit =
        let circuit = CircuitBuilder.empty numQubits

        let afterXGates =
            [ 0 .. numQubits - 1 ]
            |> List.fold (fun c q -> c |> CircuitBuilder.addGate (CircuitBuilder.X q)) circuit

        let afterControlledZ =
            if numQubits = 1 then
                afterXGates |> CircuitBuilder.addGate (CircuitBuilder.Z 0)
            elif numQubits = 2 then
                afterXGates |> CircuitBuilder.addGate (CircuitBuilder.CZ(0, 1))
            else
                let controls = [ 0 .. numQubits - 2 ]

                afterXGates
                |> CircuitBuilder.addGate (CircuitBuilder.MCZ(controls, numQubits - 1))

        [ 0 .. numQubits - 1 ]
        |> List.fold (fun c q -> c |> CircuitBuilder.addGate (CircuitBuilder.X q)) afterControlledZ

    /// Reflection about the prepared state |ψ⟩ = A|0⟩ :  2|ψ⟩⟨ψ| − I = A · S₀ · A†
    /// (up to a global phase).
    ///
    /// This is the CORRECT diffusion for amplitude amplification of an arbitrary,
    /// possibly non-uniform state preparation A. The textbook H-based diffusion
    /// (H^⊗n S₀ H^⊗n) only reflects about the uniform superposition and is therefore
    /// only valid when A = H^⊗n — it gives wrong amplification for a Möttönen-encoded
    /// (non-uniform) distribution, which is exactly the case in quantum finance.
    let private buildStateReflection (statePrep: CircuitBuilder.Circuit) : CircuitBuilder.Circuit =
        let numQubits = statePrep.QubitCount
        let aDagger = CircuitBuilder.reverse statePrep
        let s0 = buildZeroReflection numQubits
        // Operator product A·S₀·A† ⇒ execute A† first, then S₀, then A.
        // CircuitBuilder.compose c1 c2 runs c1 then c2.
        CircuitBuilder.compose (CircuitBuilder.compose aDagger s0) statePrep

    /// Amplitude-amplification (Grover) operator  Q = (reflection about |ψ⟩) · (oracle).
    /// Applying Q^k to |ψ⟩ rotates the good-state amplitude to sin²((2k+1)θ) where
    /// sin²θ = a is the marked-subspace probability.
    let private buildGroverOperator
        (statePrep: CircuitBuilder.Circuit)
        (oracle: CircuitBuilder.Circuit)
        : CircuitBuilder.Circuit =
        let diffusion = buildStateReflection statePrep
        // Run the oracle first, then the reflection about |ψ⟩ (compose c1 c2 = c1 then c2).
        CircuitBuilder.compose oracle diffusion

    // ========================================================================
    // INTENT → PLAN → EXECUTE (ADR: Intent-First)
    // ========================================================================

    type private QmcIntent = { Config: QMCConfig }

    [<RequireQualifiedAccess>]
    type private QmcPlan = | ExecuteViaCircuit

    let private supportsCircuit (backend: IQuantumBackend) (circuit: CircuitBuilder.Circuit) : bool =
        circuit.Gates
        |> List.forall (fun gate -> backend.SupportsOperation(QuantumOperation.Gate gate))

    let private plan (backend: IQuantumBackend) (intent: QmcIntent) : Result<QmcPlan, QuantumError> =
        // Quantum Monte Carlo relies on gate operations; explicit refusal for annealing backends.
        match backend.NativeStateType with
        | QuantumStateType.Annealing ->
            Error(
                QuantumError.OperationError(
                    "QuantumMonteCarlo",
                    $"Backend '{backend.Name}' does not support quantum Monte Carlo (native state type: {backend.NativeStateType})"
                )
            )
        | _ ->
            // Ensure all gates in the user-provided circuits are supported.
            if
                supportsCircuit backend intent.Config.StatePreparation
                && supportsCircuit backend intent.Config.Oracle
            then
                Ok QmcPlan.ExecuteViaCircuit
            else
                Error(
                    QuantumError.OperationError(
                        "QuantumMonteCarlo",
                        $"Backend '{backend.Name}' does not support all required circuit operations"
                    )
                )

    /// Probability of every computational basis state (index i: qubit q = bit q of i) of a
    /// state a backend returned: |amplitude|² of a state vector, which is exact on a
    /// simulator and the job's outcome frequencies for a sampled whole-circuit result, or the
    /// diagonal of a density matrix.
    let private basisProbabilities (state: QuantumState) : Result<float[], QuantumError> =
        match state with
        | QuantumState.StateVector sv ->
            let dim = 1 <<< StateVector.numQubits sv

            Ok(
                Array.init dim (fun i ->
                    let a = StateVector.getAmplitude i sv
                    a.Real * a.Real + a.Imaginary * a.Imaginary)
            )
        | QuantumState.SparseState(amplitudes, n) ->
            let probabilities = Array.zeroCreate (1 <<< n)

            for KeyValue(i, a) in amplitudes do
                probabilities.[i] <- a.Real * a.Real + a.Imaginary * a.Imaginary

            Ok probabilities
        | QuantumState.DensityMatrix(rho, n) -> Ok(Array.init (1 <<< n) (fun i -> rho.[i, i].Real))
        | QuantumState.FusionSuperposition superposition ->
            // A topological backend's state: its logical-qubit probabilities, exact like a
            // simulator's (bitstring.[q] = qubit q).
            let n = superposition.LogicalQubits

            Ok(Array.init (1 <<< n) (fun i -> superposition.Probability(Array.init n (fun q -> (i >>> q) &&& 1))))
        | QuantumState.MeasurementHistogram(histogram, n) when n <= 30 ->
            // A job's counts (character q = qubit q), as outcome frequencies.
            let probabilities = Array.zeroCreate (1 <<< n)
            let total = histogram |> Map.fold (fun acc _ c -> acc + max 0 c) 0 |> max 1 |> float

            for KeyValue(key, count) in histogram do
                let index =
                    Seq.indexed key
                    |> Seq.sumBy (fun (q, c) -> if c = '1' && q < n then 1 <<< q else 0)

                probabilities.[index] <- probabilities.[index] + float (max 0 count) / total

            Ok probabilities
        | _ ->
            Error(
                QuantumError.OperationError(
                    "QuantumMonteCarlo",
                    "Amplitude estimation needs basis-state probabilities; the backend returned a state without them"
                )
            )

    /// Run a gate circuit from |0…0⟩ and return its basis-state probabilities, and whether it
    /// was submitted whole. Gate by gate (UnifiedBackend.applySequence) on a backend that applies
    /// gates one at a time; as one whole circuit (UnifiedBackend.submitAsCircuit) only when the
    /// backend refuses incremental application, as cloud hardware does.
    let private runForProbabilities
        (backend: IQuantumBackend)
        (circuit: CircuitBuilder.Circuit)
        : Result<float[] * bool, QuantumError> =
        let numQubits = circuit.QubitCount
        let operations = CircuitBuilder.getGates circuit |> List.map QuantumOperation.Gate

        match
            backend.InitializeState numQubits
            |> Result.bind (UnifiedBackend.applySequence backend operations)
        with
        | Error e when UnifiedBackend.isIncrementalUnsupported e ->
            UnifiedBackend.submitAsCircuit backend numQubits operations
            |> Result.bind basisProbabilities
            |> Result.map (fun p -> p, true)
        | stepped -> stepped |> Result.bind basisProbabilities |> Result.map (fun p -> p, false)

    /// The marked set of a phase oracle, read from the oracle's definition: the oracle circuit
    /// is simulated exactly on the uniform superposition by the local state-vector simulator,
    /// never on the target backend (a sampled result carries no phases), and the marked states
    /// are those whose amplitude it negates. The backend only measures probabilities. An
    /// oracle that does anything but multiply each basis state by +1 or -1 is an Error.
    let private markedSetOfOracle (oracle: CircuitBuilder.Circuit) : Result<Set<int>, QuantumError> =
        let numQubits = oracle.QubitCount

        let uniform =
            [ 0 .. numQubits - 1 ]
            |> List.fold (fun c q -> c |> CircuitBuilder.addGate (CircuitBuilder.H q)) (CircuitBuilder.empty numQubits)

        let exact =
            FSharp.Azure.Quantum.Backends.LocalBackend.LocalBackend() :> IQuantumBackend

        // H^⊗n first, then the oracle (compose c1 c2 = c1 then c2)
        exact.ExecuteToState(CircuitWrapper(CircuitBuilder.compose uniform oracle) :> ICircuit)
        |> Result.bind (fun state ->
            match state with
            | QuantumState.StateVector sv ->
                let dim = 1 <<< numQubits
                let expected = 1.0 / sqrt (float dim)
                let amplitudes = Array.init dim (fun i -> StateVector.getAmplitude i sv)

                if
                    amplitudes
                    |> Array.forall (fun a -> abs a.Imaginary < 1e-7 && abs (abs a.Real - expected) < 1e-7)
                then
                    amplitudes
                    |> Array.indexed
                    |> Array.choose (fun (i, a) -> if a.Real < 0.0 then Some i else None)
                    |> Set.ofArray
                    |> Ok
                else
                    Error(
                        QuantumError.ValidationError(
                            "Oracle",
                            "must be a phase oracle that multiplies each basis state by +1 or -1"
                        )
                    )
            | _ ->
                Error(
                    QuantumError.OperationError(
                        "QuantumMonteCarlo",
                        "local simulation of the oracle gave no state vector"
                    )
                ))

    /// Probability mass on the marked subspace.
    let private markedProbability (markedSet: Set<int>) (probabilities: float[]) : float =
        markedSet
        |> Set.fold
            (fun acc i ->
                if i < probabilities.Length then
                    acc + probabilities.[i]
                else
                    acc)
            0.0

    /// Grover-power schedule for Maximum-Likelihood Amplitude Estimation: exponentially
    /// increasing powers (0,1,2,4,...) capped at the configured budget. Power 0 is the
    /// bare prepared state, whose marked probability is a itself.
    let private mlaeSchedule (maxIterations: int) : int list =
        let rec build acc k =
            if k > maxIterations || List.length acc >= 7 then
                List.rev acc
            else
                build (k :: acc) (if k = 0 then 1 else k * 2)

        build [] 0

    /// Maximum-Likelihood Amplitude Estimation: recover θ (a = sin²θ) from the marked-state
    /// probabilities measured at several Grover powers, each obeying
    /// P_k(good) = sin²((2k+1)θ). A grid search over θ ∈ [0, π/2] maximises the Bernoulli
    /// log-likelihood (every power has the same shot count, so it is unweighted), followed by
    /// a local refinement, then a bisection of the score dL/dθ to the exact maximum.
    ///
    /// Why the bisection: the grid alone resolves θ to (π/2)/(2000·50) ≈ 1.6e-5 rad. That is
    /// far below the shot noise of a sampling backend, but with the exact probabilities of a
    /// simulator it quantises the estimate: about 1e-3 of an option price, which is the whole
    /// signal of a 1bp rate bump or a one-day time bump, so finite-difference Greeks (Rho,
    /// Theta, Gamma) lost several percent to it. With exact probabilities every term of the
    /// likelihood peaks at the true θ, so the score's root recovers it (to ~1e-8 when a power
    /// sits at probability 1, whose likelihood pins θ only to the square root of rounding).
    let private estimateThetaMLAE (measurements: (int * float) list) : float =
        let logLikelihood (theta: float) : float =
            measurements
            |> List.sumBy (fun (k, pGood) ->
                let angle = float (2 * k + 1) * theta
                let s = max 1e-12 ((sin angle) ** 2.0)
                let c = max 1e-12 ((cos angle) ** 2.0)
                pGood * log s + (1.0 - pGood) * log c)

        let gridN = 2000
        let half = Math.PI / 2.0

        let coarse =
            List.init (max 0 (gridN + 1)) (fun i -> let th = half * float i / float gridN in (th, logLikelihood th))
            |> List.maxBy snd
            |> fst

        let step = half / float gridN

        let fine =
            [ -50 .. 50 ]
            |> List.map (fun j -> coarse + float j * step / 50.0)
            |> List.filter (fun th -> th >= 0.0 && th <= half)
            |> List.map (fun th -> (th, logLikelihood th))
            |> List.maxBy snd
            |> fst

        // Score dL/dθ = Σ 2m·[p·cot(mθ) − (1 − p)·tan(mθ)], m = 2k + 1; NaN/∞ where a term is
        // singular (θ at 0 or π/2), in which case the grid estimate stands.
        let score (theta: float) : float =
            measurements
            |> List.sumBy (fun (k, pGood) ->
                let m = float (2 * k + 1)
                let angle = m * theta

                2.0
                * m
                * (pGood * cos angle / sin angle - (1.0 - pGood) * sin angle / cos angle))

        let lo = max 0.0 (fine - step / 50.0)
        let hi = min half (fine + step / 50.0)
        let scoreLo, scoreHi = score lo, score hi

        if
            Double.IsFinite scoreLo
            && Double.IsFinite scoreHi
            && scoreLo > 0.0
            && scoreHi < 0.0
        then
            let rec bisect (a: float) (b: float) (iterations: int) =
                let mid = 0.5 * (a + b)

                if iterations = 0 || mid <= a || mid >= b then
                    mid
                else
                    let sMid = score mid

                    if not (Double.IsFinite sMid) then mid
                    elif sMid > 0.0 then bisect mid b (iterations - 1)
                    else bisect a mid (iterations - 1)

            bisect lo hi 80
        else
            fine

    /// Shots behind each probability a sampling backend returns (IShotSamplingBackend).
    let private samplingShots (backend: IQuantumBackend) : int option =
        match backend with
        | :? IShotSamplingBackend as sampling when sampling.Shots > 0 -> Some sampling.Shots
        | _ -> None

    /// Outcome of runAmplitudeEstimation.
    type private AmplitudeEstimate =
        {
            Amplitude: float
            StandardError: float
            Powers: int list
            WholeCircuit: bool
            ShotsPerCircuit: int option
        }

    /// Amplitude estimation end to end: measure the marked-state probability P_k(good) of
    /// statePrep · Q^k on the backend at each Grover power k of the MLAE schedule, and fit
    /// a = sin²θ by maximum likelihood. StandardError is the Cramér–Rao error of that fit,
    /// sin(2θ)·σ_θ + σ_θ² with σ_θ = 1 / (2 √(N Σ_k (2k+1)²)), for N shots per circuit: the
    /// backend's when it samples whole circuits, else `shots`. The second-order term keeps
    /// it positive at a = 0 or 1.
    let private runAmplitudeEstimation
        (backend: IQuantumBackend)
        (statePrep: CircuitBuilder.Circuit)
        (oracle: CircuitBuilder.Circuit)
        (markedSet: Set<int>)
        (groverIterations: int)
        (shots: int)
        : Result<AmplitudeEstimate, QuantumError> =
        let groverOp = buildGroverOperator statePrep oracle

        let buildAmplified (k: int) : CircuitBuilder.Circuit =
            // State prep first, then k applications of the Grover operator (compose c1 c2 = c1 then c2).
            [ 1..k ] |> List.fold (fun c _ -> CircuitBuilder.compose c groverOp) statePrep

        let powers = mlaeSchedule groverIterations

        powers
        |> List.fold
            (fun accR k ->
                accR
                |> Result.bind (fun (acc, whole) ->
                    runForProbabilities backend (buildAmplified k)
                    |> Result.map (fun (probabilities, submittedWhole) ->
                        (k, markedProbability markedSet probabilities) :: acc, whole || submittedWhole)))
            (Ok([], false))
        |> Result.map (fun (measured, whole) ->
            let shotsPerCircuit = if whole then samplingShots backend else None
            let n = float (defaultArg shotsPerCircuit shots)
            let theta = estimateThetaMLAE (List.rev measured)

            let sigmaTheta =
                1.0
                / (2.0
                   * sqrt (n * (powers |> List.sumBy (fun k -> float ((2 * k + 1) * (2 * k + 1))))))

            {
                Amplitude = (sin theta) ** 2.0
                StandardError = abs (sin (2.0 * theta)) * sigmaTheta + sigmaTheta * sigmaTheta
                Powers = powers
                WholeCircuit = whole
                ShotsPerCircuit = shotsPerCircuit
            })

    /// Measure the bin probabilities q_i = |⟨i|ψ⟩|² produced by a state-preparation circuit on
    /// the given backend (basis index i ↔ bin i): exact on a simulator run gate by gate, the
    /// job's outcome frequencies on a backend that runs whole circuits only.
    let measureBinProbabilities
        (backend: IQuantumBackend)
        (statePrep: CircuitBuilder.Circuit)
        : Result<float[], QuantumError> =
        runForProbabilities backend statePrep |> Result.map fst

    /// Result of estimateBoundedExpectation.
    type BoundedExpectationResult =
        {
            /// Maximum-likelihood amplitude estimate of E[f(X)]
            Expectation: float

            /// Cramér–Rao standard error of Expectation for the shots behind each measured
            /// probability (ShotsPerCircuit, else the requested shots)
            StandardError: float

            /// Grover powers whose marked-state probability was measured (0 = the prepared state)
            GroverPowers: int list

            /// True when the backend refused gate-by-gate application and every circuit was
            /// submitted whole
            WholeCircuit: bool

            /// Shots behind each measured probability when a sampling backend
            /// (IShotSamplingBackend) ran whole circuits; None when the probabilities are exact
            ShotsPerCircuit: int option
        }

    /// Amplitude estimation of a bounded expectation E[f(X)] = Σ_i p_i f(i), f(i) ∈ [0, 1],
    /// where `statePreparation` prepares Σ_i √p_i |i⟩ on its n qubits (basis index i, qubit q
    /// = bit q of i); the construction of Woerner and Egger, "Quantum risk analysis" (2019).
    ///
    /// A = statePreparation, then an RY(2·asin √f(i)) on one ancilla (qubit n) uniformly
    /// controlled by the n state qubits, so P(ancilla = 1) = E[f(X)]. The marked set is that
    /// predicate, ancilla = 1, and the oracle is a Z on the ancilla. The estimate is the
    /// maximum-likelihood fit of the ancilla = 1 probabilities measured at the Grover powers
    /// 0, 1, 2, 4, … ≤ groverIterations: exact probabilities on a simulator run gate by gate,
    /// the job's outcome frequencies on a backend that runs whole circuits only. The circuits
    /// use n + 1 qubits and H, X, RY, CNOT and the multi-controlled Z of the reflection.
    let estimateBoundedExpectation
        (statePreparation: CircuitBuilder.Circuit)
        (values: float[])
        (groverIterations: int)
        (shots: int)
        (backend: IQuantumBackend)
        : Async<QuantumResult<BoundedExpectationResult>> =
        async {
            let n = statePreparation.QubitCount

            return
                if n < 1 then
                    Error(QuantumError.ValidationError("statePreparation", "Must act on at least 1 qubit"))
                elif n + 1 > StateVector.practicalCircuitQubits then
                    Error(
                        QuantumError.ValidationError(
                            "statePreparation",
                            $"{n} qubits plus the ancilla exceed the circuit budget ({StateVector.practicalCircuitQubits})"
                        )
                    )
                elif values.Length <> (1 <<< n) then
                    Error(
                        QuantumError.ValidationError(
                            "values",
                            $"Need one value per basis state ({1 <<< n}), got {values.Length}"
                        )
                    )
                elif
                    values
                    |> Array.exists (fun v -> Double.IsNaN v || v < -1e-12 || v > 1.0 + 1e-12)
                then
                    Error(QuantumError.ValidationError("values", "Every value must lie in [0, 1]"))
                elif groverIterations < 0 then
                    Error(QuantumError.ValidationError("groverIterations", "Must be >= 0"))
                elif shots < 1 then
                    Error(QuantumError.ValidationError("shots", "Must be >= 1"))
                elif backend.NativeStateType = QuantumStateType.Annealing then
                    Error(
                        QuantumError.OperationError(
                            "QuantumMonteCarlo",
                            $"Backend '{backend.Name}' does not support amplitude estimation (native state type: {backend.NativeStateType})"
                        )
                    )
                else
                    let ancilla = n

                    let angles = values |> Array.map (fun v -> 2.0 * asin (sqrt (min 1.0 (max 0.0 v))))

                    let widened: CircuitBuilder.Circuit =
                        {
                            QubitCount = n + 1
                            Gates = statePreparation.Gates
                        }

                    let a =
                        MottonenStatePreparation.uniformlyControlledRY angles ancilla [| 0 .. n - 1 |] widened

                    let oracle =
                        CircuitBuilder.empty (n + 1)
                        |> CircuitBuilder.addGate (CircuitBuilder.Z ancilla)

                    if not (supportsCircuit backend a && supportsCircuit backend oracle) then
                        Error(
                            QuantumError.OperationError(
                                "QuantumMonteCarlo",
                                $"Backend '{backend.Name}' does not support all required circuit operations"
                            )
                        )
                    else
                        let marked = Set.ofList [ (1 <<< n) .. (1 <<< (n + 1)) - 1 ]

                        runAmplitudeEstimation backend a oracle marked groverIterations shots
                        |> Result.map (fun estimate ->
                            {
                                Expectation = estimate.Amplitude
                                StandardError = estimate.StandardError
                                GroverPowers = estimate.Powers
                                WholeCircuit = estimate.WholeCircuit
                                ShotsPerCircuit = estimate.ShotsPerCircuit
                            })
        }

    // ========================================================================
    // PUBLIC - Quantum Monte Carlo (RULE1: backend required)
    // ========================================================================

    /// Execute Quantum Monte Carlo with quantum backend (RULE1 compliant)
    ///
    /// **REQUIRED PARAMETER**: backend: IQuantumBackend
    /// **Quadratic Speedup**: O(1/ε) quantum queries vs O(1/ε²) classical samples
    ///
    /// ALGORITHM:
    /// 1. Prepare state |ψ⟩ = StatePreparation|0⟩ = ∑_x √p(x)|x⟩
    /// 2. Apply Grover iterations: G^k |ψ⟩ where G = Diffusion · Oracle
    /// 3. Measure to estimate amplitude a (probability of marked states)
    /// 4. Extract original amplitude from Grover-amplified result
    /// 5. Return expectation value E = a
    let estimateExpectation
        (config: QMCConfig)
        (backend: IQuantumBackend) // ✅ RULE1: Backend required
        : Async<QuantumResult<QMCResult>> =

        async {
            return
                quantumResult {
                    // Validate config
                    if config.NumQubits < 1 then
                        return! Error(QuantumError.ValidationError("NumQubits", "Must be >= 1"))
                    elif config.NumQubits > StateVector.practicalCircuitQubits then
                        return!
                            Error(
                                QuantumError.ValidationError(
                                    "NumQubits",
                                    $"Too large (max {StateVector.practicalCircuitQubits})"
                                )
                            )
                    elif config.GroverIterations < 0 then
                        return! Error(QuantumError.ValidationError("GroverIterations", "Must be >= 0"))
                    elif config.Shots < 100 then
                        return! Error(QuantumError.ValidationError("Shots", "Must be >= 100"))
                    elif config.StatePreparation.QubitCount <> config.NumQubits then
                        return! Error(QuantumError.ValidationError("StatePreparation", "Qubit count mismatch"))
                    elif config.Oracle.QubitCount <> config.NumQubits then
                        return! Error(QuantumError.ValidationError("Oracle", "Qubit count mismatch"))
                    else

                        let intent = { Config = config }

                        // Validate backend support, take the marked set from the oracle's
                        // definition, then estimate the marked-subspace amplitude by
                        // Maximum-Likelihood Amplitude Estimation over a Grover-power schedule.
                        let! estimate =
                            plan backend intent
                            |> Result.bind (fun _ -> markedSetOfOracle config.Oracle)
                            |> Result.bind (fun markedSet ->
                                runAmplitudeEstimation
                                    backend
                                    config.StatePreparation
                                    config.Oracle
                                    markedSet
                                    config.GroverIterations
                                    config.Shots)

                        // The estimated marked amplitude a = sin²θ IS the expectation E[1_good] = P(good).
                        let originalAmplitude = estimate.Amplitude
                        let successProb = estimate.Amplitude

                        // The Cramér–Rao error of the maximum-likelihood fit that produced the
                        // estimate, for the shots actually behind each measured probability. The
                        // asymptotic O(1/M) query bound (1/GroverIterations) is a scaling law, not
                        // an error bar: it reported 0.25 for 4 iterations where the fit's real
                        // error at 1,000 shots is ~0.001.
                        let stdError = estimate.StandardError

                        // Classical equivalent samples for same accuracy
                        let classicalSamples =
                            if config.GroverIterations > 0 then
                                config.GroverIterations * config.GroverIterations
                            else
                                config.Shots

                        // Total quantum queries
                        let quantumQueries = config.GroverIterations * config.Shots

                        // Speedup factor
                        let speedup =
                            if quantumQueries > 0 then
                                float classicalSamples / float quantumQueries
                            else
                                1.0

                        return
                            {
                                ExpectationValue = originalAmplitude
                                StandardError = stdError
                                SuccessProbability = successProb
                                QuantumQueries = quantumQueries
                                ClassicalEquivalent = classicalSamples
                                SpeedupFactor = speedup
                            }
                }
        }

    // ========================================================================
    // CONVENIENCE FUNCTIONS (RULE1: all require backend)
    // ========================================================================

    /// Estimate probability using quantum backend (RULE1 compliant)
    ///
    /// Estimates P(f(X) = 1) where X follows distribution encoded in statePrep
    ///
    /// **REQUIRED PARAMETER**: backend: IQuantumBackend
    let estimateProbability
        (statePrep: CircuitBuilder.Circuit)
        (oracle: CircuitBuilder.Circuit)
        (iterations: int)
        (backend: IQuantumBackend) // ✅ RULE1: Backend required
        : Async<QuantumResult<float>> =

        async {
            let config =
                {
                    NumQubits = statePrep.QubitCount
                    StatePreparation = statePrep
                    Oracle = oracle
                    GroverIterations = iterations
                    Shots = 1000
                }

            let! result = estimateExpectation config backend
            return result |> Result.map (fun r -> r.ExpectationValue)
        }

    /// Numerical integration using quantum backend (RULE1 compliant)
    ///
    /// Estimates ∫_a^b f(x) dx using quantum amplitude estimation
    ///
    /// **REQUIRED PARAMETER**: backend: IQuantumBackend
    /// **Speedup**: O(1/ε) vs classical O(1/ε²)
    let integrate
        (functionOracle: CircuitBuilder.Circuit)
        (domain: float * float)
        (precision: int)
        (backend: IQuantumBackend) // ✅ RULE1: Backend required
        : Async<QuantumResult<float>> =

        async {
            let numQubits = functionOracle.QubitCount

            // Create uniform superposition over domain
            let statePrep =
                [ 0 .. numQubits - 1 ]
                |> List.fold
                    (fun c q -> c |> CircuitBuilder.addGate (CircuitBuilder.H q))
                    (CircuitBuilder.empty numQubits)

            // Estimate probability that oracle marks state
            let! prob = estimateProbability statePrep functionOracle precision backend

            // Scale by domain width
            return
                prob
                |> Result.map (fun p ->
                    let (a, b) = domain
                    (b - a) * p)
        }
