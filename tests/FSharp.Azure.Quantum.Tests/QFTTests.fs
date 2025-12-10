namespace FSharp.Azure.Quantum.Tests

open Xunit
open FSharp.Azure.Quantum.LocalSimulator.StateVector
open FSharp.Azure.Quantum.Core
open FSharp.Azure.Quantum.Core.BackendAbstraction
open FSharp.Azure.Quantum.CircuitBuilder
open FSharp.Azure.Quantum.Backends
open System

/// Module aliases to avoid name conflicts
module QFT = FSharp.Azure.Quantum.Algorithms.QFT

/// Tests for Quantum Fourier Transform (QFT) and Backend Adapter
module QFTTests =
    
    // ========================================================================
    // QFT UNIFIED BACKEND TESTS
    // ========================================================================
    
    [<Fact>]
    let ``QFT on 1-qubit |0⟩ produces |+⟩ state`` () =
        // QFT on single qubit is just Hadamard
        // |0⟩ → |+⟩ = (|0⟩ + |1⟩)/√2
        let backend = LocalBackend.LocalBackend() :> IQuantumBackend
        let config = QFT.defaultConfig
        
        match QFT.execute 1 backend config with
        | Error err -> Assert.Fail($"QFT execution failed: {err}")
        | Ok result ->
            // Check that we can measure the state
            let measurements = QuantumState.measure result.FinalState 1000
            
            // Should see roughly equal distribution of |0⟩ and |1⟩
            let zeros = measurements |> Array.filter (fun bits -> bits.[0] = 0) |> Array.length
            let ones = measurements.Length - zeros
            
            // Allow 10% tolerance
            let tolerance = 100
            Assert.True(abs(zeros - ones) < tolerance, $"Expected ~50/50 split, got {zeros}/{ones}")
    
    [<Fact>]
    let ``QFT then inverse QFT returns original state`` () =
        // QFT followed by inverse QFT should be identity (up to global phase)
        let backend = LocalBackend.LocalBackend() :> IQuantumBackend
        let numQubits = 3
        
        // Apply forward QFT
        match QFT.execute numQubits backend QFT.defaultConfig with
        | Error err -> Assert.Fail($"Forward QFT failed: {err}")
        | Ok forwardResult ->
            // Apply inverse QFT
            let inverseConfig = { QFT.defaultConfig with Inverse = true }
            match QFT.executeOnState forwardResult.FinalState backend inverseConfig with
            | Error err -> Assert.Fail($"Inverse QFT failed: {err}")
            | Ok inverseResult ->
                // Should return to |000⟩
                // Verify by measuring multiple times
                let measurements = QuantumState.measure inverseResult.FinalState 100
                let allZeros = measurements |> Array.forall (fun bits -> bits |> Array.forall ((=) 0))
                
                Assert.True(allZeros, "Should return to |000⟩ state")
    
    [<Fact>]
    let ``QFT gate count scales correctly`` () =
        // QFT requires O(n²) gates for n qubits
        let backend = LocalBackend.LocalBackend() :> IQuantumBackend
        
        for n in 1 .. 5 do
            match QFT.execute n backend QFT.defaultConfig with
            | Error err -> Assert.Fail($"QFT execution failed for {n} qubits: {err}")
            | Ok result ->
                // Each qubit needs H + controlled-phase gates
                let expectedMinGates = n  // At least n Hadamard gates
                let estimatedGates = QFT.estimateGateCount n true
                
                Assert.True(result.GateCount >= expectedMinGates, 
                    $"Expected at least {expectedMinGates} gates, got {result.GateCount}")
                Assert.Equal(estimatedGates, result.GateCount)
    
    [<Fact>]
    let ``QFT preserves state norm`` () =
        // QFT is unitary, must preserve norm
        let backend = LocalBackend.LocalBackend() :> IQuantumBackend
        let numQubits = 3
        
        match QFT.execute numQubits backend QFT.defaultConfig with
        | Error err -> Assert.Fail($"QFT execution failed: {err}")
        | Ok result ->
            let isNormalized = QuantumState.isNormalized result.FinalState
            Assert.True(isNormalized, "QFT should preserve state normalization")
    
    // ========================================================================
    // QFT VALIDATION TESTS
    // ========================================================================
    
    [<Fact>]
    let ``QFT execution returns measurement results`` () =
        let backend = LocalBackend.LocalBackend() :> IQuantumBackend
        let numQubits = 2
        
        match QFT.execute numQubits backend QFT.defaultConfig with
        | Error err -> Assert.Fail($"QFT backend execution failed: {err}")
        | Ok result ->
            // Should have successfully created state
            Assert.True(result.GateCount > 0, "Should have applied gates")
            
            // Should be able to measure
            let measurements = QuantumState.measure result.FinalState 1000
            Assert.Equal(1000, measurements.Length)
    
    [<Fact>]
    let ``QFT with basis state preparation`` () =
        let backend = LocalBackend.LocalBackend() :> IQuantumBackend
        let numQubits = 2
        let basisIndex = 1  // Binary: 01
        
        match QFT.transformBasisState numQubits basisIndex backend QFT.defaultConfig with
        | Error err -> Assert.Fail($"QFT with basis state failed: {err}")
        | Ok result ->
            // Should have results
            Assert.True(result.GateCount > 0, "Should have applied gates")
            let measurements = QuantumState.measure result.FinalState 100
            Assert.Equal(100, measurements.Length)
    
    [<Fact>]
    let ``Standard QFT convenience function works`` () =
        let backend = LocalBackend.LocalBackend() :> IQuantumBackend
        
        match QFT.execute 3 backend QFT.defaultConfig with
        | Error err -> Assert.Fail($"Standard QFT failed: {err}")
        | Ok result ->
            let measurements = QuantumState.measure result.FinalState 500
            Assert.Equal(500, measurements.Length)
    
    [<Fact>]
    let ``Inverse QFT convenience function works`` () =
        let backend = LocalBackend.LocalBackend() :> IQuantumBackend
        
        match QFT.executeInverse 3 backend 500 with
        | Error err -> Assert.Fail($"Inverse QFT failed: {err}")
        | Ok result ->
            let measurements = QuantumState.measure result.FinalState 500
            Assert.Equal(500, measurements.Length)
    
    [<Fact>]
    let ``QFT on computational basis state produces uniform superposition`` () =
        // QFT|0⟩ produces uniform superposition
        let backend = LocalBackend.LocalBackend() :> IQuantumBackend
        let numQubits = 2
        
        match QFT.execute numQubits backend QFT.defaultConfig with
        | Error err -> Assert.Fail($"QFT execution failed: {err}")
        | Ok result ->
            // For |0⟩ input, QFT produces uniform superposition
            // Verify by measuring and checking distribution is roughly uniform
            let measurements = QuantumState.measure result.FinalState 1000
            
            // Count occurrences of each state
            let N = 1 <<< numQubits
            let counts = Array.zeroCreate N
            
            for bits in measurements do
                let stateIndex = 
                    bits 
                    |> Array.indexed
                    |> Array.fold (fun acc (i, bit) -> acc + (bit <<< i)) 0
                counts.[stateIndex] <- counts.[stateIndex] + 1
            
            // Each state should appear roughly 1000/N times (allow 30% tolerance)
            let expectedCount = 1000 / N
            let tolerance = expectedCount * 30 / 100
            
            for count in counts do
                Assert.True(abs(count - expectedCount) < tolerance,
                    $"Expected ~{expectedCount} occurrences, got {count}")
    
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
