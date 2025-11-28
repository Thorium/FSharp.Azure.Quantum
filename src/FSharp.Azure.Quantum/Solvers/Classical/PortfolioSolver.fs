namespace FSharp.Azure.Quantum.Classical

open System
open FSharp.Azure.Quantum

/// Classical Portfolio Solver - CPU-based algorithms (Greedy-by-Ratio)
///
/// IMPORTANT: This solver uses CLASSICAL algorithms and does NOT accept a quantum backend.
/// - Execution: CPU-only (no quantum hardware)
/// - Algorithms: Greedy return/risk ratio optimization
/// - Speed: Fast (milliseconds for <100 assets)
/// - Cost: Free (local computation)
///
/// For QUANTUM portfolio optimization (when available), use QuantumPortfolioSolver.
///
/// Example:
///   let result = PortfolioSolver.solveGreedyByRatio assets constraints PortfolioSolver.defaultConfig
module PortfolioSolver =

    // ================================================================================
    // CORE TYPES
    // ================================================================================
    
    // Asset type is now in shared PortfolioTypes module
    type Asset = PortfolioTypes.Asset
    
    /// Portfolio constraints
    type Constraints = {
        /// Total budget available for investment
        Budget: float
        
        /// Minimum holding value per asset (0 for no minimum)
        MinHolding: float
        
        /// Maximum holding value per asset
        MaxHolding: float
    }
    
    /// Portfolio solver configuration
    type PortfolioConfig = {
        /// Maximum iterations for optimization algorithms
        MaxIterations: int
        
        /// Risk tolerance factor (0 = risk-averse, 1 = risk-neutral)
        RiskTolerance: float
    }
    
    /// Create default portfolio configuration
    let defaultConfig = {
        MaxIterations = 1000
        RiskTolerance = 0.5
    }
    
    /// Asset allocation in portfolio
    type Allocation = {
        /// Asset being allocated
        Asset: Asset
        
        /// Number of shares
        Shares: float
        
        /// Total value invested
        Value: float
        
        /// Percentage of portfolio
        Percentage: float
    }
    
    /// Portfolio solution result
    type PortfolioSolution = {
        /// Asset allocations
        Allocations: Allocation list
        
        /// Total portfolio value
        TotalValue: float
        
        /// Expected portfolio return
        ExpectedReturn: float
        
        /// Portfolio risk (standard deviation)
        Risk: float
        
        /// Sharpe ratio (return / risk)
        SharpeRatio: float
        
        /// Time taken to solve (milliseconds)
        ElapsedMs: float
    }
    
    // Validation result is now in shared Validation module
    
    // ================================================================================
    // VALIDATION FUNCTIONS
    // ================================================================================
    
    /// Validate asset data
    let private validateAsset (asset: Asset) : string list =
        [
            if String.IsNullOrWhiteSpace(asset.Symbol) then
                yield "Asset symbol cannot be empty"
            if asset.Price <= 0.0 then
                yield $"Asset {asset.Symbol} has invalid price: {asset.Price}"
            if asset.Risk < 0.0 then
                yield $"Asset {asset.Symbol} has negative risk: {asset.Risk}"
        ]
    
    /// Validate constraints
    let private validateConstraintsInternal (constraints: Constraints) : string list =
        [
            if constraints.Budget <= 0.0 then
                yield $"Budget must be positive: {constraints.Budget}"
            if constraints.MinHolding < 0.0 then
                yield $"MinHolding cannot be negative: {constraints.MinHolding}"
            if constraints.MaxHolding <= 0.0 then
                yield $"MaxHolding must be positive: {constraints.MaxHolding}"
            if constraints.MinHolding > constraints.MaxHolding then
                yield $"MinHolding ({constraints.MinHolding}) cannot exceed MaxHolding ({constraints.MaxHolding})"
            if constraints.MaxHolding > constraints.Budget then
                yield $"MaxHolding ({constraints.MaxHolding}) cannot exceed Budget ({constraints.Budget})"
        ]
    
    /// Validates that budget constraint is reasonable
    let validateBudgetConstraint (assets: Asset list) (constraints: Constraints) : Validation.ValidationResult =
        let messages = 
            [
                yield! validateConstraintsInternal constraints
                
                // Check if budget is sufficient for at least one asset
                let minPrice = assets |> List.map (fun a -> a.Price) |> List.min
                if constraints.Budget < minPrice then
                    yield $"Budget ({constraints.Budget}) is insufficient to purchase any asset (minimum price: {minPrice})"
                
                // Check if constraints allow valid allocations
                if constraints.MinHolding > 0.0 then
                    let minPurchase = assets |> List.map (fun a -> a.Price) |> List.min
                    if constraints.MinHolding < minPurchase then
                        yield $"MinHolding ({constraints.MinHolding}) is less than minimum asset price ({minPurchase})"
            ]
        
        if List.isEmpty messages then
            Validation.success
        else
            Validation.failure messages
    
    // ================================================================================
    // HELPER FUNCTIONS
    // ================================================================================
    
    /// Calculate return-to-risk ratio (Sharpe-like ratio without risk-free rate)
    let private calculateRatio (asset: Asset) : float =
        if asset.Risk = 0.0 then
            Double.MaxValue  // Zero risk with positive return = infinite ratio
        else
            asset.ExpectedReturn / asset.Risk
    
    /// Calculate portfolio metrics from allocations
    let private calculatePortfolioMetrics (allocations: Allocation list) (totalValue: float) : float * float * float =
        if List.isEmpty allocations || totalValue = 0.0 then
            (0.0, 0.0, 0.0)  // (expectedReturn, risk, sharpeRatio)
        else
            // Weighted average expected return
            let expectedReturn =
                allocations
                |> List.sumBy (fun alloc -> alloc.Asset.ExpectedReturn * alloc.Percentage)
            
            // Simplified risk calculation (assumes no correlation between assets)
            // Risk = sqrt(sum of (weight_i * risk_i)^2)
            let risk =
                allocations
                |> List.sumBy (fun alloc -> 
                    let weightedRisk = alloc.Percentage * alloc.Asset.Risk
                    weightedRisk * weightedRisk)
                |> sqrt
            
            // Sharpe ratio (simplified without risk-free rate)
            let sharpeRatio = 
                if risk = 0.0 then 0.0
                else expectedReturn / risk
            
            (expectedReturn, risk, sharpeRatio)
    
    // ================================================================================
    // GREEDY-BY-RATIO ALGORITHM
    // ================================================================================
    
    /// Solve portfolio optimization using greedy-by-ratio algorithm
    /// Allocates budget to assets with highest return/risk ratio first
    let solveGreedyByRatio (assets: Asset list) (constraints: Constraints) (config: PortfolioConfig) : PortfolioSolution =
        let startTime = DateTime.UtcNow
        
        // Sort assets by return/risk ratio (descending)
        let sortedAssets = 
            assets
            |> List.map (fun asset -> (asset, calculateRatio asset))
            |> List.sortByDescending snd
            |> List.map fst
        
        // Greedy allocation
        let rec allocateGreedy (remainingAssets: Asset list) (remainingBudget: float) (allocations: Allocation list) =
            match remainingAssets with
            | [] -> allocations
            | asset :: rest ->
                if remainingBudget <= 0.0 then
                    allocations
                else
                    // Calculate how much we can allocate to this asset
                    let maxValueByBudget = remainingBudget
                    let maxValueByConstraint = constraints.MaxHolding
                    let maxValue = min maxValueByBudget maxValueByConstraint
                    
                    // Check if we can afford at least one share
                    if maxValue < asset.Price then
                        // Skip this asset, try next
                        allocateGreedy rest remainingBudget allocations
                    else
                        // Calculate shares (fractional allowed)
                        let shares = maxValue / asset.Price
                        let actualValue = shares * asset.Price
                        
                        let allocation = {
                            Asset = asset
                            Shares = shares
                            Value = actualValue
                            Percentage = 0.0  // Will calculate after all allocations
                        }
                        
                        let newBudget = remainingBudget - actualValue
                        allocateGreedy rest newBudget (allocation :: allocations)
        
        // Perform greedy allocation
        let rawAllocations = allocateGreedy sortedAssets constraints.Budget []
        
        // Calculate total value
        let totalValue = rawAllocations |> List.sumBy (fun a -> a.Value)
        
        // Update percentages
        let allocations = 
            rawAllocations
            |> List.map (fun alloc -> 
                { alloc with Percentage = if totalValue > 0.0 then alloc.Value / totalValue else 0.0 })
            |> List.rev  // Reverse to maintain original order (high ratio first)
        
        // Calculate portfolio metrics
        let (expectedReturn, risk, sharpeRatio) = calculatePortfolioMetrics allocations totalValue
        
        let endTime = DateTime.UtcNow
        let elapsedMs = (endTime - startTime).TotalMilliseconds
        
        {
            Allocations = allocations
            TotalValue = totalValue
            ExpectedReturn = expectedReturn
            Risk = risk
            SharpeRatio = sharpeRatio
            ElapsedMs = elapsedMs
        }
    
    // ================================================================================
    // MEAN-VARIANCE OPTIMIZATION ALGORITHM
    // ================================================================================
    
    /// Calculate utility score for a portfolio (quadratic utility function)
    /// Utility = ExpectedReturn - (0.5 * RiskAversion * Risk^2)
    let private calculateUtility (expectedReturn: float) (risk: float) (riskAversion: float) : float =
        expectedReturn - (0.5 * riskAversion * risk * risk)
    
    /// Solve portfolio optimization using simplified mean-variance approach
    /// Uses iterative search to find allocation that maximizes utility function
    let solveMeanVariance (assets: Asset list) (constraints: Constraints) (config: PortfolioConfig) : PortfolioSolution =
        let startTime = DateTime.UtcNow
        
        // Convert risk tolerance (0-1) to risk aversion (higher tolerance = lower aversion)
        // Risk aversion = 2 * (1 - tolerance), range [0.2, 2.0]
        let riskAversion = 2.0 * (1.0 - config.RiskTolerance) + 0.2
        
        // Simplified mean-variance: Try different weighted combinations
        // Start with equal weight allocation, then adjust based on utility
        let numAssets = assets.Length
        
        // Generate candidate allocations using grid search
        let generateCandidates () =
            // Strategy: Create multiple allocation patterns
            [
                // Equal weight to all assets
                yield assets |> List.map (fun a -> (a, 1.0 / float numAssets))
                
                // Weight by return (normalized)
                let totalReturn = assets |> List.sumBy (fun a -> max 0.0 a.ExpectedReturn)
                if totalReturn > 0.0 then
                    yield assets |> List.map (fun a -> (a, (max 0.0 a.ExpectedReturn) / totalReturn))
                
                // Weight by inverse risk (normalized)
                let totalInvRisk = assets |> List.sumBy (fun a -> if a.Risk > 0.0 then 1.0 / a.Risk else 0.0)
                if totalInvRisk > 0.0 then
                    yield assets |> List.map (fun a -> 
                        let invRisk = if a.Risk > 0.0 then 1.0 / a.Risk else 0.0
                        (a, invRisk / totalInvRisk))
                
                // Weight by Sharpe ratio (normalized)
                let ratios = assets |> List.map calculateRatio
                let totalRatio = ratios |> List.sum
                if totalRatio > 0.0 then
                    yield List.zip assets ratios |> List.map (fun (a, r) -> (a, r / totalRatio))
                
                // Balanced: 50% by return, 50% by inverse risk
                if totalReturn > 0.0 && totalInvRisk > 0.0 then
                    yield assets |> List.map (fun a ->
                        let returnWeight = (max 0.0 a.ExpectedReturn) / totalReturn
                        let riskWeight = (if a.Risk > 0.0 then 1.0 / a.Risk else 0.0) / totalInvRisk
                        (a, 0.5 * returnWeight + 0.5 * riskWeight))
            ]
        
        // Convert weight allocation to actual shares within constraints
        let allocateByWeights (weights: (Asset * float) list) =
            let totalBudget = constraints.Budget
            
            // Calculate target values for each asset based on weights
            let targetAllocations =
                weights
                |> List.map (fun (asset, weight) ->
                    let targetValue = weight * totalBudget
                    let constrainedValue = min targetValue constraints.MaxHolding
                    let shares = constrainedValue / asset.Price
                    (asset, shares, shares * asset.Price))
            
            // Normalize if total exceeds budget
            let totalValue = targetAllocations |> List.sumBy (fun (_, _, v) -> v)
            
            if totalValue > totalBudget then
                // Scale down proportionally
                let scale = totalBudget / totalValue
                targetAllocations
                |> List.map (fun (asset, shares, value) ->
                    let scaledShares = shares * scale
                    let scaledValue = scaledShares * asset.Price
                    (asset, scaledShares, scaledValue))
            else
                targetAllocations
        
        // Evaluate a candidate allocation
        let evaluateCandidate (weights: (Asset * float) list) =
            let allocData = allocateByWeights weights
            let totalValue = allocData |> List.sumBy (fun (_, _, v) -> v)
            
            if totalValue = 0.0 then
                None
            else
                // Create allocations with percentages
                let allocations =
                    allocData
                    |> List.filter (fun (_, shares, _) -> shares > 0.0)
                    |> List.map (fun (asset, shares, value) ->
                        {
                            Asset = asset
                            Shares = shares
                            Value = value
                            Percentage = value / totalValue
                        })
                
                // Calculate portfolio metrics
                let (expectedReturn, risk, sharpeRatio) = calculatePortfolioMetrics allocations totalValue
                
                // Calculate utility score
                let utility = calculateUtility expectedReturn risk riskAversion
                
                Some (allocations, totalValue, expectedReturn, risk, sharpeRatio, utility)
        
        // Find best allocation among candidates
        let candidates = generateCandidates ()
        let evaluatedCandidates =
            candidates
            |> List.choose evaluateCandidate
        
        // Select candidate with highest utility
        let bestSolution =
            if evaluatedCandidates.IsEmpty then
                // Fallback: use greedy if mean-variance fails
                let greedy = solveGreedyByRatio assets constraints config
                (greedy.Allocations, greedy.TotalValue, greedy.ExpectedReturn, greedy.Risk, greedy.SharpeRatio)
            else
                let (allocations, totalValue, expectedReturn, risk, sharpeRatio, _utility) =
                    evaluatedCandidates
                    |> List.maxBy (fun (_, _, _, _, _, u) -> u)
                (allocations, totalValue, expectedReturn, risk, sharpeRatio)
        
        let (allocations, totalValue, expectedReturn, risk, sharpeRatio) = bestSolution
        
        let endTime = DateTime.UtcNow
        let elapsedMs = (endTime - startTime).TotalMilliseconds
        
        {
            Allocations = allocations
            TotalValue = totalValue
            ExpectedReturn = expectedReturn
            Risk = risk
            SharpeRatio = sharpeRatio
            ElapsedMs = elapsedMs
        }
