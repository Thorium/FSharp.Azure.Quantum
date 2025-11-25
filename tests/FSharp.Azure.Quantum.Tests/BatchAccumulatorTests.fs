namespace FSharp.Azure.Quantum.Tests

open System
open Xunit
open FSharp.Azure.Quantum.Core.Batching

module BatchAccumulatorTests =
    
    // ============================================================================
    // TDD CYCLE 1: BatchConfig - Configuration Type with Validation
    // ============================================================================
    
    [<Fact>]
    let ``BatchConfig with valid parameters should succeed`` () =
        // Arrange & Act
        let config = BatchConfig.create 50 (TimeSpan.FromSeconds 10.0) true
        
        // Assert
        match config with
        | Ok c ->
            Assert.Equal(50, c.MaxBatchSize)
            Assert.Equal(TimeSpan.FromSeconds 10.0, c.Timeout)
            Assert.True(c.Enabled)
        | Error msg ->
            Assert.True(false, $"Expected success but got error: {msg}")
    
    [<Fact>]
    let ``BatchConfig with zero batch size should fail`` () =
        // Arrange & Act
        let config = BatchConfig.create 0 (TimeSpan.FromSeconds 10.0) true
        
        // Assert
        match config with
        | Ok _ -> Assert.True(false, "Expected validation error for zero batch size")
        | Error msg -> Assert.Contains("MaxBatchSize must be positive", msg)
    
    [<Fact>]
    let ``BatchConfig with negative batch size should fail`` () =
        // Arrange & Act
        let config = BatchConfig.create -5 (TimeSpan.FromSeconds 10.0) true
        
        // Assert
        match config with
        | Ok _ -> Assert.True(false, "Expected validation error for negative batch size")
        | Error msg -> Assert.Contains("MaxBatchSize must be positive", msg)
    
    [<Fact>]
    let ``BatchConfig with zero timeout should fail`` () =
        // Arrange & Act
        let config = BatchConfig.create 50 TimeSpan.Zero true
        
        // Assert
        match config with
        | Ok _ -> Assert.True(false, "Expected validation error for zero timeout")
        | Error msg -> Assert.Contains("Timeout must be positive", msg)
    
    [<Fact>]
    let ``BatchConfig with negative timeout should fail`` () =
        // Arrange & Act
        let config = BatchConfig.create 50 (TimeSpan.FromSeconds -5.0) true
        
        // Assert
        match config with
        | Ok _ -> Assert.True(false, "Expected validation error for negative timeout")
        | Error msg -> Assert.Contains("Timeout must be positive", msg)
    
    [<Fact>]
    let ``BatchConfig default should have sensible values`` () =
        // Arrange & Act
        let config = BatchConfig.defaultConfig
        
        // Assert
        Assert.Equal(50, config.MaxBatchSize)
        Assert.Equal(TimeSpan.FromSeconds 10.0, config.Timeout)
        Assert.True(config.Enabled)
    
    // ============================================================================
    // TDD CYCLE 2: BatchAccumulator - Size Trigger
    // ============================================================================
    
    [<Fact>]
    let ``BatchAccumulator should accumulate items below max size`` () =
        // Arrange
        let config = BatchConfig.defaultConfig
        let accumulator = BatchAccumulator<string>(config)
        
        // Act
        let result1 = accumulator.Add("item1")
        let result2 = accumulator.Add("item2")
        let result3 = accumulator.Add("item3")
        
        // Assert - Should return None (keep accumulating)
        Assert.True(result1.IsNone, "First item should not trigger batch")
        Assert.True(result2.IsNone, "Second item should not trigger batch")
        Assert.True(result3.IsNone, "Third item should not trigger batch")
    
    [<Fact>]
    let ``BatchAccumulator should trigger on max batch size`` () =
        // Arrange
        let config = { BatchConfig.defaultConfig with MaxBatchSize = 3 }
        let accumulator = BatchAccumulator<string>(config)
        
        // Act
        let result1 = accumulator.Add("item1")
        let result2 = accumulator.Add("item2")
        let result3 = accumulator.Add("item3")  // Should trigger
        
        // Assert
        Assert.True(result1.IsNone)
        Assert.True(result2.IsNone)
        Assert.True(result3.IsSome, "Third item should trigger batch")
        
        match result3 with
        | Some batch ->
            Assert.Equal(3, batch.Length)
            Assert.Equal<string seq>(["item1"; "item2"; "item3"], batch)
        | None -> Assert.True(false, "Expected batch to be returned")
    
    [<Fact>]
    let ``BatchAccumulator should reset after triggering`` () =
        // Arrange
        let config = { BatchConfig.defaultConfig with MaxBatchSize = 2 }
        let accumulator = BatchAccumulator<int>(config)
        
        // Act - First batch
        let _ = accumulator.Add(1)
        let batch1 = accumulator.Add(2)  // Trigger
        
        // Act - Second batch (should start fresh)
        let result1 = accumulator.Add(3)
        let batch2 = accumulator.Add(4)  // Trigger again
        
        // Assert - First batch
        match batch1 with
        | Some batch -> 
            Assert.Equal(2, batch.Length)
            Assert.Equal<int seq>([1; 2], batch)
        | None -> Assert.True(false, "Expected first batch to be returned")
        
        // Assert - Should reset and accumulate again
        Assert.True(result1.IsNone, "After reset, should accumulate again")
        
        // Assert - Second batch
        match batch2 with
        | Some batch -> 
            Assert.Equal(2, batch.Length)
            Assert.Equal<int seq>([3; 4], batch)
        | None -> Assert.True(false, "Expected second batch to be returned")
    
    [<Fact>]
    let ``BatchAccumulator with size 1 should trigger immediately`` () =
        // Arrange
        let config = { BatchConfig.defaultConfig with MaxBatchSize = 1 }
        let accumulator = BatchAccumulator<string>(config)
        
        // Act
        let result = accumulator.Add("item")
        
        // Assert
        match result with
        | Some batch ->
            Assert.Equal(1, batch.Length)
            Assert.Equal<string seq>(["item"], batch)
        | None -> Assert.True(false, "Expected immediate trigger with batch size 1")
    
    // ============================================================================
    // TDD CYCLE 3: BatchAccumulator - Timeout Trigger
    // ============================================================================
    
    [<Fact>]
    let ``BatchAccumulator should trigger on timeout with partial batch`` () =
        // Arrange
        let config = { BatchConfig.defaultConfig with Timeout = TimeSpan.FromMilliseconds 100.0 }
        let accumulator = BatchAccumulator<string>(config)
        
        // Act
        let result1 = accumulator.Add("item1")
        Assert.True(result1.IsNone, "First item should not trigger immediately")
        
        // Wait for timeout
        System.Threading.Thread.Sleep(150)
        
        // Try to trigger timeout by adding another item or checking
        let result2 = accumulator.TryFlush()
        
        // Assert
        match result2 with
        | Some batch ->
            Assert.Equal(1, batch.Length)
            Assert.Equal<string seq>(["item1"], batch)
        | None -> Assert.True(false, "Expected timeout to trigger partial batch")
    
    [<Fact>]
    let ``BatchAccumulator should not trigger before timeout`` () =
        // Arrange
        let config = { BatchConfig.defaultConfig with Timeout = TimeSpan.FromSeconds 10.0 }
        let accumulator = BatchAccumulator<string>(config)
        
        // Act
        let result1 = accumulator.Add("item1")
        System.Threading.Thread.Sleep(50)  // Short delay, well before timeout
        let result2 = accumulator.TryFlush()
        
        // Assert
        Assert.True(result1.IsNone, "Should not trigger immediately")
        Assert.True(result2.IsNone, "Should not trigger before timeout expires")
    
    [<Fact>]
    let ``BatchAccumulator timeout should reset after batch submission`` () =
        // Arrange
        let config = { BatchConfig.defaultConfig with MaxBatchSize = 2; Timeout = TimeSpan.FromMilliseconds 100.0 }
        let accumulator = BatchAccumulator<string>(config)
        
        // Act - First batch (size trigger)
        let _ = accumulator.Add("item1")
        let batch1 = accumulator.Add("item2")  // Size trigger
        
        // Act - Second batch (should have fresh timeout)
        let result1 = accumulator.Add("item3")
        System.Threading.Thread.Sleep(50)  // Not enough time
        let result2 = accumulator.TryFlush()
        
        // Assert
        match batch1 with
        | Some batch -> 
            Assert.Equal(2, batch.Length)
            Assert.Equal<string seq>(["item1"; "item2"], batch)
        | None -> Assert.True(false, "Expected size trigger")
        
        Assert.True(result1.IsNone, "Should accumulate after reset")
        Assert.True(result2.IsNone, "Timeout should be reset after size trigger")
    
    // ============================================================================
    // TDD CYCLE 4: BatchAccumulator - Thread Safety
    // ============================================================================
    
    [<Fact>]
    let ``BatchAccumulator should handle concurrent adds safely`` () =
        // Arrange
        let config = { BatchConfig.defaultConfig with MaxBatchSize = 100 }
        let accumulator = BatchAccumulator<int>(config)
        let mutable batches = []
        let lockObj = obj()
        
        // Act - 10 threads each adding 10 items
        let tasks = 
            [1..10]
            |> List.map (fun threadId ->
                System.Threading.Tasks.Task.Run(fun () ->
                    for i in 1..10 do
                        let item = threadId * 100 + i
                        match accumulator.Add(item) with
                        | Some batch -> 
                            lock lockObj (fun () -> batches <- batch :: batches)
                        | None -> ()
                )
            )
        
        System.Threading.Tasks.Task.WaitAll(tasks |> List.toArray)
        
        // Flush any remaining items
        match accumulator.TryFlush() with
        | Some batch -> batches <- batch :: batches
        | None -> ()
        
        // Assert
        let allItems = batches |> List.collect id
        Assert.Equal(100, allItems.Length)  // All 100 items should be present
        
        // Check no duplicates (each item appears exactly once)
        let distinctItems = allItems |> List.distinct
        Assert.Equal(100, distinctItems.Length)
    
    [<Fact>]
    let ``BatchAccumulator should maintain batch size limit under concurrent load`` () =
        // Arrange
        let maxSize = 10
        let config = { BatchConfig.defaultConfig with MaxBatchSize = maxSize }
        let accumulator = BatchAccumulator<int>(config)
        let mutable batches = []
        let lockObj = obj()
        
        // Act - Multiple threads adding items
        let tasks = 
            [1..50]
            |> List.map (fun item ->
                System.Threading.Tasks.Task.Run(fun () ->
                    match accumulator.Add(item) with
                    | Some batch -> 
                        lock lockObj (fun () -> batches <- batch :: batches)
                    | None -> ()
                )
            )
        
        System.Threading.Tasks.Task.WaitAll(tasks |> List.toArray)
        
        // Assert - All batches should be at max size (except possibly the last partial one)
        let fullBatches = batches |> List.filter (fun b -> b.Length = maxSize)
        Assert.True(fullBatches.Length >= 4, $"Expected at least 4 full batches, got {fullBatches.Length}")
        
        // All batches should be <= maxSize
        batches |> List.iter (fun batch ->
            Assert.True(batch.Length <= maxSize, $"Batch size {batch.Length} exceeds max {maxSize}")
        )
    
    [<Fact>]
    let ``BatchAccumulator concurrent Add and TryFlush should not lose items`` () =
        // Arrange
        let config = { BatchConfig.defaultConfig with MaxBatchSize = 1000; Timeout = TimeSpan.FromMilliseconds 50.0 }
        let accumulator = BatchAccumulator<int>(config)
        let mutable batches = []
        let lockObj = obj()
        
        // Act - Some threads adding, others flushing
        let addTasks = 
            [1..20]
            |> List.map (fun item ->
                System.Threading.Tasks.Task.Run(fun () ->
                    System.Threading.Thread.Sleep(5 * item)  // Stagger additions
                    match accumulator.Add(item) with
                    | Some batch -> 
                        lock lockObj (fun () -> batches <- batch :: batches)
                    | None -> ()
                )
            )
        
        let flushTasks = 
            [1..5]
            |> List.map (fun _ ->
                System.Threading.Tasks.Task.Run(fun () ->
                    for _ in 1..5 do
                        System.Threading.Thread.Sleep(20)
                        match accumulator.TryFlush() with
                        | Some batch -> 
                            lock lockObj (fun () -> batches <- batch :: batches)
                        | None -> ()
                )
            )
        
        let allTasks = addTasks @ flushTasks
        System.Threading.Tasks.Task.WaitAll(allTasks |> List.toArray)
        
        // Final flush
        match accumulator.TryFlush() with
        | Some batch -> batches <- batch :: batches
        | None -> ()
        
        // Assert - All 20 items should be accounted for
        let allItems = batches |> List.collect id
        Assert.Equal(20, allItems.Length)
        
        // Check all expected items present
        let distinctItems = allItems |> List.distinct |> List.sort
        Assert.Equal<int seq>([1..20], distinctItems)
    
    // ============================================================================
    // TDD CYCLE 5: batchCircuitsAsync - Async Batch Submission Function
    // ============================================================================
    
    [<Fact>]
    let ``batchCircuitsAsync with empty list should return empty results`` () =
        // Arrange
        let config = BatchConfig.defaultConfig
        let circuits : string list = []
        
        // Act
        let results = 
            batchCircuitsAsync 
                config 
                circuits 
                (fun batch -> async { return batch |> List.map (fun c -> c + "_result") })
            |> Async.RunSynchronously
        
        // Assert
        Assert.Empty(results)
    
    [<Fact>]
    let ``batchCircuitsAsync with single circuit should return single result`` () =
        // Arrange
        let config = BatchConfig.defaultConfig
        let circuits = ["circuit1"]
        
        // Act
        let results = 
            batchCircuitsAsync 
                config 
                circuits 
                (fun batch -> async { return batch |> List.map (fun c -> c + "_result") })
            |> Async.RunSynchronously
        
        // Assert
        Assert.Equal(1, results.Length)
        Assert.Equal("circuit1_result", results.[0])
    
    [<Fact>]
    let ``batchCircuitsAsync should batch multiple circuits based on size limit`` () =
        // Arrange
        let config = { BatchConfig.defaultConfig with MaxBatchSize = 3 }
        let circuits = ["c1"; "c2"; "c3"; "c4"; "c5"]
        let mutable batchCount = 0
        
        // Mock submission function that tracks batch sizes
        let mockSubmit batch = 
            async {
                batchCount <- batchCount + 1
                return batch |> List.map (fun c -> c + "_result")
            }
        
        // Act
        let results = 
            batchCircuitsAsync config circuits mockSubmit
            |> Async.RunSynchronously
        
        // Assert - Should create 2 batches (3 + 2 circuits)
        Assert.Equal(2, batchCount)
        Assert.Equal(5, results.Length)
        Assert.Equal<string seq>(
            ["c1_result"; "c2_result"; "c3_result"; "c4_result"; "c5_result"], 
            results
        )
    
    [<Fact>]
    let ``batchCircuitsAsync should preserve circuit order in results`` () =
        // Arrange
        let config = { BatchConfig.defaultConfig with MaxBatchSize = 2 }
        let circuits = ["A"; "B"; "C"; "D"]
        
        // Act
        let results = 
            batchCircuitsAsync 
                config 
                circuits 
                (fun batch -> async { return batch |> List.map (fun c -> c + "_result") })
            |> Async.RunSynchronously
        
        // Assert - Order should be preserved
        Assert.Equal<string seq>(
            ["A_result"; "B_result"; "C_result"; "D_result"], 
            results
        )
    
    [<Fact>]
    let ``batchCircuitsAsync should handle batch submission errors gracefully`` () =
        // Arrange
        let config = { BatchConfig.defaultConfig with MaxBatchSize = 3 }
        let circuits = ["c1"; "c2"; "c3"; "c4"]
        
        let mutable batchNumber = 0
        let mockSubmit batch = 
            async {
                batchNumber <- batchNumber + 1
                if batchNumber = 1 then
                    // First batch succeeds
                    return batch |> List.map (fun c -> c + "_result")
                else
                    // Second batch fails
                    return failwith "Batch submission failed"
            }
        
        // Act & Assert
        let ex = 
            Assert.Throws<System.Exception>(fun () ->
                batchCircuitsAsync config circuits mockSubmit
                |> Async.RunSynchronously
                |> ignore
            )
        
        Assert.Contains("Batch submission failed", ex.Message)
    
    [<Fact>]
    let ``batchCircuitsAsync with disabled config should return empty results`` () =
        // Arrange
        let config = { BatchConfig.defaultConfig with Enabled = false }
        let circuits = ["c1"; "c2"; "c3"]
        
        // Act
        let results = 
            batchCircuitsAsync 
                config 
                circuits 
                (fun batch -> async { return batch |> List.map (fun c -> c + "_result") })
            |> Async.RunSynchronously
        
        // Assert
        Assert.Empty(results)
