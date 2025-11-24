namespace FSharp.Azure.Quantum

open FSharp.Azure.Quantum.Classical

/// High-level Portfolio Domain Builder
/// Provides intuitive API for solving Portfolio optimization problems
/// without requiring knowledge of QUBO encoding or quantum circuits
module Portfolio =

    // ============================================================================
    // TYPES - Domain-specific types for Portfolio problems
    // ============================================================================

    /// Asset with financial characteristics
    type Asset = {
        Symbol: string
        ExpectedReturn: float
        Risk: float
        Price: float
    }

    /// Portfolio optimization problem
    type PortfolioProblem = {
        Assets: Asset array
        AssetCount: int
        Budget: float
        Constraints: PortfolioSolver.Constraints option
    }

    /// Portfolio allocation solution
    type PortfolioAllocation = {
        Allocations: (string * float * float) list  // (symbol, shares, value)
        TotalValue: float
        ExpectedReturn: float
        Risk: float
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

    /// Create Portfolio problem from list of assets and budget
    /// Input: List of (symbol, expectedReturn, risk, price) tuples and budget
    /// Output: PortfolioProblem ready for solving
    /// Example:
    ///   let problem = Portfolio.createProblem [("AAPL", 0.12, 0.15, 150.0)] 10000.0
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

    /// Solve Portfolio problem using classical greedy-by-ratio algorithm
    /// Optional config parameter allows customization of solver behavior
    /// Returns Result with PortfolioAllocation or error message
    /// Example:
    ///   let allocation = Portfolio.solve problem None
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

    /// Convenience function: Create problem and solve in one step
    /// Input: List of (symbol, expectedReturn, risk, price) tuples, budget, and optional config
    /// Output: Result with PortfolioAllocation or error message
    /// Example:
    ///   let allocation = Portfolio.solveDirectly [("AAPL", 0.12, 0.15, 150.0)] 10000.0 None
    let solveDirectly (assets: (string * float * float * float) list) (budget: float) (config: PortfolioSolver.PortfolioConfig option) : Result<PortfolioAllocation, string> =
        let problem = createProblem assets budget
        solve problem config

