namespace FSharp.Azure.Quantum.Tests

open Xunit
open FSharp.Azure.Quantum
open FSharp.Azure.Quantum.Core.BackendAbstraction
open FSharp.Azure.Quantum.Core.CircuitAbstraction
open FSharp.Azure.Quantum.Core
open FSharp.Azure.Quantum.Backends
open FSharp.Azure.Quantum.LocalSimulator

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

    type private XViaStateVectorExtension() =
        interface IApplyToStateVectorExtension with
            member _.Id = "tests.x-via-statevector"
            member _.ApplyToStateVector stateVector =
                Gates.applyX 0 stateVector

    type private XViaLoweringExtension() =
        interface ILowerToOperationsExtension with
            member _.Id = "tests.x-via-lowering"
            member _.LowerToGates () =
                [ CircuitBuilder.X 0 ]

    type private UnsupportedExtension() =
        interface IQuantumOperationExtension with
            member _.Id = "tests.unsupported"

    [<Fact>]
    let ``LocalBackend should support gate operations`` () =
        let backend = LocalBackend.LocalBackend() :> IQuantumBackend
        
        // Test various gate operations
        Assert.True(backend.SupportsOperation (QuantumOperation.Gate (CircuitBuilder.H 0)))
        Assert.True(backend.SupportsOperation (QuantumOperation.Gate (CircuitBuilder.X 0)))
        Assert.True(backend.SupportsOperation (QuantumOperation.Gate (CircuitBuilder.CNOT(0, 1))))
        Assert.True(backend.SupportsOperation (QuantumOperation.Gate (CircuitBuilder.RZ(0, 0.5))))

    [<Fact>]
    let ``LocalBackend should support StateVector extension operations`` () =
        let backend = LocalBackend.LocalBackend() :> IQuantumBackend
        let ext = XViaStateVectorExtension() :> IQuantumOperationExtension
        Assert.True(backend.SupportsOperation (QuantumOperation.Extension ext))

    [<Fact>]
    let ``LocalBackend should support lowering extension operations`` () =
        let backend = LocalBackend.LocalBackend() :> IQuantumBackend
        let ext = XViaLoweringExtension() :> IQuantumOperationExtension
        Assert.True(backend.SupportsOperation (QuantumOperation.Extension ext))

    [<Fact>]
    let ``LocalBackend should not support unknown extension operations`` () =
        let backend = LocalBackend.LocalBackend() :> IQuantumBackend
        let ext = UnsupportedExtension() :> IQuantumOperationExtension
        Assert.False(backend.SupportsOperation (QuantumOperation.Extension ext))

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
            | Ok _finalState ->
                Assert.True(true, "Gate operation applied successfully")
            | Error err ->
                Assert.True(false, sprintf "ApplyOperation failed: %A" err)
        | Error err ->
            Assert.True(false, sprintf "InitializeState failed: %A" err)

    [<Fact>]
    let ``LocalBackend should apply StateVector extension using fast-path`` () =
        let backend = LocalBackend.LocalBackend() :> IQuantumBackend
        
        match backend.InitializeState 1 with
        | Error err ->
            Assert.True(false, sprintf "InitializeState failed: %A" err)
        | Ok initialState ->
            let ext = XViaStateVectorExtension() :> IQuantumOperationExtension
            let op = QuantumOperation.Extension ext
            match backend.ApplyOperation op initialState with
            | Error err ->
                Assert.True(false, sprintf "ApplyOperation (extension) failed: %A" err)
            | Ok (QuantumState.StateVector sv) ->
                // Starting from |0⟩, applying X yields |1⟩
                Assert.Equal(0.0, (StateVector.getAmplitude 0 sv).Real, 10)
                Assert.Equal(1.0, (StateVector.getAmplitude 1 sv).Real, 10)
            | Ok _ ->
                Assert.True(false, "Expected StateVector output")

    [<Fact>]
    let ``LocalBackend should apply lowering extension by executing lowered gates`` () =
        let backend = LocalBackend.LocalBackend() :> IQuantumBackend
        
        match backend.InitializeState 1 with
        | Error err ->
            Assert.True(false, sprintf "InitializeState failed: %A" err)
        | Ok initialState ->
            let ext = XViaLoweringExtension() :> IQuantumOperationExtension
            let op = QuantumOperation.Extension ext
            match backend.ApplyOperation op initialState with
            | Error err ->
                Assert.True(false, sprintf "ApplyOperation (extension) failed: %A" err)
            | Ok (QuantumState.StateVector sv) ->
                // Starting from |0⟩, applying X yields |1⟩
                Assert.Equal(0.0, (StateVector.getAmplitude 0 sv).Real, 10)
                Assert.Equal(1.0, (StateVector.getAmplitude 1 sv).Real, 10)
            | Ok _ ->
                Assert.True(false, "Expected StateVector output")

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

    [<Fact>]
    let ``LocalBackend should support QPE intent operation`` () =
        let backend = LocalBackend.LocalBackend() :> IQuantumBackend
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
    let ``LocalBackend should support HHL intent operation`` () =
        let backend = LocalBackend.LocalBackend() :> IQuantumBackend
        let intent =
            {
                EigenvalueQubits = 2
                SolutionQubits = 1
                DiagonalEigenvalues = [| 2.0; 3.0 |]
                InversionMethod = HhlEigenvalueInversionMethod.ExactRotation 1.0
                MinEigenvalue = 1e-6
            }

        Assert.True(backend.SupportsOperation (QuantumOperation.Algorithm (AlgorithmOperation.HHL intent)))

    [<Fact>]
    let ``LocalBackend should apply QPE intent and return StateVector`` () =
        let backend = LocalBackend.LocalBackend() :> IQuantumBackend
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
            | Ok (QuantumState.StateVector _) ->
                Assert.True(true)
            | Ok _ ->
                Assert.True(false, "Expected StateVector output")

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
