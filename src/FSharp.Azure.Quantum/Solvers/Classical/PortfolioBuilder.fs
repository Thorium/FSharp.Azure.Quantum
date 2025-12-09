namespace FSharp.Azure.Quantum

open FSharp.Azure.Quantum.Backends
open FSharp.Azure.Quantum.Classical
open FSharp.Azure.Quantum.Backends
open FSharp.Azure.Quantum.Quantum
open FSharp.Azure.Quantum.Backends
open FSharp.Azure.Quantum.Core

/// High-level Portfolio Domain Builder - Quantum-First API
/// 
/// DESIGN PHILOSOPHY:
/// This is a BUSINESS DOMAIN API for investment managers who want to optimize
/// portfolios without understanding quantum computing internals (QAOA, QUBO, backends).
/// 
/// QUANTUM-FIRST:
/// - Uses quantum optimization (QAOA) by default via LocalBackend (simulation)
/// - Optional backend parameter for cloud quantum hardware (IonQ, Rigetti)
/// - For algorithm-level control, use QuantumPortfolioSolver directly
/// 
/// EXAMPLE USAGE:
///   // Simple: Uses quantum simulation automatically
///   let allocation = Portfolio.solve problem None
///   
///   // Advanced: Specify cloud quantum backend
///   let ionqBackend = BackendAbstraction.createIonQBackend(...)
///   let allocation = Portfolio.solve problem (Some ionqBackend)
///   
///   // Expert: Direct quantum solver access
///   open FSharp.Azure.Quantum.Quantum
///   let result = QuantumPortfolioSolver.solve backend assets constraints config
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
    /// Solve Portfolio problem using quantum optimization (QAOA)
    /// </summary>
    /// <remarks>
    /// QUANTUM-FIRST API:
    /// - Uses quantum backend by default (LocalBackend for simulation)
    /// - Specify custom backend for cloud quantum hardware (IonQ, Rigetti)
    /// - Returns business-domain PortfolioAllocation result (not low-level QAOA output)
    /// 
    /// EXAMPLES:
    ///   // Simple: Automatic quantum simulation
    ///   let allocation = Portfolio.solve problem None
    ///   
    ///   // Cloud execution: Specify IonQ backend
    ///   let ionqBackend = BackendAbstraction.createIonQBackend(...)
    ///   let allocation = Portfolio.solve problem (Some ionqBackend)
    /// </remarks>
    /// <param name="problem">Portfolio problem to solve</param>
    /// <param name="backend">Optional quantum backend (defaults to LocalBackend if None)</param>
    /// <returns>Result with PortfolioAllocation or error message</returns>
    let solve (problem: PortfolioProblem) (backend: BackendAbstraction.IQuantumBackend option) : QuantumResult<PortfolioAllocation> =
        try
            // Use provided backend or create LocalBackend for simulation
            let actualBackend = 
                backend 
                |> Option.defaultValue (LocalBackend.LocalBackend() :> BackendAbstraction.IQuantumBackend)
            
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
            
            // Create quantum portfolio solver configuration
            let quantumConfig : QuantumPortfolioSolver.QuantumPortfolioConfig = {
                NumShots = 1000
                RiskAversion = 0.5
                InitialParameters = (0.5, 0.5)
            }
            
            // Call quantum portfolio solver directly using computation expression
            quantumResult {
                let! quantumResult = QuantumPortfolioSolver.solve actualBackend solverAssets constraints quantumConfig
                
                // Validate solution
                let valid = isValidPortfolio quantumResult.TotalValue problem.Budget
                
                // Convert allocations to simple format
                let allocations =
                    quantumResult.Allocations
                    |> List.map (fun alloc -> (alloc.Asset.Symbol, alloc.Shares, alloc.Value))
                
                return {
                    Allocations = allocations
                    TotalValue = quantumResult.TotalValue
                    ExpectedReturn = quantumResult.ExpectedReturn
                    Risk = quantumResult.Risk
                    IsValid = valid
                }
            }
        with
        | ex -> Error (QuantumError.OperationError ("Portfolio solve failed: ", $"Failed: {ex.Message}"))

    /// <summary>
    /// Convenience function: Create problem and solve in one step using quantum optimization
    /// </summary>
    /// <param name="assets">List of (symbol, expectedReturn, risk, price) tuples</param>
    /// <param name="budget">Total budget available for investment</param>
    /// <param name="backend">Optional quantum backend (defaults to LocalBackend if None)</param>
    /// <returns>Result with PortfolioAllocation or error message</returns>
    /// <example>
    /// <code>
    /// let allocation = Portfolio.solveDirectly [("AAPL", 0.12, 0.15, 150.0)] 10000.0 None
    /// </code>
    /// </example>
    let solveDirectly (assets: (string * float * float * float) list) (budget: float) (backend: BackendAbstraction.IQuantumBackend option) : QuantumResult<PortfolioAllocation> =
        let problem = createProblem assets budget
        solve problem backend

