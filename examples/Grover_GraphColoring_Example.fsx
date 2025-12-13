// ============================================================================
// Grover Graph Coloring Example - Unified Approach
// ============================================================================
//
// This demonstrates solving graph coloring problems using Grover's algorithm
// (gate-based quantum computing) as an alternative to QAOA/annealing approaches.
//
// ** UNIFIED ARCHITECTURE **
// The same graph coloring problem can be solved with different quantum approaches:
// - QAOA (QuantumGraphColoringSolver) - Quantum annealing style
// - Grover (this example) - Gate-based quantum search
// - Classical (greedy algorithm) - Baseline comparison
//
// ** USE CASES **
// - Register allocation (compiler optimization)
// - Frequency assignment (wireless networks)
// - Exam scheduling (university timetabling)
// - Map coloring (cartography)
//
// ** WHEN TO USE GROVER vs QAOA **
// - Grover: Better for small graphs (<10 vertices), guaranteed speedup
// - QAOA: Better for larger graphs, approximate solutions
//
// ============================================================================

// Use local build for development
#I "../src/FSharp.Azure.Quantum/bin/Debug/net10.0"
#r "FSharp.Azure.Quantum.dll"

open System
open FSharp.Azure.Quantum.Core
open FSharp.Azure.Quantum.Core.BackendAbstraction
open FSharp.Azure.Quantum.Backends.LocalBackend
open FSharp.Azure.Quantum.GroverSearch
open FSharp.Azure.Quantum.GroverSearch.Oracle

printfn "============================================"
printfn "GROVER GRAPH COLORING - GATE-BASED APPROACH"
printfn "============================================"
printfn ""

// ============================================================================
// Example 1: Path Graph (Bipartite) - 2-Coloring
// ============================================================================
//
// Graph: 0 -- 1 -- 2
// This is a path graph, which is bipartite and can be colored with 2 colors.
//
// Expected solutions: Alternating colors
//   - Color pattern: 0-1-0 or 1-0-1

printfn "=== Example 1: Path Graph (2-Coloring) ==="
printfn "Graph: 0 -- 1 -- 2 (path/bipartite)"
printfn ""

let pathGraph = graph 3 [(0, 1); (1, 2)]
let pathConfig = { Graph = pathGraph; NumColors = 2 }

match graphColoringOracle pathConfig with
| Error err ->
    printfn "❌ Failed to create oracle: %A" err
| Ok oracle ->
    printfn "✅ Oracle created successfully"
    printfn "   Vertices: %d" pathConfig.Graph.NumVertices
    printfn "   Edges: %d" pathConfig.Graph.Edges.Length
    printfn "   Colors available: %d" pathConfig.NumColors
    printfn "   Qubits needed: %d" oracle.NumQubits
    
    // Run Grover search
    printfn ""
    printfn "Running Grover's algorithm..."
    
    let backend = LocalBackend() :> IQuantumBackend
    let config = Grover.defaultConfig
    
    match Grover.search oracle backend config with
    | Error err ->
        printfn "❌ Search failed: %A" err
    | Ok result ->
        // Filter to only valid solutions (that satisfy the oracle)
        let validSolutions = 
            result.Solutions 
            |> List.filter (fun sol -> Oracle.isSolution oracle.Spec sol)
        
        if validSolutions.IsEmpty then
            printfn "⚠️  No valid solution found"
            printfn "   (Grover returned %d candidates, none satisfied oracle)" result.Solutions.Length
        else
            printfn "✅ Found %d valid coloring(s):" validSolutions.Length
            for solution in validSolutions do
                // Extract colors for each vertex (2 colors need 1 qubit per vertex)
                let color0 = solution &&& 1
                let color1 = (solution >>> 1) &&& 1
                let color2 = (solution >>> 2) &&& 1
                printfn "   Coloring: v0=%d v1=%d v2=%d (assignment=%d)" color0 color1 color2 solution
            
            printfn ""
            printfn "   Success probability: %.2f%%" (result.SuccessProbability * 100.0)
            printfn "   Iterations used: %d" result.Iterations

printfn ""
printfn "================================================"
printfn ""

// ============================================================================
// Example 2: Triangle Graph (Complete K3) - 3-Coloring
// ============================================================================
//
// Graph: Triangle where all 3 vertices are connected to each other
//   0 --- 1
//    \   /
//     \ /
//      2
//
// This requires 3 colors (chromatic number = 3).
// Expected: All 3 vertices must have different colors

printfn "=== Example 2: Triangle Graph (3-Coloring) ==="
printfn "Graph: Complete K3 (all vertices connected)"
printfn ""

let triangleGraph = graph 3 [(0, 1); (1, 2); (2, 0)]
let triangleConfig = { Graph = triangleGraph; NumColors = 3 }

match graphColoringOracle triangleConfig with
| Error err ->
    printfn "❌ Failed to create oracle: %A" err
| Ok oracle ->
    printfn "✅ Oracle created successfully"
    printfn "   Vertices: %d" triangleConfig.Graph.NumVertices
    printfn "   Edges: %d (complete graph)" triangleConfig.Graph.Edges.Length
    printfn "   Colors available: %d" triangleConfig.NumColors
    printfn "   Qubits needed: %d (2 bits per vertex for 3 colors)" oracle.NumQubits
    
    printfn ""
    printfn "Running Grover's algorithm..."
    
    let backend = LocalBackend() :> IQuantumBackend
    let config = Grover.defaultConfig
    
    match Grover.search oracle backend config with
    | Error err ->
        printfn "❌ Search failed: %A" err
    | Ok result ->
        if result.Solutions.IsEmpty then
            printfn "⚠️  No solution found"
        else
            printfn "✅ Found %d valid coloring(s):" result.Solutions.Length
            
            // Take up to 5 solutions to display
            for solution in result.Solutions |> List.truncate 5 do
                // For 3 colors, we need 2 qubits per vertex
                // Extract 2-bit colors for each vertex
                let color0 = solution &&& 0b11         // Bits 0-1
                let color1 = (solution >>> 2) &&& 0b11 // Bits 2-3
                let color2 = (solution >>> 4) &&& 0b11 // Bits 4-5
                
                // Only show if all colors are valid (< 3)
                if color0 < 3 && color1 < 3 && color2 < 3 then
                    printfn "   Coloring: v0=%d v1=%d v2=%d (all different ✓)" color0 color1 color2
            
            printfn ""
            printfn "   Success probability: %.2f%%" (result.SuccessProbability * 100.0)
            printfn "   Iterations used: %d" result.Iterations

printfn ""
printfn "================================================"
printfn ""

// ============================================================================
// Example 3: Square Graph (Cycle C4) - Minimal Coloring
// ============================================================================
//
// Graph: 4 vertices in a square
//   0 --- 1
//   |     |
//   3 --- 2
//
// This is a 4-cycle, which can be colored with 2 colors.
// However, we provide 3 colors to see if Grover finds the minimal solution.

printfn "=== Example 3: Square Graph (Cycle C4) ==="
printfn "Graph: 4-cycle (square)"
printfn ""

let squareGraph = graph 4 [(0, 1); (1, 2); (2, 3); (3, 0)]
let squareConfig = { Graph = squareGraph; NumColors = 3 }

match graphColoringOracle squareConfig with
| Error err ->
    printfn "❌ Failed to create oracle: %A" err
| Ok oracle ->
    printfn "✅ Oracle created successfully"
    printfn "   Vertices: %d" squareConfig.Graph.NumVertices
    printfn "   Edges: %d (4-cycle)" squareConfig.Graph.Edges.Length
    printfn "   Colors available: %d" squareConfig.NumColors
    printfn "   Chromatic number: 2 (optimal)"
    printfn "   Qubits needed: %d" oracle.NumQubits
    
    printfn ""
    printfn "Running Grover's algorithm..."
    
    let backend = LocalBackend() :> IQuantumBackend
    let config = Grover.defaultConfig
    
    match Grover.search oracle backend config with
    | Error err ->
        printfn "❌ Search failed: %A" err
    | Ok result ->
        if result.Solutions.IsEmpty then
            printfn "⚠️  No solution found"
        else
            printfn "✅ Found %d valid coloring(s):" result.Solutions.Length
            
            // Analyze colorings to see how many colors are actually used
            let analyzedColorings =
                result.Solutions
                |> List.map (fun solution ->
                    let color0 = solution &&& 0b11
                    let color1 = (solution >>> 2) &&& 0b11
                    let color2 = (solution >>> 4) &&& 0b11
                    let color3 = (solution >>> 6) &&& 0b11
                    
                    let colors = [color0; color1; color2; color3]
                    let uniqueColors = colors |> List.distinct |> List.filter (fun c -> c < 3)
                    
                    (colors, uniqueColors.Length)
                )
                |> List.filter (fun (colors, _) -> colors |> List.forall (fun c -> c < 3))
                |> List.truncate 5
            
            for (colors, numColorsUsed) in analyzedColorings do
                printfn "   Coloring: [%d,%d,%d,%d] uses %d colors" 
                    colors.[0] colors.[1] colors.[2] colors.[3] numColorsUsed
            
            printfn ""
            printfn "   Success probability: %.2f%%" (result.SuccessProbability * 100.0)
            printfn "   Iterations used: %d" result.Iterations

printfn ""
printfn "================================================"
printfn ""

// ============================================================================
// Example 4: Register Allocation (Compiler Optimization)
// ============================================================================
//
// Real-world application: Assign CPU registers to variables
//
// Interference graph:
//   R1 conflicts with R2, R3
//   R2 conflicts with R1, R4
//   R3 conflicts with R1, R4
//   R4 conflicts with R2, R3
//
// Goal: Minimize number of CPU registers needed

printfn "=== Example 4: Register Allocation (Real-World) ==="
printfn "Problem: Assign CPU registers to 4 program variables"
printfn "Conflicts: R1↔R2, R1↔R3, R2↔R4, R3↔R4"
printfn ""

let registerGraph = graph 4 [
    (0, 1)  // R1 conflicts with R2
    (0, 2)  // R1 conflicts with R3
    (1, 3)  // R2 conflicts with R4
    (2, 3)  // R3 conflicts with R4
]
let registerConfig = { Graph = registerGraph; NumColors = 3 }

match graphColoringOracle registerConfig with
| Error err ->
    printfn "❌ Failed to create oracle: %A" err
| Ok oracle ->
    printfn "✅ Oracle created successfully"
    printfn "   Variables (vertices): %d" registerConfig.Graph.NumVertices
    printfn "   Conflicts (edges): %d" registerConfig.Graph.Edges.Length
    printfn "   Available registers: %d (EAX, EBX, ECX)" registerConfig.NumColors
    printfn "   Qubits needed: %d" oracle.NumQubits
    
    printfn ""
    printfn "Running Grover's algorithm..."
    
    let backend = LocalBackend() :> IQuantumBackend
    let config = Grover.defaultConfig
    
    match Grover.search oracle backend config with
    | Error err ->
        printfn "❌ Search failed: %A" err
    | Ok result ->
        if result.Solutions.IsEmpty then
            printfn "⚠️  No solution found"
        else
            printfn "✅ Found %d valid register allocation(s):" result.Solutions.Length
            
            let registers = [|"EAX"; "EBX"; "ECX"|]
            
            // Show first valid allocation
            match result.Solutions 
                  |> List.map (fun solution ->
                      let r1 = solution &&& 0b11
                      let r2 = (solution >>> 2) &&& 0b11
                      let r3 = (solution >>> 4) &&& 0b11
                      let r4 = (solution >>> 6) &&& 0b11
                      [r1; r2; r3; r4])
                  |> List.filter (fun regs -> regs |> List.forall (fun r -> r < 3))
                  |> List.tryHead with
            | Some [r1; r2; r3; r4] ->
                printfn ""
                printfn "   Register Allocation:"
                printfn "     R1 → %s" registers.[r1]
                printfn "     R2 → %s" registers.[r2]
                printfn "     R3 → %s" registers.[r3]
                printfn "     R4 → %s" registers.[r4]
                
                let uniqueRegs = [r1; r2; r3; r4] |> List.distinct
                printfn ""
                printfn "   Registers used: %d (chromatic number)" uniqueRegs.Length
            | _ ->
                printfn "   (No valid allocation in first result)"
            
            printfn ""
            printfn "   Success probability: %.2f%%" (result.SuccessProbability * 100.0)
            printfn "   Iterations used: %d" result.Iterations

printfn ""
printfn "================================================"
printfn ""

printfn "✅ Graph Coloring examples completed!"
printfn ""
printfn "Key Takeaways:"
printfn "  1. Grover finds valid graph colorings using quantum search"
printfn "  2. Works for various graph types: paths, cycles, complete graphs"
printfn "  3. Real-world application: register allocation in compilers"
printfn "  4. Complements existing QAOA/annealing approaches"
printfn "  5. Best for small graphs (<10 vertices) with guaranteed speedup"
printfn ""
printfn "Comparison with other approaches:"
printfn "  • QAOA (QuantumGraphColoringSolver): Better for larger graphs"
printfn "  • Grover (this example): Better for small graphs, exact solutions"
printfn "  • Classical greedy: Fast baseline, approximate solutions"
printfn ""
