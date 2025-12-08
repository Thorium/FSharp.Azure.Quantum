#!/usr/bin/env dotnet fsi
// ============================================================================
// Graph Coloring - Problem AND Solution Visualization
// ============================================================================
// 
// This example demonstrates visualization at BOTH stages:
// 1. BEFORE solving: Visualize the problem structure
// 2. AFTER solving: Visualize the solution
//
// KEY INSIGHT:
// - .ToMermaid()/.ToASCII() work on BOTH problems AND solutions!
//
// ============================================================================

#I "../../src/FSharp.Azure.Quantum/bin/Debug/net10.0"
#r "FSharp.Azure.Quantum.dll"

open System.IO
open FSharp.Azure.Quantum
open FSharp.Azure.Quantum.GraphColoring
open FSharp.Azure.Quantum.Visualization

printfn "============================================"
printfn " Graph Coloring: Problem + Solution Viz"
printfn "============================================"
printfn ""

// ============================================================================
// Define the Problem
// ============================================================================

let registerAllocation = graphColoring {
    node "x" ["y"; "z"]
    node "y" ["x"; "w"]
    node "z" ["x"; "w"]
    node "w" ["y"; "z"]
    colors ["R0"; "R1"; "R2"; "R3"]
    objective MinimizeColors
}

// ============================================================================
// STEP 1: Visualize the PROBLEM (before solving)
// ============================================================================

printfn "=========================================="
printfn " STEP 1: Problem Visualization"
printfn "=========================================="
printfn ""
printfn "ASCII (Terminal):"
printfn "------------------"
printfn "%s" (registerAllocation.ToASCII())
printfn ""

printfn "Mermaid (GitHub/Docs):"
printfn "----------------------"
printfn "%s" (registerAllocation.ToMermaid())
printfn ""

// ============================================================================
// STEP 2: Solve the Problem
// ============================================================================

printfn "=========================================="
printfn " STEP 2: Solving..."
printfn "=========================================="
printfn ""

match GraphColoring.solve registerAllocation 4 None with
| Error err ->
    printfn "Error: %s" err.Message
| Ok solution ->
    printfn "✓ Solution Found!"
    printfn ""
    
    // ========================================================================
    // STEP 3: Visualize the SOLUTION (after solving)
    // ========================================================================
    
    printfn "=========================================="
    printfn " STEP 3: Solution Visualization"
    printfn "=========================================="
    printfn ""
    printfn "ASCII (Terminal):"
    printfn "------------------"
    printfn "%s" (solution.ToASCII())
    printfn ""
    
    printfn "Mermaid (GitHub/Docs):"
    printfn "----------------------"
    printfn "%s" (solution.ToMermaid())
    printfn ""
    
    // ========================================================================
    // STEP 4: Generate Documentation
    // ========================================================================
    
    printfn "=========================================="
    printfn " STEP 4: Generating Documentation"
    printfn "=========================================="
    printfn ""
    
    let problemMd = sprintf "# Register Allocation\n\n## Problem Definition\n\n%s\n\n%s" 
                             (registerAllocation.ToASCII()) 
                             (registerAllocation.ToMermaid())
    
    let solutionMd = sprintf "## Solution\n\n%s\n\n%s" 
                              (solution.ToASCII()) 
                              (solution.ToMermaid())
    
    let fullDoc = problemMd + "\n\n" + solutionMd
    
    File.WriteAllText("problem-and-solution.md", fullDoc)
    printfn "✓ Saved to: problem-and-solution.md"
    printfn ""

// ============================================================================
// Summary
// ============================================================================

printfn ""
printfn "=========================================="
printfn " Summary"
printfn "=========================================="
printfn ""
printfn "✓ Problem visualization: problem.ToMermaid() / problem.ToASCII()"
printfn "✓ Solution visualization: solution.ToMermaid() / solution.ToASCII()"
printfn "✓ Both work seamlessly together!"
printfn ""
printfn "This pattern works for:"
printfn "  - GraphColoring problems and solutions ✓"
printfn "  - Quantum circuits ✓"
printfn "  - (Coming soon) MaxCut, TSP, Knapsack, Portfolio, etc."
printfn ""
