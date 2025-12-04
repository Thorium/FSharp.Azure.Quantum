namespace FSharp.Azure.Quantum.TaskScheduling

open Types

/// Computation expression builders for task scheduling
module Builders =

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
        
        member _.YieldFrom(task: ScheduledTask<'T>) : ScheduledTask<'T> =
            task
        
        member _.Zero() : ScheduledTask<'T> =
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
        
        member _.Combine(task1: ScheduledTask<'T>, task2: ScheduledTask<'T>) : ScheduledTask<'T> =
            // For tasks, combine by taking non-default values from task2
            {
                Id = if System.String.IsNullOrEmpty task2.Id then task1.Id else task2.Id
                Value = task2.Value
                Duration = if task2.Duration = 0.0 then task1.Duration else task2.Duration
                EarliestStart = match task2.EarliestStart with | Some _ -> task2.EarliestStart | None -> task1.EarliestStart
                Deadline = match task2.Deadline with | Some _ -> task2.Deadline | None -> task1.Deadline
                ResourceRequirements = Map.fold (fun acc k v -> Map.add k v acc) task1.ResourceRequirements task2.ResourceRequirements
                Priority = if task2.Priority = 0.0 then task1.Priority else task2.Priority
                Properties = Map.fold (fun acc k v -> Map.add k v acc) task1.Properties task2.Properties
            }
        
        member inline _.Delay([<InlineIfLambda>] f: unit -> ScheduledTask<'T>) : ScheduledTask<'T> = f()
        
        member inline this.For(task: ScheduledTask<'T>, [<InlineIfLambda>] f: unit -> ScheduledTask<'T>) : ScheduledTask<'T> =
            this.Combine(task, f())
        
        member this.For(sequence: seq<'U>, body: 'U -> ScheduledTask<'T>) : ScheduledTask<'T> =
            let mutable state = this.Zero()
            for item in sequence do
                state <- this.Combine(state, body item)
            state

        /// Set task identifier (required)
        [<CustomOperation("taskId")>]
        member _.TaskId(task: ScheduledTask<'T>, id: string) =
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
        
        member _.YieldFrom(resource: Resource<'T>) : Resource<'T> =
            resource
        
        member _.Zero() : Resource<'T> =
            {
                Id = ""
                Value = Unchecked.defaultof<'T>
                Capacity = 0.0
                AvailableWindows = [(0.0, System.Double.MaxValue)]
                CostPerUnit = 0.0
                Properties = Map.empty
            }
        
        member _.Combine(res1: Resource<'T>, res2: Resource<'T>) : Resource<'T> =
            // For resources, combine by taking non-default values from res2
            {
                Id = if System.String.IsNullOrEmpty res2.Id then res1.Id else res2.Id
                Value = res2.Value
                Capacity = if res2.Capacity = 0.0 then res1.Capacity else res2.Capacity
                AvailableWindows = if res2.AvailableWindows = [(0.0, System.Double.MaxValue)] then res1.AvailableWindows else res2.AvailableWindows
                CostPerUnit = if res2.CostPerUnit = 0.0 then res1.CostPerUnit else res2.CostPerUnit
                Properties = Map.fold (fun acc k v -> Map.add k v acc) res1.Properties res2.Properties
            }
        
        member inline _.Delay([<InlineIfLambda>] f: unit -> Resource<'T>) : Resource<'T> = f()
        
        member inline this.For(resource: Resource<'T>, [<InlineIfLambda>] f: unit -> Resource<'T>) : Resource<'T> =
            this.Combine(resource, f())
        
        member this.For(sequence: seq<'U>, body: 'U -> Resource<'T>) : Resource<'T> =
            let mutable state = this.Zero()
            for item in sequence do
                state <- this.Combine(state, body item)
            state

        /// Set resource identifier (required)
        [<CustomOperation("resourceId")>]
        member _.ResourceId(resource: Resource<'T>, id: string) =
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
        
        member _.YieldFrom(problem: SchedulingProblem<'TTask, 'TResource>) : SchedulingProblem<'TTask, 'TResource> =
            problem
        
        member _.Zero() : SchedulingProblem<'TTask, 'TResource> =
            {
                Tasks = []
                Resources = []
                Dependencies = []
                Objective = MinimizeMakespan
                TimeHorizon = 1000.0
            }
        
        member _.Combine(prob1: SchedulingProblem<'TTask, 'TResource>, prob2: SchedulingProblem<'TTask, 'TResource>) : SchedulingProblem<'TTask, 'TResource> =
            {
                Tasks = prob1.Tasks @ prob2.Tasks
                Resources = prob1.Resources @ prob2.Resources
                Dependencies = prob1.Dependencies @ prob2.Dependencies
                Objective = prob2.Objective  // Take second if set
                TimeHorizon = if prob2.TimeHorizon = 1000.0 then prob1.TimeHorizon else prob2.TimeHorizon
            }
        
        member inline _.Delay([<InlineIfLambda>] f: unit -> SchedulingProblem<'TTask, 'TResource>) : SchedulingProblem<'TTask, 'TResource> = f()
        
        member inline this.For(problem: SchedulingProblem<'TTask, 'TResource>, [<InlineIfLambda>] f: unit -> SchedulingProblem<'TTask, 'TResource>) : SchedulingProblem<'TTask, 'TResource> =
            this.Combine(problem, f())
        
        member this.For(sequence: seq<'U>, body: 'U -> SchedulingProblem<'TTask, 'TResource>) : SchedulingProblem<'TTask, 'TResource> =
            let mutable state = this.Zero()
            for item in sequence do
                state <- this.Combine(state, body item)
            state

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
