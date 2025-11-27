namespace FSharp.Azure.Quantum.Tests

open Xunit
open FSharp.Azure.Quantum
open FSharp.Azure.Quantum.CircuitBuilder

/// TKT-97: OpenQASM 2.0 Export Tests
/// Following TDD approach with comprehensive coverage
module OpenQasmExportTests =
    
    // ========================================================================
    // BASIC GATE TRANSLATION TESTS
    // ========================================================================
    
    [<Fact>]
    let ``export simple X gate`` () =
        let circuit = { QubitCount = 1; Gates = [X 0] }
        let qasm = OpenQasm.export circuit
        
        Assert.Contains("OPENQASM 2.0;", qasm)
        Assert.Contains("include \"qelib1.inc\";", qasm)
        Assert.Contains("qreg q[1];", qasm)
        Assert.Contains("x q[0];", qasm)
    
    [<Fact>]
    let ``export Y gate`` () =
        let circuit = { QubitCount = 1; Gates = [Y 0] }
        let qasm = OpenQasm.export circuit
        Assert.Contains("y q[0];", qasm)
    
    [<Fact>]
    let ``export Z gate`` () =
        let circuit = { QubitCount = 1; Gates = [Z 0] }
        let qasm = OpenQasm.export circuit
        Assert.Contains("z q[0];", qasm)
    
    [<Fact>]
    let ``export H gate`` () =
        let circuit = { QubitCount = 1; Gates = [H 0] }
        let qasm = OpenQasm.export circuit
        Assert.Contains("h q[0];", qasm)
    
    [<Fact>]
    let ``export CNOT gate`` () =
        let circuit = { QubitCount = 2; Gates = [CNOT (0, 1)] }
        let qasm = OpenQasm.export circuit
        Assert.Contains("cx q[0],q[1];", qasm)
    
    // ========================================================================
    // ROTATION GATE TESTS WITH ANGLE FORMATTING
    // ========================================================================
    
    [<Fact>]
    let ``export RX gate with formatted angle`` () =
        let circuit = { QubitCount = 1; Gates = [RX (0, 1.5707963267948966)] }  // π/2
        let qasm = OpenQasm.export circuit
        
        // Should have 10 decimal places
        Assert.Contains("rx(1.5707963268) q[0];", qasm)
    
    [<Fact>]
    let ``export RY gate with formatted angle`` () =
        let circuit = { QubitCount = 1; Gates = [RY (0, System.Math.PI)] }
        let qasm = OpenQasm.export circuit
        Assert.Contains("ry(3.1415926536) q[0];", qasm)
    
    [<Fact>]
    let ``export RZ gate with formatted angle`` () =
        let circuit = { QubitCount = 1; Gates = [RZ (0, 0.5)] }
        let qasm = OpenQasm.export circuit
        Assert.Contains("rz(0.5000000000) q[0];", qasm)
    
    [<Fact>]
    let ``angle formatting preserves precision`` () =
        let circuit = { QubitCount = 1; Gates = [RX (0, 1.23456789012345)] }
        let qasm = OpenQasm.export circuit
        // Should round to 10 decimal places
        Assert.Contains("rx(1.2345678901) q[0];", qasm)
    
    // ========================================================================
    // MULTI-GATE CIRCUIT TESTS
    // ========================================================================
    
    [<Fact>]
    let ``export Bell state circuit`` () =
        let circuit = {
            QubitCount = 2
            Gates = [H 0; CNOT (0, 1)]
        }
        let qasm = OpenQasm.export circuit
        
        Assert.Contains("qreg q[2];", qasm)
        Assert.Contains("h q[0];", qasm)
        Assert.Contains("cx q[0],q[1];", qasm)
    
    [<Fact>]
    let ``export gates in correct order`` () =
        let circuit = {
            QubitCount = 3
            Gates = [H 0; X 1; CNOT (0, 1); Y 2; RZ (2, 0.5)]
        }
        let qasm = OpenQasm.export circuit
        let lines = qasm.Split('\n')
        
        // Find gate lines (skip header and register declarations)
        let gateLines = 
            lines 
            |> Array.filter (fun line -> 
                line.Contains("h q") || line.Contains("x q") || 
                line.Contains("cx q") || line.Contains("y q") || line.Contains("rz(")
            )
        
        Assert.Equal(5, gateLines.Length)
        Assert.Contains("h q[0];", gateLines.[0])
        Assert.Contains("x q[1];", gateLines.[1])
        Assert.Contains("cx q[0],q[1];", gateLines.[2])
        Assert.Contains("y q[2];", gateLines.[3])
        Assert.Contains("rz(0.5000000000) q[2];", gateLines.[4])
    
    // ========================================================================
    // HEADER AND REGISTER TESTS
    // ========================================================================
    
    [<Fact>]
    let ``export includes OPENQASM version header`` () =
        let circuit = { QubitCount = 1; Gates = [H 0] }
        let qasm = OpenQasm.export circuit
        Assert.StartsWith("OPENQASM 2.0;", qasm.Trim())
    
    [<Fact>]
    let ``export includes qelib1 include`` () =
        let circuit = { QubitCount = 1; Gates = [H 0] }
        let qasm = OpenQasm.export circuit
        let lines = qasm.Split('\n')
        Assert.Contains("include \"qelib1.inc\";", lines.[1])
    
    [<Fact>]
    let ``export creates correct qubit register size`` () =
        let circuit1 = { QubitCount = 1; Gates = [] }
        let circuit2 = { QubitCount = 5; Gates = [] }
        let circuit3 = { QubitCount = 20; Gates = [] }
        
        Assert.Contains("qreg q[1];", OpenQasm.export circuit1)
        Assert.Contains("qreg q[5];", OpenQasm.export circuit2)
        Assert.Contains("qreg q[20];", OpenQasm.export circuit3)
    
    [<Fact>]
    let ``export empty circuit has header and registers only`` () =
        let circuit = { QubitCount = 2; Gates = [] }
        let qasm = OpenQasm.export circuit
        let lines = qasm.Split('\n') |> Array.filter (fun s -> s.Length > 0)
        
        // Should have exactly 3 lines: OPENQASM 2.0, include, qreg
        Assert.Equal(3, lines.Length)
    
    // ========================================================================
    // VALIDATION TESTS
    // ========================================================================
    
    [<Fact>]
    let ``validate accepts valid circuit`` () =
        let circuit = { QubitCount = 2; Gates = [H 0; CNOT (0, 1)] }
        let result = OpenQasm.validate circuit
        
        match result with
        | Ok () -> Assert.True(true)
        | Error msg -> Assert.True(false, $"Expected Ok, got Error: {msg}")
    
    [<Fact>]
    let ``validate detects qubit out of range`` () =
        let circuit = { QubitCount = 2; Gates = [H 0; X 2] }  // Qubit 2 doesn't exist
        let result = OpenQasm.validate circuit
        
        match result with
        | Ok () -> Assert.True(false, "Expected Error for out-of-range qubit")
        | Error msg -> 
            Assert.Contains("qubit", msg.ToLower())
            Assert.Contains("range", msg.ToLower())
    
    [<Fact>]
    let ``validate detects CNOT with out of range control`` () =
        let circuit = { QubitCount = 2; Gates = [CNOT (3, 0)] }
        let result = OpenQasm.validate circuit
        
        match result with
        | Error msg -> Assert.Contains("qubit", msg.ToLower())
        | Ok () -> Assert.True(false, "Expected validation error")
    
    [<Fact>]
    let ``validate detects CNOT with out of range target`` () =
        let circuit = { QubitCount = 2; Gates = [CNOT (0, 5)] }
        let result = OpenQasm.validate circuit
        
        match result with
        | Error msg -> Assert.Contains("qubit", msg.ToLower())
        | Ok () -> Assert.True(false, "Expected validation error")
    
    [<Fact>]
    let ``validate accepts zero qubits`` () =
        let circuit = { QubitCount = 0; Gates = [] }
        let result = OpenQasm.validate circuit
        
        match result with
        | Ok () -> Assert.True(true)
        | Error _ -> Assert.True(false, "Zero qubits should be valid")
    
    [<Fact>]
    let ``validate detects negative qubit count`` () =
        let circuit = { QubitCount = -1; Gates = [] }
        let result = OpenQasm.validate circuit
        
        match result with
        | Error msg -> Assert.Contains("qubit", msg.ToLower())
        | Ok () -> Assert.True(false, "Negative qubit count should be invalid")
    
    // ========================================================================
    // FILE EXPORT TESTS
    // ========================================================================
    
    [<Fact>]
    let ``exportToFile creates file with correct content`` () =
        let circuit = { QubitCount = 1; Gates = [H 0] }
        let tempFile = System.IO.Path.GetTempFileName()
        let qasmFile = System.IO.Path.ChangeExtension(tempFile, ".qasm")
        
        try
            OpenQasm.exportToFile circuit qasmFile
            
            Assert.True(System.IO.File.Exists qasmFile)
            let content = System.IO.File.ReadAllText qasmFile
            Assert.Contains("OPENQASM 2.0;", content)
            Assert.Contains("h q[0];", content)
        finally
            if System.IO.File.Exists qasmFile then
                System.IO.File.Delete qasmFile
    
    [<Fact>]
    let ``exportToFile overwrites existing file`` () =
        let circuit1 = { QubitCount = 1; Gates = [H 0] }
        let circuit2 = { QubitCount = 1; Gates = [X 0] }
        let tempFile = System.IO.Path.GetTempFileName()
        let qasmFile = System.IO.Path.ChangeExtension(tempFile, ".qasm")
        
        try
            OpenQasm.exportToFile circuit1 qasmFile
            OpenQasm.exportToFile circuit2 qasmFile
            
            let content = System.IO.File.ReadAllText qasmFile
            Assert.Contains("x q[0];", content)
            Assert.DoesNotContain("h q[0];", content)
        finally
            if System.IO.File.Exists qasmFile then
                System.IO.File.Delete qasmFile
    
    // ========================================================================
    // VERSION TEST
    // ========================================================================
    
    [<Fact>]
    let ``version returns 2.0`` () =
        Assert.Equal("2.0", OpenQasm.version)
    
    // ========================================================================
    // PHASE GATE TESTS (S, SDG, T, TDG)
    // ========================================================================
    
    [<Fact>]
    let ``export S gate`` () =
        let circuit = { QubitCount = 1; Gates = [S 0] }
        let qasm = OpenQasm.export circuit
        Assert.Contains("s q[0];", qasm)
    
    [<Fact>]
    let ``export SDG gate`` () =
        let circuit = { QubitCount = 1; Gates = [SDG 0] }
        let qasm = OpenQasm.export circuit
        Assert.Contains("sdg q[0];", qasm)
    
    [<Fact>]
    let ``export T gate`` () =
        let circuit = { QubitCount = 1; Gates = [T 0] }
        let qasm = OpenQasm.export circuit
        Assert.Contains("t q[0];", qasm)
    
    [<Fact>]
    let ``export TDG gate`` () =
        let circuit = { QubitCount = 1; Gates = [TDG 0] }
        let qasm = OpenQasm.export circuit
        Assert.Contains("tdg q[0];", qasm)
    
    [<Fact>]
    let ``export phase gate sequence`` () =
        let circuit = {
            QubitCount = 1
            Gates = [H 0; S 0; T 0; TDG 0; SDG 0]
        }
        let qasm = OpenQasm.export circuit
        
        Assert.Contains("h q[0];", qasm)
        Assert.Contains("s q[0];", qasm)
        Assert.Contains("t q[0];", qasm)
        Assert.Contains("tdg q[0];", qasm)
        Assert.Contains("sdg q[0];", qasm)
    
    // ========================================================================
    // TWO-QUBIT GATE TESTS (CZ, SWAP)
    // ========================================================================
    
    [<Fact>]
    let ``export CZ gate`` () =
        let circuit = { QubitCount = 2; Gates = [CZ (0, 1)] }
        let qasm = OpenQasm.export circuit
        Assert.Contains("cz q[0],q[1];", qasm)
    
    [<Fact>]
    let ``export SWAP gate`` () =
        let circuit = { QubitCount = 2; Gates = [SWAP (0, 1)] }
        let qasm = OpenQasm.export circuit
        Assert.Contains("swap q[0],q[1];", qasm)
    
    [<Fact>]
    let ``validate detects CZ with same control and target`` () =
        let circuit = { QubitCount = 2; Gates = [CZ (0, 0)] }
        let result = OpenQasm.validate circuit
        
        match result with
        | Error msg -> Assert.Contains("different", msg.ToLower())
        | Ok () -> Assert.True(false, "Expected validation error for same qubit")
    
    [<Fact>]
    let ``validate detects SWAP with same qubits`` () =
        let circuit = { QubitCount = 2; Gates = [SWAP (1, 1)] }
        let result = OpenQasm.validate circuit
        
        match result with
        | Error msg -> Assert.Contains("different", msg.ToLower())
        | Ok () -> Assert.True(false, "Expected validation error for same qubit")
    
    [<Fact>]
    let ``validate detects CZ with out of range qubits`` () =
        let circuit = { QubitCount = 2; Gates = [CZ (0, 3)] }
        let result = OpenQasm.validate circuit
        
        match result with
        | Error msg -> Assert.Contains("range", msg.ToLower())
        | Ok () -> Assert.True(false, "Expected validation error")
    
    // ========================================================================
    // THREE-QUBIT GATE TESTS (CCX/Toffoli)
    // ========================================================================
    
    [<Fact>]
    let ``export CCX (Toffoli) gate`` () =
        let circuit = { QubitCount = 3; Gates = [CCX (0, 1, 2)] }
        let qasm = OpenQasm.export circuit
        Assert.Contains("ccx q[0],q[1],q[2];", qasm)
    
    [<Fact>]
    let ``validate detects CCX with duplicate qubits`` () =
        let circuit = { QubitCount = 3; Gates = [CCX (0, 0, 2)] }
        let result = OpenQasm.validate circuit
        
        match result with
        | Error msg -> Assert.Contains("distinct", msg.ToLower())
        | Ok () -> Assert.True(false, "Expected validation error for duplicate qubits")
    
    [<Fact>]
    let ``validate detects CCX with out of range qubits`` () =
        let circuit = { QubitCount = 3; Gates = [CCX (0, 1, 5)] }
        let result = OpenQasm.validate circuit
        
        match result with
        | Error msg -> Assert.Contains("range", msg.ToLower())
        | Ok () -> Assert.True(false, "Expected validation error")
    
    [<Fact>]
    let ``export circuit with multiple CCX gates`` () =
        let circuit = {
            QubitCount = 4
            Gates = [H 0; CCX (0, 1, 2); CCX (1, 2, 3)]
        }
        let qasm = OpenQasm.export circuit
        
        Assert.Contains("ccx q[0],q[1],q[2];", qasm)
        Assert.Contains("ccx q[1],q[2],q[3];", qasm)
    
    // ========================================================================
    // INTEGRATION TESTS: REALISTIC CIRCUITS
    // ========================================================================
    
    [<Fact>]
    let ``export GHZ state circuit`` () =
        let circuit = {
            QubitCount = 3
            Gates = [
                H 0
                CNOT (0, 1)
                CNOT (1, 2)
            ]
        }
        let qasm = OpenQasm.export circuit
        
        Assert.Contains("qreg q[3];", qasm)
        Assert.Contains("h q[0];", qasm)
        Assert.Contains("cx q[0],q[1];", qasm)
        Assert.Contains("cx q[1],q[2];", qasm)
    
    [<Fact>]
    let ``export QAOA-style circuit with rotations`` () =
        let circuit = {
            QubitCount = 2
            Gates = [
                H 0
                H 1
                RZ (0, 0.5)
                RZ (1, 0.5)
                CNOT (0, 1)
                RX (0, 1.0)
                RX (1, 1.0)
            ]
        }
        let qasm = OpenQasm.export circuit
        
        Assert.Contains("rz(0.5000000000) q[0];", qasm)
        Assert.Contains("rz(0.5000000000) q[1];", qasm)
        Assert.Contains("rx(1.0000000000) q[0];", qasm)
        Assert.Contains("rx(1.0000000000) q[1];", qasm)
    
    [<Fact>]
    let ``export comprehensive circuit with all gate types`` () =
        let circuit = {
            QubitCount = 4
            Gates = [
                // Pauli gates
                X 0; Y 1; Z 2; H 3
                // Phase gates
                S 0; SDG 1; T 2; TDG 3
                // Rotation gates
                RX (0, 1.5707963268); RY (1, 3.1415926536); RZ (2, 0.7853981634)
                // Two-qubit gates
                CNOT (0, 1); CZ (1, 2); SWAP (2, 3)
                // Three-qubit gate
                CCX (0, 1, 2)
            ]
        }
        let qasm = OpenQasm.export circuit
        
        // Verify all gate types are present
        Assert.Contains("x q[0];", qasm)
        Assert.Contains("y q[1];", qasm)
        Assert.Contains("z q[2];", qasm)
        Assert.Contains("h q[3];", qasm)
        Assert.Contains("s q[0];", qasm)
        Assert.Contains("sdg q[1];", qasm)
        Assert.Contains("t q[2];", qasm)
        Assert.Contains("tdg q[3];", qasm)
        Assert.Contains("rx(1.5707963268) q[0];", qasm)
        Assert.Contains("ry(3.1415926536) q[1];", qasm)
        Assert.Contains("rz(0.7853981634) q[2];", qasm)
        Assert.Contains("cx q[0],q[1];", qasm)
        Assert.Contains("cz q[1],q[2];", qasm)
        Assert.Contains("swap q[2],q[3];", qasm)
        Assert.Contains("ccx q[0],q[1],q[2];", qasm)
