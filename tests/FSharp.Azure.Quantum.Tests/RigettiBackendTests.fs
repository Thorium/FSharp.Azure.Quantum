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
