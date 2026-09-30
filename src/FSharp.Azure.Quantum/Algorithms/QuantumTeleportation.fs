namespace FSharp.Azure.Quantum.Algorithms

open System
open System.Numerics
open FSharp.Azure.Quantum
open FSharp.Azure.Quantum.CircuitBuilder
open FSharp.Azure.Quantum.Core
open FSharp.Azure.Quantum.Core.BackendAbstraction
open FSharp.Azure.Quantum.LocalSimulator

/// Quantum Teleportation Protocol
///
/// Quantum teleportation transfers an unknown quantum state from one qubit (Alice)
/// to another qubit (Bob) using a pre-shared entangled Bell pair and classical communication.
///
/// **IMPORTANT**: Despite the name, teleportation:
/// - Does NOT transfer matter or energy
/// - Does NOT violate speed of light (requires classical communication)
/// - DOES destroy the original state (no-cloning theorem)
/// - IS a verified quantum protocol (demonstrated experimentally since 1997)
///
/// **Production Use Cases**:
/// - Quantum Networks (distribute quantum states between nodes)
/// - Quantum Repeaters (extend quantum communication range)
/// - Distributed Quantum Computing (move quantum data between processors)
/// - Quantum Key Distribution (enhanced security protocols)
///
/// **Real-World Deployments**:
/// - Micius satellite (1400 km teleportation, 2017)
/// - USTC China (143 km fiber, 2012)
/// - Delft quantum network experiments (2022)
/// - Target for quantum internet infrastructure (2030+)
///
/// **Textbook References**:
/// - Nielsen & Chuang "Quantum Computation and Quantum Information" - Section 1.3.7
/// - "Learn Quantum Computing with Python and Q#" (Kaiser, 2021) - Chapter 8
/// - "Quantum Programming in Depth" (Manning, 2024) - Chapter 10
///
/// **Protocol Steps**:
/// 1. Alice and Bob share entangled Bell pair (|Φ⁺⟩)
/// 2. Alice entangles her unknown qubit with her half of Bell pair (CNOT, H)
/// 3. Alice measures her two qubits (gets 2 classical bits)
/// 4. Alice sends classical bits to Bob
/// 5. Bob applies corrections based on classical bits (X, Z gates)
/// 6. Bob now has original state (Alice's state destroyed)
///
/// **Resource Requirements**:
/// - Qubits: 3 (Alice's state, Alice's Bell qubit, Bob's Bell qubit)
/// - Gates: 4 (1 CNOT, 1 H, up to 2 corrections)
/// - Classical bits: 2
/// - Pre-shared entanglement: 1 Bell pair
module QuantumTeleportation =

    // ========================================================================
    // TYPES
    // ========================================================================

    /// Measurement outcome from Alice's Bell measurement
    ///
    /// Alice measures her two qubits (unknown state + her Bell qubit)
    /// The outcome determines what corrections Bob must apply
    [<Struct>]
    type AliceMeasurement =
        {
            /// First measurement bit (from Alice's unknown qubit after CNOT)
            Bit0: int

            /// Second measurement bit (from Alice's Bell qubit after H)
            Bit1: int
        }

    /// Corrections Bob must apply based on Alice's measurement
    type BobCorrection =
        /// No correction needed (00 measurement)
        | NoCorrection

        /// Apply X gate (01 measurement)
        | ApplyX

        /// Apply Z gate (10 measurement)
        | ApplyZ

        /// Apply both Z and X gates (11 measurement)
        | ApplyZX

    /// Complete teleportation protocol result
    type TeleportationResult =
        {
            /// Alice's measurement outcome
            AliceMeasurement: AliceMeasurement

            /// Correction applied by Bob
            BobCorrection: BobCorrection

            /// Bob's final quantum state (should match original input state)
            BobState: QuantumState

            /// Total number of qubits used (always 3)
            NumQubits: int

            /// Backend used
            BackendName: string

            /// Success probability (theoretical = 1.0, NISQ < 1.0)
            Fidelity: float
        }

    // ========================================================================
    // HELPER FUNCTIONS
    // ========================================================================

    /// Determine Bob's correction based on Alice's measurement
    ///
    /// Mapping:
    /// - 00 → No correction (state already correct)
    /// - 01 → Apply X (bit flip)
    /// - 10 → Apply Z (phase flip)
    /// - 11 → Apply ZX (both flips)
    let getCorrection (measurement: AliceMeasurement) : BobCorrection =
        match (measurement.Bit0, measurement.Bit1) with
        | (0, 0) -> NoCorrection
        | (0, 1) -> ApplyX
        | (1, 0) -> ApplyZ
        | (1, 1) -> ApplyZX
        | _ -> NoCorrection // Shouldn't happen

    /// Apply Bob's correction to his qubit
    let applyCorrection
        (correction: BobCorrection)
        (bobQubitIndex: int)
        (state: QuantumState)
        (backend: IQuantumBackend)
        : Result<QuantumState, QuantumError> =

        match correction with
        | NoCorrection -> Ok state

        | ApplyX -> backend.ApplyOperation (QuantumOperation.Gate(X bobQubitIndex)) state

        | ApplyZ -> backend.ApplyOperation (QuantumOperation.Gate(Z bobQubitIndex)) state

        | ApplyZX ->
            result {
                // Apply Z first, then X
                let! afterZ = backend.ApplyOperation (QuantumOperation.Gate(Z bobQubitIndex)) state
                let! afterX = backend.ApplyOperation (QuantumOperation.Gate(X bobQubitIndex)) afterZ
                return afterX
            }

    // ========================================================================
    // INTENT → PLAN → EXECUTION (ADR: intent-first algorithms)
    // ========================================================================

    [<Struct>]
    type private TeleportationIntent =
        {
            AliceInputQubit: int
            AliceBellQubit: int
            BobBellQubit: int
        }

    [<RequireQualifiedAccess>]
    type private TeleportationPlan =
        | ExecuteViaOps of
            bellOps: QuantumOperation list *
            aliceOps: QuantumOperation list *
            correctionOps: QuantumOperation list

    let private buildBellOps (intent: TeleportationIntent) : QuantumOperation list =
        [
            QuantumOperation.Gate(H intent.AliceBellQubit)
            QuantumOperation.Gate(CNOT(intent.AliceBellQubit, intent.BobBellQubit))
        ]

    let private buildAliceOps (intent: TeleportationIntent) : QuantumOperation list =
        [
            QuantumOperation.Gate(CNOT(intent.AliceInputQubit, intent.AliceBellQubit))
            QuantumOperation.Gate(H intent.AliceInputQubit)
        ]

    /// Deferred-measurement implementation of the classical corrections.
    ///
    /// This is unitary-equivalent to:
    /// - measure Alice bits (q0, q1)
    /// - apply X if Bit1=1, Z if Bit0=1
    let private buildCorrectionOps (intent: TeleportationIntent) : QuantumOperation list =
        [
            // X correction controlled by Alice's bell qubit (q1)
            QuantumOperation.Gate(CNOT(intent.AliceBellQubit, intent.BobBellQubit))
            // Z correction controlled by Alice's input qubit (q0)
            QuantumOperation.Gate(CZ(intent.AliceInputQubit, intent.BobBellQubit))
        ]

    let private plan
        (backend: IQuantumBackend)
        (intent: TeleportationIntent)
        : Result<TeleportationPlan, QuantumError> =
        // Teleportation requires standard gate-based operations (H, CNOT, CZ) and state-vector inspection for fidelity.
        // Some backends (e.g., annealing) cannot support this, and we should fail explicitly.
        match backend.NativeStateType with
        | QuantumStateType.Annealing ->
            Error(
                QuantumError.OperationError(
                    "QuantumTeleportation",
                    $"Backend '{backend.Name}' does not support quantum teleportation (native state type: {backend.NativeStateType})"
                )
            )
        | _ ->
            // Today we always lower to gates.
            // Future: add `QuantumOperation.Algorithm (AlgorithmOperation.Teleportation ...)` if/when supported.
            let bellOps = buildBellOps intent
            let aliceOps = buildAliceOps intent
            let correctionOps = buildCorrectionOps intent

            let requiredOps = bellOps @ aliceOps @ correctionOps

            if requiredOps |> List.forall backend.SupportsOperation then
                Ok(TeleportationPlan.ExecuteViaOps(bellOps, aliceOps, correctionOps))
            else
                Error(
                    QuantumError.OperationError(
                        "QuantumTeleportation",
                        $"Backend '{backend.Name}' does not support required operations for quantum teleportation"
                    )
                )

    let private executePlan
        (backend: IQuantumBackend)
        (state: QuantumState)
        (plan: TeleportationPlan)
        : Result<QuantumState * QuantumState, QuantumError> =

        // Returns: (afterAliceOps, correctedState)
        match plan with
        | TeleportationPlan.ExecuteViaOps(bellOps, aliceOps, correctionOps) ->
            result {
                let! afterBell = UnifiedBackend.applySequence backend bellOps state
                let! afterAlice = UnifiedBackend.applySequence backend aliceOps afterBell
                let! corrected = UnifiedBackend.applySequence backend correctionOps afterAlice
                return (afterAlice, corrected)
            }

    // ========================================================================
    // STATE INSPECTION HELPERS (Gate-based / simulator only)
    // ========================================================================

    let private tryAsStateVector (state: QuantumState) : Result<StateVector.StateVector, QuantumError> =
        match state with
        | QuantumState.StateVector sv -> Ok sv
        | _ ->
            Error(
                QuantumError.OperationError(
                    "QuantumTeleportation",
                    "State inspection requires a gate-based StateVector"
                )
            )

    let private normalizeVec (v0: Complex) (v1: Complex) : Complex * Complex =
        let normSquared = v0.Magnitude * v0.Magnitude + v1.Magnitude * v1.Magnitude

        if normSquared < 1e-16 then
            (Complex.Zero, Complex.Zero)
        else
            let invNorm = 1.0 / sqrt normSquared
            (v0 * invNorm, v1 * invNorm)

    /// Extract Alice's input qubit pure state amplitudes α|0⟩+β|1⟩ from a 3-qubit state.
    ///
    /// Requires the other qubits to be |00⟩ (as in our prepared input states).
    let private extractInputQubitState (inputState: QuantumState) : Result<Complex * Complex, QuantumError> =
        result {
            let! sv = tryAsStateVector inputState

            // Our convention: basisIndex bits are little-endian by qubit index.
            // |q0 q1 q2⟩ = |b0 b1 b2⟩ corresponds to basisIndex = b0 + 2*b1 + 4*b2
            let alpha = StateVector.getAmplitude 0 sv // |000⟩
            let beta = StateVector.getAmplitude 1 sv // |100⟩ (q0=1)

            let (aN, bN) = normalizeVec alpha beta
            return (aN, bN)
        }

    /// Reduced density matrix of a single qubit (2x2) from 3-qubit pure state.
    ///
    /// ρ_b = Tr_{others}(|ψ⟩⟨ψ|). Returns (ρ00, ρ01, ρ10, ρ11).
    let private reducedDensityMatrixSingleQubit
        (targetQubit: int)
        (state: QuantumState)
        : Result<Complex * Complex * Complex * Complex, QuantumError> =

        // Keep this function free of the `result {}` computation expression
        // because our ResultBuilder does not implement `For`.
        match tryAsStateVector state with
        | Error err -> Error err
        | Ok sv ->
            let n = StateVector.numQubits sv

            if n <> 3 then
                Error(QuantumError.ValidationError("state", $"Expected 3 qubits, got {n}"))
            elif targetQubit < 0 || targetQubit >= n then
                Error(QuantumError.ValidationError("targetQubit", $"Index out of range: {targetQubit}"))
            else
                let dim = 1 <<< n

                // Partial trace: sum over all basis indices i,j with same "other" bits; only target bit differs.
                let (rho00, rho01, rho10, rho11) =
                    seq {
                        for i in 0 .. dim - 1 do
                            let iBit = (i >>> targetQubit) &&& 1
                            let iOther = i &&& (~~~(1 <<< targetQubit))
                            let ampI = StateVector.getAmplitude i sv

                            for j in 0 .. dim - 1 do
                                let jBit = (j >>> targetQubit) &&& 1
                                let jOther = j &&& (~~~(1 <<< targetQubit))

                                if iOther = jOther then
                                    let ampJ = StateVector.getAmplitude j sv
                                    let term = ampI * Complex.Conjugate ampJ
                                    (iBit, jBit, term)
                    }
                    |> Seq.fold
                        (fun (r00, r01, r10, r11) (iBit, jBit, term) ->
                            match (iBit, jBit) with
                            | 0, 0 -> (r00 + term, r01, r10, r11)
                            | 0, 1 -> (r00, r01 + term, r10, r11)
                            | 1, 0 -> (r00, r01, r10 + term, r11)
                            | 1, 1 -> (r00, r01, r10, r11 + term)
                            | _ -> (r00, r01, r10, r11))
                        (Complex.Zero, Complex.Zero, Complex.Zero, Complex.Zero)

                Ok(rho00, rho01, rho10, rho11)

    /// Fidelity F(|ψ⟩, ρ) = ⟨ψ|ρ|ψ⟩ between a pure 1-qubit state and a 1-qubit density matrix.
    let private pureStateDensityFidelity
        (alpha: Complex, beta: Complex)
        (rho00: Complex, rho01: Complex, rho10: Complex, rho11: Complex)
        : float =

        // ⟨ψ|ρ|ψ⟩ = [α* β*] [ρ00 ρ01; ρ10 ρ11] [α; β]
        let aConj = Complex.Conjugate alpha
        let bConj = Complex.Conjugate beta

        let v0 = rho00 * alpha + rho01 * beta
        let v1 = rho10 * alpha + rho11 * beta
        let result = aConj * v0 + bConj * v1

        // Numerical rounding can introduce tiny imaginary component
        max 0.0 (min 1.0 result.Real)

    /// Format teleportation result for display
    let formatResult (result: TeleportationResult) : string =
        let correctionStr =
            match result.BobCorrection with
            | NoCorrection -> "None (00)"
            | ApplyX -> "X gate (01)"
            | ApplyZ -> "Z gate (10)"
            | ApplyZX -> "Z+X gates (11)"

        sprintf
            "Quantum Teleportation Result:\n\
             Alice's Measurement: %d%d\n\
             Bob's Correction: %s\n\
             Fidelity: %.2f%%\n\
             Qubits: %d\n\
             Backend: %s"
            result.AliceMeasurement.Bit0
            result.AliceMeasurement.Bit1
            correctionStr
            (result.Fidelity * 100.0)
            result.NumQubits
            result.BackendName

    // ========================================================================
    // WHOLE-CIRCUIT TELEPORTATION (backends that run complete circuits only)
    // ========================================================================

    /// Outcome distribution of qubits offset .. offset + 2 of a whole-circuit result, indexed
    /// with bit k = qubit offset + k. A job's result gives its recorded counts over the shots
    /// it measured; a computed state gives its exact marginal probabilities.
    let private copyDistribution (offset: int) (state: QuantumState) : float[] =
        let distribution = Array.zeroCreate 8

        match UnifiedBackend.recordedShots state with
        | Some shots when shots.Length > 0 ->
            for bits in shots do
                let index = bits.[offset] + 2 * bits.[offset + 1] + 4 * bits.[offset + 2]
                distribution.[index] <- distribution.[index] + 1.0 / float shots.Length
        | _ ->
            let n = QuantumState.numQubits state

            for index in 0 .. (1 <<< n) - 1 do
                let bits = Array.init n (fun q -> (index >>> q) &&& 1)
                let local = bits.[offset] + 2 * bits.[offset + 1] + 4 * bits.[offset + 2]
                distribution.[local] <- distribution.[local] + QuantumState.probability bits state

        distribution

    /// ⟨σ⟩ of Bob's qubit (bit 2) from a copy's distribution: P(q2 = 0) − P(q2 = 1).
    let private expectationOnBob (distribution: float[]) : float =
        [ 0..7 ] |> List.sumBy (fun index -> if index &&& 4 = 0 then distribution.[index] else -distribution.[index])

    /// Teleportation of the state `prepOps` make on qubit 0, run as ONE whole circuit.
    ///
    /// The corrections are already deferred-measurement gates (CNOT, CZ), so the protocol
    /// is one circuit and Alice's bits are the final measurement of q0 and q1 — measuring
    /// them at the end gives the same distribution as measuring them mid-circuit. Counts give
    /// no state vector, so fidelity is estimated by tomography of Bob's qubit in the Z, X and
    /// Y bases, and F = (1 + r_in·r)/2 for the measured Bloch vector r and the input's r_in.
    /// The three tomography settings are independent copies of the protocol side by side in
    /// one 9-qubit circuit (qubits 0–2 measured in Z, 3–5 in X, 6–8 in Y), so every shot
    /// informs all three expectations and a call costs one job, not three.
    let private teleportWholeCircuit
        (prepOps: QuantumOperation list)
        (backend: IQuantumBackend)
        : Result<TeleportationResult, QuantumError> =

        result {
            let intent =
                {
                    AliceInputQubit = 0
                    AliceBellQubit = 1
                    BobBellQubit = 2
                }

            let! teleportPlan = plan backend intent

            let protocolOps =
                match teleportPlan with
                | TeleportationPlan.ExecuteViaOps(bellOps, aliceOps, correctionOps) ->
                    prepOps @ bellOps @ aliceOps @ correctionOps

            let bob = intent.BobBellQubit
            let! xCopy = WholeCircuit.shiftOps 3 (protocolOps @ [ QuantumOperation.Gate(H bob) ])

            let! yCopy =
                WholeCircuit.shiftOps 6 (protocolOps @ [ QuantumOperation.Gate(SDG bob); QuantumOperation.Gate(H bob) ])

            let! joint = UnifiedBackend.submitAsCircuit backend 9 (protocolOps @ xCopy @ yCopy)

            let zDistribution = copyDistribution 0 joint

            let measured =
                (expectationOnBob (copyDistribution 3 joint),
                 expectationOnBob (copyDistribution 6 joint),
                 expectationOnBob zDistribution)

            // The input's Bloch vector is the reference the fidelity is measured against: the
            // state the caller asked to teleport, described exactly, not a result.
            let reference =
                FSharp.Azure.Quantum.Backends.LocalBackend.LocalBackend() :> IQuantumBackend

            let! referenceState =
                reference.InitializeState 3
                |> Result.bind (UnifiedBackend.applySequence reference prepOps)

            let! (alpha, beta) = extractInputQubitState referenceState
            let overlap = Complex.Conjugate alpha * beta

            let inputBloch =
                (2.0 * overlap.Real,
                 2.0 * overlap.Imaginary,
                 alpha.Magnitude * alpha.Magnitude - beta.Magnitude * beta.Magnitude)

            let dot (x1, y1, z1) (x2, y2, z2) = x1 * x2 + y1 * y2 + z1 * z2
            let fidelity = max 0.0 (min 1.0 ((1.0 + dot inputBloch measured) / 2.0))

            // The Z copy on its own, as the 3-qubit state of the protocol: its recorded shots
            // when the job recorded them, otherwise its phase-less marginal (all a whole
            // circuit can report either way).
            let bobState =
                match UnifiedBackend.recordedShots joint with
                | Some shots when shots.Length > 0 ->
                    shots
                    |> Array.countBy (fun bits -> String(Array.init 3 (fun q -> if bits.[2 - q] = 1 then '1' else '0')))
                    |> Map.ofArray
                    |> fun counts -> FSharp.Azure.Quantum.Backends.CloudBackendHelpers.histogramToQuantumState counts 3
                | _ ->
                    zDistribution
                    |> Array.map (fun p -> Complex(sqrt p, 0.0))
                    |> StateVector.create
                    |> QuantumState.StateVector

            // Alice's bits: one shot of the Z-basis copy.
            let bits = QuantumState.measure bobState 1 |> Array.head

            let aliceMeasurement =
                {
                    Bit0 = bits.[intent.AliceInputQubit]
                    Bit1 = bits.[intent.AliceBellQubit]
                }

            return
                {
                    AliceMeasurement = aliceMeasurement
                    BobCorrection = getCorrection aliceMeasurement
                    BobState = bobState
                    NumQubits = 3
                    BackendName = backend.Name
                    Fidelity = fidelity
                }
        }

    // ========================================================================
    // TELEPORTATION PROTOCOL
    // ========================================================================

    /// Execute quantum teleportation protocol
    ///
    /// **Circuit Layout**:
    ///   q0 (Alice's input):  ─────●─H─M─┬─────
    ///   q1 (Alice's Bell):   ─H─●─┼───M─┼─────
    ///   q2 (Bob's Bell):     ───X─┼─────┼─X?─Z?
    ///                                    │  │
    ///                           classical bits
    ///
    /// **Steps**:
    /// 1. Prepare input state on qubit 0 (Alice's qubit)
    /// 2. Create Bell pair on qubits 1-2 (Alice gets q1, Bob gets q2)
    /// 3. Alice entangles her input with her Bell qubit (CNOT, H)
    /// 4. Alice measures both her qubits (q0, q1)
    /// 5. Bob applies corrections to his qubit (q2) based on measurements
    /// 6. Bob's qubit now holds original state (Alice's state destroyed)
    ///
    /// **Parameters**:
    ///   inputState - The quantum state Alice wants to teleport (must be 3 qubits)
    ///   backend - Quantum backend to execute on
    ///
    /// **Returns**:
    ///   TeleportationResult with Bob's final state and measurement outcomes
    ///
    /// **Notes**:
    /// - Input state MUST have exactly 3 qubits
    /// - Input state MUST have desired state on qubit 0 (Alice's input qubit)
    /// - Measurements are extracted from actual quantum state (not hardcoded)
    /// - All 4 measurement outcomes (00, 01, 10, 11) are possible
    /// - Fidelity depends on Bell pair quality and gate fidelity
    let rec teleport (inputState: QuantumState) (backend: IQuantumBackend) : Result<TeleportationResult, QuantumError> =
        match teleportIncremental inputState backend with
        | Error e when UnifiedBackend.isIncrementalUnsupported e ->
            // A backend that runs complete circuits only. A job starts from |0…0⟩ and cannot
            // be handed a prepared state, so only the |0⟩ input can be teleported this way;
            // teleportOne, teleportPlus and teleportMinus prepare theirs inside the circuit.
            if UnifiedBackend.isZeroState inputState then
                teleportWholeCircuit [] backend
            else
                Error(WholeCircuit.notFromZeroError "QuantumTeleportation")
        | other -> other

    /// Teleportation applied gate by gate to `inputState`.
    and private teleportIncremental
        (inputState: QuantumState)
        (backend: IQuantumBackend)
        : Result<TeleportationResult, QuantumError> =

        result {
            let intent =
                {
                    AliceInputQubit = 0
                    AliceBellQubit = 1
                    BobBellQubit = 2
                }

            // Validate input state has exactly 3 qubits
            let numQubits = QuantumState.numQubits inputState

            do!
                if numQubits <> 3 then
                    Error(QuantumError.ValidationError("inputState", $"requires exactly 3 qubits, got {numQubits}"))
                else
                    Ok()

            // Plan and execute
            let! teleportPlan = plan backend intent
            let! (afterAliceOps, correctedState) = executePlan backend inputState teleportPlan

            // Alice's classical bits (for reporting)
            //
            // We intentionally sample from the post-H state without collapsing it.
            // The teleportation *unitary* can be implemented with deferred measurement:
            // the same correction effect is achieved by controlled operations.
            let bits = QuantumState.measure afterAliceOps 1 |> Array.head

            let aliceMeasurement =
                {
                    Bit0 = bits.[intent.AliceInputQubit]
                    Bit1 = bits.[intent.AliceBellQubit]
                }

            // Determine Bob's correction (based on sampled bits)
            let correction = getCorrection aliceMeasurement

            // Calculate fidelity (simulator-capable backends)
            let! (alpha, beta) = extractInputQubitState inputState

            let! (rho00, rho01, rho10, rho11) =
                reducedDensityMatrixSingleQubit intent.BobBellQubit correctedState

            let fidelity = pureStateDensityFidelity (alpha, beta) (rho00, rho01, rho10, rho11)

            return
                {
                    AliceMeasurement = aliceMeasurement
                    BobCorrection = correction
                    BobState = correctedState
                    NumQubits = 3
                    BackendName = backend.Name
                    Fidelity = fidelity
                }
        }

    // ========================================================================
    // CONVENIENCE FUNCTIONS
    // ========================================================================

    /// Teleport the state `prepOps` make on qubit 0 from |000⟩. Gate by gate where the backend
    /// allows it; a backend that runs complete circuits only gets the preparation inside the
    /// teleportation circuit.
    let private teleportPrepared
        (prepOps: QuantumOperation list)
        (backend: IQuantumBackend)
        : Result<TeleportationResult, QuantumError> =
        result {
            let! initialState = backend.InitializeState 3

            match UnifiedBackend.applySequence backend prepOps initialState with
            | Ok prepared -> return! teleport prepared backend
            | Error e when UnifiedBackend.isIncrementalUnsupported e -> return! teleportWholeCircuit prepOps backend
            | Error e -> return! Error e
        }

    /// Teleport a |0⟩ state (trivial test case)
    ///
    /// This is the simplest teleportation scenario:
    /// - Input: |0⟩
    /// - Expected: Alice measures 00, Bob applies no correction
    /// - Result: Bob has |0⟩
    let teleportZero (backend: IQuantumBackend) : Result<TeleportationResult, QuantumError> =
        result {
            // Prepare 3-qubit state with |000⟩
            let! initialState = backend.InitializeState 3

            // Teleport (qubit 0 is already |0⟩)
            return! teleport initialState backend
        }

    /// Teleport a |1⟩ state
    ///
    /// Input: |1⟩ on qubit 0
    /// Expected outcomes vary based on measurement
    let teleportOne (backend: IQuantumBackend) : Result<TeleportationResult, QuantumError> =
        teleportPrepared [ QuantumOperation.Gate(X 0) ] backend

    /// Teleport a |+⟩ state (superposition)
    ///
    /// Input: |+⟩ = (|0⟩ + |1⟩) / √2 on qubit 0
    /// This tests teleportation of superposition states
    let teleportPlus (backend: IQuantumBackend) : Result<TeleportationResult, QuantumError> =
        teleportPrepared [ QuantumOperation.Gate(H 0) ] backend

    /// Teleport a |-⟩ state (superposition with phase)
    ///
    /// Input: |-⟩ = (|0⟩ - |1⟩) / √2 on qubit 0
    /// This tests teleportation with relative phase
    let teleportMinus (backend: IQuantumBackend) : Result<TeleportationResult, QuantumError> =
        teleportPrepared [ QuantumOperation.Gate(H 0); QuantumOperation.Gate(Z 0) ] backend

    /// Teleport arbitrary single-qubit state
    ///
    /// **Usage**:
    /// 1. Prepare your state on a single-qubit backend
    /// 2. Embed it on qubit 0 of a 3-qubit system
    /// 3. Call this function to teleport
    ///
    /// **Parameters**:
    ///   state - 3-qubit state with input on qubit 0
    ///   backend - Quantum backend to execute on
    ///
    /// **Returns**:
    ///   TeleportationResult with Bob's final state
    ///
    /// A backend that runs complete circuits only cannot be handed a prepared state, so there
    /// this is an Error unless the state is |000⟩; teleportOne, teleportPlus and teleportMinus
    /// prepare their inputs inside the circuit.
    let teleportArbitrary (state: QuantumState) (backend: IQuantumBackend) : Result<TeleportationResult, QuantumError> =
        teleport state backend

    // ========================================================================
    // VERIFICATION
    // ========================================================================

    /// Verify teleportation fidelity by comparing input and output states
    ///
    /// **Note**: This requires state vector inspection which is not available
    /// on real quantum hardware (measurement destroys the state).
    ///
    /// This is primarily useful for:
    /// - Simulator validation
    /// - Algorithm debugging
    /// - Fidelity estimation for NISQ hardware
    ///
    /// **Parameters**:
    ///   inputState - Original state on qubit 0 (before teleportation)
    ///   outputState - Bob's state on qubit 2 (after teleportation)
    ///   backend - Quantum backend (must support state inspection)
    ///
    /// **Returns**:
    ///   Fidelity between 0.0 (orthogonal) and 1.0 (identical)
    let verifyFidelity
        (inputState: QuantumState)
        (outputState: QuantumState)
        (_backend: IQuantumBackend)
        : Result<float, QuantumError> =

        result {
            // We treat `inputState` as having the input qubit on q0.
            // We compare it against Bob's reduced 1-qubit state on q2 from `outputState`.
            let! (alpha, beta) = extractInputQubitState inputState
            let! (rho00, rho01, rho10, rho11) = reducedDensityMatrixSingleQubit 2 outputState
            return pureStateDensityFidelity (alpha, beta) (rho00, rho01, rho10, rho11)
        }

    // ========================================================================
    // STATISTICS & ANALYSIS
    // ========================================================================

    /// Run teleportation multiple times and collect statistics
    ///
    /// Useful for analyzing NISQ hardware performance:
    /// - Distribution of Alice's measurements
    /// - Distribution of Bob's corrections
    /// - Average fidelity
    ///
    /// **Parameters**:
    ///   prepareInput - Function to prepare input state
    ///   backend - Quantum backend to execute on
    ///   trials - Number of teleportation runs
    ///
    /// **Returns**:
    ///   List of teleportation results
    let runStatistics
        (prepareInput: IQuantumBackend -> Result<QuantumState, QuantumError>)
        (backend: IQuantumBackend)
        (trials: int)
        : Result<TeleportationResult list, QuantumError> =

        if trials < 1 then
            Error(QuantumError.ValidationError("trials", "must be at least 1"))
        else
            // Run trials sequentially using List.fold pattern
            let trialsList = [ 1..trials ]

            (Ok [], trialsList)
            ||> List.fold (fun resultsResult _ ->
                result {
                    let! results = resultsResult
                    let! inputState = prepareInput backend
                    let! teleportResult = teleport inputState backend
                    return teleportResult :: results
                })
            |> Result.map List.rev

    /// Analyze teleportation statistics
    ///
    /// Computes:
    /// - Average fidelity
    /// - Distribution of measurement outcomes (00, 01, 10, 11)
    /// - Distribution of corrections (None, X, Z, ZX)
    ///
    /// **Parameters**:
    ///   results - List of teleportation results from runStatistics
    ///
    /// **Returns**:
    ///   Summary statistics string
    let analyzeStatistics (results: TeleportationResult list) : string =
        if List.isEmpty results then
            "No results to analyze"
        else
            let totalTrials = List.length results

            // Average fidelity
            let avgFidelity = results |> List.averageBy (fun r -> r.Fidelity)

            // Count measurement outcomes
            let count00 =
                results
                |> List.filter (fun r -> r.AliceMeasurement.Bit0 = 0 && r.AliceMeasurement.Bit1 = 0)
                |> List.length

            let count01 =
                results
                |> List.filter (fun r -> r.AliceMeasurement.Bit0 = 0 && r.AliceMeasurement.Bit1 = 1)
                |> List.length

            let count10 =
                results
                |> List.filter (fun r -> r.AliceMeasurement.Bit0 = 1 && r.AliceMeasurement.Bit1 = 0)
                |> List.length

            let count11 =
                results
                |> List.filter (fun r -> r.AliceMeasurement.Bit0 = 1 && r.AliceMeasurement.Bit1 = 1)
                |> List.length

            sprintf
                "Teleportation Statistics (%d trials):\n\
                 ═══════════════════════════════════════\n\
                 Average Fidelity: %.2f%%\n\
                 \n\
                 Measurement Distribution:\n\
                 - 00 (No correction):  %3d (%.1f%%)\n\
                 - 01 (X correction):   %3d (%.1f%%)\n\
                 - 10 (Z correction):   %3d (%.1f%%)\n\
                 - 11 (ZX correction):  %3d (%.1f%%)"
                totalTrials
                (avgFidelity * 100.0)
                count00
                ((float count00 / float totalTrials) * 100.0)
                count01
                ((float count01 / float totalTrials) * 100.0)
                count10
                ((float count10 / float totalTrials) * 100.0)
                count11
                ((float count11 / float totalTrials) * 100.0)
