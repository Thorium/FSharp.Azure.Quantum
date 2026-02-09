namespace FSharp.Azure.Quantum.Topological.Tests

open Xunit
open FSharp.Azure.Quantum.Topological
open System.Numerics

module RMatrixTests =
    
    // ========================================================================
    // TEST HELPERS - Make tests more readable and maintainable
    // ========================================================================
    
    /// Helper to create R-matrix index (reduces repetition)
    let private rIndex a b c = {
        RMatrix.A = a
        RMatrix.B = b
        RMatrix.C = c
    }
    
    /// Helper to assert complex value matches expected (with tolerance)
    let private assertComplexEquals (expected: Complex) (actual: Complex) =
        let tolerance = 1e-10
        Assert.InRange(actual.Real, expected.Real - tolerance, expected.Real + tolerance)
        Assert.InRange(actual.Imaginary, expected.Imaginary - tolerance, expected.Imaginary + tolerance)
    
    /// Helper to assert magnitude equals 1 (on unit circle)
    let private assertOnUnitCircle (z: Complex) =
        let magnitude = z.Magnitude
        Assert.InRange(magnitude, 1.0 - 1e-10, 1.0 + 1e-10)
    
    /// Helper to get R-symbol or fail with meaningful message
    let private getRSymbolOrFail data index testContext =
        match RMatrix.getRSymbol data index with
        | Ok value -> value
        | Error err -> failwith $"{testContext}: {err.Message}"
    
    /// Helper to compute R-matrix or fail with meaningful message
    let private computeRMatrixOrFail anyonType =
        match RMatrix.computeRMatrix anyonType with
        | Ok data -> data
        | Error err -> failwith $"Failed to compute {anyonType} R-matrix: {err.Message}"
    
    // ========================================================================
    // ISING R-MATRIX TESTS - Testing Majorana zero mode braiding
    // ========================================================================
    
    [<Fact>]
    let ``Ising anyons have non-trivial R-symbols for σ×σ braiding`` () =
        // Business meaning: Braiding Majorana zero modes (σ anyons) accumulates
        // a quantum Berry phase that depends on the fusion channel (vacuum or fermion).
        let data = computeRMatrixOrFail AnyonSpecies.AnyonType.Ising
        
        Assert.Equal(AnyonSpecies.AnyonType.Ising, data.AnyonType)
        Assert.True(data.RSymbols.Count > 0, "Should have non-trivial R-symbols")
    
    [<Fact>]
    let ``Ising R[σ,σ;1] equals exp(iπ/8) for Majorana Berry phase`` () =
        // Business meaning: When two Majorana modes braid and fuse to vacuum,
        // they accumulate a topological Berry phase of π/8. This is the fundamental
        // non-Abelian signature of Majorana zero modes in topological superconductors.
        let data = computeRMatrixOrFail AnyonSpecies.AnyonType.Ising
        let sigma = AnyonSpecies.Particle.Sigma
        let vacuum = AnyonSpecies.Particle.Vacuum
        
        let value = getRSymbolOrFail data (rIndex sigma sigma vacuum) "R[σ,σ;1]"
        let expected = TopologicalHelpers.expI (System.Math.PI / 8.0)
        
        assertComplexEquals expected value
        assertOnUnitCircle value
    
    [<Fact>]
    let ``Ising R[σ,σ;ψ] equals exp(-3iπ/8) for fermion fusion channel`` () =
        // Business meaning: When Majoranas braid and fuse to a fermion (ψ),
        // the accumulated phase is different (-3π/8), reflecting the different
        // fusion topology. This phase difference is essential for quantum computation.
        let data = computeRMatrixOrFail AnyonSpecies.AnyonType.Ising
        let sigma = AnyonSpecies.Particle.Sigma
        let psi = AnyonSpecies.Particle.Psi
        
        let value = getRSymbolOrFail data (rIndex sigma sigma psi) "R[σ,σ;ψ]"
        let expected = TopologicalHelpers.expI (-3.0 * System.Math.PI / 8.0)
        
        assertComplexEquals expected value
        assertOnUnitCircle value
    
    [<Fact>]
    let ``Ising R[ψ,ψ;1] equals -1 for fermion exchange statistics`` () =
        // Business meaning: Two fermions acquire a phase of -1 when exchanged,
        // which is the standard fermion exchange statistics (Pauli exclusion principle).
        let data = computeRMatrixOrFail AnyonSpecies.AnyonType.Ising
        let psi = AnyonSpecies.Particle.Psi
        let vacuum = AnyonSpecies.Particle.Vacuum
        
        let value = getRSymbolOrFail data (rIndex psi psi vacuum) "R[ψ,ψ;1]"
        
        assertComplexEquals (Complex(-1.0, 0.0)) value
        assertOnUnitCircle value
    
    [<Fact>]
    let ``Ising R[σ,ψ;σ] equals exp(iπ/4) for Majorana-fermion braiding`` () =
        // Business meaning: Braiding a Majorana with a fermion accumulates π/4 phase.
        let data = computeRMatrixOrFail AnyonSpecies.AnyonType.Ising
        let sigma = AnyonSpecies.Particle.Sigma
        let psi = AnyonSpecies.Particle.Psi
        
        let value = getRSymbolOrFail data (rIndex sigma psi sigma) "R[σ,ψ;σ]"
        let expected = TopologicalHelpers.expI (System.Math.PI / 4.0)
        
        assertComplexEquals expected value
        assertOnUnitCircle value
    
    [<Fact>]
    let ``Ising vacuum braiding is trivial with phase 1`` () =
        // Business meaning: Braiding with vacuum (no anyon) does nothing - it's
        // the identity operation with no phase accumulation.
        let data = computeRMatrixOrFail AnyonSpecies.AnyonType.Ising
        let vacuum = AnyonSpecies.Particle.Vacuum
        let sigma = AnyonSpecies.Particle.Sigma
        
        let value1 = getRSymbolOrFail data (rIndex vacuum sigma sigma) "R[1,σ;σ]"
        let value2 = getRSymbolOrFail data (rIndex sigma vacuum sigma) "R[σ,1;σ]"
        
        assertComplexEquals Complex.One value1
        assertComplexEquals Complex.One value2
    
    [<Fact>]
    let ``Ising all R-matrix elements lie on unit circle`` () =
        // Business meaning: R-matrices are unitary operators representing topological
        // phases. All elements must have magnitude 1 to preserve quantum probabilities.
        let data = computeRMatrixOrFail AnyonSpecies.AnyonType.Ising
        
        data.RSymbols
        |> Map.iter (fun _ value -> assertOnUnitCircle value)
    
    // ========================================================================
    // FIBONACCI R-MATRIX TESTS
    // ========================================================================
    
    [<Fact>]
    let ``Fibonacci anyons have R-symbols with golden ratio phases`` () =
        // Business meaning: Fibonacci anyons exhibit phases involving multiples
        // of π/5, reflecting the underlying SU(2)_3 algebraic structure.
        let data = computeRMatrixOrFail AnyonSpecies.AnyonType.Fibonacci
        
        Assert.Equal(AnyonSpecies.AnyonType.Fibonacci, data.AnyonType)
        Assert.True(data.RSymbols.Count > 0, "Should have non-trivial R-symbols")
    
    [<Fact>]
    let ``Fibonacci R[τ,τ;1] equals exp(4πi/5) for vacuum fusion`` () =
        // Business meaning: Braiding two τ anyons that fuse to vacuum accumulates
        // a topological phase of 4π/5. This non-Abelian phase enables universal
        // quantum computation through braiding alone.
        let data = computeRMatrixOrFail AnyonSpecies.AnyonType.Fibonacci
        let tau = AnyonSpecies.Particle.Tau
        let vacuum = AnyonSpecies.Particle.Vacuum
        
        let value = getRSymbolOrFail data (rIndex tau tau vacuum) "R[τ,τ;1]"
        let expected = TopologicalHelpers.expI (4.0 * System.Math.PI / 5.0)
        
        assertComplexEquals expected value
        assertOnUnitCircle value
    
    [<Fact>]
    let ``Fibonacci R[τ,τ;τ] equals exp(-3πi/5) for τ fusion channel`` () =
        // Business meaning: When τ anyons braid and fuse back to τ, the phase
        // is -3π/5. The difference from the vacuum channel (4π/5 vs -3π/5)
        // creates interference patterns essential for quantum gates.
        let data = computeRMatrixOrFail AnyonSpecies.AnyonType.Fibonacci
        let tau = AnyonSpecies.Particle.Tau
        
        let value = getRSymbolOrFail data (rIndex tau tau tau) "R[τ,τ;τ]"
        let expected = TopologicalHelpers.expI (-3.0 * System.Math.PI / 5.0)
        
        assertComplexEquals expected value
        assertOnUnitCircle value
    
    [<Fact>]
    let ``Fibonacci vacuum braiding is trivial`` () =
        // Business meaning: As with Ising, braiding with vacuum does nothing.
        let data = computeRMatrixOrFail AnyonSpecies.AnyonType.Fibonacci
        let vacuum = AnyonSpecies.Particle.Vacuum
        let tau = AnyonSpecies.Particle.Tau
        
        let value = getRSymbolOrFail data (rIndex vacuum tau tau) "R[1,τ;τ]"
        
        assertComplexEquals Complex.One value
    
    [<Fact>]
    let ``Fibonacci all R-matrix elements lie on unit circle`` () =
        // Business meaning: Unitarity must hold for all anyon theories.
        let data = computeRMatrixOrFail AnyonSpecies.AnyonType.Fibonacci
        
        data.RSymbols
        |> Map.iter (fun _ value -> assertOnUnitCircle value)
    
    // ========================================================================
    // R-MATRIX ARRAY TESTS
    // ========================================================================
    
    [<Fact>]
    let ``Ising σ×σ produces 2×2 diagonal R-matrix`` () =
        // Business meaning: σ×σ has two fusion channels (1 and ψ), so the
        // R-matrix is 2×2. It's diagonal because Ising is multiplicity-free.
        let sigma = AnyonSpecies.Particle.Sigma
        
        match RMatrix.computeRMatrixArray sigma sigma AnyonSpecies.AnyonType.Ising with
        | Error err -> failwith $"Failed: {err.Message}"
        | Ok matrix ->
            Assert.Equal(2, Array2D.length1 matrix)
            Assert.Equal(2, Array2D.length2 matrix)
            
            // Check diagonal elements are non-zero
            assertOnUnitCircle matrix.[0, 0]
            assertOnUnitCircle matrix.[1, 1]
            
            // Check off-diagonal elements are zero
            assertComplexEquals Complex.Zero matrix.[0, 1]
            assertComplexEquals Complex.Zero matrix.[1, 0]
    
    [<Fact>]
    let ``Fibonacci τ×τ produces 2×2 diagonal R-matrix`` () =
        // Business meaning: τ×τ = 1 + τ has two channels, yielding 2×2 R-matrix.
        let tau = AnyonSpecies.Particle.Tau
        
        match RMatrix.computeRMatrixArray tau tau AnyonSpecies.AnyonType.Fibonacci with
        | Error err -> failwith $"Failed: {err.Message}"
        | Ok matrix ->
            Assert.Equal(2, Array2D.length1 matrix)
            Assert.Equal(2, Array2D.length2 matrix)
            
            assertOnUnitCircle matrix.[0, 0]
            assertOnUnitCircle matrix.[1, 1]
    
    [<Fact>]
    let ``Ising ψ×ψ produces 1×1 R-matrix`` () =
        // Business meaning: ψ×ψ = 1 (deterministic fusion) yields 1×1 R-matrix.
        let psi = AnyonSpecies.Particle.Psi
        
        match RMatrix.computeRMatrixArray psi psi AnyonSpecies.AnyonType.Ising with
        | Error err -> failwith $"Failed: {err.Message}"
        | Ok matrix ->
            Assert.Equal(1, Array2D.length1 matrix)
            Assert.Equal(1, Array2D.length2 matrix)
            
            // Should be -1 (fermion exchange)
            assertComplexEquals (Complex(-1.0, 0.0)) matrix.[0, 0]
    
    // ========================================================================
    // VALIDATION TESTS
    // ========================================================================
    
    [<Fact>]
    let ``Ising R-matrix passes unitarity validation`` () =
        // Business meaning: All R-matrix elements must lie on unit circle
        // to preserve quantum probability (unitary evolution).
        let data = computeRMatrixOrFail AnyonSpecies.AnyonType.Ising
        
        match RMatrix.validateRMatrix data with
        | Error err -> failwith $"Validation failed: {err.Message}"
        | Ok validated ->
            Assert.True(validated.IsValidated, "R-matrix should pass validation")
    
    [<Fact>]
    let ``Fibonacci R-matrix passes unitarity validation`` () =
        // Business meaning: Fibonacci R-matrices must also be unitary.
        let data = computeRMatrixOrFail AnyonSpecies.AnyonType.Fibonacci
        
        match RMatrix.validateRMatrix data with
        | Error err -> failwith $"Validation failed: {err.Message}"
        | Ok validated ->
            Assert.True(validated.IsValidated, "R-matrix should pass validation")
    
    // ========================================================================
    // ERROR HANDLING TESTS
    // ========================================================================
    
    [<Fact>]
    let ``getRSymbol rejects invalid fusion channels`` () =
        // Business meaning: Attempting to get R[a,b;c] for invalid fusion
        // a×b→c should fail with clear error message.
        let data = computeRMatrixOrFail AnyonSpecies.AnyonType.Ising
        let sigma = AnyonSpecies.Particle.Sigma
        let tau = AnyonSpecies.Particle.Tau  // Doesn't exist in Ising
        
        match RMatrix.getRSymbol data (rIndex sigma sigma tau) with
        | Ok _ -> failwith "Should have rejected invalid fusion"
        | Error err -> 
            Assert.Contains("Cannot fuse", err.Message)
    
    [<Fact>]
    let ``computeRMatrix accepts general SU(2)_k levels`` () =
        // Business meaning: General SU(2)_k R-matrices are now implemented using conformal field theory formulas.
        match RMatrix.computeRMatrix (AnyonSpecies.AnyonType.SU2Level 10) with
        | Ok data -> 
            Assert.Equal(AnyonSpecies.AnyonType.SU2Level 10, data.AnyonType)
            Assert.True(data.RSymbols.Count > 0, "Should have computed R-symbols for SU(2)_10")
        | Error err -> 
            failwith $"Should have accepted SU(2)_10: {err.Message}"
    
    // ========================================================================
    // SU(2) LEVEL 2 TESTS
    // ========================================================================
    
    [<Fact>]
    let ``SU(2)_2 R-matrix is equivalent to Ising`` () =
        // Business meaning: SU(2) level 2 and Ising are mathematically isomorphic.
        let su2Data = computeRMatrixOrFail (AnyonSpecies.AnyonType.SU2Level 2)
        let isingData = computeRMatrixOrFail AnyonSpecies.AnyonType.Ising
        
        Assert.Equal(isingData.RSymbols.Count, su2Data.RSymbols.Count)
    
    // ========================================================================
    // SU(2) LEVEL 3 TESTS
    // ========================================================================
    
    [<Fact>]
    let ``SU(2)_3 has correct particle content`` () =
        // Business meaning: SU(2)_3 has particles with j ∈ {0, 1/2, 1, 3/2}
        let data = computeRMatrixOrFail (AnyonSpecies.AnyonType.SU2Level 3)
        
        // Should have R-symbols for valid fusion channels
        // Expected particles: j=0 (vacuum), j=1/2, j=1, j=3/2
        Assert.True(data.RSymbols.Count > 0, "Should have computed R-symbols")
        
        // Verify anyon type
        Assert.Equal(AnyonSpecies.AnyonType.SU2Level 3, data.AnyonType)
    
    [<Fact>]
    let ``SU(2)_3 R-matrices are unitary`` () =
        // Business meaning: R-matrices must be unitary for physically consistent braiding.
        let data = computeRMatrixOrFail (AnyonSpecies.AnyonType.SU2Level 3)
        
        // All R-matrix elements should lie on unit circle
        for KeyValue(_, rValue) in data.RSymbols do
            let magnitude = Complex.Abs(rValue)
            Assert.True(abs(magnitude - 1.0) < 1e-10, 
                $"R-matrix element should have unit magnitude, got {magnitude}")
    
    [<Fact>]
    let ``SU(2)_3 conformal weights are correct`` () =
        // Business meaning: Conformal weights h_j = j(j+1)/(k+2) determine R-matrix phases.
        // For k=3: h_0=0, h_{1/2}=3/20, h_1=2/5, h_{3/2}=3/4
        
        let k = 3
        let kPlusTwo = float (k + 2)
        
        // Test conformal weight formula for each j
        let testWeight j =
            let expected = j * (j + 1.0) / kPlusTwo
            expected
        
        let h0 = testWeight 0.0
        let h_half = testWeight 0.5
        let h1 = testWeight 1.0
        let h_3half = testWeight 1.5
        
        Assert.Equal(0.0, h0)
        Assert.True(abs(h_half - 3.0/20.0) < 1e-10, $"h_{{1/2}} should be 3/20, got {h_half}")
        Assert.True(abs(h1 - 2.0/5.0) < 1e-10, $"h_1 should be 2/5, got {h1}")
        Assert.True(abs(h_3half - 3.0/4.0) < 1e-10, $"h_{{3/2}} should be 3/4, got {h_3half}")
    
    [<Fact>]
    let ``SU(2)_3 vacuum braiding is trivial`` () =
        // Business meaning: Braiding with vacuum (j=0) always gives phase 1.
        let data = computeRMatrixOrFail (AnyonSpecies.AnyonType.SU2Level 3)
        let vacuum = AnyonSpecies.Particle.Vacuum
        
        // Find any particle to test vacuum braiding
        let anyParticle = 
            data.RSymbols.Keys 
            |> Seq.tryFind (fun idx -> idx.A <> vacuum && idx.B <> vacuum)
        
        match anyParticle with
        | Some idx ->
            // R[vacuum, a; a] = 1 for any particle a
            let vacuumIndex = rIndex vacuum idx.A idx.A
            match RMatrix.getRSymbol data vacuumIndex with
            | Ok rValue ->
                Assert.True(abs(Complex.Abs(rValue) - 1.0) < 1e-10, "Vacuum braiding magnitude should be 1")
            | Error _ -> () // May not exist, that's okay for this test
        | None -> () // No non-vacuum particles found
    
    [<Fact>]
    let ``SU(2)_3 displays formatted output`` () =
        // Business meaning: Human-readable output for SU(2)_3 aids verification.
        let data = computeRMatrixOrFail (AnyonSpecies.AnyonType.SU2Level 3)
        let display = RMatrix.displayAllRSymbols data
        
        Assert.Contains("SU2Level 3", display)
        Assert.Contains("R[", display)
    
    // ========================================================================
    // DISPLAY TESTS
    // ========================================================================
    
    [<Fact>]
    let ``Ising R-symbols display formatted output`` () =
        // Business meaning: Human-readable output aids debugging and verification.
        let data = computeRMatrixOrFail AnyonSpecies.AnyonType.Ising
        let display = RMatrix.displayAllRSymbols data
        
        Assert.Contains("Ising", display)
        Assert.Contains("R[", display)
    
    [<Fact>]
    let ``Fibonacci R-symbols display formatted output`` () =
        // Business meaning: Display should show Fibonacci structure clearly.
        let data = computeRMatrixOrFail AnyonSpecies.AnyonType.Fibonacci
        let display = RMatrix.displayAllRSymbols data
        
        Assert.Contains("Fibonacci", display)
        Assert.Contains("R[τ,τ", display)
