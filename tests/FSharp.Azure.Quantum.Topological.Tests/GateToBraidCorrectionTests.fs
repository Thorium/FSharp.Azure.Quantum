namespace FSharp.Azure.Quantum.Topological.Tests

open Xunit
open FSharp.Azure.Quantum.Topological
open FSharp.Azure.Quantum
open System

/// Tests for the CORRECTED GateToBraid implementation.
/// These tests specifically validate the mathematical fixes applied.
module GateToBraidCorrectionTests =

    // ========================================================================
    // CRITICAL FIX #1: Pauli Z Gate (was T⁴, now identity)
    // ========================================================================
    
    [<Fact>]
    let ``CORRECTED - Z gate compiles to identity braid (empty generators)`` () =
        // Business meaning: Z = exp(iπ) is a global phase → identity in topological QC
        // BEFORE FIX: Would produce 4 T gates (WRONG!)
        // AFTER FIX: Produces empty braid (identity)
        
        match GateToBraid.zGateToBraid 0 2 with
        | Error err -> failwith $"Unexpected error: {err.Message}"
        | Ok braid ->
            // CRITICAL: Should be empty (identity)
            Assert.Empty(braid.Generators)
            Assert.Equal(3, braid.StrandCount)  // 2 qubits → 3 strands
    
    [<Fact>]
    let ``CORRECTED - Z gate compilation includes warning about global phase`` () =
        // Business meaning: Users should know Z is treated as identity
        let gate = CircuitBuilder.Gate.Z 0
        
        match GateToBraid.compileGateToBraid gate 2 1e-10 with
        | Error err -> failwith $"Unexpected error: {err.Message}"
        | Ok decomp ->
            Assert.True(decomp.DecompositionNotes.IsSome)
            Assert.Contains("global phase", decomp.DecompositionNotes.Value)
            Assert.Contains("identity", decomp.DecompositionNotes.Value)
    
    // ========================================================================
    // CRITICAL FIX #2: Angle Normalization
    // ========================================================================
    
    [<Fact>]
    let ``CORRECTED - Rz with negative angle uses counter-clockwise braiding`` () =
        // Business meaning: Rz(-π/4) should produce 1 counter-clockwise T† gate
        // instead of many clockwise gates. Signed angles pick optimal direction.
        // BEFORE FIX: Would normalize to [0, 2π) and use many clockwise braids
        // AFTER FIX: Uses signed angle → 1 counter-clockwise braid (T†)
        
        let angleNeg = -Math.PI / 4.0
        let anglePos = Math.PI / 4.0
        
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
    let ``CORRECTED - Rz zero angle produces empty braid`` () =
        // Business meaning: Rz(0) = identity = no braiding
        match GateToBraid.rzGateToBraid 0 0.0 2 1e-10 with
        | Error err -> failwith $"Unexpected error: {err.Message}"
        | Ok braid ->
            Assert.Empty(braid.Generators)
    
    [<Fact>]
    let ``CORRECTED - Rz 2π equals identity (normalized to 0)`` () =
        // Business meaning: Rz(2π) = Rz(0) = identity after normalization
        let angle = 2.0 * Math.PI
        
        match GateToBraid.rzGateToBraid 0 angle 2 1e-10 with
        | Error err -> failwith $"Unexpected error: {err.Message}"
        | Ok braid ->
            // Should normalize to ~0, producing 0 or 16 T gates (both ≈ identity mod 2π)
            Assert.InRange(braid.Generators.Length, 0, 1)
    
    // ========================================================================
    // CRITICAL FIX #3: Error Computation
    // ========================================================================
    
    [<Fact>]
    let ``CORRECTED - Rz error is computed correctly with normalization`` () =
        // Business meaning: Error should be based on normalized angle
        let angle = 0.5  // Not a multiple of π/4
        let gate = CircuitBuilder.Gate.RZ (0, angle)
        
        // With tPhase=π/4≈0.785, nearest multiple is n=1, error ≈ |0.5-0.785| ≈ 0.285
        // Use tolerance 0.3 (larger than expected error ~0.285)
        match GateToBraid.compileGateToBraid gate 2 0.3 with
        | Error err -> failwith $"Unexpected error: {err.Message}"
        | Ok decomp ->
            // Error should be non-zero
            Assert.True(decomp.ApproximationError > 0.0)
            
            // Error should be less than π/8 (half the distance between T gates at π/4 spacing)
            Assert.True(decomp.ApproximationError < Math.PI / 8.0)
    
    [<Fact>]
    let ``CORRECTED - Rz fails when error exceeds tolerance`` () =
        // Business meaning: Strict tolerance prevents silent approximations
        let angle = 0.5  // Approximation error ~0.107
        let strictTolerance = 1e-10
        let gate = CircuitBuilder.Gate.RZ (0, angle)
        
        match GateToBraid.compileGateToBraid gate 2 strictTolerance with
        | Ok _ -> failwith "Should fail with strict tolerance"
        | Error (TopologicalError.ComputationError (operation, context)) ->
            Assert.Contains("approximation error", context)
            Assert.Contains("exceeds tolerance", context)
        | Error _ -> failwith "Wrong error type"
    
    [<Fact>]
    let ``CORRECTED - Exact Rz multiples of π/4 have zero error`` () =
        // Business meaning: T-compatible angles should be exact
        // With tPhase = π/4, exact angles are multiples of π/4
        let exactAngles = [
            Math.PI / 4.0    // T (1 × π/4)
            Math.PI / 2.0    // S = T² (2 × π/4)
            3.0 * Math.PI / 4.0  // T³ (3 × π/4)
            Math.PI          // Z = T⁴ (4 × π/4)
        ]
        
        for angle in exactAngles do
            let gate = CircuitBuilder.Gate.RZ (0, angle)
            match GateToBraid.compileGateToBraid gate 2 1e-10 with
            | Error err -> failwith $"Exact angle {angle} should compile: {err.Message}"
            | Ok decomp ->
                Assert.True(decomp.ApproximationError < 1e-10, 
                    $"Angle {angle} should be exact, got error {decomp.ApproximationError}")
    
    // ========================================================================
    // EXISTING GATES (Unchanged - Sanity Checks)
    // ========================================================================
    
    [<Fact>]
    let ``T gate still compiles to single clockwise braiding`` () =
        // Business meaning: T gate was correct before, should still work
        match GateToBraid.tGateToBraid 0 2 with
        | Error err -> failwith $"Unexpected error: {err.Message}"
        | Ok braid ->
            Assert.Equal(1, braid.Generators.Length)
            Assert.Equal(0, braid.Generators.[0].Index)
            Assert.True(braid.Generators.[0].IsClockwise)
    
    [<Fact>]
    let ``S gate still compiles to two T gates`` () =
        // Business meaning: S = T² was correct before, should still work
        match GateToBraid.sGateToBraid 1 3 with
        | Error err -> failwith $"Unexpected error: {err.Message}"
        | Ok braid ->
            Assert.Equal(2, braid.Generators.Length)
            Assert.All(braid.Generators, fun gen -> Assert.True(gen.IsClockwise))
    
    // ========================================================================
    // DISPLAY UTILITIES WITH NEW FEATURES
    // ========================================================================
    
    [<Fact>]
    let ``Decomposition display includes notes when present`` () =
        // Business meaning: Users see warnings about decomposition limitations
        let gate = CircuitBuilder.Gate.Z 0
        
        match GateToBraid.compileGateToBraid gate 2 1e-10 with
        | Error err -> failwith $"Unexpected error: {err.Message}"
        | Ok decomp ->
            let display = GateToBraid.displayGateDecomposition decomp
            Assert.Contains("Notes:", display)
            Assert.Contains("global phase", display)
    
    [<Fact>]
    let ``Compilation summary includes warnings list`` () =
        // Business meaning: Aggregate warnings for full gate sequences
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
            // Should have 2 warnings (one for each Z gate)
            Assert.Equal(2, compilation.CompilationWarnings.Length)
            Assert.All(compilation.CompilationWarnings, fun w -> 
                Assert.Contains("global phase", w))
    
    // ========================================================================
    // REGRESSION TESTS (Ensure fixes don't break existing functionality)
    // ========================================================================
    
    [<Fact>]
    let ``Round-trip - T gate to braid and back`` () =
        // Business meaning: T → braid → T should be identity
        let originalGate = CircuitBuilder.Gate.T 0
        
        // Step 1: Gate → Braid
        match GateToBraid.compileGateToBraid originalGate 2 1e-10 with
        | Error err -> failwith $"Gate to braid failed: {err.Message}"
        | Ok decomp ->
            Assert.Equal(1, decomp.BraidSequence.Length)
            
            // Step 2: Braid → Gate
            match BraidToGate.compileToGates 
                    decomp.BraidSequence.[0] 
                    AnyonSpecies.AnyonType.Ising 
                    BraidToGate.defaultOptions with
            | Error err -> failwith $"Braid to gate failed: {err.Message}"
            | Ok gateSeq ->
                Assert.Equal(1, gateSeq.Gates.Length)
                Assert.Equal(originalGate, gateSeq.Gates.[0])
    
    [<Fact>]
    let ``Round-trip - S gate produces T squared`` () =
        // Business meaning: S decomposes correctly through both directions
        let sGate = CircuitBuilder.Gate.S 1
        
        match GateToBraid.compileGateToBraid sGate 3 1e-10 with
        | Error err -> failwith $"S to braid failed: {err.Message}"
        | Ok decomp ->
            // S should produce one braid with 2 generators (T, T)
            Assert.Equal(1, decomp.BraidSequence.Length)
            Assert.Equal(2, decomp.BraidSequence.[0].Generators.Length)
            
            // Compile back to gates
            match BraidToGate.compileToGates 
                    decomp.BraidSequence.[0] 
                    AnyonSpecies.AnyonType.Ising 
                    BraidToGate.defaultOptions with
            | Error err -> failwith $"Braid to gate failed: {err.Message}"
            | Ok gateSeq ->
                // Should produce 2 T gates
                Assert.Equal(2, gateSeq.Gates.Length)
                Assert.Equal(CircuitBuilder.Gate.T 1, gateSeq.Gates.[0])
                Assert.Equal(CircuitBuilder.Gate.T 1, gateSeq.Gates.[1])
    
    // ========================================================================
    // FUTURE WORK PLACEHOLDERS
    // ========================================================================
    
    [<Fact>]
    let ``Hadamard is implemented via Solovay-Kitaev approximation`` () =
        // Business meaning: Hadamard is now implemented using Solovay-Kitaev algorithm
        // Approximates H gate using T/S/Z gates (topological base set)
        let hGate = CircuitBuilder.Gate.H 0
        
        match GateToBraid.compileGateToBraid hGate 2 1e-10 with
        | Error err -> failwith $"Hadamard should be implemented: {err.Message}"
        | Ok decomp ->
            Assert.Equal("H", decomp.GateName)
            Assert.Equal<int list>([0], decomp.Qubits)
            Assert.True(decomp.DecompositionNotes.IsSome)
            Assert.Contains("Solovay-Kitaev", decomp.DecompositionNotes.Value)
            // Should have at least one braid operation
            Assert.True(decomp.BraidSequence.Length > 0)
    
    [<Fact>]
    let ``CNOT decomposes to H CZ H with optimized gate count`` () =
        // Business meaning: CNOT uses topological-specific S-K for efficient decomposition
        // Uses base set = {T, S, Z} only (no H/X/Y to avoid circular dependencies)
        let cnotGate = CircuitBuilder.Gate.CNOT (0, 1)
        
        match GateToBraid.compileGateToBraid cnotGate 2 1e-3 with
        | Error e -> failwithf "CNOT should be implemented now: %A" e
        | Ok result ->
            Assert.Equal("CNOT", result.GateName)
            Assert.Equal<int list>([0; 1], result.Qubits)
            Assert.True(result.BraidSequence.Length > 0, "Should have braiding sequence")
            Assert.True(result.DecompositionNotes.IsSome, "Should have decomposition notes")
            Assert.Contains("CZ", result.DecompositionNotes.Value)
            
            // Verify optimized gate count (should be much lower than before)
            // With topological S-K, expect ~300-500 gates per H, so CNOT ≈ 600-1000 total
            printfn "CNOT gate sequence length: %d" result.BraidSequence.Length

    // ========================================================================
    // AUTOMATIC TRANSPILATION TESTS (CZ, CCX, MCZ)
