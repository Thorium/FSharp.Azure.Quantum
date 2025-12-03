namespace FSharp.Azure.Quantum.Tests

open System
open Xunit
open FSharp.Azure.Quantum.MachineLearning
open FSharp.Azure.Quantum.MachineLearning.VariationalForms
open FSharp.Azure.Quantum.CircuitBuilder

/// Unit tests for Variational Form (Ansatz) implementations
///
/// Tests cover:
/// - Parameter count validation
/// - Circuit generation
/// - Gate structure verification
/// - Error handling
/// - Edge cases
module VariationalFormTests =
    
    // ========================================================================
    // HELPER FUNCTIONS
    // ========================================================================
    
    let countGateType (gateFilter: Gate -> bool) (circuit: Circuit) : int =
        getGates circuit |> List.filter gateFilter |> List.length
    
    let countRyGates = countGateType (function RY _ -> true | _ -> false)
    let countRzGates = countGateType (function RZ _ -> true | _ -> false)
    let countRxGates = countGateType (function RX _ -> true | _ -> false)
    let countCZGates = countGateType (function CZ _ -> true | _ -> false)
    let countCNOTGates = countGateType (function CNOT _ -> true | _ -> false)
    
    // ========================================================================
    // PARAMETER COUNT TESTS
    // ========================================================================
    
    [<Fact>]
    let ``AnsatzHelpers.parameterCount - RealAmplitudes calculates correctly`` () =
        // RealAmplitudes: 1 parameter per qubit per layer
        let ansatz = RealAmplitudes 2
        let numQubits = 3
        let count = AnsatzHelpers.parameterCount ansatz numQubits
        
        Assert.Equal(6, count)  // 3 qubits * 2 layers = 6
    
    [<Fact>]
    let ``AnsatzHelpers.parameterCount - TwoLocal calculates correctly`` () =
        // TwoLocal: 1 parameter per qubit per layer
        let ansatz = TwoLocal("Ry", "CZ", 3)
        let numQubits = 4
        let count = AnsatzHelpers.parameterCount ansatz numQubits
        
        Assert.Equal(12, count)  // 4 qubits * 3 layers = 12
    
    [<Fact>]
    let ``AnsatzHelpers.parameterCount - EfficientSU2 calculates correctly`` () =
        // EfficientSU2: 2 parameters per qubit per layer
        let ansatz = EfficientSU2 2
        let numQubits = 3
        let count = AnsatzHelpers.parameterCount ansatz numQubits
        
        Assert.Equal(12, count)  // 3 qubits * 2 layers * 2 parameters = 12
    
    // ========================================================================
    // REAL AMPLITUDES TESTS
    // ========================================================================
    
    [<Fact>]
    let ``RealAmplitudes - generates correct circuit structure for depth 1`` () =
        let numQubits = 3
        let depth = 1
        let parameters = Array.init (numQubits * depth) (fun i -> float i * 0.1)
        
        match buildVariationalForm (RealAmplitudes depth) parameters numQubits with
        | Ok circuit ->
            Assert.Equal(numQubits, circuit.QubitCount)
            
            // Should have: 3 Ry gates + 2 CZ gates
            let ryCount = countRyGates circuit
            let czCount = countCZGates circuit
            
            Assert.Equal(3, ryCount)
            Assert.Equal(2, czCount)  // n-1 CZ gates for n qubits
        
        | Error msg ->
            Assert.True(false, $"Should succeed but got error: {msg}")
    
    [<Fact>]
    let ``RealAmplitudes - generates correct circuit structure for depth 2`` () =
        let numQubits = 4
        let depth = 2
        let parameters = Array.init (numQubits * depth) (fun i -> float i * 0.1)
        
        match buildVariationalForm (RealAmplitudes depth) parameters numQubits with
        | Ok circuit ->
            // Should have: 8 Ry gates (4 qubits * 2 layers)
            let ryCount = countRyGates circuit
            Assert.Equal(8, ryCount)
            
            // Should have: 3 CZ gates (one per layer, n-1 per layer)
            let czCount = countCZGates circuit
            Assert.Equal(3, czCount)  // Only one entanglement layer for depth 2
        
        | Error msg ->
            Assert.True(false, $"Should succeed but got error: {msg}")
    
    [<Fact>]
    let ``RealAmplitudes - rejects incorrect parameter count`` () =
        let numQubits = 3
        let depth = 2
        let wrongParams = [| 1.0; 2.0 |]  // Should be 6 parameters
        
        match buildVariationalForm (RealAmplitudes depth) wrongParams numQubits with
        | Ok _ ->
            Assert.True(false, "Should fail with wrong parameter count")
        
        | Error msg ->
            Assert.Contains("requires 6 parameters", msg)
            Assert.Contains("got 2", msg)
    
    [<Fact>]
    let ``RealAmplitudes - handles single qubit`` () =
        let numQubits = 1
        let depth = 2
        let parameters = [| 0.5; 1.0 |]
        
        match buildVariationalForm (RealAmplitudes depth) parameters numQubits with
        | Ok circuit ->
            Assert.Equal(1, circuit.QubitCount)
            
            // Single qubit: 2 Ry gates, 0 CZ gates
            let ryCount = countRyGates circuit
            let czCount = countCZGates circuit
            
            Assert.Equal(2, ryCount)
            Assert.Equal(0, czCount)
        
        | Error msg ->
            Assert.True(false, $"Should succeed but got error: {msg}")
    
    // ========================================================================
    // TWO-LOCAL TESTS
    // ========================================================================
    
    [<Fact>]
    let ``TwoLocal - generates Ry + CZ circuit correctly`` () =
        let numQubits = 3
        let depth = 1
        let parameters = Array.init (numQubits * depth) (fun i -> float i * 0.1)
        
        match buildVariationalForm (TwoLocal("Ry", "CZ", depth)) parameters numQubits with
        | Ok circuit ->
            let ryCount = countRyGates circuit
            let czCount = countCZGates circuit
            
            Assert.Equal(3, ryCount)
            Assert.Equal(2, czCount)
        
        | Error msg ->
            Assert.True(false, $"Should succeed but got error: {msg}")
    
    [<Fact>]
    let ``TwoLocal - generates Rx + CNOT circuit correctly`` () =
        let numQubits = 4
        let depth = 1
        let parameters = Array.init (numQubits * depth) (fun i -> float i * 0.1)
        
        match buildVariationalForm (TwoLocal("Rx", "CNOT", depth)) parameters numQubits with
        | Ok circuit ->
            let rxCount = countRxGates circuit
            let cnotCount = countCNOTGates circuit
            
            Assert.Equal(4, rxCount)
            Assert.Equal(3, cnotCount)
        
        | Error msg ->
            Assert.True(false, $"Should succeed but got error: {msg}")
    
    [<Fact>]
    let ``TwoLocal - handles case-insensitive gate names`` () =
        let numQubits = 2
        let depth = 1
        let parameters = [| 0.1; 0.2 |]
        
        // Test lowercase
        match buildVariationalForm (TwoLocal("ry", "cz", depth)) parameters numQubits with
        | Ok circuit ->
            Assert.Equal(2, countRyGates circuit)
            Assert.Equal(1, countCZGates circuit)
        | Error msg ->
            Assert.True(false, $"Should handle lowercase: {msg}")
        
        // Test uppercase
        match buildVariationalForm (TwoLocal("RY", "CZ", depth)) parameters numQubits with
        | Ok circuit ->
            Assert.Equal(2, countRyGates circuit)
            Assert.Equal(1, countCZGates circuit)
        | Error msg ->
            Assert.True(false, $"Should handle uppercase: {msg}")
    
    [<Fact>]
    let ``TwoLocal - defaults to Ry and CZ for unknown gates`` () =
        let numQubits = 2
        let depth = 1
        let parameters = [| 0.1; 0.2 |]
        
        match buildVariationalForm (TwoLocal("UnknownRot", "UnknownEnt", depth)) parameters numQubits with
        | Ok circuit ->
            // Should default to Ry and CZ
            Assert.Equal(2, countRyGates circuit)
            Assert.Equal(1, countCZGates circuit)
        
        | Error msg ->
            Assert.True(false, $"Should use defaults: {msg}")
    
    // ========================================================================
    // EFFICIENT SU(2) TESTS
    // ========================================================================
    
    [<Fact>]
    let ``EfficientSU2 - generates Ry + Rz layers correctly`` () =
        let numQubits = 3
        let depth = 1
        let parameters = Array.init (2 * numQubits * depth) (fun i -> float i * 0.1)
        
        match buildVariationalForm (EfficientSU2 depth) parameters numQubits with
        | Ok circuit ->
            let ryCount = countRyGates circuit
            let rzCount = countRzGates circuit
            let cnotCount = countCNOTGates circuit
            
            // Each qubit gets Ry and Rz
            Assert.Equal(3, ryCount)
            Assert.Equal(3, rzCount)
            Assert.Equal(2, cnotCount)  // n-1 CNOT gates
        
        | Error msg ->
            Assert.True(false, $"Should succeed but got error: {msg}")
    
    [<Fact>]
    let ``EfficientSU2 - requires double parameters per qubit`` () =
        let numQubits = 2
        let depth = 2
        let correctParams = Array.init (2 * numQubits * depth) (fun i -> float i * 0.1)
        
        match buildVariationalForm (EfficientSU2 depth) correctParams numQubits with
        | Ok circuit ->
            // Should have 4 Ry and 4 Rz (2 per qubit per layer)
            Assert.Equal(4, countRyGates circuit)
            Assert.Equal(4, countRzGates circuit)
        
        | Error msg ->
            Assert.True(false, $"Should succeed but got error: {msg}")
    
    [<Fact>]
    let ``EfficientSU2 - rejects incorrect parameter count`` () =
        let numQubits = 3
        let depth = 1
        let wrongParams = [| 1.0; 2.0; 3.0 |]  // Should be 6 (2 per qubit)
        
        match buildVariationalForm (EfficientSU2 depth) wrongParams numQubits with
        | Ok _ ->
            Assert.True(false, "Should fail with wrong parameter count")
        
        | Error msg ->
            Assert.Contains("requires 6 parameters", msg)
            Assert.Contains("got 3", msg)
    
    // ========================================================================
    // VALIDATION TESTS
    // ========================================================================
    
    [<Fact>]
    let ``buildVariationalForm - rejects zero qubits`` () =
        let parameters = [| |]
        
        match buildVariationalForm (RealAmplitudes 1) parameters 0 with
        | Ok _ ->
            Assert.True(false, "Should reject zero qubits")
        
        | Error msg ->
            Assert.Contains("at least 1", msg)
    
    [<Fact>]
    let ``buildVariationalForm - rejects negative qubits`` () =
        let parameters = [| |]
        
        match buildVariationalForm (RealAmplitudes 1) parameters -1 with
        | Ok _ ->
            Assert.True(false, "Should reject negative qubits")
        
        | Error msg ->
            Assert.Contains("at least 1", msg)
    
    [<Fact>]
    let ``buildVariationalForm - rejects zero depth`` () =
        let parameters = [| |]
        
        match buildVariationalForm (RealAmplitudes 0) parameters 3 with
        | Ok _ ->
            Assert.True(false, "Should reject zero depth")
        
        | Error msg ->
            Assert.Contains("Depth must be at least 1", msg)
    
    [<Fact>]
    let ``buildVariationalForm - rejects negative depth`` () =
        let parameters = [| |]
        
        match buildVariationalForm (EfficientSU2 -1) parameters 3 with
        | Ok _ ->
            Assert.True(false, "Should reject negative depth")
        
        | Error msg ->
            Assert.Contains("Depth must be at least 1", msg)
    
    // ========================================================================
    // PARAMETER INITIALIZATION TESTS
    // ========================================================================
    
    [<Fact>]
    let ``randomParameters - generates correct count`` () =
        let ansatz = RealAmplitudes 2
        let numQubits = 4
        let parameters = randomParameters ansatz numQubits (Some 42)
        
        Assert.Equal(8, parameters.Length)  // 4 qubits * 2 depth
    
    [<Fact>]
    let ``randomParameters - generates values in range [0, 2π]`` () =
        let ansatz = RealAmplitudes 2
        let numQubits = 3
        let parameters = randomParameters ansatz numQubits (Some 42)
        
        // All values should be in [0, 2π]
        for p in parameters do
            Assert.True(p >= 0.0 && p <= 2.0 * Math.PI, $"Parameter {p} out of range [0, 2π]")
    
    [<Fact>]
    let ``randomParameters - is deterministic with seed`` () =
        let ansatz = RealAmplitudes 2
        let numQubits = 3
        
        let params1 = randomParameters ansatz numQubits (Some 42)
        let params2 = randomParameters ansatz numQubits (Some 42)
        
        Assert.Equal<float array>(params1, params2)
    
    [<Fact>]
    let ``randomParameters - differs without seed or different seeds`` () =
        let ansatz = RealAmplitudes 2
        let numQubits = 3
        
        let params1 = randomParameters ansatz numQubits (Some 42)
        let params2 = randomParameters ansatz numQubits (Some 99)
        
        Assert.NotEqual<float array>(params1, params2)
    
    [<Fact>]
    let ``zeroParameters - generates all zeros`` () =
        let ansatz = EfficientSU2 2
        let numQubits = 3
        let parameters = zeroParameters ansatz numQubits
        
        Assert.Equal(12, parameters.Length)  // 3 * 2 * 2
        Assert.All(parameters, fun p -> Assert.Equal(0.0, p))
    
    [<Fact>]
    let ``constantParameters - generates all same value`` () =
        let ansatz = TwoLocal("Ry", "CZ", 2)
        let numQubits = 4
        let value = 1.5
        let parameters = constantParameters ansatz numQubits value
        
        Assert.Equal(8, parameters.Length)
        Assert.All(parameters, fun p -> Assert.Equal(value, p))
    
    // ========================================================================
    // CIRCUIT COMPOSITION TESTS
    // ========================================================================
    
    [<Fact>]
    let ``composeWithFeatureMap - successfully composes matching circuits`` () =
        let numQubits = 3
        
        // Create feature map circuit (simple example)
        let featureMap = 
            let mutable c = empty numQubits
            for i in 0 .. numQubits - 1 do
                c <- addGate (H i) c
            c
        
        // Create variational circuit
        let parameters = [| 0.1; 0.2; 0.3 |]
        match buildVariationalForm (RealAmplitudes 1) parameters numQubits with
        | Ok varCircuit ->
            match composeWithFeatureMap featureMap varCircuit with
            | Ok composed ->
                Assert.Equal(numQubits, composed.QubitCount)
                
                // Should have gates from both circuits
                let totalGates = gateCount composed
                Assert.True(totalGates > gateCount featureMap)
                Assert.True(totalGates > gateCount varCircuit)
            
            | Error msg ->
                Assert.True(false, $"Composition should succeed: {msg}")
        
        | Error msg ->
            Assert.True(false, $"Circuit generation failed: {msg}")
    
    [<Fact>]
    let ``composeWithFeatureMap - rejects mismatched qubit counts`` () =
        let featureMap = empty 3
        let varCircuit = empty 4
        
        match composeWithFeatureMap featureMap varCircuit with
        | Ok _ ->
            Assert.True(false, "Should reject qubit count mismatch")
        
        | Error msg ->
            Assert.Contains("Qubit count mismatch", msg)
            Assert.Contains("3 qubits", msg)
            Assert.Contains("4 qubits", msg)
    
    // ========================================================================
    // INTEGRATION TESTS
    // ========================================================================
    
    [<Fact>]
    let ``End-to-end - RealAmplitudes depth 3 on 5 qubits`` () =
        let numQubits = 5
        let depth = 3
        let parameters = randomParameters (RealAmplitudes depth) numQubits (Some 42)
        
        match buildVariationalForm (RealAmplitudes depth) parameters numQubits with
        | Ok circuit ->
            Assert.Equal(numQubits, circuit.QubitCount)
            Assert.Equal(15, countRyGates circuit)  // 5 * 3
            Assert.Equal(8, countCZGates circuit)   // 4 * 2 (entanglement in first 2 layers)
        
        | Error msg ->
            Assert.True(false, $"Integration test failed: {msg}")
    
    [<Fact>]
    let ``End-to-end - EfficientSU2 with varied parameters`` () =
        let numQubits = 3
        let depth = 2
        let parameters = [| 0.1; 0.2; 0.3; 0.4; 0.5; 0.6; 0.7; 0.8; 0.9; 1.0; 1.1; 1.2 |]
        
        match buildVariationalForm (EfficientSU2 depth) parameters numQubits with
        | Ok circuit ->
            // Verify parameter usage: each distinct angle should appear once
            let gates = getGates circuit
            let angles = 
                gates 
                |> List.choose (function 
                    | RY(_, a) | RZ(_, a) -> Some a 
                    | _ -> None)
            
            Assert.Equal(12, angles.Length)  // 3 qubits * 2 layers * 2 rotations
            Assert.Equal(parameters.Length, angles.Length)
        
        | Error msg ->
            Assert.True(false, $"Integration test failed: {msg}")
