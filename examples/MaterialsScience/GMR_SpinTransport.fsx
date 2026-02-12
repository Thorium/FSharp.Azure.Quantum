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
// Experimental context:
// Spin- and band-structure features are commonly investigated using advanced
// beam-based probes; accelerator-driven photon sources (synchrotrons) are a
// major enabler for materials characterization at scale.
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
// Nobel Prize in Physics 2007: Albert Fert and Peter Gruenberg
// for the discovery of Giant Magnetoresistance
//
// Usage:
//   dotnet fsi GMR_SpinTransport.fsx                                   (defaults)
//   dotnet fsi GMR_SpinTransport.fsx -- --help                         (show options)
//   dotnet fsi GMR_SpinTransport.fsx -- --layers 5                     (multilayer count)
//   dotnet fsi GMR_SpinTransport.fsx -- --temperature 300              (temperature K)
//   dotnet fsi GMR_SpinTransport.fsx -- --output results.json --csv results.csv
//   dotnet fsi GMR_SpinTransport.fsx -- --quiet --output results.json  (pipeline mode)
//
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

The discovery of GMR in 1988 by Albert Fert (France) and Peter Gruenberg
(Germany) revolutionized data storage technology and was awarded the
2007 Nobel Prize in Physics.

MOTT'S TWO-CURRENT MODEL (Sutton pp. 87-89):

In a ferromagnet, conduction electrons carry spin (up or down).
Due to exchange splitting, electrons experience different scattering
rates depending on their spin:

  - Spin-up electrons: Low resistance (r) when aligned with magnetization
  - Spin-down electrons: High resistance (R) when anti-aligned

Nevill Mott proposed treating spin-up and spin-down currents as
two parallel channels, each with its own resistance.

For a TRILAYER structure (e.g., Fe/Cr/Fe):

PARALLEL ALIGNMENT:
Both ferromagnetic layers magnetized in same direction.

  Spin-up channel:   r + r = 2r
  Spin-down channel:  R + R = 2R

Combined (parallel resistors):
  R_parallel = (2r * 2R) / (2r + 2R) = 2rR / (r + R)

ANTIPARALLEL ALIGNMENT:
Adjacent ferromagnetic layers magnetized oppositely.

  Spin-up channel:   r + R
  Spin-down channel:  R + r

Combined (parallel resistors):
  R_antiparallel = (r + R)(r + R) / (2(r + R)) = (r + R) / 2

GMR RATIO:
The magnetoresistance ratio is defined as:

  GMR = (R_AP - R_P) / R_P = (R - r)^2 / (4rR)

For large asymmetry (R >> r), GMR can exceed 100%!

KEY PHYSICS (Sutton p. 89):
1. Exchange splitting creates spin-dependent density of states
2. Majority spin electrons have lower scattering (aligned with d-band)
3. Minority spin electrons scatter strongly (anti-aligned with d-band)
4. Interface scattering dominates in thin multilayers

RKKY COUPLING (Sutton p. 91):
Ferromagnetic layers couple through the non-magnetic spacer via
Ruderman-Kittel-Kasuya-Yosida (RKKY) interaction:

  J(d) ~ cos(2k_F * d) / d^2

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
  [3] Fert & Gruenberg Nobel Prize lecture (2007)
  [4] Baibich et al. "Giant Magnetoresistance" PRL 61, 2472 (1988)
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
    "GMR_SpinTransport.fsx"
    "VQE simulation of GMR exchange coupling and spin-dependent transport."
    [ { Cli.OptionSpec.Name = "layers";      Description = "Number of magnetic layers for multilayer calculation"; Default = Some "3" }
      { Cli.OptionSpec.Name = "temperature"; Description = "Temperature in Kelvin";                                Default = Some "300" }
      { Cli.OptionSpec.Name = "output";      Description = "Write results to JSON file";                           Default = None }
      { Cli.OptionSpec.Name = "csv";         Description = "Write results to CSV file";                            Default = None }
      { Cli.OptionSpec.Name = "quiet";       Description = "Suppress informational output";                        Default = None } ]
    args

let quiet = Cli.hasFlag "quiet" args
let numLayers = args |> Cli.getIntOr "layers" 3
let userTemperature = args |> Cli.getFloatOr "temperature" 300.0
let outputPath = Cli.tryGet "output" args
let csvPath = Cli.tryGet "csv" args

/// Bohr magneton (J/T)
let mu_B = 9.274e-24

// ==============================================================================
// FERROMAGNETIC MATERIAL DATA
// ==============================================================================

/// Ferromagnetic material properties for GMR
type FerromagneticMaterial = {
    Name: string
    SpinPolarization: float     // P = (n_up - n_down)/(n_up + n_down) at Fermi level
    ExchangeSplitting: float    // eV
    MajorityResistivity: float  // uOhm*cm (spin-up when magnetized)
    MinorityResistivity: float  // uOhm*cm (spin-down when magnetized)
    CurieTemperature: float     // K
    MagneticMoment: float       // mu_B per atom
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
    FermiWavevector: float  // nm^-1
    Resistivity: float      // uOhm*cm
}

let Chromium = {
    Name = "Chromium (Cr)"
    FermiWavevector = 12.0  // nm^-1
    Resistivity = 12.9
}

let Copper = {
    Name = "Copper (Cu)"
    FermiWavevector = 13.6  // nm^-1
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
/// GMR = (R_AP - R_P) / R_P = (R - r)^2 / (4rR)
let gmrRatio (majorityR: float) (minorityR: float) : float =
    let R_P = resistanceParallel majorityR minorityR
    let R_AP = resistanceAntiparallel majorityR minorityR
    (R_AP - R_P) / R_P

/// Alternative GMR formula
let gmrRatioAlt (majorityR: float) (minorityR: float) : float =
    let diff = minorityR - majorityR
    (diff * diff) / (4.0 * majorityR * minorityR)

/// Calculate spin asymmetry parameter alpha = R/r
let spinAsymmetry (majorityR: float) (minorityR: float) : float =
    minorityR / majorityR

// ==============================================================================
// RKKY INTERLAYER COUPLING
// ==============================================================================

/// RKKY coupling strength vs spacer thickness
/// J(d) ~ cos(2k_F * d) / d^2
let rkkyCoupling (spacer: SpacerMaterial) (thickness_nm: float) : float =
    let k_F = spacer.FermiWavevector  // nm^-1
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

if not quiet then
    printfn "=================================================================="
    printfn "   Giant Magnetoresistance (GMR) and Spin Transport"
    printfn "=================================================================="
    printfn ""
    printfn "Theoretical Foundation: Sutton 'Concepts of Materials Science'"
    printfn "------------------------------------------------------------------"
    printfn "  Chapter 7: Small is Different"
    printfn "  Section 7.5: Giant Magnetoresistance (pp. 87-93)"
    printfn ""
    printfn "  Nobel Prize in Physics 2007: Fert & Gruenberg"
    printfn ""
    printfn "Quantum Backend"
    printfn "------------------------------------------------------------------"
    printfn "  Backend: %s" backend.Name
    printfn "  Type: Statevector Simulator"
    printfn ""
    printfn "  Layers: %d" numLayers
    printfn "  Temperature: %.0f K" userTemperature
    printfn ""

// ==============================================================================
// PART 1: MOTT'S TWO-CURRENT MODEL
// ==============================================================================

let ferromagnets = [Iron; Cobalt; Nickel; Permalloy]

if not quiet then
    printfn "=================================================================="
    printfn "   Part 1: Mott's Two-Current Model"
    printfn "=================================================================="
    printfn ""
    printfn "Nevill Mott's key insight: In ferromagnets, spin-up and spin-down"
    printfn "electrons experience different scattering rates."
    printfn ""
    printfn "Two-channel resistor model:"
    printfn ""
    printfn "  PARALLEL (aligned):                ANTIPARALLEL (opposed):"
    printfn ""
    printfn "  Spin-up:  -[r]-[r]-  (2r)         Spin-up:  -[r]-[R]-  (r+R)"
    printfn "  Spin-down: -[R]-[R]-  (2R)         Spin-down: -[R]-[r]-  (r+R)"
    printfn ""
    printfn "  R_P = 2rR/(r+R)                   R_AP = (r+R)/2"
    printfn ""
    printfn "Spin-dependent resistivities of ferromagnetic materials:"
    printfn "------------------------------------------------------------------"
    printfn "  Material              r (uOhm*cm)  R (uOhm*cm)  alpha=R/r  P"
    printfn "  --------              ----------  ----------  --------  ---"

    for fm in ferromagnets do
        let alpha = spinAsymmetry fm.MajorityResistivity fm.MinorityResistivity
        printfn "  %-20s    %.1f         %.1f         %.2f      %.2f"
                fm.Name fm.MajorityResistivity fm.MinorityResistivity alpha fm.SpinPolarization

    printfn ""
    printfn "where r = majority spin (low scattering), R = minority spin (high scattering)"
    printfn "      alpha = spin asymmetry ratio, P = spin polarization at Fermi level"
    printfn ""

// ==============================================================================
// PART 2: GMR IN Fe/Cr/Fe TRILAYER
// ==============================================================================

let r_Fe = Iron.MajorityResistivity
let R_Fe = Iron.MinorityResistivity

let R_parallel = resistanceParallel r_Fe R_Fe
let R_antiparallel = resistanceAntiparallel r_Fe R_Fe
let gmr = gmrRatio r_Fe R_Fe

if not quiet then
    printfn "=================================================================="
    printfn "   Part 2: GMR in Fe/Cr/Fe Trilayer (Original Discovery)"
    printfn "=================================================================="
    printfn ""
    printfn "In 1988, Fert and Gruenberg independently discovered GMR in"
    printfn "Fe/Cr multilayers, observing ~50%% resistance change!"
    printfn ""
    printfn "Fe/Cr/Fe trilayer calculation:"
    printfn "------------------------------------------------------------------"
    printfn "  Majority spin resistivity (r):     %.1f uOhm*cm" r_Fe
    printfn "  Minority spin resistivity (R):     %.1f uOhm*cm" R_Fe
    printfn "  Spin asymmetry (alpha = R/r):      %.2f" (spinAsymmetry r_Fe R_Fe)
    printfn ""
    printfn "  Parallel resistance (R_P):         %.2f uOhm*cm" R_parallel
    printfn "  Antiparallel resistance (R_AP):    %.2f uOhm*cm" R_antiparallel
    printfn ""
    printfn "  GMR Ratio = (R_AP - R_P)/R_P:      %.1f%%" (gmr * 100.0)
    printfn ""
    let gmr_alt = gmrRatioAlt r_Fe R_Fe
    printfn "Verification with alternative formula:"
    printfn "  GMR = (R-r)^2/(4rR):                %.1f%%" (gmr_alt * 100.0)
    printfn ""

// ==============================================================================
// PART 3: COMPARISON OF FERROMAGNETIC MATERIALS
// ==============================================================================

if not quiet then
    printfn "=================================================================="
    printfn "   Part 3: GMR for Different Ferromagnetic Materials"
    printfn "=================================================================="
    printfn ""
    printfn "GMR depends on spin asymmetry - higher alpha gives larger effect:"
    printfn "------------------------------------------------------------------"
    printfn "  Material              R_P (uOhm*cm)  R_AP (uOhm*cm)  GMR (%%)"
    printfn "  --------              ------------  -------------  -------"

    for fm in ferromagnets do
        let R_P = resistanceParallel fm.MajorityResistivity fm.MinorityResistivity
        let R_AP = resistanceAntiparallel fm.MajorityResistivity fm.MinorityResistivity
        let gmrVal = gmrRatio fm.MajorityResistivity fm.MinorityResistivity
        printfn "  %-20s    %.2f          %.2f           %.1f"
                fm.Name R_P R_AP (gmrVal * 100.0)

    printfn ""
    printfn "Iron shows highest GMR due to large spin asymmetry (alpha = 3.0)"
    printfn ""

// ==============================================================================
// PART 4: RKKY COUPLING AND SPACER THICKNESS
// ==============================================================================

let spacerThicknesses = [0.5; 0.8; 1.0; 1.2; 1.5; 1.8; 2.0; 2.5; 3.0]

if not quiet then
    printfn "=================================================================="
    printfn "   Part 4: RKKY Interlayer Coupling"
    printfn "=================================================================="
    printfn ""
    printfn "The magnetic layers couple through the non-magnetic spacer via"
    printfn "RKKY (Ruderman-Kittel-Kasuya-Yosida) interaction:"
    printfn ""
    printfn "  J(d) ~ cos(2k_F * d) / d^2"
    printfn ""
    printfn "This oscillates between ferromagnetic and antiferromagnetic!"
    printfn ""
    printfn "RKKY coupling vs Cr spacer thickness:"
    printfn "------------------------------------------------------------------"
    printfn "  d (nm)    J (a.u.)    Coupling Type"
    printfn "  ------    --------    -------------"

    for d in spacerThicknesses do
        let J = rkkyCoupling Chromium d
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

// Calculate sensitivity
let fieldSensitivity = gmr * 100.0 / 50.0  // Assume 50 Oe switching field

if not quiet then
    printfn "=================================================================="
    printfn "   Part 5: GMR Device Applications"
    printfn "=================================================================="
    printfn ""
    printfn "1. HARD DRIVE READ HEADS (since 1997)"
    printfn "   - GMR sensors replaced anisotropic magnetoresistance (AMR)"
    printfn "   - Enabled ~100x increase in storage density"
    printfn "   - Sensitivity: ~1%% change per Oe"
    printfn ""
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

if not quiet then
    printfn "=================================================================="
    printfn "   Part 6: VQE Simulation of Exchange Coupling (Fe2 Dimer)"
    printfn "=================================================================="
    printfn ""
    printfn "SCIENTIFIC APPROACH:"
    printfn "  The Fe2 dimer is a LEGITIMATE benchmark for magnetic exchange coupling."
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
    printfn "Fe2 Dimer Properties:"
    printfn "------------------------------------------------------------------"
    printfn "  Fe-Fe bond length: ~2.02 A (experimental)"
    printfn "  Ground state: septet (S=3, 6 unpaired electrons)"
    printfn "  Each Fe: 3d6 4s2 configuration, ~4 unpaired d-electrons"
    printfn ""
    printfn "  This is the BUILDING BLOCK of Fe/Cr GMR physics!"
    printfn "  Exchange coupling in Fe2 -> exchange coupling in Fe layers"
    printfn ""

// ==============================================================================
// Fe2 DIMER MODEL FOR EXCHANGE COUPLING
// ==============================================================================

/// Fe-Fe bond length in Angstroms (experimental)
let feFeBondLength = 2.02

/// Create Fe2 dimer at specified spin state
///
/// The exchange coupling constant J is computed from:
/// J = E(low-spin) - E(high-spin)
///
/// For Fe2:
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

// ==============================================================================
// VQE CALCULATIONS FOR Fe2 SPIN STATES
// ==============================================================================

if not quiet then
    printfn "VQE Results for Fe2 Dimer Exchange Coupling:"
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

// High-spin (septet, S=3) - Ferromagnetically coupled
let fe2HighSpin = createFe2Dimer 7
let highSpinResult =
    runVqe
        "1. Fe2 High-Spin (Septet, M=7) - Ferromagnetic coupling"
        "Spin: S = 3 (6 unpaired electrons); Fe spins PARALLEL (ferromagnetic)"
        fe2HighSpin

// Low-spin (singlet, S=0) - Antiferromagnetically coupled
let fe2LowSpin = createFe2Dimer 1
let lowSpinResult =
    runVqe
        "2. Fe2 Low-Spin (Singlet, M=1) - Antiferromagnetic coupling"
        "Spin: S = 0 (all electrons paired); Fe spins ANTIPARALLEL (antiferromagnetic)"
        fe2LowSpin

// Triplet state for comparison
let fe2Triplet = createFe2Dimer 3
let tripletResult =
    runVqe
        "3. Fe2 Intermediate-Spin (Triplet, M=3) - Mixed coupling"
        "Spin: S = 1 (2 unpaired electrons)"
        fe2Triplet

let vqeResults = [highSpinResult; lowSpinResult; tripletResult]

// ==============================================================================
// EXCHANGE COUPLING ANALYSIS
// ==============================================================================

let exchangeRow =
    let E_highspin =
        highSpinResult
        |> Map.tryFind "energy_hartree"
        |> Option.bind (fun s -> match Double.TryParse s with true, v -> Some v | _ -> None)
    let E_lowspin =
        lowSpinResult
        |> Map.tryFind "energy_hartree"
        |> Option.bind (fun s -> match Double.TryParse s with true, v -> Some v | _ -> None)

    match E_highspin, E_lowspin with
    | Some eH, Some eL ->
        let J_Hartree = eL - eH
        let J_eV = J_Hartree * 27.2114
        let J_meV = J_eV * 1000.0
        let J_K = J_eV * 11604.5  // 1 eV = 11604.5 K
        let couplingKind = if J_Hartree > 0.0 then "Ferromagnetic" else "Antiferromagnetic"

        if not quiet then
            printfn "Exchange Coupling Analysis:"
            printfn "------------------------------------------------------------------"
            printfn "  J = E(singlet) - E(septet)"
            printfn "    = %.6f - %.6f" eL eH
            printfn "    = %.6f Hartree" J_Hartree
            printfn "    = %.3f eV = %.1f meV" J_eV J_meV
            printfn "    = %.0f K (Curie temperature scale)" J_K
            printfn ""
            if J_Hartree > 0.0 then
                printfn "  Result: J > 0 -> FERROMAGNETIC ground state"
                printfn "  (High-spin septet is lower energy)"
            else
                printfn "  Result: J < 0 -> ANTIFERROMAGNETIC ground state"
                printfn "  (Low-spin singlet is lower energy)"
            printfn ""
            printfn "  Connection to GMR:"
            printfn "  - Fe layers in Fe/Cr multilayers show this exchange physics"
            printfn "  - RKKY coupling through Cr modulates effective J"
            printfn "  - Resistance depends on relative spin alignment"
            printfn ""

        Map.ofList [
            "quantity", "exchange_coupling"
            "J_hartree", sprintf "%.6f" J_Hartree
            "J_eV", sprintf "%.3f" J_eV
            "J_meV", sprintf "%.1f" J_meV
            "J_K", sprintf "%.0f" J_K
            "coupling_type", couplingKind
        ]
    | _ ->
        if not quiet then
            printfn "Exchange Coupling Analysis:"
            printfn "------------------------------------------------------------------"
            printfn "  (Exchange coupling calculation requires both spin states)"
            printfn ""
        Map.ofList [ "quantity", "exchange_coupling"; "status", "incomplete" ]

if not quiet then
    printfn "SCIENTIFIC INTERPRETATION:"
    printfn "------------------------------------------------------------------"
    printfn "  The Fe2 dimer energy difference directly measures exchange coupling."
    printfn "  This is the SAME physics that drives GMR in Fe/Cr multilayers:"
    printfn ""
    printfn "  - Fe layer 1: All spins aligned (ferromagnetic, like high-spin Fe2)"
    printfn "  - Fe layer 2: All spins aligned (ferromagnetic)"
    printfn "  - RKKY coupling: Determines if layers are parallel or antiparallel"
    printfn "  - Resistance: Depends on relative alignment (GMR effect)"
    printfn ""
    printfn "  VQE on Fe2 gives insight into this fundamental magnetic physics!"
    printfn ""

// ==============================================================================
// PART 7: MODERN GMR STRUCTURES
// ==============================================================================

if not quiet then
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
    printfn "  Resistance        Low (Ohm)       High (kOhm)"
    printfn "  Mechanism         Spin scattering Spin tunneling"
    printfn "  Applications      HDD sensors     MRAM"
    printfn ""

// ==============================================================================
// QUANTUM ADVANTAGE ANALYSIS
// ==============================================================================

if not quiet then
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

if not quiet then
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
    printfn "  - Spin asymmetry alpha = R/r determines GMR magnitude"
    printfn "  - RKKY coupling oscillates with spacer thickness"
    printfn "  - Antiferromagnetic coupling needed for GMR"
    printfn ""
    printfn "Historical Impact:"
    printfn "  - GMR enabled >1000x increase in HDD capacity"
    printfn "  - Nobel Prize 2007: Fert & Gruenberg"
    printfn "  - Foundation for MRAM and spintronics"
    printfn ""
    printfn "=================================================================="
    printfn "  Giant Magnetoresistance: A quantum effect that changed"
    printfn "  data storage forever and launched the field of spintronics."
    printfn "=================================================================="
    printfn ""

// ==============================================================================
// SUGGESTED EXTENSIONS
// ==============================================================================

if not quiet then
    printfn "Suggested Extensions"
    printfn "------------------------------------------------------------------"
    printfn ""
    printfn "1. Spin valve structures:"
    printfn "   - Exchange-biased pinned layer"
    printfn "   - Angular dependence of resistance"
    printfn ""
    printfn "2. Tunnel magnetoresistance (TMR):"
    printfn "   - Julliere model"
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

// ==============================================================================
// STRUCTURED OUTPUT
// ==============================================================================

// Build GMR summary rows for structured output
let gmrRows =
    ferromagnets
    |> List.map (fun fm ->
        let R_P = resistanceParallel fm.MajorityResistivity fm.MinorityResistivity
        let R_AP = resistanceAntiparallel fm.MajorityResistivity fm.MinorityResistivity
        let gmrVal = gmrRatio fm.MajorityResistivity fm.MinorityResistivity
        Map.ofList [
            "material", fm.Name
            "majority_resistivity", sprintf "%.1f" fm.MajorityResistivity
            "minority_resistivity", sprintf "%.1f" fm.MinorityResistivity
            "spin_asymmetry", sprintf "%.2f" (spinAsymmetry fm.MajorityResistivity fm.MinorityResistivity)
            "spin_polarization", sprintf "%.2f" fm.SpinPolarization
            "R_parallel", sprintf "%.2f" R_P
            "R_antiparallel", sprintf "%.2f" R_AP
            "gmr_pct", sprintf "%.1f" (gmrVal * 100.0)
            "layers", sprintf "%d" numLayers
            "temperature_K", sprintf "%.0f" userTemperature
        ])

let allResultRows = gmrRows @ vqeResults @ [exchangeRow]

match outputPath with
| Some path ->
    Reporting.writeJson path allResultRows
    if not quiet then printfn "Results written to %s" path
| None -> ()

match csvPath with
| Some path ->
    let header =
        [ "material"; "majority_resistivity"; "minority_resistivity"; "spin_asymmetry";
          "spin_polarization"; "R_parallel"; "R_antiparallel"; "gmr_pct";
          "layers"; "temperature_K"; "molecule"; "label"; "energy_hartree";
          "iterations"; "time_seconds"; "status"; "quantity"; "J_hartree";
          "J_eV"; "J_meV"; "J_K"; "coupling_type" ]
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
    printfn "   dotnet fsi GMR_SpinTransport.fsx -- --help"
    printfn "   dotnet fsi GMR_SpinTransport.fsx -- --layers 5"
    printfn "   dotnet fsi GMR_SpinTransport.fsx -- --output results.json --csv results.csv"
    printfn "   dotnet fsi GMR_SpinTransport.fsx -- --quiet --output results.json  (pipeline mode)"
    printfn ""
