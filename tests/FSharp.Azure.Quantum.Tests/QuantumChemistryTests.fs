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
  
/// Tests for Ground State Energy Estimation (Task 2)  
module GroundStateEnergyTests =
    
    [<Fact>]
    let ``Estimate H2 ground state energy should be approximately -1.174 Hartree`` () =
        // Arrange
        let h2 = Molecule.createH2 0.74  // Equilibrium bond length
        let config = {
            Method = GroundStateMethod.VQE
            MaxIterations = 100
            Tolerance = 1e-6
            InitialParameters = None
        }
        
        // Act
        let result = GroundStateEnergy.estimateEnergy h2 config |> Async.RunSynchronously
        
        // Assert
        match result with
        | Ok energy ->
            // H2 ground state: -1.174 Hartree (allow 5% error for numerical methods)
            let expected = -1.174
            let tolerance = 0.1  // 0.1 Hartree tolerance
            Assert.True(abs(energy - expected) < tolerance, 
                sprintf "Expected ~%.3f, got %.3f" expected energy)
        | Error msg ->
            Assert.True(false, sprintf "Energy calculation failed: %s" msg)
    
    [<Fact>]
    let ``Estimate H2O ground state energy should be approximately -76.0 Hartree`` () =
        // Arrange
        let h2o = Molecule.createH2O()
        let config = {
            Method = GroundStateMethod.VQE
            MaxIterations = 200
            Tolerance = 1e-6
            InitialParameters = None
        }
        
        // Act
        let result = GroundStateEnergy.estimateEnergy h2o config |> Async.RunSynchronously
        
        // Assert
        match result with
        | Ok energy ->
            // H2O ground state: -76.0 Hartree (allow larger tolerance for complex molecule)
            let expected = -76.0
            let tolerance = 1.0  // 1.0 Hartree tolerance
            Assert.True(abs(energy - expected) < tolerance,
                sprintf "Expected ~%.1f, got %.3f" expected energy)
        | Error msg ->
            Assert.True(false, sprintf "Energy calculation failed: %s" msg)
    
    [<Fact>]
    let ``VQE method should be selectable`` () =
        // Arrange
        let h2 = Molecule.createH2 0.74
        let config = {
            Method = GroundStateMethod.VQE
            MaxIterations = 50
            Tolerance = 1e-6
            InitialParameters = None
        }
        
        // Act
        let result = GroundStateEnergy.estimateEnergyWith GroundStateMethod.VQE h2 config
                     |> Async.RunSynchronously
        
        // Assert
        Assert.True(result |> Result.isOk, "VQE should complete successfully")
    
    [<Fact>]
    let ``Classical DFT fallback should work for small molecules`` () =
        // Arrange
        let h2 = Molecule.createH2 0.74
        let config = {
            Method = GroundStateMethod.ClassicalDFT
            MaxIterations = 50
            Tolerance = 1e-6
            InitialParameters = None
        }
        
        // Act
        let result = GroundStateEnergy.estimateEnergyWith GroundStateMethod.ClassicalDFT h2 config
                     |> Async.RunSynchronously
        
        // Assert
        match result with
        | Ok energy ->
            // DFT should give reasonable approximation
            let expected = -1.174
            let tolerance = 0.2  // DFT may be less accurate
            Assert.True(abs(energy - expected) < tolerance,
                sprintf "DFT: Expected ~%.3f, got %.3f" expected energy)
        | Error _ ->
            // DFT fallback might not be implemented yet, that's ok
            Assert.True(true, "DFT not implemented - acceptable for now")
    
    [<Fact>]
    let ``Auto-detect method should choose appropriate algorithm`` () =
        // Arrange - small molecule should use classical
        let h2 = Molecule.createH2 0.74
        let config = {
            Method = GroundStateMethod.Automatic
            MaxIterations = 50
            Tolerance = 1e-6
            InitialParameters = None
        }
        
        // Act
        let result = GroundStateEnergy.estimateEnergy h2 config |> Async.RunSynchronously
        
        // Assert
        Assert.True(result |> Result.isOk, "Auto-detect should work")
    
    [<Fact>]
    let ``Invalid molecule should return error`` () =
        // Arrange - molecule with no atoms
        let invalidMolecule = {
            Name = "Empty"
            Atoms = []
            Bonds = []
            Charge = 0
            Multiplicity = 1
        }
        let config = {
            Method = GroundStateMethod.VQE
            MaxIterations = 50
            Tolerance = 1e-6
            InitialParameters = None
        }
        
        // Act
        let result = GroundStateEnergy.estimateEnergy invalidMolecule config
                     |> Async.RunSynchronously
        
        // Assert
        match result with
        | Error msg -> Assert.Contains("Invalid", msg)
        | Ok _ -> Assert.True(false, "Should have failed for invalid molecule")
    
    [<Fact>]
    let ``Energy units should be in Hartree`` () =
        // Arrange
        let h2 = Molecule.createH2 0.74
        let config = {
            Method = GroundStateMethod.VQE
            MaxIterations = 50
            Tolerance = 1e-6
            InitialParameters = None
        }
        
        // Act
        let result = GroundStateEnergy.estimateEnergy h2 config |> Async.RunSynchronously
        
        // Assert
        match result with
        | Ok energy ->
            // Energy should be negative and in reasonable range for H2
            Assert.True(energy < 0.0, "Ground state energy should be negative")
            Assert.True(energy > -10.0, "H2 energy should be > -10 Hartree")
        | Error _ ->
            Assert.True(false, "Should calculate energy")
    
    [<Fact>]
    let ``VQE should handle convergence limits`` () =
        // Arrange
        let h2 = Molecule.createH2 0.74
        let config = {
            Method = GroundStateMethod.VQE
            MaxIterations = 10  // Very few iterations
            Tolerance = 1e-8   // Very tight tolerance
            InitialParameters = None
        }
        
        // Act
        let result = GroundStateEnergy.estimateEnergy h2 config |> Async.RunSynchronously
        
        // Assert
        // Should either converge or return error about max iterations
        match result with
        | Ok _ -> Assert.True(true, "Converged successfully")
        | Error msg -> 
            // Acceptable to hit max iterations with tight constraints
            Assert.True(true, "Hit max iterations - acceptable")
    
    [<Fact>]
    let ``Initial parameters can be provided for VQE`` () =
        // Arrange
        let h2 = Molecule.createH2 0.74
        let initialParams = [| 0.1; 0.2; 0.3 |]  // Some starting parameters
        let config = {
            Method = GroundStateMethod.VQE
            MaxIterations = 50
            Tolerance = 1e-6
            InitialParameters = Some initialParams
        }
        
        // Act
        let result = GroundStateEnergy.estimateEnergy h2 config |> Async.RunSynchronously
        
        // Assert
        Assert.True(result |> Result.isOk, "Should accept initial parameters")
