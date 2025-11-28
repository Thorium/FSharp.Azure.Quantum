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
        
        /// Try to flush the current batch if timeout has expired
        /// 
        /// Returns Some(batch) if timeout expired and batch is non-empty, None otherwise
        member _.TryFlush() : 'T list option =
            lock lockObj (fun () ->
                match batchStartTime with
                | None -> None  // No batch in progress
                | Some startTime ->
                    let elapsed = DateTimeOffset.UtcNow - startTime
                    
                    // Check timeout trigger
                    if elapsed >= config.Timeout && batch.Length > 0 then
                        let result = List.rev batch
                        batch <- []
                        batchStartTime <- None
                        Some result  // Trigger batch submission
                    else
                        None  // Keep accumulating
            )
    
    // ============================================================================
    // ASYNC BATCH SUBMISSION
    // ============================================================================
    
    /// Batch multiple circuits and submit them asynchronously
    /// 
    /// Takes a list of circuits and submits them in batches according to the
    /// configured batch size. Uses the provided submission function to process
    /// each batch.
    /// 
    /// Example:
    /// <code>
    /// let circuits = ["circuit1"; "circuit2"; "circuit3"]
    /// let results = 
    ///     batchCircuitsAsync 
    ///         BatchConfig.defaultConfig 
    ///         circuits 
    ///         (fun batch -> async { return submitToAzure batch })
    ///     |> Async.RunSynchronously
    /// </code>
    let batchCircuitsAsync<'TCircuit, 'TResult>
        (config: BatchConfig)
        (circuits: 'TCircuit list)
        (submitBatch: 'TCircuit list -> Async<'TResult list>)
        : Async<'TResult list> =
        async {
            if not config.Enabled || circuits.IsEmpty then
                return []
            else
                // Split circuits into batches using functional approach
                let batches = 
                    circuits 
                    |> List.chunkBySize config.MaxBatchSize
                
                // Submit batches sequentially and collect results
                let! allBatchResults = 
                    batches
                    |> List.map submitBatch
                    |> Async.Sequential
                
                // Flatten batch results into single list
                return allBatchResults |> Array.toList |> List.collect id
        }
    
    // ============================================================================
    // BATCH METRICS
    // ============================================================================
    
    /// Metrics tracking for batch execution monitoring
    /// 
    /// Tracks batch sizes, execution times, and efficiency metrics for
    /// monitoring and debugging batch operations.
    type BatchMetrics() =
        let mutable totalCircuits = 0
        let mutable batchCount = 0
        let mutable totalExecutionTime = 0.0
        let mutable batchSizes : int list = []
        let lockObj = obj()
        
        /// Record a completed batch
        member _.RecordBatch(batchSize: int, executionTimeMs: float) =
            lock lockObj (fun () ->
                totalCircuits <- totalCircuits + batchSize
                batchCount <- batchCount + 1
                totalExecutionTime <- totalExecutionTime + executionTimeMs
                batchSizes <- batchSize :: batchSizes
            )
        
        /// Total number of circuits processed
        member _.TotalCircuits = totalCircuits
        
        /// Total number of batches submitted
        member _.BatchCount = batchCount
        
        /// Total execution time across all batches (ms)
        member _.TotalExecutionTimeMs = totalExecutionTime
        
        /// Average batch size
        member _.AverageBatchSize =
            if batchCount = 0 then 0.0
            else float totalCircuits / float batchCount
        
        /// Calculate batch efficiency (0.0 - 1.0)
        /// 
        /// Efficiency is the ratio of actual batch size to maximum batch size,
        /// averaged across all batches.
        member this.GetEfficiency(maxBatchSize: int) =
            if batchCount = 0 || maxBatchSize <= 0 then 0.0
            else
                let avgSize = this.AverageBatchSize
                avgSize / float maxBatchSize
    
    /// BatchMetrics module with factory functions
    module BatchMetrics =
        
        /// Create a new BatchMetrics instance
        let create() = BatchMetrics()
