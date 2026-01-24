namespace FSharp.Azure.Quantum.Data

/// Molecule Library - Pre-defined molecules loaded from embedded CSV data
/// 
/// This module provides a curated library of molecules for quantum chemistry calculations.
/// Geometries are from NIST CCCBDB, experimental data, and computational chemistry literature.
///
/// Categories:
/// - common: Basic molecules (H2, H2O, CO2, NH3, etc.)
/// - hydrocarbons: Organic molecules (methane, benzene, etc.)
/// - materials: Materials science molecules (metal hydrides, quantum dots, etc.)
///
/// Usage:
///   open FSharp.Azure.Quantum.Data
///   let water = MoleculeLibrary.get "H2O"
///   let benzene = MoleculeLibrary.get "benzene"
///   let aromatics = MoleculeLibrary.byCategory "aromatic"

open System

module MoleculeLibrary =
    
    // ========================================================================
    // LOCAL TYPE DEFINITIONS
    // ========================================================================
    // These mirror types in QuantumChemistry namespace but are defined here
    // to avoid circular dependencies (Data layer compiles before Solvers)
    
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
        Multiplicity: int
        /// Category (e.g., "diatomic", "aromatic", "metal_hydride")
        Category: string
        /// Reference source (e.g., "NIST CCCBDB")
        Reference: string
    }
    
    // ========================================================================
    // EMBEDDED CSV DATA
    // ========================================================================
    
    /// Common molecules CSV data
    let private commonCsvData = """Name,Formula,Charge,Multiplicity,Category,Reference,Atoms
H2,H2,0,1,diatomic,NIST CCCBDB,H:0.0:0.0:0.0;H:0.74:0.0:0.0
He2,He2,0,1,diatomic,theoretical,He:0.0:0.0:0.0;He:2.97:0.0:0.0
LiH,LiH,0,1,diatomic,NIST CCCBDB,Li:0.0:0.0:0.0;H:1.595:0.0:0.0
HF,HF,0,1,diatomic,NIST CCCBDB,H:0.0:0.0:0.0;F:0.917:0.0:0.0
HCl,HCl,0,1,diatomic,NIST CCCBDB,H:0.0:0.0:0.0;Cl:1.275:0.0:0.0
HBr,HBr,0,1,diatomic,NIST CCCBDB,H:0.0:0.0:0.0;Br:1.414:0.0:0.0
N2,N2,0,1,diatomic,NIST CCCBDB,N:0.0:0.0:0.0;N:1.098:0.0:0.0
O2,O2,0,3,diatomic,NIST CCCBDB,O:0.0:0.0:0.0;O:1.208:0.0:0.0
F2,F2,0,1,diatomic,NIST CCCBDB,F:0.0:0.0:0.0;F:1.412:0.0:0.0
Cl2,Cl2,0,1,diatomic,NIST CCCBDB,Cl:0.0:0.0:0.0;Cl:1.988:0.0:0.0
CO,CO,0,1,diatomic,NIST CCCBDB,C:0.0:0.0:0.0;O:1.128:0.0:0.0
NO,NO,0,2,diatomic,NIST CCCBDB,N:0.0:0.0:0.0;O:1.151:0.0:0.0
H2O,H2O,0,1,triatomic,NIST CCCBDB,O:0.0:0.0:0.0;H:0.958:0.0:0.0;H:-0.240:0.927:0.0
H2S,H2S,0,1,triatomic,NIST CCCBDB,S:0.0:0.0:0.0;H:1.336:0.0:0.0;H:-0.335:1.294:0.0
CO2,CO2,0,1,triatomic,NIST CCCBDB,C:0.0:0.0:0.0;O:-1.162:0.0:0.0;O:1.162:0.0:0.0
SO2,SO2,0,1,triatomic,NIST CCCBDB,S:0.0:0.0:0.0;O:-1.237:0.723:0.0;O:1.237:0.723:0.0
NO2,NO2,0,2,triatomic,NIST CCCBDB,N:0.0:0.0:0.0;O:-1.098:0.456:0.0;O:1.098:0.456:0.0
O3,O3,0,1,triatomic,NIST CCCBDB,O:0.0:0.0:0.0;O:-1.078:0.655:0.0;O:1.078:0.655:0.0
NH3,NH3,0,1,small,NIST CCCBDB,N:0.0:0.0:0.0;H:0.0:0.939:0.381;H:0.813:-0.470:0.381;H:-0.813:-0.470:0.381
PH3,PH3,0,1,small,NIST CCCBDB,P:0.0:0.0:0.0;H:0.0:1.193:0.770;H:1.033:-0.596:0.770;H:-1.033:-0.596:0.770"""

    /// Hydrocarbon molecules CSV data
    let private hydrocarbonsCsvData = """Name,Formula,Charge,Multiplicity,Category,Reference,Atoms
methane,CH4,0,1,alkane,NIST CCCBDB,C:0.0:0.0:0.0;H:1.089:0.0:0.0;H:-0.363:1.027:0.0;H:-0.363:-0.513:0.890;H:-0.363:-0.513:-0.890
ethane,C2H6,0,1,alkane,NIST CCCBDB,C:-0.762:0.0:0.0;C:0.762:0.0:0.0;H:-1.157:1.013:0.0;H:-1.157:-0.507:0.878;H:-1.157:-0.507:-0.878;H:1.157:-1.013:0.0;H:1.157:0.507:-0.878;H:1.157:0.507:0.878
propane,C3H8,0,1,alkane,NIST CCCBDB,C:0.0:0.0:0.0;C:-1.270:0.0:0.880;C:1.270:0.0:0.880;H:0.0:0.0:-1.090;H:0.0:-1.013:0.363;H:-1.270:0.0:1.970;H:-1.270:-1.013:0.517;H:-2.163:0.507:0.517;H:1.270:0.0:1.970;H:1.270:-1.013:0.517;H:2.163:0.507:0.517
ethylene,C2H4,0,1,alkene,NIST CCCBDB,C:-0.665:0.0:0.0;C:0.665:0.0:0.0;H:-1.234:0.927:0.0;H:-1.234:-0.927:0.0;H:1.234:0.927:0.0;H:1.234:-0.927:0.0
propene,C3H6,0,1,alkene,NIST CCCBDB,C:0.0:0.0:0.0;C:1.330:0.0:0.0;C:-0.765:1.270:0.0;H:-0.505:-0.960:0.0;H:1.860:-0.960:0.0;H:1.860:0.960:0.0;H:-1.852:1.160:0.0;H:-0.235:2.230:0.0;H:-0.765:1.270:1.090
acetylene,C2H2,0,1,alkyne,NIST CCCBDB,C:-0.602:0.0:0.0;C:0.602:0.0:0.0;H:-1.665:0.0:0.0;H:1.665:0.0:0.0
benzene,C6H6,0,1,aromatic,NIST CCCBDB,C:1.396:0.0:0.0;C:0.698:1.209:0.0;C:-0.698:1.209:0.0;C:-1.396:0.0:0.0;C:-0.698:-1.209:0.0;C:0.698:-1.209:0.0;H:2.481:0.0:0.0;H:1.240:2.148:0.0;H:-1.240:2.148:0.0;H:-2.481:0.0:0.0;H:-1.240:-2.148:0.0;H:1.240:-2.148:0.0
toluene,C7H8,0,1,aromatic,NIST CCCBDB,C:1.396:0.0:0.0;C:0.698:1.209:0.0;C:-0.698:1.209:0.0;C:-1.396:0.0:0.0;C:-0.698:-1.209:0.0;C:0.698:-1.209:0.0;C:2.896:0.0:0.0;H:1.240:2.148:0.0;H:-1.240:2.148:0.0;H:-2.481:0.0:0.0;H:-1.240:-2.148:0.0;H:1.240:-2.148:0.0;H:3.261:1.028:0.0;H:3.261:-0.514:0.890;H:3.261:-0.514:-0.890
naphthalene,C10H8,0,1,aromatic,NIST CCCBDB,C:0.0:0.712:0.0;C:0.0:-0.712:0.0;C:1.243:1.403:0.0;C:-1.243:1.403:0.0;C:1.243:-1.403:0.0;C:-1.243:-1.403:0.0;C:2.426:0.712:0.0;C:-2.426:0.712:0.0;C:2.426:-0.712:0.0;C:-2.426:-0.712:0.0;H:1.243:2.491:0.0;H:-1.243:2.491:0.0;H:1.243:-2.491:0.0;H:-1.243:-2.491:0.0;H:3.370:1.249:0.0;H:-3.370:1.249:0.0;H:3.370:-1.249:0.0;H:-3.370:-1.249:0.0
formaldehyde,CH2O,0,1,aldehyde,NIST CCCBDB,C:0.0:0.0:0.0;O:0.0:1.208:0.0;H:0.943:-0.587:0.0;H:-0.943:-0.587:0.0
acetaldehyde,C2H4O,0,1,aldehyde,NIST CCCBDB,C:0.0:0.0:0.0;C:1.500:0.0:0.0;O:-0.604:1.139:0.0;H:-0.520:-0.980:0.0;H:1.857:1.028:0.0;H:1.857:-0.514:0.890;H:1.857:-0.514:-0.890
methanol,CH4O,0,1,alcohol,NIST CCCBDB,C:0.0:0.0:0.0;O:1.427:0.0:0.0;H:-0.390:1.011:0.0;H:-0.390:-0.506:0.876;H:-0.390:-0.506:-0.876;H:1.783:0.898:0.0
ethanol,C2H6O,0,1,alcohol,NIST CCCBDB,C:0.0:0.0:0.0;C:1.512:0.0:0.0;O:-0.577:1.312:0.0;H:-0.363:-0.513:0.890;H:-0.363:-0.513:-0.890;H:1.876:1.013:0.0;H:1.876:-0.507:0.878;H:1.876:-0.507:-0.878;H:-1.534:1.312:0.0
formic_acid,CH2O2,0,1,carboxylic_acid,NIST CCCBDB,C:0.0:0.0:0.0;O:1.196:0.0:0.0;O:-0.655:1.139:0.0;H:-0.640:-0.940:0.0;H:0.218:1.736:0.0
acetic_acid,C2H4O2,0,1,carboxylic_acid,NIST CCCBDB,C:0.0:0.0:0.0;C:1.500:0.0:0.0;O:2.037:1.139:0.0;O:2.037:-1.139:0.0;H:-0.363:0.513:0.890;H:-0.363:0.513:-0.890;H:-0.363:-1.027:0.0;H:2.966:-1.139:0.0"""

    /// Materials science molecules CSV data
    let private materialsCsvData = """Name,Formula,Charge,Multiplicity,Category,Reference,Atoms
FeH,FeH,0,4,metal_hydride,Phillips 1987,Fe:0.0:0.0:0.0;H:1.63:0.0:0.0
CoH,CoH,0,3,metal_hydride,NIST CCCBDB,Co:0.0:0.0:0.0;H:1.54:0.0:0.0
NiH,NiH,0,2,metal_hydride,NIST CCCBDB,Ni:0.0:0.0:0.0;H:1.48:0.0:0.0
CuH,CuH,0,1,metal_hydride,NIST CCCBDB,Cu:0.0:0.0:0.0;H:1.46:0.0:0.0
Fe2,Fe2,0,7,metal_dimer,Moskovits 1984,Fe:0.0:0.0:0.0;Fe:2.02:0.0:0.0
Co2,Co2,0,5,metal_dimer,NIST CCCBDB,Co:0.0:0.0:0.0;Co:2.00:0.0:0.0
Ni2,Ni2,0,3,metal_dimer,NIST CCCBDB,Ni:0.0:0.0:0.0;Ni:2.15:0.0:0.0
Cu2,Cu2,0,1,metal_dimer,NIST CCCBDB,Cu:0.0:0.0:0.0;Cu:2.22:0.0:0.0
SiH4,SiH4,0,1,semiconductor,NIST CCCBDB,Si:0.0:0.0:0.0;H:1.480:0.0:0.0;H:-0.493:1.395:0.0;H:-0.493:-0.698:1.209;H:-0.493:-0.698:-1.209
GeH4,GeH4,0,1,semiconductor,NIST CCCBDB,Ge:0.0:0.0:0.0;H:1.527:0.0:0.0;H:-0.509:1.440:0.0;H:-0.509:-0.720:1.247;H:-0.509:-0.720:-1.247
AsH3,AsH3,0,1,semiconductor,NIST CCCBDB,As:0.0:0.0:0.0;H:0.0:1.274:0.737;H:1.103:-0.637:0.737;H:-1.103:-0.637:0.737
CdSe,CdSe,0,1,quantum_dot,Peng 2000,Cd:0.0:0.0:0.0;Se:2.62:0.0:0.0
CdTe,CdTe,0,1,quantum_dot,NIST CCCBDB,Cd:0.0:0.0:0.0;Te:2.76:0.0:0.0
ZnS,ZnS,0,1,quantum_dot,NIST CCCBDB,Zn:0.0:0.0:0.0;S:2.05:0.0:0.0
ZnSe,ZnSe,0,1,quantum_dot,NIST CCCBDB,Zn:0.0:0.0:0.0;Se:2.25:0.0:0.0
ZnO,ZnO,0,1,quantum_dot,NIST CCCBDB,Zn:0.0:0.0:0.0;O:1.72:0.0:0.0
PbS,PbS,0,1,quantum_dot,NIST CCCBDB,Pb:0.0:0.0:0.0;S:2.29:0.0:0.0
PbSe,PbSe,0,1,quantum_dot,NIST CCCBDB,Pb:0.0:0.0:0.0;Se:2.40:0.0:0.0
FeO,FeO,0,5,metal_oxide,NIST CCCBDB,Fe:0.0:0.0:0.0;O:1.62:0.0:0.0
CoO,CoO,0,4,metal_oxide,NIST CCCBDB,Co:0.0:0.0:0.0;O:1.63:0.0:0.0
NiO,NiO,0,3,metal_oxide,NIST CCCBDB,Ni:0.0:0.0:0.0;O:1.63:0.0:0.0
CuO,CuO,0,2,metal_oxide,NIST CCCBDB,Cu:0.0:0.0:0.0;O:1.72:0.0:0.0
TiO2,TiO2,0,1,metal_oxide,NIST CCCBDB,Ti:0.0:0.0:0.0;O:-1.62:0.55:0.0;O:1.62:0.55:0.0
Pt2,Pt2,0,3,catalyst,NIST CCCBDB,Pt:0.0:0.0:0.0;Pt:2.33:0.0:0.0
Pd2,Pd2,0,3,catalyst,NIST CCCBDB,Pd:0.0:0.0:0.0;Pd:2.48:0.0:0.0
Au2,Au2,0,1,catalyst,NIST CCCBDB,Au:0.0:0.0:0.0;Au:2.47:0.0:0.0
Ag2,Ag2,0,1,catalyst,NIST CCCBDB,Ag:0.0:0.0:0.0;Ag:2.53:0.0:0.0"""

    // ========================================================================
    // PARSING FUNCTIONS
    // ========================================================================
    
    /// Parse a single atom from "Element:X:Y:Z" format
    let private parseAtom (atomStr: string) : Atom option =
        let parts = atomStr.Split(':')
        if parts.Length >= 4 then
            try
                Some {
                    Element = parts.[0].Trim()
                    Position = (
                        Double.Parse(parts.[1].Trim()),
                        Double.Parse(parts.[2].Trim()),
                        Double.Parse(parts.[3].Trim())
                    )
                }
            with _ -> None
        else None
    
    /// Parse atoms string "H:0:0:0;H:0.74:0:0" into Atom list
    let private parseAtoms (atomsStr: string) : Atom list =
        atomsStr.Split(';')
        |> Array.choose parseAtom
        |> Array.toList
    
    /// Infer bonds from atomic distances using covalent radii
    /// Uses PeriodicTable.estimateBondLength for accurate thresholds
    let private inferBonds (atoms: Atom list) : Bond list =
        let atomArray = atoms |> Array.ofList
        let n = atomArray.Length
        
        [
            for i in 0 .. n - 2 do
                for j in i + 1 .. n - 1 do
                    let atom1 = atomArray.[i]
                    let atom2 = atomArray.[j]
                    
                    // Calculate actual distance
                    let (x1, y1, z1) = atom1.Position
                    let (x2, y2, z2) = atom2.Position
                    let dx = x2 - x1
                    let dy = y2 - y1
                    let dz = z2 - z1
                    let distance = sqrt (dx * dx + dy * dy + dz * dz)
                    
                    // Get expected bond length from periodic table
                    match PeriodicTable.estimateBondLength atom1.Element atom2.Element with
                    | Some expectedLength ->
                        // Allow 20% tolerance for bond detection
                        let tolerance = 0.20
                        let maxBondLength = expectedLength * (1.0 + tolerance)
                        
                        if distance <= maxBondLength then
                            // Estimate bond order based on how short the bond is
                            // Shorter than expected = higher bond order
                            let bondRatio = distance / expectedLength
                            let bondOrder =
                                if bondRatio < 0.80 then 3.0      // Triple bond
                                elif bondRatio < 0.90 then 2.0    // Double bond
                                else 1.0                           // Single bond
                            
                            yield { Atom1 = i; Atom2 = j; BondOrder = bondOrder }
                    | None ->
                        // Fallback: use simple distance threshold for elements without covalent radius
                        // Most covalent bonds are < 3.0 A
                        if distance < 3.0 then
                            yield { Atom1 = i; Atom2 = j; BondOrder = 1.0 }
        ]
    
    /// Parse a CSV line into a Molecule
    let private parseMoleculeLine (line: string) : Molecule option =
        // Skip comment lines and empty lines
        if String.IsNullOrWhiteSpace(line) || line.TrimStart().StartsWith("#") then
            None
        else
            let fields = line.Split(',')
            if fields.Length >= 7 then
                try
                    let name = fields.[0].Trim()
                    let charge = Int32.Parse(fields.[2].Trim())
                    let multiplicity = Int32.Parse(fields.[3].Trim())
                    let category = fields.[4].Trim()
                    let reference = fields.[5].Trim()
                    let atoms = parseAtoms fields.[6]
                    let bonds = inferBonds atoms
                    
                    Some {
                        Name = name
                        Atoms = atoms
                        Bonds = bonds
                        Charge = charge
                        Multiplicity = multiplicity
                        Category = category
                        Reference = reference
                    }
                with _ -> None
            else None
    
    /// Load molecules from CSV content
    let private loadFromCsvContent (content: string) : Molecule array =
        content.Split([|'\n'; '\r'|], StringSplitOptions.RemoveEmptyEntries)
        |> Array.skip 1  // Skip header
        |> Array.choose parseMoleculeLine
    
    // ========================================================================
    // INTERNAL DATA STRUCTURES (lazy loaded)
    // ========================================================================
    
    /// All molecules from all CSV sources
    let private allMolecules = lazy (
        [|
            yield! loadFromCsvContent commonCsvData
            yield! loadFromCsvContent hydrocarbonsCsvData
            yield! loadFromCsvContent materialsCsvData
        |]
    )
    
    /// Lookup by name (case-insensitive)
    let private byNameMap = lazy (
        allMolecules.Value
        |> Array.map (fun m -> m.Name.ToLowerInvariant(), m)
        |> Map.ofArray
    )
    
    // ========================================================================
    // PUBLIC API
    // ========================================================================
    
    /// Get all molecules in the library
    let all () : Molecule array = allMolecules.Value
    
    /// Get molecule by name (case-insensitive)
    /// Returns None if not found
    let tryGet (name: string) : Molecule option =
        byNameMap.Value.TryFind (name.ToLowerInvariant())
    
    /// Get molecule by name (case-insensitive)
    /// Throws if not found
    let get (name: string) : Molecule =
        match tryGet name with
        | Some m -> m
        | None -> failwithf "Molecule not found: %s" name
    
    /// Search molecules by name (partial match, case-insensitive)
    let search (query: string) : Molecule array =
        let q = query.ToLowerInvariant()
        allMolecules.Value
        |> Array.filter (fun m -> m.Name.ToLowerInvariant().Contains(q))
    
    /// Get molecules by category (exact match, case-insensitive)
    let byCategory (category: string) : Molecule array =
        let cat = category.ToLowerInvariant()
        allMolecules.Value
        |> Array.filter (fun m -> m.Category.ToLowerInvariant() = cat)
    
    /// Get all unique categories in the library
    let categories () : string array =
        allMolecules.Value
        |> Array.map (fun m -> m.Category)
        |> Array.distinct
        |> Array.sort
    
    /// Get count of molecules in the library
    let count () : int = allMolecules.Value.Length
    
    /// Check if a molecule exists in the library
    let exists (name: string) : bool = tryGet name |> Option.isSome
    
    // ========================================================================
    // CATEGORY-SPECIFIC ACCESSORS
    // ========================================================================
    
    /// Get all diatomic molecules
    let diatomics () : Molecule array = byCategory "diatomic"
    
    /// Get all triatomic molecules
    let triatomics () : Molecule array = byCategory "triatomic"
    
    /// Get all aromatic molecules
    let aromatics () : Molecule array = byCategory "aromatic"
    
    /// Get all alkanes
    let alkanes () : Molecule array = byCategory "alkane"
    
    /// Get all alkenes
    let alkenes () : Molecule array = byCategory "alkene"
    
    /// Get all alkynes
    let alkynes () : Molecule array = byCategory "alkyne"
    
    /// Get all metal hydrides
    let metalHydrides () : Molecule array = byCategory "metal_hydride"
    
    /// Get all metal dimers
    let metalDimers () : Molecule array = byCategory "metal_dimer"
    
    /// Get all quantum dot materials
    let quantumDots () : Molecule array = byCategory "quantum_dot"
    
    /// Get all metal oxides
    let metalOxides () : Molecule array = byCategory "metal_oxide"
    
    /// Get all semiconductor materials
    let semiconductors () : Molecule array = byCategory "semiconductor"
    
    /// Get all catalyst molecules
    let catalysts () : Molecule array = byCategory "catalyst"
    
    /// Get all alcohols
    let alcohols () : Molecule array = byCategory "alcohol"
    
    /// Get all aldehydes
    let aldehydes () : Molecule array = byCategory "aldehyde"
    
    /// Get all carboxylic acids
    let carboxylicAcids () : Molecule array = byCategory "carboxylic_acid"
    
    /// Get all small molecules (NH3, PH3, etc.)
    let smallMolecules () : Molecule array = byCategory "small"
