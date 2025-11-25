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
