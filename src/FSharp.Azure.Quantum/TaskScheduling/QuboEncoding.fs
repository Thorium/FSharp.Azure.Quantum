namespace FSharp.Azure.Quantum.TaskScheduling
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
        : float * float * float =
        
        let maxDuration = tasks |> List.map (fun t -> t.Duration) |> List.max
        let penaltyOneHot = Qubo.computeLucasPenalties maxDuration timeHorizon
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
    let decodeBitstring
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
    let buildSolutionFromStarts
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
    let toQubo 
        (problem: SchedulingProblem<'TTask, 'TResource>) 
        (timeHorizon: int)
        : QuantumResult<GraphOptimization.QuboMatrix> =
        
        let numTasks = problem.Tasks.Length
        
        if numTasks = 0 then
            Error (QuantumError.ValidationError ("Tasks", "No tasks to schedule"))
        elif timeHorizon <= 0 then
            Error (QuantumError.ValidationError ("TimeHorizon", "Time horizon must be positive"))
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
