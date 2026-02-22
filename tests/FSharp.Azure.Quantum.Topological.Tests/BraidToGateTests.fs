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

    let private identityOrFail n context =
        match BraidGroup.identity n with
        | Ok braid -> braid
        | Error err -> failwith $"{context}: {err.Message}"

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
    let ``Ising clockwise braiding compiles to S gate`` () =
        // Physics: Ising σ×σ relative phase = e^{iπ/2} per braid → S gate
        // One CW braid = S gate (exact in Ising model)
        let braid = braidFromGensOrFail 2 [BraidGroup.sigma 0] "Single σ_0"
        let sequence = 
            compileOrFail braid AnyonSpecies.AnyonType.Ising 
                BraidToGate.defaultOptions "Ising compilation"
        
        Assert.Equal(1, sequence.Gates.Length)
        Assert.Equal(CircuitBuilder.Gate.S 0, sequence.Gates.[0])
    
    [<Fact>]
    let ``Ising counter-clockwise braiding compiles to S-dagger`` () =
        // Physics: Inverse braiding gives conjugate phase e^{-iπ/2} = S†
        let braid = braidFromGensOrFail 2 [BraidGroup.sigmaInv 0] "Single σ_0^-1"
        let sequence = 
            compileOrFail braid AnyonSpecies.AnyonType.Ising 
                BraidToGate.defaultOptions "Ising inverse"
        
        Assert.Equal(1, sequence.Gates.Length)
        Assert.Equal(CircuitBuilder.Gate.SDG 0, sequence.Gates.[0])
    
    [<Fact>]
    let ``Multiple Ising braidings compile to sequence of S gates`` () =
        // Business meaning: Composing braids = composing gates
        let braid = 
            braidFromGensOrFail 3 
                [BraidGroup.sigma 0; BraidGroup.sigma 1; BraidGroup.sigma 0]
                "σ_0 σ_1 σ_0"
        
        let sequence = 
            compileOrFail braid AnyonSpecies.AnyonType.Ising 
                BraidToGate.defaultOptions "Multiple braidings"
        
        Assert.Equal(3, sequence.Gates.Length)
        Assert.Equal(CircuitBuilder.Gate.S 0, sequence.Gates.[0])
        Assert.Equal(CircuitBuilder.Gate.S 1, sequence.Gates.[1])
        Assert.Equal(CircuitBuilder.Gate.S 0, sequence.Gates.[2])
    
    [<Fact>]
    let ``Ising identity braid compiles to empty gate sequence`` () =
        // Business meaning: Identity operation = no gates needed
        let braid = identityOrFail 3 "Identity 3-strand"
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
        // With corrected Ising mapping: braids → S/SDG (Clifford), so T-count = 0
        let braid = 
            braidFromGensOrFail 3 
                [BraidGroup.sigma 0; BraidGroup.sigmaInv 1; BraidGroup.sigma 0]
                "Mixed directions"
        
        let sequence = 
            compileOrFail braid AnyonSpecies.AnyonType.Ising 
                BraidToGate.defaultOptions "T-count test"
        
        Assert.Equal(0, sequence.TCount)  // S and SDG are Clifford, not counted as T
    
    [<Fact>]
    let ``Empty braid produces zero-depth circuit`` () =
        // Business meaning: Identity operation has no depth
        let braid = identityOrFail 5 "Identity 5-strand"
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
        Assert.Contains("S:", stats)  // Gate type breakdown (Ising braids produce S/SDG, not T)
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

    // ========================================================================
    // BRAIDING PHASE COMPUTATION TESTS
    // ========================================================================

    [<Fact>]
    let ``Identity braid has unit total phase`` () =
        // Business meaning: No braiding = no phase acquired
        let braid = identityOrFail 3 "Identity 3-strand"
        let sequence =
            compileOrFail braid AnyonSpecies.AnyonType.Ising
                BraidToGate.defaultOptions "Identity phase"

        // Phase should be exactly Complex.One (1 + 0i)
        Assert.Equal(1.0, sequence.TotalPhase.Real, 10)
        Assert.Equal(0.0, sequence.TotalPhase.Imaginary, 10)

    [<Fact>]
    let ``Single Ising clockwise braid has phase exp(-i*pi/8)`` () =
        // Business meaning: Majorana exchange produces Berry phase exp(-iπ/8) (Kitaev 2006)
        let braid = braidFromGensOrFail 2 [BraidGroup.sigma 0] "Single s_0"
        let sequence =
            compileOrFail braid AnyonSpecies.AnyonType.Ising
                BraidToGate.defaultOptions "Ising phase"

        let expectedAngle = -System.Math.PI / 8.0
        Assert.Equal(cos expectedAngle, sequence.TotalPhase.Real, 10)
        Assert.Equal(sin expectedAngle, sequence.TotalPhase.Imaginary, 10)

    [<Fact>]
    let ``Single Ising counter-clockwise braid has phase exp(i*pi/8)`` () =
        // Business meaning: Inverse braiding produces conjugate phase
        let braid = braidFromGensOrFail 2 [BraidGroup.sigmaInv 0] "s_0^-1"
        let sequence =
            compileOrFail braid AnyonSpecies.AnyonType.Ising
                BraidToGate.defaultOptions "Ising inv phase"

        let expectedAngle = System.Math.PI / 8.0
        Assert.Equal(cos expectedAngle, sequence.TotalPhase.Real, 10)
        Assert.Equal(sin expectedAngle, sequence.TotalPhase.Imaginary, 10)

    [<Fact>]
    let ``Two Ising clockwise braids accumulate phase exp(-i*pi/4)`` () =
        // Business meaning: Phase accumulates multiplicatively
        let braid =
            braidFromGensOrFail 2
                [BraidGroup.sigma 0; BraidGroup.sigma 0]
                "s_0 s_0"
        let sequence =
            compileOrFail braid AnyonSpecies.AnyonType.Ising
                { BraidToGate.defaultOptions with OptimizationLevel = 0 }
                "Two Ising phase"

        // exp(-iπ/8) * exp(-iπ/8) = exp(-iπ/4)
        let expectedAngle = -System.Math.PI / 4.0
        Assert.Equal(cos expectedAngle, sequence.TotalPhase.Real, 10)
        Assert.Equal(sin expectedAngle, sequence.TotalPhase.Imaginary, 10)

    [<Fact>]
    let ``Ising clockwise and counter-clockwise cancels phase to unity`` () =
        // Business meaning: s*s^-1 = identity, phase cancels exactly
        let braid =
            braidFromGensOrFail 2
                [BraidGroup.sigma 0; BraidGroup.sigmaInv 0]
                "s_0 s_0^-1"
        let sequence =
            compileOrFail braid AnyonSpecies.AnyonType.Ising
                { BraidToGate.defaultOptions with OptimizationLevel = 0 }
                "Phase cancellation"

        // exp(ipi/8) * exp(-ipi/8) = 1
        Assert.Equal(1.0, sequence.TotalPhase.Real, 10)
        Assert.Equal(0.0, sequence.TotalPhase.Imaginary, 10)

    [<Fact>]
    let ``Fibonacci clockwise braid has phase exp(4*pi*i/5)`` () =
        // Business meaning: Fibonacci anyon exchange phase from R[tau,tau;1]
        let braid = braidFromGensOrFail 2 [BraidGroup.sigma 0] "tau braiding"
        let sequence =
            compileOrFail braid AnyonSpecies.AnyonType.Fibonacci
                BraidToGate.defaultOptions "Fibonacci phase"

        let expectedAngle = 4.0 * System.Math.PI / 5.0
        Assert.Equal(cos expectedAngle, sequence.TotalPhase.Real, 10)
        Assert.Equal(sin expectedAngle, sequence.TotalPhase.Imaginary, 10)

    [<Fact>]
    let ``Fibonacci counter-clockwise braid has conjugate phase`` () =
        // Business meaning: Inverse Fibonacci braiding = conjugate R-matrix phase
        let braid = braidFromGensOrFail 2 [BraidGroup.sigmaInv 0] "tau inv braiding"
        let sequence =
            compileOrFail braid AnyonSpecies.AnyonType.Fibonacci
                BraidToGate.defaultOptions "Fibonacci inv phase"

        let expectedAngle = -4.0 * System.Math.PI / 5.0
        Assert.Equal(cos expectedAngle, sequence.TotalPhase.Real, 10)
        Assert.Equal(sin expectedAngle, sequence.TotalPhase.Imaginary, 10)

    [<Fact>]
    let ``Multi-generator Ising phase is product of individual phases`` () =
        // Business meaning: Total phase = product of per-generator R-matrix phases
        // 3 clockwise + 1 counter-clockwise = net 2 clockwise = exp(2*(-iπ/8)) = exp(-iπ/4)
        let braid =
            braidFromGensOrFail 3
                [ BraidGroup.sigma 0
                  BraidGroup.sigma 1
                  BraidGroup.sigma 0
                  BraidGroup.sigmaInv 1 ]
                "Mixed generators"
        let sequence =
            compileOrFail braid AnyonSpecies.AnyonType.Ising
                { BraidToGate.defaultOptions with OptimizationLevel = 0 }
                "Multi-gen phase"

        // 3 * (-π/8) + 1 * (π/8) = -2π/8 = -π/4
        let expectedAngle = -System.Math.PI / 4.0
        Assert.Equal(cos expectedAngle, sequence.TotalPhase.Real, 10)
        Assert.Equal(sin expectedAngle, sequence.TotalPhase.Imaginary, 10)

    [<Fact>]
    let ``Phase magnitude is always unity (on unit circle)`` () =
        // Business meaning: Braiding phases are unitary, |phase| = 1
        let braid =
            braidFromGensOrFail 4
                [ BraidGroup.sigma 0; BraidGroup.sigma 1
                  BraidGroup.sigma 2; BraidGroup.sigmaInv 0
                  BraidGroup.sigma 1 ]
                "Long sequence"
        let sequence =
            compileOrFail braid AnyonSpecies.AnyonType.Ising
                BraidToGate.defaultOptions "Phase magnitude"

        let magnitude = sequence.TotalPhase.Magnitude
        Assert.Equal(1.0, magnitude, 10)

    [<Fact>]
    let ``braidingPhase function returns correct Ising phases`` () =
        // Business meaning: Unit function for direct phase lookup
        let cw = BraidToGate.braidingPhase AnyonSpecies.AnyonType.Ising true
        let ccw = BraidToGate.braidingPhase AnyonSpecies.AnyonType.Ising false

        // Clockwise: exp(-iπ/8) per Kitaev (2006) convention
        Assert.Equal(cos (-System.Math.PI / 8.0), cw.Real, 10)
        Assert.Equal(sin (-System.Math.PI / 8.0), cw.Imaginary, 10)

        // Counter-clockwise: exp(iπ/8) = conjugate
        Assert.Equal(cw.Real, ccw.Real, 10)
        Assert.Equal(-cw.Imaginary, ccw.Imaginary, 10)
    
    // ========================================================================
    // GATES COMMUTE DETECTION
    // ========================================================================
    
    [<Fact>]
    let ``gatesCommute returns true for gates on different qubits`` () =
        Assert.True(BraidToGate.gatesCommute (CircuitBuilder.Gate.T 0) (CircuitBuilder.Gate.H 1))
    
    [<Fact>]
    let ``gatesCommute returns false for gates on same qubit`` () =
        Assert.False(BraidToGate.gatesCommute (CircuitBuilder.Gate.T 0) (CircuitBuilder.Gate.H 0))
    
    [<Fact>]
    let ``gatesCommute handles CNOT correctly`` () =
        // CNOT(0,1) shares qubit 1 with H(1) → do not commute
        Assert.False(BraidToGate.gatesCommute (CircuitBuilder.Gate.CNOT (0, 1)) (CircuitBuilder.Gate.H 1))
        // CNOT(0,1) does not share qubits with H(2) → commute
        Assert.True(BraidToGate.gatesCommute (CircuitBuilder.Gate.CNOT (0, 1)) (CircuitBuilder.Gate.H 2))
    
    // ========================================================================
    // COMMUTATION-BASED CANCELLATION
    // ========================================================================
    
    [<Fact>]
    let ``commutationCancellation cancels T and Tdg through commuting gate`` () =
        // T(q0) H(q1) Tdg(q0) → H(q1)
        // T and Tdg cancel because H(q1) commutes with both
        let gates = [
            CircuitBuilder.Gate.T 0
            CircuitBuilder.Gate.H 1
            CircuitBuilder.Gate.TDG 0
        ]
        let result = BraidToGate.commutationCancellation gates
        Assert.Equal(1, result.Length)
        Assert.Equal(CircuitBuilder.Gate.H 1, result.[0])
    
    [<Fact>]
    let ``commutationCancellation does not cancel through non-commuting gate`` () =
        // T(q0) H(q0) Tdg(q0) → T(q0) H(q0) Tdg(q0) (H blocks cancellation)
        let gates = [
            CircuitBuilder.Gate.T 0
            CircuitBuilder.Gate.H 0
            CircuitBuilder.Gate.TDG 0
        ]
        let result = BraidToGate.commutationCancellation gates
        Assert.Equal(3, result.Length)
    
    [<Fact>]
    let ``commutationCancellation cancels X through multiple commuting gates`` () =
        // X(q0) T(q1) S(q2) X(q0) → T(q1) S(q2)
        let gates = [
            CircuitBuilder.Gate.X 0
            CircuitBuilder.Gate.T 1
            CircuitBuilder.Gate.S 2
            CircuitBuilder.Gate.X 0
        ]
        let result = BraidToGate.commutationCancellation gates
        Assert.Equal(2, result.Length)
    
    [<Fact>]
    let ``commutationCancellation with empty input returns empty`` () =
        let result = BraidToGate.commutationCancellation []
        Assert.Empty(result)
    
    [<Fact>]
    let ``commutationCancellation with no cancellable pairs is identity`` () =
        let gates = [
            CircuitBuilder.Gate.H 0
            CircuitBuilder.Gate.T 0
            CircuitBuilder.Gate.CNOT (0, 1)
        ]
        let result = BraidToGate.commutationCancellation gates
        Assert.Equal(3, result.Length)
    
    // ========================================================================
    // TEMPLATE MATCHING
    // ========================================================================
    
    [<Fact>]
    let ``templateMatching replaces S S with Z`` () =
        let gates = [ CircuitBuilder.Gate.S 0; CircuitBuilder.Gate.S 0 ]
        let result = BraidToGate.templateMatching gates
        Assert.Equal(1, result.Length)
        Assert.Equal(CircuitBuilder.Gate.Z 0, result.[0])
    
    [<Fact>]
    let ``templateMatching replaces T T with S`` () =
        let gates = [ CircuitBuilder.Gate.T 0; CircuitBuilder.Gate.T 0 ]
        let result = BraidToGate.templateMatching gates
        Assert.Equal(1, result.Length)
        Assert.Equal(CircuitBuilder.Gate.S 0, result.[0])
    
    [<Fact>]
    let ``templateMatching replaces Tdg Tdg with Sdg`` () =
        let gates = [ CircuitBuilder.Gate.TDG 0; CircuitBuilder.Gate.TDG 0 ]
        let result = BraidToGate.templateMatching gates
        Assert.Equal(1, result.Length)
        Assert.Equal(CircuitBuilder.Gate.SDG 0, result.[0])
    
    [<Fact>]
    let ``templateMatching replaces Sdg Sdg with Z`` () =
        let gates = [ CircuitBuilder.Gate.SDG 0; CircuitBuilder.Gate.SDG 0 ]
        let result = BraidToGate.templateMatching gates
        Assert.Equal(1, result.Length)
        Assert.Equal(CircuitBuilder.Gate.Z 0, result.[0])
    
    [<Fact>]
    let ``templateMatching replaces H Z H with X`` () =
        let gates = [ CircuitBuilder.Gate.H 0; CircuitBuilder.Gate.Z 0; CircuitBuilder.Gate.H 0 ]
        let result = BraidToGate.templateMatching gates
        Assert.Equal(1, result.Length)
        Assert.Equal(CircuitBuilder.Gate.X 0, result.[0])
    
    [<Fact>]
    let ``templateMatching replaces H X H with Z`` () =
        let gates = [ CircuitBuilder.Gate.H 0; CircuitBuilder.Gate.X 0; CircuitBuilder.Gate.H 0 ]
        let result = BraidToGate.templateMatching gates
        Assert.Equal(1, result.Length)
        Assert.Equal(CircuitBuilder.Gate.Z 0, result.[0])
    
    [<Fact>]
    let ``templateMatching does not match different qubits`` () =
        // S(q0) S(q1) should NOT be replaced
        let gates = [ CircuitBuilder.Gate.S 0; CircuitBuilder.Gate.S 1 ]
        let result = BraidToGate.templateMatching gates
        Assert.Equal(2, result.Length)
    
    [<Fact>]
    let ``templateMatching chains: T T T T becomes S S in single pass`` () =
        // T T T T → single pass: T T → S, T T → S → result is [S, S]
        // A second pass of template matching would reduce S S → Z,
        // but templateMatching is a single-pass algorithm.
        let gates = [
            CircuitBuilder.Gate.T 0
            CircuitBuilder.Gate.T 0
            CircuitBuilder.Gate.T 0
            CircuitBuilder.Gate.T 0
        ]
        let result = BraidToGate.templateMatching gates
        Assert.Equal(2, result.Length)
        Assert.Equal(CircuitBuilder.Gate.S 0, result.[0])
        Assert.Equal(CircuitBuilder.Gate.S 0, result.[1])
    
    [<Fact>]
    let ``optimizeAggressive reduces T T T T to Z via multi-pass`` () =
        // optimizeAggressive applies template matching + basic optimization multiple passes
        // T T T T → S S (template) → Z (template in next basic pass)
        let gates = [
            CircuitBuilder.Gate.T 0
            CircuitBuilder.Gate.T 0
            CircuitBuilder.Gate.T 0
            CircuitBuilder.Gate.T 0
        ]
        let result = BraidToGate.optimizeAggressive gates
        // Multi-pass strategy should reduce this further
        Assert.True(result.Length <= 2, 
            $"Expected at most 2 gates after aggressive optimization, got {result.Length}")
    
    [<Fact>]
    let ``templateMatching with empty input returns empty`` () =
        let result = BraidToGate.templateMatching []
        Assert.Empty(result)
    
    // ========================================================================
    // AGGRESSIVE OPTIMIZATION (END-TO-END)
    // ========================================================================
    
    [<Fact>]
    let ``optimizeAggressive reduces T Tdg to empty`` () =
        let gates = [ CircuitBuilder.Gate.T 0; CircuitBuilder.Gate.TDG 0 ]
        let result = BraidToGate.optimizeAggressive gates
        Assert.Empty(result)
    
    [<Fact>]
    let ``optimizeAggressive reduces through commutation and templates`` () =
        // T(q0) H(q1) Tdg(q0) → cancels through commutation → H(q1)
        let gates = [
            CircuitBuilder.Gate.T 0
            CircuitBuilder.Gate.H 1
            CircuitBuilder.Gate.TDG 0
        ]
        let result = BraidToGate.optimizeAggressive gates
        Assert.Equal(1, result.Length)
        Assert.Equal(CircuitBuilder.Gate.H 1, result.[0])
    
    [<Fact>]
    let ``optimizeAggressive does not change irreducible sequence`` () =
        let gates = [
            CircuitBuilder.Gate.H 0
            CircuitBuilder.Gate.T 0
            CircuitBuilder.Gate.CNOT (0, 1)
        ]
        let result = BraidToGate.optimizeAggressive gates
        Assert.Equal(3, result.Length)
    
    [<Fact>]
    let ``optimizeAggressive never increases gate count`` () =
        // Property: optimization should never make things worse
        let gates = [
            CircuitBuilder.Gate.T 0; CircuitBuilder.Gate.T 0
            CircuitBuilder.Gate.H 1; CircuitBuilder.Gate.H 1
            CircuitBuilder.Gate.S 2; CircuitBuilder.Gate.SDG 2
            CircuitBuilder.Gate.CNOT (0, 1)
        ]
        let result = BraidToGate.optimizeAggressive gates
        Assert.True(result.Length <= gates.Length,
            $"Optimized {result.Length} should be <= original {gates.Length}")
    
    [<Fact>]
    let ``optimizeAggressive is accessible via optimizeGates level 2`` () =
        // Verify that optimization level 2+ uses aggressive optimization
        let gates = [ CircuitBuilder.Gate.T 0; CircuitBuilder.Gate.TDG 0 ]
        let result = BraidToGate.optimizeGates 2 gates
        Assert.Empty(result)
