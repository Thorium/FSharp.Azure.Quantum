namespace FSharp.Azure.Quantum.TaskScheduling
open FSharp.Azure.Quantum.Core

open Types

/// Classical greedy scheduling algorithm (dependency-only, no resource constraints)
module ClassicalSolver =

    // ============================================================================
    // HELPER FUNCTIONS - Functional pipeline for scheduling
    // ============================================================================

    /// Topological sort for dependency ordering
    let private topologicalSort (tasks: ScheduledTask<'T> list) (dependencies: Dependency list) : ScheduledTask<'T> list =
        let taskMap = tasks |> List.map (fun t -> t.Id, t) |> Map.ofList
        
        // Build adjacency list (task -> dependencies)
        let depMap =
            dependencies
            |> List.groupBy (fun dep ->
                match dep with
                | FinishToStart(_, succId, _) -> succId
            )
            |> List.map (fun (succId, deps) ->
                let predIds =
                    deps
                    |> List.map (fun dep ->
                        match dep with
                        | FinishToStart(predId, _, _) -> predId
                    )
                succId, Set.ofList predIds
            )
            |> Map.ofList

        // Kahn's algorithm for topological sort
        let rec sort (ready: string list) (remaining: Map<string, Set<string>>) (result: ScheduledTask<'T> list) =
            match ready with
            | [] ->
                if Map.isEmpty remaining then
                    List.rev result
                else
                    // If there are remaining tasks with dependencies, it's a cycle
                    // For now, just append them (this shouldn't happen with valid DAG)
                    List.rev result @ (remaining |> Map.toList |> List.map (fun (id, _) -> Map.find id taskMap))
            | taskId :: rest ->
                let task = Map.find taskId taskMap
                
                // Remove this task from all dependency sets
                let newRemaining =
                    remaining
                    |> Map.map (fun _ deps -> Set.remove taskId deps)
                
                // Find tasks that are now ready (no dependencies left)
                let newReady =
                    newRemaining
                    |> Map.filter (fun _ deps -> Set.isEmpty deps)
                    |> Map.toList
                    |> List.map fst
                
                // Remove newly ready tasks from remaining
                let newRemaining2 =
                    newRemaining
                    |> Map.filter (fun id _ -> not (List.contains id newReady))
                
                sort (rest @ newReady) newRemaining2 (task :: result)

        // Find tasks with no dependencies (ready to start)
        let initialReady =
            tasks
            |> List.filter (fun t -> not (Map.containsKey t.Id depMap))
            |> List.map (fun t -> t.Id)
            |> List.sortByDescending (fun id -> (Map.find id taskMap).Priority)

        sort initialReady depMap []

    /// Compute earliest start time for a task based on dependencies
    let private computeStartTime
        (task: ScheduledTask<'T>)
        (completionTimes: Map<string, float>)
        (dependencies: Dependency list)
        : float =
        
        // Find earliest start time based on dependencies
        let depEndTime =
            dependencies
            |> List.choose (function
                | FinishToStart(predId, succId, lag) when succId = task.Id ->
                    Map.tryFind predId completionTimes
                    |> Option.map (fun endTime -> endTime + lag)
                | _ -> None)
            |> function
                | [] -> 0.0
                | times -> List.max times
        
        // Consider earliest start constraint
        match task.EarliestStart with
        | Some earliest -> max earliest depEndTime
        | None -> depEndTime
    
    /// Create assignment from task and start time
    let private createAssignment
        (task: ScheduledTask<'T>)
        (startTime: float)
        : TaskAssignment =
        
        {
            TaskId = task.Id
            StartTime = startTime
            EndTime = startTime + task.Duration
            AssignedResources = task.ResourceRequirements
        }
    
    /// Calculate makespan from assignments
    let calculateMakespan (assignments: TaskAssignment list) : float =
        if List.isEmpty assignments then 0.0
        else assignments |> List.map (fun a -> a.EndTime) |> List.max
    
    /// Calculate total cost from assignments and resources
    let calculateTotalCost
        (assignments: TaskAssignment list)
        (resources: Resource<'R> list)
        : float =
        
        assignments
        |> List.sumBy (fun a ->
            let duration = a.EndTime - a.StartTime
            a.AssignedResources
            |> Map.toList
            |> List.sumBy (fun (resourceId, quantity) ->
                match resources |> List.tryFind (fun r -> r.Id = resourceId) with
                | Some resource -> resource.CostPerUnit * quantity * duration
                | None -> 0.0
            )
        )
    
    /// Find tasks that violate their deadlines
    let findDeadlineViolations
        (tasks: ScheduledTask<'T> list)
        (completionTimes: Map<string, float>)
        : string list =
        
        tasks
        |> List.choose (fun task ->
            match task.Deadline with
            | Some deadline ->
                let completion = Map.find task.Id completionTimes
                if completion > deadline then Some task.Id else None
            | None -> None
        )
    
    /// Calculate resource utilization across all resources
    let calculateResourceUtilization
        (assignments: TaskAssignment list)
        (resources: Resource<'R> list)
        (makespan: float)
        : Map<string, float> =
        
        resources
        |> List.map (fun r ->
            let totalUsage =
                assignments
                |> List.sumBy (fun a ->
                    let duration = a.EndTime - a.StartTime
                    match Map.tryFind r.Id a.AssignedResources with
                    | Some quantity -> quantity * duration
                    | None -> 0.0
                )
            let maxPossible = r.Capacity * makespan
            let utilization = if maxPossible > 0.0 then totalUsage / maxPossible else 0.0
            r.Id, utilization
        )
        |> Map.ofList
    
    // ============================================================================
    // PUBLIC API - Classical Greedy Solver
    // ============================================================================

    /// Solve scheduling problem using classical greedy algorithm
    /// 
    /// Note: This solver handles dependencies but ignores resource capacity constraints.
    /// For resource-constrained scheduling, use QuantumSolver.solveQuantum with IQuantumBackend.
    let solve (problem: SchedulingProblem<'TTask, 'TResource>) : QuantumResult<Solution> =
        // Validate problem first
        match Validation.validateProblem problem with
        | Error err -> Error err
        | Ok () ->

        // Topological sort tasks by dependencies
        let sortedTasks = topologicalSort problem.Tasks problem.Dependencies

        // Schedule each task using functional fold
        let (assignments, completionTimes) =
            sortedTasks
            |> List.fold (fun (assigns, compTimes) task ->
                let startTime = computeStartTime task compTimes problem.Dependencies
                let assignment = createAssignment task startTime
                let newCompTimes = Map.add task.Id assignment.EndTime compTimes
                (assignment :: assigns, newCompTimes)
            ) ([], Map.empty)
        
        let assignments = List.rev assignments  // Reverse to maintain original order

        // Calculate metrics using helper functions
        let makespan = calculateMakespan assignments
        let totalCost = calculateTotalCost assignments problem.Resources
        let violations = findDeadlineViolations problem.Tasks completionTimes
        let resourceUtil = calculateResourceUtilization assignments problem.Resources makespan

        let solution = {
            Assignments = assignments
            Makespan = makespan
            TotalCost = totalCost
            ResourceUtilization = resourceUtil
            DeadlineViolations = violations
            IsValid = List.isEmpty violations
        }

        Ok solution
