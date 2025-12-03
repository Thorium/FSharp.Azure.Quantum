namespace FSharp.Azure.Quantum.Core

open System
open System.Net.Http
open System.Net.Http.Headers
open System.Text
open System.Text.Json
open System.Threading
open Microsoft.Extensions.Logging
open FSharp.Azure.Quantum.Core.Types
open FSharp.Azure.Quantum.Core.Authentication
open FSharp.Azure.Quantum.Core.Retry
open FSharp.Azure.Quantum.Core.CostEstimation

module Client =

    /// Azure Quantum REST API endpoints
    module Endpoints =
        let private azureResourceManager = "https://management.azure.com"

        let private workspacePath subscriptionId resourceGroup workspaceName =
            sprintf
                "/subscriptions/%s/resourceGroups/%s/providers/Microsoft.Quantum/Workspaces/%s"
                subscriptionId
                resourceGroup
                workspaceName

        let jobsPath subscriptionId resourceGroup workspaceName =
            sprintf "%s/jobs?api-version=2022-09-12-preview" (workspacePath subscriptionId resourceGroup workspaceName)

        let jobPath subscriptionId resourceGroup workspaceName jobId =
            sprintf
                "%s/jobs/%s?api-version=2022-09-12-preview"
                (workspacePath subscriptionId resourceGroup workspaceName)
                jobId

        let cancelJobPath subscriptionId resourceGroup workspaceName jobId =
            sprintf
                "%s/jobs/%s/cancel?api-version=2022-09-12-preview"
                (workspacePath subscriptionId resourceGroup workspaceName)
                jobId

        let fullUrl path = azureResourceManager + path

    /// Quantum client configuration
    type QuantumClientConfig =
        { SubscriptionId: string
          ResourceGroup: string
          WorkspaceName: string
          HttpClient: HttpClient
          RetryConfig: RetryConfig option
          Logger: ILogger option

          // Cost management
          CostEstimationEnabled: bool
          PerJobCostLimit: decimal<USD> option
          DailyCostLimit: decimal<USD> option }

    /// Create default config
    let createConfig subscriptionId resourceGroup workspaceName httpClient =
        { SubscriptionId = subscriptionId
          ResourceGroup = resourceGroup
          WorkspaceName = workspaceName
          HttpClient = httpClient
          RetryConfig = Some Retry.defaultConfig
          Logger = None
          CostEstimationEnabled = true
          PerJobCostLimit = Some 200.0M<USD> // Conservative default: $200 per job
          DailyCostLimit = None // No daily limit by default
        }

    /// Job submission response from Azure
    type SubmitJobResponse =
        { JobId: string
          Status: JobStatus
          CreationTime: DateTimeOffset
          Uri: string }

    /// Quantum client for Azure Quantum REST API
    type QuantumClient(config: QuantumClientConfig) =

        let jsonOptions = JsonSerializerOptions()
        do jsonOptions.PropertyNameCaseInsensitive <- true

        let retryConfig = config.RetryConfig |> Option.defaultValue Retry.defaultConfig

        // Helper to log if logger exists
        member private this.Log(logLevel: LogLevel, message: string, [<ParamArray>] args: obj[]) =
            config.Logger |> Option.iter (fun logger -> logger.Log(logLevel, message, args))

        // Helper to check cost limits before submission
        member private this.CheckCostLimit(submission: JobSubmission) : Result<unit, QuantumError> =
            if not config.CostEstimationEnabled then
                Ok()
            else
                // Extract shot count from input params
                let shots =
                    match submission.InputParams.TryFind("shots") with
                    | Some s ->
                        try
                            s :?> int
                        with _ ->
                            1000 // Default to 1000 if conversion fails
                    | None -> 1000 // Default to 1000 shots

                match estimateCostSimple submission.Target shots with
                | Ok estimate ->
                    this.Log(
                        LogLevel.Information,
                        "Cost estimate for job {JobId}: ${Cost:F2} (Range: ${Min:F2} - ${Max:F2})",
                        submission.JobId,
                        float (estimate.ExpectedCost / 1.0M<USD>),
                        float (estimate.MinimumCost / 1.0M<USD>),
                        float (estimate.MaximumCost / 1.0M<USD>)
                    )

                    // Check per-job cost limit
                    match config.PerJobCostLimit with
                    | Some limit when estimate.ExpectedCost > limit ->
                        this.Log(
                            LogLevel.Warning,
                            "Job {JobId} estimated cost ${Cost:F2} exceeds per-job limit ${Limit:F2}",
                            submission.JobId,
                            float (estimate.ExpectedCost / 1.0M<USD>),
                            float (limit / 1.0M<USD>)
                        )

                        Error(
                            QuantumError.QuotaExceeded(
                                sprintf
                                    "Job cost $%.2f exceeds limit $%.2f"
                                    (float (estimate.ExpectedCost / 1.0M<USD>))
                                    (float (limit / 1.0M<USD>))
                            )
                        )
                    | _ ->
                        // Log warnings
                        estimate.Warnings
                        |> List.iter (fun warning ->
                            this.Log(
                                LogLevel.Warning,
                                "Cost warning for job {JobId}: {Warning}",
                                submission.JobId,
                                warning
                            ))

                        Ok()

                | Error msg ->
                    this.Log(
                        LogLevel.Warning,
                        "Could not estimate cost for job {JobId}: {Error}",
                        submission.JobId,
                        msg
                    )

                    Ok() // Don't block submission if cost estimation fails

        /// Submit a quantum job (internal implementation without retry)
        member private this.SubmitJobAsyncInternal(submission: JobSubmission, ct: CancellationToken) =
            async {

                this.Log(
                    LogLevel.Information,
                    "Submitting job {JobId} to target {Target}",
                    submission.JobId,
                    submission.Target
                )

                // Check cost limits before proceeding
                let costCheckResult = this.CheckCostLimit(submission)

                match costCheckResult with
                | Error err -> return Error err
                | Ok() ->

                    try
                        // Build endpoint URL
                        let endpoint =
                            Endpoints.jobPath
                                config.SubscriptionId
                                config.ResourceGroup
                                config.WorkspaceName
                                submission.JobId

                        let url = Endpoints.fullUrl endpoint

                        // Create request
                        use request = new HttpRequestMessage(HttpMethod.Put, url)

                        // Build request body
                        let body =
                            {| id = submission.JobId
                               name = submission.Name |> Option.defaultValue submission.JobId
                               target = submission.Target
                               inputData = submission.InputData
                               inputDataFormat = submission.InputDataFormat.ToFormatString()
                               inputParams = submission.InputParams
                               metadata = submission.Tags |}

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

                            let submitResponse =
                                { JobId = jobId
                                  Status = status
                                  CreationTime = creationTime
                                  Uri = url }

                            this.Log(
                                LogLevel.Information,
                                "Job {JobId} submitted successfully with status {Status}",
                                jobId,
                                status
                            )

                            return Ok submitResponse
                        else
                            let! errorBody = response.Content.ReadAsStringAsync(ct) |> Async.AwaitTask
                            let error = Retry.categorizeHttpError response.StatusCode errorBody

                            this.Log(
                                LogLevel.Error,
                                "Failed to submit job {JobId}: {StatusCode} - {Error}",
                                submission.JobId,
                                response.StatusCode,
                                error
                            )

                            return Error error
                    with ex ->
                        this.Log(
                            LogLevel.Error,
                            "Exception submitting job {JobId}: {Exception}",
                            submission.JobId,
                            ex.Message
                        )

                        return Error(QuantumError.UnknownError(0, ex.Message))
            }

        /// Submit a quantum job with retry logic
        member this.SubmitJobAsync(submission: JobSubmission, ?cancellationToken: CancellationToken) =
            async {
                let ct = defaultArg cancellationToken CancellationToken.None
                return! Retry.executeWithRetry retryConfig (fun ct -> this.SubmitJobAsyncInternal(submission, ct)) ct
            }

        /// Get job status (internal implementation without retry)
        member private this.GetJobStatusAsyncInternal(jobId: string, ct: CancellationToken) =
            async {

                try
                    // Build endpoint URL
                    let endpoint =
                        Endpoints.jobPath config.SubscriptionId config.ResourceGroup config.WorkspaceName jobId

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
                                Some(element.GetDateTimeOffset())
                            else
                                None

                        let endExecutionTime =
                            if root.TryGetProperty("endExecutionTime", &element) then
                                Some(element.GetDateTimeOffset())
                            else
                                None

                        let cancellationTime =
                            if root.TryGetProperty("cancellationTime", &element) then
                                Some(element.GetDateTimeOffset())
                            else
                                None

                        let status = JobStatus.Parse(statusStr, None, None)

                        let quantumJob =
                            { JobId = jobId
                              Status = status
                              Target = target
                              CreationTime = creationTime
                              BeginExecutionTime = beginExecutionTime
                              EndExecutionTime = endExecutionTime
                              CancellationTime = cancellationTime
                              OutputDataUri = None }

                        return Ok quantumJob
                    else
                        let! errorBody = response.Content.ReadAsStringAsync(ct) |> Async.AwaitTask
                        return Error(Retry.categorizeHttpError response.StatusCode errorBody)
                with ex ->
                    return Error(QuantumError.UnknownError(0, ex.Message))
            }

        /// Get job status with retry logic
        member this.GetJobStatusAsync(jobId: string, ?cancellationToken: CancellationToken) =
            async {
                let ct = defaultArg cancellationToken CancellationToken.None
                return! Retry.executeWithRetry retryConfig (fun ct -> this.GetJobStatusAsyncInternal(jobId, ct)) ct
            }

        /// Cancel a quantum job
        member this.CancelJobAsync(jobId: string, ?cancellationToken: CancellationToken) =
            async {
                let ct = defaultArg cancellationToken CancellationToken.None

                this.Log(LogLevel.Information, "Cancelling job {JobId}", jobId)

                try
                    // Build endpoint URL
                    let endpoint =
                        Endpoints.cancelJobPath config.SubscriptionId config.ResourceGroup config.WorkspaceName jobId

                    let url = Endpoints.fullUrl endpoint

                    // Create request
                    use request = new HttpRequestMessage(HttpMethod.Post, url)
                    request.Content <- new StringContent("{}", Encoding.UTF8, "application/json")

                    // Send request
                    let! response = config.HttpClient.SendAsync(request, ct) |> Async.AwaitTask

                    // Handle response
                    if response.IsSuccessStatusCode then
                        this.Log(LogLevel.Information, "Job {JobId} cancelled successfully", jobId)
                        return Ok()
                    else
                        let! errorBody = response.Content.ReadAsStringAsync(ct) |> Async.AwaitTask

                        this.Log(
                            LogLevel.Error,
                            "Failed to cancel job {JobId}: {StatusCode}",
                            jobId,
                            response.StatusCode
                        )

                        return Error(QuantumError.UnknownError(int response.StatusCode, errorBody))
                with ex ->
                    this.Log(LogLevel.Error, "Exception cancelling job {JobId}: {Exception}", jobId, ex.Message)
                    return Error(QuantumError.UnknownError(0, ex.Message))
            }

        /// Get job results after completion
        member this.GetResultsAsync(jobId: string, ?cancellationToken: CancellationToken) =
            async {
                let ct = defaultArg cancellationToken CancellationToken.None

                this.Log(LogLevel.Information, "Retrieving results for job {JobId}", jobId)

                try
                    // Build endpoint URL
                    let endpoint =
                        Endpoints.jobPath config.SubscriptionId config.ResourceGroup config.WorkspaceName jobId

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

                        let jobIdResult = root.GetProperty("id").GetString()
                        let statusStr = root.GetProperty("status").GetString()
                        let status = JobStatus.Parse(statusStr, None, None)

                        // Check if job has completed
                        if not (QuantumClient.isTerminalState status) then
                            return Error(QuantumError.UnknownError(400, "Job has not completed yet"))
                        else
                            // Check if job has results
                            let mutable element = Unchecked.defaultof<JsonElement>

                            if not (root.TryGetProperty("outputData", &element)) then
                                return Error(QuantumError.UnknownError(400, "Job does not have output data"))
                            else
                                let outputData = element

                                let outputDataFormat =
                                    if root.TryGetProperty("outputDataFormat", &element) then
                                        element.GetString()
                                    else
                                        "unknown"

                                let executionTime =
                                    if root.TryGetProperty("executionTime", &element) then
                                        // Parse ISO 8601 duration format (PT1.5S)
                                        let durationStr = element.GetString()

                                        try
                                            Some(System.Xml.XmlConvert.ToTimeSpan(durationStr))
                                        with _ ->
                                            None
                                    else
                                        None

                                let jobResult =
                                    { JobId = jobIdResult
                                      Status = status
                                      OutputData = box outputData
                                      OutputDataFormat = outputDataFormat
                                      ExecutionTime = executionTime }

                                this.Log(
                                    LogLevel.Information,
                                    "Retrieved results for job {JobId} (format: {Format})",
                                    jobIdResult,
                                    outputDataFormat
                                )

                                return Ok jobResult
                    else
                        let! errorBody = response.Content.ReadAsStringAsync(ct) |> Async.AwaitTask

                        this.Log(
                            LogLevel.Error,
                            "Failed to retrieve results for job {JobId}: {StatusCode}",
                            jobId,
                            response.StatusCode
                        )

                        return Error(QuantumError.UnknownError(int response.StatusCode, errorBody))
                with ex ->
                    this.Log(
                        LogLevel.Error,
                        "Exception retrieving results for job {JobId}: {Exception}",
                        jobId,
                        ex.Message
                    )

                    return Error(QuantumError.UnknownError(0, ex.Message))
            }

        /// Check if job is in terminal state
        static member private isTerminalState =
            function
            | JobStatus.Succeeded -> true
            | JobStatus.Failed _ -> true
            | JobStatus.Cancelled -> true
            | _ -> false

        /// Polling loop with exponential backoff (functional recursive approach)
        member private this.pollForCompletion
            (jobId: string)
            (startTime: DateTimeOffset)
            (currentDelay: int)
            (maxDelay: int)
            (timeoutMs: int)
            (ct: CancellationToken)
            : Async<Result<QuantumJob, QuantumError>> =
            async {

                if ct.IsCancellationRequested then
                    return Error(QuantumError.UnknownError(0, "Operation cancelled"))
                else
                    // Check timeout
                    let elapsed = (DateTimeOffset.UtcNow - startTime).TotalMilliseconds

                    if elapsed > float timeoutMs then
                        return Error(QuantumError.Timeout(sprintf "Job %s timed out after %dms" jobId timeoutMs))
                    else
                        // Poll job status
                        this.Log(LogLevel.Debug, "Polling job {JobId} status (delay: {Delay}ms)", jobId, currentDelay)
                        let! statusResult = this.GetJobStatusAsync(jobId, ct)

                        match statusResult with
                        | Ok job when QuantumClient.isTerminalState job.Status ->
                            // Job completed (success, failure, or cancelled)
                            this.Log(
                                LogLevel.Information,
                                "Job {JobId} completed with status {Status}",
                                jobId,
                                job.Status
                            )

                            return Ok job

                        | Ok job ->
                            // Job still running - wait and poll again
                            this.Log(
                                LogLevel.Debug,
                                "Job {JobId} still in progress (status: {Status}), waiting {Delay}ms",
                                jobId,
                                job.Status,
                                currentDelay
                            )

                            do! Async.Sleep currentDelay
                            let nextDelay = min (currentDelay * 2) maxDelay
                            return! this.pollForCompletion jobId startTime nextDelay maxDelay timeoutMs ct

                        | Error err ->
                            // Error getting status
                            this.Log(LogLevel.Error, "Error polling job {JobId}: {Error}", jobId, err)
                            return Error err
            }

        /// Wait for job completion with exponential backoff polling
        member this.WaitForCompletionAsync
            (
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

