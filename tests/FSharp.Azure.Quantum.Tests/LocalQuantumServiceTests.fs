namespace FSharp.Azure.Quantum.Tests

open System
open System.Net
open System.Net.Http
open System.Text.Json
open System.Threading
open System.Threading.Tasks
open Xunit
open FSharp.Azure.Quantum
open FSharp.Azure.Quantum.Core
open FSharp.Azure.Quantum.Core.Types
open FSharp.Azure.Quantum.Core.BackendAbstraction
open FSharp.Azure.Quantum.Core.CircuitAbstraction
open FSharp.Azure.Quantum.LocalSimulator
open FSharp.Azure.Quantum.Backends
open FSharp.Azure.Quantum.Backends.CloudBackends

/// End-to-end tests of the cloud backends against LocalQuantumService: the real HTTP job
/// lifecycle (submit → poll → download result blob → provider parser) with the jobs run on
/// the local simulator. The service binds 127.0.0.1 only and never contacts the network.
[<Collection("NonParallel")>]
module LocalQuantumServiceTests =

    [<Literal>]
    let private shots = 1000

    let private seeded =
        { LocalQuantumService.defaultOptions with
            Seed = Some 42
        }

    let private circuitOf (numQubits: int) (gates: CircuitBuilder.Gate list) : ICircuit =
        gates
        |> List.fold (fun c g -> CircuitBuilder.addGate g c) (CircuitBuilder.empty numQubits)
        |> wrapCircuit

    let private bell = circuitOf 2 [ CircuitBuilder.H 0; CircuitBuilder.CNOT(0, 1) ]

    /// Cloud backend for `provider` pointed at the service.
    let private backendFor
        (provider: string)
        (service: LocalQuantumService)
        (http: HttpClient)
        (budget: CloudBackendHelpers.JobBudget)
        : IQuantumBackend =
        let url = service.WorkspaceUrl

        match provider with
        | "ionq" -> IonQCloudBackend(http, url, "ionq.simulator", shots, jobBudget = budget) :> IQuantumBackend
        | "rigetti" -> RigettiCloudBackend(http, url, "rigetti.sim.qvm", shots, jobBudget = budget) :> IQuantumBackend
        | "quantinuum" ->
            QuantinuumCloudBackend(http, url, "quantinuum.sim.h1-1e", shots, jobBudget = budget) :> IQuantumBackend
        | "iqm" -> IqmCloudBackend(http, url, "iqm.sim", shots, jobBudget = budget) :> IQuantumBackend
        | "atom" ->
            AtomComputingCloudBackend(http, url, "atom-computing.sim", shots, jobBudget = budget) :> IQuantumBackend
        | other -> failwith $"unknown provider {other}"

    let private probabilities (state: QuantumState) : float[] =
        match state with
        | QuantumState.StateVector sv -> Measurement.getProbabilityDistribution sv
        | other -> failwith $"expected a state vector, got {other}"

    let private expectOk (result: Result<'T, QuantumError>) : 'T =
        result
        |> Result.defaultWith (fun err -> failwith $"expected Ok, got {err.Message}")

    // ------------------------------------------------------------------------
    // Bell circuit through every provider
    // ------------------------------------------------------------------------

    [<Theory>]
    [<InlineData("ionq")>]
    [<InlineData("rigetti")>]
    [<InlineData("quantinuum")>]
    [<InlineData("iqm")>]
    [<InlineData("atom")>]
    let ``Bell circuit through the cloud backend returns only 00 and 11`` (provider: string) =
        use service = LocalQuantumService.start seeded
        use http = service.CreateHttpClient()
        let budget = CloudBackendHelpers.JobBudget()
        let backend = backendFor provider service http budget

        let state = backend.ExecuteToState bell |> expectOk
        let p = probabilities state

        Assert.Equal(4, p.Length)
        Assert.Equal(0.0, p.[1], 12)
        Assert.Equal(0.0, p.[2], 12)
        Assert.InRange(p.[0], 0.4, 0.6)
        Assert.Equal(1.0, p.[0] + p.[3], 9)

        Assert.Equal(1, service.SubmittedJobCount)
        Assert.Equal(1, budget.Submitted)

        let job = service.Jobs |> List.exactlyOne
        Assert.Equal(JobStatus.Succeeded, job.Status)
        Assert.Equal(shots, job.Shots)
        Assert.Equal(shots, job.Histogram |> Map.toSeq |> Seq.sumBy snd)
        Assert.True(job.Histogram |> Map.forall (fun key _ -> key = "00" || key = "11"), $"%A{job.Histogram}")

    [<Theory>]
    [<InlineData("ionq")>]
    [<InlineData("rigetti")>]
    [<InlineData("quantinuum")>]
    [<InlineData("iqm")>]
    [<InlineData("atom")>]
    let ``Bit order matches the library convention: X on qubit 0 of 3 reads basis state 1`` (provider: string) =
        use service = LocalQuantumService.start seeded
        use http = service.CreateHttpClient()
        let backend = backendFor provider service http (CloudBackendHelpers.JobBudget())

        let state = backend.ExecuteToState(circuitOf 3 [ CircuitBuilder.X 0 ]) |> expectOk

        Assert.Equal(1.0, (probabilities state).[1], 12)
        let job = service.Jobs |> List.exactlyOne
        Assert.Equal<Map<string, int>>(Map [ "001", shots ], job.Histogram)

    [<Fact>]
    let ``Result blobs use each provider's result shape`` () =
        use service = LocalQuantumService.start seeded
        use http = service.CreateHttpClient()

        for provider in [ "ionq"; "rigetti"; "quantinuum" ] do
            let backend = backendFor provider service http (CloudBackendHelpers.JobBudget())
            backend.ExecuteToState bell |> expectOk |> ignore

        let output (providerId: string) =
            let job = service.Jobs |> List.find (fun j -> j.ProviderId = providerId)
            JsonDocument.Parse(job.OutputData.Value).RootElement

        // IonQ: probabilities keyed by decimal basis index
        let ionq = output "ionq"
        let histogram = ionq.GetProperty "histogram"

        Assert.True(
            histogram.EnumerateObject()
            |> Seq.forall (fun p -> p.Name = "0" || p.Name = "3")
        )

        Assert.Equal(1.0, histogram.EnumerateObject() |> Seq.sumBy (fun p -> p.Value.GetDouble()), 9)

        // Rigetti: one ro readout per shot
        let rigetti = output "rigetti"
        Assert.Equal(shots, rigetti.GetProperty("ro").GetArrayLength())

        // Quantinuum: one c register string per shot
        let quantinuum = output "quantinuum"
        Assert.Equal(shots, quantinuum.GetProperty("c").GetArrayLength())
        // Only the providers' native registers: the library parses the real formats.
        Assert.False(quantinuum.TryGetProperty("results") |> fst)
        Assert.False(rigetti.TryGetProperty("histogram") |> fst)

        // Bell circuits carry no measurement; the backends added one per qubit, so the
        // service raised no "reads all zeros" warning.
        for job in service.Jobs do
            Assert.Empty job.Warnings

    // ------------------------------------------------------------------------
    // Job lifecycle
    // ------------------------------------------------------------------------

    [<Fact>]
    let ``Polling observes Waiting then Executing then Succeeded`` () =
        use service =
            LocalQuantumService.start
                { seeded with
                    PollsBeforeCompletion = 2
                }

        use http = service.CreateHttpClient()
        let backend = backendFor "ionq" service http (CloudBackendHelpers.JobBudget())

        backend.ExecuteToState bell |> expectOk |> ignore

        let job = service.Jobs |> List.exactlyOne
        Assert.Equal<JobStatus list>([ JobStatus.Waiting; JobStatus.Executing; JobStatus.Succeeded ], job.StatusHistory)
        Assert.Equal(3, job.StatusPolls)

    [<Fact>]
    let ``A target the service does not support is rejected as an Error`` () =
        use service = LocalQuantumService.start seeded
        use http = service.CreateHttpClient()

        let backend =
            QuantinuumCloudBackend(http, service.WorkspaceUrl, "contoso.qpu.imaginary", shots) :> IQuantumBackend

        match backend.ExecuteToState bell with
        | Ok _ -> failwith "expected the submission to be rejected"
        | Error(QuantumError.AzureError(AzureQuantumError.UnknownError(400, body))) ->
            Assert.Contains("InvalidTarget", body)
        | Error other -> failwith $"unexpected error {other}"

        Assert.Equal(0, service.SubmittedJobCount)

    [<Fact>]
    let ``A program the service cannot decode fails the job with InvalidCircuit`` () : Task =
        task {
            use service = LocalQuantumService.start seeded
            use http = service.CreateHttpClient()

            let submission: JobSubmission =
                {
                    JobId = Guid.NewGuid().ToString()
                    Target = "ionq.simulator"
                    Name = Some "garbage"
                    InputData = box "this is not an IonQ circuit"
                    InputDataFormat = CircuitFormat.IonQ_V1
                    InputParams = Map [ "shots", box 10 ]
                    Tags = Map.empty
                }

            let! jobId = JobLifecycle.submitJobAsync http service.WorkspaceUrl submission
            let jobId = expectOk jobId

            let! job =
                JobLifecycle.pollJobUntilCompleteAsync
                    http
                    service.WorkspaceUrl
                    jobId
                    (TimeSpan.FromMinutes 1.0)
                    CancellationToken.None

            let job = job |> expectOk

            match job.Status with
            | JobStatus.Failed(code, message) ->
                Assert.Equal("InvalidCircuit", code)
                Assert.Contains("IonQ", message)

                match IonQBackend.mapIonQError code message with
                | QuantumError.ValidationError _ -> ()
                | other -> failwith $"expected ValidationError, got {other}"
            | other -> failwith $"expected Failed, got {other}"

            Assert.Equal(None, job.OutputDataUri)
        }
        :> Task

    [<Fact>]
    let ``Injected job failure surfaces as an Error and the next job succeeds`` () =
        use service = LocalQuantumService.start seeded
        use http = service.CreateHttpClient()
        let backend = backendFor "ionq" service http (CloudBackendHelpers.JobBudget())

        service.FailNextJobs 1

        match backend.ExecuteToState bell with
        | Error(QuantumError.AzureError(AzureQuantumError.ServiceUnavailable _)) -> ()
        | other -> failwith $"expected ServiceUnavailable, got %A{other}"

        backend.ExecuteToState bell |> expectOk |> ignore
        Assert.Equal(2, service.SubmittedJobCount)

    [<Fact>]
    let ``Injected submission rejection surfaces as ServiceUnavailable without creating a job`` () =
        use service = LocalQuantumService.start seeded
        use http = service.CreateHttpClient()
        let backend = backendFor "rigetti" service http (CloudBackendHelpers.JobBudget())

        service.RejectNextSubmissions(1, HttpStatusCode.ServiceUnavailable)

        match backend.ExecuteToState bell with
        | Error(QuantumError.AzureError(AzureQuantumError.ServiceUnavailable _)) -> ()
        | other -> failwith $"expected ServiceUnavailable, got %A{other}"

        Assert.Equal(0, service.SubmittedJobCount)

    [<Fact>]
    let ``JobBudget counts jobs and refuses the one over its limit`` () =
        use service = LocalQuantumService.start seeded
        use http = service.CreateHttpClient()
        let budget = CloudBackendHelpers.JobBudget.Limit 2
        let ionq = backendFor "ionq" service http budget
        let rigetti = backendFor "rigetti" service http budget

        ionq.ExecuteToState bell |> expectOk |> ignore
        rigetti.ExecuteToState bell |> expectOk |> ignore

        match ionq.ExecuteToState bell with
        | Error(QuantumError.AzureError(AzureQuantumError.QuotaExceeded _)) -> ()
        | other -> failwith $"expected QuotaExceeded, got %A{other}"

        Assert.Equal(2, budget.Submitted)
        Assert.Equal(Some 0, budget.Remaining)
        Assert.Equal(2, service.SubmittedJobCount)

    // ------------------------------------------------------------------------
    // Authentication and isolation
    // ------------------------------------------------------------------------

    [<Fact>]
    let ``Bearer token goes to the workspace API and never to the result blob`` () =
        use service = LocalQuantumService.start seeded
        use http = service.CreateHttpClient()
        let backend = backendFor "quantinuum" service http (CloudBackendHelpers.JobBudget())

        backend.ExecuteToState bell |> expectOk |> ignore

        let requests = service.Requests

        let jobRequests =
            requests |> List.filter (fun r -> r.PathAndQuery.Contains "/jobs/")

        let blobRequests =
            requests |> List.filter (fun r -> r.PathAndQuery.StartsWith "/blobs/")

        Assert.NotEmpty jobRequests
        Assert.All(jobRequests, fun r -> Assert.True r.HadBearerToken)
        Assert.Single blobRequests |> ignore
        Assert.All(blobRequests, fun r -> Assert.False r.HadBearerToken)

    [<Fact>]
    let ``A client without the bearer token gets InvalidCredentials`` () =
        use service = LocalQuantumService.start seeded
        use http = new HttpClient(new SocketsHttpHandler(UseProxy = false))
        let backend = backendFor "ionq" service http (CloudBackendHelpers.JobBudget())

        match backend.ExecuteToState bell with
        | Error(QuantumError.AzureError AzureQuantumError.InvalidCredentials) -> ()
        | other -> failwith $"expected InvalidCredentials, got %A{other}"

        Assert.Equal(0, service.SubmittedJobCount)

    [<Fact>]
    let ``The service client refuses external hosts`` () =
        task {
            use service = LocalQuantumService.start seeded
            use http = service.CreateHttpClient()

            let! ex =
                Assert.ThrowsAnyAsync<exn>(fun () ->
                    http.GetAsync "https://example.com/" :> Task)

            Assert.Contains("refused", ex.Message)
            Assert.Empty service.Requests
        }
        :> Task

    // ------------------------------------------------------------------------
    // QuantumClient (Client.fs) through the routed data-plane URL
    // ------------------------------------------------------------------------

    [<Fact>]
    let ``QuantumClient submits, lists across pages, fetches results and cancels via the routed data-plane URL`` () : Task =
        task {
            use service = LocalQuantumService.start { seeded with JobsPageSize = 1 }

            use http = service.CreateHttpClient()
            let client = Client.QuantumClient(service.CreateClientConfig http)

            let ionqCircuit: IonQBackend.IonQCircuit =
                {
                    Qubits = 2
                    Circuit = [ IonQBackend.SingleQubit("h", 0); IonQBackend.TwoQubit("cnot", 0, 1) ]
                }

            let submission = IonQBackend.createJobSubmission ionqCircuit 200 "ionq.simulator"

            let! submitted = client.SubmitJobAsync submission
            let submitted = submitted |> expectOk

            Assert.Equal(submission.JobId, submitted.JobId)
            Assert.StartsWith("https://local.quantum.azure.com/subscriptions/", submitted.Uri)

            let! finished = client.WaitForCompletionAsync(submission.JobId, initialDelayMs = 10)
            let finished = finished |> expectOk

            Assert.Equal(JobStatus.Succeeded, finished.Status)

            let! result = client.GetResultsAsync submission.JobId
            let result = result |> expectOk

            Assert.Equal("ionq.quantum-results.v1", result.OutputDataFormat)

            let histogram =
                match IonQBackend.parseIonQResult 2 200 (result.OutputData :?> string) with
                | Ok h -> h
                | Error e -> failwith e

            Assert.Equal(200, histogram |> Map.toSeq |> Seq.sumBy snd)
            Assert.True(histogram |> Map.forall (fun key _ -> key = "00" || key = "11"))

            // A second job, then list: JobsPageSize = 1 forces nextLink pagination.
            let second = IonQBackend.createJobSubmission ionqCircuit 10 "ionq.simulator"
            let! secondSubmitted = client.SubmitJobAsync second
            secondSubmitted |> expectOk |> ignore

            let! listed = client.ListJobsAsync()
            let listed = listed |> expectOk
            Assert.Equal<string list>([ submission.JobId; second.JobId ], listed |> List.map (fun j -> j.JobId))

            // The second job has not been polled, so it is still Waiting and can be cancelled.
            let! cancelled = client.CancelJobAsync second.JobId
            cancelled |> expectOk
            Assert.Equal(JobStatus.Cancelled, (service.TryGetJob second.JobId).Value.Status)
        }
        :> Task

    // ------------------------------------------------------------------------
    // Decoders
    // ------------------------------------------------------------------------

    let private runProgram (program: LocalQuantumServiceFormats.DecodedProgram) =
        LocalQuantumServiceFormats.execute (Random 7) 50 program
        |> Result.map LocalQuantumServiceFormats.histogram

    [<Fact>]
    let ``Quil decoder handles MEASURE into ro, DAGGER, pi expressions and comments`` () =
        let quil =
            String.concat
                "\n"
                [
                    "# comment"
                    "DECLARE ro BIT[3]"
                    "PRAGMA INITIAL_REWIRING \"NAIVE\""
                    "RX(pi/2) 0"
                    "DAGGER RX(pi/2) 0 # undoes the rotation"
                    "X 1"
                    "MEASURE 1 ro[2]"
                ]

        let program = LocalQuantumServiceFormats.decodeQuil quil

        match program with
        | Error e -> failwith e
        | Ok program ->
            Assert.Equal(3, program.ClassicalBits)
            Assert.Equal(Ok(Map [ "100", 50 ]), runProgram program)

    [<Fact>]
    let ``Quil decoder rejects unsupported instructions with a clear message`` () =
        match LocalQuantumServiceFormats.decodeQuil "DECLARE ro BIT[1]\nDEFGATE FOO:\n    1, 0\n    0, 1" with
        | Error e -> Assert.Contains("line 2", e)
        | Ok _ -> failwith "expected an error"

    [<Fact>]
    let ``Mid-circuit measurement is simulated shot by shot`` () =
        let qasm =
            "OPENQASM 2.0;\ninclude \"qelib1.inc\";\nqreg q[2];\ncreg c[2];\nx q[0];\nmeasure q[0] -> c[0];\nx q[0];\nh q[1];\nh q[1];"

        match LocalQuantumServiceFormats.decodeOpenQasm qasm with
        | Error e -> failwith e
        | Ok program ->
            // c[0] records the measurement before the second X; c[1] is never written.
            Assert.Equal(Ok(Map [ "01", 50 ]), runProgram program)

    // ------------------------------------------------------------------------
    // Provider result formats and readout
    // ------------------------------------------------------------------------

    [<Fact>]
    let ``Rigetti ro and Quantinuum c registers parse per shot with bit 0 on the right`` () =
        // ro[0] = 1, ro[1] = 0 on two shots; ro[0] = 0, ro[1] = 1 on one.
        match RigettiBackend.parseRigettiResults """{"ro": [[1, 0], [1, 0], [0, 1]]}""" with
        | Ok histogram -> Assert.Equal<Map<string, int>>(Map [ "01", 2; "10", 1 ], histogram)
        | Error e -> Assert.Fail(e.Message)

        match QuantinuumBackend.parseQuantinuumResult """{"c": ["01", "01", "10"]}""" with
        | Ok histogram -> Assert.Equal<Map<string, int>>(Map [ "01", 2; "10", 1 ], histogram)
        | Error e -> Assert.Fail e

        // The aggregated shapes are still read.
        Assert.True(RigettiBackend.parseRigettiResults """{"histogram": {"00": 3}}""" |> Result.isOk)

        Assert.True(
            QuantinuumBackend.parseQuantinuumResult """{"results": {"00": 3}}"""
            |> Result.isOk
        )

    [<Fact>]
    let ``Circuits without measurements are read out in full on every provider`` () =
        use service = LocalQuantumService.start seeded
        use http = service.CreateHttpClient()
        let xOnOne = circuitOf 2 [ CircuitBuilder.X 1 ]

        for provider in [ "rigetti"; "quantinuum"; "iqm"; "atom" ] do
            let backend = backendFor provider service http (CloudBackendHelpers.JobBudget())
            let state = backend.ExecuteToState xOnOne |> expectOk

            // Every shot reads qubit 1 as 1: the submitted program measured it.
            Assert.Equal(1.0, (probabilities state).[2], 9)
            // The state carries the job's own shots.
            Assert.Equal(Some shots, QuantumState.recordedShotCount state)
            Assert.Equal(shots, (UnifiedBackend.measureState state 5000).Length)

        let quil =
            service.Jobs
            |> List.find (fun j -> j.ProviderId = "rigetti")
            |> fun j -> j.InputData

        Assert.Contains("MEASURE 1 ro[1]", quil)
