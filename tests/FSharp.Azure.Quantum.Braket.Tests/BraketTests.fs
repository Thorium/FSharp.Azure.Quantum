namespace FSharp.Azure.Quantum.Braket.Tests

open System
open System.Text.Json
open System.Threading
open System.Threading.Tasks
open Amazon
open Amazon.Braket
open Amazon.Braket.Model
open Amazon.Runtime
open Amazon.S3
open Amazon.S3.Model
open Xunit
open FSharp.Azure.Quantum
open FSharp.Azure.Quantum.Braket
open FSharp.Azure.Quantum.Core

/// Tests for the pure AWS Braket helpers (action wrapping, result parsing, device ARNs) and
/// for the submission flow (BraketExecution) against stub clients: no test calls AWS.
module BraketTests =

    [<Fact>]
    let ``openQasmAction wraps OpenQASM 3.0 source in a valid Braket action`` () =
        let bell =
            CircuitBuilder.empty 2
            |> CircuitBuilder.addGate (CircuitBuilder.H 0)
            |> CircuitBuilder.addGate (CircuitBuilder.CNOT(0, 1))

        let action = Braket.openQasmAction (OpenQasm.exportV3 bell)
        use doc = JsonDocument.Parse(action) // must be valid JSON
        let root = doc.RootElement

        Assert.Equal(
            "braket.ir.openqasm.program",
            root.GetProperty("braketSchemaHeader").GetProperty("name").GetString()
        )
        // The source round-trips (newlines/quotes escaped correctly) and contains the circuit.
        let source = root.GetProperty("source").GetString()
        Assert.Contains("OPENQASM 3.0;", source)
        Assert.Contains("cx q[0],q[1];", source)

    [<Fact>]
    let ``parseGateResult reads a per-shot measurements array`` () =
        let json = """{ "measurements": [ [0,0], [1,1], [0,0], [1,1], [1,1] ] }"""
        let counts = Braket.parseGateResult json
        Assert.Equal(2, counts.["00"])
        Assert.Equal(3, counts.["11"])

    [<Fact>]
    let ``parseGateResult falls back to measurementProbabilities`` () =
        let json = """{ "measurementProbabilities": { "00": 0.5, "11": 0.5 } }"""
        let counts = Braket.parseGateResult json
        Assert.True(counts.ContainsKey "00" && counts.["00"] > 0)
        Assert.True(counts.ContainsKey "11" && counts.["11"] > 0)

    [<Fact>]
    let ``device ARNs are the expected Braket resources`` () =
        Assert.Equal("arn:aws:braket:eu-west-2::device/qpu/oqc/Lucy", Braket.Devices.oqcLucy)
        Assert.Equal("arn:aws:braket:us-east-1::device/qpu/infleqtion/Sqale", Braket.Devices.infleqtionSqale)
        Assert.Equal("arn:aws:braket:us-east-1::device/qpu/quera/Aquila", Braket.Devices.queraAquila)
        Assert.Contains("quantum-simulator/amazon/sv1", Braket.Devices.sv1)

    [<Fact>]
    let ``BraketBackend transpiles composite gates for each device before submission`` () =
        // A zero-job budget refuses at submission, after transpilation and OpenQASM 3.0 export,
        // so no AWS client is touched: QuotaExceeded proves the circuit exported.
        let circuit =
            CircuitBuilder.empty 4
            |> CircuitBuilder.addGates
                [
                    CircuitBuilder.H 0
                    CircuitBuilder.H 1
                    CircuitBuilder.H 2
                    CircuitBuilder.MCZ([ 0; 1; 2 ], 3)
                    CircuitBuilder.CCX(0, 1, 2)
                    CircuitBuilder.CP(0, 3, 0.4)
                    CircuitBuilder.CRZ(1, 2, 0.7)
                    CircuitBuilder.T 3
                    CircuitBuilder.TDG 2
                ]

        for device in [ Braket.Devices.sv1; Braket.Devices.ionqAria1; Braket.Devices.oqcLucy ] do
            let budget = FSharp.Azure.Quantum.Backends.CloudBackendHelpers.JobBudget.Limit 0

            let backend =
                BraketExecution.BraketBackend(
                    null,
                    null,
                    { Bucket = "b"; KeyPrefix = "k" },
                    device,
                    100,
                    jobBudget = budget
                )
                :> BackendAbstraction.IQuantumBackend

            match backend.ExecuteToState(CircuitAbstraction.wrapCircuit circuit) with
            | Error(QuantumError.AzureError(AzureQuantumError.QuotaExceeded _)) -> ()
            | other -> Assert.Fail($"%s{device}: expected the job-budget refusal, got %A{other}")

            Assert.Equal(0, budget.Submitted)

    [<Fact>]
    let ``Devices.maxQubits knows the gate-model devices and nothing else`` () =
        Assert.Equal(ValueSome 25, Braket.Devices.maxQubits Braket.Devices.ionqAria1)
        Assert.Equal(ValueSome 36, Braket.Devices.maxQubits Braket.Devices.ionqForte1)
        Assert.Equal(ValueSome 84, Braket.Devices.maxQubits Braket.Devices.rigettiAnkaa3)
        Assert.Equal(ValueSome 20, Braket.Devices.maxQubits Braket.Devices.iqmGarnet)
        Assert.Equal(ValueSome 8, Braket.Devices.maxQubits Braket.Devices.oqcLucy)
        Assert.Equal(ValueSome 34, Braket.Devices.maxQubits Braket.Devices.sv1)
        Assert.Equal(ValueSome 17, Braket.Devices.maxQubits Braket.Devices.dm1)
        Assert.Equal(ValueSome 50, Braket.Devices.maxQubits Braket.Devices.tn1)
        // analog device, and a device this module does not name
        Assert.Equal(ValueNone, Braket.Devices.maxQubits Braket.Devices.queraAquila)
        Assert.Equal(ValueNone, Braket.Devices.maxQubits Braket.Devices.infleqtionSqale)
        Assert.Equal(ValueNone, Braket.Devices.maxQubits "arn:aws:braket:::device/qpu/unknown/Device")

    [<Fact>]
    let ``the Braket backend reports its device's qubits and bills every circuit`` () =
        let backendFor (device: string) =
            BraketExecution.BraketBackend(null, null, { Bucket = "b"; KeyPrefix = "k" }, device, 100)
            :> BackendAbstraction.IQuantumBackend

        Assert.Equal(Some 8, BackendAbstraction.UnifiedBackend.getRunnableQubits (backendFor Braket.Devices.oqcLucy))
        Assert.Equal(Some 34, BackendAbstraction.UnifiedBackend.getMaxQubits (backendFor Braket.Devices.sv1))
        Assert.Equal(None, BackendAbstraction.UnifiedBackend.getMaxQubits (backendFor Braket.Devices.infleqtionSqale))

        // A billed backend is split only on request (SplitPolicy.Always).
        let lucy = backendFor Braket.Devices.oqcLucy
        let settings = QaoaExecutionHelpers.defaultSplitSettings
        Assert.Equal(ValueNone, QaoaExecutionHelpers.splitPieceQubits settings lucy 20)

        Assert.Equal(
            ValueSome 8,
            QaoaExecutionHelpers.splitPieceQubits
                { settings with
                    Policy = QaoaExecutionHelpers.SplitPolicy.Always
                }
                lucy
                20
        )

    [<Fact>]
    let ``maxQubits replaces the built-in figure of a Braket device`` () =
        let backendWith (device: string) (qubits: int) =
            BraketExecution.BraketBackend(
                null,
                null,
                { Bucket = "b"; KeyPrefix = "k" },
                device,
                100,
                maxQubits = qubits
            )
            :> BackendAbstraction.IQuantumBackend

        // a device the table knows, grown since
        Assert.Equal(Some 64, BackendAbstraction.UnifiedBackend.getMaxQubits (backendWith Braket.Devices.ionqAria1 64))

        Assert.Equal(
            Some 64,
            BackendAbstraction.UnifiedBackend.getRunnableQubits (backendWith Braket.Devices.ionqAria1 64)
        )
        // a device the table does not know
        Assert.Equal(
            Some 24,
            BackendAbstraction.UnifiedBackend.getMaxQubits (backendWith Braket.Devices.infleqtionSqale 24)
        )

        Assert.Throws<ArgumentException>(fun () -> backendWith Braket.Devices.sv1 0 |> ignore)
        |> ignore

    [<Fact>]
    let ``qubitCountOfCapabilities reads paradigm.qubitCount of a device capabilities document`` () =
        let hardware =
            """{"braketSchemaHeader":{"name":"braket.device_schema.ionq.ionq_device_capabilities","version":"1"},
                "service":{"shotsRange":[1,5000]},
                "action":{"braket.ir.openqasm.program":{"actionType":"braket.ir.openqasm.program","version":["1"]}},
                "paradigm":{"qubitCount":36,"nativeGateSet":["GPI","GPI2","ZZ"],
                            "connectivity":{"fullyConnected":true,"connectivityGraph":{}}}}"""

        let simulator =
            """{"service":{"shotsRange":[0,100000]},"paradigm":{"qubitCount":34}}"""

        Assert.Equal(ValueSome 36, Braket.Devices.qubitCountOfCapabilities hardware)
        Assert.Equal(ValueSome 34, Braket.Devices.qubitCountOfCapabilities simulator)

        // documents that name no usable count
        for document in
            [
                """{"service":{}}"""
                """{"paradigm":{"nativeGateSet":["cz"]}}"""
                """{"paradigm":{"qubitCount":"many"}}"""
                """{"paradigm":{"qubitCount":0}}"""
                """{"paradigm":36}"""
                "[1, 2]"
                "not json"
                ""
                null
            ] do
            Assert.Equal(ValueNone, Braket.Devices.qubitCountOfCapabilities document)

    // ------------------------------------------------------------------------
    // BraketExecution.submitActionAsync against stub clients (no AWS call is made)
    // ------------------------------------------------------------------------

    [<Literal>]
    let private taskArn = "arn:aws:braket:us-east-1:000000000000:quantum-task/stub"

    let private s3Config: BraketExecution.S3Config = { Bucket = "b"; KeyPrefix = "k" }

    /// Braket client stub: creating a task returns taskArn, status requests go to `status`
    /// and cancellation requests to `cancel`.
    let private stubBraket
        (status: unit -> GetQuantumTaskResponse)
        (cancel: CancelQuantumTaskRequest -> CancelQuantumTaskResponse)
        : AmazonBraketClient =
        { new AmazonBraketClient(AnonymousAWSCredentials(), RegionEndpoint.USEast1) with
            override _.CreateQuantumTaskAsync(_: CreateQuantumTaskRequest, _: CancellationToken) =
                Task.FromResult(CreateQuantumTaskResponse(QuantumTaskArn = taskArn))

            override _.GetQuantumTaskAsync(_: GetQuantumTaskRequest, _: CancellationToken) =
                try
                    Task.FromResult(status ())
                with ex ->
                    Task.FromException<GetQuantumTaskResponse> ex

            override _.CancelQuantumTaskAsync(request: CancelQuantumTaskRequest, _: CancellationToken) =
                try
                    Task.FromResult(cancel request)
                with ex ->
                    Task.FromException<CancelQuantumTaskResponse> ex
        }

    /// S3 client stub that serves `json` for every object.
    let private stubS3 (json: string) : AmazonS3Client =
        { new AmazonS3Client(AnonymousAWSCredentials(), RegionEndpoint.USEast1) with
            override _.GetObjectAsync(_: string, _: string, _: CancellationToken) =
                Task.FromResult(
                    new GetObjectResponse(
                        ResponseStream = new System.IO.MemoryStream(System.Text.Encoding.UTF8.GetBytes json)
                    )
                )
        }

    let private taskIn (status: QuantumTaskStatus) =
        GetQuantumTaskResponse(Status = status, OutputS3Bucket = "b", OutputS3Directory = "k/stub")

    /// Counts the cancellation requests and accepts them.
    let private acceptCancel (requests: int ref) (request: CancelQuantumTaskRequest) =
        Assert.Equal(taskArn, request.QuantumTaskArn)
        requests.Value <- requests.Value + 1
        CancelQuantumTaskResponse(QuantumTaskArn = taskArn, CancellationStatus = CancellationStatus.CANCELLING)

    let private submit
        (braket: AmazonBraketClient)
        (s3: AmazonS3Client)
        (timeout: TimeSpan)
        (ct: CancellationToken)
        =
        BraketExecution.submitActionAsync
            braket
            s3
            s3Config
            Braket.Devices.sv1
            (Braket.openQasmAction "OPENQASM 3.0;")
            10
            Braket.parseGateResult
            (TimeSpan.FromMilliseconds 1.0)
            timeout
            ct

    let private expectTaskError (result: Result<Map<string, int>, QuantumError>) : string =
        match result with
        | Error(QuantumError.OperationError("Braket", message)) ->
            Assert.Contains(taskArn, message)
            message
        | other -> failwith $"expected a Braket error naming the task, got %A{other}"

    [<Fact>]
    let ``A completed task returns its histogram and is not cancelled`` () : Task =
        task {
            let cancels = ref 0
            use braket = stubBraket (fun () -> taskIn QuantumTaskStatus.COMPLETED) (acceptCancel cancels)
            use s3 = stubS3 """{ "measurements": [ [0,0], [1,1], [1,1] ] }"""

            let! result = submit braket s3 (TimeSpan.FromMinutes 1.0) CancellationToken.None

            Assert.Equal(Ok(Map [ "00", 1; "11", 2 ]), result)
            Assert.Equal(0, cancels.Value)
        }

    [<Fact>]
    let ``A task that times out is cancelled and named in the Error`` () : Task =
        task {
            let cancels = ref 0
            use braket = stubBraket (fun () -> taskIn QuantumTaskStatus.RUNNING) (acceptCancel cancels)
            use s3 = stubS3 "{}"

            let! result = submit braket s3 (TimeSpan.FromMilliseconds 20.0) CancellationToken.None

            let message = expectTaskError result
            Assert.Contains("did not complete", message)
            Assert.Contains("cancellation request was accepted", message)
            Assert.Equal(1, cancels.Value)
        }

    [<Fact>]
    let ``A refused cancellation says the task may still run`` () : Task =
        task {
            use braket =
                stubBraket (fun () -> taskIn QuantumTaskStatus.QUEUED) (fun _ -> failwith "cancel is not allowed")

            use s3 = stubS3 "{}"

            let! result = submit braket s3 (TimeSpan.FromMilliseconds 20.0) CancellationToken.None

            let message = expectTaskError result
            Assert.Contains("not accepted", message)
            Assert.Contains("cancel is not allowed", message)
            Assert.Contains("may still run and bill", message)
        }

    [<Fact>]
    let ``A failed status request keeps the task ARN and cancels the task`` () : Task =
        task {
            let cancels = ref 0
            use braket = stubBraket (fun () -> failwith "throttled") (acceptCancel cancels)
            use s3 = stubS3 "{}"

            let! result = submit braket s3 (TimeSpan.FromMinutes 1.0) CancellationToken.None

            let message = expectTaskError result
            Assert.Contains("throttled", message)
            Assert.DoesNotContain("submission failed", message)
            Assert.Equal(1, cancels.Value)
        }

    [<Fact>]
    let ``A cancellation by the caller cancels the task and names it`` () : Task =
        task {
            let cancels = ref 0
            use braket = stubBraket (fun () -> taskIn QuantumTaskStatus.RUNNING) (acceptCancel cancels)
            use s3 = stubS3 "{}"
            use cts = new CancellationTokenSource()
            do! cts.CancelAsync()

            let! result = submit braket s3 (TimeSpan.FromMinutes 1.0) cts.Token

            let message = expectTaskError result
            Assert.Contains("cancelled by the caller", message)
            Assert.Contains("cancellation request was accepted", message)
            Assert.Equal(1, cancels.Value)
        }

    [<Fact>]
    let ``A result of an unrecognised shape is an Error, not an empty histogram`` () : Task =
        task {
            Assert.Throws<FormatException>(fun () ->
                Braket.parseGateResult """{ "taskMetadata": {} }""" |> ignore)
            |> ignore

            Assert.Throws<FormatException>(fun () ->
                Braket.parseGateResult """{ "measurementProbabilities": 3 }""" |> ignore)
            |> ignore

            let cancels = ref 0
            use braket = stubBraket (fun () -> taskIn QuantumTaskStatus.COMPLETED) (acceptCancel cancels)
            use s3 = stubS3 """{ "taskMetadata": {} }"""

            let! result = submit braket s3 (TimeSpan.FromMinutes 1.0) CancellationToken.None

            let message = expectTaskError result
            Assert.Contains("could not be parsed", message)
            // The task completed: there is nothing left to cancel.
            Assert.Equal(0, cancels.Value)
        }
