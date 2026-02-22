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
/// 
/// All functions use task { } CE for direct Task-based I/O without
/// Async<>/Task<> bridging overhead. HttpClient is natively Task-based.
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
        : Task<Result<string, QuantumError>> =
        task {
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
                use content = new StringContent(jsonContent, Encoding.UTF8, "application/json")
                
                // Make PUT request to /jobs/{id}
                let url = sprintf "%s/jobs/%s" workspaceUrl submission.JobId
                use! response = httpClient.PutAsync(url, content)
                
                // Handle response
                match response.StatusCode with
                | HttpStatusCode.Created -> 
                    return Ok submission.JobId
                | HttpStatusCode.Unauthorized -> 
                    return Error (QuantumError.AzureError AzureQuantumError.InvalidCredentials)
                | HttpStatusCode.TooManyRequests ->
                    let retryAfter = 
                        if not (isNull response.Headers.RetryAfter) && response.Headers.RetryAfter.Delta.HasValue then
                            response.Headers.RetryAfter.Delta.Value
                        else
                            TimeSpan.FromSeconds(60.0)
                    return Error (QuantumError.AzureError (AzureQuantumError.RateLimited retryAfter))
                | HttpStatusCode.ServiceUnavailable ->
                    let retryAfter = 
                        if not (isNull response.Headers.RetryAfter) && response.Headers.RetryAfter.Delta.HasValue then
                            Some response.Headers.RetryAfter.Delta.Value
                        else
                            Some (TimeSpan.FromSeconds(30.0))
                    return Error (QuantumError.AzureError (AzureQuantumError.ServiceUnavailable retryAfter))
                | HttpStatusCode.NotFound ->
                    return Error (QuantumError.BackendError(submission.Target, "Backend not found"))
                | _ ->
                    let! errorBody = response.Content.ReadAsStringAsync()
                    return Error (QuantumError.AzureError (AzureQuantumError.UnknownError(int response.StatusCode, errorBody)))
            with
            | :? TaskCanceledException ->
                return Error (QuantumError.AzureError (AzureQuantumError.NetworkTimeout 1))
            | ex ->
                return Error (QuantumError.AzureError (AzureQuantumError.UnknownError(0, ex.Message)))
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
        : Task<Result<QuantumJob, QuantumError>> =
        task {
            try
                // Make GET request to /jobs/{id}
                let url = sprintf "%s/jobs/%s" workspaceUrl jobId
                use! response = httpClient.GetAsync(url)
                
                // Handle response
                match response.StatusCode with
                | HttpStatusCode.OK ->
                    // Parse JSON response manually using JsonDocument
                    let! jsonBody = response.Content.ReadAsStringAsync()
                    use jsonDoc = JsonDocument.Parse(jsonBody)
                    let root = jsonDoc.RootElement
                    
                    // Helper to safely get required string property (throws if null or missing)
                    let getRequiredString (name: string) =
                        let prop = root.GetProperty(name)
                        if prop.ValueKind = JsonValueKind.Null then
                            failwith (sprintf "Required property '%s' is null" name)
                        else
                            prop.GetString()
                    
                    // Extract required fields (will throw if null or missing - intentional for validation)
                    let id = getRequiredString "id"
                    let status = getRequiredString "status"
                    let target = getRequiredString "target"
                    let creationTime = getRequiredString "creationTime"
                    
                    // Helper to safely get optional string property
                    let tryGetStringProperty (name: string) =
                        tryGetJsonString name root
                    
                    // Extract optional timestamp fields
                    let beginExecutionTime = tryGetStringProperty "beginExecutionTime"
                    let endExecutionTime = tryGetStringProperty "endExecutionTime"
                    let cancellationTime = tryGetStringProperty "cancellationTime"
                    let outputDataUri = tryGetStringProperty "outputDataUri"
                    
                    // Extract error data if present
                    let (errorCode, errorMessage) =
                        match tryGetJsonProperty "errorData" root with
                        | Some errorData ->
                            let code = tryGetJsonString "code" errorData
                            let message = tryGetJsonString "message" errorData
                            (code, message)
                        | None ->
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
                    return Error (QuantumError.AzureError AzureQuantumError.InvalidCredentials)
                    
                | HttpStatusCode.NotFound ->
                    return Error (QuantumError.AzureError (AzureQuantumError.UnknownError(404, sprintf "Job %s not found" jobId)))
                    
                | HttpStatusCode.TooManyRequests ->
                    let retryAfter = 
                        if not (isNull response.Headers.RetryAfter) && response.Headers.RetryAfter.Delta.HasValue then
                            response.Headers.RetryAfter.Delta.Value
                        else
                            TimeSpan.FromSeconds(60.0)
                    return Error (QuantumError.AzureError (AzureQuantumError.RateLimited retryAfter))
                    
                | _ ->
                    let! errorBody = response.Content.ReadAsStringAsync()
                    return Error (QuantumError.AzureError (AzureQuantumError.UnknownError(int response.StatusCode, errorBody)))
                    
            with
            | :? TaskCanceledException ->
                return Error (QuantumError.AzureError (AzureQuantumError.NetworkTimeout 1))
            | ex ->
                return Error (QuantumError.AzureError (AzureQuantumError.UnknownError(0, ex.Message)))
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
        : Task<Result<QuantumJob, QuantumError>> =
        task {
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
            
            // Iterative polling loop (task CE does not support tail-call recursion)
            let mutable currentInterval = initialInterval
            let mutable finished = false
            let mutable finalResult = Error (QuantumError.OperationError("Job polling", "Polling did not complete"))
            
            while not finished do
                // Check cancellation
                if cancellationToken.IsCancellationRequested then
                    finalResult <- Error (QuantumError.OperationError("Job polling", "Operation cancelled"))
                    finished <- true
                else
                    // Check timeout
                    let elapsed = DateTimeOffset.UtcNow - startTime
                    if elapsed >= timeout then
                        finalResult <- Error (QuantumError.OperationError("Job polling", "Operation cancelled due to timeout"))
                        finished <- true
                    else
                        // Get current job status
                        let! result = getJobStatusAsync httpClient workspaceUrl jobId
                        
                        match result with
                        | Ok job ->
                            if isTerminal job.Status then
                                // Job complete - return result
                                finalResult <- Ok job
                                finished <- true
                            else
                                // Job still running - wait and poll again
                                do! Task.Delay(int currentInterval.TotalMilliseconds)
                                
                                // Calculate next interval with exponential backoff
                                let doubled = TimeSpan.FromMilliseconds(currentInterval.TotalMilliseconds * 2.0)
                                currentInterval <- if doubled > maxInterval then maxInterval else doubled
                        
                        | Error err ->
                            // Error getting status - return error
                            finalResult <- Error err
                            finished <- true
            
            return finalResult
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
        : Task<Result<JobResult, QuantumError>> =
        task {
            try
                // Make GET request to blob storage URI
                use! response = httpClient.GetAsync(blobUri)
                
                // Handle response
                match response.StatusCode with
                | HttpStatusCode.OK ->
                    // Download result data
                    let! resultJson = response.Content.ReadAsStringAsync()
                    let contentType = 
                        if not (isNull response.Content.Headers.ContentType) then
                            response.Content.Headers.ContentType.MediaType
                        else
                            "application/json"
                    
                    // Parse JSON to extract job ID if present
                    let jobId = 
                        try
                            use jsonDoc = JsonDocument.Parse(resultJson)
                            let root = jsonDoc.RootElement
                            tryGetJsonString "jobId" root
                            |> Option.defaultValue "unknown"
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
                    return Error (QuantumError.AzureError (AzureQuantumError.UnknownError(404, sprintf "Result blob not found at %s" blobUri)))
                    
                | HttpStatusCode.Unauthorized ->
                    return Error (QuantumError.AzureError AzureQuantumError.InvalidCredentials)
                    
                | _ ->
                    let! errorBody = response.Content.ReadAsStringAsync()
                    return Error (QuantumError.AzureError (AzureQuantumError.UnknownError(int response.StatusCode, errorBody)))
                    
            with
            | :? TaskCanceledException ->
                return Error (QuantumError.AzureError (AzureQuantumError.NetworkTimeout 1))
            | ex ->
                return Error (QuantumError.AzureError (AzureQuantumError.UnknownError(0, ex.Message)))
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
        : Task<Result<unit, QuantumError>> =
        task {
            try
                // Make DELETE request to /jobs/{id}
                let url = sprintf "%s/jobs/%s" workspaceUrl jobId
                use! response = httpClient.DeleteAsync(url)
                
                // Handle response
                match response.StatusCode with
                | HttpStatusCode.NoContent ->
                    // Successfully cancelled
                    return Ok ()
                    
                | HttpStatusCode.OK ->
                    // Some APIs return 200 OK for successful cancellation
                    return Ok ()
                    
                | HttpStatusCode.NotFound ->
                    return Error (QuantumError.AzureError (AzureQuantumError.UnknownError(404, sprintf "Job %s not found" jobId)))
                    
                | HttpStatusCode.Unauthorized ->
                    return Error (QuantumError.AzureError AzureQuantumError.InvalidCredentials)
                    
                | HttpStatusCode.TooManyRequests ->
                    let retryAfter = 
                        if not (isNull response.Headers.RetryAfter) && response.Headers.RetryAfter.Delta.HasValue then
                            response.Headers.RetryAfter.Delta.Value
                        else
                            TimeSpan.FromSeconds(60.0)
                    return Error (QuantumError.AzureError (AzureQuantumError.RateLimited retryAfter))
                    
                | _ ->
                    let! errorBody = response.Content.ReadAsStringAsync()
                    return Error (QuantumError.AzureError (AzureQuantumError.UnknownError(int response.StatusCode, errorBody)))
                    
            with
            | :? TaskCanceledException ->
                return Error (QuantumError.AzureError (AzureQuantumError.NetworkTimeout 1))
            | ex ->
                return Error (QuantumError.AzureError (AzureQuantumError.UnknownError(0, ex.Message)))
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
        : Task<Result<JobResult, QuantumError>> =
        task {
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
                        return Error (QuantumError.AzureError (AzureQuantumError.UnknownError(500, "Job completed but no output URI available")))
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
        : Task<Result<QuantumJob, QuantumError>> =
        task {
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
        : Task<Result<JobResult, QuantumError>> =
        task {
            match job.OutputDataUri with
            | None ->
                return Error (QuantumError.AzureError (AzureQuantumError.UnknownError(500, sprintf "Job %s has no output URI" job.JobId)))
            | Some uri ->
                return! getJobResultAsync httpClient uri
        }
