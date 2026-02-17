namespace FSharp.Azure.Quantum.Tests

open Xunit
open FSharp.Azure.Quantum
open FSharp.Azure.Quantum.Core

module ErrorMitigationStrategyTests =
    
    // ============================================================================
    // Test Helpers
    // ============================================================================
    
    /// Create a test backend
    let createTestBackend () : Types.Backend =
        {
            Id = "ionq.simulator"
            Provider = "IonQ"
            Name = "IonQ Simulator"
            Status = "Available"
        }
    
    /// Create base selection criteria for testing
    let createBaseCriteria () : ErrorMitigationStrategy.SelectionCriteria =
        {
            CircuitDepth = 25
            QubitCount = 5
            Backend = createTestBackend ()
            MaxCostUSD = None
            RequiredAccuracy = None
            Calibration = None
        }
    
    // ============================================================================
    // Tests: Shallow Circuit Selection (2 tests)
    // ============================================================================
    
    [<Fact>]
    let ``Shallow circuit (5 gates) should select Readout-only`` () =
        // Arrange
        let criteria = { createBaseCriteria () with CircuitDepth = 5 }
        
        // Act
        let strategy = ErrorMitigationStrategy.selectStrategy criteria
        
        // Assert
        match strategy.Primary with
        | ErrorMitigationStrategy.ReadoutErrorMitigation _ -> 
            Assert.True(true)
        | _ -> 
            Assert.Fail("Expected ReadoutErrorMitigation for shallow circuit")
        
        Assert.Equal(0.0, strategy.EstimatedCostMultiplier)
        Assert.Contains("Shallow circuit", strategy.Reasoning)
    
    [<Fact>]
    let ``Shallow circuit (9 gates) should select Readout-only`` () =
        // Arrange
        let criteria = { createBaseCriteria () with CircuitDepth = 9 }
        
        // Act
        let strategy = ErrorMitigationStrategy.selectStrategy criteria
        
        // Assert
        match strategy.Primary with
        | ErrorMitigationStrategy.ReadoutErrorMitigation _ -> 
            Assert.True(true)
        | _ -> 
            Assert.Fail("Expected ReadoutErrorMitigation for shallow circuit")
        
        Assert.Equal(0.0, strategy.EstimatedCostMultiplier)
        Assert.InRange(strategy.EstimatedAccuracy, 0.80, 0.90)
    
    // ============================================================================
    // Tests: Medium Circuit Selection (2 tests)
    // ============================================================================
    
    [<Fact>]
    let ``Medium circuit (25 gates) with budget should select ZNE + Readout`` () =
        // Arrange
        let criteria = { createBaseCriteria () with 
                           CircuitDepth = 25
                           MaxCostUSD = Some 20.0 }
        
        // Act
        let strategy = ErrorMitigationStrategy.selectStrategy criteria
        
        // Assert
        match strategy.Primary with
        | ErrorMitigationStrategy.Combined techniques ->
            Assert.Equal(2, List.length techniques)
            Assert.Contains("ZNE", strategy.Reasoning)
        | _ -> 
            Assert.Fail("Expected Combined (ZNE + Readout) for medium circuit with budget")
        
        Assert.Equal(3.0, strategy.EstimatedCostMultiplier)
        Assert.InRange(strategy.EstimatedAccuracy, 0.70, 0.80)
    
    [<Fact>]
    let ``Medium circuit (45 gates) with budget should select ZNE + Readout with fallback`` () =
        // Arrange
        let criteria = { createBaseCriteria () with 
                           CircuitDepth = 45
                           MaxCostUSD = Some 15.0 }
        
        // Act
        let strategy = ErrorMitigationStrategy.selectStrategy criteria
        
        // Assert
        match strategy.Primary with
        | ErrorMitigationStrategy.Combined _ -> 
            Assert.True(true)
        | _ -> 
            Assert.Fail("Expected Combined for medium circuit")
        
        // Should have fallback to Readout-only
        match strategy.Fallback with
        | Some (ErrorMitigationStrategy.ReadoutErrorMitigation _) ->
            Assert.True(true)
        | _ ->
            Assert.Fail("Expected Readout fallback")
    
    // ============================================================================
    // Tests: Deep Circuit Selection (2 tests)
    // ============================================================================
    
    [<Fact>]
    let ``Deep circuit (60 gates) should select ZNE + Readout`` () =
        // Arrange
        let criteria = { createBaseCriteria () with CircuitDepth = 60 }
        
        // Act
        let strategy = ErrorMitigationStrategy.selectStrategy criteria
        
        // Assert
        match strategy.Primary with
        | ErrorMitigationStrategy.Combined techniques ->
            Assert.Equal(2, List.length techniques)
        | _ -> 
            Assert.Fail("Expected Combined (ZNE + Readout) for deep circuit")
        
        Assert.Equal(3.0, strategy.EstimatedCostMultiplier)
        Assert.Contains("Deep circuit", strategy.Reasoning)
    
    [<Fact>]
    let ``High accuracy requirement (92%) with budget should select full mitigation stack`` () =
        // Arrange
        let criteria = { createBaseCriteria () with 
                           CircuitDepth = 40
                           RequiredAccuracy = Some 0.92
                           MaxCostUSD = Some 150.0 }
        
        // Act
        let strategy = ErrorMitigationStrategy.selectStrategy criteria
        
        // Assert
        match strategy.Primary with
        | ErrorMitigationStrategy.Combined techniques ->
            // Should have PEC + ZNE + Readout (3 techniques)
            Assert.Equal(3, List.length techniques)
            Assert.Contains("High accuracy", strategy.Reasoning)
        | _ -> 
            Assert.Fail("Expected full mitigation stack for high accuracy requirement")
        
        Assert.True(strategy.EstimatedCostMultiplier > 50.0)
        Assert.InRange(strategy.EstimatedAccuracy, 0.90, 0.95)
    
    // ============================================================================
    // Tests: Budget-Constrained Selection (2 tests)
    // ============================================================================
    
    [<Fact>]
    let ``Very low budget ($0.50) should select Readout-only`` () =
        // Arrange
        let criteria = { createBaseCriteria () with 
                           CircuitDepth = 30
                           MaxCostUSD = Some 0.50 }
        
        // Act
        let strategy = ErrorMitigationStrategy.selectStrategy criteria
        
        // Assert
        match strategy.Primary with
        | ErrorMitigationStrategy.ReadoutErrorMitigation _ -> 
            Assert.True(true)
        | _ -> 
            Assert.Fail("Expected Readout-only for minimal budget")
        
        Assert.Equal(0.0, strategy.EstimatedCostMultiplier)
        Assert.Contains("Minimal budget", strategy.Reasoning)
    
    [<Fact>]
    let ``Low budget ($5) should select Readout-only`` () =
        // Arrange
        let criteria = { createBaseCriteria () with 
                           CircuitDepth = 35
                           MaxCostUSD = Some 5.0 }
        
        // Act
        let strategy = ErrorMitigationStrategy.selectStrategy criteria
        
        // Assert
        match strategy.Primary with
        | ErrorMitigationStrategy.ReadoutErrorMitigation _ -> 
            Assert.True(true)
        | _ -> 
            Assert.Fail("Expected Readout-only for low budget")
        
        Assert.Equal(0.0, strategy.EstimatedCostMultiplier)
        Assert.Contains("Budget constrained", strategy.Reasoning)
        Assert.Equal(None, strategy.Fallback)
    
    // ============================================================================
    // Tests: Fallback Handling (2 tests)
    // ============================================================================
    
    [<Fact>]
    let ``applyStrategy should succeed with primary strategy`` () =
        // Arrange
        let criteria = { createBaseCriteria () with CircuitDepth = 25 }
        let strategy = ErrorMitigationStrategy.selectStrategy criteria
        let histogram = Map.ofList [("00000", 480); ("11111", 520)]
        
        // Act
        let result = ErrorMitigationStrategy.applyStrategy histogram strategy
        
        // Assert
        match result with
        | Ok mitigated ->
            Assert.False(mitigated.UsedFallback)
            Assert.Equal(2, Map.count mitigated.Histogram)
        | Error msg ->
            Assert.Fail(sprintf "Strategy application failed: %s" msg.Message)
    
    [<Fact>]
    let ``Strategy with fallback should provide secondary option`` () =
        // Arrange
        let criteria = { createBaseCriteria () with 
                           CircuitDepth = 30
                           MaxCostUSD = Some 15.0 }
        let strategy = ErrorMitigationStrategy.selectStrategy criteria
        
        // Assert
        match strategy.Fallback with
        | Some fallback ->
            // Fallback should be simpler/cheaper than primary
            match fallback with
            | ErrorMitigationStrategy.ReadoutErrorMitigation _ ->
                Assert.True(true)
            | ErrorMitigationStrategy.Combined techniques ->
                // If combined, should have fewer techniques than primary
                match strategy.Primary with
                | ErrorMitigationStrategy.Combined primaryTechniques ->
                    Assert.True(List.length techniques < List.length primaryTechniques)
                | _ -> ()
            | _ -> ()
        | None ->
            // Some strategies legitimately have no fallback (e.g., Readout-only)
            match strategy.Primary with
            | ErrorMitigationStrategy.ReadoutErrorMitigation _ ->
                Assert.True(true)  // Readout-only doesn't need fallback
            | _ ->
                Assert.Fail("Expected fallback for non-Readout strategies")
    
    // ============================================================================
    // Additional Tests: Cost Estimation
    // ============================================================================
    
    [<Fact>]
    let ``Cost estimation should match strategy selection`` () =
        // Arrange
        let criteria = { createBaseCriteria () with CircuitDepth = 25 }
        
        // Act
        let strategy = ErrorMitigationStrategy.selectStrategy criteria
        
        // Assert
        match strategy.Primary with
        | ErrorMitigationStrategy.ReadoutErrorMitigation _ ->
            Assert.Equal(0.0, strategy.EstimatedCostMultiplier)
        | ErrorMitigationStrategy.ZeroNoiseExtrapolation _ ->
            Assert.InRange(strategy.EstimatedCostMultiplier, 2.0, 4.0)
        | ErrorMitigationStrategy.ProbabilisticErrorCancellation _ ->
            Assert.True(strategy.EstimatedCostMultiplier > 10.0)
        | ErrorMitigationStrategy.Combined techniques ->
            // Combined cost should be sum of individual techniques
            Assert.True(strategy.EstimatedCostMultiplier > 0.0)
    
    [<Fact>]
    let ``Default strategy (no constraints) should be balanced`` () =
        // Arrange
        let criteria = createBaseCriteria ()
        
        // Act
        let strategy = ErrorMitigationStrategy.selectStrategy criteria
        
        // Assert
        Assert.Contains("Balanced", strategy.Reasoning)
        Assert.InRange(strategy.EstimatedCostMultiplier, 1.0, 10.0)
        Assert.InRange(strategy.EstimatedAccuracy, 0.70, 0.80)
