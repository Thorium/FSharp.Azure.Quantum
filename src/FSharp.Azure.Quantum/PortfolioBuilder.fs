namespace FSharp.Azure.Quantum

open FSharp.Azure.Quantum.Classical

/// High-level Portfolio Domain Builder
/// Provides intuitive API for solving Portfolio optimization problems
/// without requiring knowledge of QUBO encoding or quantum circuits
module Portfolio =

    // ============================================================================
    // TYPES - Domain-specific types for Portfolio problems
    // ============================================================================

    /// <summary>
    /// Asset with financial characteristics
    /// </summary>
    type Asset = {
        /// Stock symbol (e.g., "AAPL", "GOOGL")
        Symbol: string
        /// Expected annual return as decimal (e.g., 0.12 for 12%)
        ExpectedReturn: float
        /// Risk factor (standard deviation) as decimal
        Risk: float
        /// Current price per share
        Price: float
    }

    /// <summary>
    /// Portfolio optimization problem
    /// </summary>
    type PortfolioProblem = {
        /// Array of assets to consider for portfolio
        Assets: Asset array
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

    /// Convert Portfolio.Asset to PortfolioSolver.Asset
    let private toSolverAsset (asset: Asset) : PortfolioSolver.Asset =
        {
            Symbol = asset.Symbol
            ExpectedReturn = asset.ExpectedReturn
            Risk = asset.Risk
            Price = asset.Price
        }

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
        let assetArray =
            assets
            |> List.map (fun (symbol, expectedReturn, risk, price) -> 
                { Symbol = symbol; ExpectedReturn = expectedReturn; Risk = risk; Price = price })
            |> List.toArray
        
        {
            Assets = assetArray
            AssetCount = assetArray.Length
            Budget = budget
            Constraints = None
        }

    /// <summary>
    /// Solve Portfolio problem using classical greedy-by-ratio algorithm
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
        try
            // Convert to solver types
            let solverAssets = problem.Assets |> Array.map toSolverAsset |> Array.toList
            
            // Use provided config or default
            let solverConfig = config |> Option.defaultValue PortfolioSolver.defaultConfig
            
            // Create constraints
            let constraints = 
                problem.Constraints 
                |> Option.defaultValue {
                    Budget = problem.Budget
                    MinHolding = 0.0
                    MaxHolding = problem.Budget  // No per-asset limit by default
                }
            
            // Solve using greedy-by-ratio algorithm
            let solution = PortfolioSolver.solveGreedyByRatio solverAssets constraints solverConfig
            
            // Validate solution
            let valid = isValidPortfolio solution.TotalValue problem.Budget
            
            // Convert allocations to simple format
            let allocations =
                solution.Allocations
                |> List.map (fun alloc -> (alloc.Asset.Symbol, alloc.Shares, alloc.Value))
            
            Ok {
                Allocations = allocations
                TotalValue = solution.TotalValue
                ExpectedReturn = solution.ExpectedReturn
                Risk = solution.Risk
                IsValid = valid
            }
        with
        | ex -> Error $"Portfolio solve failed: {ex.Message}"

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

