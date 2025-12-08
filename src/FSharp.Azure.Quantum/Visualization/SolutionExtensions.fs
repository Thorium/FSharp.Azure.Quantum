namespace FSharp.Azure.Quantum.Visualization

open FSharp.Azure.Quantum
open System.Text

/// Extension methods for solution visualization
[<AutoOpen>]
module SolutionVisualizationExtensions =
    
    /// Extension methods for Graph Coloring solutions
    type GraphColoring.ColoringSolution with
        
        /// Generate Mermaid graph diagram showing colored nodes and edges
        member this.ToMermaid() : string =
            // Extract nodes and their colors
            let nodes =
                this.Assignments
                |> Map.toList
                |> List.map (fun (nodeName, color) ->
                    // Convert color name to hex (simple mapping for common colors)
                    let colorHex = 
                        match color.ToLower() with
                        | "red" -> "ff6b6b"
                        | "blue" -> "4ecdc4"
                        | "green" -> "95e1d3"
                        | "yellow" -> "f9ca24"
                        | "purple" -> "a29bfe"
                        | "orange" -> "ff9f43"
                        | "pink" -> "fd79a8"
                        | "cyan" -> "00b894"
                        | _ -> "cccccc"  // Default gray for unknown colors
                    
                    MermaidRenderer.Graph.nodeWithColorAndLabel nodeName colorHex color)
            
            // For now, we don't have edge information in the solution
            // In future, we could add edges from GraphColoringProblem
            let edges = []
            
            MermaidRenderer.Graph.render nodes edges
        
        /// Generate ASCII art representation
        member this.ToASCII() : string =
            let sb = System.Text.StringBuilder()
            sb.AppendLine("Graph Coloring Solution") |> ignore
            sb.AppendLine("======================") |> ignore
            sb.AppendLine($"Colors Used: {this.ColorsUsed}") |> ignore
            sb.AppendLine($"Conflicts: {this.ConflictCount}") |> ignore
            sb.AppendLine($"Valid: {this.IsValid}") |> ignore
            sb.AppendLine("") |> ignore
            sb.AppendLine("Node Assignments:") |> ignore
            
            this.Assignments
            |> Map.toList
            |> List.sortBy fst
            |> List.iter (fun (node, color) ->
                sb.AppendLine($"  {node} → {color}") |> ignore)
            
            sb.ToString()
    
    /// Extension methods for Quantum Circuits
    type CircuitBuilder.Circuit with
        
        /// Generate ASCII diagram of the quantum circuit
        member this.ToASCII() : string =
            // Convert CircuitBuilder.Gate to VisualizationGate
            let vizGates = 
                this.Gates 
                |> List.map (fun gate -> CircuitGate gate)
            
            // Render using ASCII renderer
            ASCIIRenderer.render this.QubitCount vizGates
        
        /// Generate Mermaid sequence diagram of the quantum circuit
        member this.ToMermaid() : string =
            // Convert CircuitBuilder.Gate to VisualizationGate
            let vizGates = 
                this.Gates 
                |> List.map (fun gate -> CircuitGate gate)
            
            // Render using Mermaid sequence diagram
            MermaidRenderer.Sequence.render this.QubitCount vizGates
    
    /// Extension methods for Graph Coloring Problems (before solving)
    type GraphColoring.GraphColoringProblem with
        
        /// Generate Mermaid graph diagram showing the problem structure (nodes and conflicts)
        member this.ToMermaid() : string =
            // Create nodes
            let nodes =
                this.Nodes
                |> List.map (fun node ->
                    let label = 
                        match node.FixedColor with
                        | Some color -> sprintf "Node %s<br/>(Fixed: %s)" node.Id color
                        | None -> sprintf "Node %s" node.Id
                    MermaidRenderer.Graph.nodeWithLabel node.Id label)
            
            // Create edges from conflicts
            let edges =
                this.Nodes
                |> List.collect (fun node ->
                    node.ConflictsWith
                    |> List.filter (fun targetId -> targetId > node.Id) // Avoid duplicates
                    |> List.map (fun targetId ->
                        MermaidRenderer.Graph.edge node.Id targetId))
            
            MermaidRenderer.Graph.render nodes edges
        
        /// Generate ASCII representation of the problem
        member this.ToASCII() : string =
            let sb = StringBuilder()
            sb.AppendLine("Graph Coloring Problem") |> ignore
            sb.AppendLine("======================") |> ignore
            sb.AppendLine(sprintf "Nodes: %d" this.Nodes.Length) |> ignore
            sb.AppendLine(sprintf "Available Colors: %s" (String.concat ", " this.AvailableColors)) |> ignore
            sb.AppendLine(sprintf "Objective: %A" this.Objective) |> ignore
            sb.AppendLine("") |> ignore
            sb.AppendLine("Node Conflicts:") |> ignore
            
            this.Nodes
            |> List.sortBy (fun n -> n.Id)
            |> List.iter (fun node ->
                let fixedStr = 
                    match node.FixedColor with
                    | Some c -> sprintf " (Fixed: %s)" c
                    | None -> ""
                let conflictsStr = 
                    if node.ConflictsWith.IsEmpty then "none"
                    else String.concat ", " node.ConflictsWith
                sb.AppendLine(sprintf "  %s%s → conflicts with: %s" node.Id fixedStr conflictsStr) |> ignore)
            
            sb.ToString()
    
    /// Extension methods for QUBO Matrices
    type GraphOptimization.QuboMatrix with
        
        /// Generate Mermaid diagram showing QUBO matrix structure
        member this.ToMermaid() : string =
            let sb = StringBuilder()
            sb.AppendLine("```mermaid") |> ignore
            sb.AppendLine("graph TD") |> ignore
            sb.AppendLine("    subgraph QUBO[\"QUBO Matrix Structure\"]") |> ignore
            
            // Show variables as nodes
            for i in 0 .. this.NumVariables - 1 do
                sb.AppendLine(sprintf "        v%d[\"x_%d\"]" i i) |> ignore
            
            sb.AppendLine("    end") |> ignore
            sb.AppendLine("") |> ignore
            sb.AppendLine("    subgraph Interactions[\"Non-zero Coefficients\"]") |> ignore
            
            // Show non-zero coefficients as edges
            let coefficients = 
                this.Q 
                |> Map.toList 
                |> List.sortBy (fun ((i, j), _) -> (i, j))
            
            for ((i, j), coef) in coefficients do
                if abs coef > 1e-10 then // Ignore near-zero coefficients
                    if i = j then
                        // Diagonal term (linear coefficient)
                        let color = if coef > 0.0 then "red" else "green"
                        sb.AppendLine(sprintf "        v%d -.\"%.2f\".-> v%d" i coef i) |> ignore
                        sb.AppendLine(sprintf "        style v%d stroke:%s,stroke-width:3px" i color) |> ignore
                    else
                        // Off-diagonal term (quadratic coefficient)
                        let style = if coef > 0.0 then "solid" else "dashed"
                        sb.AppendLine(sprintf "        v%d ==\"%.2f\"==> v%d" i coef j) |> ignore
            
            sb.AppendLine("    end") |> ignore
            sb.AppendLine("```") |> ignore
            sb.ToString()
        
        /// Generate ASCII representation of QUBO matrix
        member this.ToASCII() : string =
            let sb = StringBuilder()
            sb.AppendLine("QUBO Matrix") |> ignore
            sb.AppendLine("===========") |> ignore
            sb.AppendLine(sprintf "Variables: %d" this.NumVariables) |> ignore
            sb.AppendLine(sprintf "Non-zero coefficients: %d" this.Q.Count) |> ignore
            sb.AppendLine("") |> ignore
            
            // Separate linear and quadratic terms
            let linearTerms = 
                this.Q 
                |> Map.toList 
                |> List.filter (fun ((i, j), _) -> i = j)
                |> List.sortBy fst
            
            let quadraticTerms = 
                this.Q 
                |> Map.toList 
                |> List.filter (fun ((i, j), _) -> i <> j)
                |> List.sortBy fst
            
            // Display linear terms
            if not linearTerms.IsEmpty then
                sb.AppendLine("Linear Terms (diagonal):") |> ignore
                for ((i, _), coef) in linearTerms do
                    sb.AppendLine(sprintf "  x_%d: %.4f" i coef) |> ignore
                sb.AppendLine("") |> ignore
            
            // Display quadratic terms
            if not quadraticTerms.IsEmpty then
                sb.AppendLine("Quadratic Terms (off-diagonal):") |> ignore
                for ((i, j), coef) in quadraticTerms do
                    sb.AppendLine(sprintf "  x_%d * x_%d: %.4f" i j coef) |> ignore
                sb.AppendLine("") |> ignore
            
            // Summary statistics
            let allCoefs = this.Q |> Map.toList |> List.map snd
            if not allCoefs.IsEmpty then
                let minCoef = List.min allCoefs
                let maxCoef = List.max allCoefs
                let avgCoef = List.average allCoefs
                
                sb.AppendLine("Statistics:") |> ignore
                sb.AppendLine(sprintf "  Min coefficient: %.4f" minCoef) |> ignore
                sb.AppendLine(sprintf "  Max coefficient: %.4f" maxCoef) |> ignore
                sb.AppendLine(sprintf "  Avg coefficient: %.4f" avgCoef) |> ignore
            
            sb.ToString()
