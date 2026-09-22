namespace FSharp.Azure.Quantum.LocalSimulator

open System
open System.Numerics

/// State Vector Module for Local Quantum Simulation
///
/// Implements quantum state vector representation using complex number arrays.
/// State vector represents quantum state |ψ⟩ = Σ αᵢ|i⟩ where αᵢ are complex amplitudes.
///
/// For n qubits, state vector has 2^n dimensions.
/// Initial state |0⟩^⊗n has amplitude 1.0 at index 0, all others are 0.
module StateVector =

    // ============================================================================
    // 1. TYPES (Primitives, no dependencies)
    // ============================================================================

    /// Quantum state vector - array of complex amplitudes
    /// Dimension = 2^n for n qubits
    type StateVector =
        private
            {
                Amplitudes: Complex[]
                NumQubits: int
            }

    // ============================================================================
    // 1b. CAPACITY (how wide a state this machine can actually hold)
    // ============================================================================

    /// Hard structural ceiling, independent of how much memory is installed.
    ///
    /// The amplitudes live in one flat Complex[], and .NET caps a single-dimension
    /// array at Array.MaxLength = 2,147,483,591 elements. A 31-qubit state needs
    /// 2³¹ = 2,147,483,648 of them, so it cannot be allocated at all — and `1 <<< 31`
    /// overflows Int32 into a negative dimension besides. 30 qubits (2³⁰ amplitudes)
    /// is therefore the widest dense state vector this representation can express.
    [<Literal>]
    let StructuralMaxQubits = 30

    /// Floor we always advertise, so a small container never reports less capacity
    /// than the library's own algorithms assume (Shor's counting budget, QPE
    /// precision bounds and friends are all written against 20 qubits).
    /// 2²⁰ amplitudes is 16 MB — allocatable anywhere.
    [<Literal>]
    let MinMaxQubits = 20

    /// Bytes per amplitude: System.Numerics.Complex is two float64 fields.
    [<Literal>]
    let private BytesPerAmplitude = 16L

    /// Full-size arrays live simultaneously while one gate is applied: the source
    /// state and the new amplitude array built from it. Budget for both.
    [<Literal>]
    let private LiveArraysPerGate = 2L

    /// Fraction of available memory a single simulation may claim. The rest is
    /// headroom for the rest of the process (and for the GC to not thrash).
    [<Literal>]
    let private MemoryBudgetFraction = 0.5

    /// Environment override, e.g. FSAQ_MAX_QUBITS=24, for pinning the limit in CI
    /// or deliberately testing beyond what the heuristic allows. Clamped to
    /// StructuralMaxQubits, which no amount of memory can lift.
    [<Literal>]
    let MaxQubitsEnvironmentVariable = "FSAQ_MAX_QUBITS"

    /// Widest dense state vector that fits a given memory budget.
    ///
    /// Pure, so capacity can be asked about a machine other than this one — a 32 GB
    /// development box can check what a 128 GB server will allow without running
    /// there. Result is clamped to [MinMaxQubits, StructuralMaxQubits].
    ///
    /// Each qubit doubles the requirement, so the thresholds are 2ⁿ × 16 bytes ×
    /// 2 live arrays ÷ the budget fraction — i.e. 2ⁿ × 64 bytes of total memory:
    ///   1 GB → 24 qubits    16 GB → 28 qubits
    ///   4 GB → 26 qubits    32 GB → 29 qubits
    ///   8 GB → 27 qubits    64 GB and beyond → 30 (the structural ceiling)
    let maxQubitsForAvailableBytes (availableBytes: int64) : int =
        let affordableAmplitudes =
            if availableBytes <= 0L then
                0L
            else
                int64 (float availableBytes * MemoryBudgetFraction)
                / (BytesPerAmplitude * LiveArraysPerGate)

        [ MinMaxQubits..StructuralMaxQubits ]
        |> List.filter (fun n -> (1L <<< n) <= affordableAmplitudes)
        |> function
            | [] -> MinMaxQubits
            | fitting -> List.max fitting

    let private computeMaxQubits () =
        let fromEnvironment =
            match Environment.GetEnvironmentVariable MaxQubitsEnvironmentVariable with
            | null
            | "" -> None
            | raw ->
                match
                    Int32.TryParse(
                        raw.Trim(),
                        Globalization.NumberStyles.Integer,
                        Globalization.CultureInfo.InvariantCulture
                    )
                with
                | true, requested when requested > 0 -> Some(min requested StructuralMaxQubits)
                | _ -> None

        match fromEnvironment with
        | Some requested -> requested
        | None ->
            // TotalAvailableMemoryBytes is what the GC believes it may use: physical
            // RAM, or the container/job-object limit when one applies.
            maxQubitsForAvailableBytes (GC.GetGCMemoryInfo().TotalAvailableMemoryBytes)

    /// Largest number of qubits a dense state vector can hold on this machine.
    ///
    /// Derived once, from available memory rather than a fixed constant: an n-qubit
    /// state is 2ⁿ × 16 bytes, so every extra qubit doubles the requirement and the
    /// answer genuinely depends on the hardware. Bounded below by `MinMaxQubits` and
    /// above by `StructuralMaxQubits`; override with the FSAQ_MAX_QUBITS environment
    /// variable.
    let maxQubits: int = computeMaxQubits ()

    /// Widest circuit worth simulating in WALL-CLOCK terms, as opposed to what fits.
    ///
    /// `maxQubits` answers "does the state fit in memory". This answers a different
    /// question — "will the simulation finish" — and the two must not be conflated.
    /// Every gate touches all 2ⁿ amplitudes, so each extra qubit doubles the time per
    /// gate; an algorithm applying hundreds of gates crosses from seconds to hours a
    /// few qubits below the memory limit. Algorithms that would otherwise silently
    /// choose an intractable circuit — Shor's modular exponentiation picking the
    /// genuine quantum path over its classically-assisted fallback — budget against
    /// this rather than against capacity.
    ///
    /// Default 20 (2²⁰ amplitudes, milliseconds per gate). Raise with
    /// FSAQ_MAX_CIRCUIT_QUBITS when you have both the memory and the patience; it is
    /// clamped to `maxQubits`, since no amount of patience conjures memory.
    let practicalCircuitQubits: int =
        let requested =
            match Environment.GetEnvironmentVariable "FSAQ_MAX_CIRCUIT_QUBITS" with
            | null
            | "" -> MinMaxQubits
            | raw ->
                match
                    Int32.TryParse(
                        raw.Trim(),
                        Globalization.NumberStyles.Integer,
                        Globalization.CultureInfo.InvariantCulture
                    )
                with
                | true, v when v > 0 -> v
                | _ -> MinMaxQubits

        min requested maxQubits

    /// Memory one n-qubit dense state vector occupies, in bytes.
    /// Useful for reporting and for callers doing their own capacity planning.
    let stateVectorBytes (numQubits: int) : int64 =
        if numQubits < 0 || numQubits > StructuralMaxQubits then
            0L
        else
            (1L <<< numQubits) * BytesPerAmplitude

    // ============================================================================
    // 2. CONSTRUCTION (depend on types)
    // ============================================================================

    /// Initialize state vector to |0⟩^⊗n (all qubits in |0⟩ state)
    ///
    /// For n qubits, creates 2^n dimensional state vector with:
    /// - amplitude[0] = 1.0 + 0.0i (|00...0⟩ state)
    /// - amplitude[i] = 0.0 + 0.0i for all i > 0
    let init (numQubits: int) : StateVector =
        if numQubits < 0 || numQubits > maxQubits then
            let neededGb = float (stateVectorBytes numQubits) / 1073741824.0

            failwith (
                $"Number of qubits must be between 0 and {maxQubits}, got {numQubits}. "
                + $"The limit is derived from available memory: one {numQubits}-qubit state vector needs "
                + $"{neededGb:F1} GB and applying a gate holds two. "
                + $"Set {MaxQubitsEnvironmentVariable} to override, up to the structural maximum of {StructuralMaxQubits}."
            )

        let dimension = 1 <<< numQubits // 2^numQubits using bit shift
        let amplitudes = Array.create dimension Complex.Zero
        amplitudes[0] <- Complex(1.0, 0.0) // |0⟩^⊗n state

        {
            Amplitudes = amplitudes
            NumQubits = numQubits
        }

    // ============================================================================
    // 3. ACCESSORS (depend on types)
    // ============================================================================

    /// Get dimension of state vector (2^n for n qubits)
    let dimension (state: StateVector) : int = state.Amplitudes.Length

    /// Get amplitude at specific basis state index
    let getAmplitude (index: int) (state: StateVector) : Complex =
        if index < 0 || index >= state.Amplitudes.Length then
            failwith $"Index {index} out of range for state vector of dimension {state.Amplitudes.Length}"

        state.Amplitudes[index]

    /// Get number of qubits
    let numQubits (state: StateVector) : int = state.NumQubits

    /// Validate an amplitude array and return the qubit count it encodes.
    let private qubitCountOf (amplitudes: Complex[]) : int =
        let n = amplitudes.Length

        if n = 0 || (n &&& (n - 1)) <> 0 then
            failwith $"Amplitude array length must be a power of 2, got {n}"

        let numQubits = int (Math.Log(float n, 2.0))

        if numQubits > maxQubits then
            failwith (
                $"State vector supports at most {maxQubits} qubits here ({1L <<< maxQubits} dimensions), "
                + $"got {numQubits} qubits ({n} dimensions). Set {MaxQubitsEnvironmentVariable} to override, "
                + $"up to the structural maximum of {StructuralMaxQubits}."
            )

        numQubits

    /// Create state vector from custom amplitudes (defensive copy)
    let create (amplitudes: Complex[]) : StateVector =
        {
            Amplitudes = Array.copy amplitudes
            NumQubits = qubitCountOf amplitudes
        }

    /// The underlying amplitude array, WITHOUT copying.
    ///
    /// For gate kernels, which read every amplitude of a 2^n array and cannot afford
    /// `getAmplitude`'s per-element bounds check and closure indirection. Callers must
    /// treat the result as READ-ONLY: it is the live array of `state`, and mutating it
    /// would change a value that its owner believes is immutable (`Primitives.expectation`
    /// reads the source state again after deriving another from it).
    ///
    /// To produce a new state, write into a fresh array and hand it to
    /// `ofAmplitudesOwned`. To mutate deliberately, use `mutateOwned`, whose name
    /// says the caller owns the state.
    let internal amplitudesView (state: StateVector) : Complex[] = state.Amplitudes

    /// A state vector whose holder has EXCLUSIVE ownership and may overwrite in place.
    ///
    /// The distinction the type carries is lifetime, not representation: an ordinary
    /// `StateVector` may be shared — `Primitives.expectation` derives a second state
    /// from one and then reads the original again — so gates must leave it untouched
    /// and return a fresh array. Inside a circuit-execution fold no such sharing
    /// exists: each intermediate state is consumed by the next gate and never read
    /// again, so the array can be overwritten and the per-gate allocation skipped
    /// entirely (16 GB per gate at 30 qubits).
    ///
    /// Only `takeOwnership` produces one, and it is the caller's promise that no other
    /// reference to that state survives.
    [<Struct>]
    type Owned = internal { State: StateVector }

    /// Claim exclusive ownership of a state. The caller must hold the only reference:
    /// anything still pointing at `state` will observe the in-place mutations.
    let internal takeOwnership (state: StateVector) : Owned = { State = state }

    /// Give up ownership and hand back an ordinary, shareable state vector.
    let internal release (owned: Owned) : StateVector = owned.State

    /// The amplitude array of an owned state, writable.
    let internal ownedAmplitudes (owned: Owned) : Complex[] = owned.State.Amplitudes

    /// Create a state vector that TAKES OWNERSHIP of `amplitudes` instead of copying it.
    ///
    /// Only for callers that just allocated the array and drop every other reference
    /// to it — gate application, which builds a fresh array per gate. `create` would
    /// copy it straight back, doubling the peak memory of every gate and halving the
    /// qubit width the machine can hold.
    let internal ofAmplitudesOwned (amplitudes: Complex[]) : StateVector =
        {
            Amplitudes = amplitudes
            NumQubits = qubitCountOf amplitudes
        }

    // ============================================================================
    // 4. NORMALIZATION AND NORM (depend on accessors)
    // ============================================================================

    /// Calculate norm of state vector: ||ψ|| = sqrt(Σ |αᵢ|²)
    let norm (state: StateVector) : float =
        state.Amplitudes
        |> Array.sumBy (fun amp -> amp.Magnitude * amp.Magnitude)
        |> sqrt

    /// Normalize state vector to unit norm
    let normalize (state: StateVector) : StateVector =
        let normValue = norm state

        if normValue < 1e-10 then
            failwith "Cannot normalize zero state vector"

        let normalizedAmps = state.Amplitudes |> Array.map (fun amp -> amp / normValue)

        {
            Amplitudes = normalizedAmps
            NumQubits = state.NumQubits
        }

    // ============================================================================
    // 5. INNER PRODUCT AND PROBABILITIES (depend on norm)
    // ============================================================================

    /// Calculate inner product <ψ|φ> = Σ ψᵢ* φᵢ
    let innerProduct (bra: StateVector) (ket: StateVector) : Complex =
        if bra.Amplitudes.Length <> ket.Amplitudes.Length then
            failwith
                $"State vectors must have same dimension for inner product, calling innerProduct with bra: {bra}, ket: {ket}"

        Array.zip bra.Amplitudes ket.Amplitudes
        |> Array.fold (fun sum (braAmp, ketAmp) -> sum + (Complex.Conjugate braAmp) * ketAmp) Complex.Zero

    /// Calculate probability of measuring basis state |i⟩
    /// P(i) = |αᵢ|² where αᵢ is amplitude at index i
    let probability (index: int) (state: StateVector) : float =
        if index < 0 || index >= state.Amplitudes.Length then
            failwith $"Index {index} out of range for state vector of dimension {state.Amplitudes.Length}"

        let amp = state.Amplitudes[index]
        amp.Magnitude * amp.Magnitude

    // ============================================================================
    // 6. EQUALITY AND COMPARISON (depend on all above)
    // ============================================================================

    /// Check if two state vectors are equal (within tolerance)
    let equals (state1: StateVector) (state2: StateVector) : bool =
        if state1.NumQubits <> state2.NumQubits then
            false
        else
            Array.zip state1.Amplitudes state2.Amplitudes
            |> Array.forall (fun (amp1, amp2) ->
                abs (amp1.Real - amp2.Real) < 1e-10
                && abs (amp1.Imaginary - amp2.Imaginary) < 1e-10)

    // ============================================================================
    // 7. TENSOR PRODUCT (depends on create)
    // ============================================================================

    /// Compute tensor product |ψ⟩ ⊗ |φ⟩
    ///
    /// For state1 with dimension n and state2 with dimension m,
    /// result has dimension n*m with amplitudes:
    /// result[i*m + j] = state1[i] * state2[j]
    let tensorProduct (state1: StateVector) (state2: StateVector) : StateVector =
        let dim1 = state1.Amplitudes.Length
        let dim2 = state2.Amplitudes.Length
        let resultDim = dim1 * dim2

        let resultAmps =
            Array.init resultDim (fun idx ->
                let i = idx / dim2
                let j = idx % dim2
                state1.Amplitudes[i] * state2.Amplitudes[j])

        create resultAmps
