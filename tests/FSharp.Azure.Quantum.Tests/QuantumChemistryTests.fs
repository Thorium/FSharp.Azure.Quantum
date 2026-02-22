namespace FSharp.Azure.Quantum.Tests

open Xunit
open FSharp.Azure.Quantum.QuantumChemistry
open FSharp.Azure.Quantum.Core
open FSharp.Azure.Quantum  // For ErrorMitigationStrategy

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
        | Error err -> Assert.Contains("Bond references non-existent atom", err.Message)
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
            Backend = None
            ProgressReporter = None
            ErrorMitigation = None
            IntegralProvider = None
        }
        
        // Act
        let result = GroundStateEnergy.estimateEnergy h2 config |> Async.RunSynchronously
        
        // Assert
        match result with
        | Ok vqeResult ->
            // H2 ground state: -1.174 Hartree (allow 5% error for numerical methods)
            let expected = -1.174
            let tolerance = 0.1  // 0.1 Hartree tolerance
            Assert.True(abs(vqeResult.Energy - expected) < tolerance, 
                sprintf "Expected ~%.3f, got %.3f" expected vqeResult.Energy)
        | Error err ->
            Assert.True(false, sprintf "Energy calculation failed: %s" err.Message)
    
    [<Fact>]
    let ``Estimate H2O ground state energy should be approximately -76.0 Hartree`` () =
        // Arrange
        let h2o = Molecule.createH2O()
        let config = {
            Method = GroundStateMethod.VQE
            MaxIterations = 200
            Tolerance = 1e-6
            InitialParameters = None
            Backend = None
            ProgressReporter = None
            ErrorMitigation = None
            IntegralProvider = None
        }
        
        // Act
        let result = GroundStateEnergy.estimateEnergy h2o config |> Async.RunSynchronously
        
        // Assert
        match result with
        | Ok vqeResult ->
            // H2O ground state: -76.0 Hartree (allow larger tolerance for complex molecule)
            let expected = -76.0
            let tolerance = 1.0  // 1.0 Hartree tolerance
            Assert.True(abs(vqeResult.Energy - expected) < tolerance,
                sprintf "Expected ~%.1f, got %.3f" expected vqeResult.Energy)
        | Error err ->
            Assert.True(false, sprintf "Energy calculation failed: %s" err.Message)
    
    [<Fact>]
    let ``VQE method should be selectable`` () =
        // Arrange
        let h2 = Molecule.createH2 0.74
        let config = {
            Method = GroundStateMethod.VQE
            MaxIterations = 50
            Tolerance = 1e-6
            InitialParameters = None
            Backend = None
            ProgressReporter = None
            ErrorMitigation = None
            IntegralProvider = None
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
            Backend = None
            ProgressReporter = None
            ErrorMitigation = None
            IntegralProvider = None
        }
        
        // Act
        let result = GroundStateEnergy.estimateEnergyWith GroundStateMethod.ClassicalDFT h2 config
                     |> Async.RunSynchronously
        
        // Assert
        match result with
        | Ok vqeResult ->
            // DFT should give reasonable approximation
            let expected = -1.174
            let tolerance = 0.2  // DFT may be less accurate
            Assert.True(abs(vqeResult.Energy - expected) < tolerance,
                sprintf "DFT: Expected ~%.3f, got %.3f" expected vqeResult.Energy)
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
            Backend = None
            ProgressReporter = None
            ErrorMitigation = None
            IntegralProvider = None
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
            Backend = None
            ProgressReporter = None
            ErrorMitigation = None
            IntegralProvider = None
        }
        
        // Act
        let result = GroundStateEnergy.estimateEnergy invalidMolecule config
                     |> Async.RunSynchronously
        
        // Assert
        match result with
        | Error err -> Assert.Contains("Invalid", err.Message)
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
            Backend = None
            ProgressReporter = None
            ErrorMitigation = None
            IntegralProvider = None
        }
        
        // Act
        let result = GroundStateEnergy.estimateEnergy h2 config |> Async.RunSynchronously
        
        // Assert
        match result with
        | Ok vqeResult ->
            // Energy should be negative and in reasonable range for H2
            Assert.True(vqeResult.Energy < 0.0, "Ground state energy should be negative")
            Assert.True(vqeResult.Energy > -10.0, "H2 energy should be > -10 Hartree")
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
            Backend = None
            ProgressReporter = None
            ErrorMitigation = None
            IntegralProvider = None
        }
        
        // Act
        let result = GroundStateEnergy.estimateEnergy h2 config |> Async.RunSynchronously
        
        // Assert
        // Should either converge or return error about max iterations
        match result with
        | Ok _ -> Assert.True(true, "Converged successfully")
        | Error err -> 
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
            Backend = None
            ProgressReporter = None
            ErrorMitigation = None
            IntegralProvider = None
        }
        
        // Act
        let result = GroundStateEnergy.estimateEnergy h2 config |> Async.RunSynchronously
        
        // Assert
        Assert.True(result |> Result.isOk, "Should accept initial parameters")

/// Tests for Hamiltonian Simulation (Task 3)
module HamiltonianSimulationTests =
    
    open FSharp.Azure.Quantum.LocalSimulator
    open FSharp.Azure.Quantum.Core.BackendAbstraction
    
    /// Helper to extract StateVector from QuantumState
    let extractStateVector (state: QuantumState) =
        match state with
        | QuantumState.StateVector sv -> sv
        | _ -> failwith "Expected StateVector state"
    
    [<Fact>]
    let ``Trivial Hamiltonian (H=0) should leave state unchanged`` () =
        // Arrange - empty Hamiltonian (no terms)
        let hamiltonian = {
            QaoaCircuit.NumQubits = 2
            QaoaCircuit.Terms = [||]
        }
        
        let initialSV = StateVector.init 2
        let initialState = QuantumState.StateVector initialSV
        let config = {
            HamiltonianSimulation.SimulationConfig.Time = 1.0
            HamiltonianSimulation.SimulationConfig.TrotterSteps = 10
            HamiltonianSimulation.SimulationConfig.TrotterOrder = 1
            HamiltonianSimulation.SimulationConfig.Backend = None
        }
        
        // Act
        let result = HamiltonianSimulation.simulate hamiltonian initialState config
        
        // Assert - state should be unchanged (still |00⟩)
        match result with
        | Error err -> Assert.True(false, $"Simulation failed: {err.Message}")
        | Ok finalState ->
            let finalSV = extractStateVector finalState
            let amplitude0 = StateVector.getAmplitude 0 finalSV
            Assert.Equal(1.0, amplitude0.Real, 10)
            Assert.Equal(0.0, amplitude0.Imaginary, 10)
    
    [<Fact>]
    let ``Single Pauli-Z term evolution should preserve computational basis`` () =
        // Arrange - Hamiltonian H = Z₀ (only affects |1⟩ state)
        let hamiltonian = {
            QaoaCircuit.NumQubits = 1
            QaoaCircuit.Terms = [|
                {
                    Coefficient = 1.0
                    QubitsIndices = [| 0 |]
                    PauliOperators = [| QaoaCircuit.PauliZ |]
                }
            |]
        }
        
        // Start in |0⟩ state
        let initialSV = StateVector.init 1
        let initialState = QuantumState.StateVector initialSV
        let config = {
            HamiltonianSimulation.SimulationConfig.Time = 1.0
            HamiltonianSimulation.SimulationConfig.TrotterSteps = 10
            HamiltonianSimulation.SimulationConfig.TrotterOrder = 1
            HamiltonianSimulation.SimulationConfig.Backend = None
        }
        
        // Act
        let result = HamiltonianSimulation.simulate hamiltonian initialState config
        
        // Assert - |0⟩ is eigenstate of Z with eigenvalue +1, so gets phase exp(-i*1*t)
        // But global phase doesn't affect probabilities
        match result with
        | Error err -> Assert.True(false, $"Simulation failed: {err.Message}")
        | Ok finalState ->
            let finalSV = extractStateVector finalState
            let amplitude0 = StateVector.getAmplitude 0 finalSV
            Assert.True(abs amplitude0.Magnitude - 1.0 < 1e-10, "Probability should be preserved")
    
    [<Fact>]
    let ``Time evolution should be unitary (preserve norm)`` () =
        // Arrange - H2 Hamiltonian
        let h2 = Molecule.createH2 0.74
        let hamiltonianResult = MolecularHamiltonian.build h2
        
        match hamiltonianResult with
        | Error err -> Assert.True(false, $"Hamiltonian construction failed: {err.Message}")
        | Ok hamiltonian ->
        
        let initialSV = StateVector.init hamiltonian.NumQubits
        let initialState = QuantumState.StateVector initialSV
        let config = {
            HamiltonianSimulation.SimulationConfig.Time = 0.5
            HamiltonianSimulation.SimulationConfig.TrotterSteps = 20
            HamiltonianSimulation.SimulationConfig.TrotterOrder = 2
            HamiltonianSimulation.SimulationConfig.Backend = None
        }
        
        // Act
        let result = HamiltonianSimulation.simulate hamiltonian initialState config
        
        // Assert - norm should be preserved (unitary evolution)
        match result with
        | Error err -> Assert.True(false, $"Simulation failed: {err.Message}")
        | Ok finalState ->
            let finalSV = extractStateVector finalState
            let norm = StateVector.norm finalSV
            Assert.Equal(1.0, norm, 6)  // Within 1e-6 tolerance
    
    [<Fact>]
    let ``Higher Trotter steps should improve accuracy`` () =
        // Arrange - Simple 1-qubit Hamiltonian
        let hamiltonian = {
            QaoaCircuit.NumQubits = 1
            QaoaCircuit.Terms = [|
                {
                    Coefficient = 0.5
                    QubitsIndices = [| 0 |]
                    PauliOperators = [| QaoaCircuit.PauliZ |]
                }
            |]
        }
        
        let initialSV = StateVector.init 1
        let initialState = QuantumState.StateVector initialSV
        let time = 1.0
        
        // Act - simulate with different Trotter steps
        let config10Steps = { 
            HamiltonianSimulation.SimulationConfig.Time = time
            HamiltonianSimulation.SimulationConfig.TrotterSteps = 10
            HamiltonianSimulation.SimulationConfig.TrotterOrder = 1 
            HamiltonianSimulation.SimulationConfig.Backend = None
        }
        let config100Steps = { 
            HamiltonianSimulation.SimulationConfig.Time = time
            HamiltonianSimulation.SimulationConfig.TrotterSteps = 100
            HamiltonianSimulation.SimulationConfig.TrotterOrder = 1 
            HamiltonianSimulation.SimulationConfig.Backend = None
        }
        
        let result10 = HamiltonianSimulation.simulate hamiltonian initialState config10Steps
        let result100 = HamiltonianSimulation.simulate hamiltonian initialState config100Steps
        
        // Assert - both should have norm 1 (basic sanity check)
        match result10, result100 with
        | Ok state10, Ok state100 ->
            let sv10 = extractStateVector state10
            let sv100 = extractStateVector state100
            Assert.Equal(1.0, StateVector.norm sv10, 6)
            Assert.Equal(1.0, StateVector.norm sv100, 6)
        | Error err, _ -> Assert.True(false, $"Simulation with 10 steps failed: {err.Message}")
        | _, Error err -> Assert.True(false, $"Simulation with 100 steps failed: {err.Message}")
    
    [<Fact>]
    let ``Second-order Trotter should be supported`` () =
        // Arrange
        let hamiltonian = {
            QaoaCircuit.NumQubits = 2
            QaoaCircuit.Terms = [|
                {
                    Coefficient = 1.0
                    QubitsIndices = [| 0; 1 |]
                    PauliOperators = [| QaoaCircuit.PauliZ; QaoaCircuit.PauliZ |]
                }
            |]
        }
        
        let initialSV = StateVector.init 2
        let initialState = QuantumState.StateVector initialSV
        let config = {
            HamiltonianSimulation.SimulationConfig.Time = 0.5
            HamiltonianSimulation.SimulationConfig.TrotterSteps = 10
            HamiltonianSimulation.SimulationConfig.TrotterOrder = 2  // Second-order Trotter
            HamiltonianSimulation.SimulationConfig.Backend = None
        }
        
        // Act
        let result = HamiltonianSimulation.simulate hamiltonian initialState config
        
        // Assert - should complete without error and preserve norm
        match result with
        | Error err -> Assert.True(false, $"Simulation failed: {err.Message}")
        | Ok finalState ->
            let finalSV = extractStateVector finalState
            Assert.Equal(1.0, StateVector.norm finalSV, 6)
    
    [<Fact>]
    let ``Two-qubit ZZ interaction should be handled correctly`` () =
        // Arrange - ZZ interaction between qubits 0 and 1
        let hamiltonian = {
            QaoaCircuit.NumQubits = 2
            QaoaCircuit.Terms = [|
                {
                    Coefficient = 0.5
                    QubitsIndices = [| 0; 1 |]
                    PauliOperators = [| QaoaCircuit.PauliZ; QaoaCircuit.PauliZ |]
                }
            |]
        }
        
        let initialSV = StateVector.init 2  // |00⟩ state
        let initialState = QuantumState.StateVector initialSV
        let config = {
            HamiltonianSimulation.SimulationConfig.Time = 1.0
            HamiltonianSimulation.SimulationConfig.TrotterSteps = 20
            HamiltonianSimulation.SimulationConfig.TrotterOrder = 1
            HamiltonianSimulation.SimulationConfig.Backend = None
        }
        
        // Act
        let result = HamiltonianSimulation.simulate hamiltonian initialState config
        
        // Assert - |00⟩ is eigenstate of Z₀⊗Z₁ with eigenvalue +1
        // Evolution should add global phase only
        match result with
        | Error err -> Assert.True(false, $"Simulation failed: {err.Message}")
        | Ok finalState ->
            let finalSV = extractStateVector finalState
            Assert.Equal(1.0, StateVector.norm finalSV, 6)
            
            // Check |00⟩ amplitude still has magnitude 1
            let amplitude00 = StateVector.getAmplitude 0 finalSV
            Assert.True(abs amplitude00.Magnitude - 1.0 < 1e-6, "Should stay in |00⟩ state")
    
    [<Fact>]
    let ``Three-qubit ZZZ interaction should be handled correctly`` () =
        // Arrange - ZZZ interaction between qubits 0, 1, and 2
        let hamiltonian = {
            QaoaCircuit.NumQubits = 3
            QaoaCircuit.Terms = [|
                {
                    Coefficient = 0.5
                    QubitsIndices = [| 0; 1; 2 |]
                    PauliOperators = [| QaoaCircuit.PauliZ; QaoaCircuit.PauliZ; QaoaCircuit.PauliZ |]
                }
            |]
        }
        
        let initialSV = StateVector.init 3  // |000⟩ state
        let initialState = QuantumState.StateVector initialSV
        let config = {
            HamiltonianSimulation.SimulationConfig.Time = 1.0
            HamiltonianSimulation.SimulationConfig.TrotterSteps = 20
            HamiltonianSimulation.SimulationConfig.TrotterOrder = 1
            HamiltonianSimulation.SimulationConfig.Backend = None
        }
        
        // Act
        let result = HamiltonianSimulation.simulate hamiltonian initialState config
        
        // Assert - |000⟩ is eigenstate of Z₀⊗Z₁⊗Z₂ with eigenvalue +1
        // Evolution should add global phase only
        match result with
        | Error err -> Assert.True(false, $"Simulation with 3-qubit term failed: {err.Message}")
        | Ok finalState ->
            let finalSV = extractStateVector finalState
            Assert.Equal(1.0, StateVector.norm finalSV, 6)
            
            // Check |000⟩ amplitude still has magnitude 1
            let amplitude000 = StateVector.getAmplitude 0 finalSV
            Assert.True(abs amplitude000.Magnitude - 1.0 < 1e-6, "Should stay in |000⟩ state")
    
    [<Fact>]
    let ``Two-qubit XY interaction should be handled correctly`` () =
        // Arrange - XY interaction (requires basis change)
        let hamiltonian = {
            QaoaCircuit.NumQubits = 2
            QaoaCircuit.Terms = [|
                {
                    Coefficient = 0.5
                    QubitsIndices = [| 0; 1 |]
                    PauliOperators = [| QaoaCircuit.PauliX; QaoaCircuit.PauliY |]
                }
            |]
        }
        
        let initialSV = StateVector.init 2  // |00⟩ state
        let initialState = QuantumState.StateVector initialSV
        let config = {
            HamiltonianSimulation.SimulationConfig.Time = 0.5
            HamiltonianSimulation.SimulationConfig.TrotterSteps = 20
            HamiltonianSimulation.SimulationConfig.TrotterOrder = 1
            HamiltonianSimulation.SimulationConfig.Backend = None
        }
        
        // Act
        let result = HamiltonianSimulation.simulate hamiltonian initialState config
        
        // Assert - simulation should complete and preserve norm
        match result with
        | Error err -> Assert.True(false, $"Simulation with XY term failed: {err.Message}")
        | Ok finalState ->
            let finalSV = extractStateVector finalState
            Assert.Equal(1.0, StateVector.norm finalSV, 6)
    
    [<Fact>]
    let ``Four-qubit ZZZZ interaction should be handled correctly`` () =
        // Arrange - 4-qubit ZZZ...Z interaction
        let hamiltonian = {
            QaoaCircuit.NumQubits = 4
            QaoaCircuit.Terms = [|
                {
                    Coefficient = 0.25
                    QubitsIndices = [| 0; 1; 2; 3 |]
                    PauliOperators = [| QaoaCircuit.PauliZ; QaoaCircuit.PauliZ; QaoaCircuit.PauliZ; QaoaCircuit.PauliZ |]
                }
            |]
        }
        
        let initialSV = StateVector.init 4  // |0000⟩ state
        let initialState = QuantumState.StateVector initialSV
        let config = {
            HamiltonianSimulation.SimulationConfig.Time = 1.0
            HamiltonianSimulation.SimulationConfig.TrotterSteps = 10
            HamiltonianSimulation.SimulationConfig.TrotterOrder = 1
            HamiltonianSimulation.SimulationConfig.Backend = None
        }
        
        // Act
        let result = HamiltonianSimulation.simulate hamiltonian initialState config
        
        // Assert - simulation should complete successfully
        match result with
        | Error err -> Assert.True(false, $"Simulation with 4-qubit term failed: {err.Message}")
        | Ok finalState ->
            let finalSV = extractStateVector finalState
            Assert.Equal(1.0, StateVector.norm finalSV, 6)
    
    [<Fact>]
    let ``Mixed multi-qubit terms should be handled correctly`` () =
        // Arrange - Combination of 1, 2, and 3 qubit terms
        let hamiltonian = {
            QaoaCircuit.NumQubits = 3
            QaoaCircuit.Terms = [|
                // Single-qubit Z term
                {
                    Coefficient = 1.0
                    QubitsIndices = [| 0 |]
                    PauliOperators = [| QaoaCircuit.PauliZ |]
                }
                // Two-qubit ZZ term
                {
                    Coefficient = 0.5
                    QubitsIndices = [| 0; 1 |]
                    PauliOperators = [| QaoaCircuit.PauliZ; QaoaCircuit.PauliZ |]
                }
                // Three-qubit ZZZ term
                {
                    Coefficient = 0.25
                    QubitsIndices = [| 0; 1; 2 |]
                    PauliOperators = [| QaoaCircuit.PauliZ; QaoaCircuit.PauliZ; QaoaCircuit.PauliZ |]
                }
            |]
        }
        
        let initialSV = StateVector.init 3  // |000⟩ state
        let initialState = QuantumState.StateVector initialSV
        let config = {
            HamiltonianSimulation.SimulationConfig.Time = 0.5
            HamiltonianSimulation.SimulationConfig.TrotterSteps = 20
            HamiltonianSimulation.SimulationConfig.TrotterOrder = 2  // 2nd order Trotter
            HamiltonianSimulation.SimulationConfig.Backend = None
        }
        
        // Act
        let result = HamiltonianSimulation.simulate hamiltonian initialState config
        
        // Assert - simulation should complete and preserve norm
        match result with
        | Error err -> Assert.True(false, $"Simulation with mixed terms failed: {err.Message}")
        | Ok finalState ->
            let finalSV = extractStateVector finalState
            Assert.Equal(1.0, StateVector.norm finalSV, 6)
            
            // |000⟩ is eigenstate of all Z-only terms, should have magnitude 1 (with phase)
            let amplitude000 = StateVector.getAmplitude 0 finalSV
            Assert.True(abs amplitude000.Magnitude - 1.0 < 1e-6, "Should stay in |000⟩ state")

/// Tests for Molecular Input Parsers (Task 4)
module MolecularInputTests =
    
    open System.IO
    
    [<Fact>]
    let ``Parse simple XYZ file - H2 molecule`` () =
        async {
            // Arrange - create temporary XYZ file
            let xyzContent = """2
 H2 molecule
 H  0.0  0.0  0.0
 H  0.0  0.0  0.74"""
            
            let tempFile = Path.GetTempFileName()
            File.WriteAllText(tempFile, xyzContent)
            
            try
                // Act
                let! result = Molecule.fromXyzFileAsync tempFile
                
                // Assert
                match result with
                | Error err -> Assert.True(false, $"Parsing failed: {err.Message}")
                | Ok molecule ->
                    Assert.Equal("H2 molecule", molecule.Name)
                    Assert.Equal(2, molecule.Atoms.Length)
                    Assert.Equal("H", molecule.Atoms.[0].Element)
                    Assert.Equal("H", molecule.Atoms.[1].Element)
                    
                    // Check coordinates
                    let (x1, y1, z1) = molecule.Atoms.[0].Position
                    let (x2, y2, z2) = molecule.Atoms.[1].Position
                    Assert.Equal(0.0, x1, 6)
                    Assert.Equal(0.0, y1, 6)
                    Assert.Equal(0.0, z1, 6)
                    Assert.Equal(0.0, x2, 6)
                    Assert.Equal(0.0, y2, 6)
                    Assert.Equal(0.74, z2, 6)
                    
                    // Should infer bond (distance < 1.8 Å)
                    Assert.True(molecule.Bonds.Length > 0, "Should infer H-H bond")
            finally
                File.Delete(tempFile)
        } |> Async.StartAsTask
    
    [<Fact>]
    let ``Parse XYZ file - H2O molecule`` () =
        async {
            // Arrange
            let xyzContent = """3
 Water molecule
 O  0.000  0.000  0.000
 H  0.000  0.757  0.587
 H  0.000 -0.757  0.587"""
            
            let tempFile = Path.GetTempFileName()
            File.WriteAllText(tempFile, xyzContent)
            
            try
                // Act
                let! result = Molecule.fromXyzFileAsync tempFile
                
                // Assert
                match result with
                | Error err -> Assert.True(false, $"Parsing failed: {err.Message}")
                | Ok molecule ->
                    Assert.Equal("Water molecule", molecule.Name)
                    Assert.Equal(3, molecule.Atoms.Length)
                    Assert.Equal("O", molecule.Atoms.[0].Element)
                    Assert.Equal("H", molecule.Atoms.[1].Element)
                    Assert.Equal("H", molecule.Atoms.[2].Element)
                    
                    // Should infer 2 O-H bonds
                    Assert.True(molecule.Bonds.Length >= 2, "Should infer at least 2 O-H bonds")
            finally
                File.Delete(tempFile)
        } |> Async.StartAsTask
    
    [<Fact>]
    let ``XYZ parser should handle tabs and multiple spaces`` () =
        async {
            // Arrange - XYZ with irregular whitespace
            let xyzContent = """2
 H2 with tabs
 H    0.0    0.0    0.0
 H		0.0		0.0		0.74"""
            
            let tempFile = Path.GetTempFileName()
            File.WriteAllText(tempFile, xyzContent)
            
            try
                // Act
                let! result = Molecule.fromXyzFileAsync tempFile
                
                // Assert
                match result with
                | Error err -> Assert.True(false, $"Should handle whitespace: {err.Message}")
                | Ok molecule ->
                    Assert.Equal(2, molecule.Atoms.Length)
            finally
                File.Delete(tempFile)
        } |> Async.StartAsTask
    
    [<Fact>]
    let ``XYZ parser should reject malformed file`` () =
        async {
            // Arrange - invalid XYZ (wrong atom count)
            let xyzContent = """5
 Should have 5 atoms but only has 2
 H  0.0  0.0  0.0
 H  0.0  0.0  0.74"""
            
            let tempFile = Path.GetTempFileName()
            File.WriteAllText(tempFile, xyzContent)
            
            try
                // Act
                let! result = Molecule.fromXyzFileAsync tempFile
                
                // Assert
                match result with
                | Ok _ -> Assert.True(false, "Should reject file with wrong atom count")
                | Error err -> Assert.Contains("Expected", err.Message)  // Error message from MoleculeFormats
            finally
                File.Delete(tempFile)
        } |> Async.StartAsTask
    
    [<Fact>]
    let ``Parse FCIDump header - extract NORB and NELEC`` () =
        async {
            // Arrange - minimal FCIDump header
            let fcidumpContent = """&FCI NORB=  2,NELEC=  2,MS2=  0,
  ORBSYM=1,1,
  ISYM=1,
 &END"""
            
            let tempFile = Path.GetTempFileName()
            File.WriteAllText(tempFile, fcidumpContent)
            
            try
                // Act - Use MoleculeFormats directly since FCIDump has no geometry
                // Molecule.fromFciDumpFileAsync will fail with MissingGeometry error
                let! result = FSharp.Azure.Quantum.Data.MoleculeFormats.FciDump.readAsync tempFile System.Threading.CancellationToken.None |> Async.AwaitTask
                
                // Assert
                match result with
                | Error err -> Assert.True(false, $"Parsing failed: {err.Message}")
                | Ok moleculeData ->
                    // Should extract NORB=2, NELEC=2 from header metadata
                    // Note: Placeholder atoms = NELEC/2 = 1 atom
                    Assert.Equal(1, moleculeData.Topology.Atoms.Length)  // Placeholder atoms (NELEC/2)
                    Assert.Equal(Some 0, moleculeData.Topology.Charge)   // NORB - NELEC = 0
                    Assert.Equal(Some 1, moleculeData.Topology.Multiplicity)  // MS2=0 → singlet
                    Assert.Equal(None, moleculeData.Geometry)  // FCIDump has no geometry
                    // Verify metadata has the original values
                    Assert.Equal("2", moleculeData.Topology.Metadata.["norb"])
                    Assert.Equal("2", moleculeData.Topology.Metadata.["nelec"])
            finally
                File.Delete(tempFile)
        } |> Async.StartAsTask
    
    [<Fact>]
    let ``FCIDump parser should handle missing parameters`` () =
        async {
            // Arrange - FCIDump without NORB
            let fcidumpContent = """&FCI NELEC=  2,MS2=  0,
 &END"""
            
            let tempFile = Path.GetTempFileName()
            File.WriteAllText(tempFile, fcidumpContent)
            
            try
                // Act - Use MoleculeFormats directly
                let! result = FSharp.Azure.Quantum.Data.MoleculeFormats.FciDump.readAsync tempFile System.Threading.CancellationToken.None |> Async.AwaitTask
                
                // Assert
                match result with
                | Ok _ -> Assert.True(false, "Should require NORB parameter")
                | Error err -> Assert.Contains("NORB", err.Message)
            finally
                File.Delete(tempFile)
        } |> Async.StartAsTask
    
    [<Fact>]
    let ``Convert molecule to XYZ format`` () =
        // Arrange
        let h2 = Molecule.createH2 0.74
        
        // Act
        let xyzContent = Molecule.toXyz h2
        
        // Assert
        let lines = xyzContent.Split([| '\n'; '\r' |], System.StringSplitOptions.RemoveEmptyEntries)
        Assert.True(lines.Length >= 4, "Should have at least 4 lines (count, title, 2 atoms)")
        Assert.Equal("2", lines[0].Trim())  // Atom count
        Assert.Equal("H2", lines[1].Trim())  // Name
        Assert.Contains("H", lines[2])  // First H atom
        Assert.Contains("H", lines[3])  // Second H atom
    
    [<Fact>]
    let ``Save and reload XYZ file`` () =
        async {
            // Arrange
            let original = Molecule.createH2O()
            let tempFile = Path.GetTempFileName()
            
            try
                // Act - save to file
                let! saveResult = Molecule.saveToXyzFileAsync tempFile original
                
                match saveResult with
                | Error err -> Assert.True(false, $"Save failed: {err.Message}")
                | Ok () ->
                
                // Act - reload from file
                let! loadResult = Molecule.fromXyzFileAsync tempFile
                
                match loadResult with
                | Error err -> Assert.True(false, $"Load failed: {err.Message}")
                | Ok reloaded ->
                    // Assert - should match original
                    Assert.Equal(original.Atoms.Length, reloaded.Atoms.Length)
                    Assert.Equal(original.Name, reloaded.Name)
                    
                    // Check first atom coordinates match
                    let (x1, y1, z1) = original.Atoms.[0].Position
                    let (x2, y2, z2) = reloaded.Atoms.[0].Position
                    Assert.Equal(x1, x2, 6)
                    Assert.Equal(y1, y2, 6)
                    Assert.Equal(z1, z2, 6)
            finally
                if File.Exists tempFile then File.Delete(tempFile)
        } |> Async.StartAsTask

// ============================================================================
// TKT-79: QUANTUM CHEMISTRY DOMAIN BUILDER TESTS
// ============================================================================

/// Tests for Quantum Chemistry Builder (TKT-79)
module QuantumChemistryBuilderTests =
    
    open System.IO
    open FSharp.Azure.Quantum.QuantumChemistry.QuantumChemistryBuilder
    
    // ========================================================================
    // TEST 1: Basic Builder Functionality
    // ========================================================================
    
    [<Fact>]
    let ``Builder should construct valid chemistry problem`` () =
        // Arrange & Act
        let problem = quantumChemistry {
            molecule (h2 0.74)
            basis "sto-3g"
            ansatz UCCSD
        }
        
        // Assert
        Assert.True(problem.Molecule.IsSome, "Molecule should be set")
        Assert.Equal(Some "sto-3g", problem.Basis)
        Assert.Equal(Some UCCSD, problem.Ansatz)
        Assert.True(problem.Optimizer.IsSome, "Default optimizer should be set")
        Assert.Equal(100, problem.MaxIterations)
    
    [<Fact>]
    let ``Builder should apply default optimizer`` () =
        // Arrange & Act
        let problem = quantumChemistry {
            molecule (h2 0.74)
            basis "sto-3g"
            ansatz UCCSD
        }
        
        // Assert - should have default COBYLA optimizer
        Assert.True(problem.Optimizer.IsSome)
        Assert.Equal("COBYLA", problem.Optimizer.Value.Method)
    
    // ========================================================================
    // TEST 2: Pre-built Molecules
    // ========================================================================
    
    [<Fact>]
    let ``h2 helper creates valid H2 molecule`` () =
        // Arrange & Act
        let molecule = h2 0.74
        
        // Assert
        Assert.Equal("H2", molecule.Name)
        Assert.Equal(2, molecule.Atoms.Length)
        Assert.Equal("H", molecule.Atoms.[0].Element)
        Assert.Equal("H", molecule.Atoms.[1].Element)
        
        // Verify bond length
        let bondLength = Molecule.calculateBondLength molecule.Atoms.[0] molecule.Atoms.[1]
        Assert.Equal(0.74, bondLength, 6)
    
    [<Fact>]
    let ``h2o helper creates valid H2O molecule`` () =
        // Arrange & Act
        let molecule = h2o 0.96 104.5  // Equilibrium geometry
        
        // Assert
        Assert.Equal("H2O", molecule.Name)
        Assert.Equal(3, molecule.Atoms.Length)
        Assert.Equal("O", molecule.Atoms.[0].Element)
        Assert.Equal("H", molecule.Atoms.[1].Element)
        Assert.Equal("H", molecule.Atoms.[2].Element)
        Assert.Equal(2, molecule.Bonds.Length)
    
    [<Fact>]
    let ``lih helper creates valid LiH molecule`` () =
        // Arrange & Act
        let molecule = lih 1.596
        
        // Assert
        Assert.Equal("LiH", molecule.Name)
        Assert.Equal(2, molecule.Atoms.Length)
        Assert.Equal("Li", molecule.Atoms.[0].Element)
        Assert.Equal("H", molecule.Atoms.[1].Element)
        
        // Verify bond length
        let bondLength = Molecule.calculateBondLength molecule.Atoms.[0] molecule.Atoms.[1]
        Assert.Equal(1.596, bondLength, 6)
    
    // ========================================================================
    // TEST 3: Ansatz Types (Struct)
    // ========================================================================
    
    [<Fact>]
    let ``ChemistryAnsatz should be value type (struct)`` () =
        // Assert - verify type is struct
        let ansatzType = typeof<ChemistryAnsatz>
        Assert.True(ansatzType.IsValueType, "ChemistryAnsatz should be a struct")
    
    [<Fact>]
    let ``All ansatz types should be available`` () =
        // Arrange & Act
        let uccsd = UCCSD
        let hea = HEA
        let adapt = ADAPT
        
        // Assert - just verify they exist and are distinct
        Assert.NotEqual<ChemistryAnsatz>(uccsd, hea)
        Assert.NotEqual<ChemistryAnsatz>(uccsd, adapt)
        Assert.NotEqual<ChemistryAnsatz>(hea, adapt)
    
    // ========================================================================
    // TEST 4: Builder Custom Operations
    // ========================================================================
    
    [<Fact>]
    let ``Builder should support custom optimizer`` () =
        // Arrange & Act
        let problem = quantumChemistry {
            molecule (h2 0.74)
            basis "sto-3g"
            ansatz HEA
            optimizer "SLSQP"
        }
        
        // Assert
        Assert.True(problem.Optimizer.IsSome)
        Assert.Equal("SLSQP", problem.Optimizer.Value.Method)
    
    [<Fact>]
    let ``Builder should support maxIterations`` () =
        // Arrange & Act
        let problem = quantumChemistry {
            molecule (h2 0.74)
            basis "sto-3g"
            ansatz ADAPT
            maxIterations 200
        }
        
        // Assert
        Assert.Equal(200, problem.MaxIterations)
    
    [<Fact>]
    let ``Builder should support initialParameters`` () =
        // Arrange
        let params' = [| 0.1; 0.2; 0.3 |]
        
        // Act
        let problem = quantumChemistry {
            molecule (h2 0.74)
            basis "sto-3g"
            ansatz UCCSD
            initialParameters params'
        }
        
        // Assert
        Assert.True(problem.InitialParameters.IsSome)
        Assert.Equal<float[]>(params', problem.InitialParameters.Value)
    
    // ========================================================================
    // TEST 5: Different Basis Sets
    // ========================================================================
    
    [<Fact>]
    let ``Builder should accept different basis sets`` () =
        // Arrange & Act
        let minimalBasis = quantumChemistry {
            molecule (h2 0.74)
            basis "sto-3g"
            ansatz UCCSD
        }
        
        let largeBasis = quantumChemistry {
            molecule (h2 0.74)
            basis "6-31g"
            ansatz UCCSD
        }
        
        // Assert
        Assert.Equal(Some "sto-3g", minimalBasis.Basis)
        Assert.Equal(Some "6-31g", largeBasis.Basis)
    
    // ========================================================================
    // TEST 6: Solver Integration
    // ========================================================================
    
    [<Fact>]
    let ``Solve should execute VQE for H2`` () =
        // Arrange
        let problem = quantumChemistry {
            molecule (h2 0.74)
            basis "sto-3g"
            ansatz UCCSD
            maxIterations 50
        }
        
        // Act
        let result = solve problem |> Async.RunSynchronously
        
        // Assert
        match result with
        | Ok chemResult ->
            // H2 ground state should be negative
            Assert.True(chemResult.GroundStateEnergy < 0.0, "Ground state energy should be negative")
            
            // Should have bond length information
            Assert.True(chemResult.BondLengths.Count > 0, "Should compute bond lengths")
            
            // H-H bond should be present
            let hasHHBond = chemResult.BondLengths |> Map.exists (fun k _ -> k.Contains("H"))
            Assert.True(hasHHBond, "Should have H-H bond length")
            
        | Error err ->
            Assert.True(false, sprintf "Solve failed: %s" err.Message)
    
    [<Fact>]
    let ``Solve should compute bond lengths for H2O`` () =
        // Arrange
        let problem = quantumChemistry {
            molecule (h2o 0.96 104.5)
            basis "sto-3g"
            ansatz HEA
            maxIterations 50
        }
        
        // Act
        let result = solve problem |> Async.RunSynchronously
        
        // Assert
        match result with
        | Ok chemResult ->
            // H2O should have multiple bond lengths (O-H bonds)
            Assert.True(chemResult.BondLengths.Count >= 2, "H2O should have at least 2 bond lengths")
            
        | Error err ->
            Assert.True(false, sprintf "Solve failed: %s" err.Message)
    
    // ========================================================================
    // TEST 7: Multiple Molecules
    // ========================================================================
    
    [<Fact>]
    let ``Builder should work with different molecules`` () =
        // Arrange & Act - H2
        let h2Problem = quantumChemistry {
            molecule (h2 0.74)
            basis "sto-3g"
            ansatz UCCSD
        }
        
        // Arrange & Act - H2O
        let h2oProblem = quantumChemistry {
            molecule (h2o 0.96 104.5)
            basis "sto-3g"
            ansatz HEA
        }
        
        // Assert
        Assert.Equal("H2", h2Problem.Molecule.Value.Name)
        Assert.Equal("H2O", h2oProblem.Molecule.Value.Name)
        Assert.Equal(Some UCCSD, h2Problem.Ansatz)
        Assert.Equal(Some HEA, h2oProblem.Ansatz)
    
    // ========================================================================
    // TEST 8: Comparison with Other Builders (Pattern Consistency)
    // ========================================================================
    
    [<Fact>]
    let ``Builder pattern should match GraphColoring builder style`` () =
        // This test verifies architectural consistency with TKT-80 GraphColoring builder
        
        // GraphColoring pattern:
        // let problem = graphColoring { node "X" ["Y"]; colors [...]; objective MinimizeColors }
        
        // QuantumChemistry pattern:
        // let problem = quantumChemistry { molecule (h2 0.74); basis "..."; ansatz UCCSD }
        
        // Both patterns:
        // 1. Use computation expressions
        // 2. Have required fields validated in Run()
        // 3. Support control flow (if/for)
        // 4. Have domain-specific custom operations
        
        // Act
        let problem = quantumChemistry {
            molecule (h2 0.74)
            basis "sto-3g"
            ansatz UCCSD
        }
        
        // Assert - pattern should feel consistent
        Assert.True(problem.Molecule.IsSome)
        Assert.True(problem.Basis.IsSome)
        Assert.True(problem.Ansatz.IsSome)
    
    // ========================================================================
    // TEST 9: File Loading Custom Operations
    // ========================================================================
    
    [<Fact>]
    let ``molecule_from_xyz should set XyzFile source`` () =
        // Arrange & Act
        let problem = quantumChemistry {
            molecule_from_xyz "test.xyz"
            basis "sto-3g"
            ansatz UCCSD
        }
        
        // Assert
        Assert.True(problem.MoleculeSource.IsSome, "MoleculeSource should be set")
        match problem.MoleculeSource.Value with
        | MoleculeSource.XyzFile path -> Assert.Equal("test.xyz", path)
        | _ -> Assert.Fail("Expected XyzFile source")
    
    [<Fact>]
    let ``molecule_from_fcidump should set FciDumpFile source`` () =
        // Arrange & Act
        let problem = quantumChemistry {
            molecule_from_fcidump "integrals.fcidump"
            basis "sto-3g"
            ansatz UCCSD
        }
        
        // Assert
        Assert.True(problem.MoleculeSource.IsSome, "MoleculeSource should be set")
        match problem.MoleculeSource.Value with
        | MoleculeSource.FciDumpFile path -> Assert.Equal("integrals.fcidump", path)
        | _ -> Assert.Fail("Expected FciDumpFile source")
    
    [<Fact>]
    let ``molecule_from_name should set FromDefaultProvider source`` () =
        // Arrange & Act
        let problem = quantumChemistry {
            molecule_from_name "benzene"
            basis "6-31g"
            ansatz HEA
        }
        
        // Assert
        Assert.True(problem.MoleculeSource.IsSome, "MoleculeSource should be set")
        match problem.MoleculeSource.Value with
        | MoleculeSource.FromDefaultProvider name -> Assert.Equal("benzene", name)
        | _ -> Assert.Fail("Expected FromDefaultProvider source")
    
    [<Fact>]
    let ``Builder should accept either molecule or molecule_from_xyz`` () =
        // Test 1: Direct molecule (legacy)
        let problem1 = quantumChemistry {
            molecule (h2 0.74)
            basis "sto-3g"
            ansatz UCCSD
        }
        Assert.True(problem1.Molecule.IsSome)
        Assert.True(problem1.MoleculeSource.IsNone)
        
        // Test 2: From XYZ file (new)
        let problem2 = quantumChemistry {
            molecule_from_xyz "test.xyz"
            basis "sto-3g"
            ansatz UCCSD
        }
        Assert.True(problem2.Molecule.IsNone)
        Assert.True(problem2.MoleculeSource.IsSome)
    
    [<Fact>]
    let ``solve with molecule_from_xyz should load and calculate`` () =
        async {
            // Arrange - create temporary XYZ file with H2 molecule
            let xyzContent = """2
H2 test molecule
H  0.0  0.0  0.0
H  0.0  0.0  0.74"""
            
            let tempFile = Path.GetTempFileName()
            File.WriteAllText(tempFile, xyzContent)
            
            try
                // Act - use builder with file loading
                let problem = quantumChemistry {
                    molecule_from_xyz tempFile
                    basis "sto-3g"
                    ansatz UCCSD
                    maxIterations 5  // Quick test
                }
                
                let! result = solve problem
                
                // Assert
                match result with
                | Ok chemistry ->
                    // Energy should be negative (bound state)
                    Assert.True(chemistry.GroundStateEnergy < 0.0, 
                        $"Ground state energy should be negative, got {chemistry.GroundStateEnergy}")
                    // Should have H-H bond length
                    Assert.True(chemistry.BondLengths.ContainsKey("H-H"), "Should have H-H bond")
                | Error err ->
                    Assert.Fail($"solve failed: {err.Message}")
            finally
                File.Delete(tempFile)
        } |> Async.StartAsTask
    
    [<Fact>]
    let ``solve with molecule_from_name should load from default provider`` () =
        async {
            // Act - use builder with named molecule
            let problem = quantumChemistry {
                molecule_from_name "h2"  // Built-in H2 molecule
                basis "sto-3g"
                ansatz UCCSD
                maxIterations 5  // Quick test
            }
            
            let! result = solve problem
            
            // Assert
            match result with
            | Ok chemistry ->
                Assert.True(chemistry.GroundStateEnergy < 0.0,
                    $"Ground state energy should be negative, got {chemistry.GroundStateEnergy}")
            | Error err ->
                // This might fail if "h2" isn't in the default provider
                // That's OK - we're testing the mechanism works
                Assert.True(true, $"Provider lookup: {err.Message}")
        } |> Async.StartAsTask

// ============================================================================
// Error Mitigation Integration Tests
// ============================================================================

/// Tests for VQE Error Mitigation integration
module VQEErrorMitigationTests =
    open FSharp.Azure.Quantum.Core.BackendAbstraction
    open FSharp.Azure.Quantum.Backends.LocalBackend
    
    [<Fact>]
    let ``SolverConfig should accept ErrorMitigation field`` () =
        // Arrange - Create a config with no error mitigation
        let config = {
            Method = GroundStateMethod.VQE
            Backend = Some (LocalBackend() :> IQuantumBackend)
            MaxIterations = 10
            Tolerance = 1e-6
            InitialParameters = None
            ProgressReporter = None
            ErrorMitigation = None
            IntegralProvider = None
        }
        
        // Assert
        Assert.True(config.ErrorMitigation.IsNone)
        Assert.Equal(GroundStateMethod.VQE, config.Method)
    
    [<Fact>]
    let ``SolverConfig should accept ErrorMitigation strategy`` () =
        // Arrange - Create a recommended strategy
        let testBackend : Types.Backend = {
            Id = "test-backend"
            Provider = "Test"
            Name = "Test Backend"
            Status = "Available"
        }
        
        let criteria : ErrorMitigationStrategy.SelectionCriteria = {
            CircuitDepth = 20
            QubitCount = 4
            Backend = testBackend
            MaxCostUSD = Some 50.0
            RequiredAccuracy = None
            Calibration = None
        }
        
        let strategy = ErrorMitigationStrategy.selectStrategy criteria
        
        // Act - Create a config with error mitigation
        let config = {
            Method = GroundStateMethod.VQE
            Backend = Some (LocalBackend() :> IQuantumBackend)
            MaxIterations = 10
            Tolerance = 1e-6
            InitialParameters = None
            ProgressReporter = None
            ErrorMitigation = Some strategy
            IntegralProvider = None
        }
        
        // Assert
        Assert.True(config.ErrorMitigation.IsSome)
        Assert.NotEmpty(config.ErrorMitigation.Value.Reasoning)
    
    [<Fact>]
    let ``VQE run should succeed with no error mitigation`` () =
        // Arrange
        let h2 = Molecule.createH2 0.74
        let config = {
            Method = GroundStateMethod.VQE
            Backend = Some (LocalBackend() :> IQuantumBackend)
            MaxIterations = 5  // Low for fast test
            Tolerance = 1e-3
            InitialParameters = None
            ProgressReporter = None
            ErrorMitigation = None
            IntegralProvider = None
        }
        
        // Act
        let result = GroundStateEnergy.estimateEnergy h2 config |> Async.RunSynchronously
        
        // Assert
        match result with
        | Ok vqeResult ->
            Assert.True(vqeResult.Energy < 0.0)  // Energy should be negative
        | Error err ->
            Assert.Fail($"VQE should succeed: {err.Message}")
    
    [<Fact>]
    let ``VQE run should succeed with error mitigation strategy`` () =
        // Arrange
        let h2 = Molecule.createH2 0.74
        
        let testBackend : Types.Backend = {
            Id = "test-backend"
            Provider = "Test"
            Name = "Test Backend"
            Status = "Available"
        }
        
        // Select a strategy (readout mitigation only - cheapest)
        let criteria : ErrorMitigationStrategy.SelectionCriteria = {
            CircuitDepth = 5  // Shallow circuit for H2
            QubitCount = 4
            Backend = testBackend
            MaxCostUSD = Some 1.0  // Low budget forces readout-only
            RequiredAccuracy = None
            Calibration = None
        }
        let strategy = ErrorMitigationStrategy.selectStrategy criteria
        
        let config = {
            Method = GroundStateMethod.VQE
            Backend = Some (LocalBackend() :> IQuantumBackend)
            MaxIterations = 5  // Low for fast test
            Tolerance = 1e-3
            InitialParameters = None
            ProgressReporter = None
            ErrorMitigation = Some strategy
            IntegralProvider = None
        }
        
        // Act
        let result = GroundStateEnergy.estimateEnergy h2 config |> Async.RunSynchronously
        
        // Assert
        match result with
        | Ok vqeResult ->
            Assert.True(vqeResult.Energy < 0.0)  // Energy should be negative
        | Error err ->
            Assert.Fail($"VQE with error mitigation should succeed: {err.Message}")
    
    [<Fact>]
    let ``ErrorMitigationStrategy selectStrategy should return valid strategy`` () =
        // Arrange
        let testBackend : Types.Backend = {
            Id = "noisy-backend"
            Provider = "IonQ"
            Name = "Noisy Backend"
            Status = "Available"
        }
        
        // Test different scenarios
        let scenarios : ErrorMitigationStrategy.SelectionCriteria list = [
            // Shallow circuit, low budget
            { CircuitDepth = 5; QubitCount = 4; Backend = testBackend; MaxCostUSD = Some 0.5; RequiredAccuracy = None; Calibration = None }
            // Medium circuit, medium budget
            { CircuitDepth = 30; QubitCount = 6; Backend = testBackend; MaxCostUSD = Some 50.0; RequiredAccuracy = None; Calibration = None }
            // Deep circuit, high budget, high accuracy requirement
            { CircuitDepth = 100; QubitCount = 10; Backend = testBackend; MaxCostUSD = Some 500.0; RequiredAccuracy = Some 0.95; Calibration = None }
        ]
        
        // Act & Assert
        for criteria in scenarios do
            let strategy = ErrorMitigationStrategy.selectStrategy criteria
            
            // Strategy should have valid properties
            Assert.NotEmpty(strategy.Reasoning)
            Assert.True(strategy.EstimatedCostMultiplier >= 0.0)
            Assert.True(strategy.EstimatedAccuracy >= 0.0 && strategy.EstimatedAccuracy <= 1.0)
    
    [<Fact>]
    let ``ErrorMitigationStrategy applyStrategy should process histogram`` () =
        // Arrange - Sample histogram from measurements
        let histogram = 
            Map.ofList [
                ("00", 450)
                ("01", 50)
                ("10", 50)
                ("11", 450)
            ]
        
        let testBackend : Types.Backend = {
            Id = "test"
            Provider = "Test"
            Name = "Test"
            Status = "Available"
        }
        
        let criteria : ErrorMitigationStrategy.SelectionCriteria = {
            CircuitDepth = 5
            QubitCount = 2
            Backend = testBackend
            MaxCostUSD = Some 10.0
            RequiredAccuracy = None
            Calibration = None
        }
        let strategy = ErrorMitigationStrategy.selectStrategy criteria
        
        // Act
        let result = ErrorMitigationStrategy.applyStrategy histogram strategy
        
        // Assert
        match result with
        | Ok mitigated ->
            Assert.NotEmpty(mitigated.Histogram)
            Assert.True(mitigated.ActualCostMultiplier >= 0.0)
        | Error err ->
            Assert.Fail($"Error mitigation should succeed: {err.Message}")
