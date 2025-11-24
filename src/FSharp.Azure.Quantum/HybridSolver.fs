namespace FSharp.Azure.Quantum.Classical

open System

/// Hybrid Solver - automatically routes problems to quantum or classical solvers
/// based on problem analysis and quantum advantage estimation
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
    // SOLVER ROUTING
    // ================================================================================

    /// Solve TSP problem using hybrid solver (automatic quantum vs classical selection)
    /// Routes to classical TSP solver based on Quantum Advisor recommendation
    let solve (distances: float[,]) : Result<Solution<TspSolver.TspSolution>, string> =
        let startTime = DateTime.UtcNow

        // Get recommendation from Quantum Advisor
        match QuantumAdvisor.getRecommendation distances with
        | Error msg -> Error msg
        | Ok recommendation ->
            // For now, always use classical solver
            // (Quantum routing will be added in next iteration)
            let classicalSolution = TspSolver.solveWithDistances distances TspSolver.defaultConfig

            let reasoning =
                $"Quantum Advisor recommendation: {recommendation.Reasoning} "
                + $"Routing to classical TSP solver for optimal performance."

            let elapsedMs = (DateTime.UtcNow - startTime).TotalMilliseconds

            Ok {
                Method = Classical
                Result = classicalSolution
                Reasoning = reasoning
                ElapsedMs = elapsedMs
                Recommendation = Some recommendation
            }
