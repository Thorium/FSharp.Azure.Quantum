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

        // Use larger key for stability.
        let keyLength = 256
        let sampleRatio = 0.20
        let qberThreshold = 0.11
        let seed = Some 67890

        match QuantumKeyDistribution.runBB84WithEve keyLength backend sampleRatio qberThreshold seed with
        | Error err -> Assert.Fail($"BB84 with Eve failed: {err}")
        | Ok result ->
            Assert.True(result.EavesdropCheck.EavesdropDetected, "Expected eavesdropping to be detected")
            Assert.True(result.EavesdropCheck.ErrorRate > qberThreshold, "Expected QBER above threshold")
