// ==============================================================================
// Quantum Tunneling and Hydrogen Embrittlement Example
// ==============================================================================
// Demonstrates VQE for studying quantum tunneling of hydrogen in metals,
// with application to hydrogen economy infrastructure and steel embrittlement.
//
// Business Context:
// Understanding hydrogen tunneling is crucial for:
// - Hydrogen economy (storage tanks, pipelines, fuel cells)
// - Steel industry (preventing embrittlement failures)
// - Nuclear reactors (hydrogen-induced cracking)
// - Aerospace (pressure vessel integrity)
// - Chemical industry (hydrogenation processes)
//
// Experimental context:
// Hydrogen-in-metals studies are often validated with scattering/spectroscopy
// techniques performed at large-scale facilities (synchrotron radiation and
// spallation neutron sources).
//
// This example shows:
// - Quantum tunneling probability calculations
// - WKB approximation for barrier penetration
// - Hydrogen diffusion in metals (Fe, Ni, Pd)
// - Temperature dependence of tunneling vs classical rates
// - VQE simulation of H in metal lattices
//
// Quantum Advantage:
// Hydrogen is light enough that quantum effects dominate at low temperatures.
// Classical molecular dynamics completely misses:
// - Tunneling through diffusion barriers
// - Zero-point energy effects
// - Isotope effects (H vs D vs T)
//
// THEORETICAL FOUNDATION:
// Based on "Concepts of Materials Science" by Adrian P. Sutton
// Chapter 6: Electronic Structure
// Section 6.8: Quantum Diffusion (pp. 79-80)
//
// Background reading (physics):
// - The Feynman Lectures on Physics, Vol III: diffusion equation; crystals
//
// RULE1 COMPLIANCE:
// All quantum calculations use IQuantumBackend throughout.
// ==============================================================================

// BACKGROUND THEORY:
// -------------------------------------------------------------------------------
// QUANTUM TUNNELING allows particles to pass through energy barriers that
// would be classically forbidden. For hydrogen in metals, this is critical
// because:
//
// 1. Hydrogen atoms are light (mass = 1 amu)
// 2. de Broglie wavelength is large (about 1 Angstrom at 300K)
// 3. Barrier widths are comparable to wavelength
//
// TUNNELING PROBABILITY (Sutton Eq. 6.5):
// For a rectangular barrier:
//   P = exp(-2 * kappa * w)
// where:
//   kappa = sqrt(2 * m * V0) / hbar
//   w = barrier width
//   V0 = barrier height
//
// At low temperatures, tunneling dominates over thermally-activated hopping.
// The crossover temperature is roughly:
//   T_crossover = hbar * omega / (2 * pi * k_B)
// where omega is the attempt frequency.
//
// HYDROGEN EMBRITTLEMENT:
// Hydrogen diffuses into metals and causes:
// - Grain boundary weakening
// - Stress-corrosion cracking
// - Delayed fracture
// This is a major concern for the hydrogen economy.
//
// References:
//   [1] Sutton, A.P. "Concepts of Materials Science" Ch.6 (Oxford, 2021)
//   [2] Wikipedia: Hydrogen_embrittlement
//   [3] Flynn, C.P. and Stoneham, A.M. Phys. Rev. B 1, 3966 (1970)
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

/// Proton mass (kg)
let m_H = 1.6735575e-27

/// Deuterium mass (kg)
let m_D = 2.0 * m_H

/// Tritium mass (kg)
let m_T = 3.0 * m_H

/// Boltzmann constant (J/K)
let k_B = 1.38065e-23

/// Electron volt to Joules
let eV_to_J = 1.60218e-19

/// Joules to electron volt
let J_to_eV = 1.0 / eV_to_J

/// Angstrom to meters
let A_to_m = 1.0e-10

/// Hartree to eV
let hartreeToEV = 27.2114

// ==============================================================================
// METAL HOST DATA
// ==============================================================================

/// Metal properties for hydrogen diffusion
type MetalHost = {
    Name: string
    BarrierHeight: float    // eV (activation energy for diffusion)
    BarrierWidth: float     // Angstroms (typical jump distance)
    AttemptFrequency: float // Hz (vibrational frequency)
    LatticeConstant: float  // Angstroms
    HydrogenSolubility: float // atomic fraction at 1 atm, 300K
}

let Iron = {
    Name = "Iron (Fe) - BCC"
    BarrierHeight = 0.04    // eV - relatively low
    BarrierWidth = 1.2      // Angstroms
    AttemptFrequency = 1.0e13   // Hz
    LatticeConstant = 2.87
    HydrogenSolubility = 1.0e-8  // Very low
}

let Nickel = {
    Name = "Nickel (Ni) - FCC"
    BarrierHeight = 0.41    // eV
    BarrierWidth = 1.5      // Angstroms
    AttemptFrequency = 1.0e13
    LatticeConstant = 3.52
    HydrogenSolubility = 1.0e-5
}

let Palladium = {
    Name = "Palladium (Pd) - FCC"
    BarrierHeight = 0.23    // eV
    BarrierWidth = 1.4      // Angstroms
    AttemptFrequency = 1.0e13
    LatticeConstant = 3.89
    HydrogenSolubility = 0.6  // Very high - H storage material
}

let Titanium = {
    Name = "Titanium (Ti) - HCP"
    BarrierHeight = 0.54    // eV
    BarrierWidth = 1.6
    AttemptFrequency = 1.0e13
    LatticeConstant = 2.95
    HydrogenSolubility = 0.08
}

let Steel = {
    Name = "Steel (Fe-C)"
    BarrierHeight = 0.05    // eV - slightly higher than pure Fe
    BarrierWidth = 1.3
    AttemptFrequency = 1.0e13
    LatticeConstant = 2.87
    HydrogenSolubility = 2.0e-8
}

// ==============================================================================
// TUNNELING CALCULATIONS
// ==============================================================================

/// Calculate de Broglie wavelength
/// lambda = h / sqrt(2 * m * E)
let deBroglieWavelength (mass: float) (energy_eV: float) : float =
    let E_J = energy_eV * eV_to_J
    h / Math.Sqrt(2.0 * mass * E_J) / A_to_m  // Returns in Angstroms

/// Calculate WKB tunneling probability for rectangular barrier
/// P = exp(-2 * kappa * w) where kappa = sqrt(2mV0)/hbar
let tunnelingProbability (mass: float) (barrierHeight_eV: float) (barrierWidth_A: float) : float =
    let V0_J = barrierHeight_eV * eV_to_J
    let w_m = barrierWidth_A * A_to_m
    let kappa = Math.Sqrt(2.0 * mass * V0_J) / hbar
    Math.Exp(-2.0 * kappa * w_m)

/// Calculate classical Arrhenius hopping rate
/// Rate = nu * exp(-E_a / k_B T)
let classicalHoppingRate (metal: MetalHost) (T_Kelvin: float) : float =
    let E_a_J = metal.BarrierHeight * eV_to_J
    metal.AttemptFrequency * Math.Exp(-E_a_J / (k_B * T_Kelvin))

/// Calculate quantum tunneling rate (temperature-independent)
/// Rate = nu * P_tunnel
let quantumTunnelingRate (metal: MetalHost) (mass: float) : float =
    let P_tunnel = tunnelingProbability mass metal.BarrierHeight metal.BarrierWidth
    metal.AttemptFrequency * P_tunnel

/// Calculate crossover temperature between classical and quantum regimes
/// T_crossover = hbar * omega / (2 * pi * k_B)
let crossoverTemperature (attemptFrequency: float) : float =
    hbar * attemptFrequency / (2.0 * Math.PI * k_B)

/// Calculate effective diffusion coefficient
/// D = a^2 * rate / 6 (for 3D random walk)
let diffusionCoefficient (metal: MetalHost) (rate: float) : float =
    let a = metal.LatticeConstant * A_to_m
    a * a * rate / 6.0

// ==============================================================================
// QUANTUM BACKEND SETUP
// ==============================================================================

printfn "=================================================================="
printfn "   Quantum Tunneling and Hydrogen Embrittlement"
printfn "=================================================================="
printfn ""

printfn "Theoretical Foundation: Sutton 'Concepts of Materials Science'"
printfn "------------------------------------------------------------------"
printfn "  Chapter 6: Electronic Structure"
printfn "  Section 6.8: Quantum Diffusion (pp. 79-80)"
printfn ""

let backend = LocalBackend() :> IQuantumBackend

printfn "Quantum Backend"
printfn "------------------------------------------------------------------"
printfn "  Backend: %s" backend.Name
printfn "  Type: Statevector Simulator"
printfn ""

// ==============================================================================
// PART 1: DE BROGLIE WAVELENGTH
// ==============================================================================

printfn "=================================================================="
printfn "   Part 1: de Broglie Wavelength of Hydrogen"
printfn "=================================================================="
printfn ""

printfn "Quantum effects are important when de Broglie wavelength is"
printfn "comparable to barrier width (about 1-2 Angstroms)"
printfn ""

let thermalEnergies = [0.01; 0.025; 0.05; 0.1; 0.2]  // eV

printfn "de Broglie wavelength vs thermal energy:"
printfn "------------------------------------------------------------------"
printfn "  E (eV)    lambda_H (A)   lambda_D (A)   lambda_T (A)"
printfn "  ------    ------------   ------------   ------------"

for E in thermalEnergies do
    let lambda_H = deBroglieWavelength m_H E
    let lambda_D = deBroglieWavelength m_D E
    let lambda_T = deBroglieWavelength m_T E
    printfn "  %.3f       %.2f           %.2f           %.2f" E lambda_H lambda_D lambda_T

printfn ""
printfn "At room temperature (kT about 0.025 eV), H has lambda about 1 A"
printfn "This is comparable to lattice spacings - quantum effects matter!"
printfn ""

// ==============================================================================
// PART 2: TUNNELING PROBABILITY
// ==============================================================================

printfn "=================================================================="
printfn "   Part 2: Tunneling Probability (WKB Approximation)"
printfn "=================================================================="
printfn ""

printfn "WKB formula: P = exp(-2 * kappa * w)"
printfn "where kappa = sqrt(2mV0) / hbar"
printfn ""

let metals = [Iron; Nickel; Palladium; Titanium; Steel]

printfn "Tunneling probabilities for different metals:"
printfn "------------------------------------------------------------------"
printfn "  Metal                 V0 (eV)   w (A)    P_H        P_D"
printfn "  -----                 -------   -----    ----       ----"

for metal in metals do
    let P_H = tunnelingProbability m_H metal.BarrierHeight metal.BarrierWidth
    let P_D = tunnelingProbability m_D metal.BarrierHeight metal.BarrierWidth
    printfn "  %-20s  %.2f      %.1f      %.2e   %.2e" 
            metal.Name metal.BarrierHeight metal.BarrierWidth P_H P_D

printfn ""
printfn "Note: Deuterium tunnels much less (isotope effect)"
printfn "This is why tritium (even heavier) shows classical behavior"
printfn ""

// ==============================================================================
// PART 3: CLASSICAL VS QUANTUM DIFFUSION
// ==============================================================================

printfn "=================================================================="
printfn "   Part 3: Classical vs Quantum Diffusion Rates"
printfn "=================================================================="
printfn ""

let temperatures = [77.0; 150.0; 200.0; 250.0; 300.0; 400.0]  // K

printfn "Comparison for Iron (most important for embrittlement):"
printfn "------------------------------------------------------------------"
printfn "  T (K)    Classical (Hz)   Quantum (Hz)   Dominant"
printfn "  -----    --------------   ------------   --------"

let quantum_rate_Fe = quantumTunnelingRate Iron m_H

for T in temperatures do
    let classical_rate = classicalHoppingRate Iron T
    let dominant = if quantum_rate_Fe > classical_rate then "Quantum" else "Classical"
    printfn "    %.0f      %.2e       %.2e       %s" T classical_rate quantum_rate_Fe dominant

printfn ""

let T_cross = crossoverTemperature Iron.AttemptFrequency
printfn "Crossover temperature estimate: %.0f K" T_cross
printfn ""
printfn "Below crossover: tunneling dominates (temperature-independent)"
printfn "Above crossover: Arrhenius behavior (exponential T-dependence)"
printfn ""

// ==============================================================================
// PART 4: ISOTOPE EFFECTS
// ==============================================================================

printfn "=================================================================="
printfn "   Part 4: Isotope Effects (H vs D vs T)"
printfn "=================================================================="
printfn ""

printfn "Isotope effects are a signature of quantum tunneling:"
printfn "Heavier isotopes tunnel less efficiently"
printfn ""

printfn "Diffusion rates in Iron at 200 K:"
printfn "------------------------------------------------------------------"
printfn "  Isotope   Mass (amu)   P_tunnel     Rate (Hz)    D (m2/s)"
printfn "  -------   ----------   --------     ---------    --------"

let isotopes = [("H", m_H, 1.0); ("D", m_D, 2.0); ("T", m_T, 3.0)]

for (name, mass, amu) in isotopes do
    let P = tunnelingProbability mass Iron.BarrierHeight Iron.BarrierWidth
    let rate = Iron.AttemptFrequency * P
    let D = diffusionCoefficient Iron rate
    printfn "  %-8s  %.1f          %.2e     %.2e     %.2e" name amu P rate D

printfn ""
printfn "Isotope effect ratio (H/D): %.1f" 
        (tunnelingProbability m_H Iron.BarrierHeight Iron.BarrierWidth /
         tunnelingProbability m_D Iron.BarrierHeight Iron.BarrierWidth)
printfn ""
printfn "Large isotope effects indicate dominant quantum tunneling"
printfn ""

// ==============================================================================
// PART 5: HYDROGEN EMBRITTLEMENT RISK
// ==============================================================================

printfn "=================================================================="
printfn "   Part 5: Hydrogen Embrittlement Risk Assessment"
printfn "=================================================================="
printfn ""

printfn "Hydrogen embrittlement depends on:"
printfn "  1. Hydrogen diffusion rate (tunneling-enhanced)"
printfn "  2. Hydrogen solubility in the metal"
printfn "  3. Trapping at defects (grain boundaries, dislocations)"
printfn ""

printfn "Material susceptibility to hydrogen embrittlement:"
printfn "------------------------------------------------------------------"
printfn "  Material            Solubility   Diffusion    Risk"
printfn "  --------            ----------   ---------    ----"

for metal in metals do
    let D_H = quantumTunnelingRate metal m_H |> diffusionCoefficient metal
    let risk = 
        if metal.Name.Contains("Fe") || metal.Name.Contains("Steel") then "HIGH"
        elif metal.Name.Contains("Ti") then "MEDIUM"
        elif metal.Name.Contains("Pd") then "LOW (absorbs)"
        else "MEDIUM"
    printfn "  %-20s  %.1e     %.2e     %s" 
            metal.Name metal.HydrogenSolubility D_H risk

printfn ""
printfn "Steel/Iron: High risk despite low solubility (fast diffusion)"
printfn "Palladium: Low risk - absorbs H into bulk, prevents embrittlement"
printfn ""

// ==============================================================================
// PART 6: VQE SIMULATION OF H IN METAL LATTICE
// ==============================================================================

printfn "=================================================================="
printfn "   Part 6: VQE Simulation of Hydrogen in Metal"
printfn "=================================================================="
printfn ""

printfn "VQE calculates Fe-H bond energies - directly relevant to H in metals:"
printfn "  - Fe-H binding energy determines trapping strength"
printfn "  - Spin state affects magnetic coupling to Fe lattice"
printfn "  - Bond length correlates with interstitial site geometry"
printfn ""

// ============================================================================
// LEGITIMATE MOLECULAR MODELS FOR H IN Fe
// ============================================================================
// 
// Iron monohydride (FeH) is a well-characterized molecule:
//   - Ground state: ⁴Δ (quartet, S=3/2, 3 unpaired electrons)
//   - Experimental Fe-H bond length: 1.63 Å
//   - Detected in stellar atmospheres (sunspots, M-dwarf stars)
//   - Well-studied by laser magnetic resonance spectroscopy
//
// WHY THIS IS SCIENTIFICALLY VALID:
// 1. FeH represents the fundamental Fe-H bonding interaction
// 2. The Fe-H bond strength correlates with H binding in Fe lattice
// 3. Comparing FeH ground state vs excited states models local
//    electronic environment changes during diffusion
// 4. Fe-H bond length (1.63 Å) is comparable to interstitial site distances
//
// References:
//   [1] Phillips et al., Astrophysical Journal Supplement, 138, 227 (2002)
//   [2] Dulick et al., Astrophysical Journal, 594, 651 (2003)
// ============================================================================

/// Create FeH molecule at specified bond length (inline for self-contained example)
let createFeHMolecule (bondLength: float) (multiplicity: int) : Molecule =
    {
        Name = sprintf "FeH (M=%d)" multiplicity
        Atoms = [
            { Element = "Fe"; Position = (0.0, 0.0, 0.0) }
            { Element = "H"; Position = (0.0, 0.0, bondLength) }
        ]
        Bonds = [
            { Atom1 = 0; Atom2 = 1; BondOrder = 1.0 }
        ]
        Charge = 0
        Multiplicity = multiplicity
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

// ============================================================================
// VQE CALCULATION: FeH at different bond lengths
// ============================================================================
// 
// Varying Fe-H bond length models H position in the interstitial potential:
// - At equilibrium (1.63 Å): H at potential minimum (trap site)
// - Compressed (1.4 Å): H approaching saddle point
// - Extended (2.0 Å): H in delocalized region
// ============================================================================

printfn "VQE Results for FeH (Iron Monohydride):"
printfn "------------------------------------------------------------------"
printfn ""
printfn "FeH ground state: ⁴Δ (quartet, 3 unpaired electrons)"
printfn "Fe-H equilibrium bond length: 1.63 Å (experimental)"
printfn ""

let feHBondLengths = [
    (1.40, "Compressed (saddle point region)")
    (1.63, "Equilibrium (trap site minimum)")
    (2.00, "Extended (delocalized)")
]

printfn "  Bond (Å)    Description                    VQE E (Ha)    Time (s)"
printfn "  --------    -----------                    ----------    --------"

let mutable equilibriumEnergy = 0.0

for (bondLength, description) in feHBondLengths do
    let feH = createFeHMolecule bondLength 4  // Quartet ground state
    
    match calculateVQEEnergy feH with
    | Ok (energy, iterations, time) ->
        if abs(bondLength - 1.63) < 0.01 then
            equilibriumEnergy <- energy
        printfn "  %.2f        %-30s %.6f      %.2f" bondLength description energy time
    | Error msg ->
        printfn "  %.2f        %-30s Error: %s" bondLength description msg

printfn ""

// ============================================================================
// SPIN STATE COMPARISON
// ============================================================================
// 
// Different spin states of FeH model different local magnetic environments:
// - Quartet (M=4): Ground state, ferromagnetic coupling
// - Doublet (M=2): Excited state, reduced magnetic moment
// 
// The energy difference relates to exchange coupling with Fe lattice.
// ============================================================================

printfn "Spin State Comparison at Equilibrium (1.63 Å):"
printfn "------------------------------------------------------------------"

let spinStates = [
    (4, "Quartet (ground state)")
    (2, "Doublet (excited)")
]

printfn "  Multiplicity    State                VQE E (Ha)"
printfn "  -----------     -----                ----------"

let mutable quartetEnergy = 0.0
let mutable doubletEnergy = 0.0

for (mult, description) in spinStates do
    let feH = createFeHMolecule 1.63 mult
    
    match calculateVQEEnergy feH with
    | Ok (energy, _, _) ->
        if mult = 4 then quartetEnergy <- energy
        if mult = 2 then doubletEnergy <- energy
        printfn "  %d             %-20s %.6f" mult description energy
    | Error msg ->
        printfn "  %d             %-20s Error: %s" mult description msg

printfn ""

if quartetEnergy <> 0.0 && doubletEnergy <> 0.0 then
    let spinGap = (doubletEnergy - quartetEnergy) * hartreeToEV * 1000.0  // meV
    printfn "Spin excitation energy: %.1f meV" spinGap
    printfn "This correlates with magnetic coupling strength in Fe lattice"
    printfn ""

printfn "Physical Interpretation:"
printfn "  - FeH bond energy represents H-Fe interaction strength"
printfn "  - Bond length variation probes the interstitial potential"
printfn "  - Spin state energy gap relates to magnetic coupling"
printfn "  - These are building blocks for understanding H diffusion barriers"
printfn ""

// ==============================================================================
// PART 7: APPLICATIONS TO HYDROGEN ECONOMY
// ==============================================================================

printfn "=================================================================="
printfn "   Part 7: Hydrogen Economy Applications"
printfn "=================================================================="
printfn ""

printfn "1. HYDROGEN STORAGE"
printfn "   - High-pressure tanks: Steel embrittlement concern"
printfn "   - Metal hydrides (Pd, Ti, Mg): Quantum diffusion enables cycling"
printfn "   - Temperature affects absorption/desorption kinetics"
printfn ""

printfn "2. FUEL CELLS"
printfn "   - Pd membranes for H2 purification"
printfn "   - Quantum tunneling through catalyst layers"
printfn "   - Isotope separation (H vs D)"
printfn ""

printfn "3. PIPELINES AND INFRASTRUCTURE"
printfn "   - Steel pipeline embrittlement risk"
printfn "   - Low temperature operation increases tunneling"
printfn "   - Coatings to reduce H ingress"
printfn ""

printfn "4. NUCLEAR APPLICATIONS"
printfn "   - Tritium containment"
printfn "   - H/D/T isotope effects critical"
printfn "   - Radiation-enhanced diffusion"
printfn ""

// ==============================================================================
// QUANTUM ADVANTAGE ANALYSIS
// ==============================================================================

printfn "=================================================================="
printfn "   Why Quantum Computing for H Diffusion?"
printfn "=================================================================="
printfn ""

printfn "CLASSICAL LIMITATIONS:"
printfn "  - Molecular dynamics misses tunneling entirely"
printfn "  - Path integral MD very expensive"
printfn "  - DFT barrier heights inaccurate"
printfn "  - Cannot capture isotope effects correctly"
printfn ""

printfn "QUANTUM ADVANTAGES:"
printfn "  - Direct calculation of tunneling matrix elements"
printfn "  - Accurate barrier heights from correlated wavefunctions"
printfn "  - Natural treatment of quantum nuclear motion"
printfn "  - Isotope effects emerge automatically"
printfn ""

printfn "NISQ-ERA TARGETS:"
printfn "  - H in small metal clusters"
printfn "  - Barrier height calculations"
printfn "  - Isotope effect predictions"
printfn ""

printfn "FAULT-TOLERANT ERA:"
printfn "  - Full metal lattice simulations"
printfn "  - Dynamic tunneling rates"
printfn "  - Multi-H interactions"
printfn ""

// ==============================================================================
// SUMMARY
// ==============================================================================

printfn "=================================================================="
printfn "   Summary"
printfn "=================================================================="
printfn ""

printfn "Key Results:"
printfn "  - Calculated de Broglie wavelengths for H isotopes"
printfn "  - Demonstrated WKB tunneling probability calculations"
printfn "  - Compared classical vs quantum diffusion regimes"
printfn "  - Showed isotope effects as quantum signature"
printfn "  - Assessed hydrogen embrittlement risk"
printfn "  - Performed VQE simulation of H in metal lattice"
printfn ""

printfn "Physics Insights:"
printfn "  - H wavelength comparable to barrier width -> tunneling dominates"
printfn "  - Crossover temperature about %d K for typical metals" (int T_cross)
printfn "  - Large H/D isotope effect confirms quantum mechanism"
printfn "  - Steel embrittlement enhanced by tunneling at low T"
printfn ""

printfn "Practical Implications:"
printfn "  - Hydrogen economy infrastructure needs quantum-aware design"
printfn "  - Low temperature operation may increase embrittlement"
printfn "  - Isotope substitution can reduce tunneling rates"
printfn ""

printfn "RULE1 compliant: All VQE calculations via IQuantumBackend"
printfn ""

printfn "=================================================================="
printfn "  Quantum tunneling: The invisible threat to hydrogen"
printfn "  infrastructure that classical physics cannot predict."
printfn "=================================================================="
printfn ""

// ==============================================================================
// SUGGESTED EXTENSIONS
// ==============================================================================

printfn "Suggested Extensions"
printfn "------------------------------------------------------------------"
printfn ""
printfn "1. Multi-dimensional tunneling:"
printfn "   - Coupled H motion paths"
printfn "   - Corner-cutting effects"
printfn ""
printfn "2. Trap states:"
printfn "   - Grain boundary trapping"
printfn "   - Dislocation core trapping"
printfn ""
printfn "3. Stress effects:"
printfn "   - Barrier modification under strain"
printfn "   - H accumulation at crack tips"
printfn ""
printfn "4. Real metal atoms:"
printfn "   - Fe, Ni, Pd with actual electronic structure"
printfn "   - Charge transfer effects"
printfn ""
