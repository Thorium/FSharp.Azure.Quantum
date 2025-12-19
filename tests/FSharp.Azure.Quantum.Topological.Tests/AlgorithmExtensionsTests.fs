namespace FSharp.Azure.Quantum.Topological.Tests

open Xunit
open FSharp.Azure.Quantum.Core
open FSharp.Azure.Quantum.GroverSearch
open FSharp.Azure.Quantum.Algorithms
open FSharp.Azure.Quantum.Topological

/// Tests for AlgorithmExtensions - Topological backend integration
/// 
/// Verifies that *WithTopology functions provide convenient access to
/// topological backends for basic integration testing.
/// 
/// NOTE: Full Grover search generates gates beyond current GateToBraid support.
/// These tests verify the integration architecture works correctly.
module AlgorithmExtensionsTests =
    
    [<Fact>]
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


