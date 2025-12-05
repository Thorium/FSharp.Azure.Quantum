namespace FSharp.Azure.Quantum.Tests

open System
open System.Net
open System.Net.Http
open System.Text.Json
open Xunit
open FSharp.Azure.Quantum.Core.QuantinuumBackend
open FSharp.Azure.Quantum.Core.Types
open FSharp.Azure.Quantum

module QuantinuumBackendTests =
    
    // ============================================================================
    // TDD CYCLE 1: Circuit Conversion to OpenQASM 2.0
    // Note: Quantinuum uses OpenQasmExport.export() - these tests verify
    // that CircuitBuilder circuits convert correctly for Quantinuum
    // ============================================================================
    
    [<Fact>]
    let ``OpenQasmExport - simple H gate circuit produces valid OpenQASM 2.0`` () =
        // Arrange
        let circuit = 
            CircuitBuilder.empty 1
            |> CircuitBuilder.addGate (CircuitBuilder.H 0)
        
        // Act
        let qasm = OpenQasmExport.export circuit
        
        // Assert
        Assert.Contains("OPENQASM 2.0;", qasm)
        Assert.Contains("include \"qelib1.inc\";", qasm)
        Assert.Contains("qreg q[1];", qasm)
        Assert.Contains("h q[0];", qasm)
    
    [<Fact>]
    let ``OpenQasmExport - Bell state circuit with H and CNOT`` () =
        // Arrange
        let circuit = 
            CircuitBuilder.empty 2
            |> CircuitBuilder.addGate (CircuitBuilder.H 0)
            |> CircuitBuilder.addGate (CircuitBuilder.CNOT (0, 1))
        
        // Act
        let qasm = OpenQasmExport.export circuit
        
        // Assert
        Assert.Contains("OPENQASM 2.0;", qasm)
        Assert.Contains("qreg q[2];", qasm)
        Assert.Contains("h q[0];", qasm)
        Assert.Contains("cx q[0],q[1];", qasm)
    
    [<Fact>]
    let ``OpenQasmExport - circuit with rotation gates`` () =
        // Arrange
        let circuit = 
            CircuitBuilder.empty 2
            |> CircuitBuilder.addGate (CircuitBuilder.RX (0, Math.PI / 2.0))
            |> CircuitBuilder.addGate (CircuitBuilder.RY (1, Math.PI / 4.0))
            |> CircuitBuilder.addGate (CircuitBuilder.RZ (0, Math.PI))
        
        // Act
        let qasm = OpenQasmExport.export circuit
        
        // Assert
        Assert.Contains("rx(", qasm)
        Assert.Contains("ry(", qasm)
        Assert.Contains("rz(", qasm)
    
    [<Fact>]
    let ``OpenQasmExport - circuit with CZ gate (Quantinuum native)`` () =
        // Arrange
        let circuit = 
            CircuitBuilder.empty 2
            |> CircuitBuilder.addGate (CircuitBuilder.H 0)
            |> CircuitBuilder.addGate (CircuitBuilder.CZ (0, 1))
        
        // Act
        let qasm = OpenQasmExport.export circuit
        
        // Assert
        Assert.Contains("h q[0];", qasm)
        Assert.Contains("cz q[0],q[1];", qasm)
    
    // ============================================================================
    // TDD CYCLE 2: Job Submission
    // ============================================================================
    
    [<Fact>]
    let ``createJobSubmission - creates valid JobSubmission with OpenQASM string`` () =
        // Arrange
        let qasmCode = "OPENQASM 2.0;\ninclude \"qelib1.inc\";\nqreg q[2];\nh q[0];\ncx q[0],q[1];"
        let shots = 100
        let target = "quantinuum.sim.h1-1sc"
        
        // Act
        let submission = createJobSubmission qasmCode shots target
        
        // Assert
        Assert.Equal(target, submission.Target)
        Assert.Equal(CircuitFormat.Custom "qasm.v2", submission.InputDataFormat)
        Assert.True(submission.InputParams.ContainsKey("shots"))
        Assert.Equal(100, submission.InputParams.["shots"] :?> int)
        Assert.IsType<string>(submission.InputData) |> ignore
        Assert.Equal(qasmCode, submission.InputData :?> string)
    
    // ============================================================================
    // TDD CYCLE 3: Result Parsing
    // ============================================================================
    
    [<Fact>]
    let ``parseQuantinuumResult - parses histogram from Quantinuum result JSON`` () =
        // Arrange
        let resultJson = """{"results": {"00": 48, "11": 52}}"""
        
        // Act
        let histogram = parseQuantinuumResult resultJson
        
        // Assert
        Assert.Equal(2, histogram.Count)
        Assert.Equal(48, histogram.["00"])
        Assert.Equal(52, histogram.["11"])
    
    [<Fact>]
    let ``parseQuantinuumResult - handles single bitstring result`` () =
        // Arrange
        let resultJson = """{"results": {"000": 100}}"""
        
        // Act
        let histogram = parseQuantinuumResult resultJson
        
        // Assert
        Assert.Equal(1, histogram.Count)
        Assert.Equal(100, histogram.["000"])
    
    [<Fact>]
    let ``parseQuantinuumResult - handles complex histogram`` () =
        // Arrange
        let resultJson = """{"results": {"00": 25, "01": 25, "10": 25, "11": 25}}"""
        
        // Act
        let histogram = parseQuantinuumResult resultJson
        
        // Assert
        Assert.Equal(4, histogram.Count)
        Assert.Equal(25, histogram.["00"])
        Assert.Equal(25, histogram.["01"])
        Assert.Equal(25, histogram.["10"])
        Assert.Equal(25, histogram.["11"])
    
    // ============================================================================
    // TDD CYCLE 4: Error Mapping
    // ============================================================================
    
    [<Fact>]
    let ``mapQuantinuumError - InvalidCircuit maps to QuantumError InvalidCircuit`` () =
        // Arrange
        let errorCode = "InvalidCircuit"
        let errorMessage = "Unsupported gate X on qubit 100"
        
        // Act
        let quantumError = mapQuantinuumError errorCode errorMessage
        
        // Assert
        match quantumError with
        | QuantumError.InvalidCircuit messages ->
            Assert.Contains(errorMessage, messages)
        | _ -> Assert.True(false, "Expected InvalidCircuit error")
    
    [<Fact>]
    let ``mapQuantinuumError - TooManyQubits maps to InvalidCircuit`` () =
        // Arrange
        let errorCode = "TooManyQubits"
        let errorMessage = "Circuit requires 40 qubits but H1-1SC supports max 32"
        
        // Act
        let quantumError = mapQuantinuumError errorCode errorMessage
        
        // Assert
        match quantumError with
        | QuantumError.InvalidCircuit messages ->
            Assert.True(messages |> List.exists (fun m -> m.Contains("too large")))
        | _ -> Assert.True(false, "Expected InvalidCircuit error")
    
    [<Fact>]
    let ``mapQuantinuumError - QuotaExceeded maps to QuantumError QuotaExceeded`` () =
        // Arrange
        let errorCode = "QuotaExceeded"
        let errorMessage = "Insufficient HQC credits"
        
        // Act
        let quantumError = mapQuantinuumError errorCode errorMessage
        
        // Assert
        match quantumError with
        | QuantumError.QuotaExceeded msg ->
            Assert.Equal("Insufficient HQC credits", msg)
        | _ -> Assert.True(false, "Expected QuotaExceeded error")
    
    [<Fact>]
    let ``mapQuantinuumError - BackendUnavailable maps to ServiceUnavailable`` () =
        // Arrange
        let errorCode = "BackendUnavailable"
        let errorMessage = "H1-1 hardware in maintenance"
        
        // Act
        let quantumError = mapQuantinuumError errorCode errorMessage
        
        // Assert
        match quantumError with
        | QuantumError.ServiceUnavailable retryAfter ->
            Assert.True(retryAfter.IsSome)
            Assert.True(retryAfter.Value.TotalMinutes >= 5.0)
        | _ -> Assert.True(false, "Expected ServiceUnavailable error")
    
    [<Fact>]
    let ``mapQuantinuumError - unknown error code maps to UnknownError`` () =
        // Arrange
        let errorCode = "WeirdError"
        let errorMessage = "Something strange happened"
        
        // Act
        let quantumError = mapQuantinuumError errorCode errorMessage
        
        // Assert
        match quantumError with
        | QuantumError.UnknownError (code, msg) ->
            Assert.Contains("Quantinuum error", msg)
            Assert.Contains("WeirdError", msg)
        | _ -> Assert.True(false, "Expected UnknownError")
