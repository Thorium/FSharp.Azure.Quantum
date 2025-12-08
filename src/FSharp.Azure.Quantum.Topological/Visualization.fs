namespace FSharp.Azure.Quantum.Topological

open System.Text

/// Visualization extensions for topological quantum computing types
[<AutoOpen>]
module VisualizationExtensions =
    
    /// Extension methods for Fusion Tree States
    type FusionTree.State with
        
        /// Generate Mermaid diagram of fusion tree structure
        member this.ToMermaid() : string =
            let sb = StringBuilder()
            sb.AppendLine("```mermaid") |> ignore
            sb.AppendLine("graph TD") |> ignore
            sb.AppendLine("    %% Fusion Tree Structure") |> ignore
            
            // Recursive function to render tree nodes
            let rec renderNode (tree: FusionTree.Tree) (nodeId: int) : int =
                match tree with
                | FusionTree.Leaf particle ->
                    // Leaf: single anyon particle
                    sb.AppendLine(sprintf "    n%d[\"%A\"]" nodeId particle) |> ignore
                    sb.AppendLine(sprintf "    style n%d fill:#95e1d3,stroke:#333,stroke-width:2px" nodeId) |> ignore
                    nodeId + 1
                
                | FusionTree.Fusion (left, right, channel) ->
                    // Internal node: fusion operation
                    let currentNode = nodeId
                    sb.AppendLine(sprintf "    n%d[\"Fusion<br/>→ %A\"]" currentNode channel) |> ignore
                    sb.AppendLine(sprintf "    style n%d fill:#4ecdc4,stroke:#333,stroke-width:2px" currentNode) |> ignore
                    
                    // Render left subtree
                    let nextId = renderNode left (nodeId + 1)
                    sb.AppendLine(sprintf "    n%d --> n%d" currentNode (nodeId + 1)) |> ignore
                    
                    // Render right subtree
                    let finalId = renderNode right nextId
                    sb.AppendLine(sprintf "    n%d --> n%d" currentNode nextId) |> ignore
                    
                    finalId
            
            // Start rendering from root
            let _ = renderNode this.Tree 0
            
            // Add metadata
            sb.AppendLine("") |> ignore
            sb.AppendLine(sprintf "    subgraph Info[\"Fusion Tree Info\"]") |> ignore
            sb.AppendLine(sprintf "        info1[\"Theory: %A\"]" this.AnyonType) |> ignore
            sb.AppendLine(sprintf "        info2[\"Anyons: %d\"]" (FusionTree.size this.Tree)) |> ignore
            sb.AppendLine(sprintf "        info3[\"Depth: %d\"]" (FusionTree.depth this.Tree)) |> ignore
            sb.AppendLine(sprintf "        info4[\"Total Charge: %A\"]" (FusionTree.totalCharge this.Tree this.AnyonType)) |> ignore
            sb.AppendLine("    end") |> ignore
            sb.AppendLine("```") |> ignore
            
            sb.ToString()
        
        /// Generate ASCII diagram of fusion tree
        member this.ToASCII() : string =
            let sb = StringBuilder()
            sb.AppendLine("Fusion Tree State") |> ignore
            sb.AppendLine("=================") |> ignore
            sb.AppendLine(sprintf "Theory: %A" this.AnyonType) |> ignore
            sb.AppendLine(sprintf "Anyons: %d" (FusionTree.size this.Tree)) |> ignore
            sb.AppendLine(sprintf "Depth: %d" (FusionTree.depth this.Tree)) |> ignore
            sb.AppendLine(sprintf "Total Charge: %A" (FusionTree.totalCharge this.Tree this.AnyonType)) |> ignore
            sb.AppendLine("") |> ignore
            sb.AppendLine("Tree Structure:") |> ignore
            
            // Recursive function to render tree with indentation
            let rec renderTree (tree: FusionTree.Tree) (indent: string) (isLast: bool) =
                let prefix = indent + (if isLast then "└─ " else "├─ ")
                let childIndent = indent + (if isLast then "   " else "│  ")
                
                match tree with
                | FusionTree.Leaf particle ->
                    sb.AppendLine(sprintf "%sLeaf: %A" prefix particle) |> ignore
                
                | FusionTree.Fusion (left, right, channel) ->
                    sb.AppendLine(sprintf "%sFusion → %A" prefix channel) |> ignore
                    renderTree left childIndent false
                    renderTree right childIndent true
            
            renderTree this.Tree "" true
            sb.ToString()
    
    /// Extension methods for Topological Superposition states
    type TopologicalOperations.Superposition with
        
        /// Generate Mermaid diagram showing superposition components
        member this.ToMermaid() : string =
            let sb = StringBuilder()
            sb.AppendLine("```mermaid") |> ignore
            sb.AppendLine("graph LR") |> ignore
            sb.AppendLine("    %% Quantum Superposition") |> ignore
            
            // Show superposition as weighted sum of basis states
            sb.AppendLine(sprintf "    psi[\"|ψ⟩<br/>Superposition<br/>(%d terms)\"]" this.Terms.Length) |> ignore
            sb.AppendLine("    style psi fill:#ff6b6b,stroke:#333,stroke-width:3px") |> ignore
            
            // Show each term with amplitude and probability
            this.Terms
            |> List.iteri (fun i (amplitude, state) ->
                let prob = TopologicalOperations.probability amplitude
                let treeStr = FusionTree.toString state.Tree
                
                sb.AppendLine(sprintf "    term%d[\"%s<br/>amp: %.3f + %.3fi<br/>P: %.3f\"]" 
                    i treeStr amplitude.Real amplitude.Imaginary prob) |> ignore
                
                // Color by probability
                let color = 
                    if prob > 0.5 then "95e1d3"  // High probability: green
                    elif prob > 0.25 then "4ecdc4" // Medium: cyan
                    else "a29bfe"  // Low: purple
                
                sb.AppendLine(sprintf "    style term%d fill:#%s,stroke:#333" i color) |> ignore
                sb.AppendLine(sprintf "    psi ==>|\"%.3f\"| term%d" prob i) |> ignore
            )
            
            // Add normalization check
            let isNorm = TopologicalOperations.isNormalized this
            let normColor = if isNorm then "green" else "red"
            sb.AppendLine("") |> ignore
            sb.AppendLine(sprintf "    norm[\"Normalized: %b\"]" isNorm) |> ignore
            sb.AppendLine(sprintf "    style norm fill:#ffffff,stroke:%s,stroke-width:2px" normColor) |> ignore
            
            sb.AppendLine("```") |> ignore
            sb.ToString()
        
        /// Generate ASCII representation of superposition
        member this.ToASCII() : string =
            let sb = StringBuilder()
            sb.AppendLine("Topological Quantum Superposition") |> ignore
            sb.AppendLine("==================================") |> ignore
            sb.AppendLine(sprintf "Theory: %A" this.AnyonType) |> ignore
            sb.AppendLine(sprintf "Terms: %d" this.Terms.Length) |> ignore
            sb.AppendLine(sprintf "Normalized: %b" (TopologicalOperations.isNormalized this)) |> ignore
            sb.AppendLine("") |> ignore
            
            // Calculate total probability
            let totalProb = 
                this.Terms 
                |> List.sumBy (fun (amp, _) -> TopologicalOperations.probability amp)
            sb.AppendLine(sprintf "Total Probability: %.6f" totalProb) |> ignore
            sb.AppendLine("") |> ignore
            
            // Show each term
            sb.AppendLine("Basis States:") |> ignore
            this.Terms
            |> List.iteri (fun i (amplitude, state) ->
                let prob = TopologicalOperations.probability amplitude
                let treeStr = FusionTree.toString state.Tree
                
                sb.AppendLine(sprintf "  [%d] %.4f + %.4fi  |  P = %.4f" i amplitude.Real amplitude.Imaginary prob) |> ignore
                sb.AppendLine(sprintf "      Tree: %s" treeStr) |> ignore
                sb.AppendLine(sprintf "      Anyons: %d, Charge: %A" 
                    (FusionTree.size state.Tree) 
                    (FusionTree.totalCharge state.Tree state.AnyonType)) |> ignore
                sb.AppendLine("") |> ignore
            )
            
            sb.ToString()
