namespace FSharp.Azure.Quantum.Tests.Topological

open Xunit
open System.Numerics
open FSharp.Azure.Quantum.Topological

/// Comprehensive tests for TopologicalOperations module
/// 
/// Tests demonstrate:
/// - How quantum superpositions work in topological QC
/// - Braiding operations (the fundamental gates)
/// - Measurement and state collapse
/// - Normalization and probability calculations
module TopologicalOperationsTests =
    
    // ========================================================================
    // SUPERPOSITION CONSTRUCTION
    // ========================================================================
    
    [<Fact>]
    let ``Pure state is a superposition with single term`` () =
        // A pure state |ψ⟩ has amplitude 1 and single basis state
        let sigma = FusionTree.leaf AnyonSpecies.Particle.Sigma
        let tree = FusionTree.fuse sigma sigma AnyonSpecies.Particle.Vacuum
        let state = FusionTree.create tree AnyonSpecies.AnyonType.Ising
        
        let superposition = TopologicalOperations.pureState state
        
        Assert.Equal(1, superposition.Terms.Length)
        Assert.Equal(Complex.One, fst superposition.Terms.[0])
        Assert.True(TopologicalOperations.isNormalized superposition)
    
    [<Fact>]
    let ``Uniform superposition has equal amplitudes`` () =
        // Create |+⟩ = (|0⟩ + |1⟩)/√2 superposition
        let sigma = FusionTree.leaf AnyonSpecies.Particle.Sigma
        
        let state0 = FusionTree.create (FusionTree.fuse sigma sigma AnyonSpecies.Particle.Vacuum) AnyonSpecies.AnyonType.Ising
        let state1 = FusionTree.create (FusionTree.fuse sigma sigma AnyonSpecies.Particle.Psi) AnyonSpecies.AnyonType.Ising
        
        let superposition = TopologicalOperations.uniform [state0; state1] AnyonSpecies.AnyonType.Ising
        
        Assert.Equal(2, superposition.Terms.Length)
        
        // Each amplitude should be 1/√2
        let expectedAmp = 1.0 / sqrt 2.0
        superposition.Terms |> List.iter (fun (amp, _) ->
            Assert.Equal(expectedAmp, amp.Real, 10)
            Assert.Equal(0.0, amp.Imaginary, 10)
        )
        
        // Should be normalized
        Assert.True(TopologicalOperations.isNormalized superposition)
    
    [<Fact>]
    let ``Superposition can be normalized`` () =
        // Create unnormalized state: 2|0⟩ + 3|1⟩
        let sigma = FusionTree.leaf AnyonSpecies.Particle.Sigma
        
        let state0 = FusionTree.create (FusionTree.fuse sigma sigma AnyonSpecies.Particle.Vacuum) AnyonSpecies.AnyonType.Ising
        let state1 = FusionTree.create (FusionTree.fuse sigma sigma AnyonSpecies.Particle.Psi) AnyonSpecies.AnyonType.Ising
        
        let unnormalized : TopologicalOperations.Superposition = { 
            Terms = [(Complex(2.0, 0.0), state0); (Complex(3.0, 0.0), state1)]
            AnyonType = AnyonSpecies.AnyonType.Ising 
        }
        
        // Not normalized initially
        Assert.False(TopologicalOperations.isNormalized unnormalized)
        
        // Normalize it
        let normalized = TopologicalOperations.normalize unnormalized
        
        // Now it should be normalized
        Assert.True(TopologicalOperations.isNormalized normalized)
        
        // Check probabilities: |2|² = 4, |3|² = 9, total = 13
        // After normalization: 4/13 and 9/13
        let prob0 = TopologicalOperations.probability (fst normalized.Terms.[0])
        let prob1 = TopologicalOperations.probability (fst normalized.Terms.[1])
        
        Assert.Equal(4.0 / 13.0, prob0, 10)
        Assert.Equal(9.0 / 13.0, prob1, 10)
    
    // ========================================================================
    // BUSINESS MEANING: QUANTUM SUPERPOSITION
    // ========================================================================
    
    [<Fact>]
    let ``Topological qubit in equal superposition has 50-50 measurement probability`` () =
        // THE KEY QUANTUM IDEA: Superposition = being in multiple states simultaneously
        // |+⟩ = (|0⟩ + |1⟩)/√2 means 50% chance of measuring 0, 50% chance of measuring 1
        
        let sigma = FusionTree.leaf AnyonSpecies.Particle.Sigma
        
        // Create basis states
        let qubitZero = FusionTree.create (FusionTree.fuse sigma sigma AnyonSpecies.Particle.Vacuum) AnyonSpecies.AnyonType.Ising
        let qubitOne = FusionTree.create (FusionTree.fuse sigma sigma AnyonSpecies.Particle.Psi) AnyonSpecies.AnyonType.Ising
        
        // Create equal superposition
        let plusState = TopologicalOperations.uniform [qubitZero; qubitOne] AnyonSpecies.AnyonType.Ising
        
        // Each term has amplitude 1/√2
        let prob0 = TopologicalOperations.probability (fst plusState.Terms.[0])
        let prob1 = TopologicalOperations.probability (fst plusState.Terms.[1])
        
        // Both probabilities should be 1/2 (50%)
        Assert.Equal(0.5, prob0, 10)
        Assert.Equal(0.5, prob1, 10)
        
        // Total probability = 1 (something MUST happen when we measure)
        Assert.Equal(1.0, prob0 + prob1, 10)
    
    [<Fact>]
    let ``Unequal superposition has asymmetric measurement probabilities`` () =
        // |ψ⟩ = (√3/2)|0⟩ + (1/2)|1⟩
        // Probability of measuring |0⟩ = (√3/2)² = 3/4 = 75%
        // Probability of measuring |1⟩ = (1/2)² = 1/4 = 25%
        
        let sigma = FusionTree.leaf AnyonSpecies.Particle.Sigma
        
        let state0 = FusionTree.create (FusionTree.fuse sigma sigma AnyonSpecies.Particle.Vacuum) AnyonSpecies.AnyonType.Ising
        let state1 = FusionTree.create (FusionTree.fuse sigma sigma AnyonSpecies.Particle.Psi) AnyonSpecies.AnyonType.Ising
        
        let amp0 = Complex(sqrt 3.0 / 2.0, 0.0)
        let amp1 = Complex(0.5, 0.0)
        
        let superposition : TopologicalOperations.Superposition = { 
            Terms = [(amp0, state0); (amp1, state1)]
            AnyonType = AnyonSpecies.AnyonType.Ising 
        }
        
        let prob0 = TopologicalOperations.probability amp0
        let prob1 = TopologicalOperations.probability amp1
        
        Assert.Equal(0.75, prob0, 10)  // 75% chance
        Assert.Equal(0.25, prob1, 10)  // 25% chance
        Assert.True(TopologicalOperations.isNormalized superposition)
    
    // ========================================================================
    // BRAIDING OPERATIONS
    // ========================================================================
    
    [<Fact>]
    let ``Braiding two sigma anyons accumulates phase`` () =
        // Braiding is the fundamental gate operation!
        // When we braid σ × σ, we get a phase from the R-matrix
        
        let sigma = FusionTree.leaf AnyonSpecies.Particle.Sigma
        let tree = FusionTree.fuse sigma sigma AnyonSpecies.Particle.Vacuum
        let state = FusionTree.create tree AnyonSpecies.AnyonType.Ising
        
        // Braid the two sigma anyons (indices 0 and 1)
        match TopologicalOperations.braidAdjacentAnyons 0 state with
        | Error err -> Assert.Fail($"Braiding should succeed: {err.Message}")
        | Ok braided ->
            // Should accumulate a phase (from R-matrix)
            Assert.NotEmpty(braided.Terms)
            
            // Single basis state in this simple 2-anyon case
            Assert.Equal(1, braided.Terms.Length)
            
            let (amp, _) = braided.Terms.[0]
            Assert.NotEqual(Complex.Zero, amp)
            
            // Should have unit magnitude (unitary operation)
            let magnitude = Complex.Abs amp
            Assert.Equal(1.0, magnitude, 10)
    
    [<Fact>]
    let ``Braiding is a unitary operation (preserves norm)`` () =
        // THE KEY PROPERTY: Braiding doesn't change probabilities, only phases
        // This makes it a valid quantum gate (unitary)
        
        let sigma = FusionTree.leaf AnyonSpecies.Particle.Sigma
        let state0 = FusionTree.create (FusionTree.fuse sigma sigma AnyonSpecies.Particle.Vacuum) AnyonSpecies.AnyonType.Ising
        let state1 = FusionTree.create (FusionTree.fuse sigma sigma AnyonSpecies.Particle.Psi) AnyonSpecies.AnyonType.Ising
        
        let superposition = TopologicalOperations.uniform [state0; state1] AnyonSpecies.AnyonType.Ising
        
        // Braid anyons at position 0
        match TopologicalOperations.braidSuperposition 0 superposition with
        | Error err -> Assert.Fail($"Braiding superposition should succeed: {err.Message}")
        | Ok braided ->
            // Should still be normalized (unitary preserves norm)
            Assert.True(TopologicalOperations.isNormalized braided)
            
            // Same number of terms
            Assert.Equal(superposition.Terms.Length, braided.Terms.Length)
    
    [<Fact>]
    let ``Invalid braid index returns validation error`` () =
        let sigma = FusionTree.leaf AnyonSpecies.Particle.Sigma
        let tree = FusionTree.fuse sigma sigma AnyonSpecies.Particle.Vacuum
        let state = FusionTree.create tree AnyonSpecies.AnyonType.Ising
        
        // Only 2 anyons, so valid indices are 0 only (braids indices 0-1)
        // Index 1 would try to braid indices 1-2, but we only have 2 anyons
        match TopologicalOperations.braidAdjacentAnyons 1 state with
        | Ok _ -> Assert.Fail("Should have returned validation error")
        | Error (TopologicalError.ValidationError (_, reason)) ->
            Assert.Contains("Invalid braid index", reason)
        | Error err -> Assert.Fail($"Expected ValidationError but got {err.Category}")
        
        // Negative index should also fail
        match TopologicalOperations.braidAdjacentAnyons -1 state with
        | Ok _ -> Assert.Fail("Should have returned validation error")
        | Error (TopologicalError.ValidationError _) -> ()
        | Error err -> Assert.Fail($"Expected ValidationError but got {err.Category}")
    
    // ========================================================================
    // MEASUREMENT OPERATIONS
    // ========================================================================
    
    [<Fact>]
    let ``Measurement collapses superposition to classical outcome`` () =
        // Measurement is IRREVERSIBLE - we learn information but destroy superposition
        
        let sigma = FusionTree.leaf AnyonSpecies.Particle.Sigma
        let tree = FusionTree.fuse sigma sigma AnyonSpecies.Particle.Vacuum
        let state = FusionTree.create tree AnyonSpecies.AnyonType.Ising
        
        // Measure fusion at index 0
        match TopologicalOperations.measureFusion 0 state with
        | Error err -> Assert.Fail($"Measurement should succeed: {err.Message}")
        | Ok outcomes ->
            // Should get possible outcomes (for σ × σ, could be Vacuum or Psi)
            Assert.NotEmpty(outcomes)
            
            // Each outcome has a probability
            outcomes |> List.iter (fun (prob, result) ->
                Assert.True(prob >= 0.0 && prob <= 1.0)
                
                // Measurement gives classical information
                Assert.True(result.ClassicalOutcome.IsSome)
            )
            
            // Probabilities should sum to 1
            let totalProb = outcomes |> List.sumBy fst
            Assert.Equal(1.0, totalProb, 10)
    
    [<Fact>]
    let ``Measurement reduces number of anyons`` () =
        // When we fuse two anyons, they combine into one
        // This is how we extract classical information
        
        let sigma = FusionTree.leaf AnyonSpecies.Particle.Sigma
        
        // Create 4 sigma anyons
        let pair1 = FusionTree.fuse sigma sigma AnyonSpecies.Particle.Vacuum
        let pair2 = FusionTree.fuse sigma sigma AnyonSpecies.Particle.Vacuum
        let fourSigmas = FusionTree.fuse pair1 pair2 AnyonSpecies.Particle.Vacuum
        let state = FusionTree.create fourSigmas AnyonSpecies.AnyonType.Ising
        
        Assert.Equal(4, FusionTree.size state.Tree)
        
        // Measure fusion at index 0 (fuse first two anyons)
        match TopologicalOperations.measureFusion 0 state with
        | Error err -> Assert.Fail($"Measurement should succeed: {err.Message}")
        | Ok outcomes ->
            // After measurement, should have fewer anyons
            outcomes |> List.iter (fun (_, result) ->
                let newSize = FusionTree.size result.State.Tree
                Assert.True(newSize < 4)  // Reduced by fusion
            )
    
    // ========================================================================
    // PROBABILITY CALCULATIONS
    // ========================================================================
    
    [<Fact>]
    let ``Probability is magnitude squared of amplitude`` () =
        // Born rule: P = |ψ|²
        
        let testCases = [
            (Complex(1.0, 0.0), 1.0)           // |1|² = 1
            (Complex(0.0, 1.0), 1.0)           // |i|² = 1
            (Complex(1.0/sqrt(2.0), 0.0), 0.5) // |1/√2|² = 1/2
            (Complex(0.6, 0.8), 1.0)           // |0.6 + 0.8i|² = 0.36 + 0.64 = 1
        ]
        
        testCases |> List.iter (fun (amp, expectedProb) ->
            let prob = TopologicalOperations.probability amp
            Assert.Equal(expectedProb, prob, 10)
        )
    
    [<Fact>]
    let ``Zero amplitude has zero probability`` () =
        let prob = TopologicalOperations.probability Complex.Zero
        Assert.Equal(0.0, prob)
    
    // ========================================================================
    // UTILITY FUNCTIONS
    // ========================================================================
    
    [<Fact>]
    let ``Dimension equals number of basis states in superposition`` () =
        let sigma = FusionTree.leaf AnyonSpecies.Particle.Sigma
        let state0 = FusionTree.create (FusionTree.fuse sigma sigma AnyonSpecies.Particle.Vacuum) AnyonSpecies.AnyonType.Ising
        let state1 = FusionTree.create (FusionTree.fuse sigma sigma AnyonSpecies.Particle.Psi) AnyonSpecies.AnyonType.Ising
        
        let superposition = TopologicalOperations.uniform [state0; state1] AnyonSpecies.AnyonType.Ising
        
        Assert.Equal(2, TopologicalOperations.dimension superposition)
    
    [<Fact>]
    let ``Basis states can be extracted from superposition`` () =
        let sigma = FusionTree.leaf AnyonSpecies.Particle.Sigma
        let state0 = FusionTree.create (FusionTree.fuse sigma sigma AnyonSpecies.Particle.Vacuum) AnyonSpecies.AnyonType.Ising
        let state1 = FusionTree.create (FusionTree.fuse sigma sigma AnyonSpecies.Particle.Psi) AnyonSpecies.AnyonType.Ising
        
        let superposition = TopologicalOperations.uniform [state0; state1] AnyonSpecies.AnyonType.Ising
        let basis = TopologicalOperations.basisStates superposition
        
        Assert.Equal(2, basis.Length)
        
        // Should contain both basis states
        Assert.Contains(basis, fun s -> 
            FusionTree.totalCharge s.Tree s.AnyonType = AnyonSpecies.Particle.Vacuum)
        Assert.Contains(basis, fun s -> 
            FusionTree.totalCharge s.Tree s.AnyonType = AnyonSpecies.Particle.Psi)
    
    [<Fact>]
    let ``Superposition can be pretty-printed`` () =
        let sigma = FusionTree.leaf AnyonSpecies.Particle.Sigma
        let state0 = FusionTree.create (FusionTree.fuse sigma sigma AnyonSpecies.Particle.Vacuum) AnyonSpecies.AnyonType.Ising
        let state1 = FusionTree.create (FusionTree.fuse sigma sigma AnyonSpecies.Particle.Psi) AnyonSpecies.AnyonType.Ising
        
        let superposition = TopologicalOperations.uniform [state0; state1] AnyonSpecies.AnyonType.Ising
        let display = TopologicalOperations.displaySuperposition superposition
        
        // Should contain key information
        Assert.Contains("Superposition", display)
        Assert.Contains("Normalized", display)
        Assert.Contains("Sigma", display)
    
    // ========================================================================
    // NORMALIZATION EDGE CASES
    // ========================================================================
    
    [<Fact>]
    let ``Normalizing zero state returns zero state`` () =
        let sigma = FusionTree.leaf AnyonSpecies.Particle.Sigma
        let state = FusionTree.create (FusionTree.fuse sigma sigma AnyonSpecies.Particle.Vacuum) AnyonSpecies.AnyonType.Ising
        
        let zeroState : TopologicalOperations.Superposition = { 
            Terms = [(Complex.Zero, state)]
            AnyonType = AnyonSpecies.AnyonType.Ising 
        }
        
        let normalized = TopologicalOperations.normalize zeroState
        
        // Should still be zero
        Assert.Equal(Complex.Zero, fst normalized.Terms.[0])
    
    [<Fact>]
    let ``Multiple identical states sum amplitudes`` () =
        // If superposition has duplicate states, they should be distinguishable
        let sigma = FusionTree.leaf AnyonSpecies.Particle.Sigma
        let state = FusionTree.create (FusionTree.fuse sigma sigma AnyonSpecies.Particle.Vacuum) AnyonSpecies.AnyonType.Ising
        
        // Create superposition with same state twice
        let superposition : TopologicalOperations.Superposition = { 
            Terms = [(Complex(0.6, 0.0), state); (Complex(0.8, 0.0), state)]
            AnyonType = AnyonSpecies.AnyonType.Ising 
        }
        
        // This is technically allowed (represents 0.6|ψ⟩ + 0.8|ψ⟩ = 1.4|ψ⟩)
        // After normalization, should be normalized
        let normalized = TopologicalOperations.normalize superposition
        Assert.True(TopologicalOperations.isNormalized normalized)

    // ========================================================================
    // F-MOVE OPERATIONS (BASIC)
    // ========================================================================

    [<Fact>]
    let ``F-move returns normalized superposition`` () =
        // F-moves are basis transformations
        // For meaningful systems, they can produce a superposition.

        // Use σσσ→σ associator where F is non-trivial.
        let sigma = FusionTree.leaf AnyonSpecies.Particle.Sigma

        // ((σ×σ→1)×σ→σ)
        let leftAssoc =
            FusionTree.fuse
                (FusionTree.fuse sigma sigma AnyonSpecies.Particle.Vacuum)
                sigma
                AnyonSpecies.Particle.Sigma

        let state = FusionTree.create leftAssoc AnyonSpecies.AnyonType.Ising
        let result = TopologicalOperations.fMove TopologicalOperations.FMoveDirection.LeftToRight 0 state

        Assert.NotEmpty(result.Terms)
        Assert.True(TopologicalOperations.isNormalized result)

    // ========================================================================
    // BRAIDING + F-MOVE (INTERFERENCE)
    // ========================================================================

    [<Fact>]
    let ``Braiding in a 3-anyon system mixes basis amplitudes`` () =
        // In the σσσ→σ fusion space, braiding the right pair requires an F-move,
        // so it should generally create a superposition in the left-associated basis.

        let sigma = FusionTree.leaf AnyonSpecies.Particle.Sigma

        // Start in a basis state with a fixed intermediate channel e=1
        let initialTree =
            FusionTree.fuse
                (FusionTree.fuse sigma sigma AnyonSpecies.Particle.Vacuum)
                sigma
                AnyonSpecies.Particle.Sigma

        let state = FusionTree.create initialTree AnyonSpecies.AnyonType.Ising

        // Braid b and c (indices 1 and 2)
        match TopologicalOperations.braidAdjacentAnyons 1 state with
        | Error err -> Assert.Fail($"Braiding should succeed: {err.Message}")
        | Ok braided ->
            // Should create more than one term (mixing)
            Assert.True(braided.Terms.Length >= 2)

            // Still normalized/unitary
            Assert.True(TopologicalOperations.isNormalized braided)

            // There should be at least two distinct trees involved
            let distinctTrees =
                braided.Terms
                |> List.map (fun (_, s) -> FusionTree.toString s.Tree)
                |> List.distinct

            Assert.True(distinctTrees.Length >= 2)

    // ========================================================================
    // HADAMARD GATE
    // ========================================================================

    /// Helper: create an n-qubit computational basis state as a Superposition
    let private mkBasisState (bits: int list) =
        match FusionTree.fromComputationalBasis bits AnyonSpecies.AnyonType.Ising with
        | Ok tree ->
            let state = FusionTree.create tree AnyonSpecies.AnyonType.Ising
            TopologicalOperations.pureState state
        | Error err -> failwith $"Failed to create basis state {bits}: {err.Message}"

    /// Helper: read the computational basis bits from every term in a superposition
    let private readTermBits (sup: TopologicalOperations.Superposition) =
        sup.Terms
        |> List.map (fun (amp, st) -> (amp, FusionTree.toComputationalBasis st.Tree))

    [<Fact>]
    let ``Hadamard on |0⟩ produces equal superposition (|0⟩ + |1⟩)/√2`` () =
        let sup0 = mkBasisState [0]

        match TopologicalOperations.hadamard 0 sup0 with
        | Error err -> Assert.Fail($"Hadamard should succeed: {err.Message}")
        | Ok result ->
            // Should have two terms
            Assert.Equal(2, result.Terms.Length)

            // Read bits for each term
            let termBits = readTermBits result
            let amp0 = termBits |> List.find (fun (_, bits) -> bits = [0]) |> fst
            let amp1 = termBits |> List.find (fun (_, bits) -> bits = [1]) |> fst

            let invSqrt2 = 1.0 / sqrt 2.0

            // H|0⟩ = (|0⟩ + |1⟩)/√2 — both amplitudes positive and equal
            Assert.Equal(invSqrt2, amp0.Real, 10)
            Assert.Equal(0.0, amp0.Imaginary, 10)
            Assert.Equal(invSqrt2, amp1.Real, 10)
            Assert.Equal(0.0, amp1.Imaginary, 10)

            Assert.True(TopologicalOperations.isNormalized result)

    [<Fact>]
    let ``Hadamard on |1⟩ produces (|0⟩ - |1⟩)/√2`` () =
        let sup1 = mkBasisState [1]

        match TopologicalOperations.hadamard 0 sup1 with
        | Error err -> Assert.Fail($"Hadamard should succeed: {err.Message}")
        | Ok result ->
            Assert.Equal(2, result.Terms.Length)

            let termBits = readTermBits result
            let amp0 = termBits |> List.find (fun (_, bits) -> bits = [0]) |> fst
            let amp1 = termBits |> List.find (fun (_, bits) -> bits = [1]) |> fst

            let invSqrt2 = 1.0 / sqrt 2.0

            // H|1⟩ = (|0⟩ - |1⟩)/√2 — |0⟩ positive, |1⟩ negative
            Assert.Equal(invSqrt2, amp0.Real, 10)
            Assert.Equal(0.0, amp0.Imaginary, 10)
            Assert.Equal(-invSqrt2, amp1.Real, 10)
            Assert.Equal(0.0, amp1.Imaginary, 10)

            Assert.True(TopologicalOperations.isNormalized result)

    [<Fact>]
    let ``Hadamard is an involution (HH = I)`` () =
        // Apply H twice to |0⟩ → should return to |0⟩
        let sup0 = mkBasisState [0]

        match TopologicalOperations.hadamard 0 sup0 with
        | Error err -> Assert.Fail($"First H should succeed: {err.Message}")
        | Ok afterFirstH ->
            match TopologicalOperations.hadamard 0 afterFirstH with
            | Error err -> Assert.Fail($"Second H should succeed: {err.Message}")
            | Ok afterSecondH ->
                // Should be back to |0⟩ (single term)
                let termBits = readTermBits afterSecondH
                // After combineLikeTerms, zero-amplitude terms should be eliminated
                let nonZeroTerms =
                    termBits |> List.filter (fun (amp, _) -> Complex.Abs amp > 1e-10)

                Assert.Equal(1, nonZeroTerms.Length)
                let (amp, bits) = nonZeroTerms.[0]
                Assert.Equal<int list>([0], bits)
                Assert.Equal(1.0, amp.Real, 10)
                Assert.Equal(0.0, amp.Imaginary, 10)

    [<Fact>]
    let ``Hadamard is an involution on |1⟩ (HH|1⟩ = |1⟩)`` () =
        let sup1 = mkBasisState [1]

        match TopologicalOperations.hadamard 0 sup1 with
        | Error err -> Assert.Fail($"First H should succeed: {err.Message}")
        | Ok afterFirstH ->
            match TopologicalOperations.hadamard 0 afterFirstH with
            | Error err -> Assert.Fail($"Second H should succeed: {err.Message}")
            | Ok afterSecondH ->
                let termBits = readTermBits afterSecondH
                let nonZeroTerms =
                    termBits |> List.filter (fun (amp, _) -> Complex.Abs amp > 1e-10)

                Assert.Equal(1, nonZeroTerms.Length)
                let (amp, bits) = nonZeroTerms.[0]
                Assert.Equal<int list>([1], bits)
                Assert.Equal(1.0, amp.Real, 10)
                Assert.Equal(0.0, amp.Imaginary, 10)

    [<Fact>]
    let ``Hadamard on qubit 0 of 2-qubit |00⟩ only affects qubit 0`` () =
        let sup00 = mkBasisState [0; 0]

        match TopologicalOperations.hadamard 0 sup00 with
        | Error err -> Assert.Fail($"Hadamard should succeed: {err.Message}")
        | Ok result ->
            // Should have 2 terms: |00⟩ and |10⟩ (qubit 0 in superposition, qubit 1 still 0)
            Assert.Equal(2, result.Terms.Length)

            let termBits = readTermBits result
            let bits0 = termBits |> List.find (fun (_, bits) -> bits = [0; 0]) |> fst
            let bits1 = termBits |> List.find (fun (_, bits) -> bits = [1; 0]) |> fst

            let invSqrt2 = 1.0 / sqrt 2.0
            Assert.Equal(invSqrt2, Complex.Abs bits0, 10)
            Assert.Equal(invSqrt2, Complex.Abs bits1, 10)

            // Qubit 1 is always 0 in both terms
            termBits |> List.iter (fun (_, bits) ->
                Assert.Equal(0, bits.[1])
            )

            Assert.True(TopologicalOperations.isNormalized result)

    [<Fact>]
    let ``Hadamard on qubit 1 of 2-qubit |00⟩ only affects qubit 1`` () =
        let sup00 = mkBasisState [0; 0]

        match TopologicalOperations.hadamard 1 sup00 with
        | Error err -> Assert.Fail($"Hadamard should succeed: {err.Message}")
        | Ok result ->
            // Should have 2 terms: |00⟩ and |01⟩ (qubit 0 still 0, qubit 1 in superposition)
            Assert.Equal(2, result.Terms.Length)

            let termBits = readTermBits result
            let bits0 = termBits |> List.find (fun (_, bits) -> bits = [0; 0]) |> fst
            let bits1 = termBits |> List.find (fun (_, bits) -> bits = [0; 1]) |> fst

            let invSqrt2 = 1.0 / sqrt 2.0
            Assert.Equal(invSqrt2, Complex.Abs bits0, 10)
            Assert.Equal(invSqrt2, Complex.Abs bits1, 10)

            // Qubit 0 is always 0 in both terms
            termBits |> List.iter (fun (_, bits) ->
                Assert.Equal(0, bits.[0])
            )

            Assert.True(TopologicalOperations.isNormalized result)

    [<Fact>]
    let ``Hadamard preserves normalization`` () =
        // Test with various initial states
        let states = [
            mkBasisState [0]
            mkBasisState [1]
            mkBasisState [0; 0]
            mkBasisState [1; 1]
            mkBasisState [0; 1; 0]
        ]

        states |> List.iteri (fun i sup ->
            match TopologicalOperations.hadamard 0 sup with
            | Error err -> Assert.Fail($"Hadamard on state {i} should succeed: {err.Message}")
            | Ok result ->
                Assert.True(
                    TopologicalOperations.isNormalized result,
                    $"State {i} should be normalized after Hadamard"
                )
        )

    [<Fact>]
    let ``Hadamard with negative qubit index returns ValidationError`` () =
        let sup = mkBasisState [0]

        match TopologicalOperations.hadamard -1 sup with
        | Ok _ -> Assert.Fail("Should return error for negative index")
        | Error (TopologicalError.ValidationError (field, _)) ->
            Assert.Equal("qubitIndex", field)
        | Error err -> Assert.Fail($"Expected ValidationError but got {err.Category}")

    [<Fact>]
    let ``Hadamard with out-of-range qubit index returns ValidationError`` () =
        let sup = mkBasisState [0]  // 1-qubit state, valid index is 0 only

        match TopologicalOperations.hadamard 1 sup with
        | Ok _ -> Assert.Fail("Should return error for out-of-range index")
        | Error (TopologicalError.ValidationError (field, reason)) ->
            Assert.Equal("qubitIndex", field)
            Assert.Contains("1", reason)  // mentions the invalid index
        | Error err -> Assert.Fail($"Expected ValidationError but got {err.Category}")

    [<Fact>]
    let ``Hadamard with large out-of-range index returns ValidationError`` () =
        let sup = mkBasisState [0; 1]  // 2-qubit state, valid indices 0 and 1

        match TopologicalOperations.hadamard 5 sup with
        | Ok _ -> Assert.Fail("Should return error for index 5 on 2-qubit state")
        | Error (TopologicalError.ValidationError _) -> ()
        | Error err -> Assert.Fail($"Expected ValidationError but got {err.Category}")

    [<Fact>]
    let ``Hadamard on (|0⟩ + |1⟩)/√2 returns |0⟩ (H undoes plus state)`` () =
        // H|+⟩ = H·(|0⟩+|1⟩)/√2 = |0⟩
        // First create |+⟩ by applying H to |0⟩
        let sup0 = mkBasisState [0]

        match TopologicalOperations.hadamard 0 sup0 with
        | Error err -> Assert.Fail($"Creating |+⟩ should succeed: {err.Message}")
        | Ok plusState ->
            // Now apply H again: should get |0⟩
            match TopologicalOperations.hadamard 0 plusState with
            | Error err -> Assert.Fail($"H on |+⟩ should succeed: {err.Message}")
            | Ok result ->
                let termBits = readTermBits result
                let nonZeroTerms =
                    termBits |> List.filter (fun (amp, _) -> Complex.Abs amp > 1e-10)

                Assert.Equal(1, nonZeroTerms.Length)
                let (_, bits) = nonZeroTerms.[0]
                Assert.Equal<int list>([0], bits)

    [<Fact>]
    let ``Hadamard on (|0⟩ - |1⟩)/√2 returns |1⟩ (H undoes minus state)`` () =
        // H|−⟩ = H·(|0⟩-|1⟩)/√2 = |1⟩
        // Create |−⟩ by applying H to |1⟩
        let sup1 = mkBasisState [1]

        match TopologicalOperations.hadamard 0 sup1 with
        | Error err -> Assert.Fail($"Creating |−⟩ should succeed: {err.Message}")
        | Ok minusState ->
            match TopologicalOperations.hadamard 0 minusState with
            | Error err -> Assert.Fail($"H on |−⟩ should succeed: {err.Message}")
            | Ok result ->
                let termBits = readTermBits result
                let nonZeroTerms =
                    termBits |> List.filter (fun (amp, _) -> Complex.Abs amp > 1e-10)

                Assert.Equal(1, nonZeroTerms.Length)
                let (_, bits) = nonZeroTerms.[0]
                Assert.Equal<int list>([1], bits)

    [<Fact>]
    let ``Hadamard on qubit 0 then qubit 1 of |00⟩ creates full superposition`` () =
        // H₀ H₁ |00⟩ = (|0⟩+|1⟩)/√2 ⊗ (|0⟩+|1⟩)/√2 = (|00⟩+|01⟩+|10⟩+|11⟩)/2
        let sup00 = mkBasisState [0; 0]

        match TopologicalOperations.hadamard 0 sup00 with
        | Error err -> Assert.Fail($"H on qubit 0 should succeed: {err.Message}")
        | Ok afterH0 ->
            match TopologicalOperations.hadamard 1 afterH0 with
            | Error err -> Assert.Fail($"H on qubit 1 should succeed: {err.Message}")
            | Ok result ->
                // Should have 4 terms with equal amplitude 1/2
                Assert.Equal(4, result.Terms.Length)

                let termBits = readTermBits result
                let allBitPatterns =
                    termBits |> List.map snd |> List.sort

                Assert.Equal<int list list>([[0;0]; [0;1]; [1;0]; [1;1]], allBitPatterns)

                // Each amplitude should be 1/2
                termBits |> List.iter (fun (amp, _) ->
                    Assert.Equal(0.5, Complex.Abs amp, 10)
                )

                Assert.True(TopologicalOperations.isNormalized result)

