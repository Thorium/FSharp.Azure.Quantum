#!/usr/bin/env dotnet fsi
// ============================================================================
// QUBO Matrix Visualization - See the Internal Representation
// ============================================================================
// 
// This example demonstrates how to visualize the QUBO (Quadratic Unconstrained
// Binary Optimization) matrix that represents the internal mathematical
// formulation of optimization problems.
//
// WHAT IS QUBO?
// - QUBO converts discrete optimization problems into quadratic form
// - Used by quantum annealers and classical optimization solvers
// - Matrix coefficients represent objective and constraints
//
// WHY VISUALIZE QUBO?
// - Debug problem formulations
// - Understand variable interactions
// - Verify constraint encoding
// - Educational purposes
//
// ============================================================================

#I "../../src/FSharp.Azure.Quantum/bin/Debug/net10.0"
#r "FSharp.Azure.Quantum.dll"

open System
open FSharp.Azure.Quantum
open FSharp.Azure.Quantum.GraphColoring
open FSharp.Azure.Quantum.Visualization

printfn "============================================"
printfn " QUBO Matrix Visualization"
printfn "============================================"
printfn ""

// ============================================================================
// Example: Graph Coloring Problem → QUBO
// ============================================================================

printfn "=== Graph Coloring QUBO Visualization ==="
printfn ""

let coloringProblem = graphColoring {
    node "A" ["B"; "C"]
    node "B" ["A"]
    node "C" ["A"]
    colors ["Red"; "Blue"]
    objective MinimizeColors
}

printfn "Step 1: Original Problem"
printfn "------------------------"
printfn "%s" (coloringProblem.ToASCII())

// Convert to quantum solver problem format  
let quantumProblem : Quantum.QuantumGraphColoringSolver.GraphColoringProblem = {
    Vertices = ["A"; "B"; "C"]
    Edges = [
        GraphOptimization.edge "A" "B" 1.0 |> fun e -> { e with Value = Some () }
        GraphOptimization.edge "A" "C" 1.0 |> fun e -> { e with Value = Some () }
    ]
    NumColors = 2
    FixedColors = Map.empty
}

// Generate QUBO matrix
match Quantum.QuantumGraphColoringSolver.toQubo quantumProblem 10.0 with
| Error err -> 
    printfn "Error generating QUBO: %A" err
| Ok (quboMatrix, variableMap) ->
    printfn ""
    printfn "Step 2: Variable Mapping"
    printfn "-------------------------"
    printfn "QUBO uses binary variables to encode color assignments:"
    printfn ""
    variableMap
    |> Map.toList
    |> List.sortBy fst
    |> List.iter (fun (idx, (vertex, color)) ->
        printfn "  x_%d = 1  means  vertex '%s' has color %d" idx vertex color)
    printfn ""
    
    printfn "Step 3: QUBO Matrix (ASCII)"
    printfn "----------------------------"
    printfn "%s" (quboMatrix.ToASCII())
    
    printfn "Step 4: QUBO Matrix (Mermaid)"
    printfn "------------------------------"
    printfn "%s" (quboMatrix.ToMermaid())
    printfn ""

// ============================================================================
// Understanding QUBO Structure
// ============================================================================

printfn ""
printfn "=========================================="
printfn " Understanding QUBO Structure"
printfn "=========================================="
printfn ""
printfn "QUBO Matrix Interpretation:"
printfn "---------------------------"
printfn "• Linear terms (diagonal, Q[i,i]):"
printfn "    Affects single variable x_i"
printfn "    Example: Q[0,0] = -5 means preferring x_0 = 1 lowers energy"
printfn ""
printfn "• Quadratic terms (off-diagonal, Q[i,j]):"
printfn "    Affects interaction x_i * x_j"
printfn "    Example: Q[0,1] = 10 means x_0 = x_1 = 1 increases energy (penalty)"
printfn ""
printfn "Coefficient Signs:"
printfn "------------------"
printfn "• Positive: Penalize (prefer variable = 0)"
printfn "• Negative: Reward (prefer variable = 1)"
printfn ""
printfn "In Graph Coloring QUBO:"
printfn "-----------------------"
printfn "1. Large positive off-diagonal terms:"
printfn "   → Penalty for adjacent nodes with same color"
printfn "   → Ensures valid coloring (hard constraint)"
printfn ""
printfn "2. Negative diagonal terms (optional):"
printfn "   → Reward for using fewer colors"
printfn "   → Objective function (soft constraint)"
printfn ""
printfn "3. One-hot encoding constraints:"
printfn "   → Each node must have exactly ONE color"
printfn "   → Enforced by additional penalty terms"
printfn ""

// ============================================================================
// Complete Visualization Pipeline
// ============================================================================

printfn ""
printfn "=========================================="
printfn " Complete Visualization Pipeline"
printfn "=========================================="
printfn ""
printfn "1. Problem → problem.ToMermaid()"
printfn "   Shows: Graph structure, conflicts"
printfn ""
printfn "2. QUBO → quboMatrix.ToMermaid()"
printfn "   Shows: Variable interactions, coefficients"
printfn ""
printfn "3. Circuit → circuit.ToMermaid()"
printfn "   Shows: Quantum gates, qubit operations"
printfn ""
printfn "4. Solution → solution.ToMermaid()"
printfn "   Shows: Final color assignments"
printfn ""
printfn "This gives COMPLETE transparency from problem to solution!"
printfn ""
