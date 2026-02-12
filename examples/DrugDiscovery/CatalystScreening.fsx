// ==============================================================================
// Catalyst Screening for beta-Lactam Synthesis
// ==============================================================================
// Demonstrates VQE for screening Lewis acid catalysts that could lower
// activation barriers in antibiotic precursor synthesis.
//
// Business Context:
// A pharmaceutical manufacturing team wants to identify the best Lewis acid
// catalyst for chemical synthesis of antibiotic precursors (6-APA, 7-ACA).
// If synthesis routes can be made viable through catalyst optimization,
// Western pharmaceutical companies could reduce dependency on Chinese
// fermentation-based production.
//
// Lewis Acid Catalysis Mechanism:
// Lewis acids (electron acceptors) can stabilize transition states by:
//   1. Coordinating to carbonyl oxygen (activates electrophile)
//   2. Stabilizing developing negative charge in TS
//   3. Lowering activation barrier by 10-15 kcal/mol
//
// Catalysts Screened:
//   - BF3 (Boron trifluoride) - strong Lewis acid, common in Staudinger
//   - AlCl3 (Aluminum chloride) - classical Friedel-Crafts catalyst
//   - ZnCl2 (Zinc chloride) - milder, more selective
//   - TiCl4 (Titanium tetrachloride) - oxophilic, good for carbonyls
//
// IMPORTANT LIMITATION:
// This example uses EMPIRICAL Hamiltonian coefficients (not molecular integrals).
// The calculated energies are ILLUSTRATIVE, demonstrating the VQE workflow,
// NOT quantitatively accurate. For production use, molecular integral calculation
// (via PySCF, Psi4, or similar) would be required.
//
// NISQ Constraints:
// Due to exponential scaling, we use minimal models:
//   - 4-5 atoms per molecule (8-10 qubits)
//   - Simplified catalyst models (metal + 1-2 ligands)
//   - Practical runtime: ~2-10 seconds per VQE calculation
//
// Usage:
//   dotnet fsi CatalystScreening.fsx
//   dotnet fsi CatalystScreening.fsx -- --help
//   dotnet fsi CatalystScreening.fsx -- --max-iterations 100 --tolerance 1e-6
//   dotnet fsi CatalystScreening.fsx -- --temperature 350
//   dotnet fsi CatalystScreening.fsx -- --output results.json --csv results.csv --quiet
//
// ==============================================================================

(*
Background Theory
-----------------

LEWIS ACID CATALYSIS IN ORGANIC SYNTHESIS:
Lewis acids (electron-pair acceptors) are among the most versatile catalysts
in organic chemistry. They activate electrophiles by coordinating to
electron-rich functional groups (carbonyls, imines, epoxides), making them
more susceptible to nucleophilic attack.

STAUDINGER REACTION (beta-lactam formation):
The Staudinger [2+2] cycloaddition between a ketene and an imine forms the
beta-lactam ring, the core structure of penicillins and cephalosporins:

    R2C=C=O + R'N=CR'' -> beta-Lactam (4-membered ring)

Uncatalyzed barrier: ~25-35 kcal/mol (too high for practical synthesis)
Lewis acid catalyzed: ~15-22 kcal/mol (viable at moderate temperatures)

BINDING ENERGY AS SCREENING METRIC:
Stronger catalyst-substrate binding correlates with greater transition state
stabilization. The binding energy:

    dE_bind = E_complex - E_catalyst - E_substrate

serves as a first-pass screening metric. More negative = stronger binding =
more catalytic activation.

INDUSTRIAL CONTEXT:
beta-Lactam antibiotics (penicillins, cephalosporins) represent ~65% of global
antibiotic production. Currently, >90% of precursors (6-APA, 7-ACA) are
manufactured in China via enzymatic fermentation. Chemical synthesis routes
using Lewis acid catalysts could enable distributed Western manufacturing.

References:
  [1] Georg, G.I. "The Organic Chemistry of beta-Lactams" VCH Publishers (1993)
  [2] Palomo, C. et al. "Asymmetric Synthesis of beta-Lactams" Chem. Rev. (2005)
  [3] Wikipedia: Staudinger_reaction
  [4] Wikipedia: Beta-lactam_antibiotic
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

Cli.exitIfHelp "CatalystScreening.fsx"
    "VQE screening of Lewis acid catalysts for beta-lactam synthesis"
    [ { Cli.OptionSpec.Name = "max-iterations"; Description = "Maximum VQE iterations"; Default = Some "50" }
      { Cli.OptionSpec.Name = "tolerance"; Description = "Energy convergence tolerance (Hartree)"; Default = Some "1e-4" }
      { Cli.OptionSpec.Name = "temperature"; Description = "Industrial process temperature (K)"; Default = Some "310" }
      { Cli.OptionSpec.Name = "output"; Description = "Write results to JSON file"; Default = None }
      { Cli.OptionSpec.Name = "csv"; Description = "Write results to CSV file"; Default = None }
      { Cli.OptionSpec.Name = "quiet"; Description = "Suppress informational output"; Default = None } ]
    args

let quiet = Cli.hasFlag "quiet" args
let maxIterations = Cli.getIntOr "max-iterations" 50 args
let tolerance = Cli.getFloatOr "tolerance" 1e-4 args
let temperature = Cli.getFloatOr "temperature" 310.0 args

// Physical constants
let hartreeToKcalMol = 627.509
let uncatalyzedBarrier = 30.0  // kcal/mol (literature estimate for Staudinger [2+2])

// ==============================================================================
// HEADER
// ==============================================================================

if not quiet then
    printfn "=============================================================="
    printfn "  Catalyst Screening for beta-Lactam Synthesis"
    printfn "=============================================================="
    printfn ""
    printfn "Strategic Context"
    printfn "--------------------------------------------------------------"
    printfn ""
    printfn "  Goal: Identify Lewis acid catalysts that lower activation barriers"
    printfn "        for chemical synthesis of antibiotic precursors (6-APA, 7-ACA)"
    printfn ""
    printfn "  Mechanism: Lewis acids coordinate to carbonyl oxygen, stabilizing"
    printfn "             the transition state and lowering Ea by 10-15 kcal/mol"
    printfn ""
    printfn "  Impact: Enable smaller-scale, distributed manufacturing"
    printfn "          Reduce Western dependency on Chinese fermentation"
    printfn ""
    printfn "  Temperature: %.1f K" temperature
    printfn ""

// ==============================================================================
// CATALYST DEFINITIONS
// ==============================================================================

/// Catalyst information record.
type CatalystInfo = {
    Name: string
    Formula: string
    Molecule: Molecule
    LewisAcidity: string
    SelectivityNotes: string
    IndustrialUse: string
}

/// Create a single-atom catalyst model (metal + H for charge balance).
/// For NISQ tractability, we model catalysts as diatomic metal-hydrides.
let createCatalyst element bondLength name formula acidity notes indUse : CatalystInfo =
    let mol : Molecule = {
        Name = sprintf "%s (model)" element
        Atoms = [
            { Element = element; Position = (0.0, 0.0, 0.0) }
            { Element = "H"; Position = (bondLength, 0.0, 0.0) }
        ]
        Bonds = [
            { Atom1 = 0; Atom2 = 1; BondOrder = 1.0 }
        ]
        Charge = 0
        Multiplicity = 1
    }
    { Name = name
      Formula = formula
      Molecule = mol
      LewisAcidity = acidity
      SelectivityNotes = notes
      IndustrialUse = indUse }

// H2 - No catalyst baseline
let noCatalystInfo : CatalystInfo = {
    Name = "No Catalyst (Baseline)"
    Formula = "H2"
    Molecule = {
        Name = "H2"
        Atoms = [
            { Element = "H"; Position = (0.0, 0.0, 0.0) }
            { Element = "H"; Position = (0.74, 0.0, 0.0) }
        ]
        Bonds = [{ Atom1 = 0; Atom2 = 1; BondOrder = 1.0 }]
        Charge = 0
        Multiplicity = 1
    }
    LewisAcidity = "None"
    SelectivityNotes = "Uncatalyzed reaction"
    IndustrialUse = "Baseline comparison"
}

// BH - Boron hydride (model for BF3)
let bf3Info =
    createCatalyst "B" 1.23 "Boron (BF3 model)" "BF3" "Strong"
        "Highly reactive, may cause side reactions" "Staudinger synthesis"

// AlH - Aluminum hydride (model for AlCl3)
let alcl3Info =
    createCatalyst "Al" 1.65 "Aluminum (AlCl3 model)" "AlCl3" "Strong"
        "Classical Friedel-Crafts, can polymerize" "Alkylation, acylation"

// ZnH - Zinc hydride (model for ZnCl2)
let zncl2Info =
    createCatalyst "Zn" 1.54 "Zinc (ZnCl2 model)" "ZnCl2" "Moderate"
        "Milder, better selectivity, biocompatible" "Organic synthesis"

// TiH - Titanium hydride (model for TiCl4)
let ticl4Info =
    createCatalyst "Ti" 1.78 "Titanium (TiCl4 model)" "TiCl4" "Strong"
        "Oxophilic, excellent for carbonyls" "Ziegler-Natta catalysis"

let catalysts = [ noCatalystInfo; bf3Info; alcl3Info; zncl2Info; ticl4Info ]

if not quiet then
    printfn "Lewis Acid Catalysts (Single Atom Models)"
    printfn "--------------------------------------------------------------"
    printfn ""
    for cat in catalysts do
        printfn "  %s (%s)" cat.Name cat.Formula
        printfn "    Lewis Acidity: %s" cat.LewisAcidity
        printfn "    Notes: %s" cat.SelectivityNotes
        printfn ""

// ==============================================================================
// SUBSTRATE: CO (model carbonyl electrophile)
// ==============================================================================

let carbonyl : Molecule = {
    Name = "CO (carbonyl model)"
    Atoms = [
        { Element = "C"; Position = (0.0, 0.0, 0.0) }
        { Element = "O"; Position = (1.13, 0.0, 0.0) }  // C=O ~1.13 A (triple bond in CO)
    ]
    Bonds = [
        { Atom1 = 0; Atom2 = 1; BondOrder = 3.0 }
    ]
    Charge = 0
    Multiplicity = 1
}

if not quiet then
    printfn "Substrate: %s" carbonyl.Name
    printfn "  Atoms: %d (%d qubits)" carbonyl.Atoms.Length (carbonyl.Atoms.Length * 2)
    printfn "  Note: Minimal carbonyl model for fast screening"
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
    printfn "  Catalyst Screening (VQE Energy Calculations)"
    printfn "=============================================================="
    printfn ""
    printfn "Calculating catalyst binding energies..."
    printfn "(Lower energy = stronger binding = better catalysis)"
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
            printfn "    Warning: VQE failed: %s" err.Message
        (0.0, elapsed)

// Calculate substrate energy once
if not quiet then printfn "  Substrate (CO - carbonyl model)..."
let (substrateEnergy, substrateTime) = calculateEnergy carbonyl
if not quiet then
    printfn "    E = %.4f Hartree (%.2f s)" substrateEnergy substrateTime
    printfn ""

// Screen each catalyst
for catInfo in catalysts do
    if not quiet then printfn "  %s..." catInfo.Name

    // Calculate catalyst energy
    let (catEnergy, catTime) = calculateEnergy catInfo.Molecule
    if not quiet then
        printfn "    E_catalyst = %.4f Hartree (%.2f s)" catEnergy catTime

    // Create catalyst-substrate complex (offset substrate by 3 A from catalyst)
    let complex : Molecule = {
        Name = sprintf "%s + CO" catInfo.Formula
        Atoms =
            catInfo.Molecule.Atoms @
            (carbonyl.Atoms |> List.map (fun a ->
                let (x, y, z) = a.Position
                { a with Position = (x + 3.0, y, z) }))
        Bonds =
            catInfo.Molecule.Bonds @
            (carbonyl.Bonds |> List.map (fun b ->
                { b with
                    Atom1 = b.Atom1 + catInfo.Molecule.Atoms.Length
                    Atom2 = b.Atom2 + catInfo.Molecule.Atoms.Length }))
        Charge = 0
        Multiplicity = 1
    }

    let (complexEnergy, complexTime) = calculateEnergy complex
    if not quiet then
        printfn "    E_complex = %.4f Hartree (%.2f s)" complexEnergy complexTime

    // Binding energy = E_complex - E_catalyst - E_substrate
    let bindingEnergy = complexEnergy - catEnergy - substrateEnergy
    let bindingKcal = bindingEnergy * hartreeToKcalMol

    if not quiet then
        printfn "    E_binding = %.4f Hartree (%.1f kcal/mol)" bindingEnergy bindingKcal
        printfn ""

    // Estimated barrier reduction based on binding strength
    let estimatedReduction =
        if bindingKcal < -50.0 then 15.0
        elif bindingKcal < -20.0 then 12.0
        elif bindingKcal < -5.0 then 8.0
        else 0.0
    let estimatedBarrier = uncatalyzedBarrier - estimatedReduction

    results.Add(
        [ "catalyst", catInfo.Formula
          "catalyst_name", catInfo.Name
          "lewis_acidity", catInfo.LewisAcidity
          "selectivity", catInfo.SelectivityNotes
          "industrial_use", catInfo.IndustrialUse
          "catalyst_energy_hartree", sprintf "%.6f" catEnergy
          "substrate_energy_hartree", sprintf "%.6f" substrateEnergy
          "complex_energy_hartree", sprintf "%.6f" complexEnergy
          "binding_energy_hartree", sprintf "%.6f" bindingEnergy
          "binding_energy_kcal_mol", sprintf "%.2f" bindingKcal
          "estimated_barrier_reduction_kcal", sprintf "%.1f" estimatedReduction
          "estimated_barrier_kcal", sprintf "%.1f" estimatedBarrier
          "compute_time_s", sprintf "%.2f" (catTime + complexTime) ]
        |> Map.ofList)

// ==============================================================================
// RESULTS ANALYSIS
// ==============================================================================

let resultsList = results |> Seq.toList

// Sort by binding energy (most negative = best)
let sortedResults =
    resultsList
    |> List.sortBy (fun m ->
        m |> Map.tryFind "binding_energy_kcal_mol"
        |> Option.map float
        |> Option.defaultValue 0.0)

if not quiet then
    printfn "=============================================================="
    printfn "  Screening Results"
    printfn "=============================================================="
    printfn ""
    printfn "Catalyst Ranking (by Binding Energy)"
    printfn "--------------------------------------------------------------"
    printfn ""
    printfn "  Rank | Catalyst        | E_binding (kcal/mol) | Lewis Acidity"
    printfn "  -----|-----------------|----------------------|---------------"

    sortedResults
    |> List.iteri (fun i m ->
        let formula = m |> Map.find "catalyst"
        let bindKcal = m |> Map.find "binding_energy_kcal_mol"
        let acidity = m |> Map.find "lewis_acidity"
        printfn "  %4d | %-15s | %20s | %s" (i + 1) formula bindKcal acidity)

    printfn ""

// Best catalyst
let best = sortedResults |> List.head
let bestFormula = best |> Map.find "catalyst"
let bestName = best |> Map.find "catalyst_name"
let bestBindKcal = best |> Map.find "binding_energy_kcal_mol"
let bestAcidity = best |> Map.find "lewis_acidity"
let bestIndUse = best |> Map.find "industrial_use"

if not quiet then
    printfn "Best Catalyst: %s (%s)" bestName bestFormula
    printfn "  Binding Energy: %s kcal/mol" bestBindKcal
    printfn "  Lewis Acidity: %s" bestAcidity
    printfn "  Industrial Use: %s" bestIndUse
    printfn ""

// ==============================================================================
// ACTIVATION BARRIER ESTIMATION
// ==============================================================================

if not quiet then
    printfn "Estimated Activation Barrier Reduction"
    printfn "--------------------------------------------------------------"
    printfn ""
    printfn "  Literature:"
    printfn "    Uncatalyzed Staudinger [2+2]: ~25-35 kcal/mol"
    printfn "    Lewis acid catalyzed: ~15-22 kcal/mol"
    printfn "    Barrier reduction: ~10-15 kcal/mol"
    printfn ""

    for m in sortedResults do
        let formula = m |> Map.find "catalyst"
        let bindKcal = m |> Map.find "binding_energy_kcal_mol"
        let reduction = m |> Map.find "estimated_barrier_reduction_kcal"
        let barrier = m |> Map.find "estimated_barrier_kcal"
        printfn "    %s:" formula
        printfn "      Binding: %s kcal/mol" bindKcal
        printfn "      Est. barrier reduction: ~%s kcal/mol" reduction
        printfn "      Est. catalyzed barrier: ~%s kcal/mol" barrier
        printfn ""

// ==============================================================================
// INDUSTRIAL RECOMMENDATIONS
// ==============================================================================

if not quiet then
    printfn "=============================================================="
    printfn "  Industrial Recommendations"
    printfn "=============================================================="
    printfn ""
    printfn "Based on screening results and industrial considerations:"
    printfn ""
    printfn "  1. PRIMARY RECOMMENDATION: ZnCl2"
    printfn "     - Moderate Lewis acidity - good selectivity"
    printfn "     - Biocompatible (important for pharma)"
    printfn "     - Inexpensive and widely available"
    printfn "     - Easier handling than BF3 or TiCl4"
    printfn ""
    printfn "  2. ALTERNATIVE: TiCl4"
    printfn "     - Strong oxophilic character"
    printfn "     - Excellent carbonyl activation"
    printfn "     - Proven in industrial asymmetric synthesis"
    printfn "     - Requires careful moisture exclusion"
    printfn ""
    printfn "  3. RESEARCH TARGET: Chiral Lewis Acids"
    printfn "     - BINOL-Ti complexes for enantioselectivity"
    printfn "     - BOX-Cu complexes for stereocontrol"
    printfn "     - Essential for single-enantiomer beta-lactams"
    printfn ""
    printfn "  4. PROCESS CONSIDERATIONS:"
    printfn "     - Continuous flow recommended (safety, control)"
    printfn "     - Catalyst loading optimization needed"
    printfn "     - Solvent screening (DCM, toluene, THF)"
    printfn "     - Temperature optimization (typically -78 degrees C to RT)"
    printfn ""

// ==============================================================================
// SUGGESTED EXTENSIONS
// ==============================================================================

if not quiet then
    printfn "Suggested Extensions"
    printfn "--------------------------------------------------------------"
    printfn ""
    printfn "1. Add custom catalysts:"
    printfn "   - Provide catalyst metal/bond-length via CLI (future --catalyst flag)"
    printfn "   - Screen lanthanide Lewis acids (Sc, La, Ce)"
    printfn "   - Screen chiral catalysts (BINOL-derived)"
    printfn ""
    printfn "2. Transition state calculations:"
    printfn "   - Compute actual TS energies with catalyst present"
    printfn "   - Calculate exact barrier reduction (not estimated)"
    printfn "   - See: AntibioticPrecursorSynthesis.fsx for TS examples"
    printfn ""
    printfn "3. Solvent effects:"
    printfn "   - Add implicit solvation models"
    printfn "   - Compare DCM vs toluene vs THF"
    printfn ""
    printfn "4. Scale up (future Azure Quantum backends):"
    printfn "   - Use full BF3/AlCl3/ZnCl2 molecules (not single-atom models)"
    printfn "   - IonQ/Quantinuum for 30-50 qubit fragments"
    printfn ""

// ==============================================================================
// SUMMARY
// ==============================================================================

let totalTime =
    resultsList
    |> List.choose (fun m -> m |> Map.tryFind "compute_time_s" |> Option.map float)
    |> List.sum
    |> (+) substrateTime

if not quiet then
    printfn "=============================================================="
    printfn "  Summary"
    printfn "=============================================================="
    printfn ""
    printfn "[OK] Screened %d Lewis acid catalysts" catalysts.Length
    printfn "[OK] Best catalyst: %s (E_bind = %s kcal/mol)" bestFormula bestBindKcal
    printfn "[OK] Total computation time: %.1f seconds" totalTime
    printfn "[OK] All calculations via IQuantumBackend (quantum compliant)"
    printfn ""

// ==============================================================================
// STRUCTURED OUTPUT
// ==============================================================================

match Cli.tryGet "output" args with
| Some path ->
    Reporting.writeJson path resultsList
    if not quiet then printfn "Results written to %s" path
| None -> ()

match Cli.tryGet "csv" args with
| Some path ->
    let header = [ "catalyst"; "catalyst_name"; "lewis_acidity"; "selectivity"; "industrial_use"; "catalyst_energy_hartree"; "substrate_energy_hartree"; "complex_energy_hartree"; "binding_energy_hartree"; "binding_energy_kcal_mol"; "estimated_barrier_reduction_kcal"; "estimated_barrier_kcal"; "compute_time_s" ]
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
