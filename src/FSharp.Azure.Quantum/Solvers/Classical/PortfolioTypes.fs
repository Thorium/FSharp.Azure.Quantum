namespace FSharp.Azure.Quantum

/// <summary>
/// Shared types for Portfolio optimization across both Classical and Quantum solvers
/// </summary>
module PortfolioTypes =

    /// <summary>
    /// Represents a financial asset with characteristics for portfolio optimization
    /// </summary>
    type Asset = {
        /// Stock ticker symbol (e.g., "AAPL", "GOOGL")
        Symbol: string
        
        /// Expected annual return rate as decimal (e.g., 0.12 = 12%)
        ExpectedReturn: float
        
        /// Risk measure (standard deviation) as decimal
        Risk: float
        
        /// Current price per share
        Price: float
    }
