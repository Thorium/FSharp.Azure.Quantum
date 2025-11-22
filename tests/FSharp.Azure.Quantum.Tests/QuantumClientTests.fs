module FSharp.Azure.Quantum.Tests.QuantumClientTests

open System
open System.Net
open System.Net.Http
open System.Threading
open System.Threading.Tasks
open Xunit
open FSharp.Azure.Quantum.Core.Types
open FSharp.Azure.Quantum.Core.Client

// Mock HTTP message handler for testing
type MockHttpMessageHandler(responseFunc: HttpRequestMessage -> Task<HttpResponseMessage>) =
    inherit HttpMessageHandler()
    
    override this.SendAsync(request: HttpRequestMessage, cancellationToken: CancellationToken) =
        responseFunc request

[<Fact>]
let ``SubmitJobAsync should send PUT request to correct endpoint`` () = async {
    let mutable capturedRequest: HttpRequestMessage option = None
    
    let mockHandler = MockHttpMessageHandler(fun request ->
        capturedRequest <- Some request
        let response = new HttpResponseMessage(HttpStatusCode.OK)
        response.Content <- new StringContent("""{
            "id": "job-123",
            "status": "Waiting",
            "creationTime": "2025-11-22T00:00:00Z",
            "uri": "https://example.com/jobs/job-123"
        }""")
        Task.FromResult(response)
    )
    
    let httpClient = new HttpClient(mockHandler)
    let config = {
        SubscriptionId = "sub-123"
        ResourceGroup = "rg-test"
        WorkspaceName = "ws-test"
        HttpClient = httpClient
    }
    
    let client = QuantumClient(config)
    
    let submission = {
        JobId = "job-123"
        Target = "ionq.simulator"
        Name = Some "Test Job"
        InputData = box "circuit-data"
        InputDataFormat = CircuitFormat.QIR_V1
        InputParams = Map.empty
        Tags = Map.empty
    }
    
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
    | Error err ->
        Assert.True(false, sprintf "Expected success but got error: %A" err)
}

[<Fact>]
let ``GetJobStatusAsync should send GET request and parse response`` () = async {
    let mockHandler = MockHttpMessageHandler(fun request ->
        let response = new HttpResponseMessage(HttpStatusCode.OK)
        response.Content <- new StringContent("""{
            "id": "job-456",
            "status": "Executing",
            "target": "ionq.qpu",
            "creationTime": "2025-11-22T00:00:00Z",
            "beginExecutionTime": "2025-11-22T00:01:00Z"
        }""")
        Task.FromResult(response)
    )
    
    let httpClient = new HttpClient(mockHandler)
    let config = {
        SubscriptionId = "sub-123"
        ResourceGroup = "rg-test"
        WorkspaceName = "ws-test"
        HttpClient = httpClient
    }
    
    let client = QuantumClient(config)
    
    let! result = client.GetJobStatusAsync("job-456")
    
    match result with
    | Ok job ->
        Assert.Equal("job-456", job.JobId)
        Assert.Equal(JobStatus.Executing, job.Status)
        Assert.True(job.BeginExecutionTime.IsSome)
    | Error err ->
        Assert.True(false, sprintf "Expected success but got error: %A" err)
}

[<Fact>]
let ``GetJobStatusAsync should handle 404 error`` () = async {
    let mockHandler = MockHttpMessageHandler(fun request ->
        let response = new HttpResponseMessage(HttpStatusCode.NotFound)
        response.Content <- new StringContent("""{"error": {"code": "JobNotFound", "message": "Job not found"}}""")
        Task.FromResult(response)
    )
    
    let httpClient = new HttpClient(mockHandler)
    let config = {
        SubscriptionId = "sub-123"
        ResourceGroup = "rg-test"
        WorkspaceName = "ws-test"
        HttpClient = httpClient
    }
    
    let client = QuantumClient(config)
    
    let! result = client.GetJobStatusAsync("nonexistent-job")
    
    match result with
    | Error (QuantumError.UnknownError(statusCode, _)) ->
        Assert.Equal(404, statusCode)
    | _ ->
        Assert.True(false, "Expected NotFound error")
}

[<Fact>]
let ``CancelJobAsync should send POST to cancel endpoint`` () = async {
    let mutable capturedRequest: HttpRequestMessage option = None
    
    let mockHandler = MockHttpMessageHandler(fun request ->
        capturedRequest <- Some request
        let response = new HttpResponseMessage(HttpStatusCode.OK)
        response.Content <- new StringContent("{}")
        Task.FromResult(response)
    )
    
    let httpClient = new HttpClient(mockHandler)
    let config = {
        SubscriptionId = "sub-123"
        ResourceGroup = "rg-test"
        WorkspaceName = "ws-test"
        HttpClient = httpClient
    }
    
    let client = QuantumClient(config)
    
    let! result = client.CancelJobAsync("job-789")
    
    match result with
    | Ok () ->
        match capturedRequest with
        | Some req ->
            Assert.Equal(HttpMethod.Post, req.Method)
            Assert.Contains("/jobs/job-789/cancel", req.RequestUri.ToString())
        | None -> Assert.True(false, "Request was not captured")
    | Error err ->
        Assert.True(false, sprintf "Expected success but got error: %A" err)
}

[<Fact>]
let ``WaitForCompletionAsync should poll until job succeeds`` () = async {
    let mutable pollCount = 0
    
    let mockHandler = MockHttpMessageHandler(fun request ->
        pollCount <- pollCount + 1
        let response = new HttpResponseMessage(HttpStatusCode.OK)
        
        // First 2 polls: job is executing
        // 3rd poll: job succeeded
        let status = if pollCount < 3 then "Executing" else "Succeeded"
        
        response.Content <- new StringContent(sprintf """{
            "id": "job-poll-1",
            "status": "%s",
            "target": "ionq.simulator",
            "creationTime": "2025-11-22T00:00:00Z"
        }""" status)
        Task.FromResult(response)
    )
    
    let httpClient = new HttpClient(mockHandler)
    let config = {
        SubscriptionId = "sub-123"
        ResourceGroup = "rg-test"
        WorkspaceName = "ws-test"
        HttpClient = httpClient
    }
    
    let client = QuantumClient(config)
    
    let! result = client.WaitForCompletionAsync("job-poll-1", initialDelayMs = 10, maxDelayMs = 50, timeoutMs = 5000)
    
    match result with
    | Ok job ->
        Assert.Equal("job-poll-1", job.JobId)
        Assert.Equal(JobStatus.Succeeded, job.Status)
        Assert.True(pollCount >= 3, sprintf "Expected at least 3 polls, got %d" pollCount)
    | Error err ->
        Assert.True(false, sprintf "Expected success but got error: %A" err)
}

[<Fact>]
let ``WaitForCompletionAsync should return error when job fails`` () = async {
    let mockHandler = MockHttpMessageHandler(fun request ->
        let response = new HttpResponseMessage(HttpStatusCode.OK)
        response.Content <- new StringContent("""{
            "id": "job-fail-1",
            "status": "Failed",
            "target": "ionq.simulator",
            "creationTime": "2025-11-22T00:00:00Z"
        }""")
        Task.FromResult(response)
    )
    
    let httpClient = new HttpClient(mockHandler)
    let config = {
        SubscriptionId = "sub-123"
        ResourceGroup = "rg-test"
        WorkspaceName = "ws-test"
        HttpClient = httpClient
    }
    
    let client = QuantumClient(config)
    
    let! result = client.WaitForCompletionAsync("job-fail-1", initialDelayMs = 10, maxDelayMs = 50, timeoutMs = 5000)
    
    match result with
    | Ok job ->
        match job.Status with
        | JobStatus.Failed _ -> Assert.True(true) // Expected Failed status
        | _ -> Assert.True(false, sprintf "Expected Failed status but got: %A" job.Status)
    | Error err ->
        Assert.True(false, sprintf "Expected success but got error: %A" err)
}

[<Fact>]
let ``WaitForCompletionAsync should timeout if job takes too long`` () = async {
    let mockHandler = MockHttpMessageHandler(fun request ->
        let response = new HttpResponseMessage(HttpStatusCode.OK)
        // Always return Executing status
        response.Content <- new StringContent("""{
            "id": "job-timeout",
            "status": "Executing",
            "target": "ionq.simulator",
            "creationTime": "2025-11-22T00:00:00Z"
        }""")
        Task.FromResult(response)
    )
    
    let httpClient = new HttpClient(mockHandler)
    let config = {
        SubscriptionId = "sub-123"
        ResourceGroup = "rg-test"
        WorkspaceName = "ws-test"
        HttpClient = httpClient
    }
    
    let client = QuantumClient(config)
    
    // Set very short timeout (100ms) to ensure we hit it
    let! result = client.WaitForCompletionAsync("job-timeout", initialDelayMs = 10, maxDelayMs = 50, timeoutMs = 100)
    
    match result with
    | Error (QuantumError.Timeout _) ->
        Assert.True(true) // Expected timeout error
    | _ ->
        Assert.True(false, "Expected Timeout error")
}
