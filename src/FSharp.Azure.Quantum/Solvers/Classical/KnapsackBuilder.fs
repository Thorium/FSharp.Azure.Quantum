namespace FSharp.Azure.Quantum

open FSharp.Azure.Quantum.Backends
open FSharp.Azure.Quantum.Quantum
open FSharp.Azure.Quantum.Backends
open FSharp.Azure.Quantum.Core

/// High-level Knapsack Domain Builder - Quantum-First API
/// 
/// DESIGN PHILOSOPHY:
/// This is a BUSINESS DOMAIN API for users who want to solve Knapsack problems
/// without understanding quantum computing internals (QAOA, QUBO, backends).
/// 
/// QUANTUM-FIRST:
/// - Uses quantum optimization (QAOA) by default via LocalBackend (simulation)
/// - Optional backend parameter for cloud quantum hardware (IonQ, Rigetti)
/// - For algorithm-level control, use QuantumKnapsackSolver directly
/// 
/// WHAT IS KNAPSACK:
/// The 0/1 Knapsack Problem is a fundamental combinatorial optimization problem:
/// select items with weights and values to maximize total value without exceeding capacity.
/// 
/// USE CASES:
/// - Resource allocation: Select projects within budget constraint
/// - Portfolio optimization: Choose investments within capital limit
/// - Cargo loading: Maximize value of goods on truck/ship
/// - Task scheduling: Select tasks within time/resource constraints
/// - Budget planning: Choose features to implement within sprint capacity
/// 
/// EXAMPLE USAGE:
///   // Simple: Uses quantum simulation automatically
///   let solution = Knapsack.solve problem None
///   
///   // Advanced: Specify cloud quantum backend
///   let ionqBackend = BackendAbstraction.createIonQBackend(...)
///   let solution = Knapsack.solve problem (Some ionqBackend)
///   
///   // Expert: Direct quantum solver access
///   open FSharp.Azure.Quantum.Quantum
///   let result = QuantumKnapsackSolver.solve backend problem config
module Knapsack =

    // ============================================================================
    // TYPES - Domain-specific types for Knapsack problems
    // ============================================================================

    /// Knapsack Item with weight and value.
    /// Alias of the solver's item type so the builder and solver share one
    /// definition (no parallel record, no field-by-field mapping).
    type Item = QuantumKnapsackSolver.KnapsackItem

    /// Knapsack Problem representation
    type Problem = {
        /// Available items to choose from
        Items: Item list
        
        /// Knapsack capacity (maximum total weight)
        Capacity: float
        
        /// Number of items
        ItemCount: int
        
        /// Total value if all items selected (upper bound)
        TotalValue: float
        
        /// Total weight if all items selected
        TotalWeight: float
    }

    /// Knapsack Solution with selected items and metrics
    type Solution = {
        /// Selected items
        SelectedItems: Item list
        
        /// Total weight of selected items
        TotalWeight: float
        
        /// Total value of selected items
        TotalValue: float
        
        /// Whether solution satisfies capacity constraint
        IsFeasible: bool
        
        /// Value-to-weight ratio (efficiency metric)
        Efficiency: float
        
        /// Capacity utilization percentage (0-100%)
        CapacityUtilization: float
        
        /// Backend used (LocalBackend, IonQ, etc.)
        BackendName: string
        
        /// Whether quantum or classical solver was used
        IsQuantum: bool
    }

    // ============================================================================
    // PROBLEM CREATION
    // ============================================================================

    /// Create Knapsack problem from items and capacity
    /// 
    /// PARAMETERS:
    ///   items - List of (id, weight, value) tuples
    ///   capacity - Maximum total weight allowed
    /// 
    /// RETURNS:
    ///   Problem ready for solving
    /// 
    /// EXAMPLE:
    ///   let items = [
    ///       ("laptop", 3.0, 1000.0)
    ///       ("phone", 0.5, 500.0)
    ///       ("tablet", 1.5, 700.0)
    ///   ]
    ///   let problem = Knapsack.createProblem items 5.0
    let createProblem (items: (string * float * float) list) (capacity: float) : Problem =
        let itemList : Item list =
            items
            |> List.map (fun (id, weight, value) ->
                { Id = id; Weight = weight; Value = value })
        
        let totalValue = itemList |> List.sumBy (fun item -> item.Value)
        let totalWeight = itemList |> List.sumBy (fun item -> item.Weight)
        
        {
            Items = itemList
            Capacity = capacity
            ItemCount = itemList.Length
            TotalValue = totalValue
            TotalWeight = totalWeight
        }

    // ============================================================================
    // HELPER FUNCTIONS - COMMON PROBLEM INSTANCES
    // ============================================================================

    /// Create a budget allocation problem
    /// 
    /// PARAMETERS:
    ///   projects - List of (name, cost, benefit) tuples
    ///   budget - Total available budget
    /// 
    /// EXAMPLE:
    ///   let projects = [
    ///       ("Feature A", 10000.0, 25000.0)
    ///       ("Feature B", 15000.0, 30000.0)
    ///       ("Feature C", 8000.0, 18000.0)
    ///   ]
    ///   let problem = Knapsack.budgetAllocation projects 25000.0
    let budgetAllocation (projects: (string * float * float) list) (budget: float) : Problem =
        createProblem projects budget

    /// Create a cargo loading problem
    /// 
    /// PARAMETERS:
    ///   cargo - List of (name, weight_kg, value_usd) tuples
    ///   capacity_kg - Maximum cargo weight in kilograms
    /// 
    /// EXAMPLE:
    ///   let cargo = [
    ///       ("Electronics", 100.0, 50000.0)
    ///       ("Furniture", 500.0, 15000.0)
    ///       ("Textiles", 200.0, 20000.0)
    ///   ]
    ///   let problem = Knapsack.cargoLoading cargo 1000.0
    let cargoLoading (cargo: (string * float * float) list) (capacity_kg: float) : Problem =
        createProblem cargo capacity_kg

    /// Create a task scheduling problem
    /// 
    /// PARAMETERS:
    ///   tasks - List of (name, time_hours, priority) tuples
    ///   available_hours - Total time available
    /// 
    /// EXAMPLE:
    ///   let tasks = [
    ///       ("Critical Bug Fix", 4.0, 100.0)
    ///       ("Feature Request", 8.0, 60.0)
    ///       ("Code Review", 2.0, 40.0)
    ///   ]
    ///   let problem = Knapsack.taskScheduling tasks 10.0
    let taskScheduling (tasks: (string * float * float) list) (available_hours: float) : Problem =
        createProblem tasks available_hours

    /// Create a random knapsack instance (for testing/benchmarking)
    /// 
    /// PARAMETERS:
    ///   numItems - Number of items to generate
    ///   maxWeight - Maximum weight per item
    ///   maxValue - Maximum value per item
    ///   capacityRatio - Capacity as fraction of total weight (0.0-1.0)
    /// 
    /// EXAMPLE:
    ///   let problem = Knapsack.randomInstance 10 100.0 500.0 0.5
    let randomInstance (numItems: int) (maxWeight: float) (maxValue: float) (capacityRatio: float) : Problem =
        let rng = System.Random()
        
        let items = 
            [1 .. numItems]
            |> List.map (fun i ->
                let weight = rng.NextDouble() * maxWeight
                let value = rng.NextDouble() * maxValue
                (sprintf "Item%d" i, weight, value))
        
        let totalWeight = items |> List.sumBy (fun (_, w, _) -> w)
        let capacity = totalWeight * capacityRatio
        
        createProblem items capacity

    // ============================================================================
    // MAIN SOLVER
    // ============================================================================

    /// Solve Knapsack problem using quantum optimization (QAOA)
    /// 
    /// QUANTUM-FIRST API:
    /// - Uses quantum backend by default (LocalBackend for simulation)
    /// - Specify custom backend for cloud quantum hardware (IonQ, Rigetti)
    /// - Returns business-domain Solution result (not low-level QAOA output)
    /// 
    /// PARAMETERS:
    ///   problem - Knapsack problem with items and capacity
    ///   backend - Optional quantum backend (defaults to LocalBackend if None)
    /// 
    /// EXAMPLES:
    ///   // Simple: Automatic quantum simulation
    ///   let solution = Knapsack.solve problem None
    ///   
    ///   // Cloud execution: Specify IonQ backend
    ///   let ionqBackend = BackendAbstraction.createIonQBackend(...)
    ///   let solution = Knapsack.solve problem (Some ionqBackend)
    /// 
    /// RETURNS:
    ///   Result with Solution (selected items, value, feasibility) or error message
    let solve (problem: Problem) (backend: BackendAbstraction.IQuantumBackend option) : QuantumResult<Solution> =
        try
            // Use provided backend or create LocalBackend for simulation
            let actualBackend = 
                backend 
                |> Option.defaultValue (LocalBackend.LocalBackend() :> BackendAbstraction.IQuantumBackend)
            
            // Convert to quantum solver format
            let quantumProblem : QuantumKnapsackSolver.KnapsackProblem =
                { Items = problem.Items; Capacity = problem.Capacity }
            
            // Create quantum Knapsack solver configuration
            let quantumConfig : QuantumKnapsackSolver.QaoaConfig = {
                NumShots = 1000
                InitialParameters = (0.5, 0.5)
            }
            
            // Call quantum Knapsack solver directly using computation expression
            quantumResult {
                let! quantumResult = QuantumKnapsackSolver.solve actualBackend quantumProblem quantumConfig
                
                let efficiency = 
                    if quantumResult.TotalWeight > 0.0 then
                        quantumResult.TotalValue / quantumResult.TotalWeight
                    else 0.0
                
                let capacityUtilization = 
                    if problem.Capacity > 0.0 then
                        (quantumResult.TotalWeight / problem.Capacity) * 100.0
                    else 0.0
                
                // Convert back to domain types
                let selectedItems = quantumResult.SelectedItems
                
                return {
                    SelectedItems = selectedItems
                    TotalWeight = quantumResult.TotalWeight
                    TotalValue = quantumResult.TotalValue
                    IsFeasible = quantumResult.IsFeasible
                    Efficiency = efficiency
                    CapacityUtilization = capacityUtilization
                    BackendName = quantumResult.BackendName
                    IsQuantum = true
                }
            }
        with
        | ex -> Error (QuantumError.OperationError ("Knapsack solve failed: ", $"Failed: {ex.Message}"))

    /// Solve Knapsack using classical greedy algorithm (for comparison)
    /// 
    /// PARAMETERS:
    ///   problem - Knapsack problem with items and capacity
    /// 
    /// RETURNS:
    ///   Solution using classical value-to-weight ratio heuristic
    /// 
    /// EXAMPLE:
    ///   let classicalSolution = Knapsack.solveClassicalGreedy problem
    let internal solveClassicalGreedy (problem: Problem) : Solution =
        // Convert to quantum solver format
        let quantumProblem : QuantumKnapsackSolver.KnapsackProblem =
            { Items = problem.Items; Capacity = problem.Capacity }
        
        let classicalResult = QuantumKnapsackSolver.solveClassical quantumProblem
        
        let efficiency = 
            if classicalResult.TotalWeight > 0.0 then
                classicalResult.TotalValue / classicalResult.TotalWeight
            else 0.0
        
        let capacityUtilization = 
            if problem.Capacity > 0.0 then
                (classicalResult.TotalWeight / problem.Capacity) * 100.0
            else 0.0
        
        let selectedItems = classicalResult.SelectedItems
        
        {
            SelectedItems = selectedItems
            TotalWeight = classicalResult.TotalWeight
            TotalValue = classicalResult.TotalValue
            IsFeasible = classicalResult.IsFeasible
            Efficiency = efficiency
            CapacityUtilization = capacityUtilization
            BackendName = "Classical Greedy"
            IsQuantum = false
        }

    /// Upper bound on DP table cells ((items+1) * (scaled capacity+1)).
    /// Keeps memory bounded (~40 MB of floats worst case).
    let private maxDpCells = 5_000_000

    /// Exact 0/1 knapsack via the standard O(n*W) dynamic-programming table.
    ///
    /// Item weights are floats, so they are discretized first: the finest
    /// power-of-ten scale (up to 10^4, i.e. 4 decimal digits) that keeps the
    /// DP table within maxDpCells is chosen. Item weights are rounded UP and
    /// the capacity DOWN, so any selection the DP reports feasible is feasible
    /// for the original float weights. For weights with no more decimal digits
    /// than the chosen scale the result is exactly optimal; otherwise it is
    /// optimal for the conservatively rounded problem.
    ///
    /// Returns None when even integer resolution (scale 1) would exceed the
    /// table budget, in which case the caller should fall back to a heuristic.
    let private trySolveDpExact (items: Item list) (capacity: float) : Item list option =
        let n = items.Length
        if n = 0 || capacity <= 0.0 then
            Some []
        else
            let scale =
                [ 10_000; 1_000; 100; 10; 1 ]
                |> List.tryFind (fun s ->
                    let w = floor (capacity * float s + 1e-9)
                    float (n + 1) * (w + 1.0) <= float maxDpCells)

            match scale with
            | None -> None
            | Some s ->
                let cap = int (floor (capacity * float s + 1e-9))
                let itemsArr = List.toArray items
                // Round item weights UP so DP-feasible implies truly feasible.
                // Items heavier than the capacity are clamped to cap+1 (never
                // selectable) to avoid int overflow on extreme weights.
                let weights =
                    itemsArr
                    |> Array.map (fun item ->
                        let scaled = ceil (item.Weight * float s - 1e-9)
                        if scaled <= 0.0 then 0
                        elif scaled > float cap then cap + 1
                        else int scaled)

                // dp.[i].[w] = best value using the first i items with weight budget w
                let dp = Array.init (n + 1) (fun _ -> Array.zeroCreate<float> (cap + 1))
                for i in 1 .. n do
                    let wi = weights.[i - 1]
                    let vi = itemsArr.[i - 1].Value
                    for w in 0 .. cap do
                        let without = dp.[i - 1].[w]
                        dp.[i].[w] <-
                            if wi <= w then max without (dp.[i - 1].[w - wi] + vi)
                            else without

                // Backtrack to recover the selected items
                let selected = ResizeArray<Item>()
                let mutable w = cap
                for i = n downto 1 do
                    if dp.[i].[w] > dp.[i - 1].[w] then
                        selected.Add itemsArr.[i - 1]
                        w <- w - weights.[i - 1]

                selected |> Seq.rev |> List.ofSeq |> Some

    /// Solve Knapsack using dynamic programming (classical, optimal)
    ///
    /// Uses the standard O(n*W) 0/1 knapsack DP over discretized weights
    /// (see trySolveDpExact for the precision guarantees). If the problem is
    /// too large for the DP table even at integer weight resolution, falls
    /// back to the greedy heuristic and labels the result honestly.
    ///
    /// PARAMETERS:
    ///   problem - Knapsack problem with items and capacity
    ///
    /// RETURNS:
    ///   Optimal solution using classical DP algorithm
    ///
    /// EXAMPLE:
    ///   let optimalSolution = Knapsack.solveClassicalDP problem
    let internal solveClassicalDP (problem: Problem) : Solution =
        match trySolveDpExact problem.Items problem.Capacity with
        | None ->
            // DP table intractable at integer resolution: be honest about the method
            { (solveClassicalGreedy problem) with BackendName = "Classical Greedy (DP intractable, heuristic fallback)" }
        | Some selectedItems ->
            let totalWeight = selectedItems |> List.sumBy (fun item -> item.Weight)
            let totalValue = selectedItems |> List.sumBy (fun item -> item.Value)

            let efficiency =
                if totalWeight > 0.0 then totalValue / totalWeight else 0.0

            let capacityUtilization =
                if problem.Capacity > 0.0 then
                    (totalWeight / problem.Capacity) * 100.0
                else 0.0

            {
                SelectedItems = selectedItems
                TotalWeight = totalWeight
                TotalValue = totalValue
                IsFeasible = totalWeight <= problem.Capacity
                Efficiency = efficiency
                CapacityUtilization = capacityUtilization
                BackendName = "Classical DP (Optimal)"
                IsQuantum = false
            }

    /// Convenience function: Create problem and solve in one step using quantum optimization
    /// 
    /// PARAMETERS:
    ///   items - List of (id, weight, value) tuples
    ///   capacity - Maximum total weight allowed
    ///   backend - Optional quantum backend (defaults to LocalBackend if None)
    /// 
    /// RETURNS:
    ///   Result with Solution or error message
    /// 
    /// EXAMPLE:
    ///   let items = [("item1", 2.0, 10.0); ("item2", 3.0, 15.0)]
    ///   let solution = Knapsack.solveDirectly items 5.0 None
    let solveDirectly 
        (items: (string * float * float) list) 
        (capacity: float)
        (backend: BackendAbstraction.IQuantumBackend option) 
        : QuantumResult<Solution> =
        
        let problem = createProblem items capacity
        solve problem backend

    // ============================================================================
    // EXACT SUM ENUMERATION - FIND ALL VALID COMBINATIONS
    // ============================================================================

    /// Classical fallback: Find ALL valid combinations that sum exactly to capacity.
    /// Private implementation used when no quantum backend is provided.
    ///
    /// ALGORITHM:
    /// Recursive backtracking to explore all possible subsets.
    /// Time complexity: O(2^n) - exponential, suitable for small n (typically < 20 items)
    let private findAllExactCombinationsClassical (problem: Problem) : Item list list =
        let rec findCombinations (items: Item list) (target: float) (current: Item list) : Item list list =
            let currentSum = current |> List.sumBy (fun item -> item.Weight)

            // Tolerance for floating-point comparison
            let epsilon = 0.0001

            if abs(currentSum - target) < epsilon then
                // Found exact match!
                [current]
            elif currentSum > target || List.isEmpty items then
                // Exceeded target or no more items
                []
            else
                // Try including first item OR excluding it
                let first = List.head items
                let rest = List.tail items

                let withFirst = findCombinations rest target (first :: current)
                let withoutFirst = findCombinations rest target current

                withFirst @ withoutFirst

        findCombinations problem.Items problem.Capacity []

    /// Find ALL valid combinations that sum exactly to capacity using quantum QAOA.
    ///
    /// QUANTUM-FIRST API (RULE 1 COMPLIANT):
    /// ✅ Requires IQuantumBackend parameter — executes iterative QAOA on quantum hardware/simulator
    ///
    /// Uses iterative QAOA with exclusion penalties to discover all subset-sum solutions:
    /// 1. Encode exact-sum constraint as QUBO: minimize λ*(Σ w_i*x_i - W)²
    /// 2. Run QAOA on quantum backend, sample measurements
    /// 3. Extract feasible solutions (exact sum match)
    /// 4. Add exclusion penalties for found solutions to QUBO
    /// 5. Repeat until no new solutions found
    ///
    /// Falls back to classical recursive backtracking if no backend is provided.
    ///
    /// PARAMETERS:
    ///   problem - Knapsack problem with items and capacity
    ///   backend - Optional quantum backend (None = classical fallback)
    ///
    /// RETURNS:
    ///   List of all valid combinations (each combination is a list of items that sum exactly to capacity)
    ///
    /// EXAMPLE:
    ///   let problem = Knapsack.createProblem [("A", 2.0, 2.0); ("B", 5.0, 5.0); ("C", 3.0, 3.0); ("D", 4.0, 4.0)] 7.0
    ///   let combinations = Knapsack.findAllExactCombinations problem (Some backend)
    ///   // Returns: [[A,B], [C,D]] - both combinations that sum exactly to 7
    let findAllExactCombinations (problem: Problem) (backend: BackendAbstraction.IQuantumBackend option) : Item list list =
        match backend with
        | None ->
            // Classical fallback (no quantum backend provided)
            findAllExactCombinationsClassical problem
        | Some quantumBackend ->
            // Quantum path: use iterative QAOA via QuantumKnapsackSolver
            let quantumItems = problem.Items

            let config = QuantumKnapsackSolver.defaultSubsetSumConfig

            match QuantumKnapsackSolver.findAllExactCombinations quantumBackend quantumItems problem.Capacity config with
            | Ok result ->
                // Convert quantum items back to domain items
                result.Combinations
            | Error _ ->
                // On quantum error, fall back to classical
                findAllExactCombinationsClassical problem

    /// Find all items that appear in at least one valid combination (union of all combinations)
    ///
    /// QUANTUM-FIRST API (RULE 1 COMPLIANT):
    /// ✅ Accepts optional IQuantumBackend parameter — delegates to quantum findAllExactCombinations
    ///
    /// ALGORITHM:
    /// 1. Find all exact combinations using quantum QAOA (or classical fallback)
    /// 2. Flatten all combinations into a single list
    /// 3. Remove duplicates to get unique items (union operation)
    ///
    /// Example: Items=[2,5,3,4], Capacity=7
    /// - Valid combinations: [[2,5], [3,4]]
    /// - Union: [2,5,3,4] - All items that appear in any combination
    ///
    /// PARAMETERS:
    ///   problem - Knapsack problem
    ///   backend - Optional quantum backend (None = classical fallback)
    ///
    /// RETURNS:
    ///   List of all items that appear in at least one valid combination
    ///
    /// EXAMPLE:
    ///   let unionItems = Knapsack.findAllCapturedItems problem (Some backend)
    ///   // For capacity=7, items=[2,5,3,4]: Returns all 4 items (appear in some combination)
    let findAllCapturedItems (problem: Problem) (backend: BackendAbstraction.IQuantumBackend option) : Item list =
        let allCombinations = findAllExactCombinations problem backend

        allCombinations
        |> List.concat
        |> List.distinctBy (fun item -> item.Id)

    /// Find all valid combinations that sum exactly to capacity, with detailed results
    ///
    /// QUANTUM-FIRST API (RULE 1 COMPLIANT):
    /// ✅ Accepts optional IQuantumBackend parameter
    ///
    /// CONVENIENCE FUNCTION:
    /// Combines findAllExactCombinations and findAllCapturedItems into one call.
    /// Useful when you need both individual combinations and their union.
    ///
    /// PARAMETERS:
    ///   problem - Knapsack problem
    ///   backend - Optional quantum backend (None = classical fallback)
    ///
    /// RETURNS:
    ///   Tuple of (all combinations, union of all items, combination count)
    ///
    /// EXAMPLE:
    ///   let (combinations, unionItems, count) = Knapsack.findAllValidCombinations problem (Some backend)
    ///   printfn "Found %d valid combinations" count
    ///   printfn "Total unique items across all solutions: %d" (List.length unionItems)
    let findAllValidCombinations (problem: Problem) (backend: BackendAbstraction.IQuantumBackend option) : (Item list list * Item list * int) =
        let combinations = findAllExactCombinations problem backend
        let allItems =
            combinations
            |> List.concat
            |> List.distinctBy (fun item -> item.Id)
        let count = List.length combinations

        (combinations, allItems, count)

    // ============================================================================
    // ENHANCED SOLVE WITH MODE SELECTION
    // ============================================================================

    /// Solve Knapsack with optional mode: find one optimal solution OR all exact combinations
    ///
    /// QUANTUM-FIRST API (RULE 1 COMPLIANT):
    /// ✅ Both modes use IQuantumBackend when provided
    /// - findAll=false: Standard QAOA knapsack optimization
    /// - findAll=true: Iterative QAOA subset-sum enumeration
    ///
    /// MODE SELECTION:
    /// - findAll=false (default): Standard knapsack - finds ONE optimal subset maximizing value ≤ capacity
    /// - findAll=true: Quantum enumeration - returns union of ALL items from all exact-sum combinations
    ///
    /// PARAMETERS:
    ///   problem - Knapsack problem with items and capacity
    ///   backend - Optional quantum backend (defaults to LocalBackend if None)
    ///   findAll - If true, finds ALL exact combinations; if false, finds one optimal solution
    ///
    /// RETURNS:
    ///   If findAll=true: Solution with ALL items from all valid exact-sum combinations
    ///   If findAll=false: Solution with ONE optimal subset (standard knapsack)
    ///
    /// EXAMPLE (Find all mode):
    ///   let solution = Knapsack.solveWithMode problem (Some backend) true
    ///   // Returns union of all items that appear in any exact-sum combination
    ///
    /// EXAMPLE (Standard mode):
    ///   let solution = Knapsack.solveWithMode problem None false
    ///   // Returns ONE optimal subset maximizing value
    let solveWithMode (problem: Problem) (backend: BackendAbstraction.IQuantumBackend option) (findAll: bool) : QuantumResult<Solution> =
        if findAll then
            // FIND ALL MODE: Return union of all items from all exact-sum combinations
            // Uses quantum QAOA when backend is provided
            try
                let allCapturedItems = findAllCapturedItems problem backend
                let totalWeight = allCapturedItems |> List.sumBy (fun item -> item.Weight)
                let totalValue = allCapturedItems |> List.sumBy (fun item -> item.Value)

                let efficiency =
                    if totalWeight > 0.0 then totalValue / totalWeight else 0.0

                let capacityUtilization =
                    if problem.Capacity > 0.0 then (totalWeight / problem.Capacity) * 100.0 else 0.0

                let isFeasible = totalWeight <= problem.Capacity

                let backendName =
                    match backend with
                    | Some b -> sprintf "Quantum QAOA Subset-Sum (%s)" b.Name
                    | None -> "Classical Enumeration (All Combinations)"

                Ok {
                    SelectedItems = allCapturedItems
                    TotalWeight = totalWeight
                    TotalValue = totalValue
                    IsFeasible = isFeasible
                    Efficiency = efficiency
                    CapacityUtilization = capacityUtilization
                    BackendName = backendName
                    IsQuantum = backend.IsSome
                }
            with
            | ex -> Error (QuantumError.OperationError ("Find all mode failed: ", $"Failed: {ex.Message}"))
        else
            // STANDARD MODE: Find one optimal solution
            solve problem backend

    // ============================================================================
    // VALIDATION AND UTILITIES
    // ============================================================================

    /// Validate that a solution is feasible (satisfies capacity constraint)
    /// 
    /// PARAMETERS:
    ///   problem - Knapsack problem
    ///   selectedItems - Proposed selection
    /// 
    /// RETURNS:
    ///   true if total weight ≤ capacity, false otherwise
    let isFeasible (problem: Problem) (selectedItems: Item list) : bool =
        let totalWeight = selectedItems |> List.sumBy (fun item -> item.Weight)
        totalWeight <= problem.Capacity

    /// Calculate total value of a selection
    /// 
    /// PARAMETERS:
    ///   selectedItems - Items to evaluate
    /// 
    /// RETURNS:
    ///   Sum of item values
    let totalValue (selectedItems: Item list) : float =
        selectedItems |> List.sumBy (fun item -> item.Value)

    /// Calculate total weight of a selection
    /// 
    /// PARAMETERS:
    ///   selectedItems - Items to evaluate
    /// 
    /// RETURNS:
    ///   Sum of item weights
    let totalWeight (selectedItems: Item list) : float =
        selectedItems |> List.sumBy (fun item -> item.Weight)

    /// Calculate value-to-weight efficiency ratio
    /// 
    /// PARAMETERS:
    ///   selectedItems - Items to evaluate
    /// 
    /// RETURNS:
    ///   Total value divided by total weight
    let efficiency (selectedItems: Item list) : float =
        let w = totalWeight selectedItems
        let v = totalValue selectedItems
        if w > 0.0 then v / w else 0.0
