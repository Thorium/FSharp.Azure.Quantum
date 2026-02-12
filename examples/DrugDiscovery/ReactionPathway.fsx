// ==============================================================================
// Drug Metabolism Reaction Pathway Example
// ==============================================================================
// Demonstrates VQE for calculating drug metabolism activation energies.
//
// Business Context:
// A pharmaceutical research team needs to predict how a drug is metabolized
// by cytochrome P450 enzymes in the liver. The rate of metabolism determines:
// - Drug half-life and dosing frequency
// - Potential for toxic metabolite formation
// - Drug-drug interactions
//
// Quantum chemistry calculates ACTIVATION ENERGY BARRIERS that determine
// which metabolic pathway is preferred.
//
// This example shows:
// - Transition state theory for reaction kinetics
// - VQE calculation of reactant, transition state, and product energies
// - Activation energy barrier calculation
// - Rate constant estimation (Eyring equation)
//
// Quantum Advantage:
// Transition state energies require accurate electron correlation.
// VQE provides this naturally. Classical DFT often underestimates barriers.
//
// CURRENT LIMITATIONS (NISQ era):
// - Limited to small active spaces (~20 qubits)
// - Transition state search is classical (geometry optimization)
// - Full enzyme simulation requires fault-tolerant QC
//
// Usage:
//   dotnet fsi ReactionPathway.fsx
//   dotnet fsi ReactionPathway.fsx -- --help
//   dotnet fsi ReactionPathway.fsx -- --temperature 298.15
//   dotnet fsi ReactionPathway.fsx -- --max-iterations 100 --tolerance 1e-6
//   dotnet fsi ReactionPathway.fsx -- --output results.json --csv results.csv --quiet
//
// ==============================================================================

(*
Background Theory
-----------------

BIOCHEMISTRY FOUNDATION:
This example builds on concepts from Harper's Illustrated Biochemistry
(28th Edition, Murray et al.):
  - Chapter 53: Metabolism of Xenobiotics - CYP450 enzymes, Phase I/II reactions
  - Chapter 12: Biologic Oxidation - redox chemistry, cytochromes

XENOBIOTIC METABOLISM (Harper's Ch.53):
Foreign compounds (xenobiotics) including drugs undergo biotransformation
primarily in the liver, converting lipophilic molecules to hydrophilic
metabolites for excretion. This occurs in two phases:

  PHASE I (Functionalization):
    - Oxidation, reduction, hydrolysis
    - Introduces or exposes functional groups (-OH, -NH2, -COOH)
    - Cytochrome P450 enzymes are primary catalysts
    - Creates reactive intermediates (sometimes toxic)

  PHASE II (Conjugation):
    - Glucuronidation, sulfation, glutathione conjugation, acetylation
    - Greatly increases water solubility
    - Usually detoxification (but not always)

CYTOCHROME P450 ENZYMES (Harper's Ch.53):
The P450 superfamily contains >6000 members (57 in humans). Key drug-metabolizing
isoforms include:

  | Enzyme  | % of Drug Metabolism | Notable Substrates      |
  |---------|----------------------|-------------------------|
  | CYP3A4  | ~50%                 | Most drugs              |
  | CYP2D6  | ~25%                 | Codeine, tamoxifen      |
  | CYP2C9  | ~15%                 | Warfarin, NSAIDs        |
  | CYP1A2  | ~5%                  | Caffeine, theophylline  |

The CYP450 catalytic cycle involves iron-oxo intermediates:
  Fe(III) -> Fe(II) -> Fe(II)-O2 -> Fe(III)-OOH -> [Fe(IV)=O] -> Fe(III)
                                                          |
                                                  Hydrogen abstraction
                                                  from substrate R-H

TRANSITION STATE THEORY (TST), developed by Eyring, Polanyi, and Evans in 1935,
provides the theoretical framework for understanding chemical reaction rates.
The rate of a reaction depends on the ACTIVATION ENERGY (Ea) -- the energy
barrier that must be overcome for reactants to become products.

The reaction coordinate connects:
  Reactants -> [Transition State] -> Products

The ARRHENIUS EQUATION relates rate constant to activation energy:

    k = A * exp(-Ea / RT)

The EYRING EQUATION from TST gives the prefactor explicitly:

    k = (kB * T / h) * exp(-dG_act / RT)

Where dG_act = dH_act - T*dS_act includes entropy of activation.

For DRUG METABOLISM, cytochrome P450 enzymes catalyze hydroxylation:

    R-H + [Fe=O]2+ -> R-OH + Fe2+

The rate-determining step (C-H bond activation) has Ea ~ 10-25 kcal/mol
depending on the substrate and enzyme.

Key Equations:
  - Activation Energy: Ea = E_TS - E_reactants
  - Arrhenius: k = A * exp(-Ea / RT)
  - Eyring: k = (kB*T/h) * exp(-dG_act/RT)
  - Half-life: t_1/2 = ln(2) / k

References:
  [1] Eyring, H. "The Activated Complex in Chemical Reactions" J. Chem. Phys. 3, 107 (1935)
  [2] Guengerich, F.P. "Mechanisms of Cytochrome P450" Chem. Res. Toxicol. (2001)
  [3] Wikipedia: Transition_state_theory
  [4] Reiher, M. et al. "Elucidating reaction mechanisms on quantum computers" PNAS (2017)
  [5] Harper's Illustrated Biochemistry, 28th Ed., Chapter 53
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
// CLI CONFIGURATION
// ==============================================================================

let argv = fsi.CommandLineArgs |> Array.skip 1
let args = Cli.parse argv

Cli.exitIfHelp "ReactionPathway.fsx" "Drug metabolism reaction pathway analysis via VQE"
    [ { Cli.OptionSpec.Name = "max-iterations"; Description = "Maximum VQE iterations (default: 50)"; Default = Some "50" }
      { Cli.OptionSpec.Name = "tolerance"; Description = "Convergence tolerance in Hartree (default: 1e-4)"; Default = Some "1e-4" }
      { Cli.OptionSpec.Name = "temperature"; Description = "Temperature in Kelvin for rate calc (default: 310 = body temp)"; Default = Some "310" }
      { Cli.OptionSpec.Name = "output"; Description = "Write results to JSON file"; Default = None }
      { Cli.OptionSpec.Name = "csv"; Description = "Write results to CSV file"; Default = None }
      { Cli.OptionSpec.Name = "quiet"; Description = "Suppress informational output"; Default = None } ]
    args

let quiet = Cli.hasFlag "quiet" args
let maxIterations = Cli.getIntOr "max-iterations" 50 args
let tolerance = Cli.getFloatOr "tolerance" 1e-4 args
let temperature = Cli.getFloatOr "temperature" 310.0 args

// ==============================================================================
// PHYSICAL CONSTANTS
// ==============================================================================

let kB = 1.380649e-23          // Boltzmann constant (J/K)
let hPlanck = 6.62607015e-34   // Planck constant (J*s)
let gasR = 8.314               // Gas constant (J/mol*K)
let hartreeToKcalMol = 627.509 // 1 Hartree = 627.5 kcal/mol
let hartreeToKJMol = 2625.5    // 1 Hartree = 2625.5 kJ/mol

// ==============================================================================
// MOLECULAR STRUCTURES FOR HYDROXYLATION REACTION
// ==============================================================================
//
// We model a simplified HYDROXYLATION reaction, the most common P450 pathway:
//
//   R-H + [Fe=O]2+ -> R* + [Fe-OH]2+ -> R-OH + Fe2+
//
// For NISQ tractability, we model the key C-H -> C-OH step using small molecules.
// Real application: Use full CYP active site with QM/MM
//
// Model reaction: CH4 -> CH3OH (methane hydroxylation)
// This captures the essential C-H bond activation chemistry.
//

if not quiet then
    printfn "============================================================"
    printfn "  Drug Metabolism: Reaction Pathway Analysis (VQE)"
    printfn "============================================================"
    printfn ""
    printfn "Configuration:"
    printfn "  Max iterations: %d" maxIterations
    printfn "  Tolerance:      %g Hartree" tolerance
    printfn "  Temperature:    %.1f K (%.1f C)" temperature (temperature - 273.15)
    printfn ""

// -- Reactant: Methane (CH4) - simplified drug substrate

let reactantMethane: Molecule =
    { Name = "Methane (Reactant)"
      Atoms =
          [ { Element = "C"; Position = (0.0, 0.0, 0.0) }
            { Element = "H"; Position = (1.09, 0.0, 0.0) }
            { Element = "H"; Position = (-0.363, 1.028, 0.0) }
            { Element = "H"; Position = (-0.363, -0.514, 0.890) }
            { Element = "H"; Position = (-0.363, -0.514, -0.890) } ]
      Bonds =
          [ { Atom1 = 0; Atom2 = 1; BondOrder = 1.0 }
            { Atom1 = 0; Atom2 = 2; BondOrder = 1.0 }
            { Atom1 = 0; Atom2 = 3; BondOrder = 1.0 }
            { Atom1 = 0; Atom2 = 4; BondOrder = 1.0 } ]
      Charge = 0
      Multiplicity = 1 }

// -- Oxidant: OH radical (simplified; in reality this is the Fe=O moiety of CYP450)

let oxidantOH: Molecule =
    { Name = "Hydroxyl (Oxidant)"
      Atoms =
          [ { Element = "O"; Position = (5.0, 0.0, 0.0) }
            { Element = "H"; Position = (5.97, 0.0, 0.0) } ]
      Bonds = [ { Atom1 = 0; Atom2 = 1; BondOrder = 1.0 } ]
      Charge = 0
      Multiplicity = 2 }

// -- Transition State: [CH3...H...OH]
// The transferring H atom is equidistant between C and O.
// Bond distances stretched (~1.3-1.4 A vs normal 1.1 A).

let transitionState: Molecule =
    { Name = "Transition State [CH3...H...OH]"
      Atoms =
          [ { Element = "C"; Position = (0.0, 0.0, 0.0) }
            { Element = "H"; Position = (-0.363, 1.028, 0.0) }
            { Element = "H"; Position = (-0.363, -0.514, 0.890) }
            { Element = "H"; Position = (-0.363, -0.514, -0.890) }
            { Element = "H"; Position = (1.35, 0.0, 0.0) }   // Stretched C-H (normally 1.09)
            { Element = "O"; Position = (2.65, 0.0, 0.0) }   // O-H forming (normally ~0.97)
            { Element = "H"; Position = (3.3, 0.7, 0.0) } ]
      Bonds =
          [ { Atom1 = 0; Atom2 = 1; BondOrder = 1.0 }
            { Atom1 = 0; Atom2 = 2; BondOrder = 1.0 }
            { Atom1 = 0; Atom2 = 3; BondOrder = 1.0 }
            { Atom1 = 0; Atom2 = 4; BondOrder = 0.5 }  // Breaking C-H bond (partial)
            { Atom1 = 4; Atom2 = 5; BondOrder = 0.5 }  // Forming O-H bond (partial)
            { Atom1 = 5; Atom2 = 6; BondOrder = 1.0 } ]
      Charge = 0
      Multiplicity = 2 }

// -- Product: Methanol (CH3OH)

let productMethanol: Molecule =
    { Name = "Methanol (Product)"
      Atoms =
          [ { Element = "C"; Position = (0.0, 0.0, 0.0) }
            { Element = "H"; Position = (-0.363, 1.028, 0.0) }
            { Element = "H"; Position = (-0.363, -0.514, 0.890) }
            { Element = "H"; Position = (-0.363, -0.514, -0.890) }
            { Element = "O"; Position = (1.43, 0.0, 0.0) }
            { Element = "H"; Position = (1.83, 0.89, 0.0) } ]
      Bonds =
          [ { Atom1 = 0; Atom2 = 1; BondOrder = 1.0 }
            { Atom1 = 0; Atom2 = 2; BondOrder = 1.0 }
            { Atom1 = 0; Atom2 = 3; BondOrder = 1.0 }
            { Atom1 = 0; Atom2 = 4; BondOrder = 1.0 }
            { Atom1 = 4; Atom2 = 5; BondOrder = 1.0 } ]
      Charge = 0
      Multiplicity = 1 }

// Combined reactant system (for energy comparison)
let reactantSystem: Molecule =
    { Name = "CH4 + OH (Separated)"
      Atoms = reactantMethane.Atoms @ oxidantOH.Atoms
      Bonds =
          reactantMethane.Bonds
          @ (oxidantOH.Bonds
             |> List.map (fun b ->
                 { b with
                     Atom1 = b.Atom1 + reactantMethane.Atoms.Length
                     Atom2 = b.Atom2 + reactantMethane.Atoms.Length }))
      Charge = 0
      Multiplicity = 2 }

// Display molecular info
let displayMolecule (mol: Molecule) =
    if not quiet then
        printfn "  %s:" mol.Name
        printfn "    Atoms: %d" mol.Atoms.Length
        printfn "    Electrons: %d" (Molecule.countElectrons mol)
        printfn "    Multiplicity: %d (spin = %d/2)" mol.Multiplicity (mol.Multiplicity - 1)

if not quiet then
    printfn "Reaction Model: C-H Hydroxylation"
    printfn "------------------------------------------------------------"
    printfn ""
    printfn "  Real CYP450 reaction:"
    printfn "    Drug-H + [Fe=O]2+ -> Drug-OH + Fe2+"
    printfn ""
    printfn "  Simplified model (for NISQ):"
    printfn "    CH4 -> [CH3...H...OH] -> CH3OH"
    printfn "           (transition state)"
    printfn ""
    printfn "Molecular Species:"
    printfn "------------------------------------------------------------"
    displayMolecule reactantMethane
    displayMolecule oxidantOH
    displayMolecule transitionState
    displayMolecule productMethanol
    printfn ""

// ==============================================================================
// QUANTUM BACKEND SETUP
// ==============================================================================

let backend = LocalBackend() :> IQuantumBackend

if not quiet then
    printfn "Quantum Backend: %s" backend.Name
    printfn ""

// ==============================================================================
// VQE CALCULATIONS
// ==============================================================================

/// Calculate ground state energy for a molecule using VQE
let calculateEnergy (molecule: Molecule) : float * float =
    let startTime = DateTime.Now

    let config =
        { Method = GroundStateMethod.VQE
          Backend = Some backend
          MaxIterations = maxIterations
          Tolerance = tolerance
          InitialParameters = None
          ProgressReporter = None
          ErrorMitigation = None
          IntegralProvider = None }

    let result = GroundStateEnergy.estimateEnergy molecule config |> Async.RunSynchronously
    let elapsed = (DateTime.Now - startTime).TotalSeconds

    match result with
    | Ok vqeResult -> (vqeResult.Energy, elapsed)
    | Error err ->
        if not quiet then printfn "  Warning: VQE calculation failed: %A" err.Message
        (0.0, elapsed)

if not quiet then
    printfn "VQE Energy Calculations"
    printfn "============================================================"
    printfn ""

// Step 1: Reactant system
if not quiet then
    printfn "Step 1: Reactant System (CH4 + OH)"
    printfn "------------------------------------------------------------"

let (reactantEnergy, reactantTime) = calculateEnergy reactantSystem

if not quiet then
    printfn "  E_reactant = %.6f Hartree (%.2f s)" reactantEnergy reactantTime
    printfn ""

// Step 2: Transition state
if not quiet then
    printfn "Step 2: Transition State [CH3...H...OH]"
    printfn "------------------------------------------------------------"

let (tsEnergy, tsTime) = calculateEnergy transitionState

if not quiet then
    printfn "  E_TS = %.6f Hartree (%.2f s)" tsEnergy tsTime
    printfn ""

// Step 3: Product
if not quiet then
    printfn "Step 3: Product (CH3OH)"
    printfn "------------------------------------------------------------"

let (productEnergy, productTime) = calculateEnergy productMethanol

if not quiet then
    printfn "  E_product = %.6f Hartree (%.2f s)" productEnergy productTime
    printfn ""

// ==============================================================================
// ACTIVATION ENERGY CALCULATION
// ==============================================================================

let activationEnergyHartree = tsEnergy - reactantEnergy
let activationEnergyKcal = activationEnergyHartree * hartreeToKcalMol
let activationEnergyKJ = activationEnergyHartree * hartreeToKJMol

let reactionEnergyHartree = productEnergy - reactantEnergy
let reactionEnergyKcal = reactionEnergyHartree * hartreeToKcalMol

let barrierInterpretation =
    if abs activationEnergyKcal < 5.0 then "Very fast (diffusion limited)"
    elif abs activationEnergyKcal < 15.0 then "Fast (typical enzymatic)"
    elif abs activationEnergyKcal < 25.0 then "Moderate (rate-determining)"
    elif abs activationEnergyKcal < 35.0 then "Slow (requires enzyme catalysis)"
    else "Very slow (unlikely pathway)"

if not quiet then
    printfn "============================================================"
    printfn "  Activation Energy Analysis"
    printfn "============================================================"
    printfn ""
    printfn "Energy Components (Hartree):"
    printfn "  E_reactant = %.6f" reactantEnergy
    printfn "  E_TS       = %.6f" tsEnergy
    printfn "  E_product  = %.6f" productEnergy
    printfn ""
    printfn "Activation Energy (forward reaction):"
    printfn "  Ea = E_TS - E_reactant"
    printfn "  Ea = %.6f Hartree" activationEnergyHartree
    printfn "     = %.2f kcal/mol" activationEnergyKcal
    printfn "     = %.2f kJ/mol" activationEnergyKJ
    printfn ""
    printfn "Reaction Energy (thermodynamics):"
    printfn "  dE = E_product - E_reactant"
    printfn "  dE = %.6f Hartree" reactionEnergyHartree
    printfn "     = %.2f kcal/mol" reactionEnergyKcal
    printfn ""
    printfn "Barrier Assessment: %s" barrierInterpretation
    printfn ""

// ==============================================================================
// RATE CONSTANT CALCULATION (Eyring Equation)
// ==============================================================================

// Eyring equation: k = (kB*T/h) * exp(-Ea/RT)
let eaJoules = activationEnergyKJ * 1000.0 // kJ/mol -> J/mol
let kBT_h = kB * temperature / hPlanck     // prefactor ~6.4e12 s^-1 at 310K
let exponent = -eaJoules / (gasR * temperature)
let rateConstant = kBT_h * exp(exponent)

if not quiet then
    printfn "Rate Constant Estimation (Eyring TST)"
    printfn "------------------------------------------------------------"
    printfn ""
    printfn "  k = (kB*T/h) * exp(-Ea/RT)"
    printfn ""
    printfn "  Temperature:     %.1f K (%.1f C)" temperature (temperature - 273.15)
    printfn "  Prefactor kB*T/h: %.2e s^-1" kBT_h
    printfn "  Activation energy: %.2f kJ/mol" activationEnergyKJ
    printfn ""

let halfLifeStr =
    if rateConstant > 1e-30 && rateConstant < 1e30 then
        let halfLife = 0.693 / rateConstant
        if not quiet then
            printfn "  Rate constant k = %.2e s^-1" rateConstant
        let desc =
            if halfLife < 1e-9 then sprintf "%.2e ns (instantaneous)" (halfLife * 1e9)
            elif halfLife < 1e-6 then sprintf "%.2e us (very fast)" (halfLife * 1e6)
            elif halfLife < 1e-3 then sprintf "%.2e ms (fast)" (halfLife * 1e3)
            elif halfLife < 1.0 then sprintf "%.2e s (moderate)" halfLife
            elif halfLife < 60.0 then sprintf "%.1f s (slow)" halfLife
            elif halfLife < 3600.0 then sprintf "%.1f min (very slow)" (halfLife / 60.0)
            else sprintf "%.1f hours (extremely slow)" (halfLife / 3600.0)
        if not quiet then printfn "  Half-life: %s" desc
        desc
    else
        if not quiet then
            printfn "  Rate constant: Outside reasonable range"
            printfn "  (Check activation energy calculation)"
        "N/A"

if not quiet then printfn ""

// ==============================================================================
// DRUG METABOLISM IMPLICATIONS
// ==============================================================================

// Reference CYP450 activation barriers (kcal/mol)
let referenceBarriers =
    [ ("CYP3A4 hydroxylation", 12.0, 18.0)
      ("CYP2D6 N-dealkylation", 15.0, 22.0)
      ("CYP2C9 aromatic oxidation", 10.0, 16.0)
      ("Spontaneous (non-enzymatic)", 25.0, 40.0) ]

let potentialEnzymes =
    if abs activationEnergyKcal < 20.0 then
        [ "CYP3A4 (major liver enzyme)"
          "CYP2D6 (polymorphic - variable metabolism)"
          "CYP2C9 (warfarin metabolism)" ]
    elif abs activationEnergyKcal < 25.0 then
        [ "CYP1A2 (caffeine metabolism)"
          "CYP2E1 (ethanol metabolism)" ]
    else
        [ "Unlikely to be CYP-mediated"
          "May require different enzyme family" ]

if not quiet then
    printfn "============================================================"
    printfn "  Drug Metabolism Implications"
    printfn "============================================================"
    printfn ""
    printfn "Reference Activation Barriers (kcal/mol):"
    for (pathway, low, high) in referenceBarriers do
        printfn "  %s: %.0f - %.0f" pathway low high
    printfn ""
    printfn "Calculated Barrier: %.1f kcal/mol" (abs activationEnergyKcal)
    printfn ""
    printfn "Potential Metabolizing Enzymes:"
    for enzyme in potentialEnzymes do
        printfn "  - %s" enzyme
    printfn ""
    printfn "Clinical Relevance:"
    printfn "  1. Metabolic rate determines systemic clearance"
    printfn "     Faster metabolism -> shorter half-life -> more frequent dosing"
    printfn "  2. Drug-Drug Interaction Risk:"
    printfn "     Inhibitors: ketoconazole, erythromycin, grapefruit juice"
    printfn "     Inducers: rifampin, carbamazepine, St. John's wort"
    printfn "  3. Pharmacogenomics:"
    printfn "     CYP2D6 poor metabolizers (5-10%% of population)"
    printfn "     Affects codeine->morphine, tamoxifen->endoxifen activation"
    printfn "  4. Toxic Metabolite Risk:"
    printfn "     If barrier to toxic pathway is lower than safe pathway"
    printfn ""

// ==============================================================================
// QUANTUM ADVANTAGE ANALYSIS
// ==============================================================================

if not quiet then
    printfn "Quantum Computing Advantage"
    printfn "------------------------------------------------------------"
    printfn ""
    printfn "  Why quantum matters for reaction pathways:"
    printfn "  1. Transition states are multiconfigurational"
    printfn "     Breaking/forming bonds -> degenerate electronic states"
    printfn "     Classical DFT underestimates barriers by 5-10 kcal/mol"
    printfn "  2. Radical character in CYP450 intermediates"
    printfn "     Spin-state changes during reaction"
    printfn "  3. Near-degeneracy effects"
    printfn "     Multiple low-lying electronic states"
    printfn ""
    printfn "  Method           | Typical Error | Computation"
    printfn "  -----------------|---------------|-------------------"
    printfn "  B3LYP (DFT)      | +/-5 kcal/mol | Minutes"
    printfn "  CCSD(T) (gold)   | +/-1 kcal/mol | Days (small mol)"
    printfn "  VQE (quantum)    | +/-1 kcal/mol | Hours (NISQ)"
    printfn "  VQE (FT-QC)      | +/-1 kcal/mol | Minutes (future)"
    printfn ""
    printfn "  For drug metabolism: 5 kcal/mol error -> 10-100x error in rate constant"
    printfn ""

// ==============================================================================
// STRUCTURED OUTPUT
// ==============================================================================

let totalTime = reactantTime + tsTime + productTime

let resultsList = System.Collections.Generic.List<Map<string, string>>()

// Energy results
let energyResults =
    [ "stage", "energy_calculation"
      "reactant_energy_hartree", sprintf "%.6f" reactantEnergy
      "ts_energy_hartree", sprintf "%.6f" tsEnergy
      "product_energy_hartree", sprintf "%.6f" productEnergy
      "activation_energy_hartree", sprintf "%.6f" activationEnergyHartree
      "activation_energy_kcal_mol", sprintf "%.2f" activationEnergyKcal
      "activation_energy_kj_mol", sprintf "%.2f" activationEnergyKJ
      "reaction_energy_hartree", sprintf "%.6f" reactionEnergyHartree
      "reaction_energy_kcal_mol", sprintf "%.2f" reactionEnergyKcal
      "barrier_assessment", barrierInterpretation
      "thermodynamics", (if reactionEnergyKcal < 0.0 then "exothermic" else "endothermic") ]
    |> Map.ofList

resultsList.Add(energyResults)

// Rate constant results
let rateResults =
    [ "stage", "rate_constant"
      "temperature_k", sprintf "%.1f" temperature
      "rate_constant_s_inv", sprintf "%.2e" rateConstant
      "half_life", halfLifeStr
      "prefactor_s_inv", sprintf "%.2e" kBT_h
      "potential_enzymes", (potentialEnzymes |> String.concat "; ") ]
    |> Map.ofList

resultsList.Add(rateResults)

// Computation summary
let summaryResults =
    [ "stage", "computation_summary"
      "max_iterations", string maxIterations
      "tolerance", sprintf "%g" tolerance
      "reactant_time_s", sprintf "%.2f" reactantTime
      "ts_time_s", sprintf "%.2f" tsTime
      "product_time_s", sprintf "%.2f" productTime
      "total_time_s", sprintf "%.2f" totalTime ]
    |> Map.ofList

resultsList.Add(summaryResults)

let allResults = resultsList |> Seq.toList

match Cli.tryGet "output" args with
| Some path ->
    Reporting.writeJson path allResults
    if not quiet then printfn "Results written to %s" path
| None -> ()

match Cli.tryGet "csv" args with
| Some path ->
    let header =
        [ "stage"
          "reactant_energy_hartree"
          "ts_energy_hartree"
          "product_energy_hartree"
          "activation_energy_kcal_mol"
          "reaction_energy_kcal_mol"
          "barrier_assessment"
          "temperature_k"
          "rate_constant_s_inv"
          "half_life"
          "total_time_s" ]

    let rows =
        allResults
        |> List.map (fun m ->
            header |> List.map (fun h -> m |> Map.tryFind h |> Option.defaultValue ""))

    Reporting.writeCsv path header rows
    if not quiet then printfn "CSV written to %s" path
| None -> ()

// ==============================================================================
// SUMMARY
// ==============================================================================

if not quiet then
    printfn ""
    printfn "============================================================"
    printfn "  Summary"
    printfn "============================================================"
    printfn ""
    printfn "  Calculated C-H hydroxylation reaction pathway"
    printfn "  Activation energy: %.2f kcal/mol (%s)" (abs activationEnergyKcal) barrierInterpretation
    printfn "  Reaction thermodynamics: %.2f kcal/mol (%s)"
        reactionEnergyKcal
        (if reactionEnergyKcal < 0.0 then "exothermic" else "endothermic")
    printfn "  Rate constant estimated via transition state theory"
    printfn "  All computation via IQuantumBackend (quantum compliant)"
    printfn ""
    printfn "  Total computation time: %.2f seconds" totalTime
    printfn ""

if argv.Length = 0 && not quiet then
    printfn "Tip: Run with -- --help to see available options"
    printfn "     --temperature 298.15   (room temperature instead of body temp)"
    printfn "     --max-iterations 100   (more VQE iterations)"
    printfn "     --output results.json  (structured output)"
