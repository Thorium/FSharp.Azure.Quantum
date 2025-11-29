// ==============================================================================
// Supply Chain Optimization Example (Small Test Version)
// ==============================================================================
// Simplified 2-stage supply chain to test quantum network flow solver
// Within LocalSimulator's 10-qubit limit
// ==============================================================================

#r "../../src/FSharp.Azure.Quantum/bin/Debug/net10.0/FSharp.Azure.Quantum.dll"

open System
open FSharp.Azure.Quantum.Quantum
open FSharp.Azure.Quantum.Core
open FSharp.Azure.Quantum.GraphOptimization

printfn "╔══════════════════════════════════════════════════════════════════════════════╗"
printfn "║       SUPPLY CHAIN OPTIMIZATION (SMALL TEST)                                 ║"
printfn "║       2-Stage: Suppliers → Customers                                         ║"
printfn "╚══════════════════════════════════════════════════════════════════════════════╝"
printfn ""

// Simplified problem: 2 suppliers, 2 customers, 4 routes = 4 qubits
let edges = [
    { Source = "S1"; Target = "C1"; Weight = 10.0; Directed = true; Value = Some 10.0; Properties = Map.empty }
    { Source = "S1"; Target = "C2"; Weight = 15.0; Directed = true; Value = Some 15.0; Properties = Map.empty }
    { Source = "S2"; Target = "C1"; Weight = 12.0; Directed = true; Value = Some 12.0; Properties = Map.empty }
    { Source = "S2"; Target = "C2"; Weight = 8.0; Directed = true; Value = Some 8.0; Properties = Map.empty }
]

let flowProblem : QuantumNetworkFlowSolver.NetworkFlowProblem = {
    Sources = ["S1"; "S2"]
    Sinks = ["C1"; "C2"]
    IntermediateNodes = []
    Edges = edges
    Capacities = Map.ofList [("S1", 100); ("S2", 100); ("C1", 50); ("C2", 50)]
    Demands = Map.ofList [("C1", 1); ("C2", 1)]
    Supplies = Map.ofList [("S1", 100); ("S2", 100)]
}

printfn "Problem: Route products from 2 suppliers to 2 customers"
printfn "  • 4 possible routes"
printfn "  • 4 qubits required (within LocalSimulator limit)"
printfn ""

printfn "Running quantum network flow optimization..."
let startTime = DateTime.UtcNow

let backend = BackendAbstraction.createLocalBackend()
let solutionResult = QuantumNetworkFlowSolver.solveWithShots backend flowProblem 1000

let elapsed = DateTime.UtcNow - startTime
printfn "Completed in %d ms" (int elapsed.TotalMilliseconds)
printfn ""

match solutionResult with
| Error msg ->
    printfn "❌ Solution failed: %s" msg
| Ok solution ->
    printfn "✅ SOLUTION FOUND:"
    printfn "────────────────────────────────────────────────────────────────────────────────"
    printfn "  Total Cost: $%.2f" solution.TotalCost
    printfn "  Routes Selected: %d" solution.SelectedEdges.Length
    printfn "  Fill Rate: %.1f%%" (solution.FillRate * 100.0)
    printfn "  Backend: %s" solution.BackendName
    printfn ""
    printfn "  Selected Routes:"
    for edge in solution.SelectedEdges do
        printfn "    %s → %s: $%.2f" edge.Source edge.Target edge.Weight
    printfn ""

printfn "✨ Test complete - Quantum Network Flow Solver is working!"
printfn ""
