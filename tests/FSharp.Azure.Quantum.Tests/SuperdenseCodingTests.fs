module SuperdenseCodingTests

open Xunit
open FSharp.Azure.Quantum.Algorithms
open FSharp.Azure.Quantum.Core.BackendAbstraction
open FSharp.Azure.Quantum.Backends

let private createLocalBackend () : IQuantumBackend =
    LocalBackend.LocalBackend() :> IQuantumBackend

// ========================================================================
// Individual message tests (all 4 encoding cases)
// ========================================================================

[<Fact>]
let ``SuperdenseCoding.send00 returns correct bits`` () =
    let backend = createLocalBackend ()

    match SuperdenseCoding.send00 backend with
    | Error err -> Assert.Fail($"Expected Ok, got Error: {err}")
    | Ok result ->
        Assert.True(result.Success, $"Expected success, got received={result.ReceivedMessage.Bit1}{result.ReceivedMessage.Bit2}")
        Assert.Equal(0, result.ReceivedMessage.Bit1)
        Assert.Equal(0, result.ReceivedMessage.Bit2)

[<Fact>]
let ``SuperdenseCoding.send01 returns correct bits`` () =
    let backend = createLocalBackend ()

    match SuperdenseCoding.send01 backend with
    | Error err -> Assert.Fail($"Expected Ok, got Error: {err}")
    | Ok result ->
        Assert.True(result.Success, $"Expected success, got received={result.ReceivedMessage.Bit1}{result.ReceivedMessage.Bit2}")
        Assert.Equal(0, result.ReceivedMessage.Bit1)
        Assert.Equal(1, result.ReceivedMessage.Bit2)

[<Fact>]
let ``SuperdenseCoding.send10 returns correct bits`` () =
    let backend = createLocalBackend ()

    match SuperdenseCoding.send10 backend with
    | Error err -> Assert.Fail($"Expected Ok, got Error: {err}")
    | Ok result ->
        Assert.True(result.Success, $"Expected success, got received={result.ReceivedMessage.Bit1}{result.ReceivedMessage.Bit2}")
        Assert.Equal(1, result.ReceivedMessage.Bit1)
        Assert.Equal(0, result.ReceivedMessage.Bit2)

[<Fact>]
let ``SuperdenseCoding.send11 returns correct bits`` () =
    let backend = createLocalBackend ()

    match SuperdenseCoding.send11 backend with
    | Error err -> Assert.Fail($"Expected Ok, got Error: {err}")
    | Ok result ->
        Assert.True(result.Success, $"Expected success, got received={result.ReceivedMessage.Bit1}{result.ReceivedMessage.Bit2}")
        Assert.Equal(1, result.ReceivedMessage.Bit1)
        Assert.Equal(1, result.ReceivedMessage.Bit2)

// ========================================================================
// All four messages produce distinct results (anti-gaming)
// ========================================================================

[<Fact>]
let ``SuperdenseCoding.AllFourMessages produce distinct results`` () =
    let backend = createLocalBackend ()

    let messages : SuperdenseCoding.ClassicalMessage list = [
        { Bit1 = 0; Bit2 = 0 }
        { Bit1 = 0; Bit2 = 1 }
        { Bit1 = 1; Bit2 = 0 }
        { Bit1 = 1; Bit2 = 1 }
    ]

    let results =
        messages
        |> List.map (fun msg ->
            match SuperdenseCoding.send backend msg with
            | Error err -> failwith $"Unexpected error: {err}"
            | Ok result -> (result.ReceivedMessage.Bit1, result.ReceivedMessage.Bit2)
        )

    // All 4 received messages should be distinct
    let distinctResults = results |> List.distinct
    Assert.Equal(4, distinctResults.Length)

// ========================================================================
// Statistics test
// ========================================================================

[<Fact>]
let ``SuperdenseCoding.runStatistics all trials succeed on local backend`` () =
    let backend = createLocalBackend ()
    let message : SuperdenseCoding.ClassicalMessage = { Bit1 = 1; Bit2 = 0 }

    match SuperdenseCoding.runStatistics backend message 25 with
    | Error err -> Assert.Fail($"Expected Ok, got Error: {err}")
    | Ok stats ->
        Assert.Equal(25, stats.TotalTrials)
        Assert.Equal(25, stats.SuccessCount)
        Assert.Equal(1.0, stats.SuccessRate)

// ========================================================================
// Validation tests
// ========================================================================

[<Fact>]
let ``SuperdenseCoding.send rejects invalid bit values`` () =
    let backend = createLocalBackend ()

    let badMsg : SuperdenseCoding.ClassicalMessage = { Bit1 = 2; Bit2 = 0 }
    match SuperdenseCoding.send backend badMsg with
    | Ok _ -> Assert.Fail("Expected Error for invalid bit value")
    | Error _ -> () // Expected

[<Fact>]
let ``SuperdenseCoding.runStatistics rejects zero trials`` () =
    let backend = createLocalBackend ()
    let message : SuperdenseCoding.ClassicalMessage = { Bit1 = 0; Bit2 = 0 }

    match SuperdenseCoding.runStatistics backend message 0 with
    | Ok _ -> Assert.Fail("Expected Error for zero trials")
    | Error _ -> () // Expected
