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
        match
            QuantumKeyDistribution.runBB84 16 (LocalBackend.LocalBackend() :> IQuantumBackend) 0.001 0.11 (Some 5)
        with
        | Error(FSharp.Azure.Quantum.Core.QuantumError.ValidationError(field, message)) ->
            Assert.Equal("sampleRatio", field)
            Assert.Contains("eavesdropping check", message)
        | other -> Assert.Fail($"expected a validation error, got {other}")

    [<Fact>]
    let ``BB84 refuses a key length below one`` () =
        match QuantumKeyDistribution.runBB84 0 (LocalBackend.LocalBackend() :> IQuantumBackend) 0.2 0.11 (Some 5) with
        | Error(FSharp.Azure.Quantum.Core.QuantumError.ValidationError(field, _)) -> Assert.Equal("keyLength", field)
        | other -> Assert.Fail($"expected a keyLength validation error, got {other}")

    // ========================================================================
    // ERROR CORRECTION
    // ========================================================================

    /// A key of `length` seeded bits, and a copy with the bits at `errors` flipped
    let private keysWithErrors (length: int) (errors: int list) =
        let rng = System.Random(11)
        let alice = Array.init length (fun _ -> rng.Next 2 = 1)
        let bob = alice |> Array.mapi (fun i bit -> if List.contains i errors then not bit else bit)
        alice, bob

    [<Fact>]
    let ``errorCorrection locates the differing bit by bisection`` () =
        let alice, bob = keysWithErrors 16 [ 5 ]
        let corrected = QuantumKeyDistribution.errorCorrection alice bob 8

        Assert.True(corrected.Success)
        Assert.Equal<bool[]>(alice, corrected.CorrectedKey)
        Assert.Equal(1, corrected.ErrorsCorrected)
        // Two block parities, and three halvings of the 8-bit block that holds the error
        Assert.Equal(5.0, corrected.InformationLeaked)

    [<Fact>]
    let ``one pass leaves two errors of one block uncorrected and reports failure`` () =
        let alice, bob = keysWithErrors 16 [ 1; 2 ]
        let corrected = QuantumKeyDistribution.errorCorrection alice bob 8

        // Equal parities: the pass sees nothing, and does not claim success
        Assert.Equal(0, corrected.ErrorsDetected)
        Assert.False(corrected.Success)
        Assert.Equal<bool[]>(bob, corrected.CorrectedKey)

    [<Fact>]
    let ``Cascade reconciles scattered and paired errors and counts every revealed parity`` () =
        // 1 and 2 share a first-pass block; the later, shuffled passes separate them
        let errors = [ 1; 2; 40; 77; 78; 130; 131; 200; 249 ]
        let alice, bob = keysWithErrors 256 errors
        let corrected = QuantumKeyDistribution.cascadeErrorCorrection alice bob 8 4

        Assert.True(corrected.Success)
        Assert.Equal<bool[]>(alice, corrected.CorrectedKey)
        Assert.Equal<bool[]>(bob, corrected.OriginalKey)
        // Every bisection ends on a real error, so the corrections are exactly the errors
        Assert.Equal(errors.Length, corrected.ErrorsCorrected)
        // 32 + 16 + 8 + 4 block parities, and at least three halvings per error
        Assert.True(corrected.InformationLeaked >= float (60 + 3 * errors.Length))

    [<Fact>]
    let ``Cascade on equal keys corrects nothing and still pays for its block parities`` () =
        let alice, _ = keysWithErrors 64 []
        let corrected = QuantumKeyDistribution.cascadeErrorCorrection alice (Array.copy alice) 8 2

        Assert.True(corrected.Success)
        Assert.Equal(0, corrected.ErrorsCorrected)
        Assert.Equal(12.0, corrected.InformationLeaked) // 8 blocks of 8, then 4 of 16

    // ========================================================================
    // PRIVACY AMPLIFICATION
    // ========================================================================

    [<Fact>]
    let ``SHA-256 privacy amplification reports the margin it has, not a fixed 128 bits`` () =
        let key, _ = keysWithErrors 300 []

        let shortened = QuantumKeyDistribution.privacyAmplificationSHA256 key 100
        Assert.Equal(100, shortened.FinalLength)
        Assert.Equal(128, shortened.SecurityParameter) // 200 bits shorter, capped at 128

        let tight = QuantumKeyDistribution.privacyAmplificationSHA256 key 256
        Assert.Equal(44, tight.SecurityParameter)

        let unshortened =
            QuantumKeyDistribution.privacyAmplificationSHA256 (Array.truncate 256 key) 256

        Assert.Equal(0, unshortened.SecurityParameter)

    [<Fact>]
    let ``privacy amplification with nothing left returns no key and no security`` () =
        let key, _ = keysWithErrors 40 []
        let amplified = QuantumKeyDistribution.privacyAmplification key 20.0 32

        Assert.Empty(amplified.AmplifiedKey)
        Assert.Equal(0, amplified.SecurityParameter)

    // ========================================================================
    // COMPLETE PIPELINE
    // ========================================================================

    /// LocalBackend behind a channel that flips a transmitted qubit with the given
    /// probability: a Y on the freshly initialised qubit flips the bit Alice encodes, in
    /// both bases. The flips come from a seeded generator, so a seeded run repeats them;
    /// the sifted bits of a run are then fully determined (equal bases measure
    /// deterministically).
    type private NoisyChannelBackend(flipProbability: float, seed: int) =
        let inner = LocalBackend.LocalBackend() :> IQuantumBackend
        let rng = System.Random(seed)

        interface IQuantumBackend with
            member _.Name = "NoisyChannel"
            member _.NativeStateType = inner.NativeStateType
            member _.SupportsOperation op = inner.SupportsOperation op

            member _.InitializeState n =
                inner.InitializeState n
                |> Result.bind (fun state ->
                    if rng.NextDouble() < flipProbability then
                        inner.ApplyOperation (QuantumOperation.Gate(FSharp.Azure.Quantum.CircuitBuilder.Y 0)) state
                    else
                        Ok state)

            member _.ApplyOperation op state = inner.ApplyOperation op state
            member _.ExecuteToState circuit = inner.ExecuteToState circuit
            member _.ExecuteToStateAsync circuit ct = inner.ExecuteToStateAsync circuit ct
            member _.ApplyOperationAsync op state ct = inner.ApplyOperationAsync op state ct

    let private noisy (flipProbability: float) =
        NoisyChannelBackend(flipProbability, 2025) :> IQuantumBackend

    [<Fact>]
    let ``complete QKD on a clean channel gives both sides one key, shortened by what was revealed`` () =
        let backend = LocalBackend.LocalBackend() :> IQuantumBackend

        match QuantumKeyDistribution.runCompleteQKD 256 backend true 64 (Some 3) with
        | Error err -> Assert.Fail($"pipeline failed: {err}")
        | Ok qkd ->
            Assert.True(qkd.Success)
            Assert.Equal<bool[]>(qkd.FinalSecureKey, qkd.BobFinalSecureKey)

            let rawLength = qkd.BB84Result.FinalKeyLength
            let correction = qkd.ErrorCorrection.Value
            Assert.Equal(0, correction.ErrorsCorrected)
            Assert.True(correction.InformationLeaked > 0.0)

            // No error in the sample: Eve's share is the revealed parities alone
            Assert.Equal(correction.InformationLeaked, qkd.TotalInformationLeaked)
            Assert.Equal(min 256 (rawLength - int correction.InformationLeaked - 64), qkd.FinalKeyLength)
            Assert.True(qkd.FinalKeyLength < rawLength)
            Assert.InRange(qkd.SecurityLevel, 64, 128)

    [<Fact>]
    let ``on a noisy channel Alice and Bob share a key only because error correction ran`` () =
        // About 4% of the transmitted bits flip: below the 11% threshold
        match QuantumKeyDistribution.runCompleteQKD 400 (noisy 0.04) true 32 (Some 17) with
        | Error err -> Assert.Fail($"pipeline failed: {err}")
        | Ok qkd ->
            let check = qkd.BB84Result.EavesdropCheck
            Assert.InRange(check.ErrorRate, 0.0001, 0.11)
            Assert.False(check.EavesdropDetected)

            // The raw keys differ: Bob's is what he measured, not a copy of Alice's
            let aliceRaw = qkd.BB84Result.FinalKey

            let bobRaw =
                QuantumKeyDistribution.extractBobFinalKey qkd.BB84Result.SiftedKey check.SampleIndices

            let rawErrors =
                Array.fold2 (fun count a b -> if a <> b then count + 1 else count) 0 aliceRaw bobRaw

            Assert.True(rawErrors > 0, "the channel flipped no bit of the raw key")

            let correction = qkd.ErrorCorrection.Value
            Assert.True(correction.Success)
            Assert.Equal(rawErrors, correction.ErrorsCorrected)
            Assert.Equal<bool[]>(bobRaw, correction.OriginalKey)
            Assert.Equal<bool[]>(aliceRaw, correction.CorrectedKey)

            // One key on both sides, shorter than the raw key by Eve's share and the margin
            Assert.Equal<bool[]>(qkd.FinalSecureKey, qkd.BobFinalSecureKey)
            Assert.True(qkd.TotalInformationLeaked > correction.InformationLeaked)

            Assert.Equal(
                min 256 (int (floor (float aliceRaw.Length - qkd.TotalInformationLeaked - 32.0))),
                qkd.FinalKeyLength
            )

            Assert.InRange(qkd.SecurityLevel, 32, 128)

        // The same run without error correction has no shared key to return
        match QuantumKeyDistribution.runCompleteQKD 400 (noisy 0.04) false 32 (Some 17) with
        | Error(FSharp.Azure.Quantum.Core.QuantumError.OperationError(operation, message)) ->
            Assert.Equal("KeyConfirmation", operation)
            Assert.Contains("differ", message)
        | other -> Assert.Fail($"expected a KeyConfirmation error, got {other}")

    [<Fact>]
    let ``a channel disturbed like an intercept-resend attack is refused, with or without error correction`` () =
        // An intercept-resend eavesdropper flips a quarter of the sifted bits
        for errorCorrection in [ true; false ] do
            match QuantumKeyDistribution.runCompleteQKD 400 (noisy 0.25) errorCorrection 32 (Some 17) with
            | Error(FSharp.Azure.Quantum.Core.QuantumError.ValidationError(field, _)) ->
                Assert.Equal("eavesdropping", field)
            | other -> Assert.Fail($"expected the eavesdropping error, got {other}")

    [<Fact>]
    let ``complete QKD is an Error when the key is too short for the security parameter`` () =
        let backend = LocalBackend.LocalBackend() :> IQuantumBackend

        match QuantumKeyDistribution.runCompleteQKD 32 backend true 128 (Some 7) with
        | Error(FSharp.Azure.Quantum.Core.QuantumError.ValidationError(field, message)) ->
            Assert.Equal("keyLength", field)
            Assert.Contains("no secure bit remains", message)
        | other -> Assert.Fail($"expected a keyLength validation error, got {other}")

        match QuantumKeyDistribution.runCompleteQKD 32 backend true (-1) (Some 7) with
        | Error(FSharp.Azure.Quantum.Core.QuantumError.ValidationError(field, _)) ->
            Assert.Equal("securityParameter", field)
        | other -> Assert.Fail($"expected a securityParameter validation error, got {other}")
