namespace FSharp.Azure.Quantum.Topological.Tests

open Xunit
open FSharp.Azure.Quantum
open FSharp.Azure.Quantum.Topological
open System.Threading.Tasks
open FSharp.Azure.Quantum.Core.BackendAbstraction
open FSharp.Azure.Quantum.Core
open FSharp.Azure.Quantum.Core.CircuitAbstraction

module TopologicalBackendTests =

    type private LoweringProbeExtension() =
        let mutable wasLowered = false

        member _.WasLowered = wasLowered

        interface ILowerToOperationsExtension with
            member _.Id = "tests.topological.lowering-probe"
            member _.LowerToGates () =
                wasLowered <- true
                [ CircuitBuilder.X 0 ]

    type private UnsupportedExtension() =
        interface IQuantumOperationExtension with
            member _.Id = "tests.topological.unsupported"
    
    // ========================================================================
    // CAPABILITY VALIDATION
    // ========================================================================
    
    [<Fact>]
    let ``Unified Topological backend should support lowering extension operations`` () =
        let backend = TopologicalUnifiedBackend.TopologicalUnifiedBackend(AnyonSpecies.AnyonType.Ising, 10) :> IQuantumBackend
        let ext = LoweringProbeExtension() :> IQuantumOperationExtension
        Assert.True(backend.SupportsOperation (QuantumOperation.Extension ext))

    [<Fact>]
    let ``Unified Topological backend should not support unknown extension operations`` () =
        let backend = TopologicalUnifiedBackend.TopologicalUnifiedBackend(AnyonSpecies.AnyonType.Ising, 10) :> IQuantumBackend
        let ext = UnsupportedExtension() :> IQuantumOperationExtension
        Assert.False(backend.SupportsOperation (QuantumOperation.Extension ext))

    [<Fact>]
    let ``Unified Topological backend should apply lowering extension by executing lowered gates`` () =
        let backend = TopologicalUnifiedBackend.TopologicalUnifiedBackend(AnyonSpecies.AnyonType.Ising, 10) :> IQuantumBackend
 
        match backend.InitializeState 2 with
        | Error err ->
            Assert.True(false, sprintf "InitializeState failed: %A" err)
        | Ok initialState ->
            let probe = LoweringProbeExtension()
            let op = QuantumOperation.Extension (probe :> IQuantumOperationExtension)
 
            match backend.ApplyOperation op initialState with
            | Error err ->
                Assert.True(false, sprintf "ApplyOperation (extension) failed: %A" err)
            | Ok (QuantumState.FusionSuperposition fs) ->
                Assert.True(probe.WasLowered, "Expected LowerToGates() to be invoked")
                Assert.True(fs.IsNormalized, "Expected resulting topological state to be normalized")
            | Ok _ ->
                Assert.True(false, "Expected FusionSuperposition output")

    [<Fact>]
    let ``Unified Topological backend should support QPE intent operation`` () =
        // Note: CP/CRZ transpilation may require additional anyon resources.
        let backend = TopologicalUnifiedBackend.TopologicalUnifiedBackend(AnyonSpecies.AnyonType.Ising, 40) :> IQuantumBackend
        let intent =
            {
                CountingQubits = 3
                TargetQubits = 1
                Unitary = QpeUnitary.TGate
                PrepareTargetOne = true
                ApplySwaps = false
            }

        Assert.True(backend.SupportsOperation (QuantumOperation.Algorithm (AlgorithmOperation.QPE intent)))

    [<Fact>]
    let ``Unified Topological backend should apply QPE intent and return FusionSuperposition`` () =
        // Note: CP/CRZ transpilation may require additional anyon resources.
        let backend = TopologicalUnifiedBackend.TopologicalUnifiedBackend(AnyonSpecies.AnyonType.Ising, 40) :> IQuantumBackend
        let intent =
            {
                CountingQubits = 3
                TargetQubits = 1
                Unitary = QpeUnitary.TGate
                PrepareTargetOne = true
                ApplySwaps = false
            }

        match backend.InitializeState 4 with
        | Error err ->
            Assert.True(false, sprintf "InitializeState failed: %A" err)
        | Ok initialState ->
            match backend.ApplyOperation (QuantumOperation.Algorithm (AlgorithmOperation.QPE intent)) initialState with
            | Error err ->
                Assert.True(false, sprintf "ApplyOperation (QPE intent) failed: %A" err)
            | Ok (QuantumState.FusionSuperposition fs) ->
                Assert.True(fs.IsNormalized, "Expected resulting topological state to be normalized")
            | Ok _ ->
                Assert.True(false, "Expected FusionSuperposition output")


    [<Fact>]
    let ``Simulator backend advertises correct capabilities for Ising anyons`` () =
        let backend = TopologicalBackend.createSimulator AnyonSpecies.AnyonType.Ising 10
        let caps = backend.Capabilities
        
        Assert.Contains(AnyonSpecies.AnyonType.Ising, caps.SupportedAnyonTypes)
        Assert.Equal(Some 10, caps.MaxAnyons)
        Assert.True(caps.SupportsBraiding)
        Assert.True(caps.SupportsMeasurement)
        Assert.True(caps.SupportsFMoves)
        Assert.False(caps.SupportsErrorCorrection) // Classical simulator has no error correction
    
    [<Fact>]
    let ``Simulator backend advertises correct capabilities for Fibonacci anyons`` () =
        let backend = TopologicalBackend.createSimulator AnyonSpecies.AnyonType.Fibonacci 8
        let caps = backend.Capabilities
        
        Assert.Contains(AnyonSpecies.AnyonType.Fibonacci, caps.SupportedAnyonTypes)
        Assert.Equal(Some 8, caps.MaxAnyons)
        Assert.True(caps.SupportsBraiding)
    
    [<Fact>]
    let ``validateCapabilities succeeds when backend supports required features`` () =
        let backend = TopologicalBackend.createSimulator AnyonSpecies.AnyonType.Ising 10
        
        let required = {
            TopologicalBackend.SupportedAnyonTypes = [AnyonSpecies.AnyonType.Ising]
            TopologicalBackend.MaxAnyons = Some 5
            TopologicalBackend.SupportsBraiding = true
            TopologicalBackend.SupportsMeasurement = true
            TopologicalBackend.SupportsFMoves = false
            TopologicalBackend.SupportsErrorCorrection = false
        }
        
        let result = TopologicalBackend.validateCapabilities backend required
        Assert.True(result.IsOk)
    
    [<Fact>]
    let ``validateCapabilities fails when anyon type not supported`` () =
        let backend = TopologicalBackend.createSimulator AnyonSpecies.AnyonType.Ising 10
        
        let required = {
            TopologicalBackend.SupportedAnyonTypes = [AnyonSpecies.AnyonType.Fibonacci]
            TopologicalBackend.MaxAnyons = None
            TopologicalBackend.SupportsBraiding = false
            TopologicalBackend.SupportsMeasurement = false
            TopologicalBackend.SupportsFMoves = false
            TopologicalBackend.SupportsErrorCorrection = false
        }
        
        let result = TopologicalBackend.validateCapabilities backend required
        Assert.True(result.IsError)
        match result with
        | Error err -> Assert.Contains("does not support required anyon types", err.Message)
        | Ok _ -> Assert.True(false, "Should have failed")
    
    [<Fact>]
    let ``validateCapabilities fails when too many anyons requested`` () =
        let backend = TopologicalBackend.createSimulator AnyonSpecies.AnyonType.Ising 10
        
        let required = {
            TopologicalBackend.SupportedAnyonTypes = [AnyonSpecies.AnyonType.Ising]
            TopologicalBackend.MaxAnyons = Some 20 // More than backend supports!
            TopologicalBackend.SupportsBraiding = false
            TopologicalBackend.SupportsMeasurement = false
            TopologicalBackend.SupportsFMoves = false
            TopologicalBackend.SupportsErrorCorrection = false
        }
        
        let result = TopologicalBackend.validateCapabilities backend required
        Assert.True(result.IsError)
        match result with
        | Error err -> Assert.Contains("supports max 10 anyons", err.Message)
        | Ok _ -> Assert.True(false, "Should have failed")
    
    [<Fact>]
    let ``validateCapabilities fails when braiding required but not supported`` () =
        // This is hypothetical - we'd need a backend that doesn't support braiding
        // For now, test the logic with the simulator
        let backend = TopologicalBackend.createSimulator AnyonSpecies.AnyonType.Ising 10
        
        let required = {
            TopologicalBackend.SupportedAnyonTypes = [AnyonSpecies.AnyonType.Ising]
            TopologicalBackend.MaxAnyons = Some 5
            TopologicalBackend.SupportsBraiding = true
            TopologicalBackend.SupportsMeasurement = true
            TopologicalBackend.SupportsFMoves = true
            TopologicalBackend.SupportsErrorCorrection = true // Not supported!
        }
        
        let result = TopologicalBackend.validateCapabilities backend required
        Assert.True(result.IsError)
        match result with
        | Error err -> Assert.Contains("does not support error correction", err.Message)
        | Ok _ -> Assert.True(false, "Should have failed")
    
    // ========================================================================
    // INITIALIZATION
    // ========================================================================
    
    [<Fact>]
    let ``Initialize creates vacuum state for Ising anyons`` () = task {
        let backend = TopologicalBackend.createSimulator AnyonSpecies.AnyonType.Ising 10
        
        let! stateResult = backend.Initialize AnyonSpecies.AnyonType.Ising 4
        
        match stateResult with
        | Ok state ->
            Assert.NotNull(state)
            Assert.False(state.Terms.IsEmpty)
        | Error err ->
            Assert.Fail($"Expected Ok but got Error: {err.Message}")
    }
    
    [<Fact>]
    let ``Initialize fails when anyon type not supported`` () = task {
        let backend = TopologicalBackend.createSimulator AnyonSpecies.AnyonType.Ising 10
        
        // Should return Error because backend only supports Ising
        let! result = backend.Initialize AnyonSpecies.AnyonType.Fibonacci 4
        
        match result with
        | Error (TopologicalError.ValidationError (field, reason)) ->
            Assert.Contains("only supports", reason)
        | Ok _ ->
            Assert.Fail("Expected ValidationError but got Ok")
        | Error err ->
            Assert.Fail($"Expected ValidationError but got {err.Category}")
    }
    
    [<Fact>]
    let ``Initialize fails when too many anyons requested`` () = task {
        let backend = TopologicalBackend.createSimulator AnyonSpecies.AnyonType.Ising 10
        
        // Should return Error because 20 > 10 max anyons
        let! result = backend.Initialize AnyonSpecies.AnyonType.Ising 20
        
        match result with
        | Error (TopologicalError.BackendError (backend, reason)) ->
            Assert.Contains("supports max 10 anyons", reason)
        | Ok _ ->
            Assert.Fail("Expected BackendError but got Ok")
        | Error err ->
            Assert.Fail($"Expected BackendError but got {err.Category}")
    }
    
    [<Fact>]
    let ``Two sigma anyons initialize to valid fusion state`` () = task {
        // Business-meaningful: σ × σ = 1 + ψ means 2-dimensional space
        let backend = TopologicalBackend.createSimulator AnyonSpecies.AnyonType.Ising 10
        
        let! stateResult = backend.Initialize AnyonSpecies.AnyonType.Ising 2
        
        match stateResult with
        | Ok state ->
            // State should be pure (single fusion tree)
            Assert.Single(state.Terms) |> ignore
            
            let (amplitude, fusionState) = state.Terms.[0]
            
            // Should have a valid fusion state for 2 sigma anyons
            Assert.NotNull(fusionState)
            Assert.Equal(AnyonSpecies.AnyonType.Ising, fusionState.AnyonType)
        | Error err ->
            Assert.Fail($"Expected Ok but got Error: {err.Message}")
    }
    
    [<Fact>]
    let ``Four sigma anyons initialize to valid fusion state`` () = task {
        // Business-meaningful: 4 σ anyons have a 2D fusion space.
        // (This test is for the legacy simulator Initialize, not the unified backend encoding.)
        let backend = TopologicalBackend.createSimulator AnyonSpecies.AnyonType.Ising 10
        
        let! stateResult = backend.Initialize AnyonSpecies.AnyonType.Ising 4
        
        match stateResult with
        | Ok state ->
            Assert.Single(state.Terms) |> ignore
            let (_, fusionState) = state.Terms.[0]
            Assert.Equal(AnyonSpecies.AnyonType.Ising, fusionState.AnyonType)
        | Error err ->
            Assert.Fail($"Expected Ok but got Error: {err.Message}")
    }
    
    // ========================================================================
    // BRAIDING OPERATIONS
    // ========================================================================
    
    [<Fact>]
    let ``Braiding operation preserves superposition structure`` () = task {
        let backend = TopologicalBackend.createSimulator AnyonSpecies.AnyonType.Ising 10
        
        let! initialStateResult = backend.Initialize AnyonSpecies.AnyonType.Ising 4
        
        match initialStateResult with
        | Ok initialState ->
            // Braid first pair of anyons
            let! braidedStateResult = backend.Braid 0 initialState
            
            match braidedStateResult with
            | Ok braidedState ->
                Assert.NotNull(braidedState)
                Assert.False(braidedState.Terms.IsEmpty)
                Assert.NotEqual(initialState, braidedState) // State should change
            | Error err ->
                Assert.Fail($"Braid failed: {err.Message}")
        | Error err ->
            Assert.Fail($"Initialize failed: {err.Message}")
    }
    
    [<Fact>]
    let ``Braiding is a unitary operation on qubit state`` () = task {
        // Business-meaningful: Braiding implements quantum gates
        let backend = TopologicalBackend.createSimulator AnyonSpecies.AnyonType.Ising 10
        
        let! stateResult = backend.Initialize AnyonSpecies.AnyonType.Ising 4
        
        match stateResult with
        | Ok state ->
            let! braidedOnceResult = backend.Braid 0 state
            
            match braidedOnceResult with
            | Ok braidedOnce ->
                // Check state is still valid (unitary operation)
                Assert.Single(braidedOnce.Terms) |> ignore
                
                // Braiding preserves theory type
                let (_, fusionState) = braidedOnce.Terms.[0]
                Assert.Equal(AnyonSpecies.AnyonType.Ising, fusionState.AnyonType)
            | Error err ->
                Assert.Fail($"Braid failed: {err.Message}")
        | Error err ->
            Assert.Fail($"Initialize failed: {err.Message}")
    }
    
    [<Fact>]
    let ``Sequential braiding operations compose correctly`` () = task {
        let backend = TopologicalBackend.createSimulator AnyonSpecies.AnyonType.Ising 10
        
        let! stateResult = backend.Initialize AnyonSpecies.AnyonType.Ising 6
        
        match stateResult with
        | Ok state ->
            // Perform sequence of braids
            let! state1Result = backend.Braid 0 state
            match state1Result with
            | Ok state1 ->
                let! state2Result = backend.Braid 2 state1
                match state2Result with
                | Ok state2 ->
                    let! state3Result = backend.Braid 0 state2
                    match state3Result with
                    | Ok state3 ->
                        Assert.NotNull(state3)
                        Assert.False(state3.Terms.IsEmpty)
                    | Error err ->
                        Assert.Fail($"Third braid failed: {err.Message}")
                | Error err ->
                    Assert.Fail($"Second braid failed: {err.Message}")
            | Error err ->
                Assert.Fail($"First braid failed: {err.Message}")
        | Error err ->
            Assert.Fail($"Initialize failed: {err.Message}")
    }
    
    // ========================================================================
    // FUSION MEASUREMENT
    // ========================================================================
    
    [<Fact>]
    let ``Fusion measurement returns valid anyon outcome`` () = task {
        let backend = TopologicalBackend.createSimulator AnyonSpecies.AnyonType.Ising 10
        
        let! stateResult = backend.Initialize AnyonSpecies.AnyonType.Ising 4
        
        match stateResult with
        | Ok state ->
            // Measure fusion of first two anyons
            let! measureResult = backend.MeasureFusion 0 state
            
            match measureResult with
            | Ok (outcome, collapsedState, probability) ->
                // Outcome should be valid Ising anyon (1, σ, or ψ)
                Assert.Contains(outcome, [
                    AnyonSpecies.Particle.Vacuum
                    AnyonSpecies.Particle.Sigma
                    AnyonSpecies.Particle.Psi
                ])
                
                // Probability should be in [0,1]
                Assert.InRange(probability, 0.0, 1.0)
                
                // Collapsed state should exist
                Assert.NotNull(collapsedState)
            | Error err ->
                Assert.Fail($"MeasureFusion failed: {err.Message}")
        | Error err ->
            Assert.Fail($"Initialize failed: {err.Message}")
    }
    
    [<Fact>]
    let ``Fusion measurement collapses state`` () = task {
        // Business-meaningful: Measurement destroys superposition
        let backend = TopologicalBackend.createSimulator AnyonSpecies.AnyonType.Ising 10
        
        let! stateResult = backend.Initialize AnyonSpecies.AnyonType.Ising 4
        
        match stateResult with
        | Ok state ->
            let! measureResult = backend.MeasureFusion 0 state
            
            match measureResult with
            | Ok (_, collapsedState, _) ->
                // After measurement, state should be pure (single term)
                Assert.Single(collapsedState.Terms) |> ignore
            | Error err ->
                Assert.Fail($"MeasureFusion failed: {err.Message}")
        | Error err ->
            Assert.Fail($"Initialize failed: {err.Message}")
    }
    
    [<Fact>]
    let ``Measuring sigma-sigma fusion gives valid outcome`` () = task {
        // Business-meaningful: σ × σ = 1 + ψ
        let backend = TopologicalBackend.createSimulator AnyonSpecies.AnyonType.Ising 10
        
        let! stateResult = backend.Initialize AnyonSpecies.AnyonType.Ising 2
        
        match stateResult with
        | Ok state ->
            let! measureResult = backend.MeasureFusion 0 state
            
            match measureResult with
            | Ok (outcome, _, _) ->
                // Should be a valid Ising particle
                Assert.Contains(outcome, [
                    AnyonSpecies.Particle.Vacuum
                    AnyonSpecies.Particle.Sigma
                    AnyonSpecies.Particle.Psi
                ])
            | Error err ->
                Assert.Fail($"MeasureFusion failed: {err.Message}")
        | Error err ->
            Assert.Fail($"Initialize failed: {err.Message}")
    }
    
    // ========================================================================
    // COMPLETE PROGRAM EXECUTION
    // ========================================================================
    
    [<Fact>]
    let ``Execute empty program returns initial state`` () = task {
        let backend = TopologicalBackend.createSimulator AnyonSpecies.AnyonType.Ising 10
        
        let! initialStateResult = backend.Initialize AnyonSpecies.AnyonType.Ising 4
        
        match initialStateResult with
        | Ok initialState ->
            let! executeResult = backend.Execute initialState []
            
            match executeResult with
            | Ok result ->
                Assert.Equal(initialState, result.FinalState)
                Assert.Empty(result.MeasurementOutcomes)
                Assert.InRange(result.ExecutionTimeMs, 0.0, 1000.0)
            | Error err ->
                Assert.Fail($"Execute failed: {err.Message}")
        | Error err ->
            Assert.Fail($"Initialize failed: {err.Message}")
    }
    
    [<Fact>]
    let ``Execute program with braiding operations`` () = task {
        let backend = TopologicalBackend.createSimulator AnyonSpecies.AnyonType.Ising 10
        
        let! initialStateResult = backend.Initialize AnyonSpecies.AnyonType.Ising 6
        
        match initialStateResult with
        | Ok initialState ->
            let program = [
                TopologicalBackend.Braid 0
                TopologicalBackend.Braid 2
                TopologicalBackend.Braid 0
            ]
            
            let! executeResult = backend.Execute initialState program
            
            match executeResult with
            | Ok result ->
                Assert.NotEqual(initialState, result.FinalState)
                Assert.Equal(3, result.Messages.Length) // One message per braid
                Assert.All(result.Messages, fun msg -> Assert.Contains("Braided", msg))
            | Error err ->
                Assert.Fail($"Execute failed: {err.Message}")
        | Error err ->
            Assert.Fail($"Initialize failed: {err.Message}")
    }
    
    [<Fact>]
    let ``Execute program with measurement records outcomes`` () = task {
        let backend = TopologicalBackend.createSimulator AnyonSpecies.AnyonType.Ising 10
        
        let! initialStateResult = backend.Initialize AnyonSpecies.AnyonType.Ising 4
        
        match initialStateResult with
        | Ok initialState ->
            let program = [
                TopologicalBackend.Braid 0
                TopologicalBackend.Measure 0
            ]
            
            let! executeResult = backend.Execute initialState program
            
            match executeResult with
            | Ok result ->
                // Should have one measurement outcome
                Assert.Single(result.MeasurementOutcomes) |> ignore
                
                let (outcome, probability) = result.MeasurementOutcomes.[0]
                Assert.InRange(probability, 0.0, 1.0)
                
                // Should have message about measurement
                Assert.Contains(result.Messages, fun msg -> msg.Contains("Measured fusion"))
            | Error err ->
                Assert.Fail($"Execute failed: {err.Message}")
        | Error err ->
            Assert.Fail($"Initialize failed: {err.Message}")
    }
    
    [<Fact>]
    let ``Execute complex program with braiding and measurement`` () = task {
        // Business-meaningful: Simulate a simple topological quantum circuit
        let backend = TopologicalBackend.createSimulator AnyonSpecies.AnyonType.Ising 10
        
        let! initialStateResult = backend.Initialize AnyonSpecies.AnyonType.Ising 6
        
        match initialStateResult with
        | Ok initialState ->
            let program = [
                TopologicalBackend.Braid 0  // Braid first pair
                TopologicalBackend.Braid 2  // Braid second pair
                TopologicalBackend.Braid 0  // Braid first pair again
                TopologicalBackend.Measure 0 // Measure first fusion
                TopologicalBackend.Braid 2  // Continue with remaining anyons
                TopologicalBackend.Measure 2 // Measure second fusion
            ]
            
            let! executeResult = backend.Execute initialState program
            
            match executeResult with
            | Ok result ->
                // Should have two measurements
                Assert.Equal(2, result.MeasurementOutcomes.Length)
                
                // All probabilities should be valid
                Assert.All(result.MeasurementOutcomes, fun (_, prob) ->
                    Assert.InRange(prob, 0.0, 1.0)
                )
                
                // Should have messages for all operations
                Assert.Equal(6, result.Messages.Length)
            | Error err ->
                Assert.Fail($"Execute failed: {err.Message}")
        | Error err ->
            Assert.Fail($"Initialize failed: {err.Message}")
    }
    
    [<Fact>]
    let ``Execute program reports execution time`` () = task {
        let backend = TopologicalBackend.createSimulator AnyonSpecies.AnyonType.Ising 10
        
        let! initialStateResult = backend.Initialize AnyonSpecies.AnyonType.Ising 4
        
        match initialStateResult with
        | Ok initialState ->
            let program = [
                TopologicalBackend.Braid 0
                TopologicalBackend.Measure 0
            ]
            
            let! executeResult = backend.Execute initialState program
            
            match executeResult with
            | Ok result ->
                // Execution time should be positive and reasonable
                Assert.InRange(result.ExecutionTimeMs, 0.0, 10000.0) // Max 10 seconds
                Assert.True(result.ExecutionTimeMs > 0.0, "Execution time should be positive")
            | Error err ->
                Assert.Fail($"Execute failed: {err.Message}")
        | Error err ->
            Assert.Fail($"Initialize failed: {err.Message}")
    }
    
    // ========================================================================
    // FIBONACCI ANYON TESTS
    // ========================================================================
    
    [<Fact>]
    let ``Fibonacci backend initializes with τ anyons`` () = task {
        let backend = TopologicalBackend.createSimulator AnyonSpecies.AnyonType.Fibonacci 10
        
        let! stateResult = backend.Initialize AnyonSpecies.AnyonType.Fibonacci 4
        
        match stateResult with
        | Ok state ->
            Assert.NotNull(state)
            Assert.False(state.Terms.IsEmpty)
        | Error err ->
            Assert.Fail($"Initialize failed: {err.Message}")
    }
    
    [<Fact>]
    let ``Three Fibonacci anyons create valid fusion state`` () = task {
        // Business-meaningful: τ × τ = 1 + τ
        let backend = TopologicalBackend.createSimulator AnyonSpecies.AnyonType.Fibonacci 10
        
        let! stateResult = backend.Initialize AnyonSpecies.AnyonType.Fibonacci 3
        
        match stateResult with
        | Ok state ->
            Assert.Single(state.Terms) |> ignore
            let (_, fusionState) = state.Terms.[0]
            
            // Should be Fibonacci theory
            Assert.Equal(AnyonSpecies.AnyonType.Fibonacci, fusionState.AnyonType)
        | Error err ->
            Assert.Fail($"Initialize failed: {err.Message}")
    }
    
    [<Fact>]
    let ``Fibonacci anyons support braiding operations`` () = task {
        let backend = TopologicalBackend.createSimulator AnyonSpecies.AnyonType.Fibonacci 10
        
        let! stateResult = backend.Initialize AnyonSpecies.AnyonType.Fibonacci 4
        
        match stateResult with
        | Ok state ->
            let! braidedStateResult = backend.Braid 0 state
            
            match braidedStateResult with
            | Ok braidedState ->
                Assert.NotNull(braidedState)
                Assert.NotEqual(state, braidedState)
            | Error err ->
                Assert.Fail($"Braid failed: {err.Message}")
        | Error err ->
            Assert.Fail($"Initialize failed: {err.Message}")
    }
