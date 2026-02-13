// ==============================================================================
// Supply Chain Optimization Example (Small Test Version)
// ==============================================================================
// Simplified 2-stage supply chain to test quantum network flow solver.
// Routes products from 2 suppliers to 2 customers using 4 qubits
// (within LocalSimulator's 10-qubit limit).
//
// Usage:
//   dotnet fsi SupplyChain-Small.fsx
//   dotnet fsi SupplyChain-Small.fsx -- --shots 2000
//   dotnet fsi SupplyChain-Small.fsx -- --quiet --output results.json --csv results.csv
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
Cli.exitIfHelp "SupplyChain-Small.fsx" "Small 2-stage supply chain optimization (4 qubits)" [
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

// --- Problem Definition ---
pr "=== Supply Chain Optimization (Small Test) ==="
pr "    2-Stage: Suppliers -> Customers"
pr ""

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

pr "Problem: Route products from 2 suppliers to 2 customers"
pr "  4 possible routes, 4 qubits required"
pr ""

// --- Quantum Execution ---
let quantumBackend = LocalBackend() :> IQuantumBackend

pr "Running quantum network flow optimization (%d shots)..." cliShots
let startTime = DateTime.UtcNow
let solutionResult = QuantumNetworkFlowSolver.solveWithShots quantumBackend flowProblem cliShots
let elapsed = (DateTime.UtcNow - startTime).TotalMilliseconds

pr "Completed in %.0f ms" elapsed
pr ""

// --- Results ---
match solutionResult with
| Error err ->
    pr "FAILED: %s" err.Message
| Ok solution ->
    pr "SOLUTION FOUND:"
    pr "-------------------------------------------"
    pr "  Total Cost:      $%.2f" solution.TotalCost
    pr "  Routes Selected: %d" solution.SelectedEdges.Length
    pr "  Fill Rate:       %.1f%%" (solution.FillRate * 100.0)
    pr "  Backend:         %s" solution.BackendName
    pr ""
    pr "  Selected Routes:"
    solution.SelectedEdges |> List.iter (fun edge ->
        pr "    %s -> %s: $%.2f" edge.Source edge.Target edge.Weight
    )
    pr ""

    // --- JSON output ---
    outputPath |> Option.iter (fun path ->
        let payload =
            {| totalCost = solution.TotalCost
               routesSelected = solution.SelectedEdges.Length
               fillRate = solution.FillRate
               backendName = solution.BackendName
               shots = cliShots
               elapsedMs = elapsed
               routes = solution.SelectedEdges |> List.map (fun e ->
                   {| source = e.Source; target = e.Target; weight = e.Weight |}) |}
        Reporting.writeJson path payload
        pr "JSON written to %s" path
    )

    // --- CSV output ---
    csvPath |> Option.iter (fun path ->
        let header = ["Source"; "Target"; "Weight"]
        let rows =
            solution.SelectedEdges |> List.map (fun e ->
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
