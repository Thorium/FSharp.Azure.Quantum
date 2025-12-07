namespace FSharp.Azure.Quantum.Core

open System
open System.Collections.Concurrent

/// Batching module for optimizing Azure Quantum job submissions
/// 
/// Implements automatic batching with timeout-based accumulation to amortize
/// Azure Quantum's 30s-5min communication overhead across multiple circuits.
module Batching =
    
    open FSharp.Azure.Quantum.Core
    
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
        let create maxBatchSize timeout enabled : QuantumResult<BatchConfig> =
            if maxBatchSize <= 0 then
                Error (QuantumError.ValidationError ("MaxBatchSize", "must be positive"))
            elif timeout <= TimeSpan.Zero then
                Error (QuantumError.ValidationError ("Timeout", "must be positive"))
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
    /// then returns the batch. Uses ConcurrentQueue for lock-free enqueueing
    /// and a single lock only when flushing to ensure atomicity.
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
        // Use ConcurrentQueue for lock-free thread-safe enqueueing
        let queue = ConcurrentQueue<'T>()
        
        // Use mutable for batch timing (idiomatic F# with lock-based sync)
        let mutable batchStartTime : DateTimeOffset option = None
        let lockObj = obj()
        
        /// Atomically drain up to maxCount items from the queue
        let drainQueue(maxCount: int option) =
            let items = ResizeArray<'T>()
            let mutable item = Unchecked.defaultof<'T>
            let mutable count = 0
            let limit = defaultArg maxCount System.Int32.MaxValue
            
            while count < limit && queue.TryDequeue(&item) do
                items.Add(item)
                count <- count + 1
            
            items |> List.ofSeq
        
        /// Add an item to the batch
        /// 
        /// Returns Some(batch) if size trigger activates, None otherwise
        member _.Add(item: 'T) : 'T list option =
            // Enqueue is lock-free and thread-safe
            queue.Enqueue(item)
            
            // Lock for timer initialization and batch extraction
            lock lockObj (fun () ->
                // Start timer on first item
                if batchStartTime.IsNone then
                    batchStartTime <- Some DateTimeOffset.UtcNow
                
                // Check size trigger (inside lock to get accurate count)
                if queue.Count >= config.MaxBatchSize then
                    // Drain exactly MaxBatchSize items to prevent oversized batches
                    let batch = drainQueue(Some config.MaxBatchSize)
                    
                    // Reset timer for remaining items or clear if queue is empty
                    if queue.IsEmpty then
                        batchStartTime <- None
                    else
                        // Restart timer for remaining items
                        batchStartTime <- Some DateTimeOffset.UtcNow
                    
                    // Only return batch if we got items
                    if batch.IsEmpty then None
                    else Some batch
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
                    if elapsed >= config.Timeout && not queue.IsEmpty then
                        // Drain all items on timeout (no size limit)
                        let batch = drainQueue(None)
                        batchStartTime <- None
                        
                        // Only return batch if we got items
                        if batch.IsEmpty then None
                        else Some batch
                    else
                        None  // Keep accumulating
            )
        
        /// Force flush all remaining items regardless of timeout
        /// 
        /// Returns Some(batch) if items present, None if queue empty.
        /// Useful for cleanup and testing scenarios.
        member _.ForceFlush() : 'T list option =
            lock lockObj (fun () ->
                if queue.IsEmpty then
                    None
                else
                    let batch = drainQueue(None)
                    batchStartTime <- None
                    if batch.IsEmpty then None
                    else Some batch
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
    
    /// Immutable snapshot of batch execution metrics
    /// 
    /// Represents a point-in-time view of batch performance statistics.
    type BatchMetrics = {
        /// Total number of circuits processed across all batches
        TotalCircuits: int
        
        /// Total number of batches submitted
        BatchCount: int
        
        /// Total execution time across all batches (milliseconds)
        TotalExecutionTimeMs: float
        
        /// Individual batch sizes in submission order
        BatchSizes: int list
    }
    
    /// BatchMetrics module with functional operations
    module BatchMetrics =
        
        /// Empty metrics (initial state)
        let empty : BatchMetrics = {
            TotalCircuits = 0
            BatchCount = 0
            TotalExecutionTimeMs = 0.0
            BatchSizes = []
        }
        
        /// Create a new BatchMetrics instance (alias for empty)
        let create() = empty
        
        /// Record a completed batch and return updated metrics
        /// 
        /// Pure function - returns new metrics without mutating the original
        let recordBatch (batchSize: int) (executionTimeMs: float) (metrics: BatchMetrics) : BatchMetrics =
            {
                TotalCircuits = metrics.TotalCircuits + batchSize
                BatchCount = metrics.BatchCount + 1
                TotalExecutionTimeMs = metrics.TotalExecutionTimeMs + executionTimeMs
                BatchSizes = batchSize :: metrics.BatchSizes
            }
        
        /// Calculate average batch size
        let averageBatchSize (metrics: BatchMetrics) : float =
            if metrics.BatchCount = 0 then 0.0
            else float metrics.TotalCircuits / float metrics.BatchCount
        
        /// Calculate batch efficiency (0.0 - 1.0)
        /// 
        /// Efficiency is the ratio of actual batch size to maximum batch size,
        /// averaged across all batches.
        let getEfficiency (maxBatchSize: int) (metrics: BatchMetrics) : float =
            if metrics.BatchCount = 0 || maxBatchSize <= 0 then 0.0
            else
                let avgSize = averageBatchSize metrics
                avgSize / float maxBatchSize
    
    /// Thread-safe mutable wrapper for BatchMetrics (for stateful scenarios)
    /// 
    /// Wraps immutable BatchMetrics with thread-safe mutation for cases where
    /// you need a shared accumulator. Prefer using immutable BatchMetrics directly
    /// when possible.
    /// 
    /// Performance Note:
    /// Each property access acquires a lock and creates a snapshot. For bulk access,
    /// call GetSnapshot() once and read multiple properties from the snapshot:
    /// 
    /// Good (1 lock):
    ///   let snapshot = accumulator.GetSnapshot()
    ///   let total = snapshot.TotalCircuits
    ///   let count = snapshot.BatchCount
    /// 
    /// Suboptimal (2 locks):
    ///   let total = accumulator.TotalCircuits
    ///   let count = accumulator.BatchCount
    type BatchMetricsAccumulator() =
        let mutable metrics = BatchMetrics.empty
        let lockObj = obj()
        
        /// Record a completed batch (thread-safe)
        member _.RecordBatch(batchSize: int, executionTimeMs: float) =
            lock lockObj (fun () ->
                metrics <- BatchMetrics.recordBatch batchSize executionTimeMs metrics
            )
        
        /// Get current metrics snapshot (thread-safe)
        /// 
        /// Returns immutable snapshot of current metrics state.
        /// Recommended for accessing multiple properties to avoid repeated lock acquisition.
        member _.GetSnapshot() : BatchMetrics =
            lock lockObj (fun () -> metrics)
        
        /// Total number of circuits processed (thread-safe)
        /// 
        /// Note: Acquires lock on each access. For bulk property access, use GetSnapshot().
        member this.TotalCircuits = 
            this.GetSnapshot().TotalCircuits
        
        /// Total number of batches submitted (thread-safe)
        /// 
        /// Note: Acquires lock on each access. For bulk property access, use GetSnapshot().
        member this.BatchCount = 
            this.GetSnapshot().BatchCount
        
        /// Total execution time across all batches (ms) (thread-safe)
        /// 
        /// Note: Acquires lock on each access. For bulk property access, use GetSnapshot().
        member this.TotalExecutionTimeMs = 
            this.GetSnapshot().TotalExecutionTimeMs
        
        /// Average batch size (thread-safe)
        /// 
        /// Note: Acquires lock on each access. For bulk property access, use GetSnapshot().
        member this.AverageBatchSize = 
            this.GetSnapshot() |> BatchMetrics.averageBatchSize
        
        /// Calculate batch efficiency (0.0 - 1.0) (thread-safe)
        /// 
        /// Note: Acquires lock on each access. For bulk property access, use GetSnapshot().
        member this.GetEfficiency(maxBatchSize: int) = 
            this.GetSnapshot() |> BatchMetrics.getEfficiency maxBatchSize
