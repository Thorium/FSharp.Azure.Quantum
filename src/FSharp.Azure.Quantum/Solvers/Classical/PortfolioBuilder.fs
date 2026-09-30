namespace FSharp.Azure.Quantum

open System
open System.Threading
open System.Threading.Tasks
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
///   let! allocation = Portfolio.solveAsync problem None cancellationToken
///
///   // Advanced: Specify cloud quantum backend
///   let ionqBackend = BackendAbstraction.createIonQBackend(...)
///   let! allocation = Portfolio.solveAsync problem (Some ionqBackend) cancellationToken
///
///   // Expert: Direct quantum solver access
///   open FSharp.Azure.Quantum.Quantum
///   let! result = QuantumPortfolioSolver.solveAsync backend assets constraints config cancellationToken
module Portfolio =

    // ============================================================================
    // TYPES - Domain-specific types for Portfolio problems
    // ============================================================================

    // Using shared Asset type directly from PortfolioTypes module

    /// <summary>
    /// Portfolio optimization problem
    /// </summary>
    type PortfolioProblem =
        {
            /// Array of assets to consider for portfolio
            Assets: PortfolioTypes.Asset array
            /// Number of assets in the portfolio
            AssetCount: int
            /// Total budget available for investment
            Budget: float
            /// Optional constraints for portfolio optimization
            Constraints: PortfolioSolver.Constraints option
            /// Optional covariance matrix of the asset returns, rows and columns in Assets order.
            /// None treats the assets as independent: Risk = sqrt(Σ (wᵢσᵢ)²).
            Covariance: float[,] option
        }

    /// <summary>
    /// Portfolio allocation solution
    /// </summary>
    type PortfolioAllocation =
        {
            /// List of allocations: (symbol, shares, value)
            Allocations: (string * float * float) list
            /// Total value of the allocated portfolio
            TotalValue: float
            /// Expected return of the portfolio
            ExpectedReturn: float
            /// Overall risk of the portfolio: sqrt(wᵀΣw) with a covariance matrix,
            /// otherwise sqrt(Σ (wᵢσᵢ)²) (independent assets)
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
        let assetArray: PortfolioTypes.Asset array =
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
            Covariance = None
        }

    /// <summary>
    /// Create Portfolio problem with a covariance matrix of the asset returns
    /// </summary>
    /// <param name="assets">List of (symbol, expectedReturn, risk, price) tuples</param>
    /// <param name="budget">Total budget available for investment</param>
    /// <param name="covariance">Covariance matrix Σ, rows and columns in asset order (validated by solve)</param>
    /// <returns>PortfolioProblem whose risk is sqrt(wᵀΣw)</returns>
    /// <example>
    /// <code>
    /// let problem = Portfolio.createProblemWithCovariance assets 10000.0 covariance
    /// </code>
    /// </example>
    let createProblemWithCovariance
        (assets: (string * float * float * float) list)
        (budget: float)
        (covariance: float[,])
        : PortfolioProblem =
        { createProblem assets budget with
            Covariance = Some covariance
        }

    /// <summary>
    /// Create Portfolio problem with a correlation matrix: Σ[i,j] = ρ[i,j] × riskᵢ × riskⱼ,
    /// using each asset's risk as its volatility.
    /// </summary>
    /// <param name="assets">List of (symbol, expectedReturn, risk, price) tuples</param>
    /// <param name="budget">Total budget available for investment</param>
    /// <param name="correlation">Correlation matrix ρ, rows and columns in asset order</param>
    /// <returns>PortfolioProblem, or a ValidationError when the correlation shape does not match the assets</returns>
    let createProblemWithCorrelation
        (assets: (string * float * float * float) list)
        (budget: float)
        (correlation: float[,])
        : QuantumResult<PortfolioProblem> =
        let problem = createProblem assets budget
        let volatilities = problem.Assets |> Array.map (fun a -> a.Risk)

        PortfolioTypes.covarianceFromCorrelation volatilities correlation
        |> Result.map (fun covariance ->
            { problem with
                Covariance = Some covariance
            })

    /// <summary>
    /// Solve Portfolio problem using quantum optimization (QAOA), asynchronously
    /// </summary>
    /// <remarks>
    /// QUANTUM-FIRST API:
    /// - Uses quantum backend by default (LocalBackend for simulation)
    /// - Specify custom backend for cloud quantum hardware (IonQ, Rigetti)
    /// - Returns business-domain PortfolioAllocation result (not low-level QAOA output)
    /// - Does not block: the backend call is awaited, so cloud jobs do not tie up a thread
    ///
    /// EXAMPLES:
    ///   // Simple: Automatic quantum simulation
    ///   let! allocation = Portfolio.solveAsync problem None CancellationToken.None
    ///
    ///   // Cloud execution: Specify IonQ backend
    ///   let ionqBackend = BackendAbstraction.createIonQBackend(...)
    ///   let! allocation = Portfolio.solveAsync problem (Some ionqBackend) cancellationToken
    /// </remarks>
    /// With problem.Covariance the QUBO includes the covariance terms and Risk is sqrt(wᵀΣw);
    /// an invalid covariance gives a ValidationError.
    /// <param name="problem">Portfolio problem to solve</param>
    /// <param name="backend">Optional quantum backend (defaults to LocalBackend if None)</param>
    /// <param name="cancellationToken">Cancels the backend execution</param>
    /// <returns>Task of Result with PortfolioAllocation or error message</returns>
    let solveAsync
        (problem: PortfolioProblem)
        (backend: BackendAbstraction.IQuantumBackend option)
        (cancellationToken: CancellationToken)
        : Task<QuantumResult<PortfolioAllocation>> =
        quantumResultTask {
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
                    |> Option.defaultValue
                        {
                            Budget = problem.Budget
                            MinHolding = 0.0
                            MaxHolding = problem.Budget // No per-asset limit by default
                        }

                // Create quantum portfolio solver configuration
                let quantumConfig: QuantumPortfolioSolver.QuantumPortfolioConfig =
                    {
                        NumShots = 1000
                        RiskAversion = 0.5
                        InitialParameters = (0.5, 0.5)
                    }

                // Call quantum portfolio solver directly
                let! quantumResult =
                    match problem.Covariance with
                    | Some covariance ->
                        QuantumPortfolioSolver.solveWithCovarianceAsync
                            actualBackend
                            solverAssets
                            covariance
                            constraints
                            quantumConfig
                            cancellationToken
                    | None ->
                        QuantumPortfolioSolver.solveAsync
                            actualBackend
                            solverAssets
                            constraints
                            quantumConfig
                            cancellationToken

                // Validate solution
                let valid = isValidPortfolio quantumResult.TotalValue problem.Budget

                // Convert allocations to simple format
                let allocations =
                    quantumResult.Allocations
                    |> List.map (fun alloc -> (alloc.Asset.Symbol, alloc.Shares, alloc.Value))

                return
                    {
                        Allocations = allocations
                        TotalValue = quantumResult.TotalValue
                        ExpectedReturn = quantumResult.ExpectedReturn
                        Risk = quantumResult.Risk
                        IsValid = valid
                    }
            with ex ->
                return! Error(QuantumError.OperationError("Portfolio solve failed: ", $"Failed: {ex.Message}"))
        }

    /// <summary>
    /// Solve Portfolio problem using quantum optimization (QAOA)
    /// </summary>
    /// <remarks>
    /// This is a synchronous wrapper around <c>solveAsync</c> for backward compatibility:
    /// it blocks the calling thread until the backend has answered.
    /// </remarks>
    /// <param name="problem">Portfolio problem to solve</param>
    /// <param name="backend">Optional quantum backend (defaults to LocalBackend if None)</param>
    /// <returns>Result with PortfolioAllocation or error message</returns>
    [<Obsolete("Use solveAsync for non-blocking execution against cloud backends")>]
    let solve
        (problem: PortfolioProblem)
        (backend: BackendAbstraction.IQuantumBackend option)
        : QuantumResult<PortfolioAllocation> =
        solveAsync problem backend CancellationToken.None
        |> Async.AwaitTask
        |> Async.RunSynchronously

    /// <summary>
    /// Convenience function: Create problem and solve in one step using quantum optimization, asynchronously
    /// </summary>
    /// <param name="assets">List of (symbol, expectedReturn, risk, price) tuples</param>
    /// <param name="budget">Total budget available for investment</param>
    /// <param name="backend">Optional quantum backend (defaults to LocalBackend if None)</param>
    /// <param name="cancellationToken">Cancels the backend execution</param>
    /// <returns>Task of Result with PortfolioAllocation or error message</returns>
    /// <example>
    /// <code>
    /// let! allocation = Portfolio.solveDirectlyAsync [("AAPL", 0.12, 0.15, 150.0)] 10000.0 None CancellationToken.None
    /// </code>
    /// </example>
    let solveDirectlyAsync
        (assets: (string * float * float * float) list)
        (budget: float)
        (backend: BackendAbstraction.IQuantumBackend option)
        (cancellationToken: CancellationToken)
        : Task<QuantumResult<PortfolioAllocation>> =
        let problem = createProblem assets budget
        solveAsync problem backend cancellationToken

    /// <summary>
    /// Convenience function: Create problem and solve in one step using quantum optimization
    /// </summary>
    /// <remarks>
    /// This is a synchronous wrapper around <c>solveDirectlyAsync</c> for backward compatibility.
    /// </remarks>
    /// <param name="assets">List of (symbol, expectedReturn, risk, price) tuples</param>
    /// <param name="budget">Total budget available for investment</param>
    /// <param name="backend">Optional quantum backend (defaults to LocalBackend if None)</param>
    /// <returns>Result with PortfolioAllocation or error message</returns>
    [<Obsolete("Use solveDirectlyAsync for non-blocking execution against cloud backends")>]
    let solveDirectly
        (assets: (string * float * float * float) list)
        (budget: float)
        (backend: BackendAbstraction.IQuantumBackend option)
        : QuantumResult<PortfolioAllocation> =
        solveDirectlyAsync assets budget backend CancellationToken.None
        |> Async.AwaitTask
        |> Async.RunSynchronously
