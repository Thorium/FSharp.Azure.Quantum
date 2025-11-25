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
