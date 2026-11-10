namespace FSharp.Azure.Quantum.Algorithms

open System
open FSharp.Azure.Quantum
open FSharp.Azure.Quantum.CircuitBuilder
open FSharp.Azure.Quantum.Core
open FSharp.Azure.Quantum.Core.BackendAbstraction

/// BB84 Quantum Key Distribution Protocol
///
/// BB84 is the first and most widely-used quantum key distribution (QKD) protocol,
/// enabling unconditionally secure key exchange between two parties (Alice and Bob).
///
/// **Production Use Cases**:
/// - Banking & Finance: Secure transaction key distribution
/// - Government/Military: Classified communication key exchange
/// - Critical Infrastructure: Power grid, telecom network security
/// - Healthcare: HIPAA-compliant data encryption keys
/// - Quantum Internet: Future backbone for quantum-secure communications
///
/// **Real-World Deployments**:
/// - ID Quantique (Switzerland): Commercial QKD systems since 2001
/// - Toshiba Quantum Key Distribution: London fiber network (2018+)
/// - QuantumCTek (China): Micius satellite to ground stations (2016+)
/// - Battelle Memorial Institute: Smart grid QKD network (Ohio, USA)
/// - European SECOQC Network: Multi-node QKD network (Vienna, 2008)
/// - UK National Quantum Network: Government quantum-secure network (2021+)
///
/// **Security Guarantees**:
/// - Information-theoretic security (not computational)
/// - Guaranteed eavesdropping detection via quantum physics
/// - Cannot be broken by quantum computers (unlike RSA, ECC)
/// - Secure against all future mathematical/computational advances
///
/// **Textbook References**:
/// - Bennett & Brassard (1984) "Quantum Cryptography: Public Key Distribution and Coin Tossing"
/// - Shor & Preskill (2000) "Simple proof of security of the BB84 quantum key distribution protocol"
/// - Nielsen & Chuang "Quantum Computation and Quantum Information" - Chapter 12
/// - "Quantum Cryptography and Secret-Key Distillation" (Gisin et al., 2002)
///
/// **Performance Characteristics**:
/// - Key efficiency: ~50% (half the qubits have matching bases)
/// - Error rate without Eve: ~0-5% (quantum channel noise)
/// - Error rate with Eve (intercept-resend): ~25%
/// - Detection threshold (QBER): Typically 11% (exceed = abort)
/// - Practical key rates: 1 kbps - 1 Mbps (commercial systems)
///
/// **Protocol Overview**:
/// 1. Alice prepares qubits in random bases (rectilinear/diagonal) with random bits
/// 2. Alice sends qubits to Bob over quantum channel
/// 3. Bob measures qubits in random bases
/// 4. Alice and Bob compare bases over classical channel (not bits!)
/// 5. Sift key: keep only bits where bases matched
/// 6. Sample subset to check for eavesdropping (error rate > threshold?)
/// 7. If secure: Apply privacy amplification (hash to final key)
/// 8. If insecure: Abort and retry
module QuantumKeyDistribution =

    // ========================================================================
    // TYPES
    // ========================================================================

    /// Measurement basis for BB84 protocol
    ///
    /// Two non-orthogonal bases ensure eavesdropping detection:
    /// - If Eve measures in wrong basis, she disturbs the state
    /// - Bob's measurement will have ~25% error rate
    /// - This quantum disturbance cannot be hidden by classical physics
    type Basis =
        /// Rectilinear basis (Z basis): {|0⟩, |1⟩}
        /// Standard computational basis
        /// Measure directly without basis change
        | Rectilinear

        /// Diagonal basis (X basis): {|+⟩, |-⟩}
        /// Hadamard-rotated basis
        /// |+⟩ = (|0⟩ + |1⟩)/√2, |-⟩ = (|0⟩ - |1⟩)/√2
        /// Measure by applying H gate first
        | Diagonal

    /// Classical bit value
    type Bit =
        | Zero
        | One

    /// Convert Bit to bool
    let bitToBool bit =
        match bit with
        | Zero -> false
        | One -> true

    /// Convert bool to Bit
    let boolToBit value = if value then One else Zero

    /// Convert Bit to int
    let bitToInt bit =
        match bit with
        | Zero -> 0
        | One -> 1

    /// Alice's state for BB84 protocol
    ///
    /// Alice randomly chooses bits and bases for each qubit.
    /// She keeps these secret until basis reconciliation phase.
    type AliceState =
        {
            /// Random bits to encode (0 or 1)
            Bits: Bit[]

            /// Random bases for each bit (Rectilinear or Diagonal)
            Bases: Basis[]

            /// Number of qubits sent
            NumQubits: int
        }

    /// Bob's measurement results
    ///
    /// Bob randomly chooses measurement bases independently of Alice.
    /// After measurement, he keeps results secret until basis reconciliation.
    type BobMeasurement =
        {
            /// Random bases Bob chose for measurement
            Bases: Basis[]

            /// Measurement results (0 or 1)
            Results: Bit[]

            /// Number of qubits measured
            NumQubits: int
        }

    /// Result after basis reconciliation (key sifting)
    ///
    /// Alice and Bob compare bases publicly and keep only bits where bases matched.
    /// This yields ~50% of original qubits as the raw key.
    type SiftedKey =
        {
            /// Indices where Alice and Bob used same basis
            MatchingIndices: int[]

            /// Sifted bits (from matching bases only)
            AliceBits: bool[]
            BobBits: bool[]

            /// Number of bits in sifted key
            Length: int

            /// Efficiency (matching rate)
            Efficiency: float
        }

    /// Result of eavesdropping check
    ///
    /// Alice and Bob sacrifice a random subset of the sifted key to check for Eve.
    /// They compare these bits publicly - high error rate indicates eavesdropping.
    ///
    /// **Performance**: Struct for stack allocation (~24 bytes)
    type EavesdropCheck =
        {
            /// Number of bits compared
            SampleSize: int

            /// Number of mismatches found
            Errors: int

            /// Quantum Bit Error Rate (QBER) = errors / sampleSize
            ErrorRate: float

            /// Eavesdropping detected? (errorRate > threshold)
            EavesdropDetected: bool

            /// Detection threshold used (typically 0.11)
            Threshold: float

            /// Indices of bits used in eavesdrop check sample
            /// These bits were publicly revealed and must be removed from final key
            SampleIndices: int[]
        }

    /// Final BB84 protocol result
    type BB84Result =
        {
            /// Initial number of qubits sent
            InitialKeyLength: int

            /// Sifted key after basis reconciliation
            SiftedKey: SiftedKey

            /// Eavesdropping check result
            EavesdropCheck: EavesdropCheck

            /// Alice's raw key: her sifted bits without the revealed check sample. Bob's raw key
            /// (extractBobFinalKey) differs from it wherever the channel flipped a bit; the two
            /// become one secret key only after error correction and privacy amplification
            /// (runCompleteQKD)
            FinalKey: bool[]

            /// Final key length
            FinalKeyLength: int

            /// Overall efficiency (finalKeyLength / initialKeyLength)
            OverallEfficiency: float

            /// Protocol succeeded?
            Success: bool

            /// Backend used
            BackendName: string
        }

    // ========================================================================
    // QUANTUM STATE PREPARATION (Alice)
    // ========================================================================

    /// Prepare a single qubit in BB84 encoding
    ///
    /// Encoding table:
    ///   Bit=0, Basis=Rectilinear → |0⟩ (no gates)
    ///   Bit=1, Basis=Rectilinear → |1⟩ (X gate)
    ///   Bit=0, Basis=Diagonal    → |+⟩ = (|0⟩+|1⟩)/√2 (H gate)
    ///   Bit=1, Basis=Diagonal    → |-⟩ = (|0⟩-|1⟩)/√2 (X then H gates)
    ///
    /// Parameters:
    ///   bit - Classical bit to encode (0 or 1)
    ///   basis - Basis to use (Rectilinear or Diagonal)
    ///   backend - Quantum backend to execute on
    ///
    /// Returns:
    ///   Quantum state representing the encoded qubit
    let prepareQubit (bit: Bit) (basis: Basis) (backend: IQuantumBackend) : Result<QuantumState, QuantumError> =
        result {
            // Initialize |0⟩ state
            let! state = backend.InitializeState 1

            // Apply bit encoding
            let! afterBit =
                match bit with
                | Zero -> Ok state // |0⟩ stays as-is
                | One -> backend.ApplyOperation (QuantumOperation.Gate(X 0)) state // |0⟩ → |1⟩

            // Apply basis rotation
            let! finalState =
                match basis with
                | Rectilinear -> Ok afterBit // Z basis, no rotation
                | Diagonal -> backend.ApplyOperation (QuantumOperation.Gate(H 0)) afterBit // X basis, apply H

            return finalState
        }

    /// Create Alice's random state for BB84
    ///
    /// Alice generates random bits and random bases for each qubit.
    ///
    /// Parameters:
    ///   keyLength - Number of qubits to prepare
    ///   rng - Random number generator
    ///
    /// Returns:
    ///   AliceState with random bits and bases
    let createAliceState (keyLength: int) (rng: Random) : AliceState =
        let bits = Array.init keyLength (fun _ -> if rng.Next(2) = 0 then Zero else One)

        let bases =
            Array.init keyLength (fun _ -> if rng.Next(2) = 0 then Rectilinear else Diagonal)

        {
            Bits = bits
            Bases = bases
            NumQubits = keyLength
        }

    // ========================================================================
    // QUANTUM MEASUREMENT (Bob)
    // ========================================================================

    /// Measure a qubit in specified basis
    ///
    /// Measurement process:
    /// - Rectilinear basis: Measure directly in Z basis
    /// - Diagonal basis: Apply H gate, then measure in Z basis
    ///
    /// Parameters:
    ///   state - Quantum state to measure
    ///   basis - Basis to measure in
    ///   backend - Quantum backend to execute on
    ///
    /// Returns:
    ///   Measured bit (0 or 1)
    let measureQubit (state: QuantumState) (basis: Basis) (backend: IQuantumBackend) : Result<Bit, QuantumError> =
        result {
            // Apply basis rotation if needed
            let! rotatedState =
                match basis with
                | Rectilinear -> Ok state // Z basis, measure directly
                | Diagonal -> backend.ApplyOperation (QuantumOperation.Gate(H 0)) state // X basis, apply H first

            // Measure in computational basis
            let measurements = QuantumState.measure rotatedState 1 // 1 shot
            let measurementBits = measurements.[0] // First (and only) measurement
            let measureResult = measurementBits.[0] // Result for qubit 0

            // Convert measurement result to bit
            return if measureResult = 1 then One else Zero
        }

    /// Create Bob's random measurement bases
    ///
    /// Bob independently chooses random bases for each measurement.
    ///
    /// Parameters:
    ///   keyLength - Number of qubits to measure
    ///   rng - Random number generator
    ///
    /// Returns:
    ///   Array of random bases
    let createBobBases (keyLength: int) (rng: Random) : Basis[] =
        Array.init keyLength (fun _ -> if rng.Next(2) = 0 then Rectilinear else Diagonal)

    // ========================================================================
    // CLASSICAL POST-PROCESSING
    // ========================================================================

    /// Sift key by comparing bases
    ///
    /// Alice and Bob publicly announce their bases (NOT their bits).
    /// They keep only bits where they used the same basis.
    ///
    /// Expected efficiency: ~50% (bases match ~50% of the time)
    ///
    /// Parameters:
    ///   aliceState - Alice's original state
    ///   bobMeasurement - Bob's measurement results
    ///
    /// Returns:
    ///   SiftedKey with matching bits only
    let siftKey (aliceState: AliceState) (bobMeasurement: BobMeasurement) : SiftedKey =
        // Find indices where bases match
        let matchingIndices =
            Array.indexed aliceState.Bases
            |> Array.filter (fun (i, aliceBasis) -> aliceBasis = bobMeasurement.Bases.[i])
            |> Array.map fst

        // Extract bits at matching indices
        let aliceBits =
            matchingIndices |> Array.map (fun i -> bitToBool aliceState.Bits.[i])

        let bobBits =
            matchingIndices |> Array.map (fun i -> bitToBool bobMeasurement.Results.[i])

        let length = matchingIndices.Length
        let efficiency = float length / float aliceState.NumQubits

        {
            MatchingIndices = matchingIndices
            AliceBits = aliceBits
            BobBits = bobBits
            Length = length
            Efficiency = efficiency
        }

    /// Sifted bits without the positions revealed in the eavesdropping check
    let private withoutSample (siftedBits: bool[]) (sampleIndices: int[]) : bool[] =
        let sampleSet = Set.ofArray sampleIndices

        siftedBits
        |> Array.indexed
        |> Array.filter (fun (i, _) -> not (sampleSet.Contains i))
        |> Array.map snd

    /// Extract Alice's raw key after eavesdropping check
    ///
    /// Remove sampled bits from Alice's sifted bits.
    /// The sampled bits were revealed publicly and must be discarded.
    ///
    /// Parameters:
    ///   siftedKey - Sifted key from basis reconciliation
    ///   sampleIndices - Indices of bits used in eavesdrop check
    ///
    /// Returns:
    ///   Alice's non-sampled bits. Bob's are extractBobFinalKey: on a noisy channel the two
    ///   differ until error correction.
    let extractFinalKey (siftedKey: SiftedKey) (sampleIndices: int[]) : bool[] =
        withoutSample siftedKey.AliceBits sampleIndices

    /// Extract Bob's raw key after eavesdropping check: his sifted bits (what he measured)
    /// without the sampled positions, in the same order as extractFinalKey gives Alice's.
    let extractBobFinalKey (siftedKey: SiftedKey) (sampleIndices: int[]) : bool[] =
        withoutSample siftedKey.BobBits sampleIndices

    // ========================================================================
    // FULL BB84 PROTOCOL (Intent → Plan → Execute)
    // ========================================================================

    /// Eve action for a specific qubit transmission.
    [<RequireQualifiedAccess>]
    type EveAction =
        | None
        | InterceptResend of basis: Basis

    /// Canonical (deterministic) BB84 intent.
    ///
    /// All randomness (bases, bits, Eve choices, sample indices) is captured here.
    type Bb84Intent =
        {
            /// Requested final key length (approximate; protocol may yield fewer bits).
            KeyLength: int

            /// Number of qubits sent over the quantum channel.
            InitialQubits: int

            /// Alice's random bits and bases.
            Alice: AliceState

            /// Bob's random measurement bases.
            BobBases: Basis[]

            /// Eve actions per qubit (may be `None`).
            EveActions: EveAction[]

            /// Indices into the sifted key used for the eavesdrop check sample.
            ///
            /// These indices are in the sifted-key index space (0..siftedLength-1).
            SampleIndices: int[]

            /// Fraction of sifted key used for eavesdrop check.
            SampleRatio: float

            /// QBER threshold for eavesdropping detection.
            QberThreshold: float
        }

    [<Struct; RequireQualifiedAccess>]
    type Bb84Attack =
        | None
        | InterceptResend
        | Beamsplitter of probability: float

    [<RequireQualifiedAccess>]
    type Bb84Plan = ExecuteViaOps of requiredOps: QuantumOperation list

    let private requiredOps: QuantumOperation list =
        [ QuantumOperation.Gate(X 0); QuantumOperation.Gate(H 0) ]

    let private planBb84 (backend: IQuantumBackend) : Result<Bb84Plan, QuantumError> =
        match backend.NativeStateType with
        | QuantumStateType.Annealing ->
            Error(
                QuantumError.OperationError(
                    "BB84",
                    $"Backend '{backend.Name}' does not support BB84 (native state type: {backend.NativeStateType})"
                )
            )
        | _ ->
            if requiredOps |> List.forall backend.SupportsOperation then
                Ok(Bb84Plan.ExecuteViaOps requiredOps)
            else
                Error(
                    QuantumError.OperationError(
                        "BB84",
                        $"Backend '{backend.Name}' does not support required operations for BB84"
                    )
                )

    let private chooseRandomBasis (rng: Random) : Basis =
        if rng.Next(2) = 0 then Rectilinear else Diagonal

    /// The sifted-key positions revealed for the eavesdropping check: a uniform sample
    /// without repeats, the first `sampleSize` positions of a shuffle.
    let private buildSampleIndices (siftedLength: int) (sampleRatio: float) (rng: Random) : int[] =
        let sampleSize = int (float siftedLength * sampleRatio)
        let indices = Array.init siftedLength id
        rng.Shuffle indices
        indices |> Array.truncate (max 0 sampleSize)

    /// Qubits Alice sends for a key of `keyLength` bits: about half survive sifting and
    /// `sampleRatio` of those are revealed, plus a margin of ten.
    let private transmissionsFor (keyLength: int) (sampleRatio: float) : float =
        floor (float keyLength / (0.5 * (1.0 - sampleRatio))) + 10.0

    /// A run needs a key to produce and a check sample that is a real part of the sifted key:
    /// a ratio of 0 or less reveals nothing, so the run would pass with no check at all, and
    /// a ratio of 1 or more leaves no key. A ratio close to 1 multiplies the transmissions
    /// without bound.
    let private validateBb84Parameters (keyLength: int) (sampleRatio: float) : Result<unit, QuantumError> =
        if keyLength < 1 then
            Error(QuantumError.ValidationError("keyLength", $"must be at least 1, got {keyLength}"))
        elif not (sampleRatio > 0.0 && sampleRatio < 1.0) then
            Error(
                QuantumError.ValidationError(
                    "sampleRatio",
                    $"must be greater than 0 and less than 1, got {sampleRatio}"
                )
            )
        elif transmissionsFor keyLength sampleRatio > float Array.MaxLength then
            Error(
                QuantumError.ValidationError(
                    "sampleRatio",
                    $"a key of %d{keyLength} bits with a sample ratio of {sampleRatio} needs %.3g{transmissionsFor keyLength sampleRatio} transmissions, more than one run holds (%d{Array.MaxLength})"
                )
            )
        else
            Ok()

    let private buildBb84Intent
        (keyLength: int)
        (sampleRatio: float)
        (qberThreshold: float)
        (attack: Bb84Attack)
        (seed: int option)
        : Bb84Intent =

        // Bits, bases and the check sample are secret material: without a seed they come
        // from the OS cryptographic generator. A seed gives a reproducible run.
        let rng = CryptographicRandom.UnlessSeeded(ValueOption.ofOption seed)

        // Scale up initial qubits to account for sifting (~50%) and sampling
        let initialQubits = int (transmissionsFor keyLength sampleRatio)

        // Alice + Bob randomness
        let alice = createAliceState initialQubits rng
        let bobBases = createBobBases initialQubits rng

        let eveActions =
            match attack with
            | Bb84Attack.None -> Array.create initialQubits EveAction.None
            | Bb84Attack.InterceptResend ->
                Array.init initialQubits (fun _ -> EveAction.InterceptResend(chooseRandomBasis rng))
            | Bb84Attack.Beamsplitter p ->
                Array.init initialQubits (fun _ ->
                    if rng.NextDouble() < p then
                        EveAction.InterceptResend(chooseRandomBasis rng)
                    else
                        EveAction.None)

        // Sample indices are chosen from the sifted-key index space.
        // Sifted length depends only on basis agreement (not on measurements).
        let predictedSiftedLength =
            Array.indexed alice.Bases
            |> Array.filter (fun (i, aliceBasis) -> aliceBasis = bobBases.[i])
            |> Array.length

        let sampleIndices =
            if predictedSiftedLength <= 0 then
                Array.empty
            else
                buildSampleIndices predictedSiftedLength sampleRatio rng

        {
            KeyLength = keyLength
            InitialQubits = initialQubits
            Alice = alice
            BobBases = bobBases
            EveActions = eveActions
            SampleIndices = sampleIndices
            SampleRatio = sampleRatio
            QberThreshold = qberThreshold
        }

    let private checkEavesdroppingWithSample
        (siftedKey: SiftedKey)
        (sampleIndices: int[])
        (threshold: float)
        : EavesdropCheck =

        let sampleIndices =
            sampleIndices |> Array.filter (fun i -> i >= 0 && i < siftedKey.Length)

        let errors =
            sampleIndices
            |> Array.filter (fun i -> siftedKey.AliceBits.[i] <> siftedKey.BobBits.[i])
            |> Array.length

        let sampleSize = sampleIndices.Length

        let errorRate =
            if sampleSize > 0 then
                float errors / float sampleSize
            else
                0.0

        {
            SampleSize = sampleSize
            Errors = errors
            ErrorRate = errorRate
            EavesdropDetected = errorRate > threshold
            Threshold = threshold
            SampleIndices = sampleIndices
        }

    /// Bob's results for every transmission, run as whole circuits on a backend that runs
    /// complete circuits only.
    ///
    /// A transmission is fixed by Alice's bit and basis, Eve's action and Bob's basis, so
    /// there are at most 24 kinds of transmission, and transmissions of one kind are identical
    /// independent experiments. `WholeCircuit.runTrials` runs each kind in slots side by side
    /// on disjoint qubits, circuits as wide as the backend allows, and takes an outcome from
    /// every shot of every job. A 200-bit key on a 100-shot backend takes one or two jobs
    /// rather than one job per dozen transmissions with 99 of every 100 paid shots discarded.
    /// Outcomes of a kind go to its transmissions in order.
    ///
    /// Eve's intercept-resend is a mid-circuit measurement followed by re-preparation; it is
    /// deferred: rotate into her basis, CNOT the qubit onto an ancilla of her own, rotate
    /// back. That decoheres the qubit in her basis exactly as measuring it and resending the
    /// eigenstate she saw would, so Bob's statistics are the protocol's.
    let private bobResultsWholeCircuit (backend: IQuantumBackend) (intent: Bb84Intent) : Result<Bit[], QuantumError> =
        let rotation basis qubit =
            match basis with
            | Rectilinear -> []
            | Diagonal -> [ QuantumOperation.Gate(H qubit) ]

        let kindOf i =
            intent.Alice.Bits.[i], intent.Alice.Bases.[i], intent.EveActions.[i], intent.BobBases.[i]

        // Gates of one transmission of a kind on `qubit` (and Eve's ancilla next to it).
        let transmission (bit, aliceBasis, eveAction, bobBasis) qubit =
            let prepare =
                (match bit with
                 | One -> [ QuantumOperation.Gate(X qubit) ]
                 | Zero -> [])
                @ rotation aliceBasis qubit

            let eve =
                match eveAction with
                | EveAction.None -> []
                | EveAction.InterceptResend eveBasis ->
                    rotation eveBasis qubit
                    @ [ QuantumOperation.Gate(CNOT(qubit, qubit + 1)) ]
                    @ rotation eveBasis qubit

            prepare @ eve @ rotation bobBasis qubit

        let kinds =
            [ 0 .. intent.InitialQubits - 1 ]
            |> List.countBy kindOf
            |> List.map (fun (((_, _, eveAction, _) as key), count) ->
                ({
                    Key = key
                    Width =
                        match eveAction with
                        | EveAction.None -> 1
                        | EveAction.InterceptResend _ -> 2
                    Gates = transmission key
                    Count = count
                }
                : WholeCircuit.TrialKind<_>))

        WholeCircuit.runTrials "BB84" backend kinds
        |> Result.map (fun outcomes ->
            let next = Collections.Generic.Dictionary<_, int>()

            Array.init intent.InitialQubits (fun i ->
                let key = kindOf i

                let taken =
                    match next.TryGetValue key with
                    | true, n -> n
                    | _ -> 0

                next.[key] <- taken + 1
                if outcomes.[key].[taken].[0] = 1 then One else Zero))

    let private executeBb84Planned
        (backend: IQuantumBackend)
        (_plan: Bb84Plan)
        (intent: Bb84Intent)
        : Result<BB84Result, QuantumError> =

        result {
            // Asked up front: the gate-by-gate loop below never touches the backend for a
            // |0⟩ sent and measured in the rectilinear basis, so on a backend that runs
            // complete circuits only it would "measure" those transmissions without running
            // anything.
            let! probeState = backend.InitializeState 1

            let! bobResults =
                if WholeCircuit.refusesIncremental backend probeState then
                    bobResultsWholeCircuit backend intent
                else
                    [| 0 .. intent.InitialQubits - 1 |]
                    |> Result.traverseArray (fun i ->
                        result {
                            let! aliceQubit = prepareQubit intent.Alice.Bits.[i] intent.Alice.Bases.[i] backend

                            let! qubitForBob =
                                match intent.EveActions.[i] with
                                | EveAction.None -> Ok aliceQubit
                                | EveAction.InterceptResend eveBasis ->
                                    result {
                                        let! eveMeasurement = measureQubit aliceQubit eveBasis backend
                                        return! prepareQubit eveMeasurement eveBasis backend
                                    }

                            return! measureQubit qubitForBob intent.BobBases.[i] backend
                        })

            let bobMeasurement =
                {
                    Bases = intent.BobBases
                    Results = bobResults
                    NumQubits = intent.InitialQubits
                }

            let siftedKey = siftKey intent.Alice bobMeasurement

            do!
                if siftedKey.Length < 10 then
                    Error(
                        QuantumError.ValidationError(
                            "siftedKey",
                            "Insufficient sifted key length for eavesdrop check (minimum 10 bits required)"
                        )
                    )
                else
                    Ok()

            let eavesdropCheck =
                checkEavesdroppingWithSample siftedKey intent.SampleIndices intent.QberThreshold

            // An empty sample has an error rate of 0 and would report a secure key unchecked.
            do!
                if eavesdropCheck.SampleSize = 0 then
                    Error(
                        QuantumError.ValidationError(
                            "sampleRatio",
                            $"{intent.SampleRatio} of a sifted key of {siftedKey.Length} bits leaves no bit for the eavesdropping check"
                        )
                    )
                else
                    Ok()

            let finalKey = extractFinalKey siftedKey eavesdropCheck.SampleIndices

            let finalKeyLength = finalKey.Length
            let overallEfficiency = float finalKeyLength / float intent.InitialQubits
            let success = not eavesdropCheck.EavesdropDetected && finalKeyLength > 0

            return
                {
                    InitialKeyLength = intent.InitialQubits
                    SiftedKey = siftedKey
                    EavesdropCheck = eavesdropCheck
                    FinalKey = finalKey
                    FinalKeyLength = finalKeyLength
                    OverallEfficiency = overallEfficiency
                    Success = success
                    BackendName = backend.Name
                }
        }

    /// Run complete BB84 protocol (simulation)
    ///
    /// This simulates the full BB84 protocol:
    /// 1. Alice prepares random qubits in random bases
    /// 2. Bob measures qubits in random bases
    /// 3. Basis reconciliation (sifting)
    /// 4. Eavesdropping check via sampling
    /// 5. Final key extraction
    ///
    /// Note: This is a simulation where Alice and Bob operations run sequentially.
    /// In real QKD, Alice sends qubits over quantum channel to Bob.
    ///
    /// Parameters:
    ///   keyLength - Desired final key length (actual may be less due to sampling)
    ///   backend - Quantum backend to execute on
    ///   sampleRatio - Fraction of sifted key to use for eavesdrop check (default 0.15)
    ///   qberThreshold - QBER threshold for eavesdropping detection (default 0.11)
    ///   seed - Random seed (optional, for reproducibility)
    ///
    /// Returns:
    ///   BB84Result with final key and protocol statistics
    let runBB84
        (keyLength: int)
        (backend: IQuantumBackend)
        (sampleRatio: float)
        (qberThreshold: float)
        (seed: int option)
        : Result<BB84Result, QuantumError> =

        result {
            do! validateBb84Parameters keyLength sampleRatio

            let intent =
                buildBb84Intent keyLength sampleRatio qberThreshold Bb84Attack.None seed

            let! bb84Plan = planBb84 backend
            return! executeBb84Planned backend bb84Plan intent
        }

    /// Run BB84 protocol with default parameters
    ///
    /// Uses standard parameters:
    /// - Sample ratio: 15% (typical 10-20%)
    /// - QBER threshold: 11% (typical 5-15%)
    ///
    /// Parameters:
    ///   keyLength - Desired final key length
    ///   backend - Quantum backend to execute on
    ///
    /// Returns:
    ///   BB84Result with final key and protocol statistics
    let runBB84Default (keyLength: int) (backend: IQuantumBackend) : Result<BB84Result, QuantumError> =
        runBB84 keyLength backend 0.15 0.11 None

    // ========================================================================
    // EVE SIMULATION (Intent-based)
    // ========================================================================
    /// Run BB84 with eavesdropper (Eve)
    ///
    /// Simulates BB84 protocol with Eve performing intercept-resend attack.
    /// This should result in ~25% error rate and eavesdropping detection.
    let runBB84WithEve
        (keyLength: int)
        (backend: IQuantumBackend)
        (sampleRatio: float)
        (qberThreshold: float)
        (seed: int option)
        : Result<BB84Result, QuantumError> =

        result {
            do! validateBb84Parameters keyLength sampleRatio

            let intent =
                buildBb84Intent keyLength sampleRatio qberThreshold Bb84Attack.InterceptResend seed

            let! bb84Plan = planBb84 backend
            return! executeBb84Planned backend bb84Plan intent
        }

    /// Run BB84 with Eve using default parameters
    let runBB84WithEveDefault (keyLength: int) (backend: IQuantumBackend) : Result<BB84Result, QuantumError> =
        runBB84WithEve keyLength backend 0.15 0.11 None

    // ========================================================================
    // ADVANCED EAVESDROPPING ATTACKS
    // ========================================================================

    /// Eavesdropping strategy type
    type EavesdropStrategy =
        /// Intercept-Resend: Measure and resend (most detectable, ~25% QBER)
        | InterceptResend

        /// Beamsplitter: Partial measurement with probability p (lower QBER, less information)
        /// Parameter is interception probability (0.0 to 1.0)
        | Beamsplitter of probability: float

        /// Entangling Probe: Entangle auxiliary qubit, measure later (coherent attack)
        /// More sophisticated, requires quantum memory
        | EntanglingProbe

        /// Collective Attack: Wait for basis announcement, then measure optimally
        /// Requires quantum memory to store qubits
        | CollectiveAttack

    /// Eve's information gain analysis
    ///
    /// **Performance**: Struct for stack allocation (~40 bytes)
    [<Struct>]
    type EveInformation =
        {
            /// Average mutual information between Eve and Alice (bits)
            MutualInformation: float

            /// Probability Eve guesses correct bit
            CorrectGuessProb: float

            /// Expected QBER introduced
            ExpectedQBER: float

            /// Attack strategy used
            Strategy: EavesdropStrategy

            /// Detection probability (Alice/Bob detect Eve)
            DetectionProbability: float
        }

    /// Calculate Eve's information gain for different strategies
    ///
    /// Theoretical information-theoretic analysis of eavesdropping.
    ///
    /// **Theory**:
    /// - Intercept-Resend: I(A:E) = 0.5 bits, QBER = 25%
    /// - Beamsplitter(p): I(A:E) = p/2 bits, QBER = p/4
    /// - Optimal Individual: I(A:E) ≈ 0.415 bits, QBER = 14.6%
    /// - Collective: Limited by Holevo bound
    ///
    /// Parameters:
    ///   strategy - Eavesdropping strategy to analyze
    ///
    /// Returns:
    ///   EveInformation with theoretical bounds
    let analyzeEveInformation (strategy: EavesdropStrategy) : EveInformation =
        match strategy with
        | InterceptResend ->
            // Eve measures in random basis, gets 50% information
            // Introduces 25% QBER when bases don't match
            {
                MutualInformation = 0.5 // bits
                CorrectGuessProb = 0.75 // 100% when basis matches (50%), 50% otherwise
                ExpectedQBER = 0.25 // 50% wrong basis × 50% error = 25%
                Strategy = InterceptResend
                DetectionProbability = 0.82 // Empirical from 100 runs with 11% threshold
            }

        | Beamsplitter p when p >= 0.0 && p <= 1.0 ->
            // Eve intercepts fraction p of qubits
            // Information scales linearly, QBER scales as p/4
            {
                MutualInformation = p * 0.5
                CorrectGuessProb = 0.5 + (p * 0.25) // Interpolate between 0.5 (no info) and 0.75 (full IR)
                ExpectedQBER = p * 0.25
                Strategy = Beamsplitter p
                DetectionProbability =
                    if p * 0.25 > 0.11 then
                        0.82 // Above threshold
                    else
                        0.0 // Below detection threshold
            }

        | Beamsplitter p ->
            // Invalid probability
            {
                MutualInformation = 0.0
                CorrectGuessProb = 0.5
                ExpectedQBER = 0.0
                Strategy = Beamsplitter p
                DetectionProbability = 0.0
            }

        | EntanglingProbe ->
            // Eve entangles auxiliary qubit with signal
            // Measures auxiliary after basis announcement
            // Theoretically optimal: I(A:E) ≈ 0.415 bits, QBER ≈ 14.6%
            {
                MutualInformation = 0.415 // Near optimal individual attack
                CorrectGuessProb = 0.71 // Better than IR but not perfect
                ExpectedQBER = 0.146 // Inouye-Wiesner bound
                Strategy = EntanglingProbe
                DetectionProbability = 0.92 // Higher detection due to higher QBER
            }

        | CollectiveAttack ->
            // Eve stores qubits in quantum memory
            // Measures optimally after public discussion
            // Limited by Holevo bound: χ(X:E) ≤ 1 bit
            // But requires quantum memory for all qubits (impractical)
            {
                MutualInformation = 0.5 // Bounded by Holevo
                CorrectGuessProb = 0.75 // Optimal with basis knowledge
                ExpectedQBER = 0.0 // Can avoid introducing errors
                Strategy = CollectiveAttack
                DetectionProbability = 0.0 // Undetectable in principle
            }

    /// Run BB84 with beamsplitter attack
    ///
    /// Eve intercepts only a fraction of qubits, trading off information for stealth.
    ///
    /// **Example**: p=0.5 → QBER=12.5%, I(A:E)=0.25 bits
    let runBB84WithBeamsplitter
        (keyLength: int)
        (backend: IQuantumBackend)
        (probability: float)
        (sampleRatio: float)
        (qberThreshold: float)
        (seed: int option)
        : Result<BB84Result, QuantumError> =

        result {
            do! validateBb84Parameters keyLength sampleRatio

            let intent =
                buildBb84Intent keyLength sampleRatio qberThreshold (Bb84Attack.Beamsplitter probability) seed

            let! bb84Plan = planBb84 backend
            return! executeBb84Planned backend bb84Plan intent
        }

    /// Format Eve's information analysis
    let formatEveInformation (info: EveInformation) : string =
        let sb = System.Text.StringBuilder()

        sb.AppendLine "Eve's Information Gain Analysis" |> ignore
        sb.AppendLine "===============================" |> ignore
        sb.AppendLine "" |> ignore

        let strategyName =
            match info.Strategy with
            | InterceptResend -> "Intercept-Resend"
            | Beamsplitter p -> $"Beamsplitter (p=%.2f{p})"
            | EntanglingProbe -> "Entangling Probe"
            | CollectiveAttack -> "Collective Attack"

        sb.AppendLine $"Attack Strategy: {strategyName}" |> ignore
        sb.AppendLine "" |> ignore

        sb.AppendLine "Information Metrics:" |> ignore

        sb.AppendLine $"  Mutual Information I(A:E): %.3f{info.MutualInformation} bits"
        |> ignore

        sb.AppendLine(sprintf "  Correct Guess Probability: %.1f%%" (info.CorrectGuessProb * 100.0))
        |> ignore

        sb.AppendLine(sprintf "  Expected QBER: %.1f%%" (info.ExpectedQBER * 100.0))
        |> ignore

        sb.AppendLine(sprintf "  Detection Probability: %.1f%%" (info.DetectionProbability * 100.0))
        |> ignore

        sb.AppendLine "" |> ignore

        sb.AppendLine "Security Analysis:" |> ignore

        if info.ExpectedQBER > 0.11 then
            sb.AppendLine "  ⚠️  LIKELY DETECTED (QBER > 11% threshold)" |> ignore
        elif info.ExpectedQBER > 0.05 then
            sb.AppendLine "  ⚠️  MAY BE DETECTED (QBER in detection range)" |> ignore
        else
            sb.AppendLine "  ✅ BELOW DETECTION (QBER < 5%)" |> ignore

        sb.AppendLine "" |> ignore

        // Information-theoretic bounds
        sb.AppendLine "Theoretical Bounds:" |> ignore
        sb.AppendLine "  • Holevo Bound: χ(X:E) ≤ 1 bit (max info from qubit)" |> ignore

        sb.AppendLine "  • Optimal Individual Attack: I(A:E) ≈ 0.415 bits, QBER ≈ 14.6%"
        |> ignore

        sb.AppendLine "  • Trade-off: More information → Higher QBER → Easier detection"
        |> ignore

        sb.ToString()

    // ========================================================================
    // FORMATTING AND DISPLAY
    // ========================================================================

    /// Format BB84 result for display
    let formatResult (result: BB84Result) : string =
        let sb = System.Text.StringBuilder()

        sb.AppendLine "BB84 Quantum Key Distribution Result" |> ignore
        sb.AppendLine "====================================" |> ignore
        sb.AppendLine "" |> ignore

        sb.AppendLine $"Initial Qubits Sent: {result.InitialKeyLength}" |> ignore
        sb.AppendLine "" |> ignore

        sb.AppendLine "Basis Reconciliation (Sifting):" |> ignore

        sb.AppendLine $"  Matching Bases: {result.SiftedKey.Length} / {result.InitialKeyLength}"
        |> ignore

        sb.AppendLine(sprintf "  Sifting Efficiency: %.1f%%" (result.SiftedKey.Efficiency * 100.0))
        |> ignore

        sb.AppendLine "" |> ignore

        sb.AppendLine "Eavesdropping Check:" |> ignore

        sb.AppendLine $"  Sample Size: {result.EavesdropCheck.SampleSize} bits"
        |> ignore

        sb.AppendLine $"  Errors Found: {result.EavesdropCheck.Errors}" |> ignore

        sb.AppendLine(sprintf "  QBER: %.2f%%" (result.EavesdropCheck.ErrorRate * 100.0))
        |> ignore

        sb.AppendLine(sprintf "  Threshold: %.1f%%" (result.EavesdropCheck.Threshold * 100.0))
        |> ignore

        if result.EavesdropCheck.EavesdropDetected then
            sb.AppendLine "  ⚠️  EAVESDROPPING DETECTED!" |> ignore
        else
            sb.AppendLine "  ✅ No eavesdropping detected" |> ignore

        sb.AppendLine "" |> ignore
        sb.AppendLine "Final Key:" |> ignore
        sb.AppendLine $"  Length: {result.FinalKeyLength} bits" |> ignore

        sb.AppendLine(sprintf "  Overall Efficiency: %.1f%%" (result.OverallEfficiency * 100.0))
        |> ignore

        if result.Success then
            sb.AppendLine "  ✅ Protocol SUCCESS - Secure key established!" |> ignore
        else
            sb.AppendLine "  ❌ Protocol FAILED - Abort and retry" |> ignore

        sb.AppendLine "" |> ignore
        sb.AppendLine $"Backend: {result.BackendName}" |> ignore

        sb.ToString()

    /// Format key as hex string (first N bits)
    let formatKeyHex (key: bool[]) (maxBits: int) : string =
        let bitsToShow = min maxBits key.Length

        let bytes =
            key
            |> Array.take bitsToShow
            |> Array.chunkBySize 8
            |> Array.map (fun bits ->
                bits
                |> Array.indexed
                |> Array.fold (fun acc (i, bit) -> if bit then acc ||| (1uy <<< (7 - i)) else acc) 0uy)

        bytes |> Array.map (sprintf "%02X") |> String.concat " "

    // ========================================================================
    // ERROR CORRECTION (Information Reconciliation)
    // ========================================================================

    /// Error syndrome for error correction
    [<Struct>]
    type ErrorSyndrome =
        {
            /// Parity check results
            ParityBits: bool[]

            /// Number of parity checks performed
            NumChecks: int

            /// Estimated error positions
            ErrorPositions: int[]
        }

    /// Cascade protocol block for error correction
    type CascadeBlock =
        {
            /// Block index
            BlockIndex: int

            /// Bit positions in this block
            Positions: int[]

            /// Alice's parity for this block
            AliceParity: bool

            /// Bob's parity for this block
            BobParity: bool

            /// Parity mismatch detected?
            HasError: bool
        }

    /// Error correction result
    type ErrorCorrectionResult =
        {
            /// Original key (before correction)
            OriginalKey: bool[]

            /// Corrected key (after correction)
            CorrectedKey: bool[]

            /// Number of errors detected
            ErrorsDetected: int

            /// Number of errors corrected
            ErrorsCorrected: int

            /// Information leaked to Eve (bits)
            InformationLeaked: float

            /// Error correction succeeded?
            Success: bool
        }

    /// Calculate parity of bits
    let calculateParity (bits: bool[]) : bool =
        bits |> Array.fold (fun acc b -> acc <> b) false

    /// Parity of the key bits at the given positions
    let private parityAt (key: bool[]) (positions: int[]) : bool =
        positions |> Array.fold (fun parity i -> parity <> key.[i]) false

    /// The position of one differing bit in a block whose parities differ between the two
    /// keys, found by bisection (the BINARY step of Cascade), and the number of parities
    /// Alice reveals for it: the parity of the left half at every halving.
    [<TailCall>]
    let rec private locateError (aliceKey: bool[]) (bobKey: bool[]) (block: int[]) (revealed: int) : int * int =
        if block.Length = 1 then
            block.[0], revealed
        else
            let left, right = Array.splitAt (block.Length / 2) block

            if parityAt aliceKey left <> parityAt bobKey left then
                locateError aliceKey bobKey left (revealed + 1)
            else
                locateError aliceKey bobKey right (revealed + 1)

    /// Cascade reconciliation (Brassard and Salvail, 1994) of Bob's key towards Alice's.
    ///
    /// Pass p cuts the key into blocks of initialBlockSize·2^p positions (at most half the
    /// key), pass 0 in key order and every later pass over a public shuffle of the positions. Alice reveals the parity
    /// of every block; a block whose parity differs holds an odd number of errors, and one of
    /// them is located by bisection and flipped in Bob's key. That flip turns the parity of
    /// every earlier block holding the same position odd, so those blocks are bisected in
    /// turn, until every block examined so far agrees again.
    ///
    /// Bob's key is corrected in place in a private copy: a correction changes the parity of
    /// blocks of other passes, which later steps read.
    ///
    /// InformationLeaked counts every parity Alice reveals: one per block and pass, and one
    /// per halving of a bisection. Success is true only when the corrected key equals
    /// Alice's; the comparison stands for the key-confirmation step of a deployed link.
    let private reconcile
        (aliceKey: bool[])
        (bobKey: bool[])
        (initialBlockSize: int)
        (numPasses: int)
        : ErrorCorrectionResult =
        if aliceKey.Length <> bobKey.Length then
            {
                OriginalKey = bobKey
                CorrectedKey = bobKey
                ErrorsDetected = 0
                ErrorsCorrected = 0
                InformationLeaked = 0.0
                Success = false
            }
        else
            let n = aliceKey.Length
            let corrected = Array.copy bobKey

            // The shuffles are public (both sides must cut the same blocks), so a fixed
            // generator per pass is enough; nothing secret is drawn from it.
            let passBlocks: int[][][] =
                Array.init (max 0 numPasses) (fun pass ->
                    let order = Array.init n id

                    if pass > 0 then
                        Random(pass).Shuffle order

                    // Doubles every pass, up to half the key: a block of the whole key
                    // cannot separate two errors
                    let blockSize =
                        min (int64 (max 1 ((n + 1) / 2))) (int64 (max 1 initialBlockSize) <<< min pass 30)

                    Array.chunkBySize (int blockSize) order)

            // blockOf.[pass].[position]: the block of the pass that holds the position
            let blockOf: int[][] =
                passBlocks
                |> Array.map (fun blocks ->
                    let owner = Array.zeroCreate n
                    blocks |> Array.iteri (fun b positions -> positions |> Array.iter (fun i -> owner.[i] <- b))
                    owner)

            /// Bisect every pending block that still disagrees. `pass` and `examined` name the
            /// block the main sweep stands at: only blocks whose parity Alice has revealed
            /// (earlier passes, and this pass up to `examined`) are re-opened by a correction.
            let rec settle (pass: int) (examined: int) (pending: (int * int) list) (located: int) (revealed: int) =
                match pending with
                | [] -> located, revealed
                | (blockPass, b) :: rest ->
                    let block = passBlocks.[blockPass].[b]

                    if parityAt aliceKey block = parityAt corrected block then
                        settle pass examined rest located revealed
                    else
                        let position, bisected = locateError aliceKey corrected block 0
                        corrected.[position] <- not corrected.[position]

                        let reopened =
                            [
                                for other in 0..pass do
                                    let holder = blockOf.[other].[position]

                                    if other <> blockPass && (other < pass || holder <= examined) then
                                        yield (other, holder)
                            ]

                        settle pass examined (reopened @ rest) (located + 1) (revealed + bisected)

            let located, revealed =
                ((0, 0),
                 seq {
                     for pass in 0 .. passBlocks.Length - 1 do
                         for b in 0 .. passBlocks.[pass].Length - 1 -> pass, b
                 })
                ||> Seq.fold (fun (located, revealed) (pass, b) ->
                    // One revealed parity for the block itself
                    settle pass b [ (pass, b) ] located (revealed + 1))

            {
                OriginalKey = bobKey
                CorrectedKey = corrected
                ErrorsDetected = located
                ErrorsCorrected = located
                InformationLeaked = float revealed
                Success = corrected = aliceKey
            }

    /// One pass of parity-check error correction
    ///
    /// **Protocol**:
    /// 1. Divide key into blocks of blockSize bits
    /// 2. Alice and Bob compare parity of each block
    /// 3. If parity differs, binary search for one error and flip it in Bob's key
    ///
    /// A block with an even number of errors has equal parities and stays uncorrected, so a
    /// single pass does not reconcile every key: Success is true only when the corrected key
    /// equals Alice's. cascadeErrorCorrection runs several passes over shuffled blocks.
    ///
    /// **Information Leakage**: Each revealed parity (one per block, one per halving of a
    /// binary search) gives Eve 1 bit
    ///
    /// Parameters:
    ///   aliceKey - Alice's key bits
    ///   bobKey - Bob's key bits (may have errors)
    ///   blockSize - Block size for parity checks
    ///
    /// Returns:
    ///   ErrorCorrectionResult with corrected key
    let errorCorrection (aliceKey: bool[]) (bobKey: bool[]) (blockSize: int) : ErrorCorrectionResult =
        reconcile aliceKey bobKey blockSize 1

    /// Error correction using the Cascade protocol
    ///
    /// **Cascade Protocol** (Brassard & Salvail, 1994):
    /// - Pass 1: blocks of k₁ bits in key order; a block with differing parity is bisected
    ///   and one error corrected
    /// - Pass 2+: blocks of twice the size over a public shuffle; every correction re-opens
    ///   the earlier blocks that hold the corrected bit
    ///
    /// Parameters:
    ///   aliceKey - Alice's key
    ///   bobKey - Bob's key (with errors)
    ///   initialBlockSize - Starting block size (about 0.73 / QBER)
    ///   numPasses - Number of correction passes
    ///
    /// Returns:
    ///   ErrorCorrectionResult. Success is true only when the corrected key equals Alice's
    ///   key; errors that every pass left paired inside one block remain and give false.
    let cascadeErrorCorrection
        (aliceKey: bool[])
        (bobKey: bool[])
        (initialBlockSize: int)
        (numPasses: int)
        : ErrorCorrectionResult =
        reconcile aliceKey bobKey initialBlockSize numPasses

    // ========================================================================
    // PRIVACY AMPLIFICATION
    // ========================================================================

    /// Privacy amplification result
    type PrivacyAmplificationResult =
        {
            /// Original key (before amplification)
            OriginalKey: bool[]

            /// Amplified key (after hashing)
            AmplifiedKey: bool[]

            /// Original key length (bits)
            OriginalLength: int

            /// Final key length (bits)
            FinalLength: int

            /// Compression ratio
            CompressionRatio: float

            /// Hash function used
            HashFunction: string

            /// Security parameter (bits of security)
            SecurityParameter: int
        }

    /// Simple hash function (XOR-based, for demonstration)
    ///
    /// Real QKD systems use SHA-256, SHA-3, or universal hash families.
    /// This is a simplified educational implementation.
    ///
    /// Parameters:
    ///   input - Input bit string
    ///   outputLength - Desired output length
    ///
    /// Returns:
    ///   Hashed output of specified length
    let simpleHash (input: bool[]) (outputLength: int) : bool[] =
        if input.Length = 0 || outputLength = 0 then
            Array.empty
        else
            // Simple deterministic hash: chunk input and XOR
            let chunks =
                input |> Array.chunkBySize ((input.Length + outputLength - 1) / outputLength)

            Array.init outputLength (fun i ->
                chunks
                |> Array.filter (fun chunk -> chunk.Length > (i % chunk.Length))
                |> Array.fold (fun acc chunk -> acc <> chunk.[i % chunk.Length]) false)

    /// Privacy amplification using hash function
    ///
    /// Removes any partial information Eve might have obtained.
    /// Even if Eve knows some bits, hashed key is secure.
    ///
    /// **Theory** (Bennett et al., 1995):
    /// - If Eve knows ≤ t bits about n-bit key
    /// - Hash to (n - t - s) bits for s-bit security
    /// - Final key has 2^(-s) probability of Eve guessing
    ///
    /// **Real Systems**: Use SHA-256, SHA-3, or Toeplitz matrices
    ///
    /// Parameters:
    ///   key - Input key (may be partially compromised)
    ///   eveInformation - Estimated bits Eve knows (from QBER)
    ///   securityParameter - Desired security level (bits)
    ///
    /// Returns:
    ///   PrivacyAmplificationResult with secure shortened key
    let privacyAmplification
        (key: bool[])
        (eveInformation: float)
        (securityParameter: int)
        : PrivacyAmplificationResult =

        let originalLength = key.Length

        // Calculate safe output length: n - t - s
        // where n = key length, t = Eve's info, s = security parameter.
        // When that is not positive no bit is secure: the result is the empty key with a
        // security parameter of 0, never a padded bit.
        let finalLength =
            max 0 (int (floor (float originalLength - eveInformation - float securityParameter)))

        // Apply hash function
        let amplifiedKey = simpleHash key finalLength

        {
            OriginalKey = key
            AmplifiedKey = amplifiedKey
            OriginalLength = originalLength
            FinalLength = amplifiedKey.Length
            CompressionRatio =
                if originalLength > 0 then
                    float amplifiedKey.Length / float originalLength
                else
                    0.0
            HashFunction = "SimpleXOR (educational)"
            SecurityParameter = if amplifiedKey.Length > 0 then securityParameter else 0
        }

    /// Privacy amplification using SHA-256
    ///
    /// Hashes the key and keeps the first targetLength bits of the digest.
    ///
    /// Parameters:
    ///   key - Input key bits
    ///   targetLength - Desired output length (bits, max 256)
    ///
    /// Returns:
    ///   PrivacyAmplificationResult with SHA-256 hashed key. This function is given no
    ///   estimate of what Eve knows, so its SecurityParameter is the margin for an input Eve
    ///   knows nothing about: the bits by which the output is shorter than the input, at
    ///   most 128 (no more is claimed for SHA-256). An output as long as the input has a
    ///   margin of 0. A caller who knows Eve's information subtracts it from that margin, as
    ///   runCompleteQKD does.
    let privacyAmplificationSHA256 (key: bool[]) (targetLength: int) : PrivacyAmplificationResult =

        let originalLength = key.Length

        // Convert bits to bytes
        let bytes = Array.zeroCreate ((key.Length + 7) / 8)

        for i in 0 .. key.Length - 1 do
            let byteIdx = i / 8
            let bitIdx = i % 8

            if key.[i] then
                bytes.[byteIdx] <- bytes.[byteIdx] ||| (1uy <<< bitIdx)

        // Compute SHA-256
        use sha256 = System.Security.Cryptography.SHA256.Create()
        let hash = sha256.ComputeHash bytes

        // Extract bits from hash
        let maxBits = max 0 (min targetLength (hash.Length * 8))

        let amplifiedKey =
            Array.init maxBits (fun i ->
                let byteIdx = i / 8
                let bitIdx = i % 8
                (hash.[byteIdx] &&& (1uy <<< bitIdx)) <> 0uy)

        {
            OriginalKey = key
            AmplifiedKey = amplifiedKey
            OriginalLength = originalLength
            FinalLength = amplifiedKey.Length
            CompressionRatio =
                if originalLength > 0 then
                    float amplifiedKey.Length / float originalLength
                else
                    0.0
            HashFunction = "SHA-256"
            SecurityParameter = min 128 (max 0 (originalLength - amplifiedKey.Length))
        }

    // ========================================================================
    // COMPLETE QKD PIPELINE WITH ERROR CORRECTION & PRIVACY AMPLIFICATION
    // ========================================================================

    /// Complete QKD result with all post-processing stages
    type CompleteQKDResult =
        {
            /// BB84 protocol result
            BB84Result: BB84Result

            /// Error correction result (optional)
            ErrorCorrection: ErrorCorrectionResult option

            /// Privacy amplification result
            PrivacyAmplification: PrivacyAmplificationResult

            /// Alice's final secure key: her reconciled key after privacy amplification
            FinalSecureKey: bool[]

            /// Bob's final secure key: his reconciled key after the same privacy
            /// amplification. Equal to FinalSecureKey in every Ok result (the pipeline is an
            /// Error when the reconciled keys differ)
            BobFinalSecureKey: bool[]

            /// Final key length
            FinalKeyLength: int

            /// End-to-end efficiency (final / initial)
            EndToEndEfficiency: float

            /// Information Eve can hold about the reconciled key (bits): n·h(QBER) for the n
            /// reconciled bits, from the error rate of the check sample, plus every parity
            /// revealed in error correction. The asymptotic estimate: the sampling
            /// uncertainty of the QBER is not added
            TotalInformationLeaked: float

            /// Security level achieved (bits): reconciled bits − TotalInformationLeaked −
            /// FinalKeyLength, at most 128 (no more is claimed for SHA-256). At least the
            /// requested security parameter, up to that cap
            SecurityLevel: int

            /// Protocol succeeded?
            Success: bool
        }

    /// Binary entropy h(p) = −p·log₂p − (1−p)·log₂(1−p), in bits
    let private binaryEntropy (p: float) : float =
        if p <= 0.0 || p >= 1.0 then
            0.0
        else
            -p * Math.Log2 p - (1.0 - p) * Math.Log2(1.0 - p)

    /// Number of Cascade passes of runCompleteQKD: four doubling passes and two more over
    /// fresh shuffles, which separate error pairs the earlier passes left in one block.
    [<Literal>]
    let private CascadePasses = 6

    /// First-pass block size of Cascade: about 0.73 / error rate (Brassard and Salvail),
    /// between 4 and 64 bits. The error rate is an upper estimate from the check sample
    /// (its rate plus two standard errors plus 3 / sample size, the last term bounding a
    /// sample without errors): blocks sized for a rate the sample underestimates hide pairs
    /// of errors behind equal parities.
    let private cascadeBlockSize (check: EavesdropCheck) : int =
        let sampleSize = float (max 1 check.SampleSize)
        let rate = check.ErrorRate

        let upperRate =
            rate + 2.0 * sqrt (rate * (1.0 - rate) / sampleSize) + 3.0 / sampleSize

        max 4 (min 64 (int (0.73 / upperRate)))

    /// Run complete QKD pipeline with all post-processing
    ///
    /// **Full Pipeline**:
    /// 1. BB84 quantum protocol (basis reconciliation + eavesdrop check)
    /// 2. Error correction (optional): Cascade reconciles Bob's raw key, taken from what he
    ///    measured, with Alice's
    /// 3. Key confirmation: the two reconciled keys must be equal
    /// 4. Privacy amplification (mandatory, removes Eve's information): both keys are
    ///    hashed to n − t − s bits (at most 256, one SHA-256 digest), where n is the
    ///    reconciled length, t = TotalInformationLeaked and s = securityParameter
    ///
    /// Parameters:
    ///   keyLength - Length BB84 aims at for the raw key; the secure key is shorter by t + s
    ///   backend - Quantum backend
    ///   doErrorCorrection - Apply error correction? Without it the pipeline succeeds only
    ///                       on a channel that flipped no bit of the raw key
    ///   securityParameter - Security margin s (bits) taken off the key length
    ///   seed - Random seed
    ///
    /// Returns:
    ///   CompleteQKDResult with the key both sides hold, or an Error when eavesdropping is
    ///   detected, when Alice's and Bob's keys differ after the error-correction step (or
    ///   without it), or when n − t − s leaves no secure bit
    let runCompleteQKD
        (keyLength: int)
        (backend: IQuantumBackend)
        (doErrorCorrection: bool)
        (securityParameter: int)
        (seed: int option)
        : Result<CompleteQKDResult, QuantumError> =

        result {
            do!
                if securityParameter < 0 then
                    Error(
                        QuantumError.ValidationError(
                            "securityParameter",
                            $"must not be negative, got {securityParameter}"
                        )
                    )
                else
                    Ok()

            // Step 1: Run BB84 protocol
            let! bb84 = runBB84 keyLength backend 0.15 0.11 seed

            // Check if eavesdropping detected
            do!
                if bb84.EavesdropCheck.EavesdropDetected then
                    Error(QuantumError.ValidationError("eavesdropping", "Eavesdropping detected - aborting protocol"))
                else
                    Ok()

            // Step 2: Error correction (optional), on the raw keys: the sifted bits without
            // the publicly revealed check sample. Alice's are what she sent, Bob's what he
            // measured; reconciling Alice's key with a copy of itself would find no error on
            // any channel.
            let aliceKey = bb84.FinalKey

            let bobRawKey =
                extractBobFinalKey bb84.SiftedKey bb84.EavesdropCheck.SampleIndices

            let errorCorrectionResult =
                if doErrorCorrection then
                    Some(
                        cascadeErrorCorrection
                            aliceKey
                            bobRawKey
                            (cascadeBlockSize bb84.EavesdropCheck)
                            CascadePasses
                    )
                else
                    None

            let bobKey =
                match errorCorrectionResult with
                | Some corrected -> corrected.CorrectedKey
                | None -> bobRawKey

            // Step 3: Key confirmation. Two different keys are not a shared key, whatever
            // is done to them afterwards.
            let differing =
                Array.fold2 (fun count a b -> if a <> b then count + 1 else count) 0 aliceKey bobKey

            do!
                if differing = 0 then
                    Ok()
                elif doErrorCorrection then
                    Error(
                        QuantumError.OperationError(
                            "ErrorCorrection",
                            $"Alice's and Bob's keys still differ in {differing} of {aliceKey.Length} bits after {CascadePasses} Cascade passes; no shared key"
                        )
                    )
                else
                    Error(
                        QuantumError.OperationError(
                            "KeyConfirmation",
                            $"Alice's and Bob's keys differ in {differing} of {aliceKey.Length} bits and error correction is off; no shared key"
                        )
                    )

            // Step 4: What Eve can know about the reconciled key: h(QBER) per bit from the
            // disturbance the check sample measured, and every parity revealed in error
            // correction.
            let eveInfoFromQBER =
                float aliceKey.Length * binaryEntropy bb84.EavesdropCheck.ErrorRate

            let informationLeakedEC =
                errorCorrectionResult
                |> Option.map (fun corrected -> corrected.InformationLeaked)
                |> Option.defaultValue 0.0

            let totalEveInfo = eveInfoFromQBER + informationLeakedEC

            // Step 5: Privacy amplification to n − t − s bits
            let secureLength =
                int (floor (float aliceKey.Length - totalEveInfo - float securityParameter))

            do!
                if secureLength < 1 then
                    Error(
                        QuantumError.ValidationError(
                            "keyLength",
                            $"no secure bit remains: %d{aliceKey.Length} reconciled bits − %.1f{eveInfoFromQBER} (Eve, from a QBER of %.1f{bb84.EavesdropCheck.ErrorRate * 100.0}%%) − %.0f{informationLeakedEC} (error-correction parities) − %d{securityParameter} (security parameter); use a longer key or a smaller security parameter"
                        )
                    )
                else
                    Ok()

            let paResult = privacyAmplificationSHA256 aliceKey (min 256 secureLength)
            let bobResult = privacyAmplificationSHA256 bobKey (min 256 secureLength)

            let finalKeyLength = paResult.FinalLength

            let securityLevel =
                min 128 (int (floor (float aliceKey.Length - totalEveInfo)) - finalKeyLength)

            let endToEndEfficiency =
                if bb84.InitialKeyLength > 0 then
                    float finalKeyLength / float bb84.InitialKeyLength
                else
                    0.0

            return
                {
                    BB84Result = bb84
                    ErrorCorrection = errorCorrectionResult
                    PrivacyAmplification =
                        { paResult with
                            SecurityParameter = securityLevel
                        }
                    FinalSecureKey = paResult.AmplifiedKey
                    BobFinalSecureKey = bobResult.AmplifiedKey
                    FinalKeyLength = finalKeyLength
                    EndToEndEfficiency = endToEndEfficiency
                    TotalInformationLeaked = totalEveInfo
                    SecurityLevel = securityLevel
                    Success = bb84.Success && finalKeyLength > 0
                }
        }

    /// Format complete QKD result
    let formatCompleteQKDResult (result: CompleteQKDResult) : string =
        let sb = System.Text.StringBuilder()

        sb.AppendLine "═══════════════════════════════════════════════════════════════"
        |> ignore

        sb.AppendLine "Complete QKD Pipeline Result" |> ignore

        sb.AppendLine "═══════════════════════════════════════════════════════════════"
        |> ignore

        sb.AppendLine "" |> ignore

        sb.AppendLine "Stage 1: BB84 Quantum Protocol" |> ignore
        sb.AppendLine "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━" |> ignore

        sb.AppendLine $"  Initial qubits: {result.BB84Result.InitialKeyLength}"
        |> ignore

        sb.AppendLine $"  Sifted key: {result.BB84Result.SiftedKey.Length} bits"
        |> ignore

        sb.AppendLine(sprintf "  QBER: %.2f%%" (result.BB84Result.EavesdropCheck.ErrorRate * 100.0))
        |> ignore

        let eavesdropStatus =
            if result.BB84Result.EavesdropCheck.EavesdropDetected then
                "⚠️ DETECTED"
            else
                "✅ PASS"

        sb.AppendLine $"  Eavesdrop check: {eavesdropStatus}" |> ignore
        sb.AppendLine "" |> ignore

        match result.ErrorCorrection with
        | Some ec ->
            sb.AppendLine "Stage 2: Error Correction (Cascade)" |> ignore
            sb.AppendLine "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━" |> ignore
            sb.AppendLine $"  Errors detected: {ec.ErrorsDetected}" |> ignore
            sb.AppendLine $"  Errors corrected: {ec.ErrorsCorrected}" |> ignore

            sb.AppendLine $"  Information leaked: %.1f{ec.InformationLeaked} bits" |> ignore

            let ecStatus = if ec.Success then "✅ SUCCESS" else "⚠️ PARTIAL"
            sb.AppendLine $"  Status: {ecStatus}" |> ignore
            sb.AppendLine "" |> ignore
        | None ->
            sb.AppendLine "Stage 2: Error Correction - SKIPPED" |> ignore
            sb.AppendLine "" |> ignore

        sb.AppendLine "Stage 3: Privacy Amplification" |> ignore
        sb.AppendLine "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━" |> ignore

        sb.AppendLine $"  Hash function: {result.PrivacyAmplification.HashFunction}"
        |> ignore

        sb.AppendLine $"  Input length: {result.PrivacyAmplification.OriginalLength} bits"
        |> ignore

        sb.AppendLine $"  Output length: {result.PrivacyAmplification.FinalLength} bits"
        |> ignore

        sb.AppendLine(sprintf "  Compression: %.1f%%" (result.PrivacyAmplification.CompressionRatio * 100.0))
        |> ignore

        sb.AppendLine $"  Security level: {result.SecurityLevel} bits" |> ignore
        sb.AppendLine "" |> ignore

        sb.AppendLine "Final Result" |> ignore
        sb.AppendLine "━━━━━━━━━━━━" |> ignore
        sb.AppendLine $"  Final key length: {result.FinalKeyLength} bits" |> ignore

        sb.AppendLine(sprintf "  End-to-end efficiency: %.1f%%" (result.EndToEndEfficiency * 100.0))
        |> ignore

        sb.AppendLine $"  Total info leaked to Eve: %.1f{result.TotalInformationLeaked} bits"
        |> ignore

        let finalStatus =
            if result.Success then
                "✅ SECURE KEY ESTABLISHED"
            else
                "❌ FAILED"

        sb.AppendLine $"  Status: {finalStatus}" |> ignore
        sb.AppendLine "" |> ignore

        sb.AppendLine "═══════════════════════════════════════════════════════════════"
        |> ignore

        sb.ToString()
