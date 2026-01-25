module FciDumpParserTests

open Xunit
open FSharp.Azure.Quantum.Data
open FSharp.Azure.Quantum.Data.ChemistryDataProviders
open FSharp.Azure.Quantum.Data.ChemistryDataProviders.FciDumpParser

// =============================================================================
// TEST DATA: Sample FCIDump content
// =============================================================================

/// Simple H2 FCIDump header
let h2FciDump = """&FCI NORB=2, NELEC=2, MS2=0,
ORBSYM=1,1,
ISYM=1,
/
  0.6746059439078200E+00  1  1  1  1
  0.6636548730804900E+00  2  2  2  2
  0.1813223512633300E+00  2  1  2  1
  0.6634768838289000E+00  2  2  1  1
  0.1813223512633300E+00  2  1  1  2
 -0.1252477303982200E+01  1  1  0  0
 -0.4759344611440600E+00  2  2  0  0
  0.7137758743754100E+00  0  0  0  0
"""

/// Water FCIDump header
let waterFciDump = """&FCI NORB=13, NELEC=10, MS2=0,
ORBSYM=1,1,1,1,1,1,2,2,2,3,3,3,4,
ISYM=1,
/
  0.9999999999999E+00  1  1  1  1
"""

/// FCIDump with doublet multiplicity (MS2=1)
let doubletFciDump = """&FCI NORB=5, NELEC=3, MS2=1,
ORBSYM=1,1,1,1,1,
ISYM=1,
/
"""

/// FCIDump with alternative $FCI syntax
let altSyntaxFciDump = """$FCI NORB=8, NELEC=6, MS2=0,
ORBSYM=1,1,1,1,1,1,1,1,
$
"""

/// FCIDump without MS2 (should default to singlet)
let noMs2FciDump = """&FCI NORB=4, NELEC=4,
ORBSYM=1,1,1,1,
/
"""

/// FCIDump with NIRREP specified
let withNirrepFciDump = """&FCI NORB=10, NELEC=8, MS2=0, NIRREP=4,
ORBSYM=1,1,2,2,3,3,4,4,1,1,
/
"""

// =============================================================================
// PARSER TESTS
// =============================================================================

[<Fact>]
let ``parseHeader extracts NORB and NELEC from H2`` () =
    let result = parseHeader h2FciDump
    match result with
    | Ok header ->
        Assert.Equal(2, header.NumOrbitals)
        Assert.Equal(2, header.NumElectrons)
    | Error e -> 
        Assert.True(false, $"Parse failed: {e}")

[<Fact>]
let ``parseHeader extracts MS2 correctly`` () =
    let result = parseHeader h2FciDump
    match result with
    | Ok header ->
        Assert.Equal(Some 0, header.MS2)
    | Error e -> 
        Assert.True(false, $"Parse failed: {e}")

[<Fact>]
let ``parseHeader handles water FCIDump`` () =
    let result = parseHeader waterFciDump
    match result with
    | Ok header ->
        Assert.Equal(13, header.NumOrbitals)
        Assert.Equal(10, header.NumElectrons)
        Assert.Equal(Some 0, header.MS2)
    | Error e -> 
        Assert.True(false, $"Parse failed: {e}")

[<Fact>]
let ``parseHeader handles doublet MS2=1`` () =
    let result = parseHeader doubletFciDump
    match result with
    | Ok header ->
        Assert.Equal(5, header.NumOrbitals)
        Assert.Equal(3, header.NumElectrons)
        Assert.Equal(Some 1, header.MS2)
    | Error e -> 
        Assert.True(false, $"Parse failed: {e}")

[<Fact>]
let ``parseHeader handles alternative $FCI syntax`` () =
    let result = parseHeader altSyntaxFciDump
    match result with
    | Ok header ->
        Assert.Equal(8, header.NumOrbitals)
        Assert.Equal(6, header.NumElectrons)
    | Error e -> 
        Assert.True(false, $"Parse failed: {e}")

[<Fact>]
let ``parseHeader handles missing MS2`` () =
    let result = parseHeader noMs2FciDump
    match result with
    | Ok header ->
        Assert.Equal(4, header.NumOrbitals)
        Assert.Equal(4, header.NumElectrons)
        Assert.Equal(None, header.MS2)
    | Error e -> 
        Assert.True(false, $"Parse failed: {e}")

[<Fact>]
let ``parseHeader extracts NIRREP when present`` () =
    let result = parseHeader withNirrepFciDump
    match result with
    | Ok header ->
        Assert.Equal(Some 4, header.NumIrrep)
    | Error e -> 
        Assert.True(false, $"Parse failed: {e}")

[<Fact>]
let ``parseHeader extracts ORBSYM array`` () =
    let result = parseHeader h2FciDump
    match result with
    | Ok header ->
        Assert.True(header.OrbSym.IsSome, "OrbSym should be present")
        let orbsym = header.OrbSym.Value
        Assert.Equal(2, orbsym.Length)
        Assert.Equal(1, orbsym.[0])
        Assert.Equal(1, orbsym.[1])
    | Error e -> 
        Assert.True(false, $"Parse failed: {e}")

[<Fact>]
let ``parseHeader fails on missing header`` () =
    let badContent = """
This is not a valid FCIDump file
It has no &FCI header line
"""
    let result = parseHeader badContent
    match result with
    | Ok _ -> Assert.True(false, "Should have failed")
    | Error msg -> Assert.Contains("header", msg.ToLower())

[<Fact>]
let ``parseHeader fails on missing NORB`` () =
    let badContent = """&FCI NELEC=2, MS2=0,
/
"""
    let result = parseHeader badContent
    match result with
    | Ok _ -> Assert.True(false, "Should have failed")
    | Error msg -> Assert.Contains("NORB", msg)

[<Fact>]
let ``parseHeader fails on missing NELEC`` () =
    let badContent = """&FCI NORB=2, MS2=0,
/
"""
    let result = parseHeader badContent
    match result with
    | Ok _ -> Assert.True(false, "Should have failed")
    | Error msg -> Assert.Contains("NELEC", msg)

// =============================================================================
// CONVERSION TESTS
// =============================================================================

[<Fact>]
let ``toMoleculeInstance creates instance with correct metadata`` () =
    let result = parseHeader h2FciDump
    match result with
    | Ok header ->
        let mol = toMoleculeInstance header (Some "test.fcidump")
        
        Assert.Equal(Some "test", mol.Id)
        Assert.True(mol.Name.IsSome)
        Assert.Contains("FCIDump", mol.Name.Value)
        
        // Check metadata
        Assert.Equal("fcidump", mol.Topology.Metadata.["format"])
        Assert.Equal("2", mol.Topology.Metadata.["norb"])
        Assert.Equal("2", mol.Topology.Metadata.["nelec"])
        Assert.Equal("0", mol.Topology.Metadata.["ms2"])
    | Error e -> 
        Assert.True(false, $"Parse failed: {e}")

[<Fact>]
let ``toMoleculeInstance has no geometry`` () =
    let result = parseHeader h2FciDump
    match result with
    | Ok header ->
        let mol = toMoleculeInstance header None
        Assert.True(mol.Geometry.IsNone, "FCIDump should not provide geometry")
    | Error e -> 
        Assert.True(false, $"Parse failed: {e}")

[<Fact>]
let ``toMoleculeInstance calculates multiplicity from MS2`` () =
    let result = parseHeader doubletFciDump
    match result with
    | Ok header ->
        let mol = toMoleculeInstance header None
        // MS2=1 means multiplicity = MS2 + 1 = 2 (doublet)
        Assert.Equal(Some 2, mol.Topology.Multiplicity)
    | Error e -> 
        Assert.True(false, $"Parse failed: {e}")

[<Fact>]
let ``toMoleculeInstance defaults to singlet without MS2`` () =
    let result = parseHeader noMs2FciDump
    match result with
    | Ok header ->
        let mol = toMoleculeInstance header None
        Assert.Equal(Some 1, mol.Topology.Multiplicity)
    | Error e -> 
        Assert.True(false, $"Parse failed: {e}")

[<Fact>]
let ``toMoleculeInstance uses placeholder atoms`` () =
    let result = parseHeader waterFciDump
    match result with
    | Ok header ->
        let mol = toMoleculeInstance header None
        // 10 electrons -> 5 "placeholder" atoms (one per electron pair)
        Assert.True(mol.Topology.Atoms.Length > 0)
        // All should be "X" placeholders
        Assert.True(mol.Topology.Atoms |> Array.forall (fun a -> a = "X"))
    | Error e -> 
        Assert.True(false, $"Parse failed: {e}")
