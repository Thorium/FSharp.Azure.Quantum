namespace FSharp.Azure.Quantum.Tests

open Xunit
open FSharp.Azure.Quantum

module ZeroNoiseExtrapolationTests =
    
    // ============================================================================
    // TKT-43: Zero-Noise Extrapolation Tests
    // ============================================================================
    
    // Cycle #1: NoiseScaling type and basic structure
    
    [<Fact>]
    let ``NoiseScaling IdentityInsertion should represent insertion rate`` () =
        // Arrange: Identity insertion with 50% rate (adds 50% more circuit depth)
        let noiseScaling = ZeroNoiseExtrapolation.NoiseScaling.IdentityInsertion 0.5
        
        // Act: Extract insertion rate
        let rate = 
            match noiseScaling with
            | ZeroNoiseExtrapolation.NoiseScaling.IdentityInsertion r -> r
            | _ -> failwith "Expected IdentityInsertion"
        
        // Assert: Rate should be 0.5
        Assert.Equal(0.5, rate)
    
    [<Fact>]
    let ``NoiseScaling PulseStretching should represent stretch factor`` () =
        // Arrange: Pulse stretching with 1.5x factor (50% longer pulses)
        let noiseScaling = ZeroNoiseExtrapolation.NoiseScaling.PulseStretching 1.5
        
        // Act: Extract stretch factor
        let factor = 
            match noiseScaling with
            | ZeroNoiseExtrapolation.NoiseScaling.PulseStretching f -> f
            | _ -> failwith "Expected PulseStretching"
        
        // Assert: Factor should be 1.5
        Assert.Equal(1.5, factor)
    
    // Cycle #2: Apply noise scaling to circuits - Beautiful composition!
    
    [<Fact>]
    let ``applyNoiseScaling IdentityInsertion should increase circuit depth`` () =
        // Arrange: Simple circuit with 3 gates
        let circuit = 
            CircuitBuilder.empty 2
            |> CircuitBuilder.addGate (CircuitBuilder.H 0)
            |> CircuitBuilder.addGate (CircuitBuilder.CNOT (0, 1))
            |> CircuitBuilder.addGate (CircuitBuilder.H 1)
        
        let noiseScaling = ZeroNoiseExtrapolation.NoiseScaling.IdentityInsertion 0.5
        
        // Act: Apply identity insertion (50% rate = 1.5x depth)
        let noisyCircuit = ZeroNoiseExtrapolation.applyNoiseScaling noiseScaling circuit
        
        // Assert: Circuit depth should be ~1.5x (3 gates → ~4-5 gates with I·I pairs)
        let originalDepth = CircuitBuilder.gateCount circuit
        let noisyDepth = CircuitBuilder.gateCount noisyCircuit
        
        Assert.True(noisyDepth > originalDepth, 
            sprintf "Expected noisy circuit (%d gates) to have more gates than original (%d gates)" 
                noisyDepth originalDepth)
    
    [<Fact>]
    let ``applyNoiseScaling IdentityInsertion should preserve qubit count`` () =
        // Arrange: Circuit with 3 qubits
        let circuit = CircuitBuilder.empty 3
        let noiseScaling = ZeroNoiseExtrapolation.NoiseScaling.IdentityInsertion 0.5
        
        // Act: Apply noise scaling
        let noisyCircuit = ZeroNoiseExtrapolation.applyNoiseScaling noiseScaling circuit
        
        // Assert: Qubit count should remain the same
        Assert.Equal(CircuitBuilder.qubitCount circuit, CircuitBuilder.qubitCount noisyCircuit)
    
    [<Fact>]
    let ``applyNoiseScaling PulseStretching should preserve circuit structure`` () =
        // Arrange: Circuit with rotation gates
        let circuit = 
            CircuitBuilder.empty 2
            |> CircuitBuilder.addGate (CircuitBuilder.RX (0, 0.5))
            |> CircuitBuilder.addGate (CircuitBuilder.RY (1, 1.0))
        
        let noiseScaling = ZeroNoiseExtrapolation.NoiseScaling.PulseStretching 1.5
        
        // Act: Apply pulse stretching
        let noisyCircuit = ZeroNoiseExtrapolation.applyNoiseScaling noiseScaling circuit
        
        // Assert: Gate count and qubit count should remain the same
        // (Pulse stretching doesn't add gates, it modifies pulse duration metadata)
        Assert.Equal(CircuitBuilder.gateCount circuit, CircuitBuilder.gateCount noisyCircuit)
        Assert.Equal(CircuitBuilder.qubitCount circuit, CircuitBuilder.qubitCount noisyCircuit)
    
    // Cycle #3: Polynomial fitting - Idiomatic F# with MathNet
    
    [<Fact>]
    let ``fitPolynomial should fit quadratic ZNE curve`` () =
        // Arrange: Realistic ZNE scenario - noise decreases expectation value
        // As noise increases (λ), expectation value decreases
        let noisePoints = [
            (1.0, 0.80)   // Baseline noise level
            (1.5, 0.72)   // 50% more noise
            (2.0, 0.65)   // 100% more noise
        ]
        let degree = 2
        
        // Act: Fit polynomial using MathNet
        let coefficients = ZeroNoiseExtrapolation.fitPolynomial degree noisePoints
        
        // Assert: Should have 3 coefficients [a₀, a₁, a₂]
        Assert.Equal(3, coefficients.Length)
        
        // a₀ (zero-noise value) should be > baseline (error mitigation improves result)
        Assert.True(coefficients.[0] > 0.80,
            sprintf "Expected a₀ > 0.80 (zero-noise should be better than baseline), got %f" coefficients.[0])
    
    [<Fact>]
    let ``fitPolynomial should handle linear fit`` () =
        // Arrange: Linear relationship E(λ) = 1.0 - 0.2λ
        let noisePoints = [
            (1.0, 0.8)   // E(1.0) = 0.8
            (1.5, 0.7)   // E(1.5) = 0.7
            (2.0, 0.6)   // E(2.0) = 0.6
        ]
        let degree = 1
        
        // Act: Fit polynomial
        let coefficients = ZeroNoiseExtrapolation.fitPolynomial degree noisePoints
        
        // Assert: Should have 2 coefficients [a₀, a₁]
        Assert.Equal(2, coefficients.Length)
        
        // a₀ should be close to 1.0 (zero-noise extrapolation)
        Assert.True(abs (coefficients.[0] - 1.0) < 0.1,
            sprintf "Expected a₀ ≈ 1.0, got %f" coefficients.[0])
    
    [<Fact>]
    let ``extrapolateToZeroNoise should return constant term`` () =
        // Arrange: Polynomial coefficients [0.9, -0.1, 0.05]
        // E(λ) = 0.9 - 0.1λ + 0.05λ²
        let coefficients = [0.9; -0.1; 0.05]
        
        // Act: Extrapolate to λ=0
        let zeroNoiseValue = ZeroNoiseExtrapolation.extrapolateToZeroNoise coefficients
        
        // Assert: E(0) = a₀ = 0.9
        Assert.Equal(0.9, zeroNoiseValue, 3)
    
    [<Fact>]
    let ``extrapolateToZeroNoise with empty coefficients should return zero`` () =
        // Arrange: No coefficients (edge case)
        let coefficients = []
        
        // Act: Extrapolate to λ=0
        let zeroNoiseValue = ZeroNoiseExtrapolation.extrapolateToZeroNoise coefficients
        
        // Assert: Should return 0.0 as fallback
        Assert.Equal(0.0, zeroNoiseValue)
    
    // Cycle #4: Full ZNE pipeline - Beautiful composition of all pieces!
    
    [<Fact>]
    let ``mitigate should compose all ZNE steps`` () =
        async {
            // Arrange: Simple circuit and configuration
            let circuit = 
                CircuitBuilder.empty 2
                |> CircuitBuilder.addGate (CircuitBuilder.H 0)
                |> CircuitBuilder.addGate (CircuitBuilder.CNOT (0, 1))
            
            let config: ZeroNoiseExtrapolation.ZNEConfig = {
                NoiseScalings = [
                    ZeroNoiseExtrapolation.NoiseScaling.IdentityInsertion 0.0   // Baseline (1.0x noise)
                    ZeroNoiseExtrapolation.NoiseScaling.IdentityInsertion 0.5   // 1.5x noise
                    ZeroNoiseExtrapolation.NoiseScaling.IdentityInsertion 1.0   // 2.0x noise
                ]
                PolynomialDegree = 2
                MinSamples = 100
            }
            
            // Mock executor: Realistic noise model (noise decreases expectation)
            let mockExecutor (noisyCircuit: CircuitBuilder.Circuit) =
                async {
                    let gateCount = float (CircuitBuilder.gateCount noisyCircuit)
                    let baselineGates = 2.0  // Original circuit
                    let noiseLevelEstimate = gateCount / baselineGates
                    // Realistic: E(λ) decreases with noise, but not linearly
                    // Using quadratic: E(λ) = 0.95 - 0.15λ + 0.05λ²
                    let expectation = 0.95 - 0.15 * noiseLevelEstimate + 0.05 * (noiseLevelEstimate ** 2.0)
                    return Ok expectation
                }
            
            // Act: Run full ZNE pipeline
            let! result = ZeroNoiseExtrapolation.mitigate circuit config mockExecutor
            
            // Assert: Should return successful ZNE result
            match result with
            | Ok zneResult ->
                // Zero-noise value should be >= baseline
                Assert.True(zneResult.ZeroNoiseValue >= 0.8,
                    sprintf "Expected zero-noise value >= 0.8, got %f" zneResult.ZeroNoiseValue)
                
                // Should have 3 measurements (one per noise level)
                Assert.Equal(3, zneResult.MeasuredValues.Length)
                
                // Should have polynomial coefficients
                Assert.Equal(3, zneResult.PolynomialCoefficients.Length)
                
                // Goodness of fit should be reasonable
                Assert.True(zneResult.GoodnessOfFit >= 0.0 && zneResult.GoodnessOfFit <= 1.0)
            | Error err ->
                Assert.Fail(sprintf "ZNE pipeline failed: %s" err)
        } |> Async.RunSynchronously
    
    [<Fact>]
    let ``mitigate should demonstrate error reduction`` () =
        async {
            // Arrange: Circuit with known baseline noise
            let circuit = CircuitBuilder.empty 1 |> CircuitBuilder.addGate (CircuitBuilder.H 0)
            
            let config: ZeroNoiseExtrapolation.ZNEConfig = {
                NoiseScalings = [
                    ZeroNoiseExtrapolation.NoiseScaling.IdentityInsertion 0.0   // 1.0x
                    ZeroNoiseExtrapolation.NoiseScaling.IdentityInsertion 0.5   // 1.5x  
                    ZeroNoiseExtrapolation.NoiseScaling.IdentityInsertion 1.0   // 2.0x
                ]
                PolynomialDegree = 2
                MinSamples = 100
            }
            
            // Realistic executor: Linear noise degradation
            let mockExecutor (noisyCircuit: CircuitBuilder.Circuit) =
                async {
                    let noiseLevel = float (CircuitBuilder.gateCount noisyCircuit)
                    let baselineExpectation = 0.80  // Noisy baseline
                    let expectation = baselineExpectation - (noiseLevel - 1.0) * 0.1
                    return Ok expectation
                }
            
            // Act: Apply ZNE
            let! result = ZeroNoiseExtrapolation.mitigate circuit config mockExecutor
            
            // Assert: Error reduction (zero-noise > baseline)
            match result with
            | Ok zneResult ->
                // First measurement is baseline (1.0x noise)
                let baseline = zneResult.MeasuredValues |> List.head |> snd
                
                // Zero-noise should be better than baseline (error mitigation!)
                Assert.True(zneResult.ZeroNoiseValue > baseline,
                    sprintf "Expected error reduction: zero-noise (%f) > baseline (%f)" 
                        zneResult.ZeroNoiseValue baseline)
            | Error err ->
                Assert.Fail(sprintf "ZNE failed: %s" err)
        } |> Async.RunSynchronously
    
    // Cycle #5: Configuration builders and defaults - Idiomatic F# usability
    
    [<Fact>]
    let ``defaultConfig should provide sensible IonQ defaults`` () =
        // Arrange & Act: Get default config for IonQ
        let config = ZeroNoiseExtrapolation.defaultIonQConfig
        
        // Assert: Should use identity insertion
        Assert.Equal(3, config.NoiseScalings.Length)
        Assert.All(config.NoiseScalings, fun scaling ->
            match scaling with
            | ZeroNoiseExtrapolation.NoiseScaling.IdentityInsertion _ -> ()
            | _ -> Assert.Fail("Expected IdentityInsertion for IonQ"))
        
        // Should have reasonable polynomial degree
        Assert.Equal(2, config.PolynomialDegree)
        
        // Should have sufficient samples
        Assert.True(config.MinSamples >= 1000)
    
    [<Fact>]
    let ``defaultConfig should provide sensible Rigetti defaults`` () =
        // Arrange & Act: Get default config for Rigetti
        let config = ZeroNoiseExtrapolation.defaultRigettiConfig
        
        // Assert: Should use pulse stretching
        Assert.Equal(3, config.NoiseScalings.Length)
        Assert.All(config.NoiseScalings, fun scaling ->
            match scaling with
            | ZeroNoiseExtrapolation.NoiseScaling.PulseStretching _ -> ()
            | _ -> Assert.Fail("Expected PulseStretching for Rigetti"))
        
        // Same quality standards
        Assert.Equal(2, config.PolynomialDegree)
        Assert.True(config.MinSamples >= 1000)
    
    [<Fact>]
    let ``withNoiseScalings should override noise levels`` () =
        // Arrange: Start with default config
        let customScalings = [
            ZeroNoiseExtrapolation.NoiseScaling.IdentityInsertion 0.0
            ZeroNoiseExtrapolation.NoiseScaling.IdentityInsertion 1.0
        ]
        
        // Act: Override noise scalings (fluent API)
        let config = 
            ZeroNoiseExtrapolation.defaultIonQConfig
            |> ZeroNoiseExtrapolation.withNoiseScalings customScalings
        
        // Assert: Should have custom scalings
        Assert.Equal(2, config.NoiseScalings.Length)
    
    [<Fact>]
    let ``withPolynomialDegree should override degree`` () =
        // Arrange & Act: Override polynomial degree
        let config = 
            ZeroNoiseExtrapolation.defaultIonQConfig
            |> ZeroNoiseExtrapolation.withPolynomialDegree 1  // Linear fit
        
        // Assert: Should have degree 1
        Assert.Equal(1, config.PolynomialDegree)
    
    [<Fact>]
    let ``fluent API should compose beautifully`` () =
        // Arrange & Act: Chain multiple overrides
        let config = 
            ZeroNoiseExtrapolation.defaultRigettiConfig
            |> ZeroNoiseExtrapolation.withPolynomialDegree 3
            |> ZeroNoiseExtrapolation.withMinSamples 2048
            |> ZeroNoiseExtrapolation.withNoiseScalings [
                ZeroNoiseExtrapolation.NoiseScaling.PulseStretching 1.0
                ZeroNoiseExtrapolation.NoiseScaling.PulseStretching 2.0
            ]
        
        // Assert: All overrides applied
        Assert.Equal(3, config.PolynomialDegree)
        Assert.Equal(2048, config.MinSamples)
        Assert.Equal(2, config.NoiseScalings.Length)
    
    // Cycle #6: Edge cases and robustness - Production-ready error handling
    
    [<Fact>]
    let ``mitigate should handle executor failures gracefully`` () =
        async {
            // Arrange: Circuit and config
            let circuit = CircuitBuilder.empty 1
            let config = ZeroNoiseExtrapolation.defaultIonQConfig
            
            // Failing executor
            let failingExecutor (_: CircuitBuilder.Circuit) =
                async { return Error "Quantum hardware unavailable" }
            
            // Act: Attempt ZNE
            let! result = ZeroNoiseExtrapolation.mitigate circuit config failingExecutor
            
            // Assert: Should propagate error gracefully
            match result with
            | Error err -> Assert.Contains("execution failed", err)
            | Ok _ -> Assert.Fail("Expected error for failing executor")
        } |> Async.RunSynchronously
    
    [<Fact>]
    let ``fitPolynomial should fail with insufficient data points`` () =
        // Arrange: Only 2 points for degree-2 polynomial (need 3)
        let insufficientData = [(1.0, 0.8); (2.0, 0.7)]
        let degree = 2
        
        // Act & Assert: Should throw exception
        Assert.Throws<exn>(fun () -> 
            ZeroNoiseExtrapolation.fitPolynomial degree insufficientData |> ignore)
    
    [<Fact>]
    let ``applyNoiseScaling with zero rate should return original circuit`` () =
        // Arrange: Circuit with zero insertion rate
        let circuit = 
            CircuitBuilder.empty 2
            |> CircuitBuilder.addGate (CircuitBuilder.H 0)
        
        let noiseScaling = ZeroNoiseExtrapolation.NoiseScaling.IdentityInsertion 0.0
        
        // Act: Apply zero noise scaling
        let result = ZeroNoiseExtrapolation.applyNoiseScaling noiseScaling circuit
        
        // Assert: Should be identical (optimization: no gates added)
        Assert.Equal(CircuitBuilder.gateCount circuit, CircuitBuilder.gateCount result)
    
    [<Fact>]
    let ``mitigate with single noise level should still work`` () =
        async {
            // Arrange: Only baseline noise (edge case)
            let circuit = CircuitBuilder.empty 1
            let config: ZeroNoiseExtrapolation.ZNEConfig = {
                NoiseScalings = [ZeroNoiseExtrapolation.NoiseScaling.IdentityInsertion 0.0]
                PolynomialDegree = 0  // Constant fit
                MinSamples = 100
            }
            
            let executor (_: CircuitBuilder.Circuit) =
                async { return Ok 0.85 }
            
            // Act: Run ZNE
            let! result = ZeroNoiseExtrapolation.mitigate circuit config executor
            
            // Assert: Should return baseline value
            match result with
            | Ok zneResult -> 
                Assert.Equal(0.85, zneResult.ZeroNoiseValue, 2)
            | Error err -> 
                Assert.Fail(sprintf "Should handle single noise level: %s" err)
        } |> Async.RunSynchronously
