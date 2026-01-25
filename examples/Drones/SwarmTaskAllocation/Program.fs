/// Drone Swarm Task Allocation Example
/// 
/// This example demonstrates how to use FSharp.Azure.Quantum's TaskScheduling API
/// to allocate and schedule tasks across a fleet of drones with dependencies and
/// resource constraints.
/// 
/// DRONE DOMAIN MAPPING:
/// - Drone tasks (delivery, inspection, surveillance) → Scheduled Tasks
/// - Drone fleet → Resources with capacity constraints
/// - Task dependencies (must complete A before B) → Precedence constraints
/// - Mission completion time → Makespan objective
/// 
/// USE CASES:
/// - Multi-drone delivery coordination
/// - Collaborative search and rescue
/// - Agricultural monitoring with multiple UAVs
/// - Infrastructure inspection campaigns
/// 
/// QUANTUM ADVANTAGE:
/// - Classical scheduling: NP-hard for resource-constrained cases
/// - Quantum QAOA: Can explore solution space more efficiently
/// - Particularly useful when combining dependencies + resource limits
namespace FSharp.Azure.Quantum.Examples.Drones.SwarmTaskAllocation

open System
open System.Diagnostics
open System.IO

open FSharp.Azure.Quantum
open FSharp.Azure.Quantum.Core
open FSharp.Azure.Quantum.Core.BackendAbstraction
open FSharp.Azure.Quantum.Backends.LocalBackend
open FSharp.Azure.Quantum.TaskScheduling.Types

open FSharp.Azure.Quantum.Examples.Common
open FSharp.Azure.Quantum.Examples.Drones.Domain

// =============================================================================
// DOMAIN TYPES
// =============================================================================

/// Type of drone task
type DroneTaskType =
    | Takeoff
    | Delivery
    | Inspection
    | Surveillance
    | EmergencyResponse
    | RelaySetup
    | ReturnToBase
    | Charging

/// A task to be performed by a drone
type DroneTask = {
    Id: string
    TaskType: DroneTaskType
    WaypointId: string
    DurationMinutes: float
    Priority: int
    PayloadKg: float
    DependsOn: string list
}

/// A drone resource
type DroneResource = {
    Id: string
    Model: string
    MaxRangeKm: float
    MaxPayloadKg: float
    BatteryCapacityWh: float
}

// =============================================================================
// DATA PARSING
// =============================================================================

module Parse =
    let private tryGet (k: string) (row: Data.CsvRow) =
        row.Values |> Map.tryFind k |> Option.map (fun s -> s.Trim())
    
    let private tryFloat (s: string option) =
        match s with
        | None -> None
        | Some v when String.IsNullOrWhiteSpace v -> None
        | Some v ->
            match Double.TryParse v with
            | true, x -> Some x
            | false, _ -> None
    
    let private tryInt (s: string option) =
        match s with
        | None -> None
        | Some v when String.IsNullOrWhiteSpace v -> None
        | Some v ->
            match Int32.TryParse v with
            | true, x -> Some x
            | false, _ -> None
    
    let private parseTaskType (s: string) : DroneTaskType option =
        match s.ToLowerInvariant().Replace("_", "") with
        | "takeoff" -> Some Takeoff
        | "delivery" -> Some Delivery
        | "inspection" -> Some Inspection
        | "surveillance" -> Some Surveillance
        | "emergencyresponse" -> Some EmergencyResponse
        | "relaysetup" -> Some RelaySetup
        | "returntobase" -> Some ReturnToBase
        | "charging" -> Some Charging
        | _ -> None
    
    let readTasks (path: string) : DroneTask list * string list =
        let rows, structuralErrors = Data.readCsvWithHeaderWithErrors path
        
        let tasks, errors =
            rows
            |> List.mapi (fun i row ->
                let rowNum = i + 2
                match tryGet "task_id" row,
                      tryGet "type" row,
                      tryGet "waypoint_id" row,
                      tryFloat (tryGet "duration_min" row),
                      tryInt (tryGet "priority" row),
                      tryFloat (tryGet "payload_kg" row) with
                | Some id, Some typeStr, Some wp, Some dur, Some pri, Some payload ->
                    match parseTaskType typeStr with
                    | Some taskType ->
                        let deps =
                            tryGet "depends_on" row
                            |> Option.map (fun s -> 
                                s.Split([|';'; ','|], StringSplitOptions.RemoveEmptyEntries)
                                |> Array.map (fun x -> x.Trim())
                                |> Array.toList)
                            |> Option.defaultValue []
                        Ok {
                            Id = id
                            TaskType = taskType
                            WaypointId = wp
                            DurationMinutes = dur
                            Priority = pri
                            PayloadKg = payload
                            DependsOn = deps
                        }
                    | None -> Error (sprintf "row=%d invalid task type '%s'" rowNum typeStr)
                | _ -> Error (sprintf "row=%d missing or invalid task fields" rowNum))
            |> List.fold
                (fun (oks, errs) r ->
                    match r with
                    | Ok v -> (v :: oks, errs)
                    | Error e -> (oks, e :: errs))
                ([], [])
        
        (List.rev tasks, structuralErrors @ (List.rev errors))
    
    let readDrones (path: string) : DroneResource list * string list =
        let rows, structuralErrors = Data.readCsvWithHeaderWithErrors path
        
        let drones, errors =
            rows
            |> List.mapi (fun i row ->
                let rowNum = i + 2
                match tryGet "drone_id" row,
                      tryGet "model" row,
                      tryFloat (tryGet "max_range_km" row),
                      tryFloat (tryGet "max_payload_kg" row),
                      tryFloat (tryGet "battery_capacity_wh" row) with
                | Some id, Some model, Some range, Some payload, Some battery ->
                    Ok {
                        Id = id
                        Model = model
                        MaxRangeKm = range
                        MaxPayloadKg = payload
                        BatteryCapacityWh = battery
                    }
                | _ -> Error (sprintf "row=%d missing or invalid drone fields" rowNum))
            |> List.fold
                (fun (oks, errs) r ->
                    match r with
                    | Ok v -> (v :: oks, errs)
                    | Error e -> (oks, e :: errs))
                ([], [])
        
        (List.rev drones, structuralErrors @ (List.rev errors))

// =============================================================================
// TASK SCHEDULING CONVERSION
// =============================================================================

module Scheduler =
    
    /// Convert drone task to FSharp.Azure.Quantum ScheduledTask
    /// Adds overhead time for takeoff/landing tasks based on domain constants
    let toScheduledTask (task: DroneTask) : ScheduledTask<DroneTaskType> =
        // Add domain-specific overhead for certain task types
        let effectiveDuration =
            match task.TaskType with
            | Takeoff -> task.DurationMinutes + Scheduling.preflightCheckDurationMin
            | ReturnToBase -> task.DurationMinutes + Scheduling.takeoffLandingOverheadMin
            | Charging -> max task.DurationMinutes Scheduling.fastChargeTo80PercentMin
            | _ -> task.DurationMinutes
        
        {
            Id = task.Id
            Value = task.TaskType
            Duration = effectiveDuration
            EarliestStart = None
            Deadline = None
            ResourceRequirements = 
                if task.PayloadKg > 0.0 then
                    Map.ofList [ ("payload_capacity", task.PayloadKg) ]
                else
                    Map.empty
            Priority = float task.Priority
            Properties = Map.ofList [ ("waypoint", task.WaypointId) ]
        }
    
    /// Convert drone to FSharp.Azure.Quantum Resource
    /// Uses domain constant for minimum ground time between operations
    let toResource (drone: DroneResource) : Resource<string> =
        {
            Id = drone.Id
            Value = drone.Model
            Capacity = drone.MaxPayloadKg
            AvailableWindows = [ (0.0, 1440.0) ]  // Available all day (in minutes)
            CostPerUnit = Scheduling.minGroundTimeMin  // Minimum turnaround time as "cost"
            Properties = Map.ofList [
                ("max_range_km", string drone.MaxRangeKm)
                ("battery_wh", string drone.BatteryCapacityWh)
            ]
        }
    
    /// Create dependency from task reference (using FinishToStart DU)
    let toDependency (fromTaskId: string) (toTaskId: string) : Dependency =
        FinishToStart (fromTaskId, toTaskId, 0.0)  // lag = 0 means tasks can start immediately after predecessor
    
    /// Build scheduling problem from drone tasks and resources
    let buildProblem (tasks: DroneTask list) (drones: DroneResource list) : SchedulingProblem<DroneTaskType, string> =
        let scheduledTasks = tasks |> List.map toScheduledTask
        let resources = drones |> List.map toResource
        
        // Extract dependencies from task definitions
        let dependencies =
            tasks
            |> List.collect (fun task ->
                task.DependsOn
                |> List.map (fun depId -> toDependency depId task.Id))
        
        // Calculate time horizon based on total task duration + buffer
        let totalDuration = tasks |> List.sumBy (fun t -> t.DurationMinutes)
        let timeHorizon = totalDuration * 2.0  // 2x buffer for scheduling flexibility
        
        {
            Tasks = scheduledTasks
            Resources = resources
            Dependencies = dependencies
            Objective = MinimizeMakespan
            TimeHorizon = timeHorizon
        }
    
    /// Solve scheduling problem (classical approach)
    let solveClassical (problem: SchedulingProblem<DroneTaskType, string>) : Async<QuantumResult<Solution>> =
        solve problem
    
    /// Solve scheduling problem with quantum backend
    let solveWithQuantum (backend: IQuantumBackend) (problem: SchedulingProblem<DroneTaskType, string>) : Async<QuantumResult<Solution>> =
        solveQuantum backend problem

// =============================================================================
// VISUALIZATION
// =============================================================================

module Visualization =
    
    /// Generate ASCII Gantt chart
    let generateGanttChart (solution: Solution) (timeScale: float) : string =
        let sb = System.Text.StringBuilder()
        
        sb.AppendLine("") |> ignore
        sb.AppendLine("╔════════════════════════════════════════════════════════════════════════════╗") |> ignore
        sb.AppendLine("║  TASK SCHEDULE GANTT CHART                                                 ║") |> ignore
        sb.AppendLine("╠════════════════════════════════════════════════════════════════════════════╣") |> ignore
        
        // Time axis
        let maxTime = solution.Makespan
        let numTicks = min 20 (int (maxTime / timeScale))
        let tickWidth = 3
        
        sb.Append("║ Task         |") |> ignore
        for i in 0..numTicks do
            sb.Append(sprintf "%3d" (int (float i * timeScale))) |> ignore
        sb.AppendLine(" (min)") |> ignore
        
        sb.Append("║ -------------|") |> ignore
        for _ in 0..numTicks do
            sb.Append("---") |> ignore
        sb.AppendLine("") |> ignore
        
        // Task bars
        for assignment in solution.Assignments |> List.sortBy (fun a -> a.StartTime) do
            let startPos = int (assignment.StartTime / timeScale)
            let endPos = int (assignment.EndTime / timeScale)
            let barLength = max 1 (endPos - startPos)
            
            let taskName = 
                if assignment.TaskId.Length > 12 then assignment.TaskId.Substring(0, 12)
                else assignment.TaskId.PadRight(12)
            
            sb.Append(sprintf "║ %s |" taskName) |> ignore
            
            for i in 0..numTicks do
                if i >= startPos && i < startPos + barLength then
                    sb.Append("███") |> ignore
                else
                    sb.Append("   ") |> ignore
            
            sb.AppendLine("") |> ignore
        
        sb.AppendLine("╚════════════════════════════════════════════════════════════════════════════╝") |> ignore
        
        sb.ToString()

// =============================================================================
// METRICS
// =============================================================================

type Metrics = {
    run_id: string
    tasks_path: string
    drones_path: string
    tasks_sha256: string
    drones_sha256: string
    task_count: int
    drone_count: int
    dependency_count: int
    method_used: string
    makespan_min: float
    total_idle_time_min: float
    resource_utilization: float
    elapsed_ms: int64
}

// =============================================================================
// MAIN PROGRAM
// =============================================================================

module Program =
    
    [<EntryPoint>]
    let main argv =
        let args = Cli.parse argv
        
        if Cli.hasFlag "help" args || Cli.hasFlag "h" args then
            printfn "╔════════════════════════════════════════════════════════════╗"
            printfn "║  DRONE SWARM TASK ALLOCATION                               ║"
            printfn "║  Quantum-Enhanced Scheduling Optimization                  ║"
            printfn "╠════════════════════════════════════════════════════════════╣"
            printfn "║  Allocates and schedules tasks across a drone fleet        ║"
            printfn "║  respecting dependencies and resource constraints.         ║"
            printfn "╠════════════════════════════════════════════════════════════╣"
            printfn "║  OPTIONS:                                                  ║"
            printfn "║    --tasks <path>    CSV file with task definitions        ║"
            printfn "║    --drones <path>   CSV file with drone specifications    ║"
            printfn "║    --out <dir>       Output directory for results          ║"
            printfn "║    --method <m>      classical | quantum (default: class.) ║"
            printfn "║    --help            Show this help                        ║"
            printfn "╚════════════════════════════════════════════════════════════╝"
            0
        else
            let sw = Stopwatch.StartNew()
            
            let tasksPath = Cli.getOr "tasks" "examples/Drones/_data/tasks.csv" args
            let dronesPath = Cli.getOr "drones" "examples/Drones/_data/drones.csv" args
            let outDir = Cli.getOr "out" (Path.Combine("runs", "drone", "swarm-task-allocation")) args
            let method = Cli.getOr "method" "classical" args
            
            Data.ensureDirectory outDir
            let runId = DateTimeOffset.UtcNow.ToString("yyyyMMdd_HHmmss")
            
            printfn ""
            printfn "╔════════════════════════════════════════════════════════════╗"
            printfn "║  DRONE SWARM TASK ALLOCATION                               ║"
            printfn "║  FSharp.Azure.Quantum Example                              ║"
            printfn "╚════════════════════════════════════════════════════════════╝"
            printfn ""
            printfn "Loading tasks from: %s" tasksPath
            printfn "Loading drones from: %s" dronesPath
            printfn "Method: %s" method
            printfn ""
            
            // Read input data
            let tasks, taskErrors = Parse.readTasks tasksPath
            let drones, droneErrors = Parse.readDrones dronesPath
            
            if not taskErrors.IsEmpty then
                printfn "⚠ Task parsing errors:"
                taskErrors |> List.iter (printfn "  - %s")
            
            if not droneErrors.IsEmpty then
                printfn "⚠ Drone parsing errors:"
                droneErrors |> List.iter (printfn "  - %s")
            
            if tasks.IsEmpty then
                printfn "❌ No tasks loaded. Exiting."
                1
            else
                printfn "Loaded %d tasks with %d dependencies" tasks.Length (tasks |> List.sumBy (fun t -> t.DependsOn.Length))
                printfn "Loaded %d drones" drones.Length
                printfn ""
                
                // Build and solve scheduling problem
                let problem = Scheduler.buildProblem tasks drones
                
                printfn "Solving task allocation problem..."
                printfn ""
                
                let methodUsed, result =
                    match method.ToLowerInvariant() with
                    | "quantum" ->
                        let backend = LocalBackend() :> IQuantumBackend
                        ("Quantum (LocalBackend)", Scheduler.solveWithQuantum backend problem |> Async.RunSynchronously)
                    | _ ->
                        ("Classical (Topological Sort)", Scheduler.solveClassical problem |> Async.RunSynchronously)
                
                // Helper to get primary resource from AssignedResources map
                let getPrimaryResource (assignedResources: Map<string, float>) : string =
                    assignedResources
                    |> Map.toSeq
                    |> Seq.tryHead
                    |> Option.map fst
                    |> Option.defaultValue "unassigned"
                
                match result with
                | Error e ->
                    printfn "❌ Scheduling failed: %s" e.Message
                    1
                | Ok solution ->
                    sw.Stop()
                    
                    // Print results
                    printfn "╔════════════════════════════════════════════════════════════╗"
                    printfn "║  SCHEDULING RESULTS                                        ║"
                    printfn "╠════════════════════════════════════════════════════════════╣"
                    printfn "║  Method: %-48s ║" methodUsed
                    printfn "║  Makespan: %8.1f minutes                                ║" solution.Makespan
                    printfn "║  Tasks Scheduled: %3d                                      ║" solution.Assignments.Length
                    printfn "╠════════════════════════════════════════════════════════════╣"
                    printfn "║  TASK ASSIGNMENTS:                                         ║"
                    
                    solution.Assignments
                    |> List.sortBy (fun a -> a.StartTime)
                    |> List.iter (fun assignment ->
                        let resource = getPrimaryResource assignment.AssignedResources
                        printfn "║  %-12s │ Start: %6.1f │ End: %6.1f │ %s ║" 
                            assignment.TaskId assignment.StartTime assignment.EndTime resource)
                    
                    printfn "╚════════════════════════════════════════════════════════════╝"
                    
                    // Gantt chart
                    let gantt = Visualization.generateGanttChart solution 5.0
                    printfn "%s" gantt
                    
                    // Calculate metrics
                    let totalTaskTime = solution.Assignments |> List.sumBy (fun a -> a.EndTime - a.StartTime)
                    let totalSlotTime = solution.Makespan * float (max 1 drones.Length)
                    let utilization = if totalSlotTime > 0.0 then totalTaskTime / totalSlotTime else 0.0
                    let idleTime = totalSlotTime - totalTaskTime
                    
                    let tasksSha = Data.fileSha256Hex tasksPath
                    let dronesSha = Data.fileSha256Hex dronesPath
                    
                    let metrics: Metrics = {
                        run_id = runId
                        tasks_path = tasksPath
                        drones_path = dronesPath
                        tasks_sha256 = tasksSha
                        drones_sha256 = dronesSha
                        task_count = tasks.Length
                        drone_count = drones.Length
                        dependency_count = tasks |> List.sumBy (fun t -> t.DependsOn.Length)
                        method_used = methodUsed
                        makespan_min = solution.Makespan
                        total_idle_time_min = idleTime
                        resource_utilization = utilization
                        elapsed_ms = sw.ElapsedMilliseconds
                    }
                    
                    Reporting.writeJson (Path.Combine(outDir, "metrics.json")) metrics
                    
                    // Write schedule as CSV
                    let scheduleRows =
                        solution.Assignments
                        |> List.sortBy (fun a -> a.StartTime)
                        |> List.map (fun a ->
                            [ a.TaskId
                              sprintf "%.1f" a.StartTime
                              sprintf "%.1f" a.EndTime
                              sprintf "%.1f" (a.EndTime - a.StartTime)
                              getPrimaryResource a.AssignedResources ])
                    
                    Reporting.writeCsv
                        (Path.Combine(outDir, "schedule.csv"))
                        [ "task_id"; "start_time_min"; "end_time_min"; "duration_min"; "resource_id" ]
                        scheduleRows
                    
                    // Write Gantt chart
                    Reporting.writeTextFile (Path.Combine(outDir, "gantt.txt")) gantt
                    
                    // Write report
                    let report = $"""# Drone Swarm Task Allocation Results

## Summary

- **Run ID**: {runId}
- **Method**: {methodUsed}
- **Tasks**: {tasks.Length}
- **Drones**: {drones.Length}
- **Dependencies**: {tasks |> List.sumBy (fun t -> t.DependsOn.Length)}
- **Makespan**: {solution.Makespan:F1} minutes
- **Resource Utilization**: {utilization * 100.0:F1}%%
- **Elapsed Time**: {sw.ElapsedMilliseconds} ms

## Task Schedule

| Task | Start (min) | End (min) | Duration | Resource |
|------|-------------|-----------|----------|----------|
{solution.Assignments |> List.sortBy (fun a -> a.StartTime) |> List.map (fun a -> sprintf "| %s | %.1f | %.1f | %.1f | %s |" a.TaskId a.StartTime a.EndTime (a.EndTime - a.StartTime) (getPrimaryResource a.AssignedResources)) |> String.concat "\n"}

## Quantum Computing Context

This example demonstrates mapping drone task allocation to **Resource-Constrained Scheduling**:

- **Classical approach**: Topological sort + greedy assignment
- **Quantum approach**: QUBO encoding + QAOA optimization

Key constraints handled:
- **Precedence**: Tasks must respect dependency order
- **Resource capacity**: Drones have limited payload capacity
- **Makespan minimization**: Complete all tasks as quickly as possible

## Files Generated

- `metrics.json` - Performance metrics
- `schedule.csv` - Task assignments with timing
- `gantt.txt` - ASCII Gantt chart visualization
"""
                    
                    Reporting.writeTextFile (Path.Combine(outDir, "run-report.md")) report
                    
                    printfn "Results written to: %s" outDir
                    0
