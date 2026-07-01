namespace FSharp.Azure.Quantum.Tests

open Xunit
open FSharp.Azure.Quantum

/// Tests for OpenQASM 3.0 export (the format AWS Braket gate devices consume).
module OpenQasm3Tests =

    [<Fact>]
    let ``exportV3 emits valid OpenQASM 3.0 for a Bell circuit`` () =
        let bell =
            CircuitBuilder.empty 2
            |> CircuitBuilder.addGate (CircuitBuilder.H 0)
            |> CircuitBuilder.addGate (CircuitBuilder.CNOT (0, 1))
            |> CircuitBuilder.addGate (CircuitBuilder.Measure 0)
            |> CircuitBuilder.addGate (CircuitBuilder.Measure 1)
        let qasm = OpenQasm.exportV3 bell
        Assert.Contains("OPENQASM 3.0;", qasm)
        Assert.Contains("include \"stdgates.inc\";", qasm)   // 3.0 std library (not qelib1.inc)
        Assert.Contains("qubit[2] q;", qasm)                 // 3.0 register syntax (not qreg)
        Assert.Contains("bit[2] c;", qasm)
        Assert.Contains("h q[0];", qasm)
        Assert.Contains("cx q[0],q[1];", qasm)               // CNOT
        Assert.Contains("c[0] = measure q[0];", qasm)        // 3.0 measurement syntax

    [<Fact>]
    let ``exportV3 maps rotations and CZ`` () =
        let circuit =
            CircuitBuilder.empty 2
            |> CircuitBuilder.addGate (CircuitBuilder.RZ (0, 1.5))
            |> CircuitBuilder.addGate (CircuitBuilder.CZ (0, 1))
        let qasm = OpenQasm.exportV3 circuit
        Assert.Contains("rz(", qasm)
        Assert.Contains("cz q[0],q[1];", qasm)
