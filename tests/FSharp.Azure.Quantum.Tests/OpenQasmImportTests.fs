namespace FSharp.Azure.Quantum.Tests

open Xunit
open FSharp.Azure.Quantum
open FSharp.Azure.Quantum.CircuitBuilder

/// TKT-97: OpenQASM 2.0 Import Tests
/// Following TDD approach with comprehensive coverage for parsing OpenQASM circuits
module OpenQasmImportTests =
    
    // ========================================================================
    // BASIC PARSING TESTS
    // ========================================================================
    
    [<Fact>]
    let ``parse simple X gate`` () =
        let qasm = """
OPENQASM 2.0;
include "qelib1.inc";
qreg q[1];
x q[0];
"""
        let result = OpenQasmImport.parse qasm
        
        match result with
        | Ok circuit ->
            Assert.Equal(1, circuit.QubitCount)
            Assert.Equal(1, circuit.Gates.Length)
            match circuit.Gates.[0] with
            | X 0 -> ()
            | _ -> Assert.True(false, "Expected X gate on qubit 0")
        | Error msg -> Assert.True(false, $"Parse failed: {msg}")
    
    [<Fact>]
    let ``parse Y gate`` () =
        let qasm = """
OPENQASM 2.0;
include "qelib1.inc";
qreg q[1];
y q[0];
"""
        let result = OpenQasmImport.parse qasm
        
        match result with
        | Ok circuit ->
            match circuit.Gates.[0] with
            | Y 0 -> ()
            | _ -> Assert.True(false, "Expected Y gate")
        | Error msg -> Assert.True(false, $"Parse failed: {msg}")
    
    [<Fact>]
    let ``parse Z gate`` () =
        let qasm = """
OPENQASM 2.0;
include "qelib1.inc";
qreg q[1];
z q[0];
"""
        let result = OpenQasmImport.parse qasm
        
        match result with
        | Ok circuit ->
            match circuit.Gates.[0] with
            | Z 0 -> ()
            | _ -> Assert.True(false, "Expected Z gate")
        | Error msg -> Assert.True(false, $"Parse failed: {msg}")
    
    [<Fact>]
    let ``parse H gate`` () =
        let qasm = """
OPENQASM 2.0;
include "qelib1.inc";
qreg q[1];
h q[0];
"""
        let result = OpenQasmImport.parse qasm
        
        match result with
        | Ok circuit ->
            match circuit.Gates.[0] with
            | H 0 -> ()
            | _ -> Assert.True(false, "Expected H gate")
        | Error msg -> Assert.True(false, $"Parse failed: {msg}")
    
    // ========================================================================
    // PHASE GATE TESTS
    // ========================================================================
    
    [<Fact>]
    let ``parse S gate`` () =
        let qasm = """
OPENQASM 2.0;
include "qelib1.inc";
qreg q[1];
s q[0];
"""
        let result = OpenQasmImport.parse qasm
        
        match result with
        | Ok circuit ->
            match circuit.Gates.[0] with
            | S 0 -> ()
            | _ -> Assert.True(false, "Expected S gate")
        | Error msg -> Assert.True(false, $"Parse failed: {msg}")
    
    [<Fact>]
    let ``parse SDG gate`` () =
        let qasm = """
OPENQASM 2.0;
include "qelib1.inc";
qreg q[1];
sdg q[0];
"""
        let result = OpenQasmImport.parse qasm
        
        match result with
        | Ok circuit ->
            match circuit.Gates.[0] with
            | SDG 0 -> ()
            | _ -> Assert.True(false, "Expected SDG gate")
        | Error msg -> Assert.True(false, $"Parse failed: {msg}")
    
    [<Fact>]
    let ``parse T gate`` () =
        let qasm = """
OPENQASM 2.0;
include "qelib1.inc";
qreg q[1];
t q[0];
"""
        let result = OpenQasmImport.parse qasm
        
        match result with
        | Ok circuit ->
            match circuit.Gates.[0] with
            | T 0 -> ()
            | _ -> Assert.True(false, "Expected T gate")
        | Error msg -> Assert.True(false, $"Parse failed: {msg}")
    
    [<Fact>]
    let ``parse TDG gate`` () =
        let qasm = """
OPENQASM 2.0;
include "qelib1.inc";
qreg q[1];
tdg q[0];
"""
        let result = OpenQasmImport.parse qasm
        
        match result with
        | Ok circuit ->
            match circuit.Gates.[0] with
            | TDG 0 -> ()
            | _ -> Assert.True(false, "Expected TDG gate")
        | Error msg -> Assert.True(false, $"Parse failed: {msg}")
    
    // ========================================================================
    // ROTATION GATE TESTS
    // ========================================================================
    
    [<Fact>]
    let ``parse RX gate with angle`` () =
        let qasm = """
OPENQASM 2.0;
include "qelib1.inc";
qreg q[1];
rx(1.5707963268) q[0];
"""
        let result = OpenQasmImport.parse qasm
        
        match result with
        | Ok circuit ->
            match circuit.Gates.[0] with
            | RX (0, angle) ->
                Assert.InRange(angle, 1.5707, 1.5708)  // π/2
            | _ -> Assert.True(false, "Expected RX gate")
        | Error msg -> Assert.True(false, $"Parse failed: {msg}")
    
    [<Fact>]
    let ``parse RY gate with angle`` () =
        let qasm = """
OPENQASM 2.0;
include "qelib1.inc";
qreg q[1];
ry(3.1415926536) q[0];
"""
        let result = OpenQasmImport.parse qasm
        
        match result with
        | Ok circuit ->
            match circuit.Gates.[0] with
            | RY (0, angle) ->
                Assert.InRange(angle, 3.1415, 3.1416)  // π
            | _ -> Assert.True(false, "Expected RY gate")
        | Error msg -> Assert.True(false, $"Parse failed: {msg}")
    
    [<Fact>]
    let ``parse RZ gate with angle`` () =
        let qasm = """
OPENQASM 2.0;
include "qelib1.inc";
qreg q[1];
rz(0.7853981634) q[0];
"""
        let result = OpenQasmImport.parse qasm
        
        match result with
        | Ok circuit ->
            match circuit.Gates.[0] with
            | RZ (0, angle) ->
                Assert.InRange(angle, 0.7853, 0.7854)  // π/4
            | _ -> Assert.True(false, "Expected RZ gate")
        | Error msg -> Assert.True(false, $"Parse failed: {msg}")
    
    // ========================================================================
    // TWO-QUBIT GATE TESTS
    // ========================================================================
    
    [<Fact>]
    let ``parse CNOT gate`` () =
        let qasm = """
OPENQASM 2.0;
include "qelib1.inc";
qreg q[2];
cx q[0],q[1];
"""
        let result = OpenQasmImport.parse qasm
        
        match result with
        | Ok circuit ->
            Assert.Equal(2, circuit.QubitCount)
            match circuit.Gates.[0] with
            | CNOT (0, 1) -> ()
            | _ -> Assert.True(false, "Expected CNOT gate")
        | Error msg -> Assert.True(false, $"Parse failed: {msg}")
    
    [<Fact>]
    let ``parse CZ gate`` () =
        let qasm = """
OPENQASM 2.0;
include "qelib1.inc";
qreg q[2];
cz q[0],q[1];
"""
        let result = OpenQasmImport.parse qasm
        
        match result with
        | Ok circuit ->
            match circuit.Gates.[0] with
            | CZ (0, 1) -> ()
            | _ -> Assert.True(false, "Expected CZ gate")
        | Error msg -> Assert.True(false, $"Parse failed: {msg}")
    
    [<Fact>]
    let ``parse SWAP gate`` () =
        let qasm = """
OPENQASM 2.0;
include "qelib1.inc";
qreg q[2];
swap q[0],q[1];
"""
        let result = OpenQasmImport.parse qasm
        
        match result with
        | Ok circuit ->
            match circuit.Gates.[0] with
            | SWAP (0, 1) -> ()
            | _ -> Assert.True(false, "Expected SWAP gate")
        | Error msg -> Assert.True(false, $"Parse failed: {msg}")
    
    // ========================================================================
    // THREE-QUBIT GATE TESTS
    // ========================================================================
    
    [<Fact>]
    let ``parse CCX (Toffoli) gate`` () =
        let qasm = """
OPENQASM 2.0;
include "qelib1.inc";
qreg q[3];
ccx q[0],q[1],q[2];
"""
        let result = OpenQasmImport.parse qasm
        
        match result with
        | Ok circuit ->
            Assert.Equal(3, circuit.QubitCount)
            match circuit.Gates.[0] with
            | CCX (0, 1, 2) -> ()
            | _ -> Assert.True(false, "Expected CCX gate")
        | Error msg -> Assert.True(false, $"Parse failed: {msg}")
    
    // ========================================================================
    // MULTI-GATE CIRCUIT TESTS
    // ========================================================================
    
    [<Fact>]
    let ``parse Bell state circuit`` () =
        let qasm = """
OPENQASM 2.0;
include "qelib1.inc";
qreg q[2];
h q[0];
cx q[0],q[1];
"""
        let result = OpenQasmImport.parse qasm
        
        match result with
        | Ok circuit ->
            Assert.Equal(2, circuit.QubitCount)
            Assert.Equal(2, circuit.Gates.Length)
            match circuit.Gates.[0], circuit.Gates.[1] with
            | H 0, CNOT (0, 1) -> ()
            | _ -> Assert.True(false, "Expected H and CNOT gates")
        | Error msg -> Assert.True(false, $"Parse failed: {msg}")
    
    [<Fact>]
    let ``parse complex circuit with multiple gates`` () =
        let qasm = """
OPENQASM 2.0;
include "qelib1.inc";
qreg q[3];
h q[0];
s q[1];
t q[2];
cx q[0],q[1];
cz q[1],q[2];
ccx q[0],q[1],q[2];
"""
        let result = OpenQasmImport.parse qasm
        
        match result with
        | Ok circuit ->
            Assert.Equal(3, circuit.QubitCount)
            Assert.Equal(6, circuit.Gates.Length)
        | Error msg -> Assert.True(false, $"Parse failed: {msg}")
    
    // ========================================================================
    // WHITESPACE AND FORMATTING TESTS
    // ========================================================================
    
    [<Fact>]
    let ``parse with extra whitespace`` () =
        let qasm = """
OPENQASM 2.0;
include "qelib1.inc";

qreg q[2];

h q[0];
cx q[0],q[1];

"""
        let result = OpenQasmImport.parse qasm
        
        match result with
        | Ok circuit ->
            Assert.Equal(2, circuit.Gates.Length)
        | Error msg -> Assert.True(false, $"Parse failed: {msg}")
    
    [<Fact>]
    let ``parse with comments`` () =
        let qasm = """
// This is a Bell state
OPENQASM 2.0;
include "qelib1.inc";
qreg q[2];  // 2 qubits
h q[0];     // Hadamard on qubit 0
cx q[0],q[1];  // CNOT
"""
        let result = OpenQasmImport.parse qasm
        
        match result with
        | Ok circuit ->
            Assert.Equal(2, circuit.Gates.Length)
        | Error msg -> Assert.True(false, $"Parse failed: {msg}")
    
    [<Fact>]
    let ``parse with no spaces around commas`` () =
        let qasm = """
OPENQASM 2.0;
include "qelib1.inc";
qreg q[2];
cx q[0],q[1];
"""
        let result = OpenQasmImport.parse qasm
        Assert.True(result.IsOk, "Should parse CNOT without spaces")
    
    [<Fact>]
    let ``parse with spaces around commas`` () =
        let qasm = """
OPENQASM 2.0;
include "qelib1.inc";
qreg q[2];
cx q[0] , q[1];
"""
        let result = OpenQasmImport.parse qasm
        Assert.True(result.IsOk, "Should parse CNOT with spaces")
    
    // ========================================================================
    // ERROR HANDLING TESTS
    // ========================================================================
    
    [<Fact>]
    let ``parse missing version should fail`` () =
        let qasm = """
include "qelib1.inc";
qreg q[1];
x q[0];
"""
        let result = OpenQasmImport.parse qasm
        Assert.True(result.IsError, "Should fail without version")
    
    [<Fact>]
    let ``parse invalid version should fail`` () =
        let qasm = """
OPENQASM 99.0;
include "qelib1.inc";
qreg q[1];
x q[0];
"""
        let result = OpenQasmImport.parse qasm
        Assert.True(result.IsError, "Should fail with unsupported version")
    
    [<Fact>]
    let ``parse missing qreg should fail`` () =
        let qasm = """
OPENQASM 2.0;
include "qelib1.inc";
x q[0];
"""
        let result = OpenQasmImport.parse qasm
        Assert.True(result.IsError, "Should fail without qreg declaration")
    
    [<Fact>]
    let ``parse unknown gate should fail`` () =
        let qasm = """
OPENQASM 2.0;
include "qelib1.inc";
qreg q[1];
unknown_gate q[0];
"""
        let result = OpenQasmImport.parse qasm
        Assert.True(result.IsError, "Should fail with unknown gate")
    
    [<Fact>]
    let ``parse invalid qubit index should fail`` () =
        let qasm = """
OPENQASM 2.0;
include "qelib1.inc";
qreg q[2];
x q[5];
"""
        let result = OpenQasmImport.parse qasm
        Assert.True(result.IsError, "Should fail with out-of-bounds qubit index")
    
    [<Fact>]
    let ``parse malformed gate syntax should fail`` () =
        let qasm = """
OPENQASM 2.0;
include "qelib1.inc";
qreg q[2];
cx q[0];
"""
        let result = OpenQasmImport.parse qasm
        Assert.True(result.IsError, "Should fail with incomplete CNOT")
    
    // ========================================================================
    // ROUND-TRIP TESTS (Export → Import → Export)
    // ========================================================================
    
    [<Fact>]
    let ``round-trip simple circuit`` () =
        let original = { QubitCount = 2; Gates = [H 0; CNOT (0, 1)] }
        
        // Export to QASM
        let qasm = OpenQasmExport.export original
        
        // Import back
        let result = OpenQasmImport.parse qasm
        
        match result with
        | Ok imported ->
            Assert.Equal(original.QubitCount, imported.QubitCount)
            Assert.Equal(original.Gates.Length, imported.Gates.Length)
            Assert.Equal<Gate list>(original.Gates, imported.Gates)
        | Error msg -> Assert.True(false, $"Round-trip failed: {msg}")
    
    [<Fact>]
    let ``round-trip circuit with rotation gates`` () =
        let original = { 
            QubitCount = 2
            Gates = [
                RX (0, System.Math.PI / 2.0)
                RY (1, System.Math.PI / 4.0)
                RZ (0, System.Math.PI)
                CNOT (0, 1)
            ]
        }
        
        let qasm = OpenQasmExport.export original
        let result = OpenQasmImport.parse qasm
        
        match result with
        | Ok imported ->
            Assert.Equal(original.QubitCount, imported.QubitCount)
            Assert.Equal(original.Gates.Length, imported.Gates.Length)
            
            // Check angles are approximately equal (floating point precision)
            for i in 0 .. original.Gates.Length - 1 do
                match original.Gates.[i], imported.Gates.[i] with
                | RX (q1, a1), RX (q2, a2) ->
                    Assert.Equal(q1, q2)
                    Assert.Equal(a1, a2, 9)  // 9 decimal places
                | RY (q1, a1), RY (q2, a2) ->
                    Assert.Equal(q1, q2)
                    Assert.Equal(a1, a2, 9)
                | RZ (q1, a1), RZ (q2, a2) ->
                    Assert.Equal(q1, q2)
                    Assert.Equal(a1, a2, 9)
                | g1, g2 -> Assert.Equal(g1, g2)
        | Error msg -> Assert.True(false, $"Round-trip failed: {msg}")
    
    [<Fact>]
    let ``round-trip circuit with all gate types`` () =
        let original = {
            QubitCount = 3
            Gates = [
                X 0; Y 1; Z 2
                H 0; S 1; SDG 2
                T 0; TDG 1
                RX (0, 1.5); RY (1, 2.5); RZ (2, 3.5)
                CNOT (0, 1); CZ (1, 2); SWAP (0, 2)
                CCX (0, 1, 2)
            ]
        }
        
        let qasm = OpenQasmExport.export original
        let result = OpenQasmImport.parse qasm
        
        match result with
        | Ok imported ->
            Assert.Equal(original.QubitCount, imported.QubitCount)
            Assert.Equal(original.Gates.Length, imported.Gates.Length)
        | Error msg -> Assert.True(false, $"Round-trip failed: {msg}")
    
    // ========================================================================
    // FILE I/O TESTS
    // ========================================================================
    
    [<Fact>]
    let ``parseFromFile should load and parse file`` () =
        // Create temp file
        let tempFile = System.IO.Path.GetTempFileName() + ".qasm"
        let qasm = """
OPENQASM 2.0;
include "qelib1.inc";
qreg q[2];
h q[0];
cx q[0],q[1];
"""
        System.IO.File.WriteAllText(tempFile, qasm)
        
        try
            let result = OpenQasmImport.parseFromFile tempFile
            
            match result with
            | Ok circuit ->
                Assert.Equal(2, circuit.QubitCount)
                Assert.Equal(2, circuit.Gates.Length)
            | Error msg -> Assert.True(false, $"File parse failed: {msg}")
        finally
            if System.IO.File.Exists(tempFile) then
                System.IO.File.Delete(tempFile)
    
    [<Fact>]
    let ``parseFromFile with non-existent file should fail`` () =
        let result = OpenQasmImport.parseFromFile "non_existent_file.qasm"
        Assert.True(result.IsError, "Should fail for non-existent file")
