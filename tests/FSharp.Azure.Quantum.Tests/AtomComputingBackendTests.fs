namespace FSharp.Azure.Quantum.Tests

open System
open System.Net
open System.Net.Http
open System.Text.Json
open Xunit
open FSharp.Azure.Quantum.Core.AtomComputingBackend
open FSharp.Azure.Quantum.Core.Types
open FSharp.Azure.Quantum.Core
open FSharp.Azure.Quantum

module AtomComputingBackendTests =
    
    // ============================================================================
    // TDD CYCLE 1: Circuit Conversion to OpenQASM 2.0
    // Note: Atom Computing uses OpenQasmExport.export() - these tests verify
    // that CircuitBuilder circuits convert correctly for Atom Computing
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
    let ``OpenQasmExport - CZ gate (native Atom Computing)`` () =
        // Arrange
        let circuit = 
            CircuitBuilder.empty 2
            |> CircuitBuilder.addGate (CircuitBuilder.CZ (0, 1))
        
        // Act
        let qasm = OpenQasmExport.export circuit
        
        // Assert
        Assert.Contains("cz q[0],q[1];", qasm)
    
    [<Fact>]
    let ``OpenQasmExport - Rotation gates (RX, RY, RZ)`` () =
        // Arrange
        let circuit = 
            CircuitBuilder.empty 1
            |> CircuitBuilder.addGate (CircuitBuilder.RX (0, Math.PI / 4.0))
            |> CircuitBuilder.addGate (CircuitBuilder.RY (0, Math.PI / 2.0))
            |> CircuitBuilder.addGate (CircuitBuilder.RZ (0, Math.PI))
        
        // Act
        let qasm = OpenQasmExport.export circuit
        
        // Assert
        Assert.Contains("rx(", qasm)
        Assert.Contains("ry(", qasm)
        Assert.Contains("rz(", qasm)
    
    // ============================================================================
    // TDD CYCLE 2: Job Submission Creation
    // ============================================================================
    
    [<Fact>]
    let ``createJobSubmission - creates valid job submission for simulator`` () =
        // Arrange
        let qasmCode = "OPENQASM 2.0;\ninclude \"qelib1.inc\";\nqreg q[2];\nh q[0];\ncz q[0],q[1];\n"
        let shots = 1000
        let target = "atom-computing.sim"
        
        // Act
        let submission = createJobSubmission qasmCode shots target
        
        // Assert
        Assert.Equal(target, submission.Target)
        Assert.Equal(shots, submission.InputParams.["shots"] :?> int)
        Assert.Equal(CircuitFormat.Custom "qasm.v2", submission.InputDataFormat)
        Assert.True(submission.Name.IsSome)
        Assert.Contains("AtomComputing", submission.Name.Value)
    
    [<Fact>]
    let ``createJobSubmission - creates valid job submission for Phoenix QPU`` () =
        // Arrange
        let qasmCode = "OPENQASM 2.0;\nqreg q[3];\nh q[0];\ncz q[0],q[1];\ncz q[1],q[2];\n"
        let shots = 500
        let target = "atom-computing.qpu.phoenix"
        
        // Act
        let submission = createJobSubmission qasmCode shots target
        
        // Assert
        Assert.Equal(target, submission.Target)
        Assert.Equal(shots, submission.InputParams.["shots"] :?> int)
        Assert.Contains("phoenix", target.ToLowerInvariant())
    
    // ============================================================================
    // TDD CYCLE 3: Result Parsing
    // ============================================================================
    
    [<Fact>]
    let ``parseAtomComputingResult - parses histogram with 'results' key`` () =
        // Arrange
        let jsonResult = """{"results": {"00": 48, "11": 52}}"""
        
        // Act
        let histogram = parseAtomComputingResult jsonResult
        
        // Assert
        Assert.Equal(2, histogram.Count)
        Assert.Equal(48, histogram.["00"])
        Assert.Equal(52, histogram.["11"])
    
    [<Fact>]
    let ``parseAtomComputingResult - parses histogram with 'measurements' key`` () =
        // Arrange
        let jsonResult = """{"measurements": {"000": 250, "111": 750}}"""
        
        // Act
        let histogram = parseAtomComputingResult jsonResult
        
        // Assert
        Assert.Equal(2, histogram.Count)
        Assert.Equal(250, histogram.["000"])
        Assert.Equal(750, histogram.["111"])
    
    [<Fact>]
    let ``parseAtomComputingResult - parses histogram with root-level data`` () =
        // Arrange
        let jsonResult = """{"00": 10, "01": 20, "10": 30, "11": 40}"""
        
        // Act
        let histogram = parseAtomComputingResult jsonResult
        
        // Assert
        Assert.Equal(4, histogram.Count)
        Assert.Equal(10, histogram.["00"])
        Assert.Equal(20, histogram.["01"])
        Assert.Equal(30, histogram.["10"])
        Assert.Equal(40, histogram.["11"])
    
    [<Fact>]
    let ``parseAtomComputingResult - handles large qubit counts`` () =
        // Arrange - 5-qubit circuit result
        let jsonResult = """{"results": {"00000": 512, "11111": 512}}"""
        
        // Act
        let histogram = parseAtomComputingResult jsonResult
        
        // Assert
        Assert.Equal(2, histogram.Count)
        Assert.Equal(512, histogram.["00000"])
        Assert.Equal(512, histogram.["11111"])
    
    // ============================================================================
    // TDD CYCLE 4: Error Mapping
    // ============================================================================
    
    [<Fact>]
    let ``mapAtomComputingError - maps InvalidCircuit error`` () =
        // Arrange
        let errorCode = "InvalidCircuit"
        let errorMessage = "Unsupported gate: TOFFOLI"
        
        // Act
        let quantumError = mapAtomComputingError errorCode errorMessage
        
        // Assert
        match quantumError with
        | QuantumError.ValidationError(field, reason) ->
            Assert.Contains(errorMessage, reason)
        | _ -> Assert.True(false, $"Expected ValidationError, got {quantumError}")
    
    [<Fact>]
    let ``mapAtomComputingError - maps TooManyQubits error`` () =
        // Arrange
        let errorCode = "TooManyQubits"
        let errorMessage = "Circuit requires 200 qubits, max is 100"
        
        // Act
        let quantumError = mapAtomComputingError errorCode errorMessage
        
        // Assert
        match quantumError with
        | QuantumError.ValidationError(field, reason) ->
            Assert.Contains("Circuit too large", reason)
        | _ -> Assert.True(false, $"Expected ValidationError, got {quantumError}")
    
    [<Fact>]
    let ``mapAtomComputingError - maps QuotaExceeded error`` () =
        // Arrange
        let errorCode = "QuotaExceeded"
        let errorMessage = "Insufficient credits"
        
        // Act
        let quantumError = mapAtomComputingError errorCode errorMessage
        
        // Assert
        match quantumError with
        | QuantumError.AzureError(AzureQuantumError.QuotaExceeded msg) ->
            Assert.Equal(errorMessage, msg)
        | _ -> Assert.True(false, $"Expected QuotaExceeded, got {quantumError}")
    
    [<Fact>]
    let ``mapAtomComputingError - maps BackendUnavailable error`` () =
        // Arrange
        let errorCode = "BackendUnavailable"
        let errorMessage = "System under maintenance"
        
        // Act
        let quantumError = mapAtomComputingError errorCode errorMessage
        
        // Assert
        match quantumError with
        | QuantumError.AzureError(AzureQuantumError.ServiceUnavailable retryAfter) ->
            Assert.True(retryAfter.IsSome)
            Assert.Equal(TimeSpan.FromMinutes(5.0), retryAfter.Value)
        | _ -> Assert.True(false, $"Expected ServiceUnavailable, got {quantumError}")
    
    [<Fact>]
    let ``mapAtomComputingError - maps unknown error`` () =
        // Arrange
        let errorCode = "UnknownError"
        let errorMessage = "Something went wrong"
        
        // Act
        let quantumError = mapAtomComputingError errorCode errorMessage
        
        // Assert
        match quantumError with
        | QuantumError.AzureError(AzureQuantumError.UnknownError (code, msg)) ->
            Assert.Equal(0, code)
            Assert.Contains("Atom Computing error", msg)
            Assert.Contains(errorCode, msg)
            Assert.Contains(errorMessage, msg)
        | _ -> Assert.True(false, $"Expected UnknownError, got {quantumError}")
