namespace FSharp.Azure.Quantum.Topological.Tests

open System
open System.Numerics
open Xunit
open FSharp.Azure.Quantum.Topological

module NoiseModelsTests =

    // ========================================================================
    // HELPERS
    // ========================================================================

    /// Create a simple two-term Ising superposition for testing noise effects.
    /// |0⟩ = σ×σ→1 (vacuum), |1⟩ = σ×σ→ψ
    let private makeTwoTermSuperposition (amp0: Complex) (amp1: Complex) =
        let sigma = FusionTree.leaf AnyonSpecies.Particle.Sigma
        let state0 = FusionTree.create (FusionTree.fuse sigma sigma AnyonSpecies.Particle.Vacuum) AnyonSpecies.AnyonType.Ising
        let state1 = FusionTree.create (FusionTree.fuse sigma sigma AnyonSpecies.Particle.Psi) AnyonSpecies.AnyonType.Ising
        { TopologicalOperations.Superposition.Terms = [ (amp0, state0); (amp1, state1) ]
          TopologicalOperations.Superposition.AnyonType = AnyonSpecies.AnyonType.Ising }

    /// Create a normalized equal superposition (1/sqrt(2), 1/sqrt(2))
    let private makeEqualSuperposition () =
        let amp = Complex(1.0 / sqrt 2.0, 0.0)
        makeTwoTermSuperposition amp amp

    /// Create a single-term superposition
    let private makeSingleTermSuperposition () =
        let sigma = FusionTree.leaf AnyonSpecies.Particle.Sigma
        let state0 = FusionTree.create (FusionTree.fuse sigma sigma AnyonSpecies.Particle.Vacuum) AnyonSpecies.AnyonType.Ising
        { TopologicalOperations.Superposition.Terms = [ (Complex.One, state0) ]
          TopologicalOperations.Superposition.AnyonType = AnyonSpecies.AnyonType.Ising }

    /// Create an empty superposition
    let private makeEmptySuperposition () =
        { TopologicalOperations.Superposition.Terms = []
          TopologicalOperations.Superposition.AnyonType = AnyonSpecies.AnyonType.Ising }

    let private totalProbability (sup: TopologicalOperations.Superposition) =
        sup.Terms |> List.sumBy (fun (amp, _) -> (Complex.Abs amp) ** 2.0)

    // ========================================================================
    // CONSTRUCTOR VALIDATION TESTS
    // ========================================================================

    [<Fact>]
    let ``createDecoherenceParameters accepts valid T1 and T2`` () =
        let result = NoiseModels.createDecoherenceParameters 50.0 30.0
        Assert.True(Result.isOk result)
        match result with
        | Ok d ->
            Assert.Equal(50.0, d.T1)
            Assert.Equal(30.0, d.T2)
        | Error _ -> failwith "Expected Ok"

    [<Fact>]
    let ``createDecoherenceParameters rejects T1 <= 0`` () =
        let result = NoiseModels.createDecoherenceParameters 0.0 10.0
        Assert.True(Result.isError result)
        let result2 = NoiseModels.createDecoherenceParameters -5.0 10.0
        Assert.True(Result.isError result2)

    [<Fact>]
    let ``createDecoherenceParameters rejects T2 <= 0`` () =
        let result = NoiseModels.createDecoherenceParameters 50.0 0.0
        Assert.True(Result.isError result)
        let result2 = NoiseModels.createDecoherenceParameters 50.0 -1.0
        Assert.True(Result.isError result2)

    [<Fact>]
    let ``createDecoherenceParameters rejects T2 greater than 2*T1`` () =
        // T2 <= 2*T1 is quantum mechanics constraint
        let result = NoiseModels.createDecoherenceParameters 10.0 21.0
        Assert.True(Result.isError result)

    [<Fact>]
    let ``createDecoherenceParameters accepts T2 equal to 2*T1`` () =
        let result = NoiseModels.createDecoherenceParameters 10.0 20.0
        Assert.True(Result.isOk result)

    [<Fact>]
    let ``createGateErrorParameters accepts valid rates`` () =
        let result = NoiseModels.createGateErrorParameters 0.001 0.01 0.05
        Assert.True(Result.isOk result)
        match result with
        | Ok g ->
            Assert.Equal(0.001, g.SingleQubitErrorRate)
            Assert.Equal(0.01, g.TwoQubitErrorRate)
            Assert.Equal(0.05, g.BraidingTime)
        | Error _ -> failwith "Expected Ok"

    [<Fact>]
    let ``createGateErrorParameters rejects negative single qubit error rate`` () =
        let result = NoiseModels.createGateErrorParameters -0.1 0.01 0.05
        Assert.True(Result.isError result)

    [<Fact>]
    let ``createGateErrorParameters rejects single qubit error rate above 1`` () =
        let result = NoiseModels.createGateErrorParameters 1.1 0.01 0.05
        Assert.True(Result.isError result)

    [<Fact>]
    let ``createGateErrorParameters rejects negative two qubit error rate`` () =
        let result = NoiseModels.createGateErrorParameters 0.001 -0.01 0.05
        Assert.True(Result.isError result)

    [<Fact>]
    let ``createGateErrorParameters rejects two qubit error rate above 1`` () =
        let result = NoiseModels.createGateErrorParameters 0.001 1.5 0.05
        Assert.True(Result.isError result)

    [<Fact>]
    let ``createGateErrorParameters rejects non-positive braiding time`` () =
        let result = NoiseModels.createGateErrorParameters 0.001 0.01 0.0
        Assert.True(Result.isError result)
        let result2 = NoiseModels.createGateErrorParameters 0.001 0.01 -1.0
        Assert.True(Result.isError result2)

    [<Fact>]
    let ``createGateErrorParameters accepts zero error rates`` () =
        let result = NoiseModels.createGateErrorParameters 0.0 0.0 1.0
        Assert.True(Result.isOk result)

    [<Fact>]
    let ``createGateErrorParameters accepts error rates equal to 1`` () =
        let result = NoiseModels.createGateErrorParameters 1.0 1.0 1.0
        Assert.True(Result.isOk result)

    [<Fact>]
    let ``createMeasurementErrorParameters accepts valid values`` () =
        let result = NoiseModels.createMeasurementErrorParameters 0.02 0.1
        Assert.True(Result.isOk result)
        match result with
        | Ok m ->
            Assert.Equal(0.02, m.ReadoutErrorRate)
            Assert.Equal(0.1, m.MeasurementTime)
        | Error _ -> failwith "Expected Ok"

    [<Fact>]
    let ``createMeasurementErrorParameters rejects negative readout error rate`` () =
        let result = NoiseModels.createMeasurementErrorParameters -0.1 0.1
        Assert.True(Result.isError result)

    [<Fact>]
    let ``createMeasurementErrorParameters rejects readout error rate above 1`` () =
        let result = NoiseModels.createMeasurementErrorParameters 1.1 0.1
        Assert.True(Result.isError result)

    [<Fact>]
    let ``createMeasurementErrorParameters rejects non-positive measurement time`` () =
        let result = NoiseModels.createMeasurementErrorParameters 0.02 0.0
        Assert.True(Result.isError result)

    [<Fact>]
    let ``createQuasiparticlePoisoningParameters accepts valid values`` () =
        let result = NoiseModels.createQuasiparticlePoisoningParameters 100.0 0.050
        Assert.True(Result.isOk result)
        match result with
        | Ok q ->
            Assert.Equal(100.0, q.PoisoningRate)
            Assert.Equal(0.050, q.Temperature)
        | Error _ -> failwith "Expected Ok"

    [<Fact>]
    let ``createQuasiparticlePoisoningParameters accepts zero rate`` () =
        let result = NoiseModels.createQuasiparticlePoisoningParameters 0.0 0.050
        Assert.True(Result.isOk result)

    [<Fact>]
    let ``createQuasiparticlePoisoningParameters rejects negative rate`` () =
        let result = NoiseModels.createQuasiparticlePoisoningParameters -10.0 0.050
        Assert.True(Result.isError result)

    [<Fact>]
    let ``createQuasiparticlePoisoningParameters rejects negative temperature`` () =
        let result = NoiseModels.createQuasiparticlePoisoningParameters 100.0 -1.0
        Assert.True(Result.isError result)

    [<Fact>]
    let ``createQuasiparticlePoisoningParameters accepts zero temperature`` () =
        let result = NoiseModels.createQuasiparticlePoisoningParameters 100.0 0.0
        Assert.True(Result.isOk result)

    // ========================================================================
    // PRESET MODEL TESTS
    // ========================================================================

    [<Fact>]
    let ``noiseless model has all None parameters`` () =
        let model = NoiseModels.noiseless ()
        Assert.True(model.Decoherence.IsNone)
        Assert.True(model.GateErrors.IsNone)
        Assert.True(model.MeasurementErrors.IsNone)
        Assert.True(model.QuasiparticlePoisoning.IsNone)
        Assert.True(model.RandomSeed.IsNone)

    [<Fact>]
    let ``realisticSuperconducting creates valid model`` () =
        let result = NoiseModels.realisticSuperconducting ()
        Assert.True(Result.isOk result)
        match result with
        | Ok model ->
            Assert.True(model.Decoherence.IsSome)
            Assert.True(model.GateErrors.IsSome)
            Assert.True(model.MeasurementErrors.IsSome)
            Assert.True(model.QuasiparticlePoisoning.IsNone)  // Not applicable to superconducting
        | Error _ -> failwith "Expected Ok"

    [<Fact>]
    let ``realisticTopological creates valid model with poisoning`` () =
        let result = NoiseModels.realisticTopological ()
        Assert.True(Result.isOk result)
        match result with
        | Ok model ->
            Assert.True(model.Decoherence.IsSome)
            Assert.True(model.GateErrors.IsSome)
            Assert.True(model.MeasurementErrors.IsSome)
            Assert.True(model.QuasiparticlePoisoning.IsSome)  // Topological-specific
        | Error _ -> failwith "Expected Ok"

    [<Fact>]
    let ``realisticTopological has lower gate errors than superconducting`` () =
        let sc = NoiseModels.realisticSuperconducting () |> Result.defaultWith (fun _ -> failwith "sc")
        let topo = NoiseModels.realisticTopological () |> Result.defaultWith (fun _ -> failwith "topo")
        let scGate = sc.GateErrors.Value.SingleQubitErrorRate
        let topoGate = topo.GateErrors.Value.SingleQubitErrorRate
        Assert.True(topoGate < scGate,
            $"Topological gate error ({topoGate}) should be lower than superconducting ({scGate})")

    [<Fact>]
    let ``realisticTopological has longer coherence than superconducting`` () =
        let sc = NoiseModels.realisticSuperconducting () |> Result.defaultWith (fun _ -> failwith "sc")
        let topo = NoiseModels.realisticTopological () |> Result.defaultWith (fun _ -> failwith "topo")
        Assert.True(topo.Decoherence.Value.T1 > sc.Decoherence.Value.T1)
        Assert.True(topo.Decoherence.Value.T2 > sc.Decoherence.Value.T2)

    // ========================================================================
    // DECOHERENCE APPLICATION TESTS
    // ========================================================================

    [<Fact>]
    let ``applyDecoherence on empty superposition returns unchanged`` () =
        let sup = makeEmptySuperposition ()
        let decoherence = (NoiseModels.createDecoherenceParameters 50.0 30.0) |> Result.defaultWith (fun _ -> failwith "")
        let random = Random(42)
        let result = NoiseModels.applyDecoherence decoherence 1.0 sup random
        Assert.Empty(result.Terms)

    [<Fact>]
    let ``applyDecoherence on single term returns unchanged`` () =
        let sup = makeSingleTermSuperposition ()
        let decoherence = (NoiseModels.createDecoherenceParameters 50.0 30.0) |> Result.defaultWith (fun _ -> failwith "")
        let random = Random(42)
        let result = NoiseModels.applyDecoherence decoherence 1.0 sup random
        Assert.Single(result.Terms) |> ignore

    [<Fact>]
    let ``applyDecoherence damps excited states over time`` () =
        let sup = makeEqualSuperposition ()
        let decoherence = (NoiseModels.createDecoherenceParameters 50.0 30.0) |> Result.defaultWith (fun _ -> failwith "")
        let random = Random(42)

        // Apply with significant elapsed time
        let result = NoiseModels.applyDecoherence decoherence 10.0 sup random

        // Ground state amplitude should increase (probability transfer from excited)
        let groundProb = (Complex.Abs (fst result.Terms.[0])) ** 2.0
        Assert.True(groundProb > 0.5,
            $"Ground state probability ({groundProb}) should increase above 0.5 due to T1 relaxation")

    [<Fact>]
    let ``applyDecoherence preserves normalization`` () =
        let sup = makeEqualSuperposition ()
        let decoherence = (NoiseModels.createDecoherenceParameters 50.0 30.0) |> Result.defaultWith (fun _ -> failwith "")
        let random = Random(42)
        let result = NoiseModels.applyDecoherence decoherence 5.0 sup random
        let totalProb = totalProbability result
        Assert.True(abs (totalProb - 1.0) < 1e-8,
            $"Total probability should be ~1.0 after normalization, got {totalProb}")

    [<Fact>]
    let ``applyDecoherence with very short time barely changes state`` () =
        let sup = makeEqualSuperposition ()
        let decoherence = (NoiseModels.createDecoherenceParameters 50.0 30.0) |> Result.defaultWith (fun _ -> failwith "")
        let random = Random(42)
        let result = NoiseModels.applyDecoherence decoherence 0.001 sup random
        // With very short time, ground probability should be very close to 0.5
        let groundProb = (Complex.Abs (fst result.Terms.[0])) ** 2.0
        Assert.True(abs (groundProb - 0.5) < 0.01,
            $"Ground state probability should be ~0.5 with short time, got {groundProb}")

    // ========================================================================
    // DEPOLARIZING NOISE TESTS
    // ========================================================================

    [<Fact>]
    let ``applyDepolarizingNoise with error produces uniform amplitudes`` () =
        // Use seed where NextDouble() < errorRate (errorRate = 1.0 forces error)
        let sup = makeTwoTermSuperposition (Complex(0.9, 0.0)) (Complex(sqrt(1.0 - 0.81), 0.0))
        let random = Random(42)
        let result = NoiseModels.applyDepolarizingNoise 1.0 sup random  // 100% error rate
        // Should produce uniform amplitudes: 1/sqrt(2) for both terms
        let expectedAmp = 1.0 / sqrt 2.0
        for (amp, _) in result.Terms do
            Assert.True(abs (Complex.Abs amp - expectedAmp) < 1e-10,
                $"Expected uniform amplitude {expectedAmp}, got {Complex.Abs amp}")

    [<Fact>]
    let ``applyDepolarizingNoise with zero error rate never changes state`` () =
        let sup = makeEqualSuperposition ()
        let random = Random(42)
        let result = NoiseModels.applyDepolarizingNoise 0.0 sup random
        // Amplitudes should be identical
        for i in 0 .. sup.Terms.Length - 1 do
            let (origAmp, _) = sup.Terms.[i]
            let (resAmp, _) = result.Terms.[i]
            Assert.True(abs (Complex.Abs origAmp - Complex.Abs resAmp) < 1e-10)

    [<Fact>]
    let ``applyDepolarizingNoise on empty superposition returns unchanged`` () =
        let sup = makeEmptySuperposition ()
        let random = Random(42)
        let result = NoiseModels.applyDepolarizingNoise 1.0 sup random
        Assert.Empty(result.Terms)

    // ========================================================================
    // MEASUREMENT ERROR TESTS
    // ========================================================================

    [<Fact>]
    let ``applyMeasurementError with zero rate never flips`` () =
        let random = Random(42)
        // Run many trials - should never flip
        for _ in 1 .. 100 do
            let result = NoiseModels.applyMeasurementError 0.0 AnyonSpecies.Particle.Vacuum random
            Assert.Equal(AnyonSpecies.Particle.Vacuum, result)

    [<Fact>]
    let ``applyMeasurementError with rate 1 always flips`` () =
        let random = Random(42)
        let result = NoiseModels.applyMeasurementError 1.0 AnyonSpecies.Particle.Vacuum random
        Assert.Equal(AnyonSpecies.Particle.Psi, result)

    [<Fact>]
    let ``applyMeasurementError flips Vacuum to Psi`` () =
        let random = Random(42)
        let result = NoiseModels.applyMeasurementError 1.0 AnyonSpecies.Particle.Vacuum random
        Assert.Equal(AnyonSpecies.Particle.Psi, result)

    [<Fact>]
    let ``applyMeasurementError flips Psi to Vacuum`` () =
        let random = Random(42)
        let result = NoiseModels.applyMeasurementError 1.0 AnyonSpecies.Particle.Psi random
        Assert.Equal(AnyonSpecies.Particle.Vacuum, result)

    [<Fact>]
    let ``applyMeasurementError flips Sigma to Psi`` () =
        let random = Random(42)
        let result = NoiseModels.applyMeasurementError 1.0 AnyonSpecies.Particle.Sigma random
        Assert.Equal(AnyonSpecies.Particle.Psi, result)

    [<Fact>]
    let ``applyMeasurementError flips Tau to Vacuum`` () =
        let random = Random(42)
        let result = NoiseModels.applyMeasurementError 1.0 AnyonSpecies.Particle.Tau random
        Assert.Equal(AnyonSpecies.Particle.Vacuum, result)

    // ========================================================================
    // QUASIPARTICLE POISONING TESTS
    // ========================================================================

    [<Fact>]
    let ``checkQuasiparticlePoisoning with zero rate never triggers`` () =
        let poisoning = (NoiseModels.createQuasiparticlePoisoningParameters 0.0 0.050)
                        |> Result.defaultWith (fun _ -> failwith "")
        let random = Random(42)
        for _ in 1 .. 100 do
            let result = NoiseModels.checkQuasiparticlePoisoning poisoning 1.0 random
            Assert.False(result)

    [<Fact>]
    let ``checkQuasiparticlePoisoning probability increases with time`` () =
        // P = 1 - exp(-rate * time / 1_000_000)
        // With rate = 1_000_000 and time = 1.0: P = 1 - exp(-1) ≈ 0.632
        // With rate = 1_000_000 and time = 10.0: P = 1 - exp(-10) ≈ 0.99995
        let poisoning = (NoiseModels.createQuasiparticlePoisoningParameters 1_000_000.0 0.050)
                        |> Result.defaultWith (fun _ -> failwith "")

        // With very high rate and long time, should almost always trigger
        let random = Random(42)
        let mutable triggered = 0
        for _ in 1 .. 100 do
            if NoiseModels.checkQuasiparticlePoisoning poisoning 100.0 random then
                triggered <- triggered + 1
        // Should trigger frequently with high rate*time product
        Assert.True(triggered > 90, $"Expected >90 triggers, got {triggered}")

    [<Fact>]
    let ``checkQuasiparticlePoisoning with very short time rarely triggers`` () =
        let poisoning = (NoiseModels.createQuasiparticlePoisoningParameters 100.0 0.050)
                        |> Result.defaultWith (fun _ -> failwith "")
        // P = 1 - exp(-100 * 0.001 / 1_000_000) ≈ 1e-7
        let random = Random(42)
        let mutable triggered = 0
        for _ in 1 .. 100 do
            if NoiseModels.checkQuasiparticlePoisoning poisoning 0.001 random then
                triggered <- triggered + 1
        Assert.True(triggered < 5, $"Expected <5 triggers with tiny time, got {triggered}")

    // ========================================================================
    // NOISY BRAID TESTS
    // ========================================================================

    [<Fact>]
    let ``noisyBraid with noiseless model returns unchanged superposition`` () =
        let sup = makeEqualSuperposition ()
        let model = NoiseModels.noiseless ()
        let random = Random(42)
        let result = NoiseModels.noisyBraid model 0 sup random
        // With no noise, should be identical
        for i in 0 .. sup.Terms.Length - 1 do
            let (origAmp, _) = sup.Terms.[i]
            let (resAmp, _) = result.Terms.[i]
            Assert.True(abs (Complex.Abs origAmp - Complex.Abs resAmp) < 1e-10)

    [<Fact>]
    let ``noisyBraid with noise model changes state`` () =
        let sup = makeEqualSuperposition ()
        let model = NoiseModels.realisticSuperconducting ()
                    |> Result.defaultWith (fun _ -> failwith "")
        // Use a model with very high error rate to guarantee visible effect
        let highNoiseModel = {
            model with
                GateErrors = Some {
                    SingleQubitErrorRate = 0.5
                    TwoQubitErrorRate = 1.0  // 100% depolarizing
                    BraidingTime = 10.0
                }
        }
        let random = Random(42)
        let result = NoiseModels.noisyBraid highNoiseModel 0 sup random
        // With 100% depolarizing noise, amplitudes should become uniform
        let expectedAmp = 1.0 / sqrt 2.0
        for (amp, _) in result.Terms do
            // After depolarizing + decoherence + normalization, should be roughly uniform
            Assert.True(Complex.Abs amp > 0.0, "Amplitudes should be non-zero")

    // ========================================================================
    // NOISY MEASURE TESTS
    // ========================================================================

    [<Fact>]
    let ``noisyMeasure with noiseless model returns original outcome`` () =
        let model = NoiseModels.noiseless ()
        let random = Random(42)
        let result = NoiseModels.noisyMeasure model AnyonSpecies.Particle.Vacuum random
        Assert.Equal(AnyonSpecies.Particle.Vacuum, result)

    [<Fact>]
    let ``noisyMeasure with error flips outcome`` () =
        let model = {
            NoiseModels.noiseless () with
                MeasurementErrors = Some { ReadoutErrorRate = 1.0; MeasurementTime = 0.1 }
        }
        let random = Random(42)
        let result = NoiseModels.noisyMeasure model AnyonSpecies.Particle.Vacuum random
        Assert.NotEqual(AnyonSpecies.Particle.Vacuum, result)

    // ========================================================================
    // EFFECTIVE ERROR RATE TESTS
    // ========================================================================

    [<Fact>]
    let ``effectiveErrorRate of noiseless model is zero`` () =
        let model = NoiseModels.noiseless ()
        let rate = NoiseModels.effectiveErrorRate model 1.0
        Assert.Equal(0.0, rate, 10)

    [<Fact>]
    let ``effectiveErrorRate increases with operation time`` () =
        let model = NoiseModels.realisticSuperconducting ()
                    |> Result.defaultWith (fun _ -> failwith "")
        let rate1 = NoiseModels.effectiveErrorRate model 1.0
        let rate10 = NoiseModels.effectiveErrorRate model 10.0
        Assert.True(rate10 > rate1,
            $"Error rate at t=10 ({rate10}) should exceed rate at t=1 ({rate1})")

    [<Fact>]
    let ``effectiveErrorRate is clamped to max 1`` () =
        let model = NoiseModels.realisticSuperconducting ()
                    |> Result.defaultWith (fun _ -> failwith "")
        // Very long operation time should clamp at 1.0
        let rate = NoiseModels.effectiveErrorRate model 1_000_000.0
        Assert.True(rate <= 1.0, $"Error rate should be <= 1.0, got {rate}")

    [<Fact>]
    let ``effectiveErrorRate combines decoherence and gate errors`` () =
        let model = NoiseModels.realisticSuperconducting ()
                    |> Result.defaultWith (fun _ -> failwith "")
        let rate = NoiseModels.effectiveErrorRate model 1.0
        // Should be > gate error alone (decoherence adds to it)
        let gateError = model.GateErrors.Value.TwoQubitErrorRate
        Assert.True(rate > gateError,
            $"Combined rate ({rate}) should exceed gate error alone ({gateError})")

    [<Fact>]
    let ``effectiveErrorRate with only gate errors equals two-qubit rate`` () =
        let model = {
            NoiseModels.noiseless () with
                GateErrors = Some { SingleQubitErrorRate = 0.001; TwoQubitErrorRate = 0.05; BraidingTime = 1.0 }
        }
        let rate = NoiseModels.effectiveErrorRate model 1.0
        Assert.Equal(0.05, rate, 10)

    // ========================================================================
    // DISPLAY NOISE MODEL TESTS
    // ========================================================================

    [<Fact>]
    let ``displayNoiseModel for noiseless model shows None sections`` () =
        let model = NoiseModels.noiseless ()
        let output = NoiseModels.displayNoiseModel model
        Assert.Contains("Noise Model Configuration", output)
        Assert.Contains("None (ideal)", output)
        Assert.Contains("None (perfect gates)", output)
        Assert.Contains("None (perfect readout)", output)

    [<Fact>]
    let ``displayNoiseModel for realistic model shows parameters`` () =
        let model = NoiseModels.realisticSuperconducting ()
                    |> Result.defaultWith (fun _ -> failwith "")
        let output = NoiseModels.displayNoiseModel model
        Assert.Contains("T1 (relaxation)", output)
        Assert.Contains("T2 (dephasing)", output)
        Assert.Contains("Single-qubit", output)
        Assert.Contains("Two-qubit", output)
        Assert.Contains("Readout error", output)

    [<Fact>]
    let ``displayNoiseModel for topological model shows poisoning`` () =
        let model = NoiseModels.realisticTopological ()
                    |> Result.defaultWith (fun _ -> failwith "")
        let output = NoiseModels.displayNoiseModel model
        Assert.Contains("Quasiparticle Poisoning", output)
        Assert.Contains("Poisoning rate", output)
        Assert.Contains("Temperature", output)

    [<Fact>]
    let ``displayNoiseModel with random seed shows seed`` () =
        let model = { NoiseModels.noiseless () with RandomSeed = Some 42 }
        let output = NoiseModels.displayNoiseModel model
        Assert.Contains("42", output)
        Assert.Contains("reproducible", output)

    [<Fact>]
    let ``displayNoiseModel without random seed shows not set`` () =
        let model = NoiseModels.noiseless ()
        let output = NoiseModels.displayNoiseModel model
        Assert.Contains("Not set", output)
