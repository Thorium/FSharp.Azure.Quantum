// ==============================================================================
// Supply Chain Optimization using Quantum QAOA Network Flow
// ==============================================================================
// Multi-stage supply chain optimization using QuantumNetworkFlowSolver (QAOA)
// to minimize total logistics cost while meeting demand.  Routes products
// through a 4-stage network: suppliers -> warehouses -> distributors -> customers.
//
// Usage:
//   dotnet fsi SupplyChain.fsx
//   dotnet fsi SupplyChain.fsx -- --help
//   dotnet fsi SupplyChain.fsx -- --shots 3000
//   dotnet fsi SupplyChain.fsx -- --input nodes.csv --edges edges.csv
//   dotnet fsi SupplyChain.fsx -- --quiet --output results.json --csv results.csv
//
// References:
//   [1] Farhi et al., "A Quantum Approximate Optimization Algorithm",
//       arXiv:1411.4028 (2014).
//   [2] Wikipedia: Minimum-cost_flow_problem
//       https://en.wikipedia.org/wiki/Minimum-cost_flow_problem
// ==============================================================================

#r "nuget: Microsoft.Extensions.Logging.Abstractions, 10.0.0"
#r "../../src/FSharp.Azure.Quantum/bin/Debug/net10.0/FSharp.Azure.Quantum.dll"
#load "../_common/Cli.fs"
#load "../_common/Data.fs"
#load "../_common/Reporting.fs"

open System
open FSharp.Azure.Quantum.Quantum
open FSharp.Azure.Quantum.Core
open FSharp.Azure.Quantum.Core.BackendAbstraction
open FSharp.Azure.Quantum.GraphOptimization
open FSharp.Azure.Quantum.Backends.LocalBackend
open FSharp.Azure.Quantum.Examples.Common

// ==============================================================================
// CLI
// ==============================================================================

let argv = fsi.CommandLineArgs |> Array.skip 1
let args = Cli.parse argv

Cli.exitIfHelp "SupplyChain.fsx"
    "Multi-stage supply chain optimization (QAOA network flow)."
    [ { Cli.OptionSpec.Name = "shots"; Description = "Number of measurement shots"; Default = Some "1000" }
      { Cli.OptionSpec.Name = "input"; Description = "CSV file with node definitions (id,type,capacity,cost,demand,revenue)"; Default = Some "built-in network" }
      { Cli.OptionSpec.Name = "edges"; Description = "CSV file with edge definitions (source,target,cost)"; Default = Some "built-in routes" }
      { Cli.OptionSpec.Name = "output"; Description = "Write results to JSON file"; Default = None }
      { Cli.OptionSpec.Name = "csv"; Description = "Write results to CSV file"; Default = None }
      { Cli.OptionSpec.Name = "quiet"; Description = "Suppress informational output (flag)"; Default = None } ]
    args

let cliShots = Cli.getIntOr "shots" 1000 args
let quiet = Cli.hasFlag "quiet" args
let inputFile = Cli.tryGet "input" args
let edgesFile = Cli.tryGet "edges" args

// ==============================================================================
// TYPES
// ==============================================================================

type SupplyChainNode =
    { Id: string
      NodeType: string
      Capacity: int
      OperatingCost: float
      Demand: int option
      Revenue: float option }

// ==============================================================================
// BUILT-IN NETWORK DATA
// ==============================================================================

let private builtinNodes =
    [ { Id = "S1_Shanghai"; NodeType = "Supplier"; Capacity = 1000; OperatingCost = 100.0; Demand = None; Revenue = None }
      { Id = "S2_Mumbai"; NodeType = "Supplier"; Capacity = 800; OperatingCost = 90.0; Demand = None; Revenue = None }
      { Id = "W1_Singapore"; NodeType = "Warehouse"; Capacity = 1200; OperatingCost = 50.0; Demand = None; Revenue = None }
      { Id = "W2_Dubai"; NodeType = "Warehouse"; Capacity = 900; OperatingCost = 45.0; Demand = None; Revenue = None }
      { Id = "D1_London"; NodeType = "Distributor"; Capacity = 800; OperatingCost = 30.0; Demand = None; Revenue = None }
      { Id = "D2_Frankfurt"; NodeType = "Distributor"; Capacity = 700; OperatingCost = 35.0; Demand = None; Revenue = None }
      { Id = "C1_Paris"; NodeType = "Customer"; Capacity = 0; OperatingCost = 0.0; Demand = Some 400; Revenue = Some 200.0 }
      { Id = "C2_Berlin"; NodeType = "Customer"; Capacity = 0; OperatingCost = 0.0; Demand = Some 500; Revenue = Some 220.0 }
      { Id = "C3_Amsterdam"; NodeType = "Customer"; Capacity = 0; OperatingCost = 0.0; Demand = Some 350; Revenue = Some 180.0 } ]

let private builtinRoutes =
    [ ("S1_Shanghai", "W1_Singapore", 20.0)
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
      ("D2_Frankfurt", "C3_Amsterdam", 13.0) ]

// ==============================================================================
// CSV LOADING
// ==============================================================================

let private loadNodesFromCsv (path: string) : SupplyChainNode list =
    let rows, errors = Data.readCsvWithHeaderWithErrors path
    if not (List.isEmpty errors) && not quiet then
        for err in errors do eprintfn "  Warning (CSV): %s" err
    rows
    |> List.choose (fun row ->
        let get key = row.Values |> Map.tryFind key
        match get "id", get "type" with
        | Some id, Some nodeType ->
            let tryInt key = get key |> Option.bind (fun s -> match Int32.TryParse s with true, v -> Some v | _ -> None)
            let tryFloat key = get key |> Option.bind (fun s -> match Double.TryParse s with true, v -> Some v | _ -> None)
            Some { Id = id; NodeType = nodeType
                   Capacity = tryInt "capacity" |> Option.defaultValue 0
                   OperatingCost = tryFloat "cost" |> Option.defaultValue 0.0
                   Demand = tryInt "demand"
                   Revenue = tryFloat "revenue" }
        | _ ->
            if not quiet then eprintfn "  Warning: node row missing 'id' or 'type'"
            None)

let private loadEdgesFromCsv (path: string) : (string * string * float) list =
    let rows, errors = Data.readCsvWithHeaderWithErrors path
    if not (List.isEmpty errors) && not quiet then
        for err in errors do eprintfn "  Warning (CSV): %s" err
    rows
    |> List.choose (fun row ->
        let get key = row.Values |> Map.tryFind key
        match get "source", get "target", get "cost" with
        | Some src, Some tgt, Some costStr ->
            match Double.TryParse costStr with
            | true, cost -> Some (src, tgt, cost)
            | _ ->
                if not quiet then eprintfn "  Warning: invalid cost for edge %s->%s" src tgt
                None
        | _ ->
            if not quiet then eprintfn "  Warning: edge row missing 'source', 'target', or 'cost'"
            None)

// ==============================================================================
// NETWORK SELECTION
// ==============================================================================

let supplyChainNodes =
    match inputFile with
    | Some path ->
        let resolved = Data.resolveRelative __SOURCE_DIRECTORY__ path
        if not quiet then printfn "Loading nodes from: %s" resolved
        loadNodesFromCsv resolved
    | None -> builtinNodes

let transportRoutes =
    match edgesFile with
    | Some path ->
        let resolved = Data.resolveRelative __SOURCE_DIRECTORY__ path
        if not quiet then printfn "Loading edges from: %s" resolved
        loadEdgesFromCsv resolved
    | None -> builtinRoutes

if List.isEmpty supplyChainNodes then
    eprintfn "Error: No nodes loaded."
    exit 1

// ==============================================================================
// PROBLEM SETUP
// ==============================================================================

let nodesByType nodeType =
    supplyChainNodes |> List.filter (fun n -> n.NodeType = nodeType)

let totalSupply = nodesByType "Supplier" |> List.sumBy (fun n -> n.Capacity)
let totalDemand = nodesByType "Customer" |> List.sumBy (fun n -> n.Demand |> Option.defaultValue 0)

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

let flowProblem : QuantumNetworkFlowSolver.NetworkFlowProblem =
    { Sources = sources
      Sinks = sinks
      IntermediateNodes = intermediateNodes
      Edges = edges
      Capacities = capacities
      Demands = demands
      Supplies = supplies }

// ==============================================================================
// QUANTUM BACKEND (Rule 1)
// ==============================================================================

let quantumBackend : IQuantumBackend = LocalBackend() :> IQuantumBackend

if not quiet then
    printfn ""
    printfn "=================================================================="
    printfn "  Supply Chain Optimization (QAOA Network Flow)"
    printfn "=================================================================="
    printfn ""
    printfn "  Backend:      %s" quantumBackend.Name
    printfn "  Nodes:        %d (Suppliers: %d, Warehouses: %d, Distributors: %d, Customers: %d)"
        supplyChainNodes.Length
        (nodesByType "Supplier" |> List.length)
        (nodesByType "Warehouse" |> List.length)
        (nodesByType "Distributor" |> List.length)
        (nodesByType "Customer" |> List.length)
    printfn "  Edges:        %d" transportRoutes.Length
    printfn "  Supply:       %d units" totalSupply
    printfn "  Demand:       %d units" totalDemand
    printfn "  Shots:        %d" cliShots
    printfn ""

// ==============================================================================
// QUANTUM EXECUTION
// ==============================================================================

let startTime = DateTime.UtcNow
let solutionResult = QuantumNetworkFlowSolver.solveWithShots quantumBackend flowProblem cliShots
let elapsed = (DateTime.UtcNow - startTime).TotalMilliseconds

if not quiet then
    printfn "  Completed in %.0f ms" elapsed
    printfn ""

// ==============================================================================
// RESULTS
// ==============================================================================

let hasQuantumFailure, routeMaps =
    match solutionResult with
    | Error err ->
        if not quiet then eprintfn "  FAILED: %s" err.Message
        true, []
    | Ok solution ->
        let routes = solution.SelectedEdges
        if not quiet then
            let stages =
                [ ("Suppliers -> Warehouses", "S", "W")
                  ("Warehouses -> Distributors", "W", "D")
                  ("Distributors -> Customers", "D", "C") ]
            stages |> List.iter (fun (label, srcPfx, tgtPfx) ->
                let stageRoutes = routes |> List.filter (fun e -> e.Source.StartsWith(srcPfx) && e.Target.StartsWith(tgtPfx))
                if not (List.isEmpty stageRoutes) then
                    printfn "  %s:" label
                    stageRoutes |> List.iter (fun r -> printfn "    %s -> %s  $%.2f/unit" r.Source r.Target r.Weight)
                    printfn "")

        let totalRevenue =
            supplyChainNodes
            |> List.choose (fun n ->
                match n.Revenue, n.Demand with
                | Some rev, Some dem -> Some (rev * float dem)
                | _ -> None)
            |> List.sum

        let maps =
            routes
            |> List.mapi (fun i e ->
                [ "rank", string (i + 1)
                  "source", e.Source
                  "target", e.Target
                  "cost_per_unit", sprintf "%.2f" e.Weight
                  "total_cost", sprintf "%.2f" solution.TotalCost
                  "demand_satisfied", sprintf "%.0f" solution.DemandSatisfied
                  "total_demand", sprintf "%.0f" solution.TotalDemand
                  "fill_rate", sprintf "%.3f" solution.FillRate
                  "backend", solution.BackendName
                  "shots", string solution.NumShots
                  "elapsed_ms", sprintf "%.0f" elapsed
                  "estimated_revenue", sprintf "%.2f" totalRevenue
                  "estimated_profit", sprintf "%.2f" (totalRevenue - solution.TotalCost)
                  "has_quantum_failure", "False" ]
                |> Map.ofList)
        false, maps

// If no routes were returned, produce a single summary row
let resultMaps =
    if List.isEmpty routeMaps then
        [ [ "rank", "1"
            "source", ""; "target", ""
            "cost_per_unit", ""; "total_cost", ""
            "demand_satisfied", "0"; "total_demand", string totalDemand
            "fill_rate", "0.000"
            "backend", quantumBackend.Name
            "shots", string cliShots
            "elapsed_ms", sprintf "%.0f" elapsed
            "estimated_revenue", ""; "estimated_profit", ""
            "has_quantum_failure", string hasQuantumFailure ]
          |> Map.ofList ]
    else routeMaps

// ==============================================================================
// ROUTE TABLE (unconditional)
// ==============================================================================

let printTable () =
    printfn "=================================================================="
    printfn "  Supply Chain Optimization Results"
    printfn "=================================================================="
    printfn ""
    printfn "  %-4s  %-16s  %-16s  %10s" "#" "Source" "Target" "Cost/Unit"
    printfn "  %s" (String('=', 55))

    resultMaps
    |> List.iteri (fun i m ->
        let src = m |> Map.tryFind "source" |> Option.defaultValue ""
        let tgt = m |> Map.tryFind "target" |> Option.defaultValue ""
        let cost = m |> Map.tryFind "cost_per_unit" |> Option.defaultValue ""
        if src <> "" && tgt <> "" then
            printfn "  %-4d  %-16s  %-16s  %10s" (i + 1) src tgt cost)

    printfn ""
    let first = resultMaps |> List.tryHead
    match first with
    | Some m ->
        printfn "  Total Cost:       %s" (m |> Map.tryFind "total_cost" |> Option.defaultValue "N/A")
        printfn "  Fill Rate:        %s" (m |> Map.tryFind "fill_rate" |> Option.defaultValue "N/A")
        printfn "  Demand Satisfied: %s / %s"
            (m |> Map.tryFind "demand_satisfied" |> Option.defaultValue "0")
            (m |> Map.tryFind "total_demand" |> Option.defaultValue "0")
    | None -> ()
    printfn ""

printTable ()

// ==============================================================================
// STRUCTURED OUTPUT
// ==============================================================================

match Cli.tryGet "output" args with
| Some path ->
    Reporting.writeJson path resultMaps
    if not quiet then printfn "Results written to %s" path
| None -> ()

match Cli.tryGet "csv" args with
| Some path ->
    let header =
        [ "rank"; "source"; "target"; "cost_per_unit"
          "total_cost"; "demand_satisfied"; "total_demand"; "fill_rate"
          "backend"; "shots"; "elapsed_ms"
          "estimated_revenue"; "estimated_profit"; "has_quantum_failure" ]
    let rows =
        resultMaps
        |> List.map (fun m ->
            header |> List.map (fun h -> m |> Map.tryFind h |> Option.defaultValue ""))
    Reporting.writeCsv path header rows
    if not quiet then printfn "Results written to %s" path
| None -> ()

if argv.Length = 0 && not quiet then
    printfn ""
    printfn "Tip: Run with --help to see all options."
    printfn "     --shots 3000           Increase measurement shots"
    printfn "     --input nodes.csv      Load custom network nodes"
    printfn "     --edges edges.csv      Load custom transport edges"
    printfn "     --csv results.csv      Export route table as CSV"
    printfn ""
