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
open FSharp.Azure.Quantum
open FSharp.Azure.Quantum.Core
open FSharp.Azure.Quantum.Core.BackendAbstraction
open FSharp.Azure.Quantum.Backends.LocalBackend
open FSharp.Azure.Quantum.Business
open FSharp.Azure.Quantum.TaskScheduling  // time-indexed scheduler (genuine precedence)

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
    pr "=== Example 4: Manufacturing with Precedence (Quantum, time-indexed) ==="
    pr ""
    pr "  Precedence (\"A must finish before B\") is a TEMPORAL constraint, so this case uses"
    pr "  the time-indexed TaskScheduling solver rather than the resource-assignment"
    pr "  ConstraintScheduler. Painting must follow BOTH welding and assembly."
    pr ""

    // Stations modelled as capacity-1 resources (one job at a time), with per-unit cost.
    let weldStationA = resource { resourceId "WeldingStation_A"; capacity 1.0; costPerUnit 50.0 }
    let weldStationB = resource { resourceId "WeldingStation_B"; capacity 1.0; costPerUnit 30.0 }
    let assemblyLine = resource { resourceId "AssemblyLine_1"; capacity 1.0; costPerUnit 40.0 }
    let paintingBooth = resource { resourceId "PaintingBooth"; capacity 1.0; costPerUnit 35.0 }

    let manufacturingTasks : ScheduledTask<unit> list = [
        // Real-time durations (System.TimeSpan). The solver discretises time into a bounded slot
        // grid internally, so genuine hours/minutes work directly.
        scheduledTask { taskId "Welding_Job1"; duration (hours 1.0); requires "WeldingStation_A" 1.0 }
        scheduledTask { taskId "Welding_Job2"; duration (hours 1.0); requires "WeldingStation_B" 1.0 }
        scheduledTask { taskId "Assembly_Job1"; duration (hours 1.0); requires "AssemblyLine_1" 1.0 }
        // Genuine precedence: Painting can only start after Welding_Job1 AND Assembly_Job1 finish.
        scheduledTask {
            taskId "Painting_Job1"
            duration (hours 1.0)
            afterMultiple ["Welding_Job1"; "Assembly_Job1"]
            requires "PaintingBooth" 1.0
        }
    ]

    let manufacturingProblem : SchedulingProblem<unit, unit> = scheduling {
        tasks manufacturingTasks
        resources [weldStationA; weldStationB; assemblyLine; paintingBooth]
        objective MinimizeMakespan
        timeHorizon (hours 4.0)
    }

    match solveQuantum quantumBackend manufacturingProblem |> Async.RunSynchronously with
    | Ok schedule ->
        pr "Manufacturing Complete (precedence respected)"
        pr ""
        pr "  Schedule (ordered by start time):"
        let ordered = schedule.Assignments |> List.sortBy (fun a -> a.StartTime)
        ordered |> List.iter (fun a ->
            let res = a.AssignedResources |> Map.toList |> List.map fst |> String.concat ", "
            pr "    %-16s %.1fh -> %.1fh  on [%s]"
                a.TaskId a.StartTime.TotalHours a.EndTime.TotalHours res)
        pr ""
        pr "  Makespan:   %.1f hours" schedule.Makespan.TotalHours
        pr "  Total Cost: $%.2f" schedule.TotalCost

        // Validate the precedence held in the produced schedule: painting must start strictly
        // after both predecessors finish.
        let startOf id =
            ordered |> List.tryPick (fun a -> if a.TaskId = id then Some a.StartTime else None)
        match startOf "Welding_Job1", startOf "Assembly_Job1", startOf "Painting_Job1" with
        | Some w, Some asm, Some p ->
            pr "  Precedence respected: Painting after Welding (%b) and after Assembly (%b)"
                (p > w) (p > asm)
        | _ -> ()

        jsonResults <-
            (box {| example = "Manufacturing"
                    makespanHours = schedule.Makespan.TotalHours
                    totalCost = schedule.TotalCost
                    assignments = schedule.Assignments.Length |}) :: jsonResults
    | Error err ->
        pr "Error: %A" err
    pr ""

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
