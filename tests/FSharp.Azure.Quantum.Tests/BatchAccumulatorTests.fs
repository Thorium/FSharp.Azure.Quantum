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
