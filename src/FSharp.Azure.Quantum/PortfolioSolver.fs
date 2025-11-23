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
