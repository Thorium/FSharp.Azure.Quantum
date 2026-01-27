// ==============================================================================
// Protein Structure Analysis Example (PDB Parsing)
// ==============================================================================
// Demonstrates parsing PDB files and analyzing protein binding sites for
// quantum-enhanced drug discovery applications.
//
// Business Context:
// A pharmaceutical research team needs to analyze protein structures from the
// Protein Data Bank (PDB) to identify binding sites and calculate interaction
// energies with potential drug candidates.
//
// This example shows:
// - Parsing PDB file format (ATOM/HETATM records)
// - Extracting binding pocket residues
// - Calculating geometric properties of binding sites
// - Preparing protein fragments for quantum calculations
// - Identifying key residues for ligand interaction
//
// Quantum Advantage:
// While PDB parsing is classical, the extracted binding site information enables:
// - VQE calculations on active site fragments
// - Quantum-enhanced binding affinity prediction
// - Electron correlation effects in protein-ligand interactions
//
// CURRENT LIMITATIONS (NISQ era):
// - Full protein simulation impossible (~1000s of atoms)
// - Must extract minimal binding site fragment (10-20 atoms)
// - Fragment Molecular Orbital (FMO) approach required
//
// ==============================================================================

(*
===============================================================================
 Background Theory
===============================================================================

BIOCHEMISTRY FOUNDATION (Harper's Illustrated Biochemistry, 28th Ed.):

Understanding protein structure is essential for rational drug design:

Chapter 5: Proteins: Higher Orders of Structure
  - Primary structure: Amino acid sequence
  - Secondary structure: Alpha-helix, beta-sheet, turns, loops
  - Tertiary structure: 3D folding (what PDB files capture)
  - Quaternary structure: Multi-subunit assemblies
  - Folding is driven by hydrophobic effect (entropy of water)

Chapter 6: Proteins: Myoglobin & Hemoglobin
  - Heme proteins as drug targets (CYP450, hemoglobin)
  - Allosteric regulation and cooperative binding
  - Oxygen binding curves (Hill equation)
  - Relevance: Many drugs target heme enzymes

Chapter 7: Enzymes: Mechanism of Action
  - Active site geometry and substrate specificity
  - Lock-and-key vs induced-fit models
  - Transition state theory (drugs often mimic TS)
  - Catalytic mechanisms (acid-base, covalent, metal ion)

Chapter 8: Enzymes: Kinetics
  - Michaelis-Menten kinetics (Km, Vmax, kcat)
  - Inhibition types: Competitive, non-competitive, uncompetitive
  - Ki determination (binding affinity of inhibitor)
  - Lineweaver-Burk and Eadie-Hofstee plots

Chapter 4: Proteins: Determination of Primary Structure
  - Sequencing methods (mass spectrometry)
  - Post-translational modifications
  - Disulfide bond mapping

DRUG TARGET CONSIDERATIONS:
  - Binding pocket druggability (volume, polarity balance)
  - Selectivity over related family members
  - Allosteric vs orthosteric sites
  - Covalent vs non-covalent inhibition strategies

===============================================================================

PROTEIN DATA BANK (PDB) FORMAT:
The PDB file format is the standard for representing 3D structures of biological
macromolecules determined by X-ray crystallography, NMR, or cryo-EM.

Key record types:
  ATOM   - Standard amino acid atoms
  HETATM - Ligands, waters, ions, modified residues
  CONECT - Explicit bonds (usually for HETATM)
  SEQRES - Amino acid sequence
  HEADER - Metadata (protein name, date, method)

ATOM Record Format (columns):
  1-6:   Record name ("ATOM  " or "HETATM")
  7-11:  Atom serial number
  13-16: Atom name (e.g., "CA", "CB", "N", "O")
  17:    Alternate location indicator
  18-20: Residue name (e.g., "ALA", "GLY", "HIS")
  22:    Chain identifier
  23-26: Residue sequence number
  31-38: X coordinate (Angstroms)
  39-46: Y coordinate (Angstroms)
  47-54: Z coordinate (Angstroms)
  55-60: Occupancy
  61-66: Temperature factor (B-factor)
  77-78: Element symbol

BINDING SITE ANALYSIS:
The binding pocket is identified by:
1. Finding ligand atoms (HETATM records)
2. Identifying protein residues within cutoff distance (typically 4-5 A)
3. Extracting the binding pocket residues for analysis

DRUGGABILITY ASSESSMENT:
Properties that make a binding site "druggable":
- Volume: 300-1500 A^3 (too small = no room, too large = weak binding)
- Enclosure: Partially enclosed (not flat surface)
- Hydrophobicity balance: Mix of polar and non-polar residues
- Presence of hydrogen bond donors/acceptors

Key Equations:
  Distance: d = sqrt((x2-x1)^2 + (y2-y1)^2 + (z2-z1)^2)
  Volume: Estimated by convex hull or alpha shapes
  Hydrophobicity: Kyte-Doolittle scale per residue

References:
  [1] Berman, H.M. et al. "The Protein Data Bank" Nucleic Acids Res. (2000)
  [2] Wikipedia: Protein_Data_Bank_(file_format)
  [3] Volkamer, A. et al. "DoGSiteScorer" J. Chem. Inf. Model. (2012)
  [4] See also: _data/PHARMA_GLOSSARY.md for TPP criteria
===============================================================================
*)

#r "../../src/FSharp.Azure.Quantum/bin/Debug/net10.0/FSharp.Azure.Quantum.dll"

open System
open System.IO
open FSharp.Azure.Quantum.Core
open FSharp.Azure.Quantum.Core.BackendAbstraction
open FSharp.Azure.Quantum.Backends.LocalBackend
open FSharp.Azure.Quantum.QuantumChemistry
open FSharp.Azure.Quantum.QuantumChemistry.QuantumChemistryBuilder

// ==============================================================================
// CONFIGURATION
// ==============================================================================

/// Cutoff distance for binding site residues (Angstroms)
let bindingSiteCutoff = 5.0

/// Minimum atoms for quantum fragment
let minFragmentAtoms = 4

/// Maximum atoms for NISQ quantum calculation
let maxFragmentAtoms = 20

// ==============================================================================
// PDB DATA TYPES
// ==============================================================================

/// Represents a single atom from PDB file
type PdbAtom = {
    SerialNumber: int
    AtomName: string
    AltLocation: char option
    ResidueName: string
    ChainId: char
    ResidueSeq: int
    X: float
    Y: float
    Z: float
    Occupancy: float
    TempFactor: float
    Element: string
    IsHetAtom: bool  // True for HETATM records
}

/// Represents a residue (amino acid or ligand)
type Residue = {
    Name: string
    ChainId: char
    SeqNumber: int
    Atoms: PdbAtom list
    IsLigand: bool
}

/// Represents a protein chain
type Chain = {
    Id: char
    Residues: Residue list
}

/// Represents a complete PDB structure
type PdbStructure = {
    Header: string
    Title: string
    Chains: Chain list
    Ligands: Residue list
    Waters: PdbAtom list
}

/// Binding site analysis results
type BindingSite = {
    LigandId: string
    PocketResidues: Residue list
    Volume: float  // Estimated in A^3
    Centroid: float * float * float
    HydrophobicFraction: float
    HydrogenBondSites: int
}

// ==============================================================================
// PDB PARSING
// ==============================================================================

printfn "=========================================="
printfn " Protein Structure Analysis (PDB Parsing)"
printfn "=========================================="
printfn ""

/// Parse a single ATOM/HETATM line
let parseAtomLine (line: string) : PdbAtom option =
    if line.Length < 54 then None
    else
        try
            let recordType = line.[0..5].Trim()
            if recordType <> "ATOM" && recordType <> "HETATM" then None
            else
                let serial = Int32.Parse(line.[6..10].Trim())
                let atomName = line.[12..15].Trim()
                let altLoc = if line.[16] = ' ' then None else Some line.[16]
                let resName = line.[17..19].Trim()
                let chainId = line.[21]
                let resSeq = Int32.Parse(line.[22..25].Trim())
                let x = Double.Parse(line.[30..37].Trim())
                let y = Double.Parse(line.[38..45].Trim())
                let z = Double.Parse(line.[46..53].Trim())
                let occupancy = 
                    if line.Length > 60 then 
                        try Double.Parse(line.[54..59].Trim()) with _ -> 1.0
                    else 1.0
                let tempFactor =
                    if line.Length > 66 then
                        try Double.Parse(line.[60..65].Trim()) with _ -> 0.0
                    else 0.0
                let element =
                    if line.Length > 78 then line.[76..77].Trim()
                    else atomName.[0..0]  // Fallback to first char
                
                Some {
                    SerialNumber = serial
                    AtomName = atomName
                    AltLocation = altLoc
                    ResidueName = resName
                    ChainId = chainId
                    ResidueSeq = resSeq
                    X = x
                    Y = y
                    Z = z
                    Occupancy = occupancy
                    TempFactor = tempFactor
                    Element = element
                    IsHetAtom = (recordType = "HETATM")
                }
        with _ -> None

/// Group atoms into residues
let groupIntoResidues (atoms: PdbAtom list) : Residue list =
    atoms
    |> List.groupBy (fun a -> (a.ChainId, a.ResidueSeq, a.ResidueName))
    |> List.map (fun ((chain, seq, name), atomList) ->
        let isLigand = 
            atomList |> List.exists (fun a -> a.IsHetAtom) &&
            name <> "HOH" && name <> "WAT"  // Exclude water
        {
            Name = name
            ChainId = chain
            SeqNumber = seq
            Atoms = atomList
            IsLigand = isLigand
        })

/// Parse PDB content (string)
let parsePdbContent (content: string) : PdbStructure =
    let lines = content.Split([|'\n'; '\r'|], StringSplitOptions.RemoveEmptyEntries)
    
    let header = 
        lines 
        |> Array.tryFind (fun l -> l.StartsWith("HEADER"))
        |> Option.defaultValue ""
    
    let title =
        lines
        |> Array.filter (fun l -> l.StartsWith("TITLE"))
        |> Array.map (fun l -> if l.Length > 10 then l.[10..].Trim() else "")
        |> String.concat " "
    
    let atoms = 
        lines 
        |> Array.choose parseAtomLine
        |> Array.toList
    
    let residues = groupIntoResidues atoms
    
    let waters = 
        atoms 
        |> List.filter (fun a -> a.ResidueName = "HOH" || a.ResidueName = "WAT")
    
    let ligands = 
        residues 
        |> List.filter (fun r -> r.IsLigand)
    
    let chains =
        residues
        |> List.filter (fun r -> not r.IsLigand && r.Name <> "HOH" && r.Name <> "WAT")
        |> List.groupBy (fun r -> r.ChainId)
        |> List.map (fun (chainId, res) -> { Id = chainId; Residues = res })
    
    {
        Header = header
        Title = title
        Chains = chains
        Ligands = ligands
        Waters = waters
    }

// ==============================================================================
// SAMPLE PDB DATA (Minimal example)
// ==============================================================================

// This is a simplified excerpt - real PDB files are much larger
// Example: Fragment of a kinase with ATP-binding site
let samplePdbContent = """
HEADER    TRANSFERASE                             01-JAN-00   XXXX              
TITLE     SAMPLE KINASE STRUCTURE FOR DEMONSTRATION
ATOM      1  N   ALA A   1       0.000   0.000   0.000  1.00 20.00           N
ATOM      2  CA  ALA A   1       1.458   0.000   0.000  1.00 20.00           C
ATOM      3  C   ALA A   1       2.009   1.420   0.000  1.00 20.00           C
ATOM      4  O   ALA A   1       1.246   2.390   0.000  1.00 20.00           O
ATOM      5  CB  ALA A   1       1.986  -0.728  -1.232  1.00 20.00           C
ATOM     10  N   LYS A   2       3.320   1.520   0.000  1.00 18.00           N
ATOM     11  CA  LYS A   2       3.950   2.840   0.000  1.00 18.00           C
ATOM     12  C   LYS A   2       5.460   2.720   0.000  1.00 18.00           C
ATOM     13  O   LYS A   2       6.050   1.640   0.000  1.00 18.00           O
ATOM     14  CB  LYS A   2       3.560   3.680   1.220  1.00 18.00           C
ATOM     15  NZ  LYS A   2       3.200   5.100   2.800  1.00 18.00           N
ATOM     20  N   GLU A   3       6.050   3.920   0.000  1.00 22.00           N
ATOM     21  CA  GLU A   3       7.480   4.140   0.000  1.00 22.00           C
ATOM     22  C   GLU A   3       8.120   3.480   1.220  1.00 22.00           C
ATOM     23  O   GLU A   3       7.400   3.000   2.100  1.00 22.00           O
ATOM     24  OE1 GLU A   3       9.200   5.800  -1.500  1.00 22.00           O
ATOM     25  OE2 GLU A   3       9.800   5.200  -3.200  1.00 22.00           O
ATOM     30  N   ASP A   4       9.420   3.480   1.220  1.00 19.00           N
ATOM     31  CA  ASP A   4      10.100   2.900   2.400  1.00 19.00           C
ATOM     32  C   ASP A   4      10.100   1.380   2.400  1.00 19.00           C
ATOM     33  O   ASP A   4       9.100   0.700   2.400  1.00 19.00           O
ATOM     34  OD1 ASP A   4      11.800   3.800   4.200  1.00 19.00           O
ATOM     35  OD2 ASP A   4      12.400   3.200   2.800  1.00 19.00           O
ATOM     40  N   VAL A   5      11.280   0.800   2.400  1.00 16.00           N
ATOM     41  CA  VAL A   5      11.400  -0.640   2.400  1.00 16.00           C
ATOM     42  C   VAL A   5      12.820  -1.160   2.400  1.00 16.00           C
ATOM     43  O   VAL A   5      13.760  -0.400   2.400  1.00 16.00           O
ATOM     44  CB  VAL A   5      10.700  -1.300   3.600  1.00 16.00           C
ATOM     45  CG1 VAL A   5      10.700  -2.800   3.600  1.00 16.00           C
ATOM     46  CG2 VAL A   5       9.200  -0.900   3.600  1.00 16.00           C
HETATM  100  C1  ATP A 100       5.500   3.500   3.000  1.00 25.00           C
HETATM  101  C2  ATP A 100       6.200   4.200   3.800  1.00 25.00           C
HETATM  102  N1  ATP A 100       5.800   4.800   4.900  1.00 25.00           N
HETATM  103  C3  ATP A 100       7.500   4.200   3.500  1.00 25.00           C
HETATM  104  N2  ATP A 100       8.200   4.900   4.300  1.00 25.00           N
HETATM  105  C4  ATP A 100       7.600   5.500   5.200  1.00 25.00           C
HETATM  106  C5  ATP A 100       6.200   5.500   5.500  1.00 25.00           C
HETATM  107  N3  ATP A 100       5.500   6.200   6.400  1.00 25.00           N
HETATM  108  O1  ATP A 100       8.200   2.800   1.500  1.00 25.00           O
HETATM  109  P1  ATP A 100       9.500   2.200   1.000  1.00 25.00           P
HETATM  110  O2  ATP A 100       9.800   1.000   1.500  1.00 25.00           O
HETATM  111  O3  ATP A 100      10.600   3.200   1.200  1.00 25.00           O
HETATM  112  O4  ATP A 100       9.200   2.000  -0.500  1.00 25.00           O
END
"""

// ==============================================================================
// BINDING SITE ANALYSIS
// ==============================================================================

/// Calculate Euclidean distance between two atoms
let distance (a1: PdbAtom) (a2: PdbAtom) : float =
    let dx = a2.X - a1.X
    let dy = a2.Y - a1.Y
    let dz = a2.Z - a1.Z
    sqrt(dx*dx + dy*dy + dz*dz)

/// Check if residue is within cutoff of any ligand atom
let isNearLigand (cutoff: float) (ligandAtoms: PdbAtom list) (residue: Residue) : bool =
    residue.Atoms
    |> List.exists (fun resAtom ->
        ligandAtoms
        |> List.exists (fun ligAtom -> distance resAtom ligAtom <= cutoff))

/// Kyte-Doolittle hydrophobicity scale
let hydrophobicityScale = 
    Map.ofList [
        ("ALA", 1.8);  ("ARG", -4.5); ("ASN", -3.5); ("ASP", -3.5)
        ("CYS", 2.5);  ("GLN", -3.5); ("GLU", -3.5); ("GLY", -0.4)
        ("HIS", -3.2); ("ILE", 4.5);  ("LEU", 3.8);  ("LYS", -3.9)
        ("MET", 1.9);  ("PHE", 2.8);  ("PRO", -1.6); ("SER", -0.8)
        ("THR", -0.7); ("TRP", -0.9); ("TYR", -1.3); ("VAL", 4.2)
    ]

/// Identify hydrogen bond donors/acceptors in residue
let countHBondSites (residue: Residue) : int =
    // Simplified: count N and O atoms (potential H-bond sites)
    residue.Atoms
    |> List.filter (fun a -> a.Element = "N" || a.Element = "O")
    |> List.length

/// Calculate binding site centroid
let calculateCentroid (atoms: PdbAtom list) : float * float * float =
    let n = float (List.length atoms)
    if n = 0.0 then (0.0, 0.0, 0.0)
    else
        let sumX = atoms |> List.sumBy (fun a -> a.X)
        let sumY = atoms |> List.sumBy (fun a -> a.Y)
        let sumZ = atoms |> List.sumBy (fun a -> a.Z)
        (sumX / n, sumY / n, sumZ / n)

/// Estimate binding site volume (simplified bounding box)
let estimateVolume (atoms: PdbAtom list) : float =
    if List.isEmpty atoms then 0.0
    else
        let xs = atoms |> List.map (fun a -> a.X)
        let ys = atoms |> List.map (fun a -> a.Y)
        let zs = atoms |> List.map (fun a -> a.Z)
        let dx = (List.max xs) - (List.min xs) + 3.0  // Add van der Waals radii
        let dy = (List.max ys) - (List.min ys) + 3.0
        let dz = (List.max zs) - (List.min zs) + 3.0
        dx * dy * dz * 0.52  // Approximate sphere packing factor

/// Analyze binding site for a ligand
let analyzeBindingSite (pdb: PdbStructure) (ligand: Residue) (cutoff: float) : BindingSite =
    let ligandAtoms = ligand.Atoms
    
    // Find all residues within cutoff
    let pocketResidues =
        pdb.Chains
        |> List.collect (fun c -> c.Residues)
        |> List.filter (isNearLigand cutoff ligandAtoms)
    
    let allPocketAtoms = 
        pocketResidues 
        |> List.collect (fun r -> r.Atoms)
    
    // Calculate hydrophobic fraction
    let hydrophobicFraction =
        let hydrophobicRes = 
            pocketResidues 
            |> List.filter (fun r -> 
                hydrophobicityScale 
                |> Map.tryFind r.Name 
                |> Option.map (fun h -> h > 0.0)
                |> Option.defaultValue false)
        if List.isEmpty pocketResidues then 0.0
        else float (List.length hydrophobicRes) / float (List.length pocketResidues)
    
    // Count H-bond sites
    let hbondSites = 
        pocketResidues 
        |> List.sumBy countHBondSites
    
    {
        LigandId = ligand.Name
        PocketResidues = pocketResidues
        Volume = estimateVolume (ligandAtoms @ allPocketAtoms)
        Centroid = calculateCentroid ligandAtoms
        HydrophobicFraction = hydrophobicFraction
        HydrogenBondSites = hbondSites
    }

// ==============================================================================
// MAIN ANALYSIS
// ==============================================================================

printfn "Parsing PDB structure..."
let pdb = parsePdbContent samplePdbContent

printfn ""
printfn "Structure Summary:"
printfn "  Header: %s" (if pdb.Header.Length > 60 then pdb.Header.[0..59] + "..." else pdb.Header)
printfn "  Title:  %s" (if pdb.Title.Length > 60 then pdb.Title.[0..59] + "..." else pdb.Title)
printfn "  Chains: %d" (List.length pdb.Chains)
printfn "  Ligands: %d" (List.length pdb.Ligands)
printfn "  Water molecules: %d" (List.length pdb.Waters)
printfn ""

// Analyze each chain
for chain in pdb.Chains do
    printfn "Chain %c:" chain.Id
    printfn "  Residues: %d" (List.length chain.Residues)
    let residueNames = 
        chain.Residues 
        |> List.map (fun r -> r.Name) 
        |> List.distinct
    printfn "  Unique residue types: %s" (String.concat ", " residueNames)
    printfn ""

// ==============================================================================
// BINDING SITE ANALYSIS
// ==============================================================================

printfn "=========================================="
printfn " Binding Site Analysis"
printfn "=========================================="
printfn ""

for ligand in pdb.Ligands do
    printfn "Analyzing ligand: %s (chain %c, position %d)" 
        ligand.Name ligand.ChainId ligand.SeqNumber
    
    let site = analyzeBindingSite pdb ligand bindingSiteCutoff
    
    let cx, cy, cz = site.Centroid
    printfn "  Pocket residues: %d" (List.length site.PocketResidues)
    printfn "  Estimated volume: %.1f A^3" site.Volume
    printfn "  Centroid: (%.2f, %.2f, %.2f)" cx cy cz
    printfn "  Hydrophobic fraction: %.1f%%" (site.HydrophobicFraction * 100.0)
    printfn "  H-bond sites: %d" site.HydrogenBondSites
    
    // List pocket residues
    printfn "  Pocket composition:"
    for res in site.PocketResidues do
        let hydro = 
            hydrophobicityScale 
            |> Map.tryFind res.Name 
            |> Option.defaultValue 0.0
        let hydroLabel = if hydro > 0.0 then "hydrophobic" else "polar"
        printfn "    - %s%d (%s, %d atoms)" res.Name res.SeqNumber hydroLabel (List.length res.Atoms)
    printfn ""

// ==============================================================================
// DRUGGABILITY ASSESSMENT
// ==============================================================================

printfn "=========================================="
printfn " Druggability Assessment"
printfn "=========================================="
printfn ""

// Criteria from literature (DoGSiteScorer, FPocket)
// See: _data/PHARMA_GLOSSARY.md for TPP criteria
let assessDruggability (site: BindingSite) =
    let volumeScore = 
        if site.Volume >= 300.0 && site.Volume <= 1500.0 then 1.0
        elif site.Volume >= 200.0 && site.Volume <= 2000.0 then 0.5
        else 0.0
    
    let hydrophobicScore =
        if site.HydrophobicFraction >= 0.3 && site.HydrophobicFraction <= 0.7 then 1.0
        elif site.HydrophobicFraction >= 0.2 && site.HydrophobicFraction <= 0.8 then 0.5
        else 0.0
    
    let hbondScore =
        if site.HydrogenBondSites >= 3 && site.HydrogenBondSites <= 15 then 1.0
        elif site.HydrogenBondSites >= 1 then 0.5
        else 0.0
    
    let totalScore = (volumeScore + hydrophobicScore + hbondScore) / 3.0
    
    let assessment = 
        if totalScore >= 0.8 then "Highly druggable"
        elif totalScore >= 0.5 then "Moderately druggable"
        else "Challenging target"
    
    (totalScore, assessment)

for ligand in pdb.Ligands do
    let site = analyzeBindingSite pdb ligand bindingSiteCutoff
    let score, assessment = assessDruggability site
    
    printfn "Ligand %s binding site:" ligand.Name
    printfn "  Druggability score: %.2f" score
    printfn "  Assessment: %s" assessment
    printfn ""

// ==============================================================================
// QUANTUM FRAGMENT EXTRACTION
// ==============================================================================

printfn "=========================================="
printfn " Quantum Fragment Extraction"
printfn "=========================================="
printfn ""

/// Extract minimal fragment for quantum calculation
let extractQuantumFragment (site: BindingSite) (ligand: Residue) : (string * (float * float * float)) list =
    // Select key atoms: ligand + nearest backbone atoms
    let ligandAtoms = 
        ligand.Atoms 
        |> List.map (fun a -> (a.Element, (a.X, a.Y, a.Z)))
    
    // Get closest atoms from each pocket residue (backbone N, CA, C, O)
    let backboneAtoms =
        site.PocketResidues
        |> List.collect (fun res ->
            res.Atoms
            |> List.filter (fun a -> 
                List.contains a.AtomName ["N"; "CA"; "C"; "O"])
            |> List.take (min 2 (List.length res.Atoms))  // Max 2 atoms per residue
            |> List.map (fun a -> (a.Element, (a.X, a.Y, a.Z))))
    
    // Combine and limit to maxFragmentAtoms
    (ligandAtoms @ backboneAtoms)
    |> List.truncate maxFragmentAtoms

for ligand in pdb.Ligands do
    let site = analyzeBindingSite pdb ligand bindingSiteCutoff
    let fragment = extractQuantumFragment site ligand
    
    printfn "Quantum fragment for %s:" ligand.Name
    printfn "  Total atoms: %d" (List.length fragment)
    
    // Count by element
    let elementCounts = 
        fragment 
        |> List.countBy fst
        |> List.sortBy fst
    
    for (element, count) in elementCounts do
        printfn "    %s: %d" element count
    
    if List.length fragment >= minFragmentAtoms then
        printfn "  Status: Ready for VQE calculation"
    else
        printfn "  Status: Fragment too small, need more atoms"
    printfn ""

// ==============================================================================
// PREPARING FOR VQE (INTEGRATION WITH QUANTUM CHEMISTRY)
// ==============================================================================

printfn "=========================================="
printfn " Integration with Quantum Chemistry"
printfn "=========================================="
printfn ""

printfn "To perform VQE on the extracted fragment:"
printfn ""
printfn "1. Convert PDB atoms to QuantumChemistry.Molecule format:"
printfn "   let molecule : Molecule = {"
printfn "       Name = \"BindingSiteFragment\""
printfn "       Atoms = extractedAtoms |> List.map (fun (elem, pos) ->"
printfn "           { Element = elem; Position = pos })"
printfn "   }"
printfn ""
printfn "2. Use the VQE builder from BindingAffinity.fsx:"
printfn "   let result = quantumChemistry {"
printfn "       molecule molecule"
printfn "       method UCCSD"
printfn "       activeSpace (electrons=4, orbitals=4)"
printfn "       optimizer (COBYLA, maxIterations=100)"
printfn "       backend localBackend"
printfn "   }"
printfn ""
printfn "3. Calculate binding energy as:"
printfn "   Delta_E = E_complex - E_protein_fragment - E_ligand"
printfn ""
printfn "See: examples/DrugDiscovery/BindingAffinity.fsx for complete VQE workflow"
printfn "See: _data/PHARMA_GLOSSARY.md for TPP binding criteria"
printfn ""

// ==============================================================================
// NEXT STEPS
// ==============================================================================

printfn "=========================================="
printfn " Next Steps"
printfn "=========================================="
printfn ""
printfn "1. Load real PDB file:"
printfn "   let pdbContent = File.ReadAllText(\"path/to/protein.pdb\")"
printfn "   let structure = parsePdbContent pdbContent"
printfn ""
printfn "2. For larger binding sites, use Fragment Molecular Orbital (FMO):"
printfn "   - Divide pocket into overlapping fragments"
printfn "   - Calculate each fragment with VQE"
printfn "   - Sum interaction energies"
printfn "   See: CaffeineEnergy.fsx for FMO approach"
printfn ""
printfn "3. Combine with molecular similarity screening:"
printfn "   - Use ProteinStructure.fsx to identify binding site"
printfn "   - Use MolecularSimilarity.fsx to find similar ligands"
printfn "   - Use BindingAffinity.fsx to score top candidates"
printfn ""
printfn "4. ADMET filtering:"
printfn "   - Use ADMETPrediction.fsx for drug-likeness"
printfn "   - Filter candidates by Lipinski's Rule of 5"
printfn "   - Predict metabolic stability"
printfn ""
printfn "5. PDB Resources:"
printfn "   - RCSB PDB: https://www.rcsb.org/"
printfn "   - AlphaFold DB: https://alphafold.ebi.ac.uk/"
printfn "   - PDBe: https://www.ebi.ac.uk/pdbe/"
