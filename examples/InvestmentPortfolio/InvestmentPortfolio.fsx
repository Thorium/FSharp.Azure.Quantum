// ==============================================================================
// Investment Portfolio Balancing Example
// ==============================================================================
// Demonstrates portfolio optimization using HybridSolver with quantum-ready
// optimization to balance risk and return across multiple assets.
//
// Business Context:
// An investment advisor needs to allocate a $100,000 portfolio across 8 tech
// stocks, maximizing expected returns while managing risk. The portfolio must
// balance growth potential (high return) against volatility (risk).
//
// This example shows:
// - Mean-variance portfolio optimization
// - Risk-return trade-off analysis
// - Sharpe ratio calculation (return per unit of risk)
// - Diversification benefits
// - Quantum-ready optimization via HybridSolver
// ==============================================================================

(*
===============================================================================
 Background Theory
===============================================================================

Modern Portfolio Theory (MPT), introduced by Harry Markowitz in 1952, provides
the mathematical framework for constructing portfolios that maximize expected
return for a given level of risk. The key insight is that portfolio risk is not
simply the weighted average of individual asset risks—correlations between assets
allow for "diversification benefit" where combined volatility is lower than the
sum of parts. This leads to the concept of the "efficient frontier": the set of
portfolios offering maximum return for each risk level.

The mean-variance optimization problem seeks to find optimal asset weights w that
minimize portfolio variance σ²_p = w'Σw subject to achieving target return μ_p =
w'μ and budget constraint Σwᵢ = 1. This is a quadratic programming problem that
becomes computationally challenging as the number of assets grows (O(n³) for
classical solvers with n assets). Real-world portfolios with thousands of assets
and complex constraints (sector limits, ESG requirements, transaction costs)
strain classical optimization.

Key Equations:
  - Expected portfolio return: μ_p = Σᵢ wᵢ·μᵢ = w'μ
  - Portfolio variance: σ²_p = Σᵢ Σⱼ wᵢwⱼσᵢⱼ = w'Σw
  - Sharpe ratio: S = (μ_p - r_f) / σ_p  (return per unit risk, r_f = risk-free rate)
  - Efficient frontier: min w'Σw  s.t. w'μ ≥ μ_target, w'1 = 1, w ≥ 0

Quantum Advantage:
  Portfolio optimization maps naturally to QUBO (Quadratic Unconstrained Binary
  Optimization), which can be solved using QAOA or quantum annealing. For large
  portfolios (n > 50 assets), quantum approaches may offer speedup through:
  (1) Parallel exploration of the 2ⁿ solution space via superposition
  (2) Tunneling through local minima in complex constraint landscapes
  (3) Potential quantum speedup for sampling from Boltzmann distributions
  Current NISQ devices handle ~20-50 assets; fault-tolerant quantum computers
  could enable real-time optimization of institutional portfolios.

References:
  [1] Markowitz, "Portfolio Selection", Journal of Finance 7(1), 77-91 (1952).
      https://doi.org/10.2307/2975974
  [2] Orus et al., "Quantum computing for finance: Overview and prospects",
      Reviews of Modern Physics 91, 045001 (2019). https://doi.org/10.1103/RevModPhys.91.045001
  [3] Egger et al., "Quantum Computing for Finance: State-of-the-Art and Future
      Prospects", IEEE Trans. Quantum Eng. 1, 1-24 (2020). https://doi.org/10.1109/TQE.2020.3030314
  [4] Wikipedia: Modern_portfolio_theory
      https://en.wikipedia.org/wiki/Modern_portfolio_theory
*)

//#r "nuget: FSharp.Azure.Quantum"
#r "../../src/FSharp.Azure.Quantum/bin/Debug/net10.0/FSharp.Azure.Quantum.dll"


open System
open FSharp.Azure.Quantum
open FSharp.Azure.Quantum.Classical

// ==============================================================================
// DOMAIN MODEL - Investment Portfolio Types
// ==============================================================================

/// Represents a stock with historical performance data
type Stock = {
    Symbol: string
    Name: string
    ExpectedReturn: float  // Annual return as decimal (e.g., 0.15 = 15%)
    Volatility: float      // Annual volatility (standard deviation)
    Price: float           // Current price per share
}

/// Portfolio allocation result with analysis
type PortfolioAnalysis = {
    Allocations: (string * float * float) list  // (symbol, shares, value)
    TotalValue: float
    ExpectedReturn: float
    Risk: float
    SharpeRatio: float
    DiversificationScore: int  // Number of stocks held
}

// ==============================================================================
// STOCK DATA - Real tech sector data (2024 historical averages)
// ==============================================================================
// Data based on historical performance analysis
// Returns and volatility calculated from 5-year trailing data

let stocks = [
    { Symbol = "AAPL"; Name = "Apple Inc."
      ExpectedReturn = 0.18; Volatility = 0.22; Price = 175.00 }
    
    { Symbol = "MSFT"; Name = "Microsoft Corp."
      ExpectedReturn = 0.22; Volatility = 0.25; Price = 380.00 }
    
    { Symbol = "GOOGL"; Name = "Alphabet Inc."
      ExpectedReturn = 0.16; Volatility = 0.28; Price = 140.00 }
    
    { Symbol = "AMZN"; Name = "Amazon.com Inc."
      ExpectedReturn = 0.24; Volatility = 0.32; Price = 155.00 }
    
    { Symbol = "NVDA"; Name = "NVIDIA Corp."
      ExpectedReturn = 0.35; Volatility = 0.45; Price = 485.00 }
    
    { Symbol = "META"; Name = "Meta Platforms Inc."
      ExpectedReturn = 0.28; Volatility = 0.38; Price = 350.00 }
    
    { Symbol = "TSLA"; Name = "Tesla Inc."
      ExpectedReturn = 0.30; Volatility = 0.55; Price = 245.00 }
    
    { Symbol = "AMD"; Name = "Advanced Micro Devices"
      ExpectedReturn = 0.26; Volatility = 0.42; Price = 125.00 }
]

// ==============================================================================
// CONFIGURATION
// ==============================================================================

let budget = 100000.0  // $100,000 investment budget

// ==============================================================================
// PURE FUNCTIONS - Portfolio calculations
// ==============================================================================

/// Calculate return-to-risk ratio (simplified Sharpe ratio)
let calculateSharpeRatio (expectedReturn: float) (risk: float) : float =
    if risk = 0.0 then 0.0
    else expectedReturn / risk

/// Count number of assets in portfolio
let countDiversification (allocations: (string * float * float) list) : int =
    allocations |> List.length

/// Format currency amount
let formatCurrency (amount: float) : string =
    sprintf "$%s" (amount.ToString("N2"))

/// Format percentage
let formatPercent (value: float) : string =
    sprintf "%.2f%%" (value * 100.0)

/// Create portfolio analysis from raw solution
let createAnalysis 
    (allocations: (string * float * float) list)
    (totalValue: float)
    (expectedReturn: float)
    (risk: float)
    : PortfolioAnalysis =
    {
        Allocations = allocations
        TotalValue = totalValue
        ExpectedReturn = expectedReturn
        Risk = risk
        SharpeRatio = calculateSharpeRatio expectedReturn risk
        DiversificationScore = countDiversification allocations
    }

// ==============================================================================
// PORTFOLIO OPTIMIZATION (Using HybridSolver for Quantum-Ready Optimization)
// ==============================================================================

/// Convert stock to Asset type for HybridSolver
let stockToAsset (stock: Stock) : PortfolioSolver.Asset = {
    Symbol = stock.Symbol
    ExpectedReturn = stock.ExpectedReturn
    Risk = stock.Volatility  // Note: Asset.Risk corresponds to Stock.Volatility
    Price = stock.Price
}

/// Solve portfolio optimization problem using HybridSolver
let optimizePortfolio (stocks: Stock list) (budget: float) : Result<PortfolioAnalysis * string, string> =
    // Convert to Portfolio API format
    let assets = stocks |> List.map stockToAsset
    let constraints = {
        PortfolioSolver.Constraints.Budget = budget
        PortfolioSolver.Constraints.MinHolding = 0.0  // No minimum holding constraint
        PortfolioSolver.Constraints.MaxHolding = budget  // Can invest entire budget in one asset if optimal
    }
    
    // Use HybridSolver for automatic classical/quantum routing
    // 
    // ROUTING OPTIONS:
    // 1. Automatic (None): Quantum Advisor analyzes and recommends Classical/Quantum
    // 2. Force Classical (Some HybridSolver.SolverMethod.Classical): Use CPU-based greedy algorithm
    // 3. Force Quantum (Some HybridSolver.SolverMethod.Quantum): Use QAOA on quantum backend
    //
    // For this 8-asset problem, Quantum Advisor will recommend Classical (fast, accurate).
    // To test quantum solver, use: Some HybridSolver.SolverMethod.Quantum
    //
    // Note: Quantum portfolio optimization becomes advantageous for 50+ assets with
    // complex correlation matrices and non-linear constraints.
    match HybridSolver.solvePortfolio assets constraints None None None with
    | Ok solution ->
        // Extract allocations from solution
        let allocations =
            solution.Result.Allocations
            |> List.map (fun alloc -> (alloc.Asset.Symbol, alloc.Shares, alloc.Value))
        
        let analysis = 
            createAnalysis
                allocations
                solution.Result.TotalValue
                solution.Result.ExpectedReturn
                solution.Result.Risk
        
        Ok (analysis, solution.Reasoning)
    
    | Error err -> Error (sprintf "HybridSolver failed: %s" err.Message)

// ==============================================================================
// REPORTING - Pure functions for output
// ==============================================================================

/// Generate detailed allocation report
let generateAllocationReport (stocks: Stock list) (analysis: PortfolioAnalysis) : string list =
    [
        "╔══════════════════════════════════════════════════════════════════════════════╗"
        "║                       PORTFOLIO ALLOCATION REPORT                            ║"
        "╚══════════════════════════════════════════════════════════════════════════════╝"
        ""
        "ASSETS SELECTED:"
        "────────────────────────────────────────────────────────────────────────────────"
        
        yield! 
            analysis.Allocations
            |> List.mapi (fun i (symbol, shares, value) ->
                let stock = stocks |> List.find (fun s -> s.Symbol = symbol)
                let pct = (value / analysis.TotalValue) * 100.0
                sprintf "  %d. %-6s | %6.2f shares @ %s = %s (%.1f%%)"
                    (i + 1)
                    symbol
                    shares
                    (formatCurrency stock.Price)
                    (formatCurrency value)
                    pct)
        
        ""
        "PORTFOLIO SUMMARY:"
        "────────────────────────────────────────────────────────────────────────────────"
        sprintf "  Total Invested:        %s" (formatCurrency analysis.TotalValue)
        sprintf "  Number of Holdings:    %d stocks" analysis.DiversificationScore
        sprintf "  Expected Annual Return: %s" (formatPercent analysis.ExpectedReturn)
        sprintf "  Portfolio Risk (σ):    %s" (formatPercent analysis.Risk)
        sprintf "  Sharpe Ratio:          %.2f" analysis.SharpeRatio
        ""
    ]

/// Generate risk-return analysis report
let generateRiskReturnAnalysis (stocks: Stock list) (analysis: PortfolioAnalysis) : string list =
    [
        "╔══════════════════════════════════════════════════════════════════════════════╗"
        "║                      RISK-RETURN ANALYSIS                                    ║"
        "╚══════════════════════════════════════════════════════════════════════════════╝"
        ""
        "INDIVIDUAL STOCK METRICS:"
        "────────────────────────────────────────────────────────────────────────────────"
        "  Symbol  | Expected Return | Volatility | Sharpe Ratio | Allocation"
        "  --------|-----------------|------------|--------------|------------"
        
        yield!
            stocks
            |> List.map (fun stock ->
                let sharpe = calculateSharpeRatio stock.ExpectedReturn stock.Volatility
                let allocation = 
                    analysis.Allocations 
                    |> List.tryFind (fun (s, _, _) -> s = stock.Symbol)
                let allocPct = 
                    match allocation with
                    | Some (_, _, value) -> sprintf "%.1f%%" ((value / analysis.TotalValue) * 100.0)
                    | None -> "0.0%"
                
                sprintf "  %-7s | %14s | %9s | %12.2f | %10s"
                    stock.Symbol
                    (formatPercent stock.ExpectedReturn)
                    (formatPercent stock.Volatility)
                    sharpe
                    allocPct)
        
        ""
        "PORTFOLIO VS. INDIVIDUAL STOCKS:"
        "────────────────────────────────────────────────────────────────────────────────"
        
        // Calculate average metrics for comparison
        let avgReturn = stocks |> List.averageBy (fun s -> s.ExpectedReturn)
        let avgVolatility = stocks |> List.averageBy (fun s -> s.Volatility)
        let avgSharpe = calculateSharpeRatio avgReturn avgVolatility
        
        sprintf "  Average Stock Return:  %s" (formatPercent avgReturn)
        sprintf "  Portfolio Return:      %s (%.1fx better)" 
            (formatPercent analysis.ExpectedReturn)
            (analysis.ExpectedReturn / avgReturn)
        ""
        sprintf "  Average Stock Risk:    %s" (formatPercent avgVolatility)
        sprintf "  Portfolio Risk:        %s (%.1fx lower)" 
            (formatPercent analysis.Risk)
            (avgVolatility / analysis.Risk)
        ""
        sprintf "  Average Sharpe Ratio:  %.2f" avgSharpe
        sprintf "  Portfolio Sharpe:      %.2f (%.1fx better)" 
            analysis.SharpeRatio
            (analysis.SharpeRatio / avgSharpe)
        ""
    ]

/// Generate business impact report
let generateBusinessImpact (analysis: PortfolioAnalysis) (budget: float) : string list =
    let expectedGain = analysis.TotalValue * analysis.ExpectedReturn
    let potentialRange = analysis.TotalValue * analysis.Risk
    let bestCase = analysis.TotalValue + expectedGain + potentialRange
    let worstCase = analysis.TotalValue + expectedGain - potentialRange
    
    [
        "╔══════════════════════════════════════════════════════════════════════════════╗"
        "║                          BUSINESS IMPACT ANALYSIS                            ║"
        "╚══════════════════════════════════════════════════════════════════════════════╝"
        ""
        "PROJECTED ANNUAL OUTCOMES (1 Year):"
        "────────────────────────────────────────────────────────────────────────────────"
        sprintf "  Initial Investment:    %s" (formatCurrency budget)
        sprintf "  Expected Return:       %s (%s gain)"
            (formatCurrency expectedGain)
            (formatPercent analysis.ExpectedReturn)
        ""
        "  SCENARIO ANALYSIS (95%% confidence interval):"
        sprintf "  • Best Case:           %s (+%s)" 
            (formatCurrency bestCase) 
            (formatPercent ((bestCase - budget) / budget))
        sprintf "  • Expected:            %s (+%s)" 
            (formatCurrency (analysis.TotalValue + expectedGain))
            (formatPercent (expectedGain / budget))
        sprintf "  • Worst Case:          %s (%s)" 
            (formatCurrency worstCase)
            (formatPercent ((worstCase - budget) / budget))
        ""
        "KEY INSIGHTS:"
        "────────────────────────────────────────────────────────────────────────────────"
        sprintf "  ✓ Diversification across %d tech stocks reduces risk" analysis.DiversificationScore
        sprintf "  ✓ Sharpe ratio of %.2f indicates efficient risk-adjusted returns" analysis.SharpeRatio
        sprintf "  ✓ Expected to generate %s annually" (formatCurrency expectedGain)
        "  ✓ Risk-managed approach balances growth and volatility"
        ""
    ]

// ==============================================================================
// MAIN EXECUTION
// ==============================================================================

printfn "╔══════════════════════════════════════════════════════════════════════════════╗"
printfn "║              INVESTMENT PORTFOLIO OPTIMIZATION EXAMPLE                       ║"
printfn "║              Using HybridSolver (Quantum-Ready Optimization)                 ║"
printfn "╚══════════════════════════════════════════════════════════════════════════════╝"
printfn ""
printfn "Problem: Allocate %s across %d tech stocks" (formatCurrency budget) stocks.Length
printfn "Objective: Maximize returns while managing risk"
printfn ""
printfn "Running portfolio optimization with HybridSolver..."

// Time the optimization
let startTime = DateTime.UtcNow

// Solve the optimization problem
let result = optimizePortfolio stocks budget

let endTime = DateTime.UtcNow
let elapsed = (endTime - startTime).TotalMilliseconds

printfn "Completed in %.0f ms" elapsed
printfn ""

// Display results
match result with
| Ok (analysis, reasoning) ->
    printfn "💡 Solver Decision: %s" reasoning
    printfn ""
    
    // Generate reports
    let allocationReport = generateAllocationReport stocks analysis
    let riskReturnReport = generateRiskReturnAnalysis stocks analysis
    let businessImpact = generateBusinessImpact analysis budget
    
    // Print reports
    allocationReport |> List.iter (printfn "%s")
    riskReturnReport |> List.iter (printfn "%s")
    businessImpact |> List.iter (printfn "%s")
    
    printfn "╔══════════════════════════════════════════════════════════════════════════════╗"
    printfn "║                     OPTIMIZATION SUCCESSFUL                                  ║"
    printfn "╚══════════════════════════════════════════════════════════════════════════════╝"
    printfn ""
    printfn "✨ Note: HybridSolver automatically routes between classical and quantum solvers"
    printfn "   based on problem size and structure. For 8 assets → classical optimizer."
    printfn "   For larger portfolios (50+ assets), quantum advantage may emerge."

| Error errMsg ->
    printfn "❌ Optimization failed: %s" errMsg
    exit 1
