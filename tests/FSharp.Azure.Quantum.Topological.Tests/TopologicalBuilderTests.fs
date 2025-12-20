namespace FSharp.Azure.Quantum.Topological.Tests

open Xunit
open FSharp.Azure.Quantum.Topological

module TopologicalBuilderTests =
    
    [<Fact>]
    let ``Builder module exists and is accessible`` () =
        // This test verifies the builder infrastructure is in place
        let backend = TopologicalUnifiedBackendFactory.createUnified AnyonSpecies.AnyonType.Ising 10
        let builder = topological backend
        Assert.NotNull(builder)
    
    [<Fact>]
    let ``Builder can execute simple program`` () = task {
        let backend = TopologicalUnifiedBackendFactory.createUnified AnyonSpecies.AnyonType.Ising 10
        
        let program = topological backend {
            do! TopologicalBuilder.initialize AnyonSpecies.AnyonType.Ising 4
            return ()
        }
        
        let! result = TopologicalBuilder.execute backend program
        
        match result with
        | Ok _ -> Assert.True(true)
        | Error err -> Assert.Fail($"Program failed: {err.Message}")
    }
    
    [<Fact>]
    let ``Builder operations thread state correctly`` () = task {
        let backend = TopologicalUnifiedBackendFactory.createUnified AnyonSpecies.AnyonType.Ising 10
        
        let program = topological backend {
            do! TopologicalBuilder.initialize AnyonSpecies.AnyonType.Ising 4
            do! TopologicalBuilder.braid 0
            do! TopologicalBuilder.braid 2
            return ()
        }
        
        let! result = TopologicalBuilder.execute backend program
        
        match result with
        | Ok _ -> Assert.True(true) // Success - operations threaded correctly
        | Error err -> Assert.Fail($"Program failed: {err.Message}")
    }
    
    [<Fact>]
    let ``Builder works with ANY backend (backend-agnostic principle)`` () = task {
        // This test demonstrates the key architectural principle:
        // Programs work with ANY IQuantumBackend implementation
        
        // Test with simulator backend
        let simulatorBackend = TopologicalUnifiedBackendFactory.createUnified AnyonSpecies.AnyonType.Ising 10
        
        let program = topological simulatorBackend {
            do! TopologicalBuilder.initialize AnyonSpecies.AnyonType.Ising 4
            do! TopologicalBuilder.braid 0
            return ()
        }
        
        let! result = TopologicalBuilder.execute simulatorBackend program
        
        match result with
        | Ok _ -> Assert.True(true)
        | Error err -> Assert.Fail($"Program failed: {err.Message}")
        
        // In future: Same program will work with hardware backend!
        // let hardwareBackend = MicrosoftMajoranaBackend.create(...)
        // let! hardwareResult = topological hardwareBackend { ... }
        // Programs are COMPLETELY backend-agnostic!
    }
    
    [<Fact>]
    let ``Builder with braiding sequence`` () = task {
        // Increased backend capacity to 20 anyons to support 6 logical qubits
        let backend = TopologicalUnifiedBackendFactory.createUnified AnyonSpecies.AnyonType.Ising 20
        
        let program = topological backend {
            do! TopologicalBuilder.initialize AnyonSpecies.AnyonType.Ising 6
            do! TopologicalBuilder.braid 0
            do! TopologicalBuilder.braid 2
            do! TopologicalBuilder.braid 4
            return ()
        }
        
        let! result = TopologicalBuilder.execute backend program
        
        match result with
        | Ok _ ->
            // Successfully completed braiding sequence
            Assert.True(true)
        | Error err ->
            Assert.Fail($"Program failed: {err.Message}")
    }
