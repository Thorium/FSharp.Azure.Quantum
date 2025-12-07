namespace FSharp.Azure.Quantum.Topological.Tests

open Xunit
open FSharp.Azure.Quantum.Topological
open FSharp.Azure.Quantum

module BraidToGateTests =

    // ========================================================================
    // TEST HELPERS
    // ========================================================================
    
    let private braidFromGensOrFail n gens context =
        match BraidGroup.fromGenerators n gens with
        | Error err -> failwith $"{context}: {err.Message}"
        | Ok braid -> braid
    
    let private compileOrFail braid anyonType options context =
        match BraidToGate.compileToGates braid anyonType options with
        | Error err -> failwith $"{context}: {err.Message}"
        | Ok sequence -> sequence

    // ========================================================================
    // GATE NAME AND UTILITIES TESTS
    // ========================================================================
    
    [<Fact>]
    let ``Gate names are correctly identified`` () =
        // Business meaning: Gate names must match standard quantum computing notation
        Assert.Equal("H", BraidToGate.getGateName (CircuitBuilder.Gate.H 0))
        Assert.Equal("T", BraidToGate.getGateName (CircuitBuilder.Gate.T 0))
        Assert.Equal("Tdg", BraidToGate.getGateName (CircuitBuilder.Gate.TDG 0))
        Assert.Equal("CNOT", BraidToGate.getGateName (CircuitBuilder.Gate.CNOT (0, 1)))
        Assert.Equal("X", BraidToGate.getGateName (CircuitBuilder.Gate.X 0))
    
    [<Fact>]
    let ``Single-qubit gates affect one qubit`` () =
        // Business meaning: Single-qubit operations should only affect their target qubit
        let qubits = BraidToGate.getAffectedQubits (CircuitBuilder.Gate.T 3)
        Assert.Equal<int list>([3], qubits)
    
    [<Fact>]
    let ``CNOT gate affects control and target qubits`` () =
        // Business meaning: Two-qubit gates create entanglement between qubits
        let qubits = BraidToGate.getAffectedQubits (CircuitBuilder.Gate.CNOT (2, 5))
        Assert.Equal<int list>([2; 5], qubits)
    
    [<Fact>]
    let ``Clifford gates are correctly identified`` () =
        // Business meaning: Clifford gates are efficiently simulable classically
        Assert.True(BraidToGate.isClifford (CircuitBuilder.Gate.H 0))
        Assert.True(BraidToGate.isClifford (CircuitBuilder.Gate.S 0))
        Assert.True(BraidToGate.isClifford (CircuitBuilder.Gate.CNOT (0, 1)))
        Assert.False(BraidToGate.isClifford (CircuitBuilder.Gate.T 0))  // T is magic gate
    
    [<Fact>]
    let ``T-count is correctly calculated`` () =
        // Business meaning: T-count determines fault-tolerant circuit cost
        let gates = [
            CircuitBuilder.Gate.T 0
            CircuitBuilder.Gate.H 1
            CircuitBuilder.Gate.TDG 2
            CircuitBuilder.Gate.T 0
            CircuitBuilder.Gate.S 1
        ]
        Assert.Equal(3, BraidToGate.countTGates gates)

    // ========================================================================
    // ISING ANYON COMPILATION TESTS
    // ========================================================================
    
    [<Fact>]
    let ``Ising clockwise braiding compiles to T gate`` () =
        // Business meaning: Majorana braiding phase exp(iπ/8) IS the T gate!
        // This is the deep connection between topological and gate-based QC.
        let braid = braidFromGensOrFail 2 [BraidGroup.sigma 0] "Single σ_0"
        let sequence = 
            compileOrFail braid AnyonSpecies.AnyonType.Ising 
                BraidToGate.defaultOptions "Ising compilation"
        
        Assert.Equal(1, sequence.Gates.Length)
        Assert.Equal(CircuitBuilder.Gate.T 0, sequence.Gates.[0])
    
    [<Fact>]
    let ``Ising counter-clockwise braiding compiles to T-dagger`` () =
        // Business meaning: Inverse braiding gives conjugate phase exp(-iπ/8) = T†
        let braid = braidFromGensOrFail 2 [BraidGroup.sigmaInv 0] "Single σ_0^-1"
        let sequence = 
            compileOrFail braid AnyonSpecies.AnyonType.Ising 
                BraidToGate.defaultOptions "Ising inverse"
        
        Assert.Equal(1, sequence.Gates.Length)
        Assert.Equal(CircuitBuilder.Gate.TDG 0, sequence.Gates.[0])
    
    [<Fact>]
    let ``Multiple Ising braidings compile to sequence of T gates`` () =
        // Business meaning: Composing braids = composing gates
        let braid = 
            braidFromGensOrFail 3 
                [BraidGroup.sigma 0; BraidGroup.sigma 1; BraidGroup.sigma 0]
                "σ_0 σ_1 σ_0"
        
        let sequence = 
            compileOrFail braid AnyonSpecies.AnyonType.Ising 
                BraidToGate.defaultOptions "Multiple braidings"
        
        Assert.Equal(3, sequence.Gates.Length)
        Assert.Equal(CircuitBuilder.Gate.T 0, sequence.Gates.[0])
        Assert.Equal(CircuitBuilder.Gate.T 1, sequence.Gates.[1])
        Assert.Equal(CircuitBuilder.Gate.T 0, sequence.Gates.[2])
    
    [<Fact>]
    let ``Ising identity braid compiles to empty gate sequence`` () =
        // Business meaning: Identity operation = no gates needed
        let braid = BraidGroup.identity 3
        let sequence = 
            compileOrFail braid AnyonSpecies.AnyonType.Ising 
                BraidToGate.defaultOptions "Identity"
        
        Assert.Empty(sequence.Gates)
    
    [<Fact>]
    let ``Ising braid on n strands produces n-1 qubit circuit`` () =
        // Business meaning: n anyons on strands ↔ n-1 qubits in gate model
        let braid = braidFromGensOrFail 4 [BraidGroup.sigma 0] "4 strands"
        let sequence = 
            compileOrFail braid AnyonSpecies.AnyonType.Ising 
                BraidToGate.defaultOptions "Qubit counting"
        
        Assert.Equal(3, sequence.NumQubits)  // 4 strands → 3 qubits

    // ========================================================================
    // FIBONACCI ANYON COMPILATION TESTS
    // ========================================================================
    
    [<Fact>]
    let ``Fibonacci braiding compiles to rotation gate`` () =
        // Business meaning: Fibonacci phases don't match simple gates, need Rz
        let braid = braidFromGensOrFail 2 [BraidGroup.sigma 0] "Single τ braiding"
        let sequence = 
            compileOrFail braid AnyonSpecies.AnyonType.Fibonacci 
                BraidToGate.defaultOptions "Fibonacci compilation"
        
        Assert.Equal(1, sequence.Gates.Length)
        match sequence.Gates.[0] with
        | CircuitBuilder.Gate.RZ (q, angle) ->
            Assert.Equal(0, q)
            Assert.Equal(4.0 * System.Math.PI / 5.0, angle, 10)  // exp(4πi/5)
        | _ -> failwith "Expected Rz gate"
    
    [<Fact>]
    let ``Fibonacci inverse braiding uses negative rotation`` () =
        // Business meaning: Inverse braiding = conjugate phase = negative angle
        let braid = braidFromGensOrFail 2 [BraidGroup.sigmaInv 0] "τ inverse"
        let sequence = 
            compileOrFail braid AnyonSpecies.AnyonType.Fibonacci 
                BraidToGate.defaultOptions "Fibonacci inverse"
        
        match sequence.Gates.[0] with
        | CircuitBuilder.Gate.RZ (_, angle) ->
            Assert.True(angle < 0.0)
        | _ -> failwith "Expected Rz gate"

    // ========================================================================
    // GATE OPTIMIZATION TESTS
    // ========================================================================
    
    [<Fact>]
    let ``Adjacent T and T-dagger gates cancel`` () =
        // Business meaning: Optimization reduces circuit cost
        let gates = [CircuitBuilder.Gate.T 0; CircuitBuilder.Gate.TDG 0; CircuitBuilder.Gate.H 1]
        let optimized = BraidToGate.optimizeGates 1 gates
        
        // Should cancel T and T†, leaving only H
        Assert.Equal(1, optimized.Length)
        Assert.Equal(CircuitBuilder.Gate.H 1, optimized.[0])
    
    [<Fact>]
    let ``Multiple inverse pairs are all cancelled`` () =
        // Business meaning: Full optimization removes all redundant operations
        let gates = [
            CircuitBuilder.Gate.T 0
            CircuitBuilder.Gate.TDG 0
            CircuitBuilder.Gate.S 1
            CircuitBuilder.Gate.SDG 1
            CircuitBuilder.Gate.H 2
            CircuitBuilder.Gate.H 2
        ]
        let optimized = BraidToGate.optimizeGates 1 gates
        
        Assert.Empty(optimized)
    
    [<Fact>]
    let ``Non-adjacent inverse pairs are not cancelled`` () =
        // Business meaning: Basic optimization doesn't reorder gates
        let gates = [CircuitBuilder.Gate.T 0; CircuitBuilder.Gate.H 1; CircuitBuilder.Gate.TDG 0]
        let optimized = BraidToGate.optimizeGates 1 gates
        
        Assert.Equal(3, optimized.Length)  // No cancellation
    
    [<Fact>]
    let ``Adjacent Rz gates on same qubit merge`` () =
        // Business meaning: Multiple rotations combine into single rotation
        let gates = [
            CircuitBuilder.Gate.RZ (2, System.Math.PI / 4.0)
            CircuitBuilder.Gate.RZ (2, System.Math.PI / 8.0)
        ]
        let optimized = BraidToGate.optimizeGates 1 gates
        
        Assert.Equal(1, optimized.Length)
        match optimized.[0] with
        | CircuitBuilder.Gate.RZ (q, angle) ->
            Assert.Equal(2, q)
            Assert.Equal(3.0 * System.Math.PI / 8.0, angle, 10)
        | _ -> failwith "Expected merged Rz gate"
    
    [<Fact>]
    let ``Rz gates on different qubits do not merge`` () =
        // Business meaning: Parallel operations are independent
        let gates = [
            CircuitBuilder.Gate.RZ (0, System.Math.PI / 4.0)
            CircuitBuilder.Gate.RZ (1, System.Math.PI / 8.0)
        ]
        let optimized = BraidToGate.optimizeGates 1 gates
        
        Assert.Equal(2, optimized.Length)
    
    [<Fact>]
    let ``Optimization level 0 performs no optimization`` () =
        // Business meaning: User can disable optimization if needed
        let gates = [CircuitBuilder.Gate.T 0; CircuitBuilder.Gate.TDG 0]
        let optimized = BraidToGate.optimizeGates 0 gates
        
        Assert.Equal(2, optimized.Length)  // No changes
    
    [<Fact>]
    let ``Braid with inverse compiles to optimized empty sequence`` () =
        // Business meaning: σ_i σ_i^-1 = identity after optimization
        let braid = 
            braidFromGensOrFail 2 
                [BraidGroup.sigma 0; BraidGroup.sigmaInv 0] 
                "σ_0 σ_0^-1"
        
        let sequence = 
            compileOrFail braid AnyonSpecies.AnyonType.Ising 
                BraidToGate.defaultOptions "Identity after optimization"
        
        Assert.Empty(sequence.Gates)

    // ========================================================================
    // CIRCUIT DEPTH TESTS
    // ========================================================================
    
    [<Fact>]
    let ``Sequential gates on same qubit have depth N`` () =
        // Business meaning: Gates on same qubit must execute sequentially
        let braid = 
            braidFromGensOrFail 2 
                [BraidGroup.sigma 0; BraidGroup.sigma 0; BraidGroup.sigma 0]
                "Three sequential"
        
        let sequence = 
            compileOrFail braid AnyonSpecies.AnyonType.Ising 
                BraidToGate.defaultOptions "Sequential depth"
        
        Assert.Equal(3, sequence.Depth)
    
    [<Fact>]
    let ``Parallel gates on different qubits have depth 1`` () =
        // Business meaning: Independent operations can execute in parallel
        let braid = 
            braidFromGensOrFail 4 
                [BraidGroup.sigma 0; BraidGroup.sigma 2]  // Non-adjacent
                "Parallel gates"
        
        let sequence = 
            compileOrFail braid AnyonSpecies.AnyonType.Ising 
                BraidToGate.defaultOptions "Parallel depth"
        
        Assert.Equal(1, sequence.Depth)
    
    [<Fact>]
    let ``Adjacent generators compile to independent gates with depth 1`` () =
        // Business meaning: σ_0 → T(q0) and σ_1 → T(q1) operate on different
        // qubits, so they can execute in parallel in the gate model!
        // This shows a key difference: topological adjacency ≠ gate dependence.
        let braid = 
            braidFromGensOrFail 3 
                [BraidGroup.sigma 0; BraidGroup.sigma 1]
                "Adjacent generators"
        
        let sequence = 
            compileOrFail braid AnyonSpecies.AnyonType.Ising 
                BraidToGate.defaultOptions "Adjacent depth"
        
        Assert.Equal(1, sequence.Depth)  // Parallel execution!

    // ========================================================================
    // METADATA TESTS
    // ========================================================================
    
    [<Fact>]
    let ``Gate sequence reports correct T-count`` () =
        // Business meaning: T-count is critical metric for fault-tolerant cost
        let braid = 
            braidFromGensOrFail 3 
                [BraidGroup.sigma 0; BraidGroup.sigmaInv 1; BraidGroup.sigma 0]
                "Mixed directions"
        
        let sequence = 
            compileOrFail braid AnyonSpecies.AnyonType.Ising 
                BraidToGate.defaultOptions "T-count test"
        
        Assert.Equal(3, sequence.TCount)  // 2 T + 1 T†
    
    [<Fact>]
    let ``Empty braid produces zero-depth circuit`` () =
        // Business meaning: Identity operation has no depth
        let braid = BraidGroup.identity 5
        let sequence = 
            compileOrFail braid AnyonSpecies.AnyonType.Ising 
                BraidToGate.defaultOptions "Zero depth"
        
        Assert.Equal(0, sequence.Depth)
    
    [<Fact>]
    let ``Gate sequence metadata is consistent`` () =
        // Business meaning: All metadata should accurately reflect the circuit
        let braid = 
            braidFromGensOrFail 4 
                [BraidGroup.sigma 0; BraidGroup.sigma 1; BraidGroup.sigma 2]
                "Full chain"
        
        let sequence = 
            compileOrFail braid AnyonSpecies.AnyonType.Ising 
                BraidToGate.defaultOptions "Metadata test"
        
        Assert.Equal(3, sequence.Gates.Length)
        Assert.Equal(3, sequence.NumQubits)
        Assert.True(sequence.Depth > 0)
        Assert.Equal(sequence.TCount, BraidToGate.countTGates sequence.Gates)

    // ========================================================================
    // DISPLAY TESTS
    // ========================================================================
    
    [<Fact>]
    let ``Gate display shows standard notation`` () =
        // Business meaning: Output should match standard quantum circuit notation
        let display = BraidToGate.displayGate (CircuitBuilder.Gate.T 3)
        Assert.Contains("T(q3)", display)
    
    [<Fact>]
    let ``CNOT gate displays control and target`` () =
        // Business meaning: Two-qubit gates show both qubits clearly
        let display = BraidToGate.displayGate (CircuitBuilder.Gate.CNOT (0, 2))
        Assert.Contains("CNOT(q0, q2)", display)
    
    [<Fact>]
    let ``Gate sequence display includes circuit metadata`` () =
        // Business meaning: Full summary helps understand circuit complexity
        let braid = braidFromGensOrFail 3 [BraidGroup.sigma 0; BraidGroup.sigma 1] "Test"
        let sequence = 
            compileOrFail braid AnyonSpecies.AnyonType.Ising 
                BraidToGate.defaultOptions "Display test"
        
        let display = BraidToGate.displayGateSequence sequence
        
        Assert.Contains("Gate Sequence", display)
        Assert.Contains("Circuit depth", display)
        Assert.Contains("T-count", display)
    
    [<Fact>]
    let ``Statistics display shows gate breakdown`` () =
        // Business meaning: Understanding gate composition helps optimization
        let braid = 
            braidFromGensOrFail 3 
                [BraidGroup.sigma 0; BraidGroup.sigmaInv 1; BraidGroup.sigma 0]
                "Mixed"
        
        let sequence = 
            compileOrFail braid AnyonSpecies.AnyonType.Ising 
                BraidToGate.defaultOptions "Stats test"
        
        let stats = BraidToGate.displayStatistics sequence
        
        Assert.Contains("Circuit Statistics", stats)
        Assert.Contains("Total gates", stats)
        Assert.Contains("T:", stats)  // Gate type breakdown
        Assert.Contains("Clifford gates", stats)
        Assert.Contains("Non-Clifford gates", stats)

    // ========================================================================
    // COMPILATION OPTIONS TESTS
    // ========================================================================
    
    [<Fact>]
    let ``Default options use Clifford+T gate set`` () =
        // Business meaning: Standard fault-tolerant gate set by default
        let opts = BraidToGate.defaultOptions
        Assert.Contains("T", opts.TargetGateSet)
        Assert.Contains("H", opts.TargetGateSet)
        Assert.Contains("CNOT", opts.TargetGateSet)
    
    [<Fact>]
    let ``Universal gate set includes rotation gates`` () =
        // Business meaning: More expressive gate set for simulation
        let gateSet = BraidToGate.universalGateSet
        Assert.Contains("Rz", gateSet)
        Assert.Contains("U3", gateSet)
        Assert.Contains("Phase", gateSet)
    
    [<Fact>]
    let ``Custom optimization level is respected`` () =
        // Business meaning: User control over optimization trade-offs
        let opts = { BraidToGate.defaultOptions with OptimizationLevel = 0 }
        
        let braid = 
            braidFromGensOrFail 2 
                [BraidGroup.sigma 0; BraidGroup.sigmaInv 0]
                "Should not optimize"
        
        let sequence = compileOrFail braid AnyonSpecies.AnyonType.Ising opts "No opt"
        
        Assert.Equal(2, sequence.Gates.Length)  // Not optimized
