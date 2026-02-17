namespace FSharp.Azure.Quantum.Tests

open Xunit
open FSharp.Azure.Quantum
open FSharp.Azure.Quantum.CircuitBuilder
open FSharp.Azure.Quantum.OpenQasmVersion
open FSharp.Azure.Quantum.Core
open FSharp.Azure.Quantum.Core.BackendAbstraction
open FSharp.Azure.Quantum.Core.CircuitAbstraction
open FSharp.Azure.Quantum.Backends
open FSharp.Azure.Quantum.LocalSimulator

/// Comprehensive tests for Reset and Barrier gate support across all layers:
/// CircuitBuilder (DU, CE, helpers, validation, reverse, gateToQASM),
/// OpenQasmExport (versioned export, validation),
/// OpenQasmImport (parsing), and round-trip fidelity.
module ResetBarrierTests =

    // ── Gate DU construction ──────────────────────────────────────────

    [<Fact>]
    let ``Reset gate stores qubit index`` () =
        let gate = Reset 3
        match gate with
        | Reset q -> Assert.Equal(3, q)
        | _ -> Assert.Fail("Expected Reset gate")

    [<Fact>]
    let ``Barrier gate stores qubit list`` () =
        let gate = Barrier [0; 1; 2]
        match gate with
        | Barrier qs -> Assert.Equal<int list>([0; 1; 2], qs)
        | _ -> Assert.Fail("Expected Barrier gate")

    [<Fact>]
    let ``Barrier gate with single qubit`` () =
        let gate = Barrier [0]
        match gate with
        | Barrier qs -> Assert.Equal<int list>([0], qs)
        | _ -> Assert.Fail("Expected Barrier gate")

    // ── Helper functions ──────────────────────────────────────────────

    [<Fact>]
    let ``reset helper creates Reset gate`` () =
        let gate = reset 5
        Assert.Equal(Reset 5, gate)

    [<Fact>]
    let ``barrier helper creates Barrier gate`` () =
        let gate = barrier [1; 3]
        Assert.Equal(Barrier [1; 3], gate)

    // ── getGateName ───────────────────────────────────────────────────

    [<Fact>]
    let ``getGateName returns Reset for Reset gate`` () =
        Assert.Equal("Reset", getGateName (Reset 0))

    [<Fact>]
    let ``getGateName returns Barrier for Barrier gate`` () =
        Assert.Equal("Barrier", getGateName (Barrier [0; 1]))

    // ── gateToQASM (CircuitBuilder) ───────────────────────────────────

    [<Fact>]
    let ``gateToQASM produces reset instruction`` () =
        let c = circuit { qubits 2; Reset 0 }
        let qasm = OpenQasm.export c
        Assert.Contains("reset q[0];", qasm)

    [<Fact>]
    let ``gateToQASM produces barrier instruction with multiple qubits`` () =
        let c = circuit { qubits 3; Barrier [0; 1; 2] }
        let qasm = OpenQasm.export c
        Assert.Contains("barrier q[0],q[1],q[2];", qasm)

    [<Fact>]
    let ``gateToQASM produces barrier instruction with single qubit`` () =
        let c = circuit { qubits 2; Barrier [1] }
        let qasm = OpenQasm.export c
        Assert.Contains("barrier q[1];", qasm)

    // ── Circuit Builder CE ────────────────────────────────────────────

    [<Fact>]
    let ``CE Reset adds Reset gate to circuit`` () =
        let c = circuit { qubits 2; Reset 0 }
        Assert.Contains(Reset 0, c.Gates)

    [<Fact>]
    let ``CE Barrier adds Barrier gate to circuit`` () =
        let c = circuit { qubits 3; Barrier [0; 1; 2] }
        Assert.Contains(Barrier [0; 1; 2], c.Gates)

    [<Fact>]
    let ``CE Reset and Barrier in sequence`` () =
        let c = circuit {
            qubits 3
            H 0
            Barrier [0; 1; 2]
            Reset 1
            X 2
        }
        Assert.Equal(3, c.QubitCount)
        Assert.Equal<Gate list>([X 2; Reset 1; Barrier [0; 1; 2]; H 0], c.Gates)

    [<Fact>]
    let ``CE multiple Reset gates in sequence`` () =
        let c = circuit {
            qubits 3
            Reset 0
            Reset 1
            Reset 2
        }
        Assert.Equal<Gate list>([Reset 2; Reset 1; Reset 0], c.Gates)

    [<Fact>]
    let ``CE Barrier constructed directly`` () =
        let c = circuit {
            qubits 2
            Barrier [0; 1]
        }
        Assert.Contains(Barrier [0; 1], c.Gates)

    // ── Validation (CircuitBuilder.validate) ──────────────────────────

    [<Fact>]
    let ``validate accepts Reset with valid qubit`` () =
        let c = circuit { qubits 2; Reset 0 }
        let result = CircuitBuilder.validate c
        Assert.True(result.IsValid)

    [<Fact>]
    let ``validate rejects Reset with negative qubit`` () =
        let c = { QubitCount = 2; Gates = [Reset -1] }
        let result = CircuitBuilder.validate c
        Assert.False(result.IsValid)
        Assert.Contains("negative", result.Messages.[0])

    [<Fact>]
    let ``validate rejects Reset with out-of-bounds qubit`` () =
        let c = { QubitCount = 2; Gates = [Reset 5] }
        let result = CircuitBuilder.validate c
        Assert.False(result.IsValid)
        Assert.Contains("out of bounds", result.Messages.[0])

    [<Fact>]
    let ``validate accepts Barrier with valid qubits`` () =
        let c = circuit { qubits 3; Barrier [0; 1; 2] }
        let result = CircuitBuilder.validate c
        Assert.True(result.IsValid)

    [<Fact>]
    let ``validate rejects Barrier with empty qubit list`` () =
        let c = { QubitCount = 2; Gates = [Barrier []] }
        let result = CircuitBuilder.validate c
        Assert.False(result.IsValid)
        Assert.Contains("at least one qubit", result.Messages.[0])

    [<Fact>]
    let ``validate rejects Barrier with duplicate qubits`` () =
        let c = { QubitCount = 3; Gates = [Barrier [0; 1; 0]] }
        let result = CircuitBuilder.validate c
        Assert.False(result.IsValid)
        Assert.Contains("distinct", result.Messages.[0])

    [<Fact>]
    let ``validate rejects Barrier with out-of-bounds qubit`` () =
        let c = { QubitCount = 2; Gates = [Barrier [0; 5]] }
        let result = CircuitBuilder.validate c
        Assert.False(result.IsValid)

    // ── Validation (OpenQasmExport.validate) ──────────────────────────

    [<Fact>]
    let ``OpenQasm validate accepts Reset with valid qubit`` () =
        let c = circuit { qubits 2; Reset 1 }
        let result = OpenQasm.validate c
        Assert.True(Result.isOk result)

    [<Fact>]
    let ``OpenQasm validate rejects Reset with out-of-bounds qubit`` () =
        let c = { QubitCount = 2; Gates = [Reset 3] }
        let result = OpenQasm.validate c
        Assert.True(Result.isError result)

    [<Fact>]
    let ``OpenQasm validate accepts Barrier with valid qubits`` () =
        let c = circuit { qubits 4; Barrier [0; 2; 3] }
        let result = OpenQasm.validate c
        Assert.True(Result.isOk result)

    [<Fact>]
    let ``OpenQasm validate rejects Barrier with empty list`` () =
        let c = { QubitCount = 2; Gates = [Barrier []] }
        let result = OpenQasm.validate c
        match result with
        | Error msg -> Assert.Contains("at least one qubit", msg)
        | Ok () -> Assert.Fail("Expected validation error")

    [<Fact>]
    let ``OpenQasm validate rejects Barrier with duplicate qubits`` () =
        let c = { QubitCount = 3; Gates = [Barrier [1; 2; 1]] }
        let result = OpenQasm.validate c
        match result with
        | Error msg -> Assert.Contains("distinct", msg)
        | Ok () -> Assert.Fail("Expected validation error")

    [<Fact>]
    let ``OpenQasm validate rejects Barrier with out-of-bounds qubit`` () =
        let c = { QubitCount = 2; Gates = [Barrier [0; 10]] }
        let result = OpenQasm.validate c
        Assert.True(Result.isError result)

    // ── reverse / inverseGate ─────────────────────────────────────────

    [<Fact>]
    let ``reverse preserves Reset gate unchanged`` () =
        let c = circuit { qubits 2; H 0; Reset 0 }
        let reversed = CircuitBuilder.reverse c
        Assert.Contains(Reset 0, reversed.Gates)

    [<Fact>]
    let ``reverse preserves Barrier gate unchanged`` () =
        let c = circuit { qubits 3; H 0; Barrier [0; 1; 2]; X 1 }
        let reversed = CircuitBuilder.reverse c
        Assert.Contains(Barrier [0; 1; 2], reversed.Gates)

    [<Fact>]
    let ``reverse inverts surrounding gates but keeps Reset and Barrier`` () =
        let c = circuit {
            qubits 2
            S 0
            Reset 1
            Barrier [0; 1]
            T 0
        }
        let reversed = CircuitBuilder.reverse c
        // S -> SDG, T -> TDG, but Reset and Barrier stay as-is
        Assert.Contains(SDG 0, reversed.Gates)
        Assert.Contains(TDG 0, reversed.Gates)
        Assert.Contains(Reset 1, reversed.Gates)
        Assert.Contains(Barrier [0; 1], reversed.Gates)

    // ── OpenQASM Export — V2.0 (default) ──────────────────────────────

    [<Fact>]
    let ``export V2_0 Reset produces reset instruction`` () =
        let c = circuit { qubits 2; Reset 0 }
        let qasm = OpenQasm.export c
        Assert.Contains("OPENQASM 2.0;", qasm)
        Assert.Contains("reset q[0];", qasm)

    [<Fact>]
    let ``export V2_0 Barrier produces barrier instruction`` () =
        let c = circuit { qubits 3; Barrier [0; 1; 2] }
        let qasm = OpenQasm.export c
        Assert.Contains("OPENQASM 2.0;", qasm)
        Assert.Contains("barrier q[0],q[1],q[2];", qasm)

    [<Fact>]
    let ``export V2_0 Reset does not add creg`` () =
        let c = circuit { qubits 2; Reset 0 }
        let qasm = OpenQasm.export c
        // Reset is not a measurement — should not trigger creg declaration
        Assert.DoesNotContain("creg", qasm)

    [<Fact>]
    let ``export V2_0 mixed circuit with Reset Barrier and Measure`` () =
        let c = circuit {
            qubits 2
            H 0
            Barrier [0; 1]
            CNOT 0 1
            Reset 0
            Measure 1
        }
        let qasm = OpenQasm.export c
        Assert.Contains("h q[0];", qasm)
        Assert.Contains("barrier q[0],q[1];", qasm)
        Assert.Contains("cx q[0],q[1];", qasm)
        Assert.Contains("reset q[0];", qasm)
        Assert.Contains("measure q[1]", qasm)
        Assert.Contains("creg", qasm)

    // ── OpenQASM Export — V3.0 ────────────────────────────────────────

    [<Fact>]
    let ``exportV3 Reset produces reset instruction`` () =
        let c = circuit { qubits 2; Reset 0 }
        let qasm = OpenQasm.exportV3 c
        Assert.Contains("OPENQASM 3.0;", qasm)
        Assert.Contains("reset q[0];", qasm)

    [<Fact>]
    let ``exportV3 Barrier produces barrier instruction`` () =
        let c = circuit { qubits 3; Barrier [1; 2] }
        let qasm = OpenQasm.exportV3 c
        Assert.Contains("OPENQASM 3.0;", qasm)
        Assert.Contains("barrier q[1],q[2];", qasm)

    [<Fact>]
    let ``exportV3 Reset does not add bit declaration`` () =
        let c = circuit { qubits 2; Reset 0 }
        let qasm = OpenQasm.exportV3 c
        // No measurement, so no bit[] declaration (use \nbit to avoid matching "qubit[")
        Assert.DoesNotContain("\nbit[", qasm)

    [<Fact>]
    let ``exportV3 mixed circuit with Reset and Measure`` () =
        let c = circuit {
            qubits 2
            H 0
            Reset 1
            Measure 0
        }
        let qasm = OpenQasm.exportV3 c
        Assert.Contains("reset q[1];", qasm)
        Assert.Contains("bit[", qasm)  // Measure triggers bit[] declaration

    // ── OpenQASM Export — V1.0 ────────────────────────────────────────

    [<Fact>]
    let ``exportWithConfig V1_0 Reset produces reset instruction`` () =
        let config = configFor V1_0
        let c = circuit { qubits 2; Reset 0 }
        let qasm = OpenQasm.exportWithConfig config c
        Assert.Contains("OPENQASM 1.0;", qasm)
        Assert.Contains("reset q[0];", qasm)

    [<Fact>]
    let ``exportWithConfig V1_0 Barrier produces barrier instruction`` () =
        let config = configFor V1_0
        let c = circuit { qubits 3; Barrier [0; 2] }
        let qasm = OpenQasm.exportWithConfig config c
        Assert.Contains("barrier q[0],q[2];", qasm)

    // ── OpenQASM Import — Reset ───────────────────────────────────────

    [<Fact>]
    let ``import parses reset instruction V2_0`` () =
        let qasm = "OPENQASM 2.0;\ninclude \"qelib1.inc\";\nqreg q[2];\nreset q[0];"
        let result = OpenQasmImport.parse qasm
        match result with
        | Ok circuit ->
            Assert.Equal(2, circuit.QubitCount)
            Assert.Equal<Gate list>([Reset 0], circuit.Gates)
        | Error msg -> Assert.Fail(msg)

    [<Fact>]
    let ``import parses reset instruction V3_0`` () =
        let qasm = "OPENQASM 3.0;\ninclude \"stdgates.inc\";\nqubit[2] q;\nreset q[1];"
        let result = OpenQasmImport.parse qasm
        match result with
        | Ok circuit ->
            Assert.Equal(2, circuit.QubitCount)
            Assert.Equal<Gate list>([Reset 1], circuit.Gates)
        | Error msg -> Assert.Fail(msg)

    [<Fact>]
    let ``import parses multiple reset instructions`` () =
        let qasm = "OPENQASM 2.0;\ninclude \"qelib1.inc\";\nqreg q[3];\nreset q[0];\nreset q[1];\nreset q[2];"
        let result = OpenQasmImport.parse qasm
        match result with
        | Ok circuit ->
            Assert.Equal<Gate list>([Reset 0; Reset 1; Reset 2], circuit.Gates)
        | Error msg -> Assert.Fail(msg)

    [<Fact>]
    let ``import rejects reset before qubit declaration`` () =
        let qasm = "OPENQASM 2.0;\ninclude \"qelib1.inc\";\nreset q[0];\nqreg q[2];"
        let result = OpenQasmImport.parse qasm
        Assert.True(Result.isError result)

    [<Fact>]
    let ``import rejects reset with out-of-bounds qubit`` () =
        let qasm = "OPENQASM 2.0;\ninclude \"qelib1.inc\";\nqreg q[2];\nreset q[5];"
        let result = OpenQasmImport.parse qasm
        Assert.True(Result.isError result)

    // ── OpenQASM Import — Barrier ─────────────────────────────────────

    [<Fact>]
    let ``import parses barrier with multiple qubits V2_0`` () =
        let qasm = "OPENQASM 2.0;\ninclude \"qelib1.inc\";\nqreg q[3];\nbarrier q[0],q[1],q[2];"
        let result = OpenQasmImport.parse qasm
        match result with
        | Ok circuit ->
            Assert.Equal(3, circuit.QubitCount)
            Assert.Equal<Gate list>([Barrier [0; 1; 2]], circuit.Gates)
        | Error msg -> Assert.Fail(msg)

    [<Fact>]
    let ``import parses barrier with single qubit`` () =
        let qasm = "OPENQASM 2.0;\ninclude \"qelib1.inc\";\nqreg q[2];\nbarrier q[1];"
        let result = OpenQasmImport.parse qasm
        match result with
        | Ok circuit ->
            Assert.Equal<Gate list>([Barrier [1]], circuit.Gates)
        | Error msg -> Assert.Fail(msg)

    [<Fact>]
    let ``import parses barrier V3_0`` () =
        let qasm = "OPENQASM 3.0;\ninclude \"stdgates.inc\";\nqubit[3] q;\nbarrier q[0],q[2];"
        let result = OpenQasmImport.parse qasm
        match result with
        | Ok circuit ->
            Assert.Equal<Gate list>([Barrier [0; 2]], circuit.Gates)
        | Error msg -> Assert.Fail(msg)

    [<Fact>]
    let ``import parses barrier with spaces around brackets`` () =
        let qasm = "OPENQASM 2.0;\ninclude \"qelib1.inc\";\nqreg q[3];\nbarrier q[ 0 ], q[ 1 ];"
        let result = OpenQasmImport.parse qasm
        match result with
        | Ok circuit ->
            Assert.Equal<Gate list>([Barrier [0; 1]], circuit.Gates)
        | Error msg -> Assert.Fail(msg)

    [<Fact>]
    let ``import rejects barrier before qubit declaration`` () =
        let qasm = "OPENQASM 2.0;\ninclude \"qelib1.inc\";\nbarrier q[0],q[1];\nqreg q[2];"
        let result = OpenQasmImport.parse qasm
        Assert.True(Result.isError result)

    [<Fact>]
    let ``import rejects barrier with out-of-bounds qubit`` () =
        let qasm = "OPENQASM 2.0;\ninclude \"qelib1.inc\";\nqreg q[2];\nbarrier q[0],q[10];"
        let result = OpenQasmImport.parse qasm
        Assert.True(Result.isError result)

    // ── Mixed circuits import ─────────────────────────────────────────

    [<Fact>]
    let ``import mixed circuit with gates reset and barrier`` () =
        let qasm =
            "OPENQASM 2.0;\n" +
            "include \"qelib1.inc\";\n" +
            "qreg q[3];\n" +
            "h q[0];\n" +
            "barrier q[0],q[1],q[2];\n" +
            "cx q[0],q[1];\n" +
            "reset q[2];\n" +
            "x q[1];"
        let result = OpenQasmImport.parse qasm
        match result with
        | Ok circuit ->
            Assert.Equal(3, circuit.QubitCount)
            Assert.Equal<Gate list>(
                [H 0; Barrier [0; 1; 2]; CNOT(0, 1); Reset 2; X 1],
                circuit.Gates)
        | Error msg -> Assert.Fail(msg)

    [<Fact>]
    let ``import V3_0 mixed circuit with reset barrier and measurement`` () =
        let qasm =
            "OPENQASM 3.0;\n" +
            "include \"stdgates.inc\";\n" +
            "qubit[2] q;\n" +
            "bit[1] c;\n" +
            "h q[0];\n" +
            "barrier q[0],q[1];\n" +
            "reset q[0];\n" +
            "c[0] = measure q[1];"
        let result = OpenQasmImport.parse qasm
        match result with
        | Ok circuit ->
            Assert.Equal(2, circuit.QubitCount)
            Assert.Contains(H 0, circuit.Gates)
            Assert.Contains(Barrier [0; 1], circuit.Gates)
            Assert.Contains(Reset 0, circuit.Gates)
            Assert.Contains(Measure 1, circuit.Gates)
        | Error msg -> Assert.Fail(msg)

    // ── Round-trip fidelity ───────────────────────────────────────────

    [<Fact>]
    let ``round-trip Reset gate V2_0`` () =
        let original = circuit { qubits 2; Reset 0 }
        let exported = OpenQasm.export original
        let imported = OpenQasmImport.parse exported
        match imported with
        | Ok parsed ->
            Assert.Equal(original.QubitCount, parsed.QubitCount)
            Assert.Equal<Gate list>(original.Gates, parsed.Gates)
        | Error msg -> Assert.Fail($"Round-trip failed: {msg}")

    [<Fact>]
    let ``round-trip Barrier gate V2_0`` () =
        let original = circuit { qubits 3; Barrier [0; 1; 2] }
        let exported = OpenQasm.export original
        let imported = OpenQasmImport.parse exported
        match imported with
        | Ok parsed ->
            Assert.Equal(original.QubitCount, parsed.QubitCount)
            Assert.Equal<Gate list>(original.Gates, parsed.Gates)
        | Error msg -> Assert.Fail($"Round-trip failed: {msg}")

    [<Fact>]
    let ``round-trip Reset gate V3_0`` () =
        let original = circuit { qubits 2; Reset 1 }
        let exported = OpenQasm.exportV3 original
        let imported = OpenQasmImport.parse exported
        match imported with
        | Ok parsed ->
            Assert.Equal(original.QubitCount, parsed.QubitCount)
            Assert.Equal<Gate list>(original.Gates, parsed.Gates)
        | Error msg -> Assert.Fail($"Round-trip failed: {msg}")

    [<Fact>]
    let ``round-trip Barrier gate V3_0`` () =
        let original = circuit { qubits 4; Barrier [1; 3] }
        let exported = OpenQasm.exportV3 original
        let imported = OpenQasmImport.parse exported
        match imported with
        | Ok parsed ->
            Assert.Equal(original.QubitCount, parsed.QubitCount)
            Assert.Equal<Gate list>(original.Gates, parsed.Gates)
        | Error msg -> Assert.Fail($"Round-trip failed: {msg}")

    [<Fact>]
    let ``round-trip mixed circuit with Reset Barrier and standard gates V2_0`` () =
        let original = circuit {
            qubits 3
            H 0
            Barrier [0; 1; 2]
            CNOT 0 1
            Reset 2
            X 1
        }
        let exported = OpenQasm.export original
        let imported = OpenQasmImport.parse exported
        match imported with
        | Ok parsed ->
            Assert.Equal(original.QubitCount, parsed.QubitCount)
            Assert.Equal<Gate list>(original.Gates, parsed.Gates)
        | Error msg -> Assert.Fail($"Round-trip failed: {msg}")

    [<Fact>]
    let ``round-trip mixed circuit with Reset Barrier and Measure V2_0`` () =
        let original = circuit {
            qubits 2
            H 0
            Reset 1
            Barrier [0; 1]
            Measure 0
        }
        let exported = OpenQasm.export original
        let imported = OpenQasmImport.parse exported
        match imported with
        | Ok parsed ->
            Assert.Equal(original.QubitCount, parsed.QubitCount)
            Assert.Equal<Gate list>(original.Gates, parsed.Gates)
        | Error msg -> Assert.Fail($"Round-trip failed: {msg}")

    [<Fact>]
    let ``round-trip mixed circuit V3_0`` () =
        let original = circuit {
            qubits 3
            H 0
            Barrier [0; 1]
            Reset 2
            CNOT 0 1
            Measure 0
        }
        let exported = OpenQasm.exportV3 original
        let imported = OpenQasmImport.parse exported
        match imported with
        | Ok parsed ->
            Assert.Equal(original.QubitCount, parsed.QubitCount)
            Assert.Equal<Gate list>(original.Gates, parsed.Gates)
        | Error msg -> Assert.Fail($"Round-trip failed: {msg}")

    // ── Edge cases ────────────────────────────────────────────────────

    [<Fact>]
    let ``Reset on last qubit index`` () =
        let c = circuit { qubits 5; Reset 4 }
        let result = CircuitBuilder.validate c
        Assert.True(result.IsValid)
        let qasm = OpenQasm.export c
        Assert.Contains("reset q[4];", qasm)

    [<Fact>]
    let ``Barrier on all qubits of large circuit`` () =
        let c = { QubitCount = 8; Gates = [Barrier [0; 1; 2; 3; 4; 5; 6; 7]] }
        let result = CircuitBuilder.validate c
        Assert.True(result.IsValid)
        let qasm = OpenQasm.export c
        Assert.Contains("barrier q[0],q[1],q[2],q[3],q[4],q[5],q[6],q[7];", qasm)

    [<Fact>]
    let ``multiple Reset gates on same qubit`` () =
        let c = circuit { qubits 2; Reset 0; H 0; Reset 0 }
        let result = CircuitBuilder.validate c
        Assert.True(result.IsValid)
        Assert.Equal<Gate list>([Reset 0; H 0; Reset 0], c.Gates)

    [<Fact>]
    let ``multiple Barrier gates in sequence`` () =
        let c = circuit {
            qubits 3
            Barrier [0; 1]
            Barrier [1; 2]
            Barrier [0; 1; 2]
        }
        let result = CircuitBuilder.validate c
        Assert.True(result.IsValid)
        Assert.Equal(3, c.Gates.Length)

    [<Fact>]
    let ``Reset on qubit 0 in single-qubit circuit`` () =
        let c = circuit { qubits 1; Reset 0 }
        let result = CircuitBuilder.validate c
        Assert.True(result.IsValid)
        let qasm = OpenQasm.export c
        Assert.Contains("reset q[0];", qasm)

    [<Fact>]
    let ``Barrier non-contiguous qubits`` () =
        let c = circuit { qubits 10; Barrier [0; 3; 7; 9] }
        let result = CircuitBuilder.validate c
        Assert.True(result.IsValid)
        let qasm = OpenQasm.export c
        Assert.Contains("barrier q[0],q[3],q[7],q[9];", qasm)

    // ── Whole-register barrier import ─────────────────────────────────

    [<Fact>]
    let ``import parses barrier with bare register name V2_0`` () =
        let qasm = "OPENQASM 2.0;\ninclude \"qelib1.inc\";\nqreg q[3];\nbarrier q;"
        let result = OpenQasmImport.parse qasm
        match result with
        | Ok circuit ->
            Assert.Equal(3, circuit.QubitCount)
            Assert.Equal<Gate list>([Barrier [0; 1; 2]], circuit.Gates)
        | Error msg -> Assert.Fail(msg)

    [<Fact>]
    let ``import parses barrier with bare register name V3_0`` () =
        let qasm = "OPENQASM 3.0;\ninclude \"stdgates.inc\";\nqubit[4] q;\nbarrier q;"
        let result = OpenQasmImport.parse qasm
        match result with
        | Ok circuit ->
            Assert.Equal(4, circuit.QubitCount)
            Assert.Equal<Gate list>([Barrier [0; 1; 2; 3]], circuit.Gates)
        | Error msg -> Assert.Fail(msg)

    [<Fact>]
    let ``import parses barrier with multiple bare register names`` () =
        let qasm =
            "OPENQASM 2.0;\n" +
            "include \"qelib1.inc\";\n" +
            "qreg a[2];\n" +
            "qreg b[3];\n" +
            "barrier a,b;"
        let result = OpenQasmImport.parse qasm
        match result with
        | Ok circuit ->
            Assert.Equal(5, circuit.QubitCount)
            // a = qubits 0,1 and b = qubits 2,3,4
            Assert.Equal<Gate list>([Barrier [0; 1; 2; 3; 4]], circuit.Gates)
        | Error msg -> Assert.Fail(msg)

    [<Fact>]
    let ``import rejects barrier with unknown bare register name`` () =
        let qasm = "OPENQASM 2.0;\ninclude \"qelib1.inc\";\nqreg q[2];\nbarrier xyz;"
        let result = OpenQasmImport.parse qasm
        Assert.True(Result.isError result)

    // ── LocalBackend Reset simulation ─────────────────────────────────

    [<Fact>]
    let ``LocalBackend executes circuit with Reset without crashing`` () =
        let c = circuit {
            qubits 2
            X 0       // put qubit 0 into |1⟩
            Reset 0   // should reset it back to |0⟩
        }
        let backend = LocalBackend.LocalBackend() :> IQuantumBackend
        let wrapped = CircuitWrapper(c) :> ICircuit
        match backend.ExecuteToState wrapped with
        | Ok (QuantumState.StateVector sv) ->
            // After X then Reset, qubit 0 should be back in |0⟩
            // The state should be close to |00⟩
            let prob00 = StateVector.probability 0 sv
            Assert.True(prob00 > 0.99, $"|00⟩ probability should be ~1.0 after Reset, got {prob00}")
        | Ok _ ->
            Assert.Fail("Expected StateVector, got different state type")
        | Error err -> Assert.Fail($"Execution failed: {err}")

    [<Fact>]
    let ``LocalBackend executes circuit with Barrier as no-op`` () =
        let c = circuit {
            qubits 2
            H 0
            Barrier [0; 1]
            CNOT 0 1
        }
        // Should not throw — Barrier is a no-op in simulation
        let backend = LocalBackend.LocalBackend() :> IQuantumBackend
        let wrapped = CircuitWrapper(c) :> ICircuit
        let result = backend.ExecuteToState wrapped
        Assert.True(Result.isOk result)
