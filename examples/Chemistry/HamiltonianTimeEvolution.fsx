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
//   - H2 molecular Hamiltonian construction
//   - Time evolution via exp(-iHt)|psi> using Trotter decomposition
//   - Backend-agnostic algorithm design via IQuantumBackend
//   - Comparison of 1st vs 2nd order Trotter accuracy
//
// Quantum Advantage:
// Time evolution of molecular Hamiltonians is exponentially hard classically
// (Hilbert space grows as 2^n). Quantum simulation provides natural encoding
// and efficient evolution via Trotter-Suzuki decomposition.
//
// Usage:
//   dotnet fsi HamiltonianTimeEvolution.fsx
//   dotnet fsi HamiltonianTimeEvolution.fsx -- --bond-length 0.8
//   dotnet fsi HamiltonianTimeEvolution.fsx -- --time 2.0 --steps 40
//   dotnet fsi HamiltonianTimeEvolution.fsx -- --backend local
//   dotnet fsi HamiltonianTimeEvolution.fsx -- --backend topological
//   dotnet fsi HamiltonianTimeEvolution.fsx -- --output results.json --csv results.csv
//   dotnet fsi HamiltonianTimeEvolution.fsx -- --quiet --output results.json
//
// ==============================================================================

#r "nuget: Microsoft.Extensions.Logging.Abstractions, 10.0.0"
#r "../../src/FSharp.Azure.Quantum/bin/Debug/net10.0/FSharp.Azure.Quantum.dll"
#r "../../src/FSharp.Azure.Quantum.Topological/bin/Debug/net10.0/FSharp.Azure.Quantum.Topological.dll"
#load "../_common/Cli.fs"
#load "../_common/Data.fs"
#load "../_common/Reporting.fs"

open FSharp.Azure.Quantum.QuantumChemistry
open FSharp.Azure.Quantum.LocalSimulator
open FSharp.Azure.Quantum.Core
open FSharp.Azure.Quantum.Core.BackendAbstraction
open FSharp.Azure.Quantum.Backends
open FSharp.Azure.Quantum.Topological
open FSharp.Azure.Quantum.Examples.Common

// ==============================================================================
// CLI ARGUMENT PARSING
// ==============================================================================

let argv = fsi.CommandLineArgs |> Array.skip 1
let args = Cli.parse argv

Cli.exitIfHelp
    "HamiltonianTimeEvolution.fsx"
    "Trotter-Suzuki time evolution for molecular Hamiltonians on unified backends."
    [ { Cli.OptionSpec.Name = "bond-length"; Description = "H2 bond length in Angstroms";        Default = Some "0.74" }
      { Cli.OptionSpec.Name = "time";        Description = "Evolution time in atomic units";      Default = Some "1.0" }
      { Cli.OptionSpec.Name = "steps";       Description = "Number of Trotter steps";             Default = Some "20" }
      { Cli.OptionSpec.Name = "order";       Description = "Trotter order (1 or 2)";              Default = Some "2" }
      { Cli.OptionSpec.Name = "backend";     Description = "Backend: local, topological, or both"; Default = Some "both" }
      { Cli.OptionSpec.Name = "output";      Description = "Write results to JSON file";          Default = None }
      { Cli.OptionSpec.Name = "csv";         Description = "Write results to CSV file";           Default = None }
      { Cli.OptionSpec.Name = "quiet";       Description = "Suppress informational output";      Default = None } ]
    args

let quiet = Cli.hasFlag "quiet" args
let bondLength = Cli.getFloatOr "bond-length" 0.74 args
let evolutionTime = Cli.getFloatOr "time" 1.0 args
let trotterSteps = Cli.getIntOr "steps" 20 args
let trotterOrder = Cli.getIntOr "order" 2 args
let backendArg = Cli.getOr "backend" "both" args

let runLocal = backendArg.ToLowerInvariant() = "local" || backendArg.ToLowerInvariant() = "both"
let runTopo  = backendArg.ToLowerInvariant() = "topological" || backendArg.ToLowerInvariant() = "both"

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

/// Get probability of specific basis state (works with any QuantumState)
let getBasisProbability (state: QuantumState) (basisIndex: int) (numQubits: int) =
    let bitstring =
        [| for i in 0 .. numQubits - 1 -> (basisIndex >>> i) &&& 1 |]
    QuantumState.probability bitstring state

// ==============================================================================
// MOLECULE AND HAMILTONIAN SETUP
// ==============================================================================

if not quiet then
    printfn "=================================================================="
    printfn "   Hamiltonian Time Evolution Simulation"
    printfn "=================================================================="
    printfn ""
    printfn "Parameters:"
    printfn "  Bond length: %.2f A" bondLength
    printfn "  Evolution time: %.1f a.u." evolutionTime
    printfn "  Trotter steps: %d" trotterSteps
    printfn "  Trotter order: %d" trotterOrder
    printfn "  Backends: %s" backendArg
    printfn ""

let h2 = Molecule.createH2 bondLength

if not quiet then
    printfn "Creating H2 molecule (bond length: %.2f A)" bondLength

let hamiltonianResult = MolecularHamiltonian.build h2

/// Mutable list to collect results across backends
let mutable allBackendResults : Map<string, string> list = []

match hamiltonianResult with
| Error err ->
    if not quiet then printfn "Hamiltonian construction failed: %A" err
| Ok hamiltonian ->
    if not quiet then
        printfn "Molecular Hamiltonian constructed"
        printfn "  Qubits: %d" hamiltonian.NumQubits
        printfn "  Terms: %d" hamiltonian.Terms.Length
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
            match backend.InitializeState hamiltonian.NumQubits with
            | Ok state -> state
            | Error err -> failwithf "Failed to initialize state: %A" err

        if not quiet then
            printfn "  Initial state: |00...0>"
            analyzeState initialState hamiltonian.NumQubits "Initial" |> ignore
            printfn ""

        let config = {
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
            printfn "  Running exp(-iHt)|psi0>..."

        let result = HamiltonianSimulation.simulate hamiltonian initialState config

        match result with
        | Error err ->
            if not quiet then
                printfn "  Simulation failed: %A" err
                printfn ""
            Map.ofList [
                "backend", backendLabel
                "backend_name", backend.Name
                "bond_length_A", sprintf "%.2f" bondLength
                "time_au", sprintf "%.1f" evolutionTime
                "trotter_steps", sprintf "%d" trotterSteps
                "trotter_order", sprintf "%d" trotterOrder
                "num_qubits", sprintf "%d" hamiltonian.NumQubits
                "num_terms", sprintf "%d" hamiltonian.Terms.Length
                "ground_state_prob", "N/A"
                "normalized", "N/A"
                "status", sprintf "Error: %A" err
            ]
        | Ok finalState ->
            let p0 = getBasisProbability finalState 0 hamiltonian.NumQubits
            let normalized = QuantumState.isNormalized finalState

            if not quiet then
                printfn "  Simulation complete"
                analyzeState finalState hamiltonian.NumQubits "Final" |> ignore
                printfn ""
                printfn "  P(|0...0>): %.6f" p0
                printfn "  Normalized: %b" normalized
                printfn ""

            Map.ofList [
                "backend", backendLabel
                "backend_name", backend.Name
                "bond_length_A", sprintf "%.2f" bondLength
                "time_au", sprintf "%.1f" evolutionTime
                "trotter_steps", sprintf "%d" trotterSteps
                "trotter_order", sprintf "%d" trotterOrder
                "num_qubits", sprintf "%d" hamiltonian.NumQubits
                "num_terms", sprintf "%d" hamiltonian.Terms.Length
                "ground_state_prob", sprintf "%.6f" p0
                "normalized", sprintf "%b" normalized
                "status", "OK"
            ]

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
        let numAnyons = hamiltonian.NumQubits + 5
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
            printfn "   Trotter Order Comparison"
            printfn "=================================================================="
            printfn ""

        let localBackend = LocalBackend.LocalBackend() :> IQuantumBackend
        let localInitialState =
            match localBackend.InitializeState hamiltonian.NumQubits with
            | Ok state -> state
            | Error err -> failwithf "Failed to initialize state: %A" err

        let config1st = {
            HamiltonianSimulation.SimulationConfig.Time = evolutionTime
            HamiltonianSimulation.SimulationConfig.TrotterSteps = trotterSteps
            HamiltonianSimulation.SimulationConfig.TrotterOrder = 1
            HamiltonianSimulation.SimulationConfig.Backend = Some localBackend
        }
        let config2nd = { config1st with TrotterOrder = 2 }

        let result1st = HamiltonianSimulation.simulate hamiltonian localInitialState config1st
        let result2nd = HamiltonianSimulation.simulate hamiltonian localInitialState config2nd

        match result1st, result2nd with
        | Ok state1st, Ok state2nd ->
            let p0_1st = getBasisProbability state1st 0 hamiltonian.NumQubits
            let p0_2nd = getBasisProbability state2nd 0 hamiltonian.NumQubits

            if not quiet then
                printfn "  1st order Trotter:"
                printfn "    Normalized: %b" (QuantumState.isNormalized state1st)
                printfn "    P(|0...0>): %.6f" p0_1st
                printfn ""
                printfn "  2nd order Trotter (symmetric):"
                printfn "    Normalized: %b" (QuantumState.isNormalized state2nd)
                printfn "    P(|0...0>): %.6f" p0_2nd
                printfn ""
                printfn "  2nd order is more accurate (O(dt^3) vs O(dt^2) error per step)"
                printfn ""

            // Add Trotter comparison rows
            allBackendResults <- allBackendResults @ [
                Map.ofList [
                    "backend", "Trotter-1st-order"
                    "backend_name", localBackend.Name
                    "bond_length_A", sprintf "%.2f" bondLength
                    "time_au", sprintf "%.1f" evolutionTime
                    "trotter_steps", sprintf "%d" trotterSteps
                    "trotter_order", "1"
                    "num_qubits", sprintf "%d" hamiltonian.NumQubits
                    "num_terms", sprintf "%d" hamiltonian.Terms.Length
                    "ground_state_prob", sprintf "%.6f" p0_1st
                    "normalized", sprintf "%b" (QuantumState.isNormalized state1st)
                    "status", "OK"
                ]
                Map.ofList [
                    "backend", "Trotter-2nd-order"
                    "backend_name", localBackend.Name
                    "bond_length_A", sprintf "%.2f" bondLength
                    "time_au", sprintf "%.1f" evolutionTime
                    "trotter_steps", sprintf "%d" trotterSteps
                    "trotter_order", "2"
                    "num_qubits", sprintf "%d" hamiltonian.NumQubits
                    "num_terms", sprintf "%d" hamiltonian.Terms.Length
                    "ground_state_prob", sprintf "%.6f" p0_2nd
                    "normalized", sprintf "%b" (QuantumState.isNormalized state2nd)
                    "status", "OK"
                ]
            ]
        | _ ->
            if not quiet then printfn "  Trotter comparison failed"; printfn ""

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

let allResults : Map<string, obj> =
    Map.ofList [
        "script", box "HamiltonianTimeEvolution.fsx"
        "bond_length_A", box bondLength
        "evolution_time_au", box evolutionTime
        "trotter_steps", box trotterSteps
        "trotter_order", box trotterOrder
        "backend_arg", box backendArg
        "backend_results", box allBackendResults
    ]

Cli.tryGet "output" args |> Option.iter (fun path ->
    Reporting.writeJson path allResults
    if not quiet then printfn "Results written to %s" path)

match Cli.tryGet "csv" args with
| Some path ->
    let header = [ "backend"; "backend_name"; "bond_length_A"; "time_au"; "trotter_steps"; "trotter_order"; "num_qubits"; "num_terms"; "ground_state_prob"; "normalized"; "status" ]
    let rows =
        allBackendResults
        |> List.map (fun m ->
            header |> List.map (fun h -> m |> Map.tryFind h |> Option.defaultValue ""))
    Reporting.writeCsv path header rows
    if not quiet then printfn "Results written to %s" path
| None -> ()

// ==============================================================================
// USAGE HINTS
// ==============================================================================

if argv.Length = 0 && not quiet then
    printfn "Tip: Customize this example with command-line options:"
    printfn "  dotnet fsi HamiltonianTimeEvolution.fsx -- --bond-length 0.8"
    printfn "  dotnet fsi HamiltonianTimeEvolution.fsx -- --time 2.0 --steps 40"
    printfn "  dotnet fsi HamiltonianTimeEvolution.fsx -- --backend local"
    printfn "  dotnet fsi HamiltonianTimeEvolution.fsx -- --backend topological"
    printfn "  dotnet fsi HamiltonianTimeEvolution.fsx -- --output results.json --csv results.csv"
    printfn "  dotnet fsi HamiltonianTimeEvolution.fsx -- --help"
    printfn ""
