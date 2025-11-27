namespace FSharp.Azure.Quantum

/// Generic Scheduling Framework for task scheduling, resource allocation, and job shop problems.
/// 
/// Provides fluent builder API for composing scheduling problems with tasks, resources,
/// dependencies, and constraints. Supports both quantum (QAOA) and classical (List Scheduling) solvers.
module Scheduling =
    
    // ============================================================================
    // CORE TYPES - Domain Model
    // ============================================================================
    
    /// Represents a scheduled task with duration, constraints, and resource requirements.
    /// Note: Named ScheduledTask to avoid collision with System.Threading.Tasks.Task<'T>
    type ScheduledTask<'T when 'T : equality> = {
        /// Unique task identifier
        Id: string
        
        /// Task value (business data)
        Value: 'T
        
        /// Task duration in time units
        Duration: float
        
        /// Earliest allowed start time (optional)
        EarliestStart: float option
        
        /// Latest allowed completion time (deadline, optional)
        Deadline: float option
        
        /// Resource requirements (resource ID -> quantity needed)
        ResourceRequirements: Map<string, float>
        
        /// Task priority for tie-breaking (higher = more important)
        Priority: float
        
        /// Custom properties for domain-specific metadata
        Properties: Map<string, obj>
    }
    
    /// Represents a resource with capacity and availability constraints.
    type Resource<'T> = {
        /// Unique resource identifier
        Id: string
        
        /// Resource value (business data)
        Value: 'T
        
        /// Maximum capacity (units available)
        Capacity: float
        
        /// Time windows when resource is available (startTime, endTime)
        AvailableWindows: (float * float) list
        
        /// Cost per unit of resource usage
        CostPerUnit: float
        
        /// Custom properties for domain-specific metadata
        Properties: Map<string, obj>
    }
    
    // ============================================================================
    // DEPENDENCY TYPES - Task Relationships
    // ============================================================================
    
    /// Task dependency types defining temporal relationships between tasks.
    /// 
    /// Lag is the minimum time delay between the related events (can be 0 or positive).
    type Dependency =
        /// Task2 cannot start until Task1 finishes (most common)
        | FinishToStart of task1: string * task2: string * lag: float
        
        /// Task2 cannot start until Task1 starts
        | StartToStart of task1: string * task2: string * lag: float
        
        /// Task2 cannot finish until Task1 finishes
        | FinishToFinish of task1: string * task2: string * lag: float
        
        /// Task2 cannot finish until Task1 starts (rare)
        | StartToFinish of task1: string * task2: string * lag: float
    
    // ============================================================================
    // SOLUTION TYPES - Schedule Output (must be before constraints for forward ref)
    // ============================================================================
    
    /// Task assignment details in schedule.
    type TaskAssignment = {
        /// Task identifier
        TaskId: string
        
        /// Start time
        StartTime: float
        
        /// End time
        EndTime: float
        
        /// Assigned resources (resource ID -> quantity)
        AssignedResources: Map<string, float>
    }
    
    /// Resource allocation over time.
    type ResourceAllocation = {
        /// Resource identifier
        ResourceId: string
        
        /// Allocated task ID
        TaskId: string
        
        /// Allocation start time
        StartTime: float
        
        /// Allocation end time
        EndTime: float
        
        /// Quantity allocated
        Quantity: float
    }
    
    /// Complete schedule solution with task assignments and metrics.
    type Schedule = {
        /// Task assignments (task ID -> assignment details)
        TaskAssignments: Map<string, TaskAssignment>
        
        /// Resource allocations (resource ID -> time-based allocations)
        ResourceAllocations: Map<string, ResourceAllocation list>
        
        /// Total completion time (max task end time)
        Makespan: float
        
        /// Total cost of schedule
        TotalCost: float
    }
    
    // ============================================================================
    // CONSTRAINT TYPES - Problem Constraints
    // ============================================================================
    
    /// Scheduling constraints defining hard/soft rules for valid schedules.
    type SchedulingConstraint =
        /// Temporal dependencies between tasks
        | TaskDependencies of Dependency list
        
        /// Resource cannot exceed maximum capacity at any time
        | ResourceCapacity of resource: string * max: float
        
        /// Tasks must not overlap in time (e.g., same machine)
        | NoOverlap of tasks: string list
        
        /// Task must start/finish within time window
        | TimeWindow of task: string * earliest: float * latest: float
        
        /// Maximum total completion time (makespan)
        | MaxMakespan of duration: float
        
        /// Custom constraint function (for domain-specific rules)
        | Custom of (Schedule -> bool)
    
    // ============================================================================
    // OBJECTIVE TYPES - Optimization Goals
    // ============================================================================
    
    /// Scheduling objectives defining optimization criteria.
    type SchedulingObjective =
        /// Minimize total completion time (finish all tasks ASAP)
        | MinimizeMakespan
        
        /// Minimize total tardiness (delay past deadlines)
        | MinimizeTardiness
        
        /// Minimize total resource usage cost
        | MinimizeCost
        
        /// Maximize average resource utilization (keep resources busy)
        | MaximizeResourceUtilization
        
        /// Minimize total idle time across all resources
        | MinimizeIdleTime
        
        /// Custom objective function (for domain-specific goals)
        | Custom of (Schedule -> float)
    
    // ============================================================================
    // PROBLEM DEFINITION - Scheduling Problem
    // ============================================================================
    
    /// Complete scheduling problem definition.
    type SchedulingProblem<'TTask, 'TResource when 'TTask : equality and 'TResource : equality> = {
        /// Tasks to schedule
        Tasks: ScheduledTask<'TTask> list
        
        /// Available resources
        Resources: Resource<'TResource> list
        
        /// Task dependencies
        Dependencies: Dependency list
        
        /// Scheduling constraints
        Constraints: SchedulingConstraint list
        
        /// Optimization objective
        Objective: SchedulingObjective
        
        /// Time horizon (max time considered)
        TimeHorizon: float
    }
    
    // ============================================================================
    // FLUENT BUILDER - Scheduling Problem Construction
    // ============================================================================
    
    /// Fluent builder for composing scheduling problems with method chaining.
    /// Uses immutable record pattern for thread-safety and functional composition.
    /// 
    /// Example:
    /// ```fsharp
    /// let problem =
    ///     SchedulingBuilder.Create()
    ///         .Tasks([task1; task2; task3])
    ///         .Resources([resource1; resource2])
    ///         .AddDependency(FinishToStart("T1", "T2", 0.0))
    ///         .Objective(MinimizeMakespan)
    ///         .Build()
    /// ```
    type SchedulingBuilder<'TTask, 'TResource when 'TTask : equality and 'TResource : equality> = private {
        tasks: ScheduledTask<'TTask> list
        resources: Resource<'TResource> list
        dependencies: Dependency list
        constraints: SchedulingConstraint list
        objective: SchedulingObjective
        timeHorizon: float
    } with
        /// Create a new builder with default values
        static member Create() : SchedulingBuilder<'TTask, 'TResource> = {
            tasks = []
            resources = []
            dependencies = []
            constraints = []
            objective = MinimizeMakespan
            timeHorizon = 100.0  // default 100 time units
        }
        
        /// Fluent API: Set tasks to schedule
        member this.Tasks(taskList: ScheduledTask<'TTask> list) =
            { this with tasks = taskList }
        
        /// Fluent API: Set available resources
        member this.Resources(resourceList: Resource<'TResource> list) =
            { this with resources = resourceList }
        
        /// Fluent API: Add a task dependency
        member this.AddDependency(dependency: Dependency) =
            { this with dependencies = dependency :: this.dependencies }
        
        /// Fluent API: Add a scheduling constraint
        member this.AddConstraint(constr: SchedulingConstraint) =
            { this with constraints = constr :: this.constraints }
        
        /// Fluent API: Set optimization objective
        member this.Objective(obj: SchedulingObjective) =
            { this with objective = obj }
        
        /// Fluent API: Set time horizon
        member this.TimeHorizon(horizon: float) =
            { this with timeHorizon = horizon }
        
        /// Build the scheduling problem
        member this.Build() : SchedulingProblem<'TTask, 'TResource> =
            {
                Tasks = this.tasks
                Resources = this.resources
                Dependencies = List.rev this.dependencies  // reverse to maintain insertion order
                Constraints = List.rev this.constraints    // reverse to maintain insertion order
                Objective = this.objective
                TimeHorizon = this.timeHorizon
            }
    
    // ============================================================================
    // HELPER FUNCTIONS - Task and Resource Creation
    // ============================================================================
    
    /// Create a scheduled task with minimal fields (id, value, duration).
    /// All optional fields default to None/empty.
    let task (id: string) (value: 'T) (duration: float) : ScheduledTask<'T> =
        {
            Id = id
            Value = value
            Duration = duration
            EarliestStart = None
            Deadline = None
            ResourceRequirements = Map.empty
            Priority = 0.0
            Properties = Map.empty
        }
    
    /// Create a scheduled task with resource requirements.
    let taskWithRequirements (id: string) (value: 'T) (duration: float) (requirements: (string * float) list) : ScheduledTask<'T> =
        {
            Id = id
            Value = value
            Duration = duration
            EarliestStart = None
            Deadline = None
            ResourceRequirements = Map.ofList requirements
            Priority = 0.0
            Properties = Map.empty
        }
    
    // ============================================================================
    // CLASSICAL SOLVER - List Scheduling Algorithm
    // ============================================================================
    
    /// Classical List Scheduling solver with dependency resolution.
    /// Uses greedy heuristic to schedule tasks on available resources.
    let solveClassical (problem: SchedulingProblem<'TTask, 'TResource>) : Result<Schedule, string> =
        try
            // Helper: Check if all dependencies are satisfied
            let areDependenciesSatisfied (taskId: string) (scheduled: Map<string, TaskAssignment>) : bool =
                problem.Dependencies
                |> List.forall (fun dep ->
                    match dep with
                    | FinishToStart(t1, t2, _) when t2 = taskId -> scheduled.ContainsKey(t1)
                    | StartToStart(t1, t2, _) when t2 = taskId -> scheduled.ContainsKey(t1)
                    | FinishToFinish(t1, t2, _) when t2 = taskId -> scheduled.ContainsKey(t1)
                    | StartToFinish(t1, t2, _) when t2 = taskId -> scheduled.ContainsKey(t1)
                    | _ -> true)  // Dependency doesn't involve this task
            
            // Helper: Get earliest start time based on all dependency types
            let getEarliestStartTime (taskId: string) (scheduled: Map<string, TaskAssignment>) : float =
                let depTimes =
                    problem.Dependencies
                    |> List.choose (fun dep ->
                        match dep with
                        // Task2 cannot start until Task1 finishes + lag
                        | FinishToStart(t1, t2, lag) when t2 = taskId ->
                            scheduled
                            |> Map.tryFind t1
                            |> Option.map (fun assignment -> assignment.EndTime + lag)
                        
                        // Task2 cannot start until Task1 starts + lag
                        | StartToStart(t1, t2, lag) when t2 = taskId ->
                            scheduled
                            |> Map.tryFind t1
                            |> Option.map (fun assignment -> assignment.StartTime + lag)
                        
                        // For FinishToFinish: Task2 must finish after Task1 finishes + lag
                        // This affects start time: startTime = (t1.EndTime + lag) - task2.Duration
                        | FinishToFinish(t1, t2, lag) when t2 = taskId ->
                            let task2Duration = 
                                problem.Tasks
                                |> List.find (fun t -> t.Id = taskId)
                                |> fun t -> t.Duration
                            scheduled
                            |> Map.tryFind t1
                            |> Option.map (fun assignment -> (assignment.EndTime + lag) - task2Duration)
                        
                        // For StartToFinish: Task2 must finish after Task1 starts + lag (rare)
                        // This affects start time: startTime = (t1.StartTime + lag) - task2.Duration
                        | StartToFinish(t1, t2, lag) when t2 = taskId ->
                            let task2Duration = 
                                problem.Tasks
                                |> List.find (fun t -> t.Id = taskId)
                                |> fun t -> t.Duration
                            scheduled
                            |> Map.tryFind t1
                            |> Option.map (fun assignment -> (assignment.StartTime + lag) - task2Duration)
                        
                        | _ -> None)  // Dependency doesn't constrain this task's start time
                
                match depTimes with
                | [] -> 0.0
                | times -> List.max times
            
            // List Scheduling Algorithm
            let rec scheduleRecursive (remaining: ScheduledTask<'TTask> list) (scheduled: Map<string, TaskAssignment>) : Map<string, TaskAssignment> =
                if List.isEmpty remaining then
                    scheduled
                else
                    // Find tasks with all dependencies satisfied
                    let ready =
                        remaining
                        |> List.filter (fun task -> areDependenciesSatisfied task.Id scheduled)
                        |> List.sortByDescending (fun t -> t.Priority)  // Higher priority first
                    
                    match ready with
                    | [] ->
                        failwith "Circular dependency detected or invalid task graph"
                    
                    | task :: _ ->
                        // Schedule this task
                        let earliestStart = getEarliestStartTime task.Id scheduled
                        let startTime = max earliestStart (task.EarliestStart |> Option.defaultValue 0.0)
                        let endTime = startTime + task.Duration
                        
                        let assignment = {
                            TaskId = task.Id
                            StartTime = startTime
                            EndTime = endTime
                            AssignedResources = Map.empty  // TODO: Resource allocation
                        }
                        
                        let newScheduled = scheduled |> Map.add task.Id assignment
                        let newRemaining = remaining |> List.filter (fun t -> t.Id <> task.Id)
                        
                        scheduleRecursive newRemaining newScheduled
            
            // Run scheduling algorithm
            let assignments = scheduleRecursive problem.Tasks Map.empty
            
            // Calculate makespan
            let makespan =
                if Map.isEmpty assignments then 0.0
                else
                    assignments
                    |> Map.toSeq
                    |> Seq.map (fun (_, assignment) -> assignment.EndTime)
                    |> Seq.max
            
            // Build schedule
            let schedule = {
                TaskAssignments = assignments
                ResourceAllocations = Map.empty  // TODO: Resource tracking
                Makespan = makespan
                TotalCost = 0.0  // TODO: Cost calculation
            }
            
            Ok schedule
            
        with
            | ex -> Error $"Scheduling failed: {ex.Message}"
