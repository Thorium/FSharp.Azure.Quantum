// ==============================================================================
// Giant Magnetoresistance (GMR) and Spin Transport Example
// ==============================================================================
// Demonstrates VQE for computing spin-dependent transport properties in
// magnetic multilayers, with application to hard drive read heads and MRAM.
//
// Business Context:
// Understanding spin transport is crucial for:
// - Hard drive read heads (GMR sensors)
// - Magnetic RAM (MRAM) for non-volatile memory
// - Spin-transfer torque devices
// - Spintronic logic circuits
// - Magnetic field sensors
//
// This example shows:
// - Mott's two-current model for spin-dependent scattering
// - GMR in Fe/Cr/Fe trilayer structures
// - Spin-dependent resistance calculations
// - VQE simulation of magnetic interactions
// - Device applications and sensor design
//
// Quantum Advantage:
// While simple resistor models capture basic GMR, accurate prediction of
// spin-dependent scattering requires quantum simulation of:
// - Spin-polarized band structures
// - Interface scattering
// - Exchange coupling between layers
//
// THEORETICAL FOUNDATION:
// Based on "Concepts of Materials Science" by Adrian P. Sutton
// Chapter 7: Small is Different
// Section 7.5: Giant Magnetoresistance (pp. 87-93)
//
// Nobel Prize in Physics 2007: Albert Fert and Peter Grünberg
// for the discovery of Giant Magnetoresistance
//
// RULE1 COMPLIANCE:
// All quantum calculations use IQuantumBackend throughout.
// ==============================================================================

(*
===============================================================================
 Background Theory: Giant Magnetoresistance
===============================================================================

MATERIALS SCIENCE REFERENCE:
This example implements concepts from "Concepts of Materials Science" by
Adrian P. Sutton (Oxford University Press):
  - Chapter 7: Small is Different (pp. 81-93)
  - Section 7.5: Giant Magnetoresistance

GIANT MAGNETORESISTANCE (GMR) is a quantum mechanical effect where the
electrical resistance of a multilayer structure changes dramatically depending
on the relative alignment of magnetization in adjacent ferromagnetic layers.

The discovery of GMR in 1988 by Albert Fert (France) and Peter Grünberg
(Germany) revolutionized data storage technology and was awarded the
2007 Nobel Prize in Physics.

MOTT'S TWO-CURRENT MODEL (Sutton pp. 87-89):

In a ferromagnet, conduction electrons carry spin (up or down).
Due to exchange splitting, electrons experience different scattering
rates depending on their spin:

  - Spin-↑ electrons: Low resistance (r) when aligned with magnetization
  - Spin-↓ electrons: High resistance (R) when anti-aligned

Nevill Mott proposed treating spin-up and spin-down currents as
two parallel channels, each with its own resistance.

For a TRILAYER structure (e.g., Fe/Cr/Fe):

PARALLEL ALIGNMENT (↑↑):
Both ferromagnetic layers magnetized in same direction.

                    ┌───────┐
  Spin-↑ channel:   │   r   │───│   r   │  = 2r
                    └───────┘
                    ┌───────┐
  Spin-↓ channel:   │   R   │───│   R   │  = 2R
                    └───────┘

Combined (parallel resistors):
  R_parallel = (2r × 2R) / (2r + 2R) = 2rR / (r + R)

ANTIPARALLEL ALIGNMENT (↑↓):
Adjacent ferromagnetic layers magnetized oppositely.

                    ┌───────┐
  Spin-↑ channel:   │   r   │───│   R   │  = r + R
                    └───────┘
                    ┌───────┐
  Spin-↓ channel:   │   R   │───│   r   │  = r + R
                    └───────┘

Combined (parallel resistors):
  R_antiparallel = (r + R)(r + R) / (2(r + R)) = (r + R) / 2

GMR RATIO:
The magnetoresistance ratio is defined as:

  GMR = (R_AP - R_P) / R_P = (R - r)² / (4rR)

For large asymmetry (R >> r), GMR can exceed 100%!

KEY PHYSICS (Sutton p. 89):
1. Exchange splitting creates spin-dependent density of states
2. Majority spin electrons have lower scattering (aligned with d-band)
3. Minority spin electrons scatter strongly (anti-aligned with d-band)
4. Interface scattering dominates in thin multilayers

RKKY COUPLING (Sutton p. 91):
Ferromagnetic layers couple through the non-magnetic spacer via
Ruderman-Kittel-Kasuya-Yosida (RKKY) interaction:

  J(d) ∝ cos(2k_F × d) / d²

where d is spacer thickness and k_F is Fermi wavevector.
This oscillatory coupling can be ferromagnetic or antiferromagnetic!

APPLICATIONS:
1. Hard drive read heads (GMR sensors since 1997)
2. Magnetic RAM (MRAM) for non-volatile storage
3. Magnetic field sensors (automotive, industrial)
4. Spin-transfer torque devices

References:
  [1] Sutton, A.P. "Concepts of Materials Science" Ch.7 (Oxford, 2021)
  [2] Wikipedia: Giant_magnetoresistance
  [3] Fert & Grünberg Nobel Prize lecture (2007)
  [4] Baibich et al. "Giant Magnetoresistance" PRL 61, 2472 (1988)
*)

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

/// Bohr magneton (J/T)
let mu_B = 9.274e-24

/// Electron volt to Joules
let eV_to_J = 1.60218e-19

/// Joules to electron volt
let J_to_eV = 1.0 / eV_to_J

/// Angstrom to meters
let A_to_m = 1.0e-10

/// Hartree to eV
let hartreeToEV = 27.2114

// ==============================================================================
// FERROMAGNETIC MATERIAL DATA
// ==============================================================================

/// Ferromagnetic material properties for GMR
type FerromagneticMaterial = {
    Name: string
    SpinPolarization: float     // P = (n↑ - n↓)/(n↑ + n↓) at Fermi level
    ExchangeSplitting: float    // eV
    MajorityResistivity: float  // μΩ·cm (spin-↑ when magnetized)
    MinorityResistivity: float  // μΩ·cm (spin-↓ when magnetized)
    CurieTemperature: float     // K
    MagneticMoment: float       // μ_B per atom
}

let Iron = {
    Name = "Iron (Fe)"
    SpinPolarization = 0.45
    ExchangeSplitting = 2.2
    MajorityResistivity = 5.0
    MinorityResistivity = 15.0
    CurieTemperature = 1043.0
    MagneticMoment = 2.22
}

let Cobalt = {
    Name = "Cobalt (Co)"
    SpinPolarization = 0.42
    ExchangeSplitting = 1.8
    MajorityResistivity = 4.0
    MinorityResistivity = 12.0
    CurieTemperature = 1388.0
    MagneticMoment = 1.72
}

let Nickel = {
    Name = "Nickel (Ni)"
    SpinPolarization = 0.33
    ExchangeSplitting = 0.6
    MajorityResistivity = 3.0
    MinorityResistivity = 8.0
    CurieTemperature = 627.0
    MagneticMoment = 0.62
}

let Permalloy = {  // Ni80Fe20
    Name = "Permalloy (Ni80Fe20)"
    SpinPolarization = 0.38
    ExchangeSplitting = 0.8
    MajorityResistivity = 4.0
    MinorityResistivity = 10.0
    CurieTemperature = 850.0
    MagneticMoment = 1.00
}

/// Non-magnetic spacer materials
type SpacerMaterial = {
    Name: string
    FermiWavevector: float  // nm⁻¹
    Resistivity: float      // μΩ·cm
}

let Chromium = {
    Name = "Chromium (Cr)"
    FermiWavevector = 12.0  // nm⁻¹
    Resistivity = 12.9
}

let Copper = {
    Name = "Copper (Cu)"
    FermiWavevector = 13.6  // nm⁻¹
    Resistivity = 1.7
}

// ==============================================================================
// MOTT TWO-CURRENT MODEL
// ==============================================================================

/// Calculate resistance for parallel magnetic alignment
/// R_parallel = 2rR / (r + R) where r = majority, R = minority
let resistanceParallel (majorityR: float) (minorityR: float) : float =
    2.0 * majorityR * minorityR / (majorityR + minorityR)

/// Calculate resistance for antiparallel magnetic alignment
/// R_antiparallel = (r + R) / 2
let resistanceAntiparallel (majorityR: float) (minorityR: float) : float =
    (majorityR + minorityR) / 2.0

/// Calculate GMR ratio
/// GMR = (R_AP - R_P) / R_P = (R - r)² / (4rR)
let gmrRatio (majorityR: float) (minorityR: float) : float =
    let R_P = resistanceParallel majorityR minorityR
    let R_AP = resistanceAntiparallel majorityR minorityR
    (R_AP - R_P) / R_P

/// Alternative GMR formula
let gmrRatioAlt (majorityR: float) (minorityR: float) : float =
    let diff = minorityR - majorityR
    (diff * diff) / (4.0 * majorityR * minorityR)

/// Calculate spin asymmetry parameter α = R/r
let spinAsymmetry (majorityR: float) (minorityR: float) : float =
    minorityR / majorityR

// ==============================================================================
// RKKY INTERLAYER COUPLING
// ==============================================================================

/// RKKY coupling strength vs spacer thickness
/// J(d) ∝ cos(2k_F × d) / d²
let rkkyyCoupling (spacer: SpacerMaterial) (thickness_nm: float) : float =
    let k_F = spacer.FermiWavevector  // nm⁻¹
    let J0 = 1.0  // Coupling strength prefactor (arbitrary units)
    J0 * Math.Cos(2.0 * k_F * thickness_nm) / (thickness_nm * thickness_nm)

/// Determine coupling type from RKKY value
let couplingType (coupling: float) : string =
    if coupling > 0.01 then "Ferromagnetic"
    elif coupling < -0.01 then "Antiferromagnetic"
    else "Weak"

// ==============================================================================
// QUANTUM BACKEND SETUP
// ==============================================================================

printfn "=================================================================="
printfn "   Giant Magnetoresistance (GMR) and Spin Transport"
printfn "=================================================================="
printfn ""

printfn "Theoretical Foundation: Sutton 'Concepts of Materials Science'"
printfn "------------------------------------------------------------------"
printfn "  Chapter 7: Small is Different"
printfn "  Section 7.5: Giant Magnetoresistance (pp. 87-93)"
printfn ""
printfn "  Nobel Prize in Physics 2007: Fert & Grünberg"
printfn ""

let backend = LocalBackend() :> IQuantumBackend

printfn "Quantum Backend"
printfn "------------------------------------------------------------------"
printfn "  Backend: %s" backend.Name
printfn "  Type: Statevector Simulator"
printfn ""

// ==============================================================================
// PART 1: MOTT'S TWO-CURRENT MODEL
// ==============================================================================

printfn "=================================================================="
printfn "   Part 1: Mott's Two-Current Model"
printfn "=================================================================="
printfn ""

printfn "Nevill Mott's key insight: In ferromagnets, spin-up and spin-down"
printfn "electrons experience different scattering rates."
printfn ""

printfn "Two-channel resistor model:"
printfn ""
printfn "  PARALLEL (↑↑):                    ANTIPARALLEL (↑↓):"
printfn ""
printfn "  Spin-↑:  ─[r]─[r]─  (2r)         Spin-↑:  ─[r]─[R]─  (r+R)"
printfn "  Spin-↓:  ─[R]─[R]─  (2R)         Spin-↓:  ─[R]─[r]─  (r+R)"
printfn ""
printfn "  R_P = 2rR/(r+R)                   R_AP = (r+R)/2"
printfn ""

let ferromagnets = [Iron; Cobalt; Nickel; Permalloy]

printfn "Spin-dependent resistivities of ferromagnetic materials:"
printfn "------------------------------------------------------------------"
printfn "  Material              r (μΩ·cm)   R (μΩ·cm)   α = R/r   P"
printfn "  --------              ---------   ---------   -------   ---"

for fm in ferromagnets do
    let alpha = spinAsymmetry fm.MajorityResistivity fm.MinorityResistivity
    printfn "  %-20s    %.1f         %.1f         %.2f      %.2f" 
            fm.Name fm.MajorityResistivity fm.MinorityResistivity alpha fm.SpinPolarization

printfn ""
printfn "where r = majority spin (low scattering), R = minority spin (high scattering)"
printfn "      α = spin asymmetry ratio, P = spin polarization at Fermi level"
printfn ""

// ==============================================================================
// PART 2: GMR IN Fe/Cr/Fe TRILAYER
// ==============================================================================

printfn "=================================================================="
printfn "   Part 2: GMR in Fe/Cr/Fe Trilayer (Original Discovery)"
printfn "=================================================================="
printfn ""

printfn "In 1988, Fert and Grünberg independently discovered GMR in"
printfn "Fe/Cr multilayers, observing ~50%% resistance change!"
printfn ""

let r = Iron.MajorityResistivity
let R = Iron.MinorityResistivity

let R_parallel = resistanceParallel r R
let R_antiparallel = resistanceAntiparallel r R
let gmr = gmrRatio r R

printfn "Fe/Cr/Fe trilayer calculation:"
printfn "------------------------------------------------------------------"
printfn "  Majority spin resistivity (r):     %.1f μΩ·cm" r
printfn "  Minority spin resistivity (R):     %.1f μΩ·cm" R
printfn "  Spin asymmetry (α = R/r):          %.2f" (spinAsymmetry r R)
printfn ""
printfn "  Parallel resistance (R_P):         %.2f μΩ·cm" R_parallel
printfn "  Antiparallel resistance (R_AP):    %.2f μΩ·cm" R_antiparallel
printfn ""
printfn "  GMR Ratio = (R_AP - R_P)/R_P:      %.1f%%" (gmr * 100.0)
printfn ""

printfn "Verification with alternative formula:"
let gmr_alt = gmrRatioAlt r R
printfn "  GMR = (R-r)²/(4rR):                %.1f%%" (gmr_alt * 100.0)
printfn ""

// ==============================================================================
// PART 3: COMPARISON OF FERROMAGNETIC MATERIALS
// ==============================================================================

printfn "=================================================================="
printfn "   Part 3: GMR for Different Ferromagnetic Materials"
printfn "=================================================================="
printfn ""

printfn "GMR depends on spin asymmetry - higher α gives larger effect:"
printfn "------------------------------------------------------------------"
printfn "  Material              R_P (μΩ·cm)   R_AP (μΩ·cm)   GMR (%%)"
printfn "  --------              -----------   ------------   -------"

for fm in ferromagnets do
    let R_P = resistanceParallel fm.MajorityResistivity fm.MinorityResistivity
    let R_AP = resistanceAntiparallel fm.MajorityResistivity fm.MinorityResistivity
    let gmr = gmrRatio fm.MajorityResistivity fm.MinorityResistivity
    printfn "  %-20s    %.2f          %.2f           %.1f" 
            fm.Name R_P R_AP (gmr * 100.0)

printfn ""
printfn "Iron shows highest GMR due to large spin asymmetry (α = 3.0)"
printfn ""

// ==============================================================================
// PART 4: RKKY COUPLING AND SPACER THICKNESS
// ==============================================================================

printfn "=================================================================="
printfn "   Part 4: RKKY Interlayer Coupling"
printfn "=================================================================="
printfn ""

printfn "The magnetic layers couple through the non-magnetic spacer via"
printfn "RKKY (Ruderman-Kittel-Kasuya-Yosida) interaction:"
printfn ""
printfn "  J(d) ∝ cos(2k_F × d) / d²"
printfn ""
printfn "This oscillates between ferromagnetic and antiferromagnetic!"
printfn ""

let spacerThicknesses = [0.5; 0.8; 1.0; 1.2; 1.5; 1.8; 2.0; 2.5; 3.0]

printfn "RKKY coupling vs Cr spacer thickness:"
printfn "------------------------------------------------------------------"
printfn "  d (nm)    J (a.u.)    Coupling Type"
printfn "  ------    --------    -------------"

for d in spacerThicknesses do
    let J = rkkyyCoupling Chromium d
    let coupling = couplingType J
    let Jstr = if J >= 0.0 then sprintf "+%.3f" J else sprintf "%.3f" J
    printfn "    %.1f      %s       %s" d Jstr coupling

printfn ""
printfn "Note: GMR requires ANTIFERROMAGNETIC coupling (J < 0)"
printfn "so layers naturally align antiparallel at zero field."
printfn ""

// ==============================================================================
// PART 5: DEVICE APPLICATIONS
// ==============================================================================

printfn "=================================================================="
printfn "   Part 5: GMR Device Applications"
printfn "=================================================================="
printfn ""

printfn "1. HARD DRIVE READ HEADS (since 1997)"
printfn "   - GMR sensors replaced anisotropic magnetoresistance (AMR)"
printfn "   - Enabled ~100x increase in storage density"
printfn "   - Sensitivity: ~1%% change per Oe"
printfn ""

// Calculate sensitivity
let fieldSensitivity = gmr * 100.0 / 50.0  // Assume 50 Oe switching field
printfn "   Estimated Fe/Cr/Fe sensitivity: %.2f%%/Oe" fieldSensitivity
printfn ""

printfn "2. MAGNETIC RAM (MRAM)"
printfn "   - Non-volatile memory using magnetic states"
printfn "   - Read via GMR/TMR sensing"
printfn "   - Write via spin-transfer torque (STT)"
printfn ""

printfn "3. MAGNETIC FIELD SENSORS"
printfn "   - Automotive: wheel speed, position"
printfn "   - Industrial: current sensing, proximity"
printfn "   - Biomedical: magnetic bead detection"
printfn ""

printfn "4. SPIN VALVES"
printfn "   - One layer pinned (exchange bias)"
printfn "   - One layer free (responds to field)"
printfn "   - Linear response to magnetic field"
printfn ""

// ==============================================================================
// PART 6: VQE SIMULATION OF MAGNETIC COUPLING
// ==============================================================================

printfn "=================================================================="
printfn "   Part 6: VQE Simulation of Exchange Coupling (Fe₂ Dimer)"
printfn "=================================================================="
printfn ""

printfn "SCIENTIFIC APPROACH:"
printfn "  The Fe₂ dimer is a LEGITIMATE benchmark for magnetic exchange coupling."
printfn "  This is the simplest system that exhibits the physics of GMR:"
printfn ""
printfn "  - Two Fe atoms with unpaired d-electrons"
printfn "  - Exchange coupling J determines alignment preference"
printfn "  - J > 0: Ferromagnetic (parallel spins, high-spin ground state)"
printfn "  - J < 0: Antiferromagnetic (antiparallel spins, low-spin ground state)"
printfn ""
printfn "  VQE computes the energy difference between spin states:"
printfn "  J = E(singlet) - E(high-spin)  [Heisenberg model convention]"
printfn ""

printfn "Fe₂ Dimer Properties:"
printfn "------------------------------------------------------------------"
printfn "  Fe-Fe bond length: ~2.02 Å (experimental)"
printfn "  Ground state: ⁷Δᵤ (septet, S=3, 6 unpaired electrons)"
printfn "  Each Fe: 3d⁶4s² configuration, ~4 unpaired d-electrons"
printfn ""
printfn "  This is the BUILDING BLOCK of Fe/Cr GMR physics!"
printfn "  Exchange coupling in Fe₂ → exchange coupling in Fe layers"
printfn ""

// ==============================================================================
// Fe₂ DIMER MODEL FOR EXCHANGE COUPLING
// ==============================================================================

/// Fe-Fe bond length in Angstroms (experimental)
let feFeBondLength = 2.02

/// Create Fe₂ dimer at specified spin state
/// 
/// The exchange coupling constant J is computed from:
/// J = E(low-spin) - E(high-spin)
/// 
/// For Fe₂:
/// - High-spin (septet, M=7): Ferromagnetically coupled
/// - Low-spin (singlet, M=1): Antiferromagnetically coupled
let createFe2Dimer (multiplicity: int) : Molecule =
    {
        Name = sprintf "Fe2_M%d" multiplicity
        Atoms = [
            { Element = "Fe"; Position = (0.0, 0.0, 0.0) }
            { Element = "Fe"; Position = (feFeBondLength, 0.0, 0.0) }
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

// ==============================================================================
// VQE CALCULATIONS FOR Fe₂ SPIN STATES
// ==============================================================================

printfn "VQE Results for Fe₂ Dimer Exchange Coupling:"
printfn "------------------------------------------------------------------"
printfn ""

// High-spin (septet, S=3) - Ferromagnetically coupled
printfn "1. Fe₂ High-Spin (Septet, M=7) - Ferromagnetic coupling:"
let fe2HighSpin = createFe2Dimer 7
printfn "   Molecule: %s" fe2HighSpin.Name
printfn "   Spin: S = 3 (6 unpaired electrons)"
printfn "   Physical meaning: Fe spins PARALLEL (ferromagnetic)"
let mutable E_highspin = 0.0
match calculateVQEEnergy fe2HighSpin with
| Ok (energy, iterations, time) ->
    E_highspin <- energy
    printfn "   VQE Ground State Energy: %.6f Hartree" energy
    printfn "   Iterations: %d, Time: %.2f s" iterations time
| Error msg ->
    printfn "   Error: %s" msg
printfn ""

// Low-spin (singlet, S=0) - Antiferromagnetically coupled
printfn "2. Fe₂ Low-Spin (Singlet, M=1) - Antiferromagnetic coupling:"
let fe2LowSpin = createFe2Dimer 1
printfn "   Molecule: %s" fe2LowSpin.Name
printfn "   Spin: S = 0 (all electrons paired)"
printfn "   Physical meaning: Fe spins ANTIPARALLEL (antiferromagnetic)"
let mutable E_lowspin = 0.0
match calculateVQEEnergy fe2LowSpin with
| Ok (energy, iterations, time) ->
    E_lowspin <- energy
    printfn "   VQE Ground State Energy: %.6f Hartree" energy
    printfn "   Iterations: %d, Time: %.2f s" iterations time
| Error msg ->
    printfn "   Error: %s" msg
printfn ""

// Triplet state for comparison
printfn "3. Fe₂ Intermediate-Spin (Triplet, M=3) - Mixed coupling:"
let fe2Triplet = createFe2Dimer 3
printfn "   Molecule: %s" fe2Triplet.Name
printfn "   Spin: S = 1 (2 unpaired electrons)"
match calculateVQEEnergy fe2Triplet with
| Ok (energy, iterations, time) ->
    printfn "   VQE Ground State Energy: %.6f Hartree" energy
    printfn "   Iterations: %d, Time: %.2f s" iterations time
| Error msg ->
    printfn "   Error: %s" msg
printfn ""

// ==============================================================================
// EXCHANGE COUPLING ANALYSIS
// ==============================================================================

printfn "Exchange Coupling Analysis:"
printfn "------------------------------------------------------------------"
if E_highspin <> 0.0 && E_lowspin <> 0.0 then
    let J_Hartree = E_lowspin - E_highspin
    let J_eV = J_Hartree * 27.2114
    let J_meV = J_eV * 1000.0
    let J_K = J_eV * 11604.5  // 1 eV = 11604.5 K
    
    printfn "  J = E(singlet) - E(septet)"
    printfn "    = %.6f - %.6f" E_lowspin E_highspin
    printfn "    = %.6f Hartree" J_Hartree
    printfn "    = %.3f eV = %.1f meV" J_eV J_meV
    printfn "    = %.0f K (Curie temperature scale)" J_K
    printfn ""
    
    if J_Hartree > 0.0 then
        printfn "  Result: J > 0 → FERROMAGNETIC ground state"
        printfn "  (High-spin septet is lower energy)"
    else
        printfn "  Result: J < 0 → ANTIFERROMAGNETIC ground state"
        printfn "  (Low-spin singlet is lower energy)"
    printfn ""
    
    printfn "  Connection to GMR:"
    printfn "  - Fe layers in Fe/Cr multilayers show this exchange physics"
    printfn "  - RKKY coupling through Cr modulates effective J"
    printfn "  - Resistance depends on relative spin alignment"
else
    printfn "  (Exchange coupling calculation requires both spin states)"
printfn ""

printfn "SCIENTIFIC INTERPRETATION:"
printfn "------------------------------------------------------------------"
printfn "  The Fe₂ dimer energy difference directly measures exchange coupling."
printfn "  This is the SAME physics that drives GMR in Fe/Cr multilayers:"
printfn ""
printfn "  - Fe layer 1: All spins aligned (ferromagnetic, like high-spin Fe₂)"
printfn "  - Fe layer 2: All spins aligned (ferromagnetic)"
printfn "  - RKKY coupling: Determines if layers are parallel or antiparallel"
printfn "  - Resistance: Depends on relative alignment (GMR effect)"
printfn ""
printfn "  VQE on Fe₂ gives insight into this fundamental magnetic physics!"
printfn ""

// ==============================================================================
// PART 7: MODERN GMR STRUCTURES
// ==============================================================================

printfn "=================================================================="
printfn "   Part 7: Modern GMR and TMR Structures"
printfn "=================================================================="
printfn ""

printfn "Evolution of magnetoresistive technologies:"
printfn ""
printfn "  1988: GMR discovered (Fe/Cr multilayers, ~50%% MR)"
printfn "  1991: Spin valves (Co/Cu/Co, ~10%% at room temp)"
printfn "  1995: TMR (tunnel magnetoresistance, Al2O3 barrier)"
printfn "  2004: High TMR with MgO barrier (>200%%)"
printfn "  2010+: STT-MRAM commercialization"
printfn ""

printfn "GMR vs TMR:"
printfn "------------------------------------------------------------------"
printfn "  Property          GMR             TMR"
printfn "  --------          ---             ---"
printfn "  MR ratio          ~20%%            >200%%"
printfn "  Resistance        Low (Ω)         High (kΩ)"
printfn "  Mechanism         Spin scattering Spin tunneling"
printfn "  Applications      HDD sensors     MRAM"
printfn ""

// ==============================================================================
// QUANTUM ADVANTAGE ANALYSIS
// ==============================================================================

printfn "=================================================================="
printfn "   Why Quantum Computing for Spintronics?"
printfn "=================================================================="
printfn ""

printfn "CLASSICAL LIMITATIONS:"
printfn "  - Spin-polarized DFT limited accuracy"
printfn "  - Interface effects hard to capture"
printfn "  - Dynamical spin transport challenging"
printfn "  - Many-body correlations in magnetic materials"
printfn ""

printfn "QUANTUM ADVANTAGES:"
printfn "  - Direct simulation of spin degrees of freedom"
printfn "  - Accurate exchange coupling calculations"
printfn "  - Spin-orbit coupling effects"
printfn "  - Non-equilibrium transport"
printfn ""

printfn "NISQ-ERA TARGETS:"
printfn "  - Small magnetic clusters (2-6 atoms)"
printfn "  - Exchange coupling vs distance"
printfn "  - Spin-dependent scattering amplitudes"
printfn ""

printfn "FAULT-TOLERANT ERA:"
printfn "  - Full multilayer band structures"
printfn "  - Temperature-dependent transport"
printfn "  - Device-scale simulations"
printfn ""

// ==============================================================================
// SUMMARY
// ==============================================================================

printfn "=================================================================="
printfn "   Summary"
printfn "=================================================================="
printfn ""

printfn "Key Results:"
printfn "  - Demonstrated Mott two-current model for GMR"
printfn "  - Calculated GMR ratios for various ferromagnets"
printfn "  - Showed RKKY oscillatory coupling vs spacer thickness"
printfn "  - Performed VQE simulation of magnetic coupling"
printfn ""

printfn "Physics Insights:"
printfn "  - GMR arises from spin-dependent scattering"
printfn "  - Spin asymmetry α = R/r determines GMR magnitude"
printfn "  - RKKY coupling oscillates with spacer thickness"
printfn "  - Antiferromagnetic coupling needed for GMR"
printfn ""

printfn "Historical Impact:"
printfn "  - GMR enabled >1000x increase in HDD capacity"
printfn "  - Nobel Prize 2007: Fert & Grünberg"
printfn "  - Foundation for MRAM and spintronics"
printfn ""

printfn "RULE1 compliant: All VQE calculations via IQuantumBackend"
printfn ""

printfn "=================================================================="
printfn "  Giant Magnetoresistance: A quantum effect that changed"
printfn "  data storage forever and launched the field of spintronics."
printfn "=================================================================="
printfn ""

// ==============================================================================
// SUGGESTED EXTENSIONS
// ==============================================================================

printfn "Suggested Extensions"
printfn "------------------------------------------------------------------"
printfn ""
printfn "1. Spin valve structures:"
printfn "   - Exchange-biased pinned layer"
printfn "   - Angular dependence of resistance"
printfn ""
printfn "2. Tunnel magnetoresistance (TMR):"
printfn "   - Jullière model"
printfn "   - MgO barrier effects"
printfn ""
printfn "3. Spin-transfer torque:"
printfn "   - Current-induced magnetization switching"
printfn "   - STT-MRAM write mechanisms"
printfn ""
printfn "4. Spin-orbit torque:"
printfn "   - Spin Hall effect"
printfn "   - Rashba-Edelstein effect"
printfn ""
