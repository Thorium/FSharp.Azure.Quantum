namespace FSharp.Azure.Quantum.Tests

open Xunit
open FSharp.Azure.Quantum.QuantumChemistry
open FSharp.Azure.Quantum.Core
open FSharp.Azure.Quantum // For ErrorMitigationStrategy
open System.Threading.Tasks

/// Tests for Molecule Representation (Task 1)
module MoleculeTests =

    [<Fact>]
    let ``Create simple H2 molecule with 2 atoms and 1 bond`` () =
        // Arrange & Act
        let h2 =
            {
                Name = "H2"
                Atoms =
                    [
                        {
                            Element = "H"
                            Position = (0.0, 0.0, 0.0)
                        }
                        {
                            Element = "H"
                            Position = (0.0, 0.0, 0.74)
                        } // 0.74 Angstroms apart
                    ]
                Bonds =
                    [
                        {
                            Atom1 = 0
                            Atom2 = 1
                            BondOrder = 1.0
                        } // Single bond
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
        let h2o =
            {
                Name = "H2O"
                Atoms =
                    [
                        {
                            Element = "O"
                            Position = (0.0, 0.0, 0.0)
                        }
                        {
                            Element = "H"
                            Position = (0.0, 0.757, 0.587)
                        }
                        {
                            Element = "H"
                            Position = (0.0, -0.757, 0.587)
                        }
                    ]
                Bonds =
                    [
                        {
                            Atom1 = 0
                            Atom2 = 1
                            BondOrder = 1.0
                        } // O-H bond
                        {
                            Atom1 = 0
                            Atom2 = 2
                            BondOrder = 1.0
                        } // O-H bond
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
        let lih =
            {
                Name = "LiH"
                Atoms =
                    [
                        {
                            Element = "Li"
                            Position = (0.0, 0.0, 0.0)
                        }
                        {
                            Element = "H"
                            Position = (0.0, 0.0, 1.596)
                        } // 1.596 Angstroms
                    ]
                Bonds =
                    [
                        {
                            Atom1 = 0
                            Atom2 = 1
                            BondOrder = 1.0
                        }
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
        let invalidMolecule =
            {
                Name = "Invalid"
                Atoms =
                    [
                        {
                            Element = "H"
                            Position = (0.0, 0.0, 0.0)
                        }
                    ]
                Bonds =
                    [
                        {
                            Atom1 = 0
                            Atom2 = 5
                            BondOrder = 1.0
                        } // Atom 5 doesn't exist!
                    ]
                Charge = 0
                Multiplicity = 1
            }

        // Act
        let result = Molecule.validate invalidMolecule

        // Assert
        result
        |> Result.map (fun _ -> Assert.True(false, "Should have failed validation"))
        |> Result.defaultWith (fun err -> Assert.Contains("Bond references non-existent atom", err.Message))

    [<Fact>]
    let ``Validate bond connectivity - valid molecule passes`` () =
        // Arrange
        let validMolecule =
            {
                Name = "H2"
                Atoms =
                    [
                        {
                            Element = "H"
                            Position = (0.0, 0.0, 0.0)
                        }
                        {
                            Element = "H"
                            Position = (0.0, 0.0, 0.74)
                        }
                    ]
                Bonds =
                    [
                        {
                            Atom1 = 0
                            Atom2 = 1
                            BondOrder = 1.0
                        }
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
        let atom1 =
            {
                Element = "H"
                Position = (0.0, 0.0, 0.0)
            }

        let atom2 =
            {
                Element = "H"
                Position = (0.0, 0.0, 0.74)
            }

        // Act
        let bondLength = Molecule.calculateBondLength atom1 atom2

        // Assert
        Assert.Equal(0.74, bondLength, 6) // 6 decimal places

    [<Fact>]
    let ``Calculate bond length for 3D coordinates`` () =
        // Arrange
        let atom1 =
            {
                Element = "C"
                Position = (1.0, 2.0, 3.0)
            }

        let atom2 =
            {
                Element = "H"
                Position = (4.0, 6.0, 3.0)
            }

        // Act
        let bondLength = Molecule.calculateBondLength atom1 atom2

        // Assert - sqrt((4-1)^2 + (6-2)^2 + (3-3)^2) = sqrt(9 + 16 + 0) = 5.0
        Assert.Equal(5.0, bondLength, 6)

    [<Fact>]
    let ``Count total electrons in molecule`` () =
        // Arrange - H2O has 10 electrons (8 from O, 1 from each H)
        let h2o =
            {
                Name = "H2O"
                Atoms =
                    [
                        {
                            Element = "O"
                            Position = (0.0, 0.0, 0.0)
                        }
                        {
                            Element = "H"
                            Position = (0.0, 0.757, 0.587)
                        }
                        {
                            Element = "H"
                            Position = (0.0, -0.757, 0.587)
                        }
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
        let h3o_plus =
            {
                Name = "H3O+"
                Atoms =
                    [
                        {
                            Element = "O"
                            Position = (0.0, 0.0, 0.0)
                        }
                        {
                            Element = "H"
                            Position = (0.0, 0.0, 1.0)
                        }
                        {
                            Element = "H"
                            Position = (0.866, 0.0, -0.5)
                        }
                        {
                            Element = "H"
                            Position = (-0.866, 0.0, -0.5)
                        }
                    ]
                Bonds = []
                Charge = 1 // +1 charge (lost 1 electron)
                Multiplicity = 1
            }

        // Act
        let electronCount = Molecule.countElectrons h3o_plus

        // Assert
        Assert.Equal(10, electronCount) // 11 - 1 = 10

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
        let h2o = Molecule.createH2O ()

        // Assert
        Assert.Equal("H2O", h2o.Name)
        Assert.Equal(3, h2o.Atoms.Length)
        Assert.Equal(2, h2o.Bonds.Length)
        Assert.Equal("O", h2o.Atoms.[0].Element)

/// Tests for Ground State Energy Estimation (Task 2)
module GroundStateEnergyTests =

    [<Fact>]
    let ``Estimate H2 ground state energy should reach the STO-3G FCI energy -1.137 Hartree`` () =
        // Arrange
        task {
            let h2 = Molecule.createH2 0.74 // Equilibrium bond length

            let config =
                {
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

            // Assert
            match! GroundStateEnergy.estimateEnergy h2 config |> Async.StartImmediateAsTask with
            | Ok vqeResult ->
                // UCCSD-VQE on the bundled H2/STO-3G integrals: FCI -1.13727 Ha within chemical accuracy
                let expected = -1.13727
                let tolerance = 0.0016

                Assert.True(
                    abs (vqeResult.Energy - expected) < tolerance,
                    $"Expected ~%.5f{expected}, got %.5f{vqeResult.Energy}"
                )

                Assert.Equal(ComputedSto3gIntegrals, vqeResult.Source)
                Assert.True(vqeResult.Iterations > 0, "VQE must iterate")
            | Error err -> Assert.True(false, $"Energy calculation failed: %s{err.Message}")
        }
        :> Task

    [<Fact>]
    let ``Estimate H2O ground state energy without an IntegralProvider returns Error, not a tabulated energy`` () =
        // Arrange
        task {
            let h2o = Molecule.createH2O ()

            let config =
                {
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

            // Assert
            match! GroundStateEnergy.estimateEnergy h2o config |> Async.StartImmediateAsTask with
            | Ok vqeResult ->
                Assert.Fail($"H2O has no bundled integrals; got energy {vqeResult.Energy} from {vqeResult.Source}")
            | Error err -> Assert.Contains("IntegralProvider", err.Message)
        }
        :> Task

    [<Fact>]
    let ``VQE method should be selectable`` () =
        // Arrange
        task {
            let h2 = Molecule.createH2 0.74

            let config =
                {
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
            let! result =
                GroundStateEnergy.estimateEnergyWith GroundStateMethod.VQE h2 config
                |> Async.StartImmediateAsTask

            // Assert
            Assert.True(result |> Result.isOk, "VQE should complete successfully")
        }
        :> Task

    [<Fact>]
    let ``Classical DFT fallback should work for small molecules`` () =
        // Arrange
        task {
            let h2 = Molecule.createH2 0.74

            let config =
                {
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
            let! result =
                GroundStateEnergy.estimateEnergyWith GroundStateMethod.ClassicalDFT h2 config
                |> Async.StartImmediateAsTask

            // Assert
            match result with
            | Ok vqeResult ->
                // DFT should give reasonable approximation
                let expected = -1.174
                let tolerance = 0.2 // DFT may be less accurate

                Assert.True(
                    abs (vqeResult.Energy - expected) < tolerance,
                    $"DFT: Expected ~%.3f{expected}, got %.3f{vqeResult.Energy}"
                )

                Assert.Equal(TabulatedReference, vqeResult.Source)
            | Error e -> Assert.Fail($"ClassicalDFT is a supported method, got: {e}")
        }
        :> Task

    [<Fact>]
    let ``Auto-detect method should choose appropriate algorithm`` () =
        // Arrange - Automatic picks a quantum method, never the tabulated reference
        task {
            let h2 = Molecule.createH2 0.74

            let config =
                {
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
            let! result =
                GroundStateEnergy.estimateEnergy h2 config |> Async.StartImmediateAsTask

            // Assert
            match result with
            | Ok r -> Assert.NotEqual(TabulatedReference, r.Source)
            | Error e -> Assert.Fail($"Auto-detect should work: {e.Message}")
        }
        :> Task

    [<Fact>]
    let ``Invalid molecule should return error`` () =
        // Arrange - molecule with no atoms
        task {
            let invalidMolecule =
                {
                    Name = "Empty"
                    Atoms = []
                    Bonds = []
                    Charge = 0
                    Multiplicity = 1
                }

            let config =
                {
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
            let! result =
                GroundStateEnergy.estimateEnergy invalidMolecule config
                |> Async.StartImmediateAsTask

            // Assert
            result
            |> Result.map (fun _ -> Assert.True(false, "Should have failed for invalid molecule"))
            |> Result.defaultWith (fun err -> Assert.Contains("Invalid", err.Message))
        }
        :> Task

    [<Fact>]
    let ``Energy units should be in Hartree`` () =
        // Arrange
        task {
            let h2 = Molecule.createH2 0.74

            let config =
                {
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

            // Assert
            match! GroundStateEnergy.estimateEnergy h2 config |> Async.StartImmediateAsTask with
            | Ok vqeResult ->
                // Energy should be negative and in reasonable range for H2
                Assert.True(vqeResult.Energy < 0.0, "Ground state energy should be negative")
                Assert.True(vqeResult.Energy > -10.0, "H2 energy should be > -10 Hartree")
            | Error _ -> Assert.True(false, "Should calculate energy")
        }
        :> Task

    [<Fact>]
    let ``VQE should handle convergence limits`` () =
        // Arrange
        task {
            let h2 = Molecule.createH2 0.74

            let config =
                {
                    Method = GroundStateMethod.VQE
                    MaxIterations = 10 // Very few iterations
                    Tolerance = 1e-8 // Very tight tolerance
                    InitialParameters = None
                    Backend = None
                    ProgressReporter = None
                    ErrorMitigation = None
                    IntegralProvider = None
                }

            // Act

            // Assert
            // Hitting the iteration cap is not an error: VQE returns its best energy and
            // reports Converged = false.
            match! GroundStateEnergy.estimateEnergy h2 config |> Async.StartImmediateAsTask with
            | Ok r ->
                Assert.True(r.Energy < 0.0, $"H2 ground-state energy should be negative, got {r.Energy}")

                Assert.True(
                    r.Converged || r.Iterations = config.MaxIterations,
                    $"not converged, yet stopped at iteration {r.Iterations} of {config.MaxIterations}"
                )
            | Error e -> Assert.Fail($"the iteration cap must not turn into an error: {e}")
        }
        :> Task

    [<Fact>]
    let ``Initial parameters can be provided for VQE`` () =
        // Arrange
        task {
            let h2 = Molecule.createH2 0.74
            // UCCSD for H2 (2 electrons, 4 spin orbitals): 4 singles + 1 double
            let initialParams = [| 0.0; 0.0; 0.0; 0.0; 0.1 |]

            let config =
                {
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
            let! result =
                GroundStateEnergy.estimateEnergy h2 config |> Async.StartImmediateAsTask

            // Assert
            Assert.True(result |> Result.isOk, "Should accept initial parameters")
        }
        :> Task

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
        let hamiltonian =
            {
                QaoaCircuit.NumQubits = 2
                QaoaCircuit.Terms = [||]
            }

        let initialSV = StateVector.init 2
        let initialState = QuantumState.StateVector initialSV

        let config =
            {
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
        let hamiltonian =
            {
                QaoaCircuit.NumQubits = 1
                QaoaCircuit.Terms =
                    [|
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

        let config =
            {
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

            let config =
                {
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
                Assert.Equal(1.0, norm, 6) // Within 1e-6 tolerance

    [<Fact>]
    let ``Higher Trotter steps should improve accuracy`` () =
        // Arrange - Simple 1-qubit Hamiltonian
        let hamiltonian =
            {
                QaoaCircuit.NumQubits = 1
                QaoaCircuit.Terms =
                    [|
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
        let config10Steps =
            {
                HamiltonianSimulation.SimulationConfig.Time = time
                HamiltonianSimulation.SimulationConfig.TrotterSteps = 10
                HamiltonianSimulation.SimulationConfig.TrotterOrder = 1
                HamiltonianSimulation.SimulationConfig.Backend = None
            }

        let config100Steps =
            {
                HamiltonianSimulation.SimulationConfig.Time = time
                HamiltonianSimulation.SimulationConfig.TrotterSteps = 100
                HamiltonianSimulation.SimulationConfig.TrotterOrder = 1
                HamiltonianSimulation.SimulationConfig.Backend = None
            }

        let result10 = HamiltonianSimulation.simulate hamiltonian initialState config10Steps

        let result100 =
            HamiltonianSimulation.simulate hamiltonian initialState config100Steps

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
        let hamiltonian =
            {
                QaoaCircuit.NumQubits = 2
                QaoaCircuit.Terms =
                    [|
                        {
                            Coefficient = 1.0
                            QubitsIndices = [| 0; 1 |]
                            PauliOperators = [| QaoaCircuit.PauliZ; QaoaCircuit.PauliZ |]
                        }
                    |]
            }

        let initialSV = StateVector.init 2
        let initialState = QuantumState.StateVector initialSV

        let config =
            {
                HamiltonianSimulation.SimulationConfig.Time = 0.5
                HamiltonianSimulation.SimulationConfig.TrotterSteps = 10
                HamiltonianSimulation.SimulationConfig.TrotterOrder = 2 // Second-order Trotter
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
        let hamiltonian =
            {
                QaoaCircuit.NumQubits = 2
                QaoaCircuit.Terms =
                    [|
                        {
                            Coefficient = 0.5
                            QubitsIndices = [| 0; 1 |]
                            PauliOperators = [| QaoaCircuit.PauliZ; QaoaCircuit.PauliZ |]
                        }
                    |]
            }

        let initialSV = StateVector.init 2 // |00⟩ state
        let initialState = QuantumState.StateVector initialSV

        let config =
            {
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
        let hamiltonian =
            {
                QaoaCircuit.NumQubits = 3
                QaoaCircuit.Terms =
                    [|
                        {
                            Coefficient = 0.5
                            QubitsIndices = [| 0; 1; 2 |]
                            PauliOperators = [| QaoaCircuit.PauliZ; QaoaCircuit.PauliZ; QaoaCircuit.PauliZ |]
                        }
                    |]
            }

        let initialSV = StateVector.init 3 // |000⟩ state
        let initialState = QuantumState.StateVector initialSV

        let config =
            {
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
        let hamiltonian =
            {
                QaoaCircuit.NumQubits = 2
                QaoaCircuit.Terms =
                    [|
                        {
                            Coefficient = 0.5
                            QubitsIndices = [| 0; 1 |]
                            PauliOperators = [| QaoaCircuit.PauliX; QaoaCircuit.PauliY |]
                        }
                    |]
            }

        let initialSV = StateVector.init 2 // |00⟩ state
        let initialState = QuantumState.StateVector initialSV

        let config =
            {
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
        let hamiltonian =
            {
                QaoaCircuit.NumQubits = 4
                QaoaCircuit.Terms =
                    [|
                        {
                            Coefficient = 0.25
                            QubitsIndices = [| 0; 1; 2; 3 |]
                            PauliOperators =
                                [|
                                    QaoaCircuit.PauliZ
                                    QaoaCircuit.PauliZ
                                    QaoaCircuit.PauliZ
                                    QaoaCircuit.PauliZ
                                |]
                        }
                    |]
            }

        let initialSV = StateVector.init 4 // |0000⟩ state
        let initialState = QuantumState.StateVector initialSV

        let config =
            {
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
        let hamiltonian =
            {
                QaoaCircuit.NumQubits = 3
                QaoaCircuit.Terms =
                    [|
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

        let initialSV = StateVector.init 3 // |000⟩ state
        let initialState = QuantumState.StateVector initialSV

        let config =
            {
                HamiltonianSimulation.SimulationConfig.Time = 0.5
                HamiltonianSimulation.SimulationConfig.TrotterSteps = 20
                HamiltonianSimulation.SimulationConfig.TrotterOrder = 2 // 2nd order Trotter
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
            let xyzContent =
                """2
 H2 molecule
 H  0.0  0.0  0.0
 H  0.0  0.0  0.74"""

            let tempFile = Path.GetTempFileName()
            do! File.WriteAllTextAsync(tempFile, xyzContent) |> Async.AwaitTask

            try
                // Act

                // Assert
                match! Molecule.fromXyzFileAsync tempFile with
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
        }
        |> Async.StartAsTask

    [<Fact>]
    let ``Parse XYZ file - H2O molecule`` () =
        async {
            // Arrange
            let xyzContent =
                """3
 Water molecule
 O  0.000  0.000  0.000
 H  0.000  0.757  0.587
 H  0.000 -0.757  0.587"""

            let tempFile = Path.GetTempFileName()
            do! File.WriteAllTextAsync(tempFile, xyzContent) |> Async.AwaitTask

            try
                // Act

                // Assert
                match! Molecule.fromXyzFileAsync tempFile with
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
        }
        |> Async.StartAsTask

    [<Fact>]
    let ``XYZ parser should handle tabs and multiple spaces`` () =
        async {
            // Arrange - XYZ with irregular whitespace
            let xyzContent =
                """2
 H2 with tabs
 H    0.0    0.0    0.0
 H		0.0		0.0		0.74"""

            let tempFile = Path.GetTempFileName()
            do! File.WriteAllTextAsync(tempFile, xyzContent) |> Async.AwaitTask

            try
                // Act

                // Assert
                match! Molecule.fromXyzFileAsync tempFile with
                | Error err -> Assert.True(false, $"Should handle whitespace: {err.Message}")
                | Ok molecule -> Assert.Equal(2, molecule.Atoms.Length)
            finally
                File.Delete(tempFile)
        }
        |> Async.StartAsTask

    [<Fact>]
    let ``XYZ parser should reject malformed file`` () =
        async {
            // Arrange - invalid XYZ (wrong atom count)
            let xyzContent =
                """5
 Should have 5 atoms but only has 2
 H  0.0  0.0  0.0
 H  0.0  0.0  0.74"""

            let tempFile = Path.GetTempFileName()
            do! File.WriteAllTextAsync(tempFile, xyzContent) |> Async.AwaitTask

            try
                // Act

                // Assert
                match! Molecule.fromXyzFileAsync tempFile with
                | Ok _ -> Assert.True(false, "Should reject file with wrong atom count")
                | Error err -> Assert.Contains("Expected", err.Message) // Error message from MoleculeFormats
            finally
                File.Delete(tempFile)
        }
        |> Async.StartAsTask

    [<Fact>]
    let ``Parse FCIDump header - extract NORB and NELEC`` () =
        async {
            // Arrange - minimal FCIDump header
            let fcidumpContent =
                """&FCI NORB=  2,NELEC=  2,MS2=  0,
  ORBSYM=1,1,
  ISYM=1,
 &END"""

            let tempFile = Path.GetTempFileName()
            do! File.WriteAllTextAsync(tempFile, fcidumpContent) |> Async.AwaitTask

            try
                // Act - Use MoleculeFormats directly since FCIDump has no geometry
                // Molecule.fromFciDumpFileAsync will fail with MissingGeometry error

                // Assert
                match!
                    FSharp.Azure.Quantum.Data.MoleculeFormats.FciDump.readAsync
                        tempFile
                        System.Threading.CancellationToken.None
                    |> Async.AwaitTask
                with
                | Error err -> Assert.True(false, $"Parsing failed: {err.Message}")
                | Ok moleculeData ->
                    // Should extract NORB=2, NELEC=2 from header metadata
                    // Note: Placeholder atoms = NELEC/2 = 1 atom
                    Assert.Equal(1, moleculeData.Topology.Atoms.Length) // Placeholder atoms (NELEC/2)
                    Assert.Equal(Some 0, moleculeData.Topology.Charge) // NORB - NELEC = 0
                    Assert.Equal(Some 1, moleculeData.Topology.Multiplicity) // MS2=0 → singlet
                    Assert.Equal(None, moleculeData.Geometry) // FCIDump has no geometry
                    // Verify metadata has the original values
                    Assert.Equal("2", moleculeData.Topology.Metadata.["norb"])
                    Assert.Equal("2", moleculeData.Topology.Metadata.["nelec"])
            finally
                File.Delete(tempFile)
        }
        |> Async.StartAsTask

    [<Fact>]
    let ``FCIDump parser should handle missing parameters`` () =
        async {
            // Arrange - FCIDump without NORB
            let fcidumpContent =
                """&FCI NELEC=  2,MS2=  0,
 &END"""

            let tempFile = Path.GetTempFileName()
            do! File.WriteAllTextAsync(tempFile, fcidumpContent) |> Async.AwaitTask

            try
                // Act - Use MoleculeFormats directly

                // Assert
                match!
                    FSharp.Azure.Quantum.Data.MoleculeFormats.FciDump.readAsync
                        tempFile
                        System.Threading.CancellationToken.None
                    |> Async.AwaitTask
                with
                | Ok _ -> Assert.True(false, "Should require NORB parameter")
                | Error err -> Assert.Contains("NORB", err.Message)
            finally
                File.Delete(tempFile)
        }
        |> Async.StartAsTask

    [<Fact>]
    let ``Convert molecule to XYZ format`` () =
        // Arrange
        let h2 = Molecule.createH2 0.74

        // Act
        let xyzContent = Molecule.toXyz h2

        // Assert
        let lines =
            xyzContent.Split([| '\n'; '\r' |], System.StringSplitOptions.RemoveEmptyEntries)

        Assert.True(lines.Length >= 4, "Should have at least 4 lines (count, title, 2 atoms)")
        Assert.Equal("2", lines[0].Trim()) // Atom count
        Assert.Equal("H2", lines[1].Trim()) // Name
        Assert.Contains("H", lines[2]) // First H atom
        Assert.Contains("H", lines[3]) // Second H atom

    [<Fact>]
    let ``Save and reload XYZ file`` () =
        async {
            // Arrange
            let original = Molecule.createH2O ()
            let tempFile = Path.GetTempFileName()

            try
                // Act - save to file

                match! Molecule.saveToXyzFileAsync tempFile original with
                | Error err -> Assert.True(false, $"Save failed: {err.Message}")
                | Ok() ->

                    // Act - reload from file

                    match! Molecule.fromXyzFileAsync tempFile with
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
                if File.Exists tempFile then
                    File.Delete(tempFile)
        }
        |> Async.StartAsTask

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
        let problem =
            quantumChemistry {
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
        let problem =
            quantumChemistry {
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
        let molecule = h2o 0.96 104.5 // Equilibrium geometry

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
        let problem =
            quantumChemistry {
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
        let problem =
            quantumChemistry {
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
        let problem =
            quantumChemistry {
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
        let minimalBasis =
            quantumChemistry {
                molecule (h2 0.74)
                basis "sto-3g"
                ansatz UCCSD
            }

        let largeBasis =
            quantumChemistry {
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
        task {
            let problem =
                quantumChemistry {
                    molecule (h2 0.74)
                    basis "sto-3g"
                    ansatz UCCSD
                    maxIterations 50
                }

            // Act

            // Assert
            match! solve problem |> Async.StartImmediateAsTask with
            | Ok chemResult ->
                // H2 ground state should be negative
                Assert.True(chemResult.GroundStateEnergy < 0.0, "Ground state energy should be negative")

                // Should have bond length information
                Assert.True(chemResult.BondLengths.Count > 0, "Should compute bond lengths")

                // H-H bond should be present
                let hasHHBond = chemResult.BondLengths |> Map.exists (fun k _ -> k.Contains 'H')
                Assert.True(hasHHBond, "Should have H-H bond length")

            | Error err -> Assert.True(false, $"Solve failed: %s{err.Message}")
        }
        :> Task

    [<Fact>]
    let ``Solve on H2O without molecular integrals returns Error, not a tabulated energy`` () =
        // Arrange
        task {
            let problem =
                quantumChemistry {
                    molecule (h2o 0.96 104.5)
                    basis "sto-3g"
                    ansatz HEA
                    maxIterations 50
                }

            // Act

            // Assert
            // The builder passes no IntegralProvider, and H2O has no bundled integrals.
            match! solve problem |> Async.StartImmediateAsTask with
            | Ok chemResult ->
                Assert.Fail(
                    $"H2O has no integrals here; got {chemResult.GroundStateEnergy} Ha from {chemResult.Source}"
                )
            | Error err -> Assert.Contains("IntegralProvider", err.Message)
        }
        :> Task

    // ========================================================================
    // TEST 7: Multiple Molecules
    // ========================================================================

    [<Fact>]
    let ``Builder should work with different molecules`` () =
        // Arrange & Act - H2
        let h2Problem =
            quantumChemistry {
                molecule (h2 0.74)
                basis "sto-3g"
                ansatz UCCSD
            }

        // Arrange & Act - H2O
        let h2oProblem =
            quantumChemistry {
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
        let problem =
            quantumChemistry {
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
        let problem =
            quantumChemistry {
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
        let problem =
            quantumChemistry {
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
        let problem =
            quantumChemistry {
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
        let problem1 =
            quantumChemistry {
                molecule (h2 0.74)
                basis "sto-3g"
                ansatz UCCSD
            }

        Assert.True(problem1.Molecule.IsSome)
        Assert.True(problem1.MoleculeSource.IsNone)

        // Test 2: From XYZ file (new)
        let problem2 =
            quantumChemistry {
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
            let xyzContent =
                """2
H2 test molecule
H  0.0  0.0  0.0
H  0.0  0.0  0.74"""

            let tempFile = Path.GetTempFileName()
            do! File.WriteAllTextAsync(tempFile, xyzContent) |> Async.AwaitTask

            try
                // Act - use builder with file loading
                let problem =
                    quantumChemistry {
                        molecule_from_xyz tempFile
                        basis "sto-3g"
                        ansatz UCCSD
                        maxIterations 5 // Quick test
                    }


                // Assert
                match! solve problem with
                | Ok chemistry ->
                    // Energy should be negative (bound state)
                    Assert.True(
                        chemistry.GroundStateEnergy < 0.0,
                        $"Ground state energy should be negative, got {chemistry.GroundStateEnergy}"
                    )
                    // Should have H-H bond length
                    Assert.True(chemistry.BondLengths.ContainsKey("H-H"), "Should have H-H bond")
                | Error err -> Assert.Fail($"solve failed: {err.Message}")
            finally
                File.Delete(tempFile)
        }
        |> Async.StartAsTask

    [<Fact>]
    let ``solve with molecule_from_name should load from default provider`` () =
        async {
            // Act - use builder with named molecule
            let problem =
                quantumChemistry {
                    molecule_from_name "h2" // Built-in H2 molecule
                    basis "sto-3g"
                    ansatz UCCSD
                    maxIterations 5 // Quick test
                }


            // Assert
            match! solve problem with
            | Ok chemistry ->
                Assert.True(
                    chemistry.GroundStateEnergy < 0.0,
                    $"Ground state energy should be negative, got {chemistry.GroundStateEnergy}"
                )
            | Error err -> Assert.Fail($"'h2' should load from the default provider: {err.Message}")
        }
        |> Async.StartAsTask

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
        let config =
            {
                Method = GroundStateMethod.VQE
                Backend = Some(LocalBackend() :> IQuantumBackend)
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
        let testBackend: Types.Backend =
            {
                Id = "test-backend"
                Provider = "Test"
                Name = "Test Backend"
                Status = "Available"
            }

        let criteria: ErrorMitigationStrategy.SelectionCriteria =
            {
                CircuitDepth = 20
                QubitCount = 4
                Backend = testBackend
                MaxCostUSD = Some 50.0
                RequiredAccuracy = None
                Calibration = None
            }

        let strategy = ErrorMitigationStrategy.selectStrategy criteria

        // Act - Create a config with error mitigation
        let config =
            {
                Method = GroundStateMethod.VQE
                Backend = Some(LocalBackend() :> IQuantumBackend)
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
        task {
            let h2 = Molecule.createH2 0.74

            let config =
                {
                    Method = GroundStateMethod.VQE
                    Backend = Some(LocalBackend() :> IQuantumBackend)
                    MaxIterations = 5 // Low for fast test
                    Tolerance = 1e-3
                    InitialParameters = None
                    ProgressReporter = None
                    ErrorMitigation = None
                    IntegralProvider = None
                }

            // Act

            // Assert
            match! GroundStateEnergy.estimateEnergy h2 config |> Async.StartImmediateAsTask with
            | Ok vqeResult -> Assert.True(vqeResult.Energy < 0.0) // Energy should be negative
            | Error err -> Assert.Fail($"VQE should succeed: {err.Message}")
        }
        :> Task

    [<Fact>]
    let ``VQE run should succeed with error mitigation strategy`` () =
        // Arrange
        task {
            let h2 = Molecule.createH2 0.74

            let testBackend: Types.Backend =
                {
                    Id = "test-backend"
                    Provider = "Test"
                    Name = "Test Backend"
                    Status = "Available"
                }

            // Select a strategy (readout mitigation only - cheapest)
            let criteria: ErrorMitigationStrategy.SelectionCriteria =
                {
                    CircuitDepth = 5 // Shallow circuit for H2
                    QubitCount = 4
                    Backend = testBackend
                    MaxCostUSD = Some 1.0 // Low budget forces readout-only
                    RequiredAccuracy = None
                    Calibration = None
                }

            let strategy = ErrorMitigationStrategy.selectStrategy criteria

            let config =
                {
                    Method = GroundStateMethod.VQE
                    Backend = Some(LocalBackend() :> IQuantumBackend)
                    MaxIterations = 5 // Low for fast test
                    Tolerance = 1e-3
                    InitialParameters = None
                    ProgressReporter = None
                    ErrorMitigation = Some strategy
                    IntegralProvider = None
                }

            // Act

            // Assert
            match! GroundStateEnergy.estimateEnergy h2 config |> Async.StartImmediateAsTask with
            | Ok vqeResult -> Assert.True(vqeResult.Energy < 0.0) // Energy should be negative
            | Error err -> Assert.Fail($"VQE with error mitigation should succeed: {err.Message}")
        }
        :> Task

    [<Fact>]
    let ``ErrorMitigationStrategy selectStrategy should return valid strategy`` () =
        // Arrange
        let testBackend: Types.Backend =
            {
                Id = "noisy-backend"
                Provider = "IonQ"
                Name = "Noisy Backend"
                Status = "Available"
            }

        // Test different scenarios
        let scenarios: ErrorMitigationStrategy.SelectionCriteria list =
            [
                // Shallow circuit, low budget
                {
                    CircuitDepth = 5
                    QubitCount = 4
                    Backend = testBackend
                    MaxCostUSD = Some 0.5
                    RequiredAccuracy = None
                    Calibration = None
                }
                // Medium circuit, medium budget
                {
                    CircuitDepth = 30
                    QubitCount = 6
                    Backend = testBackend
                    MaxCostUSD = Some 50.0
                    RequiredAccuracy = None
                    Calibration = None
                }
                // Deep circuit, high budget, high accuracy requirement
                {
                    CircuitDepth = 100
                    QubitCount = 10
                    Backend = testBackend
                    MaxCostUSD = Some 500.0
                    RequiredAccuracy = Some 0.95
                    Calibration = None
                }
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
        let histogram = Map.ofList [ ("00", 450); ("01", 50); ("10", 50); ("11", 450) ]

        let testBackend: Types.Backend =
            {
                Id = "test"
                Provider = "Test"
                Name = "Test"
                Status = "Available"
            }

        let criteria: ErrorMitigationStrategy.SelectionCriteria =
            {
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
        | Error err -> Assert.Fail($"Error mitigation should succeed: {err.Message}")

/// Regression tests pinning the physical correctness of the bundled H2/STO-3G
/// reference integrals (h2Sto3gIntegrals) and the fail-closed placeholder path.
module H2ReferenceIntegralTests =
    open System.Numerics
    open FSharp.Azure.Quantum.QuantumChemistry
    open FSharp.Azure.Quantum.QuantumChemistry.MolecularHamiltonian
    open FSharp.Azure.Quantum.QuantumChemistry.FermionMapping
    open FSharp.Azure.Quantum.Core.QaoaCircuit // PauliOperator (PauliX/Y/Z/I)
    open FSharp.Azure.Quantum.Core.BackendAbstraction
    open FSharp.Azure.Quantum.Backends.LocalBackend
    open MathNet.Numerics.LinearAlgebra

    // Single-qubit Pauli matrices over System.Numerics.Complex.
    let private c0 = Complex.Zero
    let private c1 = Complex.One
    let private ci = Complex.ImaginaryOne
    let private mat (a: Complex[,]) = Matrix<Complex>.Build.DenseOfArray a
    let private mI = mat (array2D [ [ c1; c0 ]; [ c0; c1 ] ])
    let private mX = mat (array2D [ [ c0; c1 ]; [ c1; c0 ] ])
    let private mY = mat (array2D [ [ c0; -ci ]; [ ci; c0 ] ])
    let private mZ = mat (array2D [ [ c1; c0 ]; [ c0; -c1 ] ])

    let private pauliMat (op: PauliOperator) =
        match op with
        | PauliOperator.PauliI -> mI
        | PauliOperator.PauliX -> mX
        | PauliOperator.PauliY -> mY
        | PauliOperator.PauliZ -> mZ

    [<Fact>]
    let ``h2Sto3gIntegrals reproduce FCI ground state within chemical accuracy`` () =
        // Build the JW qubit Hamiltonian from the bundled real integrals, then
        // exactly diagonalise it. The total ground state (electronic + nuclear
        // repulsion) must match the known FCI value for H2/STO-3G at R=0.7414 A.
        match buildFromIntegrals h2Sto3gIntegrals JordanWigner with
        | Error e -> Assert.Fail($"buildFromIntegrals failed: {e}")
        | Ok(qaoaHam, nucRep) ->
            let mh = fromQaoaHamiltonian qaoaHam
            Assert.Equal(4, mh.NumQubits)
            let dim = 1 <<< mh.NumQubits
            let mutable H = Matrix<Complex>.Build.Dense(dim, dim, c0)

            for term in mh.Terms do
                let mutable acc = Matrix<Complex>.Build.Dense(1, 1, c1)

                for q in 0 .. mh.NumQubits - 1 do
                    let p =
                        match Map.tryFind q term.Operators with
                        | Some op -> pauliMat op
                        | None -> mI

                    acc <- acc.KroneckerProduct p

                H <- H + acc.Multiply(term.Coefficient)

            let minElectronic =
                H.Evd(Symmetricity.Hermitian).EigenValues
                |> Seq.map (fun z -> z.Real)
                |> Seq.min

            let total = minElectronic + nucRep
            let fciTotal = -1.137270 // literature FCI, H2/STO-3G at R=0.7414 A

            Assert.True(
                abs (total - fciTotal) < 0.0016,
                $"Total ground-state energy {total} Ha must be within chemical accuracy of FCI {fciTotal} Ha"
            )

    let private h2Molecule =
        {
            Name = "H2"
            Atoms =
                [
                    {
                        Element = "H"
                        Position = (0.0, 0.0, 0.0)
                    }
                    {
                        Element = "H"
                        Position = (0.0, 0.0, 0.7414)
                    }
                ]
            Bonds =
                [
                    {
                        Atom1 = 0
                        Atom2 = 1
                        BondOrder = 1.0
                    }
                ]
            Charge = 0
            Multiplicity = 1
        }

    [<Fact>]
    let ``buildWithMapping uses an IntegralProvider to build a real Hamiltonian`` () =
        // The integration seam: a provider supplying real integrals (here the verified
        // h2Sto3gIntegrals) yields a physically correct 4-qubit Hamiltonian.
        let provider: IntegralProvider = fun _ -> Ok h2Sto3gIntegrals

        (buildWithMapping h2Molecule JordanWigner (Some provider))
        |> Result.map (fun hamiltonian -> Assert.Equal(4, (fromQaoaHamiltonian hamiltonian).NumQubits))
        |> Result.defaultWith (fun e -> Assert.Fail($"Provider-backed build should succeed, got {e}"))

    [<Fact; Trait("Category", "Slow")>]
    let ``ChemistryVQE UCCSD converges to FCI for H2 within chemical accuracy`` () =
        // End-to-end VQE: the UCCSD ansatz must reach the H2 ground state. Guards
        // against the two convergence bugs — the ansatz rotation using Coefficient.Real
        // (the cluster operator's JW image is imaginary, so the ansatz was a no-op), and
        // shot-sampled energy that made the finite-difference gradients pure noise.
        match buildFromIntegrals h2Sto3gIntegrals JordanWigner with
        | Error e -> Assert.Fail($"buildFromIntegrals failed: {e}")
        | Ok(qaoaHam, nucRep) ->
            let backend = LocalBackend() :> IQuantumBackend

            let config: ChemistryVQE.ChemistryVQEConfig =
                {
                    Hamiltonian = fromQaoaHamiltonian qaoaHam
                    Ansatz = ChemistryVQE.UCCSD(2, 4)
                    MaxIterations = 100
                    Tolerance = 1e-4
                    UseHFInitialState = true
                    Backend = backend
                    ProgressReporter = None
                }

            match ChemistryVQE.run config |> Async.RunSynchronously with
            | Ok result ->
                let total = result.Energy + nucRep

                Assert.True(
                    abs (total - (-1.137270)) < 0.0016,
                    $"VQE total energy {total} Ha must be within chemical accuracy of FCI -1.13727 Ha "
                    + $"(electronic {result.Energy}, nuclear {nucRep})"
                )
            | Error e -> Assert.Fail($"ChemistryVQE.run failed: {e}")

/// VQE.run runs UCCSD circuits on molecular integrals (IntegralProvider or the bundled
/// H2/STO-3G set) and returns Error rather than a tabulated energy when it cannot run.
module VqeIntegralPathTests =
    open FSharp.Azure.Quantum.Core.BackendAbstraction
    open FSharp.Azure.Quantum.Backends.LocalBackend

    [<Literal>]
    let private fciH2 = -1.13727
    let private chemicalAccuracy = 0.0016

    /// Counts every operation the solver applies through the backend.
    type private CountingBackend(inner: IQuantumBackend) =
        let mutable applied = 0
        member _.Applied = applied

        interface IQuantumBackend with
            member _.ExecuteToState circuit = inner.ExecuteToState circuit
            member _.NativeStateType = inner.NativeStateType

            member _.ApplyOperation operation state =
                applied <- applied + 1
                inner.ApplyOperation operation state

            member _.SupportsOperation operation = inner.SupportsOperation operation
            member _.Name = inner.Name + " (counting)"
            member _.InitializeState numQubits = inner.InitializeState numQubits
            member _.ExecuteToStateAsync circuit ct = inner.ExecuteToStateAsync circuit ct

            member _.ApplyOperationAsync operation state ct =
                inner.ApplyOperationAsync operation state ct

    /// A backend that reports a capacity of `maxQubits`.
    type private QubitLimitedBackend(inner: IQuantumBackend, maxQubits: int) =
        let counting = CountingBackend inner
        member _.Applied = counting.Applied

        interface IQubitLimitedBackend with
            member _.MaxQubits = Some maxQubits

        interface IQuantumBackend with
            member _.ExecuteToState circuit =
                (counting :> IQuantumBackend).ExecuteToState circuit

            member _.NativeStateType = inner.NativeStateType

            member _.ApplyOperation operation state =
                (counting :> IQuantumBackend).ApplyOperation operation state

            member _.SupportsOperation operation = inner.SupportsOperation operation
            member _.Name = $"limited-{maxQubits}"
            member _.InitializeState numQubits = inner.InitializeState numQubits
            member _.ExecuteToStateAsync circuit ct = inner.ExecuteToStateAsync circuit ct

            member _.ApplyOperationAsync operation state ct =
                inner.ApplyOperationAsync operation state ct

    let private configWith (backend: IQuantumBackend option) (provider: IntegralProvider option) =
        {
            Method = GroundStateMethod.VQE
            MaxIterations = 100
            Tolerance = 1e-6
            InitialParameters = None
            Backend = backend
            ProgressReporter = None
            ErrorMitigation = None
            IntegralProvider = provider
        }

    let private energyOf molecule config =
        GroundStateEnergy.estimateEnergy molecule config |> Async.RunSynchronously

    let private h2Integrals = MolecularHamiltonian.h2Sto3gIntegrals

    [<Fact>]
    let ``VQE on H2 runs UCCSD circuits on the configured backend and reaches FCI`` () =
        let backend = CountingBackend(LocalBackend())

        match energyOf (Molecule.createH2 0.7414) (configWith (Some(backend :> IQuantumBackend)) None) with
        | Ok r ->
            Assert.True(backend.Applied > 0, "VQE must apply operations through the configured backend")
            Assert.True(r.Iterations > 0, $"VQE must iterate, got {r.Iterations}")
            Assert.NotEmpty(r.OptimalParameters)
            Assert.NotEmpty(r.EnergyHistory)
            Assert.Equal(ComputedSto3gIntegrals, r.Source)

            Assert.True(
                abs (r.Energy - fciH2) < chemicalAccuracy,
                $"UCCSD-VQE energy {r.Energy} Ha must be within chemical accuracy of FCI {fciH2} Ha"
            )
        | Error e -> Assert.Fail($"VQE on H2 should succeed: {e.Message}")

    [<Fact>]
    let ``IntegralProvider integrals determine the VQE energy`` () =
        let h2 = Molecule.createH2 0.7414

        // Same electronic Hamiltonian, nuclear repulsion shifted by exactly 1 Ha.
        let shifted: IntegralProvider =
            fun _ ->
                Ok
                    { h2Integrals with
                        NuclearRepulsion = h2Integrals.NuclearRepulsion + 1.0
                    }

        // Different electronic Hamiltonian: one-electron integrals scaled by 1.1.
        let scaled: IntegralProvider =
            fun _ ->
                Ok
                    { h2Integrals with
                        OneElectron.Integrals = Array2D.map (fun h -> 1.1 * h) h2Integrals.OneElectron.Integrals
                    }

        match
            energyOf h2 (configWith None (Some(fun _ -> Ok h2Integrals))),
            energyOf h2 (configWith None (Some shifted)),
            energyOf h2 (configWith None (Some scaled))
        with
        | Ok bundled, Ok fromShifted, Ok fromScaled ->
            Assert.Equal(ProviderIntegrals, fromShifted.Source)
            Assert.Equal(ProviderIntegrals, fromScaled.Source)

            Assert.True(
                abs (fromShifted.Energy - bundled.Energy - 1.0) < 1e-9,
                $"shifted nuclear repulsion must shift the energy by 1 Ha: {bundled.Energy} -> {fromShifted.Energy}"
            )

            Assert.True(
                abs (fromScaled.Energy - bundled.Energy) > 0.1,
                $"scaled one-electron integrals must change the energy: {bundled.Energy} vs {fromScaled.Energy}"
            )
        | a, b, c -> Assert.Fail($"all three runs should succeed: %A{a} / %A{b} / %A{c}")

    [<Fact>]
    let ``IntegralProvider Error is returned as Error`` () =
        let failing: IntegralProvider = fun _ -> Error "PySCF is not installed"

        match energyOf (Molecule.createH2 0.7414) (configWith None (Some failing)) with
        | Ok r -> Assert.Fail($"a failing provider must not yield an energy, got {r.Energy} from {r.Source}")
        | Error e -> Assert.Contains("PySCF is not installed", e.Message)

    [<Fact>]
    let ``Stretched H2 runs on computed STO-3G integrals and a provider takes precedence`` () =
        let stretched = Molecule.createH2 1.0

        match energyOf stretched (configWith None None) with
        | Ok r ->
            Assert.Equal(ComputedSto3gIntegrals, r.Source)

            // FCI for H2/STO-3G at R = 1.0 Å
            Assert.True(
                abs (r.Energy - -1.1011503) < chemicalAccuracy,
                $"UCCSD-VQE energy {r.Energy} Ha must be within chemical accuracy of FCI -1.1011503 Ha"
            )
        | Error e -> Assert.Fail($"H2 at 1.0 A should run on STO-3G integrals: {e.Message}")

        match energyOf stretched (configWith None (Some(fun _ -> Ok h2Integrals))) with
        | Ok r -> Assert.Equal(ProviderIntegrals, r.Source)
        | Error e -> Assert.Fail($"provider-backed VQE should succeed: {e.Message}")

    [<Fact>]
    let ``Doublets run on STO-3G integrals and other open shells are Errors`` () =
        let hydrogenAtom =
            {
                Name = "H"
                Atoms =
                    [
                        {
                            Element = "H"
                            Position = (0.0, 0.0, 0.0)
                        }
                    ]
                Bonds = []
                Charge = 0
                Multiplicity = 2
            }

        // STO-3G hydrogen atom: E = -0.466582 Ha
        match energyOf hydrogenAtom (configWith None None) with
        | Ok r ->
            Assert.Equal(ComputedSto3gIntegrals, r.Source)
            Assert.True(abs (r.Energy - -0.466582) < 1e-5, $"H atom energy {r.Energy} Ha")
        | Error e -> Assert.Fail($"the H atom doublet should run: {e.Message}")

        // H2+ (one electron): exact energy is the lowest core-Hamiltonian orbital energy + E_nuc
        let cation =
            { Molecule.createH2 1.0 with
                Name = "H2+"
                Charge = 1
                Multiplicity = 2
            }

        match Sto3gIntegrals.compute cation, energyOf cation (configWith None None) with
        | Ok integrals, Ok r ->
            let h = integrals.OneElectron.Integrals
            let exact = min h.[0, 0] h.[1, 1] + integrals.NuclearRepulsion
            Assert.True(abs (r.Energy - exact) < chemicalAccuracy, $"H2+ VQE {r.Energy} Ha vs exact {exact} Ha")
        | a, b -> Assert.Fail($"H2+ should run: %A{a} / %A{b}")

        let tripletH2 =
            { Molecule.createH2 1.0 with
                Multiplicity = 3
            }

        match energyOf tripletH2 (configWith None None) with
        | Ok r -> Assert.Fail($"a triplet must not yield an energy, got {r.Energy} from {r.Source}")
        | Error e -> Assert.Contains("multiplicity 3", e.Message)

    /// LocalBackend whose states are SparseState, so energies are sampled rather than exact.
    type private SparseLocalBackend() =
        let inner = LocalBackend() :> IQuantumBackend
        let toSparse = QuantumStateConversion.convert QuantumStateType.Sparse
        let toDense = QuantumStateConversion.convert QuantumStateType.GateBased

        interface IQuantumBackend with
            member _.ExecuteToState circuit =
                inner.ExecuteToState circuit |> Result.bind toSparse

            member _.NativeStateType = QuantumStateType.Sparse

            member _.ApplyOperation operation state =
                toDense state
                |> Result.bind (inner.ApplyOperation operation)
                |> Result.bind toSparse

            member _.SupportsOperation operation = inner.SupportsOperation operation
            member _.Name = "Sparse local (sampled)"

            member _.InitializeState numQubits =
                inner.InitializeState numQubits |> Result.bind toSparse

            member this.ExecuteToStateAsync circuit _ =
                Task.FromResult((this :> IQuantumBackend).ExecuteToState circuit)

            member this.ApplyOperationAsync operation state _ =
                Task.FromResult((this :> IQuantumBackend).ApplyOperation operation state)

    [<Fact>]
    let ``Error mitigation on a sampled backend is applied or refused, never skipped`` () =
        let noisy = SparseLocalBackend() :> IQuantumBackend

        let strategy technique : ErrorMitigationStrategy.RecommendedStrategy =
            {
                Primary = technique
                Fallback = None
                Reasoning = "test"
                EstimatedCostMultiplier = 1.0
                EstimatedAccuracy = 1.0
            }

        let run mitigation =
            energyOf
                (Molecule.createH2 0.7414)
                { configWith (Some noisy) None with
                    MaxIterations = 1
                    ErrorMitigation = mitigation
                }

        // Readout mitigation without a calibration matrix corrects nothing: refused.
        match run (Some(strategy (ErrorMitigationStrategy.ReadoutErrorMitigation None))) with
        | Ok r -> Assert.Fail($"uncalibrated readout mitigation must be refused, got {r.Energy}")
        | Error e -> Assert.Contains("no correction", e.Message)

        // A strategy with a circuit-level component cannot be applied to a histogram: refused,
        // even when combined with readout correction.
        let zne =
            ErrorMitigationStrategy.ZeroNoiseExtrapolation ZeroNoiseExtrapolation.defaultIonQConfig

        let identity: ReadoutErrorMitigation.CalibrationMatrix =
            {
                Matrix = Array2D.init 16 16 (fun i j -> if i = j then 1.0 else 0.0)
                Qubits = 4
                Timestamp = System.DateTime.UtcNow
                Backend = "test"
                CalibrationShots = 1000
            }

        match
            run (
                Some(
                    strategy (
                        ErrorMitigationStrategy.Combined
                            [ zne; ErrorMitigationStrategy.ReadoutErrorMitigation(Some identity) ]
                    )
                )
            )
        with
        | Ok r -> Assert.Fail($"ZNE cannot be applied to a histogram, got {r.Energy}")
        | Error e -> Assert.Contains("ZNE", e.Message)

        // Without mitigation the sampled path runs and reports no mitigation.
        match run None with
        | Ok r ->
            Assert.True(r.Iterations > 0)
            Assert.False(r.ErrorMitigationApplied)
        | Error e -> Assert.Fail($"sampled UCCSD-VQE without mitigation should run: {e.Message}")

    /// LocalBackend whose measurements misread qubit `noisyQubit`: a 1 reads as 0 with
    /// probability p10 and a 0 as 1 with probability p01. States reach `measure` as diagonal
    /// density matrices of the misread distribution; the pure state behind each is kept aside.
    type private ReadoutNoiseBackend(noisyQubit: int, p01: float, p10: float) =
        let inner = LocalBackend() :> IQuantumBackend

        let pureStates =
            System.Collections.Generic.Dictionary<QuantumState, QuantumState>(HashIdentity.Reference)

        let wrap (state: QuantumState) =
            match state with
            | QuantumState.StateVector sv ->
                let probabilities =
                    FSharp.Azure.Quantum.LocalSimulator.Measurement.getProbabilityDistribution sv

                let n = FSharp.Azure.Quantum.LocalSimulator.StateVector.numQubits sv
                let dim = probabilities.Length
                let misread = Array.zeroCreate dim
                let bit = 1 <<< noisyQubit

                probabilities
                |> Array.iteri (fun i p ->
                    let flip = if i &&& bit = 0 then p01 else p10
                    misread.[i] <- misread.[i] + (1.0 - flip) * p
                    misread.[i ^^^ bit] <- misread.[i ^^^ bit] + flip * p)

                let rho =
                    Array2D.init dim dim (fun i j ->
                        if i = j then
                            System.Numerics.Complex(misread.[i], 0.0)
                        else
                            System.Numerics.Complex.Zero)

                let noisy = QuantumState.DensityMatrix(rho, n)
                pureStates.[noisy] <- state
                Ok noisy
            | other -> Error(QuantumError.OperationError("ReadoutNoiseBackend", $"unexpected state {other}"))

        interface IQuantumBackend with
            member _.ExecuteToState circuit = inner.ExecuteToState circuit
            member _.NativeStateType = QuantumStateType.Mixed

            member _.ApplyOperation operation state =
                inner.ApplyOperation operation pureStates.[state] |> Result.bind wrap

            member _.SupportsOperation operation = inner.SupportsOperation operation
            member _.Name = "Readout-noise local"

            member _.InitializeState numQubits =
                inner.InitializeState numQubits |> Result.bind wrap

            member this.ExecuteToStateAsync circuit _ =
                Task.FromResult((this :> IQuantumBackend).ExecuteToState circuit)

            member this.ApplyOperationAsync operation state _ =
                Task.FromResult((this :> IQuantumBackend).ApplyOperation operation state)

    /// Confusion matrix M[measured, prepared] of 4 qubits where only `qubit` misreads.
    let private singleQubitCalibration
        (qubit: int)
        (p01: float)
        (p10: float)
        : ReadoutErrorMitigation.CalibrationMatrix =
        let bit = 1 <<< qubit

        {
            Matrix =
                Array2D.init 16 16 (fun measured prepared ->
                    if (measured ^^^ prepared) &&& ~~~bit <> 0 then
                        0.0
                    elif prepared &&& bit = 0 then
                        (if measured = prepared then 1.0 - p01 else p01)
                    elif measured = prepared then
                        1.0 - p10
                    else
                        p10)
            Qubits = 4
            Timestamp = System.DateTime.UtcNow
            Backend = "test"
            CalibrationShots = 100000
        }

    [<Fact>]
    let ``Readout mitigation with the right single-qubit calibration recovers the noiseless energy`` () =
        // Qubit 0 is occupied in the H2 Hartree-Fock state |1100>, so misreading it biases <Z0>.
        let p01, p10 = 0.05, 0.35
        let hartreeFock = [| 0.0; 0.0; 0.0; 0.0; 0.0 |]

        let energyAt (backend: IQuantumBackend) mitigation =
            // MaxIterations = 0: the energy of the starting (Hartree-Fock) amplitudes.
            energyOf
                (Molecule.createH2 0.7414)
                { configWith (Some backend) None with
                    MaxIterations = 0
                    InitialParameters = Some hartreeFock
                    ErrorMitigation = mitigation
                }

        let readout calibration : ErrorMitigationStrategy.RecommendedStrategy =
            {
                Primary = ErrorMitigationStrategy.ReadoutErrorMitigation(Some calibration)
                Fallback = None
                Reasoning = "test"
                EstimatedCostMultiplier = 1.0
                EstimatedAccuracy = 1.0
            }

        let exact =
            match energyAt (LocalBackend()) None with
            | Ok r -> r.Energy
            | Error e -> failwith e.Message

        let noisy = ReadoutNoiseBackend(0, p01, p10) :> IQuantumBackend

        // Mean of several runs keeps the sampling error (1000 shots per Pauli term) small.
        let mean mitigation =
            [ 1..6 ]
            |> List.map (fun _ ->
                (energyAt noisy mitigation) |> Result.defaultWith (fun e -> failwith e.Message))
            |> fun runs ->
                runs |> List.averageBy (fun r -> r.Energy), runs |> List.forall (fun r -> r.ErrorMitigationApplied)

        let unmitigated, unmitigatedFlag = mean None

        let mitigated, mitigatedFlag =
            mean (Some(readout (singleQubitCalibration 0 p01 p10)))

        let wrongQubit, _ = mean (Some(readout (singleQubitCalibration 3 p01 p10)))

        Assert.False(unmitigatedFlag)
        Assert.True(mitigatedFlag, "mitigation on sampled measurements must be reported as applied")
        Assert.True(abs (unmitigated - exact) > 0.05, $"readout noise must bias the energy: {unmitigated} vs {exact}")
        Assert.True(abs (mitigated - exact) < 0.03, $"mitigated {mitigated} Ha must recover the noiseless {exact} Ha")
        Assert.True(abs (wrongQubit - exact) > 0.05, $"a calibration of the wrong qubit must not fix it: {wrongQubit}")

    [<Fact>]
    let ``Mitigation requested on a statevector backend reports that nothing was corrected`` () =
        let strategy: ErrorMitigationStrategy.RecommendedStrategy =
            {
                Primary = ErrorMitigationStrategy.ReadoutErrorMitigation None
                Fallback = None
                Reasoning = "test"
                EstimatedCostMultiplier = 1.0
                EstimatedAccuracy = 1.0
            }

        match
            energyOf
                (Molecule.createH2 0.7414)
                { configWith (Some(LocalBackend() :> IQuantumBackend)) None with
                    ErrorMitigation = Some strategy
                }
        with
        | Ok r ->
            Assert.False(r.ErrorMitigationApplied, "statevector expectations are exact: nothing to mitigate")
            Assert.True(abs (r.Energy - fciH2) < chemicalAccuracy)
        | Error e -> Assert.Fail e.Message

    /// Fails exactly one ApplyOperation call, the `failAt`-th (1-based).
    type private FailOnceBackend(inner: IQuantumBackend, failAt: int) =
        let mutable calls = 0

        interface IQuantumBackend with
            member _.ExecuteToState circuit = inner.ExecuteToState circuit
            member _.NativeStateType = inner.NativeStateType

            member _.ApplyOperation operation state =
                calls <- calls + 1

                if calls = failAt then
                    Error(QuantumError.OperationError("FailOnceBackend", $"injected failure at call {calls}"))
                else
                    inner.ApplyOperation operation state

            member _.SupportsOperation operation = inner.SupportsOperation operation
            member _.Name = inner.Name
            member _.InitializeState numQubits = inner.InitializeState numQubits
            member _.ExecuteToStateAsync circuit ct = inner.ExecuteToStateAsync circuit ct

            member _.ApplyOperationAsync operation state ct =
                inner.ApplyOperationAsync operation state ct

    [<Fact>]
    let ``A failed gradient evaluation is returned as Error`` () =
        let oneIteration backend =
            { configWith (Some backend) None with
                MaxIterations = 1
            }

        // One iteration = energy evaluation + one finite-difference evaluation per parameter;
        // the last operation belongs to the last gradient evaluation.
        let counting = CountingBackend(LocalBackend())

        match energyOf (Molecule.createH2 0.7414) (oneIteration (counting :> IQuantumBackend)) with
        | Error e -> Assert.Fail($"reference run failed: {e.Message}")
        | Ok _ ->
            let failing = FailOnceBackend(LocalBackend(), counting.Applied)

            match energyOf (Molecule.createH2 0.7414) (oneIteration (failing :> IQuantumBackend)) with
            | Ok r ->
                Assert.Fail($"a failed gradient evaluation must not be treated as a zero gradient, got {r.Energy}")
            | Error e -> Assert.Contains("injected failure", e.Message)

    [<Fact>]
    let ``Molecule wider than the NISQ qubit budget returns Error naming the limit`` () =
        // 11 spatial orbitals -> 22 spin orbitals, above Types.NisqPracticalQubits; nothing is simulated.
        let n = 11

        let tooLarge: IntegralProvider =
            fun _ ->
                Ok
                    {
                        NumOrbitals = n
                        NumElectrons = 2
                        NuclearRepulsion = 0.0
                        OneElectron =
                            {
                                NumOrbitals = n
                                Integrals = Array2D.zeroCreate n n
                            }
                        TwoElectron =
                            {
                                NumOrbitals = n
                                Integrals = Array4D.zeroCreate n n n n
                            }
                        ReferenceEnergy = None
                    }

        let backend = CountingBackend(LocalBackend())

        match energyOf (Molecule.createH2 0.7414) (configWith (Some(backend :> IQuantumBackend)) (Some tooLarge)) with
        | Ok r -> Assert.Fail($"22 qubits exceeds the budget, got {r.Energy} from {r.Source}")
        | Error e ->
            Assert.Contains($"max {Types.NisqPracticalQubits}", e.Message)
            Assert.Equal(0, backend.Applied)

    [<Fact>]
    let ``Backend qubit limit returns Error naming the backend limit`` () =
        let backend = QubitLimitedBackend(LocalBackend(), 2)

        match energyOf (Molecule.createH2 0.7414) (configWith (Some(backend :> IQuantumBackend)) None) with
        | Ok r -> Assert.Fail($"H2/STO-3G needs 4 qubits, backend runs 2; got {r.Energy} from {r.Source}")
        | Error e ->
            Assert.Contains("runs at most 2", e.Message)
            Assert.Equal(0, backend.Applied)

    [<Fact>]
    let ``Automatic method on H2O returns Error instead of the tabulated reference`` () =
        let config =
            { configWith None None with
                Method = GroundStateMethod.Automatic
            }

        match energyOf (Molecule.createH2O ()) config with
        | Ok r -> Assert.Fail($"Automatic must not substitute a tabulated energy, got {r.Energy} from {r.Source}")
        | Error e -> Assert.Contains("IntegralProvider", e.Message)

    [<Fact>]
    let ``Initial parameters of the wrong length return Error`` () =
        let config =
            { configWith None None with
                InitialParameters = Some [| 0.1; 0.2; 0.3 |]
            }

        match energyOf (Molecule.createH2 0.7414) config with
        | Ok r -> Assert.Fail($"UCCSD for H2 takes 5 parameters; 3 must be rejected, got {r.Energy}")
        | Error e -> Assert.Contains("takes 5 parameters", e.Message)

/// FCIDUMP integrals (FciDumpIntegrals / MoleculeFormats.FciDump.parseIntegrals) feed VQE.
module FciDumpIntegralTests =
    open System.IO

    /// MolecularHamiltonian.h2Sto3gIntegrals as pyscf.tools.fcidump.from_integrals writes them:
    /// unique (ij|kl) with i >= j, k >= l, ij >= kl, then h_ij with i >= j, then the core energy.
    let private h2Fcidump =
        String.concat
            "\n"
            [
                " &FCI NORB=   2,NELEC= 2,MS2=0,"
                "  ORBSYM=1,1,"
                "  ISYM=1,"
                " &END"
                "  0.674493 1 1 1 1"
                "  0.181287 2 1 2 1"
                "  0.663472 2 2 1 1"
                "  0.697398 2 2 2 2"
                " -1.252477 1 1 0 0"
                " -0.475934 2 2 0 0"
                "  0.713754 0 0 0 0"
                ""
            ]

    let private bundled = MolecularHamiltonian.h2Sto3gIntegrals

    let private assertMatchesBundled (integrals: MolecularIntegrals) =
        Assert.Equal(bundled.NumOrbitals, integrals.NumOrbitals)
        Assert.Equal(bundled.NumElectrons, integrals.NumElectrons)
        Assert.Equal(bundled.NuclearRepulsion, integrals.NuclearRepulsion, 12)

        for p in 0..1 do
            for q in 0..1 do
                Assert.Equal(bundled.OneElectron.Integrals.[p, q], integrals.OneElectron.Integrals.[p, q], 12)

                for r in 0..1 do
                    for s in 0..1 do
                        Assert.Equal(
                            bundled.TwoElectron.Integrals.[p, q, r, s],
                            integrals.TwoElectron.Integrals.[p, q, r, s],
                            12
                        )

    let private vqeConfig (provider: IntegralProvider option) =
        {
            Method = GroundStateMethod.VQE
            MaxIterations = 100
            Tolerance = 1e-6
            InitialParameters = None
            Backend = None
            ProgressReporter = None
            ErrorMitigation = None
            IntegralProvider = provider
        }

    [<Fact>]
    let ``FCIDUMP integrals match the bundled H2 integrals with the 8-fold symmetry filled`` () =
        match FciDumpIntegrals.parse h2Fcidump with
        | Ok integrals -> assertMatchesBundled integrals
        | Error e -> Assert.Fail($"parse failed: {e.Message}")

    [<Fact>]
    let ``Molpro-style and one-line FCIDUMP headers with Fortran exponents parse the same`` () =
        let body =
            [
                "  0.674493D+00 1 1 1 1"
                "  0.181287D+00 1 2 1 2"
                "  0.663472d0 1 1 2 2"
                "  0.697398E+00 2 2 2 2"
                " -1.252477 1 1 0 0"
                " -0.475934 2 2 0 0"
                " -0.578554 1 0 0 0" // orbital energy line: no Hamiltonian term
                "  0.713754 0 0 0 0"
            ]

        let molpro =
            String.concat "\n" ([ " &FCI NORB=2,NELEC=2,MS2=0,"; "  ORBSYM=1,1,"; "  ISYM=1"; " /" ] @ body)

        let oneLine =
            String.concat "\r\n" ([ "&FCI NORB=2,NELEC=2,MS2=0,ORBSYM=1,1,ISYM=1,&END" ] @ body)

        for content in [ molpro; oneLine ] do
            match FciDumpIntegrals.parse content with
            | Ok integrals -> assertMatchesBundled integrals
            | Error e -> Assert.Fail($"parse failed: {e.Message}\n{content}")

    [<Fact>]
    let ``VQE on an FCIDUMP IntegralProvider matches the bundled-integral VQE`` () =
        let path =
            Path.Combine(Path.GetTempPath(), $"h2-sto3g-{System.Guid.NewGuid():N}.fcidump")

        File.WriteAllText(path, h2Fcidump)

        try
            let h2 = Molecule.createH2 0.7414

            let run provider =
                GroundStateEnergy.estimateEnergy h2 (vqeConfig provider)
                |> Async.RunSynchronously

            match run (Some(fun _ -> Ok bundled)), run (Some(FciDumpIntegrals.fromFile path)) with
            | Ok fromBundled, Ok fromFcidump ->
                Assert.Equal(ProviderIntegrals, fromFcidump.Source)
                Assert.True(fromFcidump.Iterations > 0, "VQE must iterate")

                Assert.True(
                    abs (fromFcidump.Energy - fromBundled.Energy) < 1e-9,
                    $"FCIDUMP energy {fromFcidump.Energy} must equal bundled-integral energy {fromBundled.Energy}"
                )

                Assert.True(
                    abs (fromFcidump.Energy - -1.13727) < 0.0016,
                    $"FCIDUMP VQE energy {fromFcidump.Energy} Ha must be within chemical accuracy of FCI -1.13727 Ha"
                )
            | a, b -> Assert.Fail($"both runs should succeed: %A{a} / %A{b}")
        finally
            File.Delete path

    [<Fact>]
    let ``Malformed FCIDUMP content returns Error`` () =
        let header = " &FCI NORB=2,NELEC=2,MS2=0,\n  ORBSYM=1,1,\n  ISYM=1,\n &END\n"

        let cases =
            [
                "no header", "  0.674493 1 1 1 1\n", "No FCIDump header"
                "no end marker", " &FCI NORB=2,NELEC=2,MS2=0,\n  0.674493 1 1 1 1\n", "end marker"
                "index above NORB", header + "  0.5 3 1 1 1\n", "line 5"
                "non-numeric value", header + "  abc 1 1 1 1\n", "not a number"
                "missing index", header + "  0.5 1 1 1\n", "expected 'value i j k l'"
                "bad index pattern", header + "  0.5 1 0 1 0\n", "index pattern"
                "unrestricted", " &FCI NORB=2,NELEC=2,MS2=0,IUHF=1,\n &END\n", "IUHF"
                "NELEC too large", " &FCI NORB=2,NELEC=5,MS2=0,\n &END\n", "NELEC=5"
            ]

        for name, content, expected in cases do
            match FciDumpIntegrals.parse content with
            | Ok _ -> Assert.Fail($"{name}: malformed FCIDUMP must not parse")
            | Error e -> Assert.True(e.Message.Contains expected, $"{name}: expected '{expected}' in '{e.Message}'")

    [<Fact>]
    let ``Missing FCIDUMP file surfaces as a VQE Error`` () =
        task {
            let path =
                Path.Combine(Path.GetTempPath(), $"missing-{System.Guid.NewGuid():N}.fcidump")

            match!
                GroundStateEnergy.estimateEnergy
                     (Molecule.createH2 0.7414)
                     (vqeConfig (Some(FciDumpIntegrals.fromFile path)))
                 |> Async.StartImmediateAsTask
            with
            | Ok r -> Assert.Fail($"a missing FCIDUMP must not yield an energy, got {r.Energy} from {r.Source}")
            | Error e -> Assert.Contains("File not found", e.Message)
        } :> Task

    [<Fact>]
    let ``Builder molecule_from_fcidump runs VQE on the file's integrals`` () =
        let path = Path.Combine(Path.GetTempPath(), $"h2-{System.Guid.NewGuid():N}.fcidump")
        File.WriteAllText(path, h2Fcidump)

        try
            let problem =
                QuantumChemistryBuilder.quantumChemistry {
                    molecule_from_fcidump path
                    basis "sto-3g"
                    ansatz QuantumChemistryBuilder.UCCSD
                }

            match QuantumChemistryBuilder.solve problem |> Async.RunSynchronously with
            | Ok r ->
                Assert.Equal(ProviderIntegrals, r.Source)

                Assert.True(
                    abs (r.GroundStateEnergy - -1.13727) < 0.0016,
                    $"FCIDUMP VQE energy {r.GroundStateEnergy} Ha must be within chemical accuracy of FCI"
                )
            | Error e -> Assert.Fail($"solve on an FCIDUMP should succeed: {e.Message}")
        finally
            File.Delete path

    [<Fact>]
    let ``Builder integralProvider supplies the integrals`` () =
        task {
            let shifted: IntegralProvider =
                fun _ ->
                    Ok
                        { bundled with
                            NuclearRepulsion = bundled.NuclearRepulsion + 1.0
                        }

            let problem =
                QuantumChemistryBuilder.quantumChemistry {
                    molecule (QuantumChemistryBuilder.h2 0.7414)
                    basis "sto-3g"
                    ansatz QuantumChemistryBuilder.UCCSD
                    integralProvider shifted
                }

            match! QuantumChemistryBuilder.solve problem |> Async.StartImmediateAsTask with
            | Ok r ->
                Assert.Equal(ProviderIntegrals, r.Source)

                Assert.True(
                    abs (r.GroundStateEnergy - (-1.13735 + 1.0)) < 0.0016,
                    $"shifted provider energy {r.GroundStateEnergy} Ha"
                )
            | Error e -> Assert.Fail($"solve with integralProvider should succeed: {e.Message}")
        } :> Task

/// STO-3G integrals computed by the library (Sto3gIntegrals) and nuclear repulsion.
module Sto3gIntegralTests =

    /// Exact ground state of minimal-basis H2: the 2×2 CI between the HF determinant and the
    /// doubly excited one (singles do not couple by symmetry), plus nuclear repulsion.
    let private h2Fci (i: MolecularIntegrals) =
        let h = i.OneElectron.Integrals
        let g = i.TwoElectron.Integrals
        let e0 = 2.0 * h.[0, 0] + g.[0, 0, 0, 0]
        let e1 = 2.0 * h.[1, 1] + g.[1, 1, 1, 1]
        let k = g.[0, 1, 0, 1]
        (e0 + e1) / 2.0 - sqrt (((e0 - e1) / 2.0) ** 2.0 + k * k) + i.NuclearRepulsion

    let private computeOk molecule =
        (Sto3gIntegrals.compute molecule) |> Result.defaultWith (fun e -> failwith e.Message)

    [<Fact>]
    let ``Computed H2 integrals at 0.7414 A match the bundled reference integrals`` () =
        let computed = computeOk (Molecule.createH2 0.7414)
        let bundled = MolecularHamiltonian.h2Sto3gIntegrals

        // The bundled values carry six decimals and a slightly different bohr conversion.
        let tolerance = 5e-5

        for p in 0..1 do
            for q in 0..1 do
                Assert.True(
                    abs (computed.OneElectron.Integrals.[p, q] - bundled.OneElectron.Integrals.[p, q]) < tolerance,
                    $"h[{p},{q}]: {computed.OneElectron.Integrals.[p, q]} vs {bundled.OneElectron.Integrals.[p, q]}"
                )

                for r in 0..1 do
                    for s in 0..1 do
                        let c = computed.TwoElectron.Integrals.[p, q, r, s]
                        let b = bundled.TwoElectron.Integrals.[p, q, r, s]
                        Assert.True(abs (c - b) < tolerance, $"({p}{q}|{r}{s}): {c} vs {b}")

        Assert.True(abs (computed.NuclearRepulsion - bundled.NuclearRepulsion) < 1e-4)
        Assert.True(abs (computed.ReferenceEnergy.Value - bundled.ReferenceEnergy.Value) < 1e-4)

    [<Theory; InlineData(0.7414, -1.1372702); InlineData(1.0, -1.1011503); InlineData(2.0, -0.9486411)>]
    let ``Computed H2 integrals reproduce the STO-3G FCI energy`` (bondLength: float, fci: float) =
        let energy = h2Fci (computeOk (Molecule.createH2 bondLength))
        Assert.True(abs (energy - fci) < 1e-6, $"R = {bondLength} A: FCI {energy} Ha, reference {fci} Ha")

    [<Theory>]
    [<InlineData(0.5)>]
    [<InlineData(0.7414)>]
    [<InlineData(1.0)>]
    [<InlineData(1.5)>]
    [<InlineData(2.0)>]
    [<InlineData(2.5)>]
    let ``UCCSD-VQE reaches the H2 STO-3G FCI energy within 1e-4 Ha`` (bondLength: float) =
        task {
            let h2 = Molecule.createH2 bondLength
            let fci = h2Fci (computeOk h2)

            let config =
                {
                    Method = GroundStateMethod.VQE
                    MaxIterations = 100
                    Tolerance = 1e-6
                    InitialParameters = None
                    Backend = None
                    ProgressReporter = None
                    ErrorMitigation = None
                    IntegralProvider = None
                }

            match! GroundStateEnergy.estimateEnergy h2 config |> Async.StartImmediateAsTask with
            | Ok r ->
                Assert.True(r.Converged, $"R = {bondLength} A: not converged after {r.Iterations} iterations")

                Assert.True(
                    abs (r.Energy - fci) < 1e-4,
                    $"R = {bondLength} A: VQE {r.Energy} Ha vs FCI {fci} Ha ({r.Iterations} iterations)"
                )
            | Error e -> Assert.Fail($"R = {bondLength} A: {e.Message}")
        } :> Task

    [<Fact>]
    let ``Helium atom STO-3G Hartree-Fock energy`` () =
        let helium =
            {
                Name = "He"
                Atoms =
                    [
                        {
                            Element = "He"
                            Position = (0.0, 0.0, 0.0)
                        }
                    ]
                Bonds = []
                Charge = 0
                Multiplicity = 1
            }

        let integrals = computeOk helium
        Assert.Equal(1, integrals.NumOrbitals)
        // STO-3G He (zeta = 1.69): E_HF = -2.807784 Ha
        Assert.True(abs (integrals.ReferenceEnergy.Value - -2.807784) < 1e-5, $"{integrals.ReferenceEnergy}")

    [<Fact>]
    let ``Non-H/He, open-shell and empty molecules are Errors`` () =
        let atom e =
            {
                Element = e
                Position = (0.0, 0.0, 0.0)
            }

        let molecule atoms multiplicity =
            {
                Name = "m"
                Atoms = atoms
                Bonds = []
                Charge = 0
                Multiplicity = multiplicity
            }

        for m, expected in
            [
                Molecule.createH2O (), "H and He only"
                molecule [ atom "H" ] 1, "doublets"
                molecule [] 1, "no atoms"
            ] do
            match Sto3gIntegrals.compute m with
            | Ok _ -> Assert.Fail($"expected Error containing '{expected}'")
            | Error e -> Assert.Contains(expected, e.Message)

    [<Fact>]
    let ``Nuclear repulsion sums all atom pairs in bohr`` () =
        let h at = { Element = "H"; Position = at }

        let triangle =
            {
                Name = "H3+"
                Atoms = [ h (0.0, 0.0, 0.0); h (1.0, 0.0, 0.0); h (0.5, sqrt 3.0 / 2.0, 0.0) ]
                Bonds = []
                Charge = 1
                Multiplicity = 1
            }

        // Three H-H pairs at 1 Å: 3 / 1.8897261 bohr
        match Molecule.nuclearRepulsion triangle with
        | Ok e -> Assert.Equal(3.0 / Molecule.BohrPerAngstrom, e, 10)
        | Error e -> Assert.Fail e.Message

        // Water: O-H 0.96 Å... summed over all three pairs, Z_O = 8
        let water = Molecule.createH2O ()

        let expected =
            [
                for i in 0 .. water.Atoms.Length - 2 do
                    for j in i + 1 .. water.Atoms.Length - 1 ->
                        let zi = if water.Atoms.[i].Element = "O" then 8.0 else 1.0
                        let zj = if water.Atoms.[j].Element = "O" then 8.0 else 1.0

                        zi * zj
                        / (Molecule.calculateBondLength water.Atoms.[i] water.Atoms.[j]
                           * Molecule.BohrPerAngstrom)
            ]
            |> List.sum

        match Molecule.nuclearRepulsion water with
        | Ok e -> Assert.Equal(expected, e, 10)
        | Error e -> Assert.Fail e.Message

        let unknown =
            {
                Name = "X2"
                Atoms =
                    [
                        h (0.0, 0.0, 0.0)
                        {
                            Element = "Xx"
                            Position = (1.0, 0.0, 0.0)
                        }
                    ]
                Bonds = []
                Charge = 0
                Multiplicity = 1
            }

        match Molecule.nuclearRepulsion unknown with
        | Ok e -> Assert.Fail($"unknown element must be an Error, got {e}")
        | Error e -> Assert.Contains("Unknown element", e.Message)

/// The bundled FCIDUMP files the chemistry examples run on (examples/_data/chemistry/fcidump,
/// written by PySCF): every file parses, fits 16 qubits, and UCCSD-VQE on the CAS(2,2) ones
/// reproduces the CASSCF energy recorded in manifest.json.
module BundledFciDumpTests =
    open System.IO
    open System.Text.Json

    let private directory =
        Path.Combine(__SOURCE_DIRECTORY__, "..", "..", "examples", "_data", "chemistry", "fcidump")

    let private manifest () =
        use document =
            JsonDocument.Parse(File.ReadAllText(Path.Combine(directory, "manifest.json")))

        [
            for s in document.RootElement.GetProperty("species").EnumerateArray() ->
                s.GetProperty("file").GetString(),
                s.GetProperty("active_orbitals").GetInt32(),
                s.GetProperty("e_cas").GetDouble()
        ]

    [<Fact>]
    let ``Every bundled FCIDUMP parses and fits 16 qubits`` () =
        let entries = manifest ()
        Assert.True(entries.Length >= 30, $"expected the bundled species, found {entries.Length}")

        for file, orbitals, _ in entries do
            match FciDumpIntegrals.readFile (Path.Combine(directory, file)) with
            | Ok integrals ->
                Assert.Equal(orbitals, integrals.NumOrbitals)
                Assert.True(2 * integrals.NumOrbitals <= 16, $"{file}: {2 * integrals.NumOrbitals} qubits")
            | Error e -> Assert.Fail($"{file}: {e.Message}")

    [<Fact>]
    let ``UCCSD-VQE on the bundled CAS(2,2) FCIDUMPs reproduces the PySCF CASSCF energies`` () =
        for file, _, eCas in manifest () |> List.filter (fun (_, orbitals, _) -> orbitals = 2) do
            let config =
                {
                    Method = GroundStateMethod.VQE
                    MaxIterations = 100
                    Tolerance = 1e-8
                    InitialParameters = None
                    Backend = None
                    ProgressReporter = None
                    ErrorMitigation = None
                    IntegralProvider = Some(FciDumpIntegrals.fromFile (Path.Combine(directory, file)))
                }

            match
                GroundStateEnergy.estimateEnergy (Molecule.createH2 0.74) config
                |> Async.RunSynchronously
            with
            | Ok r ->
                Assert.Equal(ProviderIntegrals, r.Source)
                Assert.True(abs (r.Energy - eCas) < 1e-5, $"{file}: VQE {r.Energy} Ha vs PySCF CASSCF {eCas} Ha")
            | Error e -> Assert.Fail($"{file}: {e.Message}")

/// Admission rules and basis support of the computed-integral and provider paths.
module ChemistryAdmissionTests =
    open System.Numerics
    open MathNet.Numerics.LinearAlgebra
    open FSharp.Azure.Quantum.Core.QaoaCircuit

    let private atom element (x, y, z) =
        {
            Element = element
            Position = (x, y, z)
        }

    let private molecule name atoms charge multiplicity =
        {
            Name = name
            Atoms = atoms
            Bonds = []
            Charge = charge
            Multiplicity = multiplicity
        }

    let private config provider =
        {
            Method = GroundStateMethod.VQE
            MaxIterations = 100
            Tolerance = 1e-6
            InitialParameters = None
            Backend = None
            ProgressReporter = None
            ErrorMitigation = None
            IntegralProvider = provider
        }

    let private run m provider =
        GroundStateEnergy.estimateEnergy m (config provider) |> Async.RunSynchronously

    /// Lowest eigenvalue of the Jordan-Wigner Hamiltonian of `integrals` among states with
    /// `electrons` electrons, plus nuclear repulsion: an FCI independent of the VQE code.
    let private sectorFci (integrals: MolecularIntegrals) (electrons: int) =
        match MolecularHamiltonian.buildFromIntegrals integrals MolecularHamiltonian.JordanWigner with
        | Error e -> failwith e.Message
        | Ok(hamiltonian, nuclearRepulsion) ->
            let q = FermionMapping.fromQaoaHamiltonian hamiltonian
            let dim = 1 <<< q.NumQubits
            let h = Matrix<Complex>.Build.Dense(dim, dim)

            for term in q.Terms do
                for b in 0 .. dim - 1 do
                    let mutable target = b
                    let mutable phase = Complex.One

                    for KeyValue(qubit, op) in term.Operators do
                        let bit = (b >>> qubit) &&& 1

                        match op with
                        | PauliOperator.PauliX -> target <- target ^^^ (1 <<< qubit)
                        | PauliOperator.PauliY ->
                            target <- target ^^^ (1 <<< qubit)

                            phase <-
                                phase
                                * (if bit = 0 then
                                       Complex.ImaginaryOne
                                   else
                                       -Complex.ImaginaryOne)
                        | PauliOperator.PauliZ ->
                            if bit = 1 then
                                phase <- -phase
                        | PauliOperator.PauliI -> ()

                    h.[target, b] <- h.[target, b] + term.Coefficient * phase

            let sector =
                [|
                    for b in 0 .. dim - 1 do
                        if System.Numerics.BitOperations.PopCount(uint32 b) = electrons then
                            b
                |]

            let block =
                Matrix<Complex>.Build.Dense(sector.Length, sector.Length, fun i j -> h.[sector.[i], sector.[j]])

            (block.Evd(Symmetricity.Hermitian).EigenValues
             |> Seq.map (fun z -> z.Real)
             |> Seq.min)
            + nuclearRepulsion

    let private computeOk basis m =
        (Sto3gIntegrals.computeInBasis basis m) |> Result.defaultWith (fun e -> failwith e.Message)

    [<Fact>]
    let ``6-31G H2 and He integrals reproduce PySCF RHF and FCI energies`` () =
        // PySCF 2.14, basis 6-31g: H2 at 0.7414 A RHF -1.1267339671, FCI -1.1516827321;
        // H2 at 1.5 A RHF -0.9974972943, FCI -1.0543474460; He RHF -2.8551604262, FCI -2.8701621389.
        for r, rhf, fci in [ 0.7414, -1.1267339671, -1.1516827321; 1.5, -0.9974972943, -1.0543474460 ] do
            let integrals = computeOk "6-31G" (Molecule.createH2 r)
            Assert.Equal(4, integrals.NumOrbitals)
            Assert.True(abs (integrals.ReferenceEnergy.Value - rhf) < 1e-6, $"R = {r}: RHF {integrals.ReferenceEnergy}")
            let own = sectorFci integrals 2
            Assert.True(abs (own - fci) < 1e-6, $"R = {r}: FCI {own} vs PySCF {fci}")

        let helium = computeOk "6-31g" (molecule "He" [ atom "He" (0.0, 0.0, 0.0) ] 0 1)
        Assert.True(abs (helium.ReferenceEnergy.Value - -2.8551604262) < 1e-6, $"He RHF {helium.ReferenceEnergy}")
        Assert.True(abs (sectorFci helium 2 - -2.8701621389) < 1e-6)

    [<Fact>]
    let ``Unsupported basis for computed integrals is an Error naming the supported ones`` () =
        match Sto3gIntegrals.computeInBasis "cc-pVDZ" (Molecule.createH2 0.74) with
        | Ok _ -> Assert.Fail("cc-pVDZ is not computed by the library")
        | Error e ->
            Assert.Contains("STO-3G, 6-31G", e.Message)
            Assert.Contains("carry their own basis", e.Message)

    [<Fact; Trait("Category", "Slow")>]
    let ``Builder basis selects the computed integrals`` () =
        let solve basisName =
            QuantumChemistryBuilder.quantumChemistry {
                molecule (QuantumChemistryBuilder.h2 0.7414)
                basis basisName
                ansatz QuantumChemistryBuilder.UCCSD
            }
            |> QuantumChemistryBuilder.solve
            |> Async.RunSynchronously

        match solve "6-31g" with
        | Ok r ->
            Assert.Equal(Computed631gIntegrals, r.Source)
            Assert.True(abs (r.GroundStateEnergy - -1.1516827321) < 1e-3, $"6-31G VQE {r.GroundStateEnergy} Ha")
        | Error e -> Assert.Fail($"6-31G should run: {e.Message}")

        match solve "cc-pvdz" with
        | Ok r ->
            Assert.Fail($"cc-pVDZ must not silently run in another basis, got {r.GroundStateEnergy} from {r.Source}")
        | Error e -> Assert.Contains("6-31G", e.Message)

    [<Fact>]
    let ``RHF converges for stretched hydrogen chains and rings`` () =
        // PySCF 2.14 RHF/STO-3G references
        let chain n d =
            molecule $"H{n}" [ for i in 0 .. n - 1 -> atom "H" (0.0, 0.0, float i * d) ] 0 1

        let ring n r =
            molecule
                $"H{n} ring"
                [
                    for i in 0 .. n - 1 ->
                        let a = 2.0 * System.Math.PI * float i / float n
                        atom "H" (r * cos a, r * sin a, 0.0)
                ]
                0
                1

        for m, reference in
            [
                chain 4 2.5, -1.4097529967
                ring 6 2.0, -2.4171767632
                chain 8 2.0, -3.1614329658
            ] do
            match Sto3gIntegrals.compute m with
            | Ok i ->
                Assert.True(
                    abs (i.ReferenceEnergy.Value - reference) < 1e-6,
                    $"{m.Name}: RHF {i.ReferenceEnergy} vs {reference}"
                )
            | Error e -> Assert.Fail($"{m.Name}: {e.Message}")

    [<Fact>]
    let ``UCCSD above the parameter limit is an Error before any VQE runs`` () =
        // H6 chain: 6 electrons in 12 spin orbitals -> 261 UCCSD parameters
        let h6 = molecule "H6" [ for i in 0..5 -> atom "H" (0.0, 0.0, float i * 0.9) ] 0 1

        let watch = System.Diagnostics.Stopwatch.StartNew()

        match run h6 None with
        | Ok r -> Assert.Fail($"H6 UCCSD must be refused, got {r.Energy}")
        | Error e ->
            Assert.Contains("261 parameters", e.Message)
            Assert.Contains("active space", e.Message)

        Assert.True(watch.Elapsed.TotalSeconds < 10.0, $"refusal took {watch.Elapsed.TotalSeconds} s")

    [<Fact>]
    let ``Spin states UCCSD cannot honour are Errors on both paths`` () =
        // Triplet FCIDUMP (MS2=2) for two electrons
        let tripletFcidump =
            String.concat
                "\n"
                [
                    " &FCI NORB=2,NELEC=2,MS2=2,"
                    " &END"
                    "  0.674493 1 1 1 1"
                    " -1.252477 1 1 0 0"
                    "  0.713754 0 0 0 0"
                    ""
                ]

        match FciDumpIntegrals.parse tripletFcidump with
        | Ok _ -> Assert.Fail("an MS2=2 FCIDUMP must not be read as a singlet")
        | Error e -> Assert.Contains("MS2=2", e.Message)

        // Provider integrals for a closed shell, molecule asking for a triplet
        let triplet =
            { Molecule.createH2 0.7414 with
                Multiplicity = 3
            }

        match run triplet (Some(fun _ -> Ok MolecularHamiltonian.h2Sto3gIntegrals)) with
        | Ok r -> Assert.Fail($"a triplet request must not return the singlet energy {r.Energy}")
        | Error e -> Assert.Contains("multiplicity 3", e.Message)

        // Three-electron doublet: not honoured by either path
        let h3 =
            molecule "H3" [ atom "H" (0.0, 0.0, 0.0); atom "H" (0.0, 0.0, 0.8); atom "H" (0.3, 0.0, 2.1) ] 0 2

        match run h3 None with
        | Ok r -> Assert.Fail($"H3 doublet must be refused, got {r.Energy}")
        | Error e -> Assert.Contains("one-electron doublets", e.Message)

    [<Fact>]
    let ``ClassicalDFT is deterministic and matches by composition`` () =
        let classical m =
            GroundStateEnergy.estimateEnergyWith GroundStateMethod.ClassicalDFT m (config None)
            |> Async.RunSynchronously

        let renamed =
            { Molecule.createH2 0.74 with
                Name = "hydrogen from file"
            }

        match classical renamed, classical renamed with
        | Ok a, Ok b ->
            Assert.Equal(-1.174, a.Energy)
            Assert.Equal(a.Energy, b.Energy)
            Assert.Equal(TabulatedReference, a.Source)
        | a, b -> Assert.Fail($"%A{a} / %A{b}")

        // Name alone does not match: an "H2"-named water is water
        match
            classical
                { Molecule.createH2O () with
                    Name = "H2"
                }
        with
        | Ok r -> Assert.Equal(-76.0, r.Energy)
        | Error e -> Assert.Fail e.Message

    [<Fact>]
    let ``ClassicalDFT refuses states its table does not describe`` () =
        let classical m =
            GroundStateEnergy.estimateEnergyWith GroundStateMethod.ClassicalDFT m (config None)
            |> Async.RunSynchronously

        let h2 = Molecule.createH2 0.7414

        for m in
            [
                { h2 with Multiplicity = 3 }
                { h2 with Charge = 1; Multiplicity = 2 }
                { h2 with
                    Charge = -1
                    Multiplicity = 2
                }
                Molecule.createH2 3.0
                Molecule.createLiH 2.5
            ] do
            match classical m with
            | Ok r -> Assert.Fail($"charge {m.Charge}, multiplicity {m.Multiplicity}: got the table value {r.Energy}")
            | Error e -> Assert.Contains("not the state the table describes", e.Message)

        match classical (Molecule.createLiH 1.595) with
        | Ok r -> Assert.Equal(-8.0, r.Energy)
        | Error e -> Assert.Fail e.Message

    [<Fact>]
    let ``RHF finds the lowest solution where DIIS from the core guess stops higher`` () =
        // Lowest RHF energies from a seeded random-start damped SCF search; DIIS from the core
        // guess alone converged 27-72 mHa above each of them.
        let square r =
            [
                atom "H" (0.0, 0.0, 0.0)
                atom "H" (r, 0.0, 0.0)
                atom "H" (r, r, 0.0)
                atom "H" (0.0, r, 0.0)
            ]

        for basis, atoms, lowestFound in
            [
                "STO-3G",
                [
                    atom "H" (1.0, 1.0, 1.0)
                    atom "H" (1.0, -1.0, -1.0)
                    atom "H" (-1.0, 1.0, -1.0)
                    atom "H" (-1.0, -1.0, 1.0)
                ],
                -1.3331964
                "STO-3G", square 1.1, -1.7825512
                "6-31G", square 0.9, -1.8785183
                "6-31G", square 1.1, -1.9305384
                "6-31G", square 1.6, -1.8830182
                "6-31G", square 2.0, -1.7997037
                "6-31G",
                [
                    atom "H" (2.331157145, 0.5847571711, 1.955643376)
                    atom "H" (1.270237073, 0.2770959727, 0.7752167204)
                    atom "H" (2.164644011, 0.9132653852, 0.08800534768)
                    atom "H" (1.693053147, 0.5344625379, 0.6890876373)
                ],
                -1.8300455
            ] do
            let integrals = computeOk basis (molecule "H4" atoms 0 1)

            Assert.True(
                integrals.ReferenceEnergy.Value < lowestFound + 1e-6,
                $"{basis} {atoms}: RHF {integrals.ReferenceEnergy.Value} above the lowest found {lowestFound}"
            )

    [<Fact>]
    let ``UCCSD size limit is checked before the qubit Hamiltonian is built`` () =
        // 10 electrons in 10 orbitals: 2125 parameters; building its 20-qubit Hamiltonian is slow.
        let n = 10

        let wide: IntegralProvider =
            fun _ ->
                Ok
                    {
                        NumOrbitals = n
                        NumElectrons = n
                        NuclearRepulsion = 0.0
                        OneElectron =
                            {
                                NumOrbitals = n
                                Integrals = Array2D.init n n (fun i j -> if i = j then -1.0 + 0.1 * float i else 0.01)
                            }
                        TwoElectron =
                            {
                                NumOrbitals = n
                                Integrals = Array4D.init n n n n (fun p q r s -> if p = q && r = s then 0.5 else 0.001)
                            }
                        ReferenceEnergy = None
                    }

        let watch = System.Diagnostics.Stopwatch.StartNew()

        match run (molecule "X" [ atom "C" (0.0, 0.0, 0.0) ] 0 1) (Some wide) with
        | Ok r -> Assert.Fail($"2125 UCCSD parameters must be refused, got {r.Energy}")
        | Error e -> Assert.Contains("MaxUccsdParameters", e.Message)

        Assert.True(watch.Elapsed.TotalSeconds < 3.0, $"refusal took {watch.Elapsed.TotalSeconds} s")

/// UCCSD-VQE on backends that run only whole circuits (cloud hardware).
module WholeCircuitUccsdTests =
    open System
    open FSharp.Azure.Quantum.Core.BackendAbstraction
    open FSharp.Azure.Quantum.LocalSimulator
    open FSharp.Azure.Quantum.QuantumChemistry.FermionMapping

    /// Refuses incremental ApplyOperation, like cloud hardware. ExecuteToState runs the circuit
    /// on the local simulator and, with shots > 0, returns the frequencies of `shots` seeded
    /// samples as a cloud backend does (reporting Shots); with shots = 0 the exact state.
    type private WholeCircuitBackend(shots: int, seed: int) =
        let inner = Backends.LocalBackend.LocalBackend() :> IQuantumBackend
        let rng = Random seed

        let incremental: Result<QuantumState, QuantumError> =
            Error(
                QuantumError.OperationError(
                    "ApplyOperation",
                    "WholeCircuit does not support incremental ApplyOperation. Use ExecuteToState with a complete circuit instead."
                )
            )

        member val Executed = 0 with get, set

        interface IShotSamplingBackend with
            member _.Shots = shots

        interface IQuantumBackend with
            member this.ExecuteToState circuit =
                this.Executed <- this.Executed + 1

                match inner.ExecuteToState circuit with
                | Ok(QuantumState.StateVector sv) when shots > 0 ->
                    let p = Measurement.getProbabilityDistribution sv
                    let n = StateVector.numQubits sv
                    let cumulative = Array.scan (+) 0.0 p |> Array.tail
                    let counts = Array.zeroCreate p.Length

                    for _ in 1..shots do
                        let u = rng.NextDouble()
                        let k = defaultArg (Array.tryFindIndex (fun c -> u < c) cumulative) (p.Length - 1)
                        counts.[k] <- counts.[k] + 1

                    // Azure histogram keys: rightmost character = qubit 0.
                    let histogram =
                        counts
                        |> Array.mapi (fun i c -> Convert.ToString(i, 2).PadLeft(n, '0'), c)
                        |> Array.filter (fun (_, c) -> c > 0)
                        |> Map.ofArray

                    Ok(Backends.CloudBackendHelpers.histogramToQuantumState histogram n)
                | other -> other

            member _.NativeStateType = inner.NativeStateType
            member _.ApplyOperation _ _ = incremental
            member _.SupportsOperation _ = true
            member _.Name = "whole-circuit test backend"
            member _.InitializeState n = inner.InitializeState n

            member this.ExecuteToStateAsync circuit _ =
                Task.FromResult((this :> IQuantumBackend).ExecuteToState circuit)

            member _.ApplyOperationAsync _ _ _ = Task.FromResult incremental

    let private h2 = Molecule.createH2 0.7414

    let private h2Integrals =
        (Sto3gIntegrals.compute h2) |> Result.defaultWith (fun e -> failwith e.Message)

    let private qubitHamiltonian (integrals: MolecularIntegrals) =
        match MolecularHamiltonian.buildFromIntegrals integrals MolecularHamiltonian.JordanWigner with
        | Ok(h, _) -> fromQaoaHamiltonian h
        | Error e -> failwith e.Message

    let private config backend maxIterations =
        {
            Method = GroundStateMethod.VQE
            MaxIterations = maxIterations
            Tolerance = 1e-6
            InitialParameters = None
            Backend = Some backend
            ProgressReporter = None
            ErrorMitigation = None
            IntegralProvider = None
        }

    let private exactVqe () =
        match
            VQE.run
                h2
                { config (Backends.LocalBackend.LocalBackend() :> IQuantumBackend) 100 with
                    Backend = None
                }
            |> Async.RunSynchronously
        with
        | Ok r -> r
        | Error e -> failwith e.Message

    [<Fact>]
    let ``Measurement groups cover every term once and qubit-wise commute`` () =
        let ethane =
            System.IO.Path.Combine(
                __SOURCE_DIRECTORY__,
                "..",
                "..",
                "examples",
                "_data",
                "chemistry",
                "fcidump",
                "ethane-cas-4-4.fcidump"
            )

        let ethaneIntegrals =
            (FciDumpIntegrals.readFile ethane) |> Result.defaultWith (fun e -> failwith e.Message)

        for integrals, expectedGroups in [ h2Integrals, Some 5; ethaneIntegrals, None ] do
            let hamiltonian = qubitHamiltonian integrals
            let groups = ChemistryVQE.measurementGroups hamiltonian

            let measured =
                hamiltonian.Terms |> List.filter (fun t -> t.Coefficient.Magnitude >= 1e-10)

            let grouped = groups |> List.collect (fun g -> g.Terms)
            Assert.Equal(measured.Length, grouped.Length)

            for term in measured do
                Assert.Equal(1, grouped |> List.filter (fun t -> obj.ReferenceEquals(t, term)) |> List.length)

            for group in groups do
                for term in group.Terms do
                    for KeyValue(q, p) in term.Operators do
                        if p <> QaoaCircuit.PauliI then
                            Assert.Equal(p, group.Basis.[q])

            expectedGroups |> Option.iter (fun n -> Assert.Equal(n, groups.Length))

    [<Fact>]
    let ``Sampled expectation measures X and Y with the right sign`` () =
        let single pauli coefficient : QubitHamiltonian =
            {
                NumQubits = 1
                Terms =
                    [
                        {
                            Coefficient = Numerics.Complex(coefficient, 0.0)
                            Operators = Map [ 0, pauli ]
                        }
                    ]
            }

        let exact = WholeCircuitBackend(0, 1) :> IQuantumBackend

        // RX(-π/2)|0⟩ = |+i⟩: ⟨Y⟩ = +1.  H|0⟩ = |+⟩: ⟨X⟩ = +1.
        for preparation, pauli in
            [
                CircuitBuilder.empty 1
                |> CircuitBuilder.addGate (CircuitBuilder.RX(0, -Math.PI / 2.0)),
                QaoaCircuit.PauliY
                CircuitBuilder.empty 1 |> CircuitBuilder.addGate (CircuitBuilder.H 0), QaoaCircuit.PauliX
            ] do
            match ChemistryVQE.sampledExpectation exact None preparation (single pauli 0.5) with
            | Ok e -> Assert.Equal(0.5, e.Energy, 12)
            | Error e -> Assert.Fail e.Message

    [<Fact>]
    let ``Whole-circuit UCCSD energy equals the gate-by-gate energy at the same amplitudes`` () =
        let hamiltonian = qubitHamiltonian h2Integrals
        let parameters = [| 0.03; -0.02; 0.01; 0.04; -0.1 |]

        let gateByGate =
            {
                Hamiltonian = hamiltonian
                Ansatz = ChemistryVQE.UCCSD(2, 4)
                MaxIterations = 0
                Tolerance = 1e-8
                UseHFInitialState = true
                Backend = Backends.LocalBackend.LocalBackend() :> IQuantumBackend
                ProgressReporter = None
            }
            : ChemistryVQE.ChemistryVQEConfig

        let exactEnergy =
            match ChemistryVQE.runWith (Some parameters) None gateByGate |> Async.RunSynchronously with
            | Ok r ->
                Assert.Equal(ExactExpectation, r.Estimation)
                r.Energy
            | Error e -> failwith e.Message

        match
            ChemistryVQE.uccsdCircuit 2 4 parameters
            |> Result.bind (fun circuit ->
                ChemistryVQE.sampledExpectation (WholeCircuitBackend(0, 1) :> IQuantumBackend) None circuit hamiltonian)
        with
        | Ok e ->
            Assert.Equal(exactEnergy, e.Energy, 10)
            Assert.Equal(None, e.StandardError)
        | Error e -> Assert.Fail e.Message

    [<Fact>]
    let ``Exported UCCSD measurement circuits round-trip through OpenQASM`` () =
        let hamiltonian = qubitHamiltonian h2Integrals

        let ansatz =
            match ChemistryVQE.uccsdCircuit 2 4 [| 0.03; -0.02; 0.01; 0.04; -0.1 |] with
            | Ok c -> c
            | Error e -> failwith e.Message

        let backend = Backends.LocalBackend.LocalBackend() :> IQuantumBackend

        let probabilities circuit =
            match backend.ExecuteToState(CircuitAbstraction.wrapCircuit circuit) with
            | Ok(QuantumState.StateVector sv) -> Measurement.getProbabilityDistribution sv
            | other -> failwith $"%A{other}"

        for group in ChemistryVQE.measurementGroups hamiltonian do
            let circuit = ChemistryVQE.measurementCircuit ansatz group
            let qasm = OpenQasmExport.export circuit

            match OpenQasmImport.parse qasm with
            | Error e -> Assert.Fail $"re-import failed: {e}\n{qasm}"
            | Ok parsed ->
                Assert.Equal(circuit.QubitCount, parsed.QubitCount)
                Assert.Equal(CircuitBuilder.gateCount circuit, CircuitBuilder.gateCount parsed)
                let expected = probabilities circuit
                let actual = probabilities parsed

                for i in 0 .. expected.Length - 1 do
                    Assert.Equal(expected.[i], actual.[i], 9)

    [<Fact>]
    let ``H2 UCCSD-VQE on a sampled whole-circuit backend reaches FCI within shot noise`` () =
        // Tolerance: the reported energy is one sampled estimate, so it may fall 4 standard
        // errors either side of the true energy at the returned amplitudes; that energy lies
        // at or above FCI by the optimiser's residual, allowed up to chemical accuracy.
        task {
            let shots = 20000
            let fci = -1.1372701 // UCCSD = FCI for H2/STO-3G; the exact-path VQE below agrees
            Assert.Equal(fci, (exactVqe ()).Energy, 6)

            let backend = WholeCircuitBackend(shots, 7)

            match! VQE.run h2 (config (backend :> IQuantumBackend) 200) |> Async.StartImmediateAsTask with
            | Ok r ->
                match r.Estimation with
                | SampledCircuits(circuitsPerEnergy, shotsPerCircuit, executed) ->
                    Assert.Equal(5, circuitsPerEnergy)
                    Assert.Equal(Some shots, shotsPerCircuit)
                    Assert.Equal(backend.Executed, executed)
                | other -> Assert.Fail $"expected whole-circuit sampling, got %A{other}"

                Assert.Equal(ComputedSto3gIntegrals, r.Source)

                let standardError =
                    match
                        ChemistryVQE.uccsdCircuit 2 4 r.OptimalParameters
                        |> Result.bind (fun circuit ->
                            ChemistryVQE.sampledExpectation
                                (WholeCircuitBackend(shots, 11) :> IQuantumBackend)
                                None
                                circuit
                                (qubitHamiltonian h2Integrals))
                    with
                    | Ok e -> e.StandardError.Value
                    | Error e -> failwith e.Message

                let chemicalAccuracy = 1.6e-3

                Assert.True(
                    r.Energy > fci - 4.0 * standardError
                    && r.Energy < fci + chemicalAccuracy + 4.0 * standardError,
                    $"sampled VQE {r.Energy} Ha vs FCI {fci} (standard error {standardError}, {r.Iterations} iterations)"
                )
            | Error e -> Assert.Fail e.Message
        } :> Task

    [<Fact>]
    let ``H2 UCCSD-VQE on the noiseless density-matrix backend reaches FCI`` () =
        task {
            let backend =
                Backends.DensityMatrixSimulator.NoisyLocalBackend(Backends.DensityMatrixSimulator.noiseless)

            match! VQE.run h2 (config (backend :> IQuantumBackend) 200) |> Async.StartImmediateAsTask with
            | Ok r ->
                match r.Estimation with
                | SampledCircuits(5, None, _) -> ()
                | other -> Assert.Fail $"expected whole circuits with exact probabilities, got %A{other}"

                Assert.True(r.Converged)
                Assert.True(abs (r.Energy - (exactVqe ()).Energy) < 1.6e-3, $"{r.Energy} Ha")
            | Error e -> Assert.Fail e.Message
        } :> Task

    let private bundled name =
        System.IO.Path.Combine(
            __SOURCE_DIRECTORY__,
            "..",
            "..",
            "examples",
            "_data",
            "chemistry",
            "fcidump",
            name + ".fcidump"
        )

    let private placeholder =
        {
            Name = "active space"
            Atoms =
                [
                    {
                        Element = "C"
                        Position = (0.0, 0.0, 0.0)
                    }
                ]
            Bonds = []
            Charge = 0
            Multiplicity = 1
        }

    let private providerConfig (file: string) backend maxIterations =
        { config backend maxIterations with
            IntegralProvider = Some(FciDumpIntegrals.fromFile file)
        }

    /// Exact energy (nuclear repulsion included) of the UCCSD state at `parameters`.
    let private trueEnergy (integrals: MolecularIntegrals) (parameters: float[]) =
        match
            ChemistryVQE.uccsdCircuit integrals.NumElectrons (2 * integrals.NumOrbitals) parameters
            |> Result.bind (fun circuit ->
                ChemistryVQE.sampledExpectation
                    (WholeCircuitBackend(0, 1) :> IQuantumBackend)
                    None
                    circuit
                    (qubitHamiltonian integrals))
        with
        | Ok e -> e.Energy + integrals.NuclearRepulsion
        | Error e -> failwith e.Message

    [<Fact>]
    let ``Sampled UCCSD-VQE never reports Converged above the noise`` () =
        // Water CAS(2,2) at 1000 shots: SPSA's Converged means no improvement above the shot
        // noise for 30 iterations, so a converged run's parameters must lie within
        // Tolerance + 3 standard errors of one energy estimate of the minimum.
        let file = bundled "water-cas-2-2"

        let integrals =
            (FciDumpIntegrals.readFile file) |> Result.defaultWith (fun e -> failwith e.Message)

        let minimum =
            match
                VQE.run placeholder (providerConfig file (Backends.LocalBackend.LocalBackend() :> IQuantumBackend) 100)
                |> Async.RunSynchronously
            with
            | Ok r -> r.Energy
            | Error e -> failwith e.Message

        for seed in 1..6 do
            let backend = WholeCircuitBackend(1000, seed) :> IQuantumBackend

            match VQE.run placeholder (providerConfig file backend 200) |> Async.RunSynchronously with
            | Error e -> Assert.Fail e.Message
            | Ok r ->
                let standardError =
                    match
                        ChemistryVQE.uccsdCircuit 2 4 r.OptimalParameters
                        |> Result.bind (fun circuit ->
                            ChemistryVQE.sampledExpectation backend None circuit (qubitHamiltonian integrals))
                    with
                    | Ok e -> e.StandardError.Value
                    | Error e -> failwith e.Message

                let above = trueEnergy integrals r.OptimalParameters - minimum

                if r.Converged then
                    Assert.True(
                        above <= 1e-6 + 3.0 * standardError,
                        $"seed {seed}: Converged {above * 1000.0:F2} mHa above the minimum (standard error {standardError * 1000.0:F2} mHa)"
                    )
                else
                    Assert.NotEmpty(r.Notes)

    [<Fact>]
    let ``Whole-circuit UCCSD-VQE refuses a plan over the circuit budget before running`` () =
        // Ethane CAS(4,4): 52 amplitudes, 100 measurement circuits per energy; 200 SPSA
        // iterations need over 40000 circuits.
        let backend = WholeCircuitBackend(0, 1)
        let watch = System.Diagnostics.Stopwatch.StartNew()

        match
            VQE.run placeholder (providerConfig (bundled "ethane-cas-4-4") (backend :> IQuantumBackend) 200)
            |> Async.RunSynchronously
        with
        | Ok r -> Assert.Fail $"expected the budget refusal, got {r.Energy}"
        | Error e ->
            Assert.Contains("MaxWholeCircuitJobs", e.Message)
            Assert.Equal(0, backend.Executed)

        Assert.True(watch.Elapsed.TotalSeconds < 10.0)

    [<Fact; Trait("Category", "Slow")>]
    let ``Whole-circuit UCCSD-VQE on 52 amplitudes never ends above its start`` () =
        // A few SPSA steps on ethane CAS(4,4) cannot reach the minimum; the first steps are
        // calibrated small (0.05/sqrt 52 per amplitude), so the run must not climb above the
        // starting energy, and must say it did not converge.
        task {
            let file = bundled "ethane-cas-4-4"

            let integrals =
                (FciDumpIntegrals.readFile file) |> Result.defaultWith (fun e -> failwith e.Message)

            let start =
                trueEnergy integrals (let rng = Random 42 in Array.init 52 (fun _ -> (rng.NextDouble() - 0.5) * 0.01))

            match!
                VQE.run placeholder (providerConfig file (WholeCircuitBackend(0, 1) :> IQuantumBackend) 10)
                 |> Async.StartImmediateAsTask
            with
            | Error e -> Assert.Fail e.Message
            | Ok r ->
                Assert.False(r.Converged)
                Assert.NotEmpty(r.Notes)
                Assert.True(trueEnergy integrals r.OptimalParameters <= start + 1e-9, $"{r.Energy} Ha vs start {start} Ha")
        } :> Task

    /// A whole-circuit backend whose circuits each take `seconds`.
    type private SlowWholeCircuitBackend(seconds: float) =
        let inner = WholeCircuitBackend(0, 1) :> IQuantumBackend
        member val Executed = 0 with get, set

        interface IQuantumBackend with
            member this.ExecuteToState circuit =
                this.Executed <- this.Executed + 1
                Threading.Thread.Sleep(TimeSpan.FromSeconds seconds)
                inner.ExecuteToState circuit

            member _.NativeStateType = inner.NativeStateType
            member _.ApplyOperation operation state = inner.ApplyOperation operation state
            member _.SupportsOperation operation = inner.SupportsOperation operation
            member _.Name = "slow whole-circuit test backend"
            member _.InitializeState n = inner.InitializeState n

            member this.ExecuteToStateAsync circuit _ =
                Task.FromResult((this :> IQuantumBackend).ExecuteToState circuit)

            member _.ApplyOperationAsync operation state ct =
                inner.ApplyOperationAsync operation state ct

    [<Fact>]
    let ``A slow local whole-circuit simulator is refused after its first circuit`` () =
        // 2 s per circuit x 2151 planned circuits is over the one-hour simulation budget.
        task {
            let backend = SlowWholeCircuitBackend(2.0)

            match! VQE.run h2 (config (backend :> IQuantumBackend) 200) |> Async.StartImmediateAsTask with
            | Ok r -> Assert.Fail $"expected the time refusal, got {r.Energy}"
            | Error e ->
                Assert.Contains("planned circuits would take", e.Message)
                Assert.Equal(1, backend.Executed)
        } :> Task

    [<Fact>]
    let ``Cancellation stops a whole-circuit run`` () =
        task {
            let cancelled =
                { new Progress.IProgressReporter with
                    member _.Report _ = ()
                    member _.IsCancellationRequested = true
                }

            match!
                VQE.run
                     h2
                     { config (WholeCircuitBackend(0, 1) :> IQuantumBackend) 200 with
                         ProgressReporter = Some cancelled
                     }
                 |> Async.StartImmediateAsTask
            with
            | Ok r -> Assert.Fail $"expected cancellation, got {r.Energy}"
            | Error e -> Assert.Contains("cancelled", e.Message)
        } :> Task

    [<Fact>]
    let ``UCCSD circuit applies exp(T - Tdagger) with the usual amplitude sign`` () =
        // Only the double excitation: its eight Pauli strings commute, so the Trotter product
        // is exact and the circuit must equal e^(T - T†)|HF⟩.
        let parameters = [| 0.0; 0.0; 0.0; 0.0; 0.2 |]

        let cluster =
            match UCCSD.generateExcitationPool 2 4 parameters with
            | Ok pool -> UCCSD.toQubitHamiltonian pool 4 true
            | Error e -> failwith e

        let dim = 16

        let generator =
            MathNet.Numerics.LinearAlgebra.Matrix<Numerics.Complex>.Build.Dense(dim, dim)

        for term in cluster.Terms do
            for col in 0 .. dim - 1 do
                let mutable row = col
                let mutable phase = Numerics.Complex(term.Coefficient.Imaginary, 0.0)

                for KeyValue(q, p) in term.Operators do
                    let bit = (col >>> q) &&& 1

                    match p with
                    | QaoaCircuit.PauliX -> row <- row ^^^ (1 <<< q)
                    | QaoaCircuit.PauliY ->
                        row <- row ^^^ (1 <<< q)

                        phase <-
                            phase
                            * (if bit = 0 then
                                   Numerics.Complex.ImaginaryOne
                               else
                                   -Numerics.Complex.ImaginaryOne)
                    | QaoaCircuit.PauliZ ->
                        if bit = 1 then
                            phase <- -phase
                    | QaoaCircuit.PauliI -> ()

                generator.[row, col] <- generator.[row, col] + phase

        // T - T† = i·B with B = Σ Im(c) P Hermitian: e^(T - T†) = e^(iB).
        let evd = generator.Evd MathNet.Numerics.LinearAlgebra.Symmetricity.Hermitian

        let unitary =
            evd.EigenVectors
            * MathNet.Numerics.LinearAlgebra.Matrix<Numerics.Complex>.Build.DiagonalOfDiagonalArray(
                evd.EigenValues.ToArray()
                |> Array.map (fun e -> Numerics.Complex.Exp(Numerics.Complex(0.0, e.Real)))
            )
            * evd.EigenVectors.ConjugateTranspose()

        let expected = unitary.Column 3 // |HF⟩ = qubits 0 and 1 occupied

        match
            ChemistryVQE.uccsdCircuit 2 4 parameters
            |> Result.bind (fun circuit ->
                (Backends.LocalBackend.LocalBackend() :> IQuantumBackend)
                    .ExecuteToState(CircuitAbstraction.wrapCircuit circuit))
        with
        | Ok(QuantumState.StateVector sv) ->
            for i in 0 .. dim - 1 do
                let a = StateVector.getAmplitude i sv
                Assert.True((a - expected.[i]).Magnitude < 1e-9, $"amplitude {i}: {a} vs {expected.[i]}")
        | other -> Assert.Fail $"%A{other}"

/// Quantum phase estimation of molecular Hamiltonians (QPE.run).
module ChemistryQpeTests =
    open System
    open System.Numerics
    open MathNet.Numerics.LinearAlgebra
    open FSharp.Azure.Quantum.Core.BackendAbstraction
    open FSharp.Azure.Quantum.LocalSimulator
    open FSharp.Azure.Quantum.Algorithms.TrotterSuzuki

    // PySCF-independent references: FCI of the library's STO-3G integrals.
    [<Literal>]
    let private fciEquilibrium = -1.1372701 // H2 at 0.7414 Å
    [<Literal>]
    let private fciStretched = -0.9486411 // H2 at 2.0 Å
    [<Literal>]
    let private excitedStretched = -0.3764321 // the doubly excited singlet the HF state overlaps at 2.0 Å

    let private config backend =
        {
            Method = GroundStateMethod.QPE
            MaxIterations = 100
            Tolerance = 1e-6
            InitialParameters = None
            Backend = backend
            ProgressReporter = None
            ErrorMitigation = None
            IntegralProvider = None
        }

    let private details (r: VQE.VQEResult) =
        match r.Estimation with
        | PhaseEstimation d -> d
        | other -> failwith $"expected PhaseEstimation, got %A{other}"

    let private pauliHamiltonian (r: float) =
        match Sto3gIntegrals.compute (Molecule.createH2 r) with
        | Error e -> failwith e.Message
        | Ok integrals ->
            match MolecularHamiltonian.buildFromIntegrals integrals MolecularHamiltonian.JordanWigner with
            | Ok(h, _) -> QPE.toPauliHamiltonian h
            | Error e -> failwith e.Message

    /// Matrix of a Pauli string on `n` qubits (qubit q = bit q of the basis index).
    let private pauliMatrix (n: int) (term: PauliString) =
        let dim = 1 <<< n
        let m = Matrix<Complex>.Build.Dense(dim, dim)

        for col in 0 .. dim - 1 do
            let mutable row = col
            let mutable phase = Complex.One

            for q in 0 .. n - 1 do
                let bit = (col >>> q) &&& 1

                match term.Operators.[q] with
                | 'X' -> row <- row ^^^ (1 <<< q)
                | 'Y' ->
                    row <- row ^^^ (1 <<< q)

                    phase <-
                        phase
                        * (if bit = 0 then
                               Complex.ImaginaryOne
                           else
                               -Complex.ImaginaryOne)
                | 'Z' ->
                    if bit = 1 then
                        phase <- -phase
                | _ -> ()

            m.[row, col] <- m.[row, col] + phase * term.Coefficient

        m

    [<Fact>]
    let ``H2 QPE from Hartree-Fock reaches FCI within chemical accuracy`` () =
        // Tolerance 1.6 mHa: the first-order Trotter error of the ground eigenvalue at the
        // default 4 steps is 0.7 mHa, and the peak refinement recovers the phase between bins.
        task {
            match!
                GroundStateEnergy.estimateEnergy (Molecule.createH2 0.7414) (config None)
                 |> Async.StartImmediateAsTask
            with
            | Error e -> Assert.Fail e.Message
            | Ok r ->
                Assert.Equal(QpeTrotterEvolution, r.Source)
                let d = details r
                Assert.Equal(8, d.CountingQubits)
                Assert.Equal(1, d.TrotterOrder)
                Assert.Equal(4, d.TrotterStepsPerEvolution)
                Assert.Equal(None, d.ShotsPerCircuit)
                Assert.True(abs (r.Energy - fciEquilibrium) < 1.6e-3, $"QPE {r.Energy} Ha vs FCI {fciEquilibrium}")
                // The bin centre alone is within half a bin
                Assert.True(abs (d.PeakBinEnergy - fciEquilibrium) < d.BinWidth / 2.0 + 1.6e-3)
                // HF overlaps the ground state with probability 0.987
                Assert.True(d.Peaks.Head.Probability > 0.95, $"%A{d.Peaks}")
        } :> Task

    [<Fact>]
    let ``Controlled Trotter evolution matches the exact exp(-iHt) to the Trotter bound`` () =
        let hamiltonian = pauliHamiltonian 0.7414
        let plan = QPE.evolutionPlan hamiltonian 8 1 4
        let n = hamiltonian.NumQubits
        let control = n
        let dim = 1 <<< (n + 1)
        let backend = Backends.LocalBackend.LocalBackend() :> IQuantumBackend

        let controlled =
            synthesizeControlledHamiltonianEvolution
                control
                plan.ShiftedHamiltonian
                plan.Trotter
                [| 0 .. n - 1 |]
                (CircuitBuilder.empty (n + 1))

        // Columns of the circuit's unitary, one basis state at a time.
        let circuitMatrix = Matrix<Complex>.Build.Dense(dim, dim)

        for col in 0 .. dim - 1 do
            let preparation =
                [
                    for q in 0..n do
                        if (col >>> q) &&& 1 = 1 then
                            CircuitBuilder.X q
                ]

            let circuit =
                CircuitBuilder.empty (n + 1)
                |> CircuitBuilder.addGates (preparation @ CircuitBuilder.getGates controlled)

            match backend.ExecuteToState(CircuitAbstraction.wrapCircuit circuit) with
            | Ok(QuantumState.StateVector sv) ->
                for row in 0 .. dim - 1 do
                    circuitMatrix.[row, col] <- StateVector.getAmplitude row sv
            | other -> failwith $"%A{other}"

        let h = plan.ShiftedHamiltonian.Terms |> List.map (pauliMatrix n) |> List.reduce (+)

        let evd = h.Evd Symmetricity.Hermitian

        let exact =
            evd.EigenVectors
            * Matrix<Complex>
                .Build.DiagonalOfDiagonalArray(
                    evd.EigenValues.ToArray()
                    |> Array.map (fun e -> Complex.Exp(Complex(0.0, -e.Real * plan.Time)))
                )
            * evd.EigenVectors.ConjugateTranspose()

        let half = 1 <<< n
        let idle = circuitMatrix.SubMatrix(0, half, 0, half)
        let active = circuitMatrix.SubMatrix(half, half, half, half)

        Assert.True((idle - Matrix<Complex>.Build.DenseIdentity half).L2Norm() < 1e-9, "control |0>: identity")
        Assert.True(circuitMatrix.SubMatrix(0, half, half, half).L2Norm() < 1e-9)

        // First-order Trotter: ||U_trotter - U|| <= t^2/(2s) Σ_{i<j} ||[H_i, H_j]||, and two
        // Pauli strings commute or anticommute (||[c_i P_i, c_j P_j]|| = 2|c_i c_j|).
        let terms = plan.ShiftedHamiltonian.Terms |> Array.ofList

        let anticommute (a: PauliString) (b: PauliString) =
            Array.map2 (fun x y -> x <> 'I' && y <> 'I' && x <> y) a.Operators b.Operators
            |> Array.filter id
            |> Array.length
            |> fun count -> count % 2 = 1

        let commutators =
            [
                for i in 0 .. terms.Length - 1 do
                    for j in i + 1 .. terms.Length - 1 do
                        if anticommute terms.[i] terms.[j] then
                            2.0 * abs (terms.[i].Coefficient.Real * terms.[j].Coefficient.Real)
            ]
            |> List.sum

        let bound =
            plan.Time * plan.Time / (2.0 * float plan.Trotter.NumSteps) * commutators

        let error = (active - exact).L2Norm()
        Assert.True(error <= bound, $"||U_trotter - U|| = {error} > bound {bound}")
        Assert.True(error > 0.0)

    [<Fact>]
    let ``Phase and energy map both ways across the spectral bound`` () =
        let hamiltonian = pauliHamiltonian 0.7414
        let plan = QPE.evolutionPlan hamiltonian 8 1 4

        let lambda =
            plan.ShiftedHamiltonian.Terms
            |> List.sumBy (fun t ->
                if t.Operators |> Array.forall ((=) 'I') then
                    0.0
                else
                    abs t.Coefficient.Real)

        for fraction in [ 0.0; 0.1; 0.5; 0.9; 1.0 ] do
            let energy = plan.Shift - 2.0 * lambda * fraction
            let phase = QPE.energyToPhase plan energy
            Assert.InRange(phase, 0.0, 1.0 - 2.0 / 256.0 + 1e-12)
            Assert.Equal(energy, QPE.phaseToEnergy plan phase, 10)

    [<Fact>]
    let ``Stretched H2 shows the overlap caveat as a second peak`` () =
        // At 2.0 Å the Hartree-Fock state overlaps the ground state with probability 0.71
        // and the doubly excited singlet with 0.29: QPE returns either eigenvalue.
        task {
            match!
                QPE.runWith
                     { QPE.defaultSettings with
                         CountingQubits = Some 6
                     }
                     (Molecule.createH2 2.0)
                     (config None)
                 |> Async.StartImmediateAsTask
            with
            | Error e -> Assert.Fail e.Message
            | Ok r ->
                let d = details r
                Assert.Equal(2, d.Peaks.Length)
                let ground, excited = d.Peaks.[0], d.Peaks.[1]
                Assert.True(abs (ground.Energy - fciStretched) < 1.6e-3, $"%A{d.Peaks}")
                Assert.True(abs (excited.Energy - excitedStretched) < 5e-3, $"%A{d.Peaks}")
                Assert.InRange(ground.Probability, 0.6, 0.8)
                Assert.InRange(excited.Probability, 0.2, 0.35)
                Assert.Equal(ground.Energy, r.Energy)
                Assert.Contains(r.Notes, fun note -> note.Contains "Other peaks")
        } :> Task

    /// Cloud-style backend: refuses incremental ApplyOperation and returns the frequencies of
    /// `shots` seeded samples of each whole circuit.
    type private SampledWholeCircuitBackend(shots: int, seed: int) =
        let inner = Backends.LocalBackend.LocalBackend() :> IQuantumBackend
        let rng = Random seed

        let incremental: Result<QuantumState, QuantumError> =
            Error(
                QuantumError.OperationError(
                    "ApplyOperation",
                    "SampledWholeCircuit does not support incremental ApplyOperation. Use ExecuteToState with a complete circuit instead."
                )
            )

        member val Executed = 0 with get, set

        interface IShotSamplingBackend with
            member _.Shots = shots

        interface IQuantumBackend with
            member this.ExecuteToState circuit =
                this.Executed <- this.Executed + 1

                match inner.ExecuteToState circuit with
                | Ok(QuantumState.StateVector sv) ->
                    let p = Measurement.getProbabilityDistribution sv
                    let n = StateVector.numQubits sv
                    let cumulative = Array.scan (+) 0.0 p |> Array.tail
                    let counts = Collections.Generic.Dictionary<int, int>()

                    for _ in 1..shots do
                        let u = rng.NextDouble()
                        let k = Array.BinarySearch(cumulative, u)
                        let k = min (p.Length - 1) (if k >= 0 then k + 1 else ~~~k)

                        counts.[k] <-
                            (match counts.TryGetValue k with
                             | true, c -> c
                             | _ -> 0)
                            + 1

                    let histogram =
                        counts
                        |> Seq.map (fun kv -> Convert.ToString(kv.Key, 2).PadLeft(n, '0'), kv.Value)
                        |> Map.ofSeq

                    Ok(Backends.CloudBackendHelpers.histogramToQuantumState histogram n)
                | other -> other

            member _.NativeStateType = inner.NativeStateType
            member _.ApplyOperation _ _ = incremental
            member _.SupportsOperation _ = true
            member _.Name = "sampled whole-circuit test backend"
            member _.InitializeState n = inner.InitializeState n

            member this.ExecuteToStateAsync circuit _ =
                Threading.Tasks.Task.FromResult((this :> IQuantumBackend).ExecuteToState circuit)

            member _.ApplyOperationAsync _ _ _ =
                Threading.Tasks.Task.FromResult incremental

    [<Fact>]
    let ``H2 QPE runs as one whole circuit on a sampling cloud-style backend`` () =
        // 20000 shots: the refinement reads the peak and its neighbour, whose counts carry
        // shot noise; 3 mHa allows it beside the 0.7 mHa Trotter error.
        task {
            let backend = SampledWholeCircuitBackend(20000, 3)

            match!
                QPE.runWith
                     { QPE.defaultSettings with
                         CountingQubits = Some 6
                     }
                     (Molecule.createH2 0.7414)
                     (config (Some(backend :> IQuantumBackend)))
                 |> Async.StartImmediateAsTask
            with
            | Error e -> Assert.Fail e.Message
            | Ok r ->
                Assert.Equal(1, backend.Executed)
                let d = details r
                Assert.Equal(Some 20000, d.ShotsPerCircuit)
                Assert.True(abs (r.Energy - fciEquilibrium) < 3e-3, $"QPE {r.Energy} Ha vs FCI {fciEquilibrium}")
        } :> Task

    [<Fact>]
    let ``QPE needs integrals and a counting register`` () =
        task {
            let water = Molecule.createH2O ()

            match QPE.run water (config None) |> Async.RunSynchronously with
            | Ok r -> Assert.Fail $"H2O without integrals must be refused, got {r.Energy} from {r.Source}"
            | Error e -> Assert.Contains("IntegralProvider", e.Message)

            match!
                QPE.runWith
                     { QPE.defaultSettings with
                         CountingQubits = Some 13
                     }
                     (Molecule.createH2 0.7414)
                     (config None)
                 |> Async.StartImmediateAsTask
            with
            | Ok _ -> Assert.Fail "4 + 13 qubits exceed 16"
            | Error e -> Assert.Contains("exceed 16", e.Message)
        } :> Task

    [<Fact>]
    let ``Chemistry builder runs QPE when asked`` () =
        task {
            let problem =
                QuantumChemistryBuilder.quantumChemistry {
                    molecule (QuantumChemistryBuilder.h2 2.0)
                    basis "sto-3g"
                    groundStateMethod GroundStateMethod.QPE
                }

            match! QuantumChemistryBuilder.solve problem |> Async.StartImmediateAsTask with
            | Error e -> Assert.Fail e.Message
            | Ok r ->
                Assert.Equal(QpeTrotterEvolution, r.Source)
                Assert.True(abs (r.GroundStateEnergy - fciStretched) < 1.6e-3, $"{r.GroundStateEnergy}")

                match r.Estimation with
                | PhaseEstimation d -> Assert.Equal(8, d.CountingQubits)
                | other -> Assert.Fail $"%A{other}"
        } :> Task

/// Hamiltonian simulation and empirical-Hamiltonian VQE on backends that run only whole circuits.
module WholeCircuitEmpiricalAndTrotterTests =
    open FSharp.Azure.Quantum.Core.BackendAbstraction
    open FSharp.Azure.Quantum.LocalSimulator

    /// H = X₀ on one qubit: from |0⟩, P(1) after time t is sin²(t).
    let private xHamiltonian: QaoaCircuit.ProblemHamiltonian =
        {
            NumQubits = 1
            Terms =
                [|
                    {
                        Coefficient = 1.0
                        QubitsIndices = [| 0 |]
                        PauliOperators = [| QaoaCircuit.PauliX |]
                    }
                |]
        }

    let private simulationConfig (backend: IQuantumBackend) : HamiltonianSimulation.SimulationConfig =
        {
            Time = 0.6
            TrotterSteps = 4
            TrotterOrder = 1
            Backend = Some backend
        }

    [<Fact>]
    let ``simulateFromPreparation is exact gate by gate on the local simulator`` () =
        let backend = Backends.LocalBackend.LocalBackend() :> IQuantumBackend

        match
            HamiltonianSimulation.simulateFromPreparation
                xHamiltonian
                (CircuitBuilder.empty 1)
                (simulationConfig backend)
        with
        | Ok r ->
            Assert.Equal(HamiltonianSimulation.GateByGate, r.Route)
            Assert.True(r.FinalState.IsSome)
            Assert.Equal(sin 0.6 ** 2.0, r.Probabilities.[1], 9)
        | Error e -> failwith e.Message

    [<Fact>]
    let ``simulateFromPreparation submits preparation and Trotter gates whole to a sampling backend`` () =
        let backend = SampledWholeCircuit.Backend(8000, 21)

        // Preparation X₀ starts from |1⟩: P(0) after time t is sin²(t).
        let preparation =
            CircuitBuilder.empty 1 |> CircuitBuilder.addGate (CircuitBuilder.X 0)

        match HamiltonianSimulation.simulateFromPreparation xHamiltonian preparation (simulationConfig backend) with
        | Ok r ->
            Assert.Equal(HamiltonianSimulation.WholeCircuit(Some 8000), r.Route)
            Assert.True(r.FinalState.IsNone)
            Assert.Equal(1, backend.Executed)
            Assert.True(abs (r.Probabilities.[0] - sin 0.6 ** 2.0) < 0.03, $"P(0) = {r.Probabilities.[0]}")
        | Error e -> failwith e.Message

    [<Fact>]
    let ``simulate refuses an arbitrary input state on a whole-circuit backend`` () =
        let backend = SampledWholeCircuit.Backend(1000, 1)

        match
            HamiltonianSimulation.simulate
                xHamiltonian
                (QuantumState.StateVector(StateVector.init 1))
                (simulationConfig backend)
        with
        | Error(QuantumError.OperationError("HamiltonianSimulation", msg)) ->
            Assert.Contains("simulateFromPreparation", msg)
        | other -> failwith $"Expected an Error naming simulateFromPreparation, got {other}"

    /// Carbon monoxide has no integral path here, so VQE uses the empirical Hamiltonian.
    let private carbonMonoxide =
        {
            Name = "CO"
            Atoms =
                [
                    {
                        Element = "C"
                        Position = (0.0, 0.0, 0.0)
                    }
                    {
                        Element = "O"
                        Position = (0.0, 0.0, 1.128)
                    }
                ]
            Bonds =
                [
                    {
                        Atom1 = 0
                        Atom2 = 1
                        BondOrder = 3.0
                    }
                ]
            Charge = 0
            Multiplicity = 1
        }

    let private empiricalConfig (backend: IQuantumBackend) =
        {
            Method = GroundStateMethod.VQE
            MaxIterations = 2
            Tolerance = 1e-9
            InitialParameters = Some(Array.create 8 0.3)
            Backend = Some backend
            ProgressReporter = None
            ErrorMitigation = None
            IntegralProvider = None
        }

    [<Fact>]
    let ``empirical-Hamiltonian VQE samples gate by gate on the local simulator`` () =
        task {
            let backend = Backends.LocalBackend.LocalBackend() :> IQuantumBackend

            match! VQE.run carbonMonoxide (empiricalConfig backend) |> Async.StartImmediateAsTask with
            | Ok r ->
                Assert.Equal(EmpiricalHamiltonian, r.Source)
                Assert.Equal(SampledGateByGate 1000, r.Estimation)
            | Error e -> failwith e.Message
        } :> Task

    [<Fact>]
    let ``empirical-Hamiltonian VQE runs whole circuits on a sampling backend`` () =
        task {
            let backend = SampledWholeCircuit.Backend(2000, 9)

            match! VQE.run carbonMonoxide (empiricalConfig backend) |> Async.StartImmediateAsTask with
            | Ok r ->
                Assert.Equal(EmpiricalHamiltonian, r.Source)
                Assert.True(System.Double.IsFinite r.Energy)

                match r.Estimation with
                | SampledCircuits(perEnergy, shots, executed) ->
                    // Z and ZZ terms only: one measurement group per energy.
                    Assert.Equal(1, perEnergy)
                    Assert.Equal(Some 2000, shots)
                    Assert.Equal(backend.Executed, executed)
                    Assert.True(executed > 0)
                | other -> failwith $"Expected SampledCircuits, got {other}"
            | Error e -> failwith e.Message
        } :> Task
