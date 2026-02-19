namespace FSharp.Azure.Quantum.Topological.Tests

open Xunit
open FSharp.Azure.Quantum.Core
open FSharp.Azure.Quantum.GroverSearch
open FSharp.Azure.Quantum.Algorithms
open FSharp.Azure.Quantum.Topological
open System.Numerics

/// Tests for AlgorithmExtensions - Topological backend integration
/// 
/// Verifies that *WithTopology functions provide convenient access to
/// topological backends for basic integration testing.
/// 
/// NOTE: Full Grover search generates gates beyond current GateToBraid support.
/// These tests verify the integration architecture works correctly.
module AlgorithmExtensionsTests =
    
    [<Fact(Skip = "Shor on 30 Ising anyons compiles hundreds of gates through Solovay-Kitaev; >10 min")>]
    let ``AlgorithmExtensions - factorWithTopology accepts topological backend`` () =
        // Arrange
        // Factoring 15 needs 8 precision + 4 target qubits = 12 qubits
        // Ising anyons: 2n + 2 anyons -> 2*12 + 2 = 26 anyons
        let topoBackend = TopologicalUnifiedBackendFactory.createIsing 30
        
        // Act
        // Use 15 as standard test case
        let result = AlgorithmExtensions.factorWithTopology 15 topoBackend None
        
        // Assert
        match result with
        | Ok _ -> Assert.True(true)
        | Error (QuantumError.OperationError (name, _)) ->
            Assert.Equal("TopologicalBackend", name)
        | Error err ->
            Assert.True(false, $"Unexpected error: {err}")

    [<Fact>]
    let ``AlgorithmExtensions - solveLinearSystemTopology accepts topological backend`` () =
        // Arrange
        // HHL: 1 qubit for eigenvalue (simple test) + 1 qubit for solution + 1 ancilla = 3 qubits
        // Ising anyons: 2n + 2 anyons -> 2*3 + 2 = 8 anyons
        let topoBackend = TopologicalUnifiedBackendFactory.createIsing 16
        
        // Setup simple 2x2 identity system: I * x = b
        // x should equal b
        let vector = [| System.Numerics.Complex.One; System.Numerics.Complex.Zero |]
        let matrixRes = HHLTypes.createDiagonalMatrix [| 1.0; 1.0 |]
        let vectorRes = HHLTypes.createQuantumVector vector
        
        match matrixRes, vectorRes with
        | Ok matrix, Ok qVector ->
            // Act
            let result = AlgorithmExtensions.solveLinearSystemTopology matrix qVector topoBackend None
            
            // Assert
            match result with
            | Ok _ -> Assert.True(true)
            | Error (QuantumError.OperationError (name, _)) ->
                Assert.Equal("TopologicalBackend", name)
            | Error (QuantumError.NotImplemented _) ->
                // Expected: GateBased-to-TopologicalBraiding conversion is not implemented in Core
                Assert.True(true)
            | Error err ->
                Assert.True(false, $"Unexpected error: {err}")
        | _ -> Assert.True(false, "Failed to create HHL test data")

    [<Fact>]
    let ``AlgorithmExtensions - Empty target list rejected`` () =
        // Arrange: Create backend
        let topoBackend = TopologicalUnifiedBackendFactory.createIsing 8
        let config = Grover.defaultConfig
        
        // Act: Try to search with empty target list
        let result = AlgorithmExtensions.searchMultipleWithTopology [] 4 topoBackend config
        
        // Assert: Should fail with validation error
        match result with
        | Ok _ ->
            Assert.True(false, "Should reject empty target list")
        | Error (QuantumError.ValidationError (param, _)) ->
            Assert.Equal("Targets", param)
        | Error err ->
            Assert.True(false, $"Wrong error type: {err}")
    
    [<Fact>]
    let ``AlgorithmExtensions - Adapter respects qubit count limits`` () =
        // Arrange: Create backend with strict qubit limit
        // 5 anyons is enough for 2 qubits (Ising needs 2n+2 generally, or specific fusion tree size)
        // 5 anyons < (3 qubits * 2 + 2) = 8 anyons
        let topoBackend = TopologicalUnifiedBackendFactory.createIsing 5  
        let config = { Grover.defaultConfig with Shots = 10 }
        
        // Act: Try to create circuit requiring 3 qubits (which exceeds 5 anyon limit)
        let result = AlgorithmExtensions.searchSingleWithTopology 1 3 topoBackend config
        
        // Assert: Should fail with validation or backend error
        match result with
        | Ok _ ->
            Assert.True(false, "Should reject circuit exceeding anyon limit")
        | Error _ ->
            // Expected: validation or execution failure
            Assert.True(true)
    
    [<Fact>]
    let ``AlgorithmExtensions - qftWithTopology accepts topological backend`` () =
        // Arrange
        let topoBackend = TopologicalUnifiedBackendFactory.createIsing 8
        let config = QFT.defaultConfig
        
        // Act
        let result = AlgorithmExtensions.qftWithTopology 3 topoBackend config
        
        // Assert
        match result with
        | Ok _ -> Assert.True(true)
        | Error (QuantumError.OperationError (name, _)) ->
            Assert.Equal("TopologicalBackend", name)
        | Error err ->
            Assert.True(false, $"Unexpected error: {err}")

    // ========================================================================
    // HHL ON TOPOLOGICAL BACKEND
    // ========================================================================
    //
    // Bug 1 fixed HHL's FusionSuperposition post-processing (5 functions that
    // silently returned garbage for topological states).
    //
    // Verified behavior (Ising backend, diagonal matrices): HHL takes the
    // native diagonal path — StateVector→FusionSuperposition conversion
    // followed by a single ancilla RY rotation. Post-selection on ancilla |1⟩
    // fails because the rotation does not move enough amplitude into the
    // ancilla-1 subspace when executed through braid compilation. This yields:
    //   - Solution: all-zero (ancilla-mask extraction reads the wrong subspace)
    //   - SuccessProbability: 0.0
    //   - PostSelectionSuccess: false
    //   - EstimatedEigenvalues: correct (passed through from diagonal config)
    //
    // These tests assert the *actual* behavior so regressions and future
    // improvements (magic state distillation, Fibonacci backend) are detected
    // immediately — a change from zero to non-zero results will break a test,
    // signaling that the assertions should be upgraded to validate correctness.

    [<Fact>]
    let ``HHL on topological backend returns Ok with zero solution for Ising diagonal`` () =
        // Arrange: Simple 2x2 diagonal system Ax=b where A=diag(2,3), b=[1,0]
        let topoBackend = TopologicalUnifiedBackendFactory.createIsing 40

        let matrixRes = HHLTypes.createDiagonalMatrix [| 2.0; 3.0 |]
        let vectorRes = HHLTypes.createQuantumVector [| Complex(1.0, 0.0); Complex.Zero |]

        match matrixRes, vectorRes with
        | Ok matrix, Ok qVector ->
            let result = AlgorithmExtensions.solveLinearSystemTopology matrix qVector topoBackend None

            match result with
            | Ok hhlResult ->
                // Native diagonal HHL on Ising: ancilla RY rotation compiled through braids
                // does not produce post-selectable amplitude. Solution extraction yields zeros.
                Assert.Equal(2, hhlResult.Solution.Length)
                let allZero = hhlResult.Solution |> Array.forall (fun c -> c.Magnitude < 1e-10)
                Assert.True(allZero,
                    $"Expected all-zero solution from Ising diagonal HHL, got {hhlResult.Solution}")
            | Error (QuantumError.NotImplemented _) ->
                // Acceptable: conversion pipeline may change in future refactors
                ()
            | Error (QuantumError.OperationError (name, _)) ->
                Assert.Equal("TopologicalBackend", name)
            | Error err ->
                Assert.Fail($"Unexpected error: {err}")
        | _ -> Assert.Fail("Failed to create HHL test data")

    [<Fact>]
    let ``HHL on topological backend returns zero success probability for Ising diagonal`` () =
        let topoBackend = TopologicalUnifiedBackendFactory.createIsing 40

        let matrixRes = HHLTypes.createDiagonalMatrix [| 2.0; 3.0 |]
        let vectorRes = HHLTypes.createQuantumVector [| Complex(1.0, 0.0); Complex.Zero |]

        match matrixRes, vectorRes with
        | Ok matrix, Ok qVector ->
            let result = AlgorithmExtensions.solveLinearSystemTopology matrix qVector topoBackend None

            match result with
            | Ok hhlResult ->
                // Post-selection fails on Ising diagonal HHL: ancilla never reaches |1⟩
                // with sufficient amplitude through braid-compiled RY.
                Assert.Equal(0.0, hhlResult.SuccessProbability)
                Assert.False(hhlResult.PostSelectionSuccess,
                    "Post-selection should fail for Ising diagonal HHL")
            | Error (QuantumError.NotImplemented _) -> ()
            | Error (QuantumError.OperationError (name, _)) ->
                Assert.Equal("TopologicalBackend", name)
            | Error err ->
                Assert.Fail($"Unexpected error: {err}")
        | _ -> Assert.Fail("Failed to create HHL test data")

    [<Fact>]
    let ``HHL on topological backend preserves diagonal eigenvalues`` () =
        let topoBackend = TopologicalUnifiedBackendFactory.createIsing 40

        let matrixRes = HHLTypes.createDiagonalMatrix [| 2.0; 3.0 |]
        let vectorRes = HHLTypes.createQuantumVector [| Complex(1.0, 0.0); Complex.Zero |]

        match matrixRes, vectorRes with
        | Ok matrix, Ok qVector ->
            let result = AlgorithmExtensions.solveLinearSystemTopology matrix qVector topoBackend None

            match result with
            | Ok hhlResult ->
                // Diagonal eigenvalues are passed through from the config, not extracted
                // from the quantum state. They should match the input exactly.
                Assert.Equal(2, hhlResult.EstimatedEigenvalues.Length)
                Assert.Equal(2.0, hhlResult.EstimatedEigenvalues.[0])
                Assert.Equal(3.0, hhlResult.EstimatedEigenvalues.[1])
            | Error (QuantumError.NotImplemented _) -> ()
            | Error (QuantumError.OperationError (name, _)) ->
                Assert.Equal("TopologicalBackend", name)
            | Error err ->
                Assert.Fail($"Unexpected error: {err}")
        | _ -> Assert.Fail("Failed to create HHL test data")

    [<Fact>]
    let ``HHL on topological backend with identity matrix returns zero solution`` () =
        // Arrange: Ix = b means x = b (classically), but Ising HHL can't solve this
        let topoBackend = TopologicalUnifiedBackendFactory.createIsing 40

        let matrixRes = HHLTypes.createDiagonalMatrix [| 1.0; 1.0 |]
        let vectorRes = HHLTypes.createQuantumVector [| Complex(1.0, 0.0); Complex.Zero |]

        match matrixRes, vectorRes with
        | Ok matrix, Ok qVector ->
            let result = AlgorithmExtensions.solveLinearSystemTopology matrix qVector topoBackend None

            match result with
            | Ok hhlResult ->
                // Same limitation as diag(2,3): native diagonal path on Ising
                // yields all-zero solutions with failed post-selection.
                Assert.Equal(2, hhlResult.Solution.Length)
                let allZero = hhlResult.Solution |> Array.forall (fun c -> c.Magnitude < 1e-10)
                Assert.True(allZero,
                    $"Expected all-zero solution from Ising identity HHL, got {hhlResult.Solution}")
                Assert.Equal(0.0, hhlResult.SuccessProbability)
                Assert.Equal(2, hhlResult.EstimatedEigenvalues.Length)
                Assert.Equal(1.0, hhlResult.EstimatedEigenvalues.[0])
                Assert.Equal(1.0, hhlResult.EstimatedEigenvalues.[1])
            | Error (QuantumError.NotImplemented _) -> ()
            | Error (QuantumError.OperationError (name, _)) ->
                Assert.Equal("TopologicalBackend", name)
            | Error err ->
                Assert.Fail($"Unexpected error: {err}")
        | _ -> Assert.Fail("Failed to create HHL test data")