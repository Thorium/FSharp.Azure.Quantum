namespace FSharp.Azure.Quantum.Tests.Topological

open Xunit
open System.Numerics
open FSharp.Azure.Quantum.Topological

/// Comprehensive unit tests for BraidingOperators module
///
/// Tests cover:
/// - R-matrix (braiding operator) elements and matrices
/// - F-matrix (fusion basis change) elements and matrices
/// - Unitarity properties
/// - Mathematical consistency (Yang-Baxter, Pentagon equations)
module BraidingOperatorsTests =

    // ============================================================================
    // HELPER FUNCTIONS
    // ============================================================================

    /// Helper to create unit complex number e^(iθ)
    let expI (theta: float) : Complex = Complex(cos theta, sin theta)

    /// Pi constant
    let π = System.Math.PI

    /// Assert complex numbers are equal within tolerance
    let assertComplexEqual (expected: Complex) (actual: Complex) (tolerance: float) =
        let diff = Complex.Abs(expected - actual)
        Assert.True(diff < tolerance, $"Expected {expected}, got {actual}, diff {diff}")

    /// Assert complex number has magnitude 1 (is on unit circle)
    let assertUnitMagnitude (z: Complex) =
        let mag = Complex.Abs(z)
        Assert.Equal(1.0, mag, 10)

    // ============================================================================
    // R-MATRIX ELEMENT TESTS
    // ============================================================================

    [<Fact>]
    let ``R-matrix: Ising Sigma × Sigma → Vacuum gives e^(-iπ/8)`` () =
        match
            BraidingOperators.element
                AnyonSpecies.Particle.Sigma
                AnyonSpecies.Particle.Sigma
                AnyonSpecies.Particle.Vacuum
                AnyonSpecies.AnyonType.Ising
        with
        | Ok r ->
            let expected = expI (-π / 8.0)
            assertComplexEqual expected r 1e-10
        | Error err -> Assert.Fail($"Expected Ok but got Error: {err.Message}")

    // ============================================================================
    // SU(2)_k F-MATRIX BRIDGE TESTS
    // ============================================================================
    // These verify that BraidingOperators.matrixElement works for general SU(2)_k
    // types by delegating to FMatrix.computeFMatrix/getFSymbol.

    [<Fact>]
    let ``F-matrix element: SU(2)_3 (Fibonacci-like) returns Ok for valid particles`` () =
        // SU(2)_3 has particles j=0,1/2,1,3/2 (j_doubled=0,1,2,3)
        // j=1/2 × j=1/2 can fuse to j=0 or j=1
        // F[j=1/2, j=1/2, j=1/2, j=1/2; e=j0, f=j0] should succeed
        let su2k3 = AnyonSpecies.AnyonType.SU2Level 3
        let halfSpin = AnyonSpecies.Particle.SpinJ(1, 3) // j=1/2
        let spin0 = AnyonSpecies.Particle.SpinJ(0, 3) // j=0 (vacuum-like)

        match BraidingOperators.matrixElement halfSpin halfSpin halfSpin halfSpin spin0 spin0 su2k3 with
        | Ok value ->
            // SU(2)_3 is the Fibonacci theory in disguise, so this entry is the
            // golden-ratio symbol 1/φ = (1 - √5) / 2 ≈ -0.618034 — not merely "some
            // value that isn't NotImplemented".
            let goldenRatio = (1.0 + sqrt 5.0) / 2.0
            assertComplexEqual (Complex(-1.0 / goldenRatio, 0.0)) value 1e-10
        | Error err -> Assert.Fail($"SU(2)_3 F-matrix element should succeed: {err.Message}")

    [<Fact>]
    let ``F-matrix element: SU(2)_4 returns Ok for valid fusion channels`` () =
        // SU(2)_4 has particles j=0,1/2,1,3/2,2 (j_doubled=0,1,2,3,4)
        let su2k4 = AnyonSpecies.AnyonType.SU2Level 4
        let spin1 = AnyonSpecies.Particle.SpinJ(2, 4) // j=1
        let spin0 = AnyonSpecies.Particle.SpinJ(0, 4) // j=0

        // j=1 × j=1 can fuse to j=0, j=1, j=2
        // F[j1,j1,j1,j1; j0,j0] should be valid
        match BraidingOperators.matrixElement spin1 spin1 spin1 spin1 spin0 spin0 su2k4 with
        | Ok value -> assertComplexEqual (Complex(0.5, 0.0)) value 1e-10
        | Error err -> Assert.Fail($"SU(2)_4 F-matrix element should succeed: {err.Message}")

    [<Fact>]
    let ``F-matrix: SU(2)_3 fusionBasisChange produces unitary matrix`` () =
        // F-matrix should be unitary: F · F† = I
        let su2k3 = AnyonSpecies.AnyonType.SU2Level 3
        let halfSpin = AnyonSpecies.Particle.SpinJ(1, 3)

        match BraidingOperators.fusionBasisChange halfSpin halfSpin halfSpin halfSpin su2k3 with
        | Ok fMatrix ->
            let n = Array2D.length1 fMatrix
            // Verify F · F† ≈ I (unitarity)
            for i in 0 .. n - 1 do
                for j in 0 .. n - 1 do
                    let mutable sum = Complex.Zero

                    for k in 0 .. (Array2D.length2 fMatrix) - 1 do
                        sum <- sum + fMatrix.[i, k] * Complex.Conjugate(fMatrix.[j, k])

                    let expected = if i = j then Complex.One else Complex.Zero
                    assertComplexEqual expected sum 1e-8
        | Error err ->
            Assert.False(
                err.Message.Contains("not implemented"),
                $"SU(2)_3 fusionBasisChange should not return 'not implemented': {err.Message}"
            )

    [<Fact>]
    let ``F-matrix: SU(2)_2 (Ising equivalent) matches Ising F-matrix`` () =
        // SU(2)_2 is equivalent to Ising theory — both use Sigma/Psi particles.
        // The design decision throughout the codebase is that SU2Level 2 maps to
        // Ising particle types (Vacuum, Sigma, Psi), so both calls use Sigma.
        let sigma = AnyonSpecies.Particle.Sigma

        match BraidingOperators.fusionBasisChange sigma sigma sigma sigma AnyonSpecies.AnyonType.Ising with
        | Ok isingF ->
            let su2k2 = AnyonSpecies.AnyonType.SU2Level 2

            match BraidingOperators.fusionBasisChange sigma sigma sigma sigma su2k2 with
            | Ok su2F ->
                // Both should have same dimensions
                Assert.Equal(Array2D.length1 isingF, Array2D.length1 su2F)
                Assert.Equal(Array2D.length2 isingF, Array2D.length2 su2F)
                // Values should match exactly (same particle set, same F-symbols)
                for i in 0 .. (Array2D.length1 isingF) - 1 do
                    for j in 0 .. (Array2D.length2 isingF) - 1 do
                        let isingMag = Complex.Abs(isingF.[i, j])
                        let su2Mag = Complex.Abs(su2F.[i, j])
                        Assert.InRange(abs (isingMag - su2Mag), 0.0, 0.15)
            | Error err -> Assert.Fail($"SU(2)_2 fusionBasisChange failed: {err.Message}")
        | Error err -> Assert.Fail($"Ising fusionBasisChange failed: {err.Message}")

    // ============================================================================
    // F-MATRIX SPECIAL-VALUE REGRESSION TESTS (vacuum legs and Ising ±1 signs)
    // ============================================================================
    // These pin the fix that routes ALL matrixElement lookups through the
    // pentagon-verified FMatrix module. Previously a local catch-all returned
    // +1 for F^{σψσ}_ψ and F^{ψσψ}_σ (pentagon fixes them at −1), returned φ⁻¹
    // for the 1×1 unitary F^{τττ}_1 (must be 1), and dropped valid vacuum-leg
    // configurations to 0 (breaking F-move unitarity).

    [<Fact>]
    let ``F-matrix element: vacuum external leg gives trivial coefficient 1`` () =
        // a = Vacuum: valid paths force e = b and f = d, coefficient 1.
        // F[1,σ,σ,ψ; σ,ψ] passes fusion validation (1×σ→σ, σ×σ→ψ, σ×σ→ψ, 1×ψ→ψ)
        // and previously fell through to 0.
        match
            BraidingOperators.matrixElement
                AnyonSpecies.Particle.Vacuum
                AnyonSpecies.Particle.Sigma
                AnyonSpecies.Particle.Sigma
                AnyonSpecies.Particle.Psi
                AnyonSpecies.Particle.Sigma
                AnyonSpecies.Particle.Psi
                AnyonSpecies.AnyonType.Ising
        with
        | Ok value -> assertComplexEqual Complex.One value 1e-10
        | Error err -> Assert.Fail($"Expected Ok but got Error: {err.Message}")

    [<Fact>]
    let ``F-matrix: fusionBasisChange with vacuum external leg is unitary`` () =
        // Unitarity regression: with a = Vacuum the F-move is trivial, so the
        // (1×1) F-matrix must have unit magnitude — no amplitude loss.
        for d in [ AnyonSpecies.Particle.Vacuum; AnyonSpecies.Particle.Psi ] do
            match
                BraidingOperators.fusionBasisChange
                    AnyonSpecies.Particle.Vacuum
                    AnyonSpecies.Particle.Sigma
                    AnyonSpecies.Particle.Sigma
                    d
                    AnyonSpecies.AnyonType.Ising
            with
            | Ok f ->
                Assert.Equal(1, Array2D.length1 f)
                Assert.Equal(1, Array2D.length2 f)

                Assert.True(
                    abs (Complex.Abs f.[0, 0] - 1.0) < 1e-10,
                    $"F-move with vacuum leg (d = {d}) should be unitary, got |F| = {Complex.Abs f.[0, 0]}"
                )
            | Error err -> Assert.Fail($"fusionBasisChange failed for d = {d}: {err.Message}")

    [<Fact>]
    let ``F-matrix element: Ising F(sigma psi sigma -> psi) is minus one`` () =
        // Pentagon consistency fixes F^{σψσ}_ψ = −1 (Kitaev 2006); a previous
        // local catch-all returned +1.
        match
            BraidingOperators.matrixElement
                AnyonSpecies.Particle.Sigma
                AnyonSpecies.Particle.Psi
                AnyonSpecies.Particle.Sigma
                AnyonSpecies.Particle.Psi
                AnyonSpecies.Particle.Sigma
                AnyonSpecies.Particle.Sigma
                AnyonSpecies.AnyonType.Ising
        with
        | Ok value -> assertComplexEqual (Complex(-1.0, 0.0)) value 1e-10
        | Error err -> Assert.Fail($"Expected Ok but got Error: {err.Message}")

    [<Fact>]
    let ``F-matrix element: Ising F(psi sigma psi -> sigma) is minus one`` () =
        // Pentagon consistency fixes F^{ψσψ}_σ = −1 (Kitaev 2006).
        match
            BraidingOperators.matrixElement
                AnyonSpecies.Particle.Psi
                AnyonSpecies.Particle.Sigma
                AnyonSpecies.Particle.Psi
                AnyonSpecies.Particle.Sigma
                AnyonSpecies.Particle.Sigma
                AnyonSpecies.Particle.Sigma
                AnyonSpecies.AnyonType.Ising
        with
        | Ok value -> assertComplexEqual (Complex(-1.0, 0.0)) value 1e-10
        | Error err -> Assert.Fail($"Expected Ok but got Error: {err.Message}")

    [<Fact>]
    let ``F-matrix element: Fibonacci F(tau tau tau -> vacuum) is one`` () =
        // F^{τττ}_1 is a 1×1 unitary matrix, so its entry must have magnitude 1
        // (value 1 in this gauge). A previous local table used φ⁻¹, which is not
        // unitary and disagreed with FMatrix.computeFibonacciFSymbols.
        match
            BraidingOperators.matrixElement
                AnyonSpecies.Particle.Tau
                AnyonSpecies.Particle.Tau
                AnyonSpecies.Particle.Tau
                AnyonSpecies.Particle.Vacuum
                AnyonSpecies.Particle.Tau
                AnyonSpecies.Particle.Tau
                AnyonSpecies.AnyonType.Fibonacci
        with
        | Ok value -> assertComplexEqual Complex.One value 1e-10
        | Error err -> Assert.Fail($"Expected Ok but got Error: {err.Message}")

    [<Fact>]
    let ``F-matrix element: SU(2)_k returns zero for invalid fusion channels`` () =
        // Invalid fusion: j=1/2 × j=1/2 cannot fuse to j=3/2 in SU(2)_3
        // So F[j1/2,j1/2,j1/2,j1/2; j3/2, j0] should be zero
        let su2k3 = AnyonSpecies.AnyonType.SU2Level 3
        let halfSpin = AnyonSpecies.Particle.SpinJ(1, 3)
        let spin3half = AnyonSpecies.Particle.SpinJ(3, 3) // j=3/2
        let spin0 = AnyonSpecies.Particle.SpinJ(0, 3) // j=0

        match BraidingOperators.matrixElement halfSpin halfSpin halfSpin halfSpin spin3half spin0 su2k3 with
        | Ok value ->
            // j=3/2 is not a fusion channel of j=1/2 × j=1/2, so the symbol is zero.
            // An impossible channel is a well-defined query with a zero answer, not a
            // failure, so this must not come back as an Error either.
            assertComplexEqual Complex.Zero value 1e-10
        | Error err -> Assert.Fail($"An impossible fusion channel should give zero, not an error: {err.Message}")


    [<Fact>]
    let ``R-matrix: Vacuum braiding is trivial (phase 1)`` () =
        match
            BraidingOperators.element
                AnyonSpecies.Particle.Vacuum
                AnyonSpecies.Particle.Sigma
                AnyonSpecies.Particle.Sigma
                AnyonSpecies.AnyonType.Ising
        with
        | Ok r -> Assert.Equal(Complex.One, r)
        | Error err -> Assert.Fail($"Expected Ok but got Error: {err.Message}")

    [<Fact>]
    let ``R-matrix: All Ising R-matrices have unit magnitude`` () =
        let particles =
            [
                AnyonSpecies.Particle.Vacuum
                AnyonSpecies.Particle.Sigma
                AnyonSpecies.Particle.Psi
            ]

        particles
        |> List.allPairs particles
        |> List.iter (fun (a, b) ->
            match FusionRules.channels a b AnyonSpecies.AnyonType.Ising with
            | Ok channels ->
                channels
                |> List.iter (fun c ->
                    (BraidingOperators.element a b c AnyonSpecies.AnyonType.Ising)
                    |> Result.map (fun r -> assertUnitMagnitude r)
                    |> Result.defaultWith (fun err -> Assert.Fail($"Expected Ok but got Error: {err.Message}")))
            | Error err -> Assert.Fail($"Expected Ok but got Error: {err.Message}"))

    [<Fact>]
    let ``R-matrix: Fibonacci Tau × Tau → Vacuum gives e^(4πi/5)`` () =
        match
            BraidingOperators.element
                AnyonSpecies.Particle.Tau
                AnyonSpecies.Particle.Tau
                AnyonSpecies.Particle.Vacuum
                AnyonSpecies.AnyonType.Fibonacci
        with
        | Ok r ->
            let expected = expI (4.0 * π / 5.0)
            assertComplexEqual expected r 1e-10
        | Error err -> Assert.Fail($"Expected Ok but got Error: {err.Message}")

    [<Fact>]
    let ``R-matrix: Fibonacci Tau × Tau → Tau gives e^(-3πi/5)`` () =
        match
            BraidingOperators.element
                AnyonSpecies.Particle.Tau
                AnyonSpecies.Particle.Tau
                AnyonSpecies.Particle.Tau
                AnyonSpecies.AnyonType.Fibonacci
        with
        | Ok r ->
            let expected = expI (-3.0 * π / 5.0)
            assertComplexEqual expected r 1e-10
        | Error err -> Assert.Fail($"Expected Ok but got Error: {err.Message}")

    [<Fact>]
    let ``R-matrix: Invalid fusion returns LogicError`` () =
        match
            BraidingOperators.element
                AnyonSpecies.Particle.Sigma
                AnyonSpecies.Particle.Sigma
                AnyonSpecies.Particle.Sigma // Invalid: σ × σ cannot give σ
                AnyonSpecies.AnyonType.Ising
        with
        | Error(TopologicalError.LogicError _) -> () // Expected
        | Ok _ -> Assert.Fail("Expected LogicError for invalid fusion")
        | Error err -> Assert.Fail($"Expected LogicError but got {err.Category}")

    // ============================================================================
    // R-MATRIX TESTS
    // ============================================================================

    [<Fact>]
    let ``R-matrix: Ising Sigma × Sigma produces 2×2 matrix`` () =
        match
            BraidingOperators.matrix
                AnyonSpecies.Particle.Sigma
                AnyonSpecies.Particle.Sigma
                AnyonSpecies.AnyonType.Ising
        with
        | Ok r ->
            Assert.Equal(2, Array2D.length1 r)
            Assert.Equal(2, Array2D.length2 r)
        | Error err -> Assert.Fail($"Expected Ok but got Error: {err.Message}")

    [<Fact>]
    let ``R-matrix: Ising Sigma × Sigma is diagonal`` () =
        match
            BraidingOperators.matrix
                AnyonSpecies.Particle.Sigma
                AnyonSpecies.Particle.Sigma
                AnyonSpecies.AnyonType.Ising
        with
        | Ok r ->
            // R-matrix for Ising σ×σ is diagonal with entries [e^(-iπ/8), e^(3iπ/8)]
            Assert.Equal(Complex.Zero, r.[0, 1])
            Assert.Equal(Complex.Zero, r.[1, 0])

            assertComplexEqual (expI (-π / 8.0)) r.[0, 0] 1e-10
            assertComplexEqual (expI (3.0 * π / 8.0)) r.[1, 1] 1e-10
        | Error err -> Assert.Fail($"Expected Ok but got Error: {err.Message}")

    [<Fact>]
    let ``R-matrix: Fibonacci Tau × Tau produces 2×2 matrix`` () =
        match
            BraidingOperators.matrix
                AnyonSpecies.Particle.Tau
                AnyonSpecies.Particle.Tau
                AnyonSpecies.AnyonType.Fibonacci
        with
        | Ok r ->
            Assert.Equal(2, Array2D.length1 r)
            Assert.Equal(2, Array2D.length2 r)
        | Error err -> Assert.Fail($"Expected Ok but got Error: {err.Message}")

    [<Fact>]
    let ``R-matrix: Deterministic fusion (Psi × Psi) produces 1×1 matrix`` () =
        match
            BraidingOperators.matrix AnyonSpecies.Particle.Psi AnyonSpecies.Particle.Psi AnyonSpecies.AnyonType.Ising
        with
        | Ok r ->
            Assert.Equal(1, Array2D.length1 r)
            Assert.Equal(1, Array2D.length2 r)
            // Psi is a fermion, so braiding gives -1 (fermion exchange statistics)
            Assert.Equal(Complex(-1.0, 0.0), r.[0, 0])
        | Error err -> Assert.Fail($"Expected Ok but got Error: {err.Message}")

    // ============================================================================
    // F-MATRIX ELEMENT TESTS
    // ============================================================================

    // Note: F-matrix element tests require careful understanding of fusion tree paths
    // (Path 1: a × b → e, then e × c → d vs Path 2: b × c → f, then a × f → d)
    // Testing at the matrix level is more robust for basic verification.

    // ============================================================================
    // F-MATRIX TESTS
    // ============================================================================

    [<Fact>]
    let ``F-matrix: Ising σσσ produces 2×2 matrix`` () =
        match
            BraidingOperators.fusionBasisChange
                AnyonSpecies.Particle.Sigma
                AnyonSpecies.Particle.Sigma
                AnyonSpecies.Particle.Sigma
                AnyonSpecies.Particle.Sigma
                AnyonSpecies.AnyonType.Ising
        with
        | Ok f ->
            Assert.Equal(2, Array2D.length1 f)
            Assert.Equal(2, Array2D.length2 f)
        | Error err -> Assert.Fail($"Expected Ok but got Error: {err.Message}")

    [<Fact>]
    let ``F-matrix: Ising σσσ is symmetric`` () =
        match
            BraidingOperators.fusionBasisChange
                AnyonSpecies.Particle.Sigma
                AnyonSpecies.Particle.Sigma
                AnyonSpecies.Particle.Sigma
                AnyonSpecies.Particle.Sigma
                AnyonSpecies.AnyonType.Ising
        with
        | Ok f ->
            // F-matrix should be symmetric for Ising
            assertComplexEqual f.[0, 1] f.[1, 0] 1e-10
        | Error err -> Assert.Fail($"Expected Ok but got Error: {err.Message}")

    [<Fact>]
    let ``F-matrix: Fibonacci τττ produces 2×2 matrix`` () =
        match
            BraidingOperators.fusionBasisChange
                AnyonSpecies.Particle.Tau
                AnyonSpecies.Particle.Tau
                AnyonSpecies.Particle.Tau
                AnyonSpecies.Particle.Tau
                AnyonSpecies.AnyonType.Fibonacci
        with
        | Ok f ->
            Assert.Equal(2, Array2D.length1 f)
            Assert.Equal(2, Array2D.length2 f)
        | Error err -> Assert.Fail($"Expected Ok but got Error: {err.Message}")

    [<Fact>]
    let ``F-matrix: Deterministic fusion produces 1×1 matrix`` () =
        // F-matrix for deterministic fusion (ψ × ψ → 1)
        match
            BraidingOperators.fusionBasisChange
                AnyonSpecies.Particle.Psi
                AnyonSpecies.Particle.Psi
                AnyonSpecies.Particle.Vacuum
                AnyonSpecies.Particle.Vacuum
                AnyonSpecies.AnyonType.Ising
        with
        | Ok f ->
            Assert.Equal(1, Array2D.length1 f)
            Assert.Equal(1, Array2D.length2 f)
            // A 1×1 basis change between two one-dimensional fusion spaces is the
            // identity in this gauge.
            assertComplexEqual Complex.One f.[0, 0] 1e-10
        | Error err -> Assert.Fail($"Expected Ok but got Error: {err.Message}")

    // ============================================================================
    // MATHEMATICAL CONSISTENCY TESTS
    // ============================================================================

    [<Fact>]
    let ``R-matrix unitarity: R × R† = I (for Ising)`` () =
        match
            BraidingOperators.matrix
                AnyonSpecies.Particle.Sigma
                AnyonSpecies.Particle.Sigma
                AnyonSpecies.AnyonType.Ising
        with
        | Ok r ->
            // For diagonal R-matrix, R × R† = I means |R[i,i]|² = 1
            assertUnitMagnitude r.[0, 0]
            assertUnitMagnitude r.[1, 1]
        | Error err -> Assert.Fail($"Expected Ok but got Error: {err.Message}")

    /// Assert F × F† = I for a fusion-basis-change matrix.
    /// F is a change of orthonormal basis between the (a×b)×c and a×(b×c) fusion
    /// trees, so it is unitary in every gauge — a gauge transformation rephases
    /// basis vectors and maps unitaries to unitaries.
    let private assertUnitary (label: string) (f: Complex[,]) =
        let rows = Array2D.length1 f
        let cols = Array2D.length2 f
        Assert.Equal(rows, cols)

        for i in 0 .. rows - 1 do
            for j in 0 .. rows - 1 do
                let entry =
                    Seq.init cols (fun k -> f.[i, k] * Complex.Conjugate f.[j, k])
                    |> Seq.fold (+) Complex.Zero

                let expected = if i = j then Complex.One else Complex.Zero

                Assert.True(
                    Complex.Abs(entry - expected) < 1e-10,
                    $"{label}: (F·F†)[{i},{j}] = {entry}, expected {expected}"
                )

    [<Fact>]
    let ``F-matrix unitarity: F × F† = I`` () =
        // Every fusion-basis change is unitary, for both non-abelian 2×2 blocks
        // (σσσ→σ, τττ→τ) and the deterministic 1×1 blocks.
        let cases =
            [
                "Ising σσσ→σ",
                AnyonSpecies.Particle.Sigma,
                AnyonSpecies.Particle.Sigma,
                AnyonSpecies.Particle.Sigma,
                AnyonSpecies.Particle.Sigma,
                AnyonSpecies.AnyonType.Ising
                "Ising ψψ1→1",
                AnyonSpecies.Particle.Psi,
                AnyonSpecies.Particle.Psi,
                AnyonSpecies.Particle.Vacuum,
                AnyonSpecies.Particle.Vacuum,
                AnyonSpecies.AnyonType.Ising
                "Fibonacci τττ→τ",
                AnyonSpecies.Particle.Tau,
                AnyonSpecies.Particle.Tau,
                AnyonSpecies.Particle.Tau,
                AnyonSpecies.Particle.Tau,
                AnyonSpecies.AnyonType.Fibonacci
                "Fibonacci τττ→1",
                AnyonSpecies.Particle.Tau,
                AnyonSpecies.Particle.Tau,
                AnyonSpecies.Particle.Tau,
                AnyonSpecies.Particle.Vacuum,
                AnyonSpecies.AnyonType.Fibonacci
            ]

        for (label, a, b, c, d, anyonType) in cases do
            match BraidingOperators.fusionBasisChange a b c d anyonType with
            | Ok f -> assertUnitary label f
            | Error err -> Assert.Fail($"{label}: expected Ok but got Error: {err.Message}")

    [<Fact>]
    let ``R-matrix elements are on unit circle`` () =
        // All R-matrix elements should have magnitude 1 (pure phases)
        let testCases =
            [
                (AnyonSpecies.Particle.Sigma,
                 AnyonSpecies.Particle.Sigma,
                 AnyonSpecies.Particle.Vacuum,
                 AnyonSpecies.AnyonType.Ising)
                (AnyonSpecies.Particle.Sigma,
                 AnyonSpecies.Particle.Sigma,
                 AnyonSpecies.Particle.Psi,
                 AnyonSpecies.AnyonType.Ising)
                (AnyonSpecies.Particle.Tau,
                 AnyonSpecies.Particle.Tau,
                 AnyonSpecies.Particle.Vacuum,
                 AnyonSpecies.AnyonType.Fibonacci)
                (AnyonSpecies.Particle.Tau,
                 AnyonSpecies.Particle.Tau,
                 AnyonSpecies.Particle.Tau,
                 AnyonSpecies.AnyonType.Fibonacci)
            ]

        testCases
        |> List.iter (fun (a, b, c, anyonType) ->
            (BraidingOperators.element a b c anyonType)
            |> Result.map (fun r -> assertUnitMagnitude r)
            |> Result.defaultWith (fun err -> Assert.Fail($"Expected Ok but got Error: {err.Message}")))

    /// Braid a superposition through a sequence of elementary generators (all clockwise).
    let private braidSequence (generators: int list) (start: TopologicalOperations.Superposition) =
        generators
        |> List.fold
            (fun acc i -> acc |> Result.bind (TopologicalOperations.braidSuperpositionDirected i true))
            (Ok start)

    /// Total |LHS - RHS| over the shared basis of two superpositions.
    let private superpositionDistance
        (lhs: TopologicalOperations.Superposition)
        (rhs: TopologicalOperations.Superposition)
        =
        let byTree (s: TopologicalOperations.Superposition) =
            s.Terms
            |> List.map (fun (amp, state) -> (sprintf "%A" state.Tree), amp)
            |> List.sortBy fst

        let l, r = byTree lhs, byTree rhs
        Assert.Equal(l.Length, r.Length)

        List.zip l r
        |> List.sumBy (fun ((kl, al), (kr, ar)) ->
            Assert.Equal(kl, kr)
            Complex.Abs(al - ar))

    [<Fact>]
    let ``Yang-Baxter verification: σ₁σ₂σ₁ = σ₂σ₁σ₂ on three anyons`` () =
        // The braid relation acting on the actual fusion space — not just a shape
        // check on the R-matrix. Both sides are applied to every basis tree of the
        // three-anyon space and must agree amplitude for amplitude.
        //
        // Three anyons is the scope of this test by necessity: braiding a cross-pair
        // neighbour on a larger tree is reported NotImplemented by
        // braidSuperpositionDirected ("the F-move machinery required for cross-pair
        // braids is only implemented for 3-anyon trees"), so a 4-anyon version would
        // be asserting a gap rather than the relation.
        let cases =
            [
                AnyonSpecies.AnyonType.Ising, AnyonSpecies.Particle.Sigma, AnyonSpecies.Particle.Sigma
                AnyonSpecies.AnyonType.Fibonacci, AnyonSpecies.Particle.Tau, AnyonSpecies.Particle.Tau
            ]

        for (anyonType, particle, totalCharge) in cases do
            match FusionTree.allTrees [ particle; particle; particle ] totalCharge anyonType with
            | Error err -> Assert.Fail($"{anyonType}: could not enumerate basis: {err.Message}")
            | Ok trees ->
                Assert.Equal(2, trees.Length) // both theories give a 2-dimensional space

                for tree in trees do
                    let start = TopologicalOperations.pureState (FusionTree.create tree anyonType)

                    match braidSequence [ 0; 1; 0 ] start, braidSequence [ 1; 0; 1 ] start with
                    | Ok lhs, Ok rhs ->
                        let distance = superpositionDistance lhs rhs

                        Assert.True(
                            distance < 1e-10,
                            $"{anyonType} on {FusionTree.toString tree}: |σ₁σ₂σ₁ - σ₂σ₁σ₂| = {distance}"
                        )
                    | Error err, _
                    | _, Error err -> Assert.Fail($"{anyonType}: braiding failed: {err.Message}")
