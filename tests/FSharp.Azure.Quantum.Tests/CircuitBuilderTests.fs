namespace FSharp.Azure.Quantum.Tests

open Xunit
open FSharp.Azure.Quantum

module CircuitBuilderTests =

    [<Fact>]
    let ``Gate X should have correct qubit`` () =
        let gate = CircuitBuilder.Gate.X 0
        match gate with
        | CircuitBuilder.Gate.X qubit -> Assert.Equal(0, qubit)
        | _ -> Assert.True(false, "Expected X gate")

    [<Fact>]
    let ``Gate Y should have correct qubit`` () =
        let gate = CircuitBuilder.Gate.Y 1
        match gate with
        | CircuitBuilder.Gate.Y qubit -> Assert.Equal(1, qubit)
        | _ -> Assert.True(false, "Expected Y gate")

    [<Fact>]
    let ``Gate Z should have correct qubit`` () =
        let gate = CircuitBuilder.Gate.Z 2
        match gate with
        | CircuitBuilder.Gate.Z qubit -> Assert.Equal(2, qubit)
        | _ -> Assert.True(false, "Expected Z gate")

    [<Fact>]
    let ``Gate H should have correct qubit`` () =
        let gate = CircuitBuilder.Gate.H 3
        match gate with
        | CircuitBuilder.Gate.H qubit -> Assert.Equal(3, qubit)
        | _ -> Assert.True(false, "Expected H gate")

    [<Fact>]
    let ``Gate CNOT should have correct control and target qubits`` () =
        let gate = CircuitBuilder.Gate.CNOT (0, 1)
        match gate with
        | CircuitBuilder.Gate.CNOT (control, target) -> 
            Assert.Equal(0, control)
            Assert.Equal(1, target)
        | _ -> Assert.True(false, "Expected CNOT gate")

    [<Fact>]
    let ``Gate RX should have correct qubit and angle`` () =
        let gate = CircuitBuilder.Gate.RX (0, 1.5)
        match gate with
        | CircuitBuilder.Gate.RX (qubit, angle) -> 
            Assert.Equal(0, qubit)
            Assert.Equal(1.5, angle)
        | _ -> Assert.True(false, "Expected RX gate")

    [<Fact>]
    let ``Gate RY should have correct qubit and angle`` () =
        let gate = CircuitBuilder.Gate.RY (1, 2.5)
        match gate with
        | CircuitBuilder.Gate.RY (qubit, angle) -> 
            Assert.Equal(1, qubit)
            Assert.Equal(2.5, angle)
        | _ -> Assert.True(false, "Expected RY gate")

    [<Fact>]
    let ``Gate RZ should have correct qubit and angle`` () =
        let gate = CircuitBuilder.Gate.RZ (2, 3.5)
        match gate with
        | CircuitBuilder.Gate.RZ (qubit, angle) -> 
            Assert.Equal(2, qubit)
            Assert.Equal(3.5, angle)
        | _ -> Assert.True(false, "Expected RZ gate")

    [<Fact>]
    let ``Empty circuit should have zero gates`` () =
        let circuit = CircuitBuilder.empty 5
        let gateCount = CircuitBuilder.gateCount circuit
        Assert.Equal(0, gateCount)

    [<Fact>]
    let ``Empty circuit should have correct qubit count`` () =
        let circuit = CircuitBuilder.empty 5
        let qubits = CircuitBuilder.qubitCount circuit
        Assert.Equal(5, qubits)

    // Circuit construction API tests

    [<Fact>]
    let ``addGate should add a single gate to circuit`` () =
        let circuit = CircuitBuilder.empty 3
        let newCircuit = CircuitBuilder.addGate (CircuitBuilder.Gate.H 0) circuit
        Assert.Equal(1, CircuitBuilder.gateCount newCircuit)
        Assert.Equal(3, CircuitBuilder.qubitCount newCircuit)

    [<Fact>]
    let ``addGate should preserve existing gates`` () =
        let circuit = 
            CircuitBuilder.empty 3
            |> CircuitBuilder.addGate (CircuitBuilder.Gate.H 0)
            |> CircuitBuilder.addGate (CircuitBuilder.Gate.X 1)
            |> CircuitBuilder.addGate (CircuitBuilder.Gate.CNOT (0, 1))
        Assert.Equal(3, CircuitBuilder.gateCount circuit)

    [<Fact>]
    let ``addGates should add multiple gates at once`` () =
        let gates = [
            CircuitBuilder.Gate.H 0
            CircuitBuilder.Gate.H 1
            CircuitBuilder.Gate.CNOT (0, 1)
        ]
        let circuit = CircuitBuilder.empty 2 |> CircuitBuilder.addGates gates
        Assert.Equal(3, CircuitBuilder.gateCount circuit)

    [<Fact>]
    let ``compose should combine two circuits`` () =
        let circuit1 = 
            CircuitBuilder.empty 3
            |> CircuitBuilder.addGate (CircuitBuilder.Gate.H 0)
            |> CircuitBuilder.addGate (CircuitBuilder.Gate.H 1)
        
        let circuit2 = 
            CircuitBuilder.empty 3
            |> CircuitBuilder.addGate (CircuitBuilder.Gate.CNOT (0, 1))
            |> CircuitBuilder.addGate (CircuitBuilder.Gate.X 2)
        
        let combined = CircuitBuilder.compose circuit1 circuit2
        Assert.Equal(4, CircuitBuilder.gateCount combined)
        Assert.Equal(3, CircuitBuilder.qubitCount combined)

    [<Fact>]
    let ``getGates should return gates in order`` () =
        let circuit = 
            CircuitBuilder.empty 2
            |> CircuitBuilder.addGate (CircuitBuilder.Gate.H 0)
            |> CircuitBuilder.addGate (CircuitBuilder.Gate.X 1)
            |> CircuitBuilder.addGate (CircuitBuilder.Gate.CNOT (0, 1))
        
        let gates = CircuitBuilder.getGates circuit
        Assert.Equal(3, gates.Length)
        
        match gates.[0] with
        | CircuitBuilder.Gate.H 0 -> ()
        | _ -> Assert.True(false, "Expected H 0 as first gate")
        
        match gates.[1] with
        | CircuitBuilder.Gate.X 1 -> ()
        | _ -> Assert.True(false, "Expected X 1 as second gate")
        
        match gates.[2] with
        | CircuitBuilder.Gate.CNOT (0, 1) -> ()
        | _ -> Assert.True(false, "Expected CNOT (0,1) as third gate")

    // Basic optimization tests

    [<Fact>]
    let ``optimize should remove consecutive H gates on same qubit`` () =
        let circuit = 
            CircuitBuilder.empty 2
            |> CircuitBuilder.addGate (CircuitBuilder.Gate.H 0)
            |> CircuitBuilder.addGate (CircuitBuilder.Gate.H 0)  // Should be removed
            |> CircuitBuilder.addGate (CircuitBuilder.Gate.X 1)
        
        let optimized = CircuitBuilder.optimize circuit
        Assert.Equal(1, CircuitBuilder.gateCount optimized)
        
        let gates = CircuitBuilder.getGates optimized
        match gates.[0] with
        | CircuitBuilder.Gate.X 1 -> ()
        | _ -> Assert.True(false, "Expected X 1 after H-H removal")

    [<Fact>]
    let ``optimize should remove consecutive X gates on same qubit`` () =
        let circuit = 
            CircuitBuilder.empty 2
            |> CircuitBuilder.addGate (CircuitBuilder.Gate.X 0)
            |> CircuitBuilder.addGate (CircuitBuilder.Gate.X 0)  // Should be removed
            |> CircuitBuilder.addGate (CircuitBuilder.Gate.H 1)
        
        let optimized = CircuitBuilder.optimize circuit
        Assert.Equal(1, CircuitBuilder.gateCount optimized)

    [<Fact>]
    let ``optimize should fuse consecutive RX gates on same qubit`` () =
        let circuit = 
            CircuitBuilder.empty 1
            |> CircuitBuilder.addGate (CircuitBuilder.Gate.RX (0, 1.5))
            |> CircuitBuilder.addGate (CircuitBuilder.Gate.RX (0, 2.0))  // Should fuse to RX(0, 3.5)
        
        let optimized = CircuitBuilder.optimize circuit
        Assert.Equal(1, CircuitBuilder.gateCount optimized)
        
        let gates = CircuitBuilder.getGates optimized
        match gates.[0] with
        | CircuitBuilder.Gate.RX (0, angle) -> Assert.Equal(3.5, angle)
        | _ -> Assert.True(false, "Expected fused RX gate")

    [<Fact>]
    let ``optimize should preserve gates on different qubits`` () =
        let circuit = 
            CircuitBuilder.empty 3
            |> CircuitBuilder.addGate (CircuitBuilder.Gate.H 0)
            |> CircuitBuilder.addGate (CircuitBuilder.Gate.H 1)  // Different qubit, keep both
            |> CircuitBuilder.addGate (CircuitBuilder.Gate.X 0)
            |> CircuitBuilder.addGate (CircuitBuilder.Gate.X 2)  // Different qubit, keep both
        
        let optimized = CircuitBuilder.optimize circuit
        Assert.Equal(4, CircuitBuilder.gateCount optimized)

    [<Fact>]
    let ``optimize should handle complex optimization scenarios`` () =
        let circuit = 
            CircuitBuilder.empty 2
            |> CircuitBuilder.addGate (CircuitBuilder.Gate.H 0)
            |> CircuitBuilder.addGate (CircuitBuilder.Gate.H 0)      // Remove H-H
            |> CircuitBuilder.addGate (CircuitBuilder.Gate.X 0)
            |> CircuitBuilder.addGate (CircuitBuilder.Gate.X 0)      // Remove X-X
            |> CircuitBuilder.addGate (CircuitBuilder.Gate.RX (1, 1.0))
            |> CircuitBuilder.addGate (CircuitBuilder.Gate.RX (1, 2.0))  // Fuse to RX(1, 3.0)
            |> CircuitBuilder.addGate (CircuitBuilder.Gate.CNOT (0, 1))
        
        let optimized = CircuitBuilder.optimize circuit
        Assert.Equal(2, CircuitBuilder.gateCount optimized)  // RX(1, 3.0) and CNOT

    // OpenQASM output generation tests

    [<Fact>]
    let ``toOpenQASM should include header and qubit declaration`` () =
        let circuit = CircuitBuilder.empty 3
        let qasm = CircuitBuilder.toOpenQASM circuit
        
        Assert.Contains("OPENQASM 2.0;", qasm)
        Assert.Contains("include \"qelib1.inc\";", qasm)
        Assert.Contains("qreg q[3];", qasm)

    [<Fact>]
    let ``toOpenQASM should output X gate correctly`` () =
        let circuit = 
            CircuitBuilder.empty 2
            |> CircuitBuilder.addGate (CircuitBuilder.Gate.X 0)
        
        let qasm = CircuitBuilder.toOpenQASM circuit
        Assert.Contains("x q[0];", qasm)

    [<Fact>]
    let ``toOpenQASM should output Y gate correctly`` () =
        let circuit = 
            CircuitBuilder.empty 2
            |> CircuitBuilder.addGate (CircuitBuilder.Gate.Y 1)
        
        let qasm = CircuitBuilder.toOpenQASM circuit
        Assert.Contains("y q[1];", qasm)

    [<Fact>]
    let ``toOpenQASM should output Z gate correctly`` () =
        let circuit = 
            CircuitBuilder.empty 2
            |> CircuitBuilder.addGate (CircuitBuilder.Gate.Z 0)
        
        let qasm = CircuitBuilder.toOpenQASM circuit
        Assert.Contains("z q[0];", qasm)

    [<Fact>]
    let ``toOpenQASM should output H gate correctly`` () =
        let circuit = 
            CircuitBuilder.empty 2
            |> CircuitBuilder.addGate (CircuitBuilder.Gate.H 1)
        
        let qasm = CircuitBuilder.toOpenQASM circuit
        Assert.Contains("h q[1];", qasm)

    [<Fact>]
    let ``toOpenQASM should output CNOT gate correctly`` () =
        let circuit = 
            CircuitBuilder.empty 2
            |> CircuitBuilder.addGate (CircuitBuilder.Gate.CNOT (0, 1))
        
        let qasm = CircuitBuilder.toOpenQASM circuit
        Assert.Contains("cx q[0],q[1];", qasm)

    [<Fact>]
    let ``toOpenQASM should output RX gate correctly`` () =
        let circuit = 
            CircuitBuilder.empty 1
            |> CircuitBuilder.addGate (CircuitBuilder.Gate.RX (0, 1.5707963))
        
        let qasm = CircuitBuilder.toOpenQASM circuit
        Assert.Contains("rx(1.5707963) q[0];", qasm)

    [<Fact>]
    let ``toOpenQASM should output RY gate correctly`` () =
        let circuit = 
            CircuitBuilder.empty 1
            |> CircuitBuilder.addGate (CircuitBuilder.Gate.RY (0, 3.1415926))
        
        let qasm = CircuitBuilder.toOpenQASM circuit
        Assert.Contains("ry(3.1415926) q[0];", qasm)

    [<Fact>]
    let ``toOpenQASM should output RZ gate correctly`` () =
        let circuit = 
            CircuitBuilder.empty 1
            |> CircuitBuilder.addGate (CircuitBuilder.Gate.RZ (0, 0.7853982))
        
        let qasm = CircuitBuilder.toOpenQASM circuit
        Assert.Contains("rz(0.7853982) q[0];", qasm)

    [<Fact>]
    let ``toOpenQASM should output complete circuit correctly`` () =
        let circuit = 
            CircuitBuilder.empty 3
            |> CircuitBuilder.addGate (CircuitBuilder.Gate.H 0)
            |> CircuitBuilder.addGate (CircuitBuilder.Gate.H 1)
            |> CircuitBuilder.addGate (CircuitBuilder.Gate.CNOT (0, 1))
            |> CircuitBuilder.addGate (CircuitBuilder.Gate.RX (2, 1.5))
            |> CircuitBuilder.addGate (CircuitBuilder.Gate.X 2)
        
        let qasm = CircuitBuilder.toOpenQASM circuit
        
        // Verify structure
        Assert.Contains("OPENQASM 2.0;", qasm)
        Assert.Contains("qreg q[3];", qasm)
        
        // Verify gates in order
        Assert.Contains("h q[0];", qasm)
        Assert.Contains("h q[1];", qasm)
        Assert.Contains("cx q[0],q[1];", qasm)
        Assert.Contains("rx(1.5) q[2];", qasm)
        Assert.Contains("x q[2];", qasm)

    // Circuit validation tests

    [<Fact>]
    let ``validate should pass for valid circuit`` () =
        let circuit = 
            CircuitBuilder.empty 3
            |> CircuitBuilder.addGate (CircuitBuilder.Gate.H 0)
            |> CircuitBuilder.addGate (CircuitBuilder.Gate.CNOT (0, 1))
            |> CircuitBuilder.addGate (CircuitBuilder.Gate.RX (2, 1.5))
        
        let result = CircuitBuilder.validate circuit
        Assert.True(result.IsValid, "Valid circuit should pass validation")
        Assert.Empty(result.Messages)

    [<Fact>]
    let ``validate should fail for qubit index out of bounds`` () =
        let circuit = 
            CircuitBuilder.empty 2
            |> CircuitBuilder.addGate (CircuitBuilder.Gate.H 0)
            |> CircuitBuilder.addGate (CircuitBuilder.Gate.X 2)  // Out of bounds!
        
        let result = CircuitBuilder.validate circuit
        Assert.False(result.IsValid)
        Assert.Contains("Qubit index 2 out of bounds", result.Messages.[0])

    [<Fact>]
    let ``validate should fail for CNOT with out of bounds control`` () =
        let circuit = 
            CircuitBuilder.empty 2
            |> CircuitBuilder.addGate (CircuitBuilder.Gate.CNOT (3, 1))  // Control out of bounds
        
        let result = CircuitBuilder.validate circuit
        Assert.False(result.IsValid)
        Assert.Contains("Qubit index 3 out of bounds", result.Messages.[0])

    [<Fact>]
    let ``validate should fail for CNOT with out of bounds target`` () =
        let circuit = 
            CircuitBuilder.empty 2
            |> CircuitBuilder.addGate (CircuitBuilder.Gate.CNOT (0, 5))  // Target out of bounds
        
        let result = CircuitBuilder.validate circuit
        Assert.False(result.IsValid)
        Assert.Contains("Qubit index 5 out of bounds", result.Messages.[0])

    [<Fact>]
    let ``validate should fail for CNOT with same control and target`` () =
        let circuit = 
            CircuitBuilder.empty 3
            |> CircuitBuilder.addGate (CircuitBuilder.Gate.CNOT (1, 1))  // Same qubit
        
        let result = CircuitBuilder.validate circuit
        Assert.False(result.IsValid)
        Assert.Contains("CNOT control and target cannot be the same qubit", result.Messages.[0])

    [<Fact>]
    let ``validate should collect multiple errors`` () =
        let circuit = 
            CircuitBuilder.empty 2
            |> CircuitBuilder.addGate (CircuitBuilder.Gate.H 5)        // Out of bounds
            |> CircuitBuilder.addGate (CircuitBuilder.Gate.X 10)       // Out of bounds
            |> CircuitBuilder.addGate (CircuitBuilder.Gate.CNOT (0, 0))  // Same qubit
        
        let result = CircuitBuilder.validate circuit
        Assert.False(result.IsValid)
        Assert.Equal(3, result.Messages.Length)

    [<Fact>]
    let ``validate should fail for negative qubit index`` () =
        let circuit = 
            CircuitBuilder.empty 3
            |> CircuitBuilder.addGate (CircuitBuilder.Gate.H -1)  // Negative index
        
        let result = CircuitBuilder.validate circuit
        Assert.False(result.IsValid)
        Assert.Contains("negative", result.Messages.[0].ToLower())
