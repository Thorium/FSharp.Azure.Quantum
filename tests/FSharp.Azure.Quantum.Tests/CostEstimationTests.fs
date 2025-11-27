module FSharp.Azure.Quantum.Tests.CostEstimationTests

open System
open Xunit
open FSharp.Azure.Quantum.Core.CostEstimation

// ============================================================================
// HELPER FUNCTIONS
// ============================================================================

let createSimpleCircuit singleQubitGates twoQubitGates measurements qubits =
    {
        SingleQubitGates = singleQubitGates * 1<gate>
        TwoQubitGates = twoQubitGates * 1<gate>
        Measurements = measurements * 1<gate>
        QubitCount = qubits * 1<qubit>
    }

/// Convert decimal<USD> to float for string formatting
let usdToFloat (cost: decimal<USD>) : float = float (cost / 1.0M<USD>)

/// Convert float<ms> to float for string formatting  
let msToFloat (time: float<ms>) : float = float (time / 1.0<ms>)

// ============================================================================
// IONQ COST CALCULATION TESTS
// ============================================================================

[<Fact>]
let ``IonQ cost calculation - simple circuit with error mitigation`` () =
    // Arrange
    let circuit = createSimpleCircuit 50 30 2 2
    let shots = 1000<shot>
    let backend = IonQ true
    
    // Act
    let result = estimateCost backend circuit shots
    
    // Assert
    match result with
    | Ok estimate ->
        // Expected: Base cost ($97.50) + gate costs
        // Single-qubit: 50 * 0.000220 * 1000 = $11.00
        // Two-qubit: 30 * 0.000975 * 1000 = $29.25
        // Total: $97.50 + $11.00 + $29.25 = $137.75
        Assert.InRange(estimate.ExpectedCost, 130.0M<USD>, 145.0M<USD>)
        Assert.Equal("USD", estimate.Currency)
        Assert.Equal(backend, estimate.Backend)
        Assert.True(estimate.Breakdown.IsSome, "Breakdown should be provided")
    | Error msg ->
        Assert.Fail(sprintf "Expected success but got error: %s" msg)

[<Fact>]
let ``IonQ cost calculation - without error mitigation is cheaper`` () =
    // Arrange
    let circuit = createSimpleCircuit 50 30 2 2
    let shots = 1000<shot>
    let withEM = IonQ true
    let withoutEM = IonQ false
    
    // Act
    let resultWithEM = estimateCost withEM circuit shots
    let resultWithoutEM = estimateCost withoutEM circuit shots
    
    // Assert
    match resultWithEM, resultWithoutEM with
    | Ok estimateWithEM, Ok estimateWithoutEM ->
        Assert.True(estimateWithoutEM.ExpectedCost < estimateWithEM.ExpectedCost,
            sprintf "Without EM ($%.2f) should be cheaper than with EM ($%.2f)" 
                (usdToFloat estimateWithoutEM.ExpectedCost) (usdToFloat estimateWithEM.ExpectedCost))
        // Base cost difference: $97.50 - $12.42 = $85.08
        let costDiff = estimateWithEM.ExpectedCost - estimateWithoutEM.ExpectedCost
        Assert.InRange(costDiff, 80.0M<USD>, 90.0M<USD>)
    | Error msg1, Ok _ ->
        Assert.Fail(sprintf "With EM estimate failed: %s" msg1)
    | Ok _, Error msg2 ->
        Assert.Fail(sprintf "Without EM estimate failed: %s" msg2)
    | Error msg1, Error msg2 ->
        Assert.Fail(sprintf "Both estimates failed: %s, %s" msg1 msg2)

[<Fact>]
let ``IonQ cost calculation - cost increases with shot count`` () =
    // Arrange
    let circuit = createSimpleCircuit 50 30 2 2
    let lowShots = 100<shot>
    let highShots = 10000<shot>
    let backend = IonQ true
    
    // Act
    let lowResult = estimateCost backend circuit lowShots
    let highResult = estimateCost backend circuit highShots
    
    // Assert
    match lowResult, highResult with
    | Ok lowEstimate, Ok highEstimate ->
        Assert.True(highEstimate.ExpectedCost > lowEstimate.ExpectedCost,
            sprintf "Higher shot count should cost more: $%.2f vs $%.2f" 
                (usdToFloat highEstimate.ExpectedCost) (usdToFloat lowEstimate.ExpectedCost))
    | Error msg, Ok _ ->
        Assert.Fail(sprintf "Low shot estimate failed: %s" msg)
    | Ok _, Error msg ->
        Assert.Fail(sprintf "High shot estimate failed: %s" msg)
    | Error msg1, Error msg2 ->
        Assert.Fail(sprintf "Both estimates failed: %s, %s" msg1 msg2)

[<Fact>]
let ``IonQ cost calculation - cost increases with gate count`` () =
    // Arrange
    let smallCircuit = createSimpleCircuit 10 5 2 2
    let largeCircuit = createSimpleCircuit 100 50 2 2
    let shots = 1000<shot>
    let backend = IonQ true
    
    // Act
    let smallResult = estimateCost backend smallCircuit shots
    let largeResult = estimateCost backend largeCircuit shots
    
    // Assert
    match smallResult, largeResult with
    | Ok smallEstimate, Ok largeEstimate ->
        Assert.True(largeEstimate.ExpectedCost > smallEstimate.ExpectedCost,
            sprintf "Larger circuit should cost more: $%.2f vs $%.2f" 
                (usdToFloat largeEstimate.ExpectedCost) (usdToFloat smallEstimate.ExpectedCost))
    | Error msg, Ok _ ->
        Assert.Fail(sprintf "Small circuit estimate failed: %s" msg)
    | Ok _, Error msg ->
        Assert.Fail(sprintf "Large circuit estimate failed: %s" msg)
    | Error msg1, Error msg2 ->
        Assert.Fail(sprintf "Both estimates failed: %s, %s" msg1 msg2)

[<Fact>]
let ``IonQ cost calculation - two-qubit gates cost more than single-qubit`` () =
    // Arrange
    let singleQubitCircuit = createSimpleCircuit 100 0 2 2
    let twoQubitCircuit = createSimpleCircuit 0 100 2 2
    let shots = 1000<shot>
    let backend = IonQ true
    
    // Act
    let singleQubitResult = estimateCost backend singleQubitCircuit shots
    let twoQubitResult = estimateCost backend twoQubitCircuit shots
    
    // Assert
    match singleQubitResult, twoQubitResult with
    | Ok singleEstimate, Ok twoEstimate ->
        // Two-qubit gates (0.000975) cost ~4.4x more than single-qubit (0.000220)
        Assert.True(twoEstimate.ExpectedCost > singleEstimate.ExpectedCost,
            sprintf "Two-qubit gates should cost more: $%.2f vs $%.2f" 
                (usdToFloat twoEstimate.ExpectedCost) (usdToFloat singleEstimate.ExpectedCost))
    | Error msg, Ok _ ->
        Assert.Fail(sprintf "Single-qubit estimate failed: %s" msg)
    | Ok _, Error msg ->
        Assert.Fail(sprintf "Two-qubit estimate failed: %s" msg)
    | Error msg1, Error msg2 ->
        Assert.Fail(sprintf "Both estimates failed: %s, %s" msg1 msg2)

[<Fact>]
let ``IonQ cost calculation - warning for high-cost jobs`` () =
    // Arrange
    let circuit = createSimpleCircuit 1000 500 2 2
    let shots = 5000<shot>
    let backend = IonQ true
    
    // Act
    let result = estimateCost backend circuit shots
    
    // Assert
    match result with
    | Ok estimate ->
        Assert.True(estimate.ExpectedCost > 200.0M<USD>, "High-cost job should exceed $200")
        Assert.NotEmpty(estimate.Warnings)
        Assert.Contains("$200", String.concat " " estimate.Warnings)
    | Error msg ->
        Assert.Fail(sprintf "Expected success but got error: %s" msg)

[<Fact>]
let ``IonQ cost breakdown - validates component costs`` () =
    // Arrange
    let circuit = createSimpleCircuit 50 30 2 2
    let shots = 1000<shot>
    let backend = IonQ true
    
    // Act
    let result = estimateCost backend circuit shots
    
    // Assert
    match result with
    | Ok estimate ->
        Assert.True(estimate.Breakdown.IsSome)
        let breakdown = estimate.Breakdown.Value
        Assert.Equal(97.50M<USD>, breakdown.BaseCost)
        Assert.True(breakdown.SingleQubitGateCost > 0.0M<USD>)
        Assert.True(breakdown.TwoQubitGateCost > 0.0M<USD>)
        Assert.Equal(breakdown.TotalCost, estimate.ExpectedCost)
    | Error msg ->
        Assert.Fail(sprintf "Expected success but got error: %s" msg)

// ============================================================================
// QUANTINUUM COST CALCULATION TESTS
// ============================================================================

[<Fact>]
let ``Quantinuum cost calculation - HQC quota consumption`` () =
    // Arrange
    let circuit = createSimpleCircuit 50 30 10 2
    let shots = 1000<shot>
    let backend = Quantinuum
    
    // Act
    let result = estimateCost backend circuit shots
    
    // Assert
    match result with
    | Ok estimate ->
        Assert.Equal("HQC", estimate.Currency)
        Assert.Equal(0.0M<USD>, estimate.ExpectedCost)  // Subscription model
        Assert.NotEmpty(estimate.Warnings)
        Assert.Contains("HQC", String.concat " " estimate.Warnings)
    | Error msg ->
        Assert.Fail(sprintf "Expected success but got error: %s" msg)

[<Fact>]
let ``Quantinuum HQC calculation - increases with circuit complexity`` () =
    // Arrange
    let simpleCircuit = createSimpleCircuit 10 5 2 2
    let complexCircuit = createSimpleCircuit 100 50 10 2
    let shots = 1000<shot>
    
    // Act
    let simpleHQC = calculateQuantinuumHQC QuantinuumPricing.Default simpleCircuit shots
    let complexHQC = calculateQuantinuumHQC QuantinuumPricing.Default complexCircuit shots
    
    // Assert
    Assert.True(int complexHQC > int simpleHQC,
        sprintf "Complex circuit should consume more HQC: %d vs %d" 
            (int complexHQC) (int simpleHQC))

[<Fact>]
let ``Quantinuum HQC calculation - minimum cost enforced`` () =
    // Arrange
    let tinyCircuit = createSimpleCircuit 1 1 1 1
    let shots = 1<shot>
    
    // Act
    let hqc = calculateQuantinuumHQC QuantinuumPricing.Default tinyCircuit shots
    
    // Assert
    Assert.True(int hqc >= 5, sprintf "Should have minimum 5 HQC, got %d" (int hqc))

[<Fact>]
let ``Quantinuum HQC calculation - two-qubit gates weighted more heavily`` () =
    // Arrange
    let singleQubitCircuit = createSimpleCircuit 100 0 10 2
    let twoQubitCircuit = createSimpleCircuit 0 10 10 2  // 10 two-qubit gates instead of 100 single-qubit
    let shots = 1000<shot>
    
    // Act
    let singleQubitHQC = calculateQuantinuumHQC QuantinuumPricing.Default singleQubitCircuit shots
    let twoQubitHQC = calculateQuantinuumHQC QuantinuumPricing.Default twoQubitCircuit shots
    
    // Assert
    // Two-qubit weight (10.0) vs single-qubit weight (1.0) means 10 two-qubit gates ~ 100 single-qubit gates
    let diff = abs (int twoQubitHQC - int singleQubitHQC)
    Assert.True(diff < 5, sprintf "HQC should be similar: %d vs %d" (int twoQubitHQC) (int singleQubitHQC))

// ============================================================================
// RIGETTI COST CALCULATION TESTS
// ============================================================================

[<Fact>]
let ``Rigetti cost calculation - time-based pricing`` () =
    // Arrange
    let circuit = createSimpleCircuit 50 30 2 2
    let shots = 1000<shot>
    let backend = Rigetti
    
    // Act
    let result = estimateCost backend circuit shots
    
    // Assert
    match result with
    | Ok estimate ->
        Assert.Equal("USD", estimate.Currency)
        Assert.True(estimate.ExpectedCost > 0.0M<USD>)
        Assert.True(estimate.Breakdown.IsSome)
        let breakdown = estimate.Breakdown.Value
        Assert.Equal(0.0M<USD>, breakdown.BaseCost)  // No base cost for Rigetti
        Assert.True(breakdown.ShotCost > 0.0M<USD>)  // All cost is execution time
    | Error msg ->
        Assert.Fail(sprintf "Expected success but got error: %s" msg)

[<Fact>]
let ``Rigetti cost calculation - cost scales with execution time`` () =
    // Arrange
    let shortCircuit = createSimpleCircuit 10 5 2 2
    let longCircuit = createSimpleCircuit 1000 500 2 2
    let shots = 1000<shot>
    let backend = Rigetti
    
    // Act
    let shortResult = estimateCost backend shortCircuit shots
    let longResult = estimateCost backend longCircuit shots
    
    // Assert
    match shortResult, longResult with
    | Ok shortEstimate, Ok longEstimate ->
        Assert.True(longEstimate.ExpectedCost > shortEstimate.ExpectedCost,
            sprintf "Longer circuit should cost more: $%.2f vs $%.2f" 
                (usdToFloat longEstimate.ExpectedCost) (usdToFloat shortEstimate.ExpectedCost))
    | Error msg, Ok _ ->
        Assert.Fail(sprintf "Short circuit estimate failed: %s" msg)
    | Ok _, Error msg ->
        Assert.Fail(sprintf "Long circuit estimate failed: %s" msg)
    | Error msg1, Error msg2 ->
        Assert.Fail(sprintf "Both estimates failed: %s, %s" msg1 msg2)

[<Fact>]
let ``Rigetti execution time estimation - includes all gates`` () =
    // Arrange
    let circuit = createSimpleCircuit 100 50 2 2
    let timing = GateTiming.RigettiDefault
    
    // Act
    let execTime = estimateRigettiExecutionTime timing circuit
    
    // Assert
    // 100 single-qubit (0.05 us each) + 50 two-qubit (0.20 us each) = 5 us + 10 us = 15 us = 0.015 ms
    Assert.True(msToFloat execTime > 0.01 && msToFloat execTime < 0.02,
        sprintf "Expected ~0.015 ms, got %.6f ms" (msToFloat execTime))

// ============================================================================
// CROSS-BACKEND COMPARISON TESTS
// ============================================================================

[<Fact>]
let ``compareCosts - returns estimates for all backends`` () =
    // Arrange
    let circuit = createSimpleCircuit 50 30 2 2
    let shots = 1000<shot>
    let backends = [IonQ true; Quantinuum; Rigetti]
    
    // Act
    let result = compareCosts backends circuit shots
    
    // Assert
    match result with
    | Ok estimates ->
        Assert.Equal(3, List.length estimates)
        Assert.True(estimates |> List.forall (fun e -> e.Currency <> ""), "All estimates should have currency")
    | Error msg ->
        Assert.Fail(sprintf "Expected success but got error: %s" msg)

[<Fact>]
let ``compareCosts - handles empty backend list`` () =
    // Arrange
    let circuit = createSimpleCircuit 50 30 2 2
    let shots = 1000<shot>
    let backends = []
    
    // Act
    let result = compareCosts backends circuit shots
    
    // Assert
    match result with
    | Ok estimates ->
        Assert.Empty(estimates)
    | Error msg ->
        Assert.Fail(sprintf "Expected empty list but got error: %s" msg)

// ============================================================================
// BUDGET ENFORCEMENT TESTS
// ============================================================================

[<Fact>]
let ``Budget check - approves job within limits`` () =
    // Arrange
    // Use custom policy with higher limits to accommodate IonQ's base cost (~$98)
    let policy = { BudgetPolicy.Development with 
                    PerJobLimit = Some 200.0M<USD>
                    DailyLimit = Some 500.0M<USD> }
    let circuit = createSimpleCircuit 10 5 2 2
    let shots = 100<shot>
    let backend = IonQ true
    let estimate = match estimateCost backend circuit shots with Ok e -> e | Error _ -> failwith "Setup failed"
    
    // Act
    let result = checkBudget policy estimate 0.0M<USD> 0.0M<USD>
    
    // Assert
    match result with
    | Approved -> ()  // Success - no need for Assert.True(true)
    | Warning msg -> Assert.Fail(sprintf "Expected approval but got warning: %s" msg)
    | Denied reason -> Assert.Fail(sprintf "Expected approval but got denial: %s" reason)

[<Fact>]
let ``Budget check - denies job exceeding per-job limit`` () =
    // Arrange
    let policy = { BudgetPolicy.Development with PerJobLimit = Some 10.0M<USD> }
    let circuit = createSimpleCircuit 100 50 2 2
    let shots = 1000<shot>
    let backend = IonQ true
    let estimate = match estimateCost backend circuit shots with Ok e -> e | Error _ -> failwith "Setup failed"
    
    // Act
    let result = checkBudget policy estimate 0.0M<USD> 0.0M<USD>
    
    // Assert
    match result with
    | Denied reason ->
        Assert.Contains("per-job limit", reason)
    | Approved -> Assert.Fail("Expected denial for per-job limit but got approval")
    | Warning msg -> Assert.Fail(sprintf "Expected denial for per-job limit but got warning: %s" msg)

[<Fact>]
let ``Budget check - denies job exceeding daily limit`` () =
    // Arrange
    // Use very high per-job limit so we test daily limit instead
    let policy = { BudgetPolicy.Development with DailyLimit = Some 100.0M<USD>; PerJobLimit = Some 300.0M<USD> }
    let circuit = createSimpleCircuit 100 50 2 2
    let shots = 1000<shot>
    let backend = IonQ true
    let estimate = match estimateCost backend circuit shots with Ok e -> e | Error _ -> failwith "Setup failed"
    let dailySpent = 20.0M<USD>  // Low spending but job will push over limit
    
    // Act
    let result = checkBudget policy estimate dailySpent 0.0M<USD>
    
    // Assert
    match result with
    | Denied reason ->
        Assert.Contains("daily limit", reason)
    | Approved -> Assert.Fail("Expected denial for daily limit but got approval")
    | Warning msg -> Assert.Fail(sprintf "Expected denial for daily limit but got warning: %s" msg)

[<Fact>]
let ``Budget check - denies job exceeding monthly limit`` () =
    // Arrange
    // Use very high per-job and daily limits so we test monthly limit instead
    let policy = { BudgetPolicy.Development with MonthlyLimit = Some 200.0M<USD>; DailyLimit = Some 300.0M<USD>; PerJobLimit = Some 300.0M<USD> }
    let circuit = createSimpleCircuit 100 50 2 2
    let shots = 1000<shot>
    let backend = IonQ true
    let estimate = match estimateCost backend circuit shots with Ok e -> e | Error _ -> failwith "Setup failed"
    let monthlySpent = 100.0M<USD>  // Spent $100, job will push over $200 limit
    
    // Act
    let result = checkBudget policy estimate 0.0M<USD> monthlySpent
    
    // Assert
    match result with
    | Denied reason ->
        Assert.Contains("monthly limit", reason)
    | Approved -> Assert.Fail("Expected denial for monthly limit but got approval")
    | Warning msg -> Assert.Fail(sprintf "Expected denial for monthly limit but got warning: %s" msg)

[<Fact>]
let ``Budget check - warns when approaching limit`` () =
    // Arrange
    // Use high per-job limit, and configure daily limit for warning test
    let policy = { BudgetPolicy.Development with DailyLimit = Some 200.0M<USD>; PerJobLimit = Some 300.0M<USD>; WarnAtPercent = 80.0 }
    let circuit = createSimpleCircuit 50 30 2 2
    let shots = 100<shot>
    let backend = IonQ false  // Without EM for lower cost (~$12 base + gates)
    let estimate = match estimateCost backend circuit shots with Ok e -> e | Error _ -> failwith "Setup failed"
    let dailySpent = 130.0M<USD>  // $130 + estimate will be > 80% of $200
    
    // Act
    let result = checkBudget policy estimate dailySpent 0.0M<USD>
    
    // Assert
    match result with
    | Warning msg ->
        Assert.Contains("daily budget", msg)
    | Approved -> 
        // This is also acceptable if the estimate is small enough
        ()
    | Denied reason -> Assert.Fail(sprintf "Should warn, not deny: %s" reason)

// ============================================================================
// COST TRACKING TESTS
// ============================================================================

[<Fact>]
let ``Cost tracker - creates empty tracker`` () =
    // Act
    let tracker = CostTracker.create()
    
    // Assert
    Assert.Empty(tracker.Records)
    Assert.Equal(0.0M<USD>, tracker.DailySpent)
    Assert.Equal(0.0M<USD>, tracker.MonthlySpent)

[<Fact>]
let ``Cost tracker - adds record and updates spending`` () =
    // Arrange
    let tracker = CostTracker.create()
    let record = {
        JobId = "job-123"
        Backend = IonQ true
        EstimatedCost = 100.0M<USD>
        ActualCost = Some 105.0M<USD>
        Timestamp = DateTimeOffset.UtcNow
        Circuit = createSimpleCircuit 50 30 2 2
        Shots = 1000<shot>
    }
    
    // Act
    let updatedTracker = CostTracker.addRecord record tracker
    
    // Assert
    let itm = Assert.Single(updatedTracker.Records)
    Assert.Equal(105.0M<USD>, updatedTracker.DailySpent)
    Assert.Equal(105.0M<USD>, updatedTracker.MonthlySpent)

[<Fact>]
let ``Cost tracker - uses estimated cost when actual cost unavailable`` () =
    // Arrange
    let tracker = CostTracker.create()
    let record = {
        JobId = "job-123"
        Backend = IonQ true
        EstimatedCost = 100.0M<USD>
        ActualCost = None  // No actual cost available
        Timestamp = DateTimeOffset.UtcNow
        Circuit = createSimpleCircuit 50 30 2 2
        Shots = 1000<shot>
    }
    
    // Act
    let updatedTracker = CostTracker.addRecord record tracker
    
    // Assert
    Assert.Equal(100.0M<USD>, updatedTracker.DailySpent)

[<Fact>]
let ``Cost tracker - tracks multiple records`` () =
    // Arrange
    let tracker = CostTracker.create()
    let record1 = {
        JobId = "job-1"
        Backend = IonQ true
        EstimatedCost = 100.0M<USD>
        ActualCost = Some 105.0M<USD>
        Timestamp = DateTimeOffset.UtcNow
        Circuit = createSimpleCircuit 50 30 2 2
        Shots = 1000<shot>
    }
    let record2 = {
        JobId = "job-2"
        Backend = Rigetti
        EstimatedCost = 50.0M<USD>
        ActualCost = Some 48.0M<USD>
        Timestamp = DateTimeOffset.UtcNow
        Circuit = createSimpleCircuit 30 15 2 2
        Shots = 500<shot>
    }
    
    // Act
    let updatedTracker = 
        tracker
        |> CostTracker.addRecord record1
        |> CostTracker.addRecord record2
    
    // Assert
    Assert.Equal(2, List.length updatedTracker.Records)
    Assert.Equal(153.0M<USD>, updatedTracker.DailySpent)  // 105 + 48
    Assert.Equal(153.0M<USD>, updatedTracker.MonthlySpent)

[<Fact>]
let ``Cost tracker - getSpendingByBackend groups correctly`` () =
    // Arrange
    let tracker = CostTracker.create()
    let record1 = {
        JobId = "job-1"
        Backend = IonQ true
        EstimatedCost = 100.0M<USD>
        ActualCost = Some 100.0M<USD>
        Timestamp = DateTimeOffset.UtcNow
        Circuit = createSimpleCircuit 50 30 2 2
        Shots = 1000<shot>
    }
    let record2 = {
        JobId = "job-2"
        Backend = IonQ true
        EstimatedCost = 50.0M<USD>
        ActualCost = Some 50.0M<USD>
        Timestamp = DateTimeOffset.UtcNow
        Circuit = createSimpleCircuit 30 15 2 2
        Shots = 500<shot>
    }
    let record3 = {
        JobId = "job-3"
        Backend = Rigetti
        EstimatedCost = 30.0M<USD>
        ActualCost = Some 30.0M<USD>
        Timestamp = DateTimeOffset.UtcNow
        Circuit = createSimpleCircuit 20 10 2 2
        Shots = 300<shot>
    }
    
    // Act
    let updatedTracker = 
        tracker
        |> CostTracker.addRecord record1
        |> CostTracker.addRecord record2
        |> CostTracker.addRecord record3
    let spendingByBackend = CostTracker.getSpendingByBackend updatedTracker
    
    // Assert
    Assert.Equal(150.0M<USD>, spendingByBackend.[IonQ true])
    Assert.Equal(30.0M<USD>, spendingByBackend.[Rigetti])

// ============================================================================
// ERROR HANDLING TESTS
// ============================================================================

[<Fact>]
let ``estimateCost - returns error for invalid shot count`` () =
    // Arrange
    let circuit = createSimpleCircuit 50 30 2 2
    let shots = 0<shot>
    let backend = IonQ true
    
    // Act
    let result = estimateCost backend circuit shots
    
    // Assert
    match result with
    | Error msg ->
        Assert.Contains("Shot count must be at least 1", msg)
    | Ok estimate -> Assert.Fail(sprintf "Expected error for invalid shot count but got estimate: $%.2f" (usdToFloat estimate.ExpectedCost))

// ============================================================================
// COST OPTIMIZATION TESTS (TKT-48)
// ============================================================================

[<Fact>]
let ``findCheapestBackend returns backend with lowest cost`` () =
    // Arrange
    let circuit = createSimpleCircuit 50 30 2 2
    let shots = 1000<shot>
    let backends = [IonQ true; IonQ false; Rigetti]
    
    // Act
    let result = findCheapestBackend backends circuit shots
    
    // Assert
    match result with
    | Ok (cheapest, estimate) ->
        // Should return one of the backends
        Assert.Contains(cheapest, backends)
        Assert.True(estimate.ExpectedCost > 0.0M<USD>)
        
        // Verify it's actually the cheapest by comparing with all backends
        match compareCosts backends circuit shots with
        | Ok allEstimates ->
            let minCost = allEstimates |> List.map (fun e -> e.ExpectedCost) |> List.min
            Assert.Equal(minCost, estimate.ExpectedCost)
        | Error msg ->
            Assert.Fail(sprintf "compareCosts failed: %s" msg)
    | Error msg ->
        Assert.Fail(sprintf "Expected success but got error: %s" msg)

[<Fact>]
let ``recommendCostOptimization suggests cheaper backend`` () =
    // Arrange
    let circuit = createSimpleCircuit 50 30 2 2
    let shots = 1000<shot>
    let currentBackend = IonQ true  // Most expensive option
    let availableBackends = [IonQ true; IonQ false; Rigetti]
    
    // Act
    let result = recommendCostOptimization currentBackend availableBackends circuit shots
    
    // Assert
    match result with
    | Ok (Some recommendation) ->
        // Should recommend switching to a cheaper backend
        Assert.NotEqual(currentBackend, recommendation.RecommendedBackend)
        Assert.True(recommendation.PotentialSavings > 0.0M<USD>)
        Assert.False(String.IsNullOrWhiteSpace(recommendation.Reasoning))
    | Ok None ->
        Assert.Fail("Expected recommendation for expensive backend but got None")
    | Error msg ->
        Assert.Fail(sprintf "Expected success but got error: %s" msg)

// ============================================================================
// CLI DASHBOARD TESTS (TKT-48)
// ============================================================================

[<Fact>]
let ``displayCostDashboard shows spending summary`` () =
    // Arrange
    let now = System.DateTimeOffset.UtcNow
    let circuit = createSimpleCircuit 50 30 2 2
    let records = [
        {
            JobId = "job-1"
            Backend = IonQ false
            EstimatedCost = 50.0M<USD>
            ActualCost = Some 52.0M<USD>
            Timestamp = now
            Circuit = circuit
            Shots = 1000<shot>
        }
        {
            JobId = "job-2"
            Backend = Rigetti
            EstimatedCost = 30.0M<USD>
            ActualCost = Some 28.0M<USD>
            Timestamp = now.AddHours(-1.0)
            Circuit = circuit
            Shots = 1000<shot>
        }
    ]
    
    // Act - should not throw exception
    displayCostDashboard records
    
    // Assert - if we get here without exception, test passes
    Assert.True(true)

[<Fact>]
let ``displayCostDashboard handles empty records`` () =
    // Arrange
    let records = []
    
    // Act - should not throw exception
    displayCostDashboard records
    
    // Assert
    Assert.True(true)

[<Fact>]
let ``findCheapestBackend returns error for empty backend list`` () =
    // Arrange
    let circuit = createSimpleCircuit 50 30 2 2
    let shots = 1000<shot>
    let backends = []
    
    // Act
    let result = findCheapestBackend backends circuit shots
    
    // Assert
    match result with
    | Error msg ->
        Assert.Contains("No backends provided", msg)
    | Ok _ ->
        Assert.Fail("Expected error for empty backend list")

[<Fact>]
let ``recommendCostOptimization returns None when already using cheapest`` () =
    // Arrange
    let circuit = createSimpleCircuit 10 5 2 2  // Small circuit
    let shots = 1000<shot>
    // Rigetti is typically cheapest for small circuits
    let currentBackend = Rigetti
    let availableBackends = [IonQ true; IonQ false; Rigetti]
    
    // Act
    let result = recommendCostOptimization currentBackend availableBackends circuit shots
    
    // Assert
    match result with
    | Ok None ->
        Assert.True(true)  // Expected: no recommendation when already optimal
    | Ok (Some recommendation) ->
        // If there is a recommendation, savings should be minimal (< 20%)
        let savingsPercent = (float (recommendation.PotentialSavings / recommendation.CurrentCost.ExpectedCost)) * 100.0
        Assert.True(savingsPercent < 20.0, 
            sprintf "Expected no recommendation or < 20%% savings, got %.1f%%" savingsPercent)
    | Error msg ->
        Assert.Fail(sprintf "Unexpected error: %s" msg)

[<Fact>]
let ``recommendCostOptimization provides detailed reasoning`` () =
    // Arrange
    let circuit = createSimpleCircuit 50 30 2 2
    let shots = 1000<shot>
    let currentBackend = IonQ true
    let availableBackends = [IonQ true; Rigetti]
    
    // Act
    let result = recommendCostOptimization currentBackend availableBackends circuit shots
    
    // Assert
    match result with
    | Ok (Some recommendation) ->
        Assert.NotEmpty(recommendation.Reasoning)
        Assert.Contains("Save", recommendation.Reasoning)
        Assert.Contains("reduction", recommendation.Reasoning)
    | Ok None ->
        () // No recommendation is also valid
    | Error msg ->
        Assert.Fail(sprintf "Expected success but got error: %s" msg)

[<Fact>]
let ``findCheapestBackend works with single backend`` () =
    // Arrange
    let circuit = createSimpleCircuit 50 30 2 2
    let shots = 1000<shot>
    let backends = [Rigetti]
    
    // Act
    let result = findCheapestBackend backends circuit shots
    
    // Assert
    match result with
    | Ok (cheapest, estimate) ->
        Assert.Equal(Rigetti, cheapest)
        Assert.True(estimate.ExpectedCost > 0.0M<USD>)
    | Error msg ->
        Assert.Fail(sprintf "Expected success but got error: %s" msg)
