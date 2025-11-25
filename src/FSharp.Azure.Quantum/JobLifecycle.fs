namespace FSharp.Azure.Quantum.Core

open System
open System.Net
open System.Net.Http
open System.Text
open System.Text.Json
open System.Threading
open System.Threading.Tasks
open FSharp.Azure.Quantum.Core.Types

/// Job lifecycle management for Azure Quantum
/// 
/// Implements job submission, status polling with exponential backoff,
/// result retrieval from blob storage, and job cancellation.
module JobLifecycle =
    
    // ============================================================================
    // JOB SUBMISSION
    // ============================================================================
    
    /// Submit a quantum job to Azure Quantum
    /// 
    /// Parameters:
    /// - httpClient: HTTP client for API requests
    /// - workspaceUrl: Azure Quantum workspace URL
    /// - submission: Job submission details
    /// 
    /// Returns: Result with job ID or QuantumError
    let submitJobAsync 
        (httpClient: HttpClient)
        (workspaceUrl: string)
        (submission: JobSubmission)
        : Async<Result<string, QuantumError>> =
        async {
            try
                // Construct job submission payload
                let payload = 
                    {|
                        id = submission.JobId
                        target = submission.Target
                        name = submission.Name |> Option.defaultValue "Quantum Job"
                        inputData = submission.InputData
                        inputDataFormat = submission.InputDataFormat.ToFormatString()
                        inputParams = submission.InputParams |> Map.toSeq |> dict
                        metadata = submission.Tags |> Map.toSeq |> dict
                    |}
                
                // Serialize to JSON
                let jsonContent = JsonSerializer.Serialize(payload)
                let content = new StringContent(jsonContent, System.Text.Encoding.UTF8, "application/json")
                
                // Make PUT request to /jobs/{id}
                let url = sprintf "%s/jobs/%s" workspaceUrl submission.JobId
                let! response = httpClient.PutAsync(url, content) |> Async.AwaitTask
                
                // Handle response
                match response.StatusCode with
                | HttpStatusCode.Created -> 
                    return Ok submission.JobId
                | HttpStatusCode.Unauthorized -> 
                    return Error QuantumError.InvalidCredentials
                | HttpStatusCode.TooManyRequests ->
                    let retryAfter = 
                        if response.Headers.RetryAfter <> null && response.Headers.RetryAfter.Delta.HasValue then
                            response.Headers.RetryAfter.Delta.Value
                        else
                            TimeSpan.FromSeconds(60.0)
                    return Error (QuantumError.RateLimited retryAfter)
                | HttpStatusCode.ServiceUnavailable ->
                    let retryAfter = 
                        if response.Headers.RetryAfter <> null && response.Headers.RetryAfter.Delta.HasValue then
                            Some response.Headers.RetryAfter.Delta.Value
                        else
                            Some (TimeSpan.FromSeconds(30.0))
                    return Error (QuantumError.ServiceUnavailable retryAfter)
                | HttpStatusCode.NotFound ->
                    return Error (QuantumError.BackendNotFound submission.Target)
                | _ ->
                    let! errorBody = response.Content.ReadAsStringAsync() |> Async.AwaitTask
                    return Error (QuantumError.UnknownError(int response.StatusCode, errorBody))
            with
            | :? TaskCanceledException as ex ->
                return Error (QuantumError.NetworkTimeout 1)
            | ex ->
                return Error (QuantumError.UnknownError(0, ex.Message))
        }
    
    // ============================================================================
    // JOB STATUS POLLING
    // ============================================================================
    
    /// JSON error data response
    [<CLIMutable>]
    type private ErrorData = {
        code: string
        message: string
    }
    
    /// JSON response type for job status
    [<CLIMutable>]
    type private JobStatusResponse = {
        id: string
        status: string
        target: string
        creationTime: string
        beginExecutionTime: string option
        endExecutionTime: string option
        cancellationTime: string option
        errorData: ErrorData option
    }
    
    /// Get current job status from Azure Quantum
    /// 
    /// Parameters:
    /// - httpClient: HTTP client for API requests
    /// - workspaceUrl: Azure Quantum workspace URL
    /// - jobId: Job identifier
    /// 
    /// Returns: Result with QuantumJob or QuantumError
    let getJobStatusAsync
        (httpClient: HttpClient)
        (workspaceUrl: string)
        (jobId: string)
        : Async<Result<QuantumJob, QuantumError>> =
        async {
            try
                // Make GET request to /jobs/{id}
                let url = sprintf "%s/jobs/%s" workspaceUrl jobId
                let! response = httpClient.GetAsync(url) |> Async.AwaitTask
                
                // Handle response
                match response.StatusCode with
                | HttpStatusCode.OK ->
                    // Parse JSON response manually using JsonDocument
                    let! jsonBody = response.Content.ReadAsStringAsync() |> Async.AwaitTask
                    use jsonDoc = JsonDocument.Parse(jsonBody)
                    let root = jsonDoc.RootElement
                    
                    // Extract required fields
                    let id = root.GetProperty("id").GetString()
                    let status = root.GetProperty("status").GetString()
                    let target = root.GetProperty("target").GetString()
                    let creationTime = root.GetProperty("creationTime").GetString()
                    
                    // Helper to safely get optional string property
                    let tryGetStringProperty (name: string) =
                        let mutable prop = Unchecked.defaultof<JsonElement>
                        if root.TryGetProperty(name, &prop) && prop.ValueKind <> JsonValueKind.Null then
                            Some (prop.GetString())
                        else
                            None
                    
                    // Extract optional timestamp fields
                    let beginExecutionTime = tryGetStringProperty "beginExecutionTime"
                    let endExecutionTime = tryGetStringProperty "endExecutionTime"
                    let cancellationTime = tryGetStringProperty "cancellationTime"
                    let outputDataUri = tryGetStringProperty "outputDataUri"
                    
                    // Extract error data if present
                    let (errorCode, errorMessage) =
                        let mutable errorData = Unchecked.defaultof<JsonElement>
                        if root.TryGetProperty("errorData", &errorData) && errorData.ValueKind <> JsonValueKind.Null then
                            let code = errorData.GetProperty("code").GetString()
                            let message = errorData.GetProperty("message").GetString()
                            (Some code, Some message)
                        else
                            (None, None)
                    
                    // Parse job status
                    let jobStatus = JobStatus.Parse(status, errorCode, errorMessage)
                    
                    // Build QuantumJob
                    let quantumJob = {
                        JobId = id
                        Status = jobStatus
                        Target = target
                        CreationTime = DateTimeOffset.Parse(creationTime)
                        BeginExecutionTime = beginExecutionTime |> Option.map DateTimeOffset.Parse
                        EndExecutionTime = endExecutionTime |> Option.map DateTimeOffset.Parse
                        CancellationTime = cancellationTime |> Option.map DateTimeOffset.Parse
                        OutputDataUri = outputDataUri
                    }
                    
                    return Ok quantumJob
                    
                | HttpStatusCode.Unauthorized -> 
                    return Error QuantumError.InvalidCredentials
                    
                | HttpStatusCode.NotFound ->
                    return Error (QuantumError.UnknownError(404, sprintf "Job %s not found" jobId))
                    
                | HttpStatusCode.TooManyRequests ->
                    let retryAfter = 
                        if response.Headers.RetryAfter <> null && response.Headers.RetryAfter.Delta.HasValue then
                            response.Headers.RetryAfter.Delta.Value
                        else
                            TimeSpan.FromSeconds(60.0)
                    return Error (QuantumError.RateLimited retryAfter)
                    
                | _ ->
                    let! errorBody = response.Content.ReadAsStringAsync() |> Async.AwaitTask
                    return Error (QuantumError.UnknownError(int response.StatusCode, errorBody))
                    
            with
            | :? TaskCanceledException ->
                return Error (QuantumError.NetworkTimeout 1)
            | ex ->
                return Error (QuantumError.UnknownError(0, ex.Message))
        }
    
    /// Poll job until it reaches a terminal state with exponential backoff
    /// 
    /// Polling strategy:
    /// - Start with 2 second interval
    /// - Double interval after each poll
    /// - Cap at 30 second maximum
    /// 
    /// Parameters:
    /// - httpClient: HTTP client for API requests
    /// - workspaceUrl: Azure Quantum workspace URL
    /// - jobId: Job identifier
    /// - timeout: Maximum time to wait (default: 5 minutes)
    /// - cancellationToken: Cancellation token
    /// 
    /// Returns: Result with final QuantumJob or QuantumError
    let pollJobUntilCompleteAsync
        (httpClient: HttpClient)
        (workspaceUrl: string)
        (jobId: string)
        (timeout: TimeSpan)
        (cancellationToken: CancellationToken)
        : Async<Result<QuantumJob, QuantumError>> =
        async {
            // Helper to check if job status is terminal
            let isTerminal (status: JobStatus) =
                match status with
                | JobStatus.Succeeded -> true
                | JobStatus.Failed _ -> true
                | JobStatus.Cancelled -> true
                | _ -> false
            
            // Exponential backoff parameters
            let initialInterval = TimeSpan.FromSeconds(2.0)
            let maxInterval = TimeSpan.FromSeconds(30.0)
            let startTime = DateTimeOffset.UtcNow
            
            // Recursive polling loop
            let rec pollLoop (currentInterval: TimeSpan) : Async<Result<QuantumJob, QuantumError>> =
                async {
                    // Check cancellation
                    if cancellationToken.IsCancellationRequested then
                        return Error QuantumError.Cancelled
                    else
                        // Check timeout
                        let elapsed = DateTimeOffset.UtcNow - startTime
                        if elapsed >= timeout then
                            return Error QuantumError.Cancelled
                        else
                            // Get current job status
                            let! result = getJobStatusAsync httpClient workspaceUrl jobId
                            
                            match result with
                            | Ok job ->
                                if isTerminal job.Status then
                                    // Job complete - return result
                                    return Ok job
                                else
                                    // Job still running - wait and poll again
                                    let! _ = Async.Sleep(int currentInterval.TotalMilliseconds)
                                    
                                    // Calculate next interval with exponential backoff
                                    let nextInterval = 
                                        let doubled = TimeSpan.FromMilliseconds(currentInterval.TotalMilliseconds * 2.0)
                                        if doubled > maxInterval then maxInterval else doubled
                                    
                                    // Continue polling
                                    return! pollLoop nextInterval
                            
                            | Error err ->
                                // Error getting status - return error
                                return Error err
                }
            
            // Start polling loop with initial interval
            return! pollLoop initialInterval
        }
    
    // ============================================================================
    // JOB RESULT RETRIEVAL
    // ============================================================================
    
    /// Download job result from blob storage
    /// 
    /// Parameters:
    /// - httpClient: HTTP client for API requests
    /// - blobUri: Azure Blob Storage URI for results
    /// 
    /// Returns: Result with JobResult or QuantumError
    let getJobResultAsync
        (httpClient: HttpClient)
        (blobUri: string)
        : Async<Result<JobResult, QuantumError>> =
        async {
            try
                // Make GET request to blob storage URI
                let! response = httpClient.GetAsync(blobUri) |> Async.AwaitTask
                
                // Handle response
                match response.StatusCode with
                | HttpStatusCode.OK ->
                    // Download result data
                    let! resultJson = response.Content.ReadAsStringAsync() |> Async.AwaitTask
                    let contentType = 
                        if response.Content.Headers.ContentType <> null then
                            response.Content.Headers.ContentType.MediaType
                        else
                            "application/json"
                    
                    // Parse JSON to extract job ID if present
                    let jobId = 
                        try
                            use jsonDoc = JsonDocument.Parse(resultJson)
                            let root = jsonDoc.RootElement
                            let mutable idProp = Unchecked.defaultof<JsonElement>
                            if root.TryGetProperty("jobId", &idProp) then
                                idProp.GetString()
                            else
                                "unknown"
                        with
                        | _ -> "unknown"
                    
                    // Create JobResult
                    let jobResult = {
                        JobId = jobId
                        Status = JobStatus.Succeeded  // Results only available for succeeded jobs
                        OutputData = box resultJson
                        OutputDataFormat = contentType
                        ExecutionTime = None  // Not available in result blob
                    }
                    
                    return Ok jobResult
                    
                | HttpStatusCode.NotFound ->
                    return Error (QuantumError.UnknownError(404, sprintf "Result blob not found at %s" blobUri))
                    
                | HttpStatusCode.Unauthorized ->
                    return Error QuantumError.InvalidCredentials
                    
                | _ ->
                    let! errorBody = response.Content.ReadAsStringAsync() |> Async.AwaitTask
                    return Error (QuantumError.UnknownError(int response.StatusCode, errorBody))
                    
            with
            | :? TaskCanceledException ->
                return Error (QuantumError.NetworkTimeout 1)
            | ex ->
                return Error (QuantumError.UnknownError(0, ex.Message))
        }
    
    // ============================================================================
    // JOB CANCELLATION
    // ============================================================================
    
    /// Cancel a running job
    /// 
    /// Parameters:
    /// - httpClient: HTTP client for API requests
    /// - workspaceUrl: Azure Quantum workspace URL
    /// - jobId: Job identifier
    /// 
    /// Returns: Result with unit or QuantumError
    let cancelJobAsync
        (httpClient: HttpClient)
        (workspaceUrl: string)
        (jobId: string)
        : Async<Result<unit, QuantumError>> =
        async {
            try
                // Make DELETE request to /jobs/{id}
                let url = sprintf "%s/jobs/%s" workspaceUrl jobId
                let! response = httpClient.DeleteAsync(url) |> Async.AwaitTask
                
                // Handle response
                match response.StatusCode with
                | HttpStatusCode.NoContent ->
                    // Successfully cancelled
                    return Ok ()
                    
                | HttpStatusCode.OK ->
                    // Some APIs return 200 OK for successful cancellation
                    return Ok ()
                    
                | HttpStatusCode.NotFound ->
                    return Error (QuantumError.UnknownError(404, sprintf "Job %s not found" jobId))
                    
                | HttpStatusCode.Unauthorized ->
                    return Error QuantumError.InvalidCredentials
                    
                | HttpStatusCode.TooManyRequests ->
                    let retryAfter = 
                        if response.Headers.RetryAfter <> null && response.Headers.RetryAfter.Delta.HasValue then
                            response.Headers.RetryAfter.Delta.Value
                        else
                            TimeSpan.FromSeconds(60.0)
                    return Error (QuantumError.RateLimited retryAfter)
                    
                | _ ->
                    let! errorBody = response.Content.ReadAsStringAsync() |> Async.AwaitTask
                    return Error (QuantumError.UnknownError(int response.StatusCode, errorBody))
                    
            with
            | :? TaskCanceledException ->
                return Error (QuantumError.NetworkTimeout 1)
            | ex ->
                return Error (QuantumError.UnknownError(0, ex.Message))
        }
    
    // ============================================================================
    // COMPOSITIONAL WORKFLOWS
    // ============================================================================
    
    /// Submit job and poll until complete, then retrieve results
    /// 
    /// This is the primary workflow function that composes the entire job lifecycle:
    /// submit → poll → get results
    /// 
    /// Parameters:
    /// - httpClient: HTTP client for API requests
    /// - workspaceUrl: Azure Quantum workspace URL
    /// - submission: Job submission details
    /// - timeout: Maximum polling time (default: 5 minutes)
    /// - cancellationToken: Cancellation token
    /// 
    /// Returns: Result with JobResult or QuantumError
    let submitAndWaitForResultAsync
        (httpClient: HttpClient)
        (workspaceUrl: string)
        (submission: JobSubmission)
        (timeout: TimeSpan)
        (cancellationToken: CancellationToken)
        : Async<Result<JobResult, QuantumError>> =
        async {
            // Step 1: Submit job
            let! submitResult = submitJobAsync httpClient workspaceUrl submission
            
            match submitResult with
            | Error err -> return Error err
            | Ok jobId ->
                // Step 2: Poll until complete
                let! pollResult = pollJobUntilCompleteAsync httpClient workspaceUrl jobId timeout cancellationToken
                
                match pollResult with
                | Error err -> return Error err
                | Ok job ->
                    // Step 3: Get results if available
                    match job.OutputDataUri with
                    | None -> 
                        return Error (QuantumError.UnknownError(500, "Job completed but no output URI available"))
                    | Some uri ->
                        return! getJobResultAsync httpClient uri
        }
    
    /// Submit job and poll until complete
    /// 
    /// Convenience function that submits a job and waits for completion.
    /// Use this when you want the final job status but not the results.
    /// 
    /// Parameters:
    /// - httpClient: HTTP client for API requests
    /// - workspaceUrl: Azure Quantum workspace URL
    /// - submission: Job submission details
    /// - timeout: Maximum polling time (default: 5 minutes)
    /// - cancellationToken: Cancellation token
    /// 
    /// Returns: Result with final QuantumJob or QuantumError
    let submitAndWaitAsync
        (httpClient: HttpClient)
        (workspaceUrl: string)
        (submission: JobSubmission)
        (timeout: TimeSpan)
        (cancellationToken: CancellationToken)
        : Async<Result<QuantumJob, QuantumError>> =
        async {
            // Step 1: Submit job
            let! submitResult = submitJobAsync httpClient workspaceUrl submission
            
            match submitResult with
            | Error err -> return Error err
            | Ok jobId ->
                // Step 2: Poll until complete
                return! pollJobUntilCompleteAsync httpClient workspaceUrl jobId timeout cancellationToken
        }
    
    /// Get job result from completed job
    /// 
    /// Convenience function that retrieves results from a QuantumJob.
    /// Handles the OutputDataUri extraction automatically.
    /// 
    /// Parameters:
    /// - httpClient: HTTP client for API requests
    /// - job: Completed quantum job
    /// 
    /// Returns: Result with JobResult or QuantumError
    let getResultFromJobAsync
        (httpClient: HttpClient)
        (job: QuantumJob)
        : Async<Result<JobResult, QuantumError>> =
        async {
            match job.OutputDataUri with
            | None ->
                return Error (QuantumError.UnknownError(500, sprintf "Job %s has no output URI" job.JobId))
            | Some uri ->
                return! getJobResultAsync httpClient uri
        }
