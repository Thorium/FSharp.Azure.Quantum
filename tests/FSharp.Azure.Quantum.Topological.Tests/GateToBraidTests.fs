namespace FSharp.Azure.Quantum.Topological.Tests

open Xunit
open FSharp.Azure.Quantum.Topological
open FSharp.Azure.Quantum
open System

module GateToBraidTests =

    // ========================================================================
    // TEST HELPERS
    // ========================================================================
    
    let private unwrapResult result context =
        match result with
        | Error err -> failwith $"{context}: {TopologicalError.message err}"
        | Ok value -> value
    
    let private assertExactDecomposition (decomp: GateToBraid.GateDecomposition) =
        Assert.True(decomp.ApproximationError < 1e-10, 
            $"Expected exact decomposition, got error {decomp.ApproximationError}")

    // ========================================================================
    // T GATE DECOMPOSITION TESTS (The Magic Gate!)
    // ========================================================================
    
    [<Fact>]
    let ``T gate decomposes to single clockwise braiding`` () =
        // Business meaning: T gate = Majorana braiding! This is the KEY insight
        // of topological quantum computing with Ising anyons.
        let braid = unwrapResult (GateToBraid.tGateToBraid 0 2) "T gate"
        
        Assert.Equal(3, braid.StrandCount)  // 2 qubits = 3 strands
        Assert.Equal(1, braid.Generators.Length)
        Assert.Equal(0, braid.Generators.[0].Index)
        Assert.True(braid.Generators.[0].IsClockwise)
    
    [<Fact>]
    let ``T-dagger gate decomposes to counter-clockwise braiding`` () =
        // Business meaning: Inverse gate = inverse braiding (topology!)
        let braid = unwrapResult (GateToBraid.tDaggerGateToBraid 1 3) "T† gate"
        
        Assert.Equal(4, braid.StrandCount)  // 3 qubits = 4 strands
        Assert.Equal(1, braid.Generators.Length)
        Assert.Equal(1, braid.Generators.[0].Index)
        Assert.False(braid.Generators.[0].IsClockwise)
    
    [<Fact>]
    let ``T gate compilation is exact with zero error`` () =
        // Business meaning: No approximation needed for T gates - topological!
        let gate = CircuitBuilder.Gate.T 2
        let decomp = unwrapResult (GateToBraid.compileGateToBraid gate 4 1e-10) "Compile T"
        
        assertExactDecomposition decomp
        Assert.Equal("T", decomp.GateName)
        Assert.Equal<int list>([2], decomp.Qubits)

    // ========================================================================
    // CLIFFORD GATE DECOMPOSITION TESTS
    // ========================================================================
    
    [<Fact>]
    let ``S gate decomposes to two T gates`` () =
        // Business meaning: S = T² (two sequential braidings)
        let braid = unwrapResult (GateToBraid.sGateToBraid 0 2) "S gate"
        
        Assert.Equal(2, braid.Generators.Length)
        Assert.True(braid.Generators.[0].IsClockwise)
        Assert.True(braid.Generators.[1].IsClockwise)
        Assert.Equal(braid.Generators.[0].Index, braid.Generators.[1].Index)
    
    [<Fact>]
    let ``S-dagger gate decomposes to two T-dagger gates`` () =
        // Business meaning: S† = (T†)²
        let braid = unwrapResult (GateToBraid.sDaggerGateToBraid 1 3) "S† gate"
        
        Assert.Equal(2, braid.Generators.Length)
        Assert.False(braid.Generators.[0].IsClockwise)
        Assert.False(braid.Generators.[1].IsClockwise)
    
    [<Fact>]
    let ``Pauli Z gate decomposes to four T gates`` () =
        // Business meaning: Z = T⁴ (four sequential braidings)
        // This shows how even simple gates require multiple topological operations
        let braid = unwrapResult (GateToBraid.zGateToBraid 2 4) "Z gate"
        
        Assert.Equal(4, braid.Generators.Length)
        Assert.All(braid.Generators, fun gen -> Assert.True(gen.IsClockwise))
        Assert.All(braid.Generators, fun gen -> Assert.Equal(2, gen.Index))
    
    [<Fact>]
    let ``S and Z gate compilations are exact`` () =
        // Business meaning: All Clifford+T gates compile exactly
        let sGate = CircuitBuilder.Gate.S 1
        let zGate = CircuitBuilder.Gate.Z 2
        
        let sDecomp = unwrapResult (GateToBraid.compileGateToBraid sGate 3 1e-10) "S"
        let zDecomp = unwrapResult (GateToBraid.compileGateToBraid zGate 3 1e-10) "Z"
        
        assertExactDecomposition sDecomp
        assertExactDecomposition zDecomp

    // ========================================================================
    // ROTATION GATE DECOMPOSITION TESTS
    // ========================================================================
    
    [<Fact>]
    let ``Rz gate with pi/8 angle decomposes to single T`` () =
        // Business meaning: Rz(π/8) = T exactly
        let angle = Math.PI / 8.0
        let braid = unwrapResult (GateToBraid.rzGateToBraid 0 angle 2 1e-10) "Rz(π/8)"
        
        Assert.Equal(1, braid.Generators.Length)
        Assert.True(braid.Generators.[0].IsClockwise)
    
    [<Fact>]
    let ``Rz gate with pi/4 angle decomposes to two T gates`` () =
        // Business meaning: Rz(π/4) = S = T²
        let angle = Math.PI / 4.0
        let braid = unwrapResult (GateToBraid.rzGateToBraid 1 angle 3 1e-10) "Rz(π/4)"
        
        Assert.Equal(2, braid.Generators.Length)
    
    [<Fact>]
    let ``Rz gate with negative angle uses counter-clockwise braiding`` () =
        // Business meaning: Negative rotation = inverse braiding
        let angle = -Math.PI / 8.0
        let braid = unwrapResult (GateToBraid.rzGateToBraid 0 angle 2 1e-10) "Rz(-π/8)"
        
        Assert.Equal(1, braid.Generators.Length)
        Assert.False(braid.Generators.[0].IsClockwise)
    
    [<Fact>]
    let ``Rz gate with arbitrary angle approximates using T gates`` () =
        // Business meaning: Any rotation can be approximated with braiding
        let angle = 1.234  // Random angle
        let braid = unwrapResult (GateToBraid.rzGateToBraid 0 angle 2 1e-3) "Rz(1.234)"
        
        // Should have ~3-4 T gates (1.234 / (π/8) ≈ 3.14)
        Assert.InRange(braid.Generators.Length, 2, 5)
    
    [<Fact>]
    let ``Rz compilation reports approximation error for non-T-multiple angles`` () =
        // Business meaning: Only multiples of π/8 are exact
        let angle = 0.5  // Not a multiple of π/8
        let gate = CircuitBuilder.Gate.RZ (0, angle)
        let decomp = unwrapResult (GateToBraid.compileGateToBraid gate 2 1e-3) "Rz(0.5)"
        
        Assert.True(decomp.ApproximationError > 0.0)
        Assert.Contains("Rz", decomp.GateName)
    
    [<Fact>]
    let ``Phase gate compilation is equivalent to Rz`` () =
        // Business meaning: Phase and Rz are the same operation
        let angle = Math.PI / 4.0
        let phaseGate = CircuitBuilder.Gate.P (1, angle)
        let decomp = unwrapResult (GateToBraid.compileGateToBraid phaseGate 3 1e-10) "Phase"
        
        Assert.Equal(1, decomp.BraidSequence.Length)
        Assert.Equal(2, decomp.BraidSequence.[0].Generators.Length)  // S = T²

    // ========================================================================
    // UNSUPPORTED GATE TESTS (Future Work)
    // ========================================================================
    
    [<Fact>]
    let ``Hadamard gate compilation works via Solovay-Kitaev`` () =
        // Business meaning: H gate is approximated using Solovay-Kitaev algorithm
        let gate = CircuitBuilder.Gate.H 0
        let result = GateToBraid.compileGateToBraid gate 2 1e-10
        
        match result with
        | Ok decomp ->
            Assert.Equal("H", decomp.GateName)
            Assert.True(decomp.BraidSequence.Length > 0)
        | Error err -> failwith $"Hadamard should work: {err.Message}"
    
    [<Fact>]
    let ``CNOT gate compilation works via H-CZ-H decomposition`` () =
        // Business meaning: CNOT is implemented via Hadamard and controlled-Z
        let gate = CircuitBuilder.Gate.CNOT (0, 1)
        let result = GateToBraid.compileGateToBraid gate 2 1e-10
        
        match result with
        | Ok decomp ->
            Assert.Equal("CNOT", decomp.GateName)
            Assert.Equal<int list>([0; 1], decomp.Qubits)
        | Error err -> failwith $"CNOT should work: {err.Message}"
    
    [<Fact>]
    let ``Pauli X gate compilation works via Solovay-Kitaev`` () =
        // Business meaning: X gate is approximated using Solovay-Kitaev algorithm
        let gate = CircuitBuilder.Gate.X 0
        let result = GateToBraid.compileGateToBraid gate 2 1e-10
        
        match result with
        | Ok decomp ->
            Assert.Equal("X", decomp.GateName)
            Assert.True(decomp.BraidSequence.Length > 0)
        | Error err -> failwith $"Pauli X should work: {err.Message}"

    // ========================================================================
    // GATE SEQUENCE COMPILATION TESTS
    // ========================================================================
    
    [<Fact>]
    let ``Empty gate sequence compiles to empty braid list`` () =
        // Business meaning: Identity circuit = no braiding
        let emptySequence = {
            BraidToGate.GateSequence.Gates = []
            NumQubits = 3
            TotalPhase = System.Numerics.Complex(1.0, 0.0)
            Depth = 0
            TCount = 0
        }
        
        let compilation = 
            unwrapResult 
                (GateToBraid.compileGateSequence emptySequence 1e-10 AnyonSpecies.AnyonType.Ising) 
                "Empty sequence"
        
        Assert.Empty(compilation.CompiledBraids)
        Assert.True(compilation.IsExact)
        Assert.Equal(0.0, compilation.TotalError)
    
    [<Fact>]
    let ``Sequence of T gates compiles to sequence of braids`` () =
        // Business meaning: Gate sequence → braiding sequence
        let gateSeq = {
            BraidToGate.GateSequence.Gates = [
                CircuitBuilder.Gate.T 0
                CircuitBuilder.Gate.T 1
                CircuitBuilder.Gate.TDG 0
            ]
            NumQubits = 2
            TotalPhase = System.Numerics.Complex(1.0, 0.0)
            Depth = 2
            TCount = 3
        }
        
        let compilation = 
            unwrapResult 
                (GateToBraid.compileGateSequence gateSeq 1e-10 AnyonSpecies.AnyonType.Ising) 
                "T sequence"
        
        Assert.Equal(3, compilation.CompiledBraids.Length)
        Assert.True(compilation.IsExact)
        Assert.Equal(3, compilation.OriginalGateCount)
    
    [<Fact>]
    let ``Clifford+T gate sequence compiles exactly`` () =
        // Business meaning: All Clifford+T circuits compile without approximation
        let gateSeq = {
            BraidToGate.GateSequence.Gates = [
                CircuitBuilder.Gate.T 0
                CircuitBuilder.Gate.S 1
                CircuitBuilder.Gate.Z 0
                CircuitBuilder.Gate.TDG 1
            ]
            NumQubits = 2
            TotalPhase = System.Numerics.Complex(1.0, 0.0)
            Depth = 3
            TCount = 2
        }
        
        let compilation = 
            unwrapResult 
                (GateToBraid.compileGateSequence gateSeq 1e-10 AnyonSpecies.AnyonType.Ising) 
                "Clifford+T"
        
        Assert.True(compilation.IsExact)
        Assert.Equal(0.0, compilation.TotalError)
    
    [<Fact>]
    let ``Gate sequence with Rz approximation reports total error`` () =
        // Business meaning: Track cumulative approximation error
        let gateSeq = {
            BraidToGate.GateSequence.Gates = [
                CircuitBuilder.Gate.RZ (0, 0.5)  // Not exact
                CircuitBuilder.Gate.RZ (1, 0.7)  // Not exact
            ]
            NumQubits = 2
            TotalPhase = System.Numerics.Complex(1.0, 0.0)
            Depth = 1
            TCount = 0
        }
        
        let compilation = 
            unwrapResult 
                (GateToBraid.compileGateSequence gateSeq 1e-3 AnyonSpecies.AnyonType.Ising) 
                "Rz approx"
        
        Assert.False(compilation.IsExact)
        Assert.True(compilation.TotalError > 0.0)
    
    [<Fact>]
    let ``Fibonacci anyon compilation succeeds for T gate`` () =
        // Business meaning: Fibonacci anyons are now supported with braid approximation
        let gateSeq = {
            BraidToGate.GateSequence.Gates = [CircuitBuilder.Gate.T 0]
            NumQubits = 2
            TotalPhase = System.Numerics.Complex(1.0, 0.0)
            Depth = 1
            TCount = 1
        }
        
        let result = GateToBraid.compileGateSequence gateSeq 0.5 AnyonSpecies.AnyonType.Fibonacci
        
        match result with
        | Ok compilation ->
            Assert.Equal(AnyonSpecies.AnyonType.Fibonacci, compilation.AnyonType)
            Assert.True(compilation.CompiledBraids.Length > 0)
        | Error err -> failwith $"Fibonacci compilation should succeed, got error: {err}"

    // ========================================================================
    // ROUND-TRIP TESTS (Gate → Braid → Gate)
    // ========================================================================
    
    [<Fact>]
    let ``T gate round-trip preserves gate identity`` () =
        // Business meaning: T → braid → T is identity (for exact gates)
        // Step 1: Gate → Braid
        let originalGate = CircuitBuilder.Gate.T 0
        let decomp = unwrapResult (GateToBraid.compileGateToBraid originalGate 2 1e-10) "Round-trip 1"
        
        Assert.Equal(1, decomp.BraidSequence.Length)
        
        // Step 2: Braid → Gate
        let gateSeq = 
            unwrapResult 
                (BraidToGate.compileToGates 
                    decomp.BraidSequence.[0] 
                    AnyonSpecies.AnyonType.Ising 
                    BraidToGate.defaultOptions)
                "Round-trip 2"
        
        Assert.Equal(1, gateSeq.Gates.Length)
        Assert.Equal(originalGate, gateSeq.Gates.[0])
    
    [<Fact>]
    let ``S gate round-trip produces T squared`` () =
        // Business meaning: S → (T, T) → S preserves semantics
        let originalGate = CircuitBuilder.Gate.S 1
        let decomp = unwrapResult (GateToBraid.compileGateToBraid originalGate 3 1e-10) "S decomp"
        
        // S decomposes to single braid with 2 generators (T, T)
        Assert.Equal(1, decomp.BraidSequence.Length)
        Assert.Equal(2, decomp.BraidSequence.[0].Generators.Length)

    // ========================================================================
    // DISPLAY TESTS
    // ========================================================================
    
    [<Fact>]
    let ``Gate decomposition display shows key information`` () =
        // Business meaning: Human-readable output for debugging
        let gate = CircuitBuilder.Gate.T 2
        let decomp = unwrapResult (GateToBraid.compileGateToBraid gate 4 1e-10) "Display test"
        
        let display = GateToBraid.displayGateDecomposition decomp
        
        Assert.Contains("T", display)
        Assert.Contains("EXACT", display)  // Zero error
        Assert.Contains("Braiding", display)
    
    [<Fact>]
    let ``Compilation summary shows statistics`` () =
        // Business meaning: Summary helps understand compilation efficiency
        let gateSeq = {
            BraidToGate.GateSequence.Gates = [
                CircuitBuilder.Gate.T 0
                CircuitBuilder.Gate.S 1
            ]
            NumQubits = 2
            TotalPhase = System.Numerics.Complex(1.0, 0.0)
            Depth = 1
            TCount = 1
        }
        
        let compilation = 
            unwrapResult 
                (GateToBraid.compileGateSequence gateSeq 1e-10 AnyonSpecies.AnyonType.Ising) 
                "Summary test"
        
        let summary = GateToBraid.displayCompilationSummary compilation
        
        Assert.Contains("Compilation Summary", summary)
        Assert.Contains("Ising", summary)
        Assert.Contains("Original gates: 2", summary)
        Assert.Contains("EXACT", summary)

    // ========================================================================
    // EDGE CASE TESTS
    // ========================================================================
    
    [<Fact>]
    let ``Zero angle Rz gate produces empty braid`` () =
        // Business meaning: Rz(0) = Identity = no braiding
        let braid = unwrapResult (GateToBraid.rzGateToBraid 0 0.0 2 1e-10) "Rz(0)"
        
        Assert.Empty(braid.Generators)
    
    [<Fact>]
    let ``Large angle Rz produces many T gates`` () =
        // Business meaning: Large rotations need many braiding operations
        let largeAngle = 10.0 * Math.PI  // Many multiples of π/8
        let braid = unwrapResult (GateToBraid.rzGateToBraid 0 largeAngle 2 1e-3) "Rz(10π)"
        
        Assert.True(braid.Generators.Length > 10)
    
    [<Fact>]
    let ``Qubit index is preserved through compilation`` () =
        // Business meaning: Gate on qubit i → braiding on strand i
        for qubitIndex in [0; 1; 2; 3] do
            let gate = CircuitBuilder.Gate.T qubitIndex
            let decomp = unwrapResult (GateToBraid.compileGateToBraid gate 5 1e-10) "Qubit index"
            
            Assert.Equal<int list>([qubitIndex], decomp.Qubits)
            Assert.Equal(qubitIndex, decomp.BraidSequence.[0].Generators.[0].Index)

    // ========================================================================
    // AUTOMATIC TRANSPILATION TESTS (CZ, CCX, MCZ)
    // ========================================================================
    
    [<Fact>]
    let ``CZ gate is automatically transpiled in compileGateSequence`` () =
        // Business meaning: Complex gates are automatically decomposed before topological compilation
        // CZ → H + CNOT + H (3 gates)
        let gateSeq = {
            BraidToGate.GateSequence.Gates = [
                CircuitBuilder.Gate.CZ (0, 1)
            ]
            NumQubits = 2
            TotalPhase = System.Numerics.Complex(1.0, 0.0)
            Depth = 1
            TCount = 0
        }
        
        // compileGateSequence should automatically transpile CZ to H+CNOT+H
        let compilation = 
            unwrapResult 
                (GateToBraid.compileGateSequence gateSeq 1e-10 AnyonSpecies.AnyonType.Ising) 
                "CZ transpilation"
        
        // Should succeed without errors (CZ transpiled to elementary gates)
        Assert.True(compilation.IsExact)
        Assert.Equal(1, compilation.OriginalGateCount)  // Original: 1 CZ gate
    
    [<Fact>]
    let ``CCX (Toffoli) gate is automatically transpiled in compileGateSequence`` () =
        // Business meaning: Toffoli requires 6 CNOTs + T gates via Barenco decomposition
        // CCX → 6 CNOTs + 7 T/TDG/H gates = 13 elementary gates
        let gateSeq = {
            BraidToGate.GateSequence.Gates = [
                CircuitBuilder.Gate.CCX (0, 1, 2)
            ]
            NumQubits = 3
            TotalPhase = System.Numerics.Complex(1.0, 0.0)
            Depth = 1
            TCount = 0
        }
        
        // compileGateSequence should automatically transpile CCX
        let compilation = 
            unwrapResult 
                (GateToBraid.compileGateSequence gateSeq 1e-10 AnyonSpecies.AnyonType.Ising) 
                "CCX transpilation"
        
        // Should succeed without errors (CCX transpiled to elementary gates)
        Assert.True(compilation.IsExact)
        Assert.Equal(1, compilation.OriginalGateCount)  // Original: 1 CCX gate
        Assert.True(compilation.CompiledBraids.Length > 10)  // Should produce many braids
    
    [<Fact>]
    let ``MCZ gate with 3 controls is automatically transpiled in compileGateSequence`` () =
        // Business meaning: Multi-controlled gates are recursively decomposed
        // MCZ(3 controls) → recursive Toffoli decomposition
        let gateSeq = {
            BraidToGate.GateSequence.Gates = [
                CircuitBuilder.Gate.MCZ ([0; 1; 2], 3)
            ]
            NumQubits = 4
            TotalPhase = System.Numerics.Complex(1.0, 0.0)
            Depth = 1
            TCount = 0
        }
        
        // compileGateSequence should automatically transpile MCZ
        let compilation = 
            unwrapResult 
                (GateToBraid.compileGateSequence gateSeq 1e-10 AnyonSpecies.AnyonType.Ising) 
                "MCZ transpilation"
        
        // Should succeed without errors (MCZ transpiled to elementary gates)
        Assert.True(compilation.IsExact)
        Assert.Equal(1, compilation.OriginalGateCount)  // Original: 1 MCZ gate
        Assert.True(compilation.CompiledBraids.Length > 20)  // MCZ(3) produces many braids
    
    [<Fact>]
    let ``Mixed circuit with CZ and CCX is automatically transpiled`` () =
        // Business meaning: Real circuits mix complex and elementary gates
        // All complex gates should be transparently handled
        let gateSeq = {
            BraidToGate.GateSequence.Gates = [
                CircuitBuilder.Gate.H 0
                CircuitBuilder.Gate.CZ (0, 1)      // Should be transpiled
                CircuitBuilder.Gate.T 2
                CircuitBuilder.Gate.CCX (0, 1, 2)  // Should be transpiled
                CircuitBuilder.Gate.S 1
            ]
            NumQubits = 3
            TotalPhase = System.Numerics.Complex(1.0, 0.0)
            Depth = 3
            TCount = 1
        }
        
        // compileGateSequence handles mixed circuit automatically
        let compilation = 
            unwrapResult 
                (GateToBraid.compileGateSequence gateSeq 1e-10 AnyonSpecies.AnyonType.Ising) 
                "Mixed circuit transpilation"
        
        // Should succeed - all gates compiled
        Assert.True(compilation.IsExact)
        Assert.Equal(5, compilation.OriginalGateCount)  // 5 original gates
        Assert.True(compilation.CompiledBraids.Length > 15)  // CZ+CCX produce many braids
