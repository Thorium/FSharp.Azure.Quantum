// ==============================================================================
// Job Scheduling Example
// ==============================================================================
// Demonstrates resource allocation and task scheduling with dependencies using
// the FSharp.Azure.Quantum Scheduling builder with quantum-ready architecture.
//
// Business Context:
// A manufacturing facility needs to schedule 10 production jobs across 3 machines,
// minimizing total completion time (makespan) while respecting task dependencies.
// This is a classic constraint satisfaction and optimization problem.
//
// This example shows:
// - Dependency graph modeling (DAG - Directed Acyclic Graph)
// - Resource allocation using Scheduling builder
// - Makespan minimization (total completion time)
// - Machine utilization analysis
// - Quantum-ready QUBO encoding for large-scale problems
// ==============================================================================

#r "../../src/FSharp.Azure.Quantum/bin/Debug/net10.0/FSharp.Azure.Quantum.dll"

open System
open FSharp.Azure.Quantum

// ==============================================================================
// DOMAIN MODEL - Job Scheduling Types
// ==============================================================================

/// Represents a production job for the domain
type ProductionJob = {
    Id: string
    Name: string
    Duration: float  // hours
    Dependencies: string list
    Priority: int
}

// ==============================================================================
// JOB DATA - Manufacturing facility production schedule
// ==============================================================================

let productionJobs = [
    // Initial preparation jobs (no dependencies)
    { Id = "J1"; Name = "Prep Materials"; Duration = 3.0; Dependencies = []; Priority = 1 }
    { Id = "J5"; Name = "Quality Check"; Duration = 2.0; Dependencies = []; Priority = 1 }
    
    // Assembly stage 1 (depends on prep)
    { Id = "J2"; Name = "Base Assembly"; Duration = 4.0; Dependencies = ["J1"]; Priority = 2 }
    { Id = "J3"; Name = "Component A"; Duration = 3.0; Dependencies = ["J1"]; Priority = 2 }
    
    // Assembly stage 2 (depends on stage 1)
    { Id = "J4"; Name = "Integration"; Duration = 5.0; Dependencies = ["J2"; "J3"]; Priority = 3 }
    { Id = "J6"; Name = "Component B"; Duration = 4.0; Dependencies = ["J5"]; Priority = 2 }
    
    // Final assembly (depends on all previous stages)
    { Id = "J7"; Name = "Final Assembly"; Duration = 6.0; Dependencies = ["J4"; "J6"]; Priority = 4 }
    
    // Testing and packaging
    { Id = "J8"; Name = "Testing"; Duration = 3.0; Dependencies = ["J7"]; Priority = 5 }
    { Id = "J9"; Name = "Packaging"; Duration = 2.0; Dependencies = ["J8"]; Priority = 6 }
    { Id = "J10"; Name = "Shipping"; Duration = 2.0; Dependencies = ["J9"]; Priority = 7 }
]

// ==============================================================================
// PROBLEM SETUP - Using Scheduling Builder
// ==============================================================================

printfn "╔══════════════════════════════════════════════════════════════════════════════╗"
printfn "║                   JOB SCHEDULING OPTIMIZATION EXAMPLE                        ║"
printfn "║                   Using Scheduling Builder (Quantum-Ready)                   ║"
printfn "╚══════════════════════════════════════════════════════════════════════════════╝"
printfn ""
printfn "Problem: Schedule %d jobs across 3 machines" productionJobs.Length
printfn "Objective: Minimize makespan (total completion time)"
printfn "Constraints: Respect job dependencies"
printfn ""

// Convert jobs to ScheduledTasks
let tasks =
    productionJobs
    |> List.map (fun job ->
        { 
            Id = job.Id
            Value = job
            Duration = job.Duration
            EarliestStart = None
            Deadline = None
            ResourceRequirements = Map.ofList [("Machine", 1.0)]  // Each job needs 1 machine
            Priority = float job.Priority
            Properties = Map.empty
        })

// Define machine resources
let machines = [
    { Id = "Machine1"; Value = "Machine"; Capacity = 1.0; AvailableWindows = [(0.0, 1000.0)]; CostPerUnit = 500.0; Properties = Map.empty }
    { Id = "Machine2"; Value = "Machine"; Capacity = 1.0; AvailableWindows = [(0.0, 1000.0)]; CostPerUnit = 500.0; Properties = Map.empty }
    { Id = "Machine3"; Value = "Machine"; Capacity = 1.0; AvailableWindows = [(0.0, 1000.0)]; CostPerUnit = 500.0; Properties = Map.empty }
]

// Convert job dependencies to Finish-To-Start dependencies
let dependencies =
    productionJobs
    |> List.collect (fun job ->
        job.Dependencies
        |> List.map (fun depId -> FinishToStart(depId, job.Id, 0.0))
    )

// Build scheduling problem using the builder API
let problem =
    SchedulingBuilder<ProductionJob, string>.Create()
        .Tasks(tasks)
        .Resources(machines)
        .Objective(MinimizeMakespan)
        .TimeHorizon(100.0)  // 100 hour planning horizon
        .Build()

// Add dependencies to problem
let problemWithDeps =
    { problem with Dependencies = dependencies }

// ==============================================================================
// SOLVE - Using classical solver (quantum for large-scale)
// ==============================================================================

printfn "Running scheduling optimization..."
let startTime = DateTime.UtcNow

match solveClassical problemWithDeps with
| Ok schedule ->
    let elapsed = DateTime.UtcNow - startTime
    printfn "Completed in %d ms" (int elapsed.TotalMilliseconds)
    printfn ""
    
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
        schedule.TaskAssignments
        |> Map.toList
        |> List.sortBy (fun (_, assignment) -> assignment.StartTime)
    
    for (taskId, assignment) in sortedAssignments do
        let job = tasks |> List.find (fun t -> t.Id = taskId) |> fun t -> t.Value
        printfn "  %s: hours %.0f-%.0f (duration: %.0fh, priority: %d)" 
            job.Name 
            assignment.StartTime 
            assignment.EndTime 
            (assignment.EndTime - assignment.StartTime)
            job.Priority
    
    printfn ""
    printfn "RESOURCE UTILIZATION:"
    printfn "────────────────────────────────────────────────────────────────────────────────"
    
    for machine in machines do
        match Map.tryFind machine.Id schedule.ResourceAllocations with
        | Some allocations ->
            let totalTime = allocations |> List.sumBy (fun a -> a.EndTime - a.StartTime)
            let utilization = (totalTime / schedule.Makespan) * 100.0
            printfn "  %s: %.1f%% utilization (%.0f hours used)" machine.Id utilization totalTime
        | None ->
            printfn "  %s: 0.0%% utilization (0 hours used)" machine.Id
    
    printfn ""
    printfn "PERFORMANCE SUMMARY:"
    printfn "────────────────────────────────────────────────────────────────────────────────"
    printfn "  Total Jobs:            %d" productionJobs.Length
    printfn "  Makespan:              %.0f hours" schedule.Makespan
    printfn "  Total Cost:            $%.2f" schedule.TotalCost
    
    // Calculate utilization
    let totalWorkTime = productionJobs |> List.sumBy (fun j -> j.Duration)
    let totalAvailableTime = schedule.Makespan * 3.0  // 3 machines
    let avgUtilization = (totalWorkTime / totalAvailableTime) * 100.0
    
    printfn "  Average Utilization:   %.1f%%" avgUtilization
    printfn "  Total Idle Time:       %.0f machine-hours" (totalAvailableTime - totalWorkTime)
    printfn ""
    
    // ==============================================================================
    // BUSINESS IMPACT ANALYSIS
    // ==============================================================================
    
    printfn "╔══════════════════════════════════════════════════════════════════════════════╗"
    printfn "║                       BUSINESS IMPACT ANALYSIS                               ║"
    printfn "╚══════════════════════════════════════════════════════════════════════════════╝"
    printfn ""
    
    // Calculate sequential time (all jobs on one machine)
    let sequentialTime = productionJobs |> List.sumBy (fun j -> j.Duration)
    let speedup = sequentialTime / schedule.Makespan
    let timeSaved = sequentialTime - schedule.Makespan
    let timeSavedPct = (timeSaved / sequentialTime) * 100.0
    
    printfn "TIME ANALYSIS:"
    printfn "────────────────────────────────────────────────────────────────────────────────"
    printfn "  Sequential Time (1 machine):   %.0f hours" sequentialTime
    printfn "  Parallel Time (3 machines):    %.0f hours" schedule.Makespan
    printfn "  Speedup Factor:                %.1fx faster" speedup
    printfn "  Time Saved:                    %.0f hours (%.1f%%)" timeSaved timeSavedPct
    printfn ""
    
    let costPerMachineHour = 500.0
    let sequentialCost = sequentialTime * costPerMachineHour
    let parallelCost = schedule.TotalCost
    let additionalCost = parallelCost - sequentialCost
    
    printfn "COST ANALYSIS (@ $%.0f/machine-hour):" costPerMachineHour
    printfn "────────────────────────────────────────────────────────────────────────────────"
    printfn "  Sequential Cost:               $%.2f" sequentialCost
    printfn "  Parallel Cost:                 $%.2f" parallelCost
    printfn "  Additional Cost:               $%.2f" additionalCost
    printfn ""
    
    printfn "KEY INSIGHTS:"
    printfn "────────────────────────────────────────────────────────────────────────────────"
    printfn "  ✓ Achieved %.1fx speedup with 3 machines" speedup
    printfn "  ✓ Average machine utilization: %.1f%%" avgUtilization
    
    if speedup > 1.5 then
        printfn "  ✓ Parallel scheduling provides significant time savings (%.1f%% faster)" timeSavedPct
    else
        printfn "  ⚠ Sequential execution would be more cost-effective (fewer dependencies needed)"
    
    printfn ""
    
    printfn "╔══════════════════════════════════════════════════════════════════════════════╗"
    printfn "║                       SCHEDULING SUCCESSFUL                                  ║"
    printfn "╚══════════════════════════════════════════════════════════════════════════════╝"
    printfn ""
    printfn "✨ Note: This example uses the Scheduling builder with classical List Scheduling."
    printfn "   For large-scale problems (100+ tasks), the builder provides QUBO encoding"
    printfn "   for quantum backends (QAOA, quantum annealing) via IQuantumBackend interface."
    printfn ""

| Error msg ->
    printfn "❌ Scheduling failed: %s" msg
