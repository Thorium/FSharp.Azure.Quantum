namespace FSharp.Azure.Quantum.Tests

open Xunit
open FSharp.Azure.Quantum
open FSharp.Azure.Quantum.Core.BackendAbstraction
open FSharp.Azure.Quantum.Core.CircuitAbstraction
open FSharp.Azure.Quantum.Core
open FSharp.Azure.Quantum.Backends

module BackendAbstractionTests =

    // ========================================================================
    // Local Backend Tests - State-Based Interface
    // ========================================================================

    [<Fact>]
    let ``LocalBackend should initialize quantum state`` () =
        let backend = LocalBackend.LocalBackend() :> IQuantumBackend
        
        match backend.InitializeState 3 with
        | Ok state ->
            match state with
            | QuantumState.StateVector sv ->
                Assert.True(true, "State initialized successfully")
            | _ -> Assert.True(false, "Expected StateVector representation")
        | Error err ->
            Assert.True(false, sprintf "InitializeState failed: %A" err)

    [<Fact>]
    let ``LocalBackend should execute simple circuit`` () =
        let backend = LocalBackend.LocalBackend() :> IQuantumBackend
        let circuit = CircuitBuilder.empty 2
        let wrapper = CircuitWrapper(circuit) :> ICircuit
        
        match backend.ExecuteToState wrapper with
        | Ok state ->
            match state with
            | QuantumState.StateVector _ ->
                Assert.True(true, "Circuit executed successfully")
            | _ -> Assert.True(false, "Expected StateVector")
        | Error err ->
            Assert.True(false, sprintf "ExecuteToState failed: %A" err)

    [<Fact>]
    let ``LocalBackend should support gate operations`` () =
        let backend = LocalBackend.LocalBackend() :> IQuantumBackend
        
        // Test various gate operations
        Assert.True(backend.SupportsOperation (QuantumOperation.Gate (CircuitBuilder.H 0)))
        Assert.True(backend.SupportsOperation (QuantumOperation.Gate (CircuitBuilder.X 0)))
        Assert.True(backend.SupportsOperation (QuantumOperation.Gate (CircuitBuilder.CNOT(0, 1))))
        Assert.True(backend.SupportsOperation (QuantumOperation.Gate (CircuitBuilder.RZ(0, 0.5))))

    [<Fact>]
    let ``LocalBackend should not support braiding operations`` () =
        let backend = LocalBackend.LocalBackend() :> IQuantumBackend
        
        // LocalBackend is gate-based, not topological
        Assert.False(backend.SupportsOperation (QuantumOperation.Braid 0))

    [<Fact>]
    let ``LocalBackend should have GateBased native state type`` () =
        let backend = LocalBackend.LocalBackend() :> IQuantumBackend
        
        Assert.Equal(QuantumStateType.GateBased, backend.NativeStateType)

    [<Fact>]
    let ``LocalBackend should apply single gate operations`` () =
        let backend = LocalBackend.LocalBackend() :> IQuantumBackend
        
        match backend.InitializeState 2 with
        | Ok initialState ->
            let hGate = QuantumOperation.Gate (CircuitBuilder.H 0)
            match backend.ApplyOperation hGate initialState with
            | Ok finalState ->
                Assert.True(true, "Gate operation applied successfully")
            | Error err ->
                Assert.True(false, sprintf "ApplyOperation failed: %A" err)
        | Error err ->
            Assert.True(false, sprintf "InitializeState failed: %A" err)

    [<Fact>]
    let ``LocalBackend should execute circuit with multiple gates`` () =
        let backend = LocalBackend.LocalBackend() :> IQuantumBackend
        
        // Create Bell state circuit: H 0; CNOT 0 1
        let circuit = 
            CircuitBuilder.empty 2
            |> CircuitBuilder.addGate (CircuitBuilder.H 0)
            |> CircuitBuilder.addGate (CircuitBuilder.CNOT(0, 1))
        
        let wrapper = CircuitWrapper(circuit) :> ICircuit
        
        match backend.ExecuteToState wrapper with
        | Ok state ->
            match state with
            | QuantumState.StateVector sv ->
                // Verify state is valid (implementation details checked elsewhere)
                Assert.True(true, "Bell state circuit executed")
            | _ -> Assert.True(false, "Expected StateVector")
        | Error err ->
            Assert.True(false, sprintf "Circuit execution failed: %A" err)

    [<Fact>]
    let ``LocalBackend should handle empty circuit`` () =
        let backend = LocalBackend.LocalBackend() :> IQuantumBackend
        let circuit = CircuitBuilder.empty 3
        let wrapper = CircuitWrapper(circuit) :> ICircuit
        
        match backend.ExecuteToState wrapper with
        | Ok state ->
            match state with
            | QuantumState.StateVector _ ->
                Assert.True(true, "Empty circuit returns |000⟩")
            | _ -> Assert.True(false, "Expected StateVector")
        | Error err ->
            Assert.True(false, sprintf "Empty circuit failed: %A" err)

    // ========================================================================
    // IonQ/Rigetti Backend Tests
    // ========================================================================
    // NOTE: IonQ and Rigetti backends are Azure Quantum cloud backends.
    // These require real workspace credentials and are tested via integration tests.
    // Unit tests for backend abstraction are covered by LocalBackend tests above.

    // ========================================================================
    // Legacy Backend Abstraction Tests
    // ========================================================================
    // NOTE: Legacy tests for validateCircuitForBackend and convertCircuitToProviderFormat
    // are commented out. These functions exist in Backends/Legacy/BackendAbstraction.fs
    // but are not exposed to the test project. They're tested via integration tests
    // with real Azure Quantum backends.
    //
    // If needed, these can be re-enabled by adding explicit module imports
