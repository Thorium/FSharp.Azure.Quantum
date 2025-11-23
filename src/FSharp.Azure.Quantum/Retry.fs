namespace FSharp.Azure.Quantum.Core

open System
open System.Net
open System.Threading
open FSharp.Azure.Quantum.Core.Types

module Retry =

    /// Retry configuration
    type RetryConfig =
        { MaxAttempts: int
          InitialDelayMs: int
          MaxDelayMs: int
          JitterFactor: float }

    /// Default retry configuration
    let defaultConfig =
        { MaxAttempts = 3
          InitialDelayMs = 500
          MaxDelayMs = 4000
          JitterFactor = 0.2 }

    /// Check if HTTP status code indicates a transient error
    let isTransientStatusCode (statusCode: HttpStatusCode) =
        match statusCode with
        | HttpStatusCode.RequestTimeout
        | HttpStatusCode.TooManyRequests
        | HttpStatusCode.InternalServerError
        | HttpStatusCode.BadGateway
        | HttpStatusCode.ServiceUnavailable
        | HttpStatusCode.GatewayTimeout -> true
        | _ -> false

    /// Check if QuantumError is transient (retriable)
    let isTransientError =
        function
        | QuantumError.ServiceUnavailable _
        | QuantumError.RateLimited _
        | QuantumError.NetworkTimeout _ -> true
        | QuantumError.UnknownError(statusCode, _) -> isTransientStatusCode (enum<HttpStatusCode> statusCode)
        | _ -> false

    /// Calculate delay with exponential backoff and jitter
    let calculateDelay (config: RetryConfig) (attemptNumber: int) =
        let random = Random()

        // Exponential backoff: initialDelay * 2^(attemptNumber - 1)
        let baseDelay = float config.InitialDelayMs * (2.0 ** float (attemptNumber - 1))
        let cappedDelay = min baseDelay (float config.MaxDelayMs)

        // Add jitter: delay * (1 ± jitterFactor)
        let jitterRange = cappedDelay * config.JitterFactor
        let jitter = (random.NextDouble() * 2.0 - 1.0) * jitterRange
        let finalDelay = cappedDelay + jitter

        max 0 (int finalDelay)

    /// Categorize HTTP status code into specific QuantumError
    let categorizeHttpError (statusCode: HttpStatusCode) (responseBody: string) =
        match statusCode with
        | HttpStatusCode.Unauthorized
        | HttpStatusCode.Forbidden -> QuantumError.InvalidCredentials

        | HttpStatusCode.TooManyRequests -> QuantumError.RateLimited(TimeSpan.FromSeconds(60.0))

        | HttpStatusCode.ServiceUnavailable -> QuantumError.ServiceUnavailable(Some(TimeSpan.FromSeconds(30.0)))

        | HttpStatusCode.RequestTimeout
        | HttpStatusCode.GatewayTimeout -> QuantumError.NetworkTimeout(0)

        | HttpStatusCode.BadRequest ->
            // Parse error message to detect specific errors
            if responseBody.Contains("InvalidCircuit") || responseBody.Contains("invalid") then
                QuantumError.InvalidCircuit([ responseBody ])
            elif responseBody.Contains("quota") || responseBody.Contains("Quota") then
                QuantumError.QuotaExceeded("unknown")
            else
                QuantumError.UnknownError(int statusCode, responseBody)

        | HttpStatusCode.NotFound ->
            if responseBody.Contains("backend") || responseBody.Contains("Backend") then
                QuantumError.BackendNotFound("unknown")
            else
                QuantumError.UnknownError(int statusCode, responseBody)

        | _ -> QuantumError.UnknownError(int statusCode, responseBody)

    /// Execute async operation with retry logic (functional, recursive approach)
    let rec private retryLoop<'T>
        (config: RetryConfig)
        (operation: CancellationToken -> Async<Result<'T, QuantumError>>)
        (ct: CancellationToken)
        (attempt: int)
        : Async<Result<'T, QuantumError>> =
        async {

            if ct.IsCancellationRequested then
                return Error(QuantumError.UnknownError(0, "Operation cancelled"))
            else
                // Execute operation
                let! result = operation ct

                match result with
                | Ok value -> return Ok value

                | Error error when isTransientError error && attempt < config.MaxAttempts ->
                    // Transient error and retries remaining - wait and retry
                    let delay = calculateDelay config attempt
                    do! Async.Sleep delay
                    return! retryLoop config operation ct (attempt + 1)

                | Error error ->
                    // Non-transient error or max attempts reached
                    return Error error
        }

    /// Execute async operation with retry logic
    let executeWithRetry<'T>
        (config: RetryConfig)
        (operation: CancellationToken -> Async<Result<'T, QuantumError>>)
        (ct: CancellationToken)
        : Async<Result<'T, QuantumError>> =
        retryLoop config operation ct 1
