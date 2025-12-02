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
