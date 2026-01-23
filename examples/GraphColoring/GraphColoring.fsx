// ============================================================================
// Graph Coloring Examples - FSharp.Azure.Quantum
// ============================================================================
//
// This script demonstrates the quantum-first Graph Coloring API with multiple
// real-world use cases:
//
// 1. Register Allocation (compiler optimization)
// 2. Frequency Assignment (wireless network planning)
// 3. Exam Scheduling (university timetabling)
// 4. Simple Graph Coloring (basic K-coloring)
// 5. Comparison: Quantum vs Classical
//
// WHAT IS GRAPH COLORING:
// Assign colors to graph vertices such that no adjacent vertices share
// the same color, while minimizing the total number of colors used.
//
// WHY USE QUANTUM:
// - Quantum QAOA can explore color assignments in superposition
// - Better solutions for complex constraint graphs
// - Useful for NP-complete problems (chromatic number)
//
// ============================================================================

(*
===============================================================================
 Background Theory
===============================================================================

Graph k-coloring asks: can a graph G = (V, E) be colored with at most k colors
such that no two adjacent vertices share a color? Determining the chromatic
number χ(G) (minimum k for which this is possible) is NP-hard. Graph coloring
has extensive applications: register allocation in compilers (interference graph),
frequency assignment in wireless networks (avoid interference), exam scheduling
(avoid student conflicts), map coloring, and Sudoku solving.

The problem is formulated as QUBO by introducing binary variables xᵥ,c ∈ {0,1}
indicating vertex v has color c. Constraints ensure: (1) each vertex gets exactly
one color: Σc xᵥ,c = 1, and (2) adjacent vertices differ: xᵤ,c + xᵥ,c ≤ 1 for
edges (u,v). These constraints are converted to penalty terms in the objective
function, which QAOA minimizes. Finding the chromatic number requires testing
k = 1, 2, ... until a valid coloring exists.

Key Equations:
  - One-color constraint: (Σc xᵥ,c - 1)² = 0 for each vertex v
  - Edge constraint: Σc xᵤ,c·xᵥ,c = 0 for each edge (u,v)
  - QUBO objective: min Σᵥ A(Σc xᵥ,c - 1)² + Σ₍ᵤ,ᵥ₎∈E B·Σc xᵤ,c·xᵥ,c
  - Chromatic number: χ(G) = min{k : G is k-colorable}
  - Greedy upper bound: χ(G) ≤ Δ(G) + 1 where Δ is max degree

Quantum Advantage:
  Graph coloring's combinatorial explosion (kⁿ possible assignments for n vertices,
  k colors) makes it ideal for quantum speedup. QAOA explores colorings in
  superposition, with interference amplifying valid solutions. For sparse graphs
  with complex constraint structures (e.g., register allocation interference
  graphs), quantum approaches may find optimal or near-optimal colorings faster
  than classical branch-and-bound. Current demonstrations handle ~20 vertices;
  scaling to industrial compiler workloads requires fault-tolerant hardware.

References:
  [1] Garey & Johnson, "Computers and Intractability: A Guide to the Theory of
      NP-Completeness", W.H. Freeman (1979), Section 5.5.
  [2] Lucas, "Ising formulations of many NP problems", Front. Phys. 2, 5 (2014).
      https://doi.org/10.3389/fphy.2014.00005
  [3] Tabi et al., "Quantum Optimization for the Graph Coloring Problem with
      Polynomial Encoding", IEEE ICRC (2020). https://doi.org/10.1109/ICRC2020.2020.00006
  [4] Wikipedia: Graph_coloring
      https://en.wikipedia.org/wiki/Graph_coloring
*)

//#r "nuget: FSharp.Azure.Quantum"
#r "../../src/FSharp.Azure.Quantum/bin/Debug/net10.0/FSharp.Azure.Quantum.dll"


open FSharp.Azure.Quantum
open FSharp.Azure.Quantum.GraphColoring

// ============================================================================
// EXAMPLE 1: Register Allocation (Compiler Optimization)
// ============================================================================
//
// PROBLEM: Assign CPU registers to program variables such that variables
// that are "live" at the same time get different registers.
//
// REAL-WORLD IMPACT:
// - Fewer registers = faster code, smaller binary
// - Better register allocation = 5-10% performance gain in compiled code
//
// GRAPH MODEL:
// - Vertices = program variables
// - Edges = variable liveness conflicts (cannot share same register)
// - Colors = physical CPU registers (EAX, EBX, ECX, EDX)
//
printfn "========================================="
printfn "EXAMPLE 1: Register Allocation"
printfn "========================================="
printfn ""

// Define variables and their liveness conflicts
let registerProblem = graphColoring {
    // Variable R1 conflicts with R2 and R3 (live at same time)
    node "R1" ["R2"; "R3"]
    node "R2" ["R1"; "R4"]
    node "R3" ["R1"; "R4"]
    node "R4" ["R2"; "R3"]
    
    // Available CPU registers
    colors ["EAX"; "EBX"; "ECX"; "EDX"]
    objective MinimizeColors
}

printfn "Problem: Allocate 4 variables to CPU registers"
printfn "Conflicts: R1↔R2, R1↔R3, R2↔R4, R3↔R4"
printfn ""

// Solve with quantum simulation (default)
match GraphColoring.solve registerProblem 4 None with
| Ok solution ->
    printfn "✅ Quantum Solution:"
    printfn "   Used %d registers (chromatic number)" solution.ColorsUsed
    printfn "   Valid: %b | Conflicts: %d" solution.IsValid solution.ConflictCount
    printfn ""
    printfn "   Register Allocation:"
    for (var, register) in Map.toList solution.Assignments do
        printfn "     %s → %s" var register
    printfn ""
    printfn "   Register Usage:"
    for (register, count) in Map.toList solution.ColorDistribution do
        printfn "     %s: %d variables" register count
| Error err ->
    printfn "❌ Error: %s" err.Message

printfn ""

// ============================================================================
// EXAMPLE 2: Frequency Assignment (Wireless Network Planning)
// ============================================================================
//
// PROBLEM: Assign radio frequencies to cell towers such that nearby towers
// don't interfere with each other.
//
// REAL-WORLD IMPACT:
// - Minimize spectrum usage (save money on frequency licenses)
// - Avoid interference (better call quality)
// - Maximize network capacity
//
// GRAPH MODEL:
// - Vertices = cell towers
// - Edges = interference range (nearby towers)
// - Colors = radio frequencies (F1, F2, F3, F4)
//
printfn "========================================="
printfn "EXAMPLE 2: Frequency Assignment"
printfn "========================================="
printfn ""

// Create frequency assignment problem using graphColoring builder
let frequencyProblem = graphColoring {
    // Define cell towers and their interference conflicts (reduced to 3 towers)
    node "Tower1" ["Tower2"; "Tower3"]
    node "Tower2" ["Tower1"; "Tower3"]
    node "Tower3" ["Tower1"; "Tower2"]
    
    // Available frequencies
    colors ["F1"; "F2"; "F3"]
    objective MinimizeColors
}

printfn "Problem: Assign frequencies to 3 cell towers"
printfn "Interference pairs: 3 edges (complete triangle)"
printfn ""

match GraphColoring.solve frequencyProblem 3 None with
| Ok solution ->
    printfn "✅ Quantum Solution:"
    printfn "   Used %d frequencies" solution.ColorsUsed
    printfn "   Valid: %b | Conflicts: %d" solution.IsValid solution.ConflictCount
    printfn ""
    printfn "   Frequency Assignment:"
    for (tower, frequency) in Map.toList solution.Assignments do
        printfn "     %s → %s" tower frequency
    printfn ""
    printfn "   Frequency Usage:"
    for (frequency, count) in Map.toList solution.ColorDistribution do
        printfn "     %s: %d towers" frequency count
| Error err ->
    printfn "❌ Error: %s" err.Message

printfn ""

// ============================================================================
// EXAMPLE 3: Exam Scheduling (University Timetabling)
// ============================================================================
//
// PROBLEM: Schedule university exams so that students enrolled in multiple
// courses don't have exam conflicts.
//
// REAL-WORLD IMPACT:
// - Minimize exam periods (finish semester faster)
// - Avoid student conflicts (no two exams at same time for one student)
// - Better resource utilization (fewer exam rooms needed per period)
//
// GRAPH MODEL:
// - Vertices = exams
// - Edges = student enrollment conflicts (same students in both courses)
// - Colors = time slots (Morning1, Morning2, Afternoon1, Afternoon2)
//
printfn "========================================="
printfn "EXAMPLE 3: Exam Scheduling"
printfn "========================================="
printfn ""

// Create exam scheduling problem using graphColoring builder (reduced to 3 exams to fit 20 qubit limit)
let examProblem = graphColoring {
    // Define exams and their student enrollment conflicts
    node "Math101" ["CS101"; "Physics101"]
    node "CS101" ["Math101"]
    node "Physics101" ["Math101"]
    
    // Available time slots
    colors ["Morning"; "Afternoon"; "Evening"]
    objective MinimizeColors
}

printfn "Problem: Schedule 3 exams into time slots"
printfn "Student conflicts: 2 pairs"
printfn ""

match GraphColoring.solve examProblem 3 None with
| Ok solution ->
    printfn "✅ Quantum Solution:"
    printfn "   Used %d time slots" solution.ColorsUsed
    printfn "   Valid: %b | Conflicts: %d" solution.IsValid solution.ConflictCount
    printfn ""
    printfn "   Exam Schedule:"
    for (exam, timeSlot) in Map.toList solution.Assignments do
        printfn "     %s → %s" exam timeSlot
    printfn ""
    printfn "   Time Slot Usage:"
    for (timeSlot, count) in Map.toList solution.ColorDistribution do
        printfn "     %s: %d exams" timeSlot count
| Error err ->
    printfn "❌ Error: %s" err.Message

printfn ""

// ============================================================================
// EXAMPLE 4: Simple Graph Coloring (Basic K-Coloring)
// ============================================================================
//
// PROBLEM: Color a simple graph (Petersen graph - a classic example)
// with minimal colors.
//
// WHY INTERESTING:
// - Petersen graph has chromatic number = 3 (requires exactly 3 colors)
// - Classic benchmark for graph coloring algorithms
// - Tests algorithm effectiveness on well-studied graphs
//
printfn "========================================="
printfn "EXAMPLE 4: Cycle Graph Coloring"
printfn "========================================="
printfn ""

// Simple cycle graph (reduced from Petersen to fit 20 qubit limit)
// A 4-cycle requires 2 colors
let cycleGraph = graphColoring {
    node "V1" ["V2"; "V4"]
    node "V2" ["V1"; "V3"]
    node "V3" ["V2"; "V4"]
    node "V4" ["V3"; "V1"]
    
    colors ["Red"; "Green"; "Blue"]
    objective MinimizeColors
}

printfn "Problem: Color cycle graph (4 vertices, 4 edges)"
printfn "Known chromatic number: 2 colors"
printfn ""

match GraphColoring.solve cycleGraph 3 None with
| Ok solution ->
    printfn "✅ Quantum Solution:"
    printfn "   Used %d colors" solution.ColorsUsed
    printfn "   Valid: %b | Conflicts: %d" solution.IsValid solution.ConflictCount
    printfn ""
    printfn "   Vertex Coloring:"
    for (vertex, color) in Map.toList solution.Assignments do
        printfn "     %s → %s" vertex color
    printfn ""
    printfn "   Color Distribution:"
    for (color, count) in Map.toList solution.ColorDistribution do
        printfn "     %s: %d vertices" color count
| Error err ->
    printfn "❌ Error: %s" err.Message

printfn ""

// ============================================================================
// EXAMPLE 5: Complex Graph Coloring
// ============================================================================
//
// PROBLEM: Color a dense graph with 6 vertices to test quantum optimization
//
// WHY INTERESTING:
// - Dense connectivity makes coloring challenging
// - Demonstrates quantum QAOA on non-trivial problem
// - Shows objective function (minimize colors)
//
printfn "========================================="
printfn "EXAMPLE 5: Complex Dense Graph"
printfn "========================================="
printfn ""

let comparisonGraph = graphColoring {
    // 4-vertex graph with complex structure (reduced to fit qubit limit)
    node "A" ["B"; "C"]
    node "B" ["A"; "C"; "D"]
    node "C" ["A"; "B"; "D"]
    node "D" ["B"; "C"]
    
    colors ["Color1"; "Color2"; "Color3"]
    objective MinimizeColors
}

printfn "Problem: Color 4-vertex graph with dense edges"
printfn ""

// Quantum solution (LocalBackend simulation)
printfn "🔬 Quantum QAOA Solution:"
match GraphColoring.solve comparisonGraph 3 None with
| Ok solution ->
    printfn "   Colors used: %d" solution.ColorsUsed
    printfn "   Valid: %b | Conflicts: %d" solution.IsValid solution.ConflictCount
    printfn "   Backend: %s" solution.BackendName
    printfn "   Is Quantum: %b" solution.IsQuantum
| Error err ->
    printfn "   ❌ Error: %s" err.Message

printfn ""

// ============================================================================
// EXAMPLE 6: Advanced - Fixed Colors (Pre-coloring Constraints)
// ============================================================================
//
// PROBLEM: Some vertices have pre-assigned colors (e.g., some registers
// already allocated), and we must color the rest accordingly.
//
// USE CASE:
// - Incremental compilation (some code already compiled)
// - Partial solutions (some assignments already made)
// - Constraints from external systems
//
printfn "========================================="
printfn "EXAMPLE 6: Pre-colored Vertices"
printfn "========================================="
printfn ""

let precoloredProblem = graphColoring {
    // R1 is pre-assigned to EAX (fixed)
    nodes [
        coloredNode {
            nodeId "R1"
            conflictsWith ["R2"; "R3"]
            fixedColor "EAX"  // Pre-assigned
        }
    ]
    
    // Other variables with normal conflicts
    node "R2" ["R1"; "R4"]
    node "R3" ["R1"; "R4"]
    node "R4" ["R2"; "R3"]
    
    colors ["EAX"; "EBX"; "ECX"; "EDX"]
    objective MinimizeColors
}

printfn "Problem: R1 is pre-assigned to EAX, color the rest"
printfn ""

match GraphColoring.solve precoloredProblem 4 None with
| Ok solution ->
    printfn "✅ Solution with fixed color:"
    printfn "   Register Allocation:"
    for (var, register) in Map.toList solution.Assignments do
        let marker = if var = "R1" then " (fixed)" else ""
        printfn "     %s → %s%s" var register marker
| Error err ->
    printfn "❌ Error: %s" err.Message

printfn ""

// ============================================================================
// SUMMARY
// ============================================================================

printfn "========================================="
printfn "SUMMARY"
printfn "========================================="
printfn ""
printfn "Graph Coloring Examples Completed:"
printfn "  1. ✅ Register Allocation (compiler)"
printfn "  2. ✅ Frequency Assignment (wireless)"
printfn "  3. ✅ Exam Scheduling (university)"
printfn "  4. ✅ Cycle Graph (2-colorable)"
printfn "  5. ✅ Complex Dense Graph (quantum QAOA)"
printfn "  6. ✅ Pre-colored vertices (constraints)"
printfn ""
printfn "KEY TAKEAWAYS:"
printfn "  • Graph coloring is NP-complete (chromatic number)"
printfn "  • Quantum QAOA explores superposition of colorings"
printfn "  • Real-world applications: compilers, networks, scheduling"
printfn "  • Quantum-first API with LocalBackend simulation by default"
printfn "  • Computation expression builder for easy problem definition"
printfn ""
printfn "NEXT STEPS:"
printfn "  • Try with cloud quantum backends (IonQ, Rigetti)"
printfn "  • Experiment with larger graphs (10-15 vertices)"
printfn "  • Explore BalanceColors objective for load balancing"
printfn "  • Add custom constraints with AvoidColors and Priority"
printfn ""
