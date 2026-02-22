namespace FSharp.Azure.Quantum.Tests

open System
open System.Net.Http
open System.Numerics
open System.Threading
open System.Threading.Tasks
open Xunit
open FSharp.Azure.Quantum.Core
open FSharp.Azure.Quantum.Core.BackendAbstraction
open FSharp.Azure.Quantum.LocalSimulator
open FSharp.Azure.Quantum.Backends

/// Tests for CloudBackendHelpers and cloud IQuantumBackend wrapper classes.
///
/// Covers:
///   - Histogram → QuantumState conversion
///   - Qubit inference from histogram
///   - Operation support checking
///   - Interface compliance for all 4 cloud backends
///   - Factory functions
///   - Cloud backend limitations (ApplyOperation always returns Error)
module CloudBackendTests =

    // ============================================================================
    // TEST HELPERS
    // ============================================================================

    /// Create a dummy HttpClient for backend construction.
    /// Cloud backends require an HttpClient but we don't actually call HTTP in these tests.
    let private createDummyHttpClient () =
        new HttpClient()

    /// Create all 4 cloud backends for parametric testing.
    let private createAllBackends () =
        let httpClient = createDummyHttpClient ()
        let workspaceUrl = "https://test.quantum.azure.com/subscriptions/test/resourceGroups/test/providers/Microsoft.Quantum/Workspaces/test"
        [|
            CloudBackends.RigettiCloudBackend(httpClient, workspaceUrl, "rigetti.sim.qvm", 1000) :> IQuantumBackend
            CloudBackends.IonQCloudBackend(httpClient, workspaceUrl, "ionq.simulator", 1000) :> IQuantumBackend
            CloudBackends.QuantinuumCloudBackend(httpClient, workspaceUrl, "quantinuum.sim.h1-1sc", 1000) :> IQuantumBackend
            CloudBackends.AtomComputingCloudBackend(httpClient, workspaceUrl, "atom-computing.sim", 1000) :> IQuantumBackend
        |]

    // ============================================================================
    // HISTOGRAM → QUANTUM STATE CONVERSION TESTS
    // ============================================================================

    [<Fact>]
    let ``histogramToQuantumState converts equal Bell state histogram correctly`` () =
        // Arrange: 50/50 measurement of |00⟩ and |11⟩ (approximate Bell state)
        let histogram = Map.ofList [ ("00", 500); ("11", 500) ]

        // Act
        let result = CloudBackendHelpers.histogramToQuantumState histogram 2

        // Assert
        match result with
        | QuantumState.StateVector sv ->
            Assert.Equal(4, StateVector.dimension sv) // 2 qubits → 4 amplitudes
            // |00⟩ amplitude ≈ sqrt(500/1000) ≈ 0.707
            let amp0 = StateVector.getAmplitude 0 sv
            Assert.True(abs (amp0.Real - sqrt 0.5) < 1e-10, sprintf "Expected ~0.707 for |00⟩, got %f" amp0.Real)
            Assert.Equal(0.0, amp0.Imaginary)
            // |01⟩ and |10⟩ should be zero
            Assert.Equal(Complex.Zero, StateVector.getAmplitude 1 sv)
            Assert.Equal(Complex.Zero, StateVector.getAmplitude 2 sv)
            // |11⟩ amplitude ≈ sqrt(500/1000) ≈ 0.707
            let amp3 = StateVector.getAmplitude 3 sv
            Assert.True(abs (amp3.Real - sqrt 0.5) < 1e-10, sprintf "Expected ~0.707 for |11⟩, got %f" amp3.Real)
        | _ -> Assert.True(false, "Expected StateVector result")

    [<Fact>]
    let ``histogramToQuantumState converts single-state histogram to basis state`` () =
        // Arrange: All measurements collapse to |01⟩
        let histogram = Map.ofList [ ("01", 1000) ]

        // Act
        let result = CloudBackendHelpers.histogramToQuantumState histogram 2

        // Assert
        match result with
        | QuantumState.StateVector sv ->
            Assert.Equal(4, StateVector.dimension sv)
            Assert.Equal(Complex.Zero, StateVector.getAmplitude 0 sv) // |00⟩ = 0
            Assert.Equal(Complex(1.0, 0.0), StateVector.getAmplitude 1 sv) // |01⟩ = 1.0
            Assert.Equal(Complex.Zero, StateVector.getAmplitude 2 sv) // |10⟩ = 0
            Assert.Equal(Complex.Zero, StateVector.getAmplitude 3 sv) // |11⟩ = 0
        | _ -> Assert.True(false, "Expected StateVector result")

    [<Fact>]
    let ``histogramToQuantumState handles asymmetric histogram`` () =
        // Arrange: 75/25 split → amplitudes sqrt(0.75) and sqrt(0.25)
        let histogram = Map.ofList [ ("0", 750); ("1", 250) ]

        // Act
        let result = CloudBackendHelpers.histogramToQuantumState histogram 1

        // Assert
        match result with
        | QuantumState.StateVector sv ->
            Assert.Equal(2, StateVector.dimension sv) // 1 qubit → 2 amplitudes
            let amp0 = StateVector.getAmplitude 0 sv
            let amp1 = StateVector.getAmplitude 1 sv
            Assert.True(abs (amp0.Real - sqrt 0.75) < 1e-10)
            Assert.True(abs (amp1.Real - sqrt 0.25) < 1e-10)
        | _ -> Assert.True(false, "Expected StateVector result")

    [<Fact>]
    let ``histogramToQuantumState handles empty bins in histogram`` () =
        // Arrange: Only "000" measured, 3-qubit system with 8 basis states
        let histogram = Map.ofList [ ("000", 1000) ]

        // Act
        let result = CloudBackendHelpers.histogramToQuantumState histogram 3

        // Assert
        match result with
        | QuantumState.StateVector sv ->
            Assert.Equal(8, StateVector.dimension sv)
            Assert.Equal(Complex(1.0, 0.0), StateVector.getAmplitude 0 sv) // |000⟩ = 1.0
            for i in 1 .. 7 do
                Assert.Equal(Complex.Zero, StateVector.getAmplitude i sv)
        | _ -> Assert.True(false, "Expected StateVector result")

    // ============================================================================
    // INFER NUM QUBITS TESTS
    // ============================================================================

    [<Fact>]
    let ``inferNumQubits returns correct count from 2-qubit histogram`` () =
        let histogram = Map.ofList [ ("00", 500); ("11", 500) ]
        let result = CloudBackendHelpers.inferNumQubits histogram
        Assert.Equal(Some 2, result)

    [<Fact>]
    let ``inferNumQubits returns correct count from 3-qubit histogram`` () =
        let histogram = Map.ofList [ ("000", 500); ("111", 500) ]
        let result = CloudBackendHelpers.inferNumQubits histogram
        Assert.Equal(Some 3, result)

    [<Fact>]
    let ``inferNumQubits returns None for empty histogram`` () =
        let histogram = Map.empty<string, int>
        let result = CloudBackendHelpers.inferNumQubits histogram
        Assert.Equal(None, result)

    [<Fact>]
    let ``inferNumQubits returns 1 for single-qubit histogram`` () =
        let histogram = Map.ofList [ ("0", 700); ("1", 300) ]
        let result = CloudBackendHelpers.inferNumQubits histogram
        Assert.Equal(Some 1, result)

    // ============================================================================
    // OPERATION SUPPORT TESTS
    // ============================================================================

    [<Fact>]
    let ``isCloudSupportedOperation returns true for Gate`` () =
        let gate = QuantumOperation.Gate (FSharp.Azure.Quantum.CircuitBuilder.Gate.H 0)
        Assert.True(CloudBackendHelpers.isCloudSupportedOperation gate)

    [<Fact>]
    let ``isCloudSupportedOperation returns true for Measure`` () =
        let op = QuantumOperation.Measure 0
        Assert.True(CloudBackendHelpers.isCloudSupportedOperation op)

    [<Fact>]
    let ``isCloudSupportedOperation returns true for Sequence`` () =
        let op = QuantumOperation.Sequence []
        Assert.True(CloudBackendHelpers.isCloudSupportedOperation op)

    [<Fact>]
    let ``isCloudSupportedOperation returns false for Braid`` () =
        let op = QuantumOperation.Braid 0
        Assert.False(CloudBackendHelpers.isCloudSupportedOperation op)

    [<Fact>]
    let ``isCloudSupportedOperation returns false for FMove`` () =
        let op = QuantumOperation.FMove (FMoveDirection.Forward, 1)
        Assert.False(CloudBackendHelpers.isCloudSupportedOperation op)

    // ============================================================================
    // RIGETTI CLOUD BACKEND TESTS
    // ============================================================================

    [<Fact>]
    let ``RigettiCloudBackend Name includes target`` () =
        let httpClient = createDummyHttpClient ()
        let backend = CloudBackends.RigettiCloudBackend(httpClient, "https://test", "rigetti.sim.qvm") :> IQuantumBackend
        Assert.Equal("Rigetti Cloud (rigetti.sim.qvm)", backend.Name)

    [<Fact>]
    let ``RigettiCloudBackend NativeStateType is GateBased`` () =
        let httpClient = createDummyHttpClient ()
        let backend = CloudBackends.RigettiCloudBackend(httpClient, "https://test", "rigetti.sim.qvm") :> IQuantumBackend
        Assert.Equal(QuantumStateType.GateBased, backend.NativeStateType)

    [<Fact>]
    let ``RigettiCloudBackend InitializeState creates valid state`` () =
        let httpClient = createDummyHttpClient ()
        let backend = CloudBackends.RigettiCloudBackend(httpClient, "https://test", "rigetti.sim.qvm") :> IQuantumBackend
        match backend.InitializeState 2 with
        | Ok (QuantumState.StateVector sv) ->
            Assert.Equal(4, StateVector.dimension sv) // 2 qubits → 4 amplitudes
            Assert.Equal(Complex(1.0, 0.0), StateVector.getAmplitude 0 sv) // |00⟩ = 1
        | Ok _ -> Assert.True(false, "Expected StateVector")
        | Error err -> Assert.True(false, sprintf "InitializeState failed: %A" err)

    [<Fact>]
    let ``RigettiCloudBackend SupportsOperation for Gate returns true`` () =
        let httpClient = createDummyHttpClient ()
        let backend = CloudBackends.RigettiCloudBackend(httpClient, "https://test", "rigetti.sim.qvm") :> IQuantumBackend
        let gate = QuantumOperation.Gate (FSharp.Azure.Quantum.CircuitBuilder.Gate.H 0)
        Assert.True(backend.SupportsOperation gate)

    [<Fact>]
    let ``RigettiCloudBackend SupportsOperation for Braid returns false`` () =
        let httpClient = createDummyHttpClient ()
        let backend = CloudBackends.RigettiCloudBackend(httpClient, "https://test", "rigetti.sim.qvm") :> IQuantumBackend
        Assert.False(backend.SupportsOperation (QuantumOperation.Braid 0))

    [<Fact>]
    let ``RigettiCloudBackend ApplyOperationAsync returns Error`` () : Task =
        task {
            let httpClient = createDummyHttpClient ()
            let backend = CloudBackends.RigettiCloudBackend(httpClient, "https://test", "rigetti.sim.qvm") :> IQuantumBackend
            let dummyState = QuantumState.StateVector (FSharp.Azure.Quantum.LocalSimulator.StateVector.init 2)
            let gate = QuantumOperation.Gate (FSharp.Azure.Quantum.CircuitBuilder.Gate.H 0)
            let! result = backend.ApplyOperationAsync gate dummyState CancellationToken.None
            match result with
            | Error (QuantumError.OperationError _) -> () // Expected
            | Error err -> Assert.True(false, sprintf "Expected OperationError, got: %A" err)
            | Ok _ -> Assert.True(false, "Expected Error for cloud ApplyOperation")
        } :> Task

    [<Fact>]
    let ``RigettiCloudBackend QPU MaxQubits is 84`` () =
        let httpClient = createDummyHttpClient ()
        let backend = CloudBackends.RigettiCloudBackend(httpClient, "https://test", "rigetti.qpu.ankaa-3") :> IQubitLimitedBackend
        Assert.Equal(Some 84, backend.MaxQubits)

    [<Fact>]
    let ``RigettiCloudBackend Sim MaxQubits is 20`` () =
        let httpClient = createDummyHttpClient ()
        let backend = CloudBackends.RigettiCloudBackend(httpClient, "https://test", "rigetti.sim.qvm") :> IQubitLimitedBackend
        Assert.Equal(Some 20, backend.MaxQubits)

    // ============================================================================
    // IONQ CLOUD BACKEND TESTS
    // ============================================================================

    [<Fact>]
    let ``IonQCloudBackend Name includes target`` () =
        let httpClient = createDummyHttpClient ()
        let backend = CloudBackends.IonQCloudBackend(httpClient, "https://test", "ionq.simulator") :> IQuantumBackend
        Assert.Equal("IonQ Cloud (ionq.simulator)", backend.Name)

    [<Fact>]
    let ``IonQCloudBackend NativeStateType is GateBased`` () =
        let httpClient = createDummyHttpClient ()
        let backend = CloudBackends.IonQCloudBackend(httpClient, "https://test", "ionq.simulator") :> IQuantumBackend
        Assert.Equal(QuantumStateType.GateBased, backend.NativeStateType)

    [<Fact>]
    let ``IonQCloudBackend InitializeState creates valid state`` () =
        let httpClient = createDummyHttpClient ()
        let backend = CloudBackends.IonQCloudBackend(httpClient, "https://test", "ionq.simulator") :> IQuantumBackend
        match backend.InitializeState 3 with
        | Ok (QuantumState.StateVector sv) ->
            Assert.Equal(8, StateVector.dimension sv) // 3 qubits → 8 amplitudes
            Assert.Equal(Complex(1.0, 0.0), StateVector.getAmplitude 0 sv)
        | Ok _ -> Assert.True(false, "Expected StateVector")
        | Error err -> Assert.True(false, sprintf "InitializeState failed: %A" err)

    [<Fact>]
    let ``IonQCloudBackend ApplyOperationAsync returns Error`` () : Task =
        task {
            let httpClient = createDummyHttpClient ()
            let backend = CloudBackends.IonQCloudBackend(httpClient, "https://test", "ionq.simulator") :> IQuantumBackend
            let dummyState = QuantumState.StateVector (FSharp.Azure.Quantum.LocalSimulator.StateVector.init 2)
            let gate = QuantumOperation.Gate (FSharp.Azure.Quantum.CircuitBuilder.Gate.H 0)
            let! result = backend.ApplyOperationAsync gate dummyState CancellationToken.None
            match result with
            | Error (QuantumError.OperationError _) -> ()
            | Error err -> Assert.True(false, sprintf "Expected OperationError, got: %A" err)
            | Ok _ -> Assert.True(false, "Expected Error for cloud ApplyOperation")
        } :> Task

    [<Fact>]
    let ``IonQCloudBackend Aria MaxQubits is 25`` () =
        let httpClient = createDummyHttpClient ()
        let backend = CloudBackends.IonQCloudBackend(httpClient, "https://test", "ionq.qpu.aria-1") :> IQubitLimitedBackend
        Assert.Equal(Some 25, backend.MaxQubits)

    [<Fact>]
    let ``IonQCloudBackend Forte MaxQubits is 36`` () =
        let httpClient = createDummyHttpClient ()
        let backend = CloudBackends.IonQCloudBackend(httpClient, "https://test", "ionq.qpu.forte-1") :> IQubitLimitedBackend
        Assert.Equal(Some 36, backend.MaxQubits)

    [<Fact>]
    let ``IonQCloudBackend Simulator MaxQubits is 20`` () =
        let httpClient = createDummyHttpClient ()
        let backend = CloudBackends.IonQCloudBackend(httpClient, "https://test", "ionq.simulator") :> IQubitLimitedBackend
        Assert.Equal(Some 20, backend.MaxQubits)

    // ============================================================================
    // QUANTINUUM CLOUD BACKEND TESTS
    // ============================================================================

    [<Fact>]
    let ``QuantinuumCloudBackend Name includes target`` () =
        let httpClient = createDummyHttpClient ()
        let backend = CloudBackends.QuantinuumCloudBackend(httpClient, "https://test", "quantinuum.sim.h1-1sc") :> IQuantumBackend
        Assert.Equal("Quantinuum Cloud (quantinuum.sim.h1-1sc)", backend.Name)

    [<Fact>]
    let ``QuantinuumCloudBackend NativeStateType is GateBased`` () =
        let httpClient = createDummyHttpClient ()
        let backend = CloudBackends.QuantinuumCloudBackend(httpClient, "https://test", "quantinuum.sim.h1-1sc") :> IQuantumBackend
        Assert.Equal(QuantumStateType.GateBased, backend.NativeStateType)

    [<Fact>]
    let ``QuantinuumCloudBackend InitializeState creates valid state`` () =
        let httpClient = createDummyHttpClient ()
        let backend = CloudBackends.QuantinuumCloudBackend(httpClient, "https://test", "quantinuum.sim.h1-1sc") :> IQuantumBackend
        match backend.InitializeState 1 with
        | Ok (QuantumState.StateVector sv) ->
            Assert.Equal(2, StateVector.dimension sv) // 1 qubit → 2 amplitudes
            Assert.Equal(Complex(1.0, 0.0), StateVector.getAmplitude 0 sv)
            Assert.Equal(Complex.Zero, StateVector.getAmplitude 1 sv)
        | Ok _ -> Assert.True(false, "Expected StateVector")
        | Error err -> Assert.True(false, sprintf "InitializeState failed: %A" err)

    [<Fact>]
    let ``QuantinuumCloudBackend ApplyOperationAsync returns Error`` () : Task =
        task {
            let httpClient = createDummyHttpClient ()
            let backend = CloudBackends.QuantinuumCloudBackend(httpClient, "https://test", "quantinuum.sim.h1-1sc") :> IQuantumBackend
            let dummyState = QuantumState.StateVector (FSharp.Azure.Quantum.LocalSimulator.StateVector.init 2)
            let gate = QuantumOperation.Measure 0
            let! result = backend.ApplyOperationAsync gate dummyState CancellationToken.None
            match result with
            | Error (QuantumError.OperationError _) -> ()
            | Error err -> Assert.True(false, sprintf "Expected OperationError, got: %A" err)
            | Ok _ -> Assert.True(false, "Expected Error for cloud ApplyOperation")
        } :> Task

    [<Fact>]
    let ``QuantinuumCloudBackend H1 MaxQubits is 32`` () =
        let httpClient = createDummyHttpClient ()
        let backend = CloudBackends.QuantinuumCloudBackend(httpClient, "https://test", "quantinuum.qpu.h1-1") :> IQubitLimitedBackend
        Assert.Equal(Some 32, backend.MaxQubits)

    [<Fact>]
    let ``QuantinuumCloudBackend H2 MaxQubits is 56`` () =
        let httpClient = createDummyHttpClient ()
        let backend = CloudBackends.QuantinuumCloudBackend(httpClient, "https://test", "quantinuum.qpu.h2-1") :> IQubitLimitedBackend
        Assert.Equal(Some 56, backend.MaxQubits)

    // ============================================================================
    // ATOM COMPUTING CLOUD BACKEND TESTS
    // ============================================================================

    [<Fact>]
    let ``AtomComputingCloudBackend Name includes target`` () =
        let httpClient = createDummyHttpClient ()
        let backend = CloudBackends.AtomComputingCloudBackend(httpClient, "https://test", "atom-computing.sim") :> IQuantumBackend
        Assert.Equal("Atom Computing Cloud (atom-computing.sim)", backend.Name)

    [<Fact>]
    let ``AtomComputingCloudBackend NativeStateType is GateBased`` () =
        let httpClient = createDummyHttpClient ()
        let backend = CloudBackends.AtomComputingCloudBackend(httpClient, "https://test", "atom-computing.sim") :> IQuantumBackend
        Assert.Equal(QuantumStateType.GateBased, backend.NativeStateType)

    [<Fact>]
    let ``AtomComputingCloudBackend InitializeState creates valid state`` () =
        let httpClient = createDummyHttpClient ()
        let backend = CloudBackends.AtomComputingCloudBackend(httpClient, "https://test", "atom-computing.sim") :> IQuantumBackend
        match backend.InitializeState 4 with
        | Ok (QuantumState.StateVector sv) ->
            Assert.Equal(16, StateVector.dimension sv) // 4 qubits → 16 amplitudes
            Assert.Equal(Complex(1.0, 0.0), StateVector.getAmplitude 0 sv)
            for i in 1 .. 15 do
                Assert.Equal(Complex.Zero, StateVector.getAmplitude i sv)
        | Ok _ -> Assert.True(false, "Expected StateVector")
        | Error err -> Assert.True(false, sprintf "InitializeState failed: %A" err)

    [<Fact>]
    let ``AtomComputingCloudBackend ApplyOperationAsync returns Error`` () : Task =
        task {
            let httpClient = createDummyHttpClient ()
            let backend = CloudBackends.AtomComputingCloudBackend(httpClient, "https://test", "atom-computing.sim") :> IQuantumBackend
            let dummyState = QuantumState.StateVector (FSharp.Azure.Quantum.LocalSimulator.StateVector.init 2)
            let gate = QuantumOperation.Gate (FSharp.Azure.Quantum.CircuitBuilder.Gate.H 0)
            let! result = backend.ApplyOperationAsync gate dummyState CancellationToken.None
            match result with
            | Error (QuantumError.OperationError _) -> ()
            | Error err -> Assert.True(false, sprintf "Expected OperationError, got: %A" err)
            | Ok _ -> Assert.True(false, "Expected Error for cloud ApplyOperation")
        } :> Task

    [<Fact>]
    let ``AtomComputingCloudBackend QPU MaxQubits is 100`` () =
        let httpClient = createDummyHttpClient ()
        let backend = CloudBackends.AtomComputingCloudBackend(httpClient, "https://test", "atom-computing.qpu.phoenix") :> IQubitLimitedBackend
        Assert.Equal(Some 100, backend.MaxQubits)

    [<Fact>]
    let ``AtomComputingCloudBackend Sim MaxQubits is 20`` () =
        let httpClient = createDummyHttpClient ()
        let backend = CloudBackends.AtomComputingCloudBackend(httpClient, "https://test", "atom-computing.sim") :> IQubitLimitedBackend
        Assert.Equal(Some 20, backend.MaxQubits)

    // ============================================================================
    // INTERFACE COMPLIANCE TESTS (ALL BACKENDS)
    // ============================================================================

    [<Fact>]
    let ``All cloud backends implement IQuantumBackend`` () =
        let backends = createAllBackends ()
        for backend in backends do
            Assert.IsAssignableFrom<IQuantumBackend>(backend) |> ignore

    [<Fact>]
    let ``All cloud backends implement IQubitLimitedBackend`` () =
        let httpClient = createDummyHttpClient ()
        let workspaceUrl = "https://test"
        let backends: IQubitLimitedBackend[] = [|
            CloudBackends.RigettiCloudBackend(httpClient, workspaceUrl, "rigetti.sim.qvm") :> IQubitLimitedBackend
            CloudBackends.IonQCloudBackend(httpClient, workspaceUrl, "ionq.simulator") :> IQubitLimitedBackend
            CloudBackends.QuantinuumCloudBackend(httpClient, workspaceUrl, "quantinuum.sim.h1-1sc") :> IQubitLimitedBackend
            CloudBackends.AtomComputingCloudBackend(httpClient, workspaceUrl, "atom-computing.sim") :> IQubitLimitedBackend
        |]
        for backend in backends do
            Assert.IsAssignableFrom<IQubitLimitedBackend>(backend) |> ignore

    [<Fact>]
    let ``All cloud backends have GateBased NativeStateType`` () =
        let backends = createAllBackends ()
        for backend in backends do
            Assert.Equal(QuantumStateType.GateBased, backend.NativeStateType)

    [<Fact>]
    let ``All cloud backends support Gate operations`` () =
        let backends = createAllBackends ()
        let gate = QuantumOperation.Gate (FSharp.Azure.Quantum.CircuitBuilder.Gate.H 0)
        for backend in backends do
            Assert.True(backend.SupportsOperation gate, sprintf "%s should support Gate" backend.Name)

    [<Fact>]
    let ``All cloud backends support Measure operations`` () =
        let backends = createAllBackends ()
        let op = QuantumOperation.Measure 0
        for backend in backends do
            Assert.True(backend.SupportsOperation op, sprintf "%s should support Measure" backend.Name)

    [<Fact>]
    let ``All cloud backends reject Braid operations`` () =
        let backends = createAllBackends ()
        let op = QuantumOperation.Braid 0
        for backend in backends do
            Assert.False(backend.SupportsOperation op, sprintf "%s should not support Braid" backend.Name)

    [<Fact>]
    let ``All cloud backends reject FMove operations`` () =
        let backends = createAllBackends ()
        let op = QuantumOperation.FMove (FMoveDirection.Forward, 1)
        for backend in backends do
            Assert.False(backend.SupportsOperation op, sprintf "%s should not support FMove" backend.Name)

    // ============================================================================
    // FACTORY TESTS
    // ============================================================================

    [<Fact>]
    let ``CloudBackendFactory createRigetti returns valid IQuantumBackend`` () =
        let httpClient = createDummyHttpClient ()
        let backend = CloudBackends.CloudBackendFactory.createRigetti httpClient "https://test" "rigetti.sim.qvm" 1000
        Assert.Equal("Rigetti Cloud (rigetti.sim.qvm)", backend.Name)

    [<Fact>]
    let ``CloudBackendFactory createIonQ returns valid IQuantumBackend`` () =
        let httpClient = createDummyHttpClient ()
        let backend = CloudBackends.CloudBackendFactory.createIonQ httpClient "https://test" "ionq.simulator" 1000
        Assert.Equal("IonQ Cloud (ionq.simulator)", backend.Name)

    [<Fact>]
    let ``CloudBackendFactory createQuantinuum returns valid IQuantumBackend`` () =
        let httpClient = createDummyHttpClient ()
        let backend = CloudBackends.CloudBackendFactory.createQuantinuum httpClient "https://test" "quantinuum.sim.h1-1sc" 1000
        Assert.Equal("Quantinuum Cloud (quantinuum.sim.h1-1sc)", backend.Name)

    [<Fact>]
    let ``CloudBackendFactory createAtomComputing returns valid IQuantumBackend`` () =
        let httpClient = createDummyHttpClient ()
        let backend = CloudBackends.CloudBackendFactory.createAtomComputing httpClient "https://test" "atom-computing.sim" 1000
        Assert.Equal("Atom Computing Cloud (atom-computing.sim)", backend.Name)

    // ============================================================================
    // EDGE CASE TESTS
    // ============================================================================

    [<Fact>]
    let ``histogramToQuantumState handles uniform 3-qubit distribution`` () =
        // Arrange: All 8 states measured equally
        let histogram = Map.ofList [
            ("000", 125); ("001", 125); ("010", 125); ("011", 125)
            ("100", 125); ("101", 125); ("110", 125); ("111", 125)
        ]

        // Act
        let result = CloudBackendHelpers.histogramToQuantumState histogram 3

        // Assert
        match result with
        | QuantumState.StateVector sv ->
            Assert.Equal(8, StateVector.dimension sv)
            // Each amplitude should be sqrt(125/1000) = sqrt(0.125) ≈ 0.354
            let expected = sqrt 0.125
            for i in 0 .. 7 do
                let amp = StateVector.getAmplitude i sv
                Assert.True(abs (amp.Real - expected) < 1e-10,
                    sprintf "Amplitude[%d] expected %f, got %f" i expected amp.Real)
        | _ -> Assert.True(false, "Expected StateVector result")

    [<Fact>]
    let ``histogramToQuantumState preserves normalization`` () =
        // Arrange: Arbitrary histogram
        let histogram = Map.ofList [ ("00", 300); ("01", 200); ("10", 100); ("11", 400) ]

        // Act
        let result = CloudBackendHelpers.histogramToQuantumState histogram 2

        // Assert: Sum of |amplitude|^2 should equal 1.0
        match result with
        | QuantumState.StateVector sv ->
            let dim = StateVector.dimension sv
            let normSquared =
                [| 0 .. dim - 1 |]
                |> Array.sumBy (fun i ->
                    let a = StateVector.getAmplitude i sv
                    a.Real * a.Real + a.Imaginary * a.Imaginary)
            Assert.True(abs (normSquared - 1.0) < 1e-10,
                sprintf "State should be normalized, but norm^2 = %f" normSquared)
        | _ -> Assert.True(false, "Expected StateVector result")

    [<Fact>]
    let ``unsupportedOperationError creates OperationError with backend name`` () =
        let error = CloudBackendHelpers.unsupportedOperationError "TestBackend" (QuantumOperation.Braid 0)
        match error with
        | QuantumError.OperationError (context, message) ->
            Assert.Equal("ApplyOperation", context)
            Assert.Contains("TestBackend", message)
            Assert.Contains("Braid", message)
        | _ -> Assert.True(false, sprintf "Expected OperationError, got: %A" error)
