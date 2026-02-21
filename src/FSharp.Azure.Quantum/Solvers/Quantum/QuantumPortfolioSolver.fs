namespace FSharp.Azure.Quantum.Quantum

open System
open FSharp.Azure.Quantum
open FSharp.Azure.Quantum.Backends
open FSharp.Azure.Quantum.Classical
open FSharp.Azure.Quantum.Core

/// Quantum Portfolio Solver using QAOA and Backend Abstraction
/// 
/// ALGORITHM-LEVEL API (for advanced users):
/// This module provides direct access to quantum portfolio optimization via QAOA.
/// For business-domain API, use the Portfolio module instead.
/// 
/// COMPARISON:
///   // Business Domain (Recommended for most users):
///   open FSharp.Azure.Quantum
///   let allocation = Portfolio.solve problem None  // Automatic LocalBackend
///   
///   // Algorithm Level (This module - for experts):
///   open FSharp.Azure.Quantum.Quantum
///   let backend = BackendAbstraction.createIonQBackend(...)
///   let result = QuantumPortfolioSolver.solve backend assets constraints config
/// 
/// RULE 1 COMPLIANCE:
/// ✅ Requires IQuantumBackend parameter (explicit quantum execution)
/// 
/// TECHNICAL DETAILS:
/// - Execution: Quantum hardware/simulator via backend
/// - Algorithm: QAOA (Quantum Approximate Optimization Algorithm)
/// - Speed: Seconds to minutes (includes job queue wait for cloud backends)
/// - Cost: ~$10-100 per run on real quantum hardware (IonQ, Rigetti)
/// - LocalBackend: Free simulation (limited to ~16 qubits)
///
/// QUANTUM PIPELINE:
/// 1. Portfolio Problem → QUBO Matrix (mean-variance optimization encoding)
/// 2. QUBO → QAOA Circuit (Hamiltonians + Layers)
/// 3. Execute on Quantum Backend (IonQ/Rigetti/Local)
/// 4. Decode Measurements → Asset Allocations
/// 5. Return Best Solution
///
/// Examples:
///   // Synchronous (blocks until complete):
///   let backend = BackendAbstraction.createLocalBackend()
///   let config = { NumShots = 1000; RiskAversion = 0.5; InitialParameters = (0.5, 0.5) }
///   match QuantumPortfolioSolver.solve backend assets constraints config with
///   | Ok result -> printfn "Expected return: %f" result.ExpectedReturn
///   | Error msg -> printfn "Error: %s" msg
///   
///   // Asynchronous (non-blocking, preferred for cloud backends):
///   async {
///     match! QuantumPortfolioSolver.solveAsync backend assets constraints config with
///     | Ok result -> printfn "Expected return: %f" result.ExpectedReturn
///     | Error msg -> printfn "Error: %s" msg
///   } |> Async.RunSynchronously
module QuantumPortfolioSolver =

    // ================================================================================
    // PROBLEM DEFINITION
    // ================================================================================

    /// Portfolio optimization problem specification
    type PortfolioProblem = {
        /// List of available assets
        Assets: PortfolioTypes.Asset list
        
        /// Portfolio constraints
        Constraints: PortfolioSolver.Constraints
        
        /// Risk aversion parameter (higher = more risk-averse)
        RiskAversion: float
    }
    
    /// Quantum portfolio solution result
    type QuantumPortfolioSolution = {
        /// Asset allocations
        Allocations: PortfolioSolver.Allocation list
        
        /// Total portfolio value
        TotalValue: float
        
        /// Expected portfolio return
        ExpectedReturn: float
        
        /// Portfolio risk (standard deviation)
        Risk: float
        
        /// Sharpe ratio (return / risk)
        SharpeRatio: float
        
        /// Backend used for execution
        BackendName: string
        
        /// Number of measurement shots
        NumShots: int
        
        /// Execution time in milliseconds
        ElapsedMs: float
        
        /// QUBO objective value (energy)
        BestEnergy: float
        
        /// Selected assets (binary: 1 = included, 0 = excluded)
        SelectedAssets: Map<string, bool>
    }

    // ================================================================================
    // QUBO ENCODING FOR PORTFOLIO OPTIMIZATION
    // ================================================================================

    /// Encode portfolio optimization as QUBO
    /// 
    /// Variables: x_i = 1 if asset i is included in portfolio
    /// Objective: Maximize return - risk_aversion * risk^2
    ///   Return: Σ (return_i * x_i)
    ///   Risk: Σ (risk_i^2 * x_i) (simplified - assumes no correlation)
    /// 
    /// Constraints (as penalty terms):
    /// 1. Budget constraint: Σ (price_i * x_i) ≤ budget
    /// 2. Diversification: Encourage selecting multiple assets (optional)
    let toQubo (problem: PortfolioProblem) : Result<GraphOptimization.QuboMatrix, QuantumError> =
        try
            let numAssets = problem.Assets.Length
            
            if numAssets = 0 then
                Error (QuantumError.ValidationError ("numAssets", "Portfolio problem has no assets"))
            else
                // Penalty weight for constraint violations
                let penaltyWeight = 
                    let maxReturn = problem.Assets |> List.map (fun a -> abs a.ExpectedReturn) |> List.max
                    maxReturn * 10.0
                
                // ========================================================================
                // OBJECTIVE: Maximize (Return - RiskAversion * Risk^2)
                // Convert to minimization: Minimize -(Return - RiskAversion * Risk^2)
                // ========================================================================
                
                // Functional accumulation: collect objective terms
                let objectiveTerms =
                    [0 .. numAssets - 1]
                    |> List.collect (fun i ->
                        let asset = problem.Assets.[i]
                        
                        // Linear term: -return_i (negative because we're minimizing)
                        let returnCoeff = -asset.ExpectedReturn
                        let returnTerm = ((i, i), returnCoeff)
                        
                        // Quadratic term: +risk_aversion * risk_i^2 (penalty for risk)
                        let riskCoeff = problem.RiskAversion * asset.Risk * asset.Risk
                        let riskTerm = ((i, i), riskCoeff)
                        
                        [returnTerm; riskTerm]
                    )
                
                // ========================================================================
                // CONSTRAINT 1: Budget Constraint
                // Σ (price_i * x_i) ≤ budget
                // Penalty form: (Σ (price_i * x_i) - budget)^2 if sum > budget
                // 
                // For QUBO: Use penalty if sum exceeds budget
                // Simplified: Penalize pairs of expensive assets being selected together
                // ========================================================================
                
                let totalBudget = problem.Constraints.Budget
                
                // Functional approach: collect all budget constraint terms
                let budgetConstraintTerms =
                    [0 .. numAssets - 1]
                    |> List.collect (fun i ->
                        let asset_i = problem.Assets.[i]
                        
                        // Diagonal penalty for expensive assets
                        let diagonalTerms =
                            if asset_i.Price > totalBudget * 0.5 then
                                let budgetPenalty = penaltyWeight * (asset_i.Price / totalBudget)
                                [((i, i), budgetPenalty)]
                            else
                                []
                        
                        // Off-diagonal: penalize pairs that exceed budget
                        let offDiagonalTerms =
                            [i + 1 .. numAssets - 1]
                            |> List.choose (fun j ->
                                let asset_j = problem.Assets.[j]
                                if asset_i.Price + asset_j.Price > totalBudget then
                                    Some ((i, j), 2.0 * penaltyWeight)
                                else
                                    None
                            )
                        
                        diagonalTerms @ offDiagonalTerms
                    )
                
                // ========================================================================
                // CONSTRAINT 2: Diversification (soft constraint)
                // Encourage selecting multiple assets (not just one)
                // Add small negative bias to encourage more selections
                // ========================================================================
                
                let diversificationTerms =
                    [0 .. numAssets - 1]
                    |> List.map (fun i ->
                        let diversificationBonus = -0.1 * penaltyWeight / float numAssets
                        ((i, i), diversificationBonus)
                    )
                
                // ========================================================================
                // Build QUBO Matrix: Combine all terms functionally
                // ========================================================================
                
                // Functional aggregation: no mutable state
                let allTerms = 
                    objectiveTerms 
                    @ budgetConstraintTerms 
                    @ diversificationTerms
                
                // Aggregate terms with same indices (add coefficients)
                let aggregatedTerms =
                    allTerms
                    |> List.groupBy fst
                    |> List.map (fun (key, terms) ->
                        let totalCoeff = terms |> List.sumBy snd
                        key, totalCoeff)
                    |> Map.ofList
                
                Ok {
                    NumVariables = numAssets
                    Q = aggregatedTerms
                }
        
        with ex ->
            Error (QuantumError.OperationError ("QuboEncoding", sprintf "Failed to encode portfolio as QUBO: %s" ex.Message))

    // ================================================================================
    // TRANSACTION COST QUBO ENCODING
    // ================================================================================
    
    /// Transaction cost parameters for portfolio rebalancing
    /// 
    /// When rebalancing from a current portfolio to a new portfolio,
    /// transaction costs are incurred for buying or selling assets.
    type TransactionCosts = {
        /// Cost rate for buying assets (e.g., 0.001 = 0.1% of transaction value)
        BuyCostRate: float
        
        /// Cost rate for selling assets (e.g., 0.001 = 0.1% of transaction value)
        SellCostRate: float
        
        /// Fixed cost per transaction (e.g., $5 per trade)
        FixedCostPerTrade: float
    }
    
    /// Default transaction costs (typical brokerage fees)
    let defaultTransactionCosts = {
        BuyCostRate = 0.001      // 0.1% commission
        SellCostRate = 0.001    // 0.1% commission
        FixedCostPerTrade = 0.0 // No fixed cost
    }
    
    /// Current portfolio holdings for rebalancing
    type CurrentHoldings = {
        /// Current holdings per asset (symbol -> number of shares)
        Holdings: Map<string, float>
    }
    
    /// Portfolio problem with transaction costs for rebalancing
    type PortfolioProblemWithCosts = {
        /// Base portfolio problem
        BaseProblem: PortfolioProblem
        
        /// Current holdings (empty for new portfolio)
        CurrentHoldings: CurrentHoldings
        
        /// Transaction cost parameters
        TransactionCosts: TransactionCosts
    }
    
    /// Encode portfolio optimization with transaction costs as QUBO
    /// 
    /// Extends the base QUBO formulation to include:
    /// - Transaction cost penalties for changing positions
    /// - Holding cost considerations for existing positions
    /// 
    /// QUBO Formulation:
    ///   Minimize: -Return + RiskAversion * Risk² + TransactionCostPenalty
    /// 
    /// Where TransactionCostPenalty:
    ///   - For new positions (buying): rate * price_i * x_i
    ///   - For closing positions (selling): rate * price_i * (1 - x_i)
    ///   - Fixed costs: fixed_cost * |change|
    /// 
    /// Variables: x_i = 1 if asset i is included in new portfolio
    /// 
    /// Note: This is a simplified linear approximation of transaction costs.
    /// For exact quadratic encoding of |x_new - x_old|, auxiliary variables
    /// would be needed, which significantly increases problem size.
    let toQuboWithTransactionCosts 
        (problemWithCosts: PortfolioProblemWithCosts) 
        : Result<GraphOptimization.QuboMatrix, QuantumError> =
        
        try
            let problem = problemWithCosts.BaseProblem
            let costs = problemWithCosts.TransactionCosts
            let holdings = problemWithCosts.CurrentHoldings.Holdings
            let numAssets = problem.Assets.Length
            
            if numAssets = 0 then
                Error (QuantumError.ValidationError ("numAssets", "Portfolio problem has no assets"))
            elif costs.BuyCostRate < 0.0 || costs.SellCostRate < 0.0 then
                Error (QuantumError.ValidationError ("TransactionCosts", "Cost rates must be non-negative"))
            elif costs.FixedCostPerTrade < 0.0 then
                Error (QuantumError.ValidationError ("TransactionCosts", "Fixed cost must be non-negative"))
            else
                // First get the base QUBO terms
                match toQubo problem with
                | Error err -> Error err
                | Ok baseQubo ->
                    
                    // ================================================================
                    // TRANSACTION COST TERMS
                    // ================================================================
                    // 
                    // For each asset i:
                    // - If currently held (h_i > 0) and not selected (x_i = 0): SELL
                    //   Cost = sell_rate * price_i * h_i
                    //   In QUBO: penalty when x_i = 0, so add positive constant - penalty * x_i
                    //   
                    // - If not held (h_i = 0) and selected (x_i = 1): BUY
                    //   Cost = buy_rate * price_i * value_allocated
                    //   In QUBO: penalty when x_i = 1, so add penalty * x_i
                    //
                    // Simplification: Use average allocation value for buy penalty
                    // ================================================================
                    
                    let avgAllocationValue = problem.Constraints.Budget / float numAssets
                    
                    let transactionCostTerms =
                        [0 .. numAssets - 1]
                        |> List.map (fun i ->
                            let asset = problem.Assets.[i]
                            let currentHolding = 
                                holdings 
                                |> Map.tryFind asset.Symbol 
                                |> Option.defaultValue 0.0
                            
                            let isCurrentlyHeld = currentHolding > 0.0
                            
                            if isCurrentlyHeld then
                                // Currently held: incentivize keeping (penalize selling)
                                // QUBO term: -sellCost * x_i (diagonal term)
                                // When x_i = 1: contribution = -sellCost (bonus for keeping)
                                // When x_i = 0: contribution = 0 (no bonus = effective penalty)
                                // This incentivizes x_i = 1 (keep position) over x_i = 0 (sell)
                                let sellCost = costs.SellCostRate * asset.Price * currentHolding
                                let fixedCost = costs.FixedCostPerTrade
                                ((i, i), -(sellCost + fixedCost))
                            else
                                // Not held: cost to buy if selected
                                // Add penalty when x_i = 1
                                // QUBO term: +buyCost * x_i
                                // When x_i = 1: penalty = buyCost (buying)
                                // When x_i = 0: no penalty (don't buy)
                                let buyCost = costs.BuyCostRate * avgAllocationValue
                                let fixedCost = costs.FixedCostPerTrade
                                ((i, i), buyCost + fixedCost)
                        )
                    
                    // ================================================================
                    // TURNOVER PENALTY (optional quadratic term)
                    // ================================================================
                    // 
                    // NOTE: This is a simplified heuristic, not exact turnover modeling.
                    // 
                    // For pairs where one asset is currently held and one is not,
                    // we add a small penalty when BOTH are selected in the new portfolio.
                    // This encourages some consistency with current holdings.
                    //
                    // Limitation: True turnover penalty would require auxiliary variables
                    // to model |x_new - x_old|, which significantly increases problem size.
                    // This heuristic provides a reasonable approximation for small portfolios.
                    // ================================================================
                    
                    let turnoverPenaltyTerms =
                        if costs.FixedCostPerTrade > 0.0 then
                            [0 .. numAssets - 2]
                            |> List.collect (fun i ->
                                let asset_i = problem.Assets.[i]
                                let held_i = 
                                    holdings 
                                    |> Map.tryFind asset_i.Symbol 
                                    |> Option.map (fun h -> h > 0.0) 
                                    |> Option.defaultValue false
                                
                                [i + 1 .. numAssets - 1]
                                |> List.choose (fun j ->
                                    let asset_j = problem.Assets.[j]
                                    let held_j = 
                                        holdings 
                                        |> Map.tryFind asset_j.Symbol 
                                        |> Option.map (fun h -> h > 0.0) 
                                        |> Option.defaultValue false
                                    
                                    // Penalize selecting both when holdings differ
                                    // This encourages portfolio stability
                                    if held_i <> held_j then
                                        // Small penalty for selecting both (x_i * x_j = 1)
                                        Some ((i, j), 0.5 * costs.FixedCostPerTrade)
                                    else
                                        None
                                )
                            )
                        else
                            []
                    
                    // ================================================================
                    // Combine base QUBO with transaction cost terms
                    // ================================================================
                    
                    let allTerms =
                        (baseQubo.Q |> Map.toList)
                        @ transactionCostTerms
                        @ turnoverPenaltyTerms
                    
                    // Aggregate terms with same indices
                    let aggregatedTerms =
                        allTerms
                        |> List.groupBy fst
                        |> List.map (fun (key, terms) ->
                            let totalCoeff = terms |> List.sumBy snd
                            key, totalCoeff)
                        |> Map.ofList
                    
                    Ok {
                        NumVariables = numAssets
                        Q = aggregatedTerms
                    }
        
        with ex ->
            Error (QuantumError.OperationError ("QuboEncodingWithCosts", sprintf "Failed to encode portfolio with costs as QUBO: %s" ex.Message))
    
    /// Create a portfolio problem with transaction costs
    let createProblemWithCosts 
        (assets: PortfolioTypes.Asset list)
        (constraints: PortfolioSolver.Constraints)
        (riskAversion: float)
        (currentHoldings: Map<string, float>)
        (transactionCosts: TransactionCosts)
        : PortfolioProblemWithCosts =
        {
            BaseProblem = {
                Assets = assets
                Constraints = constraints
                RiskAversion = riskAversion
            }
            CurrentHoldings = { Holdings = currentHoldings }
            TransactionCosts = transactionCosts
        }

    // ================================================================================
    // SOLUTION DECODING
    // ================================================================================

    /// Decode QUBO solution bitstring to portfolio allocation
    let private decodeSolution 
        (problem: PortfolioProblem) 
        (bitstring: int array)
        : QuantumPortfolioSolution option =
        
        try
            // Extract selected assets (where bit = 1)
            let selectedAssets =
                bitstring
                |> Array.mapi (fun i bit -> 
                    let asset = problem.Assets.[i]
                    (asset.Symbol, bit = 1))
                |> Map.ofArray
            
            let selectedAssetList =
                problem.Assets
                |> List.mapi (fun i asset -> i, asset)
                |> List.filter (fun (i, _) -> bitstring.[i] = 1)
                |> List.map snd
            
            if selectedAssetList.IsEmpty then
                None
            else
                // Calculate total value and check budget
                let totalCost = selectedAssetList |> List.sumBy (fun a -> a.Price)
                
                if totalCost > problem.Constraints.Budget then
                    // Violates budget - return None
                    None
                else
                    // Calculate allocations (equal weight for selected assets)
                    let numSelected = selectedAssetList.Length
                    let valuePerAsset = problem.Constraints.Budget / float numSelected
                    
                    let allocations =
                        selectedAssetList
                        |> List.map (fun asset ->
                            let shares = valuePerAsset / asset.Price
                            let actualValue = shares * asset.Price
                            {
                                PortfolioSolver.Allocation.Asset = asset
                                PortfolioSolver.Allocation.Shares = shares
                                PortfolioSolver.Allocation.Value = actualValue
                                PortfolioSolver.Allocation.Percentage = actualValue / problem.Constraints.Budget
                            })
                    
                    let totalValue = allocations |> List.sumBy (fun a -> a.Value)
                    
                    // Calculate portfolio metrics
                    let expectedReturn =
                        allocations
                        |> List.sumBy (fun alloc -> alloc.Asset.ExpectedReturn * alloc.Percentage)
                    
                    let risk =
                        allocations
                        |> List.sumBy (fun alloc -> 
                            let weightedRisk = alloc.Percentage * alloc.Asset.Risk
                            weightedRisk * weightedRisk)
                        |> sqrt
                    
                    let sharpeRatio = 
                        if risk = 0.0 then 0.0
                        else expectedReturn / risk
                    
                    // Energy = -(return - risk_aversion * risk^2)
                    let energy = -(expectedReturn - problem.RiskAversion * risk * risk)
                    
                    Some {
                        Allocations = allocations
                        TotalValue = totalValue
                        ExpectedReturn = expectedReturn
                        Risk = risk
                        SharpeRatio = sharpeRatio
                        BackendName = ""  // Will be set by caller
                        NumShots = 0      // Will be set by caller
                        ElapsedMs = 0.0   // Will be set by caller
                        BestEnergy = energy
                        SelectedAssets = selectedAssets
                    }
        
        with _ ->
            None

    // ================================================================================
    // QUANTUM SOLVER
    // ================================================================================

    /// Configuration for quantum portfolio solving
    type QuantumPortfolioConfig = {
        /// Number of shots for execution
        NumShots: int
        
        /// Risk aversion parameter (0 = risk-neutral, 1 = very risk-averse)
        RiskAversion: float
        
        /// Initial QAOA parameters (gamma, beta)
        InitialParameters: float * float
    }
    
    /// Default configuration
    let defaultConfig = {
        NumShots = 1000
        RiskAversion = 0.5
        InitialParameters = (0.5, 0.5)
    }

    /// Solve portfolio optimization using quantum backend via QAOA (asynchronous)
    /// 
    /// Full Pipeline:
    /// 1. Portfolio problem → QUBO matrix (mean-variance encoding)
    /// 2. QUBO → QaoaCircuit (Hamiltonians + layers)
    /// 3. Execute circuit on quantum backend (async, non-blocking)
    /// 4. Decode measurements → portfolio allocations
    /// 5. Return best solution
    /// 
    /// Parameters:
    ///   backend - Quantum backend to execute on (LocalBackend, IonQ, Rigetti)
    ///   assets - List of assets to optimize
    ///   constraints - Portfolio constraints (budget, min/max holding)
    ///   config - Configuration for execution
    ///   
    /// Returns:
    ///   Async computation that returns Result with QuantumPortfolioSolution or QuantumError
    ///   
    /// Note: This is the preferred method for cloud backends (IonQ, Rigetti) as it allows
    /// non-blocking execution. For synchronous API, use `solve` which wraps this function.
    let solveAsync 
        (backend: BackendAbstraction.IQuantumBackend)
        (assets: PortfolioTypes.Asset list)
        (constraints: PortfolioSolver.Constraints)
        (config: QuantumPortfolioConfig)
        : Async<Result<QuantumPortfolioSolution, QuantumError>> = async {
        
        let startTime = DateTime.UtcNow
        
        // Validate inputs
        let numAssets = assets.Length
        let requiredQubits = numAssets
        
        if numAssets = 0 then
            return Error (QuantumError.ValidationError ("numAssets", "Portfolio problem has no assets"))
        // Note: Backend validation removed (MaxQubits/Name properties no longer in interface)
        // Backends will return errors if qubit count exceeded
        elif config.NumShots <= 0 then
            return Error (QuantumError.ValidationError ("numShots", "Number of shots must be positive"))
        else
            try
                // Build portfolio problem
                let problem : PortfolioProblem = {
                    Assets = assets
                    Constraints = constraints
                    RiskAversion = config.RiskAversion
                }
                
                // Step 1: Encode portfolio as QUBO
                match toQubo problem with
                | Error msg -> return Error msg
                | Ok quboMatrix ->
                    
                    // Step 2: Execute QAOA pipeline from dense QUBO
                    let quboArray = Qubo.toDenseArray quboMatrix.NumVariables quboMatrix.Q
                    let (gamma, beta) = config.InitialParameters
                    let parameters = [| gamma, beta |]
                    
                    match QaoaExecutionHelpers.executeFromQubo backend quboArray parameters config.NumShots with
                    | Error err -> return Error err
                    | Ok measurements ->
                        
                        // Step 5: Decode measurements to portfolio solutions
                        let portfolioResults =
                            measurements
                            |> Array.choose (decodeSolution problem)
                        
                        if portfolioResults.Length = 0 then
                            return Error (QuantumError.OperationError ("DecodeSolution", "No valid portfolio solutions found in quantum measurements"))
                        else
                            // Select best solution (minimum energy = maximum utility)
                            let bestSolution = 
                                portfolioResults
                                |> Array.minBy (fun sol -> sol.BestEnergy)
                            
                            let elapsedMs = (DateTime.UtcNow - startTime).TotalMilliseconds
                            
                            return Ok {
                                bestSolution with
                                    BackendName = backend.Name
                                    NumShots = config.NumShots
                                    ElapsedMs = elapsedMs
                            }
            
            with ex ->
                return Error (QuantumError.OperationError ("QuantumPortfolioSolver", sprintf "Quantum portfolio solver failed: %s" ex.Message))
    }

    /// Solve portfolio optimization using quantum backend via QAOA (synchronous)
    /// 
    /// This is a synchronous wrapper around `solveAsync` for backward compatibility.
    /// For better performance with cloud backends, prefer using `solveAsync` directly.
    /// 
    /// Parameters:
    ///   backend - Quantum backend to execute on (LocalBackend, IonQ, Rigetti)
    ///   assets - List of assets to optimize
    ///   constraints - Portfolio constraints (budget, min/max holding)
    ///   config - Configuration for execution
    ///   
    /// Returns:
    ///   Result with QuantumPortfolioSolution or QuantumError
    let solve 
        (backend: BackendAbstraction.IQuantumBackend)
        (assets: PortfolioTypes.Asset list)
        (constraints: PortfolioSolver.Constraints)
        (config: QuantumPortfolioConfig)
        : Result<QuantumPortfolioSolution, QuantumError> =
        solveAsync backend assets constraints config
        |> Async.RunSynchronously

    /// Solve portfolio with default configuration
    let solveWithDefaults 
        (backend: BackendAbstraction.IQuantumBackend)
        (assets: PortfolioTypes.Asset list)
        (constraints: PortfolioSolver.Constraints)
        : Result<QuantumPortfolioSolution, QuantumError> =
        solve backend assets constraints defaultConfig
    
    /// Solve portfolio with custom number of shots and risk aversion
    let solveWithParams 
        (backend: BackendAbstraction.IQuantumBackend)
        (assets: PortfolioTypes.Asset list)
        (constraints: PortfolioSolver.Constraints)
        (numShots: int)
        (riskAversion: float)
        : Result<QuantumPortfolioSolution, QuantumError> =
        let config = { defaultConfig with NumShots = numShots; RiskAversion = riskAversion }
        solve backend assets constraints config
