namespace FSharp.Azure.Quantum.Algorithms

open System
open FSharp.Azure.Quantum
open FSharp.Azure.Quantum.CircuitBuilder
open FSharp.Azure.Quantum.Core
open FSharp.Azure.Quantum.Core.BackendAbstraction

/// Quantum Error Correction Codes
///
/// Implements four standard quantum error correction codes from textbook
/// quantum computing:
///
/// 1. **BitFlip** (3-qubit, [[3,1,1]]): Corrects single bit-flip (X) errors
/// 2. **PhaseFlip** (3-qubit, [[3,1,1]]): Corrects single phase-flip (Z) errors
/// 3. **Shor** (9-qubit, [[9,1,3]]): Corrects arbitrary single-qubit errors
/// 4. **Steane** (7-qubit, [[7,1,3]]): CSS code correcting arbitrary single-qubit errors
///
/// **Production Use Cases**:
/// - Fault-tolerant quantum computation (protecting logical qubits)
/// - Quantum memory (preserving quantum states over time)
/// - Quantum communication (error correction in quantum channels)
/// - Benchmarking quantum hardware (testing error rates)
///
/// **Textbook References**:
/// - Nielsen & Chuang, "Quantum Computation and Quantum Information", Chapter 10
/// - Shor (1995), "Scheme for reducing decoherence in quantum computer memory"
/// - Steane (1996), "Error correcting codes in quantum theory"
/// - Calderbank & Shor (1996), "Good quantum error-correcting codes exist"
///
/// **Notation**:
/// - [[n,k,d]]: n physical qubits, k logical qubits, d = code distance
/// - Code distance d corrects floor((d-1)/2) errors
module QuantumErrorCorrection =

    // ========================================================================
    // TYPES
    // ========================================================================

    /// Type of quantum error that can be injected or detected
    [<Struct>]
    type ErrorType =
        /// Bit-flip error (X gate): |0> <-> |1>
        | BitFlipError
        /// Phase-flip error (Z gate): |+> <-> |->
        | PhaseFlipError
        /// Combined bit+phase flip (Y gate, up to global phase)
        | CombinedError
        /// Error affecting multiple qubits beyond code's correction capability
        /// (e.g., two independent errors on different qubits in a distance-3 code)
        | UncorrectableError

    /// Quantum error correction code identifier
    [<Struct>]
    type ErrorCode =
        /// 3-qubit bit-flip code [[3,1,1]]
        | BitFlipCode3
        /// 3-qubit phase-flip code [[3,1,1]]
        | PhaseFlipCode3
        /// Shor 9-qubit code [[9,1,3]]
        | ShorCode9
        /// Steane 7-qubit code [[7,1,3]]
        | SteaneCode7

    /// Code parameters describing the structure of an error correction code
    [<Struct>]
    type CodeParameters = {
        /// Which code this describes
        Code: ErrorCode
        /// Number of physical qubits used
        PhysicalQubits: int
        /// Number of logical qubits encoded
        LogicalQubits: int
        /// Code distance (minimum weight of undetectable error)
        Distance: int
        /// Number of errors the code can correct
        CorrectableErrors: int
    }

    /// Syndrome measurement result
    type SyndromeResult = {
        /// Raw syndrome bits
        SyndromeBits: int list
        /// Detected error type (if any)
        DetectedError: ErrorType option
        /// Qubit where error was detected (if identifiable)
        ErrorQubit: int option
    }

    /// Result of encoding a logical qubit
    type EncodingResult = {
        /// Which code was used
        Code: ErrorCode
        /// Number of physical qubits
        PhysicalQubits: int
        /// The encoded quantum state
        EncodedState: QuantumState
    }

    /// Result of a full correction cycle
    type CorrectionResult = {
        /// The syndrome measurement
        Syndrome: SyndromeResult
        /// State after correction
        CorrectedState: QuantumState
        /// Whether a correction gate was applied
        CorrectionApplied: bool
    }

    /// Result of a full round-trip test (encode -> error -> syndrome -> correct -> verify)
    type RoundTripResult = {
        /// Which code was used
        Code: ErrorCode
        /// Logical bit that was encoded (0 or 1)
        LogicalBit: int
        /// Error that was injected (if any)
        InjectedError: (ErrorType * int) option
        /// Syndrome measurement
        Syndrome: SyndromeResult
        /// Whether correction was applied
        CorrectionApplied: bool
        /// Decoded bit after correction
        DecodedBit: int
        /// Whether the round-trip succeeded (decoded == encoded)
        Success: bool
        /// Backend used
        BackendName: string
    }

    // ========================================================================
    // CODE PARAMETERS
    // ========================================================================

    /// Get the parameters for a given error correction code
    let codeParameters (code: ErrorCode) : CodeParameters =
        match code with
        | BitFlipCode3 ->
            { Code = BitFlipCode3; PhysicalQubits = 3; LogicalQubits = 1
              Distance = 1; CorrectableErrors = 1 }
        | PhaseFlipCode3 ->
            { Code = PhaseFlipCode3; PhysicalQubits = 3; LogicalQubits = 1
              Distance = 1; CorrectableErrors = 1 }
        | ShorCode9 ->
            { Code = ShorCode9; PhysicalQubits = 9; LogicalQubits = 1
              Distance = 3; CorrectableErrors = 1 }
        | SteaneCode7 ->
            { Code = SteaneCode7; PhysicalQubits = 7; LogicalQubits = 1
              Distance = 3; CorrectableErrors = 1 }

    // ========================================================================
    // INTENT -> PLAN -> EXECUTE (shared infrastructure)
    // ========================================================================

    /// Required operations for error correction circuits
    let private requiredOps : QuantumOperation list =
        [ QuantumOperation.Gate (H 0)
          QuantumOperation.Gate (X 0)
          QuantumOperation.Gate (Z 0)
          QuantumOperation.Gate (CNOT (0, 1)) ]

    /// Validate that the backend supports gate-based QEC
    let private validateBackend (moduleName: string) (backend: IQuantumBackend) : Result<unit, QuantumError> =
        match backend.NativeStateType with
        | QuantumStateType.Annealing ->
            Error (QuantumError.OperationError (moduleName, $"Backend '{backend.Name}' does not support quantum error correction (native state type: {backend.NativeStateType})"))
        | _ ->
            if requiredOps |> List.forall backend.SupportsOperation then
                Ok ()
            else
                Error (QuantumError.OperationError (moduleName, $"Backend '{backend.Name}' does not support required gate operations for quantum error correction"))

    // ========================================================================
    // ERROR INJECTION UTILITIES
    // ========================================================================

    /// Inject a bit-flip (X) error on a specific qubit
    let injectBitFlip
        (backend: IQuantumBackend)
        (qubit: int)
        (state: QuantumState)
        : Result<QuantumState, QuantumError> =
        backend.ApplyOperation (QuantumOperation.Gate (X qubit)) state

    /// Inject a phase-flip (Z) error on a specific qubit
    let injectPhaseFlip
        (backend: IQuantumBackend)
        (qubit: int)
        (state: QuantumState)
        : Result<QuantumState, QuantumError> =
        backend.ApplyOperation (QuantumOperation.Gate (Z qubit)) state

    /// Inject a combined bit+phase flip (Y, up to global phase) error on a specific qubit
    let injectCombinedError
        (backend: IQuantumBackend)
        (qubit: int)
        (state: QuantumState)
        : Result<QuantumState, QuantumError> =
        result {
            // Y = iXZ, apply X then Z (global phase i is irrelevant for measurement)
            let! afterX = backend.ApplyOperation (QuantumOperation.Gate (X qubit)) state
            return! backend.ApplyOperation (QuantumOperation.Gate (Z qubit)) afterX
        }

    /// Inject an error of a given type on a given qubit
    let injectError
        (backend: IQuantumBackend)
        (errorType: ErrorType)
        (qubit: int)
        (state: QuantumState)
        : Result<QuantumState, QuantumError> =
        match errorType with
        | BitFlipError -> injectBitFlip backend qubit state
        | PhaseFlipError -> injectPhaseFlip backend qubit state
        | CombinedError -> injectCombinedError backend qubit state
        | UncorrectableError ->
            Error (QuantumError.OperationError ("QuantumErrorCorrection", "Cannot inject an uncorrectable error: it is a detection-only classification"))

    // ========================================================================
    // 3-QUBIT BIT-FLIP CODE [[3,1,1]]
    // ========================================================================

    /// 3-qubit bit-flip code
    ///
    /// Encoding:
    ///   |0> -> |000>
    ///   |1> -> |111>
    ///   a|0> + b|1> -> a|000> + b|111>
    ///
    /// Syndrome measurement (using ancilla qubits):
    ///   s1 = Z0 x Z1 (parity of qubits 0,1)
    ///   s2 = Z1 x Z2 (parity of qubits 1,2)
    ///
    ///   (s1, s2) = (0,0) -> no error
    ///   (s1, s2) = (1,0) -> error on qubit 0
    ///   (s1, s2) = (1,1) -> error on qubit 1
    ///   (s1, s2) = (0,1) -> error on qubit 2
    ///
    /// Correction: Apply X to the identified error qubit
    module BitFlip =

        /// Total qubits: 3 data + 2 ancilla = 5
        let private dataQubits = [| 0; 1; 2 |]
        let private ancilla1 = 3
        let private ancilla2 = 4
        let private totalQubits = 5

        /// Encode a logical bit (0 or 1) into the 3-qubit bit-flip code
        ///
        /// Uses qubits 0,1,2 as data qubits. Qubits 3,4 are ancilla (unused during encoding).
        /// If logicalBit = 1, first applies X to qubit 0 to prepare |1>.
        /// Then CNOT(0,1) and CNOT(0,2) to spread the state:
        ///   |0> -> |000>
        ///   |1> -> |111>
        let encode
            (backend: IQuantumBackend)
            (logicalBit: int)
            : Result<EncodingResult, QuantumError> =

            result {
                do! validateBackend "BitFlip.encode" backend

                do!
                    if logicalBit <> 0 && logicalBit <> 1 then
                        Error (QuantumError.ValidationError ("logicalBit", "must be 0 or 1"))
                    else
                        Ok ()

                let! initialState = backend.InitializeState totalQubits

                // Prepare logical state on qubit 0
                let! preparedState =
                    if logicalBit = 1 then
                        backend.ApplyOperation (QuantumOperation.Gate (X dataQubits.[0])) initialState
                    else
                        Ok initialState

                // Encode: CNOT(0,1) then CNOT(0,2)
                let encodeOps = [
                    QuantumOperation.Gate (CNOT (dataQubits.[0], dataQubits.[1]))
                    QuantumOperation.Gate (CNOT (dataQubits.[0], dataQubits.[2]))
                ]
                let! encodedState = UnifiedBackend.applySequence backend encodeOps preparedState

                return {
                    Code = BitFlipCode3
                    PhysicalQubits = 3
                    EncodedState = encodedState
                }
            }

        /// Measure the syndrome using ancilla qubits
        ///
        /// Syndrome extraction circuit:
        ///   CNOT(data0, ancilla1), CNOT(data1, ancilla1) -> ancilla1 = parity(q0, q1)
        ///   CNOT(data1, ancilla2), CNOT(data2, ancilla2) -> ancilla2 = parity(q1, q2)
        ///
        /// Then measure ancilla qubits to get syndrome bits.
        ///
        /// Returns the syndrome result AND the post-syndrome quantum state.
        /// The returned state includes the ancilla entanglement from syndrome
        /// extraction. Correction gates must be applied to this state, not
        /// the pre-syndrome state, to match real quantum hardware behavior.
        let measureSyndrome
            (backend: IQuantumBackend)
            (state: QuantumState)
            : Result<SyndromeResult * QuantumState, QuantumError> =

            result {
                // Extract syndrome via CNOT to ancilla
                let syndromeOps = [
                    // s1 = parity(q0, q1): CNOT q0->a1, CNOT q1->a1
                    QuantumOperation.Gate (CNOT (dataQubits.[0], ancilla1))
                    QuantumOperation.Gate (CNOT (dataQubits.[1], ancilla1))
                    // s2 = parity(q1, q2): CNOT q1->a2, CNOT q2->a2
                    QuantumOperation.Gate (CNOT (dataQubits.[1], ancilla2))
                    QuantumOperation.Gate (CNOT (dataQubits.[2], ancilla2))
                ]
                let! afterSyndrome = UnifiedBackend.applySequence backend syndromeOps state

                // Measure ancilla qubits
                let measurements = QuantumState.measure afterSyndrome 1
                let bits = measurements.[0]
                let s1 = bits.[ancilla1]
                let s2 = bits.[ancilla2]

                let (detectedError, errorQubit) =
                    match (s1, s2) with
                    | (0, 0) -> (None, None)
                    | (1, 0) -> (Some BitFlipError, Some dataQubits.[0])
                    | (1, 1) -> (Some BitFlipError, Some dataQubits.[1])
                    | (0, 1) -> (Some BitFlipError, Some dataQubits.[2])
                    | _ -> (None, None)

                return ({
                    SyndromeBits = [ s1; s2 ]
                    DetectedError = detectedError
                    ErrorQubit = errorQubit
                }, afterSyndrome)
            }

        /// Apply correction based on syndrome result
        let correct
            (backend: IQuantumBackend)
            (state: QuantumState)
            (syndrome: SyndromeResult)
            : Result<CorrectionResult, QuantumError> =

            result {
                match syndrome.ErrorQubit with
                | Some qubit ->
                    let! correctedState =
                        backend.ApplyOperation (QuantumOperation.Gate (X qubit)) state
                    return {
                        Syndrome = syndrome
                        CorrectedState = correctedState
                        CorrectionApplied = true
                    }
                | None ->
                    return {
                        Syndrome = syndrome
                        CorrectedState = state
                        CorrectionApplied = false
                    }
            }

        /// Decode: measure qubit 0 to recover the logical bit
        let private decode (state: QuantumState) : int =
            let measurements = QuantumState.measure state 1
            measurements.[0].[dataQubits.[0]]

        /// Full round-trip: encode -> inject error -> measure syndrome -> correct -> decode
        let roundTrip
            (backend: IQuantumBackend)
            (logicalBit: int)
            (errorOnQubit: int option)
            : Result<RoundTripResult, QuantumError> =

            result {
                let! encoded = encode backend logicalBit

                // Optionally inject a bit-flip error
                let! afterError =
                    match errorOnQubit with
                    | Some q -> injectBitFlip backend q encoded.EncodedState
                    | None -> Ok encoded.EncodedState

                // Measure syndrome (returns post-syndrome state for correction)
                let! (syndrome, postSyndromeState) = measureSyndrome backend afterError

                // Apply correction to the post-syndrome state
                let! correction = correct backend postSyndromeState syndrome

                // Decode
                let decodedBit = decode correction.CorrectedState

                let injectedError =
                    errorOnQubit |> Option.map (fun q -> (BitFlipError, q))

                return {
                    Code = BitFlipCode3
                    LogicalBit = logicalBit
                    InjectedError = injectedError
                    Syndrome = syndrome
                    CorrectionApplied = correction.CorrectionApplied
                    DecodedBit = decodedBit
                    Success = (decodedBit = logicalBit)
                    BackendName = backend.Name
                }
            }

    // ========================================================================
    // 3-QUBIT PHASE-FLIP CODE [[3,1,1]]
    // ========================================================================

    /// 3-qubit phase-flip code (dual of bit-flip code)
    ///
    /// Encoding:
    ///   |0> -> |+++>
    ///   |1> -> |--->
    ///   a|0> + b|1> -> a|+++> + b|--->
    ///
    /// This is achieved by first applying the bit-flip encoding
    /// (CNOT spreading), then applying H to all three qubits to move
    /// from the Z-basis to the X-basis.
    ///
    /// Syndrome measurement: Transform to Z-basis (apply H to all),
    /// measure Z-parity syndromes, then transform back.
    ///
    /// Correction: Apply Z to the identified error qubit
    module PhaseFlip =

        let private dataQubits = [| 0; 1; 2 |]
        let private ancilla1 = 3
        let private ancilla2 = 4
        let private totalQubits = 5

        /// Encode a logical bit into the 3-qubit phase-flip code
        ///
        /// Steps:
        /// 1. If logicalBit = 1, apply X to qubit 0
        /// 2. CNOT(0,1), CNOT(0,2) to spread (like bit-flip)
        /// 3. H on all 3 data qubits to move to X-basis
        ///    |000> -> |+++>, |111> -> |--->
        let encode
            (backend: IQuantumBackend)
            (logicalBit: int)
            : Result<EncodingResult, QuantumError> =

            result {
                do! validateBackend "PhaseFlip.encode" backend

                do!
                    if logicalBit <> 0 && logicalBit <> 1 then
                        Error (QuantumError.ValidationError ("logicalBit", "must be 0 or 1"))
                    else
                        Ok ()

                let! initialState = backend.InitializeState totalQubits

                let! preparedState =
                    if logicalBit = 1 then
                        backend.ApplyOperation (QuantumOperation.Gate (X dataQubits.[0])) initialState
                    else
                        Ok initialState

                // Bit-flip encoding + Hadamard on all data qubits
                let encodeOps = [
                    QuantumOperation.Gate (CNOT (dataQubits.[0], dataQubits.[1]))
                    QuantumOperation.Gate (CNOT (dataQubits.[0], dataQubits.[2]))
                    QuantumOperation.Gate (H dataQubits.[0])
                    QuantumOperation.Gate (H dataQubits.[1])
                    QuantumOperation.Gate (H dataQubits.[2])
                ]
                let! encodedState = UnifiedBackend.applySequence backend encodeOps preparedState

                return {
                    Code = PhaseFlipCode3
                    PhysicalQubits = 3
                    EncodedState = encodedState
                }
            }

        /// Measure syndrome for phase-flip detection
        ///
        /// A phase-flip Z in the X-basis is equivalent to a bit-flip X in the Z-basis.
        /// So we:
        /// 1. Apply H to all data qubits (move to Z-basis)
        /// 2. Measure Z-parity syndromes via CNOT to ancilla (same as bit-flip code)
        /// 3. Apply H to all data qubits (move back to X-basis)
        ///
        /// Returns the syndrome result AND the post-syndrome state (data qubits
        /// restored to X-basis encoding, ancilla entangled from syndrome extraction).
        let measureSyndrome
            (backend: IQuantumBackend)
            (state: QuantumState)
            : Result<SyndromeResult * QuantumState, QuantumError> =

            result {
                // Transform to Z-basis
                let toZBasis = [
                    QuantumOperation.Gate (H dataQubits.[0])
                    QuantumOperation.Gate (H dataQubits.[1])
                    QuantumOperation.Gate (H dataQubits.[2])
                ]
                let! inZBasis = UnifiedBackend.applySequence backend toZBasis state

                // Extract syndrome via CNOT to ancilla (same as bit-flip)
                let syndromeOps = [
                    QuantumOperation.Gate (CNOT (dataQubits.[0], ancilla1))
                    QuantumOperation.Gate (CNOT (dataQubits.[1], ancilla1))
                    QuantumOperation.Gate (CNOT (dataQubits.[1], ancilla2))
                    QuantumOperation.Gate (CNOT (dataQubits.[2], ancilla2))
                ]
                let! afterSyndrome = UnifiedBackend.applySequence backend syndromeOps inZBasis

                // Measure ancilla
                let measurements = QuantumState.measure afterSyndrome 1
                let bits = measurements.[0]
                let s1 = bits.[ancilla1]
                let s2 = bits.[ancilla2]

                // Transform back to X-basis (restore encoding basis for correction)
                let toXBasis = [
                    QuantumOperation.Gate (H dataQubits.[0])
                    QuantumOperation.Gate (H dataQubits.[1])
                    QuantumOperation.Gate (H dataQubits.[2])
                ]
                let! restoredState = UnifiedBackend.applySequence backend toXBasis afterSyndrome

                let (detectedError, errorQubit) =
                    match (s1, s2) with
                    | (0, 0) -> (None, None)
                    | (1, 0) -> (Some PhaseFlipError, Some dataQubits.[0])
                    | (1, 1) -> (Some PhaseFlipError, Some dataQubits.[1])
                    | (0, 1) -> (Some PhaseFlipError, Some dataQubits.[2])
                    | _ -> (None, None)

                return ({
                    SyndromeBits = [ s1; s2 ]
                    DetectedError = detectedError
                    ErrorQubit = errorQubit
                }, restoredState)
            }

        /// Apply correction based on syndrome
        let correct
            (backend: IQuantumBackend)
            (state: QuantumState)
            (syndrome: SyndromeResult)
            : Result<CorrectionResult, QuantumError> =

            result {
                match syndrome.ErrorQubit with
                | Some qubit ->
                    // For phase-flip code, correct with Z gate
                    let! correctedState =
                        backend.ApplyOperation (QuantumOperation.Gate (Z qubit)) state
                    return {
                        Syndrome = syndrome
                        CorrectedState = correctedState
                        CorrectionApplied = true
                    }
                | None ->
                    return {
                        Syndrome = syndrome
                        CorrectedState = state
                        CorrectionApplied = false
                    }
            }

        /// Decode: apply H to all data qubits, then measure qubit 0
        let private decode
            (backend: IQuantumBackend)
            (state: QuantumState)
            : Result<int, QuantumError> =

            result {
                // Move back to Z-basis for decoding
                let toZBasis = [
                    QuantumOperation.Gate (H dataQubits.[0])
                    QuantumOperation.Gate (H dataQubits.[1])
                    QuantumOperation.Gate (H dataQubits.[2])
                ]
                let! inZBasis = UnifiedBackend.applySequence backend toZBasis state
                let measurements = QuantumState.measure inZBasis 1
                return measurements.[0].[dataQubits.[0]]
            }

        /// Full round-trip: encode -> inject error -> syndrome -> correct -> decode
        let roundTrip
            (backend: IQuantumBackend)
            (logicalBit: int)
            (errorOnQubit: int option)
            : Result<RoundTripResult, QuantumError> =

            result {
                let! encoded = encode backend logicalBit

                let! afterError =
                    match errorOnQubit with
                    | Some q -> injectPhaseFlip backend q encoded.EncodedState
                    | None -> Ok encoded.EncodedState

                // Measure syndrome (returns post-syndrome state with data qubits
                // restored to X-basis encoding after H→CNOT→H-back round-trip)
                let! (syndrome, postSyndromeState) = measureSyndrome backend afterError

                // Apply correction to the post-syndrome state
                let! correction = correct backend postSyndromeState syndrome

                let! decodedBit = decode backend correction.CorrectedState

                let injectedError =
                    errorOnQubit |> Option.map (fun q -> (PhaseFlipError, q))

                return {
                    Code = PhaseFlipCode3
                    LogicalBit = logicalBit
                    InjectedError = injectedError
                    Syndrome = syndrome
                    CorrectionApplied = correction.CorrectionApplied
                    DecodedBit = decodedBit
                    Success = (decodedBit = logicalBit)
                    BackendName = backend.Name
                }
            }

    // ========================================================================
    // SHOR 9-QUBIT CODE [[9,1,3]]
    // ========================================================================

    /// Shor's 9-qubit code (first quantum error correcting code)
    ///
    /// The Shor code is a concatenation of the phase-flip and bit-flip codes.
    /// It uses 9 physical qubits to encode 1 logical qubit, and can correct
    /// any single-qubit error (bit-flip, phase-flip, or both).
    ///
    /// Encoding:
    ///   |0> -> (|000> + |111>)(|000> + |111>)(|000> + |111>) / 2*sqrt(2)
    ///   |1> -> (|000> - |111>)(|000> - |111>)(|000> - |111>) / 2*sqrt(2)
    ///
    /// Structure:
    ///   Step 1: Phase-flip protection across 3 blocks (qubits 0,3,6)
    ///   Step 2: Bit-flip protection within each block (qubits 0-2, 3-5, 6-8)
    ///
    /// Syndrome:
    ///   6 bit-flip syndrome bits (2 per block)
    ///   2 phase-flip syndrome bits (inter-block parity)
    module Shor =

        /// Data qubits: 9 (0..8), Ancilla: 8 (9..16), Total: 17
        /// Bit-flip ancilla: 9,10 (block 0), 11,12 (block 1), 13,14 (block 2)
        /// Phase-flip ancilla: 15,16
        let private totalQubits = 17

        // Block structure: 3 blocks of 3 qubits
        let private block0 = [| 0; 1; 2 |]
        let private block1 = [| 3; 4; 5 |]
        let private block2 = [| 6; 7; 8 |]

        // Bit-flip ancilla per block
        let private bfAncilla0 = (9, 10)
        let private bfAncilla1 = (11, 12)
        let private bfAncilla2 = (13, 14)

        // Phase-flip ancilla
        let private pfAncilla1 = 15
        let private pfAncilla2 = 16

        /// Encode a logical bit into the Shor 9-qubit code
        ///
        /// Steps:
        /// 1. Prepare logical state on qubit 0
        /// 2. Phase-flip encoding: CNOT(0,3), CNOT(0,6), then H on 0,3,6
        /// 3. Bit-flip encoding per block: CNOT(0,1), CNOT(0,2) for each block
        let encode
            (backend: IQuantumBackend)
            (logicalBit: int)
            : Result<EncodingResult, QuantumError> =

            result {
                do! validateBackend "Shor.encode" backend

                do!
                    if logicalBit <> 0 && logicalBit <> 1 then
                        Error (QuantumError.ValidationError ("logicalBit", "must be 0 or 1"))
                    else
                        Ok ()

                let! initialState = backend.InitializeState totalQubits

                let! preparedState =
                    if logicalBit = 1 then
                        backend.ApplyOperation (QuantumOperation.Gate (X 0)) initialState
                    else
                        Ok initialState

                // Phase-flip encoding: spread across blocks
                let phaseEncOps = [
                    QuantumOperation.Gate (CNOT (0, 3))
                    QuantumOperation.Gate (CNOT (0, 6))
                    // Hadamard on each block leader
                    QuantumOperation.Gate (H 0)
                    QuantumOperation.Gate (H 3)
                    QuantumOperation.Gate (H 6)
                ]
                let! afterPhase = UnifiedBackend.applySequence backend phaseEncOps preparedState

                // Bit-flip encoding within each block
                let bitEncOps = [
                    // Block 0: CNOT(0,1), CNOT(0,2)
                    QuantumOperation.Gate (CNOT (block0.[0], block0.[1]))
                    QuantumOperation.Gate (CNOT (block0.[0], block0.[2]))
                    // Block 1: CNOT(3,4), CNOT(3,5)
                    QuantumOperation.Gate (CNOT (block1.[0], block1.[1]))
                    QuantumOperation.Gate (CNOT (block1.[0], block1.[2]))
                    // Block 2: CNOT(6,7), CNOT(6,8)
                    QuantumOperation.Gate (CNOT (block2.[0], block2.[1]))
                    QuantumOperation.Gate (CNOT (block2.[0], block2.[2]))
                ]
                let! encodedState = UnifiedBackend.applySequence backend bitEncOps afterPhase

                return {
                    Code = ShorCode9
                    PhysicalQubits = 9
                    EncodedState = encodedState
                }
            }

        /// Measure bit-flip syndrome for one block using ancilla
        let private measureBlockBitFlipSyndrome
            (backend: IQuantumBackend)
            (block: int[])
            (anc1: int)
            (anc2: int)
            (state: QuantumState)
            : Result<int * int * QuantumState, QuantumError> =

            result {
                let syndromeOps = [
                    // s1 = parity(block[0], block[1])
                    QuantumOperation.Gate (CNOT (block.[0], anc1))
                    QuantumOperation.Gate (CNOT (block.[1], anc1))
                    // s2 = parity(block[1], block[2])
                    QuantumOperation.Gate (CNOT (block.[1], anc2))
                    QuantumOperation.Gate (CNOT (block.[2], anc2))
                ]
                let! afterSyndrome = UnifiedBackend.applySequence backend syndromeOps state
                let measurements = QuantumState.measure afterSyndrome 1
                let bits = measurements.[0]
                return (bits.[anc1], bits.[anc2], afterSyndrome)
            }

        /// Measure the full Shor code syndrome
        ///
        /// Returns 8 syndrome bits and the post-syndrome quantum state:
        ///   [s1_b0; s2_b0; s1_b1; s2_b1; s1_b2; s2_b2; s_pf1; s_pf2]
        ///
        /// Bit-flip syndrome per block (6 bits):
        ///   Same as 3-qubit bit-flip code within each block
        ///
        /// Phase-flip syndrome (2 bits):
        ///   Measure parity between blocks in the X-basis
        ///   H on support qubits, then CNOT to phase-flip ancilla, then H back
        ///
        /// The returned state includes all syndrome extraction gates applied.
        /// Correction gates must be applied to this state, not the pre-syndrome
        /// state, to match real quantum hardware behavior.
        let measureSyndrome
            (backend: IQuantumBackend)
            (state: QuantumState)
            : Result<SyndromeResult * QuantumState, QuantumError> =

            result {
                // === Bit-flip syndrome for each block ===
                let! (s1_b0, s2_b0, afterBf0) =
                    measureBlockBitFlipSyndrome backend block0 (fst bfAncilla0) (snd bfAncilla0) state
                let! (s1_b1, s2_b1, afterBf1) =
                    measureBlockBitFlipSyndrome backend block1 (fst bfAncilla1) (snd bfAncilla1) afterBf0
                let! (s1_b2, s2_b2, afterBf2) =
                    measureBlockBitFlipSyndrome backend block2 (fst bfAncilla2) (snd bfAncilla2) afterBf1

                // === Phase-flip syndrome ===
                // To detect phase-flip, we measure X-stabilizers between blocks.
                // X-stabilizer 1: X_0 X_1 X_2 X_3 X_4 X_5 (blocks 0 and 1)
                // X-stabilizer 2: X_3 X_4 X_5 X_6 X_7 X_8 (blocks 1 and 2)
                //
                // Measurement: H on support qubits, CNOT to ancilla (Z-parity), H back
                let phaseSetup = [
                    // Stabilizer 1: X on qubits 0-5
                    // H on all qubits in support
                    QuantumOperation.Gate (H 0)
                    QuantumOperation.Gate (H 1)
                    QuantumOperation.Gate (H 2)
                    QuantumOperation.Gate (H 3)
                    QuantumOperation.Gate (H 4)
                    QuantumOperation.Gate (H 5)
                    // CNOT from each to ancilla 1
                    QuantumOperation.Gate (CNOT (0, pfAncilla1))
                    QuantumOperation.Gate (CNOT (1, pfAncilla1))
                    QuantumOperation.Gate (CNOT (2, pfAncilla1))
                    QuantumOperation.Gate (CNOT (3, pfAncilla1))
                    QuantumOperation.Gate (CNOT (4, pfAncilla1))
                    QuantumOperation.Gate (CNOT (5, pfAncilla1))
                    // H back
                    QuantumOperation.Gate (H 0)
                    QuantumOperation.Gate (H 1)
                    QuantumOperation.Gate (H 2)
                    QuantumOperation.Gate (H 3)
                    QuantumOperation.Gate (H 4)
                    QuantumOperation.Gate (H 5)
                    // Stabilizer 2: X on qubits 3-8
                    QuantumOperation.Gate (H 3)
                    QuantumOperation.Gate (H 4)
                    QuantumOperation.Gate (H 5)
                    QuantumOperation.Gate (H 6)
                    QuantumOperation.Gate (H 7)
                    QuantumOperation.Gate (H 8)
                    // CNOT from each to ancilla 2
                    QuantumOperation.Gate (CNOT (3, pfAncilla2))
                    QuantumOperation.Gate (CNOT (4, pfAncilla2))
                    QuantumOperation.Gate (CNOT (5, pfAncilla2))
                    QuantumOperation.Gate (CNOT (6, pfAncilla2))
                    QuantumOperation.Gate (CNOT (7, pfAncilla2))
                    QuantumOperation.Gate (CNOT (8, pfAncilla2))
                    // H back
                    QuantumOperation.Gate (H 3)
                    QuantumOperation.Gate (H 4)
                    QuantumOperation.Gate (H 5)
                    QuantumOperation.Gate (H 6)
                    QuantumOperation.Gate (H 7)
                    QuantumOperation.Gate (H 8)
                ]
                let! afterPhase = UnifiedBackend.applySequence backend phaseSetup afterBf2

                let pfMeasurements = QuantumState.measure afterPhase 1
                let pfBits = pfMeasurements.[0]
                let s_pf1 = pfBits.[pfAncilla1]
                let s_pf2 = pfBits.[pfAncilla2]

                // Determine error from syndrome
                let bitFlipSyndromes = [
                    (s1_b0, s2_b0, block0)
                    (s1_b1, s2_b1, block1)
                    (s1_b2, s2_b2, block2)
                ]

                // Check for bit-flip error in any block
                let bitFlipQubit =
                    bitFlipSyndromes
                    |> List.tryPick (fun (s1, s2, block) ->
                        match (s1, s2) with
                        | (1, 0) -> Some block.[0]
                        | (1, 1) -> Some block.[1]
                        | (0, 1) -> Some block.[2]
                        | _ -> None)

                // Check for phase-flip error between blocks
                let phaseFlipBlock : int option =
                    match (s_pf1, s_pf2) with
                    | (0, 0) -> None
                    | (1, 0) -> Some 0  // Phase flip in block 0
                    | (1, 1) -> Some 1  // Phase flip in block 1
                    | (0, 1) -> Some 2  // Phase flip in block 2
                    | _ -> None

                let (detectedError, errorQubit) =
                    match (bitFlipQubit, phaseFlipBlock) with
                    | (Some q, None) -> (Some BitFlipError, Some q)
                    | (None, Some blockIdx) ->
                        // Phase flip on the block leader
                        let blocks = [| block0; block1; block2 |]
                        (Some PhaseFlipError, Some blocks.[blockIdx].[0])
                    | (Some q, Some _) -> (Some CombinedError, Some q)
                    | (None, None) -> (None, None)

                return ({
                    SyndromeBits = [ s1_b0; s2_b0; s1_b1; s2_b1; s1_b2; s2_b2; s_pf1; s_pf2 ]
                    DetectedError = detectedError
                    ErrorQubit = errorQubit
                }, afterPhase)
            }

        /// Apply correction based on syndrome
        let correct
            (backend: IQuantumBackend)
            (state: QuantumState)
            (syndrome: SyndromeResult)
            : Result<CorrectionResult, QuantumError> =

            result {
                match (syndrome.DetectedError, syndrome.ErrorQubit) with
                | (Some BitFlipError, Some qubit) ->
                    let! corrected = backend.ApplyOperation (QuantumOperation.Gate (X qubit)) state
                    return { Syndrome = syndrome; CorrectedState = corrected; CorrectionApplied = true }
                | (Some PhaseFlipError, Some qubit) ->
                    let! corrected = backend.ApplyOperation (QuantumOperation.Gate (Z qubit)) state
                    return { Syndrome = syndrome; CorrectedState = corrected; CorrectionApplied = true }
                | (Some CombinedError, Some qubit) ->
                    // Apply both X and Z (Y up to phase)
                    let! afterX = backend.ApplyOperation (QuantumOperation.Gate (X qubit)) state
                    let! afterZ = backend.ApplyOperation (QuantumOperation.Gate (Z qubit)) afterX
                    return { Syndrome = syndrome; CorrectedState = afterZ; CorrectionApplied = true }
                | (Some UncorrectableError, _) ->
                    // Multi-qubit error beyond code distance; no reliable correction possible
                    return { Syndrome = syndrome; CorrectedState = state; CorrectionApplied = false }
                | _ ->
                    return { Syndrome = syndrome; CorrectedState = state; CorrectionApplied = false }
            }

        /// Decode: undo encoding and measure logical qubit
        let private decode
            (backend: IQuantumBackend)
            (state: QuantumState)
            : Result<int, QuantumError> =

            result {
                // Undo bit-flip encoding
                let undoBitFlip = [
                    QuantumOperation.Gate (CNOT (block0.[0], block0.[2]))
                    QuantumOperation.Gate (CNOT (block0.[0], block0.[1]))
                    QuantumOperation.Gate (CNOT (block1.[0], block1.[2]))
                    QuantumOperation.Gate (CNOT (block1.[0], block1.[1]))
                    QuantumOperation.Gate (CNOT (block2.[0], block2.[2]))
                    QuantumOperation.Gate (CNOT (block2.[0], block2.[1]))
                ]
                let! afterUndoBf = UnifiedBackend.applySequence backend undoBitFlip state

                // Undo phase-flip encoding: H on block leaders, then undo CNOT spread
                let undoPhaseFlip = [
                    QuantumOperation.Gate (H 0)
                    QuantumOperation.Gate (H 3)
                    QuantumOperation.Gate (H 6)
                    QuantumOperation.Gate (CNOT (0, 6))
                    QuantumOperation.Gate (CNOT (0, 3))
                ]
                let! decoded = UnifiedBackend.applySequence backend undoPhaseFlip afterUndoBf

                let measurements = QuantumState.measure decoded 1
                return measurements.[0].[0]
            }

        /// Full round-trip test
        let roundTrip
            (backend: IQuantumBackend)
            (logicalBit: int)
            (errorType: ErrorType)
            (errorQubit: int)
            : Result<RoundTripResult, QuantumError> =

            result {
                let! encoded = encode backend logicalBit

                // Inject error
                let! afterError = injectError backend errorType errorQubit encoded.EncodedState

                // Measure syndrome (returns post-syndrome state for correction)
                let! (syndrome, postSyndromeState) = measureSyndrome backend afterError

                // Correct on post-syndrome state
                let! correction = correct backend postSyndromeState syndrome

                // Decode
                let! decodedBit = decode backend correction.CorrectedState

                return {
                    Code = ShorCode9
                    LogicalBit = logicalBit
                    InjectedError = Some (errorType, errorQubit)
                    Syndrome = syndrome
                    CorrectionApplied = correction.CorrectionApplied
                    DecodedBit = decodedBit
                    Success = (decodedBit = logicalBit)
                    BackendName = backend.Name
                }
            }

    // ========================================================================
    // STEANE 7-QUBIT CODE [[7,1,3]]
    // ========================================================================

    /// Steane's 7-qubit code (CSS code based on classical [7,4,3] Hamming code)
    ///
    /// This is a CSS (Calderbank-Shor-Steane) code that uses 7 physical qubits
    /// to encode 1 logical qubit. It can correct any single-qubit error.
    ///
    /// The code is based on the classical [7,4,3] Hamming code, which has
    /// parity check matrix:
    ///   H = | 0 0 0 1 1 1 1 |
    ///       | 0 1 1 0 0 1 1 |
    ///       | 1 0 1 0 1 0 1 |
    ///
    /// Stabilizer generators:
    ///   X-type (detect Z errors): X0X1X2X3, X1X2X4X5, X2X4X6 (wrong)
    ///
    /// Actually, the 6 stabilizers are:
    ///   g1 = I I I X X X X    (X on qubits 3,4,5,6)
    ///   g2 = I X X I I X X    (X on qubits 1,2,5,6)
    ///   g3 = X I X I X I X    (X on qubits 0,2,4,6)
    ///   g4 = I I I Z Z Z Z    (Z on qubits 3,4,5,6)
    ///   g5 = I Z Z I I Z Z    (Z on qubits 1,2,5,6)
    ///   g6 = Z I Z I Z I Z    (Z on qubits 0,2,4,6)
    ///
    /// Encoding:
    ///   |0_L> = (1/sqrt(8)) * sum of all even-weight codewords of [7,4,3] code
    ///   |1_L> = (1/sqrt(8)) * sum of all odd-weight codewords of [7,4,3] code
    ///
    /// Syndrome: 3 X-stabilizer checks detect Z errors,
    ///           3 Z-stabilizer checks detect X errors.
    ///           The 3-bit syndrome maps directly to error location (Hamming code).
    module Steane =

        /// Data qubits: 7 (0..6), Ancilla: 6 (7..12), Total: 13
        /// X-stabilizer ancilla: 7,8,9 (for detecting Z errors)
        /// Z-stabilizer ancilla: 10,11,12 (for detecting X errors)
        let private totalQubits = 13

        // Stabilizer structure based on [7,4,3] Hamming parity check matrix
        // Each row of H identifies which qubits participate in each stabilizer
        // Row 1: qubits 3,4,5,6  (0-indexed)
        // Row 2: qubits 1,2,5,6
        // Row 3: qubits 0,2,4,6
        let private stabilizer1Qubits = [| 3; 4; 5; 6 |]
        let private stabilizer2Qubits = [| 1; 2; 5; 6 |]
        let private stabilizer3Qubits = [| 0; 2; 4; 6 |]

        let private xAncilla = [| 7; 8; 9 |]
        let private zAncilla = [| 10; 11; 12 |]

        /// Encode a logical bit into the Steane 7-qubit code
        ///
        /// The encoding circuit prepares the logical |0> or |1> state
        /// using a sequence of Hadamard and CNOT gates that project
        /// into the code space.
        ///
        /// Encoding circuit for |0_L>:
        ///   1. Start with |0000000>
        ///   2. H on qubits 0,1,3 (generators of the [7,4] code)
        ///   3. CNOT cascade to create codeword superposition
        ///
        /// For |1_L>: Start with X on qubit 0 (or equivalently, apply
        /// logical X after encoding |0_L>)
        let encode
            (backend: IQuantumBackend)
            (logicalBit: int)
            : Result<EncodingResult, QuantumError> =

            result {
                do! validateBackend "Steane.encode" backend

                do!
                    if logicalBit <> 0 && logicalBit <> 1 then
                        Error (QuantumError.ValidationError ("logicalBit", "must be 0 or 1"))
                    else
                        Ok ()

                let! initialState = backend.InitializeState totalQubits

                // Encode |0_L> using the Steane encoding circuit.
                // |0_L> = (1/√8) * sum of all 8 codewords of the [7,3,4] dual code C^⊥.
                //
                // C^⊥ generators (rows of the parity check matrix):
                //   h1 = 0001111  (bits 3,4,5,6)
                //   h2 = 0110011  (bits 1,2,5,6)
                //   h3 = 1010101  (bits 0,2,4,6)
                //
                // Circuit:
                //   H on q0, q1, q3 (superpose the generator indices)
                //   CNOT(q3, q4), CNOT(q3, q5), CNOT(q3, q6)  -- h1 contributions
                //   CNOT(q1, q2), CNOT(q1, q5), CNOT(q1, q6)  -- h2 contributions
                //   CNOT(q0, q2), CNOT(q0, q4), CNOT(q0, q6)  -- h3 contributions
                let encodeOps = [
                    QuantumOperation.Gate (H 0)
                    QuantumOperation.Gate (H 1)
                    QuantumOperation.Gate (H 3)
                    // h1: q3 → q4, q5, q6
                    QuantumOperation.Gate (CNOT (3, 4))
                    QuantumOperation.Gate (CNOT (3, 5))
                    QuantumOperation.Gate (CNOT (3, 6))
                    // h2: q1 → q2, q5, q6
                    QuantumOperation.Gate (CNOT (1, 2))
                    QuantumOperation.Gate (CNOT (1, 5))
                    QuantumOperation.Gate (CNOT (1, 6))
                    // h3: q0 → q2, q4, q6
                    QuantumOperation.Gate (CNOT (0, 2))
                    QuantumOperation.Gate (CNOT (0, 4))
                    QuantumOperation.Gate (CNOT (0, 6))
                ]
                let! encodedState = UnifiedBackend.applySequence backend encodeOps initialState

                // For |1_L>, apply logical X (transversal X on all 7 data qubits)
                let! finalState =
                    if logicalBit = 1 then
                        let logicalXOps =
                            [ for i in 0..6 -> QuantumOperation.Gate (X i) ]
                        UnifiedBackend.applySequence backend logicalXOps encodedState
                    else
                        Ok encodedState

                return {
                    Code = SteaneCode7
                    PhysicalQubits = 7
                    EncodedState = finalState
                }
            }

        /// Measure X-stabilizer syndrome (detects Z errors)
        ///
        /// For simulation with non-collapsing measurement, we use a direct approach:
        ///   1. Apply H to all 7 data qubits (transforms Z-basis to X-basis)
        ///   2. Sample the state (measures parity in X-basis)
        ///   3. Apply H back to restore the state
        ///   4. Compute syndrome bits from the sampled values
        ///
        /// Returns syndrome bits and the post-syndrome state (data qubits
        /// restored after H→sample→H-back round-trip). On a real quantum
        /// computer, this would use ancilla-based indirect measurement.
        let private measureXSyndrome
            (backend: IQuantumBackend)
            (state: QuantumState)
            : Result<int[] * QuantumState, QuantumError> =

            result {
                // Apply H to all 7 data qubits
                let hOps = [ for i in 0..6 -> QuantumOperation.Gate (H i) ]
                let! afterH = UnifiedBackend.applySequence backend hOps state

                // Sample the state
                let measurements = QuantumState.measure afterH 1
                let bits = measurements.[0]

                // Compute syndrome: parity of each stabilizer group in the H-transformed basis
                // Stabilizer 1 (X on 3,4,5,6): parity of bits 3,4,5,6 after H
                let s1 = (bits.[3] + bits.[4] + bits.[5] + bits.[6]) % 2
                // Stabilizer 2 (X on 1,2,5,6): parity of bits 1,2,5,6 after H
                let s2 = (bits.[1] + bits.[2] + bits.[5] + bits.[6]) % 2
                // Stabilizer 3 (X on 0,2,4,6): parity of bits 0,2,4,6 after H
                let s3 = (bits.[0] + bits.[2] + bits.[4] + bits.[6]) % 2

                let syndrome = [| s1; s2; s3 |]

                // Apply H back to restore original encoding basis
                let! restoredState = UnifiedBackend.applySequence backend hOps afterH

                return (syndrome, restoredState)
            }

        /// Measure Z-stabilizer syndrome (detects X errors)
        ///
        /// Direct computation: sample data qubits and compute parity per stabilizer.
        let private measureZSyndrome
            (backend: IQuantumBackend)
            (state: QuantumState)
            : Result<int[] * QuantumState, QuantumError> =

            result {
                // Sample the state directly (Z-basis)
                let measurements = QuantumState.measure state 1
                let bits = measurements.[0]

                // Compute syndrome: parity of each stabilizer group
                // Stabilizer 1 (Z on 3,4,5,6): parity of bits 3,4,5,6
                let s1 = (bits.[3] + bits.[4] + bits.[5] + bits.[6]) % 2
                // Stabilizer 2 (Z on 1,2,5,6): parity of bits 1,2,5,6
                let s2 = (bits.[1] + bits.[2] + bits.[5] + bits.[6]) % 2
                // Stabilizer 3 (Z on 0,2,4,6): parity of bits 0,2,4,6
                let s3 = (bits.[0] + bits.[2] + bits.[4] + bits.[6]) % 2

                let syndrome = [| s1; s2; s3 |]

                return (syndrome, state)
            }

        /// Decode Hamming syndrome (3-bit) to error qubit index
        ///
        /// The syndrome bits form a binary number that directly identifies
        /// the error position (1-indexed in Hamming, 0-indexed in our qubits):
        ///   000 -> no error
        ///   001 -> qubit 0
        ///   010 -> qubit 1
        ///   011 -> qubit 2 (= qubit at position 3-1 in 1-indexed)
        ///   100 -> qubit 3
        ///   101 -> qubit 4
        ///   110 -> qubit 5
        ///   111 -> qubit 6
        let private decodeSyndrome (syndrome: int[]) : int option =
            let value = syndrome.[0] * 4 + syndrome.[1] * 2 + syndrome.[2]
            if value = 0 then None
            else Some (value - 1)  // Convert 1-indexed to 0-indexed

        /// Measure full syndrome (X and Z stabilizers)
        ///
        /// Returns the syndrome result AND the post-syndrome quantum state.
        /// The X-stabilizer measurement applies H→sample→H-back (restoring basis).
        /// The Z-stabilizer measurement samples directly (no state modification).
        /// Correction gates must be applied to the returned state.
        let measureSyndrome
            (backend: IQuantumBackend)
            (state: QuantumState)
            : Result<SyndromeResult * QuantumState, QuantumError> =

            result {
                // X-stabilizers detect Z errors (applies H→sample→H-back)
                let! (xSyndrome, afterXSyndrome) = measureXSyndrome backend state

                // Z-stabilizers detect X errors (direct sampling, state unchanged)
                let! (zSyndrome, afterZSyndrome) = measureZSyndrome backend afterXSyndrome

                let xErrorQubit = decodeSyndrome xSyndrome  // Z error location
                let zErrorQubit = decodeSyndrome zSyndrome  // X error location

                let (detectedError, errorQubit) =
                    match (zErrorQubit, xErrorQubit) with
                    | (Some zq, Some xq) when zq = xq ->
                        (Some CombinedError, Some zq)
                    | (Some zq, None) ->
                        (Some BitFlipError, Some zq)
                    | (None, Some xq) ->
                        (Some PhaseFlipError, Some xq)
                    | (Some _zq, Some _xq) ->
                        // Different qubits: multi-qubit error beyond code distance
                        (Some UncorrectableError, None)
                    | (None, None) ->
                        (None, None)

                let allBits =
                    (Array.toList zSyndrome) @ (Array.toList xSyndrome)

                return ({
                    SyndromeBits = allBits
                    DetectedError = detectedError
                    ErrorQubit = errorQubit
                }, afterZSyndrome)
            }

        /// Apply correction based on syndrome
        let correct
            (backend: IQuantumBackend)
            (state: QuantumState)
            (syndrome: SyndromeResult)
            : Result<CorrectionResult, QuantumError> =

            result {
                match (syndrome.DetectedError, syndrome.ErrorQubit) with
                | (Some BitFlipError, Some qubit) ->
                    let! corrected = backend.ApplyOperation (QuantumOperation.Gate (X qubit)) state
                    return { Syndrome = syndrome; CorrectedState = corrected; CorrectionApplied = true }
                | (Some PhaseFlipError, Some qubit) ->
                    let! corrected = backend.ApplyOperation (QuantumOperation.Gate (Z qubit)) state
                    return { Syndrome = syndrome; CorrectedState = corrected; CorrectionApplied = true }
                | (Some CombinedError, Some qubit) ->
                    let! afterX = backend.ApplyOperation (QuantumOperation.Gate (X qubit)) state
                    let! afterZ = backend.ApplyOperation (QuantumOperation.Gate (Z qubit)) afterX
                    return { Syndrome = syndrome; CorrectedState = afterZ; CorrectionApplied = true }
                | (Some UncorrectableError, _) ->
                    // Multi-qubit error beyond code distance; no reliable correction possible
                    return { Syndrome = syndrome; CorrectedState = state; CorrectionApplied = false }
                | _ ->
                    return { Syndrome = syndrome; CorrectedState = state; CorrectionApplied = false }
            }

        /// Decode: measure all 7 data qubits and compute parity
        ///
        /// For the Steane code, the logical bit is determined by the weight
        /// parity of the codeword:
        ///   |0_L> is a superposition of even-weight codewords of C^⊥
        ///   |1_L> = X^⊗7|0_L> is a superposition of odd-weight codewords
        ///
        /// After error correction, the state is back in the code space.
        /// Measuring all 7 qubits and computing the parity of 1s gives
        /// the logical bit directly: even parity → 0, odd parity → 1.
        let private decode
            (_backend: IQuantumBackend)
            (state: QuantumState)
            : Result<int, QuantumError> =

            result {
                let measurements = QuantumState.measure state 1
                let bits = measurements.[0]
                // Logical bit = parity of all 7 data qubits
                let parity = (bits.[0] + bits.[1] + bits.[2] + bits.[3] + bits.[4] + bits.[5] + bits.[6]) % 2
                return parity
            }

        /// Full round-trip test
        let roundTrip
            (backend: IQuantumBackend)
            (logicalBit: int)
            (errorType: ErrorType)
            (errorQubit: int)
            : Result<RoundTripResult, QuantumError> =

            result {
                let! encoded = encode backend logicalBit

                let! afterError = injectError backend errorType errorQubit encoded.EncodedState

                // Measure syndrome (returns post-syndrome state for correction)
                let! (syndrome, postSyndromeState) = measureSyndrome backend afterError

                // Correct on post-syndrome state
                let! correction = correct backend postSyndromeState syndrome

                let! decodedBit = decode backend correction.CorrectedState

                return {
                    Code = SteaneCode7
                    LogicalBit = logicalBit
                    InjectedError = Some (errorType, errorQubit)
                    Syndrome = syndrome
                    CorrectionApplied = correction.CorrectionApplied
                    DecodedBit = decodedBit
                    Success = (decodedBit = logicalBit)
                    BackendName = backend.Name
                }
            }

    // ========================================================================
    // FORMATTING
    // ========================================================================

    /// Format code parameters for display
    let formatCodeParameters (code: ErrorCode) : string =
        let p = codeParameters code
        let name =
            match code with
            | BitFlipCode3 -> "3-Qubit Bit-Flip Code"
            | PhaseFlipCode3 -> "3-Qubit Phase-Flip Code"
            | ShorCode9 -> "Shor 9-Qubit Code"
            | SteaneCode7 -> "Steane 7-Qubit Code"
        sprintf "%s [[%d,%d,%d]]: %d physical qubits, %d logical qubit(s), corrects %d error(s)"
            name p.PhysicalQubits p.LogicalQubits p.Distance
            p.PhysicalQubits p.LogicalQubits p.CorrectableErrors

    /// Format a syndrome result for display
    let formatSyndrome (result: SyndromeResult) : string =
        let sb = Text.StringBuilder()
        let syndromeStr =
            result.SyndromeBits
            |> List.map string
            |> String.concat ""
        sb.AppendLine (sprintf "Syndrome: [%s]" syndromeStr) |> ignore
        match result.DetectedError with
        | Some errorType ->
            let errorStr =
                match errorType with
                | BitFlipError -> "Bit-Flip (X)"
                | PhaseFlipError -> "Phase-Flip (Z)"
                | CombinedError -> "Combined (Y)"
                | UncorrectableError -> "Uncorrectable (multi-qubit)"
            sb.AppendLine (sprintf "Detected Error: %s on qubit %s"
                errorStr
                (result.ErrorQubit |> Option.map string |> Option.defaultValue "unknown")) |> ignore
        | None ->
            sb.AppendLine "No error detected" |> ignore
        sb.ToString()

    /// Format a round-trip result for display
    let formatRoundTrip (result: RoundTripResult) : string =
        let sb = Text.StringBuilder()
        let codeName =
            match result.Code with
            | BitFlipCode3 -> "Bit-Flip [[3,1,1]]"
            | PhaseFlipCode3 -> "Phase-Flip [[3,1,1]]"
            | ShorCode9 -> "Shor [[9,1,3]]"
            | SteaneCode7 -> "Steane [[7,1,3]]"
        sb.AppendLine (sprintf "Code: %s" codeName) |> ignore
        sb.AppendLine (sprintf "Logical bit: |%d>" result.LogicalBit) |> ignore
        match result.InjectedError with
        | Some (errorType, qubit) ->
            let errorStr =
                match errorType with
                | BitFlipError -> "Bit-Flip (X)"
                | PhaseFlipError -> "Phase-Flip (Z)"
                | CombinedError -> "Combined (Y)"
                | UncorrectableError -> "Uncorrectable (multi-qubit)"
            sb.AppendLine (sprintf "Injected: %s on qubit %d" errorStr qubit) |> ignore
        | None ->
            sb.AppendLine "Injected: None" |> ignore
        sb.AppendLine (formatSyndrome result.Syndrome) |> ignore
        sb.AppendLine (sprintf "Correction applied: %b" result.CorrectionApplied) |> ignore
        sb.AppendLine (sprintf "Decoded: |%d>" result.DecodedBit) |> ignore
        sb.AppendLine (sprintf "Success: %b" result.Success) |> ignore
        sb.AppendLine (sprintf "Backend: %s" result.BackendName) |> ignore
        sb.ToString()
