namespace FSharp.Azure.Quantum.Tests

open Xunit
open FSharp.Azure.Quantum.QuantumChemistry

/// Tests for Molecule Representation (Task 1)
module MoleculeTests =
    
    [<Fact>]
    let ``Create simple H2 molecule with 2 atoms and 1 bond`` () =
        // Arrange & Act
        let h2 = {
            Name = "H2"
            Atoms = [
                { Element = "H"; Position = (0.0, 0.0, 0.0) }
                { Element = "H"; Position = (0.0, 0.0, 0.74) }  // 0.74 Angstroms apart
            ]
            Bonds = [
                { Atom1 = 0; Atom2 = 1; BondOrder = 1.0 }  // Single bond
            ]
            Charge = 0
            Multiplicity = 1
        }
        
        // Assert
        Assert.Equal("H2", h2.Name)
        Assert.Equal(2, h2.Atoms.Length)
        Assert.Equal(1, h2.Bonds.Length)
        Assert.Equal("H", h2.Atoms.[0].Element)
        Assert.Equal("H", h2.Atoms.[1].Element)
        Assert.Equal(0, h2.Bonds.[0].Atom1)
        Assert.Equal(1, h2.Bonds.[0].Atom2)
        Assert.Equal(1.0, h2.Bonds.[0].BondOrder)
    
    [<Fact>]
    let ``Create H2O molecule with 3 atoms and 2 bonds`` () =
        // Arrange & Act
        let h2o = {
            Name = "H2O"
            Atoms = [
                { Element = "O"; Position = (0.0, 0.0, 0.0) }
                { Element = "H"; Position = (0.0, 0.757, 0.587) }
                { Element = "H"; Position = (0.0, -0.757, 0.587) }
            ]
            Bonds = [
                { Atom1 = 0; Atom2 = 1; BondOrder = 1.0 }  // O-H bond
                { Atom1 = 0; Atom2 = 2; BondOrder = 1.0 }  // O-H bond
            ]
            Charge = 0
            Multiplicity = 1
        }
        
        // Assert
        Assert.Equal("H2O", h2o.Name)
        Assert.Equal(3, h2o.Atoms.Length)
        Assert.Equal(2, h2o.Bonds.Length)
        Assert.Equal("O", h2o.Atoms.[0].Element)
        Assert.Equal("H", h2o.Atoms.[1].Element)
        Assert.Equal("H", h2o.Atoms.[2].Element)
    
    [<Fact>]
    let ``Create LiH molecule`` () =
        // Arrange & Act
        let lih = {
            Name = "LiH"
            Atoms = [
                { Element = "Li"; Position = (0.0, 0.0, 0.0) }
                { Element = "H"; Position = (0.0, 0.0, 1.596) }  // 1.596 Angstroms
            ]
            Bonds = [
                { Atom1 = 0; Atom2 = 1; BondOrder = 1.0 }
            ]
            Charge = 0
            Multiplicity = 1
        }
        
        // Assert
        Assert.Equal("LiH", lih.Name)
        Assert.Equal(2, lih.Atoms.Length)
        Assert.Equal("Li", lih.Atoms.[0].Element)
        Assert.Equal("H", lih.Atoms.[1].Element)
    
    [<Fact>]
    let ``Validate bond connectivity - atoms must exist`` () =
        // Arrange
        let invalidMolecule = {
            Name = "Invalid"
            Atoms = [
                { Element = "H"; Position = (0.0, 0.0, 0.0) }
            ]
            Bonds = [
                { Atom1 = 0; Atom2 = 5; BondOrder = 1.0 }  // Atom 5 doesn't exist!
            ]
            Charge = 0
            Multiplicity = 1
        }
        
        // Act
        let result = Molecule.validate invalidMolecule
        
        // Assert
        match result with
        | Error msg -> Assert.Contains("Bond references non-existent atom", msg)
        | Ok _ -> Assert.True(false, "Should have failed validation")
    
    [<Fact>]
    let ``Validate bond connectivity - valid molecule passes`` () =
        // Arrange
        let validMolecule = {
            Name = "H2"
            Atoms = [
                { Element = "H"; Position = (0.0, 0.0, 0.0) }
                { Element = "H"; Position = (0.0, 0.0, 0.74) }
            ]
            Bonds = [
                { Atom1 = 0; Atom2 = 1; BondOrder = 1.0 }
            ]
            Charge = 0
            Multiplicity = 1
        }
        
        // Act
        let result = Molecule.validate validMolecule
        
        // Assert
        Assert.True(result |> Result.isOk)
    
    [<Fact>]
    let ``Calculate bond length between two atoms`` () =
        // Arrange
        let atom1 = { Element = "H"; Position = (0.0, 0.0, 0.0) }
        let atom2 = { Element = "H"; Position = (0.0, 0.0, 0.74) }
        
        // Act
        let bondLength = Molecule.calculateBondLength atom1 atom2
        
        // Assert
        Assert.Equal(0.74, bondLength, 6)  // 6 decimal places
    
    [<Fact>]
    let ``Calculate bond length for 3D coordinates`` () =
        // Arrange
        let atom1 = { Element = "C"; Position = (1.0, 2.0, 3.0) }
        let atom2 = { Element = "H"; Position = (4.0, 6.0, 3.0) }
        
        // Act
        let bondLength = Molecule.calculateBondLength atom1 atom2
        
        // Assert - sqrt((4-1)^2 + (6-2)^2 + (3-3)^2) = sqrt(9 + 16 + 0) = 5.0
        Assert.Equal(5.0, bondLength, 6)
    
    [<Fact>]
    let ``Count total electrons in molecule`` () =
        // Arrange - H2O has 10 electrons (8 from O, 1 from each H)
        let h2o = {
            Name = "H2O"
            Atoms = [
                { Element = "O"; Position = (0.0, 0.0, 0.0) }
                { Element = "H"; Position = (0.0, 0.757, 0.587) }
                { Element = "H"; Position = (0.0, -0.757, 0.587) }
            ]
            Bonds = []
            Charge = 0
            Multiplicity = 1
        }
        
        // Act
        let electronCount = Molecule.countElectrons h2o
        
        // Assert
        Assert.Equal(10, electronCount)
    
    [<Fact>]
    let ``Count electrons with charged molecule`` () =
        // Arrange - H3O+ (hydronium) has 11 nuclear electrons (O=8, 3×H=3)
        // With +1 charge (lost 1 electron), total = 11 - 1 = 10 electrons
        let h3o_plus = {
            Name = "H3O+"
            Atoms = [
                { Element = "O"; Position = (0.0, 0.0, 0.0) }
                { Element = "H"; Position = (0.0, 0.0, 1.0) }
                { Element = "H"; Position = (0.866, 0.0, -0.5) }
                { Element = "H"; Position = (-0.866, 0.0, -0.5) }
            ]
            Bonds = []
            Charge = 1  // +1 charge (lost 1 electron)
            Multiplicity = 1
        }
        
        // Act
        let electronCount = Molecule.countElectrons h3o_plus
        
        // Assert
        Assert.Equal(10, electronCount)  // 11 - 1 = 10
    
    [<Fact>]
    let ``Molecule module helper - createH2 at equilibrium`` () =
        // Act
        let h2 = Molecule.createH2 0.74
        
        // Assert
        Assert.Equal("H2", h2.Name)
        Assert.Equal(2, h2.Atoms.Length)
        Assert.Equal(1, h2.Bonds.Length)
        
        // Verify bond length is correct
        let bondLength = Molecule.calculateBondLength h2.Atoms.[0] h2.Atoms.[1]
        Assert.Equal(0.74, bondLength, 6)
    
    [<Fact>]
    let ``Molecule module helper - createH2O`` () =
        // Act
        let h2o = Molecule.createH2O()
        
        // Assert
        Assert.Equal("H2O", h2o.Name)
        Assert.Equal(3, h2o.Atoms.Length)
        Assert.Equal(2, h2o.Bonds.Length)
        Assert.Equal("O", h2o.Atoms.[0].Element)
    
    [<Fact>]
    let ``Molecule module helper - createLiH`` () =
        // Act
        let lih = Molecule.createLiH()
        
        // Assert
        Assert.Equal("LiH", lih.Name)
        Assert.Equal(2, lih.Atoms.Length)
        Assert.Equal("Li", lih.Atoms.[0].Element)
        Assert.Equal("H", lih.Atoms.[1].Element)
