namespace FSharp.Azure.Quantum.Topological

/// Hardware device profiles for Microsoft topological (tetron) quantum processors.
///
/// These profiles capture the *measured / design* physical parameters that
/// characterize a generation of Majorana-based hardware. They are descriptive
/// metadata: the underlying anyon theory (Ising / MZM) is identical across
/// generations — what changes between Majorana 1 and Majorana 2 is the
/// material stack and the resulting figures of merit (topological gap,
/// Majorana splitting, parity lifetime, …).
///
/// The Majorana 2 numbers are taken from Microsoft Quantum,
/// "20 Second Parity Lifetime in an InAs–Pb Tetron Device" (June 2, 2026)
/// and the accompanying blog post. See <see cref="NoiseModels"/> for the
/// corresponding simulation noise presets derived from these profiles.
[<RequireQualifiedAccess>]
module DeviceProfile =

    /// Measured / design parameters of a topological (tetron) quantum processor.
    ///
    /// A tetron encodes one logical qubit in the joint fermion parity of four
    /// Majorana zero modes hosted on an H-shaped superconducting island. The
    /// dominant error channels are quasiparticle poisoning (set by the parent
    /// superconducting gap) and Majorana hybridization (set by E_M).
    type TopologicalDeviceProfile = {
        /// Human-readable device / generation name.
        Name: string

        /// Superconductor–semiconductor material stack and substrate.
        MaterialStack: string

        /// Parent superconducting gap Δ_parent (µeV). Larger ⇒ harder to break
        /// Cooper pairs ⇒ stronger suppression of quasiparticle poisoning.
        ParentGapUeV: float

        /// Proximity-induced gap in the nanowire Δ_ind (µeV); None if not reported.
        InducedGapUeV: float option

        /// Topological gap Δ_T, top-quintile (µeV). Error rates are exponentially
        /// suppressed as Δ_T increases (near equilibrium).
        TopologicalGapUeV: float

        /// Majorana hybridization splitting E_M (µeV). Reported as an upper bound
        /// when it falls below the experimental measurement resolution.
        MajoranaSplittingUpperBoundUeV: float

        /// Measured single-wire parity (Z) lifetime (seconds). Maps directly to
        /// the qubit bit-flip lifetime, since the qubit is encoded in parity.
        ParityLifetimeSeconds: float

        /// Typical qubit operation (measurement) time (microseconds).
        OperationTimeMicroseconds: float

        /// Operating temperature (Kelvin).
        TemperatureKelvin: float

        /// Number of physical tetron qubits in the demonstrated array.
        TetronQubits: int

        /// Citation / source for these numbers.
        Source: string
    }

    /// Quasiparticle-poisoning rate implied by the measured parity lifetime (Hz).
    /// The parity flip process is a homogeneous Poisson process with rate 1/τ.
    let poisoningRateHz (profile: TopologicalDeviceProfile) : float =
        if profile.ParityLifetimeSeconds <= 0.0 then 0.0
        else 1.0 / profile.ParityLifetimeSeconds

    /// Ratio of parity lifetime to operation time — roughly the number of
    /// operations that fit inside a single parity state before a flip is likely.
    let operationsPerParityFlip (profile: TopologicalDeviceProfile) : float =
        let opSeconds = profile.OperationTimeMicroseconds / 1_000_000.0
        if opSeconds <= 0.0 then 0.0
        else profile.ParityLifetimeSeconds / opSeconds

    /// Majorana 1 — Al–InAs tetron (single-tetron generation).
    ///
    /// Representative of the aluminium-based devices whose interferometric
    /// single-shot parity measurements reported parity lifetimes of ~1–12 ms.
    /// The Al generation's E_M is not separately reported; the value below is the
    /// conductance technique's resolution floor (≈1.76·k_B·T ≈ 7.6 µeV at 50 mK),
    /// NOT a measured splitting — E_M could be anywhere below it.
    let majorana1 : TopologicalDeviceProfile = {
        Name = "Majorana 1 (Al–InAs tetron)"
        MaterialStack = "Al–InAs on InP"
        ParentGapUeV = 300.0
        InducedGapUeV = None
        TopologicalGapUeV = 30.0                 // top-quintile Δ_T in Al–InAs devices
        // Resolution floor of the conductance technique, NOT a measured E_M
        // (the M2 rf method resolves ~1 µeV — see majorana2).
        MajoranaSplittingUpperBoundUeV = 7.6
        ParityLifetimeSeconds = 0.012            // 1–12 ms range (upper end)
        OperationTimeMicroseconds = 1.0
        TemperatureKelvin = 0.050
        TetronQubits = 1
        Source =
            "Microsoft Quantum Al–InAs hybrid tetron devices (Majorana 1 generation); "
            + "~1–12 ms parity lifetimes, top-quintile Δ_T ≈ 30 µeV."
    }

    /// Majorana 2 — InAs–Pb tetron, multi-tetron array (June 2026).
    ///
    /// Replacing aluminium with the higher-gap superconductor lead, and moving
    /// to a GaSb substrate with an InAs / InAs₀.₈Sb₀.₂ composite quantum well,
    /// more than doubles the topological gap and extends the parity lifetime by
    /// over three orders of magnitude (τ_Z = 22 ± 1 s). The demonstrated unit
    /// cell hosts four tetron qubits (AA / AB / BA / BB) and tiles to larger
    /// arrays (e.g. 12 qubits) without changing control or readout.
    let majorana2 : TopologicalDeviceProfile = {
        Name = "Majorana 2 (InAs–Pb tetron)"
        MaterialStack = "InAs–Pb on GaSb (InAs / InAs₀.₈Sb₀.₂ quantum well, 10 nm Pb)"
        ParentGapUeV = 1300.0                    // epitaxial Pb, Δ_Pb ≈ 1.3 meV
        InducedGapUeV = Some 570.0               // induced gap in lowest-subband nanowire
        TopologicalGapUeV = 70.0                 // top-quintile Δ_T ≈ 70 µeV (> 2× Majorana 1)
        MajoranaSplittingUpperBoundUeV = 1.0     // E_M < 1 µeV (below rf spectroscopy resolution)
        ParityLifetimeSeconds = 22.0             // τ_Z = 22 ± 1 s (minute-scale instances observed)
        OperationTimeMicroseconds = 1.0          // µs-scale Pauli measurements
        TemperatureKelvin = 0.050                // Δ_T / k_B T > 10
        TetronQubits = 4                         // 4-tetron unit cell; tiles to 12-qubit arrays
        Source =
            "Microsoft Quantum, \"20 Second Parity Lifetime in an InAs–Pb Tetron Device\" "
            + "(June 2, 2026); quantum.microsoft.com Majorana 2 blog post."
    }

    /// All known device profiles, newest first.
    let all : TopologicalDeviceProfile list = [ majorana2; majorana1 ]
