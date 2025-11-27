// ==============================================================================
// Job Scheduling Example
// ==============================================================================
// Demonstrates resource allocation and task scheduling with dependencies using
// a greedy scheduling algorithm (classical approach).
//
// Business Context:
// A manufacturing facility needs to schedule 10 production jobs across 3 machines,
// minimizing total completion time (makespan) while respecting task dependencies.
// This is a classic constraint satisfaction and optimization problem.
//
// This example shows:
// - Dependency graph modeling (DAG - Directed Acyclic Graph)
// - Resource allocation across parallel machines
// - Makespan minimization (total completion time)
// - Machine utilization analysis
// ==============================================================================

open System
open System.Collections.Generic

// ==============================================================================
// DOMAIN MODEL - Job Scheduling Types
// ==============================================================================

/// Represents a production job with dependencies and duration
type Job = {
    Id: string
    Duration: int  // hours
    Dependencies: string list  // Jobs that must complete before this job
    Priority: int  // Higher priority = scheduled earlier (tie-breaker)
}

/// Machine assignment with start/end times
type Assignment = {
    JobId: string
    Machine: int
    StartTime: int
    EndTime: int
    Duration: int
}

/// Complete schedule solution
type Schedule = {
    Assignments: Assignment list
    Makespan: int  // Total time to complete all jobs
    MachineCount: int
    JobCount: int
}

/// Schedule analysis with performance metrics
type ScheduleAnalysis = {
    Schedule: Schedule
    AverageUtilization: float  // Average machine utilization %
    MachineUtilizations: (int * float) list  // Per-machine utilization
    CriticalPath: string list  // Jobs on critical path (bottleneck)
    IdleTime: int  // Total idle time across all machines
}

// ==============================================================================
// JOB DATA - Manufacturing facility production schedule
// ==============================================================================
// Realistic manufacturing scenario: 10 production jobs with dependencies
// representing a product assembly line workflow

let jobs = [
    // Initial preparation jobs (no dependencies)
    { Id = "J1_PrepMaterials"; Duration = 3; Dependencies = []; Priority = 1 }
    { Id = "J5_QualityCheck"; Duration = 2; Dependencies = []; Priority = 1 }
    
    // Assembly stage 1 (depends on prep)
    { Id = "J2_BaseAssembly"; Duration = 4; Dependencies = ["J1_PrepMaterials"]; Priority = 2 }
    { Id = "J3_ComponentA"; Duration = 3; Dependencies = ["J1_PrepMaterials"]; Priority = 2 }
    
    // Assembly stage 2 (depends on stage 1)
    { Id = "J4_Integration"; Duration = 5; Dependencies = ["J2_BaseAssembly"; "J3_ComponentA"]; Priority = 3 }
    { Id = "J6_ComponentB"; Duration = 4; Dependencies = ["J5_QualityCheck"]; Priority = 2 }
    
    // Final assembly (depends on all previous stages)
    { Id = "J7_FinalAssembly"; Duration = 6; Dependencies = ["J4_Integration"; "J6_ComponentB"]; Priority = 4 }
    
    // Testing and packaging
    { Id = "J8_Testing"; Duration = 3; Dependencies = ["J7_FinalAssembly"]; Priority = 5 }
    { Id = "J9_Packaging"; Duration = 2; Dependencies = ["J8_Testing"]; Priority = 6 }
    { Id = "J10_Shipping"; Duration = 2; Dependencies = ["J9_Packaging"]; Priority = 7 }
]

let machineCount = 3

// ==============================================================================
// PURE FUNCTIONS - Scheduling algorithm and analysis
// ==============================================================================

/// Check if all dependencies are satisfied at given time
let areDependenciesSatisfied (job: Job) (assignments: Assignment list) : bool =
    job.Dependencies
    |> List.forall (fun depId ->
        assignments
        |> List.exists (fun a -> a.JobId = depId))

/// Get the earliest time a job can start (after all dependencies complete)
let getEarliestStartTime (job: Job) (assignments: Assignment list) : int =
    if List.isEmpty job.Dependencies then
        0
    else
        job.Dependencies
        |> List.choose (fun depId ->
            assignments
            |> List.tryFind (fun a -> a.JobId = depId)
            |> Option.map (fun a -> a.EndTime))
        |> function
            | [] -> 0
            | times -> List.max times

/// Find the earliest available time slot on a machine
let findEarliestSlot (machine: int) (startTime: int) (duration: int) (assignments: Assignment list) : int =
    let machineAssignments =
        assignments
        |> List.filter (fun a -> a.Machine = machine)
        |> List.sortBy (fun a -> a.StartTime)
    
    // Check if we can start at the desired start time
    let rec findSlot currentTime remainingSlots =
        match remainingSlots with
        | [] ->
            // No more slots, can start at currentTime
            max currentTime startTime
        
        | next :: rest ->
            let proposedEnd = max currentTime startTime + duration
            if proposedEnd <= next.StartTime then
                // Fits before next assignment
                max currentTime startTime
            else
                // Try after this assignment
                findSlot next.EndTime rest
    
    findSlot 0 machineAssignments

/// Find the machine with earliest available slot for the job
let findBestMachine (machines: int list) (job: Job) (earliestStart: int) (assignments: Assignment list) : int * int =
    machines
    |> List.map (fun m ->
        let slotTime = findEarliestSlot m earliestStart job.Duration assignments
        (m, slotTime))
    |> List.minBy snd

/// Greedy scheduling algorithm with dependency constraints
let scheduleJobs (jobs: Job list) (machineCount: int) : Schedule =
    let machines = [0 .. machineCount - 1]
    
    // Build dependency map for topological ordering
    let dependencyMap =
        jobs
        |> List.map (fun j -> (j.Id, j.Dependencies))
        |> Map.ofList
    
    // Topological sort with priority
    let rec topologicalSort (remaining: Job list) (scheduled: Job list) (assignments: Assignment list) =
        if List.isEmpty remaining then
            List.rev scheduled
        else
            // Find jobs with all dependencies satisfied
            let ready =
                remaining
                |> List.filter (fun job -> areDependenciesSatisfied job assignments)
                |> List.sortByDescending (fun j -> j.Priority)  // Higher priority first
            
            match ready with
            | [] ->
                // This shouldn't happen with valid DAG
                failwith "Circular dependency detected or invalid job graph"
            
            | job :: _ ->
                // Schedule this job
                let earliestStart = getEarliestStartTime job assignments
                let (bestMachine, startTime) = findBestMachine machines job earliestStart assignments
                
                let assignment = {
                    JobId = job.Id
                    Machine = bestMachine
                    StartTime = startTime
                    EndTime = startTime + job.Duration
                    Duration = job.Duration
                }
                
                let newRemaining = remaining |> List.filter (fun j -> j.Id <> job.Id)
                topologicalSort newRemaining (job :: scheduled) (assignment :: assignments)
    
    let sortedJobs = topologicalSort jobs [] []
    
    // Re-run scheduling with sorted order to get assignments
    let assignments =
        sortedJobs
        |> List.fold (fun (assigns: Assignment list) job ->
            let earliestStart = getEarliestStartTime job assigns
            let (bestMachine, startTime) = findBestMachine machines job earliestStart assigns
            
            let assignment = {
                JobId = job.Id
                Machine = bestMachine
                StartTime = startTime
                EndTime = startTime + job.Duration
                Duration = job.Duration
            }
            
            assignment :: assigns
        ) []
        |> List.rev
    
    let makespan =
        if List.isEmpty assignments then 0
        else assignments |> List.map (fun a -> a.EndTime) |> List.max
    
    {
        Assignments = assignments
        Makespan = makespan
        MachineCount = machineCount
        JobCount = List.length jobs
    }

/// Calculate machine utilization
let calculateUtilization (schedule: Schedule) : float list =
    [0 .. schedule.MachineCount - 1]
    |> List.map (fun machine ->
        let busyTime =
            schedule.Assignments
            |> List.filter (fun a -> a.Machine = machine)
            |> List.sumBy (fun a -> a.Duration)
        
        if schedule.Makespan = 0 then 0.0
        else (float busyTime) / (float schedule.Makespan) * 100.0)

/// Find critical path (longest dependency chain)
let findCriticalPath (jobs: Job list) (schedule: Schedule) : string list =
    // Build reverse dependency graph
    let reverseDeps =
        jobs
        |> List.collect (fun job ->
            job.Dependencies
            |> List.map (fun dep -> (dep, job.Id)))
        |> List.groupBy fst
        |> Map.ofList
        |> Map.map (fun _ deps -> deps |> List.map snd)
    
    // Find path with maximum total duration
    let rec findLongestPath (jobId: string) (visited: Set<string>) : string list =
        if Set.contains jobId visited then
            []
        else
            let newVisited = Set.add jobId visited
            match Map.tryFind jobId reverseDeps with
            | None ->
                [jobId]
            | Some children ->
                children
                |> List.map (fun child -> findLongestPath child newVisited)
                |> List.maxBy (fun path ->
                    path
                    |> List.sumBy (fun jId ->
                        jobs |> List.find (fun j -> j.Id = jId) |> fun j -> j.Duration))
                |> fun longestChild -> jobId :: longestChild
    
    // Find all leaf jobs (no dependencies on them)
    let leafJobs =
        jobs
        |> List.filter (fun job ->
            not (Map.containsKey job.Id reverseDeps))
        |> List.map (fun j -> j.Id)
    
    leafJobs
    |> List.map (fun leaf -> findLongestPath leaf Set.empty)
    |> List.maxBy (fun path ->
        path
        |> List.sumBy (fun jId ->
            jobs |> List.find (fun j -> j.Id = jId) |> fun j -> j.Duration))
    |> List.rev

/// Analyze schedule performance
let analyzeSchedule (jobs: Job list) (schedule: Schedule) : ScheduleAnalysis =
    let utilizations = calculateUtilization schedule
    let avgUtilization = utilizations |> List.average
    
    let machineUtils =
        utilizations
        |> List.indexed
        |> List.map (fun (i, util) -> (i, util))
    
    let criticalPath = findCriticalPath jobs schedule
    
    let totalWorkTime =
        schedule.Assignments
        |> List.sumBy (fun a -> a.Duration)
    
    let totalAvailableTime = schedule.Makespan * schedule.MachineCount
    let idleTime = totalAvailableTime - totalWorkTime
    
    {
        Schedule = schedule
        AverageUtilization = avgUtilization
        MachineUtilizations = machineUtils
        CriticalPath = criticalPath
        IdleTime = idleTime
    }

// ==============================================================================
// REPORTING - Pure functions for output
// ==============================================================================

/// Generate schedule visualization
let generateScheduleReport (analysis: ScheduleAnalysis) (jobs: Job list) : string list =
    [
        "╔══════════════════════════════════════════════════════════════════════════════╗"
        "║                       JOB SCHEDULE REPORT                                    ║"
        "╚══════════════════════════════════════════════════════════════════════════════╝"
        ""
        "SCHEDULE BY MACHINE:"
        "────────────────────────────────────────────────────────────────────────────────"
        
        yield!
            [0 .. analysis.Schedule.MachineCount - 1]
            |> List.collect (fun machine ->
                let assignments =
                    analysis.Schedule.Assignments
                    |> List.filter (fun a -> a.Machine = machine)
                    |> List.sortBy (fun a -> a.StartTime)
                
                let (_, util) = analysis.MachineUtilizations |> List.find (fun (m, _) -> m = machine)
                
                [
                    sprintf "  Machine %d (Utilization: %.1f%%):" (machine + 1) util
                    yield!
                        assignments
                        |> List.map (fun a ->
                            sprintf "    • %s: hours %d-%d (duration: %dh)"
                                a.JobId a.StartTime a.EndTime a.Duration)
                    ""
                ])
        
        "PERFORMANCE SUMMARY:"
        "────────────────────────────────────────────────────────────────────────────────"
        sprintf "  Total Jobs:            %d" analysis.Schedule.JobCount
        sprintf "  Makespan:              %d hours" analysis.Schedule.Makespan
        sprintf "  Average Utilization:   %.1f%%" analysis.AverageUtilization
        sprintf "  Total Idle Time:       %d machine-hours" analysis.IdleTime
        ""
    ]

/// Generate critical path analysis
let generateCriticalPathReport (analysis: ScheduleAnalysis) (jobs: Job list) : string list =
    let criticalDuration =
        analysis.CriticalPath
        |> List.sumBy (fun jId ->
            jobs |> List.find (fun j -> j.Id = jId) |> fun j -> j.Duration)
    
    [
        "╔══════════════════════════════════════════════════════════════════════════════╗"
        "║                       CRITICAL PATH ANALYSIS                                 ║"
        "╚══════════════════════════════════════════════════════════════════════════════╝"
        ""
        "CRITICAL PATH (Bottleneck):"
        "────────────────────────────────────────────────────────────────────────────────"
        
        yield!
            analysis.CriticalPath
            |> List.mapi (fun i jobId ->
                let job = jobs |> List.find (fun j -> j.Id = jobId)
                sprintf "  %d. %-20s (duration: %d hours)" (i + 1) job.Id job.Duration)
        
        ""
        sprintf "  Total Critical Path Duration: %d hours" criticalDuration
        sprintf "  Makespan:                     %d hours" analysis.Schedule.Makespan
        ""
        "INSIGHTS:"
        "────────────────────────────────────────────────────────────────────────────────"
        
        if criticalDuration = analysis.Schedule.Makespan then
            "  ✓ Critical path equals makespan - optimal sequential constraint satisfaction"
        else
            "  ✓ Parallelism achieved - jobs executed concurrently where possible"
        
        sprintf "  ✓ Average machine utilization: %.1f%%" analysis.AverageUtilization
        
        if analysis.AverageUtilization > 75.0 then
            "  ✓ High utilization - machines are well-utilized"
        elif analysis.AverageUtilization > 50.0 then
            "  ⚠ Moderate utilization - some idle time due to dependencies"
        else
            "  ⚠ Low utilization - consider reducing machines or adding jobs"
        ""
    ]

/// Generate business impact analysis
let generateBusinessImpact (analysis: ScheduleAnalysis) (jobs: Job list) : string list =
    let totalWorkHours = jobs |> List.sumBy (fun j -> j.Duration)
    let sequentialTime = totalWorkHours  // If done on 1 machine sequentially
    let parallelSpeedup = (float sequentialTime) / (float analysis.Schedule.Makespan)
    
    // Assume $500/hour cost per machine
    let hourlyMachineRate = 500.0
    let sequentialCost = (float sequentialTime) * hourlyMachineRate
    let parallelCost = (float analysis.Schedule.Makespan) * hourlyMachineRate * (float analysis.Schedule.MachineCount)
    let savingsPercent = ((sequentialCost - parallelCost) / sequentialCost) * 100.0
    
    [
        "╔══════════════════════════════════════════════════════════════════════════════╗"
        "║                       BUSINESS IMPACT ANALYSIS                               ║"
        "╚══════════════════════════════════════════════════════════════════════════════╝"
        ""
        "TIME ANALYSIS:"
        "────────────────────────────────────────────────────────────────────────────────"
        sprintf "  Sequential Time (1 machine):   %d hours" sequentialTime
        sprintf "  Parallel Time (%d machines):    %d hours" analysis.Schedule.MachineCount analysis.Schedule.Makespan
        sprintf "  Speedup Factor:                %.1fx faster" parallelSpeedup
        sprintf "  Time Saved:                    %d hours (%.1f%%)" 
            (sequentialTime - analysis.Schedule.Makespan)
            ((float (sequentialTime - analysis.Schedule.Makespan) / float sequentialTime) * 100.0)
        ""
        "COST ANALYSIS (@ $500/machine-hour):"
        "────────────────────────────────────────────────────────────────────────────────"
        sprintf "  Sequential Cost:               $%.2f" sequentialCost
        sprintf "  Parallel Cost:                 $%.2f" parallelCost
        
        if savingsPercent > 0.0 then
            sprintf "  Cost Savings:                  $%.2f (%.1f%% reduction)" (sequentialCost - parallelCost) savingsPercent
        else
            sprintf "  Additional Cost:               $%.2f" (parallelCost - sequentialCost)
        
        ""
        "KEY INSIGHTS:"
        "────────────────────────────────────────────────────────────────────────────────"
        sprintf "  ✓ Achieved %.1fx speedup with %d machines" parallelSpeedup analysis.Schedule.MachineCount
        sprintf "  ✓ Average machine utilization: %.1f%%" analysis.AverageUtilization
        sprintf "  ✓ Critical path: %d steps (%d hours)" 
            (List.length analysis.CriticalPath)
            (analysis.CriticalPath |> List.sumBy (fun jId -> (jobs |> List.find (fun j -> j.Id = jId)).Duration))
        
        if savingsPercent > 0.0 then
            "  ✓ Parallel execution is cost-effective for this workload"
        else
            "  ⚠ Sequential execution would be more cost-effective (fewer dependencies needed)"
        ""
    ]

// ==============================================================================
// MAIN EXECUTION
// ==============================================================================

printfn "╔══════════════════════════════════════════════════════════════════════════════╗"
printfn "║                   JOB SCHEDULING OPTIMIZATION EXAMPLE                        ║"
printfn "║                   Using Greedy Scheduling Algorithm                          ║"
printfn "╚══════════════════════════════════════════════════════════════════════════════╝"
printfn ""
printfn "Problem: Schedule %d jobs across %d machines" (List.length jobs) machineCount
printfn "Objective: Minimize makespan (total completion time)"
printfn "Constraints: Respect job dependencies"
printfn ""
printfn "Running scheduling optimization..."

// Time the optimization
let startTime = DateTime.UtcNow

// Solve the scheduling problem
let schedule = scheduleJobs jobs machineCount
let analysis = analyzeSchedule jobs schedule

let endTime = DateTime.UtcNow
let elapsed = (endTime - startTime).TotalMilliseconds

printfn "Completed in %.0f ms" elapsed
printfn ""

// Generate reports
let scheduleReport = generateScheduleReport analysis jobs
let criticalPathReport = generateCriticalPathReport analysis jobs
let businessImpact = generateBusinessImpact analysis jobs

// Print reports
scheduleReport |> List.iter (printfn "%s")
criticalPathReport |> List.iter (printfn "%s")
businessImpact |> List.iter (printfn "%s")

printfn "╔══════════════════════════════════════════════════════════════════════════════╗"
printfn "║                       SCHEDULING SUCCESSFUL                                  ║"
printfn "╚══════════════════════════════════════════════════════════════════════════════╝"
