namespace FSharp.Azure.Quantum.Tests

open Xunit
open FSharp.Azure.Quantum
open FSharp.Azure.Quantum.CircuitBuilder
open FSharp.Azure.Quantum.Core

/// Comprehensive tests for GateTranspiler module
/// 
/// Tests cover:
/// - Phase gate decompositions (S, SDG, T, TDG → RZ)
/// - CZ decomposition (CZ → H+CNOT+H)
/// - CCX decomposition (Toffoli → 6xCNOT)
/// - Backend-aware transpilation
/// - Transpilation statistics
module GateTranspilerTests =
    
    // ========================================================================
    // PHASE GATE DECOMPOSITION TESTS
    // ========================================================================
    
    [<Fact>]
    let ``Transpile S gate to RZ for IonQ`` () =
        let circuit = {
            QubitCount = 1
            Gates = [S 0]
        }
        
        let transpiled = GateTranspiler.transpileForBackend "ionq.simulator" circuit
        
        // Should decompose S → RZ(π/2)
        Assert.Equal(1, transpiled.Gates.Length)
        match transpiled.Gates.[0] with
        | RZ (q, angle) ->
            Assert.Equal(0, q)
            Assert.InRange(angle, 1.5707, 1.571)  // π/2 ≈ 1.5708
        | _ -> Assert.True(false, "Expected RZ gate")
    
    [<Fact>]
    let ``Transpile SDG gate to RZ for IonQ`` () =
        let circuit = {
            QubitCount = 1
            Gates = [SDG 0]
        }
        
        let transpiled = GateTranspiler.transpileForBackend "ionq.simulator" circuit
        
        // Should decompose SDG → RZ(-π/2)
        Assert.Equal(1, transpiled.Gates.Length)
        match transpiled.Gates.[0] with
        | RZ (q, angle) ->
            Assert.Equal(0, q)
            Assert.InRange(angle, -1.571, -1.5707)  // -π/2 ≈ -1.5708
        | _ -> Assert.True(false, "Expected RZ gate")
    
    [<Fact>]
    let ``Transpile T gate to RZ for IonQ`` () =
        let circuit = {
            QubitCount = 1
            Gates = [T 0]
        }
        
        let transpiled = GateTranspiler.transpileForBackend "ionq.simulator" circuit
        
        // Should decompose T → RZ(π/4)
        Assert.Equal(1, transpiled.Gates.Length)
        match transpiled.Gates.[0] with
        | RZ (q, angle) ->
            Assert.Equal(0, q)
            Assert.InRange(angle, 0.785, 0.786)  // π/4 ≈ 0.7854
        | _ -> Assert.True(false, "Expected RZ gate")
    
    [<Fact>]
    let ``Transpile TDG gate to RZ for IonQ`` () =
        let circuit = {
            QubitCount = 1
            Gates = [TDG 0]
        }
        
        let transpiled = GateTranspiler.transpileForBackend "ionq.simulator" circuit
        
        // Should decompose TDG → RZ(-π/4)
        Assert.Equal(1, transpiled.Gates.Length)
        match transpiled.Gates.[0] with
        | RZ (q, angle) ->
            Assert.Equal(0, q)
            Assert.InRange(angle, -0.786, -0.785)  // -π/4 ≈ -0.7854
        | _ -> Assert.True(false, "Expected RZ gate")
    
    [<Fact>]
    let ``Multiple phase gates transpiled correctly`` () =
        let circuit = {
            QubitCount = 2
            Gates = [S 0; T 1; SDG 0; TDG 1]
        }
        
        let transpiled = GateTranspiler.transpileForBackend "ionq.simulator" circuit
        
        // All 4 gates should be RZ gates
        Assert.Equal(4, transpiled.Gates.Length)
        Assert.All(transpiled.Gates, fun gate ->
            match gate with
            | RZ _ -> ()
            | _ -> failwith "Expected all RZ gates")
    
    // ========================================================================
    // CZ DECOMPOSITION TESTS
    // ========================================================================
    
    [<Fact>]
    let ``Transpile CZ gate to H+CNOT+H for IonQ`` () =
        let circuit = {
            QubitCount = 2
            Gates = [CZ (0, 1)]
        }
        
        let transpiled = GateTranspiler.transpileForBackend "ionq.simulator" circuit
        
        // Should decompose CZ → H + CNOT + H
        Assert.Equal(3, transpiled.Gates.Length)
        
        match transpiled.Gates with
        | [H q1; CNOT (c, t); H q2] ->
            Assert.Equal(1, q1)  // H on target
            Assert.Equal(0, c)   // Control
            Assert.Equal(1, t)   // Target
            Assert.Equal(1, q2)  // H on target
        | _ -> Assert.True(false, "Expected H + CNOT + H pattern")
    
    [<Fact>]
    let ``CZ not decomposed for Rigetti`` () =
        let circuit = {
            QubitCount = 2
            Gates = [CZ (0, 1)]
        }
        
        let transpiled = GateTranspiler.transpileForBackend "rigetti.sim.qvm" circuit
        
        // Rigetti supports CZ natively - should not decompose
        Assert.Equal(1, transpiled.Gates.Length)
        match transpiled.Gates.[0] with
        | CZ (0, 1) -> ()
        | _ -> Assert.True(false, "CZ should remain unchanged for Rigetti")
    
    // ========================================================================
    // CCX (TOFFOLI) DECOMPOSITION TESTS
    // ========================================================================
    
    [<Fact>]
    let ``Transpile CCX gate for IonQ`` () =
        let circuit = {
            QubitCount = 3
            Gates = [CCX (0, 1, 2)]
        }
        
        let transpiled = GateTranspiler.transpileForBackend "ionq.simulator" circuit
        
        // CCX decomposes into multiple gates (standard decomposition)
        // Should use RZ instead of T/TDG for IonQ
        Assert.True(transpiled.Gates.Length > 1, "CCX should be decomposed")
        
        // Check that no CCX gates remain
        let hasCCX = transpiled.Gates |> List.exists (function | CCX _ -> true | _ -> false)
        Assert.False(hasCCX, "CCX should be fully decomposed")
        
        // Should only contain gates supported by IonQ
        Assert.All(transpiled.Gates, fun gate ->
            match gate with
            | X _ | Y _ | Z _ | H _ | RX _ | RY _ | RZ _ | CNOT _ | SWAP _ -> ()
            | _ -> failwith $"Unsupported gate for IonQ: {gate}")
    
    [<Fact>]
    let ``Transpile CCX gate for Rigetti`` () =
        let circuit = {
            QubitCount = 3
            Gates = [CCX (0, 1, 2)]
        }
        
        let transpiled = GateTranspiler.transpileForBackend "rigetti.sim.qvm" circuit
        
        // CCX decomposes, Rigetti supports CZ but not T gates
        Assert.True(transpiled.Gates.Length > 1, "CCX should be decomposed")
        
        // Should only contain gates supported by Rigetti
        Assert.All(transpiled.Gates, fun gate ->
            match gate with
            | X _ | Y _ | Z _ | H _ | RX _ | RY _ | RZ _ | CNOT _ | CZ _ | SWAP _ -> ()
            | _ -> failwith $"Unsupported gate for Rigetti: {gate}")
    
    // ========================================================================
    // BACKEND-AWARE TRANSPILATION TESTS
    // ========================================================================
    
    [<Fact>]
    let ``No transpilation needed for local simulator`` () =
        let circuit = {
            QubitCount = 3
            Gates = [S 0; T 1; CZ (0, 1); CCX (0, 1, 2)]
        }
        
        let transpiled = GateTranspiler.transpileForBackend "local.simulator" circuit
        
        // Local simulator supports all gates - no transpilation
        Assert.Equal(circuit.Gates.Length, transpiled.Gates.Length)
        Assert.Equal<Gate list>(circuit.Gates, transpiled.Gates)
    
    [<Fact>]
    let ``IonQ transpilation preserves native gates`` () =
        let circuit = {
            QubitCount = 2
            Gates = [H 0; X 1; CNOT (0, 1); RX (0, 1.5); SWAP (0, 1)]
        }
        
        let transpiled = GateTranspiler.transpileForBackend "ionq.simulator" circuit
        
        // These gates are natively supported - should not change
        Assert.Equal(circuit.Gates.Length, transpiled.Gates.Length)
        Assert.Equal<Gate list>(circuit.Gates, transpiled.Gates)
    
    [<Fact>]
    let ``Mixed circuit transpilation for IonQ`` () =
        let circuit = {
            QubitCount = 2
            Gates = [
                H 0           // Native - keep
                S 0           // Decompose to RZ
                CNOT (0, 1)   // Native - keep
                CZ (0, 1)     // Decompose to H+CNOT+H
                T 1           // Decompose to RZ
            ]
        }
        
        let transpiled = GateTranspiler.transpileForBackend "ionq.simulator" circuit
        
        // H, CNOT stay; S, CZ, T decompose
        // Original: 5 gates
        // Transpiled: H, RZ(S), CNOT, H+CNOT+H(CZ), RZ(T) = 7 gates
        Assert.Equal(7, transpiled.Gates.Length)
    
    // ========================================================================
    // BACKEND CONSTRAINTS TRANSPILATION TESTS
    // ========================================================================
    
    [<Fact>]
    let ``Transpile using backend constraints`` () =
        let circuit = {
            QubitCount = 2
            Gates = [S 0; CZ (0, 1)]
        }
        
        let ionqConstraints = CircuitValidator.BackendConstraints.ionqSimulator()
        let transpiled = GateTranspiler.transpile ionqConstraints circuit
        
        // Both gates should be decomposed for IonQ
        Assert.True(transpiled.Gates.Length > 2, "Gates should be decomposed")
    
    [<Fact>]
    let ``needsTranspilation detects unsupported gates`` () =
        let circuit = {
            QubitCount = 2
            Gates = [S 0; T 1; CZ (0, 1)]
        }
        
        let ionqConstraints = CircuitValidator.BackendConstraints.ionqSimulator()
        let needs = GateTranspiler.needsTranspilation ionqConstraints circuit
        
        Assert.True(needs, "Circuit has unsupported gates for IonQ")
    
    [<Fact>]
    let ``needsTranspilation returns false for supported gates`` () =
        let circuit = {
            QubitCount = 2
            Gates = [H 0; CNOT (0, 1); RZ (1, 1.5)]
        }
        
        let ionqConstraints = CircuitValidator.BackendConstraints.ionqSimulator()
        let needs = GateTranspiler.needsTranspilation ionqConstraints circuit
        
        Assert.False(needs, "All gates are supported by IonQ")
    
    // ========================================================================
    // TRANSPILATION STATISTICS TESTS
    // ========================================================================
    
    [<Fact>]
    let ``getTranspilationStats returns correct counts`` () =
        let circuit = {
            QubitCount = 2
            Gates = [H 0; S 0; CNOT (0, 1); CZ (0, 1)]
        }
        
        let ionqConstraints = CircuitValidator.BackendConstraints.ionqSimulator()
        let (original, transpiled, decomposed) = 
            GateTranspiler.getTranspilationStats ionqConstraints circuit
        
        // Original: 4 gates
        Assert.Equal(4, original)
        
        // H and CNOT stay (2), S→RZ (1), CZ→H+CNOT+H (3) = 6 total
        Assert.Equal(6, transpiled)
        
        // 2 gates decomposed (S and CZ)
        Assert.Equal(2, decomposed)
    
    // ========================================================================
    // EQUIVALENCE TESTS (Verify decompositions are correct)
    // ========================================================================
    
    [<Fact>]
    let ``S decomposition is equivalent to S gate`` () =
        // S and RZ(π/2) differ by a global phase: S = e^(iπ/4) · RZ(π/2)
        // Global phase doesn't affect measurement probabilities
        
        // Test that S and RZ(π/2) produce the same measurement probabilities
        let initialState = LocalSimulator.StateVector.init 1
        let stateWith1 = LocalSimulator.Gates.applyX 0 initialState
        
        // Apply S gate
        let resultS = LocalSimulator.Gates.applyS 0 stateWith1
        
        // Apply RZ(π/2)
        let resultRZ = LocalSimulator.Gates.applyRz 0 (System.Math.PI / 2.0) stateWith1
        
        // Check that measurement probabilities are identical (global phase doesn't matter)
        for i in 0 .. (LocalSimulator.StateVector.dimension resultS - 1) do
            let probS = 
                let amp = LocalSimulator.StateVector.getAmplitude i resultS
                amp.Real * amp.Real + amp.Imaginary * amp.Imaginary
            let probRZ = 
                let amp = LocalSimulator.StateVector.getAmplitude i resultRZ
                amp.Real * amp.Real + amp.Imaginary * amp.Imaginary
            Assert.Equal(probS, probRZ, 10)  // Probabilities should match
    
    [<Fact>]
    let ``CZ decomposition is equivalent to CZ gate`` () =
        // Original: CZ
        let originalCircuit = { QubitCount = 2; Gates = [CZ (0, 1)] }
        
        // Decomposed: H + CNOT + H
        let decomposedCircuit = { QubitCount = 2; Gates = [H 1; CNOT (0, 1); H 1] }
        
        // Test on |11⟩ state (should add -1 phase)
        let initialState = LocalSimulator.StateVector.init 2
        let state11 = 
            initialState
            |> LocalSimulator.Gates.applyX 0
            |> LocalSimulator.Gates.applyX 1
        
        // Apply CZ
        let resultCZ = LocalSimulator.Gates.applyCZ 0 1 state11
        
        // Apply H + CNOT + H
        let resultDecomposed = 
            state11
            |> LocalSimulator.Gates.applyH 1
            |> LocalSimulator.Gates.applyCNOT 0 1
            |> LocalSimulator.Gates.applyH 1
        
        // States should be equivalent
        for i in 0 .. (LocalSimulator.StateVector.dimension resultCZ - 1) do
            let ampCZ = LocalSimulator.StateVector.getAmplitude i resultCZ
            let ampDec = LocalSimulator.StateVector.getAmplitude i resultDecomposed
            Assert.Equal(ampCZ.Real, ampDec.Real, 5)
            Assert.Equal(ampCZ.Imaginary, ampDec.Imaginary, 5)
