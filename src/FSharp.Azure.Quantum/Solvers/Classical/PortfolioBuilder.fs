namespace FSharp.Azure.Quantum

open FSharp.Azure.Quantum.Classical

/// High-level Portfolio Domain Builder
/// Provides intuitive API for solving Portfolio optimization problems
/// Routes through HybridSolver for consistent quantum-classical decision making
module Portfolio =

    // ============================================================================
    // TYPES - Domain-specific types for Portfolio problems
    // ============================================================================

    // Using shared Asset type directly from PortfolioTypes module

    /// <summary>
    /// Portfolio optimization problem
    /// </summary>
    type PortfolioProblem = {
        /// Array of assets to consider for portfolio
        Assets: PortfolioTypes.Asset array
        /// Number of assets in the portfolio
        AssetCount: int
        /// Total budget available for investment
        Budget: float
        /// Optional constraints for portfolio optimization
        Constraints: PortfolioSolver.Constraints option
    }

    /// <summary>
    /// Portfolio allocation solution
    /// </summary>
    type PortfolioAllocation = {
        /// List of allocations: (symbol, shares, value)
        Allocations: (string * float * float) list
        /// Total value of the allocated portfolio
        TotalValue: float
        /// Expected return of the portfolio
        ExpectedReturn: float
        /// Overall risk of the portfolio
        Risk: float
        /// Whether the allocation satisfies all constraints
        IsValid: bool
    }

    // ============================================================================
    // HELPER FUNCTIONS
    // ============================================================================

    // No longer need conversion - both use PortfolioTypes.Asset

    /// Validate that portfolio satisfies constraints
    let private isValidPortfolio (totalValue: float) (budget: float) : bool =
        totalValue > 0.0 && totalValue <= budget

    // ============================================================================
    // PUBLIC API
    // ============================================================================

    /// <summary>
    /// Create Portfolio problem from list of assets and budget
    /// </summary>
    /// <param name="assets">List of (symbol, expectedReturn, risk, price) tuples</param>
    /// <param name="budget">Total budget available for investment</param>
    /// <returns>PortfolioProblem ready for solving</returns>
    /// <example>
    /// <code>
    /// let problem = Portfolio.createProblem [("AAPL", 0.12, 0.15, 150.0)] 10000.0
    /// </code>
    /// </example>
    let createProblem (assets: (string * float * float * float) list) (budget: float) : PortfolioProblem =
        let assetArray : PortfolioTypes.Asset array =
            assets
            |> List.map (fun (symbol, expectedReturn, risk, price) -> 
                { 
                    PortfolioTypes.Symbol = symbol
                    PortfolioTypes.ExpectedReturn = expectedReturn
                    PortfolioTypes.Risk = risk
                    PortfolioTypes.Price = price 
                })
            |> List.toArray
        
        {
            Assets = assetArray
            AssetCount = assetArray.Length
            Budget = budget
            Constraints = None
        }

    /// <summary>
    /// Solve Portfolio problem using HybridSolver (automatic quantum-classical routing)
    /// Routes through HybridSolver for intelligent method selection
    /// Optional config parameter is currently ignored (HybridSolver uses defaults)
    /// </summary>
    /// <param name="problem">Portfolio problem to solve</param>
    /// <param name="config">Optional configuration for solver behavior</param>
    /// <returns>Result with PortfolioAllocation or error message</returns>
    /// <example>
    /// <code>
    /// let allocation = Portfolio.solve problem None
    /// </code>
    /// </example>
    let solve (problem: PortfolioProblem) (config: PortfolioSolver.PortfolioConfig option) : Result<PortfolioAllocation, string> =
        // Assets are already the correct type (PortfolioTypes.Asset)
        let solverAssets = problem.Assets |> Array.toList
        
        // Create constraints
        let constraints = 
            problem.Constraints 
            |> Option.defaultValue {
                Budget = problem.Budget
                MinHolding = 0.0
                MaxHolding = problem.Budget  // No per-asset limit by default
            }
        
        // Route through HybridSolver for consistent quantum-classical decision making
        match HybridSolver.solvePortfolio solverAssets constraints None None None with
        | Error msg -> Error $"Portfolio solve failed: {msg}"
        | Ok hybridResult ->
            try
                // Validate solution
                let valid = isValidPortfolio hybridResult.Result.TotalValue problem.Budget
                
                // Convert allocations to simple format
                let allocations =
                    hybridResult.Result.Allocations
                    |> List.map (fun alloc -> (alloc.Asset.Symbol, alloc.Shares, alloc.Value))
                
                Ok {
                    Allocations = allocations
                    TotalValue = hybridResult.Result.TotalValue
                    ExpectedReturn = hybridResult.Result.ExpectedReturn
                    Risk = hybridResult.Result.Risk
                    IsValid = valid
                }
            with
            | ex -> Error $"Portfolio result conversion failed: {ex.Message}"

    /// <summary>
    /// Convenience function: Create problem and solve in one step
    /// </summary>
    /// <param name="assets">List of (symbol, expectedReturn, risk, price) tuples</param>
    /// <param name="budget">Total budget available for investment</param>
    /// <param name="config">Optional configuration for solver behavior</param>
    /// <returns>Result with PortfolioAllocation or error message</returns>
    /// <example>
    /// <code>
    /// let allocation = Portfolio.solveDirectly [("AAPL", 0.12, 0.15, 150.0)] 10000.0 None
    /// </code>
    /// </example>
    let solveDirectly (assets: (string * float * float * float) list) (budget: float) (config: PortfolioSolver.PortfolioConfig option) : Result<PortfolioAllocation, string> =
        let problem = createProblem assets budget
        solve problem config

