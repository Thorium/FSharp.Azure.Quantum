namespace FSharp.Azure.Quantum.Topological.Tests

open Xunit
open FSharp.Azure.Quantum.Topological

module EntanglementEntropyTests =
    
    // ========================================================================
    // BASIC TOPOLOGICAL ENTROPY TESTS
    // ========================================================================
    
    [<Fact>]
    let ``Ising topological entropy equals log(2)`` () =
        // Arrange
        let anyonType = AnyonSpecies.AnyonType.Ising
        
        // Act
        let result = EntanglementEntropy.topologicalEntropy anyonType
        
        // Assert
        match result with
        | Ok gamma ->
            let expected = log 2.0  // D = 2 for Ising
            Assert.InRange(gamma, expected - 1e-10, expected + 1e-10)
        | Error err ->
            Assert.Fail($"Should succeed, got error: {err.Message}")
    
    [<Fact>]
    let ``Fibonacci topological entropy matches theory`` () =
        // Arrange
        let anyonType = AnyonSpecies.AnyonType.Fibonacci
        
        // Act
        let result = EntanglementEntropy.topologicalEntropy anyonType
        
        // Assert
        match result with
        | Ok gamma ->
            // D = √(1² + φ²) where φ = (1+√5)/2
            let phi = (1.0 + sqrt 5.0) / 2.0
            let D = sqrt (1.0 + phi * phi)
            let expected = log D
            Assert.InRange(gamma, expected - 1e-10, expected + 1e-10)
        | Error err ->
            Assert.Fail($"Should succeed, got error: {err.Message}")
    
    [<Fact>]
    let ``SU(2)_2 equals Ising`` () =
        // Arrange: SU(2)₂ should be identical to Ising
        let ising = AnyonSpecies.AnyonType.Ising
        let su2Level2 = AnyonSpecies.AnyonType.SU2Level 2
        
        // Act
        let gammaIsing = EntanglementEntropy.topologicalEntropy ising
        let gammaSU2 = EntanglementEntropy.topologicalEntropy su2Level2
        
        // Assert
        match gammaIsing, gammaSU2 with
        | Ok g1, Ok g2 ->
            Assert.InRange(abs (g1 - g2), 0.0, 1e-10)
        | _ ->
            Assert.Fail("Both should succeed")
    
    [<Fact>]
    let ``Topological entropy is always non-negative`` () =
        // Arrange
        let anyonTypes = [
            AnyonSpecies.AnyonType.Ising
            AnyonSpecies.AnyonType.Fibonacci
            AnyonSpecies.AnyonType.SU2Level 2
            AnyonSpecies.AnyonType.SU2Level 3
        ]
        
        // Act & Assert
        for anyonType in anyonTypes do
            match EntanglementEntropy.topologicalEntropy anyonType with
            | Ok gamma ->
                Assert.True(gamma >= 0.0, $"{anyonType} entropy should be non-negative")
            | Error _ ->
                ()  // Skip unimplemented types
    
    // ========================================================================
    // LOG BASE 2 (BITS) TESTS
    // ========================================================================
    
    [<Fact>]
    let ``Ising entropy in bits equals 1`` () =
        // Arrange
        let anyonType = AnyonSpecies.AnyonType.Ising
        
        // Act
        let result = EntanglementEntropy.topologicalEntropyLog2 anyonType
        
        // Assert
        match result with
        | Ok gammaBits ->
            // log₂(2) = 1
            Assert.InRange(gammaBits, 1.0 - 1e-10, 1.0 + 1e-10)
        | Error err ->
            Assert.Fail($"Should succeed, got error: {err.Message}")
    
    [<Fact>]
    let ``Natural log and log2 are consistent`` () =
        // Arrange
        let anyonType = AnyonSpecies.AnyonType.Fibonacci
        
        // Act
        let gammaNat = EntanglementEntropy.topologicalEntropyNat anyonType
        let gammaLog2 = EntanglementEntropy.topologicalEntropyLog2 anyonType
        
        // Assert
        match gammaNat, gammaLog2 with
        | Ok gn, Ok g2 ->
            let expected = gn / log 2.0
            Assert.InRange(g2, expected - 1e-10, expected + 1e-10)
        | _ ->
            Assert.Fail("Both should succeed")
    
    // ========================================================================
    // VON NEUMANN ENTROPY FROM EIGENVALUES
    // ========================================================================
    
    [<Fact>]
    let ``Pure state has zero entropy`` () =
        // Arrange: |ψ⟩ = |0⟩ → eigenvalues = [1.0]
        let eigenvalues = [1.0]
        
        // Act
        let result = EntanglementEntropy.vonNeumannEntropyFromEigenvalues eigenvalues
        
        // Assert
        match result with
        | Ok calc ->
            Assert.InRange(calc.Entropy, 0.0, 1e-10)
            Assert.InRange(calc.EntropyBits, 0.0, 1e-10)
        | Error err ->
            Assert.Fail($"Should succeed, got error: {err.Message}")
    
    [<Fact>]
    let ``Maximally mixed qubit has entropy log(2)`` () =
        // Arrange: ρ = I/2 → eigenvalues = [0.5, 0.5]
        let eigenvalues = [0.5; 0.5]
        
        // Act
        let result = EntanglementEntropy.vonNeumannEntropyFromEigenvalues eigenvalues
        
        // Assert
        match result with
        | Ok calc ->
            let expected = log 2.0
            Assert.InRange(calc.Entropy, expected - 1e-10, expected + 1e-10)
            Assert.InRange(calc.EntropyBits, 1.0 - 1e-10, 1.0 + 1e-10)  // 1 bit
        | Error err ->
            Assert.Fail($"Should succeed, got error: {err.Message}")
    
    [<Fact>]
    let ``Maximally mixed qutrit has entropy log(3)`` () =
        // Arrange: ρ = I/3 → eigenvalues = [1/3, 1/3, 1/3]
        let eigenvalues = [1.0/3.0; 1.0/3.0; 1.0/3.0]
        
        // Act
        let result = EntanglementEntropy.vonNeumannEntropyFromEigenvalues eigenvalues
        
        // Assert
        match result with
        | Ok calc ->
            let expected = log 3.0
            Assert.InRange(calc.Entropy, expected - 1e-10, expected + 1e-10)
        | Error err ->
            Assert.Fail($"Should succeed, got error: {err.Message}")
    
    [<Fact>]
    let ``Eigenvalues must be non-negative`` () =
        // Arrange: Invalid negative eigenvalue
        let eigenvalues = [0.6; 0.5; -0.1]  // Unphysical!
        
        // Act
        let result = EntanglementEntropy.vonNeumannEntropyFromEigenvalues eigenvalues
        
        // Assert
        match result with
        | Ok _ ->
            Assert.Fail("Should reject negative eigenvalues")
        | Error (TopologicalError.ValidationError (field, reason)) ->
            Assert.Contains("non-negative", reason)
        | Error err ->
            Assert.Fail($"Wrong error type: {err.Message}")
    
    [<Fact>]
    let ``Eigenvalues must sum to 1`` () =
        // Arrange: Not normalized
        let eigenvalues = [0.6; 0.3]  // Sum = 0.9 ≠ 1.0
        
        // Act
        let result = EntanglementEntropy.vonNeumannEntropyFromEigenvalues eigenvalues
        
        // Assert
        match result with
        | Ok _ ->
            Assert.Fail("Should reject unnormalized eigenvalues")
        | Error (TopologicalError.ValidationError (field, reason)) ->
            Assert.Contains("sum to 1", reason)
        | Error err ->
            Assert.Fail($"Wrong error type: {err.Message}")
    
    [<Fact>]
    let ``Zero eigenvalues contribute zero to entropy`` () =
        // Arrange: ρ with one zero eigenvalue (rank-deficient)
        let eigenvalues = [0.7; 0.3; 0.0]
        
        // Act
        let result = EntanglementEntropy.vonNeumannEntropyFromEigenvalues eigenvalues
        
        // Assert
        match result with
        | Ok calc ->
            // S = -0.7*log(0.7) - 0.3*log(0.3) - 0*log(0)
            // The 0*log(0) term should contribute 0 (not NaN!)
            Assert.False(System.Double.IsNaN calc.Entropy)
            Assert.True(calc.Entropy > 0.0)
        | Error err ->
            Assert.Fail($"Should succeed, got error: {err.Message}")
    
    // ========================================================================
    // KITAEV-PRESKILL FORMULA
    // ========================================================================
    
    [<Fact>]
    let ``Kitaev-Preskill extracts correct entropy for Ising`` () =
        // Arrange: Simulated entropy values for Ising toric code
        // These would come from actual reduced density matrix calculations
        let regions: EntanglementEntropy.KitaevPreskillRegions = {
            S_A = 2.5
            S_B = 2.5
            S_C = 2.5
            S_AB = 4.307  // S_A + S_B - γ where γ = log(2)
            S_BC = 4.307
            S_CA = 4.307
            S_ABC = 4.728  // Chosen to give KP formula result = -log(2)
        }
        
        // Act
        let extracted = EntanglementEntropy.kitaevPreskill regions
        
        // Assert
        let expectedGamma = log 2.0  // Ising: γ = log(2)
        // Note: Kitaev-Preskill returns -γ by convention
        Assert.InRange(extracted, -expectedGamma - 0.1, -expectedGamma + 0.1)
    
    [<Fact>]
    let ``Verify Kitaev-Preskill against theoretical value`` () =
        // Arrange
        let anyonType = AnyonSpecies.AnyonType.Ising
        let regions: EntanglementEntropy.KitaevPreskillRegions = {
            S_A = 2.5
            S_B = 2.5
            S_C = 2.5
            S_AB = 4.307  // S_A + S_B - γ where γ = log(2)
            S_BC = 4.307
            S_CA = 4.307
            S_ABC = 4.728  // Chosen to give KP formula result = -log(2)
        }
        
        // Act
        let result = EntanglementEntropy.verifyKitaevPreskill anyonType regions
        
        // Assert
        match result with
        | Ok isConsistent ->
            Assert.True(isConsistent, "Kitaev-Preskill should match theoretical value")
        | Error err ->
            Assert.Fail($"Should succeed, got error: {err.Message}")
    
    // ========================================================================
    // LEVIN-WEN METHOD
    // ========================================================================
    
    [<Fact>]
    let ``Levin-Wen extracts topological entropy`` () =
        // Arrange: Two disk regions of different sizes
        // S = α*P - γ (area law)
        let alpha = 0.5  // Area law coefficient
        let gamma = log 2.0  // Topological entropy
        
        let regions: EntanglementEntropy.LevinWenRegions = {
            S_small = alpha * 10.0 - gamma
            Perimeter_small = 10.0
            S_large = alpha * 20.0 - gamma
            Perimeter_large = 20.0
        }
        
        // Act
        let (alphaExtracted, gammaExtracted) = EntanglementEntropy.levinWen regions
        
        // Assert
        Assert.InRange(alphaExtracted, alpha - 1e-10, alpha + 1e-10)
        Assert.InRange(gammaExtracted, gamma - 1e-10, gamma + 1e-10)
    
    [<Fact>]
    let ``Levin-Wen handles degenerate case`` () =
        // Arrange: Same size regions (degenerate)
        let regions: EntanglementEntropy.LevinWenRegions = {
            S_small = 5.0
            Perimeter_small = 10.0
            S_large = 5.0
            Perimeter_large = 10.0
        }
        
        // Act
        let (alpha, gamma) = EntanglementEntropy.levinWen regions
        
        // Assert: Should handle gracefully (α=0, γ=-5.0)
        // In degenerate case: γ = α * P - S = 0 * 10 - 5 = -5
        // Note: Negative γ is unphysical, indicating degenerate extraction failed
        Assert.InRange(alpha, 0.0 - 1e-10, 0.0 + 1e-10)
        Assert.InRange(gamma, -5.0 - 1e-10, -5.0 + 1e-10)
    
    // ========================================================================
    // GROUND STATE DEGENERACY
    // ========================================================================
    
    [<Fact>]
    let ``Sphere (g=0) has GSD=1`` () =
        // Arrange
        let anyonType = AnyonSpecies.AnyonType.Ising
        let genus = 0
        
        // Act
        let result = EntanglementEntropy.groundStateDegeneracy anyonType genus
        
        // Assert
        match result with
        | Ok gsd ->
            Assert.Equal(1, gsd)
        | Error err ->
            Assert.Fail($"Should succeed, got error: {err.Message}")
    
    [<Fact>]
    let ``Torus (g=1) GSD equals number of anyon types`` () =
        // Arrange
        let anyonType = AnyonSpecies.AnyonType.Ising
        let genus = 1
        
        // Act
        let result = EntanglementEntropy.groundStateDegeneracy anyonType genus
        
        // Assert
        match result with
        | Ok gsd ->
            // Ising has 3 anyon types: {1, σ, ψ}
            Assert.Equal(3, gsd)
        | Error err ->
            Assert.Fail($"Should succeed, got error: {err.Message}")
    
    [<Fact>]
    let ``Fibonacci torus has GSD=2`` () =
        // Arrange
        let anyonType = AnyonSpecies.AnyonType.Fibonacci
        let genus = 1
        
        // Act
        let result = EntanglementEntropy.groundStateDegeneracy anyonType genus
        
        // Assert
        match result with
        | Ok gsd ->
            // Fibonacci has 2 anyon types: {1, τ}
            Assert.Equal(2, gsd)
        | Error err ->
            Assert.Fail($"Should succeed, got error: {err.Message}")
    
    [<Fact>]
    let ``Negative genus is invalid`` () =
        // Arrange
        let anyonType = AnyonSpecies.AnyonType.Ising
        let genus = -1
        
        // Act
        let result = EntanglementEntropy.groundStateDegeneracy anyonType genus
        
        // Assert
        match result with
        | Ok _ ->
            Assert.Fail("Should reject negative genus")
        | Error (TopologicalError.ValidationError (field, reason)) ->
            Assert.Contains("non-negative", reason)
        | Error err ->
            Assert.Fail($"Wrong error type: {err.Message}")
    
    [<Fact>]
    let ``GSD entropy for torus relates to topological entropy`` () =
        // Arrange
        let anyonType = AnyonSpecies.AnyonType.Ising
        let genus = 1
        
        // Act
        let gsdEntropy = EntanglementEntropy.groundStateDegeneracyEntropy anyonType genus
        let topoEntropy = EntanglementEntropy.topologicalEntropy anyonType
        
        // Assert
        match gsdEntropy, topoEntropy with
        | Ok sGSD, Ok gamma ->
            // For torus: GSD = 3, so S_GSD = log(3)
            // γ = log(2)
            // They're different but related
            Assert.True(sGSD > 0.0)
            Assert.True(gamma > 0.0)
        | _ ->
            Assert.Fail("Both should succeed")
    
    // ========================================================================
    // DISPLAY AND UTILITY TESTS
    // ========================================================================
    
    [<Fact>]
    let ``Display shows correct information for Ising`` () =
        // Arrange
        let anyonType = AnyonSpecies.AnyonType.Ising
        
        // Act
        let result = EntanglementEntropy.display anyonType
        
        // Assert
        match result with
        | Ok text ->
            Assert.Contains("Ising", text)
            Assert.Contains("0.693", text)  // log(2) ≈ 0.693
            Assert.Contains("Long-range entanglement: YES", text)
            Assert.Contains("Topological order: Present", text)
        | Error err ->
            Assert.Fail($"Should succeed, got error: {err.Message}")
    
    [<Fact>]
    let ``Compare different anyon theories`` () =
        // Arrange
        let ising = AnyonSpecies.AnyonType.Ising
        let fibonacci = AnyonSpecies.AnyonType.Fibonacci
        
        // Act
        let result = EntanglementEntropy.compare ising fibonacci
        
        // Assert
        match result with
        | Ok text ->
            Assert.Contains("Ising", text)
            Assert.Contains("Fibonacci", text)
            Assert.Contains("Ratio", text)
        | Error err ->
            Assert.Fail($"Should succeed, got error: {err.Message}")
    
    // ========================================================================
    // DENSITY MATRIX CONSTRUCTION
    // ========================================================================
    
    [<Fact>]
    let ``densityMatrix of single qubit |0⟩ is correct`` () =
        // |0⟩ = [1, 0] → ρ = [[1,0],[0,0]]
        let amps = [ System.Numerics.Complex.One; System.Numerics.Complex.Zero ]
        let rho = EntanglementEntropy.densityMatrix amps
        
        Assert.Equal(1.0, rho.[0, 0].Real, 10)
        Assert.Equal(0.0, rho.[0, 1].Real, 10)
        Assert.Equal(0.0, rho.[1, 0].Real, 10)
        Assert.Equal(0.0, rho.[1, 1].Real, 10)
    
    [<Fact>]
    let ``densityMatrix of |+⟩ state has equal off-diagonal elements`` () =
        // |+⟩ = [1/√2, 1/√2] → ρ = [[1/2, 1/2],[1/2, 1/2]]
        let s = 1.0 / sqrt 2.0
        let amps = [ System.Numerics.Complex(s, 0.0); System.Numerics.Complex(s, 0.0) ]
        let rho = EntanglementEntropy.densityMatrix amps
        
        Assert.Equal(0.5, rho.[0, 0].Real, 10)
        Assert.Equal(0.5, rho.[0, 1].Real, 10)
        Assert.Equal(0.5, rho.[1, 0].Real, 10)
        Assert.Equal(0.5, rho.[1, 1].Real, 10)
    
    [<Fact>]
    let ``densityMatrix trace equals 1 for normalized state`` () =
        let s = 1.0 / sqrt 2.0
        let amps = [ System.Numerics.Complex(s, 0.0); System.Numerics.Complex(0.0, s) ]
        let rho = EntanglementEntropy.densityMatrix amps
        
        let trace = rho.[0, 0].Real + rho.[1, 1].Real
        Assert.Equal(1.0, trace, 10)
    
    // ========================================================================
    // PARTIAL TRACE
    // ========================================================================
    
    [<Fact>]
    let ``partialTraceB of product state returns subsystem A density matrix`` () =
        // |ψ⟩ = |0⟩_A ⊗ |0⟩_B = [1, 0, 0, 0]
        // ρ_A = Tr_B(|00⟩⟨00|) = |0⟩⟨0| = [[1,0],[0,0]]
        let amps = [
            System.Numerics.Complex.One
            System.Numerics.Complex.Zero
            System.Numerics.Complex.Zero
            System.Numerics.Complex.Zero
        ]
        let rho = EntanglementEntropy.densityMatrix amps
        
        match EntanglementEntropy.partialTraceB rho 2 2 with
        | Ok rhoA ->
            Assert.Equal(1.0, rhoA.[0, 0].Real, 10)
            Assert.Equal(0.0, rhoA.[0, 1].Real, 10)
            Assert.Equal(0.0, rhoA.[1, 0].Real, 10)
            Assert.Equal(0.0, rhoA.[1, 1].Real, 10)
        | Error err -> Assert.Fail($"Expected Ok, got: {err.Message}")
    
    [<Fact>]
    let ``partialTraceB of Bell state returns maximally mixed state`` () =
        // |Φ+⟩ = (|00⟩ + |11⟩)/√2 = [1/√2, 0, 0, 1/√2]
        // ρ_A = Tr_B(|Φ+⟩⟨Φ+|) = I/2
        let s = 1.0 / sqrt 2.0
        let amps = [
            System.Numerics.Complex(s, 0.0)
            System.Numerics.Complex.Zero
            System.Numerics.Complex.Zero
            System.Numerics.Complex(s, 0.0)
        ]
        let rho = EntanglementEntropy.densityMatrix amps
        
        match EntanglementEntropy.partialTraceB rho 2 2 with
        | Ok rhoA ->
            Assert.Equal(0.5, rhoA.[0, 0].Real, 8)
            Assert.Equal(0.0, rhoA.[0, 1].Real, 8)
            Assert.Equal(0.0, rhoA.[1, 0].Real, 8)
            Assert.Equal(0.5, rhoA.[1, 1].Real, 8)
        | Error err -> Assert.Fail($"Expected Ok, got: {err.Message}")
    
    [<Fact>]
    let ``partialTraceA of Bell state returns maximally mixed state`` () =
        let s = 1.0 / sqrt 2.0
        let amps = [
            System.Numerics.Complex(s, 0.0)
            System.Numerics.Complex.Zero
            System.Numerics.Complex.Zero
            System.Numerics.Complex(s, 0.0)
        ]
        let rho = EntanglementEntropy.densityMatrix amps
        
        match EntanglementEntropy.partialTraceA rho 2 2 with
        | Ok rhoB ->
            Assert.Equal(0.5, rhoB.[0, 0].Real, 8)
            Assert.Equal(0.0, rhoB.[0, 1].Real, 8)
            Assert.Equal(0.0, rhoB.[1, 0].Real, 8)
            Assert.Equal(0.5, rhoB.[1, 1].Real, 8)
        | Error err -> Assert.Fail($"Expected Ok, got: {err.Message}")
    
    [<Fact>]
    let ``partialTraceB with mismatched dimensions returns error`` () =
        let amps = [
            System.Numerics.Complex.One
            System.Numerics.Complex.Zero
            System.Numerics.Complex.Zero
            System.Numerics.Complex.Zero
        ]
        let rho = EntanglementEntropy.densityMatrix amps
        
        // 4x4 matrix but dimA=3, dimB=2 → 3*2=6 ≠ 4
        match EntanglementEntropy.partialTraceB rho 3 2 with
        | Error (TopologicalError.ValidationError (_, _)) -> ()
        | _ -> Assert.Fail("Expected validation error for mismatched dimensions")
    
    [<Fact>]
    let ``partialTraceB trace preservation holds`` () =
        // Tr(ρ_A) = Tr(ρ_AB) = 1 for normalized states
        let s = 1.0 / sqrt 2.0
        let amps = [
            System.Numerics.Complex(s, 0.0)
            System.Numerics.Complex.Zero
            System.Numerics.Complex.Zero
            System.Numerics.Complex(s, 0.0)
        ]
        let rho = EntanglementEntropy.densityMatrix amps
        
        match EntanglementEntropy.partialTraceB rho 2 2 with
        | Ok rhoA ->
            let trace = rhoA.[0, 0].Real + rhoA.[1, 1].Real
            Assert.Equal(1.0, trace, 8)
        | Error err -> Assert.Fail($"Expected Ok, got: {err.Message}")
    
    // ========================================================================
    // EIGENVALUE DECOMPOSITION
    // ========================================================================
    
    [<Fact>]
    let ``eigenvaluesHermitian of identity matrix returns all ones`` () =
        let n = 3
        let identity = Array2D.init n n (fun i j ->
            if i = j then System.Numerics.Complex.One else System.Numerics.Complex.Zero)
        
        match EntanglementEntropy.eigenvaluesHermitian identity 100 1e-12 with
        | Ok eigenvals ->
            Assert.Equal(3, eigenvals.Length)
            eigenvals |> List.iter (fun v -> Assert.Equal(1.0, v, 8))
        | Error err -> Assert.Fail($"Expected Ok, got: {err.Message}")
    
    [<Fact>]
    let ``eigenvaluesHermitian of diagonal matrix returns diagonal entries`` () =
        let matrix = Array2D.init 2 2 (fun i j ->
            if i = 0 && j = 0 then System.Numerics.Complex(0.7, 0.0)
            elif i = 1 && j = 1 then System.Numerics.Complex(0.3, 0.0)
            else System.Numerics.Complex.Zero)
        
        match EntanglementEntropy.eigenvaluesHermitian matrix 100 1e-12 with
        | Ok eigenvals ->
            Assert.Equal(2, eigenvals.Length)
            // Sorted descending
            Assert.Equal(0.7, eigenvals.[0], 8)
            Assert.Equal(0.3, eigenvals.[1], 8)
        | Error err -> Assert.Fail($"Expected Ok, got: {err.Message}")
    
    [<Fact>]
    let ``eigenvaluesHermitian of 1x1 matrix returns single value`` () =
        let matrix = Array2D.init 1 1 (fun _ _ -> System.Numerics.Complex(0.42, 0.0))
        match EntanglementEntropy.eigenvaluesHermitian matrix 100 1e-12 with
        | Ok eigenvals ->
            Assert.Equal(1, eigenvals.Length)
            Assert.Equal(0.42, eigenvals.[0], 10)
        | Error err -> Assert.Fail($"Expected Ok, got: {err.Message}")
    
    [<Fact>]
    let ``eigenvaluesHermitian of empty matrix returns empty`` () =
        let matrix = Array2D.create 0 0 System.Numerics.Complex.Zero
        match EntanglementEntropy.eigenvaluesHermitian matrix 100 1e-12 with
        | Ok eigenvals -> Assert.Empty(eigenvals)
        | Error err -> Assert.Fail($"Expected Ok, got: {err.Message}")
    
    [<Fact>]
    let ``eigenvaluesHermitian of maximally mixed 2x2 returns equal eigenvalues`` () =
        // I/2 = [[0.5, 0], [0, 0.5]]
        let matrix = Array2D.init 2 2 (fun i j ->
            if i = j then System.Numerics.Complex(0.5, 0.0) else System.Numerics.Complex.Zero)
        
        match EntanglementEntropy.eigenvaluesHermitian matrix 100 1e-12 with
        | Ok eigenvals ->
            Assert.Equal(2, eigenvals.Length)
            Assert.Equal(0.5, eigenvals.[0], 8)
            Assert.Equal(0.5, eigenvals.[1], 8)
        | Error err -> Assert.Fail($"Expected Ok, got: {err.Message}")
    
    // ========================================================================
    // VON NEUMANN ENTROPY FROM DENSITY MATRIX
    // ========================================================================
    
    [<Fact>]
    let ``vonNeumannEntropyFromDensityMatrix of pure state returns zero`` () =
        // |0⟩⟨0| has eigenvalues [1, 0] → S = 0
        let rho = Array2D.init 2 2 (fun i j ->
            if i = 0 && j = 0 then System.Numerics.Complex.One else System.Numerics.Complex.Zero)
        
        match EntanglementEntropy.vonNeumannEntropyFromDensityMatrix rho with
        | Ok result ->
            Assert.Equal(0.0, result.Entropy, 8)
        | Error err -> Assert.Fail($"Expected Ok, got: {err.Message}")
    
    [<Fact>]
    let ``vonNeumannEntropyFromDensityMatrix of maximally mixed state returns log(2)`` () =
        // I/2 has eigenvalues [0.5, 0.5] → S = log(2)
        let rho = Array2D.init 2 2 (fun i j ->
            if i = j then System.Numerics.Complex(0.5, 0.0) else System.Numerics.Complex.Zero)
        
        match EntanglementEntropy.vonNeumannEntropyFromDensityMatrix rho with
        | Ok result ->
            Assert.Equal(log 2.0, result.Entropy, 6)
            Assert.Equal(1.0, result.EntropyBits, 6)
        | Error err -> Assert.Fail($"Expected Ok, got: {err.Message}")
    
    // ========================================================================
    // ENTANGLEMENT ENTROPY (FULL PIPELINE)
    // ========================================================================
    
    [<Fact>]
    let ``entanglementEntropy of product state is zero`` () =
        // |00⟩ = [1, 0, 0, 0] → no entanglement → S = 0
        let amps = [
            System.Numerics.Complex.One
            System.Numerics.Complex.Zero
            System.Numerics.Complex.Zero
            System.Numerics.Complex.Zero
        ]
        match EntanglementEntropy.entanglementEntropy amps 2 2 with
        | Ok result ->
            Assert.Equal(0.0, result.Entropy, 6)
        | Error err -> Assert.Fail($"Expected Ok, got: {err.Message}")
    
    [<Fact>]
    let ``entanglementEntropy of Bell state is log(2)`` () =
        // |Φ+⟩ = (|00⟩ + |11⟩)/√2 → maximally entangled → S = log(2)
        let s = 1.0 / sqrt 2.0
        let amps = [
            System.Numerics.Complex(s, 0.0)
            System.Numerics.Complex.Zero
            System.Numerics.Complex.Zero
            System.Numerics.Complex(s, 0.0)
        ]
        match EntanglementEntropy.entanglementEntropy amps 2 2 with
        | Ok result ->
            Assert.Equal(log 2.0, result.Entropy, 4)
            Assert.Equal(1.0, result.EntropyBits, 4)
        | Error err -> Assert.Fail($"Expected Ok, got: {err.Message}")
    
    [<Fact>]
    let ``entanglementEntropy with mismatched dimensions returns error`` () =
        // 4 amplitudes but dimA=3, dimB=2 → error
        let amps = [
            System.Numerics.Complex.One
            System.Numerics.Complex.Zero
            System.Numerics.Complex.Zero
            System.Numerics.Complex.Zero
        ]
        match EntanglementEntropy.entanglementEntropy amps 3 2 with
        | Error (TopologicalError.ValidationError (_, _)) -> ()
        | _ -> Assert.Fail("Expected validation error for mismatched dimensions")
    
    [<Fact>]
    let ``entanglementEntropy of partially entangled state is between 0 and log(2)`` () =
        // |ψ⟩ = cos(π/8)|00⟩ + sin(π/8)|11⟩ → partial entanglement
        let c = cos (System.Math.PI / 8.0)
        let s = sin (System.Math.PI / 8.0)
        let amps = [
            System.Numerics.Complex(c, 0.0)
            System.Numerics.Complex.Zero
            System.Numerics.Complex.Zero
            System.Numerics.Complex(s, 0.0)
        ]
        match EntanglementEntropy.entanglementEntropy amps 2 2 with
        | Ok result ->
            Assert.True(result.Entropy > 0.0, "Should have non-zero entropy")
            Assert.True(result.Entropy < log 2.0, "Should be less than maximally entangled")
        | Error err -> Assert.Fail($"Expected Ok, got: {err.Message}")
    
    // ========================================================================
    // INTEGRATION WITH ANYON SPECIES
    // ========================================================================
    
    [<Fact>]
    let ``Topological entropy uses correct total quantum dimension`` () =
        // Arrange
        let anyonType = AnyonSpecies.AnyonType.Ising
        
        // Act
        let gamma = EntanglementEntropy.topologicalEntropy anyonType
        let D = AnyonSpecies.totalQuantumDimension anyonType
        
        // Assert
        match gamma, D with
        | Ok g, Ok d ->
            let expectedGamma = log d
            Assert.InRange(g, expectedGamma - 1e-10, expectedGamma + 1e-10)
        | _ ->
            Assert.Fail("Both should succeed")
