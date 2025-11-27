namespace FSharp.Azure.Quantum.QuantumChemistry

open System

/// Quantum Chemistry - Molecule Representation and Ground State Energy Estimation
/// Implements VQE (Variational Quantum Eigensolver) for molecular ground state energies

// ============================================================================
// UNITS OF MEASURE
// ============================================================================

/// Angstroms (Å) - unit of atomic distance
[<Measure>] type angstrom

/// Hartree - atomic unit of energy
[<Measure>] type hartree

/// Electron volts
[<Measure>] type eV

// ============================================================================
// ATOMIC DATA
// ============================================================================

/// Atomic numbers for common elements
module AtomicNumbers =
    let H = 1   // Hydrogen
    let He = 2  // Helium
    let Li = 3  // Lithium
    let C = 6   // Carbon
    let N = 7   // Nitrogen
    let O = 8   // Oxygen
    let F = 9   // Fluorine
    let Na = 11 // Sodium
    let Mg = 12 // Magnesium
    let S = 16  // Sulfur
    let Cl = 17 // Chlorine
    
    /// Get atomic number from element symbol
    let fromSymbol (element: string) : int option =
        match element.ToUpperInvariant() with
        | "H" -> Some H
        | "HE" -> Some He
        | "LI" -> Some Li
        | "C" -> Some C
        | "N" -> Some N
        | "O" -> Some O
        | "F" -> Some F
        | "NA" -> Some Na
        | "MG" -> Some Mg
        | "S" -> Some S
        | "CL" -> Some Cl
        | _ -> None

// ============================================================================
// MOLECULE REPRESENTATION
// ============================================================================

/// Atom in 3D space
type Atom = {
    /// Element symbol (H, C, N, O, etc.)
    Element: string
    
    /// Position in 3D space (x, y, z) in Angstroms
    Position: float * float * float
}

/// Bond between two atoms
type Bond = {
    /// Index of first atom (0-based)
    Atom1: int
    
    /// Index of second atom (0-based)
    Atom2: int
    
    /// Bond order: 1.0 = single, 2.0 = double, 3.0 = triple
    BondOrder: float
}

/// Molecular structure
type Molecule = {
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
    let validate (molecule: Molecule) : Result<unit, string> =
        // Check all bonds reference valid atoms
        let invalidBonds =
            molecule.Bonds
            |> List.filter (fun bond ->
                bond.Atom1 < 0 || bond.Atom1 >= molecule.Atoms.Length ||
                bond.Atom2 < 0 || bond.Atom2 >= molecule.Atoms.Length)
        
        if not invalidBonds.IsEmpty then
            Error (sprintf "Bond references non-existent atom indices: %A" invalidBonds)
        else
            Ok ()
    
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
            |> List.sumBy (fun atom ->
                AtomicNumbers.fromSymbol atom.Element
                |> Option.defaultValue 0)
        
        // Subtract charge (positive charge = fewer electrons)
        nuclearElectrons - molecule.Charge
    
    /// Create H2 molecule at specified bond length
    let createH2 (bondLength: float) : Molecule =
        {
            Name = "H2"
            Atoms = [
                { Element = "H"; Position = (0.0, 0.0, 0.0) }
                { Element = "H"; Position = (0.0, 0.0, bondLength) }
            ]
            Bonds = [
                { Atom1 = 0; Atom2 = 1; BondOrder = 1.0 }
            ]
            Charge = 0
            Multiplicity = 1  // Singlet (all spins paired)
        }
    
    /// Create H2O molecule (water) at equilibrium geometry
    let createH2O () : Molecule =
        // Equilibrium geometry: O-H bond length = 0.957 Å, H-O-H angle = 104.5°
        let ohBondLength = 0.957
        let angleRad = 104.5 * Math.PI / 180.0
        let halfAngle = angleRad / 2.0
        
        {
            Name = "H2O"
            Atoms = [
                { Element = "O"; Position = (0.0, 0.0, 0.0) }
                { 
                    Element = "H"
                    Position = (0.0, ohBondLength * sin halfAngle, ohBondLength * cos halfAngle)
                }
                {
                    Element = "H"
                    Position = (0.0, -ohBondLength * sin halfAngle, ohBondLength * cos halfAngle)
                }
            ]
            Bonds = [
                { Atom1 = 0; Atom2 = 1; BondOrder = 1.0 }  // O-H
                { Atom1 = 0; Atom2 = 2; BondOrder = 1.0 }  // O-H
            ]
            Charge = 0
            Multiplicity = 1  // Singlet
        }
    
    /// Create LiH molecule at equilibrium bond length
    let createLiH () : Molecule =
        {
            Name = "LiH"
            Atoms = [
                { Element = "Li"; Position = (0.0, 0.0, 0.0) }
                { Element = "H"; Position = (0.0, 0.0, 1.596) }  // 1.596 Å equilibrium
            ]
            Bonds = [
                { Atom1 = 0; Atom2 = 1; BondOrder = 1.0 }
            ]
            Charge = 0
            Multiplicity = 1  // Singlet
        }
