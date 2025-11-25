namespace FSharp.Azure.Quantum.Tests

open System
open System.Net
open System.Net.Http
open System.Text
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
