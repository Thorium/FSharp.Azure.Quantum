// ==============================================================================
// Mott Metal-Insulator Transition Example
// ==============================================================================
// Demonstrates VQE for studying the Mott transition - the abrupt change from
// metal to insulator driven by electron correlation, with application to
// smart windows, neuromorphic computing, and strongly correlated materials.
//
// Business Context:
// Understanding Mott transitions is crucial for:
// - Smart windows (VO2 thermochromic coatings)
// - Neuromorphic computing (Mott memristors)
// - High-temperature superconductors (cuprate parent compounds)
// - Oxide electronics (correlated oxide devices)
// - Battery materials (lithium cobaltate transitions)
//
// Experimental context:
// Strongly correlated phases are frequently mapped using scattering and
// spectroscopy; synchrotron radiation and spallation neutron sources are common
// infrastructure for these measurements.
//
// This example shows:
// - Mott criterion for metal-insulator transition
// - Hubbard model for electron correlation
// - Doping-induced transitions (P-doped Si)
// - Temperature-driven transitions (VO2)
// - VQE simulation of correlated electron systems
//
// Quantum Advantage:
// The Mott transition is fundamentally a many-body quantum effect.
// Classical methods fail because:
// - Mean-field theory misses correlations
// - DFT underestimates Mott gaps
// - Exact diagonalization limited to tiny systems
//
// THEORETICAL FOUNDATION:
// Based on "Concepts of Materials Science" by Adrian P. Sutton
// Chapter 8: Electrical Resistance of Metals
// Section 8.3.1: Metal-Insulator Transition (pp. 96-98)
//
// BACKGROUND THEORY:
// -------------------------------------------------------------------------------
// THE MOTT TRANSITION is a dramatic quantum phase transition where a material
// suddenly changes from metallic (conducting) to insulating (non-conducting)
// due to electron-electron interactions.
//
// MOTT CRITERION (Sutton p. 97):
// Mott predicted that a metal becomes an insulator when:
//   n_to_one_third times a_H is approximately 0.26
// where n = electron density, a_H = effective Bohr radius
//
// HUBBARD MODEL:
// The simplest model capturing the Mott transition:
//   H = -t Sum(hopping) + U Sum(double_occupancy)
// For U/t >> 1: MOTT INSULATOR
// For U/t << 1: METAL
//
// References:
//   [1] Sutton, A.P. "Concepts of Materials Science" Ch.8 (Oxford, 2021)
//   [2] Wikipedia: Mott_insulator
//   [3] Mott, N.F. "Metal-Insulator Transitions" (Taylor and Francis, 1990)
//
// Usage:
//   dotnet fsi MottTransition.fsx                                         (defaults)
//   dotnet fsi MottTransition.fsx -- --help                               (show options)
//   dotnet fsi MottTransition.fsx -- --material Si                        (filter by material)
//   dotnet fsi MottTransition.fsx -- --material all                       (all materials)
//   dotnet fsi MottTransition.fsx -- --hopping 0.2                        (Hubbard t in eV)
//   dotnet fsi MottTransition.fsx -- --output results.json --csv results.csv
//   dotnet fsi MottTransition.fsx -- --quiet --output results.json        (pipeline mode)
//
// ==============================================================================

#load "_materialsCommon.fsx"
#load "../_common/Cli.fs"
#load "../_common/Data.fs"
#load "../_common/Reporting.fs"

open System
open FSharp.Azure.Quantum.QuantumChemistry
open FSharp.Azure.Quantum.Core
open FSharp.Azure.Quantum.Core.BackendAbstraction
open FSharp.Azure.Quantum.Examples.Common
open MaterialsCommon

// ==============================================================================
// CLI ARGUMENT PARSING
// ==============================================================================

let argv = fsi.CommandLineArgs |> Array.skip 1
let args = Cli.parse argv

Cli.exitIfHelp
    "MottTransition.fsx"
    "VQE simulation of Mott metal-insulator transition driven by electron correlation."
    [ { Cli.OptionSpec.Name = "material"; Description = "Filter by material: Si, Ge, VO2, V2O3, NdNiO3, or all"; Default = Some "all" }
      { Cli.OptionSpec.Name = "hopping"; Description = "Hubbard hopping parameter t (eV)";                       Default = Some "0.1" }
      { Cli.OptionSpec.Name = "output";  Description = "Write results to JSON file";                              Default = None }
      { Cli.OptionSpec.Name = "csv";     Description = "Write results to CSV file";                               Default = None }
      { Cli.OptionSpec.Name = "quiet";   Description = "Suppress informational output";                           Default = None } ]
    args

let quiet = Cli.hasFlag "quiet" args
let materialFilter = args |> Cli.getOr "material" "all"
let t_estimate = args |> Cli.getFloatOr "hopping" 0.1
let outputPath = Cli.tryGet "output" args
let csvPath = Cli.tryGet "csv" args

// ==============================================================================
// PHYSICAL CONSTANTS
// ==============================================================================

/// Bohr radius (m)
let a_0 = 5.29177e-11

/// Bohr radius (Angstroms)
let a_0_A = 0.529177

// ==============================================================================
// MATERIAL DATA FOR MOTT TRANSITIONS
// ==============================================================================

/// Material properties relevant for Mott transition
type MottMaterial = {
    Name: string
    ShortName: string           // Key for CLI filtering
    DielectricConstant: float   // epsilon_r
    EffectiveMass: float        // m_star/m_e
    CriticalDensity: float      // n_c (per cm cubed) for Mott transition
    TransitionType: string      // "Doping" or "Temperature"
    TransitionTemp: float option // K (for temperature-driven)
}

let SiliconPDoped = {
    Name = "P-doped Silicon"
    ShortName = "Si"
    DielectricConstant = 11.7
    EffectiveMass = 0.26
    CriticalDensity = 3.7e18    // Sutton p. 97
    TransitionType = "Doping"
    TransitionTemp = None
}

let GermaniumAsDoped = {
    Name = "As-doped Germanium"
    ShortName = "Ge"
    DielectricConstant = 16.0
    EffectiveMass = 0.12
    CriticalDensity = 1.0e17
    TransitionType = "Doping"
    TransitionTemp = None
}

let VO2 = {
    Name = "Vanadium Dioxide (VO2)"
    ShortName = "VO2"
    DielectricConstant = 36.0
    EffectiveMass = 3.0
    CriticalDensity = 3.0e21
    TransitionType = "Temperature"
    TransitionTemp = Some 341.0  // 68C
}

let V2O3 = {
    Name = "Vanadium Sesquioxide (V2O3)"
    ShortName = "V2O3"
    DielectricConstant = 12.0
    EffectiveMass = 5.0
    CriticalDensity = 1.0e22
    TransitionType = "Temperature"
    TransitionTemp = Some 150.0  // Under pressure
}

let NdNiO3 = {
    Name = "Neodymium Nickelate (NdNiO3)"
    ShortName = "NdNiO3"
    DielectricConstant = 20.0
    EffectiveMass = 4.0
    CriticalDensity = 5.0e21
    TransitionType = "Temperature"
    TransitionTemp = Some 200.0
}

// ==============================================================================
// MOTT CRITERION CALCULATIONS
// ==============================================================================

/// Calculate effective Bohr radius
/// a_H = epsilon * (m_e/m_star) * a_0
let effectiveBohrRadius (material: MottMaterial) : float =
    material.DielectricConstant * (1.0 / material.EffectiveMass) * a_0_A

/// Calculate Mott criterion parameter
/// n^(1/3) * a_H
let mottParameter (density_per_cm3: float) (effectiveBohr_A: float) : float =
    let n_per_A3 = density_per_cm3 * 1.0e-24  // Convert per cm cubed to per A cubed
    Math.Pow(n_per_A3, 1.0/3.0) * effectiveBohr_A

/// Predict phase based on Mott criterion
/// Metal if n^(1/3) * a_H > 0.26
let predictPhase (mottParam: float) : string =
    if mottParam > 0.26 then "METAL"
    else "INSULATOR"

/// Calculate critical density from Mott criterion
/// n_c = (0.26 / a_H) cubed
let criticalDensity (effectiveBohr_A: float) : float =
    let n_c_per_A3 = Math.Pow(0.26 / effectiveBohr_A, 3.0)
    n_c_per_A3 * 1.0e24  // Convert per A cubed to per cm cubed

// ==============================================================================
// HUBBARD MODEL PARAMETERS
// ==============================================================================

/// Calculate Hubbard U from screened Coulomb
/// U approx e squared / (4*pi*epsilon_0*epsilon*a_H)
let hubbardU (material: MottMaterial) : float =
    let a_H = effectiveBohrRadius material * 1.0e-10  // Convert to meters
    let U_J = e_charge * e_charge / (4.0 * Math.PI * 8.854e-12 * material.DielectricConstant * a_H)
    U_J * J_to_eV  // Convert to eV

/// Calculate bandwidth W from hopping
/// W approx 2zt where z = coordination number (about 6 for 3D)
let bandwidth (hopping_eV: float) : float =
    2.0 * 6.0 * hopping_eV

/// Calculate U/W ratio (controls Mott transition)
let correlationStrength (U_eV: float) (W_eV: float) : float =
    U_eV / W_eV

// ==============================================================================
// QUANTUM BACKEND SETUP
// ==============================================================================

if not quiet then
    printfn "=================================================================="
    printfn "   Mott Metal-Insulator Transition"
    printfn "=================================================================="
    printfn ""

    printfn "Theoretical Foundation: Sutton 'Concepts of Materials Science'"
    printfn "------------------------------------------------------------------"
    printfn "  Chapter 8: Electrical Resistance of Metals"
    printfn "  Section 8.3.1: Metal-Insulator Transition (pp. 96-98)"
    printfn ""

    printfn "Quantum Backend"
    printfn "------------------------------------------------------------------"
    printfn "  Backend: %s" backend.Name
    printfn "  Type: Statevector Simulator"
    printfn ""

// ==============================================================================
// PART 1: MOTT CRITERION
// ==============================================================================

if not quiet then
    printfn "=================================================================="
    printfn "   Part 1: Mott Criterion for Metal-Insulator Transition"
    printfn "=================================================================="
    printfn ""

    printfn "Mott's criterion: A material is metallic when electron wavefunctions"
    printfn "overlap significantly. This occurs when:"
    printfn ""
    printfn "  n^(1/3) * a_H > 0.26"
    printfn ""
    printfn "where:"
    printfn "  n   = carrier density"
    printfn "  a_H = effective Bohr radius = epsilon * (m_e/m_star) * a_0"
    printfn ""

let materials = [SiliconPDoped; GermaniumAsDoped; VO2; V2O3; NdNiO3]

/// All available Mott materials
let allMaterials = materials

/// Materials filtered by --material CLI argument
let filteredMaterials =
    if materialFilter.ToLowerInvariant() = "all" then allMaterials
    else
        allMaterials
        |> List.filter (fun m ->
            m.ShortName.Equals(materialFilter, StringComparison.OrdinalIgnoreCase))

if not quiet then
    printfn "Effective Bohr radii for various materials:"
    printfn "------------------------------------------------------------------"
    printfn "  Material                      eps_r  m*/m_e   a_H (A)"
    printfn "  --------                      -----  ------   -------"

    for mat in filteredMaterials do
        let a_H = effectiveBohrRadius mat
        printfn "  %-28s  %.1f    %.2f     %.1f" mat.Name mat.DielectricConstant mat.EffectiveMass a_H

    printfn ""

// ==============================================================================
// PART 2: PHOSPHORUS-DOPED SILICON (Sutton Example)
// ==============================================================================

if not quiet then
    printfn "=================================================================="
    printfn "   Part 2: P-doped Silicon Mott Transition"
    printfn "=================================================================="
    printfn ""

    printfn "Phosphorus-doped silicon shows a classic Mott transition:"
    printfn "  - Below n_c: INSULATOR (localized donor states)"
    printfn "  - Above n_c: METAL (overlapping wavefunctions)"
    printfn ""

let a_H_Si = effectiveBohrRadius SiliconPDoped
let n_c_Si_calc = criticalDensity a_H_Si

if not quiet then
    printfn "P-doped Silicon calculation:"
    printfn "------------------------------------------------------------------"
    printfn "  Effective Bohr radius: a_H = %.1f A" a_H_Si
    printfn "  Calculated n_c: %.2e per cm cubed" n_c_Si_calc
    printfn "  Experimental n_c: %.2e per cm cubed (Sutton)" SiliconPDoped.CriticalDensity
    printfn ""

let dopingLevels = [1.0e17; 5.0e17; 1.0e18; 3.0e18; 5.0e18; 1.0e19; 5.0e19]

if not quiet then
    printfn "Phase diagram for P-doped Si:"
    printfn "------------------------------------------------------------------"
    printfn "  n (per cm3)  n^(1/3)*a_H    Phase"
    printfn "  -----------  -----------    -----"

    for n in dopingLevels do
        let mottP = mottParameter n a_H_Si
        let phase = predictPhase mottP
        printfn "  %.1e       %.3f          %s" n mottP phase

    printfn ""
    printfn "Transition occurs at n^(1/3)*a_H approx 0.26"
    printfn ""

// ==============================================================================
// PART 3: COOPERATIVE TUNNELING MECHANISM
// ==============================================================================

if not quiet then
    printfn "=================================================================="
    printfn "   Part 3: Cooperative Tunneling (Why Transition is Sharp)"
    printfn "=================================================================="
    printfn ""

    printfn "The Mott transition is FIRST-ORDER (discontinuous) due to"
    printfn "positive feedback between tunneling and screening:"
    printfn ""

    printfn "  +---------------------+"
    printfn "  |                     |"
    printfn "  V                     |"
    printfn "  +---------------+   +-+---------------+"
    printfn "  | More carriers |   | Better          |"
    printfn "  | tunneling     |-->| screening       |"
    printfn "  +---------------+   +-+---------------+"
    printfn "  |                     |"
    printfn "  |                     V"
    printfn "  |   +-------------------------+"
    printfn "  |   | Reduced Coulomb         |"
    printfn "  |   | barrier to tunneling    |"
    printfn "  |   +------------+------------+"
    printfn "  |                |"
    printfn "  +----------------+"
    printfn ""
    printfn "This avalanche effect makes the transition ABRUPT!"
    printfn ""

// ==============================================================================
// PART 4: HUBBARD MODEL
// ==============================================================================

if not quiet then
    printfn "=================================================================="
    printfn "   Part 4: Hubbard Model Parameters"
    printfn "=================================================================="
    printfn ""

    printfn "The Hubbard model captures the competition between:"
    printfn "  - Kinetic energy (hopping, t): favors delocalization -> METAL"
    printfn "  - Coulomb repulsion (U): favors localization -> INSULATOR"
    printfn ""
    printfn "  H = -t Sum (hopping) + U Sum (double occupancy)"
    printfn ""
    printfn "  When U/W > 1 (W = bandwidth approx 2zt): MOTT INSULATOR"
    printfn "  When U/W < 1: METAL"
    printfn ""

    printfn "Hubbard model parameters (t = %.2f eV):" t_estimate
    printfn "------------------------------------------------------------------"
    printfn "  Material                      U (eV)   W (eV)   U/W    Phase"
    printfn "  --------                      ------   ------   ---    -----"

    for mat in filteredMaterials do
        let U = hubbardU mat
        let W = bandwidth t_estimate
        let ratio = correlationStrength U W
        let phase = if ratio > 1.0 then "Insulator" else "Metal"
        printfn "  %-28s  %.2f     %.2f     %.2f   %s" mat.Name U W ratio phase

    printfn ""

// ==============================================================================
// PART 5: TEMPERATURE-DRIVEN TRANSITIONS
// ==============================================================================

if not quiet then
    printfn "=================================================================="
    printfn "   Part 5: Temperature-Driven Mott Transitions"
    printfn "=================================================================="
    printfn ""

    printfn "Some materials show Mott transitions driven by temperature:"
    printfn ""

let tempMaterials = filteredMaterials |> List.filter (fun m -> m.TransitionTemp.IsSome)

if not quiet then
    printfn "Temperature-driven Mott insulators:"
    printfn "------------------------------------------------------------------"
    printfn "  Material                      T_c (K)    T_c (C)    Application"
    printfn "  --------                      -------    -------    -----------"

    for mat in tempMaterials do
        let T_K = mat.TransitionTemp.Value
        let T_C = T_K - 273.15
        let app = 
            if mat.Name.Contains("VO2") then "Smart windows"
            elif mat.Name.Contains("NdNiO") then "Neuromorphic"
            else "Research"
        printfn "  %-28s  %.0f        %.0f         %s" mat.Name T_K T_C app

    printfn ""
    printfn "VO2 is particularly interesting: transitions at 68C!"
    printfn "This enables thermochromic 'smart windows' that automatically"
    printfn "block infrared light when hot (summer) and transmit it when"
    printfn "cold (winter)."
    printfn ""

// ==============================================================================
// PART 6: VQE SIMULATION OF CORRELATED ELECTRONS
// ==============================================================================

if not quiet then
    printfn "=================================================================="
    printfn "   Part 6: VQE Simulation of Electron Correlation"
    printfn "=================================================================="
    printfn ""

    printfn "The Mott transition requires treating electron correlation exactly."
    printfn "VQE can capture these effects that mean-field theories miss."
    printfn ""

// ============================================================================
// WHY H2 IS A LEGITIMATE HUBBARD MODEL ANALOG
// ============================================================================
//
// The stretched H2 molecule is a TEXTBOOK example for studying the Mott
// transition. This is NOT a fake model - it's the simplest exact realization
// of Hubbard physics. Here's why:
//
// 1. HALF-FILLING: H2 has 2 electrons on 2 "sites" (atoms) = half-filled
//    This is exactly the regime where Mott physics is most important
//
// 2. TUNABLE U/t RATIO:
//    - At equilibrium (0.74 A): Strong overlap -> large t -> METALLIC
//    - At large separation (3+ A): Weak overlap -> small t -> INSULATING
//    - The H2 dissociation curve spans the entire Mott transition!
//
// 3. EXACT MAPPING TO HUBBARD MODEL:
//    The H2 Hamiltonian in a minimal basis maps EXACTLY to a 2-site Hubbard:
//      H = -t(c1_dag c2 + c2_dag c1) + U(n1_up n1_down + n2_up n2_down)
//    where t = hopping integral, U = on-site Coulomb repulsion
//
// 4. BENCHMARK SYSTEM:
//    H2 dissociation is THE standard test for many-body methods because:
//    - Exact solution is known (FCI with minimal basis)
//    - Single-reference methods (HF, MP2) fail catastrophically at large R
//    - DFT gives qualitatively wrong dissociation curves
//    - Correct physics REQUIRES correlation (exactly what Mott transition needs)
//
// 5. PHYSICAL INTERPRETATION:
//    - Small R: Electrons delocalize over both atoms (metallic bonding)
//    - Large R: Electrons localize, one per atom (Mott insulator = two H atoms)
//    - The "Mott gap" is the energy cost to move an electron between atoms
//
// References:
//   [1] Hubbard, J. Proc. R. Soc. London A 276, 238 (1963) - original Hubbard paper
//   [2] Lieb & Wu, Phys. Rev. Lett. 20, 1445 (1968) - exact 1D Hubbard solution
//   [3] Dagotto, Rev. Mod. Phys. 66, 763 (1994) - review of correlated electrons
//   [4] Cohen, Mori-Sanchez & Yang, Chem. Rev. 112, 289 (2012) - DFT failures
//
// ============================================================================

/// Create H2 at varying separation - the simplest Mott model
/// This is the TEXTBOOK system for studying metal-insulator transitions:
/// - Small R (< 1 A): Metallic (electrons delocalized)
/// - Large R (> 3 A): Mott insulator (electrons localized, one per atom)
let createH2MottModel (separation_A: float) : Molecule =
    {
        Name = sprintf "H2_R=%.2fA" separation_A
        Atoms = [
            { Element = "H"; Position = (0.0, 0.0, 0.0) }
            { Element = "H"; Position = (separation_A, 0.0, 0.0) }
        ]
        Bonds = [
            { Atom1 = 0; Atom2 = 1; BondOrder = 1.0 }
        ]
        Charge = 0
        Multiplicity = 1  // Singlet ground state (antiferromagnetic at large R)
    }

/// Run VQE for a molecule and return result row for structured output
let runVqe (label: string) (description: string) (molecule: Molecule) : Map<string, string> =
    if not quiet then
        printfn "%s:" label
        printfn "   Molecule: %s" molecule.Name
        printfn "   %s" description

    match calculateVQEEnergy backend molecule with
    | Ok (energy, iterations, time) ->
        if not quiet then
            printfn "   VQE Ground State Energy: %.6f Hartree" energy
            printfn "   Iterations: %d, Time: %.2f s" iterations time
            printfn ""
        Map.ofList [
            "molecule", molecule.Name
            "label", label
            "energy_hartree", sprintf "%.6f" energy
            "iterations", sprintf "%d" iterations
            "time_seconds", sprintf "%.2f" time
            "status", "OK"
        ]
    | Error msg ->
        if not quiet then
            printfn "   Error: %s" msg
            printfn ""
        Map.ofList [
            "molecule", molecule.Name
            "label", label
            "energy_hartree", "N/A"
            "iterations", "N/A"
            "time_seconds", "N/A"
            "status", sprintf "Error: %s" msg
        ]

// ============================================================================
// VQE CALCULATION: H2 dissociation as Mott transition model
// ============================================================================

if not quiet then
    printfn "VQE Results for H2 Dissociation (Mott Transition Model):"
    printfn "------------------------------------------------------------------"
    printfn ""
    printfn "H2 dissociation is THE textbook model for the Mott transition:"
    printfn "  - Small R: Electrons delocalized (metallic bonding)"
    printfn "  - Large R: Electrons localized, one per atom (Mott insulator)"
    printfn ""

// Key bond lengths spanning the Mott transition
let separations = [
    (0.74, "Equilibrium (metallic)")
    (1.50, "Stretched (correlated)")
    (2.50, "Dissociating (Mott)")
    (4.00, "Separated (insulator)")
]

let vqeResults =
    separations
    |> List.map (fun (sep, regime) ->
        let model = createH2MottModel sep
        let label = sprintf "H2 R=%.2f A (%s)" sep regime
        let result = runVqe label regime model
        result |> Map.add "separation_A" (sprintf "%.2f" sep) |> Map.add "regime" regime)

// Extract equilibrium energy for delta-E calculations
let equilibriumEnergy =
    vqeResults
    |> List.tryHead
    |> Option.bind (fun m -> m |> Map.tryFind "energy_hartree")
    |> Option.bind (fun s -> match Double.TryParse s with true, v -> Some v | _ -> None)

if not quiet then
    printfn "Dissociation Curve Summary:"
    printfn "  R (A)    Regime              VQE E (Ha)    E - E_eq (eV)"
    printfn "  -----    ------              ----------    -------------"

    for result in vqeResults do
        let sep = result |> Map.tryFind "separation_A" |> Option.defaultValue "?"
        let regime = result |> Map.tryFind "regime" |> Option.defaultValue "?"
        let energyStr = result |> Map.tryFind "energy_hartree" |> Option.defaultValue "N/A"
        let deltaStr =
            match equilibriumEnergy with
            | Some eEq ->
                match Double.TryParse energyStr with
                | true, e -> sprintf "%.3f" ((e - eEq) * hartreeToEV)
                | _ -> "N/A"
            | None -> "N/A"
        printfn "  %-7s  %-20s  %-12s  %s" sep regime energyStr deltaStr

    printfn ""
    printfn "Physical Interpretation:"
    printfn "  - At R = 0.74 A: Strong bond, electrons shared (metallic character)"
    printfn "  - At R > 2.5 A: Bond breaking, electrons localize (Mott insulator)"
    printfn "  - The energy gap to move an electron = 'Mott gap'"
    printfn ""
    printfn "This is why H2 dissociation is a stringent test for quantum methods:"
    printfn "  - Hartree-Fock fails completely (wrong dissociation limit)"
    printfn "  - Standard DFT gives qualitatively wrong curves"
    printfn "  - Only correlated methods (FCI, CCSD(T), VQE) get it right"
    printfn ""

// ==============================================================================
// PART 7: APPLICATIONS
// ==============================================================================

if not quiet then
    printfn "=================================================================="
    printfn "   Part 7: Applications of Mott Materials"
    printfn "=================================================================="
    printfn ""

    printfn "1. SMART WINDOWS (VO2)"
    printfn "   - Automatically block infrared above 68C"
    printfn "   - Reduce air conditioning costs by about 20%%"
    printfn "   - Commercial products entering market"
    printfn ""

    printfn "2. NEUROMORPHIC COMPUTING"
    printfn "   - Mott memristors mimic neuron behavior"
    printfn "   - Abrupt switching = action potential"
    printfn "   - Energy-efficient AI hardware"
    printfn ""

    printfn "3. HIGH-TEMPERATURE SUPERCONDUCTORS"
    printfn "   - Cuprate parent compounds are Mott insulators"
    printfn "   - Doping creates superconductivity"
    printfn "   - Understanding Mott physics key to room-temp SC"
    printfn ""

    printfn "4. OXIDE ELECTRONICS"
    printfn "   - Correlated oxide thin films"
    printfn "   - Sharp resistance switching"
    printfn "   - Non-volatile memory (ReRAM)"
    printfn ""

    printfn "5. BATTERY MATERIALS"
    printfn "   - LiCoO2 shows Mott transition during charging"
    printfn "   - Affects conductivity and capacity"
    printfn "   - Important for battery design"
    printfn ""

// ==============================================================================
// QUANTUM ADVANTAGE ANALYSIS
// ==============================================================================

if not quiet then
    printfn "=================================================================="
    printfn "   Why Quantum Computing for Mott Physics?"
    printfn "=================================================================="
    printfn ""

    printfn "CLASSICAL LIMITATIONS:"
    printfn "  - DFT severely underestimates Mott gaps"
    printfn "  - DFT+U requires empirical parameters"
    printfn "  - DMFT limited to local correlations"
    printfn "  - Exact diagonalization: only about 20 sites"
    printfn ""

    printfn "QUANTUM ADVANTAGES:"
    printfn "  - Direct treatment of strong correlations"
    printfn "  - No double-counting issues"
    printfn "  - Access to entanglement (key for Mott transition)"
    printfn "  - Accurate gaps without empirical parameters"
    printfn ""

    printfn "NISQ-ERA TARGETS:"
    printfn "  - 2-4 site Hubbard models"
    printfn "  - U/t phase diagram"
    printfn "  - Doping effects on small clusters"
    printfn ""

    printfn "FAULT-TOLERANT ERA:"
    printfn "  - 2D Hubbard model (cuprate physics)"
    printfn "  - Multi-orbital models (realistic oxides)"
    printfn "  - Dynamic correlation functions"
    printfn ""

// ==============================================================================
// SUMMARY
// ==============================================================================

if not quiet then
    printfn "=================================================================="
    printfn "   Summary"
    printfn "=================================================================="
    printfn ""

    printfn "Key Results:"
    printfn "  - Demonstrated Mott criterion: n^(1/3)*a_H approx 0.26"
    printfn "  - Calculated critical densities for various materials"
    printfn "  - Explained cooperative tunneling feedback mechanism"
    printfn "  - Performed VQE simulation of correlation effects"
    printfn ""

    printfn "Physics Insights:"
    printfn "  - Mott transition is driven by electron-electron repulsion"
    printfn "  - Band theory fails - need many-body treatment"
    printfn "  - Transition is first-order due to cooperative effects"
    printfn "  - U/W ratio controls metal vs insulator"
    printfn ""

    printfn "Applications:"
    printfn "  - Smart windows (VO2 at 68C)"
    printfn "  - Neuromorphic computing (Mott memristors)"
    printfn "  - High-Tc superconductors (cuprates)"
    printfn ""

    printfn "=================================================================="
    printfn "  The Mott transition: where quantum mechanics meets"
    printfn "  electronic traffic jams to create smart materials."
    printfn "=================================================================="
    printfn ""

// ==============================================================================
// SUGGESTED EXTENSIONS
// ==============================================================================

if not quiet then
    printfn "Suggested Extensions"
    printfn "------------------------------------------------------------------"
    printfn ""
    printfn "1. Doped Mott insulators:"
    printfn "   - Single-hole dynamics"
    printfn "   - Polaron formation"
    printfn ""
    printfn "2. 2D Hubbard model:"
    printfn "   - Cuprate physics"
    printfn "   - Pseudogap regime"
    printfn ""
    printfn "3. Multi-orbital Hubbard:"
    printfn "   - d-orbital correlations"
    printfn "   - Hund's coupling effects"
    printfn ""
    printfn "4. Dynamics:"
    printfn "   - Ultrafast switching"
    printfn "   - Photo-induced transitions"
    printfn ""

// ==============================================================================
// STRUCTURED OUTPUT
// ==============================================================================

// Build material summary rows for structured output
let materialRows =
    filteredMaterials
    |> List.map (fun mat ->
        let a_H = effectiveBohrRadius mat
        let U = hubbardU mat
        let W = bandwidth t_estimate
        let ratio = correlationStrength U W
        let phase = if ratio > 1.0 then "Insulator" else "Metal"
        Map.ofList [
            "material", mat.Name
            "short_name", mat.ShortName
            "dielectric_constant", sprintf "%.1f" mat.DielectricConstant
            "effective_mass", sprintf "%.2f" mat.EffectiveMass
            "effective_bohr_A", sprintf "%.1f" a_H
            "critical_density_per_cm3", sprintf "%.2e" mat.CriticalDensity
            "hubbard_U_eV", sprintf "%.2f" U
            "bandwidth_W_eV", sprintf "%.2f" W
            "U_over_W", sprintf "%.2f" ratio
            "predicted_phase", phase
            "hopping_t_eV", sprintf "%.2f" t_estimate
            "transition_type", mat.TransitionType
            "transition_temp_K", (match mat.TransitionTemp with Some t -> sprintf "%.0f" t | None -> "N/A")
        ])

let allResultRows = materialRows @ vqeResults

match outputPath with
| Some path ->
    Reporting.writeJson path allResultRows
    if not quiet then printfn "Results written to %s" path
| None -> ()

match csvPath with
| Some path ->
    let header =
        [ "material"; "short_name"; "dielectric_constant"; "effective_mass";
          "effective_bohr_A"; "critical_density_per_cm3"; "hubbard_U_eV";
          "bandwidth_W_eV"; "U_over_W"; "predicted_phase"; "hopping_t_eV";
          "transition_type"; "transition_temp_K";
          "molecule"; "label"; "energy_hartree"; "iterations";
          "time_seconds"; "status"; "separation_A"; "regime" ]
    let rows =
        allResultRows
        |> List.map (fun m ->
            header |> List.map (fun h -> m |> Map.tryFind h |> Option.defaultValue ""))
    Reporting.writeCsv path header rows
    if not quiet then printfn "Results written to %s" path
| None -> ()

// ==============================================================================
// USAGE HINTS
// ==============================================================================

if argv.Length = 0 && not quiet then
    printfn "Tip: Run with --help to see all options:"
    printfn "   dotnet fsi MottTransition.fsx -- --help"
    printfn "   dotnet fsi MottTransition.fsx -- --material Si"
    printfn "   dotnet fsi MottTransition.fsx -- --hopping 0.2"
    printfn "   dotnet fsi MottTransition.fsx -- --output results.json --csv results.csv"
    printfn "   dotnet fsi MottTransition.fsx -- --quiet --output results.json  (pipeline mode)"
    printfn ""
