// ==============================================================================
// Job Scheduling Example
// ==============================================================================
// Demonstrates resource allocation and task scheduling with dependencies using
// the FSharp.Azure.Quantum TaskScheduling builder with quantum-ready architecture.
//
// Business Context:
// A manufacturing facility needs to schedule 10 production jobs across 3 machines,
// minimizing total completion time (makespan) while respecting task dependencies.
// This is a classic constraint satisfaction and optimization problem.
//
// This example shows:
// - Dependency graph modeling (DAG - Directed Acyclic Graph)
// - Resource allocation using TaskScheduling builder
// - Makespan minimization (total completion time)
// - Machine utilization analysis
// - Quantum-ready QUBO encoding for large-scale problems
// ==============================================================================

#r "../../src/FSharp.Azure.Quantum/bin/Debug/net10.0/FSharp.Azure.Quantum.dll"

open System
open FSharp.Azure.Quantum.TaskScheduling

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
// JOB DATA - Manufacturing facility production schedule
// ==============================================================================

let productionJobs = [
    // Initial preparation jobs (no dependencies)
    { Id = "J1"; Name = "Prep Materials"; DurationHours = 3.0; Priority = 1 }
    { Id = "J5"; Name = "Quality Check"; DurationHours = 2.0; Priority = 1 }
    
    // Assembly stage 1 (depends on prep)
    { Id = "J2"; Name = "Base Assembly"; DurationHours = 4.0; Priority = 2 }
    { Id = "J3"; Name = "Component A"; DurationHours = 3.0; Priority = 2 }
    
    // Assembly stage 2 (depends on stage 1)
    { Id = "J4"; Name = "Integration"; DurationHours = 5.0; Priority = 3 }
    { Id = "J6"; Name = "Component B"; DurationHours = 4.0; Priority = 2 }
    
    // Final assembly (depends on all previous stages)
    { Id = "J7"; Name = "Final Assembly"; DurationHours = 6.0; Priority = 4 }
    
    // Testing and packaging
    { Id = "J8"; Name = "Testing"; DurationHours = 3.0; Priority = 5 }
    { Id = "J9"; Name = "Packaging"; DurationHours = 2.0; Priority = 6 }
    { Id = "J10"; Name = "Shipping"; DurationHours = 2.0; Priority = 7 }
]

// ==============================================================================
// PROBLEM SETUP - Using TaskScheduling Builder
// ==============================================================================

printfn "╔══════════════════════════════════════════════════════════════════════════════╗"
printfn "║                   JOB SCHEDULING OPTIMIZATION EXAMPLE                        ║"
printfn "║                   Using TaskScheduling Builder (Quantum-Ready)               ║"
printfn "╚══════════════════════════════════════════════════════════════════════════════╝"
printfn ""
printfn "Problem: Schedule %d jobs across 3 machines" productionJobs.Length
printfn "Objective: Minimize makespan (total completion time)"
printfn "Constraints: Respect job dependencies"
printfn ""

// Convert jobs to ScheduledTasks using computation expressions
let j1 : ScheduledTask<unit> = scheduledTask {
    id "J1"
    duration (hours 3.0)
    priority 1.0
}

let j2 : ScheduledTask<unit> = scheduledTask {
    id "J2"
    duration (hours 4.0)
    after "J1"
    priority 2.0
}

let j3 : ScheduledTask<unit> = scheduledTask {
    id "J3"
    duration (hours 3.0)
    after "J1"
    priority 2.0
}

let j4 : ScheduledTask<unit> = scheduledTask {
    id "J4"
    duration (hours 5.0)
    afterMultiple ["J2"; "J3"]
    priority 3.0
}

let j5 : ScheduledTask<unit> = scheduledTask {
    id "J5"
    duration (hours 2.0)
    priority 1.0
}

let j6 : ScheduledTask<unit> = scheduledTask {
    id "J6"
    duration (hours 4.0)
    after "J5"
    priority 2.0
}

let j7 : ScheduledTask<unit> = scheduledTask {
    id "J7"
    duration (hours 6.0)
    afterMultiple ["J4"; "J6"]
    priority 4.0
}

let j8 : ScheduledTask<unit> = scheduledTask {
    id "J8"
    duration (hours 3.0)
    after "J7"
    priority 5.0
}

let j9 : ScheduledTask<unit> = scheduledTask {
    id "J9"
    duration (hours 2.0)
    after "J8"
    priority 6.0
}

let j10 : ScheduledTask<unit> = scheduledTask {
    id "J10"
    duration (hours 2.0)
    after "J9"
    priority 7.0
}

// Build scheduling problem using the computation expression
let problem : SchedulingProblem<unit, unit> = scheduling {
    tasks [j1; j2; j3; j4; j5; j6; j7; j8; j9; j10]
    objective MinimizeMakespan
    timeHorizon (100.0 * 60.0)  // 100 hours in minutes
}

// ==============================================================================
// SOLVE - Using classical solver (quantum for large-scale)
// ==============================================================================

printfn "Running scheduling optimization..."
let startTime = DateTime.UtcNow

let result = solve problem |> Async.RunSynchronously

let elapsed = DateTime.UtcNow - startTime
printfn "Completed in %d ms" (int elapsed.TotalMilliseconds)
printfn ""

match result with
| Ok schedule ->
    // ==============================================================================
    // RESULTS - Schedule Report
    // ==============================================================================
    
    printfn "╔══════════════════════════════════════════════════════════════════════════════╗"
    printfn "║                       JOB SCHEDULE REPORT                                    ║"
    printfn "╚══════════════════════════════════════════════════════════════════════════════╝"
    printfn ""
    printfn "SCHEDULE BY JOB:"
    printfn "────────────────────────────────────────────────────────────────────────────────"
    
    // Sort assignments by start time
    let sortedAssignments =
        schedule.Assignments
        |> List.sortBy (fun a -> a.StartTime)
    
    // Convert minutes back to hours for display
    for assignment in sortedAssignments do
        let job = productionJobs |> List.find (fun j -> j.Id = assignment.TaskId)
        let startHours = assignment.StartTime / 60.0
        let endHours = assignment.EndTime / 60.0
        let durationHours = (assignment.EndTime - assignment.StartTime) / 60.0
        printfn "  %s: hours %.1f-%.1f (duration: %.1fh, priority: %d)" 
            job.Name 
            startHours 
            endHours 
            durationHours
            job.Priority
    
    printfn ""
    printfn "PERFORMANCE SUMMARY:"
    printfn "────────────────────────────────────────────────────────────────────────────────"
    printfn "  Total Jobs:            %d" productionJobs.Length
    printfn "  Makespan:              %.1f hours" (schedule.Makespan / 60.0)
    printfn "  Total Cost:            $%.2f" schedule.TotalCost
    
    // Calculate utilization
    let totalWorkMinutes = productionJobs |> List.sumBy (fun j -> j.DurationHours * 60.0)
    let totalWorkHours = totalWorkMinutes / 60.0
    
    printfn "  Total Work:            %.1f hours" totalWorkHours
    printfn ""
    
    // ==============================================================================
    // BUSINESS IMPACT ANALYSIS
    // ==============================================================================
    
    printfn "╔══════════════════════════════════════════════════════════════════════════════╗"
    printfn "║                       BUSINESS IMPACT ANALYSIS                               ║"
    printfn "╚══════════════════════════════════════════════════════════════════════════════╝"
    printfn ""
    
    // Calculate sequential time (all jobs on one machine)
    let sequentialHours = productionJobs |> List.sumBy (fun j -> j.DurationHours)
    let makespanHours = schedule.Makespan / 60.0
    let speedup = sequentialHours / makespanHours
    let timeSaved = sequentialHours - makespanHours
    let timeSavedPct = (timeSaved / sequentialHours) * 100.0
    
    printfn "TIME ANALYSIS:"
    printfn "────────────────────────────────────────────────────────────────────────────────"
    printfn "  Sequential Time (1 machine):   %.1f hours" sequentialHours
    printfn "  Parallel Time (optimized):     %.1f hours" makespanHours
    printfn "  Speedup Factor:                %.2fx faster" speedup
    printfn "  Time Saved:                    %.1f hours (%.1f%%)" timeSaved timeSavedPct
    printfn ""
    
    let costPerMachineHour = 500.0
    let sequentialCost = sequentialHours * costPerMachineHour
    
    printfn "COST ANALYSIS (@ $%.0f/machine-hour):" costPerMachineHour
    printfn "────────────────────────────────────────────────────────────────────────────────"
    printfn "  Sequential Cost:               $%.2f" sequentialCost
    printfn ""
    
    printfn "KEY INSIGHTS:"
    printfn "────────────────────────────────────────────────────────────────────────────────"
    printfn "  ✓ Achieved %.2fx speedup through optimal scheduling" speedup
    
    if speedup > 1.5 then
        printfn "  ✓ Parallel scheduling provides significant time savings (%.1f%% faster)" timeSavedPct
    else
        printfn "  ⚠ Sequential execution would be more cost-effective (fewer dependencies needed)"
    
    printfn ""
    
    // Export Gantt chart
    exportGanttChart schedule "schedule.txt"
    printfn "✓ Gantt chart exported to: schedule.txt"
    printfn ""
    
    printfn "╔══════════════════════════════════════════════════════════════════════════════╗"
    printfn "║                       SCHEDULING SUCCESSFUL                                  ║"
    printfn "╚══════════════════════════════════════════════════════════════════════════════╝"
    printfn ""
    printfn "✨ Note: This example uses the TaskScheduling builder with classical greedy"
    printfn "   scheduling algorithm. For large-scale problems (100+ tasks), the builder"
    printfn "   provides QUBO encoding for quantum backends (QAOA, quantum annealing) via"
    printfn "   IQuantumBackend interface."
    printfn ""

| Error msg ->
    printfn "❌ Scheduling failed: %s" msg
