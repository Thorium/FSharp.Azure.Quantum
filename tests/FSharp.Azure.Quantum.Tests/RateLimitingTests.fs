module FSharp.Azure.Quantum.Tests.RateLimitingTests

open Xunit
open System
open System.Net.Http
open System.Threading.Tasks
open FSharp.Azure.Quantum.Core.RateLimiting

// ============================================================================
// TDD Cycle #1: RateLimitInfo Type Tests
// ============================================================================

[<Fact>]
let ``RateLimitInfo should store remaining requests, limit, and reset time`` () =
    // Arrange
    let remaining = 45
    let limit = 60
    let resetTime = DateTimeOffset.UtcNow.AddMinutes(1.0)
    
    // Act
    let info = { Remaining = remaining; Limit = limit; ResetTime = resetTime }
    
    // Assert
    Assert.Equal(remaining, info.Remaining)
    Assert.Equal(limit, info.Limit)
    Assert.Equal(resetTime, info.ResetTime)

// ============================================================================
// TDD Cycle #2: Parse Rate Limit Headers
// ============================================================================

[<Fact>]
let ``parseRateLimitHeaders should extract rate limit info from HTTP headers`` () =
    // Arrange
    let response = new HttpResponseMessage()
    response.Headers.Add("x-ms-ratelimit-remaining", "45")
    response.Headers.Add("x-ms-ratelimit-limit", "60")
    
    // Act
    let result = parseRateLimitHeaders response
    
    // Assert
    match result with
    | Some info ->
        Assert.Equal(45, info.Remaining)
        Assert.Equal(60, info.Limit)
    | None ->
        Assert.True(false, "Expected Some(RateLimitInfo) but got None")

// ============================================================================
// TDD Cycle #3: RateLimiter State Tracking
// ============================================================================

[<Fact>]
let ``RateLimiter should track rate limit state across requests`` () =
    // Arrange
    let limiter = RateLimiter()
    let info1 = { Remaining = 50; Limit = 60; ResetTime = DateTimeOffset.UtcNow.AddMinutes(1.0) }
    let info2 = { Remaining = 49; Limit = 60; ResetTime = DateTimeOffset.UtcNow.AddMinutes(1.0) }
    
    // Act
    limiter.UpdateState(info1)
    let state1 = limiter.GetCurrentState()
    
    limiter.UpdateState(info2)
    let state2 = limiter.GetCurrentState()
    
    // Assert
    match state1, state2 with
    | Some s1, Some s2 ->
        Assert.Equal(50, s1.Remaining)
        Assert.Equal(49, s2.Remaining)
    | _ ->
        Assert.True(false, "Expected rate limiter to track state")

// ============================================================================
// TDD Cycle #4: Throttling Decision Logic
// ============================================================================

[<Fact>]
let ``RateLimiter shouldThrottle returns true when approaching rate limit`` () =
    // Arrange
    let limiter = RateLimiter()
    let info = { Remaining = 5; Limit = 60; ResetTime = DateTimeOffset.UtcNow.AddMinutes(1.0) }
    limiter.UpdateState(info)
    
    // Act
    let shouldThrottle = limiter.ShouldThrottle()
    
    // Assert - should throttle when remaining < 10
    Assert.True(shouldThrottle, "Should throttle when less than 10 requests remaining")

[<Fact>]
let ``RateLimiter shouldThrottle returns false when plenty of requests remain`` () =
    // Arrange
    let limiter = RateLimiter()
    let info = { Remaining = 50; Limit = 60; ResetTime = DateTimeOffset.UtcNow.AddMinutes(1.0) }
    limiter.UpdateState(info)
    
    // Act
    let shouldThrottle = limiter.ShouldThrottle()
    
    // Assert - should NOT throttle when plenty of requests remain
    Assert.False(shouldThrottle, "Should not throttle when 50 requests remaining")

// ============================================================================
// TDD Cycle #5: Exponential Backoff Calculation
// ============================================================================

[<Fact>]
let ``calculateExponentialBackoff should return increasing delays`` () =
    // Act
    let delay1 = calculateExponentialBackoff 1
    let delay2 = calculateExponentialBackoff 2
    let delay3 = calculateExponentialBackoff 3
    let delay4 = calculateExponentialBackoff 4
    let delay5 = calculateExponentialBackoff 5
    let delay6 = calculateExponentialBackoff 6
    let delay7 = calculateExponentialBackoff 7  // Should be capped at 60s
    
    // Assert - exponential backoff: 1s, 2s, 4s, 8s, 16s, 32s, 60s (max)
    Assert.Equal(1000, delay1)   // 1 second
    Assert.Equal(2000, delay2)   // 2 seconds
    Assert.Equal(4000, delay3)   // 4 seconds
    Assert.Equal(8000, delay4)   // 8 seconds
    Assert.Equal(16000, delay5)  // 16 seconds
    Assert.Equal(32000, delay6)  // 32 seconds
    Assert.Equal(60000, delay7)  // 60 seconds (capped)

// ============================================================================
// TDD Cycle #6: ThrottlingHandler Integration
// ============================================================================

[<Fact>]
let ``ThrottlingHandler should parse rate limit headers from responses`` () =
    async {
        // Arrange
        let testHandler = 
            { new HttpMessageHandler() with
                member _.SendAsync(request, ct) =
                    task {
                        let response = new HttpResponseMessage(Net.HttpStatusCode.OK)
                        response.Headers.Add("x-ms-ratelimit-remaining", "45")
                        response.Headers.Add("x-ms-ratelimit-limit", "60")
                        return response
                    }
            }
        
        let throttlingHandler = new ThrottlingHandler(testHandler)
        let client = new HttpClient(throttlingHandler)
        
        // Act
        let! response = client.GetAsync("https://test.example.com") |> Async.AwaitTask
        
        // Assert
        let limiter = throttlingHandler.GetRateLimiter()
        let state = limiter.GetCurrentState()
        
        match state with
        | Some info ->
            Assert.Equal(45, info.Remaining)
            Assert.Equal(60, info.Limit)
        | None ->
            Assert.True(false, "Expected rate limiter to have state after response")
    } |> Async.RunSynchronously

// ============================================================================
// TDD Cycle #7: 429 Response Handling
// ============================================================================

[<Fact>]
let ``ThrottlingHandler should handle 429 responses gracefully`` () =
    async {
        // Arrange
        let mutable callCount = 0
        let testHandler = 
            { new HttpMessageHandler() with
                member _.SendAsync(request, ct) =
                    task {
                        callCount <- callCount + 1
                        // First call returns 429, second returns OK
                        let response = 
                            if callCount = 1 then
                                let r = new HttpResponseMessage(Net.HttpStatusCode.TooManyRequests)
                                r.Headers.Add("Retry-After", "1")
                                r
                            else
                                new HttpResponseMessage(Net.HttpStatusCode.OK)
                        return response
                    }
            }
        
        let throttlingHandler = new ThrottlingHandler(testHandler)
        let client = new HttpClient(throttlingHandler)
        
        // Act
        let! response = client.GetAsync("https://test.example.com") |> Async.AwaitTask
        
        // Assert - should have made initial request and gotten 429
        Assert.Equal(Net.HttpStatusCode.TooManyRequests, response.StatusCode)
        Assert.Equal(1, callCount)  // Only one call made (backoff happens after response)
    } |> Async.RunSynchronously
