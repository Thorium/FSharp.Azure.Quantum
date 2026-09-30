namespace FSharp.Azure.Quantum.TaskScheduling

open FSharp.Azure.Quantum
open FSharp.Azure.Quantum.Backends
open FSharp.Azure.Quantum.Core
open Types

/// Quantum solver for resource-constrained scheduling
module QuantumSolver =

    /// Tasks in the longest chain of finish-to-start dependencies (1 when none).
    let internal longestChain (tasks: ScheduledTask<'T> list) (dependencies: Dependency list) : int =
        let preds =
            dependencies
            |> List.map (fun (FinishToStart(pred, succ, _)) -> (succ, pred))
            |> List.groupBy fst
            |> List.map (fun (succ, ps) -> (succ, ps |> List.map snd))
            |> Map.ofList

        let memo = System.Collections.Generic.Dictionary<string, int>()

        // `seen` guards against cycles (validation does not reject them), so a
        // cyclic input still terminates here and ends in "no valid solutions".
        let rec depth (seen: Set<string>) (id: string) =
            match memo.TryGetValue id with
            | true, d -> d
            | _ ->
                let d =
                    1
                    + (preds.TryFind id
                       |> Option.defaultValue []
                       |> List.filter (seen.Contains >> not)
                       |> List.map (depth (seen.Add id))
                       |> List.fold max 0)

                memo.[id] <- d
                d

        tasks |> List.map (fun t -> depth Set.empty t.Id) |> List.fold max 1

    /// Solve scheduling problem with resource constraints using quantum backend
    ///
    /// RULE 1 COMPLIANCE:
    /// ✅ Requires IQuantumBackend parameter (explicit quantum execution)
    ///
    /// Resource-constrained scheduling is solved via quantum optimization:
    /// 1. Encodes tasks, dependencies, and resource limits as QUBO problem
    /// 2. Uses QAOA or quantum annealing to find optimal schedule
    /// 3. Respects resource capacity constraints (unlike classical solver)
    /// 4. Respects EarliestStart and resource availability windows: slots that break them
    ///    are penalised in the QUBO, cleared before decoding, and re-checked on the result
    /// 5. Breaks ties between equally good schedules by task Priority
    ///
    /// Use this when:
    /// - Tasks have resource requirements (workers, machines, budget)
    /// - Resources have limited capacity
    /// - Need optimal allocation under constraints
    ///
    /// Example:
    ///   let backend = LocalBackend.LocalBackend() :> BackendAbstraction.IQuantumBackend
    ///   let! result = solveQuantum backend problem
    let solveAsync
        (backend: BackendAbstraction.IQuantumBackend)
        (problem: SchedulingProblem<'TTask, 'TResource>)
        : Async<QuantumResult<Solution>> =
        async {
            // Validate problem first
            match Validation.validateProblem problem with
            | Error err -> return Error err
            | Ok() ->

                // Discretise the real-time problem into a BOUNDED grid of time slots. Real durations
                // (minutes/hours/days) are mapped onto integer slots via slotMinutes, so the QUBO never
                // conflates real time with slot indices, and the qubit count (numTasks × timeHorizon)
                // stays within the local simulator's reach.
                let numTasks = problem.Tasks.Length
                let totalWorkMin = problem.Tasks |> List.sumBy (fun t -> t.Duration.TotalMinutes)

                let horizonMin =
                    let h = problem.TimeHorizon.TotalMinutes
                    if h > 0.0 then max h totalWorkMin else totalWorkMin

                let minDurMin =
                    problem.Tasks
                    |> List.choose (fun t ->
                        if t.Duration.TotalMinutes > 0.0 then
                            Some t.Duration.TotalMinutes
                        else
                            None)
                    |> function
                        | [] -> 1.0
                        | xs -> List.min xs
                // Cap slots so numTasks × timeHorizon stays modest (~18 qubits)...
                let maxSlots = max 2 (min 10 (18 / max 1 numTasks))

                // ...but never below the longest dependency chain: k tasks that
                // must run one after another need k slots, and with fewer no
                // bitstring is a valid schedule; sampling would only end in
                // "no valid solutions".
                let chain = longestChain problem.Tasks problem.Dependencies

                let timeHorizon =
                    max chain (max 2 (min maxSlots (int (ceil (horizonMin / minDurMin)))))

                let slotMinutes =
                    if timeHorizon > 0 then
                        horizonMin / float timeHorizon
                    else
                        1.0

                let neededQubits = numTasks * timeHorizon

                // The budget is what FINISHES, not what fits: widening the grid for a long
                // chain must not turn a quick refusal into a multi-gigabyte, minutes-long
                // simulation. getRunnableQubits takes capacity and wall-clock together, so
                // the backend declares its own limits instead of this module type-testing
                // for the local simulator.
                let capacity = BackendAbstraction.UnifiedBackend.getRunnableQubits backend

                // Start slots ruled out by EarliestStart or resource availability windows
                let forbiddenVars =
                    QuboEncoding.forbiddenStartVariables problem timeHorizon slotMinutes

                let (varMapping, reverseMapping, _) =
                    QuboEncoding.createVariableMappings problem.Tasks timeHorizon

                let unplaceable =
                    problem.Tasks
                    |> List.filter (fun task ->
                        [ 0 .. timeHorizon - 1 ]
                        |> List.forall (fun t -> forbiddenVars.Contains varMapping.[(task.Id, t)]))
                    |> List.map (fun task -> task.Id)

                match capacity with
                | _ when not (List.isEmpty unplaceable) ->
                    return
                        Error(
                            QuantumError.ValidationError(
                                "AvailableWindows",
                                $"""no start slot of the {timeHorizon}-slot grid ({slotMinutes:F1} min per slot) satisfies earliestStart and the resource availability windows for task(s) {String.concat ", " unplaceable}: slots start every {slotMinutes:F1} min from 0, and none falls where the task may start. Align the windows or earliestStart with the slot starts, or schedule with ClassicalSolver.solve, which starts tasks at any minute"""
                            )
                        )
                | Some maxQubits when neededQubits > maxQubits ->
                    return
                        Error(
                            QuantumError.ValidationError(
                                "qubits",
                                $"the schedule needs {neededQubits} qubits ({numTasks} tasks x {timeHorizon} time slots; the longest dependency chain alone needs {chain} slots) and {backend.Name} can run {maxQubits} (for the local simulator the wall-clock budget, FSAQ_MAX_CIRCUIT_QUBITS): use a larger backend or schedule classically"
                            )
                        )
                | _ ->

                    // Encode problem as QUBO
                    match QuboEncoding.toQubo problem timeHorizon slotMinutes with
                    | Error err -> return Error err
                    | Ok quboMatrix ->

                        // Convert sparse QUBO to dense array for QAOA
                        let quboArray = Array2D.zeroCreate quboMatrix.NumVariables quboMatrix.NumVariables

                        for KeyValue((i, j), value) in quboMatrix.Q do
                            quboArray.[i, j] <- value

                        // p = 1 QAOA at fixed angles through the shared solver pipeline
                        // (normalised cost Hamiltonian, minimisation convention)
                        let gamma, beta = 0.5, 0.5
                        let numShots = 1000
                        let! cancellationToken = Async.CancellationToken

                        let! execution =
                            QaoaExecutionHelpers.executeFromQuboAsync
                                backend
                                quboArray
                                [| (gamma, beta) |]
                                numShots
                                cancellationToken
                            |> Async.AwaitTask

                        match execution with
                        | Error err -> return Error err
                        | Ok measurements ->

                            // Decode each measurement and find best feasible solution
                            // A schedule respects precedence iff every finish-to-start dependency holds:
                            // the successor starts no earlier than the predecessor finishes (+ lag).
                            let respectsDependencies (assignments: TaskAssignment list) =
                                problem.Dependencies
                                |> List.forall (fun dep ->
                                    match dep with
                                    | FinishToStart(predId, succId, lag) ->
                                        match
                                            assignments |> List.tryFind (fun a -> a.TaskId = predId),
                                            assignments |> List.tryFind (fun a -> a.TaskId = succId)
                                        with
                                        | Some pred, Some succ -> succ.StartTime >= pred.EndTime + lag
                                        | _ -> true)

                            // A schedule respects resource limits iff at every moment the combined usage of all
                            // concurrently running tasks stays within each resource's capacity. Usage only changes
                            // when a task starts, so checking at each assignment's start time is sufficient.
                            let respectsResources (assignments: TaskAssignment list) =
                                problem.Resources
                                |> List.forall (fun resource ->
                                    assignments
                                    |> List.forall (fun a ->
                                        let usageAtStart =
                                            assignments
                                            |> List.sumBy (fun b ->
                                                if b.StartTime <= a.StartTime && a.StartTime < b.EndTime then
                                                    b.AssignedResources
                                                    |> Map.tryFind resource.Id
                                                    |> Option.defaultValue 0.0
                                                else
                                                    0.0)

                                        usageAtStart <= resource.Capacity + 1e-9))

                            // Every task starts no earlier than its EarliestStart and runs inside an
                            // availability window of each resource it requires.
                            let respectsStartRestrictions (assignments: TaskAssignment list) =
                                assignments
                                |> List.forall (fun a ->
                                    problem.Tasks
                                    |> List.tryFind (fun t -> t.Id = a.TaskId)
                                    |> Option.forall (fun t ->
                                        Validation.startIsAllowed problem.Resources t a.StartTime.TotalMinutes))

                            let solutions =
                                measurements
                                |> Array.choose (fun measured ->
                                    // Forbidden start bits are cleared before decoding, so the repair
                                    // below picks each task's earliest ALLOWED set slot.
                                    let bitstring =
                                        measured
                                        |> Array.mapi (fun i bit -> if forbiddenVars.Contains i then 0 else bit)

                                    // One-hot REPAIR decode: tasks with multiple set start bits take
                                    // their earliest set slot (QAOA rarely samples exact one-hot
                                    // states, so the strict decode would reject nearly every shot);
                                    // tasks with zero set bits still yield no start, making
                                    // buildSolutionFromStarts return None. Feasibility of repaired
                                    // schedules is enforced by the classical validation below.
                                    let taskStarts = QuboEncoding.decodeBitstringWithRepair bitstring reverseMapping

                                    match
                                        QuboEncoding.buildSolutionFromStarts problem.Tasks taskStarts slotMinutes
                                    with
                                    // Keep only fully feasible measurements (precedence, resource capacity,
                                    // earliest starts and availability windows).
                                    // The QUBO penalties bias QAOA sampling toward these, but the final
                                    // min-makespan selection must not pick a lower-makespan measurement that
                                    // VIOLATES the constraints the user specified — otherwise the returned
                                    // "solution" would silently break dependencies or overload resources.
                                    | Some assignments when
                                        respectsDependencies assignments
                                        && respectsResources assignments
                                        && respectsStartRestrictions assignments
                                        ->
                                        let makespan = ScheduleMetrics.calculateMakespan assignments
                                        Some(makespan, assignments)
                                    | _ -> None)

                            if Array.isEmpty solutions then
                                return
                                    Error(
                                        QuantumError.OperationError(
                                            "Quantum scheduling",
                                            "No valid solutions found from quantum measurements. Try increasing numShots or adjusting QAOA parameters."
                                        )
                                    )
                            else
                                // Select the best feasible solution PER THE DECLARED OBJECTIVE.
                                // MinimizeCost and MaximizeResourceUtilization share the makespan
                                // selection because, in this model, resource assignments always equal
                                // each task's fixed requirements: total cost is identical for every
                                // feasible schedule, and utilisation is maximised by minimising
                                // makespan (see QuboEncoding.toQubo).
                                let totalLatenessMinutes (assignments: TaskAssignment list) =
                                    assignments
                                    |> List.sumBy (fun a ->
                                        problem.Tasks
                                        |> List.tryFind (fun t -> t.Id = a.TaskId)
                                        |> Option.bind (fun t -> t.Deadline)
                                        |> Option.map (fun deadline -> max 0.0 (a.EndTime - deadline).TotalMinutes)
                                        |> Option.defaultValue 0.0)

                                // Priority breaks remaining ties: the smaller Σ priority × end time
                                // wins, so higher-priority tasks finish earlier.
                                let priorityWeightedEnd (assignments: TaskAssignment list) =
                                    assignments
                                    |> List.sumBy (fun a ->
                                        problem.Tasks
                                        |> List.tryFind (fun t -> t.Id = a.TaskId)
                                        |> Option.map (fun t -> t.Priority * a.EndTime.TotalMinutes)
                                        |> Option.defaultValue 0.0)

                                let (bestMakespan, bestAssignments) =
                                    match problem.Objective with
                                    | MinimizeLateness ->
                                        // Least total lateness first; makespan, then priority, breaks ties.
                                        solutions
                                        |> Array.minBy (fun (makespan, assignments) ->
                                            (totalLatenessMinutes assignments,
                                             makespan,
                                             priorityWeightedEnd assignments))
                                    | MinimizeMakespan
                                    | MinimizeCost
                                    | MaximizeResourceUtilization ->
                                        solutions
                                        |> Array.minBy (fun (makespan, assignments) ->
                                            (makespan, priorityWeightedEnd assignments))

                                // Score the quantum-decoded schedule with the shared ScheduleMetrics helpers
                                // (pure metric calculation — no classical solving in the quantum path)
                                let totalCost = ScheduleMetrics.calculateTotalCost bestAssignments problem.Resources

                                let completionTimes =
                                    bestAssignments |> List.map (fun a -> a.TaskId, a.EndTime) |> Map.ofList

                                let violations =
                                    ScheduleMetrics.findDeadlineViolations problem.Tasks completionTimes

                                let resourceUtil =
                                    ScheduleMetrics.calculateResourceUtilization
                                        bestAssignments
                                        problem.Resources
                                        bestMakespan

                                let solution =
                                    {
                                        Assignments = bestAssignments
                                        Makespan = bestMakespan
                                        TotalCost = totalCost
                                        ResourceUtilization = resourceUtil
                                        DeadlineViolations = violations
                                        IsValid = List.isEmpty violations
                                    }

                                return Ok solution
        }
