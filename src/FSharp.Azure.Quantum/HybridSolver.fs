namespace FSharp.Azure.Quantum.Classical

open System

/// Hybrid Solver - automatically routes problems to quantum or classical solvers
/// based on problem analysis and quantum advantage estimation
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
    // SOLVER ROUTING - TSP
    // ================================================================================

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
        : Result<Solution<TspSolver.TspSolution>, string> =
        
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
            Error "Quantum TSP solver not yet implemented. Use Classical or let advisor decide."

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
        : Result<Solution<PortfolioSolver.PortfolioSolution>, string> =
        
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
            Error "Quantum Portfolio solver not yet implemented. Use Classical or let advisor decide."

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
    // LEGACY COMPATIBILITY
    // ================================================================================

    /// Legacy solve function for backward compatibility (TSP only, no optional parameters)
    let solve (distances: float[,]) : Result<Solution<TspSolver.TspSolution>, string> =
        solveTsp distances None None None
