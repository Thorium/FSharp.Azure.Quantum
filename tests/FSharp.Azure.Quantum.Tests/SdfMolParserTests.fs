module SdfMolParserTests

open Xunit
open FSharp.Azure.Quantum.Data
open FSharp.Azure.Quantum.Data.ChemistryDataProviders
open FSharp.Azure.Quantum.Data.ChemistryDataProviders.SdfMolParser

// =============================================================================
// TEST DATA: Sample MOL/SDF content
// =============================================================================

/// Ethanol MOL file (V2000 format)
let ethanolMol = """
Ethanol
  Program timestamp line
  Comment line
  3  2  0  0  0  0  0  0  0  0999 V2000
   -0.0178    1.4655    0.0101 C   0  0  0  0  0  0
    1.2001    0.5424   -0.0167 C   0  0  0  0  0  0
    2.4398    1.4342    0.0101 O   0  0  0  0  0  0
  1  2  1  0  0  0  0
  2  3  1  0  0  0  0
M  END
"""

/// Water MOL file
let waterMol = """
Water
  Test
  H2O molecule
  3  2  0  0  0  0  0  0  0  0999 V2000
    0.0000    0.0000    0.1173 O   0  0  0  0  0  0
    0.7572    0.0000   -0.4692 H   0  0  0  0  0  0
   -0.7572    0.0000   -0.4692 H   0  0  0  0  0  0
  1  2  1  0  0  0  0
  1  3  1  0  0  0  0
M  END
"""

/// Benzene MOL file with aromatic bonds
let benzeneMol = """
Benzene
  Test
  Aromatic ring
  6  6  0  0  0  0  0  0  0  0999 V2000
    1.2124    0.7000    0.0000 C   0  0  0  0  0  0
    1.2124   -0.7000    0.0000 C   0  0  0  0  0  0
    0.0000   -1.4000    0.0000 C   0  0  0  0  0  0
   -1.2124   -0.7000    0.0000 C   0  0  0  0  0  0
   -1.2124    0.7000    0.0000 C   0  0  0  0  0  0
    0.0000    1.4000    0.0000 C   0  0  0  0  0  0
  1  2  4  0  0  0  0
  2  3  4  0  0  0  0
  3  4  4  0  0  0  0
  4  5  4  0  0  0  0
  5  6  4  0  0  0  0
  6  1  4  0  0  0  0
M  END
"""

/// Charged molecule with M  CHG line (acetate ion)
let acetateMol = """
Acetate
  Test
  Charged molecule
  4  3  0  0  0  0  0  0  0  0999 V2000
   -0.0178    1.4655    0.0101 C   0  0  0  0  0  0
    1.2001    0.5424   -0.0167 C   0  0  0  0  0  0
    2.3000    1.2000    0.0000 O   0  0  0  0  0  0
    1.2001   -0.7000    0.0000 O   0  0  0  0  0  0
  1  2  1  0  0  0  0
  2  3  2  0  0  0  0
  2  4  1  0  0  0  0
M  CHG  1   4  -1
M  END
"""

/// SDF file with multiple molecules and data fields
let multiMoleculeSdf = """
Ethanol
  Test
  First molecule
  3  2  0  0  0  0  0  0  0  0999 V2000
   -0.0178    1.4655    0.0101 C   0  0  0  0  0  0
    1.2001    0.5424   -0.0167 C   0  0  0  0  0  0
    2.4398    1.4342    0.0101 O   0  0  0  0  0  0
  1  2  1  0  0  0  0
  2  3  1  0  0  0  0
M  END
>  <MolecularWeight>
46.07

>  <SMILES>
CCO

$$$$
Methanol
  Test
  Second molecule
  2  1  0  0  0  0  0  0  0  0999 V2000
    0.0000    0.0000    0.0000 C   0  0  0  0  0  0
    1.4000    0.0000    0.0000 O   0  0  0  0  0  0
  1  2  1  0  0  0  0
M  END
>  <MolecularWeight>
32.04

>  <SMILES>
CO

$$$$
"""

// =============================================================================
// MOL FILE PARSING TESTS
// =============================================================================

[<Fact>]
let ``parseMolFile parses ethanol correctly`` () =
    match parseMolFile ethanolMol with
    | Ok record ->
        Assert.Equal("Ethanol", record.Name)
        Assert.Equal(3, record.Atoms.Length)
        Assert.Equal(2, record.Bonds.Length)
        
        // Check first atom (Carbon)
        Assert.Equal("C", record.Atoms.[0].Symbol)
        Assert.True(abs(record.Atoms.[0].X - (-0.0178)) < 0.001)
        
        // Check oxygen
        Assert.Equal("O", record.Atoms.[2].Symbol)
    | Error e ->
        Assert.Fail($"Expected Ok but got Error: {e}")

[<Fact>]
let ``parseMolFile parses water correctly`` () =
    match parseMolFile waterMol with
    | Ok record ->
        Assert.Equal("Water", record.Name)
        Assert.Equal(3, record.Atoms.Length)
        Assert.Equal(2, record.Bonds.Length)
        
        // Check oxygen
        Assert.Equal("O", record.Atoms.[0].Symbol)
        
        // Check hydrogens
        Assert.Equal("H", record.Atoms.[1].Symbol)
        Assert.Equal("H", record.Atoms.[2].Symbol)
    | Error e ->
        Assert.Fail($"Expected Ok but got Error: {e}")

[<Fact>]
let ``parseMolFile parses benzene with aromatic bonds`` () =
    match parseMolFile benzeneMol with
    | Ok record ->
        Assert.Equal("Benzene", record.Name)
        Assert.Equal(6, record.Atoms.Length)
        Assert.Equal(6, record.Bonds.Length)
        
        // All atoms should be carbon
        for atom in record.Atoms do
            Assert.Equal("C", atom.Symbol)
        
        // All bonds should be aromatic (type 4)
        for bond in record.Bonds do
            Assert.Equal(4, bond.BondType)
    | Error e ->
        Assert.Fail($"Expected Ok but got Error: {e}")

[<Fact>]
let ``parseMolFile parses M CHG charge lines`` () =
    match parseMolFile acetateMol with
    | Ok record ->
        Assert.Equal("Acetate", record.Name)
        Assert.Equal(4, record.Atoms.Length)
        Assert.Equal(3, record.Bonds.Length)
        
        // Check charges from M  CHG line
        Assert.Equal(1, record.Charges.Length)
        let (atomIdx, charge) = record.Charges.[0]
        Assert.Equal(4, atomIdx)  // 1-indexed
        Assert.Equal(-1, charge)
    | Error e ->
        Assert.Fail($"Expected Ok but got Error: {e}")

[<Fact>]
let ``parseMolFile returns error for invalid content`` () =
    let invalidMol = "This is not a valid MOL file"
    match parseMolFile invalidMol with
    | Ok _ -> Assert.Fail("Expected Error but got Ok")
    | Error _ -> () // Expected

// =============================================================================
// SDF FILE PARSING TESTS
// =============================================================================

[<Fact>]
let ``parseSdfFile parses multiple molecules`` () =
    match parseSdfFile multiMoleculeSdf with
    | Ok records ->
        Assert.Equal(2, records.Length)
        
        // First molecule
        Assert.Equal("Ethanol", records.[0].Name)
        Assert.Equal(3, records.[0].Atoms.Length)
        
        // Second molecule
        Assert.Equal("Methanol", records.[1].Name)
        Assert.Equal(2, records.[1].Atoms.Length)
    | Error e ->
        Assert.Fail($"Expected Ok but got Error: {e}")

[<Fact>]
let ``parseSdfFile extracts associated data fields`` () =
    match parseSdfFile multiMoleculeSdf with
    | Ok records ->
        // Check ethanol properties
        Assert.True(records.[0].Properties.ContainsKey("MolecularWeight"))
        Assert.Equal("46.07", records.[0].Properties.["MolecularWeight"])
        Assert.True(records.[0].Properties.ContainsKey("SMILES"))
        Assert.Equal("CCO", records.[0].Properties.["SMILES"])
        
        // Check methanol properties
        Assert.True(records.[1].Properties.ContainsKey("MolecularWeight"))
        Assert.Equal("32.04", records.[1].Properties.["MolecularWeight"])
        Assert.True(records.[1].Properties.ContainsKey("SMILES"))
        Assert.Equal("CO", records.[1].Properties.["SMILES"])
    | Error e ->
        Assert.Fail($"Expected Ok but got Error: {e}")

// =============================================================================
// CONVERSION TO MoleculeInstance TESTS
// =============================================================================

[<Fact>]
let ``toMoleculeInstance creates correct topology`` () =
    match parseMolFile ethanolMol with
    | Ok record ->
        let instance = toMoleculeInstance record
        
        // Check atoms
        Assert.Equal(3, instance.Topology.Atoms.Length)
        Assert.Equal("C", instance.Topology.Atoms.[0])
        Assert.Equal("C", instance.Topology.Atoms.[1])
        Assert.Equal("O", instance.Topology.Atoms.[2])
        
        // Check bonds (should be 0-indexed now)
        Assert.Equal(2, instance.Topology.Bonds.Length)
        let (a1, a2, order) = instance.Topology.Bonds.[0]
        Assert.Equal(0, a1)  // 0-indexed
        Assert.Equal(1, a2)
        Assert.Equal(Some 1.0, order)
    | Error e ->
        Assert.Fail($"Expected Ok but got Error: {e}")

[<Fact>]
let ``toMoleculeInstance preserves 3D geometry`` () =
    match parseMolFile waterMol with
    | Ok record ->
        let instance = toMoleculeInstance record
        
        Assert.True(instance.Geometry.IsSome)
        let geom = instance.Geometry.Value
        
        Assert.Equal(3, geom.Coordinates.Length)
        Assert.Equal("angstrom", geom.Units)
        
        // Check oxygen position
        Assert.True(abs(geom.Coordinates.[0].Z - 0.1173) < 0.0001)
    | Error e ->
        Assert.Fail($"Expected Ok but got Error: {e}")

[<Fact>]
let ``toMoleculeInstance includes charge from M CHG`` () =
    match parseMolFile acetateMol with
    | Ok record ->
        let instance = toMoleculeInstance record
        
        // Should have -1 total charge
        Assert.Equal(Some -1, instance.Topology.Charge)
    | Error e ->
        Assert.Fail($"Expected Ok but got Error: {e}")

[<Fact>]
let ``toMoleculeInstance preserves metadata from SDF`` () =
    match parseSdfFile multiMoleculeSdf with
    | Ok records ->
        let instance = toMoleculeInstance records.[0]
        
        Assert.True(instance.Topology.Metadata.ContainsKey("SMILES"))
        Assert.Equal("CCO", instance.Topology.Metadata.["SMILES"])
    | Error e ->
        Assert.Fail($"Expected Ok but got Error: {e}")

// =============================================================================
// PROVIDER TESTS (using in-memory content)
// =============================================================================

[<Fact>]
let ``SDF molecules have valid atom symbols`` () =
    match parseSdfFile multiMoleculeSdf with
    | Ok records ->
        for record in records do
            for atom in record.Atoms do
                // All symbols should be valid elements
                Assert.True(
                    PeriodicTable.isValidSymbol atom.Symbol,
                    $"Invalid element symbol: {atom.Symbol}")
    | Error e ->
        Assert.Fail($"Expected Ok but got Error: {e}")

[<Fact>]
let ``Benzene has correct aromatic bond representation`` () =
    match parseMolFile benzeneMol with
    | Ok record ->
        let instance = toMoleculeInstance record
        
        // All bonds should have order 4.0 (aromatic)
        for (_, _, order) in instance.Topology.Bonds do
            Assert.Equal(Some 4.0, order)
    | Error e ->
        Assert.Fail($"Expected Ok but got Error: {e}")
