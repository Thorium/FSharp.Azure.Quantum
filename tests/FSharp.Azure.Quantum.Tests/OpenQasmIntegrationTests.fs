namespace FSharp.Azure.Quantum.Tests

open Xunit
open FSharp.Azure.Quantum
open FSharp.Azure.Quantum.CircuitBuilder
open System
open System.IO

/// Integration tests for OpenQASM Export, Import, and Transpiler
/// 
/// Tests real-world workflows:
/// - Export → Import → Transpile
/// - Large circuit handling
/// - Algorithm implementations (Grover, QAOA)
/// - Edge cases and performance
module OpenQasmIntegrationTests =
    
    // ========================================================================
    // TRANSPILER + IMPORT/EXPORT INTEGRATION
    // ========================================================================
    
    [<Fact>]
    let ``export, import, then transpile for IonQ`` () =
        // Create circuit with gates that need transpilation
        let original = { QubitCount = 3; Gates = [S 0; T 1; CZ (0, 1); CCX (0, 1, 2)] }
        
        // Export to QASM
        let qasm = OpenQasmExport.export original
        
        // Import back
        let imported = OpenQasmImport.parse qasm
        
        match imported with
        | Ok circuit ->
            // Transpile for IonQ (should decompose S, T, CZ, CCX)
            let transpiled = GateTranspiler.transpileForBackend "ionq.simulator" circuit
            
            // Should have more gates after transpilation
            Assert.True(transpiled.Gates.Length > original.Gates.Length, 
                        $"Expected transpilation to increase gate count. Original: {original.Gates.Length}, Transpiled: {transpiled.Gates.Length}")
            
            // Should only contain IonQ-native gates
            for gate in transpiled.Gates do
                match gate with
                | X _ | Y _ | Z _ | H _ | RX _ | RY _ | RZ _ | CNOT _ | SWAP _ -> ()
                | _ -> Assert.True(false, $"Transpiled circuit contains non-IonQ gate: {gate}")
        | Error msg -> Assert.True(false, $"Import failed: {msg}")
    
    [<Fact>]
    let ``full workflow - build, export, import, transpile, validate`` () =
        // Build Bell state
        let circuit = 
            CircuitBuilder.empty 2
            |> CircuitBuilder.addGate (H 0)
            |> CircuitBuilder.addGate (CNOT (0, 1))
        
        // Export
        let qasm = OpenQasmExport.export circuit
        
        // Validate QASM format
        Assert.Contains("OPENQASM 2.0", qasm)
        Assert.Contains("h q[0]", qasm)
        Assert.Contains("cx q[0],q[1]", qasm)
        
        // Import
        match OpenQasmImport.parse qasm with
        | Ok imported ->
            // Verify equality
            Assert.Equal(circuit.QubitCount, imported.QubitCount)
            Assert.Equal(circuit.Gates.Length, imported.Gates.Length)
            
            // Transpile for Rigetti (no changes needed for Bell state)
            let transpiled = GateTranspiler.transpileForBackend "rigetti.sim.qvm" imported
            
            // Validate transpiled circuit
            let validation = CircuitBuilder.validate transpiled
            if not validation.IsValid then
                let errorMsg = String.concat ", " validation.Messages
                Assert.True(false, $"Validation failed: {errorMsg}")
        | Error msg -> Assert.True(false, $"Import failed: {msg}")
    
    // ========================================================================
    // LARGE CIRCUIT HANDLING
    // ========================================================================
    
    [<Fact>]
    let ``export and import large circuit (100 gates)`` () =
        // Create large circuit
        let gates = 
            [ for i in 0 .. 99 do
                match i % 5 with
                | 0 -> H (i % 10)
                | 1 -> X (i % 10)
                | 2 -> RZ (i % 10, float i * 0.1)
                | 3 -> CNOT (i % 10, (i + 1) % 10)
                | _ -> S (i % 10) ]
        
        let circuit = { QubitCount = 10; Gates = gates }
        
        // Export
        let qasm = OpenQasmExport.export circuit
        
        // Verify QASM contains all gates
        Assert.Contains("qreg q[10]", qasm)
        
        // Import
        match OpenQasmImport.parse qasm with
        | Ok imported ->
            Assert.Equal(circuit.QubitCount, imported.QubitCount)
            Assert.Equal(100, imported.Gates.Length)
        | Error msg -> Assert.True(false, $"Large circuit import failed: {msg}")
    
    [<Fact>]
    let ``export and import circuit with 20 qubits`` () =
        let gates = 
            [ for i in 0 .. 19 do H i
              for i in 0 .. 18 do CNOT (i, i + 1) ]
        
        let circuit = { QubitCount = 20; Gates = gates }
        
        let qasm = OpenQasmExport.export circuit
        
        match OpenQasmImport.parse qasm with
        | Ok imported ->
            Assert.Equal(20, imported.QubitCount)
            Assert.Equal(39, imported.Gates.Length)  // 20 H gates + 19 CNOTs
        | Error msg -> Assert.True(false, $"20-qubit circuit failed: {msg}")
    
    // ========================================================================
    // ALGORITHM IMPLEMENTATIONS
    // ========================================================================
    
    [<Fact>]
    let ``export and import Grover oracle circuit`` () =
        // Simple Grover oracle for 2 qubits targeting |11⟩
        let gates = [
            H 0; H 1                     // Superposition
            CZ (0, 1)                    // Oracle for |11⟩
            H 0; H 1                     // Hadamard
            X 0; X 1                     // X gates
            CZ (0, 1)                    // Diffusion
            X 0; X 1                     // X gates
            H 0; H 1                     // Hadamard
        ]
        
        let circuit = { QubitCount = 2; Gates = gates }
        
        let qasm = OpenQasmExport.export circuit
        
        match OpenQasmImport.parse qasm with
        | Ok imported ->
            Assert.Equal(circuit.QubitCount, imported.QubitCount)
            Assert.Equal(gates.Length, imported.Gates.Length)
        | Error msg -> Assert.True(false, $"Grover circuit failed: {msg}")
    
    [<Fact>]
    let ``export and import GHZ state circuit`` () =
        // GHZ state on 4 qubits: H q[0]; CNOT q[0],q[1]; CNOT q[0],q[2]; CNOT q[0],q[3]
        let circuit = {
            QubitCount = 4
            Gates = [
                H 0
                CNOT (0, 1)
                CNOT (0, 2)
                CNOT (0, 3)
            ]
        }
        
        let qasm = OpenQasmExport.export circuit
        
        match OpenQasmImport.parse qasm with
        | Ok imported ->
            Assert.Equal(4, imported.QubitCount)
            Assert.Equal(4, imported.Gates.Length)
            
            // Verify gate sequence
            match imported.Gates with
            | [H 0; CNOT (0, 1); CNOT (0, 2); CNOT (0, 3)] -> ()
            | _ -> Assert.True(false, "GHZ gate sequence incorrect")
        | Error msg -> Assert.True(false, $"GHZ circuit failed: {msg}")
    
    [<Fact>]
    let ``export and import QAOA-style mixing layer`` () =
        // QAOA mixing layer: RX rotations on all qubits
        let beta = Math.PI / 4.0
        let gates = [ for i in 0 .. 3 do RX (i, beta) ]
        
        let circuit = { QubitCount = 4; Gates = gates }
        
        let qasm = OpenQasmExport.export circuit
        
        match OpenQasmImport.parse qasm with
        | Ok imported ->
            Assert.Equal(4, imported.QubitCount)
            Assert.Equal(4, imported.Gates.Length)
            
            // Check all angles match
            for gate in imported.Gates do
                match gate with
                | RX (_, angle) -> Assert.Equal(beta, angle, 9)
                | _ -> Assert.True(false, "Expected RX gates")
        | Error msg -> Assert.True(false, $"QAOA mixing failed: {msg}")
    
    [<Fact>]
    let ``export and import quantum teleportation circuit`` () =
        // Quantum teleportation circuit
        let circuit = {
            QubitCount = 3
            Gates = [
                // Create Bell pair between q1 and q2
                H 1
                CNOT (1, 2)
                
                // Alice's operations
                CNOT (0, 1)
                H 0
                
                // Bob's corrections (would be conditional in real implementation)
                CNOT (1, 2)
                CZ (0, 2)
            ]
        }
        
        let qasm = OpenQasmExport.export circuit
        
        match OpenQasmImport.parse qasm with
        | Ok imported ->
            Assert.Equal(3, imported.QubitCount)
            Assert.Equal(6, imported.Gates.Length)
        | Error msg -> Assert.True(false, $"Teleportation circuit failed: {msg}")
    
    // ========================================================================
    // EDGE CASES
    // ========================================================================
    
    [<Fact>]
    let ``export and import circuit with extreme rotation angles`` () =
        let gates = [
            RX (0, 0.0)                    // Zero angle
            RY (1, Math.PI * 2.0)          // Full rotation
            RZ (2, Math.PI / 1000.0)       // Tiny angle
            RX (3, Math.PI * 10.0)         // Large angle
            RY (4, -Math.PI)               // Negative angle
        ]
        
        let circuit = { QubitCount = 5; Gates = gates }
        
        let qasm = OpenQasmExport.export circuit
        
        match OpenQasmImport.parse qasm with
        | Ok imported ->
            Assert.Equal(5, imported.Gates.Length)
            
            // Check angle preservation (within floating point precision)
            for i in 0 .. 4 do
                match gates.[i], imported.Gates.[i] with
                | RX (_, a1), RX (_, a2) -> Assert.Equal(a1, a2, 9)
                | RY (_, a1), RY (_, a2) -> Assert.Equal(a1, a2, 9)
                | RZ (_, a1), RZ (_, a2) -> Assert.Equal(a1, a2, 9)
                | _ -> Assert.True(false, "Gate mismatch")
        | Error msg -> Assert.True(false, $"Extreme angles failed: {msg}")
    
    [<Fact>]
    let ``export and import circuit with scientific notation angles`` () =
        let circuit = { 
            QubitCount = 1
            Gates = [RX (0, 1.23e-5); RY (0, 4.56e10)] 
        }
        
        let qasm = OpenQasmExport.export circuit
        
        // Verify scientific notation is handled
        match OpenQasmImport.parse qasm with
        | Ok imported ->
            Assert.Equal(2, imported.Gates.Length)
            match imported.Gates.[0] with
            | RX (0, angle) -> Assert.Equal(1.23e-5, angle, 15)
            | _ -> Assert.True(false, "Expected RX gate")
            match imported.Gates.[1] with
            | RY (0, angle) -> Assert.Equal(4.56e10, angle, 5)
            | _ -> Assert.True(false, "Expected RY gate")
        | Error msg -> Assert.True(false, $"Scientific notation failed: {msg}")
    
    [<Fact>]
    let ``import handles QASM with CRLF line endings`` () =
        let qasm = "OPENQASM 2.0;\r\ninclude \"qelib1.inc\";\r\nqreg q[1];\r\nx q[0];\r\n"
        
        match OpenQasmImport.parse qasm with
        | Ok circuit ->
            Assert.Equal(1, circuit.QubitCount)
            Assert.Equal(1, circuit.Gates.Length)
        | Error msg -> Assert.True(false, $"CRLF handling failed: {msg}")
    
    [<Fact>]
    let ``import handles QASM with mixed line endings`` () =
        let qasm = "OPENQASM 2.0;\ninclude \"qelib1.inc\";\r\nqreg q[1];\nx q[0];\r\n"
        
        match OpenQasmImport.parse qasm with
        | Ok circuit ->
            Assert.Equal(1, circuit.QubitCount)
            Assert.Equal(1, circuit.Gates.Length)
        | Error msg -> Assert.True(false, $"Mixed line endings failed: {msg}")
    
    [<Fact>]
    let ``import handles QASM with trailing whitespace`` () =
        let qasm = """
OPENQASM 2.0;   
include "qelib1.inc";  
qreg q[2];     
h q[0];        
cx q[0],q[1];  
"""
        match OpenQasmImport.parse qasm with
        | Ok circuit ->
            Assert.Equal(2, circuit.QubitCount)
            Assert.Equal(2, circuit.Gates.Length)
        | Error msg -> Assert.True(false, $"Trailing whitespace failed: {msg}")
    
    // ========================================================================
    // FILE I/O INTEGRATION
    // ========================================================================
    
    [<Fact>]
    let ``full file workflow - export to file, import from file, verify`` () =
        let tempFile = Path.GetTempFileName() + ".qasm"
        
        try
            // Create circuit
            let circuit = { 
                QubitCount = 3
                Gates = [H 0; S 1; T 2; CNOT (0, 1); CZ (1, 2); CCX (0, 1, 2)]
            }
            
            // Export to file
            OpenQasmExport.exportToFile circuit tempFile
            
            // Verify file exists
            Assert.True(File.Exists(tempFile), "Export should create file")
            
            // Import from file
            match OpenQasmImport.parseFromFile tempFile with
            | Ok imported ->
                Assert.Equal(circuit.QubitCount, imported.QubitCount)
                Assert.Equal(circuit.Gates.Length, imported.Gates.Length)
                Assert.Equal<Gate list>(circuit.Gates, imported.Gates)
            | Error msg -> Assert.True(false, $"File import failed: {msg}")
        finally
            if File.Exists(tempFile) then File.Delete(tempFile)
    
    [<Fact>]
    let ``export, import, modify, re-export workflow`` () =
        // Original circuit
        let original = { QubitCount = 2; Gates = [H 0] }
        
        // Export
        let qasm1 = OpenQasmExport.export original
        
        // Import
        match OpenQasmImport.parse qasm1 with
        | Ok imported ->
            // Modify by adding gate
            let modified = CircuitBuilder.addGate (CNOT (0, 1)) imported
            
            // Re-export
            let qasm2 = OpenQasmExport.export modified
            
            // Verify modification
            Assert.Contains("h q[0]", qasm2)
            Assert.Contains("cx q[0],q[1]", qasm2)
            
            // Re-import to verify round-trip
            match OpenQasmImport.parse qasm2 with
            | Ok final ->
                Assert.Equal(2, final.Gates.Length)
            | Error msg -> Assert.True(false, $"Re-import failed: {msg}")
        | Error msg -> Assert.True(false, $"Import failed: {msg}")
    
    // ========================================================================
    // TRANSPILER INTEGRATION
    // ========================================================================
    
    [<Fact>]
    let ``transpile, export, import preserves gate count`` () =
        // Circuit with gates needing transpilation
        let original = { QubitCount = 2; Gates = [S 0; T 1; CZ (0, 1)] }
        
        // Transpile for IonQ
        let transpiled = GateTranspiler.transpileForBackend "ionq.simulator" original
        
        // Export transpiled circuit
        let qasm = OpenQasmExport.export transpiled
        
        // Import back
        match OpenQasmImport.parse qasm with
        | Ok imported ->
            // Should have same gate count as transpiled version
            Assert.Equal(transpiled.Gates.Length, imported.Gates.Length)
            
            // Should only have IonQ-native gates
            for gate in imported.Gates do
                match gate with
                | RZ _ | H _ | CNOT _ -> ()  // Expected IonQ gates
                | _ -> Assert.True(false, $"Unexpected gate in imported circuit: {gate}")
        | Error msg -> Assert.True(false, $"Import after transpile failed: {msg}")
    
    [<Fact>]
    let ``transpiler statistics match after round-trip`` () =
        let original = { QubitCount = 3; Gates = [S 0; T 1; CCX (0, 1, 2)] }
        
        // Get transpilation stats for original
        let ionqConstraints = Core.CircuitValidator.BackendConstraints.ionqSimulator()
        
        let (origCount, transpCount, decomposed) = 
            GateTranspiler.getTranspilationStats ionqConstraints original
        
        // Export and import original
        let qasm = OpenQasmExport.export original
        match OpenQasmImport.parse qasm with
        | Ok imported ->
            // Get transpilation stats for imported
            let (impOrigCount, impTranspCount, impDecomposed) = 
                GateTranspiler.getTranspilationStats ionqConstraints imported
            
            // Stats should match
            Assert.Equal(origCount, impOrigCount)
            Assert.Equal(transpCount, impTranspCount)
            Assert.Equal(decomposed, impDecomposed)
        | Error msg -> Assert.True(false, $"Import failed: {msg}")
    
    // ========================================================================
    // VALIDATION INTEGRATION
    // ========================================================================
    
    [<Fact>]
    let ``imported circuit passes validation`` () =
        let qasm = """
OPENQASM 2.0;
include "qelib1.inc";
qreg q[3];
h q[0];
cx q[0],q[1];
cx q[0],q[2];
"""
        match OpenQasmImport.parse qasm with
        | Ok circuit ->
            let validation = CircuitBuilder.validate circuit
            if not validation.IsValid then
                let errorMsg = String.concat ", " validation.Messages
                Assert.True(false, $"Imported circuit failed validation: {errorMsg}")
        | Error msg -> Assert.True(false, $"Import failed: {msg}")
    
    [<Fact>]
    let ``exported then imported circuit maintains optimization opportunities`` () =
        // Create circuit with consecutive inverse gates
        let circuit = {
            QubitCount = 1
            Gates = [H 0; H 0; X 0; X 0; S 0; SDG 0]  // Should optimize to empty
        }
        
        // Export and import
        let qasm = OpenQasmExport.export circuit
        match OpenQasmImport.parse qasm with
        | Ok imported ->
            // Optimize
            let optimized = CircuitBuilder.optimize imported
            
            // Should have removed inverse pairs
            Assert.True(optimized.Gates.Length < circuit.Gates.Length,
                        $"Optimization should reduce gates. Original: {circuit.Gates.Length}, Optimized: {optimized.Gates.Length}")
        | Error msg -> Assert.True(false, $"Import failed: {msg}")
    
    // ========================================================================
    // PERFORMANCE AND STRESS TESTS
    // ========================================================================
    
    [<Fact>]
    let ``export and import 1000 gate circuit completes quickly`` () =
        // Create large circuit
        let gates = [ for i in 0 .. 999 do H (i % 10) ]
        let circuit = { QubitCount = 10; Gates = gates }
        
        // Measure export time
        let sw = System.Diagnostics.Stopwatch.StartNew()
        let qasm = OpenQasmExport.export circuit
        sw.Stop()
        Assert.True(sw.ElapsedMilliseconds < 100, $"Export took {sw.ElapsedMilliseconds}ms (expected <100ms)")
        
        // Measure import time
        sw.Restart()
        match OpenQasmImport.parse qasm with
        | Ok imported ->
            sw.Stop()
            Assert.True(sw.ElapsedMilliseconds < 200, $"Import took {sw.ElapsedMilliseconds}ms (expected <200ms)")
            Assert.Equal(1000, imported.Gates.Length)
        | Error msg -> Assert.True(false, $"Large circuit import failed: {msg}")
    
    [<Fact>]
    let ``multiple round-trips maintain circuit integrity`` () =
        let original = { 
            QubitCount = 3
            Gates = [
                H 0; RX (1, Math.PI / 3.0); CNOT (0, 1)
                S 2; CZ (1, 2); T 0
            ]
        }
        
        // Perform 5 round-trips
        let mutable current = original
        for _ in 1 .. 5 do
            let qasm = OpenQasmExport.export current
            match OpenQasmImport.parse qasm with
            | Ok imported -> current <- imported
            | Error msg -> Assert.True(false, $"Round-trip failed: {msg}")
        
        // After 5 round-trips, should still match original
        Assert.Equal(original.QubitCount, current.QubitCount)
        Assert.Equal(original.Gates.Length, current.Gates.Length)
