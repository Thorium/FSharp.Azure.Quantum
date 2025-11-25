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
                let mutable allResults : 'TResult list = []
                let mutable currentBatch : 'TCircuit list = []
                
                for circuit in circuits do
                    currentBatch <- circuit :: currentBatch
                    
                    // Check if batch is full
                    if currentBatch.Length >= config.MaxBatchSize then
                        let batch = List.rev currentBatch
                        let! results = submitBatch batch
                        allResults <- allResults @ results
                        currentBatch <- []
                
                // Submit remaining circuits
                if not currentBatch.IsEmpty then
                    let batch = List.rev currentBatch
                    let! results = submitBatch batch
                    allResults <- allResults @ results
                
                return allResults
        }
