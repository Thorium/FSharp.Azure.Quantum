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
