// ==============================================================================
// Hartree-Fock Initial State Preparation Example
// ==============================================================================
// Demonstrates the importance of starting VQE from the Hartree-Fock (HF)
// state instead of |0...0> for quantum chemistry applications.
//
// Key Insight: VQE converges 10-100x faster from HF initial state!
//
// The Hartree-Fock state is the best single-determinant approximation
// to the ground state of a molecule. Preparing it on a quantum computer
// requires only simple X gates on the lowest-energy orbitals, giving a
// low-depth circuit that drastically reduces VQE convergence time.
//
// This example prepares HF states for configurable molecules (H2, LiH)
// and verifies that the resulting quantum states match the expected
// occupation pattern.
//
// Quantum Advantage:
// While the HF state itself is classically computable, it serves as the
// starting point for quantum algorithms (VQE, QPE) that capture
// electron correlation effects beyond HF -- effects that are
// exponentially hard to compute classically for large molecules.
//
// Usage:
//   dotnet fsi HartreeFockInitialStateExample.fsx
//   dotnet fsi HartreeFockInitialStateExample.fsx -- --molecule H2
//   dotnet fsi HartreeFockInitialStateExample.fsx -- --molecule LiH
//   dotnet fsi HartreeFockInitialStateExample.fsx -- --molecule both --output results.json
//   dotnet fsi HartreeFockInitialStateExample.fsx -- --csv results.csv --quiet
//
// ==============================================================================

#r "nuget: Microsoft.Extensions.Logging.Abstractions, 10.0.0"
#r "../../src/FSharp.Azure.Quantum/bin/Debug/net10.0/FSharp.Azure.Quantum.dll"
#load "../_common/Cli.fs"
#load "../_common/Data.fs"
#load "../_common/Reporting.fs"

open FSharp.Azure.Quantum.QuantumChemistry.FermionMapping.HartreeFock
open FSharp.Azure.Quantum.Core.BackendAbstraction
open FSharp.Azure.Quantum.Core.QuantumState
open FSharp.Azure.Quantum.Backends.LocalBackend
open FSharp.Azure.Quantum.Examples.Common

// ==============================================================================
// CLI ARGUMENT PARSING
// ==============================================================================

let argv = fsi.CommandLineArgs |> Array.skip 1
let args = Cli.parse argv

Cli.exitIfHelp
    "HartreeFockInitialStateExample.fsx"
    "Hartree-Fock initial state preparation for quantum chemistry VQE."
    [ { Cli.OptionSpec.Name = "molecule"; Description = "Molecule to prepare: H2, LiH, or both"; Default = Some "both" }
      { Cli.OptionSpec.Name = "output";   Description = "Write results to JSON file";             Default = None }
      { Cli.OptionSpec.Name = "csv";      Description = "Write results to CSV file";              Default = None }
      { Cli.OptionSpec.Name = "quiet";    Description = "Suppress informational output";          Default = None } ]
    args

let quiet = Cli.hasFlag "quiet" args
let moleculeArg = Cli.getOr "molecule" "both" args

// ==============================================================================
// MOLECULE CONFIGURATIONS
// ==============================================================================

/// A molecule configuration for HF state preparation
type HFMolecule = {
    Name: string
    ShortName: string
    Electrons: int
    SpinOrbitals: int
    ExpectedStateLabel: string
}

let h2Config = {
    Name = "H2 (Hydrogen)"
    ShortName = "H2"
    Electrons = 2
    SpinOrbitals = 4
    ExpectedStateLabel = "|1100>"
}

let lihConfig = {
    Name = "LiH (Lithium Hydride)"
    ShortName = "LiH"
    Electrons = 4
    SpinOrbitals = 10
    ExpectedStateLabel = "|1111000000>"
}

let allMolecules = [ h2Config; lihConfig ]

/// Molecules filtered by CLI --molecule argument
let filteredMolecules =
    match moleculeArg.ToLowerInvariant() with
    | "both" | "all" -> allMolecules
    | "h2"           -> [ h2Config ]
    | "lih"          -> [ lihConfig ]
    | _              -> allMolecules

// ==============================================================================
// QUANTUM BACKEND SETUP
// ==============================================================================

let backend = LocalBackend() :> IQuantumBackend

if not quiet then
    printfn "=================================================================="
    printfn "   Hartree-Fock Initial State Preparation"
    printfn "=================================================================="
    printfn ""
    printfn "Quantum Backend"
    printfn "------------------------------------------------------------------"
    printfn "  Backend: %s" backend.Name
    printfn "  Type: Statevector Simulator"
    printfn ""

// ==============================================================================
// HF STATE PREPARATION AND VERIFICATION
// ==============================================================================

/// Prepare and verify a Hartree-Fock state, returning structured results
let prepareAndVerify (mol: HFMolecule) : Map<string, string> =
    if not quiet then
        printfn "=================================================================="
        printfn "   %s (%d electrons, %d spin orbitals)" mol.Name mol.Electrons mol.SpinOrbitals
        printfn "=================================================================="
        printfn ""
        printfn "Configuration:"
        printfn "  Electrons: %d" mol.Electrons
        printfn "  Spin Orbitals: %d" mol.SpinOrbitals
        printfn "  Expected HF State: %s" mol.ExpectedStateLabel
        printfn ""

    match prepareHartreeFockState mol.Electrons mol.SpinOrbitals backend with
    | Error err ->
        if not quiet then printfn "  Error: %A" err; printfn ""
        Map.ofList [
            "molecule", mol.ShortName
            "electrons", sprintf "%d" mol.Electrons
            "spin_orbitals", sprintf "%d" mol.SpinOrbitals
            "expected_state", mol.ExpectedStateLabel
            "num_qubits", "N/A"
            "hf_match", "N/A"
            "probability", "N/A"
            "status", sprintf "Error: %A" err
        ]
    | Ok hfState ->
        let nQubits = numQubits hfState
        let isCorrect = isHartreeFockState mol.Electrons hfState

        // Construct expected bitstring (big-endian: [qN-1; ...; q1; q0])
        // HF state: lowest orbitals occupied -> q0..q(n-1) = 1, rest = 0
        let expectedBitstring =
            Array.init mol.SpinOrbitals (fun i ->
                if i >= mol.SpinOrbitals - mol.Electrons then 1 else 0)
        let prob = probability expectedBitstring hfState

        if not quiet then
            printfn "  Hartree-Fock state prepared successfully!"
            printfn ""
            printfn "State Verification:"
            printfn "  Number of qubits: %d" nQubits
            if isCorrect then
                printfn "  State matches expected HF configuration"
            else
                printfn "  State does NOT match HF configuration"
            printfn ""
            printfn "Computational Basis Probability:"
            printfn "  %s: %.6f (expected: 1.0)" mol.ExpectedStateLabel prob
            printfn ""

        Map.ofList [
            "molecule", mol.ShortName
            "electrons", sprintf "%d" mol.Electrons
            "spin_orbitals", sprintf "%d" mol.SpinOrbitals
            "expected_state", mol.ExpectedStateLabel
            "num_qubits", sprintf "%d" nQubits
            "hf_match", sprintf "%b" isCorrect
            "probability", sprintf "%.6f" prob
            "status", "OK"
        ]

/// Results from all configured molecules
let moleculeResults =
    filteredMolecules |> List.map prepareAndVerify

// ==============================================================================
// INPUT VALIDATION TESTS
// ==============================================================================

if not quiet then
    printfn "=================================================================="
    printfn "   Input Validation"
    printfn "=================================================================="
    printfn ""

/// Run a validation test and return a result map
let runValidationTest (testLabel: string) (electrons: int) (orbitals: int) : Map<string, string> =
    if not quiet then
        printfn "  %s (%d electrons, %d orbitals)" testLabel electrons orbitals
    match prepareHartreeFockState electrons orbitals backend with
    | Error err ->
        if not quiet then printfn "    Correctly rejected: %A" err; printfn ""
        Map.ofList [
            "test", testLabel; "electrons", sprintf "%d" electrons
            "orbitals", sprintf "%d" orbitals; "status", "Rejected"
            "error", sprintf "%A" err
        ]
    | Ok _ ->
        if not quiet then printfn "    Unexpectedly accepted (should have been rejected!)"; printfn ""
        Map.ofList [
            "test", testLabel; "electrons", sprintf "%d" electrons
            "orbitals", sprintf "%d" orbitals; "status", "Accepted (unexpected)"
            "error", "N/A"
        ]

let validationResults = [
    runValidationTest "More electrons than orbitals" 6 4
    runValidationTest "Negative electrons" -2 4
    runValidationTest "Zero orbitals" 2 0
]

// ==============================================================================
// SUMMARY
// ==============================================================================

if not quiet then
    printfn "=================================================================="
    printfn "   Key Takeaways"
    printfn "=================================================================="
    printfn ""
    printfn "Hartree-Fock Initial State"
    printfn "------------------------------------------------------------------"
    printfn "  - HF state = best single-determinant approximation"
    printfn "  - Quantum state: |11...100...0> (first n qubits = |1>)"
    printfn "  - Prepared using simple X gates (low depth)"
    printfn "  - Standard practice in ALL quantum chemistry codes"
    printfn ""
    printfn "Production Benefits"
    printfn "------------------------------------------------------------------"
    printfn "  - VQE convergence: 10-100x faster"
    printfn "  - Circuit depth: 50-90%% reduction"
    printfn "  - Error accumulation: Significantly reduced"
    printfn "  - Cloud costs: Lower due to fewer shots/circuits"
    printfn ""
    printfn "When to Use"
    printfn "------------------------------------------------------------------"
    printfn "  - ALWAYS use for quantum chemistry VQE"
    printfn "  - Drug discovery applications"
    printfn "  - Materials science simulations"
    printfn "  - Any molecular ground state calculation"
    printfn "  - NOT needed for generic optimization (QAOA, etc.)"
    printfn ""

// ==============================================================================
// STRUCTURED OUTPUT
// ==============================================================================

let allResults : Map<string, obj> =
    Map.ofList [
        "script", box "HartreeFockInitialStateExample.fsx"
        "molecule_arg", box moleculeArg
        "molecule_results", box moleculeResults
        "validation_results", box validationResults
    ]

Cli.tryGet "output" args |> Option.iter (fun path ->
    Reporting.writeJson path allResults
    if not quiet then printfn "Results written to %s" path)

match Cli.tryGet "csv" args with
| Some path ->
    let header = [ "molecule"; "electrons"; "spin_orbitals"; "expected_state"; "num_qubits"; "hf_match"; "probability"; "status" ]
    let rows =
        moleculeResults
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
    printfn "  dotnet fsi HartreeFockInitialStateExample.fsx -- --molecule H2"
    printfn "  dotnet fsi HartreeFockInitialStateExample.fsx -- --molecule LiH"
    printfn "  dotnet fsi HartreeFockInitialStateExample.fsx -- --output results.json"
    printfn "  dotnet fsi HartreeFockInitialStateExample.fsx -- --csv results.csv --quiet"
    printfn "  dotnet fsi HartreeFockInitialStateExample.fsx -- --help"
    printfn ""
