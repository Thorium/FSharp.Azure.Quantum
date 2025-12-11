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

    /// Knapsack Item with weight and value
    type Item = {
        /// Item identifier/name
        Id: string
        
        /// Item weight (consumes capacity)
        Weight: float
        
        /// Item value (benefit/profit)
        Value: float
    }

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
        let itemList = 
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
            let quantumProblem : QuantumKnapsackSolver.KnapsackProblem = {
                Items = 
                    problem.Items 
                    |> List.map (fun item -> 
                        { 
                            QuantumKnapsackSolver.KnapsackItem.Id = item.Id
                            Weight = item.Weight
                            Value = item.Value 
                        })
                Capacity = problem.Capacity
            }
            
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
                let selectedItems = 
                    quantumResult.SelectedItems 
                    |> List.map (fun qItem -> 
                        { Id = qItem.Id; Weight = qItem.Weight; Value = qItem.Value })
                
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
    let solveClassicalGreedy (problem: Problem) : Solution =
        // Convert to quantum solver format
        let quantumProblem : QuantumKnapsackSolver.KnapsackProblem = {
            Items = 
                problem.Items 
                |> List.map (fun item -> 
                    { 
                        QuantumKnapsackSolver.KnapsackItem.Id = item.Id
                        Weight = item.Weight
                        Value = item.Value 
                    })
            Capacity = problem.Capacity
        }
        
        let classicalResult = QuantumKnapsackSolver.solveClassical quantumProblem
        
        let efficiency = 
            if classicalResult.TotalWeight > 0.0 then
                classicalResult.TotalValue / classicalResult.TotalWeight
            else 0.0
        
        let capacityUtilization = 
            if problem.Capacity > 0.0 then
                (classicalResult.TotalWeight / problem.Capacity) * 100.0
            else 0.0
        
        let selectedItems = 
            classicalResult.SelectedItems 
            |> List.map (fun qItem -> 
                { Id = qItem.Id; Weight = qItem.Weight; Value = qItem.Value })
        
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

    /// Solve Knapsack using dynamic programming (classical, optimal)
    /// 
    /// PARAMETERS:
    ///   problem - Knapsack problem with items and capacity
    /// 
    /// RETURNS:
    ///   Optimal solution using classical DP algorithm
    /// 
    /// EXAMPLE:
    ///   let optimalSolution = Knapsack.solveClassicalDP problem
    let solveClassicalDP (problem: Problem) : Solution =
        // Convert to quantum solver format
        let quantumProblem : QuantumKnapsackSolver.KnapsackProblem = {
            Items = 
                problem.Items 
                |> List.map (fun item -> 
                    { 
                        QuantumKnapsackSolver.KnapsackItem.Id = item.Id
                        Weight = item.Weight
                        Value = item.Value 
                    })
            Capacity = problem.Capacity
        }
        
        let dpResult = QuantumKnapsackSolver.solveClassicalDP quantumProblem
        
        let efficiency = 
            if dpResult.TotalWeight > 0.0 then
                dpResult.TotalValue / dpResult.TotalWeight
            else 0.0
        
        let capacityUtilization = 
            if problem.Capacity > 0.0 then
                (dpResult.TotalWeight / problem.Capacity) * 100.0
            else 0.0
        
        let selectedItems = 
            dpResult.SelectedItems 
            |> List.map (fun qItem -> 
                { Id = qItem.Id; Weight = qItem.Weight; Value = qItem.Value })
        
        {
            SelectedItems = selectedItems
            TotalWeight = dpResult.TotalWeight
            TotalValue = dpResult.TotalValue
            IsFeasible = dpResult.IsFeasible
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

    /// Find ALL valid combinations that sum exactly to capacity
    /// 
    /// USE CASES:
    /// - Subset sum enumeration: Find all solutions, not just one optimal
    /// - Exact constraint satisfaction: Problems requiring exact match (not ≤)
    /// - Combinatorial analysis: Count valid combinations
    /// - Games and puzzles: Card games, resource allocation with exact constraints
    /// 
    /// ALGORITHM:
    /// Recursive backtracking to explore all possible subsets.
    /// Time complexity: O(2^n) - exponential, suitable for small n (typically < 20 items)
    /// 
    /// Example: Items=[2,5,3,4], Capacity=7
    /// - Combination 1: [2,5] sum=7
    /// - Combination 2: [3,4] sum=7
    /// - Returns: [[2,5], [3,4]]
    /// 
    /// PARAMETERS:
    ///   problem - Knapsack problem
    /// 
    /// RETURNS:
    ///   List of all valid combinations (each combination is a list of items that sum exactly to capacity)
    /// 
    /// EXAMPLE:
    ///   let problem = Knapsack.createProblem [("A", 2.0, 2.0); ("B", 5.0, 5.0); ("C", 3.0, 3.0); ("D", 4.0, 4.0)] 7.0
    ///   let combinations = Knapsack.findAllExactCombinations problem
    ///   // Returns: [[A,B], [C,D]] - both combinations that sum exactly to 7
    let findAllExactCombinations (problem: Problem) : Item list list =
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

    /// Find all items that appear in at least one valid combination (union of all combinations)
    /// 
    /// USE CASES:
    /// - Resource pooling: Identify all resources used across any valid solution
    /// - Game logic: Determine which items participate in any winning combination
    /// - Impact analysis: Find all items involved in feasible solutions
    /// - Set covering: Items that contribute to any exact-sum solution
    /// 
    /// ALGORITHM:
    /// 1. Find all exact combinations using findAllExactCombinations
    /// 2. Flatten all combinations into a single list
    /// 3. Remove duplicates to get unique items (union operation)
    /// 
    /// Example: Items=[2,5,3,4], Capacity=7
    /// - Valid combinations: [[2,5], [3,4]]
    /// - Union: [2,5,3,4] - All items that appear in any combination
    /// 
    /// PARAMETERS:
    ///   problem - Knapsack problem
    /// 
    /// RETURNS:
    ///   List of all items that appear in at least one valid combination
    /// 
    /// EXAMPLE:
    ///   let unionItems = Knapsack.findAllCapturedItems problem
    ///   // For capacity=7, items=[2,5,3,4]: Returns all 4 items (appear in some combination)
    let findAllCapturedItems (problem: Problem) : Item list =
        let allCombinations = findAllExactCombinations problem
        
        allCombinations
        |> List.concat
        |> List.distinctBy (fun item -> item.Id)

    /// Find all valid combinations that sum exactly to capacity, with detailed results
    /// 
    /// CONVENIENCE FUNCTION:
    /// Combines findAllExactCombinations and findAllCapturedItems into one call.
    /// Useful when you need both individual combinations and their union.
    /// 
    /// USE CASES:
    /// - Reporting: Show all solutions and summary statistics
    /// - Analysis: Compare individual solutions vs combined coverage
    /// - Game logic: Present choices and final outcome
    /// 
    /// PARAMETERS:
    ///   problem - Knapsack problem
    /// 
    /// RETURNS:
    ///   Tuple of (all combinations, union of all items, combination count)
    /// 
    /// EXAMPLE:
    ///   let (combinations, unionItems, count) = Knapsack.findAllValidCombinations problem
    ///   printfn "Found %d valid combinations" count
    ///   printfn "Total unique items across all solutions: %d" (List.length unionItems)
    let findAllValidCombinations (problem: Problem) : (Item list list * Item list * int) =
        let combinations = findAllExactCombinations problem
        let allItems = findAllCapturedItems problem
        let count = List.length combinations
        
        (combinations, allItems, count)

    // ============================================================================
    // ENHANCED SOLVE WITH MODE SELECTION
    // ============================================================================

    /// Solve Knapsack with optional mode: find one optimal solution OR all exact combinations
    /// 
    /// MODE SELECTION:
    /// - findAll=false (default): Standard knapsack - finds ONE optimal subset maximizing value ≤ capacity
    /// - findAll=true: Exact enumeration - returns union of ALL items from all exact-sum combinations
    /// 
    /// USE CASES FOR findAll=true:
    /// - Card games requiring exact matches (e.g., Kasino, Rummy)
    /// - Resource allocation where all feasible solutions contribute
    /// - Constraint satisfaction with exact sum requirement
    /// - Combinatorial analysis needing complete solution coverage
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
    ///   let solution = Knapsack.solveWithMode problem None true
    ///   // Returns union of all items that appear in any exact-sum combination
    /// 
    /// EXAMPLE (Standard mode):
    ///   let solution = Knapsack.solveWithMode problem None false
    ///   // Returns ONE optimal subset maximizing value
    let solveWithMode (problem: Problem) (backend: BackendAbstraction.IQuantumBackend option) (findAll: bool) : QuantumResult<Solution> =
        if findAll then
            // FIND ALL MODE: Return union of all items from all exact-sum combinations
            try
                let allCapturedItems = findAllCapturedItems problem
                let totalWeight = allCapturedItems |> List.sumBy (fun item -> item.Weight)
                let totalValue = allCapturedItems |> List.sumBy (fun item -> item.Value)
                
                let efficiency = 
                    if totalWeight > 0.0 then totalValue / totalWeight else 0.0
                
                let capacityUtilization = 
                    if problem.Capacity > 0.0 then (totalWeight / problem.Capacity) * 100.0 else 0.0
                
                let isFeasible = totalWeight <= problem.Capacity
                
                Ok {
                    SelectedItems = allCapturedItems
                    TotalWeight = totalWeight
                    TotalValue = totalValue
                    IsFeasible = isFeasible
                    Efficiency = efficiency
                    CapacityUtilization = capacityUtilization
                    BackendName = "Exact Enumeration (All Combinations)"
                    IsQuantum = false
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
