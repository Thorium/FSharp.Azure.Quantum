namespace FSharp.Azure.Quantum.Algorithms

open FSharp.Azure.Quantum
open FSharp.Azure.Quantum.CircuitBuilder
open FSharp.Azure.Quantum.Core
open FSharp.Azure.Quantum.Core.BackendAbstraction

/// Superdense Coding Protocol
/// 
/// Superdense coding is the dual of quantum teleportation. While teleportation sends
/// 1 qubit of quantum information using 2 classical bits + 1 shared entangled qubit,
/// superdense coding sends 2 classical bits using 1 qubit + 1 shared entangled qubit.
/// 
/// **IMPORTANT**: This protocol:
/// - Sends 2 classical bits by transmitting only 1 qubit
/// - Requires a pre-shared entangled Bell pair (|Phi+>)
/// - Does NOT violate information-theoretic bounds (entanglement is pre-shared)
/// - IS a verified quantum protocol (demonstrated experimentally)
/// 
/// **Production Use Cases**:
/// - Quantum Networks (efficient classical communication over quantum channels)
/// - Quantum Communication (double channel capacity for classical bits)
/// - Quantum Dense Coding in quantum internet protocols
/// 
/// **Textbook References**:
/// - Nielsen & Chuang "Quantum Computation and Quantum Information" - Section 2.3
/// - "Learn Quantum Computing with Python and Q#" (Kaiser, 2021) - Chapter 8
/// - "Quantum Programming in Depth" (Manning, 2024) - Chapter 10
/// 
/// **Protocol Steps**:
/// 1. Alice and Bob share entangled Bell pair |Phi+> = (|00> + |11>) / sqrt(2)
/// 2. Alice encodes 2 classical bits by applying one of 4 operations to her qubit:
///    - 00 -> I (identity), 01 -> X (bit flip), 10 -> Z (phase flip), 11 -> ZX (both)
/// 3. Alice sends her qubit to Bob (1 qubit carries 2 bits)
/// 4. Bob decodes by performing Bell measurement (CNOT -> H -> Measure)
/// 5. Bob recovers the 2 classical bits
/// 
/// **Resource Requirements**:
/// - Qubits: 2 (Alice's qubit, Bob's qubit)
/// - Gates: 4-5 (Bell pair creation + encoding + decoding)
/// - Classical bits transmitted: 0 (only 1 qubit transmitted)
/// - Pre-shared entanglement: 1 Bell pair
module SuperdenseCoding =

    // ========================================================================
    // TYPES
    // ========================================================================

    /// Two classical bits to transmit via the superdense coding protocol
    [<Struct>]
    type ClassicalMessage = {
        /// First classical bit (0 or 1)
        Bit1: int

        /// Second classical bit (0 or 1)
        Bit2: int
    }

    /// Result of a single superdense coding transmission
    type SuperdenseResult = {
        /// The 2-bit message Alice intended to send
        SentMessage: ClassicalMessage

        /// The 2-bit message Bob received after decoding
        ReceivedMessage: ClassicalMessage

        /// Whether the received message matches the sent message
        Success: bool

        /// Backend used
        BackendName: string
    }

    /// Statistics from running superdense coding multiple times
    type SuperdenseStatistics = {
        /// Total number of trials
        TotalTrials: int

        /// Number of successful transmissions
        SuccessCount: int

        /// Success rate (0.0 to 1.0)
        SuccessRate: float

        /// The message that was sent in all trials
        Message: ClassicalMessage
    }

    // ========================================================================
    // INTENT -> PLAN -> EXECUTION (ADR: intent-first algorithms)
    // ========================================================================

    type private SuperdenseIntent = {
        AliceQubit: int
        BobQubit: int
        Message: ClassicalMessage
    }

    [<RequireQualifiedAccess>]
    type private SuperdensePlan =
        | ExecuteViaOps of bellOps: QuantumOperation list * encodeOps: QuantumOperation list * decodeOps: QuantumOperation list

    /// Build Bell pair creation operations: H(0) -> CNOT(0,1)
    /// Creates |Phi+> = (|00> + |11>) / sqrt(2)
    let private buildBellOps (intent: SuperdenseIntent) : QuantumOperation list =
        [
            QuantumOperation.Gate (H intent.AliceQubit)
            QuantumOperation.Gate (CNOT (intent.AliceQubit, intent.BobQubit))
        ]

    /// Build Alice's encoding operations based on the 2-bit message
    /// 
    /// Encoding table (applied to Alice's qubit):
    /// - 00 -> I (identity, no gate)
    /// - 01 -> X (bit flip) 
    /// - 10 -> Z (phase flip)
    /// - 11 -> Z then X (both flips)
    /// 
    /// Each encoding maps |Phi+> to a distinct Bell state:
    /// - 00: |Phi+> -> |Phi+>  (I applied)
    /// - 01: |Phi+> -> |Psi+>  (X applied)
    /// - 10: |Phi+> -> |Phi->  (Z applied)
    /// - 11: |Phi+> -> |Psi->  (ZX applied)
    let private buildEncodeOps (intent: SuperdenseIntent) : QuantumOperation list =
        match (intent.Message.Bit1, intent.Message.Bit2) with
        | (0, 0) -> []  // Identity - do nothing
        | (0, 1) -> [ QuantumOperation.Gate (X intent.AliceQubit) ]
        | (1, 0) -> [ QuantumOperation.Gate (Z intent.AliceQubit) ]
        | (1, 1) -> [ QuantumOperation.Gate (Z intent.AliceQubit); QuantumOperation.Gate (X intent.AliceQubit) ]

        | _ -> []  // Shouldn't happen with validated input

    /// Build Bob's decoding operations: CNOT(Alice, Bob) -> H(Alice) -> Measure
    /// 
    /// This is the inverse of Bell pair creation (Bell measurement).
    /// After these operations, measuring in the computational basis yields
    /// the original 2 classical bits.
    let private buildDecodeOps (intent: SuperdenseIntent) : QuantumOperation list =
        [
            QuantumOperation.Gate (CNOT (intent.AliceQubit, intent.BobQubit))
            QuantumOperation.Gate (H intent.AliceQubit)
        ]

    let private plan (backend: IQuantumBackend) (intent: SuperdenseIntent) : Result<SuperdensePlan, QuantumError> =
        // Superdense coding requires standard gate-based operations (H, CNOT, X, Z).
        // Some backends (e.g., annealing) cannot support this, so fail explicitly.
        match backend.NativeStateType with
        | QuantumStateType.Annealing ->
            Error (QuantumError.OperationError ("SuperdenseCoding", $"Backend '{backend.Name}' does not support superdense coding (native state type: {backend.NativeStateType})"))
        | _ ->
            let bellOps = buildBellOps intent
            let encodeOps = buildEncodeOps intent
            let decodeOps = buildDecodeOps intent

            let requiredOps = bellOps @ encodeOps @ decodeOps

            if requiredOps |> List.forall backend.SupportsOperation then
                Ok (SuperdensePlan.ExecuteViaOps (bellOps, encodeOps, decodeOps))
            else
                Error (QuantumError.OperationError ("SuperdenseCoding", $"Backend '{backend.Name}' does not support required operations for superdense coding"))

    let private executePlan
        (backend: IQuantumBackend)
        (state: QuantumState)
        (plan: SuperdensePlan)
        : Result<QuantumState, QuantumError> =

        match plan with
        | SuperdensePlan.ExecuteViaOps (bellOps, encodeOps, decodeOps) ->
            result {
                // Step 1: Create Bell pair
                let! afterBell = UnifiedBackend.applySequence backend bellOps state

                // Step 2: Alice encodes her 2-bit message
                let! afterEncode =
                    if List.isEmpty encodeOps then Ok afterBell
                    else UnifiedBackend.applySequence backend encodeOps afterBell

                // Step 3: Bob decodes (Bell measurement)
                let! afterDecode = UnifiedBackend.applySequence backend decodeOps afterEncode

                return afterDecode
            }

    // ========================================================================
    // MAIN PROTOCOL
    // ========================================================================

    /// Execute the superdense coding protocol to send a 2-bit classical message
    /// 
    /// **Circuit Layout**:
    ///   q0 (Alice): ─H─●─[encode]─●─H─M─
    ///                   │          │
    ///   q1 (Bob):   ───X──────────X───M─
    /// 
    /// Where [encode] is one of: I, X, Z, or ZX (depending on message)
    /// 
    /// **Parameters**:
    ///   backend - Quantum backend to execute on
    ///   message - The 2-bit classical message to send (Bit1 and Bit2 must be 0 or 1)
    /// 
    /// **Returns**:
    ///   SuperdenseResult with sent and received messages
    let send (backend: IQuantumBackend) (message: ClassicalMessage)
        : Result<SuperdenseResult, QuantumError> =

        result {
            // Validate input bits
            do!
                if message.Bit1 <> 0 && message.Bit1 <> 1 then
                    Error (QuantumError.ValidationError ("message.Bit1", $"must be 0 or 1, got {message.Bit1}"))
                elif message.Bit2 <> 0 && message.Bit2 <> 1 then
                    Error (QuantumError.ValidationError ("message.Bit2", $"must be 0 or 1, got {message.Bit2}"))
                else
                    Ok ()

            let intent = {
                AliceQubit = 0
                BobQubit = 1
                Message = message
            }

            // Initialize 2-qubit state |00>
            let! initialState = backend.InitializeState 2

            // Plan and execute
            let! codingPlan = plan backend intent
            let! decodedState = executePlan backend initialState codingPlan

            // Measure to recover classical bits
            let measurements = UnifiedBackend.measureState decodedState 1
            let bits = measurements.[0]

            let receivedMessage = {
                Bit1 = bits.[intent.AliceQubit]
                Bit2 = bits.[intent.BobQubit]
            }

            return {
                SentMessage = message
                ReceivedMessage = receivedMessage
                Success = (message.Bit1 = receivedMessage.Bit1 && message.Bit2 = receivedMessage.Bit2)
                BackendName = backend.Name
            }
        }

    // ========================================================================
    // CONVENIENCE FUNCTIONS
    // ========================================================================

    /// Send message 00 (identity encoding)
    let send00 (backend: IQuantumBackend) : Result<SuperdenseResult, QuantumError> =
        send backend { Bit1 = 0; Bit2 = 0 }

    /// Send message 01 (X encoding)
    let send01 (backend: IQuantumBackend) : Result<SuperdenseResult, QuantumError> =
        send backend { Bit1 = 0; Bit2 = 1 }

    /// Send message 10 (Z encoding)
    let send10 (backend: IQuantumBackend) : Result<SuperdenseResult, QuantumError> =
        send backend { Bit1 = 1; Bit2 = 0 }

    /// Send message 11 (ZX encoding)
    let send11 (backend: IQuantumBackend) : Result<SuperdenseResult, QuantumError> =
        send backend { Bit1 = 1; Bit2 = 1 }

    // ========================================================================
    // STATISTICS
    // ========================================================================

    /// Run superdense coding multiple times and collect statistics
    /// 
    /// Useful for analyzing protocol reliability on NISQ hardware.
    /// On a noise-free simulator, success rate should be 1.0.
    /// 
    /// **Parameters**:
    ///   backend - Quantum backend to execute on
    ///   message - The 2-bit message to send in each trial
    ///   trials - Number of protocol runs
    /// 
    /// **Returns**:
    ///   SuperdenseStatistics with success rate and counts
    let runStatistics (backend: IQuantumBackend) (message: ClassicalMessage) (trials: int)
        : Result<SuperdenseStatistics, QuantumError> =

        if trials < 1 then
            Error (QuantumError.ValidationError ("trials", "must be at least 1"))
        else
            let trialsList = [ 1 .. trials ]

            let resultsResult =
                (Ok [], trialsList)
                ||> List.fold (fun accResult _ ->
                    result {
                        let! acc = accResult
                        let! sendResult = send backend message
                        return sendResult :: acc
                    }
                )
                |> Result.map List.rev

            resultsResult
            |> Result.map (fun results ->
                let successCount = results |> List.filter (fun r -> r.Success) |> List.length
                {
                    TotalTrials = trials
                    SuccessCount = successCount
                    SuccessRate = float successCount / float trials
                    Message = message
                }
            )

    // ========================================================================
    // FORMATTING
    // ========================================================================

    /// Format a single superdense coding result for display
    let formatResult (result: SuperdenseResult) : string =
        let encodingStr =
            match (result.SentMessage.Bit1, result.SentMessage.Bit2) with
            | (0, 0) -> "I (identity)"
            | (0, 1) -> "X (bit flip)"
            | (1, 0) -> "Z (phase flip)"
            | (1, 1) -> "ZX (both flips)"
            | _ -> "unknown"

        sprintf
            "Superdense Coding Result:\n\
             Sent Message:     %d%d\n\
             Encoding:         %s\n\
             Received Message: %d%d\n\
             Success:          %b\n\
             Backend:          %s"
            result.SentMessage.Bit1
            result.SentMessage.Bit2
            encodingStr
            result.ReceivedMessage.Bit1
            result.ReceivedMessage.Bit2
            result.Success
            result.BackendName

    /// Format statistics for display
    let formatStatistics (stats: SuperdenseStatistics) : string =
        sprintf
            "Superdense Coding Statistics (%d trials):\n\
             ==========================================\n\
             Message:      %d%d\n\
             Success Rate: %.1f%% (%d/%d)\n"
            stats.TotalTrials
            stats.Message.Bit1
            stats.Message.Bit2
            (stats.SuccessRate * 100.0)
            stats.SuccessCount
            stats.TotalTrials
