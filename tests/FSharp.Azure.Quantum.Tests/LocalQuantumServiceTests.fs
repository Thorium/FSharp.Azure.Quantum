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
                    ProviderId = None
                    Name = Some "garbage"
                    InputData = box "this is not an IonQ circuit"
                    InputDataFormat = CircuitFormat.IonQ_V1
                    OutputDataFormat = None
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
    let ``Bearer token goes to the workspace API and never to the input or result blob`` () =
        use service = LocalQuantumService.start seeded
        use http = service.CreateHttpClient()
        let backend = backendFor "quantinuum" service http (CloudBackendHelpers.JobBudget())

        backend.ExecuteToState bell |> expectOk |> ignore

        let requests = service.Requests

        let workspaceRequests =
            requests
            |> List.filter (fun r -> r.PathAndQuery.Contains "/jobs/" || r.PathAndQuery.Contains "/storage/sasUri")

        let blobRequests =
            requests |> List.filter (fun r -> r.PathAndQuery.StartsWith "/blobs/")

        Assert.NotEmpty workspaceRequests
        Assert.All(workspaceRequests, fun r -> Assert.True r.HadBearerToken)
        // The input upload and the result download, each authorised by its SAS query alone.
        Assert.Equal<string list>([ "PUT"; "GET" ], blobRequests |> List.map (fun r -> r.Method))
        Assert.All(blobRequests, fun r -> Assert.False r.HadBearerToken)
        Assert.All(requests, fun r -> Assert.InRange(r.StatusCode, 200, 299))

    // ------------------------------------------------------------------------
    // The data-plane contract the service enforces
    // ------------------------------------------------------------------------

    let private apiVersion = $"api-version={JobLifecycle.ApiVersion}"

    let private send (http: HttpClient) (method: HttpMethod) (url: string) (body: string option) =
        task {
            use request = new HttpRequestMessage(method, url)

            body
            |> Option.iter (fun text ->
                request.Content <- new StringContent(text, Text.Encoding.UTF8, "application/json"))

            use! response = http.SendAsync request
            let! text = response.Content.ReadAsStringAsync()
            return int response.StatusCode, text
        }

    /// Container and input blob SAS URIs for a job, with a Bell circuit uploaded as its input.
    let private uploadInput (service: LocalQuantumService) (http: HttpClient) (jobId: string) =
        task {
            let sasUri (body: string) =
                task {
                    let! status, text =
                        send http HttpMethod.Post $"{service.WorkspaceUrl}/storage/sasUri?{apiVersion}" (Some body)

                    Assert.Equal(200, status)
                    return JsonDocument.Parse(text).RootElement.GetProperty("sasUri").GetString()
                }

            let! containerUri = sasUri $"""{{"containerName":"job-{jobId}"}}"""
            let! inputDataUri = sasUri $"""{{"containerName":"job-{jobId}","blobName":"inputData"}}"""

            use upload = new HttpRequestMessage(HttpMethod.Put, inputDataUri)
            Authentication.markNoAuth upload
            upload.Headers.TryAddWithoutValidation("x-ms-blob-type", "BlockBlob") |> ignore

            upload.Content <-
                new StringContent("""{"qubits":2,"circuit":[{"gate":"h","target":0},{"gate":"cnot","control":0,"target":1}]}""")

            use! uploaded = http.SendAsync upload
            Assert.Equal(HttpStatusCode.Created, uploaded.StatusCode)
            return containerUri, inputDataUri
        }

    /// A job definition that meets the contract, as (property, JSON value) pairs.
    let private jobFields (jobId: string) (containerUri: string) (inputDataUri: string) =
        [
            "id", $"\"{jobId}\""
            "itemType", "\"Job\""
            "name", "\"contract\""
            "providerId", "\"ionq\""
            "target", "\"ionq.simulator\""
            "containerUri", $"\"{containerUri}\""
            "inputDataUri", $"\"{inputDataUri}\""
            "inputDataFormat", "\"ionq.circuit.v1\""
            "inputParams", """{"shots":10}"""
        ]

    let private jobBody (fields: (string * string) list) =
        fields
        |> List.map (fun (name, value) -> $"\"{name}\":{value}")
        |> String.concat ","
        |> sprintf "{%s}"

    [<Fact>]
    let ``A job that meets the contract is created, and one posted to :cancel is cancelled`` () : Task =
        task {
            use service = LocalQuantumService.start seeded
            use http = service.CreateHttpClient()
            let jobId = Guid.NewGuid().ToString()
            let! containerUri, inputDataUri = uploadInput service http jobId
            let jobUrl = $"{service.WorkspaceUrl}/jobs/{jobId}"

            let! status, body =
                send http HttpMethod.Put $"{jobUrl}?{apiVersion}" (Some(jobBody (jobFields jobId containerUri inputDataUri)))

            Assert.Equal(201, status)
            Assert.Equal("Waiting", JsonDocument.Parse(body).RootElement.GetProperty("status").GetString())

            // {job}/cancel is not a route of the data plane: cancellation is {job}:cancel.
            let! slashCancelStatus, _ = send http HttpMethod.Post $"{jobUrl}/cancel?{apiVersion}" None
            Assert.Equal(404, slashCancelStatus)

            let! cancelStatus, cancelBody = send http HttpMethod.Post $"{jobUrl}:cancel?{apiVersion}" None
            Assert.Equal(200, cancelStatus)
            Assert.Equal("Cancelled", JsonDocument.Parse(cancelBody).RootElement.GetProperty("status").GetString())
            Assert.Equal(JobStatus.Cancelled, (service.TryGetJob jobId).Value.Status)
        }
        :> Task

    [<Theory; InlineData("jobs"); InlineData("quotas"); InlineData("providerStatus")>]
    let ``A workspace request without api-version is rejected`` (resource: string) : Task =
        task {
            use service = LocalQuantumService.start seeded
            use http = service.CreateHttpClient()

            let! status, body = send http HttpMethod.Get $"{service.WorkspaceUrl}/{resource}" None
            Assert.Equal(400, status)
            Assert.Contains("MissingApiVersionParameter", body)

            let! withVersion, _ = send http HttpMethod.Get $"{service.WorkspaceUrl}/{resource}?{apiVersion}" None
            Assert.Equal(200, withVersion)
        }
        :> Task

    [<Fact>]
    let ``Job creation, status, cancel and sasUri requests without api-version are rejected`` () : Task =
        task {
            use service = LocalQuantumService.start seeded
            use http = service.CreateHttpClient()
            let jobId = Guid.NewGuid().ToString()
            let! containerUri, inputDataUri = uploadInput service http jobId
            let jobUrl = $"{service.WorkspaceUrl}/jobs/{jobId}"
            let body = jobBody (jobFields jobId containerUri inputDataUri)

            let! create, _ = send http HttpMethod.Put jobUrl (Some body)
            Assert.Equal(400, create)
            Assert.Equal(0, service.SubmittedJobCount)

            let! created, _ = send http HttpMethod.Put $"{jobUrl}?{apiVersion}" (Some body)
            Assert.Equal(201, created)

            let! status, _ = send http HttpMethod.Get jobUrl None
            Assert.Equal(400, status)
            let! cancel, _ = send http HttpMethod.Post $"{jobUrl}:cancel" None
            Assert.Equal(400, cancel)

            let! sas, _ =
                send http HttpMethod.Post $"{service.WorkspaceUrl}/storage/sasUri" (Some """{"containerName":"c"}""")

            Assert.Equal(400, sas)
            Assert.Equal(JobStatus.Waiting, (service.TryGetJob jobId).Value.Status)
        }
        :> Task

    [<Theory>]
    [<InlineData("containerUri")>]
    [<InlineData("providerId")>]
    [<InlineData("target")>]
    [<InlineData("name")>]
    [<InlineData("itemType")>]
    let ``A job without a required property is rejected`` (missing: string) : Task =
        task {
            use service = LocalQuantumService.start seeded
            use http = service.CreateHttpClient()
            let jobId = Guid.NewGuid().ToString()
            let! containerUri, inputDataUri = uploadInput service http jobId

            let body =
                jobFields jobId containerUri inputDataUri
                |> List.filter (fun (name, _) -> name <> missing)
                |> jobBody

            let! status, reply = send http HttpMethod.Put $"{service.WorkspaceUrl}/jobs/{jobId}?{apiVersion}" (Some body)

            Assert.Equal(400, status)
            Assert.Contains(missing, reply)
            Assert.Equal(0, service.SubmittedJobCount)
        }
        :> Task

    [<Fact>]
    let ``Inline input, a non-GUID id, a foreign provider and an unknown container are rejected`` () : Task =
        task {
            use service = LocalQuantumService.start seeded
            use http = service.CreateHttpClient()
            let jobId = Guid.NewGuid().ToString()
            let! containerUri, inputDataUri = uploadInput service http jobId
            let fields = jobFields jobId containerUri inputDataUri

            let create (pid: string) (body: string) =
                send http HttpMethod.Put $"{service.WorkspaceUrl}/jobs/{pid}?{apiVersion}" (Some body)

            let replace (name: string) (value: string) =
                fields |> List.map (fun (n, v) -> if n = name then n, value else n, v)

            // The contract has no inline input property.
            let! inlineStatus, inlineReply =
                create jobId (jobBody (fields @ [ "inputData", """{"qubits":1,"circuit":[]}""" ]))

            Assert.Equal(400, inlineStatus)
            Assert.Contains("inputData", inlineReply)

            let! idStatus, idReply = create "job-123" (jobBody (replace "id" "\"job-123\""))
            Assert.Equal(400, idStatus)
            Assert.Contains("GUID", idReply)

            let! providerStatus, providerReply = create jobId (jobBody (replace "providerId" "\"rigetti\""))
            Assert.Equal(400, providerStatus)
            Assert.Contains("providerId", providerReply)

            let foreignContainer = containerUri.Replace($"job-{jobId}", "never-requested")
            let! containerStatus, containerReply = create jobId (jobBody (replace "containerUri" $"\"{foreignContainer}\""))
            Assert.Equal(400, containerStatus)
            Assert.Contains("containerUri", containerReply)

            Assert.Equal(0, service.SubmittedJobCount)
        }
        :> Task

    [<Fact>]
    let ``A blob upload needs the BlockBlob header, a SAS signature and an existing container`` () : Task =
        task {
            use service = LocalQuantumService.start seeded
            use http = service.CreateHttpClient()
            let jobId = Guid.NewGuid().ToString()
            let! _, inputDataUri = uploadInput service http jobId

            let put (url: string) (blobType: string option) =
                task {
                    use request = new HttpRequestMessage(HttpMethod.Put, url)
                    Authentication.markNoAuth request

                    blobType
                    |> Option.iter (fun value ->
                        request.Headers.TryAddWithoutValidation("x-ms-blob-type", value) |> ignore)

                    request.Content <- new StringContent "{}"
                    use! response = http.SendAsync request
                    return int response.StatusCode
                }

            let! noHeader = put inputDataUri None
            Assert.Equal(400, noHeader)

            let! noSignature = put (inputDataUri.Substring(0, inputDataUri.IndexOf '?')) (Some "BlockBlob")
            Assert.Equal(403, noSignature)

            let! noContainer = put (inputDataUri.Replace($"job-{jobId}", "never-requested")) (Some "BlockBlob")
            Assert.Equal(404, noContainer)

            let! ok = put inputDataUri (Some "BlockBlob")
            Assert.Equal(201, ok)
        }
        :> Task

    // ------------------------------------------------------------------------
    // Jobs that are no longer waited for
    // ------------------------------------------------------------------------

    [<Fact>]
    let ``A backend that times out cancels its job and names it in the Error`` () =
        // The job never completes within the backend's timeout.
        use service =
            LocalQuantumService.start
                { seeded with
                    ExecutionDelay = TimeSpan.FromMinutes 10.0
                }

        use http = service.CreateHttpClient()

        let backend =
            CloudBackendFactory.createIonQWithTimeout
                http
                service.WorkspaceUrl
                "ionq.simulator"
                shots
                (TimeSpan.FromMilliseconds 200.0)

        match backend.ExecuteToState bell with
        | Error(QuantumError.AzureError(AzureQuantumError.JobTimeout message)) ->
            let job = service.Jobs |> List.exactlyOne
            Assert.Contains(job.JobId, message)
            Assert.Contains("cancellation request was accepted", message)
            Assert.Equal(JobStatus.Cancelled, job.Status)
        | other -> failwith $"expected JobTimeout, got %A{other}"

    [<Fact>]
    let ``A caller's cancellation cancels the job on the service`` () : Task =
        task {
            use service =
                LocalQuantumService.start
                    { seeded with
                        ExecutionDelay = TimeSpan.FromMinutes 10.0
                    }

            use http = service.CreateHttpClient()
            let backend = backendFor "rigetti" service http (CloudBackendHelpers.JobBudget())
            use cts = new CancellationTokenSource(TimeSpan.FromMilliseconds 300.0)

            match! backend.ExecuteToStateAsync bell cts.Token with
            | Error(QuantumError.OperationError("Job polling", message)) ->
                let job = service.Jobs |> List.exactlyOne
                Assert.Contains(job.JobId, message)
                Assert.Contains("cancelled", message)
                Assert.Equal(JobStatus.Cancelled, job.Status)
            | other -> failwith $"expected the polling cancellation error, got %A{other}"
        }
        :> Task

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
                Assert.ThrowsAnyAsync<exn>(fun () -> http.GetAsync "https://example.com/" :> Task)

            Assert.Contains("refused", ex.Message)
            Assert.Empty service.Requests
        }
        :> Task

    // ------------------------------------------------------------------------
    // QuantumClient (Client.fs) through the routed data-plane URL
    // ------------------------------------------------------------------------

    [<Fact>]
    let ``QuantumClient submits, lists across pages, fetches results and cancels via the routed data-plane URL``
        ()
        : Task =
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

    [<Fact>]
    let ``Workspace quotas and provider status come from the service`` () : Task =
        task {
            use service = LocalQuantumService.start seeded
            use http = service.CreateHttpClient()

            // One IonQ job, so that provider has utilization and a target
            let backend = backendFor "ionq" service http (CloudBackendHelpers.JobBudget())
            let! ran = backend.ExecuteToStateAsync bell CancellationToken.None
            ran |> expectOk |> ignore

            let workspace =
                AzureQuantumWorkspace.createWithHttpClient
                    seeded.SubscriptionId
                    seeded.ResourceGroup
                    seeded.WorkspaceName
                    seeded.Location
                    http

            let! quotas = workspace.ListQuotasAsync CancellationToken.None
            Assert.Equal(5, quotas.Length)

            let ionq = quotas |> List.find (fun q -> q.Provider = "ionq")
            Assert.Equal(Some(float shots), ionq.Used)
            Assert.Equal(Some 1_000_000.0, ionq.Limit)
            Assert.Equal(Some(1_000_000.0 - float shots), ionq.Remaining)
            Assert.Equal(Some "Workspace", ionq.Scope)
            Assert.Equal(Some "Monthly", ionq.Period)

            let! rigetti = workspace.GetProviderQuotaAsync("rigetti", CancellationToken.None)
            Assert.Equal(Some 0.0, rigetti |> Option.bind (fun q -> q.Used))

            let! total = workspace.GetTotalQuotaAsync CancellationToken.None
            Assert.Equal(Some 5_000_000.0, total.Limit)
            Assert.Equal(Some(float shots), total.Used)

            let! providers = workspace.ListProvidersAsync CancellationToken.None
            Assert.Equal(5, providers.Length)

            let ionqStatus = providers |> List.find (fun p -> p.ProviderId = "ionq")
            Assert.Equal(Some "Available", ionqStatus.CurrentAvailability)
            Assert.Equal(1, ionqStatus.TargetCount)
            Assert.Equal(0, (providers |> List.find (fun p -> p.ProviderId = "iqm")).TargetCount)

            // The workspace never disposes a client it was given
            (workspace :> IDisposable).Dispose()
            use second = AzureQuantumWorkspace.createWithHttpClient "s" "rg" "ws" "local" http
            let! again = second.ListQuotasAsync CancellationToken.None
            Assert.Equal(5, again.Length)
        }
        :> Task
