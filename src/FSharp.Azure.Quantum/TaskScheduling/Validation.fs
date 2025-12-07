namespace FSharp.Azure.Quantum.TaskScheduling
open FSharp.Azure.Quantum.Core

open Types

/// Validation logic for scheduling problems
module Validation =

    /// Validate scheduling problem before solving
    let validateProblem (problem: SchedulingProblem<'TTask, 'TResource>) : QuantumResult<unit> =
        // Check all tasks have non-empty IDs
        let emptyIds = problem.Tasks |> List.filter (fun t -> System.String.IsNullOrWhiteSpace(t.Id))
        if not (List.isEmpty emptyIds) then
            Error (QuantumError.ValidationError ("TaskIds", "All tasks must have non-empty unique IDs"))
        else

        // Check all tasks have unique IDs
        let duplicates =
            problem.Tasks
            |> List.groupBy (fun t -> t.Id)
            |> List.filter (fun (_, tasks) -> List.length tasks > 1)
            |> List.map fst

        if not (List.isEmpty duplicates) then
            Error (QuantumError.ValidationError ("TaskIds", sprintf "Duplicate task IDs found: %A" duplicates))
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
            Error (QuantumError.ValidationError ("Dependencies", sprintf "Invalid task dependencies reference non-existent tasks: %A" invalidDeps))
        else
            Ok ()
