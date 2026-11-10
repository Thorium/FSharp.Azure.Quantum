module FSharp.Azure.Quantum.Tests.CostTests

open System
open Xunit
open FSharp.Azure.Quantum.Core.CostEstimation
open FSharp.Azure.Quantum.Core

[<Fact>]
let ``estimateCost should return zero cost for simulator targets`` () =
    // Arrange
    let target = "ionq.simulator"
    let shots = 1000

    // Act
    let result = estimateCostSimple target shots

    // Assert
    match result with
    | Ok estimate ->
        Assert.Equal(0.0M<USD>, estimate.ExpectedCost)
        Assert.Equal(0.0M<USD>, estimate.MinimumCost)
        Assert.Equal(0.0M<USD>, estimate.MaximumCost)
        Assert.Equal("USD", estimate.Currency)
        Assert.Equal(target, estimate.Target)
        Assert.Empty(estimate.Warnings)
    | Error err -> Assert.True(false, $"Expected success but got error: %s{err.Message}")

[<Fact>]
let ``estimateCost should return non-zero cost for QPU targets`` () =
    // Arrange
    let target = "ionq.qpu.aria-1"
    let shots = 1000

    // Act
    let result = estimateCostSimple target shots

    // Assert
    match result with
    | Ok estimate ->
        Assert.True(estimate.ExpectedCost > 0.0M<USD>, "QPU should have non-zero cost")
        Assert.True(estimate.MinimumCost > 0.0M<USD>, "QPU should have minimum base cost")
        Assert.True(estimate.MaximumCost >= estimate.MinimumCost, "Max cost should be >= min cost")
        Assert.Equal("USD", estimate.Currency)
        Assert.Equal(target, estimate.Target)
    | Error err -> Assert.True(false, $"Expected success but got error: %s{err.Message}")

[<Fact>]
let ``estimateCost should increase with shot count`` () =
    // Arrange
    let target = "ionq.qpu.aria-1"
    let lowShots = 100
    let highShots = 10000

    // Act
    let lowResult = estimateCostSimple target lowShots
    let highResult = estimateCostSimple target highShots

    // Assert
    match lowResult, highResult with
    | Ok lowEstimate, Ok highEstimate ->
        Assert.True(
            highEstimate.ExpectedCost > lowEstimate.ExpectedCost,
            $"Higher shot count should cost more: %M{highEstimate.ExpectedCost} vs %M{lowEstimate.ExpectedCost}"
        )
    | _ -> Assert.True(false, "Both estimates should succeed")

[<Fact>]
let ``estimateCost should return error for invalid shot count`` () =
    // Arrange
    let target = "ionq.simulator"
    let shots = 0

    // Act
    let result = estimateCostSimple target shots

    // Assert
    result
    |> Result.map (fun _ -> Assert.True(false, "Expected error for zero shots"))
    |> Result.defaultWith (fun err -> Assert.Contains("Shot count must be at least 1", err.Message))

[<Fact>]
let ``estimateCost should return error for empty target`` () =
    // Arrange
    let target = ""
    let shots = 1000

    // Act
    let result = estimateCostSimple target shots

    // Assert
    result
    |> Result.map (fun _ -> Assert.True(false, "Expected error for empty target"))
    |> Result.defaultWith (fun err -> Assert.Contains("Target backend cannot be empty", err.Message))

[<Fact>]
let ``estimateCost should add warning for high-cost jobs`` () =
    // Arrange
    let target = "ionq.qpu.aria-1"
    let shots = 50000 // High shot count to trigger warning

    // Act
    let result = estimateCostSimple target shots

    // Assert
    match result with
    | Ok estimate ->
        Assert.NotEmpty(estimate.Warnings)
        Assert.Contains("$200", estimate.Warnings.[0])
    | Error err -> Assert.True(false, $"Expected success but got error: %s{err.Message}")

[<Theory>]
[<InlineData("ionq.simulator")>]
[<InlineData("rigetti.sim.qvm")>]
[<InlineData("iqm.sim")>]
[<InlineData("atom-computing.sim")>]
[<InlineData("quantinuum.sim.h1-1sc")>]
[<InlineData("pasqal.sim.emu-tn")>]
[<InlineData("Rigetti Cloud (rigetti.sim.qvm)")>]
[<InlineData("IonQ Cloud (ionq.simulator)")>]
let ``isSimulatorTarget recognises the providers' free simulator targets`` (target: string) =
    Assert.True(isSimulatorTarget target)

    // The simple estimate and the predicate agree: a simulator target costs nothing.
    match estimateCostSimple target 1000 with
    | Ok estimate -> Assert.Equal(0.0M<USD>, estimate.ExpectedCost)
    | Error err -> Assert.Fail($"Expected success but got error: %s{err.Message}")

[<Theory>]
[<InlineData("ionq.qpu.aria-1")>]
[<InlineData("rigetti.qpu.ankaa-3")>]
[<InlineData("iqm.qpu.garnet")>]
[<InlineData("quantinuum.qpu.h1-1")>]
// Quantinuum's emulators are billed in emulator credits.
[<InlineData("quantinuum.sim.h1-1e")>]
[<InlineData("quantinuum.sim.h2-1e")>]
[<InlineData("Quantinuum Cloud (quantinuum.sim.h1-1e)")>]
// "simulator" inside another word is not a simulator target.
[<InlineData("arn:aws:braket:::device/quantum-simulator/amazon/sv1")>]
[<InlineData("simulation.qpu")>]
let ``isSimulatorTarget is false for hardware and billed emulators`` (target: string) =
    Assert.False(isSimulatorTarget target)

    match estimateCostSimple target 1000 with
    | Ok estimate -> Assert.True(estimate.ExpectedCost > 0.0M<USD>)
    | Error err -> Assert.Fail($"Expected success but got error: %s{err.Message}")

[<Fact>]
let ``the cost guard lets a provider simulator through and stops hardware above the limit`` () =
    let guard target =
        FSharp.Azure.Quantum.Backends.CloudBackendHelpers.checkCostGuard target 1000 (Some 1.0M)

    for target in [ "ionq.simulator"; "rigetti.sim.qvm"; "iqm.sim"; "atom-computing.sim" ] do
        match guard target with
        | Ok() -> ()
        | Error err -> Assert.Fail($"%s{target} should pass the guard, got %s{err.Message}")

    match guard "rigetti.qpu.ankaa-3" with
    | Error(QuantumError.AzureError(AzureQuantumError.QuotaExceeded _)) -> ()
    | other -> Assert.Fail($"expected QuotaExceeded, got %A{other}")

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
