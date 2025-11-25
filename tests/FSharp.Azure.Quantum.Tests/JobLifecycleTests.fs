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
