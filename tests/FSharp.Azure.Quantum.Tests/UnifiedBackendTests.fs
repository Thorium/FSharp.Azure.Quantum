namespace FSharp.Azure.Quantum.Tests

open Xunit
open System.Numerics
open FSharp.Azure.Quantum.Core
open FSharp.Azure.Quantum.Core.BackendAbstraction
open FSharp.Azure.Quantum.Core.CircuitAbstraction
open FSharp.Azure.Quantum.Core.UnifiedBackendAbstraction
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
    let wrapCircuit (circuit: CircuitBuilder.CircuitBuilder) : ICircuit =
        CircuitWrapper(circuit) :> ICircuit
    
    // ========================================================================
    // LocalBackend Tests (Gate-Based Backend)
    // ========================================================================
    
    [<Fact>]
    let ``LocalBackend implements IUnifiedQuantumBackend`` () =
        let backend = LocalBackend.LocalBackend()
        Assert.IsAssignableFrom<IUnifiedQuantumBackend>(backend) |> ignore
    
    [<Fact>]
    let ``LocalBackend returns GateBased as native state type`` () =
        let backend = LocalBackend.LocalBackend() :> IUnifiedQuantumBackend
        Assert.Equal(QuantumStateType.GateBased, backend.NativeStateType)
    
    [<Fact>]
    let ``LocalBackend ExecuteToState returns StateVector`` () =
        let backend = LocalBackend.LocalBackend() :> IUnifiedQuantumBackend
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
        let backend = LocalBackend.LocalBackend() :> IUnifiedQuantumBackend
        
        match backend.InitializeState 3 with
        | Ok (QuantumState.StateVector sv) ->
            Assert.Equal(3, StateVector.numQubits sv)
            
            // Check that state is |000⟩
            let amp0 = StateVector.getAmplitude 0 sv
            Assert.Equal(Complex.One, amp0)
            
            // All other amplitudes should be zero
            for i in 1 .. 7 do
                let amp = StateVector.getAmplitude i sv
                Assert.True(Complex.magnitude amp < 1e-10, $"Amplitude {i} should be zero")
        | Ok _ ->
            Assert.True(false, "Expected StateVector")
        | Error err ->
            Assert.True(false, $"InitializeState failed: {err}")
    
    [<Fact>]
    let ``LocalBackend ApplyOperation applies gate correctly`` () =
        let backend = LocalBackend.LocalBackend() :> IUnifiedQuantumBackend
        
        match backend.InitializeState 1 with
        | Ok initialState ->
            // Apply Hadamard gate
            match backend.ApplyOperation (QuantumOperation.Gate (CircuitBuilder.H 0)) initialState with
            | Ok (QuantumState.StateVector sv) ->
                // After H|0⟩, we should have |+⟩ = (|0⟩ + |1⟩)/√2
                let amp0 = StateVector.getAmplitude 0 sv
                let amp1 = StateVector.getAmplitude 1 sv
                
                let expected = Complex(1.0 / sqrt 2.0, 0.0)
                Assert.True(Complex.abs (amp0 - expected) < 1e-10, "Amplitude 0 incorrect")
                Assert.True(Complex.abs (amp1 - expected) < 1e-10, "Amplitude 1 incorrect")
            | Ok _ ->
                Assert.True(false, "Expected StateVector")
            | Error err ->
                Assert.True(false, $"ApplyOperation failed: {err}")
        | Error err ->
            Assert.True(false, $"InitializeState failed: {err}")
    
    [<Fact>]
    let ``LocalBackend rejects braiding operations`` () =
        let backend = LocalBackend.LocalBackend() :> IUnifiedQuantumBackend
        
        // Braiding should not be supported by gate-based backend
        Assert.False(backend.SupportsOperation (QuantumOperation.Braid 0))
    
    [<Fact>]
    let ``LocalBackend backward compatibility with IQuantumBackend`` () =
        let backend = LocalBackend.LocalBackend() :> IQuantumBackend
        let circuit = createBellCircuit () |> wrapCircuit
        
        match backend.Execute circuit 100 with
        | Ok result ->
            Assert.Equal(100, result.NumShots)
            Assert.Equal("Local Simulator", result.BackendName)
            Assert.Equal(100, result.Measurements.Length)
        | Error err ->
            Assert.True(false, $"Execution failed: {err}")
    
    // ========================================================================
    // TopologicalBackend Tests (Topological Backend)
    // ========================================================================
    
    [<Fact>]
    let ``TopologicalBackend implements IUnifiedQuantumBackend`` () =
        let backend = TopologicalUnifiedBackend.TopologicalUnifiedBackend(AnyonSpecies.AnyonType.Ising, 20)
        Assert.IsAssignableFrom<IUnifiedQuantumBackend>(backend) |> ignore
    
    [<Fact>]
    let ``TopologicalBackend returns TopologicalBraiding as native state type`` () =
        let backend = TopologicalUnifiedBackend.TopologicalUnifiedBackend(AnyonSpecies.AnyonType.Ising, 20) :> IUnifiedQuantumBackend
        Assert.Equal(QuantumStateType.TopologicalBraiding, backend.NativeStateType)
    
    [<Fact>]
    let ``TopologicalBackend ExecuteToState returns FusionSuperposition`` () =
        let backend = TopologicalUnifiedBackend.TopologicalUnifiedBackend(AnyonSpecies.AnyonType.Ising, 20) :> IUnifiedQuantumBackend
        let circuit = createBellCircuit () |> wrapCircuit
        
        match backend.ExecuteToState circuit with
        | Ok (QuantumState.FusionSuperposition fs) ->
            Assert.NotEmpty(fs.BasisStates)
        | Ok _ ->
            Assert.True(false, "Expected FusionSuperposition, got different state type")
        | Error err ->
            Assert.True(false, $"Execution failed: {err}")
    
    [<Fact>]
    let ``TopologicalBackend InitializeState creates topological |0⟩^⊗n`` () =
        let backend = TopologicalUnifiedBackend.TopologicalUnifiedBackend(AnyonSpecies.AnyonType.Ising, 20) :> IUnifiedQuantumBackend
        
        match backend.InitializeState 3 with
        | Ok (QuantumState.FusionSuperposition fs) ->
            // Should have at least one fusion tree
            Assert.NotEmpty(fs.BasisStates)
        | Ok _ ->
            Assert.True(false, "Expected FusionSuperposition")
        | Error err ->
            Assert.True(false, $"InitializeState failed: {err}")
    
    [<Fact>]
    let ``TopologicalBackend ApplyOperation applies braid correctly`` () =
        let backend = TopologicalUnifiedBackend.TopologicalUnifiedBackend(AnyonSpecies.AnyonType.Ising, 20) :> IUnifiedQuantumBackend
        
        match backend.InitializeState 2 with
        | Ok initialState ->
            // Apply braiding operation
            match backend.ApplyOperation (QuantumOperation.Braid 0) initialState with
            | Ok (QuantumState.FusionSuperposition fs) ->
                Assert.NotEmpty(fs.BasisStates)
            | Ok _ ->
                Assert.True(false, "Expected FusionSuperposition")
            | Error err ->
                Assert.True(false, $"ApplyOperation failed: {err}")
        | Error err ->
            Assert.True(false, $"InitializeState failed: {err}")
    
    [<Fact>]
    let ``TopologicalBackend supports braiding operations`` () =
        let backend = TopologicalUnifiedBackend.TopologicalUnifiedBackend(AnyonSpecies.AnyonType.Ising, 20) :> IUnifiedQuantumBackend
        
        // Braiding should be supported by topological backend
        Assert.True(backend.SupportsOperation (QuantumOperation.Braid 0))
    
    [<Fact>]
    let ``TopologicalBackend supports gate compilation`` () =
        let backend = TopologicalUnifiedBackend.TopologicalUnifiedBackend(AnyonSpecies.AnyonType.Ising, 20) :> IUnifiedQuantumBackend
        
        // Clifford gates should be supported (compiled to braiding)
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
        | Ok (QuantumState.StateVector sv2) ->
            Assert.Equal(StateVector.numQubits sv, StateVector.numQubits sv2)
        | Ok _ ->
            Assert.True(false, "Conversion returned wrong type")
        | Error err ->
            Assert.True(false, $"Conversion failed: {err}")
    
    [<Fact>]
    let ``Convert StateVector to FusionSuperposition`` () =
        let sv = StateVector.init 2
        let state = QuantumState.StateVector sv
        
        match QuantumStateConversion.convert QuantumStateType.TopologicalBraiding state with
        | Ok (QuantumState.FusionSuperposition fs) ->
            Assert.NotEmpty(fs.BasisStates)
        | Ok _ ->
            Assert.True(false, "Conversion returned wrong type")
        | Error err ->
            Assert.True(false, $"Conversion failed: {err}")
    
    [<Fact>]
    let ``Round-trip conversion StateVector → Fusion → StateVector is lossless`` () =
        // Create initial state |00⟩
        let sv = StateVector.init 2
        let state = QuantumState.StateVector sv
        
        // Convert to fusion
        match QuantumStateConversion.convert QuantumStateType.TopologicalBraiding state with
        | Error err ->
            Assert.True(false, $"First conversion failed: {err}")
        | Ok fusionState ->
            // Convert back to state vector
            match QuantumStateConversion.convert QuantumStateType.GateBased fusionState with
            | Error err ->
                Assert.True(false, $"Second conversion failed: {err}")
            | Ok (QuantumState.StateVector sv2) ->
                // Check amplitudes match
                let n = StateVector.numQubits sv
                let dim = 1 <<< n
                
                for i in 0 .. dim - 1 do
                    let amp1 = StateVector.getAmplitude i sv
                    let amp2 = StateVector.getAmplitude i sv2
                    let diff = Complex.abs (amp1 - amp2)
                    Assert.True(diff < 1e-10, $"Amplitude {i} differs: {diff}")
            | Ok _ ->
                Assert.True(false, "Second conversion returned wrong type")
    
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
        let result =
            QuantumStateConversion.convert QuantumStateType.TopologicalBraiding state
            |> Result.bind (QuantumStateConversion.convert QuantumStateType.GateBased)
        
        match result with
        | Ok (QuantumState.StateVector sv2) ->
            // Check amplitudes match
            for i in 0 .. 1 do
                let amp1 = StateVector.getAmplitude i sv
                let amp2 = StateVector.getAmplitude i sv2
                let diff = Complex.abs (amp1 - amp2)
                Assert.True(diff < 1e-10, $"Amplitude {i} differs: {diff}")
        | Ok _ ->
            Assert.True(false, "Conversion returned wrong type")
        | Error err ->
            Assert.True(false, $"Round-trip conversion failed: {err}")
    
    // ========================================================================
    // Backend Interoperability Tests
    // ========================================================================
    
    [<Fact>]
    let ``Execute circuit on LocalBackend and convert to topological`` () =
        let localBackend = LocalBackend.LocalBackend() :> IUnifiedQuantumBackend
        let circuit = createBellCircuit () |> wrapCircuit
        
        match localBackend.ExecuteToState circuit with
        | Ok state ->
            // Convert to topological representation
            match QuantumStateConversion.convert QuantumStateType.TopologicalBraiding state with
            | Ok (QuantumState.FusionSuperposition fs) ->
                Assert.NotEmpty(fs.BasisStates)
            | Ok _ ->
                Assert.True(false, "Conversion returned wrong type")
            | Error err ->
                Assert.True(false, $"Conversion failed: {err}")
        | Error err ->
            Assert.True(false, $"Execution failed: {err}")
    
    [<Fact>]
    let ``Execute circuit on TopologicalBackend and convert to gate-based`` () =
        let topBackend = TopologicalUnifiedBackend.TopologicalUnifiedBackend(AnyonSpecies.AnyonType.Ising, 20) :> IUnifiedQuantumBackend
        let circuit = createBellCircuit () |> wrapCircuit
        
        match topBackend.ExecuteToState circuit with
        | Ok state ->
            // Convert to gate-based representation
            match QuantumStateConversion.convert QuantumStateType.GateBased state with
            | Ok (QuantumState.StateVector sv) ->
                Assert.Equal(2, StateVector.numQubits sv)
            | Ok _ ->
                Assert.True(false, "Conversion returned wrong type")
            | Error err ->
                Assert.True(false, $"Conversion failed: {err}")
        | Error err ->
            Assert.True(false, $"Execution failed: {err}")
    
    [<Fact>]
    let ``Switching backends mid-computation via state conversion`` () =
        let localBackend = LocalBackend.LocalBackend() :> IUnifiedQuantumBackend
        let topBackend = TopologicalUnifiedBackend.TopologicalUnifiedBackend(AnyonSpecies.AnyonType.Ising, 20) :> IUnifiedQuantumBackend
        
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
                match QuantumStateConversion.convert QuantumStateType.TopologicalBraiding state2 with
                | Error err ->
                    Assert.True(false, $"Conversion failed: {err}")
                | Ok topState ->
                    // Continue execution on topological backend
                    match topBackend.ApplyOperation (QuantumOperation.Gate (CircuitBuilder.X 1)) topState with
                    | Ok (QuantumState.FusionSuperposition fs) ->
                        Assert.NotEmpty(fs.BasisStates)
                    | Ok _ ->
                        Assert.True(false, "Expected FusionSuperposition")
                    | Error err ->
                        Assert.True(false, $"Second operation failed: {err}")
    
    // ========================================================================
    // Smart Dispatch Tests
    // ========================================================================
    
    [<Fact>]
    let ``UnifiedBackend applyWithConversion avoids unnecessary conversion`` () =
        let backend = LocalBackend.LocalBackend() :> IUnifiedQuantumBackend
        
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
        let backend = LocalBackend.LocalBackend() :> IUnifiedQuantumBackend
        
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
        let backend = LocalBackend.LocalBackend() :> IUnifiedQuantumBackend
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
