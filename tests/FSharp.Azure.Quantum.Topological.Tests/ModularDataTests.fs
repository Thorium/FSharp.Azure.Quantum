namespace FSharp.Azure.Quantum.Topological.Tests

open Xunit
open FSharp.Azure.Quantum.Topological
open System.Numerics

module ModularDataTests =
    
    [<Fact>]
    let ``Ising S-matrix is computed correctly`` () =
        // Arrange & Act
        let result = ModularData.computeSMatrix AnyonSpecies.Ising
        
        // Assert
        match result with
        | Ok s ->
            // Check dimensions
            Assert.Equal(3, Array2D.length1 s)
            Assert.Equal(3, Array2D.length2 s)
            
            // Check specific values from Simon Table 17.1
            let sqrt2 = sqrt 2.0
            Assert.Equal(0.5, s.[0, 0].Real, 10)
            Assert.Equal(sqrt2 / 2.0, s.[0, 1].Real, 10)
            Assert.Equal(0.5, s.[0, 2].Real, 10)
            Assert.Equal(0.0, s.[1, 1].Real, 10)
        | Error e ->
            Assert.Fail($"Failed to compute S-matrix: {e}")
    
    [<Fact>]
    let ``Fibonacci S-matrix is computed correctly`` () =
        // Arrange & Act
        let result = ModularData.computeSMatrix AnyonSpecies.Fibonacci
        
        // Assert
        match result with
        | Ok s ->
            // Check dimensions
            Assert.Equal(2, Array2D.length1 s)
            Assert.Equal(2, Array2D.length2 s)
            
            // Check golden ratio appears
            let phi = (1.0 + sqrt 5.0) / 2.0
            let norm = sqrt (2.0 + phi)
            Assert.Equal(1.0 / norm, s.[0, 0].Real, 10)
            Assert.Equal(phi / norm, s.[0, 1].Real, 10)
        | Error e ->
            Assert.Fail($"Failed to compute S-matrix: {e}")
    
    [<Fact>]
    let ``Ising T-matrix is diagonal`` () =
        // Arrange & Act
        let result = ModularData.computeTMatrix AnyonSpecies.Ising
        
        // Assert
        match result with
        | Ok t ->
            // Check it's diagonal
            Assert.True(ModularData.verifyTMatrixDiagonal t)
            
            // Check diagonal elements are unit magnitude
            for i in 0 .. 2 do
                Assert.Equal(1.0, Complex.Abs(t.[i, i]), 10)
        | Error e ->
            Assert.Fail($"Failed to compute T-matrix: {e}")
    
    [<Fact>]
    let ``Fibonacci T-matrix is diagonal`` () =
        // Arrange & Act
        let result = ModularData.computeTMatrix AnyonSpecies.Fibonacci
        
        // Assert
        match result with
        | Ok t ->
            // Check it's diagonal
            Assert.True(ModularData.verifyTMatrixDiagonal t)
            
            // Check diagonal elements
            Assert.Equal(1.0, t.[0, 0].Real, 10)  // h=0 → θ=1
            
            // h_τ = 2/5 → θ = exp(4πi/5)
            let expectedPhase = 2.0 * System.Math.PI * 2.0 / 5.0
            let expectedReal = cos expectedPhase
            let expectedImag = sin expectedPhase
            Assert.Equal(expectedReal, t.[1, 1].Real, 10)
            Assert.Equal(expectedImag, t.[1, 1].Imaginary, 10)
        | Error e ->
            Assert.Fail($"Failed to compute T-matrix: {e}")
    
    [<Fact>]
    let ``Ising S-matrix is unitary`` () =
        // Arrange & Act
        let result = ModularData.computeSMatrix AnyonSpecies.Ising
        
        // Assert
        match result with
        | Ok s ->
            Assert.True(ModularData.verifySMatrixUnitary s)
        | Error e ->
            Assert.Fail($"Failed: {e}")
    
    [<Fact>]
    let ``Fibonacci S-matrix is unitary`` () =
        // Arrange & Act
        let result = ModularData.computeSMatrix AnyonSpecies.Fibonacci
        
        // Assert
        match result with
        | Ok s ->
            Assert.True(ModularData.verifySMatrixUnitary s)
        | Error e ->
            Assert.Fail($"Failed: {e}")
    
    [<Fact>]
    let ``Ising modular data satisfies (ST)^3 = S^2`` () =
        // Arrange & Act
        let sResult = ModularData.computeSMatrix AnyonSpecies.Ising
        let tResult = ModularData.computeTMatrix AnyonSpecies.Ising
        
        // Assert
        match sResult, tResult with
        | Ok s, Ok t ->
            Assert.True(ModularData.verifyModularSTRelation s t)
        | _ ->
            Assert.Fail("Failed to compute S or T matrix")
    
    [<Fact>]
    let ``Fibonacci modular data satisfies (ST)^3 = S^2`` () =
        // Arrange & Act
        let sResult = ModularData.computeSMatrix AnyonSpecies.Fibonacci
        let tResult = ModularData.computeTMatrix AnyonSpecies.Fibonacci
        
        // Assert
        match sResult, tResult with
        | Ok s, Ok t ->
            Assert.True(ModularData.verifyModularSTRelation s t)
        | _ ->
            Assert.Fail("Failed to compute S or T matrix")
    
    [<Fact>]
    let ``Complete Ising modular data is consistent`` () =
        // Arrange & Act
        let result = ModularData.computeModularData AnyonSpecies.Ising
        
        // Assert
        match result with
        | Ok data ->
            Assert.True(ModularData.verifyModularData data)
            Assert.Equal(0.5, data.CentralCharge)
            Assert.Equal(3, data.Particles.Length)
        | Error e ->
            Assert.Fail($"Failed: {e}")
    
    [<Fact>]
    let ``Complete Fibonacci modular data is consistent`` () =
        // Arrange & Act
        let result = ModularData.computeModularData AnyonSpecies.Fibonacci
        
        // Assert
        match result with
        | Ok data ->
            Assert.True(ModularData.verifyModularData data)
            Assert.Equal(2.8, data.CentralCharge)  // 14/5
            Assert.Equal(2, data.Particles.Length)
        | Error e ->
            Assert.Fail($"Failed: {e}")
    
    [<Fact>]
    let ``Ising total quantum dimension is 2`` () =
        // Arrange & Act
        let result = ModularData.totalQuantumDimension AnyonSpecies.Ising
        
        // Assert
        match result with
        | Ok d ->
            // D² = d₁² + d_σ² + d_ψ² = 1 + 2 + 1 = 4 → D = 2
            Assert.Equal(2.0, d, 10)
        | Error e ->
            Assert.Fail($"Failed: {e}")
    
    [<Fact>]
    let ``Fibonacci total quantum dimension matches sqrt(1 + phi^2)`` () =
        // Arrange & Act
        let result = ModularData.totalQuantumDimension AnyonSpecies.Fibonacci
        
        // Assert
        match result with
        | Ok d ->
            let phi = (1.0 + sqrt 5.0) / 2.0
            let expected = sqrt (1.0 + phi * phi)
            Assert.Equal(expected, d, 10)
        | Error e ->
            Assert.Fail($"Failed: {e}")
    
    [<Fact>]
    let ``Ground state degeneracy on sphere is 1`` () =
        // Arrange
        let result = ModularData.computeModularData AnyonSpecies.Ising
        
        // Act & Assert
        match result with
        | Ok data ->
            let dim = ModularData.groundStateDegeneracy data 0  // genus=0 (sphere)
            Assert.Equal(1, dim)
        | Error e ->
            Assert.Fail($"Failed: {e}")
    
    [<Fact>]
    let ``Ising ground state degeneracy on torus is 3`` () =
        // Arrange
        let result = ModularData.computeModularData AnyonSpecies.Ising
        
        // Act & Assert
        match result with
        | Ok data ->
            let dim = ModularData.groundStateDegeneracy data 1  // genus=1 (torus)
            Assert.Equal(3, dim)  // Number of particle types
        | Error e ->
            Assert.Fail($"Failed: {e}")
    
    [<Fact>]
    let ``Fibonacci ground state degeneracy on torus is 2`` () =
        // Arrange
        let result = ModularData.computeModularData AnyonSpecies.Fibonacci
        
        // Act & Assert
        match result with
        | Ok data ->
            let dim = ModularData.groundStateDegeneracy data 1  // genus=1 (torus)
            Assert.Equal(2, dim)  // Number of particle types
        | Error e ->
            Assert.Fail($"Failed: {e}")
    
    [<Fact>]
    let ``S-matrix first row gives normalized quantum dimensions`` () =
        // Arrange & Act
        let sResult = ModularData.computeSMatrix AnyonSpecies.Ising
        let dResult = ModularData.totalQuantumDimension AnyonSpecies.Ising
        
        // Assert
        match sResult, dResult with
        | Ok s, Ok totalD ->
            // S₀ₐ = dₐ/D for all particles a
            let d1 = 1.0
            let dSigma = sqrt 2.0
            let dPsi = 1.0
            
            Assert.Equal(d1 / totalD, s.[0, 0].Real, 10)
            Assert.Equal(dSigma / totalD, s.[0, 1].Real, 10)
            Assert.Equal(dPsi / totalD, s.[0, 2].Real, 10)
        | _ ->
            Assert.Fail("Failed to compute S-matrix or total dimension")
    
    [<Fact>]
    let ``SU(2)_2 is same as Ising`` () =
        // Arrange & Act
        let isingSResult = ModularData.computeSMatrix AnyonSpecies.Ising
        let su2SResult = ModularData.computeSMatrix (AnyonSpecies.SU2Level 2)
        
        // Assert
        match isingSResult, su2SResult with
        | Ok s1, Ok s2 ->
            // Check matrices are equal
            for i in 0 .. 2 do
                for j in 0 .. 2 do
                    Assert.Equal(s1.[i, j].Real, s2.[i, j].Real, 10)
                    Assert.Equal(s1.[i, j].Imaginary, s2.[i, j].Imaginary, 10)
        | _ ->
            Assert.Fail("Failed to compute S-matrices")
    
    [<Fact>]
    let ``SU(2)_3 central charge is 9/7`` () =
        // Arrange & Act
        let result = ModularData.centralCharge (AnyonSpecies.SU2Level 3)
        
        // Assert
        match result with
        | Ok c ->
            // c = 3k/(k+2) = 3*3/(3+2) = 9/5
            Assert.Equal(9.0 / 5.0, c, 10)
        | Error e ->
            Assert.Fail($"Failed: {e}")
    
    // ========================================================================
    // SU(2)_k GENERAL TESTS
    // ========================================================================
    
    [<Fact>]
    let ``SU(2)_k S-matrix is computed for arbitrary k`` () =
        // Business meaning: General SU(2)_k theories now have full modular data
        let result = ModularData.computeSMatrix (AnyonSpecies.SU2Level 4)
        
        match result with
        | Ok s ->
            // SU(2)_4 has j ∈ {0, 1/2, 1, 3/2, 2} → 5 particles
            Assert.Equal(5, Array2D.length1 s)
            Assert.Equal(5, Array2D.length2 s)
            
            // Verify unitarity
            Assert.True(ModularData.verifySMatrixUnitary s, "S-matrix should be unitary")
            
            // Verify symmetry (S should be symmetric for SU(2)_k) - idiomatic F#
            let indices = [0..4]
            indices |> List.iter (fun i ->
                indices |> List.iter (fun j ->
                    Assert.Equal(s.[i, j].Real, s.[j, i].Real, 10)
                    Assert.Equal(s.[i, j].Imaginary, s.[j, i].Imaginary, 10)))
        | Error err ->
            Assert.Fail($"Should compute S-matrix for SU(2)_4: {err.Message}")
    
    [<Fact>]
    let ``SU(2)_k T-matrix is computed for arbitrary k`` () =
        // Business meaning: T-matrix determines topological spin for all particles
        let result = ModularData.computeTMatrix (AnyonSpecies.SU2Level 5)
        
        match result with
        | Ok t ->
            // SU(2)_5 has j ∈ {0, 1/2, 1, 3/2, 2, 5/2} → 6 particles
            Assert.Equal(6, Array2D.length1 t)
            Assert.Equal(6, Array2D.length2 t)
            
            // Verify diagonal
            Assert.True(ModularData.verifyTMatrixDiagonal t, "T-matrix should be diagonal")
            
            // Verify all diagonal elements have unit magnitude - idiomatic F#
            [0..5]
            |> List.iter (fun i ->
                let magnitude = Complex.Abs(t.[i, i])
                Assert.InRange(magnitude, 0.9999, 1.0001))
        | Error err ->
            Assert.Fail($"Should compute T-matrix for SU(2)_5: {err.Message}")
    
    [<Fact>]
    let ``SU(2)_3 S-matrix matches known properties`` () =
        // Business meaning: Verify general formula produces correct structure
        let result = ModularData.computeSMatrix (AnyonSpecies.SU2Level 3)
        
        match result with
        | Ok s ->
            // Verify dimensions
            Assert.Equal(4, Array2D.length1 s)
            
            // Verify S-matrix is symmetric - idiomatic F#
            [0..3] |> List.iter (fun i ->
                [0..3] |> List.iter (fun j ->
                    Assert.Equal(s.[i, j].Real, s.[j, i].Real, 10)))
            
            // Verify unitarity
            Assert.True(ModularData.verifySMatrixUnitary s)
        | Error err ->
            Assert.Fail($"Failed to compute SU(2)_3 S-matrix: {err.Message}")
    
    [<Fact>]
    let ``SU(2)_3 T-matrix has correct structure`` () =
        // Business meaning: Topological spins should be consistent with conformal weights
        let result = ModularData.computeTMatrix (AnyonSpecies.SU2Level 3)
        
        match result with
        | Ok t ->
            // Verify diagonal structure
            Assert.True(ModularData.verifyTMatrixDiagonal t)
            
            // All diagonal elements should have unit magnitude
            [0..3]
            |> List.iter (fun i ->
                let magnitude = Complex.Abs(t.[i, i])
                Assert.InRange(magnitude, 0.9999, 1.0001))
        | Error err ->
            Assert.Fail($"Failed to compute SU(2)_3 T-matrix: {err.Message}")
    
    [<Fact>]
    let ``SU(2)_k S-matrix is symmetric and unitary`` () =
        // Business meaning: S-matrix must satisfy fundamental modular tensor category axioms
        let result = ModularData.computeSMatrix (AnyonSpecies.SU2Level 3)
        
        match result with
        | Ok s ->
            // Verify S-matrix is symmetric - idiomatic F#
            [0..3] |> List.iter (fun i ->
                [0..3] |> List.iter (fun j ->
                    Assert.Equal(s.[i, j].Real, s.[j, i].Real, 10)
                    Assert.Equal(s.[i, j].Imaginary, s.[j, i].Imaginary, 10)))
            
            // Verify unitarity
            Assert.True(ModularData.verifySMatrixUnitary s)
        | Error err ->
            Assert.Fail($"Failed: {err.Message}")

