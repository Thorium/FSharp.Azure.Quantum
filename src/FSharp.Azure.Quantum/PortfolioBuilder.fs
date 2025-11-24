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
