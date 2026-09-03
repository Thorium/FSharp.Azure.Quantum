namespace FSharp.Azure.Quantum.Tests

open System
open Xunit
open FSharp.Azure.Quantum.Algorithms.QuantumDistributions

module QuantumDistributionsComprehensiveTests =

    // ========================================================================
    // DISTRIBUTION VALIDATION (via sample)
    // ========================================================================

    [<Fact>]
    let ``sample Normal rejects non-positive stddev`` () =
        (sample (Normal(0.0, 0.0))) |> Result.map (fun _ -> failwith "Expected Error for zero stddev") |> Result.defaultWith (fun msg -> Assert.Contains("positive", msg.ToLower()))

    [<Fact>]
    let ``sample Normal rejects negative stddev`` () =
        (sample (Normal(0.0, -1.0))) |> Result.map (fun _ -> failwith "Expected Error for negative stddev") |> Result.defaultWith (fun msg -> Assert.Contains("positive", msg.ToLower()))

    [<Fact>]
    let ``sample Normal rejects NaN mean`` () =
        (sample (Normal(Double.NaN, 1.0))) |> Result.map (fun _ -> failwith "Expected Error for NaN mean") |> Result.defaultWith (fun msg -> Assert.Contains("finite", msg.ToLower()))

    [<Fact>]
    let ``sample Exponential rejects non-positive lambda`` () =
        (sample (Exponential(0.0))) |> Result.map (fun _ -> failwith "Expected Error for zero lambda") |> Result.defaultWith (fun msg -> Assert.Contains("positive", msg.ToLower()))

    [<Fact>]
    let ``sample Uniform rejects min >= max`` () =
        (sample (Uniform(5.0, 3.0))) |> Result.map (fun _ -> failwith "Expected Error for min >= max") |> Result.defaultWith (fun msg -> Assert.Contains("min", msg.ToLower()))

    [<Fact>]
    let ``sample LogNormal rejects non-positive sigma`` () =
        (sample (LogNormal(0.0, -1.0))) |> Result.map (fun _ -> failwith "Expected Error for negative sigma") |> Result.defaultWith (fun msg -> Assert.Contains("positive", msg.ToLower()))

    [<Fact>]
    let ``sample Custom rejects empty name`` () =
        (sample (Custom("", id))) |> Result.map (fun _ -> failwith "Expected Error for empty name") |> Result.defaultWith (fun msg -> Assert.Contains("name", msg.ToLower()))

    // ========================================================================
    // SUCCESSFUL SAMPLING
    // ========================================================================

    [<Fact>]
    let ``sample StandardNormal returns finite value`` () =
        match sample StandardNormal with
        | Ok result ->
            Assert.True(Double.IsFinite(result.Value), $"Value {result.Value} should be finite")
            Assert.Equal(53, result.QuantumBitsUsed)
        | Error msg -> failwith $"Expected Ok, got Error: {msg}"

    [<Fact>]
    let ``sample Normal returns finite value`` () =
        (sample (Normal(10.0, 2.0))) |> Result.map (fun result -> Assert.True(Double.IsFinite(result.Value))) |> Result.defaultWith (fun msg -> failwith $"Expected Ok, got Error: {msg}")

    [<Fact>]
    let ``sample Exponential returns positive value`` () =
        (sample (Exponential(1.0))) |> Result.map (fun result -> Assert.True(result.Value > 0.0, $"Exponential sample {result.Value} should be positive")) |> Result.defaultWith (fun msg -> failwith $"Expected Ok, got Error: {msg}")

    [<Fact>]
    let ``sample LogNormal returns positive value`` () =
        (sample (LogNormal(0.0, 1.0))) |> Result.map (fun result -> Assert.True(result.Value > 0.0, $"LogNormal sample {result.Value} should be positive")) |> Result.defaultWith (fun msg -> failwith $"Expected Ok, got Error: {msg}")

    [<Fact>]
    let ``sample Uniform returns value in range`` () =
        match sample (Uniform(2.0, 5.0)) with
        | Ok result ->
            Assert.True(result.Value >= 2.0 && result.Value <= 5.0,
                $"Uniform sample {result.Value} should be in [2, 5]")
        | Error msg -> failwith $"Expected Ok, got Error: {msg}"

    [<Fact>]
    let ``sample Custom applies transform`` () =
        let transform (u: float) = u * 100.0
        match sample (Custom("scale100", transform)) with
        | Ok result ->
            Assert.True(result.Value >= 0.0 && result.Value <= 100.0,
                $"Custom sample {result.Value} should be in [0, 100]")
        | Error msg -> failwith $"Expected Ok, got Error: {msg}"

    // ========================================================================
    // SAMPLE MANY
    // ========================================================================

    [<Fact>]
    let ``sampleMany returns correct count`` () =
        (sampleMany StandardNormal 10) |> Result.map (fun samples -> Assert.Equal(10, samples.Length)) |> Result.defaultWith (fun msg -> failwith $"Expected Ok, got Error: {msg}")

    [<Fact>]
    let ``sampleMany rejects non-positive count`` () =
        (sampleMany StandardNormal 0) |> Result.map (fun _ -> failwith "Expected Error for zero count") |> Result.defaultWith (fun msg -> Assert.Contains("positive", msg.ToLower()))

    [<Fact>]
    let ``sampleMany rejects count > 1000000`` () =
        (sampleMany StandardNormal 1000001) |> Result.map (fun _ -> failwith "Expected Error for too-large count") |> Result.defaultWith (fun msg -> Assert.Contains("large", msg.ToLower()))

    // ========================================================================
    // STATISTICAL UTILITIES
    // ========================================================================

    [<Fact>]
    let ``computeStatistics computes correct mean`` () =
        let samples = [|
            { Value = 1.0; Distribution = StandardNormal; QuantumBitsUsed = 53 }
            { Value = 2.0; Distribution = StandardNormal; QuantumBitsUsed = 53 }
            { Value = 3.0; Distribution = StandardNormal; QuantumBitsUsed = 53 }
        |]
        let stats = computeStatistics samples
        Assert.True(abs(stats.Mean - 2.0) < 1e-10)
        Assert.Equal(3, stats.Count)
        Assert.True(abs(stats.Min - 1.0) < 1e-10)
        Assert.True(abs(stats.Max - 3.0) < 1e-10)

    [<Fact>]
    let ``computeStatistics throws on empty array`` () =
        Assert.Throws<Exception>(fun () ->
            computeStatistics [||] |> ignore) |> ignore

    // ========================================================================
    // DISTRIBUTION HELPERS
    // ========================================================================

    [<Fact>]
    let ``distributionName returns descriptive string`` () =
        Assert.Contains("Normal", distributionName (Normal(0.0, 1.0)))
        Assert.Contains("StandardNormal", distributionName StandardNormal)
        Assert.Contains("LogNormal", distributionName (LogNormal(0.0, 1.0)))
        Assert.Contains("Exponential", distributionName (Exponential(1.0)))
        Assert.Contains("Uniform", distributionName (Uniform(0.0, 1.0)))
        Assert.Contains("Custom", distributionName (Custom("test", id)))

    [<Fact>]
    let ``expectedMean returns correct values`` () =
        Assert.Equal(Some 5.0, expectedMean (Normal(5.0, 1.0)))
        Assert.Equal(Some 0.0, expectedMean StandardNormal)
        Assert.Equal(Some 0.5, expectedMean (Exponential(2.0)))
        Assert.Equal(Some 3.0, expectedMean (Uniform(1.0, 5.0)))
        Assert.Equal(None, expectedMean (Custom("test", id)))

    [<Fact>]
    let ``expectedStdDev returns correct values`` () =
        Assert.Equal(Some 2.0, expectedStdDev (Normal(0.0, 2.0)))
        Assert.Equal(Some 1.0, expectedStdDev StandardNormal)
        Assert.Equal(None, expectedStdDev (Custom("test", id)))

    [<Fact>]
    let ``expectedMean LogNormal is correct`` () =
        // E[X] = exp(mu + sigma^2/2)
        let mu = 0.0
        let sigma = 1.0
        let expected = exp(mu + sigma * sigma / 2.0)
        match expectedMean (LogNormal(mu, sigma)) with
        | Some m -> Assert.True(abs(m - expected) < 1e-10)
        | None -> failwith "Expected Some"
