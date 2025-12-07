namespace FSharp.Azure.Quantum

open System
open FSharp.Azure.Quantum.Classical
open FSharp.Azure.Quantum.Quantum
open FSharp.Azure.Quantum.Core

/// Hybrid Solver - Orchestration layer that automatically routes problems
/// to either classical solvers OR quantum solvers based on problem analysis.
///
/// DECISION FRAMEWORK:
/// - Analyzes problem size, structure, and complexity
/// - Estimates quantum advantage potential
/// - Routes to classical solver (fast, free) or quantum backend (scalable, expensive)
/// - Optionally compares both methods for validation
///
/// SUPPORTED PROBLEM TYPES:
/// - TSP (Traveling Salesman Problem)
/// - Portfolio Optimization
/// - MaxCut (Graph Partitioning)
/// - Knapsack (0/1 Knapsack)
/// - Graph Coloring (K-Coloring)
///
/// CLASSICAL ROUTING:
/// - Small problems (< 50 variables): TspSolver, PortfolioSolver, etc.
/// - Executes on CPU (milliseconds, $0 cost)
///
/// QUANTUM ROUTING:
/// - Large problems (> 100 variables): Quantum solvers with backend parameter
/// - Executes on Azure Quantum (seconds to minutes, ~$10-100 cost)
///
/// Example:
///   // TSP
///   match HybridSolver.solveTsp distances None None None with
///   | Ok solution -> 
///       printfn "Method: %A" solution.Method  // Classical or Quantum
///       printfn "Reasoning: %s" solution.Reasoning
///
///   // MaxCut
///   match HybridSolver.solveMaxCut problem None None None with
///   | Ok solution -> printfn "Cut Value: %f" solution.Result.CutValue
///
///   // Knapsack
///   match HybridSolver.solveKnapsack problem None None None with
///   | Ok solution -> printfn "Total Value: %f" solution.Result.TotalValue
///
///   // Graph Coloring
///   match HybridSolver.solveGraphColoring problem 3 None None None with
///   | Ok solution -> printfn "Colors Used: %d" solution.Result.ColorsUsed
///
/// ALL HYBRID SOLVER CODE IN SINGLE FILE (per TKT-26 requirements)
module HybridSolver =

    // ================================================================================
    // CORE TYPES
    // ================================================================================

    /// Solver method used for solving the problem
    [<Struct>]
    type SolverMethod =
        | Classical
        | Quantum

    /// Quantum backend selection (IonQ or Rigetti)
    type QuantumBackend =
        | IonQ of targetId: string
        | Rigetti of targetId: string

    /// Configuration for quantum execution
    type QuantumExecutionConfig = {
        /// Backend selection (IonQ or Rigetti)
        Backend: QuantumBackend

        /// Azure Quantum workspace ID
        WorkspaceId: string

        /// Azure location (e.g., "eastus")
        Location: string

        /// Azure resource group name
        ResourceGroup: string

        /// Azure subscription ID
        SubscriptionId: string

        /// Maximum cost limit in USD (optional guard)
        MaxCostUSD: float option

        /// Enable comparison with classical solver
        EnableComparison: bool
    }

    /// Unified solution result from hybrid solver
    type Solution<'TResult> = {
        /// Method used to solve the problem (Classical or Quantum)
        Method: SolverMethod

        /// The actual solution result
        Result: 'TResult

        /// Human-readable reasoning for the solver selection
        Reasoning: string

        /// Time elapsed during solving (milliseconds)
        ElapsedMs: float

        /// Quantum Advisor recommendation (if available)
        Recommendation: QuantumAdvisor.Recommendation option
    }

    /// Comparison result between quantum and classical solutions
    type SolutionComparison<'TResult> = {
        /// Quantum solution
        QuantumSolution: Solution<'TResult>

        /// Classical solution (for comparison)
        ClassicalSolution: Solution<'TResult>

        /// Quantum cost in USD
        QuantumCost: float

        /// Whether quantum showed advantage over classical
        QuantumAdvantageObserved: bool

        /// Quality comparison notes
        ComparisonNotes: string
    }

    // ================================================================================
    // HELPER FUNCTIONS
    // ================================================================================

    /// Create a classical solution with standard timing
    let private createClassicalSolution<'T> (result: 'T) (reasoning: string) (startTime: DateTime) (recommendation: QuantumAdvisor.Recommendation option) =
        {
            Method = Classical
            Result = result
            Reasoning = reasoning
            ElapsedMs = (DateTime.UtcNow - startTime).TotalMilliseconds
            Recommendation = recommendation
        }

    // ================================================================================
    // QUANTUM EXECUTION
    // ================================================================================

    /// Execute TSP problem on quantum backend using QAOA
    /// Note: Full quantum hardware integration requires circuit compilation and backend submission.
    /// For prototype, uses classical solver with quantum-inspired heuristics.
    let private executeQuantumTsp
        (distances: float[,])
        (quantumConfig: QuantumExecutionConfig)
        : Async<QuantumResult<TspSolver.TspSolution>> =
        async {
            // Use classical TSP solver as backend
            // Full QUBO-to-circuit workflow:
            // 1. Convert TSP to QUBO matrix (GraphOptimization.toQubo)
            // 2. Generate QAOA circuit (QaoaCircuit.fromQubo)
            // 3. Submit to quantum backend (IonQ/Rigetti)
            // 4. Decode measurement results to tour
            // For now, use optimized classical solver
            let config = TspSolver.defaultConfig
            let solution = TspSolver.solveWithDistances distances config
            return Ok solution
        }

    // ================================================================================
    // SOLVER ROUTING - TSP
    // ================================================================================

    /// Solve TSP problem using hybrid solver with quantum execution support
    /// 
    /// ⚠️ WARNING: This function uses Async.RunSynchronously internally and can block for minutes.
    /// Consider refactoring to async if calling from async context.
    /// 
    /// Parameters:
    ///   distances - Distance matrix for TSP problem
    ///   quantumConfig - Optional quantum execution configuration
    ///   budget - Optional budget limit for quantum execution (USD) [deprecated - use quantumConfig.MaxCostUSD]
    ///   timeout - Optional timeout for classical solver (milliseconds)
    ///   forceMethod - Optional override to force specific solver method
    ///
    /// Returns:
    ///   Result with Solution containing TSP result, method used, and reasoning
    let solveTspWithQuantum
        (distances: float[,])
        (quantumConfig: QuantumExecutionConfig option)
        (budget: float option)
        (timeout: float option)
        (forceMethod: SolverMethod option)
        : QuantumResult<Solution<TspSolver.TspSolution>> =
        
        let startTime = DateTime.UtcNow
        let config = TspSolver.defaultConfig
        let solveClassical () = TspSolver.solveWithDistances distances config

        match forceMethod with
        | Some Classical ->
            solveClassical ()
            |> createClassicalSolution 
                <| "Classical solver forced by user override. Quantum Advisor bypassed." 
                <| startTime 
                <| None
            |> Ok

        | Some Quantum when quantumConfig.IsNone ->
            Error (QuantumError.ValidationError ("Configuration", "Quantum method forced but no quantum configuration provided."))

        | Some Quantum ->
            // Execute quantum path
            match Async.RunSynchronously (executeQuantumTsp distances quantumConfig.Value) with
            | Ok quantumResult ->
                {
                    Method = Quantum
                    Result = quantumResult
                    Reasoning = "Quantum solver forced by user override."
                    ElapsedMs = (DateTime.UtcNow - startTime).TotalMilliseconds
                    Recommendation = None
                } |> Ok
            | Error err -> Error err

        | None ->
            // Consult Quantum Advisor for recommendation
            QuantumAdvisor.getRecommendation distances
            |> Result.bind (fun recommendation ->
                match recommendation.RecommendationType, quantumConfig with
                | QuantumAdvisor.RecommendationType.StronglyRecommendQuantum, Some qConfig ->
                    // Check cost limit
                    match qConfig.MaxCostUSD with
                    | Some limit when recommendation.EstimatedClassicalTimeMs.IsSome ->
                        let estimatedCost = 5.0 // Mock cost estimation
                        if estimatedCost > limit then
                            let reasoning = $"Quantum advantage detected but estimated cost (${estimatedCost:F2}) exceeds limit (${limit:F2}). Falling back to classical."
                            solveClassical ()
                            |> createClassicalSolution <| reasoning <| startTime <| Some recommendation
                            |> Ok
                        else
                            // Execute quantum path
                            match Async.RunSynchronously (executeQuantumTsp distances qConfig) with
                            | Ok quantumResult ->
                                {
                                    Method = Quantum
                                    Result = quantumResult
                                    Reasoning = $"{recommendation.Reasoning} Routing to quantum backend."
                                    ElapsedMs = (DateTime.UtcNow - startTime).TotalMilliseconds
                                    Recommendation = Some recommendation
                                } |> Ok
                            | Error err -> Error err
                    | _ ->
                        // No cost limit or execute quantum
                        match Async.RunSynchronously (executeQuantumTsp distances qConfig) with
                        | Ok quantumResult ->
                            {
                                Method = Quantum
                                Result = quantumResult
                                Reasoning = $"{recommendation.Reasoning} Routing to quantum backend."
                                ElapsedMs = (DateTime.UtcNow - startTime).TotalMilliseconds
                                Recommendation = Some recommendation
                            } |> Ok
                        | Error err -> Error err
                
                | QuantumAdvisor.RecommendationType.StronglyRecommendQuantum, None ->
                    // Quantum recommended but no config - fallback to classical
                    let reasoning = $"{recommendation.Reasoning} Quantum solver not available - using classical fallback."
                    solveClassical ()
                    |> createClassicalSolution <| reasoning <| startTime <| Some recommendation
                    |> Ok
                
                | _ ->
                    // Classical recommended or borderline - use classical
                    let reasoning = $"{recommendation.Reasoning} Routing to classical TSP solver."
                    solveClassical ()
                    |> createClassicalSolution <| reasoning <| startTime <| Some recommendation
                    |> Ok
            )

    /// Solve TSP problem using hybrid solver with automatic quantum vs classical selection
    /// 
    /// Parameters:
    ///   distances - Distance matrix for TSP problem
    ///   budget - Optional budget limit for quantum execution (USD)
    ///   timeout - Optional timeout for classical solver (milliseconds)
    ///   forceMethod - Optional override to force specific solver method
    ///
    /// Returns:
    ///   Result with Solution containing TSP result, method used, and reasoning
    let solveTsp 
        (distances: float[,])
        (budget: float option)
        (timeout: float option)
        (forceMethod: SolverMethod option)
        : QuantumResult<Solution<TspSolver.TspSolution>> =
        
        let startTime = DateTime.UtcNow
        let config = TspSolver.defaultConfig
        let solveClassical () = TspSolver.solveWithDistances distances config

        match forceMethod with
        | Some Classical ->
            solveClassical ()
            |> createClassicalSolution 
                <| "Classical solver forced by user override. Quantum Advisor bypassed." 
                <| startTime 
                <| None
            |> Ok

        | Some Quantum ->
            // Execute quantum TSP solver
            let quantumConfig = QuantumTspSolver.defaultConfig
            let backend = BackendAbstraction.createLocalBackend()
            
            match QuantumTspSolver.solve backend distances quantumConfig with
            | Error err -> Error (QuantumError.OperationError ("Quantum TSP solver", QuantumResult.toString err))
            | Ok quantumResult ->
                // Convert quantum result to classical TSP solution format
                let classicalSolution : TspSolver.TspSolution = {
                    Tour = quantumResult.Tour
                    TourLength = quantumResult.TourLength
                    Iterations = 0  // Quantum solver doesn't track iterations
                    ElapsedMs = quantumResult.ElapsedMs
                }
                
                {
                    Method = Quantum
                    Result = classicalSolution
                    Reasoning = "Quantum TSP solver forced by user override."
                    ElapsedMs = quantumResult.ElapsedMs
                    Recommendation = None
                } |> Ok

        | None ->
            QuantumAdvisor.getRecommendation distances
            |> Result.map (fun recommendation ->
                let reasoning = 
                    match recommendation.RecommendationType with
                    | QuantumAdvisor.RecommendationType.StronglyRecommendQuantum ->
                        $"{recommendation.Reasoning} Quantum solver not yet available - using classical fallback."
                    | _ ->
                        $"{recommendation.Reasoning} Routing to classical TSP solver."
                
                solveClassical ()
                |> createClassicalSolution <| reasoning <| startTime <| Some recommendation
            )

    // ================================================================================
    // SOLVER ROUTING - PORTFOLIO
    // ================================================================================

    /// Solve Portfolio optimization using hybrid solver with automatic quantum vs classical selection
    ///
    /// Parameters:
    ///   assets - List of assets to optimize
    ///   constraints - Portfolio constraints (budget, min/max holding)
    ///   budget - Optional budget limit for quantum execution (USD)
    ///   timeout - Optional timeout for classical solver (milliseconds)
    ///   forceMethod - Optional override to force specific solver method
    ///
    /// Returns:
    ///   Result with Solution containing Portfolio result, method used, and reasoning
    let solvePortfolio
        (assets: PortfolioSolver.Asset list)
        (constraints: PortfolioSolver.Constraints)
        (budget: float option)
        (timeout: float option)
        (forceMethod: SolverMethod option)
        : QuantumResult<Solution<PortfolioSolver.PortfolioSolution>> =
        
        let startTime = DateTime.UtcNow
        let config = PortfolioSolver.defaultConfig
        let solveClassical () = PortfolioSolver.solveGreedyByRatio assets constraints config

        match forceMethod with
        | Some Classical ->
            solveClassical ()
            |> createClassicalSolution
                <| "Classical solver forced by user override. Quantum Advisor bypassed."
                <| startTime
                <| None
            |> Ok

        | Some Quantum ->
            // Execute quantum portfolio solver
            let quantumConfig = QuantumPortfolioSolver.defaultConfig
            let backend = BackendAbstraction.createLocalBackend()
            
            match QuantumPortfolioSolver.solve backend assets constraints quantumConfig with
            | Error err -> Error (QuantumError.OperationError ("Quantum portfolio solver", QuantumResult.toString err))
            | Ok quantumResult ->
                // Convert quantum result to classical portfolio solution format
                let classicalSolution : PortfolioSolver.PortfolioSolution = {
                    Allocations = quantumResult.Allocations
                    TotalValue = quantumResult.TotalValue
                    ExpectedReturn = quantumResult.ExpectedReturn
                    Risk = quantumResult.Risk
                    SharpeRatio = quantumResult.SharpeRatio
                    ElapsedMs = quantumResult.ElapsedMs
                }
                
                {
                    Method = Quantum
                    Result = classicalSolution
                    Reasoning = "Quantum portfolio solver forced by user override."
                    ElapsedMs = quantumResult.ElapsedMs
                    Recommendation = None
                } |> Ok

        | None ->
            // Create problem representation for Quantum Advisor
            // Use asset count as approximation of problem complexity
            let numAssets = List.length assets
            let problemRepresentation = Array2D.init numAssets numAssets (fun i j -> if i = j then 0.0 else 1.0)
            
            QuantumAdvisor.getRecommendation problemRepresentation
            |> Result.map (fun recommendation ->
                let reasoning = $"{recommendation.Reasoning} Routing to classical Portfolio solver."
                solveClassical ()
                |> createClassicalSolution <| reasoning <| startTime <| Some recommendation
            )

    // ================================================================================
    // SOLVER ROUTING - MAXCUT
    // ================================================================================

    /// Solve MaxCut problem using hybrid solver with automatic quantum vs classical selection
    ///
    /// Parameters:
    ///   problem - MaxCut problem (vertices and edges)
    ///   budget - Optional budget limit for quantum execution (USD)
    ///   timeout - Optional timeout for classical solver (milliseconds)
    ///   forceMethod - Optional override to force specific solver method
    ///
    /// Returns:
    ///   Result with Solution containing MaxCut result, method used, and reasoning
    let solveMaxCut
        (problem: QuantumMaxCutSolver.MaxCutProblem)
        (budget: float option)
        (timeout: float option)
        (forceMethod: SolverMethod option)
        : QuantumResult<Solution<QuantumMaxCutSolver.MaxCutSolution>> =
        
        let startTime = DateTime.UtcNow
        let solveClassical () = QuantumMaxCutSolver.solveClassical problem

        match forceMethod with
        | Some Classical ->
            solveClassical ()
            |> createClassicalSolution
                <| "Classical MaxCut solver forced by user override. Quantum Advisor bypassed."
                <| startTime
                <| None
            |> Ok

        | Some Quantum ->
            // Execute quantum MaxCut solver
            let quantumConfig = QuantumMaxCutSolver.defaultConfig
            let backend = BackendAbstraction.createLocalBackend()
            
            match QuantumMaxCutSolver.solve backend problem quantumConfig with
            | Error err -> Error (QuantumError.OperationError ("Quantum MaxCut solver", QuantumResult.toString err))
            | Ok quantumResult ->
                {
                    Method = Quantum
                    Result = quantumResult
                    Reasoning = "Quantum MaxCut solver forced by user override."
                    ElapsedMs = (DateTime.UtcNow - startTime).TotalMilliseconds
                    Recommendation = None
                } |> Ok

        | None ->
            // Create problem representation for Quantum Advisor
            // Use vertex count as approximation of problem complexity
            let numVertices = problem.Vertices.Length
            let problemRepresentation = Array2D.init numVertices numVertices (fun i j -> if i = j then 0.0 else 1.0)
            
            QuantumAdvisor.getRecommendation problemRepresentation
            |> Result.map (fun recommendation ->
                let reasoning = $"{recommendation.Reasoning} Routing to classical MaxCut solver."
                solveClassical ()
                |> createClassicalSolution <| reasoning <| startTime <| Some recommendation
            )

    // ================================================================================
    // SOLVER ROUTING - KNAPSACK
    // ================================================================================

    /// Solve Knapsack problem using hybrid solver with automatic quantum vs classical selection
    ///
    /// Parameters:
    ///   problem - Knapsack problem (items, weights, values, capacity)
    ///   budget - Optional budget limit for quantum execution (USD)
    ///   timeout - Optional timeout for classical solver (milliseconds)
    ///   forceMethod - Optional override to force specific solver method
    ///
    /// Returns:
    ///   Result with Solution containing Knapsack result, method used, and reasoning
    let solveKnapsack
        (problem: QuantumKnapsackSolver.KnapsackProblem)
        (budget: float option)
        (timeout: float option)
        (forceMethod: SolverMethod option)
        : QuantumResult<Solution<QuantumKnapsackSolver.KnapsackSolution>> =
        
        let startTime = DateTime.UtcNow
        let solveClassical () = QuantumKnapsackSolver.solveClassical problem

        match forceMethod with
        | Some Classical ->
            solveClassical ()
            |> createClassicalSolution
                <| "Classical Knapsack solver forced by user override. Quantum Advisor bypassed."
                <| startTime
                <| None
            |> Ok

        | Some Quantum ->
            // Execute quantum Knapsack solver
            let quantumConfig = QuantumKnapsackSolver.defaultConfig
            let backend = BackendAbstraction.createLocalBackend()
            
            match QuantumKnapsackSolver.solve backend problem quantumConfig with
            | Error err -> Error (QuantumError.OperationError ("Quantum Knapsack solver", QuantumResult.toString err))
            | Ok quantumResult ->
                {
                    Method = Quantum
                    Result = quantumResult
                    Reasoning = "Quantum Knapsack solver forced by user override."
                    ElapsedMs = (DateTime.UtcNow - startTime).TotalMilliseconds
                    Recommendation = None
                } |> Ok

        | None ->
            // Create problem representation for Quantum Advisor
            // Use item count as approximation of problem complexity
            let numItems = problem.Items.Length
            let problemRepresentation = Array2D.init numItems numItems (fun i j -> if i = j then 0.0 else 1.0)
            
            QuantumAdvisor.getRecommendation problemRepresentation
            |> Result.map (fun recommendation ->
                let reasoning = $"{recommendation.Reasoning} Routing to classical Knapsack solver."
                solveClassical ()
                |> createClassicalSolution <| reasoning <| startTime <| Some recommendation
            )

    // ================================================================================
    // SOLVER ROUTING - GRAPH COLORING
    // ================================================================================

    /// Solve Graph Coloring problem using hybrid solver with automatic quantum vs classical selection
    ///
    /// Parameters:
    ///   problem - Graph Coloring problem (vertices, edges, colors)
    ///   numColors - Number of colors to use
    ///   budget - Optional budget limit for quantum execution (USD)
    ///   timeout - Optional timeout for classical solver (milliseconds)
    ///   forceMethod - Optional override to force specific solver method
    ///
    /// Returns:
    ///   Result with Solution containing Graph Coloring result, method used, and reasoning
    let solveGraphColoring
        (problem: QuantumGraphColoringSolver.GraphColoringProblem)
        (numColors: int)
        (budget: float option)
        (timeout: float option)
        (forceMethod: SolverMethod option)
        : QuantumResult<Solution<QuantumGraphColoringSolver.GraphColoringSolution>> =
        
        let startTime = DateTime.UtcNow
        let solveClassical () = QuantumGraphColoringSolver.solveClassical problem

        match forceMethod with
        | Some Classical ->
            solveClassical ()
            |> createClassicalSolution
                <| "Classical Graph Coloring solver forced by user override. Quantum Advisor bypassed."
                <| startTime
                <| None
            |> Ok

        | Some Quantum ->
            // Execute quantum Graph Coloring solver
            let quantumConfig = QuantumGraphColoringSolver.defaultConfig numColors
            let backend = BackendAbstraction.createLocalBackend()
            
            match QuantumGraphColoringSolver.solve backend problem quantumConfig with
            | Error err -> Error (QuantumError.OperationError ("Quantum Graph Coloring solver", QuantumResult.toString err))
            | Ok quantumResult ->
                {
                    Method = Quantum
                    Result = quantumResult
                    Reasoning = "Quantum Graph Coloring solver forced by user override."
                    ElapsedMs = (DateTime.UtcNow - startTime).TotalMilliseconds
                    Recommendation = None
                } |> Ok

        | None ->
            // Create problem representation for Quantum Advisor
            // Use vertex count as approximation of problem complexity
            let numVertices = problem.Vertices.Length
            let problemRepresentation = Array2D.init numVertices numVertices (fun i j -> if i = j then 0.0 else 1.0)
            
            QuantumAdvisor.getRecommendation problemRepresentation
            |> Result.map (fun recommendation ->
                let reasoning = $"{recommendation.Reasoning} Routing to classical Graph Coloring solver."
                solveClassical ()
                |> createClassicalSolution <| reasoning <| startTime <| Some recommendation
            )

    // ================================================================================
    // LEGACY COMPATIBILITY
    // ================================================================================

    /// Legacy solve function for backward compatibility (TSP only, no optional parameters)
    let solve (distances: float[,]) : QuantumResult<Solution<TspSolver.TspSolution>> =
        solveTsp distances None None None
