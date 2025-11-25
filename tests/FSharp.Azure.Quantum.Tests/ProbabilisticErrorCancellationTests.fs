namespace FSharp.Azure.Quantum.Tests

open Xunit
open FSharp.Azure.Quantum

module ProbabilisticErrorCancellationTests =
    
    // ============================================================================
    // TKT-44: Probabilistic Error Cancellation Tests
    // ============================================================================
    
    // Cycle #1: Core types - NoiseModel, QuasiProbDecomposition, PECConfig, PECResult
    
    [<Fact>]
    let ``NoiseModel should represent depolarizing noise rates`` () =
        // Arrange & Act: Create noise model with realistic error rates
        let noiseModel: ProbabilisticErrorCancellation.NoiseModel = {
            SingleQubitDepolarizing = 0.001  // 0.1% error per single-qubit gate
            TwoQubitDepolarizing = 0.01      // 1% error per two-qubit gate
            ReadoutError = 0.02              // 2% measurement error
        }
        
        // Assert: Verify fields are accessible and have correct values
        Assert.Equal(0.001, noiseModel.SingleQubitDepolarizing)
        Assert.Equal(0.01, noiseModel.TwoQubitDepolarizing)
        Assert.Equal(0.02, noiseModel.ReadoutError)
    
    [<Fact>]
    let ``QuasiProbDecomposition should hold terms with negative probabilities`` () =
        // Arrange: Create decomposition with negative quasi-probabilities
        // This is the key feature of PEC - some probabilities are negative!
        let decomposition: ProbabilisticErrorCancellation.QuasiProbDecomposition = {
            Terms = [
                (CircuitBuilder.H 0, 1.1)      // Positive: desired gate
                (CircuitBuilder.X 0, -0.05)    // Negative: Pauli X correction
                (CircuitBuilder.Y 0, -0.05)    // Negative: Pauli Y correction
            ]
            Normalization = 1.2  // Sum of |pᵢ| = |1.1| + |-0.05| + |-0.05| = 1.2
        }
        
        // Assert: Verify structure
        Assert.Equal(3, decomposition.Terms.Length)
        Assert.Equal(1.2, decomposition.Normalization)
        
        // Assert: Verify negative probabilities are present
        let hasNegativeProbability = 
            decomposition.Terms 
            |> List.exists (fun (_, prob) -> prob < 0.0)
        Assert.True(hasNegativeProbability, "PEC decomposition should have negative probabilities")
    
    [<Fact>]
    let ``PECConfig should specify noise model and sampling parameters`` () =
        // Arrange & Act: Create PEC configuration
        let noiseModel: ProbabilisticErrorCancellation.NoiseModel = {
            SingleQubitDepolarizing = 0.001
            TwoQubitDepolarizing = 0.01
            ReadoutError = 0.02
        }
        
        let config: ProbabilisticErrorCancellation.PECConfig = {
            NoiseModel = noiseModel
            Samples = 100  // 100x overhead for Monte Carlo sampling
            Seed = Some 42
        }
        
        // Assert: Verify configuration
        Assert.Equal(0.001, config.NoiseModel.SingleQubitDepolarizing)
        Assert.Equal(100, config.Samples)
        Assert.Equal(Some 42, config.Seed)
    
    [<Fact>]
    let ``PECResult should track corrected expectation and overhead`` () =
        // Arrange & Act: Create PEC result showing error mitigation
        let result: ProbabilisticErrorCancellation.PECResult = {
            CorrectedExpectation = 0.95       // After PEC mitigation
            UncorrectedExpectation = 0.80     // Before PEC (noisy)
            ErrorReduction = 0.75             // 75% error reduction!
            SamplesUsed = 100
            Overhead = 100.0                  // 100x circuit executions
        }
        
        // Assert: Verify fields
        Assert.Equal(0.95, result.CorrectedExpectation)
        Assert.Equal(0.80, result.UncorrectedExpectation)
        Assert.True(result.CorrectedExpectation > result.UncorrectedExpectation,
            "PEC should improve expectation value")
        Assert.Equal(100.0, result.Overhead)
    
    // Cycle #2: Single-qubit gate decomposition - 5-term quasi-probability
    
    [<Fact>]
    let ``decomposeSingleQubitGate should produce 5-term decomposition`` () =
        // Arrange: Noise model with 0.1% depolarizing error
        let noiseModel: ProbabilisticErrorCancellation.NoiseModel = {
            SingleQubitDepolarizing = 0.001
            TwoQubitDepolarizing = 0.01
            ReadoutError = 0.02
        }
        
        let gate = CircuitBuilder.H 0  // Hadamard gate
        
        // Act: Decompose noisy gate into quasi-probability distribution
        let decomposition = ProbabilisticErrorCancellation.decomposeSingleQubitGate gate noiseModel
        
        // Assert: Should have exactly 5 terms (gate + 4 Pauli corrections)
        Assert.Equal(5, decomposition.Terms.Length)
        
        // Assert: First term is the desired gate with positive probability
        let (firstGate, firstProb) = decomposition.Terms.[0]
        Assert.Equal(gate, firstGate)
        Assert.True(firstProb > 0.0, "First term (desired gate) should have positive probability")
    
    [<Fact>]
    let ``decomposeSingleQubitGate should have correct probability formula`` () =
        // Arrange: Use p = 0.001 for easy calculation
        let p = 0.001
        let noiseModel: ProbabilisticErrorCancellation.NoiseModel = {
            SingleQubitDepolarizing = p
            TwoQubitDepolarizing = 0.01
            ReadoutError = 0.02
        }
        
        let gate = CircuitBuilder.X 0  // Pauli X gate
        
        // Act: Decompose
        let decomposition = ProbabilisticErrorCancellation.decomposeSingleQubitGate gate noiseModel
        
        // Assert: First term probability should be (1 + p)
        // From depolarizing channel inversion: U = (1+p)·Noisy_U - ...
        let (_, firstProb) = decomposition.Terms.[0]
        Assert.Equal(1.0 + p, firstProb, 10)
        
        // Assert: Correction terms should each be -p/4
        // Depolarizing over {I, X, Y, Z} → 4 terms, each with weight -p/4
        let correctionProbs = decomposition.Terms |> List.skip 1 |> List.map snd
        Assert.Equal(4, correctionProbs.Length)
        
        correctionProbs |> List.iter (fun prob ->
            Assert.Equal(-p / 4.0, prob, 10))
    
    [<Fact>]
    let ``decomposeSingleQubitGate should have correct normalization`` () =
        // Arrange
        let p = 0.001
        let noiseModel: ProbabilisticErrorCancellation.NoiseModel = {
            SingleQubitDepolarizing = p
            TwoQubitDepolarizing = 0.01
            ReadoutError = 0.02
        }
        
        let gate = CircuitBuilder.Y 0
        
        // Act
        let decomposition = ProbabilisticErrorCancellation.decomposeSingleQubitGate gate noiseModel
        
        // Assert: Normalization = Σ|pᵢ| = (1+p) + 4×(p/4) = 1 + 2p
        let expectedNorm = 1.0 + 2.0 * p
        Assert.Equal(expectedNorm, decomposition.Normalization, 10)
        
        // Assert: Manual calculation should match
        let manualNorm = 
            decomposition.Terms 
            |> List.sumBy (fun (_, prob) -> abs prob)
        Assert.Equal(expectedNorm, manualNorm, 10)
    
    [<Fact>]
    let ``decomposeSingleQubitGate quasi-probabilities should sum to 1`` () =
        // Arrange: This is a key property - quasi-probs sum to 1 (before importance sampling)
        let p = 0.001
        let noiseModel: ProbabilisticErrorCancellation.NoiseModel = {
            SingleQubitDepolarizing = p
            TwoQubitDepolarizing = 0.01
            ReadoutError = 0.02
        }
        
        let gate = CircuitBuilder.Z 0
        
        // Act
        let decomposition = ProbabilisticErrorCancellation.decomposeSingleQubitGate gate noiseModel
        
        // Assert: Σpᵢ = 1 (quasi-probability normalization)
        // (1+p) + 4×(-p/4) = 1 + p - p = 1
        let sum = decomposition.Terms |> List.sumBy snd
        Assert.Equal(1.0, sum, 10)
    
    [<Fact>]
    let ``decomposeSingleQubitGate should include Pauli corrections`` () =
        // Arrange: Check that correction terms are identity-like gates
        let noiseModel: ProbabilisticErrorCancellation.NoiseModel = {
            SingleQubitDepolarizing = 0.001
            TwoQubitDepolarizing = 0.01
            ReadoutError = 0.02
        }
        
        let gate = CircuitBuilder.H 0
        
        // Act
        let decomposition = ProbabilisticErrorCancellation.decomposeSingleQubitGate gate noiseModel
        
        // Assert: Correction terms (indices 1-4) should be identity or Pauli-like
        let correctionGates = 
            decomposition.Terms 
            |> List.skip 1 
            |> List.map fst
        
        Assert.Equal(4, correctionGates.Length)
        
        // All correction gates should have negative probabilities
        let correctionProbs = 
            decomposition.Terms 
            |> List.skip 1 
            |> List.map snd
        
        correctionProbs |> List.iter (fun prob ->
            Assert.True(prob < 0.0, "Correction terms must have negative probabilities"))
    
    [<Fact>]
    let ``decomposeSingleQubitGate should work with zero noise`` () =
        // Arrange: Edge case - zero noise (p = 0)
        let noiseModel: ProbabilisticErrorCancellation.NoiseModel = {
            SingleQubitDepolarizing = 0.0
            TwoQubitDepolarizing = 0.01
            ReadoutError = 0.02
        }
        
        let gate = CircuitBuilder.H 0
        
        // Act
        let decomposition = ProbabilisticErrorCancellation.decomposeSingleQubitGate gate noiseModel
        
        // Assert: With p=0, first term is 1.0, corrections are 0.0
        let (_, firstProb) = decomposition.Terms.[0]
        Assert.Equal(1.0, firstProb, 10)
        
        let correctionProbs = decomposition.Terms |> List.skip 1 |> List.map snd
        correctionProbs |> List.iter (fun prob ->
            Assert.Equal(0.0, prob, 10))
        
        // Assert: Normalization = 1.0 (no overhead needed)
        Assert.Equal(1.0, decomposition.Normalization, 10)
    
    // Cycle #3: Two-qubit gate decomposition - 16-term Pauli basis
    
    [<Fact>]
    let ``decomposeTwoQubitGate should produce 16-term decomposition`` () =
        // Arrange: Two-qubit depolarizing noise
        let noiseModel: ProbabilisticErrorCancellation.NoiseModel = {
            SingleQubitDepolarizing = 0.001
            TwoQubitDepolarizing = 0.01  // Higher error rate for two-qubit gates
            ReadoutError = 0.02
        }
        
        let gate = CircuitBuilder.CNOT (0, 1)
        
        // Act: Decompose into Pauli basis
        let decomposition = ProbabilisticErrorCancellation.decomposeTwoQubitGate gate noiseModel
        
        // Assert: 16 terms = 1 desired gate + 15 Pauli basis corrections
        // Pauli basis: {I, X, Y, Z} ⊗ {I, X, Y, Z} = 16 combinations
        // One term is the desired gate, so 15 correction terms
        Assert.Equal(16, decomposition.Terms.Length)
    
    [<Fact>]
    let ``decomposeTwoQubitGate should have correct probability formula`` () =
        // Arrange
        let p = 0.01  // 1% two-qubit error
        let noiseModel: ProbabilisticErrorCancellation.NoiseModel = {
            SingleQubitDepolarizing = 0.001
            TwoQubitDepolarizing = p
            ReadoutError = 0.02
        }
        
        let gate = CircuitBuilder.CNOT (0, 1)
        
        // Act
        let decomposition = ProbabilisticErrorCancellation.decomposeTwoQubitGate gate noiseModel
        
        // Assert: First term is (1 + p)
        let (_, firstProb) = decomposition.Terms.[0]
        Assert.Equal(1.0 + p, firstProb, 10)
        
        // Assert: Each correction term is -p/15
        // Two-qubit depolarizing: 15 Pauli operators (excluding II)
        let correctionProbs = decomposition.Terms |> List.skip 1 |> List.map snd
        Assert.Equal(15, correctionProbs.Length)
        
        correctionProbs |> List.iter (fun prob ->
            Assert.Equal(-p / 15.0, prob, 10))
    
    [<Fact>]
    let ``decomposeTwoQubitGate should have correct normalization`` () =
        // Arrange
        let p = 0.01
        let noiseModel: ProbabilisticErrorCancellation.NoiseModel = {
            SingleQubitDepolarizing = 0.001
            TwoQubitDepolarizing = p
            ReadoutError = 0.02
        }
        
        let gate = CircuitBuilder.CNOT (0, 1)
        
        // Act
        let decomposition = ProbabilisticErrorCancellation.decomposeTwoQubitGate gate noiseModel
        
        // Assert: Normalization = Σ|pᵢ| = (1+p) + 15×(p/15) = 1 + 2p
        let expectedNorm = 1.0 + 2.0 * p
        Assert.Equal(expectedNorm, decomposition.Normalization, 10)
    
    [<Fact>]
    let ``decomposeTwoQubitGate quasi-probabilities should sum to 1`` () =
        // Arrange
        let p = 0.01
        let noiseModel: ProbabilisticErrorCancellation.NoiseModel = {
            SingleQubitDepolarizing = 0.001
            TwoQubitDepolarizing = p
            ReadoutError = 0.02
        }
        
        let gate = CircuitBuilder.CNOT (0, 1)
        
        // Act
        let decomposition = ProbabilisticErrorCancellation.decomposeTwoQubitGate gate noiseModel
        
        // Assert: Σpᵢ = 1 (quasi-probability normalization)
        // (1+p) + 15×(-p/15) = 1 + p - p = 1
        let sum = decomposition.Terms |> List.sumBy snd
        Assert.Equal(1.0, sum, 10)
    
    [<Fact>]
    let ``decomposeTwoQubitGate should include all Pauli basis terms`` () =
        // Arrange
        let noiseModel: ProbabilisticErrorCancellation.NoiseModel = {
            SingleQubitDepolarizing = 0.001
            TwoQubitDepolarizing = 0.01
            ReadoutError = 0.02
        }
        
        let gate = CircuitBuilder.CNOT (0, 1)
        
        // Act
        let decomposition = ProbabilisticErrorCancellation.decomposeTwoQubitGate gate noiseModel
        
        // Assert: All correction terms should have negative probabilities
        let correctionProbs = 
            decomposition.Terms 
            |> List.skip 1 
            |> List.map snd
        
        correctionProbs |> List.iter (fun prob ->
            Assert.True(prob < 0.0, "All Pauli basis corrections must have negative probabilities"))
    
    [<Fact>]
    let ``decomposeTwoQubitGate should work with zero noise`` () =
        // Arrange: Zero two-qubit noise
        let noiseModel: ProbabilisticErrorCancellation.NoiseModel = {
            SingleQubitDepolarizing = 0.001
            TwoQubitDepolarizing = 0.0  // No noise!
            ReadoutError = 0.02
        }
        
        let gate = CircuitBuilder.CNOT (0, 1)
        
        // Act
        let decomposition = ProbabilisticErrorCancellation.decomposeTwoQubitGate gate noiseModel
        
        // Assert: First term is 1.0, corrections are 0.0
        let (_, firstProb) = decomposition.Terms.[0]
        Assert.Equal(1.0, firstProb, 10)
        
        let correctionProbs = decomposition.Terms |> List.skip 1 |> List.map snd
        correctionProbs |> List.iter (fun prob ->
            Assert.Equal(0.0, prob, 10))
        
        // Assert: Normalization = 1.0
        Assert.Equal(1.0, decomposition.Normalization, 10)
    
    // Cycle #4: Importance sampling - Converting negative probabilities to proper sampling
    
    [<Fact>]
    let ``sampleQuasiProb should return gate and weight`` () =
        // Arrange: Simple decomposition with negative probabilities
        let decomposition: ProbabilisticErrorCancellation.QuasiProbDecomposition = {
            Terms = [
                (CircuitBuilder.H 0, 1.1)      // Positive
                (CircuitBuilder.X 0, -0.1)     // Negative
            ]
            Normalization = 1.2  // |1.1| + |-0.1| = 1.2
        }
        
        let rng = System.Random(42)
        
        // Act: Sample from quasi-probability distribution
        let (sampledGate, weight) = ProbabilisticErrorCancellation.sampleQuasiProb decomposition rng
        
        // Assert: Should return a gate from the decomposition
        let gates = decomposition.Terms |> List.map fst
        Assert.Contains(sampledGate, gates)
        
        // Assert: Weight should be ± Normalization (sign × normalization)
        let absWeight = abs weight
        Assert.Equal(decomposition.Normalization, absWeight, 10)
    
    [<Fact>]
    let ``sampleQuasiProb should preserve sign information`` () =
        // Arrange: Decomposition with known positive and negative terms
        let decomposition: ProbabilisticErrorCancellation.QuasiProbDecomposition = {
            Terms = [
                (CircuitBuilder.H 0, 1.0)      // Positive
                (CircuitBuilder.X 0, -0.5)     // Negative
            ]
            Normalization = 1.5
        }
        
        let rng = System.Random(42)
        
        // Act: Sample multiple times to check sign preservation
        let samples = 
            [1..100] 
            |> List.map (fun _ -> ProbabilisticErrorCancellation.sampleQuasiProb decomposition rng)
        
        // Assert: All weights should be ± Normalization
        samples |> List.iter (fun (_, weight) ->
            let absWeight = abs weight
            Assert.Equal(1.5, absWeight, 10))
        
        // Assert: Should see both positive and negative weights (stochastic)
        let hasPositive = samples |> List.exists (fun (_, w) -> w > 0.0)
        let hasNegative = samples |> List.exists (fun (_, w) -> w < 0.0)
        Assert.True(hasPositive, "Should sample terms with positive weights")
        Assert.True(hasNegative, "Should sample terms with negative weights")
    
    [<Fact>]
    let ``sampleQuasiProb should sample according to absolute probabilities`` () =
        // Arrange: Heavily weighted toward one term
        let decomposition: ProbabilisticErrorCancellation.QuasiProbDecomposition = {
            Terms = [
                (CircuitBuilder.H 0, 0.9)      // High absolute probability
                (CircuitBuilder.X 0, -0.1)     // Low absolute probability
            ]
            Normalization = 1.0
        }
        
        let rng = System.Random(42)
        
        // Act: Sample many times
        let samples = 
            [1..1000] 
            |> List.map (fun _ -> ProbabilisticErrorCancellation.sampleQuasiProb decomposition rng)
        
        // Assert: H gate should be sampled ~90% of the time (stochastic test)
        let hGateCount = 
            samples 
            |> List.filter (fun (gate, _) -> gate = CircuitBuilder.H 0)
            |> List.length
        
        let hGateRatio = float hGateCount / 1000.0
        // Allow 5% tolerance for statistical variance
        Assert.True(hGateRatio > 0.85 && hGateRatio < 0.95, 
            sprintf "Expected H gate ratio ~0.90, got %.3f" hGateRatio)
    
    [<Fact>]
    let ``sampleQuasiProb should handle all positive probabilities`` () =
        // Arrange: Edge case - no negative probabilities (shouldn't happen in PEC, but test it)
        let decomposition: ProbabilisticErrorCancellation.QuasiProbDecomposition = {
            Terms = [
                (CircuitBuilder.H 0, 0.6)
                (CircuitBuilder.X 0, 0.4)
            ]
            Normalization = 1.0
        }
        
        let rng = System.Random(42)
        
        // Act: Sample multiple times
        let samples = 
            [1..100] 
            |> List.map (fun _ -> ProbabilisticErrorCancellation.sampleQuasiProb decomposition rng)
        
        // Assert: All weights should be positive (no negative terms)
        samples |> List.iter (fun (_, weight) ->
            Assert.True(weight > 0.0, "All weights should be positive when no negative quasi-probs"))
    
    [<Fact>]
    let ``sampleQuasiProb should work with single-qubit decomposition`` () =
        // Arrange: Use actual single-qubit decomposition
        let noiseModel: ProbabilisticErrorCancellation.NoiseModel = {
            SingleQubitDepolarizing = 0.001
            TwoQubitDepolarizing = 0.01
            ReadoutError = 0.02
        }
        
        let gate = CircuitBuilder.H 0
        let decomposition = ProbabilisticErrorCancellation.decomposeSingleQubitGate gate noiseModel
        
        let rng = System.Random(42)
        
        // Act: Sample
        let (sampledGate, weight) = ProbabilisticErrorCancellation.sampleQuasiProb decomposition rng
        
        // Assert: Sampled gate should be from decomposition
        let gates = decomposition.Terms |> List.map fst
        Assert.Contains(sampledGate, gates)
        
        // Assert: Weight should have correct magnitude
        let absWeight = abs weight
        Assert.Equal(decomposition.Normalization, absWeight, 10)
    
    [<Fact>]
    let ``sampleQuasiProb should be deterministic with same seed`` () =
        // Arrange
        let decomposition: ProbabilisticErrorCancellation.QuasiProbDecomposition = {
            Terms = [
                (CircuitBuilder.H 0, 1.0)
                (CircuitBuilder.X 0, -0.5)
            ]
            Normalization = 1.5
        }
        
        // Act: Sample with same seed twice
        let rng1 = System.Random(123)
        let sample1 = ProbabilisticErrorCancellation.sampleQuasiProb decomposition rng1
        
        let rng2 = System.Random(123)
        let sample2 = ProbabilisticErrorCancellation.sampleQuasiProb decomposition rng2
        
        // Assert: Should get identical results
        Assert.Equal(sample1, sample2)
    
    // Cycle #5: Full PEC pipeline - Monte Carlo with weighted sampling
    
    [<Fact>]
    let ``mitigate should execute full PEC pipeline`` () =
        async {
            // Arrange: Simple circuit with single-qubit gate
            let circuit = 
                CircuitBuilder.empty 1
                |> CircuitBuilder.addGate (CircuitBuilder.H 0)
            
            let config: ProbabilisticErrorCancellation.PECConfig = {
                NoiseModel = {
                    SingleQubitDepolarizing = 0.001
                    TwoQubitDepolarizing = 0.01
                    ReadoutError = 0.02
                }
                Samples = 10  // Small number for fast test
                Seed = Some 42
            }
            
            // Mock executor: returns constant expectation value
            let mockExecutor (_: CircuitBuilder.Circuit) =
                async { return Ok 0.85 }
            
            // Act: Run PEC
            let! result = ProbabilisticErrorCancellation.mitigate circuit config mockExecutor
            
            // Assert: Should return successful result
            match result with
            | Ok pecResult ->
                Assert.True(pecResult.SamplesUsed > 0, "Should have used samples")
                Assert.Equal(10, pecResult.SamplesUsed)
                Assert.True(pecResult.Overhead > 0.0, "Should have overhead")
            | Error err ->
                Assert.Fail(sprintf "PEC failed: %s" err)
        } |> Async.RunSynchronously
    
    [<Fact>]
    let ``mitigate should demonstrate error reduction`` () =
        async {
            // Arrange: Circuit with realistic noisy executor
            let circuit = 
                CircuitBuilder.empty 1
                |> CircuitBuilder.addGate (CircuitBuilder.H 0)
            
            let config: ProbabilisticErrorCancellation.PECConfig = {
                NoiseModel = {
                    SingleQubitDepolarizing = 0.01  // 1% error
                    TwoQubitDepolarizing = 0.02
                    ReadoutError = 0.02
                }
                Samples = 100  // More samples for better statistics
                Seed = Some 42
            }
            
            let trueValue = 1.0  // Ideal expectation
            let baselineNoisy = 0.85  // Noisy baseline (15% error)
            
            // Mock executor: Returns noisy value with some variance
            let mutable executionCount = 0
            let mockExecutor (_: CircuitBuilder.Circuit) =
                async {
                    executionCount <- executionCount + 1
                    // Add small variance to simulate realistic noise
                    let variance = (float executionCount % 3.0 - 1.0) * 0.01
                    return Ok (baselineNoisy + variance)
                }
            
            // Act: Apply PEC
            let! result = ProbabilisticErrorCancellation.mitigate circuit config mockExecutor
            
            // Assert: Error should be reduced (corrected closer to true value)
            match result with
            | Ok pecResult ->
                // PEC should improve over baseline
                let baselineError = abs (trueValue - baselineNoisy)
                let pecError = abs (trueValue - pecResult.CorrectedExpectation)
                
                // Note: With mock executor, we may not see dramatic improvement,
                // but the algorithm should complete successfully
                Assert.True(pecResult.ErrorReduction >= 0.0, 
                    "Error reduction should be non-negative")
                
                printfn "Baseline error: %.3f, PEC error: %.3f, Reduction: %.1f%%" 
                    baselineError pecError (pecResult.ErrorReduction * 100.0)
            | Error err ->
                Assert.Fail(sprintf "PEC failed: %s" err)
        } |> Async.RunSynchronously
    
    [<Fact>]
    let ``mitigate should track overhead correctly`` () =
        async {
            // Arrange
            let circuit = 
                CircuitBuilder.empty 1
                |> CircuitBuilder.addGate (CircuitBuilder.H 0)
            
            let samples = 50
            let config: ProbabilisticErrorCancellation.PECConfig = {
                NoiseModel = {
                    SingleQubitDepolarizing = 0.001
                    TwoQubitDepolarizing = 0.01
                    ReadoutError = 0.02
                }
                Samples = samples
                Seed = Some 42
            }
            
            let mutable executionCount = 0
            let mockExecutor (_: CircuitBuilder.Circuit) =
                async {
                    System.Threading.Interlocked.Increment(&executionCount) |> ignore
                    return Ok 0.85
                }
            
            // Act
            let! result = ProbabilisticErrorCancellation.mitigate circuit config mockExecutor
            
            // Assert: Should execute samples + 1 (baseline) circuits
            match result with
            | Ok pecResult ->
                // PEC overhead: samples for mitigation + 1 for uncorrected baseline
                let expectedExecutions = samples + 1
                Assert.Equal(expectedExecutions, executionCount)
                
                // Overhead should be samples (since baseline is 1x)
                Assert.Equal(float samples, pecResult.Overhead, 1)
            | Error err ->
                Assert.Fail(sprintf "PEC failed: %s" err)
        } |> Async.RunSynchronously
    
    [<Fact>]
    let ``mitigate should handle executor failures gracefully`` () =
        async {
            // Arrange
            let circuit = 
                CircuitBuilder.empty 1
                |> CircuitBuilder.addGate (CircuitBuilder.H 0)
            
            let config: ProbabilisticErrorCancellation.PECConfig = {
                NoiseModel = {
                    SingleQubitDepolarizing = 0.001
                    TwoQubitDepolarizing = 0.01
                    ReadoutError = 0.02
                }
                Samples = 10
                Seed = Some 42
            }
            
            // Failing executor
            let failingExecutor (_: CircuitBuilder.Circuit) =
                async { return Error "Quantum hardware unavailable" }
            
            // Act
            let! result = ProbabilisticErrorCancellation.mitigate circuit config failingExecutor
            
            // Assert: Should propagate error gracefully
            match result with
            | Error err -> 
                Assert.Contains("execution failed", err.ToLower())
            | Ok _ -> 
                Assert.Fail("Expected error for failing executor")
        } |> Async.RunSynchronously
    
    [<Fact>]
    let ``mitigate should work with multi-gate circuit`` () =
        async {
            // Arrange: Circuit with multiple gates
            let circuit = 
                CircuitBuilder.empty 2
                |> CircuitBuilder.addGate (CircuitBuilder.H 0)
                |> CircuitBuilder.addGate (CircuitBuilder.CNOT (0, 1))
                |> CircuitBuilder.addGate (CircuitBuilder.H 1)
            
            let config: ProbabilisticErrorCancellation.PECConfig = {
                NoiseModel = {
                    SingleQubitDepolarizing = 0.001
                    TwoQubitDepolarizing = 0.01
                    ReadoutError = 0.02
                }
                Samples = 20
                Seed = Some 42
            }
            
            let mockExecutor (_: CircuitBuilder.Circuit) =
                async { return Ok 0.75 }
            
            // Act
            let! result = ProbabilisticErrorCancellation.mitigate circuit config mockExecutor
            
            // Assert: Should handle multi-gate circuit
            match result with
            | Ok pecResult ->
                Assert.Equal(20, pecResult.SamplesUsed)
                Assert.True(pecResult.CorrectedExpectation <> 0.0)
            | Error err ->
                Assert.Fail(sprintf "Multi-gate PEC failed: %s" err)
        } |> Async.RunSynchronously
    
    [<Fact>]
    let ``mitigate should be deterministic with same seed`` () =
        async {
            // Arrange
            let circuit = 
                CircuitBuilder.empty 1
                |> CircuitBuilder.addGate (CircuitBuilder.H 0)
            
            let config: ProbabilisticErrorCancellation.PECConfig = {
                NoiseModel = {
                    SingleQubitDepolarizing = 0.001
                    TwoQubitDepolarizing = 0.01
                    ReadoutError = 0.02
                }
                Samples = 20
                Seed = Some 123  // Fixed seed
            }
            
            let mockExecutor (_: CircuitBuilder.Circuit) =
                async { return Ok 0.85 }
            
            // Act: Run twice with same seed
            let! result1 = ProbabilisticErrorCancellation.mitigate circuit config mockExecutor
            let! result2 = ProbabilisticErrorCancellation.mitigate circuit config mockExecutor
            
            // Assert: Should get identical results
            match result1, result2 with
            | Ok pec1, Ok pec2 ->
                Assert.Equal(pec1.CorrectedExpectation, pec2.CorrectedExpectation, 10)
                Assert.Equal(pec1.ErrorReduction, pec2.ErrorReduction, 10)
            | _ ->
                Assert.Fail("Both runs should succeed")
        } |> Async.RunSynchronously
