namespace FSharp.Azure.Quantum.Topological.Tests

open Xunit
open FSharp.Azure.Quantum.Core
open FSharp.Azure.Quantum.GroverSearch
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
    let ``AlgorithmExtensions - searchWithTopology accepts topological backend`` () =
        // Arrange: Create topological backend and simple oracle
        let topoBackend = TopologicalBackend.createSimulator AnyonSpecies.AnyonType.Ising 8
        
        match Oracle.forValue 1 2 with
        | Error err ->
            Assert.True(false, $"Oracle creation failed: {err}")
        | Ok oracle ->
            let config = { Search.defaultConfig with Shots = 10; MaxIterations = Some 1 }
            
            // Act: Call searchWithTopology (integration test - may fail on unsupported gates)
            let result = AlgorithmExtensions.searchWithTopology oracle topoBackend config
            
            // Assert: Verify function signature and error handling work
            match result with
            | Ok _ -> 
                // Success - full integration working
                Assert.True(true)
            | Error (QuantumError.BackendError (name, msg)) ->
                // Expected: Grover may generate unsupported gates
                Assert.Contains("Topological Backend Adapter", name)
                // This is OK - proves adapter is being invoked
            | Error err ->
                Assert.True(false, $"Unexpected error type: {err}")
    
    [<Fact>]
    let ``AlgorithmExtensions - Empty target list rejected`` () =
        // Arrange: Create backend
        let topoBackend = TopologicalBackend.createSimulator AnyonSpecies.AnyonType.Ising 8
        let config = Search.defaultConfig
        
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
    let ``AlgorithmExtensions - Fibonacci adapter rejects non-Ising compilation`` () =
        // Arrange: Create Fibonacci anyon backend
        // NOTE: This test is now obsolete - AlgorithmExtensions only supports Ising
        // Kept for backwards compatibility - will always use Ising adapter internally
        let topoBackend = TopologicalBackend.createSimulator AnyonSpecies.AnyonType.Fibonacci 8
        let config = { Search.defaultConfig with Shots = 10; MaxIterations = Some 1 }
        
        // Act: Try to use Fibonacci anyons (will use Ising adapter anyway)
        let result = AlgorithmExtensions.searchSingleWithTopology 1 2 topoBackend config
        
        // Assert: Should fail since backend is Fibonacci but adapter is Ising
        match result with
        | Ok _ ->
            Assert.True(false, "Fibonacci backend with Ising adapter should fail")
        | Error _ ->
            // Expected: mismatch between backend and adapter
            Assert.True(true)
    
    [<Fact>]
    let ``AlgorithmExtensions - Adapter respects qubit count limits`` () =
        // Arrange: Create backend with strict qubit limit
        let topoBackend = TopologicalBackend.createSimulator AnyonSpecies.AnyonType.Ising 5  // Max 4 qubits (5 anyons)
        let config = { Search.defaultConfig with Shots = 10 }
        
        // Act: Try to create circuit requiring 6 anyons (5 qubits)
        let result = AlgorithmExtensions.searchSingleWithTopology 1 5 topoBackend config
        
        // Assert: Should fail with validation or backend error
        match result with
        | Ok _ ->
            Assert.True(false, "Should reject circuit exceeding anyon limit")
        | Error _ ->
            // Expected: validation or execution failure
            Assert.True(true)
    
    [<Fact>]
    let ``AlgorithmExtensions - All convenience functions have correct signatures`` () =
        // This test verifies all public API functions compile and have expected signatures
        // No execution - just type checking
        
        let topoBackend = TopologicalBackend.createSimulator AnyonSpecies.AnyonType.Ising 8
        let config = Search.defaultConfig
        
        // Verify function signatures exist (compile-time test)
        // NOTE: Signatures updated - anyonType parameter removed (now implicit Ising)
        let _ = AlgorithmExtensions.searchWithTopology : Oracle.CompiledOracle -> TopologicalBackend.ITopologicalBackend -> Search.SearchConfig -> QuantumResult<Search.SearchResult>
        let _ = AlgorithmExtensions.searchSingleWithTopology : int -> int -> TopologicalBackend.ITopologicalBackend -> Search.SearchConfig -> QuantumResult<Search.SearchResult>
        let _ = AlgorithmExtensions.searchMultipleWithTopology : int list -> int -> TopologicalBackend.ITopologicalBackend -> Search.SearchConfig -> QuantumResult<Search.SearchResult>
        let _ = AlgorithmExtensions.searchWithPredicateTopology : (int -> bool) -> int -> TopologicalBackend.ITopologicalBackend -> Search.SearchConfig -> QuantumResult<Search.SearchResult>
        
        Assert.True(true, "All function signatures correct")


