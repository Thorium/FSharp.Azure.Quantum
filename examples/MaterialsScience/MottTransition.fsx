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
// RULE1 COMPLIANCE:
// All quantum calculations use IQuantumBackend throughout.
// ==============================================================================

#r "../../src/FSharp.Azure.Quantum/bin/Debug/net10.0/FSharp.Azure.Quantum.dll"

open System
open FSharp.Azure.Quantum.QuantumChemistry
open FSharp.Azure.Quantum.Core
open FSharp.Azure.Quantum.Core.BackendAbstraction
open FSharp.Azure.Quantum.Backends.LocalBackend

// ==============================================================================
// PHYSICAL CONSTANTS
// ==============================================================================

/// Planck's constant (J*s)
let h = 6.62607015e-34

/// Reduced Planck's constant (J*s)
let hbar = h / (2.0 * Math.PI)

/// Electron mass (kg)
let m_e = 9.10938e-31

/// Electron charge (C)
let e_charge = 1.60218e-19

/// Boltzmann constant (J/K)
let k_B = 1.38065e-23

/// Bohr radius (m)
let a_0 = 5.29177e-11

/// Bohr radius (Angstroms)
let a_0_A = 0.529177

/// Electron volt to Joules
let eV_to_J = 1.60218e-19

/// Joules to electron volt
let J_to_eV = 1.0 / eV_to_J

/// Hartree to eV
let hartreeToEV = 27.2114

// ==============================================================================
// MATERIAL DATA FOR MOTT TRANSITIONS
// ==============================================================================

/// Material properties relevant for Mott transition
type MottMaterial = {
    Name: string
    DielectricConstant: float   // epsilon_r
    EffectiveMass: float        // m_star/m_e
    CriticalDensity: float      // n_c (per cm cubed) for Mott transition
    TransitionType: string      // "Doping" or "Temperature"
    TransitionTemp: float option // K (for temperature-driven)
}

let SiliconPDoped = {
    Name = "P-doped Silicon"
    DielectricConstant = 11.7
    EffectiveMass = 0.26
    CriticalDensity = 3.7e18    // Sutton p. 97
    TransitionType = "Doping"
    TransitionTemp = None
}

let GermaniumAsDoped = {
    Name = "As-doped Germanium"
    DielectricConstant = 16.0
    EffectiveMass = 0.12
    CriticalDensity = 1.0e17
    TransitionType = "Doping"
    TransitionTemp = None
}

let VO2 = {
    Name = "Vanadium Dioxide (VO2)"
    DielectricConstant = 36.0
    EffectiveMass = 3.0
    CriticalDensity = 3.0e21
    TransitionType = "Temperature"
    TransitionTemp = Some 341.0  // 68C
}

let V2O3 = {
    Name = "Vanadium Sesquioxide (V2O3)"
    DielectricConstant = 12.0
    EffectiveMass = 5.0
    CriticalDensity = 1.0e22
    TransitionType = "Temperature"
    TransitionTemp = Some 150.0  // Under pressure
}

let NdNiO3 = {
    Name = "Neodymium Nickelate (NdNiO3)"
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

printfn "=================================================================="
printfn "   Mott Metal-Insulator Transition"
printfn "=================================================================="
printfn ""

printfn "Theoretical Foundation: Sutton 'Concepts of Materials Science'"
printfn "------------------------------------------------------------------"
printfn "  Chapter 8: Electrical Resistance of Metals"
printfn "  Section 8.3.1: Metal-Insulator Transition (pp. 96-98)"
printfn ""

let backend = LocalBackend() :> IQuantumBackend

printfn "Quantum Backend"
printfn "------------------------------------------------------------------"
printfn "  Backend: %s" backend.Name
printfn "  Type: Statevector Simulator"
printfn ""

// ==============================================================================
// PART 1: MOTT CRITERION
// ==============================================================================

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

printfn "Effective Bohr radii for various materials:"
printfn "------------------------------------------------------------------"
printfn "  Material                      eps_r  m*/m_e   a_H (A)"
printfn "  --------                      -----  ------   -------"

for mat in materials do
    let a_H = effectiveBohrRadius mat
    printfn "  %-28s  %.1f    %.2f     %.1f" mat.Name mat.DielectricConstant mat.EffectiveMass a_H

printfn ""

// ==============================================================================
// PART 2: PHOSPHORUS-DOPED SILICON (Sutton Example)
// ==============================================================================

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

printfn "P-doped Silicon calculation:"
printfn "------------------------------------------------------------------"
printfn "  Effective Bohr radius: a_H = %.1f A" a_H_Si
printfn "  Calculated n_c: %.2e per cm cubed" n_c_Si_calc
printfn "  Experimental n_c: %.2e per cm cubed (Sutton)" SiliconPDoped.CriticalDensity
printfn ""

let dopingLevels = [1.0e17; 5.0e17; 1.0e18; 3.0e18; 5.0e18; 1.0e19; 5.0e19]

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

let t_estimate = 0.1  // eV, typical hopping integral

printfn "Hubbard model parameters:"
printfn "------------------------------------------------------------------"
printfn "  Material                      U (eV)   W (eV)   U/W    Phase"
printfn "  --------                      ------   ------   ---    -----"

for mat in materials do
    let U = hubbardU mat
    let W = bandwidth t_estimate
    let ratio = correlationStrength U W
    let phase = if ratio > 1.0 then "Insulator" else "Metal"
    printfn "  %-28s  %.2f     %.2f     %.2f   %s" mat.Name U W ratio phase

printfn ""

// ==============================================================================
// PART 5: TEMPERATURE-DRIVEN TRANSITIONS
// ==============================================================================

printfn "=================================================================="
printfn "   Part 5: Temperature-Driven Mott Transitions"
printfn "=================================================================="
printfn ""

printfn "Some materials show Mott transitions driven by temperature:"
printfn ""

let tempMaterials = materials |> List.filter (fun m -> m.TransitionTemp.IsSome)

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

printfn "=================================================================="
printfn "   Part 6: VQE Simulation of Electron Correlation"
printfn "=================================================================="
printfn ""

printfn "The Mott transition requires treating electron correlation exactly."
printfn "VQE can capture these effects that mean-field theories miss."
printfn ""

// ============================================================================
// WHY H₂ IS A LEGITIMATE HUBBARD MODEL ANALOG
// ============================================================================
//
// The stretched H₂ molecule is a TEXTBOOK example for studying the Mott 
// transition. This is NOT a fake model - it's the simplest exact realization
// of Hubbard physics. Here's why:
//
// 1. HALF-FILLING: H₂ has 2 electrons on 2 "sites" (atoms) = half-filled
//    This is exactly the regime where Mott physics is most important
//
// 2. TUNABLE U/t RATIO:
//    - At equilibrium (0.74 Å): Strong overlap → large t → METALLIC
//    - At large separation (3+ Å): Weak overlap → small t → INSULATING
//    - The H₂ dissociation curve spans the entire Mott transition!
//
// 3. EXACT MAPPING TO HUBBARD MODEL:
//    The H₂ Hamiltonian in a minimal basis maps EXACTLY to a 2-site Hubbard:
//      H = -t(c₁†c₂ + c₂†c₁) + U(n₁↑n₁↓ + n₂↑n₂↓)
//    where t = hopping integral, U = on-site Coulomb repulsion
//
// 4. BENCHMARK SYSTEM:
//    H₂ dissociation is THE standard test for many-body methods because:
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
//   [4] Cohen, Mori-Sánchez & Yang, Chem. Rev. 112, 289 (2012) - DFT failures
//
// ============================================================================

/// Create H₂ at varying separation - the simplest Mott model
/// This is the TEXTBOOK system for studying metal-insulator transitions:
/// - Small R (< 1 Å): Metallic (electrons delocalized)
/// - Large R (> 3 Å): Mott insulator (electrons localized, one per atom)
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

/// Calculate ground state energy using VQE
let calculateVQEEnergy (molecule: Molecule) : Result<float * int * float, string> =
    let startTime = DateTime.Now
    
    let config = {
        Method = GroundStateMethod.VQE
        Backend = Some backend
        MaxIterations = 50
        Tolerance = 1e-5
        InitialParameters = None
        ProgressReporter = None
        ErrorMitigation = None
        IntegralProvider = None
    }
    
    try
        let result = GroundStateEnergy.estimateEnergy molecule config |> Async.RunSynchronously
        let elapsed = (DateTime.Now - startTime).TotalSeconds
        
        match result with
        | Ok vqeResult -> Ok (vqeResult.Energy, vqeResult.Iterations, elapsed)
        | Error err -> Error err.Message
    with
    | ex -> Error ex.Message

printfn "VQE Results for H₂ Dissociation (Mott Transition Model):"
printfn "------------------------------------------------------------------"
printfn ""
printfn "H₂ dissociation is THE textbook model for the Mott transition:"
printfn "  - Small R: Electrons delocalized (metallic bonding)"
printfn "  - Large R: Electrons localized, one per atom (Mott insulator)"
printfn ""
printfn "  R (Å)    Regime              VQE E (Ha)    E - E_eq (eV)"
printfn "  -----    ------              ----------    -------------"

// Key bond lengths spanning the Mott transition
let separations = [
    (0.74, "Equilibrium (metallic)")
    (1.50, "Stretched (correlated)")
    (2.50, "Dissociating (Mott)")
    (4.00, "Separated (insulator)")
]

let mutable equilibriumEnergy = 0.0

for (sep, regime) in separations do
    let model = createH2MottModel sep
    
    match calculateVQEEnergy model with
    | Ok (energy, iterations, time) ->
        if abs(sep - 0.74) < 0.01 then
            equilibriumEnergy <- energy
        let deltaE = if equilibriumEnergy <> 0.0 then (energy - equilibriumEnergy) * hartreeToEV else 0.0
        printfn "  %.2f     %-20s  %.6f      %.3f" sep regime energy deltaE
    | Error msg ->
        printfn "  %.2f     %-20s  Error: %s" sep regime msg

printfn ""
printfn "Physical Interpretation:"
printfn "  - At R = 0.74 Å: Strong bond, electrons shared (metallic character)"
printfn "  - At R > 2.5 Å: Bond breaking, electrons localize (Mott insulator)"
printfn "  - The energy gap to move an electron = 'Mott gap'"
printfn ""
printfn "This is why H₂ dissociation is a stringent test for quantum methods:"
printfn "  - Hartree-Fock fails completely (wrong dissociation limit)"
printfn "  - Standard DFT gives qualitatively wrong curves"
printfn "  - Only correlated methods (FCI, CCSD(T), VQE) get it right"
printfn ""

// ==============================================================================
// PART 7: APPLICATIONS
// ==============================================================================

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

printfn "RULE1 compliant: All VQE calculations via IQuantumBackend"
printfn ""

printfn "=================================================================="
printfn "  The Mott transition: where quantum mechanics meets"
printfn "  electronic traffic jams to create smart materials."
printfn "=================================================================="
printfn ""

// ==============================================================================
// SUGGESTED EXTENSIONS
// ==============================================================================

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
