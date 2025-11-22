namespace FSharp.Azure.Quantum.Core

open System
open System.Net
open System.Threading
open FSharp.Azure.Quantum.Core.Types

module Retry =
    
    /// Retry configuration
    type RetryConfig = {
        /// Maximum retry attempts (default: 3)
        MaxAttempts: int
        
        /// Initial delay in milliseconds (default: 500ms)
        InitialDelayMs: int
        
        /// Maximum delay in milliseconds (default: 4000ms)
        MaxDelayMs: int
        
        /// Jitter factor (0.0 - 1.0, default: 0.2 for 20% jitter)
        JitterFactor: float
    }
    
    /// Default retry configuration
    let defaultConfig = {
        MaxAttempts = 3
        InitialDelayMs = 500
        MaxDelayMs = 4000
        JitterFactor = 0.2
    }
    
    /// Check if HTTP status code indicates a transient error
    let isTransientError (statusCode: HttpStatusCode) =
        match statusCode with
        | HttpStatusCode.RequestTimeout          // 408
        | HttpStatusCode.TooManyRequests         // 429
        | HttpStatusCode.InternalServerError     // 500
        | HttpStatusCode.BadGateway              // 502
        | HttpStatusCode.ServiceUnavailable      // 503
        | HttpStatusCode.GatewayTimeout -> true  // 504
        | _ -> false
    
    /// Check if QuantumError is transient
    let isTransientQuantumError (error: QuantumError) =
        match error with
        | QuantumError.ServiceUnavailable _
        | QuantumError.RateLimited _
        | QuantumError.NetworkTimeout _ -> true
        | QuantumError.UnknownError(statusCode, _) -> 
            isTransientError (enum<HttpStatusCode> statusCode)
        | _ -> false
    
    /// Calculate delay with exponential backoff and jitter
    let calculateDelay (config: RetryConfig) (attemptNumber: int) =
        let random = Random()
        
        // Exponential backoff: initialDelay * 2^(attemptNumber - 1)
        let baseDelay = float config.InitialDelayMs * Math.Pow(2.0, float (attemptNumber - 1))
        let cappedDelay = min baseDelay (float config.MaxDelayMs)
        
        // Add jitter: delay * (1 ± jitterFactor)
        let jitterRange = cappedDelay * config.JitterFactor
        let jitter = (random.NextDouble() * 2.0 - 1.0) * jitterRange // -jitterRange to +jitterRange
        let finalDelay = cappedDelay + jitter
        
        int (max 0.0 finalDelay)
    
    /// Categorize HTTP status code into QuantumError
    let categorizeHttpError (statusCode: HttpStatusCode) (responseBody: string) =
        match statusCode with
        | HttpStatusCode.Unauthorized 
        | HttpStatusCode.Forbidden -> 
            QuantumError.InvalidCredentials
            
        | HttpStatusCode.TooManyRequests ->
            // Try to parse Retry-After header (simplified here)
            QuantumError.RateLimited(TimeSpan.FromSeconds(60.0))
            
        | HttpStatusCode.ServiceUnavailable ->
            QuantumError.ServiceUnavailable(Some(TimeSpan.FromSeconds(30.0)))
            
        | HttpStatusCode.RequestTimeout
        | HttpStatusCode.GatewayTimeout ->
            QuantumError.NetworkTimeout(0)
            
        | HttpStatusCode.BadRequest ->
            // Try to parse error message to detect specific errors
            if responseBody.Contains("InvalidCircuit") || responseBody.Contains("invalid") then
                QuantumError.InvalidCircuit([responseBody])
            elif responseBody.Contains("quota") || responseBody.Contains("Quota") then
                QuantumError.QuotaExceeded("unknown")
            else
                QuantumError.UnknownError(int statusCode, responseBody)
                
        | HttpStatusCode.NotFound ->
            if responseBody.Contains("backend") || responseBody.Contains("Backend") then
                QuantumError.BackendNotFound("unknown")
            else
                QuantumError.UnknownError(int statusCode, responseBody)
                
        | _ ->
            QuantumError.UnknownError(int statusCode, responseBody)
    
    /// Execute async operation with retry logic
    let executeWithRetry<'T> 
        (config: RetryConfig) 
        (operation: CancellationToken -> Async<Result<'T, QuantumError>>) 
        (cancellationToken: CancellationToken) 
        : Async<Result<'T, QuantumError>> = async {
        
        let mutable attempt = 0
        let mutable result: Result<'T, QuantumError> option = None
        let mutable shouldRetry = true
        
        while shouldRetry && attempt < config.MaxAttempts && not cancellationToken.IsCancellationRequested do
            attempt <- attempt + 1
            
            // Execute operation
            let! operationResult = operation cancellationToken
            
            match operationResult with
            | Ok value ->
                result <- Some (Ok value)
                shouldRetry <- false
                
            | Error error ->
                // Check if error is transient and we have retries left
                if isTransientQuantumError error && attempt < config.MaxAttempts then
                    // Wait before retry with exponential backoff + jitter
                    let delay = calculateDelay config attempt
                    do! Async.Sleep delay
                    // Continue loop for retry
                else
                    // Non-transient error or max attempts reached
                    result <- Some (Error error)
                    shouldRetry <- false
        
        return result |> Option.defaultValue (Error (QuantumError.UnknownError(0, "Retry loop ended unexpectedly")))
    }
