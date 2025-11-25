namespace FSharp.Azure.Quantum.Core

open System

/// Batching module for optimizing Azure Quantum job submissions
/// 
/// Implements automatic batching with timeout-based accumulation to amortize
/// Azure Quantum's 30s-5min communication overhead across multiple circuits.
module Batching =
    
    // ============================================================================
    // BATCH CONFIGURATION
    // ============================================================================
    
    /// Batch configuration
    type BatchConfig = {
        /// Maximum number of items per batch (default: 50)
        MaxBatchSize: int
        
        /// Timeout before submitting partial batch (default: 10s)
        Timeout: TimeSpan
        
        /// Whether batching is enabled (default: true)
        Enabled: bool
    }
    
    /// BatchConfig module with factory functions
    module BatchConfig =
        
        /// Create a batch configuration with validation
        let create maxBatchSize timeout enabled : Result<BatchConfig, string> =
            if maxBatchSize <= 0 then
                Error "MaxBatchSize must be positive"
            elif timeout <= TimeSpan.Zero then
                Error "Timeout must be positive"
            else
                Ok {
                    MaxBatchSize = maxBatchSize
                    Timeout = timeout
                    Enabled = enabled
                }
        
        /// Default batch configuration
        /// - MaxBatchSize: 50
        /// - Timeout: 10 seconds
        /// - Enabled: true
        let defaultConfig : BatchConfig =
            {
                MaxBatchSize = 50
                Timeout = TimeSpan.FromSeconds 10.0
                Enabled = true
            }
