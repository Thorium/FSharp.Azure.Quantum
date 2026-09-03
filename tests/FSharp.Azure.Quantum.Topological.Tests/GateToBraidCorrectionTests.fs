namespace FSharp.Azure.Quantum.Topological.Tests

open Xunit
open FSharp.Azure.Quantum.Topological
open FSharp.Azure.Quantum
open System

/// Tests for the CORRECTED GateToBraid implementation.
/// These tests validate the physically correct Ising anyon braid conventions:
///   - One clockwise braid = S gate (relative phase π/2), NOT T gate
///   - Two clockwise braids = Z gate (relative phase π), NOT identity
///   - T gate is NOT exact in Ising braiding (requires non-topological supplementation)
/// Reference: Simon "Topological Quantum" Eq. 10.9-10.10, Section 11.2.4
module GateToBraidCorrectionTests =

    // ========================================================================
    // PHYSICALLY CORRECT: S Gate = 1 Braid (Exact)
    // ========================================================================
    
    [<Fact>]
    let ``S gate compiles to single clockwise braid (exact)`` () =
        // Physics: One Ising braid produces relative phase e^{iπ/2} = i = S gate
        // This is EXACT — no approximation needed.
        // Qubit q occupies fusion-tree leaves (2q, 2q+1), so the within-pair
        // exchange for qubit 1 is braid generator index 2 (LEAF indexing —
        // previously the qubit index was used directly, so gates on qubit > 0
        // silently braided the wrong anyons).
        match GateToBraid.sGateToBraid 1 3 with
        | Error err -> failwith $"Unexpected error: {err.Message}"
        | Ok braid ->
            Assert.Equal(1, braid.Generators.Length)
            Assert.True(braid.Generators.[0].IsClockwise)
            Assert.Equal(2, braid.Generators.[0].Index)  // leaf index 2·q = 2

    [<Fact>]
    let ``S† gate compiles to single counter-clockwise braid (exact)`` () =
        match GateToBraid.sDaggerGateToBraid 0 2 with
        | Error err -> failwith $"Unexpected error: {err.Message}"
        | Ok braid ->
            Assert.Equal(1, braid.Generators.Length)
            Assert.False(braid.Generators.[0].IsClockwise)

    // ========================================================================
    // PHYSICALLY CORRECT: Z Gate = 2 Braids (Exact)
    // ========================================================================
    
    [<Fact>]
    let ``Z gate compiles to two clockwise braids (S² = Z)`` () =
        // Physics: Two Ising braids produce relative phase (e^{iπ/2})² = e^{iπ} = -1 = Z
        // This is EXACT and is a RELATIVE phase (physically meaningful)
        match GateToBraid.zGateToBraid 0 2 with
        | Error err -> failwith $"Unexpected error: {err.Message}"
        | Ok braid ->
            Assert.Equal(2, braid.Generators.Length)
            Assert.Equal(6, braid.StrandCount)  // σ-pair encoding: 2 qubits → 2·(2+1) = 6 strands
            Assert.All(braid.Generators, fun gen -> Assert.True(gen.IsClockwise))
            Assert.All(braid.Generators, fun gen -> Assert.Equal(0, gen.Index))  // qubit 0 → leaf 0

    [<Fact>]
    let ``Z gate compilation has no special warnings`` () =
        // Z is now a proper exact braid compilation, no warnings needed
        let gate = CircuitBuilder.Gate.Z 0
        
        match GateToBraid.compileGateToBraid gate 2 1e-10 with
        | Error err -> failwith $"Unexpected error: {err.Message}"
        | Ok decomp ->
            Assert.True(decomp.DecompositionNotes.IsNone,
                $"Z gate should have no warnings, got: {decomp.DecompositionNotes}")
            Assert.Equal(0.0, decomp.ApproximationError)

    // ========================================================================
    // PHYSICALLY CORRECT: T Gate is NOT Exact
    // ========================================================================

    [<Fact>]
    let ``T gate errors when compiled to braid (not exact for Ising anyons)`` () =
        // Physics: T gate requires relative phase π/4, but one Ising braid gives π/2.
        // T gate is NOT in the Clifford group reachable by Ising braiding.
        // Reference: Simon "Topological Quantum" §11.2.4
        (GateToBraid.tGateToBraid 0 2) |> Result.map (fun _ -> failwith "T gate should NOT compile to exact braid for Ising anyons") |> Result.defaultWith (fun err -> Assert.Contains("not exact", err.Message))

    [<Fact>]
    let ``T† gate errors when compiled to braid (not exact for Ising anyons)`` () =
        (GateToBraid.tDaggerGateToBraid 0 2) |> Result.map (fun _ -> failwith "T† gate should NOT compile to exact braid for Ising anyons") |> Result.defaultWith (fun err -> Assert.Contains("not exact", err.Message))

    // ========================================================================
    // ANGLE NORMALIZATION (updated for braidPhase = π/2)
    // ========================================================================
    
    [<Fact>]
    let ``Rz with negative angle uses counter-clockwise braiding`` () =
        // Rz(-π/2) should produce 1 counter-clockwise braid
        let angleNeg = -Math.PI / 2.0
        let anglePos = Math.PI / 2.0
        
        match GateToBraid.rzGateToBraid 0 angleNeg 2 1e-10, GateToBraid.rzGateToBraid 0 anglePos 2 1e-10 with
        | Ok braidNeg, Ok braidPos ->
            // Both should produce 1 braid (optimal direction)
            Assert.Equal(1, braidNeg.Generators.Length)
            Assert.Equal(1, braidPos.Generators.Length)
            
            // Negative angle → counter-clockwise, positive → clockwise
            Assert.False(braidNeg.Generators.[0].IsClockwise)
            Assert.True(braidPos.Generators.[0].IsClockwise)
        | _ -> failwith "Rz compilation should succeed"
    
    [<Fact>]
    let ``Rz zero angle produces empty braid`` () =
        (GateToBraid.rzGateToBraid 0 0.0 2 1e-10) |> Result.map (fun braid -> Assert.Empty(braid.Generators)) |> Result.defaultWith (fun err -> failwith $"Unexpected error: {err.Message}")
    
    [<Fact>]
    let ``Rz 2π equals identity (normalized to 0)`` () =
        let angle = 2.0 * Math.PI
        
        match GateToBraid.rzGateToBraid 0 angle 2 1e-10 with
        | Error err -> failwith $"Unexpected error: {err.Message}"
        | Ok braid ->
            // Should normalize to ~0, producing 0 braids
            Assert.InRange(braid.Generators.Length, 0, 1)
    
    // ========================================================================
    // ERROR COMPUTATION (updated for braidPhase = π/2)
    // ========================================================================
    
    [<Fact>]
    let ``Rz error is computed correctly with normalization`` () =
        // With braidPhase=π/2≈1.571, angle=0.5 rounds to n=0, error ≈ 0.5
        let angle = 0.5
        let gate = CircuitBuilder.Gate.RZ (0, angle)
        
        // Use tolerance 0.6 (larger than expected error ~0.5)
        match GateToBraid.compileGateToBraid gate 2 0.6 with
        | Error err -> failwith $"Unexpected error: {err.Message}"
        | Ok decomp ->
            // Error should be non-zero
            Assert.True(decomp.ApproximationError > 0.0)
            
            // Error should be less than π/4 (half the distance between braid phases at π/2 spacing)
            Assert.True(decomp.ApproximationError < Math.PI / 4.0)
    
    [<Fact>]
    let ``Rz fails when error exceeds tolerance`` () =
        let angle = 0.5  // Approximation error ~0.5 (not a multiple of π/2)
        let strictTolerance = 1e-10
        let gate = CircuitBuilder.Gate.RZ (0, angle)
        
        match GateToBraid.compileGateToBraid gate 2 strictTolerance with
        | Ok _ -> failwith "Should fail with strict tolerance"
        | Error (TopologicalError.ComputationError (operation, context)) ->
            Assert.Contains("approximation error", context)
            Assert.Contains("exceeds tolerance", context)
        | Error _ -> failwith "Wrong error type"
    
    [<Fact>]
    let ``Exact Rz multiples of π/2 have zero error`` () =
        // With braidPhase = π/2, exact angles are multiples of π/2
        let exactAngles = [
            Math.PI / 2.0    // S (1 braid, 1 × π/2)
            Math.PI          // Z (2 braids, 2 × π/2)
            3.0 * Math.PI / 2.0  // S³ = S† (3 × π/2, normalized to -π/2 → 1 CCW braid)
        ]
        
        for angle in exactAngles do
            let gate = CircuitBuilder.Gate.RZ (0, angle)
            match GateToBraid.compileGateToBraid gate 2 1e-10 with
            | Error err -> failwith $"Exact angle {angle} should compile: {err.Message}"
            | Ok decomp ->
                Assert.True(decomp.ApproximationError < 1e-10, 
                    $"Angle {angle} should be exact, got error {decomp.ApproximationError}")

    // ========================================================================
    // ROUND-TRIP TESTS (Gate → Braid → Gate)
    // ========================================================================
    
    [<Fact>]
    let ``Round-trip - S gate to braid and back`` () =
        // S → 1 braid → S
        let originalGate = CircuitBuilder.Gate.S 0
        
        match GateToBraid.compileGateToBraid originalGate 2 1e-10 with
        | Error err -> failwith $"Gate to braid failed: {err.Message}"
        | Ok decomp ->
            Assert.Equal(1, decomp.BraidSequence.Length)
            Assert.Equal(1, decomp.BraidSequence.[0].Generators.Length)
            
            // Braid → Gate
            match BraidToGate.compileToGates 
                    decomp.BraidSequence.[0] 
                    AnyonSpecies.AnyonType.Ising 
                    BraidToGate.defaultOptions with
            | Error err -> failwith $"Braid to gate failed: {err.Message}"
            | Ok gateSeq ->
                Assert.Equal(1, gateSeq.Gates.Length)
                Assert.Equal(originalGate, gateSeq.Gates.[0])

    [<Fact>]
    let ``Round-trip - Z gate to braid produces two S gates`` () =
        // Z → 2 braids → [S; S]
        let zGate = CircuitBuilder.Gate.Z 0
        
        match GateToBraid.compileGateToBraid zGate 2 1e-10 with
        | Error err -> failwith $"Z to braid failed: {err.Message}"
        | Ok decomp ->
            Assert.Equal(1, decomp.BraidSequence.Length)
            Assert.Equal(2, decomp.BraidSequence.[0].Generators.Length)
            
            // Compile back to gates
            match BraidToGate.compileToGates 
                    decomp.BraidSequence.[0] 
                    AnyonSpecies.AnyonType.Ising 
                    BraidToGate.defaultOptions with
            | Error err -> failwith $"Braid to gate failed: {err.Message}"
            | Ok gateSeq ->
                // Should produce 2 S gates (which together = Z)
                Assert.Equal(2, gateSeq.Gates.Length)
                Assert.Equal(CircuitBuilder.Gate.S 0, gateSeq.Gates.[0])
                Assert.Equal(CircuitBuilder.Gate.S 0, gateSeq.Gates.[1])

    // ========================================================================
    // SOLOVAY-KITAEV APPROXIMATION TESTS
    // ========================================================================
    
    [<Fact>]
    let ``Hadamard braid compilation returns explicit error (off-diagonal, unreachable)`` () =
        // Ising within-pair braids generate only the DIAGONAL group {I, S, Z, S†},
        // so the off-diagonal H can never be approximated by braiding. Previously
        // the Solovay-Kitaev residual error was silently ignored and a diagonal
        // braid far from H was returned with reported success — the compilation
        // must fail explicitly instead. (The TopologicalBackend implements H on
        // Ising via an exact amplitude-level intercept.)
        let hGate = CircuitBuilder.Gate.H 0

        (GateToBraid.compileGateToBraid hGate 2 1e-10) |> Result.map (fun _ -> failwith "H braid compilation should fail explicitly for Ising anyons") |> Result.defaultWith (fun err -> Assert.Contains("diagonal", err.Message))

    [<Fact>]
    let ``CNOT braid compilation returns explicit error (requires off-diagonal H)`` () =
        // CNOT = H·CZ·H requires the Hadamard, which is unreachable by Ising
        // within-pair braiding — the error must propagate instead of emitting a
        // braid whose unitary is far from CNOT. (The TopologicalBackend executes
        // CNOT on Ising via an exact amplitude-level intercept.)
        let cnotGate = CircuitBuilder.Gate.CNOT (0, 1)

        (GateToBraid.compileGateToBraid cnotGate 2 1e-3) |> Result.map (fun _ -> failwith "CNOT braid compilation should fail explicitly for Ising anyons") |> Result.defaultWith (fun err -> Assert.False(System.String.IsNullOrWhiteSpace err.Message))

    // ========================================================================
    // COMPILATION SUMMARY TESTS
    // ========================================================================

    [<Fact>]
    let ``Z gate compilation summary has no warnings`` () =
        // Z gates are now exact braids, no warnings produced
        let gateSeq : BraidToGate.GateSequence = {
            Gates = [CircuitBuilder.Gate.Z 0; CircuitBuilder.Gate.Z 1]
            NumQubits = 2
            TotalPhase = System.Numerics.Complex(1.0, 0.0)
            Depth = 1
            TCount = 0
        }
        
        match GateToBraid.compileGateSequence gateSeq 1e-10 AnyonSpecies.AnyonType.Ising with
        | Error err -> failwith $"Unexpected error: {err.Message}"
        | Ok compilation ->
            // Z gates should produce no warnings (they're exact now)
            Assert.Equal(0, compilation.CompilationWarnings.Length)
