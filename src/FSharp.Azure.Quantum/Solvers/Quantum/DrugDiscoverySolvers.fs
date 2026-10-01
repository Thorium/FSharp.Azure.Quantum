namespace FSharp.Azure.Quantum.Quantum

open System
open System.Threading
open System.Threading.Tasks
open FSharp.Azure.Quantum
open FSharp.Azure.Quantum.Core
open FSharp.Azure.Quantum.Backends

/// Drug Discovery Quantum Solvers
///
/// Domain-specific solvers for pharmaceutical optimization problems using QAOA.
/// These solvers provide correct QUBO formulations for drug discovery use cases.
///
/// Features:
/// - Multi-layer QAOA (p > 1) for improved solution quality
/// - Nelder-Mead parameter optimization
/// - Constraint repair post-processing for soft constraint violations
///
/// RULE 1 COMPLIANCE:
/// ✅ All solvers require IQuantumBackend parameter (explicit quantum execution)
module DrugDiscoverySolvers =

    // ================================================================================
    // QAOA CONFIGURATION
    // ================================================================================

    /// Configuration for QAOA execution.
    /// Type alias for the unified QaoaSolverConfig from QaoaExecutionHelpers.
    type QaoaConfig = QaoaExecutionHelpers.QaoaSolverConfig

    /// Default QAOA configuration (balanced speed/quality)
    let defaultConfig: QaoaConfig = QaoaExecutionHelpers.defaultConfig

    /// Fast configuration (for quick prototyping)
    let fastConfig: QaoaConfig = QaoaExecutionHelpers.fastConfig

    /// High-quality configuration (for production)
    let highQualityConfig: QaoaConfig = QaoaExecutionHelpers.highQualityConfig

    // ================================================================================
    // SHARED UTILITIES (delegated to QaoaExecutionHelpers)
    // ================================================================================

    /// Evaluate QUBO objective for a bitstring
    let private evaluateQubo (qubo: float[,]) (bits: int[]) : float =
        QaoaExecutionHelpers.evaluateQubo qubo bits

    /// Create objective function for Nelder-Mead optimization
    /// Returns expectation value of QUBO Hamiltonian (lower = better)
    let private createObjectiveFunction
        (backend: BackendAbstraction.IQuantumBackend)
        (qubo: float[,])
        (problemHam: QaoaCircuit.ProblemHamiltonian)
        (mixerHam: QaoaCircuit.MixerHamiltonian)
        (numLayers: int)
        (shots: int)
        : float[] -> float =
        QaoaExecutionHelpers.createObjectiveFunction backend qubo problemHam mixerHam numLayers shots

    // ================================================================================
    // MAXIMUM WEIGHT INDEPENDENT SET (MWIS)
    // ================================================================================
    //
    // Problem: Select maximum-weight subset of nodes with no edges between them.
    //
    // Use case: Pharmacophore feature selection
    // - Nodes = pharmacophore features with importance weights
    // - Edges = overlapping (conflicting) features
    // - Goal = select highest-importance non-overlapping features
    //
    // QUBO Formulation:
    //   Variables: x_i ∈ {0,1} (select node i)
    //   Minimize: -Σ w_i * x_i + λ * Σ_{(i,j)∈E} x_i * x_j
    //
    //   First term: maximize weight (negated for minimization)
    //   Second term: penalty for selecting adjacent nodes (λ large enough)
    // ================================================================================

    module IndependentSet =

        /// Node in an independent set problem
        type Node = { Id: string; Weight: float }

        /// Independent set problem
        type Problem =
            {
                Nodes: Node list
                /// Edges represent conflicts (adjacent nodes cannot both be selected)
                Edges: (int * int) list
            }

        /// Solution result
        type Solution =
            {
                SelectedNodes: Node list
                TotalWeight: float
                IsValid: bool // No selected nodes are adjacent
                WasRepaired: bool // Whether constraint repair was applied
                BackendName: string
                NumShots: int
                OptimizedParameters: (float * float)[] option
                OptimizationConverged: bool option
                /// Standing of this solution among the final samples; None when no single sampling run produced it
                Sampling: QaoaExecutionHelpers.SampleStatistics option
                /// How the problem was split into circuits that fit the backend; None when it ran as one circuit
                Split: QaoaExecutionHelpers.SplitReport option
            }

        /// Build QUBO for Maximum Weight Independent Set
        let toQubo (problem: Problem) : float[,] =
            let n = problem.Nodes.Length
            let qubo = Array2D.zeroCreate n n

            // Penalty must exceed maximum possible weight gain from violating constraint
            let maxWeight = problem.Nodes |> List.sumBy (fun node -> abs node.Weight)
            let penalty = maxWeight + 1.0

            // Linear terms: -w_i (maximize weight)
            for i, node in problem.Nodes |> List.indexed do
                qubo.[i, i] <- -node.Weight

            // Quadratic penalty for edges: +λ for each edge
            for (i, j) in problem.Edges do
                qubo.[i, j] <- qubo.[i, j] + penalty / 2.0
                qubo.[j, i] <- qubo.[j, i] + penalty / 2.0

            qubo

        /// Check if solution is valid (no adjacent nodes selected)
        let isValid (problem: Problem) (selected: int[]) : bool =
            problem.Edges
            |> List.forall (fun (i, j) -> not (selected.[i] = 1 && selected.[j] = 1))

        /// Constraint repair: remove conflicting nodes (keep higher weight)
        let private repairConstraints (problem: Problem) (bits: int[]) : int[] =
            let repaired = Array.copy bits
            let nodes = List.toArray problem.Nodes

            // Find and fix violations
            for (i, j) in problem.Edges do
                if repaired.[i] = 1 && repaired.[j] = 1 then
                    // Both selected but adjacent - remove the one with lower weight
                    let wi = nodes.[i].Weight
                    let wj = nodes.[j].Weight
                    if wi >= wj then repaired.[j] <- 0 else repaired.[i] <- 0

            repaired

        /// Decode bitstring to solution
        let decode (problem: Problem) (bits: int[]) : Solution =
            let selected =
                problem.Nodes
                |> List.indexed
                |> List.filter (fun (i, _) -> bits.[i] = 1)
                |> List.map snd

            {
                SelectedNodes = selected
                TotalWeight = selected |> List.sumBy (fun n -> n.Weight)
                IsValid = isValid problem bits
                WasRepaired = false
                BackendName = ""
                NumShots = 0
                OptimizedParameters = None
                OptimizationConverged = None
                Sampling = None
                Split = None
            }

        /// Solve using quantum QAOA with advanced features.
        let solveWithConfigAsync
            (backend: BackendAbstraction.IQuantumBackend)
            (problem: Problem)
            (config: QaoaConfig)
            (cancellationToken: CancellationToken)
            : Task<Result<Solution, QuantumError>> =
            quantumResultTask {
                cancellationToken.ThrowIfCancellationRequested()

                if problem.Nodes.IsEmpty then
                    return! Error(QuantumError.ValidationError("nodes", "Problem has no nodes"))
                else
                    let qubo = toQubo problem

                    let! run = QuboSplitting.runQaoaAsync backend qubo config cancellationToken

                    let bits = run.Best
                    let optParams = run.Direct |> Option.map (fun direct -> direct.Parameters)
                    let converged = run.Direct |> Option.bind (fun direct -> direct.Converged)

                    // Apply constraint repair if enabled and solution is invalid
                    let finalBits, wasRepaired =
                        if config.EnableConstraintRepair && not (isValid problem bits) then
                            (repairConstraints problem bits, true)
                        else
                            (bits, false)

                    let solution = decode problem finalBits

                    // A split run has no single sample set to take statistics from
                    let sampling =
                        run.Direct
                        |> Option.map (fun direct ->
                            QaoaExecutionHelpers.sampleStatistics
                                bits.Length
                                (isValid problem)
                                (fun sample -> sample = finalBits)
                                direct.Samples)

                    return
                        { solution with
                            BackendName = backend.Name
                            NumShots = config.FinalShots
                            WasRepaired = wasRepaired
                            OptimizedParameters = optParams
                            OptimizationConverged = converged
                            Sampling = sampling
                            Split = run.Split
                        }
            }

        /// Classical greedy solver for comparison
        let internal solveClassical (problem: Problem) : Solution =
            // Greedy: sort by weight, add if no conflict
            let sorted =
                problem.Nodes |> List.indexed |> List.sortByDescending (fun (_, n) -> n.Weight)

            let n = problem.Nodes.Length
            let selected = Array.zeroCreate n

            let adjacency =
                problem.Edges
                |> List.collect (fun (i, j) -> [ (i, j); (j, i) ])
                |> List.groupBy fst
                |> List.map (fun (k, vs) -> k, vs |> List.map snd |> Set.ofList)
                |> Map.ofList

            for (idx, _) in sorted do
                let neighbors = adjacency |> Map.tryFind idx |> Option.defaultValue Set.empty
                let hasConflict = neighbors |> Set.exists (fun j -> selected.[j] = 1)

                if not hasConflict then
                    selected.[idx] <- 1

            decode problem selected
            |> fun s ->
                { s with
                    BackendName = "Classical Greedy"
                }

    // ================================================================================
    // INFLUENCE MAXIMIZATION (k-node selection)
    // ================================================================================
    //
    // Problem: Select k nodes that maximize combined influence in a network.
    //
    // Use case: Key drug target identification
    // - Nodes = proteins with disease relevance scores
    // - Edges = protein-protein interactions with strength weights
    // - Goal = select k most influential proteins for drug targeting
    //
    // QUBO Formulation:
    //   Variables: x_i ∈ {0,1} (select node i)
    //   Maximize: Σ score_i * x_i + α * Σ_{(i,j)∈E} w_ij * x_i * x_j
    //   Subject to: Σ x_i = k
    //
    //   First term: node importance
    //   Second term: bonus for selecting connected nodes (synergy)
    //   Constraint: encoded as penalty λ * (Σ x_i - k)²,
    //               λ = Σ|score_i| + |α| * Σ|w_ij| + 1
    // ================================================================================

    module InfluenceMaximization =

        /// Node in influence network
        type Node =
            {
                Id: string
                /// Importance score (e.g., disease relevance)
                Score: float
            }

        /// Edge representing interaction
        [<Struct>]
        type Edge =
            {
                Source: int
                Target: int
                /// Interaction strength
                Weight: float
            }

        /// Problem definition
        type Problem =
            {
                Nodes: Node list
                Edges: Edge list
                /// Number of nodes to select
                K: int
                /// Weight for synergy term (default 0.5)
                SynergyWeight: float
            }

        /// Solution result
        type Solution =
            {
                SelectedNodes: Node list
                TotalScore: float
                SynergyBonus: float
                NumSelected: int // For checking cardinality constraint
                WasRepaired: bool
                BackendName: string
                NumShots: int
                OptimizedParameters: (float * float)[] option
                OptimizationConverged: bool option
                /// Standing of this solution among the final samples; None when no single sampling run produced it
                Sampling: QaoaExecutionHelpers.SampleStatistics option
                /// How the problem was split into circuits that fit the backend; None when it ran as one circuit
                Split: QaoaExecutionHelpers.SplitReport option
            }

        /// Build QUBO for Influence Maximization
        ///
        /// Cardinality penalty: λ = Σ|score_i| + |α|·Σ|w_ij| + 1. Two selections differ in
        /// objective by at most Σ|score_i| + |α|·Σ|w_ij| (every score and every edge term
        /// changes by at most its magnitude), and a selection of the wrong size costs at
        /// least λ, so every minimum-energy state selects exactly K nodes and is a best
        /// selection of K nodes.
        let toQubo (problem: Problem) : float[,] =
            let n = problem.Nodes.Length
            let k = problem.K
            let alpha = problem.SynergyWeight
            let qubo = Array2D.zeroCreate n n

            // Penalty for cardinality constraint (must select exactly k):
            // above the widest gap the score and synergy terms together can open
            let totalScore = problem.Nodes |> List.sumBy (fun node -> abs node.Score)

            let totalSynergy =
                problem.Edges |> List.sumBy (fun edge -> abs (alpha * edge.Weight))

            let penalty = totalScore + totalSynergy + 1.0

            // Linear terms from constraint: λ * (Σ x_i - k)² = λ * (Σ x_i² - 2k * Σ x_i + k²)
            // Since x_i² = x_i for binary: λ * ((1 - 2k) * Σ x_i + k²)
            // Q_ii contribution: λ * (1 - 2k) = λ - 2λk
            for i, node in problem.Nodes |> List.indexed do
                // Maximize score (negate for minimization) + constraint penalty
                qubo.[i, i] <- -node.Score + penalty * (1.0 - 2.0 * float k)

            // Quadratic terms from constraint: λ * 2 * x_i * x_j for i ≠ j
            for i in 0 .. n - 1 do
                for j in i + 1 .. n - 1 do
                    qubo.[i, j] <- qubo.[i, j] + penalty
                    qubo.[j, i] <- qubo.[j, i] + penalty

            // Synergy bonus for edges (negate for minimization)
            for edge in problem.Edges do
                let i, j = edge.Source, edge.Target
                let bonus = -alpha * edge.Weight / 2.0
                qubo.[i, j] <- qubo.[i, j] + bonus
                qubo.[j, i] <- qubo.[j, i] + bonus

            qubo

        /// Constraint repair: adjust selection to exactly k nodes
        let private repairConstraints (problem: Problem) (bits: int[]) : int[] =
            let repaired = Array.copy bits
            let currentCount = repaired |> Array.sum
            let k = problem.K

            if currentCount = k then
                repaired
            elif currentCount < k then
                // Need to add more nodes - add highest scoring unselected
                let unselected =
                    problem.Nodes
                    |> List.indexed
                    |> List.filter (fun (i, _) -> repaired.[i] = 0)
                    |> List.sortByDescending (fun (_, n) -> n.Score)

                let toAdd = min (k - currentCount) (List.length unselected)

                for idx in 0 .. toAdd - 1 do
                    let (i, _) = unselected.[idx]
                    repaired.[i] <- 1

                repaired
            else
                // Need to remove nodes - remove lowest scoring selected
                let selected =
                    problem.Nodes
                    |> List.indexed
                    |> List.filter (fun (i, _) -> repaired.[i] = 1)
                    |> List.sortBy (fun (_, n) -> n.Score)

                let toRemove = currentCount - k

                for idx in 0 .. toRemove - 1 do
                    let (i, _) = selected.[idx]
                    repaired.[i] <- 0

                repaired

        /// Decode bitstring to solution
        let decode (problem: Problem) (bits: int[]) : Solution =
            let selected =
                problem.Nodes
                |> List.indexed
                |> List.filter (fun (i, _) -> bits.[i] = 1)
                |> List.map snd

            let selectedIndices =
                bits
                |> Array.indexed
                |> Array.filter (fun (_, b) -> b = 1)
                |> Array.map fst
                |> Set.ofArray

            let synergy =
                problem.Edges
                |> List.filter (fun e -> Set.contains e.Source selectedIndices && Set.contains e.Target selectedIndices)
                |> List.sumBy (fun e -> e.Weight)

            {
                SelectedNodes = selected
                TotalScore = selected |> List.sumBy (fun n -> n.Score)
                SynergyBonus = synergy * problem.SynergyWeight
                NumSelected = selected.Length
                WasRepaired = false
                BackendName = ""
                NumShots = 0
                OptimizedParameters = None
                OptimizationConverged = None
                Sampling = None
                Split = None
            }

        /// Solve using quantum QAOA with advanced features.
        let solveWithConfigAsync
            (backend: BackendAbstraction.IQuantumBackend)
            (problem: Problem)
            (config: QaoaConfig)
            (cancellationToken: CancellationToken)
            : Task<Result<Solution, QuantumError>> =
            quantumResultTask {
                cancellationToken.ThrowIfCancellationRequested()

                if problem.Nodes.IsEmpty then
                    return! Error(QuantumError.ValidationError("nodes", "Problem has no nodes"))
                elif problem.K <= 0 || problem.K > problem.Nodes.Length then
                    return!
                        Error(QuantumError.ValidationError("k", $"k must be between 1 and %d{problem.Nodes.Length}"))
                else
                    let qubo = toQubo problem

                    let! run = QuboSplitting.runQaoaAsync backend qubo config cancellationToken

                    let bits = run.Best
                    let optParams = run.Direct |> Option.map (fun direct -> direct.Parameters)
                    let converged = run.Direct |> Option.bind (fun direct -> direct.Converged)

                    let currentCount = bits |> Array.sum

                    // Apply constraint repair if enabled and cardinality is wrong
                    let finalBits, wasRepaired =
                        if config.EnableConstraintRepair && currentCount <> problem.K then
                            (repairConstraints problem bits, true)
                        else
                            (bits, false)

                    let solution = decode problem finalBits

                    // A split run has no single sample set to take statistics from
                    let sampling =
                        run.Direct
                        |> Option.map (fun direct ->
                            QaoaExecutionHelpers.sampleStatistics
                                bits.Length
                                (fun sample -> Array.sum sample = problem.K)
                                (fun sample -> sample = finalBits)
                                direct.Samples)

                    return
                        { solution with
                            BackendName = backend.Name
                            NumShots = config.FinalShots
                            WasRepaired = wasRepaired
                            OptimizedParameters = optParams
                            OptimizationConverged = converged
                            Sampling = sampling
                            Split = run.Split
                        }
            }

        /// Classical greedy solver for comparison
        let internal solveClassical (problem: Problem) : Solution =
            // Greedy: iteratively select node with highest marginal gain
            let nodes = List.toArray problem.Nodes
            let n = nodes.Length
            let selected = Array.zeroCreate n

            for _ in 1 .. problem.K do
                let bestIdx =
                    [ 0 .. n - 1 ]
                    |> List.filter (fun i -> selected.[i] = 0)
                    |> List.maxBy (fun i ->
                        let node = nodes.[i]
                        // Marginal gain: node score + synergy with already selected
                        let synergy =
                            problem.Edges
                            |> List.filter (fun e ->
                                (e.Source = i && selected.[e.Target] = 1)
                                || (e.Target = i && selected.[e.Source] = 1))
                            |> List.sumBy (fun e -> e.Weight * problem.SynergyWeight)

                        node.Score + synergy)

                selected.[bestIdx] <- 1

            decode problem selected
            |> fun s ->
                { s with
                    BackendName = "Classical Greedy"
                }

    // ================================================================================
    // DIVERSE SUBSET SELECTION (Quadratic Knapsack with Diversity)
    // ================================================================================
    //
    // Problem: Select items maximizing value + pairwise diversity within capacity.
    //
    // Use case: Compound selection for screening
    // - Items = compounds with activity scores and costs
    // - Diversity = chemical dissimilarity matrix
    // - Goal = select diverse, high-activity compounds within budget
    //
    // QUBO Formulation:
    //   Variables: x_i ∈ {0,1} (select item i)
    //   Maximize: Σ value_i * x_i + β * Σ_{i<j} diversity_ij * x_i * x_j
    //   Subject to: Σ cost_i * x_i ≤ budget
    //
    //   First term: item value
    //   Second term: diversity bonus for pairs
    //   Constraint: costs and budget rescaled by one common factor to integers c_i and B;
    //               binary slack s ∈ {0..B}; penalty λ * (Σ c_i * x_i + s - B)²
    //               A budget that covers every item needs no slack and no penalty.
    // ================================================================================

    module DiverseSelection =

        /// Item to select
        type Item =
            {
                Id: string
                Value: float
                Cost: float
            }

        /// Problem definition
        type Problem =
            {
                Items: Item list
                /// Pairwise diversity scores (higher = more diverse)
                Diversity: float[,]
                /// Maximum total cost
                Budget: float
                /// Weight for diversity term
                DiversityWeight: float
            }

        /// Solution result
        type Solution =
            {
                SelectedItems: Item list
                TotalValue: float
                TotalCost: float
                DiversityBonus: float
                IsFeasible: bool
                WasRepaired: bool
                BackendName: string
                NumShots: int
                OptimizedParameters: (float * float)[] option
                OptimizationConverged: bool option
                /// Standing of this solution among the final samples; None when no single sampling run produced it
                Sampling: QaoaExecutionHelpers.SampleStatistics option
                /// How the problem was split into circuits that fit the backend; None when it ran as one circuit
                Split: QaoaExecutionHelpers.SplitReport option
            }

        /// The budget constraint in integer cost units.
        type private IntegerBudget =
            {
                /// Cost of each item in integer units (cost × one common factor)
                Costs: int[]
                /// Largest total cost, in the same units, a selection may have;
                /// ValueNone when the budget covers every item, so that no selection exceeds it
                Limit: int voption
                /// Weights of the slack bits; their subset sums are exactly 0..Limit
                SlackWeights: int list
            }

        /// Put the budget constraint on integers: the costs are multiplied by one common
        /// factor f that makes them integral (Qubo.tryScaleToIntegers: the smallest power of
        /// ten, common divisor removed) and the budget becomes floor(budget·f). Integer cost
        /// sums satisfy Σ c_i·x_i ≤ budget·f exactly when they satisfy Σ c_i·x_i ≤ floor(budget·f).
        let private integerBudget (problem: Problem) : Result<IntegerBudget, QuantumError> =
            let costs = problem.Items |> List.map (fun item -> item.Cost)

            if Double.IsNaN problem.Budget || problem.Budget < 0.0 then
                Error(QuantumError.ValidationError("budget", "Budget must be a non-negative number"))
            elif costs |> List.exists (fun cost -> cost < 0.0) then
                Error(QuantumError.ValidationError("cost", "Item costs must not be negative"))
            else
                match Qubo.tryScaleToIntegers costs with
                | None ->
                    Error(
                        QuantumError.ValidationError(
                            "cost",
                            "Item costs must be finite and become integers of at most 1e9 under one power of ten up to 1e6 (at most six decimals), so that the budget constraint can be encoded on integers"
                        )
                    )
                | Some(integers, factor) ->
                    let total = integers |> List.sumBy int64

                    if total > int64 Int32.MaxValue then
                        Error(
                            QuantumError.ValidationError(
                                "cost",
                                $"Item costs sum to {total} integer cost units; the budget constraint is encoded for sums up to {Int32.MaxValue}"
                            )
                        )
                    else
                        let scaledBudget = problem.Budget * factor

                        // A product that should be integral can land just below it
                        // (0.3 × 10 = 2.9999999999999996), so the floor takes a relative tolerance.
                        let limit =
                            if scaledBudget >= float total then
                                ValueNone
                            else
                                let floored = int64 (floor (scaledBudget + 1e-9 * max 1.0 scaledBudget))

                                if floored >= total then
                                    ValueNone
                                else
                                    ValueSome(int floored)

                        Ok
                            {
                                Costs = List.toArray integers
                                Limit = limit
                                SlackWeights =
                                    match limit with
                                    | ValueSome bound -> Qubo.boundedSlackWeights bound
                                    | ValueNone -> []
                            }

        /// Total cost of the selected items in integer cost units (trailing slack bits are ignored).
        let private integerCost (budget: IntegerBudget) (bits: int[]) : int64 =
            budget.Costs
            |> Array.mapi (fun i cost -> if bits.[i] = 1 then int64 cost else 0L)
            |> Array.sum

        /// Whether the selected items fit the budget, compared in integer cost units.
        let private withinBudget (budget: IntegerBudget) (bits: int[]) : bool =
            match budget.Limit with
            | ValueNone -> true
            | ValueSome limit -> integerCost budget bits <= int64 limit

        /// Build QUBO for Diverse Subset Selection.
        ///
        /// The budget inequality Σ cost_i·x_i ≤ budget is encoded on integers: the costs are
        /// multiplied by one common factor f that makes them integral (the smallest power of
        /// ten, common divisor removed) and the budget becomes B = floor(budget·f). Binary
        /// slack bits with weights w_t whose subset sums are exactly 0..B turn it into
        ///   Σ c_i·x_i + Σ_t w_t·s_t = B
        /// penalised as λ·(Σ c_i·x_i + Σ_t w_t·s_t − B)². A selection within the budget reaches
        /// penalty 0 with slack B − Σ c_i·x_i; a selection over the budget costs at least
        /// λ = 2·(Σ|value_i| + |β|·Σ|diversity_ij| + 1), more than the objective can gain.
        /// Every minimum-energy state is therefore a best selection within the budget.
        ///
        /// A budget at or above the total cost cannot be exceeded: the QUBO then has no slack
        /// bits and no penalty, only the objective.
        ///
        /// Matrix layout: n item variables first, then the ⌊log2 B⌋ + 1 slack bits.
        /// Fails when the costs have no common integer scale (see Qubo.tryScaleToIntegers),
        /// a cost is negative, or the budget is negative or NaN.
        let tryToQubo (problem: Problem) : Result<float[,], QuantumError> =
            integerBudget problem
            |> Result.map (fun budget ->
                let items = List.toArray problem.Items
                let n = items.Length
                let beta = problem.DiversityWeight
                let numVars = n + budget.SlackWeights.Length
                let qubo = Array2D.zeroCreate numVars numVars

                // Objective: -value_i on the diagonal (maximize value) and -β·diversity_ij per
                // pair (maximize diversity), split symmetrically over [i, j] and [j, i]
                for i in 0 .. n - 1 do
                    qubo.[i, i] <- -items.[i].Value

                    for j in i + 1 .. n - 1 do
                        let diversityBonus = -beta * problem.Diversity.[i, j] / 2.0
                        qubo.[i, j] <- diversityBonus
                        qubo.[j, i] <- diversityBonus

                match budget.Limit with
                | ValueNone -> ()
                | ValueSome limit ->
                    let totalValue = problem.Items |> List.sumBy (fun item -> abs item.Value)

                    let totalDiversity =
                        [
                            for i in 0 .. n - 1 do
                                for j in i + 1 .. n - 1 do
                                    yield abs problem.Diversity.[i, j]
                        ]
                        |> List.sum

                    let penalty = 2.0 * (totalValue + abs beta * totalDiversity + 1.0)

                    // Budget equality coefficients: item costs first, then the slack weights
                    let coefficients =
                        [ for i in 0 .. n - 1 -> (i, float budget.Costs.[i]) ]
                        @ (budget.SlackWeights |> List.mapi (fun t weight -> (n + t, float weight)))

                    // λ·(Σ c_v·z_v − B)²; pair terms split symmetrically like the diversity terms
                    for KeyValue((u, v), value) in Qubo.squaredLinearPenalty penalty coefficients (-float limit) do
                        if u = v then
                            qubo.[u, u] <- qubo.[u, u] + value
                        else
                            qubo.[u, v] <- qubo.[u, v] + value / 2.0
                            qubo.[v, u] <- qubo.[v, u] + value / 2.0

                qubo)

        /// tryToQubo for a problem that is known to encode.
        /// Raises ArgumentException where tryToQubo returns an error.
        let toQubo (problem: Problem) : float[,] =
            match tryToQubo problem with
            | Ok qubo -> qubo
            | Error err -> invalidArg (nameof problem) err.Message

        /// Constraint repair: drop selected items, lowest value per cost first, until the
        /// selection is within the budget
        let private repairConstraints (problem: Problem) (budget: IntegerBudget) (bits: int[]) : int[] =
            let repaired = Array.copy bits

            match budget.Limit with
            | ValueNone -> repaired
            | ValueSome limit ->
                let items = List.toArray problem.Items

                // Items of zero cost stay: dropping them cannot bring the selection within budget
                let removalOrder =
                    [ 0 .. items.Length - 1 ]
                    |> List.filter (fun i -> repaired.[i] = 1 && budget.Costs.[i] > 0)
                    |> List.sortBy (fun i -> items.[i].Value / items.[i].Cost)

                (integerCost budget repaired, removalOrder)
                ||> List.fold (fun cost i ->
                    if cost > int64 limit then
                        repaired.[i] <- 0
                        cost - int64 budget.Costs.[i]
                    else
                        cost)
                |> ignore

                repaired

        /// Decode bitstring to solution.
        /// IsFeasible compares cost and budget on the integer cost scale of the QUBO, and on
        /// the raw cost sum where the costs have no such scale.
        let decode (problem: Problem) (bits: int[]) : Solution =
            let selected =
                problem.Items |> List.indexed |> List.filter (fun (i, _) -> bits.[i] = 1)

            let selectedItems = selected |> List.map snd
            let selectedIndices = selected |> List.map fst

            let diversity =
                [
                    for i in selectedIndices do
                        for j in selectedIndices do
                            if i < j then
                                yield problem.Diversity.[i, j]
                ]
                |> List.sum

            let totalCost = selectedItems |> List.sumBy (fun item -> item.Cost)

            let isFeasible =
                match integerBudget problem with
                | Ok budget -> withinBudget budget bits
                | Error _ -> totalCost <= problem.Budget

            {
                SelectedItems = selectedItems
                TotalValue = selectedItems |> List.sumBy (fun item -> item.Value)
                TotalCost = totalCost
                DiversityBonus = diversity * problem.DiversityWeight
                IsFeasible = isFeasible
                WasRepaired = false
                BackendName = ""
                NumShots = 0
                OptimizedParameters = None
                OptimizationConverged = None
                Sampling = None
                Split = None
            }

        /// Solve using quantum QAOA with advanced features.
        /// Sampling.Valid counts the final samples whose item bits are within the budget,
        /// Sampling.Hits those whose item bits are the returned selection.
        let solveWithConfigAsync
            (backend: BackendAbstraction.IQuantumBackend)
            (problem: Problem)
            (config: QaoaConfig)
            (cancellationToken: CancellationToken)
            : Task<Result<Solution, QuantumError>> =
            quantumResultTask {
                cancellationToken.ThrowIfCancellationRequested()

                if problem.Items.IsEmpty then
                    return! Error(QuantumError.ValidationError("items", "Problem has no items"))
                elif problem.Budget <= 0.0 then
                    return! Error(QuantumError.ValidationError("budget", "Budget must be positive"))
                else
                    let! budget = integerBudget problem
                    let! qubo = tryToQubo problem

                    let! run = QuboSplitting.runQaoaAsync backend qubo config cancellationToken

                    let bits = run.Best
                    let optParams = run.Direct |> Option.map (fun direct -> direct.Parameters)
                    let converged = run.Direct |> Option.bind (fun direct -> direct.Converged)

                    // Apply constraint repair if enabled and over budget
                    let finalBits, wasRepaired =
                        if config.EnableConstraintRepair && not (withinBudget budget bits) then
                            (repairConstraints problem budget bits, true)
                        else
                            (bits, false)

                    let solution = decode problem finalBits

                    // Only the item bits carry the selection; the trailing bits are budget slack.
                    let itemBits (sample: int[]) =
                        Array.truncate problem.Items.Length sample

                    // A split run has no single sample set to take statistics from
                    let sampling =
                        run.Direct
                        |> Option.map (fun direct ->
                            QaoaExecutionHelpers.sampleStatistics
                                bits.Length
                                (withinBudget budget)
                                (fun sample -> itemBits sample = itemBits finalBits)
                                direct.Samples)

                    return
                        { solution with
                            BackendName = backend.Name
                            NumShots = config.FinalShots
                            WasRepaired = wasRepaired
                            OptimizedParameters = optParams
                            OptimizationConverged = converged
                            Sampling = sampling
                            Split = run.Split
                        }
            }

        /// Classical greedy solver for comparison
        let internal solveClassical (problem: Problem) : Solution =
            // Greedy by value/cost ratio, considering diversity
            let n = problem.Items.Length
            let selected = Array.zeroCreate n

            let rec greedySelect remainingBudget =
                if remainingBudget <= 0.0 then
                    ()
                else
                    let candidates =
                        problem.Items
                        |> List.indexed
                        |> List.filter (fun (i, item) -> selected.[i] = 0 && item.Cost <= remainingBudget)

                    if List.isEmpty candidates then
                        ()
                    else
                        // Score: value + diversity bonus with already selected
                        let bestIdx, bestItem =
                            candidates
                            |> List.maxBy (fun (i, item) ->
                                let diversityBonus =
                                    [ 0 .. n - 1 ]
                                    |> List.filter (fun j -> selected.[j] = 1)
                                    |> List.sumBy (fun j -> problem.Diversity.[i, j] * problem.DiversityWeight)

                                item.Value + diversityBonus)

                        selected.[bestIdx] <- 1
                        greedySelect (remainingBudget - bestItem.Cost)

            greedySelect problem.Budget

            decode problem selected
            |> fun s ->
                { s with
                    BackendName = "Classical Greedy"
                }
