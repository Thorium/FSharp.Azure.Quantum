namespace FSharp.Azure.Quantum.Quantum

open System
open FSharp.Azure.Quantum
open FSharp.Azure.Quantum.Core
open FSharp.Azure.Quantum.GraphOptimization

/// Quantum Knapsack Solver using QAOA and Backend Abstraction
/// 
/// ALGORITHM-LEVEL API (for advanced users):
/// This module provides direct access to quantum 0/1 Knapsack solving via QAOA.
/// The Knapsack Problem is a fundamental combinatorial optimization problem
/// with applications in resource allocation, portfolio optimization, and scheduling.
/// 
/// RULE 1 COMPLIANCE:
/// ✅ Requires IQuantumBackend parameter (explicit quantum execution)
/// 
/// TECHNICAL DETAILS:
/// - Execution: Quantum hardware/simulator via backend
/// - Algorithm: QAOA (Quantum Approximate Optimization Algorithm)
/// - Speed: Seconds to minutes (includes job queue wait for cloud backends)
/// - Cost: ~$10-100 per run on real quantum hardware (IonQ, Rigetti)
/// - LocalBackend: Free simulation (limited to ~16 items = 16 qubits)
///
/// QUANTUM PIPELINE:
/// 1. Knapsack → QUBO Matrix (quadratic encoding with penalty for capacity)
/// 2. QUBO → QAOA Circuit (Hamiltonians + Layers)
/// 3. Execute on Quantum Backend (IonQ/Rigetti/Local)
/// 4. Decode Measurements → Item Selections
/// 5. Return Best Feasible Solution
///
/// Knapsack Problem:
///   Given items with weights w_i and values v_i, and capacity W,
///   select subset S ⊆ {1..n} to maximize:
///   
///   Value = Σ v_i * x_i  where x_i ∈ {0, 1}
///   
///   Subject to: Σ w_i * x_i ≤ W  (capacity constraint)
///
/// Example:
///   let backend = BackendAbstraction.createLocalBackend()
///   let config = { NumShots = 1000; InitialParameters = (0.5, 0.5) }
///   match QuantumKnapsackSolver.solve backend problem config with
///   | Ok solution -> printfn "Total value: %f" solution.TotalValue
///   | Error msg -> printfn "Error: %s" msg
module QuantumKnapsackSolver =

    // ================================================================================
    // PROBLEM DEFINITION
    // ================================================================================

    /// Knapsack item with weight and value
    type KnapsackItem = {
        /// Item identifier/name
        Id: string
        
        /// Item weight (consumes capacity)
        Weight: float
        
        /// Item value (objective to maximize)
        Value: float
    }
    
    /// Knapsack problem specification
    type KnapsackProblem = {
        /// Available items
        Items: KnapsackItem list
        
        /// Knapsack capacity (maximum total weight)
        Capacity: float
    }
    
    /// Knapsack solution result
    type KnapsackSolution = {
        /// Selected items
        SelectedItems: KnapsackItem list
        
        /// Total weight of selected items
        TotalWeight: float
        
        /// Total value of selected items
        TotalValue: float
        
        /// Whether solution satisfies capacity constraint
        IsFeasible: bool
        
        /// Backend used for execution
        BackendName: string
        
        /// Number of measurement shots
        NumShots: int
        
        /// Execution time in milliseconds
        ElapsedMs: float
        
        /// QUBO objective value (energy)
        BestEnergy: float
    }

    // ================================================================================
    // QUBO ENCODING FOR KNAPSACK
    // ================================================================================

    /// Convert sparse QUBO matrix (Map) to dense 2D array
    let private quboMapToArray (quboMatrix: QuboMatrix) : float[,] =
        let n = quboMatrix.NumVariables
        let dense = Array2D.zeroCreate n n
        
        for KeyValue((i, j), value) in quboMatrix.Q do
            dense.[i, j] <- value
        
        dense

    /// Encode Knapsack problem as QUBO
    /// 
    /// Knapsack QUBO formulation:
    /// 
    /// Variables: x_i ∈ {0, 1} where x_i = 1 means item i is selected
    /// 
    /// Objective (to MAXIMIZE):
    ///   Value = Σ v_i * x_i
    /// 
    /// Constraint (capacity):
    ///   Σ w_i * x_i ≤ W
    /// 
    /// QUBO form (to MINIMIZE for QAOA, so we negate and add penalty):
    ///   Minimize: -Σ v_i * x_i + λ * (Σ w_i * x_i - W)²
    /// 
    /// Where λ is a penalty weight ensuring capacity constraint is satisfied.
    /// The penalty term is 0 when constraint is met, and grows quadratically
    /// when violated.
    /// 
    /// Expanded penalty term:
    ///   (Σ w_i * x_i - W)² = (Σ w_i * x_i)² - 2W * Σ w_i * x_i + W²
    ///                      = Σ w_i² * x_i + Σ Σ 2*w_i*w_j*x_i*x_j - 2W * Σ w_i * x_i + W²
    /// 
    /// QUBO matrix Q (ignoring constant W²):
    ///   Q_ii = -v_i + λ * (w_i² - 2W*w_i)     (linear terms)
    ///   Q_ij = λ * 2*w_i*w_j for i < j        (quadratic terms)
    let toQubo (problem: KnapsackProblem) : Result<QuboMatrix, string> =
        try
            let numItems = problem.Items.Length
            
            if numItems = 0 then
                Error "Knapsack problem has no items"
            elif problem.Capacity <= 0.0 then
                Error "Knapsack capacity must be positive"
            else
                // Calculate penalty weight using Lucas Rule
                // Penalty must be large enough to dominate objective violations
                let maxValue = problem.Items |> List.map (fun item -> item.Value) |> List.max
                let totalValue = problem.Items |> List.sumBy (fun item -> item.Value)
                
                // Use shared Lucas Rule helper: penalty >> objective magnitude
                let penalty = Qubo.computeLucasPenalties (max maxValue totalValue) numItems
                
                // Build QUBO terms as Map<(int * int), float>
                let mutable quboTerms = Map.empty
                
                // Process linear terms (diagonal)
                for i in 0 .. numItems - 1 do
                    let item = problem.Items.[i]
                    let w = item.Weight
                    let v = item.Value
                    let W = problem.Capacity
                    
                    // Q_ii = -v_i + λ * (w_i² - 2W*w_i)
                    let linearTerm = -v + penalty * (w * w - 2.0 * W * w)
                    quboTerms <- quboTerms |> Map.add (i, i) linearTerm
                
                // Process quadratic terms (upper triangle)
                for i in 0 .. numItems - 1 do
                    for j in i + 1 .. numItems - 1 do
                        let w_i = problem.Items.[i].Weight
                        let w_j = problem.Items.[j].Weight
                        
                        // Q_ij = λ * 2*w_i*w_j
                        let quadraticTerm = penalty * 2.0 * w_i * w_j
                        quboTerms <- quboTerms |> Map.add (i, j) quadraticTerm
                
                Ok {
                    Q = quboTerms
                    NumVariables = numItems
                }
        with ex ->
            Error (sprintf "Knapsack QUBO encoding failed: %s" ex.Message)

    // ================================================================================
    // SOLUTION DECODING
    // ================================================================================

    /// Decode binary solution to Knapsack selection
    let private decodeSolution (problem: KnapsackProblem) (bitstring: int[]) : KnapsackSolution =
        let selectedItems = 
            problem.Items 
            |> List.mapi (fun i item -> i, item)
            |> List.filter (fun (i, _) -> bitstring.[i] = 1)
            |> List.map snd
        
        let totalWeight = selectedItems |> List.sumBy (fun item -> item.Weight)
        let totalValue = selectedItems |> List.sumBy (fun item -> item.Value)
        let isFeasible = totalWeight <= problem.Capacity
        
        {
            SelectedItems = selectedItems
            TotalWeight = totalWeight
            TotalValue = totalValue
            IsFeasible = isFeasible
            BackendName = ""
            NumShots = 0
            ElapsedMs = 0.0
            BestEnergy = -totalValue  // QUBO minimizes -value
        }

    /// Calculate solution value and feasibility
    let evaluateSolution (problem: KnapsackProblem) (selectedItems: KnapsackItem list) : float * bool =
        let totalWeight = selectedItems |> List.sumBy (fun item -> item.Weight)
        let totalValue = selectedItems |> List.sumBy (fun item -> item.Value)
        let isFeasible = totalWeight <= problem.Capacity
        
        (totalValue, isFeasible)

    // ================================================================================
    // QAOA CONFIGURATION
    // ================================================================================

    /// QAOA configuration parameters
    type QaoaConfig = {
        /// Number of measurement shots
        NumShots: int
        
        /// Initial QAOA parameters (gamma, beta) for single layer
        /// Typical values: (0.5, 0.5) or (π/4, π/2)
        InitialParameters: float * float
    }
    
    /// Default QAOA configuration for Knapsack
    let defaultConfig : QaoaConfig = {
        NumShots = 1000
        InitialParameters = (0.5, 0.5)
    }

    // ================================================================================
    // MAIN SOLVER
    // ================================================================================

    /// Solve Knapsack problem using quantum QAOA (async version)
    /// 
    /// Parameters:
    ///   - backend: Quantum backend (LocalBackend, IonQ, Rigetti)
    ///   - problem: Knapsack problem (items with weights/values, capacity)
    ///   - config: QAOA configuration (shots, initial parameters)
    /// 
    /// Returns: Async<Result<KnapsackSolution, string>> - Async computation with result or error
    /// 
    /// Example:
    ///   let backend = BackendAbstraction.createLocalBackend()
    ///   let problem = { Items = [...]; Capacity = 50.0 }
    ///   let config = { NumShots = 1000; InitialParameters = (0.5, 0.5) }
    ///   async {
    ///       match! solveAsync backend problem config with
    ///       | Ok solution -> printfn "Value: %f" solution.TotalValue
    ///       | Error msg -> printfn "Error: %s" msg
    ///   }
    let solveAsync 
        (backend: BackendAbstraction.IQuantumBackend) 
        (problem: KnapsackProblem) 
        (config: QaoaConfig) 
        : Async<Result<KnapsackSolution, string>> = async {
        
        let startTime = DateTime.Now
        
        try
            // Step 1: Validate problem size against backend
            let numQubits = problem.Items.Length
            
            if numQubits > backend.MaxQubits then
                return Error (sprintf "Problem requires %d qubits but backend '%s' supports max %d qubits" 
                    numQubits backend.Name backend.MaxQubits)
            elif numQubits = 0 then
                return Error "Knapsack problem has no items"
            elif problem.Capacity <= 0.0 then
                return Error "Knapsack capacity must be positive"
            else
                // Step 2: Encode Knapsack as QUBO
                match toQubo problem with
                | Error msg -> return Error (sprintf "Knapsack encoding failed: %s" msg)
                | Ok quboMatrix ->
                    
                    // Step 3: Convert QUBO to dense array for QAOA
                    let quboArray = quboMapToArray quboMatrix
                    
                    // Step 4: Create QAOA Hamiltonians from QUBO
                    let problemHam = QaoaCircuit.ProblemHamiltonian.fromQubo quboArray
                    let mixerHam = QaoaCircuit.MixerHamiltonian.create problemHam.NumQubits
                    
                    // Step 5: Build QAOA circuit with parameters
                    let (gamma, beta) = config.InitialParameters
                    let parameters = [| gamma, beta |]
                    let qaoaCircuit = QaoaCircuit.QaoaCircuit.build problemHam mixerHam parameters
                    
                    // Step 6: Wrap QAOA circuit for backend execution
                    let circuitWrapper = CircuitAbstraction.QaoaCircuitWrapper(qaoaCircuit) :> CircuitAbstraction.ICircuit
                    
                    // Step 7: Execute on quantum backend asynchronously
                    let! execResult = backend.ExecuteAsync circuitWrapper config.NumShots
                    
                    match execResult with
                    | Error msg -> return Error (sprintf "Backend execution failed: %s" msg)
                    | Ok execResult ->
                        
                        // Step 8: Decode measurements to selections
                        let solutions = 
                            execResult.Measurements
                            |> Array.map (fun bitstring -> decodeSolution problem bitstring)
                        
                        // Step 9: Find best FEASIBLE solution (satisfies capacity)
                        let feasibleSolutions = 
                            solutions
                            |> Array.filter (fun sol -> sol.IsFeasible)
                        
                        let bestSolution = 
                            if feasibleSolutions.Length > 0 then
                                feasibleSolutions |> Array.maxBy (fun sol -> sol.TotalValue)
                            else
                                // No feasible solution found - return empty selection
                                {
                                    SelectedItems = []
                                    TotalWeight = 0.0
                                    TotalValue = 0.0
                                    IsFeasible = true
                                    BackendName = backend.Name
                                    NumShots = config.NumShots
                                    ElapsedMs = 0.0
                                    BestEnergy = 0.0
                                }
                        
                        let elapsedMs = (DateTime.Now - startTime).TotalMilliseconds
                        
                        return Ok {
                            bestSolution with
                                BackendName = backend.Name
                                NumShots = config.NumShots
                                ElapsedMs = elapsedMs
                        }
        
        with ex ->
            return Error (sprintf "Quantum Knapsack solve failed: %s" ex.Message)
    }

    /// Solve Knapsack problem using quantum QAOA (synchronous wrapper)
    /// 
    /// This is a synchronous wrapper around solveAsync for backward compatibility.
    /// For cloud backends (IonQ, Rigetti), prefer using solveAsync directly.
    /// 
    /// Parameters:
    ///   - backend: Quantum backend (LocalBackend, IonQ, Rigetti)
    ///   - problem: Knapsack problem (items with weights/values, capacity)
    ///   - config: QAOA configuration (shots, initial parameters)
    /// 
    /// Returns: Ok with best feasible solution found, or Error with message
    /// 
    /// Example:
    ///   let backend = BackendAbstraction.createLocalBackend()
    ///   let problem = { Items = [...]; Capacity = 50.0 }
    ///   let config = { NumShots = 1000; InitialParameters = (0.5, 0.5) }
    ///   match solve backend problem config with
    ///   | Ok solution -> printfn "Value: %f" solution.TotalValue
    ///   | Error msg -> printfn "Error: %s" msg
    let solve 
        (backend: BackendAbstraction.IQuantumBackend) 
        (problem: KnapsackProblem) 
        (config: QaoaConfig) 
        : Result<KnapsackSolution, string> =
        solveAsync backend problem config |> Async.RunSynchronously

    // ================================================================================
    // CLASSICAL GREEDY SOLVER (for comparison)
    // ================================================================================

    /// Solve Knapsack using greedy value-to-weight ratio algorithm (classical)
    /// 
    /// This provides a classical baseline for comparison with quantum QAOA.
    /// Uses greedy heuristic: sort items by value/weight ratio, select until capacity full.
    /// 
    /// Typical performance: 80-90% of optimal for random instances
    let solveClassical (problem: KnapsackProblem) : KnapsackSolution =
        // Sort items by value-to-weight ratio (descending)
        let sortedItems = 
            problem.Items
            |> List.map (fun item -> item, item.Value / item.Weight)
            |> List.sortByDescending snd
            |> List.map fst
        
        // Greedy selection until capacity exceeded
        let rec selectItems remainingCapacity currentSelection items =
            match items with
            | [] -> currentSelection
            | item :: rest ->
                if item.Weight <= remainingCapacity then
                    selectItems (remainingCapacity - item.Weight) (item :: currentSelection) rest
                else
                    selectItems remainingCapacity currentSelection rest
        
        let selectedItems = selectItems problem.Capacity [] sortedItems
        let totalWeight = selectedItems |> List.sumBy (fun item -> item.Weight)
        let totalValue = selectedItems |> List.sumBy (fun item -> item.Value)
        
        {
            SelectedItems = selectedItems
            TotalWeight = totalWeight
            TotalValue = totalValue
            IsFeasible = totalWeight <= problem.Capacity
            BackendName = "Classical Greedy"
            NumShots = 0
            ElapsedMs = 0.0
            BestEnergy = -totalValue
        }
    
    /// Solve Knapsack using dynamic programming (classical, optimal)
    /// 
    /// This is the exact algorithm - guarantees optimal solution.
    /// Time complexity: O(n*W) where n = number of items, W = capacity
    /// 
    /// Only works for integer weights - scales capacity by 1000 to handle decimals
    let solveClassicalDP (problem: KnapsackProblem) : KnapsackSolution =
        let n = problem.Items.Length
        
        // Scale capacity and weights to integers (multiply by 1000)
        let scale = 1000.0
        let intCapacity = int (problem.Capacity * scale)
        let intWeights = problem.Items |> List.map (fun item -> int (item.Weight * scale))
        
        // DP table: dp.[i].[w] = maximum value using first i items with capacity w
        let dp = Array2D.zeroCreate (n + 1) (intCapacity + 1)
        
        // Fill DP table
        for i in 1 .. n do
            let item = problem.Items.[i - 1]
            let w = intWeights.[i - 1]
            let v = item.Value
            
            for capacity in 0 .. intCapacity do
                if w <= capacity then
                    // Can include this item - choose max of including vs not including
                    dp.[i, capacity] <- max dp.[i - 1, capacity] (dp.[i - 1, capacity - w] + v)
                else
                    // Can't include this item - carry forward previous best
                    dp.[i, capacity] <- dp.[i - 1, capacity]
        
        // Backtrack to find selected items
        let mutable selectedItems = []
        let mutable remainingCapacity = intCapacity
        
        for i in n .. -1 .. 1 do
            if dp.[i, remainingCapacity] <> dp.[i - 1, remainingCapacity] then
                // Item i-1 was included
                let item = problem.Items.[i - 1]
                selectedItems <- item :: selectedItems
                remainingCapacity <- remainingCapacity - intWeights.[i - 1]
        
        let totalWeight = selectedItems |> List.sumBy (fun item -> item.Weight)
        let totalValue = selectedItems |> List.sumBy (fun item -> item.Value)
        
        {
            SelectedItems = selectedItems
            TotalWeight = totalWeight
            TotalValue = totalValue
            IsFeasible = totalWeight <= problem.Capacity
            BackendName = "Classical DP (Optimal)"
            NumShots = 0
            ElapsedMs = 0.0
            BestEnergy = -totalValue
        }
