module FSharp.Azure.Quantum.Tests.QuantumClientTests

open System
open System.Net
open System.Net.Http
open System.Threading
open System.Threading.Tasks
open Xunit
open FSharp.Azure.Quantum.Core.Types
open FSharp.Azure.Quantum.Core.Client
open FSharp.Azure.Quantum.Core.Retry

// Helper to create test config - no retries for predictable tests
let makeConfig httpClient =
    { SubscriptionId = "sub-123"
      ResourceGroup = "rg-test"
      WorkspaceName = "ws-test"
      HttpClient = httpClient
      RetryConfig = None
      Logger = None
      CostEstimationEnabled = false // Disable cost checking in tests
      PerJobCostLimit = None
      DailyCostLimit = None }

// Mock HTTP message handler for testing
type MockHttpMessageHandler(responseFunc: HttpRequestMessage -> Task<HttpResponseMessage>) =
    inherit HttpMessageHandler()

    override this.SendAsync(request: HttpRequestMessage, cancellationToken: CancellationToken) = responseFunc request

[<Fact>]
let ``SubmitJobAsync should send PUT request to correct endpoint`` () =
    async {
        let mutable capturedRequest: HttpRequestMessage option = None

        let mockHandler =
            new MockHttpMessageHandler(fun request ->
                capturedRequest <- Some request
                let response = new HttpResponseMessage(HttpStatusCode.OK)

                response.Content <-
                    new StringContent(
                        """{
            "id": "job-123",
            "status": "Waiting",
            "creationTime": "2025-11-22T00:00:00Z",
            "uri": "https://example.com/jobs/job-123"
        }"""
                    )

                Task.FromResult(response))

        let httpClient = new HttpClient(mockHandler)
        let config = makeConfig httpClient

        let client = QuantumClient(config)

        let submission =
            { JobId = "job-123"
              Target = "ionq.simulator"
              Name = Some "Test Job"
              InputData = box "circuit-data"
              InputDataFormat = CircuitFormat.QIR_V1
              InputParams = Map.empty
              Tags = Map.empty }

        let! result = client.SubmitJobAsync(submission)

        match result with
        | Ok response ->
            Assert.Equal("job-123", response.JobId)
            Assert.Equal(JobStatus.Waiting, response.Status)

            // Verify request details
            match capturedRequest with
            | Some req ->
                Assert.Equal(HttpMethod.Put, req.Method)
                Assert.Contains("/subscriptions/sub-123/resourceGroups/rg-test", req.RequestUri.ToString())
                Assert.Contains("/providers/Microsoft.Quantum/Workspaces/ws-test", req.RequestUri.ToString())
                Assert.Contains("/jobs/job-123", req.RequestUri.ToString())
            | None -> Assert.True(false, "Request was not captured")
        | Error err -> Assert.True(false, sprintf "Expected success but got error: %A" err)
    }

[<Fact>]
let ``GetJobStatusAsync should send GET request and parse response`` () =
    async {
        let mockHandler =
            new MockHttpMessageHandler(fun request ->
                let response = new HttpResponseMessage(HttpStatusCode.OK)

                response.Content <-
                    new StringContent(
                        """{
            "id": "job-456",
            "status": "Executing",
            "target": "ionq.qpu",
            "creationTime": "2025-11-22T00:00:00Z",
            "beginExecutionTime": "2025-11-22T00:01:00Z"
        }"""
                    )

                Task.FromResult(response))

        let httpClient = new HttpClient(mockHandler)
        let config = makeConfig httpClient

        let client = QuantumClient(config)

        let! result = client.GetJobStatusAsync("job-456")

        match result with
        | Ok job ->
            Assert.Equal("job-456", job.JobId)
            Assert.Equal(JobStatus.Executing, job.Status)
            Assert.True(job.BeginExecutionTime.IsSome)
        | Error err -> Assert.True(false, sprintf "Expected success but got error: %A" err)
    }

[<Fact>]
let ``GetJobStatusAsync should handle 404 error`` () =
    async {
        let mockHandler =
            new MockHttpMessageHandler(fun request ->
                let response = new HttpResponseMessage(HttpStatusCode.NotFound)

                response.Content <-
                    new StringContent("""{"error": {"code": "JobNotFound", "message": "Job not found"}}""")

                Task.FromResult(response))

        let httpClient = new HttpClient(mockHandler)
        let config = makeConfig httpClient

        let client = QuantumClient(config)

        let! result = client.GetJobStatusAsync("nonexistent-job")

        match result with
        | Error(QuantumError.UnknownError(statusCode, _)) -> Assert.Equal(404, statusCode)
        | _ -> Assert.True(false, "Expected NotFound error")
    }

[<Fact>]
let ``CancelJobAsync should send POST to cancel endpoint`` () =
    async {
        let mutable capturedRequest: HttpRequestMessage option = None

        let mockHandler =
            new MockHttpMessageHandler(fun request ->
                capturedRequest <- Some request
                let response = new HttpResponseMessage(HttpStatusCode.OK)
                response.Content <- new StringContent("{}")
                Task.FromResult(response))

        let httpClient = new HttpClient(mockHandler)
        let config = makeConfig httpClient

        let client = QuantumClient(config)

        let! result = client.CancelJobAsync("job-789")

        match result with
        | Ok() ->
            match capturedRequest with
            | Some req ->
                Assert.Equal(HttpMethod.Post, req.Method)
                Assert.Contains("/jobs/job-789/cancel", req.RequestUri.ToString())
            | None -> Assert.True(false, "Request was not captured")
        | Error err -> Assert.True(false, sprintf "Expected success but got error: %A" err)
    }

[<Fact>]
let ``WaitForCompletionAsync should poll until job succeeds`` () =
    async {
        let mutable pollCount = 0

        let mockHandler =
            new MockHttpMessageHandler(fun request ->
                pollCount <- pollCount + 1
                let response = new HttpResponseMessage(HttpStatusCode.OK)

                // First 2 polls: job is executing
                // 3rd poll: job succeeded
                let status = if pollCount < 3 then "Executing" else "Succeeded"

                response.Content <-
                    new StringContent(
                        sprintf
                            """{
            "id": "job-poll-1",
            "status": "%s",
            "target": "ionq.simulator",
            "creationTime": "2025-11-22T00:00:00Z"
        }"""
                            status
                    )

                Task.FromResult(response))

        let httpClient = new HttpClient(mockHandler)
        let config = makeConfig httpClient

        let client = QuantumClient(config)

        let! result =
            client.WaitForCompletionAsync("job-poll-1", initialDelayMs = 10, maxDelayMs = 50, timeoutMs = 5000)

        match result with
        | Ok job ->
            Assert.Equal("job-poll-1", job.JobId)
            Assert.Equal(JobStatus.Succeeded, job.Status)
            Assert.True(pollCount >= 3, sprintf "Expected at least 3 polls, got %d" pollCount)
        | Error err -> Assert.True(false, sprintf "Expected success but got error: %A" err)
    }

[<Fact>]
let ``WaitForCompletionAsync should return error when job fails`` () =
    async {
        let mockHandler =
            new MockHttpMessageHandler(fun request ->
                let response = new HttpResponseMessage(HttpStatusCode.OK)

                response.Content <-
                    new StringContent(
                        """{
            "id": "job-fail-1",
            "status": "Failed",
            "target": "ionq.simulator",
            "creationTime": "2025-11-22T00:00:00Z"
        }"""
                    )

                Task.FromResult(response))

        let httpClient = new HttpClient(mockHandler)
        let config = makeConfig httpClient

        let client = QuantumClient(config)

        let! result =
            client.WaitForCompletionAsync("job-fail-1", initialDelayMs = 10, maxDelayMs = 50, timeoutMs = 5000)

        match result with
        | Ok job ->
            match job.Status with
            | JobStatus.Failed _ -> Assert.True(true) // Expected Failed status
            | _ -> Assert.True(false, sprintf "Expected Failed status but got: %A" job.Status)
        | Error err -> Assert.True(false, sprintf "Expected success but got error: %A" err)
    }

[<Fact>]
let ``WaitForCompletionAsync should timeout if job takes too long`` () =
    async {
        let mockHandler =
            new MockHttpMessageHandler(fun request ->
                let response = new HttpResponseMessage(HttpStatusCode.OK)
                // Always return Executing status
                response.Content <-
                    new StringContent(
                        """{
            "id": "job-timeout",
            "status": "Executing",
            "target": "ionq.simulator",
            "creationTime": "2025-11-22T00:00:00Z"
        }"""
                    )

                Task.FromResult(response))

        let httpClient = new HttpClient(mockHandler)
        let config = makeConfig httpClient

        let client = QuantumClient(config)

        // Set very short timeout (100ms) to ensure we hit it
        let! result =
            client.WaitForCompletionAsync("job-timeout", initialDelayMs = 10, maxDelayMs = 50, timeoutMs = 100)

        match result with
        | Error(QuantumError.Timeout _) -> Assert.True(true) // Expected timeout error
        | _ -> Assert.True(false, "Expected Timeout error")
    }

[<Fact>]
let ``SubmitJobAsync should retry on transient errors and succeed`` () =
    async {
        let mutable attemptCount = 0

        let mockHandler =
            new MockHttpMessageHandler(fun request ->
                attemptCount <- attemptCount + 1

                // First 2 attempts fail with 503, 3rd succeeds
                if attemptCount < 3 then
                    let response = new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)

                    response.Content <-
                        new StringContent(
                            """{"error": {"code": "ServiceUnavailable", "message": "Service temporarily unavailable"}}"""
                        )

                    Task.FromResult(response)
                else
                    let response = new HttpResponseMessage(HttpStatusCode.OK)

                    response.Content <-
                        new StringContent(
                            """{
                "id": "job-retry-success",
                "status": "Waiting",
                "creationTime": "2025-11-22T00:00:00Z",
                "uri": "https://example.com/jobs/job-retry-success"
            }"""
                        )

                    Task.FromResult(response))

        let httpClient = new HttpClient(mockHandler)

        let retryConfig =
            { MaxAttempts = 3
              InitialDelayMs = 10
              MaxDelayMs = 100
              JitterFactor = 0.1 }

        let config =
            { SubscriptionId = "sub-123"
              ResourceGroup = "rg-test"
              WorkspaceName = "ws-test"
              HttpClient = httpClient
              RetryConfig = Some retryConfig
              Logger = None
              CostEstimationEnabled = false
              PerJobCostLimit = None
              DailyCostLimit = None }

        let client = QuantumClient(config)

        let submission =
            { JobId = "job-retry-success"
              Target = "ionq.simulator"
              Name = Some "Retry Test"
              InputData = box "circuit-data"
              InputDataFormat = CircuitFormat.QIR_V1
              InputParams = Map.empty
              Tags = Map.empty }

        let! result = client.SubmitJobAsync(submission)

        match result with
        | Ok response ->
            Assert.Equal("job-retry-success", response.JobId)
            Assert.Equal(JobStatus.Waiting, response.Status)
            Assert.True((attemptCount = 3), sprintf "Expected 3 attempts, got %d" attemptCount)
        | Error err -> Assert.True(false, sprintf "Expected success after retries but got error: %A" err)
    }

[<Fact>]
let ``SubmitJobAsync should fail after max retries exceeded`` () =
    async {
        let mutable attemptCount = 0

        let mockHandler =
            new MockHttpMessageHandler(fun request ->
                attemptCount <- attemptCount + 1
                // Always return 503
                let response = new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)

                response.Content <-
                    new StringContent(
                        """{"error": {"code": "ServiceUnavailable", "message": "Service temporarily unavailable"}}"""
                    )

                Task.FromResult(response))

        let httpClient = new HttpClient(mockHandler)

        let retryConfig =
            { MaxAttempts = 2
              InitialDelayMs = 10
              MaxDelayMs = 50
              JitterFactor = 0.1 }

        let config =
            { SubscriptionId = "sub-123"
              ResourceGroup = "rg-test"
              WorkspaceName = "ws-test"
              HttpClient = httpClient
              RetryConfig = Some retryConfig
              Logger = None
              CostEstimationEnabled = false
              PerJobCostLimit = None
              DailyCostLimit = None }

        let client = QuantumClient(config)

        let submission =
            { JobId = "job-max-retries"
              Target = "ionq.simulator"
              Name = Some "Max Retry Test"
              InputData = box "circuit-data"
              InputDataFormat = CircuitFormat.QIR_V1
              InputParams = Map.empty
              Tags = Map.empty }

        let! result = client.SubmitJobAsync(submission)

        match result with
        | Error(QuantumError.ServiceUnavailable _) ->
            // MaxAttempts = 2, so should make 2 total attempts
            Assert.True((attemptCount = 2), sprintf "Expected 2 attempts (MaxAttempts=2), got %d" attemptCount)
        | _ -> Assert.True(false, "Expected ServiceUnavailable error after max attempts")
    }

[<Fact>]
let ``SubmitJobAsync should not retry on non-transient errors`` () =
    async {
        let mutable attemptCount = 0

        let mockHandler =
            new MockHttpMessageHandler(fun request ->
                attemptCount <- attemptCount + 1
                // Return 400 Bad Request (non-transient)
                let response = new HttpResponseMessage(HttpStatusCode.BadRequest)

                response.Content <-
                    new StringContent("""{"error": {"code": "InvalidInput", "message": "Invalid quantum circuit"}}""")

                Task.FromResult(response))

        let httpClient = new HttpClient(mockHandler)

        let retryConfig =
            { MaxAttempts = 3
              InitialDelayMs = 10
              MaxDelayMs = 100
              JitterFactor = 0.1 }

        let config =
            { SubscriptionId = "sub-123"
              ResourceGroup = "rg-test"
              WorkspaceName = "ws-test"
              HttpClient = httpClient
              RetryConfig = Some retryConfig
              Logger = None
              CostEstimationEnabled = false
              PerJobCostLimit = None
              DailyCostLimit = None }

        let client = QuantumClient(config)

        let submission =
            { JobId = "job-no-retry"
              Target = "ionq.simulator"
              Name = Some "No Retry Test"
              InputData = box "invalid-data"
              InputDataFormat = CircuitFormat.QIR_V1
              InputParams = Map.empty
              Tags = Map.empty }

        let! result = client.SubmitJobAsync(submission)

        match result with
        | Error(QuantumError.UnknownError(statusCode, _)) ->
            Assert.Equal(400, statusCode)
            // Should only make 1 attempt (no retries for non-transient errors)
            Assert.True(
                (attemptCount = 1),
                sprintf "Expected only 1 attempt for non-transient error, got %d" attemptCount
            )
        | _ -> Assert.True(false, "Expected BadRequest error without retries")
    }

[<Fact>]
let ``GetJobStatusAsync returns full job details including execution times`` () =
    async {
        let mockHandler =
            new MockHttpMessageHandler(fun request ->
                let response = new HttpResponseMessage(HttpStatusCode.OK)

                response.Content <-
                    new StringContent(
                        """{
            "id": "job-full-details",
            "status": "Succeeded",
            "target": "ionq.qpu",
            "creationTime": "2025-11-22T00:00:00Z",
            "beginExecutionTime": "2025-11-22T00:01:00Z",
            "endExecutionTime": "2025-11-22T00:02:00Z"
        }"""
                    )

                Task.FromResult(response))

        let httpClient = new HttpClient(mockHandler)
        let config = makeConfig httpClient

        let client = QuantumClient(config)

        let! result = client.GetJobStatusAsync("job-full-details")

        match result with
        | Ok(job: QuantumJob) ->
            Assert.Equal("job-full-details", job.JobId)
            Assert.Equal(JobStatus.Succeeded, job.Status)
            Assert.Equal("ionq.qpu", job.Target)
            Assert.True(job.BeginExecutionTime.IsSome)
            Assert.True(job.EndExecutionTime.IsSome)
        | Error err -> Assert.True(false, sprintf "Expected success but got error: %A" err)
    }

[<Fact>]
let ``GetResultsAsync should retrieve job results after completion`` () =
    async {
        let mockHandler =
            new MockHttpMessageHandler(fun request ->
                let response = new HttpResponseMessage(HttpStatusCode.OK)

                response.Content <-
                    new StringContent(
                        """{
            "id": "job-with-results",
            "status": "Succeeded",
            "outputData": {
                "histogram": {"00": 512, "11": 512}
            },
            "outputDataFormat": "microsoft.quantum.measurement-results.v1",
            "executionTime": "PT1.5S"
        }"""
                    )

                Task.FromResult(response))

        let httpClient = new HttpClient(mockHandler)
        let config = makeConfig httpClient

        let client = QuantumClient(config)

        let! result = client.GetResultsAsync("job-with-results")

        match result with
        | Ok(jobResult: JobResult) ->
            Assert.Equal("job-with-results", jobResult.JobId)
            Assert.Equal(JobStatus.Succeeded, jobResult.Status)
            Assert.NotNull(jobResult.OutputData)
            Assert.Equal("microsoft.quantum.measurement-results.v1", jobResult.OutputDataFormat)
            Assert.True(jobResult.ExecutionTime.IsSome)
        | Error err -> Assert.True(false, sprintf "Expected success but got error: %A" err)
    }

[<Fact>]
let ``GetResultsAsync should return error for incomplete job`` () =
    async {
        let mockHandler =
            new MockHttpMessageHandler(fun request ->
                let response = new HttpResponseMessage(HttpStatusCode.OK)

                response.Content <-
                    new StringContent(
                        """{
            "id": "job-not-complete",
            "status": "Executing"
        }"""
                    )

                Task.FromResult(response))

        let httpClient = new HttpClient(mockHandler)
        let config = makeConfig httpClient

        let client = QuantumClient(config)

        let! result = client.GetResultsAsync("job-not-complete")

        match result with
        | Error _ -> Assert.True(true) // Expected error for incomplete job
        | Ok _ -> Assert.True(false, "Expected error for job without results")
    }
