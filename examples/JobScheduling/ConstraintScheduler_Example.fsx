// ============================================================================
// Constraint Scheduler Example
// ============================================================================
// Demonstrates using quantum optimization to solve scheduling and resource
// allocation problems with constraints.
//
// Business Use Cases:
// - Workforce Management: Schedule shifts respecting availability and skills
// - Cloud Computing: Allocate VMs to minimize costs while meeting SLAs
// - Manufacturing: Assign tasks to machines with capacity constraints
// - Logistics: Route deliveries respecting time windows and capacity
//
// Usage:
//   dotnet fsi ConstraintScheduler_Example.fsx
//   dotnet fsi ConstraintScheduler_Example.fsx -- --example cloud
//   dotnet fsi ConstraintScheduler_Example.fsx -- --shots 3000 --quiet --output results.json
// ============================================================================

#r "nuget: Microsoft.Extensions.Logging.Abstractions, 10.0.0"
#r "../../src/FSharp.Azure.Quantum/bin/Debug/net10.0/FSharp.Azure.Quantum.dll"
#load "../_common/Cli.fs"
#load "../_common/Data.fs"
#load "../_common/Reporting.fs"
open FSharp.Azure.Quantum.Examples.Common

open System
open FSharp.Azure.Quantum.Core
open FSharp.Azure.Quantum.Core.BackendAbstraction
open FSharp.Azure.Quantum.Backends.LocalBackend
open FSharp.Azure.Quantum.Business

// --- CLI ---
let argv = fsi.CommandLineArgs |> Array.skip 1
let args = Cli.parse argv
Cli.exitIfHelp "ConstraintScheduler_Example.fsx" "Quantum constraint scheduling and resource allocation" [
    { Name = "example"; Description = "Which example: all, simple, workforce, cloud, manufacturing"; Default = Some "all" }
    { Name = "shots"; Description = "Number of measurement shots"; Default = Some "1500" }
    { Name = "output"; Description = "Write results to JSON file"; Default = None }
    { Name = "csv"; Description = "Write results to CSV file"; Default = None }
    { Name = "quiet"; Description = "Suppress printed output"; Default = None }
] args

let exampleName = Cli.getOr "example" "all" args
let cliShots = Cli.getIntOr "shots" 1500 args
let quiet = Cli.hasFlag "quiet" args
let outputPath = Cli.tryGet "output" args
let csvPath = Cli.tryGet "csv" args

let pr fmt = Printf.ksprintf (fun s -> if not quiet then printfn "%s" s) fmt

let runAll = (exampleName = "all")

// Accumulate results for JSON/CSV export
let mutable jsonResults : obj list = []
let mutable csvRows : string list list = []

// --- Quantum Backend (Rule 1) ---
let quantumBackend = LocalBackend() :> IQuantumBackend

// Helper to display schedule results
let displaySchedule (label: string) (result: Result<ConstraintScheduler.SchedulingResult, QuantumError>) =
    match result with
    | Ok schedResult ->
        pr "%s Complete" label
        pr "  Message: %s" schedResult.Message
        pr ""

        match schedResult.BestSchedule with
        | Some schedule ->
            pr "  Assignment:"
            schedule.Assignments |> List.iter (fun a ->
                pr "    %s -> %s ($%.2f)" a.Task a.Resource a.Cost
            )
            pr ""
            pr "  Total Cost: $%.2f" schedule.TotalCost
            pr "  Feasible: %b" schedule.IsFeasible
            pr "  Constraints: %d / %d hard, %d / %d soft"
                schedule.HardConstraintsSatisfied
                schedule.TotalHardConstraints
                schedule.SoftConstraintsSatisfied
                schedule.TotalSoftConstraints

            // Accumulate for export
            jsonResults <- (box {| example = label; totalCost = schedule.TotalCost
                                   feasible = schedule.IsFeasible
                                   assignments = schedule.Assignments.Length |}) :: jsonResults
            schedule.Assignments |> List.iter (fun a ->
                csvRows <- [label; a.Task; a.Resource; sprintf "%.2f" a.Cost;
                            sprintf "%b" schedule.IsFeasible] :: csvRows
            )
        | None ->
            pr "  No feasible schedule found with current constraints."

            jsonResults <- (box {| example = label; totalCost = 0.0
                                   feasible = false; assignments = 0 |}) :: jsonResults
        pr ""

    | Error err ->
        pr "Error: %A" err
        pr ""

// ============================================================================
// Example 1: Simple Task Assignment - Classical
// ============================================================================

if runAll || exampleName = "simple" then
    pr "=== Example 1: Simple Task Assignment (Classical) ==="
    pr ""

    let simpleResult = ConstraintScheduler.constraintScheduler {
        task "Task1"
        task "Task2"

        resource "ResourceA" 5.0
        resource "ResourceB" 3.0

        prefer "Task1" "ResourceA" 1.0
        prefer "Task2" "ResourceB" 1.0

        optimizeFor ConstraintScheduler.MinimizeCost

        // Explicit backend for Rule 1 compliance
        backend quantumBackend
    }

    displaySchedule "Simple" simpleResult

// ============================================================================
// Example 2: Workforce Scheduling - Classical
// ============================================================================

if runAll || exampleName = "workforce" then
    pr "=== Example 2: Workforce Scheduling (Classical) ==="
    pr ""

    let workforceResult = ConstraintScheduler.constraintScheduler {
        task "Morning"
        task "Afternoon"
        task "Evening"
        task "Night"

        resource "Alice" 25.0
        resource "Bob" 15.0
        resource "Carol" 20.0
        resource "Dave" 15.0

        conflict "Morning" "Afternoon"
        conflict "Afternoon" "Evening"
        conflict "Evening" "Night"

        prefer "Morning" "Alice" 10.0
        prefer "Afternoon" "Carol" 8.0
        prefer "Night" "Dave" 9.0

        optimizeFor ConstraintScheduler.MinimizeCost
        maxBudget 100.0

        backend quantumBackend
    }

    displaySchedule "Workforce" workforceResult

// ============================================================================
// Example 3: Cloud Resource Allocation - Quantum
// ============================================================================

if runAll || exampleName = "cloud" then
    pr "=== Example 3: Cloud Resource Allocation (Quantum, %d shots) ===" cliShots
    pr ""

    let cloudResult = ConstraintScheduler.constraintScheduler {
        task "WebServer1"
        task "WebServer2"
        task "DatabasePrimary"
        task "DatabaseReplica"
        task "CacheNode"

        resource "Server_A" 10.0
        resource "Server_B" 15.0
        resource "Server_C" 8.0

        conflict "WebServer1" "WebServer2"
        conflict "DatabasePrimary" "DatabaseReplica"
        conflict "CacheNode" "DatabasePrimary"

        prefer "WebServer1" "Server_A" 15.0
        prefer "WebServer2" "Server_A" 15.0
        prefer "DatabasePrimary" "Server_B" 20.0
        prefer "DatabaseReplica" "Server_B" 18.0

        optimizeFor ConstraintScheduler.Balanced
        maxBudget 50.0

        backend quantumBackend
        shots cliShots
    }

    match cloudResult with
    | Ok result ->
        pr "Cloud Allocation Complete"
        pr "  Message: %s" result.Message
        pr ""

        match result.BestSchedule with
        | Some schedule ->
            // Group by server
            let byServer =
                schedule.Assignments
                |> List.groupBy (fun a -> a.Resource)
                |> List.sortBy fst

            pr "  VM Assignment:"
            byServer |> List.iter (fun (server, assignments) ->
                pr "    %s:" server
                assignments |> List.iter (fun a ->
                    pr "      - %s ($%.2f)" a.Task a.Cost
                )
            )
            pr ""
            pr "  Total Cost: $%.2f" schedule.TotalCost
            pr "  Feasible: %b" schedule.IsFeasible

            jsonResults <- (box {| example = "cloud"; totalCost = schedule.TotalCost
                                   feasible = schedule.IsFeasible
                                   assignments = schedule.Assignments.Length |}) :: jsonResults
            schedule.Assignments |> List.iter (fun a ->
                csvRows <- ["cloud"; a.Task; a.Resource; sprintf "%.2f" a.Cost;
                            sprintf "%b" schedule.IsFeasible] :: csvRows
            )
        | None ->
            pr "  Quantum optimization did not converge. Try increasing shots or adjusting constraints."
            jsonResults <- (box {| example = "cloud"; totalCost = 0.0
                                   feasible = false; assignments = 0 |}) :: jsonResults
        pr ""

    | Error err ->
        pr "Error: %A" err
        pr ""

// ============================================================================
// Example 4: Manufacturing - Quantum with High Accuracy
// ============================================================================

if runAll || exampleName = "manufacturing" then
    pr "=== Example 4: Manufacturing Task Assignment (Quantum, %d shots) ===" (cliShots * 3)
    pr ""

    let manufacturingResult = ConstraintScheduler.constraintScheduler {
        task "Welding_Job1"
        task "Welding_Job2"
        task "Assembly_Job1"
        task "Painting_Job1"

        resource "WeldingStation_A" 50.0
        resource "WeldingStation_B" 30.0
        resource "AssemblyLine_1" 40.0
        resource "PaintingBooth" 35.0

        require "Welding_Job1" "WeldingStation_A"
        require "Welding_Job2" "WeldingStation_B"
        require "Assembly_Job1" "AssemblyLine_1"
        require "Painting_Job1" "PaintingBooth"

        precedence "Welding_Job1" "Painting_Job1"
        precedence "Assembly_Job1" "Painting_Job1"

        prefer "Welding_Job1" "WeldingStation_A" 10.0

        optimizeFor ConstraintScheduler.MaximizeSatisfaction
        maxBudget 200.0

        backend quantumBackend
        shots (cliShots * 3)
    }

    displaySchedule "Manufacturing" manufacturingResult

// --- JSON output ---
outputPath |> Option.iter (fun path ->
    Reporting.writeJson path (jsonResults |> List.rev)
    pr "JSON written to %s" path
)

// --- CSV output ---
csvPath |> Option.iter (fun path ->
    let header = ["Example"; "Task"; "Resource"; "Cost"; "Feasible"]
    Reporting.writeCsv path header (csvRows |> List.rev)
    pr "CSV written to %s" path
)

// --- Usage hints ---
if not quiet && outputPath.IsNone && csvPath.IsNone then
    pr "-------------------------------------------"
    pr "Tip: Use --example simple|workforce|cloud|manufacturing to run one."
    pr "     Use --shots N to change measurement count (default 1500)."
    pr "     Use --output results.json or --csv results.csv to export."
    pr "     Use --help for all options."
