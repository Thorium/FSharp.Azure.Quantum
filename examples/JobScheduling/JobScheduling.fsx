// ==============================================================================
// Job Scheduling Example - Quantum Solver with Resource Constraints
// ==============================================================================
// Demonstrates resource-constrained task scheduling using quantum optimization
// via the FSharp.Azure.Quantum TaskScheduling builder.
//
// Business Context:
// A manufacturing facility needs to schedule 10 production jobs across 3 machines
// with LIMITED CAPACITY, minimizing total completion time (makespan) while 
// respecting both task dependencies AND resource availability.
//
// Why Quantum?
// Classical greedy scheduling handles dependencies well, but resource capacity
// constraints create a combinatorial optimization problem best solved by:
// - QUBO encoding (Quadratic Unconstrained Binary Optimization)
// - QAOA (Quantum Approximate Optimization Algorithm)
// - Quantum annealing on real quantum hardware
//
// This example shows:
// - Dependency graph modeling (DAG - Directed Acyclic Graph)
// - Resource capacity constraints (limited machines)
// - QUBO encoding for quantum backends
// - Makespan minimization via quantum optimization
// - Machine utilization analysis
// ==============================================================================

//#r "nuget: FSharp.Azure.Quantum"
#r "../../src/FSharp.Azure.Quantum/bin/Debug/net10.0/FSharp.Azure.Quantum.dll"


open System
open FSharp.Azure.Quantum
open FSharp.Azure.Quantum.TaskScheduling  // For types
open FSharp.Azure.Quantum.Backends.LocalBackend

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
    // Initial job (no dependencies)
    { Id = "J1"; Name = "Prep Materials"; DurationHours = 1.0; Priority = 1 }
    
    // Assembly jobs (depend on prep)
    { Id = "J2"; Name = "Base Assembly"; DurationHours = 1.0; Priority = 2 }
    { Id = "J3"; Name = "Component A"; DurationHours = 1.0; Priority = 2 }
]

// ==============================================================================
// PROBLEM SETUP - Using TaskScheduling Builder with Resource Constraints
// ==============================================================================

printfn "╔══════════════════════════════════════════════════════════════════════════════╗"
printfn "║              JOB SCHEDULING WITH QUANTUM OPTIMIZATION                        ║"
printfn "║              Resource-Constrained Scheduling via QUBO/QAOA                   ║"
printfn "╚══════════════════════════════════════════════════════════════════════════════╝"
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
let j1 : ScheduledTask<unit> = scheduledTask {
    taskId "J1"
    duration (hours 1.0)
    requires "Machine1" 1.0  // Needs Machine1
    priority 1.0
}

let j2 : ScheduledTask<unit> = scheduledTask {
    taskId "J2"
    duration (hours 1.0)
    after "J1"
    requires "Machine1" 1.0  // Competes for Machine1
    priority 2.0
}

let j3 : ScheduledTask<unit> = scheduledTask {
    taskId "J3"
    duration (hours 1.0)
    after "J1"
    requires "Machine1" 1.0  // Competes for Machine1
    priority 2.0
}

// Build scheduling problem with resource constraints
let problem : SchedulingProblem<unit, unit> = scheduling {
    tasks [j1; j2; j3]
    resources [machine1]
    objective MinimizeMakespan
    timeHorizon 5.0  // 3 tasks × 5 time slots = 15 qubits (< 16 limit)
}

// ==============================================================================
// SOLVE - Using Quantum Solver (QUBO + QAOA)
// ==============================================================================

printfn "Initializing quantum backend..."
printfn "Note: This uses LocalBackend for demonstration (max 16 qubits)"
printfn "      For large problems (100+ tasks), use Azure Quantum with IonQ/Quantinuum"
printfn ""

// Initialize quantum backend
let backend = LocalBackend() :> FSharp.Azure.Quantum.Core.BackendAbstraction.IQuantumBackend

printfn "Running quantum optimization (QUBO encoding + QAOA)..."
printfn "- Encoding scheduling problem as QUBO matrix"
printfn "- Variables: %d tasks × %d time slots = %d qubits (fits LocalBackend limit)" 
    problem.Tasks.Length 
    (int problem.TimeHorizon) 
    (problem.Tasks.Length * int problem.TimeHorizon)
printfn "- Applying QAOA (Quantum Approximate Optimization Algorithm)"
printfn "- Measuring quantum state and decoding to schedule"
printfn ""

let startTime = DateTime.UtcNow

let result = solveQuantum backend problem |> Async.RunSynchronously

let elapsed = DateTime.UtcNow - startTime
printfn "Quantum optimization completed in %d ms" (int elapsed.TotalMilliseconds)
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
    printfn "╔══════════════════════════════════════════════════════════════════════════════╗"
    printfn "║                    WHY QUANTUM OPTIMIZATION?                                 ║"
    printfn "╚══════════════════════════════════════════════════════════════════════════════╝"
    printfn ""
    printfn "CLASSICAL vs QUANTUM SCHEDULING:"
    printfn "────────────────────────────────────────────────────────────────────────────────"
    printfn ""
    printfn "Classical Greedy Solver (solve):"
    printfn "  ✓ Handles task dependencies optimally"
    printfn "  ✓ Fast for dependency-only problems"
    printfn "  ✗ Ignores resource capacity constraints"
    printfn "  ✗ Cannot optimize resource allocation"
    printfn ""
    printfn "Quantum Solver (solveQuantum):"
    printfn "  ✓ Handles dependencies AND resource constraints"
    printfn "  ✓ Optimizes resource allocation via QUBO encoding"
    printfn "  ✓ Finds near-optimal solutions for NP-hard problems"
    printfn "  ✓ Scales to larger problems on quantum hardware"
    printfn ""
    printfn "HOW IT WORKS:"
    printfn "────────────────────────────────────────────────────────────────────────────────"
    printfn "1. QUBO Encoding: Converts scheduling to binary optimization problem"
    printfn "   - Variables: x_{task,time} ∈ {0,1} for each task and time slot"
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
    printfn "────────────────────────────────────────────────────────────────────────────────"
    printfn "LocalBackend:    16 qubits max (~3 tasks × 5 time slots, demo only)"
    printfn "Azure Quantum:   29-80+ qubits (IonQ, Quantinuum, Rigetti)"
    printfn "                 Scales to realistic production problems (100+ tasks)"
    printfn ""
    printfn "WHEN TO USE QUANTUM:"
    printfn "────────────────────────────────────────────────────────────────────────────────"
    printfn "✓ Resource capacity constraints exist"
    printfn "✓ Multiple resources compete for same tasks"
    printfn "✓ Optimization critical (minimize cost/makespan)"
    printfn "✗ Dependencies only (use classical solver instead)"
    printfn ""

| Error err ->
    printfn "❌ Scheduling failed: %s" err.Message
