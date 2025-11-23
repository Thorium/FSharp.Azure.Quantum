namespace FSharp.Azure.Quantum.Classical

open System

/// Portfolio optimization solver with greedy-by-ratio and mean-variance algorithms
module PortfolioSolver =

    // ================================================================================
    // CORE TYPES
    // ================================================================================
    
    /// Represents an asset with financial characteristics
    type Asset = {
        /// Asset ticker symbol
        Symbol: string
        
        /// Expected return rate (e.g., 0.12 = 12%)
        ExpectedReturn: float
        
        /// Risk measure (standard deviation)
        Risk: float
        
        /// Current price per share
        Price: float
    }
    
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
    
    /// Validation result with detailed error messages
    type ValidationResult = {
        /// Whether validation passed
        IsValid: bool
        
        /// Error or warning messages
        Messages: string list
    }
    
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
    let validateBudgetConstraint (assets: Asset list) (constraints: Constraints) : ValidationResult =
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
        
        {
            IsValid = List.isEmpty messages
            Messages = messages
        }
    
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

