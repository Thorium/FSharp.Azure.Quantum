// ==============================================================================
// H2 Molecule Ground State Energy Example
// ==============================================================================
// Demonstrates VQE (Variational Quantum Eigensolver) for the hydrogen molecule
// using two approaches:
//   1. Direct API: Low-level config structs for maximum control
//   2. Builder API: Computation expressions for clean, declarative code
//
// Includes bond-length scanning and energy convergence plotting.
//
// WHEN TO USE THIS LIBRARY:
//   - Lightweight quantum chemistry without heavy dependencies
//   - Custom VQE implementations and experimentation
//   - Multi-backend support (Local, IonQ, Rigetti, Azure)
//   - Small molecules (< 10 qubits): H2, H2O, LiH, NH3
//   - Pure F# implementation, no Q# required
//
// WHEN TO USE Microsoft.Quantum.Chemistry INSTEAD:
//   - Large molecules (50+ qubits) requiring full molecular orbitals
//   - Production quantum chemistry pipelines
//   - Integration with Gaussian, PySCF, NWChem
//   - Advanced features (UCCSD ansatz, Jordan-Wigner, Bravyi-Kitaev)
//
// USE BOTH TOGETHER:
//   - Use Microsoft.Quantum.Chemistry for accurate Hamiltonian construction
//   - Use this library for flexible VQE execution on any backend
//   - Bridge via FCIDump files or direct Hamiltonian conversion
//
// Usage:
//   dotnet fsi H2Molecule.fsx
//   dotnet fsi H2Molecule.fsx -- --bond-length 0.74
//   dotnet fsi H2Molecule.fsx -- --method all --max-iterations 200
//   dotnet fsi H2Molecule.fsx -- --scan 0.5,0.6,0.7,0.74,0.8,0.9,1.0
//   dotnet fsi H2Molecule.fsx -- --output results.json --csv results.csv --quiet
//
// ==============================================================================

(*
Background Theory
-----------------
The hydrogen molecule (H2) is the simplest neutral molecule and the "hydrogen atom
of quantum chemistry" -- the first system where quantum computers can outperform
pen-and-paper calculations. The H2 ground state energy problem asks: what is the
lowest energy eigenvalue of the molecular Hamiltonian H? This determines chemical
properties like bond length (0.74 A), binding energy (4.75 eV), and vibrational
frequency. Exact classical solution is possible for H2 but scales exponentially
for larger molecules, motivating quantum approaches.

The Variational Quantum Eigensolver (VQE) is a hybrid quantum-classical algorithm
for finding ground state energies. It uses the variational principle: for any trial
state |psi(theta)>, the energy E(theta) = <psi(theta)|H|psi(theta)> >= E0 (ground
state energy). VQE prepares |psi(theta)> on a quantum computer, measures E(theta),
and uses a classical optimizer to minimize over theta. For H2, a simple ansatz with
one parameter (the bond angle) suffices to reach chemical accuracy
(+/- 1.6 mHartree ~ +/- 1 kcal/mol).

Key Equations:
  - Molecular Hamiltonian: H = Sum_ij h_ij a_i^dag a_j + 1/2 Sum_ijkl h_ijkl a_i^dag a_j^dag a_k a_l + E_nuc
  - Variational principle: E(theta) = <psi(theta)|H|psi(theta)> >= E0 for all theta
  - H2 exact ground state energy: E0 = -1.137 Hartree (at equilibrium)
  - Chemical accuracy: |E_computed - E_exact| < 1.6 mHartree ~ 1 kcal/mol
  - Qubit mapping (Jordan-Wigner): a_i^dag -> (X_i - iY_i)/2 * Product_{j<i} Z_j

Quantum Advantage:
  While H2 can be solved classically, it demonstrates the VQE workflow that scales
  to intractable molecules. The number of parameters in a full CI expansion grows
  as O(N^4) for N orbitals; quantum computers handle this natively via superposition.
  For molecules like FeMoCo (nitrogen fixation catalyst, ~100 orbitals), classical
  simulation is impossible, but VQE on fault-tolerant quantum computers could solve
  it. H2 serves as a benchmark: achieving chemical accuracy on H2 validates the
  entire VQE pipeline (ansatz, optimizer, error mitigation, hardware).

References:
  [1] Peruzzo et al., "A variational eigenvalue solver on a photonic quantum
      processor", Nat. Commun. 5, 4213 (2014). https://doi.org/10.1038/ncomms5213
  [2] O'Malley et al., "Scalable Quantum Simulation of Molecular Energies",
      Phys. Rev. X 6, 031007 (2016). https://doi.org/10.1103/PhysRevX.6.031007
  [3] McArdle et al., "Quantum computational chemistry", Rev. Mod. Phys. 92,
      015003 (2020). https://doi.org/10.1103/RevModPhys.92.015003
  [4] Wikipedia: Hydrogen_molecule
      https://en.wikipedia.org/wiki/Hydrogen_molecule
*)

//#r "nuget: FSharp.Azure.Quantum"
#r "nuget: Microsoft.Extensions.Logging.Abstractions, 10.0.0"
#r "../../src/FSharp.Azure.Quantum/bin/Debug/net10.0/FSharp.Azure.Quantum.dll"
#load "../_common/Cli.fs"
#load "../_common/Data.fs"
#load "../_common/Reporting.fs"

open FSharp.Azure.Quantum.QuantumChemistry
open FSharp.Azure.Quantum.QuantumChemistry.QuantumChemistryBuilder
open FSharp.Azure.Quantum.Core
open FSharp.Azure.Quantum.Core.BackendAbstraction
open FSharp.Azure.Quantum.Backends.LocalBackend
open FSharp.Azure.Quantum.Examples.Common

// ==============================================================================
// CLI ARGUMENT PARSING
// ==============================================================================

let argv = fsi.CommandLineArgs |> Array.skip 1
let args = Cli.parse argv

Cli.exitIfHelp "H2Molecule.fsx" "H2 ground state energy via VQE (Direct API + Builder API)"
    [ { Cli.OptionSpec.Name = "bond-length"; Description = "H-H bond length in Angstroms"; Default = Some "0.74" }
      { Cli.OptionSpec.Name = "method"; Description = "Solver method: VQE, DFT, auto, or all"; Default = Some "all" }
      { Cli.OptionSpec.Name = "max-iterations"; Description = "Maximum VQE iterations"; Default = Some "100" }
      { Cli.OptionSpec.Name = "tolerance"; Description = "Convergence tolerance"; Default = Some "1e-6" }
      { Cli.OptionSpec.Name = "scan"; Description = "Comma-separated bond lengths for scan"; Default = Some "0.5,0.6,0.7,0.74,0.8,0.9,1.0" }
      { Cli.OptionSpec.Name = "basis"; Description = "Basis set for builder examples"; Default = Some "sto-3g" }
      { Cli.OptionSpec.Name = "output"; Description = "Write results to JSON file"; Default = None }
      { Cli.OptionSpec.Name = "csv"; Description = "Write results to CSV file"; Default = None }
      { Cli.OptionSpec.Name = "quiet"; Description = "Suppress informational output"; Default = None } ]
    args

let quiet = Cli.hasFlag "quiet" args
let bondLength = Cli.getFloatOr "bond-length" 0.74 args
let methodChoice = Cli.getOr "method" "all" args
let maxIterations = Cli.getIntOr "max-iterations" 100 args
let tolerance = Cli.getFloatOr "tolerance" 1e-6 args
let basisSet = Cli.getOr "basis" "sto-3g" args
let scanLengths =
    Cli.getOr "scan" "0.5,0.6,0.7,0.74,0.8,0.9,1.0" args
    |> fun s -> s.Split(',')
    |> Array.choose (fun s ->
        match System.Double.TryParse(s.Trim()) with
        | true, v -> Some v
        | false, _ -> None)
    |> Array.toList

let runVQE = methodChoice = "VQE" || methodChoice = "all"
let runDFT = methodChoice = "DFT" || methodChoice = "all"
let runAuto = methodChoice = "auto" || methodChoice = "all"

// NOTE: For standard molecules with equilibrium geometries, you can also use:
//   open FSharp.Azure.Quantum.Data
//   let water = MoleculeLibrary.get "H2O" |> Molecule.fromLibrary
// The MoleculeLibrary contains 62 pre-defined molecules from NIST CCCBDB.

// ==============================================================================
// PART 1: Direct API (Low-Level)
// ==============================================================================

if not quiet then
    printfn "================================================================"
    printfn "APPROACH 1: Direct API (Low-Level)"
    printfn "================================================================"

/// Run a ground-state energy calculation for H2 at a given bond length and method.
/// Returns the result as a Map suitable for structured output.
let runGroundState (label: string) (distance: float) (method: GroundStateMethod) =
    let h2 = Molecule.createH2 distance
    let backend = LocalBackend() :> IQuantumBackend
    let config = {
        Method = method
        Backend = Some backend
        MaxIterations = maxIterations
        Tolerance = tolerance
        InitialParameters = None
        ProgressReporter = None
        ErrorMitigation = None
        IntegralProvider = None
    }

    if not quiet then
        printfn ""
        printfn "--- %s ---" label
        printfn "Molecule: %s" h2.Name
        printfn "Atoms: %d" h2.Atoms.Length
        printfn "Bonds: %d" h2.Bonds.Length
        printfn "Electrons: %d" (Molecule.countElectrons h2)
        printfn "Bond length: %.4f A" distance
        printfn "Method: %A" method
        printfn "Running calculation..."

    let result = GroundStateEnergy.estimateEnergy h2 config |> Async.RunSynchronously

    match result with
    | Ok vqeResult ->
        let eV = vqeResult.Energy * 27.2114
        let error = abs(vqeResult.Energy - (-1.174))
        if not quiet then
            printfn "Ground state energy: %.6f Hartree" vqeResult.Energy
            printfn "  Expected (experimental): -1.174 Hartree"
            printfn "  Error: %.6f Hartree" error
            printfn "  In electron volts: %.6f eV" eV
            printfn "  Iterations: %d" vqeResult.Iterations
            printfn "  Converged: %b" vqeResult.Converged
        [ "Label", label
          "BondLength_A", sprintf "%.4f" distance
          "Method", sprintf "%A" method
          "Energy_Hartree", sprintf "%.6f" vqeResult.Energy
          "Energy_eV", sprintf "%.6f" eV
          "Error_Hartree", sprintf "%.6f" error
          "Iterations", sprintf "%d" vqeResult.Iterations
          "Converged", sprintf "%b" vqeResult.Converged ]
        |> Map.ofList
        |> Some
    | Error err ->
        if not quiet then
            printfn "Calculation failed: %s" err.Message
        None

// Run the selected methods
let directResults =
    [ if runVQE then
          runGroundState "VQE" bondLength GroundStateMethod.VQE
      if runDFT then
          runGroundState "Classical DFT" bondLength GroundStateMethod.ClassicalDFT
      if runAuto then
          runGroundState "Automatic" bondLength GroundStateMethod.Automatic ]
    |> List.choose id

// ==============================================================================
// PART 2: Builder API (Declarative)
// ==============================================================================

if not quiet then
    printfn ""
    printfn "================================================================"
    printfn "APPROACH 2: Builder API (Declarative)"
    printfn "================================================================"

// Example 1: H2 ground state at configured bond length
if not quiet then
    printfn ""
    printfn "--- Example 1: H2 Ground State at %.2f A ---" bondLength

let h2Problem = quantumChemistry {
    molecule (Molecule.createH2 bondLength)
    basis basisSet
    ansatz UCCSD
}

if not quiet then
    printfn "Problem created successfully!"
    printfn "Molecule: %s" h2Problem.Molecule.Value.Name
    printfn "Basis: %s" h2Problem.Basis.Value
    printfn "Ansatz: %A" h2Problem.Ansatz.Value

// Example 2: H2O (water) ground state
if not quiet then
    printfn ""
    printfn "--- Example 2: H2O Ground State ---"

let h2oProblem = quantumChemistry {
    molecule (Molecule.createH2O ())
    basis basisSet
    ansatz HEA
    maxIterations 150
}

if not quiet then
    printfn "Problem created successfully!"
    printfn "Molecule: %s" h2oProblem.Molecule.Value.Name
    printfn "Number of atoms: %d" h2oProblem.Molecule.Value.Atoms.Length

// Example 3: LiH (lithium hydride)
if not quiet then
    printfn ""
    printfn "--- Example 3: LiH Ground State ---"

let lihCustom = Molecule.createLiH 1.6

let lihProblem = quantumChemistry {
    molecule lihCustom
    basis basisSet
    ansatz UCCSD
    optimizer "COBYLA"
}

if not quiet then
    printfn "Problem created successfully!"
    printfn "Molecule: %s" lihProblem.Molecule.Value.Name
    printfn "Optimizer: %s" lihProblem.Optimizer.Value.Method

// Example 4: Conditional basis selection
if not quiet then
    printfn ""
    printfn "--- Example 4: Conditional Basis Selection ---"

let smallMolecule = true
let selectedBasis = if smallMolecule then "sto-3g" else "6-31g"

let conditionalProblem = quantumChemistry {
    molecule (Molecule.createH2 bondLength)
    basis selectedBasis
    ansatz UCCSD
}

if not quiet then
    printfn "Problem created with basis: %s" conditionalProblem.Basis.Value

// ==============================================================================
// PART 3: Bond Length Scan
// ==============================================================================

if not quiet then
    printfn ""
    printfn "================================================================"
    printfn "PART 3: Bond Length Scan"
    printfn "================================================================"
    printfn "Scanning bond lengths: %A" scanLengths

let scanResults =
    scanLengths
    |> List.choose (fun d ->
        let h2Scan = Molecule.createH2 d
        let backend = LocalBackend() :> IQuantumBackend
        let scanConfig = {
            Method = GroundStateMethod.VQE
            Backend = Some backend
            MaxIterations = maxIterations
            Tolerance = tolerance
            InitialParameters = None
            ProgressReporter = None
            ErrorMitigation = None
            IntegralProvider = None
        }
        let scanResult = GroundStateEnergy.estimateEnergy h2Scan scanConfig |> Async.RunSynchronously
        match scanResult with
        | Ok vqeResult ->
            if not quiet then
                printfn "  Distance %.2f A: %.6f Hartree (%d iterations)" d vqeResult.Energy vqeResult.Iterations
            [ "Label", sprintf "Scan_%.2f" d
              "BondLength_A", sprintf "%.4f" d
              "Method", "VQE"
              "Energy_Hartree", sprintf "%.6f" vqeResult.Energy
              "Energy_eV", sprintf "%.6f" (vqeResult.Energy * 27.2114)
              "Iterations", sprintf "%d" vqeResult.Iterations
              "Converged", sprintf "%b" vqeResult.Converged ]
            |> Map.ofList
            |> Some
        | Error err ->
            if not quiet then
                printfn "  Distance %.2f A: FAILED (%s)" d err.Message
            None)

// ==============================================================================
// PART 4: Energy Convergence During VQE Optimization
// ==============================================================================

if not quiet then
    printfn ""
    printfn "================================================================"
    printfn "PART 4: Energy Convergence During VQE Optimization"
    printfn "================================================================"
    printfn ""
    printfn "The VQEResult includes EnergyHistory for tracking optimization progress."
    printfn "This can be used to verify convergence and tune hyperparameters."
    printfn ""

let h2Conv = Molecule.createH2 (bondLength + 0.01)
let convConfig = {
    Method = GroundStateMethod.VQE
    Backend = Some (LocalBackend() :> IQuantumBackend)
    MaxIterations = 30
    Tolerance = 1e-8
    InitialParameters = None
    ProgressReporter = None
    ErrorMitigation = None
    IntegralProvider = None
}

let convResult = GroundStateEnergy.estimateEnergy h2Conv convConfig |> Async.RunSynchronously

match convResult with
| Ok vqeResult ->
    if not quiet then
        printfn "Final energy: %.6f Hartree" vqeResult.Energy
        printfn "Iterations: %d" vqeResult.Iterations
        printfn "Converged: %b" vqeResult.Converged
        printfn ""

        if vqeResult.EnergyHistory.Length > 0 then
            printfn "Energy History (iteration -> energy):"
            printfn "-----------------------------------------"
            let energies = vqeResult.EnergyHistory |> List.map snd
            let minE = energies |> List.min
            let maxE = energies |> List.max
            let range = maxE - minE

            if range > 0.0 then
                printfn ""
                printfn "ASCII Convergence Plot:"
                printfn ""
                for (iteration, energy) in vqeResult.EnergyHistory do
                    let normalized = (energy - minE) / range
                    let barWidth = int (normalized * 40.0)
                    let bar = String.replicate barWidth "#"
                    printfn "  %3d | %.6f | %s" iteration energy bar
            else
                printfn ""
                for (iteration, energy) in vqeResult.EnergyHistory do
                    printfn "  %3d | %.6f Hartree" iteration energy
        else
            printfn "  (Single-point calculation - no iteration history)"

        printfn ""
        printfn "Tip: Use --csv to export results for external plotting tools."
| Error err ->
    if not quiet then
        printfn "Convergence calculation failed: %s" err.Message

// ==============================================================================
// STRUCTURED OUTPUT
// ==============================================================================

let allResults = directResults @ scanResults

match Cli.tryGet "output" args with
| Some path ->
    Reporting.writeJson path allResults
    if not quiet then printfn "\nResults written to %s" path
| None -> ()

match Cli.tryGet "csv" args with
| Some path ->
    let header = [ "Label"; "BondLength_A"; "Method"; "Energy_Hartree"; "Energy_eV"; "Error_Hartree"; "Iterations"; "Converged" ]
    let rows =
        allResults
        |> List.map (fun m ->
            header |> List.map (fun h -> m |> Map.tryFind h |> Option.defaultValue ""))
    Reporting.writeCsv path header rows
    if not quiet then printfn "CSV results written to %s" path
| None -> ()

// ==============================================================================
// USAGE HINTS
// ==============================================================================

if argv.Length = 0 && not quiet then
    printfn ""
    printfn "Tip: Customize this example with CLI flags:"
    printfn "  --bond-length 0.80        Set H-H bond length (Angstroms)"
    printfn "  --method VQE|DFT|auto|all Choose solver method"
    printfn "  --scan 0.5,0.7,0.9       Custom bond-length scan values"
    printfn "  --basis 6-31g            Change basis set for builder examples"
    printfn "  --max-iterations 200      Increase VQE iterations"
    printfn "  --output results.json     Export results as JSON"
    printfn "  --csv results.csv         Export results as CSV"
    printfn "  --quiet                   Suppress informational output"
    printfn "  --help                    Show full usage information"

if not quiet then
    printfn ""
    printfn "Done!"
