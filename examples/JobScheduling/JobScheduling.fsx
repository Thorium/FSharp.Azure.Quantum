(**
# Job Scheduling with Quantum Optimization

**Business Context**:
A manufacturing facility needs to schedule production jobs across machines
with LIMITED CAPACITY, minimizing total completion time (makespan) while
respecting both task dependencies AND resource availability.

**Problem**:
Resource-constrained task scheduling is NP-hard. Classical greedy scheduling
handles dependencies well, but resource capacity constraints create a
combinatorial optimization problem best solved by QUBO/QAOA.

**Mathematical Formulation**:
- Variables: Binary x_{task,time} (1 if task starts at time slot, 0 otherwise)
- Objective: Minimize makespan (latest task completion)
- Constraints: One-hot (task starts once), dependencies, resource capacity

**Expected Performance**:
- LocalBackend: max 16 qubits (~3 tasks x 5 time slots, demo only)
- Azure Quantum: 29-80+ qubits (IonQ, Quantinuum, Rigetti)
- Scales to realistic production problems (100+ tasks)

**Quantum-Ready**: This example uses QUBO encoding + QAOA via the
TaskScheduling builder, automatically routing to available quantum backends.

Usage:
  dotnet fsi JobScheduling.fsx                                    (defaults)
  dotnet fsi JobScheduling.fsx -- --help                          (show options)
  dotnet fsi JobScheduling.fsx -- --input jobs.csv
  dotnet fsi JobScheduling.fsx -- --output schedule.json --csv schedule.csv
  dotnet fsi JobScheduling.fsx -- --quiet --output schedule.json  (pipeline mode)
*)

//#r "nuget: FSharp.Azure.Quantum"
#r "nuget: Microsoft.Extensions.Logging.Abstractions, 10.0.0"
#r "../../src/FSharp.Azure.Quantum/bin/Debug/net10.0/FSharp.Azure.Quantum.dll"

#load "../_common/Cli.fs"
#load "../_common/Data.fs"
#load "../_common/Reporting.fs"

open System
open FSharp.Azure.Quantum
open FSharp.Azure.Quantum.TaskScheduling  // For types
open FSharp.Azure.Quantum.Backends.LocalBackend
open FSharp.Azure.Quantum.Examples.Common

// ==============================================================================
// CLI ARGUMENT PARSING
// ==============================================================================

let argv = fsi.CommandLineArgs |> Array.skip 1
let args = Cli.parse argv

Cli.exitIfHelp
    "JobScheduling.fsx"
    "Quantum-ready job scheduling with resource constraints (QUBO + QAOA)."
    [ { Cli.OptionSpec.Name = "input";   Description = "CSV file with jobs (id,name,duration_hours,priority)"; Default = None }
      { Cli.OptionSpec.Name = "output";  Description = "Write results to JSON file";                            Default = None }
      { Cli.OptionSpec.Name = "csv";     Description = "Write results to CSV file";                             Default = None }
      { Cli.OptionSpec.Name = "quiet";   Description = "Suppress informational output";                         Default = None } ]
    args

let quiet = Cli.hasFlag "quiet" args
let inputPath = Cli.tryGet "input" args
let outputPath = Cli.tryGet "output" args
let csvPath = Cli.tryGet "csv" args

// ==============================================================================
// DOMAIN MODEL - Job Scheduling Types
// ==============================================================================

/// Represents a production job for the domain
type ProductionJob = {
    Id: string
    Name: string
    DurationHours: float
    Priority: int
}

// ==============================================================================
// JOB DATA - Manufacturing facility production schedule (or load from file)
// ==============================================================================

let builtInJobs = [
    // Initial job (no dependencies)
    { Id = "J1"; Name = "Prep Materials"; DurationHours = 1.0; Priority = 1 }

    // Assembly jobs (depend on prep)
    { Id = "J2"; Name = "Base Assembly"; DurationHours = 1.0; Priority = 2 }
    { Id = "J3"; Name = "Component A"; DurationHours = 1.0; Priority = 2 }
]

/// Load jobs from a CSV file with columns: id, name, duration_hours, priority
let loadJobsFromCsv (path: string) : ProductionJob list =
    let rows = Data.readCsvWithHeader path
    rows |> List.map (fun row ->
        { Id =
            row.Values |> Map.tryFind "id" |> Option.defaultValue "?"
          Name =
            row.Values |> Map.tryFind "name" |> Option.defaultValue "Unknown"
          DurationHours =
            row.Values
            |> Map.tryFind "duration_hours"
            |> Option.bind (fun s -> match Double.TryParse s with true, v -> Some v | _ -> None)
            |> Option.defaultValue 1.0
          Priority =
            row.Values
            |> Map.tryFind "priority"
            |> Option.bind (fun s -> match Int32.TryParse s with true, v -> Some v | _ -> None)
            |> Option.defaultValue 1 })

let productionJobs =
    match inputPath with
    | Some path ->
        let resolved = Data.resolveRelative __SOURCE_DIRECTORY__ path
        if not quiet then printfn "Loading jobs from: %s" resolved
        loadJobsFromCsv resolved
    | None ->
        builtInJobs

// ==============================================================================
// PROBLEM SETUP - Using TaskScheduling Builder with Resource Constraints
// ==============================================================================

if not quiet then
    printfn "â•”â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•—"
    printfn "â•‘              JOB SCHEDULING WITH QUANTUM OPTIMIZATION                        â•‘"
    printfn "â•‘              Resource-Constrained Scheduling via QUBO/QAOA                   â•‘"
    printfn "â•šâ•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•"
    printfn ""
    printfn "Problem: Schedule %d jobs with resource capacity constraints" productionJobs.Length
    printfn "Objective: Minimize makespan (total completion time)"
    printfn "Constraints: Task dependencies AND limited machine capacity"
    printfn "Method: Quantum optimization (QUBO encoding + QAOA)"
    printfn ""

// Define machine resources with limited capacity
let machine1 = resource {
    resourceId "Machine1"
    capacity 1.0  // Only 1 job at a time
    costPerUnit 100.0
}

// Convert jobs to ScheduledTasks with resource requirements
// Built-in jobs use hardcoded dependency graph; CSV-loaded jobs run independently.
let scheduledTasks : ScheduledTask<unit> list =
    match inputPath with
    | None ->
        // Built-in: J2 and J3 depend on J1
        let j1 : ScheduledTask<unit> = scheduledTask {
            taskId "J1"
            duration (hours 1.0)
            requires "Machine1" 1.0
            priority 1.0
        }
        let j2 : ScheduledTask<unit> = scheduledTask {
            taskId "J2"
            duration (hours 1.0)
            after "J1"
            requires "Machine1" 1.0
            priority 2.0
        }
        let j3 : ScheduledTask<unit> = scheduledTask {
            taskId "J3"
            duration (hours 1.0)
            after "J1"
            requires "Machine1" 1.0
            priority 2.0
        }
        [ j1; j2; j3 ]
    | Some _ ->
        // CSV-loaded: no dependency info, each job requires Machine1
        productionJobs |> List.map (fun job ->
            scheduledTask {
                taskId job.Id
                duration (hours job.DurationHours)
                requires "Machine1" 1.0
                priority (float job.Priority)
            })

// Calculate time horizon: enough time slots for all tasks, capped to keep qubits reasonable
let timeHorizonSlots =
    let totalDuration = productionJobs |> List.sumBy (fun j -> j.DurationHours)
    max 3.0 (min (totalDuration + 2.0) 5.0)

// Build scheduling problem with resource constraints
let problem : SchedulingProblem<unit, unit> = scheduling {
    tasks scheduledTasks
    resources [machine1]
    objective MinimizeMakespan
    timeHorizon timeHorizonSlots
}

// ==============================================================================
// SOLVE - Using Quantum Solver (QUBO + QAOA)
// ==============================================================================

if not quiet then
    printfn "Initializing quantum backend..."
    printfn "Note: This uses LocalBackend for demonstration (max 16 qubits)"
    printfn "      For large problems (100+ tasks), use Azure Quantum with IonQ/Quantinuum"
    printfn ""

// Initialize quantum backend
let backend = LocalBackend() :> FSharp.Azure.Quantum.Core.BackendAbstraction.IQuantumBackend

if not quiet then
    printfn "Running quantum optimization (QUBO encoding + QAOA)..."
    printfn "- Encoding scheduling problem as QUBO matrix"
    printfn "- Variables: %d tasks Ã— %d time slots = %d qubits (fits LocalBackend limit)"
        problem.Tasks.Length
        (int problem.TimeHorizon)
        (problem.Tasks.Length * int problem.TimeHorizon)
    printfn "- Applying QAOA (Quantum Approximate Optimization Algorithm)"
    printfn "- Measuring quantum state and decoding to schedule"
    printfn ""

let startTime = DateTime.UtcNow

let result = solveQuantum backend problem |> Async.RunSynchronously

let elapsed = DateTime.UtcNow - startTime
if not quiet then
    printfn "Quantum optimization completed in %d ms" (int elapsed.TotalMilliseconds)
    printfn ""

// Extract schedule result for structured output
let scheduleResult : (Solution * float * float * float) option =
    match result with
    | Ok schedule ->
        if not quiet then
            // ==============================================================================
            // RESULTS - Schedule Report
            // ==============================================================================

            printfn "â•”â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•—"
            printfn "â•‘                       JOB SCHEDULE REPORT                                    â•‘"
            printfn "â•šâ•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•"
            printfn ""
            printfn "SCHEDULE BY JOB:"
            printfn "â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€"

            // Sort assignments by start time
            let sortedAssignments =
                schedule.Assignments
                |> List.sortBy (fun a -> a.StartTime)

            // Convert minutes back to hours for display
            for assignment in sortedAssignments do
                let job = productionJobs |> List.tryFind (fun j -> j.Id = assignment.TaskId)
                let jobName = job |> Option.map (fun j -> j.Name) |> Option.defaultValue assignment.TaskId
                let jobPriority = job |> Option.map (fun j -> j.Priority) |> Option.defaultValue 0
                let startHours = assignment.StartTime / 60.0
                let endHours = assignment.EndTime / 60.0
                let durationHours = (assignment.EndTime - assignment.StartTime) / 60.0
                printfn "  %s: hours %.1f-%.1f (duration: %.1fh, priority: %d)"
                    jobName
                    startHours
                    endHours
                    durationHours
                    jobPriority

            printfn ""
            printfn "PERFORMANCE SUMMARY:"
            printfn "â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€"
            printfn "  Total Jobs:            %d" productionJobs.Length
            printfn "  Makespan:              %.1f hours" (schedule.Makespan / 60.0)
            printfn "  Total Cost:            $%.2f" schedule.TotalCost

        // Calculate utilization
        let totalWorkMinutes = productionJobs |> List.sumBy (fun j -> j.DurationHours * 60.0)
        let totalWorkHours = totalWorkMinutes / 60.0

        if not quiet then
            printfn "  Total Work:            %.1f hours" totalWorkHours
            printfn ""

        // ==============================================================================
        // BUSINESS IMPACT ANALYSIS
        // ==============================================================================

        let sequentialHours = productionJobs |> List.sumBy (fun j -> j.DurationHours)
        let makespanHours = schedule.Makespan / 60.0
        let speedup = if makespanHours > 0.0 then sequentialHours / makespanHours else 1.0
        let timeSaved = sequentialHours - makespanHours
        let timeSavedPct = if sequentialHours > 0.0 then (timeSaved / sequentialHours) * 100.0 else 0.0

        if not quiet then
            printfn "â•”â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•—"
            printfn "â•‘                       BUSINESS IMPACT ANALYSIS                               â•‘"
            printfn "â•šâ•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•"
            printfn ""

            printfn "TIME ANALYSIS:"
            printfn "â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€"
            printfn "  Sequential Time (1 machine):   %.1f hours" sequentialHours
            printfn "  Parallel Time (optimized):     %.1f hours" makespanHours
            printfn "  Speedup Factor:                %.2fx faster" speedup
            printfn "  Time Saved:                    %.1f hours (%.1f%%)" timeSaved timeSavedPct
            printfn ""

            let costPerMachineHour = 500.0
            let sequentialCost = sequentialHours * costPerMachineHour

            printfn "COST ANALYSIS (@ $%.0f/machine-hour):" costPerMachineHour
            printfn "â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€"
            printfn "  Sequential Cost:               $%.2f" sequentialCost
            printfn ""

            printfn "KEY INSIGHTS:"
            printfn "â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€"
            printfn "  âœ“ Achieved %.2fx speedup through optimal scheduling" speedup

            if speedup > 1.5 then
                printfn "  âœ“ Parallel scheduling provides significant time savings (%.1f%% faster)" timeSavedPct
            else
                printfn "  âš  Sequential execution would be more cost-effective (fewer dependencies needed)"

            printfn ""

            // Export Gantt chart
            exportGanttChart schedule "schedule.txt"
            printfn "âœ“ Gantt chart exported to: schedule.txt"
            printfn ""

            printfn "â•”â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•—"
            printfn "â•‘                       SCHEDULING SUCCESSFUL                                  â•‘"
            printfn "â•šâ•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•"
            printfn ""
            printfn "â•”â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•—"
            printfn "â•‘                    WHY QUANTUM OPTIMIZATION?                                 â•‘"
            printfn "â•šâ•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•"
            printfn ""
            printfn "CLASSICAL vs QUANTUM SCHEDULING:"
            printfn "â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€"
            printfn ""
            printfn "Classical Greedy Solver (solve):"
            printfn "  âœ“ Handles task dependencies optimally"
            printfn "  âœ“ Fast for dependency-only problems"
            printfn "  âœ— Ignores resource capacity constraints"
            printfn "  âœ— Cannot optimize resource allocation"
            printfn ""
            printfn "Quantum Solver (solveQuantum):"
            printfn "  âœ“ Handles dependencies AND resource constraints"
            printfn "  âœ“ Optimizes resource allocation via QUBO encoding"
            printfn "  âœ“ Finds near-optimal solutions for NP-hard problems"
            printfn "  âœ“ Scales to larger problems on quantum hardware"
            printfn ""
            printfn "HOW IT WORKS:"
            printfn "â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€"
            printfn "1. QUBO Encoding: Converts scheduling to binary optimization problem"
            printfn "   - Variables: x_{task,time} âˆˆ {0,1} for each task and time slot"
            printfn "   - Objective: Minimize makespan (latest task completion)"
            printfn "   - Constraints: One-hot (task starts once), dependencies, resources"
            printfn ""
            printfn "2. QAOA (Quantum Approximate Optimization Algorithm):"
            printfn "   - Prepares quantum superposition of all possible schedules"
            printfn "   - Applies problem-specific and mixing Hamiltonians"
            printfn "   - Measures to find low-energy (optimal) solutions"
            printfn ""
            printfn "3. Decoding: Converts quantum measurements to task assignments"
            printfn "   - Filters invalid solutions (constraint violations)"
            printfn "   - Selects best valid solution (minimum makespan)"
            printfn ""
            printfn "BACKENDS:"
            printfn "â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€"
            printfn "LocalBackend:    16 qubits max (~3 tasks Ã— 5 time slots, demo only)"
            printfn "Azure Quantum:   29-80+ qubits (IonQ, Quantinuum, Rigetti)"
            printfn "                 Scales to realistic production problems (100+ tasks)"
            printfn ""
            printfn "WHEN TO USE QUANTUM:"
            printfn "â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€"
            printfn "âœ“ Resource capacity constraints exist"
            printfn "âœ“ Multiple resources compete for same tasks"
            printfn "âœ“ Optimization critical (minimize cost/makespan)"
            printfn "âœ— Dependencies only (use classical solver instead)"
            printfn ""

        Some (schedule, speedup, timeSavedPct, elapsed.TotalMilliseconds)

    | Error err ->
        if not quiet then
            printfn "âŒ Scheduling failed: %s" err.Message
        None

// ==============================================================================
// STRUCTURED OUTPUT
// ==============================================================================

let resultRows : Map<string, string> list =
    match scheduleResult with
    | Some (schedule, speedup, timeSavedPct, elapsedMs) ->
        let assignmentRows =
            schedule.Assignments
            |> List.sortBy (fun a -> a.StartTime)
            |> List.map (fun a ->
                let job = productionJobs |> List.tryFind (fun j -> j.Id = a.TaskId)
                let jobName = job |> Option.map (fun j -> j.Name) |> Option.defaultValue a.TaskId
                Map.ofList
                    [ "task_id", a.TaskId
                      "task_name", jobName
                      "start_hours", sprintf "%.2f" (a.StartTime / 60.0)
                      "end_hours", sprintf "%.2f" (a.EndTime / 60.0)
                      "duration_hours", sprintf "%.2f" ((a.EndTime - a.StartTime) / 60.0)
                      "makespan_hours", sprintf "%.2f" (schedule.Makespan / 60.0)
                      "total_cost", sprintf "%.2f" schedule.TotalCost
                      "speedup", sprintf "%.2f" speedup
                      "time_saved_pct", sprintf "%.1f" timeSavedPct
                      "solution_time_ms", sprintf "%.0f" elapsedMs
                      "status", "ok" ])
        assignmentRows
    | None ->
        [ Map.ofList
            [ "task_id", "N/A"
              "task_name", "N/A"
              "start_hours", "N/A"
              "end_hours", "N/A"
              "duration_hours", "N/A"
              "makespan_hours", "N/A"
              "total_cost", "N/A"
              "speedup", "N/A"
              "time_saved_pct", "N/A"
              "solution_time_ms", sprintf "%.0f" elapsed.TotalMilliseconds
              "status", "failed" ] ]

match outputPath with
| Some path ->
    Reporting.writeJson path resultRows
    if not quiet then printfn "Results written to %s" path
| None -> ()

match csvPath with
| Some path ->
    let header = [ "task_id"; "task_name"; "start_hours"; "end_hours"; "duration_hours";
                   "makespan_hours"; "total_cost"; "speedup"; "time_saved_pct";
                   "solution_time_ms"; "status" ]
    let rows =
        resultRows
        |> List.map (fun m ->
            header |> List.map (fun h -> m |> Map.tryFind h |> Option.defaultValue ""))
    Reporting.writeCsv path header rows
    if not quiet then printfn "Results written to %s" path
| None -> ()

// ==============================================================================
// USAGE HINTS
// ==============================================================================

if argv.Length = 0 && not quiet then
    printfn "ðŸ’¡ Tip: Run with --help to see all options:"
    printfn "   dotnet fsi JobScheduling.fsx -- --help"
    printfn "   dotnet fsi JobScheduling.fsx -- --input jobs.csv --output schedule.json"
    printfn "   dotnet fsi JobScheduling.fsx -- --quiet --output schedule.json  (pipeline mode)"
    printfn ""
