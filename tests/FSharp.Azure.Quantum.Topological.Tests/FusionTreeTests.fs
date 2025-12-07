namespace FSharp.Azure.Quantum.Tests.Topological

open Xunit
open FSharp.Azure.Quantum.Topological

/// Comprehensive tests for FusionTree module
/// 
/// Tests demonstrate business-meaningful assertions:
/// - How topological qubits are encoded in fusion trees
/// - Hilbert space dimension calculations
/// - Quantum state orthogonality via different fusion trees
/// - Validation of physical consistency
module FusionTreeTests =
    
    // ========================================================================
    // BASIC TREE CONSTRUCTION
    // ========================================================================
    
    [<Fact>]
    let ``Single anyon forms a trivial fusion tree`` () =
        // A single sigma anyon is just a leaf
        let tree = FusionTree.leaf AnyonSpecies.Particle.Sigma
        let state = FusionTree.create tree AnyonSpecies.AnyonType.Ising
        
        Assert.Equal(1, FusionTree.size state.Tree)
        Assert.Equal(AnyonSpecies.Particle.Sigma, FusionTree.totalCharge state.Tree state.AnyonType)
        match FusionTree.isValid state.Tree state.AnyonType with
        | Ok true -> ()
        | _ -> Assert.Fail("Expected valid tree")
    
    [<Fact>]
    let ``Two sigma anyons can fuse to vacuum`` () =
        // σ × σ → 1 (vacuum channel)
        let left = FusionTree.leaf AnyonSpecies.Particle.Sigma
        let right = FusionTree.leaf AnyonSpecies.Particle.Sigma
        let tree = FusionTree.fuse left right AnyonSpecies.Particle.Vacuum
        
        let state = FusionTree.create tree AnyonSpecies.AnyonType.Ising
        
        Assert.Equal(2, FusionTree.size state.Tree)
        Assert.Equal(AnyonSpecies.Particle.Vacuum, FusionTree.totalCharge state.Tree state.AnyonType)
        match FusionTree.isValid state.Tree state.AnyonType with
        | Ok true -> ()
        | _ -> Assert.Fail("Expected valid tree")
    
    [<Fact>]
    let ``Two sigma anyons can fuse to psi`` () =
        // σ × σ → ψ (fermion channel)
        let left = FusionTree.leaf AnyonSpecies.Particle.Sigma
        let right = FusionTree.leaf AnyonSpecies.Particle.Sigma
        let tree = FusionTree.fuse left right AnyonSpecies.Particle.Psi
        
        let state = FusionTree.create tree AnyonSpecies.AnyonType.Ising
        
        Assert.Equal(2, FusionTree.size state.Tree)
        Assert.Equal(AnyonSpecies.Particle.Psi, FusionTree.totalCharge state.Tree state.AnyonType)
        match FusionTree.isValid state.Tree state.AnyonType with
        | Ok true -> ()
        | _ -> Assert.Fail("Expected valid tree")
    
    // ========================================================================
    // BUSINESS MEANING: TOPOLOGICAL QUBIT ENCODING
    // ========================================================================
    
    [<Fact>]
    let ``Topological qubit: Two sigma anyons encode a qubit via fusion outcome`` () =
        // THE KEY IDEA: σ × σ = 1 + ψ creates a 2-dimensional Hilbert space
        // |0⟩ ≡ σ × σ → 1 (vacuum)
        // |1⟩ ≡ σ × σ → ψ (fermion)
        
        let sigma = FusionTree.leaf AnyonSpecies.Particle.Sigma
        
        // Computational basis state |0⟩
        let qubitZero = FusionTree.fuse sigma sigma AnyonSpecies.Particle.Vacuum
        
        // Computational basis state |1⟩
        let qubitOne = FusionTree.fuse sigma sigma AnyonSpecies.Particle.Psi
        
        // Both are valid fusion trees
        match FusionTree.isValid qubitZero AnyonSpecies.AnyonType.Ising,
              FusionTree.isValid qubitOne AnyonSpecies.AnyonType.Ising with
        | Ok true, Ok true -> ()
        | _ -> Assert.Fail("Expected both trees to be valid")
        
        // They have different total charges (orthogonal quantum states!)
        Assert.NotEqual(
            FusionTree.totalCharge qubitZero AnyonSpecies.AnyonType.Ising,
            FusionTree.totalCharge qubitOne AnyonSpecies.AnyonType.Ising
        )
        
        // They are not structurally equal
        Assert.False(FusionTree.equals qubitZero qubitOne)
    
    [<Fact>]
    let ``Four sigma anyons create 2-dimensional Hilbert space (1 qubit)`` () =
        // With 4 sigma anyons fusing to vacuum, we get dimension 2
        // This encodes a single topological qubit
        
        let fourSigmas = List.replicate 4 AnyonSpecies.Particle.Sigma
        match FusionTree.fusionSpaceDimension 
                    fourSigmas 
                    AnyonSpecies.Particle.Vacuum 
                    AnyonSpecies.AnyonType.Ising with
        | Ok dimension ->
            // Dimension 2 = 1 qubit
            Assert.Equal(2, dimension)
        | Error err -> Assert.Fail($"Expected Ok but got Error: {err.Message}")
    
    [<Fact>]
    let ``Six sigma anyons create 4-dimensional Hilbert space (2 qubits)`` () =
        // With 6 sigma anyons fusing to vacuum, we get dimension 4 = 2^2
        // This encodes TWO topological qubits
        
        let sixSigmas = List.replicate 6 AnyonSpecies.Particle.Sigma
        match FusionTree.fusionSpaceDimension 
                    sixSigmas 
                    AnyonSpecies.Particle.Vacuum 
                    AnyonSpecies.AnyonType.Ising with
        | Ok dimension ->
            // Dimension 4 = 2 qubits
            Assert.Equal(4, dimension)
        | Error err -> Assert.Fail($"Expected Ok but got Error: {err.Message}")
    
    // ========================================================================
    // HILBERT SPACE DIMENSION CALCULATIONS
    // ========================================================================
    
    [<Fact>]
    let ``Single particle has dimension 1`` () =
        match FusionTree.fusionSpaceDimension 
                  [AnyonSpecies.Particle.Sigma] 
                  AnyonSpecies.Particle.Sigma 
                  AnyonSpecies.AnyonType.Ising with
        | Ok dim -> Assert.Equal(1, dim)
        | Error err -> Assert.Fail($"Expected Ok but got Error: {err.Message}")
    
    [<Fact>]
    let ``Two particles fusing to allowed outcome has dimension 1`` () =
        // σ × σ → 1 is possible with multiplicity 1
        match FusionTree.fusionSpaceDimension 
                  [AnyonSpecies.Particle.Sigma; AnyonSpecies.Particle.Sigma] 
                  AnyonSpecies.Particle.Vacuum 
                  AnyonSpecies.AnyonType.Ising with
        | Ok dim -> Assert.Equal(1, dim)
        | Error err -> Assert.Fail($"Expected Ok but got Error: {err.Message}")
    
    [<Fact>]
    let ``Two particles fusing to forbidden outcome has dimension 0`` () =
        // σ × σ → σ is NOT possible
        match FusionTree.fusionSpaceDimension 
                  [AnyonSpecies.Particle.Sigma; AnyonSpecies.Particle.Sigma] 
                  AnyonSpecies.Particle.Sigma 
                  AnyonSpecies.AnyonType.Ising with
        | Ok dim -> Assert.Equal(0, dim)
        | Error err -> Assert.Fail($"Expected Ok but got Error: {err.Message}")
    
    [<Fact>]
    let ``Fibonacci: Four tau anyons create higher-dimensional space`` () =
        // Fibonacci anyons have richer fusion structure than Ising
        // The dimension grows according to Fibonacci numbers!
        let fourTaus = List.replicate 4 AnyonSpecies.Particle.Tau
        match FusionTree.fusionSpaceDimension 
                  fourTaus 
                  AnyonSpecies.Particle.Vacuum 
                  AnyonSpecies.AnyonType.Fibonacci with
        | Ok dim ->
            // Fibonacci fusion creates a 6-dimensional space
            // (follows Fibonacci number sequence in dimension growth)
            Assert.Equal(6, dim)
        | Error err -> Assert.Fail($"Expected Ok but got Error: {err.Message}")
    
    // ========================================================================
    // TREE STRUCTURE VALIDATION
    // ========================================================================
    
    [<Fact>]
    let ``Invalid fusion tree is rejected`` () =
        // Try to create σ × σ → σ (impossible!)
        let left = FusionTree.leaf AnyonSpecies.Particle.Sigma
        let right = FusionTree.leaf AnyonSpecies.Particle.Sigma
        let invalidTree = FusionTree.fuse left right AnyonSpecies.Particle.Sigma
        
        // This tree violates fusion rules
        match FusionTree.isValid invalidTree AnyonSpecies.AnyonType.Ising with
        | Ok false -> ()
        | _ -> Assert.Fail("Expected invalid tree")
    
    [<Fact>]
    let ``Mixing Ising and Fibonacci particles is invalid`` () =
        // Can't mix σ (Ising) with τ (Fibonacci)
        let sigma = FusionTree.leaf AnyonSpecies.Particle.Sigma
        let tau = FusionTree.leaf AnyonSpecies.Particle.Tau
        let mixedTree = FusionTree.fuse sigma tau AnyonSpecies.Particle.Vacuum
        
        // Invalid in Ising theory
        match FusionTree.isValid mixedTree AnyonSpecies.AnyonType.Ising with
        | Ok false -> ()
        | _ -> Assert.Fail("Expected invalid tree in Ising theory")
        
        // Invalid in Fibonacci theory too
        match FusionTree.isValid mixedTree AnyonSpecies.AnyonType.Fibonacci with
        | Ok false -> ()
        | _ -> Assert.Fail("Expected invalid tree in Fibonacci theory")
    
    [<Fact>]
    let ``Deeply nested tree structure is validated correctly`` () =
        // ((σ × σ → 1) × (σ × σ → 1)) → 1
        let sigma = FusionTree.leaf AnyonSpecies.Particle.Sigma
        
        // Left pair: σ × σ → 1
        let leftPair = FusionTree.fuse sigma sigma AnyonSpecies.Particle.Vacuum
        
        // Right pair: σ × σ → 1
        let rightPair = FusionTree.fuse sigma sigma AnyonSpecies.Particle.Vacuum
        
        // Fuse the pairs: 1 × 1 → 1
        let fourSigmaTree = FusionTree.fuse leftPair rightPair AnyonSpecies.Particle.Vacuum
        
        Assert.Equal(4, FusionTree.size fourSigmaTree)
        Assert.Equal(2, FusionTree.depth fourSigmaTree)
        match FusionTree.isValid fourSigmaTree AnyonSpecies.AnyonType.Ising with
        | Ok true -> ()
        | _ -> Assert.Fail("Expected valid tree")
    
    // ========================================================================
    // TREE ENUMERATION
    // ========================================================================
    
    [<Fact>]
    let ``All valid fusion trees can be enumerated`` () =
        // For 4 sigma anyons fusing to vacuum, there are exactly 2 trees
        let fourSigmas = List.replicate 4 AnyonSpecies.Particle.Sigma
        match FusionTree.allTrees 
                    fourSigmas 
                    AnyonSpecies.Particle.Vacuum 
                    AnyonSpecies.AnyonType.Ising with
        | Ok trees ->
            // Should have 2 distinct fusion trees (basis states)
            Assert.Equal(2, trees.Length)
            
            // All trees should be valid
            trees |> List.iter (fun tree ->
                match FusionTree.isValid tree AnyonSpecies.AnyonType.Ising with
                | Ok true -> ()
                | _ -> Assert.Fail("Expected valid tree")
            )
            
            // All trees should have 4 anyons
            trees |> List.iter (fun tree ->
                Assert.Equal(4, FusionTree.size tree)
            )
        | Error err -> Assert.Fail($"Expected Ok but got Error: {err.Message}")
    
    [<Fact>]
    let ``Enumerated trees match calculated dimension`` () =
        // The number of trees should equal the Hilbert space dimension
        let fourSigmas = List.replicate 4 AnyonSpecies.Particle.Sigma
        
        match FusionTree.allTrees 
                    fourSigmas 
                    AnyonSpecies.Particle.Vacuum 
                    AnyonSpecies.AnyonType.Ising,
              FusionTree.fusionSpaceDimension 
                    fourSigmas 
                    AnyonSpecies.Particle.Vacuum 
                    AnyonSpecies.AnyonType.Ising with
        | Ok trees, Ok dimension ->
            Assert.Equal(dimension, trees.Length)
        | Error err, _ | _, Error err -> Assert.Fail($"Expected Ok but got Error: {err.Message}")
    
    [<Fact>]
    let ``No valid trees exist for impossible fusion`` () =
        // σ × σ → σ is impossible, so no trees should exist
        let twoSigmas = List.replicate 2 AnyonSpecies.Particle.Sigma
        match FusionTree.allTrees 
                    twoSigmas 
                    AnyonSpecies.Particle.Sigma 
                    AnyonSpecies.AnyonType.Ising with
        | Ok trees ->
            Assert.Empty(trees)
        | Error err -> Assert.Fail($"Expected Ok but got Error: {err.Message}")
    
    // ========================================================================
    // TREE INSPECTION
    // ========================================================================
    
    [<Fact>]
    let ``Leaves extracts all anyons in order`` () =
        let sigma = FusionTree.leaf AnyonSpecies.Particle.Sigma
        let psi = FusionTree.leaf AnyonSpecies.Particle.Psi
        
        let tree = FusionTree.fuse sigma psi AnyonSpecies.Particle.Sigma
        let leafList = FusionTree.leaves tree
        
        Assert.Equal(2, leafList.Length)
        Assert.Equal(AnyonSpecies.Particle.Sigma, leafList.[0])
        Assert.Equal(AnyonSpecies.Particle.Psi, leafList.[1])
    
    [<Fact>]
    let ``Tree depth measures nesting level`` () =
        let sigma = FusionTree.leaf AnyonSpecies.Particle.Sigma
        
        // Single leaf has depth 0
        Assert.Equal(0, FusionTree.depth sigma)
        
        // One fusion has depth 1
        let oneFusion = FusionTree.fuse sigma sigma AnyonSpecies.Particle.Vacuum
        Assert.Equal(1, FusionTree.depth oneFusion)
        
        // Nested fusion has depth 2
        let nested = FusionTree.fuse oneFusion oneFusion AnyonSpecies.Particle.Vacuum
        Assert.Equal(2, FusionTree.depth nested)
    
    // ========================================================================
    // TREE EQUALITY
    // ========================================================================
    
    [<Fact>]
    let ``Identical trees are equal`` () =
        let sigma = FusionTree.leaf AnyonSpecies.Particle.Sigma
        let tree1 = FusionTree.fuse sigma sigma AnyonSpecies.Particle.Vacuum
        let tree2 = FusionTree.fuse sigma sigma AnyonSpecies.Particle.Vacuum
        
        Assert.True(FusionTree.equals tree1 tree2)
    
    [<Fact>]
    let ``Trees with different channels are not equal`` () =
        let sigma = FusionTree.leaf AnyonSpecies.Particle.Sigma
        
        // σ × σ → 1
        let tree1 = FusionTree.fuse sigma sigma AnyonSpecies.Particle.Vacuum
        
        // σ × σ → ψ
        let tree2 = FusionTree.fuse sigma sigma AnyonSpecies.Particle.Psi
        
        Assert.False(FusionTree.equals tree1 tree2)
    
    [<Fact>]
    let ``Trees with different structure are not equal`` () =
        let sigma = FusionTree.leaf AnyonSpecies.Particle.Sigma
        
        // ((σ × σ → 1) × (σ × σ → 1)) → 1
        let left1 = FusionTree.fuse sigma sigma AnyonSpecies.Particle.Vacuum
        let right1 = FusionTree.fuse sigma sigma AnyonSpecies.Particle.Vacuum
        let tree1 = FusionTree.fuse left1 right1 AnyonSpecies.Particle.Vacuum
        
        // ((σ × σ → ψ) × (σ × σ → ψ)) → 1
        let left2 = FusionTree.fuse sigma sigma AnyonSpecies.Particle.Psi
        let right2 = FusionTree.fuse sigma sigma AnyonSpecies.Particle.Psi
        let tree2 = FusionTree.fuse left2 right2 AnyonSpecies.Particle.Vacuum
        
        // Same total charge but different internal structure
        Assert.Equal(
            FusionTree.totalCharge tree1 AnyonSpecies.AnyonType.Ising,
            FusionTree.totalCharge tree2 AnyonSpecies.AnyonType.Ising
        )
        Assert.False(FusionTree.equals tree1 tree2)
    
    // ========================================================================
    // PRETTY PRINTING
    // ========================================================================
    
    [<Fact>]
    let ``Tree can be converted to readable string`` () =
        let sigma = FusionTree.leaf AnyonSpecies.Particle.Sigma
        let tree = FusionTree.fuse sigma sigma AnyonSpecies.Particle.Vacuum
        
        let str = FusionTree.toString tree
        
        // Should contain particle names and fusion arrow
        Assert.Contains("Sigma", str)
        Assert.Contains("→", str)
        Assert.Contains("Vacuum", str)
    
    [<Fact>]
    let ``State display shows all relevant information`` () =
        let sigma = FusionTree.leaf AnyonSpecies.Particle.Sigma
        let tree = FusionTree.fuse sigma sigma AnyonSpecies.Particle.Vacuum
        let state = FusionTree.create tree AnyonSpecies.AnyonType.Ising
        
        let display = FusionTree.display state
        
        // Should show tree structure, total charge, size, and theory
        Assert.Contains("Tree:", display)
        Assert.Contains("Total Charge:", display)
        Assert.Contains("Anyons:", display)
        Assert.Contains("Theory:", display)
