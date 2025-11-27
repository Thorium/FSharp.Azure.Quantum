namespace FSharp.Azure.Quantum

/// Task Scheduling Domain Builder - F# Computation Expression API
/// 
/// Provides idiomatic F# builders for task scheduling optimization with:
/// - scheduledTask { ... } - Define tasks with dependencies
/// - resource { ... } - Define resource constraints
/// - scheduling { ... } - Compose complete scheduling problems
/// 
/// Uses underlying Generic Scheduling Framework (TKT-91) for solving.
module TaskScheduling =
    
    // ============================================================================
    // CORE TYPES - Task Scheduling Domain Model
    // ============================================================================
    
    /// Time unit helpers for readable duration specifications
    type Duration = float
    
    /// Task with scheduling metadata
    type Task = {
        Id: string
        Duration: Duration
        Dependencies: string list
        ResourceRequirements: Map<string, float>
        Priority: float
        Deadline: float option
        EarliestStart: float option
    }
    
    /// Resource with capacity and cost
    type Resource = {
        Id: string
        Capacity: float
        CostPerUnit: float
        AvailableFrom: float
        AvailableTo: float
    }
    
    /// Scheduling objective
    type Objective =
        | MinimizeMakespan
        | MinimizeCost
        | MaximizeResourceUtilization
        | MinimizeLateness
    
    /// Complete scheduling problem
    type SchedulingProblem = {
        Tasks: Task list
        Resources: Resource list
        Objective: Objective
        TimeHorizon: float
    }
    
    /// Task assignment in schedule
    type TaskAssignment = {
        TaskId: string
        StartTime: float
        EndTime: float
        AssignedResources: Map<string, float>
    }
    
    /// Scheduling solution
    type Solution = {
        Assignments: TaskAssignment list
        Makespan: float
        TotalCost: float
        ResourceUtilization: Map<string, float>
        DeadlineViolations: string list
        IsValid: bool
    }
    
    // ============================================================================
    // TIME UNIT HELPERS
    // ============================================================================
    
    /// Convert minutes to time units (float)
    let minutes (value: float) : Duration = value
    
    /// Convert hours to time units (60 minutes)
    let hours (value: float) : Duration = value * 60.0
    
    /// Convert days to time units (24 hours)
    let days (value: float) : Duration = value * 24.0 * 60.0
    
    // ============================================================================
    // COMPUTATION EXPRESSION BUILDERS - Task Builder
    // ============================================================================
    
    /// Computation expression builder for scheduled tasks
    type ScheduledTaskBuilder() =
        
        member _.Yield(_) : Task =
            {
                Id = ""
                Duration = 0.0
                Dependencies = []
                ResourceRequirements = Map.empty
                Priority = 0.0
                Deadline = None
                EarliestStart = None
            }
        
        /// Set task ID
        [<CustomOperation("id")>]
        member _.Id(task: Task, id: string) : Task =
            { task with Id = id }
        
        /// Set task duration
        [<CustomOperation("duration")>]
        member _.Duration(task: Task, duration: Duration) : Task =
            { task with Duration = duration }
        
        /// Add single dependency (task must complete after specified task)
        [<CustomOperation("after")>]
        member _.After(task: Task, dependsOn: string) : Task =
            { task with Dependencies = dependsOn :: task.Dependencies }
        
        /// Add multiple dependencies
        [<CustomOperation("afterMultiple")>]
        member _.AfterMultiple(task: Task, dependsOn: string list) : Task =
            { task with Dependencies = dependsOn @ task.Dependencies }
        
        /// Add resource requirement
        [<CustomOperation("requires")>]
        member _.Requires(task: Task, resourceId: string, amount: float) : Task =
            { task with ResourceRequirements = task.ResourceRequirements |> Map.add resourceId amount }
        
        /// Set task priority
        [<CustomOperation("priority")>]
        member _.Priority(task: Task, priority: float) : Task =
            { task with Priority = priority }
        
        /// Set deadline (latest completion time)
        [<CustomOperation("deadline")>]
        member _.Deadline(task: Task, deadline: float) : Task =
            { task with Deadline = Some deadline }
        
        /// Set earliest start time
        [<CustomOperation("earliestStart")>]
        member _.EarliestStart(task: Task, earliest: float) : Task =
            { task with EarliestStart = Some earliest }
    
    /// Global instance of scheduledTask builder
    let scheduledTask = ScheduledTaskBuilder()
    
    // ============================================================================
    // COMPUTATION EXPRESSION BUILDERS - Resource Builder
    // ============================================================================
    
    /// Computation expression builder for resources
    type ResourceBuilder() =
        
        member _.Yield(_) : Resource =
            {
                Id = ""
                Capacity = 0.0
                CostPerUnit = 0.0
                AvailableFrom = 0.0
                AvailableTo = System.Double.MaxValue
            }
        
        /// Set resource ID
        [<CustomOperation("id")>]
        member _.Id(resource: Resource, id: string) : Resource =
            { resource with Id = id }
        
        /// Set resource capacity
        [<CustomOperation("capacity")>]
        member _.Capacity(resource: Resource, capacity: float) : Resource =
            { resource with Capacity = capacity }
        
        /// Set cost per unit
        [<CustomOperation("costPerUnit")>]
        member _.CostPerUnit(resource: Resource, cost: float) : Resource =
            { resource with CostPerUnit = cost }
        
        /// Set availability window
        [<CustomOperation("availableWindow")>]
        member _.AvailableWindow(resource: Resource, fromTime: float, toTime: float) : Resource =
            { resource with AvailableFrom = fromTime; AvailableTo = toTime }
    
    /// Global instance of resource builder
    let resource = ResourceBuilder()
    
    // ============================================================================
    // HELPER FUNCTIONS - Simple Resource Creation
    // ============================================================================
    
    /// Quick helper: Create a crew resource (common case)
    /// Example: crew "SafetyCrew" 1 100.0
    let crew (name: string) (capacity: float) (cost: float) : Resource =
        {
            Id = name
            Capacity = capacity
            CostPerUnit = cost
            AvailableFrom = 0.0
            AvailableTo = System.Double.MaxValue
        }
    
    // ============================================================================
    // COMPUTATION EXPRESSION BUILDERS - Scheduling Problem Builder
    // ============================================================================
    
    /// Computation expression builder for complete scheduling problems
    type SchedulingProblemBuilder() =
        
        member _.Yield(_) : SchedulingProblem =
            {
                Tasks = []
                Resources = []
                Objective = MinimizeMakespan
                TimeHorizon = 1000.0  // Default: 1000 time units
            }
        
        /// Add tasks to the problem
        [<CustomOperation("tasks")>]
        member _.Tasks(problem: SchedulingProblem, tasks: Task list) : SchedulingProblem =
            { problem with Tasks = tasks }
        
        /// Add resources to the problem
        [<CustomOperation("resources")>]
        member _.Resources(problem: SchedulingProblem, resources: Resource list) : SchedulingProblem =
            { problem with Resources = resources }
        
        /// Set optimization objective
        [<CustomOperation("objective")>]
        member _.Objective(problem: SchedulingProblem, objective: Objective) : SchedulingProblem =
            { problem with Objective = objective }
        
        /// Set time horizon
        [<CustomOperation("timeHorizon")>]
        member _.TimeHorizon(problem: SchedulingProblem, horizon: float) : SchedulingProblem =
            { problem with TimeHorizon = horizon }
    
    /// Global instance of scheduling builder
    let scheduling = SchedulingProblemBuilder()
    
    // ============================================================================
    // VALIDATION
    // ============================================================================
    
    /// Validate that a scheduling problem is well-formed
    let validate (problem: SchedulingProblem) : Result<unit, string> =
        // Check all tasks have IDs
        let missingIds = problem.Tasks |> List.filter (fun t -> System.String.IsNullOrWhiteSpace(t.Id))
        if not (List.isEmpty missingIds) then
            Error "All tasks must have non-empty IDs"
        
        // Check all task IDs are unique
        else
            let duplicateIds = 
                problem.Tasks 
                |> List.groupBy (fun t -> t.Id)
                |> List.filter (fun (_, group) -> List.length group > 1)
                |> List.map fst
            
            if not (List.isEmpty duplicateIds) then
                let idsStr = String.concat ", " duplicateIds
                Error $"Duplicate task IDs found: {idsStr}"
            
            // Check all dependencies reference valid tasks
            else
                let taskIds = problem.Tasks |> List.map (fun t -> t.Id) |> Set.ofList
                let invalidDeps =
                    problem.Tasks
                    |> List.collect (fun t -> t.Dependencies)
                    |> List.filter (fun dep -> not (Set.contains dep taskIds))
                    |> List.distinct
                
                if not (List.isEmpty invalidDeps) then
                    let depsStr = String.concat ", " invalidDeps
                    Error $"Invalid task dependencies: {depsStr}"
                
                // Check for circular dependencies (simple check - topological sort)
                else
                    // TODO: Implement cycle detection
                    Ok ()
    
    // ============================================================================
    // SOLVING - Integration with Generic Scheduling Framework
    // ============================================================================
    
    /// Solve scheduling problem using Generic Scheduling Framework (TKT-91)
    /// Automatically decides between quantum and classical solvers
    let solve (problem: SchedulingProblem) : Async<Result<Solution, string>> =
        async {
            // Validate problem first
            match validate problem with
            | Error err -> return Error err
            | Ok () ->
                
                // Map our domain types to Generic Scheduling Framework types
                let scheduledTasks : Scheduling.ScheduledTask<string> list =
                    problem.Tasks
                    |> List.map (fun task ->
                        {
                            Scheduling.Id = task.Id
                            Scheduling.Value = task.Id  // Use ID as value for now
                            Scheduling.Duration = task.Duration
                            Scheduling.EarliestStart = task.EarliestStart
                            Scheduling.Deadline = task.Deadline
                            Scheduling.ResourceRequirements = task.ResourceRequirements
                            Scheduling.Priority = task.Priority
                            Scheduling.Properties = Map.empty
                        })
                
                let resources : Scheduling.Resource<string> list =
                    problem.Resources
                    |> List.map (fun res ->
                        {
                            Id = res.Id
                            Value = res.Id
                            Capacity = res.Capacity
                            AvailableWindows = [(res.AvailableFrom, res.AvailableTo)]
                            CostPerUnit = res.CostPerUnit
                            Properties = Map.empty
                        })
                
                // Build dependencies (FinishToStart with 0 lag)
                let dependencies : Scheduling.Dependency list =
                    problem.Tasks
                    |> List.collect (fun task ->
                        task.Dependencies
                        |> List.map (fun depId -> Scheduling.FinishToStart(depId, task.Id, 0.0)))
                
                // Map objective
                let objective =
                    match problem.Objective with
                    | MinimizeMakespan -> Scheduling.MinimizeMakespan
                    | MinimizeCost -> Scheduling.MinimizeCost
                    | MaximizeResourceUtilization -> Scheduling.MaximizeResourceUtilization
                    | MinimizeLateness -> Scheduling.MinimizeTardiness
                
                // Build scheduling problem using fluent API
                let genericProblem =
                    Scheduling.SchedulingBuilder.Create()
                        .Tasks(scheduledTasks)
                        .Resources(resources)
                        .Objective(objective)
                        .TimeHorizon(problem.TimeHorizon)
                
                // Add dependencies
                let problemWithDeps =
                    dependencies
                    |> List.fold (fun (builder: Scheduling.SchedulingBuilder<string, string>) dep ->
                        builder.AddDependency(dep)) genericProblem
                
                let finalProblem = problemWithDeps.Build()
                
                // Solve using classical solver (quantum solver TBD)
                let result = Scheduling.solveClassical finalProblem
                
                return
                    match result with
                    | Ok schedule ->
                        // Map solution back to our domain types
                        let assignments =
                            schedule.TaskAssignments
                            |> Map.toList
                            |> List.map (fun (taskId, assignment) ->
                                {
                                    TaskId = taskId
                                    StartTime = assignment.StartTime
                                    EndTime = assignment.EndTime
                                    AssignedResources = assignment.AssignedResources
                                })
                        
                        // Calculate deadline violations
                        let violations =
                            problem.Tasks
                            |> List.choose (fun task ->
                                match task.Deadline with
                                | Some deadline ->
                                    match schedule.TaskAssignments |> Map.tryFind task.Id with
                                    | Some assignment when assignment.EndTime > deadline ->
                                        Some task.Id
                                    | _ -> None
                                | None -> None)
                        
                        Ok {
                            Assignments = assignments
                            Makespan = schedule.Makespan
                            TotalCost = schedule.TotalCost
                            ResourceUtilization = Map.empty  // TODO: Calculate from schedule
                            DeadlineViolations = violations
                            IsValid = List.isEmpty violations
                        }
                    
                    | Error err ->
                        Error err
        }
    
    // ============================================================================
    // RESULT EXPORT - Gantt Chart
    // ============================================================================
    
    /// Export schedule as Gantt chart (simple text format for MVP)
    let exportGanttChart (solution: Solution) (filePath: string) : unit =
        let lines = [
            yield "# Gantt Chart - Task Schedule"
            yield ""
            yield $"Makespan: {solution.Makespan} time units"
            yield $"Total Cost: ${solution.TotalCost}"
            yield $"Valid: {solution.IsValid}"
            yield ""
            yield "Task Assignments:"
            yield "----------------"
            yield! solution.Assignments
                   |> List.sortBy (fun a -> a.StartTime)
                   |> List.map (fun a ->
                        let duration = a.EndTime - a.StartTime
                        let bar = String.replicate (int duration) "█"
                        $"{a.TaskId,-10} [{a.StartTime,6:F1} - {a.EndTime,6:F1}] {bar}")
            yield ""
            if not (List.isEmpty solution.DeadlineViolations) then
                yield "Deadline Violations:"
                yield! solution.DeadlineViolations |> List.map (fun id -> $"  - {id}")
        ]
        
        System.IO.File.WriteAllLines(filePath, lines)
