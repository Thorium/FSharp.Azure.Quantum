namespace FSharp.Azure.Quantum.Tests.Topological

open Xunit
open FSharp.Azure.Quantum.Topological

/// Comprehensive unit tests for FusionRules module
/// 
/// Tests cover:
/// - Ising fusion rules (σ × σ = 1 + ψ, etc.)
/// - Fibonacci fusion rules (τ × τ = 1 + τ)
/// - Fusion algebra axioms (identity, commutativity, anti-particle)
/// - Multiplicities and fusion channels
module FusionRulesTests =
    
    // ============================================================================
    // HELPER FUNCTIONS
    // ============================================================================
    
    /// Assert that fusion produces expected particles (order-independent)
    let assertFusionEquals (expected: AnyonSpecies.Particle list) (actualResult: TopologicalResult<FusionRules.Outcome list>) =
        match actualResult with
        | Ok actual ->
            let actualParticles = actual |> List.map (fun o -> o.Result) |> List.sort
            let expectedSorted = expected |> List.sort
            Assert.Equal<AnyonSpecies.Particle list>(expectedSorted, actualParticles)
        | Error err -> Assert.Fail($"Expected Ok but got Error: {err.Message}")
    
    /// Assert that all outcomes have multiplicity 1 (multiplicity-free theory)
    let assertMultiplicityOne (outcomesResult: TopologicalResult<FusionRules.Outcome list>) =
        match outcomesResult with
        | Ok outcomes ->
            outcomes |> List.iter (fun o ->
                Assert.Equal(1, o.Multiplicity)
            )
        | Error err -> Assert.Fail($"Expected Ok but got Error: {err.Message}")
    
    // ============================================================================
    // ISING FUSION RULES TESTS
    // ============================================================================
    
    [<Fact>]
    let ``Ising: Vacuum × Vacuum = Vacuum (identity)`` () =
        let outcomes = FusionRules.fuse AnyonSpecies.Particle.Vacuum AnyonSpecies.Particle.Vacuum AnyonSpecies.AnyonType.Ising
        assertFusionEquals [AnyonSpecies.Particle.Vacuum] outcomes
        assertMultiplicityOne outcomes
    
    [<Fact>]
    let ``Ising: Vacuum × Sigma = Sigma (identity)`` () =
        let outcomes = FusionRules.fuse AnyonSpecies.Particle.Vacuum AnyonSpecies.Particle.Sigma AnyonSpecies.AnyonType.Ising
        assertFusionEquals [AnyonSpecies.Particle.Sigma] outcomes
        assertMultiplicityOne outcomes
    
    [<Fact>]
    let ``Ising: Sigma × Vacuum = Sigma (commutativity check)`` () =
        let outcomes = FusionRules.fuse AnyonSpecies.Particle.Sigma AnyonSpecies.Particle.Vacuum AnyonSpecies.AnyonType.Ising
        assertFusionEquals [AnyonSpecies.Particle.Sigma] outcomes
    
    [<Fact>]
    let ``Ising: Vacuum × Psi = Psi (identity)`` () =
        let outcomes = FusionRules.fuse AnyonSpecies.Particle.Vacuum AnyonSpecies.Particle.Psi AnyonSpecies.AnyonType.Ising
        assertFusionEquals [AnyonSpecies.Particle.Psi] outcomes
    
    [<Fact>]
    let ``Ising: Sigma × Sigma = Vacuum + Psi (KEY NON-ABELIAN RULE)`` () =
        // This is the defining fusion rule of Ising anyons!
        // σ × σ = 1 + ψ creates a 2-dimensional Hilbert space
        let outcomes = FusionRules.fuse AnyonSpecies.Particle.Sigma AnyonSpecies.Particle.Sigma AnyonSpecies.AnyonType.Ising
        assertFusionEquals [AnyonSpecies.Particle.Vacuum; AnyonSpecies.Particle.Psi] outcomes
        assertMultiplicityOne outcomes
        match outcomes with
        | Ok list -> Assert.Equal(2, list.Length)  // Two possible outcomes
        | Error err -> Assert.Fail($"Expected Ok but got Error: {err.Message}")
    
    [<Fact>]
    let ``Ising: Sigma × Psi = Sigma`` () =
        let outcomes = FusionRules.fuse AnyonSpecies.Particle.Sigma AnyonSpecies.Particle.Psi AnyonSpecies.AnyonType.Ising
        assertFusionEquals [AnyonSpecies.Particle.Sigma] outcomes
        assertMultiplicityOne outcomes
    
    [<Fact>]
    let ``Ising: Psi × Sigma = Sigma (commutativity)`` () =
        let outcomes = FusionRules.fuse AnyonSpecies.Particle.Psi AnyonSpecies.Particle.Sigma AnyonSpecies.AnyonType.Ising
        assertFusionEquals [AnyonSpecies.Particle.Sigma] outcomes
    
    [<Fact>]
    let ``Ising: Psi × Psi = Vacuum (fermion pair annihilates)`` () =
        let outcomes = FusionRules.fuse AnyonSpecies.Particle.Psi AnyonSpecies.Particle.Psi AnyonSpecies.AnyonType.Ising
        assertFusionEquals [AnyonSpecies.Particle.Vacuum] outcomes
        assertMultiplicityOne outcomes
    
    // ============================================================================
    // FIBONACCI FUSION RULES TESTS
    // ============================================================================
    
    [<Fact>]
    let ``Fibonacci: Vacuum × Vacuum = Vacuum`` () =
        let outcomes = FusionRules.fuse AnyonSpecies.Particle.Vacuum AnyonSpecies.Particle.Vacuum AnyonSpecies.AnyonType.Fibonacci
        assertFusionEquals [AnyonSpecies.Particle.Vacuum] outcomes
        assertMultiplicityOne outcomes
    
    [<Fact>]
    let ``Fibonacci: Vacuum × Tau = Tau (identity)`` () =
        let outcomes = FusionRules.fuse AnyonSpecies.Particle.Vacuum AnyonSpecies.Particle.Tau AnyonSpecies.AnyonType.Fibonacci
        assertFusionEquals [AnyonSpecies.Particle.Tau] outcomes
        assertMultiplicityOne outcomes
    
    [<Fact>]
    let ``Fibonacci: Tau × Vacuum = Tau (commutativity)`` () =
        let outcomes = FusionRules.fuse AnyonSpecies.Particle.Tau AnyonSpecies.Particle.Vacuum AnyonSpecies.AnyonType.Fibonacci
        assertFusionEquals [AnyonSpecies.Particle.Tau] outcomes
    
    [<Fact>]
    let ``Fibonacci: Tau × Tau = Vacuum + Tau (FIBONACCI RULE)`` () =
        // τ × τ = 1 + τ (Fibonacci recurrence relation!)
        // This creates Fibonacci-number-dimensional Hilbert spaces
        let outcomes = FusionRules.fuse AnyonSpecies.Particle.Tau AnyonSpecies.Particle.Tau AnyonSpecies.AnyonType.Fibonacci
        assertFusionEquals [AnyonSpecies.Particle.Vacuum; AnyonSpecies.Particle.Tau] outcomes
        assertMultiplicityOne outcomes
        match outcomes with
        | Ok list -> Assert.Equal(2, list.Length)
        | Error err -> Assert.Fail($"Expected Ok but got Error: {err.Message}")
    
    // ============================================================================
    // FUSION MULTIPLICITY TESTS
    // ============================================================================
    
    [<Fact>]
    let ``Fusion multiplicity: Ising Sigma × Sigma → Vacuum is 1`` () =
        match FusionRules.multiplicity AnyonSpecies.Particle.Sigma AnyonSpecies.Particle.Sigma AnyonSpecies.Particle.Vacuum AnyonSpecies.AnyonType.Ising with
        | Ok n -> Assert.Equal(1, n)
        | Error err -> Assert.Fail($"Expected Ok but got Error: {err.Message}")
    
    [<Fact>]
    let ``Fusion multiplicity: Ising Sigma × Sigma → Psi is 1`` () =
        match FusionRules.multiplicity AnyonSpecies.Particle.Sigma AnyonSpecies.Particle.Sigma AnyonSpecies.Particle.Psi AnyonSpecies.AnyonType.Ising with
        | Ok n -> Assert.Equal(1, n)
        | Error err -> Assert.Fail($"Expected Ok but got Error: {err.Message}")
    
    [<Fact>]
    let ``Fusion multiplicity: Ising Sigma × Sigma → Sigma is 0`` () =
        // σ × σ does NOT contain σ in its fusion
        match FusionRules.multiplicity AnyonSpecies.Particle.Sigma AnyonSpecies.Particle.Sigma AnyonSpecies.Particle.Sigma AnyonSpecies.AnyonType.Ising with
        | Ok n -> Assert.Equal(0, n)
        | Error err -> Assert.Fail($"Expected Ok but got Error: {err.Message}")
    
    [<Fact>]
    let ``Fusion multiplicity: Fibonacci Tau × Tau → Tau is 1`` () =
        match FusionRules.multiplicity AnyonSpecies.Particle.Tau AnyonSpecies.Particle.Tau AnyonSpecies.Particle.Tau AnyonSpecies.AnyonType.Fibonacci with
        | Ok n -> Assert.Equal(1, n)
        | Error err -> Assert.Fail($"Expected Ok but got Error: {err.Message}")
    
    [<Fact>]
    let ``Fusion multiplicity is symmetric: N^c_ab = N^c_ba`` () =
        // Test commutativity via multiplicities
        let isingPairs = 
            [(AnyonSpecies.Particle.Vacuum, AnyonSpecies.Particle.Sigma); 
             (AnyonSpecies.Particle.Sigma, AnyonSpecies.Particle.Psi); 
             (AnyonSpecies.Particle.Psi, AnyonSpecies.Particle.Psi); 
             (AnyonSpecies.Particle.Sigma, AnyonSpecies.Particle.Sigma)]
        
        isingPairs |> List.iter (fun (a, b) ->
            [AnyonSpecies.Particle.Vacuum; AnyonSpecies.Particle.Sigma; AnyonSpecies.Particle.Psi] |> List.iter (fun c ->
                match FusionRules.multiplicity a b c AnyonSpecies.AnyonType.Ising,
                      FusionRules.multiplicity b a c AnyonSpecies.AnyonType.Ising with
                | Ok nab, Ok nba -> Assert.Equal(nab, nba)
                | Error err, _ | _, Error err -> Assert.Fail($"Expected Ok but got Error: {err.Message}")
            )
        )
    
    // ============================================================================
    // CAN FUSE TESTS
    // ============================================================================
    
    [<Fact>]
    let ``Can fuse: Sigma × Sigma → Vacuum is true`` () =
        match FusionRules.isPossible AnyonSpecies.Particle.Sigma AnyonSpecies.Particle.Sigma AnyonSpecies.Particle.Vacuum AnyonSpecies.AnyonType.Ising with
        | Ok true -> ()
        | _ -> Assert.Fail("Expected true")
    
    [<Fact>]
    let ``Can fuse: Sigma × Sigma → Psi is true`` () =
        match FusionRules.isPossible AnyonSpecies.Particle.Sigma AnyonSpecies.Particle.Sigma AnyonSpecies.Particle.Psi AnyonSpecies.AnyonType.Ising with
        | Ok true -> ()
        | _ -> Assert.Fail("Expected true")
    
    [<Fact>]
    let ``Can fuse: Sigma × Sigma → Sigma is false`` () =
        match FusionRules.isPossible AnyonSpecies.Particle.Sigma AnyonSpecies.Particle.Sigma AnyonSpecies.Particle.Sigma AnyonSpecies.AnyonType.Ising with
        | Ok false -> ()
        | _ -> Assert.Fail("Expected false")
    
    [<Fact>]
    let ``Can fuse: Psi × Psi → Psi is false`` () =
        // ψ × ψ = 1 (deterministic, does NOT give ψ)
        match FusionRules.isPossible AnyonSpecies.Particle.Psi AnyonSpecies.Particle.Psi AnyonSpecies.Particle.Psi AnyonSpecies.AnyonType.Ising with
        | Ok false -> ()
        | _ -> Assert.Fail("Expected false")
    
    // ============================================================================
    // FUSION CHANNELS TESTS
    // ============================================================================
    
    [<Fact>]
    let ``Fusion channels returns particles without multiplicities`` () =
        match FusionRules.channels AnyonSpecies.Particle.Sigma AnyonSpecies.Particle.Sigma AnyonSpecies.AnyonType.Ising with
        | Ok channels ->
            Assert.Equal(2, channels.Length)
            Assert.Contains(AnyonSpecies.Particle.Vacuum, channels)
            Assert.Contains(AnyonSpecies.Particle.Psi, channels)
        | Error err -> Assert.Fail($"Expected Ok but got Error: {err.Message}")
    
    [<Fact>]
    let ``Fusion channels for deterministic fusion returns single element`` () =
        match FusionRules.channels AnyonSpecies.Particle.Psi AnyonSpecies.Particle.Psi AnyonSpecies.AnyonType.Ising with
        | Ok channels ->
            Assert.Single(channels) |> ignore
            Assert.Equal(AnyonSpecies.Particle.Vacuum, channels.[0])
        | Error err -> Assert.Fail($"Expected Ok but got Error: {err.Message}")
    
    // ============================================================================
    // FUSION ALGEBRA AXIOM TESTS
    // ============================================================================
    
    [<Fact>]
    let ``Ising fusion algebra satisfies all axioms`` () =
        match FusionRules.verifyAlgebra AnyonSpecies.AnyonType.Ising with
        | Ok () -> Assert.True(true)  // All axioms satisfied
        | Error err -> Assert.Fail(err.Message)
    
    [<Fact>]
    let ``Fibonacci fusion algebra satisfies all axioms`` () =
        match FusionRules.verifyAlgebra AnyonSpecies.AnyonType.Fibonacci with
        | Ok () -> Assert.True(true)
        | Error err -> Assert.Fail(err.Message)
    
    [<Fact>]
    let ``Identity axiom: 1 × a = a for all Ising particles`` () =
        let particles = [AnyonSpecies.Particle.Vacuum; AnyonSpecies.Particle.Sigma; AnyonSpecies.Particle.Psi]
        particles |> List.iter (fun a ->
            match FusionRules.channels AnyonSpecies.Particle.Vacuum a AnyonSpecies.AnyonType.Ising with
            | Ok channels -> Assert.Equal<AnyonSpecies.Particle list>([a], channels)
            | Error err -> Assert.Fail($"Expected Ok but got Error: {err.Message}")
        )
    
    [<Fact>]
    let ``Commutativity: a × b = b × a for all Ising pairs`` () =
        let particles = [AnyonSpecies.Particle.Vacuum; AnyonSpecies.Particle.Sigma; AnyonSpecies.Particle.Psi]
        particles |> List.allPairs particles |> List.iter (fun (a, b) ->
            match FusionRules.channels a b AnyonSpecies.AnyonType.Ising,
                  FusionRules.channels b a AnyonSpecies.AnyonType.Ising with
            | Ok ab, Ok ba ->
                let abSet = Set.ofList ab
                let baSet = Set.ofList ba
                Assert.Equal<Set<AnyonSpecies.Particle>>(abSet, baSet)
            | Error err, _ | _, Error err -> Assert.Fail($"Expected Ok but got Error: {err.Message}")
        )
    
    [<Fact>]
    let ``Anti-particle axiom: a × ā contains Vacuum for all particles`` () =
        let testParticles = [AnyonSpecies.Particle.Vacuum; AnyonSpecies.Particle.Sigma; AnyonSpecies.Particle.Psi; AnyonSpecies.Particle.Tau]
        testParticles |> List.iter (fun a ->
            let anyonType = 
                match a with
                | AnyonSpecies.Particle.Tau -> AnyonSpecies.AnyonType.Fibonacci
                | _ -> AnyonSpecies.AnyonType.Ising
            
            match AnyonSpecies.isValid anyonType a with
            | Ok true ->
                let anti = AnyonSpecies.antiParticle a
                match FusionRules.isPossible a anti AnyonSpecies.Particle.Vacuum anyonType with
                | Ok true -> ()
                | _ -> Assert.Fail($"Expected a × ā to contain Vacuum for particle {a}")
            | _ -> ()  // Skip invalid particles
        )
    
    // ============================================================================
    // FUSION TENSOR TESTS
    // ============================================================================
    
    [<Fact>]
    let ``Fusion tensor has correct dimensions for Ising`` () =
        match FusionRules.tensor AnyonSpecies.AnyonType.Ising with
        | Ok n ->
            // Ising has 3 particles → 3×3×3 tensor
            let (d1, d2, d3) = (Array3D.length1 n, Array3D.length2 n, Array3D.length3 n)
            Assert.Equal(3, d1)
            Assert.Equal(3, d2)
            Assert.Equal(3, d3)
        | Error err -> Assert.Fail($"Expected Ok but got Error: {err.Message}")
    
    [<Fact>]
    let ``Fusion tensor has correct dimensions for Fibonacci`` () =
        match FusionRules.tensor AnyonSpecies.AnyonType.Fibonacci with
        | Ok n ->
            // Fibonacci has 2 particles → 2×2×2 tensor
            let (d1, d2, d3) = (Array3D.length1 n, Array3D.length2 n, Array3D.length3 n)
            Assert.Equal(2, d1)
            Assert.Equal(2, d2)
            Assert.Equal(2, d3)
        | Error err -> Assert.Fail($"Expected Ok but got Error: {err.Message}")
    
    [<Fact>]
    let ``Fusion tensor entries are non-negative`` () =
        match FusionRules.tensor AnyonSpecies.AnyonType.Ising with
        | Ok n ->
            for i in 0 .. Array3D.length1 n - 1 do
                for j in 0 .. Array3D.length2 n - 1 do
                    for k in 0 .. Array3D.length3 n - 1 do
                        Assert.True(n.[i,j,k] >= 0)
        | Error err -> Assert.Fail($"Expected Ok but got Error: {err.Message}")
    
    [<Fact>]
    let ``Fusion tensor is symmetric: N^c_ab = N^c_ba`` () =
        match FusionRules.tensor AnyonSpecies.AnyonType.Ising with
        | Ok n ->
            for i in 0 .. Array3D.length1 n - 1 do
                for j in 0 .. Array3D.length2 n - 1 do
                    for k in 0 .. Array3D.length3 n - 1 do
                        Assert.Equal(n.[i,j,k], n.[j,i,k])
        | Error err -> Assert.Fail($"Expected Ok but got Error: {err.Message}")
    
    // ============================================================================
    // ERROR HANDLING TESTS
    // ============================================================================
    
    [<Fact>]
    let ``Fusing Ising and Fibonacci particles returns ValidationError`` () =
        match FusionRules.fuse AnyonSpecies.Particle.Sigma AnyonSpecies.Particle.Tau AnyonSpecies.AnyonType.Ising with
        | Error (TopologicalError.ValidationError _) -> ()  // Expected
        | Ok _ -> Assert.Fail("Expected ValidationError when fusing incompatible particles")
        | Error err -> Assert.Fail($"Expected ValidationError but got {err.Category}")
    
    [<Fact>]
    let ``Fusing Tau in Ising theory returns ValidationError`` () =
        match FusionRules.fuse AnyonSpecies.Particle.Tau AnyonSpecies.Particle.Vacuum AnyonSpecies.AnyonType.Ising with
        | Error (TopologicalError.ValidationError _) -> ()  // Expected
        | Ok _ -> Assert.Fail("Expected ValidationError for Tau in Ising theory")
        | Error err -> Assert.Fail($"Expected ValidationError but got {err.Category}")
    
    [<Fact>]
    let ``Fusing Sigma in Fibonacci theory returns ValidationError`` () =
        match FusionRules.fuse AnyonSpecies.Particle.Sigma AnyonSpecies.Particle.Vacuum AnyonSpecies.AnyonType.Fibonacci with
        | Error (TopologicalError.ValidationError _) -> ()  // Expected
        | Ok _ -> Assert.Fail("Expected ValidationError for Sigma in Fibonacci theory")
        | Error err -> Assert.Fail($"Expected ValidationError but got {err.Category}")
    
    [<Fact>]
    let ``Fusing invalid particle returns ValidationError`` () =
        match FusionRules.fuse AnyonSpecies.Particle.Psi AnyonSpecies.Particle.Psi AnyonSpecies.AnyonType.Fibonacci with
        | Error (TopologicalError.ValidationError _) -> ()  // Expected
        | Ok _ -> Assert.Fail("Expected ValidationError for Psi in Fibonacci theory")
        | Error err -> Assert.Fail($"Expected ValidationError but got {err.Category}")
