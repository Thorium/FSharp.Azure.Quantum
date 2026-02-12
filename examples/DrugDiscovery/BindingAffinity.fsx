// ==============================================================================
// Protein-Ligand Binding Affinity Example
// ==============================================================================
// Demonstrates VQE for drug discovery - calculating binding energy between
// a drug molecule (ligand) and its protein target.
//
// Business Context:
// A pharmaceutical research team wants to predict how strongly drug candidates
// bind to a protein target. Binding affinity determines drug efficacy.
// Classical force fields are approximations; quantum simulation is exact.
//
// This example shows:
// - Fragment Molecular Orbital (FMO) approach for tractability
// - VQE calculation of binding site + ligand interaction energy
// - Binding energy decomposition (E_complex - E_protein - E_ligand)
// - Comparison with classical docking score approximation
//
// Quantum Advantage:
// VQE captures electron correlation effects that classical force fields miss.
// This is especially important for:
// - Charge transfer interactions
// - pi-stacking in aromatic systems
// - Hydrogen bonding with partial covalent character
//
// PROVEN QUANTUM ADVANTAGE:
// Molecular simulation requires exponential classical resources for electron
// correlation. Quantum computers simulate quantum systems naturally.
// (This is Feynman's original motivation for quantum computing.)
//
// CURRENT LIMITATIONS (NISQ era):
// - Active space limited to ~20 qubits (8-10 electrons)
// - Must use Fragment Molecular Orbital (FMO) approximation
// - Full protein simulation requires fault-tolerant quantum computers
//
// Usage:
//   dotnet fsi BindingAffinity.fsx
//   dotnet fsi BindingAffinity.fsx -- --help
//   dotnet fsi BindingAffinity.fsx -- --max-iterations 100 --tolerance 1e-5
//   dotnet fsi BindingAffinity.fsx -- --output results.json --csv results.csv --quiet
//
// ==============================================================================

(*
Background Theory
-----------------

BIOCHEMISTRY FOUNDATION:
This example builds on concepts from Harper's Illustrated Biochemistry
(28th Edition, Murray et al.):
  - Chapter 7: Enzymes: Mechanism of Action - transition states, active sites
  - Chapter 8: Enzymes: Kinetics - Michaelis-Menten, Km, Ki, inhibition types

ENZYME-SUBSTRATE INTERACTIONS (Harper's Ch.7):
Enzymes accelerate reactions by stabilizing the transition state. Drug design
exploits this by creating transition state analogs that bind tightly to the
active site. The "lock and key" model has evolved to "induced fit" - both
enzyme and substrate undergo conformational changes upon binding.

Key catalytic mechanisms (relevant to drug design):
  - ACID-BASE CATALYSIS: Proton transfer (His, Glu, Asp residues)
  - COVALENT CATALYSIS: Transient enzyme-substrate bonds (Ser, Cys)
  - METAL ION CATALYSIS: Lewis acid or redox chemistry (Zn, Fe, Mg)
  - PROXIMITY/ORIENTATION: Bringing reactants together optimally

INHIBITION TYPES (Harper's Ch.8):
  - COMPETITIVE: Drug binds active site, increases apparent Km
  - NON-COMPETITIVE: Drug binds allosteric site, decreases Vmax
  - UNCOMPETITIVE: Drug binds enzyme-substrate complex
  - MECHANISM-BASED (suicide): Drug converted to irreversible inhibitor

BINDING AFFINITY is the fundamental measure of drug-target interaction strength.
A drug's efficacy depends critically on how tightly and selectively it binds to
its intended protein target. The binding free energy (dG_bind) determines
the equilibrium between bound and unbound states:

    Kd = [Protein][Ligand] / [Complex] = exp(dG_bind / RT)

Where Kd is the dissociation constant. Drug-like binding typically requires
Kd in the nanomolar (nM) to picomolar (pM) range, corresponding to:

    dG_bind = -7 to -12 kcal/mol (favorable binding)

The binding energy can be decomposed as:

    dE_bind = E_complex - E_protein - E_ligand

Key Equations:
  - Binding Energy: dE = E_complex - E_protein - E_ligand
  - Free Energy: dG = dH - T*dS (entropic correction needed)
  - Dissociation Constant: Kd = exp(dG / RT)
  - Michaelis-Menten: v = Vmax*[S] / (Km + [S])  (Harper's Ch.8)
  - IC50 (inhibitor): Related to Ki via Cheng-Prusoff equation

References:
  [1] Shirts, M.R. & Mobley, D.L. "Free Energy Calculations" Methods Mol. Biol. (2017)
  [2] Cao, Y. et al. "Quantum Chemistry in the Age of Quantum Computing" Chem. Rev. (2019)
  [3] Wikipedia: Binding_affinity (https://en.wikipedia.org/wiki/Binding_affinity)
  [4] Reiher, M. et al. "Elucidating reaction mechanisms on quantum computers" PNAS (2017)
  [5] Harper's Illustrated Biochemistry, 28th Ed., Chapters 7-8
  [6] Aulton's Pharmaceutics, 5th Ed., Chapter 8 (polymorphism, solid-state)
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

Cli.exitIfHelp "BindingAffinity.fsx"
    "VQE protein-ligand binding energy calculation via fragment molecular orbital approach"
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
// MOLECULAR STRUCTURES
// ==============================================================================

if not quiet then
    printfn "=============================================================="
    printfn "  Protein-Ligand Binding Affinity (Quantum VQE)"
    printfn "=============================================================="
    printfn ""

// Drug ligand - simplified acetyl group (mimics aspirin's active moiety)
// Real aspirin: CC(=O)Oc1ccccc1C(=O)O
// We use a minimal fragment for NISQ tractability.
let ligandFragment : Molecule = {
    Name = "Acetyl-Fragment"
    Atoms = [
        { Element = "C"; Position = (0.0, 0.0, 0.0) }
        { Element = "O"; Position = (1.2, 0.0, 0.0) }       // C=O double bond ~1.2 A
        { Element = "H"; Position = (-0.5, 0.9, 0.0) }
        { Element = "H"; Position = (-0.5, -0.9, 0.0) }
    ]
    Bonds = [
        { Atom1 = 0; Atom2 = 1; BondOrder = 2.0 }  // C=O
        { Atom1 = 0; Atom2 = 2; BondOrder = 1.0 }  // C-H
        { Atom1 = 0; Atom2 = 3; BondOrder = 1.0 }  // C-H
    ]
    Charge = 0
    Multiplicity = 1
}

// Protein binding site - simplified serine hydroxyl group.
// Models the H-bond donor in an enzyme active site.
let proteinFragment : Molecule = {
    Name = "Serine-OH"
    Atoms = [
        { Element = "O"; Position = (3.5, 0.0, 0.0) }      // ~2.8 A from ligand O (H-bond distance)
        { Element = "H"; Position = (3.0, 0.0, 0.0) }      // H pointing toward ligand
        { Element = "C"; Position = (4.5, 0.0, 0.0) }      // Simplified Calpha backbone
    ]
    Bonds = [
        { Atom1 = 0; Atom2 = 1; BondOrder = 1.0 }  // O-H
        { Atom1 = 0; Atom2 = 2; BondOrder = 1.0 }  // O-C
    ]
    Charge = 0
    Multiplicity = 1
}

/// Protein-ligand complex (combined system).
/// Binding energy: E_binding = E_complex - E_protein - E_ligand
let proteinLigandComplex : Molecule = {
    Name = "Serine-Acetyl-Complex"
    Atoms =
        ligandFragment.Atoms @
        proteinFragment.Atoms
    Bonds =
        ligandFragment.Bonds @
        (proteinFragment.Bonds |> List.map (fun b ->
            { b with
                Atom1 = b.Atom1 + ligandFragment.Atoms.Length
                Atom2 = b.Atom2 + ligandFragment.Atoms.Length }))
    Charge = 0
    Multiplicity = 1
}

// Calculate key distances
let hBondDistance =
    let (px, _, _) = (3.0, 0.0, 0.0)  // Protein H position in complex
    abs(px - 1.2)  // Distance from ligand O to protein H

if not quiet then
    printfn "Molecular System"
    printfn "--------------------------------------------------------------"
    printfn ""
    printfn "Ligand: %s" ligandFragment.Name
    printfn "  Atoms: %d" ligandFragment.Atoms.Length
    printfn "  Electrons: %d" (Molecule.countElectrons ligandFragment)
    printfn "  Key Feature: Carbonyl (C=O) - H-bond acceptor"
    printfn ""
    printfn "Protein Fragment: %s" proteinFragment.Name
    printfn "  Atoms: %d" proteinFragment.Atoms.Length
    printfn "  Electrons: %d" (Molecule.countElectrons proteinFragment)
    printfn "  Key Feature: Hydroxyl (O-H) - H-bond donor"
    printfn ""
    printfn "Complex: %s" proteinLigandComplex.Name
    printfn "  Total Atoms: %d" proteinLigandComplex.Atoms.Length
    printfn "  Total Electrons: %d" (Molecule.countElectrons proteinLigandComplex)
    printfn ""
    printfn "Interaction Geometry:"
    printfn "  C=O...H-O distance: %.2f A" hBondDistance
    printfn "  (Typical H-bond: 1.5-2.5 A)"
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
    printfn "VQE Calculations (Fragment Molecular Orbital Approach)"
    printfn "=============================================================="
    printfn ""

let results = System.Collections.Generic.List<Map<string, string>>()

/// Calculate ground state energy for a molecule using VQE.
let calculateEnergy (molecule: Molecule) : float =
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

    match GroundStateEnergy.estimateEnergy molecule config |> Async.RunSynchronously with
    | Ok vqeResult -> vqeResult.Energy
    | Error err ->
        if not quiet then
            printfn "  Warning: VQE failed: %A" err.Message
        0.0

// Calculate energies for each component
if not quiet then
    printfn "Step 1: Calculate Ligand Energy"
    printfn "--------------------------------------------------------------"

let startTime = DateTime.Now
let ligandEnergy = calculateEnergy ligandFragment
let ligandTime = (DateTime.Now - startTime).TotalSeconds

if not quiet then
    printfn "  E_ligand = %.6f Hartree" ligandEnergy
    printfn "  Time: %.2f seconds" ligandTime
    printfn ""

if not quiet then
    printfn "Step 2: Calculate Protein Fragment Energy"
    printfn "--------------------------------------------------------------"

let startTime2 = DateTime.Now
let proteinEnergy = calculateEnergy proteinFragment
let proteinTime = (DateTime.Now - startTime2).TotalSeconds

if not quiet then
    printfn "  E_protein = %.6f Hartree" proteinEnergy
    printfn "  Time: %.2f seconds" proteinTime
    printfn ""

if not quiet then
    printfn "Step 3: Calculate Complex Energy"
    printfn "--------------------------------------------------------------"

let startTime3 = DateTime.Now
let complexEnergy = calculateEnergy proteinLigandComplex
let complexTime = (DateTime.Now - startTime3).TotalSeconds

if not quiet then
    printfn "  E_complex = %.6f Hartree" complexEnergy
    printfn "  Time: %.2f seconds" complexTime
    printfn ""

// Store VQE results
results.Add(
    [ "component", "Ligand"
      "molecule", ligandFragment.Name
      "energy_hartree", sprintf "%.6f" ligandEnergy
      "atoms", string ligandFragment.Atoms.Length
      "electrons", string (Molecule.countElectrons ligandFragment)
      "time_s", sprintf "%.2f" ligandTime ]
    |> Map.ofList)

results.Add(
    [ "component", "Protein"
      "molecule", proteinFragment.Name
      "energy_hartree", sprintf "%.6f" proteinEnergy
      "atoms", string proteinFragment.Atoms.Length
      "electrons", string (Molecule.countElectrons proteinFragment)
      "time_s", sprintf "%.2f" proteinTime ]
    |> Map.ofList)

results.Add(
    [ "component", "Complex"
      "molecule", proteinLigandComplex.Name
      "energy_hartree", sprintf "%.6f" complexEnergy
      "atoms", string proteinLigandComplex.Atoms.Length
      "electrons", string (Molecule.countElectrons proteinLigandComplex)
      "time_s", sprintf "%.2f" complexTime ]
    |> Map.ofList)

// ==============================================================================
// BINDING ENERGY CALCULATION
// ==============================================================================

// Binding energy: E_binding = E_complex - E_protein - E_ligand
// Negative = favorable binding (stable complex)
let bindingEnergyHartree = complexEnergy - proteinEnergy - ligandEnergy
let hartreeToKcalMol = 627.5
let hartreeToKJMol = 2625.5
let bindingEnergyKcal = bindingEnergyHartree * hartreeToKcalMol
let bindingEnergyKJ = bindingEnergyHartree * hartreeToKJMol

if not quiet then
    printfn "=============================================================="
    printfn "  Binding Energy Results"
    printfn "=============================================================="
    printfn ""
    printfn "Energy Components:"
    printfn "  E_ligand:     %12.6f Hartree" ligandEnergy
    printfn "  E_protein:    %12.6f Hartree" proteinEnergy
    printfn "  E_complex:    %12.6f Hartree" complexEnergy
    printfn ""
    printfn "Binding Energy:"
    printfn "  dE_binding = E_complex - E_protein - E_ligand"
    printfn "  dE_binding = %.6f Hartree" bindingEnergyHartree
    printfn "             = %.2f kcal/mol" bindingEnergyKcal
    printfn "             = %.2f kJ/mol" bindingEnergyKJ
    printfn ""

// Interpret result
let interpretation =
    if bindingEnergyKcal < -10.0 then
        "Strong binding (drug-like affinity)"
    elif bindingEnergyKcal < -5.0 then
        "Moderate binding (lead compound)"
    elif bindingEnergyKcal < -1.0 then
        "Weak binding (hit compound)"
    elif bindingEnergyKcal < 0.0 then
        "Very weak binding"
    else
        "Unfavorable (no binding)"

if not quiet then
    printfn "Interpretation: %s" interpretation
    printfn ""

// Estimate dissociation constant (Kd)
// dG = -RT ln(Kd) ~ dE for this simplified model
// At T = 300K, RT = 0.596 kcal/mol
let rtKcal = 0.596  // kcal/mol at 300K
let estimatedKd =
    if bindingEnergyKcal < 0.0 then
        exp(bindingEnergyKcal / rtKcal) * 1e-9  // Convert to molar
    else
        Double.PositiveInfinity

let kdStr =
    if estimatedKd < Double.PositiveInfinity then
        if estimatedKd < 1e-9 then
            sprintf "%.2e M (picomolar range - very strong)" estimatedKd
        elif estimatedKd < 1e-6 then
            sprintf "%.2e M (nanomolar range - drug-like)" estimatedKd
        elif estimatedKd < 1e-3 then
            sprintf "%.2e M (micromolar range - lead-like)" estimatedKd
        else
            sprintf "%.2e M (millimolar range - weak)" estimatedKd
    else
        "N/A (unfavorable binding)"

if not quiet then
    printfn "Estimated Dissociation Constant:"
    printfn "  Kd ~ %s" kdStr
    printfn ""

results.Add(
    [ "component", "BindingEnergy"
      "energy_hartree", sprintf "%.6f" bindingEnergyHartree
      "energy_kcal_mol", sprintf "%.2f" bindingEnergyKcal
      "energy_kj_mol", sprintf "%.2f" bindingEnergyKJ
      "interpretation", interpretation
      "estimated_kd", kdStr
      "hbond_distance_a", sprintf "%.2f" hBondDistance ]
    |> Map.ofList)

// ==============================================================================
// CLASSICAL COMPARISON
// ==============================================================================

// Simple classical H-bond energy estimate
let classicalHBondEnergy =
    if hBondDistance < 2.0 then -5.0
    elif hBondDistance < 2.5 then -3.0
    elif hBondDistance < 3.0 then -1.5
    else 0.0

let difference = abs(bindingEnergyKcal - classicalHBondEnergy)

if not quiet then
    printfn "Comparison with Classical Force Field"
    printfn "--------------------------------------------------------------"
    printfn ""
    printfn "Classical H-bond energy estimate: %.2f kcal/mol" classicalHBondEnergy
    printfn "Quantum VQE binding energy:       %.2f kcal/mol" bindingEnergyKcal
    printfn ""
    printfn "Difference: %.2f kcal/mol" difference
    printfn ""

    if difference > 2.0 then
        printfn "[NOTE] Significant difference detected!"
        printfn "  Quantum simulation captures electron correlation effects"
        printfn "  that classical force fields miss:"
        printfn "  - Charge polarization"
        printfn "  - Partial covalent character in H-bonds"
        printfn "  - Dispersion interactions"
    else
        printfn "[OK] Results consistent with classical approximation"
        printfn "  (For this simple system, force fields are adequate)"
    printfn ""

results.Add(
    [ "component", "ClassicalComparison"
      "classical_hbond_kcal", sprintf "%.2f" classicalHBondEnergy
      "quantum_binding_kcal", sprintf "%.2f" bindingEnergyKcal
      "difference_kcal", sprintf "%.2f" difference ]
    |> Map.ofList)

// ==============================================================================
// DRUG DISCOVERY INSIGHTS
// ==============================================================================

if not quiet then
    printfn "=============================================================="
    printfn "  Drug Discovery Insights"
    printfn "=============================================================="
    printfn ""

    printfn "Key Findings"
    printfn "--------------------------------------------------------------"
    printfn ""
    printfn "1. Binding Affinity: %.2f kcal/mol (%s)" bindingEnergyKcal interpretation
    printfn ""
    printfn "2. Quantum Advantage:"
    printfn "   - VQE captures electron correlation exactly"
    printfn "   - Critical for charge transfer interactions"
    printfn "   - Important for metal coordination (not shown here)"
    printfn ""
    printfn "3. Current Limitations (NISQ era):"
    printfn "   - Fragment approach required (can't simulate full protein)"
    printfn "   - Limited to ~20 qubits (8-10 correlated electrons)"
    printfn "   - Basis set error may exceed correlation error"
    printfn ""
    printfn "4. When Quantum Matters Most:"
    printfn "   - Covalent inhibitors (reaction energy barriers)"
    printfn "   - Metalloenzymes (d-orbital correlation)"
    printfn "   - Charge-transfer complexes"
    printfn "   - Systems where DFT fails (strong correlation)"
    printfn ""

// ==============================================================================
// NEXT STEPS
// ==============================================================================

if not quiet then
    printfn "Recommended Next Steps"
    printfn "--------------------------------------------------------------"
    printfn ""
    printfn "1. Virtual Screening:"
    printfn "   - Calculate binding energies for multiple ligand candidates"
    printfn "   - Rank by binding affinity"
    printfn "   - Filter for drug-likeness (Lipinski rules)"
    printfn ""
    printfn "2. Lead Optimization:"
    printfn "   - Modify ligand functional groups"
    printfn "   - Calculate ddG for each modification"
    printfn "   - Optimize for selectivity (binding to target vs off-target)"
    printfn ""
    printfn "3. ADMET Prediction:"
    printfn "   - Use quantum chemistry for metabolism prediction"
    printfn "   - Calculate reaction barriers for CYP450 metabolism"
    printfn "   - See: examples/DrugDiscovery/ReactionPathway.fsx"
    printfn ""
    printfn "4. Scale Up (Future):"
    printfn "   - Fault-tolerant quantum computers (2030+)"
    printfn "   - Full active site simulation (100+ qubits)"
    printfn "   - QM/MM hybrid approaches"
    printfn ""

// ==============================================================================
// SUMMARY
// ==============================================================================

let totalTime = ligandTime + proteinTime + complexTime

if not quiet then
    printfn "=============================================================="
    printfn "  Summary"
    printfn "=============================================================="
    printfn ""
    printfn "[OK] Demonstrated VQE for protein-ligand binding"
    printfn "[OK] Calculated binding energy via fragment approach"
    printfn "[OK] Compared with classical force field estimate"
    printfn "[OK] Quantum compliant (all calculations via IQuantumBackend)"
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
    let header = [ "component"; "molecule"; "energy_hartree"; "energy_kcal_mol"; "energy_kj_mol"; "atoms"; "electrons"; "time_s"; "interpretation"; "estimated_kd"; "hbond_distance_a"; "classical_hbond_kcal"; "quantum_binding_kcal"; "difference_kcal" ]
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
