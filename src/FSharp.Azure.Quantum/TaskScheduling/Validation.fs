namespace FSharp.Azure.Quantum.TaskScheduling

open FSharp.Azure.Quantum.Core

open Types

/// Validation logic for scheduling problems
module Validation =

    /// Tolerance, in minutes, for comparing start and end times with window bounds
    [<Literal>]
    let private windowTolerance = 1e-6

    /// The resources a task needs (positive requirement) that exist in the problem
    let internal requiredResources (resources: Resource<'R> list) (task: ScheduledTask<'T>) : Resource<'R> list =
        resources
        |> List.filter (fun r ->
            match Map.tryFind r.Id task.ResourceRequirements with
            | Some quantity -> quantity > 0.0
            | None -> false)

    /// Whether the task may start at `startMinutes` (offset from schedule start):
    /// no earlier than its EarliestStart, and, for every resource it requires, running
    /// entirely inside one of that resource's AvailableWindows (minutes, bounds inclusive).
    let startIsAllowed (resources: Resource<'R> list) (task: ScheduledTask<'T>) (startMinutes: float) : bool =
        let endMinutes = startMinutes + task.Duration.TotalMinutes

        let afterEarliest =
            match task.EarliestStart with
            | Some earliest -> startMinutes >= earliest.TotalMinutes - windowTolerance
            | None -> true

        afterEarliest
        && requiredResources resources task
           |> List.forall (fun r ->
               r.AvailableWindows
               |> List.exists (fun (windowStart, windowEnd) ->
                   startMinutes >= windowStart - windowTolerance
                   && endMinutes <= windowEnd + windowTolerance))

    /// The earliest allowed start (see startIsAllowed) at or after `notBeforeMinutes`, if any.
    /// It is either the lower bound (the later of `notBeforeMinutes` and EarliestStart) or the
    /// opening of one of the required resources' windows, so only those candidates are tested.
    let earliestAllowedStart
        (resources: Resource<'R> list)
        (task: ScheduledTask<'T>)
        (notBeforeMinutes: float)
        : float option =
        let lowerBound =
            match task.EarliestStart with
            | Some earliest -> max notBeforeMinutes earliest.TotalMinutes
            | None -> notBeforeMinutes

        let windowOpenings =
            requiredResources resources task
            |> List.collect (fun r -> r.AvailableWindows |> List.map fst)
            |> List.filter (fun opening -> opening > lowerBound)

        lowerBound :: windowOpenings
        |> List.distinct
        |> List.sort
        |> List.tryFind (startIsAllowed resources task)

    /// Validate scheduling problem before solving
    let validateProblem (problem: SchedulingProblem<'TTask, 'TResource>) : QuantumResult<unit> =
        // Check all tasks have non-empty IDs
        let emptyIds =
            problem.Tasks |> List.filter (fun t -> System.String.IsNullOrWhiteSpace(t.Id))

        if not (List.isEmpty emptyIds) then
            Error(QuantumError.ValidationError("TaskIds", "All tasks must have non-empty unique IDs"))
        else

            // Check all tasks have unique IDs
            let duplicates =
                problem.Tasks
                |> List.groupBy (fun t -> t.Id)
                |> List.filter (fun (_, tasks) -> List.length tasks > 1)
                |> List.map fst

            if not (List.isEmpty duplicates) then
                Error(QuantumError.ValidationError("TaskIds", $"Duplicate task IDs found: %A{duplicates}"))
            else

                // Check all dependencies reference existing tasks
                let taskIds = problem.Tasks |> List.map (fun t -> t.Id) |> Set.ofList

                let invalidDeps =
                    problem.Dependencies
                    |> List.filter (fun dep ->
                        match dep with
                        | FinishToStart(predId, succId, _) ->
                            not ((Set.contains predId taskIds) && (Set.contains succId taskIds)))

                if not (List.isEmpty invalidDeps) then
                    Error(
                        QuantumError.ValidationError(
                            "Dependencies",
                            $"Invalid task dependencies reference non-existent tasks: %A{invalidDeps}"
                        )
                    )
                else
                    Ok()
