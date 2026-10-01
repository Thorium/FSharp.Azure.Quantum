namespace FSharp.Azure.Quantum.Quantum

open System
open System.Diagnostics
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
/// LIMITATIONS:
/// - The capacity penalty and its slack terms are much larger than the item values in the
///   cost Hamiltonian, so the single-layer circuit mostly separates feasible from infeasible
///   selections: it raises the share of feasible samples, while the per-shot probability of
///   the optimal selection stays close to that of uniform sampling. The result comes mainly
///   from sampling many feasible selections and keeping the best, so NumShots must grow with
///   the number of items (the selections double with every item).
/// - When no sample is feasible the solver returns the empty selection (which is feasible)
///   with Sampling.Valid = 0 and Sampling.Hits = 0; check Sampling before using the result.
/// - Weights must be non-negative and have a common integer scale (see toQubo).
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
///   let config = { defaultConfig with NumShots = 1000 }
///   match! QuantumKnapsackSolver.solveAsync backend problem config CancellationToken.None with
///   | Ok solution -> printfn "Total value: %f" solution.TotalValue
///   | Error msg -> printfn "Error: %s" msg
module QuantumKnapsackSolver =

    // ================================================================================
    // PROBLEM DEFINITION
    // ================================================================================

    /// Knapsack item with weight and value
    type KnapsackItem =
        {
            /// Item identifier/name
            Id: string

            /// Item weight (consumes capacity)
            Weight: float

            /// Item value (objective to maximize)
            Value: float
        }

    /// Knapsack problem specification
    type KnapsackProblem =
        {
            /// Available items
            Items: KnapsackItem list

            /// Knapsack capacity (maximum total weight)
            Capacity: float
        }

    /// Knapsack solution result
    type KnapsackSolution =
        {
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

            /// Standing of this solution among the final samples; None when no sampling run produced it
            Sampling: QaoaExecutionHelpers.SampleStatistics option

            /// How the problem was split into circuits that fit the backend; None when it ran as one circuit
            Split: QaoaExecutionHelpers.SplitReport option
        }

    // ================================================================================
    // QUBO ENCODING FOR KNAPSACK
    // ================================================================================

    /// True when a total weight fits the capacity. The relative tolerance of 1e-9 absorbs the
    /// rounding of summed fractional weights (0.1 + 0.2 against a capacity of 0.3).
    let private fitsCapacity (problem: KnapsackProblem) (totalWeight: float) : bool =
        totalWeight <= problem.Capacity + 1e-9 * max 1.0 (abs problem.Capacity)

    /// Integer form of the capacity constraint: item weights rescaled to integers without a
    /// common divisor (factor f), the capacity floor(f·Capacity), and the slack weights.
    /// None when all items fit together: the constraint then holds for every selection.
    let private integerCapacity (problem: KnapsackProblem) : Result<(int list * int * int list) option, QuantumError> =
        let weights = problem.Items |> List.map (fun item -> item.Weight)

        if weights |> List.exists (fun w -> w < 0.0) then
            Error(QuantumError.ValidationError("weight", "Item weights must be non-negative"))
        elif Double.IsNaN problem.Capacity then
            Error(QuantumError.ValidationError("capacity", "Knapsack capacity is not a number"))
        else
            match Qubo.tryScaleToIntegers weights with
            | None ->
                Error(
                    QuantumError.ValidationError(
                        "weight",
                        "Item weights cannot be rescaled to integers "
                        + "(each needs at most 6 decimal places and a scaled magnitude of at most 1e9)"
                    )
                )
            | Some(integerWeights, factor) ->
                let totalWeight = integerWeights |> List.sumBy int64
                let scaledCapacity = problem.Capacity * factor

                // The tolerance of fitsCapacity in integer units: the QUBO and the classical
                // check accept the same selections.
                let capacity =
                    if Double.IsInfinity scaledCapacity then
                        scaledCapacity
                    else
                        floor (scaledCapacity + 1e-9 * max 1.0 (abs problem.Capacity) * factor)

                if capacity >= float totalWeight then
                    Ok None
                elif capacity > float Int32.MaxValue then
                    Error(
                        QuantumError.ValidationError(
                            "capacity",
                            $"The capacity in integer weight units ({capacity}) is too large to encode"
                        )
                    )
                else
                    Ok(Some(integerWeights, int capacity, Qubo.boundedSlackWeights (int capacity)))

    /// Encode Knapsack problem as QUBO (Lucas encoding with bounded integer slack)
    ///
    /// Knapsack QUBO formulation:
    ///
    /// Variables: x_i ∈ {0, 1} where x_i = 1 means item i is selected (first in the
    ///            bitstring), then slack bits s_t ∈ {0, 1}
    ///
    /// Objective (to MAXIMIZE):
    ///   Value = Σ v_i * x_i
    ///
    /// Constraint (capacity, INEQUALITY):
    ///   Σ w_i * x_i ≤ W
    ///
    /// Integer form: the weights are rescaled to integers a_i without a common divisor
    /// (Qubo.tryScaleToIntegers, factor f) and the capacity becomes b = floor(f·W); an integer
    /// total weight is at most f·W exactly when it is at most b. Weights without a short
    /// decimal form and negative weights are a ValidationError.
    ///
    /// The inequality is turned into an equality with slack bits
    /// (same pattern as QuantumBinaryILPSolver):
    ///   Σ a_i * x_i + Σ_t c_t * s_t = b
    /// with the weights c_t = Qubo.boundedSlackWeights b, whose subset sums are exactly
    /// 0 .. b. A selection has a slack setting with zero penalty exactly when it fits the
    /// capacity, so under-capacity selections are not penalised and over-capacity ones
    /// always are. When all items fit together there are no slack bits and no penalty.
    ///
    /// QUBO form (to MINIMIZE for QAOA):
    ///   Minimize: -Σ v_i * x_i + λ * (Σ a_i * x_i + Σ_t c_t * s_t - b)²
    ///   (Qubo.squaredLinearPenalty, upper triangle)
    ///
    /// λ = 10 · n · Σ|v_i| (1 when every value is 0), which is above the largest item value:
    /// removing one item of positive weight from an over-capacity selection lowers the
    /// penalty by at least λ and the value by at most max v_i, so every QUBO minimum fits the
    /// capacity and, among fitting selections with matching slack, the energy is -Value.
    let toQubo (problem: KnapsackProblem) : Result<QuboMatrix, QuantumError> =
        try
            let items = problem.Items |> List.toArray
            let numItems = items.Length

            if numItems = 0 then
                Error(QuantumError.ValidationError("numItems", "Knapsack problem has no items"))
            elif problem.Capacity <= 0.0 then
                Error(QuantumError.ValidationError("capacity", "Knapsack capacity must be positive"))
            elif
                items
                |> Array.exists (fun item -> Double.IsNaN item.Value || Double.IsInfinity item.Value)
            then
                Error(QuantumError.ValidationError("value", "Item values must be finite"))
            else
                match integerCapacity problem with
                | Error err -> Error err
                | Ok encoded ->
                    // Lucas rule: the penalty dominates the objective magnitude
                    let totalValue = items |> Array.sumBy (fun item -> abs item.Value)

                    let penalty =
                        if totalValue > 0.0 then
                            Qubo.computeLucasPenalties totalValue numItems
                        else
                            1.0

                    let objectiveTerms =
                        items
                        |> Array.indexed
                        |> Array.filter (fun (_, item) -> item.Value <> 0.0)
                        |> Array.fold (fun acc (i, item) -> Qubo.combineTerms (i, i) -item.Value acc) Map.empty

                    match encoded with
                    | None ->
                        Ok
                            {
                                Q = objectiveTerms
                                NumVariables = numItems
                            }
                    | Some(weights, capacity, slackWeights) ->
                        let linearTerms =
                            (weights
                             |> List.indexed
                             |> List.filter (fun (_, weight) -> weight <> 0)
                             |> List.map (fun (i, weight) -> (i, float weight)))
                            @ (slackWeights |> List.mapi (fun t weight -> (numItems + t, float weight)))

                        let quboTerms =
                            Qubo.squaredLinearPenalty penalty linearTerms (-(float capacity))
                            |> Map.fold (fun acc key value -> Qubo.combineTerms key value acc) objectiveTerms

                        Ok
                            {
                                Q = quboTerms
                                NumVariables = numItems + slackWeights.Length
                            }
        with ex ->
            Error(QuantumError.OperationError("QuboEncoding", $"Knapsack QUBO encoding failed: %s{ex.Message}"))

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
            let isFeasible = fitsCapacity problem totalWeight

            Some
                {
                    SelectedItems = selectedItems
                    TotalWeight = totalWeight
                    TotalValue = totalValue
                    IsFeasible = isFeasible
                    BackendName = ""
                    NumShots = 0
                    ElapsedMs = 0.0
                    BestEnergy = -totalValue // QUBO minimizes -value
                    Sampling = None
                    Split = None
                }

    /// Calculate solution value and feasibility
    let evaluateSolution (problem: KnapsackProblem) (selectedItems: KnapsackItem list) : float * bool =
        let totalWeight = selectedItems |> List.sumBy (fun item -> item.Weight)
        let totalValue = selectedItems |> List.sumBy (fun item -> item.Value)
        let isFeasible = fitsCapacity problem totalWeight

        (totalValue, isFeasible)

    // ================================================================================
    // QAOA CONFIGURATION
    // ================================================================================

    /// QAOA configuration parameters
    type QaoaConfig =
        {
            /// Number of measurement shots
            NumShots: int

            /// QAOA angles (gamma, beta) of the single layer, in units of the normalised cost
            /// Hamiltonian (minimisation convention, see Core.QaoaCircuit). Default (0.5, 0.5).
            InitialParameters: float * float

            /// When and how a problem wider than the backend is split into circuits that fit.
            /// Default: on simulators only (see QaoaExecutionHelpers.SplitSettings).
            Splitting: QaoaExecutionHelpers.SplitSettings
        }

    /// Default QAOA configuration for Knapsack
    let defaultConfig: QaoaConfig =
        {
            NumShots = 1000
            InitialParameters = (0.5, 0.5)
            Splitting = QaoaExecutionHelpers.defaultSplitSettings
        }

    // ================================================================================
    // BLOCK SPLIT (problems wider than one circuit)
    // ================================================================================

    /// Solve Knapsack block by block (QuboSplitting.solveSharesAsync): the items are cut into
    /// blocks of at most maxBlockItems, each block is asked through QAOA for its most valuable
    /// subset at every exact share of the capacity, and the blocks' answers are joined.
    ///
    /// A block takes one qubit per item and no slack bits, so a problem too wide for one
    /// circuit runs as many narrow ones: one QAOA run (config) per block and share, at most
    /// config.Splitting.MaxShareRuns in all (more is a ValidationError). The capacity is counted in the integer
    /// weight units of toQubo; a capacity of many units needs a coarser weight unit.
    ///
    /// The selection is joined from sampled subsets, so it always fits the capacity, and it
    /// is the optimum only when every block's best subsets were sampled. Split reports the blocks and
    /// runs; Sampling is None, since no single sampling run produced the answer.
    let solveInBlocksAsync
        (backend: BackendAbstraction.IQuantumBackend)
        (problem: KnapsackProblem)
        (config: QaoaExecutionHelpers.QaoaSolverConfig)
        (maxBlockItems: int)
        (cancellationToken: CancellationToken)
        : Task<Result<KnapsackSolution, QuantumError>> =
        task {
            let stopwatch = Stopwatch.StartNew()

            if List.isEmpty problem.Items then
                return Error(QuantumError.ValidationError("numItems", "Knapsack problem has no items"))
            elif problem.Capacity <= 0.0 then
                return Error(QuantumError.ValidationError("capacity", "Knapsack capacity must be positive"))
            elif
                problem.Items
                |> List.exists (fun item -> Double.IsNaN item.Value || Double.IsInfinity item.Value)
            then
                return Error(QuantumError.ValidationError("value", "Item values must be finite"))
            else
                match integerCapacity problem with
                | Error err -> return Error err
                | Ok scaled ->
                    let items = List.toArray problem.Items
                    let values = items |> Array.map (fun item -> item.Value)

                    // When all items fit together the capacity never binds: weightless items
                    // under a capacity of 0 ask each block once for its best subset.
                    let weights, capacity =
                        match scaled with
                        | Some(integerWeights, integerCapacity, _) -> List.toArray integerWeights, integerCapacity
                        | None -> Array.zeroCreate items.Length, 0

                    let sampleBlock (qubo: float[,]) =
                        task {
                            let! run =
                                QaoaExecutionHelpers.runQaoaSampledAsync backend qubo config cancellationToken

                            return run |> Result.map (fun r -> r.Samples)
                        }

                    let limits: QuboSplitting.ShareLimits =
                        {
                            MaxBlockItems = maxBlockItems
                            MaxRuns = config.Splitting.MaxShareRuns
                        }

                    let! solved =
                        QuboSplitting.solveSharesAsync limits values weights capacity sampleBlock cancellationToken

                    return
                        solved
                        |> Result.map (fun shares ->
                            let selected = shares.Items |> List.map (fun index -> items.[index])
                            let totalWeight = selected |> List.sumBy (fun item -> item.Weight)
                            let totalValue = selected |> List.sumBy (fun item -> item.Value)

                            {
                                SelectedItems = selected
                                TotalWeight = totalWeight
                                TotalValue = totalValue
                                IsFeasible = fitsCapacity problem totalWeight
                                BackendName = backend.Name
                                NumShots = config.FinalShots
                                ElapsedMs = stopwatch.Elapsed.TotalMilliseconds
                                BestEnergy = -totalValue
                                Sampling = None
                                Split =
                                    Some
                                        {
                                            Runs = shares.Runs
                                            FixedVariables = 0
                                            Blocks = shares.Blocks
                                            WidestPieceQubits =
                                                QuboSplitting.blocksOf maxBlockItems items.Length
                                                |> List.map Array.length
                                                |> List.max
                                        }
                            })
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
    /// Returns: Task<Result<KnapsackSolution, QuantumError>> - Task with result or error
    ///
    /// Example:
    ///   let backend = LocalBackend() :> IQuantumBackend
    ///   let problem = { Items = [...]; Capacity = 50.0 }
    ///   let config = { defaultConfig with NumShots = 1000 }
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

        let stopwatch = Stopwatch.StartNew()

        try
            // Step 1: Validate problem
            // Qubit count = numItems + the capacity slack bits; the actual count comes
            // from the QUBO's NumVariables below.
            let numQubits = problem.Items.Length

            // Note: Backend validation removed (MaxQubits/Name properties no longer in interface)
            // Backends will return errors if qubit count exceeded
            if numQubits = 0 then
                task { return Error(QuantumError.ValidationError("numItems", "Knapsack problem has no items")) }
            elif problem.Capacity <= 0.0 then
                task { return Error(QuantumError.ValidationError("capacity", "Knapsack capacity must be positive")) }
            else
                // Step 2: Encode Knapsack as QUBO
                match toQubo problem with
                | Error err -> task { return Error err }
                | Ok quboMatrix ->

                    // A QUBO wider than the backend is solved block by block when config.Splitting
                    // allows it. A problem whose blocks would need more runs than
                    // MaxShareRuns runs as one circuit while the backend can hold it.
                    let blockItems (pieceQubits: int) =
                        max 1 (min pieceQubits config.Splitting.MaxBlockItems)

                    let blockRuns (pieceQubits: int) =
                        match integerCapacity problem with
                        | Ok(Some(integerWeights, capacity, _)) ->
                            QuboSplitting.shareRuns (blockItems pieceQubits) (List.toArray integerWeights) capacity
                        | _ -> int64 (QuboSplitting.blocksOf (blockItems pieceQubits) problem.Items.Length).Length

                    let exceedsBackend =
                        match BackendAbstraction.UnifiedBackend.getMaxQubits backend with
                        | Some capacity -> quboMatrix.NumVariables > capacity
                        | None -> false

                    let runAsOneCircuit () =
                        // Step 3: Execute QAOA pipeline from dense QUBO
                        let quboArray = Qubo.toDenseArray quboMatrix.NumVariables quboMatrix.Q
                        let (gamma, beta) = config.InitialParameters
                        let parameters = [| gamma, beta |]

                        let handleMeasurements (measurements: int array array) =
                            // Step 8: Decode measurements to selections,
                            // dropping malformed (too short) measurements
                            let solutions = measurements |> Array.choose (decodeSolution problem)

                            // Step 9: Find best FEASIBLE solution (satisfies capacity)
                            let feasibleSolutions = solutions |> Array.filter (fun sol -> sol.IsFeasible)

                            let bestSolution =
                                if feasibleSolutions.Length > 0 then
                                    feasibleSolutions |> Array.maxBy (fun sol -> sol.TotalValue)
                                else
                                    // No feasible sample: the empty selection, which Sampling
                                    // reports with Valid = 0 and Hits = 0
                                    {
                                        SelectedItems = []
                                        TotalWeight = 0.0
                                        TotalValue = 0.0
                                        IsFeasible = true
                                        BackendName = backend.Name
                                        NumShots = config.NumShots
                                        ElapsedMs = 0.0
                                        BestEnergy = 0.0
                                        Sampling = None
                                        Split = None
                                    }

                            let sampling: QaoaExecutionHelpers.SampleStatistics =
                                {
                                    Shots = measurements.Length
                                    Qubits = quboMatrix.NumVariables
                                    Hits =
                                        feasibleSolutions
                                        |> Array.filter (fun sol -> sol.SelectedItems = bestSolution.SelectedItems)
                                        |> Array.length
                                    Valid = feasibleSolutions.Length
                                }

                            let elapsedMs = stopwatch.Elapsed.TotalMilliseconds

                            Ok
                                { bestSolution with
                                    BackendName = backend.Name
                                    NumShots = config.NumShots
                                    ElapsedMs = elapsedMs
                                    Sampling = Some sampling
                                }

                        task {
                            match!
                                QaoaExecutionHelpers.executeFromQuboAsync
                                    backend
                                    quboArray
                                    parameters
                                    config.NumShots
                                    cancellationToken
                            with
                            | Error err -> return Error err
                            | Ok measurements -> return handleMeasurements measurements
                        }

                    match QaoaExecutionHelpers.splitPieceQubits config.Splitting backend quboMatrix.NumVariables with
                    | ValueSome pieceQubits when blockRuns pieceQubits <= int64 config.Splitting.MaxShareRuns ->
                        solveInBlocksAsync
                            backend
                            problem
                            { QaoaExecutionHelpers.defaultConfig with
                                FinalShots = config.NumShots
                                Splitting = config.Splitting
                            }
                            (blockItems pieceQubits)
                            cancellationToken
                    | ValueSome pieceQubits when exceedsBackend ->
                        task {
                            return
                                Error(
                                    QuantumError.ValidationError(
                                        "qubits",
                                        $"the problem needs {quboMatrix.NumVariables} qubits, more than the backend holds; "
                                        + $"it was not split because blocks of {blockItems pieceQubits} items need "
                                        + $"{blockRuns pieceQubits} runs, above SplitSettings.MaxShareRuns = {config.Splitting.MaxShareRuns}"
                                    )
                                )
                        }
                    | _ -> runAsOneCircuit ()

        with ex when not (ex :? OperationCanceledException) ->
            task {
                return
                    Error(
                        QuantumError.OperationError(
                            "QuantumKnapsackSolver",
                            $"Quantum Knapsack solve failed: %s{ex.Message}"
                        )
                    )
            }

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
            |> List.map (fun item ->
                item,
                if item.Weight = 0.0 then
                    Double.MaxValue
                else
                    item.Value / item.Weight)
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
            IsFeasible = fitsCapacity problem totalWeight
            BackendName = "Classical Greedy"
            NumShots = 0
            ElapsedMs = 0.0
            BestEnergy = -totalValue
            Sampling = None
            Split = None
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
    ///   exclusionPenalties - Additional QUBO terms added to the base terms
    ///                        (Map.empty for the plain encoding; see buildExclusionPenalty)
    ///
    /// Returns: Result<QuboMatrix, QuantumError>
    let toSubsetSumQubo
        (items: KnapsackItem list)
        (targetSum: float)
        (exclusionPenalties: Map<(int * int), float>)
        : Result<QuboMatrix, QuantumError> =
        try
            let itemArray = items |> List.toArray
            let n = itemArray.Length

            if n = 0 then
                Error(QuantumError.ValidationError("numItems", "Subset-sum problem has no items"))
            elif targetSum <= 0.0 then
                Error(QuantumError.ValidationError("targetSum", "Target sum must be positive"))
            else
                // Penalty weight (Lucas rule)
                let maxWeight = items |> List.map (fun i -> i.Weight) |> List.max
                let penalty = Qubo.computeLucasPenalties maxWeight n

                // Linear terms (diagonal): λ * (w_i² - 2W*w_i)
                let linearTerms =
                    [
                        for i in 0 .. n - 1 do
                            let w = itemArray.[i].Weight
                            yield (i, i), penalty * (w * w - 2.0 * targetSum * w)
                    ]

                // Quadratic terms (upper triangle): λ * 2*w_i*w_j
                let quadraticTerms =
                    [
                        for i in 0 .. n - 1 do
                            for j in i + 1 .. n - 1 do
                                let w_i = itemArray.[i].Weight
                                let w_j = itemArray.[j].Weight
                                yield (i, j), penalty * 2.0 * w_i * w_j
                    ]

                // Combine base QUBO with the additional terms
                let baseTerms = linearTerms @ quadraticTerms

                let allTerms =
                    (Map.ofList baseTerms, exclusionPenalties)
                    ||> Map.fold (fun acc key value -> Qubo.combineTerms key value acc)

                Ok { Q = allTerms; NumVariables = n }
        with ex ->
            Error(QuantumError.OperationError("SubsetSumQubo", $"Subset-sum QUBO encoding failed: %s{ex.Message}"))

    /// Diagonal QUBO terms that raise the energy of bitstrings similar to a known solution s.
    ///
    /// The terms are Q_ii += strength · (2·s_i − 1): +strength where s_i = 1 and −strength
    /// where s_i = 0. Up to a constant they add
    ///
    ///   P(x) = strength · (number of positions where x agrees with s)
    ///
    /// which is largest at x = s and falls by `strength` for every differing position.
    ///
    /// Diagonal terms cannot single out one bitstring: neighbours of s are raised almost as
    /// much, and with terms for several known solutions a known solution can stay below an
    /// exact subset that lies between the others. To keep exact subsets below every other
    /// bitstring, strength · n · (number of known solutions) must stay below the smallest
    /// base penalty of a non-solution. findAllExactCombinationsAsync does not add these
    /// terms; it removes duplicates when it collects the samples.
    let buildExclusionPenalty (knownSolution: int[]) (penaltyStrength: float) : Map<(int * int), float> =
        [
            for i in 0 .. knownSolution.Length - 1 do
                let s_i = float knownSolution.[i]
                // s_i = 1: +strength when x_i = 1; s_i = 0: −strength when x_i = 1
                yield (i, i), penaltyStrength * (2.0 * s_i - 1.0)
        ]
        |> Map.ofList

    /// Configuration for iterative subset-sum quantum solver
    type SubsetSumConfig =
        {
            /// Number of measurement shots per QAOA iteration
            NumShots: int

            /// QAOA angles (gamma, beta), in units of the normalised cost Hamiltonian
            /// (minimisation convention, see Core.QaoaCircuit)
            InitialParameters: float * float

            /// Maximum number of QAOA iterations before giving up finding new solutions
            MaxIterations: int

            /// Number of consecutive failed iterations before stopping
            MaxConsecutiveFailures: int

            /// Not used: the iterations add no exclusion term and duplicates are removed when
            /// the samples are collected (see findAllExactCombinationsAsync)
            ExclusionPenaltyStrength: float

            /// When and how a search over more items than the backend runs is split into searches
            /// that fit (default: on simulators only; see QaoaExecutionHelpers.SplitSettings)
            Splitting: QaoaExecutionHelpers.SplitSettings
        }

    /// Default configuration for subset-sum solving
    let defaultSubsetSumConfig: SubsetSumConfig =
        {
            NumShots = 2000
            InitialParameters = (0.5, 0.5)
            MaxIterations = 50
            MaxConsecutiveFailures = 3
            ExclusionPenaltyStrength = 100.0
            Splitting = QaoaExecutionHelpers.defaultSplitSettings
        }

    /// Result of finding all exact combinations
    type SubsetSumResult =
        {
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

            /// How the search was split into searches that fit the backend; None when it ran as
            /// one circuit
            Split: QaoaExecutionHelpers.SplitReport option
        }

    /// One subset-sum search: every item is a qubit of one circuit that is sampled repeatedly.
    let private findInOneCircuitAsync
        (backend: BackendAbstraction.IQuantumBackend)
        (items: KnapsackItem list)
        (targetSum: float)
        (config: SubsetSumConfig)
        (cancellationToken: CancellationToken)
        : Task<Result<SubsetSumResult, QuantumError>> =
        task {
            let stopwatch = Stopwatch.StartNew()
            let n = items.Length
            let epsilon = 0.0001

            if n = 0 then
                return
                    Ok
                        {
                            Combinations = []
                            AllItems = []
                            IterationsUsed = 0
                            BackendName = backend.Name
                            ElapsedMs = 0.0
                            Split = None
                        }
            else

                try
                    let mutable knownSolutions: int[] list = []
                    let mutable consecutiveFailures = 0
                    let mutable iteration = 0
                    let mutable lastError: QuantumError option = None

                    while iteration < config.MaxIterations
                          && consecutiveFailures < config.MaxConsecutiveFailures do
                        iteration <- iteration + 1

                        // Encode as QUBO
                        match toSubsetSumQubo items targetSum Map.empty with
                        | Error err ->
                            lastError <- Some err
                            consecutiveFailures <- config.MaxConsecutiveFailures // Stop
                        | Ok quboMatrix ->

                            // Convert to dense array and execute QAOA pipeline
                            let quboArray = Qubo.toDenseArray quboMatrix.NumVariables quboMatrix.Q
                            let (gamma, beta) = config.InitialParameters
                            let parameters = [| gamma, beta |]

                            let! executed =
                                QaoaExecutionHelpers.executeFromQuboAsync
                                    backend
                                    quboArray
                                    parameters
                                    config.NumShots
                                    cancellationToken

                            match executed with
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
                                            |> List.mapi (fun i item ->
                                                if measurement.[i] = 1 then item.Weight else 0.0)
                                            |> List.sum

                                        if abs (totalWeight - targetSum) < epsilon then
                                            // Check if we've already found this solution
                                            let isDuplicate =
                                                knownSolutions
                                                |> List.exists (fun known -> Array.forall2 (=) known measurement)

                                            if not isDuplicate then
                                                knownSolutions <- measurement :: knownSolutions
                                                foundNew <- true

                                if foundNew then
                                    consecutiveFailures <- 0
                                else
                                    consecutiveFailures <- consecutiveFailures + 1

                    // A failed iteration fails the call: the subsets found before it are not a complete answer.
                    match lastError with
                    | Some err -> return Error err
                    | None ->
                        // Convert bitstring solutions to item lists
                        let combinations =
                            knownSolutions
                            |> List.rev // Preserve discovery order
                            |> List.map (fun bitstring ->
                                items
                                |> List.mapi (fun i item -> if bitstring.[i] = 1 then Some item else None)
                                |> List.choose id)

                        // Union of all items across all combinations
                        let allItems = combinations |> List.concat |> List.distinctBy (fun item -> item.Id)

                        let elapsedMs = stopwatch.Elapsed.TotalMilliseconds

                        return
                            Ok
                                {
                                    Combinations = combinations
                                    AllItems = allItems
                                    IterationsUsed = iteration
                                    BackendName = backend.Name
                                    ElapsedMs = elapsedMs
                                    Split = None
                                }

                with ex when not (ex :? OperationCanceledException) ->
                    return
                        Error(
                            QuantumError.OperationError(
                                "QuantumSubsetSum",
                                $"Quantum subset-sum solver failed: %s{ex.Message}"
                            )
                        )
        }

    /// Find ALL subsets of items whose weights sum exactly to targetSum,
    /// using repeated QAOA sampling.
    ///
    /// ALGORITHM:
    /// 1. Encode subset-sum as QUBO: minimize λ*(Σ w_i*x_i - W)²
    /// 2. Run QAOA on quantum backend, sample measurements
    /// 3. Extract feasible solutions (those with exact sum match)
    /// 4. Keep the subsets not seen before (duplicates are removed classically)
    /// 5. Repeat until MaxConsecutiveFailures iterations in a row find nothing new, or the
    ///    iteration limit is reached
    ///
    /// Every iteration samples the same circuit: a QUBO term cannot exclude the subsets
    /// already found (see buildExclusionPenalty). Subsets that are never sampled are missing
    /// from the result, so the result is complete only with enough shots and iterations.
    ///
    /// An item heavier than the target is in no exact sum and takes no qubit (when no weight
    /// is negative).
    ///
    /// WIDER THAN THE BACKEND:
    /// When the items need more qubits than the backend runs and config.Splitting allows a
    /// split there, the items are cut in two halves and every subset is found as a subset of
    /// the first half summing to t joined with a subset of the second half summing to
    /// target - t, for every t from 0 to the target in integer weight units; a half that is
    /// still too wide is cut again. Each piece is one search as above, so the searches
    /// multiply with the target: more than config.Splitting.MaxShareRuns of them is a
    /// ValidationError. The split needs positive weights with a short decimal form; without
    /// them the items run as one circuit. Split on the result says what was done.
    ///
    /// RULE 1 COMPLIANCE:
    /// ✅ Requires IQuantumBackend parameter — executes on quantum hardware/simulator
    ///
    /// Parameters:
    ///   backend - Quantum backend (LocalBackend, IonQ, Rigetti, etc.)
    ///   items - Items with weights (values unused for subset-sum)
    ///   targetSum - Exact sum to find subsets for
    ///   config - Solver configuration (shots, iterations, splitting)
    ///
    /// Returns: Result<SubsetSumResult, QuantumError>; an encoding or backend error in any
    /// iteration is returned as the Error.
    ///
    /// Example:
    ///   let backend = LocalBackendFactory.createUnified()
    ///   let items = [ {Id="2"; Weight=2.0; Value=2.0}; {Id="5"; Weight=5.0; Value=5.0}
    ///                 {Id="3"; Weight=3.0; Value=3.0}; {Id="4"; Weight=4.0; Value=4.0} ]
    ///   task {
    ///       match! findAllExactCombinationsAsync backend items 7.0 defaultSubsetSumConfig CancellationToken.None with
    ///       | Ok result -> printfn "Found %d combinations" result.Combinations.Length
    ///       | Error err -> printfn "Error: %A" err
    ///   }
    let findAllExactCombinationsAsync
        (backend: BackendAbstraction.IQuantumBackend)
        (items: KnapsackItem list)
        (targetSum: float)
        (config: SubsetSumConfig)
        (cancellationToken: CancellationToken)
        : Task<Result<SubsetSumResult, QuantumError>> =
        let stopwatch = Stopwatch.StartNew()

        let usable =
            if targetSum > 0.0 && items |> List.forall (fun item -> item.Weight >= 0.0) then
                items |> List.filter (fun item -> item.Weight <= targetSum + 0.0001)
            else
                items

        let integerUnits =
            Qubo.tryScaleToIntegers (targetSum :: (usable |> List.map (fun item -> item.Weight)))

        match QaoaExecutionHelpers.splitPieceQubits config.Splitting backend usable.Length, integerUnits with
        | ValueSome pieceQubits, Some(target :: weights, factor) when weights |> List.forall (fun w -> w > 0) ->
            let itemArray = List.toArray usable
            let weightArray = List.toArray weights
            let searches = ref 0
            let iterations = ref 0
            let widest = ref 0
            let blocks = System.Collections.Generic.HashSet<string>()
            let known = System.Collections.Generic.Dictionary<string, KnapsackItem list list>()

            // Every subset of the items at `indices` whose integer weights sum to `share`.
            let rec search (indices: int list) (share: int) : Task<Result<KnapsackItem list list, QuantumError>> =
                let weightOf (group: int list) =
                    group |> List.sumBy (fun i -> int64 weightArray.[i])

                let reachable = indices |> List.filter (fun i -> weightArray.[i] <= share)
                let key = String.Join(",", reachable) + ":" + string share

                if share = 0 then
                    Task.FromResult(Ok [ [] ])
                elif weightOf reachable < int64 share then
                    Task.FromResult(Ok [])
                else
                    match known.TryGetValue key with
                    | true, found -> Task.FromResult(Ok found)
                    | false, _ when reachable.Length <= pieceQubits ->
                        searches.Value <- searches.Value + 1

                        if searches.Value > config.Splitting.MaxShareRuns then
                            Task.FromResult(
                                Error(
                                    QuantumError.ValidationError(
                                        "qubits",
                                        $"splitting {usable.Length} items into pieces of {pieceQubits} qubits needs more than "
                                        + $"SplitSettings.MaxShareRuns = {config.Splitting.MaxShareRuns} searches"
                                    )
                                )
                            )
                        else
                            blocks.Add(String.Join(",", reachable)) |> ignore
                            widest.Value <- max widest.Value reachable.Length

                            quantumResultTask {
                                let! piece =
                                    findInOneCircuitAsync
                                        backend
                                        (reachable |> List.map (fun i -> itemArray.[i]))
                                        (float share / factor)
                                        config
                                        cancellationToken

                                iterations.Value <- iterations.Value + piece.IterationsUsed
                                known.[key] <- piece.Combinations
                                return piece.Combinations
                            }
                    | false, _ ->
                        let first, second = List.splitAt (reachable.Length / 2) reachable

                        // The first half takes no more than it weighs and leaves the second
                        // half no more than that half weighs.
                        let lowest = int (max 0L (int64 share - weightOf second))
                        let highest = int (min (int64 share) (weightOf first))

                        quantumResultTask {
                            let combined = ResizeArray<KnapsackItem list>()

                            for firstShare in lowest..highest do
                                cancellationToken.ThrowIfCancellationRequested()
                                let! lefts = search first firstShare

                                if not (List.isEmpty lefts) then
                                    let! rights = search second (share - firstShare)

                                    List.allPairs lefts rights
                                    |> List.iter (fun (left, right) -> combined.Add(left @ right))

                            let all = List.ofSeq combined
                            known.[key] <- all
                            return all
                        }

            quantumResultTask {
                let! combinations = search [ 0 .. itemArray.Length - 1 ] target

                return
                    {
                        Combinations = combinations
                        AllItems = combinations |> List.concat |> List.distinctBy (fun item -> item.Id)
                        IterationsUsed = iterations.Value
                        BackendName = backend.Name
                        ElapsedMs = stopwatch.Elapsed.TotalMilliseconds
                        Split =
                            Some
                                {
                                    Runs = searches.Value
                                    FixedVariables = 0
                                    Blocks = blocks.Count
                                    WidestPieceQubits = widest.Value
                                }
                    }
            }
        | _ -> findInOneCircuitAsync backend usable targetSum config cancellationToken
