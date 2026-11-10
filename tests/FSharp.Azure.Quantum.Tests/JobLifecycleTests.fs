namespace FSharp.Azure.Quantum.Tests

open Xunit
open FSharp.Azure.Quantum.Core
open FSharp.Azure.Quantum.Core.Types
open FSharp.Azure.Quantum.Core.JobLifecycle
open System
open System.Net
open System.Net.Http
open System.Text
open System.Text.Json
open System.Threading
open System.Threading.Tasks

module JobLifecycleTests =

    // ============================================================================
    // Mock HTTP Message Handler
    // ============================================================================

    /// Mock HTTP message handler for testing
    type MockHttpMessageHandler(responseFunc: HttpRequestMessage -> Task<HttpResponseMessage>) =
        inherit HttpMessageHandler()

        override _.SendAsync(request: HttpRequestMessage, cancellationToken: CancellationToken) = responseFunc request

    /// Create HttpClient with mock handler
    let createMockHttpClient (responseFunc: HttpRequestMessage -> Task<HttpResponseMessage>) =
        let handler = new MockHttpMessageHandler(responseFunc)
        new HttpClient(handler)

    [<Literal>]
    let private workspaceUrl =
        "https://test.quantum.azure.com/subscriptions/00000000-0000-0000-0000-000000000001/resourceGroups/rg/providers/Microsoft.Quantum/workspaces/ws"

    [<Literal>]
    let private sasSecret = "TOPSECRETSIGNATURE"

    /// One request as the mock saw it.
    type private Seen =
        {
            Method: string
            Url: string
            Body: string
            BlobType: string option
            NoAuth: bool
        }

    let private json (status: HttpStatusCode) (body: string) =
        new HttpResponseMessage(status, Content = new StringContent(body, Encoding.UTF8, "application/json"))

    let private jobJson (jobId: string) (status: string) =
        $"""{{ "id": "{jobId}", "status": "{status}", "target": "ionq.simulator", "creationTime": "2025-01-01T10:00:00Z" }}"""

    /// Mock of the data plane and its blob storage. `respond` may answer a request itself
    /// (Some response); every other request gets the contract's success answer.
    let private mockService (respond: Seen -> HttpResponseMessage option) =
        let seen = Collections.Generic.List<Seen>()

        let handle (request: HttpRequestMessage) : Task<HttpResponseMessage> =
            task {
                let! body =
                    if isNull request.Content then
                        Task.FromResult ""
                    else
                        request.Content.ReadAsStringAsync()

                let url = request.RequestUri.ToString()

                let record =
                    {
                        Method = request.Method.Method
                        Url = url
                        Body = body
                        BlobType =
                            match request.Headers.TryGetValues "x-ms-blob-type" with
                            | true, values -> Seq.tryHead values
                            | _ -> None
                        NoAuth =
                            match request.Options.TryGetValue Authentication.noAuthOptionKey with
                            | true, value -> value
                            | _ -> false
                    }

                lock seen (fun () -> seen.Add record)

                match respond record with
                | Some response -> return response
                | None ->
                    if url.Contains "/storage/sasUri" then
                        use doc = JsonDocument.Parse body
                        let container = doc.RootElement.GetProperty("containerName").GetString()

                        let path =
                            match doc.RootElement.TryGetProperty "blobName" with
                            | true, blob -> $"{container}/{blob.GetString()}"
                            | _ -> container

                        return
                            json
                                HttpStatusCode.OK
                                $"""{{ "sasUri": "https://storage.example/{path}?sv=2019-02-02&sig={sasSecret}&sp=rcw" }}"""
                    elif url.StartsWith "https://storage.example/" then
                        return new HttpResponseMessage(HttpStatusCode.Created)
                    elif url.Contains ":cancel" then
                        return json HttpStatusCode.OK "{}"
                    elif record.Method = "PUT" then
                        use doc = JsonDocument.Parse body
                        return json HttpStatusCode.Created (jobJson (doc.RootElement.GetProperty("id").GetString()) "Waiting")
                    else
                        return json HttpStatusCode.OK (jobJson "unknown" "Executing")
            }

        createMockHttpClient handle, (fun () -> lock seen (fun () -> List.ofSeq seen))

    let private submissionWith (jobId: string) : JobSubmission =
        {
            JobId = jobId
            Target = "ionq.simulator"
            ProviderId = None
            Name = Some "Test Job"
            InputData = box {| qubits = 2; circuit = [||] |}
            InputDataFormat = CircuitFormat.IonQ_V1
            OutputDataFormat = Some "ionq.quantum-results.v1"
            InputParams = Map.ofList [ ("shots", box 1000) ]
            Tags = Map.ofList [ ("purpose", "test") ]
        }

    let private isWorkspaceRequest (request: Seen) = request.Url.StartsWith workspaceUrl

    // ============================================================================
    // Job Submission Tests
    // ============================================================================

    [<Fact>]
    let ``submitJobAsync should return job ID on successful submission`` () : Task =
        task {
            let jobId = Guid.NewGuid().ToString()
            let httpClient, _ = mockService (fun _ -> None)

            match! submitJobAsync httpClient workspaceUrl (submissionWith jobId) with
            | Ok returnedJobId -> Assert.Equal(jobId, returnedJobId)
            | Error err -> Assert.True(false, $"Expected success but got error: %A{err}")
        }

    [<Fact>]
    let ``submitJobAsync asks for the container and blob SAS, uploads the input, then creates the job`` () : Task =
        task {
            let jobId = Guid.NewGuid().ToString()
            let httpClient, seen = mockService (fun _ -> None)

            let! submitted = submitJobAsync httpClient workspaceUrl (submissionWith jobId)
            Assert.Equal(Ok jobId, submitted)

            match seen () with
            | [ containerSas; blobSas; upload; create ] ->
                // Container SAS, then the input blob's SAS
                Assert.Equal("POST", containerSas.Method)
                Assert.StartsWith($"{workspaceUrl}/storage/sasUri?", containerSas.Url)
                Assert.Equal($"""{{"containerName":"job-{jobId}"}}""", containerSas.Body)
                Assert.Equal("POST", blobSas.Method)
                Assert.Contains("\"blobName\":\"inputData\"", blobSas.Body)
                Assert.Contains($"\"containerName\":\"job-{jobId}\"", blobSas.Body)

                // Input upload: a block blob, to the SAS URL, with the bearer token withheld
                Assert.Equal("PUT", upload.Method)
                Assert.StartsWith($"https://storage.example/job-{jobId}/inputData?", upload.Url)
                Assert.Equal(Some "BlockBlob", upload.BlobType)
                Assert.True(upload.NoAuth, "the blob upload must not carry the workspace bearer token")
                Assert.Contains("\"qubits\":2", upload.Body)

                // Job creation
                Assert.Equal("PUT", create.Method)
                Assert.Equal($"{workspaceUrl}/jobs/{jobId}?api-version={ApiVersion}", create.Url)
                Assert.False(create.NoAuth)

                use doc = JsonDocument.Parse create.Body
                let root = doc.RootElement
                let text (name: string) = root.GetProperty(name).GetString()
                Assert.Equal(jobId, text "id")
                Assert.Equal("Job", text "itemType")
                Assert.Equal("Test Job", text "name")
                Assert.Equal("ionq", text "providerId")
                Assert.Equal("ionq.simulator", text "target")
                Assert.StartsWith($"https://storage.example/job-{jobId}?", text "containerUri")
                Assert.StartsWith($"https://storage.example/job-{jobId}/inputData?", text "inputDataUri")
                Assert.Equal("ionq.circuit.v1", text "inputDataFormat")
                Assert.Equal("ionq.quantum-results.v1", text "outputDataFormat")
                Assert.Equal(1000, root.GetProperty("inputParams").GetProperty("shots").GetInt32())
                Assert.Equal("test", root.GetProperty("metadata").GetProperty("purpose").GetString())
                Assert.False(root.TryGetProperty("inputData") |> fst, "a job carries no inline input")
            | other -> Assert.Fail($"expected four requests, got %A{other}")

            // api-version is required on every workspace request
            for request in seen () |> List.filter isWorkspaceRequest do
                Assert.Contains($"api-version={ApiVersion}", request.Url)
        }

    [<Fact>]
    let ``submitJobAsync accepts 200 OK as well as 201 Created`` () : Task =
        task {
            let jobId = Guid.NewGuid().ToString()

            let httpClient, _ =
                mockService (fun request ->
                    if request.Method = "PUT" && request.Url.Contains "/jobs/" then
                        Some(json HttpStatusCode.OK (jobJson jobId "Waiting"))
                    else
                        None)

            let! submitted = submitJobAsync httpClient workspaceUrl (submissionWith jobId)
            Assert.Equal(Ok jobId, submitted)
        }

    [<Fact>]
    let ``submitJobAsync derives the provider id from the target unless the submission names one`` () =
        let submission = submissionWith (Guid.NewGuid().ToString())
        Assert.Equal("ionq", providerIdOf submission)

        Assert.Equal(
            "microsoft-qc",
            providerIdOf
                { submission with
                    Target = "microsoft.estimator"
                    ProviderId = Some "microsoft-qc"
                }
        )

    [<Fact>]
    let ``submitJobAsync rejects a job id that is not a GUID before any request`` () : Task =
        task {
            let httpClient, seen = mockService (fun _ -> None)

            match! submitJobAsync httpClient workspaceUrl (submissionWith "job-123") with
            | Error(QuantumError.ValidationError("JobId", _)) -> ()
            | other -> Assert.Fail($"expected a JobId validation error, got %A{other}")

            Assert.Empty(seen ())
        }

    [<Fact>]
    let ``submitJobAsync should return InvalidCredentials on 401 response`` () : Task =
        task {
            let httpClient, seen =
                mockService (fun _ -> Some(new HttpResponseMessage(HttpStatusCode.Unauthorized)))

            match! submitJobAsync httpClient workspaceUrl (submissionWith (Guid.NewGuid().ToString())) with
            | Ok _ -> Assert.True(false, "Expected InvalidCredentials error")
            | Error(QuantumError.AzureError(AzureQuantumError.InvalidCredentials)) -> Assert.True(true)
            | Error other -> Assert.True(false, $"Expected InvalidCredentials but got: %A{other}")

            // Nothing is uploaded and no job is created once the workspace refuses the SAS request.
            Assert.Single(seen ()) |> ignore
        }

    [<Fact>]
    let ``A failed input upload is an Error without the SAS signature, and no job is created`` () : Task =
        task {
            let httpClient, seen =
                mockService (fun request ->
                    if request.Url.StartsWith "https://storage.example/" then
                        Some(
                            json
                                HttpStatusCode.Forbidden
                                $"<Error><Code>AuthenticationFailed</Code><Message>{request.Url}</Message></Error>"
                        )
                    else
                        None)

            match! submitJobAsync httpClient workspaceUrl (submissionWith (Guid.NewGuid().ToString())) with
            | Error(QuantumError.AzureError(AzureQuantumError.UnknownError(403, message))) ->
                Assert.DoesNotContain(sasSecret, message)
                Assert.Contains("sig=REDACTED", message)
            | other -> Assert.Fail($"expected the upload failure, got %A{other}")

            Assert.DoesNotContain(seen (), fun request -> request.Url.Contains "/jobs/")
        }

    // ============================================================================
    // Job Status Tests
    // ============================================================================

    [<Fact>]
    let ``getJobStatusAsync should return job status for successful request`` () : Task =
        task {
            let jobId = Guid.NewGuid().ToString()

            let httpClient, seen =
                mockService (fun _ ->
                    Some(
                        json
                            HttpStatusCode.OK
                            $"""{{
                        "id": "{jobId}",
                        "status": "Executing",
                        "target": "ionq.simulator",
                        "creationTime": "2025-01-01T10:00:00Z",
                        "beginExecutionTime": "2025-01-01T10:00:05Z"
                    }}"""
                    ))

            match! getJobStatusAsync httpClient workspaceUrl jobId with
            | Ok job ->
                Assert.Equal(jobId, job.JobId)
                Assert.Equal(Types.JobStatus.Executing, job.Status)
                Assert.Equal("ionq.simulator", job.Target)
            | Error err -> Assert.True(false, $"Expected success but got error: %A{err}")

            let request = seen () |> List.exactlyOne
            Assert.Equal("GET", request.Method)
            Assert.Equal($"{workspaceUrl}/jobs/{jobId}?api-version={ApiVersion}", request.Url)
        }

    [<Fact>]
    let ``getJobStatusAsync should parse Succeeded status correctly`` () : Task =
        task {
            let jobId = Guid.NewGuid().ToString()

            let httpClient, _ =
                mockService (fun _ ->
                    Some(
                        json
                            HttpStatusCode.OK
                            $"""{{
                        "id": "{jobId}",
                        "status": "Succeeded",
                        "target": "ionq.simulator",
                        "creationTime": "2025-01-01T10:00:00Z",
                        "beginExecutionTime": "2025-01-01T10:00:05Z",
                        "endExecutionTime": "2025-01-01T10:00:10Z"
                    }}"""
                    ))

            match! getJobStatusAsync httpClient workspaceUrl jobId with
            | Ok job ->
                Assert.Equal(Types.JobStatus.Succeeded, job.Status)
                Assert.True(job.EndExecutionTime.IsSome)
            | Error err -> Assert.True(false, $"Expected success but got error: %A{err}")
        }

    [<Fact>]
    let ``getJobStatusAsync should handle Failed status with error details`` () : Task =
        task {
            let jobId = Guid.NewGuid().ToString()

            let httpClient, _ =
                mockService (fun _ ->
                    Some(
                        json
                            HttpStatusCode.OK
                            $"""{{
                        "id": "{jobId}",
                        "status": "Failed",
                        "target": "ionq.simulator",
                        "creationTime": "2025-01-01T10:00:00Z",
                        "errorData": {{
                            "code": "InvalidCircuit",
                            "message": "Circuit contains invalid gates"
                        }}
                    }}"""
                    ))

            match! getJobStatusAsync httpClient workspaceUrl jobId with
            | Ok job ->
                match job.Status with
                | Types.JobStatus.Failed(code, message) ->
                    Assert.Equal("InvalidCircuit", code)
                    Assert.Contains("invalid gates", message)
                | _ -> Assert.True(false, $"Expected Failed status but got: %A{job.Status}")
            | Error err -> Assert.True(false, $"Expected success but got error: %A{err}")
        }

    // ============================================================================
    // Polling Tests
    // ============================================================================

    /// Mock whose n-th status request (1-based) is answered by `status n`; cancellation
    /// requests are answered by `cancel`.
    let private pollingService
        (jobId: string)
        (status: int -> HttpResponseMessage)
        (cancel: unit -> HttpResponseMessage)
        =
        let polls = ref 0

        let httpClient, seen =
            mockService (fun request ->
                if request.Url.Contains ":cancel" then
                    Some(cancel ())
                else
                    Some(status (Interlocked.Increment(&polls.contents))))

        let cancelRequests () =
            seen () |> List.filter (fun request -> request.Url.Contains ":cancel")

        ignore jobId
        httpClient, (fun () -> polls.Value), cancelRequests

    let private accepted () = json HttpStatusCode.OK "{}"

    [<Fact; Trait("Category", "Slow")>]
    let ``pollJobUntilCompleteAsync should poll until job succeeds`` () : Task =
        task {
            // Arrange: Mock that returns Executing twice, then Succeeded
            let jobId = Guid.NewGuid().ToString()

            let httpClient, polls, cancelRequests =
                pollingService
                    jobId
                    (fun n -> json HttpStatusCode.OK (jobJson jobId (if n < 3 then "Executing" else "Succeeded")))
                    accepted

            use cts = new CancellationTokenSource()

            match! pollJobUntilCompleteAsync httpClient workspaceUrl jobId (TimeSpan.FromSeconds 30.0) cts.Token with
            | Ok job ->
                Assert.Equal(3, polls ()) // Should have polled 3 times
                Assert.Equal(Types.JobStatus.Succeeded, job.Status)
            | Error err -> Assert.True(false, $"Expected success but got error: %A{err}")

            Assert.Empty(cancelRequests ())
        }

    [<Fact>]
    let ``pollJobUntilCompleteAsync keeps polling through statuses that are not final`` () : Task =
        task {
            // Finishing is documented and not final; "Paused" is a status this version does not
            // know. Neither ends the wait, and neither is a failure.
            let jobId = Guid.NewGuid().ToString()

            let httpClient, polls, cancelRequests =
                pollingService
                    jobId
                    (fun n ->
                        let status =
                            match n with
                            | 1 -> "Paused"
                            | _ -> "Succeeded"

                        json HttpStatusCode.OK (jobJson jobId status))
                    accepted

            match!
                pollJobUntilCompleteAsync httpClient workspaceUrl jobId (TimeSpan.FromSeconds 30.0) CancellationToken.None
            with
            | Ok job ->
                Assert.Equal(Types.JobStatus.Succeeded, job.Status)
                Assert.Equal(2, polls ())
            | Error err -> Assert.Fail($"Expected success but got error: %A{err}")

            Assert.Empty(cancelRequests ())
        }

    [<Fact>]
    let ``pollJobUntilCompleteAsync retries a transient status failure`` () : Task =
        task {
            let jobId = Guid.NewGuid().ToString()

            let httpClient, polls, cancelRequests =
                pollingService
                    jobId
                    (fun n ->
                        match n with
                        | 1 -> json HttpStatusCode.ServiceUnavailable "busy"
                        | 2 -> json HttpStatusCode.BadGateway "bad gateway"
                        | _ -> json HttpStatusCode.OK (jobJson jobId "Succeeded"))
                    accepted

            match!
                pollJobUntilCompleteAsync httpClient workspaceUrl jobId (TimeSpan.FromSeconds 30.0) CancellationToken.None
            with
            | Ok job ->
                Assert.Equal(Types.JobStatus.Succeeded, job.Status)
                Assert.Equal(3, polls ())
            | Error err -> Assert.Fail($"Expected success after the retries but got error: %A{err}")

            Assert.Empty(cancelRequests ())
        }

    [<Fact>]
    let ``pollJobUntilCompleteAsync retries a status request that got no response`` () : Task =
        task {
            let jobId = Guid.NewGuid().ToString()
            let polls = ref 0

            let httpClient =
                createMockHttpClient (fun _ ->
                    if Interlocked.Increment(&polls.contents) = 1 then
                        Task.FromException<HttpResponseMessage>(HttpRequestException "connection reset")
                    else
                        Task.FromResult(json HttpStatusCode.OK (jobJson jobId "Succeeded")))

            match!
                pollJobUntilCompleteAsync httpClient workspaceUrl jobId (TimeSpan.FromSeconds 30.0) CancellationToken.None
            with
            | Ok job -> Assert.Equal(Types.JobStatus.Succeeded, job.Status)
            | Error err -> Assert.Fail($"Expected success after the retry but got error: %A{err}")

            Assert.Equal(2, polls.Value)
        }

    [<Fact>]
    let ``pollJobUntilCompleteAsync gives up after bounded retries, cancels the job and names it`` () : Task =
        task {
            let jobId = Guid.NewGuid().ToString()

            let httpClient, polls, cancelRequests =
                pollingService jobId (fun _ -> json HttpStatusCode.ServiceUnavailable "busy") accepted

            match!
                pollJobUntilCompleteAsync httpClient workspaceUrl jobId (TimeSpan.FromSeconds 30.0) CancellationToken.None
            with
            | Error(QuantumError.OperationError("Job polling", message)) ->
                Assert.Contains(jobId, message)
                Assert.Contains("cancellation request was accepted", message)
            | other -> Assert.Fail($"expected a polling error, got %A{other}")

            Assert.Equal(Retry.defaultConfig.MaxAttempts, polls ())

            let cancel = cancelRequests () |> List.exactlyOne
            Assert.Equal("POST", cancel.Method)
            Assert.Equal($"{workspaceUrl}/jobs/{jobId}:cancel?api-version={ApiVersion}", cancel.Url)
        }

    [<Fact>]
    let ``pollJobUntilCompleteAsync does not retry a status failure that is not transient`` () : Task =
        task {
            let jobId = Guid.NewGuid().ToString()

            let httpClient, polls, cancelRequests =
                pollingService jobId (fun _ -> json HttpStatusCode.BadRequest "bad request") accepted

            match!
                pollJobUntilCompleteAsync httpClient workspaceUrl jobId (TimeSpan.FromSeconds 30.0) CancellationToken.None
            with
            | Error(QuantumError.OperationError("Job polling", message)) -> Assert.Contains(jobId, message)
            | other -> Assert.Fail($"expected a polling error, got %A{other}")

            Assert.Equal(1, polls ())
            Assert.Single(cancelRequests ()) |> ignore
        }

    [<Fact>]
    let ``pollJobUntilCompleteAsync should respect cancellation token`` () : Task =
        task {
            // Arrange: Mock that always returns Executing
            let jobId = Guid.NewGuid().ToString()

            let httpClient, _, cancelRequests =
                pollingService jobId (fun _ -> json HttpStatusCode.OK (jobJson jobId "Executing")) accepted

            use cts = new CancellationTokenSource()

            // Cancel immediately
            do! cts.CancelAsync()

            // Assert: Should return Cancelled error that names the job, after asking the
            // service to cancel it (the caller's token is cancelled; the request must not use it)
            match! pollJobUntilCompleteAsync httpClient workspaceUrl jobId (TimeSpan.FromSeconds 30.0) cts.Token with
            | Ok _ -> Assert.True(false, "Expected cancellation error")
            | Error err ->
                match err with
                | QuantumError.OperationError(_, context) when context.Contains "cancelled" ->
                    Assert.Contains(jobId, context)
                    Assert.Contains("cancellation request was accepted", context)
                | _ -> Assert.True(false, $"Expected Cancelled error but got: %A{err}")

            Assert.Single(cancelRequests ()) |> ignore
        }

    [<Fact>]
    let ``pollJobUntilCompleteAsync should timeout after max duration`` () : Task =
        task {
            // Arrange: Mock that always returns Executing
            let jobId = Guid.NewGuid().ToString()

            let httpClient, _, cancelRequests =
                pollingService jobId (fun _ -> json HttpStatusCode.OK (jobJson jobId "Executing")) accepted

            use cts = new CancellationTokenSource()

            // Assert: a timeout error that names the job; the job's cancellation was requested
            match!
                pollJobUntilCompleteAsync httpClient workspaceUrl jobId (TimeSpan.FromMilliseconds 100.0) cts.Token
            with
            | Ok _ -> Assert.True(false, "Expected timeout error")
            | Error(QuantumError.AzureError(AzureQuantumError.JobTimeout message)) ->
                Assert.Contains(jobId, message)
                Assert.Contains("cancellation request was accepted", message)
            | Error err -> Assert.True(false, $"Expected JobTimeout but got: %A{err}")

            Assert.Single(cancelRequests ()) |> ignore
        }

    [<Fact>]
    let ``A timeout whose cancellation is refused says the job may still run`` () : Task =
        task {
            let jobId = Guid.NewGuid().ToString()

            let httpClient, _, cancelRequests =
                pollingService
                    jobId
                    (fun _ -> json HttpStatusCode.OK (jobJson jobId "Executing"))
                    (fun () -> json HttpStatusCode.Conflict "cannot cancel")

            match!
                pollJobUntilCompleteAsync
                    httpClient
                    workspaceUrl
                    jobId
                    (TimeSpan.FromMilliseconds 100.0)
                    CancellationToken.None
            with
            | Error(QuantumError.AzureError(AzureQuantumError.JobTimeout message)) ->
                Assert.Contains(jobId, message)
                Assert.Contains("not accepted", message)
                Assert.Contains("may still run and bill", message)
            | other -> Assert.Fail($"expected JobTimeout, got %A{other}")

            Assert.Single(cancelRequests ()) |> ignore
        }

    // ============================================================================
    // Job Result Retrieval Tests
    // ============================================================================

    [<Fact>]
    let ``getJobResultAsync should download result from blob storage`` () : Task =
        task {
            // Arrange: Mock HTTP response with result JSON from blob storage
            let mockResponse _ =
                let jsonResponse = """{"histogram":{"00":512,"11":488},"shots":1000}"""

                let response =
                    new HttpResponseMessage(
                        HttpStatusCode.OK,
                        Content = (new StringContent(jsonResponse, Encoding.UTF8, "application/json"))
                    )

                Task.FromResult(response)

            let httpClient = createMockHttpClient mockResponse
            let blobUri = "https://storage.blob.core.windows.net/results/job-123.json"

            // Assert: Should return JobResult with data
            match! getJobResultAsync httpClient blobUri with
            | Ok jobResult ->
                Assert.NotNull(jobResult.OutputData)
                Assert.Equal("application/json", jobResult.OutputDataFormat)
            | Error err -> Assert.True(false, $"Expected success but got error: %A{err}")
        }

    [<Fact>]
    let ``getJobResultAsync should handle 404 blob not found`` () : Task =
        task {
            // Arrange: Mock HTTP response with 404
            let mockResponse _ =
                Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound))

            let httpClient = createMockHttpClient mockResponse

            let blobUri =
                $"https://storage.blob.core.windows.net/results/nonexistent.json?sv=2019-02-02&sig={sasSecret}"

            // Assert: Should return an error that names the blob but not its SAS token
            match! getJobResultAsync httpClient blobUri with
            | Ok _ -> Assert.True(false, "Expected error for 404")
            | Error err ->
                match err with
                | QuantumError.AzureError(AzureQuantumError.UnknownError(404, message)) ->
                    Assert.Contains("/results/nonexistent.json", message)
                    Assert.DoesNotContain("?", message)
                    Assert.DoesNotContain(sasSecret, message)
                | _ -> Assert.True(false, $"Expected 404 error but got: %A{err}")
        }

    // ============================================================================
    // Job Cancellation Tests
    // ============================================================================

    [<Fact>]
    let ``cancelJobAsync should cancel running job`` () : Task =
        task {
            let jobId = Guid.NewGuid().ToString()
            let httpClient, seen = mockService (fun _ -> None)

            match! cancelJobAsync httpClient workspaceUrl jobId with
            | Ok() -> Assert.True(true)
            | Error err -> Assert.True(false, $"Expected success but got error: %A{err}")

            // Jobs - Cancel: POST {workspace}/jobs/{id}:cancel
            let request = seen () |> List.exactlyOne
            Assert.Equal("POST", request.Method)
            Assert.Equal($"{workspaceUrl}/jobs/{jobId}:cancel?api-version={ApiVersion}", request.Url)
        }

    [<Fact>]
    let ``cancelJobAsync should handle 404 job not found`` () : Task =
        task {
            // Arrange: Mock HTTP response with 404
            let jobId = Guid.NewGuid().ToString()

            let mockResponse _ =
                Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound))

            let httpClient = createMockHttpClient mockResponse

            // Assert: Should return error
            match! cancelJobAsync httpClient workspaceUrl jobId with
            | Ok _ -> Assert.True(false, "Expected error for 404")
            | Error err ->
                match err with
                | QuantumError.AzureError(AzureQuantumError.UnknownError(404, _)) -> Assert.True(true)
                | _ -> Assert.True(false, $"Expected 404 error but got: %A{err}")
        }
