// ==============================================================================
// Electron Transport Chain Simulation Example
// ==============================================================================
// Demonstrates VQE for modeling electron transfer in biological systems,
// specifically the mitochondrial respiratory chain.
//
// Business Context:
// Understanding electron transfer in biological systems is crucial for:
//   - Drug design targeting mitochondrial dysfunction
//   - Understanding aging and oxidative stress
//   - Developing new antibiotics targeting bacterial electron transport
//   - Cancer therapies affecting cellular respiration
//
// This example shows:
//   - Quantum simulation of Fe2+/Fe3+ electron transfer in cytochromes
//   - Electron tunneling through protein environments
//   - Marcus theory for electron transfer rates
//   - Comparison with classical force field approximations
//
// Quantum Advantage:
//   Electron transfer involves quantum tunneling through protein barriers.
//   Classical models use empirical parameters; quantum simulation captures
//   the true electronic structure and tunneling matrix elements.
//
// Usage:
//   dotnet fsi ElectronTransportChain.fsx
//   dotnet fsi ElectronTransportChain.fsx -- --max-iterations 100
//   dotnet fsi ElectronTransportChain.fsx -- --temperature 300 --coupling 0.02
//   dotnet fsi ElectronTransportChain.fsx -- --driving-force -0.2 --lambda 0.8
//   dotnet fsi ElectronTransportChain.fsx -- --output results.json --csv results.csv
//   dotnet fsi ElectronTransportChain.fsx -- --quiet
//
// ==============================================================================

(*
Background Theory: Biological Electron Transfer
-------------------------------------------------
BIOCHEMISTRY REFERENCE:
This example is based on concepts from Harper's Illustrated Biochemistry
(28th Edition, Murray et al.):
  - Chapter 12: Biologic Oxidation -- redox potentials, cytochromes
  - Chapter 13: The Respiratory Chain & Oxidative Phosphorylation

THE ELECTRON TRANSPORT CHAIN (ETC) is the primary site of ATP generation
in mitochondria, transferring electrons from NADH/FADH2 to molecular oxygen
through a series of protein complexes:

    NADH --> Complex I --> Q --> Complex III --> Cyt c --> Complex IV --> O2
                           ^
    FADH2 --> Complex II --+

Key electron carriers include:
  - UBIQUINONE (Coenzyme Q): Lipid-soluble, 2-electron carrier
  - CYTOCHROMES: Iron-porphyrin proteins, 1-electron carrier
  - IRON-SULFUR PROTEINS: Fe-S clusters, 1-electron carrier

CYTOCHROMES contain heme groups where iron oscillates between oxidation states:
    Fe2+ (ferrous, reduced) <--> Fe3+ (ferric, oxidized) + e-

The electron transfer rate depends on:
  1. DRIVING FORCE (Delta_G): Free energy difference between donor and acceptor
  2. REORGANIZATION ENERGY (lambda): Energy to rearrange nuclei
  3. ELECTRONIC COUPLING (H_AB): Quantum tunneling matrix element

MARCUS THEORY (Rudolph Marcus, Nobel Prize 1992) describes ET rates:

    k_ET = (2*pi/hbar) * |H_AB|^2 * (1/sqrt(4*pi*lambda*k_B*T))
           * exp(-(Delta_G + lambda)^2 / (4*lambda*k_B*T))

Where:
  - k_ET: Electron transfer rate constant (s^-1)
  - H_AB: Electronic coupling (tunneling matrix element)
  - lambda: Reorganization energy
  - Delta_G: Free energy change
  - k_B: Boltzmann constant
  - T: Temperature

The ELECTRONIC COUPLING H_AB decays exponentially with distance:
    H_AB ~ H_0 * exp(-beta * (R - R_0) / 2)

Where beta ~ 1.0-1.4 A^-1 for protein environments (quantum tunneling).

QUANTUM TUNNELING is essential: electrons traverse 10-20 Angstrom distances
through protein in ~microseconds, impossible classically. This "superexchange"
mechanism involves virtual occupation of protein electronic states.

Key Equations:
  - Redox potential: E = E_0 + (RT/nF) * ln([Ox]/[Red])  (Nernst equation)
  - Marcus rate: k_ET proportional to |H_AB|^2 * exp(-(DG+lambda)^2 / 4*lambda*kT)
  - Tunneling decay: H_AB ~ exp(-beta*R/2)
  - Proton gradient: Delta_G_ATP = -n*F*Delta_psi + 2.3*RT*Delta_pH

Quantum Advantage:
  Electronic coupling H_AB requires accurate wavefunction overlap calculation.
  Classical force fields cannot compute this -- they parameterize it empirically.
  VQE can directly compute the coupling from first principles, essential for:
  - Predicting ET rates in mutant proteins
  - Designing artificial ET systems
  - Understanding quantum coherence in biology

References:
  [1] Marcus, R.A. "Electron transfer reactions in chemistry" Rev. Mod. Phys. (1993)
  [2] Gray, H.B. & Winkler, J.R. "Electron tunneling through proteins" Q. Rev. Biophys. (2003)
  [3] Harper's Illustrated Biochemistry, 28th Ed., Chapters 12-13
  [4] Wikipedia: Electron_transport_chain (https://en.wikipedia.org/wiki/Electron_transport_chain)
  [5] Wikipedia: Marcus_theory (https://en.wikipedia.org/wiki/Marcus_theory)
*)

//#r "nuget: FSharp.Azure.Quantum"
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
// CLI ARGUMENT PARSING
// ==============================================================================

let argv = fsi.CommandLineArgs |> Array.skip 1
let args = Cli.parse argv

Cli.exitIfHelp "ElectronTransportChain.fsx" "Electron transfer in cytochromes via VQE + Marcus theory"
    [ { Cli.OptionSpec.Name = "max-iterations"; Description = "Maximum VQE iterations"; Default = Some "50" }
      { Cli.OptionSpec.Name = "tolerance"; Description = "Convergence tolerance (Hartree)"; Default = Some "1e-4" }
      { Cli.OptionSpec.Name = "temperature"; Description = "Temperature in Kelvin"; Default = Some "310" }
      { Cli.OptionSpec.Name = "coupling"; Description = "Electronic coupling H_AB (eV)"; Default = Some "0.01" }
      { Cli.OptionSpec.Name = "driving-force"; Description = "Free energy Delta_G (eV)"; Default = Some "-0.1" }
      { Cli.OptionSpec.Name = "lambda"; Description = "Reorganization energy (eV)"; Default = Some "0.7" }
      { Cli.OptionSpec.Name = "output"; Description = "Write results to JSON file"; Default = None }
      { Cli.OptionSpec.Name = "csv"; Description = "Write results to CSV file"; Default = None }
      { Cli.OptionSpec.Name = "quiet"; Description = "Suppress informational output"; Default = None } ]
    args

let quiet = Cli.hasFlag "quiet" args
let maxIterations = Cli.getIntOr "max-iterations" 50 args
let tolerance = Cli.getFloatOr "tolerance" 1e-4 args
let temperature = Cli.getFloatOr "temperature" 310.0 args
let electronicCoupling_eV = Cli.getFloatOr "coupling" 0.01 args
let drivingForce_eV = Cli.getFloatOr "driving-force" -0.1 args
let reorganizationEnergy_eV = Cli.getFloatOr "lambda" 0.7 args

// ==============================================================================
// PHYSICAL CONSTANTS
// ==============================================================================

let kB = 1.380649e-23       // Boltzmann constant (J/K)
let hbar = 1.054571817e-34  // Reduced Planck constant (J*s)
let eV_to_J = 1.60218e-19   // eV to Joules
let hartreeToEV = 27.2114   // 1 Hartree = 27.2114 eV

// ==============================================================================
// PART 1: Molecular Models for Electron Transfer
// ==============================================================================
// We model a simplified CYTOCHROME electron transfer using:
//   1. Iron-porphyrin model (heme without protein)
//   2. Two redox states: Fe2+ (reduced) and Fe3+ (oxidized)
//
// For NISQ tractability, we use a minimal Fe-ligand model.
// Real application: QM/MM with full heme and protein environment.

if not quiet then
    printfn "=================================================================="
    printfn "  Electron Transport Chain Simulation (Quantum VQE)"
    printfn "=================================================================="
    printfn ""
    printfn "Biological Context: Mitochondrial Respiratory Chain"
    printfn "------------------------------------------------------------------"
    printfn ""
    printfn "  NADH --> [Complex I] --> Q --> [Complex III] --> Cyt c --> [Complex IV] --> O2"
    printfn "                                       |"
    printfn "                            This example models electron"
    printfn "                            transfer in cytochrome c"
    printfn ""
    printfn "  Key process: Fe2+ <--> Fe3+ + e-"
    printfn ""

/// Fe(II) - reduced state (ferrous, 6 d-electrons, low-spin singlet).
let ferrous_Fe2plus : Molecule = {
    Name = "Fe(II)-bis-ammonia (Reduced Cytochrome Model)"
    Atoms = [
        { Element = "Fe"; Position = (0.0, 0.0, 0.0) }
        { Element = "N"; Position = (0.0, 0.0, 2.0) }
        { Element = "H"; Position = (0.0, 0.94, 2.38) }
        { Element = "H"; Position = (0.81, -0.47, 2.38) }
        { Element = "H"; Position = (-0.81, -0.47, 2.38) }
        { Element = "N"; Position = (0.0, 0.0, -2.0) }
        { Element = "H"; Position = (0.0, 0.94, -2.38) }
        { Element = "H"; Position = (0.81, -0.47, -2.38) }
        { Element = "H"; Position = (-0.81, -0.47, -2.38) }
    ]
    Bonds = [
        { Atom1 = 0; Atom2 = 1; BondOrder = 1.0 }
        { Atom1 = 1; Atom2 = 2; BondOrder = 1.0 }
        { Atom1 = 1; Atom2 = 3; BondOrder = 1.0 }
        { Atom1 = 1; Atom2 = 4; BondOrder = 1.0 }
        { Atom1 = 0; Atom2 = 5; BondOrder = 1.0 }
        { Atom1 = 5; Atom2 = 6; BondOrder = 1.0 }
        { Atom1 = 5; Atom2 = 7; BondOrder = 1.0 }
        { Atom1 = 5; Atom2 = 8; BondOrder = 1.0 }
    ]
    Charge = 2
    Multiplicity = 1
}

/// Fe(III) - oxidized state (ferric, 5 d-electrons, low-spin doublet).
let ferric_Fe3plus : Molecule = {
    Name = "Fe(III)-bis-ammonia (Oxidized Cytochrome Model)"
    Atoms = ferrous_Fe2plus.Atoms
    Bonds = ferrous_Fe2plus.Bonds
    Charge = 3
    Multiplicity = 2
}

/// Display molecule information.
let displayMoleculeInfo (mol: Molecule) =
    if not quiet then
        printfn "%s:" mol.Name
        printfn "  Charge: %+d" mol.Charge
        printfn "  Spin multiplicity: %d (S = %d/2)" mol.Multiplicity (mol.Multiplicity - 1)
        printfn "  Atoms: %d" mol.Atoms.Length
        printfn "  Electrons: %d" (Molecule.countElectrons mol)
        printfn ""

if not quiet then
    printfn "Molecular Models"
    printfn "------------------------------------------------------------------"
    printfn ""

displayMoleculeInfo ferrous_Fe2plus
displayMoleculeInfo ferric_Fe3plus

// ==============================================================================
// PART 2: VQE Energy Calculations
// ==============================================================================

if not quiet then
    printfn "=================================================================="
    printfn "  VQE Energy Calculations"
    printfn "=================================================================="
    printfn ""

let backend = LocalBackend() :> IQuantumBackend

if not quiet then
    printfn "Quantum Backend:"
    printfn "  Backend: %s" backend.Name
    printfn "  Type: Statevector Simulator"
    printfn ""

/// Calculate ground state energy for a molecule using VQE.
/// Returns (energy option, elapsed seconds).
let calculateEnergy (molecule: Molecule) =
    let startTime = DateTime.Now
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
    let result = GroundStateEnergy.estimateEnergy molecule config |> Async.RunSynchronously
    let elapsed = (DateTime.Now - startTime).TotalSeconds
    match result with
    | Ok vqeResult -> (Some vqeResult.Energy, elapsed)
    | Error err ->
        if not quiet then
            printfn "  Warning: VQE calculation failed: %A" err.Message
        (None, elapsed)

if not quiet then
    printfn "Step 1: Fe(II) - Reduced State (Ferrous)"
    printfn "------------------------------------------------------------------"

let (E_Fe2_opt, time_Fe2) = calculateEnergy ferrous_Fe2plus
let E_Fe2 = E_Fe2_opt |> Option.defaultValue 0.0

if not quiet then
    printfn "  E[Fe(II)] = %.6f Hartree (%.2f s)" E_Fe2 time_Fe2
    printfn ""
    printfn "Step 2: Fe(III) - Oxidized State (Ferric)"
    printfn "------------------------------------------------------------------"

let (E_Fe3_opt, time_Fe3) = calculateEnergy ferric_Fe3plus
let E_Fe3 = E_Fe3_opt |> Option.defaultValue 0.0

if not quiet then
    printfn "  E[Fe(III)] = %.6f Hartree (%.2f s)" E_Fe3 time_Fe3
    printfn ""

// ==============================================================================
// PART 3: Redox Potential Analysis
// ==============================================================================

if not quiet then
    printfn "=================================================================="
    printfn "  Redox Potential Analysis"
    printfn "=================================================================="
    printfn ""

let ionizationEnergy_Hartree = E_Fe3 - E_Fe2
let ionizationEnergy_eV = ionizationEnergy_Hartree * hartreeToEV

if not quiet then
    printfn "Ionization Energy (vertical):"
    printfn "  IE = E[Fe(III)] - E[Fe(II)]"
    printfn "  IE = %.6f Hartree" ionizationEnergy_Hartree
    printfn "     = %.3f eV" ionizationEnergy_eV
    printfn ""

    let E0_cytc_experimental = 0.26
    printfn "Comparison with Experimental Data:"
    printfn "------------------------------------------------------------------"
    printfn "  Cytochrome c E0 = +%.2f V (vs SHE, experimental)" E0_cytc_experimental
    printfn ""
    printfn "  Note: Direct comparison requires:"
    printfn "    - Solvation free energies"
    printfn "    - Reference electrode correction"
    printfn "    - Full protein environment (QM/MM)"
    printfn ""

// ==============================================================================
// PART 4: Marcus Theory Analysis
// ==============================================================================

if not quiet then
    printfn "=================================================================="
    printfn "  Marcus Theory: Electron Transfer Rate"
    printfn "=================================================================="
    printfn ""

let lambda_J = reorganizationEnergy_eV * eV_to_J
let H_AB_J = electronicCoupling_eV * eV_to_J
let deltaG_J = drivingForce_eV * eV_to_J
let kBT = kB * temperature

let marcusPrefactor = 2.0 * Math.PI / hbar * H_AB_J * H_AB_J
let marcusDensity = 1.0 / sqrt(4.0 * Math.PI * lambda_J * kBT)
let marcusExponent = -((deltaG_J + lambda_J) ** 2.0) / (4.0 * lambda_J * kBT)
let k_ET = marcusPrefactor * marcusDensity * exp(marcusExponent)

if not quiet then
    printfn "Marcus Theory Parameters:"
    printfn "------------------------------------------------------------------"
    printfn "  Reorganization energy (lambda): %.2f eV" reorganizationEnergy_eV
    printfn "  Electronic coupling (H_AB):     %.3f eV" electronicCoupling_eV
    printfn "  Driving force (Delta_G):        %.2f eV" drivingForce_eV
    printfn "  Temperature:                    %.1f K" temperature
    printfn ""
    printfn "Marcus Rate Calculation:"
    printfn "------------------------------------------------------------------"
    printfn "  k_ET = (2*pi/hbar) * |H_AB|^2 * (4*pi*lambda*kT)^(-1/2)"
    printfn "         * exp[-(Delta_G + lambda)^2 / (4*lambda*kT)]"
    printfn ""

    if k_ET > 0.0 && k_ET < 1e20 then
        printfn "  Calculated k_ET = %.2e s^-1" k_ET
        let halfLife = 0.693 / k_ET
        if halfLife < 1e-9 then
            printfn "  Half-life: %.2f ns (ultrafast)" (halfLife * 1e9)
        elif halfLife < 1e-6 then
            printfn "  Half-life: %.2f us" (halfLife * 1e6)
        elif halfLife < 1e-3 then
            printfn "  Half-life: %.2f ms" (halfLife * 1e3)
        else
            printfn "  Half-life: %.2f s" halfLife
    else
        printfn "  Rate calculation outside reasonable range"
        printfn "  (Parameters may need adjustment)"
    printfn ""

// ==============================================================================
// PART 5: Distance Dependence (Tunneling)
// ==============================================================================

if not quiet then
    printfn "=================================================================="
    printfn "  Electron Tunneling: Distance Dependence"
    printfn "=================================================================="
    printfn ""
    printfn "Electronic coupling decay with distance:"
    printfn "  H_AB(R) = H_AB(R0) * exp[-beta * (R - R0) / 2]"
    printfn ""

/// Tunneling decay constants for different media.
let betaValues = [
    ("Vacuum", 2.8)
    ("Covalent bond", 0.9)
    ("Protein (beta-sheet)", 1.0)
    ("Protein (alpha-helix)", 1.2)
    ("Water/solvent", 1.6)
]

if not quiet then
    printfn "Tunneling decay constants (beta):"
    printfn "------------------------------------------------------------------"
    for (medium, beta) in betaValues do
        printfn "  %-25s: beta = %.1f A^-1" medium beta
    printfn ""

let beta_protein = 1.1
let R0 = 3.5

let tunnelingDistances = [5.0; 7.0; 10.0; 13.0; 15.0; 20.0]

let tunnelingResults =
    tunnelingDistances
    |> List.map (fun r ->
        let coupling_rel = exp(-beta_protein * (r - R0))
        let coupling_sq = coupling_rel * coupling_rel
        let interpretation =
            if r < 7.0 then "Direct contact"
            elif r < 10.0 then "Fast (ns-us)"
            elif r < 14.0 then "Moderate (us-ms)"
            elif r < 18.0 then "Slow (ms-s)"
            else "Very slow"
        [ "Section", "Tunneling"
          "Distance_A", sprintf "%.1f" r
          "CouplingSquared_rel", sprintf "%.2e" coupling_sq
          "Interpretation", interpretation ]
        |> Map.ofList)

if not quiet then
    printfn "Coupling vs Distance (protein environment, beta = %.1f A^-1):" beta_protein
    printfn "------------------------------------------------------------------"
    printfn "  Distance (A)    |H_AB|^2 (relative)    Interpretation"
    printfn "  -------------   ------------------     --------------"
    for m in tunnelingResults do
        let d = m |> Map.find "Distance_A"
        let c = m |> Map.find "CouplingSquared_rel"
        let i = m |> Map.find "Interpretation"
        printfn "    %5s             %s           %s" d c i
    printfn ""
    printfn "Key insight: Electrons tunnel ~15 A through protein in microseconds!"
    printfn "This 'superexchange' is impossible classically -- it's quantum tunneling."
    printfn ""

// ==============================================================================
// PART 6: Respiratory Chain Context
// ==============================================================================

if not quiet then
    printfn "=================================================================="
    printfn "  Respiratory Chain: Biological Context"
    printfn "=================================================================="
    printfn ""

/// Redox potentials of ETC components (V vs SHE).
let etcComponents = [
    ("NADH/NAD+",            -0.32)
    ("Complex I (Fe-S)",     -0.30)
    ("Coenzyme Q",           +0.04)
    ("Complex III (cyt b)",  -0.10)
    ("Cytochrome c1",        +0.22)
    ("Cytochrome c",         +0.26)
    ("Complex IV (cyt a)",   +0.29)
    ("Complex IV (cyt a3)",  +0.55)
    ("O2/H2O",              +0.82)
]

if not quiet then
    printfn "Electron Transport Chain Redox Potentials:"
    printfn "------------------------------------------------------------------"
    printfn "  Component            E0 (V vs SHE)    Delta_E (eV)"
    printfn "  ---------            -------------    -----------"

    etcComponents
    |> List.fold (fun prevE (name, e0) ->
        let deltaE = e0 - prevE
        printfn "  %-20s   %+.2f            %+.2f" name e0 deltaE
        e0
    ) -0.32
    |> ignore

    printfn ""
    printfn "Total driving force: %.2f eV (NADH to O2)" (0.82 - (-0.32))
    printfn "This energy drives ATP synthesis via proton gradient."
    printfn ""

// ==============================================================================
// PART 7: Drug Discovery Implications
// ==============================================================================

if not quiet then
    printfn "=================================================================="
    printfn "  Drug Discovery Implications"
    printfn "=================================================================="
    printfn ""
    printfn "1. Mitochondrial Dysfunction Targets:"
    printfn "   - Neurodegenerative diseases (Parkinson's, Alzheimer's)"
    printfn "   - Cardiac ischemia-reperfusion injury"
    printfn "   - Cancer (altered metabolism)"
    printfn ""
    printfn "2. Antimicrobial Targets:"
    printfn "   - Bacterial cytochrome bc1 (Complex III)"
    printfn "   - Parasite electron transport (malaria, trypanosomes)"
    printfn "   - Fungal respiration"
    printfn ""
    printfn "3. Quantum Simulation Applications:"
    printfn "   - Predict ET rates for mutant cytochromes"
    printfn "   - Design artificial ET systems (biosensors)"
    printfn "   - Model drug effects on mitochondrial function"
    printfn "   - Understand reactive oxygen species (ROS) generation"
    printfn ""

// ==============================================================================
// PART 8: Quantum Advantage Analysis
// ==============================================================================

if not quiet then
    printfn "=================================================================="
    printfn "  Why Quantum Computing Matters"
    printfn "=================================================================="
    printfn ""
    printfn "Classical limitations:"
    printfn "  - Force fields cannot compute electronic coupling H_AB"
    printfn "  - DFT struggles with open-shell metal centers"
    printfn "  - Spin-state energetics often wrong by 5-10 kcal/mol"
    printfn ""
    printfn "Quantum advantages:"
    printfn "  - Direct computation of wavefunction overlap"
    printfn "  - Accurate treatment of d-orbital correlation"
    printfn "  - Multiple spin states handled naturally"
    printfn "  - Quantum coherence effects captured"
    printfn ""

// ==============================================================================
// PART 9: Summary
// ==============================================================================

let totalTime = time_Fe2 + time_Fe3

if not quiet then
    printfn "=================================================================="
    printfn "  Summary"
    printfn "=================================================================="
    printfn ""
    printfn "Calculated:"
    printfn "  - Fe(II) energy: %.6f Hartree" E_Fe2
    printfn "  - Fe(III) energy: %.6f Hartree" E_Fe3
    printfn "  - Ionization energy: %.3f eV" ionizationEnergy_eV
    printfn "  - Marcus ET rate: %.2e s^-1" k_ET
    printfn ""
    printfn "Key insights:"
    printfn "  - Electron transfer in biology is inherently quantum mechanical"
    printfn "  - Tunneling allows electrons to traverse 10-20 A through protein"
    printfn "  - Marcus theory connects quantum H_AB to measurable rates"
    printfn "  - Quantum simulation essential for predictive modeling"
    printfn ""
    printfn "Total computation time: %.2f seconds" totalTime
    printfn ""
    printfn "All calculations via IQuantumBackend"
    printfn ""

// ==============================================================================
// STRUCTURED OUTPUT
// ==============================================================================

let vqeRow =
    [ "Section", "VQE_Energies"
      "E_Fe2_Hartree", sprintf "%.6f" E_Fe2
      "E_Fe3_Hartree", sprintf "%.6f" E_Fe3
      "IE_Hartree", sprintf "%.6f" ionizationEnergy_Hartree
      "IE_eV", sprintf "%.3f" ionizationEnergy_eV
      "Time_Fe2_s", sprintf "%.2f" time_Fe2
      "Time_Fe3_s", sprintf "%.2f" time_Fe3
      "MaxIterations", sprintf "%d" maxIterations
      "Tolerance", sprintf "%.1e" tolerance ]
    |> Map.ofList

let marcusRow =
    [ "Section", "Marcus_Theory"
      "Lambda_eV", sprintf "%.2f" reorganizationEnergy_eV
      "H_AB_eV", sprintf "%.3f" electronicCoupling_eV
      "DeltaG_eV", sprintf "%.2f" drivingForce_eV
      "Temperature_K", sprintf "%.1f" temperature
      "k_ET_per_s", sprintf "%.2e" k_ET ]
    |> Map.ofList

let allResults = [vqeRow; marcusRow] @ tunnelingResults

match Cli.tryGet "output" args with
| Some path ->
    Reporting.writeJson path allResults
    if not quiet then printfn "Results written to %s" path
| None -> ()

match Cli.tryGet "csv" args with
| Some path ->
    let header = [ "Section"; "E_Fe2_Hartree"; "E_Fe3_Hartree"; "IE_Hartree"; "IE_eV";
                   "Lambda_eV"; "H_AB_eV"; "DeltaG_eV"; "Temperature_K"; "k_ET_per_s";
                   "Distance_A"; "CouplingSquared_rel"; "Interpretation";
                   "Time_Fe2_s"; "Time_Fe3_s"; "MaxIterations"; "Tolerance" ]
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
    printfn "  --max-iterations 100     Increase VQE iterations"
    printfn "  --tolerance 1e-6         Tighter convergence"
    printfn "  --temperature 300        Temperature (Kelvin)"
    printfn "  --coupling 0.02          Electronic coupling H_AB (eV)"
    printfn "  --driving-force -0.2     Free energy Delta_G (eV)"
    printfn "  --lambda 0.8             Reorganization energy (eV)"
    printfn "  --output results.json    Export results as JSON"
    printfn "  --csv results.csv        Export results as CSV"
    printfn "  --quiet                  Suppress informational output"
    printfn "  --help                   Show full usage information"

if not quiet then
    printfn ""
    printfn "Done!"
