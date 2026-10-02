namespace FSharp.Azure.Quantum.Tests

open Xunit
open FSharp.Azure.Quantum.Core.BackendAbstraction
open FSharp.Azure.Quantum.Backends

module QuantumKeyDistribution = FSharp.Azure.Quantum.Algorithms.QuantumKeyDistribution

module QuantumKeyDistributionTests =

    [<Fact>]
    let ``BB84 succeeds on LocalBackend without Eve (seeded)`` () =
        let backend = LocalBackend.LocalBackend() :> IQuantumBackend

        // Keep small-ish to stay fast but stable.
        let keyLength = 32
        let sampleRatio = 0.15
        let qberThreshold = 0.11
        let seed = Some 12345

        match QuantumKeyDistribution.runBB84 keyLength backend sampleRatio qberThreshold seed with
        | Error err -> Assert.Fail($"BB84 failed: {err}")
        | Ok result ->
            Assert.True(result.Success, "Expected BB84 to succeed without Eve")
            Assert.False(result.EavesdropCheck.EavesdropDetected, "Expected no eavesdropping detected")
            Assert.True(result.FinalKeyLength > 0, "Expected non-empty final key")

    [<Fact>]
    let ``BB84 detects Eve intercept-resend (seeded)`` () =
        let backend = LocalBackend.LocalBackend() :> IQuantumBackend

        // The seed fixes the bases and the check sample; Bob's outcomes are drawn afresh each
        // run. Of the 256 checked bits about 139 were measured by Eve in the wrong basis and
        // each of those is an error half the time, so 28 errors or fewer (a miss) is seven
        // standard deviations out.
        let keyLength = 1024
        let sampleRatio = 0.20
        let qberThreshold = 0.11
        let seed = Some 67890

        match QuantumKeyDistribution.runBB84WithEve keyLength backend sampleRatio qberThreshold seed with
        | Error err -> Assert.Fail($"BB84 with Eve failed: {err}")
        | Ok result ->
            Assert.True(result.EavesdropCheck.EavesdropDetected, "Expected eavesdropping to be detected")
            Assert.True(result.EavesdropCheck.ErrorRate > qberThreshold, "Expected QBER above threshold")

    /// A check sample is a set of distinct sifted-key positions of the size the ratio asks for.
    let private assertUniformSample (sampleRatio: float) (result: QuantumKeyDistribution.BB84Result) =
        let sifted = result.SiftedKey.Length
        let sample = result.EavesdropCheck.SampleIndices

        Assert.Equal(int (float sifted * sampleRatio), sample.Length)
        Assert.Equal(sample.Length, (Array.distinct sample).Length)
        Assert.All(sample, fun index -> Assert.InRange(index, 0, sifted - 1))
        Assert.Equal(sifted - sample.Length, result.FinalKeyLength)

    [<Fact>]
    let ``BB84 with a seed repeats its key and its check sample`` () =
        let run () =
            match
                QuantumKeyDistribution.runBB84 64 (LocalBackend.LocalBackend() :> IQuantumBackend) 0.2 0.11 (Some 2024)
            with
            | Ok result -> result
            | Error err -> failwith $"BB84 failed: {err}"

        let first = run ()
        let second = run ()

        assertUniformSample 0.2 first
        Assert.Equal<bool[]>(first.FinalKey, second.FinalKey)
        Assert.Equal<int[]>(first.EavesdropCheck.SampleIndices, second.EavesdropCheck.SampleIndices)

    [<Fact>]
    let ``BB84 without a seed succeeds and never repeats a run`` () =
        let run () =
            match QuantumKeyDistribution.runBB84 64 (LocalBackend.LocalBackend() :> IQuantumBackend) 0.2 0.11 None with
            | Ok result -> result
            | Error err -> failwith $"BB84 failed: {err}"

        let first = run ()
        let second = run ()

        for result in [ first; second ] do
            Assert.True(result.Success, "BB84 without an eavesdropper succeeds")
            Assert.Equal(0.0, result.EavesdropCheck.ErrorRate)
            assertUniformSample 0.2 result

        // Alice's sifted bits: two unseeded runs agreeing on all of them has probability 2^-64 or less.
        Assert.NotEqual<bool[]>(first.SiftedKey.AliceBits, second.SiftedKey.AliceBits)

    [<Theory>]
    [<InlineData(-0.5)>]
    [<InlineData(0.0)>]
    [<InlineData(1.0)>]
    [<InlineData(1.5)>]
    [<InlineData(System.Double.NaN)>]
    [<InlineData(0.9999999999)>]
    let ``BB84 refuses a sample ratio that leaves no check or no key`` (sampleRatio: float) =
        let backend () =
            LocalBackend.LocalBackend() :> IQuantumBackend

        let runs =
            [
                QuantumKeyDistribution.runBB84 64 (backend ()) sampleRatio 0.11 (Some 5)
                QuantumKeyDistribution.runBB84WithEve 64 (backend ()) sampleRatio 0.11 (Some 5)
                QuantumKeyDistribution.runBB84WithBeamsplitter 64 (backend ()) 0.5 sampleRatio 0.11 (Some 5)
            ]

        for run in runs do
            match run with
            | Error(FSharp.Azure.Quantum.Core.QuantumError.ValidationError(field, _)) ->
                Assert.Equal("sampleRatio", field)
            | other -> Assert.Fail($"expected a sampleRatio validation error, got {other}")

    [<Fact>]
    let ``BB84 refuses a run whose check sample comes out empty`` () =
        // 0.001 of a few dozen sifted bits rounds down to no bit: an unchecked key must not pass.
        match QuantumKeyDistribution.runBB84 16 (LocalBackend.LocalBackend() :> IQuantumBackend) 0.001 0.11 (Some 5) with
        | Error(FSharp.Azure.Quantum.Core.QuantumError.ValidationError(field, message)) ->
            Assert.Equal("sampleRatio", field)
            Assert.Contains("eavesdropping check", message)
        | other -> Assert.Fail($"expected a validation error, got {other}")

    [<Fact>]
    let ``BB84 refuses a key length below one`` () =
        match QuantumKeyDistribution.runBB84 0 (LocalBackend.LocalBackend() :> IQuantumBackend) 0.2 0.11 (Some 5) with
        | Error(FSharp.Azure.Quantum.Core.QuantumError.ValidationError(field, _)) -> Assert.Equal("keyLength", field)
        | other -> Assert.Fail($"expected a keyLength validation error, got {other}")
