namespace FSharp.Azure.Quantum.Tests

open Xunit
open FSharp.Azure.Quantum.Core
open FSharp.Azure.Quantum.Core.Types
open FSharp.Azure.Quantum.Core.JobLifecycle
open System
open System.Net
open System.Net.Http
open System.Text
open System.Threading
open System.Threading.Tasks

module JobLifecycleTests =
    
    // ============================================================================
    // Mock HTTP Message Handler
    // ============================================================================
    
    /// Mock HTTP message handler for testing
    type MockHttpMessageHandler(responseFunc: HttpRequestMessage -> Task<HttpResponseMessage>) =
        inherit HttpMessageHandler()
        
        override _.SendAsync(request: HttpRequestMessage, cancellationToken: CancellationToken) =
            responseFunc request
    
    /// Create HttpClient with mock handler
    let createMockHttpClient (responseFunc: HttpRequestMessage -> Task<HttpResponseMessage>) =
        let handler = new MockHttpMessageHandler(responseFunc)
        new HttpClient(handler)
    
    // ============================================================================
    // Job Submission Tests
    // ============================================================================
    
    [<Fact>]
    let ``submitJobAsync should return job ID on successful submission`` () =
        // Arrange: Mock HTTP client that returns 201 Created
        let jobId = Guid.NewGuid().ToString()
        let mockResponse _ = 
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.Created))
        
        let httpClient = createMockHttpClient mockResponse
        let workspaceUrl = "https://test.quantum.azure.com"
        
        let submission = {
            JobId = jobId
            Target = "ionq.simulator"
            Name = Some "Test Job"
            InputData = box {| qubits = 2; circuit = [||] |}
            InputDataFormat = CircuitFormat.IonQ_V1
            InputParams = Map.ofList [("shots", box 1000)]
            Tags = Map.empty
        }
        
        // Act: Submit job
        let result = submitJobAsync httpClient workspaceUrl submission |> Async.RunSynchronously
        
        // Assert: Should succeed
        match result with
        | Ok returnedJobId -> Assert.Equal(jobId, returnedJobId)
        | Error err -> Assert.True(false, sprintf "Expected success but got error: %A" err)
    
    [<Fact>]
    let ``submitJobAsync should return InvalidCredentials on 401 response`` () =
        // Arrange: Mock HTTP client that returns 401 Unauthorized
        let jobId = Guid.NewGuid().ToString()
        let mockResponse _ = 
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized))
        
        let httpClient = createMockHttpClient mockResponse
        let workspaceUrl = "https://test.quantum.azure.com"
        
        let submission = {
            JobId = jobId
            Target = "ionq.simulator"
            Name = Some "Test Job"
            InputData = box {| qubits = 2; circuit = [||] |}
            InputDataFormat = CircuitFormat.IonQ_V1
            InputParams = Map.ofList [("shots", box 1000)]
            Tags = Map.empty
        }
        
        // Act: Submit job with invalid credentials
        let result = submitJobAsync httpClient workspaceUrl submission |> Async.RunSynchronously
        
        // Assert: Should return InvalidCredentials error
        match result with
        | Ok _ -> Assert.True(false, "Expected InvalidCredentials error")
        | Error QuantumError.InvalidCredentials -> Assert.True(true)
        | Error other -> Assert.True(false, sprintf "Expected InvalidCredentials but got: %A" other)
    
    // ============================================================================
    // Job Status Tests
    // ============================================================================
    
    [<Fact>]
    let ``getJobStatusAsync should return job status for successful request`` () =
        // Arrange: Mock HTTP response with job status JSON
        let jobId = Guid.NewGuid().ToString()
        let mockResponse _ = 
            let jsonResponse = 
                $"""{{
                    "id": "{jobId}",
                    "status": "Executing",
                    "target": "ionq.simulator",
                    "creationTime": "2025-01-01T10:00:00Z",
                    "beginExecutionTime": "2025-01-01T10:00:05Z"
                }}"""
            let response = new HttpResponseMessage(HttpStatusCode.OK)
            response.Content <- new StringContent(jsonResponse, Encoding.UTF8, "application/json")
            Task.FromResult(response)
        
        let httpClient = createMockHttpClient mockResponse
        let workspaceUrl = "https://test.quantum.azure.com"
        
        // Act: Get job status
        let result = getJobStatusAsync httpClient workspaceUrl jobId |> Async.RunSynchronously
        
        // Assert: Should return QuantumJob with Executing status
        match result with
        | Ok job -> 
            Assert.Equal(jobId, job.JobId)
            Assert.Equal(Types.JobStatus.Executing, job.Status)
            Assert.Equal("ionq.simulator", job.Target)
        | Error err -> Assert.True(false, sprintf "Expected success but got error: %A" err)
    
    [<Fact>]
    let ``getJobStatusAsync should parse Succeeded status correctly`` () =
        // Arrange: Mock HTTP response with Succeeded status
        let jobId = Guid.NewGuid().ToString()
        let mockResponse _ = 
            let jsonResponse = 
                $"""{{
                    "id": "{jobId}",
                    "status": "Succeeded",
                    "target": "ionq.simulator",
                    "creationTime": "2025-01-01T10:00:00Z",
                    "beginExecutionTime": "2025-01-01T10:00:05Z",
                    "endExecutionTime": "2025-01-01T10:00:10Z"
                }}"""
            let response = new HttpResponseMessage(HttpStatusCode.OK)
            response.Content <- new StringContent(jsonResponse, Encoding.UTF8, "application/json")
            Task.FromResult(response)
        
        let httpClient = createMockHttpClient mockResponse
        let workspaceUrl = "https://test.quantum.azure.com"
        
        // Act: Get job status
        let result = getJobStatusAsync httpClient workspaceUrl jobId |> Async.RunSynchronously
        
        // Assert: Should return QuantumJob with Succeeded status
        match result with
        | Ok job -> 
            Assert.Equal(Types.JobStatus.Succeeded, job.Status)
            Assert.True(job.EndExecutionTime.IsSome)
        | Error err -> Assert.True(false, sprintf "Expected success but got error: %A" err)
    
    [<Fact>]
    let ``getJobStatusAsync should handle Failed status with error details`` () =
        // Arrange: Mock HTTP response with Failed status
        let jobId = Guid.NewGuid().ToString()
        let mockResponse _ = 
            let jsonResponse = 
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
            let response = new HttpResponseMessage(HttpStatusCode.OK)
            response.Content <- new StringContent(jsonResponse, Encoding.UTF8, "application/json")
            Task.FromResult(response)
        
        let httpClient = createMockHttpClient mockResponse
        let workspaceUrl = "https://test.quantum.azure.com"
        
        // Act: Get job status
        let result = getJobStatusAsync httpClient workspaceUrl jobId |> Async.RunSynchronously
        
        // Assert: Should return QuantumJob with Failed status and error details
        match result with
        | Ok job -> 
            match job.Status with
            | Types.JobStatus.Failed (code, message) ->
                Assert.Equal("InvalidCircuit", code)
                Assert.Contains("invalid gates", message)
            | _ -> Assert.True(false, sprintf "Expected Failed status but got: %A" job.Status)
        | Error err -> Assert.True(false, sprintf "Expected success but got error: %A" err)
    
    // ============================================================================
    // Polling Tests
    // ============================================================================
    
    [<Fact>]
    let ``pollJobUntilCompleteAsync should poll until job succeeds`` () =
        // Arrange: Mock that returns Executing twice, then Succeeded
        let jobId = Guid.NewGuid().ToString()
        let mutable callCount = 0
        
        let mockResponse _ =
            callCount <- callCount + 1
            let jsonResponse = 
                if callCount < 3 then
                    // First two calls: Executing
                    $"""{{
                        "id": "{jobId}",
                        "status": "Executing",
                        "target": "ionq.simulator",
                        "creationTime": "2025-01-01T10:00:00Z",
                        "beginExecutionTime": "2025-01-01T10:00:05Z"
                    }}"""
                else
                    // Third call: Succeeded
                    $"""{{
                        "id": "{jobId}",
                        "status": "Succeeded",
                        "target": "ionq.simulator",
                        "creationTime": "2025-01-01T10:00:00Z",
                        "beginExecutionTime": "2025-01-01T10:00:05Z",
                        "endExecutionTime": "2025-01-01T10:00:10Z"
                    }}"""
            
            let response = new HttpResponseMessage(HttpStatusCode.OK)
            response.Content <- new StringContent(jsonResponse, Encoding.UTF8, "application/json")
            Task.FromResult(response)
        
        let httpClient = createMockHttpClient mockResponse
        let workspaceUrl = "https://test.quantum.azure.com"
        let timeout = TimeSpan.FromSeconds(30.0)
        let cts = new CancellationTokenSource()
        
        // Act: Poll until complete
        let result = pollJobUntilCompleteAsync httpClient workspaceUrl jobId timeout cts.Token |> Async.RunSynchronously
        
        // Assert: Should eventually succeed after 3 calls
        match result with
        | Ok job -> 
            Assert.Equal(3, callCount)  // Should have polled 3 times
            Assert.Equal(Types.JobStatus.Succeeded, job.Status)
        | Error err -> Assert.True(false, sprintf "Expected success but got error: %A" err)
    
    [<Fact>]
    let ``pollJobUntilCompleteAsync should respect cancellation token`` () =
        // Arrange: Mock that always returns Executing
        let jobId = Guid.NewGuid().ToString()
        let mockResponse _ = 
            let jsonResponse = 
                $"""{{
                    "id": "{jobId}",
                    "status": "Executing",
                    "target": "ionq.simulator",
                    "creationTime": "2025-01-01T10:00:00Z"
                }}"""
            let response = new HttpResponseMessage(HttpStatusCode.OK)
            response.Content <- new StringContent(jsonResponse, Encoding.UTF8, "application/json")
            Task.FromResult(response)
        
        let httpClient = createMockHttpClient mockResponse
        let workspaceUrl = "https://test.quantum.azure.com"
        let timeout = TimeSpan.FromSeconds(30.0)
        let cts = new CancellationTokenSource()
        
        // Cancel immediately
        cts.Cancel()
        
        // Act: Poll with canceled token
        let result = pollJobUntilCompleteAsync httpClient workspaceUrl jobId timeout cts.Token |> Async.RunSynchronously
        
        // Assert: Should return Cancelled error
        match result with
        | Ok _ -> Assert.True(false, "Expected cancellation error")
        | Error err -> 
            match err with
            | Types.QuantumError.Cancelled -> Assert.True(true)
            | _ -> Assert.True(false, sprintf "Expected Cancelled error but got: %A" err)
    
    [<Fact>]
    let ``pollJobUntilCompleteAsync should timeout after max duration`` () =
        // Arrange: Mock that always returns Executing
        let jobId = Guid.NewGuid().ToString()
        let mockResponse _ = 
            let jsonResponse = 
                $"""{{
                    "id": "{jobId}",
                    "status": "Executing",
                    "target": "ionq.simulator",
                    "creationTime": "2025-01-01T10:00:00Z"
                }}"""
            let response = new HttpResponseMessage(HttpStatusCode.OK)
            response.Content <- new StringContent(jsonResponse, Encoding.UTF8, "application/json")
            Task.FromResult(response)
        
        let httpClient = createMockHttpClient mockResponse
        let workspaceUrl = "https://test.quantum.azure.com"
        let timeout = TimeSpan.FromMilliseconds(100.0)  // Very short timeout
        let cts = new CancellationTokenSource()
        
        // Act: Poll with short timeout
        let result = pollJobUntilCompleteAsync httpClient workspaceUrl jobId timeout cts.Token |> Async.RunSynchronously
        
        // Assert: Should return timeout error
        match result with
        | Ok _ -> Assert.True(false, "Expected timeout error")
        | Error err -> 
            match err with
            | Types.QuantumError.Cancelled -> Assert.True(true)  // Timeout manifests as cancellation
            | _ -> Assert.True(false, sprintf "Expected timeout/cancelled but got: %A" err)
    
    // ============================================================================
    // Job Result Retrieval Tests
    // ============================================================================
    
    [<Fact>]
    let ``getJobResultAsync should download result from blob storage`` () =
        // Arrange: Mock HTTP response with result JSON from blob storage
        let jobId = Guid.NewGuid().ToString()
        let mockResponse _ = 
            let jsonResponse = 
                """{"histogram":{"00":512,"11":488},"shots":1000}"""
            let response = new HttpResponseMessage(HttpStatusCode.OK)
            response.Content <- new StringContent(jsonResponse, Encoding.UTF8, "application/json")
            Task.FromResult(response)
        
        let httpClient = createMockHttpClient mockResponse
        let blobUri = "https://storage.blob.core.windows.net/results/job-123.json"
        
        // Act: Get job result
        let result = getJobResultAsync httpClient blobUri |> Async.RunSynchronously
        
        // Assert: Should return JobResult with data
        match result with
        | Ok jobResult -> 
            Assert.NotNull(jobResult.OutputData)
            Assert.Equal("application/json", jobResult.OutputDataFormat)
        | Error err -> Assert.True(false, sprintf "Expected success but got error: %A" err)
    
    [<Fact>]
    let ``getJobResultAsync should handle 404 blob not found`` () =
        // Arrange: Mock HTTP response with 404
        let mockResponse _ = 
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound))
        
        let httpClient = createMockHttpClient mockResponse
        let blobUri = "https://storage.blob.core.windows.net/results/nonexistent.json"
        
        // Act: Get job result
        let result = getJobResultAsync httpClient blobUri |> Async.RunSynchronously
        
        // Assert: Should return error
        match result with
        | Ok _ -> Assert.True(false, "Expected error for 404")
        | Error err -> 
            match err with
            | Types.QuantumError.UnknownError (404, _) -> Assert.True(true)
            | _ -> Assert.True(false, sprintf "Expected 404 error but got: %A" err)
    
    // ============================================================================
    // Job Cancellation Tests
    // ============================================================================
    
    [<Fact>]
    let ``cancelJobAsync should cancel running job`` () =
        // Arrange: Mock HTTP response with 204 No Content (success)
        let jobId = Guid.NewGuid().ToString()
        let mockResponse _ = 
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.NoContent))
        
        let httpClient = createMockHttpClient mockResponse
        let workspaceUrl = "https://test.quantum.azure.com"
        
        // Act: Cancel job
        let result = cancelJobAsync httpClient workspaceUrl jobId |> Async.RunSynchronously
        
        // Assert: Should succeed
        match result with
        | Ok () -> Assert.True(true)
        | Error err -> Assert.True(false, sprintf "Expected success but got error: %A" err)
    
    [<Fact>]
    let ``cancelJobAsync should handle 404 job not found`` () =
        // Arrange: Mock HTTP response with 404
        let jobId = Guid.NewGuid().ToString()
        let mockResponse _ = 
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound))
        
        let httpClient = createMockHttpClient mockResponse
        let workspaceUrl = "https://test.quantum.azure.com"
        
        // Act: Cancel job
        let result = cancelJobAsync httpClient workspaceUrl jobId |> Async.RunSynchronously
        
        // Assert: Should return error
        match result with
        | Ok _ -> Assert.True(false, "Expected error for 404")
        | Error err -> 
            match err with
            | Types.QuantumError.UnknownError (404, _) -> Assert.True(true)
            | _ -> Assert.True(false, sprintf "Expected 404 error but got: %A" err)
