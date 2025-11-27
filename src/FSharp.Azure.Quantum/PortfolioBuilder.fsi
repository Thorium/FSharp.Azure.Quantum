namespace FSharp.Azure.Quantum

open FSharp.Azure.Quantum.Classical

/// High-level Portfolio Optimization Domain Builder
/// 
/// Provides an intuitive API for solving portfolio optimization problems
/// without requiring knowledge of QUBO encoding or quantum circuits.
/// Uses classical greedy-by-ratio algorithm.
module Portfolio =
    
    /// Asset with financial characteristics
    type Asset =
        {
            /// Stock symbol (e.g., "AAPL", "GOOGL")
            Symbol: string
            
            /// Expected annual return as decimal (e.g., 0.12 for 12%)
            ExpectedReturn: float
            
            /// Risk factor (standard deviation) as decimal
            Risk: float
            
            /// Current price per share
            Price: float
        }
    
    /// Portfolio optimization problem
    type PortfolioProblem =
        {
            /// Array of assets to consider for portfolio
            Assets: Asset array
            
            /// Number of assets in the portfolio
            AssetCount: int
            
            /// Total budget available for investment
            Budget: float
            
            /// Optional constraints for portfolio optimization
            Constraints: PortfolioSolver.Constraints option
        }
    
    /// Portfolio allocation solution
    type PortfolioAllocation =
        {
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
    
    /// Create Portfolio problem from list of assets and budget
    /// 
    /// Parameters:
    /// - assets: List of (symbol, expectedReturn, risk, price) tuples
    /// - budget: Total budget available for investment
    /// 
    /// Returns: PortfolioProblem ready for solving
    /// 
    /// Example:
    /// ```fsharp
    /// let problem = Portfolio.createProblem [("AAPL", 0.12, 0.15, 150.0)] 10000.0
    /// ```
    val createProblem: assets: (string * float * float * float) list -> budget: float -> PortfolioProblem
    
    /// Solve Portfolio problem using classical greedy-by-ratio algorithm
    /// 
    /// Parameters:
    /// - problem: Portfolio problem to solve
    /// - config: Optional configuration for solver behavior
    /// 
    /// Returns: Result with PortfolioAllocation or error message
    /// 
    /// Example:
    /// ```fsharp
    /// let allocation = Portfolio.solve problem None
    /// ```
    val solve: problem: PortfolioProblem -> config: PortfolioSolver.PortfolioConfig option -> Result<PortfolioAllocation, string>
    
    /// Convenience function: Create problem and solve in one step
    /// 
    /// Parameters:
    /// - assets: List of (symbol, expectedReturn, risk, price) tuples
    /// - budget: Total budget available for investment
    /// - config: Optional configuration for solver behavior
    /// 
    /// Returns: Result with PortfolioAllocation or error message
    /// 
    /// Example:
    /// ```fsharp
    /// let allocation = Portfolio.solveDirectly [("AAPL", 0.12, 0.15, 150.0)] 10000.0 None
    /// ```
    val solveDirectly: assets: (string * float * float * float) list -> budget: float -> config: PortfolioSolver.PortfolioConfig option -> Result<PortfolioAllocation, string>
