namespace FSharp.Azure.Quantum

/// <summary>
/// Task Scheduling Domain Builder - F# Computation Expression API
/// 
/// Provides idiomatic F# builders for task scheduling optimization with dependencies,
/// resource constraints, and deadline management.
/// </summary>
/// <remarks>
/// <para>Uses underlying Generic Scheduling Framework (TKT-91) for solving.</para>
/// 
/// <para><b>Available Builders:</b></para>
/// <list type="bullet">
/// <item><c>scheduledTask { ... }</c> - Define tasks with dependencies and constraints</item>
/// <item><c>resource { ... }</c> - Define resource capacity and cost</item>
/// <item><c>scheduling { ... }</c> - Compose complete scheduling problems</item>
/// </list>
/// 
/// <para><b>Example Usage:</b></para>
/// <code>
/// open FSharp.Azure.Quantum.TaskScheduling
/// 
/// // Define tasks with dependencies
/// let taskA = scheduledTask {
///     id "TaskA"
///     duration (hours 2.0)
///     priority 10.0
/// }
/// 
/// let taskB = scheduledTask {
///     id "TaskB"
///     duration (minutes 30.0)
///     after "TaskA"  // TaskB depends on TaskA
///     requires "Worker" 1.0
///     deadline 180.0
/// }
/// 
/// // Define resources
/// let worker = resource {
///     id "Worker"
///     capacity 2.0
///     costPerUnit 50.0
/// }
/// 
/// // Compose scheduling problem
/// let problem = scheduling {
///     tasks [taskA; taskB]
///     resources [worker]
///     objective MinimizeMakespan
/// }
/// 
/// // Solve
/// let! solution = solve problem
/// printfn "Makespan: %.1f minutes" solution.Makespan
/// </code>
/// </remarks>
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
    [<Struct>]
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
    
    /// <summary>Convert minutes to time units (float).</summary>
    /// <param name="value">Number of minutes</param>
    /// <returns>Duration in time units (1 minute = 1.0)</returns>
    /// <example>
    /// <code>
    /// let taskDuration = minutes 30.0  // 30 minutes
    /// </code>
    /// </example>
    let minutes (value: float) : Duration = value
    
    /// <summary>Convert hours to time units (60 minutes).</summary>
    /// <param name="value">Number of hours</param>
    /// <returns>Duration in time units (1 hour = 60.0)</returns>
    /// <example>
    /// <code>
    /// let taskDuration = hours 2.0  // 120 minutes
    /// </code>
    /// </example>
    let hours (value: float) : Duration = value * 60.0
    
    /// <summary>Convert days to time units (24 hours).</summary>
    /// <param name="value">Number of days</param>
    /// <returns>Duration in time units (1 day = 1440.0)</returns>
    /// <example>
    /// <code>
    /// let taskDuration = days 1.0  // 1440 minutes
    /// </code>
    /// </example>
    let days (value: float) : Duration = value * 24.0 * 60.0
    
    // ============================================================================
    // COMPUTATION EXPRESSION BUILDERS - Task Builder
    // ============================================================================
    
    /// <summary>
    /// Computation expression builder for defining scheduled tasks.
    /// </summary>
    /// <remarks>
    /// <para><b>Available Operations:</b></para>
    /// <list type="table">
    /// <listheader><term>Operation</term><description>Description</description></listheader>
    /// <item><term><c>id "TaskA"</c></term><description>Set unique task identifier (required)</description></item>
    /// <item><term><c>duration (hours 2.0)</c></term><description>Set task duration in time units (required)</description></item>
    /// <item><term><c>after "TaskA"</c></term><description>Add single dependency - this task starts after TaskA completes</description></item>
    /// <item><term><c>afterMultiple ["A"; "B"]</c></term><description>Add multiple dependencies - this task starts after all complete</description></item>
    /// <item><term><c>requires "Worker" 1.0</c></term><description>Add resource requirement (resource ID, quantity)</description></item>
    /// <item><term><c>priority 10.0</c></term><description>Set task priority for tie-breaking (higher = more important)</description></item>
    /// <item><term><c>deadline 180.0</c></term><description>Set latest completion time (reports violation if missed)</description></item>
    /// <item><term><c>earliestStart 60.0</c></term><description>Set earliest allowed start time</description></item>
    /// </list>
    /// 
    /// <para><b>Example Usage:</b></para>
    /// <code>
    /// let task = scheduledTask {
    ///     id "TaskB"
    ///     duration (hours 2.0)
    ///     after "TaskA"
    ///     requires "Worker" 2.0
    ///     priority 10.0
    ///     deadline 240.0
    /// }
    /// </code>
    /// </remarks>
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
        
        /// <summary>Set task unique identifier.</summary>
        /// <param name="id">Task ID (must be unique within problem)</param>
        [<CustomOperation("id")>]
        member _.Id(task: Task, id: string) : Task =
            { task with Id = id }
        
        /// <summary>Set task duration in time units.</summary>
        /// <param name="duration">Duration (use helpers: <c>minutes</c>, <c>hours</c>, <c>days</c>)</param>
        [<CustomOperation("duration")>]
        member _.Duration(task: Task, duration: Duration) : Task =
            { task with Duration = duration }
        
        /// <summary>Add single task dependency - this task must start after the specified task completes.</summary>
        /// <param name="dependsOn">Task ID that must complete before this task</param>
        [<CustomOperation("after")>]
        member _.After(task: Task, dependsOn: string) : Task =
            { task with Dependencies = dependsOn :: task.Dependencies }
        
        /// <summary>Add multiple task dependencies - this task must start after all specified tasks complete.</summary>
        /// <param name="dependsOn">List of task IDs that must complete before this task</param>
        [<CustomOperation("afterMultiple")>]
        member _.AfterMultiple(task: Task, dependsOn: string list) : Task =
            { task with Dependencies = dependsOn @ task.Dependencies }
        
        /// <summary>Add resource requirement for this task.</summary>
        /// <param name="resourceId">Resource identifier</param>
        /// <param name="amount">Quantity of resource required</param>
        [<CustomOperation("requires")>]
        member _.Requires(task: Task, resourceId: string, amount: float) : Task =
            { task with ResourceRequirements = task.ResourceRequirements |> Map.add resourceId amount }
        
        /// <summary>Set task priority for tie-breaking when multiple tasks are ready.</summary>
        /// <param name="priority">Priority value (higher = more important, default 0.0)</param>
        [<CustomOperation("priority")>]
        member _.Priority(task: Task, priority: float) : Task =
            { task with Priority = priority }
        
        /// <summary>Set deadline (latest allowed completion time).</summary>
        /// <param name="deadline">Time by which task must complete (reports violation if missed)</param>
        [<CustomOperation("deadline")>]
        member _.Deadline(task: Task, deadline: float) : Task =
            { task with Deadline = Some deadline }
        
        /// <summary>Set earliest allowed start time.</summary>
        /// <param name="earliest">Time before which task cannot start</param>
        [<CustomOperation("earliestStart")>]
        member _.EarliestStart(task: Task, earliest: float) : Task =
            { task with EarliestStart = Some earliest }
    
    /// <summary>
    /// Global instance of the <c>scheduledTask</c> computation expression builder.
    /// Use this to define tasks with dependencies and constraints.
    /// </summary>
    /// <example>
    /// <code>
    /// let taskA = scheduledTask {
    ///     id "TaskA"
    ///     duration (hours 2.0)
    /// }
    /// 
    /// let taskB = scheduledTask {
    ///     id "TaskB"
    ///     duration (minutes 30.0)
    ///     after "TaskA"  // Dependency: B starts after A completes
    /// }
    /// </code>
    /// </example>
    let scheduledTask = ScheduledTaskBuilder()
    
    // ============================================================================
    // COMPUTATION EXPRESSION BUILDERS - Resource Builder
    // ============================================================================
    
    /// <summary>
    /// Computation expression builder for defining resources with capacity and cost.
    /// </summary>
    /// <remarks>
    /// <para><b>Available Operations:</b></para>
    /// <list type="table">
    /// <listheader><term>Operation</term><description>Description</description></listheader>
    /// <item><term><c>id "Worker"</c></term><description>Set unique resource identifier (required)</description></item>
    /// <item><term><c>capacity 2.0</c></term><description>Set maximum resource capacity/units available (required)</description></item>
    /// <item><term><c>costPerUnit 50.0</c></term><description>Set cost per unit of resource usage (default 0.0)</description></item>
    /// <item><term><c>availableWindow 0.0 480.0</c></term><description>Set time window when resource is available (default always)</description></item>
    /// </list>
    /// 
    /// <para><b>Example Usage:</b></para>
    /// <code>
    /// let worker = resource {
    ///     id "Worker"
    ///     capacity 3.0
    ///     costPerUnit 50.0
    ///     availableWindow 0.0 480.0  // Available 0-480 minutes
    /// }
    /// </code>
    /// </remarks>
    type ResourceBuilder() =
        
        member _.Yield(_) : Resource =
            {
                Id = ""
                Capacity = 0.0
                CostPerUnit = 0.0
                AvailableFrom = 0.0
                AvailableTo = System.Double.MaxValue
            }
        
        /// <summary>Set resource unique identifier.</summary>
        /// <param name="id">Resource ID (must be unique within problem)</param>
        [<CustomOperation("id")>]
        member _.Id(resource: Resource, id: string) : Resource =
            { resource with Id = id }
        
        /// <summary>Set resource capacity (maximum units available).</summary>
        /// <param name="capacity">Capacity value (e.g., 3.0 workers, 8.0 CPU cores)</param>
        [<CustomOperation("capacity")>]
        member _.Capacity(resource: Resource, capacity: float) : Resource =
            { resource with Capacity = capacity }
        
        /// <summary>Set cost per unit of resource usage.</summary>
        /// <param name="cost">Cost per time unit (default 0.0)</param>
        [<CustomOperation("costPerUnit")>]
        member _.CostPerUnit(resource: Resource, cost: float) : Resource =
            { resource with CostPerUnit = cost }
        
        /// <summary>Set time window when resource is available.</summary>
        /// <param name="fromTime">Start time of availability</param>
        /// <param name="toTime">End time of availability</param>
        [<CustomOperation("availableWindow")>]
        member _.AvailableWindow(resource: Resource, fromTime: float, toTime: float) : Resource =
            { resource with AvailableFrom = fromTime; AvailableTo = toTime }
    
    /// <summary>
    /// Global instance of the <c>resource</c> computation expression builder.
    /// Use this to define resources with capacity and cost constraints.
    /// </summary>
    /// <example>
    /// <code>
    /// let cpu = resource {
    ///     id "CPU"
    ///     capacity 8.0  // 8 cores
    ///     costPerUnit 0.10  // $0.10 per minute per core
    /// }
    /// </code>
    /// </example>
    let resource = ResourceBuilder()
    
    // ============================================================================
    // HELPER FUNCTIONS - Simple Resource Creation
    // ============================================================================
    
    /// <summary>
    /// Quick helper to create a crew/worker resource (common case).
    /// Simpler alternative to using the <c>resource { ... }</c> builder for basic resources.
    /// </summary>
    /// <param name="name">Resource name/identifier</param>
    /// <param name="capacity">Number of units available</param>
    /// <param name="cost">Cost per unit per time unit</param>
    /// <returns>Resource configured with specified parameters</returns>
    /// <example>
    /// <code>
    /// let safetyCrew = crew "SafetyCrew" 2.0 100.0
    /// // Equivalent to:
    /// // resource { id "SafetyCrew"; capacity 2.0; costPerUnit 100.0 }
    /// </code>
    /// </example>
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
    
    /// <summary>
    /// Computation expression builder for composing complete scheduling problems.
    /// </summary>
    /// <remarks>
    /// <para><b>Available Operations:</b></para>
    /// <list type="table">
    /// <listheader><term>Operation</term><description>Description</description></listheader>
    /// <item><term><c>tasks [taskA; taskB]</c></term><description>Set list of tasks to schedule (required)</description></item>
    /// <item><term><c>resources [res1; res2]</c></term><description>Set list of available resources (optional)</description></item>
    /// <item><term><c>objective MinimizeMakespan</c></term><description>Set optimization goal (default MinimizeMakespan)</description></item>
    /// <item><term><c>timeHorizon 300.0</c></term><description>Set maximum time to consider (default 1000.0)</description></item>
    /// </list>
    /// 
    /// <para><b>Available Objectives:</b></para>
    /// <list type="bullet">
    /// <item><c>MinimizeMakespan</c> - Finish all tasks as early as possible</item>
    /// <item><c>MinimizeCost</c> - Minimize total resource usage cost</item>
    /// <item><c>MaximizeResourceUtilization</c> - Keep resources busy</item>
    /// <item><c>MinimizeLateness</c> - Minimize deadline violations</item>
    /// </list>
    /// 
    /// <para><b>Example Usage:</b></para>
    /// <code>
    /// let problem = scheduling {
    ///     tasks [taskA; taskB; taskC]
    ///     resources [worker; machine]
    ///     objective MinimizeMakespan
    ///     timeHorizon 500.0
    /// }
    /// </code>
    /// </remarks>
    type SchedulingProblemBuilder() =
        
        member _.Yield(_) : SchedulingProblem =
            {
                Tasks = []
                Resources = []
                Objective = MinimizeMakespan
                TimeHorizon = 1000.0  // Default: 1000 time units
            }
        
        /// <summary>Set tasks to schedule.</summary>
        /// <param name="tasks">List of tasks defined with <c>scheduledTask { ... }</c></param>
        [<CustomOperation("tasks")>]
        member _.Tasks(problem: SchedulingProblem, tasks: Task list) : SchedulingProblem =
            { problem with Tasks = tasks }
        
        /// <summary>Set available resources.</summary>
        /// <param name="resources">List of resources defined with <c>resource { ... }</c> or <c>crew</c></param>
        [<CustomOperation("resources")>]
        member _.Resources(problem: SchedulingProblem, resources: Resource list) : SchedulingProblem =
            { problem with Resources = resources }
        
        /// <summary>Set optimization objective.</summary>
        /// <param name="objective">Objective: MinimizeMakespan, MinimizeCost, MaximizeResourceUtilization, or MinimizeLateness</param>
        [<CustomOperation("objective")>]
        member _.Objective(problem: SchedulingProblem, objective: Objective) : SchedulingProblem =
            { problem with Objective = objective }
        
        /// <summary>Set time horizon (maximum time to consider in scheduling).</summary>
        /// <param name="horizon">Maximum time units (default 1000.0)</param>
        [<CustomOperation("timeHorizon")>]
        member _.TimeHorizon(problem: SchedulingProblem, horizon: float) : SchedulingProblem =
            { problem with TimeHorizon = horizon }
    
    /// <summary>
    /// Global instance of the <c>scheduling</c> computation expression builder.
    /// Use this to compose complete scheduling problems from tasks and resources.
    /// </summary>
    /// <example>
    /// <code>
    /// let problem = scheduling {
    ///     tasks [taskA; taskB; taskC]
    ///     resources [worker1; worker2]
    ///     objective MinimizeMakespan
    /// }
    /// 
    /// let! solution = solve problem
    /// </code>
    /// </example>
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
                
                // Check for circular dependencies using DFS
                else
                    // Build adjacency list for dependency graph
                    let adjList =
                        problem.Tasks
                        |> List.map (fun t -> t.Id, t.Dependencies)
                        |> Map.ofList
                    
                    // DFS-based cycle detection
                    let rec detectCycle (visited: Set<string>) (recStack: Set<string>) (taskId: string) : Result<unit, string> =
                        if Set.contains taskId recStack then
                            Error $"Circular dependency detected involving task: {taskId}"
                        elif Set.contains taskId visited then
                            Ok ()
                        else
                            let deps = Map.tryFind taskId adjList |> Option.defaultValue []
                            let newVisited = Set.add taskId visited
                            let newRecStack = Set.add taskId recStack
                            
                            let rec checkDeps deps =
                                match deps with
                                | [] -> Ok ()
                                | dep :: rest ->
                                    match detectCycle newVisited newRecStack dep with
                                    | Error e -> Error e
                                    | Ok () -> checkDeps rest
                            
                            checkDeps deps
                    
                    // Check all tasks for cycles
                    let rec checkAllTasks (tasks: Task list) (visited: Set<string>) =
                        match tasks with
                        | [] -> Ok ()
                        | task :: rest ->
                            match detectCycle visited Set.empty task.Id with
                            | Error e -> Error e
                            | Ok () -> checkAllTasks rest (Set.add task.Id visited)
                    
                    checkAllTasks problem.Tasks Set.empty
    
    // ============================================================================
    // SOLVING - Integration with Generic Scheduling Framework
    // ============================================================================
    
    /// <summary>
    /// Solve the scheduling problem and return an optimized schedule.
    /// Automatically validates the problem and uses the Generic Scheduling Framework (TKT-91).
    /// </summary>
    /// <param name="problem">Scheduling problem composed with <c>scheduling { ... }</c></param>
    /// <returns>
    /// Async Result with either:
    /// <list type="bullet">
    /// <item><c>Ok solution</c> - Successfully computed schedule with task assignments</item>
    /// <item><c>Error message</c> - Validation or scheduling failure message</item>
    /// </list>
    /// </returns>
    /// <remarks>
    /// <para><b>Validation Checks:</b></para>
    /// <list type="bullet">
    /// <item>All tasks have non-empty unique IDs</item>
    /// <item>All dependencies reference existing tasks</item>
    /// <item>No circular dependencies (DFS-based detection)</item>
    /// </list>
    /// 
    /// <para><b>Solution Fields:</b></para>
    /// <list type="bullet">
    /// <item><c>Assignments</c> - List of task assignments with start/end times</item>
    /// <item><c>Makespan</c> - Total completion time (max end time)</item>
    /// <item><c>TotalCost</c> - Total resource usage cost</item>
    /// <item><c>DeadlineViolations</c> - List of task IDs that missed deadlines</item>
    /// <item><c>IsValid</c> - True if no deadline violations</item>
    /// </list>
    /// </remarks>
    /// <example>
    /// <code>
    /// let problem = scheduling {
    ///     tasks [taskA; taskB; taskC]
    ///     resources [worker]
    ///     objective MinimizeMakespan
    /// }
    /// 
    /// let! result = solve problem
    /// 
    /// match result with
    /// | Ok solution ->
    ///     printfn "Makespan: %.1f minutes" solution.Makespan
    ///     solution.Assignments
    ///     |> List.iter (fun a ->
    ///         printfn "%s: [%.1f - %.1f]" a.TaskId a.StartTime a.EndTime)
    /// | Error msg ->
    ///     printfn "Scheduling failed: %s" msg
    /// </code>
    /// </example>
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
                        
                        // Calculate resource utilization
                        let resourceUtilization =
                            problem.Resources
                            |> List.map (fun resource ->
                                let allocations = 
                                    schedule.ResourceAllocations 
                                    |> Map.tryFind resource.Id 
                                    |> Option.defaultValue []
                                
                                // Calculate total allocated time * quantity
                                let totalAllocated =
                                    allocations
                                    |> List.sumBy (fun alloc -> 
                                        (alloc.EndTime - alloc.StartTime) * alloc.Quantity)
                                
                                // Calculate maximum possible allocation (capacity * available time)
                                let availableTime = resource.AvailableTo - resource.AvailableFrom
                                let maxPossible = resource.Capacity * availableTime
                                
                                // Calculate utilization percentage (0.0 to 1.0)
                                let utilization = 
                                    if maxPossible > 0.0 then totalAllocated / maxPossible
                                    else 0.0
                                
                                resource.Id, utilization)
                            |> Map.ofList
                        
                        Ok {
                            Assignments = assignments
                            Makespan = schedule.Makespan
                            TotalCost = schedule.TotalCost
                            ResourceUtilization = resourceUtilization
                            DeadlineViolations = violations
                            IsValid = List.isEmpty violations
                        }
                    
                    | Error err ->
                        Error err
        }
    
    // ============================================================================
    // RESULT EXPORT - Gantt Chart
    // ============================================================================
    
    /// <summary>
    /// Export the schedule as a Gantt chart in text format.
    /// Creates a visual timeline of task assignments with start/end times.
    /// </summary>
    /// <param name="solution">Scheduling solution from <c>solve</c></param>
    /// <param name="filePath">Output file path (e.g., "schedule.txt")</param>
    /// <remarks>
    /// <para><b>Output Format:</b></para>
    /// <list type="bullet">
    /// <item>Header with makespan, total cost, and validity</item>
    /// <item>Task assignments sorted by start time</item>
    /// <item>Visual bars showing task duration (█ characters)</item>
    /// <item>Deadline violations list (if any)</item>
    /// </list>
    /// </remarks>
    /// <example>
    /// <code>
    /// let! result = solve problem
    /// match result with
    /// | Ok solution ->
    ///     exportGanttChart solution "my-schedule.txt"
    ///     printfn "Gantt chart saved to my-schedule.txt"
    /// | Error msg ->
    ///     printfn "Failed: %s" msg
    /// </code>
    /// 
    /// <para><b>Example Output:</b></para>
    /// <code>
    /// # Gantt Chart - Task Schedule
    /// 
    /// Makespan: 140.0 time units
    /// Total Cost: $0.0
    /// Valid: True
    /// 
    /// Task Assignments:
    /// ----------------
    /// TaskA      [   0.0 -   10.0] ██████████
    /// TaskB      [  10.0 -   30.0] ████████████████████
    /// TaskC      [  30.0 -   45.0] ███████████████
    /// </code>
    /// </example>
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
