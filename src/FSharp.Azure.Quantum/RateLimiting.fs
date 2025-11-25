namespace FSharp.Azure.Quantum.Core

open System
open System.Net.Http
open System.Threading
open System.Threading.Tasks
open Microsoft.Extensions.Logging

/// Rate Limiting and Throttling Module
/// 
/// Implements client-side rate limiting and exponential backoff for Azure Quantum API.
/// Parses x-ms-ratelimit headers and handles 429 responses.
///
/// ⚠️ CRITICAL: ALL rate limiting code in this SINGLE FILE for AI context optimization
module RateLimiting =
    
    // ============================================================================
    // 1. TYPES AND RECORDS (Primitives, no dependencies)
    // ============================================================================
    
    /// Rate limit information from Azure Quantum API headers
    type RateLimitInfo = {
        /// Number of requests remaining in current window
        Remaining: int
        
        /// Total request limit for the window
        Limit: int
        
        /// Time when the rate limit window resets
        ResetTime: DateTimeOffset
    }
    
    // ============================================================================
    // 2. HEADER PARSING
    // ============================================================================
    
    /// Parse rate limit headers from HTTP response
    let parseRateLimitHeaders (response: HttpResponseMessage) : RateLimitInfo option =
        let tryGetHeader name =
            match response.Headers.TryGetValues(name) with
            | true, values -> 
                values 
                |> Seq.tryHead 
                |> Option.bind (fun v -> 
                    match System.Int32.TryParse(v) with
                    | true, parsed -> Some parsed
                    | false, _ -> None)
            | false, _ -> None
        
        let remaining = tryGetHeader "x-ms-ratelimit-remaining"
        let limit = tryGetHeader "x-ms-ratelimit-limit"
        
        match remaining, limit with
        | Some r, Some l ->
            // If no reset time provided, assume 1 minute window
            let resetTime = DateTimeOffset.UtcNow.AddMinutes(1.0)
            Some { Remaining = r; Limit = l; ResetTime = resetTime }
        | _ -> None
    
    // ============================================================================
    // 3. RATE LIMITER CLASS
    // ============================================================================
    
    /// Rate limiter with mutable state tracking
    type RateLimiter() =
        let mutable currentState: RateLimitInfo option = None
        let lockObj = obj()
        
        /// Update rate limit state from new information
        member this.UpdateState(info: RateLimitInfo) =
            lock lockObj (fun () ->
                currentState <- Some info
            )
        
        /// Get current rate limit state
        member this.GetCurrentState() : RateLimitInfo option =
            lock lockObj (fun () ->
                currentState
            )
        
        /// Check if we should throttle based on current state
        /// Returns true if remaining requests < 10
        member this.ShouldThrottle() : bool =
            lock lockObj (fun () ->
                match currentState with
                | Some info -> info.Remaining < 10
                | None -> false
            )
    
    // ============================================================================
    // 4. EXPONENTIAL BACKOFF
    // ============================================================================
    
    /// Calculate exponential backoff delay in milliseconds
    /// Start at 1s, double each attempt, cap at 60s
    let calculateExponentialBackoff (attemptNumber: int) : int =
        let baseDelayMs = 1000  // 1 second
        let maxDelayMs = 60000  // 60 seconds
        
        // Calculate: 1000 * 2^(attemptNumber - 1)
        let delay = baseDelayMs * (pown 2 (attemptNumber - 1))
        
        // Cap at maximum
        min delay maxDelayMs
