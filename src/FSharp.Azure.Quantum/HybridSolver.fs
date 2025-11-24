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

        // Check if method is forced
        match forceMethod with
        | Some Classical ->
            // User forced classical method
            let config = TspSolver.defaultConfig
            let classicalSolution = TspSolver.solveWithDistances distances config
            let elapsedMs = (DateTime.UtcNow - startTime).TotalMilliseconds

            Ok {
                Method = Classical
                Result = classicalSolution
                Reasoning = "Classical solver forced by user override. Quantum Advisor bypassed."
                ElapsedMs = elapsedMs
                Recommendation = None
            }

        | Some Quantum ->
            // User forced quantum method (not yet implemented)
            Error "Quantum TSP solver not yet implemented. Use Classical or let advisor decide."

        | None ->
            // Get recommendation from Quantum Advisor
            match QuantumAdvisor.getRecommendation distances with
            | Error msg -> Error msg
            | Ok recommendation ->
                // Route based on recommendation
                match recommendation.RecommendationType with
                | QuantumAdvisor.RecommendationType.StronglyRecommendClassical
                | QuantumAdvisor.RecommendationType.ConsiderQuantum ->
                    // Use classical solver
                    let config = TspSolver.defaultConfig
                    let classicalSolution = TspSolver.solveWithDistances distances config
                    let elapsedMs = (DateTime.UtcNow - startTime).TotalMilliseconds

                    Ok {
                        Method = Classical
                        Result = classicalSolution
                        Reasoning = $"{recommendation.Reasoning} Routing to classical TSP solver."
                        ElapsedMs = elapsedMs
                        Recommendation = Some recommendation
                    }

                | QuantumAdvisor.RecommendationType.StronglyRecommendQuantum ->
                    // Quantum recommended but not yet implemented - fallback to classical
                    let config = TspSolver.defaultConfig
                    let classicalSolution = TspSolver.solveWithDistances distances config
                    let elapsedMs = (DateTime.UtcNow - startTime).TotalMilliseconds

                    Ok {
                        Method = Classical
                        Result = classicalSolution
                        Reasoning = $"{recommendation.Reasoning} Quantum solver not yet available - using classical fallback."
                        ElapsedMs = elapsedMs
                        Recommendation = Some recommendation
                    }

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

        // Check if method is forced
        match forceMethod with
        | Some Classical ->
            // User forced classical method
            let config = PortfolioSolver.defaultConfig
            let classicalSolution = PortfolioSolver.solveGreedyByRatio assets constraints config
            let elapsedMs = (DateTime.UtcNow - startTime).TotalMilliseconds

            Ok {
                Method = Classical
                Result = classicalSolution
                Reasoning = "Classical solver forced by user override. Quantum Advisor bypassed."
                ElapsedMs = elapsedMs
                Recommendation = None
            }

        | Some Quantum ->
            // User forced quantum method (not yet implemented)
            Error "Quantum Portfolio solver not yet implemented. Use Classical or let advisor decide."

        | None ->
            // Get recommendation from Quantum Advisor
            // For Portfolio problems, analyze based on problem size (number of assets)
            let numAssets = List.length assets
            
            // Create a simple representation for recommendation (use asset count as "matrix" size)
            // This is a simplification - in reality we'd analyze the optimization complexity
            let problemRepresentation = Array2D.init numAssets numAssets (fun i j -> if i = j then 0.0 else 1.0)
            
            match QuantumAdvisor.getRecommendation problemRepresentation with
            | Error msg -> Error msg
            | Ok recommendation ->
                // Route based on recommendation (always use classical for now)
                let config = PortfolioSolver.defaultConfig
                let classicalSolution = PortfolioSolver.solveGreedyByRatio assets constraints config
                let elapsedMs = (DateTime.UtcNow - startTime).TotalMilliseconds

                Ok {
                    Method = Classical
                    Result = classicalSolution
                    Reasoning = $"{recommendation.Reasoning} Routing to classical Portfolio solver."
                    ElapsedMs = elapsedMs
                    Recommendation = Some recommendation
                }

    // ================================================================================
    // LEGACY COMPATIBILITY
    // ================================================================================

    /// Legacy solve function for backward compatibility (TSP only, no optional parameters)
    let solve (distances: float[,]) : Result<Solution<TspSolver.TspSolution>, string> =
        solveTsp distances None None None
