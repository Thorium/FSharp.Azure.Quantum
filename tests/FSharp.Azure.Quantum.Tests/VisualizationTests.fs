namespace FSharp.Azure.Quantum.Tests

open Xunit
open FSharp.Azure.Quantum
open FSharp.Azure.Quantum.Visualization

module VisualizationTests =
    
    [<Fact>]
    let ``ASCII renderer should render simple circuit`` () =
        // Arrange
        let gates = [
            CircuitGate (CircuitBuilder.H 0)
            CircuitGate (CircuitBuilder.CNOT (0, 1))
            CircuitGate (CircuitBuilder.Measure 0)
            CircuitGate (CircuitBuilder.Measure 1)
        ]
        
        // Act
        let result = ASCIIRenderer.render 2 gates
        
        // Assert
        Assert.Contains("q_0:", result)
        Assert.Contains("q_1:", result)
        Assert.Contains("H", result)
        Assert.Contains("X", result)
        Assert.Contains("M", result)
        Assert.Contains("■", result)  // Control dot
    
    [<Fact>]
    let ``Mermaid sequence renderer should generate valid mermaid`` () =
        // Arrange
        let gates = [
            CircuitGate (CircuitBuilder.H 0)
            CircuitGate (CircuitBuilder.CNOT (0, 1))
        ]
        
        // Act
        let result = MermaidRenderer.Sequence.render 2 gates
        
        // Assert
        Assert.Contains("```mermaid", result)
        Assert.Contains("sequenceDiagram", result)
        Assert.Contains("participant q0", result)
        Assert.Contains("participant q1", result)
    
    [<Fact>]
    let ``Mermaid flowchart renderer should generate valid mermaid`` () =
        // Arrange
        let gates = [
            CircuitGate (CircuitBuilder.H 0)
            CircuitGate (CircuitBuilder.Measure 0)
        ]
        
        // Act
        let result = MermaidRenderer.Flowchart.render 1 gates
        
        // Assert
        Assert.Contains("```mermaid", result)
        Assert.Contains("flowchart LR", result)
    
    [<Fact>]
    let ``Mermaid graph renderer should generate colored graph`` () =
        // Arrange
        let nodes = [
            MermaidRenderer.Graph.node "A" "ff6b6b"
            MermaidRenderer.Graph.node "B" "4ecdc4"
        ]
        let edges = [
            MermaidRenderer.Graph.edge "A" "B"
        ]
        
        // Act
        let result = MermaidRenderer.Graph.render nodes edges
        
        // Assert
        Assert.Contains("```mermaid", result)
        Assert.Contains("graph TD", result)
        Assert.Contains("A[", result)
        Assert.Contains("fill:#ff6b6b", result)
        Assert.Contains("A --- B", result)
        Assert.Contains("```", result)
    
    [<Fact>]
    let ``Mermaid graph renderSimple should work with tuples`` () =
        // Arrange
        let nodes = [("A", Some "ff6b6b"); ("B", Some "4ecdc4")]
        let edges = [("A", "B")]
        
        // Act
        let result = MermaidRenderer.Graph.renderSimple nodes edges
        
        // Assert
        Assert.Contains("A[", result)
        Assert.Contains("B[", result)
        Assert.Contains("A --- B", result)
    
    (* TODO: Visualization extension methods not yet implemented
    [<Fact>]
    let ``Graph coloring solution should generate mermaid diagram`` () =
        // Arrange
        let solution : GraphColoring.ColoringSolution = {
            Assignments = Map.ofList [("A", "red"); ("B", "blue")]
            ColorsUsed = 2
            ConflictCount = 0
            IsValid = true
            ColorDistribution = Map.ofList [("red", 1); ("blue", 1)]
            Cost = 0.0
            BackendName = "Test"
            IsQuantum = false
        }
        
        // Act
        let result = solution.ToMermaid()
        
        // Assert
        Assert.Contains("```mermaid", result)
        Assert.Contains("graph TD", result)
        Assert.Contains("A[", result)
        Assert.Contains("B[", result)
    
    [<Fact>]
    let ``Graph coloring solution should generate ASCII representation`` () =
        // Arrange
        let solution : GraphColoring.ColoringSolution = {
            Assignments = Map.ofList [("A", "red"); ("B", "blue")]
            ColorsUsed = 2
            ConflictCount = 0
            IsValid = true
            ColorDistribution = Map.ofList [("red", 1); ("blue", 1)]
            Cost = 0.0
            BackendName = "Test"
            IsQuantum = false
        }
        
        // Act
        let result = solution.ToASCII()
        
        // Assert
        Assert.Contains("Graph Coloring Solution", result)
        Assert.Contains("Colors Used: 2", result)
        Assert.Contains("Conflicts: 0", result)
        Assert.Contains("Valid: True", result)
        Assert.Contains("A → red", result)
        Assert.Contains("B → blue", result)
    *)
    
    [<Fact>]
    let ``ASCII renderer should handle barriers`` () =
        // Arrange
        let gates = [
            CircuitGate (CircuitBuilder.H 0)
            Barrier [0; 1]
            CircuitGate (CircuitBuilder.Measure 0)
        ]
        
        // Act
        let result = ASCIIRenderer.render 2 gates
        
        // Assert
        Assert.Contains("░", result)  // Barrier character
    
    [<Fact>]
    let ``Visualization config should filter measurements`` () =
        // Arrange
        let config = { VisualizationConfig.defaultConfig with ShowMeasurements = false }
        let gates = [
            CircuitGate (CircuitBuilder.H 0)
            CircuitGate (CircuitBuilder.Measure 0)
        ]
        
        // Act
        let result = ASCIIRenderer.renderWithConfig config 1 gates
        
        // Assert
        Assert.Contains("H", result)
        Assert.DoesNotContain("M", result)  // Measurement should be filtered
