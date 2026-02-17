namespace FSharp.Azure.Quantum.Topological.Tests

open Xunit
open FSharp.Azure.Quantum.Topological
open System.Numerics

module BraidGroupTests =
    
    // ========================================================================
    // TEST HELPERS - Make tests more readable and maintainable
    // ========================================================================
    
    /// Helper to create braid from generators or fail
    let private braidFromGensOrFail n gens testContext =
        match BraidGroup.fromGenerators n gens with
        | Ok braid -> braid
        | Error err -> failwith $"{testContext}: {err.Message}"
    
    /// Helper to compose braids or fail
    let private composeOrFail b1 b2 testContext =
        match BraidGroup.compose b1 b2 with
        | Ok composed -> composed
        | Error err -> failwith $"{testContext}: {err.Message}"
    
    /// Helper to apply braid or fail
    let private applyBraidOrFail braid anyons channel anyonType testContext =
        match BraidGroup.applyBraid braid anyons channel anyonType with
        | Ok result -> result
        | Error err -> failwith $"{testContext}: {err.Message}"
    
    /// Helper to assert complex values are approximately equal
    let private assertPhaseEquals (expected: Complex) (actual: Complex) =
        let tolerance = 1e-10
        Assert.InRange(actual.Real, expected.Real - tolerance, expected.Real + tolerance)
        Assert.InRange(actual.Imaginary, expected.Imaginary - tolerance, expected.Imaginary + tolerance)
    
    /// Helper to unwrap identity braid or fail
    let private identityOrFail n testContext =
        match BraidGroup.identity n with
        | Ok braid -> braid
        | Error err -> failwith $"{testContext}: {err.Message}"

    /// Helper to assert phase is on unit circle
    let private assertOnUnitCircle (phase: Complex) =
        let magnitude = phase.Magnitude
        Assert.InRange(magnitude, 1.0 - 1e-10, 1.0 + 1e-10)
    
    // ========================================================================
    // BRAID WORD CONSTRUCTION TESTS
    // ========================================================================
    
    [<Fact>]
    let ``Creating sigma generator produces clockwise braid at correct index`` () =
        // Business meaning: σ_i represents braiding strand i under strand i+1
        // in the clockwise direction. This is the fundamental building block
        // of all braids in topological quantum computation.
        let gen = BraidGroup.sigma 2
        
        Assert.Equal(2, gen.Index)
        Assert.True(gen.IsClockwise, "σ_i should be clockwise")
    
    [<Fact>]
    let ``Creating sigma inverse generator produces counter-clockwise braid`` () =
        // Business meaning: σ_i^{-1} is the inverse operation - braiding strand i
        // over strand i+1 (counter-clockwise). Essential for undoing braids.
        let gen = BraidGroup.sigmaInv 1
        
        Assert.Equal(1, gen.Index)
        Assert.False(gen.IsClockwise, "σ_i⁻¹ should be counter-clockwise")
    
    [<Fact>]
    let ``Identity braid has empty generator list`` () =
        // Business meaning: The identity braid performs no exchanges. It leaves
        // all anyons in their original positions with no phase accumulation.
        let idBraid = identityOrFail 3 "Identity braid"
        
        Assert.Equal(3, idBraid.StrandCount)
        Assert.Empty(idBraid.Generators)
    
    [<Fact>]
    let ``Creating braid from valid generators succeeds`` () =
        // Business meaning: A braid word is a sequence of elementary generators.
        // For 4 strands, valid indices are 0, 1, 2 (braiding pairs 0-1, 1-2, 2-3).
        let gens = [BraidGroup.sigma 0; BraidGroup.sigma 1; BraidGroup.sigmaInv 0]
        let braid = braidFromGensOrFail 4 gens "Valid 4-strand braid"
        
        Assert.Equal(4, braid.StrandCount)
        Assert.Equal(3, braid.Generators.Length)
    
    [<Fact>]
    let ``Creating braid with out-of-range generator fails`` () =
        // Business meaning: For n strands, valid generator indices are 0 to n-2.
        // Index n-1 would try to braid non-existent strand n.
        let gens = [BraidGroup.sigma 3]  // Invalid for 3 strands
        
        match BraidGroup.fromGenerators 3 gens with
        | Ok _ -> failwith "Should have rejected out-of-range generator"
        | Error err -> 
            Assert.Contains("out of range", err.Message)
            Assert.Contains("strands", err.Message)
    
    [<Fact>]
    let ``Identity braid requires at least 2 strands`` () =
        // Business meaning: Braiding requires at least two objects to exchange.
        // A single anyon cannot be braided.
        match BraidGroup.identity 1 with
        | Ok _ -> failwith "Should have rejected 1-strand identity"
        | Error err -> Assert.Contains("2 strands", err.Message)
    
    // ========================================================================
    // BRAID COMPOSITION TESTS
    // ========================================================================
    
    [<Fact>]
    let ``Composing two braids concatenates their generators`` () =
        // Business meaning: Sequential braiding operations compose by performing
        // the first braid, then the second. This is fundamental to building complex
        // braiding patterns from elementary generators.
        let braid1 = braidFromGensOrFail 3 [BraidGroup.sigma 0; BraidGroup.sigma 1] "First braid"
        let braid2 = braidFromGensOrFail 3 [BraidGroup.sigmaInv 0] "Second braid"
        
        let composed = composeOrFail braid1 braid2 "Composition"
        
        Assert.Equal(3, composed.StrandCount)
        Assert.Equal(3, composed.Generators.Length)
        Assert.Equal(0, composed.Generators.[0].Index)
        Assert.Equal(1, composed.Generators.[1].Index)
        Assert.Equal(0, composed.Generators.[2].Index)
    
    [<Fact>]
    let ``Composing braids with different strand counts fails`` () =
        // Business meaning: Cannot compose braids on different numbers of anyons.
        // The physical systems must match.
        let braid3 = identityOrFail 3 "3-strand identity"
        let braid4 = identityOrFail 4 "4-strand identity"
        
        match BraidGroup.compose braid3 braid4 with
        | Ok _ -> failwith "Should have rejected different strand counts"
        | Error err -> Assert.Contains("different strand counts", err.Message)
    
    [<Fact>]
    let ``Composing with identity braid is a no-op`` () =
        // Business meaning: Identity is the unit element - braiding with identity
        // doesn't change the braid. This is a fundamental group property.
        let braid = braidFromGensOrFail 3 [BraidGroup.sigma 0; BraidGroup.sigma 1] "Test braid"
        let idBraid = identityOrFail 3 "Identity"
        
        let leftCompose = composeOrFail idBraid braid "Identity · Braid"
        let rightCompose = composeOrFail braid idBraid "Braid · Identity"
        
        Assert.Equal(braid.Generators.Length, leftCompose.Generators.Length)
        Assert.Equal(braid.Generators.Length, rightCompose.Generators.Length)
    
    // ========================================================================
    // BRAID INVERSE TESTS
    // ========================================================================
    
    [<Fact>]
    let ``Inverting a braid reverses generators and flips directions`` () =
        // Business meaning: The inverse braid undoes the original. To reverse
        // σ_1 σ_2, we must apply σ_2^{-1} σ_1^{-1} (reverse order, flip direction).
        let braid = 
            braidFromGensOrFail 3 
                [BraidGroup.sigma 0; BraidGroup.sigma 1; BraidGroup.sigmaInv 0]
                "Original braid"
        
        let inv = BraidGroup.inverse braid
        
        Assert.Equal(3, inv.StrandCount)
        Assert.Equal(3, inv.Generators.Length)
        
        // Check reversed order and flipped directions
        Assert.Equal(0, inv.Generators.[0].Index)
        Assert.True(inv.Generators.[0].IsClockwise)  // Was σ_0^{-1}, now σ_0
        
        Assert.Equal(1, inv.Generators.[1].Index)
        Assert.False(inv.Generators.[1].IsClockwise)  // Was σ_1, now σ_1^{-1}
        
        Assert.Equal(0, inv.Generators.[2].Index)
        Assert.False(inv.Generators.[2].IsClockwise)  // Was σ_0, now σ_0^{-1}
    
    [<Fact>]
    let ``Inverse of identity is identity`` () =
        // Business meaning: Undoing "do nothing" is still "do nothing".
        let idBraid = identityOrFail 3 "Identity for inverse"
        let inv = BraidGroup.inverse idBraid
        
        Assert.Equal(3, inv.StrandCount)
        Assert.Empty(inv.Generators)
    
    [<Fact>]
    let ``Double inverse returns original braid`` () =
        // Business meaning: (B^{-1})^{-1} = B. Inverting twice gets you back.
        let braid = braidFromGensOrFail 3 [BraidGroup.sigma 0; BraidGroup.sigma 1] "Original"
        let doubleInv = BraidGroup.inverse (BraidGroup.inverse braid)
        
        Assert.Equal(braid.Generators.Length, doubleInv.Generators.Length)
        Assert.All(
            List.zip braid.Generators doubleInv.Generators,
            fun (g1, g2) ->
                Assert.Equal(g1.Index, g2.Index)
                Assert.Equal(g1.IsClockwise, g2.IsClockwise)
        )
    
    // ========================================================================
    // BRAID SIMPLIFICATION TESTS
    // ========================================================================
    
    [<Fact>]
    let ``Simplify cancels adjacent inverse pairs`` () =
        // Business meaning: σ_i σ_i^{-1} = identity (braiding then unbraiding
        // leaves anyons unchanged). Simplification removes such redundancies.
        let braid = 
            braidFromGensOrFail 3 
                [BraidGroup.sigma 0; BraidGroup.sigmaInv 0; BraidGroup.sigma 1]
                "Braid with cancellation"
        
        let simplified = BraidGroup.simplify braid
        
        Assert.Equal(1, simplified.Generators.Length)
        Assert.Equal(1, simplified.Generators.[0].Index)
    
    [<Fact>]
    let ``Simplify handles multiple cancellations`` () =
        // Business meaning: σ_0 σ_0^{-1} σ_1 σ_1^{-1} simplifies to identity.
        let braid = 
            braidFromGensOrFail 3 
                [BraidGroup.sigma 0; BraidGroup.sigmaInv 0; BraidGroup.sigma 1; BraidGroup.sigmaInv 1]
                "Multiple cancellations"
        
        let simplified = BraidGroup.simplify braid
        
        Assert.Empty(simplified.Generators)
    
    [<Fact>]
    let ``Simplify does not change already-simple braids`` () =
        // Business meaning: If there are no cancellations, braid stays unchanged.
        let braid = braidFromGensOrFail 3 [BraidGroup.sigma 0; BraidGroup.sigma 1] "Simple braid"
        let simplified = BraidGroup.simplify braid
        
        Assert.Equal(braid.Generators.Length, simplified.Generators.Length)
    
    // ========================================================================
    // BRAID RELATION TESTS
    // ========================================================================
    
    [<Fact>]
    let ``Generators separated by 2+ indices commute (far commutativity)`` () =
        // Business meaning: σ_0 and σ_2 act on disjoint pairs of strands
        // (0-1 and 2-3), so they commute. Order doesn't matter.
        let g1 = BraidGroup.sigma 0
        let g2 = BraidGroup.sigma 2
        
        Assert.True(BraidGroup.doCommute g1 g2, "σ_0 and σ_2 should commute")
    
    [<Fact>]
    let ``Adjacent generators do not commute`` () =
        // Business meaning: σ_0 and σ_1 act on overlapping strands (1 is shared),
        // so they don't commute. Order matters for adjacent braids.
        let g1 = BraidGroup.sigma 0
        let g2 = BraidGroup.sigma 1
        
        Assert.False(BraidGroup.doCommute g1 g2, "σ_0 and σ_1 should not commute")
    
    [<Fact>]
    let ``Yang-Baxter triple σ_i σ_{i+1} σ_i is detected correctly`` () =
        // Business meaning: This is the fundamental Yang-Baxter pattern that
        // must hold for consistency in topological quantum computation.
        let g1 = BraidGroup.sigma 0
        let g2 = BraidGroup.sigma 1
        let g3 = BraidGroup.sigma 0
        
        Assert.True(BraidGroup.isYangBaxterTriple g1 g2 g3, "Should detect Yang-Baxter pattern")
    
    [<Fact>]
    let ``Yang-Baxter triple σ_{i+1} σ_i σ_{i+1} is detected correctly`` () =
        // Business meaning: The symmetric Yang-Baxter pattern (other ordering).
        let g1 = BraidGroup.sigma 1
        let g2 = BraidGroup.sigma 0
        let g3 = BraidGroup.sigma 1
        
        Assert.True(BraidGroup.isYangBaxterTriple g1 g2 g3, "Should detect symmetric pattern")
    
    [<Fact>]
    let ``Non-Yang-Baxter triple is not detected as YB`` () =
        // Business meaning: σ_0 σ_1 σ_2 doesn't satisfy Yang-Baxter.
        let g1 = BraidGroup.sigma 0
        let g2 = BraidGroup.sigma 1
        let g3 = BraidGroup.sigma 2
        
        Assert.False(BraidGroup.isYangBaxterTriple g1 g2 g3)
    
    // ========================================================================
    // APPLYING BRAIDS TO ANYON STATES
    // ========================================================================
    
    [<Fact>]
    let ``Applying identity braid gives phase 1`` () =
        // Business meaning: Identity braid doesn't change the quantum state.
        // Phase accumulated is 1 (no phase change).
        let idBraid = identityOrFail 2 "Identity 2-strand"
        let anyons = [AnyonSpecies.Particle.Sigma; AnyonSpecies.Particle.Sigma]
        let channel = AnyonSpecies.Particle.Vacuum
        
        let result = applyBraidOrFail idBraid anyons channel AnyonSpecies.AnyonType.Ising "Identity"
        
        assertPhaseEquals Complex.One result.Phase
    
    [<Fact>]
    let ``Braiding Ising σ×σ→1 accumulates R-matrix phase`` () =
        // Business meaning: Braiding two Ising Majorana modes (σ anyons) that
        // fuse to vacuum accumulates the topological Berry phase exp(-iπ/8) (Kitaev 2006).
        let braid = braidFromGensOrFail 2 [BraidGroup.sigma 0] "Single σ_0"
        let anyons = [AnyonSpecies.Particle.Sigma; AnyonSpecies.Particle.Sigma]
        let channel = AnyonSpecies.Particle.Vacuum
        
        let result = applyBraidOrFail braid anyons channel AnyonSpecies.AnyonType.Ising "σ×σ→1"
        
        let expectedPhase = TopologicalHelpers.expI (-System.Math.PI / 8.0)
        assertPhaseEquals expectedPhase result.Phase
        assertOnUnitCircle result.Phase
    
    [<Fact>]
    let ``Braiding with inverse gives conjugate phase`` () =
        // Business meaning: Braiding counter-clockwise (σ^{-1}) gives the
        // complex conjugate phase. For unitary R-matrices, R^{-1} = R*.
        let clockwise = braidFromGensOrFail 2 [BraidGroup.sigma 0] "Clockwise"
        let counterClock = braidFromGensOrFail 2 [BraidGroup.sigmaInv 0] "Counter-clockwise"
        let anyons = [AnyonSpecies.Particle.Sigma; AnyonSpecies.Particle.Sigma]
        let channel = AnyonSpecies.Particle.Vacuum
        
        let cwResult = applyBraidOrFail clockwise anyons channel AnyonSpecies.AnyonType.Ising "CW"
        let ccwResult = applyBraidOrFail counterClock anyons channel AnyonSpecies.AnyonType.Ising "CCW"
        
        assertPhaseEquals (Complex.Conjugate cwResult.Phase) ccwResult.Phase
    
    [<Fact>]
    let ``Braid and inverse composition gives identity phase`` () =
        // Business meaning: Braiding then unbraiding returns to original state
        // with no net phase (trivial phase = 1). This verifies inverse correctness.
        let braid = braidFromGensOrFail 2 [BraidGroup.sigma 0] "Original"
        let invBraid = BraidGroup.inverse braid
        let composed = composeOrFail braid invBraid "Braid · Inverse"
        
        let anyons = [AnyonSpecies.Particle.Sigma; AnyonSpecies.Particle.Sigma]
        let channel = AnyonSpecies.Particle.Vacuum
        
        let result = applyBraidOrFail composed anyons channel AnyonSpecies.AnyonType.Ising "Verification"
        
        // Should be identity phase (1 + 0i)
        assertPhaseEquals Complex.One result.Phase
    
    [<Fact>]
    let ``Applying braid with wrong number of anyons fails`` () =
        // Business meaning: Physical consistency - can't apply a 3-strand braid
        // to a 2-anyon system.
        let braid = braidFromGensOrFail 3 [BraidGroup.sigma 0] "3-strand braid"
        let anyons = [AnyonSpecies.Particle.Sigma; AnyonSpecies.Particle.Sigma]  // Only 2!
        let channel = AnyonSpecies.Particle.Vacuum
        
        match BraidGroup.applyBraid braid anyons channel AnyonSpecies.AnyonType.Ising with
        | Ok _ -> failwith "Should have rejected mismatched anyon count"
        | Error err -> Assert.Contains("strands but", err.Message)
    
    [<Fact>]
    let ``Braiding Fibonacci τ×τ→1 accumulates correct phase`` () =
        // Business meaning: Fibonacci anyons have different R-matrix phases.
        // τ×τ→1 braiding gives exp(4πi/5).
        let braid = braidFromGensOrFail 2 [BraidGroup.sigma 0] "Single σ_0"
        let anyons = [AnyonSpecies.Particle.Tau; AnyonSpecies.Particle.Tau]
        let channel = AnyonSpecies.Particle.Vacuum
        
        let result = applyBraidOrFail braid anyons channel AnyonSpecies.AnyonType.Fibonacci "τ×τ→1"
        
        let expectedPhase = TopologicalHelpers.expI (4.0 * System.Math.PI / 5.0)
        assertPhaseEquals expectedPhase result.Phase
        assertOnUnitCircle result.Phase
    
    [<Fact>]
    let ``All braid phases lie on unit circle preserving probability`` () =
        // Business meaning: R-matrices are unitary. Braiding can only change
        // quantum phase, not probability amplitudes. |R| must equal 1.
        let braid = braidFromGensOrFail 3 [BraidGroup.sigma 0; BraidGroup.sigma 1] "Multi-braid"
        let anyons = [
            AnyonSpecies.Particle.Sigma
            AnyonSpecies.Particle.Sigma
            AnyonSpecies.Particle.Sigma
        ]
        // σ×σ×σ can fuse to σ or ψ (not all three σ×σ→σ which is invalid)
        // For this test, we use ψ as a valid final fusion outcome
        let channel = AnyonSpecies.Particle.Psi
        
        let result = applyBraidOrFail braid anyons channel AnyonSpecies.AnyonType.Ising "Test"
        
        assertOnUnitCircle result.Phase
    
    // ========================================================================
    // WELL-KNOWN BRAIDS TESTS
    // ========================================================================
    
    [<Fact>]
    let ``Full twist braid for 3 strands has correct structure`` () =
        // Business meaning: Full twist rotates all anyons 360° around the center.
        // For 3 strands: σ_0 σ_1 σ_0 σ_1 (two passes through all generators).
        match BraidGroup.fullTwist 3 with
        | Error err -> failwith $"Failed: {err.Message}"
        | Ok twist ->
            Assert.Equal(3, twist.StrandCount)
            Assert.Equal(4, twist.Generators.Length)
            
            // Check pattern: 0, 1, 0, 1
            Assert.Equal(0, twist.Generators.[0].Index)
            Assert.Equal(1, twist.Generators.[1].Index)
            Assert.Equal(0, twist.Generators.[2].Index)
            Assert.Equal(1, twist.Generators.[3].Index)
    
    [<Fact>]
    let ``Exchange braid is single generator`` () =
        // Business meaning: Exchanging adjacent anyons is the elementary operation.
        match BraidGroup.exchange 1 3 with
        | Error err -> failwith $"Failed: {err.Message}"
        | Ok exch ->
            Assert.Single(exch.Generators) |> ignore
            Assert.Equal(1, exch.Generators.[0].Index)
    
    [<Fact>]
    let ``Cyclic permutation moves first strand to end`` () =
        // Business meaning: For 3 strands, moves strand 0 to position 2.
        // Pattern: σ_1 σ_0 (braid 1 over 0, then move 0 to back).
        match BraidGroup.cyclicPermutation 3 with
        | Error err -> failwith $"Failed: {err.Message}"
        | Ok cycle ->
            Assert.Equal(3, cycle.StrandCount)
            Assert.Equal(2, cycle.Generators.Length)
            Assert.Equal(1, cycle.Generators.[0].Index)
            Assert.Equal(0, cycle.Generators.[1].Index)
    
    // ========================================================================
    // YANG-BAXTER VERIFICATION TESTS
    // ========================================================================
    
    [<Fact>]
    let ``Ising anyons satisfy Yang-Baxter equation σ_0 σ_1 σ_0 = σ_1 σ_0 σ_1`` () =
        // Business meaning: This is THE fundamental consistency relation for
        // topological quantum computation. Both orderings must give same phase.
        let anyons = [
            AnyonSpecies.Particle.Sigma
            AnyonSpecies.Particle.Sigma
            AnyonSpecies.Particle.Sigma
        ]
        // σ×σ×σ can fuse to ψ (valid channel) - NOT σ (invalid)
        let channel = AnyonSpecies.Particle.Psi
        
        match BraidGroup.verifyYangBaxter 0 3 anyons channel AnyonSpecies.AnyonType.Ising with
        | Error err -> failwith $"Failed: {err.Message}"
        | Ok satisfied ->
            Assert.True(satisfied, "Ising anyons must satisfy Yang-Baxter")
    
    [<Fact>]
    let ``Fibonacci anyons satisfy Yang-Baxter equation`` () =
        // Business meaning: Fibonacci anyons must also satisfy Yang-Baxter
        // for universal topological quantum computation to work.
        let anyons = [AnyonSpecies.Particle.Tau; AnyonSpecies.Particle.Tau; AnyonSpecies.Particle.Tau]
        let channel = AnyonSpecies.Particle.Tau
        
        match BraidGroup.verifyYangBaxter 0 3 anyons channel AnyonSpecies.AnyonType.Fibonacci with
        | Error err -> failwith $"Failed: {err.Message}"
        | Ok satisfied ->
            Assert.True(satisfied, "Fibonacci anyons must satisfy Yang-Baxter")

    [<Fact>]
    let ``Ising anyons satisfy Yang-Baxter for ALL fusion channels`` () =
        // Business meaning: Yang-Baxter must hold for EVERY valid total fusion
        // channel, not just the one we happen to pick. This is the comprehensive check.
        let anyons = [
            AnyonSpecies.Particle.Sigma
            AnyonSpecies.Particle.Sigma
            AnyonSpecies.Particle.Sigma
        ]
        
        match BraidGroup.verifyYangBaxterAllChannels 0 3 anyons AnyonSpecies.AnyonType.Ising with
        | Error err -> failwith $"Failed: {err.Message}"
        | Ok satisfied ->
            Assert.True(satisfied, "Ising anyons must satisfy Yang-Baxter for all fusion channels")

    [<Fact>]
    let ``Fibonacci anyons satisfy Yang-Baxter for ALL fusion channels`` () =
        // Business meaning: Comprehensive Yang-Baxter verification across all
        // fusion channels. τ×τ×τ can fuse to both 1 and τ — both must satisfy.
        let anyons = [AnyonSpecies.Particle.Tau; AnyonSpecies.Particle.Tau; AnyonSpecies.Particle.Tau]
        
        match BraidGroup.verifyYangBaxterAllChannels 0 3 anyons AnyonSpecies.AnyonType.Fibonacci with
        | Error err -> failwith $"Failed: {err.Message}"
        | Ok satisfied ->
            Assert.True(satisfied, "Fibonacci anyons must satisfy Yang-Baxter for all fusion channels")
    
    // ========================================================================
    // DISPLAY TESTS
    // ========================================================================
    
    [<Fact>]
    let ``Display braid shows mathematical notation`` () =
        // Business meaning: Human-readable output for debugging and verification.
        let braid = 
            braidFromGensOrFail 3 
                [BraidGroup.sigma 0; BraidGroup.sigmaInv 1; BraidGroup.sigma 0]
                "Test braid"
        
        let display = BraidGroup.displayBraid braid
        
        Assert.Contains("σ_0", display)
        Assert.Contains("σ_1⁻¹", display)
        Assert.Contains("3 strands", display)
    
    [<Fact>]
    let ``Display identity braid shows identity text`` () =
        // Business meaning: Identity should be clearly labeled as such.
        let idBraid = identityOrFail 4 "Identity for display"
        let display = BraidGroup.displayBraid idBraid
        
        Assert.Contains("Identity", display)
        Assert.Contains("4 strands", display)
    
    [<Fact>]
    let ``Display braid result includes phase and steps`` () =
        // Business meaning: For complex braids, show intermediate steps and
        // final accumulated phase for debugging.
        let braid = braidFromGensOrFail 2 [BraidGroup.sigma 0] "Simple braid"
        let anyons = [AnyonSpecies.Particle.Sigma; AnyonSpecies.Particle.Sigma]
        let channel = AnyonSpecies.Particle.Vacuum
        
        let result = applyBraidOrFail braid anyons channel AnyonSpecies.AnyonType.Ising "Test"
        let display = BraidGroup.displayBraidResult result
        
        Assert.Contains("Braid:", display)
        Assert.Contains("Phase:", display)
        Assert.Contains("Steps:", display)
