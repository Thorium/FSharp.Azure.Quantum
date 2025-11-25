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
    
    /// Configuration for batch accumulation behavior.
    /// 
    /// Controls when batches are submitted based on size and timeout constraints.
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
    
    // ============================================================================
    // BATCH ACCUMULATOR
    // ============================================================================
    
    /// Thread-safe batch accumulator with size and timeout-based triggering.
    /// 
    /// Accumulates items until maxBatchSize is reached or timeout expires,
    /// then returns the batch. Uses lock-based synchronization for thread safety.
    /// 
    /// Example:
    /// <code>
    /// let config = BatchConfig.defaultConfig
    /// let accumulator = BatchAccumulator&lt;string&gt;(config)
    /// match accumulator.Add("item") with
    /// | Some batch -> // Process batch
    /// | None -> // Keep accumulating
    /// </code>
    type BatchAccumulator<'T>(config: BatchConfig) =
        let mutable batch : 'T list = []
        let mutable batchStartTime : DateTimeOffset option = None
        let lockObj = obj()
        
        /// Add an item to the batch
        /// 
        /// Returns Some(batch) if size trigger activates, None otherwise
        member _.Add(item: 'T) : 'T list option =
            lock lockObj (fun () ->
                // Start timer on first item
                if batchStartTime.IsNone then
                    batchStartTime <- Some DateTimeOffset.UtcNow
                
                // Add item to batch
                batch <- item :: batch
                
                // Check size trigger
                if batch.Length >= config.MaxBatchSize then
                    let result = List.rev batch
                    batch <- []
                    batchStartTime <- None
                    Some result  // Trigger batch submission
                else
                    None  // Keep accumulating
            )
