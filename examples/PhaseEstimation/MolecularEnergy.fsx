#!/usr/bin/env dotnet fsi
// ============================================================================
// Quantum Phase Estimation - gate eigenphases, and the H2 molecule's energy
// ============================================================================
//
// QPE reads the phase φ of an eigenvalue e^(2πiφ) of a unitary U. The first three
// scenarios estimate that phase for a one-qubit gate on its eigenvector |1⟩, on the
// local simulator (educational; "molecular" and "crystal" are scenario names only):
//   tgate      T                          φ = 1/8
//   molecular  Rz(θ) = e^(-iθZ/2)         φ = θ/(4π)  (the controlled-Rz kickback on |1⟩)
//   crystal    P(angle) = diag(1, e^(i·angle))   φ = angle/(2π)
// The molecular scenario reads Rz(θ) as U = e^(-iH) with the stand-in Hamiltonian
// H = (θ/2)·Z, so its "energy" is E = -2πφ (known modulo 2π), which is -θ/2 on |1⟩.
// Every result is checked against the exact phase.
//
// The h2 scenario is a real molecular QPE (QuantumChemistry.QPE.runWithAsync): the H2
// electronic Hamiltonian in STO-3G, mapped to 4 qubits by Jordan-Wigner, shifted by an
// upper bound on its spectrum so every eigenvalue has its own phase; controlled
// e^(-iHt·2^j) as a first-order Trotter circuit repeated 2^j times; the Hartree-Fock
// state as the starting state; 8 counting qubits (12 in all). The energy is the most
// probable peak of the readings, refined between its two highest bins, and is compared
// with UCCSD-VQE (exact for H2 in this basis). QPE returns each eigenvalue with the
// probability that the starting state overlaps it: at --bond 2.0 the Hartree-Fock state
// overlaps the ground state only 71%, and a second peak (a doubly excited state) shows.
// About 7 s on the local simulator; larger molecules need error-corrected hardware.
//
// Add `--svg [path]` to also draw the Rz(θ) estimate (with --theta)
// as an animated picture (default: _images/phase-estimation.svg next to this script).
//
// ============================================================================

#r "nuget: Microsoft.Extensions.Logging.Abstractions, 10.0.0"
#r "nuget: MathNet.Numerics, 5.0.0"
// The library comes from NuGet; `dotnet fsi --define:LOCAL_BUILD <script>` uses the repo's Debug build.
#if LOCAL_BUILD
#r "../../src/FSharp.Azure.Quantum/bin/Debug/net10.0/FSharp.Azure.Quantum.dll"
#else
#r "nuget: FSharp.Azure.Quantum"
#endif
#load "../_common/Cli.fs"
#load "../_common/Data.fs"
#load "../_common/Reporting.fs"
#load "../_common/SvgAnimation.fs"

open System
open System.IO
open System.Numerics
open System.Threading
open FSharp.Azure.Quantum
open FSharp.Azure.Quantum.QuantumPhaseEstimator
open FSharp.Azure.Quantum.Algorithms.QPE
open FSharp.Azure.Quantum.Core
open FSharp.Azure.Quantum.Core.BackendAbstraction
open FSharp.Azure.Quantum.Backends.LocalBackend
open FSharp.Azure.Quantum.QuantumChemistry
open FSharp.Azure.Quantum.Examples.Common
open SvgAnimation

// --- Quantum Backend (Rule 1) ---
let quantumBackend = LocalBackend() :> IQuantumBackend

// --- CLI ---
let argv = fsi.CommandLineArgs |> Array.skip 1
let args = Cli.parse argv

Cli.exitIfHelp
    "MolecularEnergy.fsx"
    "Quantum Phase Estimation: single-qubit unitaries (T, Rz(theta), P(angle)) and the H2 molecule's energy"
    [
        {
            Name = "scenario"
            Description =
                "Which scenario (all|tgate|molecular = Rz(theta)|crystal = P(angle)|h2 = QPE of the H2 Hamiltonian)"
            Default = Some "all"
        }
        {
            Name = "bond"
            Description = "H-H bond length for the h2 scenario (Angstrom)"
            Default = Some "0.7414"
        }
        {
            Name = "counting"
            Description = "Counting qubits for the h2 scenario (3 to 12; 4 system qubits + counting <= 16)"
            Default = Some "8"
        }
        {
            Name = "trotter-steps"
            Description = "Trotter steps per e^(-iHt) for the h2 scenario"
            Default = Some "4"
        }
        {
            Name = "precision"
            Description = "Precision qubits for estimation"
            Default = Some "10"
        }
        {
            Name = "theta"
            Description = "Rz rotation angle for the molecular scenario (radians)"
            Default = Some "1.0472"
        }
        {
            Name = "phase-angle"
            Description = "Phase-gate angle for the crystal scenario (radians)"
            Default = Some "0.7854"
        }
        {
            Name = "output"
            Description = "Write results to JSON file"
            Default = None
        }
        {
            Name = "csv"
            Description = "Write results to CSV file"
            Default = None
        }
        {
            Name = "quiet"
            Description = "Suppress console output"
            Default = None
        }
        {
            Name = "svg"
            Description = "Draw the Rz(theta) estimate as an animated SVG (default path: _images/phase-estimation.svg)"
            Default = None
        }
    ]
    args

let quiet = Cli.hasFlag "quiet" args
let outputPath = Cli.tryGet "output" args
let csvPath = Cli.tryGet "csv" args
let scenario = Cli.getOr "scenario" "all" args
let cliPrecision = Cli.getIntOr "precision" 10 args
let theta = Cli.getFloatOr "theta" (Math.PI / 3.0) args
let phaseAngle = Cli.getFloatOr "phase-angle" (Math.PI / 4.0) args
let bondLength = Cli.getFloatOr "bond" 0.7414 args
let h2Counting = Cli.tryGet "counting" args |> Option.map int
let trotterSteps = Cli.getIntOr "trotter-steps" 4 args

let pr fmt =
    Printf.ksprintf
        (fun s ->
            if not quiet then
                printfn "%s" s)
        fmt

let shouldRun key = scenario = "all" || scenario = key

/// A phase as a fraction of a turn, in [0, 1).
let wrapTurn (x: float) = (x % 1.0 + 1.0) % 1.0

/// Distance between two phases around the circle, in turns: 0.999 and 0.0 are 0.001 apart.
let phaseError (a: float) (b: float) =
    let d = abs (wrapTurn a - wrapTurn b)
    min d (1.0 - d)

/// Energy of the stand-in H = (θ/2)·Z read off U = Rz(θ) = e^(-iH) (t = 1): E = -2πφ, modulo 2π.
/// Written as a subtraction so that φ = 0 gives 0, not -0.
let energyOf (phase: float) = 0.0 - 2.0 * Math.PI * phase

// --- Result Type ---

type QPEResult =
    {
        Scenario: string
        Label: string
        Phase: float
        ExpectedPhase: float
        PhaseError: float
        Qubits: int
        GateCount: int
        PrecisionBits: int
        Note: string
    }

let mutable jsonResults: QPEResult list = []
let mutable csvRows: string list list = []

let record (r: QPEResult) =
    jsonResults <- jsonResults @ [ r ]

    csvRows <-
        csvRows
        @ [
            [
                r.Scenario
                r.Label
                $"%.6f{r.Phase}"
                $"%.6f{r.ExpectedPhase}"
                $"%.6f{r.PhaseError}"
                string r.Qubits
                string r.GateCount
                string r.PrecisionBits
                r.Note
            ]
        ]

// ============================================================================
// SCENARIO 1: T-Gate Phase Estimation (Educational)
// ============================================================================

if shouldRun "tgate" then
    pr "--- Scenario 1: T-Gate Phase Estimation (Educational) ---"
    pr ""
    pr "The T-gate has eigenvalue e^(i*pi/4), so phase = 1/8 = 0.125"
    pr ""

    let tGateProblem =
        phaseEstimator {
            unitary TGate
            precision cliPrecision
            backend quantumBackend
        }

    match tGateProblem with
    | Ok prob ->
        match estimate prob with
        | Ok result ->
            let expected = 0.125
            let err = phaseError result.Phase expected

            pr "  [OK] Phase Estimated!"
            pr "  Estimated Phase:  %.6f" result.Phase
            pr "  Expected Phase:   %.6f" expected
            pr "  Error:            %.6f" err
            pr "  Eigenvalue:       %.4f + %.4fi" result.Eigenvalue.Real result.Eigenvalue.Imaginary
            pr "  Qubits: %d  |  Gates: %d  |  Precision: %d bits" result.TotalQubits result.GateCount result.Precision
            pr ""

            record
                {
                    Scenario = "tgate"
                    Label = "T-Gate Phase"
                    Phase = result.Phase
                    ExpectedPhase = expected
                    PhaseError = err
                    Qubits = result.TotalQubits
                    GateCount = result.GateCount
                    PrecisionBits = result.Precision
                    Note = $"eigenvalue magnitude=%.4f{result.Eigenvalue.Magnitude}"
                }

        | Error err -> pr "  [ERROR] Execution: %s" err.Message

    | Error err -> pr "  [ERROR] Builder: %s" err.Message

// ============================================================================
// SCENARIO 2: Rz(θ) as a stand-in for e^(-iHt) (scenario key "molecular")
// ============================================================================

if shouldRun "molecular" then
    pr "--- Scenario 2: Rz(theta) stand-in for a time-evolution unitary ---"
    pr ""
    pr "  U = Rz(theta) = e^(-i*theta*Z/2), read as U = e^(-iH) with H = (theta/2)*Z"
    pr "  (a one-qubit toy, not a molecular Hamiltonian)"
    pr "  Rotation angle: %.4f radians (%.1f deg)" theta (theta * 180.0 / Math.PI)
    pr "  On |1>, Rz(theta) multiplies by e^(i*theta/2), so phase = theta/(4*pi)"
    pr ""

    let molecularProblem =
        phaseEstimator {
            unitary (RotationZ theta)
            precision (max cliPrecision 12)
            targetQubits 1
            backend quantumBackend
        }

    match molecularProblem with
    | Ok prob ->
        match estimate prob with
        | Ok result ->
            // QPE applies controlled-Rz(2^j θ); on |1⟩ its kickback is e^(iθ/2), a phase of θ/(4π).
            let expectedPhase = wrapTurn (theta / (4.0 * Math.PI))
            let err = phaseError result.Phase expectedPhase
            let energy = energyOf result.Phase

            pr "  [OK] Phase Estimated"
            pr "  Estimated Phase:  %.6f" result.Phase
            pr "  Expected Phase:   %.6f  (theta/(4*pi))" expectedPhase
            pr "  Phase Error:      %.6f  (one reading step = %.6f)" err (1.0 / float (1 <<< prob.Precision))
            pr "  Stand-in E:       %.6f  (E = -2*pi*phase, modulo 2*pi; exact -theta/2 = %.6f)" energy (-theta / 2.0)
            pr "  Qubits: %d  |  Gates: %d  |  Precision: %d bits" result.TotalQubits result.GateCount prob.Precision
            pr ""

            record
                {
                    Scenario = "molecular"
                    Label = "Rz(theta) stand-in"
                    Phase = result.Phase
                    ExpectedPhase = expectedPhase
                    PhaseError = err
                    Qubits = result.TotalQubits
                    GateCount = result.GateCount
                    PrecisionBits = prob.Precision
                    Note = $"stand-in E=-2*pi*phase=%.6f{energy}, theta=%.4f{theta}"
                }

        | Error err -> pr "  [ERROR] Execution: %s" err.Message

    | Error err -> pr "  [ERROR] Builder: %s" err.Message

// ============================================================================
// SCENARIO 3: Phase gate P(angle) (scenario key "crystal")
// ============================================================================

if shouldRun "crystal" then
    pr "--- Scenario 3: Phase Gate P(angle) ---"
    pr ""
    pr "  U = P(angle) = diag(1, e^(i*angle)); on |1> phase = angle/(2*pi)"
    pr "  Phase angle: %.4f radians (%.1f deg)" phaseAngle (phaseAngle * 180.0 / Math.PI)
    pr ""

    let materialProblem =
        phaseEstimator {
            unitary (PhaseGate phaseAngle)
            precision (max cliPrecision 12)
            backend quantumBackend
        }

    match materialProblem with
    | Ok problem ->
        match estimate problem with
        | Ok result ->
            let expectedPhase = wrapTurn (phaseAngle / (2.0 * Math.PI))
            let err = phaseError result.Phase expectedPhase

            pr "  [OK] Phase Estimated"
            pr "  Estimated Phase:  %.6f" result.Phase
            pr "  Expected Phase:   %.6f  (angle/(2*pi))" expectedPhase
            pr "  Phase Error:      %.6f" err
            pr "  Qubits: %d  |  Gates: %d" result.TotalQubits result.GateCount
            pr ""

            record
                {
                    Scenario = "crystal"
                    Label = "Phase gate P(angle)"
                    Phase = result.Phase
                    ExpectedPhase = expectedPhase
                    PhaseError = err
                    Qubits = result.TotalQubits
                    GateCount = result.GateCount
                    PrecisionBits = problem.Precision
                    Note = $"phaseAngle=%.4f{phaseAngle} rad"
                }

        | Error err -> pr "  [ERROR] %s" err.Message

    | Error err -> pr "  [ERROR] Builder: %s" err.Message

// ============================================================================
// SCENARIO 4: H2 ground-state energy by QPE of e^(-iHt) (scenario key "h2")
// ============================================================================

if shouldRun "h2" then
    pr "--- Scenario 4: H2 energy by phase estimation of its Hamiltonian ---"
    pr ""

    let molecule = Molecule.createH2 bondLength

    let solverConfig: SolverConfig =
        {
            Method = GroundStateMethod.QPE
            MaxIterations = 100
            Tolerance = 1e-8
            InitialParameters = None
            Backend = Some quantumBackend
            ProgressReporter = None
            ErrorMitigation = None
            IntegralProvider = None
        }

    let settings =
        { QPE.defaultSettings with
            CountingQubits = h2Counting
            TrotterSteps = trotterSteps
        }

    // The circuit QPE.runWithAsync builds, for its size: STO-3G integrals -> Jordan-Wigner -> shift and t.
    let plan =
        Sto3gIntegrals.compute molecule
        |> Result.bind (fun integrals ->
            MolecularHamiltonian.buildFromIntegrals integrals MolecularHamiltonian.JordanWigner
            |> Result.map (fun (h, _) ->
                let pauli = QPE.toPauliHamiltonian h

                let m =
                    h2Counting |> Option.defaultValue (min 8 (QPE.MaxTotalQubits - pauli.NumQubits))

                QPE.evolutionPlan pauli m settings.TrotterOrder trotterSteps))

    // Reference: UCCSD-VQE on the same integrals, which is exact (FCI) for H2 in STO-3G.
    let reference =
        VQE.runAsync
            molecule
            { solverConfig with
                Method = GroundStateMethod.VQE
            }
            CancellationToken.None
        |> Async.AwaitTask
        |> Async.RunSynchronously

    let estimate =
        QPE.runWithAsync settings molecule solverConfig CancellationToken.None
        |> Async.AwaitTask
        |> Async.RunSynchronously

    match plan, reference, estimate with
    | Ok plan, Ok vqe, Ok qpe ->
        let d =
            match qpe.Estimation with
            | PhaseEstimation d -> d
            | other -> failwithf "expected a phase estimation, got %A" other

        let nuclear = Molecule.nuclearRepulsion molecule |> Result.defaultValue 0.0

        let phaseOf energy =
            QPE.energyToPhase plan (energy - nuclear)

        let gates = (QPE.circuit plan [ for q in 0..1 -> CircuitBuilder.X q ]).Gates.Length

        pr "  H2 at %.4f A, STO-3G, Jordan-Wigner: 4 system qubits + %d counting qubits" bondLength d.CountingQubits

        pr
            "  U = exp(-i(H - %.4f)t), t = %.4f: E = -2*pi*phase/t + %.4f (+ %.6f nuclear repulsion)"
            d.EnergyShift
            d.EvolutionTime
            d.EnergyShift
            nuclear

        pr
            "  controlled-U^(2^j) = %d-step order-%d Trotter circuit repeated 2^j times; %d gates in all"
            d.TrotterStepsPerEvolution
            d.TrotterOrder
            gates

        pr "  one reading step = %.2f mHa; the peak is refined between its two highest bins" (1000.0 * d.BinWidth)
        pr ""
        pr "  Peaks of the reading distribution (each an eigenvalue the Hartree-Fock state overlaps):"

        for peak in d.Peaks do
            pr "    E = %12.6f Ha   probability %.3f" peak.Energy peak.Probability

        pr ""
        pr "  QPE energy (most probable peak): %.6f Ha" qpe.Energy

        pr
            "  UCCSD-VQE reference:             %.6f Ha  (difference %+.2f mHa)"
            vqe.Energy
            (1000.0 * (qpe.Energy - vqe.Energy))

        for note in qpe.Notes do
            pr "  note: %s" note

        pr ""

        record
            {
                Scenario = "h2"
                Label = "H2 QPE energy"
                Phase = phaseOf qpe.Energy
                ExpectedPhase = phaseOf vqe.Energy
                PhaseError = phaseError (phaseOf qpe.Energy) (phaseOf vqe.Energy)
                Qubits = 4 + d.CountingQubits
                GateCount = gates
                PrecisionBits = d.CountingQubits
                Note = $"E=%.6f{qpe.Energy} Ha, VQE %.6f{vqe.Energy} Ha, bond %.4f{bondLength} A"
            }
    | Error e, _, _
    | _, Error e, _
    | _, _, Error e -> pr "  [ERROR] %s" e.Message

// --- JSON output ---

outputPath
|> Option.iter (fun path ->
    let payload =
        jsonResults
        |> List.map (fun r ->
            dict
                [
                    "scenario", box r.Scenario
                    "label", box r.Label
                    "phase", box r.Phase
                    "expectedPhase", box r.ExpectedPhase
                    "phaseError", box r.PhaseError
                    "qubits", box r.Qubits
                    "gateCount", box r.GateCount
                    "precisionBits", box r.PrecisionBits
                    "note", box r.Note
                ])

    Reporting.writeJson path payload)

// --- CSV output ---

csvPath
|> Option.iter (fun path ->
    let header =
        [
            "scenario"
            "label"
            "phase"
            "expectedPhase"
            "phaseError"
            "qubits"
            "gateCount"
            "precisionBits"
            "note"
        ]

    Reporting.writeCsv path header csvRows)

// --- Summary ---

if not quiet then
    pr ""
    pr "=== Summary ==="

    jsonResults
    |> List.iter (fun r -> pr "  [OK] %-25s phase=%.6f (err=%.6f) %d qubits" r.Label r.Phase r.PhaseError r.Qubits)

    pr ""
    pr "Key: n counting qubits read the phase to 1/2^n of a turn, using controlled-U^(2^j)"
    pr "     for j < n. For the one-qubit gates the phase is known exactly, so those check"
    pr "     the circuit; the h2 scenario estimates the molecule's ground-state energy."
    pr ""

if not quiet && outputPath.IsNone && csvPath.IsNone && (argv |> Array.isEmpty) then
    pr "Tip: Use --output results.json or --csv results.csv to export data."
    pr "     Use --scenario tgate to run a single scenario."
    pr "     Use --precision 14 for higher accuracy."
    pr "     Run with --help for all options."

// ============================================================================
// PICTURE (only with --svg): the Rz(θ) estimate, step by step
// ============================================================================

/// Counting qubits shown gate by gate, then the precisions the picture sweeps.
[<Literal>]
let pictureKickBits = 4

let picturePrecisions = [ 4..8 ]

let orFail (r: Result<'T, QuantumError>) =
    r |> Result.defaultWith (fun e -> failwithf "picture: %s" e.Message)

/// Amplitudes of a state; index bit q is qubit q.
let amplitudesOf (state: QuantumState) : Complex[] =
    match state with
    | QuantumState.StateVector sv ->
        Array.init (LocalSimulator.StateVector.dimension sv) (fun i -> LocalSimulator.StateVector.getAmplitude i sv)
    | other -> failwithf "picture: expected a state vector, got %s" (other.GetType().Name)

/// A qubit on its own: the hand's length 2|ρ₁₀| (1 = an even mix of 0 and 1) and the
/// phase of its 1 against its 0 in turns, 0 to 1.
let handOf (amps: Complex[]) (q: int) : float * float =
    let mask = 1 <<< q
    let mutable rho10 = Complex.Zero

    for i in 0 .. amps.Length - 1 do
        if i &&& mask <> 0 then
            rho10 <- rho10 + amps.[i] * Complex.Conjugate amps.[i ^^^ mask]

    let length = 2.0 * rho10.Magnitude
    let turn = ((rho10.Phase / (2.0 * Math.PI)) % 1.0 + 1.0) % 1.0
    (length, (if length < 1e-9 || turn > 1.0 - 1e-9 then 0.0 else turn))

/// Chance of each reading of `bits` counting qubits: QPE without final swaps reads
/// counting qubit q as bit bits-1-q of the reading.
let chancesOf (bits: int) (amps: Complex[]) : float[] =
    let chances = Array.zeroCreate (1 <<< bits)

    for i in 0 .. amps.Length - 1 do
        let reading =
            Seq.sum [ for q in 0 .. bits - 1 -> ((i >>> q) &&& 1) <<< (bits - 1 - q) ]

        chances.[reading] <- chances.[reading] + amps.[i].Magnitude * amps.[i].Magnitude

    chances

/// The gates QPE lowers Rz(θ) to: H on each counting qubit, X on the target (qubit `bits`),
/// then Rz(2^j θ) on the target controlled by counting qubit j, for the first `applied` j.
let kickBack (bits: int) (applied: int) : QuantumState =
    let gates =
        [ for q in 0 .. bits - 1 -> CircuitBuilder.H q ]
        @ [ CircuitBuilder.X bits ]
        @ [
            for j in 0 .. applied - 1 -> CircuitBuilder.CRZ(j, bits, float (1 <<< j) * theta)
        ]

    let start = quantumBackend.InitializeState(bits + 1) |> orFail

    UnifiedBackend.applySequence quantumBackend (gates |> List.map QuantumOperation.Gate) start
    |> orFail

/// QPE's inverse QFT on the counting qubits, without the final swaps.
let inverseQftGates (bits: int) =
    [
        for t in bits - 1 .. -1 .. 0 do
            for k in t + 1 .. bits - 1 do
                yield CircuitBuilder.CP(k, t, -2.0 * Math.PI / float (1 <<< (k - t + 1)))

            yield CircuitBuilder.H t
    ]

type PhaseStep =
    {
        Label: string
        Caption: string
        /// Counting qubits in this step.
        Bits: int
        /// The state after the controlled-U steps applied so far: the dials.
        KickBack: Complex[]
        /// Counting qubits that have had their controlled-U.
        Applied: int
        /// Chance of each reading if the counting qubits were read at this step.
        Chances: float[]
        /// The library run's reading and phase estimate (after the inverse QFT).
        Estimate: (int * float) option
    }

let phaseSteps () : PhaseStep[] * float =
    let k = pictureKickBits

    // The exact phase, read off the state: one controlled Rz(θ) turns counting qubit 0 by φ.
    let exact = snd (handOf (amplitudesOf (kickBack k 1)) 0)
    let expected = wrapTurn (theta / (4.0 * Math.PI))

    if abs (exact - expected) > 1e-9 && abs (abs (exact - expected) - 1.0) > 1e-9 then
        failwithf "picture: one controlled Rz(θ) turned qubit 0 by %g, not θ/4π = %g" exact expected

    let steps = ResizeArray<PhaseStep>()

    for applied in 0..k do
        let state = amplitudesOf (kickBack k applied)

        let label, caption =
            if applied = 0 then
                ($"Start: %d{k} counting qubits, each 0 and 1 at once; the target qubit holds |1⟩",
                 "U = Rz(θ) leaves |1⟩ as it is apart from a phase, φ of a turn: the number we want.")
            else
                let j = applied - 1
                let times = 1 <<< j
                let turn = snd (handOf state j)

                ($"Controlled U×%d{times} on q%d{j}: its dial turns by %d{times} × φ",
                 $"The target does not change; the phase kicks back onto q%d{j}: %d{times} × %.4f{exact} → %.3f{turn} turn (whole turns drop out).")

        steps.Add
            {
                Label = label
                Caption = caption
                Bits = k
                KickBack = state
                Applied = applied
                Chances = chancesOf k state
                Estimate = None
            }

    for bits in picturePrecisions do
        let config: FSharp.Azure.Quantum.Algorithms.QPE.QPEConfig =
            {
                CountingQubits = bits
                TargetQubits = 1
                UnitaryOperator = RotationZ theta
                EigenVector = None
            }

        let result =
            FSharp.Azure.Quantum.Algorithms.QPE.executeWith config quantumBackend false
            |> orFail

        let final = amplitudesOf result.FinalState
        let dials = kickBack bits bits

        // The gates above followed by the inverse QFT must give the library's own final state.
        let mine =
            UnifiedBackend.applySequence quantumBackend (inverseQftGates bits |> List.map QuantumOperation.Gate) dials
            |> orFail
            |> amplitudesOf

        let gap =
            Array.map2 (fun (a: Complex) (b: Complex) -> (a - b).Magnitude) mine final
            |> Array.max

        if gap > 1e-9 then
            failwithf "picture: the %d-bit QPE gates differ from QPE.executeWith by %g" bits gap

        let chances = chancesOf bits final

        if chances.[result.MeasurementOutcome] < 0.5 * Array.max chances then
            failwithf "picture: the run's reading %d is not among the likely readings" result.MeasurementOutcome

        let label, caption =
            if bits = k then
                ($"Inverse QFT: the %d{k} angles become one likely %d{k}-bit reading",
                 (let angles =
                     [ for j in 0 .. k - 1 -> if j = 0 then "φ" else $"%d{1 <<< j}φ" ]
                     |> String.concat ", "

                  $"The angles %s{angles} hold φ's binary digits; the inverse QFT turns them into a number."))
            else
                ($"%d{bits} counting qubits: readings 1/%d{1 <<< bits} of a turn apart, bunched around φ",
                 $"Each extra qubit turns twice as far again: one more binary digit of φ (q%d{bits - 1}: U×%d{1 <<< (bits - 1)}).")

        steps.Add
            {
                Label = label
                Caption = caption
                Bits = bits
                KickBack = amplitudesOf dials
                Applied = bits
                Chances = chances
                Estimate = Some(result.MeasurementOutcome, result.EstimatedPhase)
            }

    (steps.ToArray(), exact)

let drawPhasePicture (path: string) =
    let steps, exact = phaseSteps ()
    let n = steps.Length
    let per (f: PhaseStep -> 'T) = Array.map f steps
    let maxBits = steps |> Array.map (fun s -> s.Bits) |> Array.max
    let picW, picH = 760.0, 566.0
    let accent, exactColour, barColour = colour 3, colour 1, colour 0

    let pic =
        Picture(picW, picH, frames = n, durationS = 2.0 * float n, title = "Quantum phase estimation", hold = 0.55)

    let energy = energyOf

    // Header.
    pic.Text(16.0, 30.0, "Phase estimation reads a phase off turning qubit dials", size = 20.0, bold = true)

    pic.Text(
        16.0,
        50.0,
        "Controlled U turns counting qubit j by 2^j times the phase φ; the inverse QFT turns those angles into a reading of φ, the eigenphase of U.",
        size = 11.5,
        fill = grey
    )

    pic.FrameText(16.0, 78.0, per (fun s -> s.Label), size = 14.0, bold = true, fill = accent)

    // Top: the target qubit and one dial per counting qubit.
    pic.Rect(16.0, 92.0, picW - 32.0, 212.0, fill = panel, stroke = frameColour, rx = 6.0)

    pic.Text(
        28.0,
        110.0,
        "Counting qubits after their controlled-U steps (before the inverse QFT)",
        size = 12.0,
        bold = true
    )

    pic.Text(
        28.0,
        124.0,
        "hand = the phase each qubit picked up, clockwise from 12, in turns · U×k = U applied k times under that qubit's control",
        size = 10.5,
        fill = grey
    )

    pic.Rect(28.0, 140.0, 96.0, 122.0, fill = "white", stroke = frameColour, rx = 4.0)
    pic.Text(76.0, 158.0, "target qubit", size = 11.0, bold = true, anchor = "middle")
    pic.Text(76.0, 180.0, "|1⟩", size = 18.0, anchor = "middle")
    pic.Text(76.0, 202.0, "U = Rz(θ)", size = 11.0, anchor = "middle")
    pic.Text(76.0, 218.0, $"θ = %.4f{theta}", size = 11.0, anchor = "middle")
    pic.Text(76.0, 236.0, "φ = θ/4π", size = 11.0, anchor = "middle")
    pic.Text(76.0, 252.0, $"= %.4f{exact} turn", size = 11.0, anchor = "middle")

    let r, dialY = 28.0, 180.0

    for j in 0 .. maxBits - 1 do
        let cx = 162.0 + 76.0 * float j
        let shown = per (fun s -> if j < s.Bits then 1.0 else 0.0)

        let isNew =
            per (fun s -> if s.Estimate.IsNone && s.Applied = j + 1 then 1.0 else 0.0)

        let hand (s: PhaseStep) =
            if j < s.Bits then handOf s.KickBack j else (0.0, 0.0)

        pic.Circle(cx, dialY, r + 5.0, stroke = accent, width = 3.0, animate = [ ("opacity", isNew) ])
        pic.Circle(cx, dialY, r, fill = "white", stroke = ink, width = 1.2, animate = [ ("opacity", shown) ])

        for t in [ 0.0; 0.25; 0.5; 0.75 ] do
            let a = 2.0 * Math.PI * t

            pic.Line(
                cx + (r - 5.0) * sin a,
                dialY - (r - 5.0) * cos a,
                cx + r * sin a,
                dialY - r * cos a,
                stroke = grey,
                width = 1.0,
                animate = [ ("opacity", shown) ]
            )

        let slice (s: PhaseStep) =
            let length, turn = hand s
            let len = r * length * 0.9

            let points =
                [
                    for p in 0..16 ->
                        let a = 2.0 * Math.PI * turn * float p / 16.0
                        sprintf "L%s,%s" (num (cx + len * sin a)) (num (dialY - len * cos a))
                ]

            sprintf "M%s,%s %s Z" (num cx) (num dialY) (String.concat " " points)

        let slices = per slice
        pic.Path(slices.[0], stroke = "none", width = 0.0, fill = tint 0.55 accent, opacity = 0.7, shapes = slices)

        let tip (f: float -> float) (s: PhaseStep) =
            let length, turn = hand s
            r * 0.9 * length * f (2.0 * Math.PI * turn)

        pic.Line(
            cx,
            dialY,
            cx,
            dialY,
            stroke = ink,
            width = 2.5,
            animate =
                [
                    "x2", per (fun s -> cx + tip sin s)
                    "y2", per (fun s -> dialY - tip cos s)
                    "opacity", shown
                ]
        )

        pic.Circle(cx, dialY, 2.5, fill = ink, animate = [ ("opacity", shown) ])

        let words (f: PhaseStep -> string) =
            per (fun s -> if j < s.Bits then f s else "")

        pic.FrameText(cx, dialY + r + 16.0, words (fun _ -> $"q%d{j}"), size = 11.0, bold = true, anchor = "middle")

        pic.FrameText(
            cx,
            dialY + r + 30.0,
            words (fun _ -> $"U×%d{1 <<< j}"),
            size = 10.0,
            fill = grey,
            anchor = "middle"
        )

        pic.FrameText(
            cx,
            dialY + r + 44.0,
            words (fun s ->
                if j < s.Applied then
                    $"%.3f{snd (hand s)} turn"
                else
                    "not yet"),
            size = 10.0,
            anchor = "middle"
        )

    pic.FrameText(picW / 2.0, 294.0, per (fun s -> s.Caption), size = 11.5, anchor = "middle")

    // Bottom: the chance of each reading, on a phase axis with the stand-in's energy under it.
    pic.Rect(16.0, 312.0, picW - 32.0, 230.0, fill = panel, stroke = frameColour, rx = 6.0)
    pic.Text(28.0, 330.0, "Chance of each reading of the counting qubits", size = 12.0, bold = true)

    pic.Text(
        28.0,
        344.0,
        "reading k of n bits means φ ≈ k/2^n of a turn · E = −2π × φ for the stand-in H = (θ/2)·Z, as the script prints it",
        size = 10.5,
        fill = grey
    )

    pic.FrameText(
        picW - 28.0,
        330.0,
        per (fun s -> $"likeliest reading: %g{Math.Round(100.0 * Array.max s.Chances, 2)}%%"),
        size = 12.0,
        bold = true,
        fill = barColour,
        anchor = "end"
    )

    let x0, x1, baseY, area = 70.0, picW - 40.0, 452.0, 90.0
    let xOf (phase: float) = x0 + (x1 - x0) * phase
    let yOf (p: float) = baseY - area * p

    for p, label in [ (0.5, "50%"); (1.0, "100%") ] do
        pic.Line(x0, yOf p, x1, yOf p, stroke = light, width = 1.0)
        pic.Text(x0 - 6.0, yOf p + 4.0, label, size = 10.0, fill = grey, anchor = "end")

    pic.Line(x0, baseY, x1, baseY, stroke = grey, width = 1.0)
    pic.Text(x0 - 24.0, baseY + 24.0, "φ", size = 11.0, bold = true, anchor = "end")
    pic.Text(x0 - 24.0, baseY + 38.0, "E", size = 11.0, bold = true, anchor = "end")

    for t in [ 0.0; 0.25; 0.5; 0.75; 1.0 ] do
        pic.Line(xOf t, baseY, xOf t, baseY + 4.0, stroke = grey, width = 1.0)
        pic.Text(xOf t, baseY + 24.0, $"%g{t}", size = 10.0, fill = grey, anchor = "middle")
        pic.Text(xOf t, baseY + 38.0, $"%.2f{energy t}", size = 10.0, fill = grey, anchor = "middle")

    // Bars as one path per frame: the gate-by-gate frames share a shape, so they morph.
    let bars (bits: int) (chances: float[]) (keepAll: bool) =
        let width = max 1.5 (0.7 * (x1 - x0) / float (1 <<< bits))

        [
            for m in 0 .. chances.Length - 1 do
                if keepAll || chances.[m] >= 5e-4 then
                    let x = xOf (float m / float (1 <<< bits))
                    let left, right = num (x - width / 2.0), num (x + width / 2.0)
                    let top = num (yOf chances.[m])
                    $"M%s{left},%s{num baseY} L%s{left},%s{top} L%s{right},%s{top} L%s{right},%s{num baseY} Z"
        ]
        |> String.concat " "

    let kick = pictureKickBits
    let flat = Array.zeroCreate<float>(1 <<< kick)

    let morphing =
        per (fun s ->
            if s.Bits = kick then
                bars kick s.Chances true
            else
                bars kick flat true)

    pic.Path(morphing.[0], stroke = "none", width = 0.0, fill = tint 0.3 barColour, shapes = morphing)

    let sweeping =
        per (fun s ->
            if s.Bits = kick then
                "M0,0"
            else
                bars s.Bits s.Chances false)

    pic.Path(sweeping.[0], stroke = "none", width = 0.0, fill = tint 0.3 barColour, shapes = sweeping)

    pic.Line(xOf exact, yOf 1.0 - 6.0, xOf exact, baseY, stroke = exactColour, width = 1.5, dash = "5 4")

    // The label sits on the side of the line with room for it.
    let onLeft = exact > 0.6

    pic.Text(
        xOf exact + (if onLeft then -6.0 else 6.0),
        yOf 1.0 - 2.0,
        $"exact φ = %.4f{exact}, E = %.4f{energy exact}",
        size = 10.5,
        fill = exactColour,
        anchor = (if onLeft then "end" else "start")
    )

    // The run's reading, as a marker under the axis.
    let marker (s: PhaseStep) =
        let x = s.Estimate |> Option.map (snd >> xOf) |> Option.defaultValue (xOf exact)

        $"M%s{num x},%s{num (baseY + 3.0)} L%s{num (x - 6.0)},%s{num (baseY + 13.0)} L%s{num (x + 6.0)},%s{num (baseY + 13.0)} Z"

    pic.Element(
        "path",
        [ "d", marker steps.[0]; "fill", accent; "stroke", "none" ],
        animate =
            [
                "d", per marker
                "opacity", per (fun s -> if s.Estimate.IsSome then "1" else "0")
            ]
    )

    let reading (s: PhaseStep) =
        match s.Estimate with
        | None ->
            ($"Read now, all %d{s.Chances.Length} readings are equally likely: the angles do not show in the counts.",
             "")
        | Some(m, phase) ->
            let bitsText = Convert.ToString(m, 2).PadLeft(s.Bits, '0')

            ($"The run read %s{bitsText} = %d{m}/%d{1 <<< s.Bits} → φ ≈ %.4f{phase}, E ≈ %.4f{energy phase} (triangle)",
             $"Off by %.4f{min (abs (phase - exact)) (1.0 - abs (phase - exact))} of a turn; neighbouring readings are %.4f{1.0 / float (1 <<< s.Bits)} apart")

    pic.FrameText(picW / 2.0, 512.0, per (reading >> fst), size = 11.5, anchor = "middle")
    pic.FrameText(picW / 2.0, 528.0, per (reading >> snd), size = 11.5, fill = grey, anchor = "middle")
    pic.Progress(16.0, 552.0, picW - 32.0, fill = accent)
    pic.Save path

match svgPath (Path.Combine(__SOURCE_DIRECTORY__, "_images", "phase-estimation.svg")) with
| Some path -> drawPhasePicture path
| None -> ()
