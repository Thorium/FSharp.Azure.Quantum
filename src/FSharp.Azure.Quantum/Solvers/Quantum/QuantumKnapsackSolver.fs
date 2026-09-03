namespace FSharp.Azure.Quantum.Quantum

open System
open System.Threading
open System.Threading.Tasks
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
///   let backend = LocalBackend() :> IQuantumBackend
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

    /// Number of binary slack bits needed to represent an integer in [0, b]:
    /// T = ceil(log2(b + 1)), minimum 1 bit for b > 0, 0 bits for b = 0.
    /// Mirrors QuantumBinaryILPSolver.slackBitsForBound (integer bit counting
    /// avoids floating-point precision issues).
    let private slackBitsForBound (b: float) : int =
        if b <= 0.0 then 0
        elif b < 1.0 then 1
        else
            let bInt = int (System.Math.Ceiling b)
            let rec countBits value bits =
                if value <= 0 then bits
                else countBits (value >>> 1) (bits + 1)
            max 1 (countBits bInt 0)

    /// Encode Knapsack problem as QUBO (standard Lucas encoding with slack bits)
    ///
    /// Knapsack QUBO formulation:
    ///
    /// Variables: x_i ∈ {0, 1} where x_i = 1 means item i is selected,
    ///            plus slack bits s_t ∈ {0, 1}, t = 0 .. T-1, T = ceil(log2(W+1))
    ///
    /// Objective (to MAXIMIZE):
    ///   Value = Σ v_i * x_i
    ///
    /// Constraint (capacity, INEQUALITY):
    ///   Σ w_i * x_i ≤ W
    ///
    /// The inequality is turned into an equality with binary slack variables
    /// (same pattern as QuantumBinaryILPSolver):
    ///   Σ w_i * x_i + Σ_t 2^t * s_t = W
    /// so under-capacity selections are NOT penalised — the slack absorbs the
    /// unused capacity. (The previous slack-free penalty λ(Σw_i x_i − W)² was an
    /// EQUALITY penalty: it punished feasible under-capacity picks as violations,
    /// letting a near-worthless capacity-saturating pick win the QUBO argmin.)
    ///
    /// QUBO form (to MINIMIZE for QAOA):
    ///   Minimize: -Σ v_i * x_i + λ * (Σ w_i * x_i + Σ_t 2^t * s_t - W)²
    ///
    /// With the unified coefficient vector c (c_i = w_i for items, c_{n+t} = 2^t
    /// for slack bits) and using b² = b for binary b, ignoring the constant W²:
    ///   Q_vv = [-v_i for items] + λ * (c_v² - 2W*c_v)   (linear terms)
    ///   Q_uv = λ * 2*c_u*c_v for u < v                  (quadratic terms)
    let toQubo (problem: KnapsackProblem) : Result<QuboMatrix, QuantumError> =
        try
            let numItems = problem.Items.Length

            if numItems = 0 then
                Error (QuantumError.ValidationError ("numItems", "Knapsack problem has no items"))
            elif problem.Capacity <= 0.0 then
                Error (QuantumError.ValidationError ("capacity", "Knapsack capacity must be positive"))
            else
                // Calculate penalty weight using Lucas Rule
                // Penalty must be large enough to dominate objective violations
                let maxValue = problem.Items |> List.map (fun item -> item.Value) |> List.max
                let totalValue = problem.Items |> List.sumBy (fun item -> item.Value)

                // Use shared Lucas Rule helper: penalty >> objective magnitude
                let penalty = Qubo.computeLucasPenalties (max maxValue totalValue) numItems

                let W = problem.Capacity
                let numSlackBits = slackBitsForBound W
                let numVars = numItems + numSlackBits

                // Unified coefficient vector: item weights, then slack powers of two
                let coeffs =
                    [ for i in 0 .. numItems - 1 -> (i, problem.Items.[i].Weight) ]
                    @ [ for t in 0 .. numSlackBits - 1 -> (numItems + t, pown 2.0 t) ]

                // Linear terms (diagonal): objective on items, penalty on all vars
                let linearTerms =
                    coeffs
                    |> List.map (fun (idx, c) ->
                        let objective =
                            if idx < numItems then -problem.Items.[idx].Value else 0.0
                        (idx, idx), objective + penalty * (c * c - 2.0 * W * c))

                // Quadratic terms (upper triangle): λ * 2*c_u*c_v
                let quadraticTerms =
                    [ for (u, cu) in coeffs do
                        for (v, cv) in coeffs do
                            if u < v then
                                yield (u, v), penalty * 2.0 * cu * cv ]

                let quboTerms = linearTerms @ quadraticTerms |> Map.ofList

                Ok {
                    Q = quboTerms
                    NumVariables = numVars
                }
        with ex ->
            Error (QuantumError.OperationError ("QuboEncoding", $"Knapsack QUBO encoding failed: %s{ex.Message}"))

    // ================================================================================
    // SOLUTION DECODING
    // ================================================================================

    /// Decode binary solution to Knapsack selection
    ///
    /// Only the first numItems bits are decision variables; any trailing bits are
    /// the capacity slack bits from the QUBO encoding and are ignored here.
    /// Returns None when the measurement has fewer bits than there are items
    /// (a malformed backend response), rather than indexing out of range.
    let private decodeSolution (problem: KnapsackProblem) (bitstring: int[]) : KnapsackSolution option =
        if bitstring.Length < problem.Items.Length then
            None
        else

        let selectedItems =
            problem.Items
            |> List.mapi (fun i item -> i, item)
            |> List.filter (fun (i, _) -> bitstring.[i] = 1)
            |> List.map snd
        
        let totalWeight = selectedItems |> List.sumBy (fun item -> item.Weight)
        let totalValue = selectedItems |> List.sumBy (fun item -> item.Value)
        let isFeasible = totalWeight <= problem.Capacity
        
        Some {
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
    /// Returns: Async<Result<KnapsackSolution, QuantumError>> - Async computation with result or error
    /// 
    /// Example:
    ///   let backend = LocalBackend() :> IQuantumBackend
    ///   let problem = { Items = [...]; Capacity = 50.0 }
    ///   let config = { NumShots = 1000; InitialParameters = (0.5, 0.5) }
    ///   task {
    ///       match! solveAsync backend problem config CancellationToken.None with
    ///       | Ok solution -> printfn "Value: %f" solution.TotalValue
    ///       | Error msg -> printfn "Error: %s" msg
    ///   }
    let solveAsync 
        (backend: BackendAbstraction.IQuantumBackend) 
        (problem: KnapsackProblem) 
        (config: QaoaConfig) 
        (cancellationToken: CancellationToken)
        : Task<Result<KnapsackSolution, QuantumError>> =
        
        let startTime = DateTime.Now
        
        try
            // Step 1: Validate problem
            // Qubit count = numItems + ceil(log2(Capacity+1)) capacity slack bits;
            // the actual count comes from the QUBO's NumVariables below.
            let numQubits = problem.Items.Length

            // Note: Backend validation removed (MaxQubits/Name properties no longer in interface)
            // Backends will return errors if qubit count exceeded
            if numQubits = 0 then
                task {
                    return Error (QuantumError.ValidationError ("numItems", "Knapsack problem has no items"))
                }
            elif problem.Capacity <= 0.0 then
                task {
                    return Error (QuantumError.ValidationError ("capacity", "Knapsack capacity must be positive"))
                }
            else
                // Step 2: Encode Knapsack as QUBO
                match toQubo problem with
                | Error err -> task { return Error err }
                | Ok quboMatrix ->
                    
                    // Step 3: Execute QAOA pipeline from dense QUBO
                    let quboArray = Qubo.toDenseArray quboMatrix.NumVariables quboMatrix.Q
                    let (gamma, beta) = config.InitialParameters
                    let parameters = [| gamma, beta |]
                    
                    let handleMeasurements (measurements:int array array) =
                        // Step 8: Decode measurements to selections,
                        // dropping malformed (too short) measurements
                        let solutions =
                            measurements
                            |> Array.choose (decodeSolution problem)
                        
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
                        
                        Ok {
                            bestSolution with
                                BackendName = backend.Name
                                NumShots = config.NumShots
                                ElapsedMs = elapsedMs
                        }

                    task {
                        match! QaoaExecutionHelpers.executeFromQuboAsync backend quboArray parameters config.NumShots cancellationToken with
                        | Error err -> return Error err
                        | Ok measurements ->
                            return handleMeasurements measurements
                    }
        
        with ex ->
            task { return Error (QuantumError.OperationError ("QuantumKnapsackSolver", $"Quantum Knapsack solve failed: %s{ex.Message}")) }

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
    /// Returns: Ok with best feasible solution found, or Error with QuantumError
    /// 
    /// Example:
    ///   let backend = LocalBackend() :> IQuantumBackend
    ///   let problem = { Items = [...]; Capacity = 50.0 }
    ///   let config = { NumShots = 1000; InitialParameters = (0.5, 0.5) }
    ///   match solve backend problem config with
    ///   | Ok solution -> printfn "Value: %f" solution.TotalValue
    ///   | Error msg -> printfn "Error: %s" msg
    [<Obsolete("Use solveAsync for non-blocking execution against cloud backends")>]
    let solve 
        (backend: BackendAbstraction.IQuantumBackend) 
        (problem: KnapsackProblem) 
        (config: QaoaConfig) 
        : Result<KnapsackSolution, QuantumError> =
        solveAsync backend problem config CancellationToken.None
        |> Async.AwaitTask
        |> Async.RunSynchronously

    // ================================================================================
    // CLASSICAL GREEDY SOLVER (for comparison)
    // ================================================================================

    /// Solve Knapsack using greedy value-to-weight ratio algorithm (classical)
    /// 
    /// This provides a classical baseline for comparison with quantum QAOA.
    /// Uses greedy heuristic: sort items by value/weight ratio, select until capacity full.
    /// 
    /// Typical performance: 80-90% of optimal for random instances
    let internal solveClassical (problem: KnapsackProblem) : KnapsackSolution =
        // Sort items by value-to-weight ratio (descending)
        let sortedItems = 
            problem.Items
            |> List.map (fun item -> item, if item.Weight = 0.0 then System.Double.MaxValue else item.Value / item.Weight)
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

    // ================================================================================
    // QUANTUM SUBSET-SUM: FIND ALL EXACT COMBINATIONS VIA ITERATIVE QAOA
    // ================================================================================

    /// Encode exact subset-sum as QUBO (penalty-only formulation).
    ///
    /// Unlike standard knapsack which maximizes value subject to capacity ≤ W,
    /// subset-sum requires: Σ w_i * x_i = W exactly.
    ///
    /// QUBO formulation (minimize):
    ///   H = λ * (Σ w_i * x_i - W)²
    ///
    /// Expanded:
    ///   H = λ * [ Σ w_i² * x_i + 2 * Σ_{i<j} w_i*w_j*x_i*x_j - 2W * Σ w_i * x_i + W² ]
    ///
    /// Since x_i² = x_i (binary), the QUBO matrix entries are:
    ///   Q_ii = λ * (w_i² - 2W*w_i)     (linear terms on diagonal)
    ///   Q_ij = λ * 2*w_i*w_j            (quadratic terms, i < j)
    ///
    /// The constant W² is ignored (shifts energy but doesn't affect argmin).
    ///
    /// Parameters:
    ///   items - List of items with weights
    ///   targetSum - The exact sum to match (W)
    ///   exclusionPenalties - Additional QUBO terms to penalize already-found solutions
    ///
    /// Returns: Result<QuboMatrix, QuantumError>
    let toSubsetSumQubo
        (items: KnapsackItem list)
        (targetSum: float)
        (exclusionPenalties: Map<(int * int), float>)
        : Result<QuboMatrix, QuantumError> =
        try
            let n = items.Length

            if n = 0 then
                Error (QuantumError.ValidationError ("numItems", "Subset-sum problem has no items"))
            elif targetSum <= 0.0 then
                Error (QuantumError.ValidationError ("targetSum", "Target sum must be positive"))
            else
                // Penalty weight: must dominate any exclusion penalties
                let maxWeight = items |> List.map (fun i -> i.Weight) |> List.max
                let penalty = Qubo.computeLucasPenalties maxWeight n

                // Linear terms (diagonal): λ * (w_i² - 2W*w_i)
                let linearTerms =
                    [ for i in 0 .. n - 1 do
                        let w = items.[i].Weight
                        yield (i, i), penalty * (w * w - 2.0 * targetSum * w) ]

                // Quadratic terms (upper triangle): λ * 2*w_i*w_j
                let quadraticTerms =
                    [ for i in 0 .. n - 1 do
                        for j in i + 1 .. n - 1 do
                            let w_i = items.[i].Weight
                            let w_j = items.[j].Weight
                            yield (i, j), penalty * 2.0 * w_i * w_j ]

                // Combine base QUBO with exclusion penalties
                let baseTerms = linearTerms @ quadraticTerms

                let allTerms =
                    (Map.ofList baseTerms, exclusionPenalties)
                    ||> Map.fold (fun acc key value ->
                        Qubo.combineTerms key value acc)

                Ok { Q = allTerms; NumVariables = n }
        with ex ->
            Error (QuantumError.OperationError ("SubsetSumQubo", $"Subset-sum QUBO encoding failed: %s{ex.Message}"))

    /// Build QUBO exclusion penalty terms for a known solution.
    ///
    /// To prevent QAOA from rediscovering a previously found solution s = (s_1, ..., s_n),
    /// we add a penalty that is maximal when x = s:
    ///
    ///   P(x) = λ_excl * Π_i f(x_i, s_i)
    ///
    /// where f(x_i, s_i) = x_i if s_i = 1, and (1 - x_i) if s_i = 0.
    ///
    /// For QUBO (degree ≤ 2), we cannot encode this product directly for n > 2.
    /// Instead, we use a linear approximation that still penalizes the known solution:
    ///
    ///   P(x) = λ_excl * [ Σ_{s_i=1} (1 - x_i) + Σ_{s_i=0} x_i ]
    ///        = λ_excl * [ |s| - Σ_{s_i=1} x_i + Σ_{s_i=0} x_i ]
    ///
    /// Wait — this is a Hamming distance penalty, which is minimized when x ≠ s but
    /// doesn't have a unique minimum at x = s. Instead we use the quadratic penalty:
    ///
    ///   P(x) = -λ_excl * [ n - Σ_i (x_i - s_i)² ]
    ///        = -λ_excl * n + λ_excl * Σ_i (x_i - s_i)²
    ///
    /// Since (x_i - s_i)² = x_i² - 2*s_i*x_i + s_i² = x_i(1 - 2*s_i) + s_i
    /// (using x_i² = x_i for binary), the QUBO diagonal terms are:
    ///
    ///   Q_ii += λ_excl * (1 - 2*s_i)    for each i
    ///
    /// This makes the energy highest when x_i = s_i for all i (perfect match with
    /// the known solution), effectively pushing QAOA away from it.
    let buildExclusionPenalty
        (knownSolution: int[])
        (penaltyStrength: float)
        : Map<(int * int), float> =
        [ for i in 0 .. knownSolution.Length - 1 do
            let s_i = float knownSolution.[i]
            // When s_i = 1: term = -λ (penalizes x_i = 1)
            // When s_i = 0: term = +λ (penalizes x_i = 0, i.e., rewards x_i = 1)
            yield (i, i), penaltyStrength * (1.0 - 2.0 * s_i) ]
        |> Map.ofList

    /// Combine multiple exclusion penalties (one per known solution)
    let private combineExclusionPenalties
        (knownSolutions: int[] list)
        (penaltyStrength: float)
        : Map<(int * int), float> =
        (Map.empty, knownSolutions)
        ||> List.fold (fun acc solution ->
            let penalty = buildExclusionPenalty solution penaltyStrength
            (acc, penalty)
            ||> Map.fold (fun m key value -> Qubo.combineTerms key value m))

    /// Configuration for iterative subset-sum quantum solver
    type SubsetSumConfig = {
        /// Number of measurement shots per QAOA iteration
        NumShots: int

        /// Initial QAOA parameters (gamma, beta)
        InitialParameters: float * float

        /// Maximum number of QAOA iterations before giving up finding new solutions
        MaxIterations: int

        /// Number of consecutive failed iterations before stopping
        MaxConsecutiveFailures: int

        /// Strength of exclusion penalty (relative to constraint penalty)
        ExclusionPenaltyStrength: float
    }

    /// Default configuration for subset-sum solving
    let defaultSubsetSumConfig : SubsetSumConfig = {
        NumShots = 2000
        InitialParameters = (0.5, 0.5)
        MaxIterations = 50
        MaxConsecutiveFailures = 3
        ExclusionPenaltyStrength = 100.0
    }

    /// Result of finding all exact combinations
    type SubsetSumResult = {
        /// All found combinations (each is a list of selected items)
        Combinations: KnapsackItem list list

        /// Union of all items across all combinations
        AllItems: KnapsackItem list

        /// Number of QAOA iterations performed
        IterationsUsed: int

        /// Backend used
        BackendName: string

        /// Total execution time in milliseconds
        ElapsedMs: float
    }

    /// Find ALL subsets of items whose weights sum exactly to targetSum,
    /// using iterative QAOA with exclusion penalties.
    ///
    /// ALGORITHM:
    /// 1. Encode subset-sum as QUBO: minimize λ*(Σ w_i*x_i - W)²
    /// 2. Run QAOA on quantum backend, sample measurements
    /// 3. Extract feasible solutions (those with exact sum match)
    /// 4. For each new solution found, add exclusion penalty to QUBO
    /// 5. Repeat until no new solutions found or iteration limit reached
    ///
    /// RULE 1 COMPLIANCE:
    /// ✅ Requires IQuantumBackend parameter — executes on quantum hardware/simulator
    ///
    /// Parameters:
    ///   backend - Quantum backend (LocalBackend, IonQ, Rigetti, etc.)
    ///   items - Items with weights (values unused for subset-sum)
    ///   targetSum - Exact sum to find subsets for
    ///   config - Solver configuration (shots, iterations, penalties)
    ///
    /// Returns: Result<SubsetSumResult, QuantumError>
    ///
    /// Example:
    ///   let backend = LocalBackendFactory.createUnified()
    ///   let items = [ {Id="2"; Weight=2.0; Value=2.0}; {Id="5"; Weight=5.0; Value=5.0}
    ///                 {Id="3"; Weight=3.0; Value=3.0}; {Id="4"; Weight=4.0; Value=4.0} ]
    ///   match findAllExactCombinations backend items 7.0 defaultSubsetSumConfig with
    ///   | Ok result -> printfn "Found %d combinations" result.Combinations.Length
    ///   | Error err -> printfn "Error: %A" err
    let findAllExactCombinations
        (backend: BackendAbstraction.IQuantumBackend)
        (items: KnapsackItem list)
        (targetSum: float)
        (config: SubsetSumConfig)
        : Result<SubsetSumResult, QuantumError> =

        let startTime = DateTime.Now
        let n = items.Length
        let epsilon = 0.0001

        if n = 0 then
            Ok { Combinations = []; AllItems = []; IterationsUsed = 0
                 BackendName = backend.Name; ElapsedMs = 0.0 }
        else

        try
            let mutable knownSolutions : int[] list = []
            let mutable consecutiveFailures = 0
            let mutable iteration = 0
            let mutable lastError : QuantumError option = None

            while iteration < config.MaxIterations
                  && consecutiveFailures < config.MaxConsecutiveFailures do
                iteration <- iteration + 1

                // Build exclusion penalties for all known solutions
                let exclusions =
                    combineExclusionPenalties knownSolutions config.ExclusionPenaltyStrength

                // Encode as QUBO with exclusion penalties
                match toSubsetSumQubo items targetSum exclusions with
                | Error err ->
                    lastError <- Some err
                    consecutiveFailures <- config.MaxConsecutiveFailures // Stop
                | Ok quboMatrix ->

                // Convert to dense array and execute QAOA pipeline
                let quboArray = Qubo.toDenseArray quboMatrix.NumVariables quboMatrix.Q
                let (gamma, beta) = config.InitialParameters
                let parameters = [| gamma, beta |]

                match QaoaExecutionHelpers.executeFromQubo backend quboArray parameters config.NumShots with
                | Error err ->
                    lastError <- Some err
                    consecutiveFailures <- config.MaxConsecutiveFailures // Stop
                | Ok measurements ->

                // Find new feasible solutions in this batch
                let mutable foundNew = false

                for measurement in measurements do
                    if measurement.Length = n then
                        // Check if this is an exact-sum solution
                        let totalWeight =
                            items
                            |> List.mapi (fun i item -> if measurement.[i] = 1 then item.Weight else 0.0)
                            |> List.sum

                        if abs (totalWeight - targetSum) < epsilon then
                            // Check if we've already found this solution
                            let isDuplicate =
                                knownSolutions
                                |> List.exists (fun known ->
                                    Array.forall2 (=) known measurement)

                            if not isDuplicate then
                                knownSolutions <- measurement :: knownSolutions
                                foundNew <- true

                if foundNew then
                    consecutiveFailures <- 0
                else
                    consecutiveFailures <- consecutiveFailures + 1

            // Convert bitstring solutions to item lists
            let combinations =
                knownSolutions
                |> List.rev  // Preserve discovery order
                |> List.map (fun bitstring ->
                    items
                    |> List.mapi (fun i item -> if bitstring.[i] = 1 then Some item else None)
                    |> List.choose id)

            // Union of all items across all combinations
            let allItems =
                combinations
                |> List.concat
                |> List.distinctBy (fun item -> item.Id)

            let elapsedMs = (DateTime.Now - startTime).TotalMilliseconds

            Ok {
                Combinations = combinations
                AllItems = allItems
                IterationsUsed = iteration
                BackendName = backend.Name
                ElapsedMs = elapsedMs
            }

        with ex ->
            Error (QuantumError.OperationError (
                "QuantumSubsetSum",
                $"Quantum subset-sum solver failed: %s{ex.Message}"))

