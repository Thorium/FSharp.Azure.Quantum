namespace FSharp.Azure.Quantum

/// Generic Subset Selection Framework for Knapsack, Subset Sum, Portfolio Optimization, and Set Cover problems.
/// 
/// Provides fluent builder API for composing subset selection problems with multi-dimensional weights,
/// constraints, and objectives. Supports both quantum (QUBO) and classical (DP, greedy) solvers.
module SubsetSelection =
    
    // ============================================================================
    // CORE TYPES - Domain Model
    // ============================================================================
    
    /// Represents an item with multi-dimensional weights for subset selection.
    type Item<'T when 'T : equality> = {
        /// Unique item identifier
        Id: string
        
        /// Item value (business data)
        Value: 'T
        
        /// Multi-dimensional weights (e.g., "weight" -> 3.0, "value" -> 1000.0)
        Weights: Map<string, float>
        
        /// Custom metadata for domain-specific data
        Metadata: Map<string, obj>
    }
    
    // ============================================================================
    // HELPER FUNCTIONS - Item Creation
    // ============================================================================
    
    /// Create an item with multi-dimensional weights.
    /// Example: itemMulti "laptop" "Laptop" ["weight", 3.0; "value", 1000.0]
    let itemMulti (id: string) (value: 'T) (weights: (string * float) list) : Item<'T> =
        {
            Id = id
            Value = value
            Weights = Map.ofList weights
            Metadata = Map.empty
        }
    
    /// Create an item with a single dimension.
    /// Example: item "item1" 5.0 "value" 5.0
    let item (id: string) (value: 'T) (dimension: string) (weight: float) : Item<'T> =
        itemMulti id value [dimension, weight]
    
    /// Create a numeric item (value is also the single weight).
    /// Example: numericItem "num1" 5.0
    let numericItem (id: string) (value: float) : Item<float> =
        item id value "value" value
    
    // ============================================================================
    // CONSTRAINT TYPES - Subset Selection Constraints
    // ============================================================================
    
    /// Subset selection constraints defining rules for valid selections.
    [<NoComparison; NoEquality>]
    type SelectionConstraint =
        /// Exact target for a dimension (Subset Sum)
        | ExactTarget of dimension: string * target: float
        
        /// Maximum limit for a dimension (Knapsack capacity)
        | MaxLimit of dimension: string * limit: float
        
        /// Minimum limit for a dimension
        | MinLimit of dimension: string * limit: float
        
        /// Range constraint for a dimension
        | Range of dimension: string * min: float * max: float
        
        /// Custom constraint function (for domain-specific rules)
        | Custom of (obj list -> bool)
    
    // ============================================================================
    // OBJECTIVE TYPES - Optimization Goals
    // ============================================================================
    
    /// Subset selection objectives defining optimization criteria.
    [<NoComparison; NoEquality>]
    type SelectionObjective =
        /// Minimize total weight in a dimension
        | MinimizeWeight of dimension: string
        
        /// Maximize total weight in a dimension (Knapsack value)
        | MaximizeWeight of dimension: string
        
        /// Minimize number of items selected (Set Cover)
        | MinimizeCount
        
        /// Maximize number of items selected
        | MaximizeCount
        
        /// Custom objective function (for domain-specific goals)
        | CustomObjective of (obj list -> float)
    
    // ============================================================================
    // PROBLEM DEFINITION - Subset Selection Problem
    // ============================================================================
    
    /// Complete subset selection problem definition.
    type SubsetSelectionProblem<'T when 'T : equality> = {
        /// Items to select from
        Items: Item<'T> list
        
        /// Selection constraints
        Constraints: SelectionConstraint list
        
        /// Optimization objective
        Objective: SelectionObjective
    }
    
    // ============================================================================
    // SOLUTION TYPES - Subset Selection Solution
    // ============================================================================
    
    /// Solution to a subset selection problem.
    type SubsetSelectionSolution<'T when 'T : equality> = {
        /// Selected items
        SelectedItems: Item<'T> list
        
        /// Total weights per dimension
        TotalWeights: Map<string, float>
        
        /// Objective value achieved
        ObjectiveValue: float
        
        /// Whether solution satisfies all constraints
        IsFeasible: bool
        
        /// Constraint violations (if any)
        Violations: string list
    }
    
    // ============================================================================
    // FLUENT BUILDER - Subset Selection Problem Construction
    // ============================================================================
    
    /// Fluent builder for composing subset selection problems with method chaining.
    /// Uses immutable record pattern for thread-safety and functional composition.
    /// 
    /// Example:
    /// ```fsharp
    /// let problem =
    ///     SubsetSelectionBuilder.Create()
    ///         .Items([item1; item2; item3])
    ///         .AddConstraint(MaxLimit("weight", 10.0))
    ///         .Objective(MaximizeWeight("value"))
    ///         .Build()
    /// ```
    type SubsetSelectionBuilder<'T when 'T : equality> = private {
        items: Item<'T> list
        constraints: SelectionConstraint list
        objective: SelectionObjective
    } with
        /// Create a new builder with default values
        static member Create() : SubsetSelectionBuilder<'T> = {
            items = []
            constraints = []
            objective = MaximizeCount  // default objective
        }
        
        /// Fluent API: Set items to select from
        member this.Items(itemList: Item<'T> list) =
            { this with items = itemList }
        
        /// Fluent API: Add a selection constraint
        member this.AddConstraint(constraint: SelectionConstraint) =
            { this with constraints = constraint :: this.constraints }
        
        /// Fluent API: Set optimization objective
        member this.Objective(obj: SelectionObjective) =
            { this with objective = obj }
        
        /// Build the subset selection problem
        member this.Build() : SubsetSelectionProblem<'T> =
            {
                Items = this.items
                Constraints = List.rev this.constraints  // reverse to maintain insertion order
                Objective = this.objective
            }
    
    // ============================================================================
    // CLASSICAL SOLVERS - Dynamic Programming & Greedy Algorithms
    // ============================================================================
    
    /// Solve 0/1 Knapsack problem using dynamic programming.
    /// 
    /// Classical DP algorithm with O(n * W) time complexity where n = items, W = capacity.
    /// Solves exact optimal solution for single-constraint knapsack problems.
    /// 
    /// Parameters:
    ///   - problem: Subset selection problem with MaxLimit constraint
    ///   - weightDim: Dimension name for capacity constraint (e.g., "weight")
    ///   - valueDim: Dimension name for objective to maximize (e.g., "value")
    /// 
    /// Returns:
    ///   - Ok solution with optimal item selection
    ///   - Error message if problem is infeasible or invalid
    let solveKnapsack (problem: SubsetSelectionProblem<'T>) (weightDim: string) (valueDim: string) 
        : Result<SubsetSelectionSolution<'T>, string> =
        
        // Extract capacity from MaxLimit constraint
        let capacityOpt =
            problem.Constraints
            |> List.tryPick (function
                | MaxLimit(dim, limit) when dim = weightDim -> Some limit
                | _ -> None)
        
        match capacityOpt with
        | None -> Error $"No MaxLimit constraint found for dimension '{weightDim}'"
        | Some capacity ->
            
            // Convert items to arrays for DP indexing
            let items = problem.Items |> List.toArray
            let n = items.Length
            
            // Scale capacity to integer for DP (multiply by 10 to handle 1 decimal place)
            let scaleFactor = 10.0
            let W = int (capacity * scaleFactor)
            
            // Extract weights and values (scaled to integers)
            let weights =
                items
                |> Array.map (fun item ->
                    match item.Weights.TryFind weightDim with
                    | Some w -> int (w * scaleFactor)
                    | None -> 0)
            
            let values =
                items
                |> Array.map (fun item ->
                    match item.Weights.TryFind valueDim with
                    | Some v -> int (v * scaleFactor)  // scale values too for precision
                    | None -> 0)
            
            // DP table: dp.[i, w] = max value using first i items with capacity w
            let dp = Array2D.create (n + 1) (W + 1) 0
            
            // Fill DP table
            for i in 1 .. n do
                for w in 0 .. W do
                    let itemIdx = i - 1
                    let itemWeight = weights.[itemIdx]
                    let itemValue = values.[itemIdx]
                    
                    if itemWeight <= w then
                        // Can include this item - take max of (include, exclude)
                        dp.[i, w] <- max (dp.[i - 1, w]) (dp.[i - 1, w - itemWeight] + itemValue)
                    else
                        // Cannot include - carry forward previous best
                        dp.[i, w] <- dp.[i - 1, w]
            
            // Backtrack to find selected items
            let rec backtrack i w selected =
                if i = 0 || w = 0 then
                    selected
                else
                    let itemIdx = i - 1
                    let itemWeight = weights.[itemIdx]
                    let itemValue = values.[itemIdx]
                    
                    // Check if this item was included
                    if itemWeight <= w && dp.[i, w] = dp.[i - 1, w - itemWeight] + itemValue then
                        // Item was included
                        backtrack (i - 1) (w - itemWeight) (items.[itemIdx] :: selected)
                    else
                        // Item was not included
                        backtrack (i - 1) w selected
            
            let selectedItems = backtrack n W []
            
            // Calculate total weights per dimension
            let totalWeights =
                selectedItems
                |> List.fold (fun acc item ->
                    item.Weights
                    |> Map.fold (fun acc2 dim weight ->
                        let current = Map.tryFind dim acc2 |> Option.defaultValue 0.0
                        Map.add dim (current + weight) acc2
                    ) acc
                ) Map.empty
            
            // Calculate objective value
            let objectiveValue = 
                totalWeights.TryFind valueDim |> Option.defaultValue 0.0
            
            // Check feasibility (weight constraint)
            let actualWeight = totalWeights.TryFind weightDim |> Option.defaultValue 0.0
            let isFeasible = actualWeight <= capacity
            
            let violations =
                if not isFeasible then
                    [$"Weight constraint violated: {actualWeight} > {capacity}"]
                else
                    []
            
            Ok {
                SelectedItems = selectedItems
                TotalWeights = totalWeights
                ObjectiveValue = objectiveValue
                IsFeasible = isFeasible
                Violations = violations
            }
    
    // ============================================================================
    // QUBO ENCODING - Quantum Solver Integration
    // ============================================================================
    
    /// QUBO (Quadratic Unconstrained Binary Optimization) representation
    type QuboMatrix = {
        NumVars: int
        Q: Map<(int * int), float>  // Sparse matrix representation: (i,j) -> coefficient
    }
    
    /// Create empty QUBO matrix
    let private emptyQubo numVars = {
        NumVars = numVars
        Q = Map.empty
    }
    
    /// Default penalty weight for constraint violations
    let private DefaultPenalty = 10.0
    
    /// Encode Subset Selection problem to QUBO format for quantum/annealing solvers.
    /// 
    /// QUBO formulation for 0/1 Knapsack:
    ///   Variables: x_i ∈ {0,1} for each item i (1 = selected, 0 = not selected)
    ///   
    ///   Objective (maximize value):
    ///     Minimize: -Σ_i (value_i * x_i)  [negative for maximization]
    ///   
    ///   Constraint (capacity limit):
    ///     Penalty: P * (Σ_i (weight_i * x_i) - capacity)²
    ///     Expanded: P * [Σ_i (weight_i² * x_i) + 2 * Σ_{i<j} (weight_i * weight_j * x_i * x_j) 
    ///                     - 2 * capacity * Σ_i (weight_i * x_i) + capacity²]
    ///   
    ///   Since x_i² = x_i (binary), linear terms go on diagonal, quadratic terms off-diagonal.
    /// 
    /// Parameters:
    ///   - problem: Subset selection problem with MaxLimit constraint
    ///   - weightDim: Dimension name for capacity constraint (e.g., "weight")
    ///   - valueDim: Dimension name for objective to maximize (e.g., "value")
    /// 
    /// Returns:
    ///   - Ok QuboMatrix with encoded problem
    ///   - Error message if problem cannot be encoded
    let toQubo (problem: SubsetSelectionProblem<'T>) (weightDim: string) (valueDim: string)
        : Result<QuboMatrix, string> =
        
        // Extract capacity from MaxLimit constraint
        let capacityOpt =
            problem.Constraints
            |> List.tryPick (function
                | MaxLimit(dim, limit) when dim = weightDim -> Some limit
                | _ -> None)
        
        match capacityOpt with
        | None -> Error $"No MaxLimit constraint found for dimension '{weightDim}'"
        | Some capacity ->
            
            let items = problem.Items |> List.toArray
            let n = items.Length
            
            // Extract weights and values for each item
            let weights =
                items
                |> Array.map (fun item ->
                    item.Weights.TryFind weightDim |> Option.defaultValue 0.0)
            
            let values =
                items
                |> Array.map (fun item ->
                    item.Weights.TryFind valueDim |> Option.defaultValue 0.0)
            
            // Initialize QUBO matrix
            let mutable qubo = emptyQubo n
            
            // Helper: Add term to QUBO matrix
            let addTerm i j coeff =
                let key = if i <= j then (i, j) else (j, i)  // Canonical ordering
                let current = Map.tryFind key qubo.Q |> Option.defaultValue 0.0
                qubo <- { qubo with Q = Map.add key (current + coeff) qubo.Q }
            
            // OBJECTIVE: Maximize value = Minimize negative value
            // Add linear terms: -value_i * x_i (diagonal entries)
            for i in 0 .. n - 1 do
                addTerm i i (-values.[i])
            
            // CONSTRAINT: Capacity limit with penalty
            // Penalty term: P * (Σ weight_i * x_i - capacity)²
            
            // Expanded form:
            // P * [Σ weight_i² * x_i + 2 * Σ_{i<j} weight_i * weight_j * x_i * x_j 
            //      - 2 * capacity * Σ weight_i * x_i + capacity²]
            
            // Linear terms (diagonal): P * (weight_i² - 2*capacity*weight_i) * x_i
            for i in 0 .. n - 1 do
                let w_i = weights.[i]
                let linearCoeff = DefaultPenalty * (w_i * w_i - 2.0 * capacity * w_i)
                addTerm i i linearCoeff
            
            // Quadratic terms (off-diagonal): 2 * P * weight_i * weight_j * x_i * x_j
            for i in 0 .. n - 1 do
                for j in i + 1 .. n - 1 do
                    let w_i = weights.[i]
                    let w_j = weights.[j]
                    let quadraticCoeff = 2.0 * DefaultPenalty * w_i * w_j
                    addTerm i j quadraticCoeff
            
            // Constant term (capacity²) doesn't affect optimization, so we omit it
            
            Ok qubo
    
    /// Decode QUBO solution back to selected items
    /// 
    /// Parameters:
    ///   problem - The original SubsetSelectionProblem that was encoded to QUBO
    ///   solution - Map from variable index to value (0.0 or 1.0)
    /// 
    /// Returns:
    ///   Result containing list of selected items or error message
    let fromQubo (problem: SubsetSelectionProblem<'T>) (solution: Map<int, float>) : Result<Item<'T> list, string> =
        try
            // Extract selected items where solution variable = 1.0
            let selected =
                solution
                |> Map.toList
                |> List.filter (fun (_, value) -> value > 0.5)  // Treat values > 0.5 as "selected"
                |> List.choose (fun (idx, _) ->
                    if idx >= 0 && idx < problem.Items.Length then
                        Some problem.Items.[idx]
                    else
                        None)
            
            Ok selected
        with ex ->
            Error $"Failed to decode QUBO solution: {ex.Message}"
