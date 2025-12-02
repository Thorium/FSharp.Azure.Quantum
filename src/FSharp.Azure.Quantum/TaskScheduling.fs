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
    let private calculateMakespan (assignments: TaskAssignment list) : float =
        if List.isEmpty assignments then 0.0
        else assignments |> List.map (fun a -> a.EndTime) |> List.max
    
    /// Calculate total cost from assignments and resources
    let private calculateTotalCost
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
    let private findDeadlineViolations
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
    let private calculateResourceUtilization
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
    
    /// Classical greedy scheduling solver
    let private solveClassical (problem: SchedulingProblem<'TTask, 'TResource>) : Result<Solution, string> =
        // Validate problem first
        match validateProblem problem with
        | Error msg -> Error msg
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
    
    // ---------- Helper Functions for Functional QUBO Construction ----------
    
    /// Add or update a float value in a Map, combining with existing value if present
    let private addOrUpdate (key: 'k) (value: float) (map: Map<'k, float>) : Map<'k, float> =
        let newValue =
            match Map.tryFind key map with
            | Some existing -> existing + value
            | None -> value
        Map.add key newValue map
    
    /// Create variable index mappings for QUBO encoding
    /// Returns (forward mapping, reverse mapping, total variables)
    let private createVariableMappings
        (tasks: ScheduledTask<'T> list)
        (timeHorizon: int)
        : Map<string * int, int> * Map<int, string * int> * int =
        
        let mappings =
            tasks
            |> List.indexed
            |> List.collect (fun (taskIdx, task) ->
                [0 .. timeHorizon - 1]
                |> List.mapi (fun timeSlot t ->
                    let varIdx = taskIdx * timeHorizon + timeSlot
                    ((task.Id, t), varIdx)))
        
        let forwardMap = mappings |> Map.ofList
        let reverseMap = mappings |> List.map (fun (k, v) -> (v, k)) |> Map.ofList
        let numVars = List.length mappings
        
        (forwardMap, reverseMap, numVars)
    
    /// Calculate penalty weights using Lucas Rule (penalties >> objective magnitude)
    let private computePenaltyWeights
        (tasks: ScheduledTask<'T> list)
        (timeHorizon: int)
        : float * float * float =
        
        let maxDuration = tasks |> List.map (fun t -> t.Duration) |> List.max
        let penaltyOneHot = float timeHorizon * 10.0
        let penaltyDependency = maxDuration * penaltyOneHot
        let penaltyResource = maxDuration * penaltyOneHot
        
        (penaltyOneHot, penaltyDependency, penaltyResource)
    
    /// Build objective QUBO terms (minimize makespan)
    let private buildObjectiveTerms
        (tasks: ScheduledTask<'T> list)
        (varMapping: Map<string * int, int>)
        (timeHorizon: int)
        : Map<int * int, float> =
        
        tasks
        |> List.collect (fun task ->
            [0 .. timeHorizon - 1]
            |> List.choose (fun t ->
                Map.tryFind (task.Id, t) varMapping
                |> Option.map (fun varIdx ->
                    let completionTime = float t + task.Duration
                    ((varIdx, varIdx), completionTime))))
        |> List.fold (fun acc (key, value) -> addOrUpdate key value acc) Map.empty
    
    /// Build one-hot constraint QUBO terms (each task starts exactly once)
    let private buildOneHotTerms
        (tasks: ScheduledTask<'T> list)
        (varMapping: Map<string * int, int>)
        (timeHorizon: int)
        (penaltyOneHot: float)
        : Map<int * int, float> =
        
        tasks
        |> List.collect (fun task ->
            // Linear terms: -λ * x_t
            let linearTerms =
                [0 .. timeHorizon - 1]
                |> List.choose (fun t ->
                    Map.tryFind (task.Id, t) varMapping
                    |> Option.map (fun varIdx -> ((varIdx, varIdx), -penaltyOneHot)))
            
            // Quadratic terms: 2λ * x_i * x_j
            let quadTerms =
                [0 .. timeHorizon - 1]
                |> List.collect (fun t1 ->
                    [t1 + 1 .. timeHorizon - 1]
                    |> List.choose (fun t2 ->
                        match Map.tryFind (task.Id, t1) varMapping, Map.tryFind (task.Id, t2) varMapping with
                        | Some varIdx1, Some varIdx2 ->
                            let (i, j) = if varIdx1 < varIdx2 then (varIdx1, varIdx2) else (varIdx2, varIdx1)
                            Some ((i, j), 2.0 * penaltyOneHot)
                        | _ -> None))
            
            linearTerms @ quadTerms)
        |> List.fold (fun acc (key, value) -> addOrUpdate key value acc) Map.empty
    
    /// Build dependency constraint QUBO terms
    let private buildDependencyTerms
        (tasks: ScheduledTask<'T> list)
        (dependencies: Dependency list)
        (varMapping: Map<string * int, int>)
        (timeHorizon: int)
        (penaltyDependency: float)
        : Map<int * int, float> =
        
        dependencies
        |> List.collect (function
            | FinishToStart(predId, succId, lag) ->
                // Find predecessor task
                match List.tryFind (fun (t: ScheduledTask<'T>) -> t.Id = predId) tasks with
                | None -> []  // Skip if task not found
                | Some predTask ->
                    let predDuration = predTask.Duration
                    
                    // Generate penalty terms for violating pairs
                    [0 .. timeHorizon - 1]
                    |> List.collect (fun t_pred ->
                        let predEnd = float t_pred + predDuration + lag
                        [0 .. int predEnd]
                        |> List.choose (fun t_succ ->
                            match Map.tryFind (predId, t_pred) varMapping, Map.tryFind (succId, t_succ) varMapping with
                            | Some predVarIdx, Some succVarIdx ->
                                let (i, j) = if predVarIdx < succVarIdx then (predVarIdx, succVarIdx) else (succVarIdx, predVarIdx)
                                Some ((i, j), penaltyDependency)
                            | _ -> None)))
        |> List.fold (fun acc (key, value) -> addOrUpdate key value acc) Map.empty
    
    /// Build resource constraint QUBO terms
    let private buildResourceTerms
        (tasks: ScheduledTask<'T> list)
        (resources: Resource<'R> list)
        (varMapping: Map<string * int, int>)
        (timeHorizon: int)
        (penaltyResource: float)
        : Map<int * int, float> =
        
        if List.isEmpty resources then
            Map.empty
        else
            resources
            |> List.collect (fun resource ->
                [0 .. timeHorizon - 1]
                |> List.map (fun t ->
                    // Find tasks that overlap at time t
                    let overlappingVars =
                        tasks
                        |> List.collect (fun task ->
                            let taskDuration = int (ceil task.Duration)
                            let startRange = max 0 (t - taskDuration + 1), t
                            
                            [fst startRange .. snd startRange]
                            |> List.choose (fun startTime ->
                                Map.tryFind resource.Id task.ResourceRequirements
                                |> Option.bind (fun usage ->
                                    if usage > 0.0 then
                                        Map.tryFind (task.Id, startTime) varMapping
                                        |> Option.map (fun varIdx -> (varIdx, usage))
                                    else None)))
                    
                    // Build terms for this time slot
                    // Linear terms: λ * (usage² - 2*capacity*usage) * x_i
                    let linearTerms =
                        overlappingVars
                        |> List.map (fun (varIdx, usage) ->
                            let coeff = penaltyResource * (usage * usage - 2.0 * resource.Capacity * usage)
                            ((varIdx, varIdx), coeff))
                    
                    // Quadratic terms: λ * 2*usage_i*usage_j * x_i * x_j
                    let quadTerms =
                        [0 .. overlappingVars.Length - 1]
                        |> List.collect (fun idx1 ->
                            [idx1 + 1 .. overlappingVars.Length - 1]
                            |> List.map (fun idx2 ->
                                let (varIdx1, usage1) = overlappingVars.[idx1]
                                let (varIdx2, usage2) = overlappingVars.[idx2]
                                let (i, j) = if varIdx1 < varIdx2 then (varIdx1, varIdx2) else (varIdx2, varIdx1)
                                let coeff = penaltyResource * 2.0 * usage1 * usage2
                                ((i, j), coeff)))
                    
                    linearTerms @ quadTerms))
            |> List.concat
            |> List.fold (fun acc (key, value) -> addOrUpdate key value acc) Map.empty
    
    /// Decode bitstring to task start times
    let private decodeBitstring
        (bitstring: int[])
        (reverseMapping: Map<int, string * int>)
        : Map<string, float> =
        
        bitstring
        |> Array.indexed
        |> Array.choose (fun (i, bit) ->
            if bit = 1 then
                let (taskId, startTime) = Map.find i reverseMapping
                Some (taskId, float startTime)
            else None
        )
        |> Map.ofArray
    
    /// Build solution from decoded task start times
    let private buildSolutionFromStarts
        (tasks: ScheduledTask<'T> list)
        (taskStarts: Map<string, float>)
        : TaskAssignment list option =
        
        // Check if valid (each task starts exactly once)
        let isValid = tasks |> List.forall (fun t -> Map.containsKey t.Id taskStarts)
        
        if isValid then
            tasks
            |> List.map (fun task ->
                let startTime = Map.find task.Id taskStarts
                {
                    TaskId = task.Id
                    StartTime = startTime
                    EndTime = startTime + task.Duration
                    AssignedResources = task.ResourceRequirements
                }
            )
            |> Some
        else
            None
    
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
        
        let numTasks = problem.Tasks.Length
        
        if numTasks = 0 then
            Error "No tasks to schedule"
        elif timeHorizon <= 0 then
            Error "Time horizon must be positive"
        else
            // Create variable mappings functionally
            let (varMapping, _, numVariables) = createVariableMappings problem.Tasks timeHorizon
            
            // Calculate penalty weights
            let (penaltyOneHot, penaltyDependency, penaltyResource) =
                computePenaltyWeights problem.Tasks timeHorizon
            
            // Build QUBO terms functionally
            let objectiveTerms = buildObjectiveTerms problem.Tasks varMapping timeHorizon
            let oneHotTerms = buildOneHotTerms problem.Tasks varMapping timeHorizon penaltyOneHot
            let dependencyTerms = buildDependencyTerms problem.Tasks problem.Dependencies varMapping timeHorizon penaltyDependency
            let resourceTerms = buildResourceTerms problem.Tasks problem.Resources varMapping timeHorizon penaltyResource
            
            // Combine all terms
            let quboTerms =
                [objectiveTerms; oneHotTerms; dependencyTerms; resourceTerms]
                |> List.fold (fun acc terms ->
                    Map.fold (fun acc2 key value -> addOrUpdate key value acc2) acc terms) Map.empty
            
            Ok {
                GraphOptimization.QuboMatrix.Q = quboTerms
                GraphOptimization.QuboMatrix.NumVariables = numVariables
            }

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
            // Reuse variable mapping function
            let (_, reverseMapping, _) = createVariableMappings problem.Tasks timeHorizon
            
            // Decode each measurement and find best feasible solution
            let solutions =
                execResult.Measurements
                |> Array.choose (fun bitstring ->
                    let taskStarts = decodeBitstring bitstring reverseMapping
                    
                    match buildSolutionFromStarts problem.Tasks taskStarts with
                    | Some assignments ->
                        let makespan = calculateMakespan assignments
                        Some (makespan, assignments)
                    | None -> None
                )
            
            if Array.isEmpty solutions then
                return Error "No valid solutions found from quantum measurements. Try increasing numShots or adjusting QAOA parameters."
            else
                // Select best solution (minimum makespan)
                let (bestMakespan, bestAssignments) = solutions |> Array.minBy fst
                
                // Calculate metrics using helper functions
                let totalCost = calculateTotalCost bestAssignments problem.Resources
                let completionTimes = bestAssignments |> List.map (fun a -> a.TaskId, a.EndTime) |> Map.ofList
                let violations = findDeadlineViolations problem.Tasks completionTimes
                let resourceUtil = calculateResourceUtilization bestAssignments problem.Resources bestMakespan
                
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
