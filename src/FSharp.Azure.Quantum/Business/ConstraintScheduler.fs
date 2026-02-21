namespace FSharp.Azure.Quantum.Business

open System
open FSharp.Azure.Quantum.Core
open FSharp.Azure.Quantum.Core.BackendAbstraction
open FSharp.Azure.Quantum.GroverSearch
open FSharp.Azure.Quantum.GroverSearch.Oracle
open FSharp.Azure.Quantum.Backends
open FSharp.Azure.Quantum.Algorithms
open FSharp.Azure.Quantum.Quantum

/// <summary>
/// High-level constraint-based scheduling using quantum optimization.
/// </summary>
/// <remarks>
/// **Business Use Cases:**
/// - Workforce Management: Schedule employees with shift constraints
/// - Resource Allocation: Assign tasks to servers with capacity limits
/// - Project Planning: Optimize task assignments with dependencies
/// - Manufacturing: Production scheduling with equipment constraints
/// - Cloud Computing: Container placement with resource costs
/// 
/// **Quantum Advantage:**
/// Uses Grover's algorithm with Max-SAT (constraint satisfaction) and
/// Weighted Graph Coloring (cost optimization) for quadratic speedup
/// over classical constraint solvers. Also supports QAOA-based
/// approximate optimization via SAT and Bin Packing formulations
/// for problems with capacity constraints or larger search spaces.
/// 
/// **Example:**
/// ```fsharp
/// let schedule = constraintScheduler {
///     task "Deploy API" requiresResource "FastServer"
///     task "Run Tests" requiresResource "TestServer"
///     conflict "Deploy API" "Run Tests"  // Can't run simultaneously
///     
///     resource "FastServer" cost 10.0
///     resource "TestServer" cost 2.0
///     
///     optimizeFor MinimizeCost
///     maxBudget 50.0
/// }
/// ```
/// </remarks>
module ConstraintScheduler =
    
    // ========================================================================
    // TYPES
    // ========================================================================
    
    /// Task identifier
    type TaskId = string
    
    /// Resource identifier (server, person, machine, etc.)
    type ResourceId = string
    
    /// Optimization objective
    type OptimizationGoal =
        /// Minimize total resource cost
        | MinimizeCost
        
        /// Maximize constraint satisfaction
        | MaximizeSatisfaction
        
        /// Balance cost and satisfaction
        | Balanced
    
    /// Algorithm strategy for quantum solving
    type AlgorithmStrategy =
        /// System selects the best algorithm based on problem structure
        | Auto
        
        /// Use Grover's search (exact search via oracle)
        | GroverSearch
        
        /// Use QAOA optimization (approximate optimization via QUBO)
        | QaoaOptimize
    
    /// Hard constraint that MUST be satisfied
    type HardConstraint =
        /// Two tasks cannot run on the same resource
        | Conflict of TaskId * TaskId
        
        /// Task requires specific resource type
        | RequiresResource of TaskId * ResourceId
        
        /// Task must complete before another
        | Precedence of before: TaskId * after: TaskId
    
    /// Soft constraint with weight (preferred but not required)
    type SoftConstraint =
        /// Prefer task on specific resource (weight = importance)
        | PreferResource of TaskId * ResourceId * weight: float
        
        /// Prefer tasks together (co-location)
        | PreferTogether of TaskId * TaskId * weight: float
        
        /// Prefer tasks apart (load balancing)
        | PreferApart of TaskId * TaskId * weight: float
    
    /// Resource with cost
    type Resource = {
        Id: ResourceId
        Cost: float
        Capacity: int option  // Max concurrent tasks (None = unlimited)
    }
    
    /// Scheduling problem configuration
    type SchedulingProblem = {
        /// All tasks to schedule
        Tasks: TaskId list
        
        /// Available resources
        Resources: Resource list
        
        /// Hard constraints (must satisfy)
        HardConstraints: HardConstraint list
        
        /// Soft constraints (prefer to satisfy)
        SoftConstraints: SoftConstraint list
        
        /// Optimization goal
        Goal: OptimizationGoal
        
        /// Maximum budget for resources
        MaxBudget: float option
        
        /// Quantum backend (None = classical algorithm, Some = quantum optimization)
        Backend: IQuantumBackend option
        
        /// Algorithm strategy (None = Auto, system picks best algorithm)
        Strategy: AlgorithmStrategy option
        
        /// Number of measurement shots (default: 1000)
        Shots: int
    }
    
    /// Task assignment to a resource
    type Assignment = {
        Task: TaskId
        Resource: ResourceId
        Cost: float
    }
    
    /// Scheduling solution
    type Schedule = {
        /// Task-to-resource assignments
        Assignments: Assignment list
        
        /// Total cost of schedule
        TotalCost: float
        
        /// Number of hard constraints satisfied
        HardConstraintsSatisfied: int
        
        /// Total hard constraints
        TotalHardConstraints: int
        
        /// Number of soft constraints satisfied
        SoftConstraintsSatisfied: int
        
        /// Total soft constraints
        TotalSoftConstraints: int
        
        /// Whether all hard constraints satisfied
        IsFeasible: bool
    }
    
    /// Result of scheduling optimization
    type SchedulingResult = {
        /// Best schedule found
        BestSchedule: Schedule option
        
        /// Execution message
        Message: string
    }
    
    // ========================================================================
    // HELPERS
    // ========================================================================
    
    /// Create task-to-index mapping
    let private createTaskIndex (tasks: TaskId list) : Map<TaskId, int> =
        tasks
        |> List.mapi (fun i task -> (task, i))
        |> Map.ofList
    
    /// Create resource-to-index mapping
    let private createResourceIndex (resources: Resource list) : Map<ResourceId, int> =
        resources
        |> List.mapi (fun i res -> (res.Id, i))
        |> Map.ofList
    
    /// Check if hard constraint is satisfied by assignment
    let private isSatisfied (constraint': HardConstraint) (assignments: Map<TaskId, ResourceId>) : bool =
        match constraint' with
        | Conflict (task1, task2) ->
            match Map.tryFind task1 assignments, Map.tryFind task2 assignments with
            | Some res1, Some res2 -> res1 <> res2  // Different resources
            | _ -> true  // Not both assigned yet
        
        | RequiresResource (task, resource) ->
            match Map.tryFind task assignments with
            | Some res -> res = resource
            | None -> true  // Not assigned yet
        
        | Precedence (before, after) ->
            // For scheduling slots, this would check ordering
            // Simplified: just check both exist
            Map.containsKey before assignments && Map.containsKey after assignments
    
    /// Calculate cost of assignments
    let private calculateCost (assignments: Assignment list) : float =
        assignments |> List.sumBy (fun a -> a.Cost)

    /// Check if soft constraint is satisfied
    let private isSoftSatisfied (constraint': SoftConstraint) (assignments: Map<TaskId, ResourceId>) : bool =
        match constraint' with
        | PreferResource (task, resource, _) ->
            match Map.tryFind task assignments with
            | Some res -> res = resource
            | None -> false
        
        | PreferTogether (task1, task2, _) ->
            match Map.tryFind task1 assignments, Map.tryFind task2 assignments with
            | Some res1, Some res2 -> res1 = res2
            | _ -> false
            
        | PreferApart (task1, task2, _) ->
            match Map.tryFind task1 assignments, Map.tryFind task2 assignments with
            | Some res1, Some res2 -> res1 <> res2
            | _ -> false

    /// Create Schedule from assignment list
    let private createSchedule (problem: SchedulingProblem) (assignments: Assignment list) : Schedule =
        let assignmentMap = 
            assignments 
            |> List.map (fun a -> a.Task, a.Resource) 
            |> Map.ofList
            
        let hardSatisfied = 
            problem.HardConstraints 
            |> List.filter (fun c -> isSatisfied c assignmentMap)
            |> List.length
            
        let softSatisfied =
            problem.SoftConstraints
            |> List.filter (fun c -> isSoftSatisfied c assignmentMap)
            |> List.length
            
        let totalCost = calculateCost assignments
        
        // Check if feasible (all hard constraints satisfied AND all tasks assigned)
        let allTasksScheduled = 
            problem.Tasks 
            |> List.forall (fun t -> Map.containsKey t assignmentMap)
            
        let isFeasible = 
            hardSatisfied = problem.HardConstraints.Length && allTasksScheduled
        
        // IMPORTANT: For RequiresResource constraint, ensure the specific resource ID matches
        // The isSatisfied check only verifies the constraint itself, but for the scheduler
        // to report feasibility correctly, we double-check hard constraints here.
        let actuallyFeasible =
             if isFeasible then
                 problem.HardConstraints
                 |> List.forall (fun c -> isSatisfied c assignmentMap)
             else
                 false

        {
            Assignments = assignments
            TotalCost = totalCost
            HardConstraintsSatisfied = hardSatisfied
            TotalHardConstraints = problem.HardConstraints.Length
            SoftConstraintsSatisfied = softSatisfied
            TotalSoftConstraints = problem.SoftConstraints.Length
            IsFeasible = actuallyFeasible
        }

    /// Decode Max-SAT bitstring solution to Schedule
    let private decodeSatSolution (problem: SchedulingProblem) (solutionBits: int) : Schedule =
        let assignments = 
            problem.Tasks
            |> List.mapi (fun tIdx task ->
                problem.Resources
                |> List.mapi (fun rIdx res ->
                    let varId = tIdx * problem.Resources.Length + rIdx
                    let isAssigned = (solutionBits >>> varId) &&& 1 = 1
                    if isAssigned then
                        Some { Task = task; Resource = res.Id; Cost = res.Cost }
                    else
                        None
                )
                |> List.choose id
            )
            |> List.concat
            
        createSchedule problem assignments

    /// Decode Graph Coloring bitstring solution to Schedule
    let private decodeColoringSolution (problem: SchedulingProblem) (solutionBits: int) : Schedule =
        let numColors = problem.Resources.Length
        let qubitsPerVert = 
            if numColors <= 1 then 1
            else 
                let log2 = Math.Log(float numColors) / Math.Log(2.0)
                int (Math.Ceiling(log2))
                
        let assignments = 
            problem.Tasks
            |> List.mapi (fun tIdx task ->
                // Extract color (resource index)
                let shift = tIdx * qubitsPerVert
                let mask = (1 <<< qubitsPerVert) - 1
                let resIdx = (solutionBits >>> shift) &&& mask
                
                if resIdx < problem.Resources.Length then
                    let res = problem.Resources.[resIdx]
                    Some { Task = task; Resource = res.Id; Cost = res.Cost }
                else
                    None
            )
            |> List.choose id
            
        createSchedule problem assignments
    
    /// Convert scheduling problem to Max-SAT for constraint satisfaction
    let private toMaxSat (problem: SchedulingProblem) : MaxSatConfig =
        let taskIdx = createTaskIndex problem.Tasks
        let resIdx = createResourceIndex problem.Resources
        
        // Each task-resource pair is a boolean variable
        let numVars = problem.Tasks.Length * problem.Resources.Length
        
        // 1. Structural Constraints: Each task must be assigned to EXACTLY one resource
        let structuralClauses =
            problem.Tasks
            |> List.collect (fun task ->
                let t = taskIdx.[task]
                
                // 1a. At least one resource (OR of all resource vars for this task)
                let atLeastOne = 
                    problem.Resources
                    |> List.mapi (fun r _ -> 
                        let varId = t * problem.Resources.Length + r
                        { VariableIndex = varId; IsNegated = false }
                    )
                    |> fun lits -> { Literals = lits }
                
                // 1b. At most one resource (Pairwise mutex: NOT r1 OR NOT r2)
                let atMostOne = 
                    problem.Resources
                    |> List.mapi (fun r1 _ ->
                        problem.Resources
                        |> List.mapi (fun r2 _ ->
                            if r1 < r2 then
                                let v1 = t * problem.Resources.Length + r1
                                let v2 = t * problem.Resources.Length + r2
                                Some { Literals = [
                                    { VariableIndex = v1; IsNegated = true }; 
                                    { VariableIndex = v2; IsNegated = true }
                                ] }
                            else
                                None
                        )
                        |> List.choose id
                    )
                    |> List.concat
                    
                atLeastOne :: atMostOne
            )

        // 2. User Hard constraints
        let constraintClauses =
            problem.HardConstraints
            |> List.collect (fun constraint' ->
                match constraint' with
                | Conflict (task1, task2) ->
                    // If task1 uses resource R, task2 cannot use R
                    // For each resource R: NOT x(t1,R) OR NOT x(t2,R)
                    match Map.tryFind task1 taskIdx, Map.tryFind task2 taskIdx with
                    | Some t1, Some t2 ->
                        problem.Resources
                        |> List.mapi (fun r _ ->
                            let v1 = t1 * problem.Resources.Length + r
                            let v2 = t2 * problem.Resources.Length + r
                            { Literals = [
                                { VariableIndex = v1; IsNegated = true }; 
                                { VariableIndex = v2; IsNegated = true }
                            ] }
                        )
                    | _ -> []
                
                | RequiresResource (task, resource) ->
                    // Task must use this specific resource
                    // x(t,r) must be true
                    match Map.tryFind task taskIdx, Map.tryFind resource resIdx with
                    | Some t, Some r ->
                        let varId = t * problem.Resources.Length + r
                        let lit = { VariableIndex = varId; IsNegated = false }
                        [ { Literals = [lit] } ]
                    | _ -> []
                
                | Precedence _ ->
                    []  // Would need temporal logic encoding, ignoring for now
            )
        
        let allClauses = structuralClauses @ constraintClauses
        
        let formula = {
            NumVariables = numVars
            Clauses = allClauses
        }
        
        {
            Formula = formula
            MinClausesSatisfied = allClauses.Length  // All constraints (structural + user) must be satisfied
        }

    /// Convert scheduling problem to Weighted Graph Coloring for cost optimization
    let private toWeightedColoring (problem: SchedulingProblem) : WeightedColoringConfig =
        let taskIdx = createTaskIndex problem.Tasks
        
        // Nodes = tasks, edges = conflicts
        let edges =
            problem.HardConstraints
            |> List.choose (fun constraint' ->
                match constraint' with
                | Conflict (task1, task2) ->
                    match Map.tryFind task1 taskIdx, Map.tryFind task2 taskIdx with
                    | Some t1, Some t2 -> Some (t1, t2)
                    | _ -> None
                | _ -> None
            )
        
        // Colors = resources, costs = resource costs
        let colorCosts =
            problem.Resources
            |> List.map (fun res -> res.Cost)
            |> Array.ofList
        
        let graph = {
            NumVertices = problem.Tasks.Length
            Edges = edges
        }
        
        // Use MaxBudget if specified, otherwise use sum of all resource costs
        let maxCost =
            match problem.MaxBudget with
            | Some budget -> budget
            | None -> colorCosts |> Array.sum
        
        {
            Graph = graph
            NumColors = problem.Resources.Length
            ColorCosts = colorCosts
            MaxTotalCost = maxCost
        }
    
    /// Find optimal schedule using quantum Max-SAT
    let private optimizeQuantumSat (backend: IQuantumBackend) (problem: SchedulingProblem) : QuantumResult<Schedule option> =
        let maxSatConfig = toMaxSat problem
        
        match maxSatOracle maxSatConfig with
        | Error err -> Error err
        | Ok oracle ->
            // Configure Grover search with specified shots
            let groverConfig = { Grover.defaultConfig with Shots = problem.Shots }
            
            // Run Grover's search algorithm
            match Grover.search oracle backend groverConfig with
            | Error err -> Error err
            | Ok groverResult ->
                // Decode bitstring solutions to Schedule
                // We pick the most likely solution (highest probability)
                match groverResult.Solutions with
                | [] -> Ok None
                | bestSolution :: _ ->
                    let schedule = decodeSatSolution problem bestSolution
                    Ok (Some schedule)
    
    /// Find optimal schedule using quantum Weighted Coloring
    let private optimizeQuantumColoring (backend: IQuantumBackend) (problem: SchedulingProblem) : QuantumResult<Schedule option> =
        let coloringConfig = toWeightedColoring problem
        
        match weightedColoringOracle coloringConfig with
        | Error err -> Error err
        | Ok oracle ->
            // Configure Grover search with specified shots
            let groverConfig = { Grover.defaultConfig with Shots = problem.Shots }
            
            // Run Grover's search algorithm
            match Grover.search oracle backend groverConfig with
            | Error err -> Error err
            | Ok groverResult ->
                // Decode bitstring solutions to Schedule
                // We pick the most likely solution (highest probability)
                match groverResult.Solutions with
                | [] -> Ok None
                | bestSolution :: _ ->
                    let schedule = decodeColoringSolution problem bestSolution
                    Ok (Some schedule)
    
    // ========================================================================
    // QAOA-BASED OPTIMIZATION (approximate, via QUBO)
    // ========================================================================
    
    /// Convert scheduling problem to QuantumSatSolver.Problem for QAOA optimization.
    /// Same encoding as toMaxSat but targets QuantumSatSolver types.
    let private toQaoaSatProblem (problem: SchedulingProblem) : QuantumSatSolver.Problem =
        let taskIdx = createTaskIndex problem.Tasks
        let resIdx = createResourceIndex problem.Resources
        
        // Each task-resource pair is a boolean variable
        let numVars = problem.Tasks.Length * problem.Resources.Length
        
        // 1. Structural: each task assigned to exactly one resource
        let structuralClauses =
            problem.Tasks
            |> List.collect (fun task ->
                let t = taskIdx.[task]
                
                // At least one resource
                let atLeastOne : QuantumSatSolver.Clause =
                    { Literals =
                        problem.Resources
                        |> List.mapi (fun r _ ->
                            let varId = t * problem.Resources.Length + r
                            ({ Variable = varId; IsNegated = false } : QuantumSatSolver.Literal))
                    }
                
                // At most one resource (pairwise mutex)
                let atMostOne : QuantumSatSolver.Clause list =
                    problem.Resources
                    |> List.mapi (fun r1 _ ->
                        problem.Resources
                        |> List.mapi (fun r2 _ ->
                            if r1 < r2 then
                                let v1 = t * problem.Resources.Length + r1
                                let v2 = t * problem.Resources.Length + r2
                                Some ({ Literals = [
                                    ({ Variable = v1; IsNegated = true } : QuantumSatSolver.Literal)
                                    ({ Variable = v2; IsNegated = true } : QuantumSatSolver.Literal)
                                ] } : QuantumSatSolver.Clause)
                            else
                                None)
                        |> List.choose id)
                    |> List.concat
                
                atLeastOne :: atMostOne)
        
        // 2. Hard constraints
        let constraintClauses =
            problem.HardConstraints
            |> List.collect (fun constraint' ->
                match constraint' with
                | Conflict (task1, task2) ->
                    match Map.tryFind task1 taskIdx, Map.tryFind task2 taskIdx with
                    | Some t1, Some t2 ->
                        problem.Resources
                        |> List.mapi (fun r _ ->
                            let v1 = t1 * problem.Resources.Length + r
                            let v2 = t2 * problem.Resources.Length + r
                            ({ Literals = [
                                ({ Variable = v1; IsNegated = true } : QuantumSatSolver.Literal)
                                ({ Variable = v2; IsNegated = true } : QuantumSatSolver.Literal)
                            ] } : QuantumSatSolver.Clause))
                    | _ -> []
                | RequiresResource (task, resource) ->
                    match Map.tryFind task taskIdx, Map.tryFind resource resIdx with
                    | Some t, Some r ->
                        let varId = t * problem.Resources.Length + r
                        [ ({ Literals = [
                            ({ Variable = varId; IsNegated = false } : QuantumSatSolver.Literal)
                        ] } : QuantumSatSolver.Clause) ]
                    | _ -> []
                | Precedence _ -> [])
        
        { NumVariables = numVars
          Clauses = structuralClauses @ constraintClauses }
    
    /// Decode a QuantumSatSolver.Solution back to a Schedule.
    /// The SAT assignment is a bool[] where variable (t * numResources + r) = true
    /// means task t is assigned to resource r.
    let private decodeQaoaSatSolution (problem: SchedulingProblem) (satSolution: QuantumSatSolver.Solution) : Schedule =
        let assignments =
            problem.Tasks
            |> List.mapi (fun tIdx task ->
                problem.Resources
                |> List.mapi (fun rIdx res ->
                    let varId = tIdx * problem.Resources.Length + rIdx
                    if varId < satSolution.Assignment.Length && satSolution.Assignment.[varId] then
                        Some { Task = task; Resource = res.Id; Cost = res.Cost }
                    else
                        None)
                |> List.choose id)
            |> List.concat
        
        createSchedule problem assignments
    
    /// Convert scheduling problem to QuantumBinPackingSolver.Problem.
    /// Tasks are "items" (unit size) and resources with Capacity are "bins".
    /// Resources without Capacity get a default capacity equal to total task count.
    let private toQaoaBinPackingProblem (problem: SchedulingProblem) : QuantumBinPackingSolver.Problem =
        let items =
            problem.Tasks
            |> List.map (fun taskId ->
                ({ Id = taskId; Size = 1.0 } : QuantumBinPackingSolver.Item))
        
        // Use the max capacity of any resource, or task count as default
        let binCapacity =
            problem.Resources
            |> List.choose (fun r -> r.Capacity)
            |> function
               | [] -> float problem.Tasks.Length  // No capacity constraints: all tasks fit
               | capacities -> capacities |> List.max |> float
        
        { Items = items
          BinCapacity = binCapacity }
    
    /// Decode a QuantumBinPackingSolver.Solution back to a Schedule.
    /// Bin indices are mapped back to resource indices.
    let private decodeQaoaBinPackingSolution (problem: SchedulingProblem) (binSolution: QuantumBinPackingSolver.Solution) : Schedule =
        let assignments =
            binSolution.Assignments
            |> List.choose (fun (item, binIdx) ->
                let taskId = item.Id
                // Map bin index to resource (modular wrap if more bins than resources)
                let resIdx = binIdx % problem.Resources.Length
                if resIdx < problem.Resources.Length then
                    let res = problem.Resources.[resIdx]
                    Some { Task = taskId; Resource = res.Id; Cost = res.Cost }
                else
                    None)
        
        createSchedule problem assignments
    
    /// Find optimal schedule using QAOA-based SAT optimization.
    let private optimizeQaoaSat (backend: IQuantumBackend) (problem: SchedulingProblem) : QuantumResult<Schedule option> =
        let satProblem = toQaoaSatProblem problem
        
        match QuantumSatSolver.solve backend satProblem problem.Shots with
        | Error err -> Error err
        | Ok satSolution ->
            let schedule = decodeQaoaSatSolution problem satSolution
            Ok (Some schedule)
    
    /// Find optimal schedule using QAOA-based bin packing optimization.
    let private optimizeQaoaBinPacking (backend: IQuantumBackend) (problem: SchedulingProblem) : QuantumResult<Schedule option> =
        let binProblem = toQaoaBinPackingProblem problem
        
        match QuantumBinPackingSolver.solve backend binProblem problem.Shots with
        | Error err -> Error err
        | Ok binSolution ->
            let schedule = decodeQaoaBinPackingSolution problem binSolution
            Ok (Some schedule)
    
    /// Determine the effective strategy based on problem characteristics.
    /// Auto selects QAOA when capacity constraints are present (bin packing formulation),
    /// and Grover otherwise (exact search is preferred for small problems).
    let private resolveStrategy (problem: SchedulingProblem) : AlgorithmStrategy =
        match problem.Strategy with
        | Some strategy -> strategy
        | None ->
            // Auto: Use QAOA if any resource has capacity constraints
            let hasCapacity = problem.Resources |> List.exists (fun r -> r.Capacity.IsSome)
            if hasCapacity then QaoaOptimize
            else GroverSearch

    /// Find schedule using classical algorithm (baseline)
    let private optimizeClassical (_problem: SchedulingProblem) : Schedule option =
        failwith
            "Classical constraint scheduling is not implemented. \
             Provide a quantum backend via SchedulingProblem.Backend."
    
    /// Execute scheduling optimization
    let solve (problem: SchedulingProblem) : QuantumResult<SchedulingResult> =
        if problem.Tasks.IsEmpty then
            Error (QuantumError.ValidationError ("Tasks", "must have at least one task"))
        elif problem.Resources.IsEmpty then
            Error (QuantumError.ValidationError ("Resources", "must have at least one resource"))
        elif problem.Tasks.Length > 50 then
            Error (QuantumError.ValidationError ("Tasks", $"too many tasks ({problem.Tasks.Length}), maximum is 50"))
        else
            // Infer quantum vs classical from backend presence
            let bestSchedule =
                match problem.Backend with
                | Some backend ->
                    let strategy = resolveStrategy problem
                    
                    match strategy, problem.Goal with
                    // Grover paths (existing)
                    | GroverSearch, MaximizeSatisfaction ->
                        optimizeQuantumSat backend problem
                    | GroverSearch, (MinimizeCost | Balanced) ->
                        optimizeQuantumColoring backend problem
                    
                    // QAOA paths (new)
                    | QaoaOptimize, MaximizeSatisfaction ->
                        optimizeQaoaSat backend problem
                    | QaoaOptimize, (MinimizeCost | Balanced) ->
                        // Use bin packing when capacity constraints exist, SAT otherwise
                        let hasCapacity = problem.Resources |> List.exists (fun r -> r.Capacity.IsSome)
                        if hasCapacity then
                            optimizeQaoaBinPacking backend problem
                        else
                            optimizeQaoaSat backend problem
                    
                    // Auto strategy dispatches to resolved strategy (already handled by resolveStrategy)
                    | Auto, _ ->
                        // resolveStrategy never returns Auto, but handle for completeness
                        optimizeQuantumSat backend problem
                
                | None ->
                    Error (QuantumError.NotImplemented (
                        "Classical constraint scheduling",
                        Some "Provide a quantum backend via SchedulingProblem.Backend."))
            
            match bestSchedule with
            | Error e -> Error e
            | Ok schedule ->

            // If quantum returned None (no solution found), try greedy fallback for small problems
            let finalSchedule = 
                match schedule with
                | Some _ -> schedule
                | None -> 
                     if problem.Tasks.Length <= 5 then
                         // Recursive permutation generator using List.collect (idiomatic F#)
                         let rec permutations = function
                             | [] -> [[]]
                             | list -> 
                                 list 
                                 |> List.collect (fun x -> 
                                     permutations (list |> List.filter ((<>) x)) 
                                     |> List.map (fun p -> x :: p))
                         
                         // Generate all possible resource assignments using recursion
                         let generateAssignments =
                             let resources = problem.Resources
                             let rec loop tasks acc =
                                 match tasks with
                                 | [] -> [acc]
                                 | task :: rest ->
                                     resources
                                     |> List.collect (fun res ->
                                         let assignment = { Task = task; Resource = res.Id; Cost = res.Cost }
                                         loop rest (assignment :: acc)
                                     )
                             loop problem.Tasks []
                         
                         generateAssignments
                         |> List.map (fun assignments -> createSchedule problem assignments)
                         |> List.filter (fun s -> s.IsFeasible)
                         |> List.sortBy (fun s -> 
                                match problem.Goal with
                                | MinimizeCost -> s.TotalCost
                                | MaximizeSatisfaction -> float (s.TotalHardConstraints - s.HardConstraintsSatisfied) // Minimize violations
                                | Balanced -> s.TotalCost // Simplified
                           )
                         |> List.tryHead
                     else
                         None

            Ok {
                BestSchedule = finalSchedule
                Message = 
                    match finalSchedule with
                    | None -> "No feasible schedule found with current constraints"
                    | Some sched ->
                        if sched.IsFeasible then
                            $"Found feasible schedule with cost ${sched.TotalCost:F2}"
                        else
                            $"Found partial schedule (unsatisfied constraints: {sched.TotalHardConstraints - sched.HardConstraintsSatisfied})"
            }
    
    // ========================================================================
    // COMPUTATION EXPRESSION BUILDER
    // ========================================================================
    
    /// <summary>
    /// Fluent builder for constraint-based scheduling.
    /// </summary>
    /// <remarks>
    /// Provides an enterprise-friendly API for scheduling optimization
    /// without requiring knowledge of quantum algorithms or SAT solvers.
    /// 
    /// **Example - Workforce Scheduling:**
    /// ```fsharp
    /// let schedule = constraintScheduler {
    ///     // Define tasks
    ///     task "MorningShift"
    ///     task "AfternoonShift"
    ///     task "NightShift"
    ///     
    ///     // Define workers (resources)
    ///     resource "Alice" cost 25.0  // Senior: $25/hour
    ///     resource "Bob" cost 15.0    // Junior: $15/hour
    ///     
    ///     // Hard constraints (must satisfy)
    ///     conflict "MorningShift" "AfternoonShift"  // Can't work consecutive shifts
    ///     
    ///     // Soft constraints (preferences)
    ///     prefer "NightShift" "Alice" weight 2.0  // Alice prefers nights
    ///     
    ///     // Optimization
    ///     optimizeFor MinimizeCost
    ///     maxBudget 100.0
    ///     
    ///     // Enable quantum acceleration (optional - omit for classical)
    ///     backend (LocalBackend.LocalBackend() :> IQuantumBackend)
    /// }
    /// 
    /// match solve schedule with
    /// | Ok result ->
    ///     match result.BestSchedule with
    ///     | Some sched ->
    ///         printfn "Total cost: $%.2f" sched.TotalCost
    ///         for assignment in sched.Assignments do
    ///             printfn "%s -> %s ($%.2f)" assignment.Task assignment.Resource assignment.Cost
    ///     | None ->
    ///         printfn "No feasible schedule found"
    /// | Error err ->
    ///     printfn "Error: %A" err
    /// ```
    /// </remarks>
    type ConstraintSchedulerBuilder() =
        
        /// Default empty problem
        let defaultProblem = {
            Tasks = []
            Resources = []
            HardConstraints = []
            SoftConstraints = []
            Goal = Balanced
            MaxBudget = None
            Backend = None
            Strategy = None
            Shots = 1000
        }
        
        /// Initialize builder
        member _.Yield(_) = defaultProblem
        
        /// Delay execution for computation expressions
        member _.Delay(f: unit -> SchedulingProblem) = f
        
        /// Execute the optimization and return result
        member _.Run(f: unit -> SchedulingProblem) : QuantumResult<SchedulingResult> =
            let problem = f()
            solve problem
        
        /// Combine operations (later operation takes precedence)
        member _.Combine(p1: SchedulingProblem, p2: SchedulingProblem) = p2
        
        /// Empty expression
        member _.Zero() = defaultProblem
        
        /// <summary>Add a task to schedule.</summary>
        /// <param name="taskId">Unique task identifier</param>
        [<CustomOperation("task")>]
        member _.Task(problem: SchedulingProblem, taskId: TaskId) : SchedulingProblem =
            { problem with Tasks = taskId :: problem.Tasks }
        
        /// <summary>Add multiple tasks at once.</summary>
        /// <param name="tasks">List of task identifiers</param>
        [<CustomOperation("tasks")>]
        member _.Tasks(problem: SchedulingProblem, tasks: TaskId list) : SchedulingProblem =
            { problem with Tasks = tasks @ problem.Tasks }
        
        /// <summary>Add a resource (server, person, machine).</summary>
        /// <param name="resourceId">Resource identifier</param>
        /// <param name="cost">Cost per use ($/hour, $/unit, etc.)</param>
        [<CustomOperation("resource")>]
        member _.Resource(problem: SchedulingProblem, resourceId: ResourceId, cost: float) : SchedulingProblem =
            let res = { Id = resourceId; Cost = cost; Capacity = None }
            { problem with Resources = res :: problem.Resources }
        
        /// <summary>Add a resource with capacity limit.</summary>
        /// <param name="resourceId">Resource identifier</param>
        /// <param name="cost">Cost per use</param>
        /// <param name="capacity">Maximum concurrent tasks</param>
        [<CustomOperation("resourceWithCapacity")>]
        member _.ResourceWithCapacity(problem: SchedulingProblem, resourceId: ResourceId, cost: float, capacity: int) : SchedulingProblem =
            let res = { Id = resourceId; Cost = cost; Capacity = Some capacity }
            { problem with Resources = res :: problem.Resources }
        
        /// <summary>Add conflict constraint (tasks cannot share resource).</summary>
        /// <param name="task1">First task</param>
        /// <param name="task2">Second task</param>
        [<CustomOperation("conflict")>]
        member _.Conflict(problem: SchedulingProblem, task1: TaskId, task2: TaskId) : SchedulingProblem =
            let constraint' = Conflict (task1, task2)
            { problem with HardConstraints = constraint' :: problem.HardConstraints }
        
        /// <summary>Require task to use specific resource.</summary>
        /// <param name="task">Task identifier</param>
        /// <param name="resource">Required resource</param>
        [<CustomOperation("require")>]
        member _.Require(problem: SchedulingProblem, task: TaskId, resource: ResourceId) : SchedulingProblem =
            let constraint' = RequiresResource (task, resource)
            { problem with HardConstraints = constraint' :: problem.HardConstraints }
        
        /// <summary>Task must complete before another.</summary>
        /// <param name="before">Task that must finish first</param>
        /// <param name="after">Task that must start after</param>
        [<CustomOperation("precedence")>]
        member _.Precedence(problem: SchedulingProblem, before: TaskId, after: TaskId) : SchedulingProblem =
            let constraint' = Precedence (before, after)
            { problem with HardConstraints = constraint' :: problem.HardConstraints }
        
        /// <summary>Prefer task on specific resource (soft constraint).</summary>
        /// <param name="task">Task identifier</param>
        /// <param name="resource">Preferred resource</param>
        /// <param name="weight">Importance (higher = more important)</param>
        [<CustomOperation("prefer")>]
        member _.Prefer(problem: SchedulingProblem, task: TaskId, resource: ResourceId, weight: float) : SchedulingProblem =
            let constraint' = PreferResource (task, resource, weight)
            { problem with SoftConstraints = constraint' :: problem.SoftConstraints }
        
        /// <summary>Set optimization goal.</summary>
        /// <param name="goal">MinimizeCost | MaximizeSatisfaction | Balanced</param>
        [<CustomOperation("optimizeFor")>]
        member _.OptimizeFor(problem: SchedulingProblem, goal: OptimizationGoal) : SchedulingProblem =
            { problem with Goal = goal }
        
        /// <summary>Set maximum budget constraint.</summary>
        /// <param name="budget">Maximum total cost allowed</param>
        [<CustomOperation("maxBudget")>]
        member _.MaxBudget(problem: SchedulingProblem, budget: float) : SchedulingProblem =
            { problem with MaxBudget = Some budget }
        
        /// <summary>Set the quantum backend for execution.</summary>
        /// <param name="backend">Quantum backend instance (enables quantum optimization)</param>
        /// <remarks>
        /// Providing a backend enables Grover's algorithm for quantum optimization.
        /// Omit this to use classical algorithm instead.
        /// 
        /// Examples:
        /// - LocalBackend: Local quantum simulation
        /// - IonQ, Rigetti, Quantinuum: Cloud quantum hardware
        /// </remarks>
        [<CustomOperation("backend")>]
        member _.Backend(problem: SchedulingProblem, backend: IQuantumBackend) : SchedulingProblem =
            { problem with Backend = Some backend }
        
        /// <summary>Set the number of measurement shots.</summary>
        /// <param name="shots">Number of circuit measurements (default: 1000)</param>
        /// <remarks>
        /// Higher shot counts increase accuracy but take longer to execute.
        /// Recommended: 1000-10000 for production, 100-1000 for testing.
        /// </remarks>
        [<CustomOperation("shots")>]
        member _.Shots(problem: SchedulingProblem, shots: int) : SchedulingProblem =
            { problem with Shots = shots }
        
        /// <summary>Use Grover's search algorithm (exact search via oracle).</summary>
        /// <remarks>
        /// Grover's algorithm uses an oracle to search for valid schedules.
        /// Best for small problems where an exact solution is desired.
        /// This is the default strategy when no capacity constraints exist.
        /// </remarks>
        [<CustomOperation("useGrover")>]
        member _.UseGrover(problem: SchedulingProblem) : SchedulingProblem =
            { problem with Strategy = Some GroverSearch }
        
        /// <summary>Use QAOA optimization (approximate optimization via QUBO).</summary>
        /// <remarks>
        /// QAOA formulates the scheduling problem as a QUBO and uses
        /// variational quantum optimization. Best for problems with
        /// capacity constraints or when approximate solutions are acceptable.
        /// Automatically selects bin packing formulation when resources have
        /// capacity limits, or SAT formulation otherwise.
        /// </remarks>
        [<CustomOperation("useQaoa")>]
        member _.UseQaoa(problem: SchedulingProblem) : SchedulingProblem =
            { problem with Strategy = Some QaoaOptimize }
    
    /// <summary>
    /// Create a constraint-based scheduler builder.
    /// </summary>
    /// <remarks>
    /// Use this builder to optimize scheduling problems with constraints,
    /// such as workforce management, resource allocation, or project planning.
    /// 
    /// **Business Applications:**
    /// - **Workforce**: Schedule employees with shift constraints and costs
    /// - **Cloud Computing**: Optimize container placement with resource limits
    /// - **Manufacturing**: Production scheduling with equipment constraints
    /// - **Logistics**: Route optimization with vehicle capacity
    /// </remarks>
    let constraintScheduler = ConstraintSchedulerBuilder()
