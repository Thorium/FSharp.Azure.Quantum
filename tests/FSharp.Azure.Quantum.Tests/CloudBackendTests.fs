namespace FSharp.Azure.Quantum.Tests

open System
open System.Net.Http
open System.Numerics
open System.Threading
open System.Threading.Tasks
open Xunit
open FSharp.Azure.Quantum.Algorithms
open FSharp.Azure.Quantum.Core
open FSharp.Azure.Quantum.Core.BackendAbstraction
open FSharp.Azure.Quantum.LocalSimulator
open FSharp.Azure.Quantum.Backends

/// Local stand-ins for cloud backends, shared by the tests of algorithms that must run on
/// hardware: they refuse incremental ApplyOperation with the cloud backends' own error, run
/// only whole circuits, and return measured shot frequencies without phases.
module CloudStyleBackends =

    /// The error cloud backends return for ApplyOperation (UnifiedBackend.isIncrementalUnsupported).
    let incrementalUnsupported (name: string) : QuantumError =
        QuantumError.OperationError(
            "ApplyOperation",
            $"%s{name} does not support incremental ApplyOperation. Use ExecuteToState with a complete circuit instead."
        )

    /// Bitstring in the Azure histogram convention (rightmost character = qubit 0).
    let azureKey (numQubits: int) (index: int) : string =
        String(
            Array.init numQubits (fun i ->
                if (index >>> (numQubits - 1 - i)) &&& 1 = 1 then
                    '1'
                else
                    '0')
        )

    /// A shot-sampling cloud backend simulated locally: each ExecuteToState is one "job" that
    /// runs the circuit on LocalBackend, measures `shots` shots (seeded — the device's own
    /// randomness) and returns them exactly as a cloud backend does, through
    /// CloudBackendHelpers.histogramToQuantumState. Records every submitted circuit and histogram.
    type ShotSamplingCloud(shots: int, seed: int) =
        let inner = LocalBackend.LocalBackend() :> IQuantumBackend
        let rng = Random(seed)
        let circuits = ResizeArray<FSharp.Azure.Quantum.CircuitBuilder.Circuit>()
        let histograms = ResizeArray<Map<string, int>>()
        let mutable applyCalls = 0

        /// Jobs submitted (ExecuteToState calls that reached the device)
        member _.Jobs = circuits.Count
        /// Circuits submitted, in order
        member _.Circuits = List.ofSeq circuits
        /// Histograms returned, in order (Azure bitstring convention)
        member _.Histograms = List.ofSeq histograms
        /// ApplyOperation attempts (all refused)
        member _.ApplyOperationCalls = applyCalls

        interface IQuantumBackend with
            member _.Name = "ShotSamplingCloud"
            member _.NativeStateType = QuantumStateType.GateBased

            member _.ExecuteToState circuit =
                match CircuitAbstraction.CircuitAdapter.tryGetCircuit circuit with
                | None -> Error(QuantumError.OperationError("ShotSamplingCloud", "not a gate circuit"))
                | Some gateCircuit ->
                    circuits.Add gateCircuit

                    inner.ExecuteToState circuit
                    |> Result.bind (fun state ->
                        match state with
                        | QuantumState.StateVector sv ->
                            let n = circuit.NumQubits

                            let histogram =
                                Measurement.sampleComputationalBasis rng sv shots
                                |> Array.map (fun bits ->
                                    bits |> Array.mapi (fun q b -> b <<< q) |> Array.sum |> azureKey n)
                                |> Array.countBy id
                                |> Map.ofArray

                            histograms.Add histogram
                            Ok(CloudBackendHelpers.histogramToQuantumState histogram n)
                        | other -> Error(QuantumError.OperationError("ShotSamplingCloud", $"unexpected {other}")))

            member this.ExecuteToStateAsync circuit _ =
                Task.FromResult((this :> IQuantumBackend).ExecuteToState circuit)

            member _.InitializeState numQubits = inner.InitializeState numQubits

            member _.ApplyOperation _ _ =
                applyCalls <- applyCalls + 1
                Error(incrementalUnsupported "ShotSamplingCloud")

            member this.ApplyOperationAsync operation state _ =
                Task.FromResult((this :> IQuantumBackend).ApplyOperation operation state)

            member _.SupportsOperation operation =
                CloudBackendHelpers.isCloudSupportedOperation operation

        interface IShotSamplingBackend with
            member _.Shots = shots

    /// Forwards everything to `inner` (an exact backend) and counts ExecuteToState calls.
    type CountingBackend(inner: IQuantumBackend) =
        let mutable executions = 0

        member _.Executions = executions

        interface IQuantumBackend with
            member _.Name = inner.Name + " (counting)"
            member _.NativeStateType = inner.NativeStateType

            member _.ExecuteToState circuit =
                executions <- executions + 1
                inner.ExecuteToState circuit

            member this.ExecuteToStateAsync circuit _ =
                Task.FromResult((this :> IQuantumBackend).ExecuteToState circuit)

            member _.InitializeState n = inner.InitializeState n
            member _.ApplyOperation operation state = inner.ApplyOperation operation state

            member _.ApplyOperationAsync operation state ct =
                inner.ApplyOperationAsync operation state ct

            member _.SupportsOperation operation = inner.SupportsOperation operation

    /// A shot-sampling cloud backend that returns the same measured histogram (Azure bitstring
    /// convention) for every circuit, and counts its jobs.
    type FixedHistogramCloud(histogram: Map<string, int>, numQubits: int, shots: int) =
        let mutable jobs = 0

        member _.Jobs = jobs

        interface IQuantumBackend with
            member _.Name = "FixedHistogramCloud"
            member _.NativeStateType = QuantumStateType.GateBased

            member _.ExecuteToState _ =
                jobs <- jobs + 1
                Ok(CloudBackendHelpers.histogramToQuantumState histogram numQubits)

            member this.ExecuteToStateAsync circuit _ =
                Task.FromResult((this :> IQuantumBackend).ExecuteToState circuit)

            member _.InitializeState n =
                Ok(QuantumState.StateVector(StateVector.init n))

            member _.ApplyOperation _ _ =
                Error(incrementalUnsupported "FixedHistogramCloud")

            member _.ApplyOperationAsync _ _ _ =
                Task.FromResult(Error(incrementalUnsupported "FixedHistogramCloud"))

            member _.SupportsOperation operation =
                CloudBackendHelpers.isCloudSupportedOperation operation

        interface IShotSamplingBackend with
            member _.Shots = shots

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
    let private createDummyHttpClient () = new HttpClient()

    /// Create all 4 cloud backends for parametric testing.
    let private createAllBackends () =
        let httpClient = createDummyHttpClient ()

        let workspaceUrl =
            "https://test.quantum.azure.com/subscriptions/test/resourceGroups/test/providers/Microsoft.Quantum/Workspaces/test"

        [|
            CloudBackends.RigettiCloudBackend(httpClient, workspaceUrl, "rigetti.sim.qvm", 1000) :> IQuantumBackend
            CloudBackends.IonQCloudBackend(httpClient, workspaceUrl, "ionq.simulator", 1000) :> IQuantumBackend
            CloudBackends.QuantinuumCloudBackend(httpClient, workspaceUrl, "quantinuum.sim.h1-1sc", 1000)
            :> IQuantumBackend
            CloudBackends.AtomComputingCloudBackend(httpClient, workspaceUrl, "atom-computing.sim", 1000)
            :> IQuantumBackend
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
            Assert.True(abs (amp0.Real - sqrt 0.5) < 1e-10, $"Expected ~0.707 for |00⟩, got %f{amp0.Real}")
            Assert.Equal(0.0, amp0.Imaginary)
            // |01⟩ and |10⟩ should be zero
            Assert.Equal(Complex.Zero, StateVector.getAmplitude 1 sv)
            Assert.Equal(Complex.Zero, StateVector.getAmplitude 2 sv)
            // |11⟩ amplitude ≈ sqrt(500/1000) ≈ 0.707
            let amp3 = StateVector.getAmplitude 3 sv
            Assert.True(abs (amp3.Real - sqrt 0.5) < 1e-10, $"Expected ~0.707 for |11⟩, got %f{amp3.Real}")
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

            for i in 1..7 do
                Assert.Equal(Complex.Zero, StateVector.getAmplitude i sv)
        | _ -> Assert.True(false, "Expected StateVector result")

    // ============================================================================
    // UNROUTE HISTOGRAM TESTS
    // ============================================================================

    [<Fact>]
    let ``unrouteHistogram permutes bits back to logical order`` () =
        // Routing left logical 0 on physical 2 and logical 2 on physical 0
        // (mapping.[logical] = physical). Keys: rightmost char = qubit 0.
        // Physical "001" (phys 0 = 1) must become logical "100" (logical 2 = 1).
        let mapping = [| 2; 1; 0 |]
        let histogram = Map.ofList [ ("001", 700); ("100", 300) ]

        let result = CloudBackendHelpers.unrouteHistogram mapping 3 histogram

        Assert.Equal(700, result.["100"])
        Assert.Equal(300, result.["001"])

    [<Fact>]
    let ``unrouteHistogram is identity for identity mapping`` () =
        let mapping = [| 0; 1 |]
        let histogram = Map.ofList [ ("00", 480); ("11", 520) ]

        let result = CloudBackendHelpers.unrouteHistogram mapping 2 histogram

        Assert.Equal<Map<string, int>>(histogram, result)

    [<Fact>]
    let ``unrouteHistogram drops swapped-through device qubits and merges counts`` () =
        // 2 logical qubits routed on a 3-qubit device: logical 1 ended on physical 2.
        // Physical bit 1 is a swap-through qubit — dropping it merges "101" and "111"
        // into the same logical key "11".
        let mapping = [| 0; 2; 1 |]
        let histogram = Map.ofList [ ("101", 400); ("111", 100); ("000", 500) ]

        let result = CloudBackendHelpers.unrouteHistogram mapping 2 histogram

        Assert.Equal(500, result.["11"])
        Assert.Equal(500, result.["00"])
        Assert.Equal(2, result.Count)

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
        let gate = QuantumOperation.Gate(FSharp.Azure.Quantum.CircuitBuilder.Gate.H 0)
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
        let op = QuantumOperation.FMove(FMoveDirection.Forward, 1)
        Assert.False(CloudBackendHelpers.isCloudSupportedOperation op)

    // ============================================================================
    // RIGETTI CLOUD BACKEND TESTS
    // ============================================================================

    [<Fact>]
    let ``RigettiCloudBackend Name includes target`` () =
        let httpClient = createDummyHttpClient ()

        let backend =
            CloudBackends.RigettiCloudBackend(httpClient, "https://test", "rigetti.sim.qvm") :> IQuantumBackend

        Assert.Equal("Rigetti Cloud (rigetti.sim.qvm)", backend.Name)

    [<Fact>]
    let ``RigettiCloudBackend NativeStateType is GateBased`` () =
        let httpClient = createDummyHttpClient ()

        let backend =
            CloudBackends.RigettiCloudBackend(httpClient, "https://test", "rigetti.sim.qvm") :> IQuantumBackend

        Assert.Equal(QuantumStateType.GateBased, backend.NativeStateType)

    [<Fact>]
    let ``RigettiCloudBackend InitializeState creates valid state`` () =
        let httpClient = createDummyHttpClient ()

        let backend =
            CloudBackends.RigettiCloudBackend(httpClient, "https://test", "rigetti.sim.qvm") :> IQuantumBackend

        match backend.InitializeState 2 with
        | Ok(QuantumState.StateVector sv) ->
            Assert.Equal(4, StateVector.dimension sv) // 2 qubits → 4 amplitudes
            Assert.Equal(Complex(1.0, 0.0), StateVector.getAmplitude 0 sv) // |00⟩ = 1
        | Ok _ -> Assert.True(false, "Expected StateVector")
        | Error err -> Assert.True(false, $"InitializeState failed: %A{err}")

    [<Fact>]
    let ``RigettiCloudBackend SupportsOperation for Gate returns true`` () =
        let httpClient = createDummyHttpClient ()

        let backend =
            CloudBackends.RigettiCloudBackend(httpClient, "https://test", "rigetti.sim.qvm") :> IQuantumBackend

        let gate = QuantumOperation.Gate(FSharp.Azure.Quantum.CircuitBuilder.Gate.H 0)
        Assert.True(backend.SupportsOperation gate)

    [<Fact>]
    let ``RigettiCloudBackend SupportsOperation for Braid returns false`` () =
        let httpClient = createDummyHttpClient ()

        let backend =
            CloudBackends.RigettiCloudBackend(httpClient, "https://test", "rigetti.sim.qvm") :> IQuantumBackend

        Assert.False(backend.SupportsOperation(QuantumOperation.Braid 0))

    [<Fact>]
    let ``RigettiCloudBackend ApplyOperationAsync returns Error`` () : Task =
        task {
            let httpClient = createDummyHttpClient ()

            let backend =
                CloudBackends.RigettiCloudBackend(httpClient, "https://test", "rigetti.sim.qvm") :> IQuantumBackend

            let dummyState = QuantumState.StateVector(StateVector.init 2)
            let gate = QuantumOperation.Gate(FSharp.Azure.Quantum.CircuitBuilder.Gate.H 0)

            match! backend.ApplyOperationAsync gate dummyState CancellationToken.None with
            | Error(QuantumError.OperationError _) -> () // Expected
            | Error err -> Assert.True(false, $"Expected OperationError, got: %A{err}")
            | Ok _ -> Assert.True(false, "Expected Error for cloud ApplyOperation")
        }
        :> Task

    [<Fact>]
    let ``RigettiCloudBackend QPU MaxQubits is 84`` () =
        let httpClient = createDummyHttpClient ()

        let backend =
            CloudBackends.RigettiCloudBackend(httpClient, "https://test", "rigetti.qpu.ankaa-3") :> IQubitLimitedBackend

        Assert.Equal(Some 84, backend.MaxQubits)

    [<Fact>]
    let ``RigettiCloudBackend Sim MaxQubits is 20`` () =
        let httpClient = createDummyHttpClient ()

        let backend =
            CloudBackends.RigettiCloudBackend(httpClient, "https://test", "rigetti.sim.qvm") :> IQubitLimitedBackend

        Assert.Equal(Some 20, backend.MaxQubits)

    // ============================================================================
    // IONQ CLOUD BACKEND TESTS
    // ============================================================================

    [<Fact>]
    let ``IonQCloudBackend Name includes target`` () =
        let httpClient = createDummyHttpClient ()

        let backend =
            CloudBackends.IonQCloudBackend(httpClient, "https://test", "ionq.simulator") :> IQuantumBackend

        Assert.Equal("IonQ Cloud (ionq.simulator)", backend.Name)

    [<Fact>]
    let ``IonQCloudBackend NativeStateType is GateBased`` () =
        let httpClient = createDummyHttpClient ()

        let backend =
            CloudBackends.IonQCloudBackend(httpClient, "https://test", "ionq.simulator") :> IQuantumBackend

        Assert.Equal(QuantumStateType.GateBased, backend.NativeStateType)

    [<Fact>]
    let ``IonQCloudBackend InitializeState creates valid state`` () =
        let httpClient = createDummyHttpClient ()

        let backend =
            CloudBackends.IonQCloudBackend(httpClient, "https://test", "ionq.simulator") :> IQuantumBackend

        match backend.InitializeState 3 with
        | Ok(QuantumState.StateVector sv) ->
            Assert.Equal(8, StateVector.dimension sv) // 3 qubits → 8 amplitudes
            Assert.Equal(Complex(1.0, 0.0), StateVector.getAmplitude 0 sv)
        | Ok _ -> Assert.True(false, "Expected StateVector")
        | Error err -> Assert.True(false, $"InitializeState failed: %A{err}")

    [<Fact>]
    let ``IonQCloudBackend ApplyOperationAsync returns Error`` () : Task =
        task {
            let httpClient = createDummyHttpClient ()

            let backend =
                CloudBackends.IonQCloudBackend(httpClient, "https://test", "ionq.simulator") :> IQuantumBackend

            let dummyState = QuantumState.StateVector(StateVector.init 2)
            let gate = QuantumOperation.Gate(FSharp.Azure.Quantum.CircuitBuilder.Gate.H 0)

            match! backend.ApplyOperationAsync gate dummyState CancellationToken.None with
            | Error(QuantumError.OperationError _) -> ()
            | Error err -> Assert.True(false, $"Expected OperationError, got: %A{err}")
            | Ok _ -> Assert.True(false, "Expected Error for cloud ApplyOperation")
        }
        :> Task

    [<Fact>]
    let ``IonQCloudBackend Aria MaxQubits is 25`` () =
        let httpClient = createDummyHttpClient ()

        let backend =
            CloudBackends.IonQCloudBackend(httpClient, "https://test", "ionq.qpu.aria-1") :> IQubitLimitedBackend

        Assert.Equal(Some 25, backend.MaxQubits)

    [<Fact>]
    let ``IonQCloudBackend Forte MaxQubits is 36`` () =
        let httpClient = createDummyHttpClient ()

        let backend =
            CloudBackends.IonQCloudBackend(httpClient, "https://test", "ionq.qpu.forte-1") :> IQubitLimitedBackend

        Assert.Equal(Some 36, backend.MaxQubits)

    [<Fact>]
    let ``IonQCloudBackend Simulator MaxQubits is 20`` () =
        let httpClient = createDummyHttpClient ()

        let backend =
            CloudBackends.IonQCloudBackend(httpClient, "https://test", "ionq.simulator") :> IQubitLimitedBackend

        Assert.Equal(Some 20, backend.MaxQubits)

    // ============================================================================
    // QUANTINUUM CLOUD BACKEND TESTS
    // ============================================================================

    [<Fact>]
    let ``QuantinuumCloudBackend Name includes target`` () =
        let httpClient = createDummyHttpClient ()

        let backend =
            CloudBackends.QuantinuumCloudBackend(httpClient, "https://test", "quantinuum.sim.h1-1sc") :> IQuantumBackend

        Assert.Equal("Quantinuum Cloud (quantinuum.sim.h1-1sc)", backend.Name)

    [<Fact>]
    let ``QuantinuumCloudBackend NativeStateType is GateBased`` () =
        let httpClient = createDummyHttpClient ()

        let backend =
            CloudBackends.QuantinuumCloudBackend(httpClient, "https://test", "quantinuum.sim.h1-1sc") :> IQuantumBackend

        Assert.Equal(QuantumStateType.GateBased, backend.NativeStateType)

    [<Fact>]
    let ``QuantinuumCloudBackend InitializeState creates valid state`` () =
        let httpClient = createDummyHttpClient ()

        let backend =
            CloudBackends.QuantinuumCloudBackend(httpClient, "https://test", "quantinuum.sim.h1-1sc") :> IQuantumBackend

        match backend.InitializeState 1 with
        | Ok(QuantumState.StateVector sv) ->
            Assert.Equal(2, StateVector.dimension sv) // 1 qubit → 2 amplitudes
            Assert.Equal(Complex(1.0, 0.0), StateVector.getAmplitude 0 sv)
            Assert.Equal(Complex.Zero, StateVector.getAmplitude 1 sv)
        | Ok _ -> Assert.True(false, "Expected StateVector")
        | Error err -> Assert.True(false, $"InitializeState failed: %A{err}")

    [<Fact>]
    let ``QuantinuumCloudBackend ApplyOperationAsync returns Error`` () : Task =
        task {
            let httpClient = createDummyHttpClient ()

            let backend =
                CloudBackends.QuantinuumCloudBackend(httpClient, "https://test", "quantinuum.sim.h1-1sc")
                :> IQuantumBackend

            let dummyState = QuantumState.StateVector(StateVector.init 2)
            let gate = QuantumOperation.Measure 0

            match! backend.ApplyOperationAsync gate dummyState CancellationToken.None with
            | Error(QuantumError.OperationError _) -> ()
            | Error err -> Assert.True(false, $"Expected OperationError, got: %A{err}")
            | Ok _ -> Assert.True(false, "Expected Error for cloud ApplyOperation")
        }
        :> Task

    [<Fact>]
    let ``QuantinuumCloudBackend H1 MaxQubits is 32`` () =
        let httpClient = createDummyHttpClient ()

        let backend =
            CloudBackends.QuantinuumCloudBackend(httpClient, "https://test", "quantinuum.qpu.h1-1")
            :> IQubitLimitedBackend

        Assert.Equal(Some 32, backend.MaxQubits)

    [<Fact>]
    let ``QuantinuumCloudBackend H2 MaxQubits is 56`` () =
        let httpClient = createDummyHttpClient ()

        let backend =
            CloudBackends.QuantinuumCloudBackend(httpClient, "https://test", "quantinuum.qpu.h2-1")
            :> IQubitLimitedBackend

        Assert.Equal(Some 56, backend.MaxQubits)

    // ============================================================================
    // ATOM COMPUTING CLOUD BACKEND TESTS
    // ============================================================================

    [<Fact>]
    let ``AtomComputingCloudBackend Name includes target`` () =
        let httpClient = createDummyHttpClient ()

        let backend =
            CloudBackends.AtomComputingCloudBackend(httpClient, "https://test", "atom-computing.sim") :> IQuantumBackend

        Assert.Equal("Atom Computing Cloud (atom-computing.sim)", backend.Name)

    [<Fact>]
    let ``AtomComputingCloudBackend NativeStateType is GateBased`` () =
        let httpClient = createDummyHttpClient ()

        let backend =
            CloudBackends.AtomComputingCloudBackend(httpClient, "https://test", "atom-computing.sim") :> IQuantumBackend

        Assert.Equal(QuantumStateType.GateBased, backend.NativeStateType)

    [<Fact>]
    let ``AtomComputingCloudBackend InitializeState creates valid state`` () =
        let httpClient = createDummyHttpClient ()

        let backend =
            CloudBackends.AtomComputingCloudBackend(httpClient, "https://test", "atom-computing.sim") :> IQuantumBackend

        match backend.InitializeState 4 with
        | Ok(QuantumState.StateVector sv) ->
            Assert.Equal(16, StateVector.dimension sv) // 4 qubits → 16 amplitudes
            Assert.Equal(Complex(1.0, 0.0), StateVector.getAmplitude 0 sv)

            for i in 1..15 do
                Assert.Equal(Complex.Zero, StateVector.getAmplitude i sv)
        | Ok _ -> Assert.True(false, "Expected StateVector")
        | Error err -> Assert.True(false, $"InitializeState failed: %A{err}")

    [<Fact>]
    let ``AtomComputingCloudBackend ApplyOperationAsync returns Error`` () : Task =
        task {
            let httpClient = createDummyHttpClient ()

            let backend =
                CloudBackends.AtomComputingCloudBackend(httpClient, "https://test", "atom-computing.sim")
                :> IQuantumBackend

            let dummyState = QuantumState.StateVector(StateVector.init 2)
            let gate = QuantumOperation.Gate(FSharp.Azure.Quantum.CircuitBuilder.Gate.H 0)

            match! backend.ApplyOperationAsync gate dummyState CancellationToken.None with
            | Error(QuantumError.OperationError _) -> ()
            | Error err -> Assert.True(false, $"Expected OperationError, got: %A{err}")
            | Ok _ -> Assert.True(false, "Expected Error for cloud ApplyOperation")
        }
        :> Task

    // ========================================================================
    // IQM (superconducting, OpenQASM 2.0 via Azure Quantum)
    // ========================================================================

    [<Fact>]
    let ``IqmCloudBackend Name includes target`` () =
        let httpClient = createDummyHttpClient ()

        let backend =
            CloudBackends.IqmCloudBackend(httpClient, "https://test", "iqm.qpu.garnet") :> IQuantumBackend

        Assert.Equal("IQM Cloud (iqm.qpu.garnet)", backend.Name)

    [<Fact>]
    let ``IqmCloudBackend NativeStateType is GateBased`` () =
        let httpClient = createDummyHttpClient ()

        let backend =
            CloudBackends.IqmCloudBackend(httpClient, "https://test", "iqm.sim") :> IQuantumBackend

        Assert.Equal(QuantumStateType.GateBased, backend.NativeStateType)

    [<Fact>]
    let ``CloudBackendFactory createIqm builds an IQM backend`` () =
        let httpClient = createDummyHttpClient ()

        let backend =
            CloudBackends.CloudBackendFactory.createIqm httpClient "https://test" "iqm.sim" 1000

        Assert.Equal("IQM Cloud (iqm.sim)", backend.Name)

    [<Fact>]
    let ``IqmCloudBackend ApplyOperationAsync returns Error`` () : Task =
        task {
            let httpClient = createDummyHttpClient ()

            let backend =
                CloudBackends.IqmCloudBackend(httpClient, "https://test", "iqm.sim") :> IQuantumBackend

            let dummyState = QuantumState.StateVector(StateVector.init 2)
            let gate = QuantumOperation.Gate(FSharp.Azure.Quantum.CircuitBuilder.Gate.H 0)

            match! backend.ApplyOperationAsync gate dummyState CancellationToken.None with
            | Error(QuantumError.OperationError _) -> ()
            | Error err -> Assert.True(false, $"Expected OperationError, got: %A{err}")
            | Ok _ -> Assert.True(false, "Expected Error for cloud ApplyOperation")
        }
        :> Task

    [<Fact>]
    let ``IqmBackend parseIqmResult reads the results histogram`` () =
        let json = """{ "results": { "00": 480, "11": 520 } }"""
        let histogram = IqmBackend.parseIqmResult json
        Assert.Equal(480, histogram.["00"])
        Assert.Equal(520, histogram.["11"])

    [<Fact>]
    let ``IqmBackend createJobSubmission uses OpenQASM 2.0 format`` () =
        let submission = IqmBackend.createJobSubmission "OPENQASM 2.0;" 500 "iqm.sim"
        Assert.Equal("iqm.sim", submission.Target)
        Assert.Equal(Types.CircuitFormat.Custom "qasm.v2", submission.InputDataFormat)

    [<Fact>]
    let ``AtomComputingCloudBackend QPU MaxQubits is 100`` () =
        let httpClient = createDummyHttpClient ()

        let backend =
            CloudBackends.AtomComputingCloudBackend(httpClient, "https://test", "atom-computing.qpu.phoenix")
            :> IQubitLimitedBackend

        Assert.Equal(Some 100, backend.MaxQubits)

    [<Fact>]
    let ``AtomComputingCloudBackend Sim MaxQubits is 20`` () =
        let httpClient = createDummyHttpClient ()

        let backend =
            CloudBackends.AtomComputingCloudBackend(httpClient, "https://test", "atom-computing.sim")
            :> IQubitLimitedBackend

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

        let backends: IQubitLimitedBackend[] =
            [|
                CloudBackends.RigettiCloudBackend(httpClient, workspaceUrl, "rigetti.sim.qvm") :> IQubitLimitedBackend
                CloudBackends.IonQCloudBackend(httpClient, workspaceUrl, "ionq.simulator") :> IQubitLimitedBackend
                CloudBackends.QuantinuumCloudBackend(httpClient, workspaceUrl, "quantinuum.sim.h1-1sc")
                :> IQubitLimitedBackend
                CloudBackends.AtomComputingCloudBackend(httpClient, workspaceUrl, "atom-computing.sim")
                :> IQubitLimitedBackend
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
        let gate = QuantumOperation.Gate(FSharp.Azure.Quantum.CircuitBuilder.Gate.H 0)

        for backend in backends do
            Assert.True(backend.SupportsOperation gate, $"%s{backend.Name} should support Gate")

    [<Fact>]
    let ``All cloud backends support Measure operations`` () =
        let backends = createAllBackends ()
        let op = QuantumOperation.Measure 0

        for backend in backends do
            Assert.True(backend.SupportsOperation op, $"%s{backend.Name} should support Measure")

    [<Fact>]
    let ``All cloud backends reject Braid operations`` () =
        let backends = createAllBackends ()
        let op = QuantumOperation.Braid 0

        for backend in backends do
            Assert.False(backend.SupportsOperation op, $"%s{backend.Name} should not support Braid")

    [<Fact>]
    let ``All cloud backends reject FMove operations`` () =
        let backends = createAllBackends ()
        let op = QuantumOperation.FMove(FMoveDirection.Forward, 1)

        for backend in backends do
            Assert.False(backend.SupportsOperation op, $"%s{backend.Name} should not support FMove")

    // ============================================================================
    // FACTORY TESTS
    // ============================================================================

    [<Fact>]
    let ``CloudBackendFactory createRigetti returns valid IQuantumBackend`` () =
        let httpClient = createDummyHttpClient ()

        let backend =
            CloudBackends.CloudBackendFactory.createRigetti httpClient "https://test" "rigetti.sim.qvm" 1000

        Assert.Equal("Rigetti Cloud (rigetti.sim.qvm)", backend.Name)

    [<Fact>]
    let ``CloudBackendFactory createIonQ returns valid IQuantumBackend`` () =
        let httpClient = createDummyHttpClient ()

        let backend =
            CloudBackends.CloudBackendFactory.createIonQ httpClient "https://test" "ionq.simulator" 1000

        Assert.Equal("IonQ Cloud (ionq.simulator)", backend.Name)

    [<Fact>]
    let ``CloudBackendFactory createQuantinuum returns valid IQuantumBackend`` () =
        let httpClient = createDummyHttpClient ()

        let backend =
            CloudBackends.CloudBackendFactory.createQuantinuum httpClient "https://test" "quantinuum.sim.h1-1sc" 1000

        Assert.Equal("Quantinuum Cloud (quantinuum.sim.h1-1sc)", backend.Name)

    [<Fact>]
    let ``CloudBackendFactory createAtomComputing returns valid IQuantumBackend`` () =
        let httpClient = createDummyHttpClient ()

        let backend =
            CloudBackends.CloudBackendFactory.createAtomComputing httpClient "https://test" "atom-computing.sim" 1000

        Assert.Equal("Atom Computing Cloud (atom-computing.sim)", backend.Name)

    // ============================================================================
    // EDGE CASE TESTS
    // ============================================================================

    [<Fact>]
    let ``histogramToQuantumState handles uniform 3-qubit distribution`` () =
        // Arrange: All 8 states measured equally
        let histogram =
            Map.ofList
                [
                    ("000", 125)
                    ("001", 125)
                    ("010", 125)
                    ("011", 125)
                    ("100", 125)
                    ("101", 125)
                    ("110", 125)
                    ("111", 125)
                ]

        // Act
        let result = CloudBackendHelpers.histogramToQuantumState histogram 3

        // Assert
        match result with
        | QuantumState.StateVector sv ->
            Assert.Equal(8, StateVector.dimension sv)
            // Each amplitude should be sqrt(125/1000) = sqrt(0.125) ≈ 0.354
            let expected = sqrt 0.125

            for i in 0..7 do
                let amp = StateVector.getAmplitude i sv

                Assert.True(
                    abs (amp.Real - expected) < 1e-10,
                    $"Amplitude[%d{i}] expected %f{expected}, got %f{amp.Real}"
                )
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

            Assert.True(abs (normSquared - 1.0) < 1e-10, $"State should be normalized, but norm^2 = %f{normSquared}")
        | _ -> Assert.True(false, "Expected StateVector result")

    [<Fact>]
    let ``unsupportedOperationError creates OperationError with backend name`` () =
        let error =
            CloudBackendHelpers.unsupportedOperationError "TestBackend" (QuantumOperation.Braid 0)

        match error with
        | QuantumError.OperationError(context, message) ->
            Assert.Equal("ApplyOperation", context)
            Assert.Contains("TestBackend", message)
            Assert.Contains("Braid", message)
        | _ -> Assert.True(false, $"Expected OperationError, got: %A{error}")

    // ============================================================================
    // HARDWARE READINESS: transpilation, intent claims, job budget
    // ============================================================================

    module CB = FSharp.Azure.Quantum.CircuitBuilder

    [<Literal>]
    let private dummyWorkspace =
        "https://example.invalid/subscriptions/x/resourceGroups/y/providers/Microsoft.Quantum/Workspaces/z"

    /// The five Azure cloud backend classes, each with a zero-job budget: ExecuteToState then
    /// runs transpilation and provider conversion and stops just before submission.
    let private zeroBudgetBackends () : (string * IQuantumBackend) list =
        let http = createDummyHttpClient ()
        let budget () = CloudBackendHelpers.JobBudget.Limit 0

        [
            "rigetti",
            CloudBackends.RigettiCloudBackend(http, dummyWorkspace, "rigetti.sim.qvm", 100, jobBudget = budget ())
            :> IQuantumBackend
            "ionq",
            CloudBackends.IonQCloudBackend(http, dummyWorkspace, "ionq.simulator", 100, jobBudget = budget ())
            :> IQuantumBackend
            "quantinuum",
            CloudBackends.QuantinuumCloudBackend(
                http,
                dummyWorkspace,
                "quantinuum.sim.h1-1e",
                100,
                jobBudget = budget ()
            )
            :> IQuantumBackend
            "atom",
            CloudBackends.AtomComputingCloudBackend(
                http,
                dummyWorkspace,
                "atom-computing.sim",
                100,
                jobBudget = budget ()
            )
            :> IQuantumBackend
            "iqm",
            CloudBackends.IqmCloudBackend(http, dummyWorkspace, "iqm.sim", 100, jobBudget = budget ())
            :> IQuantumBackend
        ]

    /// Composite gates no provider takes as they are.
    let private compositeCircuits: (string * CB.Gate list) list =
        [
            "T/TDG/S/SDG", [ CB.T 0; CB.TDG 1; CB.S 2; CB.SDG 3 ]
            "CP", [ CB.CP(0, 2, 0.37) ]
            "CRZ", [ CB.CRZ(1, 3, 0.9) ]
            "CRX/CRY", [ CB.CRX(2, 0, 1.3); CB.CRY(3, 1, -0.6) ]
            "CCX", [ CB.CCX(0, 1, 2) ]
            "MCZ3", [ CB.MCZ([ 0; 1 ], 2) ]
            "MCZ4", [ CB.MCZ([ 0; 1; 2 ], 3) ]
            "P/U3", [ CB.P(1, 0.4); CB.U3(2, 0.3, 1.2, -0.7) ]
            "SWAP/RXX/RYY/RZZ", [ CB.SWAP(0, 3); CB.RXX(0, 1, 0.5); CB.RYY(1, 2, 0.8); CB.RZZ(2, 3, 1.1) ]
        ]

    /// `gates` after a preparation that makes every qubit's state non-trivial, so a wrong
    /// decomposition changes the final state.
    let private withPreparation (gates: CB.Gate list) : CB.Circuit =
        let preparation =
            [
                CB.H 0
                CB.RY(1, 0.7)
                CB.H 2
                CB.RX(3, 1.1)
                CB.CNOT(0, 1)
                CB.RZ(0, 0.3)
                CB.H 1
            ]

        CB.empty 4 |> CB.addGates (preparation @ gates @ [ CB.H 3 ])

    let private finalState (circuit: CB.Circuit) : StateVector.StateVector =
        match
            (LocalBackend.LocalBackend() :> IQuantumBackend).ExecuteToState(CircuitAbstraction.wrapCircuit circuit)
        with
        | Ok(QuantumState.StateVector sv) -> sv
        | other -> failwith $"local execution failed: %A{other}"

    /// |⟨a|b⟩|: 1 when the states are equal up to global phase.
    let private overlap (a: StateVector.StateVector) (b: StateVector.StateVector) =
        let inner =
            [ 0 .. StateVector.dimension a - 1 ]
            |> List.fold
                (fun acc i ->
                    acc
                    + Complex.Conjugate(StateVector.getAmplitude i a) * StateVector.getAmplitude i b)
                Complex.Zero

        inner.Magnitude

    [<Fact>]
    let ``transpileForTarget gives every provider convertible gates with the same state`` () =
        for provider in [ "ionq"; "rigetti"; "quantinuum"; "atom"; "iqm" ] do
            for (name, gates) in compositeCircuits do
                let original = withPreparation gates

                let transpiled =
                    CloudBackendHelpers.transpileForTarget provider (CircuitAbstraction.wrapCircuit original)

                let gateCircuit =
                    CircuitAbstraction.CircuitAdapter.tryGetCircuit transpiled |> Option.get

                let converted =
                    match provider with
                    | "ionq" -> CircuitAbstraction.CircuitAdapter.toIonQCircuit transpiled |> Result.map ignore
                    | "rigetti" -> CircuitAbstraction.CircuitAdapter.toQuilProgram transpiled |> Result.map ignore
                    | _ ->
                        try
                            FSharp.Azure.Quantum.OpenQasmExport.export gateCircuit |> ignore
                            Ok()
                        with ex ->
                            Error(QuantumError.OperationError("export", ex.Message))

                match converted with
                | Error err -> Assert.Fail($"%s{provider} %s{name}: conversion failed after transpilation: %A{err}")
                | Ok() -> ()

                let fidelity = overlap (finalState original) (finalState gateCircuit)
                Assert.True(abs (fidelity - 1.0) < 1e-9, $"{provider} {name}: |<orig|transpiled>| = {fidelity}")

    [<Fact>]
    let ``cloud backend classes transpile inside ExecuteToState and reach submission`` () =
        // A zero-job budget refuses at the moment of submission, after conversion: QuotaExceeded
        // proves the circuit converted; a conversion error would surface instead.
        for (provider, backend) in zeroBudgetBackends () do
            for (name, gates) in compositeCircuits do
                match backend.ExecuteToState(CircuitAbstraction.wrapCircuit (withPreparation gates)) with
                | Error(QuantumError.AzureError(AzureQuantumError.QuotaExceeded _)) -> ()
                | other -> Assert.Fail($"%s{provider} %s{name}: expected the job-budget refusal, got %A{other}")

            let budget = (backend :?> CloudBackendHelpers.IJobCountingBackend).JobBudget
            Assert.Equal(0, budget.Submitted)

    [<Fact>]
    let ``transpileForBackendFully gives the same state for every target`` () =
        let original =
            CB.empty 3
            |> CB.addGates
                [
                    CB.RY(0, 0.9)
                    CB.CRY(0, 1, 0.8)
                    CB.RX(2, 0.4)
                    CB.CCX(0, 1, 2)
                    CB.RYY(1, 2, 0.6)
                    CB.CP(2, 0, 1.4)
                ]

        for target in [ "ionq"; "rigetti"; "quantinuum"; "unknown-target" ] do
            let transpiled =
                FSharp.Azure.Quantum.GateTranspiler.transpileForBackendFully target original

            Assert.True(abs (overlap (finalState original) (finalState transpiled) - 1.0) < 1e-9, target)

    let private qftIntentOp =
        QuantumOperation.Algorithm(
            AlgorithmOperation.QFT
                {
                    NumQubits = 3
                    Inverse = false
                    ApplySwaps = true
                }
        )

    [<Fact>]
    let ``cloud backends claim no algorithm intent while local and topological keep theirs`` () =
        Assert.False(CloudBackendHelpers.isCloudSupportedOperation qftIntentOp)

        for (provider, backend) in zeroBudgetBackends () do
            Assert.False(backend.SupportsOperation qftIntentOp, $"{provider} must not claim a native QFT")
            Assert.True(backend.SupportsOperation(QuantumOperation.Gate(CB.H 0)))

        // Cloud-only: the backends that run intents natively still claim them.
        Assert.True((LocalBackend.LocalBackend() :> IQuantumBackend).SupportsOperation qftIntentOp)

        let topological =
            FSharp.Azure.Quantum.Topological.TopologicalUnifiedBackendFactory.createIsing 10

        Assert.True(topological.SupportsOperation qftIntentOp)

    [<Fact>]
    let ``QFT plans the gate route on cloud classes and the native route on local and topological`` () =
        let intent: QFT.QftExecutionIntent =
            {
                NumQubits = 3
                Config = QFT.defaultConfig
                Exactness = FSharp.Azure.Quantum.Algorithms.QFT.Exact
            }

        for (provider, backend) in zeroBudgetBackends () do
            match QFT.plan backend intent with
            | Ok(QFT.QftPlan.ExecuteViaOps _) -> ()
            | other -> Assert.Fail($"%s{provider}: expected the gate route, got %A{other}")

        let nativeBackends: IQuantumBackend list =
            [
                LocalBackend.LocalBackend()
                FSharp.Azure.Quantum.Topological.TopologicalUnifiedBackendFactory.createIsing 10
            ]

        for backend in nativeBackends do
            match QFT.plan backend intent with
            | Ok(QFT.QftPlan.ExecuteNatively _) -> ()
            | other -> Assert.Fail($"%s{backend.Name}: expected the native route, got %A{other}")

    [<Fact>]
    let ``QFT on a real cloud class takes the whole-circuit route to submission`` () =
        let backend = zeroBudgetBackends () |> List.find (fst >> (=) "ionq") |> snd

        match
            QFT.execute 3 backend QFT.defaultConfig
        with
        | Error(QuantumError.AzureError(AzureQuantumError.QuotaExceeded _)) -> ()
        | other -> Assert.Fail($"expected the whole circuit to reach submission, got %A{other}")

    [<Fact>]
    let ``JobBudget counts reservations and refuses past its limit`` () =
        let budget = CloudBackendHelpers.JobBudget.Limit 2
        Assert.Equal(Ok(), budget.TryReserve "t")
        Assert.Equal(Ok(), budget.TryReserve "t")

        match budget.TryReserve "t" with
        | Error(QuantumError.AzureError(AzureQuantumError.QuotaExceeded message)) -> Assert.Contains("2 jobs", message)
        | other -> Assert.Fail($"expected QuotaExceeded, got %A{other}")

        Assert.Equal(2, budget.Submitted)
        Assert.Equal(Some 0, budget.Remaining)

        let unlimited = CloudBackendHelpers.JobBudget()

        for _ in 1..5 do
            Assert.Equal(Ok(), unlimited.TryReserve "t")

        Assert.Equal(5, unlimited.Submitted)
        Assert.Equal(None, unlimited.MaxJobs)

    [<Fact>]
    let ``cloud backends default to an unlimited budget and share one when given`` () =
        let http = createDummyHttpClient ()

        let plain =
            CloudBackends.IonQCloudBackend(http, dummyWorkspace, "ionq.simulator", 100)

        Assert.Equal(None, (plain :> CloudBackendHelpers.IJobCountingBackend).JobBudget.MaxJobs)

        let shared = CloudBackendHelpers.JobBudget.Limit 1

        let ionq =
            CloudBackends.IonQCloudBackend(http, dummyWorkspace, "ionq.simulator", 100, jobBudget = shared)
            :> IQuantumBackend

        let quantinuum =
            CloudBackends.QuantinuumCloudBackend(http, dummyWorkspace, "quantinuum.sim.h1-1e", 100, jobBudget = shared)
            :> IQuantumBackend

        let bell =
            CB.empty 2
            |> CB.addGates [ CB.H 0; CB.CNOT(0, 1) ]
            |> CircuitAbstraction.wrapCircuit

        // The first job is reserved and goes to the (unreachable) workspace.
        match ionq.ExecuteToState bell with
        | Error(QuantumError.AzureError(AzureQuantumError.QuotaExceeded _)) ->
            Assert.Fail("the first job is within budget")
        | _ -> ()

        Assert.Equal(1, shared.Submitted)

        match quantinuum.ExecuteToState bell with
        | Error(QuantumError.AzureError(AzureQuantumError.QuotaExceeded _)) -> ()
        | other -> Assert.Fail($"the shared budget is used up; expected QuotaExceeded, got %A{other}")

        Assert.Equal(1, shared.Submitted)
