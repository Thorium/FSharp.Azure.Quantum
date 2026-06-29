namespace FSharp.Azure.Quantum.TaskScheduling

open Types

/// Pure scoring/metric helpers for a decoded schedule.
///
/// These are evaluation utilities — they compute makespan, cost, deadline violations and
/// resource utilisation from an ALREADY-PRODUCED set of task assignments. They do no solving
/// or optimisation, so both the classical greedy solver and the quantum (QAOA) solver share
/// them. Keeping them in a neutral module (rather than inside ClassicalSolver) makes clear that
/// the quantum path performs genuine quantum optimisation and only reuses these scorers.
module ScheduleMetrics =

    /// Calculate makespan (latest end time) from assignments.
    let calculateMakespan (assignments: TaskAssignment list) : float =
        if List.isEmpty assignments then 0.0
        else assignments |> List.map (fun a -> a.EndTime) |> List.max

    /// Calculate total cost from assignments and resources.
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

    /// Find tasks that violate their deadlines.
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

    /// Calculate resource utilization across all resources.
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
