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

#r "nuget: FSharp.Azure.Quantum"

open FSharp.Azure.Quantum

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
| Error msg ->
    printfn "❌ Error: %s" msg

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
    // Define cell towers and their interference conflicts
    node "Tower1" ["Tower2"; "Tower3"]
    node "Tower2" ["Tower1"; "Tower4"]
    node "Tower3" ["Tower1"; "Tower4"; "Tower5"]
    node "Tower4" ["Tower2"; "Tower3"; "Tower5"]
    node "Tower5" ["Tower3"; "Tower4"]
    
    // Available frequencies
    colors ["F1"; "F2"; "F3"; "F4"; "F5"]
    objective MinimizeColors
}

printfn "Problem: Assign frequencies to 5 cell towers"
printfn "Interference pairs: 6 edges"
printfn ""

match GraphColoring.solve frequencyProblem 5 None with
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
| Error msg ->
    printfn "❌ Error: %s" msg

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

// Create exam scheduling problem using graphColoring builder
let examProblem = graphColoring {
    // Define exams and their student enrollment conflicts
    node "Math101" ["CS101"; "Physics101"]
    node "CS101" ["Math101"; "Physics101"]
    node "Physics101" ["Math101"; "CS101"; "Chem101"]
    node "Chem101" ["Physics101"; "English101"]
    node "English101" ["Chem101"]
    
    // Available time slots
    colors ["Morning1"; "Morning2"; "Afternoon1"; "Afternoon2"; "Evening"]
    objective MinimizeColors
}

printfn "Problem: Schedule 5 exams into time slots"
printfn "Student conflicts: 5 pairs"
printfn ""

match GraphColoring.solve examProblem 5 None with
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
| Error msg ->
    printfn "❌ Error: %s" msg

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
printfn "EXAMPLE 4: Petersen Graph Coloring"
printfn "========================================="
printfn ""

// Petersen graph: 10 vertices, chromatic number = 3
let petersenGraph = graphColoring {
    // Outer pentagon
    node "V1" ["V2"; "V5"; "V6"]
    node "V2" ["V1"; "V3"; "V7"]
    node "V3" ["V2"; "V4"; "V8"]
    node "V4" ["V3"; "V5"; "V9"]
    node "V5" ["V4"; "V1"; "V10"]
    
    // Inner pentagram
    node "V6" ["V1"; "V8"; "V9"]
    node "V7" ["V2"; "V9"; "V10"]
    node "V8" ["V3"; "V6"; "V10"]
    node "V9" ["V4"; "V6"; "V7"]
    node "V10" ["V5"; "V7"; "V8"]
    
    colors ["Red"; "Green"; "Blue"; "Yellow"]
    objective MinimizeColors
}

printfn "Problem: Color Petersen graph (10 vertices, 15 edges)"
printfn "Known chromatic number: 3 colors"
printfn ""

match GraphColoring.solve petersenGraph 4 None with
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
| Error msg ->
    printfn "❌ Error: %s" msg

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
    // 6-vertex graph with complex structure
    node "A" ["B"; "C"; "E"]
    node "B" ["A"; "C"; "D"; "F"]
    node "C" ["A"; "B"; "D"; "E"]
    node "D" ["B"; "C"; "E"; "F"]
    node "E" ["A"; "C"; "D"; "F"]
    node "F" ["B"; "D"; "E"]
    
    colors ["Color1"; "Color2"; "Color3"; "Color4"]
    objective MinimizeColors
}

printfn "Problem: Color 6-vertex graph with dense edges"
printfn ""

// Quantum solution (LocalBackend simulation)
printfn "🔬 Quantum QAOA Solution:"
match GraphColoring.solve comparisonGraph 4 None with
| Ok solution ->
    printfn "   Colors used: %d" solution.ColorsUsed
    printfn "   Valid: %b | Conflicts: %d" solution.IsValid solution.ConflictCount
    printfn "   Backend: %s" solution.BackendName
    printfn "   Is Quantum: %b" solution.IsQuantum
| Error msg ->
    printfn "   ❌ Error: %s" msg

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
| Error msg ->
    printfn "❌ Error: %s" msg

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
printfn "  4. ✅ Petersen Graph (classic problem)"
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
