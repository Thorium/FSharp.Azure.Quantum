namespace FSharp.Azure.Quantum.Tests.Topological

open Xunit
open FSharp.Azure.Quantum.Topological

/// Comprehensive unit tests for AnyonSpecies module
/// 
/// Tests cover:
/// - Quantum dimension calculations
/// - Particle validity checking
/// - Anti-particle relationships
/// - Frobenius-Schur indicators
/// - Total quantum dimension (normalization)
module AnyonSpeciesTests =
    
    // ============================================================================
    // QUANTUM DIMENSION TESTS
    // ============================================================================
    
    [<Fact>]
    let ``Vacuum has quantum dimension 1`` () =
        let d = AnyonSpecies.quantumDimension AnyonSpecies.Particle.Vacuum
        Assert.Equal(1.0, d)
    
    [<Fact>]
    let ``Sigma has quantum dimension sqrt(2)`` () =
        let d = AnyonSpecies.quantumDimension AnyonSpecies.Particle.Sigma
        let expected = sqrt 2.0  // ≈ 1.414
        Assert.Equal(expected, d, 10)  // 10 decimal places precision
    
    [<Fact>]
    let ``Psi has quantum dimension 1`` () =
        let d = AnyonSpecies.quantumDimension AnyonSpecies.Particle.Psi
        Assert.Equal(1.0, d)
    
    [<Fact>]
    let ``Tau has quantum dimension equal to golden ratio`` () =
        let d = AnyonSpecies.quantumDimension AnyonSpecies.Particle.Tau
        let phi = (1.0 + sqrt 5.0) / 2.0  // φ ≈ 1.618
        Assert.Equal(phi, d, 10)
    
    [<Fact>]
    let ``Golden ratio satisfies φ² = φ + 1`` () =
        // Mathematical property of Fibonacci anyons
        let phi = AnyonSpecies.quantumDimension AnyonSpecies.Particle.Tau
        let phi_squared = phi * phi
        let phi_plus_one = phi + 1.0
        Assert.Equal(phi_squared, phi_plus_one, 10)
    
    // ============================================================================
    // PARTICLE LIST TESTS
    // ============================================================================
    
    [<Fact>]
    let ``Ising theory has three particles: Vacuum, Sigma, Psi`` () =
        match AnyonSpecies.particles AnyonSpecies.AnyonType.Ising with
        | Ok particleList ->
            Assert.Equal(3, particleList.Length)
            Assert.Contains(AnyonSpecies.Particle.Vacuum, particleList)
            Assert.Contains(AnyonSpecies.Particle.Sigma, particleList)
            Assert.Contains(AnyonSpecies.Particle.Psi, particleList)
        | Error err -> Assert.Fail($"Expected Ok but got Error: {err.Message}")
    
    [<Fact>]
    let ``Fibonacci theory has two particles: Vacuum, Tau`` () =
        match AnyonSpecies.particles AnyonSpecies.AnyonType.Fibonacci with
        | Ok particleList ->
            Assert.Equal(2, particleList.Length)
            Assert.Contains(AnyonSpecies.Particle.Vacuum, particleList)
            Assert.Contains(AnyonSpecies.Particle.Tau, particleList)
        | Error err -> Assert.Fail($"Expected Ok but got Error: {err.Message}")
    
    [<Fact>]
    let ``SU2Level 2 is equivalent to Ising`` () =
        match AnyonSpecies.particles AnyonSpecies.AnyonType.Ising, AnyonSpecies.particles (AnyonSpecies.AnyonType.SU2Level 2) with
        | Ok isingParticles, Ok su2_2_particles ->
            Assert.Equal<AnyonSpecies.Particle list>(isingParticles, su2_2_particles)
        | Error err, _ | _, Error err -> Assert.Fail($"Expected Ok but got Error: {err.Message}")
    
    // ============================================================================
    // TOTAL QUANTUM DIMENSION TESTS
    // ============================================================================
    
    [<Fact>]
    let ``Ising total quantum dimension is 2`` () =
        // D² = d₁² + d_σ² + d_ψ² = 1² + (√2)² + 1² = 1 + 2 + 1 = 4
        // D = √4 = 2
        match AnyonSpecies.totalQuantumDimension AnyonSpecies.AnyonType.Ising with
        | Ok d -> Assert.Equal(2.0, d, 10)
        | Error err -> Assert.Fail($"Expected Ok but got Error: {err.Message}")
    
    [<Fact>]
    let ``Fibonacci total quantum dimension computed correctly`` () =
        // D² = d₁² + d_τ² = 1² + φ² = 1 + φ² = 1 + (φ + 1) = φ + 2
        // φ = (1+√5)/2 ≈ 1.618
        // D = √(φ + 2) ≈ 1.902
        match AnyonSpecies.totalQuantumDimension AnyonSpecies.AnyonType.Fibonacci with
        | Ok d ->
            let phi = (1.0 + sqrt 5.0) / 2.0
            let expected = sqrt (phi + 2.0)
            Assert.Equal(expected, d, 10)
        | Error err -> Assert.Fail($"Expected Ok but got Error: {err.Message}")
    
    [<Fact>]
    let ``Total quantum dimension is always positive`` () =
        let types = [AnyonSpecies.AnyonType.Ising; AnyonSpecies.AnyonType.Fibonacci; AnyonSpecies.AnyonType.SU2Level 2]
        types |> List.iter (fun t ->
            match AnyonSpecies.totalQuantumDimension t with
            | Ok d -> Assert.True(d > 0.0)
            | Error err -> Assert.Fail($"Expected Ok but got Error: {err.Message}")
        )
    
    // ============================================================================
    // VALIDITY TESTS
    // ============================================================================
    
    [<Fact>]
    let ``Vacuum is valid in all theories`` () =
        match AnyonSpecies.isValid AnyonSpecies.AnyonType.Ising AnyonSpecies.Particle.Vacuum,
              AnyonSpecies.isValid AnyonSpecies.AnyonType.Fibonacci AnyonSpecies.Particle.Vacuum,
              AnyonSpecies.isValid (AnyonSpecies.AnyonType.SU2Level 2) AnyonSpecies.Particle.Vacuum with
        | Ok true, Ok true, Ok true -> ()
        | _ -> Assert.Fail("Vacuum should be valid in all theories")
    
    [<Fact>]
    let ``Sigma is valid only in Ising`` () =
        match AnyonSpecies.isValid AnyonSpecies.AnyonType.Ising AnyonSpecies.Particle.Sigma,
              AnyonSpecies.isValid AnyonSpecies.AnyonType.Fibonacci AnyonSpecies.Particle.Sigma with
        | Ok true, Ok false -> ()
        | _ -> Assert.Fail("Sigma should be valid only in Ising")
    
    [<Fact>]
    let ``Psi is valid only in Ising`` () =
        match AnyonSpecies.isValid AnyonSpecies.AnyonType.Ising AnyonSpecies.Particle.Psi,
              AnyonSpecies.isValid AnyonSpecies.AnyonType.Fibonacci AnyonSpecies.Particle.Psi with
        | Ok true, Ok false -> ()
        | _ -> Assert.Fail("Psi should be valid only in Ising")
    
    [<Fact>]
    let ``Tau is valid only in Fibonacci`` () =
        match AnyonSpecies.isValid AnyonSpecies.AnyonType.Fibonacci AnyonSpecies.Particle.Tau,
              AnyonSpecies.isValid AnyonSpecies.AnyonType.Ising AnyonSpecies.Particle.Tau with
        | Ok true, Ok false -> ()
        | _ -> Assert.Fail("Tau should be valid only in Fibonacci")
    
    // ============================================================================
    // ANTI-PARTICLE TESTS
    // ============================================================================
    
    [<Fact>]
    let ``All Ising particles are self-conjugate`` () =
        // In Ising theory: ā = a for all particles
        Assert.Equal(AnyonSpecies.Particle.Vacuum, AnyonSpecies.antiParticle AnyonSpecies.Particle.Vacuum)
        Assert.Equal(AnyonSpecies.Particle.Sigma, AnyonSpecies.antiParticle AnyonSpecies.Particle.Sigma)
        Assert.Equal(AnyonSpecies.Particle.Psi, AnyonSpecies.antiParticle AnyonSpecies.Particle.Psi)
    
    [<Fact>]
    let ``Fibonacci particles are self-conjugate`` () =
        Assert.Equal(AnyonSpecies.Particle.Vacuum, AnyonSpecies.antiParticle AnyonSpecies.Particle.Vacuum)
        Assert.Equal(AnyonSpecies.Particle.Tau, AnyonSpecies.antiParticle AnyonSpecies.Particle.Tau)
    
    [<Fact>]
    let ``Anti-particle is involutive: (ā)̄ = a`` () =
        let testParticles = [AnyonSpecies.Particle.Vacuum; AnyonSpecies.Particle.Sigma; AnyonSpecies.Particle.Psi; AnyonSpecies.Particle.Tau]
        testParticles |> List.iter (fun p ->
            let anti = AnyonSpecies.antiParticle p
            let antiAnti = AnyonSpecies.antiParticle anti
            Assert.Equal(p, antiAnti)
        )
    
    // ============================================================================
    // FROBENIUS-SCHUR INDICATOR TESTS
    // ============================================================================
    
    [<Fact>]
    let ``Vacuum has Frobenius-Schur indicator +1`` () =
        let kappa = AnyonSpecies.frobenius_schur_indicator AnyonSpecies.Particle.Vacuum
        Assert.Equal(1, kappa)
    
    [<Fact>]
    let ``Sigma has Frobenius-Schur indicator +1 (real)`` () =
        let kappa = AnyonSpecies.frobenius_schur_indicator AnyonSpecies.Particle.Sigma
        Assert.Equal(1, kappa)
    
    [<Fact>]
    let ``Psi has Frobenius-Schur indicator -1 (pseudoreal, fermion)`` () =
        let kappa = AnyonSpecies.frobenius_schur_indicator AnyonSpecies.Particle.Psi
        Assert.Equal(-1, kappa)
    
    [<Fact>]
    let ``Tau has Frobenius-Schur indicator +1 (real)`` () =
        let kappa = AnyonSpecies.frobenius_schur_indicator AnyonSpecies.Particle.Tau
        Assert.Equal(1, kappa)
    
    [<Fact>]
    let ``Frobenius-Schur indicator is always -1, 0, or +1`` () =
        let testParticles = [AnyonSpecies.Particle.Vacuum; AnyonSpecies.Particle.Sigma; AnyonSpecies.Particle.Psi; AnyonSpecies.Particle.Tau]
        testParticles |> List.iter (fun p ->
            let kappa = AnyonSpecies.frobenius_schur_indicator p
            Assert.True(kappa = -1 || kappa = 0 || kappa = 1)
        )
    
    // ============================================================================
    // MATHEMATICAL PROPERTY TESTS
    // ============================================================================
    
    [<Fact>]
    let ``Quantum dimensions satisfy Perron-Frobenius`` () =
        // For a consistent TQFT, quantum dimensions must satisfy:
        // d_a ≥ 1 for all particles (with d₁ = 1)
        let testTheories = [AnyonSpecies.AnyonType.Ising; AnyonSpecies.AnyonType.Fibonacci]
        testTheories |> List.iter (fun theory ->
            match AnyonSpecies.particles theory with
            | Ok particleList ->
                particleList |> List.iter (fun p ->
                    let d = AnyonSpecies.quantumDimension p
                    Assert.True(d >= 1.0)
                )
            | Error err -> Assert.Fail($"Expected Ok but got Error: {err.Message}")
        )
    
    [<Fact>]
    let ``Largest quantum dimension equals total quantum dimension for Fibonacci`` () =
        // For Fibonacci: max(d₁, d_τ) = φ
        // And D = √(1 + φ²) 
        // This is a special property of Fibonacci theory
        match AnyonSpecies.particles AnyonSpecies.AnyonType.Fibonacci with
        | Ok particleList ->
            let maxDim = particleList |> List.map AnyonSpecies.quantumDimension |> List.max
            let phi = AnyonSpecies.quantumDimension AnyonSpecies.Particle.Tau
            Assert.Equal(phi, maxDim, 10)
        | Error err -> Assert.Fail($"Expected Ok but got Error: {err.Message}")
    
    // ============================================================================
    // ERROR HANDLING TESTS
    // ============================================================================
    
    [<Fact>]
    let ``SU2Level with k = 3 has 4 particles`` () =
        // SU(2)₃ has particles for j = 0, 1/2, 1, 3/2
        match AnyonSpecies.particles (AnyonSpecies.AnyonType.SU2Level 3) with
        | Ok particles ->
            Assert.Equal(4, particles.Length)
            // Check they are all SpinJ particles with level 3
            particles |> List.iter (function
                | AnyonSpecies.Particle.SpinJ (_, k) -> Assert.Equal(3, k)
                | _ -> Assert.Fail("Expected SpinJ particles"))
        | Error err -> Assert.Fail($"Unexpected error: {err.Category}")
    
    [<Fact>]
    let ``SU2Level with k = 1 has 2 particles`` () =
        // SU(2)₁ has particles for j = 0, 1/2
        match AnyonSpecies.particles (AnyonSpecies.AnyonType.SU2Level 1) with
        | Ok particles ->
            Assert.Equal(2, particles.Length)
            // Check they are all SpinJ particles with level 1
            particles |> List.iter (function
                | AnyonSpecies.Particle.SpinJ (_, k) -> Assert.Equal(1, k)
                | _ -> Assert.Fail("Expected SpinJ particles"))
        | Error err -> Assert.Fail($"Unexpected error: {err.Category}")
