namespace FSharp.Azure.Quantum.TaskScheduling
open System
open FSharp.Azure.Quantum.Core

open FSharp.Azure.Quantum
open Types

/// QUBO encoding for resource-constrained scheduling
module QuboEncoding =

    // ============================================================================
    // HELPER FUNCTIONS - Functional QUBO Construction
    // ============================================================================
    
    /// Add or update a float value in a Map, combining with existing value if present
    /// (Alias for shared Qubo.combineTerms)
    let private addOrUpdate (key: int * int) (value: float) (map: Map<int * int, float>) : Map<int * int, float> =
        Qubo.combineTerms key value map

    /// Number of whole time slots a real-time duration occupies, given the slot size in minutes.
    /// (At least 1 slot.) This is what makes real durations — minutes, hours, days — map onto the
    /// discrete QUBO time grid without conflating real time with slot indices.
    let private durationToSlots (slotMinutes: float) (d: TimeSpan) : int =
        if slotMinutes <= 0.0 then 1
        else max 1 (int (ceil (d.TotalMinutes / slotMinutes)))
    
    /// Create variable index mappings for QUBO encoding
    /// Returns (forward mapping, reverse mapping, total variables)
    let createVariableMappings
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
        (slotMinutes: float)
        : float * float * float =

        let maxDuration = tasks |> List.map (fun t -> float (durationToSlots slotMinutes t.Duration)) |> List.max
        let penaltyOneHot = Qubo.computeLucasPenalties maxDuration timeHorizon
        let penaltyDependency = maxDuration * penaltyOneHot
        let penaltyResource = maxDuration * penaltyOneHot
        
        (penaltyOneHot, penaltyDependency, penaltyResource)
    
    /// Build objective QUBO terms (minimize makespan: sum of completion times)
    let private buildObjectiveTerms
        (tasks: ScheduledTask<'T> list)
        (varMapping: Map<string * int, int>)
        (timeHorizon: int)
        (slotMinutes: float)
        : Map<int * int, float> =

        tasks
        |> List.collect (fun task ->
            [0 .. timeHorizon - 1]
            |> List.choose (fun t ->
                Map.tryFind (task.Id, t) varMapping
                |> Option.map (fun varIdx ->
                    let completionTime = float t + float (durationToSlots slotMinutes task.Duration)
                    ((varIdx, varIdx), completionTime))))
        |> List.fold (fun acc (key, value) -> addOrUpdate key value acc) Map.empty

    /// Build objective QUBO terms for MinimizeLateness: starting a task at slot t
    /// costs max(0, completion - deadline) in slot units; tasks without deadlines
    /// (or on-time start slots) contribute nothing. A small completion-time term
    /// breaks ties toward earlier schedules so the objective still discriminates
    /// when every candidate start is on time.
    let private buildLatenessTerms
        (tasks: ScheduledTask<'T> list)
        (varMapping: Map<string * int, int>)
        (timeHorizon: int)
        (slotMinutes: float)
        : Map<int * int, float> =

        let tieBreakWeight = 0.01
        tasks
        |> List.collect (fun task ->
            [0 .. timeHorizon - 1]
            |> List.choose (fun t ->
                Map.tryFind (task.Id, t) varMapping
                |> Option.map (fun varIdx ->
                    let completionSlots = float t + float (durationToSlots slotMinutes task.Duration)
                    let latenessSlots =
                        match task.Deadline with
                        | Some deadline when slotMinutes > 0.0 ->
                            max 0.0 (completionSlots - deadline.TotalMinutes / slotMinutes)
                        | _ -> 0.0
                    ((varIdx, varIdx), latenessSlots + tieBreakWeight * completionSlots))))
        |> List.fold (fun acc (key, value) -> addOrUpdate key value acc) Map.empty
    
    /// Build one-hot constraint QUBO terms (each task starts exactly once)
    let private buildOneHotTerms
        (tasks: ScheduledTask<'T> list)
        (varMapping: Map<string * int, int>)
        (timeHorizon: int)
        (penaltyOneHot: float)
        : Map<int * int, float> =
        
        tasks
        |> List.map (fun task ->
            // Get variable indices for all possible start times
            let varIndices =
                [0 .. timeHorizon - 1]
                |> List.choose (fun t -> Map.tryFind (task.Id, t) varMapping)
            
            // Use shared one-hot constraint helper
            Qubo.oneHotConstraint varIndices penaltyOneHot)
        |> List.fold (fun acc quboMap ->
            quboMap |> Map.fold (fun acc2 key value -> addOrUpdate key value acc2) acc) Map.empty
    
    /// Build dependency constraint QUBO terms
    let private buildDependencyTerms
        (tasks: ScheduledTask<'T> list)
        (dependencies: Dependency list)
        (varMapping: Map<string * int, int>)
        (timeHorizon: int)
        (slotMinutes: float)
        (penaltyDependency: float)
        : Map<int * int, float> =

        dependencies
        |> List.collect (function
            | FinishToStart(predId, succId, lag) ->
                // Find predecessor task
                match List.tryFind (fun (t: ScheduledTask<'T>) -> t.Id = predId) tasks with
                | None -> []  // Skip if task not found
                | Some predTask ->
                    // Predecessor occupancy and lag expressed in slots.
                    let predDurationSlots = float (durationToSlots slotMinutes predTask.Duration)
                    let lagSlots = if slotMinutes > 0.0 then lag.TotalMinutes / slotMinutes else 0.0

                    // Generate penalty terms for violating pairs
                    [0 .. timeHorizon - 1]
                    |> List.collect (fun t_pred ->
                        let predEnd = float t_pred + predDurationSlots + lagSlots
                        // Finish-to-start is satisfied when t_succ >= predEnd, so the successor may
                        // legally start *at* predEnd; only slots strictly before it are violations.
                        // The largest such integer slot is ceil(predEnd) - 1 — `int predEnd` wrongly
                        // penalised the exactly-feasible boundary slot (which, on the tight 2–10 slot
                        // grids, can leave QAOA with no feasible signal at all).
                        [0 .. int (ceil predEnd) - 1]
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
        (slotMinutes: float)
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
                            let taskDuration = durationToSlots slotMinutes task.Duration
                            let startRange = max 0 (t - taskDuration + 1), t
                            
                            [fst startRange .. snd startRange]
                            |> List.choose (fun startTime ->
                                Map.tryFind resource.Id task.ResourceRequirements
                                |> Option.bind (fun usage ->
                                    if usage > 0.0 then
                                        Map.tryFind (task.Id, startTime) varMapping
                                        |> Option.map (fun varIdx -> (varIdx, usage))
                                    else None)))
                    
                    // Build terms for this time slot.
                    //
                    // The ≤-capacity constraint must be encoded with NON-NEGATIVE penalties only.
                    // The previous encoding expanded the EQUALITY penalty λ(Σuᵢxᵢ − C)², whose
                    // diagonal λ(u² − 2Cu) is strictly negative whenever u < 2C — extra start bits
                    // then LOWER the energy, so the QUBO optimum set every start bit and violated
                    // the one-hot constraint. Instead, penalise only actual overloads:
                    //   - a single assignment whose usage alone exceeds capacity, and
                    //   - each pair of assignments whose combined usage exceeds capacity.
                    // Overloads only detectable at 3+ concurrent tasks (each pair fitting) are not
                    // captured by a quadratic QUBO term; those schedules are rejected by the
                    // classical feasibility validation applied when decoding measurements.

                    // Linear terms: λ * (1 + usage - capacity) * x_i when a task alone overloads
                    let linearTerms =
                        overlappingVars
                        |> List.choose (fun (varIdx, usage) ->
                            if usage > resource.Capacity then
                                let coeff = penaltyResource * (1.0 + usage - resource.Capacity)
                                Some ((varIdx, varIdx), coeff)
                            else None)

                    // Quadratic terms: λ * (1 + usage_i + usage_j - capacity) * x_i * x_j
                    // for each pair that would jointly exceed capacity
                    let quadTerms =
                        [0 .. overlappingVars.Length - 1]
                        |> List.collect (fun idx1 ->
                            [idx1 + 1 .. overlappingVars.Length - 1]
                            |> List.choose (fun idx2 ->
                                let (varIdx1, usage1) = overlappingVars.[idx1]
                                let (varIdx2, usage2) = overlappingVars.[idx2]
                                if usage1 + usage2 > resource.Capacity then
                                    let (i, j) = if varIdx1 < varIdx2 then (varIdx1, varIdx2) else (varIdx2, varIdx1)
                                    let coeff = penaltyResource * (1.0 + usage1 + usage2 - resource.Capacity)
                                    Some ((i, j), coeff)
                                else None))

                    linearTerms @ quadTerms))
            |> List.concat
            |> List.fold (fun acc (key, value) -> addOrUpdate key value acc) Map.empty
    
    /// Decode bitstring to task start times.
    /// A task's start is only returned when EXACTLY ONE of its start bits is set;
    /// tasks with zero or multiple set bits (one-hot violations) are omitted, so
    /// downstream validation (buildSolutionFromStarts) rejects the measurement
    /// instead of silently picking an arbitrary start time.
    let decodeBitstring
        (bitstring: int[])
        (reverseMapping: Map<int, string * int>)
        : Map<string, float> =

        bitstring
        |> Array.indexed
        |> Array.choose (fun (i, bit) ->
            if bit = 1 then
                match Map.tryFind i reverseMapping with
                | Some (taskId, startTime) -> Some (taskId, float startTime)
                | None ->
                    // A set bit outside the variable mapping means the measurement
                    // register and the QUBO encoding have drifted out of sync —
                    // a programming error that must fail loudly, not decode to a
                    // plausible-looking partial schedule.
                    failwith $"decodeBitstring: measurement bit {i} is set but has no QUBO variable mapping (bitstring length {bitstring.Length}, mapping size {reverseMapping.Count})"
            else None
        )
        |> Array.groupBy fst
        |> Array.choose (fun (taskId, starts) ->
            match starts with
            | [| (_, start) |] -> Some (taskId, start)
            | _ -> None)  // one-hot violated: 2+ start bits set for this task
        |> Map.ofArray

    /// Decode bitstring with one-hot REPAIR: a task with multiple set start bits
    /// gets its EARLIEST set slot (deterministic, biased toward low makespan);
    /// a task with zero set bits is still omitted (nothing to repair from).
    ///
    /// Rationale: QAOA at fixed initial parameters rarely samples exact one-hot
    /// states, so the strict decode rejects almost every measurement. Repairing
    /// keeps sampling usable — SAFELY, because the quantum solver re-validates
    /// every repaired schedule classically (dependencies + resource capacity)
    /// before it can be returned.
    let decodeBitstringWithRepair
        (bitstring: int[])
        (reverseMapping: Map<int, string * int>)
        : Map<string, float> =

        bitstring
        |> Array.indexed
        |> Array.choose (fun (i, bit) ->
            if bit = 1 then
                match Map.tryFind i reverseMapping with
                | Some (taskId, startTime) -> Some (taskId, float startTime)
                | None ->
                    failwith $"decodeBitstringWithRepair: measurement bit {i} is set but has no QUBO variable mapping (bitstring length {bitstring.Length}, mapping size {reverseMapping.Count})"
            else None
        )
        |> Array.groupBy fst
        |> Array.map (fun (taskId, starts) ->
            (taskId, starts |> Array.map snd |> Array.min))
        |> Map.ofArray

    /// Build solution from decoded task START SLOTS, mapping each slot back to a real start time
    /// (slot index × slotMinutes) so the returned schedule is in genuine time units.
    let buildSolutionFromStarts
        (tasks: ScheduledTask<'T> list)
        (taskStarts: Map<string, float>)
        (slotMinutes: float)
        : TaskAssignment list option =

        // Check if valid (each task starts exactly once)
        let isValid = tasks |> List.forall (fun t -> Map.containsKey t.Id taskStarts)

        if isValid then
            tasks
            |> List.map (fun task ->
                let startSlot = Map.find task.Id taskStarts
                let startTime = TimeSpan.FromMinutes(startSlot * slotMinutes)
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
    
    // ============================================================================
    // PUBLIC API
    // ============================================================================

    /// Encode resource-constrained scheduling as QUBO problem
    ///
    /// ENCODING SCHEME:
    /// - Variables: x_{task,time} ∈ {0,1} where x_{task,time}=1 means task starts at time
    /// - Time discretized into slots (0, 1, 2, ..., T-1)
    /// - Each task must start at exactly one time slot
    ///
    /// OBJECTIVE (per problem.Objective — the declared objective IS honoured):
    /// - MinimizeMakespan: Σ_{task,time} completion(task,time) * x_{task,time}
    /// - MinimizeLateness: Σ_{task,time} max(0, completion - deadline) * x_{task,time}
    /// - MinimizeCost: in this encoding every task is always assigned exactly its
    ///   ResourceRequirements for its fixed Duration, so total resource cost is
    ///   IDENTICAL for every feasible schedule — every feasible schedule is
    ///   cost-optimal. The completion-time objective is used purely to bias the
    ///   search toward compact feasible schedules.
    /// - MaximizeResourceUtilization: total usage is likewise schedule-invariant,
    ///   so utilisation = usage / (capacity × makespan) is maximised exactly by
    ///   minimising makespan; encoded as the makespan objective.
    ///
    /// CONSTRAINTS (encoded as penalties):
    ///   1. One-hot: Each task starts exactly once: Σ_time x_{task,time} = 1
    ///   2. Dependencies: Successor starts after predecessor finishes
    ///   3. Resources: At any time t, Σ_{overlapping tasks} resource_usage ≤ capacity
    /// 
    /// QUBO FORM (minimization for QAOA):
    ///   H = Objective + λ₁*Penalty₁ + λ₂*Penalty₂ + λ₃*Penalty₃
    let toQubo
        (problem: SchedulingProblem<'TTask, 'TResource>)
        (timeHorizon: int)
        (slotMinutes: float)
        : QuantumResult<GraphOptimization.QuboMatrix> =

        let numTasks = problem.Tasks.Length

        if numTasks = 0 then
            Error (QuantumError.ValidationError ("Tasks", "No tasks to schedule"))
        elif timeHorizon <= 0 then
            Error (QuantumError.ValidationError ("TimeHorizon", "Time horizon must be positive"))
        else
            // Create variable mappings functionally (timeHorizon = number of discrete slots)
            let (varMapping, _, numVariables) = createVariableMappings problem.Tasks timeHorizon

            // Calculate penalty weights
            let (penaltyOneHot, penaltyDependency, penaltyResource) =
                computePenaltyWeights problem.Tasks timeHorizon slotMinutes

            // Build QUBO terms functionally.
            // The objective term follows problem.Objective (previously the field was
            // never read and MinimizeCost silently optimised completion times):
            // MinimizeCost and MaximizeResourceUtilization are schedule-invariant /
            // makespan-equivalent under this encoding (see doc comment above), so
            // they share the completion-time objective; MinimizeLateness gets a
            // genuinely different deadline-based objective.
            let objectiveTerms =
                match problem.Objective with
                | MinimizeMakespan
                | MinimizeCost
                | MaximizeResourceUtilization ->
                    buildObjectiveTerms problem.Tasks varMapping timeHorizon slotMinutes
                | MinimizeLateness ->
                    buildLatenessTerms problem.Tasks varMapping timeHorizon slotMinutes
            let oneHotTerms = buildOneHotTerms problem.Tasks varMapping timeHorizon penaltyOneHot
            let dependencyTerms = buildDependencyTerms problem.Tasks problem.Dependencies varMapping timeHorizon slotMinutes penaltyDependency
            let resourceTerms = buildResourceTerms problem.Tasks problem.Resources varMapping timeHorizon slotMinutes penaltyResource
            
            // Combine all terms
            let quboTerms =
                [objectiveTerms; oneHotTerms; dependencyTerms; resourceTerms]
                |> List.fold (fun acc terms ->
                    Map.fold (fun acc2 key value -> addOrUpdate key value acc2) acc terms) Map.empty
            
            Ok {
                GraphOptimization.QuboMatrix.Q = quboTerms
                GraphOptimization.QuboMatrix.NumVariables = numVariables
            }
