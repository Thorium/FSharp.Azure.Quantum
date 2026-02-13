// ==============================================================================
// Supply Chain Optimization Example
// ==============================================================================
// Multi-stage supply chain optimization using quantum QAOA via
// QuantumNetworkFlowSolver to minimize total logistics cost while meeting demand.
//
// Business Context:
// A logistics company operates a 4-stage supply chain: suppliers -> warehouses ->
// distributors -> customers. The goal is to route products through the network
// to meet all customer demand at minimum total cost.
//
// Quantum Approach:
// - QAOA (Quantum Approximate Optimization Algorithm) via QUBO encoding
// - 14 edges = 14 qubits + complex flow conservation constraints
// - Executes on quantum backend (LocalSimulator, IonQ, Rigetti)
//
// Usage:
//   dotnet fsi SupplyChain.fsx
//   dotnet fsi SupplyChain.fsx -- --shots 3000
//   dotnet fsi SupplyChain.fsx -- --quiet --output results.json --csv results.csv
// ==============================================================================

#r "../../src/FSharp.Azure.Quantum/bin/Debug/net10.0/FSharp.Azure.Quantum.dll"
#load "../_common/Cli.fs"
#load "../_common/Data.fs"
#load "../_common/Reporting.fs"
open FSharp.Azure.Quantum.Examples.Common

open System
open FSharp.Azure.Quantum.Quantum
open FSharp.Azure.Quantum.Core
open FSharp.Azure.Quantum.Core.BackendAbstraction
open FSharp.Azure.Quantum.GraphOptimization
open FSharp.Azure.Quantum.Backends.LocalBackend

// --- CLI ---
let argv = fsi.CommandLineArgs |> Array.skip 1
let args = Cli.parse argv
Cli.exitIfHelp "SupplyChain.fsx" "Multi-stage supply chain optimization (14 qubits, QAOA)" [
    { Name = "shots"; Description = "Number of measurement shots"; Default = Some "1000" }
    { Name = "output"; Description = "Write results to JSON file"; Default = None }
    { Name = "csv"; Description = "Write results to CSV file"; Default = None }
    { Name = "quiet"; Description = "Suppress printed output"; Default = None }
] args

let cliShots = Cli.getIntOr "shots" 1000 args
let quiet = Cli.hasFlag "quiet" args
let outputPath = Cli.tryGet "output" args
let csvPath = Cli.tryGet "csv" args

let pr fmt = Printf.ksprintf (fun s -> if not quiet then printfn "%s" s) fmt

// ==============================================================================
// Domain Model
// ==============================================================================

type SupplyChainNode = {
    Id: string
    NodeType: string
    Capacity: int
    OperatingCost: float
    Demand: int option
    Revenue: float option
}

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
// Supply Chain Data
// ==============================================================================

let supplyChainNodes = [
    { Id = "S1_Shanghai"; NodeType = "Supplier"; Capacity = 1000; OperatingCost = 100.0; Demand = None; Revenue = None }
    { Id = "S2_Mumbai"; NodeType = "Supplier"; Capacity = 800; OperatingCost = 90.0; Demand = None; Revenue = None }
    { Id = "W1_Singapore"; NodeType = "Warehouse"; Capacity = 1200; OperatingCost = 50.0; Demand = None; Revenue = None }
    { Id = "W2_Dubai"; NodeType = "Warehouse"; Capacity = 900; OperatingCost = 45.0; Demand = None; Revenue = None }
    { Id = "D1_London"; NodeType = "Distributor"; Capacity = 800; OperatingCost = 30.0; Demand = None; Revenue = None }
    { Id = "D2_Frankfurt"; NodeType = "Distributor"; Capacity = 700; OperatingCost = 35.0; Demand = None; Revenue = None }
    { Id = "C1_Paris"; NodeType = "Customer"; Capacity = 0; OperatingCost = 0.0; Demand = Some 400; Revenue = Some 200.0 }
    { Id = "C2_Berlin"; NodeType = "Customer"; Capacity = 0; OperatingCost = 0.0; Demand = Some 500; Revenue = Some 220.0 }
    { Id = "C3_Amsterdam"; NodeType = "Customer"; Capacity = 0; OperatingCost = 0.0; Demand = Some 350; Revenue = Some 180.0 }
]

let transportRoutes = [
    ("S1_Shanghai", "W1_Singapore", 20.0)
    ("S1_Shanghai", "W2_Dubai", 25.0)
    ("S2_Mumbai", "W1_Singapore", 22.0)
    ("S2_Mumbai", "W2_Dubai", 18.0)
    ("W1_Singapore", "D1_London", 15.0)
    ("W1_Singapore", "D2_Frankfurt", 18.0)
    ("W2_Dubai", "D1_London", 16.0)
    ("W2_Dubai", "D2_Frankfurt", 14.0)
    ("D1_London", "C1_Paris", 10.0)
    ("D1_London", "C2_Berlin", 12.0)
    ("D1_London", "C3_Amsterdam", 11.0)
    ("D2_Frankfurt", "C1_Paris", 11.0)
    ("D2_Frankfurt", "C2_Berlin", 10.0)
    ("D2_Frankfurt", "C3_Amsterdam", 13.0)
]

// ==============================================================================
// Problem Setup
// ==============================================================================

let nodesByType nodeType =
    supplyChainNodes |> List.filter (fun n -> n.NodeType = nodeType)

let totalSupply = nodesByType "Supplier" |> List.sumBy (fun n -> n.Capacity)
let totalDemand = nodesByType "Customer" |> List.sumBy (fun n -> n.Demand |> Option.defaultValue 0)

pr "=== Supply Chain Optimization ==="
pr "    QuantumNetworkFlowSolver (QAOA)"
pr ""
pr "Problem: Route %d units through 4-stage supply chain" totalDemand
pr "  Suppliers:    %d (capacity: %d units)" (nodesByType "Supplier" |> List.length) totalSupply
pr "  Warehouses:   %d" (nodesByType "Warehouse" |> List.length)
pr "  Distributors: %d" (nodesByType "Distributor" |> List.length)
pr "  Customers:    %d (demand: %d units)" (nodesByType "Customer" |> List.length) totalDemand
pr "Objective: Minimize total cost while meeting demand"
pr ""

let sources = nodesByType "Supplier" |> List.map (fun n -> n.Id)
let sinks = nodesByType "Customer" |> List.map (fun n -> n.Id)
let intermediateNodes =
    supplyChainNodes
    |> List.filter (fun n -> n.NodeType = "Warehouse" || n.NodeType = "Distributor")
    |> List.map (fun n -> n.Id)

let capacities = supplyChainNodes |> List.map (fun n -> n.Id, n.Capacity) |> Map.ofList
let demands = supplyChainNodes |> List.choose (fun n -> n.Demand |> Option.map (fun d -> n.Id, d)) |> Map.ofList
let supplies = nodesByType "Supplier" |> List.map (fun n -> n.Id, n.Capacity) |> Map.ofList

let edges =
    transportRoutes |> List.map (fun (src, tgt, cost) ->
        { Source = src; Target = tgt; Weight = cost; Directed = true; Value = Some cost; Properties = Map.empty })

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
// Quantum Execution
// ==============================================================================

let quantumBackend = LocalBackend() :> IQuantumBackend

pr "Running quantum network flow optimization (%d shots)..." cliShots
let startTime = DateTime.UtcNow
let solutionResult = QuantumNetworkFlowSolver.solveWithShots quantumBackend flowProblem cliShots
let elapsed = (DateTime.UtcNow - startTime).TotalMilliseconds

pr "Completed in %.0f ms" elapsed
pr ""

// ==============================================================================
// Results
// ==============================================================================

pr "=== Supply Chain Flow Report ==="
pr ""

match solutionResult with
| Error err ->
    pr "FAILED: %s" err.Message
    pr ""

| Ok solution ->
    let selectedRoutes = solution.SelectedEdges

    if selectedRoutes.IsEmpty then
        pr "No routes selected - check problem constraints"
        pr ""
    else
        // Group flows by stage
        let stages = [
            ("Stage 1: Suppliers -> Warehouses (Ocean Freight)", "S", "W")
            ("Stage 2: Warehouses -> Distributors (Air Freight)", "W", "D")
            ("Stage 3: Distributors -> Customers (Ground Shipping)", "D", "C")
        ]

        stages |> List.iter (fun (label, srcPrefix, tgtPrefix) ->
            let stageRoutes =
                selectedRoutes
                |> List.filter (fun e -> e.Source.StartsWith(srcPrefix) && e.Target.StartsWith(tgtPrefix))
            pr "%s" label
            pr "-------------------------------------------"
            stageRoutes |> List.iter (fun route ->
                pr "  %s -> %s: $%.2f per unit" route.Source route.Target route.Weight
            )
            pr ""
        )

        pr "Flow Analysis:"
        pr "-------------------------------------------"
        pr "  Total Routes Selected: %d" selectedRoutes.Length
        pr "  Total Cost:            $%.2f (quantum optimized)" solution.TotalCost
        pr "  Demand Satisfied:      %.0f / %.0f units" solution.DemandSatisfied solution.TotalDemand
        pr "  Fill Rate:             %.1f%%" (solution.FillRate * 100.0)
        pr "  Backend:               %s" solution.BackendName
        pr "  Measurement Shots:     %d" solution.NumShots
        pr ""

        let totalRevenue =
            supplyChainNodes
            |> List.choose (fun n ->
                match n.Revenue, n.Demand with
                | Some rev, Some dem -> Some (rev * float dem)
                | _ -> None)
            |> List.sum

        pr "  Estimated Revenue:     $%.2f (if all demand met)" totalRevenue
        pr "  Estimated Profit:      $%.2f" (totalRevenue - solution.TotalCost)
        pr ""

        if solution.FillRate < 0.5 then
            pr "Note: Low fill rate (%.1f%%) is expected with p=1 QAOA layers." (solution.FillRate * 100.0)
            pr "  For production: Use p=3+ layers, more shots, or real quantum hardware."
            pr ""

        // --- JSON output ---
        outputPath |> Option.iter (fun path ->
            let payload =
                {| totalCost = solution.TotalCost
                   routesSelected = selectedRoutes.Length
                   fillRate = solution.FillRate
                   demandSatisfied = solution.DemandSatisfied
                   totalDemand = solution.TotalDemand
                   backendName = solution.BackendName
                   shots = solution.NumShots
                   elapsedMs = elapsed
                   estimatedRevenue = totalRevenue
                   estimatedProfit = totalRevenue - solution.TotalCost
                   routes = selectedRoutes |> List.map (fun e ->
                       {| source = e.Source; target = e.Target; weight = e.Weight |}) |}
            Reporting.writeJson path payload
            pr "JSON written to %s" path
        )

        // --- CSV output ---
        csvPath |> Option.iter (fun path ->
            let header = ["Source"; "Target"; "Weight"]
            let rows =
                selectedRoutes |> List.map (fun e ->
                    [e.Source; e.Target; sprintf "%.2f" e.Weight])
            Reporting.writeCsv path header rows
            pr "CSV written to %s" path
        )

// --- Usage hints ---
if not quiet && outputPath.IsNone && csvPath.IsNone then
    pr "-------------------------------------------"
    pr "Tip: Use --output results.json or --csv results.csv to export data."
    pr "     Use --shots N to change measurement count (default 1000)."
    pr "     Use --help for all options."
