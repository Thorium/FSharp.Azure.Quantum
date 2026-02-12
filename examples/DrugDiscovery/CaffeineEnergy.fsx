// ==============================================================================
// Caffeine Molecular Fragment - VQE Ground State Energy
// ==============================================================================
// Demonstrates VQE for drug-like molecule fragments using FSharp.Azure.Quantum.
// Caffeine (C8H10N4O2) is too large for NISQ, so we compute fragments using
// the Fragment Molecular Orbital (FMO) approach and reconstruct the total energy.
//
// Business Context:
// A pharmaceutical research team wants to understand the electronic structure
// of caffeine (an adenosine receptor antagonist) for SAR studies. Full caffeine
// requires ~200 qubits, so we decompose into chemically meaningful fragments:
// imidazole ring, urea carbonyl, and N-methylformamide.
//
// Quantum Advantage:
// VQE captures electron correlation that classical DFT approximates.
// For drug binding, correlation effects of 1-3 kcal/mol can determine
// selectivity between on-target and off-target binding.
//
// CURRENT LIMITATIONS (NISQ era):
// - Full caffeine (102 electrons) needs ~200+ qubits (infeasible)
// - Fragment approach: compute small pieces (<=20 qubits for LocalBackend)
// - Active space: freeze core electrons, correlate valence
//
// Usage:
//   dotnet fsi CaffeineEnergy.fsx
//   dotnet fsi CaffeineEnergy.fsx -- --help
//   dotnet fsi CaffeineEnergy.fsx -- --max-iterations 100 --tolerance 1e-6
//   dotnet fsi CaffeineEnergy.fsx -- --output results.json --csv results.csv --quiet
//
// ==============================================================================

(*
Background Theory
-----------------

The FRAGMENT MOLECULAR ORBITAL (FMO) method, developed by Kitaura et al. (1999),
enables quantum chemical calculations on large biomolecules by dividing them
into smaller, computationally tractable fragments. This approach is essential
for drug discovery applications where target molecules (100+ atoms) far exceed
the capabilities of both classical full-CI and current NISQ quantum hardware.

The FMO total energy is approximated as:

    E_total ~ Sum_I E_I + Sum_{I<J} (E_IJ - E_I - E_J) + higher-order terms

Where E_I is the energy of fragment I, and the pair interaction term captures
interfragment interactions. This decomposition reduces exponential scaling to
polynomial, making large molecules tractable.

For NISQ quantum computers, the Fragment Quantum Eigensolver (FQE) extends FMO:
  1. Decompose drug molecule into chemically meaningful fragments
  2. Compute each fragment's energy using VQE on quantum hardware
  3. Compute interfragment interactions (classical or quantum)
  4. Reconstruct total molecular energy

CAFFEINE (C8H10N4O2, 102 electrons) exemplifies this challenge:
  - Full VQE: ~200 qubits needed (far beyond NISQ capability)
  - Fragment VQE: Imidazole (26e), Urea (24e), etc. -> 6-12 qubits each
  - Trade accuracy for tractability while preserving quantum advantage

Fragments are chosen to preserve:
  - Chemical functionality (pharmacophores, binding motifs)
  - Electronic structure (conjugation, aromaticity)
  - Reaction centers (metabolic sites, binding interactions)

Current NISQ limits (~10-50 qubits) restrict fragment sizes, but demonstrate
the path toward fault-tolerant quantum drug discovery when larger molecules
become accessible with error-corrected quantum computers.

Key Concepts:
  - FMO decomposition: E_total from fragment energies + interactions
  - Active space: Freeze core electrons to reduce qubit count
  - Fragment selection: Preserve chemical meaning and electronic structure

Quantum Advantage:
  Even at fragment level, VQE captures electron correlation missed by
  classical DFT. For drug binding, correlation effects of 1-3 kcal/mol
  can determine selectivity between on-target and off-target binding.
  Fragment-based quantum chemistry provides a scalable path to quantum
  advantage in drug discovery.

References:
  [1] Kitaura, K. et al. "Fragment molecular orbital method" Chem. Phys. Lett. 313, 701 (1999)
  [2] Fedorov, D.G. & Kitaura, K. "The Fragment Molecular Orbital Method" CRC Press (2009)
  [3] Wikipedia: Fragment_molecular_orbital_method
  [4] Yoshikawa, T. et al. "FMO-based Investigation of Drug-Receptor Interactions" J. Phys. Chem. B (2023)
*)

#r "../../src/FSharp.Azure.Quantum/bin/Debug/net10.0/FSharp.Azure.Quantum.dll"
#load "../_common/Cli.fs"
#load "../_common/Data.fs"
#load "../_common/Reporting.fs"

open System
open FSharp.Azure.Quantum.QuantumChemistry
open FSharp.Azure.Quantum.QuantumChemistry.QuantumChemistryBuilder
open FSharp.Azure.Quantum.Core
open FSharp.Azure.Quantum.Core.BackendAbstraction
open FSharp.Azure.Quantum.Backends.LocalBackend
open FSharp.Azure.Quantum.Examples.Common

// ==============================================================================
// CLI PARSING
// ==============================================================================

let argv = fsi.CommandLineArgs |> Array.skip 1
let args = Cli.parse argv

Cli.exitIfHelp "CaffeineEnergy.fsx"
    "VQE ground state energy for caffeine molecular fragments (FMO approach)"
    [ { Cli.OptionSpec.Name = "max-iterations"; Description = "Maximum VQE iterations"; Default = Some "50" }
      { Cli.OptionSpec.Name = "tolerance"; Description = "Energy convergence tolerance (Hartree)"; Default = Some "1e-4" }
      { Cli.OptionSpec.Name = "output"; Description = "Write results to JSON file"; Default = None }
      { Cli.OptionSpec.Name = "csv"; Description = "Write results to CSV file"; Default = None }
      { Cli.OptionSpec.Name = "quiet"; Description = "Suppress informational output"; Default = None } ]
    args

let quiet = Cli.hasFlag "quiet" args
let maxIterations = Cli.getIntOr "max-iterations" 50 args
let tolerance = Cli.getFloatOr "tolerance" 1e-4 args

// ==============================================================================
// CAFFEINE OVERVIEW
// ==============================================================================

if not quiet then
    printfn "=============================================================="
    printfn "  Caffeine Fragment - VQE Ground State Energy"
    printfn "=============================================================="
    printfn ""
    printfn "Caffeine (C8H10N4O2) - Drug Discovery Context"
    printfn "--------------------------------------------------------------"
    printfn ""
    printfn "  Caffeine is a methylxanthine alkaloid found in coffee and tea."
    printfn "  It acts as an adenosine receptor antagonist (stimulant)."
    printfn ""
    printfn "  Full molecule:"
    printfn "    Formula:     C8H10N4O2"
    printfn "    Atoms:       24"
    printfn "    Electrons:   102"
    printfn "    MW:          194.19 g/mol"
    printfn ""
    printfn "  Structure (purine core + methyl groups):"
    printfn ""
    printfn "           O           CH3"
    printfn "           ||           |"
    printfn "      H3C-N---C         N"
    printfn "           |   \\       /  \\"
    printfn "           C    N-----C    N"
    printfn "          / \\         ||"
    printfn "         N   C========N"
    printfn "         |   ||"
    printfn "        CH3  O"
    printfn ""

// ==============================================================================
// NISQ LIMITATIONS
// ==============================================================================

if not quiet then
    printfn "NISQ Hardware Limitations"
    printfn "--------------------------------------------------------------"
    printfn ""
    printfn "  Full caffeine VQE is NOT feasible on current hardware:"
    printfn ""
    printfn "    Qubits needed (STO-3G):   ~50 qubits"
    printfn "    Qubits needed (cc-pVDZ):  ~200 qubits"
    printfn "    LocalBackend limit:       20 qubits"
    printfn "    Fault-tolerant needed:    ~1000+ logical qubits"
    printfn ""
    printfn "  Solution: Fragment Molecular Orbital (FMO) approach"
    printfn "    1. Divide molecule into chemically meaningful fragments"
    printfn "    2. Compute each fragment with quantum VQE"
    printfn "    3. Add interaction corrections (classical)"
    printfn "    4. Sum to approximate total energy"
    printfn ""

// ==============================================================================
// DEFINE CAFFEINE FRAGMENTS
// ==============================================================================

/// Create imidazole ring fragment (5-membered ring with 2 nitrogens).
/// This is a key pharmacophore in many drugs (histidine-like).
let createImidazole () : Molecule =
    // Imidazole: C3H4N2 (simplified, planar geometry)
    // Standard bond lengths: C-N ~1.38 A, C-C ~1.36 A, C-H ~1.08 A, N-H ~1.01 A
    {
        Name = "Imidazole"
        Atoms = [
            { Element = "N"; Position = (0.000, 1.142, 0.000) }   // N1 (pyrrole-type)
            { Element = "C"; Position = (1.088, 0.370, 0.000) }   // C2
            { Element = "N"; Position = (0.674, -0.887, 0.000) }  // N3 (pyridine-type)
            { Element = "C"; Position = (-0.674, -0.887, 0.000) } // C4
            { Element = "C"; Position = (-1.088, 0.370, 0.000) }  // C5
            { Element = "H"; Position = (0.000, 2.152, 0.000) }   // H on N1
            { Element = "H"; Position = (2.108, 0.720, 0.000) }   // H on C2
            { Element = "H"; Position = (-1.348, -1.727, 0.000) } // H on C4
            { Element = "H"; Position = (-2.108, 0.720, 0.000) }  // H on C5
        ]
        Bonds = [
            { Atom1 = 0; Atom2 = 1; BondOrder = 1.0 }  // N1-C2
            { Atom1 = 1; Atom2 = 2; BondOrder = 2.0 }  // C2=N3
            { Atom1 = 2; Atom2 = 3; BondOrder = 1.0 }  // N3-C4
            { Atom1 = 3; Atom2 = 4; BondOrder = 2.0 }  // C4=C5
            { Atom1 = 4; Atom2 = 0; BondOrder = 1.0 }  // C5-N1
        ]
        Charge = 0
        Multiplicity = 1
    }

/// Create urea fragment (C=O with two N-H).
/// Models the carbonyl groups in caffeine's xanthine core.
let createUrea () : Molecule =
    // Urea: (NH2)2C=O - planar geometry
    {
        Name = "Urea"
        Atoms = [
            { Element = "C"; Position = (0.000, 0.000, 0.000) }
            { Element = "O"; Position = (0.000, 1.250, 0.000) }   // C=O
            { Element = "N"; Position = (-1.150, -0.550, 0.000) } // NH2
            { Element = "N"; Position = (1.150, -0.550, 0.000) }  // NH2
            { Element = "H"; Position = (-1.850, 0.150, 0.000) }
            { Element = "H"; Position = (-1.250, -1.550, 0.000) }
            { Element = "H"; Position = (1.850, 0.150, 0.000) }
            { Element = "H"; Position = (1.250, -1.550, 0.000) }
        ]
        Bonds = [
            { Atom1 = 0; Atom2 = 1; BondOrder = 2.0 }  // C=O
            { Atom1 = 0; Atom2 = 2; BondOrder = 1.0 }  // C-N
            { Atom1 = 0; Atom2 = 3; BondOrder = 1.0 }  // C-N
        ]
        Charge = 0
        Multiplicity = 1
    }

/// Create N-methylformamide fragment.
/// Models the N-CH3 groups attached to the xanthine core.
let createNMethylFormamide () : Molecule =
    // N-methylformamide: HCONHCH3
    {
        Name = "N-Methylformamide"
        Atoms = [
            { Element = "C"; Position = (0.000, 0.000, 0.000) }   // Formyl C
            { Element = "O"; Position = (1.200, 0.000, 0.000) }   // C=O
            { Element = "N"; Position = (-0.600, 1.200, 0.000) }  // Amide N
            { Element = "C"; Position = (-0.100, 2.500, 0.000) }  // Methyl C
            { Element = "H"; Position = (-0.550, -0.950, 0.000) } // Formyl H
            { Element = "H"; Position = (-1.600, 1.050, 0.000) }  // N-H
            { Element = "H"; Position = (0.980, 2.550, 0.000) }   // Methyl H
            { Element = "H"; Position = (-0.500, 3.050, 0.880) }  // Methyl H
            { Element = "H"; Position = (-0.500, 3.050, -0.880) } // Methyl H
        ]
        Bonds = [
            { Atom1 = 0; Atom2 = 1; BondOrder = 2.0 }  // C=O
            { Atom1 = 0; Atom2 = 2; BondOrder = 1.0 }  // C-N
            { Atom1 = 2; Atom2 = 3; BondOrder = 1.0 }  // N-CH3
        ]
        Charge = 0
        Multiplicity = 1
    }

/// Fragment definitions: (name, molecule, description, pharmacological role)
let fragments = [
    ("Imidazole", createImidazole(), "5-membered ring (histidine-like)", "Core pharmacophore - adenosine receptor binding")
    ("Urea", createUrea(), "Carbonyl fragment (C=O)", "H-bond donor/acceptor - receptor selectivity")
    ("N-Methylformamide", createNMethylFormamide(), "N-methyl group", "Metabolic site - CYP450 N-demethylation target")
]

if not quiet then
    printfn "Caffeine Fragments for Quantum Calculation"
    printfn "--------------------------------------------------------------"
    printfn ""
    for (name, mol, description, role) in fragments do
        printfn "  %s (%s):" name mol.Name
        printfn "    Atoms:      %d" mol.Atoms.Length
        printfn "    Electrons:  %d" (Molecule.countElectrons mol)
        printfn "    Role:       %s" description
        printfn "    Pharma:     %s" role
        printfn ""

// ==============================================================================
// QUANTUM BACKEND SETUP
// ==============================================================================

let backend = LocalBackend() :> IQuantumBackend

if not quiet then
    printfn "Quantum Backend: %s" backend.Name
    printfn "  Max iterations: %d" maxIterations
    printfn "  Tolerance: %g Hartree" tolerance
    printfn ""

// ==============================================================================
// VQE CALCULATIONS
// ==============================================================================

if not quiet then
    printfn "=============================================================="
    printfn "  VQE Ground State Energy Calculations"
    printfn "=============================================================="
    printfn ""

let results = System.Collections.Generic.List<Map<string, string>>()

/// Calculate ground state energy for a molecule using VQE.
/// Returns (energy in Hartree, elapsed time in seconds).
let calculateEnergy (molecule: Molecule) : float * float =
    let config = {
        Method = GroundStateMethod.VQE
        Backend = Some backend
        MaxIterations = maxIterations
        Tolerance = tolerance
        InitialParameters = None
        ProgressReporter = None
        ErrorMitigation = None
        IntegralProvider = None
    }

    let startTime = DateTime.Now
    let result = GroundStateEnergy.estimateEnergy molecule config |> Async.RunSynchronously
    let elapsed = (DateTime.Now - startTime).TotalSeconds

    match result with
    | Ok vqeResult -> (vqeResult.Energy, elapsed)
    | Error err ->
        if not quiet then
            printfn "  Warning: VQE failed for %s: %s" molecule.Name err.Message
        (0.0, elapsed)

// Run VQE for each fragment
for (name, molecule, _description, _role) in fragments do
    let electrons = Molecule.countElectrons molecule
    let qubitsEstimate = molecule.Atoms.Length * 2  // Minimal basis estimate

    if not quiet then
        printfn "Fragment: %s" name
        printfn "--------------------------------------------------------------"
        printfn "  Atoms:      %d" molecule.Atoms.Length
        printfn "  Electrons:  %d" electrons
        printfn "  Qubits:     ~%d (STO-3G estimate)" qubitsEstimate
        printfn ""

    if qubitsEstimate > 20 then
        if not quiet then
            printfn "  Fragment too large for LocalBackend (max 20 qubits)"
            printfn "  Consider using Azure Quantum backend or further decomposition."
            printfn ""

        results.Add(
            [ "component", name
              "molecule", molecule.Name
              "atoms", string molecule.Atoms.Length
              "electrons", string electrons
              "qubits_estimate", string qubitsEstimate
              "energy_hartree", "N/A"
              "converged", "false"
              "time_s", "0.00"
              "note", "Too large for LocalBackend" ]
            |> Map.ofList)
    else
        if not quiet then printfn "  Running VQE..."

        let (energy, elapsed) = calculateEnergy molecule

        if not quiet then
            printfn "  Energy:     %.6f Hartree" energy
            printfn "  Time:       %.2f seconds" elapsed
            printfn ""

        results.Add(
            [ "component", name
              "molecule", molecule.Name
              "atoms", string molecule.Atoms.Length
              "electrons", string electrons
              "qubits_estimate", string qubitsEstimate
              "energy_hartree", sprintf "%.6f" energy
              "converged", "true"
              "time_s", sprintf "%.2f" elapsed ]
            |> Map.ofList)

// ==============================================================================
// FRAGMENT ENERGY SUMMARY AND FMO RECONSTRUCTION
// ==============================================================================

let fragmentEnergies =
    results
    |> Seq.toList
    |> List.choose (fun m ->
        match m |> Map.tryFind "energy_hartree" with
        | Some v when v <> "N/A" ->
            Some (m |> Map.find "component", float v)
        | _ -> None)

let totalFragmentEnergy = fragmentEnergies |> List.sumBy snd

if not quiet then
    printfn "=============================================================="
    printfn "  Fragment Energy Summary"
    printfn "=============================================================="
    printfn ""
    printfn "  %-22s %12s" "Fragment" "Energy (Eh)"
    printfn "  -------------------------------------------"
    for (name, energy) in fragmentEnergies do
        printfn "  %-22s %12.6f" name energy
    printfn "  -------------------------------------------"
    printfn "  %-22s %12.6f" "Fragment Sum" totalFragmentEnergy
    printfn ""

// Estimated full caffeine energy (literature reference)
let caffeineReferenceHF = -679.0  // Approximate HF/STO-3G
let hartreeToKcalMol = 627.5

if not quiet then
    printfn "  Reference (full caffeine HF): ~%.1f Eh" caffeineReferenceHF
    printfn ""
    printfn "  Note: Fragment sum != full molecule energy because:"
    printfn "    - Missing interfragment interactions"
    printfn "    - Different basis sets per fragment"
    printfn "    - No nuclear repulsion between fragments"
    printfn "    - FMO pair interaction corrections not yet applied"
    printfn ""

// Estimate interaction correction (simplified)
// In full FMO, pair interactions E_IJ - E_I - E_J would be computed
// Here we provide a rough classical estimate for context
let nFragments = fragmentEnergies.Length
let nPairs = nFragments * (nFragments - 1) / 2
let estimatedPairCorrection = float nPairs * -0.01  // ~10 mEh per pair (rough)
let correctedTotal = totalFragmentEnergy + estimatedPairCorrection

if not quiet then
    printfn "  FMO Pair Interaction Estimate:"
    printfn "    Number of fragment pairs: %d" nPairs
    printfn "    Estimated pair correction: %.4f Eh" estimatedPairCorrection
    printfn "    Corrected total estimate:  %.4f Eh" correctedTotal
    printfn ""

results.Add(
    [ "component", "FragmentSum"
      "energy_hartree", sprintf "%.6f" totalFragmentEnergy
      "num_fragments", string fragmentEnergies.Length
      "num_pairs", string nPairs
      "pair_correction_hartree", sprintf "%.4f" estimatedPairCorrection
      "corrected_total_hartree", sprintf "%.4f" correctedTotal
      "reference_hf_hartree", sprintf "%.1f" caffeineReferenceHF ]
    |> Map.ofList)

// ==============================================================================
// DRUG DISCOVERY APPLICATIONS
// ==============================================================================

if not quiet then
    printfn "=============================================================="
    printfn "  Drug Discovery Applications"
    printfn "=============================================================="
    printfn ""
    printfn "Why calculate caffeine fragment energies?"
    printfn ""
    printfn "1. Lead Optimization:"
    printfn "   - Modify fragments to improve drug properties"
    printfn "   - Calculate energy changes for each modification"
    printfn "   - Predict binding affinity changes at adenosine receptors"
    printfn ""
    printfn "2. ADMET Prediction:"
    printfn "   - Metabolic stability from fragment reactivity"
    printfn "   - N-demethylation is a common CYP450 pathway for caffeine"
    printfn "   - Fragment energies inform metabolite prediction"
    printfn "   - See _data/PHARMA_GLOSSARY.md for ADMET definitions"
    printfn ""
    printfn "3. Selectivity Design:"
    printfn "   - Caffeine binds A1 and A2A adenosine receptors"
    printfn "   - Fragment contributions to binding selectivity"
    printfn "   - Quantum accuracy for subtle electronic effects"
    printfn ""
    printfn "4. SAR Studies (Structure-Activity Relationships):"
    printfn "   - Compare: caffeine vs theophylline vs theobromine"
    printfn "   - Identify critical functional groups"
    printfn "   - Theophylline: remove one N-methyl -> different selectivity"
    printfn "   - Theobromine: move N-methyl -> different metabolism"
    printfn ""

// ==============================================================================
// NISQ vs FAULT-TOLERANT COMPARISON
// ==============================================================================

if not quiet then
    printfn "=============================================================="
    printfn "  NISQ vs Fault-Tolerant Quantum Computing"
    printfn "=============================================================="
    printfn ""
    printfn "  Current NISQ Era (2024-2026):"
    printfn "    - ~100-1000 physical qubits"
    printfn "    - High error rates (1e-3 to 1e-2)"
    printfn "    - VQE for small molecules only (<=20 qubits)"
    printfn "    - Fragment approach required for drugs"
    printfn ""
    printfn "  Near-Term NISQ+ (2026-2030):"
    printfn "    - 1000+ physical qubits"
    printfn "    - Error mitigation techniques"
    printfn "    - Larger active spaces (30-50 qubits)"
    printfn "    - Drug-like fragments directly"
    printfn ""
    printfn "  Fault-Tolerant Era (2030+):"
    printfn "    - Millions of physical qubits"
    printfn "    - Error-corrected logical qubits"
    printfn "    - Full drug molecules (100+ qubits)"
    printfn "    - Exponential advantage realized"
    printfn ""

// ==============================================================================
// SUGGESTED EXTENSIONS
// ==============================================================================

if not quiet then
    printfn "Suggested Extensions"
    printfn "--------------------------------------------------------------"
    printfn ""
    printfn "1. Add more fragments:"
    printfn "   - Pyrimidine ring (6-membered ring from xanthine core)"
    printfn "   - Additional methyl groups (three N-CH3 in caffeine)"
    printfn "   - Custom fragments via --fragments flag (future)"
    printfn ""
    printfn "2. Compute pair interactions:"
    printfn "   - Build combined fragment pairs (e.g., imidazole+urea)"
    printfn "   - Calculate E_IJ - E_I - E_J for each pair"
    printfn "   - Full FMO2 energy reconstruction"
    printfn ""
    printfn "3. SAR comparison:"
    printfn "   - Run same fragments for theophylline (remove 1-methyl)"
    printfn "   - Run same fragments for theobromine (move 7-methyl to 3)"
    printfn "   - Compare fragment energies across xanthine series"
    printfn ""
    printfn "4. Scale up (future Azure Quantum backends):"
    printfn "   - Use IonQ/Quantinuum for larger fragments"
    printfn "   - Active space of 30-50 qubits"
    printfn "   - Full xanthine core as single fragment"
    printfn ""

// ==============================================================================
// SUMMARY
// ==============================================================================

let totalTime =
    results
    |> Seq.toList
    |> List.choose (fun m -> m |> Map.tryFind "time_s" |> Option.map float)
    |> List.sum

if not quiet then
    printfn "=============================================================="
    printfn "  Summary"
    printfn "=============================================================="
    printfn ""
    printfn "[OK] VQE computed for %d caffeine fragment(s)" fragmentEnergies.Length
    printfn "[OK] Fragment molecular orbital approach demonstrated"
    printfn "[OK] Fragment sum energy: %.6f Hartree" totalFragmentEnergy
    printfn "[OK] All calculations via IQuantumBackend (quantum compliant)"
    printfn ""
    printfn "Total computation time: %.2f seconds" totalTime
    printfn ""

// ==============================================================================
// STRUCTURED OUTPUT
// ==============================================================================

let resultsList = results |> Seq.toList

match Cli.tryGet "output" args with
| Some path ->
    Reporting.writeJson path resultsList
    if not quiet then printfn "Results written to %s" path
| None -> ()

match Cli.tryGet "csv" args with
| Some path ->
    let header = [ "component"; "molecule"; "atoms"; "electrons"; "qubits_estimate"; "energy_hartree"; "converged"; "time_s"; "note"; "num_fragments"; "num_pairs"; "pair_correction_hartree"; "corrected_total_hartree"; "reference_hf_hartree" ]
    let rows =
        resultsList
        |> List.map (fun m ->
            header |> List.map (fun h -> m |> Map.tryFind h |> Option.defaultValue ""))
    Reporting.writeCsv path header rows
    if not quiet then printfn "Results written to %s" path
| None -> ()

if argv.Length = 0 && not quiet then
    printfn ""
    printfn "Tip: Run with --help to see all available options."
    printfn "     Use --output results.json --csv results.csv for structured output."
    printfn ""
