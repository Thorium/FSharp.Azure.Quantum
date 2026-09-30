namespace FSharp.Azure.Quantum.QuantumChemistry

open System
open System.IO
open System.Numerics
open System.Threading
open FSharp.Azure.Quantum.Core
open FSharp.Azure.Quantum // For ErrorMitigationStrategy
open FSharp.Azure.Quantum.Data // For PeriodicTable and ChemistryDataProviders

/// Quantum Chemistry - Molecule Representation and Ground State Energy Estimation.
/// Implements VQE (Variational Quantum Eigensolver) for molecular ground state energies.

// ============================================================================
// UNITS OF MEASURE
// ============================================================================

/// Angstroms (Å) - unit of atomic distance
[<Measure>]
type angstrom

/// Hartree - atomic unit of energy
[<Measure>]
type hartree

/// Electron volts
[<Measure>]
type eV

// ============================================================================
// ATOMIC DATA
// ============================================================================

/// Atomic numbers and data for elements used in quantum chemistry
///
/// Coverage: Elements 1-54 (H through Xe) plus common heavier elements
/// This covers all elements commonly encountered in:
/// - Organic chemistry (C, H, N, O, S, P, halogens)
/// - Materials science (Si, Fe, Co, Ni, Cu, Zn, Cd, Se)
/// - Photosynthesis (Mg in chlorophyll, Mn in water-splitting)
/// - Catalysis (transition metals)
/// - Quantum dots (Cd, Se, Zn, S, Pb, Te)
module AtomicNumbers =
    // Period 1
    /// Hydrogen
    [<Literal>]
    let H = 1

    /// Helium
    [<Literal>]
    let He = 2

    // Period 2
    /// Lithium
    [<Literal>]
    let Li = 3

    /// Beryllium
    [<Literal>]
    let Be = 4

    /// Boron
    [<Literal>]
    let B = 5

    /// Carbon
    [<Literal>]
    let C = 6

    /// Nitrogen
    [<Literal>]
    let N = 7

    /// Oxygen
    [<Literal>]
    let O = 8

    /// Fluorine
    [<Literal>]
    let F = 9

    /// Neon
    [<Literal>]
    let Ne = 10

    // Period 3
    /// Sodium
    [<Literal>]
    let Na = 11

    /// Magnesium
    [<Literal>]
    let Mg = 12

    /// Aluminum
    [<Literal>]
    let Al = 13

    /// Silicon
    [<Literal>]
    let Si = 14

    /// Phosphorus
    [<Literal>]
    let P = 15

    /// Sulfur
    [<Literal>]
    let S = 16

    /// Chlorine
    [<Literal>]
    let Cl = 17

    /// Argon
    [<Literal>]
    let Ar = 18

    // Period 4 (includes first-row transition metals)
    /// Potassium
    [<Literal>]
    let K = 19

    /// Calcium
    [<Literal>]
    let Ca = 20

    /// Scandium
    [<Literal>]
    let Sc = 21

    /// Titanium
    [<Literal>]
    let Ti = 22

    /// Vanadium
    [<Literal>]
    let V = 23

    /// Chromium
    [<Literal>]
    let Cr = 24

    /// Manganese
    [<Literal>]
    let Mn = 25

    /// Iron
    [<Literal>]
    let Fe = 26

    /// Cobalt
    [<Literal>]
    let Co = 27

    /// Nickel
    [<Literal>]
    let Ni = 28

    /// Copper
    [<Literal>]
    let Cu = 29

    /// Zinc
    [<Literal>]
    let Zn = 30

    /// Gallium
    [<Literal>]
    let Ga = 31

    /// Germanium
    [<Literal>]
    let Ge = 32

    /// Arsenic
    [<Literal>]
    let As = 33

    /// Selenium
    [<Literal>]
    let Se = 34

    /// Bromine
    [<Literal>]
    let Br = 35

    /// Krypton
    [<Literal>]
    let Kr = 36

    // Period 5 (includes second-row transition metals)
    /// Rubidium
    [<Literal>]
    let Rb = 37

    /// Strontium
    [<Literal>]
    let Sr = 38

    /// Yttrium
    [<Literal>]
    let Y = 39

    /// Zirconium
    [<Literal>]
    let Zr = 40

    /// Niobium
    [<Literal>]
    let Nb = 41

    /// Molybdenum
    [<Literal>]
    let Mo = 42

    /// Technetium
    [<Literal>]
    let Tc = 43

    /// Ruthenium
    [<Literal>]
    let Ru = 44

    /// Rhodium
    [<Literal>]
    let Rh = 45

    /// Palladium
    [<Literal>]
    let Pd = 46

    /// Silver
    [<Literal>]
    let Ag = 47

    /// Cadmium
    [<Literal>]
    let Cd = 48

    /// Indium
    [<Literal>]
    let In = 49

    /// Tin
    [<Literal>]
    let Sn = 50

    /// Antimony
    [<Literal>]
    let Sb = 51

    /// Tellurium
    [<Literal>]
    let Te = 52

    /// Iodine
    [<Literal>]
    let I = 53

    /// Xenon
    [<Literal>]
    let Xe = 54

    // Selected heavier elements (commonly used)
    /// Platinum (catalysis)
    [<Literal>]
    let Pt = 78

    /// Gold (nanoparticles)
    [<Literal>]
    let Au = 79

    /// Lead (quantum dots, perovskites)
    [<Literal>]
    let Pb = 82

    /// Get atomic number from element symbol
    /// Returns None for unsupported elements
    /// NOTE: Now delegates to PeriodicTable for complete element coverage.
    let fromSymbol (element: string) : int option =
        PeriodicTable.tryBySymbol element |> Option.map (fun e -> e.AtomicNumber)

    /// Get element symbol from atomic number
    /// Returns None for unsupported atomic numbers
    /// NOTE: Now delegates to PeriodicTable for complete element coverage.
    let toSymbol (atomicNumber: int) : string option =
        PeriodicTable.tryByNumber atomicNumber |> Option.map (fun e -> e.Symbol)

// ============================================================================
// MOLECULE REPRESENTATION
// ============================================================================

/// Atom in 3D space
type Atom =
    {
        /// Element symbol (H, C, N, O, etc.)
        Element: string

        /// Position in 3D space (x, y, z) in Angstroms
        Position: float * float * float
    }

/// Bond between two atoms
[<Struct>]
type Bond =
    {
        /// Index of first atom (0-based)
        Atom1: int

        /// Index of second atom (0-based)
        Atom2: int

        /// Bond order: 1.0 = single, 2.0 = double, 3.0 = triple
        BondOrder: float
    }

/// Molecular structure
type Molecule =
    {
        /// Molecule name (e.g., "H2", "H2O")
        Name: string

        /// List of atoms
        Atoms: Atom list

        /// List of bonds
        Bonds: Bond list

        /// Net charge (0 for neutral, +1 for cation, -1 for anion)
        Charge: int

        /// Spin multiplicity (2S + 1, where S is total spin)
        /// Singlet = 1, Doublet = 2, Triplet = 3
        Multiplicity: int
    }

/// Molecule operations
module Molecule =

    /// Validate molecule structure
    let validate (molecule: Molecule) : Result<unit, QuantumError> =
        // Check all bonds reference valid atoms
        let invalidBonds =
            molecule.Bonds
            |> List.filter (fun bond ->
                bond.Atom1 < 0
                || bond.Atom1 >= molecule.Atoms.Length
                || bond.Atom2 < 0
                || bond.Atom2 >= molecule.Atoms.Length)

        if not invalidBonds.IsEmpty then
            Error(QuantumError.ValidationError("Bonds", $"Bond references non-existent atom indices: %A{invalidBonds}"))
        else
            Ok()

    /// Calculate distance between two atoms (Euclidean distance in 3D)
    let calculateBondLength (atom1: Atom) (atom2: Atom) : float =
        let (x1, y1, z1) = atom1.Position
        let (x2, y2, z2) = atom2.Position

        let dx = x2 - x1
        let dy = y2 - y1
        let dz = z2 - z1

        sqrt (dx * dx + dy * dy + dz * dz)

    /// Count total number of electrons in molecule
    let countElectrons (molecule: Molecule) : int =
        let nuclearElectrons =
            molecule.Atoms
            |> List.sumBy (fun atom -> AtomicNumbers.fromSymbol atom.Element |> Option.defaultValue 0)

        // Subtract charge (positive charge = fewer electrons)
        nuclearElectrons - molecule.Charge

    /// Bohr radii per Ångström (CODATA 2018: a₀ = 0.529177210903 Å).
    [<Literal>]
    let BohrPerAngstrom = 1.8897261246257702

    /// Nuclear repulsion energy Σ_{i<j} Z_i Z_j / r_ij in Hartree, r_ij in bohr.
    /// Unknown elements and coincident nuclei are Errors.
    let nuclearRepulsion (molecule: Molecule) : Result<float, QuantumError> =
        match
            molecule.Atoms
            |> List.tryFind (fun atom -> (AtomicNumbers.fromSymbol atom.Element).IsNone)
        with
        | Some atom ->
            Error(QuantumError.ValidationError("Molecule", $"Unknown element '{atom.Element}' in '{molecule.Name}'"))
        | None ->
            let nuclei =
                molecule.Atoms
                |> List.map (fun atom -> atom, float (AtomicNumbers.fromSymbol atom.Element).Value)
                |> Array.ofList

            let pairs =
                [
                    for i in 0 .. nuclei.Length - 2 do
                        for j in i + 1 .. nuclei.Length - 1 -> nuclei.[i], nuclei.[j]
                ]

            match pairs |> List.tryFind (fun ((a, _), (b, _)) -> calculateBondLength a b < 1e-8) with
            | Some _ -> Error(QuantumError.ValidationError("Molecule", $"Coincident nuclei in '{molecule.Name}'"))
            | None ->
                pairs
                |> List.sumBy (fun ((a, za), (b, zb)) -> za * zb / (calculateBondLength a b * BohrPerAngstrom))
                |> Ok

    /// Create H2 molecule at specified bond length
    let createH2 (bondLength: float) : Molecule =
        {
            Name = "H2"
            Atoms =
                [
                    {
                        Element = "H"
                        Position = (0.0, 0.0, 0.0)
                    }
                    {
                        Element = "H"
                        Position = (0.0, 0.0, bondLength)
                    }
                ]
            Bonds =
                [
                    {
                        Atom1 = 0
                        Atom2 = 1
                        BondOrder = 1.0
                    }
                ]
            Charge = 0
            Multiplicity = 1 // Singlet (all spins paired)
        }

    /// Create H2O molecule (water) at equilibrium geometry
    let createH2O () : Molecule =
        // Equilibrium geometry: O-H bond length = 0.957 Å, H-O-H angle = 104.5°
        let ohBondLength = 0.957
        let angleRad = 104.5 * Math.PI / 180.0
        let halfAngle = angleRad / 2.0

        {
            Name = "H2O"
            Atoms =
                [
                    {
                        Element = "O"
                        Position = (0.0, 0.0, 0.0)
                    }
                    {
                        Element = "H"
                        Position = (0.0, ohBondLength * sin halfAngle, ohBondLength * cos halfAngle)
                    }
                    {
                        Element = "H"
                        Position = (0.0, -ohBondLength * sin halfAngle, ohBondLength * cos halfAngle)
                    }
                ]
            Bonds =
                [
                    {
                        Atom1 = 0
                        Atom2 = 1
                        BondOrder = 1.0
                    } // O-H
                    {
                        Atom1 = 0
                        Atom2 = 2
                        BondOrder = 1.0
                    } // O-H
                ]
            Charge = 0
            Multiplicity = 1 // Singlet
        }

    // ========================================================================
    // MATERIALS SCIENCE MOLECULES
    // ========================================================================

    /// Create LiH molecule (lithium hydride) at specified bond length
    /// Default bond length: 1.596 Å (experimental equilibrium)
    ///
    /// LiH is important for:
    /// - Hydrogen storage materials
    /// - Benchmark system for quantum chemistry (4 electrons)
    /// - Nuclear fusion breeding blankets (Li-6)
    let createLiH (bondLength: float) : Molecule =
        {
            Name = "LiH"
            Atoms =
                [
                    {
                        Element = "Li"
                        Position = (0.0, 0.0, 0.0)
                    }
                    {
                        Element = "H"
                        Position = (0.0, 0.0, bondLength)
                    }
                ]
            Bonds =
                [
                    {
                        Atom1 = 0
                        Atom2 = 1
                        BondOrder = 1.0
                    }
                ]
            Charge = 0
            Multiplicity = 1 // Singlet
        }

    /// Create Fe₂ dimer (iron dimer) at specified bond length
    /// Default bond length: 2.02 Å (experimental)
    ///
    /// Fe₂ is critical for:
    /// - Exchange coupling (J) calculations in magnetic materials
    /// - GMR (Giant Magnetoresistance) physics
    /// - Benchmark for spin-dependent DFT and quantum methods
    ///
    /// Ground state: ⁷Δᵤ (septet, 6 unpaired electrons, S=3)
    let createFe2 (bondLength: float) (multiplicity: int) : Molecule =
        {
            Name = "Fe2"
            Atoms =
                [
                    {
                        Element = "Fe"
                        Position = (0.0, 0.0, 0.0)
                    }
                    {
                        Element = "Fe"
                        Position = (0.0, 0.0, bondLength)
                    }
                ]
            Bonds =
                [
                    {
                        Atom1 = 0
                        Atom2 = 1
                        BondOrder = 1.0
                    } // Single bond approximation
                ]
            Charge = 0
            Multiplicity = multiplicity // Usually 7 for ground state (septet)
        }

    /// Create FeH molecule (iron monohydride) at specified bond length
    /// Default bond length: 1.63 Å (experimental)
    ///
    /// FeH is relevant for:
    /// - Hydrogen diffusion in steel (embrittlement)
    /// - Interstellar chemistry
    /// - Catalytic hydrogenation mechanisms
    ///
    /// Ground state: ⁴Δ (quartet, 3 unpaired electrons, S=3/2)
    let createFeH (bondLength: float) : Molecule =
        {
            Name = "FeH"
            Atoms =
                [
                    {
                        Element = "Fe"
                        Position = (0.0, 0.0, 0.0)
                    }
                    {
                        Element = "H"
                        Position = (0.0, 0.0, bondLength)
                    }
                ]
            Bonds =
                [
                    {
                        Atom1 = 0
                        Atom2 = 1
                        BondOrder = 1.0
                    }
                ]
            Charge = 0
            Multiplicity = 4 // Quartet ground state
        }

    /// Create SiH₄ molecule (silane) at equilibrium geometry
    /// Si-H bond length: 1.480 Å (experimental)
    ///
    /// SiH₄ is important for:
    /// - Semiconductor doping precursor (CVD)
    /// - Silicon surface chemistry
    /// - Model for Si-H bonding in passivated surfaces
    let createSiH4 () : Molecule =
        // Tetrahedral geometry: Si at center, H at corners
        let bondLength = 1.480
        // Tetrahedral angle: cos⁻¹(-1/3) ≈ 109.47°
        // Coordinates for regular tetrahedron with Si at origin
        let a = bondLength / sqrt 3.0

        {
            Name = "SiH4"
            Atoms =
                [
                    {
                        Element = "Si"
                        Position = (0.0, 0.0, 0.0)
                    }
                    { Element = "H"; Position = (a, a, a) }
                    {
                        Element = "H"
                        Position = (-a, -a, a)
                    }
                    {
                        Element = "H"
                        Position = (-a, a, -a)
                    }
                    {
                        Element = "H"
                        Position = (a, -a, -a)
                    }
                ]
            Bonds =
                [
                    {
                        Atom1 = 0
                        Atom2 = 1
                        BondOrder = 1.0
                    }
                    {
                        Atom1 = 0
                        Atom2 = 2
                        BondOrder = 1.0
                    }
                    {
                        Atom1 = 0
                        Atom2 = 3
                        BondOrder = 1.0
                    }
                    {
                        Atom1 = 0
                        Atom2 = 4
                        BondOrder = 1.0
                    }
                ]
            Charge = 0
            Multiplicity = 1 // Singlet (all electrons paired)
        }

    /// Create PH₃ molecule (phosphine) at equilibrium geometry
    /// P-H bond length: 1.42 Å, H-P-H angle: 93.5°
    ///
    /// PH₃ is important for:
    /// - Phosphorus doping in semiconductors (n-type Si)
    /// - MOCVD precursor for III-V semiconductors
    /// - Model for donor impurity states in Si
    let createPH3 () : Molecule =
        let bondLength = 1.42
        let angleRad = 93.5 * Math.PI / 180.0
        // Pyramidal geometry with P at origin
        let h = bondLength * cos (angleRad / 2.0) // Height above base
        let r = bondLength * sin (angleRad / 2.0) // Radius of base
        // Three H atoms arranged 120° apart
        {
            Name = "PH3"
            Atoms =
                [
                    {
                        Element = "P"
                        Position = (0.0, 0.0, 0.0)
                    }
                    {
                        Element = "H"
                        Position = (r, 0.0, h)
                    }
                    {
                        Element = "H"
                        Position = (-r * 0.5, r * 0.866, h)
                    }
                    {
                        Element = "H"
                        Position = (-r * 0.5, -r * 0.866, h)
                    }
                ]
            Bonds =
                [
                    {
                        Atom1 = 0
                        Atom2 = 1
                        BondOrder = 1.0
                    }
                    {
                        Atom1 = 0
                        Atom2 = 2
                        BondOrder = 1.0
                    }
                    {
                        Atom1 = 0
                        Atom2 = 3
                        BondOrder = 1.0
                    }
                ]
            Charge = 0
            Multiplicity = 1 // Singlet
        }

    /// Create CdSe dimer (cadmium selenide) - quantum dot building block
    /// Cd-Se bond length: 2.63 Å (from wurtzite crystal structure)
    ///
    /// CdSe is the most common quantum dot material:
    /// - QLED displays (Samsung, Sony)
    /// - Solar cells
    /// - Biomedical imaging
    let createCdSe (bondLength: float) : Molecule =
        {
            Name = "CdSe"
            Atoms =
                [
                    {
                        Element = "Cd"
                        Position = (0.0, 0.0, 0.0)
                    }
                    {
                        Element = "Se"
                        Position = (bondLength, 0.0, 0.0)
                    }
                ]
            Bonds =
                [
                    {
                        Atom1 = 0
                        Atom2 = 1
                        BondOrder = 2.0
                    } // Approximate
                ]
            Charge = 0
            Multiplicity = 1
        }

    // ========================================================================
    // CONVERSION FROM MOLECULELIBRARY
    // ========================================================================

    /// Convert a molecule from the MoleculeLibrary (Data layer) to QuantumChemistry.Molecule
    ///
    /// This enables using the pre-defined molecules from MoleculeLibrary with
    /// quantum chemistry solvers like GroundStateEnergy.estimateEnergy.
    ///
    /// Example:
    ///   open FSharp.Azure.Quantum.Data
    ///   open FSharp.Azure.Quantum.QuantumChemistry
    ///   let water = MoleculeLibrary.get "H2O" |> Molecule.fromLibrary
    ///   let energy = GroundStateEnergy.estimateEnergy backend water
    let fromLibrary (libMol: MoleculeLibrary.Molecule) : Molecule =
        {
            Name = libMol.Name
            Atoms =
                libMol.Atoms
                |> List.map (fun a ->
                    {
                        Element = a.Element
                        Position = a.Position
                    })
            Bonds =
                libMol.Bonds
                |> List.map (fun b ->
                    {
                        Atom1 = b.Atom1
                        Atom2 = b.Atom2
                        BondOrder = b.BondOrder
                    })
            Charge = libMol.Charge
            Multiplicity = libMol.Multiplicity
        }

    /// Try to get a molecule from MoleculeLibrary by name and convert it
    /// Returns None if the molecule is not found in the library
    ///
    /// Example:
    ///   match Molecule.tryFromLibrary "benzene" with
    ///   | Some mol -> printfn "Found: %s with %d atoms" mol.Name mol.Atoms.Length
    ///   | None -> printfn "Not found"
    let tryFromLibrary (name: string) : Molecule option =
        MoleculeLibrary.tryGet name |> Option.map fromLibrary

    /// Get a molecule from MoleculeLibrary by name and convert it
    /// Throws if the molecule is not found
    ///
    /// Example:
    ///   let water = Molecule.fromLibraryByName "H2O"
    let fromLibraryByName (name: string) : Molecule = MoleculeLibrary.get name |> fromLibrary

    // ========================================================================
    // PROVIDER-BASED MOLECULE LOADING
    // ========================================================================

    /// Convert a MoleculeInstance from the provider system to QuantumChemistry.Molecule.
    /// Returns Error if the instance has no geometry (QC requires 3D coordinates).
    ///
    /// Example:
    ///   let provider = ChemistryDataProviders.defaultDatasetProvider
    ///   match provider.Load (DatasetQuery.ByName "H2O") with
    ///   | Ok dataset ->
    ///       match Molecule.fromInstance dataset.Molecules.[0] with
    ///       | Ok mol -> // use mol
    ///       | Error e -> // missing geometry
    ///   | Error e -> // provider error
    let fromInstance (instance: ChemistryDataProviders.MoleculeInstance) : Result<Molecule, QuantumError> =
        match instance.Geometry with
        | None ->
            let molName = instance.Name |> Option.defaultValue "unknown"

            Error(
                QuantumError.ValidationError(
                    "MissingGeometry",
                    $"Molecule '{molName}' has no 3D geometry. QC requires coordinates."
                )
            )
        | Some geom ->
            let topology = instance.Topology

            // Validate atom count matches coordinate count
            if topology.Atoms.Length <> geom.Coordinates.Length then
                Error(
                    QuantumError.ValidationError(
                        "AtomCoordinateMismatch",
                        $"Topology has {topology.Atoms.Length} atoms but geometry has {geom.Coordinates.Length} coordinates"
                    )
                )
            else
                let atoms =
                    Array.zip topology.Atoms geom.Coordinates
                    |> Array.map (fun (element, coord) ->
                        {
                            Element = element
                            Position = (coord.X, coord.Y, coord.Z)
                        })
                    |> Array.toList

                let bonds =
                    topology.Bonds
                    |> Array.map (fun (a1, a2, order) ->
                        {
                            Atom1 = a1
                            Atom2 = a2
                            BondOrder = order |> Option.defaultValue 1.0
                        })
                    |> Array.toList

                Ok
                    {
                        Name = instance.Name |> Option.defaultValue "Molecule"
                        Atoms = atoms
                        Bonds = bonds
                        Charge = topology.Charge |> Option.defaultValue 0
                        Multiplicity = topology.Multiplicity |> Option.defaultValue 1
                    }

    /// Load molecule from a dataset provider by name.
    /// Returns Error if not found or if geometry is missing.
    ///
    /// Example:
    ///   let provider = ChemistryDataProviders.defaultDatasetProvider
    ///   match Molecule.fromProvider provider "H2O" with
    ///   | Ok mol -> printfn "Loaded: %s" mol.Name
    ///   | Error e -> printfn "Error: %A" e
    let fromProvider
        (provider: ChemistryDataProviders.IMoleculeDatasetProvider)
        (name: string)
        : Result<Molecule, QuantumError> =
        match provider.Load(ChemistryDataProviders.DatasetQuery.ByName name) with
        | Error e -> Error e
        | Ok dataset ->
            if dataset.Molecules.Length = 0 then
                Error(QuantumError.ValidationError("MoleculeNotFound", $"Molecule '{name}' not found in provider"))
            else
                fromInstance dataset.Molecules.[0]

    /// Load molecule from a dataset provider by name, using the default provider.
    /// This is a convenience function that uses the built-in MoleculeLibrary provider.
    ///
    /// Example:
    ///   match Molecule.fromDefaultProvider "benzene" with
    ///   | Ok mol -> printfn "Found: %s with %d atoms" mol.Name mol.Atoms.Length
    ///   | Error e -> printfn "Error: %A" e
    let fromDefaultProvider (name: string) : Result<Molecule, QuantumError> =
        fromProvider ChemistryDataProviders.defaultDatasetProvider name

    // ========================================================================
    // FILE I/O FUNCTIONS (using MoleculeFormats)
    // ========================================================================

    /// Convert MoleculeFormats.MoleculeData to Molecule.
    /// Internal helper that chains through MoleculeInstance conversion.
    let private fromMoleculeData (data: MoleculeFormats.MoleculeData) : Result<Molecule, QuantumError> =
        let instance = ChemistryDataProviders.Conversions.fromMoleculeData data
        fromInstance instance

    /// Load molecule from XYZ file asynchronously (Task-based, zero bridging).
    ///
    /// Example:
    ///   let! result = Molecule.fromXyzFileTask "water.xyz" ct
    ///   match result with
    ///   | Ok mol -> printfn "Loaded: %s" mol.Name
    ///   | Error e -> printfn "Error: %A" e
    let fromXyzFileTask
        (filePath: string)
        (ct: CancellationToken)
        : System.Threading.Tasks.Task<Result<Molecule, QuantumError>> =
        task {
            let! result = MoleculeFormats.Xyz.readAsync filePath ct
            return result |> Result.bind fromMoleculeData
        }

    /// Load molecule from XYZ file asynchronously (F# Async wrapper).
    [<System.Obsolete("Use fromXyzFileTask instead. This Async wrapper bridges through Async.AwaitTask.")>]
    let fromXyzFileAsync (filePath: string) : Async<Result<Molecule, QuantumError>> =
        async {
            let! ct = Async.CancellationToken
            let! result = MoleculeFormats.Xyz.readAsync filePath ct |> Async.AwaitTask
            return result |> Result.bind fromMoleculeData
        }

    /// Load molecule from XYZ file synchronously.
    [<System.Obsolete("Use fromXyzFileTask instead. This synchronous wrapper blocks the calling thread.")>]
    let fromXyzFile (filePath: string) : Result<Molecule, QuantumError> =
        fromXyzFileTask filePath CancellationToken.None
        |> Async.AwaitTask
        |> Async.RunSynchronously

    /// Load molecule from FCIDump file asynchronously (Task-based, zero bridging).
    ///
    /// Note: FCIDump files typically don't contain geometry, so the resulting
    /// Molecule will have placeholder atoms. Use for orbital/electron info only.
    ///
    /// Example:
    ///   let! result = Molecule.fromFciDumpFileTask "h2.fcidump" ct
    let fromFciDumpFileTask
        (filePath: string)
        (ct: CancellationToken)
        : System.Threading.Tasks.Task<Result<Molecule, QuantumError>> =
        task {
            let! result = MoleculeFormats.FciDump.readAsync filePath ct
            return result |> Result.bind fromMoleculeData
        }

    /// Load molecule from FCIDump file asynchronously (F# Async wrapper).
    [<System.Obsolete("Use fromFciDumpFileTask instead. This Async wrapper bridges through Async.AwaitTask.")>]
    let fromFciDumpFileAsync (filePath: string) : Async<Result<Molecule, QuantumError>> =
        async {
            let! ct = Async.CancellationToken
            let! result = MoleculeFormats.FciDump.readAsync filePath ct |> Async.AwaitTask
            return result |> Result.bind fromMoleculeData
        }

    /// Load molecule from FCIDump file synchronously.
    [<System.Obsolete("Use fromFciDumpFileTask instead. This synchronous wrapper blocks the calling thread.")>]
    let fromFciDumpFile (filePath: string) : Result<Molecule, QuantumError> =
        fromFciDumpFileTask filePath CancellationToken.None
        |> Async.AwaitTask
        |> Async.RunSynchronously

    /// Format molecule as XYZ string.
    ///
    /// Example:
    ///   let xyzContent = Molecule.toXyz h2Molecule
    let toXyz (molecule: Molecule) : string =
        let sb = System.Text.StringBuilder()
        sb.AppendLine(string molecule.Atoms.Length) |> ignore
        sb.AppendLine(molecule.Name) |> ignore

        for atom in molecule.Atoms do
            let (x, y, z) = atom.Position
            sb.AppendLine($"%-2s{atom.Element}  %10.6f{x}  %10.6f{y}  %10.6f{z}") |> ignore

        sb.ToString()

    /// Save molecule to XYZ file asynchronously (Task-based, zero bridging).
    ///
    /// Example:
    ///   let! result = Molecule.saveToXyzFileTask "output.xyz" molecule ct
    let saveToXyzFileTask
        (filePath: string)
        (molecule: Molecule)
        (ct: CancellationToken)
        : System.Threading.Tasks.Task<Result<unit, QuantumError>> =
        task {
            try
                let content = toXyz molecule
                do! File.WriteAllTextAsync(filePath, content, ct)
                return Ok()
            with ex ->
                return Error(QuantumError.IOError("WriteXYZ", filePath, ex.Message))
        }

    /// Save molecule to XYZ file asynchronously (F# Async wrapper).
    [<System.Obsolete("Use saveToXyzFileTask instead. This Async wrapper bridges through Async.AwaitTask.")>]
    let saveToXyzFileAsync (filePath: string) (molecule: Molecule) : Async<Result<unit, QuantumError>> =
        async {
            try
                let content = toXyz molecule
                do! File.WriteAllTextAsync(filePath, content) |> Async.AwaitTask
                return Ok()
            with ex ->
                return Error(QuantumError.IOError("WriteXYZ", filePath, ex.Message))
        }

    /// Save molecule to XYZ file synchronously.
    [<System.Obsolete("Use saveToXyzFileTask instead. This synchronous wrapper blocks the calling thread.")>]
    let saveToXyzFile (filePath: string) (molecule: Molecule) : Result<unit, QuantumError> =
        saveToXyzFileTask filePath molecule CancellationToken.None
        |> Async.AwaitTask
        |> Async.RunSynchronously

/// One peak of a quantum phase estimation outcome distribution: an eigenvalue of the
/// (Trotterised) Hamiltonian that the prepared state overlaps.
[<Struct>]
type PhaseEstimationPeak =
    {
        /// Eigenvalue estimate in Hartree, nuclear repulsion included, refined between the
        /// peak's two most probable bins
        Energy: float
        /// Probability of the peak's bins (the most probable one and two either side): an
        /// estimate of |⟨ψ|E⟩|² for the prepared state ψ
        Probability: float
    }

/// Parameters and outcome of a chemistry QPE run (QPE.run).
type PhaseEstimationDetails =
    {
        /// Counting (phase) qubits m: 2^m outcome bins
        CountingQubits: int
        /// Evolution time t of U = e^(-i(H - EnergyShift)t), atomic units (ħ/Eh)
        EvolutionTime: float
        /// Upper bound on the electronic spectrum subtracted from H, Hartree; phase φ maps to
        /// E = -2πφ/t + EnergyShift (+ nuclear repulsion)
        EnergyShift: float
        /// Trotter-Suzuki order of each U factor (1 or 2)
        TrotterOrder: int
        /// Trotter steps per U; controlled-U^(2^j) repeats the same steps 2^j times
        TrotterStepsPerEvolution: int
        /// Width of one outcome bin in Hartree, 2π/(t·2^m)
        BinWidth: float
        /// Bin-centre energy of the most probable outcome, nuclear repulsion included
        PeakBinEnergy: float
        /// Peaks of the outcome distribution holding at least 1% probability, most probable first
        Peaks: PhaseEstimationPeak list
        /// The backend's shots per circuit when it samples (IShotSamplingBackend); None when
        /// the returned state gives exact probabilities
        ShotsPerCircuit: int option
    }

/// How a VQE energy was estimated from the quantum state.
type EnergyEstimation =
    /// Exact expectation values from state-vector amplitudes, simulated gate by gate.
    | ExactExpectation
    /// Gate-by-gate simulation on a backend whose states are not state vectors: each
    /// expectation value is estimated from `shots` samples of the state.
    | SampledGateByGate of shots: int
    /// Whole circuits submitted to the backend (cloud hardware, which cannot apply gates one
    /// at a time): one circuit per qubit-wise commuting group of Pauli terms for every energy,
    /// `circuitsPerEnergy` in all, and `circuitsExecuted` over the whole run. `shotsPerCircuit`
    /// is the backend's shot count (IShotSamplingBackend); None when the backend does not
    /// report one, in which case its returned probabilities are used as they are.
    | SampledCircuits of circuitsPerEnergy: int * shotsPerCircuit: int option * circuitsExecuted: int
    /// Quantum phase estimation of e^(-iHt): the energy is an eigenvalue read from the
    /// outcome distribution of the counting register.
    | PhaseEstimation of PhaseEstimationDetails
    /// No quantum estimate: a tabulated value.
    | NotEstimated

// ============================================================================
// FERMION-TO-QUBIT MAPPINGS
// ============================================================================

/// Fermion-to-qubit transformation mappings for molecular Hamiltonians
///
/// Converts fermionic operators (creation/annihilation) to qubit Pauli operators.
/// This is essential for implementing molecular Hamiltonians on quantum hardware.
///
/// Supported mappings:
/// - Jordan-Wigner: Simple, locality-preserving for 1D systems
/// - Bravyi-Kitaev: Reduces gate depth, better for quantum circuits
module FermionMapping =

    open System.Numerics

    // ========================================================================
    // FERMIONIC OPERATORS - Second Quantization
    // ========================================================================

    /// Fermionic creation (a†) or annihilation (a) operator
    [<Struct>]
    type FermionOperatorType =
        /// Creation operator a† (adds an electron to orbital)
        | Creation
        /// Annihilation operator a (removes an electron from orbital)
        | Annihilation

    /// Single fermionic operator on a specific orbital
    type FermionOperator =
        {
            /// Orbital index (0-based)
            OrbitalIndex: int
            /// Operator type (creation or annihilation)
            OperatorType: FermionOperatorType
        }

    /// Fermionic term: product of fermionic operators with coefficient
    /// Example: 0.5 * a†₀ a†₁ a₂ a₃ (two-body interaction)
    type FermionTerm =
        {
            /// Complex coefficient
            Coefficient: Complex
            /// Ordered list of fermionic operators
            /// Convention: Creation operators first, then annihilation (normal order)
            Operators: FermionOperator list
        }

    /// Complete fermionic Hamiltonian in second quantization
    type FermionHamiltonian =
        {
            /// Number of spin orbitals
            NumOrbitals: int
            /// List of fermionic terms
            Terms: FermionTerm list
        }

    // ========================================================================
    // QUBIT PAULI OPERATORS - First Quantization
    // ========================================================================

    /// Pauli string: product of Pauli operators on qubits
    /// Example: X₀ Y₁ Z₂ (Pauli X on qubit 0, Y on 1, Z on 2)
    type PauliString =
        {
            /// Complex coefficient
            Coefficient: Complex
            /// Pauli operators for each qubit
            /// Key: qubit index, Value: Pauli operator (I, X, Y, Z)
            /// Missing keys default to Identity (I)
            Operators: Map<int, QaoaCircuit.PauliOperator>
        }

    /// Qubit Hamiltonian as sum of Pauli strings
    type QubitHamiltonian =
        {
            /// Number of qubits
            NumQubits: int
            /// List of Pauli strings
            Terms: PauliString list
        }

    // ========================================================================
    // PAULI ALGEBRA - Helper Functions
    // ========================================================================

    /// Multiply two Pauli operators, returning (phase, resultOperator)
    /// Pauli multiplication rules:
    /// - I*P = P, P*I = P (identity)
    /// - X*X = Y*Y = Z*Z = I
    /// - X*Y = iZ, Y*Z = iX, Z*X = iY (cyclic)
    /// - Y*X = -iZ, Z*Y = -iX, X*Z = -iY (anti-cyclic)
    let multiplyPaulis
        (p1: QaoaCircuit.PauliOperator)
        (p2: QaoaCircuit.PauliOperator)
        : Complex * QaoaCircuit.PauliOperator =
        match p1, p2 with
        // Identity rules
        | QaoaCircuit.PauliI, p
        | p, QaoaCircuit.PauliI -> (Complex.One, p)

        // Self-multiplication (returns Identity)
        | QaoaCircuit.PauliX, QaoaCircuit.PauliX
        | QaoaCircuit.PauliY, QaoaCircuit.PauliY
        | QaoaCircuit.PauliZ, QaoaCircuit.PauliZ -> (Complex.One, QaoaCircuit.PauliI)

        // Cyclic permutations (positive phase)
        | QaoaCircuit.PauliX, QaoaCircuit.PauliY -> (Complex.ImaginaryOne, QaoaCircuit.PauliZ)
        | QaoaCircuit.PauliY, QaoaCircuit.PauliZ -> (Complex.ImaginaryOne, QaoaCircuit.PauliX)
        | QaoaCircuit.PauliZ, QaoaCircuit.PauliX -> (Complex.ImaginaryOne, QaoaCircuit.PauliY)

        // Anti-cyclic permutations (negative phase)
        | QaoaCircuit.PauliY, QaoaCircuit.PauliX -> (-Complex.ImaginaryOne, QaoaCircuit.PauliZ)
        | QaoaCircuit.PauliZ, QaoaCircuit.PauliY -> (-Complex.ImaginaryOne, QaoaCircuit.PauliX)
        | QaoaCircuit.PauliX, QaoaCircuit.PauliZ -> (-Complex.ImaginaryOne, QaoaCircuit.PauliY)

    /// Multiply two Pauli strings
    let multiplyPauliStrings (ps1: PauliString) (ps2: PauliString) : PauliString =
        // Combine operators from both strings
        let allQubits =
            Set.union (ps1.Operators |> Map.keys |> Set.ofSeq) (ps2.Operators |> Map.keys |> Set.ofSeq)

        // Multiply Pauli operators qubit-by-qubit using fold
        let (totalPhase, resultOperators) =
            allQubits
            |> Set.fold
                (fun (phase, ops) qubitIdx ->
                    let pauli1 =
                        ps1.Operators |> Map.tryFind qubitIdx |> Option.defaultValue QaoaCircuit.PauliI

                    let pauli2 =
                        ps2.Operators |> Map.tryFind qubitIdx |> Option.defaultValue QaoaCircuit.PauliI

                    let (newPhase, resultPauli) = multiplyPaulis pauli1 pauli2
                    let updatedPhase = phase * newPhase

                    // Only store non-identity operators
                    let updatedOps =
                        if resultPauli <> QaoaCircuit.PauliI then
                            ops |> Map.add qubitIdx resultPauli
                        else
                            ops

                    (updatedPhase, updatedOps))
                (ps1.Coefficient * ps2.Coefficient, Map.empty)

        {
            Coefficient = totalPhase
            Operators = resultOperators
        }

    // ========================================================================
    // JORDAN-WIGNER TRANSFORMATION
    // ========================================================================

    /// Jordan-Wigner transformation: maps fermionic operators to qubits
    ///
    /// Mapping:
    /// - Fermion orbital j → Qubit j (one-to-one correspondence)
    /// - a†ⱼ = (X - iY)/2 * Z₀ Z₁ ... Z_{j-1}
    /// - aⱼ  = (X + iY)/2 * Z₀ Z₁ ... Z_{j-1}
    ///
    /// Properties:
    /// - Preserves locality for 1D systems
    /// - Simple, intuitive mapping
    /// - Long string of Z operators for high-index orbitals
    module JordanWigner =

        /// Transform single fermionic operator to Pauli string(s)
        /// Returns two Pauli strings (X and Y components)
        let transformOperator (op: FermionOperator) : PauliString * PauliString =
            let j = op.OrbitalIndex

            // Build Z-string: Z₀ Z₁ ... Z_{j-1}
            let zString = List.init (max 0 j) (fun i -> (i, QaoaCircuit.PauliZ)) |> Map.ofList

            match op.OperatorType with
            | Creation ->
                // a†ⱼ = (X - iY)/2 * Z-string
                let xTerm =
                    {
                        Coefficient = Complex(0.5, 0.0)
                        Operators = zString |> Map.add j QaoaCircuit.PauliX
                    }

                let yTerm =
                    {
                        Coefficient = Complex(0.0, -0.5) // -i/2
                        Operators = zString |> Map.add j QaoaCircuit.PauliY
                    }

                (xTerm, yTerm)

            | Annihilation ->
                // aⱼ = (X + iY)/2 * Z-string
                let xTerm =
                    {
                        Coefficient = Complex(0.5, 0.0)
                        Operators = zString |> Map.add j QaoaCircuit.PauliX
                    }

                let yTerm =
                    {
                        Coefficient = Complex(0.0, 0.5) // +i/2
                        Operators = zString |> Map.add j QaoaCircuit.PauliY
                    }

                (xTerm, yTerm)

        /// Transform fermionic term (product of operators) to Pauli strings
        let transformTerm (term: FermionTerm) : PauliString list =
            if term.Operators.IsEmpty then
                // Constant term (identity)
                [
                    {
                        Coefficient = term.Coefficient
                        Operators = Map.empty
                    }
                ]
            else
                // Transform each fermionic operator to (X, Y) pair
                let pauliPairs = term.Operators |> List.map transformOperator

                // Expand all combinations of X/Y terms
                // For n operators: 2^n Pauli strings
                let rec expandProduct (pairs: (PauliString * PauliString) list) : PauliString list =
                    match pairs with
                    | [] ->
                        // Base case: identity string
                        [
                            {
                                Coefficient = Complex.One
                                Operators = Map.empty
                            }
                        ]
                    | (xTerm, yTerm) :: rest ->
                        let restExpanded = expandProduct rest

                        // Combine current (X, Y) with all rest expansions
                        [
                            for prevString in restExpanded do
                                yield multiplyPauliStrings xTerm prevString
                                yield multiplyPauliStrings yTerm prevString
                        ]

                let expanded = expandProduct pauliPairs

                // Apply original coefficient
                expanded
                |> List.map (fun ps ->
                    { ps with
                        Coefficient = term.Coefficient * ps.Coefficient
                    })

        /// Transform complete fermionic Hamiltonian to qubit Hamiltonian
        let transform (hamiltonian: FermionHamiltonian) : QubitHamiltonian =
            let allPauliStrings = hamiltonian.Terms |> List.collect transformTerm

            // Group and simplify identical Pauli strings
            let simplified =
                allPauliStrings
                |> List.groupBy (fun ps -> ps.Operators)
                |> List.map (fun (operators, group) ->
                    let totalCoeff =
                        group
                        |> List.map (fun ps -> ps.Coefficient)
                        |> List.fold (fun acc c -> acc + c) Complex.Zero

                    {
                        Coefficient = totalCoeff
                        Operators = operators
                    })
                |> List.filter (fun ps -> ps.Coefficient.Magnitude > 1e-12) // Remove near-zero terms

            {
                NumQubits = hamiltonian.NumOrbitals
                Terms = simplified
            }

    // ========================================================================
    // BRAVYI-KITAEV TRANSFORMATION
    // ========================================================================

    /// Bravyi-Kitaev transformation: more efficient mapping for quantum circuits
    ///
    /// Mapping uses binary tree structure:
    /// - Reduces gate depth compared to Jordan-Wigner
    /// - Each qubit stores parity information for a subtree of orbitals
    /// - Better scaling for large molecules
    ///
    /// Properties:
    /// - Logarithmic scaling of operator weight
    /// - Preserves locality better than Jordan-Wigner for 2D/3D systems
    /// - More complex implementation
    module BravyiKitaev =

        /// Get binary representation helpers
        let private isPowerOfTwo n = n > 0 && (n &&& (n - 1)) = 0

        /// Find lowest set bit position (0-indexed)
        let private lowestSetBit n =
            if n = 0 then
                -1
            else
                let rec findBit pos value =
                    if value &&& 1 = 1 then
                        pos
                    else
                        findBit (pos + 1) (value >>> 1)

                findBit 0 n

        /// Compute parity set P(j): qubits that store parity for orbital j
        let private paritySet (j: int) (numOrbitals: int) : int list =
            [
                for k in 0 .. numOrbitals - 1 do
                    // Include qubit k if it affects orbital j's parity
                    let blockSize = 1 <<< (lowestSetBit (k + 1) + 1)
                    let blockStart = (j / blockSize) * blockSize

                    if k >= blockStart && k <= j then
                        yield k
            ]

        /// Compute update set U(j): qubits that need updating when orbital j changes
        let private updateSet (j: int) (numOrbitals: int) : int list =
            let jLowest = lowestSetBit (j + 1)

            [
                for k in j + 1 .. numOrbitals - 1 do
                    let kLowest = lowestSetBit (k + 1)

                    if kLowest < jLowest then
                        yield k
            ]

        /// Transform single fermionic operator to Pauli string(s)
        let transformOperator (op: FermionOperator) (numOrbitals: int) : PauliString * PauliString =
            let j = op.OrbitalIndex

            // Get parity and update sets
            let pSet = paritySet j numOrbitals
            let uSet = updateSet j numOrbitals

            // Build operator string using functional approach
            let buildOperators (mainOp: QaoaCircuit.PauliOperator) =
                Map.empty
                // Parity set (excluding j): Z operators
                |> fun ops ->
                    pSet
                    |> List.filter ((<>) j)
                    |> List.fold (fun m k -> Map.add k QaoaCircuit.PauliZ m) ops
                // Qubit j: main operator (X or Y)
                |> Map.add j mainOp
                // Update set: X operators
                |> fun ops -> uSet |> List.fold (fun m k -> Map.add k QaoaCircuit.PauliX m) ops

            match op.OperatorType with
            | Creation ->
                // a†ⱼ = (X - iY)/2 with BK structure
                let xTerm =
                    {
                        Coefficient = Complex(0.5, 0.0)
                        Operators = buildOperators QaoaCircuit.PauliX
                    }

                let yTerm =
                    {
                        Coefficient = Complex(0.0, -0.5)
                        Operators = buildOperators QaoaCircuit.PauliY
                    }

                (xTerm, yTerm)

            | Annihilation ->
                // aⱼ = (X + iY)/2 with BK structure
                let xTerm =
                    {
                        Coefficient = Complex(0.5, 0.0)
                        Operators = buildOperators QaoaCircuit.PauliX
                    }

                let yTerm =
                    {
                        Coefficient = Complex(0.0, 0.5)
                        Operators = buildOperators QaoaCircuit.PauliY
                    }

                (xTerm, yTerm)

        /// Transform fermionic term to Pauli strings
        let transformTerm (term: FermionTerm) (numOrbitals: int) : PauliString list =
            if term.Operators.IsEmpty then
                [
                    {
                        Coefficient = term.Coefficient
                        Operators = Map.empty
                    }
                ]
            else
                let pauliPairs =
                    term.Operators |> List.map (fun op -> transformOperator op numOrbitals)

                let rec expandProduct (pairs: (PauliString * PauliString) list) : PauliString list =
                    match pairs with
                    | [] ->
                        [
                            {
                                Coefficient = Complex.One
                                Operators = Map.empty
                            }
                        ]
                    | (xTerm, yTerm) :: rest ->
                        let restExpanded = expandProduct rest

                        [
                            for prevString in restExpanded do
                                yield multiplyPauliStrings xTerm prevString
                                yield multiplyPauliStrings yTerm prevString
                        ]

                let expanded = expandProduct pauliPairs

                expanded
                |> List.map (fun ps ->
                    { ps with
                        Coefficient = term.Coefficient * ps.Coefficient
                    })

        /// Transform complete fermionic Hamiltonian
        let transform (hamiltonian: FermionHamiltonian) : QubitHamiltonian =
            let allPauliStrings =
                hamiltonian.Terms
                |> List.collect (fun term -> transformTerm term hamiltonian.NumOrbitals)

            let simplified =
                allPauliStrings
                |> List.groupBy (fun ps -> ps.Operators)
                |> List.map (fun (operators, group) ->
                    let totalCoeff =
                        group
                        |> List.map (fun ps -> ps.Coefficient)
                        |> List.fold (fun acc c -> acc + c) Complex.Zero

                    {
                        Coefficient = totalCoeff
                        Operators = operators
                    })
                |> List.filter (fun ps -> ps.Coefficient.Magnitude > 1e-12)

            {
                NumQubits = hamiltonian.NumOrbitals
                Terms = simplified
            }

    // ========================================================================
    // CONVERSION TO LIBRARY TYPES
    // ========================================================================

    /// Convert QubitHamiltonian to library's ProblemHamiltonian format
    let toQaoaHamiltonian (hamiltonian: QubitHamiltonian) : QaoaCircuit.ProblemHamiltonian =
        let terms =
            hamiltonian.Terms
            |> List.map (fun pauliString ->
                // Extract qubits and operators in order
                let sortedOps = pauliString.Operators |> Map.toList |> List.sortBy fst

                {
                    Coefficient = pauliString.Coefficient.Real // Use real part (Hermitian)
                    QubitsIndices = sortedOps |> List.map fst |> Array.ofList
                    PauliOperators = sortedOps |> List.map snd |> Array.ofList
                }
                : QaoaCircuit.HamiltonianTerm)
            |> Array.ofList

        {
            NumQubits = hamiltonian.NumQubits
            Terms = terms
        }

    /// Convert QaoaCircuit.ProblemHamiltonian to QubitHamiltonian
    /// (Reverse of toQaoaHamiltonian)
    let fromQaoaHamiltonian (hamiltonian: QaoaCircuit.ProblemHamiltonian) : QubitHamiltonian =
        let terms =
            hamiltonian.Terms
            |> Array.map (fun term ->
                // Build Pauli operators map from arrays
                let operators = Array.zip term.QubitsIndices term.PauliOperators |> Map.ofArray

                {
                    Coefficient = Complex(term.Coefficient, 0.0)
                    Operators = operators
                }
                : PauliString)
            |> Array.toList

        {
            NumQubits = hamiltonian.NumQubits
            Terms = terms
        }

    // ========================================================================
    // UCCSD ANSATZ - Unitary Coupled Cluster Singles and Doubles
    // ========================================================================

    /// UCCSD Ansatz - Chemically-inspired variational quantum eigensolver ansatz
    ///
    /// Implements Unitary Coupled Cluster with Singles and Doubles excitations.
    /// This is the "gold standard" ansatz for quantum chemistry on quantum computers.
    ///
    /// **Theory**:
    /// UCCSD = exp(T - T†) where:
    /// - T = T₁ + T₂ (cluster operator)
    /// - T₁ = Σᵢₐ tᵢₐ a†ₐ aᵢ (single excitations: occupied i → virtual a)
    /// - T₂ = Σᵢⱼₐᵦ tᵢⱼₐᵦ a†ₐ a†ᵦ aⱼ aᵢ (double excitations: i,j → a,b)
    ///
    /// **Parameters**:
    /// - Singles: n_occupied × n_virtual amplitudes
    /// - Doubles: (n_occupied choose 2) × (n_virtual choose 2) amplitudes
    ///
    /// **Example (H2 minimal basis)**:
    /// - 2 electrons, 4 spin-orbitals (2 occupied, 2 virtual)
    /// - Singles: 2 × 2 = 4 parameters
    /// - Doubles: C(2,2) × C(2,2) = 1 parameter
    /// - Total: 5 parameters
    ///
    /// **Production Value**: ⭐⭐⭐⭐⭐
    /// - Chemical accuracy: ±1 kcal/mol (±0.0016 Hartree)
    /// - Used in drug discovery, materials science
    /// - Industry standard for molecular simulation
    ///
    /// **Textbook References**:
    /// - Peruzzo et al. "A variational eigenvalue solver..." Nature 2014
    /// - Romero et al. "Strategies for quantum computing molecular energies..." QST 2018
    /// - McArdle et al. "Quantum computational chemistry" Rev. Mod. Phys. 2020
    module UCCSD =

        open System.Numerics

        // ====================================================================
        // TYPES
        // ====================================================================

        /// Single excitation: promote one electron (occupied → virtual)
        [<Struct>]
        type SingleExcitation =
            {
                /// Virtual orbital index (unoccupied)
                VirtualOrbital: int

                /// Occupied orbital index
                OccupiedOrbital: int

                /// Excitation amplitude (variational parameter)
                Amplitude: float
            }

        /// Double excitation: promote two electrons
        type DoubleExcitation =
            {
                /// First virtual orbital
                VirtualOrbital1: int

                /// Second virtual orbital
                VirtualOrbital2: int

                /// First occupied orbital
                OccupiedOrbital1: int

                /// Second occupied orbital
                OccupiedOrbital2: int

                /// Excitation amplitude (variational parameter)
                Amplitude: float
            }

        /// UCCSD excitation pool (all possible excitations for given system)
        type ExcitationPool =
            {
                /// All single excitations
                Singles: SingleExcitation list

                /// All double excitations
                Doubles: DoubleExcitation list
            }

        // ====================================================================
        // EXCITATION GENERATORS
        // ====================================================================

        /// Generate single excitation operator: a†ₚ aᵧ - a†ᵧ aₚ
        ///
        /// This creates a fermionic term representing electron promotion
        /// from orbital q (occupied) to orbital p (virtual).
        ///
        /// **Parameters**:
        ///   p - Virtual orbital index (unoccupied in HF)
        ///   q - Occupied orbital index (occupied in HF)
        ///   amplitude - Excitation amplitude tₚᵧ
        ///
        /// **Returns**:
        ///   Two fermionic terms: +amplitude(a†ₚ aᵧ) and -amplitude(a†ᵧ aₚ)
        ///   The second term is the Hermitian conjugate (anti-Hermitian operator)
        let singleExcitationOperator (p: int) (q: int) (amplitude: float) : FermionTerm list =

            if p = q then
                [] // No excitation (same orbital)
            else
                // Forward excitation: a†ₚ aᵧ
                let forward: FermionTerm =
                    {
                        Coefficient = Complex(amplitude, 0.0)
                        Operators =
                            [
                                {
                                    OrbitalIndex = p
                                    OperatorType = Creation
                                }
                                {
                                    OrbitalIndex = q
                                    OperatorType = Annihilation
                                }
                            ]
                    }

                // Hermitian conjugate: -a†ᵧ aₚ (minus for anti-Hermitian)
                let backward: FermionTerm =
                    {
                        Coefficient = Complex(-amplitude, 0.0)
                        Operators =
                            [
                                {
                                    OrbitalIndex = q
                                    OperatorType = Creation
                                }
                                {
                                    OrbitalIndex = p
                                    OperatorType = Annihilation
                                }
                            ]
                    }

                [ forward; backward ]

        /// Generate double excitation operator: a†ₚ a†ᵧ aᵣ aₛ - a†ₛ a†ᵣ aᵧ aₚ
        ///
        /// This creates a fermionic term for promoting two electrons
        /// from orbitals (s,r) to orbitals (p,q).
        ///
        /// **Convention**: p > q (virtual), s > r (occupied)
        /// This ensures unique ordering (avoid double-counting)
        ///
        /// **Parameters**:
        ///   p - First virtual orbital (p > q)
        ///   q - Second virtual orbital
        ///   r - First occupied orbital (s > r)
        ///   s - Second occupied orbital
        ///   amplitude - Excitation amplitude tₚᵧᵣₛ
        ///
        /// **Returns**:
        ///   Two fermionic terms for anti-Hermitian operator
        let doubleExcitationOperator (p: int) (q: int) (r: int) (s: int) (amplitude: float) : FermionTerm list =

            // Validate ordering and distinct orbitals
            if p = q || r = s || Set.ofList [ p; q; r; s ] |> Set.count <> 4 then
                [] // Invalid excitation
            else
                // Forward: a†ₚ a†ᵧ aᵣ aₛ
                // Order: creation operators first (normal order)
                let forward: FermionTerm =
                    {
                        Coefficient = Complex(amplitude, 0.0)
                        Operators =
                            [
                                {
                                    OrbitalIndex = p
                                    OperatorType = Creation
                                }
                                {
                                    OrbitalIndex = q
                                    OperatorType = Creation
                                }
                                {
                                    OrbitalIndex = r
                                    OperatorType = Annihilation
                                }
                                {
                                    OrbitalIndex = s
                                    OperatorType = Annihilation
                                }
                            ]
                    }

                // Hermitian conjugate: -a†ₛ a†ᵣ aᵧ aₚ
                let backward: FermionTerm =
                    {
                        Coefficient = Complex(-amplitude, 0.0)
                        Operators =
                            [
                                {
                                    OrbitalIndex = s
                                    OperatorType = Creation
                                }
                                {
                                    OrbitalIndex = r
                                    OperatorType = Creation
                                }
                                {
                                    OrbitalIndex = q
                                    OperatorType = Annihilation
                                }
                                {
                                    OrbitalIndex = p
                                    OperatorType = Annihilation
                                }
                            ]
                    }

                [ forward; backward ]

        // ====================================================================
        // EXCITATION POOL GENERATION
        // ====================================================================

        /// Generate all possible single excitations for given occupation
        ///
        /// **Parameters**:
        ///   numElectrons - Number of electrons (occupied orbitals in HF)
        ///   numOrbitals - Total spin orbitals (occupied + virtual)
        ///   amplitudes - Excitation amplitudes (must have length = numElectrons × numVirtual)
        ///
        /// **Returns**:
        ///   List of single excitations with amplitudes
        ///
        /// **Example**:
        ///   H2: 2 electrons, 4 orbitals → 2 occupied, 2 virtual
        ///   Singles: (0→2), (0→3), (1→2), (1→3) = 4 excitations
        let generateSingles (numElectrons: int) (numOrbitals: int) (amplitudes: float[]) : SingleExcitation list =

            let numVirtual = numOrbitals - numElectrons
            let expectedParams = numElectrons * numVirtual

            if amplitudes.Length <> expectedParams then
                failwith $"Singles: expected {expectedParams} amplitudes, got {amplitudes.Length}"

            [
                for i in 0 .. numElectrons - 1 do
                    for a in numElectrons .. numOrbitals - 1 do
                        let paramIndex = i * numVirtual + (a - numElectrons)

                        {
                            OccupiedOrbital = i
                            VirtualOrbital = a
                            Amplitude = amplitudes.[paramIndex]
                        }
            ]

        /// Generate all possible double excitations
        ///
        /// **Parameters**:
        ///   numElectrons - Number of electrons
        ///   numOrbitals - Total spin orbitals
        ///   amplitudes - Excitation amplitudes
        ///
        /// **Returns**:
        ///   List of double excitations with amplitudes
        ///
        /// **Note**: Only generates unique excitations (i < j, a < b)
        /// to avoid double-counting.
        let generateDoubles (numElectrons: int) (numOrbitals: int) (amplitudes: float[]) : DoubleExcitation list =

            let numVirtual = numOrbitals - numElectrons

            // Number of unique pairs: C(n,2) = n(n-1)/2
            let numOccPairs = numElectrons * (numElectrons - 1) / 2
            let numVirtPairs = numVirtual * (numVirtual - 1) / 2
            let expectedParams = numOccPairs * numVirtPairs

            if amplitudes.Length <> expectedParams then
                failwith $"Doubles: expected {expectedParams} amplitudes, got {amplitudes.Length}"

            [
                for i in 0 .. numElectrons - 2 do
                    for j in i + 1 .. numElectrons - 1 do
                        for a in numElectrons .. numOrbitals - 2 do
                            for b in a + 1 .. numOrbitals - 1 do
                                (i, j, a, b)
            ]
            |> List.mapi (fun idx (i, j, a, b) ->
                {
                    OccupiedOrbital1 = i
                    OccupiedOrbital2 = j
                    VirtualOrbital1 = a
                    VirtualOrbital2 = b
                    Amplitude = amplitudes.[idx]
                })

        /// Generate complete UCCSD excitation pool
        ///
        /// **Parameters**:
        ///   numElectrons - Number of electrons in molecule
        ///   numOrbitals - Total number of spin orbitals
        ///   parameters - All UCCSD parameters (singles first, then doubles)
        ///
        /// **Returns**:
        ///   Complete excitation pool
        ///
        /// **Parameter Count**:
        ///   Singles: n_e × n_v
        ///   Doubles: C(n_e,2) × C(n_v,2)
        ///   Total: n_e×n_v + C(n_e,2)×C(n_v,2)
        let generateExcitationPool
            (numElectrons: int)
            (numOrbitals: int)
            (parameters: float[])
            : Result<ExcitationPool, string> =

            if numElectrons < 0 || numElectrons > numOrbitals then
                Error $"Invalid electron count: {numElectrons} electrons, {numOrbitals} orbitals"
            else
                let numVirtual = numOrbitals - numElectrons
                let numSingles = numElectrons * numVirtual

                let numDoubles =
                    let nOccPairs = numElectrons * (numElectrons - 1) / 2
                    let nVirtPairs = numVirtual * (numVirtual - 1) / 2
                    nOccPairs * nVirtPairs

                let totalParams = numSingles + numDoubles

                if parameters.Length <> totalParams then
                    Error
                        $"UCCSD: expected {totalParams} parameters ({numSingles} singles + {numDoubles} doubles), got {parameters.Length}"
                else
                    try
                        // Split parameters: singles first, then doubles
                        let singlesParams = parameters.[0 .. numSingles - 1]
                        let doublesParams = parameters.[numSingles .. totalParams - 1]

                        let singles = generateSingles numElectrons numOrbitals singlesParams
                        let doubles = generateDoubles numElectrons numOrbitals doublesParams

                        Ok { Singles = singles; Doubles = doubles }
                    with ex ->
                        Error $"UCCSD pool generation failed: {ex.Message}"

        // ====================================================================
        // UCCSD HAMILTONIAN CONSTRUCTION
        // ====================================================================

        /// Build UCCSD fermionic Hamiltonian from excitation pool
        ///
        /// **Parameters**:
        ///   pool - Excitation pool (singles + doubles)
        ///   numOrbitals - Total number of spin orbitals
        ///
        /// **Returns**:
        ///   Fermionic Hamiltonian representing UCCSD operator
        ///
        /// **Note**: This creates the T - T† operator in second quantization.
        /// To get the unitary U = exp(T - T†), this must be exponentiated
        /// using Trotter-Suzuki decomposition or exact diagonalization.
        let buildUCCSDHamiltonian (pool: ExcitationPool) (numOrbitals: int) : FermionHamiltonian =

            // Collect all fermionic terms from singles and doubles
            let singleTerms =
                pool.Singles
                |> List.collect (fun s -> singleExcitationOperator s.VirtualOrbital s.OccupiedOrbital s.Amplitude)

            let doubleTerms =
                pool.Doubles
                |> List.collect (fun d ->
                    doubleExcitationOperator
                        d.VirtualOrbital1
                        d.VirtualOrbital2
                        d.OccupiedOrbital1
                        d.OccupiedOrbital2
                        d.Amplitude)

            {
                NumOrbitals = numOrbitals
                Terms = singleTerms @ doubleTerms
            }

        // ====================================================================
        // PAULI DECOMPOSITION (via Jordan-Wigner or Bravyi-Kitaev)
        // ====================================================================

        /// Convert UCCSD excitations to qubit operators
        ///
        /// **Parameters**:
        ///   pool - UCCSD excitation pool
        ///   numOrbitals - Number of spin orbitals
        ///   mapping - Fermion-to-qubit mapping (JW or BK)
        ///
        /// **Returns**:
        ///   Qubit Hamiltonian (sum of Pauli strings)
        ///
        /// **Note**: Each fermionic excitation maps to multiple Pauli strings.
        /// - Single excitation → ~4 Pauli strings
        /// - Double excitation → ~16 Pauli strings
        let toQubitHamiltonian (pool: ExcitationPool) (numOrbitals: int) (useJordanWigner: bool) : QubitHamiltonian =

            // Build fermionic Hamiltonian
            let fermionHam = buildUCCSDHamiltonian pool numOrbitals

            // Transform to qubits using selected mapping
            if useJordanWigner then
                JordanWigner.transform fermionHam
            else
                BravyiKitaev.transform fermionHam

    // ====================================================================
    // HARTREE-FOCK INITIAL STATE
    // ====================================================================

    /// Hartree-Fock initial state preparation
    ///
    /// In quantum chemistry, the Hartree-Fock (HF) method gives the best
    /// single-determinant approximation to the ground state. For VQE,
    /// starting from the HF state leads to much faster convergence than
    /// starting from |0...0⟩.
    ///
    /// **HF State**: |11...100...0⟩ where first n electrons are |1⟩
    ///
    /// **Why This Matters**:
    /// - VQE convergence 10-100× faster than starting from |0⟩
    /// - Chemically reasonable initial guess
    /// - Standard practice in all quantum chemistry codes
    ///
    /// **Production Use**: Required for real-world VQE applications
    module HartreeFock =

        open FSharp.Azure.Quantum.Core.BackendAbstraction
        open FSharp.Azure.Quantum.Core.QuantumState
        open FSharp.Azure.Quantum.CircuitBuilder

        /// Prepare Hartree-Fock initial state |11...100...0⟩
        ///
        /// **Parameters**:
        ///   numElectrons - Number of electrons (determines occupied orbitals)
        ///   numOrbitals - Total number of spin-orbitals (qubits needed)
        ///   backend - Quantum backend for state preparation (RULE1)
        ///
        /// **Returns**:
        ///   Result<QuantumState, QuantumError> - HF state or error
        ///
        /// **Example**:
        /// ```fsharp
        /// // H2 molecule: 2 electrons, 4 orbitals
        /// let! hfState = HartreeFock.prepareHartreeFockState 2 4 backend
        /// // Result: |1100⟩ (qubits 0,1 occupied)
        /// ```
        let prepareHartreeFockState
            (numElectrons: int)
            (numOrbitals: int)
            (backend: IQuantumBackend)
            : Result<QuantumState, QuantumError> =

            result {
                // Validation
                if numElectrons < 0 then
                    return!
                        Error(QuantumError.ValidationError("numElectrons", "Number of electrons must be non-negative"))
                elif numElectrons > numOrbitals then
                    return!
                        Error(
                            QuantumError.ValidationError(
                                "numElectrons",
                                "Number of electrons cannot exceed number of orbitals"
                            )
                        )
                elif numOrbitals <= 0 then
                    return! Error(QuantumError.ValidationError("numOrbitals", "Number of orbitals must be positive"))
                else
                    // Initialize |0...0⟩ state
                    let! initialState = backend.InitializeState numOrbitals

                    // Apply X gates to first numElectrons qubits to get |11...100...0⟩
                    let xGates = [ for i in 0 .. numElectrons - 1 -> QuantumOperation.Gate(X i) ]

                    // Apply gates sequentially using fold
                    let! hfState =
                        (Ok initialState, xGates)
                        ||> List.fold (fun stateResult gate ->
                            result {
                                let! currentState = stateResult
                                return! backend.ApplyOperation gate currentState
                            })

                    return hfState
            }

        /// Check if a state is in Hartree-Fock configuration
        ///
        /// **Parameters**:
        ///   numElectrons - Expected number of electrons
        ///   state - Quantum state to check
        ///
        /// **Returns**:
        ///   true if state is |11...100...0⟩ (within numerical tolerance)
        let isHartreeFockState (numElectrons: int) (state: QuantumState) : bool =
            // Check if we have correct number of qubits
            let nQubits = numQubits state

            if nQubits < numElectrons then
                false
            else
                // Calculate expected bitstring
                // Bitstring is big-endian: [qN-1; qN-2; ...; q1; q0]
                // HF state has first numElectrons qubits (q0, q1, ..., q(n-1)) set to |1⟩
                // So we need 1s at the END of the array
                let expectedBitstring =
                    Array.init nQubits (fun i ->
                        // i=0 is highest qubit (qN-1), i=nQubits-1 is lowest (q0)
                        if i >= nQubits - numElectrons then 1 else 0)

                // Check if this basis state has probability ~1.0
                // (This is simulator-specific; on real hardware we'd use measurements)
                try
                    let prob = probability expectedBitstring state
                    abs (prob - 1.0) < 1e-10
                with _ ->
                    false // If we can't get probability, assume false

    // ====================================================================
    // CHEMISTRY VQE - UCCSD Ansatz Integration
    // ====================================================================

    /// VQE configuration for quantum chemistry with UCCSD ansatz
    module ChemistryVQE =

        open FSharp.Azure.Quantum.Core.BackendAbstraction
        open FSharp.Azure.Quantum.Core.QuantumState
        open FSharp.Azure.Quantum.CircuitBuilder
        open System.Numerics

        module Measurement = FSharp.Azure.Quantum.LocalSimulator.Measurement

        /// Helper: Sequence a list of Results into a Result of list
        module private ResultHelpers =
            let sequence (results: Result<'T, 'E> list) : Result<'T list, 'E> =
                List.foldBack
                    (fun result acc ->
                        match result, acc with
                        | Ok value, Ok values -> Ok(value :: values)
                        | Error e, _ -> Error e
                        | _, Error e -> Error e)
                    results
                    (Ok [])

        /// Ansatz type for VQE
        ///
        /// For hardware-efficient ansatz (HEA), use the general-purpose VQE module directly.
        /// This chemistry-specific VQE focuses on UCCSD, which guarantees chemical accuracy.
        type AnsatzType =
            /// UCCSD ansatz (chemistry-aware, guarantees chemical accuracy)
            | UCCSD of numElectrons: int * numOrbitals: int

        /// VQE configuration for quantum chemistry
        type ChemistryVQEConfig =
            {
                /// Hamiltonian to optimize
                Hamiltonian: QubitHamiltonian
                /// Ansatz type (UCCSD for chemistry-aware optimization)
                Ansatz: AnsatzType
                /// Maximum optimization iterations
                MaxIterations: int
                /// Convergence tolerance
                Tolerance: float
                /// Use Hartree-Fock initial state (recommended for chemistry)
                UseHFInitialState: bool
                /// Quantum backend
                Backend: IQuantumBackend
                /// Optional progress reporter
                ProgressReporter: Progress.IProgressReporter option
            }

        /// VQE result with chemistry metadata
        type ChemistryVQEResult =
            {
                /// Ground state energy (electronic energy only, no nuclear repulsion)
                Energy: float
                /// Optimal UCCSD parameters (excitation amplitudes)
                OptimalParameters: float[]
                /// Number of iterations to convergence
                Iterations: int
                /// Whether optimization converged
                Converged: bool
                /// Final quantum state; on the whole-circuit path, what the backend returned for
                /// the UCCSD circuit at OptimalParameters (measured frequencies on hardware)
                FinalState: QuantumState
                /// How the energies were estimated: exactly, or from samples (shots, circuits)
                Estimation: EnergyEstimation
                /// Caveats on Energy the other fields do not show (e.g. why a sampled run did
                /// not converge); empty when there are none
                Notes: string list
            }

        /// BFGS state of the UCCSD-VQE optimization loop
        type private OptimizationState =
            {
                Parameters: float array
                Energy: float
                Gradient: float array
                /// Inverse-Hessian approximation
                InverseHessian: float[,]
                /// InverseHessian is the identity (no curvature information yet)
                FreshHessian: bool
                Iteration: int
                FinalState: QuantumState
                Converged: bool
            }

        /// Jordan-Wigner image of the UCCSD cluster operator T - T† at the given amplitudes
        /// (singles first, then doubles, in the pool's order); the ansatz applies one Pauli
        /// rotation per term, in this order.
        let private clusterOperator (pool: UCCSD.ExcitationPool) (parameters: float[]) : QubitHamiltonian =
            let updatedPool: UCCSD.ExcitationPool =
                {
                    Singles = pool.Singles |> List.mapi (fun i s -> { s with Amplitude = parameters.[i] })
                    Doubles =
                        pool.Doubles
                        |> List.mapi (fun i d ->
                            { d with
                                Amplitude = parameters.[pool.Singles.Length + i]
                            })
                }

            let numOrbitals =
                if pool.Singles.IsEmpty && pool.Doubles.IsEmpty then
                    0
                else
                    let maxOrbital =
                        [
                            yield! pool.Singles |> List.map (fun s -> max s.VirtualOrbital s.OccupiedOrbital)
                            yield!
                                pool.Doubles
                                |> List.map (fun d ->
                                    [ d.VirtualOrbital1; d.VirtualOrbital2; d.OccupiedOrbital1; d.OccupiedOrbital2 ]
                                    |> List.max)
                        ]
                        |> List.max

                    maxOrbital + 1

            UCCSD.toQubitHamiltonian updatedPool numOrbitals true // Jordan-Wigner

        /// Build UCCSD ansatz circuit and apply to state
        ///
        /// **Parameters**:
        ///   pool - UCCSD excitation pool
        ///   parameters - Excitation amplitudes
        ///   initialState - Starting quantum state (HF or |0⟩)
        ///   backend - Quantum backend for gate application
        ///
        /// **Returns**:
        ///   Result<QuantumState, QuantumError> - State after UCCSD circuit
        let private buildUCCSDCircuit
            (pool: UCCSD.ExcitationPool)
            (parameters: float[])
            (initialState: QuantumState)
            (backend: IQuantumBackend)
            : Result<QuantumState, QuantumError> =

            result {
                // Validate parameter count
                let expectedParams = pool.Singles.Length + pool.Doubles.Length

                if parameters.Length <> expectedParams then
                    return!
                        Error(
                            QuantumError.ValidationError(
                                "parameters",
                                $"Expected {expectedParams} parameters, got {parameters.Length}"
                            )
                        )
                else
                    let qubitHam = clusterOperator pool parameters

                    // Apply UCCSD circuit using Pauli rotation gates
                    // For each Pauli string P with coefficient c, apply exp(i*c*P)
                    // This implements the Trotter approximation of exp(T - T†)

                    let! finalState =
                        (Ok initialState, qubitHam.Terms)
                        ||> List.fold (fun stateResult pauliTerm ->
                            result {
                                let! currentState = stateResult

                                // Skip identity terms (no rotation needed)
                                if pauliTerm.Operators.IsEmpty then
                                    return currentState
                                else
                                    // Apply rotation for this Pauli string: exp(i·θ·P).
                                    // The UCCSD cluster operator T − T† is anti-Hermitian, so its
                                    // Jordan–Wigner image has PURELY IMAGINARY Pauli coefficients
                                    // (c = i·θ, θ real = c.Imaginary). The CNOT-ladder + RZ(angle)
                                    // block below realises exp(−i·angle/2·P) (X by H, Y by RX(π/2),
                                    // both mapping to Z), so angle = −2·θ gives exp(i·θ·P) = e^(cP):
                                    // the amplitudes follow the standard U = e^(T − T†) convention.
                                    let angle = -2.0 * pauliTerm.Coefficient.Imaginary

                                    // For multi-qubit Pauli strings, we need to:
                                    // 1. Change basis (if X or Y)
                                    // 2. Apply CNOT ladder
                                    // 3. Apply single RZ rotation
                                    // 4. Undo CNOT ladder
                                    // 5. Undo basis change

                                    let qubits = pauliTerm.Operators |> Map.toList |> List.sortBy fst

                                    if qubits.Length = 1 then
                                        // Single-qubit Pauli rotation - direct application
                                        let (qubitIdx, pauli) = qubits.[0]

                                        let gate =
                                            match pauli with
                                            | QaoaCircuit.PauliOperator.PauliX -> RX(qubitIdx, angle)
                                            | QaoaCircuit.PauliOperator.PauliY -> RY(qubitIdx, angle)
                                            | QaoaCircuit.PauliOperator.PauliZ -> RZ(qubitIdx, angle)
                                            | QaoaCircuit.PauliOperator.PauliI ->
                                                // Identity - no gate needed, but shouldn't reach here
                                                RZ(qubitIdx, 0.0)

                                        return! backend.ApplyOperation (QuantumOperation.Gate gate) currentState

                                    else
                                        // Multi-qubit Pauli string - need basis change + entangling gates
                                        // For simplicity in MVP, we'll apply a simplified version
                                        // Full implementation would do proper Pauli string rotation

                                        // Step 1: Basis change for X and Y operators
                                        let! afterBasisChange =
                                            (Ok currentState, qubits)
                                            ||> List.fold (fun stRes (qubitIdx, pauli) ->
                                                result {
                                                    let! st = stRes

                                                    match pauli with
                                                    | QaoaCircuit.PauliOperator.PauliX ->
                                                        // Change to Z basis: H gate
                                                        return!
                                                            backend.ApplyOperation
                                                                (QuantumOperation.Gate(H qubitIdx))
                                                                st
                                                    | QaoaCircuit.PauliOperator.PauliY ->
                                                        // Change to Z basis: RX(π/2) Y RX(-π/2) = Z
                                                        let! afterRX =
                                                            backend.ApplyOperation
                                                                (QuantumOperation.Gate(RX(qubitIdx, Math.PI / 2.0)))
                                                                st

                                                        return afterRX
                                                    | QaoaCircuit.PauliOperator.PauliI
                                                    | QaoaCircuit.PauliOperator.PauliZ -> return st
                                                })

                                        // Step 2: CNOT ladder (entangle all qubits)
                                        let qubitIndices = qubits |> List.map fst

                                        let! afterCNOTs =
                                            if qubitIndices.Length > 1 then
                                                (Ok afterBasisChange, [ 0 .. qubitIndices.Length - 2 ])
                                                ||> List.fold (fun stRes i ->
                                                    result {
                                                        let! st = stRes
                                                        let control = qubitIndices.[i]
                                                        let target = qubitIndices.[i + 1]

                                                        return!
                                                            backend.ApplyOperation
                                                                (QuantumOperation.Gate(CNOT(control, target)))
                                                                st
                                                    })
                                            else
                                                Ok afterBasisChange

                                        // Step 3: Single RZ rotation on last qubit
                                        let lastQubit = qubitIndices.[qubitIndices.Length - 1]

                                        let! afterRotation =
                                            backend.ApplyOperation
                                                (QuantumOperation.Gate(RZ(lastQubit, angle)))
                                                afterCNOTs

                                        // Step 4: Undo CNOT ladder
                                        let! afterUndoCNOTs =
                                            if qubitIndices.Length > 1 then
                                                (Ok afterRotation, [ qubitIndices.Length - 2 .. -1 .. 0 ])
                                                ||> List.fold (fun stRes i ->
                                                    result {
                                                        let! st = stRes
                                                        let control = qubitIndices.[i]
                                                        let target = qubitIndices.[i + 1]

                                                        return!
                                                            backend.ApplyOperation
                                                                (QuantumOperation.Gate(CNOT(control, target)))
                                                                st
                                                    })
                                            else
                                                Ok afterRotation

                                        // Step 5: Undo basis change
                                        let! afterUndoBasis =
                                            (Ok afterUndoCNOTs, qubits |> List.rev)
                                            ||> List.fold (fun stRes (qubitIdx, pauli) ->
                                                result {
                                                    let! st = stRes

                                                    match pauli with
                                                    | QaoaCircuit.PauliOperator.PauliX ->
                                                        return!
                                                            backend.ApplyOperation
                                                                (QuantumOperation.Gate(H qubitIdx))
                                                                st
                                                    | QaoaCircuit.PauliOperator.PauliY ->
                                                        return!
                                                            backend.ApplyOperation
                                                                (QuantumOperation.Gate(RX(qubitIdx, -Math.PI / 2.0)))
                                                                st
                                                    | QaoaCircuit.PauliOperator.PauliI
                                                    | QaoaCircuit.PauliOperator.PauliZ -> return st
                                                })

                                        return afterUndoBasis
                            })

                    return finalState
            }

        /// Samples per Pauli term when a gate-by-gate backend's states are not state vectors.
        [<Literal>]
        let private shotsPerTerm = 1000

        /// Measure energy expectation value ⟨ψ|H|ψ⟩
        ///
        /// Measures each Pauli term separately by:
        /// 1. Applying basis-change gates (H for X, RX(π/2) for Y)
        /// 2. Measuring in computational basis
        /// 3. Computing expectation value from measurement statistics
        ///
        /// On a statevector the expectation is exact and there is no readout to mitigate.
        /// Otherwise it is sampled, and `errorMitigation` corrects each term's histogram;
        /// a strategy that fails or performs no correction is an Error.
        let private measureEnergy
            (errorMitigation: ErrorMitigationStrategy.RecommendedStrategy option)
            (hamiltonian: QubitHamiltonian)
            (state: QuantumState)
            (backend: IQuantumBackend)
            : Result<float, QuantumError> =

            result {
                // Measure expectation value of each Pauli string
                let! energyContributions =
                    hamiltonian.Terms
                    |> List.map (fun pauliTerm ->
                        result {
                            // Apply basis-change gates to measure in Pauli X/Y basis
                            let qubits = pauliTerm.Operators |> Map.toList

                            // Step 1: Apply basis-change gates
                            let! basisChangedState =
                                (Ok state, qubits)
                                ||> List.fold (fun stRes (qubitIdx, pauli) ->
                                    result {
                                        let! st = stRes

                                        match pauli with
                                        | QaoaCircuit.PauliOperator.PauliX ->
                                            // Measure X: apply H before measurement
                                            return! backend.ApplyOperation (QuantumOperation.Gate(H qubitIdx)) st
                                        | QaoaCircuit.PauliOperator.PauliY ->
                                            // Measure Y: apply RX(π/2), which maps Y to Z
                                            return!
                                                backend.ApplyOperation
                                                    (QuantumOperation.Gate(RX(qubitIdx, Math.PI / 2.0)))
                                                    st
                                        | QaoaCircuit.PauliOperator.PauliI
                                        | QaoaCircuit.PauliOperator.PauliZ ->
                                            // Z and I: no basis change needed
                                            return st
                                    })

                            // Steps 2 & 3: expectation of the (now all-Z) Pauli string.
                            // After the basis change every operator is measured in the Z
                            // basis, so the eigenvalue of basis state |i⟩ is the parity
                            // (-1)^(popcount of i over the operator's qubits).
                            let qubitIndices = pauliTerm.Operators |> Map.toList |> List.map fst

                            let parityOf (basisIndex: int) =
                                qubitIndices
                                |> List.fold
                                    (fun acc q -> acc * (if ((basisIndex >>> q) &&& 1) = 0 then 1.0 else -1.0))
                                    1.0

                            // On a statevector simulator compute the expectation EXACTLY from
                            // the amplitudes. Shot sampling (1000 shots) injects ~1/√N noise that
                            // is fatal to the finite-difference VQE gradients (noise/ε dominates
                            // the true gradient), so only fall back to sampling for backends that
                            // are not full statevector simulators (e.g. real hardware).
                            let! expectation =
                                match basisChangedState with
                                | QuantumState.StateVector sv ->
                                    Measurement.getProbabilityDistribution sv
                                    |> Array.mapi (fun i p -> p * parityOf i)
                                    |> Array.sum
                                    |> Ok
                                | _ ->
                                    let shots = shotsPerTerm

                                    // Bitstring key in ReadoutErrorMitigation's convention: most
                                    // significant qubit first, so qubit q is character n-1-q.
                                    let n = numQubits basisChangedState

                                    let histogram =
                                        measure basisChangedState shots
                                        |> Array.countBy (fun bits ->
                                            bits |> Array.rev |> Array.map string |> String.concat "")
                                        |> Map.ofArray

                                    let parityOfKey (key: string) =
                                        qubitIndices
                                        |> List.fold
                                            (fun acc q -> acc * (if key.[n - 1 - q] = '0' then 1.0 else -1.0))
                                            1.0

                                    // An expectation value needs the whole corrected distribution:
                                    // no clipping of negative quasi-probabilities, no small-count filter.
                                    let unbiased =
                                        { ReadoutErrorMitigation.defaultConfig with
                                            ClipNegative = false
                                            MinProbability = 0.0
                                        }

                                    let weighted =
                                        match errorMitigation with
                                        | None -> Ok(histogram |> Map.map (fun _ count -> float count))
                                        | Some strategy ->
                                            match
                                                ErrorMitigationStrategy.applyStrategyWith unbiased histogram strategy
                                            with
                                            | Ok mitigated when mitigated.CorrectionApplied -> Ok mitigated.Histogram
                                            | Ok _ ->
                                                Error(
                                                    QuantumError.ValidationError(
                                                        "ErrorMitigation",
                                                        "the strategy performed no correction (readout mitigation without a calibration matrix)"
                                                    )
                                                )
                                            | Error err -> Error err

                                    weighted
                                    |> Result.bind (fun counts ->
                                        let total = counts |> Map.toSeq |> Seq.sumBy snd

                                        if total <= 0.0 then
                                            Error(
                                                QuantumError.OperationError(
                                                    "ChemistryVQE",
                                                    "mitigated histogram has no positive weight"
                                                )
                                            )
                                        else
                                            Ok(
                                                (counts |> Map.toSeq |> Seq.sumBy (fun (k, c) -> c * parityOfKey k))
                                                / total
                                            ))

                            return pauliTerm.Coefficient.Real * expectation
                        })
                    |> ResultHelpers.sequence

                return energyContributions |> List.sum
            }

        // ================================================================
        // WHOLE-CIRCUIT (SAMPLED) PATH: backends that cannot apply gates one at a time
        // ================================================================

        /// Gates of e^(cP) = exp(i·θ·P) for one Pauli string c·P of the cluster operator T − T†
        /// (c = i·θ), in program order: the same rotation the gate-by-gate path applies. X and Y
        /// are mapped to Z by H and RX(π/2) (RX(π/2) Y RX(-π/2) = Z) around the CNOT ladder and RZ.
        let private pauliRotationGates (term: PauliString) : Gate list =
            let angle = -2.0 * term.Coefficient.Imaginary

            let qubits =
                term.Operators
                |> Map.toList
                |> List.filter (fun (_, p) -> p <> QaoaCircuit.PauliOperator.PauliI)
                |> List.sortBy fst

            match qubits with
            | [] -> []
            | [ (q, QaoaCircuit.PauliOperator.PauliX) ] -> [ RX(q, angle) ]
            | [ (q, QaoaCircuit.PauliOperator.PauliY) ] -> [ RY(q, angle) ]
            | [ (q, _) ] -> [ RZ(q, angle) ]
            | _ ->
                let toZ =
                    qubits
                    |> List.choose (fun (q, p) ->
                        match p with
                        | QaoaCircuit.PauliOperator.PauliX -> Some(H q)
                        | QaoaCircuit.PauliOperator.PauliY -> Some(RX(q, Math.PI / 2.0))
                        | QaoaCircuit.PauliOperator.PauliI
                        | QaoaCircuit.PauliOperator.PauliZ -> None)

                let fromZ =
                    qubits
                    |> List.rev
                    |> List.choose (fun (q, p) ->
                        match p with
                        | QaoaCircuit.PauliOperator.PauliX -> Some(H q)
                        | QaoaCircuit.PauliOperator.PauliY -> Some(RX(q, -Math.PI / 2.0))
                        | QaoaCircuit.PauliOperator.PauliI
                        | QaoaCircuit.PauliOperator.PauliZ -> None)

                let indices = qubits |> List.map fst |> Array.ofList

                let ladder =
                    [ for i in 0 .. indices.Length - 2 -> CNOT(indices.[i], indices.[i + 1]) ]

                toZ
                @ ladder
                @ [ RZ(indices.[indices.Length - 1], angle) ]
                @ List.rev ladder
                @ fromZ

        /// The complete UCCSD circuit for `numElectrons` electrons in `numSpinOrbitals` spin
        /// orbitals (one qubit each, Jordan-Wigner): X on the occupied orbitals (the Hartree-Fock
        /// reference), then one Trotterised Pauli-string rotation e^(cP) per term of T − T†
        /// (basis change, CNOT ladder, RZ, uncompute): a first-order Trotter product of
        /// U = e^(T − T†), so amplitudes follow the usual coupled-cluster sign convention (e.g.
        /// MP2 or CCSD amplitudes from a chemistry package). Parameters are the excitation
        /// amplitudes, singles first; a wrong count is an Error. Gates are H, X, RX, RY, RZ and CNOT, which
        /// every cloud target and the OpenQASM exporter accept.
        let uccsdCircuit
            (numElectrons: int)
            (numSpinOrbitals: int)
            (parameters: float[])
            : Result<CircuitBuilder.Circuit, QuantumError> =
            UCCSD.generateExcitationPool numElectrons numSpinOrbitals parameters
            |> Result.mapError (fun msg -> QuantumError.OperationError("UCCSD", msg))
            |> Result.bind (fun pool ->
                let expected = pool.Singles.Length + pool.Doubles.Length

                if parameters.Length <> expected then
                    Error(
                        QuantumError.ValidationError(
                            "parameters",
                            $"Expected {expected} parameters, got {parameters.Length}"
                        )
                    )
                else
                    let reference = [ for i in 0 .. numElectrons - 1 -> X i ]

                    let rotations =
                        (clusterOperator pool parameters).Terms |> List.collect pauliRotationGates

                    Ok(
                        CircuitBuilder.empty numSpinOrbitals
                        |> CircuitBuilder.addGates (reference @ rotations)
                    ))

        /// Pauli terms measured together from one circuit: they are qubit-wise commuting, so a
        /// single basis rotation per qubit diagonalises all of them.
        type MeasurementGroup =
            {
                /// Measured Pauli per qubit (X, Y or Z); qubits no term acts on are absent
                Basis: Map<int, QaoaCircuit.PauliOperator>
                /// Terms estimated from this group's circuit
                Terms: PauliString list
            }

        /// Qubit-wise commuting groups of the Hamiltonian's terms, first fit with the terms taken
        /// widest first (most non-identity qubits; ties in term order): each term joins the
        /// first group whose basis agrees with it on every qubit both act on. Every term is in
        /// exactly one group; identity terms go to the first group. Terms whose coefficient is
        /// below 1e-10 in magnitude (symmetry-forbidden terms that are zero up to rounding)
        /// change no energy measurably and are left out rather than given circuits of their
        /// own. For H2/STO-3G that leaves 15 terms in 5 groups: the Z terms, and each of the
        /// four XXYY-type terms.
        let measurementGroups (hamiltonian: QubitHamiltonian) : MeasurementGroup list =
            let actsOn (term: PauliString) =
                term.Operators |> Map.filter (fun _ p -> p <> QaoaCircuit.PauliOperator.PauliI)

            let fits (group: MeasurementGroup) (term: PauliString) =
                actsOn term
                |> Map.forall (fun q p ->
                    match group.Basis.TryFind q with
                    | Some b -> b = p
                    | None -> true)

            hamiltonian.Terms
            |> List.filter (fun term -> term.Coefficient.Magnitude >= 1e-10)
            |> List.sortBy (fun term -> -(actsOn term).Count)
            |> List.fold
                (fun (groups: MeasurementGroup list) term ->
                    match groups |> List.tryFindIndex (fun g -> fits g term) with
                    | Some index ->
                        groups
                        |> List.mapi (fun i g ->
                            if i = index then
                                {
                                    Basis = actsOn term |> Map.fold (fun basis q p -> Map.add q p basis) g.Basis
                                    Terms = g.Terms @ [ term ]
                                }
                            else
                                g)
                    | None ->
                        groups
                        @ [
                            {
                                Basis = actsOn term
                                Terms = [ term ]
                            }
                        ])
                []

        /// `preparation` followed by the rotations that map the group's basis to Z: H for X,
        /// RX(π/2) for Y (RX(π/2) Y RX(-π/2) = Z).
        let measurementCircuit
            (preparation: CircuitBuilder.Circuit)
            (group: MeasurementGroup)
            : CircuitBuilder.Circuit =
            let rotations =
                group.Basis
                |> Map.toList
                |> List.choose (fun (q, p) ->
                    match p with
                    | QaoaCircuit.PauliOperator.PauliX -> Some(H q)
                    | QaoaCircuit.PauliOperator.PauliY -> Some(RX(q, Math.PI / 2.0))
                    | QaoaCircuit.PauliOperator.PauliI | QaoaCircuit.PauliOperator.PauliZ -> None)

            preparation |> CircuitBuilder.addGates rotations

        /// An energy estimated from whole circuits.
        type SampledEnergy =
            {
                /// Σ c·⟨P⟩ over the Hamiltonian's terms (real parts of the coefficients)
                Energy: float
                /// Shot-noise standard error of Energy, when the backend reports its shot count
                /// and no readout correction was applied; None otherwise
                StandardError: float option
                /// Circuits executed for this estimate: one per measurement group
                Circuits: int
                /// The backend's shots per circuit (IShotSamplingBackend), when it reports them
                ShotsPerCircuit: int option
            }

        /// Outcome probabilities of a returned state: (basis index with bit q = qubit q, p).
        /// Basis indices are Int32, so a measured histogram wider than 31 qubits is an Error.
        let internal outcomeDistribution (state: QuantumState) : Result<(int * float)[], QuantumError> =
            match state with
            | QuantumState.StateVector sv ->
                Measurement.getProbabilityDistribution sv
                |> Array.mapi (fun i p -> i, p)
                |> Array.filter (fun (_, p) -> p > 0.0)
                |> Ok
            | QuantumState.DensityMatrix(rho, n) ->
                Array.init (1 <<< n) (fun i -> i, rho.[i, i].Real)
                |> Array.filter (fun (_, p) -> p > 0.0)
                |> Ok
            | QuantumState.SparseState(amplitudes, _) ->
                amplitudes
                |> Map.toArray
                |> Array.map (fun (i, a) -> i, a.Magnitude * a.Magnitude)
                |> Ok
            | QuantumState.MeasurementHistogram(_, n) when n > 31 ->
                Error(
                    QuantumError.ValidationError(
                        "MeasurementHistogram",
                        $"{n} measured qubits do not fit a 31-bit basis index; expectation values here index outcomes as Int32"
                    )
                )
            | QuantumState.MeasurementHistogram(histogram, _) ->
                // Keys: character q = qubit q.
                let total = histogram |> Map.fold (fun acc _ c -> acc + max 0 c) 0 |> float

                histogram
                |> Map.toArray
                |> Array.map (fun (key, count) ->
                    let index = key |> Seq.mapi (fun q c -> if c = '1' then 1 <<< q else 0) |> Seq.sum

                    index, float (max 0 count) / max 1.0 total)
                |> Ok
            | other ->
                Error(
                    QuantumError.NotImplemented(
                        "UCCSD-VQE on whole circuits",
                        Some
                            $"the backend returned a {QuantumState.stateType other} state, which has no outcome distribution"
                    )
                )

        /// Energy from one executed circuit per measurement group (see sampledExpectation),
        /// calling `guard` before each circuit (an Error stops the estimate), and the shot-noise
        /// standard error of the raw (uncorrected) counts: 0 when the backend reports no shots.
        let private estimateGroups
            (guard: unit -> Result<unit, QuantumError>)
            (backend: IQuantumBackend)
            (errorMitigation: ErrorMitigationStrategy.RecommendedStrategy option)
            (preparation: CircuitBuilder.Circuit)
            (groups: MeasurementGroup list)
            : Result<SampledEnergy * float, QuantumError> =
            let shots =
                match backend with
                | :? IShotSamplingBackend as sampling when sampling.Shots > 0 -> Some sampling.Shots
                | _ -> None

            let n = preparation.QubitCount

            // Readout correction needs integer counts: the backend's own when it reports its
            // shots, otherwise the returned probabilities scaled to 10^6 counts.
            let countScale = shots |> Option.defaultValue 1_000_000 |> float

            let unbiased =
                { ReadoutErrorMitigation.defaultConfig with
                    ClipNegative = false
                    MinProbability = 0.0
                }

            let key (index: int) =
                Convert.ToString(index, 2).PadLeft(n, '0')

            let parity (term: PauliString) (index: int) =
                term.Operators
                |> Map.fold
                    (fun acc q p ->
                        if p <> QaoaCircuit.PauliOperator.PauliI && ((index >>> q) &&& 1) = 1 then
                            -acc
                        else
                            acc)
                    1.0

            /// (weights by basis index, whether a correction ran) for one group's outcomes.
            let weightsOf (distribution: (int * float)[]) : Result<(int * float)[] * bool, QuantumError> =
                match errorMitigation with
                | None -> Ok(distribution, false)
                | Some strategy ->
                    let histogram =
                        distribution
                        |> Array.map (fun (i, p) -> key i, int (Math.Round(p * countScale)))
                        |> Array.filter (fun (_, c) -> c > 0)
                        |> Map.ofArray

                    match ErrorMitigationStrategy.applyStrategyWith unbiased histogram strategy with
                    | Ok mitigated when mitigated.CorrectionApplied ->
                        let total = mitigated.Histogram |> Map.toSeq |> Seq.sumBy snd

                        Ok(
                            mitigated.Histogram
                            |> Map.toArray
                            |> Array.map (fun (k, c) -> Convert.ToInt32(k, 2), c / total),
                            true
                        )
                    | Ok _ ->
                        Error(
                            QuantumError.ValidationError(
                                "ErrorMitigation",
                                "the strategy performed no correction (readout mitigation without a calibration matrix)"
                            )
                        )
                    | Error err -> Error err

            let moments (group: MeasurementGroup) (weights: (int * float)[]) =
                let valueAt index =
                    group.Terms |> List.sumBy (fun t -> t.Coefficient.Real * parity t index)

                let mean = weights |> Array.sumBy (fun (i, w) -> w * valueAt i)
                let second = weights |> Array.sumBy (fun (i, w) -> w * (valueAt i) ** 2.0)
                mean, max 0.0 (second - mean * mean)

            groups
            |> List.map (fun group ->
                guard ()
                |> Result.bind (fun () ->
                    backend.ExecuteToState(CircuitAbstraction.wrapCircuit (measurementCircuit preparation group)))
                |> Result.bind outcomeDistribution
                |> Result.bind (fun distribution ->
                    weightsOf distribution
                    |> Result.map (fun (weights, corrected) ->
                        let mean, _ = moments group weights
                        let _, rawVariance = moments group distribution
                        mean, rawVariance, corrected)))
            |> ResultHelpers.sequence
            |> Result.map (fun perGroup ->
                let anyCorrected = perGroup |> List.exists (fun (_, _, c) -> c)

                let noise =
                    match shots with
                    | Some s -> sqrt ((perGroup |> List.sumBy (fun (_, v, _) -> v)) / float s)
                    | None -> 0.0

                {
                    Energy = perGroup |> List.sumBy (fun (e, _, _) -> e)
                    StandardError =
                        match shots with
                        | Some _ when not anyCorrected -> Some noise
                        | _ -> None
                    Circuits = groups.Length
                    ShotsPerCircuit = shots
                },
                noise)

        /// ⟨H⟩ in the state `preparation` leaves, estimated from whole circuits: one circuit per
        /// qubit-wise commuting group of terms (measurementGroups), each executed with
        /// ExecuteToState, the backend's returned outcome frequencies weighting every term's
        /// parity. With `errorMitigation`, each group's counts are corrected with the unbiased
        /// readout inverse (negative quasi-probabilities kept); a strategy that cannot correct
        /// counts is an Error.
        let sampledExpectation
            (backend: IQuantumBackend)
            (errorMitigation: ErrorMitigationStrategy.RecommendedStrategy option)
            (preparation: CircuitBuilder.Circuit)
            (hamiltonian: QubitHamiltonian)
            : Result<SampledEnergy, QuantumError> =
            estimateGroups (fun () -> Ok()) backend errorMitigation preparation (measurementGroups hamiltonian)
            |> Result.map fst

        /// Most circuits one whole-circuit UCCSD-VQE run may submit. Each is a separate job on
        /// cloud hardware; the run is refused up front when its plan needs more.
        [<Literal>]
        let MaxWholeCircuitJobs = 20_000

        /// Longest projected wall-clock time, in seconds, of a whole-circuit UCCSD-VQE run on a
        /// backend that reports no shot count (a local whole-circuit simulator such as
        /// NoisyLocalBackend): the run is refused after its first circuit when the plan, at
        /// that circuit's time, would take longer.
        [<Literal>]
        let MaxWholeCircuitSimulationSeconds = 3600.0

        /// UCCSD-VQE on a backend that runs only whole circuits, by SPSA (simultaneous
        /// perturbation stochastic approximation, Spall 1998).
        ///
        /// Why SPSA: every energy costs one submitted circuit per measurement group, and on
        /// hardware that submission (queueing, per-job cost) dominates. SPSA needs two energies
        /// per iteration whatever the parameter count, and tolerates the shot noise in them.
        /// Parameter-shift gradients need two energies per Pauli rotation, hundreds per
        /// iteration for a (4e,4o) space, and a line search on noisy energies stalls.
        ///
        /// Gains (Spall's rules): a_k = a/(k+1+A)^0.602, c_k = c/(k+1)^0.101, A = MaxIterations/10,
        /// c = min(0.1, 0.35/√n), and `a` calibrated from four gradient samples at the start so
        /// that the first step changes each of the n amplitudes by 0.05/√n: small enough that a
        /// 52-amplitude (4e,4o) ansatz does not leap uphill, large enough for H2's 0.1 amplitude.
        ///
        /// Convergence: every 10 iterations the energy is estimated at the average of those
        /// iterates (a checkpoint), with its shot-noise standard error σ. Converged means three
        /// checkpoints in a row did not improve on the best one by more than
        /// max(Tolerance, 2√(σ² + σ_best²)): no improvement above the noise for 30 iterations.
        /// Otherwise the run stops at MaxIterations, not Converged. The result is the best
        /// checkpoint's parameters with a fresh energy estimate there (the best of several
        /// noisy estimates is biased low); if that is significantly above the starting energy,
        /// the starting amplitudes are returned instead. Notes say which.
        ///
        /// Budget: the plan's circuit count, groups × (8 calibration + 2 per iteration + one
        /// per checkpoint + start + final) + 1, must not exceed MaxWholeCircuitJobs; on a
        /// backend without a shot count the first circuit's time projects the run against
        /// MaxWholeCircuitSimulationSeconds. The progress reporter's cancellation is checked
        /// before every circuit.
        let private runSampled
            (initialParameters: float[])
            (errorMitigation: ErrorMitigationStrategy.RecommendedStrategy option)
            (config: ChemistryVQEConfig)
            (numElectrons: int)
            (numOrbitals: int)
            : Result<ChemistryVQEResult, QuantumError> =
            let groups = measurementGroups config.Hamiltonian
            let n = initialParameters.Length
            let maxIterations = max 0 config.MaxIterations
            let checkEvery = 10
            let calibrationSamples = 4

            let circuitsFor iterations =
                groups.Length
                * (2 * calibrationSamples + 2 * iterations + iterations / checkEvery + 2)
                + 1

            let plannedCircuits = circuitsFor maxIterations

            let executed = ref 0
            let clock = Diagnostics.Stopwatch()

            let sampling =
                match config.Backend with
                | :? IShotSamplingBackend as b -> b.Shots > 0
                | _ -> false

            /// Cancellation, and the time projection after the first circuit.
            let guard () =
                if config.ProgressReporter |> Option.exists (fun r -> r.IsCancellationRequested) then
                    Error(QuantumError.OperationError("UCCSD-VQE", "cancelled by the progress reporter"))
                else
                    let projected =
                        if executed.Value = 1 then
                            clock.Elapsed.TotalSeconds * float plannedCircuits
                        else
                            0.0

                    if not sampling && projected > MaxWholeCircuitSimulationSeconds then
                        Error(
                            QuantumError.ValidationError(
                                "MaxIterations",
                                $"the first circuit took {clock.Elapsed.TotalSeconds:F1} s, so the {plannedCircuits} planned circuits would take about {projected / 3600.0:F1} h (limit {MaxWholeCircuitSimulationSeconds / 3600.0:F1} h on a local whole-circuit simulator). Lower MaxIterations or use a smaller active space."
                            )
                        )
                    else
                        if executed.Value = 0 then
                            clock.Start()

                        executed.Value <- executed.Value + 1
                        Ok()

            let estimate (parameters: float[]) =
                uccsdCircuit numElectrons numOrbitals parameters
                |> Result.bind (fun circuit -> estimateGroups guard config.Backend errorMitigation circuit groups)

            let energyAt parameters =
                estimate parameters |> Result.map (fst >> fun e -> e.Energy)

            let report iteration energy =
                config.ProgressReporter
                |> Option.iter (fun r ->
                    r.Report(Progress.IterationUpdate(iteration, config.MaxIterations, Some energy)))

            let rng = Random 42

            /// SPSA gradient estimate and the mean of the two energies.
            let gradientAt (parameters: float[]) (c: float) =
                let delta = Array.init n (fun _ -> if rng.Next 2 = 0 then -1.0 else 1.0)

                let shifted sign =
                    Array.map2 (fun x d -> x + sign * c * d) parameters delta

                energyAt (shifted 1.0)
                |> Result.bind (fun plus ->
                    energyAt (shifted -1.0)
                    |> Result.map (fun minus ->
                        delta |> Array.map (fun d -> (plus - minus) / (2.0 * c) * d), 0.5 * (plus + minus)))

            let alpha, gamma = 0.602, 0.101
            let stability = float maxIterations / 10.0
            let c0 = min 0.1 (0.35 / sqrt (float (max 1 n)))
            let firstStep = 0.05 / sqrt (float (max 1 n))

            /// (energy, σ, parameters) of a checkpoint.
            let checkpoint (parameters: float[]) =
                estimate parameters
                |> Result.map (fun (e, noise) -> e.Energy, noise, parameters)

            let significant (e: float, s: float) (best: float, sBest: float) =
                e < best - max config.Tolerance (2.0 * sqrt (s * s + sBest * sBest))

            let finish (startEnergy, startNoise, _) (_: float, _: float, best: float[]) iterations converged =
                estimate best
                |> Result.bind (fun (final, finalNoise) ->
                    let uphill =
                        iterations > 0
                        && final.Energy > startEnergy + 2.0 * sqrt (finalNoise * finalNoise + startNoise * startNoise)

                    let parameters, energy, notes =
                        if uphill then
                            initialParameters,
                            startEnergy,
                            [
                                $"SPSA ended above the starting energy ({final.Energy:F6} vs {startEnergy:F6} Ha, beyond the shot noise): the starting amplitudes and their energy are returned. Raise MaxIterations or start from better amplitudes."
                            ]
                        elif converged then
                            best, final.Energy, []
                        elif iterations > 0 && obj.ReferenceEquals(best, initialParameters) then
                            best,
                            final.Energy,
                            [
                                $"SPSA found no checkpoint below the starting energy within MaxIterations ({iterations}): the starting amplitudes are returned."
                            ]
                        elif iterations > 0 then
                            best,
                            final.Energy,
                            [
                                $"SPSA stopped at MaxIterations ({iterations}) while still improving above the shot noise: Energy may lie well above the minimum."
                            ]
                        else
                            best, final.Energy, []

                    uccsdCircuit numElectrons numOrbitals parameters
                    |> Result.bind (fun circuit ->
                        guard ()
                        |> Result.bind (fun () ->
                            config.Backend.ExecuteToState(CircuitAbstraction.wrapCircuit circuit)))
                    |> Result.map (fun state ->
                        {
                            Energy = energy
                            OptimalParameters = parameters
                            Iterations = iterations
                            Converged = converged && not uphill
                            FinalState = state
                            Estimation = SampledCircuits(groups.Length, final.ShotsPerCircuit, executed.Value)
                            Notes = notes
                        }))

            let rec iterate
                k
                (parameters: float[])
                (window: float[] list)
                start
                (best: float * float * float[])
                stale
                a
                =
                if stale >= 3 then
                    finish start best k true
                elif k >= maxIterations then
                    finish start best k false
                else
                    let ak = a / (float (k + 1) + stability) ** alpha
                    let ck = c0 / float (k + 1) ** gamma

                    match gradientAt parameters ck with
                    | Error e -> Error e
                    | Ok(gradient, energy) ->
                        report (k + 1) energy
                        let next = Array.map2 (fun x g -> x - ak * g) parameters gradient
                        let window = next :: window

                        if (k + 1) % checkEvery <> 0 then
                            iterate (k + 1) next window start best stale a
                        else
                            let averaged = Array.init n (fun i -> window |> List.averageBy (fun p -> p.[i]))

                            match checkpoint averaged with
                            | Error e -> Error e
                            | Ok((e, s, _) as candidate) ->
                                let bestEnergy, bestNoise, _ = best
                                let improved = significant (e, s) (bestEnergy, bestNoise)
                                let best = if e < bestEnergy then candidate else best
                                iterate (k + 1) next [] start best (if improved then 0 else stale + 1) a

            if plannedCircuits > MaxWholeCircuitJobs then
                let perIteration = 2 * groups.Length

                let fits =
                    Seq.initInfinite id
                    |> Seq.takeWhile (fun k -> circuitsFor k <= MaxWholeCircuitJobs)
                    |> Seq.fold (fun _ k -> k) 0

                Error(
                    QuantumError.ValidationError(
                        "MaxIterations",
                        $"whole-circuit UCCSD-VQE would submit {plannedCircuits} circuits ({groups.Length} per energy, {perIteration} per SPSA iteration) for {maxIterations} iterations; the limit is ChemistryVQE.MaxWholeCircuitJobs = {MaxWholeCircuitJobs}. Use MaxIterations <= {fits} or a smaller active space."
                    )
                )
            else
                checkpoint initialParameters
                |> Result.bind (fun start ->
                    let startEnergy, _, _ = start
                    report 0 startEnergy

                    if n = 0 || maxIterations = 0 then
                        finish start start 0 (n = 0)
                    else
                        [ 1..calibrationSamples ]
                        |> List.map (fun _ -> gradientAt initialParameters c0)
                        |> ResultHelpers.sequence
                        |> Result.bind (fun samples ->
                            let magnitude = samples |> List.averageBy (fun (g, _) -> g |> Array.averageBy abs)

                            let a =
                                if magnitude > 1e-12 then
                                    firstStep * (1.0 + stability) ** alpha / magnitude
                                else
                                    firstStep * (1.0 + stability) ** alpha

                            iterate 0 initialParameters [] start start 0 a))

        /// Run UCCSD-VQE to find molecular ground state, starting from the given
        /// excitation amplitudes.
        ///
        /// **Parameters**:
        ///   initialParameters - Starting UCCSD amplitudes (singles first, then doubles);
        ///                       None starts from small seeded random values near zero.
        ///                       A length other than the ansatz's parameter count is an Error.
        ///   errorMitigation - Correction applied to sampled measurement histograms (backends
        ///                     whose states are not statevectors); unused on statevectors,
        ///                     whose expectations are exact.
        ///   config - VQE configuration with UCCSD ansatz
        ///
        /// Backends that apply gates one at a time run the exact path: BFGS on central-difference
        /// gradients, energies exact on state vectors. Backends that refuse incremental
        /// ApplyOperation (cloud hardware, NoisyLocalBackend) run whole circuits instead
        /// (uccsdCircuit, measurementGroups, sampledExpectation) optimised by SPSA; see
        /// ChemistryVQEResult.Estimation.
        ///
        /// **Returns**:
        ///   Async<Result<ChemistryVQEResult, QuantumError>> - Ground state energy and parameters
        let runWith
            (initialParameters: float[] option)
            (errorMitigation: ErrorMitigationStrategy.RecommendedStrategy option)
            (config: ChemistryVQEConfig)
            : Async<Result<ChemistryVQEResult, QuantumError>> =
            async {
                match config.Ansatz with
                | UCCSD(numElectrons, numOrbitals) ->

                    // UCCSD excitation pool size
                    let numSingles = numElectrons * (numOrbitals - numElectrons)
                    let numDoublesOccPairs = numElectrons * (numElectrons - 1) / 2

                    let numDoublesVirtPairs =
                        (numOrbitals - numElectrons) * (numOrbitals - numElectrons - 1) / 2

                    let numDoubles = numDoublesOccPairs * numDoublesVirtPairs
                    let totalParams = numSingles + numDoubles

                    // Step 1: Prepare initial state (Hartree-Fock or |0⟩)
                    let! initialStateResult =
                        async {
                            if config.UseHFInitialState then
                                return HartreeFock.prepareHartreeFockState numElectrons numOrbitals config.Backend
                            else
                                return config.Backend.InitializeState numOrbitals
                        }

                    // Initial UCCSD amplitudes: provided, or small seeded random values near zero.
                    let startingParameters () =
                        match initialParameters with
                        | Some provided -> Array.copy provided
                        | None ->
                            let rng = Random(42)
                            Array.init totalParams (fun _ -> (rng.NextDouble() - 0.5) * 0.01)

                    match initialStateResult with
                    | _ when
                        initialParameters
                        |> Option.exists (fun provided -> provided.Length <> totalParams)
                        ->
                        return
                            Error(
                                QuantumError.ValidationError(
                                    "InitialParameters",
                                    $"UCCSD({numElectrons} electrons, {numOrbitals} spin orbitals) takes {totalParams} parameters "
                                    + $"({numSingles} singles + {numDoubles} doubles), got {initialParameters.Value.Length}"
                                )
                            )
                    // A backend that cannot apply gates one at a time (cloud hardware) runs
                    // whole circuits: sampled energies, SPSA.
                    | Error err when config.UseHFInitialState && UnifiedBackend.isIncrementalUnsupported err ->
                        return runSampled (startingParameters ()) errorMitigation config numElectrons numOrbitals
                    | Error err -> return Error err
                    | Ok initialState ->
                        let initialParameters = startingParameters ()

                        // BFGS with a backtracking (Armijo) line search on central-difference
                        // gradients. Converged when every gradient component is below
                        // 0.1·√Tolerance, which bounds the remaining energy error near Tolerance.
                        // One iteration = one line search plus the gradient at the new point.
                        let epsilon = 1e-4
                        let gradientTolerance = 0.1 * sqrt config.Tolerance
                        let armijo = 1e-4
                        let minStep = 1e-8
                        let n = totalParams
                        let energyOf = measureEnergy errorMitigation config.Hamiltonian

                        /// Ansatz state and energy at the given amplitudes.
                        let evaluate (parameters: float array) : Result<QuantumState * float, QuantumError> =
                            UCCSD.generateExcitationPool numElectrons numOrbitals parameters
                            |> Result.mapError (fun msg -> QuantumError.OperationError("UCCSD", msg))
                            |> Result.bind (fun pool -> buildUCCSDCircuit pool parameters initialState config.Backend)
                            |> Result.bind (fun state ->
                                energyOf state config.Backend |> Result.map (fun energy -> (state, energy)))

                        /// Central-difference gradient; an evaluation error is returned.
                        let gradientAt (parameters: float array) : Result<float array, QuantumError> =
                            List.init (FSharp.Core.Operators.max 0 n) (fun i ->
                                let shifted delta =
                                    let p = Array.copy parameters
                                    p.[i] <- p.[i] + delta
                                    evaluate p |> Result.map snd

                                match shifted epsilon, shifted -epsilon with
                                | Ok plus, Ok minus -> Ok((plus - minus) / (2.0 * epsilon))
                                | Error e, _
                                | _, Error e -> Error e)
                            |> ResultHelpers.sequence
                            |> Result.map Array.ofList

                        let dot (a: float array) (b: float array) =
                            Array.fold2 (fun acc x y -> acc + x * y) 0.0 a b

                        let largest (v: float array) =
                            if v.Length = 0 then
                                0.0
                            else
                                v |> Array.map abs |> Array.max

                        let identity () =
                            Array2D.init n n (fun i j -> if i = j then 1.0 else 0.0)

                        let apply (m: float[,]) (v: float array) =
                            Array.init n (fun i -> Seq.sum (seq { for j in 0 .. n - 1 -> m.[i, j] * v.[j] }))

                        /// BFGS inverse-Hessian update for step s and gradient change y (sy = sᵀy > 0).
                        let bfgsUpdate (h: float[,]) (s: float array) (y: float array) (sy: float) =
                            let rho = 1.0 / sy
                            let hy = apply h y
                            let scale = rho * rho * dot y hy + rho

                            Array2D.init n n (fun i j ->
                                h.[i, j] - rho * (s.[i] * hy.[j] + hy.[i] * s.[j]) + scale * s.[i] * s.[j])

                        let report iteration energy =
                            config.ProgressReporter
                            |> Option.iter (fun r ->
                                r.Report(Progress.IterationUpdate(iteration, config.MaxIterations, Some energy)))

                        let finish (s: OptimizationState) =
                            Ok
                                {
                                    Energy = s.Energy
                                    OptimalParameters = s.Parameters
                                    Iterations = s.Iteration
                                    Converged = s.Converged
                                    FinalState = s.FinalState
                                    Estimation =
                                        match s.FinalState with
                                        | QuantumState.StateVector _ -> ExactExpectation
                                        | _ -> SampledGateByGate shotsPerTerm
                                    Notes = []
                                }

                        /// Largest step (halving from 1) along `direction` meeting the Armijo condition.
                        let rec lineSearch
                            (s: OptimizationState)
                            (direction: float array)
                            (slope: float)
                            (step: float)
                            =
                            if step < minStep then
                                Ok None
                            else
                                let candidate = Array.map2 (fun x d -> x + step * d) s.Parameters direction

                                evaluate candidate
                                |> Result.bind (fun (state, energy) ->
                                    if energy <= s.Energy + armijo * step * slope then
                                        Ok(Some(candidate, state, energy, step))
                                    else
                                        lineSearch s direction slope (step * 0.5))

                        let rec optimize (s: OptimizationState) : Result<ChemistryVQEResult, QuantumError> =
                            if largest s.Gradient < gradientTolerance then
                                finish { s with Converged = true }
                            elif s.Iteration >= config.MaxIterations then
                                finish s
                            else
                                let quasiNewton = apply s.InverseHessian s.Gradient |> Array.map (~-)

                                let direction, inverseHessian, fresh =
                                    if dot quasiNewton s.Gradient < 0.0 then
                                        quasiNewton, s.InverseHessian, s.FreshHessian
                                    else
                                        Array.map (~-) s.Gradient, identity (), true

                                lineSearch s direction (dot direction s.Gradient) 1.0
                                |> Result.bind (function
                                    | None when not fresh ->
                                        // No decrease along the quasi-Newton direction: restart from steepest descent.
                                        optimize
                                            { s with
                                                InverseHessian = identity ()
                                                FreshHessian = true
                                                Iteration = s.Iteration + 1
                                            }
                                    | None -> finish { s with Iteration = s.Iteration + 1 }
                                    | Some(parameters, state, energy, step) ->
                                        gradientAt parameters
                                        |> Result.bind (fun gradient ->
                                            report (s.Iteration + 1) energy
                                            let stepVector = direction |> Array.map (fun d -> step * d)
                                            let y = Array.map2 (-) gradient s.Gradient
                                            let sy = dot stepVector y

                                            let updated, stillFresh =
                                                if sy > 1e-12 then
                                                    bfgsUpdate inverseHessian stepVector y sy, false
                                                else
                                                    inverseHessian, fresh

                                            optimize
                                                {
                                                    Parameters = parameters
                                                    Energy = energy
                                                    Gradient = gradient
                                                    InverseHessian = updated
                                                    FreshHessian = stillFresh
                                                    Iteration = s.Iteration + 1
                                                    FinalState = state
                                                    Converged = false
                                                }))

                        return
                            evaluate initialParameters
                            |> Result.bind (fun (state, energy) ->
                                report 0 energy

                                gradientAt initialParameters
                                |> Result.bind (fun gradient ->
                                    optimize
                                        {
                                            Parameters = initialParameters
                                            Energy = energy
                                            Gradient = gradient
                                            InverseHessian = identity ()
                                            FreshHessian = true
                                            Iteration = 0
                                            FinalState = state
                                            Converged = false
                                        }))
            }

        /// Run UCCSD-VQE to find molecular ground state
        ///
        /// **Parameters**:
        ///   config - VQE configuration with UCCSD ansatz
        ///
        /// **Returns**:
        ///   Async<Result<ChemistryVQEResult, QuantumError>> - Ground state energy and parameters
        let run (config: ChemistryVQEConfig) : Async<Result<ChemistryVQEResult, QuantumError>> =
            runWith None None config

// ============================================================================
// MOLECULAR INTEGRALS (Pluggable Provider Interface)
// ============================================================================
//
// This module provides a pluggable interface for molecular integrals, allowing
// users to provide real quantum chemistry integrals from external packages
// (PySCF, Psi4, NWChem, etc.) without adding dependencies to this library.
//
// ┌─────────────────────────────────────────────────────────────────────────┐
// │ PRECONDITIONS FOR USING CUSTOM INTEGRAL PROVIDERS                       │
// ├─────────────────────────────────────────────────────────────────────────┤
// │                                                                         │
// │ 1. INTEGRAL ARRAY DIMENSIONS                                            │
// │    - OneElectron.Integrals must be [NumOrbitals x NumOrbitals]          │
// │    - TwoElectron.Integrals must be [NumOrbitals x NumOrbitals x         │
// │                                      NumOrbitals x NumOrbitals]         │
// │    - Dimension mismatch will cause IndexOutOfRangeException             │
// │                                                                         │
// │ 2. INTEGRAL NOTATION                                                    │
// │    - Two-electron integrals must be in CHEMIST notation: (pq|rs)        │
// │    - NOT physicist notation <pr|qs>                                     │
// │    - PySCF uses chemist notation by default ✓                           │
// │    - Psi4 uses physicist notation - requires conversion!                │
// │                                                                         │
// │ 3. MOLECULAR ORBITAL BASIS                                              │
// │    - All integrals must be in MO basis (not AO basis)                   │
// │    - Transform AO integrals: h_MO = C^T @ h_AO @ C                      │
// │    - Use ao2mo library for efficient 2-electron transformation          │
// │                                                                         │
// │ 4. SPIN ORBITAL EXPANSION                                               │
// │    - Provider returns SPATIAL orbitals (NumOrbitals)                    │
// │    - Library internally expands to spin orbitals (2 * NumOrbitals)      │
// │    - This doubles the qubit count: nQubits = 2 * NumOrbitals            │
// │                                                                         │
// │ 5. QUBIT LIMITS                                                         │
// │    - Maximum Types.NisqPracticalQubits (10 spatial orbitals) on NISQ       │
// │    - H2 in STO-3G: 2 orbitals → 4 qubits ✓                              │
// │    - H2O in STO-3G: 7 orbitals → 14 qubits ✓                            │
// │    - Large molecules require active space selection                     │
// │                                                                         │
// │ 6. UNITS                                                                │
// │    - All energies must be in Hartree (atomic units)                     │
// │    - Positions in Molecule type are in Angstroms                        │
// │                                                                         │
// │ 7. EXTERNAL DEPENDENCIES (for PySCF provider example)                   │
// │    - Python 3.8+ must be installed and in PATH                          │
// │    - PySCF package: pip install pyscf                                   │
// │    - NumPy package: pip install numpy                                   │
// │    - pythonnet NuGet package (referenced in script, not library)        │
// │                                                                         │
// └─────────────────────────────────────────────────────────────────────────┘
//
// FAILURE MODES:
// - Provider returns Error: VQE returns Error (ValidationError "IntegralProvider")
// - Dimension mismatch: IndexOutOfRangeException during Hamiltonian build
// - Wrong notation: Incorrect energies (may converge to wrong value)
// - AO basis integrals: Incorrect energies (integrals not properly transformed)
// - Too many orbitals: ValidationError "Molecule too large"
//
// EXAMPLE USAGE:
//   let provider = createPySCFProvider "sto-3g"  // From PySCFIntegration.fsx
//   let config = { ... ; IntegralProvider = Some provider }
//   let! result = GroundStateEnergy.estimateEnergy molecule config
//
// ============================================================================

/// One-electron integrals h_pq = <p|T + V_nuc|q>
/// Kinetic energy + nuclear attraction in molecular orbital basis
type OneElectronIntegrals =
    {
        /// Number of molecular orbitals
        NumOrbitals: int
        /// Integral matrix h[p,q] in Hartree
        /// Access: h.[p, q] gives <p|h|q>
        /// PRECONDITION: Array dimensions must match NumOrbitals
        Integrals: float[,]
    }

/// Two-electron integrals g_pqrs = (pq|rs) in chemist notation
/// Electron repulsion integrals in molecular orbital basis
type TwoElectronIntegrals =
    {
        /// Number of molecular orbitals
        NumOrbitals: int
        /// Integral tensor g[p,q,r,s] in Hartree (chemist notation)
        /// Access: g.[p,q,r,s] gives (pq|rs)
        /// PRECONDITION: Must be in CHEMIST notation (pq|rs), not physicist <pr|qs>
        /// PRECONDITION: Array dimensions must match NumOrbitals
        Integrals: float[,,,]
    }

/// Complete molecular integrals for quantum chemistry calculations
/// Can be provided by external tools (PySCF, Psi4, etc.) or computed internally
///
/// PRECONDITIONS:
/// - NumOrbitals must be ≤ 10 (gives 20 qubits after spin-orbital expansion)
/// - All integrals must be in molecular orbital (MO) basis
/// - Two-electron integrals must use chemist notation (pq|rs)
/// - All energies in Hartree
type MolecularIntegrals =
    {
        /// Number of spatial molecular orbitals
        /// PRECONDITION: Must be ≤ 10 for NISQ simulation (expands to 2N qubits)
        NumOrbitals: int
        /// Number of electrons
        NumElectrons: int
        /// Nuclear repulsion energy in Hartree (constant term)
        NuclearRepulsion: float
        /// One-electron integrals (kinetic + nuclear attraction)
        OneElectron: OneElectronIntegrals
        /// Two-electron integrals (electron repulsion)
        TwoElectron: TwoElectronIntegrals
        /// Reference energy from classical calculation (e.g., Hartree-Fock) for validation
        ReferenceEnergy: float option
    }

/// Function signature for custom integral providers
/// Takes a molecule and returns integrals or an error message
///
/// IMPLEMENTATION REQUIREMENTS:
/// - Return integrals in MO basis (not AO basis)
/// - Use chemist notation (pq|rs) for two-electron integrals
/// - Energies in Hartree, positions read from Molecule are in Angstroms
/// - Handle errors gracefully and return descriptive error messages
///
/// COMMON PROVIDERS:
/// - PySCF: See examples/DrugDiscovery/PySCFIntegration.fsx
/// - Psi4: Requires notation conversion from physicist to chemist
/// - File-based: Parse FCIDump or HDF5 files with pre-computed integrals
type IntegralProvider = Molecule -> Result<MolecularIntegrals, string>

/// Molecular integrals from FCIDUMP files, the interchange format written by PySCF
/// (pyscf.tools.fcidump), Psi4, Molpro, OpenMolcas and others. An FCIDUMP carries no
/// geometry: the file must describe the molecule it is used for.
module FciDumpIntegrals =

    /// MolecularIntegrals of parsed FCIDUMP integrals; the core energy becomes NuclearRepulsion.
    let ofParsed (parsed: MoleculeFormats.FciDump.Integrals) : MolecularIntegrals =
        let n = parsed.Header.NumOrbitals

        {
            NumOrbitals = n
            NumElectrons = parsed.Header.NumElectrons
            NuclearRepulsion = parsed.CoreEnergy
            OneElectron =
                {
                    NumOrbitals = n
                    Integrals = parsed.OneElectron
                }
            TwoElectron =
                {
                    NumOrbitals = n
                    Integrals = parsed.TwoElectron
                }
            ReferenceEnergy = None
        }

    /// Parse FCIDUMP content into MolecularIntegrals. MS2 (2·Sz) must be the lowest for the
    /// electron count (0 for even, 1 for odd): VQE starts from a Hartree-Fock reference of
    /// that spin and cannot honour a higher-spin state.
    let parse (content: string) : Result<MolecularIntegrals, QuantumError> =
        MoleculeFormats.FciDump.parseIntegrals content
        |> Result.bind (fun parsed ->
            match parsed.Header.MS2 with
            | Some ms2 when ms2 <> parsed.Header.NumElectrons % 2 ->
                Error(
                    QuantumError.ValidationError(
                        "FciDump",
                        $"MS2={ms2} with NELEC={parsed.Header.NumElectrons}: only the lowest spin state (MS2={parsed.Header.NumElectrons % 2}) is supported"
                    )
                )
            | _ -> Ok(ofParsed parsed))

    /// Read an FCIDUMP file into MolecularIntegrals.
    let readFile (path: string) : Result<MolecularIntegrals, QuantumError> =
        try
            if not (File.Exists path) then
                Error(QuantumError.IOError("ReadFciDump", path, "File not found"))
            else
                File.ReadAllText path
                |> parse
                |> Result.mapError (fun err -> QuantumError.OperationError("ReadFciDump", $"{path}: {err.Message}"))
        with ex ->
            Error(QuantumError.IOError("ReadFciDump", path, ex.Message))

    /// IntegralProvider that returns the integrals of the FCIDUMP file at `path` for any molecule.
    let fromFile (path: string) : IntegralProvider =
        fun _ -> readFile path |> Result.mapError (fun err -> err.Message)

    /// IntegralProvider that reads `<directory>/<molecule.Name>.fcidump`.
    let fromDirectory (directory: string) : IntegralProvider =
        fun molecule ->
            readFile (Path.Combine(directory, molecule.Name + ".fcidump"))
            |> Result.mapError (fun err -> err.Message)

/// Molecular-orbital integrals for molecules made only of H and He atoms, in the STO-3G or
/// 6-31G basis. For these elements both bases hold only s-type contracted Gaussians, so the
/// overlap, kinetic, nuclear-attraction and electron-repulsion integrals have closed forms in
/// the Boys function F0. Restricted Hartree-Fock turns them into molecular-orbital integrals.
module Sto3gIntegrals =

    open MathNet.Numerics.LinearAlgebra
    open MathNet.Numerics.LinearAlgebra.Factorization

    /// Contracted s shells, (exponents in bohr⁻², coefficients of normalised primitives),
    /// per basis and element: STO-3G (Hehre, Stewart, Pople 1969) and 6-31G (Ditchfield,
    /// Hehre, Pople 1971), as distributed by the Basis Set Exchange.
    let private shells: Map<string, Map<string, (float[] * float[]) list>> =
        let sto3g = [| 0.15432897; 0.53532814; 0.44463454 |]

        Map
            [
                "STO-3G",
                Map
                    [
                        "H", [ [| 3.42525091; 0.62391373; 0.16885540 |], sto3g ]
                        "HE", [ [| 6.36242139; 1.15892300; 0.31364979 |], sto3g ]
                    ]
                "6-31G",
                Map
                    [
                        "H",
                        [
                            [| 18.7311370; 2.8253937; 0.6401217 |], [| 0.03349460; 0.23472695; 0.81375733 |]
                            [| 0.1612778 |], [| 1.0 |]
                        ]
                        "HE",
                        [
                            [| 38.4216340; 5.7780300; 1.2417740 |], [| 0.0237660; 0.1546790; 0.4696300 |]
                            [| 0.2979640 |], [| 1.0 |]
                        ]
                    ]
            ]

    /// Bases computed here, by the names `computeInBasis` accepts (case-insensitive).
    let supportedBases = [ "STO-3G"; "6-31G" ]

    /// A normalised s-type primitive: coefficient × (2α/π)^¾, exponent α, centre (bohr).
    type private Primitive =
        {
            Weight: float
            Alpha: float
            Centre: float * float * float
        }

    let private dist2 (x1, y1, z1) (x2, y2, z2) =
        (x1 - x2) * (x1 - x2) + (y1 - y2) * (y1 - y2) + (z1 - z2) * (z1 - z2)

    /// Boys function F0(t) = ½·√(π/t)·erf(√t), with its series near t = 0.
    let private boysF0 (t: float) =
        if t < 1e-8 then
            1.0 - t / 3.0
        else
            0.5 * sqrt (Math.PI / t) * MathNet.Numerics.SpecialFunctions.Erf(sqrt t)

    let private gaussianCentre (a: Primitive) (b: Primitive) =
        let p = a.Alpha + b.Alpha
        let (ax, ay, az) = a.Centre
        let (bx, by, bz) = b.Centre
        ((a.Alpha * ax + b.Alpha * bx) / p, (a.Alpha * ay + b.Alpha * by) / p, (a.Alpha * az + b.Alpha * bz) / p)

    let private overlap (a: Primitive) (b: Primitive) =
        let p = a.Alpha + b.Alpha
        let mu = a.Alpha * b.Alpha / p
        (Math.PI / p) ** 1.5 * exp (-mu * dist2 a.Centre b.Centre)

    let private kinetic (a: Primitive) (b: Primitive) =
        let p = a.Alpha + b.Alpha
        let mu = a.Alpha * b.Alpha / p
        let r2 = dist2 a.Centre b.Centre
        mu * (3.0 - 2.0 * mu * r2) * (Math.PI / p) ** 1.5 * exp (-mu * r2)

    let private attraction (nuclei: (float * (float * float * float)) array) (a: Primitive) (b: Primitive) =
        let p = a.Alpha + b.Alpha
        let mu = a.Alpha * b.Alpha / p
        let centre = gaussianCentre a b
        let prefactor = 2.0 * Math.PI / p * exp (-mu * dist2 a.Centre b.Centre)

        nuclei
        |> Array.sumBy (fun (z, c) -> -z * prefactor * boysF0 (p * dist2 centre c))

    let private repulsion (a: Primitive) (b: Primitive) (c: Primitive) (d: Primitive) =
        let p = a.Alpha + b.Alpha
        let q = c.Alpha + d.Alpha
        let muAB = a.Alpha * b.Alpha / p
        let muCD = c.Alpha * d.Alpha / q
        let pq = dist2 (gaussianCentre a b) (gaussianCentre c d)

        2.0 * Math.PI ** 2.5 / (p * q * sqrt (p + q))
        * exp (-muAB * dist2 a.Centre b.Centre - muCD * dist2 c.Centre d.Centre)
        * boysF0 (p * q / (p + q) * pq)

    let private contract2 (f: Primitive -> Primitive -> float) (u: Primitive[]) (v: Primitive[]) =
        Array.sum
            [|
                for a in u do
                    for b in v -> a.Weight * b.Weight * f a b
            |]

    /// Molecular-orbital integrals in `basis` ("STO-3G" or "6-31G", case-insensitive).
    /// Closed shells (even electron count, multiplicity 1) use restricted Hartree-Fock
    /// orbitals and ReferenceEnergy is the RHF total energy: the lowest solution reached from
    /// several starting guesses (core, GWH, HOMO-LUMO mixed, seeded perturbations) with DIIS,
    /// plain and damped Roothaan iterations, then followed down any negative mode of the
    /// orbital Hessian. When no SCF converges, core-Hamiltonian orbitals are used and
    /// ReferenceEnergy is None (the Hamiltonian's spectrum is orbital-invariant; only the
    /// VQE starting determinant is poorer). A single electron (multiplicity 2) uses
    /// core-Hamiltonian orbitals, which are exact for it, and ReferenceEnergy is None.
    /// Other open shells are an Error: UCCSD-VQE does not conserve spin, so it could not
    /// honour the requested state. Other Errors: another basis, elements other than H and He,
    /// no electrons, more electrons than 2 × basis functions, or coincident nuclei.
    let computeInBasis (basis: string) (molecule: Molecule) : Result<MolecularIntegrals, QuantumError> =
        let invalid reason =
            Error(QuantumError.ValidationError("Sto3gIntegrals", $"{molecule.Name}: {reason}"))

        let basisName = basis.Trim().ToUpperInvariant()

        let unsupported =
            molecule.Atoms
            |> List.tryFind (fun a ->
                let e = a.Element.ToUpperInvariant()
                e <> "H" && e <> "HE")

        let numElectrons = Molecule.countElectrons molecule

        let functionCount (elementShells: Map<string, (float[] * float[]) list>) =
            molecule.Atoms
            |> List.sumBy (fun a -> elementShells.[a.Element.ToUpperInvariant()].Length)

        match shells.TryFind basisName, unsupported, Molecule.nuclearRepulsion molecule with
        | None, _, _ ->
            invalid (
                $"basis '{basis}' is not computed by the library (supported for H and He: "
                + String.Join(", ", supportedBases)
                + "); integrals from an IntegralProvider or an FCIDUMP file carry their own basis"
            )
        | _ when molecule.Atoms.IsEmpty -> invalid "no atoms"
        | _, Some atom, _ -> invalid $"{basisName} integrals are computed for H and He only, found '{atom.Element}'"
        | _, None, Error err -> Error err
        | _, None, Ok _ when
            numElectrons <= 0
            || (numElectrons % 2 = 0 && molecule.Multiplicity <> 1)
            || (numElectrons % 2 = 1 && (numElectrons <> 1 || molecule.Multiplicity <> 2))
            ->
            invalid
                $"supported are closed shells (even electron count, multiplicity 1) and one-electron doublets; got {numElectrons} electrons, multiplicity {molecule.Multiplicity}"
        | Some elementShells, None, Ok _ when numElectrons > 2 * functionCount elementShells ->
            invalid $"{numElectrons} electrons exceed {functionCount elementShells} {basisName} orbitals"
        | Some elementShells, None, Ok nuclearRepulsion ->
            let toBohr (x, y, z) =
                (x * Molecule.BohrPerAngstrom, y * Molecule.BohrPerAngstrom, z * Molecule.BohrPerAngstrom)

            let basis =
                molecule.Atoms
                |> List.collect (fun atom ->
                    elementShells.[atom.Element.ToUpperInvariant()]
                    |> List.map (fun (exponents, coefficients) ->
                        let primitives =
                            exponents
                            |> Array.mapi (fun k alpha ->
                                {
                                    Weight = coefficients.[k] * (2.0 * alpha / Math.PI) ** 0.75
                                    Alpha = alpha
                                    Centre = toBohr atom.Position
                                })
                        // Normalise the contracted function.
                        let norm = sqrt (contract2 overlap primitives primitives)
                        primitives |> Array.map (fun p -> { p with Weight = p.Weight / norm })))
                |> Array.ofList

            let n = basis.Length

            let nuclei =
                molecule.Atoms
                |> List.map (fun atom -> float (AtomicNumbers.fromSymbol atom.Element).Value, toBohr atom.Position)
                |> Array.ofList

            let s =
                Matrix<float>.Build.Dense(n, n, fun i j -> contract2 overlap basis.[i] basis.[j])

            let hCore =
                Matrix<float>
                    .Build.Dense(
                        n,
                        n,
                        fun i j ->
                            contract2 kinetic basis.[i] basis.[j]
                            + contract2 (attraction nuclei) basis.[i] basis.[j]
                    )

            let eri = Array4D.zeroCreate n n n n

            for i in 0 .. n - 1 do
                for j in 0..i do
                    for k in 0 .. n - 1 do
                        for l in 0..k do
                            if i * (i + 1) / 2 + j >= k * (k + 1) / 2 + l then
                                let v =
                                    Array.sum
                                        [|
                                            for a in basis.[i] do
                                                for b in basis.[j] do
                                                    for c in basis.[k] do
                                                        for d in basis.[l] ->
                                                            a.Weight
                                                            * b.Weight
                                                            * c.Weight
                                                            * d.Weight
                                                            * repulsion a b c d
                                        |]

                                for (w, x, y, z) in
                                    [
                                        (i, j, k, l)
                                        (j, i, k, l)
                                        (i, j, l, k)
                                        (j, i, l, k)
                                        (k, l, i, j)
                                        (l, k, i, j)
                                        (k, l, j, i)
                                        (l, k, j, i)
                                    ] do
                                    eri.[w, x, y, z] <- v

            // Symmetric orthogonalisation X = S^-1/2
            let sEvd = s.Evd Symmetricity.Symmetric

            let x =
                sEvd.EigenVectors
                * Matrix<float>
                    .Build.DiagonalOfDiagonalArray(sEvd.EigenValues.ToArray() |> Array.map (fun v -> 1.0 / sqrt v.Real))
                * sEvd.EigenVectors.Transpose()

            let occupied = numElectrons / 2

            /// MO coefficients (columns, ascending orbital energy) of the Fock matrix f.
            let solve (f: Matrix<float>) =
                let evd = (x.Transpose() * f * x).Evd Symmetricity.Symmetric
                let order = Array.init n id |> Array.sortBy (fun k -> evd.EigenValues.[k].Real)

                let cPrime =
                    Matrix<float>.Build.Dense(n, n, fun i j -> evd.EigenVectors.[i, order.[j]])

                x * cPrime

            /// Closed-shell density of occupied orbitals cOcc (columns, not necessarily
            /// orthonormal): P = 2 C (Cᵀ S C)⁻¹ Cᵀ.
            let densityOfOccupied (cOcc: Matrix<float>) =
                2.0 * cOcc * (cOcc.Transpose() * s * cOcc).Inverse() * cOcc.Transpose()

            /// Closed-shell density of the lowest `occupied` columns of c.
            let density (c: Matrix<float>) =
                densityOfOccupied (c.SubMatrix(0, n, 0, occupied))

            let fock (p: Matrix<float>) =
                let pa = p.ToArray()

                Matrix<float>
                    .Build.Dense(
                        n,
                        n,
                        fun i j ->
                            let mutable sum = hCore.[i, j]

                            for k in 0 .. n - 1 do
                                for l in 0 .. n - 1 do
                                    sum <- sum + pa.[k, l] * (eri.[i, j, k, l] - 0.5 * eri.[i, k, j, l])

                            sum
                    )

            /// (pq|rs) over the columns of c: Σ C_μp C_νq C_λr C_σs (μν|λσ), one index at a time.
            let toMolecularOrbitals (c: Matrix<float>) =
                let ca = c.ToArray()

                let transform (g: float[,,,]) (axis: int) =
                    let t = Array4D.zeroCreate n n n n

                    for a in 0 .. n - 1 do
                        for b in 0 .. n - 1 do
                            for r in 0 .. n - 1 do
                                for q in 0 .. n - 1 do
                                    let mutable sum = 0.0

                                    for m in 0 .. n - 1 do
                                        sum <-
                                            sum
                                            + (match axis with
                                               | 0 -> ca.[m, a] * g.[m, b, r, q]
                                               | 1 -> ca.[m, b] * g.[a, m, r, q]
                                               | 2 -> ca.[m, r] * g.[a, b, m, q]
                                               | _ -> ca.[m, q] * g.[a, b, r, m])

                                    t.[a, b, r, q] <- sum

                    t

                [ 0..3 ] |> List.fold transform eri

            let electronicEnergy (p: Matrix<float>) (f: Matrix<float>) =
                0.5 * (p.PointwiseMultiply(hCore + f).Enumerate() |> Seq.sum)

            /// Pulay DIIS extrapolation of the Fock matrix over the stored (F, error) pairs.
            let diis (history: (Matrix<float> * Matrix<float>) list) (f: Matrix<float>) =
                match history with
                | []
                | [ _ ] -> f
                | _ ->
                    let m = history.Length
                    let errors = history |> List.map snd |> Array.ofList

                    let b =
                        Matrix<float>
                            .Build.Dense(
                                m + 1,
                                m + 1,
                                fun i j ->
                                    if i = m && j = m then
                                        0.0
                                    elif i = m || j = m then
                                        -1.0
                                    else
                                        errors.[i].PointwiseMultiply(errors.[j]).Enumerate() |> Seq.sum
                            )

                    let rhs = Vector<float>.Build.Dense(m + 1, fun i -> if i = m then -1.0 else 0.0)

                    try
                        let weights = b.Solve rhs

                        if
                            weights.Enumerate()
                            |> Seq.exists (fun w -> Double.IsNaN w || Double.IsInfinity w)
                        then
                            f
                        else
                            history |> List.mapi (fun i (fi, _) -> fi * weights.[i]) |> List.reduce (+)
                    with _ ->
                        f

            /// RHF from the starting density p0 with Pulay DIIS, or with Roothaan steps whose new
            /// density is mixed with `damping` of the old one. Some(orbitals, electronic energy)
            /// at self-consistency, None when 1000 iterations do not reach it.
            let runScf (useDiis: bool) (damping: float) (p0: Matrix<float>) =
                let rec loop iteration (p: Matrix<float>) (previousEnergy: float) history =
                    let f = fock p
                    let energy = electronicEnergy p f
                    // Commutator FPS - SPF in the orthonormal basis vanishes at self-consistency.
                    let error = x.Transpose() * (f * p * s - s * p * f) * x

                    if abs (energy - previousEnergy) < 1e-12 && error.FrobeniusNorm() < 1e-9 then
                        Some(solve f, energy)
                    elif iteration >= 1000 then
                        None
                    else
                        let history = (f, error) :: history |> List.truncate 8
                        let next = density (solve (if useDiis then diis history f else f))

                        let mixed =
                            if damping > 0.0 then
                                (1.0 - damping) * next + damping * p
                            else
                                next

                        loop (iteration + 1) mixed energy history

                loop 1 p0 Double.MaxValue []

            /// Every SCF strategy from one starting density: DIIS, plain Roothaan, damped Roothaan.
            let strategies (p0: Matrix<float>) =
                [ runScf true 0.0 p0; runScf false 0.0 p0; runScf false 0.5 p0 ]

            /// The lowest-energy converged solution; the first one on ties.
            let lowest (solutions: (Matrix<float> * float) option list) =
                match List.choose id solutions with
                | [] -> None
                | converged -> Some(List.minBy snd converged)

            let virtuals = n - occupied

            /// Lowest eigenpair of the real RHF orbital Hessian over occupied-virtual rotations
            /// at canonical orbitals c: δ_ij F_ab - δ_ab F_ij + 4(ia|jb) - (ib|ja) - (ij|ab).
            /// A negative eigenvalue means c is a saddle point of the RHF energy.
            let lowestHessianMode (c: Matrix<float>) =
                let g = toMolecularOrbitals c
                let fMo = c.Transpose() * fock (density c) * c
                let dimension = occupied * virtuals

                let hessian =
                    Matrix<float>
                        .Build.Dense(
                            dimension,
                            dimension,
                            fun row column ->
                                let i, a = row / virtuals, occupied + row % virtuals
                                let j, b = column / virtuals, occupied + column % virtuals

                                (if i = j then fMo.[a, b] else 0.0) - (if a = b then fMo.[i, j] else 0.0)
                                + 4.0 * g.[i, a, j, b]
                                - g.[i, b, j, a]
                                - g.[i, j, a, b]
                        )

                let evd = hessian.Evd Symmetricity.Symmetric
                let k = [ 0 .. dimension - 1 ] |> List.minBy (fun k -> evd.EigenValues.[k].Real)
                evd.EigenValues.[k].Real, evd.EigenVectors.Column k

            /// Density after rotating the occupied orbitals of c by `step` along a Hessian mode.
            let rotatedDensity (c: Matrix<float>) (mode: Vector<float>) (step: float) =
                let t =
                    Matrix<float>.Build.Dense(virtuals, occupied, fun a i -> mode.[i * virtuals + a])

                densityOfOccupied (
                    c.SubMatrix(0, n, 0, occupied)
                    + step * c.SubMatrix(0, n, occupied, virtuals) * t
                )

            /// Follows negative orbital-Hessian modes downhill (up to 5 times) while that
            /// reaches a lower converged RHF solution.
            let rec descend (c: Matrix<float>, energy: float) round =
                if round >= 5 || virtuals = 0 then
                    (c, energy)
                else
                    match lowestHessianMode c with
                    | eigenvalue, mode when eigenvalue < -1e-6 ->
                        let downhill =
                            [ 0.3; -0.3; 0.8; -0.8 ]
                            |> List.collect (rotatedDensity c mode >> strategies)
                            |> lowest

                        match downhill with
                        | Some(c', energy') when energy' < energy - 1e-9 -> descend (c', energy') (round + 1)
                        | _ -> (c, energy)
                    | _ -> (c, energy)

            // Starting densities: core Hamiltonian, generalised Wolfsberg-Helmholz, the core
            // guess with HOMO and LUMO mixed, and three seeded random perturbations of the core
            // Hamiltonian. Different starts can converge to different RHF solutions.
            let startingDensities () =
                let core = solve hCore

                let gwh =
                    Matrix<float>
                        .Build.Dense(
                            n,
                            n,
                            fun i j ->
                                if i = j then
                                    hCore.[i, i]
                                else
                                    0.875 * s.[i, j] * (hCore.[i, i] + hCore.[j, j])
                        )

                let homoLumoMixed =
                    if virtuals = 0 then
                        []
                    else
                        let mixed = core.Clone()
                        let angle = Math.PI / 6.0

                        mixed.SetColumn(
                            occupied - 1,
                            cos angle * core.Column(occupied - 1) + sin angle * core.Column occupied
                        )

                        [ densityOfOccupied (mixed.SubMatrix(0, n, 0, occupied)) ]

                let scale = 0.2 * (Seq.init n (fun i -> abs hCore.[i, i]) |> Seq.max)

                let perturbed =
                    [ 1..3 ]
                    |> List.map (fun seed ->
                        let rng = Random seed
                        let r = Matrix<float>.Build.Dense(n, n, fun _ _ -> rng.NextDouble() - 0.5)
                        density (solve (hCore + scale * (r + r.Transpose()))))

                [ density core; density (solve gwh) ] @ homoLumoMixed @ perturbed

            // Closed shells: the lowest RHF solution found from every start and strategy, then
            // followed down any orbital-Hessian instability. When no SCF converges (e.g. some
            // stretched chains), the core-Hamiltonian orbitals are used and ReferenceEnergy is
            // None: the Hamiltonian's spectrum does not depend on the orbitals, only the VQE
            // starting determinant does. One electron: core-Hamiltonian orbitals are exact.
            let c, referenceEnergy =
                if numElectrons % 2 = 1 then
                    solve hCore, None
                else
                    match startingDensities () |> List.collect strategies |> lowest with
                    | Some best ->
                        let c, electronic = descend best 0
                        c, Some(electronic + nuclearRepulsion)
                    | None -> solve hCore, None

            Ok
                {
                    NumOrbitals = n
                    NumElectrons = numElectrons
                    NuclearRepulsion = nuclearRepulsion
                    OneElectron =
                        {
                            NumOrbitals = n
                            Integrals = (c.Transpose() * hCore * c).ToArray()
                        }
                    TwoElectron =
                        {
                            NumOrbitals = n
                            Integrals = toMolecularOrbitals c
                        }
                    ReferenceEnergy = referenceEnergy
                }

    /// Molecular-orbital integrals in the STO-3G basis (see computeInBasis).
    let compute (molecule: Molecule) : Result<MolecularIntegrals, QuantumError> = computeInBasis "STO-3G" molecule

    /// IntegralProvider computing integrals in `basis` (H and He only) for the molecule it is given.
    let providerInBasis (basis: string) : IntegralProvider =
        fun molecule -> computeInBasis basis molecule |> Result.mapError (fun err -> err.Message)

    /// IntegralProvider computing STO-3G integrals (H and He only) for the molecule it is given.
    let provider: IntegralProvider = providerInBasis "STO-3G"

// ============================================================================
// GROUND STATE ENERGY ESTIMATION
// ============================================================================

/// Ground state calculation method
type GroundStateMethod =
    /// Variational Quantum Eigensolver (quantum algorithm)
    | VQE

    /// Quantum phase estimation of the Trotterised e^(-iHt) from the Hartree-Fock state
    /// (QPE.run): an eigenvalue per peak of the outcome distribution, needing system +
    /// counting qubits (12 for H2/STO-3G)
    | QPE

    /// Tabulated classical reference energy (ClassicalDFT.run); runs no circuit.
    /// Used only when requested explicitly.
    | ClassicalDFT

    /// Quantum method chosen by the library: currently VQE. Never selects ClassicalDFT.
    | Automatic

/// Configuration for ground state energy solver
type SolverConfig =
    {
        /// Method to use for calculation
        Method: GroundStateMethod

        /// Maximum optimization iterations
        MaxIterations: int

        /// Convergence tolerance
        Tolerance: float

        /// Optional initial parameters for VQE ansatz
        InitialParameters: float[] option

        /// Quantum backend for execution (RULE1)
        /// None = use LocalBackend by default
        Backend: BackendAbstraction.IQuantumBackend option

        /// Optional progress reporter for VQE iterations
        ProgressReporter: Progress.IProgressReporter option

        /// Optional error mitigation strategy for noisy backends
        /// When set, applies error correction to measurement results
        ErrorMitigation: ErrorMitigationStrategy.RecommendedStrategy option

        /// Optional custom integral provider (e.g., from PySCF, Psi4)
        /// When provided, VQE builds the Jordan-Wigner Hamiltonian from the provider's
        /// integrals and runs UCCSD-VQE on it; a provider Error is returned as Error.
        IntegralProvider: IntegralProvider option
    }

/// Identify known molecules by their atomic composition (not just name).
/// This allows molecules loaded from XYZ files (with arbitrary names) to be
/// recognized as known molecules for empirical energy calculations.
module MoleculeIdentification =

    /// Identify a known molecule by its atomic composition.
    /// Returns the canonical name if composition matches a known molecule.
    let identify (molecule: Molecule) : string option =
        // Get sorted element counts
        let elementCounts =
            molecule.Atoms
            |> List.map (fun a -> a.Element.ToUpperInvariant())
            |> List.groupBy id
            |> List.map (fun (elem, atoms) -> (elem, atoms.Length))
            |> List.sortBy fst

        match elementCounts with
        | [ ("H", 2) ] -> Some "H2"
        | [ ("H", 2); ("O", 1) ] -> Some "H2O"
        | [ ("H", 1); ("LI", 1) ] -> Some "LiH"
        | _ -> None

/// Molecular Hamiltonian in second quantization
module MolecularHamiltonian =

    /// Fermion-to-qubit mapping method
    [<Struct>]
    type MappingMethod =
        /// Use empirical Hamiltonian (fast, accurate for known molecules)
        | Empirical
        /// Jordan-Wigner transformation (research-grade)
        | JordanWigner
        /// Bravyi-Kitaev transformation (research-grade, better scaling)
        | BravyiKitaev

    /// Build fermionic Hamiltonian terms from real molecular integrals
    ///
    /// Constructs: H = E_nuc + Σ_pq h_pq a†_p a_q + 0.5 Σ_pqrs g_pqrs a†_p a†_r a_s a_q
    ///
    /// This uses REAL integrals from external quantum chemistry packages (PySCF, Psi4, etc.)
    /// instead of empirical approximations, enabling research-grade accuracy.
    let private buildFermionTermsFromIntegrals (integrals: MolecularIntegrals) : FermionMapping.FermionTerm list =
        let n = integrals.NumOrbitals
        let h1 = integrals.OneElectron.Integrals
        let g2 = integrals.TwoElectron.Integrals

        [
            // One-electron terms: h_pq a†_p a_q (spin-orbital basis)
            // Each spatial orbital gives two spin orbitals (alpha, beta)
            for p in 0 .. n - 1 do
                for q in 0 .. n - 1 do
                    let h_pq = h1.[p, q]

                    if abs h_pq > 1e-12 then
                        // Alpha spin (even indices)
                        yield
                            {
                                FermionMapping.Coefficient = Complex(h_pq, 0.0)
                                FermionMapping.Operators =
                                    [
                                        {
                                            FermionMapping.OrbitalIndex = 2 * p
                                            FermionMapping.OperatorType = FermionMapping.Creation
                                        }
                                        {
                                            FermionMapping.OrbitalIndex = 2 * q
                                            FermionMapping.OperatorType = FermionMapping.Annihilation
                                        }
                                    ]
                            }
                        // Beta spin (odd indices)
                        yield
                            {
                                FermionMapping.Coefficient = Complex(h_pq, 0.0)
                                FermionMapping.Operators =
                                    [
                                        {
                                            FermionMapping.OrbitalIndex = 2 * p + 1
                                            FermionMapping.OperatorType = FermionMapping.Creation
                                        }
                                        {
                                            FermionMapping.OrbitalIndex = 2 * q + 1
                                            FermionMapping.OperatorType = FermionMapping.Annihilation
                                        }
                                    ]
                            }

            // Two-electron terms: 0.5 * g_pqrs a†_p a†_r a_s a_q
            // In chemist notation (pq|rs), converted to physicist <pr|qs>
            for p in 0 .. n - 1 do
                for q in 0 .. n - 1 do
                    for r in 0 .. n - 1 do
                        for s in 0 .. n - 1 do
                            let g_pqrs = g2.[p, q, r, s]

                            if abs g_pqrs > 1e-12 then
                                // Four spin combinations: αα, αβ, βα, ββ
                                // αα: p↑ r↑ s↑ q↑
                                yield
                                    {
                                        FermionMapping.Coefficient = Complex(0.5 * g_pqrs, 0.0)
                                        FermionMapping.Operators =
                                            [
                                                {
                                                    FermionMapping.OrbitalIndex = 2 * p
                                                    FermionMapping.OperatorType = FermionMapping.Creation
                                                }
                                                {
                                                    FermionMapping.OrbitalIndex = 2 * r
                                                    FermionMapping.OperatorType = FermionMapping.Creation
                                                }
                                                {
                                                    FermionMapping.OrbitalIndex = 2 * s
                                                    FermionMapping.OperatorType = FermionMapping.Annihilation
                                                }
                                                {
                                                    FermionMapping.OrbitalIndex = 2 * q
                                                    FermionMapping.OperatorType = FermionMapping.Annihilation
                                                }
                                            ]
                                    }
                                // ββ: p↓ r↓ s↓ q↓
                                yield
                                    {
                                        FermionMapping.Coefficient = Complex(0.5 * g_pqrs, 0.0)
                                        FermionMapping.Operators =
                                            [
                                                {
                                                    FermionMapping.OrbitalIndex = 2 * p + 1
                                                    FermionMapping.OperatorType = FermionMapping.Creation
                                                }
                                                {
                                                    FermionMapping.OrbitalIndex = 2 * r + 1
                                                    FermionMapping.OperatorType = FermionMapping.Creation
                                                }
                                                {
                                                    FermionMapping.OrbitalIndex = 2 * s + 1
                                                    FermionMapping.OperatorType = FermionMapping.Annihilation
                                                }
                                                {
                                                    FermionMapping.OrbitalIndex = 2 * q + 1
                                                    FermionMapping.OperatorType = FermionMapping.Annihilation
                                                }
                                            ]
                                    }
                                // αβ: p↑ r↓ s↓ q↑
                                yield
                                    {
                                        FermionMapping.Coefficient = Complex(0.5 * g_pqrs, 0.0)
                                        FermionMapping.Operators =
                                            [
                                                {
                                                    FermionMapping.OrbitalIndex = 2 * p
                                                    FermionMapping.OperatorType = FermionMapping.Creation
                                                }
                                                {
                                                    FermionMapping.OrbitalIndex = 2 * r + 1
                                                    FermionMapping.OperatorType = FermionMapping.Creation
                                                }
                                                {
                                                    FermionMapping.OrbitalIndex = 2 * s + 1
                                                    FermionMapping.OperatorType = FermionMapping.Annihilation
                                                }
                                                {
                                                    FermionMapping.OrbitalIndex = 2 * q
                                                    FermionMapping.OperatorType = FermionMapping.Annihilation
                                                }
                                            ]
                                    }
                                // βα: p↓ r↑ s↑ q↓
                                yield
                                    {
                                        FermionMapping.Coefficient = Complex(0.5 * g_pqrs, 0.0)
                                        FermionMapping.Operators =
                                            [
                                                {
                                                    FermionMapping.OrbitalIndex = 2 * p + 1
                                                    FermionMapping.OperatorType = FermionMapping.Creation
                                                }
                                                {
                                                    FermionMapping.OrbitalIndex = 2 * r
                                                    FermionMapping.OperatorType = FermionMapping.Creation
                                                }
                                                {
                                                    FermionMapping.OrbitalIndex = 2 * s
                                                    FermionMapping.OperatorType = FermionMapping.Annihilation
                                                }
                                                {
                                                    FermionMapping.OrbitalIndex = 2 * q + 1
                                                    FermionMapping.OperatorType = FermionMapping.Annihilation
                                                }
                                            ]
                                    }
        ]

    /// Build molecular Hamiltonian from real molecular integrals
    ///
    /// This function uses integrals provided by external quantum chemistry packages
    /// (PySCF, Psi4, etc.) to construct an accurate qubit Hamiltonian.
    ///
    /// PRECONDITIONS (validated):
    /// - NumOrbitals ≤ 10 (expands to 20 qubits)
    /// - OneElectron.Integrals dimensions match NumOrbitals
    /// - TwoElectron.Integrals dimensions match NumOrbitals
    /// - NumElectrons > 0
    ///
    /// PRECONDITIONS (not validated - caller responsibility):
    /// - Integrals must be in MO basis (not AO)
    /// - Two-electron integrals must use chemist notation (pq|rs)
    /// - All values in Hartree
    ///
    /// Returns: (ProblemHamiltonian, nuclearRepulsion) where nuclearRepulsion should
    /// be added to the VQE energy to get the total molecular energy.
    let buildFromIntegrals
        (integrals: MolecularIntegrals)
        (mapping: MappingMethod)
        : Result<QaoaCircuit.ProblemHamiltonian * float, QuantumError> =
        result {
            let n = integrals.NumOrbitals
            let numSpinOrbitals = n * 2

            // Validate qubit count
            do!
                if numSpinOrbitals > Types.NisqPracticalQubits then
                    Error(
                        QuantumError.ValidationError(
                            "MoleculeSize",
                            $"Molecule too large: {n} spatial orbitals → {numSpinOrbitals} spin orbitals (max {Types.NisqPracticalQubits}). "
                            + "Consider using active space selection to reduce orbital count."
                        )
                    )
                else
                    Ok()

            // Validate electron count
            do!
                if integrals.NumElectrons <= 0 then
                    Error(
                        QuantumError.ValidationError(
                            "Integrals",
                            $"Invalid electron count: {integrals.NumElectrons}. Must be positive."
                        )
                    )
                else
                    Ok()

            // Validate one-electron integral dimensions
            let h1Rows = integrals.OneElectron.Integrals.GetLength 0
            let h1Cols = integrals.OneElectron.Integrals.GetLength 1

            do!
                if h1Rows <> n || h1Cols <> n then
                    Error(
                        QuantumError.ValidationError(
                            "Integrals",
                            $"One-electron integral dimension mismatch: got [{h1Rows}x{h1Cols}], expected [{n}x{n}]. "
                            + "Ensure OneElectron.Integrals dimensions match NumOrbitals."
                        )
                    )
                else
                    Ok()

            // Validate two-electron integral dimensions
            let g2D0 = integrals.TwoElectron.Integrals.GetLength 0
            let g2D1 = integrals.TwoElectron.Integrals.GetLength 1
            let g2D2 = integrals.TwoElectron.Integrals.GetLength 2
            let g2D3 = integrals.TwoElectron.Integrals.GetLength 3

            do!
                if g2D0 <> n || g2D1 <> n || g2D2 <> n || g2D3 <> n then
                    Error(
                        QuantumError.ValidationError(
                            "Integrals",
                            $"Two-electron integral dimension mismatch: got [{g2D0}x{g2D1}x{g2D2}x{g2D3}], expected [{n}x{n}x{n}x{n}]. "
                            + "Ensure TwoElectron.Integrals dimensions match NumOrbitals."
                        )
                    )
                else
                    Ok()

            // Build fermionic Hamiltonian from real integrals
            let fermionTerms = buildFermionTermsFromIntegrals integrals

            let fermionHamiltonian =
                {
                    FermionMapping.NumOrbitals = numSpinOrbitals
                    FermionMapping.Terms = fermionTerms
                }

            // Apply fermion-to-qubit mapping
            let qubitHamiltonian =
                match mapping with
                | JordanWigner
                | Empirical -> FermionMapping.JordanWigner.transform fermionHamiltonian
                | BravyiKitaev -> FermionMapping.BravyiKitaev.transform fermionHamiltonian

            // Convert to library format
            return (FermionMapping.toQaoaHamiltonian qubitHamiltonian, integrals.NuclearRepulsion)
        }

    /// Verified reference integrals: H2 in the STO-3G minimal basis at the
    /// equilibrium bond length R = 0.7414 Å (chemist notation (pq|rs), MO basis,
    /// energies in Hartree). Provided so examples and tests have a REAL, ready-to-use
    /// MolecularIntegrals value rather than fabricated placeholders.
    ///
    /// Validation: exact diagonalisation of the Jordan-Wigner Hamiltonian built from
    /// these integrals via `buildFromIntegrals` reproduces the known full-CI ground
    /// state to better than 0.1 kcal/mol (total energy ≈ -1.1373 Ha vs the literature
    /// FCI value -1.13727 Ha) — i.e. well within chemical accuracy (1 kcal/mol).
    /// Values are the standard STO-3G results (cf. Szabo & Ostlund; OpenFermion H2).
    let h2Sto3gIntegrals: MolecularIntegrals =
        let h1 = Array2D.zeroCreate 2 2
        h1.[0, 0] <- -1.252477
        h1.[1, 1] <- -0.475934
        let g2 = Array4D.zeroCreate 2 2 2 2
        g2.[0, 0, 0, 0] <- 0.674493 // (00|00)
        g2.[1, 1, 1, 1] <- 0.697398 // (11|11)
        g2.[0, 0, 1, 1] <- 0.663472 // (00|11) Coulomb
        g2.[1, 1, 0, 0] <- 0.663472 // (11|00)
        g2.[0, 1, 0, 1] <- 0.181287 // (01|01) exchange (+ symmetric partners)
        g2.[0, 1, 1, 0] <- 0.181287
        g2.[1, 0, 0, 1] <- 0.181287
        g2.[1, 0, 1, 0] <- 0.181287

        {
            NumOrbitals = 2
            NumElectrons = 2
            NuclearRepulsion = 0.713754 // 1/R, R = 0.7414 Å = 1.401045 bohr
            OneElectron = { NumOrbitals = 2; Integrals = h1 }
            TwoElectron = { NumOrbitals = 2; Integrals = g2 }
            ReferenceEnergy = Some -1.116707
        } // Hartree-Fock energy (2·h00 + (00|00) + Enuc)

    /// Build molecular Hamiltonian from molecule structure
    /// Returns ProblemHamiltonian with Pauli Z and ZZ terms
    ///
    /// NOTE: Uses empirical parameters tuned to reproduce known ground state energies
    /// for H2 and H2O. This is a simplification for prototype - production code would
    /// use full molecular orbital calculations (Hartree-Fock, etc.)
    ///
    /// For research-grade calculations, supply real molecular-orbital integrals to
    /// `buildFromIntegrals` (e.g. the bundled `h2Sto3gIntegrals`, or integrals loaded
    /// from an FCIDUMP file). `buildWithMapping` with JordanWigner/BravyiKitaev routes
    /// through an `IntegralProvider` and will not fabricate integrals.
    let build (molecule: Molecule) : Result<QaoaCircuit.ProblemHamiltonian, QuantumError> =
        result {
            // Validate molecule
            do! Molecule.validate molecule

            do!
                if molecule.Atoms.IsEmpty then
                    Error(QuantumError.ValidationError("Molecule", "Invalid molecule: no atoms"))
                elif Molecule.countElectrons molecule <= 0 then
                    Error(QuantumError.ValidationError("Molecule", "Invalid molecule: non-positive electron count"))
                else
                    Ok()

            // Empirical Hamiltonian parameters for known molecules
            // NOTE: These are POSITIVE - we negate the expectation value in measurement
            // Use composition-based identification to handle molecules loaded from files
            let knownMolecule = MoleculeIdentification.identify molecule

            let (numQubits, oneElectronCoeff, twoElectronCoeff) =
                match knownMolecule with
                | Some "H2" ->
                    // H2: 2 qubits, empirical parameters tuned to give ~-1.174 Hartree
                    // Electronic energy target: ~-2.5 (to offset +1.35 nuclear repulsion)
                    (2, 1.3, 0.05)
                | Some "H2O" ->
                    // H2O: 6 qubits (3 atoms × 2 orbitals), empirical parameters
                    // Need large values to reach -76.0 with nuclear repulsion
                    (6, 13.0, 0.1)
                | _ ->
                    // Generic: 2 qubits per atom (minimal basis approximation)
                    let nq = molecule.Atoms.Length * 2
                    (nq, 1.0, 0.5)

            do!
                if numQubits > Types.NisqPracticalQubits then
                    Error(
                        QuantumError.ValidationError(
                            "MoleculeSize",
                            $"Molecule too large: {numQubits} qubits required (max {Types.NisqPracticalQubits})"
                        )
                    )
                else
                    Ok()

            // Build Hamiltonian terms
            // One-electron terms (Z operators)
            let oneElectronTerms =
                [|
                    for i in 0 .. numQubits - 1 ->
                        {
                            Coefficient = oneElectronCoeff
                            QubitsIndices = [| i |]
                            PauliOperators = [| QaoaCircuit.PauliZ |]
                        }
                        : QaoaCircuit.HamiltonianTerm
                |]

            // Two-electron terms (ZZ operators)
            let twoElectronTerms =
                [|
                    for i in 0 .. numQubits - 2 do
                        for j in i + 1 .. numQubits - 1 ->
                            {
                                Coefficient = twoElectronCoeff
                                QubitsIndices = [| i; j |]
                                PauliOperators = [| QaoaCircuit.PauliZ; QaoaCircuit.PauliZ |]
                            }
                            : QaoaCircuit.HamiltonianTerm
                |]

            // Return the constructed hamiltonian
            return
                {
                    QaoaCircuit.NumQubits = numQubits
                    QaoaCircuit.Terms = Array.append oneElectronTerms twoElectronTerms
                }
        }

    /// Build a Hamiltonian from a Molecule under the given mapping.
    ///
    /// PRODUCTION USE: supply an `IntegralProvider` that obtains real molecular-orbital
    /// integrals from an established quantum-chemistry package (PySCF, Psi4) or an FCIDUMP
    /// file. By design this library integrates those external tools for the integral
    /// evaluation rather than re-implementing it from scratch.
    ///
    /// - `Empirical`: returns the empirical prototype Hamiltonian (see `build`);
    ///   `integralProvider` is ignored.
    /// - `JordanWigner` / `BravyiKitaev`:
    ///     • With `Some provider` (the intended production path): calls `provider molecule`
    ///       for the integrals and delegates to `buildFromIntegrals` → a physically correct
    ///       Hamiltonian. Wire the provider to PySCF/Psi4 — see
    ///       examples/DrugDiscovery/PySCFIntegration.fsx.
    ///     • With `None`: returns `Error` — a fermionic mapping requires real molecular
    ///       integrals. Supply an `IntegralProvider` (PySCF/Psi4/FCIDUMP), or call
    ///       `buildFromIntegrals` with integrals you already hold (e.g. `h2Sto3gIntegrals`).
    ///
    /// Shortcut when you already hold integrals: call `buildFromIntegrals` directly with a
    /// `MolecularIntegrals` value such as the bundled `h2Sto3gIntegrals`, or integrals
    /// loaded from an FCIDUMP file via `Molecule.fromFciDumpFileTask`.
    let rec buildWithMapping
        (molecule: Molecule)
        (mapping: MappingMethod)
        (integralProvider: IntegralProvider option)
        : Result<QaoaCircuit.ProblemHamiltonian, QuantumError> =
        result {
            // Validate molecule
            do! Molecule.validate molecule

            do!
                if molecule.Atoms.IsEmpty then
                    Error(QuantumError.ValidationError("Molecule", "Invalid molecule: no atoms"))
                elif Molecule.countElectrons molecule <= 0 then
                    Error(QuantumError.ValidationError("Molecule", "Invalid molecule: non-positive electron count"))
                else
                    Ok()

            return!
                match mapping with
                | Empirical ->
                    // Delegate to original empirical build
                    build molecule

                | JordanWigner
                | BravyiKitaev ->
                    // Production path: an IntegralProvider supplies real MO integrals from an
                    // external chemistry package (PySCF/Psi4/FCIDUMP). A fermionic mapping is
                    // meaningless without real integrals, so `None` is a hard Error rather than
                    // a silently non-physical placeholder.
                    match integralProvider with
                    | Some provider ->
                        match provider molecule with
                        | Ok integrals ->
                            // Real integrals → physically correct Hamiltonian.
                            buildFromIntegrals integrals mapping |> Result.map fst
                        | Error msg ->
                            Error(
                                QuantumError.ValidationError(
                                    "IntegralProvider",
                                    $"Integral provider failed for molecule '{molecule.Name}': {msg}. "
                                    + "Check the provider (e.g. PySCF/Psi4 wrapper) or load integrals from "
                                    + "an FCIDUMP file via FciDumpIntegrals.fromFile."
                                )
                            )
                    | None ->
                        Error(
                            QuantumError.ValidationError(
                                "IntegralProvider",
                                $"The %A{mapping} fermionic mapping requires real molecular integrals, "
                                + "but no IntegralProvider was supplied. Pass an IntegralProvider "
                                + "(PySCF/Psi4, FciDumpIntegrals.fromFile, or Sto3gIntegrals.provider for H/He), "
                                + "or call buildFromIntegrals with integrals you already hold "
                                + "(e.g. h2Sto3gIntegrals). Use the Empirical mapping for a "
                                + "provider-free prototype Hamiltonian."
                            )
                        )
        }



/// What produced the energy of a ground-state result.
type EnergySource =
    /// UCCSD-VQE on the Jordan-Wigner Hamiltonian of the SolverConfig.IntegralProvider's integrals.
    | ProviderIntegrals
    /// UCCSD-VQE on the Jordan-Wigner Hamiltonian of the STO-3G integrals the library computes
    /// itself (Sto3gIntegrals: RHF, molecules of H and He atoms only).
    | ComputedSto3gIntegrals
    /// UCCSD-VQE on the Jordan-Wigner Hamiltonian of the 6-31G integrals the library computes
    /// itself (Sto3gIntegrals.computeInBasis "6-31G": RHF, molecules of H and He atoms only).
    | Computed631gIntegrals
    /// Hardware-efficient VQE on the empirical prototype Hamiltonian of MolecularHamiltonian.build;
    /// not a physical molecular energy.
    | EmpiricalHamiltonian
    /// Quantum phase estimation (QPE.run) of the Trotterised time evolution e^(-iHt) of the
    /// Jordan-Wigner Hamiltonian of the provider's or the library's own integrals; see
    /// VQEResult.Estimation for t, Trotter steps, counting qubits and the outcome peaks.
    | QpeTrotterEvolution
    /// ClassicalDFT.run's tabulated reference value; no quantum circuit ran.
    | TabulatedReference

/// Tabulated classical reference energies for H2, H2O and LiH near equilibrium, matched by
/// atomic composition (not by name or geometry). Runs no circuit; callable on its own or
/// through GroundStateMethod.ClassicalDFT, never as a substitute for VQE.
module ClassicalDFT =

    let private empiricalEnergies =
        Map [ ("H2", -1.174); ("H2O", -76.0); ("LiH", -8.0) ]

    /// True when the molecule is the state the table describes: neutral, singlet, and within
    /// 0.05 Å of the equilibrium bond lengths (H-H 0.741, Li-H 1.595, O-H 0.958 Å) and, for
    /// water, 5° of the 104.5° H-O-H angle.
    let private isTabulatedState (name: string) (molecule: Molecule) =
        let atomsOf (element: string) =
            molecule.Atoms |> List.filter (fun a -> a.Element.ToUpperInvariant() = element)

        let near (expected: float) (actual: float) = abs (actual - expected) <= 0.05

        molecule.Charge = 0
        && molecule.Multiplicity = 1
        && (match name, atomsOf "H" with
            | "H2", [ a; b ] -> near 0.741 (Molecule.calculateBondLength a b)
            | "LiH", [ h ] -> near 1.595 (Molecule.calculateBondLength (atomsOf "LI").Head h)
            | "H2O", [ h1; h2 ] ->
                let o = (atomsOf "O").Head
                let d1 = Molecule.calculateBondLength o h1
                let d2 = Molecule.calculateBondLength o h2
                let d12 = Molecule.calculateBondLength h1 h2

                let angle =
                    acos ((d1 * d1 + d2 * d2 - d12 * d12) / (2.0 * d1 * d2)) * 180.0 / Math.PI

                near 0.958 d1 && near 0.958 d2 && abs (angle - 104.5) <= 5.0
            | _ -> false)

    /// The tabulated energy of a neutral singlet H2, H2O or LiH near its equilibrium
    /// geometry (matched by composition and geometry, not by name). Other charges, spin
    /// states and geometries are an Error: the table has no value for them.
    let run (molecule: Molecule) (config: SolverConfig) : Async<Result<float, QuantumError>> =
        async {
            match MoleculeIdentification.identify molecule with
            | Some name when not (isTabulatedState name molecule) ->
                return
                    Error(
                        QuantumError.ValidationError(
                            "Molecule",
                            $"'{molecule.Name}' ({name}, charge {molecule.Charge}, multiplicity {molecule.Multiplicity}) is not the "
                            + "state the table describes: a neutral singlet within 0.05 Å of the equilibrium bond lengths "
                            + "(H-H 0.741, Li-H 1.595, O-H 0.958 Å; H-O-H 104.5° ± 5°). Use VQE with integrals for its geometry."
                        )
                    )
            | Some name when empiricalEnergies.ContainsKey name -> return Ok empiricalEnergies.[name]
            | _ ->
                return
                    Error(
                        QuantumError.ValidationError(
                            "Molecule",
                            $"No tabulated reference for the composition of '{molecule.Name}' (tabulated: H2, H2O, LiH)"
                        )
                    )
        }

/// VQE (Variational Quantum Eigensolver) implementation
///
/// RULE1 COMPLIANT: Uses IQuantumBackend for all quantum operations.
/// All state initialization, gate application, and measurement go through the backend.
module VQE =

    open BackendAbstraction
    open FSharp.Azure.Quantum.CircuitBuilder
    open FSharp.Azure.Quantum.Backends

    /// VQE optimization result with metadata
    type VQEResult =
        {
            /// Optimized ground state energy
            Energy: float
            /// Optimal variational parameters found
            OptimalParameters: float[]
            /// Number of iterations performed
            Iterations: int
            /// Whether optimization converged within tolerance
            Converged: bool
            /// Energy history for convergence plotting (iteration -> energy)
            EnergyHistory: (int * float) list
            /// What produced Energy
            Source: EnergySource
            /// True when SolverConfig.ErrorMitigation corrected sampled measurements. False when
            /// no strategy was given, or when the backend's statevector gave exact expectation
            /// values with no readout to correct.
            ErrorMitigationApplied: bool
            /// How Energy was estimated: exact expectation values, or samples (shots per
            /// circuit, circuits executed); NotEstimated for tabulated and proxy values
            Estimation: EnergyEstimation
            /// Caveats on how Energy was obtained that the other fields do not show, e.g. that
            /// no RHF solution converged and the VQE started from core-Hamiltonian orbitals.
            /// Empty when there are none.
            Notes: string list
        }

    /// Gates of the hardware-efficient ansatz, in program order: per layer of numQubits
    /// parameters, an RY on every qubit and a CNOT chain.
    let private ansatzGates (numQubits: int) (parameters: float[]) : Gate list =
        parameters
        |> Array.chunkBySize numQubits
        |> Array.toList
        |> List.collect (fun layerParams ->
            (layerParams |> Array.mapi (fun i theta -> RY(i, theta)) |> Array.toList)
            @ [ for i in 0 .. numQubits - 2 -> CNOT(i, i + 1) ])

    /// Build and apply parameterized ansatz circuit through backend
    ///
    /// RULE1: Uses backend.ApplyOperation instead of LocalSimulator.Gates directly
    let private buildAndApplyAnsatz
        (backend: IQuantumBackend)
        (numQubits: int)
        (parameters: float[])
        (initialState: QuantumState)
        : Result<QuantumState, QuantumError> =

        // Apply all gates sequentially through the backend
        UnifiedBackend.applySequence
            backend
            (ansatzGates numQubits parameters |> List.map QuantumOperation.Gate)
            initialState

    /// Samples per energy on the empirical-Hamiltonian path.
    [<Literal>]
    let private empiricalShots = 1000

    /// Measure energy expectation value through backend
    ///
    /// RULE1: Uses QuantumState.measure instead of LocalSimulator.Measurement directly
    /// NOTE: Negates result because Hamiltonian coefficients are positive
    /// but we want to minimize energy (occupied orbitals lower energy)
    ///
    /// When errorMitigation is provided, corrects the measured histogram (keys most
    /// significant qubit first, the ReadoutErrorMitigation convention) before computing the
    /// expectation value; a strategy that fails or corrects nothing is an Error.
    let private measureExpectation
        (hamiltonian: QaoaCircuit.ProblemHamiltonian)
        (errorMitigation: ErrorMitigationStrategy.RecommendedStrategy option)
        (state: QuantumState)
        : Result<float, QuantumError> =

        let shots = empiricalShots

        // Measurement arrays are least significant qubit first: bits.[q] is qubit q.
        let histogram =
            QuantumState.measure state shots
            |> Array.countBy (Array.rev >> Array.map string >> String.concat "")
            |> Map.ofArray

        let basisIndexOf (key: string) = Convert.ToInt32(key, 2)

        let weighted =
            match errorMitigation with
            | None -> Ok(histogram |> Map.map (fun _ count -> float count))
            | Some strategy ->
                let unbiased =
                    { ReadoutErrorMitigation.defaultConfig with
                        ClipNegative = false
                        MinProbability = 0.0
                    }

                match ErrorMitigationStrategy.applyStrategyWith unbiased histogram strategy with
                | Ok mitigated when mitigated.CorrectionApplied -> Ok mitigated.Histogram
                | Ok _ ->
                    Error(
                        QuantumError.ValidationError(
                            "ErrorMitigation",
                            "the strategy performed no correction (readout mitigation without a calibration matrix)"
                        )
                    )
                | Error err -> Error err

        weighted
        |> Result.map (fun counts ->
            // Total weight may differ from the shot count after mitigation (quasi-probabilities)
            let totalWeight = counts |> Map.toSeq |> Seq.sumBy snd |> max 1e-12

            let positiveExpectation =
                hamiltonian.Terms
                |> Array.sumBy (fun (term: QaoaCircuit.HamiltonianTerm) ->
                    let expectation =
                        counts
                        |> Map.toSeq
                        |> Seq.sumBy (fun (key, count) ->
                            let basisIndex = basisIndexOf key

                            let eigenvalue =
                                term.QubitsIndices
                                |> Array.fold
                                    (fun acc qubitIdx ->
                                        if (basisIndex &&& (1 <<< qubitIdx)) <> 0 then -acc else acc)
                                    1.0

                            eigenvalue * (count / totalWeight))

                    term.Coefficient * expectation)

            // Negate to make occupied orbitals (|1⟩) contribute negatively
            -positiveExpectation)

    /// Optimize VQE parameters using gradient descent
    ///
    /// RULE1: Uses backend for all quantum operations
    /// Supports optional error mitigation for noisy backends
    ///
    /// Each energy runs the ansatz gate by gate and samples the state (SampledGateByGate). A
    /// backend that refuses gate-by-gate application (cloud hardware) gets the ansatz as whole
    /// circuits instead, one per qubit-wise commuting group of terms
    /// (ChemistryVQE.sampledExpectation), and the energy comes from the job's outcome
    /// frequencies (SampledCircuits).
    let private optimizeParameters
        (backend: IQuantumBackend)
        (hamiltonian: QaoaCircuit.ProblemHamiltonian)
        (initialParameters: float[])
        (maxIterations: int)
        (tolerance: float)
        (progressReporter: Progress.IProgressReporter option)
        (errorMitigation: ErrorMitigationStrategy.RecommendedStrategy option)
        : Result<VQEResult, QuantumError> =

        let numQubits = hamiltonian.NumQubits
        let wholeCircuit = ref false
        let circuitsExecuted = ref 0

        let qubitHamiltonian = lazy (FermionMapping.fromQaoaHamiltonian hamiltonian)

        /// Energy from whole circuits. measureExpectation negates Σ c·⟨Z…⟩, so this does too.
        let wholeCircuitEnergy (parameters: float[]) =
            let circuit: Circuit =
                {
                    QubitCount = numQubits
                    Gates = List.rev (ansatzGates numQubits parameters)
                }

            FermionMapping.ChemistryVQE.sampledExpectation backend errorMitigation circuit qubitHamiltonian.Value
            |> Result.map (fun sampled ->
                circuitsExecuted.Value <- circuitsExecuted.Value + sampled.Circuits
                -sampled.Energy)

        let energyAt (parameters: float[]) : Result<float, QuantumError> =
            if wholeCircuit.Value then
                wholeCircuitEnergy parameters
            else
                match
                    backend.InitializeState numQubits
                    |> Result.bind (buildAndApplyAnsatz backend numQubits parameters)
                with
                | Error err when UnifiedBackend.isIncrementalUnsupported err ->
                    wholeCircuit.Value <- true
                    wholeCircuitEnergy parameters
                | Error err -> Error err
                | Ok state -> measureExpectation hamiltonian errorMitigation state

        let estimation () =
            if wholeCircuit.Value then
                let shots =
                    match backend with
                    | :? IShotSamplingBackend as sampling when sampling.Shots > 0 -> Some sampling.Shots
                    | _ -> None

                SampledCircuits(
                    FermionMapping.ChemistryVQE.measurementGroups qubitHamiltonian.Value
                    |> List.length,
                    shots,
                    circuitsExecuted.Value
                )
            else
                SampledGateByGate empiricalShots

        let rec loop iteration currentParameters prevEnergy energyHistory =
            if iteration > maxIterations then
                energyAt currentParameters
                |> Result.map (fun finalEnergy ->
                    {
                        Energy = finalEnergy
                        OptimalParameters = currentParameters
                        Iterations = iteration
                        Converged = false // Hit max iterations without converging
                        EnergyHistory = List.rev energyHistory
                        Source = EmpiricalHamiltonian
                        ErrorMitigationApplied = errorMitigation.IsSome
                        Estimation = estimation ()
                        Notes = []
                    })
            else
                match energyAt currentParameters with
                | Error err -> Error err
                | Ok energy ->

                    // Record energy for convergence plotting
                    let energyHistory' = (iteration, energy) :: energyHistory

                    // Report progress
                    progressReporter
                    |> Option.iter (fun r -> r.Report(Progress.IterationUpdate(iteration, maxIterations, Some energy)))

                    if abs (energy - prevEnergy) < tolerance then
                        Ok
                            {
                                Energy = energy
                                OptimalParameters = currentParameters
                                Iterations = iteration
                                Converged = true // Converged within tolerance
                                EnergyHistory = List.rev energyHistory'
                                Source = EmpiricalHamiltonian
                                ErrorMitigationApplied = errorMitigation.IsSome
                                Estimation = estimation ()
                                Notes = []
                            }
                    else
                        let learningRate = 0.1
                        let epsilon = 0.01

                        // Compute gradients (with potential errors)
                        let gradientsResult =
                            currentParameters
                            |> Array.mapi (fun i paramValue ->
                                let perturbedParameters = Array.copy currentParameters
                                perturbedParameters.[i] <- paramValue + epsilon

                                energyAt perturbedParameters
                                |> Result.map (fun energyForward ->
                                    let gradient = (energyForward - energy) / epsilon
                                    paramValue - learningRate * gradient))
                            |> Array.fold
                                (fun acc r ->
                                    match acc, r with
                                    | Error e, _ -> Error e
                                    | _, Error e -> Error e
                                    | Ok paramList, Ok newParam -> Ok(newParam :: paramList))
                                (Ok [])
                            |> Result.map (List.rev >> Array.ofList)

                        gradientsResult
                        |> Result.bind (fun updatedParameters ->
                            loop (iteration + 1) updatedParameters energy energyHistory')

        loop 1 initialParameters Double.MaxValue []

    /// True when every atom is H or He, the elements Sto3gIntegrals computes integrals for.
    let private isHydrogenHelium (molecule: Molecule) =
        molecule.Atoms
        |> List.forall (fun a ->
            let e = a.Element.ToUpperInvariant()
            e = "H" || e = "HE")

    /// Records the energies UCCSD-VQE reports per iteration and forwards every event.
    type private EnergyHistoryReporter(inner: Progress.IProgressReporter option) =
        let history = ResizeArray<int * float>()

        member _.History = List.ofSeq history

        interface Progress.IProgressReporter with
            member _.Report event =
                match event with
                | Progress.IterationUpdate(iteration, _, Some energy) -> history.Add((iteration, energy))
                | _ -> ()

                inner |> Option.iter (fun r -> r.Report event)

            member _.IsCancellationRequested =
                inner |> Option.exists (fun r -> r.IsCancellationRequested)

    /// Largest UCCSD parameter count VQE.run admits, checked before the qubit Hamiltonian is
    /// built. Each optimisation iteration evaluates the energy twice per parameter; 52
    /// parameters (a (4e,4o) active space, 8 qubits) took 3 to 4 minutes on the local
    /// simulator, and the count grows as the fourth power of the active-space size.
    [<Literal>]
    let MaxUccsdParameters = 64

    /// UCCSD parameter count for `electrons` in `spinOrbitals`: singles + doubles.
    let uccsdParameterCount (electrons: int) (spinOrbitals: int) =
        let virtuals = spinOrbitals - electrons

        electrons * virtuals
        + (electrons * (electrons - 1) / 2) * (virtuals * (virtuals - 1) / 2)

    /// Molecular integrals for VQE: the IntegralProvider's when one is configured, else the
    /// library's integrals in `basis` for molecules of H and He atoms, else None. Provider
    /// integrals must describe a closed shell (even electron count, molecule multiplicity 1)
    /// or a single electron (multiplicity 2), the states UCCSD-VQE from a Hartree-Fock
    /// reference can honour.
    let internal resolveIntegrals
        (basis: string)
        (molecule: Molecule)
        (config: SolverConfig)
        : Result<(MolecularIntegrals * EnergySource) option, QuantumError> =
        result {
            do! Molecule.validate molecule

            // The electron count comes from the integrals when a provider supplies them
            // (an FCIDUMP-loaded molecule has placeholder atoms).
            do!
                if molecule.Atoms.IsEmpty then
                    Error(QuantumError.ValidationError("Molecule", "Invalid molecule: no atoms"))
                elif config.IntegralProvider.IsNone && Molecule.countElectrons molecule <= 0 then
                    Error(QuantumError.ValidationError("Molecule", "Invalid molecule: non-positive electron count"))
                else
                    Ok()

            match config.IntegralProvider with
            | Some provider ->
                let provided =
                    try
                        provider molecule
                    with ex ->
                        Error ex.Message

                match provided with
                | Ok integrals when
                    (integrals.NumElectrons % 2 = 0 && molecule.Multiplicity <> 1)
                    || (integrals.NumElectrons % 2 = 1
                        && (integrals.NumElectrons <> 1 || molecule.Multiplicity <> 2))
                    ->
                    return!
                        Error(
                            QuantumError.ValidationError(
                                "Multiplicity",
                                $"'{molecule.Name}': {integrals.NumElectrons} active electrons with multiplicity {molecule.Multiplicity}; "
                                + "UCCSD-VQE supports closed shells (even electron count, multiplicity 1) and one-electron "
                                + "doublets, since its ansatz does not conserve spin"
                            )
                        )
                | Ok integrals -> return Some(integrals, ProviderIntegrals)
                | Error msg ->
                    return!
                        Error(
                            QuantumError.ValidationError(
                                "IntegralProvider",
                                $"Integral provider failed for molecule '{molecule.Name}': {msg}"
                            )
                        )
            | None when isHydrogenHelium molecule ->
                let! integrals = Sto3gIntegrals.computeInBasis basis molecule

                let source =
                    if String.Equals(basis.Trim(), "6-31G", StringComparison.OrdinalIgnoreCase) then
                        Computed631gIntegrals
                    else
                        ComputedSto3gIntegrals

                return Some(integrals, source)
            | None -> return None
        }

    /// UCCSD-VQE on the Jordan-Wigner Hamiltonian of the given integrals. Energy includes the
    /// integrals' nuclear repulsion. A Hamiltonian wider than the backend can run, or an ansatz
    /// with more than MaxUccsdParameters parameters, is an Error, found before the
    /// Hamiltonian is built. `notes` become VQEResult.Notes.
    let private runOnIntegrals
        (backend: IQuantumBackend)
        (integrals: MolecularIntegrals)
        (source: EnergySource)
        (notes: string list)
        (config: SolverConfig)
        : Async<Result<VQEResult, QuantumError>> =
        async {
            // Jordan-Wigner: one qubit per spin orbital.
            let numQubits = 2 * integrals.NumOrbitals
            let parameters = uccsdParameterCount integrals.NumElectrons numQubits

            match UnifiedBackend.getRunnableQubits backend with
            | Some limit when numQubits > limit ->
                return
                    Error(
                        QuantumError.ValidationError(
                            "MoleculeSize",
                            $"UCCSD-VQE needs {numQubits} qubits ({integrals.NumOrbitals} spatial orbitals); "
                            + $"backend '{backend.Name}' runs at most {limit}"
                        )
                    )
            // Wider than the NISQ budget: buildFromIntegrals refuses it before building anything.
            | _ when parameters > MaxUccsdParameters && numQubits <= Types.NisqPracticalQubits ->
                return
                    Error(
                        QuantumError.ValidationError(
                            "MoleculeSize",
                            $"UCCSD for {integrals.NumElectrons} electrons in {integrals.NumOrbitals} spatial orbitals has "
                            + $"{parameters} parameters (VQE.MaxUccsdParameters = {MaxUccsdParameters}). Choose a smaller "
                            + "active space in a chemistry package and pass it as an IntegralProvider or FCIDUMP file "
                            + "(FciDumpIntegrals.fromFile)"
                        )
                    )
            | _ ->
                match MolecularHamiltonian.buildFromIntegrals integrals MolecularHamiltonian.JordanWigner with
                | Error err -> return Error err
                | Ok(hamiltonian, nuclearRepulsion) ->
                    config.ProgressReporter
                    |> Option.iter (fun r ->
                        r.Report(
                            Progress.PhaseChanged(
                                "VQE Optimization",
                                Some $"UCCSD on a {numQubits}-qubit Hamiltonian..."
                            )
                        ))

                    let historyReporter = EnergyHistoryReporter(config.ProgressReporter)

                    let vqeConfig: FermionMapping.ChemistryVQE.ChemistryVQEConfig =
                        {
                            Hamiltonian = FermionMapping.fromQaoaHamiltonian hamiltonian
                            Ansatz = FermionMapping.ChemistryVQE.UCCSD(integrals.NumElectrons, numQubits)
                            MaxIterations = config.MaxIterations
                            Tolerance = config.Tolerance
                            UseHFInitialState = true
                            Backend = backend
                            ProgressReporter = Some(historyReporter :> Progress.IProgressReporter)
                        }

                    let! vqeResult =
                        FermionMapping.ChemistryVQE.runWith config.InitialParameters config.ErrorMitigation vqeConfig

                    return
                        vqeResult
                        |> Result.map (fun r ->
                            {
                                Energy = r.Energy + nuclearRepulsion
                                OptimalParameters = r.OptimalParameters
                                Iterations = r.Iterations
                                Converged = r.Converged
                                EnergyHistory =
                                    historyReporter.History
                                    |> List.map (fun (iteration, energy) -> (iteration, energy + nuclearRepulsion))
                                Source = source
                                // Exact expectations have no readout to correct.
                                ErrorMitigationApplied =
                                    config.ErrorMitigation.IsSome && r.Estimation <> ExactExpectation
                                Estimation = r.Estimation
                                Notes = notes @ r.Notes
                            })
        }

    /// Run VQE to estimate ground state energy
    ///
    /// RULE1 COMPLIANT: Requires IQuantumBackend parameter
    /// All quantum operations go through the backend abstraction.
    ///
    /// Hamiltonian, in order of precedence:
    /// - SolverConfig.IntegralProvider's integrals → UCCSD-VQE (Source = ProviderIntegrals);
    /// - molecules of H and He atoms → integrals in `basis` ("STO-3G" or "6-31G") from
    ///   Sto3gIntegrals.computeInBasis, UCCSD-VQE (Source = ComputedSto3gIntegrals or
    ///   Computed631gIntegrals); closed shells and one-electron doublets only;
    /// - H2O or LiH → Error: they need an IntegralProvider;
    /// - any other molecule → hardware-efficient VQE on the empirical prototype Hamiltonian
    ///   (Source = EmpiricalHamiltonian).
    /// UCCSD with more than MaxUccsdParameters parameters is an Error. Never substitutes a
    /// tabulated energy; ClassicalDFT.run returns those on request. `basis` applies only to
    /// the library's own H/He integrals: provider integrals carry their own basis.
    let runInBasis
        (basis: string)
        (molecule: Molecule)
        (config: SolverConfig)
        : Async<Result<VQEResult, QuantumError>> =
        async {
            // Get backend (RULE1: backend is required)
            let backend =
                config.Backend
                |> Option.defaultValue (LocalBackend.LocalBackend() :> IQuantumBackend)

            match resolveIntegrals basis molecule config, MoleculeIdentification.identify molecule with
            | Error err, _ -> return Error err
            | Ok(Some(integrals, source)), _ ->
                let notes =
                    match source with
                    | ComputedSto3gIntegrals
                    | Computed631gIntegrals when integrals.NumElectrons % 2 = 0 && integrals.ReferenceEnergy.IsNone ->
                        [
                            "No RHF solution converged; the integrals use core-Hamiltonian orbitals and UCCSD-VQE starts from their determinant. The Hamiltonian's spectrum does not depend on the orbitals."
                        ]
                    | _ -> []

                return! runOnIntegrals backend integrals source notes config
            | Ok None, Some knownName ->
                return
                    Error(
                        QuantumError.ValidationError(
                            "IntegralProvider",
                            $"VQE for '{molecule.Name}' ({knownName}) needs molecular integrals for its geometry. "
                            + "Supply SolverConfig.IntegralProvider (a PySCF/Psi4 wrapper, or FciDumpIntegrals.fromFile "
                            + "for an FCIDUMP file); the library computes integrals itself only for H and He atoms. "
                            + "ClassicalDFT.run returns a tabulated reference energy without running a circuit."
                        )
                    )
            | Ok None, None ->
                // Generic VQE for unknown molecules (may be less accurate)
                match MolecularHamiltonian.build molecule with
                | Error err -> return Error err
                | Ok hamiltonian ->

                    let numQubits = hamiltonian.NumQubits
                    let numLayers = 2
                    let numParameters = numQubits * numLayers

                    let initialParameters =
                        match config.InitialParameters with
                        | Some providedParameters when providedParameters.Length >= numParameters ->
                            providedParameters |> Array.take numParameters
                        | _ ->
                            let rng = Random()
                            Array.init numParameters (fun _ -> rng.NextDouble() * 2.0 * Math.PI)

                    // Report VQE start
                    config.ProgressReporter
                    |> Option.iter (fun r ->
                        r.Report(
                            Progress.PhaseChanged("VQE Optimization", Some $"Optimizing {numQubits}-qubit system...")
                        ))

                    // Run optimization through backend (RULE1 compliant)
                    // Passes error mitigation strategy for noisy backend support
                    match
                        optimizeParameters
                            backend
                            hamiltonian
                            initialParameters
                            config.MaxIterations
                            config.Tolerance
                            config.ProgressReporter
                            config.ErrorMitigation
                    with
                    | Error err -> return Error err
                    | Ok vqeResult ->
                        return
                            Molecule.nuclearRepulsion molecule
                            |> Result.map (fun nuclearRepulsion ->
                                { vqeResult with
                                    Energy = vqeResult.Energy + nuclearRepulsion
                                    EnergyHistory =
                                        vqeResult.EnergyHistory
                                        |> List.map (fun (iteration, energy) -> (iteration, energy + nuclearRepulsion))
                                })
        }

    /// Run VQE to estimate ground state energy; H/He molecules without a provider use
    /// STO-3G integrals (see runInBasis).
    let run (molecule: Molecule) (config: SolverConfig) : Async<Result<VQEResult, QuantumError>> =
        runInBasis "STO-3G" molecule config

/// Hamiltonian Simulation using Trotter-Suzuki decomposition
///
/// RULE1 COMPLIANT: Uses IQuantumBackend for all quantum operations.
module HamiltonianSimulation =

    open FSharp.Azure.Quantum.Core.BackendAbstraction
    open FSharp.Azure.Quantum.CircuitBuilder

    /// Configuration for time evolution simulation
    type SimulationConfig =
        {
            /// Evolution time in atomic units
            Time: float

            /// Number of Trotter steps (more = more accurate, but deeper circuit)
            TrotterSteps: int

            /// Trotter order (1 or 2 supported)
            TrotterOrder: int

            /// Quantum backend for execution (None = LocalBackend)
            Backend: IQuantumBackend option
        }

    /// How simulateFromPreparation ran its circuit.
    type SimulationRoute =
        /// Gate by gate on a backend that applies gates one at a time: FinalState is the
        /// evolved state with its phases and Probabilities are exact.
        | GateByGate
        /// As one whole circuit (preparation + Trotter gates) on a backend that refuses
        /// gate-by-gate application, as cloud hardware does: only the measured outcome
        /// probabilities come back. `shots` is the backend's shot count when it samples
        /// (IShotSamplingBackend); None when it does not report one and its returned
        /// probabilities are used as they are.
        | WholeCircuit of shots: int option

    /// Result of simulateFromPreparation.
    type SimulationResult =
        {
            /// Probability of every computational basis state after the evolution (index i:
            /// qubit q = bit q of i); sampled frequencies on the WholeCircuit route
            Probabilities: float[]

            /// The evolved state with phases; None on the WholeCircuit route, where the backend
            /// returns measurement outcomes only
            FinalState: QuantumState option

            /// How the circuit ran
            Route: SimulationRoute
        }

    let private validate (config: SimulationConfig) : Result<unit, QuantumError> =
        if config.TrotterSteps <= 0 then
            Error(QuantumError.ValidationError("TrotterSteps", "must be positive"))
        elif config.TrotterOrder <> 1 && config.TrotterOrder <> 2 then
            Error(QuantumError.ValidationError("TrotterOrder", "Only Trotter order 1 and 2 are supported"))
        else
            Ok()

    let private backendOf (config: SimulationConfig) : IQuantumBackend =
        config.Backend
        |> Option.defaultValue (FSharp.Azure.Quantum.Backends.LocalBackend.LocalBackend() :> IQuantumBackend)

    /// Gate operations of exp(-iHt) by Trotter-Suzuki decomposition, in program order.
    let private trotterOperations
        (hamiltonian: QaoaCircuit.ProblemHamiltonian)
        (config: SimulationConfig)
        : QuantumOperation list =

        let deltaT = config.Time / float config.TrotterSteps

        /// Build gate operations for a single Hamiltonian term evolution exp(-iH_k * dt)
        ///
        /// Supports arbitrary Pauli strings (1, 2, 3+ qubits) using CNOT ladder decomposition:
        /// 1. Change of basis: X → H, Y → S†H, Z → I (no change)
        /// 2. CNOT ladder to concentrate parity on target qubit
        /// 3. RZ rotation by 2*coefficient*dt
        /// 4. Inverse CNOT ladder
        /// 5. Inverse change of basis
        let buildTermEvolutionGates (term: QaoaCircuit.HamiltonianTerm) (dt: float) : QuantumOperation list =
            let angle = term.Coefficient * dt

            // Find qubits with non-identity Pauli operators
            let nonIdentityQubits =
                Array.zip term.QubitsIndices term.PauliOperators
                |> Array.filter (fun (_, op) -> op <> QaoaCircuit.PauliI)

            match nonIdentityQubits.Length with
            | 0 ->
                // All identity - global phase, skip
                []

            | 1 ->
                // Single-qubit term: apply rotation gates directly
                let (qubit, pauli) = nonIdentityQubits[0]

                match pauli with
                | QaoaCircuit.PauliZ -> [ QuantumOperation.Gate(RZ(qubit, 2.0 * angle)) ]
                | QaoaCircuit.PauliX -> [ QuantumOperation.Gate(RX(qubit, 2.0 * angle)) ]
                | QaoaCircuit.PauliY -> [ QuantumOperation.Gate(RY(qubit, 2.0 * angle)) ]
                | QaoaCircuit.PauliI -> []

            | _ ->
                // Multi-qubit term (2, 3, or more qubits): use CNOT ladder decomposition
                // Algorithm: Change basis → CNOT ladder → RZ → inverse CNOT → inverse basis

                // Step 1: Change of basis gates (X→H, Y→S†H to convert to Z basis)
                let basisChangeGates =
                    nonIdentityQubits
                    |> Array.collect (fun (qubit, pauli) ->
                        match pauli with
                        | QaoaCircuit.PauliX -> [| QuantumOperation.Gate(H qubit) |]
                        | QaoaCircuit.PauliY -> [| QuantumOperation.Gate(SDG qubit); QuantumOperation.Gate(H qubit) |]
                        | QaoaCircuit.PauliI
                        | QaoaCircuit.PauliZ -> [||] // Z and I need no change
                    )
                    |> Array.toList

                // Step 2: CNOT ladder to concentrate parity on last qubit
                let targetQubit = fst nonIdentityQubits[nonIdentityQubits.Length - 1]

                let cnotLadderGates =
                    Array.init (max 0 ((nonIdentityQubits.Length - 2) + 1)) (fun i ->
                        let controlQubit = fst nonIdentityQubits[i]
                        QuantumOperation.Gate(CNOT(controlQubit, targetQubit)))
                    |> Array.toList

                // Step 3: RZ rotation on target qubit
                let rotationGate = [ QuantumOperation.Gate(RZ(targetQubit, 2.0 * angle)) ]

                // Step 4: Inverse CNOT ladder (same gates, reverse order)
                let inverseCnotLadderGates = List.rev cnotLadderGates

                // Step 5: Inverse basis change (reverse order, conjugate gates)
                let inverseBasisChangeGates =
                    nonIdentityQubits
                    |> Array.rev
                    |> Array.collect (fun (qubit, pauli) ->
                        match pauli with
                        | QaoaCircuit.PauliX -> [| QuantumOperation.Gate(H qubit) |] // H† = H
                        | QaoaCircuit.PauliY ->
                            [|
                                QuantumOperation.Gate(H qubit) // H† = H
                                QuantumOperation.Gate(S qubit)
                            |] // (S†)† = S
                        | QaoaCircuit.PauliI
                        | QaoaCircuit.PauliZ -> [||])
                    |> Array.toList

                // Combine all gates in order
                basisChangeGates
                @ cnotLadderGates
                @ rotationGate
                @ inverseCnotLadderGates
                @ inverseBasisChangeGates

        /// Build gates for one Trotter step (forward evolution through all terms)
        let buildForwardStepGates (dt: float) : QuantumOperation list =
            hamiltonian.Terms
            |> Array.toList
            |> List.collect (fun term -> buildTermEvolutionGates term dt)

        /// Build gates for one Trotter step (backward evolution through all terms - for 2nd order)
        let buildBackwardStepGates (dt: float) : QuantumOperation list =
            hamiltonian.Terms
            |> Array.rev
            |> Array.toList
            |> List.collect (fun term -> buildTermEvolutionGates term dt)

        /// Build all gates for a complete Trotter step based on order
        let buildTrotterStepGates () : QuantumOperation list =
            match config.TrotterOrder with
            | 1 ->
                // 1st order: forward evolution with full time step
                buildForwardStepGates deltaT

            | 2 ->
                // 2nd order: symmetric splitting (forward half + backward half)
                let halfDt = deltaT / 2.0
                buildForwardStepGates halfDt @ buildBackwardStepGates halfDt

            | _ -> []

        // Build all gates for all Trotter steps
        [ 1 .. config.TrotterSteps ] |> List.collect (fun _ -> buildTrotterStepGates ())

    /// Apply time evolution exp(-iHt) to a quantum state using Trotter decomposition
    ///
    /// Trotter-Suzuki formula (1st order):
    /// exp(-iHt) ≈ [exp(-iH₁Δt) exp(-iH₂Δt) ... exp(-iHₙΔt)]^r
    /// where Δt = t/r (r = number of Trotter steps)
    ///
    /// For 2nd order Trotter (symmetric):
    /// exp(-iHt) ≈ [exp(-iH₁Δt/2) ... exp(-iHₙΔt/2) exp(-iHₙΔt/2) ... exp(-iH₁Δt/2)]^r
    ///
    /// Runs gate by gate and returns the evolved state with its phases. A backend that refuses
    /// gate-by-gate application (cloud hardware) cannot take an arbitrary input state nor return
    /// one: that is an Error naming simulateFromPreparation, which starts from a gate
    /// preparation and returns the measured outcome probabilities.
    ///
    /// RULE1: All quantum operations go through IQuantumBackend
    let simulate
        (hamiltonian: QaoaCircuit.ProblemHamiltonian)
        (initialState: QuantumState)
        (config: SimulationConfig)
        : Result<QuantumState, QuantumError> =

        validate config
        |> Result.bind (fun () ->
            let backend = backendOf config

            match UnifiedBackend.applySequence backend (trotterOperations hamiltonian config) initialState with
            | Error e when UnifiedBackend.isIncrementalUnsupported e ->
                Error(
                    QuantumError.OperationError(
                        "HamiltonianSimulation",
                        $"Backend '{backend.Name}' runs whole circuits only, so it can neither start from an arbitrary state nor return the evolved state. Use HamiltonianSimulation.simulateFromPreparation with a gate preparation of the initial state; it returns the measured outcome probabilities."
                    )
                )
            | result -> result)

    /// Time evolution exp(-iHt) of the state `preparation` prepares from |0…0⟩, by the Trotter
    /// decomposition of `simulate`, returning the outcome probabilities.
    ///
    /// Gate by gate where the backend applies gates one at a time (Route = GateByGate, exact
    /// probabilities and the final state). A backend that refuses that (cloud hardware) gets the
    /// preparation and the Trotter gates as one whole circuit (UnifiedBackend.submitAsCircuit),
    /// and the result holds its measured outcome probabilities only (Route = WholeCircuit).
    let simulateFromPreparation
        (hamiltonian: QaoaCircuit.ProblemHamiltonian)
        (preparation: Circuit)
        (config: SimulationConfig)
        : Result<SimulationResult, QuantumError> =

        let probabilitiesOf (state: QuantumState) : Result<float[], QuantumError> =
            match state with
            | QuantumState.StateVector sv ->
                Ok(
                    Array.init (1 <<< LocalSimulator.StateVector.numQubits sv) (fun i ->
                        let a = LocalSimulator.StateVector.getAmplitude i sv
                        a.Real * a.Real + a.Imaginary * a.Imaginary)
                )
            | QuantumState.SparseState(amplitudes, n) ->
                let probabilities = Array.zeroCreate (1 <<< n)

                for KeyValue(i, a) in amplitudes do
                    probabilities.[i] <- a.Real * a.Real + a.Imaginary * a.Imaginary

                Ok probabilities
            | QuantumState.DensityMatrix(rho, n) -> Ok(Array.init (1 <<< n) (fun i -> rho.[i, i].Real))
            | _ ->
                Error(
                    QuantumError.OperationError(
                        "HamiltonianSimulation",
                        "the backend returned a state without basis-state probabilities"
                    )
                )

        validate config
        |> Result.bind (fun () ->
            if preparation.QubitCount <> hamiltonian.NumQubits then
                Error(
                    QuantumError.ValidationError(
                        "preparation",
                        $"acts on {preparation.QubitCount} qubits, the Hamiltonian on {hamiltonian.NumQubits}"
                    )
                )
            else
                let backend = backendOf config
                let numQubits = hamiltonian.NumQubits

                let operations =
                    (getGates preparation |> List.map QuantumOperation.Gate)
                    @ trotterOperations hamiltonian config

                match
                    backend.InitializeState numQubits
                    |> Result.bind (UnifiedBackend.applySequence backend operations)
                with
                | Error e when UnifiedBackend.isIncrementalUnsupported e ->
                    let shots =
                        match backend with
                        | :? IShotSamplingBackend as sampling when sampling.Shots > 0 -> Some sampling.Shots
                        | _ -> None

                    UnifiedBackend.submitAsCircuit backend numQubits operations
                    |> Result.bind probabilitiesOf
                    |> Result.map (fun probabilities ->
                        {
                            Probabilities = probabilities
                            FinalState = None
                            Route = WholeCircuit shots
                        })
                | Error e -> Error e
                | Ok state ->
                    probabilitiesOf state
                    |> Result.map (fun probabilities ->
                        {
                            Probabilities = probabilities
                            FinalState = Some state
                            Route = GateByGate
                        }))

/// Quantum phase estimation of molecular energies.
///
/// The Jordan-Wigner Hamiltonian H = Σ c_k P_k of the molecule's integrals (the sources
/// VQE uses: SolverConfig.IntegralProvider or an FCIDUMP, else the library's STO-3G or
/// 6-31G integrals for H and He) is shifted by an upper bound on its spectrum, so that
/// U = e^(-i(H - shift)t) has eigenphases φ = -(E - shift)t/2π in [0, 1) without aliasing:
///
///   shift = c_I + λ,  λ = Σ_{non-identity} |c_k|,  every eigenvalue lies in [c_I - λ, c_I + λ]
///   t = 2π(1 - 2/2^m) / 2λ    (two empty bins below φ = 1)
///   E = -2πφ/t + shift        (+ nuclear repulsion for the total energy)
///
/// Controlled-U^(2^j) repeats the same Trotter-Suzuki circuit for U 2^j times
/// (TrotterSuzuki.synthesizeControlledHamiltonianEvolution), so QPE measures the
/// eigenvalues of the Trotterised U: the Trotter error does not grow with 2^j. The
/// counting register is read with the inverse QFT of Algorithms.QPE, and the whole circuit
/// is submitted at once (UnifiedBackend.submitAsCircuit), so simulators and cloud backends
/// run the same circuit.
///
/// QPE returns eigenvalue E_k with probability |⟨ψ|E_k⟩|² for the prepared state ψ (the
/// Hartree-Fock determinant, or a UCCSD state): the result reports every peak of the
/// outcome distribution, and its Energy is the most probable one, which is the ground
/// state only when ψ overlaps the ground state most.
module QPE =

    open System.Numerics
    open FSharp.Azure.Quantum.Algorithms
    open FSharp.Azure.Quantum.Algorithms.TrotterSuzuki
    open FSharp.Azure.Quantum.CircuitBuilder

    /// Largest circuit (system + counting qubits) QPE.run builds.
    [<Literal>]
    let MaxTotalQubits = 16

    /// State the system register is prepared in before phase estimation.
    type InitialState =
        /// The Hartree-Fock determinant: the lowest spin orbitals occupied
        | HartreeFockState
        /// The UCCSD state at these amplitudes (singles first, then doubles), e.g. a VQE
        /// result's OptimalParameters; closer to the ground state than Hartree-Fock where
        /// correlation is strong
        | UccsdState of amplitudes: float[]

    /// Settings of a chemistry QPE run.
    type Settings =
        {
            /// Counting (phase) qubits; None takes min(8, MaxTotalQubits - system qubits)
            CountingQubits: int option
            /// Trotter-Suzuki order of the circuit for U: 1 or 2
            TrotterOrder: int
            /// Trotter steps in the circuit for U
            TrotterSteps: int
            /// Preparation of the system register
            InitialState: InitialState
            /// Basis of the library's own integrals for H/He molecules ("STO-3G" or "6-31G");
            /// provider and FCIDUMP integrals carry their own
            Basis: string
        }

    /// First-order Trotter with 4 steps per U and 8 counting qubits (where they fit). For
    /// H2/STO-3G at 0.7414 Å (12 qubits) the Trotter error of the ground eigenvalue is
    /// 0.7 mHa and the peak refinement recovers the phase between bins, so the energy is
    /// within chemical accuracy (1.6 mHa) of FCI; second order needs twice the gates per
    /// step for a similar error here (3 steps: 1.3 mHa).
    let defaultSettings =
        {
            CountingQubits = None
            TrotterOrder = 1
            TrotterSteps = 4
            InitialState = HartreeFockState
            Basis = "STO-3G"
        }

    /// The time evolution a QPE run applies and how its phases map to energies.
    type EvolutionPlan =
        {
            /// H - Shift: the identity coefficient carries -Shift
            ShiftedHamiltonian: PauliHamiltonian
            /// Evolution time t of U = e^(-i(H - Shift)t)
            Time: float
            /// Spectral upper bound c_I + λ subtracted from H
            Shift: float
            /// Counting qubits m
            CountingQubits: int
            /// Trotter configuration of one U (Time = t)
            Trotter: TrotterConfig
        }

    /// Pauli form of a qubit Hamiltonian (qubit q = spin orbital q).
    let toPauliHamiltonian (hamiltonian: Core.QaoaCircuit.ProblemHamiltonian) : PauliHamiltonian =
        let convertPauliOp (op: Core.QaoaCircuit.PauliOperator) : char =
            match op with
            | Core.QaoaCircuit.PauliI -> 'I'
            | Core.QaoaCircuit.PauliX -> 'X'
            | Core.QaoaCircuit.PauliY -> 'Y'
            | Core.QaoaCircuit.PauliZ -> 'Z'

        {
            Terms =
                hamiltonian.Terms
                |> Array.map (fun term ->
                    let operators = Array.create hamiltonian.NumQubits 'I'

                    Array.iter2
                        (fun qIdx pauliOp -> operators[qIdx] <- convertPauliOp pauliOp)
                        term.QubitsIndices
                        term.PauliOperators

                    {
                        Operators = operators
                        Coefficient = Complex(term.Coefficient, 0.0)
                    })
                |> Array.toList
            NumQubits = hamiltonian.NumQubits
        }

    let private isIdentity (term: PauliString) =
        term.Operators |> Array.forall (fun op -> op = 'I')

    /// The evolution for `countingQubits` counting qubits: shift = c_I + λ and
    /// t = 2π(1 - 2/2^m)/(2λ), so every eigenvalue maps to a phase in [0, 1 - 2/2^m].
    let evolutionPlan
        (hamiltonian: PauliHamiltonian)
        (countingQubits: int)
        (trotterOrder: int)
        (trotterSteps: int)
        : EvolutionPlan =
        let identity =
            hamiltonian.Terms
            |> List.filter isIdentity
            |> List.sumBy (fun t -> t.Coefficient.Real)

        let others = hamiltonian.Terms |> List.filter (isIdentity >> not)
        let lambda = others |> List.sumBy (fun t -> abs t.Coefficient.Real)
        let shift = identity + lambda
        let bins = float (1 <<< countingQubits)
        // A Hamiltonian of identity terms only has one eigenvalue: any t resolves it.
        let time = 2.0 * Math.PI * (1.0 - 2.0 / bins) / (2.0 * max lambda 1e-12)

        ({
            ShiftedHamiltonian =
                { hamiltonian with
                    Terms =
                        others
                        @ [
                            {
                                Operators = Array.create hamiltonian.NumQubits 'I'
                                Coefficient = Complex(identity - shift, 0.0)
                            }
                        ]
                }
            Time = time
            Shift = shift
            CountingQubits = countingQubits
            Trotter =
                {
                    NumSteps = trotterSteps
                    Time = time
                    Order = trotterOrder
                }
        }
        : EvolutionPlan)

    /// Electronic energy of phase φ: E = -2πφ/t + shift.
    let phaseToEnergy (plan: EvolutionPlan) (phase: float) : float =
        -2.0 * Math.PI * phase / plan.Time + plan.Shift

    /// Phase in [0, 1) of electronic energy E: φ = -(E - shift)t/2π mod 1.
    let energyToPhase (plan: EvolutionPlan) (energy: float) : float =
        let phase = -(energy - plan.Shift) * plan.Time / (2.0 * Math.PI)
        phase - floor phase

    /// The phase-estimation circuit: the system register on qubits 0 .. n-1 (prepared by
    /// `preparation`), counting qubits n .. n+m-1 in |+⟩, controlled-U^(2^j) from counting
    /// qubit n+j, then the inverse QFT on the counting register (read it with
    /// Algorithms.QPE.countingOutcome on `countingQubitsOf`).
    let circuit (plan: EvolutionPlan) (preparation: Gate list) : Circuit =
        let n = plan.ShiftedHamiltonian.NumQubits
        let counting = [| n .. n + plan.CountingQubits - 1 |]
        let system = [| 0 .. n - 1 |]

        let start =
            CircuitBuilder.empty (n + plan.CountingQubits)
            |> CircuitBuilder.addGates (preparation @ [ for q in counting -> H q ])

        let evolved =
            counting
            |> Array.indexed
            |> Array.fold
                (fun circ (j, control) ->
                    let repetitions = 1 <<< j

                    synthesizeControlledHamiltonianEvolution
                        control
                        plan.ShiftedHamiltonian
                        { plan.Trotter with
                            NumSteps = plan.Trotter.NumSteps * repetitions
                            Time = plan.Time * float repetitions
                        }
                        system
                        circ)
                start

        evolved |> CircuitBuilder.addGates (Algorithms.QPE.inverseQftGates counting)

    /// Counting-qubit indices of `circuit`'s layout for a system of `systemQubits` qubits.
    let countingQubitsOf (systemQubits: int) (countingQubits: int) : int[] =
        [| systemQubits .. systemQubits + countingQubits - 1 |]

    /// Probability of each outcome k (φ = k/2^m) of the counting register in a state the
    /// backend returned for `circuit`.
    let outcomeProbabilities (plan: EvolutionPlan) (state: QuantumState) : Result<float[], QuantumError> =
        let counting =
            countingQubitsOf plan.ShiftedHamiltonian.NumQubits plan.CountingQubits

        FermionMapping.ChemistryVQE.outcomeDistribution state
        |> Result.map (fun distribution ->
            let probabilities = Array.zeroCreate (1 <<< plan.CountingQubits)

            for index, p in distribution do
                let k = Algorithms.QPE.countingOutcome counting index
                probabilities.[k] <- probabilities.[k] + p

            let total = Array.sum probabilities

            if total > 0.0 then
                probabilities |> Array.map (fun p -> p / total)
            else
                probabilities)

    /// Phase of an eigenvalue whose peak is at outcome k, refined with the more probable
    /// neighbour: for a single eigenphase φ = (k + δ)/N the bins hold
    /// P(k+d) = sin²(πδ)/(N² sin²(π(δ-d)/N)), so the ratio R of the neighbour to the peak gives
    /// tan(πδ/N) = √R sin(π/N) / (1 + √R cos(π/N)).
    let refinedPhase (probabilities: float[]) (k: int) : float =
        let bins = probabilities.Length
        let a = Math.PI / float bins
        let peak = probabilities.[k]
        let above = probabilities.[(k + 1) % bins]
        let below = probabilities.[(k - 1 + bins) % bins]

        let offset neighbour =
            if peak <= 0.0 then
                0.0
            else
                let root = sqrt (neighbour / peak)
                atan2 (root * sin a) (1.0 + root * cos a) / a

        let phase =
            if above >= below then
                (float k + offset above) / float bins
            else
                (float k - offset below) / float bins

        phase - floor phase

    /// Local maxima of the outcome distribution with at least `minimum` probability in their
    /// five bins (the maximum and two either side), strongest first; a maximum within two bins
    /// of a stronger one belongs to that peak. Returns (bin, probability of the five bins).
    let peaks (minimum: float) (probabilities: float[]) : (int * float) list =
        let bins = probabilities.Length

        let at k =
            probabilities.[((k % bins) + bins) % bins]

        let window k =
            [ -2 .. 2 ] |> List.sumBy (fun d -> at (k + d))

        let distance a b =
            min ((a - b + bins) % bins) ((b - a + bins) % bins)

        [ 0 .. bins - 1 ]
        |> List.filter (fun k ->
            probabilities.[k] > 0.0
            && probabilities.[k] >= at (k - 1)
            && probabilities.[k] >= at (k + 1))
        |> List.map (fun k -> k, window k)
        |> List.sortByDescending (fun (k, w) -> w, probabilities.[k])
        |> List.fold
            (fun (kept: (int * float) list) (k, w) ->
                if kept |> List.exists (fun (j, _) -> distance j k <= 2) then
                    kept
                else
                    kept @ [ (k, w) ])
            []
        |> List.filter (fun (_, w) -> w >= minimum)

    /// QPE of the molecule's electronic Hamiltonian (see the module summary): the energy of
    /// the most probable peak, every peak in Estimation, and the overlap caveat in Notes.
    /// Errors: no integrals (a molecule other than H/He without SolverConfig.IntegralProvider),
    /// spin states UCCSD-VQE also refuses, more than MaxTotalQubits qubits, fewer than 3
    /// counting qubits, a Trotter order other than 1 or 2, or fewer than one Trotter step.
    let runWith
        (settings: Settings)
        (molecule: Molecule)
        (config: SolverConfig)
        : Async<Result<VQE.VQEResult, QuantumError>> =
        async {
            let backend =
                config.Backend
                |> Option.defaultValue (Backends.LocalBackend.LocalBackend() :> Core.BackendAbstraction.IQuantumBackend)

            let invalid field (message: string) =
                Error(QuantumError.ValidationError(field, message))

            return
                result {
                    let! resolved = VQE.resolveIntegrals settings.Basis molecule config

                    let! integrals =
                        match resolved with
                        | Some(integrals, _) -> Ok integrals
                        | None ->
                            invalid
                                "IntegralProvider"
                                $"QPE for '{molecule.Name}' needs molecular integrals: supply SolverConfig.IntegralProvider (a PySCF/Psi4 wrapper, or FciDumpIntegrals.fromFile); the library computes integrals itself only for molecules of H and He atoms."

                    let systemQubits = 2 * integrals.NumOrbitals

                    let countingQubits =
                        settings.CountingQubits
                        |> Option.defaultValue (min 8 (MaxTotalQubits - systemQubits))

                    do!
                        if settings.TrotterOrder <> 1 && settings.TrotterOrder <> 2 then
                            invalid "TrotterOrder" $"must be 1 or 2, got {settings.TrotterOrder}"
                        elif settings.TrotterSteps < 1 then
                            invalid "TrotterSteps" $"must be at least 1, got {settings.TrotterSteps}"
                        elif countingQubits < 3 then
                            invalid
                                "CountingQubits"
                                $"{systemQubits} system qubits leave {MaxTotalQubits - systemQubits} of the {MaxTotalQubits} for counting; QPE needs at least 3. Choose a smaller active space (IntegralProvider or FCIDUMP)."
                        elif systemQubits + countingQubits > MaxTotalQubits then
                            invalid
                                "CountingQubits"
                                $"{systemQubits} system + {countingQubits} counting qubits exceed {MaxTotalQubits}"
                        else
                            match Core.BackendAbstraction.UnifiedBackend.getRunnableQubits backend with
                            | Some limit when systemQubits + countingQubits > limit ->
                                invalid
                                    "MoleculeSize"
                                    $"QPE needs {systemQubits + countingQubits} qubits; backend '{backend.Name}' runs at most {limit}"
                            | _ -> Ok()

                    let! (hamiltonian, nuclearRepulsion) =
                        MolecularHamiltonian.buildFromIntegrals integrals MolecularHamiltonian.JordanWigner

                    let plan =
                        evolutionPlan
                            (toPauliHamiltonian hamiltonian)
                            countingQubits
                            settings.TrotterOrder
                            settings.TrotterSteps

                    let! preparation =
                        match settings.InitialState with
                        | HartreeFockState -> Ok [ for q in 0 .. integrals.NumElectrons - 1 -> X q ]
                        | UccsdState amplitudes ->
                            FermionMapping.ChemistryVQE.uccsdCircuit integrals.NumElectrons systemQubits amplitudes
                            |> Result.map CircuitBuilder.getGates

                    let qpeCircuit = circuit plan preparation

                    let! state =
                        Core.BackendAbstraction.UnifiedBackend.submitAsCircuit
                            backend
                            qpeCircuit.QubitCount
                            (CircuitBuilder.getGates qpeCircuit
                             |> List.map Core.BackendAbstraction.QuantumOperation.Gate)

                    let! probabilities = outcomeProbabilities plan state
                    let bins = probabilities.Length

                    let energyOf phase =
                        phaseToEnergy plan phase + nuclearRepulsion

                    let found =
                        peaks 0.01 probabilities
                        |> List.map (fun (k, weight) ->
                            ({
                                Energy = energyOf (refinedPhase probabilities k)
                                Probability = weight
                            }
                            : PhaseEstimationPeak))

                    let top = probabilities |> Array.indexed |> Array.maxBy snd |> fst

                    let! strongest =
                        match found with
                        | head :: _ -> Ok head
                        | [] ->
                            Error(
                                QuantumError.OperationError(
                                    "QPE",
                                    "the outcome distribution has no peak with 1% probability"
                                )
                            )

                    let details: PhaseEstimationDetails =
                        {
                            CountingQubits = countingQubits
                            EvolutionTime = plan.Time
                            EnergyShift = plan.Shift
                            TrotterOrder = settings.TrotterOrder
                            TrotterStepsPerEvolution = settings.TrotterSteps
                            BinWidth = 2.0 * Math.PI / (plan.Time * float bins)
                            PeakBinEnergy = energyOf (float top / float bins)
                            Peaks = found
                            ShotsPerCircuit =
                                match backend with
                                | :? Core.BackendAbstraction.IShotSamplingBackend as sampling when sampling.Shots > 0 ->
                                    Some sampling.Shots
                                | _ -> None
                        }

                    let others = found |> List.tail

                    let lower = others |> List.filter (fun p -> p.Energy < strongest.Energy)

                    let notes =
                        [
                            yield
                                $"QPE returns eigenvalue E with probability |<psi|E>|^2 for the prepared state psi: Energy is the eigenvalue of the most probable peak (probability {strongest.Probability:F3}), the ground state only if psi overlaps it most."
                            if not others.IsEmpty then
                                yield
                                    "Other peaks: "
                                    + (others
                                       |> List.map (fun p -> $"E = {p.Energy:F6} Ha (probability {p.Probability:F3})")
                                       |> String.concat "; ")
                            if not lower.IsEmpty then
                                let lowest = lower |> List.minBy (fun p -> p.Energy)

                                yield
                                    $"A lower eigenvalue, {lowest.Energy:F6} Ha, has probability {lowest.Probability:F3}: the reported Energy is not the lowest eigenvalue found."
                        ]

                    let outcome: VQE.VQEResult =
                        {
                            Energy = strongest.Energy
                            OptimalParameters =
                                match settings.InitialState with
                                | UccsdState amplitudes -> Array.copy amplitudes
                                | HartreeFockState -> [||]
                            Iterations = 0
                            Converged = true
                            EnergyHistory = [ (0, strongest.Energy) ]
                            Source = QpeTrotterEvolution
                            ErrorMitigationApplied = false
                            Estimation = PhaseEstimation details
                            Notes = notes
                        }

                    return outcome
                }
        }

    /// QPE with defaultSettings (Hartree-Fock state, STO-3G for H/He molecules).
    let run (molecule: Molecule) (config: SolverConfig) : Async<Result<VQE.VQEResult, QuantumError>> =
        runWith defaultSettings molecule config

/// Ground state energy estimation
module GroundStateEnergy =

    let estimateEnergyWith
        (method: GroundStateMethod)
        (molecule: Molecule)
        (config: SolverConfig)
        : Async<Result<VQE.VQEResult, QuantumError>> =

        match method with
        | GroundStateMethod.VQE -> VQE.run molecule config

        | GroundStateMethod.QPE -> QPE.run molecule config

        | GroundStateMethod.ClassicalDFT ->
            async {
                let! energyResult = ClassicalDFT.run molecule config

                return
                    energyResult
                    |> Result.map (fun energy ->
                        {
                            VQE.Energy = energy
                            VQE.OptimalParameters = [||]
                            VQE.Iterations = 0
                            VQE.Converged = true
                            VQE.EnergyHistory = [ (0, energy) ]
                            VQE.Source = TabulatedReference
                            VQE.ErrorMitigationApplied = false
                            VQE.Estimation = NotEstimated
                            VQE.Notes = []
                        })
            }

        // Quantum-first: Automatic never substitutes the tabulated classical reference;
        // VQE.run returns Error when the molecule cannot run.
        | GroundStateMethod.Automatic -> VQE.run molecule config

    let estimateEnergy (molecule: Molecule) (config: SolverConfig) : Async<Result<VQE.VQEResult, QuantumError>> =

        estimateEnergyWith config.Method molecule config

// ============================================================================
// QUANTUM CHEMISTRY DOMAIN BUILDER - F# Computation Expression API (TKT-79)
// ============================================================================

/// <summary>
/// Quantum Chemistry Domain Builder - F# Computation Expression API
///
/// Provides idiomatic F# builders for quantum chemistry ground state calculations
/// with domain-specific abstractions for molecules, ansätze, and basis sets.
/// </summary>
/// <remarks>
/// <para>Uses underlying VQE Framework (TKT-95) for quantum execution.</para>
///
/// <para><b>Available Operations:</b></para>
/// <list type="bullet">
/// <item><c>molecule (h2 0.74)</c> - Set molecule for calculation</item>
/// <item><c>basis "sto-3g"</c> - Set basis set</item>
/// <item><c>ansatz UCCSD</c> - Set ansatz type</item>
/// <item><c>optimizer "COBYLA"</c> - Set optimizer</item>
/// <item><c>maxIterations 100</c> - Set iteration limit</item>
/// </list>
///
/// <para><b>Example Usage:</b></para>
/// <code>
/// open FSharp.Azure.Quantum.QuantumChemistry.QuantumChemistryBuilder
///
/// let problem = quantumChemistry {
///     molecule (h2 0.74)
///     basis "sto-3g"
///     ansatz UCCSD
/// }
///
/// let! result = solve problem
/// printfn "Energy: %.6f Ha" result.GroundStateEnergy
/// </code>
/// </remarks>
module QuantumChemistryBuilder =

    // ========================================================================
    // PRE-BUILT MOLECULES - Convenience Functions
    // ========================================================================

    /// <summary>H2 molecule at specified bond length.</summary>
    /// <param name="distance">Bond length in Angstroms</param>
    /// <returns>H2 molecule</returns>
    let h2 (distance: float) : Molecule =
        {
            Name = "H2"
            Atoms =
                [
                    {
                        Element = "H"
                        Position = (0.0, 0.0, 0.0)
                    }
                    {
                        Element = "H"
                        Position = (distance, 0.0, 0.0)
                    }
                ]
            Bonds =
                [
                    {
                        Atom1 = 0
                        Atom2 = 1
                        BondOrder = 1.0
                    }
                ]
            Charge = 0
            Multiplicity = 1
        }

    /// <summary>H2O molecule (water) with specified geometry.</summary>
    /// <param name="bondLength">O-H bond length in Angstroms</param>
    /// <param name="angle">H-O-H angle in degrees</param>
    /// <returns>H2O molecule</returns>
    let h2o (bondLength: float) (angle: float) : Molecule =
        let angleRad = angle * Math.PI / 180.0
        let halfAngle = angleRad / 2.0

        {
            Name = "H2O"
            Atoms =
                [
                    {
                        Element = "O"
                        Position = (0.0, 0.0, 0.0)
                    }
                    {
                        Element = "H"
                        Position = (0.0, bondLength * sin halfAngle, bondLength * cos halfAngle)
                    }
                    {
                        Element = "H"
                        Position = (0.0, -bondLength * sin halfAngle, bondLength * cos halfAngle)
                    }
                ]
            Bonds =
                [
                    {
                        Atom1 = 0
                        Atom2 = 1
                        BondOrder = 1.0
                    }
                    {
                        Atom1 = 0
                        Atom2 = 2
                        BondOrder = 1.0
                    }
                ]
            Charge = 0
            Multiplicity = 1
        }

    /// <summary>LiH molecule (lithium hydride) at specified bond length.</summary>
    /// <param name="distance">Bond length in Angstroms</param>
    /// <returns>LiH molecule</returns>
    let lih (distance: float) : Molecule =
        {
            Name = "LiH"
            Atoms =
                [
                    {
                        Element = "Li"
                        Position = (0.0, 0.0, 0.0)
                    }
                    {
                        Element = "H"
                        Position = (distance, 0.0, 0.0)
                    }
                ]
            Bonds =
                [
                    {
                        Atom1 = 0
                        Atom2 = 1
                        BondOrder = 1.0
                    }
                ]
            Charge = 0
            Multiplicity = 1
        }

    // ========================================================================
    // DOMAIN TYPES - Chemistry Builder State
    // ========================================================================

    /// <summary>Chemistry-specific ansatz types.</summary>
    /// <remarks>
    /// Different ansätze offer trade-offs between accuracy and computational cost.
    /// </remarks>
    [<Struct>]
    type ChemistryAnsatz =
        /// Unitary Coupled Cluster Singles Doubles (most accurate, most expensive)
        | UCCSD
        /// Hardware-Efficient Ansatz (faster, less accurate)
        | HEA
        /// Adaptive ansatz (dynamic construction based on gradients)
        | ADAPT

    /// <summary>Optimizer configuration for VQE.</summary>
    type OptimizerConfig =
        {
            /// Optimizer method name (e.g., "COBYLA", "SLSQP", "Powell")
            Method: string
            /// Maximum number of iterations
            MaxIterations: int
            /// Convergence tolerance
            Tolerance: float
            /// Initial parameter guess (for warm start)
            InitialGuess: float[] option
        }

    /// <summary>Source specification for loading molecules.</summary>
    /// <remarks>
    /// Allows deferred loading of molecules from various sources.
    /// Actual I/O happens in solve() for proper error handling.
    /// </remarks>
    type MoleculeSource =
        /// Direct molecule instance (already loaded)
        | Direct of Molecule
        /// Load from XYZ file path
        | XyzFile of string
        /// Load from FCIDump file path
        | FciDumpFile of string
        /// Load from dataset provider by name
        | FromProvider of provider: ChemistryDataProviders.IMoleculeDatasetProvider * name: string
        /// Load from default provider by name
        | FromDefaultProvider of string

    /// <summary>Quantum chemistry problem specification (builder state).</summary>
    type ChemistryProblem =
        {
            /// Molecule to calculate (legacy, for backward compatibility)
            Molecule: Molecule option
            /// Molecule source for deferred loading (new)
            MoleculeSource: MoleculeSource option
            /// Basis set (e.g., "sto-3g", "6-31g")
            Basis: string option
            /// Ansatz type
            Ansatz: ChemistryAnsatz option
            /// Optimizer configuration
            Optimizer: OptimizerConfig option
            /// Maximum VQE iterations
            MaxIterations: int
            /// Initial VQE parameters (warm start)
            InitialParameters: float[] option
            /// Molecular integrals for VQE (None: molecule_from_fcidump's file, else VQE.run's own selection)
            IntegralProvider: IntegralProvider option
            /// Ground-state method: GroundStateMethod.QPE runs QPE.runWith; anything else (or
            /// None) runs UCCSD-VQE
            Method: GroundStateMethod option
        }

    /// <summary>Chemistry-specific calculation result.</summary>
    type ChemistryResult =
        {
            /// Ground state energy in Hartrees
            GroundStateEnergy: float
            /// Optimal VQE parameters found
            OptimalParameters: float[]
            /// Number of VQE iterations performed
            Iterations: int
            /// Whether VQE converged within tolerance
            Convergence: bool
            /// Bond lengths between atoms (e.g., "H-H" -> 0.74 Å)
            BondLengths: Map<string, float>
            /// Dipole moment (if computed)
            DipoleMoment: float option
            /// What produced GroundStateEnergy
            Source: EnergySource
            /// How GroundStateEnergy was estimated (for QPE: evolution, Trotter, counting
            /// qubits and every peak of the outcome distribution)
            Estimation: EnergyEstimation
            /// Caveats on GroundStateEnergy (for QPE: the overlap of the prepared state)
            Notes: string list
        }

    // ========================================================================
    // F# COMPUTATION EXPRESSION BUILDER
    // ========================================================================

    /// <summary>
    /// Computation expression builder for quantum chemistry problems.
    /// Enables F#-idiomatic problem specification with control flow and composition.
    /// </summary>
    type QuantumChemistryBuilder() =

        // ====================================================================
        // CORE BUILDER METHODS - Lazy Composition
        // ====================================================================

        /// <summary>Initial empty state.</summary>
        member _.Yield(_) : ChemistryProblem =
            {
                Molecule = None
                MoleculeSource = None
                Basis = None
                Ansatz = None
                Optimizer = None
                MaxIterations = 100
                InitialParameters = None
                IntegralProvider = None
                Method = None
            }

        /// <summary>
        /// Final validation and transformation.
        /// Called automatically by F# compiler - no explicit .Build() needed!
        /// </summary>
        member _.Run(f: unit -> ChemistryProblem) : ChemistryProblem =
            let problem = f () // Execute delayed computation

            // Validate required fields - check both Molecule and MoleculeSource
            if problem.Molecule.IsNone && problem.MoleculeSource.IsNone then
                failwith
                    "Quantum chemistry validation: 'molecule' is required. Example: molecule (h2 0.74) or molecule_from_xyz \"file.xyz\""

            if problem.Basis.IsNone then
                failwith "Quantum chemistry validation: 'basis' is required. Example: basis \"sto-3g\""

            if problem.Ansatz.IsNone && problem.Method <> Some GroundStateMethod.QPE then
                failwith "Quantum chemistry validation: 'ansatz' is required. Example: ansatz UCCSD"

            // Apply defaults
            let withDefaults =
                { problem with
                    Optimizer =
                        problem.Optimizer
                        |> Option.orElse (
                            Some
                                {
                                    Method = "COBYLA"
                                    MaxIterations = problem.MaxIterations
                                    Tolerance = 1e-6
                                    InitialGuess = None
                                }
                        )
                }

            withDefaults

        /// <summary>Lazy evaluation wrapper.</summary>
        member _.Delay(f: unit -> ChemistryProblem) : unit -> ChemistryProblem = f

        /// <summary>Combine multiple operations sequentially.</summary>
        member _.Combine(first: ChemistryProblem, second: unit -> ChemistryProblem) : ChemistryProblem =
            let config1 = first
            let config2 = second ()

            // Merge configurations (second overrides first)
            {
                Molecule = config2.Molecule |> Option.orElse config1.Molecule
                MoleculeSource = config2.MoleculeSource |> Option.orElse config1.MoleculeSource
                Basis = config2.Basis |> Option.orElse config1.Basis
                Ansatz = config2.Ansatz |> Option.orElse config1.Ansatz
                Optimizer = config2.Optimizer |> Option.orElse config1.Optimizer
                MaxIterations =
                    if config2.MaxIterations <> 100 then
                        config2.MaxIterations
                    else
                        config1.MaxIterations
                InitialParameters = config2.InitialParameters |> Option.orElse config1.InitialParameters
                IntegralProvider = config2.IntegralProvider |> Option.orElse config1.IntegralProvider
                Method = config2.Method |> Option.orElse config1.Method
            }

        /// <summary>Empty/no-op value for conditional branches.</summary>
        member this.Zero() : ChemistryProblem = this.Yield()

        /// <summary>For loop support - iterate over sequences.</summary>
        member this.For(sequence: seq<'T>, body: 'T -> ChemistryProblem) : ChemistryProblem =
            sequence
            |> Seq.fold (fun state item -> this.Combine(state, fun () -> body item)) (this.Zero())

        /// <summary>Async support - let! binding for loading data.</summary>
        member _.Bind(computation: Async<'T>, continuation: 'T -> ChemistryProblem) : Async<ChemistryProblem> =
            async {
                let! value = computation
                return continuation value
            }

        // ====================================================================
        // CUSTOM OPERATIONS - Domain-Specific API
        // ====================================================================

        /// <summary>Set molecule for calculation.</summary>
        /// <param name="mol">Molecule instance</param>
        [<CustomOperation("molecule")>]
        member _.Molecule(problem: ChemistryProblem, mol: Molecule) : ChemistryProblem =
            { problem with Molecule = Some mol }

        /// <summary>Set basis set.</summary>
        /// <param name="basisSet">Basis set name (e.g., "sto-3g", "6-31g")</param>
        [<CustomOperation("basis")>]
        member _.Basis(problem: ChemistryProblem, basisSet: string) : ChemistryProblem =
            { problem with Basis = Some basisSet }

        /// <summary>Set ansatz type.</summary>
        /// <param name="ansatzType">Ansatz type (UCCSD, HEA, ADAPT)</param>
        [<CustomOperation("ansatz")>]
        member _.Ansatz(problem: ChemistryProblem, ansatzType: ChemistryAnsatz) : ChemistryProblem =
            { problem with
                Ansatz = Some ansatzType
            }

        /// <summary>Set optimizer.</summary>
        /// <param name="optimizerName">Optimizer method name</param>
        [<CustomOperation("optimizer")>]
        member _.Optimizer(problem: ChemistryProblem, optimizerName: string) : ChemistryProblem =
            let config =
                {
                    Method = optimizerName
                    MaxIterations = problem.MaxIterations
                    Tolerance = 1e-6
                    InitialGuess = problem.InitialParameters
                }

            { problem with Optimizer = Some config }

        /// <summary>Set maximum iterations.</summary>
        /// <param name="maxIter">Maximum iterations</param>
        [<CustomOperation("maxIterations")>]
        member _.MaxIterations(problem: ChemistryProblem, maxIter: int) : ChemistryProblem =
            { problem with MaxIterations = maxIter }

        /// <summary>Set initial parameters for warm start.</summary>
        /// <param name="params">Initial parameter values</param>
        [<CustomOperation("initialParameters")>]
        member _.InitialParameters(problem: ChemistryProblem, params': float[]) : ChemistryProblem =
            { problem with
                InitialParameters = Some params'
            }

        /// <summary>Supply molecular integrals for VQE (PySCF/Psi4 wrapper, FciDumpIntegrals, Sto3gIntegrals).</summary>
        /// <param name="provider">Integral provider called with the loaded molecule</param>
        [<CustomOperation("integralProvider")>]
        member _.IntegralProvider(problem: ChemistryProblem, provider: IntegralProvider) : ChemistryProblem =
            { problem with
                IntegralProvider = Some provider
            }

        /// <summary>Choose the ground-state method: GroundStateMethod.QPE runs quantum phase
        /// estimation of the Trotterised e^(-iHt) (QPE.runWith defaultSettings in the problem's
        /// basis; no ansatz needed); VQE, Automatic or no method runs UCCSD-VQE.</summary>
        /// <param name="groundStateMethod">The method</param>
        [<CustomOperation("groundStateMethod")>]
        member _.GroundStateMethod(problem: ChemistryProblem, groundStateMethod: GroundStateMethod) : ChemistryProblem =
            { problem with
                Method = Some groundStateMethod
            }

        // ====================================================================
        // FILE LOADING CUSTOM OPERATIONS - Deferred I/O
        // ====================================================================

        /// <summary>Load molecule from XYZ file.</summary>
        /// <param name="filePath">Path to XYZ file</param>
        /// <remarks>
        /// The file is loaded when solve() is called, not during builder construction.
        /// This allows proper error handling in the Result type.
        /// </remarks>
        /// <example>
        /// <code>
        /// let problem = quantumChemistry {
        ///     molecule_from_xyz "caffeine.xyz"
        ///     basis "sto-3g"
        ///     ansatz UCCSD
        /// }
        /// </code>
        /// </example>
        [<CustomOperation("molecule_from_xyz")>]
        member _.MoleculeFromXyz(problem: ChemistryProblem, filePath: string) : ChemistryProblem =
            { problem with
                MoleculeSource = Some(XyzFile filePath)
            }

        /// <summary>Load molecule from FCIDump file.</summary>
        /// <param name="filePath">Path to FCIDump file</param>
        /// <remarks>
        /// FCIDump files contain molecular integrals but typically not geometry.
        /// The resulting molecule has placeholder atoms; solve runs VQE on the file's
        /// integrals (FciDumpIntegrals.fromFile) unless integralProvider is set.
        /// </remarks>
        [<CustomOperation("molecule_from_fcidump")>]
        member _.MoleculeFromFciDump(problem: ChemistryProblem, filePath: string) : ChemistryProblem =
            { problem with
                MoleculeSource = Some(FciDumpFile filePath)
            }

        /// <summary>Load molecule from a dataset provider by name.</summary>
        /// <param name="provider">Dataset provider instance</param>
        /// <param name="name">Molecule name to look up</param>
        /// <example>
        /// <code>
        /// let provider = SdfFileDatasetProvider("molecules.sdf")
        /// let problem = quantumChemistry {
        ///     molecule_from_provider provider "aspirin"
        ///     basis "6-31g"
        ///     ansatz HEA
        /// }
        /// </code>
        /// </example>
        [<CustomOperation("molecule_from_provider")>]
        member _.MoleculeFromProvider
            (problem: ChemistryProblem, provider: ChemistryDataProviders.IMoleculeDatasetProvider, name: string)
            : ChemistryProblem =
            { problem with
                MoleculeSource = Some(FromProvider(provider, name))
            }

        /// <summary>Load molecule by name from the default dataset provider.</summary>
        /// <param name="name">Molecule name (e.g., "benzene", "caffeine", "aspirin")</param>
        /// <remarks>
        /// Uses the built-in molecule library. Available molecules include common
        /// organic compounds and drug molecules.
        /// </remarks>
        /// <example>
        /// <code>
        /// let problem = quantumChemistry {
        ///     molecule_from_name "benzene"
        ///     basis "sto-3g"
        ///     ansatz UCCSD
        /// }
        /// </code>
        /// </example>
        [<CustomOperation("molecule_from_name")>]
        member _.MoleculeFromName(problem: ChemistryProblem, name: string) : ChemistryProblem =
            { problem with
                MoleculeSource = Some(FromDefaultProvider name)
            }

    /// <summary>Global instance of the quantum chemistry builder.</summary>
    let quantumChemistry = QuantumChemistryBuilder()

    // ========================================================================
    // SOLVER - Transform Domain Problem → VQE Execution
    // ========================================================================

    /// <summary>Compute bond lengths from molecule geometry.</summary>
    let private computeBondLengths (molecule: Molecule) : Map<string, float> =
        molecule.Atoms
        |> List.mapi (fun i atom1 ->
            molecule.Atoms
            |> List.skip (i + 1)
            |> List.map (fun atom2 ->
                let bondName = $"%s{atom1.Element}-%s{atom2.Element}"
                let bondLength = Molecule.calculateBondLength atom1 atom2
                bondName, bondLength))
        |> List.concat
        |> Map.ofList

    /// <summary>Compute dipole moment magnitude from molecule geometry.</summary>
    /// <remarks>
    /// Computes classical nuclear contribution to dipole moment.
    /// Formula: μ = |Σᵢ Zᵢ * rᵢ| where Zᵢ is nuclear charge, rᵢ is position.
    /// Returns magnitude in Debye (1 Debye ≈ 0.2082 e·Å).
    /// Note: This is a simplified calculation that only considers nuclear charges.
    /// A full quantum calculation would require the electronic density from VQE.
    /// </remarks>
    let private computeDipoleMoment (molecule: Molecule) : float option =
        // Compute center of charge (nuclear contribution)
        let (totalCharge, dipoleX, dipoleY, dipoleZ) =
            molecule.Atoms
            |> List.fold
                (fun (charge, dx, dy, dz) atom ->
                    match AtomicNumbers.fromSymbol atom.Element with
                    | Some atomicNumber ->
                        let (x, y, z) = atom.Position
                        let zFloat = float atomicNumber
                        (charge + zFloat, dx + zFloat * x, dy + zFloat * y, dz + zFloat * z)
                    | None -> (charge, dx, dy, dz))
                (0.0, 0.0, 0.0, 0.0)

        if totalCharge = 0.0 then
            None // Cannot compute dipole for neutral fragments without electronic density
        else
            // Compute dipole magnitude in atomic units (e·Å)
            let dipoleMagnitude =
                sqrt (dipoleX * dipoleX + dipoleY * dipoleY + dipoleZ * dipoleZ)

            // Convert to Debye (1 Debye = 0.2082 e·Å)
            let dipoleInDebye = dipoleMagnitude / 0.2082
            Some dipoleInDebye

    /// <summary>
    /// Load molecule from MoleculeSource.
    /// Internal helper for deferred loading in solve().
    /// </summary>
    let private loadMoleculeFromSource (source: MoleculeSource) : Async<Result<Molecule, QuantumError>> =
        async {
            match source with
            | Direct mol -> return Ok mol

            | XyzFile path ->
                let! result = Molecule.fromXyzFileAsync path
                return result

            | FciDumpFile path ->
                // An FCIDUMP has no geometry: the molecule is a named placeholder and VQE
                // takes the file's integrals (see solve).
                return
                    FciDumpIntegrals.readFile path
                    |> Result.map (fun integrals ->
                        {
                            Name = Path.GetFileNameWithoutExtension path
                            Atoms =
                                [
                                    {
                                        Element = "X"
                                        Position = (0.0, 0.0, 0.0)
                                    }
                                ]
                            Bonds = []
                            Charge = 0
                            Multiplicity = 1 + integrals.NumElectrons % 2
                        })

            | FromProvider(provider, name) -> return Molecule.fromProvider provider name

            | FromDefaultProvider name -> return Molecule.fromDefaultProvider name
        }

    /// <summary>
    /// Solve quantum chemistry problem using VQE framework.
    /// Transforms domain problem to VQE execution, runs calculation, and returns chemistry-specific result.
    /// </summary>
    /// <param name="problem">Chemistry problem specification</param>
    /// <returns>Async result with ground state energy and bond information</returns>
    let solve (problem: ChemistryProblem) : Async<Result<ChemistryResult, QuantumError>> =
        async {
            // Load molecule from source (deferred I/O)
            let! moleculeResult =
                match problem.Molecule with
                | Some mol -> async { return Ok mol }
                | None ->
                    match problem.MoleculeSource with
                    | Some source -> loadMoleculeFromSource source
                    | None -> async { return Error(QuantumError.ValidationError("Molecule", "No molecule specified")) }

            match moleculeResult with
            | Error err -> return Error err
            | Ok molecule ->
                let optimizer = problem.Optimizer.Value

                // Configure VQE solver using existing framework
                let vqeConfig =
                    {
                        Method = GroundStateMethod.VQE
                        MaxIterations = optimizer.MaxIterations
                        Tolerance = optimizer.Tolerance
                        InitialParameters = problem.InitialParameters
                        Backend = None // Use default LocalBackend
                        ProgressReporter = None
                        ErrorMitigation = None // No error mitigation by default
                        IntegralProvider =
                            problem.IntegralProvider
                            |> Option.orElse (
                                match problem.Molecule, problem.MoleculeSource with
                                | None, Some(FciDumpFile path) -> Some(FciDumpIntegrals.fromFile path)
                                | _ -> None
                            )
                    }

                // `basis` selects the library's own integrals for H/He molecules; provider and
                // FCIDUMP integrals carry their own basis.
                let! vqeResult =
                    match problem.Method with
                    | Some GroundStateMethod.QPE ->
                        QPE.runWith
                            { QPE.defaultSettings with
                                Basis = problem.Basis.Value
                            }
                            molecule
                            { vqeConfig with
                                Method = GroundStateMethod.QPE
                            }
                    | _ -> VQE.runInBasis problem.Basis.Value molecule vqeConfig

                // Transform result: Framework → Domain
                let result =
                    match vqeResult with
                    | Ok vqe ->
                        Ok
                            {
                                GroundStateEnergy = vqe.Energy
                                OptimalParameters = vqe.OptimalParameters
                                Iterations = vqe.Iterations
                                Convergence = vqe.Converged
                                BondLengths = computeBondLengths molecule
                                DipoleMoment = computeDipoleMoment molecule
                                Source = vqe.Source
                                Estimation = vqe.Estimation
                                Notes = vqe.Notes
                            }
                    | Error err -> Error err

                return result
        }
