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
