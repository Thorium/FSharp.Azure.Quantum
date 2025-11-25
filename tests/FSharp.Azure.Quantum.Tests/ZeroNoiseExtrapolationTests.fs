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
