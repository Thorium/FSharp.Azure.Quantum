module MoleculeLibraryIntegrationTests

open Xunit
open FSharp.Azure.Quantum.Data
open FSharp.Azure.Quantum.QuantumChemistry

// =============================================================================
// CONVERSION FUNCTION TESTS
// =============================================================================

[<Fact>]
let ``Molecule.fromLibrary converts H2 correctly`` () =
    let libH2 = MoleculeLibrary.get "H2"
    let qcH2 = Molecule.fromLibrary libH2
    
    Assert.Equal(libH2.Name, qcH2.Name)
    Assert.Equal(libH2.Atoms.Length, qcH2.Atoms.Length)
    Assert.Equal(libH2.Bonds.Length, qcH2.Bonds.Length)
    Assert.Equal(libH2.Charge, qcH2.Charge)
    Assert.Equal(libH2.Multiplicity, qcH2.Multiplicity)

[<Fact>]
let ``Molecule.fromLibrary preserves atom positions`` () =
    let libH2O = MoleculeLibrary.get "H2O"
    let qcH2O = Molecule.fromLibrary libH2O
    
    for i in 0 .. libH2O.Atoms.Length - 1 do
        Assert.Equal(libH2O.Atoms.[i].Element, qcH2O.Atoms.[i].Element)
        Assert.Equal(libH2O.Atoms.[i].Position, qcH2O.Atoms.[i].Position)

[<Fact>]
let ``Molecule.fromLibrary preserves bond information`` () =
    let libBenzene = MoleculeLibrary.get "benzene"
    let qcBenzene = Molecule.fromLibrary libBenzene
    
    Assert.Equal(libBenzene.Bonds.Length, qcBenzene.Bonds.Length)
    for i in 0 .. libBenzene.Bonds.Length - 1 do
        Assert.Equal(libBenzene.Bonds.[i].Atom1, qcBenzene.Bonds.[i].Atom1)
        Assert.Equal(libBenzene.Bonds.[i].Atom2, qcBenzene.Bonds.[i].Atom2)
        Assert.Equal(libBenzene.Bonds.[i].BondOrder, qcBenzene.Bonds.[i].BondOrder)

[<Fact>]
let ``Molecule.tryFromLibrary returns Some for valid molecule`` () =
    let result = Molecule.tryFromLibrary "LiH"
    Assert.True(result.IsSome)
    Assert.Equal("LiH", result.Value.Name)

[<Fact>]
let ``Molecule.tryFromLibrary returns None for invalid molecule`` () =
    let result = Molecule.tryFromLibrary "NotARealMolecule"
    Assert.True(result.IsNone)

[<Fact>]
let ``Molecule.fromLibraryByName returns converted molecule`` () =
    let mol = Molecule.fromLibraryByName "methane"
    Assert.Equal("methane", mol.Name)
    Assert.Equal(5, mol.Atoms.Length)

[<Fact>]
let ``Molecule.fromLibraryByName throws for invalid name`` () =
    Assert.Throws<System.Exception>(fun () -> 
        Molecule.fromLibraryByName "InvalidMolecule" |> ignore)

// =============================================================================
// ELECTRON COUNT TESTS (uses QuantumChemistry.Molecule functions)
// =============================================================================

[<Fact>]
let ``Converted H2 has correct electron count`` () =
    let h2 = Molecule.fromLibraryByName "H2"
    let electrons = Molecule.countElectrons h2
    Assert.Equal(2, electrons)  // 2 hydrogen atoms = 2 electrons

[<Fact>]
let ``Converted H2O has correct electron count`` () =
    let h2o = Molecule.fromLibraryByName "H2O"
    let electrons = Molecule.countElectrons h2o
    Assert.Equal(10, electrons)  // O(8) + 2*H(1) = 10 electrons

[<Fact>]
let ``Converted LiH has correct electron count`` () =
    let lih = Molecule.fromLibraryByName "LiH"
    let electrons = Molecule.countElectrons lih
    Assert.Equal(4, electrons)  // Li(3) + H(1) = 4 electrons

[<Fact>]
let ``Converted benzene has correct electron count`` () =
    let benzene = Molecule.fromLibraryByName "benzene"
    let electrons = Molecule.countElectrons benzene
    Assert.Equal(42, electrons)  // 6*C(6) + 6*H(1) = 36 + 6 = 42 electrons

// =============================================================================
// MOLECULE VALIDATION TESTS
// =============================================================================

[<Fact>]
let ``Converted molecules pass validation`` () =
    let molecules = ["H2"; "H2O"; "LiH"; "benzene"; "CdSe"; "Fe2"]
    
    for name in molecules do
        let mol = Molecule.fromLibraryByName name
        let result = Molecule.validate mol
        Assert.True(Result.isOk result, $"Molecule {name} failed validation: {result}")

[<Fact>]
let ``Converted molecule bonds reference valid atom indices`` () =
    let mol = Molecule.fromLibraryByName "methane"
    
    for bond in mol.Bonds do
        Assert.True(bond.Atom1 >= 0 && bond.Atom1 < mol.Atoms.Length,
            $"Bond Atom1 index {bond.Atom1} out of range")
        Assert.True(bond.Atom2 >= 0 && bond.Atom2 < mol.Atoms.Length,
            $"Bond Atom2 index {bond.Atom2} out of range")

// =============================================================================
// ROUND-TRIP CONSISTENCY TESTS
// =============================================================================

[<Fact>]
let ``All library molecules can be converted and validated`` () =
    let allMolecules = MoleculeLibrary.all ()
    
    for libMol in allMolecules do
        let qcMol = Molecule.fromLibrary libMol
        let validationResult = Molecule.validate qcMol
        Assert.True(Result.isOk validationResult, 
            $"Molecule {libMol.Name} failed validation after conversion")

[<Fact>]
let ``Converted quantum dot molecules have expected properties`` () =
    let qds = MoleculeLibrary.quantumDots ()
    
    for libQd in qds do
        let qcQd = Molecule.fromLibrary libQd
        // All quantum dots in library are diatomic (simplified models)
        Assert.Equal(2, qcQd.Atoms.Length)
        Assert.True(qcQd.Bonds.Length >= 1)
        // Neutral charge
        Assert.Equal(0, qcQd.Charge)

[<Fact>]
let ``Converted metal dimers have correct multiplicities`` () =
    let fe2 = Molecule.fromLibraryByName "Fe2"
    Assert.Equal(7, fe2.Multiplicity)  // Septet (S=3, 6 unpaired electrons)
    
    let cu2 = Molecule.fromLibraryByName "Cu2"
    Assert.Equal(1, cu2.Multiplicity)  // Singlet (closed shell)
