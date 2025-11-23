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

    // TDD Cycle #2: Circuit construction API tests

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
