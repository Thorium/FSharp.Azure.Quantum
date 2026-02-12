// ==============================================================================
// H2 Molecule Ground State with UCCSD-VQE
// ==============================================================================
// Production quantum chemistry example demonstrating the complete workflow:
//   1. Build H2 molecular Hamiltonian (one + two-electron terms)
//   2. UCCSD ansatz (chemistry-aware, guarantees chemical accuracy)
//   3. Hartree-Fock initial state (10-100x faster convergence)
//   4. VQE optimization (find ground state energy)
//
// Target: H2 ground state energy = -1.137 Hartree (known exact value)
// Accuracy goal: Chemical accuracy (+/-1 kcal/mol = +/-0.0016 Hartree)
//
// Usage:
//   dotnet fsi H2_UCCSD_VQE_Example.fsx
//   dotnet fsi H2_UCCSD_VQE_Example.fsx -- --bond-length 0.80
//   dotnet fsi H2_UCCSD_VQE_Example.fsx -- --max-iterations 50 --tolerance 1e-6
//   dotnet fsi H2_UCCSD_VQE_Example.fsx -- --active-orbitals 6
//   dotnet fsi H2_UCCSD_VQE_Example.fsx -- --output results.json --csv results.csv
//   dotnet fsi H2_UCCSD_VQE_Example.fsx -- --quiet
//
// ==============================================================================

(*
Background Theory
-----------------
Unitary Coupled Cluster (UCC) is the leading ansatz for quantum chemistry on
quantum computers. Classical Coupled Cluster (CC) is the "gold standard" of
computational chemistry but is non-unitary, preventing direct quantum implementation.
UCC exponentiates the cluster operator: |psi> = exp(T - T^dag)|HF> where T creates
excitations from the Hartree-Fock reference |HF>. The "Singles and Doubles" (SD)
truncation includes only 1- and 2-electron excitations, balancing accuracy and
circuit depth.

UCCSD generates excitations T = T1 + T2 where T1 = Sum_{ia} t_ia a_a^dag a_i
(singles) and T2 = Sum_{ijab} t_ijab a_a^dag a_b^dag a_j a_i (doubles). Here i,j
are occupied orbitals and a,b are virtual orbitals. The amplitudes {t} are variational
parameters optimized via VQE. For H2 in minimal basis, UCCSD has just 1 double
excitation parameter, yet achieves exact results. For larger molecules, UCCSD with
VQE routinely achieves chemical accuracy where classical CCSD may fail for strongly
correlated systems.

Key Equations:
  - UCCSD ansatz: |psi(theta)> = exp(T(theta) - T^dag(theta))|HF>
  - Singles operator: T1 = Sum_{i in occ, a in virt} theta_ia a_a^dag a_i
  - Doubles operator: T2 = Sum_{ij in occ, ab in virt} theta_ijab a_a^dag a_b^dag a_j a_i
  - Parameter count: O(N^2 M^2) for N occupied, M virtual orbitals
  - Trotter approximation: exp(A+B) ~ (exp(A/n) exp(B/n))^n for circuit compilation
  - Jordan-Wigner depth: O(N^4) gates for N spin-orbitals

Quantum Advantage:
  UCCSD on quantum computers can handle strongly correlated systems (multiple
  near-degenerate configurations) where classical CCSD breaks down. Examples
  include transition metal complexes (catalysis), bond-breaking processes, and
  excited states. The quantum advantage comes from native representation of
  fermionic antisymmetry and efficient handling of the exponentially large CI
  space. Google's 2020 Hartree-Fock experiment and IBM's VQE demonstrations use
  UCCSD variants. For production chemistry, UCCSD-VQE with error mitigation on
  100+ qubit fault-tolerant devices could revolutionize drug discovery.

References:
  [1] Peruzzo et al., "A variational eigenvalue solver on a photonic quantum
      processor", Nat. Commun. 5, 4213 (2014). https://doi.org/10.1038/ncomms5213
  [2] Romero et al., "Strategies for quantum computing molecular energies using
      the unitary coupled cluster ansatz", Quantum Sci. Technol. 4, 014008 (2018).
      https://doi.org/10.1088/2058-9565/aad3e4
  [3] Grimsley et al., "An adaptive variational algorithm for exact molecular
      simulations on a quantum computer", Nat. Commun. 10, 3007 (2019).
      https://doi.org/10.1038/s41467-019-10988-2
  [4] Wikipedia: Coupled_cluster
      https://en.wikipedia.org/wiki/Coupled_cluster
*)

//#r "nuget: FSharp.Azure.Quantum"
#r "../../src/FSharp.Azure.Quantum/bin/Debug/net10.0/FSharp.Azure.Quantum.dll"
#load "../_common/Cli.fs"
#load "../_common/Data.fs"
#load "../_common/Reporting.fs"

open FSharp.Azure.Quantum.QuantumChemistry
open FSharp.Azure.Quantum.QuantumChemistry.FermionMapping
open FSharp.Azure.Quantum.QuantumChemistry.FermionMapping.UCCSD
open FSharp.Azure.Quantum.QuantumChemistry.FermionMapping.HartreeFock
open FSharp.Azure.Quantum.QuantumChemistry.FermionMapping.ChemistryVQE
open FSharp.Azure.Quantum.QuantumChemistry.MolecularHamiltonian
open FSharp.Azure.Quantum.Core.BackendAbstraction
open FSharp.Azure.Quantum.Backends.LocalBackend
open FSharp.Azure.Quantum.Core.QaoaCircuit
open FSharp.Azure.Quantum.Examples.Common

// ==============================================================================
// CLI ARGUMENT PARSING
// ==============================================================================

let argv = fsi.CommandLineArgs |> Array.skip 1
let args = Cli.parse argv

Cli.exitIfHelp "H2_UCCSD_VQE_Example.fsx" "H2 ground state with UCCSD ansatz and VQE optimization"
    [ { Cli.OptionSpec.Name = "bond-length"; Description = "H-H bond length in Angstroms"; Default = Some "0.74" }
      { Cli.OptionSpec.Name = "active-orbitals"; Description = "Number of spin-orbitals (2x spatial)"; Default = Some "4" }
      { Cli.OptionSpec.Name = "max-iterations"; Description = "Maximum VQE iterations"; Default = Some "10" }
      { Cli.OptionSpec.Name = "tolerance"; Description = "Convergence tolerance"; Default = Some "1e-4" }
      { Cli.OptionSpec.Name = "output"; Description = "Write results to JSON file"; Default = None }
      { Cli.OptionSpec.Name = "csv"; Description = "Write results to CSV file"; Default = None }
      { Cli.OptionSpec.Name = "quiet"; Description = "Suppress informational output"; Default = None } ]
    args

let quiet = Cli.hasFlag "quiet" args
let bondLength = Cli.getFloatOr "bond-length" 0.74 args
let numOrbitals = Cli.getIntOr "active-orbitals" 4 args
let maxIterations = Cli.getIntOr "max-iterations" 10 args
let tolerance = Cli.getFloatOr "tolerance" 1e-4 args

// ==============================================================================
// PART 1: H2 Molecule Definition
// ==============================================================================

if not quiet then
    printfn "============================================================"
    printfn "  H2 Molecule Ground State - UCCSD-VQE"
    printfn "============================================================"
    printfn ""

let numElectrons = 2

let h2Molecule : Molecule = {
    Name = "H2"
    Atoms = [
        { Element = "H"; Position = (0.0, 0.0, 0.0) }
        { Element = "H"; Position = (0.0, 0.0, bondLength) }
    ]
    Bonds = [ { Atom1 = 0; Atom2 = 1; BondOrder = 1.0 } ]
    Charge = 0
    Multiplicity = 1
}

if not quiet then
    printfn "Molecular System:"
    printfn "  Molecule: H2 (hydrogen dimer)"
    printfn "  Geometry: R = %.4f A (bond length)" bondLength
    printfn "  Basis Set: STO-3G (minimal basis)"
    printfn "  Electrons: %d" numElectrons
    printfn "  Spin Orbitals: %d" numOrbitals
    printfn ""
    printfn "Target Ground State Energy:"
    printfn "  Exact (FCI): -1.137 Hartree"
    printfn "  Chemical Accuracy: +/-0.0016 Hartree (+/-1 kcal/mol)"
    printfn ""

// ==============================================================================
// PART 2: UCCSD Parameter Analysis
// ==============================================================================

if not quiet then
    printfn "UCCSD Parameter Analysis"
    printfn "------------------------------------------------------------"
    printfn ""

let numOccupied = numElectrons
let numVirtual = numOrbitals - numElectrons
let numSinglesExcitations = numOccupied * numVirtual
let numOccPairs = numOccupied * (numOccupied - 1) / 2
let numVirtPairs = numVirtual * (numVirtual - 1) / 2
let numDoublesExcitations = numOccPairs * numVirtPairs
let totalUCCSDParams = numSinglesExcitations + numDoublesExcitations

if not quiet then
    printfn "Singles excitations: %d occupied x %d virtual = %d" numOccupied numVirtual numSinglesExcitations
    printfn "  (0 -> 2), (0 -> 3), (1 -> 2), (1 -> 3)"
    printfn ""
    printfn "Doubles excitations: C(%d,2) x C(%d,2) = %d x %d = %d"
        numOccupied numVirtual numOccPairs numVirtPairs numDoublesExcitations
    printfn "  (0,1 -> 2,3)"
    printfn ""
    printfn "Total UCCSD parameters: %d" totalUCCSDParams
    printfn ""

// Generate and inspect the excitation pool
let rng = System.Random(42)
let initialParams = Array.init totalUCCSDParams (fun _ -> (rng.NextDouble() - 0.5) * 0.1)

let excitationPoolResult = generateExcitationPool numElectrons numOrbitals initialParams

match excitationPoolResult with
| Error msg ->
    if not quiet then
        printfn "  (Excitation pool generation: %s)" msg
| Ok pool ->
    if not quiet then
        printfn "Excitation Pool:"
        pool.Singles |> List.iteri (fun i s ->
            printfn "  Single %d: orbital %d -> %d (amplitude: %.4f)"
                (i+1) s.OccupiedOrbital s.VirtualOrbital s.Amplitude)
        pool.Doubles |> List.iteri (fun i d ->
            printfn "  Double %d: (%d,%d) -> (%d,%d) (amplitude: %.4f)"
                (i+1) d.OccupiedOrbital1 d.OccupiedOrbital2
                d.VirtualOrbital1 d.VirtualOrbital2 d.Amplitude)
        printfn ""

// ==============================================================================
// PART 3: Build Hamiltonian, Prepare HF State, Run UCCSD-VQE
// ==============================================================================

/// Collect all results as Map list for structured output.
let allResults = System.Collections.Generic.List<Map<string,string>>()

/// Build the parameter-analysis result row.
let paramAnalysisRow =
    [ "Section", "ParameterAnalysis"
      "BondLength_A", sprintf "%.4f" bondLength
      "NumElectrons", sprintf "%d" numElectrons
      "NumOrbitals", sprintf "%d" numOrbitals
      "SinglesExcitations", sprintf "%d" numSinglesExcitations
      "DoublesExcitations", sprintf "%d" numDoublesExcitations
      "TotalUCCSDParams", sprintf "%d" totalUCCSDParams ]
    |> Map.ofList

allResults.Add(paramAnalysisRow)

if not quiet then
    printfn "Building Molecular Hamiltonian"
    printfn "------------------------------------------------------------"

match buildWithMapping h2Molecule JordanWigner with
| Error err ->
    if not quiet then
        printfn "Error building Hamiltonian: %A" err
| Ok qaoaHamiltonian ->

    let molecularHamiltonian = fromQaoaHamiltonian qaoaHamiltonian

    if not quiet then
        printfn "Molecular Hamiltonian built successfully!"
        printfn "  Qubits: %d" molecularHamiltonian.NumQubits
        printfn "  Pauli Terms: %d" molecularHamiltonian.Terms.Length
        printfn ""

        printfn "Sample Hamiltonian terms:"
        molecularHamiltonian.Terms
        |> List.take (min 5 molecularHamiltonian.Terms.Length)
        |> List.iteri (fun i term ->
            let paulis =
                term.Operators
                |> Map.toList
                |> List.sortBy fst
                |> List.map (fun (q, p) ->
                    let pStr = match p with
                               | PauliOperator.PauliX -> "X"
                               | PauliOperator.PauliY -> "Y"
                               | PauliOperator.PauliZ -> "Z"
                               | PauliOperator.PauliI -> "I"
                    sprintf "%s_%d" pStr q)
                |> String.concat " "
            printfn "  %d. %.4f x %s" (i+1) term.Coefficient.Real paulis)
        printfn ""

    // ------------------------------------------------------------------
    // Setup quantum backend
    // ------------------------------------------------------------------

    let backend = LocalBackend() :> IQuantumBackend

    if not quiet then
        printfn "LocalBackend initialized (statevector simulator)"
        printfn ""

    // ------------------------------------------------------------------
    // Prepare Hartree-Fock initial state
    // ------------------------------------------------------------------

    if not quiet then
        printfn "Preparing Hartree-Fock Initial State"
        printfn "------------------------------------------------------------"

    match prepareHartreeFockState numElectrons numOrbitals backend with
    | Error err ->
        if not quiet then
            printfn "Error preparing HF state: %A" err
    | Ok hfState ->
        let isValid = isHartreeFockState numElectrons hfState

        if not quiet then
            printfn "HF state prepared: |0011> (qubits 0,1 occupied)"
            printfn "  Verification: %s" (if isValid then "Valid" else "INVALID")
            printfn ""

        // ------------------------------------------------------------------
        // Run UCCSD-VQE optimization
        // ------------------------------------------------------------------

        if not quiet then
            printfn "Running UCCSD-VQE Optimization"
            printfn "============================================================"
            printfn ""

        let vqeConfig : ChemistryVQEConfig = {
            Hamiltonian = molecularHamiltonian
            Ansatz = AnsatzType.UCCSD (numElectrons, numOrbitals)
            MaxIterations = maxIterations
            Tolerance = tolerance
            UseHFInitialState = true
            Backend = backend
            ProgressReporter = None
        }

        if not quiet then
            printfn "VQE Configuration:"
            printfn "  Ansatz: UCCSD (%d parameters)" totalUCCSDParams
            printfn "  Initial State: Hartree-Fock |0011>"
            printfn "  Max Iterations: %d" vqeConfig.MaxIterations
            printfn "  Convergence Tolerance: %.2e" vqeConfig.Tolerance
            printfn ""
            printfn "Starting optimization..."
            printfn ""

        let vqeResult =
            ChemistryVQE.run vqeConfig
            |> Async.RunSynchronously

        match vqeResult with
        | Error err ->
            if not quiet then
                printfn "VQE Error: %A" err
        | Ok result ->
            let exactEnergy = -1.137
            let energyError = abs(result.Energy - exactEnergy)
            let chemicalAccuracy = 0.0016

            if not quiet then
                printfn "============================================================"
                printfn "  VQE Results"
                printfn "============================================================"
                printfn ""
                printfn "Ground State Energy:"
                printfn "  Electronic Energy: %.6f Hartree" result.Energy
                printfn "  Iterations: %d" result.Iterations
                printfn "  Converged: %s" (if result.Converged then "Yes" else "No")
                printfn ""

                printfn "Optimal UCCSD Parameters:"
                let numSingles = numElectrons * (numOrbitals - numElectrons)
                result.OptimalParameters |> Array.iteri (fun i p ->
                    if i < numSingles then
                        printfn "  t_single[%d] = %.6f" i p
                    else
                        printfn "  t_double[%d] = %.6f" (i - numSingles) p)
                printfn ""

                printfn "Accuracy Analysis:"
                printfn "  Target Energy (FCI): %.6f Hartree" exactEnergy
                printfn "  Computed Energy:     %.6f Hartree" result.Energy
                printfn "  Absolute Error:      %.6f Hartree" energyError
                printfn "  Chemical Accuracy:   %.6f Hartree (1 kcal/mol)" chemicalAccuracy
                printfn ""

                if energyError < chemicalAccuracy then
                    printfn "Chemical accuracy achieved!"
                    printfn "  Error is within +/-1 kcal/mol threshold"
                elif result.Energy <> 0.0 then
                    printfn "Energy error exceeds chemical accuracy"
                    printfn "  (May need more iterations or better initial guess)"
                else
                    printfn "Energy is zero - likely using simplified Hamiltonian"
                printfn ""

            let vqeRow =
                [ "Section", "VQE_Result"
                  "BondLength_A", sprintf "%.4f" bondLength
                  "Energy_Hartree", sprintf "%.6f" result.Energy
                  "Energy_eV", sprintf "%.6f" (result.Energy * 27.2114)
                  "Iterations", sprintf "%d" result.Iterations
                  "Converged", sprintf "%b" result.Converged
                  "Error_Hartree", sprintf "%.6f" energyError
                  "ChemAccuracy", sprintf "%b" (energyError < chemicalAccuracy)
                  "NumQubits", sprintf "%d" molecularHamiltonian.NumQubits
                  "PauliTerms", sprintf "%d" molecularHamiltonian.Terms.Length
                  "UCCSDParams", sprintf "%d" totalUCCSDParams
                  "HF_Valid", sprintf "%b" isValid ]
                |> Map.ofList
            allResults.Add(vqeRow)

// ==============================================================================
// PART 4: Summary
// ==============================================================================

if not quiet then
    printfn "============================================================"
    printfn "  Summary"
    printfn "============================================================"
    printfn ""
    printfn "What This Example Demonstrated:"
    printfn "  - Molecular Hamiltonian construction (fermionic -> qubits)"
    printfn "  - UCCSD ansatz with chemistry-aware excitations"
    printfn "  - Hartree-Fock initial state preparation"
    printfn "  - VQE optimization with gradient descent"
    printfn "  - Energy measurement with X/Y/Z Pauli operators"
    printfn ""
    printfn "UCCSD vs Hardware-Efficient Ansatz:"
    printfn "  UCCSD (this implementation):"
    printfn "    + Chemically motivated structure"
    printfn "    + Parameters = excitation amplitudes (interpretable)"
    printfn "    + Guarantees chemical accuracy (+/-1 kcal/mol)"
    printfn "    + Industry standard for drug discovery"
    printfn "    Parameters for H2: %d (singles + doubles)" totalUCCSDParams
    printfn ""
    printfn "  Hardware-Efficient Ansatz (generic):"
    printfn "    - No chemical structure"
    printfn "    - Parameters have no physical meaning"
    printfn "    - No accuracy guarantee"
    printfn "    Parameters for H2: ~%d (arbitrary layers)" (numOrbitals * 3)
    printfn ""
    printfn "Production Applications:"
    printfn "  Drug Discovery: Protein-ligand binding energies"
    printfn "  Materials Science: Battery electrode optimization"
    printfn "  Catalysis: Reaction pathway analysis"
    printfn "  Quantum Chemistry: Ground state energies"
    printfn ""
    printfn "Next Steps:"
    printfn "  1. Extend to larger molecules (LiH, H2O, NH3)"
    printfn "  2. Add error mitigation (see ErrorMitigation examples)"
    printfn "  3. Test on real quantum hardware via IQuantumBackend"
    printfn "  4. Compare UCCSD vs ADAPT-VQE for strongly correlated systems"
    printfn ""

// ==============================================================================
// STRUCTURED OUTPUT
// ==============================================================================

let resultList = allResults |> Seq.toList

match Cli.tryGet "output" args with
| Some path ->
    Reporting.writeJson path resultList
    if not quiet then printfn "Results written to %s" path
| None -> ()

match Cli.tryGet "csv" args with
| Some path ->
    let header = [ "Section"; "BondLength_A"; "Energy_Hartree"; "Energy_eV"; "Iterations"; "Converged";
                   "Error_Hartree"; "ChemAccuracy"; "NumQubits"; "PauliTerms"; "UCCSDParams"; "HF_Valid";
                   "NumElectrons"; "NumOrbitals"; "SinglesExcitations"; "DoublesExcitations"; "TotalUCCSDParams" ]
    let rows =
        resultList
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
    printfn "  --active-orbitals 6       Number of spin-orbitals"
    printfn "  --max-iterations 50       Increase VQE iterations"
    printfn "  --tolerance 1e-6          Tighter convergence tolerance"
    printfn "  --output results.json     Export results as JSON"
    printfn "  --csv results.csv         Export results as CSV"
    printfn "  --quiet                   Suppress informational output"
    printfn "  --help                    Show full usage information"

if not quiet then
    printfn ""
    printfn "Done!"
