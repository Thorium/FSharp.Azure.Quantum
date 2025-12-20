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
    
    /// Extension methods for TopologicalBuilder.BuilderContext
    type TopologicalBuilder.BuilderContext with
        
        /// Generate Mermaid sequence diagram of the topological circuit
        member this.ToMermaid() : string =
            let sb = StringBuilder()
            sb.AppendLine("```mermaid") |> ignore
            sb.AppendLine("sequenceDiagram") |> ignore
            sb.AppendLine("    autonumber") |> ignore
            
            // Reverse history to process in chronological order
            let history = List.rev this.History
            
            // Helper to get anyon names
            let getAnyonName i = sprintf "Anyon%d" i
            
            // Process history
            let mutable anyonCount = 0
            
            for op in history do
                match op with
                | TopologicalBuilder.Init (anyonType, count) ->
                    anyonCount <- count
                    sb.AppendLine(sprintf "    Note over %s,%s: Initialize %d %A Anyons" 
                        (getAnyonName 0) (getAnyonName (count-1)) count anyonType) |> ignore
                        
                | TopologicalBuilder.Braid index ->
                    let left = getAnyonName index
                    let right = getAnyonName (index + 1)
                    sb.AppendLine(sprintf "    %s->>%s: Braid σ%d" left right index) |> ignore
                    sb.AppendLine(sprintf "    %s->>%s: Swap" right left) |> ignore
                    
                | TopologicalBuilder.Measure (index, outcome, prob) ->
                    let left = getAnyonName index
                    let right = getAnyonName (index + 1)
                    sb.AppendLine(sprintf "    Note over %s,%s: Measure Fusion" left right) |> ignore
                    sb.AppendLine(sprintf "    %s-->>%s: Result: %A (P=%.2f)" left right outcome prob) |> ignore
                    
                | TopologicalBuilder.Comment msg ->
                    if anyonCount > 0 then
                        sb.AppendLine(sprintf "    Note over %s,%s: %s" 
                            (getAnyonName 0) (getAnyonName (anyonCount-1)) msg) |> ignore
            
            sb.AppendLine("```") |> ignore
            sb.ToString()

