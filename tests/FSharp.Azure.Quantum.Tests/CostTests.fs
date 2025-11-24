module FSharp.Azure.Quantum.Tests.CostTests

open System
open Xunit
open FSharp.Azure.Quantum.Core.Cost

[<Fact>]
let ``estimateCost should return zero cost for simulator targets`` () =
    // Arrange
    let target = "ionq.simulator"
    let shots = 1000

    // Act
    let result = estimateCost target shots

    // Assert
    match result with
    | Ok estimate ->
        Assert.Equal(0.0M<USD>, estimate.ExpectedCost)
        Assert.Equal(0.0M<USD>, estimate.MinimumCost)
        Assert.Equal(0.0M<USD>, estimate.MaximumCost)
        Assert.Equal("USD", estimate.Currency)
        Assert.Equal(target, estimate.Target)
        Assert.Empty(estimate.Warnings)
    | Error msg -> Assert.True(false, sprintf "Expected success but got error: %s" msg)

[<Fact>]
let ``estimateCost should return non-zero cost for QPU targets`` () =
    // Arrange
    let target = "ionq.qpu.aria-1"
    let shots = 1000

    // Act
    let result = estimateCost target shots

    // Assert
    match result with
    | Ok estimate ->
        Assert.True(estimate.ExpectedCost > 0.0M<USD>, "QPU should have non-zero cost")
        Assert.True(estimate.MinimumCost > 0.0M<USD>, "QPU should have minimum base cost")
        Assert.True(estimate.MaximumCost >= estimate.MinimumCost, "Max cost should be >= min cost")
        Assert.Equal("USD", estimate.Currency)
        Assert.Equal(target, estimate.Target)
    | Error msg -> Assert.True(false, sprintf "Expected success but got error: %s" msg)

[<Fact>]
let ``estimateCost should increase with shot count`` () =
    // Arrange
    let target = "ionq.qpu.aria-1"
    let lowShots = 100
    let highShots = 10000

    // Act
    let lowResult = estimateCost target lowShots
    let highResult = estimateCost target highShots

    // Assert
    match lowResult, highResult with
    | Ok lowEstimate, Ok highEstimate ->
        Assert.True(
            highEstimate.ExpectedCost > lowEstimate.ExpectedCost,
            sprintf "Higher shot count should cost more: %M vs %M" highEstimate.ExpectedCost lowEstimate.ExpectedCost
        )
    | _ -> Assert.True(false, "Both estimates should succeed")

[<Fact>]
let ``estimateCost should return error for invalid shot count`` () =
    // Arrange
    let target = "ionq.simulator"
    let shots = 0

    // Act
    let result = estimateCost target shots

    // Assert
    match result with
    | Ok _ -> Assert.True(false, "Expected error for zero shots")
    | Error msg -> Assert.Contains("Shot count must be at least 1", msg)

[<Fact>]
let ``estimateCost should return error for empty target`` () =
    // Arrange
    let target = ""
    let shots = 1000

    // Act
    let result = estimateCost target shots

    // Assert
    match result with
    | Ok _ -> Assert.True(false, "Expected error for empty target")
    | Error msg -> Assert.Contains("Target backend cannot be empty", msg)

[<Fact>]
let ``estimateCost should add warning for high-cost jobs`` () =
    // Arrange
    let target = "ionq.qpu.aria-1"
    let shots = 50000 // High shot count to trigger warning

    // Act
    let result = estimateCost target shots

    // Assert
    match result with
    | Ok estimate ->
        Assert.NotEmpty(estimate.Warnings)
        Assert.Contains("$200", estimate.Warnings.[0])
    | Error msg -> Assert.True(false, sprintf "Expected success but got error: %s" msg)

[<Fact>]
let ``parseCostFromMetadata should return None for null input`` () =
    // Arrange
    let costData: string option = None

    // Act
    let result = parseCostFromMetadata costData

    // Assert
    Assert.True(result.IsNone, "Should return None for null input")

[<Fact>]
let ``parseCostFromMetadata should return None for empty input`` () =
    // Arrange
    let costData = Some ""

    // Act
    let result = parseCostFromMetadata costData

    // Assert
    Assert.True(result.IsNone, "Should return None for empty input")

[<Fact>]
let ``parseCostFromMetadata should parse estimated cost from JSON`` () =
    // Arrange
    let costData = Some """{"estimated": 135.50, "currency": "USD"}"""

    // Act
    let result = parseCostFromMetadata costData

    // Assert
    match result with
    | Some costInfo ->
        Assert.True(costInfo.ActualCost.IsSome, "Should have cost value")
        Assert.Equal(135.50M<USD>, costInfo.ActualCost.Value)
        Assert.Equal("USD", costInfo.Currency)
    | None -> Assert.True(false, "Expected cost info but got None")

[<Fact>]
let ``parseCostFromMetadata should parse actual cost from JSON`` () =
    // Arrange
    let costData =
        Some """{"actual": 142.75, "currency": "USD", "billingStatus": "Charged"}"""

    // Act
    let result = parseCostFromMetadata costData

    // Assert
    match result with
    | Some costInfo ->
        Assert.True(costInfo.ActualCost.IsSome, "Should have cost value")
        Assert.Equal(142.75M<USD>, costInfo.ActualCost.Value)
        Assert.Equal("USD", costInfo.Currency)
        Assert.True(costInfo.BillingStatus.IsSome, "Should have billing status")
        Assert.Equal("Charged", costInfo.BillingStatus.Value)
    | None -> Assert.True(false, "Expected cost info but got None")

[<Fact>]
let ``parseCostFromMetadata should return None for invalid JSON`` () =
    // Arrange
    let costData = Some "invalid json {{"

    // Act
    let result = parseCostFromMetadata costData

    // Assert
    Assert.True(result.IsNone, "Should return None for invalid JSON")

[<Fact>]
let ``parseCostFromMetadata should handle missing cost fields`` () =
    // Arrange
    let costData = Some """{"currency": "USD", "billingStatus": "Pending"}"""

    // Act
    let result = parseCostFromMetadata costData

    // Assert
    match result with
    | Some costInfo ->
        Assert.True(costInfo.ActualCost.IsNone, "Should have no cost when not present")
        Assert.Equal("USD", costInfo.Currency)
        Assert.True(costInfo.BillingStatus.IsSome)
        Assert.Equal("Pending", costInfo.BillingStatus.Value)
    | None -> Assert.True(false, "Expected cost info but got None")
