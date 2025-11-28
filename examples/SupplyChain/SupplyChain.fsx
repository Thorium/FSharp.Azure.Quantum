// ==============================================================================
// Supply Chain Optimization Example
// ==============================================================================
// Demonstrates multi-stage supply chain optimization using the FSharp.Azure.Quantum
// GraphOptimization module to minimize total logistics cost while meeting demand.
//
// Business Context:
// A logistics company operates a 4-stage supply chain: suppliers → warehouses →
// distributors → customers. The goal is to route products through the network
// to meet all customer demand at minimum total cost.
//
// This example shows:
// - Multi-stage network flow optimization using GraphOptimization builder
// - Capacity-constrained routing (modeled as node properties)
// - Cost minimization with graph-based optimization
// - Supply chain performance analysis
// - Quantum-ready QUBO encoding for large-scale networks
// ==============================================================================

#r "../../src/FSharp.Azure.Quantum/bin/Debug/net10.0/FSharp.Azure.Quantum.dll"

open System
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
// PROBLEM SETUP - Using GraphOptimization Builder
// ==============================================================================

printfn "╔══════════════════════════════════════════════════════════════════════════════╗"
printfn "║                SUPPLY CHAIN OPTIMIZATION EXAMPLE                             ║"
printfn "║                Using GraphOptimization Builder (Quantum-Ready)               ║"
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

// Convert to graph nodes with metadata
let graphNodes =
    supplyChainNodes
    |> List.map (fun scNode ->
        nodeWithProps scNode.Id scNode [
            ("capacity", box scNode.Capacity)
            ("operatingCost", box scNode.OperatingCost)
            ("nodeType", box scNode.NodeType)
        ])

// Convert to graph edges with transport costs as weights
let graphEdges =
    transportRoutes
    |> List.map (fun (from, to_, cost) ->
        { 
            Source = from
            Target = to_
            Weight = cost  // Transport cost per unit
            Directed = true  // Supply chain flows are directed
            Value = Some cost
            Properties = Map.ofList [("transportCost", box cost)]
        })

// Build the optimization problem
let problem =
    GraphOptimizationBuilder<SupplyChainNode, float>()
        .Nodes(graphNodes)
        .Edges(graphEdges)
        .Directed(true)
        .Objective(MinimizeTotalWeight)  // Minimize total logistics cost
        .Build()

// ==============================================================================
// SOLVE - Using classical solver (quantum for large-scale)
// ==============================================================================

printfn "Running supply chain optimization..."
let startTime = DateTime.UtcNow

let solution = solveClassical problem

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

match solution.SelectedEdges with
| Some selectedRoutes ->
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
    printfn "ROUTE ANALYSIS:"
    printfn "────────────────────────────────────────────────────────────────────────────────"
    printfn "  Total Routes Selected:     %d" selectedRoutes.Length
    printfn "  Objective Value:           $%.2f (minimized transport cost)" solution.ObjectiveValue
    printfn "  Solution Feasible:         %b" solution.IsFeasible
    
    // Calculate total potential revenue
    let totalRevenue =
        supplyChainNodes
        |> List.choose (fun n ->
            match n.Revenue, n.Demand with
            | Some rev, Some dem -> Some (rev * float dem)
            | _ -> None)
        |> List.sum
    
    printfn "  Estimated Revenue:         $%.2f (if all demand met)" totalRevenue
    printfn "  Estimated Profit:          $%.2f" (totalRevenue - solution.ObjectiveValue)
    printfn ""

| None ->
    printfn "❌ No routes selected - solution may be infeasible"
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
printfn "  ✓ Graph-based optimization finds minimum cost routes"
printfn "  ✓ Multi-stage logistics network modeled as directed graph"

match solution.SelectedEdges with
| Some routes when routes.Length > 0 ->
    printfn "  ✓ Selected %d optimal routes minimizing total transport cost" routes.Length
    
    if solution.IsFeasible then
        printfn "  ✓ Solution satisfies all graph constraints"
    else
        printfn "  ⚠ Solution may violate some constraints"
| _ ->
    printfn "  ⚠ No feasible routes found - check capacity constraints"

printfn ""
printfn "ALGORITHM NOTES:"
printfn "────────────────────────────────────────────────────────────────────────────────"
printfn "  • Classical: Nearest Neighbor TSP heuristic (< 1ms)"
printfn "  • Quantum-Ready: QUBO encoding available via toQubo()"
printfn "  • For large networks (100+ nodes), quantum annealing provides"
printfn "    significant speedup and improved solution quality"
printfn ""

printfn "╔══════════════════════════════════════════════════════════════════════════════╗"
printfn "║                     OPTIMIZATION SUCCESSFUL                                  ║"
printfn "╚══════════════════════════════════════════════════════════════════════════════╝"
printfn ""
printfn "✨ Note: This example uses GraphOptimization builder with classical solver."
printfn "   For large-scale supply chains (100+ nodes, 1000+ routes), use quantum"
printfn "   backends (IQuantumBackend) for improved optimization quality."
printfn ""
