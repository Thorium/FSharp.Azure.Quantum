// ==============================================================================
// Protein Structure Analysis Example (PDB Parsing + VQE)
// ==============================================================================
// Demonstrates parsing PDB files, analyzing protein binding sites, and running
// VQE on extracted binding site fragments for quantum-enhanced drug discovery.
//
// Business Context:
// A pharmaceutical research team needs to analyze protein structures from the
// Protein Data Bank (PDB) to identify binding sites and calculate interaction
// energies with potential drug candidates.
//
// This example shows:
// - Parsing PDB file format (ATOM/HETATM records)
// - Extracting binding pocket residues near ligand
// - Calculating geometric properties and druggability
// - Extracting minimal fragment for VQE calculation
// - Running VQE on the fragment via IQuantumBackend
//
// Quantum Advantage:
// While PDB parsing is classical, the extracted binding site enables:
// - VQE calculations on active site fragments
// - Quantum-enhanced binding affinity prediction
// - Electron correlation effects in protein-ligand interactions
//
// CURRENT LIMITATIONS (NISQ era):
// - Full protein simulation impossible (~1000s of atoms)
// - Must extract minimal binding site fragment (10-20 atoms)
// - Fragment Molecular Orbital (FMO) approach required
//
// Usage:
//   dotnet fsi ProteinStructure.fsx
//   dotnet fsi ProteinStructure.fsx -- --help
//   dotnet fsi ProteinStructure.fsx -- --cutoff 5.0 --max-iterations 50
//   dotnet fsi ProteinStructure.fsx -- --output results.json --csv results.csv --quiet
//
// ==============================================================================

(*
Background Theory
-----------------

BIOCHEMISTRY FOUNDATION (Harper's Illustrated Biochemistry, 28th Ed.):

Chapter 5: Proteins: Higher Orders of Structure
  - Primary structure: Amino acid sequence
  - Secondary structure: Alpha-helix, beta-sheet, turns, loops
  - Tertiary structure: 3D folding (what PDB files capture)
  - Quaternary structure: Multi-subunit assemblies

Chapter 7: Enzymes: Mechanism of Action
  - Active site geometry and substrate specificity
  - Lock-and-key vs induced-fit models
  - Transition state theory (drugs often mimic TS)

Chapter 8: Enzymes: Kinetics
  - Michaelis-Menten kinetics (Km, Vmax, kcat)
  - Inhibition types: Competitive, non-competitive, uncompetitive
  - Ki determination (binding affinity of inhibitor)

PROTEIN DATA BANK (PDB) FORMAT:
The PDB file format is the standard for representing 3D structures of biological
macromolecules determined by X-ray crystallography, NMR, or cryo-EM.

Key record types:
  ATOM   - Standard amino acid atoms
  HETATM - Ligands, waters, ions, modified residues
  CONECT - Explicit bonds (usually for HETATM)

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

References:
  [1] Berman, H.M. et al. "The Protein Data Bank" Nucleic Acids Res. (2000)
  [2] Wikipedia: Protein_Data_Bank_(file_format)
  [3] Volkamer, A. et al. "DoGSiteScorer" J. Chem. Inf. Model. (2012)
*)

#r "../../src/FSharp.Azure.Quantum/bin/Debug/net10.0/FSharp.Azure.Quantum.dll"

#load "../_common/Cli.fs"
#load "../_common/Data.fs"
#load "../_common/Reporting.fs"

open System
open FSharp.Azure.Quantum.Core
open FSharp.Azure.Quantum.Core.BackendAbstraction
open FSharp.Azure.Quantum.Backends.LocalBackend
open FSharp.Azure.Quantum.QuantumChemistry
open FSharp.Azure.Quantum.QuantumChemistry.QuantumChemistryBuilder
open FSharp.Azure.Quantum.Examples.Common

// ==============================================================================
// CLI PARSING
// ==============================================================================

let argv = fsi.CommandLineArgs |> Array.skip 1
let args = Cli.parse argv
Cli.exitIfHelp "ProteinStructure.fsx" "PDB parsing, binding site analysis, and VQE fragment energy calculation"
    [ { Cli.OptionSpec.Name = "cutoff"; Description = "Binding site cutoff distance in Angstroms (default: 5.0)"; Default = Some "5.0" }
      { Cli.OptionSpec.Name = "max-fragment"; Description = "Maximum atoms in quantum fragment (default: 20)"; Default = Some "20" }
      { Cli.OptionSpec.Name = "max-iterations"; Description = "VQE max iterations (default: 50)"; Default = Some "50" }
      { Cli.OptionSpec.Name = "tolerance"; Description = "VQE convergence tolerance (default: 1e-4)"; Default = Some "1e-4" }
      { Cli.OptionSpec.Name = "output"; Description = "Write results to JSON file"; Default = None }
      { Cli.OptionSpec.Name = "csv"; Description = "Write results to CSV file"; Default = None }
      { Cli.OptionSpec.Name = "quiet"; Description = "Suppress informational output"; Default = None } ]
    args

let quiet = Cli.hasFlag "quiet" args
let bindingSiteCutoff = Cli.getFloatOr "cutoff" 5.0 args
let maxFragmentAtoms = Cli.getIntOr "max-fragment" 20 args
let maxIterations = Cli.getIntOr "max-iterations" 50 args
let tolerance = Cli.getFloatOr "tolerance" 1e-4 args

/// Minimum atoms for quantum fragment
let minFragmentAtoms = 4

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
    IsHetAtom: bool
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
    Volume: float
    Centroid: float * float * float
    HydrophobicFraction: float
    HydrogenBondSites: int
}

// ==============================================================================
// PDB PARSING
// ==============================================================================

let results = System.Collections.Generic.List<Map<string, string>>()

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
                    else atomName.[0..0]

                Some {
                    SerialNumber = serial
                    AtomName = atomName
                    AltLocation = altLoc
                    ResidueName = resName
                    ChainId = chainId
                    ResidueSeq = resSeq
                    X = x; Y = y; Z = z
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
            name <> "HOH" && name <> "WAT"
        { Name = name; ChainId = chain; SeqNumber = seq
          Atoms = atomList; IsLigand = isLigand })

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
    let waters = atoms |> List.filter (fun a -> a.ResidueName = "HOH" || a.ResidueName = "WAT")
    let ligands = residues |> List.filter (fun r -> r.IsLigand)
    let chains =
        residues
        |> List.filter (fun r -> not r.IsLigand && r.Name <> "HOH" && r.Name <> "WAT")
        |> List.groupBy (fun r -> r.ChainId)
        |> List.map (fun (chainId, res) -> { Id = chainId; Residues = res })

    { Header = header; Title = title; Chains = chains; Ligands = ligands; Waters = waters }

// ==============================================================================
// SAMPLE PDB DATA
// ==============================================================================
// Simplified excerpt -- real PDB files are much larger.
// Example: Fragment of a kinase with ATP-binding site.

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
// BINDING SITE ANALYSIS FUNCTIONS
// ==============================================================================

/// Calculate Euclidean distance between two atoms
let atomDistance (a1: PdbAtom) (a2: PdbAtom) : float =
    let dx = a2.X - a1.X
    let dy = a2.Y - a1.Y
    let dz = a2.Z - a1.Z
    sqrt(dx*dx + dy*dy + dz*dz)

/// Check if residue is within cutoff of any ligand atom
let isNearLigand (cutoff: float) (ligandAtoms: PdbAtom list) (residue: Residue) : bool =
    residue.Atoms
    |> List.exists (fun resAtom ->
        ligandAtoms
        |> List.exists (fun ligAtom -> atomDistance resAtom ligAtom <= cutoff))

/// Kyte-Doolittle hydrophobicity scale
let hydrophobicityScale =
    Map.ofList [
        ("ALA", 1.8);  ("ARG", -4.5); ("ASN", -3.5); ("ASP", -3.5)
        ("CYS", 2.5);  ("GLN", -3.5); ("GLU", -3.5); ("GLY", -0.4)
        ("HIS", -3.2); ("ILE", 4.5);  ("LEU", 3.8);  ("LYS", -3.9)
        ("MET", 1.9);  ("PHE", 2.8);  ("PRO", -1.6); ("SER", -0.8)
        ("THR", -0.7); ("TRP", -0.9); ("TYR", -1.3); ("VAL", 4.2)
    ]

/// Count hydrogen bond donors/acceptors in residue (N and O atoms)
let countHBondSites (residue: Residue) : int =
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

/// Estimate binding site volume (bounding box with van der Waals correction)
let estimateVolume (atoms: PdbAtom list) : float =
    if List.isEmpty atoms then 0.0
    else
        let xs = atoms |> List.map (fun a -> a.X)
        let ys = atoms |> List.map (fun a -> a.Y)
        let zs = atoms |> List.map (fun a -> a.Z)
        let dx = (List.max xs) - (List.min xs) + 3.0
        let dy = (List.max ys) - (List.min ys) + 3.0
        let dz = (List.max zs) - (List.min zs) + 3.0
        dx * dy * dz * 0.52  // Approximate sphere packing factor

/// Analyze binding site for a ligand
let analyzeBindingSite (pdb: PdbStructure) (ligand: Residue) (cutoff: float) : BindingSite =
    let ligandAtoms = ligand.Atoms
    let pocketResidues =
        pdb.Chains
        |> List.collect (fun c -> c.Residues)
        |> List.filter (isNearLigand cutoff ligandAtoms)

    let allPocketAtoms = pocketResidues |> List.collect (fun r -> r.Atoms)

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

    let hbondSites = pocketResidues |> List.sumBy countHBondSites

    { LigandId = ligand.Name
      PocketResidues = pocketResidues
      Volume = estimateVolume (ligandAtoms @ allPocketAtoms)
      Centroid = calculateCentroid ligandAtoms
      HydrophobicFraction = hydrophobicFraction
      HydrogenBondSites = hbondSites }

/// Assess druggability of a binding site
let assessDruggability (site: BindingSite) : float * string =
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

/// Extract minimal fragment for quantum calculation
let extractQuantumFragment (site: BindingSite) (ligand: Residue) (maxAtoms: int) : (string * (float * float * float)) list =
    let ligandAtoms =
        ligand.Atoms
        |> List.map (fun a -> (a.Element, (a.X, a.Y, a.Z)))

    let backboneAtoms =
        site.PocketResidues
        |> List.collect (fun res ->
            res.Atoms
            |> List.filter (fun a -> List.contains a.AtomName ["N"; "CA"; "C"; "O"])
            |> List.take (min 2 (List.length res.Atoms))
            |> List.map (fun a -> (a.Element, (a.X, a.Y, a.Z))))

    (ligandAtoms @ backboneAtoms)
    |> List.truncate maxAtoms

// ==============================================================================
// MAIN ANALYSIS
// ==============================================================================

if not quiet then
    printfn "=========================================="
    printfn " Protein Structure Analysis (PDB + VQE)"
    printfn "=========================================="
    printfn ""
    printfn "Configuration:"
    printfn "  Binding site cutoff: %.1f A" bindingSiteCutoff
    printfn "  Max fragment atoms: %d" maxFragmentAtoms
    printfn "  VQE max iterations: %d" maxIterations
    printfn "  VQE tolerance: %.1e" tolerance
    printfn ""

if not quiet then printfn "Parsing PDB structure..."
let pdb = parsePdbContent samplePdbContent

if not quiet then
    printfn ""
    printfn "Structure Summary:"
    printfn "  Header: %s" (if pdb.Header.Length > 60 then pdb.Header.[0..59] + "..." else pdb.Header)
    printfn "  Title:  %s" (if pdb.Title.Length > 60 then pdb.Title.[0..59] + "..." else pdb.Title)
    printfn "  Chains: %d" (List.length pdb.Chains)
    printfn "  Ligands: %d" (List.length pdb.Ligands)
    printfn "  Water molecules: %d" (List.length pdb.Waters)
    printfn ""

// Store structure summary
results.Add(
    [ "type", "structure_summary"
      "chains", string (List.length pdb.Chains)
      "ligands", string (List.length pdb.Ligands)
      "waters", string (List.length pdb.Waters) ]
    |> Map.ofList)

// Display chain info
for chain in pdb.Chains do
    let residueNames = chain.Residues |> List.map (fun r -> r.Name) |> List.distinct
    if not quiet then
        printfn "Chain %c:" chain.Id
        printfn "  Residues: %d" (List.length chain.Residues)
        printfn "  Unique residue types: %s" (String.concat ", " residueNames)
        printfn ""

// ==============================================================================
// BINDING SITE ANALYSIS + DRUGGABILITY
// ==============================================================================

if not quiet then
    printfn "=========================================="
    printfn " Binding Site Analysis"
    printfn "=========================================="
    printfn ""

for ligand in pdb.Ligands do
    let site = analyzeBindingSite pdb ligand bindingSiteCutoff
    let cx, cy, cz = site.Centroid
    let druggScore, druggAssessment = assessDruggability site

    if not quiet then
        printfn "Ligand: %s (chain %c, position %d)" ligand.Name ligand.ChainId ligand.SeqNumber
        printfn "  Pocket residues: %d" (List.length site.PocketResidues)
        printfn "  Estimated volume: %.1f A^3" site.Volume
        printfn "  Centroid: (%.2f, %.2f, %.2f)" cx cy cz
        printfn "  Hydrophobic fraction: %.1f%%" (site.HydrophobicFraction * 100.0)
        printfn "  H-bond sites: %d" site.HydrogenBondSites
        printfn "  Druggability score: %.2f (%s)" druggScore druggAssessment
        printfn ""
        printfn "  Pocket composition:"
        for res in site.PocketResidues do
            let hydro =
                hydrophobicityScale
                |> Map.tryFind res.Name
                |> Option.defaultValue 0.0
            let hydroLabel = if hydro > 0.0 then "hydrophobic" else "polar"
            printfn "    - %s%d (%s, %d atoms)" res.Name res.SeqNumber hydroLabel (List.length res.Atoms)
        printfn ""

    results.Add(
        [ "type", "binding_site"
          "ligand_id", ligand.Name
          "pocket_residues", string (List.length site.PocketResidues)
          "volume_a3", sprintf "%.1f" site.Volume
          "centroid_x", sprintf "%.2f" cx
          "centroid_y", sprintf "%.2f" cy
          "centroid_z", sprintf "%.2f" cz
          "hydrophobic_fraction_pct", sprintf "%.1f" (site.HydrophobicFraction * 100.0)
          "hbond_sites", string site.HydrogenBondSites
          "druggability_score", sprintf "%.2f" druggScore
          "druggability_assessment", druggAssessment ]
        |> Map.ofList)

// ==============================================================================
// QUANTUM FRAGMENT EXTRACTION + VQE
// ==============================================================================

if not quiet then
    printfn "=========================================="
    printfn " Quantum Fragment Extraction + VQE"
    printfn "=========================================="
    printfn ""

let backend = LocalBackend() :> IQuantumBackend

for ligand in pdb.Ligands do
    let site = analyzeBindingSite pdb ligand bindingSiteCutoff
    let fragment = extractQuantumFragment site ligand maxFragmentAtoms

    let elementCounts = fragment |> List.countBy fst |> List.sortBy fst

    if not quiet then
        printfn "Fragment for %s:" ligand.Name
        printfn "  Total atoms: %d" (List.length fragment)
        for (element, count) in elementCounts do
            printfn "    %s: %d" element count

    if List.length fragment >= minFragmentAtoms then
        if not quiet then
            printfn "  Status: Running VQE on fragment..."
            printfn ""

        // Build molecule from extracted fragment
        let fragmentMolecule : Molecule = {
            Name = sprintf "%s-BindingSiteFragment" ligand.Name
            Atoms =
                fragment
                |> List.map (fun (elem, pos) ->
                    { Element = elem; Position = pos })
            Bonds = []  // Bonds inferred from geometry by the VQE framework
            Charge = 0
            Multiplicity = 1
        }

        let config = {
            Method = GroundStateMethod.VQE
            Backend = Some backend
            MaxIterations = maxIterations
            Tolerance = tolerance
            InitialParameters = None
            ProgressReporter = None
            ErrorMitigation = None
            IntegralProvider = None
        }

        let startTime = DateTime.Now
        let result = GroundStateEnergy.estimateEnergy fragmentMolecule config |> Async.RunSynchronously
        let elapsed = (DateTime.Now - startTime).TotalSeconds

        match result with
        | Ok vqeResult ->
            if not quiet then
                printfn "  VQE Result:"
                printfn "    Fragment energy: %.6f Hartree" vqeResult.Energy
                printfn "    Computation time: %.2f s" elapsed
                printfn "    Backend: %s" backend.Name
                printfn ""

            results.Add(
                [ "type", "vqe_fragment"
                  "ligand_id", ligand.Name
                  "fragment_atoms", string (List.length fragment)
                  "energy_hartree", sprintf "%.6f" vqeResult.Energy
                  "computation_time_s", sprintf "%.2f" elapsed
                  "max_iterations", string maxIterations
                  "tolerance", sprintf "%.1e" tolerance
                  "backend", backend.Name ]
                |> Map.ofList)

        | Error err ->
            if not quiet then
                printfn "  VQE Warning: %s" err.Message
                printfn ""

            results.Add(
                [ "type", "vqe_error"
                  "ligand_id", ligand.Name
                  "fragment_atoms", string (List.length fragment)
                  "error", err.Message ]
                |> Map.ofList)
    else
        if not quiet then
            printfn "  Status: Fragment too small (%d atoms, need >= %d)" (List.length fragment) minFragmentAtoms
            printfn ""

        results.Add(
            [ "type", "fragment_too_small"
              "ligand_id", ligand.Name
              "fragment_atoms", string (List.length fragment)
              "min_required", string minFragmentAtoms ]
            |> Map.ofList)

// ==============================================================================
// DRUG DISCOVERY CONTEXT
// ==============================================================================

if not quiet then
    printfn "=========================================="
    printfn " Drug Discovery Context"
    printfn "=========================================="
    printfn ""
    printfn "This example demonstrates the full structure-based drug discovery pipeline:"
    printfn ""
    printfn "1. Parse protein structure (PDB format)"
    printfn "2. Identify binding pocket near ligand"
    printfn "3. Assess druggability (volume, polarity, H-bond capacity)"
    printfn "4. Extract minimal fragment for quantum calculation"
    printfn "5. Run VQE to compute fragment ground state energy"
    printfn ""
    printfn "In practice, binding energy = E_complex - E_protein_fragment - E_ligand"
    printfn "requires running VQE on each component separately."
    printfn ""

// ==============================================================================
// SUGGESTED EXTENSIONS
// ==============================================================================

if not quiet then
    printfn "Suggested Extensions"
    printfn "--------------------------------------------------------------"
    printfn ""
    printfn "1. Load real PDB file:"
    printfn "   - Parse actual crystal structure from RCSB (https://www.rcsb.org/)"
    printfn "   - Use --cutoff to adjust binding pocket radius"
    printfn ""
    printfn "2. Fragment Molecular Orbital (FMO) approach:"
    printfn "   - Divide pocket into overlapping fragments"
    printfn "   - Calculate each fragment with VQE separately"
    printfn "   - Sum interaction energies (see CaffeineEnergy.fsx)"
    printfn ""
    printfn "3. Binding energy decomposition:"
    printfn "   - Run VQE on complex, protein fragment, and ligand separately"
    printfn "   - dE = E_complex - E_protein - E_ligand"
    printfn "   - See BindingAffinity.fsx for complete workflow"
    printfn ""
    printfn "4. Scale up with Azure Quantum backends:"
    printfn "   - Larger active spaces on real hardware"
    printfn "   - Deeper VQE circuits for better correlation energy"
    printfn ""

// ==============================================================================
// STRUCTURED OUTPUT
// ==============================================================================

let resultsList = results |> Seq.toList

match Cli.tryGet "output" args with
| Some path ->
    Reporting.writeJson path resultsList
    if not quiet then printfn "Results written to %s" path
| None -> ()

match Cli.tryGet "csv" args with
| Some path ->
    let header = [ "type"; "chains"; "ligands"; "waters"; "ligand_id"; "pocket_residues"; "volume_a3"; "centroid_x"; "centroid_y"; "centroid_z"; "hydrophobic_fraction_pct"; "hbond_sites"; "druggability_score"; "druggability_assessment"; "fragment_atoms"; "energy_hartree"; "computation_time_s"; "max_iterations"; "tolerance"; "backend"; "min_required"; "error" ]
    let rows =
        resultsList
        |> List.map (fun m ->
            header |> List.map (fun h -> m |> Map.tryFind h |> Option.defaultValue ""))
    Reporting.writeCsv path header rows
    if not quiet then printfn "Results written to %s" path
| None -> ()

if argv.Length = 0 && not quiet then
    printfn ""
    printfn "Tip: Run with --help to see all available options."
    printfn "     Use --output results.json --csv results.csv for structured output."
    printfn ""
