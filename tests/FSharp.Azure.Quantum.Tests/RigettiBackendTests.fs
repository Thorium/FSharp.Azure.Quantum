namespace FSharp.Azure.Quantum.Tests

open System
open System.Net
open System.Net.Http
open System.Text
open System.Text.Json
open Xunit
open FSharp.Azure.Quantum.Core.RigettiBackend
open FSharp.Azure.Quantum.Core.Types

module RigettiBackendTests =
    
    // ============================================================================
    // TDD CYCLE 1: Quil Type Definitions
    // ============================================================================
    
    [<Fact>]
    let ``QuilGate - SingleQubit gate has gate name and qubit`` () =
        // Arrange & Act
        let gate = QuilGate.SingleQubit("H", 0)
        
        // Assert
        match gate with
        | QuilGate.SingleQubit(gateName, qubit) ->
            Assert.Equal("H", gateName)
            Assert.Equal(0, qubit)
        | _ -> Assert.True(false, "Expected SingleQubit gate")
    
    [<Fact>]
    let ``QuilGate - SingleQubitRotation has gate name, angle, and qubit`` () =
        // Arrange & Act
        let gate = QuilGate.SingleQubitRotation("RX", Math.PI / 2.0, 0)
        
        // Assert
        match gate with
        | QuilGate.SingleQubitRotation(gateName, angle, qubit) ->
            Assert.Equal("RX", gateName)
            Assert.Equal(Math.PI / 2.0, angle, 6)
            Assert.Equal(0, qubit)
        | _ -> Assert.True(false, "Expected SingleQubitRotation gate")
    
    [<Fact>]
    let ``QuilGate - TwoQubit gate has gate name, control, and target`` () =
        // Arrange & Act
        let gate = QuilGate.TwoQubit("CZ", 0, 1)
        
        // Assert
        match gate with
        | QuilGate.TwoQubit(gateName, control, target) ->
            Assert.Equal("CZ", gateName)
            Assert.Equal(0, control)
            Assert.Equal(1, target)
        | _ -> Assert.True(false, "Expected TwoQubit gate")
    
    [<Fact>]
    let ``QuilGate - Measure has qubit and memory reference`` () =
        // Arrange & Act
        let gate = QuilGate.Measure(0, "ro[0]")
        
        // Assert
        match gate with
        | QuilGate.Measure(qubit, memoryRef) ->
            Assert.Equal(0, qubit)
            Assert.Equal("ro[0]", memoryRef)
        | _ -> Assert.True(false, "Expected Measure gate")
    
    [<Fact>]
    let ``QuilGate - DeclareMemory has name, type, and size`` () =
        // Arrange & Act
        let gate = QuilGate.DeclareMemory("ro", "BIT", 2)
        
        // Assert
        match gate with
        | QuilGate.DeclareMemory(name, typ, size) ->
            Assert.Equal("ro", name)
            Assert.Equal("BIT", typ)
            Assert.Equal(2, size)
        | _ -> Assert.True(false, "Expected DeclareMemory gate")
    
    [<Fact>]
    let ``QuilProgram - contains declarations and instructions`` () =
        // Arrange
        let declarations = [
            QuilGate.DeclareMemory("ro", "BIT", 2)
        ]
        let instructions = [
            QuilGate.SingleQubit("H", 0)
            QuilGate.TwoQubit("CZ", 0, 1)
            QuilGate.Measure(0, "ro[0]")
            QuilGate.Measure(1, "ro[1]")
        ]
        
        // Act
        let program = { Declarations = declarations; Instructions = instructions }
        
        // Assert
        Assert.Single(program.Declarations) |> ignore
        Assert.Equal(4, program.Instructions.Length)
    
    [<Fact>]
    let ``ConnectivityGraph - contains set of edges`` () =
        // Arrange
        let edges = Set.ofList [(0, 1); (1, 2); (1, 3)]
        
        // Act
        let graph = { Edges = edges }
        
        // Assert
        Assert.Equal(3, graph.Edges.Count)
        Assert.True(graph.Edges.Contains (0, 1))
        Assert.True(graph.Edges.Contains (1, 2))
        Assert.True(graph.Edges.Contains (1, 3))
    
    // ============================================================================
    // TDD CYCLE 2: Gate Serialization to Quil Assembly
    // ============================================================================
    
    [<Fact>]
    let ``serializeGate - SingleQubit gate produces 'GATE qubit' format`` () =
        // Arrange
        let gate = QuilGate.SingleQubit("H", 0)
        
        // Act
        let quil = serializeGate gate
        
        // Assert
        Assert.Equal("H 0", quil)
    
    [<Fact>]
    let ``serializeGate - SingleQubit gate on different qubit`` () =
        // Arrange
        let gate = QuilGate.SingleQubit("X", 2)
        
        // Act
        let quil = serializeGate gate
        
        // Assert
        Assert.Equal("X 2", quil)
    
    [<Fact>]
    let ``serializeGate - SingleQubitRotation produces 'GATE(angle) qubit' format`` () =
        // Arrange
        let gate = QuilGate.SingleQubitRotation("RX", Math.PI / 2.0, 0)
        
        // Act
        let quil = serializeGate gate
        
        // Assert
        Assert.Equal("RX(1.5707963267948966) 0", quil)
    
    [<Fact>]
    let ``serializeGate - SingleQubitRotation with RY gate`` () =
        // Arrange
        let gate = QuilGate.SingleQubitRotation("RY", Math.PI, 1)
        
        // Act
        let quil = serializeGate gate
        
        // Assert
        Assert.Equal("RY(3.141592653589793) 1", quil)
    
    [<Fact>]
    let ``serializeGate - SingleQubitRotation with RZ gate`` () =
        // Arrange
        let gate = QuilGate.SingleQubitRotation("RZ", Math.PI / 4.0, 2)
        
        // Act
        let quil = serializeGate gate
        
        // Assert
        Assert.Equal("RZ(0.7853981633974483) 2", quil)
    
    [<Fact>]
    let ``serializeGate - TwoQubit gate produces 'GATE control target' format`` () =
        // Arrange
        let gate = QuilGate.TwoQubit("CZ", 0, 1)
        
        // Act
        let quil = serializeGate gate
        
        // Assert
        Assert.Equal("CZ 0 1", quil)
    
    [<Fact>]
    let ``serializeGate - TwoQubit gate with different qubits`` () =
        // Arrange
        let gate = QuilGate.TwoQubit("CZ", 2, 3)
        
        // Act
        let quil = serializeGate gate
        
        // Assert
        Assert.Equal("CZ 2 3", quil)
    
    [<Fact>]
    let ``serializeGate - Measure produces 'MEASURE qubit memoryRef' format`` () =
        // Arrange
        let gate = QuilGate.Measure(0, "ro[0]")
        
        // Act
        let quil = serializeGate gate
        
        // Assert
        Assert.Equal("MEASURE 0 ro[0]", quil)
    
    [<Fact>]
    let ``serializeGate - Measure with different qubit and memory reference`` () =
        // Arrange
        let gate = QuilGate.Measure(2, "ro[2]")
        
        // Act
        let quil = serializeGate gate
        
        // Assert
        Assert.Equal("MEASURE 2 ro[2]", quil)
    
    [<Fact>]
    let ``serializeGate - DeclareMemory produces 'DECLARE name type[size]' format`` () =
        // Arrange
        let gate = QuilGate.DeclareMemory("ro", "BIT", 2)
        
        // Act
        let quil = serializeGate gate
        
        // Assert
        Assert.Equal("DECLARE ro BIT[2]", quil)
    
    [<Fact>]
    let ``serializeGate - DeclareMemory with different size`` () =
        // Arrange
        let gate = QuilGate.DeclareMemory("ro", "BIT", 5)
        
        // Act
        let quil = serializeGate gate
        
        // Assert
        Assert.Equal("DECLARE ro BIT[5]", quil)
    
    // ============================================================================
    // TDD CYCLE 3: Program Serialization to Complete Quil Assembly
    // ============================================================================
    
    [<Fact>]
    let ``serializeProgram - Bell state produces correct Quil assembly`` () =
        // Arrange
        let program = {
            Declarations = [ QuilGate.DeclareMemory("ro", "BIT", 2) ]
            Instructions = [
                QuilGate.SingleQubit("H", 0)
                QuilGate.TwoQubit("CZ", 0, 1)
                QuilGate.Measure(0, "ro[0]")
                QuilGate.Measure(1, "ro[1]")
            ]
        }
        
        // Act
        let quil = serializeProgram program
        
        // Assert
        let expected = "DECLARE ro BIT[2]\nH 0\nCZ 0 1\nMEASURE 0 ro[0]\nMEASURE 1 ro[1]"
        Assert.Equal(expected, quil)
    
    [<Fact>]
    let ``serializeProgram - GHZ state with 3 qubits`` () =
        // Arrange
        let program = {
            Declarations = [ QuilGate.DeclareMemory("ro", "BIT", 3) ]
            Instructions = [
                QuilGate.SingleQubit("H", 0)
                QuilGate.TwoQubit("CZ", 0, 1)
                QuilGate.TwoQubit("CZ", 0, 2)
                QuilGate.Measure(0, "ro[0]")
                QuilGate.Measure(1, "ro[1]")
                QuilGate.Measure(2, "ro[2]")
            ]
        }
        
        // Act
        let quil = serializeProgram program
        
        // Assert
        let expected = "DECLARE ro BIT[3]\nH 0\nCZ 0 1\nCZ 0 2\nMEASURE 0 ro[0]\nMEASURE 1 ro[1]\nMEASURE 2 ro[2]"
        Assert.Equal(expected, quil)
    
    [<Fact>]
    let ``serializeProgram - parameterized rotation circuit`` () =
        // Arrange
        let program = {
            Declarations = [ QuilGate.DeclareMemory("ro", "BIT", 1) ]
            Instructions = [
                QuilGate.SingleQubitRotation("RX", Math.PI / 2.0, 0)
                QuilGate.SingleQubitRotation("RY", Math.PI / 4.0, 0)
                QuilGate.SingleQubitRotation("RZ", Math.PI / 8.0, 0)
                QuilGate.Measure(0, "ro[0]")
            ]
        }
        
        // Act
        let quil = serializeProgram program
        
        // Assert
        Assert.Contains("DECLARE ro BIT[1]", quil)
        Assert.Contains("RX(1.5707963267948966) 0", quil)
        Assert.Contains("RY(0.7853981633974483) 0", quil)
        Assert.Contains("RZ(0.39269908169872414) 0", quil)
        Assert.Contains("MEASURE 0 ro[0]", quil)
    
    [<Fact>]
    let ``serializeProgram - empty program produces only declarations`` () =
        // Arrange
        let program = {
            Declarations = [ QuilGate.DeclareMemory("ro", "BIT", 2) ]
            Instructions = []
        }
        
        // Act
        let quil = serializeProgram program
        
        // Assert
        Assert.Equal("DECLARE ro BIT[2]", quil)
    
    [<Fact>]
    let ``serializeProgram - program without declarations`` () =
        // Arrange
        let program = {
            Declarations = []
            Instructions = [
                QuilGate.SingleQubit("H", 0)
                QuilGate.SingleQubit("X", 1)
            ]
        }
        
        // Act
        let quil = serializeProgram program
        
        // Assert
        Assert.Equal("H 0\nX 1", quil)
    
    // ============================================================================
    // TDD CYCLE 4: Connectivity Graph Validation
    // ============================================================================
    
    [<Fact>]
    let ``isValidGate - single-qubit gate always valid (no connectivity check)`` () =
        // Arrange
        let graph = { Edges = Set.ofList [(0, 1); (1, 2)] }
        let gate = QuilGate.SingleQubit("H", 0)
        
        // Act
        let isValid = isValidGate graph gate
        
        // Assert
        Assert.True(isValid)
    
    [<Fact>]
    let ``isValidGate - single-qubit rotation always valid`` () =
        // Arrange
        let graph = { Edges = Set.ofList [(0, 1); (1, 2)] }
        let gate = QuilGate.SingleQubitRotation("RX", Math.PI, 5)
        
        // Act
        let isValid = isValidGate graph gate
        
        // Assert
        Assert.True(isValid)
    
    [<Fact>]
    let ``isValidGate - measurement always valid`` () =
        // Arrange
        let graph = { Edges = Set.ofList [(0, 1); (1, 2)] }
        let gate = QuilGate.Measure(10, "ro[10]")
        
        // Act
        let isValid = isValidGate graph gate
        
        // Assert
        Assert.True(isValid)
    
    [<Fact>]
    let ``isValidGate - declaration always valid`` () =
        // Arrange
        let graph = { Edges = Set.ofList [(0, 1); (1, 2)] }
        let gate = QuilGate.DeclareMemory("ro", "BIT", 100)
        
        // Act
        let isValid = isValidGate graph gate
        
        // Assert
        Assert.True(isValid)
    
    [<Fact>]
    let ``isValidGate - two-qubit gate valid if edge exists`` () =
        // Arrange
        let graph = { Edges = Set.ofList [(0, 1); (1, 2); (2, 3)] }
        let gate = QuilGate.TwoQubit("CZ", 1, 2)
        
        // Act
        let isValid = isValidGate graph gate
        
        // Assert
        Assert.True(isValid)
    
    [<Fact>]
    let ``isValidGate - two-qubit gate valid if edge exists (reversed)`` () =
        // Arrange - edges are bidirectional
        let graph = { Edges = Set.ofList [(0, 1); (1, 2); (2, 3)] }
        let gate = QuilGate.TwoQubit("CZ", 2, 1)  // Reversed order
        
        // Act
        let isValid = isValidGate graph gate
        
        // Assert
        Assert.True(isValid)
    
    [<Fact>]
    let ``isValidGate - two-qubit gate invalid if edge missing`` () =
        // Arrange
        let graph = { Edges = Set.ofList [(0, 1); (1, 2); (2, 3)] }
        let gate = QuilGate.TwoQubit("CZ", 0, 3)  // Not directly connected
        
        // Act
        let isValid = isValidGate graph gate
        
        // Assert
        Assert.False(isValid)
    
    [<Fact>]
    let ``validateProgram - returns Ok for valid program`` () =
        // Arrange
        let graph = { Edges = Set.ofList [(0, 1); (1, 2)] }
        let program = {
            Declarations = [ QuilGate.DeclareMemory("ro", "BIT", 2) ]
            Instructions = [
                QuilGate.SingleQubit("H", 0)
                QuilGate.TwoQubit("CZ", 0, 1)  // Valid edge
                QuilGate.Measure(0, "ro[0]")
            ]
        }
        
        // Act
        let result = validateProgram graph program
        
        // Assert
        match result with
        | Ok () -> Assert.True(true)
        | Error msg -> Assert.True(false, sprintf "Expected Ok, got Error: %s" msg)
    
    [<Fact>]
    let ``validateProgram - returns Error for invalid two-qubit gate`` () =
        // Arrange
        let graph = { Edges = Set.ofList [(0, 1); (1, 2)] }
        let program = {
            Declarations = []
            Instructions = [
                QuilGate.SingleQubit("H", 0)
                QuilGate.TwoQubit("CZ", 0, 2)  // Invalid - no direct edge
                QuilGate.Measure(0, "ro[0]")
            ]
        }
        
        // Act
        let result = validateProgram graph program
        
        // Assert
        match result with
        | Ok () -> Assert.True(false, "Expected Error, got Ok")
        | Error msg ->
            Assert.Contains("CZ 0 2", msg)
            Assert.Contains("connectivity", msg.ToLower())
    
    [<Fact>]
    let ``validateProgram - returns Error with gate details for multiple invalid gates`` () =
        // Arrange
        let graph = { Edges = Set.ofList [(0, 1); (1, 2)] }
        let program = {
            Declarations = []
            Instructions = [
                QuilGate.TwoQubit("CZ", 0, 2)  // Invalid
                QuilGate.TwoQubit("CZ", 0, 3)  // Invalid
            ]
        }
        
        // Act
        let result = validateProgram graph program
        
        // Assert
        match result with
        | Ok () -> Assert.True(false, "Expected Error, got Ok")
        | Error msg ->
            Assert.Contains("CZ 0 2", msg)
            // Should report first invalid gate
    
    // ============================================================================
    // TDD CYCLE 5 & 6: Job Submission, Result Parsing, Error Mapping
    // ============================================================================
    
    [<Fact>]
    let ``createJobSubmission - creates valid job submission for Rigetti simulator`` () =
        // Arrange
        let program = {
            Declarations = [ QuilGate.DeclareMemory("ro", "BIT", 2) ]
            Instructions = [
                QuilGate.SingleQubit("H", 0)
                QuilGate.TwoQubit("CZ", 0, 1)
                QuilGate.Measure(0, "ro[0]")
                QuilGate.Measure(1, "ro[1]")
            ]
        }
        let shots = 1000
        
        // Act
        let submission = createJobSubmission program shots "rigetti.sim.qvm" None
        
        // Assert
        Assert.Equal("rigetti.sim.qvm", submission.Target)
        Assert.Equal(CircuitFormat.Custom "application/x-quil", submission.InputDataFormat)
        Assert.True(submission.InputParams.ContainsKey "count")
        Assert.Equal(shots, submission.InputParams.["count"] :?> int)
    
    [<Fact>]
    let ``createJobSubmission - serializes program to Quil text in InputData`` () =
        // Arrange
        let program = {
            Declarations = [ QuilGate.DeclareMemory("ro", "BIT", 1) ]
            Instructions = [
                QuilGate.SingleQubit("H", 0)
                QuilGate.Measure(0, "ro[0]")
            ]
        }
        
        // Act
        let submission = createJobSubmission program 100 "rigetti.sim.qvm" None
        
        // Assert
        let quilText = submission.InputData :?> string
        Assert.Contains("DECLARE ro BIT[1]", quilText)
        Assert.Contains("H 0", quilText)
        Assert.Contains("MEASURE 0 ro[0]", quilText)
    
    [<Fact>]
    let ``parseRigettiResults - parses histogram from Rigetti response`` () =
        // Arrange - Rigetti returns results in format: {"00": 480, "01": 12, "10": 8, "11": 500}
        let rigettiResponse = 
            {|
                histogram = dict [("00", 480); ("01", 12); ("10", 8); ("11", 500)]
            |}
        let json = JsonSerializer.Serialize(rigettiResponse)
        
        // Act
        let result = parseRigettiResults json
        
        // Assert
        match result with
        | Ok histogram ->
            Assert.Equal(4, histogram.Count)
            Assert.Equal(480, histogram.["00"])
            Assert.Equal(12, histogram.["01"])
            Assert.Equal(8, histogram.["10"])
            Assert.Equal(500, histogram.["11"])
        | Error e -> Assert.True(false, sprintf "Expected Ok, got Error: %A" e)
    
    [<Fact>]
    let ``parseRigettiResults - handles empty histogram`` () =
        // Arrange
        let rigettiResponse = {| histogram = dict [] |}
        let json = JsonSerializer.Serialize(rigettiResponse)
        
        // Act
        let result = parseRigettiResults json
        
        // Assert
        match result with
        | Ok histogram -> Assert.Empty(histogram)
        | Error e -> Assert.True(false, sprintf "Expected Ok, got Error: %A" e)
    
    [<Fact>]
    let ``mapRigettiError - maps InvalidProgram to InvalidCircuit`` () =
        // Arrange
        let errorCode = "InvalidProgram"
        let errorMessage = "Malformed Quil program"
        
        // Act
        let quantumError = mapRigettiError errorCode errorMessage
        
        // Assert
        match quantumError with
        | QuantumError.InvalidCircuit errors -> 
            Assert.NotEmpty(errors)
            Assert.Contains("Malformed Quil", errors.[0])
        | _ -> Assert.True(false, "Expected InvalidCircuit")
    
    [<Fact>]
    let ``mapRigettiError - maps TopologyError to InvalidCircuit`` () =
        // Arrange
        let errorCode = "TopologyError"
        let errorMessage = "Gate CZ 0 5 violates connectivity"
        
        // Act
        let quantumError = mapRigettiError errorCode errorMessage
        
        // Assert
        match quantumError with
        | QuantumError.InvalidCircuit errors -> 
            Assert.NotEmpty(errors)
            Assert.Contains("connectivity", errors.[0])
        | _ -> Assert.True(false, "Expected InvalidCircuit")
    
    [<Fact>]
    let ``mapRigettiError - maps TooManyQubits to InvalidCircuit`` () =
        // Arrange
        let errorCode = "TooManyQubits"
        let errorMessage = "Circuit requires 50 qubits, maximum is 40"
        
        // Act
        let quantumError = mapRigettiError errorCode errorMessage
        
        // Assert
        match quantumError with
        | QuantumError.InvalidCircuit errors -> 
            Assert.NotEmpty(errors)
            Assert.Contains("40", errors.[0])
        | _ -> Assert.True(false, "Expected InvalidCircuit")
    
    [<Fact>]
    let ``mapRigettiError - maps QuotaExceeded to QuotaExceeded`` () =
        // Arrange
        let errorCode = "QuotaExceeded"
        let errorMessage = "Insufficient quantum credits"
        
        // Act
        let quantumError = mapRigettiError errorCode errorMessage
        
        // Assert
        match quantumError with
        | QuantumError.QuotaExceeded _ -> Assert.True(true)
        | _ -> Assert.True(false, "Expected QuotaExceeded")
