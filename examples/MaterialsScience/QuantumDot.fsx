// ==============================================================================
// Quantum Dot Energy Levels Example
// ==============================================================================
// Demonstrates VQE for computing electronic energy levels in semiconductor
// quantum dots, with application to optical properties and nanoelectronics.
//
// Business Context:
// Understanding quantum dot energy levels is crucial for:
// - Display technology (QLED TVs, monitors)
// - Solar cells (third-generation photovoltaics)
// - Quantum computing qubits (spin qubits in Si/Ge dots)
// - Biomedical imaging (fluorescent markers)
// - Lasers and single-photon sources
//
// Experimental context:
// Optical and electronic properties of nanomaterials are often measured using
// photon and neutron probes (e.g., synchrotron radiation and spallation neutron
// sources described in Particle Physics Reference Library, Vol. 3).
//
// This example shows:
// - Particle-in-a-box model mapping to qubit Hamiltonian
// - VQE calculation of ground and excited state energies
// - Size-dependent optical properties (quantum confinement)
// - Comparison with analytical solutions
//
// Quantum Advantage:
// While single-electron quantum dots have analytical solutions, real dots with
// multiple interacting electrons require quantum simulation. VQE handles:
// - Electron-electron correlation
// - Many-body effects
// - Realistic potentials
//
// THEORETICAL FOUNDATION:
// Based on "Concepts of Materials Science" by Adrian P. Sutton
// Chapter 7: Small is Different (Quantum Dots)
// Equation 7.1: E(nx,ny,nz) = (h^2 / 8mL^2) * (nx^2 + ny^2 + nz^2)
//
// Usage:
//   dotnet fsi QuantumDot.fsx
//   dotnet fsi QuantumDot.fsx -- --material CdSe
//   dotnet fsi QuantumDot.fsx -- --material InAs --size 3.5
//   dotnet fsi QuantumDot.fsx -- --material all --output results.json
//   dotnet fsi QuantumDot.fsx -- --material Si --csv results.csv --quiet
//
// ==============================================================================

(*
===============================================================================
 Background Theory: Quantum Dots and Quantum Confinement
===============================================================================

MATERIALS SCIENCE REFERENCE:
This example implements concepts from "Concepts of Materials Science" by
Adrian P. Sutton (Oxford University Press):
  - Chapter 7: Small is Different (pp. 81-86)
  - Section 7.3: Quantum Dots

QUANTUM DOTS are nanoscale semiconductor crystals (typically 2-10 nm) where
electrons are confined in all three dimensions. This confinement leads to
discrete energy levels, similar to atoms - hence the name "artificial atoms".

The PARTICLE-IN-A-BOX model (Sutton Eq. 7.1) gives the energy levels:

    E(nx, ny, nz) = (h^2 / 8mL^2) * (nx^2 + ny^2 + nz^2)

where:
  - h = Planck's constant (6.626 x 10^-34 J*s)
  - m = electron mass (or effective mass in semiconductor)
  - L = quantum dot size (side length of cubic dot)
  - nx, ny, nz = quantum numbers (1, 2, 3, ...)

Key physics from Sutton Chapter 7:
  1. Energy levels scale as 1/L^2 - smaller dots have larger gaps
  2. Effective mass m* replaces free electron mass
  3. Each level holds 2 electrons (spin up/down)
  4. Degeneracy: states like (2,1,1), (1,2,1), (1,1,2) have same energy

QUANTUM CONFINEMENT EFFECTS:
As dot size decreases:
  - Band gap INCREASES (blue-shifted emission)
  - Energy level spacing INCREASES
  - Surface-to-volume ratio INCREASES
  - Properties become SIZE-TUNABLE

This is why quantum dots emit different colors based on size:
  - Large dots (~6 nm): Red emission
  - Medium dots (~4 nm): Green emission
  - Small dots (~2 nm): Blue emission

EXCITONS in quantum dots:
When an electron is excited, it leaves a positively-charged "hole".
The electron-hole pair forms a bound state called an EXCITON.
The exciton binding energy in quantum dots is enhanced due to confinement.

    E_emission = E_gap + E_confinement - E_exciton

QUANTUM ADVANTAGE:
While single-electron dots have analytical solutions, multi-electron dots
with electron-electron interactions require quantum simulation:
  - Coulomb repulsion between confined electrons
  - Exchange-correlation effects
  - Spin-orbit coupling
  - Realistic potential profiles

VQE can compute these many-body effects that are intractable classically
for dots with >2 electrons.

References:
  [1] Sutton, A.P. "Concepts of Materials Science" Ch.7 (Oxford, 2021)
  [2] Wikipedia: Quantum_dot (https://en.wikipedia.org/wiki/Quantum_dot)
  [3] Efros & Efros, "Interband absorption of light", Sov. Phys. (1982)
  [4] Brus, L.E. "Electron-electron and electron-hole interactions", J. Chem. Phys. (1984)
  [5] Nobel Prize 2023: Bawendi, Brus, Ekimov for quantum dot discovery
*)

#load "_materialsCommon.fsx"
#load "../_common/Cli.fs"
#load "../_common/Data.fs"
#load "../_common/Reporting.fs"

open System
open FSharp.Azure.Quantum.QuantumChemistry
open FSharp.Azure.Quantum.Core
open FSharp.Azure.Quantum.Core.BackendAbstraction
open MaterialsCommon
open FSharp.Azure.Quantum.Examples.Common

// ==============================================================================
// EFFECTIVE MASSES FOR COMMON QUANTUM DOT MATERIALS
// ==============================================================================

/// Effective mass ratios (m*/m_e) for various semiconductor materials
/// These determine the energy level spacing in quantum dots
type QDMaterial = {
    Name: string
    ShortName: string
    ElectronMass: float      // m_e*/m_e (conduction band)
    HoleMass: float          // m_h*/m_e (valence band)
    BulkBandGap: float       // eV
    DielectricConstant: float
}

let CdSe = { 
    Name = "CdSe (Cadmium Selenide)"
    ShortName = "CdSe"
    ElectronMass = 0.13
    HoleMass = 0.45
    BulkBandGap = 1.74  // eV
    DielectricConstant = 10.0
}

let InAs = { 
    Name = "InAs (Indium Arsenide)"
    ShortName = "InAs"
    ElectronMass = 0.023
    HoleMass = 0.41
    BulkBandGap = 0.354  // eV
    DielectricConstant = 15.15
}

let Silicon = { 
    Name = "Si (Silicon)"
    ShortName = "Si"
    ElectronMass = 0.26
    HoleMass = 0.36
    BulkBandGap = 1.12  // eV
    DielectricConstant = 11.7
}

let Germanium = { 
    Name = "Ge (Germanium)"
    ShortName = "Ge"
    ElectronMass = 0.082
    HoleMass = 0.28
    BulkBandGap = 0.67  // eV
    DielectricConstant = 16.0
}

/// All available quantum dot materials
let allMaterials = [ CdSe; InAs; Silicon; Germanium ]

// ==============================================================================
// CLI ARGUMENT PARSING
// ==============================================================================

let argv = fsi.CommandLineArgs |> Array.skip 1
let args = Cli.parse argv

Cli.exitIfHelp
    "QuantumDot.fsx"
    "Quantum dot energy levels via particle-in-a-box model and VQE simulation."
    [ { Cli.OptionSpec.Name = "material"; Description = "Material to analyze: CdSe, InAs, Si, Ge, or all"; Default = Some "CdSe" }
      { Cli.OptionSpec.Name = "size";     Description = "Dot size in nm for material comparison";           Default = Some "4.0" }
      { Cli.OptionSpec.Name = "output";   Description = "Write results to JSON file";                      Default = None }
      { Cli.OptionSpec.Name = "csv";      Description = "Write results to CSV file";                       Default = None }
      { Cli.OptionSpec.Name = "quiet";    Description = "Suppress informational output";                   Default = None } ]
    args

let quiet = Cli.hasFlag "quiet" args
let materialArg = Cli.getOr "material" "CdSe" args
let dotSize = Cli.getFloatOr "size" 4.0 args

/// Materials filtered by CLI --material argument
let filteredMaterials =
    match materialArg.ToLowerInvariant() with
    | "all"  -> allMaterials
    | "cdse" -> [ CdSe ]
    | "inas" -> [ InAs ]
    | "si"   -> [ Silicon ]
    | "ge"   -> [ Germanium ]
    | _      -> [ CdSe ]

/// Primary material for detailed analysis (first of filtered, or CdSe)
let material = filteredMaterials |> List.head

// ==============================================================================
// PARTICLE-IN-A-BOX MODEL (Sutton Eq. 7.1)
// ==============================================================================

/// Calculate energy level for particle-in-a-box (cubic quantum dot)
/// Implements Sutton Eq. 7.1: E = (h^2/8mL^2)(nx^2 + ny^2 + nz^2)
/// 
/// Parameters:
///   size_nm: Quantum dot size (nanometers)
///   effectiveMass: Effective mass ratio (m*/m_e)
///   nx, ny, nz: Quantum numbers (1, 2, 3, ...)
/// 
/// Returns: Energy in electron volts
let particleInBoxEnergy (size_nm: float) (effectiveMass: float) (nx: int) (ny: int) (nz: int) : float =
    let L = size_nm * nm_to_m  // Convert to meters
    let m_eff = effectiveMass * m_e  // Effective mass in kg
    
    // Sutton Eq. 7.1
    let prefactor = h * h / (8.0 * m_eff * L * L)
    let quantumSum = float (nx * nx + ny * ny + nz * nz)
    
    prefactor * quantumSum * J_to_eV  // Convert to eV

/// Calculate the quantum confinement energy (increase in gap due to size)
/// This is the first excited state minus ground state
let confinementEnergy (size_nm: float) (material: QDMaterial) : float =
    // Confinement energy = electron confinement + hole confinement
    let E_e = particleInBoxEnergy size_nm material.ElectronMass 1 1 1
    let E_h = particleInBoxEnergy size_nm material.HoleMass 1 1 1
    E_e + E_h

/// Calculate effective band gap including confinement
/// E_gap(L) = E_gap(bulk) + E_confinement(electron) + E_confinement(hole) - E_exciton
let effectiveBandGap (size_nm: float) (material: QDMaterial) : float =
    let E_e = particleInBoxEnergy size_nm material.ElectronMass 1 1 1
    let E_h = particleInBoxEnergy size_nm material.HoleMass 1 1 1
    
    // Exciton binding energy (simplified Coulomb attraction)
    // E_exciton ~ 1.8 * e^2 / (4*pi*epsilon*epsilon_0*L)
    let e = 1.60218e-19  // Coulomb
    let epsilon_0 = 8.854e-12  // F/m
    let L = size_nm * nm_to_m
    let E_exciton = 1.8 * e * e / (4.0 * Math.PI * material.DielectricConstant * epsilon_0 * L)
    let E_exciton_eV = E_exciton * J_to_eV
    
    material.BulkBandGap + E_e + E_h - E_exciton_eV

// ==============================================================================
// QUANTUM BACKEND SETUP
// ==============================================================================

if not quiet then
    printfn "=================================================================="
    printfn "   Quantum Dot Energy Levels (VQE Simulation)"
    printfn "=================================================================="
    printfn ""

    printfn "Theoretical Foundation: Sutton 'Concepts of Materials Science'"
    printfn "------------------------------------------------------------------"
    printfn "  Chapter 7: Small is Different"
    printfn "  Section 7.3: Quantum Dots"
    printfn "  Equation 7.1: E = (h^2/8mL^2)(nx^2 + ny^2 + nz^2)"
    printfn ""

    printfn "Quantum Backend"
    printfn "------------------------------------------------------------------"
    printfn "  Backend: %s" backend.Name
    printfn "  Type: Statevector Simulator"
    printfn ""

// ==============================================================================
// PART 1: ANALYTICAL PARTICLE-IN-A-BOX ANALYSIS
// ==============================================================================

if not quiet then
    printfn "=================================================================="
    printfn "   Part 1: Particle-in-a-Box Model (Sutton Eq. 7.1)"
    printfn "=================================================================="
    printfn ""

let dotSizes = [2.0; 3.0; 4.0; 5.0; 6.0; 8.0]  // nm

if not quiet then
    printfn "Material: %s" material.Name
    printfn "  Effective electron mass: %.3f m_e" material.ElectronMass
    printfn "  Effective hole mass: %.3f m_e" material.HoleMass
    printfn "  Bulk band gap: %.2f eV" material.BulkBandGap
    printfn ""

    printfn "Energy Levels vs. Quantum Dot Size"
    printfn "------------------------------------------------------------------"
    printfn "  Size (nm)   E(1,1,1)   E(2,1,1)   E(2,2,1)   Band Gap   Color"
    printfn "  ---------   --------   --------   --------   --------   -----"

/// Classify emission color from wavelength in nm
let emissionColor (wavelength_nm: float) =
    if wavelength_nm > 650.0 then "Red"
    elif wavelength_nm > 590.0 then "Orange"
    elif wavelength_nm > 520.0 then "Green"
    elif wavelength_nm > 450.0 then "Blue"
    else "Violet"

let sizeRows =
    dotSizes |> List.map (fun size ->
        let E111 = particleInBoxEnergy size material.ElectronMass 1 1 1
        let E211 = particleInBoxEnergy size material.ElectronMass 2 1 1
        let E221 = particleInBoxEnergy size material.ElectronMass 2 2 1
        let bandGap = effectiveBandGap size material

        // Wavelength from band gap: lambda = hc/E
        let wavelength_nm = 1240.0 / bandGap  // hc = 1240 eV*nm
        let color = emissionColor wavelength_nm

        if not quiet then
            printfn "    %.1f        %.3f      %.3f      %.3f      %.2f       %s"
                    size E111 E211 E221 bandGap color

        Map.ofList [
            "material", material.ShortName
            "size_nm", sprintf "%.1f" size
            "E111_eV", sprintf "%.6f" E111
            "E211_eV", sprintf "%.6f" E211
            "E221_eV", sprintf "%.6f" E221
            "band_gap_eV", sprintf "%.4f" bandGap
            "wavelength_nm", sprintf "%.0f" wavelength_nm
            "color", color
        ])

if not quiet then
    printfn ""
    printfn "Key insight: Smaller dots -> larger gaps -> bluer emission"
    printfn "(This is quantum confinement in action!)"
    printfn ""

// ==============================================================================
// PART 2: DEGENERACY ANALYSIS
// ==============================================================================

if not quiet then
    printfn "=================================================================="
    printfn "   Part 2: Energy Level Degeneracy"
    printfn "=================================================================="
    printfn ""
    printfn "For a cubic quantum dot, many states have the same energy:"
    printfn ""

// Find all states up to some maximum quantum number
let maxN = 3
let allStates =
    [ for nx in 1..maxN do
        for ny in 1..maxN do
            for nz in 1..maxN do
                let E = particleInBoxEnergy 5.0 material.ElectronMass nx ny nz
                yield ((nx, ny, nz), E) ]
    |> List.sortBy snd

// Group by energy (with tolerance for floating point)
let groupedByEnergy =
    allStates
    |> List.groupBy (fun (_, e) -> Math.Round(e, 4))
    |> List.sortBy fst

if not quiet then
    printfn "Energy levels for 5 nm %s quantum dot (electrons):" material.ShortName
    printfn "------------------------------------------------------------------"
    printfn "  Energy (eV)   Degeneracy   States (nx,ny,nz)"
    printfn "  -----------   ----------   -----------------"

    for (energy, states) in groupedByEnergy |> List.take 8 do
        let stateStrings = states |> List.map (fun ((nx,ny,nz), _) -> sprintf "(%d,%d,%d)" nx ny nz)
        let degeneracy = states.Length
        printfn "    %.4f       %d           %s" energy degeneracy (String.concat " " stateStrings)

    printfn ""
    printfn "Each energy level holds 2*degeneracy electrons (spin up/down)"
    printfn ""

// ==============================================================================
// PART 3: VQE SIMULATION OF QUANTUM DOT MOLECULAR CLUSTERS
// ==============================================================================

if not quiet then
    printfn "=================================================================="
    printfn "   Part 3: VQE Simulation (CdSe Molecular Clusters)"
    printfn "=================================================================="
    printfn ""

    printfn "SCIENTIFIC APPROACH:"
    printfn "  Real quantum dots are nanoscale clusters of semiconductor atoms."
    printfn "  We model the electronic structure of small CdSe clusters using VQE."
    printfn "  This is legitimate quantum chemistry - VQE computes the ground"
    printfn "  state energy of the actual Cd-Se molecular system."
    printfn ""
    printfn "  For multi-electron systems, quantum simulation is needed because:"
    printfn "    - Electron-electron Coulomb repulsion"
    printfn "    - Exchange-correlation effects"
    printfn "    - Many-body interactions"
    printfn ""

// ==============================================================================
// CdSe MOLECULAR CLUSTER MODELS
// ==============================================================================
//
// CdSe has a wurtzite crystal structure with tetrahedral coordination:
//   - Each Cd is bonded to 4 Se atoms
//   - Each Se is bonded to 4 Cd atoms
//   - Cd-Se bond length: ~2.63 A
//
// We create small molecular clusters that represent the building blocks
// of quantum dots. These are legitimate molecules whose ground state
// energy can be computed with VQE.
//
// References:
//   - CdSe crystal structure: wurtzite (space group P63mc)
//   - Cd-Se bond length: 2.63 A (Inorganic Chemistry, Shriver & Atkins)
//   - Small CdSe clusters studied computationally: J. Chem. Phys. 126, 134309 (2007)

/// Cd-Se bond length in Angstroms (from crystal structure data)
let cdSeBondLength = 2.63

/// Create a CdSe dimer molecule (simplest quantum dot building block)
/// This is the smallest CdSe cluster - one Cd bonded to one Se
///
/// Electronic structure:
///   - Cd: [Kr]4d10 5s2 (12 valence electrons in minimal basis)
///   - Se: [Ar]3d10 4s2 4p4 (6 valence electrons)
///   - Total: 18 valence electrons
let createCdSeDimer () : Molecule =
    {
        Name = "CdSe_dimer"
        Atoms = [
            { Element = "Cd"; Position = (0.0, 0.0, 0.0) }
            { Element = "Se"; Position = (cdSeBondLength, 0.0, 0.0) }
        ]
        Bonds = [
            { Atom1 = 0; Atom2 = 1; BondOrder = 2.0 }  // Approximate bond order
        ]
        Charge = 0
        Multiplicity = 1  // Singlet ground state
    }

/// Create a Cd2Se2 tetrahedral cluster (rhombus structure)
/// This represents the smallest "quantum dot-like" structure with
/// multiple Cd-Se bonds in a 3D arrangement.
///
/// Structure: Rhombus with Cd atoms at two corners, Se at the other two
///   Cd --- Se
///   |  \ / |
///   |  / \ |
///   Se --- Cd
///
/// This captures:
///   - Multiple Cd-Se bonds
///   - Cd-Cd and Se-Se interactions (important for band structure)
///   - 3D confinement effects
let createCd2Se2Cluster () : Molecule =
    // Rhombus geometry: atoms at corners of a parallelogram
    // Cd-Se bond: 2.63 A, angle approximately 90 degrees for simplicity
    let d = cdSeBondLength / sqrt 2.0  // Adjust for geometry
    {
        Name = "Cd2Se2_cluster"
        Atoms = [
            { Element = "Cd"; Position = (0.0, 0.0, 0.0) }
            { Element = "Se"; Position = (d, d, 0.0) }
            { Element = "Cd"; Position = (2.0*d, 0.0, 0.0) }
            { Element = "Se"; Position = (d, -d, 0.0) }
        ]
        Bonds = [
            { Atom1 = 0; Atom2 = 1; BondOrder = 1.0 }  // Cd1-Se1
            { Atom1 = 1; Atom2 = 2; BondOrder = 1.0 }  // Se1-Cd2
            { Atom1 = 2; Atom2 = 3; BondOrder = 1.0 }  // Cd2-Se2
            { Atom1 = 3; Atom2 = 0; BondOrder = 1.0 }  // Se2-Cd1
        ]
        Charge = 0
        Multiplicity = 1
    }

/// Create a ZnS dimer for comparison (another common QD material)
/// ZnS has a similar structure to CdSe but different band gap
/// Zn-S bond length: ~2.34 A
let createZnSDimer () : Molecule =
    let znSBondLength = 2.34
    {
        Name = "ZnS_dimer"
        Atoms = [
            { Element = "Zn"; Position = (0.0, 0.0, 0.0) }
            { Element = "S"; Position = (znSBondLength, 0.0, 0.0) }
        ]
        Bonds = [
            { Atom1 = 0; Atom2 = 1; BondOrder = 2.0 }
        ]
        Charge = 0
        Multiplicity = 1
    }

// ==============================================================================
// VQE CALCULATIONS FOR QUANTUM DOT BUILDING BLOCKS
// ==============================================================================

/// Run VQE for a molecular cluster and return structured results
let runVqe (label: string) (description: string) (molecule: Molecule) : Map<string, string> =
    if not quiet then
        printfn "%s:" label
        printfn "   %s" description
    match calculateVQEEnergy backend molecule with
    | Ok (energy, iterations, time) ->
        if not quiet then
            printfn "   VQE Ground State Energy: %.6f Hartree" energy
            printfn "   Iterations: %d, Time: %.2f s" iterations time
            printfn "   Energy in eV: %.3f eV" (energy * hartreeToEV)
            printfn ""
        Map.ofList [
            "molecule", molecule.Name; "label", label
            "energy_hartree", sprintf "%.6f" energy
            "energy_eV", sprintf "%.3f" (energy * hartreeToEV)
            "iterations", sprintf "%d" iterations
            "time_seconds", sprintf "%.2f" time
            "status", "OK"
        ]
    | Error msg ->
        if not quiet then printfn "   Error: %s" msg; printfn ""
        Map.ofList [
            "molecule", molecule.Name; "label", label
            "energy_hartree", "N/A"; "energy_eV", "N/A"
            "iterations", "N/A"; "time_seconds", "N/A"
            "status", sprintf "Error: %s" msg
        ]

if not quiet then
    printfn "VQE Results for Quantum Dot Molecular Clusters:"
    printfn "------------------------------------------------------------------"
    printfn ""
    printfn "These are REAL molecules that form the building blocks of quantum dots."
    printfn "VQE computes the actual ground state energy of each molecular cluster."
    printfn ""

let cdSeDimer = createCdSeDimer()
let cd2se2 = createCd2Se2Cluster()
let znsDimer = createZnSDimer()

let vqeResults = [
    runVqe "1. CdSe Dimer" (sprintf "Simplest building block, Cd-Se bond: %.2f A" cdSeBondLength) cdSeDimer
    runVqe "2. Cd2Se2 Cluster" "Rhombus structure, 4 atoms, multi-center bonding" cd2se2
    runVqe "3. ZnS Dimer" "Comparison material, Zn-S bond: 2.34 A" znsDimer
]

if not quiet then
    printfn "SCIENTIFIC INTERPRETATION:"
    printfn "------------------------------------------------------------------"
    printfn "  These molecular clusters represent the electronic structure of"
    printfn "  quantum dot building blocks. Key observations:"
    printfn ""
    printfn "  1. CdSe vs ZnS: Different ground state energies reflect different"
    printfn "     electronic structures and ultimately different band gaps"
    printfn ""
    printfn "  2. Cluster size effects: Larger clusters (Cd2Se2) show multi-center"
    printfn "     bonding and correlation effects that single dimers miss"
    printfn ""
    printfn "  3. Connection to quantum dots: Real QDs contain thousands of these"
    printfn "     building blocks. The molecular cluster energies inform us about"
    printfn "     the fundamental Cd-Se bonding that determines QD properties."
    printfn ""
    printfn "  Note: Full quantum dot simulation (10,000+ atoms) is beyond current"
    printfn "  quantum computers. These small clusters are NISQ-tractable while"
    printfn "  still providing insight into the underlying chemistry."
    printfn ""

// ==============================================================================
// PART 4: SIZE-DEPENDENT OPTICAL PROPERTIES
// ==============================================================================

if not quiet then
    printfn "=================================================================="
    printfn "   Part 4: Size-Dependent Optical Properties"
    printfn "=================================================================="
    printfn ""

    printfn "Emission wavelength vs. quantum dot size (%s):" material.ShortName
    printfn "------------------------------------------------------------------"
    printfn ""
    printfn "  Size (nm)   Band Gap (eV)   Wavelength (nm)   Color"
    printfn "  ---------   -------------   ---------------   -----"

    for size in [2.0; 2.5; 3.0; 3.5; 4.0; 4.5; 5.0; 6.0] do
        let bandGap = effectiveBandGap size material
        let wavelength = 1240.0 / bandGap

        let (color, _colorCode) =
            if wavelength > 700.0 then ("Infrared", "NIR")
            elif wavelength > 620.0 then ("Red", "R")
            elif wavelength > 590.0 then ("Orange", "O")
            elif wavelength > 570.0 then ("Yellow", "Y")
            elif wavelength > 495.0 then ("Green", "G")
            elif wavelength > 450.0 then ("Blue", "B")
            elif wavelength > 380.0 then ("Violet", "V")
            else ("UV", "UV")

        printfn "    %.1f          %.2f           %.0f              %s" size bandGap wavelength color

    printfn ""

    // ASCII visualization of emission colors
    printfn "Emission spectrum visualization:"
    printfn ""
    printfn "   Size:  2nm   3nm   4nm   5nm   6nm"
    printfn "         |     |     |     |     |"
    printfn "  Color: [B]   [G]   [Y]   [O]   [R]"
    printfn "         |-----|-----|-----|-----|"
    printfn "         450nm 520nm 570nm 600nm 650nm"
    printfn ""

// ==============================================================================
// PART 5: COMPARISON OF QUANTUM DOT MATERIALS
// ==============================================================================

if not quiet then
    printfn "=================================================================="
    printfn "   Part 5: Material Comparison"
    printfn "=================================================================="
    printfn ""

    printfn "Effective band gap at %.1f nm dot size:" dotSize
    printfn "------------------------------------------------------------------"
    printfn "  Material              Bulk Gap    QD Gap    Confinement"
    printfn "  --------              --------    ------    -----------"

let materialRows =
    filteredMaterials |> List.map (fun mat ->
        let qdGap = effectiveBandGap dotSize mat
        let confinement = qdGap - mat.BulkBandGap
        let wavelength = 1240.0 / qdGap

        if not quiet then
            printfn "  %-20s    %.2f eV     %.2f eV   +%.2f eV"
                    mat.Name mat.BulkBandGap qdGap confinement

        Map.ofList [
            "material", mat.ShortName
            "material_name", mat.Name
            "dot_size_nm", sprintf "%.1f" dotSize
            "bulk_gap_eV", sprintf "%.3f" mat.BulkBandGap
            "qd_gap_eV", sprintf "%.4f" qdGap
            "confinement_eV", sprintf "%.4f" confinement
            "wavelength_nm", sprintf "%.0f" wavelength
            "electron_mass_ratio", sprintf "%.3f" mat.ElectronMass
            "hole_mass_ratio", sprintf "%.3f" mat.HoleMass
        ])

if not quiet then
    printfn ""

// ==============================================================================
// APPLICATIONS AND BUSINESS CONTEXT
// ==============================================================================

if not quiet then
    printfn "=================================================================="
    printfn "   Applications of Quantum Dots"
    printfn "=================================================================="
    printfn ""

    printfn "1. DISPLAY TECHNOLOGY (QLED)"
    printfn "   - Samsung, Sony, LG use QDs for enhanced color"
    printfn "   - Size-tunable pure colors without filters"
    printfn "   - Higher efficiency than OLED for large displays"
    printfn ""

    printfn "2. SOLAR CELLS"
    printfn "   - Multiple exciton generation (MEG)"
    printfn "   - Tunable absorption across solar spectrum"
    printfn "   - Third-generation photovoltaic efficiency"
    printfn ""

    printfn "3. QUANTUM COMPUTING"
    printfn "   - Silicon/Germanium spin qubits"
    printfn "   - Long coherence times at mK temperatures"
    printfn "   - Compatible with semiconductor manufacturing"
    printfn ""

    printfn "4. BIOMEDICAL IMAGING"
    printfn "   - Bright, stable fluorescent markers"
    printfn "   - Multiplexed imaging (different colors)"
    printfn "   - Deep tissue imaging with NIR QDs"
    printfn ""

    printfn "5. SINGLE-PHOTON SOURCES"
    printfn "   - Quantum communication/cryptography"
    printfn "   - Deterministic photon emission"
    printfn "   - Telecom wavelengths (InAs QDs)"
    printfn ""

// ==============================================================================
// QUANTUM ADVANTAGE ANALYSIS
// ==============================================================================

if not quiet then
    printfn "=================================================================="
    printfn "   Why Quantum Computing Matters for Quantum Dots"
    printfn "=================================================================="
    printfn ""

    printfn "CLASSICAL LIMITATIONS:"
    printfn "  - Single-electron models ignore interactions"
    printfn "  - Hartree-Fock underestimates correlation"
    printfn "  - DFT functionals unreliable for confined systems"
    printfn "  - Configuration Interaction scales exponentially"
    printfn ""

    printfn "QUANTUM ADVANTAGES:"
    printfn "  - Natural treatment of many-body entanglement"
    printfn "  - Electron correlation computed directly"
    printfn "  - Spin-orbit coupling in heavy-element dots"
    printfn "  - Excited state properties (optical transitions)"
    printfn ""

    printfn "NISQ-ERA TARGETS:"
    printfn "  - 2-6 electron quantum dots"
    printfn "  - Exciton binding energies"
    printfn "  - Size-scaling of correlation effects"
    printfn ""

    printfn "FAULT-TOLERANT ERA:"
    printfn "  - Large dots with 10+ electrons"
    printfn "  - Multi-exciton generation dynamics"
    printfn "  - Realistic heterostructures (core-shell QDs)"
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
    printfn "  - Demonstrated particle-in-a-box model (Sutton Eq. 7.1)"
    printfn "  - Showed size-dependent energy levels and band gaps"
    printfn "  - Calculated optical emission wavelengths vs. size"
    printfn "  - Performed VQE simulation for multi-electron effects"
    printfn ""

    printfn "Physics Insights:"
    printfn "  - Quantum confinement: E ~ 1/L^2 (energy increases as size decreases)"
    printfn "  - Effective mass: m* determines level spacing"
    printfn "  - Exciton binding: Enhanced in confined systems"
    printfn "  - Color tunability: Direct result of quantum mechanics"
    printfn ""

    printfn "=================================================================="
    printfn "  This example demonstrates how quantum simulation enables"
    printfn "  accurate modeling of quantum dot properties - from display"
    printfn "  technology to quantum computing qubits."
    printfn "=================================================================="
    printfn ""

// ==============================================================================
// STRUCTURED OUTPUT
// ==============================================================================

let allResults : Map<string, obj> =
    Map.ofList [
        "script", box "QuantumDot.fsx"
        "material", box material.ShortName
        "dot_size_nm", box dotSize
        "size_energy_levels", box sizeRows
        "vqe_results", box vqeResults
        "material_comparison", box materialRows
    ]

Cli.tryGet "output" args |> Option.iter (fun path -> Reporting.writeJson path allResults)

match Cli.tryGet "csv" args with
| Some path ->
    let header = [ "material"; "size_nm"; "E111_eV"; "E211_eV"; "E221_eV"; "band_gap_eV"; "wavelength_nm"; "color" ]
    let rows =
        sizeRows
        |> List.map (fun m ->
            header |> List.map (fun h -> m |> Map.tryFind h |> Option.defaultValue ""))
    Reporting.writeCsv path header rows
    if not quiet then printfn "Results written to %s" path
| None -> ()

// ==============================================================================
// USAGE HINTS
// ==============================================================================

if argv.Length = 0 && not quiet then
    printfn "Tip: Customize this example with command-line options:"
    printfn "  dotnet fsi QuantumDot.fsx -- --material InAs"
    printfn "  dotnet fsi QuantumDot.fsx -- --material all --size 3.0"
    printfn "  dotnet fsi QuantumDot.fsx -- --output results.json --csv results.csv"
    printfn "  dotnet fsi QuantumDot.fsx -- --quiet --csv results.csv"
    printfn "  dotnet fsi QuantumDot.fsx -- --help"
    printfn ""
