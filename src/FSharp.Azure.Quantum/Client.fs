namespace FSharp.Azure.Quantum.Core

open System
open System.Net.Http
open System.Net.Http.Headers
open System.Text
open System.Text.Json
open System.Threading
open FSharp.Azure.Quantum.Core.Types
open FSharp.Azure.Quantum.Core.Authentication
open FSharp.Azure.Quantum.Core.Retry

module Client =
    
    /// Azure Quantum REST API endpoints
    module Endpoints =
        let private azureResourceManager = "https://management.azure.com"
        
        let private workspacePath subscriptionId resourceGroup workspaceName =
            sprintf "/subscriptions/%s/resourceGroups/%s/providers/Microsoft.Quantum/Workspaces/%s"
                subscriptionId resourceGroup workspaceName
        
        let jobsPath subscriptionId resourceGroup workspaceName =
            sprintf "%s/jobs?api-version=2022-09-12-preview" 
                (workspacePath subscriptionId resourceGroup workspaceName)
        
        let jobPath subscriptionId resourceGroup workspaceName jobId =
            sprintf "%s/jobs/%s?api-version=2022-09-12-preview" 
                (workspacePath subscriptionId resourceGroup workspaceName) jobId
        
        let cancelJobPath subscriptionId resourceGroup workspaceName jobId =
            sprintf "%s/jobs/%s/cancel?api-version=2022-09-12-preview" 
                (workspacePath subscriptionId resourceGroup workspaceName) jobId
        
        let fullUrl path = azureResourceManager + path
    
    /// Quantum client configuration
    type QuantumClientConfig = {
        SubscriptionId: string
        ResourceGroup: string
        WorkspaceName: string
        HttpClient: HttpClient
        RetryConfig: RetryConfig option
    }
    
    /// Create default config
    let createConfig subscriptionId resourceGroup workspaceName httpClient =
        {
            SubscriptionId = subscriptionId
            ResourceGroup = resourceGroup
            WorkspaceName = workspaceName
            HttpClient = httpClient
            RetryConfig = Some Retry.defaultConfig
        }
    
    /// Job submission response from Azure
    type SubmitJobResponse = {
        JobId: string
        Status: JobStatus
        CreationTime: DateTimeOffset
        Uri: string
    }
    
    /// Quantum client for Azure Quantum REST API
    type QuantumClient(config: QuantumClientConfig) =
        
        let jsonOptions = JsonSerializerOptions()
        do jsonOptions.PropertyNameCaseInsensitive <- true
        
        let retryConfig = config.RetryConfig |> Option.defaultValue Retry.defaultConfig
        
        /// Submit a quantum job (internal implementation without retry)
        member private this.SubmitJobAsyncInternal(submission: JobSubmission, ct: CancellationToken) = async {
            
            try
                // Build endpoint URL
                let endpoint = Endpoints.jobPath config.SubscriptionId config.ResourceGroup config.WorkspaceName submission.JobId
                let url = Endpoints.fullUrl endpoint
                
                // Create request
                use request = new HttpRequestMessage(HttpMethod.Put, url)
                
                // Build request body
                let body = {|
                    id = submission.JobId
                    name = submission.Name |> Option.defaultValue submission.JobId
                    target = submission.Target
                    inputData = submission.InputData
                    inputDataFormat = submission.InputDataFormat.ToFormatString()
                    inputParams = submission.InputParams
                    metadata = submission.Tags
                |}
                
                let jsonContent = JsonSerializer.Serialize(body, jsonOptions)
                request.Content <- new StringContent(jsonContent, Encoding.UTF8, "application/json")
                
                // Send request
                let! response = config.HttpClient.SendAsync(request, ct) |> Async.AwaitTask
                
                // Handle response
                if response.IsSuccessStatusCode then
                    let! responseBody = response.Content.ReadAsStringAsync(ct) |> Async.AwaitTask
                    let jsonDoc = JsonDocument.Parse(responseBody)
                    let root = jsonDoc.RootElement
                    
                    let jobId = root.GetProperty("id").GetString()
                    let statusStr = root.GetProperty("status").GetString()
                    let creationTime = root.GetProperty("creationTime").GetDateTimeOffset()
                    
                    let status = JobStatus.Parse(statusStr, None, None)
                    
                    let submitResponse = {
                        JobId = jobId
                        Status = status
                        CreationTime = creationTime
                        Uri = url
                    }
                    
                    return Ok submitResponse
                else
                    let! errorBody = response.Content.ReadAsStringAsync(ct) |> Async.AwaitTask
                    return Error (Retry.categorizeHttpError response.StatusCode errorBody)
            with
            | ex ->
                return Error (QuantumError.UnknownError(0, ex.Message))
        }
        
        /// Submit a quantum job with retry logic
        member this.SubmitJobAsync(submission: JobSubmission, ?cancellationToken: CancellationToken) = async {
            let ct = defaultArg cancellationToken CancellationToken.None
            return! Retry.executeWithRetry retryConfig (fun ct -> this.SubmitJobAsyncInternal(submission, ct)) ct
        }
        
        /// Get job status (internal implementation without retry)
        member private this.GetJobStatusAsyncInternal(jobId: string, ct: CancellationToken) = async {
            
            try
                // Build endpoint URL
                let endpoint = Endpoints.jobPath config.SubscriptionId config.ResourceGroup config.WorkspaceName jobId
                let url = Endpoints.fullUrl endpoint
                
                // Create request
                use request = new HttpRequestMessage(HttpMethod.Get, url)
                
                // Send request
                let! response = config.HttpClient.SendAsync(request, ct) |> Async.AwaitTask
                
                // Handle response
                if response.IsSuccessStatusCode then
                    let! responseBody = response.Content.ReadAsStringAsync(ct) |> Async.AwaitTask
                    let jsonDoc = JsonDocument.Parse(responseBody)
                    let root = jsonDoc.RootElement
                    
                    let jobId = root.GetProperty("id").GetString()
                    let statusStr = root.GetProperty("status").GetString()
                    let target = root.GetProperty("target").GetString()
                    let creationTime = root.GetProperty("creationTime").GetDateTimeOffset()
                    
                    let mutable element = Unchecked.defaultof<JsonElement>
                    
                    let beginExecutionTime = 
                        if root.TryGetProperty("beginExecutionTime", &element) then
                            Some (element.GetDateTimeOffset())
                        else None
                    
                    let endExecutionTime = 
                        if root.TryGetProperty("endExecutionTime", &element) then
                            Some (element.GetDateTimeOffset())
                        else None
                    
                    let cancellationTime = 
                        if root.TryGetProperty("cancellationTime", &element) then
                            Some (element.GetDateTimeOffset())
                        else None
                    
                    let status = JobStatus.Parse(statusStr, None, None)
                    
                    let quantumJob = {
                        JobId = jobId
                        Status = status
                        Target = target
                        CreationTime = creationTime
                        BeginExecutionTime = beginExecutionTime
                        EndExecutionTime = endExecutionTime
                        CancellationTime = cancellationTime
                    }
                    
                    return Ok quantumJob
                else
                    let! errorBody = response.Content.ReadAsStringAsync(ct) |> Async.AwaitTask
                    return Error (Retry.categorizeHttpError response.StatusCode errorBody)
            with
            | ex ->
                return Error (QuantumError.UnknownError(0, ex.Message))
        }
        
        /// Get job status with retry logic
        member this.GetJobStatusAsync(jobId: string, ?cancellationToken: CancellationToken) = async {
            let ct = defaultArg cancellationToken CancellationToken.None
            return! Retry.executeWithRetry retryConfig (fun ct -> this.GetJobStatusAsyncInternal(jobId, ct)) ct
        }
        
        /// Cancel a quantum job
        member this.CancelJobAsync(jobId: string, ?cancellationToken: CancellationToken) = async {
            let ct = defaultArg cancellationToken CancellationToken.None
            
            try
                // Build endpoint URL
                let endpoint = Endpoints.cancelJobPath config.SubscriptionId config.ResourceGroup config.WorkspaceName jobId
                let url = Endpoints.fullUrl endpoint
                
                // Create request
                use request = new HttpRequestMessage(HttpMethod.Post, url)
                request.Content <- new StringContent("{}", Encoding.UTF8, "application/json")
                
                // Send request
                let! response = config.HttpClient.SendAsync(request, ct) |> Async.AwaitTask
                
                // Handle response
                if response.IsSuccessStatusCode then
                    return Ok ()
                else
                    let! errorBody = response.Content.ReadAsStringAsync(ct) |> Async.AwaitTask
                    return Error (QuantumError.UnknownError(int response.StatusCode, errorBody))
            with
            | ex ->
                return Error (QuantumError.UnknownError(0, ex.Message))
        }
        
        /// Check if job is in terminal state
        static member private isTerminalState = function
            | JobStatus.Succeeded
            | JobStatus.Failed _
            | JobStatus.Cancelled _ -> true
            | _ -> false
        
        /// Polling loop with exponential backoff (functional recursive approach)
        member private this.pollForCompletion 
            (jobId: string)
            (startTime: DateTimeOffset)
            (currentDelay: int)
            (maxDelay: int)
            (timeoutMs: int)
            (ct: CancellationToken)
            : Async<Result<QuantumJob, QuantumError>> = async {
            
            if ct.IsCancellationRequested then
                return Error (QuantumError.UnknownError(0, "Operation cancelled"))
            else
                // Check timeout
                let elapsed = (DateTimeOffset.UtcNow - startTime).TotalMilliseconds
                if elapsed > float timeoutMs then
                    return Error (QuantumError.Timeout(sprintf "Job %s timed out after %dms" jobId timeoutMs))
                else
                    // Poll job status
                    let! statusResult = this.GetJobStatusAsync(jobId, ct)
                    
                    match statusResult with
                    | Ok job when QuantumClient.isTerminalState job.Status ->
                        // Job completed (success, failure, or cancelled)
                        return Ok job
                        
                    | Ok _job ->
                        // Job still running - wait and poll again
                        do! Async.Sleep currentDelay
                        let nextDelay = min (currentDelay * 2) maxDelay
                        return! this.pollForCompletion jobId startTime nextDelay maxDelay timeoutMs ct
                        
                    | Error err ->
                        // Error getting status
                        return Error err
        }
        
        /// Wait for job completion with exponential backoff polling
        member this.WaitForCompletionAsync(
            jobId: string, 
            ?initialDelayMs: int, 
            ?maxDelayMs: int, 
            ?timeoutMs: int,
            ?cancellationToken: CancellationToken
        ) = 
            let ct = defaultArg cancellationToken CancellationToken.None
            let initialDelay = defaultArg initialDelayMs 1000
            let maxDelay = defaultArg maxDelayMs 30000
            let timeout = defaultArg timeoutMs 1800000
            
            this.pollForCompletion jobId DateTimeOffset.UtcNow initialDelay maxDelay timeout ct
