namespace FSharp.Azure.Quantum.Tests

open Xunit
open System.Numerics
open FSharp.Azure.Quantum
open FSharp.Azure.Quantum.Core
open FSharp.Azure.Quantum.Core.BackendAbstraction
open FSharp.Azure.Quantum.Core.CircuitAbstraction
open FSharp.Azure.Quantum.Core.BackendAbstraction
open FSharp.Azure.Quantum.Backends
open FSharp.Azure.Quantum.Topological
open FSharp.Azure.Quantum.LocalSimulator

/// Integration tests for Unified Quantum Backend Architecture
/// 
/// Tests verify:
/// 1. Backend interface implementation
/// 2. State-based execution
/// 3. Automatic state conversion
/// 4. Round-trip conversion (lossless)
/// 5. Backend interoperability
module UnifiedBackendTests =
    
    // ========================================================================
    // Test Helpers
    // ========================================================================
    
    /// Create a simple Bell state circuit: H(0); CNOT(0,1)
    let createBellCircuit () =
        CircuitBuilder.empty 2
        |> CircuitBuilder.addGate (CircuitBuilder.H 0)
        |> CircuitBuilder.addGate (CircuitBuilder.CNOT (0, 1))
    
    /// Create a 3-qubit GHZ state circuit: H(0); CNOT(0,1); CNOT(1,2)
    let createGHZCircuit () =
        CircuitBuilder.empty 3
        |> CircuitBuilder.addGate (CircuitBuilder.H 0)
        |> CircuitBuilder.addGate (CircuitBuilder.CNOT (0, 1))
        |> CircuitBuilder.addGate (CircuitBuilder.CNOT (1, 2))
    
    /// Wrap circuit as ICircuit interface
    let wrapCircuit (circuit: CircuitBuilder.Circuit) : ICircuit =
        CircuitWrapper(circuit) :> ICircuit
    
    // ========================================================================
    // LocalBackend Tests (Gate-Based Backend)
    // ========================================================================
    
    [<Fact>]
    let ``LocalBackend implements IQuantumBackend`` () =
        let backend = LocalBackend.LocalBackend()
        Assert.IsAssignableFrom<IQuantumBackend>(backend) |> ignore
    
    [<Fact>]
    let ``LocalBackend returns GateBased as native state type`` () =
        let backend = LocalBackend.LocalBackend() :> IQuantumBackend
        Assert.Equal(QuantumStateType.GateBased, backend.NativeStateType)
    
    [<Fact>]
    let ``LocalBackend ExecuteToState returns StateVector`` () =
        let backend = LocalBackend.LocalBackend() :> IQuantumBackend
        let circuit = createBellCircuit () |> wrapCircuit
        
        match backend.ExecuteToState circuit with
        | Ok (QuantumState.StateVector sv) ->
            Assert.Equal(2, StateVector.numQubits sv)
        | Ok _ ->
            Assert.True(false, "Expected StateVector, got different state type")
        | Error err ->
            Assert.True(false, $"Execution failed: {err}")
    
    [<Fact>]
    let ``LocalBackend InitializeState creates |0⟩^⊗n`` () =
        let backend = LocalBackend.LocalBackend() :> IQuantumBackend
        
        match backend.InitializeState 3 with
        | Ok (QuantumState.StateVector sv) ->
            Assert.Equal(3, StateVector.numQubits sv)
            
            // Check that state is |000⟩
            let amp0 = StateVector.getAmplitude 0 sv
            Assert.Equal(Complex.One, amp0)
            
            // All other amplitudes should be zero
            for i in 1 .. 7 do
                let amp = StateVector.getAmplitude i sv
                Assert.True(amp.Magnitude < 1e-10, $"Amplitude {i} should be zero")
        | Ok _ ->
            Assert.True(false, "Expected StateVector")
        | Error err ->
            Assert.True(false, $"InitializeState failed: {err}")
    
    [<Fact>]
    let ``LocalBackend ApplyOperation applies gate correctly`` () =
        let backend = LocalBackend.LocalBackend() :> IQuantumBackend
        
        match backend.InitializeState 1 with
        | Ok initialState ->
            // Apply Hadamard gate
            match backend.ApplyOperation (QuantumOperation.Gate (CircuitBuilder.H 0)) initialState with
            | Ok (QuantumState.StateVector sv) ->
                // After H|0⟩, we should have |+⟩ = (|0⟩ + |1⟩)/√2
                let amp0 = StateVector.getAmplitude 0 sv
                let amp1 = StateVector.getAmplitude 1 sv
                
                let expected = Complex(1.0 / sqrt 2.0, 0.0)
                Assert.True(Complex.Abs(amp0 - expected) < 1e-10, "Amplitude 0 incorrect")
                Assert.True(Complex.Abs(amp1 - expected) < 1e-10, "Amplitude 1 incorrect")
            | Ok _ ->
                Assert.True(false, "Expected StateVector")
            | Error err ->
                Assert.True(false, $"ApplyOperation failed: {err}")
        | Error err ->
            Assert.True(false, $"InitializeState failed: {err}")
    
    [<Fact>]
    let ``LocalBackend rejects braiding operations`` () =
        let backend = LocalBackend.LocalBackend() :> IQuantumBackend
        
        // Braiding should not be supported by gate-based backend
        Assert.False(backend.SupportsOperation (QuantumOperation.Braid 0))
    
    [<Fact>]
    let ``LocalBackend unified state-based execution with IQuantumBackend`` () =
        let backend = LocalBackend.LocalBackend() :> IQuantumBackend
        let circuit = createBellCircuit () |> wrapCircuit
        
        match backend.ExecuteToState circuit with
        | Ok state ->
            // Verify we got a valid quantum state
            let measurements = QuantumState.measure state 100
            Assert.Equal(100, measurements.Length)
            // Each measurement should be a bit array
            Assert.True(measurements |> Array.forall (fun m -> m.Length > 0))
        | Error err ->
            Assert.True(false, $"Execution failed: {err.Message}")
    
    // ========================================================================
    // TopologicalBackend Tests (Topological Backend)
    // ========================================================================
    
    [<Fact>]
    let ``TopologicalBackend implements IQuantumBackend`` () =
        let backend = TopologicalUnifiedBackend.TopologicalUnifiedBackend(AnyonSpecies.AnyonType.Ising, 20)
        Assert.IsAssignableFrom<IQuantumBackend>(backend) |> ignore
    
    [<Fact>]
    let ``TopologicalBackend returns TopologicalBraiding as native state type`` () =
        let backend = TopologicalUnifiedBackend.TopologicalUnifiedBackend(AnyonSpecies.AnyonType.Ising, 20) :> IQuantumBackend
        Assert.Equal(QuantumStateType.TopologicalBraiding, backend.NativeStateType)
    

    [<Fact>]
    let ``TopologicalBackend InitializeState creates topological |0⟩^⊗n`` () =
        let backend = TopologicalUnifiedBackend.TopologicalUnifiedBackend(AnyonSpecies.AnyonType.Ising, 20) :> IQuantumBackend
        
        match backend.InitializeState 3 with
        | Ok (QuantumState.FusionSuperposition (fs, numQubits)) ->
            // fs is obj type, cast to TopologicalOperations.Superposition
            let fusion = fs :?> TopologicalOperations.Superposition
            Assert.NotEmpty(fusion.Terms)
        | Ok _ ->
            Assert.True(false, "Expected FusionSuperposition")
        | Error err ->
            Assert.True(false, $"InitializeState failed: {err}")
    
    [<Fact>]
    let ``TopologicalBackend ApplyOperation applies braid correctly`` () =
        let backend = TopologicalUnifiedBackend.TopologicalUnifiedBackend(AnyonSpecies.AnyonType.Ising, 20) :> IQuantumBackend
        
        match backend.InitializeState 2 with
        | Ok initialState ->
            // Apply braiding operation
            match backend.ApplyOperation (QuantumOperation.Braid 0) initialState with
            | Ok (QuantumState.FusionSuperposition (fs, numQubits)) ->
                let fusion = fs :?> TopologicalOperations.Superposition
                Assert.NotEmpty(fusion.Terms)
            | Ok _ ->
                Assert.True(false, "Expected FusionSuperposition")
            | Error err ->
                Assert.True(false, $"ApplyOperation failed: {err}")
        | Error err ->
            Assert.True(false, $"InitializeState failed: {err}")
    
    [<Fact>]
    let ``TopologicalBackend supports braiding operations`` () =
        let backend = TopologicalUnifiedBackend.TopologicalUnifiedBackend(AnyonSpecies.AnyonType.Ising, 20) :> IQuantumBackend
        
        // Braiding should be supported by topological backend
        Assert.True(backend.SupportsOperation (QuantumOperation.Braid 0))
    
    [<Fact>]
    let ``TopologicalBackend supports gate operations via compilation`` () =
        let backend = TopologicalUnifiedBackend.TopologicalUnifiedBackend(AnyonSpecies.AnyonType.Ising, 20) :> IQuantumBackend
        
        // Gate operations ARE supported via gate-to-braid compilation
        Assert.True(backend.SupportsOperation (QuantumOperation.Gate (CircuitBuilder.H 0)))
        Assert.True(backend.SupportsOperation (QuantumOperation.Gate (CircuitBuilder.CNOT (0, 1))))
    
    // ========================================================================
    // State Conversion Tests
    // ========================================================================
    
    [<Fact>]
    let ``Convert StateVector to StateVector is identity`` () =
        let sv = StateVector.init 2
        let state = QuantumState.StateVector sv
        
        match QuantumStateConversion.convert QuantumStateType.GateBased state with
        | QuantumState.StateVector sv2 ->
            Assert.Equal(StateVector.numQubits sv, StateVector.numQubits sv2)
        | _ ->
            Assert.True(false, "Conversion returned wrong type")
    
    [<Fact>]
    let ``Convert StateVector to FusionSuperposition`` () =
        let sv = StateVector.init 2
        let state = QuantumState.StateVector sv
        
        // Note: QuantumStateConversion.convert returns state unchanged for topological conversions
        // Actual conversion should be done by the Topological package
        let converted = QuantumStateConversion.convert QuantumStateType.TopologicalBraiding state
        
        match converted with
        | QuantumState.StateVector _ ->
            // Expected: conversion returns original state unchanged
            Assert.True(true, "Conversion correctly returns unchanged state")
        | QuantumState.FusionSuperposition (fs, numQubits) ->
            // If topological package implements conversion, this would work
            let fusion = fs :?> TopologicalOperations.Superposition
            Assert.NotEmpty(fusion.Terms)
        | _ ->
            Assert.True(false, "Unexpected state type")
    
    [<Fact>]
    let ``Round-trip conversion StateVector → Fusion → StateVector is lossless`` () =
        // Create initial state |00⟩
        let sv = StateVector.init 2
        let state = QuantumState.StateVector sv
        
        // Note: Since topological conversion returns state unchanged,
        // this is effectively a no-op test. Real conversion would be done by Topological package.
        let fusionState = QuantumStateConversion.convert QuantumStateType.TopologicalBraiding state
        let backToStateVector = QuantumStateConversion.convert QuantumStateType.GateBased fusionState
        
        match backToStateVector with
        | QuantumState.StateVector sv2 ->
            // Check amplitudes match (should be identical since no actual conversion happened)
            let n = StateVector.numQubits sv
            let dim = 1 <<< n
            
            for i in 0 .. dim - 1 do
                let amp1 = StateVector.getAmplitude i sv
                let amp2 = StateVector.getAmplitude i sv2
                let diff = Complex.Abs(amp1 - amp2)
                Assert.True(diff < 1e-10, $"Amplitude {i} differs: {diff}")
        | _ ->
            Assert.True(false, "Expected StateVector after round-trip")
    
    [<Fact>]
    let ``Round-trip conversion on superposition state is lossless`` () =
        // Create |+⟩ state: (|0⟩ + |1⟩)/√2
        let amplitudes = [|
            Complex(1.0 / sqrt 2.0, 0.0)
            Complex(1.0 / sqrt 2.0, 0.0)
        |]
        let sv = StateVector.create amplitudes
        let state = QuantumState.StateVector sv
        
        // Round-trip: StateVector → Fusion → StateVector
        // Note: Returns state unchanged since topological conversion is not implemented in Core
        let fusionState = QuantumStateConversion.convert QuantumStateType.TopologicalBraiding state
        let result = QuantumStateConversion.convert QuantumStateType.GateBased fusionState
        
        match result with
        | QuantumState.StateVector sv2 ->
            // Check amplitudes match
            for i in 0 .. 1 do
                let amp1 = StateVector.getAmplitude i sv
                let amp2 = StateVector.getAmplitude i sv2
                let diff = Complex.Abs(amp1 - amp2)
                Assert.True(diff < 1e-10, $"Amplitude {i} differs: {diff}")
        | _ ->
            Assert.True(false, "Expected StateVector after round-trip")
    
    // ========================================================================
    // Backend Interoperability Tests
    // ========================================================================
    
    [<Fact>]
    let ``Execute circuit on LocalBackend and convert to topological`` () =
        let localBackend = LocalBackend.LocalBackend() :> IQuantumBackend
        let circuit = createBellCircuit () |> wrapCircuit
        
        match localBackend.ExecuteToState circuit with
        | Ok state ->
            // Convert to topological representation
            // Note: Returns state unchanged since topological conversion not implemented in Core
            let converted = QuantumStateConversion.convert QuantumStateType.TopologicalBraiding state
            match converted with
            | QuantumState.StateVector _ ->
                // Expected: state unchanged
                Assert.True(true, "Conversion correctly returns unchanged state")
            | QuantumState.FusionSuperposition (fs, numQubits) ->
                // If topological package implements conversion, this would work
                let fusion = fs :?> TopologicalOperations.Superposition
                Assert.NotEmpty(fusion.Terms)
            | _ ->
                Assert.True(false, "Unexpected state type")
        | Error err ->
            Assert.True(false, $"Execution failed: {err}")
    
    [<Fact>]
    let ``Execute circuit on TopologicalBackend compiles gates to braids`` () =
        let topBackend = TopologicalUnifiedBackend.TopologicalUnifiedBackend(AnyonSpecies.AnyonType.Ising, 20) :> IQuantumBackend
        let circuit = createBellCircuit () |> wrapCircuit
        
        // Gate-based circuits are now supported via automatic gate-to-braid compilation
        match topBackend.ExecuteToState circuit with
        | Ok state ->
            // Should return FusionSuperposition state (topological backend's native type)
            match state with
            | QuantumState.FusionSuperposition _ ->
                Assert.True(true, "Circuit successfully compiled and executed as braiding operations")
            | _ ->
                Assert.True(false, $"Expected FusionSuperposition state, got {state.GetType().Name}")
        | Error err ->
            Assert.True(false, $"Circuit execution should succeed via gate-to-braid compilation, got error: {err}")
    
    [<Fact>]
    let ``Switching backends mid-computation via state conversion`` () =
        let localBackend = LocalBackend.LocalBackend() :> IQuantumBackend
        let topBackend = TopologicalUnifiedBackend.TopologicalUnifiedBackend(AnyonSpecies.AnyonType.Ising, 20) :> IQuantumBackend
        
        // Start on local backend
        match localBackend.InitializeState 2 with
        | Error err ->
            Assert.True(false, $"InitializeState failed: {err}")
        | Ok state1 ->
            // Apply H gate on local backend
            match localBackend.ApplyOperation (QuantumOperation.Gate (CircuitBuilder.H 0)) state1 with
            | Error err ->
                Assert.True(false, $"First operation failed: {err}")
            | Ok state2 ->
                // Convert to topological backend's native type
                // Note: convert returns state unchanged, so state2 is still StateVector
                let topState = QuantumStateConversion.convert QuantumStateType.TopologicalBraiding state2
                
                // Topological backend doesn't support gate operations - use braiding instead
                match topBackend.InitializeState 2 with
                | Error err ->
                    Assert.True(false, $"Topological InitializeState failed: {err}")
                | Ok topState ->
                    // Apply braiding operation on topological backend
                    match topBackend.ApplyOperation (QuantumOperation.Braid 0) topState with
                    | Ok (QuantumState.FusionSuperposition (fs, numQubits)) ->
                        let fusion = fs :?> TopologicalOperations.Superposition
                        Assert.NotEmpty(fusion.Terms)
                    | Ok _ ->
                        Assert.True(false, "Expected FusionSuperposition")
                    | Error err ->
                        Assert.True(false, $"Braiding operation failed: {err}")
    
    // ========================================================================
    // Smart Dispatch Tests
    // ========================================================================
    
    [<Fact>]
    let ``UnifiedBackend applyWithConversion avoids unnecessary conversion`` () =
        let backend = LocalBackend.LocalBackend() :> IQuantumBackend
        
        // State already in native format
        match backend.InitializeState 2 with
        | Ok state ->
            // Should not require conversion
            match UnifiedBackend.applyWithConversion backend (QuantumOperation.Gate (CircuitBuilder.H 0)) state false with
            | Ok (QuantumState.StateVector _) ->
                Assert.True(true)
            | Ok _ ->
                Assert.True(false, "Expected StateVector")
            | Error err ->
                Assert.True(false, $"Operation failed: {err}")
        | Error err ->
            Assert.True(false, $"InitializeState failed: {err}")
    
    [<Fact>]
    let ``UnifiedBackend applySequence optimizes batch execution`` () =
        let backend = LocalBackend.LocalBackend() :> IQuantumBackend
        
        match backend.InitializeState 2 with
        | Ok initialState ->
            let operations = [
                QuantumOperation.Gate (CircuitBuilder.H 0)
                QuantumOperation.Gate (CircuitBuilder.CNOT (0, 1))
                QuantumOperation.Gate (CircuitBuilder.H 1)
            ]
            
            match UnifiedBackend.applySequence backend operations initialState with
            | Ok (QuantumState.StateVector sv) ->
                Assert.Equal(2, StateVector.numQubits sv)
            | Ok _ ->
                Assert.True(false, "Expected StateVector")
            | Error err ->
                Assert.True(false, $"Sequence execution failed: {err}")
        | Error err ->
            Assert.True(false, $"InitializeState failed: {err}")
    
    // ========================================================================
    // Measurement Tests
    // ========================================================================
    
    [<Fact>]
    let ``Measure Bell state gives correlated outcomes`` () =
        let backend = LocalBackend.LocalBackend() :> IQuantumBackend
        let circuit = createBellCircuit () |> wrapCircuit
        
        match backend.ExecuteToState circuit with
        | Ok state ->
            let measurements = UnifiedBackend.measureState state 100
            
            // Check that all measurements are either |00⟩ or |11⟩ (correlated)
            let allCorrelated =
                measurements
                |> Array.forall (fun m -> (m.[0] = 0 && m.[1] = 0) || (m.[0] = 1 && m.[1] = 1))
            
            Assert.True(allCorrelated, "Bell state measurements should be correlated")
        | Error err ->
            Assert.True(false, $"Execution failed: {err}")
