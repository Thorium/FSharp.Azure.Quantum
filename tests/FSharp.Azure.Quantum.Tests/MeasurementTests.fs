namespace FSharp.Azure.Quantum.Tests

open System
open System.Numerics
open Xunit
open FSharp.Azure.Quantum.LocalSimulator

module MeasurementTests =

    [<Fact>]
    let ``Get basis state probability - should compute correctly`` () =
        // Test on |0⟩
        let state0 = StateVector.init 1
        Assert.Equal(1.0, Measurement.getBasisStateProbability 0 state0, 10)
        Assert.Equal(0.0, Measurement.getBasisStateProbability 1 state0, 10)

        // Test on uniform superposition |+⟩ = (|0⟩+|1⟩)/√2
        let statePlus = Gates.applyH 0 state0
        Assert.Equal(0.5, Measurement.getBasisStateProbability 0 statePlus, 10)
        Assert.Equal(0.5, Measurement.getBasisStateProbability 1 statePlus, 10)

    [<Fact>]
    let ``Get basis state probability - should reject invalid index`` () =
        let state = StateVector.init 2

        Assert.Throws<Exception>(fun () -> Measurement.getBasisStateProbability -1 state |> ignore)
        |> ignore

        Assert.Throws<Exception>(fun () -> Measurement.getBasisStateProbability 4 state |> ignore)
        |> ignore

    [<Fact>]
    let ``Get probability distribution - should sum to 1`` () =
        let state = QaoaSimulator.initializeUniformSuperposition 3
        let probabilities = Measurement.getProbabilityDistribution state

        let total = Array.sum probabilities
        Assert.Equal(1.0, total, 10)
        Assert.Equal(8, probabilities.Length) // 2^3 = 8 basis states

    [<Fact>]
    let ``Get qubit probabilities - should compute marginals correctly`` () =
        // Test on |0⟩
        let state0 = StateVector.init 1
        let (p0_state0, p1_state0) = Measurement.getQubitProbabilities 0 state0
        Assert.Equal(1.0, p0_state0, 10)
        Assert.Equal(0.0, p1_state0, 10)

        // Test on |1⟩
        let state1 = Gates.applyX 0 state0
        let (p0_state1, p1_state1) = Measurement.getQubitProbabilities 0 state1
        Assert.Equal(0.0, p0_state1, 10)
        Assert.Equal(1.0, p1_state1, 10)

        // Test on superposition
        let statePlus = Gates.applyH 0 state0
        let (p0_plus, p1_plus) = Measurement.getQubitProbabilities 0 statePlus
        Assert.Equal(0.5, p0_plus, 10)
        Assert.Equal(0.5, p1_plus, 10)

    [<Fact>]
    let ``Get qubit probabilities - should handle entangled states`` () =
        // Create Bell state (|00⟩+|11⟩)/√2
        let state00 = StateVector.init 2
        let stateBell = state00 |> Gates.applyH 0 |> Gates.applyCNOT 0 1

        // Both qubits should have 50/50 marginal probabilities
        let (p0_q0, p1_q0) = Measurement.getQubitProbabilities 0 stateBell
        Assert.Equal(0.5, p0_q0, 10)
        Assert.Equal(0.5, p1_q0, 10)

        let (p0_q1, p1_q1) = Measurement.getQubitProbabilities 1 stateBell
        Assert.Equal(0.5, p0_q1, 10)
        Assert.Equal(0.5, p1_q1, 10)

    [<Fact>]
    let ``Measure computational basis - should return valid outcome`` () =
        let rng = Random(42) // Fixed seed for reproducibility
        let state = QaoaSimulator.initializeUniformSuperposition 2

        let outcome = Measurement.measureComputationalBasis rng state

        // Outcome should be in valid range
        Assert.True(outcome >= 0 && outcome < 4)

    [<Fact>]
    let ``Measure computational basis - should respect probabilities`` () =
        let rng = Random(42)
        let state0 = StateVector.init 1 // |0⟩

        // Measure many times
        let outcomes =
            [| 1..100 |]
            |> Array.map (fun _ -> Measurement.measureComputationalBasis rng state0)

        // All outcomes should be 0 (deterministic)
        Assert.True(Array.forall ((=) 0) outcomes)

    [<Fact>]
    let ``Measure single qubit - should return 0 or 1`` () =
        let rng = Random(42)
        let state = Gates.applyH 0 (StateVector.init 1)

        let outcome = Measurement.measureSingleQubit rng 0 state

        Assert.True(outcome = 0 || outcome = 1)

    [<Fact>]
    let ``Collapse after measurement - should zero inconsistent amplitudes`` () =
        let statePlus = Gates.applyH 0 (StateVector.init 1)

        // Collapse to |0⟩
        let collapsed0 = Measurement.collapseAfterMeasurement 0 0 statePlus
        Assert.Equal(1.0, Measurement.getBasisStateProbability 0 collapsed0, 10)
        Assert.Equal(0.0, Measurement.getBasisStateProbability 1 collapsed0, 10)

        // Collapse to |1⟩
        let collapsed1 = Measurement.collapseAfterMeasurement 0 1 statePlus
        Assert.Equal(0.0, Measurement.getBasisStateProbability 0 collapsed1, 10)
        Assert.Equal(1.0, Measurement.getBasisStateProbability 1 collapsed1, 10)

    [<Fact>]
    let ``Collapse after measurement - should preserve norm`` () =
        let state = QaoaSimulator.initializeUniformSuperposition 2

        let collapsed = Measurement.collapseAfterMeasurement 0 1 state

        Assert.Equal(1.0, StateVector.norm collapsed, 10)

    [<Fact>]
    let ``Collapse after measurement - should reject invalid inputs`` () =
        let state = StateVector.init 2

        // Invalid outcome
        Assert.Throws<Exception>(fun () -> Measurement.collapseAfterMeasurement 0 2 state |> ignore)
        |> ignore

        // Invalid qubit index
        Assert.Throws<Exception>(fun () -> Measurement.collapseAfterMeasurement 3 0 state |> ignore)
        |> ignore

    [<Fact>]
    let ``Sample measurements - should return correct number of samples`` () =
        let rng = Random(42)
        let state = QaoaSimulator.initializeUniformSuperposition 2
        let numSamples = 50

        let samples = Measurement.sampleMeasurements rng numSamples state

        Assert.Equal(numSamples, samples.Length)
        Assert.True(Array.forall (fun s -> s >= 0 && s < 4) samples)

    [<Fact>]
    let ``Sample measurements - should reject invalid sample count`` () =
        let rng = Random(42)
        let state = StateVector.init 1

        Assert.Throws<Exception>(fun () -> Measurement.sampleMeasurements rng 0 state |> ignore)
        |> ignore

        Assert.Throws<Exception>(fun () -> Measurement.sampleMeasurements rng -1 state |> ignore)
        |> ignore

    [<Fact>]
    let ``Sample and count - should produce frequency distribution`` () =
        let rng = Random(42)
        let state0 = StateVector.init 2 // |00⟩
        let numSamples = 100

        let counts = Measurement.sampleAndCount rng numSamples state0

        // All samples should be outcome 0
        Assert.Equal(1, counts.Count)
        Assert.True(counts.ContainsKey 0)
        Assert.Equal(numSamples, counts[0])

    [<Fact>]
    let ``Sample and count - should approximate probabilities`` () =
        let rng = Random(42)
        let statePlus = Gates.applyH 0 (StateVector.init 1) // 50/50 superposition
        let numSamples = 10000 // Large sample for statistics

        let counts = Measurement.sampleAndCount rng numSamples statePlus

        // Should get approximately 50/50 distribution
        let count0 =
            match counts.TryFind 0 with
            | Some value -> float value
            | None -> 0.0

        let count1 =
            match counts.TryFind 1 with
            | Some value -> float value
            | None -> 0.0

        let ratio0 = count0 / float numSamples
        let ratio1 = count1 / float numSamples

        Assert.True(ratio0 > 0.45 && ratio0 < 0.55, $"Expected ~0.5, got {ratio0}")
        Assert.True(ratio1 > 0.45 && ratio1 < 0.55, $"Expected ~0.5, got {ratio1}")

    [<Fact>]
    let ``Get most likely outcome - should return highest probability state`` () =
        // State heavily biased toward |0⟩
        let state0 = StateVector.init 1
        let mostLikely0 = Measurement.getMostLikelyOutcome state0
        Assert.Equal(0, mostLikely0)

        // State heavily biased toward |1⟩
        let state1 = Gates.applyX 0 state0
        let mostLikely1 = Measurement.getMostLikelyOutcome state1
        Assert.Equal(1, mostLikely1)

    [<Fact>]
    let ``Get top outcomes - should return sorted by probability`` () =
        let state = QaoaSimulator.initializeUniformSuperposition 2
        let topOutcomes = Measurement.getTopOutcomes 3 state

        Assert.Equal(3, topOutcomes.Length)

        // Should be sorted descending by probability
        for i in 0 .. topOutcomes.Length - 2 do
            let (_, prob1) = topOutcomes[i]
            let (_, prob2) = topOutcomes[i + 1]
            Assert.True(prob1 >= prob2)

        // For uniform superposition, all probabilities should be equal (0.25)
        for (_, prob) in topOutcomes do
            Assert.Equal(0.25, prob, 10)

    [<Fact>]
    let ``Get top outcomes - should handle n larger than dimension`` () =
        let state = StateVector.init 2 // 4-dimensional
        let topOutcomes = Measurement.getTopOutcomes 10 state

        // Should return only 4 outcomes
        Assert.Equal(4, topOutcomes.Length)

    [<Fact>]
    let ``Compute expected value - should calculate correctly`` () =
        // State |0⟩, function f(x) = x
        let state0 = StateVector.init 2
        let expectedValue0 = Measurement.computeExpectedValue (fun x -> float x) state0
        Assert.Equal(0.0, expectedValue0, 10) // |00⟩ → index 0 → value 0

        // State |11⟩, function f(x) = x
        let state11 = state0 |> Gates.applyX 0 |> Gates.applyX 1
        let expectedValue11 = Measurement.computeExpectedValue (fun x -> float x) state11
        Assert.Equal(3.0, expectedValue11, 10) // |11⟩ → index 3 → value 3

        // Uniform superposition, function f(x) = x
        let stateUniform = QaoaSimulator.initializeUniformSuperposition 2

        let expectedValueUniform =
            Measurement.computeExpectedValue (fun x -> float x) stateUniform
        // E[x] = 0.25*0 + 0.25*1 + 0.25*2 + 0.25*3 = 1.5
        Assert.Equal(1.5, expectedValueUniform, 10)

    [<Fact>]
    let ``Compute standard deviation - should calculate correctly`` () =
        // Deterministic state |0⟩, function f(x) = x
        let state0 = StateVector.init 1
        let std0 = Measurement.computeStandardDeviation (fun x -> float x) state0
        Assert.Equal(0.0, std0, 10) // No uncertainty in deterministic state

        // Uniform superposition, function f(x) = x
        let statePlus = Gates.applyH 0 state0
        let stdPlus = Measurement.computeStandardDeviation (fun x -> float x) statePlus
        // E[x] = 0.5*0 + 0.5*1 = 0.5
        // E[x²] = 0.5*0 + 0.5*1 = 0.5
        // Var = 0.5 - 0.25 = 0.25
        // Std = 0.5
        Assert.Equal(0.5, stdPlus, 10)

    [<Fact>]
    let ``Full measurement workflow - simulate and analyze`` () =
        // Create interesting quantum state
        let state = StateVector.init 2 |> Gates.applyH 0 |> Gates.applyCNOT 0 1 // Bell state

        // Get probability distribution
        let probabilities = Measurement.getProbabilityDistribution state
        Assert.Equal(4, probabilities.Length)
        Assert.Equal(0.5, probabilities[0], 10) // |00⟩
        Assert.Equal(0.0, probabilities[1], 10) // |01⟩
        Assert.Equal(0.0, probabilities[2], 10) // |10⟩
        Assert.Equal(0.5, probabilities[3], 10) // |11⟩

        // Sample measurements
        let rng = Random(42)
        let samples = Measurement.sampleMeasurements rng 1000 state

        // Count outcomes
        let counts = Measurement.sampleAndCount rng 1000 state

        // Should only see outcomes 0 and 3 (|00⟩ and |11⟩)
        Assert.True(counts.ContainsKey 0)
        Assert.True(counts.ContainsKey 3)
        Assert.False(counts.ContainsKey 1)
        Assert.False(counts.ContainsKey 2)

    // ========================================================================
    // BATCH SAMPLING
    // ========================================================================
    //
    // `sampleComputationalBasis` replaced a per-shot call to
    // `measureComputationalBasis`, which rebuilt the whole 2^n probability
    // distribution every single shot. The saving is large (O(shots·2^n) to
    // O(2^n + shots·log 2^n)), so the thing that must be pinned is that the
    // OUTCOME DISTRIBUTION did not move with it.

    /// Index of a bit array, LSB first — the convention measureAll uses.
    let private basisIndexOf (bits: int[]) =
        bits |> Array.mapi (fun i b -> b <<< i) |> Array.sum

    [<Fact>]
    let ``batch sampling only ever returns basis states the state supports`` () =
        // |01> + |10> over 2 qubits: outcomes 0 and 3 must never appear.
        let half = 1.0 / sqrt 2.0

        let sv =
            StateVector.create [| Complex.Zero; Complex(half, 0.0); Complex(half, 0.0); Complex.Zero |]

        let samples = Measurement.sampleComputationalBasis (System.Random(7)) sv 2000

        Assert.Equal(2000, samples.Length)

        for bits in samples do
            Assert.Equal(2, bits.Length)
            let index = basisIndexOf bits
            Assert.True(index = 1 || index = 2, $"impossible outcome {index} for this state")

    [<Fact>]
    let ``batch sampling reproduces a known non-uniform distribution`` () =
        // p = [0.1; 0.2; 0.3; 0.4] — deliberately lopsided, so a sampler that
        // silently used a uniform draw or an off-by-one cumulative bound would show up.
        let probabilities = [| 0.1; 0.2; 0.3; 0.4 |]

        let sv =
            probabilities |> Array.map (fun p -> Complex(sqrt p, 0.0)) |> StateVector.create

        let shots = 200000
        let samples = Measurement.sampleComputationalBasis (System.Random(11)) sv shots

        let counts = Array.zeroCreate 4

        for bits in samples do
            counts.[basisIndexOf bits] <- counts.[basisIndexOf bits] + 1

        for i in 0..3 do
            let observed = float counts.[i] / float shots

            Assert.True(
                abs (observed - probabilities.[i]) < 0.01,
                $"basis state {i}: expected p={probabilities.[i]}, sampled {observed}"
            )

    [<Fact>]
    let ``batch sampling agrees with per-shot measurement in distribution`` () =
        // The replaced implementation, sampled independently. Both estimate the same
        // distribution, so their histograms must agree to within sampling error.
        let probabilities = [| 0.05; 0.15; 0.5; 0.3 |]

        let sv =
            probabilities |> Array.map (fun p -> Complex(sqrt p, 0.0)) |> StateVector.create

        let shots = 200000

        let batchCounts = Array.zeroCreate 4

        for bits in Measurement.sampleComputationalBasis (System.Random(23)) sv shots do
            batchCounts.[basisIndexOf bits] <- batchCounts.[basisIndexOf bits] + 1

        let perShotRng = System.Random(29)
        let perShotCounts = Array.zeroCreate 4

        for _ in 1..shots do
            let index = Measurement.measureComputationalBasis perShotRng sv
            perShotCounts.[index] <- perShotCounts.[index] + 1

        for i in 0..3 do
            let batch = float batchCounts.[i] / float shots
            let perShot = float perShotCounts.[i] / float shots

            Assert.True(abs (batch - perShot) < 0.01, $"basis state {i}: batch {batch} vs per-shot {perShot}")

    [<Fact>]
    let ``batch sampling handles the degenerate and single-outcome cases`` () =
        // A basis state: every shot must return it.
        let sv = StateVector.init 3
        let samples = Measurement.sampleComputationalBasis (System.Random(3)) sv 100

        for bits in samples do
            Assert.Equal(0, basisIndexOf bits)

        // Zero shots is an empty batch, not a failure.
        Assert.Empty(Measurement.sampleComputationalBasis (System.Random(3)) sv 0)

        // A one-qubit state still produces one bit per shot.
        let single = StateVector.init 1
        let oneQubit = Measurement.sampleComputationalBasis (System.Random(5)) single 10

        for bits in oneQubit do
            Assert.Equal(1, bits.Length)
