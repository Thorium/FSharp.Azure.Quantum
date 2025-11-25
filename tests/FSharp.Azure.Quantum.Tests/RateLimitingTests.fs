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
