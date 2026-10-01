// ==============================================================================
// Hamiltonian Time Evolution Example
// ==============================================================================
// Demonstrates Trotter-Suzuki simulation for molecular dynamics using the
// unified backend architecture. The same simulation code runs on different
// quantum backends:
//   - LocalBackend (gate-based StateVector simulation)
//   - TopologicalUnifiedBackend (braiding-based FusionSuperposition)
//
// This example shows:
//   - The H2 Hamiltonian from real STO-3G integrals (bundled for R = 0.7414 Å),
//     Jordan-Wigner mapped to 4 qubits, one per spin orbital
//   - Time evolution exp(-iHt)|HF> from the Hartree-Fock state |1100>, which
//     mixes with the doubly excited determinant |0011> as time passes
//   - Trotter results checked against exact evolution of the 16-entry state vector
//   - Backend-agnostic algorithm design via IQuantumBackend
//   - Comparison of 1st vs 2nd order Trotter accuracy
//
// Qubit q is spin orbital q: qubits 0 and 1 are the lower (bonding) orbital,
// spin up and down; qubits 2 and 3 the upper (antibonding) one. Bitstrings list
// qubit 0 first, so |1100> has both electrons in the lower orbital.
//
// Other bond lengths need their own integrals: MolecularIntegrals from PySCF,
// Psi4 or an FCIDUMP file (for example through an IntegralProvider), passed to
// MolecularHamiltonian.buildFromIntegrals.
//
// Quantum Advantage:
// Time evolution of molecular Hamiltonians is exponentially hard classically
// (Hilbert space grows as 2^n). Quantum simulation provides natural encoding
// and efficient evolution via Trotter-Suzuki decomposition.
//
// Usage:
//   dotnet fsi HamiltonianTimeEvolution.fsx
//   dotnet fsi HamiltonianTimeEvolution.fsx -- --time 5.0 --steps 40
//   dotnet fsi HamiltonianTimeEvolution.fsx -- --backend local
//   dotnet fsi HamiltonianTimeEvolution.fsx -- --backend topological
//   dotnet fsi HamiltonianTimeEvolution.fsx -- --output results.json --csv results.csv
//   dotnet fsi HamiltonianTimeEvolution.fsx -- --quiet --output results.json
//   dotnet fsi HamiltonianTimeEvolution.fsx -- --svg      (animated picture)
//
// ==============================================================================

#r "nuget: Microsoft.Extensions.Logging.Abstractions, 10.0.0"
// The library comes from NuGet; `dotnet fsi --define:LOCAL_BUILD <script>` uses the repo's Debug build.
#if LOCAL_BUILD
#r "../../src/FSharp.Azure.Quantum/bin/Debug/net10.0/FSharp.Azure.Quantum.dll"
#r "../../src/FSharp.Azure.Quantum.Topological/bin/Debug/net10.0/FSharp.Azure.Quantum.Topological.dll"
#else
#r "nuget: FSharp.Azure.Quantum"
#r "nuget: FSharp.Azure.Quantum.Topological"
#endif
#load "../_common/Cli.fs"
#load "../_common/Data.fs"
#load "../_common/Reporting.fs"
#load "../_common/SvgAnimation.fs"

open FSharp.Azure.Quantum.QuantumChemistry
open FSharp.Azure.Quantum.LocalSimulator
open FSharp.Azure.Quantum.Core
open FSharp.Azure.Quantum.Core.BackendAbstraction
open FSharp.Azure.Quantum.Backends
open FSharp.Azure.Quantum.Topological
open FSharp.Azure.Quantum.Examples.Common
open System

// ==============================================================================
// CLI ARGUMENT PARSING
// ==============================================================================

let argv = fsi.CommandLineArgs |> Array.skip 1
let args = Cli.parse argv

Cli.exitIfHelp
    "HamiltonianTimeEvolution.fsx"
    "Trotter-Suzuki time evolution for molecular Hamiltonians on unified backends."
    [
        {
            Cli.OptionSpec.Name = "bond-length"
            Description = "H2 bond length in Angstroms (the bundled integrals are for 0.7414 only)"
            Default = Some "0.7414"
        }
        {
            Cli.OptionSpec.Name = "time"
            Description = "Evolution time in atomic units"
            Default = Some "10.0"
        }
        {
            Cli.OptionSpec.Name = "steps"
            Description = "Number of Trotter steps"
            Default = Some "20"
        }
        {
            Cli.OptionSpec.Name = "order"
            Description = "Trotter order (1 or 2)"
            Default = Some "2"
        }
        {
            Cli.OptionSpec.Name = "backend"
            Description = "Backend: local, topological, or both"
            Default = Some "both"
        }
        {
            Cli.OptionSpec.Name = "output"
            Description = "Write results to JSON file"
            Default = None
        }
        {
            Cli.OptionSpec.Name = "csv"
            Description = "Write results to CSV file"
            Default = None
        }
        {
            Cli.OptionSpec.Name = "quiet"
            Description = "Suppress informational output"
            Default = None
        }
        {
            Cli.OptionSpec.Name = "svg"
            Description = "Draw an animated SVG (default path: _images/hamiltonian-time-evolution.svg)"
            Default = None
        }
    ]
    args

let quiet = Cli.hasFlag "quiet" args
let integrals = MolecularHamiltonian.h2Sto3gIntegrals

[<Literal>]
let integralsBondLength = 0.7414

let bondLength = Cli.getFloatOr "bond-length" integralsBondLength args
let evolutionTime = Cli.getFloatOr "time" 10.0 args
let trotterSteps = Cli.getIntOr "steps" 20 args
let trotterOrder = Cli.getIntOr "order" 2 args
let backendArg = Cli.getOr "backend" "both" args

if abs (bondLength - integralsBondLength) > 1e-9 then
    eprintfn
        "The bundled H2 integrals are for R = %.4f A only; --bond-length %g has no integrals here."
        integralsBondLength
        bondLength

    eprintfn "Other bond lengths need MolecularIntegrals from PySCF, Psi4 or an FCIDUMP file (for example"
    eprintfn "through an IntegralProvider), passed to MolecularHamiltonian.buildFromIntegrals."
    exit 1

let runLocal =
    String.Equals(backendArg, "local", StringComparison.OrdinalIgnoreCase)
    || String.Equals(backendArg, "both", StringComparison.OrdinalIgnoreCase)

let runTopo =
    String.Equals(backendArg, "topological", StringComparison.OrdinalIgnoreCase)
    || String.Equals(backendArg, "both", StringComparison.OrdinalIgnoreCase)

// ==============================================================================
// UNIFIED STATE ANALYSIS HELPERS
// ==============================================================================

/// Analyze any QuantumState using the unified API (backend-agnostic)
let analyzeState (state: QuantumState) (numQubits: int) (label: string) =
    if not quiet then
        printfn "  %s:" label
        printfn "    State type: %A" (QuantumState.stateType state)
        printfn "    Num qubits: %d" (QuantumState.numQubits state)
        printfn "    Normalized: %b" (QuantumState.isNormalized state)

    // Sample measurements using unified API (works with any state type)
    let measurements = QuantumState.measure state 1000

    // Count measurement outcomes
    let counts =
        measurements
        |> Array.groupBy id
        |> Array.map (fun (bits, occurrences) ->
            let bitstring = bits |> Array.map string |> String.concat ""
            (bitstring, occurrences.Length))
        |> Array.sortByDescending snd

    if not quiet then
        printfn "    Top measurement outcomes (1000 shots):"

        counts
        |> Array.truncate 4
        |> Array.iter (fun (bitstring, count) ->
            let prob = float count / 1000.0
            printfn "      |%s>: %d (%.1f%%)" bitstring count (prob * 100.0))

    counts

/// Qubit values of a basis index: bit q of the index is qubit q.
let bitsOf (numQubits: int) (basisIndex: int) =
    [| for i in 0 .. numQubits - 1 -> (basisIndex >>> i) &&& 1 |]

/// A basis index as a bitstring, qubit 0 first.
let bitstringOf (numQubits: int) (basisIndex: int) =
    bitsOf numQubits basisIndex |> Array.map string |> String.concat ""

/// Get probability of specific basis state (works with any QuantumState)
let getBasisProbability (state: QuantumState) (basisIndex: int) (numQubits: int) =
    QuantumState.probability (bitsOf numQubits basisIndex) state

// ==============================================================================
// EXACT EVOLUTION (dense state vector, for checking the Trotter results)
// ==============================================================================

/// H|psi> on a dense state vector; bit q of the index is qubit q.
let applyPauliSum (hamiltonian: QaoaCircuit.ProblemHamiltonian) (psi: Numerics.Complex[]) =
    let out = Array.create psi.Length Numerics.Complex.Zero

    for term in hamiltonian.Terms do
        for b in 0 .. psi.Length - 1 do
            let mutable target = b
            let mutable factor = Numerics.Complex(term.Coefficient, 0.0)

            for q, op in Array.zip term.QubitsIndices term.PauliOperators do
                let bit = (b >>> q) &&& 1

                match op with
                | QaoaCircuit.PauliI -> ()
                | QaoaCircuit.PauliX -> target <- target ^^^ (1 <<< q)
                | QaoaCircuit.PauliY ->
                    target <- target ^^^ (1 <<< q)
                    factor <- factor * Numerics.Complex(0.0, (if bit = 0 then 1.0 else -1.0))
                | QaoaCircuit.PauliZ ->
                    if bit = 1 then
                        factor <- -factor

            out.[target] <- out.[target] + factor * psi.[b]

    out

/// exp(-iHt)|psi>, exact to machine precision: the Taylor series converges
/// fast on slices where the sum of |coefficients| times the slice is <= 0.25.
let evolveExactly (hamiltonian: QaoaCircuit.ProblemHamiltonian) (t: float) (psi: Numerics.Complex[]) =
    let bound = hamiltonian.Terms |> Array.sumBy (fun term -> abs term.Coefficient)
    let slices = max 1 (int (ceil (abs t * bound / 0.25)))
    let dt = t / float slices
    let mutable state = psi

    for _ in 1..slices do
        let next = Array.copy state
        let mutable term = state
        let mutable k = 1

        while k <= 40 && (term |> Array.sumBy (fun a -> a.Magnitude)) > 1e-18 do
            let factor = Numerics.Complex(0.0, -dt / float k)
            term <- applyPauliSum hamiltonian term |> Array.map (fun a -> a * factor)

            for i in 0 .. next.Length - 1 do
                next.[i] <- next.[i] + term.[i]

            k <- k + 1

        state <- next

    state

/// The basis state |index> as a dense vector.
let basisVector (dim: int) (index: int) =
    Array.init dim (fun i ->
        if i = index then
            Numerics.Complex.One
        else
            Numerics.Complex.Zero)

/// Probabilities of a dense state vector.
let probabilitiesOf (psi: Numerics.Complex[]) =
    psi |> Array.map (fun a -> a.Magnitude * a.Magnitude)

/// 1 - |<exact|state>|^2 (blind to global phase), for state-vector states only.
let stateError (exact: Numerics.Complex[]) (state: QuantumState) =
    match state with
    | QuantumState.StateVector sv ->
        let overlap =
            exact
            |> Array.mapi (fun i a -> Numerics.Complex.Conjugate a * StateVector.getAmplitude i sv)
            |> Array.fold (+) Numerics.Complex.Zero

        Some(max 0.0 (1.0 - overlap.Magnitude * overlap.Magnitude))
    | _ -> None

/// A small number as "2.2e-16" (no padded exponent).
let scientific (v: float) =
    if v = 0.0 then
        "0"
    else
        let e = int (floor (log10 (abs v)))
        let m = v / (10.0 ** float e)

        if abs m >= 9.95 then
            sprintf "%.1fe%d" (m / 10.0) (e + 1)
        else
            $"%.1f{m}e%d{e}"

// ==============================================================================
// MOLECULE AND HAMILTONIAN SETUP
// ==============================================================================

if not quiet then
    printfn "=================================================================="
    printfn "   Hamiltonian Time Evolution Simulation"
    printfn "=================================================================="
    printfn ""
    printfn "Parameters:"
    printfn "  Bond length: %.4f A (bundled STO-3G integrals)" bondLength
    printfn "  Evolution time: %.1f a.u." evolutionTime
    printfn "  Trotter steps: %d" trotterSteps
    printfn "  Trotter order: %d" trotterOrder
    printfn "  Backends: %s" backendArg
    printfn ""

let hamiltonianResult =
    MolecularHamiltonian.buildFromIntegrals integrals MolecularHamiltonian.JordanWigner

/// Hartree-Fock: the lowest spin orbitals filled, one qubit per electron.
let hartreeFockIndex = (1 <<< integrals.NumElectrons) - 1

/// Every electron moved up by one spatial orbital: for H2, both into the upper orbital.
let doubleExcitationIndex = hartreeFockIndex <<< integrals.NumElectrons

/// Mutable list to collect results across backends
let mutable allBackendResults: Map<string, string> list = []

match hamiltonianResult with
| Error err ->
    if not quiet then
        printfn "Hamiltonian construction failed: %A" err
| Ok(hamiltonian, nuclearRepulsion) ->
    let numQubits = hamiltonian.NumQubits
    let dim = 1 <<< numQubits
    let hfVector = basisVector dim hartreeFockIndex
    let hfLabel = bitstringOf numQubits hartreeFockIndex
    let doubleLabel = bitstringOf numQubits doubleExcitationIndex

    let hfEnergy =
        (applyPauliSum hamiltonian hfVector).[hartreeFockIndex].Real + nuclearRepulsion

    let exactFinal = evolveExactly hamiltonian evolutionTime hfVector |> probabilitiesOf

    if not quiet then
        printfn "Molecular Hamiltonian constructed (H2, STO-3G integrals, Jordan-Wigner)"
        printfn "  Qubits: %d (one per spin orbital)" numQubits
        printfn "  Pauli terms: %d" hamiltonian.Terms.Length
        printfn "  Nuclear repulsion: %.6f Ha" nuclearRepulsion
        printfn "  Hartree-Fock energy <HF|H|HF> + nuclear repulsion: %.6f Ha" hfEnergy
        printfn ""
        printfn "  Initial state: Hartree-Fock |%s> (both electrons in the lower orbital)" hfLabel
        printfn "  It mixes with |%s> (both electrons in the upper orbital) over time." doubleLabel
        printfn ""
        printfn "  Exact evolution at t = %.1f a.u.:" evolutionTime
        printfn "    P(|%s>): %.6f" hfLabel exactFinal.[hartreeFockIndex]
        printfn "    P(|%s>): %.6f" doubleLabel exactFinal.[doubleExcitationIndex]
        printfn ""

    // ==================================================================
    // Run simulation on a given backend and return structured result
    // ==================================================================

    let runSimulation (backendLabel: string) (backend: IQuantumBackend) : Map<string, string> =
        if not quiet then
            printfn "=================================================================="
            printfn "   %s" backendLabel
            printfn "=================================================================="
            printfn ""
            printfn "  Backend: %s" backend.Name
            printfn "  Native state type: %A" backend.NativeStateType
            printfn ""

        let initialState =
            match FermionMapping.HartreeFock.prepareHartreeFockState integrals.NumElectrons numQubits backend with
            | Ok state -> state
            | Error err -> failwithf "Failed to prepare the Hartree-Fock state: %A" err

        if not quiet then
            printfn "  Initial state: Hartree-Fock |%s>" hfLabel
            analyzeState initialState numQubits "Initial" |> ignore
            printfn ""

        let config =
            {
                HamiltonianSimulation.SimulationConfig.Time = evolutionTime
                HamiltonianSimulation.SimulationConfig.TrotterSteps = trotterSteps
                HamiltonianSimulation.SimulationConfig.TrotterOrder = trotterOrder
                HamiltonianSimulation.SimulationConfig.Backend = Some backend
            }

        if not quiet then
            printfn "  Simulation config:"
            printfn "    Evolution time: %.1f a.u." config.Time
            printfn "    Trotter steps: %d" config.TrotterSteps
            printfn "    Trotter order: %d" config.TrotterOrder
            printfn ""
            printfn "  Running exp(-iHt)|HF>..."

        let result = HamiltonianSimulation.simulate hamiltonian initialState config

        let common =
            [
                "backend", backendLabel
                "backend_name", backend.Name
                "bond_length_A", $"%.4f{bondLength}"
                "time_au", $"%.1f{evolutionTime}"
                "trotter_steps", $"%d{trotterSteps}"
                "trotter_order", $"%d{trotterOrder}"
                "num_qubits", $"%d{numQubits}"
                "num_terms", $"%d{hamiltonian.Terms.Length}"
                "exact_hf_prob", $"%.6f{exactFinal.[hartreeFockIndex]}"
            ]

        match result with
        | Error err ->
            if not quiet then
                printfn "  Simulation failed: %A" err
                printfn ""

            Map.ofList (
                common
                @ [
                    "hf_prob", "N/A"
                    "double_excitation_prob", "N/A"
                    "normalized", "N/A"
                    "status", $"Error: %A{err}"
                ]
            )
        | Ok finalState ->
            let pHf = getBasisProbability finalState hartreeFockIndex numQubits
            let pDouble = getBasisProbability finalState doubleExcitationIndex numQubits
            let normalized = QuantumState.isNormalized finalState

            if not quiet then
                printfn "  Simulation complete"
                analyzeState finalState numQubits "Final" |> ignore
                printfn ""
                printfn "  P(|%s>): %.6f  (exact %.6f)" hfLabel pHf exactFinal.[hartreeFockIndex]
                printfn "  P(|%s>): %.6f  (exact %.6f)" doubleLabel pDouble exactFinal.[doubleExcitationIndex]
                printfn "  Normalized: %b" normalized
                printfn ""

            Map.ofList (
                common
                @ [
                    "hf_prob", $"%.6f{pHf}"
                    "double_excitation_prob", $"%.6f{pDouble}"
                    "normalized", $"%b{normalized}"
                    "status", "OK"
                ]
            )

    // ==================================================================
    // BACKEND 1: LocalBackend
    // ==================================================================

    if runLocal then
        let localBackend = LocalBackend.LocalBackend() :> IQuantumBackend
        let result = runSimulation "LocalBackend (Gate-based StateVector)" localBackend
        allBackendResults <- allBackendResults @ [ result ]

    // ==================================================================
    // BACKEND 2: TopologicalUnifiedBackend
    // ==================================================================

    if runTopo then
        // Ising anyons encode n logical qubits in 2n + 2 anyons.
        let numAnyons = 2 * numQubits + 2
        let topoBackend = TopologicalUnifiedBackendFactory.createIsing numAnyons

        if not quiet then
            printfn "  Anyon type: Ising"
            printfn "  Max anyons: %d" numAnyons
            printfn ""

        let result = runSimulation "TopologicalBackend (Braiding-based)" topoBackend
        allBackendResults <- allBackendResults @ [ result ]

    // ==================================================================
    // TROTTER ORDER COMPARISON (LocalBackend only)
    // ==================================================================

    if runLocal then
        if not quiet then
            printfn "=================================================================="
            printfn "   Trotter Order Comparison (against exact evolution)"
            printfn "=================================================================="
            printfn ""

        let localBackend = LocalBackend.LocalBackend() :> IQuantumBackend

        let localInitialState =
            match FermionMapping.HartreeFock.prepareHartreeFockState integrals.NumElectrons numQubits localBackend with
            | Ok state -> state
            | Error err -> failwithf "Failed to prepare the Hartree-Fock state: %A" err

        let exactState = evolveExactly hamiltonian evolutionTime hfVector

        let config1st =
            {
                HamiltonianSimulation.SimulationConfig.Time = evolutionTime
                HamiltonianSimulation.SimulationConfig.TrotterSteps = trotterSteps
                HamiltonianSimulation.SimulationConfig.TrotterOrder = 1
                HamiltonianSimulation.SimulationConfig.Backend = Some localBackend
            }

        let config2nd = { config1st with TrotterOrder = 2 }

        let result1st =
            HamiltonianSimulation.simulate hamiltonian localInitialState config1st

        let result2nd =
            HamiltonianSimulation.simulate hamiltonian localInitialState config2nd

        match result1st, result2nd with
        | Ok state1st, Ok state2nd ->
            let populationGap (state: QuantumState) =
                Array.init dim (fun b -> abs (getBasisProbability state b numQubits - exactFinal.[b]))
                |> Array.max

            let describe (order: int) (state: QuantumState) =
                let gap = populationGap state
                let error = stateError exactState state |> Option.defaultValue nan

                if not quiet then
                    printfn "  Order %d Trotter:" order
                    printfn "    Normalized: %b" (QuantumState.isNormalized state)

                    printfn
                        "    P(|%s>): %.6f  (exact %.6f)"
                        hfLabel
                        (getBasisProbability state hartreeFockIndex numQubits)
                        exactFinal.[hartreeFockIndex]

                    printfn "    Largest population gap to exact: %s" (scientific gap)
                    printfn "    State error 1 - |<exact|trotter>|^2: %s" (scientific error)
                    printfn ""

                Map.ofList
                    [
                        "backend", $"Trotter-order-%d{order}"
                        "backend_name", localBackend.Name
                        "bond_length_A", $"%.4f{bondLength}"
                        "time_au", $"%.1f{evolutionTime}"
                        "trotter_steps", $"%d{trotterSteps}"
                        "trotter_order", $"%d{order}"
                        "num_qubits", $"%d{numQubits}"
                        "num_terms", $"%d{hamiltonian.Terms.Length}"
                        "hf_prob", $"%.6f{getBasisProbability state hartreeFockIndex numQubits}"
                        "double_excitation_prob", $"%.6f{getBasisProbability state doubleExcitationIndex numQubits}"
                        "exact_hf_prob", $"%.6f{exactFinal.[hartreeFockIndex]}"
                        "population_gap", scientific gap
                        "state_error", scientific error
                        "normalized", sprintf "%b" (QuantumState.isNormalized state)
                        "status", "OK"
                    ]

            let row1st = describe 1 state1st
            let row2nd = describe 2 state2nd

            if not quiet && abs (populationGap state1st - populationGap state2nd) < 1e-12 then
                printfn "  Both orders give the same populations here; the state error tells them apart."
                printfn ""

            allBackendResults <- allBackendResults @ [ row1st; row2nd ]
        | _ ->
            if not quiet then
                printfn "  Trotter comparison failed"
                printfn ""

    // ==================================================================
    // UNIFIED ARCHITECTURE SUMMARY
    // ==================================================================

    if not quiet then
        printfn "=================================================================="
        printfn "   Unified Backend Architecture Benefits"
        printfn "=================================================================="
        printfn ""
        printfn "The HamiltonianSimulation.simulate function:"
        printfn ""
        printfn "  1. Accepts ANY IQuantumBackend implementation"
        printfn "  2. Works with ANY QuantumState variant:"
        printfn "     - StateVector (gate-based)"
        printfn "     - FusionSuperposition (topological)"
        printfn "     - SparseState (Clifford simulation)"
        printfn "     - DensityMatrix (noisy simulation)"
        printfn ""
        printfn "  3. Uses UnifiedBackend.applySequence which:"
        printfn "     - Automatically converts state types if needed"
        printfn "     - Dispatches operations to backend.ApplyOperation"
        printfn "     - Handles errors uniformly across backends"
        printfn ""
        printfn "  4. Enables backend-agnostic algorithm development:"
        printfn "     - Write once, run on any quantum hardware model"
        printfn "     - TopologicalBackend compiles gates -> braids transparently"
        printfn "     - Future backends (trapped ion, photonic) plug in seamlessly"
        printfn ""

    // ==================================================================
    // APPLICATIONS
    // ==================================================================

    if not quiet then
        printfn "=================================================================="
        printfn "   Applications"
        printfn "=================================================================="
        printfn ""
        printfn "  - Molecular dynamics simulation"
        printfn "  - Chemical reaction pathways"
        printfn "  - Adiabatic state preparation for VQE"
        printfn "  - Quantum annealing simulation"
        printfn ""

// ==============================================================================
// STRUCTURED OUTPUT
// ==============================================================================

let allResults: Map<string, obj> =
    Map.ofList
        [
            "script", box "HamiltonianTimeEvolution.fsx"
            "bond_length_A", box bondLength
            "evolution_time_au", box evolutionTime
            "trotter_steps", box trotterSteps
            "trotter_order", box trotterOrder
            "backend_arg", box backendArg
            "backend_results", box allBackendResults
        ]

Cli.tryGet "output" args
|> Option.iter (fun path ->
    Reporting.writeJson path allResults

    if not quiet then
        printfn "Results written to %s" path)

match Cli.tryGet "csv" args with
| Some path ->
    let header =
        [
            "backend"
            "backend_name"
            "bond_length_A"
            "time_au"
            "trotter_steps"
            "trotter_order"
            "num_qubits"
            "num_terms"
            "hf_prob"
            "double_excitation_prob"
            "exact_hf_prob"
            "population_gap"
            "state_error"
            "normalized"
            "status"
        ]

    let rows =
        allBackendResults
        |> List.map (fun m -> header |> List.map (fun h -> m |> Map.tryFind h |> Option.defaultValue ""))

    Reporting.writeCsv path header rows

    if not quiet then
        printfn "Results written to %s" path
| None -> ()

// ==============================================================================
// ANIMATED PICTURE (--svg [path])
// ==============================================================================
// Every Trotter point is HamiltonianSimulation.simulate on the LocalBackend from
// the Hartree-Fock state, with this run's time step; the exact curve and the
// state error come from exp(-iHt) applied to the 16-entry state vector above.
// Determinants drawn: every one whose population passes 0.1% at some point.

open SvgAnimation

/// Attoseconds in one atomic unit of time.
[<Literal>]
let attosecondsPerAu = 24.188843

/// A tick spacing of 1, 2 or 5 times a power of ten, giving about `count` ticks.
let niceStep (range: float) (count: int) =
    let raw = range / float count
    let magnitude = 10.0 ** floor (log10 raw)

    [ 1.0; 2.0; 5.0; 10.0 ]
    |> List.map (fun f -> f * magnitude)
    |> List.find (fun s -> s >= raw)

let drawEvolution (path: string) (hamiltonian: QaoaCircuit.ProblemHamiltonian) (nuclearRepulsion: float) =
    let n = hamiltonian.NumQubits
    let dim = 1 <<< n
    let backend = LocalBackend.LocalBackend() :> IQuantumBackend

    let start =
        match FermionMapping.HartreeFock.prepareHartreeFockState integrals.NumElectrons n backend with
        | Ok state -> state
        | Error err -> failwithf "Failed to prepare the Hartree-Fock state: %A" err

    // At most 40 animated steps; with more Trotter steps, frames skip evenly.
    let shown = min trotterSteps 40
    let frames = shown + 1

    let stepAt k =
        int (Math.Round(float k * float trotterSteps / float shown))

    let dt = evolutionTime / float trotterSteps
    let timeAt k = float (stepAt k) * dt

    let trotter order k =
        if stepAt k = 0 then
            start
        else
            let config =
                {
                    HamiltonianSimulation.SimulationConfig.Time = timeAt k
                    HamiltonianSimulation.SimulationConfig.TrotterSteps = stepAt k
                    HamiltonianSimulation.SimulationConfig.TrotterOrder = order
                    HamiltonianSimulation.SimulationConfig.Backend = Some backend
                }

            match HamiltonianSimulation.simulate hamiltonian start config with
            | Ok state -> state
            | Error err -> failwithf "Trotter evolution failed: %A" err

    let populations (state: QuantumState) =
        Array.init dim (fun b -> getBasisProbability state b n)

    let firstStates = Array.init frames (trotter 1)
    let secondStates = Array.init frames (trotter 2)

    let chosen =
        (if trotterOrder = 1 then firstStates else secondStates)
        |> Array.map populations

    let hfVector = basisVector dim hartreeFockIndex

    let exactStates =
        Array.init frames (fun k -> evolveExactly hamiltonian (timeAt k) hfVector)

    let exactFrames = exactStates |> Array.map probabilitiesOf
    let samples = 240
    let sampleTime i = evolutionTime * float i / float samples

    let exactCurve =
        Array.init (samples + 1) (fun i -> evolveExactly hamiltonian (sampleTime i) hfVector |> probabilitiesOf)

    let errors (states: QuantumState[]) =
        Array.init frames (fun k -> stateError exactStates.[k] states.[k] |> Option.defaultValue 0.0)

    let firstErrors = errors firstStates
    let secondErrors = errors secondStates

    let populationGap k =
        Array.map2 (fun a b -> abs (a - b)) chosen.[k] exactFrames.[k] |> Array.max

    let determinants =
        [ 0 .. dim - 1 ]
        |> List.filter (fun b ->
            let peak =
                Seq.append (exactCurve |> Seq.map (fun p -> p.[b])) (chosen |> Seq.map (fun p -> p.[b]))
                |> Seq.max

            b = hartreeFockIndex || peak > 1e-3)
        |> List.sortBy (fun b -> if b = hartreeFockIndex then -1 else b)

    let movers = determinants |> List.filter ((<>) hartreeFockIndex)

    let describe b =
        if b = hartreeFockIndex then "Hartree-Fock"
        elif b = doubleExcitationIndex then "double excitation"
        else "other"

    // Layout: two charts sharing the time axis on the left, bars on the right.
    let width, height = 760.0, 560.0
    let x0, x1 = 64.0, 492.0
    let yTop, yBottom = 112.0, 262.0
    let eTop, eBottom = 300.0, 388.0
    let tx t = x0 + (x1 - x0) * t / evolutionTime

    let peak =
        movers
        |> List.collect (fun b -> [ for p in exactCurve -> p.[b] ] @ [ for p in chosen -> p.[b] ])
        |> List.fold max 1e-3

    let pStep = niceStep peak 4
    let pMax = ceil (peak / pStep - 1e-9) * pStep
    let py p = yBottom - (yBottom - yTop) * p / pMax

    let positiveErrors =
        Array.append firstErrors secondErrors |> Array.filter (fun e -> e > 0.0)

    let decadeLow, decadeHigh =
        if positiveErrors.Length = 0 then
            -16.0, -15.0
        else
            floor (log10 (Array.min positiveErrors)), ceil (log10 (Array.max positiveErrors))

    let ey (e: float) =
        let d = if e > 0.0 then max decadeLow (log10 e) else decadeLow
        eBottom - (eBottom - eTop) * (d - decadeLow) / (decadeHigh - decadeLow)

    let title = "H2 time evolution: Trotter steps against exact evolution"
    let pic = Picture(width, height, frames, 18.0, title, hold = 0.1)
    pic.Text(24.0, 30.0, title, size = 18.0, bold = true)

    pic.Text(
        24.0,
        50.0,
        "Both electrons start in the lower orbital; a few percent moves, both together, to the upper one and back.",
        size = 12.5,
        fill = grey
    )

    let percent (p: float) =
        sprintf "%g%%" (Math.Round(p * 100.0, 3))

    // Upper chart: populations of the determinants that move.
    let moverNames =
        movers
        |> List.map (fun b -> sprintf "|%s> (%s)" (bitstringOf n b) (describe b))
        |> String.concat ", "

    pic.Text(x0, yTop - 8.0, $"Population of %s{moverNames}", size = 12.0, fill = grey)
    pic.Rect(x0, yTop, x1 - x0, yBottom - yTop, fill = panel, stroke = frameColour)

    for i in 0 .. int (Math.Round(pMax / pStep)) do
        let p = float i * pStep
        pic.Line(x0, py p, x1, py p, stroke = light)
        pic.Text(x0 - 6.0, py p + 4.0, percent p, size = 11.0, fill = grey, anchor = "end")

    pic.Text(
        18.0,
        (yTop + yBottom) / 2.0,
        "population",
        size = 12.0,
        anchor = "middle",
        transform = sprintf "rotate(-90 18 %s)" (num ((yTop + yBottom) / 2.0))
    )

    for b in movers do
        let d =
            exactCurve
            |> Array.mapi (fun i p ->
                sprintf "%s%s %s" (if i = 0 then "M" else "L") (num (tx (sampleTime i))) (num (py p.[b])))
            |> String.concat " "

        pic.Path(d, stroke = colour 1, width = 2.0, opacity = 0.55)

        for k in 0 .. frames - 1 do
            pic.Circle(tx (timeAt k), py chosen.[k].[b], 2.2, fill = colour 1, opacity = 0.8)

        pic.Circle(
            tx 0.0,
            py chosen.[0].[b],
            5.5,
            fill = colour 1,
            stroke = "white",
            width = 1.5,
            animate =
                [
                    "cx", Array.init frames (timeAt >> tx)
                    "cy", Array.init frames (fun k -> py chosen.[k].[b])
                ]
        )

    // Lower chart: how far each Trotter order's state is from the exact state.
    pic.Text(x0, eTop - 8.0, "Trotter state error, 1 - |<exact|trotter>|², log scale", size = 12.0, fill = grey)
    pic.Rect(x0, eTop, x1 - x0, eBottom - eTop, fill = panel, stroke = frameColour)

    for d in int decadeLow .. int decadeHigh do
        let e = 10.0 ** float d
        pic.Line(x0, ey e, x1, ey e, stroke = light)
        pic.Text(x0 - 6.0, ey e + 4.0, $"1e%d{d}", size = 11.0, fill = grey, anchor = "end")

    pic.Text(
        18.0,
        (eTop + eBottom) / 2.0,
        "state error",
        size = 12.0,
        anchor = "middle",
        transform = sprintf "rotate(-90 18 %s)" (num ((eTop + eBottom) / 2.0))
    )

    for order, series, c in [ 1, firstErrors, colour 4; 2, secondErrors, colour 2 ] do
        let d =
            [ 1 .. frames - 1 ]
            |> List.mapi (fun i k ->
                sprintf "%s%s %s" (if i = 0 then "M" else "L") (num (tx (timeAt k))) (num (ey series.[k])))
            |> String.concat " "

        pic.Path(d, stroke = c, width = 1.5, opacity = 0.7)

        pic.Circle(
            tx 0.0,
            ey series.[0],
            4.5,
            fill = c,
            stroke = "white",
            width = 1.2,
            animate =
                [
                    "cx", Array.init frames (timeAt >> tx)
                    "cy", Array.init frames (fun k -> ey series.[k])
                ]
        )

        pic.Text(x1 + 6.0, ey series.[frames - 1] + 4.0, $"order %d{order}", size = 11.5, fill = c, bold = true)

    // Shared time axis and cursor.
    let tStep = niceStep evolutionTime 5

    for i in 0 .. int (floor (evolutionTime / tStep + 1e-9)) do
        let t = float i * tStep
        pic.Line(tx t, yBottom, tx t, yBottom + 4.0, stroke = grey)
        pic.Line(tx t, eBottom, tx t, eBottom + 4.0, stroke = grey)
        pic.Text(tx t, eBottom + 17.0, sprintf "%g" (Math.Round(t, 6)), size = 11.0, fill = grey, anchor = "middle")

    pic.Text(
        (x0 + x1) / 2.0,
        eBottom + 36.0,
        $"time (atomic units; 1 a.u. = %.1f{attosecondsPerAu} attoseconds)",
        size = 12.0,
        anchor = "middle"
    )

    for top, bottom in [ yTop, yBottom; eTop, eBottom ] do
        pic.Line(
            tx 0.0,
            top,
            tx 0.0,
            bottom,
            stroke = ink,
            dash = "3 3",
            animate =
                [
                    "x1", Array.init frames (timeAt >> tx)
                    "x2", Array.init frames (timeAt >> tx)
                ]
        )

    pic.FrameText(
        x0,
        84.0,
        Array.init frames (fun k ->
            sprintf
                "t = %.2f a.u. (%.0f attoseconds), Trotter step %d of %d"
                (timeAt k)
                (timeAt k * attosecondsPerAu)
                (stepAt k)
                trotterSteps),
        size = 12.5,
        bold = true
    )

    // Bars: every drawn determinant at the cursor, a black tick at the exact value.
    let barLeft, barRight = 548.0, 740.0
    let slot = (barRight - barLeft) / float determinants.Length
    let barWidth = min 44.0 (slot * 0.6)
    let bp p = eBottom - (eBottom - yTop) * p

    pic.Text((barLeft + barRight) / 2.0, 84.0, "at the cursor", size = 12.5, bold = true, anchor = "middle")
    pic.Line(barLeft, eBottom, barRight, eBottom, stroke = grey)

    for i, b in List.indexed determinants do
        let bx = barLeft + slot * float i + (slot - barWidth) / 2.0
        let centre = bx + barWidth / 2.0
        let c = if b = hartreeFockIndex then colour 0 else colour 1

        pic.Rect(
            bx,
            bp chosen.[0].[b],
            barWidth,
            eBottom - bp chosen.[0].[b],
            fill = c,
            animate =
                [
                    "y", Array.init frames (fun k -> bp chosen.[k].[b])
                    "height", Array.init frames (fun k -> eBottom - bp chosen.[k].[b])
                ]
        )

        pic.Line(
            bx - 4.0,
            bp exactFrames.[0].[b],
            bx + barWidth + 4.0,
            bp exactFrames.[0].[b],
            stroke = ink,
            width = 2.0,
            animate =
                [
                    "y1", Array.init frames (fun k -> bp exactFrames.[k].[b])
                    "y2", Array.init frames (fun k -> bp exactFrames.[k].[b])
                ]
        )

        pic.FrameText(
            centre,
            yTop - 8.0,
            Array.init frames (fun k -> sprintf "%.1f%%" (chosen.[k].[b] * 100.0)),
            size = 12.0,
            fill = c,
            bold = true,
            anchor = "middle"
        )

        pic.Text(centre, eBottom + 17.0, sprintf "|%s>" (bitstringOf n b), size = 12.0, anchor = "middle")
        pic.Text(centre, eBottom + 32.0, describe b, size = 11.0, fill = grey, anchor = "middle")

    // Legend, error per frame, the Hamiltonian.
    let legendY = eBottom + 64.0
    pic.Line(x0, legendY - 4.0, x0 + 22.0, legendY - 4.0, stroke = colour 1, width = 2.0, opacity = 0.55)
    pic.Text(x0 + 28.0, legendY, "exact exp(-iHt)", size = 12.0)
    pic.Circle(x0 + 142.0, legendY - 4.0, 4.5, fill = colour 1)

    pic.Text(
        x0 + 152.0,
        legendY,
        sprintf "Trotter, order %d, step %g a.u., LocalBackend" trotterOrder (Math.Round(dt, 6)),
        size = 12.0
    )

    pic.Line(barLeft + 20.0, legendY - 4.0, barLeft + 42.0, legendY - 4.0, stroke = ink, width = 2.0)
    pic.Text(barLeft + 48.0, legendY, "exact value", size = 12.0)

    pic.FrameText(
        x0,
        legendY + 22.0,
        Array.init frames (fun k ->
            if stepAt k = 0 then
                "At t = 0 the Trotter state is the exact state."
            else
                sprintf
                    "At this step: population gap to exact %s; state error, order 1 %s, order 2 %s."
                    (scientific (populationGap k))
                    (scientific firstErrors.[k])
                    (scientific secondErrors.[k])),
        size = 12.0
    )

    pic.Text(
        x0,
        legendY + 44.0,
        sprintf
            "H2, STO-3G integrals at %.4f Å, Jordan-Wigner: %d qubits, %d Pauli terms; nuclear repulsion %.4f Ha."
            integralsBondLength
            n
            hamiltonian.Terms.Length
            nuclearRepulsion,
        size = 12.0,
        fill = grey
    )

    let samePopulations =
        Array.init frames (fun k ->
            Array.map2 (fun a b -> abs (a - b)) (populations firstStates.[k]) (populations secondStates.[k])
            |> Array.max)
        |> Array.max
            <
            1e-12

    if samePopulations then
        pic.Text(
            x0,
            legendY + 62.0,
            "Both orders give the same populations here; only the state error tells them apart.",
            size = 12.0,
            fill = grey
        )

    pic.Progress(x0, height - 14.0, barRight - x0)
    pic.Save(path, quiet = quiet)

    if not quiet then
        printfn
            "Picture: largest population gap to exact %s; largest state error, order 1 %s, order 2 %s"
            (scientific (Array.init frames populationGap |> Array.max))
            (scientific (Array.max firstErrors))
            (scientific (Array.max secondErrors))

match
    svgPath (IO.Path.Combine(__SOURCE_DIRECTORY__, "_images", "hamiltonian-time-evolution.svg")), hamiltonianResult
with
| Some _, Ok _ when trotterSteps < 1 || evolutionTime <= 0.0 ->
    eprintfn "No picture drawn: it needs --steps 1 or more and a --time above 0."
| Some path, Ok(hamiltonian, nuclearRepulsion) -> drawEvolution path hamiltonian nuclearRepulsion
| Some _, Error err -> eprintfn "No picture drawn: %A" err
| None, _ -> ()

// ==============================================================================
// USAGE HINTS
// ==============================================================================

if argv.Length = 0 && not quiet then
    printfn "Tip: Customize this example with command-line options:"
    printfn "  dotnet fsi HamiltonianTimeEvolution.fsx -- --time 5.0 --steps 40"
    printfn "  dotnet fsi HamiltonianTimeEvolution.fsx -- --backend local"
    printfn "  dotnet fsi HamiltonianTimeEvolution.fsx -- --backend topological"
    printfn "  dotnet fsi HamiltonianTimeEvolution.fsx -- --output results.json --csv results.csv"
    printfn "  dotnet fsi HamiltonianTimeEvolution.fsx -- --svg"
    printfn "  dotnet fsi HamiltonianTimeEvolution.fsx -- --help"
    printfn ""
