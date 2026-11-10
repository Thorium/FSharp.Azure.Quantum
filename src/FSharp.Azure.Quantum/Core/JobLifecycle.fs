namespace FSharp.Azure.Quantum.Core

open System
open System.Net
open System.Net.Http
open System.Net.Http.Headers
open System.Text
open System.Text.Json
open System.Text.RegularExpressions
open System.Threading
open System.Threading.Tasks
open FSharp.Azure.Quantum.Core.Types

/// Job lifecycle management for Azure Quantum
///
/// Implements job submission (input upload to the workspace's blob storage, then job
/// creation), status polling with exponential backoff, result retrieval from blob storage,
/// and job cancellation, against the Azure Quantum data-plane REST API.
///
/// All functions use task { } CE for direct Task-based I/O without
/// Async<>/Task<> bridging overhead. HttpClient is natively Task-based.
module JobLifecycle =

    /// Azure Quantum data-plane API version. The service requires `api-version` on every
    /// workspace request; a request without it is rejected.
    [<Literal>]
    let ApiVersion = "2026-01-15-preview"

    /// URL of a workspace resource: {workspaceUrl}/{relativePath}?api-version={ApiVersion}.
    let workspaceResourceUrl (workspaceUrl: string) (relativePath: string) : string =
        $"%s{workspaceUrl.TrimEnd '/'}/%s{relativePath}?api-version=%s{ApiVersion}"

    // ============================================================================
    // SAS URLS AND ERROR MAPPING
    // ============================================================================

    let private sasSignature =
        Regex(@"(\bsig=)[^&\s""'<>]+", RegexOptions.IgnoreCase ||| RegexOptions.Compiled)

    /// Text with the signature of every SAS URL in it removed. A SAS signature is a
    /// credential: it never goes into an error message or a log.
    let internal withoutSasSignature (text: string) : string =
        if isNull text then
            ""
        else
            sasSignature.Replace(text, "$1REDACTED")

    /// A URI without its query string (the SAS token of a blob URI).
    let internal withoutQuery (uri: string) : string =
        match uri.IndexOf '?' with
        | -1 -> uri
        | index -> uri.Substring(0, index)

    /// Maps a response that is not the expected success, and its body, to the error reported.
    type internal ResponseError = HttpResponseMessage -> string -> QuantumError

    let private retryAfter (response: HttpResponseMessage) (fallback: TimeSpan) : TimeSpan =
        match response.Headers.RetryAfter with
        | null -> fallback
        | header when header.Delta.HasValue -> header.Delta.Value
        | _ -> fallback

    /// Error for a workspace response; `notFound` is what a 404 means for the request.
    let private workspaceError (notFound: QuantumError) : ResponseError =
        fun response body ->
            match response.StatusCode with
            | HttpStatusCode.Unauthorized -> QuantumError.AzureError AzureQuantumError.InvalidCredentials
            | HttpStatusCode.TooManyRequests ->
                QuantumError.AzureError(
                    AzureQuantumError.RateLimited(retryAfter response (TimeSpan.FromSeconds 60.0))
                )
            | HttpStatusCode.ServiceUnavailable ->
                QuantumError.AzureError(
                    AzureQuantumError.ServiceUnavailable(Some(retryAfter response (TimeSpan.FromSeconds 30.0)))
                )
            | HttpStatusCode.NotFound -> notFound
            | status -> QuantumError.AzureError(AzureQuantumError.UnknownError(int status, withoutSasSignature body))

    let private jobNotFound (jobId: string) : QuantumError =
        QuantumError.AzureError(AzureQuantumError.UnknownError(404, $"Job %s{jobId} not found"))

    /// Error for an exception thrown while a request was in flight. Status code 0 marks a
    /// request that produced no HTTP response.
    let private requestException (ex: exn) : QuantumError =
        match ex with
        | :? TaskCanceledException -> QuantumError.AzureError(AzureQuantumError.NetworkTimeout 1)
        | _ -> QuantumError.AzureError(AzureQuantumError.UnknownError(0, withoutSasSignature ex.Message))

    /// Send a request and return the response body when the status is one of `expected`.
    let private sendAsync
        (httpClient: HttpClient)
        (toError: ResponseError)
        (expected: HttpStatusCode list)
        (request: HttpRequestMessage)
        (ct: CancellationToken)
        : Task<Result<string, QuantumError>> =
        task {
            use! response = httpClient.SendAsync(request, ct)
            let! body = response.Content.ReadAsStringAsync ct

            return
                if List.contains response.StatusCode expected then
                    Ok body
                else
                    Error(toError response body)
        }

    /// Continue with `next` on Ok; an Error ends the chain.
    let private andThenAsync
        (next: 'T -> Task<Result<'U, QuantumError>>)
        (step: Task<Result<'T, QuantumError>>)
        : Task<Result<'U, QuantumError>> =
        task {
            match! step with
            | Ok value -> return! next value
            | Error error -> return Error error
        }

    // ============================================================================
    // JOB SUBMISSION
    // ============================================================================

    /// Blob container the service keeps a job's input and output in.
    let private containerName (jobId: string) = $"job-%s{jobId}"

    [<Literal>]
    let private inputBlobName = "inputData"

    /// Provider id sent with a submission: its ProviderId, else the part of the target
    /// before the first '.' ("ionq.simulator" → "ionq").
    let providerIdOf (submission: JobSubmission) : string =
        match submission.ProviderId with
        | Some provider when not (String.IsNullOrWhiteSpace provider) -> provider
        | _ ->
            match submission.Target.IndexOf '.' with
            | -1 -> submission.Target
            | index -> submission.Target.Substring(0, index)

    /// The service takes only a GUID (8-4-4-4-12 hex digits) as a job id.
    let private validateSubmission (submission: JobSubmission) : Result<unit, QuantumError> =
        if not (fst (Guid.TryParseExact(submission.JobId, "D"))) then
            Error(QuantumError.ValidationError("JobId", $"'%s{submission.JobId}' is not a GUID"))
        elif String.IsNullOrWhiteSpace submission.Target then
            Error(QuantumError.ValidationError("Target", "A job needs a target"))
        elif String.IsNullOrWhiteSpace(providerIdOf submission) then
            Error(QuantumError.ValidationError("ProviderId", "A job needs a provider id"))
        elif isNull submission.InputData then
            Error(QuantumError.ValidationError("InputData", "A job needs input data"))
        else
            Ok()

    /// Bytes and media type of the input blob: a string or byte[] as it is, anything else as JSON.
    let private inputContent (inputData: obj) : byte[] * string =
        match inputData with
        | :? (byte[]) as bytes -> bytes, "application/octet-stream"
        | :? string as text ->
            let start = text.TrimStart()

            let mediaType =
                if start.StartsWith '{' || start.StartsWith '[' then
                    "application/json"
                else
                    "text/plain"

            Encoding.UTF8.GetBytes text, mediaType
        | other -> JsonSerializer.SerializeToUtf8Bytes other, "application/json"

    /// Ask the workspace for a SAS URI of the container (which the service creates when it
    /// does not exist), or of one blob in it.
    let private requestSasUriAsync
        (httpClient: HttpClient)
        (toError: ResponseError)
        (workspaceUrl: string)
        (container: string)
        (blob: string voption)
        (ct: CancellationToken)
        : Task<Result<string, QuantumError>> =
        task {
            let payload =
                match blob with
                | ValueSome blobName ->
                    JsonSerializer.Serialize
                        {|
                            containerName = container
                            blobName = blobName
                        |}
                | ValueNone -> JsonSerializer.Serialize {| containerName = container |}

            use request =
                new HttpRequestMessage(HttpMethod.Post, workspaceResourceUrl workspaceUrl "storage/sasUri")

            request.Content <- new StringContent(payload, Encoding.UTF8, "application/json")

            match! sendAsync httpClient toError [ HttpStatusCode.OK ] request ct with
            | Error error -> return Error error
            | Ok body ->
                let sasUri =
                    try
                        use doc = JsonDocument.Parse body
                        tryGetJsonString "sasUri" doc.RootElement
                    with
                    | :? JsonException
                    | :? InvalidOperationException -> None

                match sasUri with
                | Some uri when not (String.IsNullOrWhiteSpace uri) -> return Ok uri
                | _ ->
                    return
                        Error(
                            QuantumError.AzureError(
                                AzureQuantumError.UnknownError(200, "The storage/sasUri response holds no sasUri")
                            )
                        )
        }

    /// PUT a block blob to its SAS URI. The SAS query string is the credential: the request
    /// is marked so the workspace bearer token is withheld from the storage host.
    let private uploadBlobAsync
        (httpClient: HttpClient)
        (sasUri: string)
        (content: byte[])
        (mediaType: string)
        (ct: CancellationToken)
        : Task<Result<unit, QuantumError>> =
        task {
            use request = new HttpRequestMessage(HttpMethod.Put, sasUri)
            Authentication.markNoAuth request
            request.Headers.TryAddWithoutValidation("x-ms-blob-type", "BlockBlob") |> ignore
            request.Content <- new ByteArrayContent(content)
            request.Content.Headers.ContentType <- MediaTypeHeaderValue mediaType

            let toError: ResponseError =
                fun response body ->
                    QuantumError.AzureError(
                        AzureQuantumError.UnknownError(
                            int response.StatusCode,
                            $"Uploading the job input failed: %s{withoutSasSignature body}"
                        )
                    )

            let! uploaded = sendAsync httpClient toError [ HttpStatusCode.Created ] request ct
            return uploaded |> Result.map ignore
        }

    /// Job definition for Jobs - Create. The service takes no inline input: the job names
    /// its container and the uploaded input blob.
    let private jobDefinition (submission: JobSubmission) (containerUri: string) (inputDataUri: string) : string =
        let fields: (string * obj) list =
            [
                "id", box submission.JobId
                "name", box (submission.Name |> Option.defaultValue "Quantum Job")
                "itemType", box "Job"
                "providerId", box (providerIdOf submission)
                "target", box submission.Target
                "containerUri", box containerUri
                "inputDataUri", box inputDataUri
                "inputDataFormat", box (submission.InputDataFormat.ToFormatString())
                "inputParams", box submission.InputParams
                "metadata", box submission.Tags
                match submission.OutputDataFormat with
                | Some format -> "outputDataFormat", box format
                | None -> ()
            ]

        JsonSerializer.Serialize(Map.ofList fields)

    /// Submit a job and return the service's job document (the Jobs - Create response body):
    /// container SAS → input blob SAS → input upload → job creation. Exceptions, cancellation
    /// included, propagate to the caller.
    let internal createJobAsync
        (toError: ResponseError)
        (httpClient: HttpClient)
        (workspaceUrl: string)
        (submission: JobSubmission)
        (ct: CancellationToken)
        : Task<Result<string, QuantumError>> =
        match validateSubmission submission with
        | Error error -> Task.FromResult(Error error)
        | Ok() ->
            let container = containerName submission.JobId

            requestSasUriAsync httpClient toError workspaceUrl container ValueNone ct
            |> andThenAsync (fun containerUri ->
                requestSasUriAsync httpClient toError workspaceUrl container (ValueSome inputBlobName) ct
                |> andThenAsync (fun inputDataUri ->
                    let content, mediaType = inputContent submission.InputData

                    uploadBlobAsync httpClient inputDataUri content mediaType ct
                    |> andThenAsync (fun () ->
                        task {
                            use request =
                                new HttpRequestMessage(
                                    HttpMethod.Put,
                                    workspaceResourceUrl workspaceUrl $"jobs/%s{submission.JobId}"
                                )

                            request.Content <-
                                new StringContent(
                                    jobDefinition submission containerUri inputDataUri,
                                    Encoding.UTF8,
                                    "application/json"
                                )

                            return!
                                sendAsync
                                    httpClient
                                    toError
                                    [ HttpStatusCode.OK; HttpStatusCode.Created ]
                                    request
                                    ct
                        })))

    /// Submit a quantum job to Azure Quantum
    ///
    /// The input is uploaded to the workspace's blob storage (the service takes no inline
    /// input) and the job is then created with its container and input blob URIs.
    ///
    /// Parameters:
    /// - httpClient: HTTP client for API requests
    /// - workspaceUrl: Azure Quantum workspace URL
    /// - submission: Job submission details (JobId must be a GUID)
    ///
    /// Returns: Result with job ID or QuantumError
    let submitJobAsync
        (httpClient: HttpClient)
        (workspaceUrl: string)
        (submission: JobSubmission)
        : Task<Result<string, QuantumError>> =
        task {
            try
                let notFound = QuantumError.BackendError(submission.Target, "Backend not found")

                let! created =
                    createJobAsync (workspaceError notFound) httpClient workspaceUrl submission CancellationToken.None

                return created |> Result.map (fun _ -> submission.JobId)
            with ex ->
                return Error(requestException ex)
        }

    // ============================================================================
    // JOB STATUS POLLING
    // ============================================================================

    /// QuantumJob of a job document (Jobs - Get). Throws when a required property is
    /// missing or null.
    let private parseJob (root: JsonElement) : QuantumJob =
        // Helper to safely get required string property (throws if null or missing)
        let getRequiredString (name: string) =
            let prop = root.GetProperty name

            if prop.ValueKind = JsonValueKind.Null then
                failwith $"Required property '%s{name}' is null"
            else
                prop.GetString()

        // Extract required fields (will throw if null or missing - intentional for validation)
        let pid = getRequiredString "id"
        let status = getRequiredString "status"
        let target = getRequiredString "target"
        let creationTime = getRequiredString "creationTime"

        // Helper to safely get optional string property
        let tryGetStringProperty (name: string) = tryGetJsonString name root

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
            | None -> (None, None)

        {
            JobId = pid
            Status = JobStatus.Parse(status, errorCode, errorMessage)
            Target = target
            CreationTime = DateTimeOffset.Parse(creationTime, System.Globalization.CultureInfo.InvariantCulture)
            BeginExecutionTime = beginExecutionTime |> Option.map DateTimeOffset.Parse
            EndExecutionTime = endExecutionTime |> Option.map DateTimeOffset.Parse
            CancellationTime = cancellationTime |> Option.map DateTimeOffset.Parse
            OutputDataUri = outputDataUri
        }

    let private getJobStatusWithTokenAsync
        (httpClient: HttpClient)
        (workspaceUrl: string)
        (jobId: string)
        (ct: CancellationToken)
        : Task<Result<QuantumJob, QuantumError>> =
        task {
            try
                use request =
                    new HttpRequestMessage(HttpMethod.Get, workspaceResourceUrl workspaceUrl $"jobs/%s{jobId}")

                match! sendAsync httpClient (workspaceError (jobNotFound jobId)) [ HttpStatusCode.OK ] request ct with
                | Error error -> return Error error
                | Ok body ->
                    try
                        use jsonDoc = JsonDocument.Parse body
                        return Ok(parseJob jsonDoc.RootElement)
                    with ex ->
                        // A response that arrived but cannot be read is not a missing
                        // response: it keeps its status code, so it is not retried as one.
                        return
                            Error(
                                QuantumError.AzureError(
                                    AzureQuantumError.UnknownError(
                                        200,
                                        $"The status of job %s{jobId} could not be read: %s{withoutSasSignature ex.Message}"
                                    )
                                )
                            )
            with ex ->
                return Error(requestException ex)
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
        getJobStatusWithTokenAsync httpClient workspaceUrl jobId CancellationToken.None

    // ============================================================================
    // JOB CANCELLATION
    // ============================================================================

    /// Request the cancellation of a job (Jobs - Cancel)
    ///
    /// Parameters:
    /// - httpClient: HTTP client for API requests
    /// - workspaceUrl: Azure Quantum workspace URL
    /// - jobId: Job identifier
    ///
    /// Returns: Ok when the service accepted the request (the job then moves through
    /// Cancelling to Cancelled), or QuantumError
    let cancelJobAsync
        (httpClient: HttpClient)
        (workspaceUrl: string)
        (jobId: string)
        : Task<Result<unit, QuantumError>> =
        task {
            try
                use request =
                    new HttpRequestMessage(
                        HttpMethod.Post,
                        workspaceResourceUrl workspaceUrl $"jobs/%s{jobId}:cancel"
                    )

                let! cancelled =
                    sendAsync
                        httpClient
                        (workspaceError (jobNotFound jobId))
                        [ HttpStatusCode.OK; HttpStatusCode.Accepted; HttpStatusCode.NoContent ]
                        request
                        CancellationToken.None

                return cancelled |> Result.map ignore
            with ex ->
                return Error(requestException ex)
        }

    // ============================================================================
    // POLLING UNTIL COMPLETE
    // ============================================================================

    /// Why waiting for a job stopped before the job reached a final status.
    [<RequireQualifiedAccess>]
    type private Abandoned =
        | TimedOut
        | CallerCancelled
        | StatusUnavailable of QuantumError

    /// A status request that failed in a way a later attempt can succeed: no HTTP response
    /// at all (status code 0), a timeout, 429 or 5xx.
    let private isTransientStatusFailure (error: QuantumError) : bool =
        match error with
        | QuantumError.AzureError(AzureQuantumError.UnknownError(0, _)) -> true
        | other -> Retry.isTransientError other

    /// Error for a job that is no longer waited for. It always names the job, and says
    /// whether the service accepted the cancellation, so the caller knows what may still
    /// run and bill.
    let private abandonedError
        (jobId: string)
        (timeout: TimeSpan)
        (reason: Abandoned)
        (cancellation: Result<unit, QuantumError>)
        : QuantumError =
        let cancellationNote =
            match cancellation with
            | Ok() -> "the cancellation request was accepted"
            | Error error ->
                $"the cancellation request was not accepted (%s{error.Message}), so the job may still run and bill"

        match reason with
        | Abandoned.TimedOut ->
            QuantumError.AzureError(
                AzureQuantumError.JobTimeout(
                    $"Job %s{jobId} did not reach a final status within %g{timeout.TotalSeconds} s; %s{cancellationNote}"
                )
            )
        | Abandoned.CallerCancelled ->
            QuantumError.OperationError(
                "Job polling",
                $"Operation cancelled by the caller while waiting for job %s{jobId}; %s{cancellationNote}"
            )
        | Abandoned.StatusUnavailable error ->
            QuantumError.OperationError(
                "Job polling",
                $"The status of job %s{jobId} could not be read (%s{error.Message}); %s{cancellationNote}"
            )

    /// Poll job until it reaches a terminal state with exponential backoff
    ///
    /// Polling strategy:
    /// - Start with 2 second interval
    /// - Double interval after each poll
    /// - Cap at 30 second maximum
    /// - A status request that fails transiently (no response, timeout, 429, 5xx) is
    ///   retried with Retry.defaultConfig's backoff, up to its MaxAttempts in a row
    ///
    /// A job that is no longer waited for is not left running: on timeout, on caller
    /// cancellation and when the status cannot be read, the service is asked to cancel the
    /// job (without the caller's token), and the Error names the job id and says whether
    /// the cancellation request was accepted.
    ///
    /// Parameters:
    /// - httpClient: HTTP client for API requests
    /// - workspaceUrl: Azure Quantum workspace URL
    /// - jobId: Job identifier
    /// - timeout: Maximum time to wait
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
            // Exponential backoff parameters
            let initialInterval = TimeSpan.FromSeconds(2.0)
            let maxInterval = TimeSpan.FromSeconds(30.0)
            let startTime = DateTimeOffset.UtcNow

            // Sleep that ends early on cancellation and never runs past the timeout; the
            // loop head then reports whichever happened.
            let pause (duration: TimeSpan) : Task =
                task {
                    let remaining = timeout - (DateTimeOffset.UtcNow - startTime)
                    let capped = if duration < remaining then duration else remaining

                    if capped > TimeSpan.Zero then
                        try
                            do! Task.Delay(capped, cancellationToken)
                        with :? OperationCanceledException ->
                            ()
                }

            // Iterative polling loop (task CE does not support tail-call recursion)
            let mutable currentInterval = initialInterval
            let mutable failedAttempts = 0
            let mutable outcome: Result<QuantumJob, Abandoned> voption = ValueNone

            while outcome.IsNone do
                if cancellationToken.IsCancellationRequested then
                    outcome <- ValueSome(Error Abandoned.CallerCancelled)
                elif DateTimeOffset.UtcNow - startTime >= timeout then
                    outcome <- ValueSome(Error Abandoned.TimedOut)
                else
                    match! getJobStatusWithTokenAsync httpClient workspaceUrl jobId cancellationToken with
                    | Ok job when job.Status.IsTerminal -> outcome <- ValueSome(Ok job)
                    | Ok _ ->
                        failedAttempts <- 0
                        do! pause currentInterval

                        // Calculate next interval with exponential backoff
                        let doubled = TimeSpan.FromMilliseconds(currentInterval.TotalMilliseconds * 2.0)
                        currentInterval <- if doubled > maxInterval then maxInterval else doubled
                    | Error error when
                        isTransientStatusFailure error
                        && failedAttempts + 1 < Retry.defaultConfig.MaxAttempts
                        ->
                        failedAttempts <- failedAttempts + 1

                        do!
                            pause (
                                TimeSpan.FromMilliseconds(
                                    float (Retry.calculateDelay Retry.defaultConfig failedAttempts)
                                )
                            )
                    | Error error -> outcome <- ValueSome(Error(Abandoned.StatusUnavailable error))

            match outcome.Value with
            | Ok job -> return Ok job
            | Error reason ->
                let! cancellation = cancelJobAsync httpClient workspaceUrl jobId
                return Error(abandonedError jobId timeout reason cancellation)
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
    let getJobResultAsync (httpClient: HttpClient) (blobUri: string) : Task<Result<JobResult, QuantumError>> =
        task {
            try
                // Make GET request to blob storage URI. The SAS query string is the
                // credential: suppress the workspace bearer token, which Azure Storage
                // would reject (401) and must not receive.
                use request = new HttpRequestMessage(HttpMethod.Get, blobUri)
                Authentication.markNoAuth request
                use! response = httpClient.SendAsync request

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
                            tryGetJsonString "jobId" root |> Option.defaultValue "unknown"
                        with
                        // The blob need not be JSON at all, and "jobId" need not be a string.
                        | :? JsonException
                        | :? InvalidOperationException -> "unknown"

                    // Create JobResult
                    let jobResult =
                        {
                            JobId = jobId
                            Status = JobStatus.Succeeded // Results only available for succeeded jobs
                            OutputData = box resultJson
                            OutputDataFormat = contentType
                            ExecutionTime = None // Not available in result blob
                        }

                    return Ok jobResult

                | HttpStatusCode.NotFound ->
                    // The blob URI carries a SAS token in its query string: only the path is reported.
                    return
                        Error(
                            QuantumError.AzureError(
                                AzureQuantumError.UnknownError(
                                    404,
                                    $"Result blob not found at %s{withoutQuery blobUri}"
                                )
                            )
                        )

                | HttpStatusCode.Unauthorized ->
                    return Error(QuantumError.AzureError AzureQuantumError.InvalidCredentials)

                | _ ->
                    let! errorBody = response.Content.ReadAsStringAsync()

                    return
                        Error(
                            QuantumError.AzureError(
                                AzureQuantumError.UnknownError(int response.StatusCode, withoutSasSignature errorBody)
                            )
                        )

            with ex ->
                return Error(requestException ex)
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
    /// - timeout: Maximum polling time
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

            match! submitJobAsync httpClient workspaceUrl submission with
            | Error err -> return Error err
            | Ok jobId ->
                // Step 2: Poll until complete

                match! pollJobUntilCompleteAsync httpClient workspaceUrl jobId timeout cancellationToken with
                | Error err -> return Error err
                | Ok job ->
                    // Step 3: Get results if available
                    match job.OutputDataUri with
                    | None ->
                        return
                            Error(
                                QuantumError.AzureError(
                                    AzureQuantumError.UnknownError(500, "Job completed but no output URI available")
                                )
                            )
                    | Some uri -> return! getJobResultAsync httpClient uri
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
    /// - timeout: Maximum polling time
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

            match! submitJobAsync httpClient workspaceUrl submission with
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
    let getResultFromJobAsync (httpClient: HttpClient) (job: QuantumJob) : Task<Result<JobResult, QuantumError>> =
        task {
            match job.OutputDataUri with
            | None ->
                return
                    Error(
                        QuantumError.AzureError(
                            AzureQuantumError.UnknownError(500, $"Job %s{job.JobId} has no output URI")
                        )
                    )
            | Some uri -> return! getJobResultAsync httpClient uri
        }
