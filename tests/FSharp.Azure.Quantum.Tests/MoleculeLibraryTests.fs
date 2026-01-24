module MoleculeLibraryTests

open Xunit
open FSharp.Azure.Quantum.Data

// =============================================================================
// BASIC LOOKUP TESTS
// =============================================================================

[<Fact>]
let ``MoleculeLibrary.get returns H2 molecule`` () =
    let h2 = MoleculeLibrary.get "H2"
    Assert.Equal("H2", h2.Name)
    Assert.Equal(2, h2.Atoms.Length)
    Assert.Equal(0, h2.Charge)
    Assert.Equal(1, h2.Multiplicity)
    Assert.Equal("diatomic", h2.Category)

[<Fact>]
let ``MoleculeLibrary.get returns H2O molecule with correct geometry`` () =
    let h2o = MoleculeLibrary.get "H2O"
    Assert.Equal("H2O", h2o.Name)
    Assert.Equal(3, h2o.Atoms.Length)
    Assert.Equal(1, h2o.Multiplicity)
    Assert.Equal("triatomic", h2o.Category)
    
    // Check oxygen is first atom
    Assert.Equal("O", h2o.Atoms.[0].Element)
    Assert.Equal("H", h2o.Atoms.[1].Element)
    Assert.Equal("H", h2o.Atoms.[2].Element)

[<Fact>]
let ``MoleculeLibrary.get is case-insensitive`` () =
    let lower = MoleculeLibrary.tryGet "h2o"
    let upper = MoleculeLibrary.tryGet "H2O"
    Assert.True(lower.IsSome)
    Assert.True(upper.IsSome)
    Assert.Equal(lower.Value.Name, upper.Value.Name)

[<Fact>]
let ``MoleculeLibrary.get throws for invalid molecule`` () =
    Assert.Throws<System.Exception>(fun () -> MoleculeLibrary.get "InvalidMolecule" |> ignore)

[<Fact>]
let ``MoleculeLibrary.tryGet returns None for invalid molecule`` () =
    let result = MoleculeLibrary.tryGet "NotARealMolecule"
    Assert.True(result.IsNone)

[<Fact>]
let ``MoleculeLibrary.exists returns true for valid molecules`` () =
    Assert.True(MoleculeLibrary.exists "H2")
    Assert.True(MoleculeLibrary.exists "benzene")
    Assert.True(MoleculeLibrary.exists "CdSe")

[<Fact>]
let ``MoleculeLibrary.exists returns false for invalid molecules`` () =
    Assert.False(MoleculeLibrary.exists "FakeMolecule")

// =============================================================================
// COLLECTION TESTS
// =============================================================================

[<Fact>]
let ``MoleculeLibrary.all returns expected count`` () =
    let molecules = MoleculeLibrary.all ()
    // We documented 62 molecules
    Assert.True(molecules.Length >= 60, $"Expected at least 60 molecules, got {molecules.Length}")

[<Fact>]
let ``MoleculeLibrary.count matches all length`` () =
    let count = MoleculeLibrary.count ()
    let all = MoleculeLibrary.all ()
    Assert.Equal(all.Length, count)

[<Fact>]
let ``MoleculeLibrary molecules have unique names (case-sensitive)`` () =
    // Note: CO2 (carbon dioxide) and Co2 (dicobalt) are distinct molecules
    // They differ only in case, so we must compare case-sensitively
    let molecules = MoleculeLibrary.all ()
    let names = molecules |> Array.map (fun m -> m.Name)
    let uniqueCount = names |> Array.distinct |> Array.length
    Assert.Equal(molecules.Length, uniqueCount)

// =============================================================================
// CATEGORY TESTS
// =============================================================================

[<Fact>]
let ``MoleculeLibrary.categories returns all categories`` () =
    let categories = MoleculeLibrary.categories ()
    Assert.True(categories.Length >= 10, $"Expected at least 10 categories, got {categories.Length}")
    Assert.Contains("diatomic", categories)
    Assert.Contains("aromatic", categories)
    Assert.Contains("quantum_dot", categories)

[<Fact>]
let ``MoleculeLibrary.byCategory returns molecules for valid category`` () =
    let diatomics = MoleculeLibrary.byCategory "diatomic"
    Assert.True(diatomics.Length >= 5, $"Expected at least 5 diatomics, got {diatomics.Length}")
    Assert.All(diatomics, fun m -> Assert.Equal("diatomic", m.Category))

[<Fact>]
let ``MoleculeLibrary.byCategory is case-insensitive`` () =
    let lower = MoleculeLibrary.byCategory "diatomic"
    let upper = MoleculeLibrary.byCategory "DIATOMIC"
    Assert.Equal(lower.Length, upper.Length)

[<Fact>]
let ``MoleculeLibrary.byCategory returns empty for invalid category`` () =
    let result = MoleculeLibrary.byCategory "not_a_real_category"
    Assert.Empty(result)

// =============================================================================
// CATEGORY-SPECIFIC ACCESSOR TESTS
// =============================================================================

[<Fact>]
let ``MoleculeLibrary.diatomics returns diatomic molecules`` () =
    let diatomics = MoleculeLibrary.diatomics ()
    Assert.True(diatomics.Length >= 5)
    Assert.All(diatomics, fun m -> 
        Assert.Equal("diatomic", m.Category)
        Assert.Equal(2, m.Atoms.Length))

[<Fact>]
let ``MoleculeLibrary.aromatics returns aromatic molecules`` () =
    let aromatics = MoleculeLibrary.aromatics ()
    Assert.True(aromatics.Length >= 2)
    Assert.All(aromatics, fun m -> Assert.Equal("aromatic", m.Category))
    // Benzene should be in the list
    Assert.Contains(aromatics, fun m -> m.Name = "benzene")

[<Fact>]
let ``MoleculeLibrary.quantumDots returns quantum dot molecules`` () =
    let qds = MoleculeLibrary.quantumDots ()
    Assert.True(qds.Length >= 5)
    Assert.All(qds, fun m -> Assert.Equal("quantum_dot", m.Category))
    // CdSe should be in the list
    Assert.Contains(qds, fun m -> m.Name = "CdSe")

[<Fact>]
let ``MoleculeLibrary.metalHydrides returns metal hydride molecules`` () =
    let mhs = MoleculeLibrary.metalHydrides ()
    Assert.True(mhs.Length >= 3)
    Assert.All(mhs, fun m -> Assert.Equal("metal_hydride", m.Category))

[<Fact>]
let ``MoleculeLibrary.catalysts returns catalyst molecules`` () =
    let catalysts = MoleculeLibrary.catalysts ()
    Assert.True(catalysts.Length >= 2)
    Assert.All(catalysts, fun m -> Assert.Equal("catalyst", m.Category))

// =============================================================================
// SEARCH TESTS
// =============================================================================

[<Fact>]
let ``MoleculeLibrary.search finds molecules by partial name`` () =
    let results = MoleculeLibrary.search "eth"
    Assert.True(results.Length >= 2)  // ethane, ethylene, ethanol, etc.

[<Fact>]
let ``MoleculeLibrary.search is case-insensitive`` () =
    let lower = MoleculeLibrary.search "benzene"
    let upper = MoleculeLibrary.search "BENZENE"
    Assert.Equal(lower.Length, upper.Length)

[<Fact>]
let ``MoleculeLibrary.search returns empty for no matches`` () =
    let results = MoleculeLibrary.search "xyznotfound123"
    Assert.Empty(results)

// =============================================================================
// BOND INFERENCE TESTS
// =============================================================================

[<Fact>]
let ``MoleculeLibrary molecules have inferred bonds`` () =
    let h2 = MoleculeLibrary.get "H2"
    Assert.True(h2.Bonds.Length >= 1)  // H-H bond

[<Fact>]
let ``MoleculeLibrary H2O has two bonds`` () =
    let h2o = MoleculeLibrary.get "H2O"
    Assert.Equal(2, h2o.Bonds.Length)  // Two O-H bonds

[<Fact>]
let ``MoleculeLibrary benzene has multiple bonds`` () =
    let benzene = MoleculeLibrary.get "benzene"
    Assert.True(benzene.Bonds.Length >= 6)  // At least 6 C-C bonds in ring

// =============================================================================
// SPECIFIC MOLECULE TESTS
// =============================================================================

[<Fact>]
let ``MoleculeLibrary Fe2 has correct multiplicity for ground state`` () =
    let fe2 = MoleculeLibrary.get "Fe2"
    Assert.Equal("Fe2", fe2.Name)
    Assert.Equal(7, fe2.Multiplicity)  // Septet ground state
    Assert.Equal("metal_dimer", fe2.Category)

[<Fact>]
let ``MoleculeLibrary LiH has correct bond length`` () =
    let lih = MoleculeLibrary.get "LiH"
    let li = lih.Atoms.[0]
    let h = lih.Atoms.[1]
    
    // Calculate distance
    let (x1, y1, z1) = li.Position
    let (x2, y2, z2) = h.Position
    let distance = sqrt ((x2-x1)**2.0 + (y2-y1)**2.0 + (z2-z1)**2.0)
    
    // LiH bond length is ~1.595 Å from NIST CCCBDB
    Assert.True(abs(distance - 1.595) < 0.01, $"Expected ~1.595 Å, got {distance}")

[<Fact>]
let ``MoleculeLibrary methane has tetrahedral structure`` () =
    let ch4 = MoleculeLibrary.get "methane"
    Assert.Equal(5, ch4.Atoms.Length)  // 1 C + 4 H
    Assert.Equal("C", ch4.Atoms.[0].Element)
    Assert.Equal(4, ch4.Bonds.Length)  // 4 C-H bonds

[<Fact>]
let ``MoleculeLibrary CdSe has correct reference`` () =
    let cdse = MoleculeLibrary.get "CdSe"
    Assert.Equal("Peng 2000", cdse.Reference)
    Assert.Equal("quantum_dot", cdse.Category)
