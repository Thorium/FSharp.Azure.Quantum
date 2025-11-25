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
