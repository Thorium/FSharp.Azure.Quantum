module EkertQKDTests

open Xunit
open FSharp.Azure.Quantum.Algorithms
open FSharp.Azure.Quantum.Core.BackendAbstraction
open FSharp.Azure.Quantum.Backends

let private createLocalBackend () : IQuantumBackend =
    LocalBackend.LocalBackend() :> IQuantumBackend

// ========================================================================
// CHSH violation tests (no eavesdropper)
// ========================================================================

[<Fact>]
let ``EkertQKD.run NoEavesdropper CHSH violated`` () =
    let backend = createLocalBackend ()

    // Use 500 pairs with seed for reproducibility; quantum bound |S| ~ 2.828
    match EkertQKD.run backend 500 (Some 42) with
    | Error err -> Assert.Fail($"Expected Ok, got Error: {err}")
    | Ok result ->
        // |S| should exceed classical bound 2.0 (quantum violation)
        let absS = abs result.CHSHTest.S
        Assert.True(absS > 2.0, $"Expected |S| > 2.0 (CHSH violation), got |S| = {absS}")
        Assert.True(result.CHSHTest.IsSecure, $"Expected IsSecure = true, S = {result.CHSHTest.S}")
        Assert.True(result.IsSecure, $"Expected overall IsSecure = true")

// ========================================================================
// Key bit correlation tests (no eavesdropper)
// ========================================================================

[<Fact>]
let ``EkertQKD.run NoEavesdropper key bits correlated`` () =
    let backend = createLocalBackend ()

    match EkertQKD.run backend 500 (Some 123) with
    | Error err -> Assert.Fail($"Expected Ok, got Error: {err}")
    | Ok result ->
        // On matching bases (0,0) and (45,45), Alice and Bob should get same results
        // For |Phi+> = (|00>+|11>)/sqrt(2), same-basis measurements are perfectly correlated
        let matchingPairs =
            result.Pairs
            |> List.filter (fun p ->
                match (p.AliceBasis, p.BobBasis) with
                | (EkertQKD.AliceDeg0, EkertQKD.BobDeg0) -> true
                | (EkertQKD.AliceDeg45, EkertQKD.BobDeg45) -> true
                | _ -> false)

        let correlatedCount =
            matchingPairs
            |> List.filter (fun p -> p.AliceResult = p.BobResult)
            |> List.length

        // With quantum simulation, matching bases should yield >80% correlation
        // (ideally 100% for perfect Bell pairs, but simulation noise may lower it)
        let correlationRate = float correlatedCount / float matchingPairs.Length
        Assert.True(
            correlationRate > 0.75,
            $"Expected high correlation on matching bases, got {correlationRate:F3} ({correlatedCount}/{matchingPairs.Length})")

// ========================================================================
// Eavesdropper detection
// ========================================================================

[<Fact>]
let ``EkertQKD.runWithEve CHSH reduced by eavesdropper`` () =
    let backend = createLocalBackend ()

    // Run with Eve intercepting - should destroy quantum correlations
    match EkertQKD.runWithEve backend 500 (Some 42) with
    | Error err -> Assert.Fail($"Expected Ok, got Error: {err}")
    | Ok eveResult ->
        // Also run without Eve for comparison
        match EkertQKD.run backend 500 (Some 42) with
        | Error err -> Assert.Fail($"Expected Ok for no-Eve run, got Error: {err}")
        | Ok noEveResult ->
            // With Eve, |S| should be lower than without Eve
            let absSWithEve = abs eveResult.CHSHTest.S
            let absSNoEve = abs noEveResult.CHSHTest.S
            Assert.True(
                absSWithEve < absSNoEve,
                $"Expected |S| with Eve ({absSWithEve:F4}) < |S| without Eve ({absSNoEve:F4})")

// ========================================================================
// Key rate test
// ========================================================================

[<Fact>]
let ``EkertQKD.run sifted key rate approximately correct`` () =
    let backend = createLocalBackend ()

    match EkertQKD.run backend 900 (Some 77) with
    | Error err -> Assert.Fail($"Expected Ok, got Error: {err}")
    | Ok result ->
        // Expected key rate: 2/9 ~ 0.222 (2 matching basis combinations out of 9)
        // With random basis selection, allow generous tolerance
        Assert.True(
            result.KeyRate > 0.10 && result.KeyRate < 0.40,
            $"Expected key rate ~22%% (0.10-0.40), got {result.KeyRate:F4} ({result.SiftedKeyLength} of {result.TotalPairs})")

// ========================================================================
// Security detection
// ========================================================================

[<Fact>]
let ``EkertQKD.runWithEve detects eavesdropper via CHSH`` () =
    let backend = createLocalBackend ()

    match EkertQKD.runWithEve backend 500 (Some 99) with
    | Error err -> Assert.Fail($"Expected Ok, got Error: {err}")
    | Ok result ->
        // Eve's intercept-resend should break entanglement
        // |S| should drop near or below classical bound 2.0
        let absS = abs result.CHSHTest.S
        Assert.True(
            absS < 2.5,
            $"Expected |S| < 2.5 with Eve (intercept-resend should reduce), got |S| = {absS:F4}")

// ========================================================================
// All basis combinations used
// ========================================================================

[<Fact>]
let ``EkertQKD.run uses all 9 basis combinations`` () =
    let backend = createLocalBackend ()

    // With 900 pairs and random choices, all 9 combos should appear (~100 each)
    match EkertQKD.run backend 900 (Some 55) with
    | Error err -> Assert.Fail($"Expected Ok, got Error: {err}")
    | Ok result ->
        let basisCombinations =
            result.Pairs
            |> List.map (fun p -> (p.AliceBasis, p.BobBasis))
            |> List.distinct

        Assert.Equal(9, basisCombinations.Length)

// ========================================================================
// Validation tests
// ========================================================================

[<Fact>]
let ``EkertQKD.run rejects zero pairs`` () =
    let backend = createLocalBackend ()

    match EkertQKD.run backend 0 None with
    | Ok _ -> Assert.Fail("Expected Error for zero pairs")
    | Error _ -> () // Expected: validation error

[<Fact>]
let ``EkertQKD.run rejects negative pairs`` () =
    let backend = createLocalBackend ()

    match EkertQKD.run backend -5 None with
    | Ok _ -> Assert.Fail("Expected Error for negative pairs")
    | Error _ -> () // Expected: validation error

// ========================================================================
// Formatting tests (smoke tests)
// ========================================================================

[<Fact>]
let ``EkertQKD.formatResult produces non-empty output`` () =
    let backend = createLocalBackend ()

    match EkertQKD.run backend 50 (Some 42) with
    | Error err -> Assert.Fail($"Expected Ok, got Error: {err}")
    | Ok result ->
        let formatted = EkertQKD.formatResult result
        Assert.False(System.String.IsNullOrWhiteSpace(formatted), "Expected non-empty formatted output")
        Assert.Contains("E91 Quantum Key Distribution Result", formatted)
        Assert.Contains("CHSH Inequality Test", formatted)

[<Fact>]
let ``EkertQKD.formatBasisTable shows all combinations`` () =
    let table = EkertQKD.formatBasisTable ()
    Assert.Contains("E91 Basis Combinations", table)
    Assert.Contains("KEY", table)
    Assert.Contains("CHSH", table)

[<Fact>]
let ``EkertQKD.computeCHSH on empty list returns zero S`` () =
    let chsh = EkertQKD.computeCHSH []
    Assert.Equal(0.0, chsh.S)
    Assert.True(chsh.EavesdropperDetected, "Empty pairs should show eavesdropper detected (|S|=0 <= 2)")
