// ==============================================================================
// Supply Chain Optimization Example
// ==============================================================================
// Demonstrates multi-stage supply chain optimization using the FSharp.Azure.Quantum
// QuantumNetworkFlowSolver to minimize total logistics cost while meeting demand.
//
// Business Context:
// A logistics company operates a 4-stage supply chain: suppliers → warehouses →
// distributors → customers. The goal is to route products through the network
// to meet all customer demand at minimum total cost.
//
// This example shows:
// - Multi-stage network flow optimization using QUBO encoding
// - Quantum-ready min-cost flow solver (Rule 1 compliant)
// - Capacity-constrained routing with flow conservation
// - Cost minimization with quantum backend support
// - Supply chain performance analysis
// ==============================================================================

#r "../../src/FSharp.Azure.Quantum/bin/Debug/net10.0/FSharp.Azure.Quantum.dll"

open System
open FSharp.Azure.Quantum.Quantum
open FSharp.Azure.Quantum.Core
open FSharp.Azure.Quantum.GraphOptimization

// ==============================================================================
// DOMAIN MODEL - Supply Chain Types
// ==============================================================================

/// Supply chain node with capacity and operating cost
type SupplyChainNode = {
    Id: string
    NodeType: string  // "Supplier", "Warehouse", "Distributor", "Customer"
    Capacity: int       // Maximum units that can flow through
    OperatingCost: float  // Cost per unit processed
    Demand: int option  // For customers only
    Revenue: float option  // For customers only
}

/// Complete supply chain solution
type SupplyChainSolution = {
    SelectedRoutes: Edge<float> list
    TotalCost: float
    TotalRevenue: float
    Profit: float
    DemandFulfilled: int
    TotalDemand: int
    FillRate: float
}

// ==============================================================================
// SUPPLY CHAIN DATA - Realistic logistics network
// ==============================================================================

let supplyChainNodes = [
    // Suppliers
    { Id = "S1_Shanghai"; NodeType = "Supplier"; Capacity = 1000; OperatingCost = 100.0; Demand = None; Revenue = None }
    { Id = "S2_Mumbai"; NodeType = "Supplier"; Capacity = 800; OperatingCost = 90.0; Demand = None; Revenue = None }
    
    // Warehouses
    { Id = "W1_Singapore"; NodeType = "Warehouse"; Capacity = 1200; OperatingCost = 50.0; Demand = None; Revenue = None }
    { Id = "W2_Dubai"; NodeType = "Warehouse"; Capacity = 900; OperatingCost = 45.0; Demand = None; Revenue = None }
    
    // Distributors
    { Id = "D1_London"; NodeType = "Distributor"; Capacity = 800; OperatingCost = 30.0; Demand = None; Revenue = None }
    { Id = "D2_Frankfurt"; NodeType = "Distributor"; Capacity = 700; OperatingCost = 35.0; Demand = None; Revenue = None }
    
    // Customers
    { Id = "C1_Paris"; NodeType = "Customer"; Capacity = 0; OperatingCost = 0.0; Demand = Some 400; Revenue = Some 200.0 }
    { Id = "C2_Berlin"; NodeType = "Customer"; Capacity = 0; OperatingCost = 0.0; Demand = Some 500; Revenue = Some 220.0 }
    { Id = "C3_Amsterdam"; NodeType = "Customer"; Capacity = 0; OperatingCost = 0.0; Demand = Some 350; Revenue = Some 180.0 }
]

let transportRoutes = [
    // Suppliers → Warehouses (ocean freight)
    ("S1_Shanghai", "W1_Singapore", 20.0)
    ("S1_Shanghai", "W2_Dubai", 25.0)
    ("S2_Mumbai", "W1_Singapore", 22.0)
    ("S2_Mumbai", "W2_Dubai", 18.0)
    
    // Warehouses → Distributors (air freight)
    ("W1_Singapore", "D1_London", 15.0)
    ("W1_Singapore", "D2_Frankfurt", 18.0)
    ("W2_Dubai", "D1_London", 16.0)
    ("W2_Dubai", "D2_Frankfurt", 14.0)
    
    // Distributors → Customers (ground shipping)
    ("D1_London", "C1_Paris", 10.0)
    ("D1_London", "C2_Berlin", 12.0)
    ("D1_London", "C3_Amsterdam", 11.0)
    ("D2_Frankfurt", "C1_Paris", 11.0)
    ("D2_Frankfurt", "C2_Berlin", 10.0)
    ("D2_Frankfurt", "C3_Amsterdam", 13.0)
]

// ==============================================================================
// PROBLEM SETUP - Using QuantumNetworkFlowSolver
// ==============================================================================

printfn "╔══════════════════════════════════════════════════════════════════════════════╗"
printfn "║                SUPPLY CHAIN OPTIMIZATION EXAMPLE                             ║"
printfn "║              Using QuantumNetworkFlowSolver (Rule 1 Compliant)              ║"
printfn "╚══════════════════════════════════════════════════════════════════════════════╝"
printfn ""

let totalSupply =
    supplyChainNodes
    |> List.filter (fun n -> n.NodeType = "Supplier")
    |> List.sumBy (fun n -> n.Capacity)

let totalDemand =
    supplyChainNodes
    |> List.filter (fun n -> n.NodeType = "Customer")
    |> List.sumBy (fun n -> n.Demand |> Option.defaultValue 0)

let supplierCount = supplyChainNodes |> List.filter (fun n -> n.NodeType = "Supplier") |> List.length
let warehouseCount = supplyChainNodes |> List.filter (fun n -> n.NodeType = "Warehouse") |> List.length
let distributorCount = supplyChainNodes |> List.filter (fun n -> n.NodeType = "Distributor") |> List.length
let customerCount = supplyChainNodes |> List.filter (fun n -> n.NodeType = "Customer") |> List.length

printfn "Problem: Route %d units through 4-stage supply chain" totalDemand
printfn "  • Suppliers: %d (capacity: %d units)" supplierCount totalSupply
printfn "  • Warehouses: %d" warehouseCount
printfn "  • Distributors: %d" distributorCount
printfn "  • Customers: %d (demand: %d units)" customerCount totalDemand
printfn "Objective: Minimize total cost while meeting demand"
printfn ""

// Convert to network flow problem specification
let sources = supplyChainNodes |> List.filter (fun n -> n.NodeType = "Supplier") |> List.map (fun n -> n.Id)
let sinks = supplyChainNodes |> List.filter (fun n -> n.NodeType = "Customer") |> List.map (fun n -> n.Id)
let intermediateNodes = 
    supplyChainNodes 
    |> List.filter (fun n -> n.NodeType = "Warehouse" || n.NodeType = "Distributor")
    |> List.map (fun n -> n.Id)

// Build capacity map
let capacities =
    supplyChainNodes
    |> List.map (fun n -> n.Id, n.Capacity)
    |> Map.ofList

// Build demand map
let demands =
    supplyChainNodes
    |> List.choose (fun n -> n.Demand |> Option.map (fun d -> n.Id, d))
    |> Map.ofList

// Build supply map
let supplies =
    supplyChainNodes
    |> List.filter (fun n -> n.NodeType = "Supplier")
    |> List.map (fun n -> n.Id, n.Capacity)
    |> Map.ofList

// Convert routes to edges
let edges =
    transportRoutes
    |> List.map (fun (from, to_, cost) ->
        { 
            Source = from
            Target = to_
            Weight = cost
            Directed = true
            Value = Some cost
            Properties = Map.empty
        })

// Build network flow problem
let flowProblem : QuantumNetworkFlowSolver.NetworkFlowProblem = {
    Sources = sources
    Sinks = sinks
    IntermediateNodes = intermediateNodes
    Edges = edges
    Capacities = capacities
    Demands = demands
    Supplies = supplies
}

// ==============================================================================
// SOLVE - Using Quantum Backend (LocalSimulator)
// ==============================================================================

printfn "Running quantum network flow optimization..."
let startTime = DateTime.UtcNow

// Create backend (use LocalSimulator for development)
let backend = BackendAbstraction.createLocalBackend()

// Solve using quantum network flow solver
let solutionResult = QuantumNetworkFlowSolver.solveWithShots backend flowProblem 1000

let elapsed = DateTime.UtcNow - startTime
printfn "Completed in %d ms" (int elapsed.TotalMilliseconds)
printfn ""

// ==============================================================================
// RESULTS - Flow Analysis
// ==============================================================================

printfn "╔══════════════════════════════════════════════════════════════════════════════╗"
printfn "║                     SUPPLY CHAIN FLOW REPORT                                 ║"
printfn "╚══════════════════════════════════════════════════════════════════════════════╝"
printfn ""

match solutionResult with
| Error msg ->
    printfn "❌ Solution failed: %s" msg
    printfn ""
| Ok solution ->
    let selectedRoutes = solution.SelectedEdges
    
    if selectedRoutes.IsEmpty then
        printfn "❌ No routes selected - check problem constraints"
        printfn ""
    else
        // Group flows by stage
        let supplierToWarehouse =
            selectedRoutes
            |> List.filter (fun e -> e.Source.StartsWith("S") && e.Target.StartsWith("W"))
        
        let warehouseToDistributor =
            selectedRoutes
            |> List.filter (fun e -> e.Source.StartsWith("W") && e.Target.StartsWith("D"))
        
        let distributorToCustomer =
            selectedRoutes
            |> List.filter (fun e -> e.Source.StartsWith("D") && e.Target.StartsWith("C"))
        
        printfn "STAGE 1: SUPPLIERS → WAREHOUSES (Ocean Freight)"
        printfn "────────────────────────────────────────────────────────────────────────────────"
        for route in supplierToWarehouse do
            printfn "  %s → %s: $%.2f per unit" route.Source route.Target route.Weight
        
        printfn ""
        printfn "STAGE 2: WAREHOUSES → DISTRIBUTORS (Air Freight)"
        printfn "────────────────────────────────────────────────────────────────────────────────"
        for route in warehouseToDistributor do
            printfn "  %s → %s: $%.2f per unit" route.Source route.Target route.Weight
        
        printfn ""
        printfn "STAGE 3: DISTRIBUTORS → CUSTOMERS (Ground Shipping)"
        printfn "────────────────────────────────────────────────────────────────────────────────"
        for route in distributorToCustomer do
            printfn "  %s → %s: $%.2f per unit" route.Source route.Target route.Weight
        
        printfn ""
        printfn "FLOW ANALYSIS:"
        printfn "────────────────────────────────────────────────────────────────────────────────"
        printfn "  Total Routes Selected:     %d" selectedRoutes.Length
        printfn "  Total Cost:                $%.2f (quantum optimized)" solution.TotalCost
        printfn "  Demand Satisfied:          %.0f / %.0f units" solution.DemandSatisfied solution.TotalDemand
        printfn "  Fill Rate:                 %.1f%%" (solution.FillRate * 100.0)
        printfn "  Backend:                   %s" solution.BackendName
        printfn "  Measurement Shots:         %d" solution.NumShots
        
        // Calculate total potential revenue
        let totalRevenue =
            supplyChainNodes
            |> List.choose (fun n ->
                match n.Revenue, n.Demand with
                | Some rev, Some dem -> Some (rev * float dem)
                | _ -> None)
            |> List.sum
        
        printfn "  Estimated Revenue:         $%.2f (if all demand met)" totalRevenue
        printfn "  Estimated Profit:          $%.2f" (totalRevenue - solution.TotalCost)
        printfn ""

// ==============================================================================
// BUSINESS INSIGHTS
// ==============================================================================

printfn "╔══════════════════════════════════════════════════════════════════════════════╗"
printfn "║                       BUSINESS INSIGHTS                                      ║"
printfn "╚══════════════════════════════════════════════════════════════════════════════╝"
printfn ""
printfn "KEY INSIGHTS:"
printfn "────────────────────────────────────────────────────────────────────────────────"

match solutionResult with
| Error _ ->
    printfn "  ⚠ Quantum solver requires optimization tuning for this problem size"
    printfn "  ⚠ Consider adjusting QAOA parameters or using larger shot count"
| Ok solution ->
    if solution.SelectedEdges.IsEmpty then
        printfn "  ⚠ No feasible flow found - adjust constraints or network topology"
    else
        printfn "  ✓ Quantum QAOA found min-cost flow through network"
        printfn "  ✓ Multi-stage logistics network optimized via QUBO encoding"
        printfn "  ✓ Selected %d optimal routes minimizing total transport cost" solution.SelectedEdges.Length
        printfn "  ✓ Fill rate: %.1f%% of demand satisfied" (solution.FillRate * 100.0)

printfn ""
printfn "ALGORITHM NOTES:"
printfn "────────────────────────────────────────────────────────────────────────────────"
printfn "  • Quantum: QAOA with min-cost flow QUBO encoding (Rule 1 compliant)"
printfn "  • Backend: %s (supports IonQ, Rigetti, Local)" (match solutionResult with Ok s -> s.BackendName | Error _ -> "N/A")
printfn "  • For large networks (100+ nodes), quantum annealing provides"
printfn "    significant speedup and improved solution quality over classical algorithms"
printfn ""

printfn "╔══════════════════════════════════════════════════════════════════════════════╗"
printfn "║                     OPTIMIZATION COMPLETE                                    ║"
printfn "╚══════════════════════════════════════════════════════════════════════════════╝"
printfn ""
printfn "✨ Note: This example uses QuantumNetworkFlowSolver with LocalBackend."
printfn "   For production deployments, use Azure Quantum backends (IonQ, Rigetti)"
printfn "   for improved solution quality on large-scale supply chain networks."
printfn ""
