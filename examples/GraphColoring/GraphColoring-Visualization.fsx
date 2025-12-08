#!/usr/bin/env dotnet fsi
// Graph Coloring with Visualization Example
// 
// This example demonstrates how to visualize Graph Coloring solutions
// using ASCII (for terminals) and Mermaid (for documentation).

#I "../../src/FSharp.Azure.Quantum/bin/Debug/net10.0"
#r "FSharp.Azure.Quantum.dll"

open System.IO
open FSharp.Azure.Quantum
open FSharp.Azure.Quantum.GraphColoring
open FSharp.Azure.Quantum.Visualization

// Example: Register Allocation for a Simple Compiler
// Variables x, y, z, w have conflicts when they're live at the same time

let registerAllocation = graphColoring {
    node "x" ["y"; "z"]
    node "y" ["x"; "w"]
    node "z" ["x"; "w"]
    node "w" ["y"; "z"]
    colors ["R0"; "R1"; "R2"; "R3"]
    objective MinimizeColors
}

printfn "=========================================="
printfn " Graph Coloring Visualization Example"
printfn " Use Case: Register Allocation"
printfn "=========================================="
printfn ""

// Solve the problem
match GraphColoring.solve registerAllocation 4 None with
| Error err ->
    printfn "Error: %s" err.Message
| Ok solution ->
    printfn "Solution Found!"
    printfn ""
    
    // 1. Display ASCII visualization in terminal
    printfn "==========================================  "
    printfn " ASCII Visualization (Terminal-Friendly)"
    printfn "=========================================="
    printfn "%s" (solution.ToASCII())
    printfn ""
    
    // 2. Generate Mermaid diagram for documentation
    let mermaidOutput = solution.ToMermaid()
    
    printfn "=========================================="
    printfn " Mermaid Diagram (GitHub/Docs)"
    printfn "=========================================="
    printfn "%s" mermaidOutput
    printfn ""
    
    // 3. Save Mermaid to file for documentation
    let outputPath = "register-allocation.md"
    let assignmentsText =
        solution.Assignments 
        |> Map.toList 
        |> List.map (fun (var, reg) -> sprintf "- `%s` -> `%s`" var reg)
        |> String.concat "\n"
    
    let markdown = sprintf "# Register Allocation Solution\n\n## Problem\nAllocate CPU registers to variables with minimal conflicts.\n\n**Variables:** x, y, z, w\n\n**Constraints:**\n- x conflicts with y, z\n- y conflicts with x, w\n- z conflicts with x, w\n- w conflicts with y, z\n\n## Solution\n%s\n\n## Analysis\n- **Registers Used:** %d / 4\n- **Valid:** %b\n- **Cost:** %.2f\n\n## Register Assignments\n%s\n" mermaidOutput solution.ColorsUsed solution.IsValid solution.Cost assignmentsText
    
    File.WriteAllText(outputPath, markdown)
    printfn "Saved visualization to: %s" outputPath
    printfn ""
    
    printfn "=========================================="
    printfn " Usage Instructions"
    printfn "=========================================="
    printfn "1. View in GitHub: Open %s in GitHub" outputPath
    printfn "2. View locally: Use VS Code with Mermaid extension"
    printfn "3. Terminal: Use ASCII output above"
    printfn ""
    printfn "Done! Check %s for the full visualization." "register-allocation.md"
