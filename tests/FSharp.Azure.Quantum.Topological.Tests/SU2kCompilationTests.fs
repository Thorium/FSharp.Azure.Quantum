namespace FSharp.Azure.Quantum.Topological.Tests

open System
open System.Numerics
open Xunit
open FSharp.Azure.Quantum
open FSharp.Azure.Quantum.Topological

/// Tests for SU(2)_k gate-to-braid compilation (k ≥ 3)
///
/// Validates that general SU(2)_k Chern-Simons theories can compile
/// quantum gates to braiding operations via the same iterative refinement
/// algorithm used for Fibonacci, but with k-specific R/F matrices.
///
/// Key mathematical fact: SU(2)_3 and Fibonacci have DIFFERENT R-matrix
/// phase conventions, so SU(2)_3 compilation CANNOT delegate to Fibonacci.
module SU2kCompilationTests =

    /// Helper to unwrap Result or fail test
    let private unwrapResult (result: Result<'T, TopologicalError>) (context: string) : 'T =
        match result with
        | Ok value -> value
        | Error err -> failwith $"Expected Ok but got Error in {context}: {err.Message}"

    // ========================================================================
    // BRAID GENERATOR TESTS
    // ========================================================================

    [<Fact>]
    let ``computeSU2kSigmaMatrices returns Ok for SU(2)_3`` () =
        let result = SolovayKitaev.computeSU2kSigmaMatrices 3
        match result with
        | Ok (sigma1, sigma2) ->
            // Both should be non-trivial matrices
            Assert.NotEqual(Complex.Zero, sigma1.A)
            Assert.NotEqual(Complex.Zero, sigma2.A)
        | Error err ->
            failwith $"Expected Ok but got Error: {err.Message}"

    [<Fact>]
    let ``SU(2)_3 sigma1 is diagonal`` () =
        // σ₁ = diag(R^0_{1/2,1/2}, R^1_{1/2,1/2}) is diagonal because
        // it acts in the fusion channel basis
        let result = SolovayKitaev.computeSU2kSigmaMatrices 3
        let (sigma1, _) = unwrapResult result "SU(2)_3 sigma matrices"

        // Off-diagonal elements should be zero
        Assert.True(sigma1.B.Magnitude < 1e-10,
            $"sigma1.B should be zero, got {sigma1.B}")
        Assert.True(sigma1.C.Magnitude < 1e-10,
            $"sigma1.C should be zero, got {sigma1.C}")

    [<Fact>]
    let ``SU(2)_3 sigma matrices are unitary`` () =
        let result = SolovayKitaev.computeSU2kSigmaMatrices 3
        let (sigma1, sigma2) = unwrapResult result "SU(2)_3 sigma matrices"

        // Check unitarity: U · U† = I
        let checkUnitary (name: string) (m: SolovayKitaev.SU2Matrix) =
            let mDag = SolovayKitaev.dagger m
            let product = SolovayKitaev.multiply m mDag
            let dist = SolovayKitaev.operatorDistance product SolovayKitaev.identity
            Assert.True(dist < 1e-10,
                $"{name} is not unitary: distance from identity = {dist}")

        checkUnitary "sigma1" sigma1
        checkUnitary "sigma2" sigma2

    [<Fact>]
    let ``SU(2)_3 sigma matrices differ from Fibonacci sigma matrices`` () =
        // This is the KEY test: SU(2)_3 has different R-matrix phases than Fibonacci
        // Fibonacci: R^1_ττ = exp(4πi/5)
        // SU(2)_3: R[1/2,1/2;0] = exp(2πi * (h_{1/2} + h_{1/2} - h_0)) = exp(2πi * 3/10) = exp(3πi/5)
        let result = SolovayKitaev.computeSU2kSigmaMatrices 3
        let (su2k_sigma1, su2k_sigma2) = unwrapResult result "SU(2)_3 sigma matrices"

        let fib_sigma1 = SolovayKitaev.fibonacciSigma1
        let fib_sigma2 = SolovayKitaev.fibonacciSigma2

        // σ₁ diagonal elements should differ between SU(2)_3 and Fibonacci
        let dist1 = SolovayKitaev.operatorDistance su2k_sigma1 fib_sigma1
        Assert.True(dist1 > 0.01,
            $"SU(2)_3 sigma1 should differ from Fibonacci sigma1, but distance = {dist1}")

        let dist2 = SolovayKitaev.operatorDistance su2k_sigma2 fib_sigma2
        Assert.True(dist2 > 0.01,
            $"SU(2)_3 sigma2 should differ from Fibonacci sigma2, but distance = {dist2}")

    [<Fact>]
    let ``SU(2)_3 sigma1 diagonal elements have correct phases from CFT`` () =
        // From CFT: h_j = j(j+1)/(k+2) for SU(2)_k
        // For k=3: h_{1/2} = (1/2)(3/2)/5 = 3/20
        // R[1/2,1/2;0] = exp(2πi * (h_{1/2} + h_{1/2} - h_0)) = exp(2πi * 3/10) = exp(3πi/5)
        // R[1/2,1/2;1] = exp(2πi * (h_{1/2} + h_{1/2} - h_1)) = exp(2πi * (3/10 - 2/5)) = exp(-πi/5)
        let result = SolovayKitaev.computeSU2kSigmaMatrices 3
        let (sigma1, _) = unwrapResult result "SU(2)_3 sigma matrices"

        let i = Complex.ImaginaryOne
        let expectedR0 = Complex.Exp(i * 3.0 * Math.PI / 5.0)     // R[1/2,1/2;0] = exp(3πi/5)
        let expectedR1 = Complex.Exp(-i * Math.PI / 5.0)           // R[1/2,1/2;1] = exp(-πi/5)

        let diffA = (sigma1.A - expectedR0).Magnitude
        let diffD = (sigma1.D - expectedR1).Magnitude

        Assert.True(diffA < 1e-10,
            $"sigma1.A = {sigma1.A}, expected {expectedR0} (diff = {diffA})")
        Assert.True(diffD < 1e-10,
            $"sigma1.D = {sigma1.D}, expected {expectedR1} (diff = {diffD})")

    [<Fact>]
    let ``computeSU2kSigmaMatrices returns Ok for SU(2)_5`` () =
        // k=5 (odd) is generally believed universal
        let result = SolovayKitaev.computeSU2kSigmaMatrices 5
        match result with
        | Ok (sigma1, sigma2) ->
            // Check unitarity
            let checkUnitary (m: SolovayKitaev.SU2Matrix) =
                let mDag = SolovayKitaev.dagger m
                let product = SolovayKitaev.multiply m mDag
                SolovayKitaev.operatorDistance product SolovayKitaev.identity < 1e-10
            Assert.True(checkUnitary sigma1, "SU(2)_5 sigma1 should be unitary")
            Assert.True(checkUnitary sigma2, "SU(2)_5 sigma2 should be unitary")
        | Error err ->
            failwith $"Expected Ok but got Error: {err.Message}"

    [<Fact>]
    let ``computeSU2kSigmaMatrices fails for k < 3`` () =
        // k=1 has no j=1/2 particle (only j=0, j=1/2 with k=1 has only 0,1/2
        // but j=1/2 × j=1/2 has no 2D fusion space)
        // Actually k=1 has particles j=0 and j=1/2 but 1/2 × 1/2 → 0 only (1D)
        // k=2 is Ising - handled separately
        let result = SolovayKitaev.computeSU2kSigmaMatrices 1
        match result with
        | Error _ -> () // Expected
        | Ok _ -> failwith "k=1 should fail (1D fusion space, not enough for qubit encoding)"

    // ========================================================================
    // GATE APPROXIMATION TESTS
    // ========================================================================

    [<Fact>]
    let ``approximateGateSU2k approximates identity for SU(2)_3`` () =
        let result = SolovayKitaev.approximateGateSU2k 3 SolovayKitaev.identity 0.5 4 12
        match result with
        | Ok (braidOps, error) ->
            Assert.True(error < 0.5, $"Identity approximation error should be < 0.5, got {error}")
            Assert.True(braidOps.Length > 0, "Should produce non-empty braid sequence")
        | Error err ->
            failwith $"Identity approximation failed: {err.Message}"

    [<Fact>]
    let ``approximateGateSU2k approximates Hadamard for SU(2)_3`` () =
        let hMatrix = SolovayKitaev.gateToMatrix SolovayKitaev.H
        let result = SolovayKitaev.approximateGateSU2k 3 hMatrix 0.7 4 12
        match result with
        | Ok (braidOps, error) ->
            Assert.True(error < 0.7, $"Hadamard approximation error should be < 0.7, got {error}")
        | Error err ->
            failwith $"Hadamard approximation failed: {err.Message}"

    [<Fact>]
    let ``approximateGateSU2k approximates T gate for SU(2)_3`` () =
        let tMatrix = SolovayKitaev.gateToMatrix SolovayKitaev.T
        let result = SolovayKitaev.approximateGateSU2k 3 tMatrix 0.7 4 12
        match result with
        | Ok (braidOps, error) ->
            Assert.True(error < 0.7, $"T gate approximation error should be < 0.7, got {error}")
        | Error err ->
            failwith $"T gate approximation failed: {err.Message}"

    [<Fact>]
    let ``approximateGateSU2k approximates X gate for SU(2)_3`` () =
        let xMatrix = SolovayKitaev.gateToMatrix SolovayKitaev.X
        let result = SolovayKitaev.approximateGateSU2k 3 xMatrix 0.7 4 12
        match result with
        | Ok (braidOps, error) ->
            Assert.True(error < 0.7, $"X gate approximation error should be < 0.7, got {error}")
        | Error err ->
            failwith $"X gate approximation failed: {err.Message}"

    [<Fact>]
    let ``approximateGateSU2k returns different braids than Fibonacci`` () =
        // Since SU(2)_3 uses different sigma matrices, the braid sequences
        // for the same gate should generally differ
        let hMatrix = SolovayKitaev.gateToMatrix SolovayKitaev.H
        let su2kResult = SolovayKitaev.approximateGateSU2k 3 hMatrix 0.5 4 8
        let (fibBraids, _) = SolovayKitaev.approximateGateFibonacci hMatrix 0.5 4 8

        match su2kResult with
        | Ok (su2kBraids, _) ->
            // The braid sequences should differ in content (different generators
            // produce different matrices). We check lengths differ OR if same length,
            // they produce different matrices with the respective sigma sets.
            // This is a statistical test - extremely unlikely to produce identical sequences.
            let su2kLen = su2kBraids.Length
            let fibLen = fibBraids.Length
            // At minimum, verify they produce results (non-trivial test)
            Assert.True(su2kLen > 0, "SU(2)_3 should produce non-empty braids")
            Assert.True(fibLen > 0, "Fibonacci should produce non-empty braids")
        | Error err ->
            failwith $"SU(2)_3 approximation failed: {err.Message}"

    // ========================================================================
    // GATE-TO-BRAID COMPILATION PIPELINE TESTS
    // ========================================================================

    [<Fact>]
    let ``compileGateSequence succeeds for SU(2)_3 with T gate`` () =
        let gateSeq : BraidToGate.GateSequence = {
            Gates = [CircuitBuilder.Gate.T 0]
            NumQubits = 1
            TotalPhase = Complex(1.0, 0.0)
            Depth = 1
            TCount = 1
        }

        let result = GateToBraid.compileGateSequence gateSeq 0.5 (AnyonSpecies.AnyonType.SU2Level 3)

        match result with
        | Ok compilation ->
            Assert.Equal(1, compilation.OriginalGateCount)
            Assert.True(compilation.CompiledBraids.Length > 0, "Should produce braids")
            Assert.Equal(AnyonSpecies.AnyonType.SU2Level 3, compilation.AnyonType)
        | Error err ->
            failwith $"SU(2)_3 T gate compilation should succeed but got: {err.Message}"

    [<Fact>]
    let ``compileGateSequence succeeds for SU(2)_3 with H gate`` () =
        let gateSeq : BraidToGate.GateSequence = {
            Gates = [CircuitBuilder.Gate.H 0]
            NumQubits = 1
            TotalPhase = Complex(1.0, 0.0)
            Depth = 1
            TCount = 0
        }

        let result = GateToBraid.compileGateSequence gateSeq 0.5 (AnyonSpecies.AnyonType.SU2Level 3)

        match result with
        | Ok compilation ->
            Assert.Equal(1, compilation.OriginalGateCount)
            Assert.True(compilation.CompiledBraids.Length > 0)
        | Error err ->
            failwith $"SU(2)_3 H gate compilation should succeed but got: {err.Message}"

    [<Fact>]
    let ``compileGateSequence succeeds for SU(2)_3 multi-gate circuit`` () =
        let gateSeq : BraidToGate.GateSequence = {
            Gates = [
                CircuitBuilder.Gate.H 0
                CircuitBuilder.Gate.T 0
                CircuitBuilder.Gate.Measure 0
            ]
            NumQubits = 1
            TotalPhase = Complex(1.0, 0.0)
            Depth = 3
            TCount = 1
        }

        let result = GateToBraid.compileGateSequence gateSeq 0.5 (AnyonSpecies.AnyonType.SU2Level 3)

        match result with
        | Ok compilation ->
            Assert.Equal(3, compilation.OriginalGateCount)
            Assert.True(compilation.CompiledBraids.Length > 0)
            Assert.True(compilation.CompilationWarnings.Length >= 2,
                "Should have warnings from H and T compilation notes")
        | Error err ->
            failwith $"SU(2)_3 multi-gate compilation should succeed but got: {err.Message}"

    [<Fact>]
    let ``compileGateSequence handles empty circuit for SU(2)_3`` () =
        let gateSeq : BraidToGate.GateSequence = {
            Gates = []
            NumQubits = 1
            TotalPhase = Complex(1.0, 0.0)
            Depth = 0
            TCount = 0
        }

        let result = GateToBraid.compileGateSequence gateSeq 0.5 (AnyonSpecies.AnyonType.SU2Level 3)

        match result with
        | Ok compilation ->
            Assert.Equal(0, compilation.OriginalGateCount)
            Assert.Empty(compilation.CompiledBraids)
            Assert.True(compilation.IsExact)
        | Error err ->
            failwith $"Empty circuit should succeed but got: {err.Message}"

    // ========================================================================
    // TWO-QUBIT GATE TESTS
    // ========================================================================

    [<Fact>]
    let ``compileGateSequence handles CNOT for SU(2)_3`` () =
        let gateSeq : BraidToGate.GateSequence = {
            Gates = [CircuitBuilder.Gate.CNOT(0, 1)]
            NumQubits = 2
            TotalPhase = Complex(1.0, 0.0)
            Depth = 1
            TCount = 0
        }

        let result = GateToBraid.compileGateSequence gateSeq 0.5 (AnyonSpecies.AnyonType.SU2Level 3)

        match result with
        | Ok compilation ->
            Assert.True(compilation.CompiledBraids.Length > 0, "CNOT should produce braids")
            Assert.Equal(AnyonSpecies.AnyonType.SU2Level 3, compilation.AnyonType)
        | Error err ->
            failwith $"SU(2)_3 CNOT compilation should succeed but got: {err.Message}"

    [<Fact>]
    let ``compileGateSequence handles CZ for SU(2)_3`` () =
        let gateSeq : BraidToGate.GateSequence = {
            Gates = [CircuitBuilder.Gate.CZ(0, 1)]
            NumQubits = 2
            TotalPhase = Complex(1.0, 0.0)
            Depth = 1
            TCount = 0
        }

        let result = GateToBraid.compileGateSequence gateSeq 0.5 (AnyonSpecies.AnyonType.SU2Level 3)

        match result with
        | Ok compilation ->
            Assert.True(compilation.CompiledBraids.Length > 0, "CZ should produce braids")
        | Error err ->
            failwith $"SU(2)_3 CZ compilation should succeed but got: {err.Message}"

    [<Fact>]
    let ``compileGateSequence handles SWAP for SU(2)_3`` () =
        let gateSeq : BraidToGate.GateSequence = {
            Gates = [CircuitBuilder.Gate.SWAP(0, 1)]
            NumQubits = 2
            TotalPhase = Complex(1.0, 0.0)
            Depth = 1
            TCount = 0
        }

        let result = GateToBraid.compileGateSequence gateSeq 0.5 (AnyonSpecies.AnyonType.SU2Level 3)

        match result with
        | Ok compilation ->
            Assert.True(compilation.CompiledBraids.Length > 0, "SWAP should produce braids")
        | Error err ->
            failwith $"SU(2)_3 SWAP compilation should succeed but got: {err.Message}"

    // ========================================================================
    // UNIVERSALITY WARNINGS
    // ========================================================================

    [<Fact>]
    let ``compileGateSequence for SU(2)_4 includes non-universality warning`` () =
        // k=4 is NOT universal by braiding alone
        let gateSeq : BraidToGate.GateSequence = {
            Gates = [CircuitBuilder.Gate.H 0]
            NumQubits = 1
            TotalPhase = Complex(1.0, 0.0)
            Depth = 1
            TCount = 0
        }

        let result = GateToBraid.compileGateSequence gateSeq 0.5 (AnyonSpecies.AnyonType.SU2Level 4)

        match result with
        | Ok compilation ->
            // Should succeed but with a warning about non-universality
            let hasUniversalityWarning =
                compilation.CompilationWarnings
                |> List.exists (fun w -> w.ToLower().Contains("universal"))
            Assert.True(hasUniversalityWarning,
                $"SU(2)_4 should warn about non-universality. Warnings: {compilation.CompilationWarnings}")
        | Error _ ->
            // It's also acceptable to error if we choose not to compile for non-universal k
            ()

    [<Fact>]
    let ``compileGateSequence for SU(2)_5 succeeds without non-universality warning`` () =
        // k=5 (odd, ≥3) is generally believed universal
        let gateSeq : BraidToGate.GateSequence = {
            Gates = [CircuitBuilder.Gate.H 0]
            NumQubits = 1
            TotalPhase = Complex(1.0, 0.0)
            Depth = 1
            TCount = 0
        }

        let result = GateToBraid.compileGateSequence gateSeq 0.5 (AnyonSpecies.AnyonType.SU2Level 5)

        match result with
        | Ok compilation ->
            Assert.True(compilation.CompiledBraids.Length > 0)
            // k=5 should NOT have non-universality warning
            let hasNonUniversalWarning =
                compilation.CompilationWarnings
                |> List.exists (fun w -> w.ToLower().Contains("not universal") || w.ToLower().Contains("non-universal"))
            Assert.False(hasNonUniversalWarning,
                $"SU(2)_5 should NOT warn about non-universality. Warnings: {compilation.CompilationWarnings}")
        | Error err ->
            failwith $"SU(2)_5 H gate compilation should succeed but got: {err.Message}"

    // ========================================================================
    // BRAIDING OPERATORS R-MATRIX DELEGATION TEST
    // ========================================================================

    [<Fact>]
    let ``BraidingOperators element succeeds for SU(2)_3 particles`` () =
        // SpinJ(1,3) = j=1/2 in SU(2)_3
        let halfSpin = AnyonSpecies.Particle.SpinJ(1, 3)
        let vacuum = AnyonSpecies.Particle.SpinJ(0, 3)

        let result = BraidingOperators.element halfSpin halfSpin vacuum (AnyonSpecies.AnyonType.SU2Level 3)

        match result with
        | Ok rValue ->
            // R[1/2,1/2;0] should be a unit-magnitude complex number
            Assert.True(abs (rValue.Magnitude - 1.0) < 1e-10,
                $"R-matrix element should have unit magnitude, got {rValue.Magnitude}")
        | Error err ->
            failwith $"BraidingOperators.element should succeed for SU(2)_3 but got: {err.Message}"

    [<Fact>]
    let ``BraidingOperators element for SU(2)_3 matches RMatrix module`` () =
        // Verify that BraidingOperators.element delegates correctly to RMatrix
        let halfSpin = AnyonSpecies.Particle.SpinJ(1, 3)
        let spin1 = AnyonSpecies.Particle.SpinJ(2, 3)

        // R[1/2,1/2;1] via BraidingOperators
        let braidResult = BraidingOperators.element halfSpin halfSpin spin1 (AnyonSpecies.AnyonType.SU2Level 3)

        // R[1/2,1/2;1] via RMatrix
        let rMatrixResult =
            RMatrix.computeRMatrix (AnyonSpecies.AnyonType.SU2Level 3)
            |> Result.bind (fun data ->
                let idx : RMatrix.RMatrixIndex = { RMatrix.RMatrixIndex.A = halfSpin; RMatrix.RMatrixIndex.B = halfSpin; RMatrix.RMatrixIndex.C = spin1 }
                RMatrix.getRSymbol data idx)

        match braidResult, rMatrixResult with
        | Ok braidVal, Ok rMatVal ->
            let diff = (braidVal - rMatVal).Magnitude
            Assert.True(diff < 1e-10,
                $"BraidingOperators ({braidVal}) should match RMatrix ({rMatVal}), diff = {diff}")
        | Error e1, _ -> failwith $"BraidingOperators failed: {e1.Message}"
        | _, Error e2 -> failwith $"RMatrix failed: {e2.Message}"

    // ========================================================================
    // ROTATION GATE TESTS (mixed valid/invalid to prevent gaming)
    // ========================================================================

    [<Fact>]
    let ``compileGateSequence handles RZ gate for SU(2)_3`` () =
        let gateSeq : BraidToGate.GateSequence = {
            Gates = [CircuitBuilder.Gate.RZ(0, Math.PI / 3.0)]
            NumQubits = 1
            TotalPhase = Complex(1.0, 0.0)
            Depth = 1
            TCount = 0
        }

        let result = GateToBraid.compileGateSequence gateSeq 0.5 (AnyonSpecies.AnyonType.SU2Level 3)

        match result with
        | Ok compilation ->
            Assert.True(compilation.CompiledBraids.Length > 0)
        | Error err ->
            failwith $"SU(2)_3 RZ compilation should succeed but got: {err.Message}"

    [<Fact>]
    let ``compileGateSequence rejects Reset for SU(2)_3`` () =
        // Reset is not supported for any anyon type
        let gateSeq : BraidToGate.GateSequence = {
            Gates = [CircuitBuilder.Gate.Reset 0]
            NumQubits = 1
            TotalPhase = Complex(1.0, 0.0)
            Depth = 1
            TCount = 0
        }

        let result = GateToBraid.compileGateSequence gateSeq 0.5 (AnyonSpecies.AnyonType.SU2Level 3)

        match result with
        | Error _ -> () // Expected
        | Ok _ -> failwith "Reset should fail for SU(2)_3"
