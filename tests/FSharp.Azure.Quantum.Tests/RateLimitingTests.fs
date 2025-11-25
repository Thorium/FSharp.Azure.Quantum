module FSharp.Azure.Quantum.Tests.RateLimitingTests

open Xunit
open System
open System.Net.Http
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
