namespace FSharp.Azure.Quantum.Quantum

open System
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
    
    /// Configuration for QAOA execution
    type QaoaConfig = {
        /// Number of QAOA layers (p parameter). Higher p = better solutions but slower.
        NumLayers: int
        
        /// Enable Nelder-Mead parameter optimization
        EnableOptimization: bool
        
        /// Number of shots for optimization phase (lower = faster)
        OptimizationShots: int
        
        /// Number of shots for final execution (higher = better sampling)
        FinalShots: int
        
        /// Enable constraint repair post-processing
        EnableConstraintRepair: bool
        
        /// Maximum optimization iterations for Nelder-Mead
        MaxOptimizationIterations: int
    }
    
    /// Default QAOA configuration (balanced speed/quality)
    let defaultConfig : QaoaConfig = {
        NumLayers = 2
        EnableOptimization = true
        OptimizationShots = 100
        FinalShots = 1000
        EnableConstraintRepair = true
        MaxOptimizationIterations = 200
    }
    
    /// Fast configuration (for quick prototyping)
    let fastConfig : QaoaConfig = {
        NumLayers = 1
        EnableOptimization = false
        OptimizationShots = 50
        FinalShots = 500
        EnableConstraintRepair = true
        MaxOptimizationIterations = 100
    }
    
    /// High-quality configuration (for production)
    let highQualityConfig : QaoaConfig = {
        NumLayers = 3
        EnableOptimization = true
        OptimizationShots = 200
        FinalShots = 2000
        EnableConstraintRepair = true
        MaxOptimizationIterations = 500
    }

    // ================================================================================
    // SHARED UTILITIES
    // ================================================================================

    /// Evaluate QUBO objective for a bitstring
    let private evaluateQubo (qubo: float[,]) (bits: int[]) : float =
        let n = Array2D.length1 qubo
        seq {
            for i in 0 .. n - 1 do
                for j in 0 .. n - 1 do
                    yield qubo.[i, j] * float bits.[i] * float bits.[j]
        }
        |> Seq.sum
    
    /// Execute a single QAOA circuit with given parameters and return measurements
    let private executeQaoaCircuit
        (backend: BackendAbstraction.IQuantumBackend)
        (problemHam: QaoaCircuit.ProblemHamiltonian)
        (mixerHam: QaoaCircuit.MixerHamiltonian)
        (parameters: (float * float)[])
        (shots: int)
        : Result<int[][], QuantumError> =
        
        let qaoaCircuit = QaoaCircuit.QaoaCircuit.build problemHam mixerHam parameters
        let circuit = CircuitAbstraction.QaoaCircuitWrapper(qaoaCircuit) :> CircuitAbstraction.ICircuit
        
        match backend.ExecuteToState circuit with
        | Error err -> Error err
        | Ok state -> Ok (QuantumState.measure state shots)
    
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
        
        fun (flatParams: float[]) ->
            // Convert flat array to (gamma, beta) pairs
            let parameters = 
                Array.init numLayers (fun i ->
                    let gamma = flatParams.[2 * i]
                    let beta = flatParams.[2 * i + 1]
                    (gamma, beta))
            
            match executeQaoaCircuit backend problemHam mixerHam parameters shots with
            | Error _ -> System.Double.MaxValue  // Penalty for failed execution
            | Ok measurements ->
                // Calculate average QUBO energy across all measurements
                measurements
                |> Array.map (fun bits -> evaluateQubo qubo bits)
                |> Array.average
    
    /// Execute QAOA with Nelder-Mead parameter optimization
    let private executeQaoaWithOptimization
        (backend: BackendAbstraction.IQuantumBackend)
        (qubo: float[,])
        (config: QaoaConfig)
        : Result<int[] * (float * float)[] * bool, QuantumError> =
        
        let n = Array2D.length1 qubo
        let problemHam = QaoaCircuit.ProblemHamiltonian.fromQubo qubo
        let mixerHam = QaoaCircuit.MixerHamiltonian.create n
        
        // Create objective function for optimization
        let objectiveFunc = createObjectiveFunction backend qubo problemHam mixerHam config.NumLayers config.OptimizationShots
        
        // Initial parameters: use standard QAOA heuristic
        // γ ∈ [0, π/2], β ∈ [0, π/4] works well for many problems
        let rng = Random(42)  // Fixed seed for reproducibility
        let initialParams = 
            Array.init (2 * config.NumLayers) (fun i ->
                if i % 2 = 0 then 
                    rng.NextDouble() * (Math.PI / 2.0)  // gamma
                else 
                    rng.NextDouble() * (Math.PI / 4.0)) // beta
        
        // Parameter bounds
        let lowerBounds = Array.init (2 * config.NumLayers) (fun i ->
            if i % 2 = 0 then 0.0 else 0.0)
        let upperBounds = Array.init (2 * config.NumLayers) (fun i ->
            if i % 2 = 0 then Math.PI else Math.PI / 2.0)
        
        // Run Nelder-Mead optimization (may throw MaximumIterationsException)
        let optimResult, converged = 
            try
                let result = QaoaOptimizer.Optimizer.minimizeWithBounds 
                                objectiveFunc initialParams lowerBounds upperBounds
                (result, result.Converged)
            with
            | :? MathNet.Numerics.Optimization.MaximumIterationsException ->
                // Optimizer didn't converge - use initial parameters as fallback
                { QaoaOptimizer.OptimizationResult.OptimizedParameters = initialParams
                  QaoaOptimizer.OptimizationResult.FinalObjectiveValue = System.Double.MaxValue
                  QaoaOptimizer.OptimizationResult.Converged = false
                  QaoaOptimizer.OptimizationResult.Iterations = config.MaxOptimizationIterations }, false
        
        // Extract optimized parameters (or initial if optimization failed)
        let optimizedParams =
            Array.init config.NumLayers (fun i ->
                (optimResult.OptimizedParameters.[2 * i], 
                 optimResult.OptimizedParameters.[2 * i + 1]))
        
        // Execute final circuit with optimized parameters and more shots
        match executeQaoaCircuit backend problemHam mixerHam optimizedParams config.FinalShots with
        | Error err -> Error err
        | Ok measurements ->
            // Find best solution (lowest QUBO energy)
            let bestSolution =
                measurements
                |> Array.minBy (fun bits -> evaluateQubo qubo bits)
            
            Ok (bestSolution, optimizedParams, converged)
    
    /// Execute QAOA with grid search (fallback when optimization disabled)
    let private executeQaoaWithGridSearch
        (backend: BackendAbstraction.IQuantumBackend)
        (qubo: float[,])
        (config: QaoaConfig)
        : Result<int[] * (float * float)[], QuantumError> =
        
        let n = Array2D.length1 qubo
        let problemHam = QaoaCircuit.ProblemHamiltonian.fromQubo qubo
        let mixerHam = QaoaCircuit.MixerHamiltonian.create n
        
        // Grid search parameter sets for multi-layer QAOA
        let gammaValues = [| 0.1; 0.3; 0.5; 0.7; 1.0; 1.5; Math.PI / 4.0 |]
        let betaValues = [| 0.1; 0.3; 0.5; 0.7; 1.0 |]
        
        let initialState = {| BestSolution = None; BestEnergy = System.Double.MaxValue; BestParams = Array.empty<float * float>; LastError = None |}
        
        // Try different parameter combinations
        let result =
            (initialState, seq {
                for gamma in gammaValues do
                    for beta in betaValues do
                        yield (gamma, beta)
            })
            ||> Seq.fold (fun state (gamma, beta) ->
                // Create multi-layer parameters (same gamma/beta for each layer)
                let parameters = Array.init config.NumLayers (fun _ -> (gamma, beta))
                
                match executeQaoaCircuit backend problemHam mixerHam parameters config.FinalShots with
                | Error err -> 
                    {| state with LastError = Some err |}
                | Ok measurements ->
                    // Find best measurement in this batch
                    let candidate = 
                        measurements
                        |> Array.minBy (fun bits -> evaluateQubo qubo bits)
                    
                    let energy = evaluateQubo qubo candidate
                    if energy < state.BestEnergy then
                        {| state with BestSolution = Some candidate; BestEnergy = energy; BestParams = parameters |}
                    else
                        state)
        
        match result.BestSolution with
        | Some solution -> Ok (solution, result.BestParams)
        | None -> 
            match result.LastError with
            | Some err -> Error err
            | None -> Error (QuantumError.OperationError ("QAOA", "No valid solution found"))

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
        type Node = {
            Id: string
            Weight: float
        }
        
        /// Independent set problem
        type Problem = {
            Nodes: Node list
            /// Edges represent conflicts (adjacent nodes cannot both be selected)
            Edges: (int * int) list
        }
        
        /// Solution result
        type Solution = {
            SelectedNodes: Node list
            TotalWeight: float
            IsValid: bool  // No selected nodes are adjacent
            WasRepaired: bool  // Whether constraint repair was applied
            BackendName: string
            NumShots: int
            OptimizedParameters: (float * float)[] option
            OptimizationConverged: bool option
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
            
            // Find and fix violations
            for (i, j) in problem.Edges do
                if repaired.[i] = 1 && repaired.[j] = 1 then
                    // Both selected but adjacent - remove the one with lower weight
                    let wi = problem.Nodes.[i].Weight
                    let wj = problem.Nodes.[j].Weight
                    if wi >= wj then
                        repaired.[j] <- 0
                    else
                        repaired.[i] <- 0
            
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
            }
        
        /// Solve using quantum QAOA with advanced features
        let solveWithConfig
            (backend: BackendAbstraction.IQuantumBackend)
            (problem: Problem)
            (config: QaoaConfig)
            : Result<Solution, QuantumError> =
            
            if problem.Nodes.IsEmpty then
                Error (QuantumError.ValidationError ("nodes", "Problem has no nodes"))
            else
                let qubo = toQubo problem
                
                let result =
                    if config.EnableOptimization then
                        executeQaoaWithOptimization backend qubo config
                        |> Result.map (fun (bits, optParams, converged) -> 
                            (bits, Some optParams, Some converged))
                    else
                        executeQaoaWithGridSearch backend qubo config
                        |> Result.map (fun (bits, optParams) -> 
                            (bits, Some optParams, None))
                
                match result with
                | Error err -> Error err
                | Ok (bits, optParams, converged) ->
                    // Apply constraint repair if enabled and solution is invalid
                    let finalBits, wasRepaired =
                        if config.EnableConstraintRepair && not (isValid problem bits) then
                            (repairConstraints problem bits, true)
                        else
                            (bits, false)
                    
                    let solution = decode problem finalBits
                    Ok { solution with 
                            BackendName = backend.Name
                            NumShots = config.FinalShots
                            WasRepaired = wasRepaired
                            OptimizedParameters = optParams
                            OptimizationConverged = converged }
        
        /// Solve using quantum QAOA with default configuration
        let solve 
            (backend: BackendAbstraction.IQuantumBackend)
            (problem: Problem)
            (shots: int)
            : Result<Solution, QuantumError> =
            
            let config = { defaultConfig with FinalShots = shots }
            solveWithConfig backend problem config
        
        /// Classical greedy solver for comparison
        let solveClassical (problem: Problem) : Solution =
            // Greedy: sort by weight, add if no conflict
            let sorted = 
                problem.Nodes 
                |> List.indexed 
                |> List.sortByDescending (fun (_, n) -> n.Weight)
            
            let n = problem.Nodes.Length
            let selected = Array.zeroCreate n
            let adjacency = 
                problem.Edges
                |> List.collect (fun (i, j) -> [(i, j); (j, i)])
                |> List.groupBy fst
                |> List.map (fun (k, vs) -> k, vs |> List.map snd |> Set.ofList)
                |> Map.ofList
            
            for (idx, _) in sorted do
                let neighbors = adjacency |> Map.tryFind idx |> Option.defaultValue Set.empty
                let hasConflict = neighbors |> Set.exists (fun j -> selected.[j] = 1)
                if not hasConflict then
                    selected.[idx] <- 1
            
            decode problem selected
            |> fun s -> { s with BackendName = "Classical Greedy" }

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
    //   Constraint: encoded as penalty λ * (Σ x_i - k)²
    // ================================================================================

    module InfluenceMaximization =
        
        /// Node in influence network
        type Node = {
            Id: string
            /// Importance score (e.g., disease relevance)
            Score: float
        }
        
        /// Edge representing interaction
        type Edge = {
            Source: int
            Target: int
            /// Interaction strength
            Weight: float
        }
        
        /// Problem definition
        type Problem = {
            Nodes: Node list
            Edges: Edge list
            /// Number of nodes to select
            K: int
            /// Weight for synergy term (default 0.5)
            SynergyWeight: float
        }
        
        /// Solution result
        type Solution = {
            SelectedNodes: Node list
            TotalScore: float
            SynergyBonus: float
            NumSelected: int  // For checking cardinality constraint
            WasRepaired: bool
            BackendName: string
            NumShots: int
            OptimizedParameters: (float * float)[] option
            OptimizationConverged: bool option
        }
        
        /// Build QUBO for Influence Maximization
        /// 
        /// Uses Lucas Rule penalty: penalty = numOptions * objectiveMagnitude * 10.0
        /// This ensures the cardinality constraint dominates the objective.
        let toQubo (problem: Problem) : float[,] =
            let n = problem.Nodes.Length
            let k = problem.K
            let alpha = problem.SynergyWeight
            let qubo = Array2D.zeroCreate n n
            
            // Penalty for cardinality constraint (must select exactly k)
            // Use Lucas Rule: penalty = n * maxObjective * 10.0
            let maxScore = problem.Nodes |> List.map (fun node -> abs node.Score) |> List.max
            let totalScore = problem.Nodes |> List.sumBy (fun node -> abs node.Score)
            let objectiveMagnitude = max maxScore totalScore
            let penalty = float n * objectiveMagnitude * 10.0
            
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
                bits |> Array.indexed |> Array.filter (fun (_, b) -> b = 1) |> Array.map fst |> Set.ofArray
            
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
            }
        
        /// Solve using quantum QAOA with advanced features
        let solveWithConfig
            (backend: BackendAbstraction.IQuantumBackend)
            (problem: Problem)
            (config: QaoaConfig)
            : Result<Solution, QuantumError> =
            
            if problem.Nodes.IsEmpty then
                Error (QuantumError.ValidationError ("nodes", "Problem has no nodes"))
            elif problem.K <= 0 || problem.K > problem.Nodes.Length then
                Error (QuantumError.ValidationError ("k", sprintf "k must be between 1 and %d" problem.Nodes.Length))
            else
                let qubo = toQubo problem
                
                let result =
                    if config.EnableOptimization then
                        executeQaoaWithOptimization backend qubo config
                        |> Result.map (fun (bits, optParams, converged) -> 
                            (bits, Some optParams, Some converged))
                    else
                        executeQaoaWithGridSearch backend qubo config
                        |> Result.map (fun (bits, optParams) -> 
                            (bits, Some optParams, None))
                
                match result with
                | Error err -> Error err
                | Ok (bits, optParams, converged) ->
                    let currentCount = bits |> Array.sum
                    
                    // Apply constraint repair if enabled and cardinality is wrong
                    let finalBits, wasRepaired =
                        if config.EnableConstraintRepair && currentCount <> problem.K then
                            (repairConstraints problem bits, true)
                        else
                            (bits, false)
                    
                    let solution = decode problem finalBits
                    Ok { solution with 
                            BackendName = backend.Name
                            NumShots = config.FinalShots
                            WasRepaired = wasRepaired
                            OptimizedParameters = optParams
                            OptimizationConverged = converged }
        
        /// Solve using quantum QAOA with default configuration
        let solve 
            (backend: BackendAbstraction.IQuantumBackend)
            (problem: Problem)
            (shots: int)
            : Result<Solution, QuantumError> =
            
            let config = { defaultConfig with FinalShots = shots }
            solveWithConfig backend problem config
        
        /// Classical greedy solver for comparison
        let solveClassical (problem: Problem) : Solution =
            // Greedy: iteratively select node with highest marginal gain
            let n = problem.Nodes.Length
            let selected = Array.zeroCreate n
            
            for _ in 1 .. problem.K do
                let bestIdx = 
                    [0 .. n - 1]
                    |> List.filter (fun i -> selected.[i] = 0)
                    |> List.maxBy (fun i ->
                        let node = problem.Nodes.[i]
                        // Marginal gain: node score + synergy with already selected
                        let synergy = 
                            problem.Edges
                            |> List.filter (fun e -> 
                                (e.Source = i && selected.[e.Target] = 1) ||
                                (e.Target = i && selected.[e.Source] = 1))
                            |> List.sumBy (fun e -> e.Weight * problem.SynergyWeight)
                        node.Score + synergy)
                selected.[bestIdx] <- 1
            
            decode problem selected
            |> fun s -> { s with BackendName = "Classical Greedy" }

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
    //   Constraint: capacity penalty λ * max(0, Σ cost_i * x_i - budget)²
    // ================================================================================

    module DiverseSelection =
        
        /// Item to select
        type Item = {
            Id: string
            Value: float
            Cost: float
        }
        
        /// Problem definition  
        type Problem = {
            Items: Item list
            /// Pairwise diversity scores (higher = more diverse)
            Diversity: float[,]
            /// Maximum total cost
            Budget: float
            /// Weight for diversity term
            DiversityWeight: float
        }
        
        /// Solution result
        type Solution = {
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
        }
        
        /// Build QUBO for Diverse Subset Selection
        let toQubo (problem: Problem) : float[,] =
            let n = problem.Items.Length
            let beta = problem.DiversityWeight
            let budget = problem.Budget
            let qubo = Array2D.zeroCreate n n
            
            // Penalty weight for budget constraint
            let maxValue = problem.Items |> List.sumBy (fun item -> abs item.Value)
            let maxDiversity = 
                [ for i in 0 .. n - 1 do
                    for j in i + 1 .. n - 1 do
                        yield abs problem.Diversity.[i, j] ]
                |> List.sum
            let penalty = 2.0 * (maxValue + beta * maxDiversity + 1.0)
            
            // Linear terms:
            // From objective: -value_i (maximize value)
            // From constraint: λ * (cost_i² - 2*budget*cost_i)
            for i, item in problem.Items |> List.indexed do
                let costTerm = penalty * (item.Cost * item.Cost - 2.0 * budget * item.Cost)
                qubo.[i, i] <- -item.Value + costTerm
            
            // Quadratic terms:
            // From diversity: -β * diversity_ij (maximize diversity, negated)
            // From constraint: λ * 2 * cost_i * cost_j
            for i in 0 .. n - 1 do
                for j in i + 1 .. n - 1 do
                    let diversityBonus = -beta * problem.Diversity.[i, j] / 2.0
                    let costPenalty = penalty * problem.Items.[i].Cost * problem.Items.[j].Cost
                    qubo.[i, j] <- qubo.[i, j] + diversityBonus + costPenalty
                    qubo.[j, i] <- qubo.[j, i] + diversityBonus + costPenalty
            
            qubo
        
        /// Constraint repair: remove items to get within budget (remove lowest value first)
        let private repairConstraints (problem: Problem) (bits: int[]) : int[] =
            let repaired = Array.copy bits
            let currentCost = 
                problem.Items
                |> List.indexed
                |> List.filter (fun (i, _) -> repaired.[i] = 1)
                |> List.sumBy (fun (_, item) -> item.Cost)
            
            if currentCost <= problem.Budget then
                repaired
            else
                // Remove items until within budget (remove lowest value/cost ratio first)
                let selected =
                    problem.Items
                    |> List.indexed
                    |> List.filter (fun (i, _) -> repaired.[i] = 1)
                    |> List.sortBy (fun (_, item) -> 
                        // Guard against division by zero - if cost is 0, item is "free" so keep it (high ratio)
                        if item.Cost <= 0.0 then System.Double.MaxValue
                        else item.Value / item.Cost)  // Remove worst ratio first
                
                let _finalCost =
                    (currentCost, selected)
                    ||> List.fold (fun cost (i, item) ->
                        if cost > problem.Budget then
                            repaired.[i] <- 0
                            cost - item.Cost
                        else
                            cost)
                
                repaired
        
        /// Decode bitstring to solution
        let decode (problem: Problem) (bits: int[]) : Solution =
            let selected = 
                problem.Items
                |> List.indexed
                |> List.filter (fun (i, _) -> bits.[i] = 1)
            
            let selectedItems = selected |> List.map snd
            let selectedIndices = selected |> List.map fst
            
            let diversity = 
                [ for i in selectedIndices do
                    for j in selectedIndices do
                        if i < j then yield problem.Diversity.[i, j] ]
                |> List.sum
            
            let totalCost = selectedItems |> List.sumBy (fun item -> item.Cost)
            
            {
                SelectedItems = selectedItems
                TotalValue = selectedItems |> List.sumBy (fun item -> item.Value)
                TotalCost = totalCost
                DiversityBonus = diversity * problem.DiversityWeight
                IsFeasible = totalCost <= problem.Budget
                WasRepaired = false
                BackendName = ""
                NumShots = 0
                OptimizedParameters = None
                OptimizationConverged = None
            }
        
        /// Solve using quantum QAOA with advanced features
        let solveWithConfig
            (backend: BackendAbstraction.IQuantumBackend)
            (problem: Problem)
            (config: QaoaConfig)
            : Result<Solution, QuantumError> =
            
            if problem.Items.IsEmpty then
                Error (QuantumError.ValidationError ("items", "Problem has no items"))
            elif problem.Budget <= 0.0 then
                Error (QuantumError.ValidationError ("budget", "Budget must be positive"))
            else
                let qubo = toQubo problem
                
                let result =
                    if config.EnableOptimization then
                        executeQaoaWithOptimization backend qubo config
                        |> Result.map (fun (bits, optParams, converged) -> 
                            (bits, Some optParams, Some converged))
                    else
                        executeQaoaWithGridSearch backend qubo config
                        |> Result.map (fun (bits, optParams) -> 
                            (bits, Some optParams, None))
                
                match result with
                | Error err -> Error err
                | Ok (bits, optParams, converged) ->
                    let currentCost = 
                        problem.Items
                        |> List.indexed
                        |> List.filter (fun (i, _) -> bits.[i] = 1)
                        |> List.sumBy (fun (_, item) -> item.Cost)
                    
                    // Apply constraint repair if enabled and over budget
                    let finalBits, wasRepaired =
                        if config.EnableConstraintRepair && currentCost > problem.Budget then
                            (repairConstraints problem bits, true)
                        else
                            (bits, false)
                    
                    let solution = decode problem finalBits
                    Ok { solution with 
                            BackendName = backend.Name
                            NumShots = config.FinalShots
                            WasRepaired = wasRepaired
                            OptimizedParameters = optParams
                            OptimizationConverged = converged }
        
        /// Solve using quantum QAOA with default configuration
        let solve 
            (backend: BackendAbstraction.IQuantumBackend)
            (problem: Problem)
            (shots: int)
            : Result<Solution, QuantumError> =
            
            let config = { defaultConfig with FinalShots = shots }
            solveWithConfig backend problem config
        
        /// Classical greedy solver for comparison
        let solveClassical (problem: Problem) : Solution =
            // Greedy by value/cost ratio, considering diversity
            let n = problem.Items.Length
            let selected = Array.zeroCreate n
            
            let rec greedySelect remainingBudget =
                if remainingBudget <= 0.0 then ()
                else
                    let candidates = 
                        problem.Items
                        |> List.indexed
                        |> List.filter (fun (i, item) -> 
                            selected.[i] = 0 && item.Cost <= remainingBudget)
                    
                    if List.isEmpty candidates then ()
                    else
                        // Score: value + diversity bonus with already selected
                        let bestIdx, bestItem = 
                            candidates
                            |> List.maxBy (fun (i, item) ->
                                let diversityBonus = 
                                    [0 .. n - 1]
                                    |> List.filter (fun j -> selected.[j] = 1)
                                    |> List.sumBy (fun j -> problem.Diversity.[i, j] * problem.DiversityWeight)
                                item.Value + diversityBonus)
                        
                        selected.[bestIdx] <- 1
                        greedySelect (remainingBudget - bestItem.Cost)
            
            greedySelect problem.Budget
            
            decode problem selected
            |> fun s -> { s with BackendName = "Classical Greedy" }
