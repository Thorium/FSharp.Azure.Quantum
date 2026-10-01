namespace FSharp.Azure.Quantum.TaskScheduling

open System.Threading
open System.Threading.Tasks
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
    ///    (see QuboEncoding.toQubo; its objective is the sum of completion times)
    /// 2. Runs QAOA as the configuration asks: config.NumLayers layers, angles from
    ///    Nelder-Mead (config.EnableOptimization) or a grid search, config.FinalShots samples
    /// 3. Decodes strictly: a sample is valid only if every task has exactly one start
    ///    slot set and the schedule respects dependencies, resource capacities,
    ///    EarliestStart and resource availability windows
    /// 4. Returns the best valid sample for the objective (minimum makespan, or least
    ///    lateness for MinimizeLateness), ties broken by task Priority
    /// 5. Only when no sample is valid, and config.EnableConstraintRepair allows it, falls
    ///    back to the one-hot repair decode (forbidden start bits cleared, a task with
    ///    several start bits takes its earliest set slot) under the same feasibility
    ///    checks; the solution then has WasRepaired = true
    ///
    /// Solution.Sampling reports the final samples: Valid = valid samples in the sense of
    /// step 3, Hits = samples that are the returned schedule (0 when it was repaired).
    ///
    /// Use this when:
    /// - Tasks have resource requirements (workers, machines, budget)
    /// - Resources have limited capacity
    /// - Need optimal allocation under constraints
    ///
    /// Example:
    ///   let backend = LocalBackend.LocalBackend() :> BackendAbstraction.IQuantumBackend
    ///   let config = { QaoaExecutionHelpers.defaultConfig with NumLayers = 3; FinalShots = 2000 }
    ///   let! result = solveWithConfigAsync backend problem config CancellationToken.None
    let solveWithConfigAsync
        (backend: BackendAbstraction.IQuantumBackend)
        (problem: SchedulingProblem<'TTask, 'TResource>)
        (config: QaoaExecutionHelpers.QaoaSolverConfig)
        (cancellationToken: CancellationToken)
        : Task<QuantumResult<Solution>> =
        task {
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
                        let quboArray = QaoaExecutionHelpers.quboMapToArray quboMatrix

                        // Angles chosen and final samples drawn through the shared solver pipeline,
                        // as the configuration asks (layers, optimisation, shots)
                        let! execution =
                            QaoaExecutionHelpers.runQaoaSampledAsync backend quboArray config cancellationToken

                        match execution with
                        | Error err -> return Error err
                        | Ok run ->

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

                            // Only fully feasible schedules (precedence, resource capacity, earliest
                            // starts and availability windows) may be returned: the minimum-makespan
                            // selection must not pick a schedule that breaks a constraint the user
                            // specified. A task without a start yields no schedule.
                            let feasibleSchedule (taskStarts: Map<string, float>) =
                                match QuboEncoding.buildSolutionFromStarts problem.Tasks taskStarts slotMinutes with
                                | Some assignments when
                                    respectsDependencies assignments
                                    && respectsResources assignments
                                    && respectsStartRestrictions assignments
                                    ->
                                    Some(ScheduleMetrics.calculateMakespan assignments, assignments)
                                | _ -> None

                            // Strict decode: the sample exactly as measured. decodeBitstring gives a
                            // start only to a task with exactly one start bit set, so a sample is valid
                            // iff it is one-hot per task and the schedule it spells is feasible.
                            let strictSchedule (measured: int[]) =
                                feasibleSchedule (QuboEncoding.decodeBitstring measured reverseMapping)

                            // Repair decode: forbidden start bits are cleared, then a task with several
                            // set start bits takes its earliest set slot; a task with none has no start.
                            let repairedSchedule (measured: int[]) =
                                let bitstring =
                                    measured
                                    |> Array.mapi (fun i bit -> if forbiddenVars.Contains i then 0 else bit)

                                feasibleSchedule (QuboEncoding.decodeBitstringWithRepair bitstring reverseMapping)

                            // Each sample that decodes, paired with its schedule
                            let decodeAll
                                (decode: int[] -> (System.TimeSpan * TaskAssignment list) option)
                                (samples: int[][])
                                =
                                samples
                                |> Array.choose (fun bits ->
                                    decode bits |> Option.map (fun schedule -> (bits, schedule)))

                            let distinctSamples = run.Samples |> Array.distinct
                            let strictSchedules = decodeAll strictSchedule distinctSamples

                            // The result is chosen among the strictly valid samples. The repair decode
                            // is used only when there is none, and the solution then says so.
                            let candidates, wasRepaired =
                                if not (Array.isEmpty strictSchedules) then
                                    (strictSchedules, false)
                                elif config.EnableConstraintRepair then
                                    (decodeAll repairedSchedule distinctSamples, true)
                                else
                                    ([||], false)

                            if Array.isEmpty candidates then
                                let repairNote =
                                    if config.EnableConstraintRepair then
                                        ", and the one-hot repair decode gives no feasible schedule either"
                                    else
                                        " (the one-hot repair decode is off: EnableConstraintRepair = false)"

                                return
                                    Error(
                                        QuantumError.OperationError(
                                            "Quantum scheduling",
                                            $"No valid solutions found from quantum measurements: none of the {run.Samples.Length} final samples has exactly one start slot per task and satisfies every constraint{repairNote}. Try more FinalShots or NumLayers."
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

                                let (bestBits, (bestMakespan, bestAssignments)) =
                                    match problem.Objective with
                                    | MinimizeLateness ->
                                        // Least total lateness first; makespan, then priority, breaks ties.
                                        candidates
                                        |> Array.minBy (fun (_, (makespan, assignments)) ->
                                            (totalLatenessMinutes assignments,
                                             makespan,
                                             priorityWeightedEnd assignments))
                                    | MinimizeMakespan
                                    | MinimizeCost
                                    | MaximizeResourceUtilization ->
                                        candidates
                                        |> Array.minBy (fun (_, (makespan, assignments)) ->
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

                                // Valid: samples that are feasible schedules as measured. Hits: samples
                                // that are the returned schedule; none when the repair decode built it.
                                let strictlyValid =
                                    System.Collections.Generic.HashSet<int[]>(
                                        strictSchedules |> Array.map fst,
                                        HashIdentity.Structural
                                    )

                                let sampling =
                                    QaoaExecutionHelpers.sampleStatistics
                                        quboMatrix.NumVariables
                                        (fun sample -> strictlyValid.Contains sample)
                                        (fun sample -> not wasRepaired && sample = bestBits)
                                        run.Samples

                                let solution =
                                    {
                                        Assignments = bestAssignments
                                        Makespan = bestMakespan
                                        TotalCost = totalCost
                                        ResourceUtilization = resourceUtil
                                        DeadlineViolations = violations
                                        IsValid = List.isEmpty violations
                                        WasRepaired = wasRepaired
                                        Sampling = Some sampling
                                    }

                                return Ok solution
        }

    /// solveWithConfigAsync with the shared default QAOA configuration
    /// (QaoaExecutionHelpers.defaultConfig: 2 layers, Nelder-Mead angle optimisation,
    /// 1000 final shots, repair decode allowed).
    ///
    /// Example:
    ///   let backend = LocalBackend.LocalBackend() :> BackendAbstraction.IQuantumBackend
    ///   let! result = solveAsync backend problem CancellationToken.None
    let solveAsync
        (backend: BackendAbstraction.IQuantumBackend)
        (problem: SchedulingProblem<'TTask, 'TResource>)
        (cancellationToken: CancellationToken)
        : Task<QuantumResult<Solution>> =
        solveWithConfigAsync backend problem QaoaExecutionHelpers.defaultConfig cancellationToken
