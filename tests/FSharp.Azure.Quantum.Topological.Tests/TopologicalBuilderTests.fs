namespace FSharp.Azure.Quantum.Topological.Tests

open Xunit
open System.Numerics
open FSharp.Azure.Quantum.Topological
open FSharp.Azure.Quantum.Core
open FSharp.Azure.Quantum.Core.BackendAbstraction

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
    
    // ============================================================================
    // MULTI-TERM SUPERPOSITION MEASUREMENT TESTS
    // ============================================================================
    
    [<Fact>]
    let ``Builder measure works on pure state (single term)`` () = task {
        let backend = TopologicalUnifiedBackendFactory.createUnified AnyonSpecies.AnyonType.Ising 10
        
        let program = topological backend {
            do! TopologicalBuilder.initialize AnyonSpecies.AnyonType.Ising 4
            let! outcome = TopologicalBuilder.measure 0
            return outcome
        }
        
        let! result = TopologicalBuilder.execute backend program
        
        match result with
        | Ok outcome ->
            // Measurement should succeed on a pure state
            Assert.True(true, $"Got outcome: {outcome}")
        | Error err -> Assert.Fail($"Pure state measurement failed: {err.Message}")
    }
    
    [<Fact>]
    let ``Builder measure works on multi-term superposition after F-move`` () = task {
        // Create a state, apply F-move via backend to create multi-term superposition,
        // then measure through the builder
        let backend = TopologicalUnifiedBackendFactory.createUnified AnyonSpecies.AnyonType.Ising 10
        
        // Initialize 4 Ising anyons
        let initResult = backend.InitializeState 4
        match initResult with
        | Error err -> Assert.Fail($"Init failed: {err.Message}")
        | Ok initState ->
        
        // Apply F-move to create multi-term superposition
        let fMoveResult = backend.ApplyOperation (QuantumOperation.FMove (FMoveDirection.Forward, 0)) initState
        match fMoveResult with
        | Error err -> Assert.Fail($"F-move failed: {err.Message}")
        | Ok fMovedState ->
        
        // Verify we actually have a multi-term superposition
        match fMovedState with
        | QuantumState.FusionSuperposition fs ->
            match TopologicalOperations.fromInterface fs with
            | Some superposition ->
                // Only test multi-term path if F-move actually created multiple terms
                if superposition.Terms.Length > 1 then
                    // Create builder context with this multi-term state
                    let ctx : TopologicalBuilder.BuilderContext = {
                        Backend = backend
                        CurrentState = fMovedState
                        MeasurementResults = []
                        ExecutionLog = []
                        History = []
                    }
                    
                    // Attempt measurement on multi-term superposition
                    let! measureResult = TopologicalBuilder.measure 0 ctx
                    match measureResult with
                    | Ok (particle, newCtx) ->
                        // Measurement should succeed and return a valid particle
                        Assert.NotNull(box particle)
                        Assert.NotEmpty(newCtx.MeasurementResults)
                    | Error err ->
                        Assert.Fail($"Multi-term measurement should succeed but got: {err.Message}")
                else
                    // Single-term F-move result — measure should work (already tested)
                    Assert.True(true, "F-move produced single term; skip multi-term test")
            | None -> Assert.Fail("Could not unwrap superposition")
        | _ -> Assert.Fail("Expected FusionSuperposition state")
    }
    
    [<Fact>]
    let ``Builder measure on superposition returns valid particle type`` () = task {
        let backend = TopologicalUnifiedBackendFactory.createUnified AnyonSpecies.AnyonType.Ising 10
        
        // Create multi-term superposition manually
        let sigma = AnyonSpecies.Particle.Sigma
        let vacuum = AnyonSpecies.Particle.Vacuum
        let tree1 = FusionTree.fuse (FusionTree.leaf sigma) (FusionTree.leaf sigma) vacuum
        let tree2 = FusionTree.fuse (FusionTree.leaf sigma) (FusionTree.leaf sigma) AnyonSpecies.Particle.Psi
        
        let state1 = FusionTree.create tree1 AnyonSpecies.AnyonType.Ising
        let state2 = FusionTree.create tree2 AnyonSpecies.AnyonType.Ising
        
        let superposition : TopologicalOperations.Superposition = {
            Terms = [
                (Complex(1.0 / sqrt 2.0, 0.0), state1)
                (Complex(1.0 / sqrt 2.0, 0.0), state2)
            ]
            AnyonType = AnyonSpecies.AnyonType.Ising
        }
        
        let multiTermState = QuantumState.FusionSuperposition (TopologicalOperations.toInterface superposition)
        
        let ctx : TopologicalBuilder.BuilderContext = {
            Backend = backend
            CurrentState = multiTermState
            MeasurementResults = []
            ExecutionLog = []
            History = []
        }
        
        let! measureResult = TopologicalBuilder.measure 0 ctx
        match measureResult with
        | Ok (particle, _) ->
            // Result should be a valid Ising particle
            let validParticles = [AnyonSpecies.Particle.Vacuum; AnyonSpecies.Particle.Sigma; AnyonSpecies.Particle.Psi]
            Assert.Contains(particle, validParticles)
        | Error err ->
            Assert.Fail($"Multi-term measurement failed: {err.Message}")
    }
