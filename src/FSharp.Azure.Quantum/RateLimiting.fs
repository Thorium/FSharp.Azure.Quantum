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
