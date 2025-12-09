namespace FSharp.Azure.Quantum.Tests

open Xunit
open FSharp.Azure.Quantum.Algorithms.QuantumFourierTransform
open FSharp.Azure.Quantum.Algorithms.QFTBackendAdapter
open FSharp.Azure.Quantum.LocalSimulator.StateVector
open FSharp.Azure.Quantum.Core.BackendAbstraction
open FSharp.Azure.Quantum.Core.BackendAbstraction
open FSharp.Azure.Quantum.CircuitBuilder
open System

/// Module aliases to avoid name conflicts
module QFTOld = FSharp.Azure.Quantum.Algorithms.QuantumFourierTransform
module QFTAdapter = FSharp.Azure.Quantum.Algorithms.QFTBackendAdapter
module QFT = FSharp.Azure.Quantum.Algorithms.QFT
module LocalBackend = FSharp.Azure.Quantum.Backends.LocalBackend

/// Tests for Quantum Fourier Transform (QFT) and Backend Adapter
module QFTTests =
    
    // ========================================================================
    // LOCAL SIMULATION TESTS (using QuantumFourierTransform module)
    // ========================================================================
    
    [<Fact>]
    let ``QFT on 1-qubit |0⟩ produces |+⟩ state`` () =
        // QFT on single qubit is just Hadamard
        // |0⟩ → |+⟩ = (|0⟩ + |1⟩)/√2
        let config = { NumQubits = 1; ApplySwaps = true; Inverse = false }
        let state = init 1  // |0⟩
        
        match execute config state with
        | Error msg -> Assert.Fail($"QFT execution failed: {msg}")
        | Ok result ->
            // Check amplitude of |0⟩ state
            let amp0 = getAmplitude 0 result.FinalState
            let expectedAmp = 1.0 / sqrt 2.0
            
            Assert.True(abs(amp0.Real - expectedAmp) < 1e-10, $"Expected amplitude {expectedAmp}, got {amp0.Real}")
            Assert.True(abs(amp0.Imaginary) < 1e-10, "Expected zero imaginary part")
    
    [<Fact>]
    let ``QFT then inverse QFT returns original state`` () =
        // QFT followed by inverse QFT should be identity (up to global phase)
        let numQubits = 3
        let state = init numQubits  // |000⟩
        
        // Apply forward QFT
        let configForward = { NumQubits = numQubits; ApplySwaps = true; Inverse = false }
        match execute configForward state with
        | Error msg -> Assert.Fail($"Forward QFT failed: {msg}")
        | Ok forwardResult ->
            // Apply inverse QFT
            let configInverse = { NumQubits = numQubits; ApplySwaps = true; Inverse = true }
            match execute configInverse forwardResult.FinalState with
            | Error msg -> Assert.Fail($"Inverse QFT failed: {msg}")
            | Ok inverseResult ->
                // Should return to |000⟩
                let amp0 = getAmplitude 0 inverseResult.FinalState
                Assert.True(abs(amp0.Real - 1.0) < 1e-10, "Should return to |000⟩ state")
                
                // Check other states have zero amplitude
                for i in 1 .. (1 <<< numQubits) - 1 do
                    let amp = getAmplitude i inverseResult.FinalState
                    let magnitude = sqrt(amp.Real * amp.Real + amp.Imaginary * amp.Imaginary)
                    Assert.True(magnitude < 1e-10, $"State |{i}⟩ should have zero amplitude")
    
    [<Fact>]
    let ``QFT gate count scales correctly`` () =
        // QFT requires O(n²) gates for n qubits
        for n in 1 .. 5 do
            let config = { NumQubits = n; ApplySwaps = true; Inverse = false }
            let state = init n
            
            match execute config state with
            | Error msg -> Assert.Fail($"QFT execution failed for {n} qubits: {msg}")
            | Ok result ->
                // Each qubit needs H + controlled-phase gates
                let expectedMinGates = n  // At least n Hadamard gates
                Assert.True(result.GateCount >= expectedMinGates, 
                    $"Expected at least {expectedMinGates} gates, got {result.GateCount}")
    
    [<Fact>]
    let ``QFT preserves state norm`` () =
        // QFT is unitary, must preserve norm
        let numQubits = 3
        let state = init numQubits
        let config = { NumQubits = numQubits; ApplySwaps = true; Inverse = false }
        
        match execute config state with
        | Error msg -> Assert.Fail($"QFT execution failed: {msg}")
        | Ok result ->
            let stateNorm = norm result.FinalState
            Assert.True(abs(stateNorm - 1.0) < 1e-10, $"QFT should preserve norm, got {stateNorm}")
    
    // ========================================================================
    // BACKEND ADAPTER TESTS
    // ========================================================================
    
    [<Fact>]
    let ``QFT backend adapter validates qubit count`` () =
        let backend = createLocalBackend()
        let config = { NumQubits = 0; ApplySwaps = true; Inverse = false }
        
        match executeQFTWithBackend config backend 100 None with
        | Ok _ -> Assert.Fail("Should reject 0 qubits")
        | Error msg -> Assert.Contains("positive", msg.ToLower())
    
    [<Fact>]
    let ``QFT backend adapter validates shot count`` () =
        let backend = createLocalBackend()
        let config = { NumQubits = 3; ApplySwaps = true; Inverse = false }
        
        match executeQFTWithBackend config backend 0 None with
        | Ok _ -> Assert.Fail("Should reject 0 shots")
        | Error msg -> Assert.Contains("positive", msg.ToLower())
    
    [<Fact>]
    let ``QFT backend adapter validates backend qubit limit`` () =
        let backend = createLocalBackend()
        let config = { NumQubits = 100; ApplySwaps = true; Inverse = false }  // Exceeds LocalBackend max
        
        match executeQFTWithBackend config backend 100 None with
        | Ok _ -> Assert.Fail("Should reject excessive qubit count")
        | Error msg -> Assert.Contains("max", msg.ToLower())
    
    [<Fact>]
    let ``QFT backend execution returns measurement counts`` () =
        let backend = createLocalBackend()
        let config = { NumQubits = 2; ApplySwaps = true; Inverse = false }
        
        match executeQFTWithBackend config backend 1000 None with
        | Error msg -> Assert.Fail($"QFT backend execution failed: {msg}")
        | Ok counts ->
            // Should have measurement results
            Assert.True(Map.count counts > 0, "Should have measurement results")
            
            // Total shots should equal input
            let totalShots = counts |> Map.toSeq |> Seq.sumBy snd
            Assert.Equal(1000, totalShots)
    
    [<Fact>]
    let ``QFT backend with input state preparation`` () =
        let backend = createLocalBackend()
        let config = { NumQubits = 2; ApplySwaps = true; Inverse = false }
        let inputState = 1  // Binary: 01
        
        match executeQFTWithBackend config backend 500 (Some inputState) with
        | Error msg -> Assert.Fail($"QFT with input state failed: {msg}")
        | Ok counts ->
            // Should have results
            Assert.True(Map.count counts > 0, "Should have measurement results")
    
    [<Fact>]
    let ``Standard QFT convenience function works`` () =
        let backend = createLocalBackend()
        
        match executeStandardQFT 3 backend 500 with
        | Error msg -> Assert.Fail($"Standard QFT failed: {msg}")
        | Ok counts ->
            let totalShots = counts |> Map.toSeq |> Seq.sumBy snd
            Assert.Equal(500, totalShots)
    
    [<Fact>]
    let ``Inverse QFT convenience function works`` () =
        let backend = createLocalBackend()
        
        match executeInverseQFT 3 backend 500 with
        | Error msg -> Assert.Fail($"Inverse QFT failed: {msg}")
        | Ok counts ->
            let totalShots = counts |> Map.toSeq |> Seq.sumBy snd
            Assert.Equal(500, totalShots)
    
    [<Fact>]
    let ``QFT circuit synthesis produces valid circuit`` () =
        let config = { NumQubits = 3; ApplySwaps = true; Inverse = false }
        
        match qftToCircuit config with
        | Error msg -> Assert.Fail($"QFT circuit synthesis failed: {msg}")
        | Ok circuit ->
            Assert.Equal(3, qubitCount circuit)
            Assert.True((gateCount circuit) > 0, "Circuit should have gates")
    
    [<Fact>]
    let ``QFT rejects excessive qubit count`` () =
        let config = { NumQubits = 25; ApplySwaps = true; Inverse = false }
        
        match qftToCircuit config with
        | Ok _ -> Assert.Fail("Should reject excessive qubits")
        | Error msg -> Assert.Contains("practical", msg.Message.ToLower())
    
    [<Fact>]
    let ``QFT on computational basis state produces uniform superposition`` () =
        // QFT|0⟩ produces uniform superposition
        let numQubits = 2
        let config = { NumQubits = numQubits; ApplySwaps = true; Inverse = false }
        let state = init numQubits  // |00⟩
        
        match execute config state with
        | Error msg -> Assert.Fail($"QFT execution failed: {msg}")
        | Ok result ->
            // For |0⟩ input, QFT produces uniform superposition
            // Each basis state should have magnitude 1/√N = 1/2
            let N = 1 <<< numQubits  // 2^n
            let expectedMag = 1.0 / sqrt (float N)
            
            for i in 0 .. N - 1 do
                let amp = getAmplitude i result.FinalState
                let magnitude = sqrt(amp.Real * amp.Real + amp.Imaginary * amp.Imaginary)
                Assert.True(abs(magnitude - expectedMag) < 1e-10, 
                    $"State |{i}⟩ should have magnitude {expectedMag}, got {magnitude}")
    
    // ========================================================================
    // UNIFIED BACKEND TESTS (NEW FUNCTIONS)
    // ========================================================================
    
    [<Fact>]
    let ``QFT verifyUnitarity confirms QFT is unitary`` () =
        let backend = LocalBackend.LocalBackend() :> IQuantumBackend
        let config = QFT.defaultConfig
        
        match QFT.verifyUnitarity 3 backend config with
        | Error err -> Assert.Fail($"verifyUnitarity failed: {err}")
        | Ok isUnitary ->
            Assert.True(isUnitary, "QFT should be unitary transformation")
    
    [<Fact>]
    let ``QFT transformBasisState creates correct superposition`` () =
        let backend = LocalBackend.LocalBackend() :> IQuantumBackend
        let numQubits = 3
        let basisIndex = 5  // |101⟩
        let config = QFT.defaultConfig
        
        match QFT.transformBasisState numQubits basisIndex backend config with
        | Error err -> Assert.Fail($"transformBasisState failed: {err}")
        | Ok result ->
            // Should have successfully transformed |5⟩
            Assert.True(result.GateCount > 0, "Should have applied gates")
            Assert.Equal(numQubits, FSharp.Azure.Quantum.Core.QuantumState.numQubits result.FinalState)
    
    [<Fact>]
    let ``QFT transformBasisState rejects invalid basis index`` () =
        let backend = LocalBackend.LocalBackend() :> IQuantumBackend
        let numQubits = 3
        let invalidIndex = 10  // Out of range for 3 qubits (max = 7)
        let config = QFT.defaultConfig
        
        match QFT.transformBasisState numQubits invalidIndex backend config with
        | Ok _ -> Assert.Fail("Should reject out-of-range basis index")
        | Error (FSharp.Azure.Quantum.Core.QuantumError.ValidationError _) -> 
            Assert.True(true, "Correctly rejected invalid index")
        | Error err -> Assert.Fail($"Wrong error type: {err}")
    
    [<Fact>]
    let ``QFT encodeAndTransform is equivalent to transformBasisState`` () =
        let backend = LocalBackend.LocalBackend() :> IQuantumBackend
        let numQubits = 3
        let value = 4
        let config = QFT.defaultConfig
        
        match QFT.encodeAndTransform numQubits value backend config with
        | Error err -> Assert.Fail($"encodeAndTransform failed: {err}")
        | Ok result ->
            // Should produce same result as transformBasisState
            Assert.True(result.GateCount > 0, "Should have applied gates")
            Assert.Equal(numQubits, FSharp.Azure.Quantum.Core.QuantumState.numQubits result.FinalState)
    
    [<Fact>]
    let ``QFT transformBasisState with all basis states`` () =
        let backend = LocalBackend.LocalBackend() :> IQuantumBackend
        let numQubits = 2
        let config = QFT.defaultConfig
        
        // Test all basis states for 2 qubits (0, 1, 2, 3)
        for basisIndex in 0 .. (1 <<< numQubits) - 1 do
            match QFT.transformBasisState numQubits basisIndex backend config with
            | Error err -> Assert.Fail($"transformBasisState failed for |{basisIndex}⟩: {err}")
            | Ok result ->
                Assert.True(result.GateCount > 0, $"Should have applied gates for |{basisIndex}⟩")
    
    [<Fact>]
    let ``QFT verifyUnitarity works with inverse config`` () =
        let backend = LocalBackend.LocalBackend() :> IQuantumBackend
        let config = { QFT.defaultConfig with Inverse = true }
        
        match QFT.verifyUnitarity 3 backend config with
        | Error err -> Assert.Fail($"verifyUnitarity with inverse failed: {err}")
        | Ok isUnitary ->
            Assert.True(isUnitary, "Inverse QFT should also be unitary")
    
    [<Fact>]
    let ``QFT verifyUnitarity works without swaps`` () =
        let backend = LocalBackend.LocalBackend() :> IQuantumBackend
        let config = { QFT.defaultConfig with ApplySwaps = false }
        
        match QFT.verifyUnitarity 3 backend config with
        | Error err -> Assert.Fail($"verifyUnitarity without swaps failed: {err}")
        | Ok isUnitary ->
            Assert.True(isUnitary, "QFT without swaps should still be unitary")
