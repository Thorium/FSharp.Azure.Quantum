namespace FSharp.Azure.Quantum.Visualization

open FSharp.Azure.Quantum

/// Visualization output format
type DiagramFormat =
    | ASCII
    | Mermaid
    | LaTeX

/// Extended gate type for visualization (adds Barrier to CircuitBuilder.Gate)
type VisualizationGate =
    /// All standard quantum gates
    | CircuitGate of CircuitBuilder.Gate
    /// Barrier for circuit sections
    | Barrier of qubits:int list

/// Visualization configuration
type VisualizationConfig = {
    /// Format to use for output
    Format: DiagramFormat
    /// Show measurement operations
    ShowMeasurements: bool
    /// Show barriers between circuit sections
    ShowBarriers: bool
    /// Include qubit initialization labels
    ShowInitialization: bool
}

module VisualizationConfig =
    /// Default configuration
    let defaultConfig = {
        Format = ASCII
        ShowMeasurements = true
        ShowBarriers = false
        ShowInitialization = true
    }
