namespace FSharp.Azure.Quantum.Algorithms

open System
open FSharp.Azure.Quantum
open FSharp.Azure.Quantum.CircuitBuilder
open FSharp.Azure.Quantum.Core
open FSharp.Azure.Quantum.Core.BackendAbstraction

/// Ekert E91 Quantum Key Distribution Protocol
/// 
/// E91 is an entanglement-based quantum key distribution protocol proposed by
/// Artur Ekert in 1991. Unlike BB84 (prepare-and-measure), E91 derives security
/// from the violation of Bell's inequality (CHSH inequality). If an eavesdropper
/// intercepts the entangled pairs, the Bell inequality violation decreases,
/// alerting Alice and Bob.
/// 
/// **Production Use Cases**:
/// - Quantum Networks: Entanglement-based secure key exchange
/// - Device-Independent QKD: Security relies only on Bell violation, not device trust
/// - Quantum Internet: Backbone protocol for entanglement distribution networks
/// - Long-Distance QKD: Suitable for satellite-based quantum communication
/// 
/// **Real-World Deployments**:
/// - Micius satellite (China, 2017): Entanglement-based QKD over 1200km
/// - European Quantum Internet Alliance: Entanglement distribution experiments
/// - QuTech (Delft, Netherlands): Metropolitan quantum network tests
/// 
/// **Security Guarantees**:
/// - Device-independent security (relies only on Bell violation)
/// - Eavesdropper detection via CHSH inequality violation
/// - Information-theoretic security (not computational)
/// - Cannot be broken by quantum computers
/// 
/// **Textbook References**:
/// - Ekert (1991) "Quantum Cryptography Based on Bell's Theorem"
/// - Clauser, Horne, Shimony, Holt (1969) "Proposed Experiment to Test Local Hidden-Variable Theories"
/// - Nielsen & Chuang "Quantum Computation and Quantum Information" - Chapter 12
/// 
/// **Performance Characteristics**:
/// - Key rate: ~2/9 (~22.2%) of total pairs (2 matching bases out of 9 combinations)
/// - CHSH parameter (no Eve): |S| = 2*sqrt(2) ~ 2.828
/// - CHSH parameter (classical/Eve): |S| <= 2.0
/// - Detection threshold: S drops below quantum bound when Eve intercepts
/// 
/// **Protocol Overview**:
/// 1. Entanglement source generates Bell pairs |Phi+> = (|00>+|11>)/sqrt(2)
/// 2. Alice measures her qubit in one of 3 bases: {0 deg, 45 deg, 90 deg}
/// 3. Bob measures his qubit in one of 3 bases: {0 deg, 45 deg, 135 deg}
/// 4. Both publicly announce their basis choices (not results)
/// 5. Matching bases (0,0) and (45,45): use for key bits (perfectly correlated)
/// 6. Non-matching bases: compute CHSH parameter S for security verification
/// 7. If |S| ~ 2*sqrt(2): secure, extract key. If |S| <= 2: eavesdropper detected
module EkertQKD =

    // ========================================================================
    // TYPES
    // ========================================================================

    /// Alice's measurement basis choices for E91
    /// 
    /// Alice randomly chooses from 3 measurement angles:
    /// - 0 degrees (Z-basis measurement)
    /// - 45 degrees (pi/4 rotated basis)
    /// - 90 degrees (X-basis measurement)
    type AliceBasis =
        /// 0 degrees: Computational (Z) basis
        | AliceDeg0
        /// 45 degrees: pi/4 rotated basis
        | AliceDeg45
        /// 90 degrees: Hadamard (X) basis
        | AliceDeg90

    /// Bob's measurement basis choices for E91
    /// 
    /// Bob randomly chooses from 3 measurement angles:
    /// - 0 degrees (Z-basis measurement)
    /// - 45 degrees (pi/4 rotated basis)
    /// - 135 degrees (3*pi/4 rotated basis)
    type BobBasis =
        /// 0 degrees: Computational (Z) basis
        | BobDeg0
        /// 45 degrees: pi/4 rotated basis
        | BobDeg45
        /// 135 degrees: 3*pi/4 rotated basis
        | BobDeg135

    /// Result of measuring a single entangled pair
    type E91Pair = {
        /// Alice's chosen basis
        AliceBasis: AliceBasis
        /// Bob's chosen basis
        BobBasis: BobBasis
        /// Alice's measurement result (0 or 1)
        AliceResult: int
        /// Bob's measurement result (0 or 1)
        BobResult: int
    }

    /// CHSH inequality test result
    /// 
    /// The CHSH parameter S is computed from correlations between
    /// non-matching basis measurements:
    /// S = E(a1,b1) - E(a1,b3) + E(a3,b1) + E(a3,b3)
    /// 
    /// where a1=0 deg, a3=90 deg, b1=45 deg, b3=135 deg
    type CHSHResult = {
        /// CHSH parameter S value
        S: float
        /// Quantum mechanical bound: 2*sqrt(2) ~ 2.828
        QuantumBound: float
        /// Classical (local hidden variable) bound: 2.0
        ClassicalBound: float
        /// Is |S| above classical bound? (indicates genuine quantum correlations)
        IsSecure: bool
        /// Eavesdropper detected? (|S| significantly below quantum bound)
        EavesdropperDetected: bool
        /// Individual correlation values used to compute S
        Correlations: (string * float) list
    }

    /// Full E91 protocol result
    type E91Result = {
        /// Total number of entangled pairs generated
        TotalPairs: int
        /// All pair measurements
        Pairs: E91Pair list
        /// Sifted key bits (from matching-basis measurements)
        KeyBits: int list
        /// Length of sifted key
        SiftedKeyLength: int
        /// CHSH security test result
        CHSHTest: CHSHResult
        /// Key rate (sifted key length / total pairs)
        KeyRate: float
        /// Protocol security status
        IsSecure: bool
        /// Backend used
        BackendName: string
    }

    // ========================================================================
    // CONSTANTS
    // ========================================================================

    /// Alice's measurement angles in radians
    let private aliceAngle (basis: AliceBasis) : float =
        match basis with
        | AliceDeg0 -> 0.0
        | AliceDeg45 -> Math.PI / 4.0
        | AliceDeg90 -> Math.PI / 2.0

    /// Bob's measurement angles in radians
    let private bobAngle (basis: BobBasis) : float =
        match basis with
        | BobDeg0 -> 0.0
        | BobDeg45 -> Math.PI / 4.0
        | BobDeg135 -> 3.0 * Math.PI / 4.0

    /// Quantum bound for CHSH: 2*sqrt(2)
    let private quantumBound = 2.0 * sqrt 2.0

    /// Classical bound for CHSH
    let private classicalBound = 2.0

    // ========================================================================
    // INTENT -> PLAN -> EXECUTE
    // ========================================================================

    /// E91 protocol intent (captures all randomness upfront)
    type private E91Intent = {
        /// Number of entangled pairs to generate
        NumPairs: int
        /// Alice's qubit index
        AliceQubit: int
        /// Bob's qubit index
        BobQubit: int
        /// Random basis choices for Alice (one per pair)
        AliceBases: AliceBasis[]
        /// Random basis choices for Bob (one per pair)
        BobBases: BobBasis[]
        /// Whether Eve intercepts (for testing)
        EveIntercepts: bool
        /// Random seed used (for reproducibility)
        Seed: int option
    }

    [<RequireQualifiedAccess>]
    type private E91Plan =
        | ExecuteViaOps of requiredOps: QuantumOperation list

    /// All operations required by E91 (Bell pair creation + rotated measurements)
    let private requiredOps : QuantumOperation list =
        [ QuantumOperation.Gate (H 0)
          QuantumOperation.Gate (CNOT (0, 1))
          QuantumOperation.Gate (RY (0, 0.1)) ]  // RY with arbitrary angle as capability test

    /// Plan the E91 protocol execution
    let private planE91 (backend: IQuantumBackend) : Result<E91Plan, QuantumError> =
        match backend.NativeStateType with
        | QuantumStateType.Annealing ->
            Error (QuantumError.OperationError ("EkertQKD", $"Backend '{backend.Name}' does not support E91 QKD (native state type: {backend.NativeStateType})"))
        | _ ->
            if requiredOps |> List.forall backend.SupportsOperation then
                Ok (E91Plan.ExecuteViaOps requiredOps)
            else
                Error (QuantumError.OperationError ("EkertQKD", $"Backend '{backend.Name}' does not support required operations for E91 QKD"))

    /// Build the E91 intent with random basis choices
    let private buildE91Intent
        (numPairs: int)
        (withEve: bool)
        (seed: int option)
        : E91Intent =

        let rng =
            match seed with
            | Some s -> Random(s)
            | None -> Random()

        let aliceBases =
            Array.init numPairs (fun _ ->
                match rng.Next(3) with
                | 0 -> AliceDeg0
                | 1 -> AliceDeg45
                | _ -> AliceDeg90)

        let bobBases =
            Array.init numPairs (fun _ ->
                match rng.Next(3) with
                | 0 -> BobDeg0
                | 1 -> BobDeg45
                | _ -> BobDeg135)

        {
            NumPairs = numPairs
            AliceQubit = 0
            BobQubit = 1
            AliceBases = aliceBases
            BobBases = bobBases
            EveIntercepts = withEve
            Seed = seed
        }

    // ========================================================================
    // MEASUREMENT IN ROTATED BASIS
    // ========================================================================

    /// Measure a qubit in a rotated basis defined by angle theta.
    /// 
    /// To measure in the basis defined by angle theta from the Z-axis:
    /// Apply Ry(-theta) to rotate the measurement basis back to Z,
    /// then measure in the computational (Z) basis.
    /// 
    /// Angles:
    ///   0 deg -> Z basis (no rotation needed)
    ///   45 deg (pi/4) -> diagonal basis
    ///   90 deg (pi/2) -> X basis
    ///   135 deg (3*pi/4) -> anti-diagonal basis
    let private measureInRotatedBasis
        (backend: IQuantumBackend)
        (state: QuantumState)
        (qubit: int)
        (angle: float)
        : Result<int, QuantumError> =

        result {
            // Apply Ry(-angle) to rotate measurement basis to Z-axis
            let! rotatedState =
                if abs angle < 1e-10 then
                    Ok state  // 0 degrees = Z basis, no rotation needed
                else
                    backend.ApplyOperation (QuantumOperation.Gate (RY (qubit, -angle))) state

            // Measure in computational (Z) basis using single-shot
            let measurements = QuantumState.measure rotatedState 1
            let bit = measurements.[0].[qubit]

            return bit
        }

    // ========================================================================
    // SINGLE PAIR PROTOCOL
    // ========================================================================

    /// Execute the E91 protocol for a single entangled pair
    /// 
    /// Steps:
    /// 1. Create Bell pair |Phi+> = (|00> + |11>) / sqrt(2)
    /// 2. Apply basis rotations Ry(-angle) on both qubits
    /// 3. Measure both qubits simultaneously in Z basis
    /// 4. Return both measurement results
    let private executeSinglePair
        (backend: IQuantumBackend)
        (intent: E91Intent)
        (aliceBasis: AliceBasis)
        (bobBasis: BobBasis)
        : Result<E91Pair, QuantumError> =

        result {
            // Initialize 2-qubit state |00>
            let! initialState = backend.InitializeState 2

            // Create Bell pair: H(0) -> CNOT(0,1) -> |Phi+>
            let bellOps = [
                QuantumOperation.Gate (H intent.AliceQubit)
                QuantumOperation.Gate (CNOT (intent.AliceQubit, intent.BobQubit))
            ]
            let! bellState = UnifiedBackend.applySequence backend bellOps initialState

            // Get measurement angles
            let aliceAngleRad = aliceAngle aliceBasis
            let bobAngleRad = bobAngle bobBasis

            // Apply Ry(-angle) rotations for BOTH qubits to their measurement bases,
            // then measure both simultaneously in the Z basis.
            // This correctly preserves entanglement correlations.
            let! afterAliceRot =
                if abs aliceAngleRad < 1e-10 then Ok bellState
                else backend.ApplyOperation (QuantumOperation.Gate (RY (intent.AliceQubit, -aliceAngleRad))) bellState

            let! afterBothRot =
                if abs bobAngleRad < 1e-10 then Ok afterAliceRot
                else backend.ApplyOperation (QuantumOperation.Gate (RY (intent.BobQubit, -bobAngleRad))) afterAliceRot

            // Measure both qubits simultaneously in Z basis
            let measurements = QuantumState.measure afterBothRot 1
            let bits = measurements.[0]

            return {
                AliceBasis = aliceBasis
                BobBasis = bobBasis
                AliceResult = bits.[intent.AliceQubit]
                BobResult = bits.[intent.BobQubit]
            }
        }

    /// Execute a single pair with Eve performing intercept-resend attack
    /// 
    /// Eve intercepts the entangled pairs and measures them,
    /// destroying the entanglement. She then sends new (unentangled)
    /// qubits to Alice and Bob, which will not violate the CHSH inequality.
    let private executeSinglePairWithEve
        (backend: IQuantumBackend)
        (intent: E91Intent)
        (aliceBasis: AliceBasis)
        (bobBasis: BobBasis)
        (rng: Random)
        : Result<E91Pair, QuantumError> =

        result {
            // Eve intercepts: she measures in a random basis, destroying entanglement.
            // She then prepares new qubits based on her measurement results.
            // This breaks the quantum correlations.

            // Initialize 2-qubit state
            let! initialState = backend.InitializeState 2

            // Create Bell pair
            let bellOps = [
                QuantumOperation.Gate (H intent.AliceQubit)
                QuantumOperation.Gate (CNOT (intent.AliceQubit, intent.BobQubit))
            ]
            let! bellState = UnifiedBackend.applySequence backend bellOps initialState

            // Eve measures both qubits in a random basis (destroying entanglement)
            let eveAngle = float (rng.Next(4)) * Math.PI / 4.0
            let! eveResultAlice = measureInRotatedBasis backend bellState intent.AliceQubit eveAngle
            let! eveResultBob = measureInRotatedBasis backend bellState intent.BobQubit eveAngle

            // Eve prepares replacement qubits (unentangled) based on her measurements
            let! replacementState = backend.InitializeState 2

            // Set Alice's qubit based on Eve's measurement
            let! afterAlicePrep =
                if eveResultAlice = 1 then
                    backend.ApplyOperation (QuantumOperation.Gate (X intent.AliceQubit)) replacementState
                else
                    Ok replacementState

            // Set Bob's qubit based on Eve's measurement
            let! afterBothPrep =
                if eveResultBob = 1 then
                    backend.ApplyOperation (QuantumOperation.Gate (X intent.BobQubit)) afterAlicePrep
                else
                    Ok afterAlicePrep

            // Alice and Bob measure their unentangled qubits
            let aliceAngleRad = aliceAngle aliceBasis
            let bobAngleRad = bobAngle bobBasis

            let! afterAliceRot =
                if abs aliceAngleRad < 1e-10 then Ok afterBothPrep
                else backend.ApplyOperation (QuantumOperation.Gate (RY (intent.AliceQubit, -aliceAngleRad))) afterBothPrep

            let! afterBothRot =
                if abs bobAngleRad < 1e-10 then Ok afterAliceRot
                else backend.ApplyOperation (QuantumOperation.Gate (RY (intent.BobQubit, -bobAngleRad))) afterAliceRot

            let measurements = QuantumState.measure afterBothRot 1
            let bits = measurements.[0]

            return {
                AliceBasis = aliceBasis
                BobBasis = bobBasis
                AliceResult = bits.[intent.AliceQubit]
                BobResult = bits.[intent.BobQubit]
            }
        }

    // ========================================================================
    // CHSH CORRELATION COMPUTATION
    // ========================================================================

    /// Compute correlation E(a,b) for a specific pair of basis choices
    /// 
    /// E(a,b) = P(same outcomes) - P(different outcomes)
    ///        = (N_same - N_different) / N_total
    /// 
    /// For |Phi+> state measured in rotated bases:
    /// E(a,b) = -cos(a - b)  (theoretical prediction)
    let private computeCorrelation
        (pairs: E91Pair list)
        (aliceBasis: AliceBasis)
        (bobBasis: BobBasis)
        : float option =

        let matching =
            pairs
            |> List.filter (fun p -> p.AliceBasis = aliceBasis && p.BobBasis = bobBasis)

        if matching.IsEmpty then
            None
        else
            let nSame =
                matching
                |> List.filter (fun p -> p.AliceResult = p.BobResult)
                |> List.length

            let nDiff = matching.Length - nSame
            Some (float (nSame - nDiff) / float matching.Length)

    /// Compute the CHSH parameter S from measured pairs
    /// 
    /// S = E(a1,b1) - E(a1,b3) + E(a3,b1) + E(a3,b3)
    /// 
    /// where:
    ///   a1 = Alice 0 deg, a3 = Alice 90 deg
    ///   b1 = Bob 45 deg, b3 = Bob 135 deg
    /// 
    /// Quantum prediction: |S| = 2*sqrt(2) ~ 2.828
    /// Classical bound: |S| <= 2.0
    let computeCHSH (pairs: E91Pair list) : CHSHResult =
        // Compute four correlations needed for CHSH
        let e_a1_b1 = computeCorrelation pairs AliceDeg0 BobDeg45 |> Option.defaultValue 0.0
        let e_a1_b3 = computeCorrelation pairs AliceDeg0 BobDeg135 |> Option.defaultValue 0.0
        let e_a3_b1 = computeCorrelation pairs AliceDeg90 BobDeg45 |> Option.defaultValue 0.0
        let e_a3_b3 = computeCorrelation pairs AliceDeg90 BobDeg135 |> Option.defaultValue 0.0

        // S = E(a1,b1) - E(a1,b3) + E(a3,b1) + E(a3,b3)
        let s = e_a1_b1 - e_a1_b3 + e_a3_b1 + e_a3_b3

        let absS = abs s
        let isSecure = absS > classicalBound
        let eavesdropperDetected = absS <= classicalBound

        {
            S = s
            QuantumBound = quantumBound
            ClassicalBound = classicalBound
            IsSecure = isSecure
            EavesdropperDetected = eavesdropperDetected
            Correlations = [
                ("E(a1=0,b1=45)", e_a1_b1)
                ("E(a1=0,b3=135)", e_a1_b3)
                ("E(a3=90,b1=45)", e_a3_b1)
                ("E(a3=90,b3=135)", e_a3_b3)
            ]
        }

    // ========================================================================
    // KEY SIFTING
    // ========================================================================

    /// Check if Alice and Bob used matching bases (for key generation)
    /// 
    /// Matching bases in E91:
    /// - (Alice 0 deg, Bob 0 deg): both measure in Z basis
    /// - (Alice 45 deg, Bob 45 deg): both measure in pi/4 rotated basis
    /// 
    /// These 2 out of 9 combinations (~22.2%) produce perfectly correlated results
    /// that can be used as key bits.
    let private isMatchingBasis (aliceBasis: AliceBasis) (bobBasis: BobBasis) : bool =
        match (aliceBasis, bobBasis) with
        | (AliceDeg0, BobDeg0) -> true
        | (AliceDeg45, BobDeg45) -> true
        | _ -> false

    /// Extract key bits from pairs with matching bases
    /// 
    /// When Alice and Bob measure in the same basis on |Phi+>,
    /// their results are perfectly correlated (both get same bit).
    /// Alice's result is used as the key bit.
    let private siftKey (pairs: E91Pair list) : int list =
        pairs
        |> List.filter (fun p -> isMatchingBasis p.AliceBasis p.BobBasis)
        |> List.map (fun p -> p.AliceResult)

    // ========================================================================
    // FULL PROTOCOL EXECUTION
    // ========================================================================

    /// Execute the E91 protocol (deterministic, given intent)
    let private executeE91
        (backend: IQuantumBackend)
        (intent: E91Intent)
        : Result<E91Result, QuantumError> =

        result {
            // Validate backend supports required operations
            let! _plan = planE91 backend

            let rng =
                match intent.Seed with
                | Some s -> Random(s + 1)  // Offset seed to avoid correlation with basis choices
                | None -> Random()

            // Execute all pair measurements
            let! pairs =
                [| 0 .. intent.NumPairs - 1 |]
                |> Result.traverseArray (fun i ->
                    if intent.EveIntercepts then
                        executeSinglePairWithEve backend intent intent.AliceBases.[i] intent.BobBases.[i] rng
                    else
                        executeSinglePair backend intent intent.AliceBases.[i] intent.BobBases.[i])

            let pairList = Array.toList pairs

            // Key sifting: extract key from matching-basis measurements
            let keyBits = siftKey pairList

            // CHSH security test on non-matching basis measurements
            let chshResult = computeCHSH pairList

            let siftedKeyLength = keyBits.Length
            let keyRate =
                if intent.NumPairs > 0 then float siftedKeyLength / float intent.NumPairs
                else 0.0

            return {
                TotalPairs = intent.NumPairs
                Pairs = pairList
                KeyBits = keyBits
                SiftedKeyLength = siftedKeyLength
                CHSHTest = chshResult
                KeyRate = keyRate
                IsSecure = chshResult.IsSecure && siftedKeyLength > 0
                BackendName = backend.Name
            }
        }

    // ========================================================================
    // MAIN PUBLIC API
    // ========================================================================

    /// Run the E91 QKD protocol
    /// 
    /// Generates entangled Bell pairs, Alice and Bob measure in random bases,
    /// extracts key from matching bases, and verifies security via CHSH test.
    /// 
    /// **Parameters**:
    ///   backend - Quantum backend to execute on
    ///   numPairs - Number of entangled pairs to generate (recommended: >= 100)
    ///   seed - Optional random seed for reproducibility
    /// 
    /// **Returns**:
    ///   E91Result with key bits, CHSH test, and security status
    let run
        (backend: IQuantumBackend)
        (numPairs: int)
        (seed: int option)
        : Result<E91Result, QuantumError> =

        result {
            do!
                if numPairs < 1 then
                    Error (QuantumError.ValidationError ("numPairs", "must be at least 1"))
                else
                    Ok ()

            let intent = buildE91Intent numPairs false seed
            return! executeE91 backend intent
        }

    /// Run the E91 protocol with an eavesdropper (Eve)
    /// 
    /// Eve performs an intercept-resend attack on the entangled pairs,
    /// which destroys entanglement. This should cause the CHSH parameter
    /// to drop below the quantum bound, alerting Alice and Bob.
    /// 
    /// **Expected Behavior**:
    /// - Without Eve: |S| ~ 2*sqrt(2) ~ 2.828
    /// - With Eve (intercept-resend): |S| ~ 0 (no quantum correlations)
    /// - Security check: IsSecure = false when Eve present
    /// 
    /// **Parameters**:
    ///   backend - Quantum backend to execute on
    ///   numPairs - Number of entangled pairs (recommended: >= 100)
    ///   seed - Optional random seed
    /// 
    /// **Returns**:
    ///   E91Result showing reduced CHSH violation and eavesdropper detection
    let runWithEve
        (backend: IQuantumBackend)
        (numPairs: int)
        (seed: int option)
        : Result<E91Result, QuantumError> =

        result {
            do!
                if numPairs < 1 then
                    Error (QuantumError.ValidationError ("numPairs", "must be at least 1"))
                else
                    Ok ()

            let intent = buildE91Intent numPairs true seed
            return! executeE91 backend intent
        }

    // ========================================================================
    // FORMATTING AND DISPLAY
    // ========================================================================

    /// Format the CHSH test result for display
    let formatCHSH (chsh: CHSHResult) : string =
        let sb = Text.StringBuilder()

        sb.AppendLine "CHSH Inequality Test" |> ignore
        sb.AppendLine "====================" |> ignore
        sb.AppendLine "" |> ignore
        sb.AppendLine (sprintf "S parameter: %.4f" chsh.S) |> ignore
        sb.AppendLine (sprintf "|S|:         %.4f" (abs chsh.S)) |> ignore
        sb.AppendLine "" |> ignore
        sb.AppendLine "Bounds:" |> ignore
        sb.AppendLine (sprintf "  Classical (local hidden variables): |S| <= %.4f" chsh.ClassicalBound) |> ignore
        sb.AppendLine (sprintf "  Quantum (Bell state):               |S| =  %.4f" chsh.QuantumBound) |> ignore
        sb.AppendLine "" |> ignore
        sb.AppendLine "Correlations:" |> ignore

        for (name, value) in chsh.Correlations do
            sb.AppendLine (sprintf "  %s = %.4f" name value) |> ignore

        sb.AppendLine "" |> ignore

        if chsh.IsSecure then
            sb.AppendLine "Security: SECURE (Bell inequality violated - genuine quantum correlations)" |> ignore
        else
            sb.AppendLine "Security: INSECURE (Bell inequality NOT violated - possible eavesdropper!)" |> ignore

        sb.ToString()

    /// Format the full E91 protocol result for display
    let formatResult (result: E91Result) : string =
        let sb = Text.StringBuilder()

        sb.AppendLine "E91 Quantum Key Distribution Result" |> ignore
        sb.AppendLine "====================================" |> ignore
        sb.AppendLine "" |> ignore

        sb.AppendLine $"Total Pairs Generated: {result.TotalPairs}" |> ignore
        sb.AppendLine "" |> ignore

        // Basis distribution
        let basisCounts =
            result.Pairs
            |> List.groupBy (fun p -> (p.AliceBasis, p.BobBasis))
            |> List.map (fun (basis, pairs) -> (basis, List.length pairs))

        sb.AppendLine "Basis Distribution (Alice, Bob):" |> ignore
        for ((aBasis, bBasis), count) in basisCounts do
            let aStr =
                match aBasis with
                | AliceDeg0 -> "0"
                | AliceDeg45 -> "45"
                | AliceDeg90 -> "90"
            let bStr =
                match bBasis with
                | BobDeg0 -> "0"
                | BobDeg45 -> "45"
                | BobDeg135 -> "135"
            let isKey = if isMatchingBasis aBasis bBasis then " [KEY]" else ""
            sb.AppendLine (sprintf "  (%s deg, %s deg): %d pairs%s" aStr bStr count isKey) |> ignore

        sb.AppendLine "" |> ignore

        sb.AppendLine "Key Sifting:" |> ignore
        sb.AppendLine $"  Sifted Key Length: {result.SiftedKeyLength} bits" |> ignore
        sb.AppendLine (sprintf "  Key Rate: %.1f%%" (result.KeyRate * 100.0)) |> ignore
        sb.AppendLine "" |> ignore

        sb.AppendLine (formatCHSH result.CHSHTest) |> ignore

        if result.IsSecure then
            sb.AppendLine "Overall: SECURE - Key exchange successful!" |> ignore
        else
            sb.AppendLine "Overall: INSECURE - Possible eavesdropper, abort protocol!" |> ignore

        sb.AppendLine "" |> ignore
        sb.AppendLine $"Backend: {result.BackendName}" |> ignore

        sb.ToString()

    /// Get all 9 basis combinations and their roles in the protocol
    let formatBasisTable () : string =
        let sb = Text.StringBuilder()

        sb.AppendLine "E91 Basis Combinations (9 total):" |> ignore
        sb.AppendLine "=================================" |> ignore
        sb.AppendLine "" |> ignore
        sb.AppendLine "  Alice\\Bob |  0 deg  |  45 deg  | 135 deg" |> ignore
        sb.AppendLine "  ---------+---------+----------+---------" |> ignore
        sb.AppendLine "    0 deg  |   KEY   | CHSH(a1b1)| CHSH(a1b3)" |> ignore
        sb.AppendLine "   45 deg  |  CHSH   |   KEY    |  CHSH   " |> ignore
        sb.AppendLine "   90 deg  |  CHSH   | CHSH(a3b1)| CHSH(a3b3)" |> ignore
        sb.AppendLine "" |> ignore
        sb.AppendLine "KEY = Used for key generation (matching bases)" |> ignore
        sb.AppendLine "CHSH = Used for CHSH security test (non-matching bases)" |> ignore

        sb.ToString()
