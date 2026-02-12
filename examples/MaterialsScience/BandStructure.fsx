// ==============================================================================
// Band Structure and Semiconductor Properties Example
// ==============================================================================
// Demonstrates VQE for computing electronic band structures and band gaps
// in semiconductors, with application to solar cells and electronics.
//
// Business Context:
// Understanding band structures is crucial for:
// - Solar cell design (optimal band gap for efficiency)
// - LED/laser design (direct vs indirect band gap)
// - Transistor engineering (carrier mobility)
// - Thermoelectric materials (band engineering)
// - Topological materials (band inversion)
//
// Experimental context:
// Many band-structure conclusions are validated via large-scale facilities.
// Synchrotron radiation sources and spallation neutron sources are widely used
// in materials and condensed-matter studies, and ion implantation is a key
// semiconductor manufacturing process.
// Reference: Particle Physics Reference Library, Vol. 3 (Myers & Schopper),
// "Accelerators, Colliders and Their Application".
//
// Background reading (physics):
// - The Feynman Lectures on Physics, Vol II: semiconductors (n-type/p-type)
// - The Feynman Lectures on Physics, Vol III: internal geometry of crystals
//
// This example shows:
// - Free electron model and Fermi energy
// - Band gap formation from periodic potentials
// - Direct vs indirect band gaps
// - VQE simulation of band structures
// - Temperature dependence of band gaps
//
// Quantum Advantage:
// While simple band models have analytical solutions, accurate band structures
// with many-body effects require quantum simulation. VQE handles:
// - Electron correlation effects on band gaps
// - Excited state properties
// - Band structure topology
//
// THEORETICAL FOUNDATION:
// Based on "Concepts of Materials Science" by Adrian P. Sutton
// Chapter 6: Electronic Structure
// Section 6.5: Band Theory of Solids (pp. 73-76)
//
// Usage:
//   dotnet fsi BandStructure.fsx                                     (defaults)
//   dotnet fsi BandStructure.fsx -- --help                           (show options)
//   dotnet fsi BandStructure.fsx -- --material Si                    (filter by material)
//   dotnet fsi BandStructure.fsx -- --temperature 77                 (set temperature K)
//   dotnet fsi BandStructure.fsx -- --output results.json --csv results.csv
//   dotnet fsi BandStructure.fsx -- --quiet --output results.json    (pipeline mode)
//
// ==============================================================================

(*
===============================================================================
 Background Theory: Electronic Band Structure
===============================================================================

MATERIALS SCIENCE REFERENCE:
This example implements concepts from "Concepts of Materials Science" by
Adrian P. Sutton (Oxford University Press):
  - Chapter 6: Electronic Structure (pp. 63-80)
  - Section 6.5: Band Theory of Solids

BAND THEORY explains why materials are metals, semiconductors, or insulators
based on their electronic structure:

1. FREE ELECTRON MODEL (Sommerfeld):
   Electrons in a metal treated as free particles in a box.
   
   Energy: E = ℏ²k²/2m
   
   Fermi energy (energy of highest occupied state at T=0):
   E_F = (ℏ²/2m)(3π²n)^(2/3)
   
   where n = electron density

2. NEARLY-FREE ELECTRON MODEL:
   Periodic potential creates band gaps at Brillouin zone boundaries.
   
   Near zone boundary (k = π/a):
   E = E_free ± |V_G|
   
   where V_G is the Fourier component of the periodic potential.
   This splitting creates the BAND GAP.

3. CLASSIFICATION OF SOLIDS:
   - METAL: Partially filled band, Fermi level in band
   - SEMICONDUCTOR: Small gap (0.1-3 eV), some thermal excitation
   - INSULATOR: Large gap (>3 eV), no thermal excitation

4. BAND GAP TYPES:
   - DIRECT: Minimum of conduction band directly above maximum of valence band
   - INDIRECT: Conduction band minimum at different k-point
   
   Direct gaps: Efficient light emission (GaAs, CdSe)
   Indirect gaps: Inefficient light emission, good for transistors (Si, Ge)

5. FERMI ENERGY (Sutton Section 6.5):
   For a free electron gas:
   E_F = (ℏ²/2m)(3π²n)^(2/3)
   
   Typical values:
   - Copper: E_F ≈ 7.0 eV
   - Aluminum: E_F ≈ 11.7 eV
   - Gold: E_F ≈ 5.5 eV

6. DENSITY OF STATES:
   g(E) = (V/2π²)(2m/ℏ²)^(3/2) * √E
   
   This determines how many states are available at each energy.

QUANTUM ADVANTAGE:
Classical DFT calculations often underestimate band gaps by 30-50%.
Quantum simulation can provide:
- Accurate band gap predictions via GW-like corrections
- Excited state energies (important for optical properties)
- Band topology for topological materials

References:
  [1] Sutton, A.P. "Concepts of Materials Science" Ch.6 (Oxford, 2021)
  [2] Wikipedia: Electronic_band_structure
  [3] Ashcroft & Mermin, "Solid State Physics" (1976)
  [4] Kittel, "Introduction to Solid State Physics" (2005)
*)

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
    "BandStructure.fsx"
    "VQE simulation of semiconductor band structures and dopant chemistry."
    [ { Cli.OptionSpec.Name = "material";    Description = "Filter by material: Si, Ge, GaAs, InP, CdTe, ZnO, or all"; Default = Some "all" }
      { Cli.OptionSpec.Name = "temperature"; Description = "Temperature in Kelvin for Varshni analysis";                Default = Some "300" }
      { Cli.OptionSpec.Name = "output";      Description = "Write results to JSON file";                                Default = None }
      { Cli.OptionSpec.Name = "csv";         Description = "Write results to CSV file";                                 Default = None }
      { Cli.OptionSpec.Name = "quiet";       Description = "Suppress informational output";                             Default = None } ]
    args

let quiet = Cli.hasFlag "quiet" args
let materialFilter = args |> Cli.getOr "material" "all"
let userTemperature = args |> Cli.getFloatOr "temperature" 300.0
let outputPath = Cli.tryGet "output" args
let csvPath = Cli.tryGet "csv" args

/// Electron charge alias (shorthand used in band structure calculations)
let e = e_charge

// ==============================================================================
// SEMICONDUCTOR MATERIAL DATA
// ==============================================================================

/// Semiconductor material properties
type Semiconductor = {
    Name: string
    ShortName: string       // Key for CLI filtering
    BandGap: float          // eV at 300K
    BandGapType: string     // "Direct" or "Indirect"
    EffectiveMassE: float   // Electron effective mass (m*/m_e)
    EffectiveMassH: float   // Hole effective mass (m*/m_e)
    LatticeConstant: float  // Angstroms
    Varshni_alpha: float    // Temperature coefficient (meV/K)
    Varshni_beta: float     // Temperature parameter (K)
}

let Silicon = {
    Name = "Silicon (Si)"; ShortName = "Si"
    BandGap = 1.12
    BandGapType = "Indirect"
    EffectiveMassE = 0.26
    EffectiveMassH = 0.36
    LatticeConstant = 5.431
    Varshni_alpha = 0.473
    Varshni_beta = 636.0
}

let Germanium = {
    Name = "Germanium (Ge)"; ShortName = "Ge"
    BandGap = 0.67
    BandGapType = "Indirect"
    EffectiveMassE = 0.082
    EffectiveMassH = 0.28
    LatticeConstant = 5.658
    Varshni_alpha = 0.477
    Varshni_beta = 235.0
}

let GaAs = {
    Name = "Gallium Arsenide (GaAs)"; ShortName = "GaAs"
    BandGap = 1.42
    BandGapType = "Direct"
    EffectiveMassE = 0.067
    EffectiveMassH = 0.45
    LatticeConstant = 5.653
    Varshni_alpha = 0.541
    Varshni_beta = 204.0
}

let InP = {
    Name = "Indium Phosphide (InP)"; ShortName = "InP"
    BandGap = 1.35
    BandGapType = "Direct"
    EffectiveMassE = 0.077
    EffectiveMassH = 0.60
    LatticeConstant = 5.869
    Varshni_alpha = 0.363
    Varshni_beta = 162.0
}

let CdTe = {
    Name = "Cadmium Telluride (CdTe)"; ShortName = "CdTe"
    BandGap = 1.44
    BandGapType = "Direct"
    EffectiveMassE = 0.096
    EffectiveMassH = 0.35
    LatticeConstant = 6.482
    Varshni_alpha = 0.310
    Varshni_beta = 108.0
}

let ZnO = {
    Name = "Zinc Oxide (ZnO)"; ShortName = "ZnO"
    BandGap = 3.37
    BandGapType = "Direct"
    EffectiveMassE = 0.24
    EffectiveMassH = 0.59
    LatticeConstant = 3.25
    Varshni_alpha = 0.72
    Varshni_beta = 700.0
}

let allSemiconductors = [Silicon; Germanium; GaAs; InP; CdTe; ZnO]

/// Filter semiconductors by CLI material argument (case-insensitive)
let semiconductors =
    if materialFilter.Equals("all", StringComparison.OrdinalIgnoreCase) then
        allSemiconductors
    else
        allSemiconductors
        |> List.filter (fun s ->
            s.ShortName.Equals(materialFilter, StringComparison.OrdinalIgnoreCase))

// ==============================================================================
// FREE ELECTRON MODEL (Sutton Section 6.5)
// ==============================================================================

/// Calculate Fermi energy for free electron gas
/// E_F = (ℏ²/2m)(3π²n)^(2/3)
let fermiEnergy (electronDensity: float) : float =
    let prefactor = hbar * hbar / (2.0 * m_e)
    let kF_cubed = 3.0 * Math.PI * Math.PI * electronDensity
    let kF = Math.Pow(kF_cubed, 1.0/3.0)
    prefactor * kF * kF * J_to_eV

/// Calculate electron density from number of valence electrons and lattice constant
/// For FCC: n = 4 * Z / a³ (4 atoms per unit cell)
let electronDensity (valenceElectrons: int) (latticeConstant_A: float) : float =
    let a = latticeConstant_A * A_to_m  // Convert to meters
    let atomsPerCell = 4.0  // FCC
    (atomsPerCell * float valenceElectrons) / (a * a * a)

/// Calculate Fermi wavevector
let fermiWavevector (electronDensity: float) : float =
    Math.Pow(3.0 * Math.PI * Math.PI * electronDensity, 1.0/3.0)

/// Calculate Fermi velocity
let fermiVelocity (electronDensity: float) : float =
    let kF = fermiWavevector electronDensity
    hbar * kF / m_e

// ==============================================================================
// BAND GAP CALCULATIONS
// ==============================================================================

/// Temperature-dependent band gap using Varshni equation
/// E_g(T) = E_g(0) - αT²/(T + β)
let bandGapVsTemperature (material: Semiconductor) (T_Kelvin: float) : float =
    let E_g0 = material.BandGap + material.Varshni_alpha * 300.0 * 300.0 / (300.0 + material.Varshni_beta) / 1000.0
    E_g0 - (material.Varshni_alpha / 1000.0) * T_Kelvin * T_Kelvin / (T_Kelvin + material.Varshni_beta)

/// Calculate intrinsic carrier concentration
/// n_i = √(N_c * N_v) * exp(-E_g/2kT)
let intrinsicCarrierConcentration (material: Semiconductor) (T_Kelvin: float) : float =
    let E_g = bandGapVsTemperature material T_Kelvin
    let kT = k_B * T_Kelvin * J_to_eV  // in eV

    // Effective density of states
    let T_ratio = T_Kelvin / 300.0
    let N_c = 2.5e19 * Math.Pow(material.EffectiveMassE, 1.5) * Math.Pow(T_ratio, 1.5)
    let N_v = 2.5e19 * Math.Pow(material.EffectiveMassH, 1.5) * Math.Pow(T_ratio, 1.5)

    Math.Sqrt(N_c * N_v) * Math.Exp(-E_g / (2.0 * kT))

/// Calculate optimal band gap for solar cell efficiency (Shockley-Queisser limit)
/// Optimal gap is ~1.34 eV for single-junction solar cell
let shockleyQueisserEfficiency (bandGap_eV: float) : float =
    // Simplified approximation of S-Q efficiency
    // Maximum efficiency ~33% at 1.34 eV
    let x = (bandGap_eV - 1.34) / 0.5
    0.33 * Math.Exp(-x * x / 2.0)

// ==============================================================================
// QUANTUM BACKEND SETUP
// ==============================================================================

if not quiet then
    printfn "=================================================================="
    printfn "   Band Structure and Semiconductor Properties"
    printfn "=================================================================="
    printfn ""
    printfn "Theoretical Foundation: Sutton 'Concepts of Materials Science'"
    printfn "------------------------------------------------------------------"
    printfn "  Chapter 6: Electronic Structure"
    printfn "  Section 6.5: Band Theory of Solids"
    printfn ""
    printfn "Quantum Backend"
    printfn "------------------------------------------------------------------"
    printfn "  Backend: %s" backend.Name
    printfn "  Type: Statevector Simulator"
    printfn ""
    if materialFilter <> "all" then
        printfn "  Material filter: %s" materialFilter
    printfn "  Temperature: %.0f K" userTemperature
    printfn ""

// ==============================================================================
// PART 1: FREE ELECTRON MODEL
// ==============================================================================

type Metal = {
    Name: string
    ValenceElectrons: int
    LatticeConstant: float  // Angstroms
    Structure: string       // Crystal structure
}

let metals = [
    { Name = "Copper (Cu)"; ValenceElectrons = 1; LatticeConstant = 3.615; Structure = "FCC" }
    { Name = "Silver (Ag)"; ValenceElectrons = 1; LatticeConstant = 4.086; Structure = "FCC" }
    { Name = "Gold (Au)"; ValenceElectrons = 1; LatticeConstant = 4.078; Structure = "FCC" }
    { Name = "Aluminum (Al)"; ValenceElectrons = 3; LatticeConstant = 4.050; Structure = "FCC" }
]

if not quiet then
    printfn "=================================================================="
    printfn "   Part 1: Free Electron Model (Fermi Energy)"
    printfn "=================================================================="
    printfn ""
    printfn "The free electron model treats conduction electrons as a gas:"
    printfn "  E_F = (ℏ²/2m)(3π²n)^(2/3)"
    printfn ""
    printfn "Fermi energy of common metals:"
    printfn "------------------------------------------------------------------"
    printfn "  Metal          n (m⁻³)        k_F (m⁻¹)      v_F (m/s)      E_F (eV)"
    printfn "  -----          -------        ---------      ---------      -------"

    for metal in metals do
        let n = electronDensity metal.ValenceElectrons metal.LatticeConstant
        let E_F = fermiEnergy n
        let k_F = fermiWavevector n
        let v_F = fermiVelocity n
        printfn "  %-14s  %.2e   %.2e   %.2e   %.2f"
                metal.Name n k_F v_F E_F

    printfn ""
    printfn "Note: Fermi energies are several eV, meaning electrons move FAST"
    printfn "even at absolute zero temperature (v_F ~ 10^6 m/s)"
    printfn ""

// ==============================================================================
// PART 2: BAND GAPS AND MATERIAL CLASSIFICATION
// ==============================================================================

if not quiet then
    printfn "=================================================================="
    printfn "   Part 2: Band Gaps and Material Classification"
    printfn "=================================================================="
    printfn ""
    printfn "Materials classified by band gap:"
    printfn ""
    printfn "  METALS:        No band gap, E_F in conduction band"
    printfn "  SEMICONDUCTORS: Small gap (0.1-3 eV)"
    printfn "  INSULATORS:    Large gap (>3 eV)"
    printfn ""
    printfn "Common semiconductor properties:"
    printfn "------------------------------------------------------------------"
    printfn "  Material             Gap (eV)   Type       m_e*     m_h*"
    printfn "  --------             --------   ----       ----     ----"

    for semi in semiconductors do
        printfn "  %-20s  %.2f       %-8s   %.3f    %.3f"
                semi.Name semi.BandGap semi.BandGapType semi.EffectiveMassE semi.EffectiveMassH

    printfn ""
    printfn "Direct vs Indirect band gaps:"
    printfn "  DIRECT:   Efficient light emission (LEDs, lasers)"
    printfn "  INDIRECT: Poor emitters but good for transistors"
    printfn ""

// ==============================================================================
// PART 3: TEMPERATURE DEPENDENCE
// ==============================================================================

let temperatures = [4.0; 77.0; 200.0; 300.0; 400.0; 500.0]  // Kelvin
// Include user-specified temperature if not already in the list
let temperatures =
    if temperatures |> List.exists (fun t -> abs(t - userTemperature) < 0.5) then
        temperatures
    else
        temperatures @ [userTemperature] |> List.sort

if not quiet then
    printfn "=================================================================="
    printfn "   Part 3: Temperature Dependence of Band Gap"
    printfn "=================================================================="
    printfn ""
    printfn "Varshni equation: E_g(T) = E_g(0) - αT²/(T + β)"
    printfn ""

    for semi in semiconductors do
        printfn "%s band gap vs temperature:" semi.Name
        printfn "------------------------------------------------------------------"
        printfn "  T (K)    E_g (eV)    n_i (cm⁻³)"
        printfn "  -----    --------    ----------"

        for T in temperatures do
            let E_g = bandGapVsTemperature semi T
            let n_i = intrinsicCarrierConcentration semi T
            if n_i > 1.0 then
                printfn "    %.0f      %.4f     %.2e" T E_g n_i
            else
                printfn "    %.0f      %.4f     ~0" T E_g

        printfn ""

    printfn "Key insight: Band gap DECREASES with temperature"
    printfn "(thermal expansion and electron-phonon coupling)"
    printfn ""

// ==============================================================================
// PART 4: SOLAR CELL APPLICATIONS
// ==============================================================================

if not quiet then
    printfn "=================================================================="
    printfn "   Part 4: Solar Cell Band Gap Optimization"
    printfn "=================================================================="
    printfn ""
    printfn "Shockley-Queisser limit: Maximum single-junction efficiency ~33%%"
    printfn "Optimal band gap: ~1.34 eV (matches solar spectrum)"
    printfn ""
    printfn "Semiconductor suitability for solar cells:"
    printfn "------------------------------------------------------------------"
    printfn "  Material             E_g (eV)   Theoretical η   Match"
    printfn "  --------             --------   -------------   -----"

    for semi in semiconductors do
        let efficiency = shockleyQueisserEfficiency semi.BandGap
        let matchQuality =
            if abs(semi.BandGap - 1.34) < 0.2 then "Excellent"
            elif abs(semi.BandGap - 1.34) < 0.4 then "Good"
            elif abs(semi.BandGap - 1.34) < 0.7 then "Fair"
            else "Poor"
        printfn "  %-20s  %.2f       %.1f%%           %s"
                semi.Name semi.BandGap (efficiency * 100.0) matchQuality

    printfn ""
    printfn "GaAs and CdTe are near-optimal for single-junction solar cells!"
    printfn ""

// ==============================================================================
// PART 5: VQE SIMULATION - SEMICONDUCTOR DOPANT MOLECULES
// ==============================================================================

if not quiet then
    printfn "=================================================================="
    printfn "   Part 5: VQE Simulation of Semiconductor Dopant Chemistry"
    printfn "=================================================================="
    printfn ""
    printfn "SCIENTIFIC APPROACH:"
    printfn "  Instead of trying to simulate bulk band structure (impossible for"
    printfn "  NISQ-era quantum computers), we compute the electronic structure"
    printfn "  of dopant molecules that are central to semiconductor technology:"
    printfn ""
    printfn "  - SiH4 (silane): Silicon CVD precursor, model for Si-H bonding"
    printfn "  - PH3 (phosphine): n-type dopant precursor (phosphorus in Si)"
    printfn "  - These molecules determine semiconductor doping behavior!"
    printfn ""
    printfn "Why this matters for semiconductor physics:"
    printfn "------------------------------------------------------------------"
    printfn "  1. Dopant atoms (P, B, As) create discrete energy levels in the"
    printfn "     band gap, enabling n-type and p-type conductivity"
    printfn ""
    printfn "  2. VQE can compute dopant ionization energies accurately:"
    printfn "     - PH3 -> P + H3 + e- (donor ionization)"
    printfn "     - Determines n-type carrier concentration"
    printfn ""
    printfn "  3. Surface chemistry (Si-H bonds in SiH4) controls:"
    printfn "     - Interface trap density"
    printfn "     - Surface recombination"
    printfn "     - Device reliability"
    printfn ""

// ==============================================================================
// SEMICONDUCTOR DOPANT MOLECULE DEFINITIONS
// ==============================================================================

/// Create SiH4 molecule (silane) at equilibrium geometry
/// Si-H bond length: 1.480 A (experimental)
///
/// SiH4 is important for:
/// - Semiconductor doping precursor (CVD)
/// - Silicon surface chemistry
/// - Model for Si-H bonding in passivated surfaces
let createSiH4 () : Molecule =
    // Tetrahedral geometry: Si at center, H at corners
    let bondLength = 1.480
    // Tetrahedral angle: cos^-1(-1/3) ~ 109.47 deg
    // Coordinates for regular tetrahedron with Si at origin
    let a = bondLength / sqrt 3.0
    {
        Name = "SiH4"
        Atoms = [
            { Element = "Si"; Position = (0.0, 0.0, 0.0) }
            { Element = "H"; Position = (a, a, a) }
            { Element = "H"; Position = (-a, -a, a) }
            { Element = "H"; Position = (-a, a, -a) }
            { Element = "H"; Position = (a, -a, -a) }
        ]
        Bonds = [
            { Atom1 = 0; Atom2 = 1; BondOrder = 1.0 }
            { Atom1 = 0; Atom2 = 2; BondOrder = 1.0 }
            { Atom1 = 0; Atom2 = 3; BondOrder = 1.0 }
            { Atom1 = 0; Atom2 = 4; BondOrder = 1.0 }
        ]
        Charge = 0
        Multiplicity = 1  // Singlet (all electrons paired)
    }

/// Create PH3 molecule (phosphine) at equilibrium geometry
/// P-H bond length: 1.42 A, H-P-H angle: 93.5 deg
///
/// PH3 is important for:
/// - Phosphorus doping in semiconductors (n-type Si)
/// - MOCVD precursor for III-V semiconductors
/// - Model for donor impurity states in Si
let createPH3 () : Molecule =
    let bondLength = 1.42
    let angleRad = 93.5 * Math.PI / 180.0
    // Pyramidal geometry with P at origin
    let h = bondLength * cos(angleRad / 2.0)  // Height above base
    let r = bondLength * sin(angleRad / 2.0)  // Radius of base
    // Three H atoms arranged 120 deg apart
    {
        Name = "PH3"
        Atoms = [
            { Element = "P"; Position = (0.0, 0.0, 0.0) }
            { Element = "H"; Position = (r, 0.0, h) }
            { Element = "H"; Position = (-r * 0.5, r * 0.866, h) }
            { Element = "H"; Position = (-r * 0.5, -r * 0.866, h) }
        ]
        Bonds = [
            { Atom1 = 0; Atom2 = 1; BondOrder = 1.0 }
            { Atom1 = 0; Atom2 = 2; BondOrder = 1.0 }
            { Atom1 = 0; Atom2 = 3; BondOrder = 1.0 }
        ]
        Charge = 0
        Multiplicity = 1  // Singlet
    }

// ==============================================================================
// VQE CALCULATIONS
// ==============================================================================

if not quiet then
    printfn "VQE Results for Semiconductor-Related Molecules:"
    printfn "------------------------------------------------------------------"
    printfn ""

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
            printfn "   Energy in eV: %.3f eV" (energy * hartreeToEV)
            printfn "   Iterations: %d, Time: %.2f s" iterations time
            printfn ""
        Map.ofList [
            "molecule", molecule.Name
            "label", label
            "energy_hartree", sprintf "%.6f" energy
            "energy_eV", sprintf "%.3f" (energy * hartreeToEV)
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
            "energy_eV", "N/A"
            "iterations", "N/A"
            "time_seconds", "N/A"
            "status", sprintf "Error: %s" msg
        ]

let siH4 = createSiH4()
let pH3 = createPH3()
let h2_SiH = Molecule.createH2 1.48

let vqeResults = [
    runVqe
        "1. SiH4 (Silane) - Silicon CVD precursor"
        "Atoms: Si + 4H (tetrahedral); Si-H bond strength determines CVD kinetics"
        siH4
    runVqe
        "2. PH3 (Phosphine) - n-type dopant precursor"
        "Atoms: P + 3H (pyramidal, 93.5 deg bond angle); P is the most common n-type dopant in Si"
        pH3
    runVqe
        "3. H2 at Si-H bond length (1.48 A) - Surface passivation model"
        "Models H-H interaction during Si surface hydrogenation"
        h2_SiH
]

if not quiet then
    printfn "SCIENTIFIC INTERPRETATION:"
    printfn "------------------------------------------------------------------"
    printfn "  These molecular energies provide insight into semiconductor"
    printfn "  processing and doping chemistry:"
    printfn ""
    printfn "  - SiH4 energy: Related to Si-H bond dissociation in CVD"
    printfn "  - PH3 energy: Determines P donor ionization in Si lattice"
    printfn "  - H2 energy: Surface hydrogenation thermodynamics"
    printfn ""
    printfn "  Note: Full band structure calculation requires periodic boundary"
    printfn "  conditions (Bloch's theorem) which is beyond current NISQ capacity."
    printfn "  These molecular calculations are what VQE can do accurately!"
    printfn ""

// ==============================================================================
// PART 6: DENSITY OF STATES
// ==============================================================================

/// Free electron density of states (per unit volume)
let densityOfStates (E_eV: float) : float =
    if E_eV <= 0.0 then 0.0
    else
        let E_J = E_eV * eV_to_J
        let prefactor = 1.0 / (2.0 * Math.PI * Math.PI) * Math.Pow(2.0 * m_e / (hbar * hbar), 1.5)
        prefactor * Math.Sqrt(E_J)

if not quiet then
    printfn "=================================================================="
    printfn "   Part 6: Density of States"
    printfn "=================================================================="
    printfn ""
    printfn "Free electron density of states: g(E) ~ sqrt(E)"
    printfn ""
    printfn "ASCII plot of density of states:"
    printfn ""
    printfn "  g(E)"
    printfn "   |                                    *****"
    printfn "   |                              ******"
    printfn "   |                        ******"
    printfn "   |                  ******"
    printfn "   |            ******"
    printfn "   |       *****"
    printfn "   |   ****"
    printfn "   | **"
    printfn "   |*"
    printfn "   +----------------------------------------- E"
    printfn "   0           E_F"
    printfn ""
    printfn "For semiconductors, there's a gap in the DOS at the Fermi level"
    printfn ""

// ==============================================================================
// APPLICATIONS AND BUSINESS CONTEXT
// ==============================================================================

if not quiet then
    printfn "=================================================================="
    printfn "   Applications of Band Structure Engineering"
    printfn "=================================================================="
    printfn ""
    printfn "1. SOLAR CELLS"
    printfn "   - Band gap determines absorption range"
    printfn "   - Multi-junction cells use different gaps"
    printfn "   - CdTe, GaAs, Si dominate market"
    printfn ""
    printfn "2. LEDS AND LASERS"
    printfn "   - Direct gap materials for efficient emission"
    printfn "   - GaN (blue), InGaN (green), AlGaInP (red)"
    printfn "   - Nobel Prize 2014: Blue LEDs"
    printfn ""
    printfn "3. TRANSISTORS (CMOS)"
    printfn "   - Silicon: workhorse of electronics"
    printfn "   - GaAs: high-frequency applications"
    printfn "   - Wide-gap: SiC, GaN for power electronics"
    printfn ""
    printfn "4. THERMOELECTRICS"
    printfn "   - Band engineering for Seebeck coefficient"
    printfn "   - PbTe, Bi2Te3 for waste heat recovery"
    printfn ""
    printfn "5. TOPOLOGICAL MATERIALS"
    printfn "   - Band inversion creates topological phases"
    printfn "   - Protected surface states"
    printfn "   - Potential for quantum computing"
    printfn ""

// ==============================================================================
// QUANTUM ADVANTAGE ANALYSIS
// ==============================================================================

if not quiet then
    printfn "=================================================================="
    printfn "   Why Quantum Computing for Band Structures?"
    printfn "=================================================================="
    printfn ""
    printfn "CLASSICAL LIMITATIONS:"
    printfn "  - DFT underestimates band gaps by 30-50%%"
    printfn "  - GW calculations very expensive"
    printfn "  - Strongly correlated materials intractable"
    printfn "  - Excited states challenging"
    printfn ""
    printfn "QUANTUM ADVANTAGES:"
    printfn "  - Direct treatment of electron correlation"
    printfn "  - Accurate excited state energies"
    printfn "  - Band topology from many-body wavefunctions"
    printfn "  - Defect states and impurity bands"
    printfn ""
    printfn "MATERIALS WHERE QUANTUM HELPS:"
    printfn "  - Transition metal oxides (Mott insulators)"
    printfn "  - Rare earth compounds"
    printfn "  - Topological insulators"
    printfn "  - Strongly correlated semiconductors"
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
    printfn "  - Calculated Fermi energies for common metals"
    printfn "  - Analyzed band gaps of major semiconductors"
    printfn "  - Showed temperature dependence via Varshni equation"
    printfn "  - Evaluated materials for solar cell applications"
    printfn "  - Performed VQE simulation of dopant molecules"
    printfn ""
    printfn "Physics Insights:"
    printfn "  - Band gaps arise from periodic potential"
    printfn "  - Direct vs indirect affects optical properties"
    printfn "  - Temperature reduces band gap"
    printfn "  - Optimal solar cell gap ~1.34 eV"
    printfn ""
    printfn "=================================================================="
    printfn "  Band structure engineering enables modern electronics,"
    printfn "  from smartphones to solar panels to quantum computers."
    printfn "=================================================================="
    printfn ""

// ==============================================================================
// SUGGESTED EXTENSIONS
// ==============================================================================

if not quiet then
    printfn "Suggested Extensions"
    printfn "------------------------------------------------------------------"
    printfn ""
    printfn "1. Alloy band gaps:"
    printfn "   - Si_xGe_{1-x} for strain engineering"
    printfn "   - In_xGa_{1-x}As for tunable detectors"
    printfn ""
    printfn "2. Quantum wells:"
    printfn "   - 2D confinement effects"
    printfn "   - Superlattice minibands"
    printfn ""
    printfn "3. Topological band theory:"
    printfn "   - Berry phase calculations"
    printfn "   - Z2 invariants"
    printfn ""
    printfn "4. Defect states:"
    printfn "   - Donor/acceptor levels"
    printfn "   - Deep level centers"
    printfn ""

// ==============================================================================
// STRUCTURED OUTPUT
// ==============================================================================

// Build semiconductor summary rows for structured output
let semiRows =
    semiconductors
    |> List.map (fun semi ->
        let E_g_T = bandGapVsTemperature semi userTemperature
        let n_i = intrinsicCarrierConcentration semi userTemperature
        let sqEff = shockleyQueisserEfficiency semi.BandGap
        Map.ofList [
            "material", semi.ShortName
            "name", semi.Name
            "band_gap_eV", sprintf "%.4f" semi.BandGap
            "band_gap_type", semi.BandGapType
            "effective_mass_e", sprintf "%.3f" semi.EffectiveMassE
            "effective_mass_h", sprintf "%.3f" semi.EffectiveMassH
            "lattice_constant_A", sprintf "%.3f" semi.LatticeConstant
            "band_gap_at_T_eV", sprintf "%.4f" E_g_T
            "temperature_K", sprintf "%.0f" userTemperature
            "carrier_concentration_cm3", sprintf "%.2e" n_i
            "sq_efficiency_pct", sprintf "%.1f" (sqEff * 100.0)
        ])

let allResultRows = semiRows @ vqeResults

match outputPath with
| Some path ->
    Reporting.writeJson path allResultRows
    if not quiet then printfn "Results written to %s" path
| None -> ()

match csvPath with
| Some path ->
    let header =
        [ "material"; "name"; "band_gap_eV"; "band_gap_type"; "effective_mass_e";
          "effective_mass_h"; "lattice_constant_A"; "band_gap_at_T_eV";
          "temperature_K"; "carrier_concentration_cm3"; "sq_efficiency_pct";
          "molecule"; "label"; "energy_hartree"; "energy_eV"; "iterations";
          "time_seconds"; "status" ]
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
    printfn "   dotnet fsi BandStructure.fsx -- --help"
    printfn "   dotnet fsi BandStructure.fsx -- --material GaAs"
    printfn "   dotnet fsi BandStructure.fsx -- --temperature 77 --output results.json"
    printfn "   dotnet fsi BandStructure.fsx -- --quiet --output results.json  (pipeline mode)"
    printfn ""
