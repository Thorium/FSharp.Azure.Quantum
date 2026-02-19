namespace FSharp.Azure.Quantum.Topological.Tests

open Xunit
open FSharp.Azure.Quantum.Topological
open System.Numerics

module FMatrixTests =
    
    // ========================================================================
    // TEST HELPERS - Make tests more readable and maintainable
    // ========================================================================
    
    /// Helper to create F-symbol index (reduces repetition)
    let private fIndex a b c d e f = {
        FMatrix.A = a
        FMatrix.B = b
        FMatrix.C = c
        FMatrix.D = d
        FMatrix.E = e
        FMatrix.F = f
    }
    
    /// Helper to assert complex value matches expected (with tolerance)
    let private assertComplexEquals (expected: Complex) (actual: Complex) =
        let tolerance = 1e-10
        Assert.InRange(actual.Real, expected.Real - tolerance, expected.Real + tolerance)
        Assert.InRange(actual.Imaginary, expected.Imaginary - tolerance, expected.Imaginary + tolerance)
    
    /// Helper to assert real value (imaginary part should be ~0)
    let private assertRealValue (expectedReal: float) (actual: Complex) =
        assertComplexEquals (Complex(expectedReal, 0.0)) actual
    
    /// Helper to get F-symbol or fail test with meaningful message
    let private getFSymbolOrFail data index testContext =
        match FMatrix.getFSymbol data index with
        | Ok value -> value
        | Error err -> failwith $"{testContext}: {err.Message}"
    
    /// Helper to compute F-matrix or fail with meaningful message
    let private computeFMatrixOrFail anyonType =
        match FMatrix.computeFMatrix anyonType with
        | Ok data -> data
        | Error err -> failwith $"Failed to compute {anyonType} F-matrix: {err.Message}"
    
    // ========================================================================
    // ISING F-MATRIX TESTS - Testing basis transformation for Majorana modes
    // ========================================================================
    
    [<Fact>]
    let ``Ising anyons have exactly 6 non-trivial F-symbols including abelian sector signs`` () =
        // Business meaning: For Ising anyons (Majorana zero modes), we have the
        // 4 σ×σ×σ→σ basis transformations plus 2 abelian sector sign F-symbols.
        // This reflects the underlying Z₂ × Z₂ algebraic structure.
        let data = computeFMatrixOrFail AnyonSpecies.AnyonType.Ising
        
        Assert.Equal(AnyonSpecies.AnyonType.Ising, data.AnyonType)
        Assert.Equal(6, data.FSymbols.Count)
    
    [<Fact>]
    let ``Ising basis transformation F[σ,σ,σ,σ;1,1] equals 1/√2 preserving normalization`` () =
        // Business meaning: When transforming from ((σ×σ)→1)×σ to σ×((σ×σ)→1),
        // both paths go through vacuum intermediate state. The 1/√2 coefficient
        // ensures probability conservation (|1/√2|² + |1/√2|² = 1).
        let data = computeFMatrixOrFail AnyonSpecies.AnyonType.Ising
        let sigma = AnyonSpecies.Particle.Sigma
        let vacuum = AnyonSpecies.Particle.Vacuum
        
        let value = getFSymbolOrFail data (fIndex sigma sigma sigma sigma vacuum vacuum) "F[σ,σ,σ,σ;1,1]"
        
        assertRealValue (1.0 / sqrt 2.0) value
    
    [<Fact>]
    let ``Ising basis transformation F[σ,σ,σ,σ;ψ,ψ] equals -1/√2 for orthogonal path`` () =
        // Business meaning: When transforming via fermion (ψ) intermediate states
        // instead of vacuum, we get -1/√2. The sign difference ensures orthogonality
        // between the two transformation paths (inner product = 0).
        let data = computeFMatrixOrFail AnyonSpecies.AnyonType.Ising
        let sigma = AnyonSpecies.Particle.Sigma
        let psi = AnyonSpecies.Particle.Psi
        
        let value = getFSymbolOrFail data (fIndex sigma sigma sigma sigma psi psi) "F[σ,σ,σ,σ;ψ,ψ]"
        
        assertRealValue (-1.0 / sqrt 2.0) value
    
    [<Fact>]
    let ``Ising trivial F-symbol F[ψ,σ,σ,ψ;σ,1] equals 1 for unambiguous fusion`` () =
        // Business meaning: When fusion paths have no degeneracy (only one way
        // to reach the final state), the F-symbol is trivial (= 1).
        // Validates: ψ×σ→σ ✓, σ×σ→1 ✓, σ×σ→1 ✓, ψ×1→ψ ✓
        let data = computeFMatrixOrFail AnyonSpecies.AnyonType.Ising
        let psi = AnyonSpecies.Particle.Psi
        let sigma = AnyonSpecies.Particle.Sigma
        let vacuum = AnyonSpecies.Particle.Vacuum
        
        let value = getFSymbolOrFail data (fIndex psi sigma sigma psi sigma vacuum) "F[ψ,σ,σ,ψ;σ,1]"
        
        assertRealValue 1.0 value
    
    [<Fact>]
    let ``Ising rejects invalid fusion channels for non-existent particles`` () =
        // Business meaning: F-symbols are only defined for valid fusion channels.
        // Attempting to use a non-existent particle (Tau) should fail validation.
        let data = computeFMatrixOrFail AnyonSpecies.AnyonType.Ising
        let sigma = AnyonSpecies.Particle.Sigma
        let tau = AnyonSpecies.Particle.Tau  // Doesn't exist in Ising theory
        
        // This should fail validation since Tau is not a valid Ising particle
        match FMatrix.getFSymbol data (fIndex sigma sigma sigma tau sigma sigma) with
        | Error _ -> () // Expected - invalid particle type
        | Ok value -> 
            // Or might return 0 for invalid channel
            Assert.Equal(0.0, value.Real)
    
    // ========================================================================
    // FIBONACCI F-MATRIX TESTS
    // ========================================================================
    
    [<Fact>]
    let ``Fibonacci anyons have non-trivial F-symbols reflecting golden ratio algebra`` () =
        // Business meaning: Fibonacci anyons are richer than Ising, with the golden
        // ratio φ appearing naturally in F-symbols due to the underlying SU(2)_3 theory.
        let data = computeFMatrixOrFail AnyonSpecies.AnyonType.Fibonacci
        
        Assert.Equal(AnyonSpecies.AnyonType.Fibonacci, data.AnyonType)
        Assert.True(data.FSymbols.Count > 0, "Should have non-trivial F-symbols")
    
    [<Fact>]
    let ``Fibonacci F[τ,τ,τ,1;τ,τ] equals 1 from unitary normalization of 1×1 block`` () =
        // Business meaning: When the total fusion outcome is vacuum (1), there is
        // only one intermediate channel (τ), making this a 1×1 block in the F-matrix.
        // Unitarity of a 1×1 block requires the value to be 1.
        let data = computeFMatrixOrFail AnyonSpecies.AnyonType.Fibonacci
        let tau = AnyonSpecies.Particle.Tau
        let vacuum = AnyonSpecies.Particle.Vacuum
        
        let value = getFSymbolOrFail data (fIndex tau tau tau vacuum tau tau) "F[τ,τ,τ,1;τ,τ]"
        
        assertRealValue 1.0 value
    
    [<Fact>]
    let ``Fibonacci F[τ,τ,τ,τ;τ,τ] equals -φ⁻¹ for τ→τ basis transformation`` () =
        // Business meaning: When all particles are τ, both intermediate states
        // are also τ. The negative sign (-φ⁻¹) ensures the F-matrix is unitary
        // and satisfies the pentagon equation for Fibonacci anyons.
        let phi = (1.0 + sqrt 5.0) / 2.0
        let data = computeFMatrixOrFail AnyonSpecies.AnyonType.Fibonacci
        let tau = AnyonSpecies.Particle.Tau
        
        let value = getFSymbolOrFail data (fIndex tau tau tau tau tau tau) "F[τ,τ,τ,τ;τ,τ]"
        
        assertRealValue (-1.0 / phi) value
    
    [<Fact>]
    let ``Fibonacci F[τ,τ,τ,τ;1,τ] equals φ⁻¹/² for vacuum-mediated path`` () =
        // Business meaning: This F-symbol connects the vacuum channel (τ×τ)→1
        // to the non-vacuum channel (τ×τ)→τ. The √(φ⁻¹) value arises from
        // normalizing the transformation to preserve probability.
        let phi = (1.0 + sqrt 5.0) / 2.0
        let data = computeFMatrixOrFail AnyonSpecies.AnyonType.Fibonacci
        let tau = AnyonSpecies.Particle.Tau
        let vacuum = AnyonSpecies.Particle.Vacuum
        
        let value = getFSymbolOrFail data (fIndex tau tau tau tau vacuum tau) "F[τ,τ,τ,τ;1,τ]"
        
        assertRealValue (sqrt (1.0 / phi)) value
    
    // ========================================================================
    // VALIDATION TESTS
    // ========================================================================
    
    [<Fact>]
    let ``Ising F-matrix passes pentagon equation and unitarity validation`` () =
        // Business meaning: Pentagon equation ensures basis transformations are
        // consistent regardless of the order we associate four anyons. Unitarity
        // ensures probability conservation in quantum state transformations.
        let data = computeFMatrixOrFail AnyonSpecies.AnyonType.Ising
        
        match FMatrix.validateFMatrix data with
        | Error err -> failwith $"Validation failed: {err.Message}"
        | Ok validated ->
            Assert.True(validated.IsValidated, "F-matrix must satisfy pentagon + unitarity")
    
    [<Fact>]
    let ``Fibonacci F-matrix passes pentagon equation and unitarity validation`` () =
        // Business meaning: Fibonacci anyons must satisfy the same consistency
        // conditions (pentagon + unitarity) to be used for quantum computation.
        let data = computeFMatrixOrFail AnyonSpecies.AnyonType.Fibonacci
        
        match FMatrix.validateFMatrix data with
        | Error err -> failwith $"Validation failed: {err.Message}"
        | Ok validated ->
            Assert.True(validated.IsValidated, "F-matrix must satisfy pentagon + unitarity")
    
    // ========================================================================
    // UNITARITY TESTS
    // ========================================================================
    
    [<Fact>]
    let ``Ising F-matrix is unitary for σ×σ×σ→σ preserving quantum probability`` () =
        // Business meaning: Unitarity (F F† = I) ensures that the total probability
        // is conserved when we transform between different fusion orderings. This
        // is essential for quantum computation where |amplitude|² = probability.
        let data = computeFMatrixOrFail AnyonSpecies.AnyonType.Ising
        let sigma = AnyonSpecies.Particle.Sigma
        
        match FMatrix.verifyFMatrixUnitarity data sigma sigma sigma sigma with
        | Error err -> failwith $"Unitarity check failed: {err.Message}"
        | Ok isUnitary ->
            Assert.True(isUnitary, "F F† = I ensures probability conservation")
    
    [<Fact>]
    let ``Fibonacci F-matrix satisfies golden ratio normalization identity`` () =
        // Business meaning: The normalization condition 1² + (φ⁻¹/²)² = φ ensures
        // that the sum of squared amplitudes equals the quantum dimension of τ.
        // This is a direct consequence of the fusion rule τ×τ = 1 + τ and
        // guarantees probability conservation in the Fibonacci fusion space.
        let phi = (1.0 + sqrt 5.0) / 2.0
        let data = computeFMatrixOrFail AnyonSpecies.AnyonType.Fibonacci
        let tau = AnyonSpecies.Particle.Tau
        let vacuum = AnyonSpecies.Particle.Vacuum
        
        // Verify F[τ,τ,τ,τ;1,τ] = φ⁻¹/²
        let value = getFSymbolOrFail data (fIndex tau tau tau tau vacuum tau) "F[τ,τ,τ,τ;1,τ]"
        assertRealValue (sqrt (1.0 / phi)) value
        
        // Check normalization: 1² + (φ⁻¹/²)² = φ⁻¹ + 1 = φ (quantum dimension of τ)
        let sqrtPhiInv = sqrt (1.0 / phi)
        let sum = 1.0 + sqrtPhiInv * sqrtPhiInv
        Assert.InRange(sum, phi - 1e-10, phi + 1e-10)
    
    // ========================================================================
    // DISPLAY TESTS
    // ========================================================================
    
    [<Fact>]
    let ``Ising F-symbols display formatted output with anyon type and values`` () =
        // Business meaning: Human-readable output aids debugging and verification
        // of F-matrix calculations during development.
        let data = computeFMatrixOrFail AnyonSpecies.AnyonType.Ising
        let display = FMatrix.displayAllFSymbols data
        
        Assert.Contains("Ising", display)
        Assert.Contains("F[σ,σ,σ,σ", display)
        Assert.Contains("1/√2", display.Replace("0.707107", "1/√2"))  // Approximate check
    
    [<Fact>]
    let ``Fibonacci F-symbols display formatted output with golden ratio notation`` () =
        // Business meaning: Display should clearly show golden ratio relationships
        // to aid understanding of the mathematical structure.
        let data = computeFMatrixOrFail AnyonSpecies.AnyonType.Fibonacci
        let display = FMatrix.displayAllFSymbols data
        
        Assert.Contains("Fibonacci", display)
        Assert.Contains("F[τ,τ,τ", display)
    
    // ========================================================================
    // SU(2) LEVEL 2 TESTS (should be same as Ising)
    // ========================================================================
    
    [<Fact>]
    let ``SU(2)_2 F-matrix is equivalent to Ising due to isomorphism`` () =
        // Business meaning: SU(2) level 2 and Ising anyons describe the same
        // physics from different perspectives. They are mathematically isomorphic,
        // so their F-matrices must have identical structure (same number of entries).
        let su2Data = computeFMatrixOrFail (AnyonSpecies.AnyonType.SU2Level 2)
        let isingData = computeFMatrixOrFail AnyonSpecies.AnyonType.Ising
        
        Assert.Equal(isingData.FSymbols.Count, su2Data.FSymbols.Count)
    
    // ========================================================================
    // ERROR HANDLING TESTS
    // ========================================================================
    
    [<Fact>]
    let ``Computing SU(2)_10 returns valid F-matrix with non-trivial symbols`` () =
        // Business meaning: General SU(2)_k F-symbols are now computed via
        // quantum 6j-symbols (q-Racah coefficients). SU(2)_10 has 6 particle
        // types (j=0,1/2,1,...,5) with rich fusion structure.
        match FMatrix.computeFMatrix (AnyonSpecies.AnyonType.SU2Level 10) with
        | Error err -> failwith $"Should succeed for SU(2)_10: {err.Message}"
        | Ok data ->
            Assert.True(data.FSymbols.Count > 0, "SU(2)_10 should have non-trivial F-symbols")
    
    [<Fact>]
    let ``getFSymbol returns trivial value (1.0) for valid but unstored F-symbols`` () =
        // Business meaning: Not all F-symbols are explicitly stored - only
        // non-trivial ones. Valid fusion channels not in the map default to 1.0
        // (identity transformation), which is correct for unambiguous fusion paths.
        let emptyData = {
            FMatrix.AnyonType = AnyonSpecies.AnyonType.Ising
            FMatrix.FSymbols = Map.empty
            FMatrix.IsValidated = false
        }
        
        let psi = AnyonSpecies.Particle.Psi
        let sigma = AnyonSpecies.Particle.Sigma
        let vacuum = AnyonSpecies.Particle.Vacuum
        
        // Use valid trivial F-symbol: F[ψ,σ,σ,ψ;σ,1]
        match FMatrix.getFSymbol emptyData (fIndex psi sigma sigma psi sigma vacuum) with
        | Error _ -> failwith "Should succeed with trivial value for valid fusion"
        | Ok value ->
            assertRealValue 1.0 value
    
    // ========================================================================
    // SU(2)_k GENERAL THEORY TESTS (quantum 6j-symbols)
    // ========================================================================
    
    [<Fact>]
    let ``SU(2)_3 F-matrix for integer-spin sector reproduces Fibonacci F-symbols`` () =
        // Business meaning: SU(2)_3 restricted to integer spins (j=0,1) is
        // isomorphic to Fibonacci anyons. j=1 corresponds to τ.
        // The F^{τττ}_τ 2×2 block should use the golden ratio.
        let data = computeFMatrixOrFail (AnyonSpecies.AnyonType.SU2Level 3)
        
        let phi = (1.0 + sqrt 5.0) / 2.0
        let j0 = AnyonSpecies.Particle.SpinJ(0, 3)  // Vacuum ≡ j=0
        let j1 = AnyonSpecies.Particle.SpinJ(2, 3)  // j=1 ≡ τ in integer sector
        
        // F[j1,j1,j1,j1;j0,j0] should correspond to Fibonacci F[τ,τ,τ,τ;1,1] = φ⁻¹
        let f_11_00 = getFSymbolOrFail data (fIndex j1 j1 j1 j1 j0 j0) "F[j1,j1,j1,j1;j0,j0]"
        assertRealValue (1.0 / phi) f_11_00
    
    [<Fact>]
    let ``SU(2)_3 F-matrix passes pentagon equation validation`` () =
        // Business meaning: The quantum 6j-symbols computed for SU(2)_3
        // must satisfy the pentagon consistency equation, verifying that
        // our Racah formula implementation is correct.
        let data = computeFMatrixOrFail (AnyonSpecies.AnyonType.SU2Level 3)
        
        match FMatrix.validateFMatrix data with
        | Error err -> failwith $"SU(2)_3 pentagon validation failed: {err.Message}"
        | Ok validated ->
            Assert.True(validated.IsValidated, "SU(2)_3 F-matrix must pass pentagon + unitarity")
    
    [<Fact>]
    let ``SU(2)_4 F-matrix passes pentagon equation validation`` () =
        // Business meaning: k=4 is the first non-trivial level beyond Fibonacci.
        // It has 5 particle types (j=0,1/2,1,3/2,2) and tests the general
        // 6j-symbol computation with half-integer spins.
        let data = computeFMatrixOrFail (AnyonSpecies.AnyonType.SU2Level 4)
        
        match FMatrix.validateFMatrix data with
        | Error err -> failwith $"SU(2)_4 pentagon validation failed: {err.Message}"
        | Ok validated ->
            Assert.True(validated.IsValidated, "SU(2)_4 F-matrix must pass pentagon + unitarity")
    
    [<Fact>]
    let ``SU(2)_5 F-matrix passes pentagon equation validation`` () =
        // Business meaning: k=5 is the first universal level (k >= 3, k != 4).
        // Pentagon validation here confirms our 6j-symbols are correct for
        // a theory that can perform universal quantum computation via braiding.
        let data = computeFMatrixOrFail (AnyonSpecies.AnyonType.SU2Level 5)
        
        match FMatrix.validateFMatrix data with
        | Error err -> failwith $"SU(2)_5 pentagon validation failed: {err.Message}"
        | Ok validated ->
            Assert.True(validated.IsValidated, "SU(2)_5 F-matrix must pass pentagon + unitarity")
    
    [<Fact>]
    let ``SU(2)_k F-matrix has correct vacuum F-symbols for all k`` () =
        // Business meaning: Fusing with vacuum is trivial — F-symbols involving
        // vacuum as external leg should be identity transformations (value = 1)
        // for all valid fusion channels.
        for k in [3; 4; 5; 6] do
            let data = computeFMatrixOrFail (AnyonSpecies.AnyonType.SU2Level k)
            let j0 = AnyonSpecies.Particle.SpinJ(0, k)
            let j1 = AnyonSpecies.Particle.SpinJ(1, k)  // j = 1/2
            
            // F[j0, j1, j0, j1; j1, j1] should be trivial (= 1)
            // Valid fusion: A×B→E: 0×½→½, E×C→D: ½×0→½, B×C→F: ½×0→½, A×F→D: 0×½→½
            // Fusing with vacuum doesn't change anything, so this is identity.
            let value = getFSymbolOrFail data (fIndex j0 j1 j0 j1 j1 j1) $"F[j0,j1,j0,j1;j1,j1] for k={k}"
            assertRealValue 1.0 value
