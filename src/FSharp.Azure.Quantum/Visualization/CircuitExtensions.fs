namespace FSharp.Azure.Quantum.Visualization

open FSharp.Azure.Quantum.CircuitBuilder

/// Extension methods for Circuit to enable easy visualization
[<AutoOpen>]
module CircuitExtensions =
    
    /// Convert CircuitBuilder.Gate to VisualizationGate
    let private toVisualizationGate (gate: Gate) : VisualizationGate =
        CircuitGate gate
    
    type Circuit with
        /// Render circuit as ASCII art diagram (Qiskit-style)
        /// 
        /// Example:
        ///   let circuit = circuit { qubits 2; H 0; CNOT (0,1) }
        ///   printfn "%s" (circuit.ToASCII())
        /// 
        /// Output:
        ///   q_0: ┌───┐
        ///        │ H │■
        ///        └───┘│
        ///   q_1: ─────┼X─
        ///             │
        member this.ToASCII() : string =
            let vizGates = this.Gates |> List.rev |> List.map toVisualizationGate
            ASCIIRenderer.render this.QubitCount vizGates
        
        /// Render circuit as ASCII art with custom configuration
        /// 
        /// Example:
        ///   let config = { VisualizationConfig.defaultConfig with ShowMeasurements = false }
        ///   printfn "%s" (circuit.ToASCIIWithConfig config)
        member this.ToASCIIWithConfig(config: VisualizationConfig) : string =
            let vizGates = this.Gates |> List.rev |> List.map toVisualizationGate
            ASCIIRenderer.renderWithConfig config this.QubitCount vizGates
        
        /// Render circuit as Mermaid sequence diagram
        /// 
        /// Example:
        ///   let circuit = circuit { qubits 2; H 0; CNOT (0,1) }
        ///   printfn "%s" (circuit.ToMermaid())
        /// 
        /// Output: Mermaid markdown showing temporal gate application
        member this.ToMermaid() : string =
            let vizGates = this.Gates |> List.rev |> List.map toVisualizationGate
            MermaidRenderer.Sequence.render this.QubitCount vizGates
        
        /// Render circuit as Mermaid flowchart (data flow view)
        /// 
        /// Example:
        ///   let circuit = circuit { qubits 2; H 0; CNOT (0,1) }
        ///   printfn "%s" (circuit.ToMermaidFlowchart())
        /// 
        /// Output: Mermaid flowchart showing qubit data flow
        member this.ToMermaidFlowchart() : string =
            let vizGates = this.Gates |> List.rev |> List.map toVisualizationGate
            MermaidRenderer.Flowchart.render this.QubitCount vizGates
