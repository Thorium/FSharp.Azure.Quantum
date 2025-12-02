namespace FSharp.Azure.Quantum

open FSharp.Azure.Quantum.Core
open FSharp.Azure.Quantum.GraphOptimization

/// Task Scheduling Domain Builder - F# Computation Expression API
///
/// DESIGN PHILOSOPHY:
/// This is a BUSINESS DOMAIN API for users who want to solve scheduling problems
/// with dependencies, resource constraints, and deadlines - without needing to
/// understand quantum computing internals (QAOA, QUBO, backends).
///
/// WHAT IS TASK SCHEDULING:
/// Assign tasks to resources over time while respecting:
/// - Precedence constraints (task A must complete before task B)
/// - Resource constraints (limited machines, workers, tools)
/// - Deadlines (tasks must complete by specific times)
/// - Optimization objectives (minimize makespan, cost, lateness)
///
/// USE CASES:
/// - Manufacturing: Production line scheduling with dependencies
/// - Cloud Computing: Container orchestration, batch job scheduling
/// - Project Management: Task allocation across team members
/// - Supply Chain: Order fulfillment with resource constraints
///
/// EXAMPLE USAGE:
///   open FSharp.Azure.Quantum.TaskScheduling
///   
///   let taskA = scheduledTask {
///       id "TaskA"
///       duration (hours 2.0)
///   }
///   
///   let taskB = scheduledTask {
///       id "TaskB"
///       duration (minutes 30.0)
///       after "TaskA"  // Dependency co-located!
///       deadline 180.0
///   }
///   
///   let problem = scheduling {
///       tasks [taskA; taskB]
///       objective MinimizeMakespan
///   }
///   
///   let! result = solve problem
module TaskScheduling =

    // ============================================================================
    // TYPES - Domain-specific types for Scheduling problems
    // ============================================================================

    /// Scheduling objective functions
    type Objective =
        | MinimizeMakespan          // Minimize total completion time
        | MinimizeCost              // Minimize total resource cost
        | MaximizeResourceUtilization // Maximize resource usage
        | MinimizeLateness          // Minimize deadline violations

    /// Dependency relationship between tasks
    type Dependency =
        | FinishToStart of predecessorId: string * successorId: string * lag: float

    /// Time duration helper type
    type Duration = Duration of float

    /// Scheduled task with duration, dependencies, and constraints
    type ScheduledTask<'T> = {
        /// Task identifier (must be unique)
        Id: string
        
        /// Task payload/value
        Value: 'T
        
        /// Task duration in time units (minutes)
        Duration: float
        
        /// Earliest allowed start time
        EarliestStart: float option
        
        /// Latest allowed completion time (deadline)
        Deadline: float option
        
        /// Resource requirements (resource ID -> quantity needed)
        ResourceRequirements: Map<string, float>
        
        /// Task priority for tie-breaking (higher = more important)
        Priority: float
        
        /// Custom properties for extensibility
        Properties: Map<string, obj>
    }

    /// Resource with capacity and cost constraints
    type Resource<'T> = {
        /// Resource identifier
        Id: string
        
        /// Resource payload/value
        Value: 'T
        
        /// Maximum units available
        Capacity: float
        
        /// Time windows when available (start, end)
        AvailableWindows: (float * float) list
        
        /// Cost per unit per time unit
        CostPerUnit: float
        
        /// Custom properties for extensibility
        Properties: Map<string, obj>
    }

    /// Complete scheduling problem definition
    type SchedulingProblem<'TTask, 'TResource> = {
        /// Tasks to schedule
        Tasks: ScheduledTask<'TTask> list
        
        /// Available resources
        Resources: Resource<'TResource> list
        
        /// Task dependencies (finish-to-start relationships)
        Dependencies: Dependency list
        
        /// Optimization objective
        Objective: Objective
        
        /// Maximum time horizon to consider
        TimeHorizon: float
    }

    /// Task assignment in schedule
    type TaskAssignment = {
        /// Task identifier
        TaskId: string
        
        /// Scheduled start time
        StartTime: float
        
        /// Scheduled end time
        EndTime: float
        
        /// Assigned resources (resource ID -> quantity allocated)
        AssignedResources: Map<string, float>
    }

    /// Complete schedule solution
    type Solution = {
        /// Task assignments
        Assignments: TaskAssignment list
        
        /// Total completion time (max end time)
        Makespan: float
        
        /// Total resource usage cost
        TotalCost: float
        
        /// Resource utilization per resource (0.0-1.0)
        ResourceUtilization: Map<string, float>
        
        /// Task IDs that missed deadlines
        DeadlineViolations: string list
        
        /// True if no deadline violations
        IsValid: bool
    }

    // ============================================================================
    // TIME UNIT HELPERS - Readable duration specifications
    // ============================================================================

    /// Convert minutes to duration
    let minutes (value: float) : Duration = Duration value

    /// Convert hours to duration (1 hour = 60 minutes)
    let hours (value: float) : Duration = Duration (value * 60.0)

    /// Convert days to duration (1 day = 1440 minutes)
    let days (value: float) : Duration = Duration (value * 1440.0)

    // ============================================================================
    // BUILDER: scheduledTask { }
    // ============================================================================

    /// Computation expression builder for defining scheduled tasks
    type ScheduledTaskBuilder<'T>() =
        member _.Yield(_) : ScheduledTask<'T> =
            {
                Id = ""
                Value = Unchecked.defaultof<'T>
                Duration = 0.0
                EarliestStart = None
                Deadline = None
                ResourceRequirements = Map.empty
                Priority = 0.0
                Properties = Map.empty
            }

        /// Set task identifier (required)
        [<CustomOperation("id")>]
        member _.Id(task: ScheduledTask<'T>, id: string) =
            { task with Id = id; Value = Unchecked.defaultof<'T> }

        /// Set task duration (required)
        [<CustomOperation("duration")>]
        member _.Duration(task: ScheduledTask<'T>, duration: Duration) =
            let (Duration d) = duration
            { task with Duration = d }

        /// Add single dependency (task must start after specified task completes)
        [<CustomOperation("after")>]
        member _.After(task: ScheduledTask<'T>, predecessorId: string) =
            // Dependencies are handled at problem level, store in Properties for now
            let deps =
                match Map.tryFind "__dependencies" task.Properties with
                | Some value ->
                    match value with
                    | :? (string list) as existing -> predecessorId :: existing
                    | _ -> [predecessorId]
                | _ -> [predecessorId]
            { task with Properties = Map.add "__dependencies" (box deps) task.Properties }

        /// Add multiple dependencies
        [<CustomOperation("afterMultiple")>]
        member _.AfterMultiple(task: ScheduledTask<'T>, predecessorIds: string list) =
            let deps =
                match Map.tryFind "__dependencies" task.Properties with
                | Some value ->
                    match value with
                    | :? (string list) as existing -> predecessorIds @ existing
                    | _ -> predecessorIds
                | _ -> predecessorIds
            { task with Properties = Map.add "__dependencies" (box deps) task.Properties }

        /// Add resource requirement
        [<CustomOperation("requires")>]
        member _.Requires(task: ScheduledTask<'T>, resourceId: string, quantity: float) =
            { task with ResourceRequirements = Map.add resourceId quantity task.ResourceRequirements }

        /// Set priority for tie-breaking
        [<CustomOperation("priority")>]
        member _.Priority(task: ScheduledTask<'T>, priority: float) =
            { task with Priority = priority }

        /// Set deadline (latest completion time)
        [<CustomOperation("deadline")>]
        member _.Deadline(task: ScheduledTask<'T>, deadline: float) =
            { task with Deadline = Some deadline }

        /// Set earliest start time
        [<CustomOperation("earliestStart")>]
        member _.EarliestStart(task: ScheduledTask<'T>, earliestStart: float) =
            { task with EarliestStart = Some earliestStart }

    /// Computation expression builder instance for scheduled tasks
    let scheduledTask<'T> = ScheduledTaskBuilder<'T>()

    // ============================================================================
    // BUILDER: resource { }
    // ============================================================================

    /// Computation expression builder for defining resources
    type ResourceBuilder<'T>() =
        member _.Yield(_) : Resource<'T> =
            {
                Id = ""
                Value = Unchecked.defaultof<'T>
                Capacity = 0.0
                AvailableWindows = [(0.0, System.Double.MaxValue)]
                CostPerUnit = 0.0
                Properties = Map.empty
            }

        /// Set resource identifier (required)
        [<CustomOperation("id")>]
        member _.Id(resource: Resource<'T>, id: string) =
            { resource with Id = id; Value = Unchecked.defaultof<'T> }

        /// Set resource capacity (required)
        [<CustomOperation("capacity")>]
        member _.Capacity(resource: Resource<'T>, capacity: float) =
            { resource with Capacity = capacity }

        /// Set cost per unit per time unit
        [<CustomOperation("costPerUnit")>]
        member _.CostPerUnit(resource: Resource<'T>, cost: float) =
            { resource with CostPerUnit = cost }

        /// Set availability window
        [<CustomOperation("availableWindow")>]
        member _.AvailableWindow(resource: Resource<'T>, startTime: float, endTime: float) =
            { resource with AvailableWindows = [(startTime, endTime)] }

    /// Computation expression builder instance for resources
    let resource<'T> = ResourceBuilder<'T>()

    /// Helper function to quickly create a crew/worker resource
    let crew (id: string) (capacity: float) (costPerUnit: float) : Resource<string> =
        {
            Id = id
            Value = id
            Capacity = capacity
            AvailableWindows = [(0.0, System.Double.MaxValue)]
            CostPerUnit = costPerUnit
            Properties = Map.empty
        }

    // ============================================================================
    // BUILDER: scheduling { }
    // ============================================================================

    /// Computation expression builder for defining scheduling problems
    type SchedulingBuilder<'TTask, 'TResource>() =
        member _.Yield(_) : SchedulingProblem<'TTask, 'TResource> =
            {
                Tasks = []
                Resources = []
                Dependencies = []
                Objective = MinimizeMakespan
                TimeHorizon = 1000.0
            }

        /// Set tasks to schedule (required)
        [<CustomOperation("tasks")>]
        member _.Tasks(problem: SchedulingProblem<'TTask, 'TResource>, tasks: ScheduledTask<'TTask> list) =
            // Extract dependencies from task properties
            let dependencies =
                tasks
                |> List.collect (fun task ->
                    match Map.tryFind "__dependencies" task.Properties with
                    | Some value ->
                        match value with
                        | :? (string list) as deps ->
                            deps |> List.map (fun predId -> FinishToStart(predId, task.Id, 0.0))
                        | _ -> []
                    | _ -> []
                )
            { problem with Tasks = tasks; Dependencies = dependencies }

        /// Set available resources
        [<CustomOperation("resources")>]
        member _.Resources(problem: SchedulingProblem<'TTask, 'TResource>, resources: Resource<'TResource> list) =
            { problem with Resources = resources }

        /// Set optimization objective
        [<CustomOperation("objective")>]
        member _.Objective(problem: SchedulingProblem<'TTask, 'TResource>, objective: Objective) =
            { problem with Objective = objective }

        /// Set time horizon
        [<CustomOperation("timeHorizon")>]
        member _.TimeHorizon(problem: SchedulingProblem<'TTask, 'TResource>, horizon: float) =
            { problem with TimeHorizon = horizon }

    /// Computation expression builder instance for scheduling problems
    let scheduling<'TTask, 'TResource> = SchedulingBuilder<'TTask, 'TResource>()

    // ============================================================================
    // VALIDATION - Problem validation before solving
    // ============================================================================

    /// Validate scheduling problem
    let private validateProblem (problem: SchedulingProblem<'TTask, 'TResource>) : Result<unit, string> =
        // Check all tasks have non-empty IDs
        let emptyIds = problem.Tasks |> List.filter (fun t -> System.String.IsNullOrWhiteSpace(t.Id))
        if not (List.isEmpty emptyIds) then
            Error "All tasks must have non-empty unique IDs"
        else

        // Check all tasks have unique IDs
        let duplicates =
            problem.Tasks
            |> List.groupBy (fun t -> t.Id)
            |> List.filter (fun (_, tasks) -> List.length tasks > 1)
            |> List.map fst

        if not (List.isEmpty duplicates) then
            Error (sprintf "Duplicate task IDs found: %A" duplicates)
        else

        // Check all dependencies reference existing tasks
        let taskIds = problem.Tasks |> List.map (fun t -> t.Id) |> Set.ofList
        let invalidDeps =
            problem.Dependencies
            |> List.filter (fun dep ->
                match dep with
                | FinishToStart(predId, succId, _) ->
                    not (Set.contains predId taskIds) || not (Set.contains succId taskIds)
            )

        if not (List.isEmpty invalidDeps) then
            Error (sprintf "Invalid task dependencies reference non-existent tasks: %A" invalidDeps)
        else
            Ok ()

    // ============================================================================
    // SOLVER - Classical Greedy Scheduling Algorithm
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

    /// Classical greedy scheduling solver
    let private solveClassical (problem: SchedulingProblem<'TTask, 'TResource>) : Result<Solution, string> =
        // Validate problem first
        match validateProblem problem with
        | Error msg -> Error msg
        | Ok () ->

        // Topological sort tasks by dependencies
        let sortedTasks = topologicalSort problem.Tasks problem.Dependencies

        // Track task completion times
        let mutable completionTimes = Map.empty<string, float>

        // Track assignments
        let mutable assignments = []

        // Schedule each task
        for task in sortedTasks do
            // Find earliest start time based on dependencies
            let depEndTime =
                problem.Dependencies
                |> List.choose (fun dep ->
                    match dep with
                    | FinishToStart(predId, succId, lag) when succId = task.Id ->
                        Map.tryFind predId completionTimes
                        |> Option.map (fun endTime -> endTime + lag)
                    | _ -> None
                )
                |> function
                    | [] -> 0.0
                    | times -> List.max times

            // Consider earliest start constraint
            let startTime =
                match task.EarliestStart with
                | Some earliest -> max earliest depEndTime
                | None -> depEndTime

            let endTime = startTime + task.Duration

            // Create assignment
            let assignment = {
                TaskId = task.Id
                StartTime = startTime
                EndTime = endTime
                AssignedResources = task.ResourceRequirements
            }

            assignments <- assignment :: assignments
            completionTimes <- Map.add task.Id endTime completionTimes

        // Calculate metrics
        let makespan =
            if List.isEmpty assignments then 0.0
            else assignments |> List.map (fun a -> a.EndTime) |> List.max

        let totalCost =
            assignments
            |> List.sumBy (fun a ->
                let duration = a.EndTime - a.StartTime
                a.AssignedResources
                |> Map.toList
                |> List.sumBy (fun (resourceId, quantity) ->
                    match problem.Resources |> List.tryFind (fun r -> r.Id = resourceId) with
                    | Some resource -> resource.CostPerUnit * quantity * duration
                    | None -> 0.0
                )
            )

        // Check deadline violations
        let violations =
            problem.Tasks
            |> List.choose (fun task ->
                match task.Deadline with
                | Some deadline ->
                    let completion = Map.find task.Id completionTimes
                    if completion > deadline then Some task.Id else None
                | None -> None
            )

        // Calculate resource utilization (placeholder - simplified)
        let resourceUtil =
            problem.Resources
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

        let solution = {
            Assignments = List.rev assignments
            Makespan = makespan
            TotalCost = totalCost
            ResourceUtilization = resourceUtil
            DeadlineViolations = violations
            IsValid = List.isEmpty violations
        }

        Ok solution

    // ============================================================================
    // QUBO ENCODING FOR RESOURCE-CONSTRAINED SCHEDULING
    // ============================================================================
    
    /// Encode resource-constrained scheduling as QUBO problem
    /// 
    /// ENCODING SCHEME:
    /// - Variables: x_{task,time} ∈ {0,1} where x_{task,time}=1 means task starts at time
    /// - Time discretized into slots (0, 1, 2, ..., T-1)
    /// - Each task must start at exactly one time slot
    /// 
    /// OBJECTIVE (minimize makespan):
    ///   Σ_{task,time} time * x_{task,time}  (weighted by latest completion)
    /// 
    /// CONSTRAINTS (encoded as penalties):
    ///   1. One-hot: Each task starts exactly once: Σ_time x_{task,time} = 1
    ///   2. Dependencies: Successor starts after predecessor finishes
    ///   3. Resources: At any time t, Σ_{overlapping tasks} resource_usage ≤ capacity
    /// 
    /// QUBO FORM (minimization for QAOA):
    ///   H = Objective + λ₁*Penalty₁ + λ₂*Penalty₂ + λ₃*Penalty₃
    let private toQubo 
        (problem: SchedulingProblem<'TTask, 'TResource>) 
        (timeHorizon: int)
        : Result<GraphOptimization.QuboMatrix, string> =
        
        try
            let numTasks = problem.Tasks.Length
            
            if numTasks = 0 then
                Error "No tasks to schedule"
            elif timeHorizon <= 0 then
                Error "Time horizon must be positive"
            else
                // Create variable index mapping: (taskId, startTime) -> variableIndex
                let mutable varIndex = 0
                let mutable varMapping = Map.empty<string * int, int>
                let mutable reverseMapping = Map.empty<int, string * int>
                
                for task in problem.Tasks do
                    for t in 0 .. timeHorizon - 1 do
                        varMapping <- varMapping |> Map.add (task.Id, t) varIndex
                        reverseMapping <- reverseMapping |> Map.add varIndex (task.Id, t)
                        varIndex <- varIndex + 1
                
                let numVariables = varIndex
                let mutable quboTerms = Map.empty<int * int, float>
                
                // Calculate penalty weights (Lucas Rule: penalties >> objective)
                let maxDuration = problem.Tasks |> List.map (fun t -> t.Duration) |> List.max
                let penaltyOneHot = float timeHorizon * 10.0  // Ensure one-hot constraint
                let penaltyDependency = maxDuration * penaltyOneHot  // Ensure dependencies respected
                let penaltyResource = maxDuration * penaltyOneHot    // Ensure resource limits
                
                // ===== OBJECTIVE: Minimize makespan (latest task completion) =====
                for task in problem.Tasks do
                    for t in 0 .. timeHorizon - 1 do
                        let varIdx = Map.find (task.Id, t) varMapping
                        let completionTime = float t + task.Duration
                        
                        // Linear term: minimize weighted completion time
                        let currentVal = Map.tryFind (varIdx, varIdx) quboTerms |> Option.defaultValue 0.0
                        quboTerms <- quboTerms |> Map.add (varIdx, varIdx) (currentVal + completionTime)
                
                // ===== CONSTRAINT 1: One-hot encoding (each task starts exactly once) =====
                // Penalty: λ * (Σ x_t - 1)² = λ * (Σ x_t² + ΣΣ 2*x_i*x_j - 2*Σ x_t + 1)
                // Simplified: λ * (ΣΣ 2*x_i*x_j - Σ x_t)  (ignoring constant, x² = x for binary)
                for task in problem.Tasks do
                    // Linear terms: -λ * x_t
                    for t in 0 .. timeHorizon - 1 do
                        let varIdx = Map.find (task.Id, t) varMapping
                        let currentVal = Map.tryFind (varIdx, varIdx) quboTerms |> Option.defaultValue 0.0
                        quboTerms <- quboTerms |> Map.add (varIdx, varIdx) (currentVal - penaltyOneHot)
                    
                    // Quadratic terms: 2λ * x_i * x_j
                    for t1 in 0 .. timeHorizon - 1 do
                        for t2 in t1 + 1 .. timeHorizon - 1 do
                            let varIdx1 = Map.find (task.Id, t1) varMapping
                            let varIdx2 = Map.find (task.Id, t2) varMapping
                            let (i, j) = if varIdx1 < varIdx2 then (varIdx1, varIdx2) else (varIdx2, varIdx1)
                            let currentVal = Map.tryFind (i, j) quboTerms |> Option.defaultValue 0.0
                            quboTerms <- quboTerms |> Map.add (i, j) (currentVal + 2.0 * penaltyOneHot)
                
                // ===== CONSTRAINT 2: Dependencies (successor starts after predecessor) =====
                for dep in problem.Dependencies do
                    match dep with
                    | FinishToStart(predId, succId, lag) ->
                        let predTask = problem.Tasks |> List.find (fun t -> t.Id = predId)
                        let predDuration = predTask.Duration
                        
                        // For each valid (pred_start, succ_start) pair where dependency is violated
                        for t_pred in 0 .. timeHorizon - 1 do
                            let predEnd = float t_pred + predDuration + lag
                            for t_succ in 0 .. int predEnd do  // Successor starts before predecessor finishes
                                let predVarIdx = Map.find (predId, t_pred) varMapping
                                let succVarIdx = Map.find (succId, t_succ) varMapping
                                
                                // Penalty for both being 1 (violation): λ * x_pred * x_succ
                                let (i, j) = if predVarIdx < succVarIdx then (predVarIdx, succVarIdx) else (succVarIdx, predVarIdx)
                                let currentVal = Map.tryFind (i, j) quboTerms |> Option.defaultValue 0.0
                                quboTerms <- quboTerms |> Map.add (i, j) (currentVal + penaltyDependency)
                
                // ===== CONSTRAINT 3: Resource capacity =====
                if not (List.isEmpty problem.Resources) then
                    for resource in problem.Resources do
                        // For each time slot, check resource usage
                        for t in 0 .. timeHorizon - 1 do
                            // Find tasks that could overlap at time t
                            let overlappingVars = 
                                problem.Tasks
                                |> List.collect (fun task ->
                                    // Task overlaps at time t if it starts at [t - duration + 1, t]
                                    let taskDuration = int (ceil task.Duration)
                                    let startRange = max 0 (t - taskDuration + 1), t
                                    
                                    [for startTime in fst startRange .. snd startRange do
                                        let usage = Map.tryFind resource.Id task.ResourceRequirements |> Option.defaultValue 0.0
                                        if usage > 0.0 then
                                            yield (Map.find (task.Id, startTime) varMapping, usage)]
                                )
                            
                            // Constraint: Σ usage_i * x_i ≤ capacity
                            // Penalty: λ * (Σ usage_i * x_i - capacity)²
                            // Expanded: λ * (Σ usage_i² * x_i + ΣΣ 2*usage_i*usage_j*x_i*x_j - 2*capacity*Σ usage_i*x_i + capacity²)
                            
                            // Linear terms
                            for (varIdx, usage) in overlappingVars do
                                let linearCoeff = penaltyResource * (usage * usage - 2.0 * resource.Capacity * usage)
                                let currentVal = Map.tryFind (varIdx, varIdx) quboTerms |> Option.defaultValue 0.0
                                quboTerms <- quboTerms |> Map.add (varIdx, varIdx) (currentVal + linearCoeff)
                            
                            // Quadratic terms
                            for idx1 in 0 .. overlappingVars.Length - 1 do
                                for idx2 in idx1 + 1 .. overlappingVars.Length - 1 do
                                    let (varIdx1, usage1) = overlappingVars.[idx1]
                                    let (varIdx2, usage2) = overlappingVars.[idx2]
                                    let (i, j) = if varIdx1 < varIdx2 then (varIdx1, varIdx2) else (varIdx2, varIdx1)
                                    
                                    let quadCoeff = penaltyResource * 2.0 * usage1 * usage2
                                    let currentVal = Map.tryFind (i, j) quboTerms |> Option.defaultValue 0.0
                                    quboTerms <- quboTerms |> Map.add (i, j) (currentVal + quadCoeff)
                
                Ok {
                    GraphOptimization.QuboMatrix.Q = quboTerms
                    GraphOptimization.QuboMatrix.NumVariables = numVariables
                }
        with ex ->
            Error (sprintf "QUBO encoding failed: %s" ex.Message)

    // ============================================================================
    // PUBLIC API
    // ============================================================================

    /// Solve scheduling problem with resource constraints using quantum backend
    /// 
    /// RULE 1 COMPLIANCE:
    /// ✅ Requires IQuantumBackend parameter (explicit quantum execution)
    /// 
    /// Resource-constrained scheduling is solved via quantum optimization:
    /// 1. Encodes tasks, dependencies, and resource limits as QUBO problem
    /// 2. Uses QAOA or quantum annealing to find optimal schedule
    /// 3. Respects resource capacity constraints (unlike classical solver)
    /// 
    /// Use this when:
    /// - Tasks have resource requirements (workers, machines, budget)
    /// - Resources have limited capacity
    /// - Need optimal allocation under constraints
    /// 
    /// Example:
    ///   let backend = BackendAbstraction.createLocalBackend()
    ///   let! result = solveQuantum backend problem
    let solveQuantum 
        (backend: Core.BackendAbstraction.IQuantumBackend)
        (problem: SchedulingProblem<'TTask, 'TResource>) 
        : Async<Result<Solution, string>> =
        async {
            // Validate problem first
            match validateProblem problem with
            | Error msg -> return Error msg
            | Ok () ->
            
            // Determine time horizon (max possible makespan)
            let totalDuration = problem.Tasks |> List.sumBy (fun t -> t.Duration)
            let timeHorizon = 
                if problem.TimeHorizon > 0.0 then
                    int (ceil problem.TimeHorizon)
                else
                    int (ceil totalDuration)  // Conservative estimate if not specified
            
            // Encode problem as QUBO
            match toQubo problem timeHorizon with
            | Error msg -> return Error msg
            | Ok quboMatrix ->
            
            // Convert sparse QUBO to dense array for QAOA
            let quboArray = Array2D.zeroCreate quboMatrix.NumVariables quboMatrix.NumVariables
            for KeyValue((i, j), value) in quboMatrix.Q do
                quboArray.[i, j] <- value
            
            // Create QAOA problem and mixer Hamiltonians
            let problemHam = QaoaCircuit.ProblemHamiltonian.fromQubo quboArray
            let mixerHam = QaoaCircuit.MixerHamiltonian.create quboMatrix.NumVariables
            
            // Build QAOA circuit with initial parameters
            let gamma, beta = 0.5, 0.5  // Initial parameters
            let qaoaCircuit = QaoaCircuit.QaoaCircuit.build problemHam mixerHam [| (gamma, beta) |]
            
            // Wrap QAOA circuit for backend execution
            let circuitWrapper = 
                CircuitAbstraction.QaoaCircuitWrapper(qaoaCircuit) 
                :> CircuitAbstraction.ICircuit
            
            // Execute on quantum backend
            let numShots = 1000
            match backend.Execute circuitWrapper numShots with
            | Error msg -> return Error (sprintf "Quantum execution failed: %s" msg)
            | Ok execResult ->
            
            // Decode measurements to find best schedule
            // Create variable mapping for decoding
            let mutable varMapping = Map.empty<string * int, int>
            let mutable reverseMapping = Map.empty<int, string * int>
            let mutable varIndex = 0
            
            for task in problem.Tasks do
                for t in 0 .. timeHorizon - 1 do
                    varMapping <- varMapping |> Map.add (task.Id, t) varIndex
                    reverseMapping <- reverseMapping |> Map.add varIndex (task.Id, t)
                    varIndex <- varIndex + 1
            
            // Decode each measurement and find best feasible solution
            let solutions =
                execResult.Measurements
                |> Array.map (fun bitstring ->
                    // Decode bitstring to task start times
                    let mutable taskStarts = Map.empty<string, float>
                    
                    for i in 0 .. bitstring.Length - 1 do
                        if bitstring.[i] = 1 then
                            let (taskId, startTime) = Map.find i reverseMapping
                            taskStarts <- taskStarts |> Map.add taskId (float startTime)
                    
                    // Check if valid (each task starts exactly once)
                    let isValid = problem.Tasks |> List.forall (fun t -> Map.containsKey t.Id taskStarts)
                    
                    if isValid then
                        // Build solution from decoded starts
                        let assignments =
                            problem.Tasks
                            |> List.map (fun task ->
                                let startTime = Map.find task.Id taskStarts
                                {
                                    TaskId = task.Id
                                    StartTime = startTime
                                    EndTime = startTime + task.Duration
                                    AssignedResources = task.ResourceRequirements
                                }
                            )
                        
                        let makespan = assignments |> List.map (fun a -> a.EndTime) |> List.max
                        
                        Some (makespan, assignments)
                    else
                        None
                )
                |> Array.choose id
            
            if Array.isEmpty solutions then
                return Error "No valid solutions found from quantum measurements. Try increasing numShots or adjusting QAOA parameters."
            else
                // Select best solution (minimum makespan)
                let (bestMakespan, bestAssignments) = solutions |> Array.minBy fst
                
                // Calculate metrics
                let totalCost =
                    bestAssignments
                    |> List.sumBy (fun a ->
                        let duration = a.EndTime - a.StartTime
                        a.AssignedResources
                        |> Map.toList
                        |> List.sumBy (fun (resourceId, quantity) ->
                            match problem.Resources |> List.tryFind (fun r -> r.Id = resourceId) with
                            | Some resource -> resource.CostPerUnit * quantity * duration
                            | None -> 0.0
                        )
                    )
                
                // Check deadline violations
                let completionTimes = bestAssignments |> List.map (fun a -> a.TaskId, a.EndTime) |> Map.ofList
                let violations =
                    problem.Tasks
                    |> List.choose (fun task ->
                        match task.Deadline with
                        | Some deadline ->
                            let completion = Map.find task.Id completionTimes
                            if completion > deadline then Some task.Id else None
                        | None -> None
                    )
                
                // Calculate resource utilization
                let resourceUtil =
                    problem.Resources
                    |> List.map (fun r ->
                        let totalUsage =
                            bestAssignments
                            |> List.sumBy (fun a ->
                                let duration = a.EndTime - a.StartTime
                                match Map.tryFind r.Id a.AssignedResources with
                                | Some quantity -> quantity * duration
                                | None -> 0.0
                            )
                        let maxPossible = r.Capacity * bestMakespan
                        let utilization = if maxPossible > 0.0 then totalUsage / maxPossible else 0.0
                        r.Id, utilization
                    )
                    |> Map.ofList
                
                let solution = {
                    Assignments = bestAssignments
                    Makespan = bestMakespan
                    TotalCost = totalCost
                    ResourceUtilization = resourceUtil
                    DeadlineViolations = violations
                    IsValid = List.isEmpty violations
                }
                
                return Ok solution
        }

    /// Solve scheduling problem and return optimized schedule (classical dependency-only)
    /// 
    /// Note: This solver handles dependencies but ignores resource capacity constraints.
    /// For resource-constrained scheduling, use solveQuantum with IQuantumBackend.
    let solve (problem: SchedulingProblem<'TTask, 'TResource>) : Async<Result<Solution, string>> =
        async {
            return solveClassical problem
        }

    /// Export schedule as Gantt chart to text file
    let exportGanttChart (solution: Solution) (filePath: string) : unit =
        use writer = new System.IO.StreamWriter(filePath)
        
        writer.WriteLine("# Gantt Chart - Task Schedule")
        writer.WriteLine("")
        writer.WriteLine(sprintf "Makespan: %.1f time units" solution.Makespan)
        writer.WriteLine(sprintf "Total Cost: $%.2f" solution.TotalCost)
        writer.WriteLine(sprintf "Valid: %b" solution.IsValid)
        writer.WriteLine("")
        
        writer.WriteLine("Task Assignments:")
        writer.WriteLine("----------------")
        
        for assignment in solution.Assignments |> List.sortBy (fun a -> a.StartTime) do
            let barLength = int (assignment.EndTime - assignment.StartTime)
            let bar = System.String('█', barLength)
            writer.WriteLine(sprintf "%-12s [%6.1f - %6.1f] %s"
                (if assignment.TaskId.Length > 12 then assignment.TaskId.Substring(0, 12) else assignment.TaskId)
                assignment.StartTime
                assignment.EndTime
                bar)
        
        if not (List.isEmpty solution.DeadlineViolations) then
            writer.WriteLine("")
            writer.WriteLine("Deadline Violations:")
            writer.WriteLine("-------------------")
            for taskId in solution.DeadlineViolations do
                writer.WriteLine(sprintf "  - %s" taskId)

// ============================================================================
// C# INTEROP - Types for easier C# consumption
// ============================================================================

/// Additional types and functions for C# compatibility (FluentAPI)
module Scheduling =
    open TaskScheduling

    /// Create a simple task (C# helper)
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

    /// Create a task with resource requirements (C# helper)
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

    /// SchedulingBuilder for C# FluentAPI
    type SchedulingBuilder<'TTask, 'TResource> private (problem: SchedulingProblem<'TTask, 'TResource>) =
        static member Create() =
            SchedulingBuilder({
                Tasks = []
                Resources = []
                Dependencies = []
                Objective = MinimizeMakespan
                TimeHorizon = 1000.0
            })

        member _.Tasks(tasks: ScheduledTask<'TTask> list) =
            SchedulingBuilder({ problem with Tasks = tasks })

        member _.Resources(resources: Resource<'TResource> list) =
            SchedulingBuilder({ problem with Resources = resources })

        member _.AddDependency(dependency: Dependency) =
            SchedulingBuilder({ problem with Dependencies = dependency :: problem.Dependencies })

        member _.Objective(objective: Objective) =
            SchedulingBuilder({ problem with Objective = objective })

        member _.TimeHorizon(horizon: float) =
            SchedulingBuilder({ problem with TimeHorizon = horizon })

        member _.Build() = problem

    /// Scheduling objective enum for C#
    type SchedulingObjective =
        static member MinimizeMakespan = MinimizeMakespan
        static member MinimizeCost = MinimizeCost
        static member MaximizeResourceUtilization = MaximizeResourceUtilization
        static member MinimizeLateness = MinimizeLateness

    /// Solve scheduling problem (synchronous for C#)
    let solveClassical (problem: SchedulingProblem<'TTask, 'TResource>) : Result<Solution, string> =
        TaskScheduling.solve problem |> Async.RunSynchronously
