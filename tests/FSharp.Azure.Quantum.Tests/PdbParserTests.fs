module PdbParserTests

open Xunit
open FSharp.Azure.Quantum.Data
open FSharp.Azure.Quantum.Data.ChemistryDataProviders
open FSharp.Azure.Quantum.Data.ChemistryDataProviders.PdbParser

// =============================================================================
// TEST DATA: Sample PDB content
// =============================================================================

/// Simple PDB with one ligand (ATP-like)
let simplePdb = """HEADER    TRANSFERASE                             01-JAN-00   1ATP
TITLE     TEST STRUCTURE WITH ATP LIGAND
ATOM      1  N   ALA A   1      11.104   6.134  -6.504  1.00 35.88           N
ATOM      2  CA  ALA A   1      11.639   6.071  -5.147  1.00 36.67           C
ATOM      3  C   ALA A   1      13.559  86.257  95.222  1.00 37.37           C
TER       4      ALA A   1
HETATM    5  PG  ATP A 500      10.885 -15.746 -14.404  1.00 47.84           P
HETATM    6  O1G ATP A 500      11.191 -14.833 -15.531  1.00 50.12           O
HETATM    7  O2G ATP A 500       9.576 -16.338 -14.706  1.00 48.55           O
HETATM    8  O3G ATP A 500      11.995 -16.703 -14.431  1.00 49.88           O
HETATM    9  PB  ATP A 500      10.932 -15.073 -13.100  1.00 49.91           P
HETATM   10  N1  ATP A 500      12.681  37.302 -25.211  1.00 15.56           N
HETATM   11  C2  ATP A 500      11.982  37.996 -26.241  1.00 16.92           C
END
"""

/// PDB with water molecules (should be excluded by default)
let pdbWithWater = """HEADER    TEST
HETATM    1  O   HOH A 101       5.000   5.000   5.000  1.00 20.00           O
HETATM    2  O   HOH A 102       6.000   6.000   6.000  1.00 20.00           O
HETATM    3  C1  LIG A 200       1.000   2.000   3.000  1.00 10.00           C
HETATM    4  C2  LIG A 200       2.000   3.000   4.000  1.00 10.00           C
HETATM    5  O1  LIG A 200       3.000   4.000   5.000  1.00 10.00           O
END
"""

/// PDB with multiple ligands
let pdbMultipleLigands = """HEADER    MULTI-LIGAND TEST
HETATM    1  C1  ABC A 100       1.000   1.000   1.000  1.00 10.00           C
HETATM    2  C2  ABC A 100       2.000   2.000   2.000  1.00 10.00           C
HETATM    3  N1  XYZ B 200       3.000   3.000   3.000  1.00 15.00           N
HETATM    4  O1  XYZ B 200       4.000   4.000   4.000  1.00 15.00           O
HETATM    5  FE  HEM C 300       5.000   5.000   5.000  1.00 20.00          FE
END
"""

/// PDB with NMR models (multiple conformations)
let pdbWithModels = """HEADER    NMR STRUCTURE
MODEL        1
HETATM    1  C1  LIG A 100       1.000   1.000   1.000  1.00 10.00           C
HETATM    2  C2  LIG A 100       2.000   2.000   2.000  1.00 10.00           C
ENDMDL
MODEL        2
HETATM    3  C1  LIG A 100       1.100   1.100   1.100  1.00 10.00           C
HETATM    4  C2  LIG A 100       2.100   2.100   2.100  1.00 10.00           C
ENDMDL
END
"""

/// Minimal ATOM line (just coordinates)
let minimalAtomLine = """HETATM    1  C   LIG A   1       1.234   5.678   9.012"""

/// Complete ATOM line with all fields
let completeAtomLine = """HETATM    1  CA AALA A   1      11.104   6.134  -6.504  0.50 35.88           C1+"""

// =============================================================================
// PARSER TESTS
// =============================================================================

[<Fact>]
let ``parse extracts PDB ID from HEADER`` () =
    let result = parse simplePdb
    match result with
    | Ok structure ->
        Assert.Equal(Some "1ATP", structure.PdbId)
    | Error e -> 
        Assert.True(false, $"Parse failed: {e}")

[<Fact>]
let ``parse extracts TITLE`` () =
    let result = parse simplePdb
    match result with
    | Ok structure ->
        Assert.True(structure.Title.IsSome)
        Assert.Contains("ATP LIGAND", structure.Title.Value)
    | Error e -> 
        Assert.True(false, $"Parse failed: {e}")

[<Fact>]
let ``parse extracts HETATM atoms by default`` () =
    let result = parse simplePdb
    match result with
    | Ok structure ->
        // Should have ATP residue only (ATOM records excluded by default)
        Assert.Equal(1, structure.Residues.Length)
        let atp = structure.Residues.[0]
        Assert.Equal("ATP", atp.Name)
        Assert.Equal(7, atp.Atoms.Length)  // 7 HETATM atoms
    | Error e -> 
        Assert.True(false, $"Parse failed: {e}")

[<Fact>]
let ``parse excludes ATOM records by default`` () =
    let result = parse simplePdb
    match result with
    | Ok structure ->
        // Should not have ALA protein residue
        let hasProtein = structure.Residues |> Array.exists (fun r -> r.Name = "ALA")
        Assert.False(hasProtein, "Should not include ATOM records by default")
    | Error e -> 
        Assert.True(false, $"Parse failed: {e}")

[<Fact>]
let ``parse excludes water by default`` () =
    let result = parse pdbWithWater
    match result with
    | Ok structure ->
        // Should have LIG but not HOH
        Assert.Equal(1, structure.Residues.Length)
        Assert.Equal("LIG", structure.Residues.[0].Name)
    | Error e -> 
        Assert.True(false, $"Parse failed: {e}")

[<Fact>]
let ``parseWithOptions includes water when not excluded`` () =
    let options = { defaultOptions with ExcludeWater = false }
    let result = parseWithOptions options pdbWithWater
    match result with
    | Ok structure ->
        // Should have both HOH and LIG
        Assert.Equal(3, structure.Residues.Length)  // 2 HOH + 1 LIG
    | Error e -> 
        Assert.True(false, $"Parse failed: {e}")

[<Fact>]
let ``parseWithOptions includes ATOM records when specified`` () =
    let options = { defaultOptions with IncludeAtom = true }
    let result = parseWithOptions options simplePdb
    match result with
    | Ok structure ->
        // Should have both ALA and ATP
        let hasAla = structure.Residues |> Array.exists (fun r -> r.Name = "ALA")
        let hasAtp = structure.Residues |> Array.exists (fun r -> r.Name = "ATP")
        Assert.True(hasAla, "Should include ALA")
        Assert.True(hasAtp, "Should include ATP")
    | Error e -> 
        Assert.True(false, $"Parse failed: {e}")

[<Fact>]
let ``parse handles multiple ligands`` () =
    let result = parse pdbMultipleLigands
    match result with
    | Ok structure ->
        Assert.Equal(3, structure.Residues.Length)
        let names = structure.Residues |> Array.map (fun r -> r.Name) |> Array.sort
        Assert.Equal<string>([|"ABC"; "HEM"; "XYZ"|], names)
    | Error e -> 
        Assert.True(false, $"Parse failed: {e}")

[<Fact>]
let ``parse extracts chain IDs`` () =
    let result = parse pdbMultipleLigands
    match result with
    | Ok structure ->
        let chains = 
            structure.Residues 
            |> Array.choose (fun r -> r.ChainId)
            |> Array.sort
        Assert.Equal<char>([|'A'; 'B'; 'C'|], chains)
    | Error e -> 
        Assert.True(false, $"Parse failed: {e}")

[<Fact>]
let ``parse handles NMR models - first model only by default`` () =
    let result = parse pdbWithModels
    match result with
    | Ok structure ->
        // Should only have atoms from model 1
        Assert.Equal(1, structure.Residues.Length)
        let lig = structure.Residues.[0]
        Assert.Equal(2, lig.Atoms.Length)
        // Coordinates should be from model 1
        Assert.Equal(1.0, lig.Atoms.[0].X)
    | Error e -> 
        Assert.True(false, $"Parse failed: {e}")

[<Fact>]
let ``parseWithOptions includes all models when specified`` () =
    let options = { defaultOptions with FirstModelOnly = false }
    let result = parseWithOptions options pdbWithModels
    match result with
    | Ok structure ->
        // Should have atoms from both models
        // They get grouped into the same residue (same resName/chain/resSeq)
        let totalAtoms = structure.Residues |> Array.sumBy (fun r -> r.Atoms.Length)
        Assert.Equal(4, totalAtoms)  // 2 from each model
    | Error e -> 
        Assert.True(false, $"Parse failed: {e}")

[<Fact>]
let ``parse extracts correct coordinates`` () =
    let result = parse simplePdb
    match result with
    | Ok structure ->
        let atp = structure.Residues.[0]
        let pg = atp.Atoms |> Array.find (fun a -> a.Name = "PG")
        Assert.Equal(10.885, pg.X, 3)
        Assert.Equal(-15.746, pg.Y, 3)
        Assert.Equal(-14.404, pg.Z, 3)
    | Error e -> 
        Assert.True(false, $"Parse failed: {e}")

[<Fact>]
let ``parse extracts element symbols`` () =
    let result = parse simplePdb
    match result with
    | Ok structure ->
        let atp = structure.Residues.[0]
        let phosphorus = atp.Atoms |> Array.filter (fun a -> a.Element = "P")
        let oxygen = atp.Atoms |> Array.filter (fun a -> a.Element = "O")
        let nitrogen = atp.Atoms |> Array.filter (fun a -> a.Element = "N")
        let carbon = atp.Atoms |> Array.filter (fun a -> a.Element = "C")
        Assert.Equal(2, phosphorus.Length)  // PG and PB
        Assert.Equal(3, oxygen.Length)      // O1G, O2G, O3G
        Assert.Equal(1, nitrogen.Length)    // N1
        Assert.Equal(1, carbon.Length)      // C2
    | Error e -> 
        Assert.True(false, $"Parse failed: {e}")

[<Fact>]
let ``parseWithOptions filters by residue name`` () =
    let options = { defaultOptions with ResidueFilter = ["ABC"] }
    let result = parseWithOptions options pdbMultipleLigands
    match result with
    | Ok structure ->
        Assert.Equal(1, structure.Residues.Length)
        Assert.Equal("ABC", structure.Residues.[0].Name)
    | Error e -> 
        Assert.True(false, $"Parse failed: {e}")

// =============================================================================
// CONVERSION TESTS
// =============================================================================

[<Fact>]
let ``residueToMoleculeInstance creates correct topology`` () =
    let result = parse simplePdb
    match result with
    | Ok structure ->
        let atp = structure.Residues.[0]
        let mol = residueToMoleculeInstance atp structure.PdbId
        
        Assert.Equal(7, mol.Topology.Atoms.Length)
        Assert.Contains("P", mol.Topology.Atoms)
        Assert.Contains("O", mol.Topology.Atoms)
        Assert.Contains("N", mol.Topology.Atoms)
    | Error e -> 
        Assert.True(false, $"Parse failed: {e}")

[<Fact>]
let ``residueToMoleculeInstance creates geometry`` () =
    let result = parse simplePdb
    match result with
    | Ok structure ->
        let atp = structure.Residues.[0]
        let mol = residueToMoleculeInstance atp structure.PdbId
        
        Assert.True(mol.Geometry.IsSome)
        Assert.Equal("angstrom", mol.Geometry.Value.Units)
        Assert.Equal(7, mol.Geometry.Value.Coordinates.Length)
    | Error e -> 
        Assert.True(false, $"Parse failed: {e}")

[<Fact>]
let ``residueToMoleculeInstance includes metadata`` () =
    let result = parse simplePdb
    match result with
    | Ok structure ->
        let atp = structure.Residues.[0]
        let mol = residueToMoleculeInstance atp structure.PdbId
        
        Assert.Equal("pdb", mol.Topology.Metadata.["format"])
        Assert.Equal("ATP", mol.Topology.Metadata.["residue_name"])
        Assert.Equal("1ATP", mol.Topology.Metadata.["pdb_id"])
    | Error e -> 
        Assert.True(false, $"Parse failed: {e}")

[<Fact>]
let ``structureToMoleculeInstances converts all ligands`` () =
    let result = parse pdbMultipleLigands
    match result with
    | Ok structure ->
        let molecules = structureToMoleculeInstances structure
        Assert.Equal(3, molecules.Length)
    | Error e -> 
        Assert.True(false, $"Parse failed: {e}")

[<Fact>]
let ``molecule name includes PDB ID and residue info`` () =
    let result = parse simplePdb
    match result with
    | Ok structure ->
        let atp = structure.Residues.[0]
        let mol = residueToMoleculeInstance atp structure.PdbId
        
        Assert.True(mol.Name.IsSome)
        Assert.Contains("1ATP", mol.Name.Value)
        Assert.Contains("ATP", mol.Name.Value)
    | Error e -> 
        Assert.True(false, $"Parse failed: {e}")

// =============================================================================
// EDGE CASE TESTS
// =============================================================================

[<Fact>]
let ``parse handles minimal PDB line`` () =
    let result = parse minimalAtomLine
    match result with
    | Ok structure ->
        Assert.Equal(1, structure.Residues.Length)
        let lig = structure.Residues.[0]
        Assert.Equal("LIG", lig.Name)
        Assert.Equal(1.234, lig.Atoms.[0].X, 3)
        Assert.Equal(5.678, lig.Atoms.[0].Y, 3)
        Assert.Equal(9.012, lig.Atoms.[0].Z, 3)
    | Error e -> 
        Assert.True(false, $"Parse failed: {e}")

[<Fact>]
let ``parse handles complete PDB line with all fields`` () =
    let result = parse completeAtomLine
    match result with
    | Ok structure ->
        Assert.Equal(1, structure.Residues.Length)
        let atom = structure.Residues.[0].Atoms.[0]
        Assert.Equal(Some 'A', atom.AltLoc)
        Assert.Equal(Some 0.50, atom.Occupancy)
        Assert.Equal(Some 35.88, atom.TempFactor)
        Assert.Equal("C", atom.Element)
        Assert.Equal(Some "1+", atom.Charge)
    | Error e -> 
        Assert.True(false, $"Parse failed: {e}")

[<Fact>]
let ``parse returns empty residues for empty content`` () =
    let result = parse ""
    match result with
    | Ok structure ->
        Assert.Equal(0, structure.Residues.Length)
    | Error e -> 
        Assert.True(false, $"Parse failed: {e}")

[<Fact>]
let ``parse handles PDB without HEADER`` () =
    let pdbNoHeader = """HETATM    1  C   LIG A   1       1.000   2.000   3.000  1.00 10.00           C
END
"""
    let result = parse pdbNoHeader
    match result with
    | Ok structure ->
        Assert.True(structure.PdbId.IsNone)
        Assert.Equal(1, structure.Residues.Length)
    | Error e -> 
        Assert.True(false, $"Parse failed: {e}")
