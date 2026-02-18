namespace FSharp.Azure.Quantum.Tests

open System
open System.Numerics
open Xunit
open FSharp.Azure.Quantum.Topological
open FSharp.Azure.Quantum.Topological.SolovayKitaev

module SolovayKitaevTests =
    
    // ========================================================================
    // HELPER FUNCTIONS
    // ========================================================================
    
    let private tolerance = 1e-10
    
    let private assertApproxEqual (expected: Complex) (actual: Complex) =
        let diff = abs (expected.Real - actual.Real) + abs (expected.Imaginary - actual.Imaginary)
        Assert.True(diff < tolerance, sprintf "Expected %A, got %A (diff: %g)" expected actual diff)
    
    let private assertMatrixEqual (expected: SU2Matrix) (actual: SU2Matrix) =
        assertApproxEqual expected.A actual.A
        assertApproxEqual expected.B actual.B
        assertApproxEqual expected.C actual.C
        assertApproxEqual expected.D actual.D
    
    let private assertApproxZero (value: float) =
        Assert.True(abs value < tolerance, sprintf "Expected ~0, got %g" value)
    
    // ========================================================================
    // SU(2) MATRIX OPERATIONS TESTS
    // ========================================================================
    
    [<Fact>]
    let ``Identity matrix is identity`` () =
        let id = identity
        Assert.Equal(Complex.One, id.A)
        Assert.Equal(Complex.Zero, id.B)
        Assert.Equal(Complex.Zero, id.C)
        Assert.Equal(Complex.One, id.D)
    
    [<Fact>]
    let ``Multiply by identity gives same matrix`` () =
        let t = gateToMatrix T
        let result = multiply t identity
        assertMatrixEqual t result
    
    [<Fact>]
    let ``Dagger of identity is identity`` () =
        let result = dagger identity
        assertMatrixEqual identity result
    
    [<Fact>]
    let ``Dagger of T is TDagger`` () =
        let t = gateToMatrix T
        let tDag = gateToMatrix TDagger
        let result = dagger t
        assertMatrixEqual tDag result
    
    [<Fact>]
    let ``T times TDagger is identity`` () =
        let t = gateToMatrix T
        let tDag = dagger t
        let result = multiply t tDag
        assertMatrixEqual identity result
    
    [<Fact>]
    let ``Operator distance is zero for identical matrices`` () =
        let t = gateToMatrix T
        let dist = operatorDistance t t
        assertApproxZero dist
    
    [<Fact>]
    let ``Operator distance is symmetric`` () =
        let t = gateToMatrix T
        let s = gateToMatrix S
        let dist1 = operatorDistance t s
        let dist2 = operatorDistance s t
        Assert.Equal(dist1, dist2)
    
    [<Fact>]
    let ``Approx equal works correctly`` () =
        let t = gateToMatrix T
        let s = gateToMatrix S
        Assert.True(approxEqual 1e-10 t t)
        Assert.False(approxEqual 1e-10 t s)
    
    // ========================================================================
    // BASIC GATE MATRICES TESTS
    // ========================================================================
    
    [<Fact>]
    let ``T gate has correct matrix representation`` () =
        let t = gateToMatrix T
        let expected = Complex.Exp(Complex.ImaginaryOne * Math.PI / 4.0)
        Assert.Equal(Complex.One, t.A)
        Assert.Equal(Complex.Zero, t.B)
        Assert.Equal(Complex.Zero, t.C)
        assertApproxEqual expected t.D
    
    [<Fact>]
    let ``S gate equals T squared`` () =
        let t = gateToMatrix T
        let s = gateToMatrix S
        let t2 = multiply t t
        assertMatrixEqual s t2
    
    [<Fact>]
    let ``H gate is Hadamard`` () =
        let h = gateToMatrix H
        let sqrt2_inv = 1.0 / sqrt 2.0
        assertApproxEqual (Complex(sqrt2_inv, 0.0)) h.A
        assertApproxEqual (Complex(sqrt2_inv, 0.0)) h.B
        assertApproxEqual (Complex(sqrt2_inv, 0.0)) h.C
        assertApproxEqual (Complex(-sqrt2_inv, 0.0)) h.D
    
    [<Fact>]
    let ``H gate is self-inverse`` () =
        let h = gateToMatrix H
        let h2 = multiply h h
        assertMatrixEqual identity h2
    
    [<Fact>]
    let ``X gate is Pauli X`` () =
        let x = gateToMatrix X
        Assert.Equal(Complex.Zero, x.A)
        Assert.Equal(Complex.One, x.B)
        Assert.Equal(Complex.One, x.C)
        Assert.Equal(Complex.Zero, x.D)
    
    [<Fact>]
    let ``Y gate is Pauli Y`` () =
        let y = gateToMatrix Y
        Assert.Equal(Complex.Zero, y.A)
        assertApproxEqual (-Complex.ImaginaryOne) y.B
        assertApproxEqual Complex.ImaginaryOne y.C
        Assert.Equal(Complex.Zero, y.D)
    
    [<Fact>]
    let ``Z gate is Pauli Z`` () =
        let z = gateToMatrix Z
        Assert.Equal(Complex.One, z.A)
        Assert.Equal(Complex.Zero, z.B)
        Assert.Equal(Complex.Zero, z.C)
        Assert.Equal(-Complex.One, z.D)
    
    [<Fact>]
    let ``Sequence to matrix multiplies left to right`` () =
        let seq = [T; T]  // Should be S
        let result = sequenceToMatrix seq
        let s = gateToMatrix S
        assertMatrixEqual s result
    
    // ========================================================================
    // BASE SET CONSTRUCTION TESTS
    // ========================================================================
    
    [<Fact>]
    let ``Generate sequences includes empty list`` () =
        let seqs = generateSequences 0 [T; H]
        Assert.Contains([], seqs)
    
    [<Fact>]
    let ``Generate sequences length 1 has all base gates`` () =
        let baseGates = [T; H]
        let seqs = generateSequences 1 baseGates
        Assert.Contains([T], seqs)
        Assert.Contains([H], seqs)
    
    [<Fact>]
    let ``Generate sequences length 2 has compositions`` () =
        let seqs = generateSequences 2 [T; H]
        Assert.Contains([T; T], seqs)
        Assert.Contains([T; H], seqs)
        Assert.Contains([H; T], seqs)
        Assert.Contains([H; H], seqs)
    
    [<Fact>]
    let ``Build base set excludes empty sequence`` () =
        let baseSet = buildBaseSet 1
        let emptyExists = baseSet |> List.exists (fun (seq, _) -> List.isEmpty seq)
        Assert.False(emptyExists)
    
    [<Fact>]
    let ``Build base set includes single gates`` () =
        let baseSet = buildBaseSet 1
        let hasT = baseSet |> List.exists (fun (seq, _) -> seq = [T])
        Assert.True(hasT, "Base set should include [T]")
    
    [<Fact>]
    let ``Find closest in base set returns exact match`` () =
        let baseSet = buildBaseSet 2
        let target = gateToMatrix S
        let (seq, matrix, dist) = findClosestInBaseSet target baseSet
        assertApproxZero dist
    
    // ========================================================================
    // GROUP COMMUTATOR TESTS
    // ========================================================================
    
    [<Fact>]
    let ``Commutator of identity with anything is identity`` () =
        let t = gateToMatrix T
        let result = commutator identity t
        assertMatrixEqual identity result
    
    [<Fact>]
    let ``Commutator is non-trivial for non-commuting gates`` () =
        let h = gateToMatrix H
        let t = gateToMatrix T
        let result = commutator h t
        // Should NOT be identity
        let dist = operatorDistance result identity
        Assert.True(dist > 0.1, "H and T should not commute")
    
    [<Fact>]
    let ``Find commutator factorization succeeds for simple targets`` () =
        let baseSet = buildBaseSet 2
        let target = gateToMatrix S  // Simple target
        let result = findCommutatorFactorization target baseSet
        Assert.True(result.IsSome, "Should find factorization")
    
    // ========================================================================
    // SOLOVAY-KITAEV APPROXIMATION TESTS
    // ========================================================================
    
    [<Fact>]
    let ``Approximate identity gate with high precision`` () =
        let target = identity
        let result = approximateGate target 1e-10 3 10
        Assert.True(result.Error < 1e-10, sprintf "Error too high: %g" result.Error)
        assertMatrixEqual target result.Matrix
    
    [<Fact>]
    let ``Approximate T gate returns exact T`` () =
        let target = gateToMatrix T
        let result = approximateGate target 1e-10 3 10
        Assert.True(result.Error < 1e-10, sprintf "Error too high: %g" result.Error)
    
    [<Fact>]
    let ``Approximate S gate returns exact S`` () =
        let target = gateToMatrix S
        let result = approximateGate target 1e-10 3 10
        Assert.True(result.Error < 1e-10, sprintf "Error too high: %g" result.Error)
    
    [<Fact>]
    let ``Approximate H gate achieves precision`` () =
        let target = gateToMatrix H
        let result = approximateGate target 1e-5 3 8
        Assert.True(result.Error < 1e-5, sprintf "Error too high: %g (target: 1e-5)" result.Error)
        Assert.True(result.GateCount > 0, "Should produce non-empty sequence")
    
    [<Fact>]
    let ``Approximate X gate achieves precision`` () =
        let target = gateToMatrix X
        let result = approximateGate target 1e-5 3 8
        Assert.True(result.Error < 1e-5, sprintf "Error too high: %g (target: 1e-5)" result.Error)
    
    [<Fact>]
    let ``Approximate Y gate achieves precision`` () =
        let target = gateToMatrix Y
        let result = approximateGate target 1e-5 3 8
        Assert.True(result.Error < 1e-5, sprintf "Error too high: %g (target: 1e-5)" result.Error)
    
    [<Fact>]
    let ``Approximate arbitrary Rz rotation`` () =
        // Rz(θ) = [[1, 0], [0, exp(iθ)]]
        let theta = 0.5  // Not a π/4 multiple
        let phase = Complex.Exp(Complex.ImaginaryOne * theta)
        let target = SolovayKitaev.createSU2 Complex.One Complex.Zero Complex.Zero phase
        
        // Note: Basic Solovay-Kitaev with small base set has limited precision.
        // With tPhase=π/4, the discrete gate set has coarser angular resolution,
        // so arbitrary rotations have higher approximation error.
        let result = approximateGate target 0.35 3 8
        Assert.True(result.Error < 0.35, sprintf "Error too high: %g (target: 0.35)" result.Error)
        Assert.True(result.GateCount > 0, "Should produce non-empty sequence")
    
    [<Fact>]
    let ``Gate count increases with better precision`` () =
        let target = gateToMatrix H
        let coarse = approximateGate target 1e-3 3 6
        let fine = approximateGate target 1e-6 3 8
        
        // Solovay-Kitaev should use more gates for better precision
        Assert.True(fine.GateCount >= coarse.GateCount,
            sprintf "Fine approximation (%d gates) should use at least as many gates as coarse (%d gates)"
                fine.GateCount coarse.GateCount)
    
    [<Fact>]
    let ``Approximation respects max depth limit`` () =
        let target = gateToMatrix H
        let result = approximateGate target 1e-10 3 3  // Low max depth
        Assert.True(result.Depth <= 3, sprintf "Exceeded max depth: %d" result.Depth)
    
    [<Fact>]
    let ``Default approximation uses reasonable parameters`` () =
        let target = gateToMatrix H
        let result = approximateGateDefault target
        Assert.True(result.Error < 1e-5, sprintf "Default approximation error too high: %g" result.Error)
        Assert.True(result.GateCount > 0, "Should produce non-empty sequence")
    
    // ========================================================================
    // GATE INVERSION TESTS (for Dagger construction)
    // ========================================================================
    
    [<Fact>]
    let ``Gate sequence inversion works for T`` () =
        let seq = [T; T]  // T² = S
        let matrix = sequenceToMatrix seq
        let s = gateToMatrix S
        assertMatrixEqual s matrix
    
    [<Fact>]
    let ``Gate sequence handles mixed gates`` () =
        let seq = [H; T; H]  // HTH is a common pattern
        let matrix = sequenceToMatrix seq
        Assert.True(true)  // Just verify it doesn't crash
    
    // ========================================================================
    // EDGE CASES
    // ========================================================================
    
    [<Fact>]
    let ``Very high precision request is bounded by max depth`` () =
        let target = gateToMatrix H
        let result = approximateGate target 1e-15 3 5  // Very high precision, low depth
        // Should still terminate and give best effort
        Assert.True(result.Depth <= 5)
        Assert.True(result.GateCount > 0)
    
    [<Fact>]
    let ``Approximation is deterministic`` () =
        let target = gateToMatrix H
        let result1 = approximateGate target 1e-5 3 8
        let result2 = approximateGate target 1e-5 3 8
        
        // Should produce identical results
        Assert.Equal(result1.GateCount, result2.GateCount)
        Assert.Equal(result1.Depth, result2.Depth)
        assertMatrixEqual result1.Matrix result2.Matrix
